using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class HomeSteamLoginTests
{
    [Fact]
    public void Home_UsesIslePilotStatsWithDirectGpsAndRemovesWebsiteSourceBlocks()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals((string?)element.Attribute(nameAttribute), name, StringComparison.Ordinal));

        var liveMapButton = Control("SteamLoginButton");
        var liveMapTitle = Control("SteamLoginTitleLabel");
        var logoutSteamButton = Control("LogoutSteamButton");
        var logoutProButton = Control("LogoutProButton");
        Assert.NotEqual("Collapsed", (string?)liveMapButton.Attribute("Visibility"));
        Assert.Equal("SteamLoginButton_Click", (string?)liveMapButton.Attribute("Click"));
        Assert.Equal(
            "SteamLoginPanel_Loaded",
            (string?)liveMapButton.Parent?.Attribute("Loaded"));
        Assert.Equal("MỞ LIVE MAP", (string?)liveMapTitle.Attribute("Text"));
        Assert.Equal("ĐĂNG XUẤT STEAM", (string?)logoutSteamButton.Attribute("Content"));
        Assert.Equal("LogoutSteamButton_Click", (string?)logoutSteamButton.Attribute("Click"));
        Assert.Equal("ĐĂNG XUẤT PRO", (string?)logoutProButton.Attribute("Content"));
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
        Assert.Contains("GPS trực tiếp · Dino stats đồng bộ qua IslePilot", text);
        Assert.DoesNotContain("SERVER DÙNG WEBSITE RIÊNG", text);
    }

    [Fact]
    public void LiveMapHandler_ComposesIslePilotStatsWithLocalPositionAndProEntities()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.IslePilot.cs"));

        Assert.Contains("IslePilotRealtimeSession.Create", source, StringComparison.Ordinal);
        Assert.Contains("new LocalPositionTelemetrySession(", source, StringComparison.Ordinal);
        Assert.Contains("App.CurrentApp.TakeLocalTelemetrySource()", source, StringComparison.Ordinal);
        Assert.Contains("CreateProPlayerSource()", source, StringComparison.Ordinal);
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
