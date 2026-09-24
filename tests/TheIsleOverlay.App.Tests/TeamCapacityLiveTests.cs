using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheIsleOverlay.ProClient;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App.Tests;

// Opt-in: production REST + SignalR through the same client as the desktop app.
// No credentials are logged or persisted. Only disposable QA rooms are touched.
public sealed class TeamCapacityLiveTests
{
    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public async Task RealClientsRespectSelectedCapacityAndShareTelemetry()
    {
        if (Environment.GetEnvironmentVariable("ISLE_TEAM_CAPACITY_LIVE") != "1") return;
        var uri = new Uri(Environment.GetEnvironmentVariable("ISLE_TEAM_CAPACITY_URL") ?? TeamRelayClient.DefaultBaseUri.AbsoluteUri);
        Assert.Contains(uri.Host, new[] { "isle-relay.klong.dev", "localhost", "127.0.0.1" });
        var store = new ProCredentialStore(new ProClientOptions().CredentialPath);
        var credential = await store.LoadAsync();
        Assert.NotNull(credential);
        Assert.True(credential.HasUsableOfflineLicense(DateTimeOffset.UtcNow), "A valid existing Pro session is needed for live capacity QA.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        foreach (var size in new[] { 3, 7, 10, 21 })
        {
            var clients = new List<TeamRelayClient>();
            try
            {
                var tier = size > 7 ? TeamAccessTier.Pro : TeamAccessTier.Free;
                var owner = new TeamRelayClient(uri); clients.Add(owner);
                owner.ConfigureAccess(tier, credential.OfflineLicenseToken);
                var session = await owner.CreateAsync($"Capacity QA {size}", tier, size, timeout.Token);
                Assert.Equal(size, session.MaxMembers);
                for (var index = 1; index < size; index++)
                {
                    var peer = new TeamRelayClient(uri); clients.Add(peer);
                    // Test clients must obey production rate limits, not disable them.
                    var joined = false;
                    while (!joined)
                    {
                        try
                        {
                            var result = await peer.JoinAsync(session.InviteCode, $"QA peer {index}", timeout.Token);
                            Assert.Equal(size, result.MaxMembers); joined = true;
                        }
                        catch (TeamRelayApiException e) when (e.StatusCode == (int)HttpStatusCode.TooManyRequests)
                        { await Task.Delay(TimeSpan.FromSeconds(5), timeout.Token); }
                    }
                }
                using var overflow = await PostWithRateLimitAsync(http, new Uri(uri, "api/v1/teams/join"),
                    new { session.InviteCode, displayName = "QA overflow", tier = 1, entitlementProof = credential.OfflineLicenseToken }, timeout.Token);
                Assert.Equal(HttpStatusCode.Conflict, overflow.StatusCode);
                Assert.Contains("team_full", await overflow.Content.ReadAsStringAsync(timeout.Token));
                foreach (var client in clients)
                    Assert.Equal(size, client.CurrentState.Session!.MaxMembers);

                Assert.True(await owner.PublishTelemetryAsync(new TeamTelemetryUpdate
                { Sequence = 1, Source = "capacity-qa", ServerKey = "qa", MapId = "gateway", HealthPercent = 71, WorldX = 1, WorldY = 2 }, timeout.Token));
                await WaitUntil(() => clients.Skip(1).All(c => c.CurrentState.Members.Any(m => m.MemberId == session.MemberId && m.Telemetry?.HealthPercent == 71)), timeout.Token);
                var peerClient = clients[^1];
                var peerId = peerClient.CurrentState.Session!.MemberId;
                Assert.True(await peerClient.PublishTelemetryAsync(new TeamTelemetryUpdate { Sequence = 1, HealthPercent = 66, ServerKey = "qa" }, timeout.Token));
                await WaitUntil(() => owner.CurrentState.Members.Any(m => m.MemberId == peerId && m.Telemetry?.HealthPercent == 66), timeout.Token);

                var ping = await owner.UpsertMapPingAsync(new TeamMapPingMutation
                { PingId = Guid.NewGuid(), MapId = "gateway", MapLeft = .5, MapTop = .5, WorldX = 1, WorldY = 2 }, timeout.Token);
                await WaitUntil(() => clients.All(c => c.CurrentState.MapPings.Any(p => p.PingId == ping.PingId)), timeout.Token);
                await Assert.ThrowsAsync<TeamMapPingException>(() => peerClient.DeleteMapPingAsync(ping.PingId, ping.Revision, timeout.Token));
                await owner.DeleteMapPingAsync(ping.PingId, ping.Revision, timeout.Token);
                await peerClient.LeaveAsync(timeout.Token);
                await WaitUntil(() => owner.CurrentState.Members.Count == size - 1, timeout.Token);
                // Rejoin a freed seat; its limit must remain the original creator choice.
                var replacement = await PostWithRateLimitAsync(http, new Uri(uri, "api/v1/teams/join"), new { session.InviteCode, displayName = "QA replacement" }, timeout.Token);
                using (replacement)
                {
                    Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
                    var replacementSession = (await replacement.Content.ReadFromJsonAsync<TeamSession>(timeout.Token))!;
                    Assert.Equal(size, replacementSession.MaxMembers);
                    using var delete = new HttpRequestMessage(HttpMethod.Delete, new Uri(uri, "api/v1/teams/me"));
                    delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", replacementSession.MemberToken);
                    using var deleted = await http.SendAsync(delete, timeout.Token); Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
                }
                Console.WriteLine($"LIVE PASS: capacity={size}; full-room rejection, bidirectional telemetry, ping ownership, leave/rejoin.");
            }
            finally { foreach (var client in clients.AsEnumerable().Reverse()) await client.DisposeAsync(); }
        }
        foreach (var proof in new[] { (string?)null, "forged.invalid.token" })
        {
            using var response = await PostWithRateLimitAsync(http, new Uri(uri, "api/v1/teams"), new { displayName = "QA denied", tier = 1, requestedMaxMembers = 21, entitlementProof = proof }, timeout.Token);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> PostWithRateLimitAsync(HttpClient http, Uri uri, object body, CancellationToken ct)
    {
        while (true)
        {
            var response = await http.PostAsJsonAsync(uri, body, ct);
            if (response.StatusCode != HttpStatusCode.TooManyRequests) return response;
            response.Dispose(); await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
    private static async Task WaitUntil(Func<bool> condition, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        while (!condition()) await Task.Delay(50, deadline.Token);
    }
}
