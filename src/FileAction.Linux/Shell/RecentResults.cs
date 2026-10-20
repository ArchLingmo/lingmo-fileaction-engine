using System.Globalization;

namespace FileAction.Linux.Shell;

/// <summary>
/// Files FileAction wrote in the last half hour, shared between processes: on Linux the wheel, the command
/// line and the folder watcher are separate processes, and the watcher must not pick up results of the others
/// (a "compress" rule would otherwise compress the compressed copy). Kept in ~/.cache/fileaction/results.
/// </summary>
public static class RecentResults
{
    public static readonly TimeSpan Remember = TimeSpan.FromMinutes(30);
    private static readonly Dictionary<string, DateTime> Local = new(StringComparer.Ordinal);

    public static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(LinuxSettings.Home, ".cache"),
        "fileaction", "results");

    /// <summary>Remembers outputs (files or folders) of a finished job.</summary>
    public static void Add(IEnumerable<string> outputs)
    {
        var now = DateTime.UtcNow;
        var paths = outputs.Select(Path.GetFullPath).ToList();
        if (paths.Count == 0)
            return;
        lock (Local)
        {
            foreach (var path in paths)
                Local[path] = now;
            try
            {
                // Rewritten whole each time: drops old lines and stays a few lines long.
                var entries = Read().Where(e => now - e.Time < Remember && !paths.Contains(e.Path))
                    .Select(e => Line(e.Time, e.Path))
                    .Concat(paths.Select(p => Line(now, p)));
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var temp = FilePath + "." + Environment.ProcessId + ".tmp";
                File.WriteAllLines(temp, entries);
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The in-process list still protects this process's own results.
            }
        }
    }

    /// <summary>True for a file FileAction wrote, or one inside a folder it wrote (split pages, extracted archives).</summary>
    public static bool Contains(string path)
    {
        var full = Path.GetFullPath(path);
        var now = DateTime.UtcNow;
        List<string> known;
        lock (Local)
        {
            foreach (var old in Local.Where(kv => now - kv.Value > Remember).Select(kv => kv.Key).ToList())
                Local.Remove(old);
            known = [.. Local.Keys, .. Read().Where(e => now - e.Time < Remember).Select(e => e.Path)];
        }
        return known.Any(own => own == full || full.StartsWith(Path.TrimEndingDirectorySeparator(own) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string Line(DateTime time, string path) => time.Ticks.ToString(CultureInfo.InvariantCulture) + "\t" + path;

    private static IEnumerable<(DateTime Time, string Path)> Read()
    {
        string[] lines;
        try
        {
            lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return lines
            .Select(l => l.Split('\t', 2))
            .Where(p => p.Length == 2 && long.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out var t) && t > 0 && t <= DateTime.MaxValue.Ticks)
            .Select(p => (new DateTime(long.Parse(p[0], CultureInfo.InvariantCulture), DateTimeKind.Utc), p[1]))
            .ToList();
    }
}
