using Baba.Application;
using Baba.Domain;
using Baba.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Tests.Accounting;

public class VoucherLifecycleTests : AccountingFixture
{
    [Fact]
    public async Task A_draft_makes_no_ledger_entries_and_has_no_number_until_it_is_posted()
    {
        var e = await NewEnvAsync();

        var draft = await e.Vouchers.SaveDraftAsync(null, Payment(e, Oct6, ("422", 300m)));

        Assert.Equal(VoucherStatus.Draft, draft.Status);
        Assert.Null(draft.Number);
        Assert.Null(draft.PostedAt);
        Assert.Empty(await e.Ledger.TotalsAsync(null, null));

        var posted = await e.Vouchers.PostAsync(draft.Id);

        Assert.Equal(VoucherStatus.Posted, posted.Status);
        Assert.Equal("PV-2026-0001", posted.Number);
        Assert.Equal(Clock.Now.UtcDateTime, posted.PostedAt);
        Assert.Equal(300m, await BalanceAsync(e, "422"));
        Assert.Equal(-300m, await BalanceAsync(e, "111")); // the cash side of the payment
    }

    [Fact]
    public async Task Each_kind_of_voucher_is_numbered_separately_and_in_order_within_its_fiscal_year()
    {
        var e = await NewEnvAsync();

        var p1 = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 10m)));
        var p2 = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 20m)));
        var r1 = await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 30m)));
        var j1 = await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("422", 5m, 0), ("512", 0, 5m)));
        var nextYear = await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2027, 1, 15), ("422", 1m)));

        Assert.Equal(["PV-2026-0001", "PV-2026-0002", "RV-2026-0001", "JV-2026-0001", "PV-2027-0001"],
            new[] { p1, p2, r1, j1, nextYear }.Select(v => v.Number));
    }

    [Fact]
    public async Task Payments_receipts_and_journals_each_post_balanced_entries_and_the_ledger_stays_in_balance()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("31", 5000m)));                          // capital in
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 750m), ("423", 120.5m)));          // rent + utilities
        await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("113", 900m, 0), ("511", 0, 900m)));      // a sale on credit

        var totals = await e.Ledger.TotalsAsync(null, null);

        Assert.Equal(totals.Sum(t => t.Debit), totals.Sum(t => t.Credit)); // a general ledger always balances
        Assert.Equal(5000m, await BalanceAsync(e, "112"));    // 5000 received into the bank
        Assert.Equal(-870.5m, await BalanceAsync(e, "111"));  // 750 + 120.5 paid out of cash
        Assert.Equal(900m, await BalanceAsync(e, "113"));
        Assert.Equal(-900m, await BalanceAsync(e, "511"));
    }

    [Fact]
    public async Task Editing_a_posted_voucher_replaces_its_entries_and_keeps_its_number()
    {
        var e = await NewEnvAsync("amal");
        var posted = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 300m)));

        var edited = await e.Vouchers.SaveAndPostAsync(posted.Id, Payment(e, new DateOnly(2026, 10, 9), ("422", 250m), ("423", 50m)));

        Assert.Equal(posted.Number, edited.Number);
        Assert.Equal(2, edited.Lines.Count);
        Assert.Equal(250m, await BalanceAsync(e, "422"));
        Assert.Equal(50m, await BalanceAsync(e, "423"));
        Assert.Equal(-300m, await BalanceAsync(e, "111"));
        Assert.Equal(posted.Id, (await e.Vouchers.ListAsync(new Application.Accounting.VoucherSearch())).Single().Id);

        // No stale entries: the ledger holds exactly the new voucher's three entries, on the new date.
        var lines = await e.Ledger.LinesAsync(null, null, null);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(new DateOnly(2026, 10, 9), l.Date));

        // And the change is in the audit log with who made it.
        using var context = e.Files.Create();
        var changes = await context.AuditLog.Where(l => l.EntityId == posted.Id && l.Action == AuditAction.Updated).ToListAsync();
        Assert.NotEmpty(changes);
        Assert.All(changes, c => Assert.Equal("amal", c.UserId));
    }

    [Fact]
    public async Task A_posted_voucher_saved_as_a_draft_goes_back_to_draft_and_reposting_keeps_the_number()
    {
        var e = await NewEnvAsync();
        var posted = await e.Vouchers.SaveAndPostAsync(null, Receipt(e, Oct6, ("511", 40m)));

        var draft = await e.Vouchers.SaveDraftAsync(posted.Id, Receipt(e, Oct6, ("511", 45m)));

        Assert.Equal(VoucherStatus.Draft, draft.Status);
        Assert.Equal(posted.Number, draft.Number); // numbers are never reused or lost
        Assert.Empty(await e.Ledger.TotalsAsync(null, null));

        var reposted = await e.Vouchers.PostAsync(posted.Id);

        Assert.Equal(posted.Number, reposted.Number);
        Assert.Equal(-45m, await BalanceAsync(e, "511"));
    }

    [Fact]
    public async Task Deleting_a_posted_voucher_removes_it_and_its_entries_but_numbers_are_not_reused()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 10m)));
        var second = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 20m)));

        await e.Vouchers.DeleteAsync(second.Id);

        Assert.Null(await e.Vouchers.GetAsync(second.Id));
        Assert.Equal(10m, await BalanceAsync(e, "422"));
        Assert.Equal(2, (await e.Ledger.LinesAsync(null, null, null)).Count);

        var third = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 5m)));
        Assert.Equal("PV-2026-0003", third.Number); // 0002 stays a gap, visible in the audit log

        using var context = e.Files.Create();
        Assert.True(await context.AuditLog.AnyAsync(l => l.EntityId == second.Id && l.Action == AuditAction.Deleted));
        await Assert.ThrowsAsync<NotFoundException>(() => e.Vouchers.DeleteAsync(second.Id));
    }

    [Fact]
    public async Task Nothing_is_saved_when_a_voucher_is_refused()
    {
        var e = await NewEnvAsync();

        var unbalanced = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("422", 100m, 0), ("512", 0, 99m))));
        Assert.Contains("balance.unbalanced", Codes(unbalanced));

        var noLines = await RefusedAsync(() => e.Vouchers.SaveDraftAsync(null, Payment(e, Oct6)));
        Assert.Contains("lines.required", Codes(noLines));

        var noCash = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 10m)) with { CashAccountId = null }));
        Assert.Contains("cash-account.required", Codes(noCash));

        var notACashAccount = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 10m)) with { CashAccountId = e.Id("423") }));
        Assert.Contains("cash-account.invalid", Codes(notACashAccount));

        var groupAccount = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("42", 10m))));
        Assert.Contains("line.account-not-posting", Codes(groupAccount));

        Assert.Empty(await e.Vouchers.ListAsync(new Application.Accounting.VoucherSearch()));
        Assert.Empty(await e.Ledger.TotalsAsync(null, null));
    }

    [Fact]
    public async Task An_inactive_account_cannot_be_posted_to_but_old_entries_stay_in_the_ledger()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("424", 10m)));
        await e.Chart.SetActiveAsync(e.Id("424"), false);

        var refused = await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("424", 5m))));

        Assert.Contains("line.account-inactive", Codes(refused));
        Assert.Equal(10m, await BalanceAsync(e, "424"));
    }

    [Fact]
    public async Task A_voucher_kind_cannot_be_changed_after_it_is_created()
    {
        var e = await NewEnvAsync();
        var payment = await e.Vouchers.SaveDraftAsync(null, Payment(e, Oct6, ("422", 10m)));

        var refused = await RefusedAsync(() => e.Vouchers.SaveDraftAsync(payment.Id, Receipt(e, Oct6, ("511", 10m))));

        Assert.Contains("voucher.kind-cannot-change", Codes(refused));
    }

    [Fact]
    public async Task A_locked_month_cannot_be_posted_to_edited_or_deleted_until_it_is_unlocked()
    {
        var e = await NewEnvAsync();
        var inOctober = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 100m)));
        var draft = await e.Vouchers.SaveDraftAsync(null, Payment(e, new DateOnly(2026, 10, 20), ("422", 7m)));

        await e.Periods.SetLockedAsync(new DateOnly(2026, 10, 1), locked: true);

        Assert.Contains("date.locked-period", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 1m))))));
        Assert.Contains("date.locked-period", Codes(await RefusedAsync(() => e.Vouchers.PostAsync(draft.Id))));
        Assert.Contains("date.locked-period", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(inOctober.Id, Payment(e, Oct6, ("422", 90m))))));
        Assert.Contains("date.locked-period", Codes(await RefusedAsync(() => e.Vouchers.SaveDraftAsync(inOctober.Id, Payment(e, Oct6, ("422", 90m))))));
        Assert.Contains("date.locked-period", Codes(await RefusedAsync(() => e.Vouchers.DeleteAsync(inOctober.Id))));
        // Moving a posted voucher OUT of a locked month changes the locked month too, so that is refused as well.
        Assert.Contains("date.locked-period", Codes(await RefusedAsync(() => e.Vouchers.SaveAndPostAsync(inOctober.Id, Payment(e, new DateOnly(2026, 11, 2), ("422", 100m))))));
        Assert.Equal(100m, await BalanceAsync(e, "422")); // unchanged

        // A draft can still be saved in a locked month (it changes no ledger), and other months stay open.
        await e.Vouchers.SaveDraftAsync(draft.Id, Payment(e, new DateOnly(2026, 10, 20), ("422", 8m)));
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 11, 2), ("422", 3m)));

        await e.Periods.SetLockedAsync(new DateOnly(2026, 10, 1), locked: false);

        await e.Vouchers.PostAsync(draft.Id);
        await e.Vouchers.DeleteAsync(inOctober.Id);
    }

    [Fact]
    public async Task The_periods_screen_lists_the_twelve_months_of_the_fiscal_year_with_their_lock_state()
    {
        var e = await NewEnvAsync();
        await e.Periods.SetLockedAsync(new DateOnly(2026, 3, 1), true);
        await e.Periods.SetLockedAsync(new DateOnly(2026, 4, 1), true);
        await e.Periods.SetLockedAsync(new DateOnly(2026, 4, 1), false);
        await e.Periods.SetLockedAsync(new DateOnly(2026, 6, 1), false); // never locked: nothing to do

        var months = await e.Periods.ListYearAsync(2026);

        Assert.Equal(12, months.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), months[0].Start);
        Assert.Equal(new DateOnly(2026, 12, 31), months[11].End);
        Assert.Equal([3], months.Where(m => m.IsLocked).Select(m => m.Start.Month));
        await Assert.ThrowsAsync<ValidationException>(() => e.Periods.SetLockedAsync(new DateOnly(2026, 3, 15), true));
    }

    [Fact]
    public async Task Sums_are_exact_to_the_last_fils_even_with_the_classic_floating_point_traps()
    {
        var e = await NewEnvAsync();
        // 0.1 + 0.2 and thirds are the classic traps; three-decimal dinars make them worse.
        for (var i = 0; i < 100; i++)
            await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("422", 0.1m, 0), ("512", 0, 0.1m)));
        await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("422", 0.2m, 0), ("512", 0, 0.2m)));
        await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("423", 333.333m, 0), ("512", 0, 333.333m)));

        Assert.Equal(10.2m, await BalanceAsync(e, "422"));
        Assert.Equal(333.333m, await BalanceAsync(e, "423"));
        Assert.Equal(-343.533m, await BalanceAsync(e, "512"));
    }

    [Fact]
    public async Task Totals_and_statement_lines_respect_date_ranges_and_come_in_a_stable_order()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 9, 30), ("422", 10m)));
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 10, 1), ("422", 20m)));
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 10, 31), ("422", 40m), ("423", 3m)));
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 11, 1), ("422", 80m)));

        var october = await e.Ledger.TotalsAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var untilSeptember = await e.Ledger.TotalsAsync(null, new DateOnly(2026, 9, 30));

        Assert.Equal(60m, october.Single(t => t.AccountId == e.Id("422")).Debit); // both ends are inclusive
        Assert.Equal(10m, untilSeptember.Single(t => t.AccountId == e.Id("422")).Debit);

        var lines = await e.Ledger.LinesAsync([e.Id("422"), e.Id("423")], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal([20m, 40m, 3m], lines.Select(l => l.Debit));
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 31)], lines.Select(l => l.Date));
        Assert.Equal(["PV-2026-0002", "PV-2026-0003", "PV-2026-0003"], lines.Select(l => l.VoucherNumber));
        Assert.All(lines, l => Assert.Equal(VoucherKind.Payment, l.Kind));
    }

    [Fact]
    public async Task The_voucher_list_filters_and_shows_the_money_moved()
    {
        var e = await NewEnvAsync();
        await e.Vouchers.SaveAndPostAsync(null, Payment(e, new DateOnly(2026, 10, 1), ("422", 100m), ("423", 20m)));
        await e.Vouchers.SaveAndPostAsync(null, Receipt(e, new DateOnly(2026, 10, 2), ("511", 500m)));
        await e.Vouchers.SaveDraftAsync(null, Journal(e, new DateOnly(2026, 10, 3), ("422", 7m, 0), ("512", 0, 7m)));

        var all = await e.Vouchers.ListAsync(new Application.Accounting.VoucherSearch());
        Assert.Equal([VoucherKind.Journal, VoucherKind.Receipt, VoucherKind.Payment], all.Select(v => v.Kind)); // newest first
        Assert.Equal([7m, 500m, 120m], all.Select(v => v.Total));
        Assert.Equal([2, 1, 2], all.Select(v => v.LineCount));

        Assert.Single(await e.Vouchers.ListAsync(new Application.Accounting.VoucherSearch(Kind: VoucherKind.Receipt)));
        Assert.Single(await e.Vouchers.ListAsync(new Application.Accounting.VoucherSearch(Status: VoucherStatus.Draft)));
        Assert.Equal(2, (await e.Vouchers.ListAsync(new Application.Accounting.VoucherSearch(From: new DateOnly(2026, 10, 2)))).Count);
        Assert.Single(await e.Vouchers.ListAsync(new Application.Accounting.VoucherSearch(To: new DateOnly(2026, 10, 1))));
    }

    [Fact]
    public async Task Ledger_entries_are_not_written_to_the_audit_log_row_by_row_but_vouchers_are()
    {
        var e = await NewEnvAsync();
        var posted = await e.Vouchers.SaveAndPostAsync(null, Payment(e, Oct6, ("422", 10m)));

        using var context = e.Files.Create();
        var names = await context.AuditLog.Select(l => l.EntityName).Distinct().ToListAsync();

        Assert.Contains(nameof(Voucher), names);
        Assert.Contains(nameof(VoucherLine), names);
        Assert.DoesNotContain(nameof(LedgerEntry), names);
        Assert.DoesNotContain(nameof(NumberSequence), names);
        Assert.Contains(await context.AuditLog.Where(l => l.EntityId == posted.Id).ToListAsync(), l => l.AfterJson!.Contains("PV-2026-0001"));
    }
}
