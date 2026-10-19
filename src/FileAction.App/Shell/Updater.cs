using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell;

/// <summary>
/// Over-the-air updates from GitHub releases. Asks GitHub for the latest release now and then (no data about
/// your files is sent), downloads the installer, checks its SHA-256 and runs it silently; the installer then
/// starts the new version. Portable copies (no uninstaller next to the exe) get the release page instead.
/// </summary>
internal sealed class Updater
{
    private static readonly HttpClient Http = CreateHttp();
    private DispatcherTimer? _timer;
    private Version? _announced;

    public static Version Current => UpdateInfo.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Installed with the setup (which can update in place) rather than unzipped somewhere.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public UpdateInfo? Available { get; private set; }

    public DateTime? LastChecked { get; private set; }

    public string? LastError { get; private set; }

    public bool Installing { get; private set; }

    public event Action? Changed;

    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(UpdateInfo.LatestReleaseApi);
            var latest = UpdateInfo.Parse(json);
            Available = latest is not null && latest.IsNewerThan(Current) ? latest : null;
            LastError = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            LastError = ex.Message;
        }
        LastChecked = DateTime.Now;
        Changed?.Invoke();
        return Available;
    }

    /// <summary>Checks shortly after start and then every few hours while enabled.</summary>
    public void StartBackgroundChecks(AppSettings settings, Action<UpdateInfo> found)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(6);
            if (!settings.CheckForUpdates || Installing)
                return;
            // Announce each new version once (a later release is announced again).
            if (await CheckAsync() is { } update && _announced != update.Version)
            {
                _announced = update.Version;
                found(update);
            }
        };
        _timer.Start();
    }

    public async Task InstallAsync(UpdateInfo update)
    {
        if (Installing)
            return;
        if (!IsInstalled || update.InstallerUrl is null)
        {
            Open(update.PageUrl);
            return;
        }

        Installing = true;
        Changed?.Invoke();
        var target = Path.Combine(Path.GetTempPath(), $"FileAction-Setup-{update.Version}.exe");
        try
        {
            Notifications.Show(T("Stahuji aktualizaci…", "Downloading the update…"), $"FileAction {update.Version}");
            // Never run an installer that can't be checked.
            var expected = update.InstallerSha256 ?? throw new InvalidOperationException(
                T("U vydání chybí kontrolní otisk, aktualizaci stáhněte ručně z webu.", "The release has no checksum; download the update from the website."));
            // The whole download, body included, must finish in time (HttpClient's timeout only covers the headers here).
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15)))
            using (var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var output = File.Create(target);
                await input.CopyToAsync(output, timeout.Token);
            }

            await using (var file = File.OpenRead(target))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
                if (actual != expected)
                    throw new InvalidOperationException(T("Stažený instalátor nesouhlasí s otiskem z GitHubu.", "The downloaded installer doesn't match the checksum published on GitHub."));
            }

            // The installer closes this app, installs over it and starts the new version (see installer\FileAction.iss).
            Process.Start(new ProcessStartInfo(target, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Installing = false;
            Changed?.Invoke();
            OutputPaths.TryDelete(target);
            Notifications.Show(T("Aktualizace se nepovedla", "Update failed"), ex.Message, error: true);
        }
    }

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FileAction/{Current}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
