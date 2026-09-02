using System.Runtime.InteropServices;

namespace TheIsleOverlay.App;

internal readonly record struct RawMouseDelta(int X, int Y);

internal static class RawMouseInput
{
    private const uint GenericDesktopUsagePage = 0x01;
    private const uint MouseUsage = 0x02;
    private const uint RidevRemove = 0x00000001;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const ushort MouseMoveAbsolute = 0x0001;

    public static bool TryRegister(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = (ushort)GenericDesktopUsagePage,
                Usage = (ushort)MouseUsage,
                Flags = RidevInputSink,
                Target = windowHandle
            }
        };
        return RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputDevice>());
    }

    public static bool TryUnregister()
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = (ushort)GenericDesktopUsagePage,
                Usage = (ushort)MouseUsage,
                Flags = RidevRemove,
                Target = IntPtr.Zero
            }
        };
        return RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputDevice>());
    }

    public static bool TryReadDelta(IntPtr rawInputHandle, out RawMouseDelta delta)
    {
        delta = default;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint dataSize = 0;
        if (GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref dataSize, headerSize) == uint.MaxValue
            || dataSize < headerSize + Marshal.SizeOf<RawMouse>())
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)dataSize));
        try
        {
            var copied = GetRawInputData(rawInputHandle, RidInput, buffer, ref dataSize, headerSize);
            if (copied == uint.MaxValue || copied < headerSize)
            {
                return false;
            }

            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != RimTypeMouse)
            {
                return false;
            }

            var mouse = Marshal.PtrToStructure<RawMouse>(IntPtr.Add(buffer, checked((int)headerSize)));
            if ((mouse.Flags & MouseMoveAbsolute) != 0 || (mouse.LastX == 0 && mouse.LastY == 0))
            {
                return false;
            }

            delta = new RawMouseDelta(mouse.LastX, mouse.LastY);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawInputHeader
    {
        public readonly uint Type;
        public readonly uint Size;
        public readonly IntPtr Device;
        public readonly IntPtr WParam;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private readonly struct RawMouse
    {
        [FieldOffset(0)] public readonly ushort Flags;
        [FieldOffset(4)] public readonly uint Buttons;
        [FieldOffset(8)] public readonly uint RawButtons;
        [FieldOffset(12)] public readonly int LastX;
        [FieldOffset(16)] public readonly int LastY;
        [FieldOffset(20)] public readonly uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint deviceSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint dataSize,
        uint headerSize);
}
