using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Endpoints;

public static partial class TheOddsApiDebugEndpoints
{
    private const string ApiSportsFootballResultSource = "api-sports-football";
    private const string ApiSportsFootballPlayerRefProvider = "api-sports-football";

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
        ILikelyHitModelService likelyHitModel,
        PredictionGenerationOptions selectionOptions,
        TopPicksModelOptions topPicksModelOptions,
        CancellationToken cancellationToken)
    {
        providerCategory = NormalizePublicProviderCategoryFilter(providerCategory);

        if (!runId.HasValue && !topPicksModelOptions.IsLikelyHitV1Enabled)
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
        var requestedTake = Math.Clamp(take ?? 10, 1, 500);
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
        var platformProps = await LoadScopedTheOddsPlatformPropsAsync(dbContext, providerCategory, cancellationToken);
        if (platformProps.Count == 0 && !runId.HasValue)
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
        var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, lifecycleFreshnessMinutes, selectionOptions);
        var strategyClassificationSummary = BuildTheOddsStrategyClassificationSummary(groups);
        var candidateGroups = groups
            .Select(group => new
            {
                Group = group,
                Strategy = ClassifyTheOddsLineShoppingStrategy(group)
            })
            .Where(row => row.Strategy.IsCandidate)
            .ToArray();
        var topPicks = topPicksModelOptions.IsLikelyHitV1Enabled
            ? BuildLikelyHitTopPickRows(
                groups.Select(group =>
                    JsonSerializer.SerializeToNode(ToLineShoppingResponseGroup(group, includeBookmakerPrices: false)) as JsonObject)
                    .Where(row => row is not null)
                    .Select(row => row!),
                likelyHitModel,
                topPicksModelOptions,
                requestedTake)
                .Cast<object>()
                .ToArray()
            : candidateGroups
                .OrderByDescending(row => row.Group.BestProbability ?? decimal.MinValue)
                .ThenByDescending(row => row.Group.BestProjectionEdge ?? decimal.MinValue)
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
                activeCandidateCount = topPicksModelOptions.IsLikelyHitV1Enabled ? topPicks.Length : candidateGroups.Length,
                actionableCandidateCount = topPicks.Count(row =>
                    JsonSerializer.SerializeToNode(row) is JsonObject obj && ReadLikelyHitBool(obj, "entryActionableForUser") == true),
                sportsbookContextCandidateCount = topPicks.Count(row =>
                    JsonSerializer.SerializeToNode(row) is JsonObject obj && string.Equals(ReadLikelyHitString(obj, "providerCategory"), "Sportsbook", StringComparison.OrdinalIgnoreCase)),
                dfsCandidateCount = topPicks.Count(row =>
                    JsonSerializer.SerializeToNode(row) is JsonObject obj && string.Equals(ReadLikelyHitString(obj, "providerCategory"), "DFS", StringComparison.OrdinalIgnoreCase)),
                modelVersion = topPicksModelOptions.IsLikelyHitV1Enabled ? LikelyHitModelService.ModelVersion : TopPicksModelOptions.Legacy,
                recommendationContractVersion = topPicksModelOptions.IsLikelyHitV1Enabled ? LikelyHitModelService.RecommendationContractVersion : null,
                probabilityModelVersion = topPicksModelOptions.IsLikelyHitV1Enabled ? LikelyHitModelService.ProbabilityModelVersion : null,
                modelEra = ResolveCurrentPublicModelEra(topPicksModelOptions),
                topPicksProviderCategoryBehavior = "Mixed DFS and Sportsbook recommendations are eligible; providerCategory filters can isolate one category.",
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
        PredictionGenerationOptions selectionOptions,
        CancellationToken cancellationToken)
    {
        providerCategory = NormalizePublicProviderCategoryFilter(providerCategory);

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
        var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, lifecycleFreshnessMinutes, selectionOptions);
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

    internal static async Task<MlbLineShoppingReadResponse> BuildSoccerWorldCupLineShoppingContextResponseAsync(
        int? take,
        bool includeBookmakerPrices,
        int? freshnessMinutes,
        string? providerCategory,
        ISoccerWorldCupPredictionService soccerPredictionService,
        SoccerPredictionsOptions soccerPredictionOptions,
        SoccerGradingOptions soccerGradingOptions,
        SoccerTopPicksOptions soccerTopPicksOptions,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken,
        bool includePlayerHistory = true,
        int? evaluationTake = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var stepStopwatch = Stopwatch.StartNew();
        var timingStages = new List<PublicMarketReadTimingStage>();
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
        var requestedTake = Math.Clamp(take ?? 50, 1, 500);
        var requestedEvaluationTake = Math.Clamp(evaluationTake ?? requestedTake, 1, 1000);
        AddTimingStage(timingStages, stepStopwatch, "request/filter normalization", rowCount: null);
        var latestSoccerRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API" && row.Sport == "SOCCER" && row.Succeeded && !row.DryRun)
            .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        AddTimingStage(timingStages, stepStopwatch, "freshness/run lookup", latestSoccerRun is null ? 0 : 1);
        var platformProps = await LoadSoccerWorldCupSportsbookPlatformPropsAsync(
            dbContext,
            providerCategory,
            cancellationToken);
        AddTimingStage(timingStages, stepStopwatch, "Soccer provider-context query/materialization", platformProps.Count);
        var rows = BuildTheOddsResearchOnlyLineShoppingRows(platformProps);
        AddTimingStage(timingStages, stepStopwatch, "row projection from provider context", rows.Count);
        var predictionBatch = await soccerPredictionService.BuildPredictionsAsync(
            rows.Select(ToSoccerWorldCupPredictionInput).ToArray(),
            requireEnabled: true,
            cancellationToken);
        AddTimingStage(timingStages, stepStopwatch, "prediction/grading enrichment", predictionBatch.InputsEvaluated);
        rows = ApplySoccerWorldCupPredictions(rows, predictionBatch);
        AddTimingStage(timingStages, stepStopwatch, "prediction projection merge", rows.Count);
        var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, lifecycleFreshnessMinutes)
            .OrderByDescending(group => group.EventStartTime ?? group.LastObservedAt)
            .ThenBy(group => group.Player)
            .ThenBy(group => group.NormalizedPropType)
            .ThenBy(group => group.Line)
            .Take(requestedEvaluationTake)
            .ToArray();
        AddTimingStage(timingStages, stepStopwatch, "line-shopping grouping", groups.Length);
        var playerHistoryEnrichment = includePlayerHistory
            ? await BuildSoccerWorldCupPlayerHistoryEnrichmentAsync(
                dbContext,
                groups,
                cancellationToken)
            : SoccerWorldCupPlayerHistoryEnrichment.Empty(groups.Length);
        AddTimingStage(timingStages, stepStopwatch, "player history lookup", includePlayerHistory ? groups.Length : 0);
        var predictionUnavailableReasonsByKey = predictionBatch.PredictionsByKey
            .ToDictionary(row => row.Key, row => row.Value.UnavailableReason, EqualityComparer<SoccerWorldCupPredictionKey>.Default);
        var comparisonGroups = groups
            .Select(group => ToSoccerWorldCupResearchOnlyResponseGroup(
                group,
                includeBookmakerPrices,
                soccerPredictionOptions.Enabled,
                soccerGradingOptions.Enabled,
                soccerTopPicksOptions,
                predictionUnavailableReasonsByKey.GetValueOrDefault(new SoccerWorldCupPredictionKey(group.PlayerId, group.PropTypeId, group.Line)),
                playerHistoryEnrichment.SummariesByPlayerId.GetValueOrDefault(group.PlayerId)))
            .ToArray();
        AddTimingStage(timingStages, stepStopwatch, "final projection/serialization prep", comparisonGroups.Length);
        var strategyClassificationSummary = BuildTheOddsStrategyClassificationSummary(groups);
        AddTimingStage(timingStages, stepStopwatch, "strategy summary", groups.Length);
        totalStopwatch.Stop();

        return new MlbLineShoppingReadResponse(
            generatedAtUtc,
            SnapshotBacked: false,
            RefreshRunId: latestSoccerRun?.Id,
            FallbackReason: platformProps.Count == 0
                ? "No persisted Soccer / World Cup sportsbook provider context rows are available yet."
                : "Built from persisted Soccer / World Cup sportsbook provider context rows because prediction-backed line-shopping snapshots are not enabled for Soccer yet.",
            Diagnostics: new
            {
                callsTheOddsApi = false,
                usesPersistedDatabaseRowsOnly = true,
                source = "platform_props",
                contextSchemaVersion = "the-odds-provider-context-v2",
                sport = "SOCCER",
                competition = "World Cup",
                providerCategory = "Sportsbook",
                endpointKind = includePlayerHistory ? "line-shopping" : "top-picks",
                publicSoccerGradingEnabled = soccerGradingOptions.Enabled,
                soccerPredictionsEnabled = soccerPredictionOptions.Enabled,
                entryActionableForUser = false,
                snapshotBacked = false,
                providerContextRowCount = platformProps.Count,
                researchOnlyComparisonGroupCount = groups.Length,
                rowsWithModelProbability = predictionBatch.RowsWithModelProbability,
                rowsGradeable = predictionBatch.RowsGradeable,
                topPickEligibleRows = predictionBatch.TopPickEligibleRows,
                predictionBlockedReasons = predictionBatch.BlockedReasons,
                rowsWithPlayerHistory = playerHistoryEnrichment.RowsWithPlayerHistory,
                rowsWithoutPlayerHistory = playerHistoryEnrichment.RowsWithoutPlayerHistory,
                uniqueLinkedProviderPlayers = playerHistoryEnrichment.UniqueLinkedProviderPlayers,
                historyStatsIncluded = playerHistoryEnrichment.HistoryStatsIncluded,
                totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                timingStages,
                lifecycleFreshnessMinutes,
                take = requestedTake,
                evaluationTake = requestedEvaluationTake,
                topOnly = false,
                includeBookmakerPrices,
                includePlayerHistory
            },
            Run: new
            {
                runId = latestSoccerRun?.Id,
                latestSoccerRun?.PredictionCount,
                latestSoccerRun?.LineShoppingGroupCount,
                latestSoccerRun?.PlatformPropsUpserted
            },
            Summary: new
            {
                totalComparisonGroups = groups.Length,
                totalMarketsCompared = rows.Count,
                totalBookmakers = rows.Select(row => row.Bookmaker).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                groupsWithMultipleBookmakers = groups.Count(group => group.BookmakerPrices.Count > 1),
                groupsWithSingleBookmaker = groups.Count(group => group.BookmakerPrices.Count == 1),
                eligibleComparisonGroups = groups.Count(group => group.BestProbability.HasValue),
                returnedComparisonGroups = comparisonGroups.Length,
                actionableCandidateCount = 0,
                sportsbookContextCandidateCount = groups.Count(group => group.BestProbability.HasValue),
                dfsCandidateCount = 0,
                providerContextRowCount = platformProps.Count,
                providerCategoryBreakdown = groups
                    .GroupBy(group => group.ProviderCategory, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        providerCategory = group.Key,
                        count = group.Count(),
                        activeCount = group.Count(row => string.Equals(row.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase)),
                        researchOnlyCount = group.Count()
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
        TopPicksModelOptions topPicksModelOptions,
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
            BuildLineShoppingSnapshot(refreshRun, predictionRunId, generatedAtUtc, group, topPicksModelOptions)));
        dbContext.MarketTopPickSnapshots.AddRange(topPickGroups.Select(group =>
            BuildTopPickSnapshot(refreshRun, predictionRunId, generatedAtUtc, group, topPicksModelOptions)));

        var topPickKeys = topPickGroups
            .Select(BuildRecommendationLedgerGroupIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ledgerCandidates = lineShoppingGroups
            .Where(group =>
            {
                var strategy = ClassifyTheOddsLineShoppingStrategy(group);
                return strategy.IsCandidate || strategy.IsWatchlist || topPickKeys.Contains(BuildRecommendationLedgerGroupIdentity(group));
            })
            .Select(group => BuildRecommendationLedgerCandidate(
                refreshRun,
                predictionRunId,
                generatedAtUtc,
                group,
                topPicksModelOptions,
                topPickKeys.Contains(BuildRecommendationLedgerGroupIdentity(group))))
            .ToArray();

        await RecommendationLedgerWriter.UpsertAsync(
            dbContext,
            ledgerCandidates,
            execute: true,
            cancellationToken);
    }

    private static RecommendationLedgerCandidate BuildRecommendationLedgerCandidate(
        TheOddsRefreshRun refreshRun,
        Guid? predictionRunId,
        DateTimeOffset generatedAtUtc,
        TheOddsLineShoppingGroup group,
        TopPicksModelOptions topPicksModelOptions,
        bool isTopPick)
    {
        var strategy = ClassifyTheOddsLineShoppingStrategy(group);
        var entry = BuildTheOddsEntryCompatibility(group);
        var selectedProbability = string.Equals(group.BestSide, "Under", StringComparison.OrdinalIgnoreCase)
            ? group.ModelProbabilityUnder
            : group.ModelProbabilityOver;
        var gradeability = BuildRecommendationGradeability(group, entry.EntryActionableForUser);
        var warning = strategy.StrategyWarnings.FirstOrDefault()
            ?? (string.IsNullOrWhiteSpace(group.SampleWarning) ? null : group.SampleWarning)
            ?? gradeability.Warning;

        return new RecommendationLedgerCandidate(
            CapturedAtUtc: generatedAtUtc,
            RefreshRunId: refreshRun.Id,
            PredictionRunId: predictionRunId,
            Sport: refreshRun.Sport,
            SlateDate: refreshRun.SlateDate,
            ExpectedResultDate: group.ExpectedResultDate,
            ResultDateSource: group.ResultDateSource,
            EventId: group.EventId,
            EventStartTimeUtc: group.EventStartTimeUtc ?? group.EventStartTime,
            CanonicalPlayerId: group.PlayerId,
            PlayerName: group.Player,
            PropTypeId: group.PropTypeId,
            PropType: group.NormalizedPropType,
            Side: group.BestSide,
            Line: group.Line,
            Provider: entry.EntryProvider,
            Bookmaker: group.BestBookmaker,
            ProviderCategory: group.ProviderCategory,
            EntryProvider: entry.EntryProvider,
            EntryCompatibilityGroup: entry.EntryCompatibilityGroup,
            HitChance: selectedProbability,
            ImpliedProbability: group.BestImpliedProbability,
            ProbabilityEdge: group.BestProbabilityEdge,
            EstimatedEv: group.BestEstimatedEv,
            TrustLevel: group.TrustLevel,
            SampleWarning: string.IsNullOrWhiteSpace(group.SampleWarning) ? null : group.SampleWarning,
            Warning: warning,
            HistoricalSampleSize: group.HistoricalSampleSize,
            StrategyClassification: strategy.StrategyClassification,
            StrategyTier: strategy.StrategyTier,
            ModelVersion: ResolveCurrentPublicModelVersion(topPicksModelOptions),
            RecommendationContractVersion: ResolveCurrentRecommendationContractVersion(topPicksModelOptions),
            ProbabilityModelVersion: ResolveCurrentProbabilityModelVersion(topPicksModelOptions),
            ModelEra: ResolveCurrentPublicModelEra(topPicksModelOptions),
            RankingScore: group.RankingScore,
            IsCandidate: strategy.IsCandidate && gradeability.IsGradeable,
            IsTopPick: isTopPick && gradeability.IsGradeable,
            IsWatchlist: strategy.IsWatchlist || !gradeability.IsGradeable,
            IsActionableForUser: entry.EntryActionableForUser,
            LifecycleStatus: group.LifecycleStatus);
    }

    private static RecommendationGradeability BuildRecommendationGradeability(
        TheOddsLineShoppingGroup group,
        bool entryActionableForUser)
    {
        var reasons = new List<string>();

        if (group.PlayerId == Guid.Empty)
        {
            reasons.Add("missing canonical player id");
        }

        if (group.PropTypeId == Guid.Empty || !IsSupportedMlbPerformancePropType(group.NormalizedPropType))
        {
            reasons.Add("unsupported result prop");
        }

        var eventStartTimeUtc = group.EventStartTimeUtc ?? group.EventStartTime;
        if (string.IsNullOrWhiteSpace(group.EventId) || !eventStartTimeUtc.HasValue || group.ExpectedResultDate == default)
        {
            reasons.Add("missing event/result-date mapping");
        }
        else
        {
            var easternDate = DateOnly.FromDateTime(ConvertUtcToMlbLocalTime(eventStartTimeUtc.Value.ToUniversalTime()).Date);
            if (easternDate != group.ExpectedResultDate)
            {
                reasons.Add("event date does not match expected MLB result date");
            }
        }

        if (!string.Equals(group.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase) ||
            group.IsStale ||
            group.IsExpired)
        {
            reasons.Add("line is not currently playable");
        }

        return reasons.Count == 0
            ? new RecommendationGradeability(true, null)
            : new RecommendationGradeability(false, $"Research only: {string.Join("; ", reasons.Distinct(StringComparer.OrdinalIgnoreCase))}.");
    }

    private static bool IsSupportedMlbPerformancePropType(string? propType)
    {
        if (string.IsNullOrWhiteSpace(propType))
        {
            return false;
        }

        return NormalizeTheOddsPropTypeName(propType) is
            "HITS" or
            "RUNS" or
            "RBIS" or
            "HOMERUNS" or
            "TOTALBASES" or
            "STOLENBASES" or
            "BATTERWALKS" or
            "WALKS" or
            "HITSRUNSRBIS" or
            "PITCHERSTRIKEOUTS" or
            "PITCHERWALKS" or
            "WALKSALLOWED" or
            "PITCHERHITSALLOWED" or
            "HITSALLOWED" or
            "PITCHEREARNEDRUNS" or
            "EARNEDRUNSALLOWED";
    }

    private sealed record RecommendationGradeability(bool IsGradeable, string? Warning);

    private static string BuildRecommendationLedgerGroupIdentity(TheOddsLineShoppingGroup group)
    {
        return string.Join("|", new[]
        {
            group.ExpectedResultDate.ToString("yyyy-MM-dd"),
            group.EventId ?? string.Empty,
            group.PlayerId.ToString("D"),
            group.PropTypeId.ToString("D"),
            group.BestSide,
            group.Line.ToString("0.####"),
            group.ProviderCategory,
            group.BestBookmaker ?? string.Empty
        });
    }

    private static MarketTopPickSnapshot BuildTopPickSnapshot(
        TheOddsRefreshRun refreshRun,
        Guid? predictionRunId,
        DateTimeOffset generatedAtUtc,
        TheOddsLineShoppingGroup group,
        TopPicksModelOptions topPicksModelOptions)
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
            ModelVersion = ResolveCurrentPublicModelVersion(topPicksModelOptions),
            RecommendationContractVersion = ResolveCurrentRecommendationContractVersion(topPicksModelOptions),
            ProbabilityModelVersion = ResolveCurrentProbabilityModelVersion(topPicksModelOptions),
            ModelEra = ResolveCurrentPublicModelEra(topPicksModelOptions),
            IsCandidate = strategy.IsCandidate,
            IsWatchlist = strategy.IsWatchlist,
            IsAvoid = strategy.IsAvoid,
            IsResearchOnly = strategy.IsResearchOnly,
            EntryProvider = entry.EntryProvider,
            EntryCompatibilityGroup = entry.EntryCompatibilityGroup,
            EntryProviderCategory = entry.EntryProviderCategory,
            EntryActionableForUser = entry.EntryActionableForUser,
            DetailsJson = BuildMarketSnapshotDetailsJson(group, includeBookmakerPrices: false, topPicksModelOptions)
        };
    }

    private static MarketLineShoppingSnapshot BuildLineShoppingSnapshot(
        TheOddsRefreshRun refreshRun,
        Guid? predictionRunId,
        DateTimeOffset generatedAtUtc,
        TheOddsLineShoppingGroup group,
        TopPicksModelOptions topPicksModelOptions)
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
            ModelVersion = ResolveCurrentPublicModelVersion(topPicksModelOptions),
            RecommendationContractVersion = ResolveCurrentRecommendationContractVersion(topPicksModelOptions),
            ProbabilityModelVersion = ResolveCurrentProbabilityModelVersion(topPicksModelOptions),
            ModelEra = ResolveCurrentPublicModelEra(topPicksModelOptions),
            IsCandidate = strategy.IsCandidate,
            IsWatchlist = strategy.IsWatchlist,
            IsAvoid = strategy.IsAvoid,
            IsResearchOnly = strategy.IsResearchOnly,
            EntryProvider = entry.EntryProvider,
            EntryCompatibilityGroup = entry.EntryCompatibilityGroup,
            EntryProviderCategory = entry.EntryProviderCategory,
            EntryActionableForUser = entry.EntryActionableForUser,
            DetailsJson = BuildMarketSnapshotDetailsJson(group, includeBookmakerPrices: true, topPicksModelOptions)
        };
    }

    private static JsonDocument BuildMarketSnapshotDetailsJson(
        TheOddsLineShoppingGroup group,
        bool includeBookmakerPrices,
        TopPicksModelOptions topPicksModelOptions)
    {
        var node = JsonSerializer.SerializeToNode(ToLineShoppingResponseGroup(group, includeBookmakerPrices)) as JsonObject ?? [];
        AddCurrentPublicModelMetadata(node, topPicksModelOptions);
        return JsonSerializer.SerializeToDocument(node);
    }

    private static void AddCurrentPublicModelMetadata(JsonObject node, TopPicksModelOptions topPicksModelOptions)
    {
        node["modelVersion"] = ResolveCurrentPublicModelVersion(topPicksModelOptions);
        node["recommendationContractVersion"] = ResolveCurrentRecommendationContractVersion(topPicksModelOptions);
        node["probabilityModelVersion"] = ResolveCurrentProbabilityModelVersion(topPicksModelOptions);
        node["modelEra"] = ResolveCurrentPublicModelEra(topPicksModelOptions);
    }

    internal static string ResolveCurrentPublicModelEra(TopPicksModelOptions topPicksModelOptions)
    {
        if (topPicksModelOptions.IsLikelyHitV1Enabled)
        {
            return string.IsNullOrWhiteSpace(topPicksModelOptions.CurrentModelEra)
                ? TopPicksModelOptions.DefaultLikelyHitV1ModelEra
                : topPicksModelOptions.CurrentModelEra.Trim();
        }

        return TopPicksModelOptions.Legacy;
    }

    private static string ResolveCurrentPublicModelVersion(TopPicksModelOptions topPicksModelOptions) =>
        topPicksModelOptions.IsLikelyHitV1Enabled ? LikelyHitModelService.ModelVersion : TopPicksModelOptions.Legacy;

    private static string? ResolveCurrentRecommendationContractVersion(TopPicksModelOptions topPicksModelOptions) =>
        topPicksModelOptions.IsLikelyHitV1Enabled ? LikelyHitModelService.RecommendationContractVersion : null;

    private static string? ResolveCurrentProbabilityModelVersion(TopPicksModelOptions topPicksModelOptions) =>
        topPicksModelOptions.IsLikelyHitV1Enabled ? LikelyHitModelService.ProbabilityModelVersion : null;

    private static async Task<MlbTopPicksReadResponse?> TryBuildTopPicksSnapshotResponseAsync(
        int? take,
        string? providerCategory,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, Guid> refreshRunIdsByCategory = string.IsNullOrWhiteSpace(providerCategory)
            ? await FindLatestTopPickSnapshotRefreshRunIdsByCategoryAsync(dbContext, cancellationToken)
            : new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var refreshRunIds = refreshRunIdsByCategory.Count > 0
            ? refreshRunIdsByCategory.Values.Distinct().ToArray()
            : await FindLatestTopPickSnapshotRefreshRunIdsAsync(
                dbContext,
                providerCategory,
                cancellationToken);
        if (refreshRunIds.Length == 0)
        {
            return null;
        }

        var requestedTake = Math.Clamp(take ?? 10, 1, 500);
        var query = dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(row => refreshRunIds.Contains(row.TheOddsRefreshRunId));
        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.ProviderCategory == providerCategory);
        }
        else if (refreshRunIdsByCategory.Count > 0)
        {
            var dfsRefreshRunId = refreshRunIdsByCategory.GetValueOrDefault("DFS");
            var sportsbookRefreshRunId = refreshRunIdsByCategory.GetValueOrDefault("Sportsbook");
            query = query.Where(row =>
                (dfsRefreshRunId != Guid.Empty && row.ProviderCategory == "DFS" && row.TheOddsRefreshRunId == dfsRefreshRunId) ||
                (sportsbookRefreshRunId != Guid.Empty && row.ProviderCategory == "Sportsbook" && row.TheOddsRefreshRunId == sportsbookRefreshRunId));
        }

        var rows = await query
            .OrderByDescending(row => row.BestEstimatedEv)
            .ThenBy(row => row.EntryActionableForUser ? 0 : 1)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var refreshRuns = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => refreshRunIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        var latestRefreshRun = refreshRuns
            .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
            .ThenByDescending(row => row.StartedAtUtc)
            .First();
        var playerIdsBySnapshotId = await ResolveTopPickSnapshotPlayerIdsAsync(dbContext, rows, cancellationToken);
        var normalizedRows = rows
            .Select(row => BuildSnapshotDetailsJsonObject(row.DetailsJson.RootElement, playerIdsBySnapshotId.GetValueOrDefault(row.Id)))
            .Where(IsSafePublicTopPickSnapshotRow)
            .OrderByDescending(row => GetDecimal(row, "displayExpectedValue") ?? decimal.MinValue)
            .ThenByDescending(row => GetDecimal(row, "selectedSideProbability") ?? decimal.MinValue)
            .ThenByDescending(row => GetDecimal(row, "rankingScore") ?? decimal.MinValue)
            .Take(requestedTake)
            .ToArray();

        return new MlbTopPicksReadResponse(
            rows.FirstOrDefault()?.GeneratedAtUtc ?? latestRefreshRun.CompletedAtUtc ?? latestRefreshRun.StartedAtUtc,
            SnapshotBacked: true,
            RefreshRunId: refreshRunIds.Length == 1 ? refreshRunIds[0] : null,
            FallbackReason: null,
            Diagnostics: BuildSnapshotDiagnostics(normalizedRows.Length, providerCategory),
            Run: new { runId = rows.FirstOrDefault()?.PredictionRunId, refreshRunIds, predictionCount = refreshRuns.Sum(row => row.PredictionCount) },
            Summary: BuildTopPickSnapshotSummary(normalizedRows),
            StrategyClassificationSummary: BuildTopPickSnapshotStrategySummary(normalizedRows),
            TopPicks: normalizedRows);
    }

    private static string? NormalizePublicProviderCategoryFilter(string? providerCategory)
    {
        if (string.IsNullOrWhiteSpace(providerCategory)
            || string.Equals(providerCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(providerCategory, "DFS", StringComparison.OrdinalIgnoreCase))
        {
            return "DFS";
        }

        if (string.Equals(providerCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return "Sportsbook";
        }

        return providerCategory.Trim();
    }

    private static async Task<MlbLineShoppingReadResponse?> TryBuildLineShoppingSnapshotResponseAsync(
        bool topOnly,
        int take,
        bool includeBookmakerPrices,
        string? providerCategory,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, Guid> refreshRunIdsByCategory = string.IsNullOrWhiteSpace(providerCategory)
            ? await FindLatestLineShoppingSnapshotRefreshRunIdsByCategoryAsync(dbContext, cancellationToken)
            : new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var refreshRunIds = refreshRunIdsByCategory.Count > 0
            ? refreshRunIdsByCategory.Values.Distinct().ToArray()
            : await FindLatestLineShoppingSnapshotRefreshRunIdsAsync(
                dbContext,
                providerCategory,
                cancellationToken);
        if (refreshRunIds.Length == 0)
        {
            return null;
        }

        var requestedTake = Math.Clamp(take, 1, 500);
        var query = dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => refreshRunIds.Contains(row.TheOddsRefreshRunId));
        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.ProviderCategory == providerCategory);
        }
        else if (refreshRunIdsByCategory.Count > 0)
        {
            var dfsRefreshRunId = refreshRunIdsByCategory.GetValueOrDefault("DFS");
            var sportsbookRefreshRunId = refreshRunIdsByCategory.GetValueOrDefault("Sportsbook");
            query = query.Where(row =>
                (dfsRefreshRunId != Guid.Empty && row.ProviderCategory == "DFS" && row.TheOddsRefreshRunId == dfsRefreshRunId) ||
                (sportsbookRefreshRunId != Guid.Empty && row.ProviderCategory == "Sportsbook" && row.TheOddsRefreshRunId == sportsbookRefreshRunId));
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

        var refreshRuns = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => refreshRunIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        var latestRefreshRun = refreshRuns
            .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
            .ThenByDescending(row => row.StartedAtUtc)
            .First();
        var playerIdsBySnapshotId = await ResolveLineShoppingSnapshotPlayerIdsAsync(dbContext, rows, cancellationToken);

        return new MlbLineShoppingReadResponse(
            rows.FirstOrDefault()?.GeneratedAtUtc ?? latestRefreshRun.CompletedAtUtc ?? latestRefreshRun.StartedAtUtc,
            SnapshotBacked: true,
            RefreshRunId: refreshRunIds.Length == 1 ? refreshRunIds[0] : null,
            FallbackReason: null,
            Diagnostics: BuildSnapshotDiagnostics(rows.Count, providerCategory, includeBookmakerPrices),
            Run: new { runId = rows.FirstOrDefault()?.PredictionRunId, refreshRunIds, predictionCount = refreshRuns.Sum(row => row.PredictionCount) },
            Summary: BuildSnapshotSummary(rows),
            StrategyClassificationSummary: BuildSnapshotStrategySummary(rows),
            ComparisonGroups: rows
                .Select(row => BuildSnapshotDetailsWithPlayerId(includeBookmakerPrices
                    ? row.DetailsJson.RootElement.Clone()
                    : StripBookmakerPrices(row.DetailsJson), playerIdsBySnapshotId.GetValueOrDefault(row.Id)))
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

    private static async Task<Guid[]> FindLatestTopPickSnapshotRefreshRunIdsAsync(
        ApplicationDbContext dbContext,
        string? providerCategory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            var refreshRunId = await FindLatestTopPickSnapshotRefreshRunIdAsync(dbContext, providerCategory, cancellationToken);
            return refreshRunId.HasValue ? [refreshRunId.Value] : [];
        }

        var dfsRefreshRunId = await FindLatestTopPickSnapshotRefreshRunIdAsync(dbContext, "DFS", cancellationToken);
        var sportsbookRefreshRunId = await FindLatestTopPickSnapshotRefreshRunIdAsync(dbContext, "Sportsbook", cancellationToken);

        return new[] { dfsRefreshRunId, sportsbookRefreshRunId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<string, Guid>> FindLatestTopPickSnapshotRefreshRunIdsByCategoryAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var providerCategory in new[] { "DFS", "Sportsbook" })
        {
            var refreshRunId = await FindLatestTopPickSnapshotRefreshRunIdAsync(dbContext, providerCategory, cancellationToken);
            if (refreshRunId.HasValue)
            {
                result[providerCategory] = refreshRunId.Value;
            }
        }

        return result;
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

    private static async Task<Guid[]> FindLatestLineShoppingSnapshotRefreshRunIdsAsync(
        ApplicationDbContext dbContext,
        string? providerCategory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            var refreshRunId = await FindLatestLineShoppingSnapshotRefreshRunIdAsync(dbContext, providerCategory, cancellationToken);
            return refreshRunId.HasValue ? [refreshRunId.Value] : [];
        }

        var dfsRefreshRunId = await FindLatestLineShoppingSnapshotRefreshRunIdAsync(dbContext, "DFS", cancellationToken);
        var sportsbookRefreshRunId = await FindLatestLineShoppingSnapshotRefreshRunIdAsync(dbContext, "Sportsbook", cancellationToken);

        return new[] { dfsRefreshRunId, sportsbookRefreshRunId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<string, Guid>> FindLatestLineShoppingSnapshotRefreshRunIdsByCategoryAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var providerCategory in new[] { "DFS", "Sportsbook" })
        {
            var refreshRunId = await FindLatestLineShoppingSnapshotRefreshRunIdAsync(dbContext, providerCategory, cancellationToken);
            if (refreshRunId.HasValue)
            {
                result[providerCategory] = refreshRunId.Value;
            }
        }

        return result;
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
            dfsCandidateCount = rows.Count(row => row.IsCandidate && string.Equals(row.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
            modelEraBreakdown = rows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.ModelEra) ? "LegacyOrUnknown" : row.ModelEra, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { modelEra = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.modelEra)
                .ToArray()
        };
    }

    private static object BuildTopPickSnapshotSummary(IReadOnlyCollection<JsonObject> rows)
    {
        return new
        {
            totalSnapshotRowsReturned = rows.Count,
            returnedTopPicks = rows.Count,
            activeCandidateCount = rows.Count(row => GetBool(row, "isCandidate") == true),
            actionableCandidateCount = rows.Count(row => GetBool(row, "isCandidate") == true && GetBool(row, "entryActionableForUser") == true),
            sportsbookContextCandidateCount = rows.Count(row => GetBool(row, "isCandidate") == true && GetBool(row, "entryActionableForUser") != true),
            dfsCandidateCount = rows.Count(row => GetBool(row, "isCandidate") == true && string.Equals(GetString(row, "providerCategory"), "DFS", StringComparison.OrdinalIgnoreCase)),
            modelEra = rows
                .Select(row => GetString(row, "modelEra"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key)
                .FirstOrDefault(),
            modelEraBreakdown = rows
                .GroupBy(row => GetString(row, "modelEra") ?? "LegacyOrUnknown", StringComparer.OrdinalIgnoreCase)
                .Select(group => new { modelEra = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.modelEra)
                .ToArray()
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

    private static object[] BuildTopPickSnapshotStrategySummary(IReadOnlyCollection<JsonObject> rows)
    {
        return rows
            .GroupBy(row => new
            {
                StrategyClassification = GetString(row, "strategyClassification") ?? string.Empty,
                StrategyTier = GetString(row, "strategyTier") ?? string.Empty
            })
            .Select(group => new
            {
                classification = group.Key.StrategyClassification,
                tier = group.Key.StrategyTier,
                count = group.Count(),
                averageBestEstimatedEv = group.Select(row => GetDecimal(row, "displayExpectedValue", "bestEstimatedEv")).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty().Average(),
                averageBestProbability = group.Select(row => GetDecimal(row, "selectedSideProbability", "bestProbability")).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty().Average(),
                averageBestPrice = group.Select(row => GetDecimal(row, "bestDecimalPrice")).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty().Average()
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

    private static object BuildSnapshotDetailsWithPlayerId(JsonElement detailsJson, Guid? playerId)
    {
        return BuildSnapshotDetailsJsonObject(detailsJson, playerId);
    }

    private static JsonObject BuildSnapshotDetailsJsonObject(JsonElement detailsJson, Guid? playerId)
    {
        var node = JsonSerializer.SerializeToNode(detailsJson);
        if (node is not JsonObject obj)
        {
            return new JsonObject();
        }

        ApplySportsbookSnapshotEvSafety(obj);
        AddExpectedValueDisplayMetadata(obj);
        AddModelProbabilityDisplayMetadata(obj);
        AddRecommendationDisplayMetadata(obj);
        NormalizePublicDetailsJsonCasing(obj);

        if (playerId.HasValue && playerId.Value != Guid.Empty)
        {
            obj["playerId"] = playerId.Value;
            obj["canonicalPlayerId"] = playerId.Value;
        }

        return obj;
    }

    private static bool IsSafePublicTopPickSnapshotRow(JsonObject obj)
    {
        if (GetBool(obj, "isCandidate") != true || GetBool(obj, "isRecommendation") != true)
        {
            return false;
        }

        if (string.Equals(GetString(obj, "providerCategory"), "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            var displayExpectedValue = GetDecimal(obj, "displayExpectedValue");
            return displayExpectedValue is > 0m and <= SportsbookRecommendationEvHardSuppressThreshold
                && string.IsNullOrWhiteSpace(GetString(obj, "evSuppressedReason"));
        }

        return true;
    }

    private static void ApplySportsbookSnapshotEvSafety(JsonObject obj)
    {
        var providerCategory = GetString(obj, "providerCategory", "ProviderCategory");
        if (!string.Equals(providerCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var rawSelectedSideProbability = GetDecimal(
            obj,
            "rawSelectedSideProbability",
            "RawSelectedSideProbability",
            "selectedSideProbability",
            "SelectedSideProbability",
            "bestProbability",
            "BestProbability");
        var impliedProbability = GetDecimal(obj, "bestImpliedProbability", "BestImpliedProbability");
        var decimalPrice = GetDecimal(obj, "bestDecimalPrice", "BestDecimalPrice")
            ?? ConvertAmericanToDecimal(GetInt32(obj, "bestAmericanPrice", "BestAmericanPrice"));
        if (!impliedProbability.HasValue && decimalPrice is > 0m)
        {
            impliedProbability = Math.Round(1m / decimalPrice.Value, 6);
        }

        var historicalSampleSize = GetInt32(obj, "historicalSampleSize", "HistoricalSampleSize", "playerSampleSize", "PlayerSampleSize") ?? 0;
        var sampleWarning = GetString(obj, "sampleWarning", "SampleWarning") ?? "None";
        var confidenceScore = GetDecimal(obj, "confidenceScore", "ConfidenceScore") ?? 0m;
        var calibration = BuildSportsbookExpectedValueCalibration(
            providerCategory!,
            rawSelectedSideProbability,
            impliedProbability,
            decimalPrice,
            historicalSampleSize,
            sampleWarning,
            confidenceScore);

        obj["rawSelectedSideProbability"] = calibration.RawSelectedSideProbability;
        obj["calibratedSelectedSideProbability"] = calibration.CalibratedSelectedSideProbability;
        obj["selectedSideProbability"] = calibration.SelectedSideProbability;
        obj["bestProbability"] = calibration.SelectedSideProbability;
        obj["probabilityCalibrationMethod"] = calibration.ProbabilityCalibrationMethod;
        obj["probabilityCalibrationReason"] = calibration.ProbabilityCalibrationReason;
        obj["rawExpectedValue"] = calibration.RawExpectedValue;
        obj["calibratedExpectedValue"] = calibration.CalibratedExpectedValue;
        obj["bestEstimatedEv"] = calibration.DisplayExpectedValue;
        obj["evCalibrationMethod"] = calibration.EvCalibrationMethod;
        obj["evSuppressedReason"] = calibration.EvSuppressedReason;

        if (calibration.SelectedSideProbability.HasValue && impliedProbability.HasValue)
        {
            obj["bestProbabilityEdge"] = Math.Round(calibration.SelectedSideProbability.Value - impliedProbability.Value, 6);
        }

        var shouldSuppressRecommendation = calibration.DisplayExpectedValue is not > 0m
            || calibration.DisplayExpectedValue > SportsbookRecommendationEvHardSuppressThreshold
            || !string.IsNullOrWhiteSpace(calibration.EvSuppressedReason);
        if (!shouldSuppressRecommendation)
        {
            return;
        }

        obj["isCandidate"] = false;
        obj["isRecommendation"] = false;
        obj["recommendationActionableForUser"] = false;
        obj["recommendationType"] = null;
        obj["strategyClassification"] = "Research";
        obj["strategyTier"] = "SportsbookEvSuppressed";
        obj["isResearchOnly"] = true;
    }

    private static void NormalizePublicDetailsJsonCasing(JsonObject obj)
    {
        var pascalKeys = obj
            .Select(pair => pair.Key)
            .Where(key => key.Length > 0 && char.IsUpper(key[0]))
            .ToArray();

        foreach (var pascalKey in pascalKeys)
        {
            var camelKey = char.ToLowerInvariant(pascalKey[0]) + pascalKey[1..];
            if (!obj.ContainsKey(camelKey) && obj.TryGetPropertyValue(pascalKey, out var value))
            {
                obj[camelKey] = value?.DeepClone();
            }

            obj.Remove(pascalKey);
        }
    }

    private static void AddRecommendationDisplayMetadata(JsonObject obj)
    {
        var providerCategory = GetString(obj, "providerCategory", "ProviderCategory");
        var isCandidate = GetBool(obj, "isCandidate", "IsCandidate") ?? false;

        if (!obj.ContainsKey("isRecommendation"))
        {
            obj["isRecommendation"] = isCandidate;
        }

        if (!obj.ContainsKey("recommendationActionableForUser"))
        {
            obj["recommendationActionableForUser"] = isCandidate;
        }

        if (!obj.ContainsKey("recommendationType"))
        {
            obj["recommendationType"] = isCandidate
                ? string.Equals(providerCategory, "DFS", StringComparison.OrdinalIgnoreCase) ? "DFS" : "Sportsbook"
                : null;
        }

        if (isCandidate
            && string.Equals(providerCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
            && !obj.ContainsKey("entryRuleWarning"))
        {
            obj["entryRuleWarning"] = "MLB sportsbook recommendation. Entry Builder is available only for DFS entries.";
        }
    }

    private static void AddExpectedValueDisplayMetadata(JsonObject obj)
    {
        var providerCategory = GetString(obj, "providerCategory", "ProviderCategory");
        var estimatedEv = GetDecimal(obj, "bestEstimatedEv", "BestEstimatedEv");
        var impliedProbability = GetDecimal(obj, "bestImpliedProbability", "BestImpliedProbability");
        var decimalPrice = GetDecimal(obj, "bestDecimalPrice", "BestDecimalPrice");
        var americanPrice = GetInt32(obj, "bestAmericanPrice", "BestAmericanPrice");
        var expectedValueDisplay = BuildExpectedValueDisplayMetadata(
            providerCategory,
            estimatedEv,
            impliedProbability,
            decimalPrice,
            americanPrice);

        obj["isDfs"] = expectedValueDisplay.IsDfs;
        obj["isSportsbook"] = expectedValueDisplay.IsSportsbook;
        obj["showExpectedValue"] = expectedValueDisplay.ShowExpectedValue;
        obj["displayExpectedValue"] = expectedValueDisplay.DisplayExpectedValue;
        obj["expectedValueUnavailableReason"] = GetString(obj, "evSuppressedReason", "EvSuppressedReason")
            ?? expectedValueDisplay.ExpectedValueUnavailableReason;
    }

    private static void AddModelProbabilityDisplayMetadata(JsonObject obj)
    {
        var bestProbability = GetDecimal(obj, "selectedSideProbability", "SelectedSideProbability", "bestProbability", "BestProbability");
        var modelOverProbability = GetDecimal(obj, "modelOverProbability", "ModelOverProbability", "modelProbabilityOver", "ModelProbabilityOver");
        var modelUnderProbability = GetDecimal(obj, "modelUnderProbability", "ModelUnderProbability", "modelProbabilityUnder", "ModelProbabilityUnder");
        var historicalSampleSize = GetInt32(obj, "historicalSampleSize", "HistoricalSampleSize") ?? 0;
        var probabilitySource = GetString(obj, "probabilitySource", "ProbabilitySource", "probabilityMethod", "ProbabilityMethod");

        obj["selectedSideProbability"] = bestProbability;
        obj["modelProbabilityAvailable"] = bestProbability.HasValue;
        obj["hitChanceUnavailableReason"] = bestProbability.HasValue
            ? null
            : historicalSampleSize > 0
                ? "HistoricalDataAvailableButModelProbabilityMissing"
                : "Model hit chance is unavailable for this row. Historical hit rate is shown in the chart.";
        obj["modelOverProbability"] = modelOverProbability;
        obj["modelUnderProbability"] = modelUnderProbability;
        obj["probabilitySource"] = bestProbability.HasValue ? probabilitySource : null;
    }

    private static string? GetString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetPropertyValue(key, out var value) &&
                value is not null &&
                value.GetValueKind() == JsonValueKind.String)
            {
                return value.GetValue<string>();
            }
        }

        return null;
    }

    private static bool? GetBool(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!obj.TryGetPropertyValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value.GetValueKind() is JsonValueKind.True or JsonValueKind.False &&
                value.AsValue().TryGetValue<bool>(out var boolean))
            {
                return boolean;
            }
        }

        return null;
    }

    private static decimal? GetDecimal(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!obj.TryGetPropertyValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value.GetValueKind() == JsonValueKind.Number &&
                value.AsValue().TryGetValue<decimal>(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static int? GetInt32(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!obj.TryGetPropertyValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value.GetValueKind() == JsonValueKind.Number &&
                value.AsValue().TryGetValue<int>(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static Task<IReadOnlyDictionary<Guid, Guid?>> ResolveTopPickSnapshotPlayerIdsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<MarketTopPickSnapshot> rows,
        CancellationToken cancellationToken)
    {
        return ResolveSnapshotPlayerIdsAsync(
            dbContext,
            rows.Select(row => new SnapshotPlayerLookup(row.Id, row.Player)).ToArray(),
            cancellationToken);
    }

    private static Task<IReadOnlyDictionary<Guid, Guid?>> ResolveLineShoppingSnapshotPlayerIdsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<MarketLineShoppingSnapshot> rows,
        CancellationToken cancellationToken)
    {
        return ResolveSnapshotPlayerIdsAsync(
            dbContext,
            rows.Select(row => new SnapshotPlayerLookup(row.Id, row.Player)).ToArray(),
            cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<Guid, Guid?>> ResolveSnapshotPlayerIdsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<SnapshotPlayerLookup> snapshots,
        CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return new Dictionary<Guid, Guid?>();
        }

        var requestedNames = snapshots
            .Select(row => NormalizeSnapshotLookupText(row.Player))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requestedNames.Count == 0)
        {
            return snapshots.ToDictionary(row => row.SnapshotId, _ => (Guid?)null);
        }

        var playerRows = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.League != null && player.League.Code == "MLB")
            .Select(player => new { player.Id, player.DisplayName })
            .ToArrayAsync(cancellationToken);
        var candidatePlayerIds = playerRows
            .Where(player => requestedNames.Contains(NormalizeSnapshotLookupText(player.DisplayName)))
            .Select(player => player.Id)
            .Distinct()
            .ToArray();
        var resultCountsByPlayerId = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => candidatePlayerIds.Contains(result.PlayerId) && result.ExternalSource == "mlb")
            .GroupBy(result => result.PlayerId)
            .Select(group => new { PlayerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
        var playersByName = playerRows
            .GroupBy(player => NormalizeSnapshotLookupText(player.DisplayName), StringComparer.OrdinalIgnoreCase)
            .Where(group => requestedNames.Contains(group.Key))
            .ToDictionary(
                group => group.Key,
                group => ResolveSnapshotPlayerId(group.Select(player => player.Id), resultCountsByPlayerId),
                StringComparer.OrdinalIgnoreCase);

        return snapshots.ToDictionary(
            row => row.SnapshotId,
            row => playersByName.GetValueOrDefault(NormalizeSnapshotLookupText(row.Player)));
    }

    private static Guid? ResolveSnapshotPlayerId(
        IEnumerable<Guid> playerIds,
        IReadOnlyDictionary<Guid, int> resultCountsByPlayerId)
    {
        var ranked = playerIds
            .Select(playerId => new { PlayerId = playerId, ResultCount = resultCountsByPlayerId.GetValueOrDefault(playerId) })
            .OrderByDescending(row => row.ResultCount)
            .ToArray();
        if (ranked.Length == 0)
        {
            return null;
        }

        if (ranked.Length == 1 || ranked[0].ResultCount > ranked[1].ResultCount)
        {
            return ranked[0].ResultCount > 0 ? ranked[0].PlayerId : null;
        }

        return null;
    }

    private static string NormalizeSnapshotLookupText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));
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

    private static void AddTimingStage(
        ICollection<PublicMarketReadTimingStage> stages,
        Stopwatch stopwatch,
        string name,
        int? rowCount)
    {
        stages.Add(new PublicMarketReadTimingStage(name, stopwatch.ElapsedMilliseconds, rowCount));
        stopwatch.Restart();
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

    internal static async Task<SoccerWorldCupProviderContextCounts> GetSoccerWorldCupProviderContextCountsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var platformProps = await LoadSoccerWorldCupSportsbookPlatformPropsAsync(
            dbContext,
            providerCategory: null,
            cancellationToken);
        var activeRows = platformProps.Count(row => string.Equals(row.Status, "active", StringComparison.OrdinalIgnoreCase));

        return new SoccerWorldCupProviderContextCounts(
            PlatformRowCount: platformProps.Count,
            ActivePlatformRowCount: activeRows,
            ProviderEventCount: platformProps
                .Select(row => row.EventId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            BookmakerCount: platformProps
                .Select(row => NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    private static async Task<List<TheOddsPersistedPlatformPropRow>> LoadSoccerWorldCupSportsbookPlatformPropsAsync(
        ApplicationDbContext dbContext,
        string? providerCategory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerCategory)
            && !string.Equals(providerCategory, "All", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(providerCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var rows = await LoadSoccerWorldCupSportsbookPlatformPropDataRowsAsync(dbContext, cancellationToken);
        var platformProps = ParseTheOddsPlatformProps(rows);

        return platformProps
            .Where(IsRealSoccerWorldCupSportsbookContextRow)
            .ToList();
    }

    private static async Task<List<TheOddsPlatformPropDataRow>> LoadSoccerWorldCupSportsbookPlatformPropDataRowsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!IsNpgsqlProvider(dbContext))
        {
            var rows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            return rows
                .Where(row =>
                {
                    var parsed = ParseTheOddsPlatformProps([row]).FirstOrDefault();
                    return parsed is not null && IsRealSoccerWorldCupSportsbookContextRow(parsed);
                })
                .ToList();
        }

        using var filter = JsonSerializer.SerializeToDocument(new
        {
            dataProvider = "The Odds API",
            contextSchemaVersion = "the-odds-provider-context-v2",
            providerCategory = "Sportsbook",
            sport = new
            {
                code = "SOCCER",
                competition = "World Cup",
                providerSportKey = "soccer_fifa_world_cup"
            }
        });

        return await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Metadata != null
                && EF.Functions.JsonContains(platformProp.Metadata, filter))
            .Select(platformProp => new TheOddsPlatformPropDataRow(
                platformProp.Id,
                platformProp.PropId,
                platformProp.PlatformPropId,
                platformProp.Label,
                platformProp.CurrentLine,
                platformProp.OverAvailable,
                platformProp.UnderAvailable,
                platformProp.PayoutType,
                platformProp.Status,
                platformProp.ObservedAt,
                platformProp.RawPayloadId,
                platformProp.Platform == null ? "Unknown" : platformProp.Platform.Name,
                platformProp.Platform == null ? "unknown" : platformProp.Platform.Slug,
                platformProp.Prop == null ? Guid.Empty : platformProp.Prop.PlayerId,
                platformProp.Prop == null ? Guid.Empty : platformProp.Prop.PropTypeId,
                platformProp.Prop == null || platformProp.Prop.Player == null ? "Unknown" : platformProp.Prop.Player.DisplayName,
                platformProp.Prop == null || platformProp.Prop.PropType == null ? "Unknown" : platformProp.Prop.PropType.Name,
                platformProp.Metadata))
            .ToListAsync(cancellationToken);
    }

    private static bool IsRealSoccerWorldCupSportsbookContextRow(TheOddsPersistedPlatformPropRow row)
    {
        return row.HasTheOddsDataProvider
            && string.Equals(row.ContextSchemaVersion, "the-odds-provider-context-v2", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.SportCode, "SOCCER", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.Competition, "World Cup", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.ProviderSportKey, "soccer_fifa_world_cup", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
            && !IsSyntheticSoccerProviderEventId(row.EventId);
    }

    private static bool IsSyntheticSoccerProviderEventId(string? eventId)
    {
        return string.IsNullOrWhiteSpace(eventId)
            || eventId.Contains("|balanced", StringComparison.OrdinalIgnoreCase)
            || eventId.Contains("|alternate", StringComparison.OrdinalIgnoreCase)
            || eventId.Contains("SCR_game", StringComparison.OrdinalIgnoreCase);
    }

    private static List<TheOddsLineShoppingSourceRow> BuildTheOddsResearchOnlyLineShoppingRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps)
    {
        return platformProps
            .Select(row =>
            {
                var eventStartUtc = row.EventStartTime?.ToUniversalTime();
                var eventDate = DateOnly.FromDateTime((eventStartUtc ?? row.ObservedAt.ToUniversalTime()).DateTime);
                var overImpliedProbability = CalculateImpliedProbability(row.PriceOverDecimal);
                var underImpliedProbability = CalculateImpliedProbability(row.PriceUnderDecimal);

                return new TheOddsLineShoppingSourceRow(
                    row.EventId,
                    row.EventStartTime,
                    row.HomeTeam,
                    row.AwayTeam,
                    row.PlayerName,
                    row.PropType,
                    row.CurrentLine,
                    row.MarketClassification,
                    row.MarketKey,
                    row.ProjectionMetadata.ProjectionType,
                    row.ProjectionMetadata.ProjectionTypeConfidence,
                    row.ProjectionMetadata.IsAlternateMarket,
                    row.ProjectionMetadata.PayoutMultiplierMayDiffer,
                    row.ProjectionMetadata.SpecialProjectionWarning,
                    "Sportsbook",
                    row.Region,
                    IsActionableForUser: false,
                    row.IsIndicativeOdds,
                    IsEligibleAsNormalRecommendation: false,
                    ModelProbabilityOver: null,
                    ModelProbabilityUnder: null,
                    ModelProbabilityPush: null,
                    ModelProjection: null,
                    ProjectionEdge: null,
                    RecentWeightedProjection: null,
                    RecentWeightedProjectionEdge: null,
                    RankingScore: 0m,
                    row.PlatformName,
                    NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                    row.PlatformPropId,
                    row.ObservedAt,
                    row.OverAvailable,
                    row.UnderAvailable,
                    row.PriceOver,
                    row.PriceOverDecimal,
                    row.PriceUnder,
                    row.PriceUnderDecimal,
                    overImpliedProbability,
                    underImpliedProbability,
                    OverProbabilityEdge: null,
                    UnderProbabilityEdge: null,
                    OverEstimatedEv: null,
                    UnderEstimatedEv: null,
                    row.PlayerId,
                    row.PropTypeId,
                    eventDate,
                    eventStartUtc,
                    eventDate,
                    "SoccerWorldCupProviderContext",
                    eventDate,
                    ResolvedMlbDate: null,
                    HistoricalSampleSize: 0,
                    ProbabilityMethod: "Unavailable",
                    ConfidenceScore: 0m,
                    RawPlayerEmpiricalOverRate: null,
                    RawPlayerEmpiricalUnderRate: null,
                    RawPriorOverRate: null,
                    RawPriorUnderRate: null,
                    PriorSampleWeight: 0,
                    EffectiveShrinkageWeight: 0m);
            })
            .ToList();
    }

    private static List<TheOddsLineShoppingSourceRow> ApplySoccerWorldCupPredictions(
        IReadOnlyCollection<TheOddsLineShoppingSourceRow> rows,
        SoccerWorldCupPredictionBatch predictionBatch)
    {
        if (!predictionBatch.Enabled || predictionBatch.PredictionsByKey.Count == 0)
        {
            return rows.ToList();
        }

        return rows
            .Select(row =>
            {
                var key = new SoccerWorldCupPredictionKey(row.PlayerId, row.PropTypeId, row.Line);
                if (!predictionBatch.PredictionsByKey.TryGetValue(key, out var prediction))
                {
                    return row;
                }

                return row with
                {
                    IsEligibleAsNormalRecommendation = prediction.TopPickEligible,
                    ModelProbabilityOver = prediction.ModelProbabilityAvailable ? prediction.ModelProbabilityOver : null,
                    ModelProbabilityUnder = prediction.ModelProbabilityAvailable ? prediction.ModelProbabilityUnder : null,
                    ModelProbabilityPush = prediction.ModelProbabilityAvailable ? prediction.ModelProbabilityPush : null,
                    ModelProjection = prediction.ModelProjection,
                    ProjectionEdge = prediction.ModelProbabilityAvailable ? prediction.ProjectionEdge : null,
                    RankingScore = prediction.ModelProbabilityAvailable ? prediction.SelectedSideProbability ?? 0m : 0m,
                    OverProbabilityEdge = prediction.ModelProbabilityAvailable && prediction.ModelProbabilityOver.HasValue ? prediction.ModelProbabilityOver.Value - 0.5m : null,
                    UnderProbabilityEdge = prediction.ModelProbabilityAvailable && prediction.ModelProbabilityUnder.HasValue ? prediction.ModelProbabilityUnder.Value - 0.5m : null,
                    OverEstimatedEv = prediction.ModelProbabilityAvailable ? prediction.OverEstimatedEv : null,
                    UnderEstimatedEv = prediction.ModelProbabilityAvailable ? prediction.UnderEstimatedEv : null,
                    HistoricalSampleSize = prediction.HistoricalSampleSize,
                    ProbabilityMethod = prediction.ProbabilityMethod,
                    ConfidenceScore = prediction.ModelProbabilityAvailable ? prediction.SelectedSideProbability ?? 0m : 0m
                };
            })
            .ToList();
    }

    private static SoccerWorldCupPredictionInput ToSoccerWorldCupPredictionInput(TheOddsLineShoppingSourceRow row)
    {
        return new SoccerWorldCupPredictionInput(
            row.PlayerId,
            row.PropTypeId,
            row.Player,
            row.PropType,
            row.MarketKey,
            row.Line,
            row.OverAvailable,
            row.UnderAvailable,
            row.EventDate,
            row.OverAmericanPrice,
            row.OverDecimalPrice,
            row.UnderAmericanPrice,
            row.UnderDecimalPrice);
    }

    private static async Task<SoccerWorldCupPlayerHistoryEnrichment> BuildSoccerWorldCupPlayerHistoryEnrichmentAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<TheOddsLineShoppingGroup> groups,
        CancellationToken cancellationToken)
    {
        var playerIds = groups
            .Select(group => group.PlayerId)
            .Where(playerId => playerId != Guid.Empty)
            .Distinct()
            .ToArray();
        var summaries = playerIds.ToDictionary(
            playerId => playerId,
            _ => BuildUnavailableSoccerPlayerHistorySummary("No approved API-Sports player link found for this Soccer platform player."));
        if (playerIds.Length == 0)
        {
            return new SoccerWorldCupPlayerHistoryEnrichment(
                summaries,
                RowsWithPlayerHistory: 0,
                RowsWithoutPlayerHistory: groups.Count,
                UniqueLinkedProviderPlayers: 0,
                HistoryStatsIncluded: []);
        }

        var approvedLinks = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == ApiSportsFootballPlayerRefProvider
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved"
                && link.CanonicalEntityId.HasValue
                && playerIds.Contains(link.CanonicalEntityId.Value))
            .Select(link => new
            {
                PlatformPlayerId = link.CanonicalEntityId!.Value,
                ProviderPlayerId = link.ProviderEntityId,
                link.ProviderEntityName,
                link.ProviderTeamName,
                link.ProviderCompetitionName,
                link.Confidence
            })
            .ToArrayAsync(cancellationToken);
        var providerIdsByPlatformPlayerId = approvedLinks
            .GroupBy(link => link.PlatformPlayerId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(link => link.ProviderPlayerId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order()
                    .ToArray());
        var providerPlayerIds = approvedLinks
            .Select(link => link.ProviderPlayerId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (providerPlayerIds.Length == 0)
        {
            return BuildSoccerWorldCupPlayerHistoryEnrichmentResult(groups, summaries, [], []);
        }

        var refs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == ApiSportsFootballPlayerRefProvider
                && providerPlayerIds.Contains(reference.ProviderPlayerId))
            .Select(reference => new
            {
                reference.PlayerId,
                reference.ProviderPlayerId
            })
            .ToArrayAsync(cancellationToken);
        var providerIdByHistoricalPlayerId = refs
            .GroupBy(reference => reference.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => group.First().ProviderPlayerId);
        var historicalPlayerIds = providerIdByHistoricalPlayerId.Keys.ToArray();
        if (historicalPlayerIds.Length == 0)
        {
            foreach (var link in approvedLinks)
            {
                summaries[link.PlatformPlayerId] = BuildUnavailableSoccerPlayerHistorySummary(
                    "Approved API-Sports player link exists, but no persisted historical player ref was found.",
                    link.ProviderPlayerId);
            }

            return BuildSoccerWorldCupPlayerHistoryEnrichmentResult(groups, summaries, providerPlayerIds, []);
        }

        var statKeysOfInterest = new[] { "GOALS", "ASSISTS", "SHOTS", "SHOTS_ON_TARGET" };
        var resultRows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.League)
            .Include(result => result.PropType)
            .Where(result => historicalPlayerIds.Contains(result.PlayerId)
                && result.ExternalSource == ApiSportsFootballResultSource
                && result.PropType != null
                && statKeysOfInterest.Contains(result.PropType.StatKey))
            .Select(result => new SoccerWorldCupHistoryResultRow(
                result.PlayerId,
                result.EventDate,
                result.ActualResultValue,
                result.PropType!.Name,
                result.PropType.StatKey,
                result.League == null ? null : result.League.Name,
                result.League == null ? null : result.League.Code,
                result.ExternalSourceIdentifier))
            .ToArrayAsync(cancellationToken);
        var rowsByProviderPlayerId = resultRows
            .Where(row => providerIdByHistoricalPlayerId.ContainsKey(row.PlayerId))
            .GroupBy(row => providerIdByHistoricalPlayerId[row.PlayerId], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var playerId in playerIds)
        {
            if (!providerIdsByPlatformPlayerId.TryGetValue(playerId, out var linkedProviderPlayerIds) ||
                linkedProviderPlayerIds.Length == 0)
            {
                continue;
            }

            var linkedRows = linkedProviderPlayerIds
                .SelectMany(providerId => rowsByProviderPlayerId.GetValueOrDefault(providerId) ?? [])
                .ToArray();
            if (linkedRows.Length == 0)
            {
                summaries[playerId] = BuildUnavailableSoccerPlayerHistorySummary(
                    "Approved API-Sports player link exists, but no persisted historical rows were found.",
                    linkedProviderPlayerIds[0]);
                continue;
            }

            summaries[playerId] = BuildAvailableSoccerPlayerHistorySummary(linkedProviderPlayerIds[0], linkedRows);
        }

        return BuildSoccerWorldCupPlayerHistoryEnrichmentResult(
            groups,
            summaries,
            providerPlayerIds,
            resultRows
                .Select(row => row.StatKey)
                .Where(statKey => !string.IsNullOrWhiteSpace(statKey))
                .Select(statKey => statKey!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToArray());
    }

    private static SoccerWorldCupPlayerHistoryEnrichment BuildSoccerWorldCupPlayerHistoryEnrichmentResult(
        IReadOnlyCollection<TheOddsLineShoppingGroup> groups,
        IReadOnlyDictionary<Guid, JsonObject> summaries,
        IReadOnlyCollection<string> providerPlayerIds,
        IReadOnlyCollection<string> statKeys)
    {
        var rowsWithHistory = groups.Count(group =>
            summaries.TryGetValue(group.PlayerId, out var summary) &&
            summary.TryGetPropertyValue("available", out var availableNode) &&
            availableNode?.GetValue<bool>() == true);

        return new SoccerWorldCupPlayerHistoryEnrichment(
            summaries,
            rowsWithHistory,
            Math.Max(0, groups.Count - rowsWithHistory),
            providerPlayerIds.Count,
            statKeys.Select(ToSoccerWorldCupHistoryStatResponseKey).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
    }

    private static JsonObject BuildUnavailableSoccerPlayerHistorySummary(string reason, string? providerPlayerId = null)
    {
        var summary = new JsonObject
        {
            ["available"] = false,
            ["unavailableReason"] = reason,
            ["provider"] = ApiSportsFootballPlayerRefProvider,
            ["providerPlayerId"] = providerPlayerId
        };
        return summary;
    }

    private static JsonObject BuildAvailableSoccerPlayerHistorySummary(
        string providerPlayerId,
        IReadOnlyCollection<SoccerWorldCupHistoryResultRow> rows)
    {
        var orderedRows = rows
            .OrderByDescending(row => row.EventDate)
            .ToArray();
        var stats = new JsonObject();
        foreach (var statGroup in rows.GroupBy(row => row.StatKey).OrderBy(group => ToSoccerWorldCupHistoryStatResponseKey(group.Key)))
        {
            var statRows = statGroup.OrderByDescending(row => row.EventDate).ToArray();
            stats[ToSoccerWorldCupHistoryStatResponseKey(statGroup.Key)] = new JsonObject
            {
                ["count"] = statRows.Length,
                ["average"] = decimal.Round(statRows.Average(row => row.Value), 3),
                ["last5Average"] = decimal.Round(statRows.Take(5).Average(row => row.Value), 3),
                ["last10Average"] = decimal.Round(statRows.Take(10).Average(row => row.Value), 3)
            };
        }

        var competitionBreakdown = new JsonArray();
        foreach (var competitionGroup in rows
            .GroupBy(row => new { row.Competition, row.CompetitionCode })
            .OrderBy(group => group.Key.Competition ?? group.Key.CompetitionCode))
        {
            competitionBreakdown.Add(new JsonObject
            {
                ["competitionKey"] = competitionGroup.Key.CompetitionCode,
                ["competitionName"] = competitionGroup.Key.Competition,
                ["sampleSize"] = competitionGroup.Count(),
                ["latestResultDate"] = competitionGroup.Max(row => row.EventDate).ToString("O")
            });
        }

        var recentGameLog = new JsonArray();
        foreach (var gameGroup in orderedRows
            .GroupBy(row => new
            {
                row.EventDate,
                row.Competition,
                row.CompetitionCode,
                FixtureId = TryExtractApiSportsFixtureId(row.ExternalSourceIdentifier)
            })
            .OrderByDescending(group => group.Key.EventDate)
            .Take(5))
        {
            var gameStats = new JsonObject();
            foreach (var statRow in gameGroup.OrderBy(row => ToSoccerWorldCupHistoryStatResponseKey(row.StatKey)))
            {
                gameStats[ToSoccerWorldCupHistoryStatResponseKey(statRow.StatKey)] = statRow.Value;
            }

            recentGameLog.Add(new JsonObject
            {
                ["eventDate"] = gameGroup.Key.EventDate.ToString("O"),
                ["competition"] = gameGroup.Key.Competition,
                ["competitionKey"] = gameGroup.Key.CompetitionCode,
                ["fixtureId"] = gameGroup.Key.FixtureId,
                ["stats"] = gameStats
            });
        }

        return new JsonObject
        {
            ["available"] = true,
            ["unavailableReason"] = null,
            ["provider"] = ApiSportsFootballPlayerRefProvider,
            ["providerPlayerId"] = providerPlayerId,
            ["sampleSize"] = rows.Count,
            ["latestResultDate"] = rows.Max(row => row.EventDate).ToString("O"),
            ["stats"] = stats,
            ["competitionBreakdown"] = competitionBreakdown,
            ["recentGameLog"] = recentGameLog
        };
    }

    private static string ToSoccerWorldCupHistoryStatResponseKey(string? statKey)
    {
        return statKey?.ToUpperInvariant() switch
        {
            "GOALS" => "Goals",
            "ASSISTS" => "Assists",
            "SHOTS" => "Shots",
            "SHOTS_ON_TARGET" => "ShotsOnTarget",
            _ => string.IsNullOrWhiteSpace(statKey) ? "Unknown" : statKey
        };
    }

    private static string? TryExtractApiSportsFixtureId(string? externalSourceIdentifier)
    {
        if (string.IsNullOrWhiteSpace(externalSourceIdentifier))
        {
            return null;
        }

        var parts = externalSourceIdentifier.Split(':', StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && string.Equals(parts[0], ApiSportsFootballResultSource, StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }

    private static object ToSoccerWorldCupResearchOnlyResponseGroup(
        TheOddsLineShoppingGroup group,
        bool includeBookmakerPrices,
        bool soccerPredictionsEnabled,
        bool publicGradingEnabled,
        SoccerTopPicksOptions soccerTopPicksOptions,
        string? predictionUnavailableReason,
        JsonObject? playerHistorySummary)
    {
        var node = NormalizeJsonObjectPropertyCasing(
            JsonSerializer.SerializeToNode(ToLineShoppingResponseGroup(group, includeBookmakerPrices)) as JsonObject ?? []);
        var modelProbabilityAvailable = group.BestProbability.HasValue;
        var lifecycleBlocked = !string.Equals(group.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase) ||
            group.IsStale ||
            group.IsExpired;
        var resultGradingSupported = publicGradingEnabled &&
            group.BestProbability.HasValue &&
            SoccerWorldCupPredictionService.ResolveSupportedStatKey(group.NormalizedPropType, group.MarketKey) is not null;
        var soccerRecommendationEligible = IsSoccerWorldCupRecommendationEligible(group, soccerTopPicksOptions, lifecycleBlocked);
        var soccerResearchOnly = !soccerRecommendationEligible;

        node["sport"] = "SOCCER";
        node["competition"] = "World Cup";
        node["providerCategory"] = "Sportsbook";
        node["entryProviderCategory"] = "Sportsbook";
        node["entryActionableForUser"] = false;
        node["isActionableForUser"] = false;
        node["sportsbookContextResearchOnly"] = soccerResearchOnly;
        node["isResearchOnly"] = soccerResearchOnly;
        node["resultGradingSupported"] = resultGradingSupported;
        var unavailableReason = BuildSoccerWorldCupUnavailableReason(
            predictionUnavailableReason,
            group.HistoricalSampleSize,
            soccerPredictionsEnabled);
        node["resultGradingUnavailableReason"] = resultGradingSupported
            ? null
            : modelProbabilityAvailable
                ? "Soccer grading is not available for this row."
                : unavailableReason;
        node["publicSoccerGradingEnabled"] = publicGradingEnabled;
        node["recommendationActionableForUser"] = soccerRecommendationEligible;
        node["isRecommendation"] = soccerRecommendationEligible;
        node["recommendationType"] = soccerRecommendationEligible ? "Sportsbook" : null;
        node["entryUnavailableReason"] = soccerRecommendationEligible
            ? "Soccer sportsbook recommendation. Entry Builder is disabled for Soccer."
            : "Soccer World Cup sportsbook context is research-only and not Entry Builder playable.";
        node["entryRuleWarning"] = soccerRecommendationEligible
            ? "Soccer sportsbook recommendation. Entry Builder is disabled for Soccer."
            : "Soccer World Cup sportsbook context is research-only and not Entry Builder playable.";
        if (!modelProbabilityAvailable)
        {
            node["showExpectedValue"] = false;
            node["displayExpectedValue"] = null;
            node["expectedValueUnavailableReason"] = "World Cup sportsbook EV requires model probability and real odds.";
            node["bestEstimatedEv"] = null;
            node["bestProbability"] = null;
            node["selectedSideProbability"] = null;
            node["modelProbabilityAvailable"] = false;
            node["modelProbabilityOver"] = null;
            node["modelProbabilityUnder"] = null;
            node["modelProbabilityPush"] = null;
            node["modelProjection"] = null;
            node["projectionEdge"] = null;
            node["probabilitySource"] = null;
            node["hitChanceUnavailableReason"] = unavailableReason;
            node["strategyClassification"] = "ResearchOnly";
            node["strategyTier"] = "ResearchOnly";
            node["isCandidate"] = false;
            node["isWatchlist"] = false;
        }
        else if (lifecycleBlocked)
        {
            node["isCandidate"] = false;
            node["isWatchlist"] = false;
            node["isTopPick"] = false;
        }
        else if (soccerRecommendationEligible)
        {
            node["trustLevel"] = BuildSoccerWorldCupRecommendationTrustLevel(group);
            node["strategyClassification"] = "Candidate";
            node["strategyTier"] = "SoccerSportsbookRecommendation";
            node["strategyReasons"] = new JsonArray("Meets Soccer sportsbook recommendation gates: active market, supported prop, model hit chance, and sample size.");
            node["strategyWarnings"] = FilterSoccerWorldCupResearchOnlyMessages(node["strategyWarnings"] as JsonArray);
            node["riskTags"] = FilterSoccerWorldCupResearchOnlyTags(node["riskTags"] as JsonArray);
            node["isCandidate"] = true;
            node["isTopPick"] = true;
            node["isWatchlist"] = false;
            node["isResearchOnly"] = false;
            node["sportsbookContextResearchOnly"] = false;
            node["candidateStrategyName"] = "SoccerSportsbookRecommendation";
        }

        node["isAvoid"] = false;
        node["playerHistorySummary"] = playerHistorySummary?.DeepClone();

        return node;
    }

    private static bool IsSoccerWorldCupRecommendationEligible(
        TheOddsLineShoppingGroup group,
        SoccerTopPicksOptions options,
        bool lifecycleBlocked)
    {
        if (lifecycleBlocked ||
            !group.BestProbability.HasValue ||
            group.BestProbability.Value < options.MinimumHitProbability ||
            group.HistoricalSampleSize < Math.Max(1, options.MinimumTopPickSampleSize) ||
            group.ProjectionProbabilityConflict ||
            HasRecentWeightedProjectionProbabilityConflict(group) ||
            !group.BestEstimatedEv.HasValue ||
            group.BestEstimatedEv.Value <= 0m ||
            !string.IsNullOrWhiteSpace(group.EvSuppressedReason) ||
            SoccerWorldCupPredictionService.ResolveSupportedStatKey(group.NormalizedPropType, group.MarketKey) is null)
        {
            return false;
        }

        return true;
    }

    private static string BuildSoccerWorldCupRecommendationTrustLevel(TheOddsLineShoppingGroup group)
    {
        return group.BestProbability >= 0.70m && group.HistoricalSampleSize >= 20
            ? "Strong"
            : "Medium";
    }

    private static JsonArray FilterSoccerWorldCupResearchOnlyTags(JsonArray? riskTags)
    {
        var filtered = new JsonArray();
        if (riskTags is null)
        {
            return filtered;
        }

        foreach (var tag in riskTags)
        {
            var value = tag?.GetValue<string>();
            if (string.Equals(value, "ResearchOnlySample", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(value);
        }

        return filtered;
    }

    private static JsonArray FilterSoccerWorldCupResearchOnlyMessages(JsonArray? messages)
    {
        var filtered = new JsonArray();
        if (messages is null)
        {
            return filtered;
        }

        foreach (var message in messages)
        {
            var value = message?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value) ||
                value.Contains("research-only", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(value);
        }

        return filtered;
    }

    private static string BuildSoccerWorldCupUnavailableReason(
        string? predictionUnavailableReason,
        int historicalSampleSize,
        bool soccerPredictionsEnabled)
    {
        if (!soccerPredictionsEnabled)
        {
            return "Soccer grading and model hit chance are not available yet.";
        }

        return predictionUnavailableReason switch
        {
            "NoApprovedApiSportsProviderEntityLink" => "Soccer model hit chance is unavailable because this player is not linked to an API-Sports history profile.",
            "ApprovedLinkExistsButNoSupportedHistoricalRows" => "Soccer model hit chance is unavailable because no supported API-Sports history rows exist for this player and prop type.",
            "InsufficientHistoricalSample" => $"Soccer model hit chance is unavailable because only {historicalSampleSize} historical matches are available for this player and prop type.",
            "UnsupportedSoccerPropType" => "Soccer model hit chance is unavailable because this prop type is not supported for Soccer predictions.",
            null or "" => "Soccer model hit chance is unavailable for this row.",
            _ => $"Soccer model hit chance is unavailable for this row: {predictionUnavailableReason}."
        };
    }

    private static JsonObject NormalizeJsonObjectPropertyCasing(JsonObject source)
    {
        var result = new JsonObject();
        var keysByInsensitiveName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in source)
        {
            var normalizedName = ToCamelCaseJsonPropertyName(property.Key);
            if (!keysByInsensitiveName.Add(normalizedName))
            {
                continue;
            }

            result[normalizedName] = NormalizeJsonNodePropertyCasing(property.Value);
        }

        return result;
    }

    private static JsonNode? NormalizeJsonNodePropertyCasing(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => NormalizeJsonObjectPropertyCasing(obj),
            JsonArray array => new JsonArray(array.Select(item => NormalizeJsonNodePropertyCasing(item)).ToArray()),
            _ => node?.DeepClone()
        };
    }

    private static string ToCamelCaseJsonPropertyName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || char.IsLower(propertyName[0]))
        {
            return propertyName;
        }

        return char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }

    private sealed record SoccerWorldCupPlayerHistoryEnrichment(
        IReadOnlyDictionary<Guid, JsonObject> SummariesByPlayerId,
        int RowsWithPlayerHistory,
        int RowsWithoutPlayerHistory,
        int UniqueLinkedProviderPlayers,
        IReadOnlyList<string> HistoryStatsIncluded)
    {
        public static SoccerWorldCupPlayerHistoryEnrichment Empty(int rowCount)
        {
            return new SoccerWorldCupPlayerHistoryEnrichment(
                new Dictionary<Guid, JsonObject>(),
                RowsWithPlayerHistory: 0,
                RowsWithoutPlayerHistory: rowCount,
                UniqueLinkedProviderPlayers: 0,
                HistoryStatsIncluded: []);
        }
    }

    private sealed record PublicMarketReadTimingStage(
        string Name,
        long ElapsedMs,
        int? RowCount);

    private sealed record SoccerWorldCupHistoryResultRow(
        Guid PlayerId,
        DateOnly EventDate,
        decimal Value,
        string? PropType,
        string? StatKey,
        string? Competition,
        string? CompetitionCode,
        string? ExternalSourceIdentifier);

    internal sealed record SoccerWorldCupProviderContextCounts(
        int PlatformRowCount,
        int ActivePlatformRowCount,
        int ProviderEventCount,
        int BookmakerCount);

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
                .OrderByDescending(group => group.BestProbability ?? decimal.MinValue)
                .ThenByDescending(group => group.BestProjectionEdge ?? decimal.MinValue)
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
            .ThenByDescending(row => row.Group.BestProbability ?? decimal.MinValue)
            .ThenByDescending(row => row.Group.BestProjectionEdge ?? decimal.MinValue)
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

    private sealed record SnapshotPlayerLookup(Guid SnapshotId, string Player);
}
