using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.App.Wheel;
using FileAction.Core;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>
/// The Wheels page: pick a wheel and a file type, see that wheel as it will open and arrange it by
/// dragging: reorder slices, hide them, bring them back from the library or move them to another wheel.
/// </summary>
internal sealed partial class SettingsWindow
{
    private WheelKind _wheel = WheelKind.Convert;
    private int _group;

    /// <summary>File types shown as groups, with sample names used to find which slices apply.</summary>
    internal static (string Czech, string English, string Glyph, string[] Samples)[] Groups =>
    [
        // Two formats per group, so "convert to X" shows up even when one sample already is X.
        ("Obrázky", "Images", Glyphs.Photo, ["a.png", "a.jpg", "a.gif"]),
        ("Video", "Video", Glyphs.Video, ["a.mp4", "a.mov"]),
        ("Audio", "Audio", Glyphs.Audio, ["a.mp3", "a.wav"]),
        ("PDF", "PDF", Glyphs.Page, ["a.pdf"]),
        ("Dokumenty", "Documents", Glyphs.Document, ["a.docx", "a.md"]),
        ("Prezentace", "Slides", Glyphs.Page, ["a.pptx", "a.odp"]),
        ("Tabulky", "Spreadsheets", Glyphs.Table, ["a.xlsx", "a.csv"]),
        ("Titulky", "Subtitles", Glyphs.Subtitles, ["a.srt", "a.vtt"]),
        ("Archivy", "Archives", Glyphs.Pack, ["a.zip", "a.tar"]),
    ];

    /// <summary>Every group with its slices; slices that work for anything (archive, actions) form the last group.</summary>
    private static List<(string Name, string Glyph, List<Operation> Ops)> GroupedOperations() => Group(Operations?.Invoke() ?? []);

    /// <summary>Splits operations into the file-type groups (each operation once, in the first group it fits).</summary>
    internal static List<(string Name, string Glyph, List<Operation> Ops)> Group(IReadOnlyList<Operation> all)
    {
        var folder = new FileItem(Path.GetTempPath());
        var shown = new HashSet<string>();
        var groups = new List<(string, string, List<Operation>)>();
        foreach (var (czech, english, glyph, samples) in Groups)
        {
            var items = samples.Select(s => new FileItem(s)).ToList();
            var ops = all.Where(o => o.Wheel != WheelKind.Actions && !o.AppliesTo([folder]) && Applies(o, items) && shown.Add(o.Id)).ToList();
            groups.Add((T(czech, english), glyph, ops));
        }
        groups.Add((T("Cokoli", "Anything"), Glyphs.Folder, all.Where(o => shown.Add(o.Id)).ToList()));
        return groups;
    }

    internal static bool Applies(Operation op, IReadOnlyList<FileItem> samples) =>
        samples.Any(s => op.AppliesTo([s]) || op.AppliesTo([s, s with { Path = "b" + Path.GetExtension(s.Path) }]));

    /// <summary>Shows the wheel page for one wheel and file type (used by the self-test).</summary>
    internal void ShowWheel(WheelKind wheel, int group)
    {
        _wheel = wheel;
        _group = group;
        Page = SettingsPage.Wheels;
    }

    // ------------------------------------------------------------------ the page

    private readonly Border _wheelHost = new() { MaxWidth = 440 };
    private readonly List<(Border Tab, WheelKind Wheel)> _wheelTabs = [];
    private Canvas? _wheelCanvas;
    private List<Operation> _visibleOps = [];
    private List<Operation> _groupOps = [];
    private string _groupName = "";
    private SliceDrag? _drag;

    /// <summary>A slice being dragged: from the wheel or from the library, and where it would land now.</summary>
    private sealed class SliceDrag(Operation op, bool fromWheel, Point start)
    {
        public Operation Op { get; } = op;
        public bool FromWheel { get; } = fromWheel;
        public Point Start { get; } = start;
        public bool Active { get; set; }
        public Border? Ghost { get; set; }
        public List<Operation>? Preview { get; set; }
        public WheelKind? TargetWheel { get; set; }
        public bool Remove { get; set; }
        public string Key { get; set; } = "";
    }

    private void WheelsPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Wheels),
            T("Přetáhněte výseč na jiné místo na kole. Mimo kolo ji skryjete, na záložku jiného kola ji přesunete. Z knihovny vpravo ji vrátíte zpátky.",
              "Drag a slice to another place on the wheel. Drop it off the wheel to hide it, or on another wheel's tab to move it there. Drag it back from the library on the right."));

        var top = new DockPanel { Margin = new Thickness(0, 14, 0, 18), LastChildFill = false };
        top.Children.Add(WheelTabs());
        var reset = Button(T("Obnovit výchozí", "Reset to default"), () =>
        {
            _settings.HiddenOperations.Clear();
            _settings.OperationOrder.Clear();
            _settings.WheelOverrides.Clear();
            Save();
            Rebuild();
        }, glyph: "");
        DockPanel.SetDock(reset, Dock.Right);
        top.Children.Add(reset);
        root.Children.Add(top);

        var groups = GroupedOperations()
            .Select(g => (g.Name, g.Glyph, Ops: g.Ops.Where(o => o.Wheel == _wheel).ToList()))
            .Where(g => g.Ops.Count > 0)
            .ToList();
        if (groups.Count == 0)
            return;
        _group = Math.Clamp(_group, 0, groups.Count - 1);
        (_groupName, _, _groupOps) = groups[_group];
        _visibleOps = [.. WheelLayout.Arrange(_groupOps, _settings.HiddenOperations, _settings.OperationOrder)];

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });

        // 1. file types
        var list = new StackPanel();
        list.Children.Add(ColumnLabel(T("1. Typ souboru", "1. File type")));
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            var shown = WheelLayout.Arrange(g.Ops, _settings.HiddenOperations, _settings.OperationOrder).Count;
            list.Children.Add(GroupItem(g.Name, g.Glyph, $"{shown}", i));
        }
        grid.Children.Add(list);

        // 2. the wheel, which is also the drop target
        var middle = new StackPanel();
        middle.Children.Add(ColumnLabel(T("2. Kolo – přetahujte výseče", "2. The wheel – drag the slices")));
        (_wheelHost.Parent as Panel)?.Children.Remove(_wheelHost);
        if (_wheelHost.Parent is Border previous)
            previous.Child = null;
        RenderWheel(_visibleOps, null);
        middle.Children.Add(new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            Child = _wheelHost,
        });
        var hint = Text(T("Kolo se otevře přesně takhle. Pořadí platí pro všechny typy souborů: co dáte na první místo tady, bude první všude, kde se nabízí.",
                          "The wheel opens exactly like this. The order applies to every file type: what you put first here comes first wherever it is offered."), 12, brush: Theme.Muted);
        hint.Margin = new Thickness(4, 10, 4, 0);
        middle.Children.Add(hint);
        if (_visibleOps.Count > 12)
        {
            var crowded = Text(T($"Na kole je {_visibleOps.Count} výsečí, popisky jsou malé. Méně používané zkuste skrýt.",
                                 $"The wheel has {_visibleOps.Count} slices and the labels get small. Try hiding the ones you rarely use."), 12, brush: Theme.Accent);
            crowded.Margin = new Thickness(4, 8, 4, 0);
            middle.Children.Add(crowded);
        }
        Grid.SetColumn(middle, 2);
        grid.Children.Add(middle);

        // 3. library
        var right = new StackPanel();
        right.Children.Add(ColumnLabel(T("3. Knihovna – táhněte na kolo", "3. Library – drag onto the wheel")));
        var rows = new StackPanel();
        var shownIds = _visibleOps.Select(o => o.Id).ToHashSet();
        foreach (var op in _visibleOps.Concat(_groupOps.Where(o => !shownIds.Contains(o.Id))))
            rows.Children.Add(LibraryRow(op, rows.Children.Count > 0));
        right.Children.Add(new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6, 4, 6, 4),
            Child = rows,
        });
        Grid.SetColumn(right, 4);
        grid.Children.Add(right);

        root.Children.Add(grid);
    }

    /// <summary>The wheel switcher; its tabs are also drop targets for moving a slice to another wheel.</summary>
    private Border WheelTabs()
    {
        _wheelTabs.Clear();
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var w in new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions })
        {
            var on = w == _wheel;
            var key = _settings.KeyFor(w);
            var text = new TextBlock
            {
                Text = key == Input.ModifierKey.None ? AppSettings.WheelName(w) : $"{AppSettings.WheelName(w)} · {AppSettings.KeyName(key)}",
                FontSize = 12.5,
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = on ? Theme.TextOnAccent : Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var tab = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 5, 12, 6),
                Background = on ? Theme.AccentGradient : Brushes.Transparent,
                BorderThickness = new Thickness(1.5),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = text,
            };
            var target = w;
            tab.MouseLeftButtonUp += (_, _) =>
            {
                if (_drag is { Active: true })
                    return;
                _wheel = target;
                _group = 0;
                Rebuild();
            };
            _wheelTabs.Add((tab, w));
            panel.Children.Add(tab);
        }
        return new Border { Background = Theme.Track, CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = panel };
    }

    private void RenderWheel(IReadOnlyList<Operation> ops, string? dragged)
    {
        var preview = WheelWindow.Preview(ops, _groupName, $"{ops.Count} / {_groupOps.Count}", dragged is null ? null : new HashSet<string> { dragged });
        _wheelCanvas = (Canvas)((Viewbox)preview).Child;
        preview.Cursor = Cursors.Hand;
        preview.MouseLeftButtonDown += (_, e) =>
        {
            var index = WheelWindow.PreviewSliceAt(e.GetPosition(_wheelCanvas), _visibleOps.Count, out var offWheel);
            if (index < 0 || offWheel)
                return;
            BeginDrag(_visibleOps[index], fromWheel: true, e);
        };
        _wheelHost.Child = preview;
    }

    private UIElement LibraryRow(Operation op, bool separator)
    {
        var hidden = _settings.HiddenOperations.Contains(op.Id);
        var toggle = Toggle(!hidden, visible =>
        {
            if (visible)
                _settings.HiddenOperations.Remove(op.Id);
            else if (!_settings.HiddenOperations.Contains(op.Id))
                _settings.HiddenOperations.Add(op.Id);
            Save();
            // Let the switch finish its animation before the wheel redraws.
            Dispatcher.BeginInvoke(Rebuild, System.Windows.Threading.DispatcherPriority.Background);
        });
        toggle.Margin = new Thickness(6, 0, 8, 0);
        toggle.VerticalAlignment = VerticalAlignment.Center;

        var grip = new TextBlock
        {
            Text = "",
            FontFamily = Theme.Icons,
            FontSize = 14,
            Foreground = Theme.Muted,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var isFormat = op.Wheel == WheelKind.Convert && op.Label.Length <= 6 && op.Label.ToUpperInvariant() == op.Label;
        var icon = new TextBlock
        {
            Text = isFormat ? "" : op.Glyph,
            FontFamily = Theme.Icons,
            FontSize = 16,
            Foreground = Theme.Text,
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(Text(op.Label, 14, isFormat ? FontWeights.SemiBold : FontWeights.Normal, wrap: false));
        var detail = op.Subtitle ?? "";
        if (op is RelocatedOperation moved)
            detail = (detail.Length > 0 ? detail + " · " : "") + T($"přesunuto z kola {AppSettings.WheelName(moved.HomeWheel)}", $"moved from {AppSettings.WheelName(moved.HomeWheel)}");
        if (detail.Length > 0)
            label.Children.Add(Text(detail, 11.5, brush: Theme.Muted, wrap: false));

        var row = new DockPanel { Margin = new Thickness(6, 5, 2, 5), Opacity = hidden ? 0.5 : 1 };
        DockPanel.SetDock(grip, Dock.Left);
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(grip);
        row.Children.Add(icon);
        row.Children.Add(toggle);
        if (op is RelocatedOperation r)
        {
            var back = new TextBlock
            {
                Text = "",
                FontFamily = Theme.Icons,
                FontSize = 14,
                Foreground = Theme.Muted,
                Padding = new Thickness(8),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = T($"Vrátit na kolo {AppSettings.WheelName(r.HomeWheel)}", $"Back to {AppSettings.WheelName(r.HomeWheel)}"),
            };
            back.MouseLeftButtonDown += (_, e) => e.Handled = true;
            back.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _settings.WheelOverrides.Remove(op.Id);
                Save();
                Rebuild();
            };
            DockPanel.SetDock(back, Dock.Right);
            row.Children.Add(back);
        }
        row.Children.Add(label);
        var item = new Border
        {
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(0, separator ? 1 : 0, 0, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Child = row,
        };
        // The switch handles its own clicks; anywhere else on the row starts a drag.
        item.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInside(source, toggle))
                return;
            BeginDrag(op, fromWheel: false, e);
        };
        return item;
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, container))
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ dragging

    private void BeginDrag(Operation op, bool fromWheel, MouseButtonEventArgs e)
    {
        _drag = new SliceDrag(op, fromWheel, e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_drag is null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(commit: false);
            return;
        }
        DragTo(e.GetPosition(this));
    }

    /// <summary>Follows the cursor at <paramref name="position"/> (window coordinates).</summary>
    private void DragTo(Point position)
    {
        if (_drag is not { } drag)
            return;
        Point At(Visual element) => TranslatePoint(position, (UIElement)element);
        if (!drag.Active)
        {
            if ((position - drag.Start).Length < 5)
                return;
            drag.Active = true;
            drag.Ghost = Ghost(drag.Op);
            _overlay.Children.Add(drag.Ghost);
        }
        Canvas.SetLeft(drag.Ghost!, At(_overlay).X + 14);
        Canvas.SetTop(drag.Ghost!, At(_overlay).Y + 10);

        // Over another wheel's tab: move the slice to that wheel.
        drag.TargetWheel = null;
        foreach (var (tab, wheel) in _wheelTabs)
        {
            var p = At(tab);
            var over = wheel != _wheel && p.X >= 0 && p.Y >= 0 && p.X <= tab.ActualWidth && p.Y <= tab.ActualHeight;
            tab.BorderBrush = over ? Theme.Accent : Brushes.Transparent;
            if (over)
                drag.TargetWheel = wheel;
        }

        var others = _visibleOps.Where(o => o.Id != drag.Op.Id).ToList();
        string key;
        if (drag.TargetWheel is { } target)
        {
            drag.Preview = null;
            drag.Remove = false;
            key = "wheel:" + target;
            SetGhostHint(drag, T($"Přesunout na kolo {AppSettings.WheelName(target)}", $"Move to {AppSettings.WheelName(target)}"));
        }
        else
        {
            var index = -2;
            if (_wheelCanvas is not null)
            {
                var slice = WheelWindow.PreviewSliceAt(At(_wheelCanvas), others.Count + 1, out var off);
                index = off ? -2 : slice;
            }
            if (index == -2)
            {
                // Off the wheel: a slice from the wheel gets hidden, one from the library goes nowhere.
                drag.Remove = drag.FromWheel;
                drag.Preview = drag.FromWheel ? others : null;
                key = drag.FromWheel ? "remove" : "none";
                SetGhostHint(drag, drag.FromWheel ? T("Pustit = skrýt", "Drop to hide") : "");
            }
            else if (index == -1)
            {
                // Over the hub: keep the last position.
                return;
            }
            else
            {
                drag.Remove = false;
                drag.Preview = WheelOrder.MoveTo(others.Select(o => o.Id).ToList(), drag.Op.Id, index)
                    .Select(id => id == drag.Op.Id ? drag.Op : others.First(o => o.Id == id)).ToList();
                key = "at:" + index;
                SetGhostHint(drag, "");
            }
        }
        if (key == drag.Key)
            return;
        drag.Key = key;
        RenderWheel(drag.Preview ?? _visibleOps, drag.Preview is null ? null : drag.Op.Id);
    }

    private void OnDragUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not null)
            EndDrag(commit: true);
    }

    private void EndDrag(bool commit)
    {
        var drag = _drag;
        _drag = null;
        ReleaseMouseCapture();
        if (drag?.Ghost is { } ghost)
            _overlay.Children.Remove(ghost);
        if (drag is not { Active: true })
            return;
        var id = drag.Op.Id;
        if (commit && drag.TargetWheel is { } wheel)
        {
            var home = drag.Op is RelocatedOperation r ? r.HomeWheel : drag.Op.Wheel;
            if (wheel == home)
                _settings.WheelOverrides.Remove(id);
            else
                _settings.WheelOverrides[id] = wheel;
            _settings.HiddenOperations.Remove(id);
            Save();
        }
        else if (commit && drag.Remove)
        {
            if (!_settings.HiddenOperations.Contains(id))
                _settings.HiddenOperations.Add(id);
            Save();
        }
        else if (commit && drag.Preview is { } arranged)
        {
            _settings.HiddenOperations.Remove(id);
            _settings.OperationOrder = WheelOrder.Merge([.. arranged.Select(o => o.Id)], _settings.OperationOrder);
            Save();
        }
        Rebuild();
    }

    // ------------------------------------------------------------------ for the self-test

    /// <summary>The ids on the wheel as shown now.</summary>
    internal IReadOnlyList<string> VisibleSliceIds => [.. _visibleOps.Select(o => o.Id)];

    /// <summary>The ids in the library (shown first, then hidden).</summary>
    internal Operation LibraryOperation(string id) => _groupOps.First(o => o.Id == id);

    /// <summary>Window coordinates of the middle of slice <paramref name="index"/> on a wheel of <paramref name="count"/>.</summary>
    internal Point SlicePoint(int index, int count) =>
        _wheelCanvas!.TranslatePoint(WheelWindow.SliceCenter(index, count), this);

    internal Point TabPoint(WheelKind wheel)
    {
        var tab = _wheelTabs.First(t => t.Wheel == wheel).Tab;
        return tab.TranslatePoint(new Point(tab.ActualWidth / 2, tab.ActualHeight / 2), this);
    }

    /// <summary>Drags <paramref name="op"/> to <paramref name="to"/> and drops it, as the mouse would.</summary>
    internal void SimulateDrag(Operation op, bool fromWheel, Point from, Point to)
    {
        _drag = new SliceDrag(op, fromWheel, from);
        DragTo(new Point(from.X + 8, from.Y + 8));
        // Between two real mouse moves WPF lays out the redrawn wheel; do the same here.
        UpdateLayout();
        DragTo(to);
        UpdateLayout();
        EndDrag(commit: true);
    }

    /// <summary>The label that follows the cursor while dragging.</summary>
    private static Border Ghost(Operation op)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(op.Label, 13.5, FontWeights.SemiBold, Theme.TextOnAccent, wrap: false));
        panel.Children.Add(new TextBlock { FontSize = 11.5, Foreground = Theme.TextOnAccent, Opacity = 0.85, Tag = "hint" });
        return new Border
        {
            Background = Theme.AccentGradient,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 6, 12, 7),
            Effect = Theme.Shadow(14),
            IsHitTestVisible = false,
            Child = panel,
        };
    }

    private static void SetGhostHint(SliceDrag drag, string text)
    {
        if (drag.Ghost?.Child is StackPanel panel && panel.Children.OfType<TextBlock>().FirstOrDefault(t => Equals(t.Tag, "hint")) is { } hint)
        {
            hint.Text = text;
            hint.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static TextBlock ColumnLabel(string text)
    {
        var label = Text(text, 13, brush: Theme.Muted);
        label.Margin = new Thickness(4, 0, 0, 10);
        return label;
    }

    private Border GroupItem(string name, string glyph, string count, int index)
    {
        var active = index == _group;
        var row = new DockPanel();
        var badge = new Border
        {
            CornerRadius = new CornerRadius(9),
            MinWidth = 26,
            Padding = new Thickness(7, 1, 7, 2),
            Background = active ? Theme.AccentGradient : Theme.Track,
            Child = new TextBlock
            {
                Text = count,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = active ? Theme.TextOnAccent : Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(badge, Dock.Right);
        row.Children.Add(badge);
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = Theme.Icons,
            FontSize = 15,
            Foreground = active ? Theme.Accent : Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(Text(name, 14, active ? FontWeights.SemiBold : FontWeights.Normal, wrap: false));
        var item = new Border
        {
            Background = active ? Theme.AccentSoft : Brushes.Transparent,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 9, 10, 9),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
            Child = row,
        };
        if (!active)
        {
            item.MouseEnter += (_, _) => item.Background = Theme.Hover;
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        }
        item.MouseLeftButtonUp += (_, _) =>
        {
            _group = index;
            Rebuild();
        };
        return item;
    }

}
