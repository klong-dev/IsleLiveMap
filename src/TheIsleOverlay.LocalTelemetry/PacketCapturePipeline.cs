using System.Threading.Channels;

namespace TheIsleOverlay.LocalTelemetry;

internal enum PacketDirection
{
    Inbound,
    Outbound
}

internal readonly record struct CapturedUdpDatagram(
    DateTimeOffset ObservedAt,
    string? SourceAddress,
    int SourcePort,
    string? DestinationAddress,
    int DestinationPort,
    byte[] Payload,
    bool Inbound = false,
    bool Outbound = false);

internal readonly record struct PacketFlowKey(
    string RemoteAddress,
    int RemotePort,
    int LocalPort,
    PacketDirection Direction);

internal enum PacketSequenceDisposition
{
    First,
    InOrder,
    ForwardGap,
    Reordered,
    Duplicate
}

public readonly record struct PacketPipelineDiagnostics(
    long CapturedPackets,
    long ProcessedPackets,
    long QueueDroppedPackets,
    long QueueDroppedBytes,
    int QueueHighWatermark,
    long NpcapDroppedPackets,
    long InterfaceDroppedPackets,
    long IrisPackets,
    long IncompleteIrisPackets,
    long SequenceGapPackets,
    long ReorderedPackets,
    long DuplicatePackets);

/// <summary>
/// A non-blocking, memory-bounded hand-off between the Npcap callback and the
/// telemetry decoders. The callback must never wait for a slow parser because
/// doing so moves loss into the kernel/Npcap capture buffer where it cannot be
/// measured or recovered by the application.
/// </summary>
internal sealed class BoundedPacketIntake
{
    internal const int DefaultPacketCapacity = 8_192;
    internal const long DefaultByteCapacity = 32L * 1024 * 1024;

    private readonly Channel<CapturedUdpDatagram> _channel;
    private readonly long _byteCapacity;
    private long _queuedBytes;
    private int _queuedPackets;
    private long _capturedPackets;
    private long _processedPackets;
    private long _droppedPackets;
    private long _droppedBytes;
    private int _highWatermark;

    public BoundedPacketIntake(
        int packetCapacity = DefaultPacketCapacity,
        long byteCapacity = DefaultByteCapacity)
    {
        if (packetCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packetCapacity));
        }

        if (byteCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCapacity));
        }

        _byteCapacity = byteCapacity;
        // Wait mode is intentional even though nobody waits to write. It makes
        // TryWrite return false when full, giving us an exact application-drop
        // counter; DropOldest/DropWrite report success after silently dropping.
        _channel = Channel.CreateBounded<CapturedUdpDatagram>(
            new BoundedChannelOptions(packetCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    public bool TryEnqueue(CapturedUdpDatagram datagram)
    {
        var byteCount = datagram.Payload.Length;
        Interlocked.Increment(ref _capturedPackets);
        if (byteCount <= 0 || byteCount > _byteCapacity)
        {
            RecordDrop(byteCount);
            return false;
        }

        var queuedBytes = Interlocked.Add(ref _queuedBytes, byteCount);
        if (queuedBytes > _byteCapacity)
        {
            Interlocked.Add(ref _queuedBytes, -byteCount);
            RecordDrop(byteCount);
            return false;
        }

        var queuedPackets = Interlocked.Increment(ref _queuedPackets);
        if (!_channel.Writer.TryWrite(datagram))
        {
            Interlocked.Decrement(ref _queuedPackets);
            Interlocked.Add(ref _queuedBytes, -byteCount);
            RecordDrop(byteCount);
            return false;
        }

        UpdateHighWatermark(queuedPackets);
        return true;
    }

    public async IAsyncEnumerable<CapturedUdpDatagram> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await foreach (var datagram in _channel.Reader
                           .ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _queuedPackets);
            Interlocked.Add(ref _queuedBytes, -datagram.Payload.Length);
            Interlocked.Increment(ref _processedPackets);
            yield return datagram;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();

    public PacketPipelineDiagnostics Snapshot(
        PacketSequenceDiagnostics sequenceDiagnostics = default,
        long npcapDroppedPackets = 0,
        long interfaceDroppedPackets = 0) => new(
        Interlocked.Read(ref _capturedPackets),
        Interlocked.Read(ref _processedPackets),
        Interlocked.Read(ref _droppedPackets),
        Interlocked.Read(ref _droppedBytes),
        Volatile.Read(ref _highWatermark),
        npcapDroppedPackets,
        interfaceDroppedPackets,
        sequenceDiagnostics.IrisPackets,
        sequenceDiagnostics.IncompleteIrisPackets,
        sequenceDiagnostics.SequenceGapPackets,
        sequenceDiagnostics.ReorderedPackets,
        sequenceDiagnostics.DuplicatePackets);

    private void RecordDrop(long byteCount)
    {
        Interlocked.Increment(ref _droppedPackets);
        Interlocked.Add(ref _droppedBytes, Math.Max(0, byteCount));
    }

    private void UpdateHighWatermark(int candidate)
    {
        var current = Volatile.Read(ref _highWatermark);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(
                ref _highWatermark,
                candidate,
                current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}

internal readonly record struct PacketSequenceDiagnostics(
    long IrisPackets,
    long IncompleteIrisPackets,
    long SequenceGapPackets,
    long ReorderedPackets,
    long DuplicatePackets);

/// <summary>
/// Tracks Unreal's 14-bit packet sequence independently for each endpoint and
/// direction. A sliding history distinguishes late packets from duplicates and
/// handles the 16383 -> 0 wrap without treating it as a reconnect-sized gap.
/// </summary>
internal sealed class IrisPacketSequenceTracker
{
    private const int SequenceModulus = 1 << 14;
    private const int HalfRange = SequenceModulus / 2;
    private const int HistorySize = 256;
    private readonly Dictionary<PacketFlowKey, FlowState> _flows = [];
    private long _irisPackets;
    private long _incompleteIrisPackets;
    private long _sequenceGapPackets;
    private long _reorderedPackets;
    private long _duplicatePackets;

    public PacketSequenceDisposition Observe(
        PacketFlowKey flow,
        int packetSequence,
        bool isComplete)
    {
        if (packetSequence is < 0 or >= SequenceModulus)
        {
            throw new ArgumentOutOfRangeException(nameof(packetSequence));
        }

        Interlocked.Increment(ref _irisPackets);
        if (!isComplete)
        {
            Interlocked.Increment(ref _incompleteIrisPackets);
        }

        if (!_flows.TryGetValue(flow, out var state))
        {
            state = new FlowState(packetSequence);
            _flows.Add(flow, state);
            return PacketSequenceDisposition.First;
        }

        var forward = (packetSequence - state.Latest + SequenceModulus)
                      % SequenceModulus;
        if (forward == 0 || state.Recent.Contains(packetSequence))
        {
            Interlocked.Increment(ref _duplicatePackets);
            return PacketSequenceDisposition.Duplicate;
        }

        if (forward < HalfRange)
        {
            state.Latest = packetSequence;
            state.Remember(packetSequence);
            if (forward == 1)
            {
                return PacketSequenceDisposition.InOrder;
            }

            Interlocked.Add(ref _sequenceGapPackets, forward - 1);
            return PacketSequenceDisposition.ForwardGap;
        }

        state.Remember(packetSequence);
        Interlocked.Increment(ref _reorderedPackets);
        return PacketSequenceDisposition.Reordered;
    }

    public PacketSequenceDiagnostics Snapshot() => new(
        Interlocked.Read(ref _irisPackets),
        Interlocked.Read(ref _incompleteIrisPackets),
        Interlocked.Read(ref _sequenceGapPackets),
        Interlocked.Read(ref _reorderedPackets),
        Interlocked.Read(ref _duplicatePackets));

    public void Reset()
    {
        _flows.Clear();
        Interlocked.Exchange(ref _irisPackets, 0);
        Interlocked.Exchange(ref _incompleteIrisPackets, 0);
        Interlocked.Exchange(ref _sequenceGapPackets, 0);
        Interlocked.Exchange(ref _reorderedPackets, 0);
        Interlocked.Exchange(ref _duplicatePackets, 0);
    }

    private sealed class FlowState
    {
        private readonly Queue<int> _history = [];

        public FlowState(int first)
        {
            Latest = first;
            Remember(first);
        }

        public int Latest { get; set; }
        public HashSet<int> Recent { get; } = [];

        public void Remember(int sequence)
        {
            if (!Recent.Add(sequence))
            {
                return;
            }

            _history.Enqueue(sequence);
            while (_history.Count > HistorySize)
            {
                Recent.Remove(_history.Dequeue());
            }
        }
    }
}
