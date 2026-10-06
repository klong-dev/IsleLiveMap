using System.IO;
using System.Text.Json;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Reads the position explicitly published by the DINORP voice bridge. This is
/// not the Hub HUD API: it supplies GPS only, never stats or player identity.
/// A current game-owned UDP flow to the DINORP endpoint is required so a
/// fresh voice file from another session cannot be labelled as DINORP GPS.
/// </summary>
public sealed class DinoRpPositionFileSource : ILocalMovementSource
{
    public const string ServerEndpoint = "104.234.180.78:7777";
    public static readonly TimeSpan PositionFreshness = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan EndpointFreshness = TimeSpan.FromSeconds(3);

    private readonly ILocalMovementSource _captureSource;
    private readonly IGameEndpointEvidenceSource _endpointEvidence;
    private readonly string _positionPath;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _watchStarted;
    private int _disposed;

    public DinoRpPositionFileSource(ILocalMovementSource captureSource, string? positionPath = null)
    {
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
        _endpointEvidence = captureSource as IGameEndpointEvidenceSource
            ?? throw new ArgumentException("Nguồn capture không cung cấp bằng chứng endpoint game.", nameof(captureSource));
        _positionPath = positionPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "dinorp-theisle-launcher", "dinorp-position.json");
    }

    public async IAsyncEnumerable<LocalMovementObservation> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
            throw new InvalidOperationException("A DINORP position source can only be watched once.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
        var captureTask = DrainCaptureAsync(linked.Token);
        DateTimeOffset lastPublishedAt = DateTimeOffset.MinValue;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            do
            {
                if (captureTask.IsFaulted)
                    await captureTask.ConfigureAwait(false);

                var now = DateTimeOffset.UtcNow;
                if (_endpointEvidence.HasRecentOutboundTraffic(ServerEndpoint, now, EndpointFreshness)
                    && TryReadPosition(_positionPath, now, out var observedAt, out var movement)
                    && observedAt > lastPublishedAt)
                {
                    lastPublishedAt = observedAt;
                    yield return new LocalMovementObservation(observedAt, movement, ServerEndpoint);
                }
            }
            while (await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false));
        }
        finally
        {
            linked.Cancel();
            try { await captureTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCancellation.Cancel();
        await _captureSource.DisposeAsync().ConfigureAwait(false);
        _disposeCancellation.Dispose();
    }

    private async Task DrainCaptureAsync(CancellationToken cancellationToken)
    {
        await foreach (var _ in _captureSource.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            // Capture is retained for game-owned endpoint proof; its decoder
            // does not understand the current DINORP transport.
        }
    }

    public static bool TryReadPosition(
        string path, DateTimeOffset now, out DateTimeOffset observedAt, out UnrealMovementCandidate movement)
    {
        observedAt = default;
        movement = default;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var formatVersion)
                || formatVersion != 1
                || !root.TryGetProperty("source", out var source)
                || source.ValueKind != JsonValueKind.String
                || source.GetString() != "IsleVOIP"
                || !root.TryGetProperty("updatedAt", out var updated)
                || updated.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(updated.GetString(), out observedAt)
                || observedAt > now.AddSeconds(2)
                || now - observedAt > PositionFreshness
                || !TryFiniteNumber(root, "x", out var x)
                || !TryFiniteNumber(root, "y", out var y)
                || x is < -505000 or > 607000
                || y is < -607000 or > 509000)
                return false;

            var z = TryFiniteNumber(root, "z", out var parsedZ) ? parsedZ : 0d;
            // The voice bridge may omit yaw. Never invent a location from it;
            // the neutral yaw only controls the local arrow until yaw arrives.
            var yaw = TryFiniteNumber(root, "yaw", out var parsedYaw) ? parsedYaw : -90d;
            movement = new UnrealMovementCandidate(x, y, z, yaw, 0f, 0, 0, 0);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    private static bool TryFiniteNumber(JsonElement root, string name, out double value)
    {
        value = default;
        return root.TryGetProperty(name, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetDouble(out value)
               && double.IsFinite(value);
    }
}
