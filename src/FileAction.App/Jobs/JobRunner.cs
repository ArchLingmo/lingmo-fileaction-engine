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

    /// <summary>
    /// At most two jobs run at once; the rest wait their turn (a watched folder receiving 200 videos must not
    /// start 200 FFmpeg processes, each using every core).
    /// </summary>
    private static readonly SemaphoreSlim Slots = new(2);

    /// <summary>Files a job created (watched folders use it to skip their own results).</summary>
    public event Action<IReadOnlyList<string>>? Produced;

    public void Start(Operation op, IReadOnlyList<FileItem> items) => Start(op, items, null);

    /// <param name="parameters">Values for the operation's questions; when given (repeat last) no dialog is shown.</param>
    /// <param name="automatic">Started by a watched folder: not remembered for "repeat last" and never revealed.</param>
    public async void Start(Operation op, IReadOnlyList<FileItem> items, IReadOnlyDictionary<string, string>? parameters, bool automatic = false)
    {
        // A slice moved to another wheel runs exactly like at home (app actions are recognised by type below).
        op = RelocatedOperation.Unwrap(op);
        if (op is NeedsToolOperation needs)
        {
            if (await DownloadToolsAsync(needs.Missing, ask: true))
                Start(needs.Inner, items, parameters);
            return;
        }

        if (op is AppAction action)
        {
            Remember(op, null);
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

        parameters ??= new Dictionary<string, string>();
        if (op.Parameters.Count > 0 && parameters.Count == 0)
        {
            var dialog = new ParameterDialog(op, items);
            if (dialog.ShowDialog() != true)
                return;
            parameters = dialog.Values;
        }
        if (!automatic)
            Remember(op, parameters);

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

        var slot = false;
        try
        {
            if (!Slots.Wait(0))
            {
                card.Waiting();
                await Slots.WaitAsync(cts.Token);
                card.Report(0);
            }
            slot = true;
            OperationResult result;
            try
            {
                result = await Task.Run(() => op.ExecuteAsync(items, context));
            }
            catch (Exception ex) when (context.OutputDirectory is null && IsReadOnlyTarget(ex))
            {
                // The file sits somewhere we can't write (a DVD, Program Files, a read-only share):
                // put the result into Downloads\FileAction instead of failing, and say so.
                var fallback = new OperationContext
                {
                    Tools = tools,
                    Progress = context.Progress,
                    CancellationToken = cts.Token,
                    OutputDirectory = KnownFolders.Incoming,
                    Parameters = parameters,
                    Settings = settings.Quality,
                };
                card.Report(0);
                result = await Task.Run(() => op.ExecuteAsync(items, fallback));
                result = result with
                {
                    Note = T($"Do původní složky nejde zapisovat, výsledek je v {KnownFolders.Incoming}.",
                             $"The original folder is read-only; the result is in {KnownFolders.Incoming}."),
                };
            }
            Produced?.Invoke(result.Outputs);
            card.Complete(result.Outputs, result.Message, result.Note);
            if (!automatic && settings.RevealResult && result.Outputs.Count > 0)
                JobCard.Reveal(result.Outputs[0]);
        }
        catch (OperationCanceledException)
        {
            card.Cancelled();
        }
        catch (ConversionException ex) when (ex.NothingToDo)
        {
            // "Already small enough", "already 720p": a neutral note, not a red error.
            card.Complete([], null, ex.Message);
        }
        catch (Exception ex)
        {
            card.Fail(Friendly(ex));
        }
        finally
        {
            if (slot)
                Slots.Release();
        }
    }

    /// <summary>Downloads FFmpeg/Pandoc with a progress card; true when everything is installed.</summary>
    public async Task<bool> DownloadToolsAsync(IReadOnlyList<ExternalTool> missing, bool ask)
    {
        if (ask)
        {
            var names = string.Join(" + ", missing.Select(ToolLocator.DisplayName));
            var size = string.Join(" + ", missing.Select(ToolInstaller.DownloadSize));
            var owner = ForegroundHelper.Host();
            var answer = System.Windows.MessageBox.Show(owner,
                T($"Pro tento převod je potřeba {names} (stažení {size}). Stáhnout teď? Stačí jednou.",
                  $"This conversion needs {names} ({size} download). Download it now? It's a one-time step."),
                "FileAction", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            owner.Close();
            if (answer != System.Windows.MessageBoxResult.Yes)
                return false;
        }

        _window ??= new JobsWindow();
        foreach (var tool in missing)
        {
            using var cts = new CancellationTokenSource();
            var card = _window.Add(T($"Stahuji {ToolLocator.DisplayName(tool)}", $"Downloading {ToolLocator.DisplayName(tool)}"), ToolInstaller.DownloadSize(tool), () =>
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            });
            try
            {
                await ToolInstaller.InstallAsync(tool, new Progress<double>(card.Report), cts.Token);
                tools.Refresh();
                card.Complete([], null, T("Připraveno, už se nebude stahovat znovu.", "Ready; it won't be downloaded again."));
            }
            catch (OperationCanceledException)
            {
                card.Cancelled();
                return false;
            }
            catch (Exception ex)
            {
                card.Fail(Friendly(ex));
                return false;
            }
        }
        return true;
    }

    private void Remember(Operation op, IReadOnlyDictionary<string, string>? parameters)
    {
        settings.LastOperationId = op.Id;
        settings.LastParameters = parameters is { Count: > 0 } ? new Dictionary<string, string>(parameters) : null;
        settings.Save();
    }

    /// <summary>Writing failed because the target folder (or disk) refuses it, not because of the file itself.</summary>
    private static bool IsReadOnlyTarget(Exception ex) => ex switch
    {
        UnauthorizedAccessException => true,
        // ERROR_WRITE_PROTECT (DVD, locked SD card) and ERROR_ACCESS_DENIED surfaced as IOException.
        System.IO.IOException io => (io.HResult & 0xFFFF) is 19 or 5,
        AggregateException agg => agg.InnerExceptions.Any(IsReadOnlyTarget),
        _ => ex.InnerException is { } inner && IsReadOnlyTarget(inner),
    };

    /// <summary>A sentence a person can act on; no exception type names.</summary>
    private static string Friendly(Exception ex) => ex switch
    {
        ConversionException => ex.Message,
        UnauthorizedAccessException => T("Do cílové složky nelze zapisovat. Zkuste v nastavení zvolit jinou výstupní složku.", "Cannot write to the target folder. Choose another output folder in Settings."),
        System.IO.FileNotFoundException or System.IO.DirectoryNotFoundException =>
            T("Soubor mezitím zmizel nebo byl přesunut.", "The file was moved or deleted in the meantime."),
        System.IO.PathTooLongException => T("Cesta k souboru je příliš dlouhá. Zkuste ho přesunout výš ve složkách.", "The path is too long. Try moving the file higher up in its folders."),
        System.IO.IOException io when (io.HResult & 0xFFFF) is 32 or 33 =>
            T("Soubor má otevřený jiný program. Zavřete ho a zkuste to znovu.", "Another program has the file open. Close it and try again."),
        System.IO.IOException io when (io.HResult & 0xFFFF) is 112 or 39 =>
            T("Na disku došlo místo.", "The disk is full."),
        System.IO.IOException => T($"Chyba při práci se souborem: {ex.Message}", $"File error: {ex.Message}"),
        ImageMagick.MagickException => T("Obrázek se nepodařilo přečíst; je možná poškozený nebo v nepodporované variantě formátu.",
                                         "Could not read the image; it may be damaged or in an unsupported variant of the format."),
        OutOfMemoryException => T("Soubor je na převod příliš velký (došla paměť).", "The file is too large to convert (out of memory)."),
        _ => T($"Něco se nepovedlo: {ex.Message}", $"Something went wrong: {ex.Message}"),
    };
}
