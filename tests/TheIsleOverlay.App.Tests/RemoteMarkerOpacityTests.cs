using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class RemoteMarkerOpacityTests
{
    [Theory]
    [InlineData(true, false, 0.60)]
    [InlineData(true, true, 0.60)]
    [InlineData(false, true, 0.60)]
    [InlineData(false, false, 1.0)]
    public void StaleMarkersAreReadableButDistinctFromLive(
        bool stale, bool provisional, double expected)
    {
        Assert.Equal(expected, MainWindow.RemoteMarkerOpacity(stale, provisional));
    }

    [Fact]
    public async Task PredictedDotHasSameSolidPaletteAsStaleDot()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var create = typeof(MainWindow).GetMethod("CreateRemotePlayerDot",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var predicted = create.Invoke(null, ["Dino ?", RemoteEntityMapCategory.UnclassifiedPlayer, true])!;
                var normal = create.Invoke(null, ["Dino", RemoteEntityMapCategory.UnclassifiedPlayer, false])!;
                var type = predicted.GetType();
                var shape = (System.Windows.Shapes.Ellipse)type.GetProperty("Shape")!.GetValue(predicted)!;
                var other = (System.Windows.Shapes.Ellipse)type.GetProperty("Shape")!.GetValue(normal)!;
                Assert.Equal(((System.Windows.Media.SolidColorBrush)other.Fill).Color,
                    ((System.Windows.Media.SolidColorBrush)shape.Fill).Color);
                Assert.Equal((byte)255, ((System.Windows.Media.SolidColorBrush)shape.Fill).Color.A);
                Assert.True(shape.StrokeDashArray is null || shape.StrokeDashArray.Count == 0);
                var visual = (System.Windows.Controls.Canvas)type.GetProperty("Visual")!.GetValue(predicted)!;
                Assert.Equal(MainWindow.RemoteMarkerOpacity(true, false), visual.Opacity);
                done.SetResult();
            }
            catch (Exception e) { done.SetException(e); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
