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
    public void Merge_UsesFreshLocalIrisWhenCanaryEnabledAndProviderHasNoVitals()
    {
        var vitals = new ExactVitals
        {
            Growth = 0.42,
            Health = 75,
            MaxHealth = 100,
            Stamina = 60,
            MaxStamina = 120,
            Hunger = 10,
            MaxHunger = 40,
            Thirst = 900,
            MaxThirst = 1_000
        };
        var local = Observation(100, 200, 300, 45) with
        {
            DinosaurVitals = new LocalDinosaurVitalsObservation(Now, vitals, 42)
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            new TelemetrySnapshot
            {
                Source = "ISLEPILOT",
                SessionState = TelemetrySessionState.UnsupportedServer
            },
            local,
            Now,
            allowLocalVitals: true);

        Assert.Same(vitals, merged.Player?.ExactVitals);
        Assert.Equal("LocalIris", merged.Player?.ExactVitalsSource);
        Assert.Equal(42, merged.Player?.GrowthPercent);
        Assert.Equal(75, merged.Player?.HealthPercent);
        Assert.Equal(50, merged.Player?.StaminaPercent);
        Assert.Equal(25, merged.Player?.HungerPercent);
        Assert.Equal(90, merged.Player?.ThirstPercent);
        Assert.Null(merged.Player?.Nutrition);
        Assert.Null(merged.Player?.Prime);
        Assert.Null(merged.Player?.Class);
    }

    [Fact]
    public void Merge_LocalVitalsFreshnessIsIndependentFromMovementFreshness()
    {
        var movementAt = Now.Subtract(LocalPositionSnapshotMerger.LocalFreshness)
            .Subtract(TimeSpan.FromMilliseconds(1));
        var vitalsAt = Now.Subtract(TimeSpan.FromMilliseconds(100));
        var local = Observation(100, 200, 300, 45) with
        {
            ObservedAt = movementAt,
            DinosaurVitals = new LocalDinosaurVitalsObservation(
                vitalsAt,
                new ExactVitals { Health = 75, MaxHealth = 100 },
                42)
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            local,
            Now,
            allowLocalVitals: true);

        Assert.Equal("LocalIris", merged.Player?.ExactVitalsSource);
        Assert.Equal(75, merged.Player?.HealthPercent);
        Assert.Null(merged.Player?.Location);
        Assert.Equal(vitalsAt, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_ExpiresLocalVitalsWithoutExpiringFreshMovement()
    {
        var local = Observation(100, 200, 300, 45) with
        {
            DinosaurVitals = new LocalDinosaurVitalsObservation(
                Now.Subtract(LocalPositionSnapshotMerger.LocalVitalsFreshness)
                    .Subtract(TimeSpan.FromMilliseconds(1)),
                new ExactVitals { Health = 75, MaxHealth = 100 },
                42)
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            local,
            Now,
            allowLocalVitals: true);

        Assert.Null(merged.Player?.ExactVitals);
        Assert.Null(merged.Player?.ExactVitalsSource);
        Assert.Equal(100, merged.Player?.Location?.X);
    }

    [Fact]
    public void Merge_ProviderExactVitalsRemainAuthoritativeWhenCanaryEnabled()
    {
        var providerVitals = new ExactVitals { Health = 8, MaxHealth = 10 };
        var local = Observation(100, 200, 300, 45) with
        {
            DinosaurVitals = new LocalDinosaurVitalsObservation(
                Now,
                new ExactVitals { Health = 1, MaxHealth = 100 },
                42)
        };
        var remote = new TelemetrySnapshot
        {
            Player = new PlayerTelemetry
            {
                ExactVitals = providerVitals,
                ExactVitalsSource = "IslePilotOverlayV2",
                HealthPercent = 80
            },
            SessionState = TelemetrySessionState.Live
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            local,
            Now,
            allowLocalVitals: true);

        Assert.Same(providerVitals, merged.Player?.ExactVitals);
        Assert.Equal("IslePilotOverlayV2", merged.Player?.ExactVitalsSource);
        Assert.Equal(80, merged.Player?.HealthPercent);
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
    public void Merge_KeepsSparseVerifiedProFrameWithinReconnectGraceWindow()
    {
        var observedAt = Now - TimeSpan.FromSeconds(5);
        var frame = RemoteFrame(
            observedAt,
            98_700,
            -247_700,
            27_800,
            312.5,
            "15.235.226.35:7777");

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            null,
            Now,
            verifiedLocalFallback: frame);

        Assert.True(merged.PlayerOnline);
        Assert.Equal(TelemetrySessionState.Live, merged.SessionState);
        Assert.Equal(observedAt, merged.UpdatedAt);
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
    public void RemoteRosterFreshness_UsesIpcReceiptInsteadOfBackloggedCaptureTime()
    {
        var frame = RemoteFrame(
            Now - TimeSpan.FromSeconds(20),
            98_700,
            -247_700,
            27_800,
            312.5,
            "115.72.226.156:7777") with
        {
            ReceivedAt = Now - TimeSpan.FromSeconds(1)
        };

        Assert.True(LocalPositionSnapshotMerger.IsRemoteFrameFresh(frame, Now));
    }

    [Fact]
    public void RemoteRosterFreshness_ExpiresWhenAgentStopsDeliveringFrames()
    {
        var frame = RemoteFrame(
            Now,
            98_700,
            -247_700,
            27_800,
            312.5,
            "115.72.226.156:7777") with
        {
            ReceivedAt = Now
                         - LocalPositionSnapshotMerger.RemotePlayerFreshness
                         - TimeSpan.FromMilliseconds(1)
        };

        Assert.False(LocalPositionSnapshotMerger.IsRemoteFrameFresh(frame, Now));
    }

    [Fact]
    public void RemoteFrameCompatibility_RejectsOldServerWhileLocalEndpointIsFresh()
    {
        var frame = RemoteFrame(
            Now,
            98_700,
            -247_700,
            27_800,
            312.5,
            "115.72.226.156:7777");
        var local = Observation(98_700, -247_700, 27_800, 312.5) with
        {
            ServerEndpoint = "15.235.226.98:7777"
        };

        Assert.False(LocalPositionTelemetrySession.IsRemoteFrameCompatibleWithLocal(
            frame,
            local,
            Now));
    }

    [Fact]
    public void RemoteFrameCompatibility_AllowsMatchingEndpointAndIgnoresStaleLocalEndpoint()
    {
        var frame = RemoteFrame(
            Now,
            98_700,
            -247_700,
            27_800,
            312.5,
            "115.72.226.156:7777");
        var matching = Observation(98_700, -247_700, 27_800, 312.5) with
        {
            ServerEndpoint = "115.72.226.156:7777"
        };
        var staleDifferent = matching with
        {
            ObservedAt = Now - LocalPositionSnapshotMerger.LocalFreshness - TimeSpan.FromSeconds(1),
            ServerEndpoint = "15.235.226.98:7777"
        };

        Assert.True(LocalPositionTelemetrySession.IsRemoteFrameCompatibleWithLocal(
            frame,
            matching,
            Now));
        Assert.True(LocalPositionTelemetrySession.IsRemoteFrameCompatibleWithLocal(
            frame,
            staleDifferent,
            Now));
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
                Now,
                ActorNetRefHandle: 7,
                PlayerStateNetRefHandle: 8,
                PawnNetRefHandle: 9)
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
    public void Merge_RemoteFrameStillUpdatesEntitiesWhenLocalGpsIsStale()
    {
        var staleLocal = Observation(12_000, -67_500, 1_200, 45) with
        {
            ObservedAt = Now - LocalPositionSnapshotMerger.LocalFreshness - TimeSpan.FromMilliseconds(1)
        };
        VerifiedRemoteEntityTelemetry[] entities =
        [
            new VerifiedRemoteEntityTelemetry(
                91,
                RemoteEntityKind.Player,
                "verified-player",
                "deinosuchus",
                "Deino",
                CreatureDiet.Carnivore,
                null,
                new WorldLocation { X = 12_345, Y = -67_890, Z = 1_234 },
                250,
                4,
                Now,
                ActorNetRefHandle: 91,
                PlayerStateNetRefHandle: 92,
                PawnNetRefHandle: 93)
        ];
        var frame = RemoteFrame(
            Now,
            12_000,
            -67_500,
            1_200,
            45,
            "server:7777") with
        {
            RemoteEntities = entities
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            new TelemetrySnapshot { Player = new PlayerTelemetry { Location = staleLocal.Movement.Location } },
            staleLocal,
            Now,
            remotePlayers: entities,
            verifiedLocalFallback: frame,
            requireFreshLocalMovement: true);

        var marker = Assert.Single(merged.Map!.Markers);
        Assert.Equal("pro-entity:player:91", marker.SteamId);
        Assert.True(merged.ProPlayerTrackingActive);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.RenderedCount);
        Assert.Equal(0, merged.ProTrackingDiagnostics?.RejectedCount);
    }

    [Fact]
    public void Merge_ReportsRejectedRemoteEntitiesInsteadOfDroppingSilently()
    {
        var local = Observation(0, 0, 0, 0);
        VerifiedRemoteEntityTelemetry[] entities =
        [
            new(0, RemoteEntityKind.Player, "proof", "rex", "Rex", CreatureDiet.Carnivore,
                null, new WorldLocation { X = 1, Y = 1 }, 1, 1, Now,
                ActorNetRefHandle: 1),
            new(12, RemoteEntityKind.Ai, null, "", "", CreatureDiet.Unknown,
                null, new WorldLocation { X = 1, Y = 1 }, 1, 1, Now),
            new(13, RemoteEntityKind.Player, null, "rex", "Rex", CreatureDiet.Carnivore,
                null, new WorldLocation { X = 1, Y = 1 }, 1, 1, Now),
            new(14, RemoteEntityKind.Player, "proof", "rex", "Rex", CreatureDiet.Carnivore,
                null, new WorldLocation { X = double.NaN, Y = 1 }, 1, 1, Now,
                ActorNetRefHandle: 14),
            new(15, RemoteEntityKind.Player, "proof", "rex", "Rex", CreatureDiet.Carnivore,
                null, new WorldLocation { X = 300_000, Y = 1 }, 1, 1, Now,
                ActorNetRefHandle: 15),
            new(16, RemoteEntityKind.Player, "proof", "rex", "Rex", CreatureDiet.Carnivore,
                null, new WorldLocation { X = 1, Y = 1 }, 1, 1, Now,
                ActorNetRefHandle: 16)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null, local, Now, remotePlayers: entities);

        var diagnostics = Assert.IsType<RemoteTrackingDiagnostics>(merged.ProTrackingDiagnostics);
        Assert.Equal(6, diagnostics.ReceivedCount);
        Assert.Equal(2, diagnostics.RenderedCount);
        Assert.Equal(4, diagnostics.RejectedCount);
        Assert.Equal(1, diagnostics.Rejections[RemoteEntityRejectionReason.InvalidTrackId]);
        Assert.Equal(1, diagnostics.Rejections[RemoteEntityRejectionReason.MissingSpecies]);
        Assert.Equal(1, diagnostics.Rejections[RemoteEntityRejectionReason.MissingPlayerProof]);
        Assert.Equal(1, diagnostics.Rejections[RemoteEntityRejectionReason.InvalidCoordinate]);
    }

    [Fact]
    public void Merge_AcceptsRemoteEntitiesWithoutLocalDistanceReference()
    {
        var entity = new VerifiedRemoteEntityTelemetry(
            99, RemoteEntityKind.Ai, null, "rex", "Rex", CreatureDiet.Carnivore,
            null, new WorldLocation { X = 10, Y = 10 }, 0, 1, Now);

        var merged = LocalPositionSnapshotMerger.Merge(
            new TelemetrySnapshot { Map = new MapTelemetry() },
            null,
            Now,
            remotePlayers: [entity]);

        Assert.Equal(1, merged.ProTrackingDiagnostics?.RenderedCount);
        Assert.Equal(0, merged.ProTrackingDiagnostics?.RejectedCount);
    }

    [Fact]
    public void Merge_AcceptsVerifiedPlayerWhenNameIsUnavailableButIdentityHandlesExist()
    {
        var entity = new VerifiedRemoteEntityTelemetry(
            1001,
            RemoteEntityKind.Player,
            null,
            "rex",
            "Rex",
            CreatureDiet.Carnivore,
            null,
            new WorldLocation { X = 10, Y = 10 },
            0,
            3,
            Now,
            IsProvisional: false,
            ActorNetRefHandle: 1001,
            PlayerStateNetRefHandle: 1002,
            PawnNetRefHandle: 1003);

        var merged = LocalPositionSnapshotMerger.Merge(
            new TelemetrySnapshot { Map = new MapTelemetry() },
            null,
            Now,
            remotePlayers: [entity]);

        Assert.Equal(1, merged.ProTrackingDiagnostics?.RenderedCount);
        Assert.Equal(0, merged.ProTrackingDiagnostics?.RejectedCount);
        Assert.Equal("pro-entity:player:1001", Assert.Single(merged.Map!.Markers).SteamId);
    }

    [Fact]
    public void Merge_UsesSpeciesLabelForAnonymousVerifiedPlayer()
    {
        var entity = new VerifiedRemoteEntityTelemetry(
            1002,
            RemoteEntityKind.Player,
            null,
            "tyrannosaurus",
            "T-Rex",
            CreatureDiet.Carnivore,
            null,
            new WorldLocation { X = 20, Y = 20 },
            0,
            2,
            Now,
            IsProvisional: false,
            ActorNetRefHandle: 2001,
            PlayerStateNetRefHandle: 2002,
            PawnNetRefHandle: 2003);

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            null,
            Now,
            remotePlayers: [entity]);

        var marker = Assert.Single(merged.Map!.Markers);
        Assert.Equal("T-Rex", marker.Label);
        Assert.Equal(RemoteEntityKind.Player, marker.ProEntityKind);
        Assert.Equal("tyrannosaurus", marker.CreatureSpeciesId);
    }

    [Fact]
    public void Merge_RejectsPresenceRefreshWhenLocationIsStale()
    {
        var entity = new VerifiedRemoteEntityTelemetry(
            100,
            RemoteEntityKind.Player,
            "verified-player",
            "rex",
            "Rex",
            CreatureDiet.Carnivore,
            null,
            new WorldLocation { X = 10, Y = 10 },
            0,
            3,
            Now,
            IsProvisional: false,
            ActorNetRefHandle: 100,
            PlayerStateNetRefHandle: 101,
            PawnNetRefHandle: 102,
            LocationObservedAt: Now - VerifiedRemoteEntityTelemetry.LocationFreshness - TimeSpan.FromMilliseconds(1));

        var merged = LocalPositionSnapshotMerger.Merge(
            new TelemetrySnapshot { Map = new MapTelemetry() },
            null,
            Now,
            remotePlayers: [entity]);

        Assert.Equal(0, merged.ProTrackingDiagnostics?.RenderedCount);
        Assert.Empty(merged.Map!.Markers);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.RejectedCount);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.StaleCount);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.Rejections[RemoteEntityRejectionReason.StaleLocation]);
    }

    [Fact]
    public void Merge_DropsPreviouslyRenderedVerifiedActorWhenLocationIsStale()
    {
        var marker = new MapMarkerTelemetry
        {
            SteamId = "pro-entity:player:100",
            Label = "Rex",
            ProEntityKind = RemoteEntityKind.Player,
            CreatureSpeciesShortName = "Rex",
            Location = new WorldLocation { X = 10, Y = 10 }
        };
        var entity = new VerifiedRemoteEntityTelemetry(
            100,
            RemoteEntityKind.Player,
            "verified-player",
            "rex",
            "Rex",
            CreatureDiet.Carnivore,
            null,
            new WorldLocation { X = 10, Y = 10 },
            0,
            3,
            Now,
            IsProvisional: false,
            LocationObservedAt: Now - VerifiedRemoteEntityTelemetry.LocationFreshness - TimeSpan.FromMilliseconds(1));

        var merged = LocalPositionSnapshotMerger.Merge(
            new TelemetrySnapshot { Map = new MapTelemetry { Markers = [marker] } },
            null,
            Now,
            remotePlayers: [entity]);

        Assert.Empty(merged.Map!.Markers);
        Assert.Equal(0, merged.ProTrackingDiagnostics?.RenderedCount);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.RejectedCount);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.StaleCount);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.Rejections[RemoteEntityRejectionReason.StaleLocation]);
    }

    [Fact]
    public void Merge_DoesNotProjectFirstVerifiedActorWhenItsOnlyLocationIsOld()
    {
        var entity = new VerifiedRemoteEntityTelemetry(
            101,
            RemoteEntityKind.Player,
            "verified-player",
            "triceratops",
            "Trice",
            CreatureDiet.Herbivore,
            null,
            new WorldLocation { X = 100, Y = 200 },
            0,
            3,
            Now,
            IsProvisional: false,
            ActorNetRefHandle: 101,
            PlayerStateNetRefHandle: 102,
            PawnNetRefHandle: 103,
            LocationObservedAt: Now - VerifiedRemoteEntityTelemetry.LocationFreshness - TimeSpan.FromSeconds(1));

        var merged = LocalPositionSnapshotMerger.Merge(
            new TelemetrySnapshot { Map = new MapTelemetry() },
            null,
            Now,
            remotePlayers: [entity]);

        Assert.Empty(merged.Map!.Markers);
        Assert.Equal(0, merged.ProTrackingDiagnostics?.RenderedCount);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.RejectedCount);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.StaleCount);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.Rejections[RemoteEntityRejectionReason.StaleLocation]);
    }

    [Fact]
    public void Merge_RefreshesStaleVerifiedActorInPlaceWhenMovementBecomesFresh()
    {
        var staleMarker = new MapMarkerTelemetry
        {
            SteamId = "pro-entity:player:102",
            Label = "Trice",
            ProEntityKind = RemoteEntityKind.Player,
            CreatureSpeciesId = "triceratops",
            CreatureSpeciesShortName = "Trice",
            ProCreatureDiet = CreatureDiet.Herbivore,
            Location = new WorldLocation { X = 100, Y = 200 },
            ProEntityIsStale = true
        };
        var freshEntity = new VerifiedRemoteEntityTelemetry(
            102,
            RemoteEntityKind.Player,
            "verified-player",
            "triceratops",
            "Trice",
            CreatureDiet.Herbivore,
            null,
            new WorldLocation { X = 300, Y = 400 },
            0,
            4,
            Now,
            IsProvisional: false,
            ActorNetRefHandle: 102,
            PlayerStateNetRefHandle: 103,
            PawnNetRefHandle: 104,
            LocationObservedAt: Now);

        var merged = LocalPositionSnapshotMerger.Merge(
            new TelemetrySnapshot { Map = new MapTelemetry { Markers = [staleMarker] } },
            null,
            Now,
            remotePlayers: [freshEntity]);

        var marker = Assert.Single(merged.Map!.Markers);
        Assert.Equal("pro-entity:player:102", marker.SteamId);
        Assert.Equal(300, marker.Location?.X);
        Assert.Equal(400, marker.Location?.Y);
        Assert.False(marker.ProEntityIsStale);
        Assert.Equal(1, merged.ProTrackingDiagnostics?.RenderedCount);
        Assert.Equal(0, merged.ProTrackingDiagnostics?.RejectedCount);
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
                Now,
                ActorNetRefHandle: 71436,
                PlayerStateNetRefHandle: 71437,
                PawnNetRefHandle: 71438)
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
    public void Merge_PresentsAnonymousVerifiedPlayerWithStructuralIdentity()
    {
        VerifiedRemoteEntityTelemetry[] remotePlayers =
        [
            new VerifiedRemoteEntityTelemetry(
                71437,
                RemoteEntityKind.Player,
                null,
                "triceratops",
                "Trice",
                CreatureDiet.Herbivore,
                null,
                new WorldLocation { X = 89_280, Y = -277_806, Z = 28_145 },
                270.5,
                66,
                Now,
                IsProvisional: false,
                LocationObservedAt: Now,
                ActorNetRefHandle: 71437,
                PlayerStateNetRefHandle: 71438,
                PawnNetRefHandle: 71439)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            Observation(80_548, -252_203, 28_061, 45),
            Now,
            remotePlayers: remotePlayers);

        var marker = Assert.Single(merged.Map!.Markers);
        Assert.Equal("pro-entity:player:71437", marker.SteamId);
        Assert.Equal("Trice", marker.Label);
        Assert.Equal(RemoteEntityKind.Player, marker.ProEntityKind);
        Assert.Equal("triceratops", marker.CreatureSpeciesId);
    }

    [Fact]
    public void Merge_PresentsAnonymousPlayerWithoutSpeciesUsingFallbackLabel()
    {
        VerifiedRemoteEntityTelemetry[] remotePlayers =
        [
            new VerifiedRemoteEntityTelemetry(
                71438,
                RemoteEntityKind.Player,
                null,
                "",
                "",
                CreatureDiet.Unknown,
                null,
                new WorldLocation { X = 89_280, Y = -277_806, Z = 28_145 },
                270.5,
                66,
                Now,
                IsProvisional: false,
                LocationObservedAt: Now,
                ActorNetRefHandle: 71438,
                PlayerStateNetRefHandle: 71439,
                PawnNetRefHandle: 71440)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            Observation(80_548, -252_203, 28_061, 45),
            Now,
            remotePlayers: remotePlayers);

        var marker = Assert.Single(merged.Map!.Markers);
        Assert.Equal("pro-entity:player:71438", marker.SteamId);
        Assert.Equal("Player ?", marker.Label);
        Assert.Equal(RemoteEntityKind.Player, marker.ProEntityKind);
    }

    [Fact]
    public void Merge_RejectsNameOnlyPlayerWithoutStructuralIdentity()
    {
        VerifiedRemoteEntityTelemetry[] remotePlayers =
        [
            new VerifiedRemoteEntityTelemetry(
                71439,
                RemoteEntityKind.Player,
                "metadata-only-name",
                "triceratops",
                "Trice",
                CreatureDiet.Herbivore,
                null,
                new WorldLocation { X = 89_280, Y = -277_806, Z = 28_145 },
                270.5,
                66,
                Now,
                IsProvisional: false,
                LocationObservedAt: Now)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            Observation(80_548, -252_203, 28_061, 45),
            Now,
            remotePlayers: remotePlayers);

        Assert.Null(merged.Map);
        var diagnostics = merged.ProTrackingDiagnostics
            ?? throw new Xunit.Sdk.XunitException("Expected remote tracking diagnostics.");
        Assert.Equal(
            1,
            diagnostics.Rejections[
                RemoteEntityRejectionReason.MissingPlayerProof]);
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
    public void Merge_PreservesTransportedEntitiesUsingFreshHostGps()
    {
        var local = Observation(100_000, -240_000, 30_000, 45);
        VerifiedRemoteEntityTelemetry[] entities =
        [
            new VerifiedRemoteEntityTelemetry(
                51,
                RemoteEntityKind.Player,
                "player-at-800m-proof",
                "deinosuchus",
                "Deino",
                CreatureDiet.Carnivore,
                null,
                new WorldLocation { X = 180_000, Y = -240_000, Z = 30_000 },
                250_000,
                3,
                Now,
                ActorNetRefHandle: 51,
                PlayerStateNetRefHandle: 52,
                PawnNetRefHandle: 53),
            new VerifiedRemoteEntityTelemetry(
                53,
                RemoteEntityKind.Ai,
                null,
                "coelacanth",
                "Coel",
                CreatureDiet.Unknown,
                null,
                new WorldLocation { X = 100_000, Y = -150_000, Z = 30_000 },
                300_000,
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
                Now,
                ActorNetRefHandle: 52,
                PlayerStateNetRefHandle: 54,
                PawnNetRefHandle: 55)
        ];

        var merged = LocalPositionSnapshotMerger.Merge(
            null,
            local,
            Now,
            remotePlayers: entities);

        var markers = merged.Map!.Markers;
        Assert.Equal(3, markers.Count);
        Assert.Contains(markers, marker => marker.SteamId == "pro-entity:player:51");
        Assert.Contains(markers, marker => marker.SteamId == "pro-entity:ai:53");
        Assert.Contains(markers, marker => marker.SteamId == "pro-entity:player:52");
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
