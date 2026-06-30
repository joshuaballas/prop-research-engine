using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Configuration;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface ITheOddsRefreshPlannerService
{
    Task<TheOddsRefreshPlanResult> BuildPlanAsync(CancellationToken cancellationToken);
    Task<TheOddsRefreshPlannerDecision> PersistDecisionAsync(
        TheOddsRefreshPlanResult plan,
        string source,
        CancellationToken cancellationToken);
}

public sealed class TheOddsRefreshPlannerService(
    ApplicationDbContext dbContext,
    IOptions<TheOddsRefreshPlannerOptions> options,
    IOptions<TheOddsApiOptions> theOddsApiOptions,
    IOptions<TheOddsScheduledRefreshOptions> scheduledRefreshOptions,
    IOptions<TheOddsIngestionWorkerOptions> workerOptions,
    IProviderDomainConfigService providerDomainConfigService) : ITheOddsRefreshPlannerService
{
    private const string DefaultExpandedSportsbookBookmakers =
        "draftkings,fanduel,betmgm,espnbet,betrivers,fanatics,williamhill_us,betonlineag,bovada,lowvig,mybookieag";
    private const string DefaultDfsBookmakers = "prizepicks,underdog,pick6,betr_us_dfs";

    public async Task<TheOddsRefreshPlanResult> BuildPlanAsync(CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var plannerOptions = options.Value;
        var scheduledOptions = scheduledRefreshOptions.Value;
        var latestRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API" && row.Sport == "MLB")
            .OrderByDescending(row => row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestSuccessfulRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API"
                && row.Sport == "MLB"
                && row.Succeeded
                && !row.DryRun
                && !row.Blocked
                && row.BookmakerPreset == plannerOptions.DefaultBookmakerPreset)
            .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var slateDate = ResolveMlbEasternDate(generatedAtUtc);
        var eventStartTimes = ExtractEventStartTimes(latestRun?.DiagnosticsJson);
        var estimatedSlateEvents = ExtractEstimatedSlateEvents(latestRun?.DiagnosticsJson);
        if (estimatedSlateEvents == 0)
        {
            estimatedSlateEvents = eventStartTimes.Length;
        }

        var hasPersistedEventEstimate = estimatedSlateEvents > 0;
        var allowEventDiscoveryFallback = scheduledOptions.Enabled &&
            scheduledOptions.AllowScheduledLiveRefresh &&
            scheduledOptions.AllowEventDiscoveryWhenNoPersistedEvents;
        var estimateSource = hasPersistedEventEstimate
            ? "PersistedEvents"
            : allowEventDiscoveryFallback
                ? "EventDiscoveryFallback"
                : "NoPersistedEvents";
        var estimatedApiCalls = hasPersistedEventEstimate
            ? 1 + estimatedSlateEvents
            : allowEventDiscoveryFallback
                ? scheduledOptions.GetFallbackEstimatedApiCalls()
                : 0;
        var latestRunObservedAtUtc = latestRun?.CompletedAtUtc ?? latestRun?.StartedAtUtc;
        var latestSuccessfulRunObservedAtUtc = latestSuccessfulRun?.CompletedAtUtc ?? latestSuccessfulRun?.StartedAtUtc;
        var latestRunDiagnostics = ExtractProviderDiagnostics(latestRun?.DiagnosticsJson, latestRunObservedAtUtc);
        var latestSuccessfulRunDiagnostics = ExtractProviderDiagnostics(latestSuccessfulRun?.DiagnosticsJson, latestSuccessfulRunObservedAtUtc);
        var providerDiagnostics = ResolveEffectiveProviderDiagnostics(
            latestRunDiagnostics,
            latestSuccessfulRunDiagnostics,
            generatedAtUtc,
            plannerOptions);
        var providerHealthRun = latestSuccessfulRun ?? latestRun;
        var activeSnapshotCount = providerHealthRun is null ? 0 : await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .CountAsync(row => row.TheOddsRefreshRunId == providerHealthRun.Id
                && row.LifecycleStatus == "Active"
                && !row.IsStale
                && !row.IsExpired, cancellationToken);
        var topPickSnapshotCount = providerHealthRun is null ? 0 : await dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .CountAsync(row => row.TheOddsRefreshRunId == providerHealthRun.Id, cancellationToken);
        var latestHadUsableMarketData = activeSnapshotCount > 0 || topPickSnapshotCount > 0;
        var providerStatus = ResolveProviderStatus(providerHealthRun, latestHadUsableMarketData, providerDiagnostics);
        var providerAvailable = providerStatus is not "Unavailable" &&
            !providerDiagnostics.ProviderRejectedKeyDetected;

        var nextEventStart = eventStartTimes.FirstOrDefault(value => value > generatedAtUtc);
        var minutesUntilNextEventStart = nextEventStart == default
            ? (decimal?)null
            : Math.Round((decimal)(nextEventStart - generatedAtUtc).TotalMinutes, 2);
        var lastSuccessfulAt = latestSuccessfulRun?.CompletedAtUtc ?? latestSuccessfulRun?.StartedAtUtc;
        var latestAttemptAt = latestRun?.CompletedAtUtc ?? latestRun?.StartedAtUtc;
        var latestFailedAttemptAt = IsRetryableFailedLiveAttempt(latestRun, plannerOptions.DefaultBookmakerPreset, lastSuccessfulAt)
            ? latestAttemptAt
            : null;
        var cadence = ResolveCadence(
            plannerOptions,
            scheduledOptions,
            minutesUntilNextEventStart,
            estimateSource,
            latestFailedAttemptAt);
        var cadenceAnchorAt = latestFailedAttemptAt ?? lastSuccessfulAt;
        var nextEligibleRefreshUtc = cadenceAnchorAt?.AddMinutes(cadence.RefreshEveryMinutes) ?? generatedAtUtc;
        var cadenceAllowsRefresh = !cadenceAnchorAt.HasValue || generatedAtUtc >= nextEligibleRefreshUtc;
        var recentProviderFailureBackoff = latestFailedAttemptAt.HasValue &&
            IsProviderQuotaOrRateLimitFailure(latestRun?.DiagnosticsJson) &&
            !cadenceAllowsRefresh;
        var budget = await BuildBudgetAsync(
            plannerOptions,
            scheduledOptions,
            providerDiagnostics,
            estimatedApiCalls,
            generatedAtUtc,
            cancellationToken);
        var stopAfterStartTriggered = plannerOptions.StopAfterEventStart &&
            eventStartTimes.Length > 0 &&
            eventStartTimes.All(value => value <= generatedAtUtc);
        var skipReason = BuildSkipReason(
            plannerOptions,
            providerDiagnostics.ProviderRejectedKeyDetected,
            providerAvailable,
            budget.BudgetAllowed,
            budget.BudgetBlockReason,
            cadenceAllowsRefresh,
            recentProviderFailureBackoff,
            stopAfterStartTriggered,
            estimatedSlateEvents,
            allowEventDiscoveryFallback);

        var targetPlans = await BuildTargetPlansAsync(
            scheduledOptions,
            plannerOptions,
            generatedAtUtc,
            currentPlanBudget: budget,
            providerAvailable,
            providerStatus,
            providerDiagnostics.ProviderRejectedKeyDetected,
            cancellationToken);

        return new TheOddsRefreshPlanResult(
            generatedAtUtc,
            ProviderStatus: providerStatus,
            ProviderRejectedKeyDetected: providerDiagnostics.ProviderRejectedKeyDetected,
            ProviderAvailable: providerAvailable,
            RequestsUsed: providerDiagnostics.RequestsUsed,
            RequestsRemaining: providerDiagnostics.RequestsRemaining,
            SlateDate: slateDate,
            BookmakerPreset: plannerOptions.DefaultBookmakerPreset,
            EstimatedSlateEvents: estimatedSlateEvents,
            EstimatedApiCalls: estimatedApiCalls,
            EstimateSource: estimateSource,
            CadenceRule: cadence,
            MinutesUntilNextEventStart: minutesUntilNextEventStart,
            LastSuccessfulRefreshAtUtc: lastSuccessfulAt,
            MinutesSinceLastSuccessfulRefresh: lastSuccessfulAt.HasValue
                ? Math.Round((decimal)(generatedAtUtc - lastSuccessfulAt.Value).TotalMinutes, 2)
                : null,
            LastAttemptAtUtc: latestAttemptAt,
            LastFailedAttemptAtUtc: latestFailedAttemptAt,
            MinutesSinceLastAttempt: latestAttemptAt.HasValue
                ? Math.Round((decimal)(generatedAtUtc - latestAttemptAt.Value).TotalMinutes, 2)
                : null,
            NextEligibleRefreshUtc: nextEligibleRefreshUtc,
            Budget: budget,
            HasUsableMarketData: latestHadUsableMarketData,
            ShouldRefresh: skipReason is null,
            SkipReason: skipReason,
            RecommendedAction: BuildRecommendedAction(skipReason, estimateSource, providerDiagnostics, budget))
        {
            Targets = targetPlans
        };
    }

    private async Task<IReadOnlyList<TheOddsRefreshTargetPlan>> BuildTargetPlansAsync(
        TheOddsScheduledRefreshOptions scheduledOptions,
        TheOddsRefreshPlannerOptions plannerOptions,
        DateTimeOffset generatedAtUtc,
        TheOddsRefreshBudgetSummary currentPlanBudget,
        bool providerAvailable,
        string providerStatus,
        bool providerRejectedKeyDetected,
        CancellationToken cancellationToken)
    {
        var targets = await providerDomainConfigService.GetTheOddsScheduledRefreshTargetsAsync(
            scheduledOptions,
            cancellationToken);
        var plans = new List<TheOddsRefreshTargetPlan>(targets.Count);

        foreach (var target in targets)
        {
            var targetKey = target.ResolveKey();
            var targetBookmakerKeys = await providerDomainConfigService.GetTheOddsBookmakerKeysAsync(
                target,
                workerOptions.Value,
                cancellationToken);
            var targetMarketKeys = await providerDomainConfigService.GetTheOddsMarketKeysAsync(
                target,
                theOddsApiOptions.Value,
                workerOptions.Value,
                cancellationToken);
            var targetBookmakers = new TargetBookmakerPlan(
                targetBookmakerKeys,
                targetBookmakerKeys,
                []);
            var targetMarkets = JoinCommaSeparated(targetMarketKeys);
            var targetBookmakerPresets = ResolveTargetPersistedBookmakerPresets(target);
            var candidateRuns = await dbContext.TheOddsRefreshRuns
                .AsNoTracking()
                .Where(row => row.Provider == "The Odds API"
                    && row.Sport == target.Sport
                    && targetBookmakerPresets.Contains(row.BookmakerPreset))
                .OrderByDescending(row => row.StartedAtUtc)
                .Take(50)
                .ToListAsync(cancellationToken);
            var matchingRuns = candidateRuns
                .Where(row => RefreshRunMatchesTarget(row, target))
                .ToArray();
            var latestRun = matchingRuns
                .OrderByDescending(row => row.StartedAtUtc)
                .FirstOrDefault();
            var latestSuccessfulRun = matchingRuns
                .Where(row => row.Succeeded && !row.DryRun && !row.Blocked)
                .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
                .FirstOrDefault();

            var latestSuccessAt = latestSuccessfulRun?.CompletedAtUtc ?? latestSuccessfulRun?.StartedAtUtc;
            var latestAttemptAt = latestRun?.CompletedAtUtc ?? latestRun?.StartedAtUtc;
            var estimatedApiCalls = Math.Max(1, target.EstimatedApiCalls ?? target.MaxEvents ?? scheduledOptions.GetFallbackEstimatedApiCalls());
            var nextEligibleRefreshUtc = latestSuccessAt?.Add(scheduledOptions.GetInterval()) ?? generatedAtUtc;
            var disabledReason = string.IsNullOrWhiteSpace(target.DisabledReason) ? null : target.DisabledReason;
            var providerSportKeyVerified = IsProviderSportKeyVerified(target);
            var executionWired = IsScheduledExecutionWired(target);
            var missingProviderKey = string.IsNullOrWhiteSpace(target.ProviderSportKey);
            var blockers = new List<string>();

            if (!target.Enabled)
            {
                blockers.Add(disabledReason ?? "TargetDisabled");
            }

            if (!scheduledOptions.Enabled)
            {
                blockers.Add("SchedulerDisabled");
            }

            if (missingProviderKey)
            {
                blockers.Add("MissingProviderSportKey");
            }
            else if (!providerSportKeyVerified)
            {
                blockers.Add("ProviderSportKeyNotVerified");
            }

            if (providerRejectedKeyDetected)
            {
                blockers.Add("ProviderRejectedKey");
            }

            if (!providerAvailable)
            {
                blockers.Add("ProviderUnavailable");
            }

            if (!scheduledOptions.AllowScheduledLiveRefresh)
            {
                blockers.Add("ScheduledLiveRefreshDisabled");
            }

            if (!executionWired)
            {
                blockers.Add("ScheduledExecutionNotYetWiredForTarget");
            }

            var shouldRefresh = scheduledOptions.Enabled
                && target.Enabled
                && !missingProviderKey
                && providerSportKeyVerified
                && providerAvailable
                && !providerRejectedKeyDetected
                && scheduledOptions.AllowScheduledLiveRefresh
                && executionWired
                && generatedAtUtc >= nextEligibleRefreshUtc;

            plans.Add(new TheOddsRefreshTargetPlan(
                Key: targetKey,
                Enabled: target.Enabled,
                Sport: target.Sport,
                Competition: target.Competition,
                ProviderCategory: target.ProviderCategory,
                BookmakerPreset: target.BookmakerPreset,
                ProviderSportKey: target.ProviderSportKey,
                MaxEvents: target.MaxEvents,
                Regions: target.Regions,
                Bookmakers: target.Bookmakers,
                EffectiveBookmakers: targetBookmakers.EffectiveBookmakers,
                RequestedBookmakers: targetBookmakers.RequestedBookmakers,
                SkippedBookmakers: targetBookmakers.SkippedBookmakers,
                Markets: targetMarkets,
                SupportsResults: target.SupportsResults,
                SupportsHistory: target.SupportsHistory,
                ExecutionWired: executionWired,
                ProviderSportKeyVerified: providerSportKeyVerified,
                EstimatedApiCalls: estimatedApiCalls,
                LatestRefreshRunId: latestRun?.Id,
                LatestRunStartedAtUtc: latestRun?.StartedAtUtc,
                LatestRunCompletedAtUtc: latestRun?.CompletedAtUtc,
                LatestRunSucceeded: latestRun?.Succeeded,
                LatestRunFailed: latestRun?.Failed,
                LatestRunBlocked: latestRun?.Blocked,
                LatestSuccessfulRefreshRunId: latestSuccessfulRun?.Id,
                LatestSuccessfulRefreshCompletedAtUtc: latestSuccessAt,
                LatestAttemptAtUtc: latestAttemptAt,
                ActualApiCallsFromLatestRun: latestRun?.ActualApiCalls,
                PlatformPropsCreatedFromLatestRun: latestRun?.PlatformPropsCreated,
                PlatformPropsUpdatedFromLatestRun: latestRun?.PlatformPropsUpdated,
                PlatformPropsUpsertedFromLatestRun: latestRun?.PlatformPropsUpserted,
                NextEligibleRefreshUtc: nextEligibleRefreshUtc,
                ProviderStatus: providerStatus,
                ProviderAvailable: providerAvailable,
                BudgetAllowed: currentPlanBudget.BudgetAllowed,
                ShouldRefresh: shouldRefresh,
                SkipReason: shouldRefresh ? null : string.Join(",", blockers.Distinct(StringComparer.OrdinalIgnoreCase)),
                Notes: target.Notes));
        }

        return plans;
    }

    private static bool RefreshRunMatchesTarget(
        TheOddsRefreshRun run,
        TheOddsScheduledRefreshTargetOptions target)
    {
        if (!string.Equals(run.Sport, target.Sport, StringComparison.OrdinalIgnoreCase)
            || !ResolveTargetPersistedBookmakerPresets(target)
                .Contains(run.BookmakerPreset, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!ContainsCsvValue(run.ProviderCategoriesIncluded, target.ProviderCategory))
        {
            return false;
        }

        if (string.Equals(target.Sport, "MLB", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(target.Competition, "MLB", StringComparison.OrdinalIgnoreCase);
        }

        var runCompetition = ExtractRefreshRunCompetition(run.DiagnosticsJson);
        return !string.IsNullOrWhiteSpace(runCompetition)
            && string.Equals(runCompetition, target.Competition, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ResolveTargetPersistedBookmakerPresets(TheOddsScheduledRefreshTargetOptions target)
    {
        var configured = string.IsNullOrWhiteSpace(target.BookmakerPreset)
            ? "default"
            : target.BookmakerPreset.Trim();

        var persisted = string.Equals(target.Sport, "MLB", StringComparison.OrdinalIgnoreCase)
            ? ResolveMlbPersistedBookmakerPreset(configured)
            : configured;
        var aliases = new List<string> { configured, persisted };
        if (string.Equals(target.Sport, "MLB", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(persisted, "allSportsbooks", StringComparison.OrdinalIgnoreCase))
            {
                aliases.Add("default");
            }

            if (string.Equals(persisted, "dfsAll", StringComparison.OrdinalIgnoreCase))
            {
                aliases.Add("dfsOnly");
            }
        }
        else if (string.Equals(target.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
            && string.Equals(persisted, "allSportsbooks", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("soccerSportsbookContext");
        }

        return aliases
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveMlbPersistedBookmakerPreset(string bookmakerPreset)
    {
        return bookmakerPreset.ToLowerInvariant() switch
        {
            "expanded" => "expanded",
            "dfsonly" or "dfs-only" or "dfs_only" => "dfsOnly",
            "dfsall" or "dfs-all" or "dfs_all" => "dfsAll",
            "sportsbookonly" or "sportsbook-only" or "sportsbook_only" => "sportsbookOnly",
            "sportsbookexpanded" or "sportsbook-expanded" or "sportsbook_expanded" => "allSportsbooks",
            "allsportsbooks" or "all-sportsbooks" or "all_sportsbooks" => "allSportsbooks",
            "all" => "all",
            _ => "default"
        };
    }

    private static TargetBookmakerPlan ResolveTargetBookmakers(
        TheOddsScheduledRefreshTargetOptions target,
        TheOddsIngestionWorkerOptions workerOptions)
    {
        var explicitBookmakers = NullIfWhiteSpace(target.Bookmakers);
        if (!string.IsNullOrWhiteSpace(explicitBookmakers))
        {
            return new TargetBookmakerPlan(
                NormalizeCsv(explicitBookmakers),
                NormalizeCsv(explicitBookmakers),
                []);
        }

        var expandedSportsbooks = NormalizeCsv(
            NullIfWhiteSpace(workerOptions.ExpandedBookmakers) ?? DefaultExpandedSportsbookBookmakers);
        var conservativeSportsbooks = NormalizeCsv(NullIfWhiteSpace(workerOptions.Bookmakers) ?? string.Empty);
        var dfsBookmakers = NormalizeCsv(NullIfWhiteSpace(workerOptions.DfsBookmakers) ?? DefaultDfsBookmakers);
        var preset = ResolveMlbPersistedBookmakerPreset(target.BookmakerPreset);

        if (string.Equals(target.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetBookmakerPlan(
                dfsBookmakers,
                dfsBookmakers,
                []);
        }

        if (!string.Equals(target.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetBookmakerPlan([], [], []);
        }

        var effectiveSportsbooks = preset switch
        {
            "expanded" or "allSportsbooks" or "all" => expandedSportsbooks,
            _ => conservativeSportsbooks.Length > 0 ? conservativeSportsbooks : expandedSportsbooks
        };

        return new TargetBookmakerPlan(
            effectiveSportsbooks,
            effectiveSportsbooks,
            []);
    }

    private static string[] NormalizeCsv(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? JoinCommaSeparated(IReadOnlyCollection<string> values)
        => values.Count == 0
            ? null
            : string.Join(',', values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ContainsCsvValue(string? csv, string value)
    {
        return (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(row => string.Equals(row, value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractRefreshRunCompetition(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null)
        {
            return null;
        }

        return diagnosticsJson.RootElement.TryGetProperty("competition", out var competition)
            && competition.ValueKind == JsonValueKind.String
                ? competition.GetString()
                : null;
    }

    private static bool IsScheduledExecutionWired(TheOddsScheduledRefreshTargetOptions target)
    {
        if (string.Equals(target.Sport, "MLB", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(target.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Competition, "World Cup", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProviderSportKeyVerified(TheOddsScheduledRefreshTargetOptions target)
    {
        if (string.Equals(target.Sport, "MLB", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(target.ProviderSportKey, "baseball_mlb", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(target.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Competition, "World Cup", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.ProviderSportKey, "soccer_fifa_world_cup", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TheOddsRefreshBudgetSummary> BuildBudgetAsync(
        TheOddsRefreshPlannerOptions plannerOptions,
        TheOddsScheduledRefreshOptions scheduledOptions,
        ProviderDiagnostics diagnostics,
        int estimatedApiCalls,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var today = generatedAtUtc.UtcDateTime.Date;
        var monthStart = new DateTime(generatedAtUtc.Year, generatedAtUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var dailyUsedCredits = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API"
                && !row.DryRun
                && !row.Blocked
                && row.StartedAtUtc >= today)
            .SumAsync(row => row.ActualApiCalls, cancellationToken);
        var persistedMonthlyUsedCredits = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API"
                && !row.DryRun
                && !row.Blocked
                && row.StartedAtUtc >= monthStart)
            .SumAsync(row => row.ActualApiCalls, cancellationToken);
        var providerReportedRequestsUsedForBudget = diagnostics.IsProviderCreditTelemetryStale
            ? null
            : diagnostics.RequestsUsed;
        var monthlyUsedCredits = Math.Max(persistedMonthlyUsedCredits, providerReportedRequestsUsedForBudget ?? 0);
        var dailyAfterEstimate = dailyUsedCredits + estimatedApiCalls;
        var monthlyAfterEstimate = monthlyUsedCredits + estimatedApiCalls;
        var localDailyBudgetBlockingEnabled = plannerOptions.EnforcementEnabled &&
            plannerOptions.BlockOnLocalDailyBudget &&
            plannerOptions.MaxDailyCredits > 0;
        var localMonthlyBudgetBlockingEnabled = plannerOptions.EnforcementEnabled &&
            plannerOptions.BlockOnLocalMonthlyBudget &&
            plannerOptions.MaxMonthlyCredits > 0;
        var perRunEstimateBlockingEnabled = plannerOptions.EnforcementEnabled &&
            plannerOptions.BlockOnPerRunEstimate;
        var providerCreditBlockingEnabled = plannerOptions.EnforcementEnabled &&
            plannerOptions.BlockOnProviderCredits;
        var localBudgetEnforcementEnabled = localDailyBudgetBlockingEnabled || localMonthlyBudgetBlockingEnabled;
        var dailyPercentAfterEstimate = plannerOptions.MaxDailyCredits > 0
            ? Percent(dailyAfterEstimate, plannerOptions.MaxDailyCredits)
            : 0m;
        var monthlyPercentAfterEstimate = plannerOptions.MaxMonthlyCredits > 0
            ? Percent(monthlyAfterEstimate, plannerOptions.MaxMonthlyCredits)
            : 0m;
        var dailyHardStop = plannerOptions.MaxDailyCredits > 0 &&
            dailyPercentAfterEstimate >= plannerOptions.HardStopAtDailyPercent;
        var monthlyHardStop = plannerOptions.MaxMonthlyCredits > 0 &&
            monthlyPercentAfterEstimate >= plannerOptions.HardStopAtMonthlyPercent;
        var providerRemainingHardStop = !diagnostics.IsProviderCreditTelemetryStale &&
            diagnostics.RequestsRemaining.HasValue &&
            diagnostics.RequestsRemaining.Value < estimatedApiCalls;
        var providerTelemetryUsedForBlocking = providerCreditBlockingEnabled &&
            !diagnostics.IsProviderCreditTelemetryStale &&
            diagnostics.RequestsRemaining.HasValue;
        var maxEstimatedCallsPerRun = scheduledOptions.GetMaxEstimatedCallsPerRun();
        var perRunEstimateHardStop = maxEstimatedCallsPerRun.HasValue &&
            estimatedApiCalls > maxEstimatedCallsPerRun.Value;
        var isBlockedByLocalBudget = (dailyHardStop && localDailyBudgetBlockingEnabled) ||
            (monthlyHardStop && localMonthlyBudgetBlockingEnabled);
        var isBlockedByProviderCredits = providerRemainingHardStop && providerCreditBlockingEnabled;
        var isBlockedByPerRunEstimate = perRunEstimateHardStop && perRunEstimateBlockingEnabled;
        var plannerWouldBlockIfEnforced = dailyHardStop ||
            monthlyHardStop ||
            providerRemainingHardStop ||
            perRunEstimateHardStop;
        var budgetBlockReason = isBlockedByPerRunEstimate
            ? "PerRunEstimateExceeded"
            : isBlockedByProviderCredits
                ? "ProviderCreditsExceeded"
                : isBlockedByLocalBudget
                    ? "LocalBudgetExceeded"
                    : null;
        var plannerWarnings = BuildPlannerWarnings(
            plannerOptions,
            diagnostics,
            estimatedApiCalls,
            maxEstimatedCallsPerRun,
            dailyHardStop,
            monthlyHardStop,
            providerRemainingHardStop,
            perRunEstimateHardStop);

        return new TheOddsRefreshBudgetSummary(
            plannerOptions.EnforcementEnabled,
            plannerOptions.MaxDailyCredits,
            localDailyBudgetBlockingEnabled,
            plannerOptions.MaxMonthlyCredits,
            localBudgetEnforcementEnabled,
            localDailyBudgetBlockingEnabled,
            localMonthlyBudgetBlockingEnabled,
            perRunEstimateBlockingEnabled,
            providerCreditBlockingEnabled,
            plannerOptions.ProviderRemainingHardStopCredits,
            maxEstimatedCallsPerRun,
            dailyUsedCredits,
            monthlyUsedCredits,
            persistedMonthlyUsedCredits,
            diagnostics.RequestsUsed,
            diagnostics.RequestsRemaining,
            diagnostics.CreditTelemetryObservedAtUtc,
            diagnostics.ProviderCreditTelemetryAgeMinutes,
            diagnostics.IsProviderCreditTelemetryStale,
            providerTelemetryUsedForBlocking,
            estimatedApiCalls,
            dailyAfterEstimate,
            monthlyAfterEstimate,
            dailyPercentAfterEstimate,
            monthlyPercentAfterEstimate,
            plannerOptions.MaxDailyCredits > 0 && dailyPercentAfterEstimate >= plannerOptions.WarnAtDailyPercent,
            plannerOptions.MaxMonthlyCredits > 0 && monthlyPercentAfterEstimate >= plannerOptions.WarnAtMonthlyPercent,
            dailyHardStop,
            monthlyHardStop,
            providerRemainingHardStop,
            perRunEstimateHardStop,
            isBlockedByLocalBudget,
            isBlockedByProviderCredits,
            isBlockedByPerRunEstimate,
            plannerWouldBlockIfEnforced,
            plannerWarnings,
            budgetBlockReason,
            BuildBudgetExplanation(
                budgetBlockReason,
                plannerOptions,
                maxEstimatedCallsPerRun,
                diagnostics,
                estimatedApiCalls,
                plannerWouldBlockIfEnforced),
            budgetBlockReason is null);
    }

    public async Task<TheOddsRefreshPlannerDecision> PersistDecisionAsync(
        TheOddsRefreshPlanResult plan,
        string source,
        CancellationToken cancellationToken)
    {
        var plannerOptions = options.Value;
        var scheduledOptions = scheduledRefreshOptions.Value;
        var decision = new TheOddsRefreshPlannerDecision
        {
            Id = Guid.NewGuid(),
            GeneratedAtUtc = plan.GeneratedAtUtc,
            Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source,
            Sport = "MLB",
            SlateDate = plan.SlateDate,
            BookmakerPreset = plan.BookmakerPreset,
            ShouldRefresh = plan.ShouldRefresh,
            Skipped = !plan.ShouldRefresh,
            SkipReason = plan.SkipReason,
            ProviderStatus = plan.ProviderStatus,
            ProviderAvailable = plan.ProviderAvailable,
            ProviderRejectedKeyDetected = plan.ProviderRejectedKeyDetected,
            RequestsUsed = plan.RequestsUsed,
            RequestsRemaining = plan.RequestsRemaining,
            EstimatedApiCalls = plan.EstimatedApiCalls,
            DailyCreditsUsed = plan.Budget.DailyUsedCredits,
            MonthlyCreditsUsed = plan.Budget.MonthlyUsedCredits,
            DailyCreditLimit = plannerOptions.MaxDailyCredits,
            MonthlyCreditLimit = plannerOptions.MaxMonthlyCredits,
            BudgetAllowed = plan.Budget.BudgetAllowed,
            CadenceRule = plan.CadenceRule.Name,
            MinutesUntilNextEventStart = plan.MinutesUntilNextEventStart,
            MinutesSinceLastSuccessfulRefresh = plan.MinutesSinceLastSuccessfulRefresh,
            NextEligibleRefreshUtc = plan.NextEligibleRefreshUtc,
            RecommendedAction = plan.RecommendedAction,
            DetailsJson = JsonSerializer.SerializeToDocument(new
            {
                plan.ProviderStatus,
                plan.ProviderAvailable,
                plan.ProviderRejectedKeyDetected,
                plan.RequestsUsed,
                plan.RequestsRemaining,
                plan.EstimatedSlateEvents,
                plan.EstimatedApiCalls,
                plan.EstimateSource,
                plan.CadenceRule,
                plan.Budget,
                plan.LastAttemptAtUtc,
                plan.LastFailedAttemptAtUtc,
                plan.MinutesSinceLastAttempt,
                plan.HasUsableMarketData,
                plan.ShouldRefresh,
                plan.SkipReason,
                plan.RecommendedAction,
                plannerOptions = new
                {
                    plannerOptions.Enabled,
                    plannerOptions.DefaultBookmakerPreset,
                    plannerOptions.PlannerIntervalSeconds,
                    plannerOptions.MaxDailyCredits,
                    plannerOptions.MaxMonthlyCredits,
                    plannerOptions.WarnAtDailyPercent,
                    plannerOptions.WarnAtMonthlyPercent,
                    plannerOptions.HardStopAtDailyPercent,
                    plannerOptions.HardStopAtMonthlyPercent,
                    plannerOptions.EnforcementEnabled,
                    plannerOptions.BlockOnProviderCredits,
                    plannerOptions.BlockOnPerRunEstimate,
                    plannerOptions.BlockOnLocalMonthlyBudget,
                    plannerOptions.BlockOnLocalDailyBudget,
                    plannerOptions.ProviderRemainingHardStopCredits,
                    plannerOptions.StopAfterEventStart,
                    plannerOptions.RetryAfterFailureMinutes,
                    scheduledOptions.AllowEventDiscoveryWhenNoPersistedEvents,
                    scheduledOptions.FallbackEstimatedApiCalls
                }
            })
        };

        dbContext.TheOddsRefreshPlannerDecisions.Add(decision);
        await dbContext.SaveChangesAsync(cancellationToken);
        return decision;
    }

    private static string ResolveProviderStatus(
        SportsBetting.Api.Models.TheOddsRefreshRun? latestRun,
        bool hasUsableMarketData,
        ProviderDiagnostics diagnostics)
    {
        if (latestRun is null)
        {
            return "Unknown";
        }

        if (diagnostics.ProviderRejectedKeyDetected)
        {
            return "Unavailable";
        }

        if (latestRun.Succeeded && hasUsableMarketData)
        {
            return diagnostics.RequestsRemaining is <= 5 ? "Degraded" : "Healthy";
        }

        return "Degraded";
    }

    private static string? BuildSkipReason(
        TheOddsRefreshPlannerOptions plannerOptions,
        bool providerRejectedKeyDetected,
        bool providerAvailable,
        bool budgetAllowed,
        string? budgetBlockReason,
        bool cadenceAllowsRefresh,
        bool recentProviderFailureBackoff,
        bool stopAfterStartTriggered,
        int estimatedSlateEvents,
        bool allowEventDiscoveryFallback)
    {
        if (!plannerOptions.Enabled) return "PlannerDisabled";
        if (providerRejectedKeyDetected) return "ProviderRejectedKey";
        if (!providerAvailable) return "ProviderUnavailable";
        if (!budgetAllowed) return budgetBlockReason ?? "BudgetBlocked";
        if (recentProviderFailureBackoff) return "RecentProviderFailureBackoff";
        if (stopAfterStartTriggered) return "StopAfterEventStart";
        if (estimatedSlateEvents <= 0 && !allowEventDiscoveryFallback) return "NoPersistedSlateEvents";
        if (!cadenceAllowsRefresh) return "CadenceNotDue";
        return null;
    }

    private static string BuildRecommendedAction(
        string? skipReason,
        string estimateSource,
        ProviderDiagnostics diagnostics,
        TheOddsRefreshBudgetSummary budget)
    {
        return skipReason switch
        {
            null when estimateSource == "EventDiscoveryFallback" => "Refresh is eligible. No persisted events are available, so scheduled refresh will discover events from The Odds before fetching lines.",
            null => "Refresh is eligible by persisted planner state. Scheduled live execution still requires explicit scheduled live config.",
            "ProviderRejectedKey" => "Check TheOddsApi:ApiKey before live refreshes.",
            "ProviderUnavailable" => "Provider is unavailable or degraded beyond planner limits; skip scheduled refresh.",
            "ProviderCreditsExceeded" => budget.BudgetExplanation,
            "PerRunEstimateExceeded" => budget.BudgetExplanation,
            "BudgetBlocked" => budget.BudgetExplanation,
            "RecentProviderFailureBackoff" => "Recent provider quota or rate-limit failure detected; wait for the short retry/backoff window before the next live attempt.",
            "CadenceNotDue" => "Skip refresh until the configured cadence window is due.",
            "StopAfterEventStart" => "Skip refresh because TheOddsRefreshPlanner:StopAfterEventStart is enabled for this target.",
            "NoPersistedSlateEvents" => "No persisted slate events are available for estimation.",
            "PlannerDisabled" => "Planner is disabled in config.",
            _ => "Skip refresh based on current planner state."
        };
    }

    private static bool IsRetryableFailedLiveAttempt(
        SportsBetting.Api.Models.TheOddsRefreshRun? latestRun,
        string bookmakerPreset,
        DateTimeOffset? lastSuccessfulAt)
    {
        if (latestRun is null ||
            !latestRun.Failed ||
            latestRun.DryRun ||
            latestRun.Blocked ||
            !latestRun.ForceLive ||
            !latestRun.BookmakerPreset.Equals(bookmakerPreset, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var latestAttemptAt = latestRun.CompletedAtUtc ?? latestRun.StartedAtUtc;
        return !lastSuccessfulAt.HasValue || latestAttemptAt > lastSuccessfulAt.Value;
    }

    private static PlannerCadenceRule ResolveCadence(
        TheOddsRefreshPlannerOptions options,
        TheOddsScheduledRefreshOptions scheduledOptions,
        decimal? minutesUntilNextEventStart,
        string estimateSource,
        DateTimeOffset? latestFailedAttemptAt)
    {
        if (latestFailedAttemptAt.HasValue)
        {
            return new PlannerCadenceRule(
                "RetryAfterFailure",
                null,
                null,
                options.GetRetryAfterFailureMinutes(),
                "Latest live refresh attempt failed after the last success; retry on the short recovery cadence.");
        }

        if (!minutesUntilNextEventStart.HasValue)
        {
            if (estimateSource == "EventDiscoveryFallback")
            {
                return new PlannerCadenceRule(
                    "EventDiscoveryFallback",
                    null,
                    null,
                    (int)scheduledOptions.GetInterval().TotalMinutes,
                    "No persisted upcoming event start was found; use scheduled cadence and discover events live.");
            }

            return new PlannerCadenceRule("NoKnownUpcomingEvent", null, null, options.FarOutRefreshMinutes, "No persisted upcoming event start was found.");
        }

        var minutes = minutesUntilNextEventStart.Value;
        if (minutes <= options.NearLockMaxMinutes && minutes >= 0)
        {
            return new PlannerCadenceRule("NearLock", 0, options.NearLockMaxMinutes, options.NearLockRefreshMinutes, "0-30 minutes before first pitch.");
        }

        if (minutes <= options.PreLockMaxMinutes)
        {
            return new PlannerCadenceRule("PreLock", options.NearLockMaxMinutes, options.PreLockMaxMinutes, options.PreLockRefreshMinutes, "30-120 minutes before first pitch.");
        }

        if (minutes <= options.SameDayMaxMinutes)
        {
            return new PlannerCadenceRule("SameDay", options.PreLockMaxMinutes, options.SameDayMaxMinutes, options.SameDayRefreshMinutes, "120-720 minutes before first pitch.");
        }

        return new PlannerCadenceRule("FarOut", options.SameDayMaxMinutes, options.FarOutMaxMinutes, options.FarOutRefreshMinutes, "720-1440+ minutes before first pitch.");
    }

    private static string BuildBudgetExplanation(
        string? budgetBlockReason,
        TheOddsRefreshPlannerOptions plannerOptions,
        int? maxEstimatedCallsPerRun,
        ProviderDiagnostics diagnostics,
        int estimatedApiCalls,
        bool plannerWouldBlockIfEnforced)
    {
        return budgetBlockReason switch
        {
            null when diagnostics.IsProviderCreditTelemetryStale =>
                $"Budget allowed. Provider credit telemetry is stale ({diagnostics.ProviderCreditTelemetryAgeMinutes} minutes old), so provider remaining credits were treated as unknown. Local daily/monthly budget enforcement is disabled.",
            null when !plannerOptions.EnforcementEnabled && plannerWouldBlockIfEnforced =>
                "Budget allowed because TheOddsRefreshPlanner:EnforcementEnabled=false. Planner budget and estimate checks are diagnostics only.",
            null when !plannerOptions.EnforcementEnabled =>
                "Budget allowed. Planner enforcement is disabled; local budget, per-run estimate, and provider credit estimates are diagnostics only.",
            null => "Budget allowed. No enabled planner budget gate is blocking this refresh.",
            "PerRunEstimateExceeded" =>
                $"Blocked because estimated calls for this run ({estimatedApiCalls}) exceed TheOddsScheduledRefresh:MaxEstimatedCallsPerRun ({maxEstimatedCallsPerRun}).",
            "ProviderCreditsExceeded" =>
                $"Blocked because fresh provider-reported remaining credits ({diagnostics.RequestsRemaining}) are below the estimated calls ({estimatedApiCalls}).",
            "LocalBudgetExceeded" =>
                "Blocked because an explicitly enabled local daily/monthly budget gate exceeded its configured hard stop.",
            _ => "Blocked by refresh budget guardrail."
        };
    }

    private static string[] BuildPlannerWarnings(
        TheOddsRefreshPlannerOptions plannerOptions,
        ProviderDiagnostics diagnostics,
        int estimatedApiCalls,
        int? maxEstimatedCallsPerRun,
        bool dailyHardStop,
        bool monthlyHardStop,
        bool providerRemainingHardStop,
        bool perRunEstimateHardStop)
    {
        var warnings = new List<string>();
        if (!plannerOptions.EnforcementEnabled)
        {
            warnings.Add("Planner enforcement is disabled; local budget, provider credit, and per-run estimate checks are diagnostics only.");
        }

        if (dailyHardStop)
        {
            warnings.Add("Daily local budget hard-stop threshold would be exceeded if local daily budget blocking were enabled.");
        }

        if (monthlyHardStop)
        {
            warnings.Add("Monthly local budget hard-stop threshold would be exceeded if local monthly budget blocking were enabled.");
        }

        if (providerRemainingHardStop)
        {
            warnings.Add($"Fresh provider-reported remaining credits ({diagnostics.RequestsRemaining}) are below estimated calls ({estimatedApiCalls}).");
        }

        if (perRunEstimateHardStop)
        {
            warnings.Add($"Estimated calls ({estimatedApiCalls}) exceed configured per-run estimate cap ({maxEstimatedCallsPerRun}).");
        }

        return warnings.ToArray();
    }

    private static bool IsProviderQuotaOrRateLimitFailure(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null)
        {
            return false;
        }

        var root = diagnosticsJson.RootElement;
        return ContainsText(root, "quota") ||
            ContainsText(root, "rate limit") ||
            ContainsText(root, "rate-limit") ||
            ContainsText(root, "credits") ||
            ContainsText(root, "payment may be exhausted");
    }

    private static ProviderDiagnostics ExtractProviderDiagnostics(
        JsonDocument? diagnosticsJson,
        DateTimeOffset? observedAtUtc)
    {
        if (diagnosticsJson is null)
        {
            return new ProviderDiagnostics(null, null, null, ProviderRejectedKeyDetected: false, observedAtUtc, null, false);
        }

        var root = diagnosticsJson.RootElement;
        return new ProviderDiagnostics(
            FindFirstIntProperty(root, "requestsUsed", "RequestsUsed", "requests_used"),
            FindFirstIntProperty(root, "requestsRemaining", "RequestsRemaining", "requests_remaining"),
            FindFirstIntProperty(root, "requestsLast", "RequestsLast", "requests_last"),
            ContainsText(root, "rejected the configured API key") || ContainsText(root, "INVALID_KEY"),
            observedAtUtc,
            null,
            false);
    }

    private static ProviderDiagnostics ResolveEffectiveProviderDiagnostics(
        ProviderDiagnostics latestRunDiagnostics,
        ProviderDiagnostics latestSuccessfulRunDiagnostics,
        DateTimeOffset generatedAtUtc,
        TheOddsRefreshPlannerOptions plannerOptions)
    {
        var latestRunWithAge = latestRunDiagnostics.WithTelemetryAge(generatedAtUtc, plannerOptions.GetProviderTelemetryStaleAfter());
        var latestSuccessfulRunWithAge = latestSuccessfulRunDiagnostics.WithTelemetryAge(generatedAtUtc, plannerOptions.GetProviderTelemetryStaleAfter());

        if (latestRunWithAge.ProviderRejectedKeyDetected ||
            (latestRunWithAge.RequestsRemaining is <= 0 && !latestRunWithAge.IsProviderCreditTelemetryStale))
        {
            return latestRunWithAge;
        }

        return latestSuccessfulRunWithAge.HasAnyValue ? latestSuccessfulRunWithAge : latestRunWithAge;
    }

    private static int ExtractEstimatedSlateEvents(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null) return 0;
        var root = diagnosticsJson.RootElement;
        if (TryGetPropertyIgnoreCase(root, "events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            return events.GetArrayLength();
        }

        return FindFirstIntProperty(root, "eventsFetched", "oddsEventsFetched", "estimatedSlateEvents") ?? 0;
    }

    private static DateTimeOffset[] ExtractEventStartTimes(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null ||
            !TryGetPropertyIgnoreCase(diagnosticsJson.RootElement, "events", out var events) ||
            events.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return events.EnumerateArray()
            .Select(item => TryGetDateTimeOffsetProperty(item, "eventStartTime") ?? TryGetDateTimeOffsetProperty(item, "EventStartTime"))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .ToArray();
    }

    private static DateOnly ResolveMlbEasternDate(DateTimeOffset utcNow)
    {
        try
        {
            var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, eastern).DateTime);
        }
        catch
        {
            return DateOnly.FromDateTime(utcNow.ToOffset(TimeSpan.FromHours(-4)).DateTime);
        }
    }

    private static DateTimeOffset? TryGetDateTimeOffsetProperty(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static int? FindFirstIntProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) &&
                    property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var value))
                {
                    return value;
                }

                var nested = FindFirstIntProperty(property.Value, names);
                if (nested.HasValue) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstIntProperty(item, names);
                if (nested.HasValue) return nested;
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool ContainsText(JsonElement element, string text)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Object => element.EnumerateObject().Any(property => ContainsText(property.Value, text)),
            JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsText(item, text)),
            _ => false
        };
    }

    private static decimal Percent(int value, int denominator)
    {
        return denominator <= 0 ? 100m : Math.Round(value / (decimal)denominator * 100m, 2);
    }

    private sealed record ProviderDiagnostics(
        int? RequestsUsed,
        int? RequestsRemaining,
        int? RequestsLast,
        bool ProviderRejectedKeyDetected,
        DateTimeOffset? CreditTelemetryObservedAtUtc,
        decimal? ProviderCreditTelemetryAgeMinutes,
        bool IsProviderCreditTelemetryStale)
    {
        public bool HasAnyValue =>
            RequestsUsed.HasValue ||
            RequestsRemaining.HasValue ||
            RequestsLast.HasValue ||
            ProviderRejectedKeyDetected;

        public ProviderDiagnostics WithTelemetryAge(DateTimeOffset generatedAtUtc, TimeSpan staleAfter)
        {
            var hasTelemetry = RequestsUsed.HasValue || RequestsRemaining.HasValue || RequestsLast.HasValue;
            if (!hasTelemetry || !CreditTelemetryObservedAtUtc.HasValue)
            {
                return this with
                {
                    ProviderCreditTelemetryAgeMinutes = null,
                    IsProviderCreditTelemetryStale = false
                };
            }

            var ageMinutes = Math.Round((decimal)(generatedAtUtc - CreditTelemetryObservedAtUtc.Value).TotalMinutes, 2);
            return this with
            {
                ProviderCreditTelemetryAgeMinutes = ageMinutes,
                IsProviderCreditTelemetryStale = ageMinutes > (decimal)staleAfter.TotalMinutes
            };
        }
    }
}

public sealed class TheOddsRefreshPlannerOptions
{
    public const string SectionName = "TheOddsRefreshPlanner";

    public bool Enabled { get; set; } = true;
    public bool EnforcementEnabled { get; set; } = false;
    public string DefaultBookmakerPreset { get; set; } = "dfsOnly";
    public int PlannerIntervalSeconds { get; set; } = 60;
    public int MaxDailyCredits { get; set; } = 0;
    public bool EnforceDailyCreditBudget { get; set; } = false;
    public bool BlockOnLocalDailyBudget { get; set; } = false;
    public bool BlockOnLocalMonthlyBudget { get; set; } = false;
    public bool BlockOnProviderCredits { get; set; } = false;
    public bool BlockOnPerRunEstimate { get; set; } = false;
    public int MaxMonthlyCredits { get; set; } = 500;
    public decimal WarnAtDailyPercent { get; set; } = 80m;
    public decimal WarnAtMonthlyPercent { get; set; } = 80m;
    public decimal HardStopAtDailyPercent { get; set; } = 100m;
    public decimal HardStopAtMonthlyPercent { get; set; } = 100m;
    public int ProviderRemainingHardStopCredits { get; set; } = 5;
    public int ProviderTelemetryStaleAfterMinutes { get; set; } = 360;
    public int FarOutRefreshMinutes { get; set; } = 60;
    public int SameDayMaxMinutes { get; set; } = 720;
    public int SameDayRefreshMinutes { get; set; } = 30;
    public int PreLockMaxMinutes { get; set; } = 120;
    public int PreLockRefreshMinutes { get; set; } = 15;
    public int NearLockMaxMinutes { get; set; } = 30;
    public int NearLockRefreshMinutes { get; set; } = 5;
    public int FarOutMaxMinutes { get; set; } = 1440;
    public bool StopAfterEventStart { get; set; } = false;
    public int RetryAfterFailureMinutes { get; set; } = 1;

    public TimeSpan GetPlannerInterval() => TimeSpan.FromSeconds(Math.Max(10, PlannerIntervalSeconds));
    public int GetRetryAfterFailureMinutes() => Math.Clamp(RetryAfterFailureMinutes, 1, 15);
    public TimeSpan GetProviderTelemetryStaleAfter() => TimeSpan.FromMinutes(Math.Clamp(ProviderTelemetryStaleAfterMinutes, 1, 10080));
}

public sealed record TheOddsRefreshPlanResult(
    DateTimeOffset GeneratedAtUtc,
    string ProviderStatus,
    bool ProviderRejectedKeyDetected,
    bool ProviderAvailable,
    int? RequestsUsed,
    int? RequestsRemaining,
    DateOnly SlateDate,
    string BookmakerPreset,
    int EstimatedSlateEvents,
    int EstimatedApiCalls,
    string EstimateSource,
    PlannerCadenceRule CadenceRule,
    decimal? MinutesUntilNextEventStart,
    DateTimeOffset? LastSuccessfulRefreshAtUtc,
    decimal? MinutesSinceLastSuccessfulRefresh,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastFailedAttemptAtUtc,
    decimal? MinutesSinceLastAttempt,
    DateTimeOffset NextEligibleRefreshUtc,
    TheOddsRefreshBudgetSummary Budget,
    bool HasUsableMarketData,
    bool ShouldRefresh,
    string? SkipReason,
    string RecommendedAction)
{
    public IReadOnlyList<TheOddsRefreshTargetPlan> Targets { get; init; } = [];
}

public sealed record TheOddsRefreshTargetPlan(
    string Key,
    bool Enabled,
    string Sport,
    string Competition,
    string ProviderCategory,
    string BookmakerPreset,
    string? ProviderSportKey,
    int? MaxEvents,
    string? Regions,
    string? Bookmakers,
    IReadOnlyList<string> EffectiveBookmakers,
    IReadOnlyList<string> RequestedBookmakers,
    IReadOnlyList<string> SkippedBookmakers,
    string? Markets,
    bool SupportsResults,
    bool SupportsHistory,
    bool ExecutionWired,
    bool ProviderSportKeyVerified,
    int EstimatedApiCalls,
    Guid? LatestRefreshRunId,
    DateTimeOffset? LatestRunStartedAtUtc,
    DateTimeOffset? LatestRunCompletedAtUtc,
    bool? LatestRunSucceeded,
    bool? LatestRunFailed,
    bool? LatestRunBlocked,
    Guid? LatestSuccessfulRefreshRunId,
    DateTimeOffset? LatestSuccessfulRefreshCompletedAtUtc,
    DateTimeOffset? LatestAttemptAtUtc,
    int? ActualApiCallsFromLatestRun,
    int? PlatformPropsCreatedFromLatestRun,
    int? PlatformPropsUpdatedFromLatestRun,
    int? PlatformPropsUpsertedFromLatestRun,
    DateTimeOffset NextEligibleRefreshUtc,
    string ProviderStatus,
    bool ProviderAvailable,
    bool BudgetAllowed,
    bool ShouldRefresh,
    string? SkipReason,
    string? Notes);

public sealed record TargetBookmakerPlan(
    IReadOnlyList<string> EffectiveBookmakers,
    IReadOnlyList<string> RequestedBookmakers,
    IReadOnlyList<string> SkippedBookmakers);

public sealed record PlannerCadenceRule(
    string Name,
    int? MinMinutesUntilStart,
    int? MaxMinutesUntilStart,
    int RefreshEveryMinutes,
    string Description);

public sealed record TheOddsRefreshBudgetSummary(
    bool PlannerEnforcementEnabled,
    int ConfiguredDailyCreditBudget,
    bool DailyBudgetEnabled,
    int ConfiguredMonthlyCreditBudget,
    bool LocalBudgetEnforcementEnabled,
    bool LocalDailyBudgetEnforcementEnabled,
    bool LocalMonthlyBudgetEnforcementEnabled,
    bool PerRunEstimateBlockingEnabled,
    bool ProviderCreditBlockingEnabled,
    int ConfiguredReservedCreditBuffer,
    int? ConfiguredMaxEstimatedCallsPerRun,
    int DailyUsedCredits,
    int MonthlyUsedCredits,
    int PersistedMonthlyUsedCredits,
    int? ProviderReportedRequestsUsed,
    int? ProviderReportedRequestsRemaining,
    DateTimeOffset? ProviderCreditTelemetryObservedAtUtc,
    decimal? ProviderCreditTelemetryAgeMinutes,
    bool IsProviderTelemetryStale,
    bool ProviderTelemetryUsedForBlocking,
    int EstimatedApiCalls,
    int DailyAfterEstimate,
    int MonthlyAfterEstimate,
    decimal DailyPercentAfterEstimate,
    decimal MonthlyPercentAfterEstimate,
    bool DailyWarning,
    bool MonthlyWarning,
    bool DailyHardStop,
    bool MonthlyHardStop,
    bool ProviderRemainingHardStop,
    bool PerRunEstimateHardStop,
    bool IsBlockedByLocalBudget,
    bool IsBlockedByProviderCredits,
    bool IsBlockedByPerRunEstimate,
    bool PlannerWouldBlockIfEnforced,
    string[] PlannerWarnings,
    string? BudgetBlockReason,
    string BudgetExplanation,
    bool BudgetAllowed);
