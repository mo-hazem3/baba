using Baba.Application.Accounting;
using Baba.Application.Trade;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Recurring invoices and entries (brief section 10.3).</summary>
public class RecurringTests : AccountingFixture
{
    private static DocumentInput Rent(Env e, decimal price = 500m) => new(
        DocumentKind.SalesInvoice, Oct6, null, e.Customer, null, null, null, null, 0,
        [new DocumentLineInput(null, null, e.Id("511"), "Monthly rent", 1, price)]);

    private static RecurringInput Monthly(DateOnly first, bool autoIssue = false, DateOnly? end = null) => new("Rent", RecurrenceFrequency.Monthly, first, end, autoIssue);

    // ------------------------------------------------------------------ Dates

    [Fact]
    public void Monthly_dates_keep_the_day_they_started_on_where_the_month_allows()
    {
        var date = new DateOnly(2026, 1, 31);
        var seen = new List<DateOnly>();
        for (var i = 0; i < 4; i++)
        {
            date = Recurrence.Next(date, RecurrenceFrequency.Monthly, 31);
            seen.Add(date);
        }

        Assert.Equal([new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 31)], seen);
        Assert.Equal(new DateOnly(2026, 10, 14), Recurrence.Next(new DateOnly(2026, 10, 7), RecurrenceFrequency.Weekly, 7));
        Assert.Equal(new DateOnly(2027, 1, 15), Recurrence.Next(new DateOnly(2026, 10, 15), RecurrenceFrequency.Quarterly, 15));
        Assert.Equal(new DateOnly(2025, 2, 28), Recurrence.Next(new DateOnly(2024, 2, 29), RecurrenceFrequency.Yearly, 29));
    }

    // ------------------------------------------------------------------ Documents

    [Fact]
    public async Task A_recurring_invoice_makes_one_draft_for_each_missed_date_and_remembers_where_it_got_to()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Rent(e));
        var schedule = await e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(new DateOnly(2026, 8, 31)));

        var result = await e.Recurring.RunDueAsync(Oct31);

        Assert.Equal([new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30), Oct31], result.Items.Select(i => i.Date));
        Assert.All(result.Items, i => Assert.Null(i.ProblemCode));
        var drafts = (await e.Trade.ListAsync(new DocumentSearch(DocumentKind.SalesInvoice, DocumentStatus.Draft))).OrderBy(d => d.Date).ToList();
        Assert.Equal([new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30), Oct31], drafts.Select(d => d.Date));
        Assert.All(drafts, d => Assert.Equal(500m, d.Total));
        Assert.Equal(500m, await BalanceAsync(e, "113")); // only the original invoice is in the books: drafts post nothing

        var after = (await e.Recurring.ListAsync()).Single(s => s.Id == schedule.Id);
        Assert.Equal(new DateOnly(2026, 11, 30), after.NextRunDate);
        Assert.Equal(3, after.RunCount);
        Assert.Equal(Oct31, after.LastRunDate);

        Assert.Empty((await e.Recurring.RunDueAsync(Oct31)).Items); // running again the same day makes nothing more
    }

    [Fact]
    public async Task An_automatic_schedule_issues_and_posts_what_it_makes()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Rent(e, 200m));
        await e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(new DateOnly(2026, 9, 1), autoIssue: true));

        var result = await e.Recurring.RunDueAsync(Oct6);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.True(i.Issued));
        Assert.All(result.Items, i => Assert.StartsWith("SI-2026-", i.Number));
        Assert.Equal(600m, await BalanceAsync(e, "113")); // the original and two more
    }

    [Fact]
    public async Task A_schedule_stops_at_its_end_date_and_a_switched_off_one_does_not_run()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Rent(e));
        await e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(new DateOnly(2026, 1, 1), end: new DateOnly(2026, 3, 1)));
        var off = await e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(new DateOnly(2026, 1, 1)));
        await e.Recurring.SetActiveAsync(off.Id, false);

        var result = await e.Recurring.RunDueAsync(Oct31);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, (await e.Recurring.ListAsync()).First(s => s.EndDate is not null).RunCount);
        Assert.Equal(0, (await e.Recurring.ListAsync()).Single(s => s.Id == off.Id).RunCount);
    }

    [Fact]
    public async Task A_locked_month_stops_that_schedule_with_a_reason_and_the_others_carry_on()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Rent(e));
        var stuck = await e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(new DateOnly(2026, 8, 10), autoIssue: true));
        var fine = await e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(new DateOnly(2026, 9, 10), autoIssue: false) with { Name = "Drafts" });
        await e.Periods.SetLockedAsync(new DateOnly(2026, 8, 1), true);

        var result = await e.Recurring.RunDueAsync(Oct31);

        var problem = Assert.Single(result.Items, i => i.ProblemCode is not null);
        Assert.Equal(stuck.Id, problem.ScheduleId);
        Assert.Equal("date.locked-period", problem.ProblemCode);
        Assert.Equal(new DateOnly(2026, 8, 10), (await e.Recurring.ListAsync()).Single(s => s.Id == stuck.Id).NextRunDate); // it will try the same date again
        Assert.Equal(2, (await e.Recurring.ListAsync()).Single(s => s.Id == fine.Id).RunCount);
    }

    // ------------------------------------------------------------------ Vouchers

    [Fact]
    public async Task A_recurring_journal_entry_posts_each_time_it_is_due()
    {
        var e = await NewEnvAsync();
        var journal = await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct1, ("422", 100m, 0), ("111", 0, 100m)));
        await e.Recurring.CreateFromVoucherAsync(journal.Id, new RecurringInput("Rent expense", RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 1), null, true));

        var result = await e.Recurring.RunDueAsync(new DateOnly(2027, 1, 1));

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, i => Assert.StartsWith("JV-", i.Number));
        Assert.Equal(400m, await BalanceAsync(e, "422")); // the original and three more
    }

    [Fact]
    public async Task What_a_schedule_cannot_be_made_from_is_refused_and_the_form_is_checked()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Rent(e));

        var system = await RefusedAsync(() => e.Recurring.CreateFromVoucherAsync(invoice.VoucherId!.Value, Monthly(Oct31)));
        var noName = await RefusedAsync(() => e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(Oct31) with { Name = " " }));
        var backwards = await RefusedAsync(() => e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(Oct31, end: Oct1)));

        Assert.Contains("recurring.template-unavailable", Codes(system));
        Assert.Contains("recurring.name-required", Codes(noName));
        Assert.Contains("recurring.end-before-next", Codes(backwards));
    }

    [Fact]
    public async Task A_schedule_can_be_changed_and_deleted_without_touching_what_it_made()
    {
        var e = await NewEnvAsync();
        var invoice = await e.Trade.IssueAsync(null, Rent(e));
        var schedule = await e.Recurring.CreateFromDocumentAsync(invoice.Id, Monthly(new DateOnly(2026, 10, 1)));
        await e.Recurring.RunDueAsync(Oct6);

        var changed = await e.Recurring.UpdateAsync(schedule.Id, new RecurringInput("Rent (quarterly)", RecurrenceFrequency.Quarterly, new DateOnly(2027, 1, 1), null, true));
        Assert.Equal(RecurrenceFrequency.Quarterly, changed.Frequency);
        Assert.True(changed.AutoIssue);

        await e.Recurring.DeleteAsync(schedule.Id);

        Assert.Empty(await e.Recurring.ListAsync());
        Assert.Single(await e.Trade.ListAsync(new DocumentSearch(DocumentKind.SalesInvoice, DocumentStatus.Draft)));
    }
}
