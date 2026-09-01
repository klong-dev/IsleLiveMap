using System.IO;
using System.Text.Json;

namespace TheIsleOverlay.App;

public sealed record ReleaseHighlightsPreferences
{
    public HashSet<string> HiddenVersions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReleaseHighlightsPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public ReleaseHighlightsPreferenceStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? AppPaths.ReleaseHighlightsPreferences
            : path;
    }

    public bool ShouldShow(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        return !Load().HiddenVersions.Contains(version.Trim());
    }

    public void HideVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        var preferences = Load();
        preferences.HiddenVersions.Add(version.Trim());

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private ReleaseHighlightsPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new ReleaseHighlightsPreferences();
            }

            return JsonSerializer.Deserialize<ReleaseHighlightsPreferences>(
                       File.ReadAllText(_path),
                       JsonOptions)
                   ?? new ReleaseHighlightsPreferences();
        }
        catch (JsonException)
        {
            return new ReleaseHighlightsPreferences();
        }
        catch (IOException)
        {
            return new ReleaseHighlightsPreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new ReleaseHighlightsPreferences();
        }
    }
}
