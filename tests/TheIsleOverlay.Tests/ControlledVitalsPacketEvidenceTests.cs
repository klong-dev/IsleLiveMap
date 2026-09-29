using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class ControlledVitalsPacketEvidenceTests
{
    // Session controlled-vitals-20260929-100520, inbound SHA-256:
    // 0ADE2E3AF3FC72E93C6DA07DECE7091E1CDB02D9D68B8A7C9EA2AC4761E6DA33.
    // These fixtures prove serialization/batch provenance only, not local ownership,
    // species, a maximum of 1000, or the semantic meaning of the candidate field.
    [Theory]
    [InlineData("HAAA803+////f4yAALAkCAMAAABiIReoDggBAAIyRNeRTAEAAAcSspj7YYhkyAVQqELAuOSMRXHJGYtSDDUGpRhqDIpLzlgUl5yxKHmVD1HyKh8iIDAGEApsIQCtIZNAkTcQA3JPFzAFGCVQCshWIIgji8VCCiqydIEoXA89QPDnAjIAoVJTZqZ9kBK6QAxAqHgQvRsRaRuQAggVCQAAADysqglk", 15949, 467, 401, 761.342041015625d)]
    [InlineData("HAA0BcbB////f4eAAFYjCAIAAABiIRdoDQi2xkwBAAAHEvK43GqIZMgFUKhCwPpqi0X11RaLmpP+BDUn/Qmqr7ZYVF9tsSgAgB5RAAA9IiAwBhAKbCEAvrKmsJU3UATyXBeQAQiV6ihcx0PaBqQAQkUCAAAAPqRsAhk=", 454, 427, 361, 1000d)]
    public void Capture_TrailingPairRetainsOwnerAndBitProvenance(
        string base64, int sequence, int bitCount, int relativeOffset, double expected)
    {
        var payload = Convert.FromBase64String(base64);
        Assert.True(new UnrealIrisPacketParser().TryParse(payload, out var packet));
        Assert.True(packet.IsComplete);
        Assert.Equal(sequence, (int)packet.PacketSequence);
        var batch = Assert.Single(packet.Batches, item => item.NetRefHandle == 189484);
        Assert.True(batch.HasOwnerData);
        Assert.Equal(205, batch.DataBitOffset);
        Assert.Equal(bitCount, batch.DataBitCount);
        Assert.Equal(bitCount, relativeOffset + 66);
        var absolute = batch.DataBitOffset + relativeOffset;
        Assert.True(absolute + 66 <= payload.Length * 8);
        Assert.Equal(expected, ReadFloat(payload, absolute));
        Assert.True((payload[(absolute + 32) / 8] & (1 << ((absolute + 32) % 8))) != 0);
        Assert.Equal(expected, ReadFloat(payload, absolute + 33));
    }

    private static double ReadFloat(byte[] payload, int offset)
    {
        uint bits = 0;
        for (var bit = 0; bit < 32; bit++)
            bits |= (uint)((payload[(offset + bit) / 8] >> ((offset + bit) % 8)) & 1) << bit;
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }
}
