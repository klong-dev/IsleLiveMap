namespace TheIsleOverlay.TeamRelay;

public sealed record CreateTeamRequest(string DisplayName);

public sealed record JoinTeamRequest(string InviteCode, string DisplayName);

public sealed record TeamSession(
    Guid TeamId,
    Guid MemberId,
    string InviteCode,
    string MemberToken,
    int MaxMembers,
    int HeartbeatIntervalSeconds);

public sealed record TeamTelemetryUpdate
{
    public long Sequence { get; init; }
    public string? Source { get; init; }
    public string? ServerKey { get; init; }
    public string? ServerName { get; init; }
    public string? MapId { get; init; }
    public string? Species { get; init; }
    public double? HealthPercent { get; init; }
    public double? HungerPercent { get; init; }
    public double? ThirstPercent { get; init; }
    public double? WorldX { get; init; }
    public double? WorldY { get; init; }
    public double? MapLeft { get; init; }
    public double? MapTop { get; init; }
    public double? HeadingDegrees { get; init; }
}

public sealed record TeamMemberTelemetry
{
    public long Sequence { get; init; }
    public string? Source { get; init; }
    public string? ServerKey { get; init; }
    public string? ServerName { get; init; }
    public string? MapId { get; init; }
    public string? Species { get; init; }
    public double? HealthPercent { get; init; }
    public double? HungerPercent { get; init; }
    public double? ThirstPercent { get; init; }
    public double? WorldX { get; init; }
    public double? WorldY { get; init; }
    public double? MapLeft { get; init; }
    public double? MapTop { get; init; }
    public double? HeadingDegrees { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TeamMemberSnapshot(
    Guid MemberId,
    string DisplayName,
    bool IsOnline,
    DateTimeOffset LastSeenAt,
    TeamMemberTelemetry? Telemetry);

public sealed record TeamSnapshot(
    Guid TeamId,
    string InviteCode,
    IReadOnlyList<TeamMemberSnapshot> Members,
    IReadOnlyList<TeamMapPingSnapshot>? MapPings = null);

public sealed record TeamMapPingMutation
{
    public Guid PingId { get; init; }
    public long ExpectedRevision { get; init; }
    public string MapId { get; init; } = "gateway";
    public int Kind { get; init; }
    public double MapLeft { get; init; }
    public double MapTop { get; init; }
    public double WorldX { get; init; }
    public double WorldY { get; init; }
}

public sealed record TeamMapPingSnapshot(
    Guid PingId,
    Guid OwnerMemberId,
    string OwnerDisplayName,
    long Revision,
    string MapId,
    int Kind,
    double MapLeft,
    double MapTop,
    double WorldX,
    double WorldY,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamApiError(string Code, string Message);
