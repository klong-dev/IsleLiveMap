using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

public static class LocalPositionSnapshotMerger
{
    public static readonly TimeSpan LocalFreshness = TimeSpan.FromSeconds(2);
    // Pro/Iris frames arrive in sparse bursts and live measurements after a
    // reconnect showed healthy gaps of roughly 1.1-4.3 seconds. Two seconds
    // made an unchanged player roster flash to zero between valid frames.
    // Endpoint changes still publish an empty roster immediately; this grace
    // period is only the pipeline-liveness fallback for a stalled frame.
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
        RemotePlayerTelemetryFrame? verifiedLocalFallback = null)
    {
        var localObservation = local.GetValueOrDefault();
        var fallback = verifiedLocalFallback;
        var hasFreshLocal = local.HasValue
                            && now - localObservation.ObservedAt <= LocalFreshness;
        var hasFreshVerifiedFallback = fallback is not null
                                       && IsRemoteFrameFresh(fallback, now)
                                       && IsFinite(fallback.LocalLocation)
                                       && double.IsFinite(fallback.MapHeadingDegrees);
        if (!hasFreshLocal && !hasFreshVerifiedFallback)
        {
            return remote is null
                ? Waiting(sourceName)
                : remote;
        }

        var baseSnapshot = remote is null
            ? new TelemetrySnapshot()
            : remote;
        var remotePlayer = baseSnapshot.Player;
        var location = hasFreshLocal
            ? localObservation.Movement.Location
            : verifiedLocalFallback!.LocalLocation;
        var mapHeadingDegrees = hasFreshLocal
            ? localObservation.Movement.MapHeadingDegrees
            : MapHeading.Normalize(verifiedLocalFallback!.MapHeadingDegrees);
        var serverEndpoint = hasFreshLocal
            ? localObservation.ServerEndpoint
            : verifiedLocalFallback!.ServerEndpoint;
        var observedAt = hasFreshLocal
            ? localObservation.ObservedAt
            : verifiedLocalFallback!.ObservedAt;
        var player = (remotePlayer ?? new PlayerTelemetry
        {
            Name = "LOCAL PLAYER"
        }) with
        {
            Server = string.IsNullOrWhiteSpace(remotePlayer?.Server)
                ? serverEndpoint
                : remotePlayer.Server,
            Class = string.IsNullOrWhiteSpace(verifiedLocalSpeciesId)
                ? remotePlayer?.Class
                : verifiedLocalSpeciesId.Trim(),
            Location = location,
            MapLocation = null,
            ExactMapHeadingDegrees = mapHeadingDegrees
        };

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
            Map = MergeRemotePlayers(
                baseSnapshot.Map,
                remotePlayers,
                location),
            ProPlayerTrackingActive = remotePlayers is not null,
            ProPlayerSync = verifiedLocalFallback?.PlayerSync,
            SessionState = TelemetrySessionState.Live,
            LiveDataStale = false,
            StatusMessage = baseSnapshot.SessionState == TelemetrySessionState.UnsupportedServer
                ? "Map trực tiếp đang hoạt động; status và nhiệm vụ IslePilot không khả dụng trên server này."
                : baseSnapshot.StatusMessage
        };
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
