using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App.Tests;

public sealed class HomeProPresentationPolicyTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-30T08:00:00Z");

    [Fact]
    public void ActivePro_UsesPremiumMapCopyAndLocksVerifiedAccessControl()
    {
        var access = Snapshot(
            new ProEntitlement("pro", "active", Now.AddDays(3)),
            agentReady: true);

        var presentation = HomeProPresentationPolicy.Evaluate(access, Now);

        Assert.True(presentation.HasCurrentProAccess);
        Assert.True(presentation.IsVerified);
        Assert.False(presentation.ShowPromotion);
        Assert.Equal("MỞ MAP PRO", presentation.MapTitle);
        Assert.Equal("MỞ MAP PRO  →", presentation.MapAction);
    }

    [Fact]
    public void ActiveProWithoutAgent_KeepsPremiumIdentityAndCurrentAccessLock()
    {
        var access = Snapshot(
            new ProEntitlement("pro", "active", null),
            agentReady: false);

        var presentation = HomeProPresentationPolicy.Evaluate(access, Now);

        Assert.True(presentation.HasCurrentProAccess);
        Assert.False(presentation.IsVerified);
        Assert.False(presentation.ShowPromotion);
        Assert.Equal("MỞ MAP PRO", presentation.MapTitle);
    }

    [Fact]
    public void ExpiredPro_ReturnsToFreePresentation()
    {
        var access = Snapshot(
            new ProEntitlement("pro", "active", Now.AddSeconds(-1)),
            agentReady: true);

        var presentation = HomeProPresentationPolicy.Evaluate(access, Now);

        Assert.False(presentation.HasCurrentProAccess);
        Assert.False(presentation.IsVerified);
        Assert.True(presentation.ShowPromotion);
        Assert.Equal("MỞ LIVE MAP", presentation.MapTitle);
        Assert.Equal("MỞ MAP  →", presentation.MapAction);
    }

    [Fact]
    public void FreeAccount_ShowsProPromotion()
    {
        var access = Snapshot(
            new ProEntitlement("free", "active", null),
            agentReady: false);

        var presentation = HomeProPresentationPolicy.Evaluate(access, Now);

        Assert.True(presentation.ShowPromotion);
    }

    private static ProAccessSnapshot Snapshot(
        ProEntitlement entitlement,
        bool agentReady) => new(
        "76561199320727228",
        entitlement,
        IsOffline: false,
        AgentReady: agentReady,
        AgentVersion: agentReady ? "0.3.19" : null,
        OfflineLicenseExpiresAt: Now.AddDays(1),
        StatusCode: null);
}
