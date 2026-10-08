using System.Text.Json;
using Baba.Application.Abstractions;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Assets;
using Baba.Domain.Budgets;
using Baba.Domain.Claims;
using Baba.Domain.Inventory;
using Baba.Domain.Security;
using Baba.Domain.Payroll;
using Baba.Domain.Trade;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Baba.Infrastructure.Persistence;

/// <summary>
/// The data of one company. Every query is limited to the open company, timestamps and user ids are filled in
/// automatically, and every change is written to the audit log in the same save.
/// </summary>
public sealed class CompanyDbContext(
    DbContextOptions<CompanyDbContext> options,
    ICurrentUser currentUser,
    TimeProvider clock,
    CompanyScope scope) : DbContext(options)
{
    private Action? _onDisposed;

    /// <summary>Called once when the context is disposed. The company file manager uses it to give back the place this context held.</summary>
    public void OnDisposed(Action callback) => _onDisposed = callback;

    public override void Dispose()
    {
        base.Dispose();
        Interlocked.Exchange(ref _onDisposed, null)?.Invoke();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Interlocked.Exchange(ref _onDisposed, null)?.Invoke();
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherLine> VoucherLines => Set<VoucherLine>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Period> Periods => Set<Period>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<PrintSettings> PrintSettings => Set<PrintSettings>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<BankReconciliation> BankReconciliations => Set<BankReconciliation>();
    public DbSet<ReconciledEntry> ReconciledEntries => Set<ReconciledEntry>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<CurrencyRate> CurrencyRates => Set<CurrencyRate>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();
    public DbSet<StockDocumentLine> StockDocumentLines => Set<StockDocumentLine>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();
    public DbSet<ExpenseClaimLine> ExpenseClaimLines => Set<ExpenseClaimLine>();
    public DbSet<BudgetEntry> BudgetEntries => Set<BudgetEntry>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeComponent> EmployeeComponents => Set<EmployeeComponent>();
    public DbSet<SalaryComponent> SalaryComponents => Set<SalaryComponent>();
    public DbSet<PayrollSettings> PayrollSettings => Set<PayrollSettings>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<PayslipItem> PayslipItems => Set<PayslipItem>();
    public DbSet<LeaveRecord> LeaveRecords => Set<LeaveRecord>();
    public DbSet<EndOfServiceAccrual> EndOfServiceAccruals => Set<EndOfServiceAccrual>();
    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
    public DbSet<AssetDepreciation> AssetDepreciations => Set<AssetDepreciation>();
    public DbSet<RecurringSchedule> RecurringSchedules => Set<RecurringSchedule>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentLine> DocumentLines => Set<DocumentLine>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListLine> PriceListLines => Set<PriceListLine>();

    // Read by the query filters below (EF turns it into a parameter per context instance).
    private Guid CurrentCompanyId => scope.CompanyId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        configuration.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configuration.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configuration.Properties<AccountType>().HaveConversion<string>();
        configuration.Properties<AuditAction>().HaveConversion<string>();
        configuration.Properties<AccountRole>().HaveConversion<string>();
        configuration.Properties<VoucherKind>().HaveConversion<string>();
        configuration.Properties<VoucherStatus>().HaveConversion<string>();
        configuration.Properties<PartyKind>().HaveConversion<string>();
        configuration.Properties<DocumentKind>().HaveConversion<string>();
        configuration.Properties<DocumentStatus>().HaveConversion<string>();
        configuration.Properties<RecurrenceFrequency>().HaveConversion<string>();
        configuration.Properties<TaxTreatment>().HaveConversion<string>();
        configuration.Properties<StockMovementKind>().HaveConversion<string>();
        configuration.Properties<StockDocumentKind>().HaveConversion<string>();
        configuration.Properties<ClaimStatus>().HaveConversion<string>();
        configuration.Properties<SalaryComponentKind>().HaveConversion<string>();
        configuration.Properties<ComponentCalculation>().HaveConversion<string>();
        configuration.Properties<PayrollStatus>().HaveConversion<string>();
        configuration.Properties<LeaveKind>().HaveConversion<string>();
        configuration.Properties<AssetKind>().HaveConversion<string>();
        configuration.Properties<DepreciationMethod>().HaveConversion<string>();
        configuration.Properties<AssetStatus>().HaveConversion<string>();
        configuration.Properties<CostMode>().HaveConversion<string>();
        configuration.Properties<RecurringTemplate>().HaveConversion<string>();
        configuration.Properties<PrintLayout>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Company>(company =>
        {
            company.Property(c => c.TaxNumbers).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<Dictionary<string, string>>(
                    (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                    v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
                    v => new Dictionary<string, string>(v)));

            company.Property(c => c.EnabledModules).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<List<string>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s)),
                    v => v.ToList()));
        });

        model.Entity<Account>(account =>
        {
            account.HasIndex(a => new { a.CompanyId, a.Code }).IsUnique();
            account.HasOne<Account>().WithMany().HasForeignKey(a => a.ParentId).OnDelete(DeleteBehavior.Restrict);
            account.Ignore(a => a.IsDebitNormal);
            account.HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        });

        // Money is stored as scaled integers (ADR 0002): only the long columns are mapped, the decimal views are ignored.
        model.Entity<Voucher>(voucher =>
        {
            voucher.Ignore(v => v.ExchangeRate).Ignore(v => v.TotalDebit).Ignore(v => v.TotalCredit);
            voucher.HasMany(v => v.Lines).WithOne().HasForeignKey(l => l.VoucherId).OnDelete(DeleteBehavior.Cascade);
            voucher.HasOne<Account>().WithMany().HasForeignKey(v => v.CashAccountId).OnDelete(DeleteBehavior.Restrict);
            // A number is unique once given; drafts have none.
            voucher.HasIndex(v => new { v.CompanyId, v.Number }).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            voucher.HasIndex(v => v.Date);
            voucher.HasQueryFilter(v => v.CompanyId == CurrentCompanyId);
        });

        model.Entity<Party>(party =>
        {
            party.Ignore(p => p.CreditLimit);
            party.HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
            party.HasQueryFilter(p => p.CompanyId == CurrentCompanyId);
        });

        model.Entity<CostCenter>(costCenter =>
        {
            costCenter.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
            costCenter.HasQueryFilter(c => c.CompanyId == CurrentCompanyId);
        });

        model.Entity<Product>(product =>
        {
            product.Ignore(p => p.SalePrice).Ignore(p => p.PurchasePrice).Ignore(p => p.ReorderLevel);
            product.HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
            product.HasOne<Account>().WithMany().HasForeignKey(p => p.SalesAccountId).OnDelete(DeleteBehavior.Restrict);
            product.HasOne<Account>().WithMany().HasForeignKey(p => p.PurchaseAccountId).OnDelete(DeleteBehavior.Restrict);
            product.HasQueryFilter(p => p.CompanyId == CurrentCompanyId);
        });

        model.Entity<PriceList>(list =>
        {
            list.HasMany(l => l.Lines).WithOne().HasForeignKey(l => l.PriceListId).OnDelete(DeleteBehavior.Cascade);
            list.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });

        model.Entity<PriceListLine>(line =>
        {
            line.Ignore(l => l.Price);
            line.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            line.HasIndex(l => new { l.PriceListId, l.ProductId }).IsUnique();
            line.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });

        model.Entity<Party>().HasOne<PriceList>().WithMany().HasForeignKey(p => p.PriceListId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<Document>(document =>
        {
            document.Ignore(d => d.ExchangeRate).Ignore(d => d.DiscountPercent);
            document.HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Cascade);
            document.HasOne<Party>().WithMany().HasForeignKey(d => d.PartyId).OnDelete(DeleteBehavior.Restrict);
            // A number is unique once given; drafts have none. The links to other documents and to the voucher are plain ids.
            document.HasIndex(d => new { d.CompanyId, d.Number }).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            document.HasIndex(d => d.Date);
            document.HasIndex(d => d.PartyId);
            document.HasIndex(d => d.VoucherId);
            document.HasQueryFilter(d => d.CompanyId == CurrentCompanyId);
        });

        model.Entity<DocumentLine>(line =>
        {
            line.Ignore(l => l.Quantity).Ignore(l => l.UnitPrice).Ignore(l => l.DiscountPercent).Ignore(l => l.TaxRate);
            line.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<CostCenter>().WithMany().HasForeignKey(l => l.CostCenterId).OnDelete(DeleteBehavior.Restrict);
            line.HasIndex(l => new { l.DocumentId, l.LineNumber });
            line.HasIndex(l => l.ProductId);
            line.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });

        model.Entity<Voucher>().HasIndex(v => v.DocumentId);

        model.Entity<TaxCode>(code =>
        {
            code.Ignore(c => c.Rate);
            code.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
            code.HasOne<Account>().WithMany().HasForeignKey(c => c.OutputAccountId).OnDelete(DeleteBehavior.Restrict);
            code.HasOne<Account>().WithMany().HasForeignKey(c => c.InputAccountId).OnDelete(DeleteBehavior.Restrict);
            code.HasQueryFilter(c => c.CompanyId == CurrentCompanyId);
        });

        model.Entity<DocumentLine>().HasOne<TaxCode>().WithMany().HasForeignKey(l => l.TaxCodeId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Product>().HasOne<TaxCode>().WithMany().HasForeignKey(p => p.TaxCodeId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<Warehouse>(warehouse =>
        {
            warehouse.HasIndex(w => new { w.CompanyId, w.Code }).IsUnique();
            warehouse.HasQueryFilter(w => w.CompanyId == CurrentCompanyId);
        });

        model.Entity<StockMovement>(movement =>
        {
            movement.Ignore(m => m.Quantity).Ignore(m => m.Value);
            movement.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
            movement.HasOne<Warehouse>().WithMany().HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            movement.HasIndex(m => m.ProductId);
            movement.HasIndex(m => m.DocumentId);
            movement.HasIndex(m => m.StockDocumentId);
            movement.HasIndex(m => m.Date);
            movement.HasQueryFilter(m => m.CompanyId == CurrentCompanyId);
        });

        model.Entity<StockDocument>(document =>
        {
            document.HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.StockDocumentId).OnDelete(DeleteBehavior.Cascade);
            document.HasOne<Account>().WithMany().HasForeignKey(d => d.CounterAccountId).OnDelete(DeleteBehavior.Restrict);
            document.HasIndex(d => new { d.CompanyId, d.Number }).IsUnique();
            document.HasIndex(d => d.Date);
            document.HasQueryFilter(d => d.CompanyId == CurrentCompanyId);
        });

        model.Entity<StockDocumentLine>(line =>
        {
            line.Ignore(l => l.Quantity).Ignore(l => l.UnitCost);
            line.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<Warehouse>().WithMany().HasForeignKey(l => l.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<Warehouse>().WithMany().HasForeignKey(l => l.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
            line.HasIndex(l => new { l.StockDocumentId, l.LineNumber });
            line.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });

        model.Entity<Product>().HasOne<Account>().WithMany().HasForeignKey(p => p.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Product>().HasOne<Account>().WithMany().HasForeignKey(p => p.CostOfSalesAccountId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Product>().HasIndex(p => p.Barcode);
        model.Entity<Document>().HasOne<Warehouse>().WithMany().HasForeignKey(d => d.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<Role>(role =>
        {
            role.Ignore(r => r.Permissions);
            role.HasIndex(r => new { r.CompanyId, r.NameEn }).IsUnique();
            role.HasIndex(r => new { r.CompanyId, r.BuiltInKey });
            role.HasQueryFilter(r => r.CompanyId == CurrentCompanyId);
        });

        model.Entity<AppUser>(user =>
        {
            user.Property(u => u.UserName).UseCollation("NOCASE");
            user.HasOne<Role>().WithMany().HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);
            user.HasIndex(u => new { u.CompanyId, u.UserName }).IsUnique();
            user.HasQueryFilter(u => u.CompanyId == CurrentCompanyId);
        });

        model.Entity<ExpenseClaim>(claim =>
        {
            claim.Ignore(c => c.Total);
            claim.HasMany(c => c.Lines).WithOne().HasForeignKey(l => l.ClaimId).OnDelete(DeleteBehavior.Cascade);
            claim.HasOne<Employee>().WithMany().HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            claim.HasIndex(c => new { c.CompanyId, c.Number }).IsUnique();
            claim.HasQueryFilter(c => c.CompanyId == CurrentCompanyId);
        });

        model.Entity<ExpenseClaimLine>(line =>
        {
            line.Ignore(l => l.Amount);
            line.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<CostCenter>().WithMany().HasForeignKey(l => l.CostCenterId).OnDelete(DeleteBehavior.Restrict);
            line.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });

        model.Entity<BudgetEntry>(entry =>
        {
            entry.Ignore(e => e.Amount);
            entry.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<CostCenter>().WithMany().HasForeignKey(e => e.CostCenterId).OnDelete(DeleteBehavior.Restrict);
            entry.HasIndex(e => new { e.CompanyId, e.FiscalYear, e.AccountId, e.CostCenterId, e.Period }).IsUnique();
            entry.HasQueryFilter(e => e.CompanyId == CurrentCompanyId);
        });

        model.Entity<Employee>(employee =>
        {
            employee.Ignore(e => e.BasicSalary).Ignore(e => e.LeaveBalanceDays);
            employee.HasMany(e => e.Components).WithOne().HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            employee.HasOne<CostCenter>().WithMany().HasForeignKey(e => e.CostCenterId).OnDelete(DeleteBehavior.Restrict);
            employee.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique();
            employee.HasQueryFilter(e => e.CompanyId == CurrentCompanyId);
        });

        model.Entity<SalaryComponent>(component =>
        {
            component.Ignore(c => c.DefaultValue);
            component.HasOne<Account>().WithMany().HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Restrict);
            component.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
            component.HasQueryFilter(c => c.CompanyId == CurrentCompanyId);
        });

        model.Entity<EmployeeComponent>(row =>
        {
            row.Ignore(c => c.Value);
            row.HasOne<SalaryComponent>().WithMany().HasForeignKey(c => c.ComponentId).OnDelete(DeleteBehavior.Restrict);
            row.HasQueryFilter(c => c.CompanyId == CurrentCompanyId);
        });

        model.Entity<PayrollSettings>(settings =>
        {
            settings.HasOne<Account>().WithMany().HasForeignKey(s => s.SalaryExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
            settings.HasOne<Account>().WithMany().HasForeignKey(s => s.SalariesPayableAccountId).OnDelete(DeleteBehavior.Restrict);
            settings.HasOne<Account>().WithMany().HasForeignKey(s => s.InsuranceExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
            settings.HasOne<Account>().WithMany().HasForeignKey(s => s.InsurancePayableAccountId).OnDelete(DeleteBehavior.Restrict);
            settings.HasOne<Account>().WithMany().HasForeignKey(s => s.EndOfServiceExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
            settings.HasOne<Account>().WithMany().HasForeignKey(s => s.EndOfServiceProvisionAccountId).OnDelete(DeleteBehavior.Restrict);
            settings.HasQueryFilter(s => s.CompanyId == CurrentCompanyId);
        });

        model.Entity<PayrollRun>(run =>
        {
            run.HasMany(r => r.Payslips).WithOne().HasForeignKey(p => p.RunId).OnDelete(DeleteBehavior.Cascade);
            run.HasIndex(r => new { r.CompanyId, r.Month }).IsUnique();
            run.HasQueryFilter(r => r.CompanyId == CurrentCompanyId);
        });

        model.Entity<Payslip>(payslip =>
        {
            payslip.Ignore(p => p.EmployeeInsurance).Ignore(p => p.EmployerInsurance).Ignore(p => p.Earnings).Ignore(p => p.Deductions).Ignore(p => p.Net);
            payslip.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.PayslipId).OnDelete(DeleteBehavior.Cascade);
            payslip.HasOne<Employee>().WithMany().HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            payslip.HasQueryFilter(p => p.CompanyId == CurrentCompanyId);
        });

        model.Entity<PayslipItem>(item =>
        {
            item.Ignore(i => i.Amount);
            item.HasOne<Account>().WithMany().HasForeignKey(i => i.AccountId).OnDelete(DeleteBehavior.Restrict);
            item.HasOne<SalaryComponent>().WithMany().HasForeignKey(i => i.ComponentId).OnDelete(DeleteBehavior.Restrict);
            item.HasQueryFilter(i => i.CompanyId == CurrentCompanyId);
        });

        model.Entity<LeaveRecord>(leave =>
        {
            leave.Ignore(l => l.Days);
            leave.HasOne<Employee>().WithMany().HasForeignKey(l => l.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            leave.HasIndex(l => l.EmployeeId);
            leave.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });

        model.Entity<EndOfServiceAccrual>(accrual =>
        {
            accrual.Ignore(a => a.Amount);
            accrual.HasIndex(a => new { a.CompanyId, a.Month }).IsUnique();
            accrual.HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        });

        model.Entity<FixedAsset>(asset =>
        {
            asset.Ignore(a => a.Cost).Ignore(a => a.Salvage).Ignore(a => a.AnnualRate).Ignore(a => a.OpeningAccumulated).Ignore(a => a.DisposalProceeds);
            asset.HasOne<Account>().WithMany().HasForeignKey(a => a.AssetAccountId).OnDelete(DeleteBehavior.Restrict);
            asset.HasOne<Account>().WithMany().HasForeignKey(a => a.AccumulatedAccountId).OnDelete(DeleteBehavior.Restrict);
            asset.HasOne<Account>().WithMany().HasForeignKey(a => a.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
            asset.HasOne<CostCenter>().WithMany().HasForeignKey(a => a.CostCenterId).OnDelete(DeleteBehavior.Restrict);
            asset.HasIndex(a => new { a.CompanyId, a.Code }).IsUnique();
            asset.HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        });

        model.Entity<AssetDepreciation>(row =>
        {
            row.Ignore(r => r.Amount);
            row.HasOne<FixedAsset>().WithMany().HasForeignKey(r => r.AssetId).OnDelete(DeleteBehavior.Restrict);
            row.HasIndex(r => new { r.AssetId, r.Month }).IsUnique();
            row.HasIndex(r => r.VoucherId);
            row.HasQueryFilter(r => r.CompanyId == CurrentCompanyId);
        });

        model.Entity<RecurringSchedule>(schedule =>
        {
            schedule.HasIndex(s => s.NextRunDate);
            schedule.HasQueryFilter(s => s.CompanyId == CurrentCompanyId);
        });

        model.Entity<Allocation>(allocation =>
        {
            allocation.Ignore(a => a.Amount).Ignore(a => a.InvoiceBase).Ignore(a => a.PaymentBase);
            // Deleting the receipt or payment takes its allocations with it; an invoice that has been paid cannot be deleted.
            allocation.HasOne<Voucher>().WithMany().HasForeignKey(a => a.PaymentVoucherId).OnDelete(DeleteBehavior.Cascade);
            allocation.HasOne<Document>().WithMany().HasForeignKey(a => a.DocumentId).OnDelete(DeleteBehavior.Restrict);
            allocation.HasIndex(a => a.PaymentVoucherId);
            allocation.HasIndex(a => a.DocumentId);
            allocation.HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        });

        model.Entity<CurrencyRate>(rate =>
        {
            rate.Ignore(r => r.Rate);
            rate.HasIndex(r => new { r.CompanyId, r.CurrencyCode, r.Date }).IsUnique();
            rate.HasQueryFilter(r => r.CompanyId == CurrentCompanyId);
        });

        model.Entity<BankReconciliation>(reconciliation =>
        {
            reconciliation.Ignore(r => r.StatementBalance);
            reconciliation.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
            reconciliation.HasIndex(r => new { r.AccountId, r.StatementDate });
            reconciliation.HasQueryFilter(r => r.CompanyId == CurrentCompanyId);
        });

        model.Entity<ReconciledEntry>(reconciled =>
        {
            reconciled.HasOne<BankReconciliation>().WithMany().HasForeignKey(r => r.ReconciliationId).OnDelete(DeleteBehavior.Cascade);
            // An entry that has been checked against a statement cannot be removed from under it.
            reconciled.HasOne<LedgerEntry>().WithMany().HasForeignKey(r => r.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
            reconciled.HasIndex(r => r.LedgerEntryId).IsUnique();
            reconciled.HasQueryFilter(r => r.CompanyId == CurrentCompanyId);
        });

        model.Entity<BankStatementLine>(line =>
        {
            line.Ignore(l => l.Amount);
            line.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<BankReconciliation>().WithMany().HasForeignKey(l => l.ReconciliationId).OnDelete(DeleteBehavior.SetNull);
            line.HasIndex(l => new { l.AccountId, l.Date });
            line.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });

        model.Entity<VoucherLine>(line =>
        {
            line.Ignore(l => l.Debit).Ignore(l => l.Credit);
            line.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<Party>().WithMany().HasForeignKey(l => l.PartyId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<CostCenter>().WithMany().HasForeignKey(l => l.CostCenterId).OnDelete(DeleteBehavior.Restrict);
            line.HasIndex(l => new { l.VoucherId, l.LineNumber });
            line.HasIndex(l => l.AccountId);
            line.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });

        model.Entity<LedgerEntry>(entry =>
        {
            entry.Ignore(e => e.Debit).Ignore(e => e.Credit).Ignore(e => e.BaseDebit).Ignore(e => e.BaseCredit);
            entry.HasOne<Voucher>().WithMany().HasForeignKey(e => e.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entry.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<Party>().WithMany().HasForeignKey(e => e.PartyId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<CostCenter>().WithMany().HasForeignKey(e => e.CostCenterId).OnDelete(DeleteBehavior.Restrict);
            entry.HasIndex(e => new { e.PartyId, e.Date });
            entry.HasIndex(e => new { e.AccountId, e.Date });
            entry.HasIndex(e => e.Date);
            entry.HasIndex(e => e.VoucherId);
            entry.HasQueryFilter(e => e.CompanyId == CurrentCompanyId);
        });

        model.Entity<Period>(period =>
        {
            period.HasIndex(p => new { p.CompanyId, p.Start }).IsUnique();
            period.HasQueryFilter(p => p.CompanyId == CurrentCompanyId);
        });

        model.Entity<PrintSettings>(settings =>
        {
            settings.HasIndex(s => s.CompanyId).IsUnique(); // one print template per company
            settings.HasQueryFilter(s => s.CompanyId == CurrentCompanyId);
        });

        model.Entity<NumberSequence>(sequence =>
        {
            sequence.HasIndex(s => new { s.CompanyId, s.Kind, s.FiscalYear }).IsUnique();
            sequence.HasQueryFilter(s => s.CompanyId == CurrentCompanyId);
        });

        model.Entity<StoredFile>().HasQueryFilter(f => f.CompanyId == CurrentCompanyId);

        model.Entity<AuditLogEntry>(log =>
        {
            log.HasIndex(l => new { l.EntityName, l.EntityId });
            log.HasIndex(l => l.At);
            log.HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Stamps company id, timestamps and user ids, and adds the audit log rows for this save.</summary>
    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow().UtcDateTime;
        var user = currentUser.UserId;
        var auditRows = new List<AuditLogEntry>();

        var changed = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditLogEntry)
            .ToList();

        foreach (var entry in changed)
        {
            if (entry is { State: EntityState.Added, Entity: ICompanyScoped scoped } && scoped.CompanyId == Guid.Empty)
                scoped.CompanyId = CurrentCompanyId;

            if (entry.Entity is IAuditable auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = now;
                    auditable.CreatedBy = user;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = user;
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                }
            }

            // Derived data (ledger entries, number counters) is not logged row by row: the voucher itself is.
            if (entry.Entity is not INotAudited && CreateAuditRow(entry, now, user) is { } row)
                auditRows.Add(row);
        }

        AuditLog.AddRange(auditRows);
    }

    private AuditLogEntry? CreateAuditRow(EntityEntry entry, DateTime now, string user)
    {
        var id = (Guid)entry.Property(nameof(Entity.Id)).CurrentValue!;
        var companyId = entry.Entity is Company ? id : CurrentCompanyId;

        // Bookkeeping columns are not interesting in the log.
        static bool IsAuditColumn(string name) =>
            name is nameof(IAuditable.CreatedAt) or nameof(IAuditable.CreatedBy)
                or nameof(IAuditable.UpdatedAt) or nameof(IAuditable.UpdatedBy);

        // Secrets never go in the log (a changed password is logged as "changed"), and a sign-in time is not a change worth logging.
        static bool IsHiddenColumn(string name) => name is "PasswordHash" or "PasswordSalt" or "PasswordIterations" or "LastSignInAt";

        var properties = entry.Properties.Where(p => !IsAuditColumn(p.Metadata.Name) && !IsHiddenColumn(p.Metadata.Name)).ToList();
        var passwordChanged = entry.State == EntityState.Modified && entry.Properties.Any(p => p.Metadata.Name == "PasswordHash" && p.IsModified);

        object? Describe(object? value) => value is byte[] bytes ? $"[{bytes.Length} bytes]" : value;

        Dictionary<string, object?> Snapshot(IEnumerable<PropertyEntry> source, bool original) =>
            source.ToDictionary(p => p.Metadata.Name, p => Describe(original ? p.OriginalValue : p.CurrentValue));

        string? before = null, after = null;
        var action = AuditAction.Created;
        switch (entry.State)
        {
            case EntityState.Added:
                after = JsonSerializer.Serialize(Snapshot(properties, original: false));
                break;
            case EntityState.Deleted:
                action = AuditAction.Deleted;
                before = JsonSerializer.Serialize(Snapshot(properties, original: true));
                break;
            default:
                action = AuditAction.Updated;
                var modified = properties.Where(p => p.IsModified).ToList();
                if (modified.Count == 0 && !passwordChanged)
                    return null; // only hidden columns changed: nothing to show
                var beforeValues = Snapshot(modified, original: true);
                var afterValues = Snapshot(modified, original: false);
                if (passwordChanged)
                {
                    beforeValues["Password"] = "••••••";
                    afterValues["Password"] = "changed";
                }

                before = JsonSerializer.Serialize(beforeValues);
                after = JsonSerializer.Serialize(afterValues);
                break;
        }

        return new AuditLogEntry
        {
            CompanyId = companyId,
            At = now,
            UserId = user,
            Action = action,
            EntityName = entry.Metadata.ClrType.Name,
            EntityId = id,
            BeforeJson = before,
            AfterJson = after,
        };
    }
}
