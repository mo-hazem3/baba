using Baba.Application.Payroll;
using Baba.Application.Security;
using Baba.Application.Printing;
using Baba.Domain;

namespace Baba.Api.Endpoints;

public sealed record CreatePayrollRunRequest(DateOnly Month);

public sealed record SavePayslipRequest(IReadOnlyList<PayslipItemInput> Items);

public sealed record AccrueEndOfServiceRequest(DateOnly Month);

/// <summary>Employees, salary components, leave, payroll runs and the end-of-service provision (brief section 10.4).</summary>
public static class PayrollEndpoints
{
    public static void MapPayrollEndpoints(this IEndpointRouteBuilder api)
    {
        var employees = api.MapGroup("/employees").WithTags("Employees");
        employees.MapGet("/", (EmployeeService service, CancellationToken ct) => service.ListAsync(ct)).WithName("ListEmployees");
        employees.MapPost("/", (EmployeeInput input, EmployeeService service, CancellationToken ct) => service.CreateAsync(input, ct)).WithName("CreateEmployee");
        employees.MapPut("/{id:guid}", (Guid id, EmployeeInput input, EmployeeService service, CancellationToken ct) => service.UpdateAsync(id, input, ct)).WithName("UpdateEmployee");
        employees.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, EmployeeService service, CancellationToken ct) => service.SetActiveAsync(id, request.Active, ct)).WithName("SetEmployeeActive");
        employees.MapDelete("/{id:guid}", async (Guid id, EmployeeService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteEmployee");

        var components = api.MapGroup("/salary-components").WithTags("Employees");
        components.MapGet("/", (EmployeeService service, CancellationToken ct) => service.ListComponentsAsync(ct)).WithName("ListSalaryComponents");
        components.MapPost("/", (SalaryComponentInput input, EmployeeService service, CancellationToken ct) => service.CreateComponentAsync(input, ct)).WithName("CreateSalaryComponent");
        components.MapPut("/{id:guid}", (Guid id, SalaryComponentInput input, EmployeeService service, CancellationToken ct) => service.UpdateComponentAsync(id, input, ct)).WithName("UpdateSalaryComponent");
        components.MapPost("/{id:guid}/active", (Guid id, SetActiveRequest request, EmployeeService service, CancellationToken ct) => service.SetComponentActiveAsync(id, request.Active, ct)).WithName("SetSalaryComponentActive");
        components.MapDelete("/{id:guid}", async (Guid id, EmployeeService service, CancellationToken ct) =>
            {
                await service.DeleteComponentAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteSalaryComponent");

        var leave = api.MapGroup("/leave").WithTags("Employees");
        leave.MapGet("/", (EmployeeService service, CancellationToken ct) => service.ListLeaveAsync(ct)).WithName("ListLeave");
        leave.MapGet("/balances", (EmployeeService service, DateOnly asOf, CancellationToken ct) => service.BalancesAsync(asOf, ct)).WithName("GetLeaveBalances");
        leave.MapPost("/", (LeaveInput input, EmployeeService service, CancellationToken ct) => service.AddLeaveAsync(input, ct)).WithName("AddLeave");
        leave.MapPut("/{id:guid}", (Guid id, LeaveInput input, EmployeeService service, CancellationToken ct) => service.UpdateLeaveAsync(id, input, ct)).WithName("UpdateLeave");
        leave.MapDelete("/{id:guid}", async (Guid id, EmployeeService service, CancellationToken ct) =>
            {
                await service.DeleteLeaveAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteLeave");

        var payroll = api.MapGroup("/payroll").WithTags("Payroll");
        payroll.MapGet("/settings", (PayrollService service, CancellationToken ct) => service.GetSettingsAsync(ct)).WithName("GetPayrollSettings");
        payroll.MapPut("/settings", (PayrollSettingsInput input, PayrollService service, CancellationToken ct) => service.SaveSettingsAsync(input, ct)).WithName("SavePayrollSettings");

        payroll.MapGet("/runs", (PayrollService service, CancellationToken ct) => service.ListRunsAsync(ct)).WithName("ListPayrollRuns");
        payroll.MapGet("/runs/{id:guid}", async (Guid id, PayrollService service, CancellationToken ct) =>
                await service.GetRunAsync(id, ct) is { } run ? Results.Ok(run) : Results.NotFound())
            .Produces<PayrollRunDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetPayrollRun");
        payroll.MapPost("/runs", (CreatePayrollRunRequest request, PayrollService service, CancellationToken ct) => service.CreateRunAsync(request.Month, ct)).WithName("CreatePayrollRun");
        payroll.MapPost("/runs/run-due", async (PayrollService service, AppSession session, CancellationToken ct) =>
            {
                using (session.AsSystem())
                    return await service.RunDueAsync(ct);
            })
            .WithName("RunDuePayroll");
        payroll.MapPost("/runs/{id:guid}/refresh", (Guid id, PayrollService service, CancellationToken ct) => service.RefreshRunAsync(id, ct)).WithName("RefreshPayrollRun");
        payroll.MapPut("/runs/{id:guid}/payslips/{payslipId:guid}", (Guid id, Guid payslipId, SavePayslipRequest request, PayrollService service, CancellationToken ct) => service.SavePayslipAsync(id, payslipId, request.Items, ct)).WithName("SavePayslip");
        payroll.MapDelete("/runs/{id:guid}/payslips/{payslipId:guid}", (Guid id, Guid payslipId, PayrollService service, CancellationToken ct) => service.RemovePayslipAsync(id, payslipId, ct)).WithName("RemovePayslip");
        payroll.MapPost("/runs/{id:guid}/post", (Guid id, PayrollService service, CancellationToken ct) => service.PostRunAsync(id, ct)).WithName("PostPayrollRun");
        payroll.MapPost("/runs/{id:guid}/unpost", (Guid id, PayrollService service, CancellationToken ct) => service.UnpostRunAsync(id, ct)).WithName("UnpostPayrollRun");
        payroll.MapPost("/runs/{id:guid}/pay", (Guid id, PayInput input, PayrollService service, CancellationToken ct) => service.PayRunAsync(id, input, ct)).WithName("PayPayrollRun");
        payroll.MapPost("/runs/{id:guid}/unpay", (Guid id, PayrollService service, CancellationToken ct) => service.UnpayRunAsync(id, ct)).WithName("UnpayPayrollRun");
        payroll.MapGet("/runs/{id:guid}/payslips/pdf", async (Guid id, PrintLayout? layout, PayslipPrintService printing, CancellationToken ct) =>
            {
                if (!printing.IsAvailable)
                    return Results.StatusCode(StatusCodes.Status501NotImplemented);
                return Results.File(await printing.RenderAsync(id, null, layout, ct), "application/pdf", "payslips.pdf");
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status501NotImplemented)
            .WithName("PrintPayslips");
        payroll.MapGet("/runs/{id:guid}/payslips/{payslipId:guid}/pdf", async (Guid id, Guid payslipId, PrintLayout? layout, PayslipPrintService printing, CancellationToken ct) =>
            {
                if (!printing.IsAvailable)
                    return Results.StatusCode(StatusCodes.Status501NotImplemented);
                return Results.File(await printing.RenderAsync(id, payslipId, layout, ct), "application/pdf", "payslip.pdf");
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status501NotImplemented)
            .WithName("PrintPayslip");
        payroll.MapDelete("/runs/{id:guid}", async (Guid id, PayrollService service, CancellationToken ct) =>
            {
                await service.DeleteRunAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeletePayrollRun");

        payroll.MapGet("/end-of-service", (PayrollService service, DateOnly asOf, CancellationToken ct) => service.EndOfServicePositionAsync(asOf, ct)).WithName("GetEndOfServicePosition");
        payroll.MapPost("/end-of-service/accrue", (AccrueEndOfServiceRequest request, PayrollService service, CancellationToken ct) => service.AccrueEndOfServiceAsync(request.Month, ct)).WithName("AccrueEndOfService");
        payroll.MapPost("/end-of-service/run-due", async (PayrollService service, AppSession session, CancellationToken ct) =>
            {
                using (session.AsSystem())
                    return await service.RunDueEndOfServiceAsync(ct) ?? new EndOfServiceResult(null, 0);
            })
            .WithName("RunDueEndOfService");
        payroll.MapPost("/end-of-service/undo", async (PayrollService service, CancellationToken ct) =>
            {
                await service.UndoEndOfServiceAsync(ct);
                return Results.NoContent();
            })
            .WithName("UndoEndOfService");
    }
}
