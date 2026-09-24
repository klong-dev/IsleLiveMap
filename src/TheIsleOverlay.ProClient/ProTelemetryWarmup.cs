using TheIsleOverlay.Core;

namespace TheIsleOverlay.ProClient;

/// <summary>Owns a prewarmed stream only until the launcher transfers it to the map.</summary>
public sealed class ProTelemetryWarmup(
    Func<IRemotePlayerTelemetrySource?> sourceFactory,
    TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private PrewarmedRemotePlayerTelemetrySource? _source;
    private SourceIdentity? _identity;
    private bool _transferred;
    private bool _disposed;

    public async Task RefreshAsync(ProAccessSnapshot access, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(access, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<IRemotePlayerTelemetrySource?> TakeAsync(
        ProAccessSnapshot access, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || _transferred) return null;
            await RefreshCoreAsync(access, cancellationToken).ConfigureAwait(false);
            var source = _source;
            if (source is null)
            {
                return null;
            }

            _source = null;
            _identity = null;
            _transferred = true;
            return source;
        }
        finally { _gate.Release(); }
    }

    // The map-open failure path must dispose its transferred source first.
    public async Task CancelHandoffAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) _transferred = false; }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task RefreshCoreAsync(ProAccessSnapshot access, CancellationToken cancellationToken)
    {
        if (_disposed || _transferred) return;
        var now = _clock.GetUtcNow();
        if (!access.IsAuthenticated || !access.Entitlement.IsProAt(now)
            || access.OfflineLicenseExpiresAt is { } expiry && expiry <= now)
        {
            await StopCoreAsync().ConfigureAwait(false);
            return;
        }

        var identity = new SourceIdentity(access.SteamId64!, access.AgentVersion, access.OfflineLicenseExpiresAt);
        if (_source is not null && _identity == identity && !_source.IsCompleted) return;
        await StopCoreAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // AgentReady is presentation state, not authorization. The service
        // factory still requires a signed installed Agent and usable license.
        if (sourceFactory() is not { } inner) return;
        var source = new PrewarmedRemotePlayerTelemetrySource(inner);
        try { source.Start(); }
        catch
        {
            await source.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        _source = source;
        _identity = identity;
    }

    private async Task StopCoreAsync()
    {
        var source = _source;
        _source = null;
        _identity = null;
        if (source is not null) await source.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record SourceIdentity(string SteamId, string? AgentVersion, DateTimeOffset? LicenseExpiry);
}
