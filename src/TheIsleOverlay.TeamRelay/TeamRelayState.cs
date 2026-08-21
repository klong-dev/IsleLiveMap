namespace TheIsleOverlay.TeamRelay;

public enum TeamRelayConnectionState
{
    None,
    Connecting,
    Live,
    Reconnecting,
    Expired,
    Error
}

public sealed record TeamRelayState
{
    public TeamRelayConnectionState ConnectionState { get; init; }
    public TeamSession? Session { get; init; }
    public IReadOnlyList<TeamMemberSnapshot> Members { get; init; } = [];
    public string? Message { get; init; }

    public bool HasActiveSession => Session is not null
        && ConnectionState is TeamRelayConnectionState.Connecting
            or TeamRelayConnectionState.Live
            or TeamRelayConnectionState.Reconnecting;
}

public sealed class TeamRelayApiException(
    string code,
    string message,
    int statusCode,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
