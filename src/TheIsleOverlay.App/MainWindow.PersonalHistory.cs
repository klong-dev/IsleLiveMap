using System.Windows.Threading;
using System.Net.Http;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private readonly PersonalHistoryTracker _historyTracker = new();
    private readonly PersonalMarkerSync _historySync = new();
    private readonly CancellationTokenSource _historyStop = new();
    private DispatcherTimer? _historyTimer;
    private bool _historySyncBusy;
    private bool _historyRelayEnabled;
    private bool _historyCleanupPending;
    private DateTimeOffset _historyNextSync;
    private string _historyStatus = "Mốc riêng trên máy · relay chưa bật";
    private DateTimeOffset _historyStarted;

    private void InitializePersonalHistory()
    {
        _historyStarted = DateTimeOffset.UtcNow;
        _historyTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            PersonalHistoryTick, Dispatcher);
        _historyTimer.Start();
    }

    private async void PersonalHistoryTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var oldServer = _historyTracker.ServerKey;
        var latest = LatestTelemetrySnapshotStore.Shared.ReceivedAt >= _historyStarted
            ? LatestTelemetrySnapshotStore.Shared.Current : null;
        var observation = _historyTracker.Observe(latest, now);
        if (observation is not null)
        {
            var result = _mapNoteStore.AddHistory(observation.Location, observation.ServerKey, observation.ObservedAt);
            if (!result.Success) _historyStatus = result.Error!;
        }
        _mapNoteStore.PurgeHistory(now);
        if (oldServer != _historyTracker.ServerKey) PositionMap();
        _mapNotesWindow?.UpdateHistoryContext(_historyTracker.ServerKey, _historyStatus);
        if ((!_historyRelayEnabled && !_historyCleanupPending) || _historySyncBusy || now < _historyNextSync) return;
        _historySyncBusy = true;
        var syncEnabled = _historyRelayEnabled;
        try
        {
            await _historySync.ReconcileAsync(syncEnabled ? _mapNoteStore.Notes.ToArray() : [], _historyStop.Token);
            _historyCleanupPending = syncEnabled && !_historyRelayEnabled;
            _historyStatus = _historyRelayEnabled ? "Relay đã đồng bộ · chỉ bạn xem/xóa" : "Đã xóa mốc trên relay · chỉ lưu máy";
            _historyNextSync = now.AddSeconds(5);
        }
        catch (OperationCanceledException) when (_historyStop.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException
            or System.IO.IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _historyStatus = "Relay chưa đồng bộ · mốc trên máy vẫn dùng được";
            _historyNextSync = now.AddSeconds(15);
        }
        finally { _historySyncBusy = false; }
    }

    private void SetHistoryRelayEnabled(bool enabled)
    {
        _historyCleanupPending |= _historyRelayEnabled && !enabled;
        _historyRelayEnabled = enabled;
        _historyStatus = enabled ? "Đang đồng bộ mốc cá nhân…" : "Mốc riêng trên máy · relay chưa bật";
        _historyNextSync = default;
    }

    private void DetachPersonalHistory()
    {
        _historyTimer?.Stop();
        if (_historyTracker.Finish() is { } last)
            _mapNoteStore.AddHistory(last.Location, last.ServerKey, last.ObservedAt);
        _historyStop.Cancel();
        _historySync.Dispose();
    }

    private IReadOnlyList<MapNote> VisibleLocalNotes() => _mapNoteStore.Notes
        .Where(n => n.IsPersonalHistory
            ? n.ExpiresAt > DateTimeOffset.UtcNow && n.ServerKey == _historyTracker.ServerKey
            : HasCurrentProFeatures).ToArray();
}
