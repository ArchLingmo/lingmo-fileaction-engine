using System.IO;
using System.Windows;
using System.Windows.Controls;
using FileAction.Core;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>The Presets and Watched folders pages.</summary>
internal sealed partial class SettingsWindow
{
    /// <summary>Ready-made presets offered while the list is empty.</summary>
    private static Preset[] PresetTemplates =>
    [
        new() { Name = T("Web 1920", "Web 1920"), Kind = PresetKind.Image, Format = "jpg", Quality = 80, MaxSide = 1920, StripMetadata = true },
        new() { Name = "WebP 1280", Kind = PresetKind.Image, Format = "webp", Quality = 75, MaxSide = 1280 },
        new() { Name = T("Poloviční", "Half size"), Kind = PresetKind.Image, Format = Preset.SameFormat, Quality = 90, Percent = 50 },
        new() { Name = T("MP4 malé", "MP4 small"), Kind = PresetKind.Video, Format = "mp4", Height = 720, VideoQuality = VideoQuality.Small },
        new() { Name = "MP3 128k", Kind = PresetKind.Audio, Format = "mp3", Bitrate = 128 },
    ];

    // ------------------------------------------------------------------ presets

    private void PresetsPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.Presets),
            T("Vlastní výseče s pevným nastavením, třeba „JPG 70 % max 1920 px“ nebo „MP4 720p“. Objeví se na kolech i ve sledovaných složkách.",
              "Slices of your own with fixed settings, like “JPG 70 % max 1920 px” or “MP4 720p”. They appear on the wheels and in watched folders."));
        var add = Button(T("Nová předvolba", "New preset"), () => EditPreset(null), primary: true, glyph: "");
        add.Margin = new Thickness(0, 14, 0, 6);
        root.Children.Add(add);

        if (_settings.Presets.Count == 0)
        {
            root.Children.Add(SectionHeader(T("Rychlý start", "Quick start")));
            foreach (var template in PresetTemplates)
            {
                var t = template;
                root.Children.Add(Card(t.Name, PresetOperations.Summary(t),
                    Button(T("Přidat", "Add"), () => AddPreset(t), glyph: ""), glyph: PresetGlyph(t)));
            }
            return;
        }

        root.Children.Add(SectionHeader(T("Vaše předvolby", "Your presets")));
        foreach (var preset in _settings.Presets.ToList())
        {
            var p = preset;
            var wheel = p.Format == Preset.SameFormat ? WheelKind.Tools : WheelKind.Convert;
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Button(T("Upravit", "Edit"), () => EditPreset(p), glyph: ""));
            var delete = Button(T("Smazat", "Delete"), () => DeletePreset(p), glyph: "");
            delete.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(delete);
            var name = string.IsNullOrWhiteSpace(p.Name) ? PresetOperations.Summary(p) : p.Name;
            root.Children.Add(Card(name, $"{AppSettings.WheelName(wheel)} · {PresetOperations.Summary(p)}", actions, glyph: PresetGlyph(p)));
        }
    }

    private static string PresetGlyph(Preset p) => p.Kind switch
    {
        PresetKind.Image => Glyphs.Photo,
        PresetKind.Video => Glyphs.Video,
        _ => Glyphs.Audio,
    };

    private void AddPreset(Preset preset)
    {
        _settings.Presets.Add(preset);
        Save();
        Rebuild();
    }

    private void EditPreset(Preset? existing)
    {
        var dialog = new PresetDialog(existing) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var index = existing is null ? -1 : _settings.Presets.IndexOf(existing);
        if (index >= 0)
            _settings.Presets[index] = dialog.Result;
        else
            _settings.Presets.Add(dialog.Result);
        Save();
        Rebuild();
    }

    private void DeletePreset(Preset preset)
    {
        var id = "preset." + preset.Id;
        var used = _settings.WatchFolders.Where(f => f.OperationId == id).ToList();
        var question = used.Count == 0
            ? T($"Smazat předvolbu „{preset.Name}“?", $"Delete the preset “{preset.Name}”?")
            : T($"Předvolbu „{preset.Name}“ používá {used.Count}× sledovaná složka. Ta se vypne. Smazat?",
                $"The preset “{preset.Name}” is used by {used.Count} watched folder(s); they will be turned off. Delete?");
        if (MessageBox.Show(this, question, "FileAction", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _settings.Presets.Remove(preset);
        _settings.OperationOrder.Remove(id);
        _settings.WheelOverrides.Remove(id);
        _settings.HiddenOperations.Remove(id);
        foreach (var folder in used)
            folder.Enabled = false;
        Save();
        Rebuild();
    }

    // ------------------------------------------------------------------ watched folders

    private void WatchFoldersPage(Panel root)
    {
        PageHeader(root, PageName(SettingsPage.WatchFolders),
            T("Co přibude do vybrané složky, to se samo zpracuje. Třeba snímky obrazovky do WebP nebo fotky z telefonu z HEIC do JPG.",
              "Whatever lands in a chosen folder is processed on its own. For example screenshots to WebP, or phone photos from HEIC to JPG."));
        var add = Button(T("Přidat složku", "Add folder"), () => EditWatchFolder(null), primary: true, glyph: "");
        add.Margin = new Thickness(0, 14, 0, 6);
        root.Children.Add(add);

        if (_settings.WatchFolders.Count > 0)
        {
            root.Children.Add(SectionHeader(T("Sledované složky", "Watched folders")));
            var operations = Operations?.Invoke() ?? [];
            foreach (var folder in _settings.WatchFolders.ToList())
            {
                var f = folder;
                var op = operations.FirstOrDefault(o => o.Id == f.OperationId);
                var what = op is null ? T("akce chybí", "action missing") : op.Label + (string.IsNullOrEmpty(op.Subtitle) ? "" : $" ({op.Subtitle})");
                var exists = Directory.Exists(f.Path);
                var description = $"{f.Path} → {what}"
                    + (f.IncludeSubfolders ? T(" · i podsložky", " · with subfolders") : "")
                    + (exists ? "" : T(" · složka neexistuje", " · folder not found"));
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var toggle = Toggle(f.Enabled, v =>
                {
                    f.Enabled = v;
                    Save();
                }, T($"Sledovat {f.Path}", $"Watch {f.Path}"));
                toggle.VerticalAlignment = VerticalAlignment.Center;
                toggle.Margin = new Thickness(0, 0, 14, 0);
                actions.Children.Add(toggle);
                actions.Children.Add(Button(T("Upravit", "Edit"), () => EditWatchFolder(f), glyph: ""));
                var delete = Button(T("Smazat", "Delete"), () =>
                {
                    _settings.WatchFolders.Remove(f);
                    Save();
                    Rebuild();
                }, glyph: "");
                delete.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(delete);
                var name = Path.GetFileName(f.Path.TrimEnd('\\', '/'));
                root.Children.Add(Card(string.IsNullOrEmpty(name) ? f.Path : name, description, actions, glyph: Glyphs.Folder));
            }
        }

        root.Children.Add(SectionHeader(T("Jak to funguje", "How it works")));
        root.Children.Add(Card(T("Jen nové soubory", "Only new files"),
            T("Soubor se zpracuje, až je celý stažený nebo zkopírovaný. Co ve složce už bylo, zůstane, jak je. Výsledky FileAction se znovu nezpracovávají.",
              "A file is processed once it has finished downloading or copying. What was already in the folder stays as it is. FileAction's own results are not processed again."),
            null, glyph: ""));
        root.Children.Add(Card(T("Kam se ukládá", "Where results go"),
            T("Podle nastavení Obecné → Kam ukládat. Originály zůstávají beze změny. Sleduje se, jen když FileAction běží.",
              "As set in General → Save results. Originals are never changed. Folders are watched only while FileAction is running."),
            null, glyph: Glyphs.Folder));
    }

    private void EditWatchFolder(WatchFolder? existing)
    {
        var dialog = new WatchFolderDialog(existing, Operations?.Invoke() ?? []) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var index = existing is null ? -1 : _settings.WatchFolders.IndexOf(existing);
        if (index >= 0)
            _settings.WatchFolders[index] = dialog.Result;
        else
            _settings.WatchFolders.Add(dialog.Result);
        Save();
        Rebuild();
    }
}
