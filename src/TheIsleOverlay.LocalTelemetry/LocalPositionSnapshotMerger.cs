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
        // Authentication is authoritative for the provider lane. A local
        // packet (or its absence) must never turn an expired IslePilot
        // session back into Live/Connecting.
        if (remote?.SessionState == TelemetrySessionState.AuthenticationRequired)
        {
            return remote;
        }
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
            if (remote is null)
            {
                return Waiting(sourceName);
            }

            // Provider stats have their own freshness/lifecycle. Losing GPS
            // must not erase them, nor may retaining them revive an expired
            // local position or a local-only synthetic player.
            if (remote is { Success: true, ServerOnline: true, PlayerOnline: true, Player: { } providerPlayer }
                && !string.IsNullOrWhiteSpace(providerPlayer.ExactVitalsSource)
                && providerPlayer.ExactVitalsSource != LocalVitalsFeature.SourceName)
            {
                return remote with
                {
                    Player = providerPlayer with { Location = null, MapLocation = null, ExactMapHeadingDegrees = null }
                };
            }

            return remote with
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
                           ? MergeRemotePlayers(baseSnapshot.Map, remotePlayers, now)
                           : remotePlayers is not null
                ? MergeRemotePlayers(baseSnapshot.Map, remotePlayers, now)
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
            ProPlayerSessionId = verifiedLocalFallback?.SessionId,
            ProPlayerServerEndpoint = verifiedLocalFallback?.ServerEndpoint,
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
        DateTimeOffset now)
    {
        if (remotePlayers is null)
        {
            return new RemoteMergeResult(map, RemoteTrackingDiagnostics.NoFrame);
        }

        var previousProMarkers = (map?.Markers ?? [])
            .Where(marker => marker.SteamId is not null
                             && (marker.SteamId.StartsWith(
                                     "pro-player:",
                                     StringComparison.Ordinal)
                                 || marker.SteamId.StartsWith(
                                     "pro-entity:",
                                     StringComparison.Ordinal)))
            .ToDictionary(
                marker => marker.SteamId!,
                StringComparer.Ordinal);
        var providerMarkers = (map?.Markers ?? [])
            .Where(marker => marker.SteamId is null
                             || (!marker.SteamId.StartsWith(
                                     "pro-player:",
                                     StringComparison.Ordinal)
                                 && !marker.SteamId.StartsWith(
                                     "pro-entity:",
                                     StringComparison.Ordinal)))
            .ToArray();
        // Player names are optional presentation metadata and are never proof.
        // Player markers require structural Iris identity (actor, PlayerState,
        // or pawn handle); AI is accepted only when the signed Pro Agent
        // classified an exact non-player fauna archetype.
        var rejectionCounts = new Dictionary<RemoteEntityRejectionReason, int>();
        var eligible = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var proMarkers = new List<MapMarkerTelemetry>();
        var staleCount = 0;
        foreach (var entity in remotePlayers)
        {
            var staleLocation = IsStaleLocation(entity, now);
            if (!TryGetRejectionReason(entity, seen, now, out var reason))
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
                    ProEntityIsProvisional = entity.IsProvisional,
                    // A retained stale marker is deliberately dimmed by the
                    // renderer and is never counted by live/fresh gates.
                    ProEntityIsStale = staleLocation
                });
                if (staleLocation)
                {
                    staleCount++;
                }
                continue;
            }

            // A previously rendered identity is continuity evidence even if
            // the current sparse frame no longer carries structural handles.
            // Retain only the exact key and only inside the same stale TTL;
            // never manufacture a new marker from a stale, unproven entity.
            if (reason == RemoteEntityRejectionReason.StaleLocation
                && previousProMarkers.TryGetValue(
                    $"pro-entity:{entity.Kind.ToString().ToLowerInvariant()}:{entity.TrackId}",
                    out var previousMarker)
                && previousMarker.Location == entity.Location
                && ((staleLocation && IsFresh(entity.ObservedAt, now, RemotePlayerFreshness))
                    || CanRetainAdmittedPlayer(entity, previousMarker, now))
                && seen.Add($"{entity.Kind}:{entity.TrackId}"))
            {
                eligible++;
                staleCount++;
                proMarkers.Add(previousMarker with
                {
                    ProEntityIsStale = true
                });
                continue;
            }

            rejectionCounts[reason] = rejectionCounts.GetValueOrDefault(reason) + 1;

            // Positions outside both admission and bounded retention remain
            // diagnostic-only; presence must not refresh location time.
            if (reason == RemoteEntityRejectionReason.StaleLocation)
            {
                staleCount++;
            }
        }
        var diagnostics = new RemoteTrackingDiagnostics
        {
            ReceivedCount = remotePlayers.Count,
            EligibleCount = eligible,
            RenderedCount = proMarkers.Count,
            StaleCount = staleCount,
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

    private static bool HasVerifiedIdentity(VerifiedRemoteEntityTelemetry entity) =>
        entity.Kind == RemoteEntityKind.Ai
            ? !string.IsNullOrWhiteSpace(entity.SpeciesId)
              && !string.IsNullOrWhiteSpace(entity.SpeciesShortName)
            : !entity.IsProvisional
              // Player names are optional metadata. Structural Iris handles
              // are the only proof accepted by the render lifecycle.
              && (entity.ActorNetRefHandle > 0
                  || entity.PlayerStateNetRefHandle > 0
                  || entity.PawnNetRefHandle > 0);

    private static bool TryGetRejectionReason(
        VerifiedRemoteEntityTelemetry entity,
        HashSet<string> seen,
        DateTimeOffset now,
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

        // Older Pro Agent frames did not carry a separate location timestamp.
        // Keep those fixtures/backward-compatible agents valid by treating the
        // entity observation as the location observation. New agents always
        // provide LocationObservedAt, which prevents presence refreshes from
        // making an old coordinate look live.
        var locationObservedAt = entity.LocationObservedAt ?? entity.ObservedAt;
        var locationAge = now - locationObservedAt;
        var retainVerifiedPosition = CanRetainVerifiedPosition(entity, now);
        var admitRecentPosition = HasReliableEntityIdentity(entity)
            && IsFresh(entity.ObservedAt, now, RemotePlayerFreshness)
            && entity.ObservedAt >= locationObservedAt
            && IsFresh(locationObservedAt, now,
                VerifiedRemoteEntityTelemetry.InitialPositionAdmission);
        if (entity.HasVerifiedPosition && !IsFresh(entity.ObservedAt, now,
                VerifiedRemoteEntityTelemetry.PresenceRetention))
        {
            reason = RemoteEntityRejectionReason.PresenceTimeout;
            return true;
        }
        if (locationObservedAt > now
            || locationAge > RemotePlayerFreshness && !retainVerifiedPosition && !admitRecentPosition
            || locationAge > VerifiedRemoteEntityTelemetry.LocationFreshness
                && !HasReliableEntityIdentity(entity))
        {
            reason = RemoteEntityRejectionReason.StaleLocation;
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
            && (!HasStablePlayerIdentity(entity)
                || entity.IsProvisional
                && (string.IsNullOrWhiteSpace(entity.SpeciesId)
                    || string.IsNullOrWhiteSpace(entity.SpeciesShortName))))
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

        reason = default;
        return false;
    }

    private static bool HasStablePlayerIdentity(VerifiedRemoteEntityTelemetry entity) =>
        entity.ActorNetRefHandle > 0
        || entity.PlayerStateNetRefHandle > 0
        || entity.PawnNetRefHandle > 0;

    private static bool HasReliableEntityIdentity(VerifiedRemoteEntityTelemetry entity) =>
        entity.Kind == RemoteEntityKind.Player
            ? HasStablePlayerIdentity(entity)
            : !string.IsNullOrWhiteSpace(entity.SpeciesId)
              && !string.IsNullOrWhiteSpace(entity.SpeciesShortName);

    private static bool IsStaleLocation(
        VerifiedRemoteEntityTelemetry entity,
        DateTimeOffset now)
    {
        var locationObservedAt = entity.LocationObservedAt ?? entity.ObservedAt;
        return locationObservedAt <= now
               && now - locationObservedAt
               > VerifiedRemoteEntityTelemetry.LocationFreshness
               && now - locationObservedAt
               <= (CanRetainVerifiedPosition(entity, now)
                   ? VerifiedRemoteEntityTelemetry.MaximumPositionRetention
                   : VerifiedRemoteEntityTelemetry.InitialPositionAdmission);
    }

    private static bool CanRetainVerifiedPosition(
        VerifiedRemoteEntityTelemetry entity, DateTimeOffset now) =>
        entity.HasVerifiedPosition
        && !entity.IsProvisional
        && HasVerifiedIdentity(entity)
        && entity.LocationObservedAt is { } positionAt
        && IsFresh(positionAt, now, VerifiedRemoteEntityTelemetry.MaximumPositionRetention)
        && entity.ObservedAt >= positionAt
        && IsFresh(entity.ObservedAt, now, VerifiedRemoteEntityTelemetry.PresenceRetention);

    // Admission and position verification are different facts. An admitted
    // player may have an unverified movement sample followed by owner-only
    // updates. Keep the exact previously displayed position as STALE, never
    // admit an unseen candidate here and never refresh its location timestamp.
    private static bool CanRetainAdmittedPlayer(
        VerifiedRemoteEntityTelemetry entity, MapMarkerTelemetry previous, DateTimeOffset now) =>
        entity.Kind == RemoteEntityKind.Player
        && !entity.IsProvisional && !previous.ProEntityIsProvisional
        && HasStablePlayerIdentity(entity)
        && previous.Location == entity.Location
        && entity.LocationObservedAt is { } positionAt
        && IsFresh(positionAt, now, VerifiedRemoteEntityTelemetry.MaximumPositionRetention)
        && entity.ObservedAt >= positionAt
        && IsFresh(entity.ObservedAt, now, VerifiedRemoteEntityTelemetry.PresenceRetention);

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
