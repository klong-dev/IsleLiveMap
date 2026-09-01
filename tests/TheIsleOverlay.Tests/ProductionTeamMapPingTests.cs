using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.Tests;

public sealed class ProductionTeamMapPingTests
{
    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public async Task TwoClientsSharePingWhileRelayEnforcesOwnerOnlyMutation()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ISLELIVEMAP_RUN_PRODUCTION_RELAY_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        await using var owner = new TeamRelayClient();
        await using var peer = new TeamRelayClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ownerSession = await owner.CreateAsync("Integration Owner", timeout.Token);
        await peer.JoinAsync(ownerSession.InviteCode, "Integration Peer", timeout.Token);
        var pingId = Guid.NewGuid();
        var peerSawPing = WaitForStateAsync(
            peer,
            state => state.MapPings.Any(ping => ping.PingId == pingId),
            timeout.Token);

        var created = await owner.UpsertMapPingAsync(new TeamMapPingMutation
        {
            PingId = pingId,
            MapId = "gateway",
            Kind = 1,
            MapLeft = 0.42,
            MapTop = 0.31,
            WorldX = 77_761.41,
            WorldY = -235_882.81
        }, timeout.Token);
        var peerState = await peerSawPing;
        var shared = Assert.Single(peerState.MapPings, ping => ping.PingId == pingId);
        Assert.Equal(ownerSession.MemberId, shared.OwnerMemberId);
        Assert.Equal(1, shared.Revision);

        var forbidden = await Assert.ThrowsAsync<TeamMapPingException>(() =>
            peer.UpsertMapPingAsync(new TeamMapPingMutation
            {
                PingId = pingId,
                ExpectedRevision = created.Revision,
                MapId = created.MapId,
                Kind = 7,
                MapLeft = created.MapLeft,
                MapTop = created.MapTop,
                WorldX = created.WorldX,
                WorldY = created.WorldY
            }, timeout.Token));
        Assert.Equal("ping_not_owned", forbidden.Code);

        var peerSawDelete = WaitForStateAsync(
            peer,
            state => state.MapPings.All(ping => ping.PingId != pingId),
            timeout.Token);
        await owner.DeleteMapPingAsync(pingId, created.Revision, timeout.Token);
        Assert.Empty((await peerSawDelete).MapPings);
    }

    private static Task<TeamRelayState> WaitForStateAsync(
        TeamRelayClient client,
        Func<TeamRelayState, bool> predicate,
        CancellationToken cancellationToken)
    {
        if (predicate(client.CurrentState))
        {
            return Task.FromResult(client.CurrentState);
        }

        var completion = new TaskCompletionSource<TeamRelayState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<TeamRelayState>? handler = null;
        handler = (_, state) =>
        {
            if (!predicate(state))
            {
                return;
            }

            client.StateChanged -= handler;
            completion.TrySetResult(state);
        };
        client.StateChanged += handler;
        cancellationToken.Register(() =>
        {
            client.StateChanged -= handler;
            completion.TrySetCanceled(cancellationToken);
        });
        return completion.Task;
    }
}
