namespace Baba.Domain.Accounting;

/// <summary>
/// One finished bank reconciliation (brief section 10.2): on <see cref="StatementDate"/> the bank said the balance was
/// <see cref="StatementBalance"/>, and the entries ticked off here add up to exactly that.
/// </summary>
public sealed class BankReconciliation : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly StatementDate { get; set; }
    public long StatementBalanceScaled { get; set; }
    public DateTime CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public decimal StatementBalance
    {
        get => Scaled.ToDecimal(StatementBalanceScaled);
        set => StatementBalanceScaled = Scaled.ToScaled(value);
    }
}

/// <summary>
/// A ledger entry that has been checked against a bank statement. The entry's own id is kept as <see cref="LedgerEntryId"/>; an entry
/// is reconciled at most once. A voucher with a reconciled entry cannot be changed or deleted until the reconciliation is undone.
/// </summary>
public sealed class ReconciledEntry : Entity, ICompanyScoped, INotAudited
{
    public Guid CompanyId { get; set; }
    public Guid ReconciliationId { get; set; }
    public Guid LedgerEntryId { get; set; }
}

/// <summary>
/// A line of a bank statement that was imported from a CSV or Excel file, to help match the bank's records with the ledger.
/// The amount is signed: positive for money in, negative for money out.
/// </summary>
public sealed class BankStatementLine : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }
    public long AmountScaled { get; set; }

    /// <summary>The file this line came from, so a wrong import can be taken back as a whole.</summary>
    public Guid BatchId { get; set; }

    /// <summary>The reconciliation that used this line, once there is one.</summary>
    public Guid? ReconciliationId { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public decimal Amount
    {
        get => Scaled.ToDecimal(AmountScaled);
        set => AmountScaled = Scaled.ToScaled(value);
    }
}
