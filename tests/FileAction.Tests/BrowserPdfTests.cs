using System.Text;
using FileAction.Core;
using FileAction.Core.Backends;
using FileAction.Core.Operations;
using UglyToad.PdfPig;

namespace FileAction.Tests;

/// <summary>Markdown and web pages printed to PDF by Edge or Chrome, without LibreOffice. Skipped without them.</summary>
[Trait("Category", "Integration")]
public class BrowserPdfTests : IDisposable
{
    private readonly string _dir = OutputPaths.TempDirectory();
    private readonly ToolLocator _tools = new();

    public void Dispose() => OutputPaths.TryDelete(_dir);

    private OperationContext Context() => new() { Tools = _tools, OutputDirectory = Path.Combine(_dir, "out") };

    private static string Text(string pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    [Fact]
    public async Task Markdown_becomes_a_pdf_with_its_text_tables_and_code()
    {
        if (_tools.Browser is null || !_tools.Has(ExternalTool.Pandoc))
            return;
        var md = Path.Combine(_dir, "poznámky.md");
        File.WriteAllText(md, """
            # Příliš žluťoučký kůň

            Úpěl **ďábelské** ódy a [odkaz](https://example.com).

            | Položka | Cena |
            |---------|-----:|
            | Řepa    | 12,50 |

            ```csharp
            var x = 42;
            ```
            """, new UTF8Encoding(false));
        var op = Catalog.Build().Single(o => o.Id == "doc.to.pdf");
        Assert.True(op.IsAvailable(_tools, [new FileItem(md)]));
        var pdf = Assert.Single((await op.ExecuteAsync([new FileItem(md)], Context())).Outputs);
        Assert.Equal("poznámky.pdf", Path.GetFileName(pdf));
        var text = Text(pdf);
        Assert.Contains("Příliš žluťoučký kůň", text);
        Assert.Contains("ďábelské", text);
        Assert.Contains("Řepa", text);
        Assert.Contains("42", text);
    }

    [Fact]
    public async Task A_web_page_is_printed_as_it_looks()
    {
        if (_tools.Browser is null)
            return;
        var html = Path.Combine(_dir, "stránka.html");
        File.WriteAllText(html, "<html><head><meta charset=\"utf-8\"><title>T</title></head><body><h1>Ahoj světe</h1><p>Žluťoučký</p></body></html>",
            new UTF8Encoding(false));
        var pdf = Path.Combine(_dir, "stránka.pdf");
        await BrowserBackend.PrintToPdfAsync(Context(), html, pdf);
        var text = Text(pdf);
        Assert.Contains("Ahoj světe", text);
        Assert.Contains("Žluťoučký", text);
        // No header or footer with the file URL and date.
        Assert.DoesNotContain("file:", text);
    }
}
