using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;

public sealed class IslePilotFallbackSessionTests
{
    [Fact]
    public async Task ProviderFailureLeavesInboundRunningAndRetainsPrime()
    {
        var provider = new ProviderStream(); var local = new LocalStream();
        await using var session = new LocalPositionTelemetrySession(provider, local, "ISLEPILOT", enableLocalVitals: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var watch = session.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await watch.MoveNextAsync());
        var prime = new PrimeTelemetry { Done = 1, Required = 2, Quests = [new() { Name = "Travel", Done = true }] };
        var now = DateTimeOffset.UtcNow;
        provider.Publish(new() { Source = "IslePilot", Success = true, ServerOnline = true, PlayerOnline = true,
            SessionState = TelemetrySessionState.Live, UpdatedAt = now,
            Player = new() { ExactVitalsSource = "IslePilotOverlayV2", ExactVitals = new() { Health = 80, MaxHealth = 100 }, Prime = prime } });
        while (watch.Current.Player?.ExactVitalsSource != "IslePilotOverlayV2") Assert.True(await watch.MoveNextAsync());
        local.Publish(LocalMovementObservation.VitalsOnly(new(DateTimeOffset.UtcNow, new() { Health = 25, MaxHealth = 100 }, 42), "server:7777"));
        provider.Fail();
        while (watch.Current.Player?.InboundStatsFallback != true) Assert.True(await watch.MoveNextAsync());
        Assert.Equal(25d, watch.Current.Player!.HealthPercent);
        Assert.Same(prime, watch.Current.Player.Prime);
        Assert.True(watch.Current.Player.PrimeDataStale);
        local.Publish(LocalMovementObservation.VitalsOnly(new(DateTimeOffset.UtcNow, new() { Health = 20, MaxHealth = 100 }, 42), "server:7777"));
        while (watch.Current.Player?.HealthPercent != 20d) Assert.True(await watch.MoveNextAsync());
    }

    private sealed class ProviderStream : ITelemetrySession
    {
        private readonly Channel<TelemetrySnapshot> _frames = Channel.CreateUnbounded<TelemetrySnapshot>();
        public void Publish(TelemetrySnapshot snapshot) => _frames.Writer.TryWrite(snapshot);
        public void Fail() => _frames.Writer.TryComplete(new IOException("simulated provider fault"));
        public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken)) yield return frame; }
        public ValueTask DisposeAsync() { _frames.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
    private sealed class LocalStream : ILocalMovementSource
    {
        private readonly Channel<LocalMovementObservation> _frames = Channel.CreateUnbounded<LocalMovementObservation>();
        public void Publish(LocalMovementObservation value) => _frames.Writer.TryWrite(value);
        public async IAsyncEnumerable<LocalMovementObservation> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken)) yield return frame; }
        public ValueTask DisposeAsync() { _frames.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
