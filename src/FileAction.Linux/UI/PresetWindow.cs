using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FileAction.Core;
using static FileAction.Core.Loc;
using static FileAction.Linux.UI.Widgets;

namespace FileAction.Linux.UI;

/// <summary>Creates or edits a preset: name, kind, format and the few settings that matter for it.</summary>
public sealed class PresetWindow : Window
{
    private readonly Preset _preset;
    private readonly TextBox _name;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly TextBlock _summary = Text("", 13, brush: Palette.Muted);

    public PresetWindow(Preset? existing)
    {
        _preset = existing is null ? new Preset { Kind = PresetKind.Image, Format = "jpg", Quality = 80, MaxSide = 1920 } : Copy(existing);
        _name = TextField(_preset.Name, 40);
        _name.TextChanged += (_, _) =>
        {
            _preset.Name = _name.Text ?? "";
            UpdateSummary();
        };
        ApplyLook(this);
        Title = existing is null ? T("Nová předvolba", "New preset") : T("Upravit předvolbu", "Edit preset");
        Width = 600;
        Height = 680;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _scroll;
        Build();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
        Opened += (_, _) => _name.Focus();
    }

    /// <summary>The preset as edited; valid after the window closed with true.</summary>
    public Preset Result => _preset;

    private static Preset Copy(Preset p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Kind = p.Kind,
        Format = p.Format,
        Quality = p.Quality,
        MaxSide = p.MaxSide,
        Percent = p.Percent,
        Grayscale = p.Grayscale,
        StripMetadata = p.StripMetadata,
        Height = p.Height,
        VideoQuality = p.VideoQuality,
        Mute = p.Mute,
        Bitrate = p.Bitrate,
    };

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        root.Children.Add(Text(Title ?? "", 22, FontWeight.SemiBold));
        var intro = Text(T("Vlastní výseč s pevným nastavením. Změna formátu se objeví na kole Převést, úpravy ve stejném formátu na kole Nástroje.",
                           "A slice of your own with fixed settings. Format changes appear on the Convert wheel, same-format edits on the Tools wheel."),
            13, brush: Palette.Muted);
        intro.Margin = new Thickness(0, 4, 0, 4);
        root.Children.Add(intro);

        // The text box survives rebuilds (kind or format changes), so it must leave its old parent first.
        (_name.Parent as Panel)?.Children.Remove(_name);
        root.Children.Add(FieldLabel(T("Název na kole", "Name on the wheel")));
        root.Children.Add(_name);
        root.Children.Add(Hint(T("Prázdné = použije se popis, např. „JPG · kvalita 80“.", "Empty = the description is used, e.g. “JPG · quality 80”.")));

        root.Children.Add(FieldLabel(T("Pro", "For")));
        root.Children.Add(Segmented(
            [(T("Obrázky", "Images"), PresetKind.Image), ("Video", PresetKind.Video), (T("Zvuk", "Audio"), PresetKind.Audio)],
            _preset.Kind,
            kind =>
            {
                if (kind == _preset.Kind)
                    return;
                _preset.Kind = kind;
                _preset.Format = PresetOperations.FormatsFor(kind)[kind == PresetKind.Image ? 1 : 0];
                Rebuild();
            }));

        root.Children.Add(FieldLabel(T("Výstupní formát", "Output format")));
        root.Children.Add(Segmented(
            [.. PresetOperations.FormatsFor(_preset.Kind).Select(f => (f == Preset.SameFormat ? T("Stejný", "Same") : Formats.Display(f), f))],
            _preset.Format,
            format =>
            {
                _preset.Format = format;
                Rebuild();
            }));

        switch (_preset.Kind)
        {
            case PresetKind.Image:
                ImageOptions(root);
                break;
            case PresetKind.Video:
                VideoOptions(root);
                break;
            default:
                AudioOptions(root);
                break;
        }

        (_summary.Parent as Panel)?.Children.Remove(_summary);
        _summary.Margin = new Thickness(0, 22, 0, 0);
        root.Children.Add(_summary);
        UpdateSummary();
        root.Children.Add(DialogButtons(() => Close(true), () => Close(false)));
        _scroll.Content = root;
    }

    private void UpdateSummary()
    {
        var wheel = WheelWindow.WheelName(_preset.Format == Preset.SameFormat ? WheelKind.Tools : WheelKind.Convert);
        _summary.Text = T($"Kolo {wheel} · {PresetOperations.Summary(_preset)}", $"{wheel} wheel · {PresetOperations.Summary(_preset)}");
    }

    private void Rebuild()
    {
        var offset = _scroll.Offset;
        Build();
        _scroll.UpdateLayout();
        _scroll.Offset = offset;
    }

    private void ImageOptions(Panel root)
    {
        if (!PresetOperations.IsLossless(_preset.Format))
        {
            root.Children.Add(FieldLabel(T("Kvalita", "Quality")));
            var value = Text(_preset.Quality.ToString(CultureInfo.InvariantCulture), 14, FontWeight.SemiBold, wrap: false);
            value.Width = 36;
            value.VerticalAlignment = VerticalAlignment.Center;
            var slider = new Slider { Minimum = 30, Maximum = 100, Value = _preset.Quality, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 320 };
            slider.ValueChanged += (_, e) =>
            {
                _preset.Quality = (int)Math.Round(e.NewValue);
                value.Text = _preset.Quality.ToString(CultureInfo.InvariantCulture);
                UpdateSummary();
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(slider);
            row.Children.Add(value);
            root.Children.Add(row);
        }
        root.Children.Add(FieldLabel(T("Velikost", "Size")));
        var size = _preset.MaxSide is { } m ? "max" + m.ToString(CultureInfo.InvariantCulture)
            : _preset.Percent is { } p ? p.ToString(CultureInfo.InvariantCulture) + "%" : "keep";
        root.Children.Add(Segmented(
            [(T("Původní", "Original"), "keep"), ("50 %", "50%"), ("Max 3840", "max3840"), ("Max 1920", "max1920"), ("Max 1280", "max1280"), ("Max 800", "max800")],
            size,
            value =>
            {
                _preset.MaxSide = value.StartsWith("max", StringComparison.Ordinal) ? int.Parse(value[3..], CultureInfo.InvariantCulture) : null;
                _preset.Percent = value.EndsWith('%') ? int.Parse(value[..^1], CultureInfo.InvariantCulture) : null;
                Rebuild();
            }));
        root.Children.Add(Hint(T("Max = delší strana v pixelech; menší obrázky se nezvětšují.", "Max = longer side in pixels; smaller images are not enlarged.")));
        root.Children.Add(OptionRow(T("Černobíle", "Grayscale"), _preset.Grayscale, v =>
        {
            _preset.Grayscale = v;
            UpdateSummary();
        }));
        root.Children.Add(OptionRow(T("Bez metadat (GPS, EXIF)", "No metadata (GPS, EXIF)"), _preset.StripMetadata, v =>
        {
            _preset.StripMetadata = v;
            UpdateSummary();
        }));
    }

    private void VideoOptions(Panel root)
    {
        root.Children.Add(FieldLabel(T("Rozlišení", "Resolution")));
        root.Children.Add(Segmented<int?>(
            [(T("Původní", "Original"), null), ("1080p", 1080), ("720p", 720), ("480p", 480)],
            _preset.Height,
            h =>
            {
                _preset.Height = h;
                Rebuild();
            }));
        root.Children.Add(FieldLabel(T("Kvalita", "Quality")));
        root.Children.Add(Segmented(
            [(T("Vysoká", "High"), VideoQuality.High), (T("Střední", "Medium"), VideoQuality.Medium), (T("Malý soubor", "Small file"), VideoQuality.Small)],
            _preset.VideoQuality,
            q =>
            {
                _preset.VideoQuality = q;
                Rebuild();
            }));
        root.Children.Add(OptionRow(T("Bez zvuku", "Mute"), _preset.Mute, v =>
        {
            _preset.Mute = v;
            UpdateSummary();
        }));
    }

    private void AudioOptions(Panel root)
    {
        if (PresetOperations.IsLossless(_preset.Format))
        {
            root.Children.Add(Hint(T("FLAC je bezeztrátový, nic dalšího se nenastavuje.", "FLAC is lossless; there is nothing else to set.")));
            return;
        }
        root.Children.Add(FieldLabel(T("Datový tok", "Bitrate")));
        root.Children.Add(Segmented(
            [("96k", 96), ("128k", 128), ("192k", 192), ("256k", 256), ("320k", 320)],
            _preset.Bitrate,
            b =>
            {
                _preset.Bitrate = b;
                Rebuild();
            }));
        root.Children.Add(Hint(T("Funguje i na video: vytáhne z něj zvuk.", "Works on video too: it extracts the audio.")));
    }
}
