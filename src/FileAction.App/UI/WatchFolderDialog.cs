using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>Picks a folder, a file type and what to do with new files of that type.</summary>
internal sealed class WatchFolderDialog : Window
{
    private readonly WatchFolder _folder;
    private readonly List<(string Name, string Glyph, List<Operation> Ops)> _groups;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _error = Text("", 13, brush: Theme.Error);
    private int _group;

    public WatchFolderDialog(WatchFolder? existing, IReadOnlyList<Operation> operations)
    {
        _folder = existing is null
            ? new WatchFolder()
            : new WatchFolder { Path = existing.Path, OperationId = existing.OperationId, Enabled = existing.Enabled, IncludeSubfolders = existing.IncludeSubfolders };
        // Only single-file operations without questions can run unattended.
        _groups = SettingsWindow.Group([.. operations.Where(o => o.Wheel != WheelKind.Actions && o.Parameters.Count == 0)])
            .Select(g => (g.Name, g.Glyph, Ops: g.Ops.Where(o => SampleFor(g.Name, o))
                // The user's own presets first: they are what a watched folder is usually for.
                .OrderBy(o => o.Id.StartsWith("preset.", StringComparison.Ordinal) ? 0 : 1).ToList()))
            .Where(g => g.Ops.Count > 0)
            .ToList();
        _group = Math.Max(0, _groups.FindIndex(g => g.Ops.Any(o => o.Id == _folder.OperationId)));

        Theme.Refresh();
        Title = existing is null ? T("Sledovat složku", "Watch a folder") : T("Upravit sledovanou složku", "Edit watched folder");
        Width = 640;
        Height = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/FileAction.ico"));
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;
        Content = _scroll;
        Build();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        };
    }

    public WatchFolder Result => _folder;

    /// <summary>True when the operation works on a single file of the group (rules run per file).</summary>
    private static bool SampleFor(string groupName, Operation op)
    {
        var group = SettingsWindow.Groups.FirstOrDefault(g => T(g.Czech, g.English) == groupName);
        return group.Samples is null
            ? op.AppliesTo([new FileItem("a.bin")]) || op.AppliesTo([new FileItem("a.jpg")])
            : group.Samples.Any(s => op.AppliesTo([new FileItem(s)]));
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        var title = Text(Title, 24, FontWeights.SemiBold);
        title.FontFamily = Theme.DisplayFont;
        root.Children.Add(title);
        var intro = Text(T("Každý nový soubor, který do složky přibude, se zpracuje sám. Originály zůstávají beze změny.",
                           "Every new file that lands in the folder is processed on its own. The originals are never changed."), 13, brush: Theme.Muted);
        intro.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(intro);

        root.Children.Add(FieldLabel(T("Složka", "Folder")));
        var path = Text(string.IsNullOrEmpty(_folder.Path) ? T("Zatím nevybraná", "Not chosen yet") : _folder.Path, 13.5,
            brush: string.IsNullOrEmpty(_folder.Path) ? Theme.Muted : Theme.Text, wrap: false);
        path.VerticalAlignment = VerticalAlignment.Center;
        path.Margin = new Thickness(12, 0, 0, 0);
        var row = new DockPanel();
        var pick = Button(T("Vybrat…", "Choose…"), Pick, glyph: Glyphs.Folder);
        DockPanel.SetDock(pick, Dock.Left);
        row.Children.Add(pick);
        row.Children.Add(path);
        root.Children.Add(row);

        root.Children.Add(FieldLabel(T("Typ souborů", "File type")));
        var types = new WrapPanel();
        for (var i = 0; i < _groups.Count; i++)
            types.Children.Add(Chip(_groups[i].Name, i == _group, i));
        root.Children.Add(types);

        root.Children.Add(FieldLabel(T("Co s nimi udělat", "What to do with them")));
        var list = new StackPanel();
        foreach (var op in _groups.Count > 0 ? _groups[_group].Ops : [])
            list.Children.Add(OperationRow(op, list.Children.Count > 0));
        root.Children.Add(new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6, 4, 6, 4),
            Child = list,
        });

        var subfolders = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var toggle = Toggle(_folder.IncludeSubfolders, v => _folder.IncludeSubfolders = v, T("Včetně podsložek", "Include subfolders"));
        DockPanel.SetDock(toggle, Dock.Right);
        subfolders.Children.Add(toggle);
        subfolders.Children.Add(Text(T("Včetně podsložek", "Include subfolders"), 14));
        root.Children.Add(subfolders);

        _error.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(_error);
        var save = Button(T("Uložit", "Save"), Save, primary: true, glyph: "");
        var cancel = Button(T("Zrušit", "Cancel"), () => DialogResult = false);
        save.IsDefault = true;
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        _scroll.Content = root;
    }

    private void Rebuild()
    {
        var offset = _scroll.VerticalOffset;
        Build();
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(offset);
    }

    private FrameworkElement Chip(string name, bool active, int index)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(14, 6, 14, 7),
            Margin = new Thickness(0, 0, 8, 8),
            Background = active ? Theme.AccentGradient : Theme.Card,
            BorderBrush = active ? Brushes.Transparent : Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = Text(name, 13, active ? FontWeights.SemiBold : FontWeights.Normal, active ? Theme.TextOnAccent : Theme.Text, wrap: false),
        };
        var button = Clickable(chip, name, () =>
        {
            _group = index;
            Rebuild();
        });
        if (active)
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, T("vybráno", "selected"));
        return button;
    }

    private UIElement OperationRow(Operation op, bool separator)
    {
        var selected = op.Id == _folder.OperationId;
        var mark = new TextBlock
        {
            Text = selected ? "" : "",
            FontFamily = Theme.Icons,
            FontSize = 14,
            Foreground = Theme.Accent,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(Text(op.Label, 14, selected ? FontWeights.SemiBold : FontWeights.Normal, wrap: false));
        var detail = AppSettings.WheelName(op.Wheel) + (string.IsNullOrEmpty(op.Subtitle) ? "" : " · " + op.Subtitle);
        label.Children.Add(Text(detail, 11.5, brush: Theme.Muted, wrap: false));
        var row = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
        DockPanel.SetDock(mark, Dock.Right);
        row.Children.Add(mark);
        row.Children.Add(label);
        var item = new Border
        {
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(0, separator ? 1 : 0, 0, 0),
            Background = selected ? Theme.AccentSoft : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = row,
        };
        var button = Clickable(item, op.Label + (string.IsNullOrEmpty(op.Subtitle) ? "" : ", " + op.Subtitle), () =>
        {
            _folder.OperationId = op.Id;
            Rebuild();
        });
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (selected)
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, T("vybráno", "selected"));
        return button;
    }

    private void Pick()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("Sledovaná složka", "Watched folder") };
        if (!string.IsNullOrEmpty(_folder.Path) && Directory.Exists(_folder.Path))
            dialog.InitialDirectory = _folder.Path;
        if (dialog.ShowDialog(this) == true)
        {
            _folder.Path = dialog.FolderName;
            Rebuild();
        }
    }

    private void Save()
    {
        if (string.IsNullOrEmpty(_folder.Path) || !Directory.Exists(_folder.Path))
            _error.Text = T("Vyberte složku.", "Choose a folder.");
        else if (string.IsNullOrEmpty(_folder.OperationId))
            _error.Text = T("Vyberte, co se má se soubory udělat.", "Choose what to do with the files.");
        else
            DialogResult = true;
    }
}
