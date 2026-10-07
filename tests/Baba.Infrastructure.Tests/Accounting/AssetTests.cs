using Baba.Application.Accounting;
using Baba.Application.Assets;
using Baba.Domain.Accounting;
using Baba.Domain.Assets;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>The asset register: depreciation runs that post and catch up, disposals with gain or loss, and the register that agrees with the ledger (brief section 10.4).</summary>
public class AssetTests : AccountingFixture
{
    private static DateOnly Jan(int day) => new(2026, 1, day);
    private static DateOnly EndOf(int month) => new(2026, month, DateTime.DaysInMonth(2026, month));

    private static AssetInput Laptop(Env e, decimal cost = 1200m, int life = 12, DateOnly? acquired = null, DepreciationMethod method = DepreciationMethod.StraightLine, decimal rate = 0m) => new(
        "A001", "", "Laptop", AssetKind.Tangible, acquired ?? Jan(15), cost, 0m, life, method, rate, e.Id("121"), e.Id("123"), e.Id("426"));

    /// <summary>What the purchase of the asset did to the books: cost in the asset account, paid from the bank.</summary>
    private static async Task BuyAsync(Env e, decimal cost, DateOnly date) =>
        await e.Vouchers.SaveAndPostAsync(null, new VoucherInput(VoucherKind.Payment, date, e.Id("112"), null, null, [new VoucherLineInput(null, e.Id("121"), null, cost, 0)]));

    [Fact]
    public async Task A_new_asset_takes_the_default_depreciation_accounts_when_none_are_chosen()
    {
        var e = await NewEnvAsync();

        var asset = await e.FixedAssets.CreateAsync(Laptop(e) with { AccumulatedAccountId = null, ExpenseAccountId = null });

        Assert.Equal(e.Id("123"), asset.AccumulatedAccountId);
        Assert.Equal(e.Id("426"), asset.ExpenseAccountId);
        Assert.Equal(1200m, asset.BookValue);
    }

    [Fact]
    public async Task A_run_posts_one_voucher_per_month_and_catches_up_every_month_that_is_due()
    {
        var e = await NewEnvAsync();
        await BuyAsync(e, 1200m, Jan(15));
        await e.FixedAssets.CreateAsync(Laptop(e)); // acquired in January: depreciation starts in January, 100 a month

        var run = await e.FixedAssets.RunDepreciationAsync(EndOf(3));

        Assert.Equal(3, run.Items.Count);
        Assert.All(run.Items, i => Assert.Null(i.ProblemCode));
        Assert.Equal(300m, await BalanceAsync(e, "426"));
        Assert.Equal(-300m, await BalanceAsync(e, "123"));
        var voucher = await e.Vouchers.GetAsync(run.Items[0].VoucherId!.Value);
        Assert.StartsWith("DP-2026-", voucher!.Number);
        Assert.Equal(EndOf(1), voucher.Date);

        var again = await e.FixedAssets.RunDepreciationAsync(EndOf(3)); // nothing left to do
        Assert.Empty(again.Items);
        Assert.Equal(300m, (await e.FixedAssets.ListAsync()).Single().Accumulated);
    }

    [Fact]
    public async Task Depreciation_stops_when_the_life_is_over_and_the_total_is_the_cost()
    {
        var e = await NewEnvAsync();
        await BuyAsync(e, 1000m, Jan(1));
        await e.FixedAssets.CreateAsync(Laptop(e, cost: 1000m, life: 3, acquired: Jan(1)));

        await e.FixedAssets.RunDepreciationAsync(new DateOnly(2026, 12, 31));

        var asset = (await e.FixedAssets.ListAsync()).Single();
        Assert.Equal(1000m, asset.Accumulated);
        Assert.Equal(0m, asset.BookValue);
        Assert.Equal(1000m, await BalanceAsync(e, "426"));
    }

    [Fact]
    public async Task The_run_that_happens_when_a_company_opens_does_only_the_months_that_have_ended()
    {
        var e = await NewEnvAsync(); // the test clock says 6 October 2026
        await e.FixedAssets.CreateAsync(Laptop(e, acquired: new DateOnly(2026, 8, 10)));

        var run = await e.FixedAssets.RunDueAsync();

        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)], run.Items.Select(i => i.Month)); // not October yet
    }

    [Fact]
    public async Task A_declining_balance_asset_loses_less_each_month()
    {
        var e = await NewEnvAsync();
        await e.FixedAssets.CreateAsync(Laptop(e, cost: 1000m, acquired: Jan(1), method: DepreciationMethod.DecliningBalance, rate: 24m));

        await e.FixedAssets.RunDepreciationAsync(EndOf(2));

        Assert.Equal(39.6m, (await e.FixedAssets.ListAsync()).Single().Accumulated); // 20 + 19.6
    }

    [Fact]
    public async Task An_asset_already_in_use_continues_from_the_month_after_what_was_depreciated_before()
    {
        var e = await NewEnvAsync();
        var asset = await e.FixedAssets.CreateAsync(Laptop(e, cost: 1200m, acquired: new DateOnly(2025, 7, 1)) with
        {
            OpeningAccumulated = 500m, DepreciatedThrough = new DateOnly(2025, 12, 31),
        });

        var run = await e.FixedAssets.RunDepreciationAsync(EndOf(2));

        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)], run.Items.Select(i => i.Month));
        Assert.Equal(700m, (await e.FixedAssets.GetAsync(asset.Id))!.Accumulated);
    }

    [Fact]
    public async Task A_locked_month_stops_the_run_and_what_follows_waits()
    {
        var e = await NewEnvAsync();
        await e.FixedAssets.CreateAsync(Laptop(e));
        await e.Periods.SetLockedAsync(new DateOnly(2026, 2, 1), true);

        var run = await e.FixedAssets.RunDepreciationAsync(EndOf(4));

        Assert.Equal(2, run.Items.Count);
        Assert.Null(run.Items[0].ProblemCode);
        Assert.Equal("date.locked-period", run.Items[1].ProblemCode);
        Assert.Equal(100m, (await e.FixedAssets.ListAsync()).Single().Accumulated); // January only
    }

    [Fact]
    public async Task The_latest_month_can_be_taken_back_and_made_again()
    {
        var e = await NewEnvAsync();
        await e.FixedAssets.CreateAsync(Laptop(e));
        await e.FixedAssets.RunDepreciationAsync(EndOf(2));

        await e.FixedAssets.UndoLastDepreciationAsync();

        Assert.Equal(100m, await BalanceAsync(e, "426"));
        Assert.Equal(100m, (await e.FixedAssets.ListAsync()).Single().Accumulated);
        await e.FixedAssets.RunDepreciationAsync(EndOf(2));
        Assert.Equal(200m, (await e.FixedAssets.ListAsync()).Single().Accumulated);
    }

    [Fact]
    public async Task What_depreciation_was_worked_out_from_cannot_change_after_it_is_posted_but_the_name_can()
    {
        var e = await NewEnvAsync();
        var asset = await e.FixedAssets.CreateAsync(Laptop(e));
        await e.FixedAssets.RunDepreciationAsync(EndOf(1));

        var refused = await RefusedAsync(() => e.FixedAssets.UpdateAsync(asset.Id, Laptop(e, cost: 2000m)));
        var renamed = await e.FixedAssets.UpdateAsync(asset.Id, Laptop(e) with { NameEn = "Office laptop" });
        var deleteRefused = await RefusedAsync(() => e.FixedAssets.DeleteAsync(asset.Id));

        Assert.Contains("asset.terms-locked", Codes(refused));
        Assert.Equal("Office laptop", renamed.NameEn);
        Assert.Contains("asset.in-use", Codes(deleteRefused));
    }

    [Fact]
    public async Task Wrong_assets_are_refused_with_the_field_and_code()
    {
        var e = await NewEnvAsync();
        await e.FixedAssets.CreateAsync(Laptop(e));

        var refused = await RefusedAsync(() => e.FixedAssets.CreateAsync(Laptop(e, cost: 0m, life: 0) with { Salvage = 5m, AssetAccountId = e.Id("511") }));

        var codes = Codes(refused).ToList();
        Assert.Contains("asset.code-duplicate", codes);
        Assert.Contains("asset.cost-invalid", codes);
        Assert.Contains("asset.life-invalid", codes);
        Assert.Contains("asset.account-invalid", codes);
    }

    [Fact]
    public async Task Selling_for_more_than_the_book_value_books_a_gain_and_the_asset_is_off_the_register()
    {
        var e = await NewEnvAsync();
        await BuyAsync(e, 1200m, Jan(15));
        var asset = await e.FixedAssets.CreateAsync(Laptop(e));

        // Sold in March for 1,000 into the bank: depreciation to the end of March is 300, so the book value is 900 and the gain 100.
        var disposed = await e.FixedAssets.DisposeAsync(asset.Id, new DisposeInput(new DateOnly(2026, 3, 20), 1000m, e.Id("112"), null));

        Assert.Equal(AssetStatus.Disposed, disposed.Status);
        Assert.Equal(0m, await BalanceAsync(e, "121"));          // the cost is gone
        Assert.Equal(0m, await BalanceAsync(e, "123"));          // and so is its depreciation
        Assert.Equal(-100m, await BalanceAsync(e, "430"));       // a gain is a credit
        Assert.Equal(300m, await BalanceAsync(e, "426"));
        var register = await e.Listings.AssetRegisterAsync(new DateOnly(2026, 4, 30));
        Assert.Single(register.Rows); // only the total row
        Assert.All(register.Checks, c => Assert.True(c.Passed));

        var again = await RefusedAsync(() => e.FixedAssets.DisposeAsync(asset.Id, new DisposeInput(new DateOnly(2026, 4, 1), 0m, null, null)));
        Assert.Contains("asset.already-disposed", Codes(again));
    }

    [Fact]
    public async Task Scrapping_an_asset_books_the_book_value_as_a_loss_and_a_disposal_can_be_taken_back()
    {
        var e = await NewEnvAsync();
        await BuyAsync(e, 1200m, Jan(15));
        var asset = await e.FixedAssets.CreateAsync(Laptop(e));

        await e.FixedAssets.DisposeAsync(asset.Id, new DisposeInput(new DateOnly(2026, 2, 10), 0m, null, null)); // two months depreciated: book value 1,000
        Assert.Equal(1000m, await BalanceAsync(e, "430"));

        await e.FixedAssets.UndoDisposalAsync(asset.Id);

        Assert.Equal(0m, await BalanceAsync(e, "430"));
        Assert.Equal(1200m, await BalanceAsync(e, "121"));
        var back = (await e.FixedAssets.ListAsync()).Single();
        Assert.Equal(AssetStatus.Active, back.Status);
        Assert.Equal(200m, back.Accumulated);
    }

    [Fact]
    public async Task The_register_agrees_with_the_ledger_and_says_so_and_notices_when_it_does_not()
    {
        var e = await NewEnvAsync();
        await BuyAsync(e, 1200m, Jan(15));
        await e.FixedAssets.CreateAsync(Laptop(e));
        await e.FixedAssets.RunDepreciationAsync(EndOf(2));

        var register = await e.Listings.AssetRegisterAsync(EndOf(2));

        var row = register.Rows.First();
        Assert.Equal("A001", row.Cells[0].Text);
        Assert.Equal(1200m, row.Cells[5].Amount);
        Assert.Equal(200m, row.Cells[6].Amount);
        Assert.Equal(1000m, row.Cells[7].Amount);
        Assert.All(register.Checks, c => Assert.True(c.Passed));

        await BuyAsync(e, 300m, EndOf(2)); // an asset bought but not entered in the register
        Assert.Contains(await e.Listings.AssetRegisterAsync(EndOf(2)) is { } r ? r.Checks : [], c => !c.Passed);
    }
}
