using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

/// <summary>Loads the pinned, build-time map snapshot. Runtime never calls the provider.</summary>
internal static class GatewayStaticMapLayerCatalog
{
    private const string ResourceSuffix = ".Assets.GatewayMapLayers.json";
    private const int SupportedSchemaVersion = 2;
    private const int LegacySchemaVersion = 1;
    private const int MaximumPolygonPointCount = 4096;
    private const int MaximumZoneCount = 512;
    private const int MaximumResourceCount = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    private static readonly Lazy<GatewayStaticMapLayers> BundledCatalog = new(
        LoadBundledCore,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static GatewayStaticMapLayers LoadBundled() => BundledCatalog.Value;

    private static GatewayStaticMapLayers LoadBundledCore()
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
        if (!string.Equals(document.MapId, "gateway", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Gateway map-layer map ID is unsupported.");
        if (document.SchemaVersion == SupportedSchemaVersion
            && !string.Equals(document.CoordinateSpace, "normalized-gateway", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Gateway map-layer coordinate space is unsupported.");
        }
        return document.SchemaVersion switch
        {
            SupportedSchemaVersion => LoadV2(document),
            LegacySchemaVersion => LoadV1(document),
            _ => throw new InvalidDataException("Gateway map-layer schema is unsupported.")
        };
    }

    private static GatewayStaticMapLayers LoadV2(GatewayMapLayerDocument document)
    {
        var zones = ValidateZones(document.Zones ?? []);
        var aiSpawnZones = ValidateAiSpawnZones(document.AiSpawnZones ?? []);
        var routes = ValidateRoutes(document.Routes ?? []);
        var waterLabels = ValidateWaterLabels(document.WaterLabels ?? []);
        var resources = ValidateResources(document.Resources ?? []);
        EnsureUniqueIds(
            zones.Select(item => item.Id)
                .Concat(aiSpawnZones.Select(item => item.Id))
                .Concat(routes.Select(item => item.Id))
                .Concat(waterLabels.Select(item => item.Id))
                .Concat(resources.Select(item => item.Id)));
        var provenance = document.Provenance is null
            ? GatewayMapProvenance.Empty
            : new GatewayMapProvenance(
                document.Provenance.Provider ?? "myislemap.com",
                document.Provenance.SnapshotMode ?? "build-time-offline",
                document.Provenance.RetrievedAt,
                document.Provenance.Counts ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        return new GatewayStaticMapLayers(
            zones,
            aiSpawnZones,
            routes,
            waterLabels,
            resources,
            ValidateIcons(document.Icons ?? []),
            ValidateDefaults(document.Defaults),
            provenance,
            []);
    }

    private static GatewayStaticMapLayers LoadV1(GatewayMapLayerDocument document)
    {
        // Migration compatibility for diagnostics and old local fixtures.
        return new GatewayStaticMapLayers(
            ValidateLegacyZones(document.Zones ?? []), [], [], [], [], [],
            MapLayerDefaults.Website, GatewayMapProvenance.Empty,
            ValidateFoodRegions(document.FoodRegions ?? []));
    }

    private static IReadOnlyList<GatewayStaticMapZone> ValidateZones(IReadOnlyList<GatewayMapZoneAsset> source)
    {
        if (source.Count > MaximumZoneCount) throw new InvalidDataException("Gateway map-layer data contains too many zones.");
        var result = new List<GatewayStaticMapZone>(source.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "zone ID");
            if (!ids.Add(id)) throw new InvalidDataException($"Duplicate Gateway zone ID '{id}'.");
            var kind = item.Kind?.Trim().ToLowerInvariant() switch
            {
                "migration" => MapZoneKind.Migration,
                "patrol" => MapZoneKind.Patrol,
                "sanctuary" => MapZoneKind.Sanctuary,
                _ => throw new InvalidDataException($"Zone '{id}' has an unsupported kind.")
            };
            result.Add(new GatewayStaticMapZone(id, RequiredText(item.Name, "zone name"), kind,
                ValidatePolygon(id, item.Points ?? []), item.GameLabel));
        }
        return result;
    }

    private static IReadOnlyList<GatewayStaticMapZone> ValidateLegacyZones(IReadOnlyList<GatewayMapZoneAsset> source)
    {
        if (source.Count > 128) throw new InvalidDataException("Gateway map-layer data contains too many zones.");
        var result = new List<GatewayStaticMapZone>(source.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "zone ID");
            if (!ids.Add(id)) throw new InvalidDataException($"Duplicate Gateway zone ID '{id}'.");
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
            result.Add(new GatewayStaticMapZone(id, RequiredText(item.Name, "zone name"), kind, points, item.GameLabel));
        }
        return result;
    }

    private static IReadOnlyList<GatewayAiSpawnZone> ValidateAiSpawnZones(IReadOnlyList<GatewayAiSpawnZoneAsset> source)
    {
        if (source.Count > MaximumZoneCount) throw new InvalidDataException("Gateway map-layer data contains too many AI spawn zones.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GatewayAiSpawnZone>(source.Count);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "AI zone ID");
            if (!ids.Add(id)) throw new InvalidDataException($"Duplicate AI zone ID '{id}'.");
            var points = item.Points ?? [];
            if (points.Count is < 1 or > 4096 || points.Any(point => !IsMapPoint(point)))
                throw new InvalidDataException($"AI zone '{id}' has invalid geometry.");
            result.Add(new GatewayAiSpawnZone(id, RequiredText(item.Name, "AI zone name"),
                points.ToArray(), (item.SpeciesKeys ?? [])
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        }
        return result;
    }

    private static IReadOnlyList<GatewayMapRoute> ValidateRoutes(IReadOnlyList<GatewayMapRouteAsset> source)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GatewayMapRoute>(source.Count);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "route ID");
            if (!ids.Add(id)) throw new InvalidDataException($"Duplicate route ID '{id}'.");
            result.Add(new GatewayMapRoute(id, RequiredText(item.Name, "route name"), item.Kind ?? "road",
                ValidatePolygon(id, item.Points ?? [], minimum: 2)));
        }
        return result;
    }

    private static IReadOnlyList<GatewayWaterLabel> ValidateWaterLabels(IReadOnlyList<GatewayWaterLabelAsset> source)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GatewayWaterLabel>(source.Count);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "water label ID");
            if (!ids.Add(id)) throw new InvalidDataException($"Duplicate water label ID '{id}'.");
            result.Add(new GatewayWaterLabel(id, RequiredText(item.Name, "water label name"), ValidatePoint(item.Point, id)));
        }
        return result;
    }

    private static IReadOnlyList<GatewayMapResource> ValidateResources(IReadOnlyList<GatewayMapResourceAsset> source)
    {
        if (source.Count > MaximumResourceCount) throw new InvalidDataException("Gateway map-layer data contains too many resources.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GatewayMapResource>(source.Count);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "resource ID");
            if (!ids.Add(id)) throw new InvalidDataException($"Duplicate resource ID '{id}'.");
            result.Add(new GatewayMapResource(id, RequiredText(item.Category, "resource category").ToLowerInvariant(),
                RequiredText(item.Key, "resource key"), RequiredText(item.Name, "resource name"), item.Group,
                item.IconKey, ValidatePoint(item.Point, id)));
        }
        return result;
    }

    private static IReadOnlyList<GatewayMapIcon> ValidateIcons(IReadOnlyList<GatewayMapIconAsset> source)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GatewayMapIcon>(source.Count);
        foreach (var item in source)
        {
            var key = RequiredText(item.Key, "icon key");
            if (!ids.Add(key)) throw new InvalidDataException($"Duplicate icon key '{key}'.");
            result.Add(new GatewayMapIcon(key, RequiredText(item.SourceUrl, "icon source URL"),
                RequiredText(item.SourceAsset, "icon source asset"), RequiredText(item.RuntimeAsset, "icon runtime asset"), item.SourceSha256));
        }
        return result;
    }

    private static void EnsureUniqueIds(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                throw new InvalidDataException($"Duplicate Gateway map-layer ID '{id}'.");
        }
    }

    private static MapLayerDefaults ValidateDefaults(GatewayMapDefaultsAsset? source) => source is null
        ? MapLayerDefaults.Website
        : new MapLayerDefaults(source.Migration, source.Patrol, source.Sanctuary, source.AiSpawnZones,
            source.Roads, source.Water, source.Animals, source.Plants, source.Earth,
            (source.SelectedResourceKeys ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value.ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<GatewayFoodRegion> ValidateFoodRegions(IReadOnlyList<GatewayFoodRegionAsset> source)
    {
        if (source.Count > 64) throw new InvalidDataException("Gateway map-layer data contains too many food regions.");
        var result = new List<GatewayFoodRegion>(source.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var id = RequiredText(item.Id, "food-region ID");
            if (!ids.Add(id)) throw new InvalidDataException($"Duplicate Gateway food-region ID '{id}'.");
            if (!IsMapPoint(item.Center) || !double.IsFinite(item.RadiusX) || !double.IsFinite(item.RadiusY)
                || item.RadiusX is <= 0d or > 0.25d || item.RadiusY is <= 0d or > 0.25d)
                throw new InvalidDataException($"Food region '{id}' has invalid geometry.");
            var foods = (item.Foods ?? []).Where(food => !string.IsNullOrWhiteSpace(food)).Select(food => food!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
            if (foods.Length == 0) throw new InvalidDataException($"Food region '{id}' does not identify any food.");
            result.Add(new GatewayFoodRegion(id, RequiredText(item.Label, "food-region label"), foods,
                item.Center!.Value, item.RadiusX, item.RadiusY));
        }
        return result;
    }

    private static IReadOnlyList<MapPoint> ValidatePolygon(string id, IReadOnlyList<MapPoint> points, int minimum = 3)
    {
        if (points.Count < minimum || points.Count > MaximumPolygonPointCount || points.Any(point => !IsMapPoint(point)))
            throw new InvalidDataException($"Map layer '{id}' has invalid geometry.");
        if (minimum >= 3)
        {
            var distinct = points.Distinct().ToArray();
            if (distinct.Length < 3 || Math.Abs(SignedArea(distinct)) < 1e-10d)
                throw new InvalidDataException($"Map layer '{id}' has degenerate polygon geometry.");
            return distinct;
        }
        return points.ToArray();
    }

    private static MapPoint ValidatePoint(MapPoint? point, string id) => point is { } value && IsMapPoint(value)
        ? value : throw new InvalidDataException($"Map layer '{id}' has an invalid point.");
    private static bool IsMapPoint(MapPoint? point) => point is { } value && IsMapPoint(value);
    private static bool IsMapPoint(MapPoint point) => double.IsFinite(point.Left) && double.IsFinite(point.Top)
        && point.Left is >= 0d and <= 1d && point.Top is >= 0d and <= 1d;
    private static double SignedArea(IReadOnlyList<MapPoint> points)
    {
        var area = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var current = points[i]; var next = points[(i + 1) % points.Count];
            area += current.Left * next.Top - next.Left * current.Top;
        }
        return area / 2d;
    }
    private static IReadOnlyList<MapPoint> BuildCircle(string id, MapPoint? center, double radius)
    {
        if (center is not { } c || !IsMapPoint(c) || !double.IsFinite(radius) || radius is <= 0d or > 0.25d)
            throw new InvalidDataException($"Zone '{id}' has invalid circle geometry.");
        var points = new MapPoint[48];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = Math.Tau * i / points.Length;
            points[i] = new MapPoint(c.Left + Math.Cos(angle) * radius, c.Top + Math.Sin(angle) * radius);
            if (!IsMapPoint(points[i])) throw new InvalidDataException($"Zone '{id}' extends outside the Gateway map.");
        }
        return points;
    }
    private static string RequiredText(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 256)
            throw new InvalidDataException($"Gateway map-layer {field} is invalid.");
        return trimmed;
    }

    private sealed record GatewayMapLayerDocument
    {
        public int SchemaVersion { get; init; }
        public string? MapId { get; init; }
        public string? CoordinateSpace { get; init; }
        public GatewayMapProvenanceAsset? Provenance { get; init; }
        public GatewayMapDefaultsAsset? Defaults { get; init; }
        public IReadOnlyList<GatewayMapZoneAsset>? Zones { get; init; }
        public IReadOnlyList<GatewayAiSpawnZoneAsset>? AiSpawnZones { get; init; }
        public IReadOnlyList<GatewayMapRouteAsset>? Routes { get; init; }
        public IReadOnlyList<GatewayWaterLabelAsset>? WaterLabels { get; init; }
        public IReadOnlyList<GatewayMapResourceAsset>? Resources { get; init; }
        public IReadOnlyList<GatewayMapIconAsset>? Icons { get; init; }
        public IReadOnlyList<GatewayFoodRegionAsset>? FoodRegions { get; init; }
    }
    private sealed record GatewayMapZoneAsset { public string? Id { get; init; } public string? Name { get; init; } public string? GameLabel { get; init; } public string? Kind { get; init; } public string? Shape { get; init; } public MapPoint? Center { get; init; } public double Radius { get; init; } public IReadOnlyList<MapPoint>? Points { get; init; } }
    private sealed record GatewayAiSpawnZoneAsset { public string? Id { get; init; } public string? Name { get; init; } public string[]? SpeciesKeys { get; init; } public IReadOnlyList<MapPoint>? Points { get; init; } }
    private sealed record GatewayMapRouteAsset { public string? Id { get; init; } public string? Name { get; init; } public string? Kind { get; init; } public IReadOnlyList<MapPoint>? Points { get; init; } }
    private sealed record GatewayWaterLabelAsset { public string? Id { get; init; } public string? Name { get; init; } public MapPoint? Point { get; init; } }
    private sealed record GatewayMapResourceAsset { public string? Id { get; init; } public string? Category { get; init; } public string? Key { get; init; } public string? Name { get; init; } public string? Group { get; init; } public string? IconKey { get; init; } public MapPoint? Point { get; init; } }
    private sealed record GatewayMapIconAsset { public string? Key { get; init; } public string? SourceUrl { get; init; } public string? SourceAsset { get; init; } public string? RuntimeAsset { get; init; } public string? SourceSha256 { get; init; } }
    private sealed record GatewayMapProvenanceAsset { public string? Provider { get; init; } public string? SnapshotMode { get; init; } public DateTimeOffset? RetrievedAt { get; init; } public IReadOnlyDictionary<string, int>? Counts { get; init; } }
    private sealed record GatewayMapDefaultsAsset
    {
        public bool Migration { get; init; } = true; public bool Patrol { get; init; } = true; public bool Sanctuary { get; init; } = true;
        public bool AiSpawnZones { get; init; } public bool Roads { get; init; } = true; public bool Water { get; init; }
        public bool Animals { get; init; } public bool Plants { get; init; } public bool Earth { get; init; }
        public Dictionary<string, string[]>? SelectedResourceKeys { get; init; }
    }
    private sealed record GatewayFoodRegionAsset { public string? Id { get; init; } public string? Label { get; init; } public IReadOnlyList<string?>? Foods { get; init; } public MapPoint? Center { get; init; } public double RadiusX { get; init; } public double RadiusY { get; init; } }
}

internal sealed record GatewayStaticMapLayers(
    IReadOnlyList<GatewayStaticMapZone> Zones,
    IReadOnlyList<GatewayAiSpawnZone> AiSpawnZones,
    IReadOnlyList<GatewayMapRoute> Routes,
    IReadOnlyList<GatewayWaterLabel> WaterLabels,
    IReadOnlyList<GatewayMapResource> Resources,
    IReadOnlyList<GatewayMapIcon> Icons,
    MapLayerDefaults Defaults,
    GatewayMapProvenance Provenance,
    IReadOnlyList<GatewayFoodRegion> FoodRegions)
{
    public static GatewayStaticMapLayers Empty { get; } = new([], [], [], [], [], [], MapLayerDefaults.Website, GatewayMapProvenance.Empty, []);
}

internal readonly record struct GatewayStaticMapZone(string Id, string Name, MapZoneKind Kind, IReadOnlyList<MapPoint> Points, string? GameLabel = null);
internal readonly record struct GatewayAiSpawnZone(string Id, string Name, IReadOnlyList<MapPoint> Points, IReadOnlyList<string> SpeciesKeys);
internal readonly record struct GatewayMapRoute(string Id, string Name, string Kind, IReadOnlyList<MapPoint> Points);
internal readonly record struct GatewayWaterLabel(string Id, string Name, MapPoint Point);
internal readonly record struct GatewayMapResource(string Id, string Category, string Key, string Name, string? Group, string? IconKey, MapPoint Point);
internal readonly record struct GatewayMapIcon(string Key, string SourceUrl, string SourceAsset, string RuntimeAsset, string? SourceSha256);
internal sealed record GatewayMapProvenance(string Provider, string SnapshotMode, DateTimeOffset? RetrievedAt, IReadOnlyDictionary<string, int> Counts)
{ public static GatewayMapProvenance Empty { get; } = new("unknown", "unknown", null, new Dictionary<string, int>()); }
internal sealed record MapLayerDefaults(bool Migration, bool Patrol, bool Sanctuary, bool AiSpawnZones, bool Roads, bool Water, bool Animals, bool Plants, bool Earth, IReadOnlyDictionary<string, IReadOnlySet<string>> SelectedResourceKeys)
{ public static MapLayerDefaults Website { get; } = new(true, true, true, false, true, false, false, false, false, new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)); }
internal readonly record struct GatewayFoodRegion(string Id, string Label, IReadOnlyList<string> Foods, MapPoint Center, double RadiusX, double RadiusY);
