using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayLayoutSettingsTests
{
    [Theory]
    [InlineData(double.NaN, 1d)]
    [InlineData(double.NegativeInfinity, 1d)]
    [InlineData(0.2d, 0.65d)]
    [InlineData(0.65d, 0.65d)]
    [InlineData(1.234d, 1.23d)]
    [InlineData(1.75d, 1.75d)]
    [InlineData(4d, 1.75d)]
    public void Scale_IsFiniteRoundedAndClamped(double input, double expected)
    {
        Assert.Equal(expected, OverlayLayoutRules.NormalizeScale(input));
    }

    [Fact]
    public void HorizontalDrag_ResizesAgainstTheWholeBaseOverlayWidth()
    {
        Assert.Equal(
            1.5d,
            OverlayLayoutRules.ScaleFromHorizontalDrag(
                startingScale: 1d,
                deltaDip: OverlayLayoutRules.BaseWidth / 2d));
        Assert.Equal(
            OverlayLayoutRules.MaximumScale,
            OverlayLayoutRules.ScaleFromHorizontalDrag(1.7d, 500d));
        Assert.Equal("65%", OverlayLayoutRules.FormatScale(0.2d));
    }

    [Fact]
    public void MapZoom_ExtendsPreviousNineTimesLimitByTwentyFivePercent()
    {
        Assert.Equal(11.25d, MapZoomRules.MaximumZoom);
        Assert.Equal(9d * 1.25d, MapZoomRules.MaximumZoom);

        var zoom = MapZoomRules.DefaultZoom;
        for (var index = 0; index < 100; index++)
        {
            zoom = MapZoomRules.ZoomIn(zoom);
        }

        Assert.Equal(MapZoomRules.MaximumZoom, zoom);
        Assert.Equal(
            MapZoomRules.MinimumZoom,
            MapZoomRules.ZoomOut(MapZoomRules.MinimumZoom));
    }

    [Fact]
    public void MapPan_DragMovesTheAbsoluteFocusWithThePointer()
    {
        var focus = MapPanRules.ApplyDragToFocus(
            new TheIsleOverlay.Core.MapPoint(0.5d, 0.5d),
            horizontalDelta: 120d,
            verticalDelta: -60d,
            imageWidth: 1200d,
            imageHeight: 600d);

        Assert.Equal(0.4d, focus.Left, precision: 10);
        Assert.Equal(0.6d, focus.Top, precision: 10);
    }

    [Fact]
    public void MapPan_ClampsAtImageEdgesWithoutAccumulatingHiddenOverscroll()
    {
        var focus = MapPanRules.ClampFocus(
            new TheIsleOverlay.Core.MapPoint(-10d, 10d),
            viewportWidth: 300d,
            viewportHeight: 300d,
            imageWidth: 1200d,
            imageHeight: 600d);

        Assert.Equal(0.125d, focus.Left, precision: 10);
        Assert.Equal(0.75d, focus.Top, precision: 10);
    }

    [Fact]
    public void MapPan_KeepsImageCenteredWhenItDoesNotExceedViewport()
    {
        var focus = MapPanRules.ClampFocus(
            new TheIsleOverlay.Core.MapPoint(0.2d, 0.8d),
            viewportWidth: 300d,
            viewportHeight: 300d,
            imageWidth: 300d,
            imageHeight: 250d);

        Assert.Equal(0.5d, focus.Left);
        Assert.Equal(0.5d, focus.Top);
    }

    [Fact]
    public void FreeLookFocusStaysAbsoluteWhenThePlayerGpsMoves()
    {
        var startingFocus = new TheIsleOverlay.Core.MapPoint(0.6, 0.4);
        var dragged = MapPanRules.ApplyDragToFocus(
            startingFocus,
            horizontalDelta: 120,
            verticalDelta: -80,
            imageWidth: 1200,
            imageHeight: 1000);
        var clamped = MapPanRules.ClampFocus(
            dragged,
            viewportWidth: 300,
            viewportHeight: 300,
            imageWidth: 1200,
            imageHeight: 1000);

        Assert.Equal(0.5, clamped.Left, 8);
        Assert.Equal(0.48, clamped.Top, 8);
        var playerMovedTo = new TheIsleOverlay.Core.MapPoint(0.9, 0.9);
        Assert.NotEqual(playerMovedTo, clamped);
    }

    [Fact]
    public void Store_RoundTripsScaleAndPositionAndRecoversFromMalformedJson()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IsleLiveMap.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "overlay-layout.json");
        try
        {
            var store = new OverlayLayoutSettingsStore(path);
            var defaults = store.Load();
            Assert.Equal(2, defaults.Version);
            Assert.Equal(OverlayLayoutRules.DefaultScale, defaults.Scale);
            Assert.Null(defaults.Left);
            Assert.Null(defaults.Top);
            Assert.Empty(defaults.Widgets);

            store.Save(new OverlayLayoutSettings
            {
                Scale = 1.37d,
                Left = 120.5d,
                Top = 80.25d,
                Widgets = new Dictionary<string, OverlayWidgetPosition>
                {
                    [OverlayLayoutRules.MapWidget] = new() { Left = 900d, Top = 70d },
                    [OverlayLayoutRules.StatsWidget] = new() { Left = 900d, Top = 382d }
                }
            });
            var restored = store.Load();
            Assert.Equal(1.37d, restored.Scale);
            Assert.Equal(120.5d, restored.Left);
            Assert.Equal(80.25d, restored.Top);
            Assert.Equal(900d, restored.Widgets[OverlayLayoutRules.MapWidget].Left);
            Assert.Equal(382d, restored.Widgets[OverlayLayoutRules.StatsWidget].Top);

            File.WriteAllText(path, "{broken");
            var recovered = store.Load();
            Assert.Equal(2, recovered.Version);
            Assert.Equal(OverlayLayoutRules.DefaultScale, recovered.Scale);
            Assert.Null(recovered.Left);
            Assert.Null(recovered.Top);
            Assert.Empty(recovered.Widgets);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Overlay_ExposesUniformScaleControlsOnlyForTheWholeHud()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "MainWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                name,
                StringComparison.Ordinal));

        var window = document.Root ?? throw new InvalidOperationException("Window root is missing.");
        Assert.Equal("Manual", (string?)window.Attribute("SizeToContent"));
        Assert.Null(window.Attribute("Width"));
        Assert.Null(Control("OverlayScaleRoot").Attribute("Width"));
        Assert.NotNull(Control("WidgetCanvas"));
        Assert.NotNull(Control("LayoutControls"));
        Assert.NotNull(Control("MapPanel"));
        Assert.NotNull(Control("StatsPanel"));
        Assert.NotNull(Control("TeamPanel"));
        Assert.NotNull(Control("MissionPanel"));
        Assert.Equal("MapFocusModeButton_Click", (string?)Control("MapFocusModeButton").Attribute("Click"));
        Assert.Equal("WidgetPanel_MouseLeftButtonDown", (string?)Control("MapPanel").Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("WidgetPanel_MouseLeftButtonDown", (string?)Control("StatsPanel").Attribute("PreviewMouseLeftButtonDown"));
        Assert.NotNull(Control("ScaleDownButton"));
        Assert.NotNull(Control("ScaleResetButton"));
        Assert.NotNull(Control("ScaleUpButton"));
        var resizeGrip = Control("ResizeGrip");
        Assert.Equal("Thumb", resizeGrip.Name.LocalName);
        Assert.Equal("ResizeGrip_DragStarted", (string?)resizeGrip.Attribute("DragStarted"));
        Assert.Equal("ResizeGrip_DragDelta", (string?)resizeGrip.Attribute("DragDelta"));
        Assert.Equal("ResizeGrip_DragCompleted", (string?)resizeGrip.Attribute("DragCompleted"));
        Assert.Equal("100%", (string?)Control("OverlayScaleLabel").Attribute("Text"));
        Assert.Equal("ResetWidgetLayoutButton_Click", (string?)Control("ResetWidgetLayoutButton").Attribute("Click"));
    }
}
