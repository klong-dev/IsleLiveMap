namespace TheIsleOverlay.Core;

public enum TelemetrySessionState
{
    Polling,
    Connecting,
    Live,
    Reconnecting,
    Stale,
    UnsupportedServer,
    AuthenticationRequired,
    Stopped
}
