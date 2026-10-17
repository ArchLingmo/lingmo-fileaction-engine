using System.Windows;
using System.Windows.Media;

namespace FileAction.App.UI;

/// <summary>Colours and fonts shared by the wheel, the job panel and the shelf.</summary>
internal static class Theme
{
    public static readonly Color AccentColor = Color.FromRgb(0xFF, 0x8A, 0x1F);
    public static readonly SolidColorBrush Accent = Freeze(new SolidColorBrush(AccentColor));
    public static readonly SolidColorBrush AccentSoft = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x8A, 0x1F)));
    public static readonly SolidColorBrush Backdrop = Freeze(new SolidColorBrush(Color.FromArgb(0xEE, 0x14, 0x14, 0x18)));
    public static readonly SolidColorBrush Slice = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x31)));
    public static readonly SolidColorBrush Hub = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x21)));
    public static readonly SolidColorBrush Panel = Freeze(new SolidColorBrush(Color.FromArgb(0xF5, 0x1E, 0x1E, 0x24)));
    public static readonly SolidColorBrush PanelBorder = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
    public static readonly SolidColorBrush Text = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6)));
    public static readonly SolidColorBrush TextOnAccent = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x10, 0x05)));
    public static readonly SolidColorBrush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xAA)));
    public static readonly SolidColorBrush Error = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
    public static readonly SolidColorBrush Success = Freeze(new SolidColorBrush(Color.FromRgb(0x7C, 0xD9, 0x92)));

    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public static Thickness Pad(double all) => new(all);
}
