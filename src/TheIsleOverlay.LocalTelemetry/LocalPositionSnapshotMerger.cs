using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

public static class LocalPositionSnapshotMerger
{
    public static readonly TimeSpan LocalFreshness = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan LocalVitalsFreshness = TimeSpan.FromSeconds(3);
    // Keep a reconnect grace window for sparse rosters. The latest-value lane
    // prevents this grace period from becoming a substitute for queueing the
    // newest position frame; diagnostics still expose the actual frame age.
    public static readonly TimeSpan RemotePlayerFreshness = TimeSpan.FromSeconds(6);
    // Unreal coordinates are centimetres: 100,000 units = 1 kilometre.
    public const double MaximumRemoteEntityDistance = 100_000d;

    public static TelemetrySnapshot Merge(
        TelemetrySnapshot? remote,
        LocalMovementObservation? local,
        DateTimeOffset now,
        string sourceName = "LOCAL",
        IReadOnlyList<VerifiedRemoteEntityTelemetry>? remotePlayers = null,
        string? verifiedLocalSpeciesId = null,
        RemotePlayerTelemetryFrame? verifiedLocalFallback = null,
        bool allowLocalVitals = false,
        bool requireFreshLocalMovement = false)
    {
        var localObservation = local.GetValueOrDefault();
        var fallback = verifiedLocalFallback;
        var hasFreshLocal = local.HasValue
                            && localObservation.HasMovement
                            && IsFresh(
                                localObservation.ObservedAt,
                                now,
                                LocalFreshness);
        var localVitals = localObservation.DinosaurVitals;
        var hasFreshLocalVitals = allowLocalVitals
                                  && localVitals is { } candidateVitals
                                  && IsFresh(
                                      candidateVitals.ObservedAt,
                                      now,
                                      LocalVitalsFreshness)
                                  && HasUsableVitals(candidateVitals.Vitals);
        var useLocalVitals = hasFreshLocalVitals
                             && (remote?.LiveDataStale == true
                                 || !HasUsableVitals(remote?.Player?.ExactVitals));
        var hasFreshVerifiedFallback = fallback is not null
                                       && IsRemoteFrameFresh(fallback, now)
                                       && IsFinite(fallback.LocalLocation)
            && double.IsFinite(fallback.MapHeadingDegrees);
        var hasFreshRemoteFrame = hasFreshVerifiedFallback && remotePlayers is not null;
        if (requireFreshLocalMovement
            && !hasFreshLocal
            && !hasFreshVerifiedFallback
            && !useLocalVitals
            && !hasFreshRemoteFrame)
        {
            return remote is null
                ? Waiting(sourceName)
                : remote with
                {
                    PlayerOnline = false,
                    Player = null,
                    SessionState = TelemetrySessionState.Connecting,
                    StatusMessage = "Đang chờ The Isle và dữ liệu movement cục bộ."
                };
        }
        if (!hasFreshLocal && !hasFreshVerifiedFallback && !useLocalVitals)
        {
            if (remote?.Player is { } previousPlayer
                && string.Equals(
                    previousPlayer.ExactVitalsSource,
                    LocalVitalsFeature.SourceName,
                    StringComparison.Ordinal))
            {
                return remote with
                {
                    Player = RemoveLocalVitals(previousPlayer)
                };
            }

            return remote is null
                ? Waiting(sourceName)
                : remote;
        }

        var baseSnapshot = remote is null
            ? new TelemetrySnapshot()
            : remote;
        var remotePlayer = baseSnapshot.Player;
        WorldLocation? location = hasFreshLocal
            ? localObservation.Movement.Location
            : hasFreshVerifiedFallback
                ? verifiedLocalFallback!.LocalLocation
                : remotePlayer?.Location;
        var mapHeadingDegrees = hasFreshLocal
            ? localObservation.Movement.MapHeadingDegrees
            : hasFreshVerifiedFallback
                ? MapHeading.Normalize(verifiedLocalFallback!.MapHeadingDegrees)
                : remotePlayer?.ExactMapHeadingDegrees;
        var serverEndpoint = hasFreshLocal || useLocalVitals
            ? localObservation.ServerEndpoint
            : hasFreshVerifiedFallback
                ? verifiedLocalFallback!.ServerEndpoint
                : null;
        var observedAt = LatestTimestamp(
            hasFreshLocal ? localObservation.ObservedAt : null,
            hasFreshVerifiedFallback ? verifiedLocalFallback!.ObservedAt : null,
            useLocalVitals ? localVitals!.Value.ObservedAt : null)
            ?? baseSnapshot.UpdatedAt;
        var player = (remotePlayer ?? new PlayerTelemetry
        {
            Name = "LOCAL PLAYER"
        }) with
        {
            Server = string.IsNullOrWhiteSpace(remotePlayer?.Server)
                ? serverEndpoint
                : remotePlayer.Server,
            ServerEndpoint = string.IsNullOrWhiteSpace(serverEndpoint)
                ? remotePlayer?.ServerEndpoint
                : serverEndpoint,
            Class = string.IsNullOrWhiteSpace(verifiedLocalSpeciesId)
                ? remotePlayer?.Class
                : verifiedLocalSpeciesId.Trim(),
            Location = hasFreshLocal || hasFreshVerifiedFallback
                ? location
                : remotePlayer?.Location,
            MapLocation = hasFreshLocal || hasFreshVerifiedFallback
                ? null
                : remotePlayer?.MapLocation,
            ExactMapHeadingDegrees = mapHeadingDegrees
        };

        if (useLocalVitals)
        {
            player = ApplyLocalVitals(player, localVitals!.Value.Vitals);
        }
        else if (!useLocalVitals
                 && string.Equals(
                     player.ExactVitalsSource,
                     LocalVitalsFeature.SourceName,
                     StringComparison.Ordinal))
        {
            player = RemoveLocalVitals(player);
        }

        // Local movement freshness only proves that the GPS lane is alive.
        // TelemetrySnapshot has no per-field freshness metadata, so a fresh
        // GPS/Iris sample cannot make stale remote Nutrition/Prime or stats
        // look globally live. Preserve the remote stale marker until refresh.
        var preserveRemoteStaleness = baseSnapshot.LiveDataStale;

        return baseSnapshot with
        {
            Source = string.IsNullOrWhiteSpace(baseSnapshot.Source)
                     || string.Equals(baseSnapshot.Source, "Unknown", StringComparison.OrdinalIgnoreCase)
                ? sourceName
                : baseSnapshot.Source,
            Success = true,
            ServerOnline = true,
            PlayerOnline = true,
            UpdatedAt = observedAt,
            Player = player,
            Map = remotePlayers is not null
                  && (hasFreshLocal || hasFreshVerifiedFallback)
                ? MergeRemotePlayers(
                    baseSnapshot.Map,
                    remotePlayers,
                    location!)
                : baseSnapshot.Map,
            ProPlayerTrackingActive = remotePlayers is not null,
            ProPlayerSequence = verifiedLocalFallback?.Sequence,
            ProPlayerSync = verifiedLocalFallback?.PlayerSync,
            SessionState = preserveRemoteStaleness
                ? baseSnapshot.SessionState
                : TelemetrySessionState.Live,
            LiveDataStale = preserveRemoteStaleness,
            StatusMessage = (hasFreshLocal || hasFreshVerifiedFallback)
                            && baseSnapshot.SessionState == TelemetrySessionState.UnsupportedServer
                ? "Map trực tiếp đang hoạt động; status và nhiệm vụ IslePilot không khả dụng trên server này."
                : baseSnapshot.StatusMessage
        };
    }

    private static PlayerTelemetry ApplyLocalVitals(
        PlayerTelemetry player,
        ExactVitals vitals) => player with
    {
        ExactVitals = vitals,
        ExactVitalsSource = LocalVitalsFeature.SourceName,
        GrowthPercent = NormalizeGrowth(vitals.Growth),
        HealthPercent = PercentOrNull(vitals.Health, vitals.MaxHealth),
        StaminaPercent = PercentOrNull(vitals.Stamina, vitals.MaxStamina),
        HungerPercent = PercentOrNull(vitals.Hunger, vitals.MaxHunger),
        ThirstPercent = PercentOrNull(vitals.Thirst, vitals.MaxThirst)
    };

    private static PlayerTelemetry RemoveLocalVitals(PlayerTelemetry player) => player with
    {
        ExactVitals = null,
        ExactVitalsSource = null,
        GrowthPercent = null,
        HealthPercent = null,
        StaminaPercent = null,
        HungerPercent = null,
        ThirstPercent = null
    };

    private static bool HasUsableVitals(ExactVitals? vitals) =>
        vitals is not null
        && (IsFiniteNonNegative(vitals.Growth)
            || IsUsablePair(vitals.Health, vitals.MaxHealth)
            || IsUsablePair(vitals.Stamina, vitals.MaxStamina)
            || IsUsablePair(vitals.Hunger, vitals.MaxHunger)
            || IsUsablePair(vitals.Thirst, vitals.MaxThirst));

    private static bool IsUsablePair(double? current, double? maximum) =>
        IsFiniteNonNegative(current)
        && maximum is > 0d
        && double.IsFinite(maximum.Value);

    private static bool IsFiniteNonNegative(double? value) =>
        value is >= 0d
        && double.IsFinite(value.Value);

    private static double? PercentOrNull(double? current, double? maximum) =>
        IsUsablePair(current, maximum)
            ? VitalMath.Percent(current, maximum)
            : null;

    private static double? NormalizeGrowth(double? growth) =>
        IsFiniteNonNegative(growth)
            ? VitalMath.Percent(null, null, growth)
            : null;

    private static bool IsFresh(
        DateTimeOffset observedAt,
        DateTimeOffset now,
        TimeSpan freshness) =>
        now >= observedAt
        && now - observedAt <= freshness;

    private static DateTimeOffset? LatestTimestamp(
        DateTimeOffset? first,
        DateTimeOffset? second,
        DateTimeOffset? third)
    {
        DateTimeOffset? latest = null;
        foreach (var candidate in new[] { first, second, third })
        {
            if (candidate is { } value
                && (latest is null || value > latest.Value))
            {
                latest = value;
            }
        }

        return latest;
    }

    private static MapTelemetry? MergeRemotePlayers(
        MapTelemetry? map,
        IReadOnlyList<VerifiedRemoteEntityTelemetry>? remotePlayers,
        WorldLocation localLocation)
    {
        if (remotePlayers is null)
        {
            return map;
        }

        var providerMarkers = (map?.Markers ?? [])
            .Where(marker => marker.SteamId is null
                             || (!marker.SteamId.StartsWith(
                                     "pro-player:",
                                     StringComparison.Ordinal)
                                 && !marker.SteamId.StartsWith(
                                     "pro-entity:",
                                     StringComparison.Ordinal)))
            .ToArray();
        // The ingame name remains a private proof field. It gates player
        // markers here but is deliberately not copied into MapTelemetry or a
        // label. AI is accepted only when the signed Pro Agent classified an
        // exact non-player fauna archetype.
        var proMarkers = remotePlayers
            .Where(entity =>
                IsMapReady(entity)
                && IsWithinRemoteEntityDistance(entity.Location, localLocation))
            .Select(entity =>
            {
                var speciesLabel = string.IsNullOrWhiteSpace(entity.SpeciesShortName)
                    ? "Player ?"
                    : entity.SpeciesShortName;
                return new MapMarkerTelemetry
                {
                    SteamId = $"pro-entity:{entity.Kind.ToString().ToLowerInvariant()}:{entity.TrackId}",
                    Label = CreatureMarkerLabelFormatter.Format(
                        speciesLabel,
                        entity.MassKg),
                    Self = false,
                    Location = entity.Location,
                    ProEntityKind = entity.Kind,
                    CreatureSpeciesId = entity.SpeciesId,
                    CreatureSpeciesShortName = entity.SpeciesShortName,
                    ProCreatureDiet = entity.Diet,
                    CreatureMassKg = entity.MassKg,
                    ProEntityIsProvisional = entity.IsProvisional
                };
            })
            .ToArray();
        if (map is null && proMarkers.Length == 0)
        {
            return null;
        }

        return (map ?? new MapTelemetry()) with
        {
            Markers = [.. providerMarkers, .. proMarkers]
        };
    }

    private static bool IsMapReady(VerifiedRemoteEntityTelemetry entity) =>
        entity.TrackId > 0
        && (entity.Kind == RemoteEntityKind.Ai
            && !string.IsNullOrWhiteSpace(entity.SpeciesId)
            && !string.IsNullOrWhiteSpace(entity.SpeciesShortName)
            || entity.Kind == RemoteEntityKind.Player
            && (entity.IsProvisional
                && !string.IsNullOrWhiteSpace(entity.SpeciesId)
                && !string.IsNullOrWhiteSpace(entity.SpeciesShortName)
                || !entity.IsProvisional
                && !string.IsNullOrWhiteSpace(entity.PlayerProofName)));

    private static bool IsWithinRemoteEntityDistance(
        WorldLocation entity,
        WorldLocation local)
    {
        var deltaX = entity.X - local.X;
        var deltaY = entity.Y - local.Y;
        var deltaZ = (entity.Z ?? 0d) - (local.Z ?? 0d);
        return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ
               <= MaximumRemoteEntityDistance * MaximumRemoteEntityDistance;
    }

    private static bool IsFinite(WorldLocation location) =>
        double.IsFinite(location.X)
        && double.IsFinite(location.Y)
        && (location.Z is null || double.IsFinite(location.Z.Value));

    internal static bool IsRemoteFrameFresh(
        RemotePlayerTelemetryFrame frame,
        DateTimeOffset now)
    {
        var freshnessTimestamp = frame.ReceivedAt ?? frame.ObservedAt;
        return now >= freshnessTimestamp
               && now - freshnessTimestamp <= RemotePlayerFreshness;
    }

    public static TelemetrySnapshot Waiting(string sourceName, string? statusMessage = null) => new()
    {
        Source = sourceName,
        Success = true,
        ServerOnline = true,
        PlayerOnline = false,
        UpdatedAt = DateTimeOffset.Now,
        SessionState = TelemetrySessionState.Connecting,
        StatusMessage = statusMessage ?? "Đang chờ The Isle và dữ liệu movement cục bộ."
    };
}
