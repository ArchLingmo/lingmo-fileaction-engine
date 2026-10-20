using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;

namespace FileAction.Core.Backends;

/// <summary>
/// Table detection. A PDF keeps no table structure, only words at positions, and the block segmenter reads a
/// table column by column ("Jméno Řehoř Ůna", "Částka 1 234,50 Kč 99,90 Kč"). So tables are found first, on
/// the raw words: lines whose words fall apart into cells (gaps too wide for a space, or a ruling line in the
/// gap) and whose cells line up into the same columns over several lines. Those words become a pipe table
/// and are taken out before the normal block processing.
/// </summary>
public static partial class PdfStructure
{
    /// <summary>A lone list label ("1.", "a)", "(3)"): a column of these is a numbered list, not a table.</summary>
    [GeneratedRegex(@"^(?:(?:\d{1,3}|[a-zA-Z])[.)]|\(\d{1,3}\))$")]
    private static partial Regex ListLabelRegex();

    /// <summary>A table found on a page: its Markdown, where it sits (for reading order) and the words it took.</summary>
    private sealed record PageTable(double Top, double Bottom, double Left, double Right, string Markdown, IReadOnlyCollection<Word> Words);

    /// <summary>A ruling line: horizontal ones run From–To in x at height At, vertical ones From–To in y at x = At.</summary>
    private readonly record struct Rule(double From, double To, double At);

    /// <summary>Neighbouring words of one line that belong together (one table cell, or a run of text).</summary>
    private sealed class Segment(List<Word> words)
    {
        public List<Word> Words { get; } = words;
        public double Left { get; } = words.Min(w => w.BoundingBox.Left);
        public double Right { get; } = words.Max(w => w.BoundingBox.Right);
        public double Center => (Left + Right) / 2;
        public double Width => Right - Left;
    }

    /// <summary>Words sharing a baseline, split into segments.</summary>
    private sealed class Line(double baseline, double size, List<Segment> segments)
    {
        public double Baseline { get; } = baseline;
        public double Size { get; } = size;
        public List<Segment> Segments { get; } = segments;

        /// <summary>A line with two or more cells can start a table row; a single segment can only continue a cell.</summary>
        public bool IsRow => Segments.Count >= 2;
    }

    /// <summary>
    /// Finds the tables on a page. Conservative on purpose: a missed table still reads as text, while a
    /// paragraph or a list wrongly turned into a table is mangled.
    /// </summary>
    private static List<PageTable> FindTables(Page page, IReadOnlyList<Word> words)
    {
        var tables = new List<PageTable>();
        if (words.Count < 4)
            return tables;
        var (horizontal, vertical) = Rulings(page);
        var region = new List<Line>();
        foreach (var line in Lines(words, vertical))
        {
            if (region.Count > 0 && !Fits(region, line, horizontal))
            {
                if (BuildTable(region, horizontal) is { } table)
                    tables.Add(table);
                region.Clear();
            }
            // A table starts with a line that has cells; lines without them only join a started table.
            if (region.Count > 0 || line.IsRow)
                region.Add(line);
        }
        if (region.Count > 0 && BuildTable(region, horizontal) is { } last)
            tables.Add(last);
        return tables;
    }

    private static double Baseline(Word word) => word.Letters[0].StartBaseLine.Y;

    private static double Size(Word word)
    {
        var size = word.Letters.Average(l => l.PointSize);
        // Some PDFs set a 1 pt font and scale it by the text matrix; the glyph height is then the better guess.
        return size >= 2 ? size : Math.Max(word.BoundingBox.Height, 2);
    }

    /// <summary>
    /// Groups the words into lines by baseline (top to bottom) and splits every line where the gap is wider
    /// than a space can be, or where a vertical ruling line runs through the gap.
    /// </summary>
    private static List<Line> Lines(IReadOnlyList<Word> words, List<Rule> vertical)
    {
        var groups = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(Baseline))
        {
            if (groups.Count > 0 && Math.Abs(Baseline(groups[^1][0]) - Baseline(word)) <= 0.3 * Size(word))
                groups[^1].Add(word);
            else
                groups.Add([word]);
        }
        var lines = new List<Line>();
        foreach (var group in groups)
        {
            group.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            var baseline = Baseline(group[0]);
            var segments = new List<Segment>();
            var current = new List<Word> { group[0] };
            for (var i = 1; i < group.Count; i++)
            {
                var previous = group[i - 1];
                var word = group[i];
                var gap = word.BoundingBox.Left - previous.BoundingBox.Right;
                // A justified space stays well under 0.8 em; table cells are padded on both sides.
                var wide = gap > 0.8 * Math.Max(Size(previous), Size(word));
                var ruled = vertical.Any(r => r.At > previous.BoundingBox.Right - 1 && r.At < word.BoundingBox.Left + 1 && r.From <= baseline && r.To >= baseline);
                if (wide || ruled)
                {
                    segments.Add(new Segment(current));
                    current = [];
                }
                current.Add(word);
            }
            segments.Add(new Segment(current));
            lines.Add(new Line(baseline, group.Average(Size), segments));
        }
        return lines;
    }

    /// <summary>Columns: the x ranges covered by cells, merged where they overlap (left to right).</summary>
    private static List<(double Left, double Right)> Columns(IEnumerable<Segment> segments)
    {
        var columns = new List<(double Left, double Right)>();
        foreach (var segment in segments.OrderBy(s => s.Left))
        {
            if (columns.Count > 0 && segment.Left < columns[^1].Right)
                columns[^1] = (columns[^1].Left, Math.Max(columns[^1].Right, segment.Right));
            else
                columns.Add((segment.Left, segment.Right));
        }
        return columns;
    }

    private static bool Overlaps(Segment segment, (double Left, double Right) column) =>
        segment.Left < column.Right && segment.Right > column.Left;

    /// <summary>A horizontal rule between two baselines that spans at least half of the table width.</summary>
    private static bool RuleBetween(List<Rule> horizontal, double upper, double lower, double left, double right) =>
        horizontal.Any(r => r.At < upper && r.At > lower && Math.Min(r.To, right) - Math.Max(r.From, left) >= 0.5 * (right - left));

    /// <summary>Whether the next line continues the table collected so far.</summary>
    private static bool Fits(List<Line> region, Line line, List<Rule> horizontal)
    {
        var previous = region[^1];
        var distance = previous.Baseline - line.Baseline;
        var size = Math.Max(previous.Size, line.Size);
        var rows = region.Where(l => l.IsRow).ToList();
        var columns = Columns(rows.SelectMany(l => l.Segments));
        var ruled = RuleBetween(horizontal, previous.Baseline, line.Baseline, columns[0].Left, columns[^1].Right);
        // Rows may be padded apart (more so between ruled cells); lines of one cell sit at line spacing.
        if (distance > (ruled ? 4 : 3) * size)
            return false;

        if (line.IsRow)
        {
            var hits = line.Segments.Select(s => columns.Count(c => Overlaps(s, c))).ToList();
            // A cell spanning two columns means the lines do not share the grid (e.g. a justified paragraph).
            if (hits.Any(n => n > 1) || hits.Count(n => n == 1) < 2)
                return false;
            // Once two rows have set the columns, a cell outside all of them ends the table (e.g. a list after it).
            return rows.Count < 2 || hits.All(n => n == 1);
        }

        // A single segment is a wrapped line of a cell: close below, inside one column, aligned with that column.
        var segment = line.Segments[0];
        if (!ruled && distance > 1.45 * size)
            return false;
        var column = columns.Where(c => Overlaps(segment, c)).ToList();
        if (column.Count != 1)
            return false;
        return region.SelectMany(l => l.Segments).Where(s => Overlaps(s, column[0])).Any(s =>
            Math.Abs(s.Left - segment.Left) <= 2 || Math.Abs(s.Right - segment.Right) <= 2 || Math.Abs(s.Center - segment.Center) <= 2);
    }

    /// <summary>Checks that the collected lines really are a table and renders it as a pipe table, or returns null.</summary>
    private static PageTable? BuildTable(List<Line> region, List<Rule> horizontal)
    {
        var anchors = region.Where(l => l.IsRow).ToList();
        if (anchors.Count < 2)
            return null;
        var segments = region.SelectMany(l => l.Segments).ToList();
        var columns = Columns(segments);
        if (columns.Count < 2)
            return null;
        int ColumnOf(Segment s) => columns.FindIndex(c => Overlaps(s, c));
        var left = columns[0].Left;
        var right = columns[^1].Right;

        // Ruled tables: a rule between every two rows separates them, even with a single filled cell in a row.
        var ruled = anchors.Zip(anchors.Skip(1)).All(p => RuleBetween(horizontal, p.First.Baseline, p.Second.Baseline, left, right));
        // Without rules, two lines in two columns are too weak a hint (a letterhead with an address and a date).
        if (!ruled && anchors.Count < 3 && columns.Count < 3)
            return null;

        var rows = new List<List<Line>>();
        for (var i = 0; i < region.Count; i++)
        {
            var line = region[i];
            var newRow = i == 0 || (ruled ? RuleBetween(horizontal, region[i - 1].Baseline, line.Baseline, left, right) : line.IsRow);
            if (newRow)
                rows.Add([line]);
            else
                rows[^1].Add(line);
        }

        // Every column is used by at least two rows; otherwise the grid is an accident.
        for (var c = 0; c < columns.Count; c++)
        {
            if (rows.Count(r => r.Any(l => l.Segments.Any(s => ColumnOf(s) == c))) < 2)
                return null;
        }

        // A first column of bullets or numbers is a list whose markers sit apart from the text.
        var first = segments.Where(s => ColumnOf(s) == 0).Select(s => FixSymbols(string.Join(" ", s.Words.Select(w => w.Text))).Trim()).ToList();
        if (first.All(t => IsBulletOnly(t) || ListLabelRegex().IsMatch(t)))
            return null;

        // Text set in columns: every column holds lines of several words that fill its width.
        var texty = Enumerable.Range(0, columns.Count).All(c =>
        {
            var inColumn = segments.Where(s => ColumnOf(s) == c).ToList();
            var width = columns[c].Right - columns[c].Left;
            return inColumn.Average(s => s.Words.Count) >= 4 && inColumn.Count(s => s.Width >= 0.75 * width) * 2 >= inColumn.Count;
        });
        if (texty)
            return null;

        var cells = rows.Select(row => Enumerable.Range(0, columns.Count).Select(c =>
            JoinLines(row.SelectMany(l => l.Segments.Where(s => ColumnOf(s) == c)).Select(s => RenderWords(s.Words).Markdown))).ToList()).ToList();
        var words = segments.SelectMany(s => s.Words).ToList();
        return new PageTable(words.Max(w => w.BoundingBox.Top), words.Min(w => w.BoundingBox.Bottom), left, right, PipeTable(cells, columns, rows, ColumnOf), words);
    }

    /// <summary>
    /// The rows as a pipe table; the first row is the header (pipe tables need one). Column alignment comes
    /// from the data cells' edges (numbers are often right-aligned), and the dashes keep the column widths'
    /// proportions, which pandoc uses when the table is wide enough to wrap.
    /// </summary>
    private static string PipeTable(List<List<string>> cells, List<(double Left, double Right)> columns, List<List<Line>> rows, Func<Segment, int> columnOf)
    {
        var total = columns.Sum(c => c.Right - c.Left);
        var separators = new List<string>();
        for (var c = 0; c < columns.Count; c++)
        {
            var dashes = new string('-', Math.Max(3, (int)Math.Round(60 * (columns[c].Right - columns[c].Left) / total)));
            var data = rows.Skip(1).Select(r => r.SelectMany(l => l.Segments).FirstOrDefault(s => columnOf(s) == c)).OfType<Segment>().ToList();
            var align = "";
            if (data.Count >= 2)
            {
                double Spread(Func<Segment, double> edge) => data.Max(edge) - data.Min(edge);
                var leftAligned = Spread(s => s.Left) <= 1.5;
                if (!leftAligned && Spread(s => s.Right) <= 1.5)
                    align = "right";
                else if (!leftAligned && Spread(s => s.Center) <= 1.5)
                    align = "center";
            }
            separators.Add(align switch
            {
                "right" => dashes[1..] + ":",
                "center" => ":" + dashes[2..] + ":",
                _ => dashes,
            });
        }
        var sb = new StringBuilder();
        sb.Append(Row(cells[0])).Append('\n').Append("|").Append(string.Join("|", separators)).Append("|\n");
        foreach (var row in cells.Skip(1))
            sb.Append(Row(row)).Append('\n');
        return sb.ToString().TrimEnd();

        static string Row(List<string> row) => "| " + string.Join(" | ", row) + " |";
    }

    /// <summary>
    /// Ruling lines (table borders) from the page's vector paths: thin strokes or filled hairline rectangles,
    /// and the edges of stroked rectangles. They help split cells that sit close together and tell rows apart.
    /// </summary>
    private static (List<Rule> Horizontal, List<Rule> Vertical) Rulings(Page page)
    {
        var horizontal = new List<Rule>();
        var vertical = new List<Rule>();
        try
        {
            foreach (var path in page.ExperimentalAccess.Paths)
            {
                if (path.IsClipping || !(path.IsStroked || path.IsFilled))
                    continue;
                foreach (var subpath in path)
                {
                    if (subpath.GetBoundingRectangle() is not { } box)
                        continue;
                    var (x1, x2) = (Math.Min(box.Left, box.Right), Math.Max(box.Left, box.Right));
                    var (y1, y2) = (Math.Min(box.Bottom, box.Top), Math.Max(box.Bottom, box.Top));
                    if (y2 - y1 <= 2.5 && x2 - x1 >= 5)
                    {
                        horizontal.Add(new Rule(x1, x2, (y1 + y2) / 2));
                    }
                    else if (x2 - x1 <= 2.5 && y2 - y1 >= 5)
                    {
                        vertical.Add(new Rule(y1, y2, (x1 + x2) / 2));
                    }
                    else if (path.IsStroked && subpath.GetDrawnRectangle() is not null)
                    {
                        horizontal.Add(new Rule(x1, x2, y1));
                        horizontal.Add(new Rule(x1, x2, y2));
                        vertical.Add(new Rule(y1, y2, x1));
                        vertical.Add(new Rule(y1, y2, x2));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Paths are an experimental PdfPig API; without rulings the tables are still found by alignment.
        }
        return (horizontal, vertical);
    }
}
