namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Reads the UE 5.5 Iris replication envelope used by The Isle. The parser only
/// exposes object-batch boundaries; it deliberately does not mutate or inject
/// game traffic.
/// </summary>
public sealed class UnrealIrisPacketParser
{
    private const int MaximumObjectBatchCount = 8_192;

    public bool TryParse(ReadOnlySpan<byte> payload, out UnrealIrisPacket packet)
    {
        packet = default;
        if (payload.IsEmpty)
        {
            return false;
        }

        var packetEnd = FindLastOneBit(payload, payload.Length * 8);
        if (packetEnd < 7)
        {
            return false;
        }

        var reader = new BitReader(payload, packetEnd);
        if (!reader.TryReadSerializedInt(4, out _)
            || !reader.TryReadSerializedInt(8, out _)
            || !reader.TryReadBits(1, out var isHandshake)
            || isHandshake != 0
            || !reader.TrySkip(8))
        {
            return false;
        }

        // Unreal terminates the packet-handler section and the packet itself
        // with sentinel bits. Neither sentinel belongs to the bunch payload.
        var bunchesEnd = FindLastOneBit(payload, packetEnd);
        if (bunchesEnd <= reader.Position)
        {
            return false;
        }

        reader.SetLimit(bunchesEnd);
        if (!reader.TryReadBits(32, out var packetHeader))
        {
            return false;
        }

        var packetSequence = (int)((packetHeader >> 18) & 0x3fff);
        var acknowledgementWordCount = (int)(packetHeader & 0xf) + 1;
        if (!reader.TrySkip(acknowledgementWordCount * 32))
        {
            return false;
        }

        if (reader.Position < bunchesEnd)
        {
            if (!reader.TryReadBits(1, out var hasServerFrameTime))
            {
                return false;
            }

            if (hasServerFrameTime != 0
                && (!reader.TryReadSerializedInt(1_024, out _)
                    || !reader.TryReadBits(1, out var hasServerFrameTimeByte)
                    || hasServerFrameTimeByte != 0 && !reader.TrySkip(8)))
            {
                return false;
            }
        }

        var batches = new List<UnrealIrisReplicationBatch>();
        var complete = true;
        string? incompleteReason = null;
        var hasDataStream = false;
        while (bunchesEnd - reader.Position >= 10)
        {
            if (!TryReadBunchHeader(ref reader, out var bunch)
                || bunch.PayloadBitCount > bunchesEnd - reader.Position)
            {
                return false;
            }

            var bunchPayloadStart = reader.Position;
            if (bunch.ChannelIndex == 2)
            {
                hasDataStream = true;
                if (!TryParseDataStream(
                        payload,
                        bunchPayloadStart,
                        bunch.PayloadBitCount,
                        batches,
                        out var streamComplete,
                        out var streamReason))
                {
                    return false;
                }

                complete &= streamComplete;
                incompleteReason ??= streamReason;
            }

            if (!reader.TrySkip(bunch.PayloadBitCount))
            {
                return false;
            }
        }

        if (reader.Position != bunchesEnd)
        {
            return false;
        }

        packet = new UnrealIrisPacket(
            packetSequence,
            hasDataStream,
            complete,
            batches,
            incompleteReason);
        return true;
    }

    private static bool TryReadBunchHeader(ref BitReader reader, out BunchHeader bunch)
    {
        bunch = default;
        if (!reader.TryReadBits(1, out var isControl))
        {
            return false;
        }

        ulong isOpen = 0;
        ulong isClose = 0;
        if (isControl != 0
            && (!reader.TryReadBits(1, out isOpen)
                || !reader.TryReadBits(1, out isClose)))
        {
            return false;
        }

        if (isClose != 0 && !reader.TryReadSerializedInt(15, out _))
        {
            return false;
        }

        if (!reader.TrySkip(1)
            || !reader.TryReadBits(1, out var isReliable)
            || !reader.TryReadLegacyPackedUInt32(out var channelIndex)
            || !reader.TryReadBits(1, out _)
            || !reader.TryReadBits(1, out _)
            || !reader.TryReadBits(1, out var isPartial))
        {
            return false;
        }

        if (isReliable != 0 && !reader.TryReadSerializedInt(1_024, out _))
        {
            return false;
        }

        if (isPartial != 0 && !reader.TrySkip(3))
        {
            return false;
        }

        if ((isOpen != 0 || isReliable != 0)
            && (!reader.TryReadBits(1, out var hasChannelName)
                || hasChannelName != 0
                    ? !reader.TryReadLegacyPackedUInt32(out _)
                    : !reader.TrySkipFStringAndNumber()))
        {
            return false;
        }

        if (!reader.TryReadSerializedInt(8_192, out var payloadBitCount))
        {
            return false;
        }

        bunch = new BunchHeader((int)channelIndex, checked((int)payloadBitCount));
        return true;
    }

    private static bool TryParseDataStream(
        ReadOnlySpan<byte> payload,
        int payloadStart,
        int payloadBitCount,
        ICollection<UnrealIrisReplicationBatch> batches,
        out bool complete,
        out string? incompleteReason)
    {
        complete = true;
        incompleteReason = null;
        var payloadEnd = payloadStart + payloadBitCount;
        var reader = new BitReader(payload, payloadEnd, payloadStart);
        if (!reader.TryReadBits(5, out var streamCountMinusOne)
            || !reader.TryReadBits(5, out var streamMask))
        {
            return false;
        }

        var streamCount = (int)streamCountMinusOne + 1;
        if (streamCount is < 1 or > 32
            || streamMask == 0
            || streamMask >> streamCount != 0)
        {
            return false;
        }

        // Bit 0 is NetTokenDataStream; bit 1 is ReplicationDataStream.
        if ((streamMask & 2) == 0)
        {
            return true;
        }

        if ((streamMask & 1) != 0 && !TrySkipNetTokenDataStream(ref reader))
        {
            complete = false;
            incompleteReason = "unsupported-net-token-stream";
            return true;
        }

        if (!reader.TryReadBits(16, out var batchCountValue)
            || !reader.TryReadBits(16, out var destructionCountValue))
        {
            return false;
        }

        var batchCount = checked((int)batchCountValue);
        var destructionCount = checked((int)destructionCountValue);
        if (batchCount >= MaximumObjectBatchCount || destructionCount > batchCount)
        {
            return false;
        }

        for (var index = 0; index < destructionCount; index++)
        {
            if (!reader.TryReadIrisPackedUInt64(out _)
                || !reader.TryReadBits(1, out var hasDestructionInfo)
                || hasDestructionInfo != 0 && !reader.TryReadIrisPackedUInt64(out _)
                || !reader.TrySkip(1))
            {
                return false;
            }
        }

        for (var index = destructionCount; index < batchCount; index++)
        {
            if (!reader.TryReadBits(1, out var isInlineDestruction))
            {
                return false;
            }

            if (isInlineDestruction != 0)
            {
                complete = false;
                incompleteReason = "inline-destruction";
                return true;
            }

            if (!reader.TryReadIrisPackedUInt64(out var netRefHandle)
                || !reader.TryReadBits(16, out var batchBitCountValue))
            {
                return false;
            }

            var batchBitCount = checked((int)batchBitCountValue);
            var batchStart = reader.Position;
            var batchEnd = batchStart + batchBitCount;
            if (batchBitCount < 2 || batchEnd > payloadEnd)
            {
                return false;
            }

            var hasOwnerData = IsBitSet(payload, batchStart);
            var hasExports = IsBitSet(payload, batchStart + 1);
            batches.Add(new UnrealIrisReplicationBatch(
                netRefHandle,
                batchStart + 2,
                batchBitCount - 2,
                hasOwnerData,
                hasExports));
            reader.Seek(batchEnd);

            if (hasExports && !TrySkipExports(ref reader))
            {
                complete = false;
                incompleteReason = "unsupported-object-exports";
                return true;
            }
        }

        complete = reader.Position == payloadEnd;
        if (!complete)
        {
            incompleteReason = "trailing-data-stream-bits";
        }

        return true;
    }

    private static bool TrySkipNetTokenDataStream(ref BitReader reader)
    {
        for (var index = 0; index < MaximumObjectBatchCount; index++)
        {
            if (!reader.TryReadBits(1, out var hasToken))
            {
                return false;
            }

            if (hasToken == 0)
            {
                return true;
            }

            if (!reader.TryReadIrisPackedUInt32(out var token)
                || token == 0
                || !reader.TryReadBits(1, out _)
                || !reader.TryReadBits(3, out _)
                || !reader.TryReadBits(1, out _)
                || !reader.TryReadBits(16, out var byteCount)
                || !reader.TrySkip(checked((int)byteCount * 8)))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TrySkipExports(ref BitReader reader)
    {
        for (var index = 0; index < MaximumObjectBatchCount; index++)
        {
            if (!reader.TryReadBits(1, out var hasNameExport))
            {
                return false;
            }

            if (hasNameExport == 0)
            {
                break;
            }

            if (!TrySkipNetToken(ref reader, true) || !TrySkipNetString(ref reader))
            {
                return false;
            }
        }

        for (var index = 0; index < MaximumObjectBatchCount; index++)
        {
            if (!reader.TryReadBits(1, out var hasObjectExport))
            {
                return false;
            }

            if (hasObjectExport == 0)
            {
                break;
            }

            if (!TrySkipInlineFullObjectReference(ref reader, 0))
            {
                return false;
            }
        }

        for (var index = 0; index < MaximumObjectBatchCount; index++)
        {
            if (!reader.TryReadBits(1, out var hasExportedHandle))
            {
                return false;
            }

            if (hasExportedHandle == 0)
            {
                return true;
            }

            if (!reader.TryReadBits(1, out var hasHandle)
                || hasHandle == 0
                || !reader.TryReadIrisPackedUInt64(out var handle)
                || handle == 0)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TrySkipInlineFullObjectReference(ref BitReader reader, int depth)
    {
        if (depth > 32 || !reader.TryReadBits(1, out var isExported))
        {
            return false;
        }

        if (isExported != 0)
        {
            return TrySkipNetRefHandle(ref reader)
                   && TrySkipNetToken(ref reader, false)
                   && TrySkipConditionalNetString(ref reader);
        }

        return TrySkipInlineObjectReferenceBody(ref reader, depth);
    }

    private static bool TrySkipInlineObjectReferenceBody(ref BitReader reader, int depth)
    {
        if (depth > 32 || !reader.TryReadBits(1, out var hasReference))
        {
            return false;
        }

        if (hasReference == 0)
        {
            return true;
        }

        if (!reader.TryReadIrisPackedUInt64(out var handle)
            || handle == 0
            || !reader.TryReadBits(1, out var hasPath))
        {
            return false;
        }

        if (hasPath == 0)
        {
            return true;
        }

        if (!reader.TrySkip(1) || !reader.TryReadBits(1, out var hasOuter))
        {
            return false;
        }

        return hasOuter == 0
               || TrySkipNetToken(ref reader, false)
               && TrySkipConditionalNetString(ref reader)
               && TrySkipInlineObjectReferenceBody(ref reader, depth + 1);
    }

    private static bool TrySkipNetRefHandle(ref BitReader reader) =>
        reader.TryReadBits(1, out var hasHandle)
        && (hasHandle == 0
            || reader.TryReadIrisPackedUInt64(out var handle) && handle != 0);

    private static bool TrySkipNetToken(ref BitReader reader, bool includesTypeId)
    {
        if (!reader.TryReadIrisPackedUInt32(out var token))
        {
            return false;
        }

        return token == 0
               || reader.TrySkip(1) && (!includesTypeId || reader.TrySkip(3));
    }

    private static bool TrySkipConditionalNetString(ref BitReader reader) =>
        reader.TryReadBits(1, out var hasString)
        && (hasString == 0 || TrySkipNetString(ref reader));

    private static bool TrySkipNetString(ref BitReader reader) =>
        reader.TrySkip(1)
        && reader.TryReadBits(16, out var byteCount)
        && reader.TrySkip(checked((int)byteCount * 8));

    private static int FindLastOneBit(ReadOnlySpan<byte> payload, int before)
    {
        for (var bit = Math.Min(before, payload.Length * 8) - 1; bit >= 0; bit--)
        {
            if (IsBitSet(payload, bit))
            {
                return bit;
            }
        }

        return -1;
    }

    private static bool IsBitSet(ReadOnlySpan<byte> payload, int bit) =>
        (payload[bit >> 3] & 1 << (bit & 7)) != 0;

    private readonly record struct BunchHeader(int ChannelIndex, int PayloadBitCount);

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _payload;
        private int _limit;

        public BitReader(ReadOnlySpan<byte> payload, int limit, int position = 0)
        {
            _payload = payload;
            _limit = limit;
            Position = position;
        }

        public int Position { get; private set; }

        public void SetLimit(int value) => _limit = value;

        public void Seek(int value) => Position = value;

        public bool TrySkip(int bitCount)
        {
            if (bitCount < 0 || Position > _limit - bitCount)
            {
                return false;
            }

            Position += bitCount;
            return true;
        }

        public bool TryReadBits(int bitCount, out ulong value)
        {
            value = 0;
            if (bitCount is < 0 or > 64 || Position > _limit - bitCount)
            {
                return false;
            }

            for (var offset = 0; offset < bitCount; offset++)
            {
                var bit = Position + offset;
                if ((_payload[bit >> 3] & 1 << (bit & 7)) != 0)
                {
                    value |= 1UL << offset;
                }
            }

            Position += bitCount;
            return true;
        }

        public bool TryReadSerializedInt(int maximum, out ulong value)
        {
            value = 0;
            uint mask = 1;
            while (value + mask < (ulong)maximum)
            {
                if (!TryReadBits(1, out var set))
                {
                    return false;
                }

                if (set != 0)
                {
                    value |= mask;
                }

                mask <<= 1;
            }

            return true;
        }

        public bool TryReadLegacyPackedUInt32(out ulong value)
        {
            value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                if (!TryReadBits(8, out var current))
                {
                    return false;
                }

                value |= current >> 1 << shift;
                if ((current & 1) == 0)
                {
                    return value <= uint.MaxValue;
                }
            }

            return false;
        }

        public bool TryReadIrisPackedUInt64(out ulong value)
        {
            value = 0;
            return TryReadBits(3, out var byteCountMinusOne)
                   && TryReadBits(checked(((int)byteCountMinusOne + 1) * 8), out value);
        }

        public bool TryReadIrisPackedUInt32(out ulong value)
        {
            value = 0;
            return TryReadBits(2, out var byteCountMinusOne)
                   && TryReadBits(checked(((int)byteCountMinusOne + 1) * 8), out value);
        }

        public bool TrySkipFStringAndNumber()
        {
            if (!TryReadBits(32, out var rawLength))
            {
                return false;
            }

            var length = unchecked((int)(uint)rawLength);
            var bitCount = Math.Abs((long)length) * (length < 0 ? 16 : 8);
            return bitCount <= int.MaxValue
                   && TrySkip((int)bitCount)
                   && TrySkip(32);
        }
    }
}
