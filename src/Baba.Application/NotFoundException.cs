namespace Baba.Application;

/// <summary>Something the request refers to (an account, a voucher) does not exist (any more).</summary>
public sealed class NotFoundException(string what) : Exception($"The {what} was not found.")
{
    public string What { get; } = what;
}
