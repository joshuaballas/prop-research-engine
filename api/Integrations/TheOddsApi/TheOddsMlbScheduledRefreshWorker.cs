using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Diagnostics;
using SportsBetting.Api.Endpoints;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsMlbScheduledRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TheOddsScheduledRefreshOptions> options,
    IOptions<TheOddsRefreshPlannerOptions> plannerOptions,
    ITheOddsScheduledRefreshState state,
    ITheOddsLiveRefreshGate liveRefreshGate,
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
        var nextTargetRefreshAt = plan.Targets
            .Where(target => target.Enabled && target.ExecutionWired)
            .Select(target => (DateTimeOffset?)target.NextEligibleRefreshUtc)
            .Min();
        if (nextTargetRefreshAt.HasValue)
        {
            return nextTargetRefreshAt.Value <= observedAt ? observedAt : nextTargetRefreshAt.Value;
        }

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

        await using var cycleGate = await liveRefreshGate.EnterAsync(cancellationToken);
        state.MarkStarted(DateTimeOffset.UtcNow, plan.NextEligibleRefreshUtc);
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
            var refreshOptions = options.Value;
            var cycleId = Guid.NewGuid();
            var cycleStartedAtUtc = DateTimeOffset.UtcNow;
            var eligibleTargets = plan.Targets
                .Where(target => target.ShouldRefresh)
                .DistinctBy(target => target.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(target => target.NextEligibleRefreshUtc)
                .ThenBy(target => target.Priority)
                .ThenBy(target => target.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (eligibleTargets.Length == 0)
            {
                state.MarkSkipped(DateTimeOffset.UtcNow, "No eligible refresh targets in this scheduled cycle.");
                logger.LogInformation("The Odds API scheduled refresh cycle had no eligible refresh targets.");
                return;
            }

            var targetTasks = eligibleTargets
                .Select(target => Task.Run(
                    () => RunTargetIngestInOwnScopeAsync(target, refreshOptions, cycleId, cancellationToken),
                    cancellationToken))
                .ToArray();
            var targetResults = await Task.WhenAll(targetTasks);

            var finalizationStartedAtUtc = DateTimeOffset.UtcNow;
            var mlbFinalization = await RunMlbFinalizationOnceAsync(
                targetResults,
                refreshOptions,
                cycleId,
                cancellationToken);
            var finalizationCompletedAtUtc = DateTimeOffset.UtcNow;
            var cycleCompletedAtUtc = finalizationCompletedAtUtc;

            var targetFailures = targetResults.Where(result => !result.Succeeded).ToArray();
            var finalizationFailed = mlbFinalization is { Succeeded: false };
            var targetStartSpreadSeconds = CalculateTargetStartSpreadSeconds(targetResults);
            var targetOverlapDetected = DetectTargetOverlap(targetResults);
            var cycleDurationSeconds = Math.Round((cycleCompletedAtUtc - cycleStartedAtUtc).TotalSeconds, 3);

            if (targetFailures.Length > 0 || finalizationFailed)
            {
                var error = BuildCycleFailureMessage(targetFailures, mlbFinalization);
                state.MarkFailed(cycleCompletedAtUtc, error);
                logger.LogWarning(
                    "The Odds API scheduled refresh cycle completed with failures. ScheduledCycleId: {ScheduledCycleId}. EligibleTargets: {EligibleTargets}. TargetSuccessCount: {TargetSuccessCount}. TargetFailureCount: {TargetFailureCount}. FinalizationSucceeded: {FinalizationSucceeded}. CycleDurationSeconds: {CycleDurationSeconds}. TargetStartSpreadSeconds: {TargetStartSpreadSeconds}. TargetOverlapDetected: {TargetOverlapDetected}. Error: {Error}.",
                    cycleId,
                    eligibleTargets.Length,
                    targetResults.Count(result => result.Succeeded),
                    targetFailures.Length,
                    mlbFinalization?.Succeeded,
                    cycleDurationSeconds,
                    targetStartSpreadSeconds,
                    targetOverlapDetected,
                    error);
                return;
            }

            state.MarkCompleted(cycleCompletedAtUtc);
            logger.LogInformation(
                "The Odds API scheduled refresh cycle completed. ScheduledCycleId: {ScheduledCycleId}. EligibleTargets: {EligibleTargets}. TargetSuccessCount: {TargetSuccessCount}. TargetFailureCount: {TargetFailureCount}. FinalizationStartedAtUtc: {FinalizationStartedAtUtc}. FinalizationCompletedAtUtc: {FinalizationCompletedAtUtc}. CycleDurationSeconds: {CycleDurationSeconds}. TargetStartSpreadSeconds: {TargetStartSpreadSeconds}. TargetOverlapDetected: {TargetOverlapDetected}. IncludeCandidates: {IncludeCandidates}. IncludeLineShopping: {IncludeLineShopping}.",
                cycleId,
                eligibleTargets.Length,
                targetResults.Count(result => result.Succeeded),
                targetFailures.Length,
                finalizationStartedAtUtc,
                finalizationCompletedAtUtc,
                cycleDurationSeconds,
                targetStartSpreadSeconds,
                targetOverlapDetected,
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
            await PersistScheduledRefreshExceptionAsync(
                exception,
                "RunRefreshCycle",
                runId: null,
                sport: null,
                provider: "The Odds API",
                metadata: new
                {
                    plan.ShouldRefresh,
                    targetCount = plan.Targets.Count,
                    eligibleTargetCount = plan.Targets.Count(target => target.ShouldRefresh)
                });
        }
    }

    private static bool IsSoccerWorldCupTarget(TheOddsRefreshTargetPlan target)
    {
        return string.Equals(target.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Competition, "World Cup", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(target.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
                || string.Equals(target.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ScheduledTargetExecutionResult> RunTargetIngestInOwnScopeAsync(
        TheOddsRefreshTargetPlan target,
        TheOddsScheduledRefreshOptions refreshOptions,
        Guid scheduledCycleId,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            if (string.Equals(target.Sport, "MLB", StringComparison.OrdinalIgnoreCase))
            {
                return await RunMlbTargetAsync(scope.ServiceProvider, target, refreshOptions, scheduledCycleId, startedAtUtc, cancellationToken);
            }

            if (IsSoccerWorldCupTarget(target))
            {
                return await RunSoccerWorldCupTargetAsync(scope.ServiceProvider, target, refreshOptions, scheduledCycleId, startedAtUtc, cancellationToken);
            }

            var completedAtUtc = DateTimeOffset.UtcNow;
            var error = $"Scheduled execution is not wired for target {target.Key}.";
            logger.LogWarning(
                "The Odds API scheduled target {TargetKey} ({Sport}/{Competition}/{ProviderCategory}) is configured but scheduled execution is not wired for this target yet.",
                target.Key,
                target.Sport,
                target.Competition,
                target.ProviderCategory);

            return ScheduledTargetExecutionResult.Failed(target, startedAtUtc, completedAtUtc, error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var completedAtUtc = DateTimeOffset.UtcNow;
            logger.LogError(
                exception,
                "The Odds API scheduled refresh target failed. ScheduledCycleId: {ScheduledCycleId}. TargetKey: {TargetKey}. Sport: {Sport}. Competition: {Competition}. ProviderCategory: {ProviderCategory}. BookmakerPreset: {BookmakerPreset}.",
                scheduledCycleId,
                target.Key,
                target.Sport,
                target.Competition,
                target.ProviderCategory,
                target.BookmakerPreset);
            await PersistScheduledRefreshExceptionAsync(
                exception,
                "RunTargetIngest",
                runId: scheduledCycleId,
                sport: target.Sport,
                provider: "The Odds API",
                metadata: new
                {
                    scheduledCycleId,
                    target.Key,
                    target.Sport,
                    target.Competition,
                    target.ProviderCategory,
                    target.BookmakerPreset,
                    startedAtUtc,
                    completedAtUtc
                });

            return ScheduledTargetExecutionResult.Failed(target, startedAtUtc, completedAtUtc, exception.Message);
        }
    }

    private async Task<ScheduledTargetExecutionResult> RunMlbTargetAsync(
        IServiceProvider serviceProvider,
        TheOddsRefreshTargetPlan target,
        TheOddsScheduledRefreshOptions refreshOptions,
        Guid scheduledCycleId,
        DateTimeOffset startedAtUtc,
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
            TriggerSource: "ScheduledWorker",
            ScheduledCycleId: scheduledCycleId,
            TargetKey: target.Key,
            TargetLaunchStartedAtUtc: startedAtUtc,
            Bookmakers: string.Equals(target.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
                ? JoinOrNull(target.EffectiveBookmakers)
                : null,
            ExpandedBookmakers: string.Equals(target.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
                ? JoinOrNull(target.EffectiveBookmakers)
                : null,
            DfsBookmakers: string.Equals(target.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)
                ? JoinOrNull(target.EffectiveBookmakers)
                : null,
            Markets: target.Markets,
            DeferPublicFinalization: true,
            BypassLiveRefreshGate: true);

        await refreshService.RefreshCurrentAsync(request, httpContext: null, cancellationToken);
        var run = await FindTargetRefreshRunAsync(serviceProvider, target, scheduledCycleId, startedAtUtc, cancellationToken);
        var completedAtUtc = DateTimeOffset.UtcNow;
        var succeeded = run is not null && run.Succeeded && !run.Failed && !run.Blocked;
        var error = run is null
            ? "No persisted target refresh run was found after scheduled MLB target ingest."
            : run.ErrorMessage;
        LogTargetCompleted(target, scheduledCycleId, succeeded, startedAtUtc, completedAtUtc, run);
        return new ScheduledTargetExecutionResult(
            target,
            startedAtUtc,
            completedAtUtc,
            succeeded,
            error,
            run?.Id,
            run?.ActualApiCalls,
            run?.PlatformPropsCreated,
            run?.PlatformPropsUpdated,
            run?.PlatformPropsUpserted);
    }

    private async Task<ScheduledTargetExecutionResult> RunSoccerWorldCupTargetAsync(
        IServiceProvider serviceProvider,
        TheOddsRefreshTargetPlan target,
        TheOddsScheduledRefreshOptions refreshOptions,
        Guid scheduledCycleId,
        DateTimeOffset startedAtUtc,
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
        var isDfs = string.Equals(target.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase);
        var request = new TheOddsMarketRefreshRequest(
            Sport: target.Sport,
            Competition: target.Competition,
            Date: null,
            FromDate: null,
            ToDate: null,
            DryRun: false,
            ForceLive: true,
            BookmakerPreset: target.BookmakerPreset,
            IncludeDfs: isDfs,
            IncludeSportsbook: !isDfs,
            IncludeCandidates: refreshOptions.IncludeCandidates,
            IncludeLineShopping: refreshOptions.IncludeLineShopping,
            IncludeFullDiagnostics: refreshOptions.IncludeFullDiagnostics,
            FreshnessMinutes: refreshOptions.FreshnessMinutes,
            Markets: ToJsonStringElement(target.Markets),
            ProviderCategory: target.ProviderCategory,
            Region: target.Regions,
            Regions: target.Regions,
            Bookmakers: ToJsonStringElement(effectiveBookmakers),
            MaxEvents: target.MaxEvents,
            TriggerSource: "ScheduledWorker",
            ScheduledCycleId: scheduledCycleId,
            TargetKey: target.Key,
            TargetLaunchStartedAtUtc: startedAtUtc);

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

        var run = await FindTargetRefreshRunAsync(serviceProvider, target, scheduledCycleId, startedAtUtc, cancellationToken);
        var completedAtUtc = DateTimeOffset.UtcNow;
        var succeeded = run is not null && run.Succeeded && !run.Failed && !run.Blocked;
        var error = run is null
            ? "No persisted target refresh run was found after scheduled Soccer World Cup target ingest."
            : run.ErrorMessage;
        LogTargetCompleted(target, scheduledCycleId, succeeded, startedAtUtc, completedAtUtc, run);
        return new ScheduledTargetExecutionResult(
            target,
            startedAtUtc,
            completedAtUtc,
            succeeded,
            error,
            run?.Id,
            run?.ActualApiCalls,
            run?.PlatformPropsCreated,
            run?.PlatformPropsUpdated,
            run?.PlatformPropsUpserted);
    }

    private async Task<TheOddsScheduledMlbFinalizationResult?> RunMlbFinalizationOnceAsync(
        IReadOnlyCollection<ScheduledTargetExecutionResult> targetResults,
        TheOddsScheduledRefreshOptions refreshOptions,
        Guid scheduledCycleId,
        CancellationToken cancellationToken)
    {
        var mlbResults = targetResults
            .Where(result => string.Equals(result.Target.Sport, "MLB", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (mlbResults.Length == 0)
        {
            return null;
        }

        var failedMlbTargets = mlbResults
            .Where(result => !result.Succeeded)
            .ToArray();
        if (failedMlbTargets.Length > 0)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var completedAtUtc = startedAtUtc;
            return new TheOddsScheduledMlbFinalizationResult(
                false,
                RefreshRunId: null,
                PredictionCount: 0,
                LineShoppingGroupCount: 0,
                ActiveCandidateCount: 0,
                startedAtUtc,
                completedAtUtc,
                $"MLB finalization skipped because {failedMlbTargets.Length} MLB target ingest(s) failed.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var refreshService = scope.ServiceProvider.GetRequiredService<ITheOddsMlbRefreshService>();
        return await refreshService.FinalizeScheduledCycleAsync(
            new TheOddsScheduledMlbFinalizationRequest(
                scheduledCycleId,
                Date: null,
                ProviderCategories: mlbResults
                    .Select(result => result.Target.ProviderCategory)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                BookmakerKeys: mlbResults
                    .SelectMany(result => result.Target.EffectiveBookmakers)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Markets: mlbResults
                    .SelectMany(result => SplitCsv(result.Target.Markets))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                TargetRefreshRunIds: mlbResults
                    .Select(result => result.RefreshRunId)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .ToArray(),
                refreshOptions.IncludeCandidates,
                refreshOptions.IncludeLineShopping,
                refreshOptions.IncludeFullDiagnostics,
                refreshOptions.FreshnessMinutes),
            cancellationToken);
    }

    private static string[] SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<TheOddsRefreshRun?> FindTargetRefreshRunAsync(
        IServiceProvider serviceProvider,
        TheOddsRefreshTargetPlan target,
        Guid scheduledCycleId,
        DateTimeOffset targetStartedAtUtc,
        CancellationToken cancellationToken)
    {
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var candidates = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(run => run.Provider == "The Odds API"
                && run.Sport == target.Sport
                && run.StartedAtUtc >= targetStartedAtUtc.AddMinutes(-1))
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.CompletedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(run =>
            string.Equals(ReadDiagnosticsString(run.DiagnosticsJson, "scheduledCycleId"), scheduledCycleId.ToString(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ReadDiagnosticsString(run.DiagnosticsJson, "targetKey"), target.Key, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadDiagnosticsString(JsonDocument? diagnosticsJson, string propertyName)
    {
        if (diagnosticsJson is null)
        {
            return null;
        }

        return TryGetPropertyIgnoreCase(diagnosticsJson.RootElement, propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static decimal CalculateTargetStartSpreadSeconds(IReadOnlyCollection<ScheduledTargetExecutionResult> targetResults)
    {
        return CalculateTargetStartSpreadSeconds(targetResults
            .Select(result => new ScheduledTargetTiming(result.StartedAtUtc, result.CompletedAtUtc))
            .ToArray());
    }

    private static bool DetectTargetOverlap(IReadOnlyCollection<ScheduledTargetExecutionResult> targetResults)
    {
        return DetectTargetOverlap(targetResults
            .Select(result => new ScheduledTargetTiming(result.StartedAtUtc, result.CompletedAtUtc))
            .ToArray());
    }

    internal static decimal CalculateTargetStartSpreadSeconds(IReadOnlyCollection<ScheduledTargetTiming> targetTimings)
    {
        if (targetTimings.Count <= 1)
        {
            return 0m;
        }

        var earliest = targetTimings.Min(result => result.StartedAtUtc);
        var latest = targetTimings.Max(result => result.StartedAtUtc);
        return Math.Round((decimal)(latest - earliest).TotalSeconds, 3);
    }

    internal static bool DetectTargetOverlap(IReadOnlyCollection<ScheduledTargetTiming> targetTimings)
    {
        var ordered = targetTimings
            .OrderBy(result => result.StartedAtUtc)
            .ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].StartedAtUtc < ordered[i - 1].CompletedAtUtc)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildCycleFailureMessage(
        IReadOnlyCollection<ScheduledTargetExecutionResult> targetFailures,
        TheOddsScheduledMlbFinalizationResult? mlbFinalization)
    {
        var pieces = new List<string>();
        if (targetFailures.Count > 0)
        {
            pieces.Add($"Target failures: {string.Join("; ", targetFailures.Select(result => $"{result.Target.Key}: {result.Error ?? "Unknown failure"}"))}");
        }

        if (mlbFinalization is { Succeeded: false })
        {
            pieces.Add($"MLB finalization: {mlbFinalization.Error ?? "Unknown failure"}");
        }

        return pieces.Count == 0 ? "Scheduled refresh failed." : string.Join(" | ", pieces);
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

    private void LogTargetCompleted(
        TheOddsRefreshTargetPlan target,
        Guid scheduledCycleId,
        bool succeeded,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        TheOddsRefreshRun? run)
    {
        logger.LogInformation(
            "The Odds API scheduled refresh target completed. ScheduledCycleId: {ScheduledCycleId}. TargetKey: {TargetKey}. Sport: {Sport}. Competition: {Competition}. ProviderCategory: {ProviderCategory}. BookmakerPreset: {BookmakerPreset}. Succeeded: {Succeeded}. RefreshRunId: {RefreshRunId}. ActualApiCalls: {ActualApiCalls}. RowsUpserted: {RowsUpserted}. DurationSeconds: {DurationSeconds}.",
            scheduledCycleId,
            target.Key,
            target.Sport,
            target.Competition,
            target.ProviderCategory,
            target.BookmakerPreset,
            succeeded,
            run?.Id,
            run?.ActualApiCalls,
            run?.PlatformPropsUpserted,
            Math.Round((completedAtUtc - startedAtUtc).TotalSeconds, 3));
    }

    private async Task PersistScheduledRefreshExceptionAsync(
        Exception exception,
        string operation,
        Guid? runId,
        string? sport,
        string? provider,
        object metadata)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var errorLogger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await errorLogger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                nameof(TheOddsMlbScheduledRefreshWorker),
                "ScheduledWorker",
                operation,
                exception,
                CorrelationId: Guid.NewGuid().ToString("N"),
                Metadata: metadata,
                RunId: runId,
                Sport: sport,
                Provider: provider), CancellationToken.None);
        }
        catch (Exception loggingException)
        {
            logger.LogError(loggingException, "Failed to persist The Odds API scheduled refresh worker exception.");
        }
    }

    private sealed record ScheduledTargetExecutionResult(
        TheOddsRefreshTargetPlan Target,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        bool Succeeded,
        string? Error,
        Guid? RefreshRunId,
        int? ActualApiCalls,
        int? RowsCreated,
        int? RowsUpdated,
        int? RowsUpserted)
    {
        public static ScheduledTargetExecutionResult Failed(
            TheOddsRefreshTargetPlan target,
            DateTimeOffset startedAtUtc,
            DateTimeOffset completedAtUtc,
            string error) =>
            new(
                target,
                startedAtUtc,
                completedAtUtc,
                Succeeded: false,
                error,
                RefreshRunId: null,
                ActualApiCalls: null,
                RowsCreated: null,
                RowsUpdated: null,
                RowsUpserted: null);
    }

    internal readonly record struct ScheduledTargetTiming(
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc);
}
