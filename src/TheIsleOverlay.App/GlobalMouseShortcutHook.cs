using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TheIsleOverlay.App;

public sealed class GlobalMouseShortcutHook : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMouseWheel = 0x020A;
    private const int VirtualKeyAlt = 0x12;

    private readonly Dispatcher _dispatcher;
    private readonly LowLevelMouseProcedure _procedure;
    private IntPtr _hook;

    public GlobalMouseShortcutHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _procedure = HookCallback;
    }

    public event Action? ZoomInRequested;
    public event Action? ZoomOutRequested;
    public event Action? ToggleMapRequested;

    public bool Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(
            WhMouseLowLevel,
            _procedure,
            GetModuleHandle(module?.ModuleName),
            0);
        return _hook != IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (GetAsyncKeyState(VirtualKeyAlt) & 0x8000) != 0)
        {
            if (message.ToInt32() == WmMouseWheel)
            {
                var details = Marshal.PtrToStructure<LowLevelMouseDetails>(data);
                var delta = unchecked((short)(details.MouseData >> 16));
                _dispatcher.BeginInvoke(delta > 0
                    ? () => ZoomInRequested?.Invoke()
                    : () => ZoomOutRequested?.Invoke());
                return (IntPtr)1;
            }

            if (message.ToInt32() == WmMiddleButtonDown)
            {
                _dispatcher.BeginInvoke(() => ToggleMapRequested?.Invoke());
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private delegate IntPtr LowLevelMouseProcedure(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LowLevelMouseDetails
    {
        public readonly Point Point;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        public readonly int X;
        public readonly int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelMouseProcedure callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
