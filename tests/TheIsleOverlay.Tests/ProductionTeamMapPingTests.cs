using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.Tests;

public sealed class ProductionTeamMapPingTests
{
    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public async Task EveryMemberReceivesEveryOtherMembersTelemetry()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ISLELIVEMAP_RUN_PRODUCTION_RELAY_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        await using var alpha = new TeamRelayClient();
        await using var bravo = new TeamRelayClient();
        await using var charlie = new TeamRelayClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var alphaSession = await alpha.CreateAsync("Matrix Alpha", timeout.Token);
        var bravoSession = await bravo.JoinAsync(alphaSession.InviteCode, "Matrix Bravo", timeout.Token);
        var charlieSession = await charlie.JoinAsync(alphaSession.InviteCode, "Matrix Charlie", timeout.Token);
        var clients = new[] { alpha, bravo, charlie };
        var sessions = new[] { alphaSession, bravoSession, charlieSession };

        for (var index = 0; index < clients.Length; index++)
        {
            var accepted = await clients[index].PublishTelemetryAsync(new TeamTelemetryUpdate
            {
                Sequence = 1,
                Source = "matrix-test",
                ServerKey = "115.72.226.156:7777",
                ServerEndpoint = "115.72.226.156:7777",
                ServerName = "Matrix Gateway",
                MapId = "gateway",
                Species = $"Species {index + 1}",
                HealthPercent = 90 - index,
                HungerPercent = 60 - index,
                ThirstPercent = 70 - index,
                MapLeft = 0.4 + index * 0.05,
                MapTop = 0.5 + index * 0.05,
                WorldX = 10_000 + index,
                WorldY = -20_000 - index,
                HeadingDegrees = index * 45
            }, timeout.Token);
            Assert.True(accepted);
        }

        for (var viewer = 0; viewer < clients.Length; viewer++)
        {
            var expectedPeers = sessions
                .Where(session => session.MemberId != sessions[viewer].MemberId)
                .Select(session => session.MemberId)
                .ToHashSet();
            var state = await WaitForStateAsync(
                clients[viewer],
                candidate => expectedPeers.All(memberId =>
                    candidate.Members.Any(member =>
                        member.MemberId == memberId
                        && member.Telemetry?.Sequence == 1)),
                timeout.Token);
            Assert.Equal(3, state.Members.Count);
            Assert.All(expectedPeers, memberId => Assert.Contains(
                state.Members,
                member => member.MemberId == memberId && member.Telemetry is not null));
        }
    }

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
