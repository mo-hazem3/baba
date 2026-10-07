using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Application.Accounting;

/// <summary>One fiscal year as the year-end screen shows it.</summary>
public sealed record FiscalYearDto(
    int Year,
    DateOnly Start,
    DateOnly End,
    bool HasEnded,
    bool IsClosed,
    decimal Profit,
    int DraftVouchers,
    Guid? ClosingVoucherId);

public sealed record CloseYearResult(FiscalYearDto Year, string VoucherNumber, string BackupPath);

/// <summary>
/// Year-end closing (brief section 10.2): the revenue and expense accounts of the year are brought to zero by one closing entry and
/// the year's profit (or loss) goes into retained earnings; then the year's twelve months are locked. Closing a year can be undone
/// (reopened) as long as no later year has been closed.
/// </summary>
public sealed class FiscalYearService(
    IAccountStore accounts,
    ILedgerQuery ledger,
    IVoucherStore vouchers,
    IPeriodStore periods,
    VoucherService voucherService,
    ICompanyFiles files,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<FiscalYearDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var company = Company();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var last = Math.Max(company.FirstFiscalYear, FiscalYear.Of(today, company.FiscalYearStartMonth));

        var result = new List<FiscalYearDto>();
        for (var year = company.FirstFiscalYear; year <= last; year++)
            result.Add(await DescribeAsync(year, company, today, cancellationToken));
        result.Reverse();
        return result;
    }

    public async Task<CloseYearResult> CloseAsync(int year, CancellationToken cancellationToken = default)
    {
        var company = Company();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var described = await DescribeAsync(year, company, today, cancellationToken);
        var (start, end) = FiscalYear.Range(year, company.FiscalYearStartMonth);

        var all = await accounts.ListAsync(cancellationToken);
        var retained = all.FirstOrDefault(a => a.Role == AccountRole.RetainedEarnings && a.IsPosting && a.IsActive);

        var issues = new List<ValidationIssue>();
        if (year < company.FirstFiscalYear || year > FiscalYear.Of(today, company.FiscalYearStartMonth))
            throw new NotFoundException("fiscal-year");
        if (described.IsClosed)
            issues.Add(new ValidationIssue("year", "year.already-closed"));
        if (!described.HasEnded)
            issues.Add(new ValidationIssue("year", "year.not-ended"));
        if (described.DraftVouchers > 0)
            issues.Add(new ValidationIssue("year", "year.drafts-exist"));
        if (retained is null)
            issues.Add(new ValidationIssue("year", "year.no-retained-earnings"));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        var totals = (await ledger.OperatingTotalsAsync(start, end, cancellationToken)).ToDictionary(t => t.AccountId);
        var lines = new List<VoucherLineInput>();
        decimal profit = 0;
        foreach (var account in all.Where(a => a.IsPosting && a.Type is AccountType.Revenue or AccountType.Expense).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (!totals.TryGetValue(account.Id, out var t) || t.Debit == t.Credit)
                continue;

            // Bring the account to zero: the opposite of its balance.
            var net = t.Debit - t.Credit;
            profit -= net;
            lines.Add(new VoucherLineInput(null, account.Id, null, net < 0 ? -net : 0, net > 0 ? net : 0));
        }

        if (lines.Count == 0)
            throw new ValidationException([new ValidationIssue("year", "year.nothing-to-close")]);

        lines.Add(new VoucherLineInput(null, retained!.Id, null, profit < 0 ? -profit : 0, profit > 0 ? profit : 0));

        // A copy of the company file first, so the close can always be taken back to exactly how it was.
        var backup = BackupPathFor(company.FilePath, year);
        await files.BackupAsync(backup, cancellationToken);

        var closing = await voucherService.SaveAndPostSystemAsync(
            new VoucherInput(VoucherKind.Closing, end, null, null, null, lines), cancellationToken: cancellationToken);
        for (var month = start; month <= end; month = month.AddMonths(1))
            await periods.SetLockedAsync(month, true, cancellationToken);

        return new CloseYearResult(await DescribeAsync(year, company, today, cancellationToken), closing.Number ?? "", backup);
    }

    /// <summary>Takes back the closing of a year: its closing entry is deleted and its months are unlocked. Only the latest closed year can be reopened.</summary>
    public async Task<FiscalYearDto> ReopenAsync(int year, CancellationToken cancellationToken = default)
    {
        var company = Company();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var described = await DescribeAsync(year, company, today, cancellationToken);
        if (!described.IsClosed)
            throw new ValidationException([new ValidationIssue("year", "year.not-closed")]);

        var later = (await ListAsync(cancellationToken)).Any(y => y.Year > year && y.IsClosed);
        if (later)
            throw new ValidationException([new ValidationIssue("year", "year.later-year-closed")]);

        var (start, end) = FiscalYear.Range(year, company.FiscalYearStartMonth);
        for (var month = start; month <= end; month = month.AddMonths(1))
            await periods.SetLockedAsync(month, false, cancellationToken);
        await voucherService.DeleteSystemAsync(described.ClosingVoucherId!.Value, cancellationToken);

        return await DescribeAsync(year, company, today, cancellationToken);
    }

    private async Task<FiscalYearDto> DescribeAsync(int year, CompanyInfo company, DateOnly today, CancellationToken cancellationToken)
    {
        var (start, end) = FiscalYear.Range(year, company.FiscalYearStartMonth);
        var types = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id, a => a.Type);
        var totals = await ledger.OperatingTotalsAsync(start, end, cancellationToken);
        var profit = totals.Where(t => types.GetValueOrDefault(t.AccountId) == AccountType.Revenue).Sum(t => t.Credit - t.Debit)
            - totals.Where(t => types.GetValueOrDefault(t.AccountId) == AccountType.Expense).Sum(t => t.Debit - t.Credit);

        var closing = (await vouchers.SearchAsync(new VoucherSearch(VoucherKind.Closing, VoucherStatus.Posted, start, end, 1), cancellationToken)).FirstOrDefault();
        var drafts = await vouchers.SearchAsync(new VoucherSearch(Status: VoucherStatus.Draft, From: start, To: end), cancellationToken);
        return new FiscalYearDto(year, start, end, today > end, closing is not null, profit, drafts.Count, closing?.Id);
    }

    private CompanyInfo Company() => files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");

    private string BackupPathFor(string companyPath, int year)
    {
        var folder = Path.GetDirectoryName(companyPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(companyPath);
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(folder, $"{name}.before-close-{year}-{stamp}.baba");
    }
}
