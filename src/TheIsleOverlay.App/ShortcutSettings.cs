using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TheIsleOverlay.App;

public enum OverlayShortcutAction
{
    EditMode,
    ToggleMissions,
    ToggleHud,
    MapNotes,
    MutationGuide
}

public sealed record OverlayShortcutSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;
    public string EditMode { get; init; } = "Ctrl+Shift+O";
    public string ToggleMissions { get; init; } = "Alt+N";
    public string ToggleHud { get; init; } = "Alt+P";
    public string MapNotes { get; init; } = "Alt+M";
    public string MutationGuide { get; init; } = "Alt+U";

    public static OverlayShortcutSettings Defaults => new();

    public string For(OverlayShortcutAction action) => action switch
    {
        OverlayShortcutAction.EditMode => EditMode,
        OverlayShortcutAction.ToggleMissions => ToggleMissions,
        OverlayShortcutAction.ToggleHud => ToggleHud,
        OverlayShortcutAction.MapNotes => MapNotes,
        OverlayShortcutAction.MutationGuide => MutationGuide,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}

public readonly record struct ShortcutBinding(uint Modifiers, uint VirtualKey, string DisplayText)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public uint NativeModifiers => Modifiers | ModNoRepeat;

    public static bool TryParse(string? text, out ShortcutBinding binding, out string error)
    {
        binding = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Nhập tổ hợp phím, ví dụ Ctrl+Shift+O.";
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            error = "Phím tắt phải có phím bổ trợ và phím chính.";
            return false;
        }

        uint modifiers = 0;
        string? keyToken = null;
        foreach (var rawToken in tokens)
        {
            var token = rawToken.Trim();
            if (token.Equals("CTRL", StringComparison.OrdinalIgnoreCase)
                || token.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryAddModifier(ref modifiers, ModControl))
                {
                    error = "Không lặp lại Ctrl trong cùng tổ hợp.";
                    return false;
                }
            }
            else if (token.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryAddModifier(ref modifiers, ModAlt))
                {
                    error = "Không lặp lại Alt trong cùng tổ hợp.";
                    return false;
                }
            }
            else if (token.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryAddModifier(ref modifiers, ModShift))
                {
                    error = "Không lặp lại Shift trong cùng tổ hợp.";
                    return false;
                }
            }
            else if (token.Equals("WIN", StringComparison.OrdinalIgnoreCase)
                     || token.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryAddModifier(ref modifiers, ModWin))
                {
                    error = "Không lặp lại Win trong cùng tổ hợp.";
                    return false;
                }
            }
            else if (keyToken is null)
            {
                keyToken = token;
            }
            else
            {
                error = "Chỉ được có một phím chính.";
                return false;
            }
        }

        if (modifiers == 0)
        {
            error = "Phím tắt phải có Ctrl, Alt, Shift hoặc Win.";
            return false;
        }

        if (keyToken is null || !TryGetVirtualKey(keyToken, out var virtualKey, out var canonicalKey))
        {
            error = $"Không nhận biết phím chính “{keyToken ?? string.Empty}”.";
            return false;
        }

        binding = new ShortcutBinding(
            modifiers,
            virtualKey,
            $"{FormatModifiers(modifiers)}+{canonicalKey}");
        return true;
    }

    public static ShortcutBinding ParseOrDefault(string? text, ShortcutBinding fallback) =>
        TryParse(text, out var parsed, out _) ? parsed : fallback;

    private static bool TryAddModifier(ref uint modifiers, uint modifier)
    {
        if ((modifiers & modifier) != 0) return false;
        modifiers |= modifier;
        return true;
    }

    private static string FormatModifiers(uint modifiers)
    {
        var parts = new List<string>(4);
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        return string.Join('+', parts);
    }

    private static bool TryGetVirtualKey(string token, out uint virtualKey, out string canonical)
    {
        virtualKey = 0;
        canonical = string.Empty;
        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                canonical = character.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }

        if (token.StartsWith('F')
            && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionNumber - 1);
            canonical = $"F{functionNumber}";
            return true;
        }

        var named = token.ToUpperInvariant() switch
        {
            "SPACE" => (0x20u, "Space"),
            "TAB" => (0x09u, "Tab"),
            "ENTER" or "RETURN" => (0x0Du, "Enter"),
            "ESC" or "ESCAPE" => (0x1Bu, "Esc"),
            "INSERT" or "INS" => (0x2Du, "Insert"),
            "DELETE" or "DEL" => (0x2Eu, "Delete"),
            "HOME" => (0x24u, "Home"),
            "END" => (0x23u, "End"),
            "PAGEUP" or "PGUP" => (0x21u, "PageUp"),
            "PAGEDOWN" or "PGDN" => (0x22u, "PageDown"),
            "LEFT" => (0x25u, "Left"),
            "UP" => (0x26u, "Up"),
            "RIGHT" => (0x27u, "Right"),
            "DOWN" => (0x28u, "Down"),
            _ => (0u, string.Empty)
        };
        virtualKey = named.Item1;
        canonical = named.Item2;
        return virtualKey != 0;
    }
}

public sealed record ShortcutDefinition(OverlayShortcutAction Action, int Id, string Label);

public static class ShortcutSettingsManager
{
    public const int EditHotkeyId = 0x714;
    public const int ToggleMissionsHotkeyId = 0x715;
    public const int ToggleHudHotkeyId = 0x716;
    public const int MapNotesHotkeyId = 0x717;
    public const int MutationGuideHotkeyId = 0x718;

    public static IReadOnlyList<ShortcutDefinition> Definitions(bool includeMapNotes)
    {
        var definitions = new List<ShortcutDefinition>
        {
            new(OverlayShortcutAction.EditMode, EditHotkeyId, "Chỉnh bố cục (Edit Mode)"),
            new(OverlayShortcutAction.ToggleMissions, ToggleMissionsHotkeyId, "Ẩn / hiện Prime"),
            new(OverlayShortcutAction.ToggleHud, ToggleHudHotkeyId, "Ẩn / hiện toàn HUD"),
            new(OverlayShortcutAction.MutationGuide, MutationGuideHotkeyId, "Mở / đóng sổ tay Mutation")
        };
        if (includeMapNotes)
        {
            definitions.Add(new(OverlayShortcutAction.MapNotes, MapNotesHotkeyId, "Mở / đóng bản đồ mốc"));
        }
        return definitions;
    }

    public static IReadOnlyList<string> Validate(
        OverlayShortcutSettings? settings,
        bool includeMapNotes = true,
        bool checkDuplicates = true)
    {
        settings ??= OverlayShortcutSettings.Defaults;
        var errors = new List<string>();
        var parsed = new Dictionary<OverlayShortcutAction, ShortcutBinding>();
        foreach (var definition in Definitions(includeMapNotes))
        {
            if (!ShortcutBinding.TryParse(settings.For(definition.Action), out var binding, out var error))
            {
                errors.Add($"{definition.Label}: {error}");
                continue;
            }

            var duplicate = checkDuplicates ? parsed.FirstOrDefault(pair =>
                pair.Value.Modifiers == binding.Modifiers
                && pair.Value.VirtualKey == binding.VirtualKey) : default;
            if (!duplicate.Equals(default(KeyValuePair<OverlayShortcutAction, ShortcutBinding>)))
            {
                errors.Add($"{definition.Label} trùng với {Label(duplicate.Key)}.");
                continue;
            }

            parsed[definition.Action] = binding;
        }
        return errors;
    }

    private static string Label(OverlayShortcutAction action) => action switch
    {
        OverlayShortcutAction.EditMode => "Chỉnh bố cục",
        OverlayShortcutAction.ToggleMissions => "Prime",
        OverlayShortcutAction.ToggleHud => "Toàn HUD",
        OverlayShortcutAction.MapNotes => "Bản đồ mốc",
        OverlayShortcutAction.MutationGuide => "Sổ tay Mutation",
        _ => "phím khác"
    };
}

public sealed class ShortcutSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public ShortcutSettingsStore(string? path = null)
    {
        var overridePath = Environment.GetEnvironmentVariable("ISLELIVEMAP_SHORTCUT_SETTINGS_PATH");
        _path = Path.GetFullPath(path
            ?? (string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KLongDev",
                    "IsleLiveMap",
                    "shortcut-settings.json")
                : overridePath));
    }

    public OverlayShortcutSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return OverlayShortcutSettings.Defaults;
            var value = JsonSerializer.Deserialize<OverlayShortcutSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return value is null
                ? OverlayShortcutSettings.Defaults
                : value with { Version = OverlayShortcutSettings.CurrentVersion };
        }
        catch (JsonException)
        {
            return OverlayShortcutSettings.Defaults;
        }
        catch (IOException)
        {
            return OverlayShortcutSettings.Defaults;
        }
        catch (UnauthorizedAccessException)
        {
            return OverlayShortcutSettings.Defaults;
        }
    }

    public bool TrySave(OverlayShortcutSettings settings, out string? error)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validation = ShortcutSettingsManager.Validate(settings);
        if (validation.Count > 0)
        {
            error = string.Join(" ", validation);
            return false;
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Shortcut settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    settings with { Version = OverlayShortcutSettings.CurrentVersion },
                    JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"Không thể lưu phím tắt: {exception.Message}";
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }
}
