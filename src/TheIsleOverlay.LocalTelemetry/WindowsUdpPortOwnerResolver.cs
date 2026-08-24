using System.Net;
using System.Runtime.InteropServices;

namespace TheIsleOverlay.LocalTelemetry;

public sealed class WindowsUdpPortOwnerResolver
{
    private const int AddressFamilyInet = 2;
    private const int ErrorInsufficientBuffer = 122;

    public IReadOnlySet<int> GetOwnedPorts(int processId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new HashSet<int>();
        }

        var size = 0;
        var result = GetExtendedUdpTable(
            IntPtr.Zero,
            ref size,
            true,
            AddressFamilyInet,
            UdpTableClass.OwnerPid,
            0);
        if (result != ErrorInsufficientBuffer || size <= sizeof(int))
        {
            return new HashSet<int>();
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedUdpTable(
                buffer,
                ref size,
                true,
                AddressFamilyInet,
                UdpTableClass.OwnerPid,
                0);
            if (result != 0)
            {
                return new HashSet<int>();
            }

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<UdpRowOwnerPid>();
            var rowPointer = IntPtr.Add(buffer, sizeof(int));
            var ports = new HashSet<int>();
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<UdpRowOwnerPid>(
                    IntPtr.Add(rowPointer, index * rowSize));
                if (row.OwningPid != processId)
                {
                    continue;
                }

                var networkPort = (short)(row.LocalPort & 0xffff);
                var port = (ushort)IPAddress.NetworkToHostOrder(networkPort);
                if (port > 0)
                {
                    ports.Add(port);
                }
            }

            return ports;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr udpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        UdpTableClass tableClass,
        uint reserved);

    private enum UdpTableClass
    {
        Basic,
        OwnerPid,
        OwnerModule
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct UdpRowOwnerPid
    {
        public readonly uint LocalAddress;
        public readonly uint LocalPort;
        public readonly int OwningPid;
    }
}
