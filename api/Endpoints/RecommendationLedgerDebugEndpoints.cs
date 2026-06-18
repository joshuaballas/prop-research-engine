using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using System.Text.Json;

namespace SportsBetting.Api.Endpoints;

public static class RecommendationLedgerDebugEndpoints
{
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
                    unmatchedRows = group.Count(row => row.GradeStatus == "Unmatched")
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
                        note = "Performance candidateOnly=true currently filters recommendation_snapshots by IsCandidate only. IsTopPick or IsWatchlist rows are counted separately unless IsCandidate is also true."
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
                    "Performance candidateOnly=true filters recommendation_snapshots by IsCandidate only; top-pick/watchlist flags are diagnostic unless IsCandidate is also true.",
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

            var examples = capturedRows
                .Select(row => BuildResultCoverageExample(row, resultRows))
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
        IReadOnlyCollection<CoveragePropResultRow> resultRows)
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
        var likelyReason = ResolveCoverageReason(
            snapshot,
            sameDateRows,
            samePlayerAnyProp,
            samePlayerSameProp,
            sameNormalizedNameAnyProp,
            sameNormalizedNameSameProp,
            samePropTypeAnyPlayer,
            nearbyDateSamePlayerSameProp);

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
                samePlayerAnyProp = samePlayerAnyProp.Length,
                samePlayerSameProp = samePlayerSameProp.Length,
                sameNormalizedNameAnyProp = sameNormalizedNameAnyProp.Length,
                sameNormalizedNameSameProp = sameNormalizedNameSameProp.Length,
                samePropTypeAnyPlayer = samePropTypeAnyPlayer.Length,
                nearbyDateSamePlayerSameProp = nearbyDateSamePlayerSameProp.Length,
                closestPlayerNameMatches = BuildClosestPlayerNameMatches(snapshot, sameDateRows, normalizedPlayerName),
                closestSamePropTypeRows = BuildClosestSamePropTypeRows(snapshot, samePropTypeAnyPlayer, normalizedPlayerName)
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
            likelyReason,
            interpretation = BuildCoverageInterpretation(snapshot, likelyReason)
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
            return "PropTypeNotImportedForPlayer";
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
            "EventDateMismatch" => "A matching player and prop exists on a nearby imported date, so inspect event/result-date assignment before changing grading.",
            "PropTypeNotImportedForPlayer" => "The player has at least one imported result on the completed date, but not this prop type. If they appeared in the relevant batting/pitching role, importer coverage may be missing a zero or category row.",
            "ProviderLineForInactivePlayer" => "The completed date has imported rows for this prop type, but not this player by id or normalized name. This commonly means the provider offered a stale/inactive player line or the player did not appear in the MLB boxscore.",
            "PlayerIdentityMismatch" => "The completed date has imported rows for this prop type, but the captured ledger row lacks a safe canonical player identity match.",
            "PlayerDidNotAppearInBoxscore" => "No persisted prop_results rows exist for this player by id or normalized name on the completed date. Without persisted raw boxscore rows, this should be treated as likely DNP/no-result until raw MLB data proves otherwise.",
            "RawBoxscoreUnavailable" => "No prop_results were imported for the searched date, so raw import status must be checked before diagnosing this row.",
            _ => "The persisted result coverage is inconclusive."
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
