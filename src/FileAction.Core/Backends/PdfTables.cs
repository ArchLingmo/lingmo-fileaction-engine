using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

public static partial class PdfStructure
{
    /// <summary>A table read from a PDF: plain-text cells, row by row, and the page it starts on.</summary>
    public sealed record ExtractedTable(int Page, List<List<string>> Rows);

    /// <summary>
    /// Every table in the PDF, in page order. A table that runs on to the next page (same number of
    /// columns, ending at the bottom of one page and starting at the top of the next) stays one table, and
    /// its header printed again on the new page is dropped.
    /// </summary>
    public static List<ExtractedTable> ExtractTables(string path, CancellationToken ct = default)
    {
        using var document = PdfDocument.Open(path);
        var tables = new List<ExtractedTable>();
        var lastEndsPage = false;
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            var found = FindTables(page, words, continued: lastEndsPage && tables.Count > 0).OrderByDescending(t => t.Top).ToList();
            if (found.Count == 0)
            {
                lastEndsPage = false;
                continue;
            }
            var line = words.Average(w => w.BoundingBox.Height) * 2.5;
            var bottom = words.Min(w => w.BoundingBox.Bottom);
            for (var i = 0; i < found.Count; i++)
            {
                var table = found[i];
                // Page headers (a title, a page number) may sit above; allow a few lines.
                var startsPage = i == 0 && words.Count(w => w.BoundingBox.Bottom > table.Top) <= 12;
                if (startsPage && lastEndsPage && tables.Count > 0 && tables[^1].Rows[0].Count == table.Cells[0].Count)
                {
                    var rows = table.Cells;
                    var previous = tables[^1].Rows;
                    if (rows.Count > 0 && rows[0].SequenceEqual(previous[0]))
                        rows = rows.Skip(1).ToList();
                    // A row split by the page break: its rest (most cells empty) belongs to the last row.
                    if (rows.Count > 0 && previous.Count > 1 && rows[0].Count(c => c.Length > 0) * 2 < rows[0].Count)
                    {
                        for (var c = 0; c < rows[0].Count; c++)
                        {
                            if (rows[0][c].Length > 0)
                                previous[^1][c] = (previous[^1][c] + " " + rows[0][c]).Trim();
                        }
                        rows = rows.Skip(1).ToList();
                    }
                    previous.AddRange(rows);
                }
                else
                {
                    tables.Add(new ExtractedTable(page.Number, table.Cells));
                }
            }
            // Page footers (a page number) may sit below; allow a few lines.
            lastEndsPage = words.Count(w => w.BoundingBox.Top < found[^1].Bottom) <= 12 || found[^1].Bottom <= bottom + line;
        }
        return tables;
    }
}

public static partial class PdfBackend
{
    [GeneratedRegex(@"\d,\d")]
    private static partial Regex DecimalCommaRegex();

    [GeneratedRegex(@"\d\.\d{1,2}(?!\d)")]
    private static partial Regex DecimalPointRegex();

    [GeneratedRegex(@"^(-?[\d\s  .,]+?)\s*(?:Kč|CZK|€|EUR|\$|USD|£)$|^(?:€|\$|£)\s*(-?[\d\s  .,]+)$")]
    private static partial Regex MoneyRegex();

    /// <summary>
    /// PDF → XLSX: every table becomes a sheet with real numbers, dates and percentages, so a bank statement
    /// or an invoice can be summed straight away. A PDF without a table (or a scan) is refused with a reason.
    /// </summary>
    public static async Task<string> ToSpreadsheetAsync(FileItem item, OperationContext ctx)
    {
        var tables = await Task.Run(() => PdfStructure.ExtractTables(item.Path, ctx.CancellationToken), ctx.CancellationToken).ConfigureAwait(false);
        if (tables.Count == 0)
            throw new ConversionException(T("V PDF jsem nenašel žádnou tabulku. (Sken bez textové vrstvy ji neobsahuje, zkuste nejdřív Text z obrázku.)",
                                            "No table found in the PDF. (A scan without a text layer has none; try Text from image first.)"), nothingToDo: true);
        // Czech documents write 1 234,50; English ones 1,234.50. Decided once for the whole document.
        var text = string.Join(" ", tables.SelectMany(t => t.Rows).SelectMany(r => r));
        var culture = DecimalCommaRegex().Matches(text).Count >= DecimalPointRegex().Matches(text).Count
            ? CultureInfo.GetCultureInfo("cs-CZ")
            : CultureInfo.InvariantCulture;
        var sheets = tables.Select((t, i) => new SpreadsheetBackend.Sheet(
                tables.Count == 1 ? T("Tabulka", "Table") : T($"Tabulka {i + 1} (str. {t.Page})", $"Table {i + 1} (p. {t.Page})"),
                t.Rows.Select(r => r.Select(cell => SpreadsheetBackend.Interpret(Money(cell, culture), culture)).ToArray()).ToList()))
            .ToList();
        return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, "xlsx",
            tmp => Task.Run(() => SpreadsheetBackend.WriteXlsx(sheets, tmp, headerRow: true), ctx.CancellationToken)).ConfigureAwait(false);
    }

    [GeneratedRegex(@"^-?\d{1,3}(?:\.\d{3})+(?:,\d+)?$")]
    private static partial Regex DotThousandsRegex();

    [GeneratedRegex(@"^(\d{1,2})/(\d{1,2})/(\d{4})$")]
    private static partial Regex SlashDateRegex();

    /// <summary>
    /// "1 234,50 Kč" → "1 234,50": an amount with a currency becomes a number (the currency is in the header
    /// or the document anyway). English thousands separators ("1,234.50") are removed for the invariant reader.
    /// Documents with a decimal comma may group thousands with dots ("1.234,56", Spain, Germany) and write
    /// dates day first with slashes ("02/09/2026").
    /// </summary>
    private static string Money(string cell, CultureInfo culture)
    {
        var value = cell.Trim().Replace('−', '-');
        if (MoneyRegex().Match(value) is { Success: true } m)
            value = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
        if (ReferenceEquals(culture, CultureInfo.InvariantCulture) && Regex.IsMatch(value, @"^-?\d{1,3}(,\d{3})+(\.\d+)?$"))
            value = value.Replace(",", "");
        if (!ReferenceEquals(culture, CultureInfo.InvariantCulture))
        {
            if (DotThousandsRegex().IsMatch(value))
                value = value.Replace(".", "");
            else if (SlashDateRegex().Match(value) is { Success: true } date)
                value = $"{date.Groups[1].Value}.{date.Groups[2].Value}.{date.Groups[3].Value}";
        }
        return value.Length == 0 ? cell : value;
    }
}
