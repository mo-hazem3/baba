namespace Baba.Api.Security;

/// <summary>
/// Only answers requests addressed to this computer (localhost, 127.0.0.1 or [::1]). This stops "DNS rebinding", where a
/// web page on another domain is pointed at 127.0.0.1 to reach the local API.
/// </summary>
public sealed class HostFilterMiddleware(RequestDelegate next, BabaApiOptions options)
{
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "[::1]"];

    public Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        var allowed = LocalHosts.Contains(host, StringComparer.OrdinalIgnoreCase)
                      || options.AdditionalAllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

        if (allowed)
            return next(context);

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    }
}
