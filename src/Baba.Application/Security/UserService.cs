using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Baba.Application.Abstractions;
using Baba.Application.Companies;
using Baba.Domain.Security;

namespace Baba.Application.Security;

public interface ISecurityStore
{
    Task<IReadOnlyList<AppUser>> ListUsersAsync(CancellationToken cancellationToken = default);
    Task<AppUser?> FindUserAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AppUser?> FindUserByNameAsync(string userName, CancellationToken cancellationToken = default);
    Task AddUserAsync(AppUser user, CancellationToken cancellationToken = default);
    Task UpdateUserAsync(AppUser user, CancellationToken cancellationToken = default);
    Task DeleteUserAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken = default);
    Task AddRoleAsync(Role role, CancellationToken cancellationToken = default);
    Task UpdateRoleAsync(Role role, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record UserDto(
    Guid Id, string UserName, string DisplayName, Guid RoleId, string RoleNameEn, string RoleNameAr, bool IsActive, bool MustChangePassword, DateTime? LastSignInAt);

public sealed record UserInput(string UserName, string DisplayName, Guid RoleId, string Password);

public sealed record UserUpdate(string DisplayName, Guid RoleId, bool IsActive);

public sealed record RoleDto(Guid Id, string NameAr, string NameEn, bool IsBuiltIn, IReadOnlyList<string> Permissions, int Users);

public sealed record RoleInput(string NameAr, string NameEn, IReadOnlyList<string> Permissions);

/// <summary>
/// Who may sign in and what they may do (brief section 10.4): users with a role each, roles that are sets of permissions. The five
/// built-in roles (Administrator, Accountant, Sales, Storekeeper, Viewer) are always there; a company makes its own for anything else.
/// A company with no users has no sign-in at all, exactly as before: turning accounts on creates the first administrator.
/// </summary>
public sealed partial class UserService(ISecurityStore store, ICompanyFiles files, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MinimumPasswordLength = 8;
    private const int Iterations = 200_000;

    [GeneratedRegex(@"^[\p{L}\p{N}._-]{2,40}$")]
    private static partial Regex NamePattern();

    private CompanyInfo CompanyOrThrow() => files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");

    // ---------------------------------------------------------------- Roles

    /// <summary>Makes sure the built-in roles exist (the first time they are asked for) and keeps their permissions as the program defines them.</summary>
    public async Task EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
        var company = CompanyOrThrow();
        var roles = await store.ListRolesAsync(cancellationToken);
        foreach (var definition in BuiltInRoles.All)
        {
            var existing = roles.FirstOrDefault(r => r.BuiltInKey == definition.Key);
            if (existing is null)
            {
                var role = new Role { CompanyId = company.Id, BuiltInKey = definition.Key, NameEn = definition.NameEn, NameAr = definition.NameAr };
                role.SetPermissions(definition.Permissions);
                await store.AddRoleAsync(role, cancellationToken);
            }
            else if (!existing.Permissions.SetEquals(definition.Permissions))
            {
                existing.SetPermissions(definition.Permissions); // a new version of the program may add an area: built-in roles follow it
                await store.UpdateRoleAsync(existing, cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRolesAsync(cancellationToken);
        var users = await store.ListUsersAsync(cancellationToken);
        var order = BuiltInRoles.All.Select((r, i) => (r.Key, i)).ToDictionary(x => x.Key, x => x.i);
        return (await store.ListRolesAsync(cancellationToken))
            .OrderBy(r => r.BuiltInKey is { } key ? order.GetValueOrDefault(key, 99) : 100).ThenBy(r => r.NameEn, StringComparer.OrdinalIgnoreCase)
            .Select(r => ToDto(r, users.Count(u => u.RoleId == r.Id))).ToList();
    }

    public async Task<RoleDto> CreateRoleAsync(RoleInput input, CancellationToken cancellationToken = default)
    {
        var company = CompanyOrThrow();
        await EnsureRolesAsync(cancellationToken);
        var role = new Role { CompanyId = company.Id };
        await ApplyAsync(role, input, cancellationToken);
        await store.AddRoleAsync(role, cancellationToken);
        return ToDto(role, 0);
    }

    public async Task<RoleDto> UpdateRoleAsync(Guid id, RoleInput input, CancellationToken cancellationToken = default)
    {
        var role = (await store.ListRolesAsync(cancellationToken)).FirstOrDefault(r => r.Id == id) ?? throw new NotFoundException("role");
        if (role.BuiltInKey is not null)
            throw Refused("role", "role.built-in");

        await ApplyAsync(role, input, cancellationToken);
        await store.UpdateRoleAsync(role, cancellationToken);
        return ToDto(role, (await store.ListUsersAsync(cancellationToken)).Count(u => u.RoleId == id));
    }

    public async Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = (await store.ListRolesAsync(cancellationToken)).FirstOrDefault(r => r.Id == id) ?? throw new NotFoundException("role");
        if (role.BuiltInKey is not null)
            throw Refused("role", "role.built-in");
        if ((await store.ListUsersAsync(cancellationToken)).Any(u => u.RoleId == id))
            throw Refused("role", "role.in-use");

        await store.DeleteRoleAsync(id, cancellationToken);
    }

    private async Task ApplyAsync(Role role, RoleInput input, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var nameEn = input.NameEn?.Trim() ?? "";
        var nameAr = input.NameAr?.Trim() ?? "";
        if (nameEn.Length == 0 && nameAr.Length == 0)
            issues.Add(new("name", "role.name-required"));
        if (nameAr.Length == 0) nameAr = nameEn;
        if (nameEn.Length == 0) nameEn = nameAr;

        var others = (await store.ListRolesAsync(cancellationToken)).Where(r => r.Id != role.Id).ToList();
        if (others.Any(r => string.Equals(r.NameEn, nameEn, StringComparison.OrdinalIgnoreCase) || string.Equals(r.NameAr, nameAr, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("name", "role.name-duplicate"));
        if (input.Permissions is null || input.Permissions.Any(p => !Permission.IsValid(p)))
            issues.Add(new("permissions", "role.permission-unknown"));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        role.NameEn = nameEn;
        role.NameAr = nameAr;
        role.SetPermissions(input.Permissions!);
    }

    // ---------------------------------------------------------------- Users

    public async Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default) => (await store.ListUsersAsync(cancellationToken)).Any(u => u.IsActive);

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var roles = (await store.ListRolesAsync(cancellationToken)).ToDictionary(r => r.Id);
        return (await store.ListUsersAsync(cancellationToken)).OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase).Select(u => ToDto(u, roles)).ToList();
    }

    /// <summary>Makes the first user, an administrator: from then on the company asks everyone to sign in.</summary>
    public async Task<UserDto> TurnOnAccountsAsync(string userName, string displayName, string password, CancellationToken cancellationToken = default)
    {
        if (await AnyUsersAsync(cancellationToken))
            throw Refused("users", "user.accounts-already-on");

        await EnsureRolesAsync(cancellationToken);
        var admin = (await store.ListRolesAsync(cancellationToken)).First(r => r.BuiltInKey == BuiltInRoles.Admin);
        return await CreateUserAsync(new UserInput(userName, displayName, admin.Id, password), mustChange: false, cancellationToken);
    }

    public Task<UserDto> CreateUserAsync(UserInput input, CancellationToken cancellationToken = default) => CreateUserAsync(input, mustChange: true, cancellationToken);

    private async Task<UserDto> CreateUserAsync(UserInput input, bool mustChange, CancellationToken cancellationToken)
    {
        var company = CompanyOrThrow();
        await EnsureRolesAsync(cancellationToken);
        var roles = (await store.ListRolesAsync(cancellationToken)).ToDictionary(r => r.Id);
        var issues = new List<ValidationIssue>();
        var name = input.UserName?.Trim() ?? "";
        if (!NamePattern().IsMatch(name))
            issues.Add(new("userName", "user.name-invalid"));
        else if (await store.FindUserByNameAsync(name, cancellationToken) is not null)
            issues.Add(new("userName", "user.name-duplicate"));
        if ((input.DisplayName?.Trim() ?? "").Length == 0)
            issues.Add(new("displayName", "user.display-name-required"));
        if (!roles.ContainsKey(input.RoleId))
            issues.Add(new("role", "user.role-unknown"));
        issues.AddRange(CheckPassword(input.Password, name));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        var user = new AppUser { CompanyId = company.Id, UserName = name, DisplayName = input.DisplayName!.Trim(), RoleId = input.RoleId, MustChangePassword = mustChange };
        SetPassword(user, input.Password);
        await store.AddUserAsync(user, cancellationToken);
        return ToDto(user, roles);
    }

    public async Task<UserDto> UpdateUserAsync(Guid id, UserUpdate input, CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserAsync(id, cancellationToken) ?? throw new NotFoundException("user");
        var roles = (await store.ListRolesAsync(cancellationToken)).ToDictionary(r => r.Id);
        var issues = new List<ValidationIssue>();
        if ((input.DisplayName?.Trim() ?? "").Length == 0)
            issues.Add(new("displayName", "user.display-name-required"));
        if (!roles.ContainsKey(input.RoleId))
            issues.Add(new("role", "user.role-unknown"));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        // The company must always have an active administrator, or nobody could manage users again.
        var losesAdmin = IsAdmin(user, roles) && user.IsActive && (!input.IsActive || !IsAdmin(input.RoleId, roles));
        if (losesAdmin && !await AnotherActiveAdminAsync(user.Id, roles, cancellationToken))
            throw Refused("user", "user.last-admin");
        if (!input.IsActive && string.Equals(user.UserName, currentUser.UserId, StringComparison.OrdinalIgnoreCase))
            throw Refused("user", "user.is-self");

        user.DisplayName = input.DisplayName!.Trim();
        user.RoleId = input.RoleId;
        user.IsActive = input.IsActive;
        await store.UpdateUserAsync(user, cancellationToken);
        return ToDto(user, roles);
    }

    /// <summary>An administrator chooses a new password for someone; they must choose their own at the next sign-in.</summary>
    public async Task ResetPasswordAsync(Guid id, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserAsync(id, cancellationToken) ?? throw new NotFoundException("user");
        var issues = CheckPassword(newPassword, user.UserName).ToList();
        if (issues.Count > 0)
            throw new ValidationException(issues);

        SetPassword(user, newPassword);
        user.MustChangePassword = !string.Equals(user.UserName, currentUser.UserId, StringComparison.OrdinalIgnoreCase);
        await store.UpdateUserAsync(user, cancellationToken);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserAsync(id, cancellationToken) ?? throw new NotFoundException("user");
        var roles = (await store.ListRolesAsync(cancellationToken)).ToDictionary(r => r.Id);
        if (string.Equals(user.UserName, currentUser.UserId, StringComparison.OrdinalIgnoreCase))
            throw Refused("user", "user.is-self");
        if (IsAdmin(user, roles) && user.IsActive && !await AnotherActiveAdminAsync(user.Id, roles, cancellationToken))
            throw Refused("user", "user.last-admin");

        await store.DeleteUserAsync(id, cancellationToken);
    }

    // ---------------------------------------------------------------- Passwords

    /// <summary>Checks a sign-in name and password; returns the user when they are right and the account is on, otherwise null (never says which was wrong).</summary>
    internal async Task<AppUser?> VerifyAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var user = await store.FindUserByNameAsync(userName?.Trim() ?? "", cancellationToken);
        if (user is null)
        {
            // The same work as for a real user, so the answer does not take longer when the name exists.
            _ = Hash(password ?? "", new byte[16], Iterations);
            return null;
        }

        var ok = FixedTimeEquals(Hash(password ?? "", Convert.FromBase64String(user.PasswordSalt), user.PasswordIterations), user.PasswordHash);
        return ok && user.IsActive ? user : null;
    }

    /// <summary>Changes a user's own password after proving the old one.</summary>
    public async Task ChangeOwnPasswordAsync(string userName, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await VerifyAsync(userName, oldPassword, cancellationToken) ?? throw Refused("oldPassword", "auth.wrong-credentials");
        var issues = CheckPassword(newPassword, user.UserName).ToList();
        if (issues.Count > 0)
            throw new ValidationException(issues);

        SetPassword(user, newPassword);
        user.MustChangePassword = false;
        await store.UpdateUserAsync(user, cancellationToken);
    }

    internal async Task RecordSignInAsync(AppUser user, CancellationToken cancellationToken)
    {
        user.LastSignInAt = clock.GetUtcNow().UtcDateTime;
        await store.UpdateUserAsync(user, cancellationToken);
    }

    /// <summary>For the recovery of a forgotten administrator password (the file password was proven): sets or makes an administrator.</summary>
    internal async Task<AppUser> RecoverAdministratorAsync(string userName, string displayName, string password, CancellationToken cancellationToken)
    {
        await EnsureRolesAsync(cancellationToken);
        var roles = (await store.ListRolesAsync(cancellationToken)).ToList();
        var admin = roles.First(r => r.BuiltInKey == BuiltInRoles.Admin);
        var name = userName?.Trim() ?? "";
        var existing = await store.FindUserByNameAsync(name, cancellationToken);
        if (existing is null)
        {
            var made = await CreateUserAsync(new UserInput(name, string.IsNullOrWhiteSpace(displayName) ? name : displayName, admin.Id, password), mustChange: false, cancellationToken);
            return (await store.FindUserAsync(made.Id, cancellationToken))!;
        }

        var issues = CheckPassword(password, existing.UserName).ToList();
        if (issues.Count > 0)
            throw new ValidationException(issues);

        SetPassword(existing, password);
        existing.MustChangePassword = false;
        existing.IsActive = true;
        existing.RoleId = admin.Id;
        await store.UpdateUserAsync(existing, cancellationToken);
        return existing;
    }

    private static IEnumerable<ValidationIssue> CheckPassword(string? password, string userName)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
            yield return new ValidationIssue("password", "user.password-short");
        else if (string.Equals(password, userName, StringComparison.OrdinalIgnoreCase))
            yield return new ValidationIssue("password", "user.password-same-as-name");
    }

    private static void SetPassword(AppUser user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        user.PasswordSalt = Convert.ToBase64String(salt);
        user.PasswordIterations = Iterations;
        user.PasswordHash = Hash(password, salt, Iterations);
    }

    private static string Hash(string password, byte[] salt, int iterations) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    // ---------------------------------------------------------------- Helpers

    private static bool IsAdmin(AppUser user, IReadOnlyDictionary<Guid, Role> roles) => IsAdmin(user.RoleId, roles);

    private static bool IsAdmin(Guid roleId, IReadOnlyDictionary<Guid, Role> roles) => roles.TryGetValue(roleId, out var role) && role.BuiltInKey == BuiltInRoles.Admin;

    private async Task<bool> AnotherActiveAdminAsync(Guid except, IReadOnlyDictionary<Guid, Role> roles, CancellationToken cancellationToken) =>
        (await store.ListUsersAsync(cancellationToken)).Any(u => u.Id != except && u.IsActive && IsAdmin(u, roles));

    private static RoleDto ToDto(Role r, int users) => new(r.Id, r.NameAr, r.NameEn, r.BuiltInKey is not null, [.. r.Permissions.OrderBy(p => p, StringComparer.Ordinal)], users);

    private static UserDto ToDto(AppUser u, IReadOnlyDictionary<Guid, Role> roles)
    {
        var role = roles.GetValueOrDefault(u.RoleId);
        return new UserDto(u.Id, u.UserName, u.DisplayName, u.RoleId, role?.NameEn ?? "", role?.NameAr ?? "", u.IsActive, u.MustChangePassword, u.LastSignInAt);
    }

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);
}
