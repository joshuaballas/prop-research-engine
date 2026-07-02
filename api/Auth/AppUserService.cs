using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Auth;

public sealed record CurrentAppUser(AppUser User, string ExternalSubjectId, string? SessionId);

public interface IAppUserService
{
    Task<CurrentAppUser?> GetOrSyncCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<bool> IsOwnerOrAdminAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class AppUserService(
    ApplicationDbContext dbContext,
    IHostEnvironment environment,
    IOptions<AuthRuntimeOptions> authOptions,
    TimeProvider timeProvider) : IAppUserService
{
    private const string ExternalProvider = "Clerk";

    public async Task<CurrentAppUser?> GetOrSyncCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subjectId = ReadClaim(principal, "sub")
            ?? ReadClaim(principal, ClaimTypes.NameIdentifier)
            ?? ReadClaim(principal, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var email = ReadClaim(principal, "email") ?? ReadClaim(principal, ClaimTypes.Email) ?? ReadClaim(principal, "primary_email_address");
        var displayName = ReadClaim(principal, "name") ?? ReadClaim(principal, "full_name") ?? ReadClaim(principal, ClaimTypes.Name);
        var sessionId = ReadClaim(principal, "sid");
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(row => row.ExternalAuthProvider == ExternalProvider && row.ExternalSubjectId == subjectId, cancellationToken);

        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                ExternalAuthProvider = ExternalProvider,
                ExternalSubjectId = subjectId,
                Email = NormalizeEmail(email),
                DisplayName = NormalizeNullable(displayName),
                Role = await ResolveNewUserRoleAsync(subjectId, email, cancellationToken),
                Status = AppUserStatuses.Active,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LastSeenAtUtc = now,
                LastSessionId = NormalizeNullable(sessionId)
            };
            dbContext.AppUsers.Add(user);
        }
        else
        {
            user.Email = NormalizeEmail(email) ?? user.Email;
            user.DisplayName = NormalizeNullable(displayName) ?? user.DisplayName;
            user.UpdatedAtUtc = now;
            user.LastSeenAtUtc = now;
            user.LastSessionId = NormalizeNullable(sessionId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CurrentAppUser(user, subjectId, sessionId);
    }

    public async Task<bool> IsOwnerOrAdminAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var current = await GetOrSyncCurrentUserAsync(principal, cancellationToken);
        if (current?.User.Status != AppUserStatuses.Active)
        {
            return false;
        }

        return current.User.Role is AppUserRoles.Owner or AppUserRoles.Admin;
    }

    private async Task<string> ResolveNewUserRoleAsync(string subjectId, string? email, CancellationToken cancellationToken)
    {
        var options = authOptions.Value;
        if (options.BootstrapOwnerExternalSubjectIds.Any(value => string.Equals(value, subjectId, StringComparison.Ordinal)))
        {
            return AppUserRoles.Owner;
        }

        var normalizedEmail = NormalizeEmail(email);
        if (!string.IsNullOrWhiteSpace(normalizedEmail)
            && options.BootstrapOwnerEmails.Any(value => string.Equals(value.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            return AppUserRoles.Owner;
        }

        if (environment.IsDevelopment()
            && options.AllowFirstUserOwnerBootstrapInDevelopment
            && !await dbContext.AppUsers.AnyAsync(row => row.Role == AppUserRoles.Owner, cancellationToken))
        {
            return AppUserRoles.Owner;
        }

        return AppUserRoles.Member;
    }

    private static string? ReadClaim(ClaimsPrincipal principal, string claimType)
    {
        return principal.Claims.FirstOrDefault(claim => string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string? NormalizeEmail(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
