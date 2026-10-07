using System.Text;
using Baba.Application.Importing;
using Baba.Domain.Accounting;
using Baba.Infrastructure.Accounting;
using Baba.Infrastructure.Printing;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Importing a chart of accounts and customers or suppliers from CSV or Excel files (brief section 10.2).</summary>
public class ImportTests : AccountingFixture
{
    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    private static ImportService Importer(Env e) => new(new AccountStore(e.Files), new PartyStore(e.Files), new TabularReader());

    // ------------------------------------------------------------------ Accounts

    [Fact]
    public async Task Accounts_are_imported_with_groups_inherited_types_and_special_uses()
    {
        var e = await NewEnvAsync();
        var file = "Code,Name,Parent,Type,Role\n9,Other assets,,Asset,\n91,Petty cash,9,,Cash\n92,Vehicles 2,9,,";

        var result = await Importer(e).ImportAccountsAsync("accounts.csv", Csv(file));

        Assert.Equal((3, 0), (result.Imported, result.Issues.Count));
        var chart = (await e.Chart.ListAsync()).ToDictionary(a => a.Code);
        Assert.False(chart["9"].IsPosting);          // it is the parent of the others, so it is a group
        Assert.True(chart["91"].IsPosting);
        Assert.Equal((Baba.Domain.AccountType.Asset, Baba.Domain.AccountRole.CashOrBank), (chart["91"].Type, chart["91"].Role)); // the type comes from the parent
        Assert.Equal(chart["9"].Id, chart["92"].ParentId);
    }

    [Fact]
    public async Task The_accounts_may_be_listed_in_any_order_and_in_arabic()
    {
        var e = await NewEnvAsync();
        var file = "الرمز,الاسم (بالعربية),الاسم (بالإنجليزية),الحساب الأب,النوع\n"
            + "951,السيارات,Cars,95,\n"
            + "95,أصول أخرى,Other assets,,أصول";

        var result = await Importer(e).ImportAccountsAsync("حسابات.csv", Csv(file));

        Assert.Equal(2, result.Imported);
        var chart = (await e.Chart.ListAsync()).ToDictionary(a => a.Code);
        Assert.Equal((chart["95"].Id, "السيارات", "Cars"), (chart["951"].ParentId!.Value, chart["951"].NameAr, chart["951"].NameEn));
    }

    [Fact]
    public async Task One_wrong_account_means_none_are_imported_and_every_wrong_row_is_named()
    {
        var e = await NewEnvAsync();
        var before = (await e.Chart.ListAsync()).Count;
        var file = string.Join('\n',
            "Code,Name,Parent,Type,Role",
            "8,Fine group,,Asset,",          // row 2: fine
            "111,Duplicate code,,Asset,",    // row 3: the code is taken
            "81,Orphan,nope,Asset,",         // row 4: no such parent
            "82,Odd type,,Banana,",          // row 5: unknown type
            "83,No type,,,",                 // row 6: no type and no parent
            "84,Wrong role,8,,Receivable,",  // row 7: fine so far
            "85,Bad role,8,,Hovercraft",     // row 8: unknown role
            "86,Under a posting account,111,Asset,"); // row 9: 111 takes entries, it cannot hold accounts

        var result = await Importer(e).ImportAccountsAsync("accounts.csv", Csv(file));

        Assert.Equal(0, result.Imported);
        Assert.Equal(
            [(3, "account.code-duplicate"), (4, "account.parent-unknown"), (5, "account.type-unknown"), (6, "account.type-required"), (8, "account.role-unknown"), (9, "account.parent-must-be-group")],
            result.Issues.Select(i => (i.Row, i.Code)));
        Assert.Equal(before, (await e.Chart.ListAsync()).Count); // nothing was added
    }

    [Fact]
    public async Task A_file_without_a_code_column_or_without_rows_is_explained()
    {
        var e = await NewEnvAsync();
        var importer = Importer(e);

        Assert.Equal("accounts.code-column-missing", (await importer.ImportAccountsAsync("x.csv", Csv("Name,Type\nx,Asset"))).Issues.Single().Code);
        Assert.Equal("import.empty", (await importer.ImportAccountsAsync("x.csv", Csv("Code,Name,Type\n"))).Issues.Single().Code);
        Assert.Equal("import.unreadable", (await importer.ImportAccountsAsync("x.xlsx", [])).Issues.Single().Code);
    }

    // ------------------------------------------------------------------ Customers and suppliers

    [Fact]
    public async Task Customers_are_imported_with_terms_limits_and_automatic_codes()
    {
        var e = await NewEnvAsync();
        var file = "Name,Phone,Credit limit,Payment terms,Tax number\nGulf Traders,+965 5555 1111,\"1,500.5\",45,TX-77\nالنور للتجارة,,,,";

        var result = await Importer(e).ImportPartiesAsync(PartyKind.Customer, "customers.csv", Csv(file));

        Assert.Equal(2, result.Imported);
        var all = (await e.Parties.ListAsync(PartyKind.Customer)).ToDictionary(p => p.Code);
        Assert.Equal(["C001", "C002", "C003"], all.Keys.Order()); // C001 is the one the fixture made, so the new ones are C002 and C003
        Assert.Equal(("Gulf Traders", 1_500.5m, 45, "TX-77"), (all["C002"].NameEn, all["C002"].CreditLimit, all["C002"].PaymentTermsDays, all["C002"].TaxNumber));
        Assert.Equal("النور للتجارة", all["C003"].NameEn); // one name is used for both languages
    }

    [Fact]
    public async Task A_kind_column_can_mix_customers_and_suppliers_in_one_file()
    {
        var e = await NewEnvAsync();
        var file = "Kind,Name\ncustomer,Buyer One\nمورد,المورد الثاني\nSupplier,Seller One";

        var result = await Importer(e).ImportPartiesAsync(PartyKind.Customer, "parties.csv", Csv(file));

        Assert.Equal(3, result.Imported);
        Assert.Equal(["Buyer One"], (await e.Parties.ListAsync(PartyKind.Customer)).Where(p => p.Code != "C001").Select(p => p.NameEn));
        Assert.Equal(["Seller One", "المورد الثاني"], (await e.Parties.ListAsync(PartyKind.Supplier)).Where(p => p.Code != "S001").Select(p => p.NameEn).Order());
    }

    [Fact]
    public async Task One_wrong_party_means_none_are_imported()
    {
        var e = await NewEnvAsync();
        var before = (await e.Parties.ListAsync()).Count;
        var file = string.Join('\n',
            "Code,Name,Credit limit,Payment terms",
            "C010,Fine,100,30",               // row 2
            "C010,Same code again,,",         // row 3: duplicate within the file
            "C001,Taken,,",                   // row 4: duplicate of an existing code
            "C011,,,",                        // row 5: no name
            "C012,Bad limit,lots,",           // row 6
            "C013,Bad terms,,soon",           // row 7
            "C014,Too long,,900");            // row 8

        var result = await Importer(e).ImportPartiesAsync(PartyKind.Customer, "customers.csv", Csv(file));

        Assert.Equal(0, result.Imported);
        Assert.Equal(
            [(3, "party.code-duplicate"), (4, "party.code-duplicate"), (5, "party.name-required"), (6, "party.credit-limit-invalid"), (7, "party.terms-invalid"), (8, "party.terms-invalid")],
            result.Issues.Select(i => (i.Row, i.Code)));
        Assert.Equal(before, (await e.Parties.ListAsync()).Count);
    }

    [Fact]
    public async Task An_excel_list_of_customers_is_imported()
    {
        var e = await NewEnvAsync();
        using var stream = new MemoryStream();
        using (var workbook = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Customers");
            sheet.Cell(1, 1).Value = "Name";
            sheet.Cell(1, 2).Value = "Credit limit";
            sheet.Cell(1, 3).Value = "Payment terms";
            sheet.Cell(2, 1).Value = "Excel Customer";
            sheet.Cell(2, 2).Value = 2500;
            sheet.Cell(2, 3).Value = 60;
            workbook.SaveAs(stream);
        }

        var result = await Importer(e).ImportPartiesAsync(PartyKind.Customer, "customers.xlsx", stream.ToArray());

        Assert.Equal(1, result.Imported);
        var party = (await e.Parties.ListAsync(PartyKind.Customer)).Single(p => p.NameEn == "Excel Customer");
        Assert.Equal((2_500m, 60), (party.CreditLimit, party.PaymentTermsDays));
    }
}
