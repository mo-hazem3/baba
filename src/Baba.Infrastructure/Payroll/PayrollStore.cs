using Baba.Application.Payroll;
using Baba.Domain.Payroll;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Payroll;

public sealed class PayrollStore(ICompanyDbContextFactory contexts) : IPayrollStore
{
    // ---------------------------------------------------------------- Employees

    public async Task<IReadOnlyList<Employee>> ListEmployeesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Employees.AsNoTracking().Include(e => e.Components).ToListAsync(cancellationToken);
    }

    public async Task<Employee?> FindEmployeeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.Employees.AsNoTracking().Include(e => e.Components).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task AddEmployeeAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Employees.Add(employee);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateEmployeeAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var stored = await context.Employees.Include(e => e.Components).SingleAsync(e => e.Id == employee.Id, cancellationToken);
        context.EmployeeComponents.RemoveRange(stored.Components);
        await context.SaveChangesAsync(cancellationToken);

        context.Entry(stored).CurrentValues.SetValues(employee);
        foreach (var component in employee.Components)
        {
            component.EmployeeId = employee.Id;
            context.EmployeeComponents.Add(new EmployeeComponent { Id = Guid.CreateVersion7(), CompanyId = employee.CompanyId, EmployeeId = employee.Id, ComponentId = component.ComponentId, ValueScaled = component.ValueScaled });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteEmployeeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.Employees.Remove(await context.Employees.SingleAsync(e => e.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> EmployeeIdsInUseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = await context.Payslips.Select(p => p.EmployeeId).Distinct().ToListAsync(cancellationToken);
        used.AddRange(await context.LeaveRecords.Select(l => l.EmployeeId).Distinct().ToListAsync(cancellationToken));
        return used.ToHashSet();
    }

    // ---------------------------------------------------------------- Components

    public async Task<IReadOnlyList<SalaryComponent>> ListComponentsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.SalaryComponents.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task AddComponentAsync(SalaryComponent component, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.SalaryComponents.Add(component);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateComponentAsync(SalaryComponent component, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.SalaryComponents.SingleAsync(c => c.Id == component.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(component);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteComponentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.SalaryComponents.Remove(await context.SalaryComponents.SingleAsync(c => c.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> ComponentIdsInUseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = await context.EmployeeComponents.Select(c => c.ComponentId).Distinct().ToListAsync(cancellationToken);
        used.AddRange(await context.PayslipItems.Where(i => i.ComponentId != null).Select(i => i.ComponentId!.Value).Distinct().ToListAsync(cancellationToken));
        return used.ToHashSet();
    }

    // ---------------------------------------------------------------- Settings

    public async Task<PayrollSettings?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.PayrollSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SaveSettingsAsync(PayrollSettings settings, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.PayrollSettings.FirstOrDefaultAsync(s => s.Id == settings.Id, cancellationToken);
        if (stored is null)
            context.PayrollSettings.Add(settings);
        else
            context.Entry(stored).CurrentValues.SetValues(settings);
        await context.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- Runs

    public async Task<IReadOnlyList<PayrollRun>> ListRunsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.PayrollRuns.AsNoTracking().Include(r => r.Payslips).ThenInclude(p => p.Items).ToListAsync(cancellationToken);
    }

    public async Task<PayrollRun?> FindRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.PayrollRuns.AsNoTracking().Include(r => r.Payslips).ThenInclude(p => p.Items).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task AddRunAsync(PayrollRun run, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.PayrollRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRunAsync(PayrollRun run, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var stored = await context.PayrollRuns.Include(r => r.Payslips).SingleAsync(r => r.Id == run.Id, cancellationToken);
        context.Payslips.RemoveRange(stored.Payslips); // their items go with them
        await context.SaveChangesAsync(cancellationToken);

        context.Entry(stored).CurrentValues.SetValues(run);
        foreach (var payslip in run.Payslips)
        {
            // Fresh copies: the objects the caller holds may share ids with the rows just removed.
            var copy = new Payslip
            {
                Id = payslip.Id, CompanyId = run.CompanyId, RunId = run.Id, EmployeeId = payslip.EmployeeId,
                EmployeeInsuranceScaled = payslip.EmployeeInsuranceScaled, EmployerInsuranceScaled = payslip.EmployerInsuranceScaled,
            };
            copy.Items = [.. payslip.Items.Select(i => new PayslipItem
            {
                Id = i.Id, CompanyId = run.CompanyId, PayslipId = copy.Id, LineNumber = i.LineNumber, Kind = i.Kind, Label = i.Label, LabelAr = i.LabelAr,
                ComponentId = i.ComponentId, AmountScaled = i.AmountScaled, AccountId = i.AccountId, IsInsurable = i.IsInsurable, InEndOfService = i.InEndOfService, IsBasic = i.IsBasic,
            })];
            context.Payslips.Add(copy);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.PayrollRuns.Remove(await context.PayrollRuns.SingleAsync(r => r.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- Leave

    public async Task<IReadOnlyList<LeaveRecord>> ListLeaveAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.LeaveRecords.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<LeaveRecord?> FindLeaveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.LeaveRecords.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task AddLeaveAsync(LeaveRecord leave, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.LeaveRecords.Add(leave);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateLeaveAsync(LeaveRecord leave, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.LeaveRecords.SingleAsync(l => l.Id == leave.Id, cancellationToken);
        context.Entry(stored).CurrentValues.SetValues(leave);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteLeaveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.LeaveRecords.Remove(await context.LeaveRecords.SingleAsync(l => l.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- End-of-service entries

    public async Task<IReadOnlyList<EndOfServiceAccrual>> ListAccrualsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        return await context.EndOfServiceAccruals.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task AddAccrualAsync(EndOfServiceAccrual accrual, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.EndOfServiceAccruals.Add(accrual);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAccrualAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        context.EndOfServiceAccruals.Remove(await context.EndOfServiceAccruals.SingleAsync(a => a.Id == id, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}
