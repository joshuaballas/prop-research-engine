using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Endpoints;

public static partial class TheOddsApiDebugEndpoints
{
    internal sealed record MlbRefreshFreshnessResponse(
        DateTimeOffset GeneratedAtUtc,
        bool CallsExternalApis,
        bool ReadOnly,
        object? LatestRun,
        object? LatestSuccessfulRun,
        object? LatestFailedRun,
        double? MinutesSinceLatestSuccessfulRefresh,
        bool IsFresh,
        bool IsStale,
        string? StaleReason,
        int FreshnessThresholdMinutes,
        object? LatestCounts);

    internal sealed record MlbLineShoppingReadResponse(
        DateTimeOffset GeneratedAtUtc,
        object Diagnostics,
        object Run,
        object Summary,
        object StrategyClassificationSummary,
        IReadOnlyList<object> ComparisonGroups);

    internal sealed record MlbTopPicksReadResponse(
        DateTimeOffset GeneratedAtUtc,
        object Diagnostics,
        object Run,
        object Summary,
        object StrategyClassificationSummary,
        IReadOnlyList<object> TopPicks);

    internal static async Task<MlbRefreshFreshnessResponse> BuildMlbRefreshFreshnessResponseAsync(
        DateOnly? slateDate,
        string? bookmakerPreset,
        int? freshnessMinutes,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var thresholdMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
        var query = dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API" && row.Sport == "MLB");

        if (slateDate.HasValue)
        {
            query = query.Where(row => row.SlateDate == slateDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(bookmakerPreset))
        {
            query = query.Where(row => row.BookmakerPreset == bookmakerPreset);
        }

        var latestRun = await query
            .OrderByDescending(row => row.StartedAtUtc)
            .ThenByDescending(row => row.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestSuccessfulRun = await query
            .Where(row => row.Succeeded)
            .OrderByDescending(row => row.CompletedAtUtc)
            .ThenByDescending(row => row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestFailedRun = await query
            .Where(row => row.Failed)
            .OrderByDescending(row => row.CompletedAtUtc)
            .ThenByDescending(row => row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var latestSuccessfulAt = latestSuccessfulRun?.CompletedAtUtc ?? latestSuccessfulRun?.StartedAtUtc;
        var minutesSinceLatestSuccessfulRefresh = latestSuccessfulAt.HasValue
            ? Math.Round((generatedAtUtc - latestSuccessfulAt.Value).TotalMinutes, 2)
            : (double?)null;
        var isFresh = minutesSinceLatestSuccessfulRefresh.HasValue &&
            minutesSinceLatestSuccessfulRefresh.Value <= thresholdMinutes;
        var staleReason = latestSuccessfulRun is null
            ? "No successful The Odds MLB refresh run has been recorded."
            : isFresh
                ? null
                : $"Latest successful refresh is older than {thresholdMinutes} minutes.";

        return new MlbRefreshFreshnessResponse(
            generatedAtUtc,
            CallsExternalApis: false,
            ReadOnly: true,
            ToTheOddsRefreshRunSummary(latestRun),
            ToTheOddsRefreshRunSummary(latestSuccessfulRun),
            ToTheOddsRefreshRunSummary(latestFailedRun),
            minutesSinceLatestSuccessfulRefresh,
            isFresh,
            IsStale: !isFresh,
            staleReason,
            thresholdMinutes,
            latestSuccessfulRun is null ? null : new
            {
                latestSuccessfulRun.PlatformPropsCreated,
                latestSuccessfulRun.PlatformPropsUpdated,
                latestSuccessfulRun.PlatformPropsUpserted,
                latestSuccessfulRun.PredictionCount,
                latestSuccessfulRun.LineShoppingGroupCount,
                latestSuccessfulRun.ActiveCandidateCount,
                latestSuccessfulRun.ActiveWatchlistCount,
                latestSuccessfulRun.ActiveAvoidCount,
                latestSuccessfulRun.EstimatedApiCalls,
                latestSuccessfulRun.ActualApiCalls
            });
    }

    internal static async Task<MlbTopPicksReadResponse> BuildMlbTopPicksResponseAsync(
        Guid? runId,
        int? take,
        int? freshnessMinutes,
        string? providerCategory,
        ApplicationDbContext dbContext,
        IPredictionProbabilityService probabilityService,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var requestedTake = Math.Clamp(take ?? 10, 1, 100);
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
        var platformProps = await LoadScopedTheOddsPlatformPropsAsync(dbContext, providerCategory, cancellationToken);

        var activePlatformProps = platformProps
            .Where(row =>
            {
                var lifecycle = BuildTheOddsLineShoppingLifecycle(
                    row.EventStartTime,
                    row.ObservedAt,
                    generatedAtUtc,
                    lifecycleFreshnessMinutes);
                return string.Equals(lifecycle.Status, "Active", StringComparison.OrdinalIgnoreCase)
                    && !lifecycle.IsStale
                    && !lifecycle.IsExpired;
            })
            .ToList();

        var probabilityAnalysis = await LoadProbabilityAnalysisAsync(
            runId,
            activePlatformProps.Select(row => row.PropId).Distinct().ToArray(),
            probabilityService,
            cancellationToken);

        var freshnessCutoff = generatedAtUtc.AddHours(-FreshnessWindowHours);
        var rows = BuildTheOddsLineShoppingRows(activePlatformProps, probabilityAnalysis, freshnessCutoff);
        var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, lifecycleFreshnessMinutes);
        var strategyClassificationSummary = BuildTheOddsStrategyClassificationSummary(groups);
        var candidateGroups = groups
            .Select(group => new
            {
                Group = group,
                Strategy = ClassifyTheOddsLineShoppingStrategy(group)
            })
            .Where(row => row.Strategy.IsCandidate)
            .ToArray();
        var preferredCandidateGroups = candidateGroups.Any(row => row.Group.IsActionableForUser)
            ? candidateGroups.Where(row => row.Group.IsActionableForUser)
            : candidateGroups;
        var topPicks = preferredCandidateGroups
            .OrderByDescending(row => row.Group.BestEstimatedEv ?? decimal.MinValue)
            .ThenByDescending(row => row.Group.RankingScore)
            .Take(requestedTake)
            .Select(row => ToLineShoppingResponseGroup(row.Group, includeBookmakerPrices: false))
            .ToArray();
        totalStopwatch.Stop();

        return new MlbTopPicksReadResponse(
            generatedAtUtc,
            Diagnostics: new
            {
                callsTheOddsApi = false,
                usesPersistedDatabaseRowsOnly = true,
                totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                lifecycleFreshnessMinutes,
                take = requestedTake,
                providerCategory,
                topPicksOnly = true
            },
            Run: ToRunResponse(probabilityAnalysis),
            Summary: new
            {
                totalActiveComparisonGroups = groups.Count,
                activeCandidateCount = candidateGroups.Length,
                actionableCandidateCount = candidateGroups.Count(row => row.Group.IsActionableForUser),
                sportsbookContextCandidateCount = candidateGroups.Count(row => !row.Group.IsActionableForUser),
                dfsCandidateCount = candidateGroups.Count(row => string.Equals(row.Group.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
                topPicksPreferredProviderCategory = candidateGroups.Any(row => row.Group.IsActionableForUser) ? "DFS actionable" : "Sportsbook context fallback",
                activeWatchlistCount = groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsWatchlist),
                returnedTopPicks = topPicks.Length
            },
            strategyClassificationSummary,
            topPicks);
    }

    internal static async Task<MlbLineShoppingReadResponse> BuildMlbLineShoppingResponseAsync(
        Guid? runId,
        bool summaryOnly,
        bool topOnly,
        int take,
        bool includeBookmakerPrices,
        int? freshnessMinutes,
        string? providerCategory,
        ApplicationDbContext dbContext,
        IPredictionProbabilityService probabilityService,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
        var requestedTake = Math.Clamp(take, 1, 500);
        var platformProps = await LoadScopedTheOddsPlatformPropsAsync(dbContext, providerCategory, cancellationToken);
        var probabilityAnalysis = await LoadProbabilityAnalysisAsync(
            runId,
            platformProps.Select(row => row.PropId).Distinct().ToArray(),
            probabilityService,
            cancellationToken);
        var freshnessCutoff = generatedAtUtc.AddHours(-FreshnessWindowHours);
        var rows = BuildTheOddsLineShoppingRows(platformProps, probabilityAnalysis, freshnessCutoff);
        var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, lifecycleFreshnessMinutes);
        var selectedGroups = SelectLineShoppingResponseGroups(groups, summaryOnly, topOnly, requestedTake);
        var comparisonGroups = selectedGroups
            .Select(group => ToLineShoppingResponseGroup(group, includeBookmakerPrices))
            .ToArray();
        var strategyClassificationSummary = BuildTheOddsStrategyClassificationSummary(groups);
        totalStopwatch.Stop();

        return new MlbLineShoppingReadResponse(
            generatedAtUtc,
            Diagnostics: new
            {
                callsTheOddsApi = false,
                usesPersistedDatabaseRowsOnly = true,
                totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                lifecycleFreshnessMinutes,
                summaryOnly,
                topOnly,
                take = requestedTake,
                includeBookmakerPrices,
                providerCategory
            },
            Run: ToRunResponse(probabilityAnalysis),
            Summary: new
            {
                totalComparisonGroups = groups.Count,
                totalMarketsCompared = rows.Count,
                totalBookmakers = rows.Select(row => row.Bookmaker).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                groupsWithMultipleBookmakers = groups.Count(group => group.BookmakerPrices.Count > 1),
                groupsWithSingleBookmaker = groups.Count(group => group.BookmakerPrices.Count == 1),
                eligibleComparisonGroups = groups.Count(group => group.IsEligibleAsNormalRecommendation),
                returnedComparisonGroups = comparisonGroups.Length,
                actionableCandidateCount = groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate && group.IsActionableForUser),
                sportsbookContextCandidateCount = groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate && !group.IsActionableForUser),
                dfsCandidateCount = groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate && string.Equals(group.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
                providerCategoryBreakdown = groups
                    .GroupBy(group => group.ProviderCategory, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        providerCategory = group.Key,
                        count = group.Count(),
                        activeCount = group.Count(row => string.Equals(row.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase)),
                        candidateCount = group.Count(row => ClassifyTheOddsLineShoppingStrategy(row).IsCandidate)
                    })
                    .OrderBy(row => row.providerCategory)
                    .ToArray()
            },
            strategyClassificationSummary,
            comparisonGroups);
    }

    private static async Task<List<TheOddsPersistedPlatformPropRow>> LoadScopedTheOddsPlatformPropsAsync(
        ApplicationDbContext dbContext,
        string? providerCategory,
        CancellationToken cancellationToken)
    {
        var rows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
        var platformProps = ParseTheOddsPlatformProps(rows);

        return string.IsNullOrWhiteSpace(providerCategory)
            ? platformProps
            : platformProps
                .Where(row => string.Equals(row.ProviderCategory, providerCategory, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private static async Task<PredictionProbabilityRunAnalysis> LoadProbabilityAnalysisAsync(
        Guid? runId,
        Guid[] propIds,
        IPredictionProbabilityService probabilityService,
        CancellationToken cancellationToken)
    {
        var probabilityAnalysis = runId.HasValue
            ? await probabilityService.AnalyzeRunForPropsAsync(runId.Value, propIds, cancellationToken)
            : await probabilityService.AnalyzeLatestRunForPropsAsync(propIds, cancellationToken);

        return probabilityAnalysis ?? throw new InvalidOperationException($"Prediction run '{runId}' was not found.");
    }

    private static IReadOnlyList<TheOddsLineShoppingGroup> SelectLineShoppingResponseGroups(
        IReadOnlyList<TheOddsLineShoppingGroup> groups,
        bool summaryOnly,
        bool topOnly,
        int take)
    {
        if (summaryOnly)
        {
            return Array.Empty<TheOddsLineShoppingGroup>();
        }

        if (!topOnly)
        {
            return groups
                .OrderByDescending(group => group.BestEstimatedEv ?? decimal.MinValue)
                .ThenByDescending(group => group.RankingScore)
                .Take(take)
                .ToArray();
        }

        return groups
            .Select(group => new
            {
                Group = group,
                Strategy = ClassifyTheOddsLineShoppingStrategy(group)
            })
            .Where(row => string.Equals(row.Group.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase)
                && !row.Group.IsStale
                && !row.Group.IsExpired
                && (row.Strategy.IsCandidate
                    || row.Strategy.IsWatchlist
                    || string.Equals(row.Strategy.StrategyClassification, "Research", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => GetLineShoppingTopOnlyStrategyRank(row.Strategy))
            .ThenBy(row => row.Group.IsActionableForUser ? 0 : 1)
            .ThenByDescending(row => row.Group.BestEstimatedEv ?? decimal.MinValue)
            .ThenByDescending(row => row.Group.RankingScore)
            .Take(take)
            .Select(row => row.Group)
            .ToArray();
    }

    private static object ToRunResponse(PredictionProbabilityRunAnalysis probabilityAnalysis)
    {
        return new
        {
            runId = probabilityAnalysis.RunId,
            probabilityAnalysis.ModelName,
            probabilityAnalysis.ModelVersion,
            probabilityAnalysis.CreatedAt,
            probabilityAnalysis.PredictionCount
        };
    }
}
