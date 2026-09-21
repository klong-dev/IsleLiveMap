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
        Assert.Equal(RemoteEntityLifecycleState.Visible, Assert.Single(first).State);

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
        Assert.Equal(RemoteEntityLifecycleState.Updated, second.State);
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
