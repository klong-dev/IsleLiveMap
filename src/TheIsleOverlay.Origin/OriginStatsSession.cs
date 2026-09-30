using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.Origin;

public sealed class OriginStatsSession : ITelemetrySession
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan PrimeInterval = TimeSpan.FromSeconds(15);
    // Field state survives polling gaps; explicit no-dino/server changes reset it.
    private readonly IOriginStatsClient _client;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<TelemetrySnapshot> _snapshots = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private OriginServer? _activeServer;
    private TelemetrySnapshot? _lastLiveSnapshot;
    private DateTimeOffset _lastLiveAt;
    private DateTimeOffset _lastPrimeAt;
    private PrimeTelemetry? _prime;
    private int _generation;
    private bool _degraded;
    private bool _authenticationFailed;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private DateTimeOffset _nextHealthAt;
    private int _discoveryIndex;
    private bool _explicitNoDino;
    private Task? _runTask;
    private int _watchStarted;
    private int _disposed;

    public OriginStatsSession(IOriginStatsClient client, OriginServer? activeServer = null, TimeProvider? timeProvider = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _activeServer = activeServer ?? client.PreferredServer;
        _clock = timeProvider ?? TimeProvider.System;
    }

    public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException("An Origin stats session can only be watched once.");
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        yield return new TelemetrySnapshot
        {
            Source = "ORIGIN x5",
            Success = false,
            SessionState = TelemetrySessionState.Connecting,
            StatusMessage = "Đang tìm dino trên Main Origin và Voice Chat Server…"
        };

        _runTask = RunAsync(lifetime.Token);
        try
        {
            // Internal cancellation completes the writer after publishing the
            // authentication error. Only caller cancellation may skip draining.
            await foreach (var snapshot in _snapshots.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return snapshot;
        }
        finally { lifetime.Cancel(); await _runTask.ConfigureAwait(false); }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try { await Task.WhenAll(HealthLoopAsync(cancellationToken), PrimeLoopAsync(cancellationToken), FreshnessLoopAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { _snapshots.Writer.TryComplete(); }
    }

    private async Task HealthLoopAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var started = _clock.GetUtcNow();
            try
            {
                OriginServer? selected;
                lock (_gate) selected = _activeServer;
                var requestStarted = _clock.GetUtcNow();
                var health = selected is null ? null : await RequestHealthAsync(selected, ct).ConfigureAwait(false);
                if (health is null || health.Status == "completed"
                    && (!health.IsCompletedSuccessfully || ParsePlayer(health.Result!.Value, selected!) is null))
                {
                    // A timeout is not proof that the player changed servers.
                    // Only rediscover after a terminal no-dino result.
                    if (selected is not null)
                    {
                        lock (_gate)
                        {
                            _discoveryIndex = (OriginServer.All.ToList().IndexOf(selected) + 1) % OriginServer.All.Count;
                            _activeServer = null; _lastLiveSnapshot = null; _prime = null; _generation++;
                            _explicitNoDino = true;
                            PublishLocked();
                        }
                    }
                    var found = await DiscoverAsync(ct).ConfigureAwait(false);
                    selected = found.Server; health = found.Health;
                    if (health?.Status == "completed" && (!health.IsCompletedSuccessfully
                        || ParsePlayer(health.Result!.Value, selected!) is null))
                    {
                        lock (_gate) { _explicitNoDino = true; PublishLocked(); }
                    }
                }
                var player = selected is not null && health?.IsCompletedSuccessfully == true
                    && (health.RequestedAt is null || health.RequestedAt <= _clock.GetUtcNow())
                    ? ParsePlayer(health.Result!.Value, selected) : null;
                lock (_gate)
                {
                    if (_authenticationFailed) return;
                    if (player is not null)
                    {
                        if (_activeServer != selected || _lastLiveSnapshot?.Player?.Class != player.Class
                            || _lastLiveSnapshot?.Player?.GrowthPercent is { } previousGrowth
                                && player.GrowthPercent is { } currentGrowth && currentGrowth < previousGrowth - 5)
                        { _generation++; _prime = null; _lastPrimeAt = default; }
                        _activeServer = selected;
                        _explicitNoDino = false;
                        // The API has no reliable measurement timestamp; keep
                        // request time as a conservative age bound for queued work.
                        _lastLiveAt = health!.RequestedAt ?? requestStarted;
                        _lastLiveSnapshot = new TelemetrySnapshot
                        {
                            Source = "ORIGIN x5", Success = true, ServerOnline = true, PlayerOnline = true,
                            UpdatedAt = _lastLiveAt, Player = player, SessionState = TelemetrySessionState.Live,
                            ProviderStatsRequestedAt = _lastLiveAt, ProviderStatsObservedAt = _lastLiveAt,
                            ProviderStatsScope = $"{_sessionId}/{selected!.ApiId}/{_generation}",
                            StatusMessage = $"Origin · {selected!.DisplayName}"
                        };
                        _degraded = _clock.GetUtcNow() - _lastLiveAt > PollInterval + TimeSpan.FromSeconds(3); failures = 0;
                    }
                    else
                    {
                        _degraded = true; failures++;
                        // Repeated command failure permits discovery, not a
                        // fabricated death/reset of the retained baseline.
                        if (health?.Status == "failed" && failures >= 3 && selected is not null)
                        {
                            _discoveryIndex = (OriginServer.All.ToList().IndexOf(selected) + 1) % OriginServer.All.Count;
                            _activeServer = null;
                            failures = 0;
                        }
                    }
                    PublishLocked();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (OriginAuthenticationException) { InvalidateAuthentication(); return; }
            catch { lock (_gate) { _degraded = true; failures++; PublishLocked(); } }
            var period = PollInterval;
            var delay = period - (_clock.GetUtcNow() - started);
            await Task.Delay(delay > TimeSpan.FromMilliseconds(250) ? delay : TimeSpan.FromMilliseconds(250), _clock, ct);
        }
    }

    private async Task<(OriginServer? Server, OriginCommandResult? Health)> DiscoverAsync(CancellationToken ct)
    {
        // Discovery obeys the same account-wide health cadence, never fan-out.
        var server = OriginServer.All[_discoveryIndex++ % OriginServer.All.Count];
        var health = await RequestHealthAsync(server, ct).ConfigureAwait(false);
        return (server, health);
    }

    private async Task<OriginCommandResult> RequestHealthAsync(OriginServer server, CancellationToken ct)
    {
        var wait = _nextHealthAt - _clock.GetUtcNow();
        if (wait > TimeSpan.Zero) await Task.Delay(wait, _clock, ct).ConfigureAwait(false);
        var started = _clock.GetUtcNow();
        _nextHealthAt = started + PollInterval;
        WriteDiagnostic("health-start", server, started, null, null);
        try
        {
            var result = await _client.ExecuteHealthAsync(server, ct).ConfigureAwait(false);
            WriteDiagnostic("health-result", server, started, result, null);
            return result with { RequestedAt = result.RequestedAt ?? started };
        }
        catch (Exception error)
        {
            WriteDiagnostic("health-error", server, started, null, error.GetType().Name);
            throw;
        }
    }

    private void WriteDiagnostic(string stage, OriginServer server, DateTimeOffset started, OriginCommandResult? result, string? error)
    {
        var path = Environment.GetEnvironmentVariable("ISLE_ORIGIN_DIAGNOSTICS_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var payload = result?.Result;
            var fields = new Dictionary<string, double?>();
            if (payload is { ValueKind: JsonValueKind.Object } value)
                foreach (var name in new[] { "hp", "maxHp", "stamina", "stam", "maxStamina", "maxStam", "hunger", "maxHunger", "thirst", "maxThirst", "growth" })
                    fields[name] = ReadDouble(value, name);
            // Explicit allowlist: never log cookies, tokens, player identity or arbitrary response values.
            File.AppendAllText(path, JsonSerializer.Serialize(new { At = _clock.GetUtcNow(), Stage = stage,
                Server = server.ApiId, Started = started, result?.RequestedAt, result?.Status,
                Completed = result?.IsCompletedSuccessfully, ErrorType = error, Fields = fields,
                Keys = payload is { ValueKind: JsonValueKind.Object } obj ? obj.EnumerateObject().Select(p => p.Name).Take(40).ToArray() : []
            }) + Environment.NewLine);
        }
        catch { /* Diagnostics cannot interrupt stats polling. */ }
    }

    private async Task PrimeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            OriginServer? server; int generation;
            lock (_gate)
            {
                if (_authenticationFailed) return;
                server = _lastLiveSnapshot is not null
                    && _clock.GetUtcNow() - _lastPrimeAt >= PrimeInterval ? _activeServer : null;
                generation = _generation;
                if (server is not null) _lastPrimeAt = _clock.GetUtcNow();
            }
            if (server is not null)
            {
                try
                {
                    var result = await _client.ExecutePrimeAsync(server, ct).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (!_authenticationFailed && generation == _generation && server == _activeServer)
                        {
                            _prime = result.IsCompletedSuccessfully
                                && (result.RequestedAt is null || _clock.GetUtcNow() - result.RequestedAt <= PrimeInterval)
                                ? ParsePrime(result.Result!.Value)
                                : (_prime ?? new PrimeTelemetry()) with { IsSynchronizing = true };
                            PublishLocked();
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (OriginAuthenticationException) { InvalidateAuthentication(); return; }
                catch { lock (_gate) { if (generation == _generation) { _prime = (_prime ?? new PrimeTelemetry()) with { IsSynchronizing = true }; PublishLocked(); } } }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), _clock, ct);
        }
    }

    private async Task FreshnessLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), _clock, ct);
            lock (_gate)
            {
                if (_authenticationFailed) return;
                if (_lastLiveSnapshot is not null && _clock.GetUtcNow() - _lastLiveAt > PollInterval + TimeSpan.FromSeconds(3))
                { _degraded = true; PublishLocked(); }
            }
        }
    }

    private void InvalidateAuthentication()
    {
        lock (_gate)
        {
            _authenticationFailed = true; _lastLiveSnapshot = null; _prime = null;
            _snapshots.Writer.TryWrite(new TelemetrySnapshot
            { Source = "ORIGIN x5", SessionState = TelemetrySessionState.AuthenticationRequired, StatusMessage = "Phiên Origin hết hạn; hãy đăng nhập lại." });
            _stop.Cancel();
        }
    }

    private void PublishLocked()
    {
        if (_authenticationFailed) return;
        var recent = _lastLiveSnapshot is not null;
        var snapshot = recent ? _lastLiveSnapshot! with
        {
            Player = _lastLiveSnapshot!.Player! with { Prime = _prime ?? new PrimeTelemetry { IsSynchronizing = true } },
            SessionState = _degraded ? TelemetrySessionState.Stale : TelemetrySessionState.Live,
            LiveDataStale = _degraded,
            StatusMessage = _degraded ? "Origin phản hồi chậm · giữ baseline, inbound tiếp tục cập nhật." : _lastLiveSnapshot.StatusMessage
        }
        : new TelemetrySnapshot
        {
            Source = "ORIGIN x5", Success = true, ServerOnline = true, LiveDataStale = true,
            ProviderStatsScope = $"{_sessionId}/{_activeServer?.ApiId ?? "none"}/{_generation}",
            ProviderStatsReset = _explicitNoDino,
            SessionState = TelemetrySessionState.Stale, StatusMessage = "Chưa nhận được stats Origin mới; GPS vẫn hoạt động."
        };
        _snapshots.Writer.TryWrite(snapshot);
    }

    private static PlayerTelemetry? ParsePlayer(JsonElement result, OriginServer server)
    {
        var species = ReadString(result, "species") ?? ReadString(result, "dino");
        if (string.IsNullOrWhiteSpace(species))
        {
            return null;
        }

        var growth = ReadDouble(result, "growth");
        var vitals = new ExactVitals
        {
            Growth = growth,
            Health = ReadDouble(result, "hp"),
            MaxHealth = ReadDouble(result, "maxHp"),
            Hunger = ReadDouble(result, "hunger"),
            MaxHunger = ReadDouble(result, "maxHunger"),
            Thirst = ReadDouble(result, "thirst"),
            MaxThirst = ReadDouble(result, "maxThirst"),
            Stamina = ReadDouble(result, "stamina") ?? ReadDouble(result, "stam"),
            MaxStamina = ReadDouble(result, "maxStamina") ?? ReadDouble(result, "maxStam")
        };

        return new PlayerTelemetry
        {
            Class = species,
            Server = server.DisplayName,
            GrowthPercent = growth is { } g ? Math.Clamp(g <= 1d ? g * 100d : g, 0d, 100d) : null,
            HealthPercent = Percent(vitals.Health, vitals.MaxHealth),
            HungerPercent = Percent(vitals.Hunger, vitals.MaxHunger),
            ThirstPercent = Percent(vitals.Thirst, vitals.MaxThirst),
            StaminaPercent = Percent(vitals.Stamina, vitals.MaxStamina),
            ExactVitals = vitals,
            ExactVitalsSource = "OriginDashboard",
            Prime = null
        };
    }

    private static PrimeTelemetry ParsePrime(JsonElement result)
    {
        var growth = ReadDouble(result, "growth");
        var completed = ReadInt(result, "completed");
        var total = ReadInt(result, "total");
        var conditions = new List<PrimeQuestTelemetry>();
        if (result.TryGetProperty("conditions", out var values)
            && values.ValueKind == JsonValueKind.Array)
        {
            var names = new[] { "Sanctuary", "Nested", "Diet", "Mass Migration", "2 Migration", "4 Patrol", "Infertile", "Spasms", "Children", "Small Species" };
            var index = 0;
            foreach (var item in values.EnumerateArray())
            {
                var done = item.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? item.GetBoolean()
                    : (bool?)null;
                conditions.Add(new PrimeQuestTelemetry
                {
                    Name = index < names.Length ? names[index] : $"Quest {index + 1}",
                    Done = done
                });
                index++;
            }
        }

        return new PrimeTelemetry
        {
            Progress = growth,
            Done = completed,
            Required = total,
            Eligible = ReadBool(result, "eligible"),
            Elder = ReadBool(result, "hasElder"),
            Quests = conditions
        };
    }

    private static double? Percent(double? current, double? maximum) =>
        current is { } value && maximum is > 0d and var max
            ? Math.Clamp(value / max * 100d, 0d, 100d)
            : null;

    private static string? ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? ReadBool(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property)
            ? property.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property.GetBoolean()
                : bool.TryParse(property.ToString(), out var parsed) ? parsed : null
            : null;

    private static int? ReadInt(JsonElement value, string name) =>
        int.TryParse(ReadString(value, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var textValue)
            ? textValue
            : value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numberValue)
                ? numberValue
                : null;

    private static double? ReadDouble(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
        {
            return double.IsFinite(number) ? number : null;
        }

        return double.TryParse(property.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text)
            && double.IsFinite(text)
                ? text
                : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _stop.Cancel();
            if (_runTask is not null) await _runTask.ConfigureAwait(false);
            _client.Dispose();
        }
    }
}
