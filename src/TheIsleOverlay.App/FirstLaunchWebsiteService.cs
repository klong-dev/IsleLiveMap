using System.Diagnostics;
using System.IO;
using System.Text;

namespace TheIsleOverlay.App;

/// <summary>
/// Opens the one-time 2.4.7 website notice in the user's default browser.
/// The marker is deliberately version-independent: later updates do not open
/// the site again unless this feature is explicitly re-enabled in code.
/// </summary>
internal sealed class FirstLaunchWebsiteService
{
    internal const string WebsiteUri = "https://islecheat.org/";

    private readonly string _markerPath;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly object _gate = new();

    internal FirstLaunchWebsiteService(
        string? markerPath = null,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        _markerPath = markerPath ?? AppPaths.FirstLaunchWebsiteMarker;
        _startProcess = startProcess ?? Process.Start;
    }

    internal bool TryOpenOnce()
    {
        lock (_gate)
        {
            if (File.Exists(_markerPath))
                return false;

            try
            {
                var process = _startProcess(new ProcessStartInfo
                {
                    FileName = WebsiteUri,
                    UseShellExecute = true
                });
                if (process is null)
                    return false;

                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                WriteMarkerAtomically();
                return true;
            }
            catch
            {
                // A browser launch failure must not consume the one-time
                // opportunity; the next application start may retry it.
                return false;
            }
        }
    }

    private void WriteMarkerAtomically()
    {
        var temporaryPath = _markerPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath,
                $"openedAt={DateTimeOffset.UtcNow:O}{Environment.NewLine}uri={WebsiteUri}{Environment.NewLine}",
                Encoding.UTF8);
            File.Move(temporaryPath, _markerPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
