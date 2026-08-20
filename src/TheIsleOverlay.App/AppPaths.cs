using System.IO;

namespace TheIsleOverlay.App;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KLongDev",
        "IsleLiveMap");

    public static string WebView2Profile { get; } = Path.Combine(Root, "WebView2");
}
