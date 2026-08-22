using System.IO;

namespace TheIsleOverlay.App;

public sealed class ReleaseHighlightsStore
{
    private readonly string _path;

    public ReleaseHighlightsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KLongDev",
            "IsleLiveMap",
            "last-release-highlights.txt");
    }

    public bool ShouldShow(string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return false;
        }

        try
        {
            if (!File.Exists(_path))
            {
                return true;
            }

            var lastShown = File.ReadAllText(_path).Trim();
            if (string.Equals(lastShown, currentVersion.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Version.TryParse(lastShown, out var previous)
                   && Version.TryParse(currentVersion.Trim(), out var current)
                ? current > previous
                : true;
        }
        catch
        {
            return true;
        }
    }

    public void MarkShown(string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, currentVersion.Trim());
        }
        catch
        {
            // A read-only profile should never prevent the app from opening.
        }
    }
}
