using System.Text;
using Baba.Application.Accounting;
using Baba.Application.Importing;
using Baba.Application.Trade;
using Baba.Domain.Accounting;
using Baba.Infrastructure.Accounting;
using Baba.Infrastructure.Printing;
using ClosedXML.Excel;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Excel and CSV import of products, exchange rates, journal entries and opening balances: all or nothing, wrong rows named.</summary>
public class ListImportTests : AccountingFixture
{
    private static ListImportService Importer(Env e) => new(
        new TabularReader(), new AccountStore(e.Files), new PartyStore(e.Files), new CostCenterStore(e.Files), e.Tax,
        e.Products, e.Rates, e.Vouchers, e.StockDocs, e.Warehouses, e.FixedAssets, e.Files);

    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Xlsx(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 1, c + 1).Value = rows[r][c];
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ------------------------------------------------------------------ Products

    [Fact]
    public async Task Products_import_from_an_excel_file_with_their_accounts_and_tax_codes()
    {
        var e = await NewEnvAsync(countryCode: "SA");
        var file = Xlsx(
            ["Code", "Name", "Name (Arabic)", "Unit", "Sale price", "Purchase price", "Revenue account", "Expense account", "Tax code"],
            ["W1", "Widget", "قطعة", "pcs", "1,250.50", "800", "511", "422", "SA-VAT-STD"],
            ["", "Consulting hour", "", "hour", "90", "", "512", "", ""]);

        var result = await Importer(e).ImportProductsAsync("products.xlsx", file);

        Assert.True(result.Issues.Count == 0, string.Join(", ", result.Issues));
        Assert.Equal(2, result.Imported);
        var products = await e.Products.ListAsync();
        var widget = products.Single(p => p.Code == "W1");
        Assert.Equal(1250.5m, widget.SalePrice);
        Assert.Equal(e.Id("511"), widget.SalesAccountId);
        Assert.NotNull(widget.TaxCodeId);
        Assert.Equal("P002", products.Single(p => p.NameEn == "Consulting hour").Code); // a missing code is made up, after the highest number used
        Assert.Equal("Consulting hour", products.Single(p => p.NameEn == "Consulting hour").NameAr);
    }

    [Fact]
    public async Task One_wrong_product_row_imports_nothing_and_every_wrong_row_is_named()
    {
        var e = await NewEnvAsync();
        await e.Products.CreateAsync(new ProductInput("OLD", "", "Old", null, 1m, 1m, null, null));
        var file = Csv("Code,Name,Sale price,Revenue account,Tax code\nA1,Fine,10,511,\nOLD,Duplicate,5,,\nA3,,5,,\nA4,Bad price,abc,,\nA5,Bad account,5,9999,\nA6,Bad tax,5,,NOPE\n");

        var result = await Importer(e).ImportProductsAsync("p.csv", file);

        Assert.Equal(0, result.Imported);
        Assert.Contains(new ImportIssue(3, "product.code-duplicate"), result.Issues);
        Assert.Contains(new ImportIssue(4, "product.name-required"), result.Issues);
        Assert.Contains(new ImportIssue(5, "product.price-invalid"), result.Issues);
        Assert.Contains(new ImportIssue(6, "product.account-unknown"), result.Issues);
        Assert.Contains(new ImportIssue(7, "product.tax-code-unknown"), result.Issues);
        Assert.Single(await e.Products.ListAsync()); // only the one that was there
    }

    // ------------------------------------------------------------------ Exchange rates

    [Fact]
    public async Task Exchange_rates_import_with_arabic_headers_and_replace_a_rate_on_the_same_date()
    {
        var e = await NewEnvAsync();
        await e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, 0.3m));

        var result = await Importer(e).ImportExchangeRatesAsync("rates.csv", Csv("العملة,التاريخ,السعر\nUSD,01/10/2026,0.31\nEUR,2026-10-01,\"0,33\"\n"));

        Assert.Equal(2, result.Imported);
        var rates = await e.Rates.ListAsync();
        Assert.Equal(0.31m, rates.Single(r => r.CurrencyCode == "USD").Rate);
        Assert.Equal(0.33m, rates.Single(r => r.CurrencyCode == "EUR").Rate);
    }

    [Fact]
    public async Task Wrong_exchange_rate_rows_are_named_and_nothing_changes()
    {
        var e = await NewEnvAsync();

        var result = await Importer(e).ImportExchangeRatesAsync("rates.csv", Csv("Currency,Date,Rate\nUSD,2026-10-01,0.3\nXXX,2026-10-01,1\nKWD,2026-10-01,1\nEUR,notadate,0.3\nGBP,2026-10-01,0\n"));

        Assert.Equal(0, result.Imported);
        Assert.Contains(new ImportIssue(3, "rate.currency-unknown"), result.Issues);
        Assert.Contains(new ImportIssue(4, "rate.currency-is-base"), result.Issues);
        Assert.Contains(new ImportIssue(5, "rate.date-required"), result.Issues);
        Assert.Contains(new ImportIssue(6, "rate.rate-invalid"), result.Issues);
        Assert.Empty(await e.Rates.ListAsync());
    }

    // ------------------------------------------------------------------ Journal entries

    [Fact]
    public async Task Journal_entries_import_group_lines_by_entry_and_post_each_one()
    {
        var e = await NewEnvAsync();
        var file = Xlsx(
            ["Entry", "Date", "Account", "Debit", "Credit", "Description", "Customer"],
            ["1", "2026-10-02", "111", "500", "", "Cash in", ""],
            ["1", "", "31", "", "500", "Capital", ""],
            ["2", "2026-10-03", "113", "200", "", "Credit sale", "C001"],
            ["2", "", "511", "", "200", "", ""]);

        var result = await Importer(e).ImportJournalAsync("journal.xlsx", file);

        Assert.Equal(2, result.Imported);
        Assert.Equal(500m, await BalanceAsync(e, "111"));
        Assert.Equal(200m, await BalanceAsync(e, "113"));
        Assert.Equal(200m, (await e.Parties.ListAsync(PartyKind.Customer)).Single(p => p.Code == "C001").Balance);
        var vouchers = await e.Vouchers.ListAsync(new VoucherSearch(VoucherKind.Journal, null, null, null));
        Assert.Equal(2, vouchers.Count);
        Assert.All(vouchers, v => Assert.StartsWith("JV-2026-", v.Number));
    }

    [Fact]
    public async Task An_unbalanced_entry_a_missing_party_and_unknown_accounts_are_named_and_nothing_is_posted()
    {
        var e = await NewEnvAsync();
        var file = Csv("Entry,Date,Account,Debit,Credit,Customer\n1,2026-10-02,111,100,,\n1,,31,,90,\n2,2026-10-03,9999,5,,\n2,,31,,5,\n");

        var result = await Importer(e).ImportJournalAsync("j.csv", file);

        Assert.Equal(0, result.Imported);
        Assert.Contains(new ImportIssue(2, "balance.unbalanced"), result.Issues);
        Assert.Contains(new ImportIssue(4, "line.account-unknown"), result.Issues);
        Assert.Equal(0m, await BalanceAsync(e, "111"));
    }

    [Fact]
    public async Task A_journal_import_needs_an_entry_column_and_a_posting_failure_takes_back_the_entries_already_posted()
    {
        var e = await NewEnvAsync();
        var missing = await Importer(e).ImportJournalAsync("j.csv", Csv("Date,Account,Debit,Credit\n2026-10-02,111,5,\n2026-10-02,31,,5\n"));
        Assert.Contains(new ImportIssue(0, "journal.entry-column-missing"), missing.Issues);

        await e.Periods.SetLockedAsync(new DateOnly(2026, 11, 1), true);
        var file = Csv("Entry,Date,Account,Debit,Credit\n1,2026-10-02,111,5,\n1,,31,,5\n2,2026-11-05,111,7,\n2,,31,,7\n");
        var result = await Importer(e).ImportJournalAsync("j.csv", file);

        Assert.Equal(0, result.Imported);
        Assert.Contains(new ImportIssue(4, "date.locked-period"), result.Issues);
        Assert.Equal(0m, await BalanceAsync(e, "111")); // the first entry was taken back
        Assert.Empty(await e.Vouchers.ListAsync(new VoucherSearch(VoucherKind.Journal, null, null, null)));
    }

    // ------------------------------------------------------------------ Opening balances

    [Fact]
    public async Task Opening_balances_import_as_the_one_opening_voucher_and_cannot_be_imported_twice()
    {
        var e = await NewEnvAsync();
        var file = Csv("Account,Debit,Credit,Customer\n111,1000,,\n113,300,,C001\n31,,1300,\n");

        var result = await Importer(e).ImportOpeningBalancesAsync("opening.csv", file);

        Assert.Equal(3, result.Imported);
        var voucher = (await e.Vouchers.ListAsync(new VoucherSearch(VoucherKind.Opening, null, null, null))).Single();
        Assert.Equal(new DateOnly(2025, 12, 31), voucher.Date); // the day before the books start
        Assert.Equal(1000m, await BalanceAsync(e, "111"));
        Assert.Equal(300m, (await e.Parties.ListAsync(PartyKind.Customer)).Single().Balance);

        var again = await Importer(e).ImportOpeningBalancesAsync("opening.csv", file);
        Assert.Contains(again.Issues, i => i.Code == "opening.already-exists");
    }

    [Fact]
    public async Task Unbalanced_opening_balances_are_refused()
    {
        var e = await NewEnvAsync();

        var result = await Importer(e).ImportOpeningBalancesAsync("opening.csv", Csv("Account,Debit,Credit\n111,100,\n31,,90\n"));

        Assert.Contains(result.Issues, i => i.Code == "balance.unbalanced");
    }
}
