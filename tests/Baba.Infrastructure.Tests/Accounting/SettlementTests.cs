using Baba.Application.Accounting;
using Baba.Application.Trade;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Receipts and payments set against specific invoices, and the exchange gain or loss when the rate moved (brief section 10.3).</summary>
public class SettlementTests : AccountingFixture
{
    private static DocumentInput Invoice(Env e, DocumentKind kind, DateOnly date, decimal price, string? currency = null, decimal? rate = null) => new(
        kind, date, null, kind.IsSales() ? e.Customer : e.Supplier, currency, rate, null, null, 0,
        [new DocumentLineInput(null, null, e.Id(kind.IsSales() ? "511" : "422"), "item", 1, price)]);

    private static SettlementInput Receive(Env e, DateOnly date, decimal? rate, string? currency, params (Guid Document, decimal Amount)[] parts) => new(
        e.Customer, date, e.Id("112"), currency, rate, null, null, parts.Select(p => new SettlementLineInput(p.Document, p.Amount)).ToList(), 0);

    private static SettlementInput Pay(Env e, DateOnly date, decimal? rate, string? currency, params (Guid Document, decimal Amount)[] parts) => new(
        e.Supplier, date, e.Id("112"), currency, rate, null, null, parts.Select(p => new SettlementLineInput(p.Document, p.Amount)).ToList(), 0);

    private static async Task<decimal> OutstandingOfAsync(Env e, Guid invoice) =>
        (await e.Settlements.OutstandingAsync(null)).Single(o => o.DocumentId == invoice).Outstanding;

    // ------------------------------------------------------------------ In the company's currency

    [Fact]
    public async Task A_receipt_pays_one_invoice_in_full_and_the_books_balance_out()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, 250m));

        var result = await e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (invoice.Id, 250m)));

        Assert.Null(result.SettlementVoucherId);
        Assert.Equal(0m, result.ExchangeGain);
        Assert.StartsWith("RV-2026-", result.Payment.Number);
        Assert.Equal(0m, await OutstandingOfAsync(e, invoice.Id));
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.Equal(250m, await BalanceAsync(e, "112"));
    }

    [Fact]
    public async Task A_part_payment_leaves_the_rest_outstanding_and_a_second_one_clears_it()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, 300m));

        await e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (invoice.Id, 100m)));
        Assert.Equal(200m, await OutstandingOfAsync(e, invoice.Id));

        await e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (invoice.Id, 200m)));
        Assert.Equal(0m, await OutstandingOfAsync(e, invoice.Id));

        var tooMuch = await RefusedAsync(() => e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (invoice.Id, 1m))));
        Assert.Contains("settlement.document-unavailable", Codes(tooMuch));
    }

    [Fact]
    public async Task A_payment_pays_the_invoice_it_was_set_against_and_not_the_oldest_one()
    {
        var e = await NewEnvAsync();
        var old = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, new DateOnly(2026, 8, 1), 100m));
        var recent = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, new DateOnly(2026, 10, 5), 200m));

        await e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (recent.Id, 200m)));
        var aging = await e.Reports.AgingAsync(PartyKind.Customer, Oct31);

        // The August invoice (61 days past its due date) is still owed in full; first-in-first-out would have paid it instead.
        var totals = aging.Rows.Last().Cells;
        Assert.Equal(0m, totals[2].Amount);   // not due
        Assert.Equal(100m, totals[5].Amount); // 61-90 days
        Assert.Equal(100m, totals[7].Amount);
    }

    [Fact]
    public async Task Allocations_are_checked_before_anything_is_posted()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, 100m));
        var supplierBill = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.PurchaseInvoice, Oct6, 50m));

        var tooHigh = await RefusedAsync(() => e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (invoice.Id, 100.001m))));
        var wrongParty = await RefusedAsync(() => e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (supplierBill.Id, 50m))));
        var nothing = await RefusedAsync(() => e.Settlements.SettleAsync(Receive(e, Oct6, null, null)));
        var zero = await RefusedAsync(() => e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (invoice.Id, 0m))));

        Assert.Contains("settlement.amount-too-high", Codes(tooHigh));
        Assert.Contains("settlement.document-unavailable", Codes(wrongParty));
        Assert.Contains("settlement.nothing-to-settle", Codes(nothing));
        Assert.Contains("settlement.amount-invalid", Codes(zero));
        Assert.Equal(100m, await BalanceAsync(e, "113")); // still just the invoice
    }

    [Fact]
    public async Task A_credit_note_reduces_what_is_outstanding()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, 400m));
        var note = await e.Trade.ConvertAsync(invoice.Id, DocumentKind.SalesCreditNote);
        await e.Trade.IssueSavedAsync(note.Id);

        Assert.Equal(0m, await OutstandingOfAsync(e, invoice.Id));
    }

    // ------------------------------------------------------------------ Two currencies

    [Fact]
    public async Task A_dollar_receipt_at_a_better_rate_books_an_exchange_gain()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct1, 1000m, "USD", 0.30m)); // 300 dinars owed

        var result = await e.Settlements.SettleAsync(Receive(e, Oct6, 0.32m, null, (invoice.Id, 1000m)));              // 320 dinars received

        Assert.Equal(20m, result.ExchangeGain);
        Assert.NotNull(result.SettlementVoucherId);
        Assert.Equal(0m, await BalanceAsync(e, "113"));   // 300 owed, 320 received, 20 gain taken to the exchange account
        Assert.Equal(320m, await BalanceAsync(e, "112"));
        Assert.Equal(-20m, await BalanceAsync(e, "428")); // a credit: income
        Assert.Equal(0m, await OutstandingOfAsync(e, invoice.Id));
        Assert.Equal(0m, (await e.Reports.AgingAsync(PartyKind.Customer, Oct31)).Rows.Last().Cells[7].Amount);
    }

    [Fact]
    public async Task A_dollar_receipt_at_a_worse_rate_books_an_exchange_loss()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct1, 1000m, "USD", 0.30m));

        var result = await e.Settlements.SettleAsync(Receive(e, Oct6, 0.28m, null, (invoice.Id, 1000m)));

        Assert.Equal(-20m, result.ExchangeGain);
        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.Equal(20m, await BalanceAsync(e, "428")); // a debit: expense
        Assert.Equal(280m, await BalanceAsync(e, "112"));
    }

    [Fact]
    public async Task Paying_a_supplier_after_the_rate_rose_is_an_exchange_loss()
    {
        var e = await NewEnvAsync();
        var bill = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.PurchaseInvoice, Oct1, 1000m, "USD", 0.30m)); // we owe 300 dinars

        var result = await e.Settlements.SettleAsync(Pay(e, Oct6, 0.32m, null, (bill.Id, 1000m)));                       // it costs 320

        Assert.Equal(-20m, result.ExchangeGain);
        Assert.StartsWith("PV-2026-", result.Payment.Number);
        Assert.Equal(0m, await BalanceAsync(e, "211"));
        Assert.Equal(20m, await BalanceAsync(e, "428"));
        Assert.Equal(-320m, await BalanceAsync(e, "112"));
    }

    [Fact]
    public async Task Two_part_payments_at_different_rates_add_their_differences_and_leave_nothing_behind()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct1, 1000m, "USD", 0.30m));

        var first = await e.Settlements.SettleAsync(Receive(e, Oct6, 0.32m, null, (invoice.Id, 400m))); // 128 received for 120 owed: +8
        Assert.Equal(8m, first.ExchangeGain);
        Assert.Equal(600m, await OutstandingOfAsync(e, invoice.Id));

        var second = await e.Settlements.SettleAsync(Receive(e, Oct6, 0.31m, null, (invoice.Id, 600m))); // 186 received for 180 owed: +6
        Assert.Equal(6m, second.ExchangeGain);

        Assert.Equal(0m, await BalanceAsync(e, "113"));
        Assert.Equal(-14m, await BalanceAsync(e, "428"));
        Assert.Equal(0m, await OutstandingOfAsync(e, invoice.Id));
    }

    [Fact]
    public async Task The_payment_rate_defaults_to_the_table_and_the_currency_must_match_the_invoice()
    {
        var e = await NewEnvAsync();
        await e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, 0.31m));
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct1, 100m, "USD", 0.30m));

        var wrong = await RefusedAsync(() => e.Settlements.SettleAsync(Receive(e, Oct6, null, "EUR", (invoice.Id, 100m))));
        Assert.Contains("settlement.currency-mismatch", Codes(wrong));

        var result = await e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (invoice.Id, 100m)));

        Assert.Equal("USD", result.Payment.CurrencyCode);
        Assert.Equal(0.31m, result.Payment.ExchangeRate);
        Assert.Equal(1m, result.ExchangeGain); // 31 received for 30 owed
    }

    [Fact]
    public async Task Without_an_exchange_differences_account_the_payment_is_refused_and_nothing_is_left_behind()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct1, 1000m, "USD", 0.30m));
        await e.Chart.SetActiveAsync(e.Id("428"), false);

        var refused = await RefusedAsync(() => e.Settlements.SettleAsync(Receive(e, Oct6, 0.32m, null, (invoice.Id, 1000m))));

        Assert.Contains("fx.account-required", Codes(refused));
        Assert.Equal(300m, await BalanceAsync(e, "113"));
        Assert.Equal(0m, await BalanceAsync(e, "112"));
    }

    // ------------------------------------------------------------------ Undoing and protecting

    [Fact]
    public async Task Deleting_the_receipt_takes_the_allocation_and_the_exchange_difference_with_it()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct1, 1000m, "USD", 0.30m));
        var result = await e.Settlements.SettleAsync(Receive(e, Oct6, 0.32m, null, (invoice.Id, 1000m)));

        await e.Vouchers.DeleteAsync(result.Payment.Id);

        Assert.Equal(1000m, await OutstandingOfAsync(e, invoice.Id));
        Assert.Equal(300m, await BalanceAsync(e, "113"));
        Assert.Equal(0m, await BalanceAsync(e, "428"));
        Assert.Null(await e.Vouchers.GetAsync(result.SettlementVoucherId!.Value));
    }

    [Fact]
    public async Task A_paid_invoice_and_its_receipt_cannot_be_edited_and_the_invoice_cannot_be_deleted()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, 100m));
        var result = await e.Settlements.SettleAsync(Receive(e, Oct6, null, null, (invoice.Id, 40m)));

        var edit = await RefusedAsync(() => e.Trade.IssueAsync(invoice.Id, Invoice(e, DocumentKind.SalesInvoice, Oct6, 120m)));
        var delete = await RefusedAsync(() => e.Trade.DeleteAsync(invoice.Id));
        var changeReceipt = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(result.Payment.Id, new VoucherInput(
            VoucherKind.Receipt, Oct6, e.Id("112"), null, null, [new VoucherLineInput(null, e.Id("113"), null, 0, 50m, e.Customer)])));

        Assert.Contains("document.has-payments", Codes(edit));
        Assert.Contains("document.has-payments", Codes(delete));
        Assert.Contains("voucher.allocated", Codes(changeReceipt));
    }

    [Fact]
    public async Task An_invoice_that_has_a_credit_note_cannot_be_changed()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct6, 100m));
        await e.Trade.IssueSavedAsync((await e.Trade.ConvertAsync(invoice.Id, DocumentKind.SalesCreditNote)).Id);

        var refused = await RefusedAsync(() => e.Trade.DeleteAsync(invoice.Id));

        Assert.Contains("document.has-notes", Codes(refused));
    }

    [Fact]
    public async Task The_exchange_difference_voucher_cannot_be_changed_by_hand()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct1, 1000m, "USD", 0.30m));
        var result = await e.Settlements.SettleAsync(Receive(e, Oct6, 0.32m, null, (invoice.Id, 1000m)));

        var refused = await RefusedAsync(() => e.Vouchers.DeleteAsync(result.SettlementVoucherId!.Value));

        Assert.Contains("voucher.system-generated", Codes(refused));
    }

    [Fact]
    public async Task A_credit_note_of_a_foreign_invoice_uses_the_invoice_rate()
    {
        var e = await NewEnvAsync();
        await e.Rates.SetAsync(new CurrencyRateInput("USD", Oct1, 0.35m)); // today's rate differs from the invoice's
        var invoice = await e.Trade.IssueAsync(null, Invoice(e, DocumentKind.SalesInvoice, Oct1, 1000m, "USD", 0.30m));

        var note = await e.Trade.ConvertAsync(invoice.Id, DocumentKind.SalesCreditNote);
        await e.Trade.IssueSavedAsync(note.Id);

        Assert.Equal(0.30m, note.ExchangeRate);
        Assert.Equal(0m, await BalanceAsync(e, "113"));
    }
}
