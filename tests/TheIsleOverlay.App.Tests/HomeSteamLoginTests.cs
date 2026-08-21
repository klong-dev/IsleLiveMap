using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class HomeSteamLoginTests
{
    [Fact]
    public void Home_ShowsOneSteamLoginAndHidesLegacyServerLogins()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals((string?)element.Attribute(nameAttribute), name, StringComparison.Ordinal));

        Assert.NotEqual("Collapsed", (string?)Control("SteamLoginButton").Attribute("Visibility"));
        foreach (var legacyButton in new[]
        {
            "EraSourceButton",
            "DinoSourceButton",
            "PremiumSourceButton",
            "HoHoSourceButton"
        })
        {
            Assert.Equal("Collapsed", (string?)Control(legacyButton).Attribute("Visibility"));
        }
    }
}
