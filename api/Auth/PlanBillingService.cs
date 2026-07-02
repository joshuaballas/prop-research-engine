using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Auth;

public sealed record SelectPlanRequest(string PlanSlug, string BillingInterval);
public sealed record CreateCheckoutIntentRequest(string PlanSlug, string BillingInterval, string? ReturnUrl);
public sealed record UpsertPlanRequest(
    string Slug,
    string Name,
    string Summary,
    string? Description,
    string Status,
    bool IsPublic,
    int SortOrder,
    int? MonthlyPriceCents,
    int? AnnualPriceCents,
    string Currency,
    int? TrialDays);
public sealed record UpsertPlanFeatureRequest(string FeatureKey, string Label, string? Description, string? Value, bool Included, int SortOrder);

public interface IPlanBillingService
{
    Task<object> GetPublicPlansAsync(CancellationToken cancellationToken);
    Task<object> GetBillingAsync(AppUser user, CancellationToken cancellationToken);
    Task<object> SelectPlanAsync(AppUser user, SelectPlanRequest request, CancellationToken cancellationToken);
    Task<object> CreateCheckoutIntentAsync(AppUser user, CreateCheckoutIntentRequest request, CancellationToken cancellationToken);
    Task<object?> GetCheckoutIntentAsync(AppUser user, Guid id, bool isAdmin, CancellationToken cancellationToken);
    Task<object> GetAdminPlansAsync(CancellationToken cancellationToken);
    Task<object> UpsertPlanAsync(UpsertPlanRequest request, Guid? id, CancellationToken cancellationToken);
    Task<object> AddFeatureAsync(Guid planId, UpsertPlanFeatureRequest request, CancellationToken cancellationToken);
    Task<object?> UpdateFeatureAsync(Guid id, UpsertPlanFeatureRequest request, CancellationToken cancellationToken);
}

public sealed class PlanBillingService(ApplicationDbContext dbContext, TimeProvider timeProvider) : IPlanBillingService
{
    public async Task<object> GetPublicPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await LoadPublicPlanDtosAsync(cancellationToken);

        return new
        {
            generatedAtUtc = timeProvider.GetUtcNow(),
            readOnly = true,
            callsExternalApis = false,
            paymentProcessorConnected = false,
            plans
        };
    }

    public async Task<object> GetBillingAsync(AppUser user, CancellationToken cancellationToken)
    {
        var state = await dbContext.UserPlanStates
            .AsNoTracking()
            .Include(row => row.Plan)
                .ThenInclude(plan => plan!.Features)
            .FirstOrDefaultAsync(row => row.AppUserId == user.Id, cancellationToken);
        var intents = await dbContext.CheckoutIntents
            .AsNoTracking()
            .Include(row => row.Plan)
            .Where(row => row.AppUserId == user.Id)
            .OrderByDescending(row => row.CreatedAtUtc)
            .Take(10)
            .ToArrayAsync(cancellationToken);

        return new
        {
            generatedAtUtc = timeProvider.GetUtcNow(),
            readOnly = true,
            callsExternalApis = false,
            paymentProcessorConnected = false,
            currentPlanState = state is null ? null : ToUserPlanStateDto(state),
            availablePlans = await LoadPublicPlanDtosAsync(cancellationToken),
            checkoutIntents = intents.Select(ToCheckoutIntentDto).ToArray()
        };
    }

    public async Task<object> SelectPlanAsync(AppUser user, SelectPlanRequest request, CancellationToken cancellationToken)
    {
        var plan = await FindActivePlanAsync(request.PlanSlug, cancellationToken)
            ?? throw new InvalidOperationException("Plan was not found.");
        var interval = NormalizeBillingInterval(request.BillingInterval);
        var amount = PriceFor(plan, interval);
        var now = timeProvider.GetUtcNow();
        var state = await dbContext.UserPlanStates.FirstOrDefaultAsync(row => row.AppUserId == user.Id, cancellationToken);

        if (state is null)
        {
            state = new UserPlanState
            {
                Id = Guid.NewGuid(),
                AppUserId = user.Id,
                CreatedAtUtc = now
            };
            dbContext.UserPlanStates.Add(state);
        }

        state.PlanId = plan.Id;
        state.BillingInterval = amount <= 0 ? BillingIntervals.None : interval;
        state.Status = amount <= 0 ? UserPlanStatuses.Active : UserPlanStatuses.PendingCheckout;
        state.UpdatedAtUtc = now;
        if (amount <= 0)
        {
            state.CurrentPeriodStartUtc = now;
            state.CurrentPeriodEndUtc = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new
        {
            generatedAtUtc = now,
            callsExternalApis = false,
            writesRows = true,
            paymentProcessorConnected = false,
            plan = ToPlanDto(plan),
            currentPlanState = ToUserPlanStateDto(state),
            requiresExternalProcessor = amount > 0
        };
    }

    public async Task<object> CreateCheckoutIntentAsync(AppUser user, CreateCheckoutIntentRequest request, CancellationToken cancellationToken)
    {
        var plan = await FindActivePlanAsync(request.PlanSlug, cancellationToken)
            ?? throw new InvalidOperationException("Plan was not found.");
        var interval = NormalizeBillingInterval(request.BillingInterval);
        var amount = PriceFor(plan, interval);
        var now = timeProvider.GetUtcNow();
        var intent = new CheckoutIntent
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            PlanId = plan.Id,
            BillingInterval = amount <= 0 ? BillingIntervals.None : interval,
            AmountCents = amount,
            Currency = plan.Currency,
            Status = amount <= 0 ? CheckoutIntentStatuses.Completed : CheckoutIntentStatuses.RequiresExternalProcessor,
            ExternalBillingProvider = amount <= 0 ? null : "Stripe",
            ReturnUrl = string.IsNullOrWhiteSpace(request.ReturnUrl) ? null : request.ReturnUrl.Trim(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            CompletedAtUtc = amount <= 0 ? now : null
        };
        dbContext.CheckoutIntents.Add(intent);

        if (amount > 0)
        {
            await SelectPlanAsync(user, new SelectPlanRequest(plan.Slug, interval), cancellationToken);
        }
        else
        {
            await SelectPlanAsync(user, new SelectPlanRequest(plan.Slug, BillingIntervals.None), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new
        {
            generatedAtUtc = now,
            callsExternalApis = false,
            writesRows = true,
            checkoutIntentId = intent.Id,
            status = intent.Status,
            paymentProcessorConnected = false,
            externalProviderRequired = amount > 0 ? "Stripe" : null,
            checkoutUrl = (string?)null,
            message = amount > 0
                ? "Checkout is ready for payment provider integration."
                : "Free plan selection completed.",
            checkoutIntent = ToCheckoutIntentDto(intent, plan)
        };
    }

    public async Task<object?> GetCheckoutIntentAsync(AppUser user, Guid id, bool isAdmin, CancellationToken cancellationToken)
    {
        var intent = await dbContext.CheckoutIntents
            .AsNoTracking()
            .Include(row => row.Plan)
            .FirstOrDefaultAsync(row => row.Id == id && (isAdmin || row.AppUserId == user.Id), cancellationToken);
        return intent is null ? null : ToCheckoutIntentDto(intent);
    }

    public async Task<object> GetAdminPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await LoadPlansQuery()
            .OrderBy(plan => plan.SortOrder)
            .ThenBy(plan => plan.Name)
            .ToArrayAsync(cancellationToken);

        return new
        {
            generatedAtUtc = timeProvider.GetUtcNow(),
            readOnly = true,
            callsExternalApis = false,
            plans = plans.Select(ToPlanDto).ToArray()
        };
    }

    public async Task<object> UpsertPlanAsync(UpsertPlanRequest request, Guid? id, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var slug = NormalizeSlug(request.Slug);
        var plan = id.HasValue
            ? await dbContext.Plans.FirstOrDefaultAsync(row => row.Id == id.Value, cancellationToken)
            : null;

        if (plan is null)
        {
            plan = new Plan { Id = Guid.NewGuid(), CreatedAtUtc = now };
            dbContext.Plans.Add(plan);
        }

        plan.Slug = slug;
        plan.Name = request.Name.Trim();
        plan.Summary = request.Summary.Trim();
        plan.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        plan.Status = NormalizePlanStatus(request.Status);
        plan.IsPublic = request.IsPublic;
        plan.SortOrder = request.SortOrder;
        plan.MonthlyPriceCents = request.MonthlyPriceCents;
        plan.AnnualPriceCents = request.AnnualPriceCents;
        plan.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant();
        plan.TrialDays = request.TrialDays;
        plan.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToPlanDto(plan);
    }

    public async Task<object> AddFeatureAsync(Guid planId, UpsertPlanFeatureRequest request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Plans.AnyAsync(row => row.Id == planId, cancellationToken))
        {
            throw new InvalidOperationException("Plan was not found.");
        }

        var feature = new PlanFeature { Id = Guid.NewGuid(), PlanId = planId };
        ApplyFeature(feature, request);
        dbContext.PlanFeatures.Add(feature);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToPlanFeatureDto(feature);
    }

    public async Task<object?> UpdateFeatureAsync(Guid id, UpsertPlanFeatureRequest request, CancellationToken cancellationToken)
    {
        var feature = await dbContext.PlanFeatures.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (feature is null)
        {
            return null;
        }

        ApplyFeature(feature, request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToPlanFeatureDto(feature);
    }

    private IQueryable<Plan> LoadPlansQuery()
    {
        return dbContext.Plans
            .AsNoTracking()
            .Include(plan => plan.Features.OrderBy(feature => feature.SortOrder));
    }

    private async Task<object[]> LoadPublicPlanDtosAsync(CancellationToken cancellationToken)
    {
        var plans = await LoadPlansQuery()
            .Where(plan => plan.Status == PlanStatuses.Active && plan.IsPublic)
            .OrderBy(plan => plan.SortOrder)
            .ToArrayAsync(cancellationToken);

        return plans.Select(ToPlanDto).ToArray();
    }

    private Task<Plan?> FindActivePlanAsync(string slug, CancellationToken cancellationToken)
    {
        var normalized = NormalizeSlug(slug);
        return LoadPlansQuery()
            .FirstOrDefaultAsync(plan => plan.Slug == normalized && plan.Status == PlanStatuses.Active, cancellationToken);
    }

    private static void ApplyFeature(PlanFeature feature, UpsertPlanFeatureRequest request)
    {
        feature.FeatureKey = NormalizeSlug(request.FeatureKey).Replace('-', '_');
        feature.Label = request.Label.Trim();
        feature.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        feature.Value = string.IsNullOrWhiteSpace(request.Value) ? null : request.Value.Trim();
        feature.Included = request.Included;
        feature.SortOrder = request.SortOrder;
    }

    private static string NormalizeBillingInterval(string value)
    {
        return value.Trim().Equals("Annual", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("Year", StringComparison.OrdinalIgnoreCase)
            ? BillingIntervals.Annual
            : value.Trim().Equals("None", StringComparison.OrdinalIgnoreCase)
                ? BillingIntervals.None
                : BillingIntervals.Monthly;
    }

    private static string NormalizePlanStatus(string value)
    {
        return value.Trim().Equals(PlanStatuses.Archived, StringComparison.OrdinalIgnoreCase)
            ? PlanStatuses.Archived
            : value.Trim().Equals(PlanStatuses.Draft, StringComparison.OrdinalIgnoreCase)
                ? PlanStatuses.Draft
                : PlanStatuses.Active;
    }

    private static string NormalizeSlug(string value)
    {
        var slug = string.IsNullOrWhiteSpace(value) ? "plan" : value.Trim().ToLowerInvariant();
        return string.Join("-", slug.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries))
            .Replace("_", "-", StringComparison.Ordinal);
    }

    private static int PriceFor(Plan plan, string interval)
    {
        return interval == BillingIntervals.Annual
            ? plan.AnnualPriceCents ?? plan.MonthlyPriceCents ?? 0
            : interval == BillingIntervals.None
                ? 0
                : plan.MonthlyPriceCents ?? 0;
    }

    private static object ToPlanDto(Plan plan)
    {
        return new
        {
            plan.Id,
            plan.Slug,
            plan.Name,
            plan.Summary,
            plan.Description,
            plan.Status,
            plan.IsPublic,
            plan.SortOrder,
            plan.MonthlyPriceCents,
            plan.AnnualPriceCents,
            plan.Currency,
            plan.TrialDays,
            features = plan.Features.OrderBy(feature => feature.SortOrder).Select(ToPlanFeatureDto).ToArray()
        };
    }

    private static object ToPlanFeatureDto(PlanFeature feature)
    {
        return new
        {
            feature.Id,
            feature.PlanId,
            feature.FeatureKey,
            feature.Label,
            feature.Description,
            feature.Value,
            feature.Included,
            feature.SortOrder
        };
    }

    private static object ToUserPlanStateDto(UserPlanState state)
    {
        return new
        {
            state.Id,
            state.AppUserId,
            state.PlanId,
            plan = state.Plan is null ? null : ToPlanDto(state.Plan),
            state.Status,
            state.BillingInterval,
            state.CurrentPeriodStartUtc,
            state.CurrentPeriodEndUtc,
            state.ExternalBillingProvider,
            state.ExternalCustomerId,
            state.ExternalSubscriptionId,
            state.CreatedAtUtc,
            state.UpdatedAtUtc
        };
    }

    private static object ToCheckoutIntentDto(CheckoutIntent intent)
    {
        return ToCheckoutIntentDto(intent, intent.Plan);
    }

    private static object ToCheckoutIntentDto(CheckoutIntent intent, Plan? plan)
    {
        return new
        {
            intent.Id,
            intent.AppUserId,
            intent.PlanId,
            planSlug = plan?.Slug,
            planName = plan?.Name,
            intent.BillingInterval,
            intent.AmountCents,
            intent.Currency,
            intent.Status,
            intent.ExternalBillingProvider,
            intent.ExternalCheckoutSessionId,
            intent.ReturnUrl,
            intent.CreatedAtUtc,
            intent.ExpiresAtUtc,
            intent.CompletedAtUtc
        };
    }
}
