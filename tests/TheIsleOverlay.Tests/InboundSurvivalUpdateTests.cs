using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;
public sealed class InboundSurvivalUpdateTests
{
    [Fact]
    public void Live395Batch_ReadsSurvivalAndStaminaBelow100Hunger_WithoutAssumingWaterMaximum()
    {
        var decoder = new ExperimentalVitalsDecoder();
        var at = DateTimeOffset.Parse("2026-09-29T06:19:08.8207349Z");
        decoder.Observe(Convert.FromBase64String("AACIrQfR////f82BABYlCAMAAABCNiBoDAgFIADq5uOXqYqcDQhQqELAHIwpQjkYU4S6GeYRdTPMI8rBmCKUgzFF6LWos9BrUWchINABEApyGkhKf6AWIKQACNAOxpLfQ1j6AyjxAKDFRAFAi4kQzfdALUBIARAgHhIRoYd0vgdQ4gEAs4kCAGYTIUYBAhmAUKl+6xE/yPB+wBUgFAAEaA8s2IIY7wdQ4gHAu4kCgHcTGQ=="), at, "flow");
        var fields = decoder.Observe(Convert.FromBase64String("AADorSLR/////8aBABwiCAEAAABCNiBoDAgFIADqyseXqYqcDQhQqELA9oUpQu0LU4QiAOYRRQDMI2pfmCLUvjBFqNaZs1CtM2chINABEApyGQY="), at.AddSeconds(.8), "flow");
        Assert.Equal(42.380821228027344, Assert.Single(fields, e => e.Field == "HungerCandidate").Value);
        Assert.Equal(998.00048828125, Assert.Single(fields, e => e.Field == "ThirstCandidate").Value);
        Assert.Equal(103.20185852050781, Assert.Single(fields, e => e.Field == "StaminaCandidate").Value);
        Assert.All(fields, e => Assert.Equal(263880UL, e.Owner));
        Assert.DoesNotContain(fields, e => e.Field == "MaxThirstCandidate" || e.Field == "GrowthCandidate");
    }
    [Fact]
    public void BriefUpdateHole_ShowsOnlyHistoricalData_ThenClearsAfterPresenceWindow()
    {
        var at = DateTimeOffset.UtcNow;
        var local = LocalMovementObservation.VitalsOnly(new LocalDinosaurVitalsObservation(at, new TheIsleOverlay.Core.ExactVitals(), 7)
        { ExperimentalEvidence = [new("flow", 7, "HealthCandidate", 200, at, 1, 20, 356, "tail")] });
        var displayed = LocalPositionSnapshotMerger.Merge(null, local, at.AddSeconds(4), allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Null(displayed.Player?.ExactVitals);
        Assert.Null(displayed.Player?.HealthPercent);
        Assert.Equal(200d, displayed.Player?.InboundStatsLastKnown?.Health);
        Assert.Equal(at, displayed.Player!.InboundStatsFieldTimes!["Health"]);
        var expired = LocalPositionSnapshotMerger.Merge(null, local, at.AddSeconds(16), allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Null(expired.Player?.InboundStatsLastKnown);
    }
    [Fact]
    public void HistoricalValue_DoesNotBecomeFreshAfterOtherFieldsUpdate()
    {
        var now = DateTimeOffset.UtcNow;
        VitalsFieldEvidence[] fields = [new("flow", 1, "GrowthCandidate", .5, now.AddSeconds(-20), 1, 20, 1391, "full"),
            new("flow", 1, "ThirstCandidate", 840, now, 2, 40, 395, "survival")];
        Assert.Null(InboundStatsAccumulator.FreshVitals(fields, now).Growth);
        Assert.Equal(.5, InboundStatsAccumulator.LastKnownVitals(fields, now).Growth);
        Assert.Equal(now.AddSeconds(-20), fields[0].ObservedAt);
    }
}

