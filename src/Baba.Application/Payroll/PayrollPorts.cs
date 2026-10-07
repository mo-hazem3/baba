using Baba.Domain.Payroll;

namespace Baba.Application.Payroll;

public interface IPayrollStore
{
    // Employees (with their components)
    Task<IReadOnlyList<Employee>> ListEmployeesAsync(CancellationToken cancellationToken = default);
    Task<Employee?> FindEmployeeAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddEmployeeAsync(Employee employee, CancellationToken cancellationToken = default);

    /// <summary>Saves the employee and replaces its components with the ones on the object.</summary>
    Task UpdateEmployeeAsync(Employee employee, CancellationToken cancellationToken = default);

    Task DeleteEmployeeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The employees that have a payslip or leave, so they cannot be deleted.</summary>
    Task<IReadOnlySet<Guid>> EmployeeIdsInUseAsync(CancellationToken cancellationToken = default);

    // Components
    Task<IReadOnlyList<SalaryComponent>> ListComponentsAsync(CancellationToken cancellationToken = default);
    Task AddComponentAsync(SalaryComponent component, CancellationToken cancellationToken = default);
    Task UpdateComponentAsync(SalaryComponent component, CancellationToken cancellationToken = default);
    Task DeleteComponentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> ComponentIdsInUseAsync(CancellationToken cancellationToken = default);

    // Settings
    Task<PayrollSettings?> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(PayrollSettings settings, CancellationToken cancellationToken = default);

    // Runs
    Task<IReadOnlyList<PayrollRun>> ListRunsAsync(CancellationToken cancellationToken = default);
    Task<PayrollRun?> FindRunAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddRunAsync(PayrollRun run, CancellationToken cancellationToken = default);

    /// <summary>Saves the run and replaces its payslips (and their items) with the ones on the object.</summary>
    Task UpdateRunAsync(PayrollRun run, CancellationToken cancellationToken = default);

    Task DeleteRunAsync(Guid id, CancellationToken cancellationToken = default);

    // Leave
    Task<IReadOnlyList<LeaveRecord>> ListLeaveAsync(CancellationToken cancellationToken = default);
    Task<LeaveRecord?> FindLeaveAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddLeaveAsync(LeaveRecord leave, CancellationToken cancellationToken = default);
    Task UpdateLeaveAsync(LeaveRecord leave, CancellationToken cancellationToken = default);
    Task DeleteLeaveAsync(Guid id, CancellationToken cancellationToken = default);

    // End-of-service provision entries
    Task<IReadOnlyList<EndOfServiceAccrual>> ListAccrualsAsync(CancellationToken cancellationToken = default);
    Task AddAccrualAsync(EndOfServiceAccrual accrual, CancellationToken cancellationToken = default);
    Task DeleteAccrualAsync(Guid id, CancellationToken cancellationToken = default);
}
