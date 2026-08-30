using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class ProEntitlementTests
{
    [Theory]
    [InlineData("pro", "active", 1, true)]
    [InlineData("pro", "active", 0, false)]
    [InlineData("pro", "active", -1, false)]
    [InlineData("pro", "expired", 1, false)]
    [InlineData("free", "active", 1, false)]
    public void IsProAt_RequiresActiveUnexpiredEntitlement(
        string tier,
        string status,
        int expiryOffsetMinutes,
        bool expected)
    {
        var now = DateTimeOffset.Parse("2026-08-30T08:00:00Z");
        var entitlement = new ProEntitlement(
            tier,
            status,
            now.AddMinutes(expiryOffsetMinutes));

        Assert.Equal(expected, entitlement.IsProAt(now));
    }

    [Fact]
    public void IsProAt_AcceptsPermanentActiveEntitlement()
    {
        var entitlement = new ProEntitlement("pro", "active", null);

        Assert.True(entitlement.IsProAt(DateTimeOffset.MaxValue));
    }
}
