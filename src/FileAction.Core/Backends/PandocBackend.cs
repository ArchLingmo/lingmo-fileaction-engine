using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

/// <summary>Document conversions through pandoc.</summary>
public static class PandocBackend
{
    public static string ReaderFor(string format) => format switch
    {
        "md" or "txt" => "markdown",
        "html" => "html",
        "docx" => "docx",
        "odt" => "odt",
        "epub" => "epub",
        "rtf" => "rtf",
        _ => throw new ConversionException(T($"Pandoc neumí číst formát {format}.", $"Pandoc cannot read {format}.")),
    };

    public static string WriterFor(string format) => format switch
    {
        "md" => "gfm",
        "txt" => "plain",
        "html" => "html5",
        "docx" => "docx",
        "odt" => "odt",
        "epub" => "epub3",
        "rtf" => "rtf",
        _ => throw new ConversionException(T($"Pandoc neumí zapsat formát {format}.", $"Pandoc cannot write {format}.")),
    };

    public static async Task ConvertAsync(OperationContext ctx, string input, string inputFormat, string output, string format,
        IEnumerable<string>? extraArgs = null)
    {
        var pandoc = ctx.Tools.Require(ExternalTool.Pandoc);
        var inputDir = Path.GetDirectoryName(Path.GetFullPath(input));
        string? temp = null;
        try
        {
            var reader = ReaderFor(inputFormat);
            if (inputFormat is "txt" or "md")
            {
                // Pandoc only reads UTF-8; older Czech text files are Windows-1250. Plain text is escaped so that
                // *, _ and # stay characters, and every line break stays a line break.
                var bytes = await File.ReadAllBytesAsync(input, ctx.CancellationToken).ConfigureAwait(false);
                var text = SpreadsheetBackend.DetectEncoding(bytes).GetString(bytes).TrimStart('\uFEFF');
                if (inputFormat == "txt")
                {
                    text = EscapePlainText(text);
                    reader = "markdown+hard_line_breaks-auto_identifiers";
                }
                temp = OutputPaths.TempDirectory();
                var utf8 = Path.Combine(temp, Path.GetFileNameWithoutExtension(input) + ".md");
                await File.WriteAllTextAsync(utf8, text, new System.Text.UTF8Encoding(false), ctx.CancellationToken).ConfigureAwait(false);
                input = utf8;
            }
            var args = new List<string>
            {
                input,
                "-f", reader,
                "-t", format == "md" && inputFormat == "epub" ? "gfm-raw_html" : WriterFor(format),
                "-o", output,
            };
            if (format is "html" or "rtf" or "epub")
                args.Add("--standalone");
            if (format == "html")
                args.Add("--embed-resources");
            // The page title (browser tab, e-book metadata) without printing the file name as a heading.
            if (format is "html")
                args.AddRange(["--metadata", "pagetitle=" + Formats.BaseNameOf(input)]);
            if (format is "epub")
                args.AddRange(["--metadata", "title=" + Formats.BaseNameOf(input)]);
            if (format is "txt")
                args.Add("--wrap=none");
            var workingDirectory = inputDir;
            if (format == "md" && inputFormat is "docx" or "odt" or "epub")
            {
                // Pictures land next to the Markdown file and are linked relatively, so the folder can be moved.
                workingDirectory = Path.GetDirectoryName(Path.GetFullPath(output))!;
                args.Add("--extract-media=" + Formats.BaseNameOf(input) + " - media");
            }
            if (inputDir is not null)
                args.Add("--resource-path=" + inputDir);
            if (extraArgs is not null)
                args.AddRange(extraArgs);
            await ProcessRunner.RunAsync(pandoc, args, ctx.CancellationToken, workingDirectory: workingDirectory).ConfigureAwait(false);
        }
        finally
        {
            if (temp is not null)
                OutputPaths.TryDelete(temp);
        }
    }

    /// <summary>Plain text as Markdown that reads back as the same text.</summary>
    private static string EscapePlainText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 64);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '#' or '[' or ']' or '<' or '>' or '|' or '$' or '~' or '^' or '-' or '+' or '!')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
