namespace TheIsleOverlay.Core;

public enum RemoteEntityLifecycleState
{
    Discovered = 1,
    Eligible = 2,
    Visible = 3,
    Updated = 4,
    TemporarilyMissing = 5,
    Stale = 6,
    Removed = 7
}

public sealed record RemoteEntityLifecycleSnapshot(
    string Key,
    RemoteEntityKind Kind,
    long TrackId,
    RemoteEntityLifecycleState State,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? LastRenderedAt,
    bool IsProvisional,
    string? ServerEndpoint,
    string? RemovalReason = null);

/// <summary>
/// Stateful lifecycle reducer for replay and live diagnostics. It deliberately
/// separates a missing frame from a new empty roster: only the latter can
/// transition an entity to TemporarilyMissing immediately.
/// </summary>
public sealed class RemoteEntityLifecycleTracker
{
    public static readonly TimeSpan PositionFreshness = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RemoveAfter = TimeSpan.FromSeconds(6);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private string? _serverEndpoint;
    private string? _sessionId;
    private long? _lastSequence;
    private long _sessionGeneration;

    public long SessionGeneration => _sessionGeneration;

    public IReadOnlyList<RemoteEntityLifecycleSnapshot> ApplyFrame(
        RemotePlayerTelemetryFrame frame,
        DateTimeOffset now,
        bool rendered = false)
    {
        var endpoint = NormalizeEndpoint(frame.ServerEndpoint);
        if (!string.Equals(endpoint, _serverEndpoint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(frame.SessionId, _sessionId, StringComparison.Ordinal))
        {
            Reset(endpoint);
            _sessionId = frame.SessionId;
        }
        if (_lastSequence is { } sequence && frame.Sequence <= sequence)
            return AdvanceWithoutFrame(now);
        _lastSequence = frame.Sequence;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in frame.RemoteEntities)
        {
            var key = CreateKey(_sessionGeneration, endpoint, entity.Kind, entity.TrackId);
            if (!seen.Add(key))
            {
                continue;
            }

            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry(key, entity.Kind, entity.TrackId, now, endpoint);
                _entries[key] = entry;
            }

            entry.LastSeenAt = now;
            entry.LastRenderedAt = rendered ? now : entry.LastRenderedAt;
            entry.IsProvisional = entity.IsProvisional;
            entry.RemovalReason = null;
            entry.State = rendered
                ? entry.State is RemoteEntityLifecycleState.Discovered
                    or RemoteEntityLifecycleState.Eligible
                    or RemoteEntityLifecycleState.TemporarilyMissing
                    or RemoteEntityLifecycleState.Stale
                    ? RemoteEntityLifecycleState.Visible
                    : RemoteEntityLifecycleState.Updated
                : RemoteEntityLifecycleState.Discovered;
        }

        // A fresh frame with an empty roster is authoritative and therefore
        // marks all prior entities missing. A frame with entities also marks
        // omitted entities missing, rather than silently keeping them live.
        foreach (var entry in _entries.Values.ToArray())
        {
            if (!seen.Contains(entry.Key) && entry.State != RemoteEntityLifecycleState.Removed)
            {
                entry.State = RemoteEntityLifecycleState.TemporarilyMissing;
            }
        }

        return Snapshot(now);
    }

    public IReadOnlyList<RemoteEntityLifecycleSnapshot> AdvanceWithoutFrame(DateTimeOffset now)
    {
        foreach (var entry in _entries.Values.ToArray())
        {
            if (entry.State == RemoteEntityLifecycleState.Removed)
            {
                continue;
            }

            var age = now - entry.LastSeenAt;
            if (age > RemoveAfter)
            {
                entry.State = RemoteEntityLifecycleState.Removed;
                entry.RemovalReason = "RemoteFrameTimeout";
            }
            else if (age > StaleAfter)
            {
                entry.State = RemoteEntityLifecycleState.Stale;
            }
        }

        return Snapshot(now);
    }

    public void Reset(string? serverEndpoint = null)
    {
        _entries.Clear();
        _serverEndpoint = NormalizeEndpoint(serverEndpoint);
        _sessionId = null;
        _lastSequence = null;
        _sessionGeneration++;
    }

    public static string CreateKey(
        long sessionGeneration,
        string? serverEndpoint,
        RemoteEntityKind kind,
        long trackId) =>
        $"{sessionGeneration}:{NormalizeEndpoint(serverEndpoint) ?? "unknown"}:{kind}:{trackId}";

    private IReadOnlyList<RemoteEntityLifecycleSnapshot> Snapshot(DateTimeOffset now) =>
        _entries.Values
            .Select(entry => new RemoteEntityLifecycleSnapshot(
                entry.Key,
                entry.Kind,
                entry.TrackId,
                entry.State,
                entry.FirstSeenAt,
                entry.LastSeenAt,
                entry.LastRenderedAt,
                entry.IsProvisional,
                entry.ServerEndpoint,
                entry.RemovalReason))
            .ToArray();

    private static string? NormalizeEndpoint(string? endpoint) =>
        string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();

    private sealed class Entry(
        string key,
        RemoteEntityKind kind,
        long trackId,
        DateTimeOffset firstSeenAt,
        string? serverEndpoint)
    {
        public string Key { get; } = key;
        public RemoteEntityKind Kind { get; } = kind;
        public long TrackId { get; } = trackId;
        public DateTimeOffset FirstSeenAt { get; } = firstSeenAt;
        public DateTimeOffset LastSeenAt { get; set; } = firstSeenAt;
        public DateTimeOffset? LastRenderedAt { get; set; }
        public bool IsProvisional { get; set; }
        public string? ServerEndpoint { get; } = serverEndpoint;
        public RemoteEntityLifecycleState State { get; set; } =
            RemoteEntityLifecycleState.Discovered;
        public string? RemovalReason { get; set; }
    }
}
