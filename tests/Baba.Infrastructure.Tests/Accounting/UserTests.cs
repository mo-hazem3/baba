using Baba.Application.Security;
using Baba.Domain;
using Baba.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Tests.Accounting;

/// <summary>Users, roles and passwords of a company file (brief section 10.4).</summary>
public class UserTests : AccountingFixture
{
    private static async Task<Env> EnvAsync() => await new UserTests().NewEnvAsync();

    private static async Task<RoleDto> RoleAsync(Env e, string key) => (await e.Users.ListRolesAsync()).Single(r => r.NameEn == BuiltInRoles.All.Single(b => b.Key == key).NameEn);

    [Fact]
    public async Task The_five_built_in_roles_exist_and_give_what_their_names_promise()
    {
        var e = await EnvAsync();

        var roles = await e.Users.ListRolesAsync();

        Assert.Equal(["Administrator", "Accountant", "Sales", "Storekeeper", "Viewer"], roles.Select(r => r.NameEn));
        Assert.All(roles, r => Assert.True(r.IsBuiltIn));
        var admin = roles[0].Permissions;
        Assert.Equal(Permission.All().Count(), admin.Count);
        var accountant = roles[1].Permissions;
        Assert.Contains("accounting.Approve", accountant);
        Assert.DoesNotContain("users.View", accountant);
        Assert.DoesNotContain("settings.Edit", accountant);
        var viewer = roles[4].Permissions;
        Assert.All(viewer.Where(p => !p.StartsWith("settings.")), p => Assert.EndsWith(".View", p));
        Assert.DoesNotContain("payroll.Edit", roles[2].Permissions); // sales cannot touch payroll
        Assert.Contains("inventory.Edit", roles[3].Permissions);     // a storekeeper can
    }

    [Fact]
    public async Task Turning_accounts_on_makes_the_first_administrator_once_and_stores_no_plain_password()
    {
        var e = await EnvAsync();
        Assert.False(await e.Users.AnyUsersAsync());

        var admin = await e.Users.TurnOnAccountsAsync("owner", "The Owner", "correct horse");

        Assert.True(await e.Users.AnyUsersAsync());
        Assert.Equal("Administrator", admin.RoleNameEn);
        Assert.False(admin.MustChangePassword);
        Assert.Contains("user.accounts-already-on", Codes(await RefusedAsync(() => e.Users.TurnOnAccountsAsync("other", "Other", "another password"))));

        await using var context = e.Files.Create();
        var stored = await context.AppUsers.AsNoTracking().SingleAsync();
        Assert.NotEqual("correct horse", stored.PasswordHash);
        Assert.DoesNotContain("correct horse", stored.PasswordHash);
        Assert.Equal(200_000, stored.PasswordIterations);
    }

    [Fact]
    public async Task A_user_needs_a_clean_name_a_long_enough_password_and_a_role_that_exists()
    {
        var e = await EnvAsync();
        await e.Users.TurnOnAccountsAsync("owner", "The Owner", "correct horse");
        var sales = await RoleAsync(e, BuiltInRoles.Sales);

        var refused = await RefusedAsync(() => e.Users.CreateUserAsync(new UserInput("a b", "", Guid.NewGuid(), "short")));
        var duplicate = await RefusedAsync(() => e.Users.CreateUserAsync(new UserInput("OWNER", "Again", sales.Id, "long enough pw")));
        var sameAsName = await RefusedAsync(() => e.Users.CreateUserAsync(new UserInput("samira01", "Samira", sales.Id, "samira01")));

        var codes = Codes(refused).ToList();
        Assert.Contains("user.name-invalid", codes);
        Assert.Contains("user.display-name-required", codes);
        Assert.Contains("user.role-unknown", codes);
        Assert.Contains("user.password-short", codes);
        Assert.Contains("user.name-duplicate", Codes(duplicate)); // not case sensitive
        Assert.Contains("user.password-same-as-name", Codes(sameAsName));
    }

    [Fact]
    public async Task A_new_user_must_choose_their_own_password_and_can_change_it_only_with_the_old_one()
    {
        var e = await EnvAsync();
        await e.Users.TurnOnAccountsAsync("owner", "The Owner", "correct horse");
        var sales = await RoleAsync(e, BuiltInRoles.Sales);

        var user = await e.Users.CreateUserAsync(new UserInput("samira", "Samira", sales.Id, "first password"));
        Assert.True(user.MustChangePassword);

        var wrong = await RefusedAsync(() => e.Users.ChangeOwnPasswordAsync("samira", "not it", "second password"));
        Assert.Contains("auth.wrong-credentials", Codes(wrong));
        await e.Users.ChangeOwnPasswordAsync("samira", "first password", "second password");

        Assert.False((await e.Users.ListUsersAsync()).Single(u => u.UserName == "samira").MustChangePassword);
        await e.Users.ChangeOwnPasswordAsync("SAMIRA", "second password", "third password"); // the name is not case sensitive
    }

    [Fact]
    public async Task The_company_always_keeps_an_active_administrator_and_nobody_removes_themselves()
    {
        var e = await EnvAsync(); // the acting user in these tests is "accountant"
        var admin = await e.Users.TurnOnAccountsAsync("owner", "The Owner", "correct horse");
        var viewer = await RoleAsync(e, BuiltInRoles.Viewer);
        var administrator = await RoleAsync(e, BuiltInRoles.Admin);

        Assert.Contains("user.last-admin", Codes(await RefusedAsync(() => e.Users.UpdateUserAsync(admin.Id, new UserUpdate("The Owner", viewer.Id, true)))));
        Assert.Contains("user.last-admin", Codes(await RefusedAsync(() => e.Users.UpdateUserAsync(admin.Id, new UserUpdate("The Owner", administrator.Id, false)))));
        Assert.Contains("user.last-admin", Codes(await RefusedAsync(() => e.Users.DeleteUserAsync(admin.Id))));

        var second = await e.Users.CreateUserAsync(new UserInput("deputy", "Deputy", administrator.Id, "deputy password"));
        await e.Users.UpdateUserAsync(admin.Id, new UserUpdate("The Owner", viewer.Id, true)); // fine now: the deputy is an administrator

        var self = await e.Users.CreateUserAsync(new UserInput("accountant", "Me", administrator.Id, "my own password"));
        Assert.Contains("user.is-self", Codes(await RefusedAsync(() => e.Users.DeleteUserAsync(self.Id))));
        Assert.Contains("user.is-self", Codes(await RefusedAsync(() => e.Users.UpdateUserAsync(self.Id, new UserUpdate("Me", administrator.Id, false)))));
        await e.Users.DeleteUserAsync(second.Id);
    }

    [Fact]
    public async Task A_company_can_make_its_own_role_but_not_change_a_built_in_one_or_delete_one_in_use()
    {
        var e = await EnvAsync();
        await e.Users.TurnOnAccountsAsync("owner", "The Owner", "correct horse");

        var role = await e.Users.CreateRoleAsync(new RoleInput("", "Payroll clerk", ["payroll.View", "payroll.Edit", "reports.View"]));
        Assert.False(role.IsBuiltIn);
        Assert.Equal(3, role.Permissions.Count);
        Assert.Equal("Payroll clerk", role.NameAr); // one name is used for both

        var changed = await e.Users.UpdateRoleAsync(role.Id, new RoleInput("كاتب رواتب", "Payroll clerk", ["payroll.View"]));
        Assert.Equal(["payroll.View"], changed.Permissions);

        var builtIn = await RoleAsync(e, BuiltInRoles.Sales);
        Assert.Contains("role.built-in", Codes(await RefusedAsync(() => e.Users.UpdateRoleAsync(builtIn.Id, new RoleInput("x", "x", [])))));
        Assert.Contains("role.built-in", Codes(await RefusedAsync(() => e.Users.DeleteRoleAsync(builtIn.Id))));
        Assert.Contains("role.permission-unknown", Codes(await RefusedAsync(() => e.Users.CreateRoleAsync(new RoleInput("", "Odd", ["payroll.Fly"])))));
        Assert.Contains("role.name-duplicate", Codes(await RefusedAsync(() => e.Users.CreateRoleAsync(new RoleInput("", "payroll clerk", [])))));

        await e.Users.CreateUserAsync(new UserInput("clerk", "Clerk", role.Id, "clerk password"));
        Assert.Contains("role.in-use", Codes(await RefusedAsync(() => e.Users.DeleteRoleAsync(role.Id))));
    }

    [Fact]
    public async Task The_audit_log_records_user_changes_but_never_a_password_or_its_hash()
    {
        var e = await EnvAsync();
        var admin = await e.Users.TurnOnAccountsAsync("owner", "The Owner", "correct horse");
        var sales = await RoleAsync(e, BuiltInRoles.Sales);
        var user = await e.Users.CreateUserAsync(new UserInput("samira", "Samira", sales.Id, "first password"));
        await e.Users.ResetPasswordAsync(user.Id, "brand new password");

        await using var context = e.Files.Create();
        var rows = await context.AuditLog.AsNoTracking().Where(a => a.EntityName == nameof(AppUser)).ToListAsync();
        var hash = (await context.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id)).PasswordHash;

        Assert.NotEmpty(rows);
        Assert.All(rows, r =>
        {
            Assert.DoesNotContain(hash, (r.BeforeJson ?? "") + (r.AfterJson ?? ""));
            Assert.DoesNotContain("PasswordHash", (r.BeforeJson ?? "") + (r.AfterJson ?? ""));
            Assert.DoesNotContain("PasswordSalt", (r.BeforeJson ?? "") + (r.AfterJson ?? ""));
        });
        Assert.Contains(rows, r => r.Action == AuditAction.Updated && (r.AfterJson ?? "").Contains("\"Password\":\"changed\""));
    }
}
