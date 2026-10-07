using Baba.Application.Accounting;
using Baba.Domain.Accounting;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>The screens ask for many things at once; the company file must serve a burst without stalling or deadlocking.</summary>
public class ConcurrencyTests : AccountingFixture
{
    [Fact]
    public async Task A_burst_of_many_more_requests_than_connections_all_complete()
    {
        var e = await SeedMonthAsync();

        // Far more simultaneous requests than the company file lets run at once, each reading or writing through its own context.
        var work = Enumerable.Range(0, 40).Select(async i =>
        {
            if (i % 5 == 0)
                await e.Vouchers.SaveAndPostAsync(null, Journal(e, Oct6, ("111", 1m, 0), ("31", 0, 1m)));
            else if (i % 5 == 1)
                await e.Reports.TrialBalanceAsync(Oct1, Oct31);
            else if (i % 5 == 2)
                await e.Chart.ListAsync();
            else if (i % 5 == 3)
                await e.Dashboard.GetAsync();
            else
                await e.Parties.ListAsync(null);
        }).ToList();

        var finished = await Task.WhenAny(Task.WhenAll(work), Task.Delay(TimeSpan.FromSeconds(90)));

        Assert.True(finished == Task.WhenAll(work) || work.All(t => t.IsCompleted), "The burst did not finish: something is waiting for a connection that is never given back.");
        await Task.WhenAll(work);
        Assert.Equal(8, (await e.Vouchers.ListAsync(new VoucherSearch(VoucherKind.Journal, null, null, null))).Count(v => v.Total == 1m));
    }
}
