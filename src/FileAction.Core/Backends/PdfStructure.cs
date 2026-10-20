using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace FileAction.Core.Backends;

/// <summary>
/// Rebuilds the structure of a text PDF as Markdown for pandoc: headings (from font size), bold and
/// italic (from the font), bulleted and numbered lists, paragraphs in reading order (columns too),
/// pictures, page breaks and tables (found by word alignment, see PdfStructure.Tables.cs).
/// </summary>
public static partial class PdfStructure
{
    // Symbol bullets may touch the text; dashes and asterisks need a space ("-5 %" is not a list item).
    [GeneratedRegex(@"^(?:[•◦▪▫●○■□►▸‣⁃·∙❖✓→]\s*|[–—\-\*]\s+)")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^(\d{1,3}|[a-zA-Z])[.)]\s+")]
    private static partial Regex NumberRegex();

    /// <summary>Raw Word paragraph with a page break (pandoc passes it through into DOCX only).</summary>
    private const string DocxPageBreak = "```{=openxml}\n<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>\n```";

    /// <summary>
    /// Bullets set in the Symbol or Wingdings font arrive as private-use characters (U+F0B7 and friends);
    /// they are shown as real bullets.
    /// </summary>
    public static string FixSymbols(string text) => text
        .Replace('\uF0B7', '•').Replace('\uF0A7', '▪').Replace('\uF0D8', '►').Replace('\uF076', '❖')
        .Replace('\uF0FC', '✓').Replace('\uF06E', '■').Replace('\uF0A8', '□').Replace('\uF0E0', '→');

    /// <param name="imageDir">Where pictures are saved (referenced by file name); null leaves them out.</param>
    /// <param name="pageBreaks">Real page breaks between pages (DOCX).</param>
    public static string ToMarkdown(string path, string? imageDir, bool pageBreaks)
    {
        using var document = PdfDocument.Open(path);
        var pages = document.GetPages().ToList();
        var body = BodySize(pages);
        var sb = new StringBuilder();
        var imageCount = 0;
        foreach (var page in pages)
        {
            var parts = PageParts(page, body, imageDir, ref imageCount);
            if (parts.Count == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(pageBreaks ? "\n\n" + DocxPageBreak + "\n\n" : "\n\n");
            sb.Append(string.Join("\n\n", parts));
        }
        if (sb.Length == 0)
            throw new ConversionException(Loc.T("PDF neobsahuje žádný text (nejspíš je to sken). Zkuste převod na JPG/PNG.", "The PDF contains no text (probably a scan). Try converting it to JPG/PNG."));
        return sb.ToString().Trim() + "\n";
    }

    /// <summary>The most common letter size, i.e. the size of the running text.</summary>
    private static double BodySize(IEnumerable<Page> pages)
    {
        var counts = new Dictionary<double, int>();
        foreach (var page in pages)
        {
            foreach (var letter in page.Letters)
            {
                if (string.IsNullOrWhiteSpace(letter.Value))
                    continue;
                var size = Math.Round(letter.PointSize * 2) / 2;
                counts[size] = counts.GetValueOrDefault(size) + 1;
            }
        }
        return counts.Count == 0 ? 11 : counts.MaxBy(kv => kv.Value).Key;
    }

    private static List<string> PageParts(Page page, double body, string? imageDir, ref int imageCount)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        // Tables first: the block segmenter would read them column by column. Their words leave the page text.
        var tables = FindTables(page, words);
        if (tables.Count > 0)
        {
            var used = tables.SelectMany(t => t.Words).ToHashSet();
            words = words.Where(w => !used.Contains(w)).ToList();
        }
        var blocks = words.Count == 0 ? [] : UnsupervisedReadingOrderDetector.Instance.Get(DocstrumBoundingBoxes.Instance.GetBlocks(words))
            .OrderBy(b => b.ReadingOrder).ToList();
        foreach (var table in tables)
            SplitAround(blocks, table);
        // Bullets and list numbers ("1.", "a)") often sit apart from their text and end up as a block of their
        // own; remember where they are and give them back to the lines on the same baseline.
        var bullets = new List<(double Y, double X, string Marker)>();
        foreach (var block in blocks.Where(b => b.TextLines.SelectMany(l => l.Words).All(w => IsBulletOnly(w.Text) || ListLabelRegex().IsMatch(w.Text.Trim()))).ToList())
        {
            bullets.AddRange(block.TextLines.SelectMany(l => l.Words).Select(w => (w.BoundingBox.Bottom, w.BoundingBox.Right,
                IsBulletOnly(w.Text) ? "•" : w.Text.Trim())));
            blocks.Remove(block);
        }
        var parts = blocks.Select(b => (Top: b.BoundingBox.Top, b.BoundingBox.Left, b.BoundingBox.Right, Text: RenderBlock(b, body, bullets)))
            .Where(p => p.Text.Length > 0).ToList();
        foreach (var table in tables)
        {
            // Before the first block (in reading order) below the table, preferably one in the same column.
            var index = parts.FindIndex(p => p.Top < table.Top && p.Right > table.Left && p.Left < table.Right);
            if (index < 0)
                index = parts.FindIndex(p => p.Top < table.Top);
            parts.Insert(index < 0 ? parts.Count : index, (table.Top, table.Left, table.Right, table.Markdown));
        }

        if (imageDir is not null)
        {
            foreach (var image in page.GetImages())
            {
                // Logos and rules are often tiny images; leave out what is too small to matter.
                if (image.BoundingBox.Width < 40 || image.BoundingBox.Height < 40 || SaveImage(image, imageDir, ++imageCount) is not { } file)
                    continue;
                var markdown = $"![](<{file}>)";
                // Before the first block (in reading order) that starts below the picture's middle.
                var middle = image.BoundingBox.Bottom + image.BoundingBox.Height / 2;
                var index = parts.FindIndex(p => p.Top < middle);
                parts.Insert(index < 0 ? parts.Count : index, (image.BoundingBox.Top, image.BoundingBox.Left, image.BoundingBox.Right, markdown));
            }
        }
        return JoinContinuations(parts.Select(p => p.Text).ToList());
    }

    /// <summary>
    /// With the table's words gone, the paragraphs above and below it may close up into one block; such a
    /// block is cut in two so the table can go between them.
    /// </summary>
    private static void SplitAround(List<TextBlock> blocks, PageTable table)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            var box = blocks[i].BoundingBox;
            if (box.Top <= table.Top || box.Bottom >= table.Bottom || box.Right <= table.Left || box.Left >= table.Right)
                continue;
            var above = blocks[i].TextLines.Where(l => l.BoundingBox.Bottom >= table.Bottom).ToList();
            var below = blocks[i].TextLines.Where(l => l.BoundingBox.Bottom < table.Bottom).ToList();
            if (above.Count == 0 || below.Count == 0)
                continue;
            blocks[i] = new TextBlock(above);
            blocks.Insert(i + 1, new TextBlock(below));
            i++;
        }
    }

    private static string? SaveImage(IPdfImage image, string dir, int number)
    {
        try
        {
            if (image.TryGetPng(out var png))
            {
                var name = $"image-{number}.png";
                File.WriteAllBytes(Path.Combine(dir, name), png);
                return name;
            }
            var raw = image.RawBytes.ToArray();
            if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8)
            {
                var name = $"image-{number}.jpg";
                File.WriteAllBytes(Path.Combine(dir, name), raw);
                return name;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An image PdfPig cannot decode is skipped; the text still converts.
        }
        return null;
    }

    /// <summary>
    /// A paragraph that runs on into the next column ends one block without finishing its sentence, and the
    /// next block starts in lower case: they are one paragraph again.
    /// </summary>
    private static List<string> JoinContinuations(List<string> parts)
    {
        var joined = new List<string>();
        foreach (var part in parts)
        {
            if (joined.Count > 0 && IsPlainParagraph(joined[^1]) && IsPlainParagraph(part)
                && !".!?:…\"“”»".Contains(joined[^1].TrimEnd()[^1]) && char.IsLower(part.TrimStart('*', '_')[0]))
            {
                var previous = joined[^1].TrimEnd();
                joined[^1] = previous.EndsWith('-') && previous.Length > 1 && char.IsLetter(previous[^2])
                    ? previous[..^1] + part.TrimStart()
                    : previous + " " + part.TrimStart();
                continue;
            }
            joined.Add(part);
        }
        return joined;
    }

    /// <summary>Running text, not a heading, list, table, picture or raw block.</summary>
    private static bool IsPlainParagraph(string markdown)
    {
        var t = markdown.TrimStart();
        return t.Length > 0 && !t.Contains('\n') && t[0] is not ('#' or '-' or '|' or '!' or '`' or '•') && !NumberRegex().IsMatch(t);
    }

    private static bool IsBulletOnly(string text)
    {
        var t = FixSymbols(text).Trim();
        return t.Length == 1 && BulletRegex().IsMatch(t + " ");
    }

    private static string RenderBlock(TextBlock block, double body, List<(double Y, double X, string Marker)> bullets)
    {
        var lines = block.TextLines.Where(l => l.Words.Count > 0).ToList();
        if (lines.Count == 0)
            return "";
        var letters = lines.SelectMany(l => l.Words).SelectMany(w => w.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        var size = letters.Count == 0 ? body : letters.Average(l => l.PointSize);
        var plain = FixSymbols(string.Join(" ", lines.Select(l => string.Join(" ", l.Words.Select(w => w.Text))))).Trim();

        // Headings: noticeably bigger than the running text, or a short line set in bold only.
        var ratio = size / body;
        var allBold = letters.Count > 0 && letters.All(IsBold);
        if (lines.Count <= 3 && plain.Length <= 200)
        {
            var level = ratio >= 1.6 ? 1 : ratio >= 1.3 ? 2 : ratio >= 1.12 ? 3 : 0;
            if (level == 0 && allBold && lines.Count == 1 && plain.Length <= 90 && !plain.EndsWith('.') && !plain.EndsWith(':') && ratio >= 0.95)
                level = 3;
            if (level > 0)
                return new string('#', level) + " " + Escape(plain);
        }

        var rendered = lines.Select(line =>
        {
            var r = RenderLine(line);
            var marker = bullets.FirstOrDefault(b => Math.Abs(b.Y - line.BoundingBox.Bottom) < 3 && b.X <= line.BoundingBox.Left + 1).Marker;
            return marker is not null && !BulletRegex().IsMatch(r.Plain) && !NumberRegex().IsMatch(r.Plain)
                ? (Markdown: marker + " " + r.Markdown, Plain: marker + " " + r.Plain)
                : r;
        }).ToList();
        if (rendered.Any(l => BulletRegex().IsMatch(l.Plain) || NumberRegex().IsMatch(l.Plain)))
            return RenderList(rendered);
        return JoinLines(rendered.Select(l => l.Markdown));
    }

    private static (string Markdown, string Plain) RenderLine(TextLine line) => RenderWords(line.Words);

    /// <summary>Words of a line (or of a table cell) as Markdown with bold/italic spans, plus their plain text for list detection.</summary>
    private static (string Markdown, string Plain) RenderWords(IReadOnlyList<Word> words)
    {
        var sb = new StringBuilder();
        var plain = FixSymbols(string.Join(" ", words.Select(w => w.Text)));
        // Group neighbouring words with the same style so markers wrap whole phrases.
        var runs = new List<(string Text, bool Bold, bool Italic)>();
        foreach (var word in words)
        {
            var visible = word.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
            var bold = visible.Count > 0 && visible.Count(IsBold) * 2 > visible.Count;
            var italic = visible.Count > 0 && visible.Count(IsItalic) * 2 > visible.Count;
            var text = FixSymbols(word.Text);
            if (runs.Count > 0 && runs[^1].Bold == bold && runs[^1].Italic == italic)
                runs[^1] = (runs[^1].Text + " " + text, bold, italic);
            else
                runs.Add((text, bold, italic));
        }
        foreach (var (text, bold, italic) in runs)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            var marker = bold && italic ? "***" : bold ? "**" : italic ? "*" : "";
            sb.Append(marker).Append(Escape(text)).Append(marker);
        }
        return (sb.ToString(), plain);
    }

    private static string RenderList(List<(string Markdown, string Plain)> lines)
    {
        var items = new List<StringBuilder>();
        foreach (var (markdown, plain) in lines)
        {
            if (BulletRegex().Match(plain) is { Success: true } bullet)
            {
                items.Add(new StringBuilder("- ").Append(StripPrefix(markdown, bullet.Length)));
            }
            else if (NumberRegex().Match(plain) is { Success: true } number)
            {
                var label = number.Groups[1].Value;
                // Pandoc only knows "1." and "a." lists; keep the original numbering otherwise.
                items.Add(new StringBuilder(int.TryParse(label, out _) ? label + ". " : label + ") ").Append(StripPrefix(markdown, number.Length)));
            }
            else if (items.Count > 0)
            {
                items[^1].Append(' ').Append(markdown);
            }
            else
            {
                items.Add(new StringBuilder(markdown));
            }
        }
        return string.Join("\n", items.Select(i => i.ToString()));
    }

    /// <summary>Removes the bullet or number from the start of a rendered line (it may sit inside a style marker).</summary>
    private static string StripPrefix(string markdown, int plainLength)
    {
        var marker = markdown.StartsWith("***", StringComparison.Ordinal) ? "***" : markdown.StartsWith("**", StringComparison.Ordinal) ? "**" : markdown.StartsWith('*') ? "*" : "";
        var rest = markdown[marker.Length..];
        // Escaping may have added backslashes in the prefix (e.g. "\-"); skip escaped characters as one.
        var skipped = 0;
        var i = 0;
        while (i < rest.Length && skipped < plainLength)
        {
            if (rest[i] == '\\' && i + 1 < rest.Length)
                i++;
            i++;
            skipped++;
        }
        return marker + rest[i..].TrimStart();
    }

    /// <summary>Joins wrapped lines into one paragraph and undoes hyphenation at line ends.</summary>
    private static string JoinLines(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (sb.Length > 0)
            {
                if (sb[^1] == '-' && sb.Length > 1 && char.IsLetter(sb[^2]) && char.IsLower(line[0]))
                    sb.Length--;
                else
                    sb.Append(' ');
            }
            sb.Append(line);
        }
        return sb.ToString();
    }

    private static bool IsBold(Letter letter) =>
        letter.FontDetails?.IsBold == true || ContainsAny(letter.FontName, "Bold", "Black", "Heavy", "Semibold", "SemiBold", "Demi");

    private static bool IsItalic(Letter letter) =>
        letter.FontDetails?.IsItalic == true || ContainsAny(letter.FontName, "Italic", "Oblique");

    private static bool ContainsAny(string? text, params string[] parts) =>
        text is not null && parts.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Escapes characters that Markdown would read as formatting.</summary>
    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>' or '#' or '|' or '$' or '~' or '^')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
