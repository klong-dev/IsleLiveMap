using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

/// <summary>Read-only bridge between the live overlay and the launcher.</summary>
public sealed class LatestTelemetrySnapshotStore
{
    private readonly object _gate = new();
    private TelemetrySnapshot? _current;
    private DateTimeOffset? _receivedAt;

    public static LatestTelemetrySnapshotStore Shared { get; } = new();

    public TelemetrySnapshot? Current { get { lock (_gate) return _current; } }
    public DateTimeOffset? ReceivedAt { get { lock (_gate) return _receivedAt; } }
    public event EventHandler? Changed;

    public void Update(TelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _current = snapshot;
            _receivedAt = DateTimeOffset.UtcNow;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate) { _current = null; _receivedAt = null; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
