using System.Threading.Channels;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class SdvnTelemetrySession(SdvnClient client, Action? authenticationExpired = null) : ITelemetrySession
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Channel<TelemetrySnapshot> _snapshots = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private IslePilotPlayerPage? _stats;
    private IslePilotMarker? _marker;
    private DateTimeOffset? _statsAt, _markerAt;
    private bool _authFailed;
    private bool _statsFailed;
    private int _started, _disposed;
    private Task? _run;
    public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Session already started.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        _run = RunAsync(lifetime.Token);
        try { await foreach (var snapshot in _snapshots.Reader.ReadAllAsync(cancellationToken)) yield return snapshot; }
        finally { lifetime.Cancel(); await _run; }
    }
    private async Task RunAsync(CancellationToken ct)
    {
        try { await Task.WhenAll(PollAsync(true, ct), PollAsync(false, ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { _snapshots.Writer.TryComplete(); }
    }
    private async Task PollAsync(bool stats, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (stats) { var value = await client.GetPlayerAsync(ct).ConfigureAwait(false); lock (_gate) { _stats = value; _statsFailed = false; _statsAt = DateTimeOffset.UtcNow; PublishLocked(); } }
                else
                {
                    var value = await client.GetMarkersAsync(ct).ConfigureAwait(false);
                    // Never choose the first teammate when self is absent.
                    lock (_gate) { _marker = value.Markers.FirstOrDefault(marker => marker.Self && double.IsFinite(marker.X) && double.IsFinite(marker.Y)); _markerAt = DateTimeOffset.UtcNow; PublishLocked(); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (IslePilotAuthenticationException)
            {
                lock (_gate)
                {
                    if (_authFailed) return;
                    _authFailed = true;
                    try { authenticationExpired?.Invoke(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    _snapshots.Writer.TryWrite(new TelemetrySnapshot { Source = client.Tenant.DisplayName, SessionState = TelemetrySessionState.AuthenticationRequired, StatusMessage = "Phiên SDVN hết hạn; quay lại Home để đăng nhập." });
                }
                _stop.Cancel(); return;
            }
            catch { lock (_gate) { if (stats) _statsFailed = true; PublishLocked(); } }
            await Task.Delay(TimeSpan.FromSeconds(stats ? 5 : 2.5), ct).ConfigureAwait(false);
        }
    }
    private void PublishLocked()
    {
        if (_authFailed) return;
        var now = DateTimeOffset.UtcNow;
        var stats = _statsAt is { } at && now - at <= TimeSpan.FromSeconds(15) ? _stats : null;
        var marker = _markerAt is { } mt && now - mt <= TimeSpan.FromSeconds(5) ? _marker : null;
        var online = stats?.Online == true || marker is not null;
        var stale = stats is null || _statsFailed;
        _snapshots.Writer.TryWrite(new TelemetrySnapshot
        {
            Source = client.Tenant.DisplayName, Success = true, ServerOnline = true, PlayerOnline = online,
            UpdatedAt = _statsAt, LiveDataStale = stale,
            SessionState = stale ? TelemetrySessionState.Stale : TelemetrySessionState.Live,
            StatusMessage = stale ? "Đang chờ stats SDVN · GPS vẫn cập nhật độc lập." : client.Tenant.DisplayName,
            Player = !online ? null : new PlayerTelemetry
            {
                Class = stats?.Species, Server = client.Tenant.DisplayName, ExactVitalsSource = "SDVN",
                GrowthPercent = stats?.GrowthPercent,
                ExactVitals = stats?.Online != true ? null : new ExactVitals { Growth = stats.GrowthPercent, Health = stats.Health, MaxHealth = stats.MaxHealth,
                    Hunger = stats.Hunger, MaxHunger = stats.MaxHunger, Thirst = stats.Thirst, MaxThirst = stats.MaxThirst, Stamina = stats.Stamina, MaxStamina = stats.MaxStamina },
                HealthPercent = Percent(stats?.Health, stats?.MaxHealth), HungerPercent = Percent(stats?.Hunger, stats?.MaxHunger),
                ThirstPercent = Percent(stats?.Thirst, stats?.MaxThirst), StaminaPercent = Percent(stats?.Stamina, stats?.MaxStamina),
                Location = marker is null ? null : new WorldLocation { X = marker.X, Y = marker.Y },
                ExactMapHeadingDegrees = marker?.Yaw is { } yaw && double.IsFinite(yaw) ? MapHeading.FromUnrealYaw(yaw) : null
            }
        });
    }
    private static double? Percent(double? current, double? max) => current is { } value && max is > 0 ? Math.Clamp(value / max.Value * 100, 0, 100) : null;
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); if (_run is not null) await _run; client.Dispose();
    }
}
