using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class ShortcutSettingsTests
{
    [Fact]
    public void Defaults_KeepEditModeAndWholeHudAsSeparateActions()
    {
        var settings = OverlayShortcutSettings.Defaults;

        Assert.Equal("Ctrl+Shift+O", settings.EditMode);
        Assert.Equal("Alt+P", settings.ToggleHud);
        Assert.Equal("Alt+U", settings.MutationGuide);
        Assert.NotEqual(settings.EditMode, settings.ToggleHud);
        Assert.Empty(ShortcutSettingsManager.Validate(settings));
        Assert.True(OverlayInputSafetyPolicy.StartClickThrough(
            shortcutRegistrationSucceeded: false));
        Assert.True(OverlayInputSafetyPolicy.StartClickThrough(
            shortcutRegistrationSucceeded: true));
    }

    [Fact]
    public void Definitions_MapEditModeAndWholeHudToTheirOwnHotkeyIds()
    {
        var settings = OverlayShortcutSettings.Defaults;
        var definitions = ShortcutSettingsManager.Definitions(includeMapNotes: true);

        var editMode = Assert.Single(
            definitions,
            definition => definition.Action == OverlayShortcutAction.EditMode);
        var wholeHud = Assert.Single(
            definitions,
            definition => definition.Action == OverlayShortcutAction.ToggleHud);

        Assert.Equal(ShortcutSettingsManager.EditHotkeyId, editMode.Id);
        Assert.Equal(ShortcutSettingsManager.ToggleHudHotkeyId, wholeHud.Id);
        Assert.Equal("Ctrl+Shift+O", settings.For(editMode.Action));
        Assert.Equal("Alt+P", settings.For(wholeHud.Action));
        Assert.Equal("Chỉnh bố cục (Edit Mode)", editMode.Label);
        Assert.Equal("Ẩn / hiện toàn HUD", wholeHud.Label);
    }

    [Theory]
    [InlineData("Ctrl+Shift+O", ShortcutBinding.ModControl | ShortcutBinding.ModShift, 0x4F)]
    [InlineData("Alt+P", ShortcutBinding.ModAlt, 0x50)]
    [InlineData("Win+F12", ShortcutBinding.ModWin, 0x7B)]
    public void Parser_NormalizesSupportedChords(string text, uint modifiers, uint key)
    {
        Assert.True(ShortcutBinding.TryParse(text, out var binding, out var error), error);
        Assert.Equal(modifiers, binding.Modifiers);
        Assert.Equal(key, binding.VirtualKey);
        Assert.NotEqual(0u, binding.NativeModifiers & ShortcutBinding.ModNoRepeat);
    }

    [Fact]
    public void Validation_RejectsDuplicateActions()
    {
        var settings = OverlayShortcutSettings.Defaults with { ToggleHud = "Ctrl+Shift+O" };
        Assert.Contains(
            ShortcutSettingsManager.Validate(settings),
            error => error.Contains("trùng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Store_RoundTripsVersionedSettings()
    {
        var path = TemporaryPath();
        try
        {
            var store = new ShortcutSettingsStore(path);
            var settings = OverlayShortcutSettings.Defaults with
            {
                EditMode = "Ctrl+Shift+F10",
                ToggleHud = "Alt+F11"
            };
            Assert.True(store.TrySave(settings, out var error), error);

            var loaded = store.Load();
            Assert.Equal(OverlayShortcutSettings.CurrentVersion, loaded.Version);
            Assert.Equal("Ctrl+Shift+F10", loaded.EditMode);
            Assert.Equal("Alt+F11", loaded.ToggleHud);
        }
        finally
        {
            DeleteDirectory(path);
        }
    }

    [Fact]
    public void RegistrationManager_RollsBackWholeWorkingSetWhenOneNewChordFails()
    {
        var native = new FakeNativeApi();
        using var manager = new ShortcutRegistrationManager((IntPtr)42, true, native);
        var initial = manager.RegisterInitial(OverlayShortcutSettings.Defaults);
        Assert.True(initial.Success);

        native.FailKey = 0x58; // X
        var requested = OverlayShortcutSettings.Defaults with { ToggleHud = "Alt+X" };
        var result = manager.TryApply(requested);

        Assert.False(result.Success);
        Assert.True(manager.HasCompleteRegistration);
        Assert.Equal("Alt+P", manager.ActiveSettings.ToggleHud);
        Assert.Equal(5, native.Active.Count);
        Assert.Contains(native.Active.Values, binding => binding.VirtualKey == 0x50);
    }

    [Fact]
    public void InitialRegistration_UsesWorkingDefaultsAfterSavedCustomCollision()
    {
        var native = new FakeNativeApi { FailKey = 0x58 }; // X
        using var manager = new ShortcutRegistrationManager((IntPtr)42, true, native);

        var result = manager.RegisterInitial(
            OverlayShortcutSettings.Defaults with { ToggleHud = "Alt+X" });

        Assert.False(result.Success);
        Assert.True(manager.HasCompleteRegistration);
        Assert.Equal(OverlayShortcutSettings.Defaults, result.ActiveSettings);
        Assert.Equal(5, native.Active.Count);
        Assert.Contains(native.Active.Values, binding => binding.VirtualKey == 0x50);
    }

    [Fact]
    public void MutationGuideCollision_DoesNotBlockAltMMapNotes()
    {
        var native = new FakeNativeApi { FailKey = 0x55 }; // U
        using var manager = new ShortcutRegistrationManager((IntPtr)42, true, native);

        var result = manager.RegisterInitial(OverlayShortcutSettings.Defaults);

        Assert.False(result.Success);
        Assert.Contains(result.Statuses, status =>
            status.Action == OverlayShortcutAction.MapNotes
            && status.Registered
            && status.Binding == "Alt+M");
        Assert.Contains(native.Active, pair =>
            pair.Key == ShortcutSettingsManager.MapNotesHotkeyId
            && pair.Value.VirtualKey == 0x4D);
        Assert.Contains(result.Failures, failure =>
            failure.Action == OverlayShortcutAction.MutationGuide);
    }

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        "IsleLiveMap.Tests",
        Guid.NewGuid().ToString("N"),
        "shortcut-settings.json");

    private static void DeleteDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeNativeApi : IGlobalShortcutNativeApi
    {
        public Dictionary<int, ShortcutBinding> Active { get; } = [];
        public uint? FailKey { get; set; }
        public int LastError => 1409;

        public bool Register(IntPtr windowHandle, int id, ShortcutBinding binding)
        {
            if (binding.VirtualKey == FailKey) return false;
            Active[id] = binding;
            return true;
        }

        public bool Unregister(IntPtr windowHandle, int id) => Active.Remove(id);
    }
}
