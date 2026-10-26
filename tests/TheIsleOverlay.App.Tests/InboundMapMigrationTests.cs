using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class InboundMapMigrationTests
{
    [Fact]
    public void DirectMapHandlerShowsConfirmationAndContainsNoIslePilotLoginFlow()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml.cs"));
        var start = source.IndexOf("private async void OpenMap_Click", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var handler = source[start..];
        Assert.Contains("InboundMigrationNotice", handler, StringComparison.Ordinal);
        Assert.Contains("MessageBoxButton.OKCancel", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("IslePilotSteamLoginWindow", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("IslePilotRealtimeSession.Create", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void InboundVitalsReplaceIslePilotByDefault()
    {
        Assert.True(TheIsleOverlay.LocalTelemetry.LocalVitalsFeature.IsEnabled());
        Assert.True(TheIsleOverlay.LocalTelemetry.LocalVitalsFeature.ReplacesIslePilot());
    }
}
