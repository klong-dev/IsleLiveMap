using System.IO;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class PersonalHistoryTests
{
    [Fact]
    public void SignalLossProducesOneLastKnownNotDeathAndRearmsOnNewGps()
    {
        var now = DateTimeOffset.UtcNow;
        var tracker = new PersonalHistoryTracker();
        var sample = Sample(now);
        Assert.Null(tracker.Observe(sample, now));
        Assert.Null(tracker.Observe(null, now.AddSeconds(1)));
        Assert.Null(tracker.Observe(null, now.AddSeconds(10)));
        var lost = tracker.Observe(null, now.AddSeconds(11));
        Assert.Equal(sample.Player!.Location, lost!.Location);
        Assert.Equal(now, lost.ObservedAt);
        Assert.Equal("server-a:7777", lost.ServerKey);
        Assert.Null(tracker.Observe(null, now.AddSeconds(30)));
        Assert.Null(tracker.Observe(sample with { UpdatedAt = now.AddSeconds(31) }, now.AddSeconds(31)));
        Assert.Null(tracker.Observe(null, now.AddSeconds(32)));
        Assert.NotNull(tracker.Observe(null, now.AddSeconds(42)));
    }

    [Fact]
    public void ServerSwitchUsesPreviousScopeAndNeverTreatsZeroHealthAsDeath()
    {
        var now = DateTimeOffset.UtcNow;
        var tracker = new PersonalHistoryTracker();
        var first = Sample(now);
        Assert.Null(tracker.Observe(first, now));
        var other = first with { Player = first.Player! with { ServerEndpoint = "server-b:7777" } };
        Assert.Equal("server-a:7777", tracker.Observe(other, now)!.ServerKey);
        Assert.Equal("server-b:7777", tracker.ServerKey);
        Assert.Null(tracker.Observe(other, now));
    }

    [Fact]
    public void HistoryPersistsConfirmationDeletionAndExpiryWithoutRemovingManualNotes()
    {
        var root = Path.Combine(Path.GetTempPath(), "isle-history-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "notes.json");
            var store = new MapNoteStore(path);
            var now = DateTimeOffset.UtcNow;
            var location = GatewayMapProjection.Unproject(new MapPoint(0.5, 0.5));
            var result = store.AddHistory(location, "server", now);
            Assert.True(result.Success);
            Assert.Equal(MapNoteKind.LastKnown, result.Note!.Kind);
            Assert.True(store.TryChangeKind(result.Note.Id, MapNoteKind.Death).Success);
            Assert.Equal(MapNoteKind.Death, Assert.Single(new MapNoteStore(path).Notes).Kind);
            store.AddDefault(0.5, 0.5);
            store.PurgeHistory(now.AddHours(24));
            Assert.Equal(MapNoteKind.Pin, Assert.Single(store.Notes).Kind);
            var another = store.AddHistory(location, "server", now);
            Assert.True(store.TryDelete(another.Note!.Id).Success);
            Assert.DoesNotContain(new MapNoteStore(path).Notes, n => n.IsPersonalHistory);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static TelemetrySnapshot Sample(DateTimeOffset now) => new()
    {
        Success = true, PlayerOnline = true, UpdatedAt = now,
        Player = new PlayerTelemetry
        {
            ServerEndpoint = "server-a:7777", Location = new WorldLocation { X = 100, Y = 200 },
            ExactVitals = new ExactVitals { Health = 0 }
        }
    };
}
