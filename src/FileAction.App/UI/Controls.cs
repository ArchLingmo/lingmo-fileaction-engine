using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FileAction.App.UI;

/// <summary>
/// Small Windows 11–style building blocks drawn in code (cards, segmented choices, toggles,
/// sliders, pill buttons), so they look the same everywhere without depending on a theme assembly.
/// </summary>
internal static class Controls
{
    public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, Brush? brush = null, bool wrap = true) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? Theme.Text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    public static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontFamily = Theme.DisplayFont,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.Text,
        Margin = new Thickness(2, 26, 0, 8),
    };

    /// <summary>A rounded card with a title/description on the left and a control on the right.</summary>
    public static Border Card(string title, string? description, UIElement? control, UIElement? below = null, string? glyph = null)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Text(title, 14, FontWeights.Normal));
        if (!string.IsNullOrEmpty(description))
        {
            var d = Text(description, 12, brush: Theme.Muted);
            d.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(d);
        }

        var row = new DockPanel { LastChildFill = true };
        if (glyph is not null)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = Theme.Icons,
                FontSize = 18,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
            };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
        }
        if (control is FrameworkElement fe)
        {
            NameControl(fe, title);
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(fe, Dock.Right);
            row.Children.Add(fe);
        }
        row.Children.Add(left);

        var content = new StackPanel();
        content.Children.Add(row);
        if (below is not null)
            content.Children.Add(below);

        return new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 18, 14),
            Margin = new Thickness(0, 0, 0, 4),
            Child = content,
        };
    }

    // ------------------------------------------------------------------ keyboard and screen readers
    //
    // The controls below draw themselves, but each one is a real WPF button, toggle or radio button
    // underneath (with an empty template), so Tab reaches it, Enter/Space/arrows work, a focus ring shows
    // for keyboard users only, and screen readers announce the right role, name and state.

    private static readonly ControlTemplate PlainTemplate = CreatePlainTemplate();

    private static ControlTemplate CreatePlainTemplate()
    {
        var template = new ControlTemplate(typeof(ContentControl));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        template.Seal();
        return template;
    }

    /// <summary>The keyboard focus ring: an accent outline just outside the control.</summary>
    public static Style FocusRing { get; } = CreateFocusRing();

    private static Style CreateFocusRing()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.SetValue(Border.BorderBrushProperty, Theme.Accent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
        var template = new ControlTemplate(typeof(Control)) { VisualTree = border };
        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Seal();
        return style;
    }

    /// <summary>
    /// Makes <paramref name="visual"/> a real button: Tab-reachable, Enter/Space activate it, focus ring,
    /// and <paramref name="name"/> for screen readers. Layout (margin, alignment) moves to the button.
    /// </summary>
    public static System.Windows.Controls.Button Clickable(FrameworkElement visual, string name, Action click)
    {
        var button = new System.Windows.Controls.Button();
        Wrap(button, visual, name);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return button;
    }

    private static void Wrap(ContentControl control, FrameworkElement visual, string name)
    {
        control.Template = PlainTemplate;
        control.FocusVisualStyle = FocusRing;
        control.Cursor = Cursors.Hand;
        control.Background = Brushes.Transparent;
        control.Margin = visual.Margin;
        control.HorizontalAlignment = visual.HorizontalAlignment;
        control.VerticalAlignment = visual.VerticalAlignment;
        visual.Margin = new Thickness(0);
        control.Content = visual;
        AutomationProperties.SetName(control, name);
    }

    /// <summary>Gives an unnamed toggle, slider or choice the card's title as its accessible name.</summary>
    private static void NameControl(FrameworkElement control, string title)
    {
        var target = control is StackPanel row && row.Children.Count > 0 && row.Children[0] is FrameworkElement first ? first : control;
        if (string.IsNullOrEmpty(AutomationProperties.GetName(target)))
            AutomationProperties.SetName(target, title);
    }

    /// <summary>A row of mutually exclusive pills; the active one is filled with the accent gradient. Arrow keys move between them.</summary>
    public static Border Segmented<T>(IReadOnlyList<(string Label, T Value)> options, T selected, Action<T> changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Once);
        var group = "segmented-" + Guid.NewGuid().ToString("N");
        var pills = new List<(Border Pill, TextBlock Text, RadioButton Radio, T Value)>();
        var ready = false;
        void Select(T value)
        {
            foreach (var (pill, text, radio, v) in pills)
            {
                var on = EqualityComparer<T>.Default.Equals(v, value);
                pill.Background = on ? Theme.AccentGradient : Brushes.Transparent;
                text.Foreground = on ? Theme.TextOnAccent : Theme.Text;
                text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                radio.IsChecked = on;
            }
        }
        foreach (var (label, value) in options)
        {
            var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 5, 12, 6),
                Background = Brushes.Transparent,
                Child = text,
            };
            var radio = new RadioButton { GroupName = group };
            Wrap(radio, pill, label);
            var v = value;
            radio.Checked += (_, _) =>
            {
                if (!ready)
                    return;
                Select(v);
                changed(v);
            };
            pills.Add((pill, text, radio, value));
            panel.Children.Add(radio);
        }
        Select(selected);
        ready = true;
        return new Border
        {
            Background = Theme.Track,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(2),
            Child = panel,
        };
    }

    /// <summary>Windows 11 toggle switch (Space toggles it; screen readers hear "on"/"off").</summary>
    public static FrameworkElement Toggle(bool value, Action<bool> changed, string? name = null)
    {
        var knob = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border
        {
            Width = 40,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = knob,
        };
        var translate = new TranslateTransform();
        knob.RenderTransform = translate;
        var toggle = new ToggleButton { IsChecked = value };
        Wrap(toggle, track, name ?? "");
        void Paint(bool animate)
        {
            var on = toggle.IsChecked == true;
            track.Background = on ? Theme.AccentGradient : Brushes.Transparent;
            track.BorderBrush = on ? Brushes.Transparent : Theme.Muted;
            knob.Fill = on ? Brushes.White : Theme.Muted;
            var x = on ? 23.0 : 3.0;
            if (animate)
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase() });
            else
                translate.X = x;
        }
        toggle.Click += (_, e) =>
        {
            e.Handled = true;
            Paint(true);
            changed(toggle.IsChecked == true);
        };
        Paint(false);
        return toggle;
    }

    /// <summary>A slim slider with an accent-filled track and a round thumb; arrows, Page Up/Down, Home and End move it.</summary>
    public static FrameworkElement Slider(int min, int max, int value, Action<int> changed, Action committed, string? name = null)
    {
        const double width = 200;
        var label = new TextBlock { Width = 40, FontSize = 13, Foreground = Theme.Text, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.AccentGradient, HorizontalAlignment = HorizontalAlignment.Left };
        var thumb = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = Brushes.White,
            Stroke = Theme.PanelBorder,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            Effect = Theme.Shadow(6),
        };
        var surface = new Grid { Width = width, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = FocusRing };
        KeyboardNavigation.SetIsTabStop(surface, true);
        surface.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Track });
        surface.Children.Add(fill);
        surface.Children.Add(thumb);
        if (name is not null)
            AutomationProperties.SetName(surface, name);

        var current = value;
        void Show()
        {
            var t = (current - min) / (double)(max - min);
            fill.Width = Math.Max(0, t * width);
            thumb.Margin = new Thickness(t * (width - 18), 0, 0, 0);
            label.Text = current.ToString();
            AutomationProperties.SetItemStatus(surface, current.ToString());
        }
        void Set(int v)
        {
            v = Math.Clamp(v, min, max);
            if (v == current)
                return;
            current = v;
            Show();
            changed(v);
        }
        void SetFrom(Point p) => Set((int)Math.Round(min + Math.Clamp((p.X - 9) / (width - 18), 0, 1) * (max - min)));
        surface.MouseLeftButtonDown += (_, e) =>
        {
            surface.Focus();
            surface.CaptureMouse();
            SetFrom(e.GetPosition(surface));
        };
        surface.MouseMove += (_, e) =>
        {
            if (surface.IsMouseCaptured)
                SetFrom(e.GetPosition(surface));
        };
        surface.MouseLeftButtonUp += (_, _) =>
        {
            surface.ReleaseMouseCapture();
            committed();
        };
        var big = Math.Max(1, (max - min) / 10);
        surface.KeyDown += (_, e) =>
        {
            var before = current;
            switch (e.Key)
            {
                case Key.Left or Key.Down:
                    Set(current - 1);
                    break;
                case Key.Right or Key.Up:
                    Set(current + 1);
                    break;
                case Key.PageDown:
                    Set(current - big);
                    break;
                case Key.PageUp:
                    Set(current + big);
                    break;
                case Key.Home:
                    Set(min);
                    break;
                case Key.End:
                    Set(max);
                    break;
                default:
                    return;
            }
            e.Handled = true;
            if (current != before)
                committed();
        };
        Show();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(surface);
        row.Children.Add(label);
        return row;
    }

    /// <summary>A rounded button; <paramref name="primary"/> ones use the accent gradient.</summary>
    public static System.Windows.Controls.Button Button(string text, Action click, bool primary = false, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var foreground = primary ? Theme.TextOnAccent : Theme.Text;
        if (glyph is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = Theme.Icons,
                FontSize = 12,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 7, 0),
            });
        }
        content.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = foreground, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
        var visual = new Border
        {
            Background = primary ? Theme.AccentGradient : Theme.Card,
            BorderBrush = primary ? Brushes.Transparent : Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 7),
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var button = Clickable(visual, text.TrimEnd('…', '.'), click);
        button.MouseEnter += (_, _) => visual.Opacity = 0.88;
        button.MouseLeave += (_, _) => visual.Opacity = 1;
        return button;
    }

    /// <summary>A single-line text box in the card style.</summary>
    public static TextBox TextField(string text, int maxLength = 200) => new()
    {
        Text = text,
        MaxLength = maxLength,
        Padding = new Thickness(10, 7, 10, 7),
        FontSize = 14,
        Background = Theme.Card,
        Foreground = Theme.Text,
        CaretBrush = Theme.Text,
        BorderBrush = Theme.PanelBorder,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>A muted label above a field.</summary>
    public static TextBlock FieldLabel(string text, double top = 16)
    {
        var label = Text(text, 13, brush: Theme.Muted);
        label.Margin = new Thickness(0, top, 0, 6);
        return label;
    }

    /// <summary>A small coloured status label, e.g. "found" / "missing".</summary>
    public static Border Badge(string text, bool good) => new()
    {
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 2, 8, 3),
        Background = new SolidColorBrush(Color.FromArgb(0x26, good ? (byte)0x3C : (byte)0xF0, good ? (byte)0xC3 : (byte)0x5A, good ? (byte)0x6E : (byte)0x5A)),
        Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = good ? Theme.Success : Theme.Error },
    };
}
