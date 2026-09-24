using System.Net;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class SdvnTests
{
    internal static string Cookie(DateTimeOffset? expiry = null) => "header." + Convert.ToBase64String(
        JsonSerializer.SerializeToUtf8Bytes(new { exp = (expiry ?? DateTimeOffset.UtcNow.AddDays(1)).ToUnixTimeSeconds() }))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";
    private const string Page = "<h1>Rex</h1><span>Online</span><span>Growth</span><span>50%</span><span>Health</span><span>40 / 100</span><span>Stamina</span><span>5 / 10</span>";

    [Fact]
    public void CatalogHasThreeEnabledHostsAndNoGuessedFourthSlug()
    {
        Assert.Equal(new[] { "1.sdvn.org", "2.sdvn.org", "3.sdvn.org" }, SdvnTenant.Available.Select(t => t.Host));
        Assert.Equal(new[] { "sdvn", "sdvn2234234", "sdvn3123123123" }, SdvnTenant.Available.Select(t => t.ServerSlug));
        var fourth = SdvnTenant.All[3];
        Assert.Null(fourth.ServerSlug); Assert.False(fourth.Enabled);
        Assert.Throws<InvalidOperationException>(() => new SdvnClient(fourth, Cookie()));
    }

    [Fact]
    public async Task DpapiCredentialsAreBoundToTenantEvenIfFileIsCopied()
    {
        var root = Path.Combine(Path.GetTempPath(), "isle-sdvn-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SdvnCredentialStore(root);
            var a = SdvnTenant.All[0]; var b = SdvnTenant.All[1]; var cookie = Cookie();
            await store.SaveAsync(a, cookie);
            Assert.Equal(cookie, await store.LoadAsync(a)); Assert.Null(await store.LoadAsync(b));
            var file = Path.Combine(root, a.Id + ".credential");
            Assert.DoesNotContain(cookie, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file)));
            File.Copy(file, Path.Combine(root, b.Id + ".credential"));
            Assert.Null(await store.LoadAsync(b)); Assert.Equal(cookie, await store.LoadAsync(a));
            store.Remember(a); Assert.Equal(a, store.LastTenant);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(a, Cookie(DateTimeOffset.UtcNow.AddDays(-1))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task CallsOnlySelectedHostAndSlug(int index)
    {
        var requests = new List<Uri>(); var cookie = Cookie(); var tenant = SdvnTenant.All[index];
        using var client = new SdvnClient(tenant, cookie, new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            Assert.Equal("islepilot_player=" + cookie, request.Headers.GetValues("Cookie").Single());
            return Task.FromResult(Response(request.RequestUri!.AbsolutePath == "/me" ? Page : """{"ok":true,"markers":[]}"""));
        }));
        await client.ValidateAsync();
        Assert.All(requests, uri => Assert.Equal(tenant.Host, uri.Host));
        Assert.Contains(requests, uri => uri.AbsolutePath == "/api/p/" + tenant.ServerSlug + "/map/markers");
        var player = await client.GetPlayerAsync();
        Assert.Equal(5, player.Stamina); Assert.Equal(10, player.MaxStamina);
    }

    [Fact]
    public async Task RejectsRedirectAndHtmlLoginInsteadOfSavingAnonymousCookie()
    {
        using var redirect = new SdvnClient(SdvnTenant.All[0], Cookie(), new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        { Headers = { Location = new Uri("https://2.sdvn.org/me") } })));
        await Assert.ThrowsAsync<InvalidDataException>(() => redirect.GetPlayerAsync());
        using var login = new SdvnClient(SdvnTenant.All[0], Cookie(), new Handler((_, _) => Task.FromResult(Response("Sign in with Steam to see your current dinosaur"))));
        await Assert.ThrowsAsync<IslePilotAuthenticationException>(() => login.GetPlayerAsync());
    }

    [Fact]
    public async Task AuthenticatedNoDinoIsNotAnExpiredSession()
    {
        using var client = new SdvnClient(SdvnTenant.All[0], Cookie(), new Handler((_, _) => Task.FromResult(Response(
            """<a href="/api/player/steam/logout">Sign out</a><h1>My dinosaur</h1><span>Offline</span>"""))));
        var player = await client.GetPlayerAsync();
        Assert.Null(player.Species);
        Assert.False(player.Online);
    }

    [Fact]
    public async Task ScriptsCannotCauseFalseAuthenticationFailure()
    {
        using var client = new SdvnClient(SdvnTenant.All[0], Cookie(), new Handler((_, _) => Task.FromResult(Response(
            Page + """<script>var text='<a href="/api/player/steam/login?x=1">Login</a>';</script>"""))));
        Assert.Equal("Rex", (await client.GetPlayerAsync()).Species);
    }

    [Fact]
    public async Task AuthenticationErrorIsDeliveredAndInvalidatesOnlyOnce()
    {
        var invalidations = 0;
        using var client = new SdvnClient(SdvnTenant.All[0], Cookie(), new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        await using var session = new SdvnTelemetrySession(client, () => Interlocked.Increment(ref invalidations));
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var reader = session.WatchAsync(ct.Token).GetAsyncEnumerator(ct.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(TheIsleOverlay.Core.TelemetrySessionState.AuthenticationRequired, reader.Current.SessionState);
        Assert.False(await reader.MoveNextAsync());
        Assert.Equal(1, invalidations);
    }

    [Fact]
    public void MalformedJwtShapesDoNotThrow()
    {
        foreach (var json in new[] { "[]", "null", "42", "{\"exp\":\"tomorrow\"}" })
        {
            var cookie = "header." + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=') + ".signature";
            Assert.False(SdvnCredentialStore.IsUsableCookie(cookie, DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task SlowStatsDoNotBlockSelfMarkerAndNeverUseOtherPlayer()
    {
        using var client = new SdvnClient(SdvnTenant.All[0], Cookie(), new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me")
            { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Response(Page); }
            return Response("""{"ok":true,"markers":[{"self":false,"x":999,"y":999},{"self":true,"x":12,"y":34,"yaw":90}]}""");
        }));
        await using var session = new SdvnTelemetrySession(client);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var reader = session.WatchAsync(ct.Token).GetAsyncEnumerator(ct.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(12, reader.Current.Player!.Location!.X);
        Assert.NotNull(reader.Current.Player.ExactMapHeadingDegrees);
        Assert.Null(reader.Current.Player.ExactVitals);
        Assert.True(reader.Current.LiveDataStale);
    }

    [Theory]
    [InlineData("bad")][InlineData("islepilot_player=abc")][InlineData("foo\r\nbar")]
    public void InvalidCookieRejected(string cookie) => Assert.False(SdvnCredentialStore.IsUsableCookie(cookie, DateTimeOffset.UtcNow));

    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { var response = await action(request, ct); response.RequestMessage = request; return response; }
    }
}
