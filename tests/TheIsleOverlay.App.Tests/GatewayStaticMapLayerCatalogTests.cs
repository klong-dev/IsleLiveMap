using System.IO;
using System.Text;
using TheIsleOverlay.App;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class GatewayStaticMapLayerCatalogTests
{
    [Fact]
    public void LoadBundled_ContainsPinnedOfflineSnapshot()
    {
        var layers = GatewayStaticMapLayerCatalog.LoadBundled();

        Assert.Equal(80, layers.Zones.Count);
        Assert.Equal(12, layers.Zones.Count(zone => zone.Kind == MapZoneKind.Migration));
        Assert.Equal(61, layers.Zones.Count(zone => zone.Kind == MapZoneKind.Patrol));
        Assert.Equal(7, layers.Zones.Count(zone => zone.Kind == MapZoneKind.Sanctuary));
        Assert.Contains(layers.Zones, zone =>
            zone.Kind == MapZoneKind.Migration && zone.Name == "Swamp");
        Assert.Contains(layers.Zones, zone =>
            zone.Kind == MapZoneKind.Patrol && zone.Name == "Swamps");
        Assert.Equal(52, layers.AiSpawnZones.Count);
        Assert.Equal(32, layers.Routes.Count);
        Assert.Equal(876, layers.Routes.Sum(route => route.Points.Count));
        Assert.Equal(28, layers.WaterLabels.Count);
        Assert.Equal(953, layers.Resources.Count);
        Assert.Equal(430, layers.Resources.Count(resource => resource.Category == "animals"));
        Assert.Equal(245, layers.Resources.Count(resource => resource.Category == "plants"));
        Assert.Equal(278, layers.Resources.Count(resource => resource.Category == "earth"));
        Assert.Equal(35, layers.Icons.Count);
        var iconKeys = layers.Icons
            .Select(icon => icon.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(layers.Resources, resource =>
        {
            Assert.False(string.IsNullOrWhiteSpace(resource.IconKey));
            Assert.Contains(resource.IconKey!, iconKeys);
        });
        Assert.Equal("myislemap.com", layers.Provenance.Provider);
        Assert.Equal("build-time-offline", layers.Provenance.SnapshotMode);
        Assert.Equal(12, layers.Provenance.Counts["migrationZones"]);
        Assert.Equal(876, layers.Provenance.Counts["routePoints"]);
        Assert.All(layers.Resources, resource =>
        {
            Assert.InRange(resource.Point.Left, 0d, 1d);
            Assert.InRange(resource.Point.Top, 0d, 1d);
        });
    }

    [Fact]
    public void Load_ConvertsCircleZoneToStablePolygon()
    {
        using var stream = Json("""
        {
          "schemaVersion": 1,
          "mapId": "gateway",
          "zones": [
            {
              "id": "delta",
              "name": "Delta",
              "kind": "migration",
              "shape": "circle",
              "center": { "left": 0.5, "top": 0.5 },
              "radius": 0.1
            }
          ],
          "foodRegions": []
        }
        """);

        var zone = Assert.Single(GatewayStaticMapLayerCatalog.Load(stream).Zones);

        Assert.Equal(MapZoneKind.Migration, zone.Kind);
        Assert.Equal(48, zone.Points.Count);
        Assert.All(zone.Points, point =>
        {
            Assert.InRange(point.Left, 0d, 1d);
            Assert.InRange(point.Top, 0d, 1d);
        });
    }

    [Fact]
    public void Load_RejectsOffMapPolygonInsteadOfClampingIt()
    {
        using var stream = Json("""
        {
          "schemaVersion": 1,
          "mapId": "gateway",
          "zones": [
            {
              "id": "bad",
              "name": "Bad",
              "kind": "patrol",
              "shape": "polygon",
              "points": [
                { "left": 0.2, "top": 0.2 },
                { "left": 1.2, "top": 0.2 },
                { "left": 0.2, "top": 0.8 }
              ]
            }
          ],
          "foodRegions": []
        }
        """);

        Assert.Throws<InvalidDataException>(() =>
            GatewayStaticMapLayerCatalog.Load(stream));
    }

    private static MemoryStream Json(string value) =>
        new(Encoding.UTF8.GetBytes(value));
}
