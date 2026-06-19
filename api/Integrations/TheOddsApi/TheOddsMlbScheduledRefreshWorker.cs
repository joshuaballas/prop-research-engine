using Microsoft.Extensions.Options;
using SportsBetting.Api.Endpoints;

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
            logger.LogInformation("The Odds API MLB scheduled refresh worker is disabled. No The Odds API calls will be made.");
            return;
        }

        var interval = options.Value.GetInterval();
        var plannerInterval = plannerOptions.Value.GetPlannerInterval();
        logger.LogWarning(
            "The Odds API MLB scheduled refresh worker is enabled. Scheduled cadence: {Interval}. Planner check interval config: {PlannerInterval}. BookmakerPreset: {BookmakerPreset}. RequireForceLive: {RequireForceLive}. AllowScheduledLiveRefresh: {AllowScheduledLiveRefresh}.",
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
                if (ShouldWaitForCadence(schedulingPlan, schedulingObservedAt))
                {
                    var cadenceWakeAt = schedulingPlan.NextEligibleRefreshUtc;
                    state.MarkWaiting(schedulingObservedAt, cadenceWakeAt);
                    logger.LogInformation(
                        "The Odds API MLB scheduled refresh waiting for cadence. Next eligible refresh: {NextEligibleRefreshUtc}.",
                        cadenceWakeAt);

                    await DelayUntilAsync(cadenceWakeAt, stoppingToken);
                    continue;
                }

                logger.LogInformation("The Odds API MLB scheduled refresh worker running planning cycle.");
                var plan = await RunGuardedCycleAsync(stoppingToken);
                var observedAt = DateTimeOffset.UtcNow;
                var nextWakeAt = ResolveNextWakeAt(plan, observedAt, interval);
                state.MarkWaiting(observedAt, nextWakeAt);

                await DelayUntilAsync(nextWakeAt, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("The Odds API MLB scheduled refresh worker is stopping.");
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

    private static bool ShouldWaitForCadence(TheOddsRefreshPlanResult plan, DateTimeOffset observedAt)
    {
        return plan.SkipReason == "CadenceNotDue" &&
            plan.NextEligibleRefreshUtc > observedAt;
    }

    private static Task DelayUntilAsync(DateTimeOffset wakeAt, CancellationToken cancellationToken)
    {
        var delay = wakeAt - DateTimeOffset.UtcNow;
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

    private static DateTimeOffset ResolveNextWakeAt(
        TheOddsRefreshPlanResult plan,
        DateTimeOffset observedAt,
        TimeSpan interval)
    {
        if (plan.SkipReason == "CadenceNotDue" &&
            plan.NextEligibleRefreshUtc > observedAt)
        {
            return plan.NextEligibleRefreshUtc;
        }

        return observedAt.Add(interval);
    }

    private async Task<TheOddsRefreshPlanResult> RunGuardedCycleAsync(CancellationToken cancellationToken)
    {
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
                "The Odds API MLB scheduled refresh skipped by planner. Reason: {Reason}. EstimatedApiCalls: {EstimatedApiCalls}. BudgetAllowed: {BudgetAllowed}. ProviderAvailable: {ProviderAvailable}.",
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

        await RunRefreshCycleAsync(cancellationToken);
        return plan;
    }

    private async Task RunRefreshCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var refreshService = scope.ServiceProvider.GetRequiredService<ITheOddsMlbRefreshService>();
            var refreshOptions = options.Value;
            var request = new TheOddsMlbRefreshRequest(
                Date: null,
                DryRun: false,
                ForceLive: true,
                BookmakerPreset: refreshOptions.BookmakerPreset,
                IncludeDfs: null,
                IncludeCandidates: refreshOptions.IncludeCandidates,
                IncludeLineShopping: refreshOptions.IncludeLineShopping,
                IncludeFullDiagnostics: refreshOptions.IncludeFullDiagnostics,
                FreshnessMinutes: refreshOptions.FreshnessMinutes);

            await refreshService.RefreshCurrentAsync(request, httpContext: null, cancellationToken);
            state.MarkCompleted(DateTimeOffset.UtcNow);
            logger.LogInformation(
                "The Odds API MLB scheduled refresh completed. BookmakerPreset: {BookmakerPreset}. IncludeCandidates: {IncludeCandidates}. IncludeLineShopping: {IncludeLineShopping}.",
                refreshOptions.BookmakerPreset,
                refreshOptions.IncludeCandidates,
                refreshOptions.IncludeLineShopping);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("The Odds API MLB scheduled refresh cycle was canceled during shutdown.");
            throw;
        }
        catch (Exception exception)
        {
            state.MarkFailed(DateTimeOffset.UtcNow, exception.Message);
            logger.LogError(exception, "The Odds API MLB scheduled refresh failed.");
        }
    }
}
