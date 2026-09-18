using System.Text.Json;
using System.IO;

namespace TheIsleOverlay.App;

internal enum MapLayerGroup
{
    Migration,
    Patrol,
    Sanctuary,
    AiSpawnZones,
    Roads,
    Water,
    Animals,
    Plants,
    Earth
}

/// <summary>Versioned local visibility preferences shared by minimap and Alt+M.</summary>
internal sealed class MapLayerPreferences
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public bool Migration { get; set; } = true;
    public bool Patrol { get; set; } = true;
    public bool Sanctuary { get; set; } = true;
    public bool AiSpawnZones { get; set; }
    public bool Roads { get; set; } = true;
    public bool Water { get; set; }
    public bool Animals { get; set; }
    public bool Plants { get; set; }
    public bool Earth { get; set; }
    public HashSet<string> ResourceKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static MapLayerPreferences FromDefaults(MapLayerDefaults defaults)
    {
        var result = new MapLayerPreferences
        {
            Migration = defaults.Migration,
            Patrol = defaults.Patrol,
            Sanctuary = defaults.Sanctuary,
            AiSpawnZones = defaults.AiSpawnZones,
            Roads = defaults.Roads,
            Water = defaults.Water,
            Animals = defaults.Animals,
            Plants = defaults.Plants,
            Earth = defaults.Earth
        };
        foreach (var values in defaults.SelectedResourceKeys.Values)
            result.ResourceKeys.UnionWith(values);
        return result;
    }

    public bool IsEnabled(MapLayerGroup group) => group switch
    {
        MapLayerGroup.Migration => Migration,
        MapLayerGroup.Patrol => Patrol,
        MapLayerGroup.Sanctuary => Sanctuary,
        MapLayerGroup.AiSpawnZones => AiSpawnZones,
        MapLayerGroup.Roads => Roads,
        MapLayerGroup.Water => Water,
        MapLayerGroup.Animals => Animals,
        MapLayerGroup.Plants => Plants,
        MapLayerGroup.Earth => Earth,
        _ => false
    };

    public void SetEnabled(MapLayerGroup group, bool enabled)
    {
        switch (group)
        {
            case MapLayerGroup.Migration: Migration = enabled; break;
            case MapLayerGroup.Patrol: Patrol = enabled; break;
            case MapLayerGroup.Sanctuary: Sanctuary = enabled; break;
            case MapLayerGroup.AiSpawnZones: AiSpawnZones = enabled; break;
            case MapLayerGroup.Roads: Roads = enabled; break;
            case MapLayerGroup.Water: Water = enabled; break;
            case MapLayerGroup.Animals: Animals = enabled; break;
            case MapLayerGroup.Plants: Plants = enabled; break;
            case MapLayerGroup.Earth: Earth = enabled; break;
        }
    }

    public MapLayerPreferences Normalize(MapLayerDefaults defaults)
    {
        Version = CurrentVersion;
        ResourceKeys ??= new(StringComparer.OrdinalIgnoreCase);
        var allowed = defaults.SelectedResourceKeys.Values
            .SelectMany(values => values)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count > 0)
            ResourceKeys.IntersectWith(allowed);
        return this;
    }
}

internal sealed class MapLayerPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _path;

    public MapLayerPreferencesStore(string? path = null)
    {
        var overridePath = Environment.GetEnvironmentVariable("ISLELIVEMAP_MAP_LAYER_SETTINGS_PATH");
        _path = System.IO.Path.GetFullPath(path
            ?? (string.IsNullOrWhiteSpace(overridePath) ? AppPaths.MapLayerSettings : overridePath));
    }

    public string Path => _path;

    public MapLayerPreferences Load(MapLayerDefaults defaults)
    {
        try
        {
            if (!File.Exists(_path)) return MapLayerPreferences.FromDefaults(defaults);
            var json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);
            if (IsLegacy(document.RootElement))
                return MigrateLegacy(document.RootElement, defaults);
            var preferences = JsonSerializer.Deserialize<MapLayerPreferences>(json, Options)
                              ?? MapLayerPreferences.FromDefaults(defaults);
            return preferences.Normalize(defaults);
        }
        catch (Exception) when (File.Exists(_path))
        {
            return MapLayerPreferences.FromDefaults(defaults);
        }
    }

    private static bool IsLegacy(JsonElement root)
    {
        if (TryProperty(root, "version", out var version)
            && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out var value))
        {
            return value < MapLayerPreferences.CurrentVersion;
        }

        return !TryProperty(root, "migration", out _)
               && (TryProperty(root, "zone", out _)
                   || TryProperty(root, "zones", out _)
                   || TryProperty(root, "food", out _)
                   || TryProperty(root, "heat", out _));
    }

    private static MapLayerPreferences MigrateLegacy(JsonElement root, MapLayerDefaults defaults)
    {
        var migrated = MapLayerPreferences.FromDefaults(defaults);
        var zones = LegacyBoolean(root, defaults.Migration, "zone", "zones", "zoneEnabled", "showZones");
        var food = LegacyBoolean(root, defaults.Animals, "food", "foodEnabled", "showFood");
        migrated.Migration = zones;
        migrated.Patrol = zones;
        migrated.Sanctuary = zones;
        migrated.Animals = food;
        migrated.Plants = food;
        migrated.Earth = food;
        // Legacy heatmap was dynamic provider data and intentionally has no
        // offline-layer equivalent. AI, roads and water retain snapshot defaults.
        return migrated.Normalize(defaults);
    }

    private static bool LegacyBoolean(JsonElement root, bool fallback, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(root, name, out var property)
                && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return property.GetBoolean();
            }
        }
        return fallback;
    }

    private static bool TryProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    public bool TrySave(MapLayerPreferences preferences, out string? error)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options));
            File.Move(temporary, _path, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
