using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FileAction.Core;

namespace FileAction.Linux;

public enum StartMode
{
    Wheel,
    Settings,
    Watch,
}

public sealed class App : Application
{
    public static IReadOnlyList<FileItem> StartupItems { get; set; } = [];
    public static StartMode Mode { get; set; }

    /// <summary>Fluent controls (switches, sliders, text fields) in the app's orange rather than the theme's blue.</summary>
    public override void Initialize() => Styles.Add(new FluentTheme
    {
        Palettes =
        {
            [ThemeVariant.Light] = new ColorPaletteResources { Accent = UI.Palette.AccentColor },
            [ThemeVariant.Dark] = new ColorPaletteResources { Accent = UI.Palette.AccentColor },
        },
    });

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = LinuxSettings.Load();
            var controller = new Controller(settings, desktop);
            switch (Mode)
            {
                case StartMode.Settings:
                    controller.OpenSettings();
                    break;
                case StartMode.Watch:
                    controller.Watch();
                    break;
                default:
                    var wheel = controller.CreateWheel(StartupItems);
                    wheel.Show();
                    wheel.Activate();
                    break;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    public static AppBuilder BuildAvaloniaApp() =>
        WithBundledFont(AppBuilder.Configure<App>().UsePlatformDetect());

    /// <summary>
    /// Uses the bundled Inter font as the default, so the app doesn't depend on which fonts the system has
    /// (a minimal install may have none, and Avalonia then refuses to start).
    /// </summary>
    public static AppBuilder WithBundledFont(AppBuilder builder) =>
        builder.WithInterFont().With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" });

    /// <summary>Held open for the life of <c>fileaction --watch</c>.</summary>
    private static FileStream? _watchLock;

    [STAThread]
    public static int Main(string[] args)
    {
        var settings = LinuxSettings.Load();
        Loc.Current = settings.Language ?? Loc.Detect();

        if (args.Length > 0 && Cli.Commands.Contains(args[0]))
            return Cli.Run(args);
        if (args.Length > 0 && args[0] == "--self-test")
            return SelfTest.Run(args.Length > 1 ? args[1] : "self-test");
        if (args.Length > 0 && args[0] == "--settings")
        {
            App.Mode = StartMode.Settings;
        }
        else if (args.Length > 0 && args[0] == "--watch")
        {
            // One watcher per user; this one quits when another already runs.
            _watchLock = Shell.WatchService.TryLock();
            if (_watchLock is null)
                return 0;
            App.Mode = StartMode.Watch;
        }

        App.StartupItems = args
            .Where(a => !a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => a.StartsWith("file://", StringComparison.Ordinal) ? new Uri(a).LocalPath : Path.GetFullPath(a))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Select(p => new FileItem(p))
            .ToList();
        var exit = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        _watchLock?.Dispose();
        return exit;
    }
}
