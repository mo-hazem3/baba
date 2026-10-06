using Baba.Application.Abstractions;

namespace Baba.Api;

/// <summary>Desktop has no sign-in yet: the Windows user name is recorded as the actor. Cloud replaces this with real accounts.</summary>
internal sealed class LocalUser : ICurrentUser
{
    public string UserId { get; } = Environment.UserName;
}
