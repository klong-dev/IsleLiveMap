using System.Net.NetworkInformation;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class NpcapLocalMovementSourceTests
{
    [Theory]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Wireless80211, true)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Tunnel, true)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Loopback, false)]
    [InlineData(OperationalStatus.Down, NetworkInterfaceType.Tunnel, false)]
    public void CaptureNetwork_IncludesActiveTunnelInterfaces(
        OperationalStatus status,
        NetworkInterfaceType type,
        bool expected)
    {
        Assert.Equal(
            expected,
            NpcapLocalMovementSource.IsEligibleCaptureNetwork(status, type));
    }

    [Fact]
    public void CaptureFilter_IncludesOnlyOutboundGameTraffic()
    {
        var filter = NpcapLocalMovementSource.BuildCaptureFilter([7778, 7777]);

        Assert.Equal("udp and (src port 7777 or src port 7778)", filter);
        Assert.DoesNotContain("dst port", filter, StringComparison.Ordinal);
    }
}
