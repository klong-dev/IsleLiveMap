using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class HomeSteamLoginTests
{
    [Fact]
    public void Home_UsesDirectFreeActivationAndRemovesWebsiteSourceBlocks()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals((string?)element.Attribute(nameAttribute), name, StringComparison.Ordinal));

        var directButton = Control("SteamLoginButton");
        Assert.NotEqual("Collapsed", (string?)directButton.Attribute("Visibility"));
        Assert.Equal("OpenDirectMapButton_Click", (string?)directButton.Attribute("Click"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => new[] { "EraSourceButton", "PandoraSourceButton" }
                .Contains((string?)element.Attribute(nameAttribute), StringComparer.Ordinal));
        Assert.DoesNotContain(
            document.Descendants(),
            element => new[] { "DinoSourceButton", "PremiumSourceButton", "HoHoSourceButton" }
                .Contains((string?)element.Attribute(nameAttribute), StringComparer.Ordinal));

        var text = document.Descendants()
            .Select(element => (string?)element.Attribute("Text"))
            .Where(value => value is not null)
            .ToArray();
        Assert.Contains("KÍCH HOẠT LIVE MAP", text);
        Assert.Contains("KÍCH HOẠT PRO", text);
        Assert.DoesNotContain("SERVER DÙNG WEBSITE RIÊNG", text);
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
