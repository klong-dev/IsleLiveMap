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
        var hasRemoteInput = remotePlayers is not null;
        if (requireFreshLocalMovement
            && !hasFreshLocal
            && !hasFreshVerifiedFallback
            && !useLocalVitals
            && !hasFreshRemoteFrame
            && !hasRemoteInput)
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
        if (!hasFreshLocal && !hasFreshVerifiedFallback && !useLocalVitals && !hasRemoteInput)
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

        var mergedRemote = remotePlayers is not null
                           && (hasFreshLocal || hasFreshVerifiedFallback || hasFreshRemoteFrame)
            ? MergeRemotePlayers(
                baseSnapshot.Map,
                remotePlayers,
                location!,
                hasFreshLocal || hasFreshVerifiedFallback || hasFreshRemoteFrame)
            : remotePlayers is not null
                ? MergeRemotePlayers(
                    baseSnapshot.Map,
                    remotePlayers,
                    location ?? new WorldLocation(),
                    hasDistanceReference: false)
            : null;

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
            Map = mergedRemote?.Map ?? baseSnapshot.Map,
            ProPlayerTrackingActive = remotePlayers is not null,
            ProPlayerSequence = verifiedLocalFallback?.Sequence,
            ProPlayerFrameObservedAt = verifiedLocalFallback?.ObservedAt,
            ProPlayerFrameReceivedAt = verifiedLocalFallback?.ReceivedAt,
            ProPlayerSync = verifiedLocalFallback?.PlayerSync,
            ProTrackingDiagnostics = mergedRemote?.Diagnostics ?? baseSnapshot.ProTrackingDiagnostics,
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

    private sealed record RemoteMergeResult(MapTelemetry? Map, RemoteTrackingDiagnostics Diagnostics);

    private static RemoteMergeResult MergeRemotePlayers(
        MapTelemetry? map,
        IReadOnlyList<VerifiedRemoteEntityTelemetry>? remotePlayers,
        WorldLocation localLocation,
        bool hasDistanceReference)
    {
        if (remotePlayers is null)
        {
            return new RemoteMergeResult(map, RemoteTrackingDiagnostics.NoFrame);
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
        var rejectionCounts = new Dictionary<RemoteEntityRejectionReason, int>();
        var eligible = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var proMarkers = new List<MapMarkerTelemetry>();
        foreach (var entity in remotePlayers)
        {
            if (!TryGetRejectionReason(entity, hasDistanceReference, localLocation, seen, out var reason))
            {
                eligible++;
                var speciesLabel = string.IsNullOrWhiteSpace(entity.SpeciesShortName)
                    ? "Player ?"
                    : entity.SpeciesShortName;
                proMarkers.Add(new MapMarkerTelemetry
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
                });
                continue;
            }

            rejectionCounts[reason] = rejectionCounts.GetValueOrDefault(reason) + 1;
        }
        var diagnostics = new RemoteTrackingDiagnostics
        {
            ReceivedCount = remotePlayers.Count,
            EligibleCount = eligible,
            RenderedCount = proMarkers.Count,
            RejectedCount = remotePlayers.Count - proMarkers.Count,
            Rejections = rejectionCounts,
            FrameState = "frame"
        };
        if (map is null && proMarkers.Count == 0)
        {
            return new RemoteMergeResult(null, diagnostics);
        }

        return new RemoteMergeResult((map ?? new MapTelemetry()) with
        {
            Markers = [.. providerMarkers, .. proMarkers]
        }, diagnostics);
    }

    private static bool TryGetRejectionReason(
        VerifiedRemoteEntityTelemetry entity,
        bool hasDistanceReference,
        WorldLocation localLocation,
        HashSet<string> seen,
        out RemoteEntityRejectionReason reason)
    {
        if (entity.TrackId <= 0)
        {
            reason = RemoteEntityRejectionReason.InvalidTrackId;
            return true;
        }

        if (entity.Kind is not RemoteEntityKind.Player and not RemoteEntityKind.Ai)
        {
            reason = RemoteEntityRejectionReason.UnsupportedKind;
            return true;
        }

        if (!IsFinite(entity.Location))
        {
            reason = RemoteEntityRejectionReason.InvalidCoordinate;
            return true;
        }

        if (entity.Kind == RemoteEntityKind.Ai
            && (string.IsNullOrWhiteSpace(entity.SpeciesId)
                || string.IsNullOrWhiteSpace(entity.SpeciesShortName)))
        {
            reason = RemoteEntityRejectionReason.MissingSpecies;
            return true;
        }

        if (entity.Kind == RemoteEntityKind.Player
            && (entity.IsProvisional
                ? string.IsNullOrWhiteSpace(entity.SpeciesId)
                  || string.IsNullOrWhiteSpace(entity.SpeciesShortName)
                : string.IsNullOrWhiteSpace(entity.PlayerProofName)))
        {
            reason = RemoteEntityRejectionReason.MissingPlayerProof;
            return true;
        }

        var key = $"{entity.Kind}:{entity.TrackId}";
        if (!seen.Add(key))
        {
            reason = RemoteEntityRejectionReason.Duplicate;
            return true;
        }

        if (!hasDistanceReference)
        {
            reason = RemoteEntityRejectionReason.DistanceCheckUnavailable;
            return true;
        }

        if (!IsWithinRemoteEntityDistance(entity.Location, localLocation))
        {
            reason = RemoteEntityRejectionReason.TooFarFromLocal;
            return true;
        }

        reason = default;
        return false;
    }

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
