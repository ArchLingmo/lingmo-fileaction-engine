using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Input;
using FileAction.App.Shell;
using FileAction.Core;

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
        Title = "FileAction – nastavení";
        Width = 560;
        Height = 760;
        MinWidth = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/FileAction.ico"));

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        root.Children.Add(Intro());

        root.Children.Add(Section("Klávesy"));
        root.Children.Add(Muted("Chyťte soubor myší a během tažení ťukněte na klávesu. Otevře se kolo, na které soubor pustíte."));
        var wheels = new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions };
        for (var i = 0; i < wheels.Length; i++)
            root.Children.Add(KeyRow(i, wheels[i]));

        root.Children.Add(Section("Klávesová zkratka"));
        root.Children.Add(Muted("Otevře kolo pro soubory vybrané v Průzkumníku (nebo zkopírované do schránky). Ovládání šipkami, Enter, Tab, Esc."));
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

        root.Children.Add(Section("Kam ukládat výsledky"));
        var nextTo = new RadioButton { Content = "Vedle původního souboru", GroupName = "out", Margin = new Thickness(0, 4, 0, 4) };
        var folder = new RadioButton { Content = "Do složky:", GroupName = "out", Margin = new Thickness(0, 4, 0, 4) };
        var folderText = new TextBlock { Text = settings.OutputDirectory ?? "(nezvoleno)", Margin = new Thickness(24, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.75 };
        var choose = new Button { Content = "Vybrat složku…", Margin = new Thickness(24, 2, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        nextTo.IsChecked = string.IsNullOrWhiteSpace(settings.OutputDirectory);
        folder.IsChecked = !nextTo.IsChecked;
        nextTo.Checked += (_, _) =>
        {
            _settings.OutputDirectory = null;
            folderText.Text = "(nezvoleno)";
            Save();
        };
        void PickFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Výstupní složka" };
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
        root.Children.Add(Check("Po dokončení zobrazit výsledek ve složce", settings.RevealResult, v => _settings.RevealResult = v));

        root.Children.Add(Section("Kvalita"));
        root.Children.Add(SliderRow("Kvalita JPG / WebP / AVIF", 50, 100, settings.Quality.JpegQuality, v => _settings.Quality.JpegQuality = v));
        root.Children.Add(SliderRow("Kvalita při „Komprimovat“", 30, 95, settings.Quality.CompressQuality, v => _settings.Quality.CompressQuality = v));
        root.Children.Add(SliderRow("Rozlišení PDF → obrázek (DPI)", 72, 600, settings.Quality.PdfDpi, v => _settings.Quality.PdfDpi = v, step: 1));

        root.Children.Add(Section("Systém"));
        root.Children.Add(Check("Spouštět po přihlášení do Windows", ShellIntegration.IsAutostartEnabled, ShellIntegration.SetAutostart));
        root.Children.Add(Check("Položka „FileAction…“ v kontextové nabídce Průzkumníka", ShellIntegration.IsContextMenuEnabled, ShellIntegration.SetContextMenu));
        root.Children.Add(Check("Pozastavit gesta (kolo se při tažení neotevře)", settings.Paused, v => _settings.Paused = v));

        root.Children.Add(Section("Pomocné programy"));
        root.Children.Add(Muted("Obrázky, PDF, tabulky, titulky a archivy zvládá FileAction sám. Pro audio a video je potřeba FFmpeg, pro dokumenty Pandoc a pro Office soubory LibreOffice."));
        root.Children.Add(_toolRows);
        var recheck = new Button { Content = "Zkontrolovat znovu", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        recheck.Click += (_, _) =>
        {
            _tools.Refresh();
            BuildToolRows();
        };
        root.Children.Add(recheck);
        BuildToolRows();

        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        root.Children.Add(Muted($"FileAction {version?.ToString(3)} · vše běží lokálně, soubory se nikam neodesílají."));

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
        text.Children.Add(new TextBlock { Text = "Chyťte soubor, ťukněte na klávesu, pusťte ho na formát.", Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
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
            box.Items.Add(new ComboBoxItem { Content = key == ModifierKey.None ? "Vypnuto" : AppSettings.KeyName(key), Tag = key });
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
                Text = path is null ? "chybí" : "nalezeno",
                ToolTip = path,
                Foreground = path is null ? Theme.Error : new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4F)),
                Width = 80,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(name);
            row.Children.Add(status);
            if (path is null)
            {
                var install = new Button { Content = "Nainstalovat přes winget", HorizontalAlignment = HorizontalAlignment.Left };
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
                            $"/c winget install -e --id {id} --accept-source-agreements --accept-package-agreements & echo. & echo Hotovo. Ve FileAction kliknete na \"Zkontrolovat znovu\". & pause")
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
