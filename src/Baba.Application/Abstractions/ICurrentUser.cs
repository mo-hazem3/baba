namespace Baba.Application.Abstractions;

/// <summary>
/// Who is acting. Desktop uses a local user (a file password and optional local users); the cloud edition
/// will plug in real accounts and roles behind this same interface.
/// </summary>
public interface ICurrentUser
{
    /// <summary>An opaque id stored in <c>CreatedBy</c>, <c>UpdatedBy</c> and the audit log.</summary>
    string UserId { get; }
}
