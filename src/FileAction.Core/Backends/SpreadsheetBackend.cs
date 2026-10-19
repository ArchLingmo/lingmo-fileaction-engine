using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ExcelDataReader;
using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

/// <summary>
/// XLSX/XLSM/XLS/CSV/TSV conversions without Office (ClosedXML + ExcelDataReader). Cells keep their type:
/// numbers stay numbers, dates stay dates, so nothing turns into text on the way.
/// </summary>
public static partial class SpreadsheetBackend
{
    static SpreadsheetBackend()
    {
        // ExcelDataReader needs legacy code pages for old XLS files.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>A sheet of typed cells: null, string, double, DateTime, TimeSpan or bool.</summary>
    public sealed record Sheet(string Name, List<object?[]> Rows);

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    public static IReadOnlyList<Sheet> Read(string path)
    {
        var format = Formats.KeyOf(path);
        return format switch
        {
            "csv" or "tsv" => [ReadDelimitedSheet(path, format == "tsv" ? '\t' : null)],
            "xlsx" or "xlsm" => ReadXlsx(path),
            "xls" => ReadXls(path),
            _ => throw new ConversionException(T($"Tabulku formátu {format} lze převést jen s LibreOffice.", $"{format} spreadsheets need LibreOffice.")),
        };
    }

    private static List<Sheet> ReadXlsx(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var sheets = new List<Sheet>();
        foreach (var ws in workbook.Worksheets)
        {
            var rows = new List<object?[]>();
            var used = ws.RangeUsed();
            if (used is not null)
            {
                // From A1, like Excel shows it: leading empty rows and columns are kept.
                var lastCol = used.LastColumn().ColumnNumber();
                for (var r = 1; r <= used.LastRow().RowNumber(); r++)
                {
                    var row = new object?[lastCol];
                    for (var c = 1; c <= lastCol; c++)
                        row[c - 1] = Typed(ws.Cell(r, c));
                    rows.Add(row);
                }
            }
            sheets.Add(new Sheet(ws.Name, rows));
        }
        return sheets;
    }

    private static object? Typed(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsBlank)
            return null;
        if (value.IsNumber)
            return value.GetNumber();
        if (value.IsDateTime)
            return value.GetDateTime();
        if (value.IsTimeSpan)
            return value.GetTimeSpan();
        if (value.IsBoolean)
            return value.GetBoolean();
        if (value.IsError)
            return cell.GetFormattedString();
        return value.GetText();
    }

    private static List<Sheet> ReadXls(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sheets = new List<Sheet>();
        do
        {
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (var c = 0; c < row.Length; c++)
                {
                    row[c] = reader.GetValue(c) switch
                    {
                        null => null,
                        DBNull => null,
                        int i => (double)i,
                        double d => d,
                        DateTime dt => dt,
                        TimeSpan ts => ts,
                        bool b => b,
                        var other => Convert.ToString(other, CultureInfo.InvariantCulture),
                    };
                }
                rows.Add(row);
            }
            sheets.Add(new Sheet(reader.Name, rows));
        }
        while (reader.NextResult());
        return sheets;
    }

    // ---------------------------------------------------------------- CSV / TSV in

    private static Sheet ReadDelimitedSheet(string path, char? delimiter)
    {
        var text = File.ReadAllText(path, DetectEncoding(path));
        var d = delimiter ?? GuessDelimiter(text);
        // Semicolon CSV is what Czech (and most European) Excel writes: decimal comma, spaces between thousands.
        var culture = d == ';' ? Czech : CultureInfo.InvariantCulture;
        var rows = ParseDelimited(text, d).Select(r => r.Select(v => Interpret(v, culture)).ToArray()).ToList();
        return new Sheet(Formats.BaseNameOf(path), rows);
    }

    /// <summary>RFC 4180 reader; guesses ',', ';', tab or '|' when <paramref name="delimiter"/> is null.</summary>
    public static List<string[]> ReadDelimited(string path, char? delimiter = null)
    {
        var text = File.ReadAllText(path, DetectEncoding(path));
        return ParseDelimited(text, delimiter ?? GuessDelimiter(text));
    }

    /// <summary>UTF-8 when the bytes are valid UTF-8, otherwise Windows-1250 (older Czech exports).</summary>
    public static Encoding DetectEncoding(string path) => DetectEncoding(File.ReadAllBytes(path));

    public static Encoding DetectEncoding(byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250);
        }
    }

    /// <summary>
    /// The separator that splits the first lines into the same number of columns (quoted text ignored);
    /// a comma inside "1 234,50" or "Cena, Kč" must not win over the real semicolons.
    /// </summary>
    public static char GuessDelimiter(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Take(10).ToList();
        var best = ',';
        var bestScore = -1;
        foreach (var candidate in new[] { ';', ',', '\t', '|' })
        {
            var counts = lines.Select(l => CountOutsideQuotes(l, candidate)).ToList();
            if (counts.Count == 0 || counts[0] == 0)
                continue;
            var consistent = counts.All(c => c == counts[0]);
            var score = (consistent ? 1000 : 0) + counts[0];
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    private static int CountOutsideQuotes(string line, char c)
    {
        var count = 0;
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"')
                quoted = !quoted;
            else if (ch == c && !quoted)
                count++;
        }
        return count;
    }

    [GeneratedRegex(@"^-?\d{1,3}(?:[   ]\d{3})+(?:,\d+)?$|^-?\d+(?:,\d+)?$")]
    private static partial Regex CzechNumberRegex();

    [GeneratedRegex(@"^-?\d+(?:\.\d+)?$")]
    private static partial Regex PlainNumberRegex();

    [GeneratedRegex(@"^(-?[\d.,  ]+?)\s?%$")]
    private static partial Regex PercentRegex();

    private static readonly string[] DateFormats =
    [
        "d.M.yyyy", "d. M. yyyy", "d.M.yyyy H:mm", "d.M.yyyy H:mm:ss", "d. M. yyyy H:mm", "d. M. yyyy H:mm:ss",
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss",
    ];

    /// <summary>
    /// What a CSV text cell really is. Only plain decimal numbers count (no exponents: "1E5" is a product
    /// code, not 100000); leading zeros and long digit strings stay text (ZIP codes, account numbers).
    /// </summary>
    public static object? Interpret(string? raw, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        var value = raw.Trim();
        if (value.Length == 0)
            return raw;
        var digits = value.Count(char.IsDigit);
        if ((value.Length > 1 && value[0] == '0' && char.IsDigit(value[1])) || digits > 15)
            return raw;

        if (PercentRegex().Match(value) is { Success: true } percent && Interpret(percent.Groups[1].Value, culture) is double p)
            return new Percent(p / 100);

        var comma = ReferenceEquals(culture, Czech);
        if (comma ? CzechNumberRegex().IsMatch(value) : PlainNumberRegex().IsMatch(value))
        {
            var normalized = comma ? value.Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(',', '.') : value;
            if (double.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number))
                return number;
        }
        if (value.Length >= 8 && DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        return raw;
    }

    /// <summary>A number shown as a percentage (written with a % format to XLSX).</summary>
    public sealed record Percent(double Value);

    public static List<string[]> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿')
            i = 1;
        for (; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
            }
            else
            {
                field.Append(ch);
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }

    // ---------------------------------------------------------------- CSV / TSV out

    public static string ToDelimited(IEnumerable<string[]> rows, char delimiter)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                if (c > 0)
                    sb.Append(delimiter);
                var v = row[c] ?? "";
                if (delimiter == '\t')
                    v = v.Replace('\t', ' ');
                if (v.IndexOfAny([delimiter, '"', '\n', '\r']) >= 0)
                    sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
                else
                    sb.Append(v);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// The CSV dialect Excel expects on this PC: Czech (and most of Europe) uses ';' with a decimal comma,
    /// English ',' with a decimal point.
    /// </summary>
    public static (char Delimiter, CultureInfo Culture) CsvDialect()
    {
        var culture = CultureInfo.CurrentCulture;
        return culture.TextInfo.ListSeparator == ";" || culture.NumberFormat.NumberDecimalSeparator == ","
            ? (';', culture)
            : (',', CultureInfo.InvariantCulture);
    }

    /// <summary>A typed cell as CSV text in <paramref name="culture"/> (no thousands separators, dates as the culture writes them).</summary>
    public static string CellText(object? value, CultureInfo culture) => value switch
    {
        null => "",
        string s => s,
        double d => d.ToString("0.###############", culture),
        Percent p => p.Value.ToString("0.###############", culture),
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString(ReferenceEquals(culture, CultureInfo.InvariantCulture) ? "yyyy-MM-dd" : culture.DateTimeFormat.ShortDatePattern, culture)
            : dt.ToString(ReferenceEquals(culture, CultureInfo.InvariantCulture) ? "yyyy-MM-dd HH:mm:ss" : culture.DateTimeFormat.ShortDatePattern + " H:mm:ss", culture),
        TimeSpan ts => ts.ToString(@"h\:mm\:ss", culture),
        bool b => b ? "TRUE" : "FALSE",
        _ => Convert.ToString(value, culture) ?? "",
    };

    // ---------------------------------------------------------------- XLSX out

    public static void WriteXlsx(IReadOnlyList<Sheet> sheets, string output)
    {
        using var workbook = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            var name = SheetName(sheet.Name, used);
            var ws = workbook.Worksheets.Add(name);
            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                for (var c = 0; c < row.Length; c++)
                    SetCell(ws.Cell(r + 1, c + 1), row[c]);
            }
            if (sheet.Rows.Count > 0)
            {
                if (LooksLikeHeader(sheet.Rows))
                    ws.Row(1).Style.Font.Bold = true;
                ws.Columns().AdjustToContents(1, Math.Min(sheet.Rows.Count, 200), 8, 60);
            }
        }
        if (sheets.Count == 0)
            workbook.Worksheets.Add(T("List1", "Sheet1"));
        workbook.SaveAs(output);
    }

    /// <summary>The first row is a header when it is all text and the rows below hold numbers or dates.</summary>
    private static bool LooksLikeHeader(List<object?[]> rows) =>
        rows.Count > 1 && rows[0].All(v => v is null or string) && rows[0].Any(v => v is string)
        && rows.Skip(1).Take(20).Any(r => r.Any(v => v is double or DateTime or Percent or TimeSpan));

    private static string SheetName(string raw, HashSet<string> used)
    {
        var clean = new string(raw.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (clean.Length == 0)
            clean = T("List", "Sheet");
        if (clean.Length > 31)
            clean = clean[..31];
        var name = clean;
        for (var i = 2; !used.Add(name); i++)
            name = clean.Length > 27 ? clean[..27] + " " + i : clean + " " + i;
        return name;
    }

    private static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case double d:
                cell.Value = d;
                break;
            case Percent p:
                cell.Value = p.Value;
                cell.Style.NumberFormat.Format = Math.Abs(p.Value * 100 % 1) > 0.0001 ? "0.0%" : "0%";
                break;
            case DateTime dt:
                cell.Value = dt;
                // Built-in formats follow the language of whoever opens the file.
                cell.Style.NumberFormat.NumberFormatId = dt.TimeOfDay == TimeSpan.Zero ? 14 : 22;
                break;
            case TimeSpan ts:
                cell.Value = ts;
                cell.Style.NumberFormat.Format = "[h]:mm:ss";
                break;
            case bool b:
                cell.Value = b;
                break;
            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture);
                break;
        }
    }

    // ---------------------------------------------------------------- conversions

    public static async Task<IEnumerable<string>> ConvertAsync(FileItem item, string format, OperationContext ctx)
    {
        var dir = ctx.OutputDirFor(item.Path);
        if (format == "xlsx")
        {
            // XLSM → XLSX: the same workbook without macros (formulas, formats, charts and all sheets stay).
            if (item.Format == "xlsm")
            {
                return [await OutputPaths.ProduceAsync(dir, item.BaseName, "xlsx", tmp => Task.Run(() =>
                {
                    using var workbook = new XLWorkbook(item.Path);
                    workbook.SaveAs(tmp);
                }, ctx.CancellationToken)).ConfigureAwait(false)];
            }
            // XLS → XLSX keeps formulas only through LibreOffice; the built-in route keeps values and types.
            if (item.Format == "xls" && ctx.Tools.Has(ExternalTool.LibreOffice))
                return [await OfficeBackend.ConvertAsync(item, "xlsx", ctx).ConfigureAwait(false)];
            var sheets = await Task.Run(() => Read(item.Path), ctx.CancellationToken).ConfigureAwait(false);
            return [await OutputPaths.ProduceAsync(dir, item.BaseName, "xlsx", tmp => Task.Run(() => WriteXlsx(sheets, tmp))).ConfigureAwait(false)];
        }

        var all = await Task.Run(() => Read(item.Path), ctx.CancellationToken).ConfigureAwait(false);
        return await WriteDelimitedAsync(all, item.BaseName, dir, format, ctx.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>One CSV/TSV per non-empty sheet, in the dialect Excel expects here, with a BOM for the diacritics.</summary>
    public static async Task<IEnumerable<string>> WriteDelimitedAsync(IReadOnlyList<Sheet> sheets, string baseName, string dir, string format, CancellationToken ct)
    {
        var (csvDelimiter, culture) = CsvDialect();
        var delimiter = format == "tsv" ? '\t' : csvDelimiter;
        var nonEmpty = sheets.Where(s => s.Rows.Count > 0).ToList();
        if (nonEmpty.Count == 0)
            nonEmpty = sheets.Take(1).ToList();
        var outputs = new List<string>();
        foreach (var sheet in nonEmpty)
        {
            var name = nonEmpty.Count == 1 ? baseName : $"{baseName} - {sheet.Name}";
            var rows = sheet.Rows.Select(r => r.Select(v => CellText(v, culture)).ToArray());
            outputs.Add(await OutputPaths.ProduceAsync(dir, name, format, tmp =>
                File.WriteAllTextAsync(tmp, ToDelimited(rows, delimiter), new UTF8Encoding(true), ct)).ConfigureAwait(false));
        }
        return outputs;
    }
}
