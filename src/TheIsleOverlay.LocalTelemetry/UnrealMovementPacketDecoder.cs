using System.Numerics;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Decodes the FVector_NetQuantize100 location and compressed control yaw from
/// client-to-server FCharacterNetworkMoveData payloads used by UE 5.5.
/// </summary>
public sealed class UnrealMovementPacketDecoder
{
    private const int QuantizedVectorHeaderBits = 7;
    private const int FloatBits = 32;
    private const int MinimumComponentBits = 1;
    private const int MaximumComponentBits = 31;

    // Gateway's playable texture covers roughly X -505k..607k and
    // Y -607k..509k. The margin tolerates map revisions and world rebasing.
    private const double MinimumWorldX = -800_000d;
    private const double MaximumWorldX = 800_000d;
    private const double MinimumWorldY = -800_000d;
    private const double MaximumWorldY = 800_000d;
    private const double MinimumWorldZ = -300_000d;
    private const double MaximumWorldZ = 300_000d;
    private const double MaximumAcceleration = 100_000d;
    private const float MaximumClientTimestamp = 10_000_000f;

    public IReadOnlyList<UnrealMovementCandidate> Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return [];
        }

        var candidates = new List<UnrealMovementCandidate>();
        var payloadBits = payload.Length * 8;
        var minimumMoveBits = QuantizedVectorHeaderBits + MinimumComponentBits * 3 + 3;

        for (var locationOffset = 0;
             locationOffset + minimumMoveBits <= payloadBits;
             locationOffset++)
        {
            var header = (int)ReadBits(payload, locationOffset, QuantizedVectorHeaderBits);
            var componentBits = header & 63;
            var usesScale = (header & 64) != 0;
            if (!usesScale
                || componentBits is < MinimumComponentBits or > MaximumComponentBits
                || locationOffset + QuantizedVectorHeaderBits + componentBits * 3 + 3 > payloadBits)
            {
                continue;
            }

            var rawX = ReadSigned(payload, locationOffset + QuantizedVectorHeaderBits, componentBits);
            var rawY = ReadSigned(payload, locationOffset + QuantizedVectorHeaderBits + componentBits, componentBits);
            var rawZ = ReadSigned(payload, locationOffset + QuantizedVectorHeaderBits + componentBits * 2, componentBits);
            if (!UsesCanonicalBitCount(rawX, rawY, rawZ, componentBits))
            {
                continue;
            }

            var x = rawX / 100d;
            var y = rawY / 100d;
            var z = rawZ / 100d;
            if (!IsPlausibleWorldLocation(x, y, z)
                || !TryReadMovePrefix(payload, locationOffset, out var clientTimestamp))
            {
                continue;
            }

            var rotationOffset = locationOffset + QuantizedVectorHeaderBits + componentBits * 3;
            if (!TryReadCompressedRotation(payload, rotationOffset, out _, out var yaw, out _))
            {
                continue;
            }

            candidates.Add(new UnrealMovementCandidate(
                x,
                y,
                z,
                yaw,
                clientTimestamp,
                payload.Length,
                locationOffset,
                componentBits));
        }

        return candidates;
    }

    private static bool TryReadMovePrefix(
        ReadOnlySpan<byte> payload,
        int locationOffset,
        out float clientTimestamp)
    {
        clientTimestamp = 0f;
        for (var accelerationBits = MinimumComponentBits;
             accelerationBits <= MaximumComponentBits;
             accelerationBits++)
        {
            var accelerationOffset = locationOffset - QuantizedVectorHeaderBits - accelerationBits * 3;
            var timestampOffset = accelerationOffset - FloatBits;
            if (timestampOffset < 0)
            {
                continue;
            }

            var header = (int)ReadBits(payload, accelerationOffset, QuantizedVectorHeaderBits);
            if ((header & 63) != accelerationBits)
            {
                continue;
            }

            var rawX = ReadSigned(payload, accelerationOffset + QuantizedVectorHeaderBits, accelerationBits);
            var rawY = ReadSigned(payload, accelerationOffset + QuantizedVectorHeaderBits + accelerationBits, accelerationBits);
            var rawZ = ReadSigned(payload, accelerationOffset + QuantizedVectorHeaderBits + accelerationBits * 2, accelerationBits);
            if (!UsesCanonicalBitCount(rawX, rawY, rawZ, accelerationBits))
            {
                continue;
            }

            var scale = (header & 64) != 0 ? 10d : 1d;
            if (Math.Abs(rawX / scale) > MaximumAcceleration
                || Math.Abs(rawY / scale) > MaximumAcceleration
                || Math.Abs(rawZ / scale) > MaximumAcceleration)
            {
                continue;
            }

            var timestampBits = (uint)ReadBits(payload, timestampOffset, FloatBits);
            var timestamp = BitConverter.Int32BitsToSingle((int)timestampBits);
            if (!float.IsFinite(timestamp)
                || timestamp < 0f
                || timestamp > MaximumClientTimestamp)
            {
                continue;
            }

            clientTimestamp = timestamp;
            return true;
        }

        return false;
    }

    private static bool TryReadCompressedRotation(
        ReadOnlySpan<byte> payload,
        int bitOffset,
        out double pitch,
        out double yaw,
        out double roll)
    {
        pitch = yaw = roll = 0d;
        return TryReadCompressedAxis(payload, ref bitOffset, out pitch)
            && TryReadCompressedAxis(payload, ref bitOffset, out yaw)
            && TryReadCompressedAxis(payload, ref bitOffset, out roll);
    }

    private static bool TryReadCompressedAxis(
        ReadOnlySpan<byte> payload,
        ref int bitOffset,
        out double degrees)
    {
        degrees = 0d;
        if (bitOffset >= payload.Length * 8)
        {
            return false;
        }

        var present = ReadBits(payload, bitOffset, 1) != 0;
        bitOffset++;
        if (!present)
        {
            return true;
        }

        if (bitOffset + 16 > payload.Length * 8)
        {
            return false;
        }

        var compressed = ReadBits(payload, bitOffset, 16);
        bitOffset += 16;
        degrees = compressed * 360d / 65_536d;
        return true;
    }

    private static bool IsPlausibleWorldLocation(double x, double y, double z) =>
        x is >= MinimumWorldX and <= MaximumWorldX
        && y is >= MinimumWorldY and <= MaximumWorldY
        && z is >= MinimumWorldZ and <= MaximumWorldZ;

    private static bool UsesCanonicalBitCount(long x, long y, long z, int componentBits) =>
        Math.Max(BitsNeeded(x), Math.Max(BitsNeeded(y), BitsNeeded(z))) == componentBits;

    private static int BitsNeeded(long value)
    {
        var massaged = (ulong)(value ^ (value >> 63));
        return 65 - BitOperations.LeadingZeroCount(massaged);
    }

    private static long ReadSigned(ReadOnlySpan<byte> payload, int bitOffset, int bitCount)
    {
        var value = ReadBits(payload, bitOffset, bitCount);
        var signBit = 1UL << (bitCount - 1);
        return (long)((value ^ signBit) - signBit);
    }

    private static ulong ReadBits(ReadOnlySpan<byte> payload, int bitOffset, int bitCount)
    {
        ulong value = 0;
        for (var bit = 0; bit < bitCount; bit++)
        {
            var sourceBit = bitOffset + bit;
            if ((payload[sourceBit >> 3] & (1 << (sourceBit & 7))) != 0)
            {
                value |= 1UL << bit;
            }
        }

        return value;
    }
}
