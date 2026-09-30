using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>Session-local field reducer. API timestamps are request starts, not HTTP completion times.</summary>
public sealed class OriginInboundStatsFusion
{
    private readonly Dictionary<string, Field> _fields = [];
    private string? _providerScope, _endpoint, _inboundFlow;
    private ulong? _owner;
    private DateTimeOffset _boundary = DateTimeOffset.MinValue;
    private DateTimeOffset _localBoundary = DateTimeOffset.MinValue;
    private DateTimeOffset _lastApi = DateTimeOffset.MinValue;
    private DateTimeOffset _lastLocal = DateTimeOffset.MinValue;
    private DateTimeOffset _lastInboundPresence = DateTimeOffset.MinValue;
    public static readonly TimeSpan SourceSilence = TimeSpan.FromSeconds(45);
    private bool _baseline;
    private PlayerTelemetry? _metadata;
    private TelemetrySnapshot? _provider;
    private string? _resetScope;
    private static readonly string[] Names = ["Health", "MaxHealth", "Stamina", "MaxStamina", "Hunger", "MaxHunger", "Thirst", "MaxThirst"];

    public void ObserveLocal(LocalMovementObservation local, DateTimeOffset receivedAt)
    {
        var stats = local.DinosaurVitals;
        var at = stats?.ObservedAt ?? local.ObservedAt;
        if (at < _lastLocal || at > receivedAt) return;
        _lastLocal = at;
        if (stats is { NetRefHandle: > 0 }) _lastInboundPresence = at;
        var evidence = stats?.ExperimentalEvidence;
        var flow = evidence?.FirstOrDefault()?.Flow;
        var owner = stats is { NetRefHandle: > 0 } s ? s.NetRefHandle : (ulong?)null;
        var changed = _endpoint is not null && local.ServerEndpoint is not null && _endpoint != local.ServerEndpoint
            || _owner is not null && owner is not null && _owner != owner
            || _inboundFlow is not null && flow is not null && _inboundFlow != flow;
        if (changed || stats is { ExperimentalEvidence.Count: 0, NetRefHandle: 0 })
        {
            ResetFields(receivedAt);
            // The packet proving the new actor was captured before it reached
            // this reducer; accept that packet, not carried fields from before it.
            _localBoundary = at;
        }
        _endpoint = local.ServerEndpoint ?? _endpoint;
        _owner = owner ?? _owner;
        _inboundFlow = flow ?? _inboundFlow;
        if (evidence is not null)
        {
            foreach (var e in evidence)
            {
                if (e.Owner != owner || e.Flow != flow || e.ObservedAt > receivedAt) continue;
                var name = e.Field.Replace("Candidate", "", StringComparison.Ordinal);
                if (Names.Contains(name)) Apply(name, e.Value, e.ObservedAt, "Inbound");
            }
            var water = evidence.FirstOrDefault(e => e.Field == "ThirstCandidate"
                && e.Owner == owner && e.Flow == flow && e.ObservedAt >= _localBoundary && e.ObservedAt <= receivedAt);
            if (water is not null && !_fields.ContainsKey("MaxThirst"))
                Apply("MaxThirst", InboundVitalsDeltaState.ProtocolMaxWater, water.ObservedAt, "Protocol");
        }
        else if (stats is { } sample && sample.NetRefHandle > 0)
            ApplyVitals(sample.Vitals, sample.ObservedAt, "Inbound");
    }

    public void ObserveProvider(TelemetrySnapshot snapshot, DateTimeOffset receivedAt)
    {
        var at = snapshot.ProviderStatsRequestedAt ?? snapshot.ProviderStatsObservedAt ?? snapshot.UpdatedAt;
        if (at > receivedAt || at < _lastApi || at < _boundary) return;
        // Connecting/empty discovery status is not a bound dino scope. Binding
        // it would reject the very first complete baseline as a server change.
        var scope = snapshot.ProviderStatsReset || snapshot.PlayerOnline && snapshot.Player is not null
            ? snapshot.ProviderStatsScope ?? snapshot.Player?.Server : null;
        if (_providerScope is not null && scope is not null && scope != _providerScope)
        {
            ResetFields(receivedAt);
            // This response establishes a new provider generation. Accept its
            // fields using request time, while rejecting inbound evidence that
            // was captured before the new generation became known.
            if (at is { } requested && !snapshot.ProviderStatsReset) _boundary = requested;
        }
        _providerScope = scope ?? _providerScope;
        _provider = snapshot;
        if (snapshot.ProviderStatsReset)
        {
            if (_resetScope != scope) { ResetFields(receivedAt); _resetScope = scope; }
            return;
        }
        if (snapshot.SessionState == TelemetrySessionState.AuthenticationRequired) return;
        if (snapshot.Player is not { } player || !snapshot.PlayerOnline || at is null || at < _boundary) return;
        if (_endpoint is not null && player.ServerEndpoint is not null && _endpoint != player.ServerEndpoint) return;
        if (_metadata?.Class is { } previous && player.Class is { } species && !CreatureSpeciesIdentity.AreSame(previous, species)
            || _metadata?.ExactVitals?.Growth is { } oldGrowth && player.ExactVitals?.Growth is { } growth && growth < oldGrowth - .05)
        { ResetFields(receivedAt); return; }
        if (!_baseline && !Complete(player.ExactVitals)) return;
        _baseline = true;
        _metadata = _metadata is null ? player : player with
        {
            Prime = player.Prime ?? _metadata.Prime,
            GrowthPercent = player.GrowthPercent ?? _metadata.GrowthPercent,
            ExactVitals = (player.ExactVitals ?? new()) with { Growth = player.ExactVitals?.Growth ?? _metadata.ExactVitals?.Growth }
        };
        if (at == _lastApi) return; // Prime-only frame may change metadata, never field timestamps.
        _lastApi = at.Value;
        if (player.ExactVitals is { } values) ApplyVitals(values, at.Value, "Origin");
    }

    public TelemetrySnapshot Build(DateTimeOffset now)
    {
        var v = new ExactVitals
        {
            Health = Read("Health"), MaxHealth = Read("MaxHealth"),
            Stamina = Read("Stamina"), MaxStamina = Read("MaxStamina"),
            Hunger = Read("Hunger"), MaxHunger = Read("MaxHunger"),
            Thirst = Read("Thirst"), MaxThirst = Read("MaxThirst"),
            Growth = _metadata?.ExactVitals?.Growth
        };
        var hasData = _fields.Count > 0;
        var active = (_lastInboundPresence <= now && now - _lastInboundPresence <= SourceSilence)
            || (_lastApi <= now && now - _lastApi <= SourceSilence);
        var prime = _metadata?.Prime;
        return new TelemetrySnapshot
        {
            Source = "ORIGIN x5", Success = hasData, ServerOnline = hasData, PlayerOnline = hasData,
            UpdatedAt = hasData ? _fields.Values.Max(f => f.At) : null,
            SessionState = !hasData ? TelemetrySessionState.Connecting : active ? TelemetrySessionState.Live : TelemetrySessionState.Stale,
            LiveDataStale = hasData && !active,
            StatusMessage = hasData && !active ? "ORIGIN + INBOUND · DỮ LIỆU CŨ"
                : !_baseline ? "INBOUND · ĐANG CHỜ BASELINE ORIGIN" : "ORIGIN + INBOUND",
            Player = hasData ? (_metadata ?? new PlayerTelemetry { Name = "LOCAL PLAYER" }) with
            {
                ServerEndpoint = _endpoint, ExactVitals = v, ExactVitalsSource = "OriginInbound",
                InboundStatsExperimental = false, InboundStatsLastKnown = null, InboundStatsFieldTimes = null,
                InboundStatsFallback = false, InboundStatsOwnerHandle = _owner,
                HealthPercent = Percent(v.Health, v.MaxHealth), StaminaPercent = Percent(v.Stamina, v.MaxStamina),
                HungerPercent = Percent(v.Hunger, v.MaxHunger), ThirstPercent = Percent(v.Thirst, v.MaxThirst),
                Prime = prime, PrimeDataStale = prime is not null && (!active || _provider?.LiveDataStale == true
                    || _provider?.SessionState == TelemetrySessionState.AuthenticationRequired),
                ProviderAuthenticationRequired = _provider?.SessionState == TelemetrySessionState.AuthenticationRequired,
                StatsFieldTimes = _fields.ToDictionary(p => p.Key, p => p.Value.At),
                StatsFieldSources = _fields.ToDictionary(p => p.Key, p => p.Value.Source)
            } : null
        };
    }

    private void ResetFields(DateTimeOffset at)
    { _fields.Clear(); _metadata = null; _baseline = false; _boundary = at; _localBoundary = at; }
    private void ApplyVitals(ExactVitals v, DateTimeOffset at, string source)
    {
        Apply("Health", v.Health, at, source); Apply("MaxHealth", v.MaxHealth, at, source);
        Apply("Stamina", v.Stamina, at, source); Apply("MaxStamina", v.MaxStamina, at, source);
        Apply("Hunger", v.Hunger, at, source); Apply("MaxHunger", v.MaxHunger, at, source);
        Apply("Thirst", v.Thirst, at, source); Apply("MaxThirst", v.MaxThirst, at, source);
    }
    private void Apply(string name, double? value, DateTimeOffset at, string source)
    {
        if (at < (source is "Inbound" or "Protocol" ? _localBoundary : _boundary)
            || value is not >= 0 || !double.IsFinite(value.Value)) return;
        if (name.StartsWith("Max", StringComparison.Ordinal) && value == 0) return;
        if (_fields.TryGetValue(name, out var old) && old.Source != "Protocol"
            && (at < old.At || at == old.At && old.Source == "Inbound")) return;
        _fields[name] = new(value.Value, at, source);
    }
    private double? Read(string name) => _fields.TryGetValue(name, out var f) ? f.Value : null;
    private static double? Percent(double? current, double? max) => current is not null && max is > 0 ? VitalMath.Percent(current, max) : null;
    private static bool Complete(ExactVitals? v) => v is not null
        && Pair(v.Health, v.MaxHealth) && Pair(v.Stamina, v.MaxStamina) && Pair(v.Hunger, v.MaxHunger) && Pair(v.Thirst, v.MaxThirst);
    private static bool Pair(double? current, double? max) => current is >= 0 && max is > 0 && double.IsFinite(current.Value) && double.IsFinite(max.Value);
    private sealed record Field(double Value, DateTimeOffset At, string Source);
}
