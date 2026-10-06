using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;

public sealed class InboundVitalsDeltaStateTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-29T07:00:00Z");
    private static VitalsFieldEvidence Field(string field, double value, int seconds = 0, ulong owner = 7, string flow = "session")
        => new(flow, owner, field + "Candidate", value, At.AddSeconds(seconds), seconds, 1, 356, "fixture");

    [Fact]
    public void MissingField_IsRetained_MaxOnlyUpdateDoesNotRefreshCurrent()
    {
        var state = new InboundVitalsDeltaState();
        state.Apply("session", 7, [Field("Health", 50), Field("MaxHealth", 100)]);
        var result = state.Apply("session", 7, [Field("MaxHealth", 120, 100)]);
        var fresh = InboundStatsAccumulator.FreshVitals(result, At.AddSeconds(100));
        var retained = InboundStatsAccumulator.LastKnownVitals(result, At.AddSeconds(100));
        Assert.Null(fresh.Health);
        Assert.Equal(At, Assert.Single(result, x => x.Field == "HealthCandidate").ObservedAt);
        var display = InboundVitalsDisplay.Resolve(fresh, retained)!;
        Assert.Equal(50d, display.Health); Assert.Equal(120d, display.MaxHealth);
        Assert.Equal(50d / 120d * 100, VitalMath.Percent(display.Health, display.MaxHealth), 5);
        Assert.Equal(120d, InboundStatsAccumulator.LastKnownVitals(state.Apply("session", 7, []), At.AddHours(1)).MaxHealth);
    }
    [Fact]
    public void CurrentOnlyAndZero_AreUpdates_NotMissingValues()
    {
        var state = new InboundVitalsDeltaState();
        state.Apply("session", 7, [Field("MaxStamina", 400)]);
        var result = state.Apply("session", 7, [Field("Stamina", 0, 1)]);
        var display = InboundStatsAccumulator.LastKnownVitals(result, At.AddSeconds(1));
        Assert.Equal(0d, display.Stamina); Assert.Equal(400d, display.MaxStamina);
        state.Apply("session", 7, [Field("Stamina", 99)]);
        Assert.Equal(0d, InboundStatsAccumulator.LastKnownVitals(state.Apply("session", 7, []), At.AddSeconds(2)).Stamina);
    }
    [Fact]
    public void ColdStartAndScopeChange_DoNotInventMaxOrBorrowPreviousDino()
    {
        var state = new InboundVitalsDeltaState();
        var cold = state.Apply("session", 7, [Field("Hunger", 10)]);
        Assert.Null(InboundStatsAccumulator.LastKnownVitals(cold, At).MaxHunger);
        state.Apply("session", 7, [Field("MaxHunger", 100)]);
        Assert.Empty(state.Apply("session", 8, []));
        Assert.Empty(state.Apply("other", 7, []));
        state.Clear(); Assert.Null(state.Owner);
    }
    [Fact]
    public void WaterMaximum_IsProtocolConstant_NotFakePacketEvidence()
    {
        VitalsFieldEvidence[] fields = [Field("Thirst", 840)];
        var vitals = InboundStatsAccumulator.LastKnownVitals(fields, At.AddMinutes(3));
        Assert.Equal(1000d, vitals.MaxThirst);
        Assert.Equal(84d, VitalMath.Percent(vitals.Thirst, vitals.MaxThirst));
        Assert.Single(fields); Assert.Equal(At, fields[0].ObservedAt);
        Assert.Null(InboundStatsAccumulator.LastKnownVitals([], At).Thirst);
    }
}
