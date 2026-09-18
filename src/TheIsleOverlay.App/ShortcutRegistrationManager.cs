using System.Runtime.InteropServices;

namespace TheIsleOverlay.App;

public interface IGlobalShortcutNativeApi
{
    bool Register(IntPtr windowHandle, int id, ShortcutBinding binding);
    bool Unregister(IntPtr windowHandle, int id);
    int LastError { get; }
}

internal sealed class WindowsGlobalShortcutNativeApi : IGlobalShortcutNativeApi
{
    public int LastError => Marshal.GetLastWin32Error();

    public bool Register(IntPtr windowHandle, int id, ShortcutBinding binding) =>
        RegisterHotKey(windowHandle, id, binding.NativeModifiers, binding.VirtualKey);

    public bool Unregister(IntPtr windowHandle, int id) => UnregisterHotKey(windowHandle, id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

public sealed record ShortcutRegistrationFailure(
    OverlayShortcutAction Action,
    string Label,
    string Binding,
    int NativeError);

public sealed record ShortcutRegistrationResult(
    bool Success,
    OverlayShortcutSettings ActiveSettings,
    IReadOnlyList<ShortcutRegistrationFailure> Failures)
{
    public IReadOnlyList<ShortcutRegistrationStatus> Statuses { get; init; } = [];

    public string FriendlyError => Failures.Count == 0
        ? string.Empty
        : string.Join(
            Environment.NewLine,
            Failures.Select(failure => failure.NativeError == 0
                ? $"{failure.Label}: {failure.Binding}"
                : $"{failure.Label}: {failure.Binding} đang bị game hoặc ứng dụng khác sử dụng."));
}

public sealed record ShortcutRegistrationStatus(
    OverlayShortcutAction Action,
    string Label,
    string Binding,
    bool Registered,
    bool UsedFallback);

public static class OverlayInputSafetyPolicy
{
    // A global-hotkey collision must never make the full-screen overlay
    // interactive during startup.
    public static bool StartClickThrough(bool shortcutRegistrationSucceeded) => true;
}

public sealed class ShortcutRegistrationManager : IDisposable
{
    private readonly IGlobalShortcutNativeApi _nativeApi;
    private readonly IntPtr _windowHandle;
    private readonly bool _includeMapNotes;
    private readonly HashSet<int> _registeredIds = [];
    private readonly HashSet<(uint Modifiers, uint VirtualKey)> _registeredBindings = [];
    private bool _disposed;

    public ShortcutRegistrationManager(
        IntPtr windowHandle,
        bool includeMapNotes,
        IGlobalShortcutNativeApi? nativeApi = null)
    {
        _windowHandle = windowHandle;
        _includeMapNotes = includeMapNotes;
        _nativeApi = nativeApi ?? new WindowsGlobalShortcutNativeApi();
    }

    public OverlayShortcutSettings ActiveSettings { get; private set; } =
        OverlayShortcutSettings.Defaults;
    public bool HasCompleteRegistration { get; private set; }

    public ShortcutRegistrationResult RegisterInitial(OverlayShortcutSettings requested)
    {
        ThrowIfDisposed();
        UnregisterAll();
        return RegisterIndependently(
            requested,
            OverlayShortcutSettings.Defaults);
    }

    /// <summary>
    /// Applies the complete set transactionally.  If any chord is unavailable,
    /// every new registration is removed and the previous working set is
    /// restored so the overlay is never left with only part of its controls.
    /// </summary>
    public ShortcutRegistrationResult TryApply(OverlayShortcutSettings requested)
    {
        ThrowIfDisposed();
        var previous = ActiveSettings;
        UnregisterAll();
        return RegisterIndependently(requested, previous);
    }

    public void Dispose()
    {
        if (_disposed) return;
        UnregisterAll();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private ShortcutRegistrationResult RegisterIndependently(
        OverlayShortcutSettings requested,
        OverlayShortcutSettings fallbackSettings)
    {
        // Duplicate chords are handled per action below. A conflict in Alt+U
        // must not prevent the independent Alt+M registration from starting.
        var validation = ShortcutSettingsManager.Validate(
            requested,
            _includeMapNotes,
            checkDuplicates: false);
        if (validation.Count > 0)
        {
            var invalid = validation.Select(message => new ShortcutRegistrationFailure(
                OverlayShortcutAction.EditMode,
                "Phím tắt không hợp lệ",
                message,
                0)).ToArray();
            HasCompleteRegistration = false;
            return new(false, ActiveSettings, invalid);
        }

        var failures = new List<ShortcutRegistrationFailure>();
        var statuses = new List<ShortcutRegistrationStatus>();
        var active = requested;
        foreach (var definition in ShortcutSettingsManager.Definitions(_includeMapNotes))
        {
            var requestedText = requested.For(definition.Action);
            ShortcutBinding.TryParse(requestedText, out var binding, out _);
            var requestedKey = (binding.Modifiers, binding.VirtualKey);
            if (!_registeredBindings.Contains(requestedKey)
                && _nativeApi.Register(_windowHandle, definition.Id, binding))
            {
                _registeredIds.Add(definition.Id);
                _registeredBindings.Add(requestedKey);
                statuses.Add(new ShortcutRegistrationStatus(
                    definition.Action,
                    definition.Label,
                    binding.DisplayText,
                    Registered: true,
                    UsedFallback: false));
                continue;
            }

            failures.Add(new ShortcutRegistrationFailure(
                definition.Action,
                definition.Label,
                binding.DisplayText,
                _registeredBindings.Contains(requestedKey) ? 1409 : _nativeApi.LastError));

            var fallbackText = fallbackSettings.For(definition.Action);
            if (string.Equals(requestedText, fallbackText, StringComparison.OrdinalIgnoreCase)
                || !ShortcutBinding.TryParse(fallbackText, out var fallbackBinding, out _))
            {
                statuses.Add(new ShortcutRegistrationStatus(
                    definition.Action,
                    definition.Label,
                    binding.DisplayText,
                    Registered: false,
                    UsedFallback: false));
                continue;
            }

            var fallbackKey = (fallbackBinding.Modifiers, fallbackBinding.VirtualKey);
            if (!_registeredBindings.Contains(fallbackKey)
                && _nativeApi.Register(_windowHandle, definition.Id, fallbackBinding))
            {
                _registeredIds.Add(definition.Id);
                _registeredBindings.Add(fallbackKey);
                active = WithBinding(active, definition.Action, fallbackText);
                statuses.Add(new ShortcutRegistrationStatus(
                    definition.Action,
                    definition.Label,
                    fallbackBinding.DisplayText,
                    Registered: true,
                    UsedFallback: true));
            }
            else
            {
                statuses.Add(new ShortcutRegistrationStatus(
                    definition.Action,
                    definition.Label,
                    binding.DisplayText,
                    Registered: false,
                    UsedFallback: true));
            }
        }

        ActiveSettings = active;
        HasCompleteRegistration = statuses.All(status => status.Registered);
        return new(
            failures.Count == 0,
            active,
            failures)
        {
            Statuses = statuses
        };
    }

    private static OverlayShortcutSettings WithBinding(
        OverlayShortcutSettings settings,
        OverlayShortcutAction action,
        string binding) => action switch
    {
        OverlayShortcutAction.EditMode => settings with { EditMode = binding },
        OverlayShortcutAction.ToggleMissions => settings with { ToggleMissions = binding },
        OverlayShortcutAction.ToggleHud => settings with { ToggleHud = binding },
        OverlayShortcutAction.MapNotes => settings with { MapNotes = binding },
        OverlayShortcutAction.MutationGuide => settings with { MutationGuide = binding },
        _ => settings
    };

    private void UnregisterAll()
    {
        foreach (var id in _registeredIds.ToArray())
        {
            _nativeApi.Unregister(_windowHandle, id);
        }
        _registeredIds.Clear();
        _registeredBindings.Clear();
        HasCompleteRegistration = false;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
