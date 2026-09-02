using System.IO;
using System.Text.Json;

namespace TheIsleOverlay.App;

public sealed record OverlayLayoutSettings
{
    public int Version { get; init; } = 2;
    public double Scale { get; init; } = OverlayLayoutRules.DefaultScale;
    public double? Left { get; init; }
    public double? Top { get; init; }
    public Dictionary<string, OverlayWidgetPosition> Widgets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record OverlayWidgetPosition
{
    public double Left { get; init; }
    public double Top { get; init; }
}

public static class OverlayLayoutRules
{
    public const string MapWidget = "map";
    public const string StatsWidget = "stats";
    public const string TeamWidget = "team";
    public const string PrimeWidget = "prime";
    public const string ControlsWidget = "controls";
    public const double BaseWidth = 318d;
    public const double DefaultScale = 1d;
    public const double MinimumScale = 0.65d;
    public const double MaximumScale = 1.75d;
    public const double ButtonStep = 0.1d;

    public static OverlayLayoutSettings Normalize(OverlayLayoutSettings? settings)
    {
        settings ??= new OverlayLayoutSettings();
        var widgets = (settings.Widgets ?? new Dictionary<string, OverlayWidgetPosition>(StringComparer.OrdinalIgnoreCase))
            .Where(pair => IsKnownWidget(pair.Key) && pair.Value is not null)
            .ToDictionary(
                pair => pair.Key.ToLowerInvariant(),
                pair => new OverlayWidgetPosition
                {
                    Left = FiniteOrZero(pair.Value.Left),
                    Top = FiniteOrZero(pair.Value.Top)
                },
                StringComparer.OrdinalIgnoreCase);
        return settings with
        {
            Version = 2,
            Scale = NormalizeScale(settings.Scale),
            Left = FiniteOrNull(settings.Left),
            Top = FiniteOrNull(settings.Top),
            Widgets = widgets
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

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0d;

    private static bool IsKnownWidget(string value) => value.ToLowerInvariant() is
        MapWidget or StatsWidget or TeamWidget or PrimeWidget or ControlsWidget;
}

public static class MapZoomRules
{
    public const double DefaultZoom = 4d;
    public const double MinimumZoom = 1d;
    public const double MaximumZoom = 11.25d;
    public const double WheelStep = 0.35d;

    public static double ZoomIn(double current) =>
        Math.Min(MaximumZoom, current + WheelStep);

    public static double ZoomOut(double current) =>
        Math.Max(MinimumZoom, current - WheelStep);
}

public enum MapFocusMode
{
    FollowPlayer,
    FreeLook
}

public static class MapPanRules
{
    public static TheIsleOverlay.Core.MapPoint ApplyDragToFocus(
        TheIsleOverlay.Core.MapPoint startingFocus,
        double horizontalDelta,
        double verticalDelta,
        double imageWidth,
        double imageHeight)
    {
        if (!IsPositiveFinite(imageWidth) || !IsPositiveFinite(imageHeight))
        {
            return Normalize(startingFocus);
        }

        var start = Normalize(startingFocus);
        return new TheIsleOverlay.Core.MapPoint(
            start.Left - NormalizeDelta(horizontalDelta) / imageWidth,
            start.Top - NormalizeDelta(verticalDelta) / imageHeight);
    }

    public static TheIsleOverlay.Core.MapPoint ClampFocus(
        TheIsleOverlay.Core.MapPoint focus,
        double viewportWidth,
        double viewportHeight,
        double imageWidth,
        double imageHeight)
    {
        var normalized = Normalize(focus);
        var horizontalLimit = CenterLimit(viewportWidth, imageWidth);
        var verticalLimit = CenterLimit(viewportHeight, imageHeight);
        return new TheIsleOverlay.Core.MapPoint(
            Math.Clamp(normalized.Left, horizontalLimit, 1d - horizontalLimit),
            Math.Clamp(normalized.Top, verticalLimit, 1d - verticalLimit));
    }

    private static double CenterLimit(double viewportSize, double imageSize)
    {
        if (!IsPositiveFinite(viewportSize) || !IsPositiveFinite(imageSize) || imageSize <= viewportSize)
        {
            return 0.5d;
        }

        return Math.Clamp(viewportSize / (2d * imageSize), 0d, 0.5d);
    }

    private static TheIsleOverlay.Core.MapPoint Normalize(TheIsleOverlay.Core.MapPoint value) =>
        new(
            Math.Clamp(NormalizeComponent(value.Left, 0.5d), 0d, 1d),
            Math.Clamp(NormalizeComponent(value.Top, 0.5d), 0d, 1d));

    private static double NormalizeComponent(double value, double fallback = 0d) =>
        double.IsFinite(value) ? value : fallback;

    private static double NormalizeDelta(double value) => double.IsFinite(value) ? value : 0d;

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0d;
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
