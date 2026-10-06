using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class UpdateReadyWindowTests
{
    [Fact]
    public void DismissedOrFailedUpdateCanBeReopenedWithoutUnlockingMap()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "TestAssets", "HomeWindow.xaml.cs"));
        Assert.Contains("ReopenUpdateReadyDialog", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("_updateReadyDialogShown = false;", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception)", source, StringComparison.Ordinal);
        Assert.Contains("MapLaunchGatePolicy.AllowsMap(_mapLaunchGateState)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateReadyModalContainsVisibleRestartAction()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "TestAssets", "UpdateReadyWindow.xaml"));
        Assert.Contains("CẬP NHẬT &amp; KHỞI ĐỘNG LẠI", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Cập nhật và khởi động lại\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_restartForUpdateButton", File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "TestAssets", "HomeWindow.xaml.cs")), StringComparison.Ordinal);
    }
}
