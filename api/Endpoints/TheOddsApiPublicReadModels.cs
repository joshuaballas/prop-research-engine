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
        PredictionGenerationOptions selectionOptions,
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
        var preferredCandidateGroups = candidateGroups.Any(row => row.Group.IsActionableForUser)
            ? candidateGroups.Where(row => row.Group.IsActionableForUser)
            : candidateGroups;
        var topPicks = preferredCandidateGroups
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
        PredictionGenerationOptions selectionOptions,
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
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
        var requestedTake = Math.Clamp(take ?? 50, 1, 500);
        var latestSoccerRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API" && row.Sport == "SOCCER" && row.Succeeded && !row.DryRun)
            .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var platformProps = await LoadSoccerWorldCupSportsbookPlatformPropsAsync(
            dbContext,
            providerCategory,
            cancellationToken);
        var rows = BuildTheOddsResearchOnlyLineShoppingRows(platformProps);
        var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, lifecycleFreshnessMinutes)
            .OrderByDescending(group => group.EventStartTime ?? group.LastObservedAt)
            .ThenBy(group => group.Player)
            .ThenBy(group => group.NormalizedPropType)
            .ThenBy(group => group.Line)
            .Take(requestedTake)
            .ToArray();
        var comparisonGroups = groups
            .Select(group => ToSoccerWorldCupResearchOnlyResponseGroup(group, includeBookmakerPrices))
            .ToArray();
        var strategyClassificationSummary = BuildTheOddsStrategyClassificationSummary(groups);
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
                publicSoccerGradingEnabled = false,
                soccerPredictionsEnabled = false,
                entryActionableForUser = false,
                snapshotBacked = false,
                providerContextRowCount = platformProps.Count,
                researchOnlyComparisonGroupCount = groups.Length,
                totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                lifecycleFreshnessMinutes,
                take = requestedTake,
                includeBookmakerPrices
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
                eligibleComparisonGroups = 0,
                returnedComparisonGroups = comparisonGroups.Length,
                actionableCandidateCount = 0,
                sportsbookContextCandidateCount = 0,
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

        if (!entryActionableForUser ||
            !string.Equals(group.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase) ||
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
        var playerIdsBySnapshotId = await ResolveTopPickSnapshotPlayerIdsAsync(dbContext, rows, cancellationToken);

        return new MlbTopPicksReadResponse(
            rows.FirstOrDefault()?.GeneratedAtUtc ?? refreshRun.CompletedAtUtc ?? refreshRun.StartedAtUtc,
            SnapshotBacked: true,
            RefreshRunId: refreshRun.Id,
            FallbackReason: null,
            Diagnostics: BuildSnapshotDiagnostics(rows.Count, providerCategory),
            Run: new { runId = rows.FirstOrDefault()?.PredictionRunId, predictionCount = refreshRun.PredictionCount },
            Summary: BuildSnapshotSummary(rows),
            StrategyClassificationSummary: BuildSnapshotStrategySummary(rows),
            TopPicks: rows
                .Select(row => BuildSnapshotDetailsWithPlayerId(row.DetailsJson.RootElement, playerIdsBySnapshotId.GetValueOrDefault(row.Id)))
                .ToArray());
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
        var playerIdsBySnapshotId = await ResolveLineShoppingSnapshotPlayerIdsAsync(dbContext, rows, cancellationToken);

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

    private static object BuildSnapshotDetailsWithPlayerId(JsonElement detailsJson, Guid? playerId)
    {
        var node = JsonSerializer.SerializeToNode(detailsJson);
        if (node is not JsonObject obj)
        {
            return detailsJson.Clone();
        }

        AddExpectedValueDisplayMetadata(obj);
        AddModelProbabilityDisplayMetadata(obj);

        if (playerId.HasValue && playerId.Value != Guid.Empty)
        {
            obj["playerId"] = playerId.Value;
            obj["canonicalPlayerId"] = playerId.Value;
        }

        return obj;
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
        obj["expectedValueUnavailableReason"] = expectedValueDisplay.ExpectedValueUnavailableReason;
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
        var rows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
        var platformProps = ParseTheOddsPlatformProps(rows);

        return platformProps
            .Where(IsRealSoccerWorldCupSportsbookContextRow)
            .Where(row => string.IsNullOrWhiteSpace(providerCategory) ||
                string.Equals(row.ProviderCategory, providerCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
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
                    ConfidenceScore: 0m);
            })
            .ToList();
    }

    private static object ToSoccerWorldCupResearchOnlyResponseGroup(
        TheOddsLineShoppingGroup group,
        bool includeBookmakerPrices)
    {
        var node = JsonSerializer.SerializeToNode(ToLineShoppingResponseGroup(group, includeBookmakerPrices)) as JsonObject ?? [];
        node["sport"] = "SOCCER";
        node["competition"] = "World Cup";
        node["providerCategory"] = "Sportsbook";
        node["entryProviderCategory"] = "Sportsbook";
        node["entryActionableForUser"] = false;
        node["isActionableForUser"] = false;
        node["sportsbookContextResearchOnly"] = true;
        node["isResearchOnly"] = true;
        node["resultGradingSupported"] = false;
        node["resultGradingUnavailableReason"] = "Soccer history is not available yet.";
        node["publicSoccerGradingEnabled"] = false;
        node["entryUnavailableReason"] = "Soccer World Cup sportsbook context is research-only and not Entry Builder playable.";
        node["entryRuleWarning"] = "Soccer World Cup sportsbook context is research-only and not Entry Builder playable.";
        node["showExpectedValue"] = false;
        node["displayExpectedValue"] = null;
        node["expectedValueUnavailableReason"] = "World Cup sportsbook context is research-only until Soccer predictions and grading are enabled.";
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
        node["hitChanceUnavailableReason"] = "World Cup model hit chance is not available yet.";
        node["strategyClassification"] = "ResearchOnly";
        node["strategyTier"] = "ResearchOnly";
        node["isCandidate"] = false;
        node["isWatchlist"] = false;
        node["isAvoid"] = false;

        return node;
    }

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
