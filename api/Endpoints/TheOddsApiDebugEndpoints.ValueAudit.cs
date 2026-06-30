using System.Globalization;
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
    private const decimal ValueAuditHighProbabilityThreshold = 0.65m;
    private const int ValueAuditHeavyJuiceAmericanPrice = -250;
    private const int ValueAuditTooManyTopPicksThreshold = 100;
    private const decimal ValueAuditTinyEvThreshold = 0.02m;
    private const decimal ValueAuditPremiumEvThreshold = 0.05m;
    private const decimal ValueAuditMarginalProjectionEdgeThreshold = 0.25m;

    internal static async Task<object> BuildLikelyHitValueAuditAsync(
        string? sport,
        string? competition,
        string? propType,
        string? playerName,
        string? providerCategory,
        int? take,
        IPredictionProbabilityService probabilityService,
        ILikelyHitModelService likelyHitModel,
        PredictionGenerationOptions predictionGenerationOptions,
        ISoccerWorldCupPredictionService soccerPredictionService,
        SoccerPredictionsOptions soccerPredictionOptions,
        SoccerGradingOptions soccerGradingOptions,
        SoccerTopPicksOptions soccerTopPicksOptions,
        TopPicksModelOptions topPicksModelOptions,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var requestedTake = Math.Clamp(take ?? 500, 1, 1000);
        var normalizedSport = NormalizeSelectedSideSportFilter(sport);
        var normalizedProviderCategory = NormalizeSelectedSideProviderCategoryFilter(providerCategory);
        var currentTopPickDiagnostics = await BuildTopPickSelectedSideDiagnosticRowsAsync(
            normalizedSport,
            competition,
            propType,
            playerName,
            normalizedProviderCategory,
            requestedTake,
            dbContext,
            cancellationToken);
        var currentTopPickRows = currentTopPickDiagnostics
            .Select(row => BuildCurrentValueAuditRow(row))
            .ToArray();
        currentTopPickRows = AddCurrentBoardSelectivityDiagnostics(currentTopPickRows);
        var historicalRows = await BuildHistoricalValueAuditRowsAsync(
            normalizedSport,
            propType,
            playerName,
            normalizedProviderCategory,
            requestedTake,
            currentTopPickDiagnostics,
            dbContext,
            cancellationToken);
        var rejectedOrNotPromotedRows = await BuildPositiveEvRejectedOrNotPromotedRowsAsync(
            normalizedSport,
            competition,
            propType,
            playerName,
            normalizedProviderCategory,
            requestedTake: 50,
            currentTopPickRows,
            dbContext,
            cancellationToken);
        var currentSelectivityDiagnostics = BuildValueAuditSelectivityDiagnostics(currentTopPickRows, historicalRows);
        var currentVolumeDiagnostics = BuildValueAuditVolumeDiagnostics(currentTopPickRows);
        var currentWarningFlags = BuildValueAuditWarningFlags(currentTopPickRows);
        var currentLowValueDiagnostics = BuildValueAuditLowValueVolumeDiagnostics(currentTopPickRows);
        var currentReconciliation = BuildValueAuditCurrentReconciliation(currentTopPickRows);
        var currentCurationComparison = await BuildValueAuditCurationComparisonAsync(
            currentTopPickRows,
            normalizedSport,
            competition,
            propType,
            playerName,
            normalizedProviderCategory,
            requestedTake,
            probabilityService,
            likelyHitModel,
            predictionGenerationOptions,
            soccerPredictionService,
            soccerPredictionOptions,
            soccerGradingOptions,
            soccerTopPicksOptions,
            topPicksModelOptions,
            dbContext,
            cancellationToken);

        return new
        {
            generatedAtUtc,
            readOnly = true,
            callsExternalApis = false,
            writesRows = false,
            source = "persistedMarketSnapshotsAndRecommendationLedger",
            modelBehaviorChanged = false,
            sideBalanceGoal = false,
            valueAuditPurpose = "Audit price quality and realized value for the current promoted LikelyHit baseline without changing selection behavior.",
            roiSemantics = new
            {
                estimatedEv = "Pre-game estimated value for the selected side: selectedSideProbability * decimalOdds - 1 when usable decimal odds exist.",
                flatEvenMoneyRoi = "Outcome ROI at even money: +1 for Won, -1 for Lost, 0 for Push.",
                priceAdjustedRoi = "Outcome ROI for 1 unit risked using logged decimal odds when available, or an explicitly labeled fallback price when only implied probability is stored."
            },
            diagnosticOnlyClassifications = new
            {
                note = "Volume/selectivity tiers and simulated board-size cuts are diagnostic-only. They do not change public Top Picks, ranking, thresholds, side selection, guards, or customer labels.",
                rankSemantics = "rank=1 is the strongest current audit row by persisted topPickScore when present, then selected-side probability, estimated EV, probability edge, and stable identity. rankPercentile is rank divided by total current rows, so lower values are more selective.",
                internalTiers = new[]
                {
                    "premiumTopPick",
                    "valueCandidate",
                    "likelyHit",
                    "broadCandidate",
                    "monitorOnly"
                }
            },
            filters = new
            {
                sport = normalizedSport,
                competition,
                propType,
                playerName,
                providerCategory = normalizedProviderCategory,
                take = requestedTake
            },
            currentTopPicks = new
            {
                summary = BuildValueAuditSummary(currentTopPickRows),
                warningFlags = currentWarningFlags,
                reconciliation = currentReconciliation,
                volumeDiagnostics = currentVolumeDiagnostics,
                selectivityDiagnostics = currentSelectivityDiagnostics,
                lowValueVolumeDiagnostics = currentLowValueDiagnostics,
                curationComparison = currentCurationComparison,
                diagnosticLists = BuildValueAuditDiagnosticLists(currentTopPickRows, rejectedOrNotPromotedRows),
                rows = currentTopPickRows
            },
            historicalGraded = new
            {
                summary = BuildValueAuditSummary(historicalRows),
                diagnosticLists = BuildValueAuditDiagnosticLists(historicalRows, []),
                rows = historicalRows
            },
            rejectedOrNotPromotedPositiveEv = new
            {
                source = "latestLineShoppingSnapshotsWhenAvailable",
                note = "Rows here are active persisted line-shopping rows with positive estimated EV that are not present in the current Top Pick snapshot identity set. This is diagnostic only and does not change public promotion.",
                rows = rejectedOrNotPromotedRows
            }
        };
    }

    private static ValueAuditRow BuildCurrentValueAuditRow(SelectedSideDiagnosticRow row)
    {
        var effectivePrice = ResolveValueAuditPrice(row.BestAmericanPrice, row.BestDecimalPrice, row.ImpliedProbability, allowImpliedFallback: false);
        var estimatedEv = CalculateValueAuditEstimatedEv(row.SelectedSideProbability, effectivePrice.EffectiveDecimalPrice);
        return BuildValueAuditRow(
            row,
            Source: "currentTopPick",
            EstimatedEv: estimatedEv,
            PersistedEstimatedEv: row.BestEstimatedEv,
            Price: effectivePrice,
            EventDate: row.ExpectedResultDate,
            GradedDate: null,
            Result: null,
            NoResultReason: null);
    }

    private static ValueAuditRow BuildHistoricalValueAuditRow(
        RecommendationSnapshot row,
        SelectedSideDiagnosticRow? matchingTopPick)
    {
        var price = matchingTopPick is not null
            ? ResolveValueAuditPrice(matchingTopPick.BestAmericanPrice, matchingTopPick.BestDecimalPrice, row.ImpliedProbability, allowImpliedFallback: true)
            : ResolveValueAuditPrice(null, null, row.ImpliedProbability, allowImpliedFallback: true);
        var selectedSideDiagnostic = matchingTopPick ?? BuildPerformanceSelectedSideDiagnosticRow(row, matchingTopPick);
        var result = NormalizeValueAuditResult(row.GradeStatus);
        var realizedProfit = CalculateValueAuditRealizedProfit(result, price.EffectiveDecimalPrice);
        var flatProfit = CalculateValueAuditFlatProfit(result);
        var estimatedEv = CalculateValueAuditEstimatedEv(row.HitChance, price.EffectiveDecimalPrice) ?? row.EstimatedEv;

        return BuildValueAuditRow(
            selectedSideDiagnostic,
            Source: "historicalPerformanceLedger",
            EstimatedEv: estimatedEv,
            PersistedEstimatedEv: row.EstimatedEv,
            Price: price,
            EventDate: ResolveValueAuditEventDate(row),
            GradedDate: row.ExpectedResultDate,
            Result: result,
            NoResultReason: result == "NoResult" ? row.GradingFailureReason : null,
            RealizedProfitAtLoggedPrice: realizedProfit,
            RealizedRoiAtLoggedPrice: realizedProfit,
            FlatUnitProfit: flatProfit,
            FlatUnitRoi: flatProfit,
            LedgerCapturedAtUtc: row.CapturedAtUtc,
            LedgerGradedAtUtc: row.GradedAtUtc);
    }

    private static ValueAuditRow BuildValueAuditRow(
        SelectedSideDiagnosticRow row,
        string Source,
        decimal? EstimatedEv,
        decimal? PersistedEstimatedEv,
        ValueAuditPriceResolution Price,
        DateOnly? EventDate,
        DateOnly? GradedDate,
        string? Result,
        string? NoResultReason,
        decimal? RealizedProfitAtLoggedPrice = null,
        decimal? RealizedRoiAtLoggedPrice = null,
        decimal? FlatUnitProfit = null,
        decimal? FlatUnitRoi = null,
        DateTimeOffset? LedgerCapturedAtUtc = null,
        DateTimeOffset? LedgerGradedAtUtc = null,
        string? notPromotedReason = null)
    {
        var projectionMinusLine = row.ProjectedValue.HasValue && row.Line.HasValue
            ? Math.Round(row.ProjectedValue.Value - row.Line.Value, 6)
            : (decimal?)null;
        var riskFlags = BuildValueRiskFlags(row, EstimatedEv, Price, projectionMinusLine);

        return new ValueAuditRow(
            Source,
            row.Sport,
            row.Competition,
            row.PlayerName,
            row.PropType,
            row.Line,
            row.BestSide,
            row.ModelOverProbability,
            row.ModelUnderProbability,
            row.SelectedSideProbability,
            row.RawSelectedSideProbability,
            row.CalibratedSelectedSideProbability,
            row.ImpliedProbability,
            row.BestProbabilityEdge,
            EstimatedEv,
            PersistedEstimatedEv,
            row.BestAmericanPrice,
            row.BestDecimalPrice,
            row.BestPriceBookmaker,
            row.BestPriceBookmaker ?? row.Bookmaker ?? row.Provider,
            row.ProviderCategory,
            row.ProjectedValue,
            row.ProjectedValue,
            projectionMinusLine,
            row.ProjectionSource,
            row.ProbabilitySource,
            row.ProbabilityModelVersion,
            row.EvCalibrationMethod,
            row.PriceGateStatus,
            row.PriceGateReason,
            row.EvSuppressedReason,
            row.ChosenSideReason,
            row.OverRejectedReason,
            row.UnderRejectedReason,
            row.SelectedSideHistoryConsistencyGuardApplied,
            row.SelectedSideHistoryConsistencyGuardPassed,
            row.FatalSelectedSideHistoryConsistencyCodes,
            row.LineBucket,
            BuildValueAuditPriceBucket(row.BestAmericanPrice, Price.EffectiveDecimalPrice),
            BuildValueAuditProbabilityBucket(row.ImpliedProbability),
            BuildValueAuditProbabilityBucket(row.SelectedSideProbability),
            BuildValueAuditEstimatedEvBucket(EstimatedEv),
            riskFlags.FirstOrDefault() ?? "None",
            riskFlags,
            EventDate,
            GradedDate,
            Result,
            RealizedProfitAtLoggedPrice,
            RealizedRoiAtLoggedPrice,
            FlatUnitProfit,
            FlatUnitRoi,
            Price.LoggedAmericanPrice,
            Price.LoggedDecimalPrice,
            Price.EffectiveDecimalPrice,
            Price.PriceStatus,
            Price.PriceWasMissing,
            Price.PriceWasFallback,
            Price.PriceWasSynthetic,
            NoResultReason,
            LedgerCapturedAtUtc,
            LedgerGradedAtUtc,
            notPromotedReason,
            row.EventId,
            row.EventName,
            row.HomeTeam,
            row.AwayTeam,
            row.TeamName,
            row.SlateDate,
            row.TopPickScore,
            Rank: null,
            RankPercentile: null,
            RankPercentileBucket: "Unranked",
            BuildValueAuditTopPickScoreBucket(row.TopPickScore),
            BuildValueAuditProbabilityEdgeBucket(row.BestProbabilityEdge),
            BuildValueAuditProjectionMinusLineBucket(projectionMinusLine),
            BuildValueAuditDiagnosticTier(row.BestSide, row.SelectedSideProbability, EstimatedEv, row.BestAmericanPrice, Price, projectionMinusLine, row.SelectedSideHistoryConsistencyGuardPassed),
            BuildValueAuditHypotheticalThresholds(row.BestSide, row.SelectedSideProbability, EstimatedEv, row.BestAmericanPrice, Price, projectionMinusLine, row.SelectedSideHistoryConsistencyGuardPassed));
    }

    private static async Task<ValueAuditRow[]> BuildHistoricalValueAuditRowsAsync(
        string? normalizedSport,
        string? propType,
        string? playerName,
        string? normalizedProviderCategory,
        int requestedTake,
        IReadOnlyCollection<SelectedSideDiagnosticRow> currentTopPickRows,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var matchingTopPickRows = currentTopPickRows
            .GroupBy(BuildSelectedSideDiagnosticIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var query = dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(row => row.IsTopPick && !row.IsExcludedFromPerformance)
            .Where(row => row.GradeStatus == "Win"
                || row.GradeStatus == "Loss"
                || row.GradeStatus == "Push"
                || row.GradeStatus == "Won"
                || row.GradeStatus == "Lost"
                || row.GradeStatus == "Pushed"
                || row.GradeStatus == "NoResult");

        if (!IsSelectedSideAllFilter(normalizedSport))
        {
            query = query.Where(row => row.Sport == normalizedSport);
        }

        if (!IsSelectedSideAllFilter(normalizedProviderCategory))
        {
            query = query.Where(row => row.ProviderCategory == normalizedProviderCategory);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            var normalizedPropType = RecommendationLedgerWriter.NormalizeKey(propType);
            query = query.Where(row => row.PropType == propType || row.NormalizedPropType == normalizedPropType);
        }

        if (!string.IsNullOrWhiteSpace(playerName))
        {
            query = query.Where(row => row.PlayerName.Contains(playerName));
        }

        var rows = await query
            .OrderByDescending(row => row.ExpectedResultDate)
            .ThenBy(row => row.PlayerName)
            .ThenBy(row => row.PropType)
            .Take(requestedTake)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                matchingTopPickRows.TryGetValue(BuildPerformanceSelectedSideIdentityKey(row), out var matchingTopPick);
                return BuildHistoricalValueAuditRow(row, matchingTopPick);
            })
            .ToArray();
    }

    private static async Task<ValueAuditRow[]> BuildPositiveEvRejectedOrNotPromotedRowsAsync(
        string? normalizedSport,
        string? competition,
        string? propType,
        string? playerName,
        string? normalizedProviderCategory,
        int requestedTake,
        IReadOnlyCollection<ValueAuditRow> currentTopPickRows,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var topPickKeys = currentTopPickRows
            .Select(BuildValueAuditOpportunityKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = dbContext.MarketLineShoppingSnapshots.AsNoTracking();

        if (!IsSelectedSideAllFilter(normalizedSport))
        {
            query = query.Where(row => row.Sport == normalizedSport);
        }

        if (!IsSelectedSideAllFilter(normalizedProviderCategory))
        {
            query = query.Where(row => row.ProviderCategory == normalizedProviderCategory);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            query = query.Where(row => row.PropType == propType);
        }

        if (!string.IsNullOrWhiteSpace(playerName))
        {
            query = query.Where(row => row.Player.Contains(playerName));
        }

        var latestRunCandidates = await query
            .Select(row => new { row.Sport, row.ProviderCategory, row.TheOddsRefreshRunId, row.GeneratedAtUtc })
            .ToArrayAsync(cancellationToken);
        var latestRunIds = latestRunCandidates
            .GroupBy(row => $"{row.Sport}|{row.ProviderCategory}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.GeneratedAtUtc).First().TheOddsRefreshRunId)
            .ToHashSet();

        if (latestRunIds.Count == 0)
        {
            return [];
        }

        var snapshots = await query
            .Where(row => latestRunIds.Contains(row.TheOddsRefreshRunId))
            .Where(row => row.LifecycleStatus == "Active" && !row.IsStale && !row.IsExpired)
            .Where(row => row.BestEstimatedEv > 0m || row.BestProbabilityEdge > 0m)
            .OrderByDescending(row => row.BestEstimatedEv)
            .ThenByDescending(row => row.BestProbabilityEdge)
            .ThenBy(row => row.Player)
            .Take(Math.Max(requestedTake * 4, 100))
            .ToArrayAsync(cancellationToken);

        return snapshots
            .Select(row => BuildLikelyHitReplaySnapshotRow(row, competition))
            .Where(row => string.IsNullOrWhiteSpace(competition) ||
                string.Equals(ReadLikelyHitString(row, "competition"), competition, StringComparison.OrdinalIgnoreCase))
            .Select(row => BuildSelectedSideDiagnosticRow(row, "lineShoppingSnapshot", "lineShoppingSnapshotDetails"))
            .Select(row => BuildValueAuditRow(
                row,
                Source: "positiveEvRejectedOrNotPromoted",
                EstimatedEv: CalculateValueAuditEstimatedEv(row.SelectedSideProbability, row.BestDecimalPrice) ?? row.BestEstimatedEv,
                PersistedEstimatedEv: row.BestEstimatedEv,
                Price: ResolveValueAuditPrice(row.BestAmericanPrice, row.BestDecimalPrice, row.ImpliedProbability, allowImpliedFallback: false),
                EventDate: row.ExpectedResultDate,
                GradedDate: null,
                Result: null,
                NoResultReason: null,
                notPromotedReason: "LatestLineShoppingRowNotPresentInCurrentTopPickSnapshotSet"))
            .Where(row => row.EstimatedEv is > 0m)
            .Where(row => !topPickKeys.Contains(BuildValueAuditOpportunityKey(row)))
            .Take(requestedTake)
            .ToArray();
    }

    private static ValueAuditRow[] AddCurrentBoardSelectivityDiagnostics(IReadOnlyCollection<ValueAuditRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var total = rows.Count;
        return rows
            .OrderByDescending(row => row.TopPickScore ?? decimal.MinValue)
            .ThenByDescending(row => row.SelectedSideProbability ?? decimal.MinValue)
            .ThenByDescending(row => row.EstimatedEv ?? decimal.MinValue)
            .ThenByDescending(row => row.ProbabilityEdge ?? decimal.MinValue)
            .ThenBy(row => row.Sport, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.EventDate)
            .ThenBy(row => row.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.PropType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.BestSide, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Line)
            .ThenBy(row => row.PriceBookmakerProvider, StringComparer.OrdinalIgnoreCase)
            .Select((row, index) =>
            {
                var rank = index + 1;
                var rankPercentile = Math.Round(rank / (decimal)total, 6);
                return row with
                {
                    Rank = rank,
                    RankPercentile = rankPercentile,
                    RankPercentileBucket = BuildValueAuditRankPercentileBucket(rankPercentile)
                };
            })
            .ToArray();
    }

    private static object BuildValueAuditSummary(IReadOnlyCollection<ValueAuditRow> rows)
    {
        return new
        {
            metrics = BuildValueAuditMetrics(rows),
            warningFlags = BuildValueAuditWarningFlags(rows),
            groupings = new
            {
                bySide = BuildValueAuditGroups(rows, row => row.BestSide),
                byPropType = BuildValueAuditGroups(rows, row => row.PropType),
                byPropFamily = BuildValueAuditGroups(rows, row => BuildValueAuditPropFamily(row.PropType)),
                byLineBucket = BuildValueAuditGroups(rows, row => row.LineBucket),
                byPriceBucket = BuildValueAuditGroups(rows, row => row.PriceBucket),
                byImpliedProbabilityBucket = BuildValueAuditGroups(rows, row => row.ImpliedProbabilityBucket),
                bySelectedSideProbabilityBucket = BuildValueAuditGroups(rows, row => row.ModelProbabilityBucket),
                byProbabilityEdgeBucket = BuildValueAuditGroups(rows, row => row.ProbabilityEdgeBucket),
                byEstimatedEvBucket = BuildValueAuditGroups(rows, row => row.EstimatedEvBucket),
                byProjectionMinusLineBucket = BuildValueAuditGroups(rows, row => row.ProjectionMinusLineBucket),
                byTopPickScoreBucket = BuildValueAuditGroups(rows, row => row.TopPickScoreBucket),
                byRankPercentileBucket = BuildValueAuditGroups(rows, row => row.RankPercentileBucket),
                byInternalDiagnosticTier = BuildValueAuditGroups(rows, row => row.InternalDiagnosticTier),
                byProviderCategory = BuildValueAuditGroups(rows, row => row.ProviderCategory),
                byBookmakerProvider = BuildValueAuditGroups(rows, row => row.PriceBookmakerProvider),
                bySport = BuildValueAuditGroups(rows, row => row.Sport),
                byDate = BuildValueAuditGroups(rows, row => row.GradedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    ?? row.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                bySidePriceBucket = BuildValueAuditGroups(rows, row => $"{row.BestSide}|{row.PriceBucket}"),
                bySideEstimatedEvBucket = BuildValueAuditGroups(rows, row => $"{row.BestSide}|{row.EstimatedEvBucket}"),
                byPropTypeSide = BuildValueAuditGroups(rows, row => $"{row.PropType}|{row.BestSide}")
            }
        };
    }

    private static object BuildValueAuditDiagnosticLists(
        IReadOnlyCollection<ValueAuditRow> rows,
        IReadOnlyCollection<ValueAuditRow> positiveEvRejectedOrNotPromotedRows)
    {
        return new
        {
            highProbabilityButNonPositiveEvPicks = rows
                .Where(row => row.SelectedSideProbability >= ValueAuditHighProbabilityThreshold && row.EstimatedEv <= 0m)
                .OrderByDescending(row => row.SelectedSideProbability)
                .Take(25)
                .ToArray(),
            highProbabilityButHeavilyJuicedPicks = rows
                .Where(row => row.SelectedSideProbability >= ValueAuditHighProbabilityThreshold && row.BestAmericanPrice <= ValueAuditHeavyJuiceAmericanPrice)
                .OrderBy(row => row.BestAmericanPrice)
                .Take(25)
                .ToArray(),
            positiveEvPicksRejectedOrNotPromoted = positiveEvRejectedOrNotPromotedRows.Take(25).ToArray(),
            underPicksWithNonPositiveEv = rows
                .Where(row => IsSelectedSide(row.BestSide, "Under") && row.EstimatedEv <= 0m)
                .OrderBy(row => row.EstimatedEv)
                .Take(25)
                .ToArray(),
            overPicksWithPositiveEv = rows
                .Where(row => IsSelectedSide(row.BestSide, "Over") && row.EstimatedEv > 0m)
                .OrderByDescending(row => row.EstimatedEv)
                .Take(25)
                .ToArray(),
            projectionMinusLineConflictsWithSelectedSide = rows
                .Where(HasValueAuditProjectionSideConflict)
                .OrderByDescending(row => Math.Abs(row.ProjectionMinusLine ?? 0m))
                .Take(25)
                .ToArray()
        };
    }

    private static object BuildValueAuditVolumeDiagnostics(IReadOnlyCollection<ValueAuditRow> rows)
    {
        var playerGroups = BuildValueAuditExposureGroups(rows, row => row.PlayerName);
        var eventGroups = BuildValueAuditExposureGroups(rows, BuildValueAuditEventKey);
        var playerPropTypeGroups = BuildValueAuditExposureGroups(rows, row => $"{row.PlayerName}|{row.PropType}")
            .Where(group => group.count > 1)
            .ToArray();
        var playerPropFamilyGroups = BuildValueAuditExposureGroups(rows, row => $"{row.PlayerName}|{BuildValueAuditPropFamily(row.PropType)}")
            .Where(group => group.count > 1)
            .ToArray();
        var samePlayerMultiplePropTypes = rows
            .GroupBy(row => row.PlayerName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupRows = group.ToArray();
                var propTypes = groupRows
                    .Select(row => row.PropType)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToArray();
                return new
                {
                    playerName = group.Key,
                    count = groupRows.Length,
                    distinctPropTypeCount = propTypes.Length,
                    propTypes,
                    rows = groupRows.Take(10).ToArray()
                };
            })
            .Where(group => group.distinctPropTypeCount > 1)
            .OrderByDescending(group => group.count)
            .ThenBy(group => group.playerName)
            .Take(50)
            .ToArray();

        return new
        {
            totalCurrentRows = rows.Count,
            totalCurrentTopPicksBySport = BuildValueAuditCountGroups(rows, row => row.Sport),
            byEventDate = BuildValueAuditCountGroups(rows, row => row.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            bySlateDate = BuildValueAuditCountGroups(rows, row => row.SlateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            byPropType = BuildValueAuditCountGroups(rows, row => row.PropType),
            byPropFamily = BuildValueAuditCountGroups(rows, row => BuildValueAuditPropFamily(row.PropType)),
            bySide = BuildValueAuditCountGroups(rows, row => row.BestSide),
            byProviderCategory = BuildValueAuditCountGroups(rows, row => row.ProviderCategory),
            byBookmakerProvider = BuildValueAuditCountGroups(rows, row => row.PriceBookmakerProvider),
            byPlayer = BuildValueAuditCountGroups(rows, row => row.PlayerName, take: 100),
            byGameEvent = BuildValueAuditCountGroups(rows, BuildValueAuditEventKey, take: 100),
            byTeam = BuildValueAuditCountGroups(rows, BuildValueAuditTeamKey, take: 100),
            picksPerPlayer = playerGroups.Take(100).ToArray(),
            picksPerGameEvent = eventGroups.Take(100).ToArray(),
            duplicateExposureByPlayerPropType = playerPropTypeGroups,
            duplicateExposureByPlayerPropFamily = playerPropFamilyGroups,
            duplicateExposureBySamePlayerAcrossMultiplePropTypes = samePlayerMultiplePropTypes
        };
    }

    private static object BuildValueAuditSelectivityDiagnostics(
        IReadOnlyCollection<ValueAuditRow> currentRows,
        IReadOnlyCollection<ValueAuditRow> historicalRows)
    {
        var cutDefinitions = BuildValueAuditCutDefinitions(currentRows.Count);
        var cuts = cutDefinitions
            .Select(cut =>
            {
                var cutRows = currentRows
                    .Where(row => row.Rank is > 0 && row.Rank <= cut.Count)
                    .ToArray();
                var historicalProfileKeys = cutRows
                    .Select(BuildValueAuditProfileMatchKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var matchedHistoricalRows = historicalRows
                    .Where(row => historicalProfileKeys.Contains(BuildValueAuditProfileMatchKey(row)))
                    .ToArray();

                return new
                {
                    cut = cut.Name,
                    requestedCount = cut.Count,
                    candidateCount = cutRows.Length,
                    metrics = BuildValueAuditMetrics(cutRows),
                    sideDistribution = BuildValueAuditCountGroups(cutRows, row => row.BestSide),
                    propTypeDistribution = BuildValueAuditCountGroups(cutRows, row => row.PropType),
                    providerCategoryDistribution = BuildValueAuditCountGroups(cutRows, row => row.ProviderCategory),
                    historicalMatchedProfileMetrics = BuildValueAuditMetrics(matchedHistoricalRows)
                };
            })
            .ToArray();

        return new
        {
            boardSize = currentRows.Count,
            cutProfileMatchSemantics = "Historical cut metrics match prior ledger rows by sport, prop type, side, line, provider category, and price bucket. They are directional profile diagnostics, not a claim that the exact current event was already graded.",
            boardSizeCuts = cuts,
            totalRowsByCut = cuts
                .Select(cut => new { cut = cut.cut, requestedCount = cut.requestedCount, candidateCount = cut.candidateCount })
                .ToArray(),
            tierDiagnostics = new
            {
                tierRules = BuildValueAuditTierRules(),
                totalRowsByTier = BuildValueAuditCountGroups(currentRows, row => row.InternalDiagnosticTier),
                tiers = BuildValueAuditGroups(currentRows, row => row.InternalDiagnosticTier)
            },
            stricterHypotheticalThresholds = new
            {
                note = "These thresholds are diagnostics only and do not alter public Top Picks.",
                hitChanceAtLeast70Count = currentRows.Count(row => row.StricterHypotheticalThresholds.HitChanceAtLeast70),
                hitChanceAtLeast75Count = currentRows.Count(row => row.StricterHypotheticalThresholds.HitChanceAtLeast75),
                estimatedEvPositiveCount = currentRows.Count(row => row.StricterHypotheticalThresholds.EstimatedEvPositive),
                estimatedEvAtLeastFivePercentCount = currentRows.Count(row => row.StricterHypotheticalThresholds.EstimatedEvAtLeastFivePercent),
                notHeavyJuiceCount = currentRows.Count(row => row.StricterHypotheticalThresholds.NotHeavyJuice),
                premiumTopPickProfileCount = currentRows.Count(row => row.StricterHypotheticalThresholds.PremiumTopPickProfile)
            }
        };
    }

    private static object BuildValueAuditLowValueVolumeDiagnostics(IReadOnlyCollection<ValueAuditRow> rows)
    {
        var outsideTopScoreRows = rows
            .Where(row => row.RankPercentile > 0.20m)
            .ToArray();
        var marginalProjectionRows = rows
            .Where(row => row.ProjectionMinusLine.HasValue && Math.Abs(row.ProjectionMinusLine.Value) <= ValueAuditMarginalProjectionEdgeThreshold)
            .ToArray();
        var strongButExpensiveRows = rows
            .Where(row => row.SelectedSideProbability >= ValueAuditHighProbabilityThreshold && row.ImpliedProbability >= ValueAuditHighProbabilityThreshold)
            .ToArray();

        return new
        {
            highHitProbabilityNonPositiveEvCount = rows.Count(row => row.SelectedSideProbability >= ValueAuditHighProbabilityThreshold && row.EstimatedEv <= 0m),
            positiveProbabilityEdgeNonPositiveEvCount = rows.Count(row => row.ProbabilityEdge > 0m && row.EstimatedEv <= 0m),
            heavilyJuicedPickCount = rows.Count(IsValueAuditHeavyJuice),
            tinyEstimatedEvCount = rows.Count(row => row.EstimatedEv is > 0m and <= ValueAuditTinyEvThreshold),
            outsideTopTwentyPercentCount = outsideTopScoreRows.Length,
            marginalProjectedEdgeCount = marginalProjectionRows.Length,
            strongProbabilityHighImpliedProbabilityCount = strongButExpensiveRows.Length,
            repeatedPlayerCount = rows.GroupBy(row => row.PlayerName, StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1),
            repeatedGameEventCount = rows.GroupBy(BuildValueAuditEventKey, StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1),
            repeatedPlayerPropFamilyCount = rows.GroupBy(row => $"{row.PlayerName}|{BuildValueAuditPropFamily(row.PropType)}", StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1),
            samples = new
            {
                highHitProbabilityNonPositiveEv = rows
                    .Where(row => row.SelectedSideProbability >= ValueAuditHighProbabilityThreshold && row.EstimatedEv <= 0m)
                    .Take(25)
                    .ToArray(),
                positiveProbabilityEdgeNonPositiveEv = rows
                    .Where(row => row.ProbabilityEdge > 0m && row.EstimatedEv <= 0m)
                    .Take(25)
                    .ToArray(),
                heavilyJuiced = rows
                    .Where(IsValueAuditHeavyJuice)
                    .Take(25)
                    .ToArray(),
                tinyEstimatedEv = rows
                    .Where(row => row.EstimatedEv is > 0m and <= ValueAuditTinyEvThreshold)
                    .Take(25)
                    .ToArray(),
                outsideTopTwentyPercent = outsideTopScoreRows.Take(25).ToArray(),
                marginalProjectedEdge = marginalProjectionRows.Take(25).ToArray(),
                strongProbabilityHighImpliedProbability = strongButExpensiveRows.Take(25).ToArray()
            }
        };
    }

    private static object BuildValueAuditWarningFlags(IReadOnlyCollection<ValueAuditRow> rows)
    {
        var total = rows.Count;
        var sideGroups = rows.GroupBy(row => string.IsNullOrWhiteSpace(row.BestSide) ? "Unknown" : row.BestSide, StringComparer.OrdinalIgnoreCase).ToArray();
        var propTypeGroups = rows.GroupBy(row => string.IsNullOrWhiteSpace(row.PropType) ? "Unknown" : row.PropType, StringComparer.OrdinalIgnoreCase).ToArray();
        var playerGroups = rows.GroupBy(row => string.IsNullOrWhiteSpace(row.PlayerName) ? "Unknown" : row.PlayerName, StringComparer.OrdinalIgnoreCase).ToArray();
        var eventGroups = rows.GroupBy(BuildValueAuditEventKey, StringComparer.OrdinalIgnoreCase).ToArray();
        var lowEvCount = rows.Count(row => !row.EstimatedEv.HasValue || row.EstimatedEv <= ValueAuditTinyEvThreshold);
        var heavyJuiceCount = rows.Count(IsValueAuditHeavyJuice);
        var likelyHitNotValueCount = rows.Count(row => row.InternalDiagnosticTier == "likelyHit");
        var premiumCount = rows.Count(row => row.InternalDiagnosticTier == "premiumTopPick");

        bool Majority(int count) => total > 0 && count / (decimal)total >= 0.50m;
        bool Concentrated(IReadOnlyCollection<IGrouping<string, ValueAuditRow>> groups, decimal threshold, int minCount)
        {
            if (total == 0 || groups.Count == 0)
            {
                return false;
            }

            var max = groups.Max(group => group.Count());
            return max >= minCount || max / (decimal)total >= threshold;
        }

        return new
        {
            tooManyTopPicksForSlate = total > ValueAuditTooManyTopPicksThreshold,
            topPicksMostlyLowEv = Majority(lowEvCount),
            topPicksMostlyHeavyJuice = Majority(heavyJuiceCount),
            topPicksHighlyCorrelatedByPlayer = Concentrated(playerGroups, 0.10m, 5),
            topPicksHighlyCorrelatedByGame = Concentrated(eventGroups, 0.15m, 10),
            topPicksSideConcentrated = Concentrated(sideGroups, 0.75m, total + 1),
            topPicksPropTypeConcentrated = Concentrated(propTypeGroups, 0.50m, total + 1),
            topPicksMostlyLikelyHitsNotValue = Majority(likelyHitNotValueCount),
            topPicksNameMayBeTooBroad = total > ValueAuditTooManyTopPicksThreshold && (total == 0 || premiumCount / (decimal)total < 0.25m)
        };
    }

    private static object BuildValueAuditCurrentReconciliation(IReadOnlyCollection<ValueAuditRow> rows)
    {
        var totalRowsBySport = BuildValueAuditCountGroups(rows, row => row.Sport);
        var totalRowsByTier = BuildValueAuditCountGroups(rows, row => row.InternalDiagnosticTier);
        var totalRowsByCut = BuildValueAuditCutDefinitions(rows.Count)
            .Select(cut => new
            {
                cut = cut.Name,
                requestedCount = cut.Count,
                candidateCount = rows.Count(row => row.Rank is > 0 && row.Rank <= cut.Count)
            })
            .ToArray();
        return new
        {
            totalCurrentRows = rows.Count,
            totalRowsBySport,
            totalRowsByTier,
            totalRowsByCut,
            sportCountSum = totalRowsBySport.Sum(row => row.count),
            tierCountSum = totalRowsByTier.Sum(row => row.count),
            countsReconcile = totalRowsBySport.Sum(row => row.count) == rows.Count
                && totalRowsByTier.Sum(row => row.count) == rows.Count
        };
    }

    private static async Task<object> BuildValueAuditCurationComparisonAsync(
        IReadOnlyCollection<ValueAuditRow> rows,
        string? normalizedSport,
        string? competition,
        string? propType,
        string? playerName,
        string? normalizedProviderCategory,
        int requestedTake,
        IPredictionProbabilityService probabilityService,
        ILikelyHitModelService likelyHitModel,
        PredictionGenerationOptions predictionGenerationOptions,
        ISoccerWorldCupPredictionService soccerPredictionService,
        SoccerPredictionsOptions soccerPredictionOptions,
        SoccerGradingOptions soccerGradingOptions,
        SoccerTopPicksOptions soccerTopPicksOptions,
        TopPicksModelOptions options,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var opportunityGroups = rows
            .GroupBy(BuildValueAuditCuratedOpportunityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.EstimatedEv ?? decimal.MinValue)
                .ThenBy(row => row.ImpliedProbability ?? decimal.MaxValue)
                .ThenByDescending(row => row.EffectiveDecimalPrice ?? row.BestDecimalPrice ?? decimal.MinValue)
                .ThenByDescending(row => row.TopPickScore ?? decimal.MinValue)
                .ThenByDescending(row => row.SelectedSideProbability ?? decimal.MinValue)
                .ThenBy(row => row.PriceBookmakerProvider, StringComparer.OrdinalIgnoreCase)
                .ToArray())
            .Select(orderedRows => new ValueAuditCuratedOpportunity(
                BuildValueAuditCuratedOpportunityKey(orderedRows[0]),
                orderedRows[0] with
                {
                    NotPromotedReason = orderedRows.Length > 1
                        ? $"CuratedRepresentativeFromProviderFanout:{orderedRows.Length}"
                        : null
                },
                orderedRows))
            .ToArray();

        var opportunities = opportunityGroups
            .Select(group => group.Representative)
            .OrderByDescending(row => row.EstimatedEv ?? decimal.MinValue)
            .ThenBy(row => row.ImpliedProbability ?? decimal.MaxValue)
            .ThenByDescending(row => row.EffectiveDecimalPrice ?? row.BestDecimalPrice ?? decimal.MinValue)
            .ThenByDescending(row => row.TopPickScore ?? decimal.MinValue)
            .ThenByDescending(row => row.SelectedSideProbability ?? decimal.MinValue)
            .ThenBy(row => row.Sport, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.EventId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.PropType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Line)
            .ToArray();

        var selected = new List<ValueAuditRow>();
        var selectedBySport = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByPlayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByPlayerPropFamily = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByGame = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByTeam = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedByPropType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var suppressed = new List<object>();
        var suppressedByPlayer = 0;
        var suppressedByPlayerPropFamily = 0;
        var suppressedByGame = 0;
        var suppressedByTeam = 0;
        var suppressedByPropType = 0;
        var suppressedBySlateCap = 0;
        var maxPerSport = PositiveOrUnlimited(options.PublicMaxTopPicksPerSlateSport);
        var maxPerPlayer = PositiveOrUnlimited(options.PublicMaxTopPicksPerPlayer);
        var maxPerPlayerPropFamily = PositiveOrUnlimited(options.PublicMaxTopPicksPerPlayerPropFamily);
        var maxPerGame = PositiveOrUnlimited(options.PublicMaxTopPicksPerGameEvent);
        var maxPerTeam = PositiveOrUnlimited(options.PublicMaxTopPicksPerTeam);
        var maxPerPropType = PositiveOrUnlimited(options.PublicMaxTopPicksPerPropType);

        foreach (var row in opportunities)
        {
            var sportKey = RecommendationLedgerWriter.NormalizeKey(row.Sport);
            var playerKey = RecommendationLedgerWriter.NormalizeKey(row.PlayerName);
            var playerPropFamilyKey = $"{playerKey}|{BuildValueAuditPropFamily(row.PropType)}";
            var gameKey = $"{sportKey}|{BuildValueAuditEventKey(row)}";
            var teamKey = $"{sportKey}|{BuildValueAuditTeamKey(row)}";
            var propTypeKey = $"{sportKey}|{RecommendationLedgerWriter.NormalizeKey(row.PropType)}";

            string? reason = null;
            if (GetValueAuditCount(selectedBySport, sportKey) >= maxPerSport)
            {
                reason = "SlateSportCap";
                suppressedBySlateCap++;
            }
            else if (GetValueAuditCount(selectedByPlayer, playerKey) >= maxPerPlayer)
            {
                reason = "PlayerExposureCap";
                suppressedByPlayer++;
            }
            else if (GetValueAuditCount(selectedByPlayerPropFamily, playerPropFamilyKey) >= maxPerPlayerPropFamily)
            {
                reason = "PlayerPropFamilyExposureCap";
                suppressedByPlayerPropFamily++;
            }
            else if (GetValueAuditCount(selectedByGame, gameKey) >= maxPerGame)
            {
                reason = "GameExposureCap";
                suppressedByGame++;
            }
            else if (GetValueAuditCount(selectedByTeam, teamKey) >= maxPerTeam)
            {
                reason = "TeamExposureCap";
                suppressedByTeam++;
            }
            else if (GetValueAuditCount(selectedByPropType, propTypeKey) >= maxPerPropType)
            {
                reason = "PropTypeExposureCap";
                suppressedByPropType++;
            }

            if (reason is not null)
            {
                suppressed.Add(new
                {
                    reason,
                    row.Sport,
                    row.PlayerName,
                    row.PropType,
                    row.BestSide,
                    row.Line,
                    row.EventId,
                    row.EventName
                });
                continue;
            }

            selected.Add(row);
            IncrementValueAuditCount(selectedBySport, sportKey);
            IncrementValueAuditCount(selectedByPlayer, playerKey);
            IncrementValueAuditCount(selectedByPlayerPropFamily, playerPropFamilyKey);
            IncrementValueAuditCount(selectedByGame, gameKey);
            IncrementValueAuditCount(selectedByTeam, teamKey);
            IncrementValueAuditCount(selectedByPropType, propTypeKey);
        }

        var curatedRows = AddCurrentBoardSelectivityDiagnostics(selected);
        var providerFanoutSuppressed = rows.Count - opportunities.Length;
        var suppressedTotal = providerFanoutSuppressed
            + suppressedByPlayer
            + suppressedByPlayerPropFamily
            + suppressedByGame
            + suppressedByTeam
            + suppressedByPropType
            + suppressedBySlateCap;
        var countsReconcile = rows.Count == providerFanoutSuppressed
            + curatedRows.Length
            + suppressedByPlayer
            + suppressedByPlayerPropFamily
            + suppressedByGame
            + suppressedByTeam
            + suppressedByPropType
            + suppressedBySlateCap;
        var providerFanoutOpportunities = opportunityGroups
            .Where(group => group.ProviderFanoutCount > 1)
            .ToArray();
        var curatedOpportunityKeys = selected
            .Select(BuildValueAuditCuratedOpportunityKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var curatedRowsWithProviderOptionsCount = providerFanoutOpportunities
            .Count(group => curatedOpportunityKeys.Contains(group.OpportunityKey));
        var providerFanoutDiagnostics = new
        {
            scope = "BroadPostOpportunityDedupePreExposureCaps",
            description = "Diagnostic broad-board provider fanout before final public-board staging and exposure caps. This can be larger than the public Top Picks summary.",
            providerFanoutOpportunityCount = providerFanoutOpportunities.Length,
            maxProviderFanoutCount = opportunityGroups.Length == 0 ? 0 : opportunityGroups.Max(group => group.ProviderFanoutCount),
            rowsWithProviderOptionsCount = curatedRowsWithProviderOptionsCount,
            sampleProviderFanoutOpportunities = providerFanoutOpportunities
                .Take(10)
                .Select(group => new
                {
                    opportunityKey = group.OpportunityKey,
                    providerFanoutCount = group.ProviderFanoutCount,
                    player = group.Representative.PlayerName,
                    propType = group.Representative.PropType,
                    propFamily = BuildValueAuditPropFamily(group.Representative.PropType),
                    side = group.Representative.BestSide,
                    group.Representative.Line,
                    representativeBookmaker = group.Representative.PriceBookmakerProvider,
                    alternatives = group.Rows
                        .Select(row => new
                        {
                            bookmaker = row.PriceBookmakerProvider,
                            row.ProviderCategory,
                            row.BestAmericanPrice,
                            row.BestDecimalPrice,
                            row.SelectedSideProbability,
                            row.EstimatedEv
                        })
                        .ToArray()
                })
                .ToArray()
        };
        var finalPublicBoardFanoutDiagnostics = await BuildValueAuditFinalPublicBoardFanoutDiagnosticsAsync(
            rows,
            normalizedSport,
            competition,
            propType,
            playerName,
            normalizedProviderCategory,
            requestedTake,
            probabilityService,
            likelyHitModel,
            predictionGenerationOptions,
            soccerPredictionService,
            soccerPredictionOptions,
            soccerGradingOptions,
            soccerTopPicksOptions,
            options,
            dbContext,
            cancellationToken);
        var compactSummary = new
        {
            broadCandidateCount = rows.Count,
            preDedupeCandidateCount = rows.Count,
            postOpportunityDedupeCount = opportunities.Length,
            curatedTopPickCount = curatedRows.Length,
            suppressedTotal,
            suppressedProviderFanoutCount = providerFanoutSuppressed,
            suppressedByPlayerExposureCount = suppressedByPlayer,
            suppressedByPlayerPropFamilyExposureCount = suppressedByPlayerPropFamily,
            suppressedByGameExposureCount = suppressedByGame,
            suppressedBySlateCapCount = suppressedBySlateCap,
            suppressedByTeamExposureCount = suppressedByTeam,
            suppressedByPropTypeExposureCount = suppressedByPropType,
            countsReconcile,
            hasProviderFanoutAlternatives = providerFanoutOpportunities.Length > 0,
            curatedRowsWithProviderOptionsCount,
            finalPublicBoardProviderFanoutOpportunityCount = finalPublicBoardFanoutDiagnostics.ProviderFanoutOpportunityCount,
            finalPublicBoardMaxProviderFanoutCount = finalPublicBoardFanoutDiagnostics.MaxProviderFanoutCount,
            finalPublicBoardRowsWithProviderOptionsCount = finalPublicBoardFanoutDiagnostics.RowsWithProviderOptionsCount
        };

        return new
        {
            diagnosticOnly = true,
            note = "This mirrors public opportunity-level Top Picks curation for audit reconciliation only. It does not change model scoring, thresholds, side selection, or this endpoint's broad row list.",
            summary = compactSummary,
            providerFanoutDiagnostics,
            preCapProviderFanoutDiagnostics = providerFanoutDiagnostics,
            finalPublicBoardFanoutDiagnostics,
            broadCandidateBoard = new
            {
                rowCount = rows.Count,
                summary = BuildValueAuditSummary(rows),
                volumeDiagnostics = BuildValueAuditVolumeDiagnostics(rows)
            },
            curatedTopPicks = new
            {
                preDedupeCandidateCount = rows.Count,
                postOpportunityDedupeCount = opportunities.Length,
                curatedTopPickCount = curatedRows.Length,
                suppressedProviderFanoutCount = providerFanoutSuppressed,
                suppressedByPlayerExposureCount = suppressedByPlayer,
                suppressedByPlayerPropFamilyExposureCount = suppressedByPlayerPropFamily,
                suppressedByGameExposureCount = suppressedByGame,
                suppressedByTeamExposureCount = suppressedByTeam,
                suppressedByPropTypeExposureCount = suppressedByPropType,
                suppressedBySlateCapCount = suppressedBySlateCap,
                providerFanoutDiagnostics,
                preCapProviderFanoutDiagnostics = providerFanoutDiagnostics,
                finalPublicBoardFanoutDiagnostics,
                suppressionReasonBreakdown = suppressed
                    .Select(row => row.GetType().GetProperty("reason")?.GetValue(row)?.ToString() ?? "Unknown")
                    .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new { reason = group.Key, count = group.Count() })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.reason)
                    .ToArray(),
                sideDistribution = BuildValueAuditCountGroups(curatedRows, row => row.BestSide),
                valueTierDistribution = BuildValueAuditCountGroups(curatedRows, row => row.InternalDiagnosticTier),
                playerExposure = BuildValueAuditExposureGroups(curatedRows, row => row.PlayerName).Take(50).ToArray(),
                gameExposure = BuildValueAuditExposureGroups(curatedRows, BuildValueAuditEventKey).Take(50).ToArray(),
                summary = BuildValueAuditSummary(curatedRows),
                volumeDiagnostics = BuildValueAuditVolumeDiagnostics(curatedRows),
                suppressedRows = suppressed.Take(50).ToArray()
            },
            reconciliation = new
            {
                totalCurrentRows = rows.Count,
                postOpportunityDedupeCount = opportunities.Length,
                curatedTopPickCount = curatedRows.Length,
                suppressedTotal,
                countsReconcile
            }
        };
    }

    private static async Task<ValueAuditFinalPublicBoardFanoutDiagnostics> BuildValueAuditFinalPublicBoardFanoutDiagnosticsAsync(
        IReadOnlyCollection<ValueAuditRow> rows,
        string? normalizedSport,
        string? competition,
        string? propType,
        string? playerName,
        string? normalizedProviderCategory,
        int requestedTake,
        IPredictionProbabilityService probabilityService,
        ILikelyHitModelService likelyHitModel,
        PredictionGenerationOptions predictionGenerationOptions,
        ISoccerWorldCupPredictionService soccerPredictionService,
        SoccerPredictionsOptions soccerPredictionOptions,
        SoccerGradingOptions soccerGradingOptions,
        SoccerTopPicksOptions soccerTopPicksOptions,
        TopPicksModelOptions options,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var hasAuditOnlyFilters = !string.IsNullOrWhiteSpace(competition)
            || !string.IsNullOrWhiteSpace(propType)
            || !string.IsNullOrWhiteSpace(playerName);
        var finalRows = new List<JsonObject>();
        var source = hasAuditOnlyFilters
            ? "FilteredAuditRowsNoExactPublicEndpointEquivalent"
            : "SharedPublicTopPicksResponse";

        if (!hasAuditOnlyFilters)
        {
            var publicResponse = IsSelectedSideAllFilter(normalizedSport)
                ? await PublicMarketsEndpoints.BuildAllMarketTopPicksResponseAsync(
                    runId: null,
                    take: requestedTake,
                    freshnessMinutes: null,
                    providerCategory: normalizedProviderCategory,
                    bookmakerPreset: null,
                    dbContext,
                    probabilityService,
                    likelyHitModel,
                    predictionGenerationOptions,
                    options,
                    soccerPredictionService,
                    soccerPredictionOptions,
                    soccerGradingOptions,
                    soccerTopPicksOptions,
                    cancellationToken)
                : await PublicMarketsEndpoints.BuildMarketTopPicksResponseAsync(
                    requestedSport: normalizedSport,
                    effectiveSport: normalizedSport ?? "MLB",
                    runId: null,
                    take: requestedTake,
                    freshnessMinutes: null,
                    providerCategory: normalizedProviderCategory,
                    bookmakerPreset: null,
                    dbContext,
                    probabilityService,
                    likelyHitModel,
                    predictionGenerationOptions,
                    options,
                    soccerPredictionService,
                    soccerPredictionOptions,
                    soccerGradingOptions,
                    soccerTopPicksOptions,
                    cancellationToken);

            var publicBoard = ExtractValueAuditPublicTopPicksBoard(publicResponse);
            return BuildValueAuditFinalPublicBoardFanoutDiagnostics(
                publicBoard.Rows,
                source,
                publicBoard.Summary,
                fromSharedPublicResponse: true);
        }

        var auditRows = rows
            .Select(BuildValueAuditPublicTopPickJsonObject)
            .ToArray();

        if (IsSelectedSideAllFilter(normalizedSport))
        {
            foreach (var sportGroup in auditRows
                         .Where(row => !string.Equals(GetString(row, "sport", "Sport"), "MLB", StringComparison.OrdinalIgnoreCase))
                         .Where(IsValueAuditPublicRecommendationRow)
                         .Select(row => EnsureValueAuditTopPickSportMetadata(
                             row,
                             GetString(row, "sport", "Sport") ?? "Unknown",
                             GetString(row, "competition", "Competition") ?? GetString(row, "sport", "Sport") ?? "Unknown"))
                         .GroupBy(row => GetString(row, "sport", "Sport") ?? "Unknown", StringComparer.OrdinalIgnoreCase))
            {
                var sportCurated = CuratePublicTopPickRows(
                    sportGroup.ToArray(),
                    requestedTake: Math.Max(requestedTake, 500),
                    normalizedProviderCategory,
                    options);
                finalRows.AddRange(CloneValueAuditJsonRows(sportCurated.Rows));
            }

            var merged = CuratePublicTopPickRows(finalRows, requestedTake, normalizedProviderCategory, options);
            finalRows = CloneValueAuditJsonRows(merged.Rows).ToList();
            return BuildValueAuditFinalPublicBoardFanoutDiagnostics(
                finalRows,
                source,
                BuildValueAuditPublicBoardSummary(merged.Diagnostics),
                fromSharedPublicResponse: false);
        }
        else if (finalRows.Count == 0)
        {
            var curated = CuratePublicTopPickRows(auditRows, requestedTake, normalizedProviderCategory, options);
            finalRows = CloneValueAuditJsonRows(curated.Rows).ToList();
            return BuildValueAuditFinalPublicBoardFanoutDiagnostics(
                finalRows,
                source,
                BuildValueAuditPublicBoardSummary(curated.Diagnostics),
                fromSharedPublicResponse: false);
        }

        return BuildValueAuditFinalPublicBoardFanoutDiagnostics(
            finalRows,
            source,
            BuildValueAuditPublicBoardSummary(finalRows),
            fromSharedPublicResponse: false);
    }

    private static ValueAuditFinalPublicBoardFanoutDiagnostics BuildValueAuditFinalPublicBoardFanoutDiagnostics(
        IReadOnlyCollection<JsonObject> finalRows,
        string source,
        ValueAuditPublicBoardSummary publicSummary,
        bool fromSharedPublicResponse)
    {
        var rowsWithFanout = finalRows
            .Where(row => GetProviderOptionCount(row) > 1 || (GetInt(row, "providerFanoutCount", "ProviderFanoutCount") ?? 0) > 1)
            .ToArray();
        var rowsWithProviderOptionsCount = finalRows.Count(row => GetProviderOptionCount(row) > 1);
        var maxProviderFanoutCount = finalRows.Count == 0
            ? 0
            : finalRows.Max(row => Math.Max(GetProviderOptionCount(row), GetInt(row, "providerFanoutCount", "ProviderFanoutCount") ?? 0));

        return new ValueAuditFinalPublicBoardFanoutDiagnostics(
            Scope: "FinalPublicBoardReturnedRows",
            Description: "Provider fanout on the final public Top Picks board after opportunity dedupe, sport staging, exposure caps, and public take limits. When source is SharedPublicTopPicksResponse, these fields are read from the same response path as /api/markets/top-picks for the same sport/provider/take.",
            Source: source,
            UsesSharedPublicTopPicksResponse: fromSharedPublicResponse,
            FinalPublicBoardRowCount: finalRows.Count,
            ProviderFanoutOpportunityCount: publicSummary.ProviderFanoutOpportunityCount,
            MaxProviderFanoutCount: publicSummary.MaxProviderFanoutCount,
            RowsWithProviderOptionsCount: publicSummary.RowsWithProviderOptionsCount,
            RowComputedProviderFanoutOpportunityCount: rowsWithFanout.Length,
            RowComputedMaxProviderFanoutCount: maxProviderFanoutCount,
            RowComputedRowsWithProviderOptionsCount: rowsWithProviderOptionsCount,
            PublicReturnedTopPicks: publicSummary.ReturnedTopPicks,
            PublicPreDedupeCandidateCount: publicSummary.PreDedupeCandidateCount,
            PublicPostOpportunityDedupeCount: publicSummary.PostOpportunityDedupeCount,
            PublicBoardFingerprint: BuildValueAuditFinalPublicBoardFingerprint(finalRows),
            SampleProviderFanoutOpportunities: rowsWithFanout
                .Take(10)
                .Select(row => new
                {
                    opportunityKey = GetString(row, "opportunityKey", "OpportunityKey"),
                    providerFanoutCount = Math.Max(GetProviderOptionCount(row), GetInt(row, "providerFanoutCount", "ProviderFanoutCount") ?? 0),
                    player = GetString(row, "playerName", "PlayerName", "player", "Player"),
                    propType = GetString(row, "normalizedPropType", "NormalizedPropType", "propType", "PropType"),
                    propFamily = GetString(row, "propFamily", "PropFamily"),
                    side = GetString(row, "bestSide", "BestSide", "selectedSide", "SelectedSide", "side", "Side"),
                    line = GetDecimal(row, "line", "Line"),
                    representativeBookmaker = GetString(row, "bestPriceBookmaker", "BestPriceBookmaker", "bestBookmaker", "BestBookmaker", "bookmaker", "Bookmaker", "provider", "Provider"),
                    providerOptions = TryGetProviderOptions(row, out var providerOptions)
                        ? providerOptions
                            .Select(option => option?.DeepClone())
                            .ToArray()
                        : Array.Empty<JsonNode?>()
                })
                .ToArray());
    }

    private static ValueAuditPublicTopPicksBoard ExtractValueAuditPublicTopPicksBoard(object publicResponse)
    {
        var response = JsonSerializer.SerializeToNode(publicResponse) as JsonObject ?? [];
        var rows = response.TryGetPropertyValue("topPicks", out var topPicksNode) && topPicksNode is JsonArray topPicks
            ? topPicks
                .Select(row => row as JsonObject ?? JsonSerializer.SerializeToNode(row) as JsonObject)
                .Where(row => row is not null)
                .Select(row => row!)
                .ToArray()
            : [];
        var summary = response.TryGetPropertyValue("summary", out var summaryNode) && summaryNode is JsonObject summaryObject
            ? BuildValueAuditPublicBoardSummary(summaryObject, rows)
            : BuildValueAuditPublicBoardSummary(rows);

        return new ValueAuditPublicTopPicksBoard(rows, summary);
    }

    private static ValueAuditPublicBoardSummary BuildValueAuditPublicBoardSummary(PublicTopPickCurationDiagnostics curation)
    {
        return new ValueAuditPublicBoardSummary(
            curation.ReturnedTopPickCount,
            curation.PreDedupeCandidateCount,
            curation.PostOpportunityDedupeCount,
            curation.ProviderFanoutOpportunityCount,
            curation.MaxProviderFanoutCount,
            curation.RowsWithProviderOptionsCount);
    }

    private static ValueAuditPublicBoardSummary BuildValueAuditPublicBoardSummary(JsonObject summary, IReadOnlyCollection<JsonObject> rows)
    {
        var rowSummary = BuildValueAuditPublicBoardSummary(rows);
        return new ValueAuditPublicBoardSummary(
            GetInt(summary, "returnedTopPicks", "ReturnedTopPicks") ?? rowSummary.ReturnedTopPicks,
            GetInt(summary, "preDedupeCandidateCount", "PreDedupeCandidateCount") ?? rowSummary.PreDedupeCandidateCount,
            GetInt(summary, "postOpportunityDedupeCount", "PostOpportunityDedupeCount") ?? rowSummary.PostOpportunityDedupeCount,
            GetInt(summary, "providerFanoutOpportunityCount", "ProviderFanoutOpportunityCount") ?? rowSummary.ProviderFanoutOpportunityCount,
            GetInt(summary, "maxProviderFanoutCount", "MaxProviderFanoutCount") ?? rowSummary.MaxProviderFanoutCount,
            GetInt(summary, "rowsWithProviderOptionsCount", "RowsWithProviderOptionsCount") ?? rowSummary.RowsWithProviderOptionsCount);
    }

    private static ValueAuditPublicBoardSummary BuildValueAuditPublicBoardSummary(IReadOnlyCollection<JsonObject> rows)
    {
        var rowsWithFanout = rows.Count(row => GetProviderOptionCount(row) > 1 || (GetInt(row, "providerFanoutCount", "ProviderFanoutCount") ?? 0) > 1);
        var maxProviderFanoutCount = rows.Count == 0
            ? 0
            : rows.Max(row => Math.Max(GetProviderOptionCount(row), GetInt(row, "providerFanoutCount", "ProviderFanoutCount") ?? 0));
        return new ValueAuditPublicBoardSummary(
            rows.Count,
            rows.Count,
            rows.Count,
            rowsWithFanout,
            maxProviderFanoutCount,
            rows.Count(row => GetProviderOptionCount(row) > 1));
    }

    private static string BuildValueAuditFinalPublicBoardFingerprint(IReadOnlyCollection<JsonObject> rows)
    {
        return string.Join(";",
            rows.Select(row => GetString(row, "opportunityKey", "OpportunityKey")
                ?? string.Join('|',
                    GetString(row, "sport", "Sport") ?? string.Empty,
                    GetString(row, "competition", "Competition") ?? string.Empty,
                    GetString(row, "eventId", "EventId", "eventName", "EventName") ?? string.Empty,
                    GetString(row, "playerName", "PlayerName", "player", "Player") ?? string.Empty,
                    GetString(row, "normalizedPropType", "NormalizedPropType", "propType", "PropType") ?? string.Empty,
                    (GetDecimal(row, "line", "Line") ?? decimal.Zero).ToString("0.####", CultureInfo.InvariantCulture),
                    GetString(row, "bestSide", "BestSide", "selectedSide", "SelectedSide", "side", "Side") ?? string.Empty)));
    }

    private static JsonObject BuildValueAuditPublicTopPickJsonObject(ValueAuditRow row)
    {
        var obj = new JsonObject
        {
            ["source"] = row.Source,
            ["sport"] = row.Sport,
            ["competition"] = row.Competition,
            ["eventId"] = row.EventId,
            ["eventName"] = row.EventName,
            ["player"] = row.PlayerName,
            ["playerName"] = row.PlayerName,
            ["normalizedPropType"] = row.PropType,
            ["propType"] = row.PropType,
            ["line"] = row.Line,
            ["bestSide"] = row.BestSide,
            ["provider"] = row.PriceBookmakerProvider,
            ["providerCategory"] = row.ProviderCategory,
            ["bookmaker"] = row.PriceBookmakerProvider,
            ["bestBookmaker"] = row.BestPriceBookmaker ?? row.PriceBookmakerProvider,
            ["bestPriceBookmaker"] = row.BestPriceBookmaker ?? row.PriceBookmakerProvider,
            ["bestAmericanPrice"] = row.BestAmericanPrice,
            ["bestDecimalPrice"] = row.BestDecimalPrice,
            ["bestImpliedProbability"] = row.ImpliedProbability,
            ["bestProbability"] = row.SelectedSideProbability,
            ["selectedSideProbability"] = row.SelectedSideProbability,
            ["bestProbabilityEdge"] = row.ProbabilityEdge,
            ["bestEstimatedEv"] = row.EstimatedEv,
            ["displayExpectedValue"] = row.EstimatedEv,
            ["topPickScore"] = row.TopPickScore,
            ["rankingScore"] = row.TopPickScore,
            ["entryActionableForUser"] = string.Equals(row.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase),
            ["playablePriceEligible"] = true,
            ["isCandidate"] = true,
            ["isRecommendation"] = true,
            ["recommendationActionableForUser"] = true,
            ["propFamily"] = BuildValueAuditPropFamily(row.PropType)
        };

        if (row.EventDate.HasValue)
        {
            obj["eventDate"] = row.EventDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            obj["expectedResultDate"] = row.EventDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (row.SlateDate.HasValue)
        {
            obj["slateDate"] = row.SlateDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return obj;
    }

    private static bool IsValueAuditPublicRecommendationRow(JsonObject row)
    {
        return GetBool(row, "isRecommendation", "IsRecommendation") == true
            && GetBool(row, "recommendationActionableForUser", "RecommendationActionableForUser") == true;
    }

    private static JsonObject EnsureValueAuditTopPickSportMetadata(JsonObject row, string sport, string competition)
    {
        if (string.IsNullOrWhiteSpace(GetString(row, "sport", "Sport")))
        {
            row["sport"] = sport;
        }

        if (string.IsNullOrWhiteSpace(GetString(row, "competition", "Competition")))
        {
            row["competition"] = competition;
        }

        return row;
    }

    private static JsonObject[] CloneValueAuditJsonRows(IEnumerable<object> rows)
    {
        return rows
            .Select(CloneValueAuditJsonRow)
            .Where(row => row is not null)
            .Select(row => row!)
            .ToArray();
    }

    private static JsonObject? CloneValueAuditJsonRow(object? row)
    {
        return row switch
        {
            JsonObject obj => obj.DeepClone().AsObject(),
            JsonElement element when element.ValueKind == JsonValueKind.Object => JsonNode.Parse(element.GetRawText()) as JsonObject,
            JsonNode node => node.DeepClone().AsObject(),
            _ => null
        };
    }

    private static string BuildValueAuditCuratedOpportunityKey(ValueAuditRow row)
    {
        return string.Join('|',
            RecommendationLedgerWriter.NormalizeKey(row.Sport),
            RecommendationLedgerWriter.NormalizeKey(row.Competition),
            RecommendationLedgerWriter.NormalizeKey(row.EventId ?? row.EventName ?? row.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            RecommendationLedgerWriter.NormalizeKey(row.PlayerName),
            RecommendationLedgerWriter.NormalizeKey(row.PropType),
            row.Line?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
            RecommendationLedgerWriter.NormalizeKey(row.BestSide));
    }

    private static int GetValueAuditCount(Dictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out var count) ? count : 0;

    private static void IncrementValueAuditCount(Dictionary<string, int> counts, string key) =>
        counts[key] = GetValueAuditCount(counts, key) + 1;

    private static object BuildValueAuditMetrics(IReadOnlyCollection<ValueAuditRow> rows)
    {
        var gradedRows = rows.Where(IsValueAuditGradedResult).ToArray();
        var priceAdjustedRows = gradedRows.Where(row => row.RealizedProfitAtLoggedPrice.HasValue).ToArray();
        var wins = gradedRows.Count(row => row.Result == "Won");
        var losses = gradedRows.Count(row => row.Result == "Lost");
        var decisions = wins + losses;

        return new
        {
            candidateCount = rows.Count,
            gradedCount = gradedRows.Length,
            noResultCount = rows.Count(row => row.Result == "NoResult"),
            wins,
            losses,
            pushes = gradedRows.Count(row => row.Result == "Push"),
            hitRate = decisions == 0 ? (decimal?)null : Math.Round(wins / (decimal)decisions, 6),
            averageSelectedSideProbability = AverageLikelyHitDecimal(rows.Select(row => row.SelectedSideProbability)),
            averageImpliedProbability = AverageLikelyHitDecimal(rows.Select(row => row.ImpliedProbability)),
            averageProbabilityEdge = AverageLikelyHitDecimal(rows.Select(row => row.ProbabilityEdge)),
            averageEstimatedEv = AverageLikelyHitDecimal(rows.Select(row => row.EstimatedEv)),
            flatProfit = gradedRows.Length == 0 ? (decimal?)null : Math.Round(gradedRows.Sum(row => row.FlatUnitProfit ?? 0m), 6),
            flatRoi = gradedRows.Length == 0 ? (decimal?)null : Math.Round(gradedRows.Sum(row => row.FlatUnitProfit ?? 0m) / gradedRows.Length, 6),
            priceAdjustedProfit = priceAdjustedRows.Length == 0 ? (decimal?)null : Math.Round(priceAdjustedRows.Sum(row => row.RealizedProfitAtLoggedPrice ?? 0m), 6),
            priceAdjustedRoi = priceAdjustedRows.Length == 0 ? (decimal?)null : Math.Round(priceAdjustedRows.Sum(row => row.RealizedProfitAtLoggedPrice ?? 0m) / priceAdjustedRows.Length, 6),
            averageAmericanPrice = AverageLikelyHitDecimal(rows.Select(row => row.BestAmericanPrice.HasValue ? (decimal?)row.BestAmericanPrice.Value : null)),
            averageDecimalPrice = AverageLikelyHitDecimal(rows.Select(row => row.EffectiveDecimalPrice ?? row.BestDecimalPrice)),
            missingPriceCount = rows.Count(row => row.PriceWasMissing),
            fallbackPriceCount = rows.Count(row => row.PriceWasFallback),
            syntheticPriceCount = rows.Count(row => row.PriceWasSynthetic),
            heavyJuiceCount = rows.Count(IsValueAuditHeavyJuice),
            tinyEstimatedEvCount = rows.Count(row => row.EstimatedEv is > 0m and <= ValueAuditTinyEvThreshold),
            positiveProbabilityEdgeNonPositiveEvCount = rows.Count(row => row.ProbabilityEdge > 0m && row.EstimatedEv <= 0m),
            negativeOrNonPositiveEvCount = rows.Count(row => row.EstimatedEv <= 0m),
            highHitProbabilityNonPositiveEvCount = rows.Count(row => row.SelectedSideProbability >= ValueAuditHighProbabilityThreshold && row.EstimatedEv <= 0m)
        };
    }

    private static object[] BuildValueAuditGroups(
        IReadOnlyCollection<ValueAuditRow> rows,
        Func<ValueAuditRow, string?> keySelector)
    {
        return rows
            .GroupBy(row => string.IsNullOrWhiteSpace(keySelector(row)) ? "Unknown" : keySelector(row)!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupRows = group.ToArray();
                return new
                {
                    key = group.Key,
                    count = groupRows.Length,
                    metrics = BuildValueAuditMetrics(groupRows)
                };
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .ToArray();
    }

    private static ValueAuditCountGroup[] BuildValueAuditCountGroups(
        IReadOnlyCollection<ValueAuditRow> rows,
        Func<ValueAuditRow, string?> keySelector,
        int take = 50)
    {
        return rows
            .GroupBy(row => string.IsNullOrWhiteSpace(keySelector(row)) ? "Unknown" : keySelector(row)!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ValueAuditCountGroup(group.Key, group.Count()))
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .Take(take)
            .ToArray();
    }

    private static ValueAuditExposureGroup[] BuildValueAuditExposureGroups(
        IReadOnlyCollection<ValueAuditRow> rows,
        Func<ValueAuditRow, string?> keySelector)
    {
        return rows
            .GroupBy(row => string.IsNullOrWhiteSpace(keySelector(row)) ? "Unknown" : keySelector(row)!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupRows = group.ToArray();
                return new ValueAuditExposureGroup(
                    group.Key,
                    groupRows.Length,
                    groupRows
                        .Select(row => row.PlayerName)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value)
                        .Take(20)
                        .ToArray(),
                    groupRows
                        .Select(row => row.PropType)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value)
                        .Take(20)
                        .ToArray(),
                    BuildValueAuditMetrics(groupRows),
                    groupRows.Take(10).ToArray());
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .ToArray();
    }

    private static ValueAuditCutDefinition[] BuildValueAuditCutDefinitions(int totalRows)
    {
        int PercentCut(decimal percent)
        {
            if (totalRows == 0)
            {
                return 0;
            }

            return Math.Clamp((int)Math.Ceiling(totalRows * percent), 1, totalRows);
        }

        return
        [
            new("Top 10", Math.Min(10, totalRows)),
            new("Top 25", Math.Min(25, totalRows)),
            new("Top 50", Math.Min(50, totalRows)),
            new("Top 100", Math.Min(100, totalRows)),
            new("Top 200", Math.Min(200, totalRows)),
            new("Top 5%", PercentCut(0.05m)),
            new("Top 10%", PercentCut(0.10m)),
            new("Top 20%", PercentCut(0.20m))
        ];
    }

    private static object[] BuildValueAuditTierRules()
    {
        return
        [
            new
            {
                tier = "premiumTopPick",
                rule = "selectedSideProbability >= 70%, estimated EV >= 5%, positive probability edge, not heavily juiced, usable price, consistency guard not failed, and no projection/side conflict."
            },
            new
            {
                tier = "valueCandidate",
                rule = "selectedSideProbability >= 65%, estimated EV >= 5%, positive probability edge, and not heavily juiced."
            },
            new
            {
                tier = "likelyHit",
                rule = "selectedSideProbability >= 70%, but EV or price quality is not strong enough for the value tiers."
            },
            new
            {
                tier = "broadCandidate",
                rule = "Other current public Top Pick rows that still have at least a 65% selected-side probability or positive estimated EV."
            },
            new
            {
                tier = "monitorOnly",
                rule = "Current public Top Pick rows with missing/weak probability, missing price, failed consistency guard, or otherwise insufficient value diagnostics."
            }
        ];
    }

    private static ValueAuditPriceResolution ResolveValueAuditPrice(
        int? loggedAmericanPrice,
        decimal? loggedDecimalPrice,
        decimal? impliedProbability,
        bool allowImpliedFallback)
    {
        if (loggedDecimalPrice.HasValue)
        {
            return new ValueAuditPriceResolution(
                loggedAmericanPrice,
                loggedDecimalPrice,
                loggedDecimalPrice,
                "LoggedDecimalPrice",
                PriceWasMissing: false,
                PriceWasFallback: false,
                PriceWasSynthetic: false);
        }

        if (loggedAmericanPrice.HasValue)
        {
            return new ValueAuditPriceResolution(
                loggedAmericanPrice,
                null,
                ConvertValueAuditAmericanToDecimal(loggedAmericanPrice.Value),
                "ConvertedFromLoggedAmericanPrice",
                PriceWasMissing: false,
                PriceWasFallback: false,
                PriceWasSynthetic: false);
        }

        if (allowImpliedFallback && impliedProbability is > 0m)
        {
            return new ValueAuditPriceResolution(
                null,
                null,
                Math.Round(1m / impliedProbability.Value, 6),
                "FallbackFromLoggedImpliedProbability",
                PriceWasMissing: false,
                PriceWasFallback: true,
                PriceWasSynthetic: false);
        }

        return new ValueAuditPriceResolution(
            null,
            null,
            null,
            "MissingPrice",
            PriceWasMissing: true,
            PriceWasFallback: false,
            PriceWasSynthetic: false);
    }

    private static decimal ConvertValueAuditAmericanToDecimal(int americanPrice)
    {
        return americanPrice > 0
            ? Math.Round(1m + americanPrice / 100m, 6)
            : Math.Round(1m + 100m / Math.Abs(americanPrice), 6);
    }

    private static decimal? CalculateValueAuditEstimatedEv(decimal? selectedSideProbability, decimal? decimalPrice)
    {
        return selectedSideProbability.HasValue && decimalPrice.HasValue
            ? Math.Round((selectedSideProbability.Value * decimalPrice.Value) - 1m, 6)
            : null;
    }

    private static decimal? CalculateValueAuditRealizedProfit(string? result, decimal? decimalPrice)
    {
        if (!decimalPrice.HasValue || !IsValueAuditGradedResult(result))
        {
            return null;
        }

        return result switch
        {
            "Won" => Math.Round(decimalPrice.Value - 1m, 6),
            "Lost" => -1m,
            "Push" => 0m,
            _ => null
        };
    }

    private static decimal? CalculateValueAuditFlatProfit(string? result)
    {
        return result switch
        {
            "Won" => 1m,
            "Lost" => -1m,
            "Push" => 0m,
            _ => null
        };
    }

    private static string? NormalizeValueAuditResult(string? gradeStatus)
    {
        return gradeStatus switch
        {
            "Win" or "Won" => "Won",
            "Loss" or "Lost" => "Lost",
            "Push" or "Pushed" => "Push",
            "NoResult" => "NoResult",
            _ => null
        };
    }

    private static bool IsValueAuditGradedResult(ValueAuditRow row) => IsValueAuditGradedResult(row.Result);

    private static bool IsValueAuditGradedResult(string? result)
    {
        return result is "Won" or "Lost" or "Push";
    }

    private static DateOnly? ResolveValueAuditEventDate(RecommendationSnapshot row)
    {
        if (row.EventStartTimeUtc.HasValue)
        {
            return row.Sport.Equals("MLB", StringComparison.OrdinalIgnoreCase)
                ? ResolveValueAuditEasternDate(row.EventStartTimeUtc.Value)
                : DateOnly.FromDateTime(row.EventStartTimeUtc.Value.UtcDateTime);
        }

        return row.ExpectedResultDate;
    }

    private static DateOnly ResolveValueAuditEasternDate(DateTimeOffset value)
    {
        try
        {
            var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, eastern).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(value.UtcDateTime);
        }
        catch (InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(value.UtcDateTime);
        }
    }

    private static string BuildValueAuditRankPercentileBucket(decimal? rankPercentile)
    {
        if (!rankPercentile.HasValue)
        {
            return "Unranked";
        }

        return rankPercentile.Value switch
        {
            <= 0.05m => "Top 5%",
            <= 0.10m => "Top 10%",
            <= 0.20m => "Top 20%",
            <= 0.50m => "Top 50%",
            _ => "Bottom 50%"
        };
    }

    private static string BuildValueAuditTopPickScoreBucket(decimal? score)
    {
        if (!score.HasValue)
        {
            return "Unavailable";
        }

        return score.Value switch
        {
            >= 0.90m => "0.90+",
            >= 0.80m => "0.80-0.89",
            >= 0.70m => "0.70-0.79",
            >= 0.60m => "0.60-0.69",
            >= 0.50m => "0.50-0.59",
            _ => "<0.50"
        };
    }

    private static string BuildValueAuditProbabilityEdgeBucket(decimal? edge)
    {
        if (!edge.HasValue)
        {
            return "Unavailable";
        }

        return edge.Value switch
        {
            <= 0m => "<=0",
            <= 0.02m => "0-2%",
            <= 0.05m => "2-5%",
            <= 0.10m => "5-10%",
            <= 0.15m => "10-15%",
            _ => "15%+"
        };
    }

    private static string BuildValueAuditProjectionMinusLineBucket(decimal? edge)
    {
        if (!edge.HasValue)
        {
            return "Unavailable";
        }

        var absoluteEdge = Math.Abs(edge.Value);
        return absoluteEdge switch
        {
            <= 0.10m => "0-0.10",
            <= 0.25m => "0.10-0.25",
            <= 0.50m => "0.25-0.50",
            <= 1.00m => "0.50-1.00",
            _ => "1.00+"
        };
    }

    private static string BuildValueAuditDiagnosticTier(
        string? bestSide,
        decimal? selectedSideProbability,
        decimal? estimatedEv,
        int? bestAmericanPrice,
        ValueAuditPriceResolution price,
        decimal? projectionMinusLine,
        bool? consistencyGuardPassed)
    {
        if (!selectedSideProbability.HasValue
            || price.PriceWasMissing
            || consistencyGuardPassed == false)
        {
            return "monitorOnly";
        }

        var positiveEdge = estimatedEv is > 0m;
        var goodValue = estimatedEv >= ValueAuditPremiumEvThreshold;
        var heavyJuice = IsValueAuditHeavyJuice(bestAmericanPrice);
        var projectionConflict = projectionMinusLine.HasValue && HasValueAuditProjectionSideConflict(bestSide, projectionMinusLine.Value);

        if (selectedSideProbability >= 0.70m
            && goodValue
            && !heavyJuice
            && projectionConflict == false)
        {
            return "premiumTopPick";
        }

        if (selectedSideProbability >= ValueAuditHighProbabilityThreshold && goodValue && !heavyJuice)
        {
            return "valueCandidate";
        }

        if (selectedSideProbability >= 0.70m)
        {
            return "likelyHit";
        }

        return selectedSideProbability >= ValueAuditHighProbabilityThreshold || positiveEdge
            ? "broadCandidate"
            : "monitorOnly";
    }

    private static ValueAuditHypotheticalThresholds BuildValueAuditHypotheticalThresholds(
        string? bestSide,
        decimal? selectedSideProbability,
        decimal? estimatedEv,
        int? bestAmericanPrice,
        ValueAuditPriceResolution price,
        decimal? projectionMinusLine,
        bool? consistencyGuardPassed)
    {
        var hitChanceAtLeast70 = selectedSideProbability >= 0.70m;
        var hitChanceAtLeast75 = selectedSideProbability >= 0.75m;
        var estimatedEvPositive = estimatedEv > 0m;
        var estimatedEvAtLeastFivePercent = estimatedEv >= ValueAuditPremiumEvThreshold;
        var notHeavyJuice = !IsValueAuditHeavyJuice(bestAmericanPrice);
        var premiumProfile = hitChanceAtLeast70
            && estimatedEvAtLeastFivePercent
            && notHeavyJuice
            && !price.PriceWasMissing
            && consistencyGuardPassed != false
            && !(projectionMinusLine.HasValue && HasValueAuditProjectionSideConflict(bestSide, projectionMinusLine.Value));

        return new ValueAuditHypotheticalThresholds(
            hitChanceAtLeast70,
            hitChanceAtLeast75,
            estimatedEvPositive,
            estimatedEvAtLeastFivePercent,
            notHeavyJuice,
            premiumProfile);
    }

    private static string BuildValueAuditPropFamily(string? propType)
    {
        var normalized = RecommendationLedgerWriter.NormalizeKey(propType ?? string.Empty);
        return normalized switch
        {
            "totalbases" or "hits" or "singles" or "doubles" or "triples" or "homeruns" => "BattingContactPower",
            "runs" or "runsscored" or "rbi" or "runsbattedin" => "BattingRunProduction",
            "walks" or "batterwalks" or "batterstrikeouts" => "BattingDiscipline",
            "pitcherstrikeouts" or "strikeouts" => "PitchingStrikeouts",
            "pitcherouts" or "outsrecorded" or "earnedruns" or "hitsallowed" or "walksallowed" => "PitchingRunPrevention",
            "goals" or "assists" => "SoccerScoringCreation",
            "shots" or "shotsontarget" => "SoccerShooting",
            _ => string.IsNullOrWhiteSpace(propType) ? "Unknown" : propType.Trim()
        };
    }

    private static string BuildValueAuditEventKey(ValueAuditRow row)
    {
        return FirstValueAuditNonBlank(
            row.EventId,
            row.EventName,
            !string.IsNullOrWhiteSpace(row.HomeTeam) || !string.IsNullOrWhiteSpace(row.AwayTeam)
                ? $"{row.AwayTeam ?? "Unknown"} @ {row.HomeTeam ?? "Unknown"}"
                : null,
            row.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.SlateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "Unknown")!;
    }

    private static string BuildValueAuditTeamKey(ValueAuditRow row)
    {
        return FirstValueAuditNonBlank(row.TeamName, row.HomeTeam, row.AwayTeam, "Unknown")!;
    }

    private static string BuildValueAuditProfileMatchKey(ValueAuditRow row)
    {
        return string.Join('|',
            row.Sport ?? string.Empty,
            row.PropType ?? string.Empty,
            row.BestSide ?? string.Empty,
            row.Line?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
            row.ProviderCategory ?? string.Empty,
            row.PriceBucket ?? string.Empty);
    }

    private static string? FirstValueAuditNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string BuildValueAuditProbabilityBucket(decimal? probability)
    {
        if (!probability.HasValue)
        {
            return "Unavailable";
        }

        return probability.Value switch
        {
            < 0.50m => "<50",
            < 0.55m => "50-55",
            < 0.60m => "55-60",
            < 0.65m => "60-65",
            < 0.70m => "65-70",
            < 0.75m => "70-75",
            < 0.80m => "75-80",
            _ => "80+"
        };
    }

    private static string BuildValueAuditEstimatedEvBucket(decimal? estimatedEv)
    {
        if (!estimatedEv.HasValue)
        {
            return "Unavailable";
        }

        return estimatedEv.Value switch
        {
            <= 0m => "<=0",
            <= 0.05m => "0-5%",
            <= 0.10m => "5-10%",
            <= 0.20m => "10-20%",
            <= 0.30m => "20-30%",
            _ => "30%+"
        };
    }

    private static string BuildValueAuditPriceBucket(int? americanPrice, decimal? decimalPrice)
    {
        if (americanPrice.HasValue)
        {
            return BuildLikelyHitOddsBucket(americanPrice);
        }

        if (!decimalPrice.HasValue)
        {
            return "Unavailable";
        }

        return decimalPrice.Value switch
        {
            < 1.35m => "Decimal <1.35",
            < 1.50m => "Decimal 1.35-1.49",
            < 1.75m => "Decimal 1.50-1.74",
            < 2.00m => "Decimal 1.75-1.99",
            < 2.50m => "Decimal 2.00-2.49",
            _ => "Decimal 2.50+"
        };
    }

    private static IReadOnlyList<string> BuildValueRiskFlags(
        SelectedSideDiagnosticRow row,
        decimal? estimatedEv,
        ValueAuditPriceResolution price,
        decimal? projectionMinusLine)
    {
        var flags = new List<string>();

        if (price.PriceWasMissing)
        {
            flags.Add("MissingPrice");
        }

        if (row.SelectedSideProbability >= ValueAuditHighProbabilityThreshold && estimatedEv <= 0m)
        {
            flags.Add("HighProbabilityNonPositiveEv");
        }
        else if (estimatedEv <= 0m)
        {
            flags.Add("NonPositiveEv");
        }

        if (IsValueAuditHeavyJuice(row.BestAmericanPrice))
        {
            flags.Add("HeavyJuice");
        }

        if (projectionMinusLine.HasValue && HasValueAuditProjectionSideConflict(row.BestSide, projectionMinusLine.Value))
        {
            flags.Add("ProjectionMinusLineConflictsWithSelectedSide");
        }

        if (price.PriceWasFallback)
        {
            flags.Add("PriceDerivedFromFallback");
        }

        return flags.Count == 0 ? ["None"] : flags;
    }

    private static bool HasValueAuditProjectionSideConflict(ValueAuditRow row)
    {
        return row.ProjectionMinusLine.HasValue && HasValueAuditProjectionSideConflict(row.BestSide, row.ProjectionMinusLine.Value);
    }

    private static bool IsValueAuditHeavyJuice(ValueAuditRow row)
    {
        return IsValueAuditHeavyJuice(row.BestAmericanPrice);
    }

    private static bool IsValueAuditHeavyJuice(int? americanPrice)
    {
        return americanPrice.HasValue && americanPrice.Value <= ValueAuditHeavyJuiceAmericanPrice;
    }

    private static bool HasValueAuditProjectionSideConflict(string? side, decimal projectionMinusLine)
    {
        return (IsSelectedSide(side, "Under") && projectionMinusLine > 0m)
            || (IsSelectedSide(side, "Over") && projectionMinusLine < 0m);
    }

    private static string BuildValueAuditOpportunityKey(ValueAuditRow row)
    {
        return string.Join('|',
            row.Sport ?? string.Empty,
            row.Competition ?? string.Empty,
            row.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            row.PlayerName ?? string.Empty,
            row.PropType ?? string.Empty,
            row.BestSide ?? string.Empty,
            row.Line?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
            row.ProviderCategory ?? string.Empty,
            row.BestPriceBookmaker ?? row.PriceBookmakerProvider ?? string.Empty);
    }

    private sealed record ValueAuditPriceResolution(
        int? LoggedAmericanPrice,
        decimal? LoggedDecimalPrice,
        decimal? EffectiveDecimalPrice,
        string PriceStatus,
        bool PriceWasMissing,
        bool PriceWasFallback,
        bool PriceWasSynthetic);

    private sealed record ValueAuditCountGroup(string key, int count);

    private sealed record ValueAuditCutDefinition(string Name, int Count);

    private sealed record ValueAuditExposureGroup(
        string key,
        int count,
        IReadOnlyList<string> players,
        IReadOnlyList<string> propTypes,
        object metrics,
        IReadOnlyList<ValueAuditRow> rows);

    private sealed record ValueAuditCuratedOpportunity(
        string OpportunityKey,
        ValueAuditRow Representative,
        IReadOnlyList<ValueAuditRow> Rows)
    {
        public int ProviderFanoutCount => Rows.Count;
    }

    private sealed record ValueAuditHypotheticalThresholds(
        bool HitChanceAtLeast70,
        bool HitChanceAtLeast75,
        bool EstimatedEvPositive,
        bool EstimatedEvAtLeastFivePercent,
        bool NotHeavyJuice,
        bool PremiumTopPickProfile);

    private sealed record ValueAuditPublicTopPicksBoard(
        IReadOnlyCollection<JsonObject> Rows,
        ValueAuditPublicBoardSummary Summary);

    private sealed record ValueAuditPublicBoardSummary(
        int ReturnedTopPicks,
        int PreDedupeCandidateCount,
        int PostOpportunityDedupeCount,
        int ProviderFanoutOpportunityCount,
        int MaxProviderFanoutCount,
        int RowsWithProviderOptionsCount);

    private sealed record ValueAuditFinalPublicBoardFanoutDiagnostics(
        string Scope,
        string Description,
        string Source,
        bool UsesSharedPublicTopPicksResponse,
        int FinalPublicBoardRowCount,
        int ProviderFanoutOpportunityCount,
        int MaxProviderFanoutCount,
        int RowsWithProviderOptionsCount,
        int RowComputedProviderFanoutOpportunityCount,
        int RowComputedMaxProviderFanoutCount,
        int RowComputedRowsWithProviderOptionsCount,
        int PublicReturnedTopPicks,
        int PublicPreDedupeCandidateCount,
        int PublicPostOpportunityDedupeCount,
        string PublicBoardFingerprint,
        object[] SampleProviderFanoutOpportunities);

    private sealed record ValueAuditRow(
        string Source,
        string Sport,
        string Competition,
        string PlayerName,
        string PropType,
        decimal? Line,
        string BestSide,
        decimal? ModelOverProbability,
        decimal? ModelUnderProbability,
        decimal? SelectedSideProbability,
        decimal? RawSelectedSideProbability,
        decimal? CalibratedSelectedSideProbability,
        decimal? ImpliedProbability,
        decimal? ProbabilityEdge,
        decimal? EstimatedEv,
        decimal? PersistedEstimatedEv,
        int? BestAmericanPrice,
        decimal? BestDecimalPrice,
        string? BestPriceBookmaker,
        string? PriceBookmakerProvider,
        string? ProviderCategory,
        decimal? ProjectionValue,
        decimal? ProjectedValue,
        decimal? ProjectionMinusLine,
        string? ProjectionSource,
        string? ProbabilitySource,
        string? ProbabilityModelVersion,
        string? EvCalibrationMethod,
        string? PriceGateStatus,
        string? PriceGateReason,
        string? EvSuppressedReason,
        string ChosenSideReason,
        string? OverRejectedReason,
        string? UnderRejectedReason,
        bool? SelectedSideHistoryConsistencyGuardApplied,
        bool? SelectedSideHistoryConsistencyGuardPassed,
        IReadOnlyList<string> FatalSelectedSideHistoryConsistencyCodes,
        string LineBucket,
        string PriceBucket,
        string ImpliedProbabilityBucket,
        string ModelProbabilityBucket,
        string EstimatedEvBucket,
        string ValueRiskFlag,
        IReadOnlyList<string> ValueRiskFlags,
        DateOnly? EventDate,
        DateOnly? GradedDate,
        string? Result,
        decimal? RealizedProfitAtLoggedPrice,
        decimal? RealizedRoiAtLoggedPrice,
        decimal? FlatUnitProfit,
        decimal? FlatUnitRoi,
        int? LoggedAmericanPrice,
        decimal? LoggedDecimalPrice,
        decimal? EffectiveDecimalPrice,
        string PriceStatus,
        bool PriceWasMissing,
        bool PriceWasFallback,
        bool PriceWasSynthetic,
        string? NoResultReason,
        DateTimeOffset? LedgerCapturedAtUtc,
        DateTimeOffset? LedgerGradedAtUtc,
        string? NotPromotedReason,
        string? EventId,
        string? EventName,
        string? HomeTeam,
        string? AwayTeam,
        string? TeamName,
        DateOnly? SlateDate,
        decimal? TopPickScore,
        int? Rank,
        decimal? RankPercentile,
        string RankPercentileBucket,
        string TopPickScoreBucket,
        string ProbabilityEdgeBucket,
        string ProjectionMinusLineBucket,
        string InternalDiagnosticTier,
        ValueAuditHypotheticalThresholds StricterHypotheticalThresholds);
}
