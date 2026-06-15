using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Diagnostics;

public sealed class DebugAdminProtectionMiddleware(
    RequestDelegate next,
    IHostEnvironment environment,
    IOptions<DebugAdminOptions> options)
{
    private const string AdminKeyHeader = "X-Debug-Admin-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsDebugAdminPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (environment.IsDevelopment())
        {
            await next(context);
            return;
        }

        var debugAdminOptions = options.Value;
        if (!debugAdminOptions.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!debugAdminOptions.RequireApiKey)
        {
            await next(context);
            return;
        }

        if (IsPlaceholderOrMissing(debugAdminOptions.ApiKey)
            || !context.Request.Headers.TryGetValue(AdminKeyHeader, out var providedKeys)
            || !ConstantTimeEquals(providedKeys.FirstOrDefault(), debugAdminOptions.ApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Debug admin access is forbidden."
            });
            return;
        }

        await next(context);
    }

    private static bool IsDebugAdminPath(PathString path)
    {
        return path.StartsWithSegments("/api/debug", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/research", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderOrMissing(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), "DEBUG_ADMIN_KEY", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ConstantTimeEquals(string? providedValue, string expectedValue)
    {
        if (string.IsNullOrEmpty(providedValue))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedValue);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedValue);

        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
