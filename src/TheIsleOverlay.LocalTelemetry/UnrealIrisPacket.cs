namespace TheIsleOverlay.LocalTelemetry;

public readonly record struct UnrealIrisPacket(
    int PacketSequence,
    bool HasDataStream,
    bool IsComplete,
    IReadOnlyList<UnrealIrisReplicationBatch> Batches,
    string? IncompleteReason = null);
