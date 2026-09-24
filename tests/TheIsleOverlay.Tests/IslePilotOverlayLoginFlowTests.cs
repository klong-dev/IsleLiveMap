using System.Net;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayLoginFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "isle-login-tests", Guid.NewGuid().ToString("N"));
    private IslePilotCredentialStore Store => new(Path.Combine(_directory, "islepilot.credential"));
    private static IslePilotOverlayAuthResult Credentials(string token = "fixture-token") =>
        new("76561198000000000", token, "fixture-cookie");

    [Fact]
    public async Task MissingCredentialsPromptsLoginAndPersistsForNextLaunch()
    {
        using var http = new HttpClient(new Handler(HttpStatusCode.OK, HttpStatusCode.OK));
        var flow = new IslePilotOverlayLoginFlow(http, Store);
        var prompts = 0;
        Task<IslePilotOverlayAuthResult?> Login(CancellationToken _)
        { prompts++; return Task.FromResult<IslePilotOverlayAuthResult?>(Credentials()); }
        Assert.Equal(Credentials(), await flow.ResolveAsync(Login));
        Assert.Equal(Credentials(), await Store.LoadAsync());
        Assert.Equal(Credentials(), await flow.ResolveAsync(Login));
        Assert.Equal(1, prompts);
        Assert.DoesNotContain("fixture-token", await File.ReadAllTextAsync(Path.Combine(_directory, "islepilot.credential")));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ExpiredSavedLoginIsReplacedBeforeOverlayIsOpened(HttpStatusCode rejected)
    {
        await Store.SaveAsync(Credentials("expired"));
        using var http = new HttpClient(new Handler(rejected, HttpStatusCode.OK));
        var flow = new IslePilotOverlayLoginFlow(http, Store);
        var result = await flow.ResolveAsync(async _ =>
        {
            Assert.Null(await Store.LoadAsync());
            return Credentials("renewed");
        });
        Assert.Equal(Credentials("renewed"), result);
        Assert.Equal(result, await Store.LoadAsync());
    }

    [Fact]
    public async Task TransientFailurePreservesSavedLoginWithoutPrompt()
    {
        await Store.SaveAsync(Credentials());
        using var http = new HttpClient(new Handler(HttpStatusCode.ServiceUnavailable));
        var result = await new IslePilotOverlayLoginFlow(http, Store).ResolveAsync(_ =>
            throw new InvalidOperationException("Must not prompt for a network failure"));
        Assert.Equal(Credentials(), result);
        Assert.Equal(result, await Store.LoadAsync());
    }

    [Fact]
    public async Task RejectedNewLoginIsNotSaved()
    {
        using var http = new HttpClient(new Handler(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<IslePilotOverlayAuthenticationException>(() =>
            new IslePilotOverlayLoginFlow(http, Store).ResolveAsync(_ => Task.FromResult<IslePilotOverlayAuthResult?>(Credentials())));
        Assert.Null(await Store.LoadAsync());
    }

    [Fact]
    public async Task CancelOrExplicitLocalOnlyReturnsNoProviderAndDoesNotSave()
    {
        using var http = new HttpClient(new Handler());
        Assert.Null(await new IslePilotOverlayLoginFlow(http, Store).ResolveAsync(_ => Task.FromResult<IslePilotOverlayAuthResult?>(null)));
        Assert.Null(await Store.LoadAsync());
    }

    [Fact]
    public async Task ClosingHomeDuringLoginDoesNotSaveOrLaunch()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new IslePilotOverlayLoginFlow(http, Store).ResolveAsync(_ =>
            {
                cancellation.Cancel();
                return Task.FromResult<IslePilotOverlayAuthResult?>(Credentials());
            }, cancellationToken: cancellation.Token));
        Assert.Null(await Store.LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class Handler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://islepilot.eu/api/overlay/me", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(_statuses.Dequeue())
            { Content = new StringContent("{\"hasData\":true,\"online\":true}") });
        }
    }
}
