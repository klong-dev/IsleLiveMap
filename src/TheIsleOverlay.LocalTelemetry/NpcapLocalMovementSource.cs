using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Channels;
using PacketDotNet;
using SharpPcap;

namespace TheIsleOverlay.LocalTelemetry;

public sealed class NpcapLocalMovementSource : ILocalMovementSource, ILocalVitalsFeatureSource, IGameEndpointEvidenceSource
{
    public const string DefaultGameProcessName = "TheIsleClient-Win64-Shipping";
    private static readonly TimeSpan ProcessPollInterval = TimeSpan.FromSeconds(1);
    // UDP sockets are frequently re-bound while the game reconnects (and
    // Steam/Unreal may add short-lived sockets during normal play).  Keep the
    // kernel capture alive and refresh only the user-mode ownership set.
    private static readonly TimeSpan OwnedPortRefreshInterval =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MinimumObservationInterval = TimeSpan.FromMilliseconds(25);
    private const int CaptureReadTimeoutMilliseconds = 25;
    internal const int InboundPacketCapacity = 2_048;
    internal const long InboundByteCapacity = 8L * 1024 * 1024;
    private readonly string _processName;
    private readonly WindowsUdpPortOwnerResolver _portResolver;
    private readonly LocalMovementTracker _tracker;
    private readonly Dictionary<string, (LocalMovementTracker Tracker, DateTimeOffset LastSeen)> _movementFlows = [];
    private readonly UnrealDinosaurVitalsTracker _vitalsTracker;
    private readonly InboundStatsAccumulator _inboundStats = new(new InboundStatsRestartCache());
    private readonly bool _replaceIslePilotStats = LocalVitalsFeature.ReplacesIslePilot();
    private readonly LocalVitalsSessionCache _vitalsCache;
    private readonly bool _trackIrisSequenceDiagnostics;
    private readonly UnrealIrisPacketParser _irisPacketParser = new();
    private readonly IrisPacketSequenceTracker _outboundSequenceTracker = new();
    private readonly IrisPacketSequenceTracker _inboundSequenceTracker = new();
    private readonly object _movementTrackerGate = new();
    private readonly object _vitalsTrackerGate = new();
    private readonly object _latestObservationGate = new();
    private readonly object _outboundEndpointGate = new();
    private readonly Dictionary<string, DateTimeOffset> _recentOutboundEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private PacketIntakePair? _activePacketIntakes;
    private PacketLaneDiagnostics _lastLaneDiagnostics;
    private PacketPipelineDiagnostics _lastPipelineDiagnostics;
    private PublishedVitals? _latestVitals;
    private LocalMovementObservation? _latestObservation;
    private string? _activeVitalsEndpoint;
    private string? _latestCapturedOutboundEndpoint;
    private string? _activeGameSessionId;
    private bool _vitalsCacheSeeded;
    private long _lastVitalsObservationUtcTicks;
    private long _publishedVitalsObservations;
    private long _vitalsSessionResets;
    private long _npcapDroppedPackets;
    private long _interfaceDroppedPackets;
    private int _watchStarted;
    private int _disposed;

    public NpcapLocalMovementSource(
        string processName = DefaultGameProcessName,
        WindowsUdpPortOwnerResolver? portResolver = null,
        LocalMovementTracker? tracker = null,
        bool trackIrisSequenceDiagnostics = true,
        bool? enableLocalVitals = null,
        UnrealDinosaurVitalsTracker? vitalsTracker = null,
        LocalVitalsSessionCache? vitalsCache = null)
    {
        _processName = processName;
        _portResolver = portResolver ?? new WindowsUdpPortOwnerResolver();
        _tracker = tracker ?? new LocalMovementTracker();
        _trackIrisSequenceDiagnostics = trackIrisSequenceDiagnostics;
        LocalVitalsEnabled = enableLocalVitals ?? LocalVitalsFeature.IsEnabled();
        _vitalsTracker = vitalsTracker ?? new UnrealDinosaurVitalsTracker();
        _vitalsCache = vitalsCache ?? new LocalVitalsSessionCache();
    }

    public bool LocalVitalsEnabled { get; }

    public bool HasRecentOutboundTraffic(string endpoint, DateTimeOffset now, TimeSpan maxAge)
    {
        lock (_outboundEndpointGate)
        {
            if (!_recentOutboundEndpoints.TryGetValue(endpoint, out var observedAt)
                || observedAt > now
                || now - observedAt > maxAge)
                return false;

            // A recently selected different game server on the same gameplay
            // port invalidates old DINORP evidence immediately on reconnect.
            var portSeparator = endpoint.LastIndexOf(':');
            var portSuffix = portSeparator >= 0 ? endpoint[portSeparator..] : string.Empty;
            return !_recentOutboundEndpoints.Any(entry =>
                !entry.Key.Equals(endpoint, StringComparison.OrdinalIgnoreCase)
                && entry.Key.EndsWith(portSuffix, StringComparison.OrdinalIgnoreCase)
                && entry.Value > observedAt);
        }
    }

    internal void RecordCapturedOutboundEndpoint(string endpoint, DateTimeOffset observedAt)
    {
        lock (_outboundEndpointGate)
        {
            _recentOutboundEndpoints[endpoint] = observedAt;
            // Bound this diagnostic set even when the game/Steam uses
            // short-lived UDP destinations during a long session.
            if (_recentOutboundEndpoints.Count > 32)
            {
                var oldest = _recentOutboundEndpoints.MinBy(entry => entry.Value).Key;
                _recentOutboundEndpoints.Remove(oldest);
            }
        }
    }

    public async IAsyncEnumerable<LocalMovementObservation> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException("A local movement source can only be watched once.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        var channel = Channel.CreateBounded<LocalMovementObservation>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        var captureTask = RunAsync(channel.Writer, linkedCancellation.Token);

        await foreach (var observation in channel.Reader
                           .ReadAllAsync(linkedCancellation.Token)
                           .ConfigureAwait(false))
        {
            yield return observation;
            // Npcap often delivers several saved-move packets in one burst.
            // Pausing the single-slot reader lets DropOldest retain only the
            // newest observation instead of making the marker replay the burst.
            // Keep this short enough for camera yaw and ground movement to feel
            // live in the overlay.
            await Task.Delay(MinimumObservationInterval, linkedCancellation.Token)
                .ConfigureAwait(false);
        }

        await captureTask.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        return ValueTask.CompletedTask;
    }

    public PacketPipelineDiagnostics GetPipelineDiagnostics()
    {
        var active = Volatile.Read(ref _activePacketIntakes);
        if (active is null)
        {
            return _lastPipelineDiagnostics;
        }

        var lanes = new PacketLaneDiagnostics(
            active.Outbound.Snapshot(),
            active.Inbound.Snapshot());
        return PacketPipelineDiagnostics.Combine(
            lanes.Outbound,
            lanes.Inbound,
            PacketSequenceDiagnostics.Combine(
                _outboundSequenceTracker.Snapshot(),
                _inboundSequenceTracker.Snapshot()),
            Interlocked.Read(ref _npcapDroppedPackets),
            Interlocked.Read(ref _interfaceDroppedPackets));
    }

    public PacketLaneDiagnostics GetLaneDiagnostics()
    {
        var intakes = Volatile.Read(ref _activePacketIntakes);
        return intakes is null
            ? _lastLaneDiagnostics
            : new PacketLaneDiagnostics(
                intakes.Outbound.Snapshot(),
                intakes.Inbound.Snapshot());
    }

    public LocalVitalsCaptureDiagnostics GetLocalVitalsDiagnostics()
    {
        var ticks = Interlocked.Read(ref _lastVitalsObservationUtcTicks);
        return new LocalVitalsCaptureDiagnostics(
            LocalVitalsEnabled,
            LocalVitalsFeature.SourceName,
            ticks == 0
                ? null
                : new DateTimeOffset(ticks, TimeSpan.Zero),
            Interlocked.Read(ref _publishedVitalsObservations),
            Interlocked.Read(ref _vitalsSessionResets));
    }

    private async Task RunAsync(
        ChannelWriter<LocalMovementObservation> writer,
        CancellationToken cancellationToken)
    {
        int? trackedProcessId = null;
        string? gameSessionId = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var processId = FindGameProcessId();
                var ports = processId is null
                    ? new HashSet<int>()
                    : _portResolver.GetOwnedPorts(processId.Value);
                if (processId is null || ports.Count == 0)
                {
                    if (processId is null && trackedProcessId is not null)
                    {
                        ResetTrackers();
                        trackedProcessId = null;
                        gameSessionId = null;
                    }

                    await Task.Delay(ProcessPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (trackedProcessId != processId)
                {
                    ResetTrackers();
                    trackedProcessId = processId;
                    gameSessionId = GetGameSessionId(processId.Value);
                    lock (_vitalsTrackerGate)
                    {
                        _activeGameSessionId = gameSessionId;
                    }
                }

                lock (_vitalsTrackerGate)
                {
                    _activeGameSessionId = gameSessionId;
                }

                var exitReason = await CaptureUntilEndpointChangesAsync(
                        processId.Value,
                        gameSessionId!,
                        ports,
                        writer,
                        cancellationToken)
                    .ConfigureAwait(false);

                // A port-set refresh is intentionally not a capture-scope
                // boundary.  Only a process/session transition invalidates
                // decoder state; otherwise the tracker would repeatedly lose
                // its eight-sample bootstrap window during harmless socket
                // churn.
                if (!cancellationToken.IsCancellationRequested
                    && exitReason is CaptureExitReason.ProcessChanged
                        or CaptureExitReason.SessionChanged)
                {
                    ResetTrackers();
                    trackedProcessId = null;
                    gameSessionId = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
            return;
        }

        writer.TryComplete();
    }

    private async Task<CaptureExitReason> CaptureUntilEndpointChangesAsync(
        int processId,
        string gameSessionId,
        IReadOnlySet<int> ports,
        ChannelWriter<LocalMovementObservation> writer,
        CancellationToken cancellationToken)
    {
        var ownedPorts = new OwnedUdpPortSnapshot(ports);
        var intakes = new PacketIntakePair(
            new BoundedPacketIntake(),
            new BoundedPacketIntake(InboundPacketCapacity, InboundByteCapacity));
        Volatile.Write(ref _activePacketIntakes, intakes);
        var outboundDecoderTask = ProcessOutboundPacketQueueAsync(
            intakes.Outbound,
            writer,
            cancellationToken);
        var inboundDecoderTask = ProcessInboundPacketQueueAsync(
            intakes.Inbound,
            gameSessionId,
            writer,
            cancellationToken);
        IReadOnlyList<OpenedCapture> devices = [];
        var exitReason = CaptureExitReason.Cancelled;
        try
        {
            devices = OpenCaptureDevices(ownedPorts, intakes);
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(OwnedPortRefreshInterval, cancellationToken)
                    .ConfigureAwait(false);

                if (FindGameProcessId() != processId)
                {
                    exitReason = CaptureExitReason.ProcessChanged;
                    break;
                }

                // A PID can be reused after a fast game restart.  Treat the
                // process start time as the session identity so old decoder
                // hypotheses and cached vitals never cross that boundary.
                var currentSessionId = GetGameSessionId(processId);
                if (!string.Equals(
                        currentSessionId,
                        gameSessionId,
                        StringComparison.Ordinal))
                {
                    exitReason = CaptureExitReason.SessionChanged;
                    break;
                }

                // Do not tear down adapters when sockets are rebound.  The
                // packet callback reads this immutable snapshot atomically;
                // replacing it is enough to classify the next datagram using
                // the current owner set.
                ownedPorts.Replace(_portResolver.GetOwnedPorts(processId));
            }
        }
        finally
        {
            foreach (var capture in devices)
            {
                capture.Device.OnPacketArrival -= capture.Handler;
                try
                {
                    capture.Device.StopCapture();
                }
                catch
                {
                }

                RecordCaptureStatistics(capture.Device);

                try
                {
                    capture.Device.Close();
                }
                catch
                {
                }
            }

            intakes.Outbound.Complete();
            intakes.Inbound.Complete();
            try
            {
                await Task.WhenAll(outboundDecoderTask, inboundDecoderTask)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            _lastLaneDiagnostics = new PacketLaneDiagnostics(
                intakes.Outbound.Snapshot(),
                intakes.Inbound.Snapshot());
            _lastPipelineDiagnostics = PacketPipelineDiagnostics.Combine(
                _lastLaneDiagnostics.Outbound,
                _lastLaneDiagnostics.Inbound,
                PacketSequenceDiagnostics.Combine(
                    _outboundSequenceTracker.Snapshot(),
                    _inboundSequenceTracker.Snapshot()),
                Interlocked.Read(ref _npcapDroppedPackets),
                Interlocked.Read(ref _interfaceDroppedPackets));
            Interlocked.CompareExchange(ref _activePacketIntakes, null, intakes);
        }

        return exitReason;
    }

    private IReadOnlyList<OpenedCapture> OpenCaptureDevices(
        OwnedUdpPortSnapshot ownedPorts,
        PacketIntakePair intakes)
    {
        CaptureDeviceList devices;
        try
        {
            NpcapAvailabilityProbe.EnsureNativeLibraryResolver();
            // Do not use CaptureDeviceList.Instance here. The singleton can be
            // initialized before the user finishes installing Npcap and then
            // keep a failed native state for the lifetime of this process.
            devices = CaptureDeviceList.New();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or TypeInitializationException
                or PcapException)
        {
            throw new LocalPacketCaptureUnavailableException(
                "Npcap is not installed or its packet-capture driver is unavailable.",
                exception);
        }

        var candidates = SelectActiveDevices(devices).ToArray();
        var opened = new List<OpenedCapture>();
        // Capture both directions, then hand them to independent bounded
        // workers. Inbound replication can be much heavier than saved moves
        // and must never add backpressure to the GPS hot path.
        // Port ownership is dynamic, so a port-specific kernel filter would
        // miss packets immediately after a reconnect.  Keep one stable UDP
        // filter and perform the cheap ownership check in user mode.
        const string filter = "udp";
        foreach (var device in candidates)
        {
            PacketArrivalEventHandler handler = (_, packetCapture) =>
                EnqueuePacket(packetCapture, ownedPorts, intakes);
            try
            {
                device.Open(DeviceModes.None, read_timeout: CaptureReadTimeoutMilliseconds);
                device.Filter = filter;
                device.OnPacketArrival += handler;
                device.StartCapture();
                opened.Add(new OpenedCapture(device, handler));
            }
            catch
            {
                device.OnPacketArrival -= handler;
                try
                {
                    device.Close();
                }
                catch
                {
                }
            }
        }

        if (opened.Count == 0)
        {
            throw new LocalPacketCaptureUnavailableException(
                "No active network adapter could be opened through Npcap.");
        }

        return opened;
    }

    private void EnqueuePacket(
        PacketCapture packetCapture,
        OwnedUdpPortSnapshot ownedPorts,
        PacketIntakePair intakes)
    {
        try
        {
            var rawPacket = packetCapture.GetPacket();
            var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
            var ip = packet.Extract<IPPacket>();
            var udp = packet.Extract<UdpPacket>();
            if (udp is null)
            {
                return;
            }

            var payload = udp.PayloadData;
            if (payload is null || payload.Length == 0)
            {
                return;
            }

            // Capture the reference once so source/destination classification
            // uses one coherent owner set even if the resolver refreshes it
            // concurrently on the capture supervisor thread.
            var ports = ownedPorts.Current;
            var direction = ClassifyDirection(
                udp.SourcePort,
                udp.DestinationPort,
                ports);
            if (direction is null)
            {
                return;
            }

            var outbound = direction == PacketDirection.Outbound;
            var datagram = new CapturedUdpDatagram(
                DateTimeOffset.UtcNow,
                ip?.SourceAddress.ToString(),
                udp.SourcePort,
                ip?.DestinationAddress.ToString(),
                udp.DestinationPort,
                payload.ToArray(),
                Inbound: !outbound,
                Outbound: outbound);
            _ = outbound
                ? intakes.Outbound.TryEnqueue(datagram)
                : intakes.Inbound.TryEnqueue(datagram);
        }
        catch
        {
            // Malformed or unrelated UDP traffic must not stop local tracking.
        }
    }

    private async Task ProcessOutboundPacketQueueAsync(
        BoundedPacketIntake rawPackets,
        ChannelWriter<LocalMovementObservation> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var packet in rawPackets
                           .ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            ProcessOutboundPacket(packet, writer);
        }
    }

    private async Task ProcessInboundPacketQueueAsync(
        BoundedPacketIntake rawPackets,
        string gameSessionId,
        ChannelWriter<LocalMovementObservation> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var packet in rawPackets
                           .ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            ProcessInboundPacket(packet, gameSessionId, writer);
        }
    }

    internal void ProcessOutboundPacket(
        CapturedUdpDatagram packet,
        ChannelWriter<LocalMovementObservation> writer)
    {
        try
        {
            var payload = packet.Payload;
            var observedAt = packet.ObservedAt;
            if (!packet.Outbound)
            {
                return;
            }

            var serverEndpoint = packet.DestinationAddress is null
                ? null
                : $"{packet.DestinationAddress}:{packet.DestinationPort}";
            if (serverEndpoint is not null)
            {
                RecordCapturedOutboundEndpoint(serverEndpoint, observedAt);
            }

            if (_trackIrisSequenceDiagnostics)
            {
                _ = TryObserveIrisSequence(
                    packet,
                    inbound: false,
                    recordDiagnostics: true);
            }

            UnrealMovementCandidate movement;
            lock (_movementTrackerGate)
            {
                // Game-owned UDP includes Steam/service traffic. Isolate tracker
                // hypotheses by flow; only a confirmed movement stream may
                // select the stats endpoint. No special-case server port.
                var flowKey = $"{serverEndpoint}|{packet.SourcePort}";
                if (!_movementFlows.TryGetValue(flowKey, out var flow))
                {
                    if (_movementFlows.Count >= 64)
                        _movementFlows.Remove(_movementFlows.MinBy(p => p.Value.LastSeen).Key);
                    flow = (_movementFlows.Count == 0 ? _tracker : new LocalMovementTracker(), observedAt);
                }
                if (observedAt < flow.LastSeen) return;
                _movementFlows[flowKey] = (flow.Tracker, observedAt);
                if (!flow.Tracker.TryTrack(payload, observedAt, out movement))
                {
                    return;
                }
            }
            if (LocalVitalsEnabled)
            {
                EstablishCapturedOutboundEndpoint(serverEndpoint);
            }

            var publishedVitals = Volatile.Read(ref _latestVitals);
            var vitals = publishedVitals is not null
                         && string.Equals(
                             publishedVitals.ServerEndpoint,
                             serverEndpoint,
                             StringComparison.OrdinalIgnoreCase)
                ? publishedVitals.Observation
                : (LocalDinosaurVitalsObservation?)null;

            PublishObservation(writer, new LocalMovementObservation(
                observedAt,
                movement,
                serverEndpoint,
                vitals));
        }
        catch
        {
            // One malformed datagram must not stop the ordered worker.
        }
    }

    internal void SetReplayGameSession(string session)
    {
        lock (_vitalsTrackerGate) _activeGameSessionId = session;
    }

    internal void ProcessInboundPacket(
        CapturedUdpDatagram packet,
        string gameSessionId,
        ChannelWriter<LocalMovementObservation> writer)
    {
        try
        {
            if (_trackIrisSequenceDiagnostics)
            {
                TryObserveIrisSequence(
                    packet,
                    inbound: true,
                    recordDiagnostics: true);
            }

            if (!LocalVitalsEnabled)
            {
                return;
            }

            var serverEndpoint = packet.SourceAddress is null
                ? null
                : $"{packet.SourceAddress}:{packet.SourcePort}";
            if (string.IsNullOrWhiteSpace(serverEndpoint))
            {
                return;
            }

            var capturedOutboundEndpoint = Volatile.Read(
                ref _latestCapturedOutboundEndpoint);
            if (!string.Equals(
                    capturedOutboundEndpoint,
                    serverEndpoint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PrepareVitalsEndpoint(serverEndpoint);
            if (!_replaceIslePilotStats) EnsureVitalsCacheSeeded(
                gameSessionId,
                serverEndpoint,
                packet.ObservedAt);

            LocalDinosaurVitalsObservation observation;
            lock (_vitalsTrackerGate)
            {
                // Outbound traffic establishes the authoritative active
                // endpoint. Late packets from a previous server are ignored.
                if (!string.Equals(
                        _activeVitalsEndpoint,
                        serverEndpoint,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        _activeGameSessionId,
                        gameSessionId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                if (_replaceIslePilotStats)
                {
                    if (!_inboundStats.TryTrack(packet.Payload, packet.ObservedAt,
                            $"{gameSessionId}|{serverEndpoint}|{packet.DestinationPort}", out observation)) return;
                }
                else if (!_vitalsTracker.TryTrack(
                        packet.Payload,
                        packet.ObservedAt,
                        out observation))
                {
                    return;
                }

            }

            // Cache I/O is intentionally outside the tracker lock so a slow
            // disk cannot make the outbound movement worker wait.
            if (!_replaceIslePilotStats) observation = _vitalsCache.Enrich(
                gameSessionId,
                serverEndpoint,
                observation);
            lock (_vitalsTrackerGate)
            {
                if (!string.Equals(
                        _activeVitalsEndpoint,
                        serverEndpoint,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        _activeGameSessionId,
                        gameSessionId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                Volatile.Write(
                    ref _latestVitals,
                    new PublishedVitals(
                        gameSessionId,
                        serverEndpoint,
                        observation));
            }

            Interlocked.Exchange(
                ref _lastVitalsObservationUtcTicks,
                observation.ObservedAt.UtcTicks);
            Interlocked.Increment(ref _publishedVitalsObservations);
            PublishObservation(
                writer,
                LocalMovementObservation.VitalsOnly(observation, serverEndpoint));
        }
        catch
        {
            // One malformed/incomplete inbound datagram must not stop either
            // ordered worker or the independent outbound GPS lane.
        }
    }

    private void PrepareVitalsEndpoint(string? serverEndpoint)
    {
        if (string.IsNullOrWhiteSpace(serverEndpoint))
        {
            return;
        }

        if (string.Equals(
                Volatile.Read(ref _activeVitalsEndpoint),
                serverEndpoint,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_vitalsTrackerGate)
        {
            if (string.Equals(
                    _activeVitalsEndpoint,
                    serverEndpoint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _vitalsTracker.Reset();
            _inboundStats.Reset();
            Volatile.Write(ref _latestVitals, null);
            _activeVitalsEndpoint = serverEndpoint;
            _vitalsCacheSeeded = false;
            Interlocked.Increment(ref _vitalsSessionResets);
        }
    }

    private void EstablishCapturedOutboundEndpoint(string? serverEndpoint)
    {
        if (string.IsNullOrWhiteSpace(serverEndpoint))
        {
            return;
        }

        Volatile.Write(ref _latestCapturedOutboundEndpoint, serverEndpoint);
        PrepareVitalsEndpoint(serverEndpoint);
    }

    private void EnsureVitalsCacheSeeded(
        string gameSessionId,
        string serverEndpoint,
        DateTimeOffset observedAt)
    {
        lock (_vitalsTrackerGate)
        {
            if (_vitalsCacheSeeded
                || !string.Equals(
                    _activeVitalsEndpoint,
                    serverEndpoint,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    _activeGameSessionId,
                    gameSessionId,
                    StringComparison.Ordinal))
            {
                return;
            }

            // Claim the attempt before disk access. Endpoint/session checks
            // below discard a seed that raced with a connection switch.
            _vitalsCacheSeeded = true;
        }

        if (!_vitalsCache.TryRestoreLatest(
                gameSessionId,
                serverEndpoint,
                observedAt,
                out var restored))
        {
            return;
        }

        lock (_vitalsTrackerGate)
        {
            if (string.Equals(
                    _activeVitalsEndpoint,
                    serverEndpoint,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    _activeGameSessionId,
                    gameSessionId,
                    StringComparison.Ordinal))
            {
                // Seeding remains actor-keyed. If the next verified owner has
                // another NetRef handle, these maximums cannot be applied.
                _vitalsTracker.SeedVerifiedVitals(restored);
            }
        }
    }

    private void PublishObservation(
        ChannelWriter<LocalMovementObservation> writer,
        LocalMovementObservation update)
    {
        LocalMovementObservation next;
        lock (_latestObservationGate)
        {
            next = LocalMovementObservation.Coalesce(_latestObservation, update);
            _latestObservation = next;
        }

        // Every vitals-only update is coalesced with the newest movement while
        // preserving its original timestamp. Therefore an inbound burst may
        // replace an output item, but cannot erase or falsely refresh GPS.
        writer.TryWrite(next);
    }

    private bool TryObserveIrisSequence(
        CapturedUdpDatagram packet,
        bool inbound,
        bool recordDiagnostics)
    {
        if (!_irisPacketParser.TryParse(packet.Payload, out var irisPacket))
        {
            return false;
        }

        if (recordDiagnostics)
        {
            var tracker = inbound
                ? _inboundSequenceTracker
                : _outboundSequenceTracker;
            tracker.Observe(
                new PacketFlowKey(
                    inbound ? packet.SourceAddress ?? string.Empty : packet.DestinationAddress ?? string.Empty,
                    inbound ? packet.SourcePort : packet.DestinationPort,
                    inbound ? packet.DestinationPort : packet.SourcePort,
                    inbound ? PacketDirection.Inbound : PacketDirection.Outbound),
                irisPacket.PacketSequence,
                irisPacket.IsComplete);
        }

        return true;
    }

    private void ResetTrackers()
    {
        lock (_outboundEndpointGate)
        {
            _recentOutboundEndpoints.Clear();
        }
        lock (_movementTrackerGate)
        {
            _tracker.Reset();
            _movementFlows.Clear();
        }

        lock (_vitalsTrackerGate)
        {
            _vitalsTracker.Reset();
            _inboundStats.Reset();
            Volatile.Write(ref _latestVitals, null);
            _activeVitalsEndpoint = null;
            Volatile.Write(ref _latestCapturedOutboundEndpoint, null);
            _activeGameSessionId = null;
            _vitalsCacheSeeded = false;
        }

        lock (_latestObservationGate)
        {
            _latestObservation = null;
        }

        _outboundSequenceTracker.Reset();
        _inboundSequenceTracker.Reset();
        Interlocked.Increment(ref _vitalsSessionResets);
        Interlocked.Exchange(ref _npcapDroppedPackets, 0);
        Interlocked.Exchange(ref _interfaceDroppedPackets, 0);
    }

    private void RecordCaptureStatistics(ILiveDevice device)
    {
        try
        {
            var statistics = device.Statistics;
            if (statistics is null)
            {
                return;
            }

            Interlocked.Add(ref _npcapDroppedPackets, statistics.DroppedPackets);
            Interlocked.Add(
                ref _interfaceDroppedPackets,
                statistics.InterfaceDroppedPackets);
        }
        catch
        {
            // Some adapters/drivers do not expose capture statistics.
        }
    }

    private int? FindGameProcessId()
    {
        var processes = Process.GetProcessesByName(_processName);
        try
        {
            return processes.Length == 0 ? null : processes[0].Id;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string GetGameSessionId(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return $"{processId}:{process.StartTime.ToUniversalTime().Ticks}";
        }
        catch
        {
            // PID still scopes the in-memory session. The cache remains
            // actor- and endpoint-keyed if start time cannot be queried.
            return processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static IEnumerable<ILiveDevice> SelectActiveDevices(CaptureDeviceList devices)
    {
        var activeDescriptions = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => IsEligibleCaptureNetwork(
                network.OperationalStatus,
                network.NetworkInterfaceType))
            .Select(network => network.Description)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = devices
            .Where(device =>
                !string.IsNullOrWhiteSpace(device.Description)
                && activeDescriptions.Contains(device.Description))
            .ToArray();
        return active.Length > 0 ? active : devices;
    }

    internal static bool IsEligibleCaptureNetwork(
        OperationalStatus operationalStatus,
        NetworkInterfaceType networkInterfaceType) =>
        operationalStatus == OperationalStatus.Up
        && networkInterfaceType != NetworkInterfaceType.Loopback;

    internal static PacketDirection? ClassifyDirection(
        int sourcePort,
        int destinationPort,
        IReadOnlySet<int> ownedPorts)
    {
        var sourceOwned = ownedPorts.Contains(sourcePort);
        var destinationOwned = ownedPorts.Contains(destinationPort);
        return (sourceOwned, destinationOwned) switch
        {
            (true, false) => PacketDirection.Outbound,
            (false, true) => PacketDirection.Inbound,
            _ => null
        };
    }

    internal static string BuildCaptureFilter(
        IEnumerable<int> ports,
        bool includeInbound = true)
    {
        var orderedPorts = ports.Distinct().Order().ToArray();
        if (orderedPorts.Length == 0)
        {
            return "udp and (false)";
        }

        var clauses = orderedPorts.SelectMany(port => includeInbound
            ? new[] { $"src port {port}", $"dst port {port}" }
            : new[] { $"src port {port}" });
        return $"udp and ({string.Join(" or ", clauses)})";
    }

    private enum CaptureExitReason
    {
        Cancelled,
        ProcessChanged,
        SessionChanged
    }

    /// <summary>
    /// Atomically published, immutable snapshots of the UDP ports currently
    /// owned by the game process.  The Npcap callback can therefore classify
    /// packets without taking a lock while the supervisor refreshes sockets.
    /// </summary>
    internal sealed class OwnedUdpPortSnapshot
    {
        private IReadOnlySet<int> _current;

        public OwnedUdpPortSnapshot(IEnumerable<int> ports)
        {
            _current = Copy(ports);
        }

        public IReadOnlySet<int> Current => Volatile.Read(ref _current);

        public bool Replace(IEnumerable<int> ports)
        {
            var next = Copy(ports);
            var previous = Volatile.Read(ref _current);
            if (previous.SetEquals(next))
            {
                return false;
            }

            Volatile.Write(ref _current, next);
            return true;
        }

        private static IReadOnlySet<int> Copy(IEnumerable<int> ports) =>
            ports is HashSet<int> hashSet
                ? new HashSet<int>(hashSet)
                : ports.ToHashSet();
    }

    private sealed record PacketIntakePair(
        BoundedPacketIntake Outbound,
        BoundedPacketIntake Inbound);

    private sealed record PublishedVitals(
        string GameSessionId,
        string ServerEndpoint,
        LocalDinosaurVitalsObservation Observation);

    private sealed record OpenedCapture(
        ILiveDevice Device,
        PacketArrivalEventHandler Handler);

}
