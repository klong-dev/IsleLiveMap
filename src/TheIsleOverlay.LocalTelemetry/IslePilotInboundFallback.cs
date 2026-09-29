using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>Provider priority and Prime are independent of inbound stat freshness.</summary>
internal static class IslePilotInboundFallback
{
    internal static readonly TimeSpan ProviderSilence = TimeSpan.FromSeconds(15);

    internal static TelemetrySnapshot Merge(TelemetrySnapshot? remote, LocalMovementObservation? local,
        DateTimeOffset now, string sourceName, IReadOnlyList<VerifiedRemoteEntityTelemetry>? entities,
        string? species, RemotePlayerTelemetryFrame? fallback, bool requireGps)
    {
        var isProvider = remote is not null && (remote.Source.Equals("IslePilot", StringComparison.OrdinalIgnoreCase)
            || remote.Player?.ExactVitalsSource?.StartsWith("IslePilot", StringComparison.OrdinalIgnoreCase) == true);
        var isOtherProvider = remote?.Player?.ExactVitalsSource is { Length: > 0 } source
            && source != LocalVitalsFeature.SourceName && !isProvider;
        var standardMap = sourceName.Equals("IslePilot", StringComparison.OrdinalIgnoreCase)
            || sourceName.Equals("INBOUND", StringComparison.OrdinalIgnoreCase)
            || sourceName.Equals("LOCAL", StringComparison.OrdinalIgnoreCase);
        if (isOtherProvider || !standardMap || (remote is not null && !isProvider
            && remote.Source is not "INBOUND" and not "Unknown"
            && remote.Player?.ExactVitalsSource != LocalVitalsFeature.SourceName))
            return LocalPositionSnapshotMerger.Merge(remote, local, now, sourceName, entities, species, fallback,
                allowLocalVitals: true, requireFreshLocalMovement: requireGps);

        var endpoint = remote?.Player?.ServerEndpoint;
        var localEndpoint = local?.ServerEndpoint;
        var wrongServer = !string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(localEndpoint)
            && !endpoint.Equals(localEndpoint, StringComparison.OrdinalIgnoreCase);
        var online = remote is { Success: true, ServerOnline: true, PlayerOnline: true, LiveDataStale: false, Player: not null }
            && remote.SessionState is TelemetrySessionState.Live or TelemetrySessionState.Polling or TelemetrySessionState.Connecting
            && (remote.ProviderStatsObservedAt ?? remote.UpdatedAt) is { } updated && updated <= now && now - updated <= ProviderSilence;
        // Growth/Prime alone must not suppress fallback for missing four-stat data.
        var values = remote?.Player;
        var hasStats = values?.ExactVitals is { } exact && (Valid(exact.Health) || Valid(exact.Stamina)
                || Valid(exact.Hunger) || Valid(exact.Thirst))
            || Valid(values?.HealthPercent) || Valid(values?.StaminaPercent)
            || Valid(values?.HungerPercent) || Valid(values?.ThirstPercent);
        if (isProvider && online && hasStats && !wrongServer)
        {
            var primary = LocalPositionSnapshotMerger.Merge(remote, local, now, sourceName, entities, species, fallback,
                allowLocalVitals: false, requireFreshLocalMovement: requireGps);
            return primary.Player is null ? primary : primary with { Player = primary.Player with
            { PrimeDataStale = primary.Player.Prime is not null && remote!.ProviderPrimeObservedAt is { } primaryPrimeAt
                && now - primaryPrimeAt > TimeSpan.FromSeconds(30) } };
        }

        // No metadata from explicitly unsupported/offline/other-server identities.
        // During a transport outage keep Prime that the reducer retained for the
        // same identity; its own state is marked stale, never made live by GPS.
        var retainIdentity = isProvider && !wrongServer && remote?.PlayerOnline == true
            && remote.SessionState != TelemetrySessionState.UnsupportedServer;
        var providerPlayer = retainIdentity ? remote!.Player : null;
        var authRequired = remote?.SessionState == TelemetrySessionState.AuthenticationRequired;
        var seed = (remote ?? new TelemetrySnapshot()) with
        {
            Source = "INBOUND",
            Player = providerPlayer is null ? null : new PlayerTelemetry
            {
                Name = providerPlayer.Name, SteamId = providerPlayer.SteamId,
                Prime = providerPlayer.Prime, Nutrition = providerPlayer.Nutrition,
                Server = providerPlayer.Server, ServerEndpoint = localEndpoint ?? providerPlayer.ServerEndpoint
            },
            SessionState = TelemetrySessionState.Connecting,
            LiveDataStale = false,
            StatusMessage = "Stats inbound dự phòng; IslePilot đang chờ kết nối."
        };
        var merged = LocalPositionSnapshotMerger.Merge(seed, local, now, sourceName, entities, species, fallback,
            allowLocalVitals: true, requireFreshLocalMovement: requireGps, replaceIslePilotStats: true);
        // Do not mask missing local data behind an online synthetic provider.
        if (merged.Player is null) return merged;
        if (!merged.Player.InboundStatsExperimental && merged.Player.ExactVitalsSource != LocalVitalsFeature.SourceName
            && merged.Player.Location is null && merged.Player.MapLocation is null)
            return remote ?? merged;
        return merged with
        {
            Player = merged.Player with
            {
                Class = providerPlayer?.Class ?? merged.Player.Class,
                Prime = providerPlayer?.Prime,
                Nutrition = providerPlayer?.Nutrition,
                PrimeDataStale = providerPlayer?.Prime is not null && (!online || authRequired
                    || remote!.ProviderPrimeObservedAt is { } primeAt && now - primeAt > TimeSpan.FromSeconds(30)),
                InboundStatsFallback = true,
                ProviderAuthenticationRequired = authRequired
            }
        };
    }
    private static bool Valid(double? value) => value is >= 0d && double.IsFinite(value.Value);
}
