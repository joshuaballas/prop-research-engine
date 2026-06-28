using System.Text.Json;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Diagnostics;
using SportsBetting.Api.Endpoints;
using SportsBetting.Api.Integrations.Results.Soccer;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsMlbScheduledRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TheOddsScheduledRefreshOptions> options,
    IOptions<TheOddsRefreshPlannerOptions> plannerOptions,
    ITheOddsScheduledRefreshState state,
    ILogger<TheOddsMlbScheduledRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            state.MarkDisabled();
            logger.LogInformation("The Odds API scheduled refresh worker is disabled. No The Odds API calls will be made.");
            return;
        }

        var interval = options.Value.GetInterval();
        var plannerInterval = plannerOptions.Value.GetPlannerInterval();
        logger.LogWarning(
            "The Odds API scheduled refresh worker is enabled. Scheduled cadence: {Interval}. Planner check interval config: {PlannerInterval}. BookmakerPreset: {BookmakerPreset}. RequireForceLive: {RequireForceLive}. AllowScheduledLiveRefresh: {AllowScheduledLiveRefresh}.",
            interval,
            plannerInterval,
            options.Value.BookmakerPreset,
            options.Value.RequireForceLive,
            options.Value.AllowScheduledLiveRefresh);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var schedulingPlan = await BuildSchedulingPlanAsync(stoppingToken);
                var schedulingObservedAt = DateTimeOffset.UtcNow;
                var schedulingSnapshot = state.GetSnapshot();
                if (IsActiveRun(schedulingSnapshot))
                {
                    logger.LogWarning(
                        "The Odds API scheduled refresh loop observed an active run and will not start another. CurrentRunStartedAtUtc: {CurrentRunStartedAtUtc}.",
                        schedulingSnapshot.LastRunStartedAtUtc);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var nextRefreshAt = ResolveNextRefreshAt(schedulingPlan, schedulingSnapshot, schedulingObservedAt, interval);
                state.MarkWaiting(schedulingObservedAt, nextRefreshAt);
                logger.LogInformation(
                    "The Odds API scheduled refresh waiting for next simple cadence run. NextRefreshAtUtc: {NextRefreshAtUtc}.",
                    nextRefreshAt);

                await DelayUntilAsync(nextRefreshAt, stoppingToken);
                await RunGuardedCycleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("The Odds API scheduled refresh worker is stopping.");
        }
    }

    private async Task<TheOddsRefreshPlanResult> BuildSchedulingPlanAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var planner = scope.ServiceProvider.GetRequiredService<ITheOddsRefreshPlannerService>();
        var plan = await planner.BuildPlanAsync(cancellationToken);
        state.MarkPlan(plan);
        return plan;
    }

    private static Task DelayUntilAsync(DateTimeOffset wakeAt, CancellationToken cancellationToken)
    {
        var delay = wakeAt - DateTimeOffset.UtcNow;
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

    internal static DateTimeOffset ResolveNextRefreshAt(
        TheOddsRefreshPlanResult plan,
        TheOddsScheduledRefreshStateSnapshot snapshot,
        DateTimeOffset observedAt,
        TimeSpan interval)
    {
        var latestPlanSuccessAt = ResolveLatestSuccessfulRefreshCompletedAt(plan);
        var anchor = Max(
            snapshot.LastSuccessfulRefreshCompletedAtUtc,
            snapshot.LastRunCompletedAtUtc,
            latestPlanSuccessAt);

        if (!anchor.HasValue)
        {
            return observedAt;
        }

        return anchor.Value.Add(interval);
    }

    internal static DateTimeOffset? ResolveLatestSuccessfulRefreshCompletedAt(TheOddsRefreshPlanResult plan)
    {
        return plan.Targets
            .Where(target => target.Enabled && target.LatestSuccessfulRefreshCompletedAtUtc.HasValue)
            .Select(target => target.LatestSuccessfulRefreshCompletedAtUtc)
            .Max() ?? plan.LastSuccessfulRefreshAtUtc;
    }

    private static DateTimeOffset? Max(params DateTimeOffset?[] values)
    {
        return values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max() is { } max && max != default
                ? max
                : null;
    }

    private async Task<TheOddsRefreshPlanResult?> RunGuardedCycleAsync(CancellationToken cancellationToken)
    {
        var snapshot = state.GetSnapshot();
        if (IsActiveRun(snapshot))
        {
            logger.LogWarning(
                "The Odds API scheduled refresh skipped because another scheduled refresh is already marked running. CurrentRunStartedAtUtc: {CurrentRunStartedAtUtc}.",
                snapshot.LastRunStartedAtUtc);
            return null;
        }

        var startedAt = DateTimeOffset.UtcNow;
        state.MarkStarted(startedAt);

        await using var scope = scopeFactory.CreateAsyncScope();
        var planner = scope.ServiceProvider.GetRequiredService<ITheOddsRefreshPlannerService>();
        var plan = await planner.BuildPlanAsync(cancellationToken);
        await planner.PersistDecisionAsync(plan, "ScheduledWorker", cancellationToken);
        state.MarkPlan(plan);

        if (!plan.ShouldRefresh)
        {
            var reason = plan.SkipReason ?? "PlannerSkip";
            state.MarkSkipped(DateTimeOffset.UtcNow, reason);
            logger.LogInformation(
                "The Odds API scheduled refresh skipped by planner. Reason: {Reason}. EstimatedApiCalls: {EstimatedApiCalls}. BudgetAllowed: {BudgetAllowed}. ProviderAvailable: {ProviderAvailable}.",
                reason,
                plan.EstimatedApiCalls,
                plan.Budget.BudgetAllowed,
                plan.ProviderAvailable);
            return plan;
        }

        if (!plan.ProviderAvailable)
        {
            const string reason = "Scheduled refresh skipped because provider status is unavailable.";
            state.MarkSkipped(DateTimeOffset.UtcNow, reason);
            logger.LogWarning("{Reason}", reason);
            return plan;
        }

        if (!plan.Budget.BudgetAllowed)
        {
            const string reason = "Scheduled refresh skipped because planner credit budget is not allowed.";
            state.MarkSkipped(DateTimeOffset.UtcNow, reason);
            logger.LogWarning("{Reason}", reason);
            return plan;
        }

        if (!options.Value.AllowScheduledLiveRefresh)
        {
            const string reason = "Scheduled live refresh is blocked because TheOddsScheduledRefresh:AllowScheduledLiveRefresh is false.";
            state.MarkBlocked(DateTimeOffset.UtcNow, reason);
            logger.LogWarning("{Reason}", reason);
            return plan;
        }

        if (options.Value.RequireForceLive && !options.Value.AllowScheduledLiveRefresh)
        {
            const string reason = "Scheduled live refresh is blocked because TheOddsScheduledRefresh:RequireForceLive is true. Set it false only in explicit local/production ops config after credit policy is approved.";
            state.MarkBlocked(DateTimeOffset.UtcNow, reason);
            logger.LogWarning("{Reason}", reason);
            return plan;
        }

        await RunRefreshCycleAsync(plan, cancellationToken);
        return plan;
    }

    private static bool IsActiveRun(TheOddsScheduledRefreshStateSnapshot snapshot)
    {
        return string.Equals(snapshot.Status, "running", StringComparison.OrdinalIgnoreCase)
            && snapshot.LastRunStartedAtUtc.HasValue
            && !snapshot.LastRunCompletedAtUtc.HasValue;
    }

    private async Task RunRefreshCycleAsync(TheOddsRefreshPlanResult plan, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var refreshOptions = options.Value;
            var eligibleTargets = plan.Targets
                .Where(target => target.ShouldRefresh)
                .DistinctBy(target => target.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (eligibleTargets.Length == 0)
            {
                state.MarkSkipped(DateTimeOffset.UtcNow, "No eligible refresh targets in this scheduled cycle.");
                logger.LogInformation("The Odds API scheduled refresh cycle had no eligible refresh targets.");
                return;
            }

            foreach (var target in eligibleTargets)
            {
                try
                {
                    if (string.Equals(target.Sport, "MLB", StringComparison.OrdinalIgnoreCase))
                    {
                        await RunMlbTargetAsync(scope.ServiceProvider, target, refreshOptions, cancellationToken);
                        continue;
                    }

                    if (IsSoccerWorldCupSportsbookTarget(target))
                    {
                        await RunSoccerWorldCupSportsbookTargetAsync(scope.ServiceProvider, target, refreshOptions, cancellationToken);
                        continue;
                    }

                    logger.LogWarning(
                        "The Odds API scheduled target {TargetKey} ({Sport}/{Competition}/{ProviderCategory}) is configured but scheduled execution is not wired for this target yet.",
                        target.Key,
                        target.Sport,
                        target.Competition,
                        target.ProviderCategory);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "The Odds API scheduled refresh target failed. TargetKey: {TargetKey}. Sport: {Sport}. Competition: {Competition}. ProviderCategory: {ProviderCategory}. BookmakerPreset: {BookmakerPreset}.",
                        target.Key,
                        target.Sport,
                        target.Competition,
                        target.ProviderCategory,
                        target.BookmakerPreset);
                }
            }

            state.MarkCompleted(DateTimeOffset.UtcNow);
            logger.LogInformation(
                "The Odds API scheduled refresh cycle completed. EligibleTargets: {EligibleTargets}. IncludeCandidates: {IncludeCandidates}. IncludeLineShopping: {IncludeLineShopping}.",
                eligibleTargets.Length,
                refreshOptions.IncludeCandidates,
                refreshOptions.IncludeLineShopping);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("The Odds API scheduled refresh cycle was canceled during shutdown.");
            throw;
        }
        catch (Exception exception)
        {
            state.MarkFailed(DateTimeOffset.UtcNow, exception.Message);
            logger.LogError(exception, "The Odds API scheduled refresh failed.");
        }
    }

    private static bool IsSoccerWorldCupSportsbookTarget(TheOddsRefreshTargetPlan target)
    {
        return string.Equals(target.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Competition, "World Cup", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunMlbTargetAsync(
        IServiceProvider serviceProvider,
        TheOddsRefreshTargetPlan target,
        TheOddsScheduledRefreshOptions refreshOptions,
        CancellationToken cancellationToken)
    {
        var refreshService = serviceProvider.GetRequiredService<ITheOddsMlbRefreshService>();
        var request = new TheOddsMlbRefreshRequest(
            Date: null,
            DryRun: false,
            ForceLive: true,
            BookmakerPreset: target.BookmakerPreset,
            IncludeDfs: string.Equals(target.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase),
            IncludeCandidates: refreshOptions.IncludeCandidates,
            IncludeLineShopping: refreshOptions.IncludeLineShopping,
            IncludeFullDiagnostics: refreshOptions.IncludeFullDiagnostics,
            FreshnessMinutes: refreshOptions.FreshnessMinutes,
            TriggerSource: "ScheduledWorker");

        await refreshService.RefreshCurrentAsync(request, httpContext: null, cancellationToken);
        LogTargetCompleted(target);
    }

    private async Task RunSoccerWorldCupSportsbookTargetAsync(
        IServiceProvider serviceProvider,
        TheOddsRefreshTargetPlan target,
        TheOddsScheduledRefreshOptions refreshOptions,
        CancellationToken cancellationToken)
    {
        var theOddsApiOptions = serviceProvider.GetRequiredService<IOptions<TheOddsApiOptions>>();
        var adapter = serviceProvider.GetRequiredService<ITheOddsApiMlbPlayerPropsAdapter>();
        var ingestionService = serviceProvider.GetRequiredService<ITheOddsApiMlbEventIngestionService>();
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var errorLogger = serviceProvider.GetRequiredService<IApplicationErrorLogger>();
        var soccerIdentityResolutionService = serviceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var soccerIdentityResolutionOptions = serviceProvider.GetRequiredService<IOptions<SoccerIdentityResolutionOptions>>();

        var effectiveBookmakers = string.IsNullOrWhiteSpace(target.Bookmakers)
            ? JoinOrNull(target.EffectiveBookmakers)
            : target.Bookmakers;
        var request = new TheOddsMarketRefreshRequest(
            Sport: target.Sport,
            Competition: target.Competition,
            Date: null,
            FromDate: null,
            ToDate: null,
            DryRun: false,
            ForceLive: true,
            BookmakerPreset: target.BookmakerPreset,
            IncludeDfs: false,
            IncludeSportsbook: true,
            IncludeCandidates: refreshOptions.IncludeCandidates,
            IncludeLineShopping: refreshOptions.IncludeLineShopping,
            IncludeFullDiagnostics: refreshOptions.IncludeFullDiagnostics,
            FreshnessMinutes: refreshOptions.FreshnessMinutes,
            Markets: ToJsonStringElement(target.Markets),
            ProviderCategory: target.ProviderCategory,
            Region: target.Regions,
            Regions: target.Regions,
            Bookmakers: ToJsonStringElement(effectiveBookmakers),
            MaxEvents: target.MaxEvents);

        await TheOddsApiDebugEndpoints.BuildSoccerWorldCupRefreshPlanningResponseAsync(
            request,
            theOddsApiOptions.Value,
            adapter,
            ingestionService,
            dbContext,
            errorLogger,
            soccerIdentityResolutionService,
            soccerIdentityResolutionOptions.Value,
            httpContext: null,
            cancellationToken);

        LogTargetCompleted(target);
    }

    private static JsonElement? ToJsonStringElement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    private static string? JoinOrNull(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? null
            : string.Join(",", values);
    }

    private void LogTargetCompleted(TheOddsRefreshTargetPlan target)
    {
        logger.LogInformation(
            "The Odds API scheduled refresh target completed. TargetKey: {TargetKey}. Sport: {Sport}. Competition: {Competition}. ProviderCategory: {ProviderCategory}. BookmakerPreset: {BookmakerPreset}.",
            target.Key,
            target.Sport,
            target.Competition,
            target.ProviderCategory,
            target.BookmakerPreset);
    }
}
