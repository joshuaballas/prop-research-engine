using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using System.Text.Json;

namespace SportsBetting.Api.Endpoints;

public static class RecommendationLedgerDebugEndpoints
{
    private static readonly string[] SupportedMlbPerformancePropTypeKeys =
    [
        "hits",
        "runs",
        "rbis",
        "homeruns",
        "totalbases",
        "stolenbases",
        "batterwalks",
        "hitsrunsrbis",
        "pitcherstrikeouts",
        "pitcherwalks",
        "walksallowed",
        "pitcherhitsallowed",
        "hitsallowed",
        "pitcherearnedruns",
        "earnedrunsallowed"
    ];

    public static IEndpointRouteBuilder MapRecommendationLedgerDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/recommendations/ledger/audit", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeFunnel,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var from = fromDate ?? DateOnly.MinValue;
            var to = toDate ?? DateOnly.MaxValue;
            var refreshRuns = await dbContext.TheOddsRefreshRuns
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .GroupBy(row => row.SlateDate)
                .Select(group => new
                {
                    date = group.Key,
                    totalRuns = group.Count(),
                    successfulRuns = group.Count(row => row.Succeeded),
                    predictionCount = group.Sum(row => row.PredictionCount),
                    lineShoppingGroupCount = group.Sum(row => row.LineShoppingGroupCount),
                    topPickSnapshotCount = group.Sum(row => row.TopPickSnapshots.Count),
                    activeCandidateCount = group.Sum(row => row.ActiveCandidateCount),
                    platformPropsCreated = group.Sum(row => row.PlatformPropsCreated),
                    platformPropsUpdated = group.Sum(row => row.PlatformPropsUpdated),
                    platformPropsUpserted = group.Sum(row => row.PlatformPropsUpserted),
                    earliestStartedAtUtc = group.Min(row => row.StartedAtUtc),
                    latestCompletedAtUtc = group.Max(row => row.CompletedAtUtc)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var predictionRuns = await dbContext.PredictionRuns
                .AsNoTracking()
                .Where(row => DateOnly.FromDateTime(row.CreatedAt.UtcDateTime) >= from && DateOnly.FromDateTime(row.CreatedAt.UtcDateTime) <= to)
                .GroupBy(row => DateOnly.FromDateTime(row.CreatedAt.UtcDateTime))
                .Select(group => new
                {
                    date = group.Key,
                    predictionRunCount = group.Count(),
                    predictionCount = group.Sum(row => row.Predictions.Count)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var topPickSnapshots = await dbContext.MarketTopPickSnapshots
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .GroupBy(row => row.SlateDate)
                .Select(group => new
                {
                    date = group.Key,
                    rows = group.Count(),
                    distinctRefreshRuns = group.Select(row => row.TheOddsRefreshRunId).Distinct().Count(),
                    candidateRows = group.Count(row => row.IsCandidate),
                    actionableRows = group.Count(row => row.EntryActionableForUser),
                    earliestGeneratedAtUtc = group.Min(row => row.GeneratedAtUtc),
                    latestGeneratedAtUtc = group.Max(row => row.GeneratedAtUtc)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var lineShoppingSnapshots = await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .GroupBy(row => row.SlateDate)
                .Select(group => new
                {
                    date = group.Key,
                    rows = group.Count(),
                    distinctRefreshRuns = group.Select(row => row.TheOddsRefreshRunId).Distinct().Count(),
                    candidateRows = group.Count(row => row.IsCandidate),
                    watchlistRows = group.Count(row => row.IsWatchlist),
                    activeRows = group.Count(row => row.LifecycleStatus == "Active"),
                    actionableRows = group.Count(row => row.EntryActionableForUser),
                    earliestGeneratedAtUtc = group.Min(row => row.GeneratedAtUtc),
                    latestGeneratedAtUtc = group.Max(row => row.GeneratedAtUtc)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var ledger = await dbContext.RecommendationSnapshots
                .AsNoTracking()
                .Where(row => row.ExpectedResultDate >= from && row.ExpectedResultDate <= to)
                .GroupBy(row => row.ExpectedResultDate)
                .Select(group => new
                {
                    date = group.Key,
                    rows = group.Count(),
                    candidateRows = group.Count(row => row.IsCandidate),
                    topPickRows = group.Count(row => row.IsTopPick),
                    actionableRows = group.Count(row => row.IsActionableForUser),
                    gradedRows = group.Count(row => row.GradeStatus == "Won" || row.GradeStatus == "Lost" || row.GradeStatus == "Pushed"),
                    noResultRows = group.Count(row => row.GradeStatus == "NoResult"),
                    unmatchedRows = group.Count(row => row.GradeStatus == "Unmatched"),
                    excludedFromPerformanceRows = group.Count(row => row.IsExcludedFromPerformance)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var propResults = await dbContext.PropResults
                .AsNoTracking()
                .Where(row => row.EventDate >= from && row.EventDate <= to)
                .GroupBy(row => row.EventDate)
                .Select(group => new
                {
                    date = group.Key,
                    rows = group.Count(),
                    distinctPlayers = group.Select(row => row.PlayerId).Distinct().Count(),
                    distinctPropTypes = group.Select(row => row.PropTypeId).Distinct().Count()
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);

            var refreshRunRows = await dbContext.TheOddsRefreshRuns
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .Select(row => new
                {
                    row.Id,
                    row.SlateDate,
                    row.StartedAtUtc,
                    row.CompletedAtUtc,
                    row.Succeeded,
                    row.Failed,
                    row.Blocked,
                    row.DryRun,
                    row.ForceLive,
                    row.BookmakerPreset,
                    row.ProviderCategoriesIncluded,
                    row.DfsBookmakersRequested,
                    row.EstimatedApiCalls,
                    row.ActualApiCalls,
                    row.PlatformPropsCreated,
                    row.PlatformPropsUpdated,
                    row.PlatformPropsUpserted,
                    row.PredictionCount,
                    row.LineShoppingGroupCount,
                    row.ActiveCandidateCount,
                    row.ActiveWatchlistCount,
                    row.ActiveAvoidCount,
                    row.ErrorMessage
                })
                .ToArrayAsync(cancellationToken);

            var predictionRunRows = await dbContext.PredictionRuns
                .AsNoTracking()
                .Where(row => DateOnly.FromDateTime(row.CreatedAt.UtcDateTime) >= from && DateOnly.FromDateTime(row.CreatedAt.UtcDateTime) <= to)
                .Select(row => new
                {
                    row.Id,
                    date = DateOnly.FromDateTime(row.CreatedAt.UtcDateTime),
                    row.CreatedAt,
                    row.CompletedAt,
                    predictionCount = row.Predictions.Count,
                    candidateCount = row.Predictions.Count(prediction => prediction.Recommendation == "OVER" || prediction.Recommendation == "UNDER"),
                    passCount = row.Predictions.Count(prediction => prediction.Recommendation == "PASS")
                })
                .ToArrayAsync(cancellationToken);

            var topPickRows = await dbContext.MarketTopPickSnapshots
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .Select(row => new
                {
                    row.Id,
                    row.SlateDate,
                    row.GeneratedAtUtc,
                    row.TheOddsRefreshRunId,
                    row.PredictionRunId,
                    row.ProviderCategory,
                    row.Provider,
                    row.EntryProvider,
                    row.Bookmaker,
                    row.Player,
                    row.PropType,
                    row.StrategyClassification,
                    row.StrategyTier,
                    row.IsCandidate,
                    IsTopPick = true,
                    row.IsWatchlist,
                    row.IsResearchOnly,
                    row.EntryActionableForUser,
                    row.LifecycleStatus,
                    row.EventStartTime
                })
                .ToArrayAsync(cancellationToken);

            var lineShoppingRows = await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .Select(row => new
                {
                    row.Id,
                    row.SlateDate,
                    row.GeneratedAtUtc,
                    row.TheOddsRefreshRunId,
                    row.PredictionRunId,
                    row.ProviderCategory,
                    row.Provider,
                    row.EntryProvider,
                    row.Bookmaker,
                    row.Player,
                    row.PropType,
                    row.StrategyClassification,
                    row.StrategyTier,
                    row.IsCandidate,
                    row.IsWatchlist,
                    row.IsResearchOnly,
                    row.EntryActionableForUser,
                    row.LifecycleStatus,
                    row.EventStartTime
                })
                .ToArrayAsync(cancellationToken);

            var ledgerRows = await dbContext.RecommendationSnapshots
                .AsNoTracking()
                .Where(row => row.ExpectedResultDate >= from && row.ExpectedResultDate <= to)
                .Select(row => new
                {
                    row.Id,
                    row.FirstSeenAtUtc,
                    row.LastSeenAtUtc,
                    row.SeenCount,
                    row.CapturedAtUtc,
                    row.RefreshRunId,
                    row.PredictionRunId,
                    row.Sport,
                    row.SlateDate,
                    row.ExpectedResultDate,
                    row.ResultDateSource,
                    row.EventStartTimeUtc,
                    row.CanonicalPlayerId,
                    row.PlayerName,
                    row.PropTypeId,
                    row.PropType,
                    row.Side,
                    row.Line,
                    row.Provider,
                    row.Bookmaker,
                    row.ProviderCategory,
                    row.EntryProvider,
                    row.StrategyClassification,
                    row.StrategyTier,
                    row.IsCandidate,
                    row.IsTopPick,
                    row.IsWatchlist,
                    row.IsActionableForUser,
                    row.LifecycleStatus,
                    row.GradeStatus,
                    row.GradingFailureReason,
                    row.IsExcludedFromPerformance,
                    row.PerformanceExclusionReason,
                    row.PerformanceExcludedAtUtc,
                    row.PerformanceExcludedBy,
                    isBackfilled = !row.RefreshRunId.HasValue && !row.PredictionRunId.HasValue
                })
                .ToArrayAsync(cancellationToken);

            var auditDates = refreshRunRows.Select(row => row.SlateDate)
                .Concat(predictionRunRows.Select(row => row.date))
                .Concat(topPickRows.Select(row => row.SlateDate))
                .Concat(lineShoppingRows.Select(row => row.SlateDate))
                .Concat(ledgerRows.Select(row => row.ExpectedResultDate))
                .Concat(propResults.Select(row => row.date))
                .Distinct()
                .Order()
                .ToArray();

            static object[] CountBy<T>(IEnumerable<T> rows, Func<T, string?> keySelector)
            {
                return rows
                    .GroupBy(row => string.IsNullOrWhiteSpace(keySelector(row)) ? "(blank)" : keySelector(row)!.Trim())
                    .Select(group => new { key = group.Key, count = group.Count() })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.key)
                    .Cast<object>()
                    .ToArray();
            }

            static object BuildSnapshotBreakdown<T>(
                IReadOnlyCollection<T> rows,
                Func<T, bool> isCandidate,
                Func<T, bool> isWatchlist,
                Func<T, bool> isActionable,
                Func<T, string?> provider,
                Func<T, string?> propType,
                Func<T, string?> lifecycleStatus,
                Func<T, string?> strategyClassification,
                Func<T, string?> strategyTier,
                Func<T, Guid> refreshRunId,
                Func<T, DateTimeOffset> generatedAtUtc)
            {
                return new
                {
                    rows = rows.Count,
                    candidateRows = rows.Count(isCandidate),
                    watchlistRows = rows.Count(isWatchlist),
                    actionableRows = rows.Count(isActionable),
                    distinctRefreshRuns = rows.Select(refreshRunId).Distinct().Count(),
                    earliestGeneratedAtUtc = rows.Count == 0 ? (DateTimeOffset?)null : rows.Min(generatedAtUtc),
                    latestGeneratedAtUtc = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(generatedAtUtc),
                    byProvider = CountBy(rows, provider),
                    byPropType = CountBy(rows, propType),
                    byLifecycleStatus = CountBy(rows, lifecycleStatus),
                    byStrategyClassification = CountBy(rows, strategyClassification),
                    byStrategyTier = CountBy(rows, strategyTier)
                };
            }

            var funnelByDate = auditDates.Select(date =>
            {
                var dateRefreshRuns = refreshRunRows.Where(row => row.SlateDate == date).ToArray();
                var datePredictionRuns = predictionRunRows.Where(row => row.date == date).ToArray();
                var dateTopPicks = topPickRows.Where(row => row.SlateDate == date).ToArray();
                var dateLineShopping = lineShoppingRows.Where(row => row.SlateDate == date).ToArray();
                var dateLedger = ledgerRows.Where(row => row.ExpectedResultDate == date).ToArray();
                var datePropResults = propResults.FirstOrDefault(row => row.date == date);
                var currentPerformanceCandidateFilterRows = dateLedger.Where(row => row.IsCandidate).ToArray();
                var candidateOrSurfacedRows = dateLedger.Where(row => row.IsCandidate || row.IsTopPick || row.IsWatchlist).ToArray();
                var backfilledRows = dateLedger.Where(row => row.isBackfilled).ToArray();
                var sourceLineShoppingRowsFound = dateLineShopping.Count(row => row.IsCandidate || row.IsWatchlist);
                var sourceTopPickRowsFound = dateTopPicks.Length;
                var sourceRowsFound = sourceLineShoppingRowsFound + sourceTopPickRowsFound;

                return new
                {
                    date,
                    theOddsRefreshRuns = new
                    {
                        totalRuns = dateRefreshRuns.Length,
                        successfulRuns = dateRefreshRuns.Count(row => row.Succeeded),
                        failedRuns = dateRefreshRuns.Count(row => row.Failed),
                        blockedRuns = dateRefreshRuns.Count(row => row.Blocked),
                        dryRuns = dateRefreshRuns.Count(row => row.DryRun),
                        earliestStartedAtUtc = dateRefreshRuns.Length == 0 ? (DateTimeOffset?)null : dateRefreshRuns.Min(row => row.StartedAtUtc),
                        earliestCompletedAtUtc = dateRefreshRuns.Length == 0 ? null : dateRefreshRuns.Min(row => row.CompletedAtUtc),
                        latestStartedAtUtc = dateRefreshRuns.Length == 0 ? (DateTimeOffset?)null : dateRefreshRuns.Max(row => row.StartedAtUtc),
                        latestCompletedAtUtc = dateRefreshRuns.Length == 0 ? null : dateRefreshRuns.Max(row => row.CompletedAtUtc),
                        predictionCount = dateRefreshRuns.Sum(row => row.PredictionCount),
                        lineShoppingGroupCount = dateRefreshRuns.Sum(row => row.LineShoppingGroupCount),
                        topPickSnapshotCount = dateTopPicks.Length,
                        activeCandidateCount = dateRefreshRuns.Sum(row => row.ActiveCandidateCount),
                        activeWatchlistCount = dateRefreshRuns.Sum(row => row.ActiveWatchlistCount),
                        platformPropsCreated = dateRefreshRuns.Sum(row => row.PlatformPropsCreated),
                        platformPropsUpdated = dateRefreshRuns.Sum(row => row.PlatformPropsUpdated),
                        platformPropsUpserted = dateRefreshRuns.Sum(row => row.PlatformPropsUpserted),
                        bookmakerPresets = CountBy(dateRefreshRuns, row => row.BookmakerPreset),
                        providerCategoriesIncluded = CountBy(dateRefreshRuns, row => row.ProviderCategoriesIncluded),
                        runIds = includeFunnel == true ? dateRefreshRuns.OrderBy(row => row.StartedAtUtc).Select(row => row.Id).ToArray() : []
                    },
                    predictionRuns = new
                    {
                        predictionRunCount = datePredictionRuns.Length,
                        predictionCount = datePredictionRuns.Sum(row => row.predictionCount),
                        candidateCount = datePredictionRuns.Sum(row => row.candidateCount),
                        passCount = datePredictionRuns.Sum(row => row.passCount),
                        earliestCreatedAtUtc = datePredictionRuns.Length == 0 ? (DateTimeOffset?)null : datePredictionRuns.Min(row => row.CreatedAt),
                        latestCreatedAtUtc = datePredictionRuns.Length == 0 ? (DateTimeOffset?)null : datePredictionRuns.Max(row => row.CreatedAt),
                        runIds = includeFunnel == true ? datePredictionRuns.OrderBy(row => row.CreatedAt).Select(row => row.Id).ToArray() : []
                    },
                    marketTopPickSnapshots = BuildSnapshotBreakdown(
                        dateTopPicks,
                        row => row.IsCandidate,
                        row => row.IsWatchlist,
                        row => row.EntryActionableForUser,
                        row => row.EntryProvider,
                        row => row.PropType,
                        row => row.LifecycleStatus,
                        row => row.StrategyClassification,
                        row => row.StrategyTier,
                        row => row.TheOddsRefreshRunId,
                        row => row.GeneratedAtUtc),
                    marketLineShoppingSnapshots = BuildSnapshotBreakdown(
                        dateLineShopping,
                        row => row.IsCandidate,
                        row => row.IsWatchlist,
                        row => row.EntryActionableForUser,
                        row => row.EntryProvider,
                        row => row.PropType,
                        row => row.LifecycleStatus,
                        row => row.StrategyClassification,
                        row => row.StrategyTier,
                        row => row.TheOddsRefreshRunId,
                        row => row.GeneratedAtUtc),
                    recommendationSnapshots = new
                    {
                        rows = dateLedger.Length,
                        candidateRows = dateLedger.Count(row => row.IsCandidate),
                        candidateOrSurfacedRows = candidateOrSurfacedRows.Length,
                        topPickRows = dateLedger.Count(row => row.IsTopPick),
                        watchlistRows = dateLedger.Count(row => row.IsWatchlist),
                        actionableRows = dateLedger.Count(row => row.IsActionableForUser),
                        gradedRows = dateLedger.Count(row => row.GradeStatus is "Won" or "Lost" or "Pushed"),
                        noResultRows = dateLedger.Count(row => row.GradeStatus == "NoResult"),
                        unmatchedRows = dateLedger.Count(row => row.GradeStatus == "Unmatched"),
                        pendingRows = dateLedger.Count(row => row.GradeStatus == "Pending"),
                        earliestCapturedAtUtc = dateLedger.Length == 0 ? (DateTimeOffset?)null : dateLedger.Min(row => row.CapturedAtUtc),
                        latestCapturedAtUtc = dateLedger.Length == 0 ? (DateTimeOffset?)null : dateLedger.Max(row => row.CapturedAtUtc),
                        firstSeenAtUtc = dateLedger.Length == 0 ? (DateTimeOffset?)null : dateLedger.Min(row => row.FirstSeenAtUtc),
                        lastSeenAtUtc = dateLedger.Length == 0 ? (DateTimeOffset?)null : dateLedger.Max(row => row.LastSeenAtUtc),
                        byProvider = CountBy(dateLedger, row => row.EntryProvider),
                        byPropType = CountBy(dateLedger, row => row.PropType),
                        byStrategyClassification = CountBy(dateLedger, row => row.StrategyClassification),
                        byStrategyTier = CountBy(dateLedger, row => row.StrategyTier),
                        bySource = CountBy(dateLedger, row => row.RefreshRunId.HasValue ? "the_odds_refresh" : row.PredictionRunId.HasValue ? "prediction_run" : "backfill_or_unknown"),
                        byIsBackfilled = CountBy(dateLedger, row => row.isBackfilled ? "true" : "false"),
                        byGradeStatus = CountBy(dateLedger, row => row.GradeStatus),
                        byResultDateSource = CountBy(dateLedger, row => row.ResultDateSource),
                        backfilledRows = backfilledRows.Length
                    },
                    backfillDiagnostics = new
                    {
                        sourceRowsFound,
                        sourceRowsFoundBySource = new
                        {
                            marketLineShoppingSnapshots = sourceLineShoppingRowsFound,
                            marketTopPickSnapshots = sourceTopPickRowsFound
                        },
                        persistedBackfilledRows = backfilledRows.Length,
                        sourceRowsMinusLedgerRows = Math.Max(0, sourceRowsFound - dateLedger.Length),
                        interpretation = sourceRowsFound > dateLedger.Length
                            ? "More source snapshot rows exist than persisted ledger rows for this date. Use the dry-run backfill endpoint for row-level skip reasons before changing grading."
                            : "Persisted ledger rows are not fewer than candidate/watchlist/top-pick snapshot source rows for this date."
                    },
                    dateAlignmentDiagnostics = new
                    {
                        rowsBySlateDate = CountBy(dateLedger, row => row.SlateDate.ToString("yyyy-MM-dd")),
                        rowsByExpectedResultDate = CountBy(dateLedger, row => row.ExpectedResultDate.ToString("yyyy-MM-dd")),
                        rowsByEventUtcDate = CountBy(dateLedger, row => row.EventStartTimeUtc.HasValue ? DateOnly.FromDateTime(row.EventStartTimeUtc.Value.UtcDateTime).ToString("yyyy-MM-dd") : "(blank)"),
                        rowsByEventEasternDate = CountBy(dateLedger, row => row.EventStartTimeUtc.HasValue ? ToEasternDate(row.EventStartTimeUtc.Value).ToString("yyyy-MM-dd") : "(blank)"),
                        utcDateDiffersFromExpectedResultDate = dateLedger.Count(row => row.EventStartTimeUtc.HasValue && DateOnly.FromDateTime(row.EventStartTimeUtc.Value.UtcDateTime) != row.ExpectedResultDate),
                        easternDateMatchesExpectedResultDate = dateLedger.Count(row => row.EventStartTimeUtc.HasValue && ToEasternDate(row.EventStartTimeUtc.Value) == row.ExpectedResultDate)
                    },
                    candidateFilterDiagnostics = new
                    {
                        ledgerRows = dateLedger.Length,
                        candidateOrSurfacedRows = candidateOrSurfacedRows.Length,
                        currentPerformanceCandidateOnlyRows = currentPerformanceCandidateFilterRows.Length,
                        excludedByCurrentCandidateOnlyFilter = dateLedger.Length - currentPerformanceCandidateFilterRows.Length,
                        excludedBecauseNotIsCandidate = dateLedger.Count(row => !row.IsCandidate),
                        excludedFromPerformanceRows = dateLedger.Count(row => row.IsExcludedFromPerformance),
                        note = "Performance candidateOnly=true evaluates actionable candidate or top-pick rows by default. Watchlist/research/stale rows are diagnostic unless explicitly included."
                    },
                    propResults = new
                    {
                        rows = datePropResults?.rows ?? 0,
                        distinctPlayers = datePropResults?.distinctPlayers ?? 0,
                        distinctPropTypes = datePropResults?.distinctPropTypes ?? 0
                    }
                };
            }).ToArray();

            return Results.Ok(new
            {
                fromDate = fromDate,
                toDate = toDate,
                callsExternalApis = false,
                includeFunnel = includeFunnel == true,
                refreshRuns,
                predictionRuns,
                topPickSnapshots,
                lineShoppingSnapshots,
                recommendationLedger = ledger,
                propResults,
                funnelByDate,
                notes = new[]
                {
                    "market_*_snapshots are read snapshots keyed by refresh run and are replaced per run before insert.",
                    "recommendation_snapshots is the durable gradeable ledger used by Performance Summary when populated.",
                    "Performance candidateOnly=true evaluates actionable candidate or top-pick rows by default. Watchlist/research/stale rows are diagnostic unless explicitly included.",
                    "This endpoint is read-only and does not call external APIs."
                }
            });
        });

        endpoints.MapGet("/api/debug/recommendations/ledger/result-coverage", async (
            DateOnly? date,
            string? sport,
            string? playerName,
            string? propType,
            int? take,
            Guid? recommendationSnapshotId,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var effectiveSport = string.IsNullOrWhiteSpace(sport) ? "MLB" : sport.Trim().ToUpperInvariant();
            var maxRows = Math.Clamp(take ?? 10, 1, 50);
            var normalizedPlayerNameFilter = NormalizeDiagnosticKey(playerName);
            var normalizedPropTypeFilter = NormalizeDiagnosticKey(propType);

            var query = dbContext.RecommendationSnapshots
                .AsNoTracking()
                .Where(row => row.Sport == effectiveSport);

            if (recommendationSnapshotId.HasValue)
            {
                query = query.Where(row => row.Id == recommendationSnapshotId.Value);
            }
            else
            {
                if (date.HasValue)
                {
                    query = query.Where(row => row.ExpectedResultDate == date.Value);
                }

                if (!string.IsNullOrWhiteSpace(normalizedPlayerNameFilter))
                {
                    query = query.Where(row =>
                        row.NormalizedPlayerName.ToLower() == normalizedPlayerNameFilter ||
                        row.PlayerName.ToLower().Replace(" ", "") == normalizedPlayerNameFilter);
                }

                if (!string.IsNullOrWhiteSpace(normalizedPropTypeFilter))
                {
                    query = query.Where(row =>
                        row.NormalizedPropType.ToLower() == normalizedPropTypeFilter ||
                        row.PropType.ToLower().Replace(" ", "") == normalizedPropTypeFilter);
                }
            }

            var capturedRows = await query
                .OrderByDescending(row => row.RankingScore)
                .ThenByDescending(row => row.EstimatedEv)
                .Take(maxRows)
                .ToArrayAsync(cancellationToken);

            var datesToInspect = capturedRows
                .SelectMany(row => new[] { row.ExpectedResultDate.AddDays(-1), row.ExpectedResultDate, row.ExpectedResultDate.AddDays(1) })
                .Distinct()
                .ToArray();

            var resultRows = datesToInspect.Length == 0
                ? []
                : await dbContext.PropResults
                    .AsNoTracking()
                    .Where(row => datesToInspect.Contains(row.EventDate))
                    .Select(row => new CoveragePropResultRow(
                        row.Id,
                        row.EventDate,
                        row.PlayerId,
                        row.Player != null ? row.Player.DisplayName : string.Empty,
                        row.PropTypeId,
                        row.PropType != null ? row.PropType.Name : string.Empty,
                        row.ActualResultValue))
                    .ToArrayAsync(cancellationToken);
            var playerIdsToInspect = capturedRows
                .Where(row => row.CanonicalPlayerId.HasValue)
                .Select(row => row.CanonicalPlayerId!.Value)
                .Distinct()
                .ToArray();
            var propTypeIdsToInspect = capturedRows
                .Where(row => row.PropTypeId.HasValue)
                .Select(row => row.PropTypeId!.Value)
                .Distinct()
                .ToArray();
            var maxExpectedResultDate = capturedRows
                .Select(row => (DateOnly?)row.ExpectedResultDate)
                .Max();
            var historicalRows = playerIdsToInspect.Length == 0 || propTypeIdsToInspect.Length == 0 || !maxExpectedResultDate.HasValue
                ? []
                : await dbContext.PropResults
                    .AsNoTracking()
                    .Where(row => playerIdsToInspect.Contains(row.PlayerId)
                        && propTypeIdsToInspect.Contains(row.PropTypeId)
                        && row.EventDate < maxExpectedResultDate.Value)
                    .Select(row => new CoveragePropResultRow(
                        row.Id,
                        row.EventDate,
                        row.PlayerId,
                        row.Player != null ? row.Player.DisplayName : string.Empty,
                        row.PropTypeId,
                        row.PropType != null ? row.PropType.Name : string.Empty,
                        row.ActualResultValue))
                    .ToArrayAsync(cancellationToken);

            var examples = capturedRows
                .Select(row => BuildResultCoverageExample(row, resultRows, historicalRows))
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                sport = effectiveSport,
                filters = new
                {
                    date,
                    playerName,
                    propType,
                    recommendationSnapshotId,
                    take = maxRows
                },
                capturedRows = capturedRows.Length,
                resultCoverageExamples = examples,
                notes = new[]
                {
                    "This endpoint inspects persisted recommendation_snapshots and prop_results only.",
                    "Raw MLB boxscore payloads are not currently persisted, so raw boxscore presence is reported as unknown.",
                    "Missing player prop_results are diagnostic only and are not treated as wins, losses, or pushes."
                }
            });
        })
        .WithName("RecommendationLedgerResultCoverageDebug")
        .WithSummary("Read-only diagnostic explaining whether captured ledger picks have matching imported MLB prop_results.");

        endpoints.MapGet("/api/debug/recommendations/ledger/no-result-audit", async (
            string? sport,
            DateOnly? date,
            bool? candidateOnly,
            bool? topPickOnly,
            bool? includeWatchlist,
            bool? includeResearch,
            bool? includeStale,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var effectiveSport = string.IsNullOrWhiteSpace(sport) ? "MLB" : sport.Trim().ToUpperInvariant();
            var effectiveDate = date ?? await dbContext.PropResults
                .AsNoTracking()
                .Select(row => (DateOnly?)row.EventDate)
                .MaxAsync(cancellationToken) ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var effectiveCandidateOnly = candidateOnly ?? !(includeWatchlist == true || includeResearch == true);
            var effectiveTopPickOnly = topPickOnly == true;
            var effectiveIncludeWatchlist = includeWatchlist == true;
            var effectiveIncludeResearch = includeResearch == true;
            var effectiveIncludeStale = includeStale == true;

            var snapshots = await dbContext.RecommendationSnapshots
                .AsNoTracking()
                .Where(row => row.Sport == effectiveSport && row.ExpectedResultDate == effectiveDate)
                .ToArrayAsync(cancellationToken);
            var datesToInspect = new[] { effectiveDate.AddDays(-1), effectiveDate, effectiveDate.AddDays(1) };
            var propResults = await dbContext.PropResults
                .AsNoTracking()
                .Where(row => datesToInspect.Contains(row.EventDate))
                .Select(row => new CoveragePropResultRow(
                    row.Id,
                    row.EventDate,
                    row.PlayerId,
                    row.Player != null ? row.Player.DisplayName : string.Empty,
                    row.PropTypeId,
                    row.PropType != null ? row.PropType.Name : string.Empty,
                    row.ActualResultValue))
                .ToArrayAsync(cancellationToken);
            var auditPropResults = propResults
                .Select(row => new AuditPropResultRow(row.Id, row.EventDate, row.PlayerId, row.PropTypeId, row.PropType, row.ActualResultValue))
                .ToArray();
            var auditPlayerIds = snapshots
                .Where(row => row.CanonicalPlayerId.HasValue)
                .Select(row => row.CanonicalPlayerId!.Value)
                .Distinct()
                .ToArray();
            var seasonStart = new DateOnly(effectiveDate.Year, 1, 1);
            var seasonEnd = new DateOnly(effectiveDate.Year, 12, 31);
            var seasonPropResults = auditPlayerIds.Length == 0
                ? []
                : await dbContext.PropResults
                    .AsNoTracking()
                    .Where(row => auditPlayerIds.Contains(row.PlayerId)
                        && row.EventDate >= seasonStart
                        && row.EventDate <= seasonEnd)
                    .Select(row => new CoveragePropResultRow(
                        row.Id,
                        row.EventDate,
                        row.PlayerId,
                        row.Player != null ? row.Player.DisplayName : string.Empty,
                        row.PropTypeId,
                        row.PropType != null ? row.PropType.Name : string.Empty,
                        row.ActualResultValue))
                    .ToArrayAsync(cancellationToken);
            var importedDates = auditPropResults
                .Where(row => row.EventDate == effectiveDate)
                .Select(row => row.EventDate)
                .Distinct()
                .ToHashSet();

            var rows = snapshots
                .Select(snapshot =>
                {
                    var grade = AuditGradeRecommendation(snapshot, auditPropResults, importedDates);
                    var exclusionReasons = BuildPerformanceExclusionReasons(
                        snapshot,
                        effectiveCandidateOnly,
                        effectiveTopPickOnly,
                        effectiveIncludeWatchlist,
                        effectiveIncludeResearch,
                        effectiveIncludeStale);
                    var normalizedPlayerName = string.IsNullOrWhiteSpace(snapshot.NormalizedPlayerName)
                        ? NormalizeDiagnosticKey(snapshot.PlayerName)
                        : NormalizeDiagnosticKey(snapshot.NormalizedPlayerName);
                    var normalizedPropType = string.IsNullOrWhiteSpace(snapshot.NormalizedPropType)
                        ? NormalizeDiagnosticKey(snapshot.PropType)
                        : NormalizeDiagnosticKey(snapshot.NormalizedPropType);
                    var sameDateRows = propResults.Where(row => row.EventDate == snapshot.ExpectedResultDate).ToArray();
                    var samePlayerAnyProp = sameDateRows
                        .Where(row => PlayerMatches(snapshot, row, normalizedPlayerName))
                        .ToArray();
                    var samePlayerSameProp = samePlayerAnyProp
                        .Where(row => PropTypeMatches(snapshot, row, normalizedPropType))
                        .ToArray();
                    var sameNormalizedNameAnyProp = sameDateRows
                        .Where(row => !string.IsNullOrWhiteSpace(normalizedPlayerName) && row.NormalizedPlayerName == normalizedPlayerName)
                        .ToArray();
                    var samePropTypeAnyPlayer = sameDateRows
                        .Where(row => PropTypeMatches(snapshot, row, normalizedPropType))
                        .ToArray();
                    var nearbyDatePlayerProp = propResults
                        .Where(row => row.EventDate != snapshot.ExpectedResultDate
                            && PlayerMatches(snapshot, row, normalizedPlayerName)
                            && PropTypeMatches(snapshot, row, normalizedPropType))
                        .ToArray();
                    var sameSeasonPlayerResults = seasonPropResults
                        .Where(row => PlayerMatches(snapshot, row, normalizedPlayerName))
                        .ToArray();
                    var sameSeasonPlayerPropResults = sameSeasonPlayerResults
                        .Where(row => PropTypeMatches(snapshot, row, normalizedPropType))
                        .ToArray();
                    var eventUtcDate = snapshot.EventStartTimeUtc.HasValue
                        ? DateOnly.FromDateTime(snapshot.EventStartTimeUtc.Value.UtcDateTime)
                        : (DateOnly?)null;
                    var eventEasternDate = snapshot.EventStartTimeUtc.HasValue
                        ? ToEasternDate(snapshot.EventStartTimeUtc.Value)
                        : (DateOnly?)null;
                    var isStale = !string.IsNullOrWhiteSpace(snapshot.Warning) && snapshot.Warning.Contains("stale", StringComparison.OrdinalIgnoreCase);
                    var isLocked = string.Equals(snapshot.LifecycleStatus, "Locked", StringComparison.OrdinalIgnoreCase);
                    var isExpired = string.Equals(snapshot.LifecycleStatus, "Expired", StringComparison.OrdinalIgnoreCase);
                    var isPlayable = snapshot.IsActionableForUser &&
                        string.Equals(snapshot.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase) &&
                        !isStale;
                    var included = exclusionReasons.Length == 0;

                    return new
                    {
                        snapshot,
                        grade,
                        included,
                        row = new
                        {
                            recommendationSnapshotId = snapshot.Id,
                            propType = snapshot.PropType,
                            provider = snapshot.Provider,
                            bookmaker = snapshot.Bookmaker,
                            entryProvider = snapshot.EntryProvider,
                            reasonCode = grade.ReasonCode,
                            finalGradeStatus = grade.Status,
                            eventId = snapshot.EventId,
                            snapshot.EventStartTimeUtc,
                            eventUtcDate,
                            eventEasternDate,
                            eventUtcDateDiffersFromExpectedResultDate = eventUtcDate.HasValue && eventUtcDate.Value != snapshot.ExpectedResultDate,
                            eventEasternDateDiffersFromExpectedResultDate = eventEasternDate.HasValue && eventEasternDate.Value != snapshot.ExpectedResultDate,
                            snapshot.ExpectedResultDate,
                            snapshot.SlateDate,
                            playerId = snapshot.CanonicalPlayerId,
                            playerName = snapshot.PlayerName,
                            snapshot.IsCandidate,
                            snapshot.IsTopPick,
                            snapshot.IsWatchlist,
                            snapshot.IsExcludedFromPerformance,
                            snapshot.PerformanceExclusionReason,
                            snapshot.PerformanceExcludedAtUtc,
                            snapshot.PerformanceExcludedBy,
                            isResearch = !snapshot.IsCandidate && !snapshot.IsTopPick && !snapshot.IsWatchlist,
                            isStale,
                            isLocked,
                            isExpired,
                            isPlayable,
                            isActionable = snapshot.IsActionableForUser,
                            includedInDefaultPerformance = IsIncludedInPerformance(snapshot, candidateOnly: true, topPickOnly: false, includeWatchlist: false, includeResearch: false, includeStale: false),
                            includedInCandidateOnlyPerformance = IsIncludedInPerformance(snapshot, candidateOnly: true, topPickOnly: false, includeWatchlist: false, includeResearch: false, includeStale: false),
                            includedInTopPickOnlyPerformance = IsIncludedInPerformance(snapshot, candidateOnly: false, topPickOnly: true, includeWatchlist: false, includeResearch: false, includeStale: false),
                            includedInRequestedPerformance = included,
                            exclusionReasons,
                            sameDatePlayerIdResultCount = snapshot.CanonicalPlayerId.HasValue
                                ? sameDateRows.Count(row => row.PlayerId == snapshot.CanonicalPlayerId.Value)
                                : 0,
                            sameDateNormalizedNameResultCount = sameNormalizedNameAnyProp.Length,
                            sameDatePlayerPropResultCount = samePlayerSameProp.Length,
                            samePropTypeAnyPlayer = samePropTypeAnyPlayer.Length,
                            nearbyDatePlayerPropResultCount = nearbyDatePlayerProp.Length,
                            nearbyDatePlayerPropMatches = nearbyDatePlayerProp
                                .OrderBy(row => Math.Abs(row.EventDate.DayNumber - snapshot.ExpectedResultDate.DayNumber))
                                .ThenBy(row => row.EventDate)
                                .Select(row => new
                                {
                                    propResultId = row.Id,
                                    row.EventDate,
                                    expectedResultDate = snapshot.ExpectedResultDate,
                                    dateDistanceDays = row.EventDate.DayNumber - snapshot.ExpectedResultDate.DayNumber,
                                    row.PlayerId,
                                    row.PlayerName,
                                    row.PropTypeId,
                                    row.PropType,
                                    value = row.ActualResultValue
                                })
                                .ToArray(),
                            sameSeasonPlayerResultCount = sameSeasonPlayerResults.Length,
                            sameSeasonPlayerPropResultCount = sameSeasonPlayerPropResults.Length,
                            recentSameSeasonPlayerPropResults = sameSeasonPlayerPropResults
                                .OrderByDescending(row => row.EventDate)
                                .Take(5)
                                .Select(row => new
                                {
                                    propResultId = row.Id,
                                    row.EventDate,
                                    row.PlayerId,
                                    row.PlayerName,
                                    row.PropTypeId,
                                    row.PropType,
                                    value = row.ActualResultValue
                                })
                                .ToArray()
                        }
                    };
                })
                .Where(row => row.included && row.grade.Status is not ("Won" or "Lost" or "Pushed"))
                .Select(row => row.row)
                .ToArray();

            var groups = rows
                .GroupBy(row => new
                {
                    row.propType,
                    row.provider,
                    row.bookmaker,
                    row.entryProvider,
                    row.reasonCode
                })
                .Select(group => new
                {
                    group.Key.propType,
                    group.Key.provider,
                    group.Key.bookmaker,
                    group.Key.entryProvider,
                    group.Key.reasonCode,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.propType)
                .ThenBy(row => row.reasonCode)
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                sport = effectiveSport,
                date = effectiveDate,
                filters = new
                {
                    candidateOnly = effectiveCandidateOnly,
                    topPickOnly = effectiveTopPickOnly,
                    includeWatchlist = effectiveIncludeWatchlist,
                    includeResearch = effectiveIncludeResearch,
                    includeStale = effectiveIncludeStale
                },
                rowsInspected = snapshots.Length,
                noResultRows = rows.Length,
                reasonCodeCounts = rows
                    .GroupBy(row => row.reasonCode)
                    .ToDictionary(group => group.Key ?? "Unknown", group => group.Count()),
                groups,
                rows
            });
        })
        .WithName("RecommendationLedgerNoResultAuditDebug")
        .WithSummary("Read-only diagnostic grouping performance-eligible recommendation rows that did not grade.");

        endpoints.MapPost("/api/debug/recommendations/performance/reset-baseline", async (
            string? sport,
            bool? gradedOnly,
            int? take,
            string? reason,
            bool? dryRun,
            bool? candidateOnly,
            bool? topPickOnly,
            bool? includeWatchlist,
            bool? includeResearch,
            bool? includeStale,
            bool? includeExcluded,
            string? gradingMode,
            string? propType,
            string? provider,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var effectiveSport = string.IsNullOrWhiteSpace(sport) ? "MLB" : sport.Trim().ToUpperInvariant();
            var effectiveGradedOnly = gradedOnly ?? true;
            var effectiveTake = Math.Clamp(take ?? 6, 1, 100);
            var effectiveReason = string.IsNullOrWhiteSpace(reason) ? "PerformanceBaselineReset" : reason.Trim();
            var effectiveDryRun = dryRun ?? true;
            var effectiveGradingMode = string.IsNullOrWhiteSpace(gradingMode) ? "latestCompleted" : gradingMode.Trim();
            var effectiveCandidateOnly = candidateOnly ?? true;
            var effectiveTopPickOnly = topPickOnly ?? false;
            var effectiveIncludeWatchlist = includeWatchlist ?? false;
            var effectiveIncludeResearch = includeResearch ?? false;
            var effectiveIncludeStale = includeStale ?? false;
            var effectiveIncludeExcluded = includeExcluded ?? false;
            var selection = await PublicMlbDfsEndpoints.BuildPerformanceResetSelectionAsync(
                requestedSport: sport,
                effectiveSport,
                provider,
                propType,
                candidateOnly: effectiveCandidateOnly,
                topPickOnly: effectiveTopPickOnly,
                includeWatchlist: effectiveIncludeWatchlist,
                includeResearch: effectiveIncludeResearch,
                includeStale: effectiveIncludeStale,
                includeExcluded: effectiveIncludeExcluded,
                gradingMode: effectiveGradingMode,
                gradedOnly: effectiveGradedOnly,
                effectiveTake,
                dbContext,
                cancellationToken);
            var selectedIds = selection.Rows
                .Select(row => row.RecommendationSnapshotId)
                .ToArray();

            if (!effectiveDryRun && selectedIds.Length > 0)
            {
                var now = DateTimeOffset.UtcNow;
                var rowsToUpdate = await dbContext.RecommendationSnapshots
                    .Where(row => selectedIds.Contains(row.Id))
                    .ToArrayAsync(cancellationToken);
                foreach (var row in rowsToUpdate)
                {
                    row.IsExcludedFromPerformance = true;
                    row.PerformanceExclusionReason = effectiveReason;
                    row.PerformanceExcludedAtUtc = now;
                    row.PerformanceExcludedBy = "debug/reset-baseline";
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = effectiveDryRun,
                sport = effectiveSport,
                selection.LatestCompletedDate,
                take = effectiveTake,
                gradedOnly = effectiveGradedOnly,
                selectionSource = "PerformanceSummaryFilteredRows",
                dryRun = effectiveDryRun,
                reason = effectiveReason,
                affectedCount = selectedIds.Length,
                recommendationSnapshotIds = selectedIds,
                filtersApplied = selection.FiltersApplied,
                selection.GradedRowsAvailableBeforeTake,
                selection.AlreadyExcludedRowsSkipped,
                publicPerformanceBeforeReset = selection.PublicPerformanceBeforeReset,
                expectedPublicPerformanceAfterReset = selection.ExpectedPublicPerformanceAfterReset,
                rows = selection.Rows.Select(row => new
                {
                    recommendationSnapshotId = row.RecommendationSnapshotId,
                    row.PlayerName,
                    row.PropType,
                    row.Provider,
                    row.Side,
                    row.Line,
                    row.FinalGradeStatus,
                    row.CapturedAtUtc,
                    row.ExpectedResultDate,
                    reason = effectiveReason,
                    row.AlreadyExcluded,
                    row.PerformanceExclusionReason,
                    row.PerformanceExcludedAtUtc,
                    row.PerformanceExcludedBy
                }).ToArray()
            });
        })
        .WithName("RecommendationPerformanceResetBaselineDebug")
        .WithSummary("Debug-only soft exclusion for polluted recommendation snapshot performance baselines.");

        endpoints.MapGet("/api/debug/recommendations/pipeline-audit", async (
            string? sport,
            DateOnly? fromDate,
            DateOnly? toDate,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var effectiveSport = string.IsNullOrWhiteSpace(sport) ? "MLB" : sport.Trim().ToUpperInvariant();
            var to = toDate ?? await dbContext.PropResults
                .AsNoTracking()
                .Select(row => (DateOnly?)row.EventDate)
                .MaxAsync(cancellationToken) ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var from = fromDate ?? to.AddDays(-7);

            var propResults = await dbContext.PropResults
                .AsNoTracking()
                .Where(row => row.EventDate >= from.AddDays(-1) && row.EventDate <= to.AddDays(1))
                .Select(row => new AuditPropResultRow(
                    row.Id,
                    row.EventDate,
                    row.PlayerId,
                    row.PropTypeId,
                    row.PropType != null ? row.PropType.Name : string.Empty,
                    row.ActualResultValue))
                .ToArrayAsync(cancellationToken);
            var importedDates = propResults
                .Where(row => row.EventDate >= from && row.EventDate <= to)
                .Select(row => row.EventDate)
                .Distinct()
                .ToHashSet();

            var predictionRuns = await dbContext.PredictionRuns
                .AsNoTracking()
                .Where(row => DateOnly.FromDateTime(row.CreatedAt.UtcDateTime) >= from && DateOnly.FromDateTime(row.CreatedAt.UtcDateTime) <= to)
                .Select(row => new
                {
                    runDate = DateOnly.FromDateTime(row.CreatedAt.UtcDateTime),
                    row.Id,
                    predictionCount = row.Predictions.Count
                })
                .ToArrayAsync(cancellationToken);
            var predictions = await dbContext.Predictions
                .AsNoTracking()
                .Where(row => row.Prop != null
                    && row.Prop.StartsAt >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    && row.Prop.StartsAt < new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    && row.Prop.League != null
                    && row.Prop.League.Code == effectiveSport)
                .Select(row => new
                {
                    eventDate = DateOnly.FromDateTime(row.Prop!.StartsAt.UtcDateTime),
                    propType = row.Prop.PropType != null ? row.Prop.PropType.Name : string.Empty,
                    row.Recommendation,
                    row.Edge,
                    row.Confidence
                })
                .ToArrayAsync(cancellationToken);
            var providerSnapshots = await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(row => row.Sport == effectiveSport && row.SlateDate >= from && row.SlateDate <= to)
                .Select(row => new
                {
                    date = row.SlateDate,
                    row.PropType,
                    row.IsCandidate,
                    row.IsWatchlist,
                    isTopPick = false,
                    row.EntryActionableForUser
                })
                .Concat(dbContext.MarketTopPickSnapshots
                    .AsNoTracking()
                    .Where(row => row.Sport == effectiveSport && row.SlateDate >= from && row.SlateDate <= to)
                    .Select(row => new
                    {
                        date = row.SlateDate,
                        row.PropType,
                        row.IsCandidate,
                        row.IsWatchlist,
                        isTopPick = true,
                        row.EntryActionableForUser
                    }))
                .ToArrayAsync(cancellationToken);
            var ledgerRows = await dbContext.RecommendationSnapshots
                .AsNoTracking()
                .Where(row => row.Sport == effectiveSport && row.ExpectedResultDate >= from && row.ExpectedResultDate <= to)
                .ToArrayAsync(cancellationToken);
            var gradedLedgerRows = ledgerRows
                .Select(row => new
                {
                    Snapshot = row,
                    Grade = AuditGradeRecommendation(row, propResults, importedDates)
                })
                .ToArray();

            var auditKeys = propResults.Select(row => new { Date = row.EventDate, PropType = row.PropType })
                .Concat(predictions.Select(row => new { Date = row.eventDate, PropType = row.propType }))
                .Concat(providerSnapshots.Select(row => new { Date = row.date, PropType = row.PropType }))
                .Concat(ledgerRows.Select(row => new { Date = row.ExpectedResultDate, row.PropType }))
                .Select(row => new { row.Date, NormalizedPropType = NormalizeDiagnosticKey(row.PropType), row.PropType })
                .Where(row => !string.IsNullOrWhiteSpace(row.NormalizedPropType))
                .GroupBy(row => new { row.Date, row.NormalizedPropType })
                .Select(group => new
                {
                    group.Key.Date,
                    group.Key.NormalizedPropType,
                    PropType = group.Select(row => row.PropType).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key.NormalizedPropType
                })
                .OrderBy(row => row.Date)
                .ThenBy(row => row.PropType)
                .ToArray();

            var rows = auditKeys.Select(key =>
            {
                var datePropResults = propResults
                    .Where(row => row.EventDate == key.Date && NormalizeDiagnosticKey(row.PropType) == key.NormalizedPropType)
                    .ToArray();
                var datePredictions = predictions
                    .Where(row => row.eventDate == key.Date && NormalizeDiagnosticKey(row.propType) == key.NormalizedPropType)
                    .ToArray();
                var dateSnapshots = providerSnapshots
                    .Where(row => row.date == key.Date && NormalizeDiagnosticKey(row.PropType) == key.NormalizedPropType)
                    .ToArray();
                var dateLedger = gradedLedgerRows
                    .Where(row => row.Snapshot.ExpectedResultDate == key.Date && NormalizeDiagnosticKey(row.Snapshot.PropType) == key.NormalizedPropType)
                    .ToArray();

                return new
                {
                    date = key.Date,
                    propType = key.PropType,
                    normalizedPropType = key.NormalizedPropType,
                    importedPropResults = datePropResults.Length,
                    distinctPlayersWithPropResults = datePropResults.Select(row => row.PlayerId).Distinct().Count(),
                    pitcherStrikeoutsResultCount = key.NormalizedPropType == NormalizeDiagnosticKey("Pitcher Strikeouts") ? datePropResults.Length : 0,
                    totalBasesResultCount = key.NormalizedPropType == NormalizeDiagnosticKey("Total Bases") ? datePropResults.Length : 0,
                    predictionRunCount = predictionRuns.Count(row => row.runDate == key.Date),
                    predictionCount = datePredictions.Length,
                    providerLineSnapshotCount = dateSnapshots.Length,
                    recommendationSnapshotCount = dateLedger.Length,
                    capturedCandidateCount = dateLedger.Count(row => row.Snapshot.IsCandidate),
                    capturedTopPickCount = dateLedger.Count(row => row.Snapshot.IsTopPick),
                    capturedWatchlistCount = dateLedger.Count(row => row.Snapshot.IsWatchlist),
                    capturedStaleNonPlayableCount = dateLedger.Count(row => IsStaleOrNonPlayable(row.Snapshot)),
                    candidateSnapshotCount = dateLedger.Count(row => row.Snapshot.IsCandidate),
                    topPickSnapshotCount = dateLedger.Count(row => row.Snapshot.IsTopPick),
                    watchlistSnapshotCount = dateLedger.Count(row => row.Snapshot.IsWatchlist),
                    gradeableSnapshotCount = dateLedger.Count(row => row.Grade.Status is "Won" or "Lost" or "Pushed"),
                    gradedCount = dateLedger.Count(row => row.Grade.Status is "Won" or "Lost" or "Pushed"),
                    gradedCandidateCount = dateLedger.Count(row => row.Snapshot.IsCandidate && row.Grade.Status is "Won" or "Lost" or "Pushed"),
                    noResultCandidateCount = dateLedger.Count(row => row.Snapshot.IsCandidate && row.Grade.Status == "NoResult"),
                    providerLineForInactivePlayerCount = dateLedger.Count(row => row.Grade.ReasonCode == "ProviderLineForInactivePlayer"),
                    eventDateMismatchCount = dateLedger.Count(row => row.Grade.ReasonCode == "EventDateMismatch"),
                    matchedAndGradedCount = dateLedger.Count(row => row.Grade.ReasonCode == "MatchedAndGraded"),
                    unknownCount = dateLedger.Count(row => row.Grade.ReasonCode == "Unknown"),
                    noResultCount = dateLedger.Count(row => row.Grade.Status == "NoResult"),
                    unmatchedCount = dateLedger.Count(row => row.Grade.Status == "Unmatched"),
                    pendingCount = dateLedger.Count(row => row.Grade.Status == "Pending"),
                    reasonCodeRollups = dateLedger
                        .GroupBy(row => row.Grade.ReasonCode)
                        .Select(group => new { reasonCode = group.Key, count = group.Count() })
                        .OrderByDescending(row => row.count)
                        .ThenBy(row => row.reasonCode)
                        .ToArray()
                };
            }).ToArray();

            var modelBuckets = gradedLedgerRows
                .GroupBy(row => new
                {
                    PropType = row.Snapshot.PropType,
                    row.Snapshot.TrustLevel,
                    CandidateBucket = row.Snapshot.IsTopPick ? "TopPick" : row.Snapshot.IsCandidate ? "Candidate" : row.Snapshot.IsWatchlist ? "Watchlist" : "Other",
                    HitChanceBucket = BucketPercent(row.Snapshot.HitChance),
                    EdgeBucket = BucketPercent(row.Snapshot.ProbabilityEdge),
                    EvBucket = BucketPercent(row.Snapshot.EstimatedEv)
                })
                .Select(group =>
                {
                    var groupRows = group.ToArray();
                    var gradeable = groupRows.Where(row => row.Grade.Status is "Won" or "Lost" or "Pushed").ToArray();
                    var wins = gradeable.Count(row => row.Grade.Status == "Won");
                    var losses = gradeable.Count(row => row.Grade.Status == "Lost");
                    var pushes = gradeable.Count(row => row.Grade.Status == "Pushed");
                    var decisions = wins + losses;
                    var profit = wins - losses;
                    return new
                    {
                        group.Key.PropType,
                        group.Key.TrustLevel,
                        group.Key.CandidateBucket,
                        group.Key.HitChanceBucket,
                        group.Key.EdgeBucket,
                        group.Key.EvBucket,
                        totalCaptured = groupRows.Length,
                        gradeable = gradeable.Length,
                        won = wins,
                        lost = losses,
                        pushed = pushes,
                        noResult = groupRows.Count(row => row.Grade.Status == "NoResult"),
                        unmatched = groupRows.Count(row => row.Grade.Status == "Unmatched"),
                        pending = groupRows.Count(row => row.Grade.Status == "Pending"),
                        hitRate = decisions == 0 ? (decimal?)null : Math.Round(wins / (decimal)decisions, 4),
                        averageEv = AverageNullable(groupRows.Select(row => row.Snapshot.EstimatedEv)),
                        realizedRoi = gradeable.Length == 0 ? (decimal?)null : Math.Round(profit / (decimal)gradeable.Length, 4)
                    };
                })
                .OrderByDescending(row => row.gradeable)
                .ThenBy(row => row.PropType)
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                sport = effectiveSport,
                fromDate = from,
                toDate = to,
                rows,
                modelBuckets,
                notes = new[]
                {
                    "This endpoint audits persisted prop_results, predictions, provider snapshots, and recommendation_snapshots only.",
                    "NoResult, Unmatched, and Pending are not counted as losses.",
                    "Provider, side, and line are not used to find result rows; side and line are grading inputs after a result row is found."
                }
            });
        })
        .WithName("RecommendationPipelineAuditDebug")
        .WithSummary("Read-only diagnostic for the prediction-to-performance pipeline by date and prop type.");

        endpoints.MapPost("/api/debug/recommendations/ledger/backfill", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? source,
            bool? dryRun,
            bool? execute,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var from = fromDate ?? DateOnly.MinValue;
            var to = toDate ?? DateOnly.MaxValue;
            var shouldExecute = execute == true && dryRun != true;
            var candidates = new List<RecommendationLedgerCandidate>();
            var requestedSource = string.IsNullOrWhiteSpace(source) ? "marketSnapshots" : source.Trim();
            var resultPropTypes = await dbContext.PropResults
                .AsNoTracking()
                .Where(row => row.EventDate >= from && row.EventDate <= to && row.PropType != null)
                .Select(row => new { row.PropTypeId, row.PropType!.Name, row.PropType.StatKey })
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var resultPropTypeIdsByName = resultPropTypes
                .SelectMany(row => new[]
                {
                    new { Key = RecommendationLedgerWriter.NormalizeKey(row.Name), row.PropTypeId },
                    new { Key = RecommendationLedgerWriter.NormalizeKey(row.StatKey), row.PropTypeId }
                })
                .Where(row => row.Key != string.Empty)
                .GroupBy(row => row.Key)
                .Where(group => group.Select(row => row.PropTypeId).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => (Guid?)group.Select(row => row.PropTypeId).First());
            var propTypes = await dbContext.PropTypes
                .AsNoTracking()
                .Select(row => new { row.Id, row.Name, row.StatKey })
                .ToArrayAsync(cancellationToken);
            var propTypeIdsByName = propTypes
                .SelectMany(row => new[]
                {
                    new { Key = RecommendationLedgerWriter.NormalizeKey(row.Name), row.Id },
                    new { Key = RecommendationLedgerWriter.NormalizeKey(row.StatKey), row.Id }
                })
                .Where(row => row.Key != string.Empty)
                .GroupBy(row => row.Key)
                .Where(group => group.Key != string.Empty && group.Select(row => row.Id).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => (Guid?)group.Select(row => row.Id).First());
            var resultPlayers = await dbContext.PropResults
                .AsNoTracking()
                .Where(row => row.EventDate >= from && row.EventDate <= to && row.Player != null)
                .Select(row => new { row.PlayerId, row.Player!.DisplayName })
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var resultPlayerIdsByName = resultPlayers
                .GroupBy(row => RecommendationLedgerWriter.NormalizeKey(row.DisplayName))
                .Where(group => group.Key != string.Empty && group.Select(row => row.PlayerId).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => (Guid?)group.Select(row => row.PlayerId).First());
            var players = await dbContext.Players
                .AsNoTracking()
                .Where(row => row.League != null && row.League.Code == "MLB")
                .Select(row => new { row.Id, row.DisplayName })
                .ToArrayAsync(cancellationToken);
            var playerIdsByName = players
                .GroupBy(row => RecommendationLedgerWriter.NormalizeKey(row.DisplayName))
                .Where(group => group.Key != string.Empty && group.Select(row => row.Id).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => (Guid?)group.Select(row => row.Id).First());

            if (requestedSource.Equals("marketSnapshots", StringComparison.OrdinalIgnoreCase) ||
                requestedSource.Equals("both", StringComparison.OrdinalIgnoreCase))
            {
                var snapshots = await dbContext.MarketLineShoppingSnapshots
                    .AsNoTracking()
                    .Where(row => row.SlateDate >= from && row.SlateDate <= to && (row.IsCandidate || row.IsWatchlist))
                    .ToArrayAsync(cancellationToken);
                foreach (var snapshot in snapshots)
                {
                    candidates.Add(MapSnapshotToLedgerCandidate(snapshot, isTopPick: false, playerIdsByName, resultPlayerIdsByName, propTypeIdsByName, resultPropTypeIdsByName));
                }

                var topPicks = await dbContext.MarketTopPickSnapshots
                    .AsNoTracking()
                    .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                    .ToArrayAsync(cancellationToken);
                foreach (var snapshot in topPicks)
                {
                    candidates.Add(MapSnapshotToLedgerCandidate(snapshot, isTopPick: true, playerIdsByName, resultPlayerIdsByName, propTypeIdsByName, resultPropTypeIdsByName));
                }
            }

            var result = await RecommendationLedgerWriter.UpsertAsync(
                dbContext,
                candidates,
                shouldExecute,
                cancellationToken);

            return Results.Ok(new
            {
                dryRun = !shouldExecute,
                execute = shouldExecute,
                source = requestedSource,
                fromDate,
                toDate,
                result.CandidateRowsFound,
                result.RowsEligibleForLedger,
                rowsAlreadyExist = result.RowsAlreadyExist,
                rowsToCreate = shouldExecute ? 0 : result.RowsCreated,
                rowsCreated = shouldExecute ? result.RowsCreated : 0,
                result.RowsUpdated,
                result.SkippedMissingPlayerId,
                result.SkippedMissingExpectedResultDate,
                result.SkippedMissingPropType,
                result.SkippedAmbiguousPlayer,
                dateBreakdown = result.DateBreakdown.Values
            });
        });

        return endpoints;
    }

    private static DateOnly ToEasternDate(DateTimeOffset value)
    {
        var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, easternZone).DateTime);
    }

    private static object BuildResultCoverageExample(
        RecommendationSnapshot snapshot,
        IReadOnlyCollection<CoveragePropResultRow> resultRows,
        IReadOnlyCollection<CoveragePropResultRow> historicalRows)
    {
        var normalizedPlayerName = string.IsNullOrWhiteSpace(snapshot.NormalizedPlayerName)
            ? NormalizeDiagnosticKey(snapshot.PlayerName)
            : NormalizeDiagnosticKey(snapshot.NormalizedPlayerName);
        var normalizedPropType = string.IsNullOrWhiteSpace(snapshot.NormalizedPropType)
            ? NormalizeDiagnosticKey(snapshot.PropType)
            : NormalizeDiagnosticKey(snapshot.NormalizedPropType);
        var nearbyDates = new[] { snapshot.ExpectedResultDate.AddDays(-1), snapshot.ExpectedResultDate, snapshot.ExpectedResultDate.AddDays(1) };
        var nearbyRows = resultRows.Where(row => nearbyDates.Contains(row.EventDate)).ToArray();
        var sameDateRows = nearbyRows.Where(row => row.EventDate == snapshot.ExpectedResultDate).ToArray();
        var samePlayerAnyProp = sameDateRows
            .Where(row => PlayerMatches(snapshot, row, normalizedPlayerName))
            .ToArray();
        var samePlayerSameProp = samePlayerAnyProp
            .Where(row => PropTypeMatches(snapshot, row, normalizedPropType))
            .ToArray();
        var sameNormalizedNameAnyProp = sameDateRows
            .Where(row => !string.IsNullOrWhiteSpace(normalizedPlayerName) && row.NormalizedPlayerName == normalizedPlayerName)
            .ToArray();
        var sameNormalizedNameSameProp = sameNormalizedNameAnyProp
            .Where(row => PropTypeMatches(snapshot, row, normalizedPropType))
            .ToArray();
        var samePropTypeAnyPlayer = sameDateRows
            .Where(row => PropTypeMatches(snapshot, row, normalizedPropType))
            .ToArray();
        var nearbyDateSamePlayerSameProp = nearbyRows
            .Where(row => row.EventDate != snapshot.ExpectedResultDate
                && PlayerMatches(snapshot, row, normalizedPlayerName)
                && PropTypeMatches(snapshot, row, normalizedPropType))
            .ToArray();
        var exactMatches = samePlayerSameProp
            .Concat(sameNormalizedNameSameProp)
            .GroupBy(row => row.Id)
            .Select(group => group.First())
            .ToArray();
        var likelyReason = ResolveCoverageReason(
            snapshot,
            sameDateRows,
            samePlayerAnyProp,
            samePlayerSameProp,
            sameNormalizedNameAnyProp,
            sameNormalizedNameSameProp,
            samePropTypeAnyPlayer,
            nearbyDateSamePlayerSameProp);
        var finalGradeStatus = exactMatches.Length == 1
            ? GradeCoverageStatus(snapshot.Side, snapshot.Line, exactMatches[0].ActualResultValue)
            : likelyReason == "RawBoxscoreUnavailable"
                ? "Pending"
                : exactMatches.Length > 1
                    ? "Unmatched"
                    : "NoResult";
        var reasonCode = exactMatches.Length == 1 && finalGradeStatus is "Won" or "Lost" or "Pushed"
            ? "MatchedAndGraded"
            : exactMatches.Length > 1
                ? "MultipleResultRowsForPlayerPropDate"
                : likelyReason;
        var historicalMatches = historicalRows
            .Where(row => row.EventDate < snapshot.ExpectedResultDate
                && PlayerMatches(snapshot, row, normalizedPlayerName)
                && PropTypeMatches(snapshot, row, normalizedPropType))
            .OrderByDescending(row => row.EventDate)
            .ToArray();
        var historicalGamesUsed = historicalMatches.Take(10).ToArray();
        var historicalHitCount = historicalGamesUsed.Count(row => SideClears(snapshot.Side, snapshot.Line, row.ActualResultValue) == true);
        var historicalValues = historicalGamesUsed.Select(row => row.ActualResultValue).ToArray();
        var isResearch = !snapshot.IsCandidate && !snapshot.IsTopPick && !snapshot.IsWatchlist;
        var isStale = !string.IsNullOrWhiteSpace(snapshot.Warning) && snapshot.Warning.Contains("stale", StringComparison.OrdinalIgnoreCase);
        var isLocked = string.Equals(snapshot.LifecycleStatus, "Locked", StringComparison.OrdinalIgnoreCase);
        var isExpired = string.Equals(snapshot.LifecycleStatus, "Expired", StringComparison.OrdinalIgnoreCase);
        var isPlayable = snapshot.IsActionableForUser &&
            string.Equals(snapshot.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase) &&
            !isStale;

        return new
        {
            capturedPick = new
            {
                recommendationSnapshotId = snapshot.Id,
                snapshot.CapturedAtUtc,
                snapshot.FirstSeenAtUtc,
                snapshot.LastSeenAtUtc,
                snapshot.ExpectedResultDate,
                snapshot.SlateDate,
                snapshot.EventId,
                snapshot.EventStartTimeUtc,
                eventUtcDate = snapshot.EventStartTimeUtc.HasValue ? DateOnly.FromDateTime(snapshot.EventStartTimeUtc.Value.UtcDateTime) : (DateOnly?)null,
                eventEasternDate = snapshot.EventStartTimeUtc.HasValue ? ToEasternDate(snapshot.EventStartTimeUtc.Value) : (DateOnly?)null,
                eventUtcDateDiffersFromExpectedResultDate = snapshot.EventStartTimeUtc.HasValue && DateOnly.FromDateTime(snapshot.EventStartTimeUtc.Value.UtcDateTime) != snapshot.ExpectedResultDate,
                eventEasternDateDiffersFromExpectedResultDate = snapshot.EventStartTimeUtc.HasValue && ToEasternDate(snapshot.EventStartTimeUtc.Value) != snapshot.ExpectedResultDate,
                playerName = snapshot.PlayerName,
                playerId = snapshot.CanonicalPlayerId,
                canonicalPlayerId = snapshot.CanonicalPlayerId,
                normalizedPlayerName,
                snapshot.Provider,
                snapshot.EntryProvider,
                snapshot.Bookmaker,
                propType = snapshot.PropType,
                propTypeId = snapshot.PropTypeId,
                normalizedPropType,
                snapshot.Side,
                snapshot.Line,
                hitChance = snapshot.HitChance,
                modelProbability = snapshot.HitChance,
                edge = snapshot.ProbabilityEdge,
                probabilityEdge = snapshot.ProbabilityEdge,
                ev = snapshot.EstimatedEv,
                estimatedEv = snapshot.EstimatedEv,
                trust = snapshot.TrustLevel,
                confidenceSourceValue = snapshot.TrustLevel,
                sampleWarning = snapshot.SampleWarning,
                warning = snapshot.Warning,
                historicalSampleSize = snapshot.HistoricalSampleSize,
                snapshot.IsCandidate,
                snapshot.IsTopPick,
                snapshot.IsWatchlist,
                snapshot.IsExcludedFromPerformance,
                snapshot.PerformanceExclusionReason,
                snapshot.PerformanceExcludedAtUtc,
                snapshot.PerformanceExcludedBy,
                isResearch,
                isStale,
                isLocked,
                isExpired,
                isPlayable,
                isActionable = snapshot.IsActionableForUser,
                snapshot.IsActionableForUser,
                snapshot.LifecycleStatus,
                includedInDefaultPerformance = !snapshot.IsExcludedFromPerformance && IsIncludedInPerformance(snapshot, candidateOnly: true, topPickOnly: false, includeWatchlist: false, includeResearch: false, includeStale: false),
                includedInCandidateOnlyPerformance = !snapshot.IsExcludedFromPerformance && IsIncludedInPerformance(snapshot, candidateOnly: true, topPickOnly: false, includeWatchlist: false, includeResearch: false, includeStale: false),
                includedInTopPickOnlyPerformance = !snapshot.IsExcludedFromPerformance && IsIncludedInPerformance(snapshot, candidateOnly: false, topPickOnly: true, includeWatchlist: false, includeResearch: false, includeStale: false),
                exclusionReasons = BuildPerformanceExclusionReasons(snapshot, candidateOnly: true, topPickOnly: false, includeWatchlist: false, includeResearch: false, includeStale: false),
                source = snapshot.RefreshRunId.HasValue
                    ? "the_odds_refresh"
                    : snapshot.PredictionRunId.HasValue
                        ? "prediction_run"
                        : "unknown_or_backfill",
                isBackfilled = !snapshot.RefreshRunId.HasValue && !snapshot.PredictionRunId.HasValue
            },
            importedResultChecks = new
            {
                propResultsForDate = sameDateRows.Length,
                resultDateImported = sameDateRows.Length > 0,
                sameDatePlayerIdResultCount = snapshot.CanonicalPlayerId.HasValue
                    ? sameDateRows.Count(row => row.PlayerId == snapshot.CanonicalPlayerId.Value)
                    : 0,
                sameDateNormalizedNameResultCount = sameNormalizedNameAnyProp.Length,
                sameDatePlayerPropResultCount = samePlayerSameProp.Length,
                nearbyDatePlayerPropResultCount = nearbyDateSamePlayerSameProp.Length,
                nearbyDatePlayerPropMatches = nearbyDateSamePlayerSameProp
                    .OrderBy(row => Math.Abs(row.EventDate.DayNumber - snapshot.ExpectedResultDate.DayNumber))
                    .ThenBy(row => row.EventDate)
                    .Select(row => new
                    {
                        propResultId = row.Id,
                        row.EventDate,
                        expectedResultDate = snapshot.ExpectedResultDate,
                        dateDistanceDays = row.EventDate.DayNumber - snapshot.ExpectedResultDate.DayNumber,
                        row.PlayerId,
                        row.PlayerName,
                        row.PropType,
                        row.PropTypeId,
                        value = row.ActualResultValue
                    })
                    .ToArray(),
                samePlayerAnyProp = samePlayerAnyProp.Length,
                samePlayerSameProp = samePlayerSameProp.Length,
                sameNormalizedNameAnyProp = sameNormalizedNameAnyProp.Length,
                sameNormalizedNameSameProp = sameNormalizedNameSameProp.Length,
                samePropTypeAnyPlayer = samePropTypeAnyPlayer.Length,
                nearbyDateSamePlayerSameProp = nearbyDateSamePlayerSameProp.Length,
                matchedPropResultIds = exactMatches.Select(row => row.Id).ToArray(),
                matchedStatValue = exactMatches.Length == 1 ? exactMatches[0].ActualResultValue : (decimal?)null,
                closestPlayerNameMatches = BuildClosestPlayerNameMatches(snapshot, sameDateRows, normalizedPlayerName),
                closestSamePropTypeRows = BuildClosestSamePropTypeRows(snapshot, samePropTypeAnyPlayer, normalizedPlayerName)
            },
            predictionFeatureQuality = new
            {
                historicalSampleSizeStored = snapshot.HistoricalSampleSize,
                historicalGamesAvailableFromPropResults = historicalMatches.Length,
                historicalGamesUsed = historicalGamesUsed.Length,
                historicalHitCountForSameSideAndLine = historicalHitCount,
                averageStatValue = historicalValues.Length == 0 ? (decimal?)null : Math.Round(historicalValues.Average(), 4),
                recentStatValues = historicalValues,
                featureSourceHadEnoughData = snapshot.HistoricalSampleSize.GetValueOrDefault() > 0 || historicalGamesUsed.Length > 0,
                playerIdWasCanonical = snapshot.CanonicalPlayerId.HasValue,
                propTypeWasNormalized = !string.IsNullOrWhiteSpace(normalizedPropType),
                fallbackUsed = snapshot.CanonicalPlayerId.HasValue ? null : "MissingCanonicalPlayerId"
            },
            rawMlbImportedDataChecks = new
            {
                playerAppearsInImportedGameBoxscoreRawData = "unknown",
                playerAppearsInBattingStats = "unknown",
                playerAppearsInPitchingStats = "unknown",
                playerTeam = "unknown",
                gameId = snapshot.EventId,
                statLine = "unknown",
                dnpOrNotInBoxscoreIndicator = "unknown",
                reason = "Raw MLB boxscore payloads are not persisted; only derived prop_results can be inspected without calling MLB APIs."
            },
            finalGradeStatus,
            reasonCode,
            userFacingReason = BuildCoverageInterpretation(snapshot, reasonCode),
            likelyReason = reasonCode,
            interpretation = BuildCoverageInterpretation(snapshot, reasonCode)
        };
    }

    private static string ResolveCoverageReason(
        RecommendationSnapshot snapshot,
        IReadOnlyCollection<CoveragePropResultRow> sameDateRows,
        IReadOnlyCollection<CoveragePropResultRow> samePlayerAnyProp,
        IReadOnlyCollection<CoveragePropResultRow> samePlayerSameProp,
        IReadOnlyCollection<CoveragePropResultRow> sameNormalizedNameAnyProp,
        IReadOnlyCollection<CoveragePropResultRow> sameNormalizedNameSameProp,
        IReadOnlyCollection<CoveragePropResultRow> samePropTypeAnyPlayer,
        IReadOnlyCollection<CoveragePropResultRow> nearbyDateSamePlayerSameProp)
    {
        if (nearbyDateSamePlayerSameProp.Count > 0)
        {
            return "EventDateMismatch";
        }

        if (samePlayerSameProp.Count > 0 || sameNormalizedNameSameProp.Count > 0)
        {
            return "Unknown";
        }

        if (samePlayerAnyProp.Count > 0 || sameNormalizedNameAnyProp.Count > 0)
        {
            return "NoResultForPlayerPropOnDate";
        }

        if (samePropTypeAnyPlayer.Count > 0)
        {
            return snapshot.CanonicalPlayerId.HasValue
                ? "ProviderLineForInactivePlayer"
                : "PlayerIdentityMismatch";
        }

        return sameDateRows.Count == 0 ? "RawBoxscoreUnavailable" : "PlayerDidNotAppearInBoxscore";
    }

    private static string BuildCoverageInterpretation(RecommendationSnapshot snapshot, string likelyReason)
    {
        return likelyReason switch
        {
            "MatchedAndGraded" => "A matching imported result row exists for this captured pick by date, player, and prop, so the side and line were graded against that result.",
            "Won" => "A matching imported result row exists and the captured side cleared the selected line.",
            "Lost" => "A matching imported result row exists and the captured side missed the selected line.",
            "Pushed" => "A matching imported result row exists and landed exactly on the selected line.",
            "MultipleResultRowsForPlayerPropDate" => "More than one imported result row matched this captured pick by date, player, and prop. Treat this as unmatched until duplicate result rows are resolved.",
            "EventDateMismatch" => "A matching player and prop exists on a nearby imported date, so inspect event/result-date assignment before changing grading.",
            "NoResultForPlayerPropOnDate" => "The player has at least one imported result on the completed date, but not this prop type. If they appeared in the relevant batting/pitching role, importer coverage may be missing a zero or category row.",
            "ProviderLineForInactivePlayer" => "The completed date has imported rows for this prop type, but not this player by id or normalized name. This commonly means the provider offered a stale/inactive player line or the player did not appear in the MLB boxscore.",
            "PlayerIdentityMismatch" => "The completed date has imported rows for this prop type, but the captured ledger row lacks a safe canonical player identity match.",
            "PlayerDidNotAppearInBoxscore" => "No persisted prop_results rows exist for this player by id or normalized name on the completed date. Without persisted raw boxscore rows, this should be treated as likely DNP/no-result until raw MLB data proves otherwise.",
            "RawBoxscoreUnavailable" => "No prop_results were imported for the searched date, so raw import status must be checked before diagnosing this row.",
            _ => "The persisted result coverage is inconclusive."
        };
    }

    private static AuditGradeResult AuditGradeRecommendation(
        RecommendationSnapshot snapshot,
        IReadOnlyCollection<AuditPropResultRow> propResults,
        IReadOnlySet<DateOnly> importedDates)
    {
        if (!importedDates.Contains(snapshot.ExpectedResultDate))
        {
            return new AuditGradeResult("Pending", "NoPropResultsForDate");
        }

        if (!snapshot.CanonicalPlayerId.HasValue)
        {
            return new AuditGradeResult("NoResult", "MissingCanonicalPlayerId");
        }

        if (!snapshot.PropTypeId.HasValue && string.IsNullOrWhiteSpace(snapshot.NormalizedPropType) && string.IsNullOrWhiteSpace(snapshot.PropType))
        {
            return new AuditGradeResult("NoResult", "MissingLedgerPropType");
        }

        var normalizedPropType = string.IsNullOrWhiteSpace(snapshot.NormalizedPropType)
            ? NormalizeDiagnosticKey(snapshot.PropType)
            : NormalizeDiagnosticKey(snapshot.NormalizedPropType);
        if (string.IsNullOrWhiteSpace(normalizedPropType))
        {
            return new AuditGradeResult("NoResult", "MissingLedgerPropType");
        }

        if (!SupportedMlbPerformancePropTypeKeys.Contains(normalizedPropType))
        {
            return new AuditGradeResult("NoResult", "UnsupportedPropType");
        }

        if (string.IsNullOrWhiteSpace(snapshot.EventId) || !snapshot.EventStartTimeUtc.HasValue || snapshot.ExpectedResultDate == default)
        {
            return new AuditGradeResult("NoResult", "MissingEventDateMapping");
        }

        var sameDateRows = propResults
            .Where(row => row.EventDate == snapshot.ExpectedResultDate)
            .ToArray();
        var samePlayerRows = sameDateRows
            .Where(row => row.PlayerId == snapshot.CanonicalPlayerId.Value)
            .ToArray();
        var matches = samePlayerRows
            .Where(row => (snapshot.PropTypeId.HasValue && row.PropTypeId == snapshot.PropTypeId.Value)
                || (!string.IsNullOrWhiteSpace(normalizedPropType) && NormalizeDiagnosticKey(row.PropType) == normalizedPropType))
            .ToArray();

        if (matches.Length > 1)
        {
            return new AuditGradeResult("Unmatched", "MultipleResultRowsForPlayerPropDate");
        }

        if (matches.Length == 0)
        {
            var nearbyRows = propResults
                .Where(row => row.EventDate != snapshot.ExpectedResultDate
                    && Math.Abs(row.EventDate.DayNumber - snapshot.ExpectedResultDate.DayNumber) <= 1
                    && row.PlayerId == snapshot.CanonicalPlayerId.Value
                    && ((snapshot.PropTypeId.HasValue && row.PropTypeId == snapshot.PropTypeId.Value)
                        || (!string.IsNullOrWhiteSpace(normalizedPropType) && NormalizeDiagnosticKey(row.PropType) == normalizedPropType)))
                .ToArray();
            if (nearbyRows.Length > 0)
            {
                return new AuditGradeResult("NoResult", "EventDateMismatch");
            }

            if (samePlayerRows.Length > 0)
            {
                return new AuditGradeResult("NoResult", "NoResultForPlayerPropOnDate");
            }

            var samePropRows = sameDateRows
                .Where(row => (snapshot.PropTypeId.HasValue && row.PropTypeId == snapshot.PropTypeId.Value)
                    || (!string.IsNullOrWhiteSpace(normalizedPropType) && NormalizeDiagnosticKey(row.PropType) == normalizedPropType))
                .ToArray();
            return samePropRows.Length > 0
                ? new AuditGradeResult("NoResult", "ProviderLineForInactivePlayer")
                : new AuditGradeResult("NoResult", "NoResultForPlayerOnDate");
        }

        var gradeStatus = GradeCoverageStatus(snapshot.Side, snapshot.Line, matches[0].ActualResultValue);
        return new AuditGradeResult(
            gradeStatus ?? "Unmatched",
            gradeStatus is "Won" or "Lost" or "Pushed" ? "MatchedAndGraded" : "Unknown");
    }

    private static bool IsStaleOrNonPlayable(RecommendationSnapshot snapshot)
    {
        return !snapshot.IsActionableForUser ||
            !string.Equals(snapshot.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(snapshot.Warning) && snapshot.Warning.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIncludedInPerformance(
        RecommendationSnapshot snapshot,
        bool candidateOnly,
        bool topPickOnly,
        bool includeWatchlist,
        bool includeResearch,
        bool includeStale)
    {
        return BuildPerformanceExclusionReasons(
            snapshot,
            candidateOnly,
            topPickOnly,
            includeWatchlist,
            includeResearch,
            includeStale).Length == 0;
    }

    private static string[] BuildPerformanceExclusionReasons(
        RecommendationSnapshot snapshot,
        bool candidateOnly,
        bool topPickOnly,
        bool includeWatchlist,
        bool includeResearch,
        bool includeStale)
    {
        var reasons = new List<string>();
        if (topPickOnly && !snapshot.IsTopPick)
        {
            reasons.Add("NotTopPick");
        }
        else if (candidateOnly && !snapshot.IsCandidate)
        {
            reasons.Add("NotCandidate");
        }
        else if (!candidateOnly && !topPickOnly && !includeResearch && !snapshot.IsCandidate && !snapshot.IsTopPick && !(includeWatchlist && snapshot.IsWatchlist))
        {
            reasons.Add("NotCandidateTopPickOrIncludedWatchlist");
        }

        if (snapshot.IsWatchlist && !includeWatchlist)
        {
            reasons.Add("WatchlistExcluded");
        }

        if (!snapshot.IsCandidate && !snapshot.IsTopPick && !snapshot.IsWatchlist && !includeResearch)
        {
            reasons.Add("ResearchExcluded");
        }

        if (!includeStale)
        {
            if (!snapshot.IsActionableForUser)
            {
                reasons.Add("NonPlayableExcluded");
            }

            if (!string.Equals(snapshot.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"{snapshot.LifecycleStatus}Excluded");
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Warning) && snapshot.Warning.Contains("stale", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("StaleWarningExcluded");
            }
        }

        if (!snapshot.CanonicalPlayerId.HasValue)
        {
            reasons.Add("MissingCanonicalPlayerId");
        }

        var normalizedPropType = string.IsNullOrWhiteSpace(snapshot.NormalizedPropType)
            ? NormalizeDiagnosticKey(snapshot.PropType)
            : NormalizeDiagnosticKey(snapshot.NormalizedPropType);
        if (!snapshot.PropTypeId.HasValue || string.IsNullOrWhiteSpace(normalizedPropType))
        {
            reasons.Add("MissingSupportedPropType");
        }
        else if (!SupportedMlbPerformancePropTypeKeys.Contains(normalizedPropType))
        {
            reasons.Add("UnsupportedPropType");
        }

        if (string.IsNullOrWhiteSpace(snapshot.EventId) || !snapshot.EventStartTimeUtc.HasValue || snapshot.ExpectedResultDate == default)
        {
            reasons.Add("MissingEventDateMapping");
        }
        else if (ToEasternDate(snapshot.EventStartTimeUtc.Value) != snapshot.ExpectedResultDate)
        {
            reasons.Add("EventDateMappingMismatch");
        }

        return reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BucketPercent(decimal? value)
    {
        if (!value.HasValue)
        {
            return "Unknown";
        }

        var percent = value.Value <= 1m && value.Value >= -1m ? value.Value * 100m : value.Value;
        return percent switch
        {
            < 0m => "<0%",
            < 5m => "0-5%",
            < 10m => "5-10%",
            < 20m => "10-20%",
            < 40m => "20-40%",
            < 60m => "40-60%",
            < 80m => "60-80%",
            _ => "80%+"
        };
    }

    private static decimal? AverageNullable(IEnumerable<decimal?> values)
    {
        var numbers = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return numbers.Length == 0 ? null : Math.Round(numbers.Average(), 6);
    }

    private static string? GradeCoverageStatus(string side, decimal line, decimal actualResult)
    {
        return side switch
        {
            "Over" when actualResult > line => "Won",
            "Over" when actualResult < line => "Lost",
            "Under" when actualResult < line => "Won",
            "Under" when actualResult > line => "Lost",
            _ when actualResult == line => "Pushed",
            _ => null
        };
    }

    private static bool? SideClears(string side, decimal line, decimal actualResult)
    {
        return side switch
        {
            "Over" when actualResult > line => true,
            "Over" when actualResult < line => false,
            "Under" when actualResult < line => true,
            "Under" when actualResult > line => false,
            _ when actualResult == line => null,
            _ => null
        };
    }

    private static object[] BuildClosestPlayerNameMatches(
        RecommendationSnapshot snapshot,
        IReadOnlyCollection<CoveragePropResultRow> sameDateRows,
        string normalizedPlayerName)
    {
        if (string.IsNullOrWhiteSpace(normalizedPlayerName))
        {
            return [];
        }

        return sameDateRows
            .GroupBy(row => new { row.PlayerId, row.PlayerName, row.NormalizedPlayerName })
            .Select(group => new
            {
                group.Key.PlayerId,
                group.Key.PlayerName,
                group.Key.NormalizedPlayerName,
                distance = LevenshteinDistance(normalizedPlayerName, group.Key.NormalizedPlayerName),
                propTypes = group.Select(row => row.PropType).Distinct(StringComparer.OrdinalIgnoreCase).Order().Take(8).ToArray()
            })
            .OrderBy(row => row.distance)
            .ThenBy(row => row.PlayerName)
            .Take(8)
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildClosestSamePropTypeRows(
        RecommendationSnapshot snapshot,
        IReadOnlyCollection<CoveragePropResultRow> samePropTypeRows,
        string normalizedPlayerName)
    {
        return samePropTypeRows
            .Select(row => new
            {
                propResultId = row.Id,
                row.EventDate,
                row.PlayerName,
                row.PlayerId,
                row.PropType,
                row.PropTypeId,
                value = row.ActualResultValue,
                namesMatch = !string.IsNullOrWhiteSpace(normalizedPlayerName) && row.NormalizedPlayerName == normalizedPlayerName,
                playerIdsMatch = snapshot.CanonicalPlayerId.HasValue && row.PlayerId == snapshot.CanonicalPlayerId.Value,
                distance = string.IsNullOrWhiteSpace(normalizedPlayerName) ? int.MaxValue : LevenshteinDistance(normalizedPlayerName, row.NormalizedPlayerName)
            })
            .OrderBy(row => row.distance)
            .ThenBy(row => row.PlayerName)
            .Take(10)
            .Cast<object>()
            .ToArray();
    }

    private static bool PlayerMatches(RecommendationSnapshot snapshot, CoveragePropResultRow result, string normalizedPlayerName)
    {
        return (snapshot.CanonicalPlayerId.HasValue && result.PlayerId == snapshot.CanonicalPlayerId.Value)
            || (!string.IsNullOrWhiteSpace(normalizedPlayerName) && result.NormalizedPlayerName == normalizedPlayerName);
    }

    private static bool PropTypeMatches(RecommendationSnapshot snapshot, CoveragePropResultRow result, string normalizedPropType)
    {
        return (snapshot.PropTypeId.HasValue && result.PropTypeId == snapshot.PropTypeId.Value)
            || (!string.IsNullOrWhiteSpace(normalizedPropType) && result.NormalizedPropType == normalizedPropType);
    }

    private static string NormalizeDiagnosticKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record CoveragePropResultRow(
        Guid Id,
        DateOnly EventDate,
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropType,
        decimal ActualResultValue)
    {
        public string NormalizedPlayerName { get; } = NormalizeDiagnosticKey(PlayerName);
        public string NormalizedPropType { get; } = NormalizeDiagnosticKey(PropType);
    }

    private sealed record AuditPropResultRow(
        Guid Id,
        DateOnly EventDate,
        Guid PlayerId,
        Guid PropTypeId,
        string PropType,
        decimal ActualResultValue);

    private sealed record AuditGradeResult(
        string Status,
        string ReasonCode);

    private static RecommendationLedgerCandidate MapSnapshotToLedgerCandidate(
        MarketLineShoppingSnapshot snapshot,
        bool isTopPick,
        IReadOnlyDictionary<string, Guid?> playerIdsByName,
        IReadOnlyDictionary<string, Guid?> resultPlayerIdsByName,
        IReadOnlyDictionary<string, Guid?> propTypeIdsByName,
        IReadOnlyDictionary<string, Guid?> resultPropTypeIdsByName)
    {
        var playerId = ExtractGuid(snapshot.DetailsJson, "playerId")
            ?? ExtractGuid(snapshot.DetailsJson, "canonicalPlayerId")
            ?? ResolveNullableGuid(resultPlayerIdsByName, snapshot.Player)
            ?? ResolveNullableGuid(playerIdsByName, snapshot.Player);
        var propTypeId = ExtractGuid(snapshot.DetailsJson, "propTypeId")
            ?? ResolveNullableGuid(resultPropTypeIdsByName, snapshot.PropType)
            ?? ResolveNullableGuid(propTypeIdsByName, snapshot.PropType);
        var expectedResultDate = ExtractDate(snapshot.DetailsJson, "expectedResultDate") ?? snapshot.SlateDate;
        var resultDateSource = ExtractString(snapshot.DetailsJson, "resultDateSource") ?? "SnapshotSlateDateFallback";
        var hitChance = string.Equals(snapshot.BestSide, "Under", StringComparison.OrdinalIgnoreCase)
            ? ExtractDecimal(snapshot.DetailsJson, "modelProbabilityUnder") ?? snapshot.BestProbability
            : ExtractDecimal(snapshot.DetailsJson, "modelProbabilityOver") ?? snapshot.BestProbability;

        return new RecommendationLedgerCandidate(
            CapturedAtUtc: snapshot.GeneratedAtUtc,
            RefreshRunId: snapshot.TheOddsRefreshRunId,
            PredictionRunId: snapshot.PredictionRunId,
            Sport: snapshot.Sport,
            SlateDate: snapshot.SlateDate,
            ExpectedResultDate: expectedResultDate,
            ResultDateSource: resultDateSource,
            EventId: snapshot.EventId,
            EventStartTimeUtc: snapshot.EventStartTime,
            CanonicalPlayerId: playerId,
            PlayerName: snapshot.Player,
            PropTypeId: propTypeId,
            PropType: snapshot.PropType,
            Side: snapshot.BestSide,
            Line: snapshot.Line,
            Provider: snapshot.EntryProvider,
            Bookmaker: snapshot.BestBookmaker ?? snapshot.Bookmaker,
            ProviderCategory: snapshot.ProviderCategory,
            EntryProvider: snapshot.EntryProvider,
            EntryCompatibilityGroup: snapshot.EntryCompatibilityGroup,
            HitChance: hitChance,
            ImpliedProbability: snapshot.BestImpliedProbability,
            ProbabilityEdge: snapshot.BestProbabilityEdge,
            EstimatedEv: snapshot.BestEstimatedEv,
            TrustLevel: ExtractString(snapshot.DetailsJson, "trustLevel") ?? string.Empty,
            SampleWarning: ExtractString(snapshot.DetailsJson, "sampleWarning"),
            Warning: ExtractFirstStringArrayValue(snapshot.DetailsJson, "warnings"),
            HistoricalSampleSize: ExtractInt(snapshot.DetailsJson, "historicalSampleSize"),
            StrategyClassification: snapshot.StrategyClassification,
            StrategyTier: snapshot.StrategyTier,
            RankingScore: ExtractDecimal(snapshot.DetailsJson, "rankingScore") ?? 0m,
            IsCandidate: snapshot.IsCandidate,
            IsTopPick: isTopPick,
            IsWatchlist: snapshot.IsWatchlist,
            IsActionableForUser: snapshot.EntryActionableForUser,
            LifecycleStatus: snapshot.LifecycleStatus);
    }

    private static RecommendationLedgerCandidate MapSnapshotToLedgerCandidate(
        MarketTopPickSnapshot snapshot,
        bool isTopPick,
        IReadOnlyDictionary<string, Guid?> playerIdsByName,
        IReadOnlyDictionary<string, Guid?> resultPlayerIdsByName,
        IReadOnlyDictionary<string, Guid?> propTypeIdsByName,
        IReadOnlyDictionary<string, Guid?> resultPropTypeIdsByName)
    {
        var playerId = ExtractGuid(snapshot.DetailsJson, "playerId")
            ?? ExtractGuid(snapshot.DetailsJson, "canonicalPlayerId")
            ?? ResolveNullableGuid(resultPlayerIdsByName, snapshot.Player)
            ?? ResolveNullableGuid(playerIdsByName, snapshot.Player);
        var propTypeId = ExtractGuid(snapshot.DetailsJson, "propTypeId")
            ?? ResolveNullableGuid(resultPropTypeIdsByName, snapshot.PropType)
            ?? ResolveNullableGuid(propTypeIdsByName, snapshot.PropType);
        var expectedResultDate = ExtractDate(snapshot.DetailsJson, "expectedResultDate") ?? snapshot.SlateDate;
        var resultDateSource = ExtractString(snapshot.DetailsJson, "resultDateSource") ?? "SnapshotSlateDateFallback";
        var hitChance = string.Equals(snapshot.BestSide, "Under", StringComparison.OrdinalIgnoreCase)
            ? ExtractDecimal(snapshot.DetailsJson, "modelProbabilityUnder") ?? snapshot.BestProbability
            : ExtractDecimal(snapshot.DetailsJson, "modelProbabilityOver") ?? snapshot.BestProbability;

        return new RecommendationLedgerCandidate(
            CapturedAtUtc: snapshot.GeneratedAtUtc,
            RefreshRunId: snapshot.TheOddsRefreshRunId,
            PredictionRunId: snapshot.PredictionRunId,
            Sport: snapshot.Sport,
            SlateDate: snapshot.SlateDate,
            ExpectedResultDate: expectedResultDate,
            ResultDateSource: resultDateSource,
            EventId: snapshot.EventId,
            EventStartTimeUtc: snapshot.EventStartTime,
            CanonicalPlayerId: playerId,
            PlayerName: snapshot.Player,
            PropTypeId: propTypeId,
            PropType: snapshot.PropType,
            Side: snapshot.BestSide,
            Line: snapshot.Line,
            Provider: snapshot.EntryProvider,
            Bookmaker: snapshot.BestBookmaker ?? snapshot.Bookmaker,
            ProviderCategory: snapshot.ProviderCategory,
            EntryProvider: snapshot.EntryProvider,
            EntryCompatibilityGroup: snapshot.EntryCompatibilityGroup,
            HitChance: hitChance,
            ImpliedProbability: snapshot.BestImpliedProbability,
            ProbabilityEdge: snapshot.BestProbabilityEdge,
            EstimatedEv: snapshot.BestEstimatedEv,
            TrustLevel: ExtractString(snapshot.DetailsJson, "trustLevel") ?? string.Empty,
            SampleWarning: ExtractString(snapshot.DetailsJson, "sampleWarning"),
            Warning: ExtractFirstStringArrayValue(snapshot.DetailsJson, "warnings"),
            HistoricalSampleSize: ExtractInt(snapshot.DetailsJson, "historicalSampleSize"),
            StrategyClassification: snapshot.StrategyClassification,
            StrategyTier: snapshot.StrategyTier,
            RankingScore: ExtractDecimal(snapshot.DetailsJson, "rankingScore") ?? 0m,
            IsCandidate: snapshot.IsCandidate,
            IsTopPick: isTopPick,
            IsWatchlist: snapshot.IsWatchlist,
            IsActionableForUser: snapshot.EntryActionableForUser,
            LifecycleStatus: snapshot.LifecycleStatus);
    }

    private static string? ExtractString(JsonDocument document, string propertyName)
    {
        return TryGetProperty(document.RootElement, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static Guid? ResolveNullableGuid(IReadOnlyDictionary<string, Guid?> lookup, string value)
    {
        return lookup.TryGetValue(RecommendationLedgerWriter.NormalizeKey(value), out var id) ? id : null;
    }

    private static Guid? ExtractGuid(JsonDocument document, string propertyName)
    {
        var text = ExtractString(document, propertyName);
        return Guid.TryParse(text, out var value) ? value : null;
    }

    private static DateOnly? ExtractDate(JsonDocument document, string propertyName)
    {
        var text = ExtractString(document, propertyName);
        return DateOnly.TryParse(text, out var value) ? value : null;
    }

    private static decimal? ExtractDecimal(JsonDocument document, string propertyName)
    {
        if (!TryGetProperty(document.RootElement, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;
    }

    private static int? ExtractInt(JsonDocument document, string propertyName)
    {
        if (!TryGetProperty(document.RootElement, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static string? ExtractFirstStringArrayValue(JsonDocument document, string propertyName)
    {
        if (!TryGetProperty(document.RootElement, propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray()
            .FirstOrDefault(item => item.ValueKind == JsonValueKind.String)
            .GetString();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
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

                if (TryGetProperty(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryGetProperty(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
