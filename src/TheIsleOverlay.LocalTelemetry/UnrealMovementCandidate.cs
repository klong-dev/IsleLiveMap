using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

public readonly record struct UnrealMovementCandidate(
    double X,
    double Y,
    double Z,
    double UnrealYawDegrees,
    float ClientTimestamp,
    int PayloadLength,
    int LocationBitOffset,
    int ComponentBitCount)
{
    public WorldLocation Location => new()
    {
        X = X,
        Y = Y,
        Z = Z
    };

    public double MapHeadingDegrees => MapHeading.FromUnrealYaw(UnrealYawDegrees);

    internal MovementLayout Layout => new(
        PayloadLength,
        LocationBitOffset,
        ComponentBitCount);
}

internal readonly record struct MovementLayout(
    int PayloadLength,
    int LocationBitOffset,
    int ComponentBitCount);
