namespace TheIsleOverlay.LocalTelemetry;

public readonly record struct UnrealIrisReplicationBatch(
    ulong NetRefHandle,
    int DataBitOffset,
    int DataBitCount,
    bool HasOwnerData,
    bool HasExports);
