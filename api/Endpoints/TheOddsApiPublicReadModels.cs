using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
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
        bool HasUsableMarketData,
        int LineShoppingSnapshotCount,
        int TopPickSnapshotCount,
        int ActiveGroupCount,
        int StaleOrExpiredGroupCount,
        int ProviderIngestionFailureCount,
        int ProviderIngestionSuccessCount,
        bool ProviderRejectedKeyDetected,
        string? UsabilityWarning,
        object? LatestCounts);

    internal sealed record MlbLineShoppingReadResponse(
        DateTimeOffset GeneratedAtUtc,
        bool SnapshotBacked,
        Guid? RefreshRunId,
        string? FallbackReason,
        object Diagnostics,
        object Run,
        object Summary,
        object StrategyClassificationSummary,
        IReadOnlyList<object> ComparisonGroups);

    internal sealed record MlbTopPicksReadResponse(
        DateTimeOffset GeneratedAtUtc,
        bool SnapshotBacked,
        Guid? RefreshRunId,
        string? FallbackReason,
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
        var lineShoppingSnapshotCount = latestSuccessfulRun is null
            ? 0
            : await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .CountAsync(row => row.TheOddsRefreshRunId == latestSuccessfulRun.Id, cancellationToken);
        var topPickSnapshotCount = latestSuccessfulRun is null
            ? 0
            : await dbContext.MarketTopPickSnapshots
                .AsNoTracking()
                .CountAsync(row => row.TheOddsRefreshRunId == latestSuccessfulRun.Id, cancellationToken);
        var activeLineShoppingSnapshotCount = latestSuccessfulRun is null
            ? 0
            : await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .CountAsync(row => row.TheOddsRefreshRunId == latestSuccessfulRun.Id
                    && row.LifecycleStatus == "Active"
                    && !row.IsStale
                    && !row.IsExpired, cancellationToken);
        var staleOrExpiredSnapshotCount = Math.Max(0, lineShoppingSnapshotCount - activeLineShoppingSnapshotCount);
        var diagnostics = ExtractRefreshDiagnostics(latestSuccessfulRun?.DiagnosticsJson);
        var activeGroupCount = lineShoppingSnapshotCount > 0
            ? activeLineShoppingSnapshotCount
            : diagnostics.ActiveGroupCount;
        var staleOrExpiredGroupCount = lineShoppingSnapshotCount > 0
            ? staleOrExpiredSnapshotCount
            : diagnostics.StaleOrExpiredGroupCount;
        var hasUsableMarketData = topPickSnapshotCount > 0 || activeGroupCount > 0;
        var usabilityWarning = BuildRefreshUsabilityWarning(
            latestSuccessfulRun,
            hasUsableMarketData,
            lineShoppingSnapshotCount,
            topPickSnapshotCount,
            diagnostics);

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
            hasUsableMarketData,
            lineShoppingSnapshotCount,
            topPickSnapshotCount,
            activeGroupCount,
            staleOrExpiredGroupCount,
            diagnostics.ProviderIngestionFailureCount,
            diagnostics.ProviderIngestionSuccessCount,
            diagnostics.ProviderRejectedKeyDetected,
            usabilityWarning,
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
        if (!runId.HasValue)
        {
            var snapshot = await TryBuildTopPicksSnapshotResponseAsync(
                take,
                providerCategory,
                dbContext,
                cancellationToken);
            if (snapshot is not null)
            {
                return snapshot;
            }
        }

        var totalStopwatch = Stopwatch.StartNew();
        var fallbackReason = await BuildSnapshotFallbackReasonAsync(
            topPickSnapshots: true,
            providerCategory,
            dbContext,
            cancellationToken);
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
            SnapshotBacked: false,
            RefreshRunId: null,
            FallbackReason: fallbackReason,
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
        if (!runId.HasValue && !summaryOnly)
        {
            var snapshot = await TryBuildLineShoppingSnapshotResponseAsync(
                topOnly,
                take,
                includeBookmakerPrices,
                providerCategory,
                dbContext,
                cancellationToken);
            if (snapshot is not null)
            {
                return snapshot;
            }
        }

        var totalStopwatch = Stopwatch.StartNew();
        var fallbackReason = await BuildSnapshotFallbackReasonAsync(
            topPickSnapshots: false,
            providerCategory,
            dbContext,
            cancellationToken);
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
            SnapshotBacked: false,
            RefreshRunId: null,
            FallbackReason: fallbackReason,
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

    private static async Task PersistTheOddsMarketSnapshotsAsync(
        TheOddsRefreshRun refreshRun,
        Guid? predictionRunId,
        DateTimeOffset generatedAtUtc,
        IReadOnlyCollection<TheOddsLineShoppingGroup> lineShoppingGroups,
        IReadOnlyCollection<TheOddsLineShoppingGroup> topPickGroups,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var existingTopPicks = await dbContext.MarketTopPickSnapshots
            .Where(row => row.TheOddsRefreshRunId == refreshRun.Id)
            .ToListAsync(cancellationToken);
        var existingLineShopping = await dbContext.MarketLineShoppingSnapshots
            .Where(row => row.TheOddsRefreshRunId == refreshRun.Id)
            .ToListAsync(cancellationToken);

        dbContext.MarketTopPickSnapshots.RemoveRange(existingTopPicks);
        dbContext.MarketLineShoppingSnapshots.RemoveRange(existingLineShopping);

        dbContext.MarketLineShoppingSnapshots.AddRange(lineShoppingGroups.Select(group =>
            BuildLineShoppingSnapshot(refreshRun, predictionRunId, generatedAtUtc, group)));
        dbContext.MarketTopPickSnapshots.AddRange(topPickGroups.Select(group =>
            BuildTopPickSnapshot(refreshRun, predictionRunId, generatedAtUtc, group)));
    }

    private static MarketTopPickSnapshot BuildTopPickSnapshot(
        TheOddsRefreshRun refreshRun,
        Guid? predictionRunId,
        DateTimeOffset generatedAtUtc,
        TheOddsLineShoppingGroup group)
    {
        var strategy = ClassifyTheOddsLineShoppingStrategy(group);
        var entry = BuildTheOddsEntryCompatibility(group);

        return new MarketTopPickSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            PredictionRunId = predictionRunId,
            Sport = refreshRun.Sport,
            SlateDate = refreshRun.SlateDate,
            GeneratedAtUtc = generatedAtUtc,
            BookmakerPreset = refreshRun.BookmakerPreset,
            ProviderCategory = group.ProviderCategory,
            Provider = entry.EntryProvider,
            Bookmaker = group.BestBookmaker,
            Player = group.Player,
            EventId = group.EventId,
            EventName = BuildSnapshotEventName(group),
            EventStartTime = group.EventStartTime,
            LifecycleStatus = group.LifecycleStatus,
            IsStale = group.IsStale,
            IsExpired = group.IsExpired,
            PropType = group.NormalizedPropType,
            Line = group.Line,
            BestSide = group.BestSide,
            BestBookmaker = group.BestBookmaker,
            BestAmericanPrice = group.BestAmericanPrice,
            BestDecimalPrice = group.BestDecimalPrice,
            BestProbability = group.BestProbability,
            BestImpliedProbability = group.BestImpliedProbability,
            BestProbabilityEdge = group.BestProbabilityEdge,
            BestEstimatedEv = group.BestEstimatedEv,
            StrategyClassification = strategy.StrategyClassification,
            StrategyTier = strategy.StrategyTier,
            IsCandidate = strategy.IsCandidate,
            IsWatchlist = strategy.IsWatchlist,
            IsAvoid = strategy.IsAvoid,
            IsResearchOnly = strategy.IsResearchOnly,
            EntryProvider = entry.EntryProvider,
            EntryCompatibilityGroup = entry.EntryCompatibilityGroup,
            EntryProviderCategory = entry.EntryProviderCategory,
            EntryActionableForUser = entry.EntryActionableForUser,
            DetailsJson = JsonSerializer.SerializeToDocument(ToLineShoppingResponseGroup(group, includeBookmakerPrices: false))
        };
    }

    private static MarketLineShoppingSnapshot BuildLineShoppingSnapshot(
        TheOddsRefreshRun refreshRun,
        Guid? predictionRunId,
        DateTimeOffset generatedAtUtc,
        TheOddsLineShoppingGroup group)
    {
        var strategy = ClassifyTheOddsLineShoppingStrategy(group);
        var entry = BuildTheOddsEntryCompatibility(group);

        return new MarketLineShoppingSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            PredictionRunId = predictionRunId,
            Sport = refreshRun.Sport,
            SlateDate = refreshRun.SlateDate,
            GeneratedAtUtc = generatedAtUtc,
            BookmakerPreset = refreshRun.BookmakerPreset,
            ProviderCategory = group.ProviderCategory,
            Provider = entry.EntryProvider,
            Bookmaker = group.BestBookmaker,
            Player = group.Player,
            EventId = group.EventId,
            EventName = BuildSnapshotEventName(group),
            EventStartTime = group.EventStartTime,
            LifecycleStatus = group.LifecycleStatus,
            IsStale = group.IsStale,
            IsExpired = group.IsExpired,
            PropType = group.NormalizedPropType,
            Line = group.Line,
            BestSide = group.BestSide,
            BestBookmaker = group.BestBookmaker,
            BestAmericanPrice = group.BestAmericanPrice,
            BestDecimalPrice = group.BestDecimalPrice,
            BestProbability = group.BestProbability,
            BestImpliedProbability = group.BestImpliedProbability,
            BestProbabilityEdge = group.BestProbabilityEdge,
            BestEstimatedEv = group.BestEstimatedEv,
            StrategyClassification = strategy.StrategyClassification,
            StrategyTier = strategy.StrategyTier,
            IsCandidate = strategy.IsCandidate,
            IsWatchlist = strategy.IsWatchlist,
            IsAvoid = strategy.IsAvoid,
            IsResearchOnly = strategy.IsResearchOnly,
            EntryProvider = entry.EntryProvider,
            EntryCompatibilityGroup = entry.EntryCompatibilityGroup,
            EntryProviderCategory = entry.EntryProviderCategory,
            EntryActionableForUser = entry.EntryActionableForUser,
            DetailsJson = JsonSerializer.SerializeToDocument(ToLineShoppingResponseGroup(group, includeBookmakerPrices: true))
        };
    }

    private static async Task<MlbTopPicksReadResponse?> TryBuildTopPicksSnapshotResponseAsync(
        int? take,
        string? providerCategory,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var refreshRunId = await FindLatestTopPickSnapshotRefreshRunIdAsync(
            dbContext,
            providerCategory,
            cancellationToken);
        if (!refreshRunId.HasValue)
        {
            return null;
        }

        var requestedTake = Math.Clamp(take ?? 10, 1, 100);
        var query = dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(row => row.TheOddsRefreshRunId == refreshRunId.Value);
        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.ProviderCategory == providerCategory);
        }

        var rows = await query
            .OrderByDescending(row => row.BestEstimatedEv)
            .ThenBy(row => row.EntryActionableForUser ? 0 : 1)
            .Take(requestedTake)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var refreshRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .FirstAsync(row => row.Id == refreshRunId.Value, cancellationToken);

        return new MlbTopPicksReadResponse(
            rows.FirstOrDefault()?.GeneratedAtUtc ?? refreshRun.CompletedAtUtc ?? refreshRun.StartedAtUtc,
            SnapshotBacked: true,
            RefreshRunId: refreshRun.Id,
            FallbackReason: null,
            Diagnostics: BuildSnapshotDiagnostics(rows.Count, providerCategory),
            Run: new { runId = rows.FirstOrDefault()?.PredictionRunId, predictionCount = refreshRun.PredictionCount },
            Summary: BuildSnapshotSummary(rows),
            StrategyClassificationSummary: BuildSnapshotStrategySummary(rows),
            TopPicks: rows.Select(row => (object)row.DetailsJson.RootElement.Clone()).ToArray());
    }

    private static async Task<MlbLineShoppingReadResponse?> TryBuildLineShoppingSnapshotResponseAsync(
        bool topOnly,
        int take,
        bool includeBookmakerPrices,
        string? providerCategory,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var refreshRunId = await FindLatestLineShoppingSnapshotRefreshRunIdAsync(
            dbContext,
            providerCategory,
            cancellationToken);
        if (!refreshRunId.HasValue)
        {
            return null;
        }

        var requestedTake = Math.Clamp(take, 1, 500);
        var query = dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.TheOddsRefreshRunId == refreshRunId.Value);
        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.ProviderCategory == providerCategory);
        }

        if (topOnly)
        {
            query = query.Where(row => row.LifecycleStatus == "Active"
                && !row.IsStale
                && !row.IsExpired
                && (row.IsCandidate || row.IsWatchlist || row.StrategyClassification == "Research"));
        }

        var rows = await query
            .OrderBy(row => row.IsCandidate ? 0 : row.IsWatchlist ? 1 : 2)
            .ThenBy(row => row.EntryActionableForUser ? 0 : 1)
            .ThenByDescending(row => row.BestEstimatedEv)
            .Take(requestedTake)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var refreshRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .FirstAsync(row => row.Id == refreshRunId.Value, cancellationToken);

        return new MlbLineShoppingReadResponse(
            rows.FirstOrDefault()?.GeneratedAtUtc ?? refreshRun.CompletedAtUtc ?? refreshRun.StartedAtUtc,
            SnapshotBacked: true,
            RefreshRunId: refreshRun.Id,
            FallbackReason: null,
            Diagnostics: BuildSnapshotDiagnostics(rows.Count, providerCategory, includeBookmakerPrices),
            Run: new { runId = rows.FirstOrDefault()?.PredictionRunId, predictionCount = refreshRun.PredictionCount },
            Summary: BuildSnapshotSummary(rows),
            StrategyClassificationSummary: BuildSnapshotStrategySummary(rows),
            ComparisonGroups: rows
                .Select(row => (object)(includeBookmakerPrices
                    ? row.DetailsJson.RootElement.Clone()
                    : StripBookmakerPrices(row.DetailsJson)))
                .ToArray());
    }

    private static async Task<Guid?> FindLatestTopPickSnapshotRefreshRunIdAsync(
        ApplicationDbContext dbContext,
        string? providerCategory,
        CancellationToken cancellationToken)
    {
        var query = dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == "MLB")
            .Join(
                dbContext.TheOddsRefreshRuns.AsNoTracking().Where(row => row.Succeeded),
                snapshot => snapshot.TheOddsRefreshRunId,
                refreshRun => refreshRun.Id,
                (snapshot, refreshRun) => new { snapshot, refreshRun });

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.snapshot.ProviderCategory == providerCategory);
        }

        return await query
            .OrderByDescending(row => row.refreshRun.CompletedAtUtc)
            .ThenByDescending(row => row.snapshot.GeneratedAtUtc)
            .Select(row => (Guid?)row.refreshRun.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<string> BuildSnapshotFallbackReasonAsync(
        bool topPickSnapshots,
        string? providerCategory,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var latestSuccessfulRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API" && row.Sport == "MLB" && row.Succeeded)
            .OrderByDescending(row => row.CompletedAtUtc)
            .ThenByDescending(row => row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSuccessfulRun is null)
        {
            return "No successful snapshot exists yet.";
        }

        var count = topPickSnapshots
            ? await dbContext.MarketTopPickSnapshots
                .AsNoTracking()
                .Where(row => row.TheOddsRefreshRunId == latestSuccessfulRun.Id)
                .Where(row => string.IsNullOrWhiteSpace(providerCategory) || row.ProviderCategory == providerCategory)
                .CountAsync(cancellationToken)
            : await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(row => row.TheOddsRefreshRunId == latestSuccessfulRun.Id)
                .Where(row => string.IsNullOrWhiteSpace(providerCategory) || row.ProviderCategory == providerCategory)
                .CountAsync(cancellationToken);

        return count == 0
            ? "Latest refresh produced no persisted market snapshots for the requested filters."
            : "Latest refresh produced snapshots, but none matched the requested active/top-only view.";
    }

    private static async Task<Guid?> FindLatestLineShoppingSnapshotRefreshRunIdAsync(
        ApplicationDbContext dbContext,
        string? providerCategory,
        CancellationToken cancellationToken)
    {
        var query = dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == "MLB")
            .Join(
                dbContext.TheOddsRefreshRuns.AsNoTracking().Where(row => row.Succeeded),
                snapshot => snapshot.TheOddsRefreshRunId,
                refreshRun => refreshRun.Id,
                (snapshot, refreshRun) => new { snapshot, refreshRun });

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.snapshot.ProviderCategory == providerCategory);
        }

        return await query
            .OrderByDescending(row => row.refreshRun.CompletedAtUtc)
            .ThenByDescending(row => row.snapshot.GeneratedAtUtc)
            .Select(row => (Guid?)row.refreshRun.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? BuildSnapshotEventName(TheOddsLineShoppingGroup group)
    {
        if (!string.IsNullOrWhiteSpace(group.AwayTeam) && !string.IsNullOrWhiteSpace(group.HomeTeam))
        {
            return $"{group.AwayTeam} @ {group.HomeTeam}";
        }

        return group.EventId;
    }

    private static object BuildSnapshotDiagnostics(
        int returnedRows,
        string? providerCategory,
        bool includeBookmakerPrices = false)
    {
        return new
        {
            callsTheOddsApi = false,
            usesPersistedDatabaseRowsOnly = true,
            snapshotBacked = true,
            returnedRows,
            providerCategory,
            includeBookmakerPrices
        };
    }

    private static object BuildSnapshotSummary(IReadOnlyCollection<MarketTopPickSnapshot> rows)
    {
        return new
        {
            totalSnapshotRowsReturned = rows.Count,
            returnedTopPicks = rows.Count,
            activeCandidateCount = rows.Count(row => row.IsCandidate),
            actionableCandidateCount = rows.Count(row => row.IsCandidate && row.EntryActionableForUser),
            sportsbookContextCandidateCount = rows.Count(row => row.IsCandidate && !row.EntryActionableForUser),
            dfsCandidateCount = rows.Count(row => row.IsCandidate && string.Equals(row.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static object BuildSnapshotSummary(IReadOnlyCollection<MarketLineShoppingSnapshot> rows)
    {
        return new
        {
            totalSnapshotRowsReturned = rows.Count,
            returnedComparisonGroups = rows.Count,
            activeCandidateCount = rows.Count(row => row.IsCandidate),
            actionableCandidateCount = rows.Count(row => row.IsCandidate && row.EntryActionableForUser),
            sportsbookContextCandidateCount = rows.Count(row => row.IsCandidate && !row.EntryActionableForUser),
            dfsCandidateCount = rows.Count(row => row.IsCandidate && string.Equals(row.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
            providerCategoryBreakdown = rows
                .GroupBy(row => row.ProviderCategory, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    providerCategory = group.Key,
                    count = group.Count(),
                    candidateCount = group.Count(row => row.IsCandidate),
                    watchlistCount = group.Count(row => row.IsWatchlist),
                    avoidCount = group.Count(row => row.IsAvoid)
                })
                .OrderBy(row => row.providerCategory)
                .ToArray()
        };
    }

    private static object[] BuildSnapshotStrategySummary(IReadOnlyCollection<MarketTopPickSnapshot> rows)
    {
        return rows
            .GroupBy(row => new { row.StrategyClassification, row.StrategyTier })
            .Select(group => new
            {
                classification = group.Key.StrategyClassification,
                tier = group.Key.StrategyTier,
                count = group.Count(),
                averageBestEstimatedEv = group.Where(row => row.BestEstimatedEv.HasValue).Select(row => row.BestEstimatedEv!.Value).DefaultIfEmpty().Average(),
                averageBestProbability = group.Where(row => row.BestProbability.HasValue).Select(row => row.BestProbability!.Value).DefaultIfEmpty().Average(),
                averageBestPrice = group.Where(row => row.BestDecimalPrice.HasValue).Select(row => row.BestDecimalPrice!.Value).DefaultIfEmpty().Average()
            })
            .OrderByDescending(row => row.count)
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildSnapshotStrategySummary(IReadOnlyCollection<MarketLineShoppingSnapshot> rows)
    {
        return rows
            .GroupBy(row => new { row.StrategyClassification, row.StrategyTier })
            .Select(group => new
            {
                classification = group.Key.StrategyClassification,
                tier = group.Key.StrategyTier,
                count = group.Count(),
                averageBestEstimatedEv = group.Where(row => row.BestEstimatedEv.HasValue).Select(row => row.BestEstimatedEv!.Value).DefaultIfEmpty().Average(),
                averageBestProbability = group.Where(row => row.BestProbability.HasValue).Select(row => row.BestProbability!.Value).DefaultIfEmpty().Average(),
                averageBestPrice = group.Where(row => row.BestDecimalPrice.HasValue).Select(row => row.BestDecimalPrice!.Value).DefaultIfEmpty().Average()
            })
            .OrderByDescending(row => row.count)
            .Cast<object>()
            .ToArray();
    }

    private static JsonElement StripBookmakerPrices(JsonDocument detailsJson)
    {
        var node = JsonSerializer.SerializeToNode(detailsJson.RootElement);
        if (node is JsonObject obj)
        {
            obj["bookmakerPrices"] = new JsonArray();
        }

        using var document = JsonSerializer.SerializeToDocument(node);
        return document.RootElement.Clone();
    }

    private static RefreshDiagnosticsSummary ExtractRefreshDiagnostics(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null)
        {
            return new RefreshDiagnosticsSummary(0, 0, 0, 0, ProviderRejectedKeyDetected: false);
        }

        var root = diagnosticsJson.RootElement;
        var activeGroupCount = TryGetInt32(root, "lineShoppingSummary", "activeGroups") ?? 0;
        var staleOrExpiredGroupCount = TryGetInt32(root, "lineShoppingSummary", "staleOrExpiredGroups") ?? 0;
        var providerSuccessCount = 0;
        var providerFailureCount = 0;
        var providerRejectedKeyDetected = false;

        if (root.TryGetProperty("ingestion", out var ingestion) &&
            ingestion.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ingestion.EnumerateArray())
            {
                var success = item.TryGetProperty("success", out var successElement) &&
                    successElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                    successElement.GetBoolean();
                if (success)
                {
                    providerSuccessCount++;
                }
                else
                {
                    providerFailureCount++;
                }

                providerRejectedKeyDetected = providerRejectedKeyDetected ||
                    ContainsText(item, "rejected the configured API key") ||
                    ContainsText(item, "INVALID_KEY");
            }
        }

        return new RefreshDiagnosticsSummary(
            activeGroupCount,
            staleOrExpiredGroupCount,
            providerFailureCount,
            providerSuccessCount,
            providerRejectedKeyDetected);
    }

    private static string? BuildRefreshUsabilityWarning(
        TheOddsRefreshRun? latestSuccessfulRun,
        bool hasUsableMarketData,
        int lineShoppingSnapshotCount,
        int topPickSnapshotCount,
        RefreshDiagnosticsSummary diagnostics)
    {
        if (latestSuccessfulRun is null)
        {
            return "No successful The Odds MLB refresh metadata exists yet.";
        }

        if (hasUsableMarketData)
        {
            return null;
        }

        if (diagnostics.ProviderRejectedKeyDetected)
        {
            return "Latest refresh was recent but provider ingestion appears to have failed because The Odds API rejected the configured API key.";
        }

        if (diagnostics.ProviderIngestionFailureCount > 0 && diagnostics.ProviderIngestionSuccessCount == 0)
        {
            return "Latest refresh was recent but all provider event-odds ingestion attempts failed, so no usable market snapshots were produced.";
        }

        if (lineShoppingSnapshotCount == 0 && topPickSnapshotCount == 0)
        {
            return "Latest refresh produced no persisted market snapshots.";
        }

        return "Latest refresh exists but no usable active market data was found.";
    }

    private static int? TryGetInt32(JsonElement root, string parent, string property)
    {
        if (!root.TryGetProperty(parent, out var parentElement) ||
            parentElement.ValueKind != JsonValueKind.Object ||
            !parentElement.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
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

    private sealed record RefreshDiagnosticsSummary(
        int ActiveGroupCount,
        int StaleOrExpiredGroupCount,
        int ProviderIngestionFailureCount,
        int ProviderIngestionSuccessCount,
        bool ProviderRejectedKeyDetected);

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
