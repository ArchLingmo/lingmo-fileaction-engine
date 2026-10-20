using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileAction.Core;
using FileAction.Linux.Shell;
using static FileAction.Core.Loc;
using static FileAction.Linux.UI.Widgets;

namespace FileAction.Linux.UI;

public enum SettingsPage
{
    Presets,
    WatchFolders,
    About,
}

/// <summary>
/// Presets, watched folders and About (feedback, support). Opened with <c>fileaction --settings</c>,
/// from the launcher's "Settings" action or the button on the drop zone.
/// </summary>
public sealed class SettingsWindow : Window
{
    /// <summary>Donations go to this Solana address; the website shows it with a QR code.</summary>
    public const string SolanaAddress = "J7vmQqiFe6t3RSX9m8qCTFkxe6xDRzbPy5nMaeGNV7bL";

    private readonly LinuxSettings _settings;
    private readonly Func<IReadOnlyList<Operation>> _watchOperations;
    private readonly Action _saved;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private SettingsPage _page;

    /// <param name="watchOperations">What a watched folder can run (built-in operations and presets).</param>
    /// <param name="saved">Called after every change was saved (the wheels pick up new presets).</param>
    public SettingsWindow(LinuxSettings settings, Func<IReadOnlyList<Operation>> watchOperations, Action saved, SettingsPage page = SettingsPage.Presets)
    {
        _settings = settings;
        _watchOperations = watchOperations;
        _saved = saved;
        _page = page;
        ApplyLook(this);
        Title = T("FileAction – nastavení", "FileAction – settings");
        Width = 720;
        Height = 680;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _scroll;
        Rebuild();
    }

    /// <summary>Ready-made presets offered while the list is empty.</summary>
    private static Preset[] PresetTemplates =>
    [
        new() { Name = T("Web 1920", "Web 1920"), Kind = PresetKind.Image, Format = "jpg", Quality = 80, MaxSide = 1920, StripMetadata = true },
        new() { Name = "WebP 1280", Kind = PresetKind.Image, Format = "webp", Quality = 75, MaxSide = 1280 },
        new() { Name = T("Poloviční", "Half size"), Kind = PresetKind.Image, Format = Preset.SameFormat, Quality = 90, Percent = 50 },
        new() { Name = T("MP4 malé", "MP4 small"), Kind = PresetKind.Video, Format = "mp4", Height = 720, VideoQuality = VideoQuality.Small },
        new() { Name = "MP3 128k", Kind = PresetKind.Audio, Format = "mp3", Bitrate = 128 },
    ];

    public SettingsPage Page
    {
        get => _page;
        set
        {
            _page = value;
            Rebuild();
        }
    }

    private static string PageName(SettingsPage page) => page switch
    {
        SettingsPage.Presets => T("Předvolby", "Presets"),
        SettingsPage.WatchFolders => T("Sledované složky", "Watched folders"),
        _ => T("O aplikaci", "About"),
    };

    private void Rebuild()
    {
        Palette.Refresh();
        var root = new StackPanel { Margin = new Thickness(28, 20, 28, 28), MaxWidth = 760 };
        root.Children.Add(Segmented([.. Enum.GetValues<SettingsPage>().Select(p => (PageName(p), p))], _page, p => Page = p));
        switch (_page)
        {
            case SettingsPage.Presets:
                PresetsPage(root);
                break;
            case SettingsPage.WatchFolders:
                WatchFoldersPage(root);
                break;
            default:
                AboutPage(root);
                break;
        }
        _scroll.Content = root;
    }

    private void Save()
    {
        _settings.Save();
        WatchService.Sync(_settings);
        _saved();
    }

    private static void PageHeader(Panel root, string title, string intro)
    {
        var heading = Text(title, 24, FontWeight.SemiBold);
        heading.Margin = new Thickness(0, 22, 0, 0);
        root.Children.Add(heading);
        var text = Text(intro, 13.5, brush: Palette.Muted);
        text.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(text);
    }

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    // ------------------------------------------------------------------ presets

    private void PresetsPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Presets),
            T("Vlastní výseče s pevným nastavením, třeba „JPG 70 % max 1920 px“ nebo „MP4 720p“. Objeví se na kolech, ve sledovaných složkách i ve fileaction list/run.",
              "Slices of your own with fixed settings, like “JPG 70 % max 1920 px” or “MP4 720p”. They appear on the wheels, in watched folders and in fileaction list/run."));
        var add = Button(T("Nová předvolba", "New preset"), () => _ = EditPresetAsync(null), primary: true);
        add.Margin = new Thickness(0, 14, 0, 6);
        root.Children.Add(add);

        if (_settings.Presets.Count == 0)
        {
            root.Children.Add(SectionHeader(T("Rychlý start", "Quick start")));
            foreach (var template in PresetTemplates)
            {
                var t = template;
                root.Children.Add(Card(t.Name, PresetOperations.Summary(t), Button(T("Přidat", "Add"), () => AddPreset(t))));
            }
            return;
        }

        root.Children.Add(SectionHeader(T("Vaše předvolby", "Your presets")));
        foreach (var preset in _settings.Presets.ToList())
        {
            var p = preset;
            var wheel = p.Format == Preset.SameFormat ? WheelKind.Tools : WheelKind.Convert;
            var name = string.IsNullOrWhiteSpace(p.Name) ? PresetOperations.Summary(p) : p.Name;
            var used = _settings.WatchFolders.Count(f => f.OperationId == "preset." + p.Id);
            var delete = ConfirmButton(T("Smazat", "Delete"),
                used == 0 ? T("Opravdu smazat?", "Really delete?") : T($"Vypne {used}× sledovanou složku. Smazat?", $"Turns off {used} watched folder(s). Delete?"),
                () => DeletePreset(p));
            root.Children.Add(Card(name, $"{WheelWindow.WheelName(wheel)} · {PresetOperations.Summary(p)} · preset.{p.Id}",
                Row(Button(T("Upravit", "Edit"), () => _ = EditPresetAsync(p)), delete)));
        }
    }

    private void AddPreset(Preset preset)
    {
        _settings.Presets.Add(preset);
        Save();
        Rebuild();
    }

    private async Task EditPresetAsync(Preset? existing)
    {
        var dialog = new PresetWindow(existing);
        if (!await dialog.ShowDialog<bool>(this))
            return;
        var index = existing is null ? -1 : _settings.Presets.IndexOf(existing);
        if (index >= 0)
            _settings.Presets[index] = dialog.Result;
        else
            _settings.Presets.Add(dialog.Result);
        Save();
        Rebuild();
    }

    private void DeletePreset(Preset preset)
    {
        var id = "preset." + preset.Id;
        _settings.Presets.Remove(preset);
        _settings.HiddenOperations.Remove(id);
        _settings.FavoriteOperations.Remove(id);
        foreach (var folder in _settings.WatchFolders.Where(f => f.OperationId == id))
            folder.Enabled = false;
        Save();
        Rebuild();
    }

    // ------------------------------------------------------------------ watched folders

    private void WatchFoldersPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.WatchFolders),
            T("Co přibude do vybrané složky, to se samo zpracuje. Třeba snímky obrazovky do WebP nebo fotky z telefonu z HEIC do JPG.",
              "Whatever lands in a chosen folder is processed on its own. For example screenshots to WebP, or phone photos from HEIC to JPG."));
        var add = Button(T("Přidat složku", "Add folder"), () => _ = EditWatchFolderAsync(null), primary: true);
        add.Margin = new Thickness(0, 14, 0, 6);
        root.Children.Add(add);

        if (_settings.WatchFolders.Count > 0)
        {
            root.Children.Add(SectionHeader(T("Sledované složky", "Watched folders")));
            var operations = _watchOperations();
            foreach (var folder in _settings.WatchFolders.ToList())
            {
                var f = folder;
                var op = operations.FirstOrDefault(o => o.Id == f.OperationId);
                var what = op is null ? T("akce chybí", "action missing") : op.Label + (string.IsNullOrEmpty(op.Subtitle) ? "" : $" ({op.Subtitle})");
                var description = $"{f.Path} → {what}"
                    + (f.IncludeSubfolders ? T(" · i podsložky", " · with subfolders") : "")
                    + (Directory.Exists(f.Path) ? "" : T(" · složka neexistuje", " · folder not found"));
                var toggle = Toggle(f.Enabled, v =>
                {
                    f.Enabled = v;
                    Save();
                });
                var delete = ConfirmButton(T("Smazat", "Delete"), T("Opravdu smazat?", "Really delete?"), () =>
                {
                    _settings.WatchFolders.Remove(f);
                    Save();
                    Rebuild();
                });
                var name = Path.GetFileName(f.Path.TrimEnd('/'));
                root.Children.Add(Card(string.IsNullOrEmpty(name) ? f.Path : name, description,
                    Row(toggle, Button(T("Upravit", "Edit"), () => _ = EditWatchFolderAsync(f)), delete)));
            }
        }

        root.Children.Add(SectionHeader(T("Jak to funguje", "How it works")));
        root.Children.Add(Card(T("Jen nové soubory", "Only new files"),
            T("Soubor se zpracuje, až je celý stažený nebo zkopírovaný. Co ve složce už bylo, zůstane, jak je. Výsledky FileAction se znovu nezpracovávají.",
              "A file is processed once it has finished downloading or copying. What was already in the folder stays as it is. FileAction's own results are not processed again.")));
        var running = WatchService.IsRunning();
        root.Children.Add(Card(T("Na pozadí", "In the background"),
            T("Složky sleduje proces „fileaction --watch“, který se spouští s přihlášením (~/.config/autostart), dokud je zapnutá aspoň jedna složka. Výsledky se ukládají vedle originálů, originály zůstávají beze změny.",
              "Folders are watched by a “fileaction --watch” process that starts when you log in (~/.config/autostart) while at least one folder is on. Results are saved next to the originals, which are never changed.")
            + " " + (running ? T("Právě běží.", "It is running now.") : T("Teď neběží.", "It is not running now."))));
    }

    private async Task EditWatchFolderAsync(WatchFolder? existing)
    {
        var dialog = new WatchFolderWindow(existing, _watchOperations());
        if (!await dialog.ShowDialog<bool>(this))
            return;
        var index = existing is null ? -1 : _settings.WatchFolders.IndexOf(existing);
        if (index >= 0)
            _settings.WatchFolders[index] = dialog.Result;
        else
            _settings.WatchFolders.Add(dialog.Result);
        Save();
        Rebuild();
    }

    // ------------------------------------------------------------------ about

    private void AboutPage(Panel root)
    {
        PageHeader(root, "FileAction " + Cli.Version,
            T("Převod souborů puštěním na kolo. Vše běží lokálně, soubory se nikam neodesílají.",
              "Convert files by dropping them on a wheel. Everything runs locally, files never leave your computer."));
        root.Children.Add(SectionHeader(T("Zpětná vazba", "Feedback")));
        root.Children.Add(Card(T("Napsat autorovi", "Write to the author"),
            T("Chyba, nápad nebo otázka. Zpráva dorazí přímo autorovi.", "A bug, an idea or a question. Your message goes straight to the author."),
            Button(T("Napsat zpětnou vazbu", "Send feedback"), () => new FeedbackWindow().Show(this), primary: true)));
        root.Children.Add(Card(T("Web", "Website"), "fileaction.apexpiksel.cz",
            Button(T("Otevřít", "Open"), () => Desktop.Open(FeedbackWindow.Website))));
        root.Children.Add(SectionHeader(T("Podpora", "Support")));
        root.Children.Add(SupportCard());
    }

    private Border SupportCard()
    {
        var address = new TextBox
        {
            Text = SolanaAddress,
            IsReadOnly = true,
            FontFamily = new FontFamily("DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace"),
            FontSize = 12.5,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var copied = Text("", 12.5, brush: Palette.Success, wrap: false);
        copied.VerticalAlignment = VerticalAlignment.Center;
        var copy = Button(T("Kopírovat adresu", "Copy address"), () => _ = CopyAsync(), primary: true);
        async Task CopyAsync()
        {
            if (Clipboard is { } clipboard)
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, SolanaAddress);
                copied.Text = T("Zkopírováno", "Copied");
            }
            else
            {
                // No clipboard (unusual session): the address can still be selected by hand.
                address.Focus();
                address.SelectAll();
            }
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), ItemSpacing = 8, LineSpacing = 8 };
        buttons.Children.Add(copy);
        buttons.Children.Add(Button(T("QR kód na webu", "QR code on the website"), () => Desktop.Open(FeedbackWindow.Website + "/#support")));
        buttons.Children.Add(Button(T("Kartou přes Ko-fi", "Card via Ko-fi"), () => Desktop.Open("https://ko-fi.com/metamorfolog")));
        buttons.Children.Add(copied);
        var below = new StackPanel();
        below.Children.Add(address);
        below.Children.Add(buttons);
        return Card(T("Podpořit vývoj", "Support development"),
            T("FileAction je zdarma a zdarma zůstane. Pokud vám šetří čas, pomůže dar přes Solanu (SOL nebo USDC) nebo kartou na Ko-fi.",
              "FileAction is free and will stay free. If it saves you time, a donation via Solana (SOL or USDC) or by card on Ko-fi helps."),
            null, below);
    }
}
