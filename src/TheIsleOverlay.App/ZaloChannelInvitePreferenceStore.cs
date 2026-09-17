using System.IO;
using System.Text.Json;

namespace TheIsleOverlay.App;

public sealed record ZaloChannelInvitePreferences
{
    public bool HiddenPermanently { get; init; }
}

public sealed class ZaloChannelInvitePreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public ZaloChannelInvitePreferenceStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? AppPaths.ZaloChannelInvitePreferences
            : path;
    }

    public bool ShouldShow() => !Load().HiddenPermanently;

    public void HidePermanently()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    new ZaloChannelInvitePreferences { HiddenPermanently = true },
                    JsonOptions));
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

    private ZaloChannelInvitePreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new ZaloChannelInvitePreferences();
            }

            return JsonSerializer.Deserialize<ZaloChannelInvitePreferences>(
                       File.ReadAllText(_path),
                       JsonOptions)
                   ?? new ZaloChannelInvitePreferences();
        }
        catch (JsonException)
        {
            return new ZaloChannelInvitePreferences();
        }
        catch (IOException)
        {
            return new ZaloChannelInvitePreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new ZaloChannelInvitePreferences();
        }
    }
}
