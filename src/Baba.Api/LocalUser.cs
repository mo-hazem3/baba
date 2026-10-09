using Baba.Application.Abstractions;
using Baba.Application.Security;

namespace Baba.Api;

/// <summary>
/// The person who signed in to the company (their sign-in name is what the audit log records). Before anyone has, or in a company with
/// no users, the Windows user name is recorded. Cloud replaces this with real accounts.
/// </summary>
internal sealed class SessionCurrentUser(AppSession session) : ICurrentUser
{
    public string UserId => session.User?.UserName ?? Environment.UserName;
}
