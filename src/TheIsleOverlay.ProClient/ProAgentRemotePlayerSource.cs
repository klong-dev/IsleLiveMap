using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.ProClient;

public sealed class ProAgentException(string message) : Exception(message);

public sealed class ProAgentRemotePlayerSource :
    IRemotePlayerTelemetrySource,
    IRemotePlayerTelemetryHealthSource
{
    // Agent and Host comparison streams use separate files. Two processes
    // appending the same JSONL path can split a line in the frozen capture.
    public const string HostComparisonOutputPathEnvironmentVariable =
        "ISLELIVEMAP_PRO_HOST_COMPARE_PATH";
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumRestartDelay = TimeSpan.FromSeconds(30);

    private readonly string _agentExecutablePath;
    private readonly string _hostVersion;
    private readonly string _steamId64;
    private readonly string _offlineLicenseToken;
    private readonly string? _liveComparePath;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _compareGate = new();
    private StreamWriter? _compareWriter;
    private int _watchStarted;
    private int _disposed;
    private RemotePlayerCaptureHealth _captureHealth = RemotePlayerCaptureHealth.Starting;

    public RemotePlayerCaptureHealth CaptureHealth =>
        Volatile.Read(ref _captureHealth);

    public ProAgentRemotePlayerSource(
        string agentExecutablePath,
        string hostVersion,
        string steamId64,
        string offlineLicenseToken,
        string? liveComparePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId64);
        ArgumentException.ThrowIfNullOrWhiteSpace(offlineLicenseToken);
        _agentExecutablePath = Path.GetFullPath(agentExecutablePath);
        _hostVersion = hostVersion;
        _steamId64 = steamId64;
        _offlineLicenseToken = offlineLicenseToken;
        _liveComparePath = string.IsNullOrWhiteSpace(liveComparePath)
            ? null
            : Path.GetFullPath(liveComparePath.Trim());
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
        var restartAttempt = 0;
        while (!linkedCancellation.IsCancellationRequested)
        {
            await using var session = WatchAgentSessionAsync(linkedCancellation.Token)
                .GetAsyncEnumerator(linkedCancellation.Token);
            var restart = false;
            while (!linkedCancellation.IsCancellationRequested)
            {
                RemotePlayerTelemetryFrame? frame = null;
                try
                {
                    if (!await session.MoveNextAsync().ConfigureAwait(false))
                    {
                        restart = true;
                        break;
                    }

                    frame = session.Current;
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                    yield break;
                }
                catch (Exception exception)
                {
                    SetFaultedHealth(UserFacingAgentFailure(exception));
                    restart = true;
                    break;
                }

                restartAttempt = 0;
                yield return frame;
            }

            if (!restart || linkedCancellation.IsCancellationRequested)
            {
                yield break;
            }

            var restartDelay = TimeSpan.FromSeconds(Math.Min(
                MaximumRestartDelay.TotalSeconds,
                2d * Math.Pow(2d, Math.Min(restartAttempt++, 4))));
            try
            {
                await Task.Delay(restartDelay, linkedCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                yield break;
            }

            Volatile.Write(ref _captureHealth, CaptureHealth with
            {
                State = RemotePlayerCaptureState.Starting,
                Message = "Đang tự khởi động lại Pro Agent."
            });
        }
    }

    private async IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAgentSessionAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
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
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ConnectionTimeout);
                try
                {
                    await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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
                    cancellationToken)
                .ConfigureAwait(false);

            AgentMessage response;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(HandshakeTimeout);
                try
                {
                    response = await ipc.ReadAsync<AgentMessage>(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ProAgentException("The Pro Agent handshake timed out.");
                }
            }

            ValidateHandshake(response);
            long lastSequence = 0;
            var hasCaptureStatus = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await ipc.ReadAsync<AgentMessage>(cancellationToken).ConfigureAwait(false);
                if (message.Error is { Fatal: true } error)
                {
                    SetFaultedHealth(error.Message);
                    throw new ProAgentException($"Pro Agent stopped: {error.Code}.");
                }

                if (message.CaptureStatus is { } captureStatus)
                {
                    hasCaptureStatus = true;
                    Volatile.Write(ref _captureHealth, MapCaptureHealth(captureStatus));
                }

                if (message.Telemetry is not { } telemetry || telemetry.Sequence <= lastSequence)
                {
                    continue;
                }

                lastSequence = telemetry.Sequence;
                if (!hasCaptureStatus)
                {
                    Volatile.Write(ref _captureHealth, CaptureHealth with
                    {
                        State = RemotePlayerCaptureState.Receiving,
                        GameProcessFound = true,
                        LastGamePacketAt = DateTimeOffset.UtcNow,
                        Message = null
                    });
                }
                WriteLiveCompare(telemetry, pipeName);
                yield return MapFrame(telemetry, pipeName);
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
            lock (_compareGate)
            {
                _compareWriter?.Dispose();
                _compareWriter = null;
            }
        }

        return ValueTask.CompletedTask;
    }

    internal void WriteLiveCompare(ProTelemetryFrame frame, string sessionId)
    {
        var configuredPath = ResolveHostComparisonPath(
            _liveComparePath ?? Environment.GetEnvironmentVariable(HostComparisonOutputPathEnvironmentVariable),
            Environment.GetEnvironmentVariable("ISLELIVEMAP_PRO_LIVE_COMPARE_PATH"));
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        try
        {
            lock (_compareGate)
            {
                if (_compareWriter is null)
                {
                    var path = Path.GetFullPath(configuredPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    _compareWriter = new StreamWriter(
                        new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                        new System.Text.UTF8Encoding(false))
                    {
                        AutoFlush = true
                    };
                }

                var record = new
                {
                    Stage = "host-ipc-received",
                    SessionId = sessionId,
                    ProcessId = Environment.ProcessId,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    frame.Sequence,
                    frame.ObservedAt,
                    FrameReceivedAt = DateTimeOffset.UtcNow,
                    frame.ServerEndpoint,
                    frame.LocalLocation,
                    frame.PlayerSync,
                    RemoteEntities = (frame.RemoteEntities ?? []).Select(entity => new
                    {
                        entity.TrackId,
                        Kind = entity.Kind.ToString(),
                        entity.PlayerProofName,
                        entity.SpeciesId,
                        entity.SpeciesShortName,
                        entity.Diet,
                        entity.Location,
                        entity.ObservedAt,
                        entity.LocationObservedAt,
                        entity.HasVerifiedPosition,
                        entity.ActorNetRefHandle,
                        entity.PlayerStateNetRefHandle,
                        entity.PawnNetRefHandle,
                        entity.IsProvisional
                    }).ToArray()
                };
                _compareWriter.WriteLine(JsonSerializer.Serialize(record));
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            lock (_compareGate)
            {
                _compareWriter?.Dispose();
                _compareWriter = null;
            }

            try
            {
                File.AppendAllText(
                    configuredPath + ".error.log",
                    $"{DateTimeOffset.UtcNow:O} {exception}{Environment.NewLine}");
            }
            catch
            {
            }
        }
        catch (UnauthorizedAccessException)
        {
            lock (_compareGate)
            {
                _compareWriter?.Dispose();
                _compareWriter = null;
            }
        }
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
        startInfo.Environment["ISLELIVEMAP_TRACKING_SESSION_ID"] = pipeName;
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

    internal static string? ResolveHostComparisonPath(string? hostPath, string? agentPath)
    {
        if (string.IsNullOrWhiteSpace(hostPath)) return null;
        var path = Path.GetFullPath(hostPath.Trim());
        // Refuse accidental aliasing even for callers supplying explicit paths.
        return !string.IsNullOrWhiteSpace(agentPath)
               && string.Equals(path, Path.GetFullPath(agentPath.Trim()), StringComparison.OrdinalIgnoreCase)
            ? null
            : path;
    }

    internal static RemotePlayerTelemetryFrame MapFrame(ProTelemetryFrame frame, string? sessionId = null)
    {
        if (!IsFinite(frame.LocalLocation))
        {
            throw new ProAgentException("The Pro Agent returned an invalid local position.");
        }

        var hasValidLocalSpecies = HasValidSpeciesIdentity(
            frame.LocalSpeciesId,
            frame.LocalSpeciesShortName);

        var entities = (frame.RemoteEntities ?? [])
            .Select(entity => new VerifiedRemoteEntityTelemetry(
                entity.TrackId,
                MapKind(entity.Kind),
                entity.Kind == MapEntityKind.Player
                    ? string.IsNullOrWhiteSpace(entity.PlayerProofName)
                        ? null
                        : entity.PlayerProofName.Trim()
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
                entity.ObservedAt,
                entity.IsProvisional,
                entity.LocationObservedAt,
                entity.ActorNetRefHandle,
                entity.PlayerStateNetRefHandle,
                entity.PawnNetRefHandle,
                entity.HasVerifiedPosition))
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
            hasValidLocalSpecies ? frame.LocalSpeciesShortName!.Trim() : null,
            DateTimeOffset.UtcNow,
            frame.PlayerSync is null
                ? null
                : new RemotePlayerSyncState(
                    frame.PlayerSync.IsSynchronizing,
                    frame.PlayerSync.VerifiedPlayers,
                    frame.PlayerSync.ProvisionalPlayers,
                    frame.PlayerSync.CandidateActors,
                    frame.PlayerSync.SpeciesEvidenceActors,
                    frame.PlayerSync.LocatedActors,
                    frame.PlayerSync.QueueDroppedPackets,
                    frame.PlayerSync.QueueDepth),
            SessionId: sessionId);
    }

    private static string UserFacingAgentFailure(Exception exception) => exception switch
    {
        ProAgentException when exception.Message.Contains(
            "unavailable",
            StringComparison.OrdinalIgnoreCase) =>
            "Không tìm thấy Pro Agent đã cài đặt.",
        ProAgentException when exception.Message.Contains(
            "connect",
            StringComparison.OrdinalIgnoreCase) =>
            "Pro Agent không kết nối được với Live Map.",
        _ => "Pro Agent đã dừng; hệ thống sẽ tự thử lại."
    };

    private static RemotePlayerCaptureHealth MapCaptureHealth(AgentCaptureStatus status) => new(
        ParseCaptureState(status.State),
        status.GameProcessFound,
        Math.Max(0, status.OwnedPortCount),
        Math.Max(0, status.OpenedAdapterCount),
        Math.Max(0, status.MatchedGamePackets),
        status.LastGamePacketAt,
        string.IsNullOrWhiteSpace(status.Message) ? null : status.Message.Trim());

    private static RemotePlayerCaptureState ParseCaptureState(string? state) =>
        state?.Trim().ToLowerInvariant() switch
        {
            "waiting-game" => RemotePlayerCaptureState.WaitingForGame,
            "waiting-port" => RemotePlayerCaptureState.WaitingForPort,
            "opening-adapters" => RemotePlayerCaptureState.OpeningAdapters,
            "capturing" => RemotePlayerCaptureState.Capturing,
            "receiving" => RemotePlayerCaptureState.Receiving,
            "faulted" => RemotePlayerCaptureState.Faulted,
            _ => RemotePlayerCaptureState.Starting
        };

    private void SetFaultedHealth(string? message) =>
        Volatile.Write(ref _captureHealth, FaultedHealth(CaptureHealth, message));

    private static RemotePlayerCaptureHealth FaultedHealth(
        RemotePlayerCaptureHealth current,
        string? message) => current with
    {
        State = RemotePlayerCaptureState.Faulted,
        Message = current.State == RemotePlayerCaptureState.Faulted
                  && !string.IsNullOrWhiteSpace(current.Message)
            ? current.Message
            : string.IsNullOrWhiteSpace(message)
                ? "Pro Agent đã dừng."
                : message.Trim()
    };

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
            // Names are optional metadata. Structural Iris identity is
            // mandatory for both verified and provisional players.
            && HasStableActorIdentity(entity)
            && (entity.IsProvisional
                ? HasValidSpecies(entity)
                : HasValidOptionalSpecies(entity)))
        && entity.ConfirmationHits > 0
        && double.IsFinite(entity.DistanceFromLocal)
        && entity.DistanceFromLocal >= 0
        && IsFinite(entity.Location);

    private static bool HasStableActorIdentity(VerifiedMapEntity entity) =>
        entity.ActorNetRefHandle > 0
        || entity.PlayerStateNetRefHandle > 0
        || entity.PawnNetRefHandle > 0;

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
        _ => RemoteEntityKind.Unknown
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
