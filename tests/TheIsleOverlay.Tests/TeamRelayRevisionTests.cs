using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.Tests;

public sealed class TeamRelayRevisionTests
{
    [Fact]
    public async Task InterleavedMemberDeltasDoNotLoseAnEarlierRevisionFromAnotherMember()
    {
        await using var client = new TeamRelayClient();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var first = Member(firstId, 8, 1, now);
        var second = Member(secondId, 8, 1, now);
        client.ReceiveSnapshot(new TeamSnapshot(Guid.NewGuid(), "ABC123", [first, second], [], 8));

        client.MemberUpdated(Member(secondId, 10, 2, now.AddSeconds(2)));
        client.MemberUpdated(Member(firstId, 9, 2, now.AddSeconds(1)));

        Assert.Equal(2, client.CurrentState.Members.Count);
        Assert.Equal(2, client.CurrentState.Members.Single(member => member.MemberId == firstId).Telemetry?.Sequence);
        Assert.Equal(2, client.CurrentState.Members.Single(member => member.MemberId == secondId).Telemetry?.Sequence);

        client.MemberUpdated(Member(firstId, 8, 1, now));
        client.ReceiveSnapshot(new TeamSnapshot(Guid.NewGuid(), "ABC123", [first, second], [], 8));
        Assert.Equal(2, client.CurrentState.Members.Single(member => member.MemberId == firstId).Telemetry?.Sequence);
    }

    [Fact]
    public async Task RemovalRevisionPreventsALateDeltaFromResurrectingMember()
    {
        await using var client = new TeamRelayClient();
        var memberId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        client.ReceiveSnapshot(new TeamSnapshot(
            Guid.NewGuid(), "ABC123", [Member(memberId, 5, 1, now)], [], 5));

        client.MemberRemoved(new TeamMemberRemoval(memberId, 7));
        client.MemberUpdated(Member(memberId, 6, 2, now.AddSeconds(1)));

        Assert.Empty(client.CurrentState.Members);
        Assert.Equal(7, client.CurrentState.StateRevision);
    }

    [Fact]
    public async Task VersionedSnapshotRejectsLateRemovalAndItsLegacyEcho()
    {
        await using var client = new TeamRelayClient();
        var memberId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        client.ReceiveSnapshot(new TeamSnapshot(
            Guid.NewGuid(), "ABC123", [Member(memberId, 12, 3, now)], [], 12));

        client.MemberRemoved(new TeamMemberRemoval(memberId, 11));
        client.MemberRemoved(memberId);

        Assert.Single(client.CurrentState.Members);
        Assert.Equal(12, client.CurrentState.StateRevision);
    }

    [Fact]
    public async Task VersionedPingBatchRejectsAnOlderListAndIgnoresLegacyEcho()
    {
        await using var client = new TeamRelayClient();
        var newer = Ping(revision: 2);
        var older = Ping(revision: 1);

        client.MapPingsChanged(new TeamMapPingBatch([newer], 9));
        client.MapPingsChanged(new TeamMapPingBatch([older], 8));
        client.MapPingsChanged(Array.Empty<TeamMapPingSnapshot>());

        var visible = Assert.Single(client.CurrentState.MapPings);
        Assert.Equal(2, visible.Revision);
        Assert.Equal(9, client.CurrentState.StateRevision);
    }

    [Fact]
    public async Task VersionedSnapshotIgnoresALateLegacyPingList()
    {
        await using var client = new TeamRelayClient();
        var current = Ping(revision: 3);

        client.ReceiveSnapshot(new TeamSnapshot(
            Guid.NewGuid(),
            "ABC123",
            [],
            [current],
            StateRevision: 12));
        client.MapPingsChanged(Array.Empty<TeamMapPingSnapshot>());

        Assert.Equal(3, Assert.Single(client.CurrentState.MapPings).Revision);
        Assert.Equal(12, client.CurrentState.StateRevision);
    }

    [Fact]
    public void TelemetryObservationUsesClientReceiptTimeAndHeartbeatDoesNotRefreshIt()
    {
        var memberId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;
        var original = Member(memberId, revision: 5, sequence: 2, receivedAt - TimeSpan.FromHours(3));

        var first = TeamRelayClient.ObserveTelemetry(original, null, receivedAt);
        var heartbeat = TeamRelayClient.ObserveTelemetry(
            Member(memberId, revision: 6, sequence: 2, receivedAt + TimeSpan.FromHours(4)),
            first,
            receivedAt + TimeSpan.FromSeconds(5));
        var changed = TeamRelayClient.ObserveTelemetry(
            Member(memberId, revision: 7, sequence: 3, receivedAt + TimeSpan.FromHours(4)),
            heartbeat,
            receivedAt + TimeSpan.FromSeconds(6));

        Assert.Equal(receivedAt, first.ClientTelemetryObservedAt);
        Assert.Equal(receivedAt, heartbeat.ClientTelemetryObservedAt);
        Assert.Equal(receivedAt + TimeSpan.FromSeconds(6), changed.ClientTelemetryObservedAt);
    }

    private static TeamMemberSnapshot Member(Guid id, long revision, long sequence, DateTimeOffset at) =>
        new(id, id.ToString("N"), true, at, new TeamMemberTelemetry
        {
            Sequence = sequence,
            UpdatedAt = at
        })
        { StateRevision = revision };

    private static TeamMapPingSnapshot Ping(long revision) => new(
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        "Owner",
        revision,
        "gateway",
        1,
        0.4,
        0.6,
        10,
        20,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);
}
