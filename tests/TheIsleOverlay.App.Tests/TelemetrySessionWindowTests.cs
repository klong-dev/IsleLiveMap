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

        Assert.InRange(duration.TotalMilliseconds, 1d, 80d);
    }
}
