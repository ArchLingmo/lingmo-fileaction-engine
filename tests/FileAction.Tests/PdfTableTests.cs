using System.Text;
using FileAction.Core;
using FileAction.Core.Backends;
using Xunit.Abstractions;

namespace FileAction.Tests;

/// <summary>
/// Table detection in PDF → Markdown: realistic PDFs are made the way users get them (Markdown → DOCX by
/// pandoc → PDF by LibreOffice, or HTML → PDF by LibreOffice) and the rebuilt Markdown must hold pipe tables
/// where the PDF had tables, and only there. Skipped when pandoc or LibreOffice is missing.
/// </summary>
[Trait("Category", "Integration")]
public class PdfTableTests : IClassFixture<PdfTableTests.Samples>
{
    public sealed class Samples : IDisposable
    {
        public string Dir { get; } = OutputPaths.TempDirectory();
        public ToolLocator Tools { get; } = new();
        public Dictionary<string, string> Pdfs { get; } = new();

        private const string OpenXmlColumns = "```{=openxml}\n<w:p><w:pPr><w:sectPr><w:type w:val=\"continuous\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/><w:cols w:num=\"2\" w:space=\"567\"/></w:sectPr></w:pPr></w:p>\n```";

        private static readonly string[] Sentences =
        [
            "Příliš žluťoučký kůň úpěl ďábelské ódy, zatímco čeřící se voda šuměla pod mostem.",
            "Řeřicha na okně rostla a ďábelský čmelák bzučel kolem úlu, když se šedivý večer snášel nad údolím.",
            "Úterní schůze se protáhla, protože nikdo nechtěl přiznat, že zápis ztratil.",
            "Ďáblík Šimon pak dlouho hledal ztracený klíč pod rozkvetlou třešní.",
        ];

        private static readonly Dictionary<string, string> Markdown = new()
        {
            ["simple"] = """
                Úvodní odstavec před tabulkou: příliš žluťoučký kůň úpěl ďábelské ódy.

                | Jméno | Částka |
                |-------|--------|
                | Řehoř | 1 234,50 Kč |
                | Ůna | 99,90 Kč |

                Závěrečný odstavec za tabulkou, ať je vidět pořadí.
                """,
            ["numbers"] = """
                Přehled hospodaření za první čtvrtletí.

                | Měsíc | Příjmy | Výdaje | Zůstatek |
                |:------|-------:|-------:|---------:|
                | Leden | 12 500 | 9 870,50 | 2 629,50 |
                | Únor | 8 000 | 11 250 | −3 250 |
                | Březen | 150 000 | 7,25 | 149 992,75 |
                | Čtvrtek | 1 | 22 | 333 |

                Částky jsou v korunách.
                """,
            ["bold"] = """
                | **Položka** | **Množství** | **Cena** |
                |---|---|---|
                | Šroubovák | 2 ks | 149 Kč |
                | Kladívko | 1 ks | 289 Kč |
                | Hřebíky | 500 ks | 75 Kč |
                """,
            ["wrapped"] = """
                Ceník služeb pro rok 2026.

                | Kód | Popis služby | Cena |
                |------|--------------------------------------------------|----------|
                | A1 | Úklid kanceláře včetně vynesení odpadků, vytření podlah a umytí oken ve všech místnostech přízemí | 1 500 Kč |
                | B2 | Údržba zahrady | 800 Kč |
                | Č3 | Čištění koberců a čalouněného nábytku šetrnou metodou s ekologickými přípravky | 2 300 Kč |

                Ceny jsou uvedeny včetně DPH.
                """,
            // Enough justified text to fill the left column and flow into the right one, so both share baselines
            // and the narrow columns stretch the spaces.
            ["columns"] = string.Join("\n\n", Enumerable.Range(1, 14).Select(i => Justified($"Odstavec číslo {i}: " +
                string.Join(" ", Sentences.Skip(i % 4).Concat(Sentences.Take(i % 4)))))) + "\n\n" + OpenXmlColumns + "\n",
            ["list"] = """
                Nákupní seznam na víkend:

                - Čerstvé pečivo z pekárny na rohu
                - Žluté papriky, řepa a šťovík
                - Ďábelsky pálivá omáčka
                - Účtenku si schovat

                Postup:

                1. Ráno vstát
                2. Uvařit čaj
                3. Vyrazit na nákup
                """,
        };

        private static string Justified(string text) =>
            $"```{{=openxml}}\n<w:p><w:pPr><w:jc w:val=\"both\"/></w:pPr><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>\n```";

        public Samples()
        {
            if (!Tools.Has(ExternalTool.Pandoc) || !Tools.Has(ExternalTool.LibreOffice))
                return;
            var ctx = new OperationContext { Tools = Tools };
            foreach (var (name, md) in Markdown)
            {
                var source = Path.Combine(Dir, name + ".md");
                File.WriteAllText(source, md, new UTF8Encoding(false));
                var docx = Path.Combine(Dir, name + ".docx");
                PandocBackend.ConvertAsync(ctx, source, "md", docx, "docx").GetAwaiter().GetResult();
                Pdfs[name] = ToPdf(ctx, docx);
            }
            // A bordered HTML table: LibreOffice draws ruling lines around every cell.
            var html = Path.Combine(Dir, "ruled.html");
            File.WriteAllText(html, """
                <html><head><meta charset="utf-8"></head><body>
                <p>Rozpis nákladů na opravu střechy.</p>
                <table border="1" cellpadding="4" cellspacing="0">
                <tr><th>Práce</th><th>Hodiny</th></tr>
                <tr><td>Pokrývačské práce</td><td>16</td></tr>
                </table>
                <p>Termín dokončení je v říjnu.</p>
                </body></html>
                """, new UTF8Encoding(false));
            Pdfs["ruled"] = ToPdf(ctx, html);
        }

        private string ToPdf(OperationContext ctx, string input)
        {
            var produced = OfficeBackend.ConvertToTempAsync(ctx, input, "pdf").GetAwaiter().GetResult();
            var pdf = Path.ChangeExtension(input, ".pdf");
            File.Move(produced, pdf);
            OutputPaths.TryDelete(Path.GetDirectoryName(produced)!);
            return pdf;
        }

        public void Dispose() => OutputPaths.TryDelete(Dir);
    }

    private readonly Samples _s;
    private readonly ITestOutputHelper output;

    public PdfTableTests(ITestOutputHelper output, Samples samples)
    {
        this.output = output;
        _s = samples;
    }

    /// <summary>The Markdown of a sample, or null (with a note) when the tools to make it are missing.</summary>
    private string? Markdown(string name)
    {
        if (!_s.Pdfs.TryGetValue(name, out var pdf))
        {
            output.WriteLine($"sample {name} skipped (pandoc or LibreOffice missing)");
            return null;
        }
        var md = PdfStructure.ToMarkdown(pdf, null, pageBreaks: false);
        output.WriteLine(md);
        return md;
    }

    /// <summary>The table rows of the Markdown with the separator row left out, cells trimmed.</summary>
    private static List<string[]> Rows(string md) => md.Split('\n')
        .Where(l => l.StartsWith('|') && !l.Replace("|", "").Replace("-", "").Replace(":", "").Trim().Equals(""))
        .Select(l => l.Trim().Trim('|').Split(" | ").Select(c => c.Trim()).ToArray())
        .ToList();

    [Fact]
    public void Simple_table_becomes_a_pipe_table_in_reading_order()
    {
        if (Markdown("simple") is not { } md)
            return;
        var rows = Rows(md);
        Assert.Equal([["Jméno", "Částka"], ["Řehoř", "1 234,50 Kč"], ["Ůna", "99,90 Kč"]], rows);
        Assert.True(md.IndexOf("Úvodní", StringComparison.Ordinal) < md.IndexOf("| Jméno", StringComparison.Ordinal));
        Assert.True(md.IndexOf("| Ůna", StringComparison.Ordinal) < md.IndexOf("Závěrečný", StringComparison.Ordinal));
        // The words of the table are not repeated as paragraphs.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(md, "Řehoř"));
    }

    [Fact]
    public void Numbers_stay_in_their_columns_and_keep_right_alignment()
    {
        if (Markdown("numbers") is not { } md)
            return;
        var rows = Rows(md);
        Assert.Equal(5, rows.Count);
        Assert.Equal(["Měsíc", "Příjmy", "Výdaje", "Zůstatek"], rows[0]);
        Assert.Equal(["Leden", "12 500", "9 870,50", "2 629,50"], rows[1]);
        Assert.Equal(["Březen", "150 000", "7,25", "149 992,75"], rows[3]);
        Assert.Contains("---:|", md);
    }

    [Fact]
    public void Bold_header_stays_bold()
    {
        if (Markdown("bold") is not { } md)
            return;
        var rows = Rows(md);
        Assert.Equal(["**Položka**", "**Množství**", "**Cena**"], rows[0]);
        Assert.Equal(["Hřebíky", "500 ks", "75 Kč"], rows[3]);
    }

    [Fact]
    public void Wrapped_cell_is_joined_into_one_cell()
    {
        if (Markdown("wrapped") is not { } md)
            return;
        var rows = Rows(md);
        Assert.Equal(4, rows.Count);
        Assert.Equal("A1", rows[1][0]);
        Assert.Equal("Úklid kanceláře včetně vynesení odpadků, vytření podlah a umytí oken ve všech místnostech přízemí", rows[1][1]);
        Assert.Equal("1 500 Kč", rows[1][2]);
        Assert.Equal(["B2", "Údržba zahrady", "800 Kč"], rows[2]);
        Assert.Equal("Čištění koberců a čalouněného nábytku šetrnou metodou s ekologickými přípravky", rows[3][1]);
        Assert.Contains("Ceny jsou uvedeny včetně DPH.", md);
    }

    [Fact]
    public void Ruled_table_with_two_rows_is_found()
    {
        if (Markdown("ruled") is not { } md)
            return;
        Assert.Equal([["**Práce**", "**Hodiny**"], ["Pokrývačské práce", "16"]], Rows(md));
    }

    [Fact]
    public void Two_text_columns_stay_text()
    {
        if (Markdown("columns") is not { } md)
            return;
        Assert.DoesNotContain("|", md);
        // Read column by column: paragraphs whole, none interleaved with the other column's lines.
        Assert.Contains("Odstavec číslo 1: Řeřicha na okně rostla a ďábelský čmelák bzučel kolem úlu", md);
        Assert.Contains("Odstavec číslo 14: Úterní schůze se protáhla, protože nikdo nechtěl přiznat", md);
    }

    [Fact]
    public void Lists_stay_lists()
    {
        if (Markdown("list") is not { } md)
            return;
        Assert.DoesNotContain("|", md);
        Assert.Contains("- Žluté papriky, řepa a šťovík", md);
        Assert.Contains("2. Uvařit čaj", md);
    }

    [Fact]
    public async Task Pandoc_turns_the_detected_table_into_a_real_table()
    {
        if (!_s.Pdfs.TryGetValue("numbers", out var pdf))
            return;
        var outDir = Path.Combine(_s.Dir, "html");
        Directory.CreateDirectory(outDir);
        var ctx = new OperationContext { Tools = _s.Tools, OutputDirectory = outDir };
        var html = File.ReadAllText(await PdfBackend.ToDocumentAsync(new FileItem(pdf), "html", ctx));
        Assert.Contains("<table", html);
        Assert.Contains("<td style=\"text-align: right;\">149 992,75</td>", html);
    }
}
