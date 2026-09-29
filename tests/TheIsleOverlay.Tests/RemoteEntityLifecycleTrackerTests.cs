using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class RemoteEntityLifecycleTrackerTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-08-24T00:00:00Z");

    [Fact]
    public void EmptyFrameIsDifferentFromNoFrameAndEventuallyRemovesEntity()
    {
        var tracker = new RemoteEntityLifecycleTracker();
        var entity = Entity(7);
        var first = tracker.ApplyFrame(Frame(1, [entity]), Start);
        Assert.Equal(RemoteEntityLifecycleState.Discovered, Assert.Single(first).State);
        Assert.Null(Assert.Single(first).LastRenderedAt);

        var missing = tracker.ApplyFrame(Frame(2, []), Start.AddSeconds(1));
        Assert.Equal(
            RemoteEntityLifecycleState.TemporarilyMissing,
            Assert.Single(missing).State);

        var stale = tracker.AdvanceWithoutFrame(Start.AddSeconds(4));
        Assert.Equal(RemoteEntityLifecycleState.Stale, Assert.Single(stale).State);

        var removed = tracker.AdvanceWithoutFrame(Start.AddSeconds(7));
        var snapshot = Assert.Single(removed);
        Assert.Equal(RemoteEntityLifecycleState.Removed, snapshot.State);
        Assert.Equal("RemoteFrameTimeout", snapshot.RemovalReason);
    }

    [Fact]
    public void ServerChangeStartsNewGenerationAndDoesNotReuseIdentity()
    {
        var tracker = new RemoteEntityLifecycleTracker();
        var first = Assert.Single(tracker.ApplyFrame(
            Frame(1, [Entity(7)], "server-a"), Start));
        var second = Assert.Single(tracker.ApplyFrame(
            Frame(1, [Entity(7)], "server-b"), Start.AddSeconds(1)));

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(2, tracker.SessionGeneration);
    }

    [Fact]
    public void ProvisionalToVerifiedKeepsSameIdentity()
    {
        var tracker = new RemoteEntityLifecycleTracker();
        var provisional = Entity(9) with { IsProvisional = true };
        var verified = Entity(9) with { IsProvisional = false, PlayerProofName = "proof" };

        var first = Assert.Single(tracker.ApplyFrame(Frame(1, [provisional]), Start));
        var second = Assert.Single(tracker.ApplyFrame(Frame(2, [verified]), Start.AddSeconds(1)));

        Assert.Equal(first.Key, second.Key);
        Assert.False(second.IsProvisional);
        Assert.Equal(RemoteEntityLifecycleState.Discovered, second.State);
    }

    [Fact]
    public void AnonymousVerifiedPlayerWithIdentityHandlesKeepsStableLifecycle()
    {
        var tracker = new RemoteEntityLifecycleTracker();
        var anonymous = Entity(11) with
        {
            IsProvisional = false,
            ActorNetRefHandle = 1101,
            PlayerStateNetRefHandle = 1102,
            PawnNetRefHandle = 1103
        };

        var first = Assert.Single(tracker.ApplyFrame(
            Frame(1, [anonymous]), Start));
        var second = Assert.Single(tracker.ApplyFrame(
            Frame(2, [anonymous with
            {
                Location = new WorldLocation { X = 12, Y = 12 },
                ObservedAt = Start.AddSeconds(1)
            }]), Start.AddSeconds(1)));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(11, second.TrackId);
        Assert.False(second.IsProvisional);
        Assert.Equal(RemoteEntityLifecycleState.Discovered, second.State);
    }

    [Fact]
    public void AnonymousPlayerIsRetainedThroughOnePartialRosterFrameAndRecovers()
    {
        var tracker = new RemoteEntityLifecycleTracker();
        var anonymous = Entity(12) with
        {
            IsProvisional = false,
            ActorNetRefHandle = 1201,
            PlayerStateNetRefHandle = 1202,
            PawnNetRefHandle = 1203
        };

        var first = Assert.Single(tracker.ApplyFrame(
            Frame(1, [anonymous]), Start));
        var temporarilyMissing = Assert.Single(tracker.ApplyFrame(
            Frame(2, [], "server"), Start.AddSeconds(1)));
        var recovered = Assert.Single(tracker.ApplyFrame(
            Frame(3, [anonymous with { ObservedAt = Start.AddSeconds(2) }]),
            Start.AddSeconds(2)));

        Assert.Equal(first.Key, temporarilyMissing.Key);
        Assert.Equal(RemoteEntityLifecycleState.TemporarilyMissing, temporarilyMissing.State);
        Assert.Equal(first.Key, recovered.Key);
        Assert.Equal(RemoteEntityLifecycleState.Discovered, recovered.State);
        Assert.False(recovered.IsProvisional);
    }

    [Fact]
    public void SameEndpointNewSessionDoesNotReuseKeyOrSequence()
    {
        var tracker = new RemoteEntityLifecycleTracker();
        var first = Assert.Single(tracker.ApplyFrame(
            Frame(99, [Entity(7)]) with { SessionId = "old" }, Start));
        var second = Assert.Single(tracker.ApplyFrame(
            Frame(1, [Entity(7)]) with { SessionId = "new" }, Start.AddSeconds(1)));
        Assert.NotEqual(first.Key, second.Key);
        Assert.Null(second.LastRenderedAt);
    }

    [Fact]
    public void DuplicateAndReorderedFramesDoNotRefreshPresence()
    {
        var tracker = new RemoteEntityLifecycleTracker();
        tracker.ApplyFrame(Frame(2, [Entity(7)]), Start);
        tracker.ApplyFrame(Frame(1, []), Start.AddSeconds(1));
        var duplicate = Assert.Single(tracker.ApplyFrame(Frame(2, [Entity(7)]), Start.AddSeconds(5)));
        Assert.Equal(Start, duplicate.LastSeenAt);
        var removed = Assert.Single(tracker.AdvanceWithoutFrame(Start.AddSeconds(7)));
        Assert.Equal(RemoteEntityLifecycleState.Removed, removed.State);
    }

    private static RemotePlayerTelemetryFrame Frame(
        long sequence,
        IReadOnlyList<VerifiedRemoteEntityTelemetry> entities,
        string endpoint = "server") => new(
        sequence,
        Start,
        endpoint,
        new WorldLocation(),
        0,
        entities);

    private static VerifiedRemoteEntityTelemetry Entity(long id) => new(
        id,
        RemoteEntityKind.Player,
        null,
        "rex",
        "Rex",
        CreatureDiet.Carnivore,
        null,
        new WorldLocation { X = id, Y = id },
        id,
        1,
        Start,
        true);
}
