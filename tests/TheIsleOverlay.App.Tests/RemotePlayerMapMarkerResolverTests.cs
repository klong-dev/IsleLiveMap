using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class RemotePlayerMapMarkerResolverTests
{
    [Fact]
    public void Resolve_AssignsRequestedPlayerAndAiCategories()
    {
        var local = new PlayerTelemetry
        {
            Class = "BP_Tyrannosaurus_C",
            Location = new WorldLocation { X = 51_000, Y = -49_000 }
        };
        var map = new MapTelemetry
        {
            Markers =
            [
                Entity(
                    "pro-entity:player:carno",
                    "Carno 1.4T",
                    RemoteEntityKind.Player,
                    "carnotaurus",
                    CreatureDiet.Carnivore,
                    location: new WorldLocation { X = 162_200, Y = 62_600 }),
                Entity(
                    "pro-entity:player:rex",
                    "T-Rex 2.3T",
                    RemoteEntityKind.Player,
                    "tyrannosaurus",
                    CreatureDiet.Carnivore,
                    point: new MapPoint(0.25, 0.75)),
                Entity(
                    "pro-entity:player:trice",
                    "Trice 200K",
                    RemoteEntityKind.Player,
                    "triceratops",
                    CreatureDiet.Herbivore,
                    point: new MapPoint(0.35, 0.65)),
                Entity(
                    "pro-entity:ai:fish",
                    "Fish 12K",
                    RemoteEntityKind.Ai,
                    "fish",
                    CreatureDiet.Unknown,
                    point: new MapPoint(0.45, 0.55)),
                new MapMarkerTelemetry
                {
                    SteamId = "provider-player",
                    Label = "Provider marker",
                    MapLocation = new MapPoint(0.2, 0.2)
                }
            ]
        };

        var result = RemotePlayerMapMarkerResolver.Resolve(map, local);

        Assert.Collection(
            result,
            marker =>
            {
                Assert.Equal("Carno 1.4T", marker.Label);
                Assert.Equal(RemoteEntityMapCategory.OtherCarnivore, marker.Category);
                Assert.Equal(0.6, marker.Point.Left, precision: 10);
                Assert.Equal(0.6, marker.Point.Top, precision: 10);
            },
            marker => Assert.Equal(RemoteEntityMapCategory.SameSpecies, marker.Category),
            marker => Assert.Equal(RemoteEntityMapCategory.OtherHerbivore, marker.Category),
            marker =>
            {
                Assert.Equal(RemoteEntityMapCategory.Ai, marker.Category);
                Assert.Equal(RemoteEntityKind.Ai, marker.EntityKind);
            });
    }

    [Fact]
    public void Resolve_UsesNeutralCategoryWhenPlayerSpeciesCannotBeColoredSafely()
    {
        var map = new MapTelemetry
        {
            Markers =
            [
                Entity(
                    "pro-entity:player:galli",
                    "Galli 300K",
                    RemoteEntityKind.Player,
                    "gallimimus",
                    CreatureDiet.Omnivore,
                    point: new MapPoint(0.2, 0.3)),
                Entity(
                    "pro-entity:player:unknown",
                    "Player ?",
                    RemoteEntityKind.Player,
                    "",
                    CreatureDiet.Unknown,
                    point: new MapPoint(0.4, 0.5))
            ]
        };

        var result = RemotePlayerMapMarkerResolver.Resolve(map, localPlayer: null);

        Assert.Equal(2, result.Count);
        Assert.All(
            result,
            marker => Assert.Equal(
                RemoteEntityMapCategory.UnclassifiedPlayer,
                marker.Category));
        Assert.Equal("Player ?", result[1].Label);
    }

    [Fact]
    public void Resolve_ExcludesSelfFlagAndMarkerAtLocalPoint()
    {
        var local = new PlayerTelemetry
        {
            Class = "Carnotaurus",
            MapLocation = new MapPoint(0.4, 0.6)
        };
        var explicitSelf = Entity(
            "pro-entity:player:self",
            "Carno 1T",
            RemoteEntityKind.Player,
            "carnotaurus",
            CreatureDiet.Carnivore,
            point: new MapPoint(0.2, 0.3)) with { Self = true };
        var map = new MapTelemetry
        {
            Markers =
            [
                explicitSelf,
                Entity(
                    "pro-entity:player:legacy-self",
                    "Carno 1T",
                    RemoteEntityKind.Player,
                    "carnotaurus",
                    CreatureDiet.Carnivore,
                    point: new MapPoint(0.4, 0.6)),
                Entity(
                    "pro-entity:player:remote",
                    "Carno 1.2T",
                    RemoteEntityKind.Player,
                    "carnotaurus",
                    CreatureDiet.Carnivore,
                    point: new MapPoint(0.7, 0.8))
            ]
        };

        var marker = Assert.Single(RemotePlayerMapMarkerResolver.Resolve(map, local));

        Assert.Equal("Carno 1.2T", marker.Label);
        Assert.Equal(RemoteEntityMapCategory.SameSpecies, marker.Category);
    }

    [Fact]
    public void Resolve_GivesDuplicateTracksDistinctStableKeys()
    {
        var map = new MapTelemetry
        {
            Markers =
            [
                Entity(
                    "pro-entity:ai:duplicate",
                    "Fish",
                    RemoteEntityKind.Ai,
                    "fish",
                    CreatureDiet.Unknown,
                    point: new MapPoint(0.1, 0.2)),
                Entity(
                    "pro-entity:ai:duplicate",
                    "Fish",
                    RemoteEntityKind.Ai,
                    "fish",
                    CreatureDiet.Unknown,
                    point: new MapPoint(0.3, 0.4))
            ]
        };

        var result = RemotePlayerMapMarkerResolver.Resolve(map, localPlayer: null);

        Assert.Equal(2, result.Count);
        Assert.NotEqual(result[0].Key, result[1].Key);
    }

    private static MapMarkerTelemetry Entity(
        string id,
        string label,
        RemoteEntityKind kind,
        string speciesId,
        CreatureDiet diet,
        WorldLocation? location = null,
        MapPoint? point = null) => new()
    {
        SteamId = id,
        Label = label,
        Location = location,
        MapLocation = point,
        ProEntityKind = kind,
        CreatureSpeciesId = speciesId,
        CreatureSpeciesShortName = label.Split(' ')[0],
        ProCreatureDiet = diet
    };
}
