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
            var settings = new AppSettings();
            var tools = new ToolLocator();
            var core = Catalog.Build();
            var actions = FileActions.Build(settings, () => { });
            IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) =>
                Catalog.For(wheel == WheelKind.Actions ? actions : core, wheel, items, tools);

            var samples = new (string Name, WheelMode Mode, WheelKind Wheel, string[] Files, int Highlight)[]
            {
                ("wheel-image-convert", WheelMode.Drag, WheelKind.Convert, [@"C:\Fotky\dovolená.jpg"], 1),
                ("wheel-image-tools", WheelMode.Click, WheelKind.Tools, [@"C:\Fotky\dovolená.jpg"], 0),
                ("wheel-video-convert", WheelMode.Drag, WheelKind.Convert, [@"C:\Videa\klip.mov"], 0),
                ("wheel-video-tools", WheelMode.Drag, WheelKind.Tools, [@"C:\Videa\klip.mov"], 3),
                ("wheel-pdf-convert", WheelMode.Drag, WheelKind.Convert, [@"C:\Dokumenty\smlouva.pdf"], 2),
                ("wheel-pdf-tools", WheelMode.Click, WheelKind.Tools, [@"C:\a.pdf", @"C:\b.pdf"], 0),
                ("wheel-sheet-convert", WheelMode.Drag, WheelKind.Convert, [@"C:\Data\tabulka.xlsx"], -1),
                ("wheel-actions", WheelMode.Drag, WheelKind.Actions, [@"C:\Fotky\dovolená.jpg"], 4),
                ("wheel-waiting-for-drop", WheelMode.Drag, WheelKind.Convert, [], -1),
            };
            foreach (var s in samples)
            {
                var window = new WheelWindow(s.Mode, s.Wheel, s.Files.Select(f => new FileItem(f)).ToList(), settings, Source, (_, _) => { });
                window.RenderPreview(Path.Combine(outDir, s.Name + ".png"), s.Highlight);
                window.Close();
                log.Add($"ok {s.Name}");
            }

            var settingsWindow = new SettingsWindow(settings, tools, () => { });
            SaveWindow(settingsWindow, Path.Combine(outDir, "settings.png"), 560);
            log.Add("ok settings");

            var jobs = new JobsWindow();
            var running = jobs.Add("Převod na MP4", "klip.mov", () => { });
            running.Report(0.42);
            jobs.Add("Komprimovat", "dovolená.jpg", () => { }).Complete([@"C:\Fotky\dovolená (komprimováno).jpg"], null);
            jobs.Add("Převod na DOCX", "smlouva.pdf", () => { }).Fail("Chybí nástroj Pandoc. Nainstalujte ho příkazem: winget install JohnMacFarlane.Pandoc");
            SaveWindow(jobs, Path.Combine(outDir, "jobs.png"), 360, dark: true);
            jobs.Close();
            log.Add("ok jobs");

            var trim = core.Single(o => o.Id == "video.trim");
            var dialog = new ParameterDialog(trim, [new FileItem(@"C:\Videa\klip.mov")]);
            SaveWindow(dialog, Path.Combine(outDir, "trim-dialog.png"), 360);
            log.Add("ok parameter dialog");

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

    private static void SaveWindow(Window window, string file, double width, bool dark = false)
    {
        var content = (UIElement)window.Content;
        window.Content = null;
        var root = new Border
        {
            Width = width,
            Background = dark ? new SolidColorBrush(Color.FromRgb(0x2F, 0x4F, 0x6F)) : window.Background ?? Brushes.White,
            Child = content,
        };
        if (content is ScrollViewer scroll)
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SaveElement(root, file);
    }

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
