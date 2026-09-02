using System.Reflection;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class TelemetrySessionWindowTests
{
    [Fact]
    public void MainWindow_ConsumesSessionInsteadOfOwningPollingTimer()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.Null(typeof(MainWindow).GetField("_refreshTimer", fields));
        var sessionField = typeof(MainWindow).GetField("_telemetrySession", fields);
        Assert.NotNull(sessionField);
        Assert.Equal(typeof(ITelemetrySession), sessionField.FieldType);
        Assert.NotNull(typeof(MainWindow).GetConstructor([typeof(ITelemetrySession), typeof(string)]));
    }

    [Fact]
    public void LiveServerHeading_AnimatesWithinOneFastVisualResponseWindow()
    {
        var durationField = typeof(MainWindow).GetField(
            "LiveHeadingAnimationDuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var duration = Assert.IsType<TimeSpan>(durationField?.GetValue(null));

        Assert.InRange(duration.TotalMilliseconds, 1d, 40d);
    }

    [Fact]
    public void MovementHeading_AnimatesFastEnoughToAvoidVisibleLag()
    {
        var durationField = typeof(MainWindow).GetField(
            "MovementHeadingAnimationDuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var duration = Assert.IsType<TimeSpan>(durationField?.GetValue(null));

        Assert.InRange(duration.TotalMilliseconds, 1d, 100d);
    }

    [Fact]
    public void UiRendering_IsCappedAtTwentyFramesPerSecond()
    {
        var intervalField = typeof(MainWindow).GetField(
            "UiRenderInterval",
            BindingFlags.Static | BindingFlags.NonPublic);
        var interval = Assert.IsType<TimeSpan>(intervalField?.GetValue(null));
        var framesPerSecond = 1000d / interval.TotalMilliseconds;

        Assert.InRange(framesPerSecond, 15d, 20d);
    }

    [Fact]
    public void LatestValueBuffer_DropsIntermediateValues()
    {
        var buffer = new LatestValueBuffer<object>();
        var first = new object();
        var latest = new object();

        buffer.Publish(first);
        buffer.Publish(latest);

        Assert.True(buffer.TryTake(out var result));
        Assert.Same(latest, result);
        Assert.False(buffer.TryTake(out _));
    }
}
