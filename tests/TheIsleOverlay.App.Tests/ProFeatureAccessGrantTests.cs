using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App.Tests;

public sealed class ProFeatureAccessGrantTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-09-02T01:00:00Z");

    [Fact]
    public void ActiveTimedAndLifetimeEntitlementsEnableProMapFeatures()
    {
        Assert.True(Grant(new ProEntitlement("pro", "active", Now.AddMinutes(1))).IsActiveAt(Now));
        Assert.True(Grant(new ProEntitlement("pro", "active", null)).IsActiveAt(Now));
    }

    [Fact]
    public void ExpiredOrFreeEntitlementsCannotKeepLocalProFeaturesOpen()
    {
        Assert.False(Grant(new ProEntitlement("pro", "active", Now)).IsActiveAt(Now));
        Assert.False(Grant(new ProEntitlement("free", "not_entitled", null)).IsActiveAt(Now));
    }

    private static ProFeatureAccessGrant Grant(ProEntitlement entitlement) =>
        ProFeatureAccessGrant.FromSnapshot(new ProAccessSnapshot(
            "76561199320727228",
            entitlement,
            IsOffline: false,
            AgentReady: true,
            AgentVersion: "test",
            OfflineLicenseExpiresAt: Now.AddHours(1),
            StatusCode: null), Now);
}
