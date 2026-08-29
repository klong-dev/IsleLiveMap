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
    public void MapZoom_ExtendsPreviousSixTimesLimitByFiftyPercent()
    {
        Assert.Equal(9d, MapZoomRules.MaximumZoom);
        Assert.Equal(6d * 1.5d, MapZoomRules.MaximumZoom);

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
            Assert.Equal(new OverlayLayoutSettings(), store.Load());

            store.Save(new OverlayLayoutSettings
            {
                Scale = 1.37d,
                Left = 120.5d,
                Top = 80.25d
            });
            var restored = store.Load();
            Assert.Equal(1.37d, restored.Scale);
            Assert.Equal(120.5d, restored.Left);
            Assert.Equal(80.25d, restored.Top);

            File.WriteAllText(path, "{broken");
            Assert.Equal(new OverlayLayoutSettings(), store.Load());
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
        Assert.Equal("WidthAndHeight", (string?)window.Attribute("SizeToContent"));
        Assert.Null(window.Attribute("Width"));
        Assert.Equal("318", (string?)Control("OverlayScaleRoot").Attribute("Width"));
        Assert.NotNull(Control("OverlayScaleTransform"));
        Assert.NotNull(Control("LayoutControls"));
        Assert.NotNull(Control("ScaleDownButton"));
        Assert.NotNull(Control("ScaleResetButton"));
        Assert.NotNull(Control("ScaleUpButton"));
        var resizeGrip = Control("ResizeGrip");
        Assert.Equal("Thumb", resizeGrip.Name.LocalName);
        Assert.Equal("ResizeGrip_DragStarted", (string?)resizeGrip.Attribute("DragStarted"));
        Assert.Equal("ResizeGrip_DragDelta", (string?)resizeGrip.Attribute("DragDelta"));
        Assert.Equal("ResizeGrip_DragCompleted", (string?)resizeGrip.Attribute("DragCompleted"));
        Assert.Equal("100%", (string?)Control("OverlayScaleLabel").Attribute("Text"));
    }
}
