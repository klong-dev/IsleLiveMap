using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayTeamPanelTests
{
    [Fact]
    public void Overlay_ContainsTeamMarkersAndCompactVitalRows()
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

        Assert.Equal("Collapsed", (string?)Control("TeamPanel").Attribute("Visibility"));
        Assert.NotNull(Control("TeamMarkerLayer"));
        Assert.NotNull(Control("TeamMembersList"));
        Assert.NotNull(Control("TeamCodeLabel"));

        var textLabels = document.Descendants()
            .Select(element => (string?)element.Attribute("Text"))
            .Where(text => text is not null)
            .ToArray();
        Assert.Contains("HP", textLabels);
        Assert.Contains("ĐÓI", textLabels);
        Assert.Contains("NƯỚC", textLabels);
    }
}
