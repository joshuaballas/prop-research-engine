using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Endpoints;

public static class TheOddsApiDebugEndpoints
{
    private const int FreshnessWindowHours = 24;
    private const int DefaultTheOddsLineShoppingFreshnessMinutes = 30;
    private const string MlbAliasProvider = "mlb-alias";

    public static IEndpointRouteBuilder MapTheOddsApiDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/the-odds/mlb/line-shopping", async (
            Guid? runId,
            int? freshnessMinutes,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            CancellationToken cancellationToken) =>
        {
            var totalStopwatch = Stopwatch.StartNew();
            var loadPredictionsMs = 0L;
            var loadPlatformPropsMs = 0L;
            var metadataParseMs = 0L;
            var groupingMs = 0L;
            var evCalculationMs = 0L;
            var responseBuildMs = 0L;

            var stepStopwatch = Stopwatch.StartNew();
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            loadPlatformPropsMs = stepStopwatch.ElapsedMilliseconds;

            stepStopwatch.Restart();
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows);
            metadataParseMs = stepStopwatch.ElapsedMilliseconds;

            var theOddsPropIds = platformProps.Select(row => row.PropId).Distinct().ToArray();

            stepStopwatch.Restart();
            var probabilityAnalysis = runId.HasValue
                ? await probabilityService.AnalyzeRunForPropsAsync(runId.Value, theOddsPropIds, cancellationToken)
                : await probabilityService.AnalyzeLatestRunForPropsAsync(theOddsPropIds, cancellationToken);
            loadPredictionsMs = stepStopwatch.ElapsedMilliseconds;

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var generatedAtUtc = DateTimeOffset.UtcNow;
            var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
            var freshnessCutoff = generatedAtUtc.AddHours(-FreshnessWindowHours);

            stepStopwatch.Restart();
            var rows = BuildTheOddsLineShoppingRows(platformProps, probabilityAnalysis, freshnessCutoff);
            evCalculationMs = stepStopwatch.ElapsedMilliseconds;

            stepStopwatch.Restart();
            var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, lifecycleFreshnessMinutes);
            var topByBestEv = groups
                .OrderByDescending(group => group.BestEstimatedEv ?? decimal.MinValue)
                .ThenByDescending(group => group.RankingScore)
                .Take(100)
                .ToList();
            var topByRankingScore = groups
                .OrderByDescending(group => group.RankingScore)
                .ThenByDescending(group => group.BestEstimatedEv ?? decimal.MinValue)
                .Take(100)
                .ToList();
            var topByPriceSpread = groups
                .OrderByDescending(group => group.LargestPriceSpread ?? decimal.MinValue)
                .ThenByDescending(group => group.BestEstimatedEv ?? decimal.MinValue)
                .Take(100)
                .ToList();
            groupingMs = stepStopwatch.ElapsedMilliseconds;

            stepStopwatch.Restart();
            var comparisonGroups = groups.Select(ToLineShoppingResponseGroup).ToList();
            var topGroupsByBestEstimatedEv = topByBestEv.Select(ToLineShoppingResponseGroup).ToList();
            var topGroupsByRankingScore = topByRankingScore.Select(ToLineShoppingResponseGroup).ToList();
            var topGroupsByLargestPriceSpread = topByPriceSpread.Select(ToLineShoppingResponseGroup).ToList();
            var strategyClassificationSummary = BuildTheOddsStrategyClassificationSummary(groups);
            var representedBookmakers = rows
                .GroupBy(row => row.Bookmaker, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { bookmaker = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.bookmaker)
                .ToList();
            var missingOrUnreliableFields = BuildLineShoppingMissingFieldFindings(rows, groups);
            responseBuildMs = stepStopwatch.ElapsedMilliseconds;
            totalStopwatch.Stop();

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                diagnostics = new
                {
                    callsTheOddsApi = false,
                    usesPersistedDatabaseRowsOnly = true,
                    totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                    loadPredictionsMs,
                    loadPlatformPropsMs,
                    metadataParseMs,
                    groupingMs,
                    evCalculationMs,
                    responseBuildMs,
                    persistedTheOddsPlatformPropsLoaded = platformProps.Count,
                    filteredPredictionResultsAnalyzed = probabilityAnalysis.Results.Count,
                    lifecycleFreshnessMinutes
                },
                run = new
                {
                    runId = probabilityAnalysis.RunId,
                    probabilityAnalysis.ModelName,
                    probabilityAnalysis.ModelVersion,
                    probabilityAnalysis.CreatedAt,
                    probabilityAnalysis.PredictionCount
                },
                summary = new
                {
                    totalComparisonGroups = groups.Count,
                    totalMarketsCompared = rows.Count,
                    totalBookmakers = rows.Select(row => row.Bookmaker).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    groupsWithMultipleBookmakers = groups.Count(group => group.BookmakerPrices.Count > 1),
                    groupsWithSingleBookmaker = groups.Count(group => group.BookmakerPrices.Count == 1),
                    eligibleComparisonGroups = groups.Count(group => group.IsEligibleAsNormalRecommendation),
                    groupsWithBestOverPrice = groups.Count(group => !string.IsNullOrWhiteSpace(group.BestOverBookmaker)),
                    groupsWithBestUnderPrice = groups.Count(group => !string.IsNullOrWhiteSpace(group.BestUnderBookmaker)),
                    strongGroups = groups.Count(group => group.TrustLevel == "Strong"),
                    mediumGroups = groups.Count(group => group.TrustLevel == "Medium"),
                    weakGroups = groups.Count(group => group.TrustLevel == "Weak"),
                    researchOnlyGroups = groups.Count(group => group.TrustLevel == "ResearchOnly"),
                    positiveBestEvGroups = groups.Count(group => group.BestEstimatedEv > 0m),
                    negativeBestEvGroups = groups.Count(group => group.BestEstimatedEv <= 0m),
                    lowSampleGroups = groups.Count(group => group.SampleWarning is "LowSample" or "InsufficientHistoricalSample" or "MissingHistoricalDistribution")
                },
                strategyClassificationSummary,
                comparisonGroups,
                topGroupsByBestEstimatedEv,
                topGroupsByRankingScore,
                topGroupsByLargestPriceSpread,
                findings = new
                {
                    areMultipleBooksAvailableForSamePlayerPropLine = groups.Any(group => group.BookmakerPrices.Count > 1),
                    representedBookmakers,
                    hasMeaningfulPriceDifferences = groups.Any(group => group.LargestPriceSpread >= 0.05m),
                    appearsReadyForDashboardDisplay = groups.Any(group => group.BookmakerPrices.Count > 1)
                        && groups.Any(group => group.IsEligibleAsNormalRecommendation),
                    missingOrUnreliableFields
                }
            });
        })
        .WithName("TheOddsApiMlbLineShoppingDebug")
        .WithSummary("Read-only line-shopping comparison for persisted The Odds API MLB markets.");

        endpoints.MapGet("/api/debug/the-odds/mlb/line-shopping-backtest", async (
            Guid? runId,
            int? sampleTake,
            bool? includeSamples,
            bool? strategyOnly,
            int? minSampleSize,
            string? bookmaker,
            string? propType,
            string? trustLevel,
            decimal? minBestProbability,
            decimal? maxBestProbability,
            decimal? minEstimatedEv,
            decimal? maxEstimatedEv,
            decimal? minBestPrice,
            decimal? maxBestPrice,
            decimal? line,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows);
            var theOddsPropIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
            var probabilityAnalysis = runId.HasValue
                ? await probabilityService.AnalyzeRunForPropsAsync(runId.Value, theOddsPropIds, cancellationToken)
                : await probabilityService.AnalyzeLatestRunForPropsAsync(theOddsPropIds, cancellationToken);

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var generatedAtUtc = DateTimeOffset.UtcNow;
            var freshnessCutoff = generatedAtUtc.AddHours(-FreshnessWindowHours);
            var rows = BuildTheOddsLineShoppingRows(platformProps, probabilityAnalysis, freshnessCutoff);
            var groups = BuildTheOddsLineShoppingGroups(rows);
            var actualResults = await LoadTheOddsMlbActualResultsAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadTheOddsEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var playerAliasCache = new Dictionary<Guid, Guid[]>();
            var backtestRows = new List<TheOddsLineShoppingBacktestRow>(groups.Count);
            var requestedSampleTake = Math.Clamp(sampleTake ?? 25, 0, 250);
            var shouldIncludeSamples = includeSamples ?? true;

            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var equivalentPlayerIds = await GetTheOddsEquivalentPlayerIdsAsync(
                    group.PlayerId,
                    playerIdentityAliasService,
                    playerAliasCache,
                    cancellationToken);
                var equivalentPropTypeIds = equivalentPropTypeIdsById.TryGetValue(group.PropTypeId, out var propTypeIds)
                    ? propTypeIds
                    : [group.PropTypeId];
                var actualResult = FindTheOddsActualResult(
                    actualResults,
                    group.EventDate,
                    equivalentPlayerIds,
                    equivalentPropTypeIds);
                var outcome = GradeTheOddsBestSide(group.BestSide, group.Line, actualResult);
                var profit = CalculateTheOddsProfitPerDollarStake(outcome, group.BestDecimalPrice);

                backtestRows.Add(new TheOddsLineShoppingBacktestRow(
                    group.Player,
                    group.NormalizedPropType,
                    group.MarketClassification,
                    group.Line,
                    group.EventId,
                    group.EventStartTime,
                    group.HomeTeam,
                    group.AwayTeam,
                    BuildTheOddsEventName(group.HomeTeam, group.AwayTeam),
                    group.EventStartTimeUtc,
                    group.ExpectedResultDate,
                    group.ResultDateSource,
                    group.UtcDate,
                    group.ResolvedMlbDate,
                    group.BestSide,
                    group.BestBookmaker,
                    group.BestAmericanPrice,
                    group.BestDecimalPrice,
                    group.BestEstimatedEv,
                    group.BestProbability,
                    group.BestImpliedProbability,
                    group.BestProbabilityEdge,
                    group.TrustLevel,
                    group.SampleWarning,
                    group.LifecycleStatus,
                    group.HistoricalSampleSize,
                    group.ConfidenceScore,
                    group.RankingScore,
                    actualResult,
                    outcome,
                    profit,
                    BuildTheOddsUngradedReason(group, actualResult, outcome)));
            }

            var gradedRows = backtestRows
                .Where(row => row.Outcome is "Win" or "Loss" or "Push")
                .ToArray();
            var ungradedRows = backtestRows
                .Where(row => !IsTheOddsGraded(row))
                .ToArray();
            var strategyRows = BuildTheOddsStrategySimulations(backtestRows);
            var queryFilteredStrategy = BuildTheOddsQueryFilteredStrategy(
                backtestRows,
                minSampleSize,
                bookmaker,
                propType,
                trustLevel,
                minBestProbability,
                maxBestProbability,
                minEstimatedEv,
                maxEstimatedEv,
                minBestPrice,
                maxBestPrice,
                line);
            var shouldReturnStrategyOnly = strategyOnly ?? false;

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                run = new
                {
                    runId = probabilityAnalysis.RunId,
                    probabilityAnalysis.ModelName,
                    probabilityAnalysis.ModelVersion,
                    probabilityAnalysis.CreatedAt,
                    probabilityAnalysis.PredictionCount
                },
                summary = BuildTheOddsLineShoppingBacktestSummary(backtestRows),
                strategySimulations = strategyRows,
                queryFilteredStrategy,
                byTrustLevel = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.TrustLevel, "trustLevel"),
                byPropType = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.PropType, "propType"),
                byBookmaker = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.BestBookmaker ?? "Unknown", "bookmaker"),
                byBestSide = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.BestSide, "bestSide"),
                byBestBookmaker = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.BestBookmaker ?? "Unknown", "bestBookmaker"),
                byEvent = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.EventName, "event"),
                byMarketClassification = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.MarketClassification, "marketClassification"),
                bySampleWarning = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.SampleWarning, "sampleWarning"),
                byBestEstimatedEvBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => BestEvBucket(row.BestEstimatedEv), "bestEstimatedEvBucket"),
                byEstimatedEvBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => BestEvBucket(row.BestEstimatedEv), "estimatedEvBucket"),
                byConfidenceBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => ConfidenceBucket(row.ConfidenceScore), "confidenceBucket"),
                bySampleSizeBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => SampleSizeBucket(row.HistoricalSampleSize), "sampleSizeBucket"),
                byLifecycleStatus = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => row.LifecycleStatus, "lifecycleStatus"),
                byModelProbabilityBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => ProbabilityBucket(row.BestProbability), "modelProbabilityBucket"),
                byImpliedProbabilityBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => ProbabilityBucket(row.BestImpliedProbability), "impliedProbabilityBucket"),
                byProbabilityEdgeBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => ProbabilityEdgeBucket(row.BestProbabilityEdge), "probabilityEdgeBucket"),
                byHistoricalSampleSizeBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => HistoricalSampleSizeBucket(row.HistoricalSampleSize), "historicalSampleSizeBucket"),
                byBestPriceBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => DecimalPriceBucket(row.BestDecimalPrice), "bestPriceBucket"),
                byRecommendationStrength = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => RecommendationStrengthBucket(row), "recommendationStrength"),
                byLineValue = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => LineValueBucket(row.Line), "lineValue"),
                byEventAndPropType = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => $"{row.EventName} | {row.PropType}", "eventAndPropType"),
                byBookmakerAndPropType = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => $"{row.BestBookmaker ?? "Unknown"} | {row.PropType}", "bookmakerAndPropType"),
                byTrustLevelAndPropType = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => $"{row.TrustLevel} | {row.PropType}", "trustLevelAndPropType"),
                byTrustLevelAndEvBucket = shouldReturnStrategyOnly ? [] : BuildTheOddsLineShoppingBacktestBreakdown(backtestRows, row => $"{row.TrustLevel} | {BestEvBucket(row.BestEstimatedEv)}", "trustLevelAndEvBucket"),
                modelProbabilityCalibration = shouldReturnStrategyOnly ? [] : BuildTheOddsModelProbabilityCalibrationTable(backtestRows),
                topUngradedReasons = shouldReturnStrategyOnly ? [] : BuildTheOddsTopUngradedReasons(ungradedRows),
                gradedSamples = !shouldReturnStrategyOnly && shouldIncludeSamples
                    ? gradedRows
                        .OrderBy(row => row.EventStartTime ?? DateTimeOffset.MaxValue)
                        .ThenBy(row => row.EventName)
                        .ThenBy(row => row.Player)
                        .Take(requestedSampleTake)
                        .Select(ToTheOddsLineShoppingGradedBacktestSample)
                        .ToArray()
                    : [],
                ungradedSamples = !shouldReturnStrategyOnly && shouldIncludeSamples
                    ? ungradedRows
                        .OrderBy(row => row.EventStartTime ?? DateTimeOffset.MaxValue)
                        .ThenBy(row => row.EventName)
                        .ThenBy(row => row.Player)
                        .Take(requestedSampleTake)
                        .Select(ToTheOddsLineShoppingUngradedBacktestSample)
                        .ToArray()
                    : [],
                topGradedWins = shouldReturnStrategyOnly
                    ? []
                    : gradedRows
                    .Where(row => row.Outcome == "Win")
                    .OrderByDescending(row => row.Profit)
                    .ThenByDescending(row => row.BestEstimatedEv ?? decimal.MinValue)
                    .Take(100)
                    .Select(ToTheOddsLineShoppingBacktestSample)
                    .ToArray(),
                topGradedLosses = shouldReturnStrategyOnly
                    ? []
                    : gradedRows
                    .Where(row => row.Outcome == "Loss")
                    .OrderBy(row => row.Profit)
                    .ThenByDescending(row => row.BestEstimatedEv ?? decimal.MinValue)
                    .Take(100)
                    .Select(ToTheOddsLineShoppingBacktestSample)
                    .ToArray(),
                diagnosis = BuildTheOddsLineShoppingBacktestDiagnosis(backtestRows)
            });
        })
        .WithName("TheOddsApiMlbLineShoppingBacktestDebug")
        .WithSummary("Read-only backtest for persisted The Odds API MLB line-shopping best-side selections.");

        endpoints.MapPost("/api/debug/the-odds/mlb/line-shopping-backtest/strategy-snapshot", async (
            Guid runId,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var computation = await BuildTheOddsLineShoppingBacktestComputationAsync(
                runId,
                dbContext,
                probabilityService,
                playerIdentityAliasService,
                cancellationToken);

            if (computation is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var slateDates = computation.BacktestRows
                .Select(row => row.ExpectedResultDate)
                .Distinct()
                .Order()
                .ToArray();
            var slateDate = slateDates.Length == 1 ? slateDates[0] : (DateOnly?)null;
            var strategyRows = BuildTheOddsStrategySimulations(computation.BacktestRows);
            var snapshots = strategyRows.Select(strategy => new LineShoppingStrategyBacktestSnapshot
            {
                Id = Guid.NewGuid(),
                RunId = computation.ProbabilityAnalysis.RunId,
                Sport = "MLB",
                SlateDate = slateDate,
                StrategyName = strategy.strategyName,
                Description = strategy.description,
                TotalGroups = strategy.totalGroups,
                GradedGroups = strategy.gradedGroups,
                Wins = strategy.wins,
                Losses = strategy.losses,
                Pushes = strategy.pushes,
                HitRate = strategy.hitRate,
                SimulatedProfit = strategy.simulatedProfit,
                SimulatedRoi = strategy.simulatedRoi,
                AverageBestEstimatedEv = strategy.averageBestEstimatedEv,
                AverageBestProbability = strategy.averageBestProbability,
                AverageBestImpliedProbability = strategy.averageBestImpliedProbability,
                AverageProbabilityEdge = strategy.averageProbabilityEdge,
                AverageHistoricalSampleSize = strategy.averageHistoricalSampleSize,
                AverageBestPrice = strategy.averageBestPrice,
                GeneratedAt = generatedAtUtc,
                CreatedAt = generatedAtUtc
            }).ToArray();

            dbContext.LineShoppingStrategyBacktestSnapshots.AddRange(snapshots);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new
            {
                runId = computation.ProbabilityAnalysis.RunId,
                sport = "MLB",
                slateDate,
                generatedAtUtc,
                snapshotCount = snapshots.Length,
                strategies = snapshots
                    .OrderByDescending(snapshot => snapshot.SimulatedRoi)
                    .ThenByDescending(snapshot => snapshot.GradedGroups)
                    .Select(ToTheOddsStrategySnapshotResponse)
                    .ToArray()
            });
        })
        .WithName("TheOddsApiMlbLineShoppingStrategySnapshotDebug")
        .WithSummary("Persists one strategy backtest snapshot set for a The Odds API MLB prediction run.");

        endpoints.MapGet("/api/debug/the-odds/mlb/line-shopping-backtest/strategy-snapshots", async (
            Guid? runId,
            string? strategyName,
            DateOnly? fromDate,
            DateOnly? toDate,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var query = dbContext.LineShoppingStrategyBacktestSnapshots.AsNoTracking();
            if (runId.HasValue)
            {
                query = query.Where(snapshot => snapshot.RunId == runId.Value);
            }

            if (!string.IsNullOrWhiteSpace(strategyName))
            {
                query = query.Where(snapshot => snapshot.StrategyName == strategyName);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(snapshot => snapshot.SlateDate.HasValue && snapshot.SlateDate.Value >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(snapshot => snapshot.SlateDate.HasValue && snapshot.SlateDate.Value <= toDate.Value);
            }

            var snapshots = await query
                .OrderByDescending(snapshot => snapshot.GeneratedAt)
                .ThenBy(snapshot => snapshot.StrategyName)
                .Take(1000)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                count = snapshots.Count,
                snapshots = snapshots.Select(ToTheOddsStrategySnapshotResponse).ToArray()
            });
        })
        .WithName("TheOddsApiMlbLineShoppingStrategySnapshotsDebug")
        .WithSummary("Reads persisted The Odds API MLB line-shopping strategy backtest snapshots.");

        endpoints.MapGet("/api/debug/the-odds/mlb/line-shopping-backtest/strategy-performance-summary", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var snapshots = await dbContext.LineShoppingStrategyBacktestSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.Sport == "MLB")
                .ToListAsync(cancellationToken);

            var rows = snapshots
                .GroupBy(snapshot => snapshot.StrategyName, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var groupRows = group.ToArray();
                    var totalWins = groupRows.Sum(snapshot => snapshot.Wins);
                    var totalLosses = groupRows.Sum(snapshot => snapshot.Losses);
                    var totalPushes = groupRows.Sum(snapshot => snapshot.Pushes);
                    var totalGraded = groupRows.Sum(snapshot => snapshot.GradedGroups);
                    var decisions = totalWins + totalLosses;
                    var aggregateProfit = Math.Round(groupRows.Sum(snapshot => snapshot.SimulatedProfit), 4);

                    return new
                    {
                        strategyName = group.Key,
                        snapshotCount = groupRows.Length,
                        totalGradedGroups = totalGraded,
                        totalWins,
                        totalLosses,
                        totalPushes,
                        aggregateHitRate = decisions == 0 ? 0m : Math.Round(totalWins / (decimal)decisions, 4),
                        aggregateProfit,
                        aggregateRoi = totalGraded == 0 ? 0m : Math.Round(aggregateProfit / totalGraded, 4),
                        averageDailyRoi = Math.Round(groupRows.Average(snapshot => snapshot.SimulatedRoi), 4),
                        bestSnapshotRoi = groupRows.Max(snapshot => snapshot.SimulatedRoi),
                        worstSnapshotRoi = groupRows.Min(snapshot => snapshot.SimulatedRoi)
                    };
                })
                .OrderByDescending(row => row.aggregateRoi)
                .ThenByDescending(row => row.totalGradedGroups)
                .ToArray();

            return Results.Ok(new
            {
                strategyCount = rows.Length,
                rows
            });
        })
        .WithName("TheOddsApiMlbLineShoppingStrategyPerformanceSummaryDebug")
        .WithSummary("Summarizes persisted The Odds API MLB line-shopping strategy backtest snapshots by strategy.");

        endpoints.MapGet("/api/debug/the-odds/mlb/event-workflow-status", async (
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .ToList();
            var theOddsPropIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
            var lineSnapshotCounts = await LoadTheOddsLineSnapshotCountsAsync(dbContext, platformProps, cancellationToken);
            var predictionRows = await LoadTheOddsPredictionWorkflowRowsAsync(dbContext, theOddsPropIds, cancellationToken);
            var probabilityAnalysis = await probabilityService.AnalyzeLatestRunForPropsAsync(theOddsPropIds, cancellationToken);
            var lineShoppingRows = BuildTheOddsLineShoppingRows(
                platformProps,
                probabilityAnalysis,
                generatedAtUtc.AddHours(-FreshnessWindowHours));
            var lineShoppingGroups = BuildTheOddsLineShoppingGroups(lineShoppingRows);
            var actualResults = await LoadTheOddsMlbActualResultsAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadTheOddsEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var playerAliasCache = new Dictionary<Guid, Guid[]>();
            var backtestRows = await BuildTheOddsBacktestRowsAsync(
                lineShoppingGroups,
                actualResults,
                equivalentPropTypeIdsById,
                playerIdentityAliasService,
                playerAliasCache,
                cancellationToken);
            var eventRows = BuildTheOddsEventWorkflowRows(
                platformProps,
                lineSnapshotCounts,
                predictionRows,
                lineShoppingGroups,
                backtestRows,
                generatedAtUtc)
                .OrderByDescending(row => row.EventStartTime)
                .ThenBy(row => row.EventId)
                .ToList();

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                generatedAtUtc,
                summary = new
                {
                    totalPersistedEvents = eventRows.Count,
                    ingestedCount = eventRows.Count(row => row.WorkflowStatus == "Ingested"),
                    predictedCount = eventRows.Count(row => row.WorkflowStatus == "Predicted"),
                    lineShoppingReadyCount = eventRows.Count(row => row.WorkflowStatus == "LineShoppingReady"),
                    awaitingResultsCount = eventRows.Count(row => row.WorkflowStatus == "AwaitingResults"),
                    backtestReadyCount = eventRows.Count(row => row.WorkflowStatus == "BacktestReady"),
                    completedCount = eventRows.Count(row => row.WorkflowStatus == "Completed"),
                    needsAttentionCount = eventRows.Count(row => row.WorkflowStatus == "NeedsAttention"),
                    mostRecentEventStartTime = eventRows
                        .Select(row => row.EventStartTime)
                        .Where(value => value.HasValue)
                        .DefaultIfEmpty()
                        .Max(),
                    latestObservedAt = eventRows
                        .Select(row => row.LatestObservedAt)
                        .Where(value => value.HasValue)
                        .DefaultIfEmpty()
                        .Max()
                },
                events = eventRows
            });
        })
        .WithName("TheOddsApiMlbEventWorkflowStatusDebug")
        .WithSummary("Read-only workflow status for persisted The Odds API MLB events.");

        endpoints.MapGet("/api/debug/the-odds/mlb/ingestion-plan", async (
            DateOnly? date,
            string? bookmakers,
            string? markets,
            int? maxEvents,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            IPredictionFeatureService featureService,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var requestedBookmakers = ParseCommaSeparatedSet(
                    bookmakers,
                    ["draftkings", "fanduel", "betmgm"])
                .Select(value => NormalizeTheOddsBookmakerKey(value, value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requestedMarkets = ParseCommaSeparatedSet(
                    markets,
                    ["batter_hits", "batter_total_bases", "pitcher_strikeouts"])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var effectiveMaxEvents = Math.Clamp(maxEvents ?? 5, 1, 100);
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .Where(row => !date.HasValue
                    || DateOnly.FromDateTime((row.EventStartTime ?? row.ObservedAt).UtcDateTime) == date.Value)
                .ToList();
            var theOddsPropIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
            var lineSnapshotCounts = await LoadTheOddsLineSnapshotCountsAsync(dbContext, platformProps, cancellationToken);
            var predictionRows = await LoadTheOddsPredictionWorkflowRowsAsync(dbContext, theOddsPropIds, cancellationToken);
            var probabilityAnalysis = await probabilityService.AnalyzeLatestRunForPropsAsync(theOddsPropIds, cancellationToken);
            var lineShoppingRows = BuildTheOddsLineShoppingRows(
                platformProps,
                probabilityAnalysis,
                generatedAtUtc.AddHours(-FreshnessWindowHours));
            var lineShoppingGroups = BuildTheOddsLineShoppingGroups(lineShoppingRows);
            var actualResults = await LoadTheOddsMlbActualResultsAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadTheOddsEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var playerAliasCache = new Dictionary<Guid, Guid[]>();
            var backtestRows = await BuildTheOddsBacktestRowsAsync(
                lineShoppingGroups,
                actualResults,
                equivalentPropTypeIdsById,
                playerIdentityAliasService,
                playerAliasCache,
                cancellationToken);
            var workflowRows = BuildTheOddsEventWorkflowRows(
                platformProps,
                lineSnapshotCounts,
                predictionRows,
                lineShoppingGroups,
                backtestRows,
                generatedAtUtc);
            var readinessByPlatformPropId = new Dictionary<Guid, PlayerHistoricalLookupDiagnostic>();

            foreach (var platformProp in platformProps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventDate = DateOnly.FromDateTime((platformProp.EventStartTime ?? platformProp.ObservedAt).UtcDateTime);
                readinessByPlatformPropId[platformProp.PlatformPropGuid] = await featureService.AnalyzeHistoricalLookupAsync(
                    platformProp.PlayerId,
                    platformProp.PropTypeId,
                    eventDate,
                    cancellationToken);
            }

            var allPlanRows = BuildTheOddsMlbIngestionPlanRows(
                platformProps,
                workflowRows,
                readinessByPlatformPropId,
                requestedBookmakers,
                requestedMarkets,
                generatedAtUtc)
                .OrderByDescending(row => row.NeedsAttention)
                .ThenBy(row => row.EventStartTime)
                .ThenBy(row => row.EventId)
                .ToArray();
            var planRows = allPlanRows.Take(effectiveMaxEvents).ToArray();

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                generatedAtUtc,
                filters = new
                {
                    date,
                    bookmakers = requestedBookmakers,
                    markets = requestedMarkets,
                    maxEvents = effectiveMaxEvents
                },
                summary = BuildTheOddsMlbIngestionPlanSummary(allPlanRows, workflowRows, date, requestedBookmakers, requestedMarkets),
                events = planRows
            });
        })
        .WithName("TheOddsApiMlbIngestionPlanDebug")
        .WithSummary("Read-only The Odds API MLB manual ingestion plan based on persisted database state only.");

        endpoints.MapGet("/api/debug/the-odds/mlb/worker-status", (
            IOptions<TheOddsIngestionWorkerOptions> options,
            ITheOddsMlbIngestionWorkerState state) =>
        {
            var workerOptions = options.Value;
            var snapshot = state.GetSnapshot();

            return Results.Ok(new
            {
                enabled = workerOptions.Enabled,
                dryRun = workerOptions.DryRun,
                intervalMinutes = workerOptions.IntervalMinutes,
                maxEventsPerRun = workerOptions.MaxEventsPerRun,
                maxEstimatedCreditsPerRun = workerOptions.MaxEstimatedCreditsPerRun,
                maxEstimatedCreditsPerDay = workerOptions.MaxEstimatedCreditsPerDay,
                bookmakers = workerOptions.Bookmakers,
                markets = workerOptions.Markets,
                onlyGameDates = workerOptions.OnlyGameDates,
                estimatedCreditsUsedToday = snapshot.EstimatedCreditsUsedToday,
                snapshot.LastRunStartedAt,
                snapshot.LastRunCompletedAt,
                snapshot.LastRunStatus,
                snapshot.LastRunDryRun,
                snapshot.LastRunCandidateEvents,
                snapshot.LastRunEstimatedCredits,
                snapshot.LastRunSkippedReasons,
                snapshot.LastRunPlannedEvents,
                snapshot.LastError,
                snapshot.LastManualDryRunStartedAt,
                snapshot.LastManualDryRunCompletedAt,
                snapshot.LastManualDryRunCandidateEvents,
                snapshot.LastManualDryRunEstimatedCredits,
                snapshot.LastManualDryRunPlannedEvents,
                snapshot.LastManualDryRunSkippedReasons,
                safety = new
                {
                    callsTheOddsApiWhenDisabled = false,
                    callsTheOddsApiInDryRun = false,
                    writesOddsRowsInDryRun = false,
                    automaticLivePollingImplemented = false
                }
            });
        })
        .WithName("TheOddsApiMlbIngestionWorkerStatusDebug")
        .WithSummary("Read-only The Odds API MLB ingestion worker status and safety configuration.");

        endpoints.MapPost("/api/debug/the-odds/mlb/worker-dry-run", async (
            IOptions<TheOddsIngestionWorkerOptions> options,
            ITheOddsMlbIngestionPlanner planner,
            ITheOddsMlbIngestionWorkerState state,
            CancellationToken cancellationToken) =>
        {
            var startedAt = DateTimeOffset.UtcNow;
            var workerOptions = options.Value;
            var dryRunOptions = new TheOddsIngestionWorkerOptions
            {
                Enabled = workerOptions.Enabled,
                DryRun = true,
                IntervalMinutes = workerOptions.IntervalMinutes,
                MaxEventsPerRun = workerOptions.MaxEventsPerRun,
                MaxEstimatedCreditsPerRun = workerOptions.MaxEstimatedCreditsPerRun,
                MaxEstimatedCreditsPerDay = workerOptions.MaxEstimatedCreditsPerDay,
                Bookmakers = workerOptions.Bookmakers,
                Markets = workerOptions.Markets,
                OnlyGameDates = workerOptions.OnlyGameDates
            };
            var estimatedCreditsUsedToday = state.GetEstimatedCreditsUsedToday(DateOnly.FromDateTime(startedAt.UtcDateTime));
            var plan = await planner.BuildPlanAsync(dryRunOptions, estimatedCreditsUsedToday, cancellationToken);
            var completedAt = DateTimeOffset.UtcNow;
            var skippedEvents = Math.Max(0, plan.CandidateEvents - plan.PlannedEventCount);
            var skippedReasons = plan.SkippedReasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            state.MarkManualDryRun(
                startedAt,
                completedAt,
                plan.PlannedEvents,
                plan.CandidateEvents,
                plan.EstimatedCredits,
                skippedReasons);

            return Results.Ok(new
            {
                enabled = workerOptions.Enabled,
                dryRun = true,
                configuredDryRun = workerOptions.DryRun,
                startedAt,
                completedAt,
                candidateEvents = plan.CandidateEvents,
                plannedEvents = plan.PlannedEvents,
                skippedEvents,
                skippedReasons,
                estimatedCredits = plan.EstimatedCredits,
                creditCaps = new
                {
                    maxEventsPerRun = workerOptions.MaxEventsPerRun,
                    maxEstimatedCreditsPerRun = workerOptions.MaxEstimatedCreditsPerRun,
                    maxEstimatedCreditsPerDay = workerOptions.MaxEstimatedCreditsPerDay,
                    estimatedCreditsUsedToday,
                    estimatedCreditsRemainingToday = Math.Max(0, workerOptions.MaxEstimatedCreditsPerDay - estimatedCreditsUsedToday)
                },
                config = new
                {
                    workerOptions.Bookmakers,
                    workerOptions.Markets,
                    workerOptions.OnlyGameDates,
                    workerOptions.IntervalMinutes
                },
                wouldCallTheOddsApi = false,
                wouldWriteRows = false,
                recommendedNextAction = BuildTheOddsMlbWorkerDryRunRecommendedAction(plan, skippedEvents, skippedReasons)
            });
        })
        .WithName("TheOddsApiMlbIngestionWorkerDryRunDebug")
        .WithSummary("Runs The Odds API MLB ingestion worker planning logic on demand without provider calls or writes.");

        endpoints.MapGet("/api/debug/the-odds/mlb/bookmaker-coverage", async (
            DateOnly? date,
            int? freshnessMinutes,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            IOptions<TheOddsIngestionWorkerOptions> workerOptions,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var allPlatformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .ToList();
            var slateDate = date
                ?? allPlatformProps
                    .Select(row => ResolveMlbSlateDate(row.EventStartTime))
                    .Where(row => row.HasValue)
                    .OrderByDescending(row => row)
                    .FirstOrDefault()
                ?? ResolveCurrentMlbSlateDate();
            var platformProps = allPlatformProps
                .Where(row => ResolveMlbSlateDate(row.EventStartTime) == slateDate)
                .ToList();
            var propIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
            var probabilityAnalysis = await probabilityService.AnalyzeLatestRunForPropsAsync(propIds, cancellationToken);

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = "No prediction run was found for persisted The Odds API platform props." });
            }

            var lineShoppingRows = BuildTheOddsLineShoppingRows(
                platformProps,
                probabilityAnalysis,
                generatedAtUtc.AddHours(-FreshnessWindowHours));
            var lineShoppingGroups = BuildTheOddsLineShoppingGroups(
                lineShoppingRows,
                generatedAtUtc,
                lifecycleFreshnessMinutes);
            var configuredBookmakers = ParseCommaSeparatedSet(workerOptions.Value.Bookmakers)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var observedBookmakers = platformProps
                .Select(row => NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName))
                .Where(row => !string.IsNullOrWhiteSpace(row))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var expectedEvents = platformProps
                .Select(row => row.EventId)
                .Where(row => !string.IsNullOrWhiteSpace(row))
                .Select(row => row!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                generatedAtUtc,
                latestRunId = probabilityAnalysis.RunId,
                slateDate,
                totalEvents = expectedEvents.Length,
                totalPlatformProps = platformProps.Count,
                totalComparisonGroups = lineShoppingGroups.Count,
                configuredBookmakers,
                observedBookmakers,
                byBookmaker = BuildTheOddsBookmakerCoverageRows(platformProps, lineShoppingGroups),
                byBookmakerAndPropType = BuildTheOddsBookmakerPropTypeCoverageRows(platformProps, lineShoppingGroups),
                missingBookmakerCoverage = BuildMissingTheOddsBookmakerCoverageRows(
                    configuredBookmakers,
                    expectedEvents,
                    platformProps),
                diagnostics = new
                {
                    callsTheOddsApi = false,
                    usesPersistedDatabaseRowsOnly = true,
                    freshnessMinutes = lifecycleFreshnessMinutes,
                    configuredBookmakerSource = "TheOddsIngestionWorker:Bookmakers",
                    configuredMarkets = workerOptions.Value.Markets
                }
            });
        })
        .WithName("TheOddsApiMlbBookmakerCoverageDebug")
        .WithSummary("Read-only compact bookmaker coverage diagnostic for persisted The Odds API MLB rows.");

        endpoints.MapGet("/api/debug/the-odds/mlb/bookmaker-coverage-audit", async (
            Guid? runId,
            string? eventId,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .Where(row => string.IsNullOrWhiteSpace(eventId)
                    || string.Equals(row.EventId, eventId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var theOddsPropIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
            var lineSnapshotCounts = await LoadTheOddsLineSnapshotCountsAsync(dbContext, platformProps, cancellationToken);
            var predictionRows = await LoadTheOddsPredictionWorkflowRowsAsync(dbContext, theOddsPropIds, runId, cancellationToken);
            var probabilityAnalysis = runId.HasValue
                ? await probabilityService.AnalyzeRunForPropsAsync(runId.Value, theOddsPropIds, cancellationToken)
                : await probabilityService.AnalyzeLatestRunForPropsAsync(theOddsPropIds, cancellationToken);

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var lineShoppingRows = BuildTheOddsLineShoppingRows(
                platformProps,
                probabilityAnalysis,
                generatedAtUtc.AddHours(-FreshnessWindowHours));
            var lineShoppingGroups = BuildTheOddsLineShoppingGroups(lineShoppingRows);
            var rawBookmakerRows = await LoadTheOddsRawBookmakerCoverageRowsAsync(dbContext, platformProps, eventId, cancellationToken);
            var auditRows = BuildTheOddsBookmakerCoverageAuditRows(
                platformProps,
                rawBookmakerRows,
                lineSnapshotCounts,
                predictionRows,
                lineShoppingGroups);

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                generatedAtUtc,
                filters = new
                {
                    eventId,
                    runId = probabilityAnalysis.RunId
                },
                summary = BuildTheOddsBookmakerCoverageSummary(auditRows),
                bookmakerCoverage = auditRows,
                diagnosis = BuildTheOddsBookmakerCoverageDiagnosis(auditRows)
            });
        })
        .WithName("TheOddsApiMlbBookmakerCoverageAuditDebug")
        .WithSummary("Read-only bookmaker coverage audit for persisted The Odds API MLB event rows.");

        endpoints.MapGet("/api/debug/the-odds/mlb/bookmaker-prediction-dropout", async (
            Guid? runId,
            string? bookmaker,
            string? eventId,
            bool? ignoreRunContext,
            ApplicationDbContext dbContext,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            var shouldIgnoreRunContext = ignoreRunContext == true;
            var requestedBookmaker = string.IsNullOrWhiteSpace(bookmaker)
                ? "FanDuel"
                : bookmaker.Trim();
            var requestedBookmakerKey = NormalizeTheOddsBookmakerKey(null, requestedBookmaker);
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .Where(row => string.Equals(
                    NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                    requestedBookmakerKey,
                    StringComparison.OrdinalIgnoreCase))
                .Where(row => string.IsNullOrWhiteSpace(eventId)
                    || string.Equals(row.EventId, eventId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(row => row.ObservedAt)
                .ThenBy(row => row.PlayerName)
                .ThenBy(row => row.PropType)
                .ToList();

            var selectedRun = shouldIgnoreRunContext
                ? null
                : await LoadTheOddsDiagnosticRunContextAsync(
                    dbContext,
                    runId,
                    requestedBookmakerKey,
                    eventId,
                    platformProps,
                    cancellationToken);

            if (runId.HasValue && !shouldIgnoreRunContext && selectedRun is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var selectedRunScope = ParseTheOddsRunScope(selectedRun?.Notes);

            var snapshotDiagnostics = await LoadTheOddsLineSnapshotDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var predictionDiagnostics = selectedRun is null
                ? new Dictionary<Guid, TheOddsPredictionDiagnosticRow>()
                : await LoadTheOddsPredictionDiagnosticsAsync(dbContext, platformProps, selectedRun.RunId, cancellationToken);
            var playerRefDiagnostics = await LoadTheOddsPlayerRefDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var rows = new List<TheOddsBookmakerPredictionDropoutRow>(platformProps.Count);

            foreach (var platformProp in platformProps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventDate = DateOnly.FromDateTime((platformProp.EventStartTime ?? platformProp.ObservedAt).UtcDateTime);
                var historicalLookup = await featureService.AnalyzeHistoricalLookupAsync(
                    platformProp.PlayerId,
                    platformProp.PropTypeId,
                    eventDate,
                    cancellationToken);
                snapshotDiagnostics.TryGetValue(platformProp.PlatformPropGuid, out var snapshotDiagnostic);
                predictionDiagnostics.TryGetValue(platformProp.PropId, out var predictionDiagnostic);
                playerRefDiagnostics.TryGetValue(platformProp.PlayerId, out var playerRefDiagnostic);
                var runMatch = BuildTheOddsRunMatch(platformProp, selectedRun, selectedRunScope, requestedBookmakerKey, shouldIgnoreRunContext);

                var dropoutReason = DetermineTheOddsDropoutReason(
                    platformProp,
                    selectedRun,
                    runMatch,
                    shouldIgnoreRunContext,
                    snapshotDiagnostic,
                    predictionDiagnostic,
                    historicalLookup,
                    playerRefDiagnostic);

                rows.Add(new TheOddsBookmakerPredictionDropoutRow(
                    platformProp.PlatformPropId,
                    platformProp.PropId,
                    platformProp.PlayerId,
                    platformProp.PlayerName,
                    platformProp.PropTypeId,
                    platformProp.PropType,
                    platformProp.Label,
                    platformProp.MarketKey,
                    platformProp.PlatformName,
                    platformProp.EventId,
                    BuildTheOddsEventName(platformProp),
                    platformProp.EventStartTime,
                    platformProp.CurrentLine,
                    snapshotDiagnostic?.LatestObservedAt,
                    snapshotDiagnostic is not null && snapshotDiagnostic.Count > 0,
                    predictionDiagnostic is not null,
                    predictionDiagnostic?.PredictionRunId,
                    selectedRun?.RunId,
                    selectedRun?.Notes,
                    runMatch.RunMatchesEvent,
                    runMatch.RunMatchesBookmaker,
                    runMatch.RunMatchesMarket,
                    selectedRunScope?.EventId,
                    selectedRunScope?.Bookmakers,
                    selectedRunScope?.Markets,
                    historicalLookup.HistoricalGamesAvailable > 0,
                    historicalLookup.HistoricalGamesAvailable,
                    historicalLookup.HistoricalGamesAvailable,
                    playerRefDiagnostic?.ExternalRefCount ?? 0,
                    historicalLookup.HasMlbMappingForEquivalentPlayers,
                    !historicalLookup.HasMlbMappingForEquivalentPlayers
                        && (playerRefDiagnostic is null || !playerRefDiagnostic.HasMlbStatsMapping),
                    historicalLookup.HasSupportedPropTypeHistory,
                    dropoutReason));
            }

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                generatedAtUtc = DateTimeOffset.UtcNow,
                filters = new
                {
                    bookmaker = requestedBookmaker,
                    normalizedBookmaker = requestedBookmakerKey,
                    eventId,
                    requestedRunId = runId,
                    evaluatedRunId = selectedRun?.RunId,
                    ignoreRunContext = shouldIgnoreRunContext
                },
                predictionRunContext = selectedRun,
                summary = BuildTheOddsBookmakerPredictionDropoutSummary(requestedBookmaker, rows),
                platformProps = rows,
                diagnosis = BuildTheOddsBookmakerPredictionDropoutDiagnosis(rows, shouldIgnoreRunContext)
            });
        })
        .WithName("TheOddsApiMlbBookmakerPredictionDropoutDebug")
        .WithSummary("Read-only prediction dropout diagnostic for persisted The Odds API MLB bookmaker rows.");

        endpoints.MapGet("/api/debug/the-odds/mlb/prediction-readiness-audit", async (
            Guid? runId,
            string? eventId,
            string? bookmaker,
            ApplicationDbContext dbContext,
            IPredictionFeatureService featureService,
            IPredictionProbabilityService probabilityService,
            Microsoft.Extensions.Options.IOptions<PredictionGenerationOptions> predictionOptions,
            CancellationToken cancellationToken) =>
        {
            var requestedBookmakerKey = string.IsNullOrWhiteSpace(bookmaker)
                ? null
                : NormalizeTheOddsBookmakerKey(bookmaker, bookmaker);
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .Where(row => string.IsNullOrWhiteSpace(eventId)
                    || string.Equals(row.EventId, eventId, StringComparison.OrdinalIgnoreCase))
                .Where(row => requestedBookmakerKey is null
                    || string.Equals(
                        NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                        requestedBookmakerKey,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(row => row.ObservedAt)
                .ThenBy(row => row.PlatformName)
                .ThenBy(row => row.PlayerName)
                .ThenBy(row => row.PropType)
                .ToList();
            var propIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
            var probabilityAnalysis = runId.HasValue
                ? await probabilityService.AnalyzeRunForPropsAsync(runId.Value, propIds, cancellationToken)
                : await probabilityService.AnalyzeLatestRunForPropsAsync(propIds, cancellationToken);

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var generatedAtUtc = DateTimeOffset.UtcNow;
            var snapshotDiagnostics = await LoadTheOddsLineSnapshotDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var playerRefDiagnostics = await LoadTheOddsPlayerRefDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var lineShoppingRows = BuildTheOddsLineShoppingRows(
                platformProps,
                probabilityAnalysis,
                generatedAtUtc.AddHours(-FreshnessWindowHours));
            var lineShoppingGroups = BuildTheOddsLineShoppingGroups(lineShoppingRows);
            var lineShoppingGroupKeys = lineShoppingGroups
                .Select(BuildTheOddsReadinessLineShoppingGroupKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var eligibleLineShoppingGroupKeys = lineShoppingGroups
                .Where(group => group.IsEligibleAsNormalRecommendation)
                .Select(BuildTheOddsReadinessLineShoppingGroupKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var predictionByPropId = probabilityAnalysis.Results
                .GroupBy(result => result.PropId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(result => result.RankingScore).First());
            var enabledPropTypes = predictionOptions.Value.EnabledPropTypes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeTheOddsPropTypeName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = new List<TheOddsPredictionReadinessAuditRow>(platformProps.Count);

            foreach (var platformProp in platformProps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventDate = DateOnly.FromDateTime((platformProp.EventStartTime ?? platformProp.ObservedAt).UtcDateTime);
                var lookup = await featureService.AnalyzeHistoricalLookupAsync(
                    platformProp.PlayerId,
                    platformProp.PropTypeId,
                    eventDate,
                    cancellationToken);
                snapshotDiagnostics.TryGetValue(platformProp.PlatformPropGuid, out var snapshotDiagnostic);
                playerRefDiagnostics.TryGetValue(platformProp.PlayerId, out var playerRefs);
                predictionByPropId.TryGetValue(platformProp.PropId, out var prediction);
                var lineShoppingKey = BuildTheOddsReadinessLineShoppingGroupKey(platformProp);
                var lineShoppingGroupFound = lineShoppingGroupKeys.Contains(lineShoppingKey);
                var eligibleAsNormalRecommendation = eligibleLineShoppingGroupKeys.Contains(lineShoppingKey);
                var stage = DetermineTheOddsPredictionReadinessDropoutStage(
                    platformProp,
                    snapshotDiagnostic,
                    playerRefs,
                    lookup,
                    prediction,
                    lineShoppingGroupFound,
                    eligibleAsNormalRecommendation,
                    enabledPropTypes);
                var reason = BuildTheOddsPredictionReadinessDropoutReason(
                    stage,
                    platformProp,
                    lookup,
                    prediction,
                    enabledPropTypes);

                rows.Add(new TheOddsPredictionReadinessAuditRow(
                    platformProp.PlatformPropId,
                    platformProp.PropId,
                    platformProp.PlayerId,
                    platformProp.PlayerName,
                    platformProp.PlatformName,
                    platformProp.EventId,
                    BuildTheOddsEventName(platformProp),
                    platformProp.EventStartTime,
                    platformProp.PropType,
                    platformProp.MarketKey,
                    platformProp.CurrentLine,
                    platformProp.ObservedAt,
                    snapshotDiagnostic is not null && snapshotDiagnostic.Count > 0,
                    lookup.HasMlbMappingForEquivalentPlayers || (playerRefs?.HasMlbStatsMapping ?? false),
                    playerRefs?.ExternalRefCount ?? 0,
                    lookup.HasSupportedPropTypeHistory,
                    lookup.HistoricalResultsForEquivalentPropTypes,
                    lookup.HistoricalGamesAvailable,
                    lookup.HistoricalGamesAvailable > 0 && lookup.SeasonGamesAvailable > 0,
                    prediction is not null,
                    prediction is null ? null : probabilityAnalysis.RunId,
                    lineShoppingGroupFound,
                    eligibleAsNormalRecommendation,
                    BuildTheOddsPredictionReadinessStatus(stage),
                    stage,
                    reason));
            }

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                generatedAtUtc,
                filters = new
                {
                    eventId,
                    bookmaker,
                    normalizedBookmaker = requestedBookmakerKey,
                    requestedRunId = runId,
                    evaluatedRunId = probabilityAnalysis.RunId
                },
                run = new
                {
                    runId = probabilityAnalysis.RunId,
                    probabilityAnalysis.ModelName,
                    probabilityAnalysis.ModelVersion,
                    probabilityAnalysis.CreatedAt,
                    probabilityAnalysis.PredictionCount
                },
                summary = BuildTheOddsPredictionReadinessSummary(rows),
                breakdowns = new
                {
                    byBookmaker = BuildTheOddsPredictionReadinessBreakdown(rows, row => row.Bookmaker, "bookmaker"),
                    byEvent = BuildTheOddsPredictionReadinessBreakdown(rows, row => row.EventName, "event"),
                    byPropType = BuildTheOddsPredictionReadinessBreakdown(rows, row => row.PropType, "propType"),
                    byMarketKey = BuildTheOddsPredictionReadinessBreakdown(rows, row => row.MarketKey ?? "Unknown", "marketKey"),
                    byPlayer = BuildTheOddsPredictionReadinessBreakdown(rows, row => row.PlayerName, "player"),
                    byDropoutStage = BuildTheOddsPredictionReadinessBreakdown(rows, row => row.DropoutStage, "dropoutStage"),
                    byDropoutReason = BuildTheOddsPredictionReadinessBreakdown(rows, row => row.DropoutReason, "dropoutReason")
                },
                topIssues = BuildTheOddsPredictionReadinessTopIssues(rows),
                diagnosis = BuildTheOddsPredictionReadinessDiagnosis(rows),
                platformProps = rows
            });
        })
        .WithName("TheOddsApiMlbPredictionReadinessAuditDebug")
        .WithSummary("Read-only pipeline readiness audit for persisted The Odds API MLB platform props.");

        endpoints.MapGet("/api/debug/the-odds/mlb/player-mapping-recovery-preview", async (
            string? bookmaker,
            string? eventId,
            string? playerName,
            ApplicationDbContext dbContext,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            var requestedBookmakerKey = string.IsNullOrWhiteSpace(bookmaker)
                ? null
                : NormalizeTheOddsBookmakerKey(bookmaker, bookmaker);
            var normalizedPlayerNameFilter = string.IsNullOrWhiteSpace(playerName)
                ? null
                : NormalizeTheOddsPlayerName(playerName);
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .Where(row => string.IsNullOrWhiteSpace(eventId)
                    || string.Equals(row.EventId, eventId, StringComparison.OrdinalIgnoreCase))
                .Where(row => requestedBookmakerKey is null
                    || string.Equals(
                        NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                        requestedBookmakerKey,
                        StringComparison.OrdinalIgnoreCase))
                .Where(row => normalizedPlayerNameFilter is null
                    || string.Equals(NormalizeTheOddsPlayerName(row.PlayerName), normalizedPlayerNameFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var snapshotDiagnostics = await LoadTheOddsLineSnapshotDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var playerRefDiagnostics = await LoadTheOddsPlayerRefDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var sourceReadinessByPlatformPropId = new Dictionary<Guid, PlayerHistoricalLookupDiagnostic>();

            foreach (var platformProp in platformProps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventDate = DateOnly.FromDateTime((platformProp.EventStartTime ?? platformProp.ObservedAt).UtcDateTime);
                sourceReadinessByPlatformPropId[platformProp.PlatformPropGuid] = await featureService.AnalyzeHistoricalLookupAsync(
                    platformProp.PlayerId,
                    platformProp.PropTypeId,
                    eventDate,
                    cancellationToken);
            }

            var unmappedPlatformProps = platformProps
                .Where(row =>
                {
                    var readiness = sourceReadinessByPlatformPropId[row.PlatformPropGuid];
                    playerRefDiagnostics.TryGetValue(row.PlayerId, out var sourceRefs);

                    return !readiness.HasMlbMappingForEquivalentPlayers
                        && (sourceRefs is null || !sourceRefs.HasMlbStatsMapping);
                })
                .ToList();
            var sourcePlayerNames = unmappedPlatformProps
                .Select(row => row.PlayerName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var candidatePlayers = await LoadTheOddsMlbCandidatePlayersAsync(dbContext, sourcePlayerNames, cancellationToken);
            var allCandidatePlayerIds = candidatePlayers.Select(player => player.PlayerId).Distinct().ToArray();
            var sourcePlayerIds = unmappedPlatformProps.Select(row => row.PlayerId).Distinct().ToArray();
            var playerIdsForRefs = allCandidatePlayerIds.Concat(sourcePlayerIds).Distinct().ToArray();
            var externalRefsByPlayerId = await LoadTheOddsExternalRefsByPlayerIdAsync(dbContext, playerIdsForRefs, cancellationToken);
            var candidateHistoryCounts = await LoadTheOddsPlayerHistoricalResultCountsAsync(dbContext, allCandidatePlayerIds, cancellationToken);
            var sourceHistoryCounts = await LoadTheOddsPlayerHistoricalResultCountsAsync(dbContext, sourcePlayerIds, cancellationToken);
            var candidatePropTypeResultCounts = await LoadTheOddsPlayerPropTypeResultCountsAsync(dbContext, allCandidatePlayerIds, cancellationToken);
            var previewRows = BuildTheOddsPlayerMappingRecoveryPreviewRows(
                unmappedPlatformProps,
                snapshotDiagnostics,
                sourceReadinessByPlatformPropId,
                externalRefsByPlayerId,
                sourceHistoryCounts,
                candidatePlayers,
                candidateHistoryCounts,
                candidatePropTypeResultCounts);

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                generatedAtUtc = DateTimeOffset.UtcNow,
                filters = new
                {
                    bookmaker,
                    normalizedBookmaker = requestedBookmakerKey,
                    eventId,
                    playerName
                },
                summary = BuildTheOddsPlayerMappingRecoverySummary(previewRows),
                impactPreview = BuildTheOddsPlayerMappingRecoveryImpactPreview(previewRows),
                players = previewRows
            });
        })
        .WithName("TheOddsApiMlbPlayerMappingRecoveryPreviewDebug")
        .WithSummary("Read-only The Odds API MLB player mapping recovery preview.");

        endpoints.MapGet("/api/debug/the-odds/mlb/player-mapping-candidates", async (
            string? bookmaker,
            string? playerName,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            var requestedBookmakerKey = string.IsNullOrWhiteSpace(bookmaker)
                ? null
                : NormalizeTheOddsBookmakerKey(bookmaker, bookmaker);
            var normalizedPlayerNameFilter = string.IsNullOrWhiteSpace(playerName)
                ? null
                : NormalizeTheOddsPlayerName(playerName);
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .Where(row => requestedBookmakerKey is null
                    || string.Equals(
                        NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                        requestedBookmakerKey,
                        StringComparison.OrdinalIgnoreCase))
                .Where(row => normalizedPlayerNameFilter is null
                    || string.Equals(NormalizeTheOddsPlayerName(row.PlayerName), normalizedPlayerNameFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var snapshotDiagnostics = await LoadTheOddsLineSnapshotDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var playerRefDiagnostics = await LoadTheOddsPlayerRefDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var sourceReadinessByPlatformPropId = new Dictionary<Guid, PlayerHistoricalLookupDiagnostic>();

            foreach (var platformProp in platformProps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventDate = DateOnly.FromDateTime((platformProp.EventStartTime ?? platformProp.ObservedAt).UtcDateTime);
                sourceReadinessByPlatformPropId[platformProp.PlatformPropGuid] = await featureService.AnalyzeHistoricalLookupAsync(
                    platformProp.PlayerId,
                    platformProp.PropTypeId,
                    eventDate,
                    cancellationToken);
            }

            var unmappedPlatformProps = platformProps
                .Where(row =>
                {
                    var readiness = sourceReadinessByPlatformPropId[row.PlatformPropGuid];
                    playerRefDiagnostics.TryGetValue(row.PlayerId, out var sourceRefs);

                    return !readiness.HasMlbMappingForEquivalentPlayers
                        && (sourceRefs is null || !sourceRefs.HasMlbStatsMapping);
                })
                .ToList();
            var seasons = BuildTheOddsCandidateSearchSeasons(unmappedPlatformProps);
            IReadOnlyList<MlbPlayerRecord> providerPlayers;
            try
            {
                providerPlayers = await LoadTheOddsMlbProviderPlayersAsync(mlbResultsProvider, seasons, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    title: "MLB Stats API player lookup failed.",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var rows = BuildTheOddsMlbManualMappingCandidateRows(
                unmappedPlatformProps,
                snapshotDiagnostics,
                providerPlayers,
                playerRefDiagnostics,
                await LoadTheOddsMlbProviderRefConflictsAsync(dbContext, providerPlayers, cancellationToken));

            return Results.Ok(new
            {
                provider = "mlb",
                dataSource = "MLB Stats API",
                generatedAtUtc = DateTimeOffset.UtcNow,
                filters = new
                {
                    bookmaker,
                    normalizedBookmaker = requestedBookmakerKey,
                    playerName
                },
                seasonsSearched = seasons,
                summary = new
                {
                    sourcePlayerCount = rows.Count,
                    autoApproveCandidateCount = rows.Count(row => row.Recommendation == "AutoApproveCandidate"),
                    manualReviewCount = rows.Count(row => row.Recommendation == "ManualReview"),
                    noCandidateCount = rows.Count(row => row.Recommendation == "NoCandidate")
                },
                players = rows
            });
        })
        .WithName("TheOddsApiMlbPlayerMappingCandidatesDebug")
        .WithSummary("Returns MLB Stats API candidates for unmapped The Odds API MLB players.");

        endpoints.MapGet("/api/debug/the-odds/mlb/player-mapping-candidates/bulk", async (
            string? eventId,
            string? bookmaker,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            var requestedBookmakerKey = string.IsNullOrWhiteSpace(bookmaker)
                ? null
                : NormalizeTheOddsBookmakerKey(bookmaker, bookmaker);
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .Where(row => string.IsNullOrWhiteSpace(eventId)
                    || string.Equals(row.EventId, eventId, StringComparison.OrdinalIgnoreCase))
                .Where(row => requestedBookmakerKey is null
                    || string.Equals(
                        NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                        requestedBookmakerKey,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
            var playerRefDiagnostics = await LoadTheOddsPlayerRefDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var sourceReadinessByPlatformPropId = new Dictionary<Guid, PlayerHistoricalLookupDiagnostic>();

            foreach (var platformProp in platformProps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventDate = DateOnly.FromDateTime((platformProp.EventStartTime ?? platformProp.ObservedAt).UtcDateTime);
                sourceReadinessByPlatformPropId[platformProp.PlatformPropGuid] = await featureService.AnalyzeHistoricalLookupAsync(
                    platformProp.PlayerId,
                    platformProp.PropTypeId,
                    eventDate,
                    cancellationToken);
            }

            var unmappedPlatformProps = platformProps
                .Where(row =>
                {
                    var readiness = sourceReadinessByPlatformPropId[row.PlatformPropGuid];
                    playerRefDiagnostics.TryGetValue(row.PlayerId, out var sourceRefs);

                    return !readiness.HasMlbMappingForEquivalentPlayers
                        && (sourceRefs is null || !sourceRefs.HasMlbStatsMapping);
                })
                .ToList();
            var seasons = BuildTheOddsCandidateSearchSeasons(unmappedPlatformProps);
            IReadOnlyList<MlbPlayerRecord> providerPlayers;
            try
            {
                providerPlayers = await LoadTheOddsMlbProviderPlayersAsync(mlbResultsProvider, seasons, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    title: "MLB Stats API player lookup failed.",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var sourcePlayerIds = unmappedPlatformProps.Select(row => row.PlayerId).Distinct().ToArray();
            var externalRefsByPlayerId = await LoadTheOddsExternalRefsByPlayerIdAsync(dbContext, sourcePlayerIds, cancellationToken);
            var rows = BuildTheOddsMlbBulkMappingCandidateRows(
                unmappedPlatformProps,
                providerPlayers,
                externalRefsByPlayerId,
                await LoadTheOddsMlbProviderRefConflictsAsync(dbContext, providerPlayers, cancellationToken));

            return Results.Ok(new
            {
                provider = "mlb",
                dataSource = "MLB Stats API",
                generatedAtUtc = DateTimeOffset.UtcNow,
                filters = new
                {
                    eventId,
                    bookmaker,
                    normalizedBookmaker = requestedBookmakerKey
                },
                seasonsSearched = seasons,
                summary = BuildTheOddsMlbBulkMappingCandidateSummary(rows),
                autoApprovePlan = BuildTheOddsMlbBulkAutoApprovePlan(rows),
                players = rows
            });
        })
        .WithName("TheOddsApiMlbBulkPlayerMappingCandidatesDebug")
        .WithSummary("Returns MLB Stats API mapping candidates for all unmapped The Odds API MLB players.");

        endpoints.MapGet("/api/debug/the-odds/mlb/player-alias-link-preview", async (
            string? playerName,
            Guid? sourcePlayerId,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var rows = await BuildTheOddsMlbAliasLinkPreviewRowsAsync(
                dbContext,
                playerName,
                sourcePlayerId,
                cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                filters = new
                {
                    playerName,
                    sourcePlayerId
                },
                summary = new
                {
                    sourcePlayers = rows.Count,
                    safeAliasLinkCount = rows.Count(row => row.Recommendation == "SafeAliasLink"),
                    manualReviewCount = rows.Count(row => row.Recommendation == "ManualReview"),
                    alreadyLinkedCount = rows.Count(row => row.AlreadyLinked),
                    notLinkableCount = rows.Count(row => row.Recommendation == "NotLinkable"),
                    affectedPlatformProps = rows.Sum(row => row.AffectedPlatformProps)
                },
                players = rows
            });
        })
        .WithName("TheOddsApiMlbPlayerAliasLinkPreviewDebug")
        .WithSummary("Read-only preview of safe The Odds API MLB alias links to existing MLB-mapped canonical players.");

        endpoints.MapPost("/api/debug/the-odds/mlb/player-alias-link", async (
            TheOddsMlbPlayerAliasLinkRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var dryRun = request.DryRun ?? true;
            if (request.SourcePlayerId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "sourcePlayerId is required." });
            }

            if (request.TargetCanonicalPlayerId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "targetCanonicalPlayerId is required." });
            }

            var sourcePlayer = await dbContext.Players
                .AsNoTracking()
                .SingleOrDefaultAsync(player => player.Id == request.SourcePlayerId, cancellationToken);
            if (sourcePlayer is null)
            {
                return Results.NotFound(new { error = $"Source player '{request.SourcePlayerId}' was not found." });
            }

            var targetPlayer = await dbContext.Players
                .AsNoTracking()
                .SingleOrDefaultAsync(player => player.Id == request.TargetCanonicalPlayerId, cancellationToken);
            if (targetPlayer is null)
            {
                return Results.NotFound(new { error = $"Target canonical player '{request.TargetCanonicalPlayerId}' was not found." });
            }

            var rows = await BuildTheOddsMlbAliasLinkPreviewRowsAsync(
                dbContext,
                sourcePlayer.DisplayName,
                request.SourcePlayerId,
                cancellationToken);
            var row = rows.SingleOrDefault(candidate => candidate.SourcePlayerId == request.SourcePlayerId);
            var targetCandidate = row?.Candidates.SingleOrDefault(candidate => candidate.TargetCanonicalPlayerId == request.TargetCanonicalPlayerId);
            if (row is null || targetCandidate is null)
            {
                return Results.BadRequest(new
                {
                    error = "The requested source/target pair is not present in the safe alias preview candidates.",
                    sourcePlayerId = request.SourcePlayerId,
                    targetCanonicalPlayerId = request.TargetCanonicalPlayerId
                });
            }

            var errors = new List<string>();
            if (row.Recommendation != "SafeAliasLink" && !row.AlreadyLinked)
            {
                errors.Add($"Alias link rejected because preview recommendation is {row.Recommendation}.");
            }

            if (targetCandidate.RiskFlags.Contains("MultipleCandidates") || targetCandidate.RiskFlags.Contains("MaxMuncyManualReview"))
            {
                errors.Add("Alias link rejected because the match is ambiguous.");
            }

            var targetMlbRefs = await dbContext.PlayerExternalRefs
                .AsNoTracking()
                .Where(reference => reference.PlayerId == request.TargetCanonicalPlayerId
                    && reference.Provider == "mlb")
                .ToArrayAsync(cancellationToken);
            if (targetMlbRefs.Length == 0)
            {
                errors.Add("Target canonical player must already have an MLB external ref.");
            }

            var sourceMlbRefs = await dbContext.PlayerExternalRefs
                .AsNoTracking()
                .Where(reference => reference.PlayerId == request.SourcePlayerId
                    && reference.Provider == "mlb")
                .ToArrayAsync(cancellationToken);
            if (sourceMlbRefs.Any(reference => targetMlbRefs.All(targetRef => !string.Equals(
                targetRef.ProviderPlayerId,
                reference.ProviderPlayerId,
                StringComparison.OrdinalIgnoreCase))))
            {
                errors.Add("Source player already has a conflicting MLB external ref.");
            }

            var aliasProviderPlayerId = BuildTheOddsMlbAliasProviderPlayerId(
                request.SourcePlayerId,
                request.TargetCanonicalPlayerId);
            var existingAlias = await dbContext.PlayerExternalRefs
                .SingleOrDefaultAsync(reference => reference.Provider == MlbAliasProvider
                    && reference.ProviderPlayerId == aliasProviderPlayerId, cancellationToken);
            var alreadyLinked = existingAlias is not null || row.AlreadyLinked;

            if (errors.Count > 0)
            {
                return Results.BadRequest(new
                {
                    sourcePlayerId = request.SourcePlayerId,
                    sourcePlayerName = sourcePlayer.DisplayName,
                    targetCanonicalPlayerId = request.TargetCanonicalPlayerId,
                    targetCanonicalPlayerName = targetPlayer.DisplayName,
                    targetMlbStatsId = targetMlbRefs.FirstOrDefault()?.ProviderPlayerId,
                    dryRun,
                    createdLink = false,
                    alreadyLinked,
                    wouldResolveMissingMapping = false,
                    errors
                });
            }

            var createdLink = false;
            if (!dryRun && existingAlias is null)
            {
                var observedAt = DateTimeOffset.UtcNow;
                dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
                {
                    Id = Guid.NewGuid(),
                    PlayerId = request.SourcePlayerId,
                    Provider = MlbAliasProvider,
                    ProviderPlayerId = aliasProviderPlayerId,
                    ProviderDisplayName = $"Alias to {targetPlayer.DisplayName} ({targetMlbRefs.First().ProviderPlayerId})",
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                createdLink = true;
                alreadyLinked = true;
            }

            return Results.Ok(new
            {
                request.SourcePlayerId,
                sourcePlayerName = sourcePlayer.DisplayName,
                request.TargetCanonicalPlayerId,
                targetCanonicalPlayerName = targetPlayer.DisplayName,
                targetMlbStatsId = targetMlbRefs.FirstOrDefault()?.ProviderPlayerId,
                affectedPlatformProps = row.AffectedPlatformProps,
                dryRun,
                createdLink,
                alreadyLinked,
                wouldResolveMissingMapping = targetMlbRefs.Length > 0,
                approvedBy = request.ApprovedBy,
                request.Reason,
                errors = Array.Empty<string>()
            });
        })
        .WithName("TheOddsApiMlbPlayerAliasLinkDebug")
        .WithSummary("Creates an explicit alias bridge from a The Odds API MLB player to an existing MLB-mapped canonical player.");

        endpoints.MapPost("/api/debug/the-odds/mlb/player-alias-link/auto-safe", async (
            TheOddsMlbAutoSafeAliasLinkRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var dryRun = request.DryRun ?? true;
            var previewRows = await BuildTheOddsMlbAliasLinkPreviewRowsAsync(
                dbContext,
                playerName: null,
                sourcePlayerId: null,
                cancellationToken);
            var rows = new List<TheOddsMlbAutoSafeAliasLinkRow>();
            var linksCreated = 0;

            var previewOperations = previewRows
                .SelectMany(previewRow => previewRow.Candidates.DefaultIfEmpty(), (previewRow, candidate) => new { previewRow, candidate })
                .GroupBy(row => new
                {
                    row.previewRow.SourcePlayerId,
                    TargetCanonicalPlayerId = row.candidate?.TargetCanonicalPlayerId
                })
                .Select(group => new
                {
                    PreviewRows = group.Select(row => row.previewRow).ToArray(),
                    Candidates = group
                        .Select(row => row.candidate)
                        .Where(candidate => candidate is not null)
                        .Cast<TheOddsMlbAliasLinkCandidateRow>()
                        .GroupBy(candidate => candidate.TargetCanonicalPlayerId)
                        .Select(candidateGroup => candidateGroup.First())
                        .ToArray()
                })
                .ToArray();

            foreach (var operation in previewOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var previewRow = operation.PreviewRows
                    .OrderBy(row => row.Recommendation == "SafeAliasLink" ? 0 : 1)
                    .ThenByDescending(row => row.AffectedPlatformProps)
                    .First();
                try
                {
                TheOddsMlbAliasLinkCandidateRow? candidate = null;
                if (operation.Candidates.Length == 1)
                {
                    candidate = operation.Candidates[0];
                }

                var riskFlags = candidate?.RiskFlags ?? [];
                var errors = new List<string>();
                var action = "SkippedUnsafe";
                var reason = "Preview row is not safe for automatic alias linking.";
                var targetMlbStatsId = candidate?.TargetMlbStatsId;
                var alreadyLinked = operation.PreviewRows.Any(row => row.AlreadyLinked) || candidate?.AlreadyLinked == true;

                if (!operation.PreviewRows.Any(row => row.Recommendation == "SafeAliasLink") && !alreadyLinked)
                {
                    errors.Add($"Recommendation is {previewRow.Recommendation}, not SafeAliasLink.");
                }

                var distinctCandidateCount = operation.Candidates.Length;
                if (candidate is null || distinctCandidateCount != 1)
                {
                    errors.Add($"Expected exactly one candidate; found {distinctCandidateCount}.");
                }

                if (IsTheOddsAutoAliasExcludedName(previewRow.SourcePlayerName))
                {
                    errors.Add("Player is explicitly excluded from auto-safe alias linking.");
                }

                if (riskFlags.Contains("MultipleCandidates") || riskFlags.Contains("MaxMuncyManualReview"))
                {
                    errors.Add("Candidate has ambiguous-name or multiple-candidate risk flags.");
                }

                if (candidate is not null)
                {
                    var targetMlbRefs = await dbContext.PlayerExternalRefs
                        .AsNoTracking()
                        .Where(reference => reference.PlayerId == candidate.TargetCanonicalPlayerId
                            && reference.Provider == "mlb")
                        .ToArrayAsync(cancellationToken);
                    targetMlbStatsId = targetMlbRefs.FirstOrDefault()?.ProviderPlayerId ?? candidate.TargetMlbStatsId;

                    if (targetMlbRefs.Length == 0)
                    {
                        errors.Add("Target canonical player does not have an MLB external ref.");
                    }

                    var sourceMlbRefs = await dbContext.PlayerExternalRefs
                        .AsNoTracking()
                        .Where(reference => reference.PlayerId == previewRow.SourcePlayerId
                            && reference.Provider == "mlb")
                        .ToArrayAsync(cancellationToken);
                    if (sourceMlbRefs.Any(reference => targetMlbRefs.All(targetRef => !string.Equals(
                        targetRef.ProviderPlayerId,
                        reference.ProviderPlayerId,
                        StringComparison.OrdinalIgnoreCase))))
                    {
                        errors.Add("Source player has a conflicting MLB external ref.");
                    }

                    var aliasProviderPlayerId = BuildTheOddsMlbAliasProviderPlayerId(
                        previewRow.SourcePlayerId,
                        candidate.TargetCanonicalPlayerId);
                    var existingAliases = await dbContext.PlayerExternalRefs
                        .AsNoTracking()
                        .Where(reference => reference.Provider == MlbAliasProvider
                            && reference.ProviderPlayerId == aliasProviderPlayerId)
                        .ToArrayAsync(cancellationToken);
                    alreadyLinked = alreadyLinked || existingAliases.Length > 0;

                    if (errors.Count == 0)
                    {
                        if (alreadyLinked)
                        {
                            action = "AlreadyLinked";
                            reason = "Alias link already exists.";
                        }
                        else if (dryRun)
                        {
                            action = "WouldCreate";
                            reason = "Dry run: alias link would be created.";
                        }
                        else
                        {
                            var observedAt = DateTimeOffset.UtcNow;
                            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
                            {
                                Id = Guid.NewGuid(),
                                PlayerId = previewRow.SourcePlayerId,
                                Provider = MlbAliasProvider,
                                ProviderPlayerId = aliasProviderPlayerId,
                                ProviderDisplayName = $"Alias to {candidate.TargetCanonicalPlayerName} ({targetMlbStatsId})",
                                CreatedAt = observedAt,
                                UpdatedAt = observedAt
                            });
                            await dbContext.SaveChangesAsync(cancellationToken);
                            linksCreated++;
                            alreadyLinked = true;
                            action = "Created";
                            reason = "Alias link created.";
                        }
                    }
                }

                rows.Add(new TheOddsMlbAutoSafeAliasLinkRow(
                    previewRow.SourcePlayerId,
                    previewRow.SourcePlayerName,
                    candidate?.TargetCanonicalPlayerId,
                    candidate?.TargetCanonicalPlayerName,
                    targetMlbStatsId,
                    action,
                    reason,
                    riskFlags,
                    errors,
                    alreadyLinked));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    rows.Add(new TheOddsMlbAutoSafeAliasLinkRow(
                        previewRow.SourcePlayerId,
                        previewRow.SourcePlayerName,
                        null,
                        null,
                        null,
                        "SkippedUnsafe",
                        "Auto-safe alias operation failed for this row.",
                        [],
                        [$"{exception.GetType().Name}: {exception.Message}"],
                        previewRow.AlreadyLinked));
                }
            }

            return Results.Ok(new
            {
                dryRun,
                candidatesEvaluated = previewRows.Count,
                safeLinksFound = rows.Count(row => row.Action is "WouldCreate" or "Created" or "AlreadyLinked"),
                linksCreated,
                alreadyLinked = rows.Count(row => row.AlreadyLinked),
                skippedAmbiguous = rows.Count(row => row.Errors.Any(error => error.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)
                    || error.Contains("excluded", StringComparison.OrdinalIgnoreCase)
                    || error.Contains("exactly one", StringComparison.OrdinalIgnoreCase))),
                skippedUnsafe = rows.Count(row => row.Action == "SkippedUnsafe"),
                approvedBy = request.ApprovedBy,
                reason = request.Reason,
                rows
            });
        })
        .WithName("TheOddsApiMlbPlayerAliasLinkAutoSafeDebug")
        .WithSummary("Dry-runs or commits only auto-safe The Odds API MLB alias links.");

        endpoints.MapPost("/api/debug/the-odds/mlb/player-mapping-approve", async (
            bool? force,
            TheOddsMlbPlayerMappingApprovalRequest request,
            ApplicationDbContext dbContext,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            if (request.SourcePlayerId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "sourcePlayerId is required." });
            }

            if (string.IsNullOrWhiteSpace(request.MlbStatsId))
            {
                return Results.BadRequest(new { error = "mlbStatsId is required." });
            }

            var sourcePlayer = await dbContext.Players
                .Include(player => player.League)
                .SingleOrDefaultAsync(player => player.Id == request.SourcePlayerId, cancellationToken);
            if (sourcePlayer is null)
            {
                return Results.NotFound(new { error = $"Source player '{request.SourcePlayerId}' was not found." });
            }

            if (sourcePlayer.League is null || !string.Equals(sourcePlayer.League.Code, "MLB", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "The Odds MLB mapping approvals require a source player in the MLB league." });
            }

            var beforeMappings = await LoadTheOddsExternalRefsByPlayerIdAsync(dbContext, [sourcePlayer.Id], cancellationToken);
            var before = beforeMappings.GetValueOrDefault(sourcePlayer.Id) ?? [];
            var existingSourceMlbRef = await dbContext.PlayerExternalRefs
                .SingleOrDefaultAsync(reference => reference.PlayerId == sourcePlayer.Id
                    && reference.Provider == "mlb", cancellationToken);
            if (existingSourceMlbRef is not null
                && !string.Equals(existingSourceMlbRef.ProviderPlayerId, request.MlbStatsId.Trim(), StringComparison.OrdinalIgnoreCase)
                && force != true)
            {
                return Results.Conflict(new
                {
                    error = "Source player already has a conflicting MLB Stats mapping. Re-submit with force=true to replace it.",
                    existingMapping = ToTheOddsExternalRefSummary(existingSourceMlbRef)
                });
            }

            var existingRefsForMlbId = await dbContext.PlayerExternalRefs
                .AsNoTracking()
                .Where(reference => reference.Provider == "mlb"
                    && reference.ProviderPlayerId == request.MlbStatsId.Trim())
                .Select(reference => new
                {
                    reference.PlayerId,
                    reference.Provider,
                    reference.ProviderPlayerId,
                    reference.ProviderDisplayName
                })
                .ToListAsync(cancellationToken);
            var refsOnOtherPlayers = existingRefsForMlbId
                .Where(reference => reference.PlayerId != sourcePlayer.Id)
                .ToArray();
            if (refsOnOtherPlayers.Length > 0)
            {
                return Results.Conflict(new
                {
                    error = "MLB Stats ID is already mapped to a different canonical player. This endpoint does not merge players.",
                    existingMappings = refsOnOtherPlayers
                });
            }

            var providerDisplayName = request.ProviderDisplayName
                ?? sourcePlayer.DisplayName
                ?? request.MlbStatsId.Trim();
            var observedAt = DateTimeOffset.UtcNow;

            if (existingSourceMlbRef is null)
            {
                dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
                {
                    Id = Guid.NewGuid(),
                    PlayerId = sourcePlayer.Id,
                    Provider = "mlb",
                    ProviderPlayerId = request.MlbStatsId.Trim(),
                    ProviderDisplayName = providerDisplayName,
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                });
            }
            else
            {
                existingSourceMlbRef.ProviderPlayerId = request.MlbStatsId.Trim();
                existingSourceMlbRef.ProviderDisplayName = providerDisplayName;
                existingSourceMlbRef.UpdatedAt = observedAt;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var afterMappings = await LoadTheOddsExternalRefsByPlayerIdAsync(dbContext, [sourcePlayer.Id], cancellationToken);
            var affectedPlatformProps = await LoadTheOddsPlatformPropsForPlayerAsync(dbContext, sourcePlayer.Id, cancellationToken);
            var affectedPropTypeIds = affectedPlatformProps.Select(row => row.PropTypeId).Distinct().ToArray();
            var estimatedPredictionsRecoverable = 0;
            var historicalFeatureAvailable = false;
            var maxHistoricalSampleSize = 0;
            foreach (var platformProp in affectedPlatformProps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventDate = DateOnly.FromDateTime((platformProp.EventStartTime ?? platformProp.ObservedAt).UtcDateTime);
                var lookup = await featureService.AnalyzeHistoricalLookupAsync(
                    sourcePlayer.Id,
                    platformProp.PropTypeId,
                    eventDate,
                    cancellationToken);
                if (lookup.HistoricalGamesAvailable > 0)
                {
                    historicalFeatureAvailable = true;
                    estimatedPredictionsRecoverable++;
                    maxHistoricalSampleSize = Math.Max(maxHistoricalSampleSize, lookup.HistoricalGamesAvailable);
                }
            }

            return Results.Ok(new
            {
                sourcePlayer = new
                {
                    sourcePlayer.Id,
                    sourcePlayer.DisplayName,
                    league = sourcePlayer.League.Code
                },
                approval = new
                {
                    provider = "mlb",
                    mlbStatsId = request.MlbStatsId.Trim(),
                    approvedBy = request.ApprovedBy,
                    reason = request.Reason,
                    forced = force == true,
                    providerDisplayName
                },
                before = before,
                after = afterMappings.GetValueOrDefault(sourcePlayer.Id) ?? [],
                verification = new
                {
                    hasMlbStatsMapping = true,
                    historicalFeatureAvailable,
                    historicalSampleSize = maxHistoricalSampleSize,
                    affectedPlatformProps = affectedPlatformProps.Count,
                    affectedPropTypes = affectedPlatformProps.Select(row => row.PropType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    estimatedPredictionsRecoverable
                }
            });
        })
        .WithName("TheOddsApiMlbPlayerMappingApproveDebug")
        .WithSummary("Manually approves an MLB Stats mapping for one The Odds API MLB source player.");

        endpoints.MapGet("/api/debug/the-odds/mlb/historical-feature-gap-audit", async (
            string? propType,
            string? playerName,
            string? eventId,
            ApplicationDbContext dbContext,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            var normalizedPropTypeFilter = string.IsNullOrWhiteSpace(propType)
                ? null
                : NormalizeTheOddsPropTypeName(propType);
            var normalizedPlayerNameFilter = string.IsNullOrWhiteSpace(playerName)
                ? null
                : NormalizeTheOddsPlayerName(playerName);
            var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
            var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
                .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                .Where(row => string.IsNullOrWhiteSpace(eventId)
                    || string.Equals(row.EventId, eventId, StringComparison.OrdinalIgnoreCase))
                .Where(row => normalizedPropTypeFilter is null
                    || string.Equals(NormalizeTheOddsPropTypeName(row.PropType), normalizedPropTypeFilter, StringComparison.OrdinalIgnoreCase))
                .Where(row => normalizedPlayerNameFilter is null
                    || string.Equals(NormalizeTheOddsPlayerName(row.PlayerName), normalizedPlayerNameFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var snapshotDiagnostics = await LoadTheOddsLineSnapshotDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var playerRefDiagnostics = await LoadTheOddsPlayerRefDiagnosticsAsync(dbContext, platformProps, cancellationToken);
            var equivalentPropTypeIdsById = await LoadTheOddsEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var playerRows = await LoadTheOddsMlbPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var playerRowsById = playerRows.ToDictionary(row => row.PlayerId);
            var playerIdsByNormalizedName = playerRows
                .GroupBy(row => row.NormalizedName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(row => row.PlayerId).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
            var mlbMappedPlayerIdsByMlbStatsId = await LoadTheOddsMlbMappedPlayerIdsByStatsIdAsync(dbContext, cancellationToken);
            var propResultRows = await LoadTheOddsMlbPropResultRowsAsync(dbContext, cancellationToken);
            var rows = new List<TheOddsHistoricalFeatureGapAuditRow>();

            foreach (var platformProp in platformProps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventDate = DateOnly.FromDateTime((platformProp.EventStartTime ?? platformProp.ObservedAt).UtcDateTime);
                var lookup = await featureService.AnalyzeHistoricalLookupAsync(
                    platformProp.PlayerId,
                    platformProp.PropTypeId,
                    eventDate,
                    cancellationToken);
                playerRefDiagnostics.TryGetValue(platformProp.PlayerId, out var playerRefs);
                var hasMlbStatsMapping = lookup.HasMlbMappingForEquivalentPlayers || (playerRefs?.HasMlbStatsMapping ?? false);
                var historicalFeatureAvailable = lookup.HistoricalGamesAvailable > 0 && lookup.SeasonGamesAvailable > 0;

                if (!hasMlbStatsMapping || !lookup.HasSupportedPropTypeHistory || historicalFeatureAvailable)
                {
                    continue;
                }

                var equivalentPropTypeIds = equivalentPropTypeIdsById.TryGetValue(platformProp.PropTypeId, out var propTypeIds)
                    ? propTypeIds
                    : [platformProp.PropTypeId];
                var directPlayerIds = new[] { platformProp.PlayerId };
                var aliasPlayerIds = lookup.EquivalentPlayerIds.Where(playerId => playerId != platformProp.PlayerId).Distinct().ToArray();
                var sameNamePlayerIds = playerIdsByNormalizedName.TryGetValue(NormalizeTheOddsPlayerName(platformProp.PlayerName), out var sameNameIds)
                    ? sameNameIds.Where(playerId => playerId != platformProp.PlayerId).Distinct().ToArray()
                    : [];
                var mlbStatsIds = playerRowsById.TryGetValue(platformProp.PlayerId, out var sourcePlayerRow)
                    ? sourcePlayerRow.MlbStatsIds
                    : [];
                var mlbStatsMappedPlayerIds = mlbStatsIds
                    .SelectMany(mlbStatsId => mlbMappedPlayerIdsByMlbStatsId.GetValueOrDefault(mlbStatsId) ?? [])
                    .Where(playerId => playerId != platformProp.PlayerId)
                    .Distinct()
                    .ToArray();
                var relatedPlayerIds = directPlayerIds
                    .Concat(aliasPlayerIds)
                    .Concat(sameNamePlayerIds)
                    .Concat(mlbStatsMappedPlayerIds)
                    .Distinct()
                    .ToArray();
                var relatedEquivalentResults = propResultRows
                    .Where(result => relatedPlayerIds.Contains(result.PlayerId)
                        && equivalentPropTypeIds.Contains(result.PropTypeId))
                    .ToArray();
                var beforeEventResults = relatedEquivalentResults
                    .Where(result => result.EventDate < eventDate)
                    .ToArray();
                var afterEventResults = relatedEquivalentResults
                    .Where(result => result.EventDate >= eventDate)
                    .ToArray();
                var pitcherStrikeoutsResults = propResultRows
                    .Where(result => relatedPlayerIds.Contains(result.PlayerId)
                        && NormalizeTheOddsPropTypeName(result.PropTypeName) == "PITCHERSTRIKEOUTS")
                    .ToArray();
                var genericStrikeoutsResults = propResultRows
                    .Where(result => relatedPlayerIds.Contains(result.PlayerId)
                        && NormalizeTheOddsPropTypeName(result.PropTypeName) is "STRIKEOUTS" or "KS")
                    .ToArray();
                var directPlayerResultCount = CountTheOddsResults(propResultRows, directPlayerIds, equivalentPropTypeIds);
                var aliasLinkedPlayerResultCount = CountTheOddsResults(propResultRows, aliasPlayerIds, equivalentPropTypeIds);
                var sameNamePlayerResultCount = CountTheOddsResults(propResultRows, sameNamePlayerIds, equivalentPropTypeIds);
                var mlbStatsMappedPlayerResultCount = CountTheOddsResults(propResultRows, mlbStatsMappedPlayerIds, equivalentPropTypeIds);
                var equivalentPropTypeResultCount = CountTheOddsResults(propResultRows, relatedPlayerIds, equivalentPropTypeIds);
                var rootCause = ClassifyTheOddsHistoricalFeatureGap(
                    platformProp,
                    directPlayerResultCount,
                    aliasLinkedPlayerResultCount,
                    sameNamePlayerResultCount,
                    mlbStatsMappedPlayerResultCount,
                    equivalentPropTypeResultCount,
                    pitcherStrikeoutsResults.Length,
                    genericStrikeoutsResults.Length,
                    beforeEventResults.Length,
                    afterEventResults.Length);

                rows.Add(new TheOddsHistoricalFeatureGapAuditRow(
                    platformProp.PlatformPropId,
                    platformProp.PlayerId,
                    platformProp.PlayerName,
                    mlbStatsIds.FirstOrDefault(),
                    platformProp.PlatformName,
                    platformProp.EventId,
                    BuildTheOddsEventName(platformProp),
                    platformProp.EventStartTime,
                    platformProp.PropType,
                    platformProp.MarketKey,
                    platformProp.CurrentLine,
                    snapshotDiagnostics.TryGetValue(platformProp.PlatformPropGuid, out var snapshot) && snapshot.Count > 0,
                    hasMlbStatsMapping,
                    lookup.HasSupportedPropTypeHistory,
                    historicalFeatureAvailable,
                    lookup.HistoricalGamesAvailable,
                    BuildTheOddsHistoricalFeatureGapReason(lookup),
                    directPlayerResultCount,
                    aliasLinkedPlayerResultCount,
                    sameNamePlayerResultCount,
                    mlbStatsMappedPlayerResultCount,
                    equivalentPropTypeResultCount,
                    pitcherStrikeoutsResults.Length,
                    genericStrikeoutsResults.Length,
                    beforeEventResults.Length,
                    afterEventResults.Length,
                    relatedEquivalentResults.Length == 0 ? null : relatedEquivalentResults.Min(result => result.EventDate),
                    relatedEquivalentResults.Length == 0 ? null : relatedEquivalentResults.Max(result => result.EventDate),
                    beforeEventResults
                        .OrderByDescending(result => result.EventDate)
                        .Take(10)
                        .Select(result => new TheOddsHistoricalResultSampleRow(
                            result.PlayerId,
                            playerRowsById.TryGetValue(result.PlayerId, out var resultPlayer) ? resultPlayer.DisplayName : "Unknown",
                            result.PropTypeId,
                            result.PropTypeName,
                            result.EventDate,
                            result.Value))
                        .ToArray(),
                    rootCause));
            }

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                generatedAtUtc = DateTimeOffset.UtcNow,
                filters = new
                {
                    propType,
                    playerName,
                    eventId
                },
                summary = BuildTheOddsHistoricalFeatureGapSummary(rows),
                breakdowns = new
                {
                    byPlayer = BuildTheOddsHistoricalFeatureGapBreakdown(rows, row => row.PlayerName, "player"),
                    byPropType = BuildTheOddsHistoricalFeatureGapBreakdown(rows, row => row.PropType, "propType"),
                    byMarketKey = BuildTheOddsHistoricalFeatureGapBreakdown(rows, row => row.MarketKey ?? "Unknown", "marketKey"),
                    byBookmaker = BuildTheOddsHistoricalFeatureGapBreakdown(rows, row => row.Bookmaker, "bookmaker"),
                    byEvent = BuildTheOddsHistoricalFeatureGapBreakdown(rows, row => row.EventName, "event"),
                    byLikelyRootCause = BuildTheOddsHistoricalFeatureGapBreakdown(rows, row => row.LikelyRootCause, "likelyRootCause")
                },
                recommendedFixes = BuildTheOddsHistoricalFeatureGapRecommendedFixes(rows),
                gaps = rows
            });
        })
        .WithName("TheOddsApiMlbHistoricalFeatureGapAuditDebug")
        .WithSummary("Read-only historical feature gap audit for The Odds API MLB props.");

        endpoints.MapGet("/api/debug/the-odds/mlb/ingestion-impact", async (
            Guid? runId,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            CancellationToken cancellationToken) =>
        {
            var probabilityAnalysis = runId.HasValue
                ? await probabilityService.AnalyzeRunAsync(runId.Value, cancellationToken)
                : await probabilityService.AnalyzeLatestRunAsync(cancellationToken);

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var generatedAtUtc = DateTimeOffset.UtcNow;
            var freshnessCutoff = generatedAtUtc.AddHours(-FreshnessWindowHours);
            var theOddsPlatformProps = await LoadTheOddsPlatformPropsAsync(dbContext, cancellationToken);
            var theOddsPropIds = theOddsPlatformProps.Select(row => row.PropId).ToHashSet();
            var predictionResults = probabilityAnalysis.Results
                .Where(result => theOddsPropIds.Contains(result.PropId))
                .OrderByDescending(result => result.RankingScore)
                .ToList();
            var predictionByPropId = predictionResults
                .GroupBy(result => result.PropId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(result => result.RankingScore).First());
            var rows = theOddsPlatformProps
                .Where(row => predictionByPropId.ContainsKey(row.PropId))
                .Select(row => BuildTheOddsImpactPredictionRow(
                    row,
                    predictionByPropId[row.PropId],
                    generatedAtUtc,
                    freshnessCutoff))
                .OrderByDescending(row => row.RankingScore)
                .ToList();

            var totalSnapshots = await dbContext.PropLineSnapshots
                .AsNoTracking()
                .CountAsync(snapshot => snapshot.PlatformProp != null
                    && snapshot.PlatformProp.Metadata != null
                    && EF.Functions.JsonContains(
                        snapshot.PlatformProp.Metadata,
                        JsonSerializer.SerializeToDocument(new { dataProvider = "The Odds API" })),
                    cancellationToken);
            var visibleRows = rows.Where(row => row.IsVisibleInResearchDashboard).ToList();
            var eligibleRows = rows.Where(row => row.IsEligibleAsNormalRecommendation).ToList();
            var bookmakerCounts = theOddsPlatformProps
                .GroupBy(row => row.PlatformName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    bookmaker = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.bookmaker)
                .ToList();
            var bookmakerPredictionCounts = rows
                .GroupBy(row => row.Bookmaker, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    bookmaker = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.bookmaker)
                .ToList();

            return Results.Ok(new
            {
                dataProvider = "The Odds API",
                run = new
                {
                    runId = probabilityAnalysis.RunId,
                    probabilityAnalysis.ModelName,
                    probabilityAnalysis.ModelVersion,
                    probabilityAnalysis.CreatedAt,
                    probabilityAnalysis.PredictionCount
                },
                persistedSummary = new
                {
                    totalTheOddsPlatformProps = theOddsPlatformProps.Count,
                    platformPropsByBookmaker = bookmakerCounts,
                    totalTheOddsLineSnapshots = totalSnapshots,
                    latestObservedTimeByBookmaker = theOddsPlatformProps
                        .GroupBy(row => row.PlatformName, StringComparer.OrdinalIgnoreCase)
                        .Select(group => new
                        {
                            bookmaker = group.Key,
                            latestObservedAt = group.Max(row => row.ObservedAt)
                        })
                        .OrderBy(row => row.bookmaker),
                    propTypeBreakdownByBookmaker = theOddsPlatformProps
                        .GroupBy(row => new { row.PlatformName, row.PropType })
                        .Select(group => new
                        {
                            bookmaker = group.Key.PlatformName,
                            propType = group.Key.PropType,
                            count = group.Count()
                        })
                        .OrderBy(row => row.bookmaker)
                        .ThenByDescending(row => row.count),
                    marketClassificationBreakdownByBookmaker = theOddsPlatformProps
                        .GroupBy(row => new { row.PlatformName, row.MarketClassification })
                        .Select(group => new
                        {
                            bookmaker = group.Key.PlatformName,
                            marketClassification = group.Key.MarketClassification,
                            count = group.Count()
                        })
                        .OrderBy(row => row.bookmaker)
                        .ThenByDescending(row => row.count)
                },
                predictionImpact = new
                {
                    totalPredictionsFromTheOddsApiRows = rows.Count,
                    predictionsByBookmaker = bookmakerPredictionCounts,
                    predictionsByPropType = rows
                        .GroupBy(row => row.PropType, StringComparer.OrdinalIgnoreCase)
                        .Select(group => new
                        {
                            propType = group.Key,
                            count = group.Count()
                        })
                        .OrderByDescending(row => row.count)
                        .ThenBy(row => row.propType),
                    visiblePredictionsFromTheOddsApi = visibleRows.Count,
                    normalRecommendationEligiblePredictionsFromTheOddsApi = eligibleRows.Count,
                    ineligibilityReasonCounts = CountReasons(rows.SelectMany(row => row.IneligibilityReasons)),
                    hiddenReasonCounts = CountReasons(rows.SelectMany(row => row.HiddenReasons))
                },
                topTheOddsPredictions = rows
                    .Take(100)
                    .Select((row, index) => new
                    {
                        rank = index + 1,
                        player = row.Player,
                        sportsbook = row.Bookmaker,
                        bookmaker = row.Bookmaker,
                        row.PlatformPropId,
                        propType = row.PropType,
                        line = row.Line,
                        priceOver = row.PriceOver,
                        priceUnder = row.PriceUnder,
                        row.ProbabilityOver,
                        row.ProbabilityUnder,
                        row.RankingScore,
                        row.MarketClassification,
                        row.IsVisibleInResearchDashboard,
                        row.IsEligibleAsNormalRecommendation,
                        row.HiddenReasons,
                        row.IneligibilityReasons
                    }),
                findings = new
                {
                    didTheOddsRowsPersistCorrectly = theOddsPlatformProps.Count > 0
                        && theOddsPlatformProps.All(row => row.HasTheOddsDataProvider)
                        && bookmakerCounts.Count > 0,
                    areBookmakerPlatformsModeledCorrectly = bookmakerCounts.Count > 0
                        && bookmakerCounts.All(row => !string.Equals(row.bookmaker, "The Odds API", StringComparison.OrdinalIgnoreCase)),
                    areAnyTheOddsRowsClassifiedAsStandard = theOddsPlatformProps.Any(row => row.MarketClassification == "Standard"),
                    areAnyTheOddsRowsEligibleAsNormalRecommendations = eligibleRows.Count > 0,
                    fanDuelBovadaExplanation = BuildMissingBookmakerExplanation(theOddsPlatformProps, rows),
                    nonEligibleBlockers = CountReasons(rows.SelectMany(row => row.IneligibilityReasons))
                }
            });
        })
        .WithName("TheOddsApiMlbIngestionImpactDebug")
        .WithSummary("Read-only impact report for The Odds API MLB one-event ingestion spike.");

        endpoints.MapPost("/api/debug/the-odds/mlb/recommendations/refresh-current", async (
            DateOnly? date,
            bool? dryRun,
            bool? includeCandidates,
            int? freshnessMinutes,
            IOptions<TheOddsIngestionWorkerOptions> workerOptions,
            ITheOddsApiMlbPlayerPropsAdapter adapter,
            ITheOddsApiMlbEventIngestionService ingestionService,
            IPredictionGenerationService generationService,
            IPredictionProbabilityService probabilityService,
            ApplicationDbContext dbContext,
            IApplicationErrorLogger errorLogger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var isDryRun = dryRun == true;
            var shouldIncludeCandidates = includeCandidates ?? true;
            var options = workerOptions.Value;
            var slateDate = date ?? ResolveCurrentMlbSlateDate();
            var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
            var bookmakers = NullIfWhiteSpace(options.Bookmakers);
            var markets = NullIfWhiteSpace(options.Markets);
            var existingSlateProps = await CountPersistedTheOddsPropsForSlateAsync(dbContext, slateDate, cancellationToken);

            if (isDryRun)
            {
                return Results.Ok(new
                {
                    success = true,
                    dryRun = true,
                    slateDate,
                    predictionRunId = (Guid?)null,
                    oddsEventsFetched = 0,
                    platformPropsCreated = 0,
                    platformPropsUpdated = 0,
                    platformPropsUpserted = 0,
                    predictionCount = 0,
                    lineShoppingSummary = new
                    {
                        totalComparisonGroups = 0,
                        activeGroups = 0,
                        activeCandidates = 0,
                        activeWatchlist = 0,
                        activeAvoid = 0
                    },
                    strategyClassificationSummary = Array.Empty<object>(),
                    activeCandidateCount = 0,
                    activeWatchlistCount = 0,
                    activeAvoidCount = 0,
                    topActiveCandidates = Array.Empty<object>(),
                    topWatchlist = Array.Empty<object>(),
                    diagnostics = new
                    {
                        wouldCallTheOddsApi = false,
                        wouldWriteRows = false,
                        configuredBookmakers = bookmakers,
                        configuredMarkets = markets,
                        existingPersistedTheOddsPropsForSlate = existingSlateProps,
                        estimatedApiCalls = "Live refresh would call The Odds API once for MLB events, then once per MLB event on the slate.",
                        recommendedAction = "Run without dryRun only when you want to spend The Odds API credits and refresh persisted market rows."
                    }
                });
            }

            try
            {
                var beforePlatformPropIds = await LoadTheOddsPlatformPropIdsAsync(dbContext, cancellationToken);
                var eventFetch = await adapter.FetchSpikeAsync(
                    eventId: null,
                    markets: null,
                    bookmakers: null,
                    regions: null,
                    oddsFormat: null,
                    cancellationToken);
                var slateEvents = eventFetch.Events
                    .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                    .Where(row => ResolveMlbSlateDate(row.EventStartTime) == slateDate)
                    .OrderBy(row => row.EventStartTime)
                    .ThenBy(row => row.EventId)
                    .ToArray();
                var ingestionResults = new List<TheOddsApiMlbEventIngestionResult>(slateEvents.Length);

                foreach (var slateEvent in slateEvents)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var ingestionResult = await ingestionService.IngestEventAsync(
                        new TheOddsApiMlbEventIngestionRequest(
                            slateEvent.EventId!,
                            bookmakers,
                            markets),
                        dryRun: false,
                        cancellationToken);
                    ingestionResults.Add(ingestionResult);
                }

                var afterPlatformPropIds = await LoadTheOddsPlatformPropIdsAsync(dbContext, cancellationToken);
                var platformPropsCreated = afterPlatformPropIds.Except(beforePlatformPropIds).Count();
                var platformPropsUpserted = ingestionResults.Sum(row => row.PlatformPropsUpserted);
                var platformPropsUpdated = Math.Max(0, platformPropsUpserted - platformPropsCreated);
                var predictionResult = slateEvents.Length == 0
                    ? null
                    : await generationService.GenerateTheOddsMlbBaselinePredictionsAsync(
                        new TheOddsMlbPredictionGenerationRequest(null, bookmakers, markets),
                        cancellationToken);

                var generatedAtUtc = DateTimeOffset.UtcNow;
                var lineShopping = predictionResult is null
                    ? new TheOddsCurrentRecommendationLineShoppingResult([], [], [], Array.Empty<object>())
                    : await BuildCurrentRecommendationLineShoppingResultAsync(
                        predictionResult.RunId,
                        generatedAtUtc,
                        lifecycleFreshnessMinutes,
                        dbContext,
                        probabilityService,
                        cancellationToken);
                var activeGroups = lineShopping.Groups
                    .Where(IsActiveTheOddsLineShoppingGroup)
                    .ToArray();
                var activeCandidates = activeGroups
                    .Where(row => ClassifyTheOddsLineShoppingStrategy(row).IsCandidate)
                    .OrderByDescending(row => row.BestEstimatedEv ?? decimal.MinValue)
                    .ThenByDescending(row => row.RankingScore)
                    .ToArray();
                var activeWatchlist = activeGroups
                    .Where(row => ClassifyTheOddsLineShoppingStrategy(row).IsWatchlist)
                    .OrderByDescending(row => row.BestEstimatedEv ?? decimal.MinValue)
                    .ThenByDescending(row => row.RankingScore)
                    .ToArray();
                var activeAvoid = activeGroups
                    .Where(row => ClassifyTheOddsLineShoppingStrategy(row).IsAvoid)
                    .ToArray();

                return Results.Ok(new
                {
                    success = true,
                    dryRun = false,
                    slateDate,
                    predictionRunId = predictionResult?.RunId,
                    oddsEventsFetched = slateEvents.Length,
                    platformPropsCreated,
                    platformPropsUpdated,
                    platformPropsUpserted,
                    predictionCount = predictionResult?.PredictionsGenerated ?? 0,
                    lineShoppingSummary = new
                    {
                        totalComparisonGroups = lineShopping.Groups.Count,
                        totalMarketsCompared = lineShopping.Rows.Count,
                        totalBookmakers = lineShopping.Rows.Select(row => row.Bookmaker).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        eligibleComparisonGroups = lineShopping.Groups.Count(row => row.IsEligibleAsNormalRecommendation),
                        activeGroups = activeGroups.Length,
                        staleOrExpiredGroups = lineShopping.Groups.Count(row => !IsActiveTheOddsLineShoppingGroup(row))
                    },
                    strategyClassificationSummary = lineShopping.StrategyClassificationSummary,
                    activeCandidateCount = activeCandidates.Length,
                    activeWatchlistCount = activeWatchlist.Length,
                    activeAvoidCount = activeAvoid.Length,
                    topActiveCandidates = shouldIncludeCandidates
                        ? activeCandidates.Take(25).Select(ToLineShoppingResponseGroup).ToArray()
                        : Array.Empty<object>(),
                    topWatchlist = shouldIncludeCandidates
                        ? activeWatchlist.Take(25).Select(ToLineShoppingResponseGroup).ToArray()
                        : Array.Empty<object>(),
                    diagnostics = new
                    {
                        callsTheOddsApi = true,
                        apiCallsMade = 1 + slateEvents.Length,
                        eventDiscoveryCreditUsage = eventFetch.CreditUsage,
                        eventIngestionCreditUsage = ingestionResults.Select(row => new
                        {
                            row.EventId,
                            row.CreditUsage
                        }),
                        configuredBookmakers = bookmakers,
                        configuredMarkets = markets,
                        existingPersistedTheOddsPropsForSlate = existingSlateProps,
                        events = slateEvents.Select(row => new
                        {
                            row.EventId,
                            row.HomeTeam,
                            row.AwayTeam,
                            row.EventStartTime,
                            slateDate = ResolveMlbSlateDate(row.EventStartTime)
                        }),
                        ingestion = ingestionResults.Select(row => new
                        {
                            row.EventId,
                            row.Success,
                            row.NormalizedRecords,
                            row.PlatformPropsUpserted,
                            row.SnapshotsCreated,
                            row.SnapshotsUnchanged,
                            row.Warnings,
                            row.Errors
                        }),
                        prediction = predictionResult is null ? null : new
                        {
                            predictionResult.RunId,
                            predictionResult.PropsEvaluated,
                            predictionResult.PredictionsGenerated,
                            predictionResult.SkippedProps,
                            predictionResult.RecommendationsSuppressedByThreshold,
                            predictionResult.RecommendationsSuppressedByPropTypeFilter,
                            predictionResult.TimingDiagnostics
                        },
                        freshnessMinutes = lifecycleFreshnessMinutes,
                        warning = activeGroups.Length == 0 && lineShopping.Groups.Count > 0
                            ? "All line-shopping rows are stale, expired, or game-started for the requested freshness window."
                            : null
                    }
                });
            }
            catch (TheOddsApiProviderException exception)
            {
                await LogTheOddsProviderFailureAsync(
                    errorLogger,
                    exception,
                    httpContext,
                    "RefreshCurrentMlbRecommendations",
                    new
                    {
                        slateDate,
                        dryRun = isDryRun,
                        includeCandidates = shouldIncludeCandidates,
                        freshnessMinutes = lifecycleFreshnessMinutes,
                        bookmakers,
                        markets
                    },
                    cancellationToken);

                return Results.Json(
                    BuildTheOddsProviderFailureResponse(exception),
                    statusCode: MapTheOddsProviderFailureStatusCode(exception));
            }
        })
        .WithName("TheOddsApiMlbRefreshCurrentRecommendationsDebug")
        .WithSummary("Debug-only current MLB The Odds API odds refresh, prediction generation, and line-shopping strategy summary.");

        endpoints.MapPost("/api/debug/the-odds/mlb/ingest-event", async (
            TheOddsApiMlbIngestEventRequest request,
            bool? dryRun,
            ITheOddsApiMlbEventIngestionService ingestionService,
            IApplicationErrorLogger errorLogger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await ingestionService.IngestEventAsync(
                    new TheOddsApiMlbEventIngestionRequest(
                        request.EventId,
                        request.Bookmakers,
                        request.Markets),
                    dryRun == true,
                    cancellationToken);

                return result.Success
                    ? Results.Ok(result)
                    : Results.BadRequest(result);
            }
            catch (TheOddsApiProviderException exception)
            {
                await LogTheOddsProviderFailureAsync(
                    errorLogger,
                    exception,
                    httpContext,
                    "IngestEvent",
                    new
                    {
                        request.EventId,
                        request.Bookmakers,
                        request.Markets,
                        dryRun = dryRun == true
                    },
                    cancellationToken);

                return Results.Json(
                    BuildTheOddsProviderFailureResponse(exception),
                    statusCode: MapTheOddsProviderFailureStatusCode(exception));
            }
        })
        .WithName("TheOddsApiMlbIngestEventDebug")
        .WithSummary("Debug-only controlled persistence spike for one The Odds API MLB event.");

        endpoints.MapGet("/api/debug/the-odds/mlb/player-props-spike", async (
            string? eventId,
            string? markets,
            string? bookmakers,
            string? regions,
            string? oddsFormat,
            ITheOddsApiMlbPlayerPropsAdapter adapter,
            IApplicationErrorLogger errorLogger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await adapter.FetchSpikeAsync(
                    eventId,
                    markets,
                    bookmakers,
                    regions,
                    oddsFormat,
                    cancellationToken);

                var records = result.Records;
                var marketKeyBreakdown = records
                    .GroupBy(record => record.MarketKey ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        marketKey = group.Key,
                        normalizedPropTypeCandidate = group
                            .Select(record => record.NormalizedPropTypeCandidate)
                            .FirstOrDefault() ?? "Unknown",
                        count = group.Count()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.marketKey)
                    .ToList();
                var bookmakerBreakdown = records
                    .GroupBy(record => record.BookmakerKey ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        bookmakerKey = group.Key,
                        bookmakerTitle = group
                            .Select(record => record.BookmakerTitle)
                            .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title)),
                        count = group.Count()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.bookmakerKey)
                    .ToList();
                var missingFieldCounts = new
                {
                    missingPlayerName = records.Count(record => string.IsNullOrWhiteSpace(record.PlayerName)),
                    missingLine = records.Count(record => !record.Line.HasValue),
                    missingPrice = records.Count(record => !record.PriceAmerican.HasValue),
                    missingBookmaker = records.Count(record => string.IsNullOrWhiteSpace(record.BookmakerKey)),
                    missingOverUnderSide = records.Count(record => record.Side is not ("Over" or "Under")),
                    missingMarketKey = records.Count(record => string.IsNullOrWhiteSpace(record.MarketKey)),
                    missingEventId = records.Count(record => string.IsNullOrWhiteSpace(record.EventId))
                };
                var selectedEvent = result.Events.FirstOrDefault(row => row.EventId == result.SelectedEventId);

                return Results.Ok(new
                {
                    dataProvider = "The Odds API",
                    mode = string.IsNullOrWhiteSpace(eventId) ? "EventDiscovery" : "EventOdds",
                    requestUrl = result.RequestUrl,
                    creditUsage = result.CreditUsage,
                    events = result.Events.Take(50),
                    selectedEventId = result.SelectedEventId,
                    selectedEvent,
                    summary = new
                    {
                        eventsReturned = result.Events.Count,
                        result.SelectedEventId,
                        bookmakerCount = records
                            .Select(record => record.BookmakerKey)
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count(),
                        marketCount = records
                            .Select(record => record.MarketKey)
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count(),
                        outcomeCount = records.Count,
                        normalizedRecordCount = records.Count,
                        recordsWithPlayerName = records.Count(record => !string.IsNullOrWhiteSpace(record.PlayerName)),
                        recordsWithLine = records.Count(record => record.Line.HasValue),
                        recordsWithPrice = records.Count(record => record.PriceAmerican.HasValue),
                        recordsWithBookmaker = records.Count(record => !string.IsNullOrWhiteSpace(record.BookmakerKey)),
                        recordsWithOverUnderSide = records.Count(record => record.Side is "Over" or "Under")
                    },
                    marketKeyBreakdown,
                    bookmakerBreakdown,
                    missingFieldCounts,
                    sampleRecords = records.Take(100),
                    integrationRecommendation = new
                    {
                        canSupportPersistence = records.Count > 0
                            && records.All(record => !string.IsNullOrWhiteSpace(record.BookmakerKey))
                            && records.All(record => !string.IsNullOrWhiteSpace(record.EventId))
                            && records.All(record => !string.IsNullOrWhiteSpace(record.PlayerName))
                            && records.All(record => record.Line.HasValue),
                        canSupportStandardMarketEligibility = records.Count > 0
                            && records.All(record => !string.IsNullOrWhiteSpace(record.BookmakerKey))
                            && records.All(record => !string.IsNullOrWhiteSpace(record.MarketKey))
                            && records.All(record => record.Side is "Over" or "Under"),
                        canSupportEv = records.Count > 0
                            && records.All(record => record.PriceAmerican.HasValue && record.PriceDecimal.HasValue),
                        recommendedBookmakerModel = "Treat The Odds API as the data provider and each bookmaker key/title as the platform to persist later.",
                        warnings = new[]
                        {
                            "Event discovery does not fetch player props unless eventId is supplied to avoid unexpected API credit usage.",
                            "Player props are fetched event-by-event; broad polling can consume credits quickly.",
                            "Historical line support depends on polling cadence or a paid historical odds product.",
                            "Bookmaker-specific market naming should be canonicalized before persistence."
                        }
                    }
                });
            }
            catch (TheOddsApiProviderException exception)
            {
                await LogTheOddsProviderFailureAsync(
                    errorLogger,
                    exception,
                    httpContext,
                    "PlayerPropsSpike",
                    new
                    {
                        eventId,
                        markets,
                        bookmakers,
                        regions,
                        oddsFormat
                    },
                    cancellationToken);

                return Results.Json(
                    BuildTheOddsProviderFailureResponse(exception),
                    statusCode: MapTheOddsProviderFailureStatusCode(exception));
            }
        })
        .WithName("TheOddsApiMlbPlayerPropsSpike")
        .WithSummary("Read-only spike for The Odds API MLB player props event discovery and event odds normalization.");

        return endpoints;
    }

    private static async Task LogTheOddsProviderFailureAsync(
        IApplicationErrorLogger errorLogger,
        TheOddsApiProviderException exception,
        HttpContext httpContext,
        string operation,
        object requestMetadata,
        CancellationToken cancellationToken)
    {
        await errorLogger.LogAsync(
            new ApplicationErrorLogRequest(
                Severity: "Warning",
                Source: "ExternalProvider",
                Component: "TheOddsApi",
                Operation: operation,
                Exception: exception,
                RequestPath: httpContext.Request.Path,
                HttpMethod: httpContext.Request.Method,
                StatusCode: exception.StatusCodeNumber,
                CorrelationId: httpContext.TraceIdentifier,
                Metadata: new
                {
                    provider = "The Odds API",
                    exception.ErrorCode,
                    providerStatusCode = exception.StatusCodeNumber,
                    exception.ProviderBodyPreview,
                    exception.RequestUrl,
                    exception.RecommendedAction,
                    exception.CreditUsage,
                    request = requestMetadata
                }),
            cancellationToken);
    }

    private static object BuildTheOddsProviderFailureResponse(TheOddsApiProviderException exception)
    {
        return new
        {
            success = false,
            source = "The Odds API",
            statusCode = exception.StatusCodeNumber,
            errorCode = exception.ErrorCode,
            message = exception.Message,
            providerBodyPreview = exception.ProviderBodyPreview,
            creditUsage = exception.CreditUsage,
            recommendedAction = exception.RecommendedAction
        };
    }

    private static int MapTheOddsProviderFailureStatusCode(TheOddsApiProviderException exception)
    {
        return exception.ErrorCode switch
        {
            "MISSING_API_KEY" => StatusCodes.Status400BadRequest,
            "INVALID_KEY" => StatusCodes.Status400BadRequest,
            "QUOTA_OR_PAYMENT_REQUIRED" => StatusCodes.Status429TooManyRequests,
            "RATE_LIMITED" => StatusCodes.Status429TooManyRequests,
            "PROVIDER_TIMEOUT" => StatusCodes.Status503ServiceUnavailable,
            "PROVIDER_ERROR" => StatusCodes.Status502BadGateway,
            "MALFORMED_PROVIDER_RESPONSE" => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status502BadGateway
        };
    }

    private static DateOnly ResolveCurrentMlbSlateDate()
    {
        return DateOnly.FromDateTime(ConvertUtcToMlbLocalTime(DateTimeOffset.UtcNow).Date);
    }

    private static DateOnly? ResolveMlbSlateDate(DateTimeOffset? eventStartTime)
    {
        return eventStartTime.HasValue
            ? DateOnly.FromDateTime(ConvertUtcToMlbLocalTime(eventStartTime.Value).Date)
            : null;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsActiveTheOddsLineShoppingGroup(TheOddsLineShoppingGroup group)
    {
        return string.Equals(group.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase)
            && !group.IsStale
            && !group.IsExpired;
    }

    private static async Task<HashSet<Guid>> LoadTheOddsPlatformPropIdsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        using var filter = JsonSerializer.SerializeToDocument(new { dataProvider = "The Odds API" });

        var ids = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Metadata != null
                && EF.Functions.JsonContains(platformProp.Metadata, filter))
            .Select(platformProp => platformProp.Id)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    private static async Task<int> CountPersistedTheOddsPropsForSlateAsync(
        ApplicationDbContext dbContext,
        DateOnly slateDate,
        CancellationToken cancellationToken)
    {
        var rows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
        return ParseTheOddsPlatformProps(rows)
            .Count(row => ResolveMlbSlateDate(row.EventStartTime) == slateDate);
    }

    private static async Task<TheOddsCurrentRecommendationLineShoppingResult> BuildCurrentRecommendationLineShoppingResultAsync(
        Guid runId,
        DateTimeOffset generatedAtUtc,
        int freshnessMinutes,
        ApplicationDbContext dbContext,
        IPredictionProbabilityService probabilityService,
        CancellationToken cancellationToken)
    {
        var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
        var platformProps = ParseTheOddsPlatformProps(platformPropDataRows);
        var theOddsPropIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
        var probabilityAnalysis = await probabilityService.AnalyzeRunForPropsAsync(
            runId,
            theOddsPropIds,
            cancellationToken);

        if (probabilityAnalysis is null)
        {
            return new TheOddsCurrentRecommendationLineShoppingResult(
                new List<TheOddsLineShoppingSourceRow>(),
                new List<TheOddsLineShoppingGroup>(),
                new List<object>(),
                Array.Empty<object>());
        }

        var freshnessCutoff = generatedAtUtc.AddHours(-FreshnessWindowHours);
        var rows = BuildTheOddsLineShoppingRows(platformProps, probabilityAnalysis, freshnessCutoff);
        var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, freshnessMinutes);
        var responseGroups = groups.Select(ToLineShoppingResponseGroup).ToList();
        var strategySummary = BuildTheOddsStrategyClassificationSummary(groups);

        return new TheOddsCurrentRecommendationLineShoppingResult(
            rows,
            groups,
            responseGroups,
            strategySummary);
    }

    private static async Task<List<TheOddsPersistedPlatformPropRow>> LoadTheOddsPlatformPropsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
        return ParseTheOddsPlatformProps(rows);
    }

    private static async Task<List<TheOddsPlatformPropDataRow>> LoadTheOddsPlatformPropDataRowsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        using var filter = JsonSerializer.SerializeToDocument(new { dataProvider = "The Odds API" });

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

    private static List<TheOddsPersistedPlatformPropRow> ParseTheOddsPlatformProps(
        IReadOnlyCollection<TheOddsPlatformPropDataRow> rows)
    {
        return rows
            .Select(row => new TheOddsPersistedPlatformPropRow(
                row.Id,
                row.PropId,
                row.PlatformPropId,
                row.Label,
                row.CurrentLine,
                row.OverAvailable,
                row.UnderAvailable,
                row.PayoutType,
                row.Status,
                row.ObservedAt,
                row.RawPayloadId,
                row.PlatformName,
                row.PlatformSlug,
                ExtractMetadataString(row.Metadata, "bookmaker", "key"),
                row.PlayerId,
                row.PropTypeId,
                row.PlayerName,
                row.PropType,
                ExtractMarketClassification(row.Metadata),
                ExtractEventId(row.Metadata),
                ExtractEventStartTime(row.Metadata),
                ExtractEventString(row.Metadata, "homeTeam"),
                ExtractEventString(row.Metadata, "awayTeam"),
                ExtractMetadataString(row.Metadata, "market", "key"),
                ExtractAmericanPrice(row.Metadata, "higher"),
                ExtractAmericanPrice(row.Metadata, "lower"),
                ExtractDecimalPrice(row.Metadata, "higher"),
                ExtractDecimalPrice(row.Metadata, "lower"),
                ResolveMlbResultDateForBacktest(row.Metadata, ExtractEventStartTime(row.Metadata), row.ObservedAt),
                HasDataProvider(row.Metadata)))
            .ToList();
    }

    private static TheOddsLineShoppingSourceRow BuildTheOddsLineShoppingSourceRow(
        TheOddsPersistedPlatformPropRow platformProp,
        PredictionProbabilityResult prediction,
        DateTimeOffset freshnessCutoff)
    {
        var impactRow = BuildTheOddsImpactPredictionRow(platformProp, prediction, DateTimeOffset.UtcNow, freshnessCutoff);
        var overImpliedProbability = CalculateImpliedProbability(platformProp.PriceOverDecimal);
        var underImpliedProbability = CalculateImpliedProbability(platformProp.PriceUnderDecimal);
        var overProbabilityEdge = prediction.ProbabilityOver.HasValue && overImpliedProbability.HasValue
            ? Math.Round(prediction.ProbabilityOver.Value - overImpliedProbability.Value, 6)
            : (decimal?)null;
        var underProbabilityEdge = prediction.ProbabilityUnder.HasValue && underImpliedProbability.HasValue
            ? Math.Round(prediction.ProbabilityUnder.Value - underImpliedProbability.Value, 6)
            : (decimal?)null;
        var overEstimatedEv = CalculateEstimatedEv(prediction.ProbabilityOver, prediction.ProbabilityUnder, platformProp.PriceOverDecimal);
        var underEstimatedEv = CalculateEstimatedEv(prediction.ProbabilityUnder, prediction.ProbabilityOver, platformProp.PriceUnderDecimal);

        return new TheOddsLineShoppingSourceRow(
            platformProp.EventId,
            platformProp.EventStartTime,
            platformProp.HomeTeam,
            platformProp.AwayTeam,
            platformProp.PlayerName,
            platformProp.PropType,
            platformProp.CurrentLine,
            platformProp.MarketClassification,
            impactRow.IsEligibleAsNormalRecommendation,
            prediction.ProbabilityOver,
            prediction.ProbabilityUnder,
            prediction.ProbabilityPush,
            prediction.RankingScore,
            platformProp.PlatformName,
            platformProp.PlatformPropId,
            platformProp.ObservedAt,
            platformProp.OverAvailable,
            platformProp.UnderAvailable,
            platformProp.PriceOver,
            platformProp.PriceOverDecimal,
            platformProp.PriceUnder,
            platformProp.PriceUnderDecimal,
            overImpliedProbability,
            underImpliedProbability,
            overProbabilityEdge,
            underProbabilityEdge,
            overEstimatedEv,
            underEstimatedEv,
            prediction.PlayerId,
            prediction.PropTypeId,
            platformProp.BacktestResultDate.ExpectedResultDate,
            platformProp.BacktestResultDate.EventStartTimeUtc,
            platformProp.BacktestResultDate.ExpectedResultDate,
            platformProp.BacktestResultDate.ResultDateSource,
            platformProp.BacktestResultDate.UtcDate,
            platformProp.BacktestResultDate.ResolvedMlbDate,
            prediction.SampleSize,
            prediction.MethodUsed,
            prediction.ConfidenceScore);
    }

    private static List<TheOddsLineShoppingSourceRow> BuildTheOddsLineShoppingRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        PredictionProbabilityRunAnalysis probabilityAnalysis,
        DateTimeOffset freshnessCutoff)
    {
        var predictionByPropId = probabilityAnalysis.Results
            .GroupBy(result => result.PropId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(result => result.RankingScore).First());

        return platformProps
            .Where(row => predictionByPropId.ContainsKey(row.PropId))
            .Select(row => BuildTheOddsLineShoppingSourceRow(
                row,
                predictionByPropId[row.PropId],
                freshnessCutoff))
            .ToList();
    }

    private static List<TheOddsLineShoppingGroup> BuildTheOddsLineShoppingGroups(
        IReadOnlyCollection<TheOddsLineShoppingSourceRow> rows,
        DateTimeOffset? generatedAtUtc = null,
        int freshnessMinutes = DefaultTheOddsLineShoppingFreshnessMinutes)
    {
        var now = generatedAtUtc ?? DateTimeOffset.UtcNow;

        return rows
            .GroupBy(row => new TheOddsLineShoppingGroupKey(
                row.EventId,
                row.EventStartTime,
                row.HomeTeam,
                row.AwayTeam,
                row.Player,
                row.PropType,
                row.Line,
                row.MarketClassification))
            .Select(group => BuildLineShoppingGroup(group.Key, group.ToList(), now, freshnessMinutes))
            .OrderByDescending(group => group.BestEstimatedEv ?? decimal.MinValue)
            .ToList();
    }

    private static TheOddsLineShoppingGroup BuildLineShoppingGroup(
        TheOddsLineShoppingGroupKey key,
        IReadOnlyList<TheOddsLineShoppingSourceRow> rows,
        DateTimeOffset generatedAtUtc,
        int freshnessMinutes)
    {
        var bestOver = rows
            .Where(row => row.OverEstimatedEv.HasValue)
            .OrderByDescending(row => row.OverEstimatedEv)
            .ThenByDescending(row => row.OverDecimalPrice)
            .FirstOrDefault();
        var bestUnder = rows
            .Where(row => row.UnderEstimatedEv.HasValue)
            .OrderByDescending(row => row.UnderEstimatedEv)
            .ThenByDescending(row => row.UnderDecimalPrice)
            .FirstOrDefault();
        var spreads = new[]
        {
            CalculatePriceSpread(rows.Select(row => row.OverDecimalPrice)),
            CalculatePriceSpread(rows.Select(row => row.UnderDecimalPrice))
        };
        var bestEstimatedEv = new[] { bestOver?.OverEstimatedEv, bestUnder?.UnderEstimatedEv }
            .Where(value => value.HasValue)
            .DefaultIfEmpty()
            .Max();
        var first = rows.OrderByDescending(row => row.RankingScore).First();
        var bestSide = SelectBestSide(bestOver, bestUnder);
        var sampleWarning = BuildSampleWarning(first.HistoricalSampleSize, first.ProbabilityMethod);
        var trustLevel = BuildTrustLevel(
            rows.Any(row => row.IsEligibleAsNormalRecommendation),
            bestSide.EstimatedEv,
            first.HistoricalSampleSize,
            first.ConfidenceScore,
            sampleWarning);
        var lastObservedAt = rows.Max(row => row.ObservedAt);
        var lifecycle = BuildTheOddsLineShoppingLifecycle(
            key.EventStartTime,
            lastObservedAt,
            generatedAtUtc,
            freshnessMinutes);

        return new TheOddsLineShoppingGroup(
            key.EventId,
            key.EventStartTime,
            lastObservedAt,
            lifecycle.MinutesSinceObserved,
            lifecycle.MinutesUntilStart,
            lifecycle.IsFresh,
            lifecycle.IsStale,
            lifecycle.IsExpired,
            lifecycle.Status,
            lifecycle.Reasons,
            key.HomeTeam,
            key.AwayTeam,
            key.Player,
            key.NormalizedPropType,
            key.Line,
            key.MarketClassification,
            first.PlayerId,
            first.PropTypeId,
            first.EventDate,
            first.EventStartTimeUtc,
            first.ExpectedResultDate,
            first.ResultDateSource,
            first.UtcDate,
            first.ResolvedMlbDate,
            rows.Any(row => row.IsEligibleAsNormalRecommendation),
            first.ModelProbabilityOver,
            first.ModelProbabilityUnder,
            first.ModelProbabilityPush,
            first.RankingScore,
            first.HistoricalSampleSize,
            first.ProbabilityMethod,
            first.ConfidenceScore,
            sampleWarning,
            trustLevel,
            rows
                .OrderBy(row => row.Bookmaker)
                .Select(row => new TheOddsBookmakerPriceRow(
                    row.Bookmaker,
                    row.PlatformPropId,
                    row.ObservedAt,
                    row.OverAvailable,
                    row.UnderAvailable,
                    row.OverAmericanPrice,
                    row.OverDecimalPrice,
                    row.UnderAmericanPrice,
                    row.UnderDecimalPrice,
                    row.OverImpliedProbability,
                    row.UnderImpliedProbability,
                    row.OverProbabilityEdge,
                    row.UnderProbabilityEdge,
                    row.OverEstimatedEv,
                    row.UnderEstimatedEv))
                .ToList(),
            bestOver?.Bookmaker,
            bestOver?.OverAmericanPrice,
            bestOver?.OverEstimatedEv,
            bestUnder?.Bookmaker,
            bestUnder?.UnderAmericanPrice,
            bestUnder?.UnderEstimatedEv,
            bestEstimatedEv,
            spreads.Where(value => value.HasValue).DefaultIfEmpty().Max(),
            bestSide.Side,
            bestSide.Bookmaker,
            bestSide.AmericanPrice,
            bestSide.DecimalPrice,
            bestSide.Probability,
            bestSide.ImpliedProbability,
            bestSide.ProbabilityEdge);
    }

    private static object ToLineShoppingResponseGroup(TheOddsLineShoppingGroup group)
    {
        var strategy = ClassifyTheOddsLineShoppingStrategy(group);

        return new
        {
            group.EventId,
            eventStartTime = group.EventStartTime,
            group.EventStartTimeUtc,
            group.ExpectedResultDate,
            group.ResultDateSource,
            group.UtcDate,
            group.ResolvedMlbDate,
            group.LastObservedAt,
            group.MinutesSinceObserved,
            group.MinutesUntilStart,
            group.IsFresh,
            group.IsStale,
            group.IsExpired,
            group.LifecycleStatus,
            group.LifecycleReasons,
            group.HomeTeam,
            group.AwayTeam,
            player = group.Player,
            normalizedPropType = group.NormalizedPropType,
            line = group.Line,
            group.MarketClassification,
            group.IsEligibleAsNormalRecommendation,
            modelProbabilityOver = group.ModelProbabilityOver,
            modelProbabilityUnder = group.ModelProbabilityUnder,
            modelProbabilityPush = group.ModelProbabilityPush,
            group.RankingScore,
            group.HistoricalSampleSize,
            group.ProbabilityMethod,
            group.ConfidenceScore,
            group.SampleWarning,
            group.TrustLevel,
            bookmakerPrices = group.BookmakerPrices,
            group.BestOverBookmaker,
            group.BestOverAmericanPrice,
            group.BestOverEstimatedEv,
            group.BestUnderBookmaker,
            group.BestUnderAmericanPrice,
            group.BestUnderEstimatedEv,
            group.BestEstimatedEv,
            group.LargestPriceSpread,
            group.BestSide,
            group.BestBookmaker,
            group.BestAmericanPrice,
            group.BestDecimalPrice,
            group.BestProbability,
            group.BestImpliedProbability,
            group.BestProbabilityEdge,
            strategy.StrategyClassification,
            strategy.StrategyTier,
            strategy.StrategyReasons,
            strategy.StrategyWarnings,
            strategy.IsCandidate,
            strategy.IsAvoid,
            strategy.IsWatchlist,
            strategy.IsResearchOnly,
            strategy.CandidateStrategyName,
            strategy.MarketShape,
            strategy.RiskTags
        };
    }

    private static TheOddsLineShoppingStrategyClassification ClassifyTheOddsLineShoppingStrategy(
        TheOddsLineShoppingGroup group)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();
        var riskTags = new List<string>();
        var hasBestPrice = group.BestDecimalPrice.HasValue;
        var hasBestSide = !string.Equals(group.BestSide, "None", StringComparison.OrdinalIgnoreCase);
        var isPitcherStrikeouts = string.Equals(group.NormalizedPropType, "Pitcher Strikeouts", StringComparison.OrdinalIgnoreCase);
        var isTotalBases = string.Equals(group.NormalizedPropType, "Total Bases", StringComparison.OrdinalIgnoreCase);
        var isBatterProp = !isPitcherStrikeouts;
        var isResearchOnly = string.Equals(group.TrustLevel, "ResearchOnly", StringComparison.OrdinalIgnoreCase)
            || group.SampleWarning is "InsufficientHistoricalSample" or "MissingHistoricalDistribution";
        var isActionableLifecycle = !group.IsStale && !group.IsExpired && string.Equals(group.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase);
        var marketShape = BuildTheOddsLineShoppingMarketShape(group, isBatterProp, isPitcherStrikeouts);

        if (!hasBestPrice)
        {
            warnings.Add("Best price is missing.");
            riskTags.Add("MissingBestPrice");
        }

        if (!hasBestSide)
        {
            warnings.Add("Best side is unavailable.");
            riskTags.Add("MissingBestSide");
        }

        if (group.IsStale)
        {
            warnings.Add("Line is stale.");
            riskTags.Add("StaleLine");
        }

        if (group.IsExpired)
        {
            warnings.Add("Event has started or expired.");
            riskTags.Add("ExpiredMarket");
        }

        if (isResearchOnly)
        {
            riskTags.Add("ResearchOnlySample");
        }

        var isPlusMoneyBatterLinePointFive = isBatterProp && group.Line == 0.5m && group.BestDecimalPrice >= 2.00m;
        var isStrongHighEv = string.Equals(group.TrustLevel, "Strong", StringComparison.OrdinalIgnoreCase)
            && group.BestEstimatedEv >= 0.20m;
        if (isPlusMoneyBatterLinePointFive || isStrongHighEv)
        {
            if (isPlusMoneyBatterLinePointFive)
            {
                reasons.Add("Matches the Experimental Plus-Money Fade Diagnostic: batter prop line 0.5 with best price >= 2.00.");
                warnings.Add("First slate backtest showed this market shape performed poorly.");
                riskTags.Add("PlusMoneyBatterLine0.5");
            }

            if (isStrongHighEv)
            {
                reasons.Add("Strong trust with EV >= 0.20 is temporarily marked Avoid until more validation exists.");
                warnings.Add("High-EV Strong rows underperformed in the first full slate backtest.");
                riskTags.Add("StrongHighEvNeedsValidation");
            }

            return new TheOddsLineShoppingStrategyClassification(
                "Avoid",
                "DiagnosticAvoid",
                reasons,
                warnings,
                false,
                true,
                false,
                false,
                null,
                marketShape,
                riskTags);
        }

        if (isResearchOnly)
        {
            reasons.Add("Trust level or sample warning indicates research-only.");
            return new TheOddsLineShoppingStrategyClassification(
                "ResearchOnly",
                "ResearchOnly",
                reasons,
                warnings,
                false,
                false,
                false,
                true,
                null,
                marketShape,
                riskTags);
        }

        var qualifiesConservative = group.IsEligibleAsNormalRecommendation
            && (isPitcherStrikeouts || isTotalBases)
            && !(isBatterProp && group.Line == 0.5m)
            && !(isBatterProp && group.BestDecimalPrice >= 2.00m)
            && hasBestPrice
            && hasBestSide
            && isActionableLifecycle;
        if (qualifiesConservative)
        {
            reasons.Add("Matches Conservative Candidate Strategy.");
            return new TheOddsLineShoppingStrategyClassification(
                "Candidate",
                "ConservativeCandidate",
                reasons,
                warnings,
                true,
                false,
                false,
                false,
                "Conservative Candidate Strategy",
                marketShape,
                riskTags);
        }

        var qualifiesPitcherStrikeout = isPitcherStrikeouts
            && group.BestEstimatedEv > 0m
            && hasBestPrice
            && hasBestSide
            && isActionableLifecycle;
        if (qualifiesPitcherStrikeout)
        {
            reasons.Add("Pitcher Strikeouts with positive estimated EV.");
            return new TheOddsLineShoppingStrategyClassification(
                "Candidate",
                "PitcherStrikeoutCandidate",
                reasons,
                warnings,
                true,
                false,
                false,
                false,
                "Pitcher Strikeouts Only",
                marketShape,
                riskTags);
        }

        var qualifiesWatchlist = (string.Equals(group.TrustLevel, "Medium", StringComparison.OrdinalIgnoreCase) && isTotalBases)
            || group.Line == 1.5m
            || (group.BestDecimalPrice >= 1.75m && group.BestDecimalPrice < 2.00m);
        if (qualifiesWatchlist)
        {
            reasons.Add("Matches an early-positive strategy slice but does not fully qualify as a candidate.");
            return new TheOddsLineShoppingStrategyClassification(
                "Watchlist",
                "Watchlist",
                reasons,
                warnings,
                false,
                false,
                true,
                false,
                null,
                marketShape,
                riskTags);
        }

        reasons.Add("No current strategy rule promoted or rejected this market.");
        return new TheOddsLineShoppingStrategyClassification(
            "Research",
            "Research",
            reasons,
            warnings,
            false,
            false,
            false,
            false,
            null,
            marketShape,
            riskTags);
    }

    private static string BuildTheOddsLineShoppingMarketShape(
        TheOddsLineShoppingGroup group,
        bool isBatterProp,
        bool isPitcherStrikeouts)
    {
        if (isPitcherStrikeouts)
        {
            return "PitcherStrikeouts";
        }

        if (isBatterProp && group.Line == 0.5m && group.BestDecimalPrice >= 2.00m)
        {
            return "BatterLine0.5PlusMoney";
        }

        if (isBatterProp && group.Line == 0.5m)
        {
            return "BatterLine0.5";
        }

        if (isBatterProp && group.Line == 1.5m)
        {
            return "BatterLine1.5";
        }

        return "Standard";
    }

    private static object[] BuildTheOddsStrategyClassificationSummary(
        IReadOnlyCollection<TheOddsLineShoppingGroup> groups)
    {
        return groups
            .Select(group => new
            {
                Group = group,
                Strategy = ClassifyTheOddsLineShoppingStrategy(group)
            })
            .GroupBy(row => new { row.Strategy.StrategyClassification, row.Strategy.StrategyTier })
            .Select(group => new
            {
                classification = group.Key.StrategyClassification,
                tier = group.Key.StrategyTier,
                count = group.Count(),
                activeCount = group.Count(row => string.Equals(row.Group.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase)),
                staleCount = group.Count(row => row.Group.IsStale),
                averageBestEstimatedEv = AverageNullable(group.Select(row => row.Group.BestEstimatedEv)),
                averageBestProbability = AverageNullable(group.Select(row => row.Group.BestProbability)),
                averageBestPrice = AverageNullable(group.Select(row => row.Group.BestDecimalPrice))
            })
            .OrderBy(row => row.classification)
            .ThenBy(row => row.tier)
            .Cast<object>()
            .ToArray();
    }

    private static TheOddsLineShoppingLifecycle BuildTheOddsLineShoppingLifecycle(
        DateTimeOffset? eventStartTime,
        DateTimeOffset lastObservedAt,
        DateTimeOffset generatedAtUtc,
        int freshnessMinutes)
    {
        var minutesSinceObserved = Math.Round((generatedAtUtc - lastObservedAt).TotalMinutes, 1);
        var minutesUntilStart = eventStartTime.HasValue
            ? Math.Round((eventStartTime.Value - generatedAtUtc).TotalMinutes, 1)
            : (double?)null;
        var isStale = minutesSinceObserved > freshnessMinutes;
        var isFresh = !isStale;
        var isExpired = eventStartTime.HasValue && eventStartTime.Value <= generatedAtUtc;
        var reasons = new List<string>();

        if (isStale)
        {
            reasons.Add($"Line was last observed {minutesSinceObserved:0.0} minutes ago, exceeding the {freshnessMinutes} minute freshness threshold.");
        }

        if (!eventStartTime.HasValue)
        {
            reasons.Add("Event start time is missing from persisted metadata.");
        }
        else if (isExpired)
        {
            reasons.Add("Event has started, so this market is no longer actionable as a live recommendation.");
        }
        else
        {
            reasons.Add($"Event starts in {minutesUntilStart:0.0} minutes.");
        }

        var status = isExpired
            ? "AwaitingResults"
            : isStale
                ? "Stale"
                : "Active";

        return new TheOddsLineShoppingLifecycle(
            minutesSinceObserved,
            minutesUntilStart,
            isFresh,
            isStale,
            isExpired,
            status,
            reasons);
    }

    private static BestSideSelection SelectBestSide(
        TheOddsLineShoppingSourceRow? bestOver,
        TheOddsLineShoppingSourceRow? bestUnder)
    {
        if (bestOver?.OverEstimatedEv is null && bestUnder?.UnderEstimatedEv is null)
        {
            return new BestSideSelection("None", null, null, null, null, null, null, null);
        }

        if (bestOver?.OverEstimatedEv is not null
            && (bestUnder?.UnderEstimatedEv is null || bestOver.OverEstimatedEv >= bestUnder.UnderEstimatedEv))
        {
            return new BestSideSelection(
                "Over",
                bestOver.Bookmaker,
                bestOver.OverAmericanPrice,
                bestOver.OverDecimalPrice,
                bestOver.OverEstimatedEv,
                bestOver.ModelProbabilityOver,
                bestOver.OverImpliedProbability,
                bestOver.OverProbabilityEdge);
        }

        return new BestSideSelection(
            "Under",
            bestUnder!.Bookmaker,
            bestUnder.UnderAmericanPrice,
            bestUnder.UnderDecimalPrice,
            bestUnder.UnderEstimatedEv,
            bestUnder.ModelProbabilityUnder,
            bestUnder.UnderImpliedProbability,
            bestUnder.UnderProbabilityEdge);
    }

    private static string BuildSampleWarning(int sampleSize, string probabilityMethod)
    {
        if (sampleSize <= 0)
        {
            return "MissingHistoricalDistribution";
        }

        if (sampleSize < 25)
        {
            return "InsufficientHistoricalSample";
        }

        if (sampleSize < 50 || string.Equals(probabilityMethod, "PropTypePriorEmpiricalDistribution", StringComparison.OrdinalIgnoreCase))
        {
            return "LowSample";
        }

        return "None";
    }

    private static string BuildTrustLevel(
        bool isEligible,
        decimal? bestEstimatedEv,
        int historicalSampleSize,
        decimal confidenceScore,
        string sampleWarning)
    {
        if (!isEligible || historicalSampleSize < 25 || sampleWarning is "MissingHistoricalDistribution" or "InsufficientHistoricalSample")
        {
            return "ResearchOnly";
        }

        if (!bestEstimatedEv.HasValue || bestEstimatedEv.Value <= 0m || confidenceScore < 0.45m)
        {
            return "Weak";
        }

        if (bestEstimatedEv.Value >= 0.10m && confidenceScore >= 0.75m && historicalSampleSize >= 100)
        {
            return "Strong";
        }

        return "Medium";
    }

    private static TheOddsImpactPredictionRow BuildTheOddsImpactPredictionRow(
        TheOddsPersistedPlatformPropRow platformProp,
        PredictionProbabilityResult prediction,
        DateTimeOffset generatedAtUtc,
        DateTimeOffset freshnessCutoff)
    {
        var hiddenReasons = new List<string>();
        var ineligibilityReasons = new List<string>();

        if (platformProp.ObservedAt < freshnessCutoff)
        {
            hiddenReasons.Add("StaleMarket");
            ineligibilityReasons.Add("StaleMarket");
        }

        if (!platformProp.OverAvailable)
        {
            hiddenReasons.Add("OverUnavailable");
        }

        if (!platformProp.UnderAvailable)
        {
            hiddenReasons.Add("UnderUnavailable");
        }

        if (platformProp.CurrentLine == 0m)
        {
            hiddenReasons.Add("MissingLine");
            ineligibilityReasons.Add("MissingLine");
        }

        if (prediction.ProbabilityOver is null || prediction.ProbabilityUnder is null || prediction.ProbabilityPush is null)
        {
            ineligibilityReasons.Add("MissingProbabilityData");
        }

        if (prediction.SampleSize < 25)
        {
            ineligibilityReasons.Add("InsufficientHistoricalSample");
        }

        if (!string.Equals(platformProp.MarketClassification, "Standard", StringComparison.OrdinalIgnoreCase))
        {
            ineligibilityReasons.Add("NonStandardMarket");
        }

        if (platformProp.PriceOver is null && platformProp.PriceUnder is null)
        {
            ineligibilityReasons.Add("MissingPayoutContext");
        }

        var isVisible = hiddenReasons.Count == 0;
        var isEligible = isVisible && ineligibilityReasons.Count == 0;

        return new TheOddsImpactPredictionRow(
            platformProp.PlayerName,
            platformProp.PlatformName,
            platformProp.PlatformPropId,
            platformProp.PropType,
            platformProp.CurrentLine,
            platformProp.PriceOver,
            platformProp.PriceUnder,
            prediction.ProbabilityOver,
            prediction.ProbabilityUnder,
            prediction.RankingScore,
            platformProp.MarketClassification,
            isVisible,
            isEligible,
            hiddenReasons.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            ineligibilityReasons.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
    }

    private static string ExtractMarketClassification(JsonDocument? metadata)
    {
        return metadata?.RootElement.ValueKind == JsonValueKind.Object
            && metadata.RootElement.TryGetProperty("marketClassification", out var classification)
            && classification.ValueKind == JsonValueKind.String
            ? classification.GetString() ?? "Unknown"
            : "Unknown";
    }

    private static int? ExtractAmericanPrice(JsonDocument? metadata, string side)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty("options", out var options)
            || options.ValueKind != JsonValueKind.Object
            || !options.TryGetProperty(side, out var sideElement)
            || sideElement.ValueKind != JsonValueKind.Object
            || !sideElement.TryGetProperty("americanPrice", out var price))
        {
            return null;
        }

        return price.ValueKind switch
        {
            JsonValueKind.Number when price.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(price.GetString(), out var value) => value,
            _ => null
        };
    }

    private static decimal? ExtractDecimalPrice(JsonDocument? metadata, string side)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty("options", out var options)
            || options.ValueKind != JsonValueKind.Object
            || !options.TryGetProperty(side, out var sideElement)
            || sideElement.ValueKind != JsonValueKind.Object
            || !sideElement.TryGetProperty("decimalPrice", out var price))
        {
            return null;
        }

        return price.ValueKind switch
        {
            JsonValueKind.Number when price.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(price.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string? ExtractEventId(JsonDocument? metadata)
    {
        return ExtractEventString(metadata, "eventId");
    }

    private static string? ExtractEventString(JsonDocument? metadata, string propertyName)
    {
        return ExtractMetadataString(metadata, "eventInfo", propertyName);
    }

    private static string? ExtractMetadataString(JsonDocument? metadata, string sectionName, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty(sectionName, out var section)
            || section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static DateTimeOffset? ExtractEventStartTime(JsonDocument? metadata)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty("eventInfo", out var eventInfo)
            || eventInfo.ValueKind != JsonValueKind.Object
            || !eventInfo.TryGetProperty("eventStartTime", out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static TheOddsMlbBacktestResultDate ResolveMlbResultDateForBacktest(
        JsonDocument? metadata,
        DateTimeOffset? eventStartTime,
        DateTimeOffset observedAt)
    {
        var eventStartTimeUtc = eventStartTime?.ToUniversalTime();
        var utcDate = eventStartTimeUtc.HasValue
            ? DateOnly.FromDateTime(eventStartTimeUtc.Value.UtcDateTime)
            : (DateOnly?)null;

        if (TryExtractMlbProviderEventDate(metadata, out var providerDate, out var providerDateSource))
        {
            return new TheOddsMlbBacktestResultDate(
                eventStartTimeUtc,
                providerDate,
                providerDateSource,
                utcDate,
                providerDate);
        }

        if (eventStartTimeUtc.HasValue)
        {
            var mlbDate = DateOnly.FromDateTime(ConvertUtcToMlbLocalTime(eventStartTimeUtc.Value).Date);
            return new TheOddsMlbBacktestResultDate(
                eventStartTimeUtc,
                mlbDate,
                "MlbEasternDateFromEventStartTimeUtc",
                utcDate,
                mlbDate);
        }

        var observedUtc = observedAt.ToUniversalTime();
        var observedMlbDate = DateOnly.FromDateTime(ConvertUtcToMlbLocalTime(observedUtc).Date);
        return new TheOddsMlbBacktestResultDate(
            null,
            observedMlbDate,
            "MlbEasternDateFromObservedAtUtc",
            DateOnly.FromDateTime(observedUtc.UtcDateTime),
            observedMlbDate);
    }

    private static bool TryExtractMlbProviderEventDate(
        JsonDocument? metadata,
        out DateOnly eventDate,
        out string source)
    {
        eventDate = default;
        source = string.Empty;

        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty("eventInfo", out var eventInfo)
            || eventInfo.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var dateFieldNames = new[]
        {
            "eventDate",
            "providerEventDate",
            "sportEventDate",
            "resultDate",
            "scheduleDate",
            "gameDate",
            "localDate"
        };

        foreach (var fieldName in dateFieldNames)
        {
            if (!eventInfo.TryGetProperty(fieldName, out var property))
            {
                continue;
            }

            if (TryReadDateOnly(property, out eventDate))
            {
                source = $"Metadata:eventInfo.{fieldName}";
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDateOnly(JsonElement property, out DateOnly eventDate)
    {
        eventDate = default;

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateOnly.TryParse(value, out eventDate))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(value, out var parsedDateTime))
        {
            eventDate = DateOnly.FromDateTime(parsedDateTime.Date);
            return true;
        }

        return false;
    }

    private static DateTime ConvertUtcToMlbLocalTime(DateTimeOffset utcDateTime)
    {
        var timeZone = GetMlbScheduleTimeZone();
        return TimeZoneInfo.ConvertTime(utcDateTime, timeZone).DateTime;
    }

    private static TimeZoneInfo GetMlbScheduleTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
    }

    private static decimal? CalculateImpliedProbability(decimal? decimalPrice)
    {
        return decimalPrice.HasValue && decimalPrice.Value > 0
            ? Math.Round(1m / decimalPrice.Value, 6)
            : null;
    }

    private static decimal? CalculateEstimatedEv(decimal? winProbability, decimal? loseProbability, decimal? decimalPrice)
    {
        return winProbability.HasValue && loseProbability.HasValue && decimalPrice.HasValue
            ? Math.Round((winProbability.Value * (decimalPrice.Value - 1m)) - loseProbability.Value, 6)
            : null;
    }

    private static decimal? CalculatePriceSpread(IEnumerable<decimal?> prices)
    {
        var values = prices
            .Where(price => price.HasValue)
            .Select(price => price!.Value)
            .ToList();

        return values.Count < 2
            ? null
            : Math.Round(values.Max() - values.Min(), 6);
    }

    private static IReadOnlyList<string> BuildLineShoppingMissingFieldFindings(
        IReadOnlyCollection<TheOddsLineShoppingSourceRow> rows,
        IReadOnlyCollection<TheOddsLineShoppingGroup> groups)
    {
        var findings = new List<string>();

        if (rows.Any(row => string.IsNullOrWhiteSpace(row.EventId)))
        {
            findings.Add("Some rows are missing event id metadata.");
        }

        if (rows.Any(row => !row.EventStartTime.HasValue))
        {
            findings.Add("Some rows are missing event start time metadata.");
        }

        if (rows.Any(row => !row.OverDecimalPrice.HasValue && !row.UnderDecimalPrice.HasValue))
        {
            findings.Add("Some rows are missing both over and under decimal prices.");
        }

        if (groups.All(group => group.BookmakerPrices.Count <= 1))
        {
            findings.Add("No comparison group has multiple bookmakers yet.");
        }

        return findings.Count == 0
            ? ["No obvious missing fields for read-only line-shopping display."]
            : findings;
    }

    private static bool HasDataProvider(JsonDocument? metadata)
    {
        return metadata?.RootElement.ValueKind == JsonValueKind.Object
            && metadata.RootElement.TryGetProperty("dataProvider", out var provider)
            && provider.ValueKind == JsonValueKind.String
            && string.Equals(provider.GetString(), "The Odds API", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<object> CountReasons(IEnumerable<string> reasons)
    {
        return reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                reason = group.Key,
                count = group.Count()
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.reason)
            .Cast<object>()
            .ToList();
    }

    private static string BuildMissingBookmakerExplanation(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> persistedRows,
        IReadOnlyCollection<TheOddsImpactPredictionRow> predictionRows)
    {
        var persistedBookmakers = persistedRows.Select(row => row.PlatformName).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var predictionBookmakers = predictionRows.Select(row => row.Bookmaker).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var missingFromPredictions = persistedBookmakers
            .Except(predictionBookmakers, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return missingFromPredictions.Length == 0
            ? "All persisted The Odds API bookmaker platforms have at least one prediction in this run."
            : $"These persisted bookmaker platforms have no predictions in this run: {string.Join(", ", missingFromPredictions)}. Most likely causes are no generated prediction for their props, missing historical feature data, prop-type support gaps, or the run predates the persisted rows.";
    }

    private static async Task<IReadOnlyDictionary<TheOddsActualResultKey, decimal>> LoadTheOddsMlbActualResultsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.League != null && result.League.Code == "MLB")
            .Select(result => new
            {
                result.PlayerId,
                result.PropTypeId,
                result.EventDate,
                result.ActualResultValue
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new TheOddsActualResultKey(row.PlayerId, row.PropTypeId, row.EventDate))
            .ToDictionary(group => group.Key, group => group.First().ActualResultValue);
    }

    private static async Task<IReadOnlyDictionary<Guid, Guid[]>> LoadTheOddsEquivalentPropTypeIdsByIdAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var propTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(propType => new
            {
                propType.Id,
                propType.Name
            })
            .ToListAsync(cancellationToken);

        return propTypes
            .GroupBy(propType => CanonicalTheOddsPropTypeName(propType.Name), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var equivalentIds = group.Select(propType => propType.Id).Distinct().ToArray();
                return group.Select(propType => new
                {
                    propType.Id,
                    EquivalentIds = equivalentIds
                });
            })
            .ToDictionary(row => row.Id, row => row.EquivalentIds);
    }

    private static async Task<Guid[]> GetTheOddsEquivalentPlayerIdsAsync(
        Guid playerId,
        IPlayerIdentityAliasService playerIdentityAliasService,
        Dictionary<Guid, Guid[]> aliasCache,
        CancellationToken cancellationToken)
    {
        if (aliasCache.TryGetValue(playerId, out var cachedPlayerIds))
        {
            return cachedPlayerIds;
        }

        var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
            playerId,
            "MLB",
            cancellationToken);
        var equivalentPlayerIdArray = equivalentPlayerIds.Distinct().ToArray();
        aliasCache[playerId] = equivalentPlayerIdArray;

        return equivalentPlayerIdArray;
    }

    private static decimal? FindTheOddsActualResult(
        IReadOnlyDictionary<TheOddsActualResultKey, decimal> actualResults,
        DateOnly eventDate,
        IReadOnlyCollection<Guid> equivalentPlayerIds,
        IReadOnlyCollection<Guid> equivalentPropTypeIds)
    {
        foreach (var playerId in equivalentPlayerIds)
        {
            foreach (var propTypeId in equivalentPropTypeIds)
            {
                if (actualResults.TryGetValue(new TheOddsActualResultKey(playerId, propTypeId, eventDate), out var actualResult))
                {
                    return actualResult;
                }
            }
        }

        return null;
    }

    private static string? GradeTheOddsBestSide(string bestSide, decimal line, decimal? actualResult)
    {
        if (!actualResult.HasValue || bestSide == "None")
        {
            return null;
        }

        return bestSide switch
        {
            "Over" when actualResult.Value > line => "Win",
            "Over" when actualResult.Value < line => "Loss",
            "Under" when actualResult.Value < line => "Win",
            "Under" when actualResult.Value > line => "Loss",
            _ when actualResult.Value == line => "Push",
            _ => null
        };
    }

    private static decimal? CalculateTheOddsProfitPerDollarStake(string? outcome, decimal? decimalPrice)
    {
        return outcome switch
        {
            "Win" when decimalPrice.HasValue => Math.Round(decimalPrice.Value - 1m, 4),
            "Loss" => -1m,
            "Push" => 0m,
            _ => null
        };
    }

    private static object BuildTheOddsLineShoppingBacktestSummary(
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        var gradedRows = rows.Where(IsTheOddsGraded).ToArray();

        return new
        {
            totalGroups = rows.Count,
            gradedGroups = gradedRows.Length,
            ungradedGroups = rows.Count - gradedRows.Length,
            wins = gradedRows.Count(row => row.Outcome == "Win"),
            losses = gradedRows.Count(row => row.Outcome == "Loss"),
            pushes = gradedRows.Count(row => row.Outcome == "Push"),
            hitRate = TheOddsHitRate(gradedRows),
            simulatedProfit = TheOddsProfit(gradedRows),
            simulatedRoi = TheOddsRoi(gradedRows),
            averageBestEstimatedEv = AverageNullable(rows.Select(row => row.BestEstimatedEv)),
            averageBestProbability = AverageNullable(rows.Select(row => row.BestProbability)),
            averageBestImpliedProbability = AverageNullable(rows.Select(row => row.BestImpliedProbability)),
            averageProbabilityEdge = AverageNullable(rows.Select(row => row.BestProbabilityEdge)),
            averageHistoricalSampleSize = AverageInt(rows.Select(row => row.HistoricalSampleSize)),
            averageBestPrice = AverageNullable(rows.Select(row => row.BestDecimalPrice))
        };
    }

    private static object[] BuildTheOddsLineShoppingBacktestBreakdown(
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows,
        Func<TheOddsLineShoppingBacktestRow, string> groupKey,
        string dimension)
    {
        return rows
            .GroupBy(groupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildTheOddsLineShoppingBacktestGroupSummary(dimension, group.Key, group.ToArray()))
            .OrderByDescending(row => row.gradedCount)
            .ThenBy(row => row.group)
            .Cast<object>()
            .ToArray();
    }

    private static TheOddsBacktestGroupSummary BuildTheOddsLineShoppingBacktestGroupSummary(
        string dimension,
        string group,
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        var gradedRows = rows.Where(IsTheOddsGraded).ToArray();
        var ungradedRows = rows.Count - gradedRows.Length;

        return new TheOddsBacktestGroupSummary(
            dimension,
            group,
            group,
            rows.Count,
            rows.Count,
            gradedRows.Length,
            gradedRows.Length,
            ungradedRows,
            gradedRows.Count(row => row.Outcome == "Win"),
            gradedRows.Count(row => row.Outcome == "Loss"),
            gradedRows.Count(row => row.Outcome == "Push"),
            TheOddsHitRate(gradedRows),
            TheOddsProfit(gradedRows),
            TheOddsRoi(gradedRows),
            AverageNullable(rows.Select(row => row.BestEstimatedEv)),
            AverageNullable(rows.Select(row => row.BestProbability)),
            AverageNullable(rows.Select(row => row.BestImpliedProbability)),
            AverageNullable(rows.Select(row => row.BestProbabilityEdge)),
            AverageInt(rows.Select(row => row.HistoricalSampleSize)),
            AverageNullable(rows.Select(row => row.BestDecimalPrice)));
    }

    private static object[] BuildTheOddsModelProbabilityCalibrationTable(
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        return rows
            .Where(row => IsTheOddsGraded(row) && row.BestProbability.HasValue)
            .GroupBy(row => ProbabilityBucket(row.BestProbability), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupRows = group.ToArray();
                var averagePredictedProbability = AverageNullable(groupRows.Select(row => row.BestProbability)) ?? 0m;
                var actualHitRate = TheOddsHitRate(groupRows);

                return new
                {
                    predictedProbabilityBucket = group.Key,
                    gradedGroups = groupRows.Length,
                    averagePredictedProbability,
                    actualHitRate,
                    calibrationError = Math.Round(actualHitRate - averagePredictedProbability, 6),
                    simulatedRoi = TheOddsRoi(groupRows)
                };
            })
            .OrderBy(row => ProbabilityBucketSortKey(row.predictedProbabilityBucket))
            .Cast<object>()
            .ToArray();
    }

    private static TheOddsStrategySimulationSummary[] BuildTheOddsStrategySimulations(
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        var gradedRows = rows.Where(IsTheOddsGraded).ToArray();
        var strategies = new (string Name, string Description, Func<TheOddsLineShoppingBacktestRow, bool> Filter)[]
        {
            ("All Graded", "Every graded line-shopping group.", _ => true),
            ("Exclude Line 0.5", "All graded groups except line 0.5.", row => row.Line != 0.5m),
            ("Line 1.5 Only", "Only line 1.5 groups.", row => row.Line == 1.5m),
            ("Pitcher Strikeouts Only", "Only Pitcher Strikeouts groups.", row => IsTheOddsPropType(row, "Pitcher Strikeouts")),
            ("Total Bases Only", "Only Total Bases groups.", row => IsTheOddsPropType(row, "Total Bases")),
            ("Hits Only", "Only Hits groups.", row => IsTheOddsPropType(row, "Hits")),
            ("Medium Trust Only", "Only Medium trust groups.", row => IsTheOddsTrustLevel(row, "Medium")),
            ("Strong Trust Only", "Only Strong trust groups.", row => IsTheOddsTrustLevel(row, "Strong")),
            ("Medium Total Bases", "Medium trust Total Bases groups.", row => IsTheOddsTrustLevel(row, "Medium") && IsTheOddsPropType(row, "Total Bases")),
            ("DraftKings Only", "Only groups where the best price is from DraftKings.", row => IsTheOddsBookmaker(row, "DraftKings")),
            ("BetMGM Only", "Only groups where the best price is from BetMGM.", row => IsTheOddsBookmaker(row, "BetMGM")),
            ("DraftKings Total Bases", "DraftKings best-price Total Bases groups.", row => IsTheOddsBookmaker(row, "DraftKings") && IsTheOddsPropType(row, "Total Bases")),
            ("Exclude BetMGM Batter Props", "All graded groups except BetMGM best-price batter props.", row => !(IsTheOddsBookmaker(row, "BetMGM") && IsTheOddsBatterProp(row))),
            ("Exclude BestPrice >= 2.00 Batter Props", "All graded groups except batter props with best decimal price at least 2.00.", row => !(IsTheOddsBatterProp(row) && row.BestDecimalPrice >= 2.00m)),
            ("BestPrice 1.75 to 1.99", "Only groups with best decimal price from 1.75 through 1.99.", row => row.BestDecimalPrice >= 1.75m && row.BestDecimalPrice < 2.00m),
            ("Positive EV Only", "Only groups with positive best estimated EV.", row => row.BestEstimatedEv > 0m),
            ("Positive Edge Only", "Only groups with positive best probability edge.", row => row.BestProbabilityEdge > 0m),
            ("EV 0.10 to 0.20 Only", "Only groups with best estimated EV from 0.10 through below 0.20.", row => row.BestEstimatedEv >= 0.10m && row.BestEstimatedEv < 0.20m),
            ("Exclude EV 0.20+", "All graded groups except best estimated EV of 0.20 or higher.", row => row.BestEstimatedEv < 0.20m || !row.BestEstimatedEv.HasValue),
            ("Conservative Candidate Strategy", "Exclude line 0.5 batter props, exclude best price >= 2.00 batter props, include Pitcher Strikeouts or Total Bases, and exclude ResearchOnly.", row =>
                !(IsTheOddsBatterProp(row) && row.Line == 0.5m)
                && !(IsTheOddsBatterProp(row) && row.BestDecimalPrice >= 2.00m)
                && (IsTheOddsPropType(row, "Pitcher Strikeouts") || IsTheOddsPropType(row, "Total Bases"))
                && !IsTheOddsTrustLevel(row, "ResearchOnly")),
            ("Experimental Plus-Money Fade Diagnostic", "Line 0.5 batter props with best decimal price at least 2.00.", row =>
                IsTheOddsBatterProp(row)
                && row.Line == 0.5m
                && row.BestDecimalPrice >= 2.00m)
        };

        return strategies
            .Select(strategy => BuildTheOddsStrategySimulation(strategy.Name, strategy.Description, gradedRows.Where(strategy.Filter).ToArray()))
            .ToArray();
    }

    private static TheOddsStrategySimulationSummary? BuildTheOddsQueryFilteredStrategy(
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows,
        int? minSampleSize,
        string? bookmaker,
        string? propType,
        string? trustLevel,
        decimal? minBestProbability,
        decimal? maxBestProbability,
        decimal? minEstimatedEv,
        decimal? maxEstimatedEv,
        decimal? minBestPrice,
        decimal? maxBestPrice,
        decimal? line)
    {
        var hasAnyFilter = minSampleSize.HasValue
            || !string.IsNullOrWhiteSpace(bookmaker)
            || !string.IsNullOrWhiteSpace(propType)
            || !string.IsNullOrWhiteSpace(trustLevel)
            || minBestProbability.HasValue
            || maxBestProbability.HasValue
            || minEstimatedEv.HasValue
            || maxEstimatedEv.HasValue
            || minBestPrice.HasValue
            || maxBestPrice.HasValue
            || line.HasValue;

        if (!hasAnyFilter)
        {
            return null;
        }

        var filteredRows = rows
            .Where(IsTheOddsGraded)
            .Where(row => !minSampleSize.HasValue || row.HistoricalSampleSize >= minSampleSize.Value)
            .Where(row => string.IsNullOrWhiteSpace(bookmaker) || IsTheOddsBookmaker(row, bookmaker))
            .Where(row => string.IsNullOrWhiteSpace(propType) || IsTheOddsPropType(row, propType))
            .Where(row => string.IsNullOrWhiteSpace(trustLevel) || IsTheOddsTrustLevel(row, trustLevel))
            .Where(row => !minBestProbability.HasValue || row.BestProbability >= minBestProbability.Value)
            .Where(row => !maxBestProbability.HasValue || row.BestProbability <= maxBestProbability.Value)
            .Where(row => !minEstimatedEv.HasValue || row.BestEstimatedEv >= minEstimatedEv.Value)
            .Where(row => !maxEstimatedEv.HasValue || row.BestEstimatedEv <= maxEstimatedEv.Value)
            .Where(row => !minBestPrice.HasValue || row.BestDecimalPrice >= minBestPrice.Value)
            .Where(row => !maxBestPrice.HasValue || row.BestDecimalPrice <= maxBestPrice.Value)
            .Where(row => !line.HasValue || row.Line == line.Value)
            .ToArray();

        return BuildTheOddsStrategySimulation(
            "Query Filtered Strategy",
            "Ad hoc strategy created from line-shopping-backtest query parameters.",
            filteredRows);
    }

    private static TheOddsStrategySimulationSummary BuildTheOddsStrategySimulation(
        string strategyName,
        string description,
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        return new TheOddsStrategySimulationSummary(
            strategyName,
            description,
            rows.Count,
            rows.Count,
            rows.Count(row => row.Outcome == "Win"),
            rows.Count(row => row.Outcome == "Loss"),
            rows.Count(row => row.Outcome == "Push"),
            TheOddsHitRate(rows),
            TheOddsProfit(rows),
            TheOddsRoi(rows),
            AverageNullable(rows.Select(row => row.BestEstimatedEv)),
            AverageNullable(rows.Select(row => row.BestProbability)),
            AverageNullable(rows.Select(row => row.BestImpliedProbability)),
            AverageNullable(rows.Select(row => row.BestProbabilityEdge)),
            AverageInt(rows.Select(row => row.HistoricalSampleSize)),
            AverageNullable(rows.Select(row => row.BestDecimalPrice)));
    }

    private static async Task<TheOddsStrategyBacktestComputation?> BuildTheOddsLineShoppingBacktestComputationAsync(
        Guid runId,
        ApplicationDbContext dbContext,
        IPredictionProbabilityService probabilityService,
        IPlayerIdentityAliasService playerIdentityAliasService,
        CancellationToken cancellationToken)
    {
        var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
        var platformProps = ParseTheOddsPlatformProps(platformPropDataRows);
        var theOddsPropIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
        var probabilityAnalysis = await probabilityService.AnalyzeRunForPropsAsync(runId, theOddsPropIds, cancellationToken);
        if (probabilityAnalysis is null)
        {
            return null;
        }

        var lineShoppingRows = BuildTheOddsLineShoppingRows(
            platformProps,
            probabilityAnalysis,
            DateTimeOffset.UtcNow.AddHours(-FreshnessWindowHours));
        var lineShoppingGroups = BuildTheOddsLineShoppingGroups(lineShoppingRows);
        var actualResults = await LoadTheOddsMlbActualResultsAsync(dbContext, cancellationToken);
        var equivalentPropTypeIdsById = await LoadTheOddsEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
        var playerAliasCache = new Dictionary<Guid, Guid[]>();
        var backtestRows = await BuildTheOddsBacktestRowsAsync(
            lineShoppingGroups,
            actualResults,
            equivalentPropTypeIdsById,
            playerIdentityAliasService,
            playerAliasCache,
            cancellationToken);

        return new TheOddsStrategyBacktestComputation(probabilityAnalysis, backtestRows);
    }

    private static object ToTheOddsStrategySnapshotResponse(LineShoppingStrategyBacktestSnapshot snapshot)
    {
        return new
        {
            snapshot.Id,
            snapshot.RunId,
            snapshot.Sport,
            snapshot.SlateDate,
            snapshot.StrategyName,
            snapshot.Description,
            snapshot.TotalGroups,
            snapshot.GradedGroups,
            snapshot.Wins,
            snapshot.Losses,
            snapshot.Pushes,
            snapshot.HitRate,
            snapshot.SimulatedProfit,
            snapshot.SimulatedRoi,
            snapshot.AverageBestEstimatedEv,
            snapshot.AverageBestProbability,
            snapshot.AverageBestImpliedProbability,
            snapshot.AverageProbabilityEdge,
            snapshot.AverageHistoricalSampleSize,
            snapshot.AverageBestPrice,
            snapshot.GeneratedAt,
            snapshot.CreatedAt
        };
    }

    private static bool IsTheOddsPropType(TheOddsLineShoppingBacktestRow row, string propType)
    {
        return string.Equals(row.PropType, propType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTheOddsBookmaker(TheOddsLineShoppingBacktestRow row, string bookmaker)
    {
        return string.Equals(row.BestBookmaker, bookmaker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTheOddsTrustLevel(TheOddsLineShoppingBacktestRow row, string trustLevel)
    {
        return string.Equals(row.TrustLevel, trustLevel, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTheOddsBatterProp(TheOddsLineShoppingBacktestRow row)
    {
        return !row.PropType.Contains("Pitcher", StringComparison.OrdinalIgnoreCase);
    }

    private static object ToTheOddsLineShoppingBacktestSample(TheOddsLineShoppingBacktestRow row)
    {
        return new
        {
            eventName = row.EventName,
            row.EventId,
            row.EventStartTime,
            row.EventStartTimeUtc,
            row.ExpectedResultDate,
            row.ResultDateSource,
            row.UtcDate,
            row.ResolvedMlbDate,
            player = row.Player,
            propType = row.PropType,
            line = row.Line,
            eventInfo = new
            {
                row.EventId,
                row.EventStartTime,
                row.HomeTeam,
                row.AwayTeam
            },
            row.BestSide,
            row.BestBookmaker,
            row.BestAmericanPrice,
            row.BestDecimalPrice,
            row.BestEstimatedEv,
            row.BestProbability,
            row.TrustLevel,
            sampleSize = row.HistoricalSampleSize,
            row.ConfidenceScore,
            row.ActualResult,
            row.Outcome,
            profit = row.Profit
        };
    }

    private static object ToTheOddsLineShoppingGradedBacktestSample(TheOddsLineShoppingBacktestRow row)
    {
        return new
        {
            row.EventName,
            row.EventId,
            row.EventStartTime,
            row.EventStartTimeUtc,
            row.ExpectedResultDate,
            row.ResultDateSource,
            row.UtcDate,
            row.ResolvedMlbDate,
            player = row.Player,
            propType = row.PropType,
            line = row.Line,
            row.BestSide,
            row.BestBookmaker,
            row.BestAmericanPrice,
            row.BestDecimalPrice,
            row.BestProbability,
            row.BestEstimatedEv,
            row.TrustLevel,
            historicalSampleSize = row.HistoricalSampleSize,
            resultValue = row.ActualResult,
            row.Outcome,
            profit = row.Profit
        };
    }

    private static object ToTheOddsLineShoppingUngradedBacktestSample(TheOddsLineShoppingBacktestRow row)
    {
        return new
        {
            row.EventName,
            row.EventId,
            row.EventStartTime,
            row.EventStartTimeUtc,
            row.ExpectedResultDate,
            row.ResultDateSource,
            row.UtcDate,
            row.ResolvedMlbDate,
            player = row.Player,
            propType = row.PropType,
            line = row.Line,
            row.BestSide,
            row.BestBookmaker,
            row.TrustLevel,
            historicalSampleSize = row.HistoricalSampleSize,
            ungradedReason = row.UngradedReason ?? "Unknown",
            playerMappingStatus = row.HistoricalSampleSize > 0 ? "Resolved" : "Unknown",
            propTypeMappingStatus = string.IsNullOrWhiteSpace(row.PropType) ? "Unknown" : "Resolved"
        };
    }

    private static object[] BuildTheOddsTopUngradedReasons(
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> ungradedRows)
    {
        return ungradedRows
            .GroupBy(row => row.UngradedReason ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                reason = group.Key,
                count = group.Count()
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.reason)
            .Cast<object>()
            .ToArray();
    }

    private static string BuildTheOddsUngradedReason(
        TheOddsLineShoppingGroup group,
        decimal? actualResult,
        string? outcome)
    {
        if (outcome is "Win" or "Loss" or "Push")
        {
            return "Graded";
        }

        if (group.BestSide == "None")
        {
            return "NoBestSide";
        }

        if (!group.BestDecimalPrice.HasValue)
        {
            return "MissingBestPrice";
        }

        if (actualResult.HasValue)
        {
            return "UnableToGrade";
        }

        if (group.EventStartTime.HasValue && group.EventStartTime.Value > DateTimeOffset.UtcNow)
        {
            return "EventNotStarted";
        }

        return "NoMatchingPropResult";
    }

    private static object BuildTheOddsLineShoppingBacktestDiagnosis(
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        var gradedRows = rows.Where(IsTheOddsGraded).ToArray();
        var positiveEvRows = gradedRows.Where(row => row.BestEstimatedEv > 0m).ToArray();
        var trustGroups = BuildTheOddsLineShoppingBacktestBreakdown(gradedRows, row => row.TrustLevel, "trustLevel")
            .Cast<TheOddsBacktestGroupSummary>()
            .ToArray();
        var segmentGroups = new[]
            {
                BuildTheOddsLineShoppingBacktestBreakdown(gradedRows, row => row.TrustLevel, "trustLevel").Cast<TheOddsBacktestGroupSummary>(),
                BuildTheOddsLineShoppingBacktestBreakdown(gradedRows, row => row.PropType, "propType").Cast<TheOddsBacktestGroupSummary>(),
                BuildTheOddsLineShoppingBacktestBreakdown(gradedRows, row => row.BestSide, "bestSide").Cast<TheOddsBacktestGroupSummary>(),
                BuildTheOddsLineShoppingBacktestBreakdown(gradedRows, row => row.BestBookmaker ?? "Unknown", "bestBookmaker").Cast<TheOddsBacktestGroupSummary>(),
                BuildTheOddsLineShoppingBacktestBreakdown(gradedRows, row => BestEvBucket(row.BestEstimatedEv), "bestEstimatedEvBucket").Cast<TheOddsBacktestGroupSummary>()
            }
            .SelectMany(group => group)
            .Where(group => group.gradedCount >= 5)
            .ToArray();
        var strongestSegment = segmentGroups
            .OrderByDescending(group => group.simulatedRoi)
            .ThenByDescending(group => group.simulatedProfit)
            .FirstOrDefault();
        var weakestSegment = segmentGroups
            .OrderBy(group => group.simulatedRoi)
            .ThenBy(group => group.simulatedProfit)
            .FirstOrDefault();
        var strongGroup = trustGroups.FirstOrDefault(group => group.group == "Strong");
        var mediumWeakGroups = trustGroups.Where(group => group.group is "Medium" or "Weak").ToArray();
        var mediumWeakRoi = mediumWeakGroups.Length == 0
            ? 0m
            : Math.Round(mediumWeakGroups.Sum(group => group.simulatedProfit) / Math.Max(1, mediumWeakGroups.Sum(group => group.gradedCount)), 4);
        var profitableTrustLevels = trustGroups
            .Where(group => group.gradedCount >= 5 && group.simulatedProfit > 0m && group.simulatedRoi > 0m)
            .OrderByDescending(group => group.simulatedRoi)
            .Select(group => group.group)
            .ToArray();
        var profitablePropTypes = BuildTheOddsLineShoppingBacktestBreakdown(gradedRows, row => row.PropType, "propType")
            .Cast<TheOddsBacktestGroupSummary>()
            .Where(group => group.gradedCount >= 5 && group.simulatedProfit > 0m && group.simulatedRoi > 0m)
            .OrderByDescending(group => group.simulatedRoi)
            .Select(group => group.group)
            .ToArray();

        return new
        {
            arePositiveEvGroupsProfitable = TheOddsProfit(positiveEvRows) > 0m,
            doStrongGroupsOutperform = strongGroup is not null
                && strongGroup.gradedCount > 0
                && strongGroup.simulatedRoi > mediumWeakRoi,
            strongestProfitableSegment = strongestSegment is null || strongestSegment.simulatedProfit <= 0m
                ? "No profitable segment with at least 5 graded groups was found."
                : $"{strongestSegment.dimension}: {strongestSegment.group} ROI {strongestSegment.simulatedRoi:P2}, profit {strongestSegment.simulatedProfit}.",
            weakestSegment = weakestSegment is null
                ? "No graded segment with at least 5 groups was found."
                : $"{weakestSegment.dimension}: {weakestSegment.group} ROI {weakestSegment.simulatedRoi:P2}, profit {weakestSegment.simulatedProfit}.",
            recommendedTrustFilter = profitableTrustLevels.Length == 0
                ? ["Keep all trust levels research-only until more completed results are graded."]
                : profitableTrustLevels,
            recommendedPropTypeFilters = profitablePropTypes.Length == 0
                ? ["No prop type has a profitable graded sample of at least 5 groups yet."]
                : profitablePropTypes,
            recommendedNextSteps = new[]
            {
                "Continue accumulating completed The Odds API standard market results before surfacing as recommendations.",
                "Use this endpoint after each completed slate to validate whether Strong trust groups and positive best EV groups remain profitable.",
                "If profitable segments remain stable, promote those filters into dashboard defaults before changing prediction generation."
            }
        };
    }

    private static async Task<Dictionary<Guid, int>> LoadTheOddsLineSnapshotCountsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        CancellationToken cancellationToken)
    {
        var platformPropIds = platformProps.Select(row => row.PlatformPropGuid).Distinct().ToArray();
        if (platformPropIds.Length == 0)
        {
            return [];
        }

        var rows = await dbContext.PropLineSnapshots
            .AsNoTracking()
            .Where(snapshot => platformPropIds.Contains(snapshot.PlatformPropId))
            .GroupBy(snapshot => snapshot.PlatformPropId)
            .Select(group => new
            {
                PlatformPropId = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.PlatformPropId, row => row.Count);
    }

    private static async Task<Dictionary<Guid, TheOddsLineSnapshotDiagnosticRow>> LoadTheOddsLineSnapshotDiagnosticsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        CancellationToken cancellationToken)
    {
        var platformPropIds = platformProps.Select(row => row.PlatformPropGuid).Distinct().ToArray();
        if (platformPropIds.Length == 0)
        {
            return [];
        }

        var rows = await dbContext.PropLineSnapshots
            .AsNoTracking()
            .Where(snapshot => platformPropIds.Contains(snapshot.PlatformPropId))
            .GroupBy(snapshot => snapshot.PlatformPropId)
            .Select(group => new TheOddsLineSnapshotDiagnosticRow(
                group.Key,
                group.Count(),
                group.Max(snapshot => snapshot.ObservedAt)))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.PlatformPropGuid, row => row);
    }

    private static async Task<Dictionary<Guid, TheOddsPredictionDiagnosticRow>> LoadTheOddsPredictionDiagnosticsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var propIds = platformProps.Select(row => row.PropId).Distinct().ToArray();
        if (propIds.Length == 0)
        {
            return [];
        }

        var rows = await dbContext.Predictions
            .AsNoTracking()
            .Where(prediction => prediction.PredictionRunId == runId
                && propIds.Contains(prediction.PropId))
            .Select(prediction => new TheOddsPredictionDiagnosticRow(
                prediction.PropId,
                prediction.PredictionRunId))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.PropId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private static async Task<Dictionary<Guid, TheOddsPlayerRefDiagnosticRow>> LoadTheOddsPlayerRefDiagnosticsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        CancellationToken cancellationToken)
    {
        var playerIds = platformProps.Select(row => row.PlayerId).Distinct().ToArray();
        if (playerIds.Length == 0)
        {
            return [];
        }

        var rows = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => playerIds.Contains(reference.PlayerId))
            .GroupBy(reference => reference.PlayerId)
            .Select(group => new TheOddsPlayerRefDiagnosticRow(
                group.Key,
                group.Count(),
                group.Any(reference => reference.Provider == "mlb")))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.PlayerId, row => row);
    }

    private static async Task<TheOddsPredictionRunContext?> LoadTheOddsDiagnosticRunContextAsync(
        ApplicationDbContext dbContext,
        Guid? runId,
        string requestedBookmakerKey,
        string? requestedEventId,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        CancellationToken cancellationToken)
    {
        if (runId.HasValue)
        {
            return await dbContext.PredictionRuns
                .AsNoTracking()
                .Where(run => run.Id == runId.Value)
                .Select(run => new TheOddsPredictionRunContext(run.Id, run.ModelName, run.ModelVersion, run.CreatedAt, run.Notes))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var eventIds = platformProps
            .Select(row => row.EventId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(requestedEventId))
        {
            eventIds = [requestedEventId];
        }

        var runs = await dbContext.PredictionRuns
            .AsNoTracking()
            .Where(run => run.Notes != null
                && run.Notes.Contains("Scoped The Odds API MLB"))
            .OrderByDescending(run => run.CreatedAt)
            .Select(run => new TheOddsPredictionRunContext(run.Id, run.ModelName, run.ModelVersion, run.CreatedAt, run.Notes))
            .Take(50)
            .ToListAsync(cancellationToken);

        return runs.FirstOrDefault(run =>
        {
            var scope = ParseTheOddsRunScope(run.Notes);
            if (scope is null)
            {
                return false;
            }

            var eventMatches = scope.EventId is null
                || scope.EventId == "any"
                || eventIds.Count == 0
                || eventIds.Contains(scope.EventId);
            var bookmakerMatches = scope.Bookmakers.Count == 0
                || scope.Bookmakers.Contains("any")
                || scope.Bookmakers.Contains(requestedBookmakerKey);

            return eventMatches && bookmakerMatches;
        })
        ?? runs.FirstOrDefault();
    }

    private static TheOddsRunScope? ParseTheOddsRunScope(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var eventId = ExtractTheOddsRunScopeValue(notes, "eventId");
        var bookmakers = SplitTheOddsCsv(ExtractTheOddsRunScopeValue(notes, "bookmakers"))
            .Select(value => NormalizeTheOddsBookmakerKey(value, value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var markets = SplitTheOddsCsv(ExtractTheOddsRunScopeValue(notes, "markets"));

        return new TheOddsRunScope(eventId, bookmakers, markets);
    }

    private static string? ExtractTheOddsRunScopeValue(string notes, string key)
    {
        var marker = $"{key}=";
        var startIndex = notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += marker.Length;
        var endIndex = notes.IndexOf(';', startIndex);
        var value = endIndex < 0
            ? notes[startIndex..]
            : notes[startIndex..endIndex];
        value = value.Trim().TrimEnd('.');

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlySet<string> SplitTheOddsCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static TheOddsRunMatch BuildTheOddsRunMatch(
        TheOddsPersistedPlatformPropRow platformProp,
        TheOddsPredictionRunContext? selectedRun,
        TheOddsRunScope? selectedRunScope,
        string requestedBookmakerKey,
        bool ignoreRunContext)
    {
        if (ignoreRunContext || selectedRun is null || selectedRunScope is null)
        {
            return new TheOddsRunMatch(null, null, null);
        }

        var rowBookmakerKey = NormalizeTheOddsBookmakerKey(platformProp.BookmakerKey, platformProp.PlatformName);
        var runMatchesEvent = selectedRunScope.EventId is null
            || selectedRunScope.EventId == "any"
            || string.Equals(selectedRunScope.EventId, platformProp.EventId, StringComparison.OrdinalIgnoreCase);
        var runMatchesBookmaker = selectedRunScope.Bookmakers.Count == 0
            || selectedRunScope.Bookmakers.Contains("any")
            || selectedRunScope.Bookmakers.Contains(rowBookmakerKey)
            || selectedRunScope.Bookmakers.Contains(requestedBookmakerKey);
        var runMatchesMarket = selectedRunScope.Markets.Count == 0
            || selectedRunScope.Markets.Contains("any")
            || (!string.IsNullOrWhiteSpace(platformProp.MarketKey)
                && selectedRunScope.Markets.Contains(platformProp.MarketKey));

        return new TheOddsRunMatch(runMatchesEvent, runMatchesBookmaker, runMatchesMarket);
    }

    private static string DetermineTheOddsDropoutReason(
        TheOddsPersistedPlatformPropRow platformProp,
        TheOddsPredictionRunContext? selectedRun,
        TheOddsRunMatch runMatch,
        bool ignoreRunContext,
        TheOddsLineSnapshotDiagnosticRow? snapshotDiagnostic,
        TheOddsPredictionDiagnosticRow? predictionDiagnostic,
        PlayerHistoricalLookupDiagnostic historicalLookup,
        TheOddsPlayerRefDiagnosticRow? playerRefDiagnostic)
    {
        if (predictionDiagnostic is not null)
        {
            return "None";
        }

        if (string.IsNullOrWhiteSpace(platformProp.EventId)
            || string.IsNullOrWhiteSpace(platformProp.MarketKey)
            || platformProp.PlayerId == Guid.Empty
            || platformProp.PropTypeId == Guid.Empty)
        {
            return "MetadataMissing";
        }

        if (snapshotDiagnostic is null || snapshotDiagnostic.Count == 0)
        {
            return "NoLineSnapshot";
        }

        if (selectedRun is null)
        {
            return ignoreRunContext
                ? DetermineTheOddsPredictionReadinessReason(platformProp, historicalLookup, playerRefDiagnostic)
                : "NoMatchingPredictionRunForEvent";
        }

        if (!ignoreRunContext && runMatch.RunMatchesEvent == false)
        {
            return "NoMatchingPredictionRunForEvent";
        }

        if (!ignoreRunContext && runMatch.RunMatchesBookmaker == false)
        {
            return "NoMatchingPredictionRunForBookmaker";
        }

        if (!ignoreRunContext && runMatch.RunMatchesMarket == false)
        {
            return "FilteredOutByScopedGeneration";
        }

        var readinessReason = DetermineTheOddsPredictionReadinessReason(platformProp, historicalLookup, playerRefDiagnostic);
        return readinessReason == "None"
            ? "FilteredOutByScopedGeneration"
            : readinessReason;
    }

    private static string DetermineTheOddsPredictionReadinessReason(
        TheOddsPersistedPlatformPropRow platformProp,
        PlayerHistoricalLookupDiagnostic historicalLookup,
        TheOddsPlayerRefDiagnosticRow? playerRefDiagnostic)
    {
        if (!historicalLookup.HasMlbMappingForEquivalentPlayers
            && (playerRefDiagnostic is null || !playerRefDiagnostic.HasMlbStatsMapping))
        {
            return "PlayerMappingMissing";
        }

        if (!historicalLookup.HasSupportedPropTypeHistory)
        {
            return "UnsupportedPropType";
        }

        if (historicalLookup.HistoricalGamesAvailable == 0)
        {
            return historicalLookup.HistoricalResultsForEquivalentPlayers > 0
                ? "PropTypeEquivalenceMissing"
                : "HistoricalFeaturesMissing";
        }

        if (historicalLookup.SeasonGamesAvailable == 0)
        {
            return "InsufficientHistoricalSample";
        }

        return "None";
    }

    private static object BuildTheOddsBookmakerPredictionDropoutSummary(
        string bookmaker,
        IReadOnlyCollection<TheOddsBookmakerPredictionDropoutRow> rows)
    {
        var droppedRows = rows.Where(row => row.DropoutReason != "None").ToArray();

        return new
        {
            bookmaker,
            totalPlatformProps = rows.Count,
            withSnapshots = rows.Count(row => row.HasLineSnapshot),
            withPredictions = rows.Count(row => row.HasPredictionInRun),
            droppedBeforePrediction = droppedRows.Length,
            dropoutReasonCounts = rows
                .GroupBy(row => row.DropoutReason, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    reason = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.reason)
                .ToArray(),
            affectedPropTypes = droppedRows
                .GroupBy(row => row.PropTypeName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    propType = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.propType)
                .Take(25)
                .ToArray(),
            affectedPlayers = droppedRows
                .GroupBy(row => row.PlayerName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    player = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.player)
                .Take(25)
                .ToArray(),
            likelyRootCause = BuildTheOddsDropoutLikelyRootCause(rows),
            recommendedFix = BuildTheOddsDropoutRecommendedFix(rows)
        };
    }

    private static object BuildTheOddsBookmakerPredictionDropoutDiagnosis(
        IReadOnlyCollection<TheOddsBookmakerPredictionDropoutRow> rows,
        bool ignoreRunContext)
    {
        var hasRunContextMismatch = !ignoreRunContext
            && rows.Any(row => row.RunMatchesEvent == false
                || row.RunMatchesBookmaker == false
                || row.RunMatchesMarket == false
                || row.DropoutReason is "NoMatchingPredictionRunForEvent" or "NoMatchingPredictionRunForBookmaker");
        var mappingIssuesDetected = rows.Any(row => row.MappingIssueDetected);

        return new
        {
            usesPersistedDatabaseRowsOnly = true,
            callsTheOddsApi = false,
            ignoreRunContext,
            hasPersistedRows = rows.Count > 0,
            allRowsHaveSnapshots = rows.Count > 0 && rows.All(row => row.HasLineSnapshot),
            anyRowsHavePredictions = rows.Any(row => row.HasPredictionInRun),
            hasRunContextMismatch,
            runContextMismatchReason = BuildTheOddsRunContextMismatchReason(rows, ignoreRunContext),
            trueDropoutRootCause = BuildTheOddsTrueDropoutRootCause(rows, ignoreRunContext),
            mappingIssuesDetected,
            likelyRootCause = BuildTheOddsDropoutLikelyRootCause(rows),
            recommendedFix = BuildTheOddsDropoutRecommendedFix(rows)
        };
    }

    private static string BuildTheOddsRunContextMismatchReason(
        IReadOnlyCollection<TheOddsBookmakerPredictionDropoutRow> rows,
        bool ignoreRunContext)
    {
        if (ignoreRunContext)
        {
            return "Run context was intentionally ignored.";
        }

        if (rows.Count == 0)
        {
            return "No rows matched the bookmaker/event filters.";
        }

        if (rows.Any(row => row.RunMatchesEvent == false))
        {
            return "The evaluated prediction run is scoped to a different The Odds API event.";
        }

        if (rows.Any(row => row.RunMatchesBookmaker == false))
        {
            return "The evaluated prediction run is scoped to different bookmaker filters.";
        }

        if (rows.Any(row => row.RunMatchesMarket == false))
        {
            return "The evaluated prediction run is scoped to different market filters.";
        }

        if (rows.Any(row => row.DropoutReason is "NoMatchingPredictionRunForEvent" or "NoMatchingPredictionRunForBookmaker"))
        {
            return "No prediction run was found that matches the audited persisted event/bookmaker scope.";
        }

        return "No run context mismatch detected.";
    }

    private static string BuildTheOddsTrueDropoutRootCause(
        IReadOnlyCollection<TheOddsBookmakerPredictionDropoutRow> rows,
        bool ignoreRunContext)
    {
        if (rows.Count == 0)
        {
            return "No persisted rows matched the diagnostic filters.";
        }

        var runContextReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NoMatchingPredictionRunForEvent",
            "NoMatchingPredictionRunForBookmaker",
            "FilteredOutByScopedGeneration"
        };
        var nonRunReason = rows
            .Where(row => row.DropoutReason != "None"
                && (!runContextReasons.Contains(row.DropoutReason) || ignoreRunContext))
            .GroupBy(row => row.DropoutReason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(nonRunReason))
        {
            return nonRunReason switch
            {
                "PlayerMappingMissing" => "The audited rows are not prediction-ready because MLB player mapping/alias history is missing.",
                "HistoricalFeaturesMissing" => "The audited rows are not prediction-ready because no historical feature data exists before event date.",
                "InsufficientHistoricalSample" => "The audited rows have some historical data but fail the prediction generator history window requirements.",
                "UnsupportedPropType" => "The audited rows use prop types without imported MLB result support.",
                "PropTypeEquivalenceMissing" => "The audited rows have player history but current prop-type equivalence does not connect them to results.",
                "NoLineSnapshot" => "The audited rows are missing line snapshots.",
                "MetadataMissing" => "The audited rows are missing required persisted metadata.",
                _ => "The audited rows are blocked by non-run readiness issues."
            };
        }

        if (!ignoreRunContext && rows.Any(row => runContextReasons.Contains(row.DropoutReason)))
        {
            return "The audited rows are not represented in the evaluated prediction run scope.";
        }

        return "No dropout root cause detected for the selected diagnostic mode.";
    }

    private static string BuildTheOddsDropoutLikelyRootCause(IReadOnlyCollection<TheOddsBookmakerPredictionDropoutRow> rows)
    {
        if (rows.Count == 0)
        {
            return "No persisted The Odds API MLB platform props matched the bookmaker/event filters.";
        }

        var topReason = rows
            .Where(row => row.DropoutReason != "None")
            .GroupBy(row => row.DropoutReason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();

        return topReason switch
        {
            null => "All matched persisted props have predictions in the selected run context.",
            "NoLineSnapshot" => "Matched props exist, but line snapshots are missing, so they cannot flow through line-history-aware diagnostics.",
            "NoPredictionRunContext" => "No prediction run was available to evaluate these persisted props.",
            "NoMatchingPredictionRunForEvent" => "No evaluated prediction run matches the persisted event for these bookmaker props.",
            "NoMatchingPredictionRunForBookmaker" => "No evaluated prediction run includes this bookmaker.",
            "PlayerMappingMissing" => "Matched props have no usable MLB player mapping or alias path for historical feature lookup.",
            "HistoricalFeaturesMissing" => "Matched props have no historical prop_results for the equivalent player and prop type before event date.",
            "InsufficientHistoricalSample" => "Matched props have some history, but not enough same-season/window history for prediction generation.",
            "UnsupportedPropType" => "Matched props use prop types that have no historical MLB result support.",
            "PropTypeEquivalenceMissing" => "Matched players have history, but not for the sportsbook prop type or its current canonical equivalents.",
            "FilteredOutByScopedGeneration" => "Matched props appear prediction-eligible, so the selected run likely did not include this bookmaker/event/market scope.",
            "MetadataMissing" => "Matched props are missing persisted event, market, player, or prop-type metadata required for scoped prediction diagnostics.",
            _ => "Matched props dropped out for a mixed set of reasons."
        };
    }

    private static string BuildTheOddsDropoutRecommendedFix(IReadOnlyCollection<TheOddsBookmakerPredictionDropoutRow> rows)
    {
        if (rows.Count == 0)
        {
            return "Confirm the bookmaker was requested from The Odds API and committed, then rerun this diagnostic.";
        }

        var topReason = rows
            .Where(row => row.DropoutReason != "None")
            .GroupBy(row => row.DropoutReason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();

        return topReason switch
        {
            null => "No fix needed for the selected run context.",
            "NoLineSnapshot" => "Inspect The Odds event ingestion snapshot creation for this bookmaker.",
            "NoPredictionRunContext" => "Generate a prediction run, preferably scoped to the same The Odds event/bookmaker/markets.",
            "NoMatchingPredictionRunForEvent" => "Generate scoped The Odds predictions for this event, or pass the matching runId explicitly.",
            "NoMatchingPredictionRunForBookmaker" => "Generate scoped The Odds predictions with this bookmaker included, or run with ignoreRunContext=true to evaluate readiness only.",
            "PlayerMappingMissing" => "Add or approve MLB player mappings for the affected players before generating predictions.",
            "HistoricalFeaturesMissing" => "Verify MLB result imports cover the event date's prior season/current season history for affected players.",
            "InsufficientHistoricalSample" => "Backfill more MLB result dates or wait for more season history before expecting predictions.",
            "UnsupportedPropType" => "Add MLB result import mappings for the affected prop types before expecting predictions.",
            "PropTypeEquivalenceMissing" => "Review prop-type canonicalization for affected prop types.",
            "FilteredOutByScopedGeneration" => "Regenerate scoped The Odds predictions with this bookmaker included, or run full MLB generation.",
            "MetadataMissing" => "Inspect persisted The Odds metadata for missing event, market, player, or prop identifiers.",
            _ => "Review reason counts and address the largest dropout bucket first."
        };
    }

    private static string BuildTheOddsEventName(TheOddsPersistedPlatformPropRow platformProp)
    {
        if (!string.IsNullOrWhiteSpace(platformProp.AwayTeam) && !string.IsNullOrWhiteSpace(platformProp.HomeTeam))
        {
            return $"{platformProp.AwayTeam} @ {platformProp.HomeTeam}";
        }

        if (!string.IsNullOrWhiteSpace(platformProp.EventId))
        {
            return platformProp.EventId;
        }

        return "Unknown";
    }

    private static string BuildTheOddsEventName(string? homeTeam, string? awayTeam)
    {
        return !string.IsNullOrWhiteSpace(awayTeam) && !string.IsNullOrWhiteSpace(homeTeam)
            ? $"{awayTeam} @ {homeTeam}"
            : "Unknown";
    }

    private static string BuildTheOddsReadinessLineShoppingGroupKey(TheOddsLineShoppingGroup group)
    {
        return string.Join(
            "|",
            group.EventId ?? string.Empty,
            group.Player,
            group.NormalizedPropType,
            group.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
            group.MarketClassification);
    }

    private static string BuildTheOddsReadinessLineShoppingGroupKey(TheOddsPersistedPlatformPropRow row)
    {
        return string.Join(
            "|",
            row.EventId ?? string.Empty,
            row.PlayerName,
            row.PropType,
            row.CurrentLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.MarketClassification);
    }

    private static string DetermineTheOddsPredictionReadinessDropoutStage(
        TheOddsPersistedPlatformPropRow platformProp,
        TheOddsLineSnapshotDiagnosticRow? snapshotDiagnostic,
        TheOddsPlayerRefDiagnosticRow? playerRefs,
        PlayerHistoricalLookupDiagnostic lookup,
        PredictionProbabilityResult? prediction,
        bool lineShoppingGroupFound,
        bool eligibleAsNormalRecommendation,
        IReadOnlySet<string> enabledPropTypes)
    {
        if (snapshotDiagnostic is null || snapshotDiagnostic.Count == 0)
        {
            return "MissingSnapshot";
        }

        if (!lookup.HasMlbMappingForEquivalentPlayers && (playerRefs is null || !playerRefs.HasMlbStatsMapping))
        {
            return "MissingPlayerMapping";
        }

        if (!lookup.HasSupportedPropTypeHistory)
        {
            return "UnsupportedPropType";
        }

        if (lookup.HistoricalGamesAvailable == 0)
        {
            return "MissingHistoricalResults";
        }

        if (lookup.SeasonGamesAvailable == 0)
        {
            return "MissingHistoricalFeatures";
        }

        if (prediction is null)
        {
            return "FilteredOutByPredictionScope";
        }

        if (!lineShoppingGroupFound)
        {
            return "NotIncludedInLineShopping";
        }

        if (eligibleAsNormalRecommendation)
        {
            return "None";
        }

        return "NotEligibleAsNormalRecommendation";
    }

    private static string BuildTheOddsPredictionReadinessDropoutReason(
        string dropoutStage,
        TheOddsPersistedPlatformPropRow platformProp,
        PlayerHistoricalLookupDiagnostic lookup,
        PredictionProbabilityResult? prediction,
        IReadOnlySet<string> enabledPropTypes)
    {
        return dropoutStage switch
        {
            "None" => "Ready for line-shopping.",
            "MissingSnapshot" => "No prop_line_snapshots row exists for this persisted platform prop.",
            "MissingPlayerMapping" => "No MLB Stats mapping or equivalent player alias is available for historical feature lookup.",
            "UnsupportedPropType" => "No imported MLB historical prop_results exist for this prop type or its canonical equivalents.",
            "MissingHistoricalResults" => "Player and prop type are supported, but no historical results exist before the event date.",
            "MissingHistoricalFeatures" => "Historical results exist, but the generator has no same-season feature window before the event date.",
            "FilteredOutByPredictionScope" => "The evaluated prediction run does not contain this prop. It may be stale, outside a scoped event/bookmaker/market run, or generated before this prop was ingested.",
            "PredictionSuppressedByPropTypeFilter" => $"Prediction exists but recommendation is PASS because prop type '{platformProp.PropType}' is not enabled in PredictionGeneration:EnabledPropTypes.",
            "PredictionSuppressedByThreshold" => $"Prediction exists but recommendation is PASS because edge '{prediction?.Edge}' did not cross the configured recommendation threshold.",
            "NotIncludedInLineShopping" => "Prediction exists, but no line-shopping comparison group was built for this event/player/prop/line/classification.",
            "NotEligibleAsNormalRecommendation" => BuildTheOddsNotEligibleReason(platformProp, prediction, enabledPropTypes),
            _ when !IsTheOddsEnabledPredictionPropType(platformProp.PropType, enabledPropTypes) => "Prop has an unknown dropout state and its prop type is currently disabled for recommendations.",
            _ => $"Unknown pipeline dropout state. HistoricalGames={lookup.HistoricalGamesAvailable}; SeasonGames={lookup.SeasonGamesAvailable}; PredictionGenerated={prediction is not null}."
        };
    }

    private static string BuildTheOddsNotEligibleReason(
        TheOddsPersistedPlatformPropRow platformProp,
        PredictionProbabilityResult? prediction,
        IReadOnlySet<string> enabledPropTypes)
    {
        if (prediction?.Recommendation == "PASS" && !IsTheOddsEnabledPredictionPropType(platformProp.PropType, enabledPropTypes))
        {
            return $"Line-shopping row exists but is research-only because prop type '{platformProp.PropType}' is disabled in PredictionGeneration:EnabledPropTypes.";
        }

        if (prediction?.Recommendation == "PASS")
        {
            return $"Line-shopping row exists but is research-only because recommendation is PASS; edge '{prediction.Edge}' did not cross the configured threshold.";
        }

        return "Line-shopping group exists, but market eligibility rules do not allow it as a normal recommendation.";
    }

    private static string BuildTheOddsPredictionReadinessStatus(string dropoutStage)
    {
        return dropoutStage switch
        {
            "None" => "Ready",
            "NotEligibleAsNormalRecommendation" => "ResearchOnly",
            _ => "Dropped"
        };
    }

    private static bool IsTheOddsEnabledPredictionPropType(string propType, IReadOnlySet<string> enabledPropTypes)
    {
        return enabledPropTypes.Count == 0 || enabledPropTypes.Contains(NormalizeTheOddsPropTypeName(propType));
    }

    private static object BuildTheOddsPredictionReadinessSummary(IReadOnlyCollection<TheOddsPredictionReadinessAuditRow> rows)
    {
        var droppedBeforePredictionStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MissingSnapshot",
            "MissingPlayerMapping",
            "UnsupportedPropType",
            "MissingHistoricalResults",
            "MissingHistoricalFeatures",
            "FilteredOutByPredictionScope"
        };

        return new
        {
            totalPlatformProps = rows.Count,
            readyForLineShopping = rows.Count(row => row.ReadinessStatus == "Ready"),
            researchOnly = rows.Count(row => row.ReadinessStatus == "ResearchOnly"),
            withSnapshots = rows.Count(row => row.HasLineSnapshot),
            withPlayerMapping = rows.Count(row => row.HasMlbStatsMapping),
            withSupportedPropType = rows.Count(row => row.CanonicalPropTypeMatched),
            withHistoricalResults = rows.Count(row => row.HistoricalResultCount > 0),
            withHistoricalFeatures = rows.Count(row => row.FeatureAvailable),
            withPredictions = rows.Count(row => row.PredictionGenerated),
            inLineShopping = rows.Count(row => row.LineShoppingGroupFound),
            eligibleAsNormalRecommendation = rows.Count(row => row.EligibleAsNormalRecommendation),
            droppedBeforePrediction = rows.Count(row => droppedBeforePredictionStages.Contains(row.DropoutStage) && !row.PredictionGenerated),
            droppedAfterPrediction = rows.Count(row => row.PredictionGenerated && row.ReadinessStatus == "Dropped"),
            missingPlayerMapping = rows.Count(row => row.DropoutStage == "MissingPlayerMapping"),
            missingHistoricalResults = rows.Count(row => row.DropoutStage == "MissingHistoricalResults"),
            missingHistoricalFeatures = rows.Count(row => row.DropoutStage == "MissingHistoricalFeatures"),
            notEligibleAsNormalRecommendation = rows.Count(row => row.DropoutStage == "NotEligibleAsNormalRecommendation"),
            dropoutStageCounts = CountTheOddsReadinessValues(rows.Select(row => row.DropoutStage), "stage"),
            dropoutReasonCounts = CountTheOddsReadinessValues(rows.Select(row => row.DropoutReason), "reason")
        };
    }

    private static IReadOnlyList<object> BuildTheOddsPredictionReadinessBreakdown(
        IReadOnlyCollection<TheOddsPredictionReadinessAuditRow> rows,
        Func<TheOddsPredictionReadinessAuditRow, string> keySelector,
        string keyName)
    {
        return rows
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var topDropoutReason = group
                    .Where(row => row.DropoutStage != "None")
                    .GroupBy(row => row.DropoutReason, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(reasonGroup => reasonGroup.Count())
                    .ThenBy(reasonGroup => reasonGroup.Key)
                    .Select(reasonGroup => reasonGroup.Key)
                    .FirstOrDefault() ?? "None";

                return new
                {
                    key = group.Key,
                    dimension = keyName,
                    totalPlatformProps = group.Count(),
                    withSnapshots = group.Count(row => row.HasLineSnapshot),
                    withPlayerMapping = group.Count(row => row.HasMlbStatsMapping),
                    withHistoricalFeatures = group.Count(row => row.FeatureAvailable),
                    withPredictions = group.Count(row => row.PredictionGenerated),
                    inLineShopping = group.Count(row => row.LineShoppingGroupFound),
                    eligible = group.Count(row => row.EligibleAsNormalRecommendation),
                    topDropoutReason
                };
            })
            .OrderByDescending(row => row.totalPlatformProps)
            .ThenBy(row => row.key)
            .Cast<object>()
            .ToArray();
    }

    private static IReadOnlyList<object> CountTheOddsReadinessValues(IEnumerable<string> values, string keyName)
    {
        return values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                key = group.Key,
                dimension = keyName,
                count = group.Count()
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .Cast<object>()
            .ToArray();
    }

    private static object BuildTheOddsPredictionReadinessTopIssues(IReadOnlyCollection<TheOddsPredictionReadinessAuditRow> rows)
    {
        return new
        {
            topPlayersMissingMapping = TopTheOddsReadinessRows(rows, row => row.DropoutStage == "MissingPlayerMapping", row => row.PlayerName, "player"),
            topPlayersMissingHistoricalFeatures = TopTheOddsReadinessRows(rows, row => row.DropoutStage is "MissingHistoricalResults" or "MissingHistoricalFeatures", row => row.PlayerName, "player"),
            topUnsupportedPropTypes = TopTheOddsReadinessRows(rows, row => row.DropoutStage == "UnsupportedPropType", row => row.PropType, "propType"),
            topBookmakersDroppingBeforePrediction = TopTheOddsReadinessRows(rows, row => !row.PredictionGenerated, row => row.Bookmaker, "bookmaker"),
            topEventsWithPredictionGaps = TopTheOddsReadinessRows(rows, row => !row.PredictionGenerated, row => row.EventName, "event"),
            topPropsPredictionReadyButNotPredicted = rows
                .Where(row => row.DropoutStage == "FilteredOutByPredictionScope")
                .OrderByDescending(row => row.LatestObservedAt)
                .Take(25)
                .ToArray(),
            topPropsPredictedButNotInLineShopping = rows
                .Where(row => row.DropoutStage == "NotIncludedInLineShopping")
                .OrderByDescending(row => row.LatestObservedAt)
                .Take(25)
                .ToArray()
        };
    }

    private static IReadOnlyList<object> TopTheOddsReadinessRows(
        IReadOnlyCollection<TheOddsPredictionReadinessAuditRow> rows,
        Func<TheOddsPredictionReadinessAuditRow, bool> predicate,
        Func<TheOddsPredictionReadinessAuditRow, string> keySelector,
        string keyName)
    {
        return rows
            .Where(predicate)
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                key = group.Key,
                dimension = keyName,
                count = group.Count(),
                sample = group
                    .OrderByDescending(row => row.LatestObservedAt)
                    .Take(5)
                    .Select(row => new
                    {
                        row.PlatformPropId,
                        row.PlayerName,
                        row.Bookmaker,
                        row.EventName,
                        row.PropType,
                        row.Line,
                        row.DropoutReason
                    })
                    .ToArray()
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .Take(25)
            .Cast<object>()
            .ToArray();
    }

    private static object BuildTheOddsPredictionReadinessDiagnosis(IReadOnlyCollection<TheOddsPredictionReadinessAuditRow> rows)
    {
        var trueBlockerStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MissingSnapshot",
            "MissingPlayerMapping",
            "UnsupportedPropType",
            "MissingHistoricalResults",
            "MissingHistoricalFeatures",
            "FilteredOutByPredictionScope",
            "NotIncludedInLineShopping"
        };
        var topStage = rows
            .Where(row => trueBlockerStages.Contains(row.DropoutStage))
            .GroupBy(row => row.DropoutStage, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? "None";
        var recommendationConfigRows = rows
            .Where(row => row.DropoutStage == "NotEligibleAsNormalRecommendation"
                && (row.DropoutReason.Contains("PredictionGeneration", StringComparison.OrdinalIgnoreCase)
                    || row.DropoutReason.Contains("recommendation is PASS", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return new
        {
            biggestCurrentBlocker = topStage,
            recommendationConfigIssue = recommendationConfigRows.Length > 0,
            recommendationConfigIssueCount = recommendationConfigRows.Length,
            bookmakerCoverageProblem = rows
                .GroupBy(row => row.Bookmaker, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Any(row => !row.PredictionGenerated) && group.Any(row => row.PredictionGenerated)),
            playerMappingProblem = rows.Any(row => row.DropoutStage == "MissingPlayerMapping"),
            historicalCoverageProblem = rows.Any(row => row.DropoutStage is "MissingHistoricalResults" or "MissingHistoricalFeatures"),
            propTypeSupportProblem = rows.Any(row => row.DropoutStage == "UnsupportedPropType"),
            predictionScopeProblem = rows.Any(row => row.DropoutStage == "FilteredOutByPredictionScope"),
            lineShoppingGroupingProblem = rows.Any(row => row.DropoutStage == "NotIncludedInLineShopping"),
            recommendedNextFixes = BuildTheOddsPredictionReadinessRecommendedFixes(rows, topStage)
        };
    }

    private static IReadOnlyList<string> BuildTheOddsPredictionReadinessRecommendedFixes(
        IReadOnlyCollection<TheOddsPredictionReadinessAuditRow> rows,
        string topStage)
    {
        var fixes = new List<string>();
        if (topStage == "MissingHistoricalFeatures" || topStage == "MissingHistoricalResults")
        {
            fixes.Add("Backfill or verify MLB prop_results for the affected players and event dates before regenerating predictions.");
        }

        if (rows.Any(row => row.DropoutStage == "MissingPlayerMapping"))
        {
            fixes.Add("Use the The Odds player mapping candidate/approval workflow for players missing MLB Stats mappings.");
        }

        if (rows.Any(row => row.DropoutStage == "FilteredOutByPredictionScope"))
        {
            fixes.Add("Regenerate scoped The Odds predictions for the affected event/bookmaker/market set, or run full MLB generation.");
        }

        if (rows.Any(row => row.DropoutStage == "UnsupportedPropType"))
        {
            fixes.Add("Add MLB result import support or prop-type equivalence for unsupported prop types before expecting predictions.");
        }

        if (rows.Any(row => row.DropoutStage == "PredictionSuppressedByPropTypeFilter"))
        {
            fixes.Add("Review PredictionGeneration:EnabledPropTypes if these predicted props should be allowed as recommendations.");
        }

        if (rows.Any(row => row.DropoutStage == "PredictionSuppressedByThreshold"))
        {
            fixes.Add("Review configured recommendation thresholds if PASS predictions should become recommendations.");
        }

        if (rows.Any(row => row.DropoutStage == "NotIncludedInLineShopping"))
        {
            fixes.Add("Inspect line-shopping grouping keys for predicted props that do not appear in comparison groups.");
        }

        if (fixes.Count == 0)
        {
            fixes.Add("No major readiness blocker detected for the selected filters.");
        }

        return fixes;
    }

    private static async Task<IReadOnlyList<TheOddsMlbPlayerIdentityRow>> LoadTheOddsMlbPlayerIdentityRowsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var players = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.League != null && player.League.Code == "MLB")
            .Select(player => new
            {
                player.Id,
                player.DisplayName
            })
            .ToListAsync(cancellationToken);
        var playerIds = players.Select(player => player.Id).ToArray();
        var mlbRefs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => playerIds.Contains(reference.PlayerId)
                && reference.Provider == "mlb")
            .Select(reference => new
            {
                reference.PlayerId,
                reference.ProviderPlayerId
            })
            .ToListAsync(cancellationToken);
        var mlbRefsByPlayerId = mlbRefs
            .GroupBy(reference => reference.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(reference => reference.ProviderPlayerId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        return players
            .Select(player => new TheOddsMlbPlayerIdentityRow(
                player.Id,
                player.DisplayName,
                NormalizeTheOddsPlayerName(player.DisplayName),
                mlbRefsByPlayerId.GetValueOrDefault(player.Id) ?? []))
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<string, Guid[]>> LoadTheOddsMlbMappedPlayerIdsByStatsIdAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var refs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == "mlb")
            .Select(reference => new
            {
                reference.PlayerId,
                reference.ProviderPlayerId
            })
            .ToListAsync(cancellationToken);

        return refs
            .GroupBy(reference => reference.ProviderPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(reference => reference.PlayerId).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<TheOddsMlbPropResultRow>> LoadTheOddsMlbPropResultRowsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.League != null && result.League.Code == "MLB")
            .Select(result => new TheOddsMlbPropResultRow(
                result.PlayerId,
                result.PropTypeId,
                result.PropType == null ? "Unknown" : result.PropType.Name,
                result.EventDate,
                result.ActualResultValue))
            .ToListAsync(cancellationToken);
    }

    private static int CountTheOddsResults(
        IReadOnlyCollection<TheOddsMlbPropResultRow> results,
        IReadOnlyCollection<Guid> playerIds,
        IReadOnlyCollection<Guid> propTypeIds)
    {
        if (playerIds.Count == 0 || propTypeIds.Count == 0)
        {
            return 0;
        }

        return results.Count(result => playerIds.Contains(result.PlayerId)
            && propTypeIds.Contains(result.PropTypeId));
    }

    private static string BuildTheOddsHistoricalFeatureGapReason(PlayerHistoricalLookupDiagnostic lookup)
    {
        if (lookup.HistoricalGamesAvailable == 0)
        {
            return "No historical results were found for the equivalent player and prop type before the event date.";
        }

        if (lookup.SeasonGamesAvailable == 0)
        {
            return "Historical results exist, but none are in the same season before the event date.";
        }

        return "Historical feature unavailable for an unknown reason.";
    }

    private static string ClassifyTheOddsHistoricalFeatureGap(
        TheOddsPersistedPlatformPropRow platformProp,
        int directPlayerResultCount,
        int aliasLinkedPlayerResultCount,
        int sameNamePlayerResultCount,
        int mlbStatsMappedPlayerResultCount,
        int equivalentPropTypeResultCount,
        int pitcherStrikeoutsResultCount,
        int genericStrikeoutsResultCount,
        int resultsBeforeEventDate,
        int resultsAfterEventDate)
    {
        if (resultsBeforeEventDate > 0 && directPlayerResultCount == 0)
        {
            return "ResultsExistUnderDifferentPlayerId";
        }

        if (aliasLinkedPlayerResultCount > 0)
        {
            return "AliasResolutionGap";
        }

        if (sameNamePlayerResultCount > 0 || mlbStatsMappedPlayerResultCount > 0)
        {
            return "ResultsExistUnderDifferentPlayerId";
        }

        if (resultsBeforeEventDate == 0 && resultsAfterEventDate > 0)
        {
            return "ResultsExistOnlyAfterEventDate";
        }

        if (equivalentPropTypeResultCount > 0)
        {
            return "ResultsExistUnderEquivalentPropType";
        }

        if (NormalizeTheOddsPropTypeName(platformProp.PropType) == "PITCHERSTRIKEOUTS"
            && pitcherStrikeoutsResultCount == 0
            && genericStrikeoutsResultCount > 0)
        {
            return "PropTypeEquivalenceGap";
        }

        if (NormalizeTheOddsPropTypeName(platformProp.PropType) == "PITCHERSTRIKEOUTS"
            && pitcherStrikeoutsResultCount == 0)
        {
            return "PitcherStrikeoutsImportGap";
        }

        if (resultsBeforeEventDate == 0 && resultsAfterEventDate == 0)
        {
            return "TrulyNoHistoricalResults";
        }

        return "Unknown";
    }

    private static object BuildTheOddsHistoricalFeatureGapSummary(IReadOnlyCollection<TheOddsHistoricalFeatureGapAuditRow> rows)
    {
        return new
        {
            totalHistoricalFeatureGaps = rows.Count,
            affectedPlayers = rows.Select(row => row.PlayerId).Distinct().Count(),
            affectedPlatformProps = rows.Count,
            pitcherStrikeoutsGaps = rows.Count(row => NormalizeTheOddsPropTypeName(row.PropType) == "PITCHERSTRIKEOUTS"),
            batterPropGaps = rows.Count(row => NormalizeTheOddsPropTypeName(row.PropType) != "PITCHERSTRIKEOUTS"),
            recoverableByAliasResolution = rows.Count(row => row.LikelyRootCause is "AliasResolutionGap" or "ResultsExistUnderDifferentPlayerId"),
            recoverableByPropTypeEquivalence = rows.Count(row => row.LikelyRootCause is "PropTypeEquivalenceGap" or "ResultsExistUnderEquivalentPropType"),
            recoverableByImportBackfill = rows.Count(row => row.LikelyRootCause is "PitcherStrikeoutsImportGap" or "ResultsExistOnlyAfterEventDate"),
            unrecoverableNoHistory = rows.Count(row => row.LikelyRootCause == "TrulyNoHistoricalResults")
        };
    }

    private static IReadOnlyList<object> BuildTheOddsHistoricalFeatureGapBreakdown(
        IReadOnlyCollection<TheOddsHistoricalFeatureGapAuditRow> rows,
        Func<TheOddsHistoricalFeatureGapAuditRow, string> keySelector,
        string keyName)
    {
        return rows
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                key = group.Key,
                dimension = keyName,
                platformPropCount = group.Count(),
                affectedPlayers = group.Select(row => row.PlayerId).Distinct().Count(),
                pitcherStrikeoutsGaps = group.Count(row => NormalizeTheOddsPropTypeName(row.PropType) == "PITCHERSTRIKEOUTS"),
                topRootCause = group
                    .GroupBy(row => row.LikelyRootCause, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(rootCauseGroup => rootCauseGroup.Count())
                    .ThenBy(rootCauseGroup => rootCauseGroup.Key)
                    .Select(rootCauseGroup => rootCauseGroup.Key)
                    .FirstOrDefault() ?? "Unknown"
            })
            .OrderByDescending(row => row.platformPropCount)
            .ThenBy(row => row.key)
            .Cast<object>()
            .ToArray();
    }

    private static object BuildTheOddsHistoricalFeatureGapRecommendedFixes(IReadOnlyCollection<TheOddsHistoricalFeatureGapAuditRow> rows)
    {
        var rootCauseCounts = rows
            .GroupBy(row => row.LikelyRootCause, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var topRootCause = rootCauseCounts
            .OrderByDescending(row => row.Value)
            .Select(row => row.Key)
            .FirstOrDefault() ?? "None";

        return new
        {
            specificNextFix = topRootCause switch
            {
                "PitcherStrikeoutsImportGap" => "Audit MLB result import for pitcher strikeout stat creation and backfill affected dates.",
                "PropTypeEquivalenceGap" => "Add or verify prop-type equivalence between Pitcher Strikeouts and imported Strikeouts results.",
                "ResultsExistUnderDifferentPlayerId" or "AliasResolutionGap" => "Update player alias resolution or external refs so feature lookup reaches the historical result player row.",
                "ResultsExistOnlyAfterEventDate" => "Treat as event-date filtering or genuine new-player sample issue; verify event date and backfill earlier dates if expected.",
                "TrulyNoHistoricalResults" => "Mark these as legitimate no-history players until MLB results accumulate.",
                _ => "Inspect sample rows for mixed or unknown root causes."
            },
            shouldBackfill = rootCauseCounts.Keys.Any(key => key is "PitcherStrikeoutsImportGap" or "ResultsExistOnlyAfterEventDate"),
            shouldAddPropTypeEquivalence = rootCauseCounts.Keys.Any(key => key is "PropTypeEquivalenceGap" or "ResultsExistUnderEquivalentPropType"),
            shouldUpdateFeatureLookup = rootCauseCounts.Keys.Any(key => key is "AliasResolutionGap" or "EventDateFilteringGap"),
            shouldMarkAsLegitimateNoHistory = rows.Count > 0 && rows.All(row => row.LikelyRootCause == "TrulyNoHistoricalResults"),
            rootCauseCounts
        };
    }

    private static async Task<IReadOnlyList<TheOddsMlbPlayerCandidateSourceRow>> LoadTheOddsMlbCandidatePlayersAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<string> sourcePlayerNames,
        CancellationToken cancellationToken)
    {
        var sourceNameKeys = sourcePlayerNames
            .SelectMany(BuildTheOddsPlayerNameKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sourceNameKeys.Count == 0)
        {
            return [];
        }

        var players = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.League != null && player.League.Code == "MLB")
            .Select(player => new
            {
                PlayerId = player.Id,
                player.DisplayName,
                LeagueCode = player.League == null ? "UNKNOWN" : player.League.Code
            })
            .ToListAsync(cancellationToken);

        return players
            .Where(player => BuildTheOddsPlayerNameKeys(player.DisplayName).Any(sourceNameKeys.Contains))
            .Select(player => new TheOddsMlbPlayerCandidateSourceRow(
                player.PlayerId,
                player.DisplayName,
                player.LeagueCode,
                BuildTheOddsPlayerNameKeys(player.DisplayName)))
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<TheOddsExternalRefSummary>>> LoadTheOddsExternalRefsByPlayerIdAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        var playerIdArray = playerIds.Distinct().ToArray();
        if (playerIdArray.Length == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<TheOddsExternalRefSummary>>();
        }

        var rows = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => playerIdArray.Contains(reference.PlayerId))
            .Select(reference => new
            {
                reference.PlayerId,
                reference.Provider,
                reference.ProviderPlayerId,
                reference.ProviderDisplayName
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(reference => reference.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TheOddsExternalRefSummary>)group
                    .OrderBy(reference => reference.Provider)
                    .ThenBy(reference => reference.ProviderPlayerId)
                    .Select(reference => new TheOddsExternalRefSummary(
                        reference.Provider,
                        reference.ProviderPlayerId,
                        reference.ProviderDisplayName))
                    .ToArray());
    }

    private static async Task<IReadOnlyDictionary<Guid, int>> LoadTheOddsPlayerHistoricalResultCountsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        var playerIdArray = playerIds.Distinct().ToArray();
        if (playerIdArray.Length == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => playerIdArray.Contains(result.PlayerId)
                && result.League != null
                && result.League.Code == "MLB")
            .GroupBy(result => result.PlayerId)
            .Select(group => new
            {
                PlayerId = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.PlayerId, row => row.Count);
    }

    private static async Task<IReadOnlyDictionary<TheOddsPlayerPropTypeResultCountKey, int>> LoadTheOddsPlayerPropTypeResultCountsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        var playerIdArray = playerIds.Distinct().ToArray();
        if (playerIdArray.Length == 0)
        {
            return new Dictionary<TheOddsPlayerPropTypeResultCountKey, int>();
        }

        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => playerIdArray.Contains(result.PlayerId)
                && result.League != null
                && result.League.Code == "MLB")
            .GroupBy(result => new { result.PlayerId, result.PropTypeId })
            .Select(group => new
            {
                group.Key.PlayerId,
                group.Key.PropTypeId,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => new TheOddsPlayerPropTypeResultCountKey(row.PlayerId, row.PropTypeId),
            row => row.Count);
    }

    private static IReadOnlyList<TheOddsPlayerMappingRecoveryPreviewRow> BuildTheOddsPlayerMappingRecoveryPreviewRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> unmappedPlatformProps,
        IReadOnlyDictionary<Guid, TheOddsLineSnapshotDiagnosticRow> snapshotDiagnostics,
        IReadOnlyDictionary<Guid, PlayerHistoricalLookupDiagnostic> sourceReadinessByPlatformPropId,
        IReadOnlyDictionary<Guid, IReadOnlyList<TheOddsExternalRefSummary>> externalRefsByPlayerId,
        IReadOnlyDictionary<Guid, int> sourceHistoryCounts,
        IReadOnlyCollection<TheOddsMlbPlayerCandidateSourceRow> candidatePlayers,
        IReadOnlyDictionary<Guid, int> candidateHistoryCounts,
        IReadOnlyDictionary<TheOddsPlayerPropTypeResultCountKey, int> candidatePropTypeResultCounts)
    {
        return unmappedPlatformProps
            .GroupBy(row => new
            {
                row.PlayerId,
                row.PlayerName,
                Bookmaker = row.PlatformName,
                row.EventId
            })
            .Select(group =>
            {
                var first = group.OrderByDescending(row => row.ObservedAt).First();
                var sourceRefs = externalRefsByPlayerId.TryGetValue(first.PlayerId, out var refs)
                    ? refs
                    : [];
                var sourceKeys = BuildTheOddsPlayerNameKeys(first.PlayerName);
                var sourceReadiness = group
                    .Select(row => sourceReadinessByPlatformPropId[row.PlatformPropGuid])
                    .ToArray();
                var propTypeIdsAffected = group.Select(row => row.PropTypeId).Distinct().ToArray();
                var candidateRows = candidatePlayers
                    .Where(candidate => candidate.PlayerId != first.PlayerId
                        && candidate.NameKeys.Any(sourceKeys.Contains))
                    .Select(candidate => BuildTheOddsPlayerMappingCandidate(
                        first.PlayerName,
                        sourceKeys,
                        propTypeIdsAffected,
                        candidate,
                        externalRefsByPlayerId.TryGetValue(candidate.PlayerId, out var candidateRefs) ? candidateRefs : [],
                        candidateHistoryCounts.GetValueOrDefault(candidate.PlayerId),
                        candidatePropTypeResultCounts))
                    .OrderByDescending(candidate => candidate.ConfidenceScore)
                    .ThenByDescending(candidate => candidate.HistoricalSampleSize)
                    .ThenBy(candidate => candidate.CandidatePlayerName)
                    .ToArray();
                var recommendation = DetermineTheOddsPlayerMappingRecommendation(candidateRows);

                return new TheOddsPlayerMappingRecoveryPreviewRow(
                    first.PlayerId,
                    first.PlayerName,
                    first.PlatformName,
                    first.EventId,
                    BuildTheOddsEventName(first),
                    group.Select(row => row.PropType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    group.Count(),
                    group.All(row => snapshotDiagnostics.TryGetValue(row.PlatformPropGuid, out var snapshot) && snapshot.Count > 0),
                    sourceRefs,
                    sourceRefs.Any(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase)),
                    sourceReadiness.Any(readiness => readiness.HistoricalGamesAvailable > 0),
                    Math.Max(
                        sourceReadiness.Select(readiness => readiness.HistoricalGamesAvailable).DefaultIfEmpty(0).Max(),
                        sourceHistoryCounts.GetValueOrDefault(first.PlayerId)),
                    candidateRows,
                    recommendation);
            })
            .OrderBy(row => row.Recommendation == "AutoRecoverable" ? 0 : row.Recommendation == "ManualReview" ? 1 : 2)
            .ThenByDescending(row => row.PlatformPropCount)
            .ThenBy(row => row.SourcePlayerName)
            .ToArray();
    }

    private static TheOddsPlayerMappingCandidateRow BuildTheOddsPlayerMappingCandidate(
        string sourcePlayerName,
        IReadOnlySet<string> sourceKeys,
        IReadOnlyCollection<Guid> propTypeIdsAffected,
        TheOddsMlbPlayerCandidateSourceRow candidate,
        IReadOnlyList<TheOddsExternalRefSummary> candidateExternalRefs,
        int historicalSampleSize,
        IReadOnlyDictionary<TheOddsPlayerPropTypeResultCountKey, int> candidatePropTypeResultCounts)
    {
        var matchingKeys = candidate.NameKeys.Where(sourceKeys.Contains).ToArray();
        var hasMlbStatsMapping = candidateExternalRefs.Any(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase));
        var mlbStatsId = candidateExternalRefs
            .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.ProviderPlayerId)
            .FirstOrDefault();
        var matchingPropResultCount = propTypeIdsAffected
            .Sum(propTypeId => candidatePropTypeResultCounts.GetValueOrDefault(new TheOddsPlayerPropTypeResultCountKey(candidate.PlayerId, propTypeId)));
        var matchReasons = BuildTheOddsPlayerMappingMatchReasons(sourcePlayerName, candidate.DisplayName, matchingKeys);
        var riskFlags = BuildTheOddsPlayerMappingRiskFlags(
            sourcePlayerName,
            candidate,
            hasMlbStatsMapping,
            historicalSampleSize,
            matchingPropResultCount);
        var confidenceScore = CalculateTheOddsPlayerMappingConfidence(
            sourcePlayerName,
            candidate.DisplayName,
            matchingKeys,
            hasMlbStatsMapping,
            historicalSampleSize,
            riskFlags);

        return new TheOddsPlayerMappingCandidateRow(
            candidate.PlayerId,
            candidate.DisplayName,
            candidateExternalRefs,
            hasMlbStatsMapping,
            mlbStatsId,
            historicalSampleSize,
            matchingPropResultCount,
            confidenceScore,
            matchReasons,
            riskFlags);
    }

    private static IReadOnlyList<string> BuildTheOddsPlayerMappingMatchReasons(
        string sourcePlayerName,
        string candidatePlayerName,
        IReadOnlyCollection<string> matchingKeys)
    {
        var reasons = new List<string>();
        if (string.Equals(sourcePlayerName, candidatePlayerName, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("ExactDisplayName");
        }

        if (matchingKeys.Contains(NormalizeTheOddsPlayerName(sourcePlayerName), StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("ExactNormalizedName");
        }

        if (matchingKeys.Contains(NormalizeTheOddsInitialsName(sourcePlayerName), StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("InitialsFullNameNormalization");
        }

        if (reasons.Count == 0 && matchingKeys.Count > 0)
        {
            reasons.Add("PunctuationInsensitiveName");
        }

        return reasons;
    }

    private static IReadOnlyList<string> BuildTheOddsPlayerMappingRiskFlags(
        string sourcePlayerName,
        TheOddsMlbPlayerCandidateSourceRow candidate,
        bool hasMlbStatsMapping,
        int historicalSampleSize,
        int matchingPropResultCount)
    {
        var flags = new List<string>();
        if (!hasMlbStatsMapping)
        {
            flags.Add("NoMlbStatsMapping");
        }

        if (!candidate.NameKeys.Contains(NormalizeTheOddsPlayerName(sourcePlayerName)))
        {
            flags.Add("NameMismatch");
        }

        if (historicalSampleSize < 25)
        {
            flags.Add("LowHistoricalSample");
        }

        if (!string.Equals(candidate.LeagueCode, "MLB", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("DifferentLeague");
        }

        if (HasTheOddsAmbiguousInitials(sourcePlayerName))
        {
            flags.Add("AmbiguousInitials");
        }

        if (matchingPropResultCount == 0)
        {
            flags.Add("NoHistoricalResults");
        }

        return flags;
    }

    private static decimal CalculateTheOddsPlayerMappingConfidence(
        string sourcePlayerName,
        string candidatePlayerName,
        IReadOnlyCollection<string> matchingKeys,
        bool hasMlbStatsMapping,
        int historicalSampleSize,
        IReadOnlyCollection<string> riskFlags)
    {
        var confidence = string.Equals(sourcePlayerName, candidatePlayerName, StringComparison.OrdinalIgnoreCase)
            ? 1.0m
            : matchingKeys.Contains(NormalizeTheOddsPlayerName(sourcePlayerName), StringComparer.OrdinalIgnoreCase)
                ? 0.98m
                : matchingKeys.Count > 0
                    ? 0.90m
                    : 0.0m;

        if (hasMlbStatsMapping)
        {
            confidence += 0.01m;
        }

        if (historicalSampleSize >= 50)
        {
            confidence += 0.01m;
        }

        if (riskFlags.Contains("AmbiguousInitials"))
        {
            confidence -= 0.05m;
        }

        if (riskFlags.Contains("NoHistoricalResults"))
        {
            confidence -= 0.10m;
        }

        return Math.Clamp(Math.Round(confidence, 4), 0m, 1m);
    }

    private static string DetermineTheOddsPlayerMappingRecommendation(IReadOnlyCollection<TheOddsPlayerMappingCandidateRow> candidates)
    {
        if (candidates.Count == 0)
        {
            return "NotRecoverable";
        }

        var highConfidenceCandidates = candidates
            .Where(candidate => candidate.ConfidenceScore >= 0.98m
                && (candidate.HasMlbStatsMapping || candidate.HistoricalSampleSize > 0)
                && !candidate.RiskFlags.Contains("NameMismatch")
                && !candidate.RiskFlags.Contains("DifferentLeague"))
            .ToArray();

        if (highConfidenceCandidates.Length == 1 && candidates.Count == 1 && !highConfidenceCandidates[0].RiskFlags.Contains("AmbiguousInitials"))
        {
            return "AutoRecoverable";
        }

        return "ManualReview";
    }

    private static object BuildTheOddsPlayerMappingRecoverySummary(
        IReadOnlyCollection<TheOddsPlayerMappingRecoveryPreviewRow> rows)
    {
        return new
        {
            totalUnmappedTheOddsPlayers = rows.Count,
            autoRecoverableCount = rows.Count(row => row.Recommendation == "AutoRecoverable"),
            manualReviewCount = rows.Count(row => row.Recommendation == "ManualReview"),
            notRecoverableCount = rows.Count(row => row.Recommendation == "NotRecoverable"),
            affectedBookmakers = rows
                .GroupBy(row => row.SourceBookmaker, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { bookmaker = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.bookmaker)
                .ToArray(),
            affectedPropTypes = rows
                .SelectMany(row => row.PropTypesAffected)
                .GroupBy(propType => propType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { propType = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.propType)
                .ToArray(),
            topAffectedPlayers = rows
                .OrderByDescending(row => row.PlatformPropCount)
                .ThenBy(row => row.SourcePlayerName)
                .Take(25)
                .Select(row => new
                {
                    playerId = row.SourcePlayerId,
                    playerName = row.SourcePlayerName,
                    row.SourceBookmaker,
                    row.PlatformPropCount,
                    row.Recommendation
                })
                .ToArray(),
            recommendedNextStep = BuildTheOddsPlayerMappingRecoveryRecommendedNextStep(rows)
        };
    }

    private static object BuildTheOddsPlayerMappingRecoveryImpactPreview(
        IReadOnlyCollection<TheOddsPlayerMappingRecoveryPreviewRow> rows)
    {
        var recoverableRows = rows.Where(row => row.Recommendation is "AutoRecoverable" or "ManualReview").ToArray();

        return new
        {
            platformPropsRecoverable = recoverableRows.Sum(row => row.PlatformPropCount),
            estimatedAdditionalPredictionsIfRecovered = recoverableRows.Sum(row => row.PlatformPropCount),
            affectedBookmakerCoverage = rows
                .GroupBy(row => row.SourceBookmaker, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    bookmaker = group.Key,
                    unmappedPlayers = group.Count(),
                    recoverablePlayers = group.Count(row => row.Recommendation is "AutoRecoverable" or "ManualReview"),
                    recoverablePlatformProps = group
                        .Where(row => row.Recommendation is "AutoRecoverable" or "ManualReview")
                        .Sum(row => row.PlatformPropCount)
                })
                .OrderByDescending(row => row.recoverablePlatformProps)
                .ThenBy(row => row.bookmaker)
                .ToArray()
        };
    }

    private static string BuildTheOddsPlayerMappingRecoveryRecommendedNextStep(
        IReadOnlyCollection<TheOddsPlayerMappingRecoveryPreviewRow> rows)
    {
        if (rows.Count == 0)
        {
            return "No unmapped The Odds API MLB players matched the filters.";
        }

        if (rows.Any(row => row.Recommendation == "AutoRecoverable"))
        {
            return "Review AutoRecoverable rows first; they are candidates for a future explicit approval endpoint, but this endpoint remains read-only.";
        }

        if (rows.Any(row => row.Recommendation == "ManualReview"))
        {
            return "Manual review is required because candidates are ambiguous, initials-based, or have limited historical evidence.";
        }

        return "No viable mapped player candidates were found. Add MLB Stats mappings through the existing player mapping workflow first.";
    }

    private static int[] BuildTheOddsCandidateSearchSeasons(IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps)
    {
        var years = platformProps
            .Select(row => row.EventStartTime?.UtcDateTime.Year)
            .Where(year => year.HasValue)
            .Select(year => year!.Value)
            .Distinct()
            .ToList();

        if (years.Count == 0)
        {
            years.Add(DateTimeOffset.UtcNow.Year);
        }

        return years
            .SelectMany(year => new[] { year, year - 1 })
            .Where(year => year >= 2020)
            .Distinct()
            .OrderByDescending(year => year)
            .ToArray();
    }

    private static async Task<IReadOnlyList<MlbPlayerRecord>> LoadTheOddsMlbProviderPlayersAsync(
        IMlbResultsProvider mlbResultsProvider,
        IReadOnlyCollection<int> seasons,
        CancellationToken cancellationToken)
    {
        var playersById = new Dictionary<string, MlbPlayerRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var season in seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var players = await mlbResultsProvider.GetPlayersAsync(season, cancellationToken);
            foreach (var player in players)
            {
                playersById.TryAdd(player.MlbPlayerId, player);
            }
        }

        return playersById.Values.ToArray();
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<TheOddsMlbProviderRefConflictRow>>> LoadTheOddsMlbProviderRefConflictsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<MlbPlayerRecord> providerPlayers,
        CancellationToken cancellationToken)
    {
        var mlbPlayerIds = providerPlayers
            .Select(player => player.MlbPlayerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mlbPlayerIds.Length == 0)
        {
            return new Dictionary<string, IReadOnlyList<TheOddsMlbProviderRefConflictRow>>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == "mlb"
                && mlbPlayerIds.Contains(reference.ProviderPlayerId))
            .Select(reference => new
            {
                reference.ProviderPlayerId,
                reference.PlayerId,
                PlayerName = reference.Player == null ? reference.ProviderDisplayName : reference.Player.DisplayName,
                reference.ProviderDisplayName
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ProviderPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TheOddsMlbProviderRefConflictRow>)group
                    .OrderBy(row => row.PlayerName)
                    .Select(row => new TheOddsMlbProviderRefConflictRow(
                        row.PlayerId,
                        row.PlayerName,
                        row.ProviderPlayerId,
                        row.ProviderDisplayName))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<TheOddsMlbAliasLinkPreviewRow>> BuildTheOddsMlbAliasLinkPreviewRowsAsync(
        ApplicationDbContext dbContext,
        string? playerName,
        Guid? sourcePlayerId,
        CancellationToken cancellationToken)
    {
        var normalizedPlayerNameFilter = string.IsNullOrWhiteSpace(playerName)
            ? null
            : NormalizeTheOddsPlayerName(playerName);
        var platformPropDataRows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
        var platformProps = ParseTheOddsPlatformProps(platformPropDataRows)
            .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
            .Where(row => sourcePlayerId is null || row.PlayerId == sourcePlayerId.Value)
            .Where(row => normalizedPlayerNameFilter is null
                || BuildTheOddsPlayerNameKeys(row.PlayerName).Contains(normalizedPlayerNameFilter))
            .ToArray();
        var sourcePlayerIds = platformProps.Select(row => row.PlayerId).Distinct().ToArray();
        var sourceExternalRefs = await LoadTheOddsExternalRefsByPlayerIdAsync(dbContext, sourcePlayerIds, cancellationToken);
        var aliasRefs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => sourcePlayerIds.Contains(reference.PlayerId)
                && reference.Provider == MlbAliasProvider)
            .Select(reference => new
            {
                reference.PlayerId,
                reference.ProviderPlayerId
            })
            .ToListAsync(cancellationToken);
        var aliasTargetsBySourcePlayerId = aliasRefs
            .Select(reference => new
            {
                reference.PlayerId,
                TargetPlayerId = TryParseTheOddsMlbAliasTargetPlayerId(reference.ProviderPlayerId, out var targetPlayerId)
                    ? targetPlayerId
                    : (Guid?)null
            })
            .Where(reference => reference.TargetPlayerId.HasValue)
            .GroupBy(reference => reference.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(reference => reference.TargetPlayerId!.Value).Distinct().ToArray());
        var targetRows = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == "mlb")
            .Where(reference => reference.Player != null && reference.Player.League != null)
            .Select(reference => new
            {
                TargetCanonicalPlayerId = reference.PlayerId,
                TargetCanonicalPlayerName = reference.Player!.DisplayName,
                TargetLeagueCode = reference.Player.League!.Code,
                TargetMlbStatsId = reference.ProviderPlayerId,
                reference.ProviderDisplayName
            })
            .ToListAsync(cancellationToken);

        var targetPlayers = targetRows
            .GroupBy(row => row.TargetCanonicalPlayerId)
            .Select(group =>
            {
                var first = group.First();
                return new
                {
                    first.TargetCanonicalPlayerId,
                    first.TargetCanonicalPlayerName,
                    first.TargetLeagueCode,
                    MlbStatsIds = group
                        .Select(row => row.TargetMlbStatsId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    NameKeys = BuildTheOddsPlayerNameKeys(first.TargetCanonicalPlayerName)
                        .Concat(group.SelectMany(row => BuildTheOddsPlayerNameKeys(row.ProviderDisplayName)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                };
            })
            .Where(row => string.Equals(row.TargetLeagueCode, "MLB", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return platformProps
            .GroupBy(row => row.PlayerId)
            .Select(group =>
            {
                var first = group.OrderByDescending(row => row.ObservedAt).First();
                var sourceNames = group
                    .Select(row => row.PlayerName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var variants = sourceNames
                    .SelectMany(BuildTheOddsPlayerNameVariants)
                    .GroupBy(variant => variant.NormalizedName, StringComparer.OrdinalIgnoreCase)
                    .Select(variantGroup => variantGroup.First())
                    .ToArray();
                var sourceKeys = variants
                    .Select(variant => variant.NormalizedName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var currentExternalRefs = sourceExternalRefs.TryGetValue(first.PlayerId, out var refs)
                    ? refs
                    : [];
                var sourceMlbRefs = currentExternalRefs
                    .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                aliasTargetsBySourcePlayerId.TryGetValue(first.PlayerId, out var existingAliasTargets);
                existingAliasTargets ??= [];
                var sourceNameRequiresManualReview = sourceNames.Any(IsTheOddsMaxMuncyName);
                var candidates = targetPlayers
                    .Where(target => target.TargetCanonicalPlayerId != first.PlayerId)
                    .Where(target => target.NameKeys.Any(sourceKeys.Contains))
                    .Select(target =>
                    {
                        var riskFlags = new List<string>();
                        if (sourceNameRequiresManualReview)
                        {
                            riskFlags.Add("MaxMuncyManualReview");
                        }

                        if (sourceMlbRefs.Length > 0)
                        {
                            riskFlags.Add("SourceAlreadyHasMlbMapping");
                        }

                        if (existingAliasTargets.Length > 0 && !existingAliasTargets.Contains(target.TargetCanonicalPlayerId))
                        {
                            riskFlags.Add("ExistingAliasToDifferentTarget");
                        }

                        if (!sourceNames.Any(sourceName => target.NameKeys.Contains(NormalizeTheOddsPlayerName(sourceName))))
                        {
                            riskFlags.Add("NormalizedAliasMatch");
                        }

                        return new TheOddsMlbAliasLinkCandidateRow(
                            target.TargetCanonicalPlayerId,
                            target.TargetCanonicalPlayerName,
                            target.MlbStatsIds.FirstOrDefault(),
                            target.MlbStatsIds,
                            target.NameKeys.Intersect(sourceKeys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                            riskFlags,
                            existingAliasTargets.Contains(target.TargetCanonicalPlayerId));
                    })
                    .OrderBy(candidate => candidate.TargetCanonicalPlayerName)
                    .ThenBy(candidate => candidate.TargetMlbStatsId)
                    .ToArray();

                if (candidates.Length > 1)
                {
                    candidates = candidates
                        .Select(candidate => candidate with
                        {
                            RiskFlags = candidate.RiskFlags.Contains("MultipleCandidates")
                                ? candidate.RiskFlags
                                : candidate.RiskFlags.Concat(["MultipleCandidates"]).ToArray()
                        })
                        .ToArray();
                }

                var alreadyLinked = candidates.Any(candidate => candidate.AlreadyLinked);
                var recommendation = DetermineTheOddsMlbAliasLinkRecommendation(candidates, sourceMlbRefs.Length > 0, sourceNameRequiresManualReview, alreadyLinked);

                return new TheOddsMlbAliasLinkPreviewRow(
                    first.PlayerId,
                    first.PlayerName,
                    first.PlayerName,
                    variants.FirstOrDefault()?.NormalizedName ?? NormalizeTheOddsPlayerName(first.PlayerName),
                    variants,
                    group.Select(row => row.PlatformName).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    group
                        .Select(row => row.EventId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => id!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    group.Select(row => row.PropType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    group.Count(),
                    currentExternalRefs,
                    candidates.Length,
                    candidates,
                    alreadyLinked,
                    candidates.Length == 1 && sourceMlbRefs.Length == 0,
                    recommendation);
            })
            .Where(row => row.CandidatesFound > 0 || sourcePlayerId.HasValue || !string.IsNullOrWhiteSpace(playerName))
            .OrderBy(row => row.Recommendation == "SafeAliasLink" ? 0 : row.Recommendation == "ManualReview" ? 1 : 2)
            .ThenByDescending(row => row.AffectedPlatformProps)
            .ThenBy(row => row.SourcePlayerName)
            .ToArray();
    }

    private static string DetermineTheOddsMlbAliasLinkRecommendation(
        IReadOnlyCollection<TheOddsMlbAliasLinkCandidateRow> candidates,
        bool sourceHasMlbRef,
        bool sourceNameRequiresManualReview,
        bool alreadyLinked)
    {
        if (alreadyLinked)
        {
            return "AlreadyLinked";
        }

        if (candidates.Count == 0 || sourceHasMlbRef)
        {
            return "NotLinkable";
        }

        if (candidates.Count == 1
            && !sourceNameRequiresManualReview
            && candidates.All(candidate => candidate.RiskFlags.Count == 0
                || candidate.RiskFlags.All(flag => flag == "NormalizedAliasMatch")))
        {
            return "SafeAliasLink";
        }

        return "ManualReview";
    }

    private static bool IsTheOddsMaxMuncyName(string playerName)
    {
        return NormalizeTheOddsPlayerName(playerName) == "MAXMUNCY";
    }

    private static bool IsTheOddsAutoAliasExcludedName(string playerName)
    {
        var normalizedName = NormalizeTheOddsPlayerName(playerName);
        return normalizedName is "MAXMUNCY" or "LUISGARCIAJR" or "LUISGARCIA";
    }

    private static string BuildTheOddsMlbAliasProviderPlayerId(Guid sourcePlayerId, Guid targetCanonicalPlayerId)
    {
        return $"source:{sourcePlayerId:N}:target:{targetCanonicalPlayerId:N}";
    }

    private static bool TryParseTheOddsMlbAliasTargetPlayerId(string providerPlayerId, out Guid targetCanonicalPlayerId)
    {
        targetCanonicalPlayerId = Guid.Empty;
        var markerIndex = providerPlayerId.IndexOf(":target:", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var targetValue = providerPlayerId[(markerIndex + ":target:".Length)..];
        return Guid.TryParseExact(targetValue, "N", out targetCanonicalPlayerId)
            || Guid.TryParse(targetValue, out targetCanonicalPlayerId);
    }

    private static IReadOnlyList<TheOddsMlbManualMappingCandidateRow> BuildTheOddsMlbManualMappingCandidateRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> unmappedPlatformProps,
        IReadOnlyDictionary<Guid, TheOddsLineSnapshotDiagnosticRow> snapshotDiagnostics,
        IReadOnlyCollection<MlbPlayerRecord> providerPlayers,
        IReadOnlyDictionary<Guid, TheOddsPlayerRefDiagnosticRow> playerRefDiagnostics,
        IReadOnlyDictionary<string, IReadOnlyList<TheOddsMlbProviderRefConflictRow>> providerRefConflicts)
    {
        var providerPlayersByNameKey = providerPlayers
            .Select(player => new
            {
                Player = player,
                NameKeys = BuildTheOddsPlayerNameKeys(player.DisplayName)
                    .Concat(BuildTheOddsPlayerNameKeys($"{player.FirstName} {player.LastName}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            })
            .SelectMany(item => item.NameKeys.Select(key => new { Key = key, item.Player, item.NameKeys }))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        return unmappedPlatformProps
            .GroupBy(row => new
            {
                row.PlayerId,
                row.PlayerName,
                Bookmaker = row.PlatformName,
                row.EventId
            })
            .Select(group =>
            {
                var first = group.OrderByDescending(row => row.ObservedAt).First();
                var sourceNameVariants = BuildTheOddsPlayerNameVariants(first.PlayerName);
                var sourceKeys = sourceNameVariants
                    .Select(variant => variant.NormalizedName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var sourceCandidates = sourceKeys
                    .Where(providerPlayersByNameKey.ContainsKey)
                    .SelectMany(key => providerPlayersByNameKey[key])
                    .GroupBy(item => item.Player.MlbPlayerId, StringComparer.OrdinalIgnoreCase)
                    .Select(candidateGroup => BuildTheOddsMlbStatsCandidate(
                        first,
                        sourceKeys,
                        candidateGroup.First().Player,
                        candidateGroup.First().NameKeys,
                        candidateGroup.Count() > 1 || candidateGroup.Select(item => item.Player.MlbPlayerId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1,
                        providerRefConflicts.TryGetValue(candidateGroup.Key, out var conflicts) ? conflicts : []))
                    .OrderByDescending(candidate => candidate.ConfidenceScore)
                    .ThenBy(candidate => candidate.FullName)
                    .ToArray();
                if (sourceCandidates.Length > 1)
                {
                    sourceCandidates = sourceCandidates
                        .Select(AddTheOddsMlbStatsCandidateRiskFlag("MultipleCandidates"))
                        .ToArray();
                }

                var recommendation = DetermineTheOddsMlbStatsCandidateRecommendation(sourceCandidates);

                return new TheOddsMlbManualMappingCandidateRow(
                    first.PlayerId,
                    first.PlayerName,
                    first.PlayerName,
                    sourceNameVariants.FirstOrDefault()?.NormalizedName ?? NormalizeTheOddsPlayerName(first.PlayerName),
                    sourceNameVariants,
                    sourceCandidates.Length,
                    first.PlatformName,
                    first.EventId,
                    BuildTheOddsEventName(first),
                    group.Select(row => row.PropType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    group.Count(),
                    playerRefDiagnostics.TryGetValue(first.PlayerId, out var refs) ? refs.ExternalRefCount : 0,
                    sourceCandidates,
                    recommendation);
            })
            .OrderBy(row => row.Recommendation == "AutoApproveCandidate" ? 0 : row.Recommendation == "ManualReview" ? 1 : 2)
            .ThenBy(row => row.SourcePlayerName)
            .ToArray();
    }

    private static IReadOnlyList<TheOddsMlbBulkMappingCandidateRow> BuildTheOddsMlbBulkMappingCandidateRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> unmappedPlatformProps,
        IReadOnlyCollection<MlbPlayerRecord> providerPlayers,
        IReadOnlyDictionary<Guid, IReadOnlyList<TheOddsExternalRefSummary>> externalRefsByPlayerId,
        IReadOnlyDictionary<string, IReadOnlyList<TheOddsMlbProviderRefConflictRow>> providerRefConflicts)
    {
        var providerPlayersByNameKey = providerPlayers
            .Select(player => new
            {
                Player = player,
                NameKeys = BuildTheOddsPlayerNameKeys(player.DisplayName)
                    .Concat(BuildTheOddsPlayerNameKeys($"{player.FirstName} {player.LastName}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            })
            .SelectMany(item => item.NameKeys.Select(key => new { Key = key, item.Player, item.NameKeys }))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        return unmappedPlatformProps
            .GroupBy(row => new
            {
                row.PlayerId,
                row.PlayerName
            })
            .Select(group =>
            {
                var first = group.OrderByDescending(row => row.ObservedAt).First();
                var sourceNameVariants = BuildTheOddsPlayerNameVariants(first.PlayerName);
                var sourceKeys = sourceNameVariants
                    .Select(variant => variant.NormalizedName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var candidates = sourceKeys
                    .Where(providerPlayersByNameKey.ContainsKey)
                    .SelectMany(key => providerPlayersByNameKey[key])
                    .GroupBy(item => item.Player.MlbPlayerId, StringComparer.OrdinalIgnoreCase)
                    .Select(candidateGroup => BuildTheOddsMlbStatsCandidate(
                        first,
                        sourceKeys,
                        candidateGroup.First().Player,
                        candidateGroup.First().NameKeys,
                        candidateGroup.Count() > 1 || candidateGroup.Select(item => item.Player.MlbPlayerId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1,
                        providerRefConflicts.TryGetValue(candidateGroup.Key, out var conflicts) ? conflicts : []))
                    .OrderByDescending(candidate => candidate.ConfidenceScore)
                    .ThenBy(candidate => candidate.FullName)
                    .ToArray();
                if (candidates.Length > 1)
                {
                    candidates = candidates
                        .Select(AddTheOddsMlbStatsCandidateRiskFlag("MultipleCandidates"))
                        .ToArray();
                }

                var recommendation = DetermineTheOddsMlbStatsCandidateRecommendation(candidates);
                var currentExternalRefs = externalRefsByPlayerId.TryGetValue(first.PlayerId, out var refs)
                    ? refs
                    : [];

                return new TheOddsMlbBulkMappingCandidateRow(
                    first.PlayerId,
                    first.PlayerName,
                    first.PlayerName,
                    sourceNameVariants.FirstOrDefault()?.NormalizedName ?? NormalizeTheOddsPlayerName(first.PlayerName),
                    sourceNameVariants,
                    candidates.Length,
                    group.Select(row => row.PlatformName).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    group
                        .GroupBy(row => row.EventId ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                        .Select(eventGroup =>
                        {
                            var eventRow = eventGroup.OrderByDescending(row => row.ObservedAt).First();
                            return new TheOddsMlbBulkAffectedEventRow(
                                eventRow.EventId,
                                BuildTheOddsEventName(eventRow),
                                eventRow.EventStartTime,
                                eventGroup.Count());
                        })
                        .OrderBy(row => row.EventStartTime)
                        .ThenBy(row => row.EventName)
                        .ToArray(),
                    group.Select(row => row.PropType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    group.Count(),
                    currentExternalRefs,
                    candidates,
                    recommendation);
            })
            .OrderBy(row => row.Recommendation == "AutoApproveCandidate" ? 0 : row.Recommendation == "ManualReview" ? 1 : 2)
            .ThenByDescending(row => row.PlatformPropCount)
            .ThenBy(row => row.SourcePlayerName)
            .ToArray();
    }

    private static object BuildTheOddsMlbBulkMappingCandidateSummary(
        IReadOnlyCollection<TheOddsMlbBulkMappingCandidateRow> rows)
    {
        return new
        {
            totalUnmappedPlayers = rows.Count,
            totalAffectedPlatformProps = rows.Sum(row => row.PlatformPropCount),
            autoApproveCandidateCount = rows.Count(row => row.Recommendation == "AutoApproveCandidate"),
            manualReviewCount = rows.Count(row => row.Recommendation == "ManualReview"),
            noCandidateCount = rows.Count(row => row.Recommendation == "NoCandidate"),
            estimatedPropsRecoverableIfAutoApproved = rows
                .Where(row => row.Recommendation == "AutoApproveCandidate")
                .Sum(row => row.PlatformPropCount),
            estimatedPropsRecoverableIfManualReviewed = rows
                .Where(row => row.Recommendation == "ManualReview")
                .Sum(row => row.PlatformPropCount),
            affectedBookmakers = rows
                .SelectMany(row => row.AffectedBookmakers)
                .GroupBy(bookmaker => bookmaker, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { bookmaker = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.bookmaker)
                .ToArray(),
            affectedPropTypes = rows
                .SelectMany(row => row.AffectedPropTypes)
                .GroupBy(propType => propType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { propType = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.propType)
                .ToArray(),
            topAffectedPlayers = rows
                .OrderByDescending(row => row.PlatformPropCount)
                .ThenBy(row => row.SourcePlayerName)
                .Take(25)
                .Select(row => new
                {
                    row.SourcePlayerId,
                    row.SourcePlayerName,
                    row.PlatformPropCount,
                    row.Recommendation,
                    topCandidate = row.Candidates.OrderByDescending(candidate => candidate.ConfidenceScore).FirstOrDefault()
                })
                .ToArray()
        };
    }

    private static IReadOnlyList<object> BuildTheOddsMlbBulkAutoApprovePlan(
        IReadOnlyCollection<TheOddsMlbBulkMappingCandidateRow> rows)
    {
        return rows
            .Where(row => row.Recommendation == "AutoApproveCandidate")
            .Select(row =>
            {
                var candidate = row.Candidates.OrderByDescending(item => item.ConfidenceScore).First();
                var requestBody = new
                {
                    sourcePlayerId = row.SourcePlayerId,
                    mlbStatsId = candidate.MlbStatsId,
                    providerDisplayName = candidate.FullName,
                    approvedBy = "debug-manual",
                    reason = "Manual approval from The Odds API MLB bulk mapping candidates"
                };

                return new
                {
                    row.SourcePlayerId,
                    row.SourcePlayerName,
                    candidate.MlbStatsId,
                    mlbFullName = candidate.FullName,
                    candidate.ConfidenceScore,
                    affectedPlatformProps = row.PlatformPropCount,
                    requestBodyPreview = requestBody,
                    approvalCommandPreview = $"POST /api/debug/the-odds/mlb/player-mapping-approve {JsonSerializer.Serialize(requestBody)}"
                };
            })
            .Cast<object>()
            .ToArray();
    }

    private static TheOddsMlbStatsCandidateRow BuildTheOddsMlbStatsCandidate(
        TheOddsPersistedPlatformPropRow source,
        IReadOnlySet<string> sourceKeys,
        MlbPlayerRecord player,
        IReadOnlySet<string> candidateKeys,
        bool multipleCandidateSignal,
        IReadOnlyList<TheOddsMlbProviderRefConflictRow> providerRefConflicts)
    {
        var matchingKeys = candidateKeys.Where(sourceKeys.Contains).ToArray();
        var matchReasons = BuildTheOddsMlbStatsCandidateMatchReasons(source.PlayerName, player, matchingKeys);
        var riskFlags = BuildTheOddsMlbStatsCandidateRiskFlags(source, player, matchingKeys, multipleCandidateSignal, providerRefConflicts);
        var confidence = CalculateTheOddsMlbStatsCandidateConfidence(source.PlayerName, player, matchingKeys, riskFlags);
        if (confidence < 0.90m && !riskFlags.Contains("LowConfidence"))
        {
            riskFlags = riskFlags.Concat(["LowConfidence"]).ToArray();
        }

        return new TheOddsMlbStatsCandidateRow(
            player.MlbPlayerId,
            player.DisplayName,
            player.FirstName,
            player.LastName,
            player.TeamName,
            null,
            null,
            confidence,
            matchReasons,
            riskFlags,
            providerRefConflicts);
    }

    private static Func<TheOddsMlbStatsCandidateRow, TheOddsMlbStatsCandidateRow> AddTheOddsMlbStatsCandidateRiskFlag(string riskFlag)
    {
        return candidate => candidate.RiskFlags.Contains(riskFlag)
            ? candidate
            : candidate with { RiskFlags = candidate.RiskFlags.Concat([riskFlag]).ToArray() };
    }

    private static IReadOnlyList<string> BuildTheOddsMlbStatsCandidateMatchReasons(
        string sourcePlayerName,
        MlbPlayerRecord player,
        IReadOnlyCollection<string> matchingKeys)
    {
        var reasons = new List<string>();
        if (string.Equals(sourcePlayerName, player.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("ExactDisplayName");
        }

        if (matchingKeys.Contains(NormalizeTheOddsPlayerName(sourcePlayerName), StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("PunctuationInsensitiveName");
        }

        if (matchingKeys.Contains(NormalizeTheOddsInitialsName(sourcePlayerName), StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("InitialsNormalization");
        }

        if (!string.IsNullOrWhiteSpace(player.FirstName) && !string.IsNullOrWhiteSpace(player.LastName)
            && matchingKeys.Contains(NormalizeTheOddsPlayerName($"{player.FirstName} {player.LastName}"), StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("FirstLastName");
        }

        return reasons.Count == 0 ? ["NameKeyMatch"] : reasons;
    }

    private static IReadOnlyList<string> BuildTheOddsMlbStatsCandidateRiskFlags(
        TheOddsPersistedPlatformPropRow source,
        MlbPlayerRecord player,
        IReadOnlyCollection<string> matchingKeys,
        bool multipleCandidateSignal,
        IReadOnlyList<TheOddsMlbProviderRefConflictRow> providerRefConflicts)
    {
        var flags = new List<string>();
        if (multipleCandidateSignal)
        {
            flags.Add("MultipleCandidates");
        }

        if (matchingKeys.Count == 0)
        {
            flags.Add("NameMismatch");
        }

        if (HasTheOddsAmbiguousInitials(source.PlayerName))
        {
            flags.Add("InitialsAmbiguous");
        }

        if (providerRefConflicts.Any(conflict => conflict.PlayerId != source.PlayerId))
        {
            flags.Add("ProviderPlayerAlreadyMapped");
            flags.Add("AliasNeeded");
        }

        if (IsPitcherPropType(source.PropType)
            && !string.IsNullOrWhiteSpace(player.DisplayName)
            && !string.IsNullOrWhiteSpace(player.TeamName))
        {
            // MLB sports/players feed used here does not expose position in the current DTO,
            // so pitcher context is noted only through prop type and candidate confidence.
        }

        if (flags.Count == 0)
        {
            return [];
        }

        return flags;
    }

    private static decimal CalculateTheOddsMlbStatsCandidateConfidence(
        string sourcePlayerName,
        MlbPlayerRecord player,
        IReadOnlyCollection<string> matchingKeys,
        IReadOnlyCollection<string> riskFlags)
    {
        var confidence = string.Equals(sourcePlayerName, player.DisplayName, StringComparison.OrdinalIgnoreCase)
            ? 1.0m
            : matchingKeys.Contains(NormalizeTheOddsPlayerName(sourcePlayerName), StringComparer.OrdinalIgnoreCase)
                ? 0.97m
                : matchingKeys.Count > 0
                    ? 0.90m
                    : 0.0m;

        if (riskFlags.Contains("InitialsAmbiguous"))
        {
            confidence -= 0.05m;
        }

        if (riskFlags.Contains("MultipleCandidates"))
        {
            confidence -= 0.05m;
        }

        return Math.Clamp(Math.Round(confidence, 4), 0m, 1m);
    }

    private static string DetermineTheOddsMlbStatsCandidateRecommendation(IReadOnlyCollection<TheOddsMlbStatsCandidateRow> candidates)
    {
        if (candidates.Count == 0)
        {
            return "NoCandidate";
        }

        var highConfidenceCandidates = candidates
            .Where(candidate => candidate.ConfidenceScore >= 0.97m
                && !candidate.RiskFlags.Contains("MultipleCandidates")
                && !candidate.RiskFlags.Contains("NameMismatch")
                && !candidate.RiskFlags.Contains("ProviderPlayerAlreadyMapped"))
            .ToArray();

        return highConfidenceCandidates.Length == 1 && candidates.Count == 1
            ? "AutoApproveCandidate"
            : "ManualReview";
    }

    private static bool IsPitcherPropType(string propType)
    {
        var normalized = NormalizeTheOddsPropTypeName(propType);
        return normalized.Contains("PITCHER", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("STRIKEOUT", StringComparison.OrdinalIgnoreCase)
            || normalized == "KS";
    }

    private static async Task<IReadOnlyList<TheOddsPersistedPlatformPropRow>> LoadTheOddsPlatformPropsForPlayerAsync(
        ApplicationDbContext dbContext,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var rows = await LoadTheOddsPlatformPropDataRowsAsync(dbContext, cancellationToken);
        return ParseTheOddsPlatformProps(rows)
            .Where(row => row.PlayerId == playerId)
            .ToArray();
    }

    private static TheOddsExternalRefSummary ToTheOddsExternalRefSummary(PlayerExternalRef reference)
    {
        return new TheOddsExternalRefSummary(
            reference.Provider,
            reference.ProviderPlayerId,
            reference.ProviderDisplayName);
    }

    private static IReadOnlySet<string> BuildTheOddsPlayerNameKeys(string displayName)
    {
        return BuildTheOddsPlayerNameVariants(displayName)
            .Select(variant => variant.NormalizedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeTheOddsPlayerName(string displayName)
    {
        return BuildCompactTheOddsNameKey(RemoveDiacritics(RemoveParentheticalBirthYear(displayName)).ToUpperInvariant());
    }

    private static IReadOnlyList<TheOddsNameVariantRow> BuildTheOddsPlayerNameVariants(string displayName)
    {
        var variants = new List<TheOddsNameVariantRow>();
        AddTheOddsNameVariant(variants, "Original", displayName);

        var accentInsensitive = RemoveDiacritics(displayName);
        AddTheOddsNameVariant(variants, "AccentInsensitive", accentInsensitive);

        var parentheticalRemoved = RemoveParentheticalBirthYear(accentInsensitive);
        AddTheOddsNameVariant(variants, "ParentheticalBirthYearRemoved", parentheticalRemoved);

        var suffixRemoved = RemoveTheOddsNameSuffix(parentheticalRemoved);
        AddTheOddsNameVariant(variants, "SuffixRemoved", suffixRemoved);

        var initialsExpanded = parentheticalRemoved
            .Replace(".", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal);
        AddTheOddsNameVariant(variants, "InitialsWhitespaceNormalized", initialsExpanded);
        AddTheOddsNameVariant(variants, "InitialsSuffixRemoved", RemoveTheOddsNameSuffix(initialsExpanded));

        return variants
            .Where(variant => !string.IsNullOrWhiteSpace(variant.NormalizedName))
            .GroupBy(variant => variant.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddTheOddsNameVariant(List<TheOddsNameVariantRow> variants, string variantType, string value)
    {
        var compact = BuildCompactTheOddsNameKey(value);
        if (!string.IsNullOrWhiteSpace(compact))
        {
            variants.Add(new TheOddsNameVariantRow(variantType, value, compact));
        }
    }

    private static string BuildCompactTheOddsNameKey(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string NormalizeTheOddsInitialsName(string displayName)
    {
        return NormalizeTheOddsPlayerName(displayName.Replace(".", string.Empty, StringComparison.Ordinal));
    }

    private static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string RemoveParentheticalBirthYear(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(value, @"\s*\(\d{4}\)\s*", " ").Trim();
    }

    private static string RemoveTheOddsNameSuffix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var tokens = value
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        while (tokens.Count > 0 && IsTheOddsNameSuffix(tokens[^1]))
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        return string.Join(' ', tokens);
    }

    private static bool IsTheOddsNameSuffix(string token)
    {
        return token.Equals("JR", StringComparison.OrdinalIgnoreCase)
            || token.Equals("SR", StringComparison.OrdinalIgnoreCase)
            || token.Equals("II", StringComparison.OrdinalIgnoreCase)
            || token.Equals("III", StringComparison.OrdinalIgnoreCase)
            || token.Equals("IV", StringComparison.OrdinalIgnoreCase)
            || token.Equals("V", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTheOddsAmbiguousInitials(string displayName)
    {
        var firstToken = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return false;
        }

        var letters = firstToken.Count(char.IsLetter);
        return firstToken.Contains('.', StringComparison.Ordinal) || letters <= 2;
    }

    private static async Task<List<TheOddsEventPredictionWorkflowRow>> LoadTheOddsPredictionWorkflowRowsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> propIds,
        CancellationToken cancellationToken)
    {
        return await LoadTheOddsPredictionWorkflowRowsAsync(dbContext, propIds, runId: null, cancellationToken);
    }

    private static async Task<List<TheOddsEventPredictionWorkflowRow>> LoadTheOddsPredictionWorkflowRowsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> propIds,
        Guid? runId,
        CancellationToken cancellationToken)
    {
        var propIdArray = propIds.Distinct().ToArray();
        if (propIdArray.Length == 0)
        {
            return [];
        }

        var query = dbContext.Predictions
            .AsNoTracking()
            .Where(prediction => propIdArray.Contains(prediction.PropId)
                && prediction.PredictionRun != null);

        if (runId.HasValue)
        {
            query = query.Where(prediction => prediction.PredictionRunId == runId.Value);
        }

        return await query
            .Select(prediction => new TheOddsEventPredictionWorkflowRow(
                prediction.PropId,
                prediction.PredictionRunId,
                prediction.PredictionRun!.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private static async Task<List<TheOddsLineShoppingBacktestRow>> BuildTheOddsBacktestRowsAsync(
        IReadOnlyCollection<TheOddsLineShoppingGroup> groups,
        IReadOnlyDictionary<TheOddsActualResultKey, decimal> actualResults,
        IReadOnlyDictionary<Guid, Guid[]> equivalentPropTypeIdsById,
        IPlayerIdentityAliasService playerIdentityAliasService,
        Dictionary<Guid, Guid[]> playerAliasCache,
        CancellationToken cancellationToken)
    {
        var rows = new List<TheOddsLineShoppingBacktestRow>(groups.Count);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var equivalentPlayerIds = await GetTheOddsEquivalentPlayerIdsAsync(
                group.PlayerId,
                playerIdentityAliasService,
                playerAliasCache,
                cancellationToken);
            var equivalentPropTypeIds = equivalentPropTypeIdsById.TryGetValue(group.PropTypeId, out var propTypeIds)
                ? propTypeIds
                : [group.PropTypeId];
            var actualResult = FindTheOddsActualResult(
                actualResults,
                group.EventDate,
                equivalentPlayerIds,
                equivalentPropTypeIds);
            var outcome = GradeTheOddsBestSide(group.BestSide, group.Line, actualResult);
            var profit = CalculateTheOddsProfitPerDollarStake(outcome, group.BestDecimalPrice);

            rows.Add(new TheOddsLineShoppingBacktestRow(
                group.Player,
                group.NormalizedPropType,
                group.MarketClassification,
                group.Line,
                group.EventId,
                group.EventStartTime,
                group.HomeTeam,
                group.AwayTeam,
                BuildTheOddsEventName(group.HomeTeam, group.AwayTeam),
                group.EventStartTimeUtc,
                group.ExpectedResultDate,
                group.ResultDateSource,
                group.UtcDate,
                group.ResolvedMlbDate,
                group.BestSide,
                group.BestBookmaker,
                group.BestAmericanPrice,
                group.BestDecimalPrice,
                group.BestEstimatedEv,
                group.BestProbability,
                group.BestImpliedProbability,
                group.BestProbabilityEdge,
                group.TrustLevel,
                group.SampleWarning,
                group.LifecycleStatus,
                group.HistoricalSampleSize,
                group.ConfidenceScore,
                group.RankingScore,
                actualResult,
                outcome,
                profit,
                BuildTheOddsUngradedReason(group, actualResult, outcome)));
        }

        return rows;
    }

    private static List<TheOddsEventWorkflowStatusRow> BuildTheOddsEventWorkflowRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        IReadOnlyDictionary<Guid, int> lineSnapshotCounts,
        IReadOnlyCollection<TheOddsEventPredictionWorkflowRow> predictionRows,
        IReadOnlyCollection<TheOddsLineShoppingGroup> lineShoppingGroups,
        IReadOnlyCollection<TheOddsLineShoppingBacktestRow> backtestRows,
        DateTimeOffset generatedAtUtc)
    {
        var predictionRowsByPropId = predictionRows
            .GroupBy(row => row.PropId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var groupsByEventId = lineShoppingGroups
            .Where(group => !string.IsNullOrWhiteSpace(group.EventId))
            .GroupBy(group => group.EventId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var backtestRowsByEventId = backtestRows
            .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
            .GroupBy(row => row.EventId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        return platformProps
            .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
            .GroupBy(row => row.EventId!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var props = group.ToArray();
                var propIds = props.Select(row => row.PropId).Distinct().ToArray();
                var eventPredictionRows = propIds
                    .Where(predictionRowsByPropId.ContainsKey)
                    .SelectMany(propId => predictionRowsByPropId[propId])
                    .ToArray();
                var predictionRunIds = eventPredictionRows
                    .GroupBy(row => row.PredictionRunId)
                    .Select(runGroup => new
                    {
                        runId = runGroup.Key,
                        createdAt = runGroup.Max(row => row.PredictionRunCreatedAt)
                    })
                    .OrderByDescending(row => row.createdAt)
                    .ToArray();
                var latestPredictionRunId = predictionRunIds.FirstOrDefault()?.runId;
                var latestPredictionRunCreatedAt = predictionRunIds.FirstOrDefault()?.createdAt;
                var latestRunPredictionRows = latestPredictionRunId.HasValue
                    ? eventPredictionRows
                        .Where(row => row.PredictionRunId == latestPredictionRunId.Value)
                        .ToArray()
                    : [];
                var latestRunPredictedPropIds = latestRunPredictionRows
                    .Select(row => row.PropId)
                    .Distinct()
                    .ToHashSet();
                var distinctPredictedPlatformProps = props.Count(prop => latestRunPredictedPropIds.Contains(prop.PropId));
                groupsByEventId.TryGetValue(group.Key, out var eventGroups);
                backtestRowsByEventId.TryGetValue(group.Key, out var eventBacktestRows);

                eventGroups ??= [];
                eventBacktestRows ??= [];

                var gradedGroups = eventBacktestRows.Count(IsTheOddsGraded);
                var ungradedGroups = eventBacktestRows.Length - gradedGroups;
                var workflowStatus = DetermineTheOddsEventWorkflowStatus(
                    props.Length,
                    latestRunPredictionRows.Length,
                    eventGroups.Length,
                    gradedGroups,
                    ungradedGroups,
                    props[0].EventStartTime,
                    generatedAtUtc);

                return new TheOddsEventWorkflowStatusRow(
                    group.Key,
                    props.Select(row => row.HomeTeam).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    props.Select(row => row.AwayTeam).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    props.Select(row => row.EventStartTime).FirstOrDefault(value => value.HasValue),
                    props.Select(row => row.EventStartTime?.Date).FirstOrDefault(value => value.HasValue),
                    props.Select(row => row.PlatformName).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    props.Select(row => row.MarketKey ?? "Unknown").Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    props.Length,
                    props.Sum(row => lineSnapshotCounts.GetValueOrDefault(row.PlatformPropGuid)),
                    props.Max(row => row.ObservedAt),
                    predictionRunIds.Select(row => row.runId).ToArray(),
                    latestPredictionRunId,
                    latestPredictionRunCreatedAt,
                    latestRunPredictionRows.Length,
                    distinctPredictedPlatformProps,
                    eventPredictionRows.Length,
                    eventGroups.Length,
                    eventGroups.Count(row => row.IsEligibleAsNormalRecommendation),
                    eventBacktestRows.Length,
                    gradedGroups,
                    ungradedGroups,
                    workflowStatus);
            })
            .ToList();
    }

    private static IReadOnlyList<TheOddsMlbIngestionPlanRow> BuildTheOddsMlbIngestionPlanRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        IReadOnlyCollection<TheOddsEventWorkflowStatusRow> workflowRows,
        IReadOnlyDictionary<Guid, PlayerHistoricalLookupDiagnostic> readinessByPlatformPropId,
        IReadOnlySet<string> requestedBookmakers,
        IReadOnlySet<string> requestedMarkets,
        DateTimeOffset generatedAtUtc)
    {
        return workflowRows
            .Select(workflowRow =>
            {
                var props = platformProps
                    .Where(prop => string.Equals(prop.EventId, workflowRow.EventId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var normalizedBookmakers = props
                    .Select(prop => NormalizeTheOddsBookmakerKey(prop.BookmakerKey, prop.PlatformName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var marketKeys = props
                    .Select(prop => prop.MarketKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Select(key => key!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingRequestedBookmakers = requestedBookmakers
                    .Where(bookmaker => !normalizedBookmakers.Contains(bookmaker))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var missingRequestedMarkets = requestedMarkets
                    .Where(market => !marketKeys.Contains(market))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var missingMappingCount = props.Count(prop =>
                    readinessByPlatformPropId.TryGetValue(prop.PlatformPropGuid, out var readiness)
                    && !readiness.HasMlbMappingForEquivalentPlayers);
                var missingHistoricalResultsCount = props.Count(prop =>
                    readinessByPlatformPropId.TryGetValue(prop.PlatformPropGuid, out var readiness)
                    && readiness.HasMlbMappingForEquivalentPlayers
                    && readiness.HistoricalGamesAvailable == 0);
                var hasResults = workflowRow.BacktestGradedGroups > 0;
                var needsIngestion = missingRequestedBookmakers.Length > 0 || missingRequestedMarkets.Length > 0;
                var needsPrediction = workflowRow.PlatformPropCount > 0 && workflowRow.PredictionCount == 0;
                var needsLineShopping = workflowRow.PredictionCount > 0 && workflowRow.LineShoppingGroupCount == 0;
                var needsResults = workflowRow.EventStartTime.HasValue
                    && workflowRow.EventStartTime.Value <= generatedAtUtc
                    && workflowRow.BacktestGradedGroups == 0;
                var needsBacktest = workflowRow.LineShoppingGroupCount > 0
                    && workflowRow.EventStartTime.HasValue
                    && workflowRow.EventStartTime.Value <= generatedAtUtc
                    && workflowRow.BacktestGradedGroups > 0
                    && workflowRow.BacktestUngradedGroups > 0;
                var coverageOpportunity = needsIngestion
                    && workflowRow.LineShoppingGroupCount > 0
                    && workflowRow.PredictionCount > 0;
                var severeGapThreshold = Math.Max(1, (int)Math.Ceiling(workflowRow.PlatformPropCount * 0.5m));
                var severeMappingOrHistoryGap = workflowRow.PlatformPropCount > 0
                    && missingMappingCount + missingHistoricalResultsCount >= severeGapThreshold
                    && workflowRow.LineShoppingGroupCount == 0;
                var dataQualityWarning = missingMappingCount > 0 || missingHistoricalResultsCount > 0;
                var needsAttention = workflowRow.PlatformPropCount == 0
                    || needsPrediction
                    || needsLineShopping
                    || severeMappingOrHistoryGap;
                var attentionSeverity = DetermineTheOddsMlbIngestionPlanAttentionSeverity(
                    needsAttention,
                    dataQualityWarning,
                    coverageOpportunity);
                var reasons = BuildTheOddsMlbIngestionPlanReasons(
                    coverageOpportunity,
                    needsPrediction,
                    needsLineShopping,
                    needsResults,
                    needsBacktest,
                    missingMappingCount,
                    missingHistoricalResultsCount,
                    missingRequestedBookmakers,
                    missingRequestedMarkets);
                return new TheOddsMlbIngestionPlanRow(
                    workflowRow.EventId,
                    BuildTheOddsEventName(workflowRow.HomeTeam, workflowRow.AwayTeam),
                    workflowRow.EventStartTime,
                    workflowRow.HomeTeam,
                    workflowRow.AwayTeam,
                    workflowRow.WorkflowStatus,
                    workflowRow.PlatformPropCount,
                    workflowRow.PredictionCount,
                    workflowRow.LatestPredictionRunId,
                    workflowRow.LatestPredictionRunCreatedAt,
                    workflowRow.PredictionCount,
                    workflowRow.DistinctPredictedPlatformProps,
                    workflowRow.AllTimePredictionCount,
                    workflowRow.LineShoppingGroupCount,
                    hasResults,
                    workflowRow.BacktestTotalGroups,
                    workflowRow.BacktestGradedGroups,
                    missingMappingCount,
                    missingHistoricalResultsCount,
                    coverageOpportunity,
                    dataQualityWarning,
                    attentionSeverity,
                    needsIngestion,
                    needsPrediction,
                    needsLineShopping,
                    needsResults,
                    needsBacktest,
                    needsAttention,
                    missingRequestedBookmakers,
                    missingRequestedMarkets,
                    reasons);
            })
            .ToArray();
    }

    private static IReadOnlyList<string> BuildTheOddsMlbIngestionPlanReasons(
        bool coverageOpportunity,
        bool needsPrediction,
        bool needsLineShopping,
        bool needsResults,
        bool needsBacktest,
        int missingMappingCount,
        int missingHistoricalResultsCount,
        IReadOnlyCollection<string> missingRequestedBookmakers,
        IReadOnlyCollection<string> missingRequestedMarkets)
    {
        var reasons = new List<string>();
        if (coverageOpportunity)
        {
            reasons.Add("Optional requested bookmaker or market coverage is missing from persisted rows.");
        }

        if (missingRequestedBookmakers.Count > 0)
        {
            reasons.Add($"Missing requested bookmakers: {string.Join(", ", missingRequestedBookmakers)}.");
        }

        if (missingRequestedMarkets.Count > 0)
        {
            reasons.Add($"Missing requested markets: {string.Join(", ", missingRequestedMarkets)}.");
        }

        if (needsPrediction)
        {
            reasons.Add("Persisted props exist but no predictions were generated.");
        }

        if (needsLineShopping)
        {
            reasons.Add("Predictions exist but no line-shopping groups were built.");
        }

        if (needsResults)
        {
            reasons.Add("Event has started, but no completed MLB result match is available yet.");
        }

        if (needsBacktest)
        {
            reasons.Add("Some line-shopping groups are graded and remaining groups are still ungraded.");
        }

        if (missingMappingCount > 0)
        {
            reasons.Add($"{missingMappingCount} persisted props still lack usable MLB player mapping.");
        }

        if (missingHistoricalResultsCount > 0)
        {
            reasons.Add($"{missingHistoricalResultsCount} persisted props have MLB mappings but no usable historical results.");
        }

        return reasons;
    }

    private static string DetermineTheOddsMlbIngestionPlanAttentionSeverity(
        bool needsAttention,
        bool dataQualityWarning,
        bool coverageOpportunity)
    {
        if (needsAttention)
        {
            return "Blocked";
        }

        if (dataQualityWarning)
        {
            return "Warning";
        }

        return coverageOpportunity ? "Info" : "None";
    }

    private static object BuildTheOddsMlbIngestionPlanSummary(
        IReadOnlyCollection<TheOddsMlbIngestionPlanRow> planRows,
        IReadOnlyCollection<TheOddsEventWorkflowStatusRow> workflowRows,
        DateOnly? requestedDate,
        IReadOnlySet<string> requestedBookmakers,
        IReadOnlySet<string> requestedMarkets)
    {
        var ingestionCandidates = planRows.Where(row => row.NeedsIngestion).ToArray();
        var recommendedNextAction = BuildTheOddsMlbIngestionPlanRecommendedAction(planRows, requestedDate);

        return new
        {
            totalPersistedEvents = workflowRows.Count,
            eventsWithPlatformProps = planRows.Count(row => row.PlatformPropCount > 0),
            eventsWithPredictions = planRows.Count(row => row.PredictionCount > 0),
            eventsInLineShopping = planRows.Count(row => row.LineShoppingGroupCount > 0),
            eventsAwaitingResults = planRows.Count(row => row.Status == "AwaitingResults"),
            eventsBacktestReady = planRows.Count(row => row.Status == "BacktestReady"),
            eventsCompleted = planRows.Count(row => row.Status == "Completed"),
            eventsNeedingAttention = planRows.Count(row => row.NeedsAttention),
            eventsWithCoverageOpportunity = planRows.Count(row => row.CoverageOpportunity),
            eventsWithDataQualityWarning = planRows.Count(row => row.DataQualityWarning),
            eventsBlocked = planRows.Count(row => row.AttentionSeverity == "Blocked"),
            persistedEventsForRequestedDate = requestedDate.HasValue
                ? planRows.Count(row => row.EventStartTime.HasValue
                    && DateOnly.FromDateTime(row.EventStartTime.Value.UtcDateTime) == requestedDate.Value)
                : (int?)null,
            requestedBookmakers,
            requestedMarkets,
            candidateEventsForManualIngestion = ingestionCandidates.Length,
            estimatedCreditsIfIngestingOneEvent = ingestionCandidates.Length > 0 ? 1 : 0,
            estimatedCreditsIfIngestingAllCandidateEvents = ingestionCandidates.Length,
            creditEstimateUnit = "event odds request; actual The Odds API credit cost may vary by account, bookmaker count, and market count.",
            recommendedNextAction
        };
    }

    private static string BuildTheOddsMlbIngestionPlanRecommendedAction(
        IReadOnlyCollection<TheOddsMlbIngestionPlanRow> planRows,
        DateOnly? requestedDate)
    {
        if (planRows.Count == 0)
        {
            return requestedDate.HasValue
                ? "No persisted The Odds API MLB events exist for this date. Fetch MLB events manually first; this planner does not call The Odds API."
                : "No persisted The Odds API MLB events exist yet. Use the manual event fetch workflow first; this planner does not call The Odds API.";
        }

        var onlyOptionalCoverageMissing = planRows.All(row => !row.NeedsAttention)
            && planRows.Any(row => row.CoverageOpportunity)
            && planRows.All(row => row.Status is "LineShoppingReady" or "AwaitingResults" or "BacktestReady" or "Completed");
        if (onlyOptionalCoverageMissing)
        {
            return "Events are line-shopping ready. Optional ingestion could improve missing bookmaker coverage. Await results for backtesting.";
        }

        if (planRows.Any(row => row.NeedsAttention))
        {
            return "Address blocked events before expanding ingestion. Check prediction generation, line-shopping inclusion, and severe mapping/history gaps.";
        }

        if (planRows.Any(row => row.CoverageOpportunity))
        {
            return "Optional bookmaker or market coverage is available. Use manual dry-run ingest before committing any The Odds API call.";
        }

        if (planRows.Any(row => row.NeedsPrediction))
        {
            return "Generate scoped The Odds MLB predictions for persisted events that have props but no predictions.";
        }

        if (planRows.Any(row => row.NeedsResults))
        {
            return "Wait for MLB results import to grade started events, then rerun line-shopping backtest.";
        }

        if (planRows.Any(row => row.DataQualityWarning))
        {
            return "Events are usable, but some rows have mapping or historical-result warnings. Review warnings before broadening ingestion.";
        }

        return "Persisted The Odds API MLB events are currently planned through line-shopping/backtest state.";
    }

    private static string BuildTheOddsMlbWorkerDryRunRecommendedAction(
        TheOddsMlbIngestionWorkerPlan plan,
        int skippedEvents,
        IReadOnlyCollection<string> skippedReasons)
    {
        if (plan.CandidateEvents == 0)
        {
            return "No worker ingestion candidates were found from persisted planner state. No The Odds API calls would be made.";
        }

        if (plan.PlannedEventCount == 0 && skippedReasons.Any(reason =>
            reason.Contains("credit cap", StringComparison.OrdinalIgnoreCase)))
        {
            return "Candidate events exist, but worker credit caps would prevent ingestion.";
        }

        if (plan.PlannedEventCount > 0)
        {
            return skippedEvents > 0
                ? "Manual dry-run found candidate events, but max events or credit caps would limit this run. No The Odds API calls or writes were performed."
                : "Manual dry-run found candidate events that the worker would plan. No The Odds API calls or writes were performed.";
        }

        return "Manual dry-run completed with no planned worker actions. No The Odds API calls or writes were performed.";
    }

    private static IReadOnlyList<string> ParseCommaSeparatedSet(string? value, IReadOnlyCollection<string>? defaults = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaults?.ToArray() ?? [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DetermineTheOddsEventWorkflowStatus(
        int platformPropCount,
        int predictionCount,
        int lineShoppingGroupCount,
        int backtestGradedGroups,
        int backtestUngradedGroups,
        DateTimeOffset? eventStartTime,
        DateTimeOffset generatedAtUtc)
    {
        if (platformPropCount <= 0)
        {
            return "NeedsAttention";
        }

        if (predictionCount == 0)
        {
            return eventStartTime.HasValue && eventStartTime.Value < generatedAtUtc
                ? "NeedsAttention"
                : "Ingested";
        }

        if (lineShoppingGroupCount == 0)
        {
            return eventStartTime.HasValue && eventStartTime.Value < generatedAtUtc
                ? "NeedsAttention"
                : "Predicted";
        }

        if (backtestGradedGroups > 0 && backtestUngradedGroups == 0)
        {
            return "Completed";
        }

        if (backtestGradedGroups > 0)
        {
            return "BacktestReady";
        }

        if (eventStartTime.HasValue && eventStartTime.Value <= generatedAtUtc)
        {
            return "AwaitingResults";
        }

        return "LineShoppingReady";
    }

    private static async Task<List<TheOddsRawBookmakerCoverageRow>> LoadTheOddsRawBookmakerCoverageRowsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        string? eventId,
        CancellationToken cancellationToken)
    {
        var rawPayloadIds = platformProps
            .Select(row => row.RawPayloadId)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();

        if (rawPayloadIds.Length == 0)
        {
            return [];
        }

        var rawPayloads = await dbContext.RawIngestionPayloads
            .AsNoTracking()
            .Where(payload => rawPayloadIds.Contains(payload.Id))
            .Select(payload => new
            {
                payload.Id,
                payload.Payload
            })
            .ToListAsync(cancellationToken);
        var rows = new List<TheOddsRawBookmakerCoverageRow>();

        foreach (var payload in rawPayloads)
        {
            rows.AddRange(ExtractRawBookmakerCoverageRows(payload.Payload)
                .Where(row => string.IsNullOrWhiteSpace(eventId)
                    || string.Equals(row.EventId, eventId, StringComparison.OrdinalIgnoreCase)));
        }

        return rows
            .GroupBy(row => new
            {
                row.EventId,
                NormalizedBookmaker = NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.BookmakerName)
            })
            .Select(group => new TheOddsRawBookmakerCoverageRow(
                group.Key.EventId,
                group.Select(row => row.BookmakerKey).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                group.Select(row => row.BookmakerName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key.NormalizedBookmaker,
                group.Sum(row => row.RawOutcomeCount),
                group.SelectMany(row => row.MarketsPresent).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()))
            .ToList();
    }

    private static IEnumerable<TheOddsRawBookmakerCoverageRow> ExtractRawBookmakerCoverageRows(JsonDocument payload)
    {
        var root = payload.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        var eventId = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(eventId)
            || !root.TryGetProperty("bookmakers", out var bookmakers)
            || bookmakers.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var bookmaker in bookmakers.EnumerateArray())
        {
            if (bookmaker.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var bookmakerKey = GetJsonString(bookmaker, "key");
            var bookmakerTitle = GetJsonString(bookmaker, "title");
            var outcomeCount = 0;
            var marketKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (bookmaker.TryGetProperty("markets", out var markets)
                && markets.ValueKind == JsonValueKind.Array)
            {
                foreach (var market in markets.EnumerateArray())
                {
                    if (market.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var marketKey = GetJsonString(market, "key");
                    if (!string.IsNullOrWhiteSpace(marketKey))
                    {
                        marketKeys.Add(marketKey);
                    }

                    if (market.TryGetProperty("outcomes", out var outcomes)
                        && outcomes.ValueKind == JsonValueKind.Array)
                    {
                        outcomeCount += outcomes.GetArrayLength();
                    }
                }
            }

            yield return new TheOddsRawBookmakerCoverageRow(
                eventId,
                bookmakerKey,
                bookmakerTitle,
                outcomeCount,
                marketKeys.Order().ToArray());
        }
    }

    private static object[] BuildTheOddsBookmakerCoverageRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        IReadOnlyCollection<TheOddsLineShoppingGroup> lineShoppingGroups)
    {
        var bookmakerKeys = platformProps
            .Select(row => NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName))
            .Concat(lineShoppingGroups.SelectMany(group => group.BookmakerPrices.Select(price => NormalizeTheOddsBookmakerKey(null, price.Bookmaker))))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return bookmakerKeys
            .Select(bookmakerKey =>
            {
                var bookmakerProps = platformProps
                    .Where(row => string.Equals(
                        NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                        bookmakerKey,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var groupsWithBookmaker = lineShoppingGroups
                    .Where(group => group.BookmakerPrices.Any(price => string.Equals(
                        NormalizeTheOddsBookmakerKey(null, price.Bookmaker),
                        bookmakerKey,
                        StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                var groupsBestAtBookmaker = lineShoppingGroups
                    .Where(group => string.Equals(
                        NormalizeTheOddsBookmakerKey(null, group.BestBookmaker),
                        bookmakerKey,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                return new
                {
                    bookmaker = bookmakerProps.Select(row => row.PlatformName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                        ?? groupsWithBookmaker.SelectMany(group => group.BookmakerPrices).Select(price => price.Bookmaker).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                        ?? bookmakerKey,
                    eventCount = bookmakerProps.Select(row => row.EventId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    platformPropCount = bookmakerProps.Length,
                    comparisonGroupCount = groupsWithBookmaker.Length,
                    activeGroupCount = groupsWithBookmaker.Count(IsActiveTheOddsLineShoppingGroup),
                    candidateCount = groupsBestAtBookmaker.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate),
                    watchlistCount = groupsBestAtBookmaker.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsWatchlist),
                    avoidCount = groupsBestAtBookmaker.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsAvoid),
                    averageBestPrice = AverageNullable(groupsBestAtBookmaker.Select(group => group.BestDecimalPrice)),
                    averageBestEstimatedEv = AverageNullable(groupsBestAtBookmaker.Select(group => group.BestEstimatedEv))
                };
            })
            .OrderByDescending(row => row.platformPropCount)
            .ThenBy(row => row.bookmaker)
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildTheOddsBookmakerPropTypeCoverageRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        IReadOnlyCollection<TheOddsLineShoppingGroup> lineShoppingGroups)
    {
        var propRows = platformProps
            .GroupBy(row => new
            {
                BookmakerKey = NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                row.PlatformName,
                row.PropType
            })
            .Select(group =>
            {
                var groupsWithBookmakerAndProp = lineShoppingGroups
                    .Where(lineShoppingGroup => string.Equals(lineShoppingGroup.NormalizedPropType, group.Key.PropType, StringComparison.OrdinalIgnoreCase)
                        && lineShoppingGroup.BookmakerPrices.Any(price => string.Equals(
                            NormalizeTheOddsBookmakerKey(null, price.Bookmaker),
                            group.Key.BookmakerKey,
                            StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                var bestAtBookmakerAndProp = groupsWithBookmakerAndProp
                    .Where(lineShoppingGroup => string.Equals(
                        NormalizeTheOddsBookmakerKey(null, lineShoppingGroup.BestBookmaker),
                        group.Key.BookmakerKey,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                return new
                {
                    bookmaker = group.Select(row => row.PlatformName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key.BookmakerKey,
                    propType = group.Key.PropType,
                    platformPropCount = group.Count(),
                    comparisonGroupCount = groupsWithBookmakerAndProp.Length,
                    candidateCount = bestAtBookmakerAndProp.Count(lineShoppingGroup => ClassifyTheOddsLineShoppingStrategy(lineShoppingGroup).IsCandidate)
                };
            })
            .OrderBy(row => row.bookmaker)
            .ThenByDescending(row => row.platformPropCount)
            .ThenBy(row => row.propType)
            .Cast<object>()
            .ToArray();

        return propRows;
    }

    private static object[] BuildMissingTheOddsBookmakerCoverageRows(
        IReadOnlyCollection<string> configuredBookmakers,
        IReadOnlyCollection<string> expectedEvents,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps)
    {
        return configuredBookmakers
            .Select(bookmaker =>
            {
                var observedEvents = platformProps
                    .Where(row => string.Equals(
                        NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName),
                        bookmaker,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.EventId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var missingEvents = expectedEvents
                    .Except(observedEvents, StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new
                {
                    bookmaker,
                    expectedEvents = expectedEvents.Count,
                    observedEvents = observedEvents.Length,
                    missingEvents,
                    notes = missingEvents.Length == 0
                        ? "Configured bookmaker has persisted rows for every observed event in this slate."
                        : "Configured bookmaker is missing persisted rows for one or more observed events. Use refresh-current live mode only when you intend to spend credits."
                };
            })
            .OrderByDescending(row => row.missingEvents.Length)
            .ThenBy(row => row.bookmaker)
            .Cast<object>()
            .ToArray();
    }

    private static List<TheOddsBookmakerCoverageAuditRow> BuildTheOddsBookmakerCoverageAuditRows(
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformProps,
        IReadOnlyCollection<TheOddsRawBookmakerCoverageRow> rawBookmakerRows,
        IReadOnlyDictionary<Guid, int> lineSnapshotCounts,
        IReadOnlyCollection<TheOddsEventPredictionWorkflowRow> predictionRows,
        IReadOnlyCollection<TheOddsLineShoppingGroup> lineShoppingGroups)
    {
        var expectedBookmakers = new[]
        {
            new ExpectedBookmaker("draftkings", "DraftKings"),
            new ExpectedBookmaker("fanduel", "FanDuel"),
            new ExpectedBookmaker("betmgm", "BetMGM"),
            new ExpectedBookmaker("bovada", "Bovada")
        };
        var eventIds = platformProps
            .Select(row => row.EventId)
            .Concat(rawBookmakerRows.Select(row => row.EventId))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToArray();
        var predictionRowsByPropId = predictionRows
            .GroupBy(row => row.PropId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return eventIds
            .SelectMany(currentEventId =>
            {
                var eventPlatformProps = platformProps
                    .Where(row => string.Equals(row.EventId, currentEventId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var eventRawRows = rawBookmakerRows
                    .Where(row => string.Equals(row.EventId, currentEventId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var bookmakerKeys = eventPlatformProps
                    .Select(row => NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName))
                    .Concat(eventRawRows.Select(row => NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.BookmakerName)))
                    .Concat(expectedBookmakers.Select(row => row.Key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order()
                    .ToArray();

                return bookmakerKeys.Select(bookmakerKey =>
                    BuildTheOddsBookmakerCoverageAuditRow(
                        currentEventId!,
                        bookmakerKey,
                        expectedBookmakers.FirstOrDefault(row => row.Key == bookmakerKey)?.Name,
                        eventPlatformProps,
                        eventRawRows,
                        lineSnapshotCounts,
                        predictionRowsByPropId,
                        lineShoppingGroups));
            })
            .OrderBy(row => row.EventStartTime)
            .ThenBy(row => row.BookmakerName)
            .ToList();
    }

    private static TheOddsBookmakerCoverageAuditRow BuildTheOddsBookmakerCoverageAuditRow(
        string eventId,
        string bookmakerKey,
        string? expectedBookmakerName,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> eventPlatformProps,
        IReadOnlyCollection<TheOddsRawBookmakerCoverageRow> eventRawRows,
        IReadOnlyDictionary<Guid, int> lineSnapshotCounts,
        IReadOnlyDictionary<Guid, TheOddsEventPredictionWorkflowRow[]> predictionRowsByPropId,
        IReadOnlyCollection<TheOddsLineShoppingGroup> allLineShoppingGroups)
    {
        var platformRows = eventPlatformProps
            .Where(row => string.Equals(NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.PlatformName), bookmakerKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var rawRows = eventRawRows
            .Where(row => string.Equals(NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.BookmakerName), bookmakerKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var propIds = platformRows.Select(row => row.PropId).Distinct().ToArray();
        var predictionCount = propIds
            .Where(predictionRowsByPropId.ContainsKey)
            .Sum(propId => predictionRowsByPropId[propId].Length);
        var lineShoppingGroups = allLineShoppingGroups
            .Where(group => string.Equals(group.EventId, eventId, StringComparison.OrdinalIgnoreCase)
                && group.BookmakerPrices.Any(price => string.Equals(
                    NormalizeTheOddsBookmakerKey(null, price.Bookmaker),
                    bookmakerKey,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var rawOutcomeCount = rawRows.Sum(row => row.RawOutcomeCount);
        var bookmakerName = platformRows.Select(row => row.PlatformName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? rawRows.Select(row => row.BookmakerName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? expectedBookmakerName
            ?? bookmakerKey;
        var marketsPresent = platformRows
            .Select(row => row.MarketKey)
            .Concat(rawRows.SelectMany(row => row.MarketsPresent))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToArray();
        var issueFlags = BuildTheOddsBookmakerIssueFlags(
            rawOutcomeCount,
            platformRows.Length,
            platformRows.Sum(row => lineSnapshotCounts.GetValueOrDefault(row.PlatformPropGuid)),
            predictionCount,
            lineShoppingGroups.Length,
            marketsPresent,
            platformRows,
            rawRows);

        return new TheOddsBookmakerCoverageAuditRow(
            eventId,
            platformRows.Select(row => row.HomeTeam).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            platformRows.Select(row => row.AwayTeam).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            platformRows.Select(row => row.EventStartTime).FirstOrDefault(value => value.HasValue),
            rawRows.Select(row => row.BookmakerKey).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? platformRows.Select(row => row.BookmakerKey).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? bookmakerKey,
            bookmakerName,
            rawRows.Length == 0 ? null : rawOutcomeCount,
            platformRows.Length,
            platformRows.Length,
            platformRows.Sum(row => lineSnapshotCounts.GetValueOrDefault(row.PlatformPropGuid)),
            predictionCount,
            lineShoppingGroups.Length,
            lineShoppingGroups.Count(group => group.IsEligibleAsNormalRecommendation),
            marketsPresent,
            platformRows.Select(row => row.PropType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            platformRows.Select(row => row.PlayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            platformRows.Select(row => row.ObservedAt).DefaultIfEmpty().Max(),
            issueFlags);
    }

    private static IReadOnlyList<string> BuildTheOddsBookmakerIssueFlags(
        int rawOutcomeCount,
        int platformPropCount,
        int lineSnapshotCount,
        int predictionCount,
        int lineShoppingGroupCount,
        IReadOnlyCollection<string> marketsPresent,
        IReadOnlyCollection<TheOddsPersistedPlatformPropRow> platformRows,
        IReadOnlyCollection<TheOddsRawBookmakerCoverageRow> rawRows)
    {
        var flags = new List<string>();

        if (rawRows.Count == 0)
        {
            flags.Add("noRawPayload");
        }

        if (rawOutcomeCount > 0 && platformPropCount == 0)
        {
            flags.Add("returnedByProviderButNotPersisted");
        }

        if (platformPropCount > 0 && lineSnapshotCount == 0)
        {
            flags.Add("persistedButNoSnapshots");
        }

        if (lineSnapshotCount > 0 && predictionCount == 0)
        {
            flags.Add("snapshotsButNoPredictions");
        }

        if (predictionCount > 0 && lineShoppingGroupCount == 0)
        {
            flags.Add("predictionsButNoLineShopping");
        }

        if (rawOutcomeCount > 0 && platformPropCount == 0 && marketsPresent.Count > 0)
        {
            flags.Add("unsupportedMarketsOnly");
        }

        if (platformRows.Any(row => string.IsNullOrWhiteSpace(row.BookmakerKey))
            || rawRows.Any(row => string.IsNullOrWhiteSpace(row.BookmakerKey)))
        {
            flags.Add("possibleBookmakerNameMismatch");
        }

        if (platformRows.Any(row => string.IsNullOrWhiteSpace(row.EventId)
            || string.IsNullOrWhiteSpace(row.MarketKey)
            || string.IsNullOrWhiteSpace(row.BookmakerKey)))
        {
            flags.Add("possibleMetadataMissing");
        }

        if (flags.Count == 0)
        {
            flags.Add("noIssuesDetected");
        }

        return flags;
    }

    private static object BuildTheOddsBookmakerCoverageSummary(
        IReadOnlyCollection<TheOddsBookmakerCoverageAuditRow> rows)
    {
        var expected = new[] { "DraftKings", "FanDuel", "BetMGM", "Bovada" };
        var seenBookmakers = rows
            .Where(row => row.PlatformPropCount > 0 || (row.RawOutcomeCount ?? 0) > 0)
            .Select(row => row.BookmakerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToArray();

        return new
        {
            totalEvents = rows.Select(row => row.EventId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            totalBookmakers = rows.Select(row => row.BookmakerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            bookmakersSeen = seenBookmakers,
            bookmakerCoverageCounts = rows
                .GroupBy(row => row.BookmakerName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    bookmaker = group.Key,
                    eventRows = group.Count(),
                    eventsWithRawPayload = group.Count(row => (row.RawOutcomeCount ?? 0) > 0),
                    eventsWithPlatformProps = group.Count(row => row.PlatformPropCount > 0),
                    eventsWithPredictions = group.Count(row => row.PredictionCount > 0),
                    eventsWithLineShopping = group.Count(row => row.LineShoppingGroupCount > 0),
                    eventsWithEligibleLineShopping = group.Count(row => row.EligibleLineShoppingGroupCount > 0)
                })
                .OrderBy(row => row.bookmaker)
                .ToArray(),
            bookmakersWithPredictions = rows
                .Where(row => row.PredictionCount > 0)
                .Select(row => row.BookmakerName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToArray(),
            bookmakersWithLineShopping = rows
                .Where(row => row.LineShoppingGroupCount > 0)
                .Select(row => row.BookmakerName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToArray(),
            bookmakersWithEligibleLineShopping = rows
                .Where(row => row.EligibleLineShoppingGroupCount > 0)
                .Select(row => row.BookmakerName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToArray(),
            missingExpectedBookmakers = expected
                .Except(seenBookmakers, StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToArray()
        };
    }

    private static object BuildTheOddsBookmakerCoverageDiagnosis(
        IReadOnlyCollection<TheOddsBookmakerCoverageAuditRow> rows)
    {
        var fanDuelRows = rows
            .Where(row => string.Equals(NormalizeTheOddsBookmakerKey(row.BookmakerKey, row.BookmakerName), "fanduel", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string likelyReason;

        if (fanDuelRows.Length == 0 || fanDuelRows.All(row => (row.RawOutcomeCount ?? 0) == 0 && row.PlatformPropCount == 0))
        {
            likelyReason = "FanDuel was not present in persisted raw payload evidence for the audited event set. It was likely not returned by The Odds API for the requested events/bookmakers/markets, or those raw payloads were never saved.";
        }
        else if (fanDuelRows.Any(row => (row.RawOutcomeCount ?? 0) > 0 && row.PlatformPropCount == 0))
        {
            likelyReason = "FanDuel appears in raw payload evidence but has no persisted platform props. Inspect normalization filters for missing player, market, line, or side fields.";
        }
        else if (fanDuelRows.Any(row => row.PlatformPropCount > 0 && row.PredictionCount == 0))
        {
            likelyReason = "FanDuel persisted platform props exist but no predictions were generated for them in the audited run context. Check historical feature availability, player mappings, and prop type support.";
        }
        else if (fanDuelRows.Any(row => row.PredictionCount > 0 && row.LineShoppingGroupCount == 0))
        {
            likelyReason = "FanDuel predictions exist but do not appear in line-shopping groups. Check event/player/prop/line grouping metadata and price availability.";
        }
        else
        {
            likelyReason = "FanDuel has persisted coverage and appears to flow through the audited pipeline where data exists.";
        }

        return new
        {
            whyFanDuelMissingMostLikelyReason = likelyReason,
            recommendedNextStep = fanDuelRows.Any(row => row.IssueFlags.Contains("noRawPayload"))
                ? "Run the event spike or a dry-run ingest for a current event with bookmakers=fanduel,draftkings,betmgm and compare the returned bookmakerBreakdown before committing."
                : "Review the per-event FanDuel issue flags in bookmakerCoverage and target the first non-empty dropout stage."
        };
    }

    private static string NormalizeTheOddsBookmakerKey(string? bookmakerKey, string? bookmakerName)
    {
        var value = !string.IsNullOrWhiteSpace(bookmakerKey) ? bookmakerKey : bookmakerName ?? string.Empty;
        var normalized = new System.Text.StringBuilder(value.Length);

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(character);
            }
        }

        return normalized.ToString() switch
        {
            "draftkings" => "draftkings",
            "fanduel" => "fanduel",
            "betmgm" => "betmgm",
            "bovada" => "bovada",
            var other when !string.IsNullOrWhiteSpace(other) => other,
            _ => "unknown"
        };
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool IsTheOddsGraded(TheOddsLineShoppingBacktestRow row)
    {
        return row.Outcome is "Win" or "Loss" or "Push";
    }

    private static decimal TheOddsHitRate(IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        var decisions = rows.Count(row => row.Outcome is "Win" or "Loss");
        return decisions == 0
            ? 0m
            : Math.Round(rows.Count(row => row.Outcome == "Win") / (decimal)decisions, 4);
    }

    private static decimal TheOddsProfit(IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        return Math.Round(rows.Sum(row => row.Profit ?? 0m), 4);
    }

    private static decimal TheOddsRoi(IReadOnlyCollection<TheOddsLineShoppingBacktestRow> rows)
    {
        var gradedCount = rows.Count(IsTheOddsGraded);
        return gradedCount == 0 ? 0m : Math.Round(TheOddsProfit(rows) / gradedCount, 4);
    }

    private static decimal? AverageNullable(IEnumerable<decimal?> values)
    {
        var presentValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return presentValues.Length == 0 ? null : Math.Round(presentValues.Average(), 6);
    }

    private static decimal AverageInt(IEnumerable<int> values)
    {
        var presentValues = values.ToArray();
        return presentValues.Length == 0 ? 0m : Math.Round((decimal)presentValues.Average(), 2);
    }

    private static string BestEvBucket(decimal? bestEstimatedEv)
    {
        if (!bestEstimatedEv.HasValue)
        {
            return "Unknown";
        }

        return bestEstimatedEv.Value switch
        {
            < 0m => "<0",
            < 0.05m => "0 to 0.05",
            < 0.10m => "0.05 to 0.10",
            < 0.20m => "0.10 to 0.20",
            _ => "0.20+"
        };
    }

    private static string ConfidenceBucket(decimal confidenceScore)
    {
        return confidenceScore switch
        {
            < 0.60m => "<0.60",
            < 0.75m => "0.60-0.75",
            < 0.90m => "0.75-0.90",
            _ => "0.90+"
        };
    }

    private static string SampleSizeBucket(int sampleSize)
    {
        return sampleSize switch
        {
            < 50 => "<50",
            < 100 => "50-100",
            < 250 => "100-250",
            _ => "250+"
        };
    }

    private static string HistoricalSampleSizeBucket(int sampleSize)
    {
        return sampleSize switch
        {
            < 25 => "<25",
            < 50 => "25-49",
            < 100 => "50-99",
            < 250 => "100-249",
            < 500 => "250-499",
            _ => "500+"
        };
    }

    private static string ProbabilityBucket(decimal? probability)
    {
        if (!probability.HasValue)
        {
            return "Unknown";
        }

        return probability.Value switch
        {
            < 0.50m => "<50%",
            < 0.55m => "50-55%",
            < 0.60m => "55-60%",
            < 0.65m => "60-65%",
            < 0.70m => "65-70%",
            < 0.75m => "70-75%",
            < 0.80m => "75-80%",
            < 0.85m => "80-85%",
            < 0.90m => "85-90%",
            _ => "90%+"
        };
    }

    private static int ProbabilityBucketSortKey(string bucket)
    {
        return bucket switch
        {
            "<50%" => 0,
            "50-55%" => 1,
            "55-60%" => 2,
            "60-65%" => 3,
            "65-70%" => 4,
            "70-75%" => 5,
            "75-80%" => 6,
            "80-85%" => 7,
            "85-90%" => 8,
            "90%+" => 9,
            _ => 99
        };
    }

    private static string ProbabilityEdgeBucket(decimal? probabilityEdge)
    {
        if (!probabilityEdge.HasValue)
        {
            return "Unknown";
        }

        return probabilityEdge.Value switch
        {
            < -0.20m => "<-20%",
            < -0.10m => "-20% to -10%",
            < -0.05m => "-10% to -5%",
            < 0m => "-5% to 0%",
            < 0.05m => "0% to 5%",
            < 0.10m => "5% to 10%",
            < 0.20m => "10% to 20%",
            _ => "20%+"
        };
    }

    private static string DecimalPriceBucket(decimal? decimalPrice)
    {
        if (!decimalPrice.HasValue)
        {
            return "Unknown";
        }

        return decimalPrice.Value switch
        {
            < 1.50m => "<1.50",
            < 1.75m => "1.50-1.74",
            < 2.00m => "1.75-1.99",
            < 2.50m => "2.00-2.49",
            < 3.00m => "2.50-2.99",
            _ => "3.00+"
        };
    }

    private static string RecommendationStrengthBucket(TheOddsLineShoppingBacktestRow row)
    {
        if (!row.BestEstimatedEv.HasValue || !row.BestProbabilityEdge.HasValue)
        {
            return "Unknown";
        }

        if (row.BestEstimatedEv.Value >= 0.20m && row.BestProbabilityEdge.Value >= 0.10m)
        {
            return "High";
        }

        if (row.BestEstimatedEv.Value >= 0.10m && row.BestProbabilityEdge.Value >= 0.05m)
        {
            return "Medium";
        }

        if (row.BestEstimatedEv.Value > 0m)
        {
            return "Low";
        }

        return "NegativeOrNoEdge";
    }

    private static string LineValueBucket(decimal line)
    {
        return line switch
        {
            0.5m => "0.5",
            1.0m => "1.0",
            1.5m => "1.5",
            2.0m => "2.0",
            2.5m => "2.5",
            3.0m => "3.0",
            3.5m => "3.5",
            4.5m => "4.5",
            5.5m => "5.5",
            _ => line.ToString("0.####", CultureInfo.InvariantCulture)
        };
    }

    private static string CanonicalTheOddsPropTypeName(string propTypeName)
    {
        return NormalizeTheOddsPropTypeName(propTypeName) switch
        {
            "RBI" => "RBIS",
            _ => NormalizeTheOddsPropTypeName(propTypeName)
        };
    }

    private static string NormalizeTheOddsPropTypeName(string propTypeName)
    {
        var builder = new System.Text.StringBuilder(propTypeName.Length);

        foreach (var character in propTypeName.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private sealed record TheOddsPlatformPropDataRow(
        Guid Id,
        Guid PropId,
        string PlatformPropId,
        string Label,
        decimal CurrentLine,
        bool OverAvailable,
        bool UnderAvailable,
        string PayoutType,
        string Status,
        DateTimeOffset ObservedAt,
        Guid? RawPayloadId,
        string PlatformName,
        string PlatformSlug,
        Guid PlayerId,
        Guid PropTypeId,
        string PlayerName,
        string PropType,
        JsonDocument? Metadata);

    private sealed record TheOddsPersistedPlatformPropRow(
        Guid PlatformPropGuid,
        Guid PropId,
        string PlatformPropId,
        string Label,
        decimal CurrentLine,
        bool OverAvailable,
        bool UnderAvailable,
        string PayoutType,
        string Status,
        DateTimeOffset ObservedAt,
        Guid? RawPayloadId,
        string PlatformName,
        string PlatformSlug,
        string? BookmakerKey,
        Guid PlayerId,
        Guid PropTypeId,
        string PlayerName,
        string PropType,
        string MarketClassification,
        string? EventId,
        DateTimeOffset? EventStartTime,
        string? HomeTeam,
        string? AwayTeam,
        string? MarketKey,
        int? PriceOver,
        int? PriceUnder,
        decimal? PriceOverDecimal,
        decimal? PriceUnderDecimal,
        TheOddsMlbBacktestResultDate BacktestResultDate,
        bool HasTheOddsDataProvider);

    private sealed record TheOddsMlbBacktestResultDate(
        DateTimeOffset? EventStartTimeUtc,
        DateOnly ExpectedResultDate,
        string ResultDateSource,
        DateOnly? UtcDate,
        DateOnly? ResolvedMlbDate);

    private sealed record TheOddsPredictionRunContext(
        Guid RunId,
        string ModelName,
        string ModelVersion,
        DateTimeOffset CreatedAt,
        string? Notes);

    private sealed record TheOddsRunScope(
        string? EventId,
        IReadOnlySet<string> Bookmakers,
        IReadOnlySet<string> Markets);

    private sealed record TheOddsRunMatch(
        bool? RunMatchesEvent,
        bool? RunMatchesBookmaker,
        bool? RunMatchesMarket);

    private sealed record TheOddsLineSnapshotDiagnosticRow(
        Guid PlatformPropGuid,
        int Count,
        DateTimeOffset LatestObservedAt);

    private sealed record TheOddsPredictionDiagnosticRow(
        Guid PropId,
        Guid PredictionRunId);

    private sealed record TheOddsPlayerRefDiagnosticRow(
        Guid PlayerId,
        int ExternalRefCount,
        bool HasMlbStatsMapping);

    private sealed record TheOddsBookmakerPredictionDropoutRow(
        string PlatformPropId,
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropTypeName,
        string RawLabel,
        string? MarketKey,
        string BookmakerName,
        string? EventId,
        string EventName,
        DateTimeOffset? EventStartTime,
        decimal Line,
        DateTimeOffset? LatestSnapshotObservedAt,
        bool HasLineSnapshot,
        bool HasPredictionInRun,
        Guid? PredictionRunId,
        Guid? EvaluatedRunId,
        string? EvaluatedRunNotes,
        bool? RunMatchesEvent,
        bool? RunMatchesBookmaker,
        bool? RunMatchesMarket,
        string? RunScopeEventId,
        IReadOnlySet<string>? RunScopeBookmakers,
        IReadOnlySet<string>? RunScopeMarkets,
        bool HistoricalFeatureAvailable,
        int HistoricalSampleSize,
        int MatchingPropResultCount,
        int PlayerExternalRefCount,
        bool HasMlbStatsMapping,
        bool MappingIssueDetected,
        bool CanonicalPropTypeMatched,
        string DropoutReason);

    private sealed record TheOddsPredictionReadinessAuditRow(
        string PlatformPropId,
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        string Bookmaker,
        string? EventId,
        string EventName,
        DateTimeOffset? EventStartTime,
        string PropType,
        string? MarketKey,
        decimal Line,
        DateTimeOffset LatestObservedAt,
        bool HasLineSnapshot,
        bool HasMlbStatsMapping,
        int PlayerExternalRefCount,
        bool CanonicalPropTypeMatched,
        int HistoricalResultCount,
        int HistoricalSampleSize,
        bool FeatureAvailable,
        bool PredictionGenerated,
        Guid? PredictionRunId,
        bool LineShoppingGroupFound,
        bool EligibleAsNormalRecommendation,
        string ReadinessStatus,
        string DropoutStage,
        string DropoutReason);

    private sealed record TheOddsMlbPlayerIdentityRow(
        Guid PlayerId,
        string DisplayName,
        string NormalizedName,
        IReadOnlyList<string> MlbStatsIds);

    private sealed record TheOddsMlbPropResultRow(
        Guid PlayerId,
        Guid PropTypeId,
        string PropTypeName,
        DateOnly EventDate,
        decimal Value);

    private sealed record TheOddsHistoricalResultSampleRow(
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropTypeName,
        DateOnly EventDate,
        decimal Value);

    private sealed record TheOddsHistoricalFeatureGapAuditRow(
        string PlatformPropId,
        Guid PlayerId,
        string PlayerName,
        string? MlbStatsId,
        string Bookmaker,
        string? EventId,
        string EventName,
        DateTimeOffset? EventStartTime,
        string PropType,
        string? MarketKey,
        decimal Line,
        bool HasLineSnapshot,
        bool HasMlbStatsMapping,
        bool CanonicalPropTypeMatched,
        bool HistoricalFeatureAvailable,
        int HistoricalSampleSize,
        string DropoutReason,
        int DirectPlayerResultCount,
        int AliasLinkedPlayerResultCount,
        int SameNamePlayerResultCount,
        int MlbStatsMappedPlayerResultCount,
        int EquivalentPropTypeResultCount,
        int PitcherStrikeoutsResultCount,
        int GenericStrikeoutsResultCount,
        int ResultsBeforeEventDate,
        int ResultsAfterEventDate,
        DateOnly? EarliestResultDate,
        DateOnly? LatestResultDate,
        IReadOnlyList<TheOddsHistoricalResultSampleRow> LatestResultsSample,
        string LikelyRootCause);

    private sealed record TheOddsMlbPlayerCandidateSourceRow(
        Guid PlayerId,
        string DisplayName,
        string LeagueCode,
        IReadOnlySet<string> NameKeys);

    private sealed record TheOddsExternalRefSummary(
        string Provider,
        string ProviderPlayerId,
        string ProviderDisplayName);

    private sealed record TheOddsPlayerPropTypeResultCountKey(
        Guid PlayerId,
        Guid PropTypeId);

    private sealed record TheOddsPlayerMappingCandidateRow(
        Guid CandidatePlayerId,
        string CandidatePlayerName,
        IReadOnlyList<TheOddsExternalRefSummary> CandidateExternalRefs,
        bool HasMlbStatsMapping,
        string? MlbStatsId,
        int HistoricalSampleSize,
        int MatchingPropResultCount,
        decimal ConfidenceScore,
        IReadOnlyList<string> MatchReasons,
        IReadOnlyList<string> RiskFlags);

    private sealed record TheOddsPlayerMappingRecoveryPreviewRow(
        Guid SourcePlayerId,
        string SourcePlayerName,
        string SourceBookmaker,
        string? EventId,
        string EventName,
        IReadOnlyList<string> PropTypesAffected,
        int PlatformPropCount,
        bool HasLineSnapshots,
        IReadOnlyList<TheOddsExternalRefSummary> CurrentExternalRefs,
        bool HasMlbStatsMapping,
        bool HistoricalFeatureAvailable,
        int HistoricalSampleSize,
        IReadOnlyList<TheOddsPlayerMappingCandidateRow> Candidates,
        string Recommendation);

    private sealed record TheOddsMlbManualMappingCandidateRow(
        Guid SourcePlayerId,
        string SourcePlayerName,
        string OriginalName,
        string NormalizedName,
        IReadOnlyList<TheOddsNameVariantRow> VariantsTried,
        int CandidatesFound,
        string SourceBookmaker,
        string? EventId,
        string EventName,
        IReadOnlyList<string> PropTypesAffected,
        int PlatformPropCount,
        int CurrentExternalRefCount,
        IReadOnlyList<TheOddsMlbStatsCandidateRow> Candidates,
        string Recommendation);

    private sealed record TheOddsMlbBulkMappingCandidateRow(
        Guid SourcePlayerId,
        string SourcePlayerName,
        string OriginalName,
        string NormalizedName,
        IReadOnlyList<TheOddsNameVariantRow> VariantsTried,
        int CandidatesFound,
        IReadOnlyList<string> AffectedBookmakers,
        IReadOnlyList<TheOddsMlbBulkAffectedEventRow> AffectedEvents,
        IReadOnlyList<string> AffectedPropTypes,
        int PlatformPropCount,
        IReadOnlyList<TheOddsExternalRefSummary> CurrentExternalRefs,
        IReadOnlyList<TheOddsMlbStatsCandidateRow> Candidates,
        string Recommendation);

    private sealed record TheOddsMlbBulkAffectedEventRow(
        string? EventId,
        string EventName,
        DateTimeOffset? EventStartTime,
        int PlatformPropCount);

    private sealed record TheOddsMlbStatsCandidateRow(
        string MlbStatsId,
        string FullName,
        string? FirstName,
        string? LastName,
        string? CurrentTeam,
        string? Position,
        bool? Active,
        decimal ConfidenceScore,
        IReadOnlyList<string> MatchReasons,
        IReadOnlyList<string> RiskFlags,
        IReadOnlyList<TheOddsMlbProviderRefConflictRow> ConflictInfo);

    private sealed record TheOddsNameVariantRow(
        string VariantType,
        string OriginalName,
        string NormalizedName);

    private sealed record TheOddsMlbProviderRefConflictRow(
        Guid PlayerId,
        string PlayerName,
        string MlbStatsId,
        string ProviderDisplayName);

    private sealed record TheOddsMlbAliasLinkPreviewRow(
        Guid SourcePlayerId,
        string SourcePlayerName,
        string OriginalName,
        string NormalizedName,
        IReadOnlyList<TheOddsNameVariantRow> VariantsTried,
        IReadOnlyList<string> AffectedBookmakers,
        IReadOnlyList<string> AffectedEvents,
        IReadOnlyList<string> AffectedPropTypes,
        int AffectedPlatformProps,
        IReadOnlyList<TheOddsExternalRefSummary> CurrentExternalRefs,
        int CandidatesFound,
        IReadOnlyList<TheOddsMlbAliasLinkCandidateRow> Candidates,
        bool AlreadyLinked,
        bool WouldResolveMissingMapping,
        string Recommendation);

    private sealed record TheOddsMlbAliasLinkCandidateRow(
        Guid TargetCanonicalPlayerId,
        string TargetCanonicalPlayerName,
        string? TargetMlbStatsId,
        IReadOnlyList<string> TargetMlbStatsIds,
        IReadOnlyList<string> MatchedNormalizedNames,
        IReadOnlyList<string> RiskFlags,
        bool AlreadyLinked);

    private sealed record TheOddsMlbPlayerAliasLinkRequest(
        Guid SourcePlayerId,
        Guid TargetCanonicalPlayerId,
        string? ApprovedBy,
        string? Reason,
        bool? DryRun);

    private sealed record TheOddsMlbAutoSafeAliasLinkRequest(
        bool? DryRun,
        string? ApprovedBy,
        string? Reason);

    private sealed record TheOddsMlbAutoSafeAliasLinkRow(
        Guid SourcePlayerId,
        string SourcePlayerName,
        Guid? TargetCanonicalPlayerId,
        string? TargetCanonicalPlayerName,
        string? TargetMlbStatsId,
        string Action,
        string Reason,
        IReadOnlyList<string> RiskFlags,
        IReadOnlyList<string> Errors,
        bool AlreadyLinked);

    private sealed record TheOddsMlbPlayerMappingApprovalRequest(
        Guid SourcePlayerId,
        string MlbStatsId,
        string? ProviderDisplayName,
        string? ApprovedBy,
        string? Reason);

    private sealed record TheOddsImpactPredictionRow(
        string Player,
        string Bookmaker,
        string PlatformPropId,
        string PropType,
        decimal Line,
        int? PriceOver,
        int? PriceUnder,
        decimal? ProbabilityOver,
        decimal? ProbabilityUnder,
        decimal RankingScore,
        string MarketClassification,
        bool IsVisibleInResearchDashboard,
        bool IsEligibleAsNormalRecommendation,
        IReadOnlyList<string> HiddenReasons,
        IReadOnlyList<string> IneligibilityReasons);

    private sealed record TheOddsLineShoppingSourceRow(
        string? EventId,
        DateTimeOffset? EventStartTime,
        string? HomeTeam,
        string? AwayTeam,
        string Player,
        string PropType,
        decimal Line,
        string MarketClassification,
        bool IsEligibleAsNormalRecommendation,
        decimal? ModelProbabilityOver,
        decimal? ModelProbabilityUnder,
        decimal? ModelProbabilityPush,
        decimal RankingScore,
        string Bookmaker,
        string PlatformPropId,
        DateTimeOffset ObservedAt,
        bool OverAvailable,
        bool UnderAvailable,
        int? OverAmericanPrice,
        decimal? OverDecimalPrice,
        int? UnderAmericanPrice,
        decimal? UnderDecimalPrice,
        decimal? OverImpliedProbability,
        decimal? UnderImpliedProbability,
        decimal? OverProbabilityEdge,
        decimal? UnderProbabilityEdge,
        decimal? OverEstimatedEv,
        decimal? UnderEstimatedEv,
        Guid PlayerId,
        Guid PropTypeId,
        DateOnly EventDate,
        DateTimeOffset? EventStartTimeUtc,
        DateOnly ExpectedResultDate,
        string ResultDateSource,
        DateOnly? UtcDate,
        DateOnly? ResolvedMlbDate,
        int HistoricalSampleSize,
        string ProbabilityMethod,
        decimal ConfidenceScore);

    private sealed record TheOddsLineShoppingGroupKey(
        string? EventId,
        DateTimeOffset? EventStartTime,
        string? HomeTeam,
        string? AwayTeam,
        string Player,
        string NormalizedPropType,
        decimal Line,
        string MarketClassification);

    private sealed record TheOddsBookmakerPriceRow(
        string Bookmaker,
        string PlatformPropId,
        DateTimeOffset ObservedAt,
        bool OverAvailable,
        bool UnderAvailable,
        int? OverAmericanPrice,
        decimal? OverDecimalPrice,
        int? UnderAmericanPrice,
        decimal? UnderDecimalPrice,
        decimal? OverImpliedProbability,
        decimal? UnderImpliedProbability,
        decimal? OverProbabilityEdge,
        decimal? UnderProbabilityEdge,
        decimal? OverEstimatedEv,
        decimal? UnderEstimatedEv);

    private sealed record BestSideSelection(
        string Side,
        string? Bookmaker,
        int? AmericanPrice,
        decimal? DecimalPrice,
        decimal? EstimatedEv,
        decimal? Probability,
        decimal? ImpliedProbability,
        decimal? ProbabilityEdge);

    private sealed record TheOddsLineShoppingLifecycle(
        double MinutesSinceObserved,
        double? MinutesUntilStart,
        bool IsFresh,
        bool IsStale,
        bool IsExpired,
        string Status,
        IReadOnlyList<string> Reasons);

    private sealed record TheOddsLineShoppingStrategyClassification(
        string StrategyClassification,
        string StrategyTier,
        IReadOnlyList<string> StrategyReasons,
        IReadOnlyList<string> StrategyWarnings,
        bool IsCandidate,
        bool IsAvoid,
        bool IsWatchlist,
        bool IsResearchOnly,
        string? CandidateStrategyName,
        string MarketShape,
        IReadOnlyList<string> RiskTags);

    private sealed record TheOddsCurrentRecommendationLineShoppingResult(
        IReadOnlyList<TheOddsLineShoppingSourceRow> Rows,
        IReadOnlyList<TheOddsLineShoppingGroup> Groups,
        IReadOnlyList<object> ResponseGroups,
        object[] StrategyClassificationSummary);

    private sealed record TheOddsLineShoppingGroup(
        string? EventId,
        DateTimeOffset? EventStartTime,
        DateTimeOffset LastObservedAt,
        double MinutesSinceObserved,
        double? MinutesUntilStart,
        bool IsFresh,
        bool IsStale,
        bool IsExpired,
        string LifecycleStatus,
        IReadOnlyList<string> LifecycleReasons,
        string? HomeTeam,
        string? AwayTeam,
        string Player,
        string NormalizedPropType,
        decimal Line,
        string MarketClassification,
        Guid PlayerId,
        Guid PropTypeId,
        DateOnly EventDate,
        DateTimeOffset? EventStartTimeUtc,
        DateOnly ExpectedResultDate,
        string ResultDateSource,
        DateOnly? UtcDate,
        DateOnly? ResolvedMlbDate,
        bool IsEligibleAsNormalRecommendation,
        decimal? ModelProbabilityOver,
        decimal? ModelProbabilityUnder,
        decimal? ModelProbabilityPush,
        decimal RankingScore,
        int HistoricalSampleSize,
        string ProbabilityMethod,
        decimal ConfidenceScore,
        string SampleWarning,
        string TrustLevel,
        IReadOnlyList<TheOddsBookmakerPriceRow> BookmakerPrices,
        string? BestOverBookmaker,
        int? BestOverAmericanPrice,
        decimal? BestOverEstimatedEv,
        string? BestUnderBookmaker,
        int? BestUnderAmericanPrice,
        decimal? BestUnderEstimatedEv,
        decimal? BestEstimatedEv,
        decimal? LargestPriceSpread,
        string BestSide,
        string? BestBookmaker,
        int? BestAmericanPrice,
        decimal? BestDecimalPrice,
        decimal? BestProbability,
        decimal? BestImpliedProbability,
        decimal? BestProbabilityEdge);

    private readonly record struct TheOddsActualResultKey(
        Guid PlayerId,
        Guid PropTypeId,
        DateOnly EventDate);

    private sealed record TheOddsLineShoppingBacktestRow(
        string Player,
        string PropType,
        string MarketClassification,
        decimal Line,
        string? EventId,
        DateTimeOffset? EventStartTime,
        string? HomeTeam,
        string? AwayTeam,
        string EventName,
        DateTimeOffset? EventStartTimeUtc,
        DateOnly ExpectedResultDate,
        string ResultDateSource,
        DateOnly? UtcDate,
        DateOnly? ResolvedMlbDate,
        string BestSide,
        string? BestBookmaker,
        int? BestAmericanPrice,
        decimal? BestDecimalPrice,
        decimal? BestEstimatedEv,
        decimal? BestProbability,
        decimal? BestImpliedProbability,
        decimal? BestProbabilityEdge,
        string TrustLevel,
        string SampleWarning,
        string LifecycleStatus,
        int HistoricalSampleSize,
        decimal ConfidenceScore,
        decimal RankingScore,
        decimal? ActualResult,
        string? Outcome,
        decimal? Profit,
        string UngradedReason);

    private sealed record TheOddsBacktestGroupSummary(
        string dimension,
        string key,
        string group,
        int totalGroups,
        int count,
        int gradedGroups,
        int gradedCount,
        int ungradedGroups,
        int wins,
        int losses,
        int pushes,
        decimal hitRate,
        decimal simulatedProfit,
        decimal simulatedRoi,
        decimal? averageBestEstimatedEv,
        decimal? averageBestProbability,
        decimal? averageBestImpliedProbability,
        decimal? averageProbabilityEdge,
        decimal averageHistoricalSampleSize,
        decimal? averageBestPrice);

    private sealed record TheOddsStrategySimulationSummary(
        string strategyName,
        string description,
        int totalGroups,
        int gradedGroups,
        int wins,
        int losses,
        int pushes,
        decimal hitRate,
        decimal simulatedProfit,
        decimal simulatedRoi,
        decimal? averageBestEstimatedEv,
        decimal? averageBestProbability,
        decimal? averageBestImpliedProbability,
        decimal? averageProbabilityEdge,
        decimal averageHistoricalSampleSize,
        decimal? averageBestPrice);

    private sealed record TheOddsStrategyBacktestComputation(
        PredictionProbabilityRunAnalysis ProbabilityAnalysis,
        IReadOnlyList<TheOddsLineShoppingBacktestRow> BacktestRows);

    private sealed record TheOddsEventPredictionWorkflowRow(
        Guid PropId,
        Guid PredictionRunId,
        DateTimeOffset PredictionRunCreatedAt);

    private sealed record TheOddsEventWorkflowStatusRow(
        string EventId,
        string? HomeTeam,
        string? AwayTeam,
        DateTimeOffset? EventStartTime,
        DateTime? EventDate,
        IReadOnlyCollection<string> BookmakersIngested,
        IReadOnlyCollection<string> MarketsIngested,
        int PlatformPropCount,
        int LineSnapshotCount,
        DateTimeOffset? LatestObservedAt,
        IReadOnlyCollection<Guid> PredictionRunIds,
        Guid? LatestPredictionRunId,
        DateTimeOffset? LatestPredictionRunCreatedAt,
        int PredictionCount,
        int DistinctPredictedPlatformProps,
        int AllTimePredictionCount,
        int LineShoppingGroupCount,
        int EligibleLineShoppingGroupCount,
        int BacktestTotalGroups,
        int BacktestGradedGroups,
        int BacktestUngradedGroups,
        string WorkflowStatus);

    private sealed record TheOddsMlbIngestionPlanRow(
        string EventId,
        string EventName,
        DateTimeOffset? EventStartTime,
        string? HomeTeam,
        string? AwayTeam,
        string Status,
        int PlatformPropCount,
        int PredictionCount,
        Guid? LatestPredictionRunId,
        DateTimeOffset? LatestPredictionRunCreatedAt,
        int LatestRunPredictionCount,
        int DistinctPredictedPlatformProps,
        int AllTimePredictionCount,
        int LineShoppingGroupCount,
        bool HasResults,
        int BacktestTotalGroups,
        int BacktestGradedGroups,
        int MissingMappingCount,
        int MissingHistoricalResultsCount,
        bool CoverageOpportunity,
        bool DataQualityWarning,
        string AttentionSeverity,
        bool NeedsIngestion,
        bool NeedsPrediction,
        bool NeedsLineShopping,
        bool NeedsResults,
        bool NeedsBacktest,
        bool NeedsAttention,
        IReadOnlyList<string> MissingRequestedBookmakers,
        IReadOnlyList<string> MissingRequestedMarkets,
        IReadOnlyList<string> Reasons);

    private sealed record TheOddsRawBookmakerCoverageRow(
        string EventId,
        string? BookmakerKey,
        string? BookmakerName,
        int RawOutcomeCount,
        IReadOnlyCollection<string> MarketsPresent);

    private sealed record TheOddsBookmakerCoverageAuditRow(
        string EventId,
        string? HomeTeam,
        string? AwayTeam,
        DateTimeOffset? EventStartTime,
        string? BookmakerKey,
        string BookmakerName,
        int? RawOutcomeCount,
        int NormalizedRecordCount,
        int PlatformPropCount,
        int LineSnapshotCount,
        int PredictionCount,
        int LineShoppingGroupCount,
        int EligibleLineShoppingGroupCount,
        IReadOnlyCollection<string> MarketsPresent,
        IReadOnlyCollection<string> PropTypesPresent,
        int PlayersPresentCount,
        DateTimeOffset? LatestObservedAt,
        IReadOnlyList<string> IssueFlags);

    private sealed record ExpectedBookmaker(string Key, string Name);
}

public sealed record TheOddsApiMlbIngestEventRequest(
    string EventId,
    string? Bookmakers,
    string? Markets);
