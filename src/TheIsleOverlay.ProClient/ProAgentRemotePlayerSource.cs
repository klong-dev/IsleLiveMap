using System.Diagnostics;
using System.IO.Pipes;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.ProClient;

public sealed class ProAgentException(string message) : Exception(message);

public sealed class ProAgentRemotePlayerSource : IRemotePlayerTelemetrySource
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    private readonly string _agentExecutablePath;
    private readonly string _hostVersion;
    private readonly string _steamId64;
    private readonly string _offlineLicenseToken;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _watchStarted;
    private int _disposed;

    public ProAgentRemotePlayerSource(
        string agentExecutablePath,
        string hostVersion,
        string steamId64,
        string offlineLicenseToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId64);
        ArgumentException.ThrowIfNullOrWhiteSpace(offlineLicenseToken);
        _agentExecutablePath = Path.GetFullPath(agentExecutablePath);
        _hostVersion = hostVersion;
        _steamId64 = steamId64;
        _offlineLicenseToken = offlineLicenseToken;
    }

    public async IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException("A Pro Agent source can only be watched once.");
        }

        if (!OperatingSystem.IsWindows() || !File.Exists(_agentExecutablePath))
        {
            throw new ProAgentException("The installed Pro Agent is unavailable.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        var pipeName = ProAgentProtocol.PipePrefix + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough | PipeOptions.CurrentUserOnly);
        using var process = StartAgent(pipeName);
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(linkedCancellation.Token))
            {
                timeout.CancelAfter(ConnectionTimeout);
                try
                {
                    await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!linkedCancellation.IsCancellationRequested)
                {
                    throw new ProAgentException("The Pro Agent did not connect in time.");
                }
            }

            await using var ipc = new IpcJsonStream(pipe);
            await ipc.WriteAsync(
                    new HostHello(
                        ProAgentProtocol.IpcApiMajor,
                        _hostVersion,
                        _offlineLicenseToken),
                    linkedCancellation.Token)
                .ConfigureAwait(false);

            AgentMessage response;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(linkedCancellation.Token))
            {
                timeout.CancelAfter(HandshakeTimeout);
                try
                {
                    response = await ipc.ReadAsync<AgentMessage>(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!linkedCancellation.IsCancellationRequested)
                {
                    throw new ProAgentException("The Pro Agent handshake timed out.");
                }
            }

            ValidateHandshake(response);
            long lastSequence = 0;
            while (!linkedCancellation.IsCancellationRequested)
            {
                var message = await ipc.ReadAsync<AgentMessage>(linkedCancellation.Token).ConfigureAwait(false);
                if (message.Error is { Fatal: true } error)
                {
                    throw new ProAgentException($"Pro Agent stopped: {error.Code}.");
                }

                if (message.Telemetry is not { } telemetry || telemetry.Sequence <= lastSequence)
                {
                    continue;
                }

                lastSequence = telemetry.Sequence;
                yield return MapFrame(telemetry);
            }
        }
        finally
        {
            StopAgent(process);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _disposeCancellation.Cancel();
            _disposeCancellation.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private Process StartAgent(string pipeName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _agentExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(_agentExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(startInfo)
               ?? throw new ProAgentException("Windows could not start the Pro Agent.");
    }

    private void ValidateHandshake(AgentMessage response)
    {
        var hello = response.Hello;
        if (!string.Equals(response.Type, "hello", StringComparison.Ordinal) ||
            hello is null ||
            !hello.Accepted ||
            hello.IpcApiMajor != ProAgentProtocol.IpcApiMajor ||
            !string.Equals(hello.SteamId64, _steamId64, StringComparison.Ordinal))
        {
            throw new ProAgentException(
                hello?.ErrorCode is { Length: > 0 } code
                    ? $"Pro Agent rejected the session: {code}."
                    : "The Pro Agent handshake is invalid.");
        }
    }

    private static RemotePlayerTelemetryFrame MapFrame(ProTelemetryFrame frame)
    {
        if (!IsFinite(frame.LocalLocation))
        {
            throw new ProAgentException("The Pro Agent returned an invalid local position.");
        }

        var hasValidLocalSpecies = HasValidSpeciesIdentity(
            frame.LocalSpeciesId,
            frame.LocalSpeciesShortName);

        var entities = (frame.RemoteEntities ?? [])
            .Where(IsValidEntity)
            .Select(entity => new VerifiedRemoteEntityTelemetry(
                entity.TrackId,
                MapKind(entity.Kind),
                entity.Kind == MapEntityKind.Player
                    ? entity.PlayerProofName!.Trim()
                    : null,
                entity.SpeciesId?.Trim() ?? string.Empty,
                entity.SpeciesShortName?.Trim() ?? string.Empty,
                MapDiet(entity.Diet),
                entity.MassKg,
                new WorldLocation
                {
                    X = entity.Location.X,
                    Y = entity.Location.Y,
                    Z = entity.Location.Z
                },
                entity.DistanceFromLocal,
                entity.ConfirmationHits,
                entity.ObservedAt))
            .ToArray();

        return new RemotePlayerTelemetryFrame(
            frame.Sequence,
            frame.ObservedAt,
            frame.ServerEndpoint,
            new WorldLocation
            {
                X = frame.LocalLocation.X,
                Y = frame.LocalLocation.Y,
                Z = frame.LocalLocation.Z
            },
            frame.MapHeadingDegrees,
            entities,
            hasValidLocalSpecies ? frame.LocalSpeciesId!.Trim() : null,
            hasValidLocalSpecies ? frame.LocalSpeciesShortName!.Trim() : null);
    }

    private static bool IsValidEntity(VerifiedMapEntity entity) =>
        entity.TrackId > 0
        && Enum.IsDefined(entity.Kind)
        && Enum.IsDefined(entity.Diet)
        && (entity.MassKg is null
            || entity.MassKg is > 0d and < 100_000d
            && double.IsFinite(entity.MassKg.Value))
        && (entity.Kind == MapEntityKind.Ai
            && HasValidSpecies(entity)
            || entity.Kind == MapEntityKind.Player
            && HasValidPlayerProof(entity)
            && HasValidOptionalSpecies(entity))
        && entity.ConfirmationHits > 0
        && double.IsFinite(entity.DistanceFromLocal)
        && entity.DistanceFromLocal >= 0
        && IsFinite(entity.Location);

    private static bool HasValidPlayerProof(VerifiedMapEntity entity) =>
        entity.PlayerProofName is { Length: > 0 and <= 64 }
        && !string.IsNullOrWhiteSpace(entity.PlayerProofName);

    private static bool HasValidSpecies(VerifiedMapEntity entity) =>
        HasValidSpeciesIdentity(entity.SpeciesId, entity.SpeciesShortName);

    private static bool HasValidSpeciesIdentity(
        string? speciesId,
        string? speciesShortName) =>
        speciesId is { Length: > 0 and <= 64 }
        && !string.IsNullOrWhiteSpace(speciesId)
        && speciesShortName is { Length: > 0 and <= 32 }
        && !string.IsNullOrWhiteSpace(speciesShortName);

    private static bool HasValidOptionalSpecies(VerifiedMapEntity entity) =>
        string.IsNullOrWhiteSpace(entity.SpeciesId)
        && string.IsNullOrWhiteSpace(entity.SpeciesShortName)
        || HasValidSpecies(entity);

    private static RemoteEntityKind MapKind(MapEntityKind kind) => kind switch
    {
        MapEntityKind.Player => RemoteEntityKind.Player,
        MapEntityKind.Ai => RemoteEntityKind.Ai,
        _ => throw new ProAgentException("The Pro Agent returned an invalid entity kind.")
    };

    private static CreatureDiet MapDiet(MapCreatureDiet diet) => diet switch
    {
        MapCreatureDiet.Carnivore => CreatureDiet.Carnivore,
        MapCreatureDiet.Herbivore => CreatureDiet.Herbivore,
        MapCreatureDiet.Omnivore => CreatureDiet.Omnivore,
        _ => CreatureDiet.Unknown
    };

    private static bool IsFinite(WorldPosition position) =>
        double.IsFinite(position.X) &&
        double.IsFinite(position.Y) &&
        (position.Z is null || double.IsFinite(position.Z.Value));

    private static void StopAgent(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2_000);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
