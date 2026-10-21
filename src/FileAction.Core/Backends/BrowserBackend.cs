using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

/// <summary>
/// HTML → PDF by the browser's own print engine (Edge or Chrome without a window). It lays out a web page exactly
/// as the browser shows it: code blocks, tables, pictures and any language's letters.
/// </summary>
public static class BrowserBackend
{
    /// <summary>Print styles for Markdown turned into a page: A4 margins and a readable body, not pandoc's narrow column.</summary>
    public const string MarkdownCss = """
        @page { size: A4; margin: 2cm 2.2cm; }
        html { font-size: 11pt; }
        body { max-width: none; margin: 0; padding: 0; font-family: "Segoe UI", "Helvetica Neue", Arial, "Noto Sans", sans-serif; line-height: 1.5; color: #1a1a1a; }
        h1, h2, h3, h4 { line-height: 1.25; break-after: avoid; }
        h1 { font-size: 1.9em; margin-top: 0; }
        pre, code { font-family: "Cascadia Mono", Consolas, "DejaVu Sans Mono", monospace; font-size: 0.9em; }
        pre { background: #f5f5f5; padding: 0.7em 0.9em; border-radius: 4px; white-space: pre-wrap; break-inside: avoid; }
        :not(pre) > code { background: #f2f2f2; padding: 0.1em 0.3em; border-radius: 3px; }
        table { border-collapse: collapse; margin: 1em 0; break-inside: auto; }
        th, td { border: 1px solid #ccc; padding: 0.3em 0.6em; text-align: left; vertical-align: top; }
        th { background: #f2f2f2; }
        tr { break-inside: avoid; }
        img { max-width: 100%; break-inside: avoid; }
        blockquote { margin: 1em 0; padding-left: 1em; border-left: 3px solid #ccc; color: #555; }
        a { color: #0b5cad; }
        """;

    /// <summary>Prints <paramref name="html"/> (a local file) to <paramref name="output"/>.</summary>
    public static async Task PrintToPdfAsync(OperationContext ctx, string html, string output)
    {
        var browser = ctx.Tools.Browser ?? throw new ConversionException(T(
            "Pro tisk do PDF je potřeba Microsoft Edge nebo Google Chrome.", "Printing to PDF needs Microsoft Edge or Google Chrome."));
        // A profile of its own: otherwise a running Edge takes the job over and the command returns at once.
        var profile = OutputPaths.TempDirectory();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await ProcessRunner.RunAsync(browser,
                [
                    "--headless", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                    "--user-data-dir=" + profile,
                    "--no-pdf-header-footer", "--print-to-pdf-no-header",
                    "--print-to-pdf=" + Path.GetFullPath(output),
                    new Uri(Path.GetFullPath(html)).AbsoluteUri,
                ], timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ctx.CancellationToken.IsCancellationRequested)
        {
            throw new ConversionException(T("Prohlížeč stránku do PDF nevytiskl včas.", "The browser did not print the page in time."));
        }
        finally
        {
            OutputPaths.TryDelete(profile);
        }
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
            throw new ConversionException(T("Prohlížeč PDF nevytvořil.", "The browser did not produce the PDF."));
    }
}
