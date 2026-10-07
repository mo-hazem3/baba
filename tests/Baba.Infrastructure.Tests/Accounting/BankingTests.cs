using System.Text;
using Baba.Application;
using Baba.Application.Accounting;
using Baba.Application.Banking;
using Baba.Domain.Accounting;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Transfers, bank reconciliation and bank statement import (brief section 10.2).</summary>
public class BankingTests : AccountingFixture
{
    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// The bank account (112): 10,000 of capital on 1 Oct, rent 1,000 on 3 Oct and utilities 250 on 8 Oct paid from it,
    /// then 400 of sales received on 20 Oct.
    /// </summary>
    private async Task<(Env Env, Guid Bank, VoucherDto Capital, VoucherDto Rent, VoucherDto Utilities, VoucherDto Sales)> SeedBankAsync()
    {
        var e = await NewEnvAsync();
        async Task<VoucherDto> Post(VoucherInput input) => await e.Vouchers.SaveAndPostAsync(null, input);

        var capital = await Post(Receipt(e, new DateOnly(2026, 10, 1), ("31", 10_000m)));
        var rent = await Post(Payment(e, new DateOnly(2026, 10, 3), ("422", 1_000m)) with { CashAccountId = e.Id("112") });
        var utilities = await Post(Payment(e, new DateOnly(2026, 10, 8), ("423", 250m)) with { CashAccountId = e.Id("112") });
        var sales = await Post(Receipt(e, new DateOnly(2026, 10, 20), ("511", 400m)));
        return (e, e.Id("112"), capital, rent, utilities, sales);
    }

    private static async Task<IReadOnlyList<Guid>> EntryIdsAsync(Env e, Guid bank, DateOnly upTo) =>
        (await e.Bank.GetReconciliationAsync(bank, upTo)).Entries.Select(x => x.EntryId).ToList();

    // ------------------------------------------------------------------ Transfers

    [Fact]
    public async Task A_transfer_moves_money_between_two_bank_or_cash_accounts_and_is_numbered()
    {
        var e = await NewEnvAsync();

        var transfer = await e.Vouchers.SaveAndPostAsync(null, new VoucherInput(
            VoucherKind.Transfer, Oct6, e.Id("111"), "TR-1", "to the bank", [new VoucherLineInput(null, e.Id("112"), null, 300m, 0)]));

        Assert.Equal("TV-2026-0001", transfer.Number);
        Assert.Equal(-300m, await BalanceAsync(e, "111")); // nothing was in the cash box yet, so it goes negative
        Assert.Equal(300m, await BalanceAsync(e, "112"));

        var refused = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, new VoucherInput(
            VoucherKind.Transfer, Oct6, e.Id("111"), null, null, [new VoucherLineInput(null, e.Id("422"), null, 10m, 0)])));
        Assert.Contains("line.account-not-cash", Codes(refused));
    }

    // ------------------------------------------------------------------ Bank accounts

    [Fact]
    public async Task The_bank_list_shows_each_account_with_its_balance_and_how_much_is_still_to_reconcile()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();

        var accounts = await e.Bank.ListAccountsAsync();

        var account = accounts.Single(a => a.AccountId == bank);
        Assert.Equal((9_150m, 0m, 4, null), (account.Balance, account.ReconciledBalance, account.Unreconciled, account.LastStatementDate));
        Assert.Contains(accounts, a => a.Code == "111"); // cash on hand is a cash account too
        Assert.DoesNotContain(accounts, a => a.Code == "511");
    }

    // ------------------------------------------------------------------ Reconciliation

    [Fact]
    public async Task A_reconciliation_can_be_finished_only_when_the_ticked_entries_equal_the_statement_balance()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        var oct10 = new DateOnly(2026, 10, 10);
        var entries = await EntryIdsAsync(e, bank, oct10);
        Assert.Equal(3, entries.Count); // the sale of 20 Oct is after the statement date

        // The bank says 8,750.000, but only two entries are ticked: 10,000 - 1,000 = 9,000.
        var wrong = await RefusedAsync(() => e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(oct10, 8_750m, entries.Take(2).ToList(), null)));
        Assert.Contains(wrong.Issues, i => i is { Field: "statementBalance", Code: "reconciliation.difference" });

        var done = await e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(oct10, 8_750m, entries, null));

        Assert.Equal((oct10, 8_750m), (done.StatementDate, done.StatementBalance));
        var account = (await e.Bank.ListAccountsAsync()).Single(a => a.AccountId == bank);
        Assert.Equal((8_750m, 1, oct10, 8_750m), (account.ReconciledBalance, account.Unreconciled, account.LastStatementDate, account.LastStatementBalance));
        Assert.Empty(await EntryIdsAsync(e, bank, oct10)); // nothing is left to tick up to that date
    }

    [Fact]
    public async Task The_next_reconciliation_continues_from_the_balance_already_agreed()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        await e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(new DateOnly(2026, 10, 10), 8_750m, await EntryIdsAsync(e, bank, new DateOnly(2026, 10, 10)), null));

        var oct31 = new DateOnly(2026, 10, 31);
        var view = await e.Bank.GetReconciliationAsync(bank, oct31);
        Assert.Equal(8_750m, view.OpeningBalance);
        Assert.Equal([400m], view.Entries.Select(x => x.Amount));

        await e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(oct31, 9_150m, view.Entries.Select(x => x.EntryId).ToList(), null)); // 8,750 + 400

        var history = await e.Bank.HistoryAsync(bank);
        Assert.Equal([9_150m, 8_750m], history.Select(h => h.StatementBalance));
    }

    [Fact]
    public async Task An_entry_cannot_be_ticked_twice_or_for_another_account()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        var oct10 = new DateOnly(2026, 10, 10);
        var entries = await EntryIdsAsync(e, bank, oct10);
        await e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(oct10, 8_750m, entries, null));

        var again = await RefusedAsync(() => e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(oct10, 8_750m, entries, null)));
        Assert.Contains("reconciliation.entry-unavailable", Codes(again));

        var notABankAccount = await RefusedAsync(() => e.Bank.GetReconciliationAsync(e.Id("511"), oct10));
        Assert.Contains("bank.account-invalid", Codes(notABankAccount));
    }

    [Fact]
    public async Task A_voucher_whose_entries_were_reconciled_cannot_be_changed_or_deleted_until_the_reconciliation_is_undone()
    {
        var (e, bank, _, rent, _, sales) = await SeedBankAsync();
        var oct10 = new DateOnly(2026, 10, 10);
        await e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(oct10, 8_750m, await EntryIdsAsync(e, bank, oct10), null));

        var edited = Payment(e, rent.Date, ("422", 1_100m)) with { CashAccountId = bank };
        Assert.Contains("voucher.reconciled", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(rent.Id, edited))));
        Assert.Contains("voucher.reconciled", Codes(await RefusedAsync(() => e.Vouchers.SaveDraftAsync(rent.Id, edited))));
        Assert.Contains("voucher.reconciled", Codes(await RefusedAsync(() => e.Vouchers.DeleteAsync(rent.Id))));
        Assert.Contains("move.has-reconciled", Codes(await RefusedAsync(() => e.Chart.MoveEntriesAsync(bank, e.Id("111"))))); // and neither can its account's entries be moved

        await e.Vouchers.DeleteAsync(sales.Id); // the sale was not reconciled, so it can go

        await e.Bank.UndoLastAsync(bank);
        await e.Vouchers.SaveAndPostAsync(rent.Id, edited);
        Assert.Equal(8_650m, await BalanceAsync(e, "112")); // 10,000 - 1,100 (the corrected rent) - 250; the 400 sale was deleted
    }

    [Fact]
    public async Task Undoing_the_last_reconciliation_makes_its_entries_open_again_and_says_so_when_there_is_none()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        Assert.Contains("reconciliation.none-to-undo", Codes(await RefusedAsync(() => e.Bank.UndoLastAsync(bank))));

        var oct10 = new DateOnly(2026, 10, 10);
        await e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(oct10, 8_750m, await EntryIdsAsync(e, bank, oct10), null));
        await e.Bank.UndoLastAsync(bank);

        Assert.Equal(3, (await EntryIdsAsync(e, bank, oct10)).Count);
        Assert.Empty(await e.Bank.HistoryAsync(bank));
        Assert.Equal(0m, (await e.Bank.ListAccountsAsync()).Single(a => a.AccountId == bank).ReconciledBalance);
    }

    // ------------------------------------------------------------------ Statement import

    private const string Statement = """
        Date,Description,Reference,Amount
        2026-10-01,Capital deposit,DEP1,"10,000.000"
        03/10/2026,Rent,CHQ7,-1000
        2026-10-09,Utilities (late posting),,-250.000
        """;

    [Fact]
    public async Task A_statement_is_imported_and_its_lines_are_matched_with_entries_of_the_same_amount_and_nearly_the_same_day()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();

        var result = await e.Bank.ImportStatementAsync(bank, "october.csv", Csv(Statement));

        Assert.Equal((3, 0, 0), (result.Imported, result.Skipped, result.Issues.Count));
        var view = await e.Bank.GetReconciliationAsync(bank, new DateOnly(2026, 10, 31));
        Assert.Equal([10_000m, -1_000m, -250m], view.StatementLines.Select(l => l.Amount));
        Assert.Equal(3, view.Suggestions.Count); // the utilities line is a day later than the entry, still a match
        Assert.Equal(view.Entries.Where(x => x.Amount != 400m).Select(x => x.EntryId).Order(), view.Suggestions.Select(s => s.EntryId).Order());
    }

    [Fact]
    public async Task Loading_the_same_statement_again_skips_lines_that_are_already_there()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        await e.Bank.ImportStatementAsync(bank, "october.csv", Csv(Statement));

        var again = await e.Bank.ImportStatementAsync(bank, "october.csv", Csv(Statement + "\n2026-10-20,Sales deposit,DEP2,400"));

        Assert.Equal((1, 3), (again.Imported, again.Skipped));
        Assert.Equal(4, (await e.Bank.GetReconciliationAsync(bank, Oct31)).StatementLines.Count);
    }

    [Fact]
    public async Task A_statement_with_debit_and_credit_columns_in_any_style_is_understood()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        // Semicolons, comma decimals, brackets, day-first dates and the bank's own wording: debit is money out, credit is money in.
        var text = "Transaction Date;Narration;Debit;Credit\n01/10/2026;Capital;;10.000,50\n03-10-2026;Rent;1.000,00;\n08.10.2026;Utilities;(250,00);";

        var result = await e.Bank.ImportStatementAsync(bank, "bank.csv", Csv(text));

        Assert.Equal(3, result.Imported);
        var amounts = (await e.Bank.GetReconciliationAsync(bank, Oct31)).StatementLines.Select(l => l.Amount);
        Assert.Equal([10_000.5m, -1_000m, -250m], amounts);
    }

    [Fact]
    public async Task A_statement_with_arabic_headers_and_arabic_digits_is_understood()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        var text = "التاريخ,البيان,مدين,دائن\n٢٠٢٦-١٠-٠١,إيداع رأس المال,,١٠٠٠٠\n٠٣/١٠/٢٠٢٦,إيجار,١٠٠٠,";

        var result = await e.Bank.ImportStatementAsync(bank, "بنك.csv", Csv(text));

        Assert.Equal(2, result.Imported);
        var view = await e.Bank.GetReconciliationAsync(bank, Oct31);
        Assert.Equal([10_000m, -1_000m], view.StatementLines.Select(l => l.Amount));
        Assert.Equal("إيجار", view.StatementLines[1].Description);
    }

    [Fact]
    public async Task One_bad_row_means_nothing_is_imported_and_the_row_is_named()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        var text = "Date,Description,Amount\n2026-10-01,Fine,100\nnot a date,Broken,50\n2026-10-03,Also broken,abc\n2026-10-04,Fine again,20";

        var result = await e.Bank.ImportStatementAsync(bank, "bad.csv", Csv(text));

        Assert.Equal(0, result.Imported);
        Assert.Equal([new ImportIssueView(3, "statement.date-invalid"), new ImportIssueView(4, "statement.amount-invalid")],
            result.Issues.Select(i => new ImportIssueView(i.Row, i.Code)));
        Assert.Empty((await e.Bank.GetReconciliationAsync(bank, Oct31)).StatementLines);
    }

    private sealed record ImportIssueView(int Row, string Code);

    [Fact]
    public async Task A_file_without_the_needed_columns_or_without_rows_is_explained()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();

        Assert.Equal("statement.date-column-missing", (await e.Bank.ImportStatementAsync(bank, "x.csv", Csv("Name,Value\na,1"))).Issues.Single().Code);
        Assert.Equal("statement.amount-column-missing", (await e.Bank.ImportStatementAsync(bank, "x.csv", Csv("Date,Description\n2026-10-01,x"))).Issues.Single().Code);
        Assert.Equal("statement.empty", (await e.Bank.ImportStatementAsync(bank, "x.csv", Csv("Date,Amount\n"))).Issues.Single().Code);
        Assert.Equal("import.unreadable", (await e.Bank.ImportStatementAsync(bank, "x.xlsx", [])).Issues.Single().Code);
    }

    [Fact]
    public async Task An_excel_statement_is_read_with_real_dates_and_numbers()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        using var stream = new MemoryStream();
        using (var workbook = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Statement");
            sheet.Cell(1, 1).Value = "Date";
            sheet.Cell(1, 2).Value = "Description";
            sheet.Cell(1, 3).Value = "Amount";
            sheet.Cell(2, 1).Value = new DateTime(2026, 10, 1);
            sheet.Cell(2, 2).Value = "Capital deposit";
            sheet.Cell(2, 3).Value = 10_000.25;
            sheet.Cell(3, 1).Value = new DateTime(2026, 10, 3);
            sheet.Cell(3, 2).Value = "Rent";
            sheet.Cell(3, 3).Value = -1_000;
            workbook.SaveAs(stream);
        }

        var result = await e.Bank.ImportStatementAsync(bank, "statement.xlsx", stream.ToArray());

        Assert.Equal(2, result.Imported);
        var lines = (await e.Bank.GetReconciliationAsync(bank, Oct31)).StatementLines;
        Assert.Equal([(new DateOnly(2026, 10, 1), 10_000.25m), (new DateOnly(2026, 10, 3), -1_000m)], lines.Select(l => (l.Date, l.Amount)));
    }

    [Fact]
    public async Task Statement_lines_that_were_used_stay_and_the_others_can_be_cleared()
    {
        var (e, bank, _, _, _, _) = await SeedBankAsync();
        await e.Bank.ImportStatementAsync(bank, "october.csv", Csv(Statement));
        var oct10 = new DateOnly(2026, 10, 10);
        var view = await e.Bank.GetReconciliationAsync(bank, oct10);
        var capitalLine = view.StatementLines.Single(l => l.Amount == 10_000m);
        var capitalEntry = view.Suggestions.Single(s => s.StatementLineId == capitalLine.Id).EntryId;

        // Only the capital deposit is reconciled (the bank's opening position here is 10,000).
        await e.Bank.CompleteAsync(bank, new CompleteReconciliationInput(oct10, 10_000m, [capitalEntry], [capitalLine.Id]));

        var removed = await e.Bank.ClearStatementAsync(bank);
        Assert.Equal(2, removed); // the rent and utilities lines were never used
        var left = (await e.Bank.GetReconciliationAsync(bank, oct10)).StatementLines;
        Assert.Equal((10_000m, true), (left.Single().Amount, left.Single().Reconciled));
    }

    // ------------------------------------------------------------------ Opening balances and the closing entry

    [Fact]
    public async Task There_is_one_opening_balances_voucher_and_it_takes_balance_sheet_accounts_only()
    {
        var e = await NewEnvAsync();
        var opening = new VoucherInput(
            VoucherKind.Opening, new DateOnly(2025, 12, 31), null, null, "opening",
            [new VoucherLineInput(null, e.Id("112"), null, 5_000m, 0), new VoucherLineInput(null, e.Id("31"), null, 0, 5_000m)]);

        var posted = await e.Vouchers.SaveAndPostAsync(null, opening);
        Assert.Equal("OB-2025-0001", posted.Number);
        Assert.Equal(5_000m, await BalanceAsync(e, "112"));

        Assert.Contains("opening.already-exists", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, opening))));
        var edited = await e.Vouchers.SaveAndPostAsync(posted.Id, opening with { Lines = [opening.Lines[0] with { Debit = 6_000m }, opening.Lines[1] with { Credit = 6_000m }] });
        Assert.Equal(posted.Id, edited.Id); // the existing one can still be corrected
        Assert.Equal(6_000m, await BalanceAsync(e, "112"));

        var withExpense = opening with { Lines = [new VoucherLineInput(null, e.Id("422"), null, 10m, 0), new VoucherLineInput(null, e.Id("31"), null, 0, 10m)] };
        await e.Vouchers.DeleteAsync(posted.Id);
        Assert.Contains("line.account-not-balance-sheet", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, withExpense))));
    }

    [Fact]
    public async Task A_closing_entry_cannot_be_made_or_deleted_by_hand()
    {
        var e = await NewEnvAsync();
        var closing = new VoucherInput(
            VoucherKind.Closing, new DateOnly(2026, 12, 31), null, null, null,
            [new VoucherLineInput(null, e.Id("112"), null, 5m, 0), new VoucherLineInput(null, e.Id("31"), null, 0, 5m)]);

        Assert.Contains("voucher.system-generated", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, closing))));
        Assert.Contains("voucher.system-generated", Codes(await RefusedAsync(() => e.Vouchers.SaveDraftAsync(null, closing))));
    }
}
