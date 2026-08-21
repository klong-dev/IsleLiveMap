using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class HomeSteamLoginTests
{
    [Fact]
    public void Home_ShowsIslePilotAndTheTwoRequiredDirectWebsiteConnections()
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
        Assert.NotEqual("Collapsed", (string?)Control("EraSourceButton").Attribute("Visibility"));
        Assert.Equal("era", (string?)Control("EraSourceButton").Attribute("Tag"));
        Assert.NotEqual("Collapsed", (string?)Control("PandoraSourceButton").Attribute("Visibility"));
        Assert.Equal("pandora", (string?)Control("PandoraSourceButton").Attribute("Tag"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => new[] { "DinoSourceButton", "PremiumSourceButton", "HoHoSourceButton" }
                .Contains((string?)element.Attribute(nameAttribute), StringComparer.Ordinal));
    }

    [Fact]
    public void Pandora_SourceCapturesTheCompleteHostSessionForItsExpressApi()
    {
        var source = TelemetrySourceDefinition.Pandora;

        Assert.Equal("https://islapandora.eu/", source.BaseUri.AbsoluteUri);
        Assert.Equal("https://islapandora.eu/live-map", source.LoginUri.AbsoluteUri);
        Assert.Equal(TelemetrySourceKind.Pandora, source.Kind);
        Assert.True(source.CaptureAllHostCookies);
        Assert.Same(source, TelemetrySourceDefinition.FromId("pandora"));
    }
}
