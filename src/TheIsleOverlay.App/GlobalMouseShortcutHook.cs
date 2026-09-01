using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TheIsleOverlay.App;

public sealed class GlobalMouseShortcutHook : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMouseWheel = 0x020A;
    private const int VirtualKeyAlt = 0x12;

    private readonly Dispatcher _dispatcher;
    private readonly LowLevelMouseProcedure _procedure;
    private IntPtr _hook;
    private bool _mapPanActive;
    private long _latestMapPanPoint;
    private int _mapPanMoveQueued;

    public GlobalMouseShortcutHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _procedure = HookCallback;
    }

    public event Action? ZoomInRequested;
    public event Action? ZoomOutRequested;
    public event Action? ToggleMapRequested;
    public event Action<GlobalMousePoint>? MapPanStarted;
    public event Action<GlobalMousePoint>? MapPanMoved;
    public event Action<GlobalMousePoint>? MapPanEnded;
    public event Action? FollowMapRequested;

    public Func<GlobalMousePoint, bool>? CanStartMapPan { get; set; }

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
        _mapPanActive = false;
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
        if (code < 0)
        {
            return CallNextHookEx(_hook, code, message, data);
        }

        var mouseMessage = message.ToInt32();
        if (_mapPanActive && mouseMessage is WmMouseMove or WmLeftButtonUp)
        {
            var point = ReadPoint(data);
            QueueMapPanMove(point);
            if (mouseMessage == WmLeftButtonUp)
            {
                _mapPanActive = false;
                _dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    () => MapPanEnded?.Invoke(point));
            }

            return (IntPtr)1;
        }

        if ((GetAsyncKeyState(VirtualKeyAlt) & 0x8000) != 0)
        {
            if (mouseMessage == WmLeftButtonDown)
            {
                var point = ReadPoint(data);
                if (ShouldStartMapPan(point))
                {
                    _mapPanActive = true;
                    _dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        () => MapPanStarted?.Invoke(point));
                    return (IntPtr)1;
                }
            }

            if (mouseMessage == WmRightButtonDown && ShouldStartMapPan(ReadPoint(data)))
            {
                _dispatcher.BeginInvoke(() => FollowMapRequested?.Invoke());
                return (IntPtr)1;
            }

            if (mouseMessage == WmMouseWheel)
            {
                var details = Marshal.PtrToStructure<LowLevelMouseDetails>(data);
                var delta = unchecked((short)(details.MouseData >> 16));
                _dispatcher.BeginInvoke(delta > 0
                    ? () => ZoomInRequested?.Invoke()
                    : () => ZoomOutRequested?.Invoke());
                return (IntPtr)1;
            }

            if (mouseMessage == WmMiddleButtonDown)
            {
                _dispatcher.BeginInvoke(() => ToggleMapRequested?.Invoke());
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private bool ShouldStartMapPan(GlobalMousePoint point)
    {
        try
        {
            return CanStartMapPan?.Invoke(point) == true;
        }
        catch
        {
            return false;
        }
    }

    private void QueueMapPanMove(GlobalMousePoint point)
    {
        Interlocked.Exchange(ref _latestMapPanPoint, Pack(point));
        if (Interlocked.Exchange(ref _mapPanMoveQueued, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                var latest = Unpack(Interlocked.Read(ref _latestMapPanPoint));
                Interlocked.Exchange(ref _mapPanMoveQueued, 0);
                MapPanMoved?.Invoke(latest);
            });
    }

    private static GlobalMousePoint ReadPoint(IntPtr data)
    {
        var details = Marshal.PtrToStructure<LowLevelMouseDetails>(data);
        return new GlobalMousePoint(details.Point.X, details.Point.Y);
    }

    private static long Pack(GlobalMousePoint point) =>
        ((long)(uint)point.X << 32) | (uint)point.Y;

    private static GlobalMousePoint Unpack(long packed) =>
        new((int)(packed >> 32), (int)packed);

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

public readonly record struct GlobalMousePoint(int X, int Y);
