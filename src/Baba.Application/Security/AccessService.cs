using Baba.Application.Companies;
using Baba.Domain.Security;

namespace Baba.Application.Security;

/// <summary>Lets the program check the file password of the open company again (for recovering a forgotten administrator password).</summary>
public interface ICompanyPassword
{
    bool Matches(string password);
}

/// <summary>Thrown when the signed-in person's role does not allow what they asked for. The screen says so in plain words.</summary>
public sealed class ForbiddenException(string area, PermissionAction action)
    : Exception($"Not allowed: {Permission.Key(area, action)}")
{
    public string Area { get; } = area;
    public PermissionAction Action { get; } = action;
}

/// <summary>Thrown when a person whose password an administrator set has not chosen their own yet.</summary>
public sealed class PasswordChangeRequiredException() : Exception("The password has to be changed first.");

/// <summary>Thrown when the company asks people to sign in and nobody has.</summary>
public sealed class SignInRequiredException() : Exception("Sign in is required.");

/// <summary>Who is signed in, with the permissions their role gives.</summary>
public sealed record SessionUser(
    Guid Id, string UserName, string DisplayName, Guid RoleId, string RoleNameEn, string RoleNameAr, IReadOnlySet<string> Permissions, bool MustChangePassword);

/// <summary>
/// The one person using the program (the desktop app has one window). The company file's own password stays as it was; when the company
/// has users, they also sign in with their own name and password after the file is opened.
/// </summary>
public sealed class AppSession
{
    private readonly object _gate = new();
    private SessionUser? _user;
    private Guid? _loadedFor;
    private bool _accountsOn;
    private static readonly AsyncLocal<bool> RunningAsSystem = new();

    public SessionUser? User
    {
        get { lock (_gate) return _user; }
    }

    public bool AccountsOn
    {
        get { lock (_gate) return _accountsOn; }
    }

    /// <summary>The program itself is acting (a run that happens when a company opens): permissions are not checked.</summary>
    public bool IsSystem => RunningAsSystem.Value;

    internal bool IsLoadedFor(Guid companyId)
    {
        lock (_gate) return _loadedFor == companyId;
    }

    internal void Load(Guid companyId, bool accountsOn)
    {
        lock (_gate)
        {
            _loadedFor = companyId;
            _accountsOn = accountsOn;
            _user = null;
        }
    }

    internal void SetAccountsOn(bool accountsOn)
    {
        lock (_gate) _accountsOn = accountsOn;
    }

    internal void SignIn(SessionUser user)
    {
        lock (_gate) _user = user;
    }

    public void SignOut()
    {
        lock (_gate) _user = null;
    }

    /// <summary>Forgets everything (a company was closed).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _user = null;
            _loadedFor = null;
            _accountsOn = false;
        }
    }

    /// <summary>For the program's own automatic work: <c>using (session.AsSystem()) { ... }</c>.</summary>
    public IDisposable AsSystem()
    {
        var previous = RunningAsSystem.Value;
        RunningAsSystem.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => RunningAsSystem.Value = previous;
    }
}

/// <summary>What the screen needs to know about sign-in.</summary>
public sealed record SessionInfo(
    /// <summary>The open company asks people to sign in.</summary>
    bool AccountsOn,
    bool SignedIn,
    string? UserName,
    string? DisplayName,
    string? RoleNameEn,
    string? RoleNameAr,
    bool MustChangePassword,
    /// <summary>What may be done (every permission when the company has no users).</summary>
    IReadOnlyList<string> Permissions,
    /// <summary>Posting and issuing need someone who may approve; the others send theirs for approval.</summary>
    bool ApprovalRequired = false);

public sealed record SignInInput(string UserName, string Password);

/// <summary>Signing in and asking whether something is allowed (brief section 10.4).</summary>
public sealed class AccessService(AppSession session, UserService users, ISecurityStore store, ICompanyFiles files, ICompanyPassword companyPassword)
{
    private static readonly IReadOnlyList<string> Everything = [.. Permission.All()];

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var company = files.Current;
        if (company is null)
        {
            session.Reset();
            return;
        }

        if (!session.IsLoadedFor(company.Id))
            session.Load(company.Id, await users.AnyUsersAsync(cancellationToken)); // a different company (or the first look): start signed out
    }

    public async Task<SessionInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        return await DescribeAsync(cancellationToken);
    }

    private async Task<SessionInfo> DescribeAsync(CancellationToken cancellationToken)
    {
        var user = session.User;
        if (!session.AccountsOn)
            return new SessionInfo(false, false, null, null, null, null, false, Everything);
        if (user is null)
            return new SessionInfo(true, false, null, null, null, null, false, []);
        var approval = (await store.GetSettingsAsync(cancellationToken)).ApprovalRequired;
        return new SessionInfo(true, true, user.UserName, user.DisplayName, user.RoleNameEn, user.RoleNameAr, user.MustChangePassword, [.. user.Permissions.OrderBy(p => p, StringComparer.Ordinal)], approval);
    }

    public async Task<SessionInfo> SignInAsync(SignInInput input, CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        if (files.Current is null)
            throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");

        var user = await users.VerifyAsync(input.UserName, input.Password, cancellationToken)
            ?? throw new ValidationException([new ValidationIssue("password", "auth.wrong-credentials")]);
        await users.RecordSignInAsync(user, cancellationToken);
        await StartSessionAsync(user, cancellationToken);
        return await DescribeAsync(cancellationToken);
    }

    public void SignOut() => session.SignOut();

    /// <summary>A person changes their own password (the screen asks for it right after an administrator reset it).</summary>
    public async Task<SessionInfo> ChangeOwnPasswordAsync(string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        var user = session.User ?? throw new SignInRequiredException();
        await users.ChangeOwnPasswordAsync(user.UserName, oldPassword, newPassword, cancellationToken);
        var fresh = await store.FindUserAsync(user.Id, cancellationToken);
        if (fresh is not null)
            await StartSessionAsync(fresh, cancellationToken);
        return await DescribeAsync(cancellationToken);
    }

    /// <summary>
    /// The way back in when the administrator password is forgotten: whoever knows the company file's own password can make (or reset)
    /// an administrator. The file password is the master key; nothing else can open the file anyway.
    /// </summary>
    public async Task<SessionInfo> RecoverAsync(string filePassword, string userName, string displayName, string newPassword, CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        if (files.Current is null)
            throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        if (!companyPassword.Matches(filePassword ?? ""))
            throw new ValidationException([new ValidationIssue("filePassword", "auth.file-password-wrong")]);

        var user = await users.RecoverAdministratorAsync(userName, displayName, newPassword, cancellationToken);
        session.SetAccountsOn(true);
        await StartSessionAsync(user, cancellationToken);
        return await DescribeAsync(cancellationToken);
    }

    /// <summary>The company just got its first user (an administrator): that person is signed in now.</summary>
    public async Task<SessionInfo> TurnOnAccountsAsync(string userName, string displayName, string password, CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        await users.TurnOnAccountsAsync(userName, displayName, password, cancellationToken);
        var user = await store.FindUserByNameAsync(userName.Trim(), cancellationToken) ?? throw new NotFoundException("user");
        session.SetAccountsOn(true);
        await StartSessionAsync(user, cancellationToken);
        return await DescribeAsync(cancellationToken);
    }

    private async Task StartSessionAsync(Domain.Security.AppUser user, CancellationToken cancellationToken)
    {
        var role = (await store.ListRolesAsync(cancellationToken)).FirstOrDefault(r => r.Id == user.RoleId) ?? throw new NotFoundException("role");
        session.SignIn(new SessionUser(user.Id, user.UserName, user.DisplayName, role.Id, role.NameEn, role.NameAr, role.Permissions, user.MustChangePassword));
    }

    // ---------------------------------------------------------------- Asking

    /// <summary>Whether the person using the program may do this. Everyone may when the company has no users, and the program itself always may.</summary>
    public bool Can(string area, PermissionAction action)
    {
        if (session.IsSystem || !session.AccountsOn)
            return true;
        return session.User?.Permissions.Contains(Permission.Key(area, action)) == true;
    }

    /// <summary>Throws <see cref="SignInRequiredException"/> or <see cref="ForbiddenException"/> unless the person may do this.</summary>
    public void Require(string area, PermissionAction action)
    {
        if (session.IsSystem || !session.AccountsOn)
            return;
        if (session.User is null)
            throw new SignInRequiredException();
        if (!session.User.Permissions.Contains(Permission.Key(area, action)))
            throw new ForbiddenException(area, action);
    }

    /// <summary>Whether the company asks people to sign in and nobody has yet (the middleware refuses everything but sign-in then).</summary>
    public async Task<bool> NeedsSignInAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        return session.AccountsOn && session.User is null && !session.IsSystem;
    }

    // ---------------------------------------------------------------- Approval

    /// <summary>
    /// Posting a voucher or issuing an invoice: when the company asks for approval, only someone who may approve the area can do it
    /// directly. Everyone else gets "approval.required" and sends it for approval instead.
    /// </summary>
    public async Task RequireDirectPostingAsync(string area, CancellationToken cancellationToken = default)
    {
        if (session.IsSystem || !session.AccountsOn || Can(area, PermissionAction.Approve))
            return;
        if ((await store.GetSettingsAsync(cancellationToken)).ApprovalRequired)
            throw new ValidationException([new ValidationIssue("approval", "approval.required")]);
    }

    /// <summary>Sending something for approval only makes sense when the company asks for it.</summary>
    public async Task RequireApprovalSwitchedOnAsync(CancellationToken cancellationToken = default)
    {
        if (!(await store.GetSettingsAsync(cancellationToken)).ApprovalRequired)
            throw new ValidationException([new ValidationIssue("approval", "approval.not-needed")]);
    }

    /// <summary>Turns "posting needs approval" on or off. Approval needs people with their own sign-ins.</summary>
    public async Task<SessionInfo> SetApprovalRequiredAsync(bool required, CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        if (required && !session.AccountsOn)
            throw new ValidationException([new ValidationIssue("approval", "approval.needs-accounts")]);
        var settings = await store.GetSettingsAsync(cancellationToken);
        settings.ApprovalRequired = required;
        await store.SaveSettingsAsync(settings, cancellationToken);
        return await DescribeAsync(cancellationToken);
    }

    /// <summary>Makes <see cref="AppSession.AccountsOn"/> right again after users were added or removed.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (files.Current is not null)
            session.SetAccountsOn(await users.AnyUsersAsync(cancellationToken));
    }
}
