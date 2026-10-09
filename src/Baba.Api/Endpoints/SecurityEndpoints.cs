using Baba.Application.Security;
using Baba.Domain.Security;

namespace Baba.Api.Endpoints;

public sealed record ChangePasswordRequest(string OldPassword, string NewPassword);

public sealed record TurnOnAccountsRequest(string UserName, string DisplayName, string Password);

public sealed record RecoverRequest(string FilePassword, string UserName, string DisplayName, string NewPassword);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record SecuritySettingsRequest(bool ApprovalRequired);

public sealed record SubmitVoucherRequest(Guid? Id, Baba.Application.Accounting.VoucherInput Input, string? Note);

public sealed record SubmitDocumentRequest(Guid? Id, Baba.Application.Trade.DocumentInput Input, string? Note);

public sealed record PermissionAreaDto(string Area, string? Module, IReadOnlyList<string> Actions);

/// <summary>Sign-in, users and roles (brief section 10.4).</summary>
public static class SecurityEndpoints
{
    public static void MapSecurityEndpoints(this IEndpointRouteBuilder api)
    {
        var session = api.MapGroup("/session").WithTags("Session");

        session.MapGet("/", (AccessService access, CancellationToken ct) => access.GetAsync(ct)).WithName("GetSession");
        session.MapPost("/sign-in", (SignInInput input, AccessService access, CancellationToken ct) => access.SignInAsync(input, ct)).WithName("SignIn");
        session.MapPost("/sign-out", (AccessService access) =>
            {
                access.SignOut();
                return Results.NoContent();
            })
            .WithName("SignOut");
        session.MapPost("/password", (ChangePasswordRequest request, AccessService access, CancellationToken ct) => access.ChangeOwnPasswordAsync(request.OldPassword, request.NewPassword, ct)).WithName("ChangeOwnPassword");
        session.MapPost("/turn-on", (TurnOnAccountsRequest request, AccessService access, CancellationToken ct) => access.TurnOnAccountsAsync(request.UserName, request.DisplayName, request.Password, ct)).WithName("TurnOnAccounts");
        session.MapPost("/recover", (RecoverRequest request, AccessService access, CancellationToken ct) => access.RecoverAsync(request.FilePassword, request.UserName, request.DisplayName, request.NewPassword, ct)).WithName("RecoverAdministrator");

        var users = api.MapGroup("/users").WithTags("Users");
        users.MapGet("/", (UserService service, CancellationToken ct) => service.ListUsersAsync(ct)).WithName("ListUsers");
        users.MapPost("/", (UserInput input, UserService service, CancellationToken ct) => service.CreateUserAsync(input, ct)).WithName("CreateUser");
        users.MapPut("/{id:guid}", (Guid id, UserUpdate input, UserService service, CancellationToken ct) => service.UpdateUserAsync(id, input, ct)).WithName("UpdateUser");
        users.MapPost("/{id:guid}/password", async (Guid id, ResetPasswordRequest request, UserService service, CancellationToken ct) =>
            {
                await service.ResetPasswordAsync(id, request.NewPassword, ct);
                return Results.NoContent();
            })
            .WithName("ResetUserPassword");
        users.MapDelete("/{id:guid}", async (Guid id, UserService service, CancellationToken ct) =>
            {
                await service.DeleteUserAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteUser");

        users.MapPut("/settings", (SecuritySettingsRequest request, AccessService access, CancellationToken ct) => access.SetApprovalRequiredAsync(request.ApprovalRequired, ct)).WithName("SetSecuritySettings");

        // Approval: people who may not post or issue send theirs for approval, and approvers approve (which posts) or reject.
        var approvals = api.MapGroup("/approvals").WithTags("Approvals");
        approvals.MapGet("/", (ApprovalService service, CancellationToken ct) => service.ListAsync(ct)).WithName("ListApprovals");
        approvals.MapPost("/{id:guid}/approve", (Guid id, ApprovalService service, CancellationToken ct) => service.ApproveAsync(id, ct)).WithName("ApproveRequest");
        approvals.MapPost("/{id:guid}/reject", (Guid id, RejectInput input, ApprovalService service, CancellationToken ct) => service.RejectAsync(id, input.Note, ct)).WithName("RejectRequest");
        api.MapPost("/vouchers/submit", (SubmitVoucherRequest request, ApprovalService service, CancellationToken ct) => service.SubmitVoucherAsync(request.Id, request.Input, request.Note, ct)).WithName("SubmitVoucher").WithTags("Approvals");
        api.MapPost("/vouchers/{id:guid}/submit", (Guid id, SubmitInput input, ApprovalService service, CancellationToken ct) => service.SubmitSavedVoucherAsync(id, input.Note, ct)).WithName("SubmitSavedVoucher").WithTags("Approvals");
        api.MapPost("/documents/submit", (SubmitDocumentRequest request, ApprovalService service, CancellationToken ct) => service.SubmitDocumentAsync(request.Id, request.Input, request.Note, ct)).WithName("SubmitDocument").WithTags("Approvals");
        api.MapPost("/documents/{id:guid}/submit", (Guid id, SubmitInput input, ApprovalService service, CancellationToken ct) => service.SubmitSavedDocumentAsync(id, input.Note, ct)).WithName("SubmitSavedDocument").WithTags("Approvals");

        api.MapGet("/audit-log", (AuditService service, DateOnly? from, DateOnly? to, string? user, string? entity, int? limit, CancellationToken ct) =>
                service.ListAsync(new AuditSearch(from, to, user, entity, limit), ct))
            .WithName("ListAuditLog").WithTags("Users");

        var roles = api.MapGroup("/roles").WithTags("Users");
        roles.MapGet("/", (UserService service, CancellationToken ct) => service.ListRolesAsync(ct)).WithName("ListRoles");
        roles.MapGet("/catalog", () => PermissionAreas.All.Select(a => new PermissionAreaDto(a, PermissionAreas.ModuleOf(a), [.. Enum.GetNames<PermissionAction>()])).ToList()).WithName("GetPermissionCatalog");
        roles.MapPost("/", (RoleInput input, UserService service, CancellationToken ct) => service.CreateRoleAsync(input, ct)).WithName("CreateRole");
        roles.MapPut("/{id:guid}", (Guid id, RoleInput input, UserService service, CancellationToken ct) => service.UpdateRoleAsync(id, input, ct)).WithName("UpdateRole");
        roles.MapDelete("/{id:guid}", async (Guid id, UserService service, CancellationToken ct) =>
            {
                await service.DeleteRoleAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteRole");
    }
}
