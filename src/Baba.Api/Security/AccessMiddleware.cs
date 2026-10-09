using Baba.Application.Companies;
using Baba.Application.Security;

namespace Baba.Api.Security;

/// <summary>
/// After the access token: when the open company asks people to sign in, refuses every route until someone has, then checks the route's
/// permission against the person's role. Routes that are not in the <see cref="PermissionMap"/> are refused.
/// </summary>
public sealed class AccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AccessService access, AppSession session, ICompanyFiles files)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var rule = PermissionMap.Resolve(context.Request.Method, path);
        if (rule is AccessRule.Open)
        {
            // Opening, creating or closing a company starts a new session: nobody is signed in to the next one.
            if (context.Request.Method == HttpMethods.Post && path.StartsWith("/api/company/", StringComparison.OrdinalIgnoreCase)
                && path[13..].Trim('/').ToLowerInvariant() is "open" or "create" or "close")
            {
                session.Reset();
            }

            await next(context);
            return;
        }

        if (files.Current is not null && await access.NeedsSignInAsync(context.RequestAborted))
            throw new SignInRequiredException();

        if (files.Current is not null && session.AccountsOn && !session.IsSystem && session.User is { MustChangePassword: true })
            throw new PasswordChangeRequiredException();

        switch (rule)
        {
            case null:
                if (files.Current is not null && session.AccountsOn)
                    throw new ForbiddenException("unknown", Domain.Security.PermissionAction.View);
                break;
            case AccessRule.Needs needs when files.Current is not null:
                access.Require(needs.Area, needs.Action);
                break;
        }

        await next(context);
    }
}
