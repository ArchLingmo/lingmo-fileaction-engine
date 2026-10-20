using System.Text.Json;
using System.Text.Json.Serialization;
using FileAction.Core;

namespace FileAction.Linux;

/// <summary>Preferences in ~/.config/fileaction/settings.json (same names as on Windows).</summary>
public sealed class LinuxSettings
{

    /// <summary>$HOME; unlike GetFolderPath it doesn't come back empty when the folder is missing.</summary>
    public static string Home => Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home
        ? home
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Folder => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(Home, ".config"),
        "fileaction");

    public static string FilePath =>Path.Combine(Folder, "settings.json");

    /// <summary>Null = follow the system language.</summary>
    public Language? Language { get; set; }

    /// <summary>Empty = save results next to the original file.</summary>
    public string? OutputDirectory { get; set; }

    public Settings Quality { get; set; } = new();
    public List<string> HiddenOperations { get; set; } = [];
    public List<string> FavoriteOperations { get; set; } = [];
    public string? LastOperationId { get; set; }
    public Dictionary<string, string>? LastParameters { get; set; }

    /// <summary>Tesseract languages for OCR, e.g. "ces+eng"; empty = pick from what is installed.</summary>
    public string? OcrLanguages { get; set; }

    /// <summary>The user's own slices (on the wheels, in <c>fileaction list/run</c> and in watched folders).</summary>
    public List<Preset> Presets { get; set; } = [];

    /// <summary>Folders whose new files the background <c>fileaction --watch</c> processes.</summary>
    public List<WatchFolder> WatchFolders { get; set; } = [];

    /// <summary>True when at least one folder should be watched.</summary>
    public bool IsWatching() => WatchFolders.Any(f => f.Enabled && !string.IsNullOrWhiteSpace(f.OperationId));

    public static LinuxSettings Load() => TryLoad() ?? new LinuxSettings();

    /// <summary>The saved settings (defaults when there is no file yet); null when the file can't be read.</summary>
    public static LinuxSettings? TryLoad()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), LinuxSettingsJson.Default.LinuxSettings)
                : new LinuxSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, LinuxSettingsJson.Default.LinuxSettings));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Source-generated JSON for the settings file (works in the trimmed build without reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LinuxSettings))]
internal sealed partial class LinuxSettingsJson : JsonSerializerContext;
