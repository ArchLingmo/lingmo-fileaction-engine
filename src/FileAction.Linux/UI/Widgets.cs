using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static FileAction.Core.Loc;

namespace FileAction.Linux.UI;

/// <summary>Small drawn controls matching the Windows app (pill buttons, cards, segmented choices).</summary>
internal static class Widgets
{
    public static Border Button(string text, Action click, bool primary = false)
    {
        var button = new Border
        {
            Background = primary ? Palette.AccentGradient : Palette.Track,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 7),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = primary ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = primary ? Palette.TextOnAccent : Palette.Text,
            },
        };
        button.PointerEntered += (_, _) => button.Opacity = 0.88;
        button.PointerExited += (_, _) => button.Opacity = 1;
        button.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return button;
    }

    /// <summary>A button that asks "Really?" on the first click and acts on the second (there is no message box).</summary>
    public static Border ConfirmButton(string text, string question, Action click)
    {
        var armed = false;
        Border? button = null;
        button = Button(text, () =>
        {
            if (armed)
            {
                click();
                return;
            }
            armed = true;
            button!.Background = Palette.Error;
            ((TextBlock)button.Child!).Text = question;
            ((TextBlock)button.Child!).Foreground = Palette.TextOnAccent;
        });
        return button;
    }

    public static TextBlock Text(string text, double size = 13, FontWeight weight = FontWeight.Normal, IBrush? brush = null, bool wrap = true) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush ?? Palette.Text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    public static TextBlock FieldLabel(string text)
    {
        var label = Text(text, 13, brush: Palette.Muted);
        label.Margin = new Thickness(0, 16, 0, 6);
        return label;
    }

    public static TextBlock Hint(string text)
    {
        var hint = Text(text, 12, brush: Palette.Muted);
        hint.Margin = new Thickness(0, 6, 0, 0);
        return hint;
    }

    public static TextBlock SectionHeader(string text)
    {
        var header = Text(text, 12, FontWeight.SemiBold, Palette.Muted);
        header.Margin = new Thickness(2, 20, 0, 8);
        return header;
    }

    /// <summary>A rounded panel with a title and description; <paramref name="right"/> sits beside them, <paramref name="below"/> under them.</summary>
    public static Border Card(string title, string? description, Control? right = null, Control? below = null)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(title, 14.5, FontWeight.SemiBold));
        if (!string.IsNullOrEmpty(description))
        {
            var d = Text(description, 12.5, brush: Palette.Muted);
            d.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(d);
        }
        var row = new DockPanel();
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
        }
        row.Children.Add(text);
        var body = new StackPanel();
        body.Children.Add(row);
        if (below is not null)
            body.Children.Add(below);
        return new Border
        {
            Background = Palette.Panel,
            BorderBrush = Palette.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = body,
        };
    }

    /// <summary>A row of choices, one of them highlighted.</summary>
    public static Border Segmented<T>(IReadOnlyList<(string Label, T Value)> options, T selected, Action<T> changed)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, value) in options)
        {
            var active = EqualityComparer<T>.Default.Equals(value, selected);
            var item = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 5, 12, 6),
                Background = active ? Palette.AccentGradient : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = Text(label, 13, active ? FontWeight.SemiBold : FontWeight.Normal, active ? Palette.TextOnAccent : Palette.Text, wrap: false),
            };
            var v = value;
            item.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                changed(v);
            };
            panel.Children.Add(item);
        }
        return new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(2),
            Background = Palette.Track,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel,
        };
    }

    public static ToggleSwitch Toggle(bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return toggle;
    }

    /// <summary>A label with a switch on the right.</summary>
    public static DockPanel OptionRow(string label, bool value, Action<bool> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var toggle = Toggle(value, changed);
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        var text = Text(label, 14);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }

    public static TextBox TextField(string? text, int maxLength = 200, bool multiline = false) => new()
    {
        Text = text,
        MaxLength = maxLength,
        FontSize = 14,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    /// <summary>Save / Cancel aligned right.</summary>
    public static StackPanel DialogButtons(Action save, Action cancel, string? saveText = null)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(Button(saveText ?? T("Uložit", "Save"), save, primary: true));
        buttons.Children.Add(Button(T("Zrušit", "Cancel"), cancel));
        return buttons;
    }

    /// <summary>Common look of the settings-style windows (they follow the desktop's light or dark theme).</summary>
    public static void ApplyLook(Window window)
    {
        Palette.Refresh();
        window.RequestedThemeVariant = Palette.IsDark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        window.Background = Palette.Page;
        window.Foreground = Palette.Text;
        window.Icon = AppIcon.Window;
    }
}
