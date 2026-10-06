using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;
public sealed class ExperimentalVitalsDecoderTests
{
    [Fact]
    public void Pair_RejectsUnequalTinyFloats_AndAcceptsExplicitZero()
    {
        var bytes = new byte[9];
        var batch = new UnrealIrisReplicationBatch(1, 0, 66, true, false);
        bytes[4] = 1;
        Assert.True(ExperimentalVitalsDecoder.Pair(bytes, batch, 0, out var zero));
        Assert.Equal(0d, zero);
        bytes[0] = 1;
        Assert.False(ExperimentalVitalsDecoder.Pair(bytes, batch, 0, out _));
        Assert.False(ExperimentalVitalsDecoder.Pair(bytes[..4], batch, 0, out _));
    }

    [Fact]
    public void GrowthLayout_IsMeasuredNotSynthesized_AndDuplicateSequenceIsRejected()
    {
        var batchPayload = Convert.FromBase64String(
            "iAAAVFanbWPKFAAAcCAhpQuBhIi4uwCFF8NPf4ssCFQUWRCoKJ+AIVE+AUOig848RAedeYjqCGgQ1RHQIKojoEFUR0CD6NXAGdGrgTOiVwNnRK8GzoheDZwRvRo4I3o1cEb0auCM6GCxy8/BYpefyILARZEFgYsiCwIXRRYELjrozEN00JmH6KAzD9FBZx6iyILARZEFgYsiCwIVRRYEKoosCFQUWRCoKLIgcFFkQeCiyILARZEFgYsA");
        Assert.True(UnrealDinosaurVitalsTracker.TryDecodeReconnectAttributeFrame(
            batchPayload, new UnrealIrisReplicationBatch(384446, 0, 1482, true, false), out var decoded));
        Assert.Equal(0.6827013, decoded.Growth!.Value, 6);
        var decoder = new ExperimentalVitalsDecoder();
        var at = DateTimeOffset.UtcNow;
        decoder.Observe(Convert.FromBase64String(First), at, "A");
        Assert.NotEmpty(decoder.Observe(Convert.FromBase64String(Second), at.AddSeconds(1), "A"));
        Assert.Empty(decoder.Observe(Convert.FromBase64String(Second), at.AddSeconds(2), "A"));
    }
    private const string First = "HAAgp3Pk/////3+AAJklCAYAAABiIRcwCwgWykwBAAAHEjKHNlGIZMgFUAbH5dH3K8qj71eUR9+vKI++X9FEFZeiiSouRUCgAyAU5BqQELyADECoVJCArx7kSC8gAxAqFTmOwwdR0gvIAIRKRY7j8EFG8AIyAKFSQQK+ehASvIAMQKhUkICvHmRLLyADECp1j8KlB0nSC8gAhEpFjuPwQUDwAjIAoVJBAr56ECS9gAxAqFTkOA4f0jYgAxCqFljKoMg=";
    private const string Second = "HABkp4Lk////f5aAAEIniAcAAABiIRcwCwjWykwBAAAHErK9eVGIZMgFUAbHxXz3K4r57lcU892vKOa7X1GalpaiNC0tRUBgDCAU2EIAIMD5QXNvIAeEKi8QAxAqrrSzSIQg6QVkAEKlog10+CBKegEZgFCpaAMdPggJXkAGIFQq7FFYD6LrFrgBCGW7GUTWhREQvUAMQKi4mjLwEJKkF5ABCJWKNtDhg4TgBWQAQqXCHoX1IEd6ARmAUKloAx0+yDpeQAYgVGp5FMIPAoIXkAEIlQp7FNaDjOAFZABCpcIehfWQtgE8gFCSFAAAANCLGRQovZhBkQE=";
    [Fact]
    public void RawHealthLayout_PublishesCandidateAfterRepeatedObservation_WithoutInventedGrowth()
    {
        var decoder = new ExperimentalVitalsDecoder();
        var at = DateTimeOffset.Parse("2026-09-29T03:19:04.642159Z");
        Assert.Empty(decoder.Observe(Convert.FromBase64String(First), at, "flow-A"));
        var values = decoder.Observe(Convert.FromBase64String(Second), at.AddSeconds(0.8), "flow-A");
        var hp = Assert.Single(values, item => item.Field == "HealthCandidate");
        Assert.Equal(189484UL, hp.Owner);
        Assert.Equal(2770.8251953125d, hp.Value);
        Assert.Equal(495, hp.AbsoluteBitOffset);
        Assert.DoesNotContain(values, item => item.Field.Contains("Growth") || item.Field.Contains("Max"));
    }
    [Fact]
    public void FlowSwitchAndReset_DoNotBorrowCandidateAdmission()
    {
        var decoder = new ExperimentalVitalsDecoder();
        var at = DateTimeOffset.UtcNow;
        decoder.Observe(Convert.FromBase64String(First), at, "flow-A");
        Assert.Empty(decoder.Observe(Convert.FromBase64String(Second), at.AddSeconds(1), "flow-B"));
        decoder.Reset();
        Assert.Empty(decoder.Observe(Convert.FromBase64String(Second), at.AddSeconds(2), "flow-B"));
    }
    [Fact]
    public void CorruptDuplicateAndReorderedPacket_DoNotPublish()
    {
        var decoder = new ExperimentalVitalsDecoder();
        var at = DateTimeOffset.UtcNow;
        decoder.Observe(Convert.FromBase64String(First), at, "flow-A");
        Assert.Empty(decoder.Observe(Convert.FromBase64String(Second), at.AddSeconds(-1), "flow-A"));
        var corrupt = Convert.FromBase64String(Second);
        corrupt[(495 + 33 + 25) / 8] ^= (byte)(1 << ((495 + 33 + 25) % 8));
        Assert.Empty(decoder.Observe(corrupt, at.AddSeconds(1), "flow-A"));
    }
}

