using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

public static class LocalPositionSnapshotMerger
{
    public static readonly TimeSpan LocalFreshness = TimeSpan.FromSeconds(2);

    public static TelemetrySnapshot Merge(
        TelemetrySnapshot? remote,
        LocalMovementObservation? local,
        DateTimeOffset now,
        string sourceName = "LOCAL")
    {
        if (local is not { } observation
            || now - observation.ObservedAt > LocalFreshness)
        {
            return remote ?? Waiting(sourceName);
        }

        var baseSnapshot = remote ?? new TelemetrySnapshot();
        var remotePlayer = baseSnapshot.Player;
        var movement = observation.Movement;
        var player = (remotePlayer ?? new PlayerTelemetry
        {
            Name = "LOCAL PLAYER"
        }) with
        {
            Server = string.IsNullOrWhiteSpace(remotePlayer?.Server)
                ? observation.ServerEndpoint
                : remotePlayer.Server,
            Location = movement.Location,
            MapLocation = null,
            ExactMapHeadingDegrees = movement.MapHeadingDegrees
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
            UpdatedAt = observation.ObservedAt,
            Player = player,
            SessionState = TelemetrySessionState.Live,
            LiveDataStale = false,
            StatusMessage = baseSnapshot.SessionState == TelemetrySessionState.UnsupportedServer
                ? "Vị trí trực tiếp đang hoạt động; server không cung cấp status."
                : baseSnapshot.StatusMessage
        };
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
