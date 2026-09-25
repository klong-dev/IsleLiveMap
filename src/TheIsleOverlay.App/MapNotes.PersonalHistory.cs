using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public sealed partial class MapNoteStore
{
    public MapNoteMutationResult AddHistory(WorldLocation location, string server, DateTimeOffset observedAt)
    {
        var point = GatewayMapProjection.ProjectUnclamped(location);
        if (string.IsNullOrWhiteSpace(server) || !double.IsFinite(point.Left) || !double.IsFinite(point.Top)
            || point.Left is < 0 or > 1 || point.Top is < 0 or > 1)
            return MapNoteMutationResult.Failed("Không có vị trí hợp lệ trên Gateway.");
        if (_notes.Count(n => n.IsPersonalHistory) >= 20)
            return MapNoteMutationResult.Failed("Đã đủ 20 mốc lịch sử; hãy xóa bớt mốc.");
        var note = new MapNote
        {
            U = point.Left, V = point.Top, WorldX = location.X, WorldY = location.Y,
            Kind = MapNoteKind.LastKnown, ServerKey = server, CreatedAt = observedAt,
            ExpiresAt = observedAt.AddHours(24)
        };
        _notes.Add(note);
        if (TrySaveAndNotify(out var error)) return MapNoteMutationResult.Succeeded(note);
        _notes.Remove(note);
        return MapNoteMutationResult.Failed(error!);
    }

    public void PurgeHistory(DateTimeOffset now)
    {
        var expired = _notes.Where(n => n.IsPersonalHistory && (n.ExpiresAt is null || n.ExpiresAt <= now)).ToArray();
        if (expired.Length == 0) return;
        _notes.RemoveAll(n => expired.Contains(n));
        if (!TrySaveAndNotify(out _)) _notes.AddRange(expired);
    }
}

/// <summary>Only local self snapshots; signal loss is never proof of death.</summary>
public sealed class PersonalHistoryTracker
{
    private WorldLocation? _last;
    private DateTimeOffset _observedAt;
    private DateTimeOffset? _lossAt;
    private bool _emitted;
    public string? ServerKey { get; private set; }

    public HistoryObservation? Finish()
    {
        if (_last is null || _emitted || ServerKey is null) return null;
        _emitted = true;
        return new(_last, ServerKey, _observedAt);
    }

    public HistoryObservation? Observe(TelemetrySnapshot? snapshot, DateTimeOffset now)
    {
        var player = snapshot?.Player;
        var server = player?.ServerEndpoint ?? player?.Server;
        var fresh = snapshot is { Success: true, PlayerOnline: true, LiveDataStale: false }
            && snapshot.UpdatedAt is { } at && now >= at && now - at <= TimeSpan.FromSeconds(3)
            && player?.Location is { } loc && double.IsFinite(loc.X) && double.IsFinite(loc.Y)
            && !string.IsNullOrWhiteSpace(server);
        if (fresh)
        {
            // Never compare locations across servers or reuse an old server's GPS.
            var key = server!.Trim().ToLowerInvariant();
            HistoryObservation? previous = ServerKey is not null && ServerKey != key && !_emitted && _last is not null
                ? new(_last, ServerKey, _observedAt) : null;
            ServerKey = key;
            _last = player!.Location;
            _observedAt = snapshot!.UpdatedAt!.Value;
            _lossAt = null;
            _emitted = false;
            return previous;
        }
        if (_last is null || _emitted) return null;
        _lossAt ??= now;
        if (now - _lossAt < TimeSpan.FromSeconds(10)) return null;
        _emitted = true;
        return new(_last, ServerKey!, _observedAt);
    }
}

public sealed record HistoryObservation(WorldLocation Location, string ServerKey, DateTimeOffset ObservedAt);
