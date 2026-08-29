using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class LocalPositionSnapshotMergerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");

    [Fact]
    public void Merge_UsesLocalPositionWhenRemoteServerIsUnsupported()
    {
        var remote = new TelemetrySnapshot
        {
            Source = "ISLEPILOT",
            SessionState = TelemetrySessionState.UnsupportedServer,
            StatusMessage = "Unsupported"
        };
        var local = Observation(123_456.78d, -234_567.89d, 20_000d, 157.37d);

        var merged = LocalPositionSnapshotMerger.Merge(remote, local, Now);

        Assert.True(merged.Success);
        Assert.True(merged.PlayerOnline);
        Assert.Equal(TelemetrySessionState.Live, merged.SessionState);
        Assert.Equal(123_456.78d, merged.Player?.Location?.X);
        Assert.Equal(-234_567.89d, merged.Player?.Location?.Y);
        Assert.Equal(247.37d, merged.Player!.ExactMapHeadingDegrees!.Value, precision: 6);
        Assert.Equal("171.232.64.234:7777", merged.Player?.Server);
    }

    [Fact]
    public void Merge_PreservesIslePilotVitalsAndOverridesOnlyPositionFields()
    {
        var providerVitals = new ExactVitals { Health = 825, MaxHealth = 1_000 };
        var prime = new PrimeTelemetry
        {
            Quests = [new PrimeQuestTelemetry { Name = "Survive", Done = false }]
        };
        var remote = new TelemetrySnapshot
        {
            Source = "ERA",
            Success = true,
            ServerOnline = true,
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                Name = "Player",
                Class = "Pteranodon",
                Server = "ERA",
                GrowthPercent = 40,
                HealthPercent = 82.5,
                ExactVitals = providerVitals,
                Nutrition = new NutritionTelemetry { Carb = 2 },
                Prime = prime,
                Location = new WorldLocation { X = 1, Y = 2, Z = 3 }
            },
            SessionState = TelemetrySessionState.Live
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            Observation(100, 200, 300, 45),
            Now);

        Assert.Same(providerVitals, merged.Player?.ExactVitals);
        Assert.Equal(40, merged.Player?.GrowthPercent);
        Assert.Equal(82.5, merged.Player?.HealthPercent);
        Assert.Equal(2, merged.Player?.Nutrition?.Carb);
        Assert.Same(prime, merged.Player?.Prime);
        Assert.Equal("Player", merged.Player?.Name);
        Assert.Equal("ERA", merged.Player?.Server);
        Assert.Equal(100, merged.Player?.Location?.X);
        Assert.Null(merged.Player?.MapLocation);
        Assert.Equal(135, merged.Player?.ExactMapHeadingDegrees);
    }

    [Fact]
    public void Merge_KeepsProviderVitalsWhileDirectGpsIsNotReady()
    {
        var remote = new TelemetrySnapshot
        {
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                ExactVitals = new ExactVitals { Health = 8, MaxHealth = 10 },
                HealthPercent = 80,
                GrowthPercent = 30,
                Prime = new PrimeTelemetry { Done = 1, Required = 3 }
            }
        };

        var merged = LocalPositionSnapshotMerger.Merge(remote, null, Now);

        Assert.Equal(8, merged.Player?.ExactVitals?.Health);
        Assert.Equal(80, merged.Player?.HealthPercent);
        Assert.Equal(30, merged.Player?.GrowthPercent);
        Assert.Equal(1, merged.Player?.Prime?.Done);
    }

    [Fact]
    public void Merge_IgnoresInboundGameVitalsAndKeepsIslePilotVitals()
    {
        var islePilotVitals = new ExactVitals
        {
            Health = 10.9,
            MaxHealth = 10.9,
            Stamina = 318,
            MaxStamina = 318
        };
        var inboundVitals = new ExactVitals
        {
            Health = 1,
            MaxHealth = 100,
            Stamina = 2,
            MaxStamina = 200
        };
        var remote = new TelemetrySnapshot
        {
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                ExactVitals = islePilotVitals,
                ExactVitalsSource = "IslePilotOverlayV2"
            }
        };
        var local = Observation(100, 200, 300, 45) with
        {
            DinosaurVitals = new LocalDinosaurVitalsObservation(Now, inboundVitals, 42)
        };

        var merged = LocalPositionSnapshotMerger.Merge(remote, local, Now);

        Assert.Same(islePilotVitals, merged.Player?.ExactVitals);
        Assert.Equal("IslePilotOverlayV2", merged.Player?.ExactVitalsSource);
    }

    [Fact]
    public void Merge_DoesNotExposeInboundGameVitalsWithoutIslePilotData()
    {
        var local = Observation(100, 200, 300, 45) with
        {
            DinosaurVitals = new LocalDinosaurVitalsObservation(
                Now,
                new ExactVitals { Health = 75, MaxHealth = 100 },
                42)
        };

        var merged = LocalPositionSnapshotMerger.Merge(null, local, Now);

        Assert.Null(merged.Player?.ExactVitals);
        Assert.Null(merged.Player?.ExactVitalsSource);
    }

    [Fact]
    public void Merge_UsesPacketVerifiedLocalSpeciesForPlayerClassification()
    {
        var remote = new TelemetrySnapshot
        {
            Player = new PlayerTelemetry
            {
                Class = "stale-provider-species"
            }
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            Observation(100, 200, 300, 45),
            Now,
            verifiedLocalSpeciesId: "carnotaurus");

        Assert.Equal("carnotaurus", merged.Player?.Class);
    }

    [Fact]
    public void Merge_IgnoresExpiredLocalPosition()
    {
        var remote = new TelemetrySnapshot
        {
            Success = true,
            ServerOnline = true,
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                Location = new WorldLocation { X = 10, Y = 20 }
            },
            SessionState = TelemetrySessionState.Live
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            Observation(100, 200, 300, 45) with
            {
                ObservedAt = Now - LocalPositionSnapshotMerger.LocalFreshness - TimeSpan.FromMilliseconds(1)
            },
            Now);

        Assert.Same(remote, merged);
    }

    [Fact]
    public void Merge_UsesFreshVerifiedProFrameWhenOutboundLocalPositionIsUnavailable()
    {
        var frame = RemoteFrame(
            Now,
            98_700,
            -247_700,
            27_800,
            312.5,
            "115.72.226.156:7777");

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            null,
            Now,
            verifiedLocalFallback: frame);

        Assert.True(merged.PlayerOnline);
        Assert.Equal(TelemetrySessionState.Live, merged.SessionState);
        Assert.Equal(98_700, merged.Player?.Location?.X);
        Assert.Equal(-247_700, merged.Player?.Location?.Y);
        Assert.Equal(312.5, merged.Player?.ExactMapHeadingDegrees);
        Assert.Equal("115.72.226.156:7777", merged.Player?.Server);
        Assert.Equal(Now, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_DoesNotUseExpiredVerifiedProFrameAsLocalPosition()
    {
        var remote = new TelemetrySnapshot
        {
            Success = true,
            PlayerOnline = false,
            SessionState = TelemetrySessionState.Connecting
        };
        var frame = RemoteFrame(
            Now - LocalPositionSnapshotMerger.RemotePlayerFreshness - TimeSpan.FromMilliseconds(1),
            98_700,
            -247_700,
            27_800,
            312.5,
            "115.72.226.156:7777");

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            null,
            Now,
            verifiedLocalFallback: frame);

        Assert.Same(remote, merged);
    }

    [Fact]
    public void Merge_AddsInboundRemotePlayersWithoutDroppingProviderMapData()
    {
        var pointOfInterest = new MapPointOfInterestTelemetry { Id = "water" };
        var providerMarker = new MapMarkerTelemetry
        {
            SteamId = "provider-player",
            Location = new WorldLocation { X = 10, Y = 20 }
        };
        var remote = new TelemetrySnapshot
        {
            Map = new MapTelemetry
            {
                Markers = [providerMarker],
                PointsOfInterest = [pointOfInterest]
            }
        };
        var local = Observation(12_000, -67_500, 1_200, 45);
        VerifiedRemoteEntityTelemetry[] remotePlayers =
        [
            new VerifiedRemoteEntityTelemetry(
                7,
                RemoteEntityKind.Player,
                "dorimekhang8",
                "tyrannosaurus",
                "T-Rex",
                CreatureDiet.Carnivore,
                2_300,
                new WorldLocation { X = 12_345, Y = -67_890, Z = 1_234 },
                250,
                9,
                Now)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            local,
            Now,
            remotePlayers: remotePlayers);

        Assert.NotNull(merged.Map);
        Assert.Same(pointOfInterest, Assert.Single(merged.Map.PointsOfInterest));
        Assert.Equal(2, merged.Map.Markers.Count);
        Assert.Same(providerMarker, merged.Map.Markers[0]);
        var inbound = merged.Map.Markers[1];
        Assert.Equal("pro-entity:player:7", inbound.SteamId);
        Assert.Equal("T-Rex 2.3T", inbound.Label);
        Assert.DoesNotContain("dorimekhang8", inbound.Label);
        Assert.Equal(RemoteEntityKind.Player, inbound.ProEntityKind);
        Assert.Equal("tyrannosaurus", inbound.CreatureSpeciesId);
        Assert.False(inbound.Self);
        Assert.Equal(12_345, inbound.Location?.X);
    }

    [Fact]
    public void Merge_DoesNotPresentUnnamedMovingActorsAsPlayers()
    {
        var providerMarker = new MapMarkerTelemetry
        {
            SteamId = "provider-player",
            Label = "Known provider player",
            Location = new WorldLocation { X = 10, Y = 20 }
        };
        var remote = new TelemetrySnapshot
        {
            Map = new MapTelemetry { Markers = [providerMarker] }
        };
        VerifiedRemoteEntityTelemetry[] remotePlayers =
        [
            new VerifiedRemoteEntityTelemetry(
                7,
                RemoteEntityKind.Player,
                "",
                "tyrannosaurus",
                "T-Rex",
                CreatureDiet.Carnivore,
                2_300,
                new WorldLocation { X = 12_345, Y = -67_890, Z = 1_234 },
                250,
                9,
                Now),
            new VerifiedRemoteEntityTelemetry(
                8,
                RemoteEntityKind.Player,
                "   ",
                "triceratops",
                "Trice",
                CreatureDiet.Herbivore,
                1_500,
                new WorldLocation { X = 22_345, Y = -57_890, Z = 1_234 },
                350,
                9,
                Now)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            Observation(100, 200, 300, 45),
            Now,
            remotePlayers: remotePlayers);

        Assert.Same(providerMarker, Assert.Single(merged.Map!.Markers));
    }

    [Fact]
    public void Merge_PresentsNamedPlayerWhileSpeciesMetadataIsPending()
    {
        VerifiedRemoteEntityTelemetry[] remotePlayers =
        [
            new VerifiedRemoteEntityTelemetry(
                71436,
                RemoteEntityKind.Player,
                "internal-proof-name",
                "",
                "",
                CreatureDiet.Unknown,
                null,
                new WorldLocation { X = 89_280, Y = -277_806, Z = 28_145 },
                270.5,
                66,
                Now)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            Observation(80_548, -252_203, 28_061, 45),
            Now,
            remotePlayers: remotePlayers);

        var marker = Assert.Single(merged.Map!.Markers);
        Assert.Equal("pro-entity:player:71436", marker.SteamId);
        Assert.Equal("Player ?", marker.Label);
        Assert.DoesNotContain("internal-proof-name", marker.Label);
        Assert.Equal(RemoteEntityKind.Player, marker.ProEntityKind);
        Assert.Equal(string.Empty, marker.CreatureSpeciesId);
    }

    [Fact]
    public void Merge_DoesNotPresentAiWithoutPositiveSpeciesClassification()
    {
        VerifiedRemoteEntityTelemetry[] entities =
        [
            new VerifiedRemoteEntityTelemetry(
                41,
                RemoteEntityKind.Ai,
                null,
                "",
                "",
                CreatureDiet.Unknown,
                null,
                new WorldLocation { X = 12_345, Y = -67_890, Z = 1_234 },
                125,
                1,
                Now)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            Observation(12_000, -67_500, 1_200, 45),
            Now,
            remotePlayers: entities);

        Assert.Null(merged.Map);
    }

    [Fact]
    public void Merge_AddsPositiveAiWithoutRequiringAPlayerName()
    {
        VerifiedRemoteEntityTelemetry[] entities =
        [
            new VerifiedRemoteEntityTelemetry(
                41,
                RemoteEntityKind.Ai,
                null,
                "fish",
                "Fish",
                CreatureDiet.Unknown,
                12.4,
                new WorldLocation { X = 12_345, Y = -67_890, Z = 1_234 },
                125,
                1,
                Now)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            Observation(12_000, -67_500, 1_200, 45),
            Now,
            remotePlayers: entities);

        var marker = Assert.Single(merged.Map!.Markers);
        Assert.Equal("pro-entity:ai:41", marker.SteamId);
        Assert.Equal("Fish 12.4K", marker.Label);
        Assert.Equal(RemoteEntityKind.Ai, marker.ProEntityKind);
    }

    [Fact]
    public void Merge_CullsTransportedEntitiesUsingFreshHostGps()
    {
        var local = Observation(100_000, -240_000, 30_000, 45);
        VerifiedRemoteEntityTelemetry[] entities =
        [
            new VerifiedRemoteEntityTelemetry(
                51,
                RemoteEntityKind.Player,
                "near-player-proof",
                "deinosuchus",
                "Deino",
                CreatureDiet.Carnivore,
                null,
                new WorldLocation { X = 105_000, Y = -242_000, Z = 29_000 },
                250_000,
                3,
                Now),
            new VerifiedRemoteEntityTelemetry(
                52,
                RemoteEntityKind.Player,
                "far-player-proof",
                "pteranodon",
                "Ptera",
                CreatureDiet.Carnivore,
                null,
                new WorldLocation { X = 16_000, Y = 300, Z = 4_000 },
                100,
                3,
                Now)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            local,
            Now,
            remotePlayers: entities);

        var marker = Assert.Single(merged.Map!.Markers);
        Assert.Equal("pro-entity:player:51", marker.SteamId);
    }

    private static LocalMovementObservation Observation(
        double x,
        double y,
        double z,
        double yaw) => new(
        Now,
        new UnrealMovementCandidate(
            x,
            y,
            z,
            yaw,
            1f,
            64,
            380,
            26),
        "171.232.64.234:7777");

    private static RemotePlayerTelemetryFrame RemoteFrame(
        DateTimeOffset observedAt,
        double x,
        double y,
        double z,
        double heading,
        string endpoint) => new(
        1,
        observedAt,
        endpoint,
        new WorldLocation { X = x, Y = y, Z = z },
        heading,
        []);
}
