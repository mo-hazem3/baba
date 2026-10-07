using Baba.Domain.Accounting;

namespace Baba.Domain.Tests.Accounting;

public class PostingEngineTests
{
    private static readonly Currency Base = new("TRI", 3);      // a three-decimal base currency
    private static readonly Currency Foreign = new("TWO", 2);   // a two-decimal foreign currency
    private static readonly Guid CompanyId = Guid.NewGuid();

    private readonly Account _cash = Posting("1110", AccountType.Asset, AccountRole.CashOrBank);
    private readonly Account _bank = Posting("1120", AccountType.Asset, AccountRole.CashOrBank);
    private readonly Account _rent = Posting("4210", AccountType.Expense);
    private readonly Account _utilities = Posting("4220", AccountType.Expense);
    private readonly Account _sales = Posting("5110", AccountType.Revenue);
    private readonly Account _group = new() { Code = "42", Type = AccountType.Expense, IsPosting = false };
    private readonly Account _closed = Posting("4290", AccountType.Expense, active: false);
    private readonly Account _receivable = Posting("1130", AccountType.Asset, AccountRole.Receivable);
    private readonly Party _customer = new() { CompanyId = CompanyId, Kind = PartyKind.Customer, Code = "C1", NameEn = "Customer" };
    private readonly Party _dormant = new() { CompanyId = CompanyId, Kind = PartyKind.Customer, Code = "C2", NameEn = "Dormant", IsActive = false };
    private readonly CostCenter _project = new() { CompanyId = CompanyId, Code = "P1", NameEn = "Project" };
    private readonly CostCenter _closedProject = new() { CompanyId = CompanyId, Code = "P2", NameEn = "Old project", IsActive = false };

    private static Account Posting(string code, AccountType type, AccountRole role = AccountRole.None, bool active = true) =>
        new() { CompanyId = CompanyId, Code = code, NameEn = code, Type = type, IsPosting = true, IsActive = active, Role = role };

    private PostingContext Context(Currency? voucherCurrency = null, Func<DateOnly, bool>? isOpen = null) =>
        new(new[] { _cash, _bank, _rent, _utilities, _sales, _group, _closed, _receivable }.ToDictionary(a => a.Id),
            Base, voucherCurrency ?? Base, isOpen ?? (_ => true),
            new[] { _customer, _dormant }.ToDictionary(p => p.Id),
            new[] { _project, _closedProject }.ToDictionary(c => c.Id));

    private Voucher NewVoucher(VoucherKind kind, params (Account Account, decimal Debit, decimal Credit)[] lines) => new()
    {
        CompanyId = CompanyId,
        Kind = kind,
        Date = new DateOnly(2026, 10, 6),
        CurrencyCode = Base.Code,
        CashAccountId = kind == VoucherKind.Journal ? null : _cash.Id,
        Memo = "memo",
        Lines = lines.Select((l, i) => new VoucherLine
        {
            CompanyId = CompanyId,
            LineNumber = i + 1,
            AccountId = l.Account.Id,
            // The customer or supplier that a receivable or payable line has to name.
            PartyId = l.Account.Role is AccountRole.Receivable or AccountRole.Payable ? _customer.Id : null,
            Debit = l.Debit,
            Credit = l.Credit,
        }).ToList(),
    };

    private static IEnumerable<string> Codes(IEnumerable<PostingIssue> issues) => issues.Select(i => i.Code);

    // ---- The three documents and their ledger entries (brief section 3) ----

    [Fact]
    public void A_payment_debits_each_line_and_credits_the_bank_or_cash_account_for_the_total()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 300.5m, 0), (_utilities, 49.25m, 0));
        var context = Context();

        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));
        var entries = PostingEngine.GenerateEntries(voucher, context);

        Assert.Equal(3, entries.Count);
        Assert.Equal((_rent.Id, 300.5m, 0m), (entries[0].AccountId, entries[0].BaseDebit, entries[0].BaseCredit));
        Assert.Equal((_utilities.Id, 49.25m, 0m), (entries[1].AccountId, entries[1].BaseDebit, entries[1].BaseCredit));
        Assert.Equal((_cash.Id, 0m, 349.75m), (entries[2].AccountId, entries[2].BaseDebit, entries[2].BaseCredit));
    }

    [Fact]
    public void A_receipt_debits_the_bank_or_cash_account_and_credits_each_line()
    {
        var voucher = NewVoucher(VoucherKind.Receipt, (_sales, 0, 1000m), (_receivable, 0, 250.125m));
        var context = Context();

        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));
        var entries = PostingEngine.GenerateEntries(voucher, context);

        Assert.Equal(3, entries.Count);
        Assert.Equal((_sales.Id, 0m, 1000m), (entries[0].AccountId, entries[0].BaseDebit, entries[0].BaseCredit));
        Assert.Equal((_receivable.Id, 0m, 250.125m), (entries[1].AccountId, entries[1].BaseDebit, entries[1].BaseCredit));
        Assert.Equal((_cash.Id, 1250.125m, 0m), (entries[2].AccountId, entries[2].BaseDebit, entries[2].BaseCredit));
    }

    [Fact]
    public void A_journal_posts_its_lines_as_written()
    {
        var voucher = NewVoucher(VoucherKind.Journal, (_rent, 80m, 0), (_utilities, 20m, 0), (_bank, 0, 100m));
        var context = Context();

        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));
        var entries = PostingEngine.GenerateEntries(voucher, context);

        Assert.Equal(3, entries.Count);
        Assert.Equal(100m, entries.Sum(e => e.BaseDebit));
        Assert.Equal(100m, entries.Sum(e => e.BaseCredit));
    }

    [Theory]
    [InlineData(VoucherKind.Payment)]
    [InlineData(VoucherKind.Receipt)]
    [InlineData(VoucherKind.Journal)]
    public void Every_voucher_makes_entries_that_balance_to_the_last_fils(VoucherKind kind)
    {
        var lines = kind switch
        {
            VoucherKind.Payment => new[] { (_rent, 0.001m, 0m), (_utilities, 333.333m, 0m), (_rent, 12.5m, 0m) },
            VoucherKind.Receipt => new[] { (_sales, 0m, 0.001m), (_sales, 0m, 333.333m), (_receivable, 0m, 12.5m) },
            _ => new[] { (_rent, 345.834m, 0m), (_sales, 0m, 345.834m) },
        };
        var voucher = NewVoucher(kind, lines);
        var context = Context();
        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));

        var entries = PostingEngine.GenerateEntries(voucher, context);

        Assert.Equal(entries.Sum(e => e.BaseDebitScaled), entries.Sum(e => e.BaseCreditScaled));
        Assert.Equal(entries.Sum(e => e.DebitScaled), entries.Sum(e => e.CreditScaled));
    }

    [Fact]
    public void Entries_carry_the_voucher_company_date_currency_and_description()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 10m, 0));
        voucher.Lines[0].Description = "October rent";

        var entries = PostingEngine.GenerateEntries(voucher, Context());

        Assert.All(entries, e =>
        {
            Assert.Equal(voucher.Id, e.VoucherId);
            Assert.Equal(CompanyId, e.CompanyId);
            Assert.Equal(new DateOnly(2026, 10, 6), e.Date);
            Assert.Equal("TRI", e.CurrencyCode);
        });
        Assert.Equal("October rent", entries[0].Description); // the line's own text
        Assert.Equal("memo", entries[1].Description);          // the bank side uses the voucher memo
    }

    // ---- Foreign currency (in the data model from day one; the screens come later) ----

    [Fact]
    public void A_foreign_currency_voucher_stores_both_amounts_and_converts_at_its_rate()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 100m, 0), (_utilities, 50.5m, 0));
        voucher.CurrencyCode = "TWO";
        voucher.ExchangeRate = 0.3075m; // 1 TWO = 0.3075 base
        var context = Context(Foreign);
        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));

        var entries = PostingEngine.GenerateEntries(voucher, context);

        Assert.Equal(100m, entries[0].Debit);
        Assert.Equal(30.75m, entries[0].BaseDebit);        // 100 x 0.3075
        Assert.Equal(15.529m, entries[1].BaseDebit);       // 50.5 x 0.3075 = 15.52875 -> 15.529 (3 decimals)
        Assert.Equal(150.5m, entries[2].Credit);           // the bank side in the transaction currency
        Assert.Equal(46.279m, entries[2].BaseCredit);      // the SUM of the rounded line amounts, not a separate rounding
        Assert.Equal(entries.Sum(e => e.BaseDebitScaled), entries.Sum(e => e.BaseCreditScaled));
        Assert.Equal(0.3075m, FxRate.ToDecimal(entries[0].FxRateScaled));
    }

    [Fact]
    public void A_journal_that_balances_in_the_foreign_currency_but_not_after_rounding_is_refused()
    {
        // Debits 0.05 + 0.05 and one credit 0.10 balance in the foreign currency. At a rate of 0.0105, in 3 decimals:
        // each debit is 0.000525 -> 0.001 (together 0.002) while the credit is 0.00105 -> 0.001. They no longer match.
        var voucher = NewVoucher(VoucherKind.Journal, (_rent, 0.05m, 0), (_utilities, 0.05m, 0), (_sales, 0, 0.10m));
        voucher.CurrencyCode = "TWO";
        voucher.ExchangeRate = 0.0105m;

        var codes = Codes(PostingEngine.ValidateForPosting(voucher, Context(Foreign)));

        Assert.Contains("balance.unbalanced-base", codes);
        Assert.DoesNotContain("balance.unbalanced", codes); // it does balance in the foreign currency itself
    }

    [Fact]
    public void A_journal_that_still_balances_after_conversion_can_be_posted()
    {
        var voucher = NewVoucher(VoucherKind.Journal, (_rent, 100m, 0), (_sales, 0, 100m));
        voucher.CurrencyCode = "TWO";
        voucher.ExchangeRate = 0.3075m;

        Assert.Empty(PostingEngine.ValidateForPosting(voucher, Context(Foreign)));
    }

    [Fact]
    public void The_base_currency_must_use_a_rate_of_one_and_every_rate_must_be_positive()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 1m, 0));
        voucher.ExchangeRate = 1.1m;
        Assert.Contains("exchange-rate.base-must-be-one", Codes(PostingEngine.ValidateDraft(voucher, Context())));

        voucher.CurrencyCode = "TWO";
        voucher.ExchangeRateScaled = 0;
        Assert.Contains("exchange-rate.invalid", Codes(PostingEngine.ValidateDraft(voucher, Context(Foreign))));

        voucher.CurrencyCode = "";
        Assert.Contains("currency.required", Codes(PostingEngine.ValidateDraft(voucher, Context())));
    }

    // ---- What a draft needs: at least one complete line ----

    [Fact]
    public void A_voucher_with_no_lines_cannot_even_be_saved()
    {
        var voucher = NewVoucher(VoucherKind.Payment);

        Assert.Contains(new PostingIssue("lines", "lines.required"), PostingEngine.ValidateDraft(voucher, Context()));
    }

    [Fact]
    public void An_incomplete_line_is_reported_with_its_row_so_the_screen_can_say_which_one()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 10m, 0), (_utilities, 0, 0));
        voucher.Lines[0].AccountId = Guid.Empty;

        var issues = PostingEngine.ValidateDraft(voucher, Context());

        Assert.Contains(new PostingIssue("lines[0].account", "line.account-required"), issues);
        Assert.Contains(new PostingIssue("lines[1].amount", "line.amount-required"), issues);
    }

    [Fact]
    public void An_unknown_account_is_reported()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 10m, 0));
        voucher.Lines[0].AccountId = Guid.NewGuid();

        Assert.Contains(new PostingIssue("lines[0].account", "line.account-unknown"), PostingEngine.ValidateDraft(voucher, Context()));
    }

    [Theory]
    [InlineData(VoucherKind.Payment, 0, 10, "line.amount-wrong-side")]   // a payment line is a debit
    [InlineData(VoucherKind.Receipt, 10, 0, "line.amount-wrong-side")]   // a receipt line is a credit
    [InlineData(VoucherKind.Journal, 10, 10, "line.amount-both-sides")]  // a journal line is one side or the other
    [InlineData(VoucherKind.Journal, -5, 0, "line.amount-negative")]
    [InlineData(VoucherKind.Payment, 0, 0, "line.amount-required")]
    public void Amounts_must_be_on_the_right_side_and_positive(VoucherKind kind, double debit, double credit, string code)
    {
        var voucher = NewVoucher(kind, (_rent, (decimal)debit, (decimal)credit));

        Assert.Contains(code, Codes(PostingEngine.ValidateDraft(voucher, Context())));
    }

    [Fact]
    public void Amounts_cannot_have_more_decimals_than_the_currency_allows()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 10.0005m, 0)); // base has 3 decimals
        Assert.Contains("line.amount-decimals", Codes(PostingEngine.ValidateDraft(voucher, Context())));

        var foreign = NewVoucher(VoucherKind.Payment, (_rent, 10.005m, 0)); // fine in 3 decimals, not in 2
        foreign.CurrencyCode = "TWO";
        Assert.Contains("line.amount-decimals", Codes(PostingEngine.ValidateDraft(foreign, Context(Foreign))));
        Assert.DoesNotContain("line.amount-decimals", Codes(PostingEngine.ValidateDraft(NewVoucher(VoucherKind.Payment, (_rent, 10.005m, 0)), Context())));
    }

    [Fact]
    public void A_draft_may_leave_the_bank_account_and_group_accounts_for_later()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_group, 10m, 0));
        voucher.CashAccountId = null;

        Assert.Empty(PostingEngine.ValidateDraft(voucher, Context()));
    }

    // ---- What posting needs on top ----

    [Fact]
    public void An_unbalanced_journal_cannot_be_posted()
    {
        var voucher = NewVoucher(VoucherKind.Journal, (_rent, 100m, 0), (_bank, 0, 99.999m));

        Assert.Contains(new PostingIssue("balance", "balance.unbalanced"), PostingEngine.ValidateForPosting(voucher, Context()));
    }

    [Fact]
    public void A_date_in_a_locked_period_cannot_be_posted()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 10m, 0));
        var context = Context(isOpen: d => d.Month != 10);

        Assert.Contains(new PostingIssue("date", "date.locked-period"), PostingEngine.ValidateForPosting(voucher, context));
        voucher.Date = new DateOnly(2026, 11, 1);
        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));
    }

    [Fact]
    public void Posting_needs_posting_active_accounts()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_group, 10m, 0), (_closed, 10m, 0));

        var issues = PostingEngine.ValidateForPosting(voucher, Context());

        Assert.Contains(new PostingIssue("lines[0].account", "line.account-not-posting"), issues);
        Assert.Contains(new PostingIssue("lines[1].account", "line.account-inactive"), issues);
    }

    [Fact]
    public void Payments_and_receipts_need_a_real_bank_or_cash_account()
    {
        var payment = NewVoucher(VoucherKind.Payment, (_rent, 10m, 0));

        payment.CashAccountId = null;
        Assert.Contains(new PostingIssue("cashAccount", "cash-account.required"), PostingEngine.ValidateForPosting(payment, Context()));

        payment.CashAccountId = _rent.Id; // an expense account is not a bank account
        Assert.Contains(new PostingIssue("cashAccount", "cash-account.invalid"), PostingEngine.ValidateForPosting(payment, Context()));

        payment.CashAccountId = Guid.NewGuid();
        Assert.Contains(new PostingIssue("cashAccount", "cash-account.invalid"), PostingEngine.ValidateForPosting(payment, Context()));

        payment.CashAccountId = _bank.Id;
        Assert.Empty(PostingEngine.ValidateForPosting(payment, Context()));
    }

    [Fact]
    public void A_journal_does_not_need_a_bank_account()
    {
        var voucher = NewVoucher(VoucherKind.Journal, (_rent, 5m, 0), (_sales, 0, 5m));

        Assert.Empty(PostingEngine.ValidateForPosting(voucher, Context()));
    }

    // ---- Customers, suppliers and cost centers (brief section 10.2) ----

    [Fact]
    public void A_line_on_a_receivable_account_must_name_the_customer_and_the_tag_travels_to_the_ledger()
    {
        var voucher = NewVoucher(VoucherKind.Receipt, (_receivable, 0, 80m));
        voucher.Lines[0].PartyId = null;
        Assert.Contains(new PostingIssue("lines[0].party", "line.party-required"), PostingEngine.ValidateForPosting(voucher, Context()));

        voucher.Lines[0].PartyId = _customer.Id;
        var context = Context();
        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));

        var entries = PostingEngine.GenerateEntries(voucher, context);
        Assert.Equal(_customer.Id, entries[0].PartyId);
        Assert.Null(entries[1].PartyId); // the bank side belongs to nobody
    }

    [Fact]
    public void A_draft_may_leave_the_customer_out_but_posting_may_not()
    {
        var voucher = NewVoucher(VoucherKind.Receipt, (_receivable, 0, 80m));
        voucher.Lines[0].PartyId = null;

        Assert.Empty(PostingEngine.ValidateDraft(voucher, Context()));
        Assert.Contains("line.party-required", Codes(PostingEngine.ValidateForPosting(voucher, Context())));
    }

    [Fact]
    public void Only_receivable_and_payable_accounts_take_a_customer_or_supplier()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 20m, 0));
        voucher.Lines[0].PartyId = _customer.Id;

        Assert.Contains(new PostingIssue("lines[0].party", "line.party-not-allowed"), PostingEngine.ValidateForPosting(voucher, Context()));
    }

    [Fact]
    public void An_unknown_or_switched_off_customer_is_refused()
    {
        var voucher = NewVoucher(VoucherKind.Receipt, (_receivable, 0, 80m));

        voucher.Lines[0].PartyId = Guid.NewGuid();
        Assert.Contains("line.party-unknown", Codes(PostingEngine.ValidateForPosting(voucher, Context())));

        voucher.Lines[0].PartyId = _dormant.Id;
        Assert.Contains("line.party-inactive", Codes(PostingEngine.ValidateForPosting(voucher, Context())));
    }

    [Fact]
    public void A_cost_center_is_optional_on_any_line_but_must_exist_and_be_active()
    {
        var voucher = NewVoucher(VoucherKind.Payment, (_rent, 20m, 0));
        Assert.Empty(PostingEngine.ValidateForPosting(voucher, Context()));

        voucher.Lines[0].CostCenterId = _project.Id;
        var context = Context();
        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));
        Assert.Equal(_project.Id, PostingEngine.GenerateEntries(voucher, context)[0].CostCenterId);

        voucher.Lines[0].CostCenterId = _closedProject.Id;
        Assert.Contains("line.cost-center-inactive", Codes(PostingEngine.ValidateForPosting(voucher, Context())));

        voucher.Lines[0].CostCenterId = Guid.NewGuid();
        Assert.Contains("line.cost-center-unknown", Codes(PostingEngine.ValidateForPosting(voucher, Context())));
    }

    // ---- Transfers, opening balances and the year-end entry ----

    [Fact]
    public void A_transfer_debits_the_account_it_goes_to_and_credits_the_account_it_comes_from()
    {
        var voucher = NewVoucher(VoucherKind.Transfer, (_bank, 400m, 0));
        var context = Context();

        Assert.Empty(PostingEngine.ValidateForPosting(voucher, context));
        var entries = PostingEngine.GenerateEntries(voucher, context);

        Assert.Equal(2, entries.Count);
        Assert.Equal((_bank.Id, 400m, 0m), (entries[0].AccountId, entries[0].BaseDebit, entries[0].BaseCredit));
        Assert.Equal((_cash.Id, 0m, 400m), (entries[1].AccountId, entries[1].BaseDebit, entries[1].BaseCredit));
    }

    [Fact]
    public void A_transfer_must_go_to_a_different_bank_or_cash_account_and_has_one_line()
    {
        var sameAccount = NewVoucher(VoucherKind.Transfer, (_cash, 400m, 0));
        Assert.Contains(new PostingIssue("lines[0].account", "line.account-same-as-source"), PostingEngine.ValidateForPosting(sameAccount, Context()));

        var toAnExpense = NewVoucher(VoucherKind.Transfer, (_rent, 400m, 0));
        Assert.Contains(new PostingIssue("lines[0].account", "line.account-not-cash"), PostingEngine.ValidateForPosting(toAnExpense, Context()));

        var twoLines = NewVoucher(VoucherKind.Transfer, (_bank, 100m, 0), (_bank, 50m, 0));
        Assert.Contains("transfer.one-line-only", Codes(PostingEngine.ValidateForPosting(twoLines, Context())));

        var withoutSource = NewVoucher(VoucherKind.Transfer, (_bank, 100m, 0));
        withoutSource.CashAccountId = null;
        Assert.Contains("cash-account.required", Codes(PostingEngine.ValidateForPosting(withoutSource, Context())));

        var credit = NewVoucher(VoucherKind.Transfer, (_bank, 0, 100m));
        Assert.Contains("line.amount-wrong-side", Codes(PostingEngine.ValidateForPosting(credit, Context())));
    }

    [Fact]
    public void Opening_balances_balance_like_a_journal_but_only_on_balance_sheet_accounts()
    {
        var capital = Posting("3100", AccountType.Equity);
        var context = new PostingContext(
            new[] { _cash, _bank, _rent, capital }.ToDictionary(a => a.Id), Base, Base, _ => true);

        var good = NewVoucher(VoucherKind.Opening, (_cash, 700m, 0), (capital, 0, 700m));
        Assert.Empty(PostingEngine.ValidateForPosting(good, context));

        var unbalanced = NewVoucher(VoucherKind.Opening, (_cash, 700m, 0), (capital, 0, 600m));
        Assert.Contains("balance.unbalanced", Codes(PostingEngine.ValidateForPosting(unbalanced, context)));

        var withExpense = NewVoucher(VoucherKind.Opening, (_rent, 50m, 0), (capital, 0, 50m));
        Assert.Contains(new PostingIssue("lines[0].account", "line.account-not-balance-sheet"), PostingEngine.ValidateForPosting(withExpense, context));
    }

    [Fact]
    public void Voucher_numbers_have_a_prefix_for_every_kind()
    {
        Assert.Equal(["PV", "RV", "JV", "TV", "OB", "CL", "SI", "SC", "PI", "PD", "FX"], Enum.GetValues<VoucherKind>().Select(VoucherNumber.Prefix));
    }

    [Fact]
    public void Money_is_stored_in_exact_scaled_integers()
    {
        var line = new VoucherLine { Debit = 1234567.891m };

        Assert.Equal(12_345_678_910L, line.DebitScaled);
        Assert.Equal(1234567.891m, line.Debit);
        Assert.Equal(0.1m + 0.2m, Scaled.ToDecimal(Scaled.ToScaled(0.1m) + Scaled.ToScaled(0.2m)));
    }

    [Fact]
    public void Voucher_totals_add_up_the_lines()
    {
        var voucher = NewVoucher(VoucherKind.Journal, (_rent, 10.5m, 0), (_utilities, 4.25m, 0), (_sales, 0, 14.75m));

        Assert.Equal((14.75m, 14.75m), (voucher.TotalDebit, voucher.TotalCredit));
    }
}
