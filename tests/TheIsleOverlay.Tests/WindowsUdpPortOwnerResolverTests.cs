using System.Net;
using System.Net.Sockets;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class WindowsUdpPortOwnerResolverTests
{
    [Fact]
    public void GetOwnedPorts_FindsUdpSocketOwnedByCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var socket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Dgram,
            ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;

        var ports = new WindowsUdpPortOwnerResolver().GetOwnedPorts(Environment.ProcessId);

        Assert.Contains(port, ports);
    }
}
