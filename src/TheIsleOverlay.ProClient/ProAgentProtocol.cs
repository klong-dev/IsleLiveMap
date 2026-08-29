using System.Buffers.Binary;
using System.Text.Json;

namespace TheIsleOverlay.ProClient;

internal static class ProAgentProtocol
{
    public const int IpcApiMajor = 2;
    public const int MaximumFrameBytes = 1024 * 1024;
    public const string PipePrefix = "IsleLiveMap.Pro.";
}

internal sealed record HostHello(
    int IpcApiMajor,
    string HostVersion,
    string OfflineLicenseToken);

internal sealed record AgentHello(
    bool Accepted,
    int IpcApiMajor,
    string AgentVersion,
    string? SteamId64,
    string? ErrorCode,
    string? ErrorMessage);

internal sealed record AgentError(string Code, string Message, bool Fatal);

internal sealed record AgentMessage(
    string Type,
    AgentHello? Hello,
    ProTelemetryFrame? Telemetry,
    AgentError? Error);

internal sealed record WorldPosition(double X, double Y, double? Z);

internal enum MapEntityKind
{
    Player = 1,
    Ai = 2
}

internal enum MapCreatureDiet
{
    Unknown = 0,
    Carnivore = 1,
    Herbivore = 2,
    Omnivore = 3
}

internal sealed record VerifiedMapEntity(
    long TrackId,
    MapEntityKind Kind,
    string? PlayerProofName,
    string SpeciesId,
    string SpeciesShortName,
    MapCreatureDiet Diet,
    double? MassKg,
    WorldPosition Location,
    double DistanceFromLocal,
    int ConfirmationHits,
    DateTimeOffset ObservedAt);

internal sealed record ProTelemetryFrame(
    long Sequence,
    DateTimeOffset ObservedAt,
    string? ServerEndpoint,
    WorldPosition LocalLocation,
    double MapHeadingDegrees,
    IReadOnlyList<VerifiedMapEntity> RemoteEntities,
    string? LocalSpeciesId = null,
    string? LocalSpeciesShortName = null);

internal sealed class IpcJsonStream(Stream stream) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async ValueTask WriteAsync<T>(
        T value,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length > ProAgentProtocol.MaximumFrameBytes)
        {
            throw new InvalidDataException("IPC frame exceeds the protocol limit.");
        }

        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask<T> ReadAsync<T>(CancellationToken cancellationToken = default)
    {
        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > ProAgentProtocol.MaximumFrameBytes)
        {
            throw new InvalidDataException("IPC frame length is invalid.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
               ?? throw new InvalidDataException("IPC frame payload is empty.");
    }

    public async ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ReadExactlyAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("The Pro Agent disconnected.");
            }

            offset += read;
        }
    }
}
