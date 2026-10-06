using Baba.Domain.Accounting;

namespace Baba.Domain.Tests.Accounting;

public class NumberingTests
{
    [Theory]
    [InlineData(2026, 1, 1, 1, 2026)]   // calendar-year company: the year is the calendar year
    [InlineData(2026, 12, 31, 1, 2026)]
    [InlineData(2026, 3, 31, 4, 2025)]  // fiscal year starting in April: March 2026 still belongs to the year that began in 2025
    [InlineData(2026, 4, 1, 4, 2026)]
    [InlineData(2027, 1, 15, 7, 2026)]
    public void A_fiscal_year_is_named_by_the_calendar_year_it_starts_in(int year, int month, int day, int startMonth, int expected) =>
        Assert.Equal(expected, FiscalYear.Of(new DateOnly(year, month, day), startMonth));

    [Fact]
    public void A_fiscal_year_runs_twelve_months_from_its_start_month()
    {
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), FiscalYear.Range(2026, 1));
        Assert.Equal((new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31)), FiscalYear.Range(2025, 4));
    }

    [Theory]
    [InlineData(VoucherKind.Payment, 2026, 1, "PV-2026-0001")]
    [InlineData(VoucherKind.Receipt, 2026, 12, "RV-2026-0012")]
    [InlineData(VoucherKind.Journal, 2025, 1234, "JV-2025-1234")]
    [InlineData(VoucherKind.Payment, 2026, 12345, "PV-2026-12345")] // more than four digits just keeps growing
    public void Numbers_read_like_PV_2026_0001(VoucherKind kind, int year, int sequence, string expected) =>
        Assert.Equal(expected, VoucherNumber.Format(kind, year, sequence));

    [Fact]
    public void A_month_runs_from_its_first_to_its_last_day()
    {
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), Period.MonthOf(new DateOnly(2026, 2, 14)));
        Assert.Equal((new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)), Period.MonthOf(new DateOnly(2028, 2, 1)));
        Assert.Equal((new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)), Period.MonthOf(new DateOnly(2026, 12, 31)));
    }
}
