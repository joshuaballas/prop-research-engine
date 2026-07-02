using SportsBetting.Api.Auth;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Endpoints;

public static class AuthBillingEndpoints
{
    public static IEndpointRouteBuilder MapAuthBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/plans", async (IPlanBillingService plans, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await plans.GetPublicPlansAsync(cancellationToken));
        })
        .WithName("PlansPublicList")
        .WithSummary("Returns public active plans from local persisted plan data.");

        endpoints.MapGet("/api/me", async (
            HttpContext context,
            IAppUserService users,
            IPlanBillingService plans,
            CancellationToken cancellationToken) =>
        {
            var current = await users.GetOrSyncCurrentUserAsync(context.User, cancellationToken);
            if (current is null)
            {
                return Results.Unauthorized();
            }

            if (current.User.Status == AppUserStatuses.Disabled)
            {
                return Results.Forbid();
            }

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                readOnly = false,
                callsExternalApis = false,
                user = ToUserDto(current.User),
                billing = await plans.GetBillingAsync(current.User, cancellationToken)
            });
        })
        .WithName("CurrentAppUser");

        endpoints.MapGet("/api/account", async (
            HttpContext context,
            IAppUserService users,
            CancellationToken cancellationToken) =>
        {
            var current = await users.GetOrSyncCurrentUserAsync(context.User, cancellationToken);
            return current is null
                ? Results.Unauthorized()
                : Results.Ok(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    readOnly = true,
                    callsExternalApis = false,
                    user = ToUserDto(current.User)
                });
        })
        .WithName("AccountProfile");

        endpoints.MapGet("/api/billing", async (
            HttpContext context,
            IAppUserService users,
            IPlanBillingService billing,
            CancellationToken cancellationToken) =>
        {
            var current = await users.GetOrSyncCurrentUserAsync(context.User, cancellationToken);
            return current is null
                ? Results.Unauthorized()
                : Results.Ok(await billing.GetBillingAsync(current.User, cancellationToken));
        })
        .WithName("BillingOverview");

        endpoints.MapPost("/api/billing/select-plan", async (
            HttpContext context,
            SelectPlanRequest request,
            IAppUserService users,
            IPlanBillingService billing,
            CancellationToken cancellationToken) =>
        {
            var current = await users.GetOrSyncCurrentUserAsync(context.User, cancellationToken);
            if (current is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await billing.SelectPlanAsync(current.User, request, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("BillingSelectPlan");

        endpoints.MapPost("/api/checkout/intents", async (
            HttpContext context,
            CreateCheckoutIntentRequest request,
            IAppUserService users,
            IPlanBillingService billing,
            CancellationToken cancellationToken) =>
        {
            var current = await users.GetOrSyncCurrentUserAsync(context.User, cancellationToken);
            if (current is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await billing.CreateCheckoutIntentAsync(current.User, request, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CheckoutIntentCreate");

        endpoints.MapGet("/api/checkout/intents/{id:guid}", async (
            Guid id,
            HttpContext context,
            IAppUserService users,
            IPlanBillingService billing,
            CancellationToken cancellationToken) =>
        {
            var current = await users.GetOrSyncCurrentUserAsync(context.User, cancellationToken);
            if (current is null)
            {
                return Results.Unauthorized();
            }

            var isAdmin = current.User.Role is AppUserRoles.Owner or AppUserRoles.Admin;
            var intent = await billing.GetCheckoutIntentAsync(current.User, id, isAdmin, cancellationToken);
            return intent is null ? Results.NotFound(new { error = "Checkout intent was not found." }) : Results.Ok(intent);
        })
        .WithName("CheckoutIntentGet");

        endpoints.MapGet("/api/admin/plans", async (IPlanBillingService billing, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await billing.GetAdminPlansAsync(cancellationToken));
        })
        .WithName("AdminPlansList");

        endpoints.MapPost("/api/admin/plans", async (
            UpsertPlanRequest request,
            IPlanBillingService billing,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await billing.UpsertPlanAsync(request, id: null, cancellationToken));
        })
        .WithName("AdminPlansCreate");

        endpoints.MapPut("/api/admin/plans/{id:guid}", async (
            Guid id,
            UpsertPlanRequest request,
            IPlanBillingService billing,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await billing.UpsertPlanAsync(request, id, cancellationToken));
        })
        .WithName("AdminPlansUpdate");

        endpoints.MapPost("/api/admin/plans/{id:guid}/features", async (
            Guid id,
            UpsertPlanFeatureRequest request,
            IPlanBillingService billing,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await billing.AddFeatureAsync(id, request, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("AdminPlanFeaturesCreate");

        endpoints.MapPut("/api/admin/plan-features/{id:guid}", async (
            Guid id,
            UpsertPlanFeatureRequest request,
            IPlanBillingService billing,
            CancellationToken cancellationToken) =>
        {
            var result = await billing.UpdateFeatureAsync(id, request, cancellationToken);
            return result is null ? Results.NotFound(new { error = "Plan feature was not found." }) : Results.Ok(result);
        })
        .WithName("AdminPlanFeaturesUpdate");

        return endpoints;
    }

    private static object ToUserDto(AppUser user)
    {
        return new
        {
            user.Id,
            user.ExternalAuthProvider,
            user.ExternalSubjectId,
            user.Email,
            user.DisplayName,
            user.Role,
            user.Status,
            user.CreatedAtUtc,
            user.UpdatedAtUtc,
            user.LastSeenAtUtc,
            user.LastSessionId
        };
    }
}
