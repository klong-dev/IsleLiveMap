using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class MapLayerPreferencesTests
{
    [Fact]
    public void BundledDefaults_MatchWebsitePresetAndPreselectResourceChildren()
    {
        var layers = GatewayStaticMapLayerCatalog.LoadBundled();
        var preferences = MapLayerPreferences.FromDefaults(layers.Defaults);

        Assert.True(preferences.Migration);
        Assert.True(preferences.Patrol);
        Assert.True(preferences.Sanctuary);
        Assert.True(preferences.Roads);
        Assert.False(preferences.AiSpawnZones);
        Assert.True(preferences.Water);
        Assert.False(preferences.Animals);
        Assert.False(preferences.Plants);
        Assert.False(preferences.Earth);
        Assert.Equal(
            layers.Resources.Select(resource => resource.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            preferences.ResourceKeys.Count);
    }

    [Fact]
    public void Store_RoundTripsFiltersAndDropsUnknownResourceKeys()
    {
        var path = TemporaryPath();
        try
        {
            var layers = GatewayStaticMapLayerCatalog.LoadBundled();
            var store = new MapLayerPreferencesStore(path);
            var preferences = MapLayerPreferences.FromDefaults(layers.Defaults);
            preferences.Animals = true;
            preferences.Roads = false;
            preferences.ResourceKeys.Clear();
            preferences.ResourceKeys.Add("boar");
            preferences.ResourceKeys.Add("not-from-snapshot");

            Assert.True(store.TrySave(preferences, out var error), error);
            var restored = store.Load(layers.Defaults);

            Assert.True(restored.Animals);
            Assert.False(restored.Roads);
            Assert.Contains("boar", restored.ResourceKeys);
            Assert.DoesNotContain("not-from-snapshot", restored.ResourceKeys);
        }
        finally
        {
            DeleteDirectory(path);
        }
    }

    [Fact]
    public void Store_MigratesLegacyZoneAndFoodToggles()
    {
        var path = TemporaryPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "version": 1,
                  "zone": false,
                  "food": true,
                  "heat": true
                }
                """);
            var layers = GatewayStaticMapLayerCatalog.LoadBundled();
            var restored = new MapLayerPreferencesStore(path).Load(layers.Defaults);

            Assert.Equal(MapLayerPreferences.CurrentVersion, restored.Version);
            Assert.False(restored.Migration);
            Assert.False(restored.Patrol);
            Assert.False(restored.Sanctuary);
            Assert.True(restored.Animals);
            Assert.True(restored.Plants);
            Assert.True(restored.Earth);
            Assert.True(restored.Roads);
            Assert.False(restored.AiSpawnZones);
        }
        finally
        {
            DeleteDirectory(path);
        }
    }

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        "IsleLiveMap.Tests",
        Guid.NewGuid().ToString("N"),
        "map-layer-settings.json");

    private static void DeleteDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
