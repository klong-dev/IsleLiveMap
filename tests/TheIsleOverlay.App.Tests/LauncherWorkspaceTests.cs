using System.IO;
using System.Xml.Linq;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class LauncherWorkspaceTests
{
    [Fact]
    public void Home_UsesNewWorkspaceWithInlineReleaseRailAndAllDestinations()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "HomeWindow.xaml");
        var document = XDocument.Load(path);
        var source = document.ToString();
        foreach (var label in new[] { "TRANG CHỦ", "THÔNG TIN", "VIỆT HÓA", "PHÍM TẮT", "CÀI ĐẶT", "HƯỚNG DẪN", "QUYỀN PRO" })
            Assert.Contains(label, source, StringComparison.Ordinal);
        Assert.Contains("ReleaseRail", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseList", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseRail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShortcutSettingsWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_ContainsTeamContactAndProRegistrationFlows()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "HomeWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "HomeWindow.xaml.cs"));
        Assert.Contains("Tag=\"team\"", source, StringComparison.Ordinal);
        Assert.Contains("Tag=\"contact\"", source, StringComparison.Ordinal);
        Assert.Contains("BuildTeam", code, StringComparison.Ordinal);
        Assert.Contains("BuildContact", code, StringComparison.Ordinal);
        Assert.Contains("TeamRoomLimits.For", code, StringComparison.Ordinal);
        Assert.Contains("https://isle.klong.dev", code, StringComparison.Ordinal);
        Assert.Contains("ZaloTeamQr.jpg", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_UsesUpdateProgressLabelsAndServerChoiceVisuals()
    {
        var code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "HomeWindow.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "HomeWindow.xaml"));

        Assert.Contains("ĐANG KIỂM TRA CẬP NHẬT", code, StringComparison.Ordinal);
        Assert.Contains("ĐANG CẬP NHẬT v", code, StringComparison.Ordinal);
        Assert.Contains("MỞ MAP PRO  →", code, StringComparison.Ordinal);
        Assert.Contains("GachaLogo.png", code, StringComparison.Ordinal);
        Assert.Contains("OriginLogo.png", code, StringComparison.Ordinal);
        Assert.Contains("Columns = 2", code, StringComparison.Ordinal);
        Assert.Contains("ServerAction", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotStore_RepresentsEmptyAndLatestState()
    {
        var store = new LatestTelemetrySnapshotStore();
        Assert.Null(store.Current);
        var snapshot = new TelemetrySnapshot { Success = true, Source = "fixture", UpdatedAt = DateTimeOffset.UtcNow };
        store.Update(snapshot);
        Assert.Same(snapshot, store.Current);
        Assert.NotNull(store.ReceivedAt);
        store.Clear();
        Assert.Null(store.Current);
    }
}
