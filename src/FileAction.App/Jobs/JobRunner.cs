using FileAction.App.Actions;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.Jobs;

/// <summary>Runs an operation in the background and shows its progress card.</summary>
internal sealed class JobRunner(AppSettings settings, ToolLocator tools)
{
    private JobsWindow? _window;

    public async void Start(Operation op, IReadOnlyList<FileItem> items)
    {
        if (op is AppAction action)
        {
            try
            {
                await action.RunAsync(items);
            }
            catch (Exception ex)
            {
                Notifications.Show(op.Label, ex.Message, error: true);
            }
            return;
        }

        IReadOnlyDictionary<string, string> parameters = new Dictionary<string, string>();
        if (op.Parameters.Count > 0)
        {
            var dialog = new ParameterDialog(op, items);
            if (dialog.ShowDialog() != true)
                return;
            parameters = dialog.Values;
        }

        _window ??= new JobsWindow();
        using var cts = new CancellationTokenSource();
        var title = op.Wheel == WheelKind.Convert && op.Label.ToUpperInvariant() == op.Label ? T($"Převod na {op.Label}", $"Convert to {op.Label}") : op.Label;
        var detail = items.Count == 1 ? items[0].Name : Items(items.Count);
        var card = _window.Add(title, detail, iconPath: items[0].Path, cancel: () =>
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });
        var context = new OperationContext
        {
            Tools = tools,
            Progress = new Progress<double>(card.Report),
            CancellationToken = cts.Token,
            OutputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? null : settings.OutputDirectory,
            Parameters = parameters,
            Settings = settings.Quality,
        };

        try
        {
            var result = await Task.Run(() => op.ExecuteAsync(items, context));
            card.Complete(result.Outputs, result.Message, result.Note);
            if (settings.RevealResult && result.Outputs.Count > 0)
                JobCard.Reveal(result.Outputs[0]);
        }
        catch (OperationCanceledException)
        {
            card.Cancelled();
        }
        catch (Exception ex)
        {
            card.Fail(Friendly(ex));
        }
    }

    private static string Friendly(Exception ex) => ex switch
    {
        ConversionException => ex.Message,
        UnauthorizedAccessException => T("Do cílové složky nelze zapisovat. Zkuste v nastavení zvolit jinou výstupní složku.", "Cannot write to the target folder. Choose another output folder in Settings."),
        System.IO.IOException => T($"Chyba souboru: {ex.Message}", $"File error: {ex.Message}"),
        ImageMagick.MagickException => T($"Obrázek se nepodařilo zpracovat: {ex.Message}", $"Could not process the image: {ex.Message}"),
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}
