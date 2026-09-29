using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;
public sealed class InboundMaxHungerRegressionTests
{
    [Fact]
    public void Raw1482Frame_PublishesMaxHungerIntoDeltaState()
    {
        var bytes=Convert.FromBase64String("AAA4SKLQ//////GAAKgqiAMAAABCNiBgLggBAIDKLuwtxJkCAAAOJOSzb7QQORsQoPBi+Olv3V/Birq/ghVFllkkiiyzSFRvDYqo3hoUUfnfGaLyvzNE5X9niMr/zhC1fzYkav9sSNT+2ZCo/bMhUftnQ6L2z4ZE7Z8Nido/GxINc0b6GeaM9NP9Fbyo+yt4UfdX8KLur+BF/Uach/qNOA/VW4MiqrcGRdT9Fbyo+yt4UfdXsKLur2BF3V/Birq/ghV1fwUv6v4KXtT9Fbyo+yt4ERAYAwgFthBA1H482vgcSALBhg+AAoQqgg5iCziHt6XIl7eiyGDRAza2EGQmQSdAqCLoILQY72iR6PrNAV3C2Pc3yiC1+gFQgFBF0EFsAUH7plS0csn3GOx3X9kPQkEkcAUIAQAB2kunDSAXRAKUeAAgTqIAQJxEiDSTABQgVBF0EFvA8MMtWdyc41EGGyIYn2I=");
        var at=new DateTimeOffset(639262658762885400L, TimeSpan.Zero);
        var accumulator=new InboundStatsAccumulator();
        Assert.True(accumulator.TryTrack(bytes,at,"session",out var observation));
        Assert.Equal(263880UL,observation.NetRefHandle);
        Assert.True(observation.Vitals.MaxHunger > 0);
        var field=Assert.Single(observation.ExperimentalEvidence!, e=>e.Field=="MaxHungerCandidate");
        Assert.Equal("measured-1482", field.Layout);
        Assert.Equal(228,field.AbsoluteBitOffset - 205);
        Assert.Equal(observation.Vitals.MaxHealth / 2, observation.Vitals.MaxHunger);
    }
}

