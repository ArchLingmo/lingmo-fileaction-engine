using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Actions;
using FileAction.App.Jobs;
using FileAction.App.UI;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Operations;

namespace FileAction.App;

/// <summary>
/// <c>FileAction.exe --self-test [folder]</c>: builds every window offscreen and renders previews to PNG.
/// Used by CI to catch UI construction errors on a real Windows machine.
/// </summary>
internal static class SelfTest
{
    public static int Run(string outDir)
    {
        var log = new List<string>();
        try
        {
            Directory.CreateDirectory(outDir);
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var (language, dark, folder) in new[] { (Language.English, true, "en"), (Language.English, false, "en-light"), (Language.Czech, true, "cs") })
            {
                Loc.Current = language;
                Theme.Override = dark;
                Theme.Refresh();
                RenderAll(Path.Combine(outDir, folder), language == Language.English, log);
            }

            File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAILED: " + ex);
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            }
            catch (IOException)
            {
            }
            return 1;
        }
    }

    private static void RenderAll(string outDir, bool english, List<string> log)
    {
        Directory.CreateDirectory(outDir);
        string N(string cs, string en) => english ? en : cs;
        var settings = new AppSettings();
        var tools = new ToolLocator();
        var core = Catalog.Build();
        var actions = FileActions.Build(settings, () => { });
        IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) =>
            Catalog.For(wheel == WheelKind.Actions ? actions : core, wheel, items, tools);

        var samples = new (string Name, WheelMode Mode, WheelKind Wheel, string[] Files, int Highlight)[]
        {
            ("wheel-image-convert", WheelMode.Drag, WheelKind.Convert, [N(@"C:\Fotky\dovolená.jpg", @"C:\Photos\vacation.jpg")], 1),
            ("wheel-image-tools", WheelMode.Click, WheelKind.Tools, [N(@"C:\Fotky\dovolená.jpg", @"C:\Photos\vacation.jpg")], 0),
            ("wheel-video-convert", WheelMode.Drag, WheelKind.Convert, [N(@"C:\Videa\klip.mov", @"C:\Videos\clip.mov")], 0),
            ("wheel-video-tools", WheelMode.Drag, WheelKind.Tools, [N(@"C:\Videa\klip.mov", @"C:\Videos\clip.mov")], 3),
            ("wheel-pdf-convert", WheelMode.Drag, WheelKind.Convert, [N(@"C:\Dokumenty\smlouva.pdf", @"C:\Documents\contract.pdf")], 2),
            ("wheel-pdf-tools", WheelMode.Click, WheelKind.Tools, [@"C:\a.pdf", @"C:\b.pdf"], 0),
            ("wheel-sheet-convert", WheelMode.Drag, WheelKind.Convert, [N(@"C:\Data\tabulka.xlsx", @"C:\Data\sales.xlsx")], -1),
            ("wheel-actions", WheelMode.Drag, WheelKind.Actions, [N(@"C:\Fotky\dovolená.jpg", @"C:\Photos\vacation.jpg")], 4),
            ("wheel-waiting-for-drop", WheelMode.Drag, WheelKind.Convert, [], -1),
        };
        foreach (var s in samples)
        {
            var window = new WheelWindow(s.Mode, s.Wheel, s.Files.Select(f => new FileItem(f)).ToList(), settings, Source, (_, _) => { });
            window.RenderPreview(Path.Combine(outDir, s.Name + ".png"), s.Highlight);
            window.Close();
            log.Add($"ok {Path.GetFileName(outDir)}/{s.Name}");
        }

        var settingsWindow = new SettingsWindow(settings, tools, () => { });
        SaveWindow(settingsWindow, Path.Combine(outDir, "settings.png"), 560);
        log.Add($"ok {Path.GetFileName(outDir)}/settings");

        var jobs = new JobsWindow();
        var running = jobs.Add(N("Převod na MP4", "Convert to MP4"), N("klip.mov", "clip.mov"), () => { }, @"C:\x.mov");
        running.Report(0.42);
        jobs.Add(N("Komprimovat", "Compress"), N("dovolená.jpg", "vacation.jpg"), () => { }, @"C:\x.jpg").Complete([N(@"C:\Fotky\dovolená (komprimováno).jpg", @"C:\Photos\vacation (compressed).jpg")], null);
        jobs.Add(N("Převod na DOCX", "Convert to DOCX"), N("smlouva.pdf", "contract.pdf"), () => { }, @"C:\x.pdf").Fail(N("Chybí nástroj Pandoc. Nainstalujte ho příkazem: ", "Pandoc is missing. Install it with: ") + "winget install JohnMacFarlane.Pandoc");
        SaveWindow(jobs, Path.Combine(outDir, "jobs.png"), 380, desktop: true);
        jobs.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/jobs");

        var trim = core.Single(o => o.Id == "video.trim");
        var dialog = new ParameterDialog(trim, [new FileItem(N(@"C:\Videa\klip.mov", @"C:\Videos\clip.mov"))]);
        SaveWindow(dialog, Path.Combine(outDir, "trim-dialog.png"), 360);
        log.Add($"ok {Path.GetFileName(outDir)}/parameter dialog");
    }

    private static void SaveWindow(Window window, string file, double width, bool desktop = false)
    {
        var content = (UIElement)window.Content;
        window.Content = null;
        var root = new Border
        {
            Width = width,
            Background = desktop ? Desktop() : window.Background ?? Brushes.White,
            Child = content,
        };
        if (content is ScrollViewer scroll)
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SaveElement(root, file);
    }

    /// <summary>A wallpaper-like backdrop for floating surfaces in previews.</summary>
    public static Brush Desktop() => Theme.IsDark
        ? new LinearGradientBrush(Color.FromRgb(0x1F, 0x2A, 0x44), Color.FromRgb(0x3A, 0x2A, 0x4A), 45)
        : new LinearGradientBrush(Color.FromRgb(0xC9, 0xD8, 0xEE), Color.FromRgb(0xE9, 0xD9, 0xEE), 45);

    public static void SaveElement(FrameworkElement element, string file)
    {
        element.Measure(new Size(element.Width > 0 ? element.Width : 800, double.PositiveInfinity));
        var size = element.DesiredSize;
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
