using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Input;
using FileAction.App.Shell;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

internal sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;
    private readonly AppSettings _settings;
    private readonly ToolLocator _tools;
    private readonly Action _changed;
    private readonly StackPanel _toolRows = new();
    private readonly ComboBox[] _keyBoxes = new ComboBox[3];
    private bool _loading = true;

    internal SettingsWindow(AppSettings settings, ToolLocator tools, Action changed)
    {
        _settings = settings;
        _tools = tools;
        _changed = changed;
        ThemeMode = ThemeMode.System;
        Title = T("FileAction – nastavení", "FileAction – settings");
        Width = 560;
        Height = 760;
        MinWidth = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/FileAction.ico"));

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        root.Children.Add(Intro());

        root.Children.Add(Section(T("Jazyk", "Language")));
        var language = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        language.Items.Add(new ComboBoxItem { Content = T("Podle Windows", "Same as Windows"), Tag = null });
        language.Items.Add(new ComboBoxItem { Content = "Čeština", Tag = Core.Language.Czech });
        language.Items.Add(new ComboBoxItem { Content = "English", Tag = Core.Language.English });
        language.SelectedIndex = settings.Language switch
        {
            Core.Language.Czech => 1,
            Core.Language.English => 2,
            _ => 0,
        };
        language.SelectionChanged += (_, _) =>
        {
            if (_loading || language.SelectedItem is not ComboBoxItem item)
                return;
            _settings.Language = item.Tag as Core.Language?;
            Save();
            // Rebuild this window in the new language.
            Close();
            ShowSingle(_settings, _tools, _changed);
        };
        root.Children.Add(language);

        root.Children.Add(Section(T("Klávesy", "Keys")));
        root.Children.Add(Muted(T("Chyťte soubor myší a během tažení ťukněte na klávesu. Otevře se kolo, na které soubor pustíte.", "Grab a file with the mouse and tap a key while dragging. A wheel opens; drop the file on it.")));
        var wheels = new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions };
        for (var i = 0; i < wheels.Length; i++)
            root.Children.Add(KeyRow(i, wheels[i]));

        root.Children.Add(Section(T("Klávesová zkratka", "Keyboard shortcut")));
        root.Children.Add(Muted(T("Otevře kolo pro soubory vybrané v Průzkumníku (nebo zkopírované do schránky). Ovládání šipkami, Enter, Tab, Esc.", "Opens the wheel for the files selected in File Explorer (or copied to the clipboard). Use arrows, Enter, Tab and Esc.")));
        var hotkey = new ComboBox { Margin = new Thickness(0, 6, 0, 0), MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var preset in Enum.GetValues<HotkeyPreset>())
            hotkey.Items.Add(new ComboBoxItem { Content = AppSettings.HotkeyName(preset), Tag = preset });
        hotkey.SelectedIndex = Array.IndexOf(Enum.GetValues<HotkeyPreset>(), settings.Hotkey);
        hotkey.SelectionChanged += (_, _) =>
        {
            if (hotkey.SelectedItem is ComboBoxItem { Tag: HotkeyPreset p })
            {
                _settings.Hotkey = p;
                Save();
            }
        };
        root.Children.Add(hotkey);

        root.Children.Add(Section(T("Kam ukládat výsledky", "Where to save results")));
        var nextTo = new RadioButton { Content = T("Vedle původního souboru", "Next to the original file"), GroupName = "out", Margin = new Thickness(0, 4, 0, 4) };
        var folder = new RadioButton { Content = T("Do složky:", "To folder:"), GroupName = "out", Margin = new Thickness(0, 4, 0, 4) };
        var folderText = new TextBlock { Text = settings.OutputDirectory ?? T("(nezvoleno)", "(not set)"), Margin = new Thickness(24, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.75 };
        var choose = new Button { Content = T("Vybrat složku…", "Choose folder…"), Margin = new Thickness(24, 2, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        nextTo.IsChecked = string.IsNullOrWhiteSpace(settings.OutputDirectory);
        folder.IsChecked = !nextTo.IsChecked;
        nextTo.Checked += (_, _) =>
        {
            _settings.OutputDirectory = null;
            folderText.Text = T("(nezvoleno)", "(not set)");
            Save();
        };
        void PickFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("Výstupní složka", "Output folder") };
            if (dialog.ShowDialog(this) == true)
            {
                _settings.OutputDirectory = dialog.FolderName;
                folderText.Text = dialog.FolderName;
                Save();
            }
            nextTo.IsChecked = string.IsNullOrWhiteSpace(_settings.OutputDirectory);
            folder.IsChecked = !nextTo.IsChecked;
        }
        folder.Checked += (_, _) =>
        {
            if (!_loading && string.IsNullOrWhiteSpace(_settings.OutputDirectory))
                PickFolder();
        };
        choose.Click += (_, _) => PickFolder();
        root.Children.Add(nextTo);
        root.Children.Add(folder);
        root.Children.Add(folderText);
        root.Children.Add(choose);
        root.Children.Add(Check(T("Po dokončení zobrazit výsledek ve složce", "Show the result in its folder when done"), settings.RevealResult, v => _settings.RevealResult = v));

        root.Children.Add(Section(T("Kvalita", "Quality")));
        root.Children.Add(SliderRow(T("Kvalita JPG / WebP / AVIF", "JPG / WebP / AVIF quality"), 50, 100, settings.Quality.JpegQuality, v => _settings.Quality.JpegQuality = v));
        root.Children.Add(SliderRow(T("Kvalita při „Komprimovat“", "“Compress” quality"), 30, 95, settings.Quality.CompressQuality, v => _settings.Quality.CompressQuality = v));
        root.Children.Add(SliderRow(T("Rozlišení PDF → obrázek (DPI)", "PDF → image resolution (DPI)"), 72, 600, settings.Quality.PdfDpi, v => _settings.Quality.PdfDpi = v, step: 1));

        root.Children.Add(Section(T("Systém", "System")));
        root.Children.Add(Check(T("Spouštět po přihlášení do Windows", "Start when I sign in to Windows"), ShellIntegration.IsAutostartEnabled, ShellIntegration.SetAutostart));
        root.Children.Add(Check(T("Položka „FileAction…“ v kontextové nabídce Průzkumníka", "“FileAction…” item in the File Explorer context menu"), ShellIntegration.IsContextMenuEnabled, ShellIntegration.SetContextMenu));
        root.Children.Add(Check(T("Pozastavit gesta (kolo se při tažení neotevře)", "Pause gestures (the wheel won’t open while dragging)"), settings.Paused, v => _settings.Paused = v));

        root.Children.Add(Section(T("Pomocné programy", "Helper programs")));
        root.Children.Add(Muted(T("Obrázky, PDF, tabulky, titulky a archivy zvládá FileAction sám. Pro audio a video je potřeba FFmpeg, pro dokumenty Pandoc a pro Office soubory LibreOffice.", "FileAction handles images, PDFs, spreadsheets, subtitles and archives on its own. Audio and video need FFmpeg, documents need Pandoc and Office files need LibreOffice.")));
        root.Children.Add(_toolRows);
        var recheck = new Button { Content = T("Zkontrolovat znovu", "Check again"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        recheck.Click += (_, _) =>
        {
            _tools.Refresh();
            BuildToolRows();
        };
        root.Children.Add(recheck);
        BuildToolRows();

        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        root.Children.Add(Muted($"FileAction {version?.ToString(3)} · " + T("vše běží lokálně, soubory se nikam neodesílají.", "everything runs locally, files never leave your PC.")));

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closed += (_, _) => _open = null;
        _loading = false;
    }

    public static void ShowSingle(AppSettings settings, ToolLocator tools, Action changed)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new SettingsWindow(settings, tools, changed);
        _open.Show();
        _open.Activate();
    }

    private void Save()
    {
        if (_loading)
            return;
        _settings.Save();
        _changed();
    }

    private static UIElement Intro()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var image = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/FileAction.png")), Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0) };
        panel.Children.Add(image);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "FileAction", FontSize = 22, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = T("Chyťte soubor, ťukněte na klávesu, pusťte ho na formát.", "Grab a file, tap a key, drop it on a format."), Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(text);
        return panel;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 22, 0, 6),
    };

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        Opacity = 0.72,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 6),
    };

    private UIElement KeyRow(int index, WheelKind wheel)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var label = new TextBlock { Text = AppSettings.WheelName(wheel), Width = 140, VerticalAlignment = VerticalAlignment.Center };
        var box = new ComboBox { MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var key in new[] { ModifierKey.Shift, ModifierKey.Ctrl, ModifierKey.Alt, ModifierKey.None })
            box.Items.Add(new ComboBoxItem { Content = key == ModifierKey.None ? T("Vypnuto", "Off") : AppSettings.KeyName(key), Tag = key });
        _keyBoxes[index] = box;
        SelectKey(box, _settings.KeyFor(wheel));
        box.SelectionChanged += (_, _) =>
        {
            if (_loading || box.SelectedItem is not ComboBoxItem { Tag: ModifierKey key })
                return;
            var old = _settings.KeyFor(wheel);
            // Keep keys unique: the wheel that had this key gets the old one.
            foreach (var other in new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions })
            {
                if (other != wheel && key != ModifierKey.None && _settings.KeyFor(other) == key)
                    SetKey(other, old);
            }
            SetKey(wheel, key);
            _loading = true;
            SelectKey(_keyBoxes[0], _settings.ConvertKey);
            SelectKey(_keyBoxes[1], _settings.ToolsKey);
            SelectKey(_keyBoxes[2], _settings.ActionsKey);
            _loading = false;
            Save();
        };
        row.Children.Add(label);
        row.Children.Add(box);
        return row;
    }

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

    private static void SelectKey(ComboBox? box, ModifierKey key)
    {
        if (box is null)
            return;
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (ModifierKey)i.Tag == key);
    }

    private CheckBox Check(string text, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        box.Checked += (_, _) => Apply(true);
        box.Unchecked += (_, _) => Apply(false);
        return box;

        void Apply(bool v)
        {
            try
            {
                set(v);
                Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "FileAction", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private UIElement SliderRow(string text, int min, int max, int value, Action<int> set, int step = 1)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var label = new TextBlock { Text = text, Width = 220, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var number = new TextBlock { Text = value.ToString(), Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = 10, IsSnapToTickEnabled = true, TickFrequency = step, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            number.Text = v.ToString();
            set(v);
        };
        slider.LostMouseCapture += (_, _) => Save();
        slider.KeyUp += (_, _) => Save();
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(number, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(number);
        row.Children.Add(slider);
        return row;
    }

    private void BuildToolRows()
    {
        _toolRows.Children.Clear();
        foreach (var tool in Enum.GetValues<ExternalTool>())
        {
            var path = _tools.Find(tool);
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var name = new TextBlock { Text = ToolLocator.DisplayName(tool), Width = 110, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var status = new TextBlock
            {
                Text = path is null ? T("chybí", "missing") : T("nalezeno", "found"),
                ToolTip = path,
                Foreground = path is null ? Theme.Error : new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4F)),
                Width = 80,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(name);
            row.Children.Add(status);
            if (path is null)
            {
                var install = new Button { Content = T("Nainstalovat přes winget", "Install with winget"), HorizontalAlignment = HorizontalAlignment.Left };
                var id = tool switch
                {
                    ExternalTool.Ffmpeg => "Gyan.FFmpeg",
                    ExternalTool.Pandoc => "JohnMacFarlane.Pandoc",
                    _ => "TheDocumentFoundation.LibreOffice",
                };
                install.Click += (_, _) =>
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
                };
                row.Children.Add(install);
            }
            else
            {
                row.Children.Add(new TextBlock { Text = path, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            }
            _toolRows.Children.Add(row);
        }
    }
}
