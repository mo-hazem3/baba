using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Application.Payroll;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Claims;

namespace Baba.Application.Claims;

public interface IClaimStore
{
    Task<IReadOnlyList<ExpenseClaim>> ListAsync(CancellationToken cancellationToken = default);
    Task<ExpenseClaim?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ExpenseClaim claim, CancellationToken cancellationToken = default);

    /// <summary>Saves the claim and replaces its lines with the ones on the object.</summary>
    Task UpdateAsync(ExpenseClaim claim, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record ClaimLineInput(DateOnly Date, string? Description, Guid AccountId, decimal Amount, Guid? CostCenterId = null);

public sealed record ClaimInput(Guid EmployeeId, DateOnly Date, string? Memo, IReadOnlyList<ClaimLineInput> Lines);

public sealed record ClaimLineDto(DateOnly Date, string? Description, Guid AccountId, decimal Amount, Guid? CostCenterId);

public sealed record ClaimDto(
    Guid Id,
    string Number,
    Guid EmployeeId,
    DateOnly Date,
    string? Memo,
    ClaimStatus Status,
    decimal Total,
    string? RejectionReason,
    Guid? VoucherId,
    Guid? PaymentVoucherId,
    DateOnly? PaidDate,
    IReadOnlyList<ClaimLineDto> Lines);

public sealed record PayClaimInput(DateOnly Date, Guid CashAccountId);

/// <summary>
/// Expense claims (brief section 10.4): an employee writes down what they spent, sends it, someone approves it, and approving it posts the
/// expenses against what the company owes the employee; paying it clears that. (Who may approve is decided with users and permissions.)
/// </summary>
public sealed class ClaimService(
    IClaimStore store,
    IPayrollStore employees,
    IAccountStore accounts,
    ICostCenterStore costCenters,
    VoucherService vouchers,
    ICompanyFiles files,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<ClaimDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await store.ListAsync(cancellationToken)).OrderByDescending(c => c.Date).ThenByDescending(c => c.Number, StringComparer.Ordinal).Select(ToDto).ToList();

    public async Task<ClaimDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await store.FindAsync(id, cancellationToken) is { } claim ? ToDto(claim) : null;

    /// <summary>Saves a draft: a new claim, or a draft or rejected one (a rejected claim that is changed becomes a draft again).</summary>
    public async Task<ClaimDto> SaveAsync(Guid? id, ClaimInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var existing = id is { } value ? await store.FindAsync(value, cancellationToken) ?? throw new NotFoundException("claim") : null;
        if (existing is { Status: not (ClaimStatus.Draft or ClaimStatus.Rejected) })
            throw Refused("claim", "claim.not-draft");

        var issues = new List<ValidationIssue>();
        var employee = await employees.FindEmployeeAsync(input.EmployeeId, cancellationToken);
        if (employee is null)
            issues.Add(new("employee", "claim.employee-unknown"));
        if (input.Date == default)
            issues.Add(new("date", "claim.date-required"));
        if (input.Lines is null || input.Lines.Count == 0)
            issues.Add(new("lines", "claim.lines-required"));

        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var centers = (await costCenters.ListAsync(cancellationToken)).Select(c => c.Id).ToHashSet();
        var lines = new List<ExpenseClaimLine>();
        for (var i = 0; i < (input.Lines?.Count ?? 0); i++)
        {
            var line = input.Lines![i];
            if (!chart.TryGetValue(line.AccountId, out var account) || !account.IsPosting || !account.IsActive || account.Type is not (AccountType.Expense or AccountType.Asset))
                issues.Add(new($"lines[{i}].account", "claim.account-invalid"));
            if (line.Amount <= 0)
                issues.Add(new($"lines[{i}].amount", "claim.amount-invalid"));
            if (line.CostCenterId is { } center && center != Guid.Empty && !centers.Contains(center))
                issues.Add(new($"lines[{i}].costCenter", "claim.cost-center-unknown"));
            lines.Add(new ExpenseClaimLine
            {
                CompanyId = company.Id, LineNumber = i + 1, Date = line.Date == default ? input.Date : line.Date, Description = string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim(),
                AccountId = line.AccountId, Amount = line.Amount, CostCenterId = line.CostCenterId == Guid.Empty ? null : line.CostCenterId,
            });
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        var claim = existing ?? new ExpenseClaim { CompanyId = company.Id, CreatedAt = clock.GetUtcNow().UtcDateTime };
        foreach (var line in lines)
            line.ClaimId = claim.Id;
        claim.EmployeeId = input.EmployeeId;
        claim.Date = input.Date;
        claim.Memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim();
        claim.Status = ClaimStatus.Draft;
        claim.RejectionReason = null;
        claim.Lines = lines;

        if (existing is null)
        {
            claim.Number = await NextNumberAsync(input.Date, company.FiscalYearStartMonth, cancellationToken);
            await store.AddAsync(claim, cancellationToken);
        }
        else
        {
            await store.UpdateAsync(claim, cancellationToken);
        }

        return ToDto(claim);
    }

    public async Task<ClaimDto> SubmitAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var claim = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("claim");
        if (claim.Status != ClaimStatus.Draft)
            throw Refused("claim", "claim.not-draft");

        claim.Status = ClaimStatus.Submitted;
        claim.SubmittedAt = clock.GetUtcNow().UtcDateTime;
        await store.UpdateAsync(claim, cancellationToken);
        return ToDto(claim);
    }

    /// <summary>Approves a submitted claim: the expenses are posted against what the company owes the employee.</summary>
    public async Task<ClaimDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var claim = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("claim");
        if (claim.Status != ClaimStatus.Submitted)
            throw Refused("claim", "claim.not-submitted");

        var payable = await PayableAccountAsync(cancellationToken);
        var employee = await employees.FindEmployeeAsync(claim.EmployeeId, cancellationToken);
        var who = employee is null ? "" : $"{employee.Code} {employee.NameEn}".Trim();
        var lines = claim.Lines.OrderBy(l => l.LineNumber)
            .Select(l => new VoucherLineInput(null, l.AccountId, $"{l.Description ?? claim.Number} — {who}", l.Amount, 0, null, l.CostCenterId)).ToList();
        lines.Add(new VoucherLineInput(null, payable, $"{claim.Number} — {who}", 0, claim.Total));

        var voucher = await vouchers.SaveAndPostSystemAsync(
            new VoucherInput(VoucherKind.ExpenseClaim, claim.Date, null, claim.Number, $"Expense claim {claim.Number}", lines), claim.VoucherId, null, cancellationToken);
        claim.VoucherId = voucher.Id;
        claim.Status = ClaimStatus.Approved;
        claim.DecidedAt = clock.GetUtcNow().UtcDateTime;
        await store.UpdateAsync(claim, cancellationToken);
        return ToDto(claim);
    }

    public async Task<ClaimDto> RejectAsync(Guid id, string? reason, CancellationToken cancellationToken = default)
    {
        var claim = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("claim");
        if (claim.Status != ClaimStatus.Submitted)
            throw Refused("claim", "claim.not-submitted");

        claim.Status = ClaimStatus.Rejected;
        claim.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        claim.DecidedAt = clock.GetUtcNow().UtcDateTime;
        await store.UpdateAsync(claim, cancellationToken);
        return ToDto(claim);
    }

    /// <summary>Takes an approval back: the entry is removed and the claim goes back to waiting for a decision (not once it has been paid).</summary>
    public async Task<ClaimDto> UnapproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var claim = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("claim");
        if (claim.Status == ClaimStatus.Paid)
            throw Refused("claim", "claim.paid");
        if (claim.Status != ClaimStatus.Approved)
            throw Refused("claim", "claim.not-approved");

        if (claim.VoucherId is { } voucherId)
            await vouchers.DeleteSystemAsync(voucherId, cancellationToken);
        claim.VoucherId = null;
        claim.Status = ClaimStatus.Submitted;
        claim.DecidedAt = null;
        await store.UpdateAsync(claim, cancellationToken);
        return ToDto(claim);
    }

    /// <summary>Pays an approved claim back to the employee out of a bank or cash account.</summary>
    public async Task<ClaimDto> PayAsync(Guid id, PayClaimInput input, CancellationToken cancellationToken = default)
    {
        var claim = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("claim");
        if (claim.Status == ClaimStatus.Paid)
            throw Refused("claim", "claim.paid");
        if (claim.Status != ClaimStatus.Approved)
            throw Refused("claim", "claim.not-approved");

        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        if (!chart.TryGetValue(input.CashAccountId, out var cash) || cash.Role != AccountRole.CashOrBank || !cash.IsPosting || !cash.IsActive)
            throw Refused("cashAccount", "claim.cash-account-invalid");

        var payable = await PayableAccountAsync(cancellationToken);
        var memo = $"Expense claim {claim.Number}";
        var voucher = await vouchers.SaveAndPostSystemAsync(
            new VoucherInput(VoucherKind.ClaimPayment, input.Date, null, claim.Number, memo,
                [new VoucherLineInput(null, payable, memo, claim.Total, 0), new VoucherLineInput(null, input.CashAccountId, memo, 0, claim.Total)]),
            null, null, cancellationToken);
        claim.PaymentVoucherId = voucher.Id;
        claim.PaidDate = input.Date;
        claim.Status = ClaimStatus.Paid;
        await store.UpdateAsync(claim, cancellationToken);
        return ToDto(claim);
    }

    public async Task<ClaimDto> UnpayAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var claim = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("claim");
        if (claim.Status != ClaimStatus.Paid || claim.PaymentVoucherId is not { } voucherId)
            throw Refused("claim", "claim.not-paid");

        await vouchers.DeleteSystemAsync(voucherId, cancellationToken);
        claim.PaymentVoucherId = null;
        claim.PaidDate = null;
        claim.Status = ClaimStatus.Approved;
        await store.UpdateAsync(claim, cancellationToken);
        return ToDto(claim);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var claim = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("claim");
        if (claim.Status is not (ClaimStatus.Draft or ClaimStatus.Rejected))
            throw Refused("claim", "claim.not-draft");
        await store.DeleteAsync(id, cancellationToken);
    }

    // ---------------------------------------------------------------- Helpers

    /// <summary>What the company owes employees for approved claims: the account with that special use.</summary>
    private async Task<Guid> PayableAccountAsync(CancellationToken cancellationToken) =>
        (await accounts.ListAsync(cancellationToken)).Where(a => a is { Role: AccountRole.ExpenseClaimsPayable, IsPosting: true, IsActive: true }).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Id
        ?? throw Refused("accounts", "claim.accounts-missing");

    private async Task<string> NextNumberAsync(DateOnly date, int fiscalYearStartMonth, CancellationToken cancellationToken)
    {
        var year = FiscalYear.Of(date, fiscalYearStartMonth);
        var prefix = $"EC-{year}-";
        var last = (await store.ListAsync(cancellationToken)).Where(c => c.Number.StartsWith(prefix, StringComparison.Ordinal))
            .Select(c => int.TryParse(c.Number[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{last + 1:0000}";
    }

    private static ClaimDto ToDto(ExpenseClaim c) => new(
        c.Id, c.Number, c.EmployeeId, c.Date, c.Memo, c.Status, c.Total, c.RejectionReason, c.VoucherId, c.PaymentVoucherId, c.PaidDate,
        [.. c.Lines.OrderBy(l => l.LineNumber).Select(l => new ClaimLineDto(l.Date, l.Description, l.AccountId, l.Amount, l.CostCenterId))]);

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);
}
