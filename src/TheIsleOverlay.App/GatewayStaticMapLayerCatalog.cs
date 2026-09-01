using System.Reflection;
using System.IO;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal static class GatewayStaticMapLayerCatalog
{
    private const string ResourceSuffix = ".Assets.GatewayMapLayers.json";
    private const int SupportedSchemaVersion = 1;
    private const int MaximumZoneCount = 128;
    private const int MaximumFoodRegionCount = 64;
    private const int MaximumPolygonPointCount = 256;
    private const int CircleSegmentCount = 48;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static GatewayStaticMapLayers LoadBundled()
    {
        var assembly = typeof(GatewayStaticMapLayerCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames().SingleOrDefault(name =>
            name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Bundled Gateway map-layer data was not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("Bundled Gateway map-layer data could not be opened.");
        return Load(stream);
    }

    public static GatewayStaticMapLayers Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = JsonSerializer.Deserialize<GatewayMapLayerDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("Gateway map-layer data was empty.");
        if (document.SchemaVersion != SupportedSchemaVersion
            || !string.Equals(document.MapId, "gateway", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Gateway map-layer schema or map ID is unsupported.");
        }

        var zones = ValidateZones(document.Zones ?? []);
        var foodRegions = ValidateFoodRegions(document.FoodRegions ?? []);
        return new GatewayStaticMapLayers(zones, foodRegions);
    }

    private static IReadOnlyList<GatewayStaticMapZone> ValidateZones(
        IReadOnlyList<GatewayMapZoneAsset> source)
    {
        if (source.Count > MaximumZoneCount)
        {
            throw new InvalidDataException("Gateway map-layer data contains too many zones.");
        }

        var result = new List<GatewayStaticMapZone>(source.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "zone ID");
            var name = RequiredText(item.Name, "zone name");
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"Duplicate Gateway zone ID '{id}'.");
            }

            var kind = item.Kind?.Trim().ToLowerInvariant() switch
            {
                "migration" => MapZoneKind.Migration,
                "patrol" => MapZoneKind.Patrol,
                _ => throw new InvalidDataException($"Zone '{id}' has an unsupported kind.")
            };
            var points = item.Shape?.Trim().ToLowerInvariant() switch
            {
                "polygon" => ValidatePolygon(id, item.Points ?? []),
                "circle" => BuildCircle(id, item.Center, item.Radius),
                _ => throw new InvalidDataException($"Zone '{id}' has an unsupported shape.")
            };
            result.Add(new GatewayStaticMapZone(id, name, kind, points));
        }

        return result;
    }

    private static IReadOnlyList<GatewayFoodRegion> ValidateFoodRegions(
        IReadOnlyList<GatewayFoodRegionAsset> source)
    {
        if (source.Count > MaximumFoodRegionCount)
        {
            throw new InvalidDataException("Gateway map-layer data contains too many food regions.");
        }

        var result = new List<GatewayFoodRegion>(source.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "food-region ID");
            var label = RequiredText(item.Label, "food-region label");
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"Duplicate Gateway food-region ID '{id}'.");
            }

            if (!IsMapPoint(item.Center)
                || !double.IsFinite(item.RadiusX)
                || !double.IsFinite(item.RadiusY)
                || item.RadiusX is <= 0d or > 0.25d
                || item.RadiusY is <= 0d or > 0.25d)
            {
                throw new InvalidDataException($"Food region '{id}' has invalid geometry.");
            }

            var foods = (item.Foods ?? [])
                .Select(food => food?.Trim())
                .Where(food => !string.IsNullOrWhiteSpace(food))
                .Select(food => food!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToArray();
            if (foods.Length == 0)
            {
                throw new InvalidDataException($"Food region '{id}' does not identify any food.");
            }

            result.Add(new GatewayFoodRegion(
                id,
                label,
                foods,
                item.Center!.Value,
                item.RadiusX,
                item.RadiusY));
        }

        return result;
    }

    private static IReadOnlyList<MapPoint> ValidatePolygon(
        string id,
        IReadOnlyList<MapPoint> points)
    {
        if (points.Count is < 3 or > MaximumPolygonPointCount
            || points.Any(point => !IsMapPoint(point)))
        {
            throw new InvalidDataException($"Zone '{id}' has invalid polygon geometry.");
        }

        var distinct = points.Distinct().ToArray();
        if (distinct.Length < 3 || Math.Abs(SignedArea(distinct)) < 1e-10d)
        {
            throw new InvalidDataException($"Zone '{id}' has degenerate polygon geometry.");
        }

        return distinct;
    }

    private static IReadOnlyList<MapPoint> BuildCircle(
        string id,
        MapPoint? center,
        double radius)
    {
        if (center is not { } resolvedCenter
            || !IsMapPoint(resolvedCenter)
            || !double.IsFinite(radius)
            || radius is <= 0d or > 0.25d)
        {
            throw new InvalidDataException($"Zone '{id}' has invalid circle geometry.");
        }

        var result = new MapPoint[CircleSegmentCount];
        for (var index = 0; index < result.Length; index++)
        {
            var angle = Math.Tau * index / result.Length;
            var point = new MapPoint(
                resolvedCenter.Left + Math.Cos(angle) * radius,
                resolvedCenter.Top + Math.Sin(angle) * radius);
            if (!IsMapPoint(point))
            {
                throw new InvalidDataException($"Zone '{id}' extends outside the Gateway map.");
            }

            result[index] = point;
        }

        return result;
    }

    private static string RequiredText(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 160)
        {
            throw new InvalidDataException($"Gateway map-layer {field} is invalid.");
        }

        return trimmed;
    }

    private static bool IsMapPoint(MapPoint? point) =>
        point is { } value && IsMapPoint(value);

    private static bool IsMapPoint(MapPoint point) =>
        double.IsFinite(point.Left)
        && double.IsFinite(point.Top)
        && point.Left is >= 0d and <= 1d
        && point.Top is >= 0d and <= 1d;

    private static double SignedArea(IReadOnlyList<MapPoint> points)
    {
        var twiceArea = 0d;
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            twiceArea += current.Left * next.Top - next.Left * current.Top;
        }

        return twiceArea / 2d;
    }

    private sealed record GatewayMapLayerDocument
    {
        public int SchemaVersion { get; init; }
        public string? MapId { get; init; }
        public IReadOnlyList<GatewayMapZoneAsset>? Zones { get; init; }
        public IReadOnlyList<GatewayFoodRegionAsset>? FoodRegions { get; init; }
    }

    private sealed record GatewayMapZoneAsset
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Kind { get; init; }
        public string? Shape { get; init; }
        public MapPoint? Center { get; init; }
        public double Radius { get; init; }
        public IReadOnlyList<MapPoint>? Points { get; init; }
    }

    private sealed record GatewayFoodRegionAsset
    {
        public string? Id { get; init; }
        public string? Label { get; init; }
        public IReadOnlyList<string?>? Foods { get; init; }
        public MapPoint? Center { get; init; }
        public double RadiusX { get; init; }
        public double RadiusY { get; init; }
    }
}

internal sealed record GatewayStaticMapLayers(
    IReadOnlyList<GatewayStaticMapZone> Zones,
    IReadOnlyList<GatewayFoodRegion> FoodRegions)
{
    public static GatewayStaticMapLayers Empty { get; } = new([], []);
}

internal readonly record struct GatewayStaticMapZone(
    string Id,
    string Name,
    MapZoneKind Kind,
    IReadOnlyList<MapPoint> Points);

internal readonly record struct GatewayFoodRegion(
    string Id,
    string Label,
    IReadOnlyList<string> Foods,
    MapPoint Center,
    double RadiusX,
    double RadiusY);
