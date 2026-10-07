using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Budgets;

namespace Baba.Application.Budgets;

public interface IBudgetStore
{
    Task<IReadOnlyList<BudgetEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces every entry of one fiscal year and cost center (or of the whole company when the cost center is empty) with these.</summary>
    Task ReplaceAsync(int fiscalYear, Guid? costCenterId, IReadOnlyList<BudgetEntry> entries, CancellationToken cancellationToken = default);
}

/// <summary>One account's plan for the twelve months of a fiscal year (index 0 is the month the year starts in).</summary>
public sealed record BudgetLineInput(Guid AccountId, IReadOnlyList<decimal> Amounts);

public sealed record BudgetInput(int FiscalYear, Guid? CostCenterId, IReadOnlyList<BudgetLineInput> Lines);

public sealed record BudgetDto(int FiscalYear, Guid? CostCenterId, DateOnly Start, DateOnly End, IReadOnlyList<BudgetLineInput> Lines);

public sealed record CopyBudgetInput(int FromFiscalYear, int ToFiscalYear, Guid? CostCenterId, decimal PercentChange);

/// <summary>What was planned for an account over a period.</summary>
public sealed record PlannedAmount(Guid AccountId, decimal Amount);

/// <summary>
/// Budgets (brief section 10.4): a plan per revenue or expense account for each month of a fiscal year, optionally for one cost center,
/// to be read against what really happened in the budget-versus-actual report.
/// </summary>
public sealed class BudgetService(IBudgetStore store, IAccountStore accounts, ICostCenterStore costCenters, ICompanyFiles files)
{
    private CompanyInfo CompanyOrThrow() => files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");

    public async Task<BudgetDto> GetAsync(int fiscalYear, Guid? costCenterId, CancellationToken cancellationToken = default)
    {
        var (start, end) = FiscalYear.Range(fiscalYear, CompanyOrThrow().FiscalYearStartMonth);
        var entries = (await store.ListAsync(cancellationToken)).Where(e => e.FiscalYear == fiscalYear && e.CostCenterId == costCenterId).ToList();
        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var lines = entries.GroupBy(e => e.AccountId).Where(g => chart.ContainsKey(g.Key)).OrderBy(g => chart[g.Key].Code, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var months = new decimal[12];
                foreach (var entry in g)
                    months[entry.Period - 1] = entry.Amount;
                return new BudgetLineInput(g.Key, months);
            }).ToList();
        return new BudgetDto(fiscalYear, costCenterId, start, end, lines);
    }

    /// <summary>The fiscal years that have a budget.</summary>
    public async Task<IReadOnlyList<int>> YearsAsync(CancellationToken cancellationToken = default) =>
        (await store.ListAsync(cancellationToken)).Select(e => e.FiscalYear).Distinct().OrderDescending().ToList();

    /// <summary>Saves a budget: what is sent replaces the budget of that fiscal year and cost center. An account with every month at zero is left out.</summary>
    public async Task<BudgetDto> SaveAsync(BudgetInput input, CancellationToken cancellationToken = default)
    {
        var company = CompanyOrThrow();
        var issues = new List<ValidationIssue>();
        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var costCenterId = input.CostCenterId == Guid.Empty ? null : input.CostCenterId;
        if (costCenterId is { } center && !(await costCenters.ListAsync(cancellationToken)).Any(c => c.Id == center))
            issues.Add(new("costCenter", "budget.cost-center-unknown"));
        if (input.FiscalYear < 1900 || input.FiscalYear > 2200)
            issues.Add(new("fiscalYear", "budget.year-invalid"));

        var entries = new List<BudgetEntry>();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < (input.Lines?.Count ?? 0); i++)
        {
            var line = input.Lines![i];
            if (!chart.TryGetValue(line.AccountId, out var account) || !account.IsPosting || account.Type is not (AccountType.Revenue or AccountType.Expense))
                issues.Add(new($"lines[{i}].account", "budget.account-invalid"));
            else if (!seen.Add(line.AccountId))
                issues.Add(new($"lines[{i}].account", "budget.account-twice"));
            if (line.Amounts is null || line.Amounts.Count != 12)
            {
                issues.Add(new($"lines[{i}].amounts", "budget.months-invalid"));
                continue;
            }

            if (line.Amounts.Any(a => a < 0))
                issues.Add(new($"lines[{i}].amounts", "budget.amount-negative"));
            for (var month = 0; month < 12; month++)
            {
                if (line.Amounts[month] == 0)
                    continue;
                entries.Add(new BudgetEntry { CompanyId = company.Id, FiscalYear = input.FiscalYear, AccountId = line.AccountId, CostCenterId = costCenterId, Period = month + 1, Amount = line.Amounts[month] });
            }
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        await store.ReplaceAsync(input.FiscalYear, costCenterId, entries, cancellationToken);
        return await GetAsync(input.FiscalYear, costCenterId, cancellationToken);
    }

    /// <summary>Starts a budget from another year's, with every amount raised or lowered by a percentage. It replaces the budget of the target year.</summary>
    public async Task<BudgetDto> CopyAsync(CopyBudgetInput input, CancellationToken cancellationToken = default)
    {
        var source = await GetAsync(input.FromFiscalYear, input.CostCenterId, cancellationToken);
        if (source.Lines.Count == 0)
            throw new ValidationException([new ValidationIssue("fiscalYear", "budget.nothing-to-copy")]);

        var factor = 1 + input.PercentChange / 100m;
        var currencyDecimals = CurrencyDecimals();
        return await SaveAsync(new BudgetInput(
            input.ToFiscalYear, input.CostCenterId,
            [.. source.Lines.Select(l => new BudgetLineInput(l.AccountId, [.. l.Amounts.Select(a => Math.Max(0, Math.Round(a * factor, currencyDecimals, MidpointRounding.AwayFromZero)))]))]),
            cancellationToken);
    }

    /// <summary>What was planned for each account in the months that fall inside a period (a month counts when its first day is inside it).</summary>
    public async Task<IReadOnlyList<PlannedAmount>> PlannedAsync(DateOnly from, DateOnly to, Guid? costCenterId, CancellationToken cancellationToken = default)
    {
        var startMonth = CompanyOrThrow().FiscalYearStartMonth;
        var planned = new Dictionary<Guid, decimal>();
        foreach (var entry in (await store.ListAsync(cancellationToken)).Where(e => e.CostCenterId == costCenterId))
        {
            var month = FiscalYear.Range(entry.FiscalYear, startMonth).Start.AddMonths(entry.Period - 1);
            if (month >= new DateOnly(from.Year, from.Month, 1) && month <= to)
                planned[entry.AccountId] = planned.GetValueOrDefault(entry.AccountId) + entry.Amount;
        }

        return planned.Select(p => new PlannedAmount(p.Key, p.Value)).ToList();
    }

    private int CurrencyDecimals() => Localization.CurrencyCatalog.Find(CompanyOrThrow().BaseCurrencyCode)?.Currency.MinorUnits ?? 2;
}
