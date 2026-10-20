using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileAction.Core;
using static FileAction.Core.Loc;
using static FileAction.Linux.UI.Widgets;

namespace FileAction.Linux.UI;

/// <summary>Adds or edits a watched folder: the folder, the file type and what to do with new files.</summary>
public sealed class WatchFolderWindow : Window
{
    /// <summary>File-type groups with sample names (two formats each, so "convert to X" shows up even when one sample is X).</summary>
    internal static (string Czech, string English, string[] Samples)[] Groups =>
    [
        ("Obrázky", "Images", ["a.png", "a.jpg", "a.gif"]),
        ("Video", "Video", ["a.mp4", "a.mov"]),
        ("Audio", "Audio", ["a.mp3", "a.wav"]),
        ("PDF", "PDF", ["a.pdf"]),
        ("Dokumenty", "Documents", ["a.docx", "a.md"]),
        ("Prezentace", "Slides", ["a.pptx", "a.odp"]),
        ("Tabulky", "Spreadsheets", ["a.xlsx", "a.csv"]),
        ("Titulky", "Subtitles", ["a.srt", "a.vtt"]),
        ("Archivy", "Archives", ["a.zip", "a.tar"]),
    ];

    private readonly WatchFolder _folder;
    private readonly List<(string Name, List<Operation> Ops)> _groups;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly TextBlock _error = Text("", 13, brush: Palette.Error);
    private int _group;

    /// <param name="operations">Operations that can run unattended (see <see cref="LinuxOperations.ForWatching"/>).</param>
    public WatchFolderWindow(WatchFolder? existing, IReadOnlyList<Operation> operations)
    {
        _folder = existing is null
            ? new WatchFolder()
            : new WatchFolder { Path = existing.Path, OperationId = existing.OperationId, Enabled = existing.Enabled, IncludeSubfolders = existing.IncludeSubfolders };
        _groups = Group(operations);
        _group = Math.Max(0, _groups.FindIndex(g => g.Ops.Any(o => o.Id == _folder.OperationId)));

        ApplyLook(this);
        Title = existing is null ? T("Sledovat složku", "Watch a folder") : T("Upravit sledovanou složku", "Edit watched folder");
        Width = 620;
        Height = 720;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _scroll;
        Build();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
    }

    public WatchFolder Result => _folder;

    /// <summary>Operations per file type, each that works on a single file of the group; the user's presets first.</summary>
    internal static List<(string Name, List<Operation> Ops)> Group(IReadOnlyList<Operation> operations)
    {
        var folder = new FileItem(Path.GetTempPath());
        var groups = new List<(string, List<Operation>)>();
        foreach (var (czech, english, samples) in Groups)
        {
            var ops = operations
                .Where(o => !o.AppliesTo([folder]) && samples.Any(s => o.AppliesTo([new FileItem(s)])))
                .OrderBy(o => o.Id.StartsWith("preset.", StringComparison.Ordinal) ? 0 : 1)
                .ToList();
            if (ops.Count > 0)
                groups.Add((T(czech, english), ops));
        }
        return groups;
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        root.Children.Add(Text(Title ?? "", 22, FontWeight.SemiBold));
        var intro = Text(T("Každý nový soubor, který do složky přibude, se zpracuje sám. Originály zůstávají beze změny.",
                           "Every new file that lands in the folder is processed on its own. The originals are never changed."), 13, brush: Palette.Muted);
        intro.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(intro);

        root.Children.Add(FieldLabel(T("Složka", "Folder")));
        var empty = string.IsNullOrEmpty(_folder.Path);
        var path = Text(empty ? T("Zatím nevybraná", "Not chosen yet") : _folder.Path, 13.5, brush: empty ? Palette.Muted : Palette.Text, wrap: false);
        path.VerticalAlignment = VerticalAlignment.Center;
        path.Margin = new Thickness(12, 0, 0, 0);
        var row = new DockPanel();
        var pick = Button(T("Vybrat…", "Choose…"), () => _ = PickAsync());
        DockPanel.SetDock(pick, Dock.Left);
        row.Children.Add(pick);
        row.Children.Add(path);
        root.Children.Add(row);

        root.Children.Add(FieldLabel(T("Typ souborů", "File type")));
        root.Children.Add(Segmented([.. _groups.Select((g, i) => (g.Name, i))], _group, i =>
        {
            _group = i;
            Rebuild();
        }));

        root.Children.Add(FieldLabel(T("Co s nimi udělat", "What to do with them")));
        var list = new StackPanel();
        foreach (var op in _groups.Count > 0 ? _groups[_group].Ops : [])
            list.Children.Add(OperationRow(op, list.Children.Count > 0));
        root.Children.Add(new Border
        {
            Background = Palette.Panel,
            BorderBrush = Palette.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6, 4, 6, 4),
            Child = list,
        });

        root.Children.Add(OptionRow(T("Včetně podsložek", "Include subfolders"), _folder.IncludeSubfolders, v => _folder.IncludeSubfolders = v));

        _error.Margin = new Thickness(0, 14, 0, 0);
        (_error.Parent as Panel)?.Children.Remove(_error);
        root.Children.Add(_error);
        root.Children.Add(DialogButtons(Save, () => Close(false)));
        _scroll.Content = root;
    }

    private void Rebuild()
    {
        var offset = _scroll.Offset;
        Build();
        _scroll.UpdateLayout();
        _scroll.Offset = offset;
    }

    private Border OperationRow(Operation op, bool separator)
    {
        var selected = op.Id == _folder.OperationId;
        var mark = Text(selected ? "✓" : "", 15, FontWeight.Bold, Palette.Accent, wrap: false);
        mark.Width = 22;
        mark.VerticalAlignment = VerticalAlignment.Center;
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(Text(op.Label, 14, selected ? FontWeight.SemiBold : FontWeight.Normal, wrap: false));
        var detail = WheelWindow.WheelName(op.Wheel) + (string.IsNullOrEmpty(op.Subtitle) ? "" : " · " + op.Subtitle);
        label.Children.Add(Text(detail, 11.5, brush: Palette.Muted, wrap: false));
        var row = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
        DockPanel.SetDock(mark, Dock.Right);
        row.Children.Add(mark);
        row.Children.Add(label);
        var item = new Border
        {
            BorderBrush = Palette.PanelBorder,
            BorderThickness = new Thickness(0, separator ? 1 : 0, 0, 0),
            Background = selected ? Palette.Track : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row,
        };
        item.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            _folder.OperationId = op.Id;
            Rebuild();
        };
        return item;
    }

    private async Task PickAsync()
    {
        var options = new FolderPickerOpenOptions { Title = T("Sledovaná složka", "Watched folder") };
        if (!string.IsNullOrEmpty(_folder.Path) && Directory.Exists(_folder.Path))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_folder.Path);
        var folders = await StorageProvider.OpenFolderPickerAsync(options);
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            _folder.Path = path;
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
            Close(true);
    }

    /// <summary>Selects a folder and operation without the pickers (self-test).</summary>
    internal void Preselect(string path, string operationId)
    {
        _folder.Path = path;
        _folder.OperationId = operationId;
        _group = Math.Max(0, _groups.FindIndex(g => g.Ops.Any(o => o.Id == operationId)));
        Rebuild();
    }
}
