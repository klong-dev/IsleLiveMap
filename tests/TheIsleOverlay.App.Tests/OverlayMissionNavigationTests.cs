using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayMissionNavigationTests
{
    [Fact]
    public void Overlay_ContainsRouteWorkspaceAndPrimeMissionListWithoutLegacyHotkeyBlock()
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
        Assert.NotNull(Control("RouteCoordinateInput"));
        Assert.Equal("StartRouteButton_Click", (string?)Control("StartRouteButton").Attribute("Click"));
        Assert.Equal("StopRouteButton_Click", (string?)Control("StopRouteButton").Attribute("Click"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                "HotkeyGuide",
                StringComparison.Ordinal));
    }
}
