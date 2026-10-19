using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FileAction.App.Input;
using FileAction.App.Shell;
using FileAction.Core;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>The settings pages shown in the sidebar.</summary>
internal enum SettingsPage
{
    General,
    Gestures,
    Wheels,
    Quality,
    Helpers,
    About,
}

/// <summary>Settings with a sidebar of pages; each page is a column of rounded cards.</summary>
internal sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    /// <summary>Set by the app: the update checker and the operations the wheel page lists.</summary>
    public static Shell.Updater? Updater { get; set; }

    public static Func<IReadOnlyList<Operation>>? Operations { get; set; }

    /// <summary>Downloads FFmpeg or Pandoc in the app (set by the app).</summary>
    public static Func<ExternalTool, Task<bool>>? DownloadTool { get; set; }
    private readonly AppSettings _settings;
    private readonly ToolLocator _tools;
    private readonly Action _changed;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private SettingsPage _page = SettingsPage.General;

    internal SettingsWindow(AppSettings settings, ToolLocator tools, Action changed)
    {
        _settings = settings;
        _tools = tools;
        _changed = changed;
        Theme.Refresh();
        Title = T("FileAction – nastavení", "FileAction – settings");
        Width = 1180;
        Height = 820;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/FileAction.ico"));
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Build();
        Action refresh = () => Dispatcher.BeginInvoke(Rebuild);
        if (Updater is not null)
            Updater.Changed += refresh;
        Closed += (_, _) =>
        {
            _open = null;
            if (Updater is not null)
                Updater.Changed -= refresh;
        };
    }

    public static void ShowSingle(AppSettings settings, ToolLocator tools, Action changed, SettingsPage? page = null)
    {
        _open ??= new SettingsWindow(settings, tools, changed);
        if (page is { } p)
            _open.Page = p;
        _open.Show();
        _open.Activate();
    }

    /// <summary>Redraws the open settings window after the app changed a setting itself.</summary>
    public static void RefreshIfOpen() => _open?.Rebuild();

    /// <summary>The page on screen; setting it switches pages.</summary>
    public SettingsPage Page
    {
        get => _page;
        set
        {
            _page = value;
            Build();
            _scroll.ScrollToTop();
        }
    }

    /// <summary>Lets the self-test render a whole page instead of the visible part.</summary>
    internal void ExpandForPreview() => _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

    private void Save()
    {
        _settings.Save();
        _changed();
    }

    /// <summary>Rebuilds the page (used when one change affects others), keeping the scroll position.</summary>
    private void Rebuild()
    {
        var offset = _scroll.VerticalOffset;
        Build();
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void Build()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = Sidebar();
        grid.Children.Add(sidebar);

        var page = new StackPanel { Margin = new Thickness(40, 34, 40, 40), MaxWidth = _page == SettingsPage.Wheels ? 1100 : 760, HorizontalAlignment = HorizontalAlignment.Left };
        switch (_page)
        {
            case SettingsPage.General:
                GeneralPage(page);
                break;
            case SettingsPage.Gestures:
                GesturesPage(page);
                break;
            case SettingsPage.Wheels:
                WheelsPage(page);
                break;
            case SettingsPage.Quality:
                QualityPage(page);
                break;
            case SettingsPage.Helpers:
                HelpersPage(page);
                break;
            default:
                AboutPage(page);
                break;
        }
        _scroll.Content = page;
        // The scroll viewer outlives each rebuild (it keeps the position); free it from the old layout.
        (_scroll.Parent as Grid)?.Children.Remove(_scroll);
        Grid.SetColumn(_scroll, 1);
        grid.Children.Add(_scroll);
        Content = grid;
    }

    private static string PageName(SettingsPage page) => page switch
    {
        SettingsPage.General => T("Obecné", "General"),
        SettingsPage.Gestures => T("Gesta a zkratky", "Gestures & keys"),
        SettingsPage.Wheels => T("Kola", "Wheels"),
        SettingsPage.Quality => T("Kvalita", "Quality"),
        SettingsPage.Helpers => T("Pomocné programy", "Helper programs"),
        _ => T("O aplikaci", "About"),
    };

    private static string PageGlyph(SettingsPage page) => page switch
    {
        SettingsPage.General => "",
        SettingsPage.Gestures => "",
        SettingsPage.Wheels => "",
        SettingsPage.Quality => "",
        SettingsPage.Helpers => "",
        _ => "",
    };

    // ------------------------------------------------------------------ sidebar

    private Border Sidebar()
    {
        var dock = new DockPanel { Margin = new Thickness(18, 24, 18, 20) };

        var brand = new DockPanel { Margin = new Thickness(8, 0, 0, 26) };
        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/FileAction.png")),
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 0, 12, 0),
        };
        brand.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Text("FileAction", 19, FontWeights.SemiBold);
        name.FontFamily = Theme.DisplayFont;
        names.Children.Add(name);
        names.Children.Add(Text(T("Chyť, ťukni, pusť.", "Grab, tap, drop."), 12, brush: Theme.Muted));
        brand.Children.Add(names);
        DockPanel.SetDock(brand, Dock.Top);
        dock.Children.Add(brand);

        var version = Text(T("Verze ", "Version ") + Shell.Updater.Current, 12, brush: Theme.Muted);
        version.Margin = new Thickness(10, 12, 0, 0);
        DockPanel.SetDock(version, Dock.Bottom);
        dock.Children.Add(version);

        var nav = new StackPanel();
        foreach (var page in Enum.GetValues<SettingsPage>())
            nav.Children.Add(NavItem(page));
        dock.Children.Add(nav);

        return new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = dock,
        };
    }

    private Border NavItem(SettingsPage page)
    {
        var active = page == _page;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = PageGlyph(page),
            FontFamily = Theme.Icons,
            FontSize = 16,
            Foreground = active ? Theme.Accent : Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        });
        row.Children.Add(Text(PageName(page), 14, active ? FontWeights.SemiBold : FontWeights.Normal, wrap: false));
        var item = new Border
        {
            Background = active ? Theme.AccentSoft : System.Windows.Media.Brushes.Transparent,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = Cursors.Hand,
            Child = row,
        };
        if (!active)
        {
            item.MouseEnter += (_, _) => item.Background = Theme.Hover;
            item.MouseLeave += (_, _) => item.Background = System.Windows.Media.Brushes.Transparent;
        }
        item.MouseLeftButtonUp += (_, _) => Page = page;
        return item;
    }

    private static void PageHeader(Panel page, string title, string subtitle)
    {
        var t = Text(title, 28, FontWeights.SemiBold);
        t.FontFamily = Theme.DisplayFont;
        page.Children.Add(t);
        var s = Text(subtitle, 13.5, brush: Theme.Muted);
        s.Margin = new Thickness(0, 4, 0, 6);
        page.Children.Add(s);
    }

    // ------------------------------------------------------------------ pages

    private void GeneralPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.General), T("Jazyk, kam se ukládají výsledky a spouštění s Windows.", "Language, where results go and starting with Windows."));

        root.Children.Add(SectionHeader(T("Jazyk", "Language")));
        root.Children.Add(Card(T("Jazyk aplikace", "App language"), null,
            Segmented<Core.Language?>(
                [(T("Podle Windows", "Windows"), null), ("Čeština", Core.Language.Czech), ("English", Core.Language.English)],
                _settings.Language,
                value =>
                {
                    _settings.Language = value;
                    Save();
                    // Rebuild the window in the new language.
                    Title = T("FileAction – nastavení", "FileAction – settings");
                    Rebuild();
                }),
            glyph: ""));

        root.Children.Add(SectionHeader(T("Výsledky", "Results")));
        var folderLine = Text(_settings.OutputDirectory ?? "", 12, brush: Theme.Muted, wrap: false);
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        folderRow.Children.Add(Button(T("Vybrat složku…", "Choose folder…"), PickFolder, glyph: Glyphs.Folder));
        folderLine.Margin = new Thickness(12, 0, 0, 0);
        folderLine.VerticalAlignment = VerticalAlignment.Center;
        folderRow.Children.Add(folderLine);
        var toFolder = !string.IsNullOrWhiteSpace(_settings.OutputDirectory);
        var below = new StackPanel();
        below.Children.Add(Wrap(Segmented(
            [(T("Vedle originálu", "Next to the original"), false), (T("Do složky", "To a folder"), true)],
            toFolder,
            value =>
            {
                if (value)
                {
                    PickFolder();
                }
                else
                {
                    _settings.OutputDirectory = null;
                    Save();
                    Rebuild();
                }
            })));
        if (toFolder)
            below.Children.Add(folderRow);
        root.Children.Add(Card(T("Kam ukládat", "Save results"), T("Originál zůstává vždy beze změny.", "The original file is never changed."), null, below, Glyphs.Folder));
        root.Children.Add(Card(T("Po dokončení ukázat ve složce", "Show in folder when done"), null,
            Toggle(_settings.RevealResult, v =>
            {
                _settings.RevealResult = v;
                Save();
            }), glyph: ""));

        root.Children.Add(SectionHeader(T("Systém", "System")));
        root.Children.Add(Card(T("Spouštět po přihlášení", "Start when I sign in"), null,
            Toggle(ShellIntegration.IsAutostartEnabled, v => Apply(() => ShellIntegration.SetAutostart(v))), glyph: ""));
        root.Children.Add(Card(T("Kontextová nabídka Průzkumníka", "File Explorer context menu"), T("Položka „FileAction…“ po kliknutí pravým tlačítkem", "A “FileAction…” item on right-click"),
            Toggle(ShellIntegration.IsContextMenuEnabled, v => Apply(() => ShellIntegration.SetContextMenu(v))), glyph: ""));
    }

    private void GesturesPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Gestures), T("Která klávesa otevře které kolo a zkratka bez myši.", "Which key opens which wheel, and a shortcut without the mouse."));

        root.Children.Add(SectionHeader(T("Gesto", "Gesture")));
        root.Children.Add(Card(
            T("Chyťte soubor a během tažení ťukněte na klávesu", "Grab a file and tap a key while dragging"),
            T("Otevře se kolo; soubor pusťte na výseč. Do středu = zrušit. Stejná klávesa znovu (nebo R) zopakuje poslední akci.",
              "A wheel opens; drop the file on a slice. Drop in the middle to cancel. The same key again (or R) repeats the last action."),
            null, glyph: Glyphs.Extract));
        foreach (var (wheel, description) in new[]
                 {
                     (WheelKind.Convert, T("Převod na jiný formát", "Convert to another format")),
                     (WheelKind.Tools, T("Komprimovat, zmenšit, oříznout, spojit…", "Compress, shrink, trim, merge…")),
                     (WheelKind.Actions, T("Kopírovat, přesunout, cesta, terminál, sdílet…", "Copy, move, path, terminal, share…")),
                 })
        {
            root.Children.Add(Card(AppSettings.WheelName(wheel), description, KeyChooser(wheel), glyph: WheelGlyph(wheel)));
        }
        root.Children.Add(Card(T("Pozastavit gesta", "Pause gestures"), T("Kolo se při tažení neotevře.", "The wheel won’t open while dragging."),
            Toggle(_settings.Paused, v =>
            {
                _settings.Paused = v;
                Save();
            }), glyph: ""));

        root.Children.Add(SectionHeader(T("Klávesnice", "Keyboard")));
        root.Children.Add(Card(T("Zkratka pro výběr v Průzkumníku", "Shortcut for the File Explorer selection"),
            T("Šipky vyberou výseč, Enter potvrdí, Tab přepne kolo, Esc zavře.", "Arrows pick a slice, Enter runs it, Tab switches wheels, Esc closes."),
            null,
            below: Wrap(Segmented(
                Enum.GetValues<HotkeyPreset>().Where(p => p != HotkeyPreset.None).Append(HotkeyPreset.None)
                    .Select(p => (AppSettings.HotkeyName(p), p)).ToList(),
                _settings.Hotkey,
                value =>
                {
                    _settings.Hotkey = value;
                    Save();
                })),
            glyph: ""));
    }

    private static string WheelGlyph(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => "",
        WheelKind.Tools => "",
        _ => "",
    };

    private void QualityPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Quality), T("Poměr kvality a velikosti výsledných souborů.", "The balance between quality and file size."));
        root.Children.Add(SectionHeader(T("Obrázky", "Images")));
        root.Children.Add(Card(T("JPG, WebP a AVIF", "JPG, WebP and AVIF"), T("Kvalita při převodu obrázků", "Quality when converting images"),
            Slider(50, 100, _settings.Quality.JpegQuality, v => _settings.Quality.JpegQuality = v, Save), glyph: Glyphs.Photo));
        root.Children.Add(Card(T("Komprimovat", "Compress"), T("Kvalita nástroje Komprimovat", "Quality of the Compress tool"),
            Slider(30, 95, _settings.Quality.CompressQuality, v => _settings.Quality.CompressQuality = v, Save), glyph: Glyphs.Shrink));
        root.Children.Add(SectionHeader("PDF"));
        root.Children.Add(Card(T("PDF na obrázky", "PDF to images"), T("Rozlišení v DPI", "Resolution in DPI"),
            Slider(72, 600, _settings.Quality.PdfDpi, v => _settings.Quality.PdfDpi = v, Save), glyph: Glyphs.Page));
    }

    private void HelpersPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Helpers), T("Programy, které FileAction používá pro audio, video a dokumenty.", "Programs FileAction uses for audio, video and documents."));
        root.Children.Add(SectionHeader(T("Vestavěné", "Built in")));
        root.Children.Add(Card(T("Co zvládá FileAction sám", "Built in"),
            T("Obrázky, PDF, tabulky, titulky a archivy. Audio a video potřebují FFmpeg, dokumenty Pandoc a soubory Office LibreOffice.",
              "Images, PDFs, spreadsheets, subtitles and archives. Audio and video need FFmpeg, documents need Pandoc and Office files need LibreOffice."),
            Button(T("Zkontrolovat znovu", "Check again"), () =>
            {
                _tools.Refresh();
                Rebuild();
            }, glyph: ""),
            glyph: ""));
        root.Children.Add(SectionHeader(T("Pomocné programy", "Helper programs")));
        foreach (var tool in Enum.GetValues<ExternalTool>())
            root.Children.Add(ToolCard(tool));
    }

    private void AboutPage(Panel root)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/FileAction.png")),
            Width = 64,
            Height = 64,
            Margin = new Thickness(0, 0, 20, 0),
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text("FileAction", 28, FontWeights.SemiBold);
        title.FontFamily = Theme.DisplayFont;
        text.Children.Add(title);
        text.Children.Add(Text(T("Chyťte soubor, ťukněte na klávesu, pusťte ho na formát.", "Grab a file, tap a key, drop it on a format."), 13.5, brush: Theme.Muted));
        header.Children.Add(text);
        root.Children.Add(header);

        root.Children.Add(SectionHeader(T("Aktualizace", "Updates")));
        root.Children.Add(UpdateCard());
        root.Children.Add(Card(T("Kontrolovat aktualizace automaticky", "Check for updates automatically"),
            T("Občas se zeptá GitHubu na nejnovější vydání. O vašich souborech se nic neodesílá.", "Now and then asks GitHub for the latest release. Nothing about your files is sent."),
            Toggle(_settings.CheckForUpdates, v =>
            {
                _settings.CheckForUpdates = v;
                Save();
            }), glyph: ""));

        root.Children.Add(SectionHeader(T("Zpětná vazba", "Feedback")));
        root.Children.Add(Card(T("Napsat zpětnou vazbu", "Send feedback"),
            T("Chyba, nápad nebo otázka. Zpráva dorazí přímo autorovi.", "A bug, an idea or a question. It goes straight to the author."),
            Button(T("Napsat…", "Write…"), FeedbackWindow.ShowSingle, primary: true, glyph: ""), glyph: ""));

        root.Children.Add(SectionHeader(T("Odkazy", "Links")));
        root.Children.Add(Card(T("Web", "Website"), "fileaction.apexpiksel.cz",
            Button(T("Otevřít", "Open"), () => Shell.Updater.Open(FeedbackWindow.Website), glyph: ""), glyph: ""));
        root.Children.Add(Card(T("Zdrojový kód", "Source code"), "github.com/" + UpdateInfo.Repository,
            Button(T("Otevřít", "Open"), () => Shell.Updater.Open("https://github.com/" + UpdateInfo.Repository), glyph: ""), glyph: ""));

        root.Children.Add(SectionHeader(T("Podpora", "Support")));
        root.Children.Add(SupportCard());

        var footer = Text(T("Vše běží lokálně, soubory se nikam neodesílají.", "Everything runs locally, files never leave your PC."), 12, brush: Theme.Muted);
        footer.Margin = new Thickness(2, 24, 0, 0);
        root.Children.Add(footer);
    }

    /// <summary>Donations go to this Solana address; the website shows it with a QR code.</summary>
    private const string SolanaAddress = "J7vmQqiFe6t3RSX9m8qCTFkxe6xDRzbPy5nMaeGNV7bL";

    private static Border SupportCard()
    {
        var address = new TextBox
        {
            Text = SolanaAddress,
            IsReadOnly = true,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12.5,
            Padding = new Thickness(8, 6, 8, 6),
            Background = Theme.Page,
            Foreground = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var copied = Text("", 12.5, brush: Theme.Success, wrap: false);
        copied.VerticalAlignment = VerticalAlignment.Center;
        copied.Margin = new Thickness(12, 0, 0, 0);
        var copy = Button(T("Kopírovat adresu", "Copy address"), () =>
        {
            try
            {
                Clipboard.SetText(SolanaAddress);
                copied.Text = T("Zkopírováno", "Copied");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard is busy in another app; the address can still be selected by hand.
                address.Focus();
                address.SelectAll();
            }
        }, primary: true, glyph: "");
        var web = Button(T("QR kód na webu", "QR code on the website"), () => Shell.Updater.Open(FeedbackWindow.Website + "/#support"), glyph: "");
        web.Margin = new Thickness(8, 0, 0, 0);
        var kofi = Button(T("Kartou přes Ko-fi", "Card via Ko-fi"), () => Shell.Updater.Open("https://ko-fi.com/metamorfolog"), glyph: "");
        kofi.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(copy);
        buttons.Children.Add(web);
        buttons.Children.Add(kofi);
        buttons.Children.Add(copied);
        var below = new StackPanel();
        below.Children.Add(address);
        below.Children.Add(buttons);
        return Card(T("Podpořit vývoj", "Support development"),
            T("FileAction je zdarma a zdarma zůstane. Pokud vám šetří čas, pomůže dar přes Solanu (SOL nebo USDC) nebo kartou na Ko-fi.",
              "FileAction is free and will stay free. If it saves you time, a donation via Solana (SOL or USDC) or by card on Ko-fi helps."),
            null, below, glyph: "");
    }

    // ------------------------------------------------------------------ helpers

    private static FrameworkElement Wrap(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.Margin = new Thickness(0, 12, 0, 0);
        return element;
    }

    private Border KeyChooser(WheelKind wheel) => Segmented(
        [("Shift", ModifierKey.Shift), ("Ctrl", ModifierKey.Ctrl), ("Alt", ModifierKey.Alt), (T("Vyp.", "Off"), ModifierKey.None)],
        _settings.KeyFor(wheel),
        key =>
        {
            var old = _settings.KeyFor(wheel);
            // Keep keys unique: the wheel that had this key gets the old one.
            foreach (var other in new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions })
            {
                if (other != wheel && key != ModifierKey.None && _settings.KeyFor(other) == key)
                    SetKey(other, old);
            }
            SetKey(wheel, key);
            Save();
            Rebuild();
        });

    private void SetKey(WheelKind wheel, ModifierKey key)
    {
        switch (wheel)
        {
            case WheelKind.Convert:
                _settings.ConvertKey = key;
                break;
            case WheelKind.Tools:
                _settings.ToolsKey = key;
                break;
            default:
                _settings.ActionsKey = key;
                break;
        }
    }

    private void PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("Výstupní složka", "Output folder") };
        if (dialog.ShowDialog(this) == true)
        {
            _settings.OutputDirectory = dialog.FolderName;
            Save();
        }
        Rebuild();
    }

    private void Apply(Action action)
    {
        try
        {
            action();
            Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FileAction", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Border UpdateCard()
    {
        var updater = Updater;
        var current = Shell.Updater.Current;
        string status;
        UIElement? action = null;
        if (updater is null)
        {
            status = "";
        }
        else if (updater.Installing)
        {
            status = T("Stahuji a instaluji…", "Downloading and installing…");
        }
        else if (updater.Available is { } update)
        {
            status = T($"K dispozici je verze {update.Version}.", $"Version {update.Version} is available.");
            action = Button(Shell.Updater.IsInstalled ? T("Aktualizovat", "Update") : T("Stáhnout", "Download"),
                () => _ = updater.InstallAsync(update), primary: true, glyph: "");
        }
        else
        {
            status = updater.LastError is not null ? T("Kontrola se nepovedla: ", "Check failed: ") + updater.LastError
                : updater.LastChecked is { } at ? T($"Máte nejnovější verzi (kontrola {at:HH:mm}).", $"You're up to date (checked {at:HH:mm}).")
                : T("Zatím nezkontrolováno.", "Not checked yet.");
            action = Button(T("Zkontrolovat", "Check now"), async () =>
            {
                await updater.CheckAsync();
                Rebuild();
            }, glyph: "");
        }
        return Card($"FileAction {current}", status, action, glyph: "");
    }

    private Border ToolCard(ExternalTool tool)
    {
        var path = _tools.Find(tool);
        var description = path ?? tool switch
        {
            ExternalTool.Ffmpeg => T("Audio a video", "Audio and video"),
            ExternalTool.Pandoc => T("Dokumenty (DOCX, Markdown, EPUB…)", "Documents (DOCX, Markdown, EPUB…)"),
            _ => T("DOC, PPTX, ODS a převod do PDF", "DOC, PPTX, ODS and conversion to PDF"),
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Badge(path is null ? T("chybí", "missing") : T("nalezeno", "found"), path is not null));
        if (path is null)
        {
            var id = tool switch
            {
                ExternalTool.Ffmpeg => "Gyan.FFmpeg",
                ExternalTool.Pandoc => "JohnMacFarlane.Pandoc",
                _ => "TheDocumentFoundation.LibreOffice",
            };
            var install = Shell.ToolInstaller.CanInstall(tool) && DownloadTool is not null
                ? Button(T($"Stáhnout ({Shell.ToolInstaller.DownloadSize(tool)})", $"Download ({Shell.ToolInstaller.DownloadSize(tool)})"), async () =>
                {
                    if (await DownloadTool(tool))
                        Rebuild();
                }, primary: true, glyph: "")
                : Button(T("Nainstalovat", "Install"), () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe",
                        $"/c winget install -e --id {id} --accept-source-agreements --accept-package-agreements & echo. & echo {T("Hotovo. Ve FileAction kliknete na Zkontrolovat znovu.", "Done. Click Check again in FileAction.")} & pause")
                    { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "FileAction", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }, primary: true, glyph: "");
            install.Margin = new Thickness(10, 0, 0, 0);
            right.Children.Add(install);
        }
        var glyph = tool switch
        {
            ExternalTool.Ffmpeg => Glyphs.Video,
            ExternalTool.Pandoc => Glyphs.Document,
            _ => Glyphs.Table,
        };
        return Card(ToolLocator.DisplayName(tool), description, right, glyph: glyph);
    }
}
