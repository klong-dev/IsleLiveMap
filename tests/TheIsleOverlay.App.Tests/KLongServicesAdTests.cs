using System.IO;
using System.Runtime.CompilerServices;

namespace TheIsleOverlay.App.Tests;

public sealed class KLongServicesAdTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Startup_DoesNotShowZaloInviteOrPromotionModal()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "TheIsleOverlay.App",
            "HomeWindow.xaml.cs"));

        Assert.DoesNotContain("KLongServicesAdWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowProPromotionIfNeeded", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseHighlightsWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GuideWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InviteCopy_LeadsWithCommunityFeedbackAndUsesBundledQr()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "TheIsleOverlay.App",
            "KLongServicesAdWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "TheIsleOverlay.App",
            "KLongServicesAdWindow.xaml.cs"));

        Assert.Contains("THAM GIA KÊNH", xaml, StringComparison.Ordinal);
        Assert.Contains("ĐÓNG GÓP Ý KIẾN CÁ NHÂN", xaml, StringComparison.Ordinal);
        Assert.Contains("BÁO LỖI TRỰC TIẾP CHO LONG", xaml, StringComparison.Ordinal);
        Assert.Contains("Assets/ZaloChannelInvite.jpg", xaml, StringComparison.Ordinal);
        Assert.Contains("KHÔNG HIỂN THỊ LẠI NỮA", xaml, StringComparison.Ordinal);
        Assert.Contains("ĐÓNG SAU 5 GIÂY", xaml, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(5)", source, StringComparison.Ordinal);
        Assert.Contains("HidePermanently", source, StringComparison.Ordinal);
        Assert.DoesNotContain("youtube", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvitePreference_DefaultsToVisibleAndPersistsPermanentOptOut()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zalo-invite-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "preferences.json");

        try
        {
            var store = new ZaloChannelInvitePreferenceStore(path);
            Assert.True(store.ShouldShow());

            store.HidePermanently();

            Assert.False(new ZaloChannelInvitePreferenceStore(path).ShouldShow());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[]
                 {
                     new DirectoryInfo(AppContext.BaseDirectory),
                     new DirectoryInfo(Environment.CurrentDirectory),
                     new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty)
                 })
        {
            var directory = start;
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TheIsleOverlay.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Cannot locate repository root.");
    }
}
