using System.Text;
using ClosedXML.Excel;
using FileAction.Core;
using FileAction.Core.Backends;
using Xunit.Abstractions;

namespace FileAction.Tests;

/// <summary>
/// PDF → XLSX on the documents people actually convert: a multi-page bank statement, an invoice and an
/// English price list, made as PDFs by LibreOffice. Skipped when LibreOffice is missing.
/// </summary>
[Trait("Category", "Integration")]
public class PdfToExcelTests : IClassFixture<PdfToExcelTests.Samples>
{
    public sealed class Samples : IDisposable
    {
        public string Dir { get; } = OutputPaths.TempDirectory();
        public ToolLocator Tools { get; } = new();
        public Dictionary<string, string> Pdfs { get; } = new();

        public Samples()
        {
            if (!Tools.Has(ExternalTool.LibreOffice))
                return;
            var ctx = new OperationContext { Tools = Tools };
            Pdfs["statement"] = Make(ctx, "vypis", Statement(repeatHeader: true));
            // Header printed once: the last rows may land on a new page with nothing to recognise them by.
            Pdfs["statement-once"] = Make(ctx, "vypis-jednou", Statement(repeatHeader: false));

            Pdfs["invoice"] = Make(ctx, "faktura", """
                <h1>Faktura 2026-0147</h1>
                <p>Dodavatel: Ďáblík s.r.o., IČO 12345678</p>
                <table cellpadding="4">
                <tr><td><b>Položka</b></td><td><b>Množství</b></td><td><b>Cena za ks</b></td><td><b>Celkem</b></td></tr>
                <tr><td>Konzultace</td><td>3</td><td>1 500,00 Kč</td><td>4 500,00 Kč</td></tr>
                <tr><td>Údržba webu</td><td>12</td><td>850,00 Kč</td><td>10 200,00 Kč</td></tr>
                <tr><td>Hosting na rok</td><td>1</td><td>2 400,00 Kč</td><td>2 400,00 Kč</td></tr>
                </table>
                <p>Celkem k úhradě: 17 100,00 Kč. Splatnost 14 dní.</p>
                """);

            Pdfs["prices"] = Make(ctx, "prices", """
                <h1>Price list 2026</h1>
                <table border="1" cellpadding="4" cellspacing="0">
                <tr><th>Product</th><th>Price</th><th>Change</th></tr>
                <tr><td>Standard licence</td><td>1,234.50</td><td>12.5%</td></tr>
                <tr><td>Team licence</td><td>4,990.00</td><td>-3%</td></tr>
                <tr><td>Support hours</td><td>89.90</td><td>0%</td></tr>
                </table>
                """);

            // An online-banking statement: shaded, padded rows set far apart, tight columns (a right-aligned amount
            // almost touching its currency), Spanish numbers and dates.
            Pdfs["bbva"] = Make(ctx, "extracto", Bbva());

            Pdfs["text"] = Make(ctx, "dopis", "<p>Vážený pane, děkujeme za objednávku. Příliš žluťoučký kůň úpěl ďábelské ódy.</p><p>S pozdravem</p>");
        }

        private static string Statement(bool repeatHeader)
        {
            var statement = new StringBuilder();
            statement.Append("<h2>Výpis z účtu 2026/10</h2><p>Majitel účtu: Řehoř Ůna</p><table border=\"1\" cellpadding=\"3\" cellspacing=\"0\">");
            var header = "<tr><th>Datum</th><th>Popis</th><th>Částka</th><th>Zůstatek</th></tr>";
            statement.Append(repeatHeader ? $"<thead>{header}</thead><tbody>" : $"<tbody>{header}");
            var balance = 25000.0;
            for (var i = 1; i <= 70; i++)
            {
                var amount = i % 5 == 0 ? 3500.25 : -(i * 37.5 + 0.4);
                balance += amount;
                statement.Append($"<tr><td>{1 + i % 28}.10.2026</td><td>{(amount > 0 ? "Příchozí platba – výplata" : $"Platba kartou Žabka č. {i}")}</td>"
                    + $"<td>{Czech(amount)}</td><td>{Czech(balance)}</td></tr>");
            }
            statement.Append("</tbody></table><p>Konec výpisu.</p>");
            return statement.ToString();
        }

        private static string Bbva()
        {
            string Es(double v) => (v < 0 ? "-" : "") + Math.Abs(v).ToString("#,##0.00", System.Globalization.CultureInfo.GetCultureInfo("es-ES"))
                .Replace(" ", "").Replace(" ", "");
            var concepts = new[] { ("PAGO CON TARJETA MERCADONA MADRID", "Pago con tarjeta", -1), ("TRANSFERENCIA RECIBIDA DE JUAN GARCIA LOPEZ", "Transferencia", 1),
                ("ADEUDO RECIBO IBERDROLA CLIENTES SAU", "Recibo", -1), ("BIZUM ENVIADO A MARIA", "Bizum", -1) };
            var balance = 2450.37;
            var rows = new StringBuilder();
            for (var i = 0; i < 40; i++)
            {
                var (concept, kind, sign) = concepts[i % concepts.Length];
                var amount = sign * (i * 37.13 % 1500 + 12.5);
                balance += amount;
                var date = $"{i / 2 + 1:00}/09/2026";
                rows.Append($"<tr{(i % 2 == 1 ? " style=\"background:#f0f0f0\"" : "")}><td>{date}</td><td>{date}</td><td>{concept}</td><td>{kind}</td>"
                    + $"<td align=\"right\">{Es(amount)}</td><td>EUR</td><td align=\"right\">{Es(balance)}</td><td>EUR</td></tr>");
            }
            return "<style>body{font-family:Arial;font-size:8pt} td,th{font-size:7.5pt;padding:2px 3px} th{text-align:left;border-bottom:1px solid #000}</style>"
                + "<p><b>BBVA</b></p><p>Movimientos de la cuenta</p>"
                + "<table><tr><td>Titular:</td><td>EJEMPLO PEREZ, ANA</td></tr><tr><td>Cuenta:</td><td>ES12 0182 0000 0000 0000 0000</td></tr></table>"
                + "<table cellspacing=\"0\" width=\"100%\"><tr><th>F. Contable</th><th>F. Valor</th><th>Concepto</th><th>Movimiento</th><th align=\"right\">Importe</th>"
                + "<th>Divisa</th><th align=\"right\">Disponible</th><th>Divisa</th></tr>" + rows + "</table>";
        }

        private static string Czech(double value) => value.ToString("#,##0.00", System.Globalization.CultureInfo.GetCultureInfo("cs-CZ")).Replace(' ', ' ');

        private string Make(OperationContext ctx, string name, string body)
        {
            var html = Path.Combine(Dir, name + ".html");
            File.WriteAllText(html, $"<html><head><meta charset=\"utf-8\"></head><body>{body}</body></html>", new UTF8Encoding(false));
            var produced = OfficeBackend.ConvertToTempAsync(ctx, html, "pdf").GetAwaiter().GetResult();
            var pdf = Path.Combine(Dir, name + ".pdf");
            File.Move(produced, pdf);
            OutputPaths.TryDelete(Path.GetDirectoryName(produced)!);
            return pdf;
        }

        public void Dispose() => OutputPaths.TryDelete(Dir);
    }

    private readonly Samples _s;
    private readonly ITestOutputHelper _output;

    public PdfToExcelTests(ITestOutputHelper output, Samples samples)
    {
        _output = output;
        _s = samples;
    }

    private async Task<XLWorkbook?> Convert(string name)
    {
        if (!_s.Pdfs.TryGetValue(name, out var pdf))
        {
            _output.WriteLine($"sample {name} skipped (LibreOffice missing)");
            return null;
        }
        var xlsx = await PdfBackend.ToSpreadsheetAsync(new FileItem(pdf), new OperationContext { Tools = _s.Tools, OutputDirectory = Path.Combine(_s.Dir, "out", name) });
        var book = new XLWorkbook(xlsx);
        foreach (var ws in book.Worksheets)
        {
            _output.WriteLine($"== {ws.Name}: {ws.LastRowUsed()?.RowNumber()} rows");
            foreach (var row in ws.RowsUsed().Take(4))
                _output.WriteLine(string.Join(" | ", row.CellsUsed().Select(c => $"{c.Value} ({c.DataType})")));
        }
        return book;
    }

    [Fact]
    public async Task Bank_statement_over_several_pages_is_one_sheet_of_numbers_and_dates()
    {
        using var book = await Convert("statement");
        if (book is null)
            return;
        var ws = Assert.Single(book.Worksheets);
        Assert.Equal("Datum", ws.Cell(1, 1).GetText());
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);
        // 70 transactions, the header repeated on later pages dropped.
        Assert.Equal(71, ws.LastRowUsed()!.RowNumber());
        Assert.Equal(XLDataType.DateTime, ws.Cell(2, 1).DataType);
        Assert.Equal(-37.9, ws.Cell(2, 3).GetDouble(), 2);
        Assert.Equal(XLDataType.Number, ws.Cell(71, 4).DataType);
        Assert.StartsWith("Platba kartou Žabka", ws.Cell(2, 2).GetText());
    }

    [Fact]
    public async Task Statement_with_its_header_printed_once_keeps_every_row()
    {
        using var book = await Convert("statement-once");
        if (book is null)
            return;
        var ws = Assert.Single(book.Worksheets);
        Assert.Equal("Datum", ws.Cell(1, 1).GetText());
        Assert.Equal(71, ws.LastRowUsed()!.RowNumber());
        Assert.Equal(-3019.15, ws.Cell(70, 4).GetDouble(), 2);
        Assert.Equal(3500.25, ws.Cell(71, 3).GetDouble(), 2);
    }

    [Fact]
    public async Task Invoice_amounts_with_currency_become_numbers()
    {
        using var book = await Convert("invoice");
        if (book is null)
            return;
        var ws = Assert.Single(book.Worksheets);
        Assert.Equal("Položka", ws.Cell(1, 1).GetText());
        Assert.Equal(4, ws.LastRowUsed()!.RowNumber());
        Assert.Equal(1500, ws.Cell(2, 3).GetDouble(), 2);
        Assert.Equal(10200, ws.Cell(3, 4).GetDouble(), 2);
        Assert.Equal(12, ws.Cell(3, 2).GetDouble());
    }

    [Fact]
    public async Task English_numbers_and_percentages()
    {
        using var book = await Convert("prices");
        if (book is null)
            return;
        var ws = Assert.Single(book.Worksheets);
        Assert.Equal(1234.5, ws.Cell(2, 2).GetDouble(), 2);
        Assert.Equal(0.125, ws.Cell(2, 3).GetDouble(), 4);
        Assert.Equal(-0.03, ws.Cell(3, 3).GetDouble(), 4);
    }

    [Fact]
    public async Task Spanish_bank_statement_with_shaded_rows()
    {
        using var book = await Convert("bbva");
        if (book is null)
            return;
        var ws = Assert.Single(book.Worksheets);
        Assert.Equal(["F. Contable", "F. Valor", "Concepto", "Movimiento", "Importe", "Divisa", "Disponible", "Divisa"],
            ws.Row(1).CellsUsed().Select(c => c.GetText()).ToArray());
        Assert.Equal(41, ws.LastRowUsed()!.RowNumber());
        Assert.Equal(new DateTime(2026, 9, 1), ws.Cell(2, 1).GetDateTime());
        Assert.Equal(-12.5, ws.Cell(2, 5).GetDouble(), 2);
        Assert.Equal("EUR", ws.Cell(2, 6).GetText());
        Assert.Equal(XLDataType.Number, ws.Cell(41, 7).DataType);
    }

    [Fact]
    public async Task A_pdf_without_tables_says_so()
    {
        if (!_s.Pdfs.TryGetValue("text", out var pdf))
            return;
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            PdfBackend.ToSpreadsheetAsync(new FileItem(pdf), new OperationContext { Tools = _s.Tools, OutputDirectory = Path.Combine(_s.Dir, "out", "text") }));
        Assert.Contains("tabulk", ex.Message);
    }
}
