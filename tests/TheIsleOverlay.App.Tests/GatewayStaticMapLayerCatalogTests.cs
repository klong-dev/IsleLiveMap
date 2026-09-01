using System.IO;
using System.Text;
using TheIsleOverlay.App;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class GatewayStaticMapLayerCatalogTests
{
    [Fact]
    public void LoadBundled_ContainsCalibratedFoodRegions()
    {
        var layers = GatewayStaticMapLayerCatalog.LoadBundled();

        Assert.Equal(34, layers.Zones.Count);
        Assert.Equal(6, layers.Zones.Count(zone => zone.Kind == MapZoneKind.Migration));
        Assert.Equal(28, layers.Zones.Count(zone => zone.Kind == MapZoneKind.Patrol));
        Assert.Contains(layers.Zones, zone =>
            zone.Kind == MapZoneKind.Migration && zone.Name == "Swamp");
        Assert.Contains(layers.Zones, zone =>
            zone.Kind == MapZoneKind.Patrol && zone.Name == "Swamps");
        Assert.Equal(13, layers.FoodRegions.Count);
        Assert.Contains(layers.FoodRegions, region =>
            region.Id == "central-mixed"
            && region.Foods.SequenceEqual(["Heo", "Nai", "Dê", "Gà"]));
        Assert.All(layers.FoodRegions, region =>
        {
            Assert.InRange(region.Center.Left, 0d, 1d);
            Assert.InRange(region.Center.Top, 0d, 1d);
            Assert.InRange(region.RadiusX, 0.001d, 0.25d);
            Assert.InRange(region.RadiusY, 0.001d, 0.25d);
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
