using System.Windows;

namespace TheIsleOverlay.App.Tests;

public sealed class MapNoteViewportTests
{
    [Theory]
    [InlineData(50, 50, 200, 50, 88, 50)]
    [InlineData(50, 50, -100, 50, 12, 50)]
    [InlineData(50, 50, 50, 200, 50, 88)]
    [InlineData(50, 50, 50, -100, 50, 12)]
    public void ClipEndpoint_PlacesOffscreenNotesAtTheMapEdge(
        double startX, double startY, double targetX, double targetY,
        double expectedX, double expectedY)
    {
        var point = MainWindow.ClipEndpoint(
            new Point(startX, startY),
            new Point(targetX, targetY),
            100,
            100,
            12);

        Assert.Equal(expectedX, point.X, precision: 6);
        Assert.Equal(expectedY, point.Y, precision: 6);
    }
}
