using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayMissionNavigationTests
{
    [Fact]
    public void Overlay_ContainsMapNotesAndPrimeMissionListWithoutCoordinateRouteWorkspace()
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

        Assert.NotNull(Control("MissionPanel"));
        Assert.NotNull(Control("MissionList"));
        Assert.NotNull(Control("MissionToast"));
        Assert.NotNull(Control("MapNoteLineLayer"));
        Assert.NotNull(Control("MapNoteMarkerLayer"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                "RouteCoordinateInput",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                "HotkeyGuide",
                StringComparison.Ordinal));
    }

    [Fact]
    public void FullMapNotesWindow_IsFixedAndProvidesAnInteractiveMarkerPalette()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "MapNotesWindow.xaml"));
        var copy = string.Join(
            " ",
            document.Descendants().Attributes("Text").Select(attribute => attribute.Value));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        Assert.Contains("KHÔNG ZOOM", copy, StringComparison.Ordinal);
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute(nameAttribute) == "MapSurface"
            && (string?)element.Attribute("MouseLeftButtonUp") == "MapSurface_MouseLeftButtonUp");
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute(nameAttribute) == "PaletteGrid");
    }
}
