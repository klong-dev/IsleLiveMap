using System.IO;
using System.Text.Json;

namespace TheIsleOverlay.App;

public sealed record OverlayLayoutSettings
{
    public int Version { get; init; } = 1;
    public double Scale { get; init; } = OverlayLayoutRules.DefaultScale;
    public double? Left { get; init; }
    public double? Top { get; init; }
}

public static class OverlayLayoutRules
{
    public const double BaseWidth = 318d;
    public const double DefaultScale = 1d;
    public const double MinimumScale = 0.65d;
    public const double MaximumScale = 1.75d;
    public const double ButtonStep = 0.1d;

    public static OverlayLayoutSettings Normalize(OverlayLayoutSettings? settings)
    {
        settings ??= new OverlayLayoutSettings();
        return settings with
        {
            Version = 1,
            Scale = NormalizeScale(settings.Scale),
            Left = FiniteOrNull(settings.Left),
            Top = FiniteOrNull(settings.Top)
        };
    }

    public static double NormalizeScale(double scale)
    {
        if (!double.IsFinite(scale))
        {
            return DefaultScale;
        }

        return Math.Round(
            Math.Clamp(scale, MinimumScale, MaximumScale),
            2,
            MidpointRounding.AwayFromZero);
    }

    public static double ScaleFromHorizontalDrag(double startingScale, double deltaDip) =>
        NormalizeScale(startingScale + deltaDip / BaseWidth);

    public static string FormatScale(double scale) => $"{NormalizeScale(scale) * 100d:0}%";

    private static double? FiniteOrNull(double? value) =>
        value is { } number && double.IsFinite(number) ? number : null;
}

public static class MapZoomRules
{
    public const double DefaultZoom = 4d;
    public const double MinimumZoom = 1d;
    public const double MaximumZoom = 9d;
    public const double WheelStep = 0.35d;

    public static double ZoomIn(double current) =>
        Math.Min(MaximumZoom, current + WheelStep);

    public static double ZoomOut(double current) =>
        Math.Max(MinimumZoom, current - WheelStep);
}

public sealed class OverlayLayoutSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public OverlayLayoutSettingsStore(string? path = null)
    {
        var overridePath = Environment.GetEnvironmentVariable(
            "ISLELIVEMAP_LAYOUT_SETTINGS_PATH");
        _path = string.IsNullOrWhiteSpace(path)
            ? string.IsNullOrWhiteSpace(overridePath)
                ? AppPaths.OverlayLayoutSettings
                : Path.GetFullPath(overridePath)
            : Path.GetFullPath(path);
    }

    public OverlayLayoutSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new OverlayLayoutSettings();
            }

            var settings = JsonSerializer.Deserialize<OverlayLayoutSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return OverlayLayoutRules.Normalize(settings);
        }
        catch
        {
            return new OverlayLayoutSettings();
        }
    }

    public void Save(OverlayLayoutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Overlay settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(OverlayLayoutRules.Normalize(settings), JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        catch
        {
            // Layout changes remain usable even if Windows temporarily blocks persistence.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }
}
