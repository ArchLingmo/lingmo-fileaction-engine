using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.Linux.Shell;

/// <summary>
/// Runs the chosen operation on every new file in a watched folder (the Linux side of the Windows watcher).
/// A file is processed once it stops growing and can be opened (downloads and copies finish first);
/// FileAction's own results are skipped (<see cref="RecentResults"/>), so a rule like "compress" does not
/// loop on its output.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(30);

    private readonly Func<LinuxSettings> _settings;
    private readonly ToolLocator _tools;
    private readonly Func<IReadOnlyList<Operation>> _operations;
    private readonly Action<Operation, IReadOnlyList<FileItem>> _run;
    private readonly Action<string, string> _warn;
    private readonly Action<Action> _post;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _waiting = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private readonly HashSet<string> _watched = new(StringComparer.Ordinal);
    private readonly Timer _retry;

    /// <param name="settings">The current settings (they may be reloaded while watching).</param>
    /// <param name="operations">What the rules can run, looked up by id.</param>
    /// <param name="run">Starts an operation on one file (on the UI thread).</param>
    /// <param name="warn">Shows a problem (title, text); each one once per run.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    public FolderWatcher(Func<LinuxSettings> settings, ToolLocator tools, Func<IReadOnlyList<Operation>> operations,
        Action<Operation, IReadOnlyList<FileItem>> run, Action<string, string> warn, Action<Action> post)
    {
        _settings = settings;
        _tools = tools;
        _operations = operations;
        _run = run;
        _warn = warn;
        _post = post;
        // Folders that were missing (USB disk, network share) or whose watcher broke are picked up again.
        _retry = new Timer(_ => _post(() =>
        {
            if (_settings().WatchFolders.Any(f => f.Enabled && !IsWatched(f.Path) && Directory.Exists(f.Path)))
                Apply();
        }), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>How often a new file's size is checked; two equal sizes in a row mean it is finished.</summary>
    public TimeSpan SettleCheck { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Folders currently watched.</summary>
    public int Count
    {
        get
        {
            lock (_watchers)
                return _watchers.Count;
        }
    }

    private bool IsWatched(string path)
    {
        lock (_watchers)
            return _watched.Contains(path);
    }

    /// <summary>(Re)starts watching the enabled folders from the settings.</summary>
    public void Apply()
    {
        lock (_watchers)
        {
            StopAll();
            foreach (var folder in _settings().WatchFolders.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.OperationId)))
            {
                if (!Directory.Exists(folder.Path))
                    continue;
                try
                {
                    var watcher = new FileSystemWatcher(folder.Path)
                    {
                        IncludeSubdirectories = folder.IncludeSubfolders,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                        InternalBufferSize = 64 * 1024,
                    };
                    watcher.Created += (_, e) => OnFile(e.FullPath);
                    // A rename is a new file only when it finishes a download ("x.part" → "x.mp4") or one of
                    // FileAction's half-written files, not when the user renames a file that was already there.
                    watcher.Renamed += (_, e) =>
                    {
                        if (!WatchRules.IsCandidate(e.OldFullPath))
                            OnFile(e.FullPath);
                    };
                    var path = folder.Path;
                    watcher.Error += (_, e) => _post(() =>
                    {
                        // Too many changes at once (inotify queue overflow) or the folder went away: start over, and say so once.
                        Warn(path + "|error", T("Sledovaná složka", "Watched folder"),
                            T($"Sledování {path} se přerušilo ({e.GetException().Message}). Zkusím to znovu.",
                              $"Watching {path} stopped ({e.GetException().Message}). Trying again."));
                        Apply();
                    });
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                    _watched.Add(folder.Path);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    // Typically the inotify watch limit (fs.inotify.max_user_watches) with large subfolder trees.
                    Warn(folder.Path, T("Složku nejde sledovat", "Cannot watch the folder"), $"{folder.Path}: {ex.Message}");
                }
            }
        }
    }

    private void OnFile(string path)
    {
        if (!WatchRules.IsCandidate(path))
            return;
        lock (_waiting)
        {
            if (!_waiting.Add(path))
                return;
        }
        _ = WaitAndRunAsync(path);
    }

    private async Task WaitAndRunAsync(string path)
    {
        try
        {
            if (!await WaitUntilFinishedAsync(path, SettleCheck) || RecentResults.Contains(path))
                return;
        }
        finally
        {
            lock (_waiting)
                _waiting.Remove(path);
        }
        _post(() => Run(path));
    }

    private void Run(string path)
    {
        var folder = _settings().WatchFolders.FirstOrDefault(f => f.Enabled && WatchRules.IsInside(f, path));
        if (folder is null)
            return;
        var op = _operations().FirstOrDefault(o => o.Id == folder.OperationId);
        if (op is null)
        {
            Warn(folder.Path, T("Sledovaná složka", "Watched folder"),
                T($"Akce pro {folder.Path} už neexistuje (smazaná předvolba?). Upravte ji v nastavení (fileaction --settings).",
                  $"The action for {folder.Path} no longer exists (deleted preset?). Change it in the settings (fileaction --settings)."));
            return;
        }
        var items = new[] { new FileItem(path) };
        if (!op.AppliesTo(items))
            return;
        if (!op.IsAvailable(_tools, items))
        {
            var missing = op.Requires.Where(t => !_tools.Has(t)).ToList();
            Warn(folder.Path + "|tools", T("Sledovaná složka", "Watched folder"),
                T($"„{op.Label}“ potřebuje {string.Join(", ", missing.Select(ToolLocator.DisplayName))}. ",
                  $"“{op.Label}” needs {string.Join(", ", missing.Select(ToolLocator.DisplayName))}. ")
                + string.Join(" ", missing.Select(ToolLocator.InstallHint)));
            return;
        }
        _run(op, items);
    }

    /// <summary>Waits until the file stops changing and can be read; false if it vanished or never settled.</summary>
    private static async Task<bool> WaitUntilFinishedAsync(string path, TimeSpan check)
    {
        long last = -1;
        var deadline = DateTime.UtcNow + GiveUpAfter;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(check).ConfigureAwait(false);
            long size;
            try
            {
                if (!File.Exists(path))
                    return false;
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                continue;
            }
            if (size > 0 && size == last && CanRead(path))
                return true;
            last = size;
        }
        return false;
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Warn(string key, string title, string text)
    {
        lock (_warned)
        {
            if (!_warned.Add(key))
                return;
        }
        _warn(title, text);
    }

    private void StopAll()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
        _watched.Clear();
    }

    public void Dispose()
    {
        _retry.Dispose();
        lock (_watchers)
            StopAll();
    }
}
