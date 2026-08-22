using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class PrimeQuestCompletionTrackerTests
{
    [Fact]
    public void Capture_DoesNotNotifyForAlreadyCompletedInitialTasks()
    {
        var tracker = new PrimeQuestCompletionTracker();

        var completed = tracker.Capture(
        [
            new PrimeQuestTelemetry { Name = "Visit a Sanctuary as a juvenile", Done = true }
        ]);

        Assert.Empty(completed);
    }

    [Fact]
    public void Capture_ReturnsOnlyFalseToTrueTransitionsOnce()
    {
        var tracker = new PrimeQuestCompletionTracker();
        tracker.Capture(
        [
            new PrimeQuestTelemetry { Name = "Get nested in", Done = false },
            new PrimeQuestTelemetry { Name = "Never get Muscle spasms", Done = true }
        ]);

        var completed = tracker.Capture(
        [
            new PrimeQuestTelemetry { Name = "Get nested in", Done = true },
            new PrimeQuestTelemetry { Name = "Never get Muscle spasms", Done = true }
        ]);
        var repeated = tracker.Capture(
        [
            new PrimeQuestTelemetry { Name = "Get nested in", Done = true }
        ]);

        Assert.Equal("Get nested in", Assert.Single(completed).Name);
        Assert.Empty(repeated);
    }

    [Fact]
    public void Reset_EstablishesANewBaselineWithoutNotifications()
    {
        var tracker = new PrimeQuestCompletionTracker();
        tracker.Capture([new PrimeQuestTelemetry { Name = "Quest", Done = false }]);
        tracker.Reset();

        Assert.Empty(tracker.Capture([new PrimeQuestTelemetry { Name = "Quest", Done = true }]));
    }
}
