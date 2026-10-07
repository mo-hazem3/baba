namespace Baba.Domain.Assets;

/// <summary>
/// How much an asset loses in one month (brief section 10.4). Pure arithmetic: the caller says how much has been depreciated so far
/// and which month of the asset's life it is, so a missed month can always be caught up.
/// </summary>
public static class DepreciationEngine
{
    /// <summary>Months from the acquisition month to this one, counting the acquisition month as the first.</summary>
    public static int MonthNumber(DateOnly acquired, DateOnly month) => (month.Year * 12 + month.Month) - (acquired.Year * 12 + acquired.Month) + 1;

    /// <summary>
    /// The depreciation of one month, rounded to the currency. Never more than what is left above the salvage value; the last month of a
    /// straight-line life takes whatever is left, so the total is exactly cost minus salvage.
    /// </summary>
    public static decimal Next(
        DepreciationMethod method, decimal cost, decimal salvage, int usefulLifeMonths, decimal annualRatePercent,
        decimal accumulated, int monthNumber, Currency currency)
    {
        var remaining = cost - salvage - accumulated;
        if (remaining <= 0 || monthNumber < 1)
            return 0;

        decimal amount;
        if (method == DepreciationMethod.StraightLine)
        {
            if (usefulLifeMonths <= 0)
                return 0;
            amount = monthNumber >= usefulLifeMonths ? remaining : Money.Round((cost - salvage) / usefulLifeMonths, currency);
        }
        else
        {
            amount = Money.Round((cost - accumulated) * annualRatePercent / 100m / 12m, currency);
        }

        return Math.Min(amount, remaining);
    }
}
