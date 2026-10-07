using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Printing;
using Baba.Application.Reporting;
using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;
using ClosedXML.Excel;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>
/// Every list can be exported to Excel with real numbers and real dates (a requirement of the owner: exporting data to Excel is
/// really important), in the language the person chooses.
/// </summary>
public class ListExportTests : AccountingFixture
{
    private async Task<(Env Env, IXLWorksheet Sheet)> ExportAsync(Env e, ReportResult report, PrintLayout layout = PrintLayout.English)
    {
        var file = await e.Exports.ExportAsync(report, ExportFormat.Xlsx, layout);
        Assert.EndsWith(".xlsx", file.FileName);
        var workbook = new XLWorkbook(new MemoryStream(file.Content));
        return (e, workbook.Worksheet(1));
    }

    private static IXLRow RowWith(IXLWorksheet sheet, string firstCell) => sheet.RowsUsed().Single(r => r.Cell(1).GetString() == firstCell);

    private static async Task<Env> SeededAsync(ListExportTests test)
    {
        var e = await test.NewEnvAsync(countryCode: "SA");
        var vat = (await e.Tax.ListAsync()).Single(c => c.Rate == 15m);
        await e.Products.CreateAsync(new ProductInput("P1", "منتج", "Widget", "pcs", 50m, 30m, e.Id("511"), e.Id("422"), vat.Id));
        await e.Trade.IssueAsync(null, new DocumentInput(
            DocumentKind.SalesInvoice, new DateOnly(2026, 10, 6), null, e.Customer, null, null, "PO-1", null, 0,
            [new DocumentLineInput(null, null, e.Id("511"), "item", 2, 500m, 0, null, vat.Id)]));
        return e;
    }


    [Fact]
    public async Task The_document_list_exports_with_real_dates_and_totals()
    {
        var e = await SeededAsync(this);

        var (_, sheet) = await ExportAsync(e, await e.Listings.DocumentsAsync(new DocumentSearch()));

        var header = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Number");
        Assert.Equal("Total", header.Cell(9).GetString());
        var row = sheet.RowsUsed().Single(r => r.Cell(1).GetString().StartsWith("SI-2026-"));
        Assert.Equal(XLDataType.DateTime, row.Cell(2).DataType);        // the date is a real Excel date, not text
        Assert.Equal(new DateTime(2026, 10, 6), row.Cell(2).GetDateTime());
        Assert.Equal("First Customer", row.Cell(4).GetString());
        Assert.Equal("Sales invoice", row.Cell(5).GetString());
        Assert.Equal(XLDataType.Number, row.Cell(9).DataType);
        Assert.Equal(1150d, row.Cell(9).GetDouble());                   // 1,000 plus 15% tax
    }

    [Fact]
    public async Task The_document_list_can_be_filtered_and_exported_in_arabic()
    {
        var e = await SeededAsync(this);

        var none = await e.Listings.DocumentsAsync(new DocumentSearch(DocumentKind.Quote));
        Assert.DoesNotContain(none.Rows, r => r.Cells[0].Text?.StartsWith("SI-") == true);

        var (_, sheet) = await ExportAsync(e, await e.Listings.DocumentsAsync(new DocumentSearch(DocumentKind.SalesInvoice)), PrintLayout.Arabic);
        Assert.True(sheet.RightToLeft);
        Assert.Equal("فاتورة مبيعات", sheet.RowsUsed().Single(r => r.Cell(1).GetString().StartsWith("SI-2026-")).Cell(5).GetString());
    }

    [Fact]
    public async Task Products_parties_tax_codes_rates_and_schedules_export_too()
    {
        var e = await SeededAsync(this);
        await e.Rates.SetAsync(new CurrencyRateInput("USD", new DateOnly(2026, 10, 1), 0.375m));
        var invoice = (await e.Trade.ListAsync(new DocumentSearch())).Single();
        await e.Recurring.CreateFromDocumentAsync(invoice.Id, new RecurringInput("Rent", RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 1), null, false));

        var (_, products) = await ExportAsync(e, await e.Listings.ProductsAsync());
        var widget = RowWith(products, "P1");
        Assert.Equal(50d, widget.Cell(4).GetDouble());
        Assert.Equal(XLDataType.Number, widget.Cell(4).DataType);
        Assert.Equal("SA-VAT-STD", widget.Cell(6).GetString());

        var (_, parties) = await ExportAsync(e, await e.Listings.PartiesAsync(PartyKind.Customer));
        var customer = RowWith(parties, "C001");
        Assert.Equal("First Customer", customer.Cell(2).GetString());
        Assert.Equal(1150d, customer.Cell(9).GetDouble()); // what the customer owes
        Assert.DoesNotContain(parties.RowsUsed(), r => r.Cell(1).GetString() == "S001");

        var (_, codes) = await ExportAsync(e, await e.Listings.TaxCodesAsync());
        Assert.Equal("15", RowWith(codes, "SA-VAT-STD").Cell(3).GetString());
        Assert.Equal("Yes", RowWith(codes, "SA-VAT-STD").Cell(9).GetString());

        var (_, rates) = await ExportAsync(e, await e.Listings.ExchangeRatesAsync());
        Assert.Equal("0.375", RowWith(rates, "USD").Cell(3).GetString());

        var (_, schedules) = await ExportAsync(e, await e.Listings.RecurringAsync());
        var rent = RowWith(schedules, "Rent");
        Assert.Equal("Monthly", rent.Cell(3).GetString());
        Assert.Equal(XLDataType.DateTime, rent.Cell(4).DataType);
    }

    [Fact]
    public async Task The_tax_return_exports_to_excel_with_its_amounts_as_numbers()
    {
        var e = await SeededAsync(this);

        var (_, sheet) = await ExportAsync(e, await e.Reports.TaxReturnAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));

        var standard = RowWith(sheet, "SA-VAT-STD");
        Assert.Equal(1000d, standard.Cell(4).GetDouble());
        Assert.Equal(150d, standard.Cell(5).GetDouble());
        Assert.Equal(XLDataType.Number, standard.Cell(5).DataType);
    }
}
