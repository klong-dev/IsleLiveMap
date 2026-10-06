using System.Diagnostics;
using System.Runtime.InteropServices;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

// ============================================================================
// KprlLocalMovementSource — host-side memory-read GPS + vitals (no Npcap).
//
// Replaces the Npcap inner source inside PrewarmedLocalMovementSource. Reads
// the local pawn position/heading straight from the game process via the kprl
// kernel driver (\\.\KPRL), and publishes LocalDinosaurVitalsObservation with
// HP/ST/hunger/thirst/growth from the replicated AttributeSet — the same data
// the inbound Iris decoder tried to recover from packets, now lossless.
//
// Requires the KPRLPoc service to be running (installed once by an admin;
// the device ACL allows non-elevated opens — verified 2026-10).
// ============================================================================
public sealed class KprlLocalMovementSource : ILocalMovementSource, ILocalVitalsFeatureSource
{
    private const string GameProcessName = "TheIsleClient-Win64-Shipping";
    private const uint KprlReadVm3 = 0x234408;
    private const uint KprlSecBase = 0x23CC0C;
    private const int MaxReadChunk = 0x80;
    private const ulong GWorldRvaHint = 0x0B614B20UL;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly object _gate = new();
    private IntPtr _device = IntPtr.Zero;
    private uint _pid;
    private ulong _base;
    private ulong _worldRva;
    private bool _ready;
    private string _diagnostic = "created";

    // Vitals freshness gate in the merger is 15 s; publish at 1 Hz.
    private DateTimeOffset _lastVitalsPublish;

    public bool LocalVitalsEnabled => true;

    /// <summary>
    /// Non-owning probe used by the app shell to prefer this source when the
    /// kprl device is present, without racing DisposeAsync on a discarded
    /// instance. Returns false when the KPRLPoc service is not running.
    /// </summary>
    public bool TryOpenDeviceForProbe()
    {
        var probe = CreateFileW("\\\\.\\KPRL", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (probe == (IntPtr)(-1))
        {
            return false;
        }
        CloseHandle(probe);
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string name, uint access, uint share, IntPtr security, uint disposition,
        uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr handle, uint control, byte[] inBuffer, uint inLength,
        byte[] outBuffer, uint outLength, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public async IAsyncEnumerable<LocalMovementObservation> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        OpenDevice();
        var lastProcessCheck = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (_pid == 0 || now - lastProcessCheck > TimeSpan.FromSeconds(2))
            {
                lastProcessCheck = now;
                TryAttachGame();
            }

            if (_ready)
            {
                var obs = CaptureFrame(now);
                if (obs is not null)
                {
                    yield return obs.Value;
                }
            }
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    private void OpenDevice()
    {
        lock (_gate)
        {
            if (_device != IntPtr.Zero) return;
            _device = CreateFileW("\\\\.\\KPRL", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            _diagnostic = _device != (IntPtr)(-1) ? "device_open" : "device_missing";
        }
    }

    private void TryAttachGame()
    {
        if (_device == IntPtr.Zero || _device == (IntPtr)(-1)) return;
        var procs = Process.GetProcessesByName(GameProcessName);
        if (procs.Length == 0)
        {
            lock (_gate) { _pid = 0; _ready = false; _diagnostic = "waiting_game"; }
            return;
        }
        var pid = (uint)procs[0].Id;
        if (pid != _pid)
        {
            lock (_gate) { _pid = pid; _ready = false; _base = 0; _diagnostic = "game_found"; }
        }
        if (!_ready || _base == 0)
        {
            ResolveBaseAndWorld();
        }
    }

    private byte[] ReadMem(ulong address, int size)
    {
        var buf = new byte[size];
        var pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            var done = 0;
            while (done < size)
            {
                var chunk = Math.Min(MaxReadChunk, size - done);
                var req = new byte[0x80];
                BitConverter.GetBytes((ulong)0).CopyTo(req, 0x00);
                BitConverter.GetBytes((ulong)pin.AddrOfPinnedObject() + (ulong)done).CopyTo(req, 0x08);
                BitConverter.GetBytes(address + (ulong)done).CopyTo(req, 0x10);
                BitConverter.GetBytes((uint)chunk).CopyTo(req, 0x18);
                BitConverter.GetBytes(_pid).CopyTo(req, 0x20);
                if (!DeviceIoControl(_device, KprlReadVm3, req, 0x80, req, 0x80,
                        out _, IntPtr.Zero))
                {
                    return Array.Empty<byte>();
                }
                done += chunk;
            }
            return buf;
        }
        finally
        {
            pin.Free();
        }
    }

    private ulong ReadU64(ulong a)
    {
        var b = ReadMem(a, 8);
        return b.Length == 8 ? BitConverter.ToUInt64(b, 0) : 0;
    }

    private double ReadF64(ulong a)
    {
        var b = ReadMem(a, 8);
        return b.Length == 8 ? BitConverter.ToDouble(b, 0) : 0;
    }

    private static bool UserPtr(ulong p) => p >= 0x10000 && p <= 0x7FFFFFFFFFFF;

    private void ResolveBaseAndWorld()
    {
        var q = new byte[64];
        BitConverter.GetBytes(_pid).CopyTo(q, 0);
        if (!DeviceIoControl(_device, KprlSecBase, q, 64, q, 64, out _, IntPtr.Zero))
        {
            lock (_gate) _diagnostic = "secbase_fail";
            return;
        }
        var gameBase = BitConverter.ToUInt64(q, 8);
        if (!UserPtr(gameBase))
        {
            lock (_gate) _diagnostic = "secbase_null";
            return;
        }
        var mz = ReadMem(gameBase, 2);
        if (mz.Length != 2 || mz[0] != 0x4D || mz[1] != 0x5A)
        {
            lock (_gate) _diagnostic = "mz_fail";
            return;
        }
        _base = gameBase;

        var worldPtr = ReadU64(gameBase + GWorldRvaHint);
        if (LooksLikeWorld(worldPtr))
        {
            _worldRva = GWorldRvaHint;
            lock (_gate) { _ready = true; _diagnostic = "ready"; }
            return;
        }
        // Bounded .data scan fallback.
        const ulong windowStart = 0x0B500000UL;
        const ulong windowEnd = 0x0BC00000UL;
        var probe = new byte[8];
        for (ulong rva = windowStart; rva < windowEnd; rva += 8)
        {
            var b = ReadMem(gameBase + rva, 8);
            if (b.Length != 8) continue;
            var cand = BitConverter.ToUInt64(b, 0);
            if (!UserPtr(cand)) continue;
            if (LooksLikeWorld(cand))
            {
                _worldRva = rva;
                lock (_gate) { _ready = true; _diagnostic = "ready_scan"; }
                return;
            }
        }
        lock (_gate) _diagnostic = "gworld_not_found";
    }

    private bool LooksLikeWorld(ulong worldPtr)
    {
        if (!UserPtr(worldPtr)) return false;
        var gi = ReadU64(worldPtr + 0x228);
        if (!UserPtr(gi)) return false;
        var lpd = ReadU64(gi + 0x38);
        if (!UserPtr(lpd)) return false;
        var lp = ReadU64(lpd);
        if (!UserPtr(lp)) return false;
        var ctrl = ReadU64(lp + 0x30);
        return UserPtr(ctrl);
    }

    private LocalMovementObservation? CaptureFrame(DateTimeOffset now)
    {
        var worldPtr = ReadU64(_base + _worldRva);
        if (!UserPtr(worldPtr)) { MarkStale(); return null; }
        var gi = ReadU64(worldPtr + 0x228);
        if (!UserPtr(gi)) return null;
        var lpd = ReadU64(gi + 0x38);
        if (!UserPtr(lpd)) return null;
        var lp = ReadU64(lpd);
        if (!UserPtr(lp)) return null;
        var ctrl = ReadU64(lp + 0x30);
        if (!UserPtr(ctrl)) return null;
        var pawn = ReadU64(ctrl + 0x350);
        var yaw = ReadF64(ctrl + 0x320 + 8);

        double x = 0, y = 0, z = 0;
        var havePos = UserPtr(pawn) && ReadPawnLocation(pawn, ref x, ref y, ref z);

        LocalDinosaurVitalsObservation? vitals = null;
        if (UserPtr(pawn) && now - _lastVitalsPublish >= TimeSpan.FromSeconds(1))
        {
            vitals = ReadVitals(pawn, now);
            if (vitals is not null) _lastVitalsPublish = now;
        }

        return new LocalMovementObservation(
            now,
            new UnrealMovementCandidate(
                x, y, z, yaw, 0, 0, 0, 0),
            null,
            vitals);
    }

    private bool ReadPawnLocation(ulong pawn, ref double x, ref double y, ref double z)
    {
        var root = ReadU64(pawn + 0x1B8);
        if (!UserPtr(root)) return false;
        foreach (var off in new ulong[] { 0x108, 0x1B0 + 0x20, 0x140 })
        {
            var t = ReadMem(root + off, 24);
            if (t.Length != 24) continue;
            x = BitConverter.ToDouble(t, 0);
            y = BitConverter.ToDouble(t, 8);
            z = BitConverter.ToDouble(t, 16);
            if (IsSane(x, y, z) && (Math.Abs(x) > 100 || Math.Abs(y) > 100)) return true;
        }
        return false;
    }

    private static bool IsSane(double x, double y, double z) =>
        double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z) &&
        Math.Abs(x) < 1e8 && Math.Abs(y) < 1e8 && Math.Abs(z) < 1e8 &&
        !(x == 0 && y == 0 && z == 0);

    private LocalDinosaurVitalsObservation? ReadVitals(ulong pawn, DateTimeOffset now)
    {
        var set = ReadU64(pawn + 0x13A0);
        if (!UserPtr(set))
        {
            var asc = ReadU64(pawn + 0x0AE0);
            if (!UserPtr(asc)) return null;
            set = ReadU64(asc + 0x10A8);
            if (!UserPtr(set)) return null;
        }
        var buf = ReadMem(set + 0x30, 0xB0);
        if (buf.Length != 0xB0) return null;
        static float Cur(byte[] b, int off) => BitConverter.ToSingle(b, off);
        var hp = Cur(buf, 0x0C);
        var hpMax = Cur(buf, 0x40 - 0x30 + 0x0C);
        var st = Cur(buf, 0x50 - 0x30 + 0x0C);
        var stMax = Cur(buf, 0x60 - 0x30 + 0x0C);
        var hu = Cur(buf, 0x70 - 0x30 + 0x0C);
        var huMax = Cur(buf, 0x80 - 0x30 + 0x0C);
        var th = Cur(buf, 0x90 - 0x30 + 0x0C);
        var thMax = Cur(buf, 0xA0 - 0x30 + 0x0C);
        if (!(hpMax > 1f) || hp < 0f || hp > hpMax * 1.25f) return null;
        var g = ReadMem(pawn + 0x1E68, 4);
        var growth = g.Length == 4 ? BitConverter.ToSingle(g, 0) : float.NaN;

        return new LocalDinosaurVitalsObservation(
            now,
            new ExactVitals
            {
                Growth = float.IsFinite(growth) && growth >= 0 && growth <= 1.5
                    ? Math.Clamp(growth <= 1 ? growth : growth / 100d, 0d, 1d)
                    : null,
                Health = hp,
                MaxHealth = hpMax,
                Stamina = st,
                MaxStamina = stMax,
                Hunger = hu,
                MaxHunger = huMax,
                Thirst = th,
                MaxThirst = thMax,
            },
            pawn); // NetRefHandle slot: pawn pointer doubles as stable identity
    }

    private void MarkStale()
    {
        lock (_gate) { _ready = false; _diagnostic = "stale"; }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_device != IntPtr.Zero && _device != (IntPtr)(-1))
            {
                CloseHandle(_device);
            }
            _device = IntPtr.Zero;
        }
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
