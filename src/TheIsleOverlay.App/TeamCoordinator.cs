using TheIsleOverlay.Core;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public sealed class TeamCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(100);

    private readonly TeamRelayClient _client;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _telemetryGate = new();
    private readonly Task _publishTask;

    private LatestTelemetry _latestTelemetry = new(null, null);
    private long _telemetryVersion;
    private long _publishedVersion = -1;
    private long _sequence;
    private Guid? _activeTeamId;
    private TeamRelayConnectionState _lastConnectionState;
    private bool _disposed;

    public TeamCoordinator(TeamRelayClient? client = null)
    {
        _client = client ?? new TeamRelayClient();
        _client.StateChanged += Client_StateChanged;
        _publishTask = PublishLoopAsync(_shutdown.Token);
    }

    public event EventHandler<TeamRelayState>? StateChanged;

    public TeamRelayState CurrentState => _client.CurrentState;

    public Task<TeamSession> CreateAsync(
        string displayName,
        CancellationToken cancellationToken = default) =>
        _client.CreateAsync(displayName.Trim(), cancellationToken);

    public Task<TeamSession> JoinAsync(
        string inviteCode,
        string displayName,
        CancellationToken cancellationToken = default) =>
        _client.JoinAsync(inviteCode, displayName.Trim(), cancellationToken);

    public Task LeaveAsync(CancellationToken cancellationToken = default) =>
        _client.LeaveAsync(cancellationToken);

    public void UpdateTelemetry(TelemetrySnapshot snapshot, double? fallbackHeadingDegrees)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_telemetryGate)
        {
            _latestTelemetry = new LatestTelemetry(snapshot, fallbackHeadingDegrees);
            _telemetryVersion++;
        }
    }

    public void ClearTelemetry()
    {
        lock (_telemetryGate)
        {
            _latestTelemetry = new LatestTelemetry(null, null);
            _telemetryVersion++;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.StateChanged -= Client_StateChanged;
        _shutdown.Cancel();
        try
        {
            await _publishTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _client.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task PublishLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PublishInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_client.CurrentState.ConnectionState != TeamRelayConnectionState.Live)
            {
                continue;
            }

            LatestTelemetry latest;
            long version;
            lock (_telemetryGate)
            {
                version = _telemetryVersion;
                if (version == _publishedVersion)
                {
                    continue;
                }

                latest = _latestTelemetry;
            }

            var update = TeamTelemetryMapper.Create(
                latest.Snapshot,
                Interlocked.Increment(ref _sequence),
                latest.FallbackHeadingDegrees);
            var accepted = await _client.PublishTelemetryAsync(update, cancellationToken)
                .ConfigureAwait(false);
            if (!accepted)
            {
                continue;
            }

            lock (_telemetryGate)
            {
                if (_telemetryVersion == version)
                {
                    _publishedVersion = version;
                }
            }
        }
    }

    private void Client_StateChanged(object? sender, TeamRelayState state)
    {
        lock (_telemetryGate)
        {
            var teamId = state.Session?.TeamId;
            if (teamId != _activeTeamId)
            {
                _activeTeamId = teamId;
                _sequence = 0;
                _publishedVersion = -1;
            }
            else if (state.ConnectionState == TeamRelayConnectionState.Live
                     && _lastConnectionState != TeamRelayConnectionState.Live)
            {
                _publishedVersion = -1;
            }

            _lastConnectionState = state.ConnectionState;
        }

        StateChanged?.Invoke(this, state);
    }

    private sealed record LatestTelemetry(
        TelemetrySnapshot? Snapshot,
        double? FallbackHeadingDegrees);
}
