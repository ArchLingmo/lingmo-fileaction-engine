using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileAction.App.Input;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App;

public enum HotkeyPreset
{
    None,
    CtrlAltSpace,
    CtrlShiftSpace,
    CtrlAltF,
    CtrlAltW,
}

/// <summary>User preferences, stored as JSON in %APPDATA%\FileAction\settings.json.</summary>
public sealed class AppSettings
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileAction");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public ModifierKey ConvertKey { get; set; } = ModifierKey.Shift;
    public ModifierKey ToolsKey { get; set; } = ModifierKey.Ctrl;
    public ModifierKey ActionsKey { get; set; } = ModifierKey.Alt;
    public HotkeyPreset Hotkey { get; set; } = HotkeyPreset.CtrlAltSpace;

    /// <summary>Null = follow the Windows display language.</summary>
    public Language? Language { get; set; }

    /// <summary>Empty = save results next to the original file.</summary>
    public string? OutputDirectory { get; set; }

    public bool RevealResult { get; set; }
    public bool Paused { get; set; }
    public bool FirstRunDone { get; set; }
    public string? LastCopyFolder { get; set; }
    public string? LastMoveFolder { get; set; }

    public Settings Quality { get; set; } = new();

    /// <summary>Operation ids the user removed from the wheels.</summary>
    public List<string> HiddenOperations { get; set; } = [];

    /// <summary>
    /// The user's slice order: these ids come first on every wheel they appear on, in this order; the rest
    /// follow in the default order. Set by dragging slices in Settings → Wheels.
    /// </summary>
    public List<string> OperationOrder { get; set; } = [];

    /// <summary>Operations the user moved to another wheel (id → wheel).</summary>
    public Dictionary<string, WheelKind> WheelOverrides { get; set; } = [];

    /// <summary>Starred slices from versions before 1.7; read once and turned into <see cref="OperationOrder"/>.</summary>
    public List<string> FavoriteOperations { get; set; } = [];

    /// <summary>The last operation run, for "repeat last" (with the values it asked for, e.g. trim times).</summary>
    public string? LastOperationId { get; set; }

    public Dictionary<string, string>? LastParameters { get; set; }

    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// PDF → DOCX through Microsoft Word (better tables and layout). Off by default: Word's PDF import can hang
    /// when it runs hidden, and then the built-in conversion takes over only after a timeout.
    /// </summary>
    public bool UseWordForPdf { get; set; }

    /// <summary>Slices the user put together (e.g. "JPG 70 %"); they appear on the wheels like the built-in ones.</summary>
    public List<Preset> Presets { get; set; } = [];

    /// <summary>Folders whose new files are processed automatically.</summary>
    public List<WatchFolder> WatchFolders { get; set; } = [];

    public WheelKind? WheelFor(ModifierKey key)
    {
        if (key == ModifierKey.None)
            return null;
        if (key == ConvertKey)
            return WheelKind.Convert;
        if (key == ToolsKey)
            return WheelKind.Tools;
        if (key == ActionsKey)
            return WheelKind.Actions;
        return null;
    }

    public ModifierKey KeyFor(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => ConvertKey,
        WheelKind.Tools => ToolsKey,
        _ => ActionsKey,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Migrate(JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings) ?? new AppSettings());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Broken settings file: start with defaults.
        }
        return new AppSettings();
    }

    /// <summary>Stars became the start of the slice order in 1.7.</summary>
    internal static AppSettings Migrate(AppSettings settings)
    {
        if (settings.FavoriteOperations.Count > 0)
        {
            settings.OperationOrder = WheelOrder.Merge(settings.FavoriteOperations, settings.OperationOrder);
            settings.FavoriteOperations = [];
        }
        return settings;
    }

    /// <summary>The self-test changes settings through the UI; it must never overwrite the user's file.</summary>
    internal static bool SavingDisabled { get; set; }

    public void Save()
    {
        if (SavingDisabled)
            return;
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal; settings simply won't persist.
        }
    }

    public static string KeyName(ModifierKey key) => key switch
    {
        ModifierKey.Shift => "Shift",
        ModifierKey.Ctrl => "Ctrl",
        ModifierKey.Alt => "Alt",
        _ => "—",
    };

    public static string WheelName(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => T("Převést", "Convert"),
        WheelKind.Tools => T("Nástroje", "Tools"),
        _ => T("Akce", "Actions"),
    };

    public static string HotkeyName(HotkeyPreset preset) => preset switch
    {
        HotkeyPreset.CtrlAltSpace => T("Ctrl + Alt + Mezerník", "Ctrl + Alt + Space"),
        HotkeyPreset.CtrlShiftSpace => T("Ctrl + Shift + Mezerník", "Ctrl + Shift + Space"),
        HotkeyPreset.CtrlAltF => "Ctrl + Alt + F",
        HotkeyPreset.CtrlAltW => "Ctrl + Alt + W",
        _ => T("Vypnuto", "Off"),
    };
}

/// <summary>Source-generated JSON for the settings file (works in the trimmed build without reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
