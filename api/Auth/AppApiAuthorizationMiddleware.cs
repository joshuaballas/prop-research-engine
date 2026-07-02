using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Auth;

public sealed class AppApiAuthorizationMiddleware(
    RequestDelegate next,
    IHostEnvironment environment,
    IOptions<AuthRuntimeOptions> authOptions)
{
    public async Task InvokeAsync(HttpContext context, IAppUserService appUserService)
    {
        var path = context.Request.Path;
        if (!RequiresAppAuthorization(path, out var ownerOrAdminRequired, out var debugPath))
        {
            await next(context);
            return;
        }

        if (environment.IsEnvironment("Testing") && !authOptions.Value.RequireAuthenticationInTesting)
        {
            await next(context);
            return;
        }

        if (debugPath && environment.IsDevelopment() && authOptions.Value.AllowAnonymousDebugInDevelopment)
        {
            await next(context);
            return;
        }

        var result = await context.AuthenticateAsync();
        if (!result.Succeeded || result.Principal is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication is required." });
            return;
        }

        context.User = result.Principal;
        var current = await appUserService.GetOrSyncCurrentUserAsync(context.User, context.RequestAborted);
        if (current is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication is required." });
            return;
        }

        if (current.User.Status == AppUserStatuses.Disabled)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Account access is disabled." });
            return;
        }

        if (ownerOrAdminRequired && current.User.Role is not (AppUserRoles.Owner or AppUserRoles.Admin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Admin access is required." });
            return;
        }

        await next(context);
    }

    private static bool RequiresAppAuthorization(PathString path, out bool ownerOrAdminRequired, out bool debugPath)
    {
        ownerOrAdminRequired = false;
        debugPath = false;

        if (path.StartsWithSegments("/api/plans", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || path == "/")
        {
            return false;
        }

        if (path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            ownerOrAdminRequired = true;
            return true;
        }

        if (path.StartsWithSegments("/api/debug", StringComparison.OrdinalIgnoreCase))
        {
            ownerOrAdminRequired = true;
            debugPath = true;
            return true;
        }

        if (path.StartsWithSegments("/research", StringComparison.OrdinalIgnoreCase))
        {
            ownerOrAdminRequired = true;
            debugPath = true;
            return true;
        }

        if (path.StartsWithSegments("/api/me", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/account", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/billing", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/checkout", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/markets", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/mlb", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/dfs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/players", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
