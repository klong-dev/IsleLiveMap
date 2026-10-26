using System.Diagnostics;
using System.IO;

namespace TheIsleOverlay.App;

/// <summary>Version-independent, at-most-once website dispatch per Windows user.</summary>
internal sealed class FirstLaunchWebsiteService
{
    internal const string WebsiteUri = "https://islecheat.org/";
    private readonly string _markerPath;
    private readonly Action<ProcessStartInfo> _openBrowser;

    internal FirstLaunchWebsiteService(
        string? markerPath = null,
        Action<ProcessStartInfo>? openBrowser = null)
    {
        _markerPath = markerPath ?? AppPaths.FirstLaunchWebsiteMarker;
        _openBrowser = openBrowser ?? OpenBrowser;
    }

    internal bool TryOpenOnce()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            // CreateNew is the cross-process claim. The marker is written
            // before shell dispatch so two launcher instances cannot both open.
            using var claim = new FileStream(_markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            claim.WriteByte(1);
            claim.Flush(flushToDisk: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        try
        {
            _openBrowser(new ProcessStartInfo(WebsiteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Security.SecurityException or NotSupportedException)
        {
            // Keep the claim consumed so later updates never reopen the site.
            return false;
        }
    }

    private static void OpenBrowser(ProcessStartInfo info)
    {
        using var process = Process.Start(info);
    }
}
