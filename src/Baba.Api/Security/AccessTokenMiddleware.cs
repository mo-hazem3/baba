using System.Security.Cryptography;
using System.Text;

namespace Baba.Api.Security;

/// <summary>
/// Requires the per-launch token on every <c>/api</c> request, and rejects changes that come from another web origin.
/// The desktop host stores the token in a strict same-site cookie before showing the app.
/// </summary>
public sealed class AccessTokenMiddleware(RequestDelegate next, BabaApiOptions options)
{
    public const string HeaderName = "X-Baba-Token";
    public const string CookieName = "baba-token";

    public async Task InvokeAsync(HttpContext context)
    {
        if (options.AccessToken is { Length: > 0 } expected && context.Request.Path.StartsWithSegments("/api"))
        {
            var supplied = context.Request.Headers[HeaderName].FirstOrDefault() ?? context.Request.Cookies[CookieName];
            if (supplied is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!IsReadOnly(context.Request.Method) && context.Request.Headers.Origin.FirstOrDefault() is { } origin && !OriginAllowed(context, origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await next(context);
    }

    private static bool IsReadOnly(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private bool OriginAllowed(HttpContext context, string origin) =>
        string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase)
        || options.AdditionalAllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
}
