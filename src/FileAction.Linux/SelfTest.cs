using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FileAction.Core;
using FileAction.Core.Operations;
using FileAction.Linux.Shell;
using FileAction.Linux.UI;

namespace FileAction.Linux;

/// <summary><c>fileaction --self-test DIR</c>: renders the windows headlessly to PNG (no display needed; used by CI).</summary>
public static class SelfTest
{
    public static int Run(string outDir)
    {
        var log = new List<string>();
        try
        {
            Program.WithBundledFont(AppBuilder.Configure<App>().UseSkia())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            foreach (var (language, dark, folder) in new[] { (Language.English, true, "en"), (Language.English, false, "en-light"), (Language.Czech, true, "cs") })
            {
                Loc.Current = language;
                Palette.Override = dark;
                RenderAll(Path.Combine(outDir, folder), language == Language.English, log);
            }
            log.AddRange(SmokeTest.Run(Path.Combine(outDir, "smoke")));
            SettingsJson(log);
            Presets(log);
            Watcher(Path.Combine(outDir, "watch"), log);
            if (FeedbackWindow.OsName("NAME=\"Ubuntu\"\nVERSION_ID=\"24.04\"\nPRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\n", new Version(6, 8, 0, 45)) != "Ubuntu 24.04 (6.8)"
                || FeedbackWindow.OsName("NAME=\"Arch Linux\"\nID=arch\n", new Version(6, 11, 2)) != "Arch Linux (6.11)")
                throw new InvalidOperationException("feedback OS name");
            log.Add("ok feedback os name");
            File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAILED: " + ex);
            Directory.CreateDirectory(outDir);
            File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RenderAll(string dir, bool english, List<string> log)
    {
        Directory.CreateDirectory(dir);
        string N(string cs, string en) => english ? en : cs;
        var settings = new LinuxSettings();
        var controller = new Controller(settings, null);
        var photo = new List<FileItem> { new(N("/home/jana/Obrázky/dovolená.jpg", "/home/jane/Pictures/vacation.jpg")) };
        var video = new List<FileItem> { new(N("/home/jana/Videa/klip.mov", "/home/jane/Videos/clip.mov")) };

        Save(Wheel(controller, photo, WheelKind.Convert, 1), dir, "wheel-image-convert", log);
        Save(Wheel(controller, photo, WheelKind.Tools, 0), dir, "wheel-image-tools", log);
        Save(Wheel(controller, video, WheelKind.Convert, 0), dir, "wheel-video-convert", log);
        Save(Wheel(controller, photo, WheelKind.Actions, 2), dir, "wheel-actions", log);
        Save(Wheel(controller, [], WheelKind.Convert, -1), dir, "wheel-drop-zone", log);

        var jobs = new JobsWindow();
        jobs.Add(N("Převod na MP4", "Convert to MP4"), N("klip.mov", "clip.mov"), () => { }).Report(0.42);
        jobs.Add(N("Komprimovat", "Compress"), N("dovolená.jpg", "vacation.jpg"), () => { })
            .Complete([N("/home/jana/Obrázky/dovolená (komprimováno).jpg", "/home/jane/Pictures/vacation (compressed).jpg")], null);
        Save(jobs, dir, "jobs", log);

        var trim = LinuxOperations.Conversions(settings).Single(o => o.Id == "video.trim");
        Save(new ParameterWindow(trim, video), dir, "trim-dialog", log);

        // Settings with a preset and a watched folder, as a user would have them.
        var preset = new Preset { Id = "web1920", Name = "Web 1920", Kind = PresetKind.Image, Format = "jpg", Quality = 80, MaxSide = 1920, StripMetadata = true };
        settings.Presets.Add(preset);
        settings.WatchFolders.Add(new WatchFolder { Path = N("/home/jana/Obrázky/Snímky obrazovky", "/home/jane/Pictures/Screenshots"), OperationId = "preset.web1920" });
        controller.Reload(settings);
        var watchOps = () => LinuxOperations.ForWatching(settings);
        Save(new SettingsWindow(settings, watchOps, () => { }), dir, "settings-presets", log);
        Save(new SettingsWindow(new LinuxSettings(), watchOps, () => { }), dir, "settings-presets-empty", log);
        Save(new SettingsWindow(settings, watchOps, () => { }, SettingsPage.WatchFolders), dir, "settings-watch", log);
        Save(new SettingsWindow(settings, watchOps, () => { }, SettingsPage.About), dir, "settings-about", log);
        Save(new PresetWindow(preset), dir, "preset-dialog", log);
        Save(new PresetWindow(new Preset { Kind = PresetKind.Video, Format = "mp4", Height = 720 }), dir, "preset-dialog-video", log);
        var watch = new WatchFolderWindow(null, watchOps());
        watch.Preselect(N("/home/jana/Stažené", "/home/jane/Downloads"), "preset.web1920");
        Save(watch, dir, "watch-dialog", log);
        Save(new FeedbackWindow(), dir, "feedback", log);
    }

    private static void SettingsJson(List<string> log)
    {
        var settings = new LinuxSettings { Language = Language.Czech };
        settings.Presets.Add(new Preset { Name = "MP4", Kind = PresetKind.Video, Format = "mp4", VideoQuality = VideoQuality.Small });
        settings.WatchFolders.Add(new WatchFolder { Path = "/tmp/in", OperationId = "image.to.webp", IncludeSubfolders = true });
        var json = System.Text.Json.JsonSerializer.Serialize(settings, LinuxSettingsJson.Default.LinuxSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, LinuxSettingsJson.Default.LinuxSettings);
        if (back?.Language != Language.Czech || back.Presets is not [{ Kind: PresetKind.Video, VideoQuality: VideoQuality.Small }]
            || back.WatchFolders is not [{ OperationId: "image.to.webp", IncludeSubfolders: true }] || !back.IsWatching() || json.Contains("IsWatching"))
            throw new InvalidOperationException("settings JSON round trip");
        // Files written before presets and watched folders existed still load.
        var old = System.Text.Json.JsonSerializer.Deserialize("""{ "Language": "English", "FavoriteOperations": ["image.to.png"] }""", LinuxSettingsJson.Default.LinuxSettings);
        if (old is not { Presets: [], WatchFolders: [], FavoriteOperations: ["image.to.png"] } || old.IsWatching())
            throw new InvalidOperationException("old settings JSON");
        log.Add("ok settings json");
    }

    /// <summary>Presets are on the wheel and in the command line's catalog; favorites still order the wheel.</summary>
    private static void Presets(List<string> log)
    {
        var settings = new LinuxSettings { FavoriteOperations = ["preset.half"] };
        settings.Presets.Add(new Preset { Id = "half", Name = "Half", Kind = PresetKind.Image, Format = Preset.SameFormat, Percent = 50 });
        settings.Presets.Add(new Preset { Id = "webp", Name = "WebP", Kind = PresetKind.Image, Format = "webp" });
        var controller = new Controller(settings, null);
        var photo = new List<FileItem> { new("/tmp/photo.png") };
        if (controller.Operations(WheelKind.Tools, photo).FirstOrDefault()?.Id != "preset.half"
            || !controller.Operations(WheelKind.Convert, photo).Any(o => o.Id == "preset.webp")
            || !LinuxOperations.Conversions(settings).Any(o => o.Id == "preset.webp")
            || !LinuxOperations.ForWatching(settings).Any(o => o.Id == "preset.half")
            || LinuxOperations.ForWatching(settings).Any(o => o.Parameters.Count > 0 || o.Wheel == WheelKind.Actions))
            throw new InvalidOperationException("presets on the wheels");
        log.Add("ok presets");
    }

    /// <summary>The folder watcher on a real folder: new files run, temporary files and FileAction's own results don't.</summary>
    private static void Watcher(string dir, List<string> log)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        // Keep the shared results list out of the real ~/.cache.
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(dir, ".cache"));
        var settings = new LinuxSettings();
        settings.WatchFolders.Add(new WatchFolder { Path = dir, OperationId = "image.to.png", IncludeSubfolders = true });
        settings.WatchFolders.Add(new WatchFolder { Path = Path.Combine(dir, "missing"), OperationId = "image.to.png" });
        var ran = new System.Collections.Concurrent.ConcurrentBag<string>();
        var warnings = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var watcher = new FolderWatcher(() => settings, new ToolLocator(), () => LinuxOperations.ForWatching(settings),
            (op, items) => ran.Add(op.Id + " " + Path.GetFileName(items[0].Path)), (title, text) => warnings.Add(text), action => action())
        {
            SettleCheck = TimeSpan.FromMilliseconds(150),
        };
        watcher.Apply();
        if (watcher.Count != 1)
            throw new InvalidOperationException($"watcher: {watcher.Count} folders watched, expected 1 (the missing one is skipped)");

        var own = Path.Combine(dir, "photo (FileAction).jpg");
        var ownFolder = Path.Combine(dir, "pages");
        RecentResults.Add([own, ownFolder]);
        var jpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=");
        File.WriteAllBytes(Path.Combine(dir, "new.jpg"), jpeg);
        File.WriteAllBytes(Path.Combine(dir, "download.jpg.crdownload"), jpeg);
        File.WriteAllBytes(Path.Combine(dir, ".hidden.jpg"), jpeg);
        File.WriteAllBytes(own, jpeg);
        Directory.CreateDirectory(ownFolder);
        File.WriteAllBytes(Path.Combine(ownFolder, "page 1.jpg"), jpeg);
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not an image");
        // A finished download: renamed from its temporary name.
        File.WriteAllBytes(Path.Combine(dir, "phone.jpg.part"), jpeg);
        File.Move(Path.Combine(dir, "phone.jpg.part"), Path.Combine(dir, "phone.jpg"));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (ran.Count < 2 && DateTime.UtcNow < deadline)
            Thread.Sleep(100);
        Thread.Sleep(1500); // anything that should not run gets the time to show up anyway
        var got = ran.OrderBy(r => r, StringComparer.Ordinal).ToList();
        if (!got.SequenceEqual(["image.to.png new.jpg", "image.to.png phone.jpg"]))
            throw new InvalidOperationException("watcher ran: " + string.Join(", ", got));
        if (!RecentResults.Contains(Path.Combine(ownFolder, "page 2.jpg")) || RecentResults.Contains(Path.Combine(dir, "pages2", "x.jpg")))
            throw new InvalidOperationException("watcher: own results");

        // A rule whose preset was deleted is reported once, not for every file.
        settings.WatchFolders[0].OperationId = "preset.deleted";
        File.WriteAllBytes(Path.Combine(dir, "late 1.jpg"), jpeg);
        File.WriteAllBytes(Path.Combine(dir, "late 2.jpg"), jpeg);
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (warnings.IsEmpty && DateTime.UtcNow < deadline)
            Thread.Sleep(100);
        Thread.Sleep(1000);
        if (warnings.Count != 1 || ran.Count != 2)
            throw new InvalidOperationException($"watcher: {warnings.Count} warnings, {ran.Count} runs");
        log.Add("ok folder watcher");
    }

    private static WheelWindow Wheel(Controller controller, List<FileItem> items, WheelKind kind, int highlight)
    {
        var wheel = controller.CreateWheel(items);
        if (kind != WheelKind.Convert)
            wheel.SwitchTo(kind);
        if (highlight >= 0)
            wheel.Highlight(highlight);
        return wheel;
    }

    private static void Save(Window window, string dir, string name, List<string> log)
    {
        Palette.Refresh();
        if (window is not WheelWindow)
            window.Background = Palette.Page;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered: " + name);
        #pragma warning disable CS0618 // the replacement overload needs encoder options we do not use
        frame.Save(Path.Combine(dir, name + ".png"));
#pragma warning restore CS0618
        window.Close();
        log.Add($"ok {Path.GetFileName(dir)}/{name}");
    }
}
