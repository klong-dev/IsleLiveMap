using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class PacketCapturePipelineTests
{
    [Fact]
    public async Task Intake_RejectsOverflowWithoutBlockingAndCountsDrop()
    {
        var intake = new BoundedPacketIntake(packetCapacity: 1, byteCapacity: 16);
        var first = Datagram([1, 2, 3]);
        var second = Datagram([4, 5, 6]);

        Assert.True(intake.TryEnqueue(first));
        Assert.False(intake.TryEnqueue(second));
        intake.Complete();

        var drained = new List<CapturedUdpDatagram>();
        await foreach (var item in intake.ReadAllAsync(CancellationToken.None))
        {
            drained.Add(item);
        }

        var diagnostics = intake.Snapshot();
        Assert.Single(drained);
        Assert.Equal(2, diagnostics.CapturedPackets);
        Assert.Equal(1, diagnostics.ProcessedPackets);
        Assert.Equal(1, diagnostics.QueueDroppedPackets);
        Assert.Equal(3, diagnostics.QueueDroppedBytes);
        Assert.Equal(1, diagnostics.QueueHighWatermark);
    }

    [Fact]
    public void SequenceTracker_HandlesGapLateRecoveryDuplicateAndWrap()
    {
        var tracker = new IrisPacketSequenceTracker();
        var flow = new PacketFlowKey("127.0.0.1", 7777, 50000, PacketDirection.Inbound);

        Assert.Equal(PacketSequenceDisposition.First, tracker.Observe(flow, 16382, true));
        Assert.Equal(PacketSequenceDisposition.InOrder, tracker.Observe(flow, 16383, true));
        Assert.Equal(PacketSequenceDisposition.ForwardGap, tracker.Observe(flow, 1, false));
        Assert.Equal(PacketSequenceDisposition.Reordered, tracker.Observe(flow, 0, true));
        Assert.Equal(PacketSequenceDisposition.Duplicate, tracker.Observe(flow, 0, true));

        var diagnostics = tracker.Snapshot();
        Assert.Equal(5, diagnostics.IrisPackets);
        Assert.Equal(1, diagnostics.IncompleteIrisPackets);
        Assert.Equal(1, diagnostics.SequenceGapPackets);
        Assert.Equal(1, diagnostics.ReorderedPackets);
        Assert.Equal(1, diagnostics.DuplicatePackets);
    }

    [Fact]
    public void SequenceTracker_KeepsDirectionsIndependent()
    {
        var tracker = new IrisPacketSequenceTracker();
        var inbound = new PacketFlowKey("server", 7777, 50000, PacketDirection.Inbound);
        var outbound = inbound with { Direction = PacketDirection.Outbound };

        Assert.Equal(PacketSequenceDisposition.First, tracker.Observe(inbound, 100, true));
        Assert.Equal(PacketSequenceDisposition.First, tracker.Observe(outbound, 800, true));
        Assert.Equal(PacketSequenceDisposition.InOrder, tracker.Observe(inbound, 101, true));
        Assert.Equal(PacketSequenceDisposition.InOrder, tracker.Observe(outbound, 801, true));
        Assert.Equal(0, tracker.Snapshot().SequenceGapPackets);
    }

    private static CapturedUdpDatagram Datagram(byte[] payload) => new(
        DateTimeOffset.UtcNow,
        "127.0.0.1",
        7777,
        "127.0.0.1",
        50000,
        payload,
        Inbound: true);
}
