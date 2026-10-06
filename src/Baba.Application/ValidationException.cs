namespace Baba.Application;

/// <summary>
/// One thing wrong with a request. <see cref="Field"/> names the input and <see cref="Code"/> is a stable,
/// translatable code such as <c>password.too-short</c>. The UI shows the message in the user's language next to the field.
/// </summary>
public sealed record ValidationIssue(string Field, string Code);

public sealed class ValidationException(IReadOnlyList<ValidationIssue> issues)
    : Exception("The request is not valid: " + string.Join(", ", issues.Select(i => $"{i.Field} ({i.Code})")))
{
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;
}
