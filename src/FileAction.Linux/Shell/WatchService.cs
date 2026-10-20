using System.Diagnostics;

namespace FileAction.Linux.Shell;

/// <summary>
/// The background <c>fileaction --watch</c> process: started from the settings when a folder is turned on,
/// and with the desktop session (an XDG autostart entry) while any folder is on. One instance at a time.
/// </summary>
public static class WatchService
{
    private static string LockPath => Path.Combine(LinuxSettings.Folder, "watch.lock");

    public static string AutostartPath => Path.Combine(Path.GetDirectoryName(LinuxSettings.Folder)!, "autostart", "fileaction-watch.desktop");

    public static string AutostartEntry(string command) => $"""
        [Desktop Entry]
        Type=Application
        Name=FileAction – watched folders
        Name[cs]=FileAction – sledované složky
        Comment=Processes new files in the folders chosen in FileAction
        Exec={command} --watch
        Icon=fileaction
        Terminal=false
        NoDisplay=true
        X-GNOME-Autostart-enabled=true

        """;

    /// <summary>The single-instance lock, or null when another watcher holds it (keep it open while watching).</summary>
    public static FileStream? TryLock()
    {
        try
        {
            Directory.CreateDirectory(LinuxSettings.Folder);
            // FileShare.None takes an exclusive flock on Linux; it is released when the process ends.
            return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsRunning()
    {
        using var probe = TryLock();
        return probe is null;
    }

    /// <summary>After the settings changed: autostart on or off, and the watcher started when it should run.</summary>
    public static void Sync(LinuxSettings settings)
    {
        var on = settings.IsWatching();
        try
        {
            if (on)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AutostartPath)!);
                File.WriteAllText(AutostartPath, AutostartEntry(Integration.Command));
            }
            else if (File.Exists(AutostartPath))
            {
                File.Delete(AutostartPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        // A running watcher reloads the settings by itself and quits when nothing is left to watch.
        if (on && !IsRunning())
            Start();
    }

    private static void Start()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return;
        var args = new List<string>();
        // "dotnet fileaction.dll" during development.
        if (Path.GetFileNameWithoutExtension(exe) == "dotnet" && typeof(WatchService).Assembly.Location is { Length: > 0 } dll)
            args.Add(dll);
        args.Add("--watch");
        // setsid: the watcher should outlive the terminal the settings were opened from.
        if (TesseractOcr.FindInPath("setsid") is { } setsid)
        {
            args.Insert(0, exe);
            exe = setsid;
        }
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
