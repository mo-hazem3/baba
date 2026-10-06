using Baba.Application.Companies;
using Baba.Domain.Accounting;

namespace Baba.Application.Accounting;

public sealed record PeriodDto(DateOnly Start, DateOnly End, bool IsLocked);

/// <summary>
/// Locking months (brief sections 8 and 10.2): after filing a VAT return, say, nobody can add, change or delete a posted voucher
/// dated in that month until it is unlocked again.
/// </summary>
public sealed class PeriodService(IPeriodStore periods, ICompanyFiles files)
{
    /// <summary>The twelve months of a fiscal year with their lock state.</summary>
    public async Task<IReadOnlyList<PeriodDto>> ListYearAsync(int fiscalYear, CancellationToken cancellationToken = default)
    {
        var startMonth = (files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.")).FiscalYearStartMonth;
        var locked = (await periods.ListAsync(cancellationToken)).Where(p => p.IsLocked).Select(p => p.Start).ToHashSet();

        var (start, _) = FiscalYear.Range(fiscalYear, startMonth);
        return Enumerable.Range(0, 12)
            .Select(i => Period.MonthOf(start.AddMonths(i)))
            .Select(month => new PeriodDto(month.Start, month.End, locked.Contains(month.Start)))
            .ToList();
    }

    public Task SetLockedAsync(DateOnly monthStart, bool locked, CancellationToken cancellationToken = default)
    {
        if (monthStart.Day != 1)
            throw new ValidationException([new ValidationIssue("month", "period.month-start-required")]);

        return periods.SetLockedAsync(monthStart, locked, cancellationToken);
    }
}
