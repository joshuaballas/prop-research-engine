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
    internal static JsonObject[] BuildLikelyHitTopPickRows(
        IEnumerable<JsonObject> sourceRows,
        ILikelyHitModelService likelyHitModel,
        TopPicksModelOptions topPicksModelOptions,
        int take)
    {
        var evaluatedRows = sourceRows
            .Select(row =>
            {
                var clone = row.DeepClone().AsObject();
                var result = ApplyLikelyHitMetadata(clone, likelyHitModel, topPicksModelOptions);
                return new { Row = clone, Result = result };
            })
            .Where(row => row.Result.CanShowAsPick)
            .GroupBy(row => BuildLikelyHitExposureKey(row.Row), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.Result.LineUtilityScore ?? decimal.MinValue)
                .ThenByDescending(row => row.Result.TopPickScore ?? decimal.MinValue)
                .ThenByDescending(row => row.Result.HitChance ?? decimal.MinValue)
                .ThenByDescending(row => ReadLikelyHitDecimal(row.Row, "bestDecimalPrice") ?? decimal.MinValue)
                .First())
            .OrderByDescending(row => row.Result.TopPickScore ?? decimal.MinValue)
            .ThenByDescending(row => row.Result.HitChance ?? decimal.MinValue)
            .ThenBy(row => ReadLikelyHitString(row.Row, "player") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(row => row.Row)
            .ToArray();

        for (var index = 0; index < evaluatedRows.Length; index++)
        {
            evaluatedRows[index]["rank"] = index + 1;
            evaluatedRows[index]["selectedRecommendedLine"] = true;
            evaluatedRows[index]["balancedLineSelectionReason"] = "SelectedBestBalancedLineInAlternateLadder";
        }

        return evaluatedRows;
    }

    internal static LikelyHitModelResult ApplyLikelyHitMetadata(
        JsonObject row,
        ILikelyHitModelService likelyHitModel,
        TopPicksModelOptions topPicksModelOptions)
    {
        var input = BuildLikelyHitInput(row);
        var result = likelyHitModel.Evaluate(input);

        row["modelVersion"] = result.ModelVersion;
        row["probabilityModelVersion"] = result.ProbabilityModelVersion;
        row["recommendationContractVersion"] = result.RecommendationContractVersion;
        row["hitChance"] = result.HitChance;
        row["rawWeightedProbability"] = result.RawWeightedProbability;
        row["smoothedProbability"] = result.SmoothedProbability;
        row["priorProbability"] = result.PriorProbability;
        row["priorWeight"] = result.PriorWeight;
        row["recentModifier"] = result.RecentModifier;
        row["marketCalibrationAdjustment"] = result.MarketCalibrationAdjustment;
        row["uncertaintyPenalty"] = result.UncertaintyPenalty;
        row["effectiveSampleSize"] = result.EffectiveSampleSize;
        row["weightedHits"] = result.WeightedHits;
        row["weightedLosses"] = result.WeightedLosses;
        row["weightedPushes"] = result.WeightedPushes;
        row["recent5HitRate"] = result.Recent5HitRate;
        row["recent10HitRate"] = result.Recent10HitRate;
        row["seasonHitRate"] = result.SeasonHitRate;
        row["longHistoryHitRate"] = result.LongHistoryHitRate;
        row["usesAggregateProbability"] = result.UsesAggregateProbability;
        row["reliabilityScore"] = result.ReliabilityScore;
        row["fatalConflictCodes"] = new JsonArray(result.FatalConflictCodes.Select(code => JsonValue.Create(code)).ToArray<JsonNode?>());
        row["topPickScore"] = result.TopPickScore;
        row["payoutQualityScore"] = result.PayoutQualityScore;
        row["sweetSpotScore"] = result.SweetSpotScore;
        row["hitChanceScore"] = result.HitChanceScore;
        row["priceGateStatus"] = result.PriceGateStatus;
        row["priceGateReason"] = result.PriceGateReason;
        row["playablePriceEligible"] = result.PlayablePriceEligible;
        row["topPickQualityEligible"] = result.TopPickQualityEligible;
        row["lineUtilityScore"] = result.LineUtilityScore;
        row["balancedLineUtility"] = result.BalancedLineUtility;
        row["effectiveAmericanOdds"] = result.EffectiveAmericanOdds;
        row["favoritePriceCeiling"] = result.FavoritePriceCeiling;
        row["underdogPriceCeiling"] = result.UnderdogPriceCeiling;
        row["suspiciousMarketGap"] = result.SuspiciousMarketGap;
        row["marketGap"] = result.MarketGap;
        row["alternateLineGroupKey"] = BuildLikelyHitExposureKey(row);
        row["selectedRecommendedLine"] = false;
        row["balancedLineSelectionReason"] = result.BalancedLineSelectionReason;
        row["isTopPick"] = result.IsTopPick;
        row["canShowAsPick"] = result.CanShowAsPick;
        row["canAddLeg"] = result.CanAddLeg;
        row["hitChanceThreshold"] = result.HitChanceThreshold;
        row["minimumHitChanceGate"] = result.MinimumHitChanceGate;
        row["minimumHitChanceRequired"] = result.MinimumHitChanceRequired;
        row["hitChanceGatePassed"] = result.HitChanceGatePassed;
        row["publicTopPickMinimumHitChance"] = result.PublicTopPickMinimumHitChance;
        row["publicTopPickHitChanceGatePassed"] = result.PublicTopPickHitChanceGatePassed;
        row["reliabilityThreshold"] = result.ReliabilityThreshold;
        row["valueScore"] = result.ValueScore;
        row["freshnessScore"] = result.FreshnessScore;

        if (topPicksModelOptions.IsLikelyHitV1Enabled && result.HitChance.HasValue)
        {
            row["selectedSideProbability"] = result.HitChance;
            row["bestProbability"] = result.HitChance;
            row["modelProbabilityAvailable"] = true;
            row["hitChanceUnavailableReason"] = null;
            var impliedProbability = ReadLikelyHitDecimal(row, "bestImpliedProbability");
            row["bestProbabilityEdge"] = impliedProbability.HasValue
                ? Math.Round(result.HitChance.Value - impliedProbability.Value, 6)
                : null;
        }

        row["isRecommendation"] = result.CanShowAsPick;
        row["recommendationActionableForUser"] = result.CanShowAsPick;
        row["recommendationType"] = result.CanShowAsPick
            ? ReadLikelyHitString(row, "providerCategory")
            : null;
        row["isCandidate"] = result.CanShowAsPick;
        if (result.CanShowAsPick)
        {
            row["strategyClassification"] = "Candidate";
            row["strategyTier"] = "LikelyHitV1";
            row["strategyReasons"] = new JsonArray();
            row["riskTags"] = new JsonArray();
            row["isResearchOnly"] = false;
            row["sportsbookContextResearchOnly"] = false;
        }

        return result;
    }

    private static LikelyHitOpportunityInput BuildLikelyHitInput(
        JsonObject row,
        IReadOnlyList<LikelyHitObservation>? history = null,
        bool forceHistoricalOpportunityActive = false)
    {
        var sport = ReadLikelyHitString(row, "sport") ?? "MLB";
        var providerCategory = ReadLikelyHitString(row, "providerCategory") ?? "DFS";
        var selectedSide = ReadLikelyHitString(row, "bestSide");
        var selectedSideProbability = ReadLikelyHitDecimal(row, "rawSelectedSideProbability")
            ?? ReadLikelyHitDecimal(row, "selectedSideProbability")
            ?? ReadLikelyHitDecimal(row, "bestProbability");
        var priorProbability = string.Equals(selectedSide, "Under", StringComparison.OrdinalIgnoreCase)
            ? ReadLikelyHitDecimal(row, "rawPriorUnderRate")
            : ReadLikelyHitDecimal(row, "rawPriorOverRate");
        var lifecycleStatus = forceHistoricalOpportunityActive ? "Active" : ReadLikelyHitString(row, "lifecycleStatus");
        var isFresh = forceHistoricalOpportunityActive
            ? true
            : ReadLikelyHitBool(row, "isFresh") ?? string.Equals(lifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase);
        var isStale = forceHistoricalOpportunityActive
            ? false
            : ReadLikelyHitBool(row, "isStale") ?? string.Equals(lifecycleStatus, "Stale", StringComparison.OrdinalIgnoreCase);
        var isExpired = forceHistoricalOpportunityActive
            ? false
            : ReadLikelyHitBool(row, "isExpired") ?? string.Equals(lifecycleStatus, "Expired", StringComparison.OrdinalIgnoreCase);
        var isLifecycleActive = string.Equals(lifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase) && isFresh && !isStale && !isExpired;
        var isSportsbook = string.Equals(providerCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase);
        var hasPrice = ReadLikelyHitDecimal(row, "bestDecimalPrice").HasValue ||
            ReadLikelyHitInt(row, "bestAmericanPrice").HasValue ||
            ReadLikelyHitDecimal(row, "bestImpliedProbability").HasValue;
        var historicalObservations = history ?? Array.Empty<LikelyHitObservation>();
        var rawSampleSize = historicalObservations.Count > 0
            ? historicalObservations.Count
            : ReadLikelyHitInt(row, "historicalSampleSize") ?? 0;
        var supportedPropType = !string.Equals(sport, "SOCCER", StringComparison.OrdinalIgnoreCase) ||
            SoccerWorldCupPredictionService.ResolveSupportedStatKey(
                ReadLikelyHitString(row, "normalizedPropType"),
                ReadLikelyHitString(row, "marketKey")) is not null;

        return new LikelyHitOpportunityInput(
            sport,
            ReadLikelyHitString(row, "competition"),
            providerCategory,
            ReadLikelyHitString(row, "player"),
            ReadLikelyHitString(row, "normalizedPropType"),
            selectedSide,
            ReadLikelyHitDecimal(row, "line"),
            ReadLikelyHitDateOnly(row, "expectedResultDate") ?? ReadLikelyHitDateOnly(row, "eventDate"),
            isLifecycleActive,
            isFresh,
            isStale,
            isExpired,
            hasPrice,
            PriceRequired: isSportsbook,
            supportedPropType,
            RequiresPlayerMapping: true,
            HasApprovedPlayerMapping: rawSampleSize > 0,
            ReadLikelyHitBool(row, "projectionProbabilityConflict") ?? false,
            ReadLikelyHitBool(row, "recentWeightedProjectionProbabilityConflict") ?? false,
            selectedSideProbability,
            priorProbability,
            ReadLikelyHitDecimal(row, "bestImpliedProbability"),
            ReadLikelyHitInt(row, "bestAmericanPrice"),
            ReadLikelyHitDecimal(row, "bestDecimalPrice"),
            rawSampleSize,
            ReadLikelyHitBool(row, "entryActionableForUser") ?? false,
            historicalObservations);
    }

    private static string BuildLikelyHitExposureKey(JsonObject row)
    {
        var sport = ReadLikelyHitString(row, "sport") ?? "MLB";
        var competition = ReadLikelyHitString(row, "competition") ?? sport;
        var eventId = ReadLikelyHitString(row, "eventId")
            ?? $"{ReadLikelyHitString(row, "awayTeam")}@{ReadLikelyHitString(row, "homeTeam")}";
        var playerId = ReadLikelyHitString(row, "canonicalPlayerId")
            ?? ReadLikelyHitString(row, "playerId")
            ?? ReadLikelyHitString(row, "player")
            ?? "unknown-player";
        var propType = ReadLikelyHitString(row, "normalizedPropType") ?? "unknown-prop";
        var side = ReadLikelyHitString(row, "bestSide") ?? "unknown-side";
        var providerCategory = ReadLikelyHitString(row, "providerCategory") ?? "unknown-provider-category";
        return string.Join("|", sport, competition, eventId, playerId, propType, side, providerCategory);
    }

    internal static object BuildLikelyHitSummary(IReadOnlyCollection<JsonObject> rows)
    {
        return new
        {
            modelVersion = LikelyHitModelService.ModelVersion,
            probabilityModelVersion = LikelyHitModelService.ProbabilityModelVersion,
            recommendationContractVersion = LikelyHitModelService.RecommendationContractVersion,
            returnedTopPicks = rows.Count,
            activeCandidateCount = rows.Count,
            recommendationActionableCount = rows.Count(row => ReadLikelyHitBool(row, "recommendationActionableForUser") == true),
            entryActionableCandidateCount = rows.Count(row => ReadLikelyHitBool(row, "entryActionableForUser") == true),
            actionableCandidateCount = rows.Count(row => ReadLikelyHitBool(row, "entryActionableForUser") == true),
            dfsCandidateCount = rows.Count(row => string.Equals(ReadLikelyHitString(row, "providerCategory"), "DFS", StringComparison.OrdinalIgnoreCase)),
            sportsbookContextCandidateCount = rows.Count(row => string.Equals(ReadLikelyHitString(row, "providerCategory"), "Sportsbook", StringComparison.OrdinalIgnoreCase)),
            averageHitChance = AverageLikelyHitDecimal(rows.Select(row => ReadLikelyHitDecimal(row, "hitChance"))),
            averageReliabilityScore = AverageLikelyHitDecimal(rows.Select(row => ReadLikelyHitDecimal(row, "reliabilityScore"))),
            averageTopPickScore = AverageLikelyHitDecimal(rows.Select(row => ReadLikelyHitDecimal(row, "topPickScore"))),
            averagePayoutQualityScore = AverageLikelyHitDecimal(rows.Select(row => ReadLikelyHitDecimal(row, "payoutQualityScore"))),
            averageSweetSpotScore = AverageLikelyHitDecimal(rows.Select(row => ReadLikelyHitDecimal(row, "sweetSpotScore"))),
            averageBalancedLineUtility = AverageLikelyHitDecimal(rows.Select(row => ReadLikelyHitDecimal(row, "balancedLineUtility"))),
            sportsbookRowsBlockedByBalancedPrice = rows.Count(row =>
                string.Equals(ReadLikelyHitString(row, "priceGateReason"), "SportsbookPriceTooExpensiveForBalancedTopPick", StringComparison.OrdinalIgnoreCase)),
            sportsbookRowsBlockedByBalancedLongshot = rows.Count(row =>
                string.Equals(ReadLikelyHitString(row, "priceGateReason"), "SportsbookPriceTooLongshotForBalancedTopPick", StringComparison.OrdinalIgnoreCase)),
            sportsbookRowsBlockedBySuspiciousMarketGap = rows.Count(row =>
                ReadLikelyHitString(row, "balancedLineSelectionReason")?.Contains("SportsbookSuspiciousModelMarketGap", StringComparison.OrdinalIgnoreCase) == true)
        };
    }

    private static async Task<IReadOnlyList<JsonObject>> BuildLikelyHitReplaySourceRowsAsync(
        string normalizedSport,
        string? competition,
        string? normalizedProviderCategory,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? propType,
        string? side,
        int maxRows,
        DateTimeOffset generatedAtUtc,
        int freshnessMinutes,
        ApplicationDbContext dbContext,
        IPredictionProbabilityService probabilityService,
        PredictionGenerationOptions selectionOptions,
        CancellationToken cancellationToken)
    {
        var snapshotQuery = dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == normalizedSport);

        if (!string.IsNullOrWhiteSpace(normalizedProviderCategory))
        {
            snapshotQuery = snapshotQuery.Where(row => row.ProviderCategory == normalizedProviderCategory);
        }

        if (dateFrom.HasValue)
        {
            snapshotQuery = snapshotQuery.Where(row => row.SlateDate >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            snapshotQuery = snapshotQuery.Where(row => row.SlateDate <= dateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            snapshotQuery = snapshotQuery.Where(row => row.PropType == propType);
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            snapshotQuery = snapshotQuery.Where(row => row.BestSide == side);
        }

        var snapshotRows = await snapshotQuery
            .OrderByDescending(row => row.GeneratedAtUtc)
            .ThenBy(row => row.Player)
            .ThenBy(row => row.PropType)
            .Take(maxRows)
            .ToListAsync(cancellationToken);

        if (snapshotRows.Count > 0)
        {
            return snapshotRows
                .Select(row => BuildLikelyHitReplaySnapshotRow(row, competition))
                .Where(row => string.IsNullOrWhiteSpace(competition) ||
                    string.Equals(ReadLikelyHitString(row, "competition"), competition, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!string.Equals(normalizedSport, "MLB", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<JsonObject>();
        }

        var platformProps = await LoadScopedTheOddsPlatformPropsAsync(dbContext, normalizedProviderCategory, cancellationToken);
        var filteredProps = platformProps
            .Where(row =>
            {
                var eventDate = DateOnly.FromDateTime((row.EventStartTime ?? row.ObservedAt).UtcDateTime);
                return (!dateFrom.HasValue || eventDate >= dateFrom.Value)
                    && (!dateTo.HasValue || eventDate <= dateTo.Value);
            })
            .ToList();

        var probabilityAnalysis = await LoadProbabilityAnalysisAsync(
            runId: null,
            filteredProps.Select(row => row.PropId).Distinct().ToArray(),
            probabilityService,
            cancellationToken);
        var rows = BuildTheOddsLineShoppingRows(filteredProps, probabilityAnalysis, generatedAtUtc.AddHours(-FreshnessWindowHours));
        var groups = BuildTheOddsLineShoppingGroups(rows, generatedAtUtc, freshnessMinutes, selectionOptions)
            .Where(group => string.IsNullOrWhiteSpace(propType) ||
                string.Equals(group.NormalizedPropType, propType, StringComparison.OrdinalIgnoreCase))
            .Where(group => string.IsNullOrWhiteSpace(side) ||
                string.Equals(group.BestSide, side, StringComparison.OrdinalIgnoreCase))
            .Take(maxRows)
            .ToArray();

        return groups
            .Select(group => JsonSerializer.SerializeToNode(ToLineShoppingResponseGroup(group, includeBookmakerPrices: false)) as JsonObject)
            .Where(row => row is not null)
            .Select(row =>
            {
                row!["replaySource"] = "currentPlatformPropsFallback";
                return row!;
            })
            .ToArray();
    }

    private static JsonObject BuildLikelyHitReplaySnapshotRow(MarketLineShoppingSnapshot row, string? competitionFilter)
    {
        var node = JsonSerializer.SerializeToNode(row.DetailsJson.RootElement) as JsonObject ?? [];
        node["replaySource"] = "marketLineShoppingSnapshot";
        node["snapshotId"] = row.Id;
        node["sport"] = ReadLikelyHitString(node, "sport") ?? row.Sport;
        node["competition"] = ReadLikelyHitString(node, "competition") ?? (string.IsNullOrWhiteSpace(competitionFilter) ? row.Sport : competitionFilter);
        node["providerCategory"] = ReadLikelyHitString(node, "providerCategory") ?? row.ProviderCategory;
        node["player"] = ReadLikelyHitString(node, "player") ?? row.Player;
        node["eventId"] = ReadLikelyHitString(node, "eventId") ?? row.EventId;
        node["eventStartTime"] = ReadLikelyHitString(node, "eventStartTime") ?? row.EventStartTime?.ToString("O");
        node["eventStartTimeUtc"] = ReadLikelyHitString(node, "eventStartTimeUtc") ?? row.EventStartTime?.ToString("O");
        node["expectedResultDate"] = ReadLikelyHitString(node, "expectedResultDate") ?? row.SlateDate.ToString("O");
        node["eventDate"] = ReadLikelyHitString(node, "eventDate") ?? row.SlateDate.ToString("O");
        node["normalizedPropType"] = ReadLikelyHitString(node, "normalizedPropType") ?? row.PropType;
        node["line"] = ReadLikelyHitDecimal(node, "line") ?? row.Line;
        node["bestSide"] = ReadLikelyHitString(node, "bestSide") ?? row.BestSide;
        node["bestBookmaker"] = ReadLikelyHitString(node, "bestBookmaker") ?? row.BestBookmaker ?? row.Bookmaker;
        node["bestAmericanPrice"] = ReadLikelyHitInt(node, "bestAmericanPrice") ?? row.BestAmericanPrice;
        node["bestDecimalPrice"] = ReadLikelyHitDecimal(node, "bestDecimalPrice") ?? row.BestDecimalPrice;
        node["bestProbability"] = ReadLikelyHitDecimal(node, "bestProbability") ?? row.BestProbability;
        node["selectedSideProbability"] = ReadLikelyHitDecimal(node, "selectedSideProbability") ?? row.BestProbability;
        node["bestImpliedProbability"] = ReadLikelyHitDecimal(node, "bestImpliedProbability") ?? row.BestImpliedProbability;
        node["lifecycleStatus"] = ReadLikelyHitString(node, "lifecycleStatus") ?? row.LifecycleStatus;
        node["isStale"] = ReadLikelyHitBool(node, "isStale") ?? row.IsStale;
        node["isExpired"] = ReadLikelyHitBool(node, "isExpired") ?? row.IsExpired;
        node["isFresh"] = ReadLikelyHitBool(node, "isFresh") ?? (!row.IsStale && !row.IsExpired);
        node["entryActionableForUser"] = ReadLikelyHitBool(node, "entryActionableForUser") ?? row.EntryActionableForUser;
        return node;
    }

    private static async Task<object> BuildLikelyHitReplayResponseAsync(
        IReadOnlyList<JsonObject> sourceRows,
        DateTimeOffset generatedAtUtc,
        string normalizedSport,
        string? competition,
        string? normalizedProviderCategory,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? propType,
        string? side,
        bool includeSamples,
        int sampleTake,
        string? baselineMode,
        LikelyHitBorderlineSimulationOptions borderlineSimulationOptions,
        LikelyHitPublicTopPickSimulationOptions publicTopPickSimulationOptions,
        ILikelyHitModelService likelyHitModel,
        TopPicksModelOptions topPicksModelOptions,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var evaluated = sourceRows
            .Select(row =>
            {
                var result = ApplyLikelyHitMetadata(row, likelyHitModel, topPicksModelOptions);
                return new LikelyHitReplayEvaluatedRow(row, result);
            })
            .ToArray();

        var selectedRows = evaluated
            .Where(row => row.Result.CanShowAsPick)
            .GroupBy(row => ReadLikelyHitString(row.Row, "alternateLineGroupKey") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.Result.LineUtilityScore ?? decimal.MinValue)
                .ThenByDescending(row => row.Result.TopPickScore ?? decimal.MinValue)
                .ThenByDescending(row => row.Result.HitChance ?? decimal.MinValue)
                .ThenByDescending(row => ReadLikelyHitDecimal(row.Row, "bestDecimalPrice") ?? decimal.MinValue)
                .First())
            .Select(row => row.Row)
            .ToHashSet();

        foreach (var row in evaluated)
        {
            var selectedRecommendedLine = selectedRows.Contains(row.Row);
            row.Row["selectedRecommendedLine"] = selectedRecommendedLine;
            if (selectedRecommendedLine)
            {
                row.Row["balancedLineSelectionReason"] = "SelectedBestBalancedLineInAlternateLadder";
            }
        }

        var replayRows = await BuildLikelyHitReplayRowsAsync(
            evaluated,
            selectedRows,
            likelyHitModel,
            dbContext,
            cancellationToken);
        var gradedRows = replayRows.Where(row => row.IsGraded).ToArray();
        var topPickRows = replayRows.Where(row => row.IsTopPick).ToArray();
        var nonTopPickRows = replayRows.Where(row => !row.IsTopPick).ToArray();
        var topPickGradedRows = topPickRows.Where(row => row.IsGraded).ToArray();
        var nonTopPickGradedRows = nonTopPickRows.Where(row => row.IsGraded).ToArray();

        var hitChanceBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => BuildLikelyHitHitChanceBucket(row.HitChance));
        var sportCategoryBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => $"{row.Sport} {row.Competition} {row.ProviderCategory}".Trim());
        var propShapeBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => $"{row.Sport} {row.PropType} {row.Side} {BuildLikelyHitLineBucket(row.PropType, row.Line)}");
        var oddsBuckets = BuildLikelyHitReplayBucketSummary(
            replayRows.Where(row => string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)),
            row => BuildLikelyHitOddsBucket(row.AmericanPrice));
        var sideBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => string.IsNullOrWhiteSpace(row.Side) ? "Unknown" : row.Side);
        var sampleBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => BuildLikelyHitSampleBucket(row.EffectiveSampleSize));
        var reliabilityBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => BuildLikelyHitScoreBucket(row.ReliabilityScore, "Reliability"));
        var payoutQualityBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => BuildLikelyHitScoreBucket(row.PayoutQualityScore, "PayoutQuality"));
        var sweetSpotBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => BuildLikelyHitScoreBucket(row.SweetSpotScore, "SweetSpot"));
        var utilityBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => BuildLikelyHitScoreBucket(row.BalancedLineUtility, "BalancedLineUtility"));
        var selectedLineBuckets = BuildLikelyHitReplayBucketSummary(replayRows, row => row.SelectedRecommendedLine ? "selectedRecommendedLine=true" : "selectedRecommendedLine=false");
        var borderlineHitChanceAnalysis = BuildLikelyHitBorderlineHitChanceAnalysis(replayRows);
        var borderlineProfileRecommendations = BuildLikelyHitBorderlineProfileRecommendations(replayRows);
        var currentBoardBorderlineSummary = BuildLikelyHitCurrentBoardBorderlineSummary(replayRows);
        var simulation = BuildLikelyHitBorderlineSimulation(replayRows, borderlineProfileRecommendations, borderlineSimulationOptions);
        var publicTopPickPerformanceAnalysis = BuildLikelyHitPublicTopPickPerformanceAnalysis(replayRows);
        var publicTopPickConcentrationAnalysis = BuildLikelyHitPublicTopPickConcentrationAnalysis(replayRows);
        var publicTopPickProfileRecommendations = BuildLikelyHitPublicTopPickProfileRecommendations(replayRows);
        var publicTopPickSimulation = BuildLikelyHitPublicTopPickSimulation(
            replayRows,
            publicTopPickProfileRecommendations,
            publicTopPickSimulationOptions);
        var currentPublicTopPickSummary = BuildLikelyHitCurrentPublicTopPickSummary(replayRows);

        return new
        {
            generatedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            writesRows = false,
            modelVersion = LikelyHitModelService.ModelVersion,
            probabilityModelVersion = LikelyHitModelService.ProbabilityModelVersion,
            recommendationContractVersion = LikelyHitModelService.RecommendationContractVersion,
            baselineMode = string.IsNullOrWhiteSpace(baselineMode) ? "LikelyHitV1RecomputedFromPersistedOpportunities" : baselineMode,
            filters = new
            {
                sport = normalizedSport,
                competition,
                providerCategory = normalizedProviderCategory,
                dateFrom,
                dateTo,
                propType,
                side,
                simulation = new
                {
                    simulateBorderlineProfileGate = borderlineSimulationOptions.Enabled,
                    minBorderlineBreakEvenMargin = borderlineSimulationOptions.MinBreakEvenMargin,
                    minBorderlineEffectiveSampleSize = borderlineSimulationOptions.MinEffectiveSampleSize,
                    borderlineWorstAllowedFavoriteAmericanOdds = borderlineSimulationOptions.WorstAllowedFavoriteAmericanOdds,
                    borderlineBestAllowedUnderdogAmericanOdds = borderlineSimulationOptions.BestAllowedUnderdogAmericanOdds,
                    requireBorderlineProfileAllow = borderlineSimulationOptions.RequireProfileAllow
                },
                publicTopPickSimulation = new
                {
                    simulatePublicTopPickProfileGate = publicTopPickSimulationOptions.Enabled,
                    requirePublicProfileAllow = publicTopPickSimulationOptions.RequireProfileAllow,
                    blockPublicProfileBlock = publicTopPickSimulationOptions.BlockProfileBlock,
                    penalizePublicProfilePenalize = publicTopPickSimulationOptions.PenalizeProfilePenalize,
                    maxPropShapeShare = publicTopPickSimulationOptions.MaxPropShapeShare,
                    maxRowsPerPropShape = publicTopPickSimulationOptions.MaxRowsPerPropShape,
                    minPublicBreakEvenMargin = publicTopPickSimulationOptions.MinBreakEvenMargin,
                    minPublicBalancedUtility = publicTopPickSimulationOptions.MinBalancedUtility
                },
                includeSamples,
                sampleTake
            },
            replayRowsEvaluated = replayRows.Count,
            replayableRows = replayRows.Count(row => row.ReplayStatus == "Replayable"),
            gradedRows = gradedRows.Length,
            ungradedRows = replayRows.Count(row => !row.IsGraded),
            topPickRows = topPickRows.Length,
            nonTopPickRows = nonTopPickRows.Length,
            overallHitRate = CalculateLikelyHitReplayHitRate(gradedRows),
            topPickHitRate = CalculateLikelyHitReplayHitRate(topPickGradedRows),
            nonTopPickHitRate = CalculateLikelyHitReplayHitRate(nonTopPickGradedRows),
            roi = CalculateLikelyHitReplayRoi(gradedRows),
            warning = gradedRows.Length < 30
                ? "Replay sample is small; use bucket-level conclusions cautiously."
                : null,
            replayLimitations = new[]
            {
                "The replay recalculates LikelyHitV1 from persisted opportunity rows and never trusts old isCandidate/isRecommendation flags.",
                "Historical rows are graded only when local prop_results match player, prop type, and expected result date.",
                "Rows with missing player ids, odds, pre-event history, or results are classified instead of forced into replay metrics.",
                "Snapshot rows may not contain every raw feature needed to perfectly reconstruct a past prediction run; stored opportunity probabilities are re-gated through LikelyHitV1."
            },
            notReplayableClassifications = new[]
            {
                "NotReplayableMissingHistory",
                "NotReplayableMissingOdds",
                "NotReplayableMissingResult",
                "NotReplayableUnsupportedSport",
                "NotReplayableUnknown"
            },
            statusBreakdown = replayRows
                .GroupBy(row => row.ReplayStatus, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { status = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ToArray(),
            hitChanceBuckets,
            sportCompetitionProviderCategoryBuckets = sportCategoryBuckets,
            propShapeBuckets,
            oddsBuckets,
            sideBuckets,
            effectiveSampleBuckets = sampleBuckets,
            reliabilityBuckets,
            payoutQualityBuckets,
            sweetSpotBuckets,
            balancedLineUtilityBuckets = utilityBuckets,
            selectedRecommendedLineBuckets = selectedLineBuckets,
            borderlineHitChanceAnalysis,
            borderlineProfileRecommendations,
            currentBoardBorderlineSummary,
            simulation,
            publicTopPickPerformanceAnalysis,
            publicTopPickConcentrationAnalysis,
            publicTopPickProfileRecommendations,
            publicTopPickSimulation,
            currentPublicTopPickSummary,
            recommendations = BuildLikelyHitReplayRecommendations(replayRows, propShapeBuckets, hitChanceBuckets, sideBuckets, oddsBuckets),
            samples = includeSamples
                ? new
                {
                    topPicks = topPickRows
                        .OrderByDescending(row => row.TopPickScore ?? decimal.MinValue)
                        .Take(sampleTake)
                        .Select(ToLikelyHitReplaySample)
                        .ToArray(),
                    ungraded = replayRows
                        .Where(row => !row.IsGraded)
                        .Take(sampleTake)
                        .Select(ToLikelyHitReplaySample)
                        .ToArray(),
                    rejected = replayRows
                        .Where(row => !row.IsTopPick)
                        .OrderByDescending(row => row.HitChance ?? decimal.MinValue)
                        .Take(sampleTake)
                        .Select(ToLikelyHitReplaySample)
                        .ToArray()
                }
                : null,
            borderlineSamples = includeSamples
                ? BuildLikelyHitBorderlineSamples(replayRows, sampleTake)
                : null,
            publicTopPickSamples = includeSamples
                ? BuildLikelyHitPublicTopPickSamples(replayRows, sampleTake)
                : null
        };
    }

    private static async Task<IReadOnlyList<LikelyHitReplayRow>> BuildLikelyHitReplayRowsAsync(
        IReadOnlyList<LikelyHitReplayEvaluatedRow> evaluatedRows,
        IReadOnlySet<JsonObject> selectedRows,
        ILikelyHitModelService likelyHitModel,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var propTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(propType => new
            {
                propType.Id,
                propType.Name,
                Sport = propType.Sport == null ? null : propType.Sport.Code
            })
            .ToListAsync(cancellationToken);
        var propTypeIdsBySportAndName = propTypes
            .GroupBy(row => $"{row.Sport ?? string.Empty}|{CanonicalTheOddsPropTypeName(row.Name)}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Id).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var propTypeIdsByName = propTypes
            .GroupBy(row => CanonicalTheOddsPropTypeName(row.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Id).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var seeds = evaluatedRows
            .Select(row =>
            {
                var sport = ReadLikelyHitString(row.Row, "sport") ?? "MLB";
                var propType = ReadLikelyHitString(row.Row, "normalizedPropType") ?? string.Empty;
                var propKey = CanonicalTheOddsPropTypeName(propType);
                var propTypeIds = propTypeIdsBySportAndName.TryGetValue($"{sport}|{propKey}", out var sportIds)
                    ? sportIds
                    : propTypeIdsByName.GetValueOrDefault(propKey) ?? Array.Empty<Guid>();
                return new
                {
                    Row = row,
                    PlayerId = ReadLikelyHitGuid(row.Row, "canonicalPlayerId") ?? ReadLikelyHitGuid(row.Row, "playerId"),
                    PropTypeIds = propTypeIds,
                    EventDate = ReadLikelyHitDateOnly(row.Row, "expectedResultDate") ?? ReadLikelyHitDateOnly(row.Row, "eventDate")
                };
            })
            .ToArray();

        var playerIds = seeds
            .Where(row => row.PlayerId.HasValue)
            .Select(row => row.PlayerId!.Value)
            .Distinct()
            .ToArray();
        var allPropTypeIds = seeds
            .SelectMany(row => row.PropTypeIds)
            .Distinct()
            .ToArray();
        var maxDate = seeds
            .Where(row => row.EventDate.HasValue)
            .Select(row => row.EventDate!.Value)
            .DefaultIfEmpty(DateOnly.MinValue)
            .Max();

        var resultRows = playerIds.Length == 0 || allPropTypeIds.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(result => playerIds.Contains(result.PlayerId)
                    && allPropTypeIds.Contains(result.PropTypeId)
                    && result.EventDate <= maxDate)
                .Select(result => new
                {
                    result.PlayerId,
                    result.PropTypeId,
                    result.EventDate,
                    result.ActualResultValue
                })
                .ToListAsync(cancellationToken);

        var preparedRows = seeds
            .Select(seed =>
            {
                var row = seed.Row.Row;
                var side = ReadLikelyHitString(row, "bestSide") ?? string.Empty;
                var line = ReadLikelyHitDecimal(row, "line");
                var matchingResults = seed.PlayerId.HasValue && seed.PropTypeIds.Length > 0
                    ? resultRows
                        .Where(resultRow => resultRow.PlayerId == seed.PlayerId.Value && seed.PropTypeIds.Contains(resultRow.PropTypeId))
                        .ToArray()
                    : [];
                var historyBeforeEvent = seed.EventDate.HasValue
                    ? matchingResults.Count(resultRow => resultRow.EventDate < seed.EventDate.Value)
                    : 0;
                var actual = seed.EventDate.HasValue
                    ? matchingResults
                        .Where(resultRow => resultRow.EventDate == seed.EventDate.Value)
                        .Select(resultRow => (decimal?)resultRow.ActualResultValue)
                        .FirstOrDefault()
                    : null;
                var outcome = actual.HasValue && line.HasValue && IsLikelyHitReplaySide(side)
                    ? ClassifyLikelyHitReplayOutcome(actual.Value, line.Value, side)
                    : (int?)null;
                var decimalPrice = ReadLikelyHitDecimal(row, "bestDecimalPrice");
                var profit = outcome.HasValue && decimalPrice.HasValue
                    ? outcome.Value > 0
                        ? decimalPrice.Value - 1m
                        : outcome.Value < 0 ? -1m : 0m
                    : (decimal?)null;
                var history = seed.EventDate.HasValue
                    ? matchingResults
                        .Where(resultRow => resultRow.EventDate < seed.EventDate.Value)
                        .OrderByDescending(resultRow => resultRow.EventDate)
                        .Select(resultRow => new LikelyHitObservation(
                            resultRow.EventDate,
                            resultRow.ActualResultValue,
                            ReadLikelyHitString(row, "competition"),
                            SameCompetition: true))
                        .ToArray()
                    : Array.Empty<LikelyHitObservation>();
                var historicalResult = likelyHitModel.Evaluate(BuildLikelyHitInput(
                    row,
                    history,
                    forceHistoricalOpportunityActive: true));
                var replayStatus = ResolveLikelyHitReplayStatus(row, seed.PlayerId, seed.PropTypeIds, seed.EventDate, historyBeforeEvent, actual, historicalResult);

                return new
                {
                    Row = row,
                    CurrentResult = seed.Row.Result,
                    HistoricalResult = historicalResult,
                    Side = side,
                    Line = line,
                    EventDate = seed.EventDate,
                    DecimalPrice = decimalPrice,
                    ReplayStatus = replayStatus,
                    HistoryRowsBeforeEvent = historyBeforeEvent,
                    Actual = actual,
                    Outcome = outcome,
                    Profit = profit
                };
            })
            .ToArray();

        var historicalSelectedRows = preparedRows
            .Where(row => row.HistoricalResult.CanShowAsPick)
            .GroupBy(row => BuildLikelyHitExposureKey(row.Row), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.HistoricalResult.LineUtilityScore ?? decimal.MinValue)
                .ThenByDescending(row => row.HistoricalResult.TopPickScore ?? decimal.MinValue)
                .ThenByDescending(row => row.HistoricalResult.HitChance ?? decimal.MinValue)
                .ThenByDescending(row => ReadLikelyHitDecimal(row.Row, "bestDecimalPrice") ?? decimal.MinValue)
                .First())
            .Select(row => row.Row)
            .ToHashSet();

        return preparedRows
            .Select(item =>
            {
                var row = item.Row;
                var result = item.CurrentResult;
                var historicalResult = item.HistoricalResult;

                return new LikelyHitReplayRow(
                    ReadLikelyHitString(row, "sport") ?? "MLB",
                    ReadLikelyHitString(row, "competition") ?? ReadLikelyHitString(row, "sport") ?? "MLB",
                    ReadLikelyHitString(row, "providerCategory") ?? "Unknown",
                    ReadLikelyHitString(row, "player") ?? string.Empty,
                    ReadLikelyHitString(row, "normalizedPropType") ?? string.Empty,
                    item.Side,
                    item.Line,
                    item.EventDate,
                    ReadLikelyHitString(row, "bestBookmaker") ?? ReadLikelyHitString(row, "bookmaker") ?? ReadLikelyHitString(row, "provider") ?? ReadLikelyHitString(row, "entryProvider"),
                    ReadLikelyHitInt(row, "bestAmericanPrice"),
                    item.DecimalPrice,
                    ReadLikelyHitDecimal(row, "bestImpliedProbability"),
                    result.MarketGap,
                    result.HitChance,
                    result.EffectiveSampleSize,
                    result.ReliabilityScore,
                    result.PayoutQualityScore,
                    result.SweetSpotScore,
                    result.BalancedLineUtility,
                    result.TopPickScore,
                    result.MinimumHitChanceGate,
                    result.MinimumHitChanceRequired,
                    result.HitChanceGatePassed,
                    result.FatalConflictCodes,
                    selectedRows.Contains(row),
                    result.CanShowAsPick && selectedRows.Contains(row),
                    item.ReplayStatus,
                    item.HistoryRowsBeforeEvent,
                    item.Actual,
                    item.Outcome,
                    item.Profit,
                    historicalSelectedRows.Contains(row),
                    historicalResult.CanShowAsPick && historicalSelectedRows.Contains(row),
                    historicalResult.HitChance,
                    historicalResult.EffectiveSampleSize,
                    historicalResult.ReliabilityScore,
                    historicalResult.PayoutQualityScore,
                    historicalResult.SweetSpotScore,
                    historicalResult.BalancedLineUtility,
                    historicalResult.TopPickScore,
                    historicalResult.MinimumHitChanceGate,
                    historicalResult.MinimumHitChanceRequired,
                    historicalResult.HitChanceGatePassed,
                    historicalResult.FatalConflictCodes);
            })
            .ToArray();
    }

    private static string ResolveLikelyHitReplayStatus(
        JsonObject row,
        Guid? playerId,
        Guid[] propTypeIds,
        DateOnly? eventDate,
        int historyBeforeEvent,
        decimal? actualResult,
        LikelyHitModelResult result)
    {
        if (!string.Equals(ReadLikelyHitString(row, "sport"), "MLB", StringComparison.OrdinalIgnoreCase))
        {
            return "NotReplayableUnsupportedSport";
        }

        if (!playerId.HasValue || propTypeIds.Length == 0)
        {
            return "NotReplayableMissingHistory";
        }

        if (string.Equals(ReadLikelyHitString(row, "providerCategory"), "Sportsbook", StringComparison.OrdinalIgnoreCase)
            && !result.EffectiveAmericanOdds.HasValue)
        {
            return "NotReplayableMissingOdds";
        }

        if (!eventDate.HasValue || !actualResult.HasValue)
        {
            return "NotReplayableMissingResult";
        }

        return historyBeforeEvent <= 0 ? "NotReplayableMissingHistory" : "Replayable";
    }

    private static LikelyHitReplayBucket[] BuildLikelyHitReplayBucketSummary(
        IEnumerable<LikelyHitReplayRow> sourceRows,
        Func<LikelyHitReplayRow, string> bucketSelector)
    {
        return sourceRows
            .GroupBy(bucketSelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rows = group.ToArray();
                var graded = rows.Where(row => row.IsGraded).ToArray();
                var wins = graded.Count(row => row.Outcome > 0);
                var losses = graded.Count(row => row.Outcome < 0);
                var pushes = graded.Count(row => row.Outcome == 0);
                var averageHitChance = AverageLikelyHitDecimal(rows.Select(row => row.HitChance));
                var hitRate = CalculateLikelyHitReplayHitRate(graded);
                return new LikelyHitReplayBucket(
                    group.Key,
                    rows.Length,
                    graded.Length,
                    wins,
                    losses,
                    pushes,
                    hitRate,
                    averageHitChance,
                    hitRate.HasValue && averageHitChance.HasValue ? Math.Round(averageHitChance.Value - hitRate.Value, 6) : null,
                    CalculateLikelyHitReplayRoi(graded),
                    rows.Count(row => row.IsTopPick));
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Bucket, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static object BuildLikelyHitBorderlineHitChanceAnalysis(IReadOnlyList<LikelyHitReplayRow> rows)
    {
        var borderlineRows = SelectLikelyHitBorderlineSportsbookRows(rows).ToArray();
        var graded = borderlineRows.Where(row => row.IsGraded).ToArray();
        var marketGapMatchesBreakEven = borderlineRows
            .Where(row => row.MarketGap.HasValue && row.BreakEvenMargin.HasValue)
            .All(row => Math.Abs(row.MarketGap!.Value - row.BreakEvenMargin!.Value) <= 0.000001m);

        return new
        {
            bucket = "60-65",
            providerCategory = "Sportsbook",
            scope = "Sportsbook rows with final LikelyHitV1 hitChance >= 0.60 and < 0.65.",
            diagnosticOnly = true,
            total = borderlineRows.Length,
            replayable = borderlineRows.Count(row => row.ReplayStatus == "Replayable"),
            graded = graded.Length,
            wins = graded.Count(row => row.Outcome > 0),
            losses = graded.Count(row => row.Outcome < 0),
            pushes = graded.Count(row => row.Outcome == 0),
            hitRate = CalculateLikelyHitReplayHitRate(graded),
            roi = CalculateLikelyHitReplayRoi(graded),
            averageHitChance = AverageLikelyHitDecimal(borderlineRows.Select(row => row.HitChance)),
            averageImpliedProbability = AverageLikelyHitDecimal(borderlineRows.Select(row => row.ImpliedProbability)),
            averageBreakEvenMargin = AverageLikelyHitDecimal(borderlineRows.Select(row => row.BreakEvenMargin)),
            averageEffectiveSampleSize = AverageLikelyHitDecimal(borderlineRows.Select(row => (decimal?)row.EffectiveSampleSize)),
            recentAgreementStatus = "NotAvailable",
            recentAgreementUnavailableReason = "Replay source rows do not consistently persist enough recent-form direction fields to reconstruct agreement safely.",
            marketGapDefinition = marketGapMatchesBreakEven
                ? "marketGap matches breakEvenMargin for all borderline rows with both values."
                : "marketGap comes from LikelyHitV1 model output and may differ from breakEvenMargin.",
            segments = new
            {
                oddsBucket = BuildLikelyHitBorderlineSegmentSummary(borderlineRows, row => BuildLikelyHitBorderlineOddsBucket(row.AmericanPrice)),
                breakEvenMarginBucket = BuildLikelyHitBorderlineSegmentSummary(borderlineRows, row => BuildLikelyHitBreakEvenMarginBucket(row.BreakEvenMargin)),
                propShape = BuildLikelyHitBorderlineSegmentSummary(borderlineRows, BuildLikelyHitBorderlinePropShapeKey),
                effectiveSampleSizeBucket = BuildLikelyHitBorderlineSegmentSummary(borderlineRows, row => BuildLikelyHitBorderlineSampleBucket(row.EffectiveSampleSize)),
                recentAgreementBucket = BuildLikelyHitBorderlineSegmentSummary(borderlineRows, _ => "NotAvailable"),
                marketGapBucket = BuildLikelyHitBorderlineSegmentSummary(borderlineRows, row => BuildLikelyHitMarketGapBucket(row.MarketGap ?? row.BreakEvenMargin)),
                bookmaker = BuildLikelyHitBorderlineSegmentSummary(borderlineRows, row => string.IsNullOrWhiteSpace(row.Bookmaker) ? "Unknown" : row.Bookmaker!),
                side = BuildLikelyHitBorderlineSegmentSummary(borderlineRows, row => string.IsNullOrWhiteSpace(row.Side) ? "Unknown" : row.Side)
            }
        };
    }

    private static LikelyHitBorderlineProfileRecommendation[] BuildLikelyHitBorderlineProfileRecommendations(IReadOnlyList<LikelyHitReplayRow> rows)
    {
        return SelectLikelyHitBorderlineSportsbookRows(rows)
            .GroupBy(row => string.Join("|",
                BuildLikelyHitBorderlinePropShapeKey(row),
                BuildLikelyHitBorderlineOddsBucket(row.AmericanPrice),
                BuildLikelyHitBreakEvenMarginBucket(row.BreakEvenMargin),
                BuildLikelyHitBorderlineSampleBucket(row.EffectiveSampleSize)), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var materialized = group.ToArray();
                var graded = materialized.Where(row => row.IsGraded).ToArray();
                var hitRate = CalculateLikelyHitReplayHitRate(graded);
                var roi = CalculateLikelyHitReplayRoi(graded);
                var averageHitChance = AverageLikelyHitDecimal(materialized.Select(row => row.HitChance));
                var recommendation = ResolveLikelyHitBorderlineProfileRecommendation(graded.Length, hitRate, roi, averageHitChance, out var reasonCodes);
                var first = materialized[0];
                return new LikelyHitBorderlineProfileRecommendation(
                    group.Key,
                    first.Sport,
                    first.ProviderCategory,
                    BuildLikelyHitBorderlinePropShapeKey(first),
                    BuildLikelyHitBorderlineOddsBucket(first.AmericanPrice),
                    BuildLikelyHitBreakEvenMarginBucket(first.BreakEvenMargin),
                    BuildLikelyHitBorderlineSampleBucket(first.EffectiveSampleSize),
                    materialized.Length,
                    graded.Length,
                    graded.Count(row => row.Outcome > 0),
                    graded.Count(row => row.Outcome < 0),
                    graded.Count(row => row.Outcome == 0),
                    hitRate,
                    roi,
                    averageHitChance,
                    AverageLikelyHitDecimal(materialized.Select(row => row.ImpliedProbability)),
                    AverageLikelyHitDecimal(materialized.Select(row => row.BreakEvenMargin)),
                    recommendation,
                    reasonCodes);
            })
            .OrderByDescending(row => row.Graded)
            .ThenBy(row => row.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static object BuildLikelyHitCurrentBoardBorderlineSummary(IReadOnlyList<LikelyHitReplayRow> rows)
    {
        var topPickRows = rows.Where(row => row.IsTopPick).ToArray();
        var sportsbookTopPickRows = topPickRows
            .Where(row => string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var borderline = SelectLikelyHitBorderlineSportsbookRows(topPickRows).ToArray();

        return new
        {
            currentTopPickCount = topPickRows.Length,
            currentSportsbookTopPickCount = sportsbookTopPickRows.Length,
            current60To65SportsbookTopPickCount = borderline.Length,
            current60To65ByPropShape = BuildLikelyHitBorderlineSegmentSummary(borderline, BuildLikelyHitBorderlinePropShapeKey),
            current60To65ByOddsBucket = BuildLikelyHitBorderlineSegmentSummary(borderline, row => BuildLikelyHitBorderlineOddsBucket(row.AmericanPrice)),
            current60To65ByBreakEvenMargin = BuildLikelyHitBorderlineSegmentSummary(borderline, row => BuildLikelyHitBreakEvenMarginBucket(row.BreakEvenMargin)),
            current60To65ByEffectiveSampleBucket = BuildLikelyHitBorderlineSegmentSummary(borderline, row => BuildLikelyHitBorderlineSampleBucket(row.EffectiveSampleSize)),
            current60To65ByBookmaker = BuildLikelyHitBorderlineSegmentSummary(borderline, row => string.IsNullOrWhiteSpace(row.Bookmaker) ? "Unknown" : row.Bookmaker!),
            current60To65BySide = BuildLikelyHitBorderlineSegmentSummary(borderline, row => string.IsNullOrWhiteSpace(row.Side) ? "Unknown" : row.Side)
        };
    }

    private static object BuildLikelyHitBorderlineSimulation(
        IReadOnlyList<LikelyHitReplayRow> rows,
        IReadOnlyList<LikelyHitBorderlineProfileRecommendation> profileRecommendations,
        LikelyHitBorderlineSimulationOptions options)
    {
        var baselineRows = rows.Where(row => row.IsTopPick).ToArray();
        var borderlineTopPicks = SelectLikelyHitBorderlineSportsbookRows(baselineRows).ToArray();
        var profileRecommendationsByKey = profileRecommendations
            .ToDictionary(row => row.ProfileKey, row => row, StringComparer.OrdinalIgnoreCase);
        var removed = options.Enabled
            ? borderlineTopPicks.Where(row => !PassesLikelyHitBorderlineSimulation(row, profileRecommendationsByKey, options)).ToArray()
            : Array.Empty<LikelyHitReplayRow>();
        var removedSet = removed.ToHashSet();
        var simulatedRows = options.Enabled
            ? baselineRows.Where(row => !removedSet.Contains(row)).ToArray()
            : baselineRows;
        var baselineGraded = baselineRows.Where(row => row.IsGraded).ToArray();
        var simulatedGraded = simulatedRows.Where(row => row.IsGraded).ToArray();

        return new
        {
            enabled = options.Enabled,
            diagnosticOnly = true,
            baselineTopPickRows = baselineRows.Length,
            simulatedTopPickRows = simulatedRows.Length,
            removedRows = removed.Length,
            baselineHitRate = CalculateLikelyHitReplayHitRate(baselineGraded),
            simulatedHitRate = CalculateLikelyHitReplayHitRate(simulatedGraded),
            baselineRoi = CalculateLikelyHitReplayRoi(baselineGraded),
            simulatedRoi = CalculateLikelyHitReplayRoi(simulatedGraded),
            removedBucketBreakdown = BuildLikelyHitBorderlineSegmentSummary(removed, BuildLikelyHitBorderlinePropShapeKey),
            keptBucketBreakdown = BuildLikelyHitBorderlineSegmentSummary(simulatedRows.Intersect(borderlineTopPicks), BuildLikelyHitBorderlinePropShapeKey),
            criteria = new
            {
                minBreakEvenMargin = options.MinBreakEvenMargin,
                minEffectiveSampleSize = options.MinEffectiveSampleSize,
                worstAllowedFavoriteAmericanOdds = options.WorstAllowedFavoriteAmericanOdds,
                bestAllowedUnderdogAmericanOdds = options.BestAllowedUnderdogAmericanOdds,
                requireProfileAllow = options.RequireProfileAllow
            }
        };
    }

    private static object BuildLikelyHitBorderlineSamples(IReadOnlyList<LikelyHitReplayRow> rows, int sampleTake)
    {
        var borderline = SelectLikelyHitBorderlineSportsbookRows(rows).ToArray();
        return new
        {
            best = borderline
                .Where(row => row.IsGraded)
                .OrderByDescending(row => row.Profit ?? decimal.MinValue)
                .ThenByDescending(row => row.HitChance ?? decimal.MinValue)
                .Take(sampleTake)
                .Select(ToLikelyHitBorderlineSample)
                .ToArray(),
            worst = borderline
                .Where(row => row.IsGraded)
                .OrderBy(row => row.Profit ?? decimal.MaxValue)
                .ThenBy(row => row.HitChance ?? decimal.MaxValue)
                .Take(sampleTake)
                .Select(ToLikelyHitBorderlineSample)
                .ToArray(),
            ungradedCurrent = borderline
                .Where(row => !row.IsGraded)
                .OrderByDescending(row => row.IsTopPick)
                .ThenByDescending(row => row.TopPickScore ?? decimal.MinValue)
                .Take(sampleTake)
                .Select(ToLikelyHitBorderlineSample)
                .ToArray()
        };
    }

    private static object BuildLikelyHitPublicTopPickPerformanceAnalysis(IReadOnlyList<LikelyHitReplayRow> rows)
    {
        var publicTopPickRows = rows.Where(row => row.IsHistoricalPublicTopPick).ToArray();
        var graded = publicTopPickRows.Where(row => row.IsGraded).ToArray();

        return new
        {
            scope = "Historical replay rows recalculated as LikelyHitV1 public Top Picks under the current configuration.",
            diagnosticOnly = true,
            total = publicTopPickRows.Length,
            replayable = publicTopPickRows.Count(row => row.ReplayStatus == "Replayable"),
            graded = graded.Length,
            wins = graded.Count(row => row.Outcome > 0),
            losses = graded.Count(row => row.Outcome < 0),
            pushes = graded.Count(row => row.Outcome == 0),
            hitRate = CalculateLikelyHitReplayHitRate(graded),
            roi = CalculateLikelyHitReplayRoi(graded),
            averageHitChance = AverageLikelyHitDecimal(publicTopPickRows.Select(row => row.HistoricalHitChance)),
            averageImpliedProbability = AverageLikelyHitDecimal(publicTopPickRows.Select(row => row.ImpliedProbability)),
            averageBreakEvenMargin = AverageLikelyHitDecimal(publicTopPickRows.Select(row => row.HistoricalBreakEvenMargin)),
            averageEffectiveSampleSize = AverageLikelyHitDecimal(publicTopPickRows.Select(row => (decimal?)row.HistoricalEffectiveSampleSize)),
            segments = new
            {
                hitChanceBucket = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, BuildLikelyHitHistoricalPublicHitChanceBucket),
                propShape = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, BuildLikelyHitPublicPropShapeKey, BuildLikelyHitPublicPropShapeLabel),
                oddsBucket = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(
                    publicTopPickRows.Where(row => string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)),
                    row => BuildLikelyHitBorderlineOddsBucket(row.AmericanPrice)),
                breakEvenMarginBucket = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, row => BuildLikelyHitBreakEvenMarginBucket(row.HistoricalBreakEvenMargin)),
                effectiveSampleSizeBucket = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, row => BuildLikelyHitPublicEffectiveSampleBucket(row.HistoricalEffectiveSampleSize)),
                side = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, row => string.IsNullOrWhiteSpace(row.Side) ? "Unknown" : row.Side),
                bookmaker = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, row => string.IsNullOrWhiteSpace(row.Bookmaker) ? "Unknown" : row.Bookmaker!),
                providerCategory = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, row => row.ProviderCategory),
                sportCompetition = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, BuildLikelyHitSportCompetitionLabel),
                balancedUtilityBucket = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, row => BuildLikelyHitPublicUtilityBucket(row.HistoricalBalancedLineUtility)),
                payoutQualityBucket = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, row => BuildLikelyHitPublicScoreBucket(row.HistoricalPayoutQualityScore)),
                sweetSpotScoreBucket = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(publicTopPickRows, row => BuildLikelyHitPublicScoreBucket(row.HistoricalSweetSpotScore))
            }
        };
    }

    private static object BuildLikelyHitPublicTopPickConcentrationAnalysis(IReadOnlyList<LikelyHitReplayRow> rows)
    {
        var publicTopPickRows = rows.Where(row => row.IsTopPick).ToArray();
        var historicalTopPickRowsByShape = rows
            .Where(row => row.IsHistoricalPublicTopPick)
            .GroupBy(BuildLikelyHitPublicPropShapeKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var totalTopPicks = publicTopPickRows.Length;
        var totalForShare = Math.Max(1, totalTopPicks);
        var topPropShapes = publicTopPickRows
            .GroupBy(BuildLikelyHitPublicPropShapeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var shapeRows = group.ToArray();
                var historicalRows = historicalTopPickRowsByShape.TryGetValue(group.Key, out var matchingHistoricalRows)
                    ? matchingHistoricalRows
                    : Array.Empty<LikelyHitReplayRow>();
                var graded = historicalRows.Where(row => row.IsGraded).ToArray();
                var share = Math.Round((decimal)shapeRows.Length / totalForShare, 6);
                var recommendation = ResolveLikelyHitPublicTopPickProfileRecommendation(
                    graded.Length,
                    CalculateLikelyHitReplayHitRate(graded),
                    CalculateLikelyHitReplayRoi(graded),
                    AverageLikelyHitDecimal(historicalRows.Select(row => row.HistoricalHitChance)),
                    out var reasonCodes);
                var concentrationReasons = new List<string>(reasonCodes);
                if (share > 0.25m)
                {
                    concentrationReasons.Add("ShapeShareAbove25Percent");
                }

                var first = shapeRows[0];
                return new
                {
                    key = group.Key,
                    label = BuildLikelyHitPublicPropShapeLabel(first),
                    currentCount = shapeRows.Length,
                    currentShare = share,
                    historicalReplayTotal = historicalRows.Length,
                    historicalReplayable = historicalRows.Count(row => row.ReplayStatus == "Replayable"),
                    historicalGraded = graded.Length,
                    historicalWins = graded.Count(row => row.Outcome > 0),
                    historicalLosses = graded.Count(row => row.Outcome < 0),
                    historicalPushes = graded.Count(row => row.Outcome == 0),
                    historicalHitRate = CalculateLikelyHitReplayHitRate(graded),
                    historicalRoi = CalculateLikelyHitReplayRoi(graded),
                    replayTotal = historicalRows.Length,
                    replayable = historicalRows.Count(row => row.ReplayStatus == "Replayable"),
                    graded = graded.Length,
                    wins = graded.Count(row => row.Outcome > 0),
                    losses = graded.Count(row => row.Outcome < 0),
                    pushes = graded.Count(row => row.Outcome == 0),
                    hitRate = CalculateLikelyHitReplayHitRate(graded),
                    roi = CalculateLikelyHitReplayRoi(graded),
                    averageHitChance = AverageLikelyHitDecimal(historicalRows.Select(row => row.HistoricalHitChance)),
                    recommendation,
                    reasonCodes = concentrationReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                };
            })
            .OrderByDescending(row => row.currentCount)
            .ThenBy(row => row.key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var maxShapeShare = topPropShapes.FirstOrDefault()?.currentShare ?? 0m;
        var top3ShapeShare = Math.Round(topPropShapes.Take(3).Sum(row => row.currentShare), 6);
        var overConcentratedShapes = topPropShapes
            .Where(row => row.currentShare > 0.25m)
            .ToArray();

        return new
        {
            diagnosticOnly = true,
            currentTopPickCount = totalTopPicks,
            topPropShapes,
            overConcentratedShapes,
            maxShapeShare,
            top3ShapeShare,
            concentrationReasonCodes = new[]
                {
                    maxShapeShare > 0.25m ? "MaxPropShapeShareAbove25Percent" : null,
                    top3ShapeShare > 0.60m ? "Top3PropShapeShareAbove60Percent" : null
                }
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .ToArray()
        };
    }

    private static LikelyHitPublicTopPickProfileRecommendation[] BuildLikelyHitPublicTopPickProfileRecommendations(IReadOnlyList<LikelyHitReplayRow> rows)
    {
        return rows
            .Where(row => row.IsHistoricalPublicTopPick)
            .GroupBy(BuildLikelyHitHistoricalPublicTopPickProfileKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var materialized = group.ToArray();
                var graded = materialized.Where(row => row.IsGraded).ToArray();
                var hitRate = CalculateLikelyHitReplayHitRate(graded);
                var roi = CalculateLikelyHitReplayRoi(graded);
                var averageHitChance = AverageLikelyHitDecimal(materialized.Select(row => row.HistoricalHitChance));
                var recommendation = ResolveLikelyHitPublicTopPickProfileRecommendation(graded.Length, hitRate, roi, averageHitChance, out var reasonCodes);
                var first = materialized[0];
                return new LikelyHitPublicTopPickProfileRecommendation(
                    group.Key,
                    first.Sport,
                    first.ProviderCategory,
                    BuildLikelyHitPublicPropShapeLabel(first),
                    BuildLikelyHitBorderlineOddsBucket(first.AmericanPrice),
                    BuildLikelyHitBreakEvenMarginBucket(first.HistoricalBreakEvenMargin),
                    BuildLikelyHitHistoricalPublicHitChanceBucket(first),
                    BuildLikelyHitPublicEffectiveSampleBucket(first.HistoricalEffectiveSampleSize),
                    materialized.Length,
                    materialized.Count(row => row.ReplayStatus == "Replayable"),
                    graded.Length,
                    graded.Count(row => row.Outcome > 0),
                    graded.Count(row => row.Outcome < 0),
                    graded.Count(row => row.Outcome == 0),
                    hitRate,
                    roi,
                    averageHitChance,
                    AverageLikelyHitDecimal(materialized.Select(row => row.ImpliedProbability)),
                    AverageLikelyHitDecimal(materialized.Select(row => row.HistoricalBreakEvenMargin)),
                    recommendation,
                    reasonCodes);
            })
            .OrderByDescending(row => row.Graded)
            .ThenByDescending(row => row.Total)
            .ThenBy(row => row.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static object BuildLikelyHitPublicTopPickSimulation(
        IReadOnlyList<LikelyHitReplayRow> rows,
        IReadOnlyList<LikelyHitPublicTopPickProfileRecommendation> profileRecommendations,
        LikelyHitPublicTopPickSimulationOptions options)
    {
        var baselineRows = rows.Where(row => row.IsHistoricalPublicTopPick).ToArray();
        var profileRecommendationsByKey = profileRecommendations
            .ToDictionary(row => row.ProfileKey, row => row, StringComparer.OrdinalIgnoreCase);
        var afterProfileGate = options.Enabled
            ? baselineRows.Where(row => PassesLikelyHitPublicTopPickSimulation(row, profileRecommendationsByKey, options)).ToArray()
            : baselineRows;
        var simulatedRows = options.Enabled
            ? ApplyLikelyHitPublicTopPickShapeCaps(afterProfileGate, options, baselineRows.Length)
            : baselineRows;
        var simulatedSet = simulatedRows.ToHashSet();
        var removed = options.Enabled
            ? baselineRows.Where(row => !simulatedSet.Contains(row)).ToArray()
            : Array.Empty<LikelyHitReplayRow>();
        var baselineGraded = baselineRows.Where(row => row.IsGraded).ToArray();
        var simulatedGraded = simulatedRows.Where(row => row.IsGraded).ToArray();

        return new
        {
            enabled = options.Enabled,
            diagnosticOnly = true,
            scope = "Historical replay rows recalculated as LikelyHitV1 public Top Picks under the current configuration.",
            simulationUnavailableReason = baselineGraded.Length == 0 ? "NoHistoricalGradedRows" : null,
            baselineTopPickRows = baselineRows.Length,
            simulatedTopPickRows = simulatedRows.Length,
            removedRows = removed.Length,
            baselineHitRate = CalculateLikelyHitReplayHitRate(baselineGraded),
            simulatedHitRate = CalculateLikelyHitReplayHitRate(simulatedGraded),
            baselineRoi = CalculateLikelyHitReplayRoi(baselineGraded),
            simulatedRoi = CalculateLikelyHitReplayRoi(simulatedGraded),
            removedBucketBreakdown = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(removed, BuildLikelyHitPublicPropShapeKey, BuildLikelyHitPublicPropShapeLabel),
            keptBucketBreakdown = BuildLikelyHitHistoricalPublicTopPickSegmentSummary(simulatedRows, BuildLikelyHitPublicPropShapeKey, BuildLikelyHitPublicPropShapeLabel),
            criteria = new
            {
                requireProfileAllow = options.RequireProfileAllow,
                blockProfileBlock = options.BlockProfileBlock,
                penalizeProfilePenalize = options.PenalizeProfilePenalize,
                maxPropShapeShare = options.MaxPropShapeShare,
                maxRowsPerPropShape = options.MaxRowsPerPropShape,
                minBreakEvenMargin = options.MinBreakEvenMargin,
                minBalancedUtility = options.MinBalancedUtility
            }
        };
    }

    private static object BuildLikelyHitCurrentPublicTopPickSummary(IReadOnlyList<LikelyHitReplayRow> rows)
    {
        var publicTopPickRows = rows.Where(row => row.IsTopPick).ToArray();
        return new
        {
            currentTopPickCount = publicTopPickRows.Length,
            currentSportsbookTopPickCount = publicTopPickRows.Count(row => string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)),
            currentDfsTopPickCount = publicTopPickRows.Count(row => string.Equals(row.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
            currentSoccerTopPickCount = publicTopPickRows.Count(row => string.Equals(row.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)),
            currentMlbTopPickCount = publicTopPickRows.Count(row => string.Equals(row.Sport, "MLB", StringComparison.OrdinalIgnoreCase)),
            byPropShape = BuildLikelyHitPublicTopPickSegmentSummary(publicTopPickRows, BuildLikelyHitPublicPropShapeKey, BuildLikelyHitPublicPropShapeLabel),
            byOddsBucket = BuildLikelyHitPublicTopPickSegmentSummary(
                publicTopPickRows.Where(row => string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)),
                row => BuildLikelyHitBorderlineOddsBucket(row.AmericanPrice)),
            byHitChanceBucket = BuildLikelyHitPublicTopPickSegmentSummary(publicTopPickRows, BuildLikelyHitPublicHitChanceBucket),
            byBreakEvenMargin = BuildLikelyHitPublicTopPickSegmentSummary(publicTopPickRows, row => BuildLikelyHitBreakEvenMarginBucket(row.BreakEvenMargin)),
            byEffectiveSampleBucket = BuildLikelyHitPublicTopPickSegmentSummary(publicTopPickRows, row => BuildLikelyHitPublicEffectiveSampleBucket(row.EffectiveSampleSize)),
            byBookmaker = BuildLikelyHitPublicTopPickSegmentSummary(publicTopPickRows, row => string.IsNullOrWhiteSpace(row.Bookmaker) ? "Unknown" : row.Bookmaker!),
            bySide = BuildLikelyHitPublicTopPickSegmentSummary(publicTopPickRows, row => string.IsNullOrWhiteSpace(row.Side) ? "Unknown" : row.Side)
        };
    }

    private static object BuildLikelyHitPublicTopPickSamples(IReadOnlyList<LikelyHitReplayRow> rows, int sampleTake)
    {
        var historicalPublicTopPickRows = rows.Where(row => row.IsHistoricalPublicTopPick).ToArray();
        var currentPublicTopPickRows = rows.Where(row => row.IsTopPick).ToArray();
        var largestShapeKey = currentPublicTopPickRows
            .GroupBy(BuildLikelyHitPublicPropShapeKey, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();

        return new
        {
            best = historicalPublicTopPickRows
                .Where(row => row.IsGraded)
                .OrderByDescending(row => row.Profit ?? decimal.MinValue)
                .ThenByDescending(row => row.HistoricalHitChance ?? decimal.MinValue)
                .Take(sampleTake)
                .Select(row => ToLikelyHitPublicTopPickSample(row, useHistoricalReplayMetrics: true))
                .ToArray(),
            worst = historicalPublicTopPickRows
                .Where(row => row.IsGraded)
                .OrderBy(row => row.Profit ?? decimal.MaxValue)
                .ThenBy(row => row.HistoricalHitChance ?? decimal.MaxValue)
                .Take(sampleTake)
                .Select(row => ToLikelyHitPublicTopPickSample(row, useHistoricalReplayMetrics: true))
                .ToArray(),
            largestConcentration = string.IsNullOrWhiteSpace(largestShapeKey)
                ? Array.Empty<object>()
                : currentPublicTopPickRows
                    .Where(row => string.Equals(BuildLikelyHitPublicPropShapeKey(row), largestShapeKey, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(row => row.TopPickScore ?? decimal.MinValue)
                    .ThenByDescending(row => row.HitChance ?? decimal.MinValue)
                    .Take(sampleTake)
                    .Select(row => ToLikelyHitPublicTopPickSample(row, useHistoricalReplayMetrics: false))
                    .ToArray(),
            ungradedCurrent = currentPublicTopPickRows
                .Where(row => !row.IsGraded)
                .OrderByDescending(row => row.TopPickScore ?? decimal.MinValue)
                .ThenByDescending(row => row.HitChance ?? decimal.MinValue)
                .Take(sampleTake)
                .Select(row => ToLikelyHitPublicTopPickSample(row, useHistoricalReplayMetrics: false))
                .ToArray()
        };
    }

    private static LikelyHitBorderlineSegment[] BuildLikelyHitPublicTopPickSegmentSummary(
        IEnumerable<LikelyHitReplayRow> sourceRows,
        Func<LikelyHitReplayRow, string> bucketSelector,
        Func<LikelyHitReplayRow, string>? labelSelector = null)
    {
        return sourceRows
            .GroupBy(bucketSelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rows = group.ToArray();
                var graded = rows.Where(row => row.IsGraded).ToArray();
                var first = rows.FirstOrDefault();
                return new LikelyHitBorderlineSegment(
                    group.Key,
                    first is null ? group.Key : labelSelector?.Invoke(first) ?? group.Key,
                    rows.Length,
                    rows.Count(row => row.ReplayStatus == "Replayable"),
                    graded.Length,
                    graded.Count(row => row.Outcome > 0),
                    graded.Count(row => row.Outcome < 0),
                    graded.Count(row => row.Outcome == 0),
                    CalculateLikelyHitReplayHitRate(graded),
                    AverageLikelyHitDecimal(rows.Select(row => row.HitChance)),
                    AverageLikelyHitDecimal(rows.Select(row => row.ImpliedProbability)),
                    AverageLikelyHitDecimal(rows.Select(row => row.BreakEvenMargin)),
                    AverageLikelyHitDecimal(rows.Select(row => (decimal?)row.EffectiveSampleSize)),
                    CalculateLikelyHitReplayRoi(graded),
                    rows.Count(row => row.IsTopPick),
                    rows.Count(row => !row.IsTopPick));
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LikelyHitBorderlineSegment[] BuildLikelyHitHistoricalPublicTopPickSegmentSummary(
        IEnumerable<LikelyHitReplayRow> sourceRows,
        Func<LikelyHitReplayRow, string> bucketSelector,
        Func<LikelyHitReplayRow, string>? labelSelector = null)
    {
        return sourceRows
            .GroupBy(bucketSelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rows = group.ToArray();
                var graded = rows.Where(row => row.IsGraded).ToArray();
                var first = rows.FirstOrDefault();
                return new LikelyHitBorderlineSegment(
                    group.Key,
                    first is null ? group.Key : labelSelector?.Invoke(first) ?? group.Key,
                    rows.Length,
                    rows.Count(row => row.ReplayStatus == "Replayable"),
                    graded.Length,
                    graded.Count(row => row.Outcome > 0),
                    graded.Count(row => row.Outcome < 0),
                    graded.Count(row => row.Outcome == 0),
                    CalculateLikelyHitReplayHitRate(graded),
                    AverageLikelyHitDecimal(rows.Select(row => row.HistoricalHitChance)),
                    AverageLikelyHitDecimal(rows.Select(row => row.ImpliedProbability)),
                    AverageLikelyHitDecimal(rows.Select(row => row.HistoricalBreakEvenMargin)),
                    AverageLikelyHitDecimal(rows.Select(row => (decimal?)row.HistoricalEffectiveSampleSize)),
                    CalculateLikelyHitReplayRoi(graded),
                    rows.Count(row => row.IsHistoricalPublicTopPick),
                    rows.Count(row => !row.IsHistoricalPublicTopPick));
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool PassesLikelyHitPublicTopPickSimulation(
        LikelyHitReplayRow row,
        IReadOnlyDictionary<string, LikelyHitPublicTopPickProfileRecommendation> profileRecommendationsByKey,
        LikelyHitPublicTopPickSimulationOptions options)
    {
        if (options.MinBreakEvenMargin.HasValue && (!row.BreakEvenMargin.HasValue || row.BreakEvenMargin.Value < options.MinBreakEvenMargin.Value))
        {
            return false;
        }

        if (options.MinBalancedUtility.HasValue && (!row.BalancedLineUtility.HasValue || row.BalancedLineUtility.Value < options.MinBalancedUtility.Value))
        {
            return false;
        }

        var profileKey = BuildLikelyHitHistoricalPublicTopPickProfileKey(row);
        var hasProfile = profileRecommendationsByKey.TryGetValue(profileKey, out var profile);
        if (options.RequireProfileAllow)
        {
            return hasProfile && string.Equals(profile!.Recommendation, "Allow", StringComparison.OrdinalIgnoreCase);
        }

        if (!hasProfile)
        {
            return true;
        }

        if (options.BlockProfileBlock && string.Equals(profile!.Recommendation, "Block", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !options.PenalizeProfilePenalize || !string.Equals(profile!.Recommendation, "Penalize", StringComparison.OrdinalIgnoreCase);
    }

    private static LikelyHitReplayRow[] ApplyLikelyHitPublicTopPickShapeCaps(
        IReadOnlyCollection<LikelyHitReplayRow> rows,
        LikelyHitPublicTopPickSimulationOptions options,
        int baselineCount)
    {
        var cap = int.MaxValue;
        if (options.MaxRowsPerPropShape > 0)
        {
            cap = Math.Min(cap, options.MaxRowsPerPropShape);
        }

        if (options.MaxPropShapeShare > 0m)
        {
            var share = Math.Clamp(options.MaxPropShapeShare, 0m, 1m);
            var shareCap = Math.Max(1, (int)Math.Floor(Math.Max(1, baselineCount) * share));
            cap = Math.Min(cap, shareCap);
        }

        if (cap == int.MaxValue)
        {
            return rows.ToArray();
        }

        return rows
            .GroupBy(BuildLikelyHitPublicPropShapeKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group
                .OrderByDescending(row => row.HistoricalTopPickScore ?? decimal.MinValue)
                .ThenByDescending(row => row.HistoricalHitChance ?? decimal.MinValue)
                .ThenBy(row => row.Player, StringComparer.OrdinalIgnoreCase)
                .Take(cap))
            .OrderByDescending(row => row.HistoricalTopPickScore ?? decimal.MinValue)
            .ThenByDescending(row => row.HistoricalHitChance ?? decimal.MinValue)
            .ThenBy(row => row.Player, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildLikelyHitPublicTopPickProfileKey(LikelyHitReplayRow row)
    {
        return string.Join("|",
            row.Sport,
            row.ProviderCategory,
            BuildLikelyHitPublicPropShapeKey(row),
            BuildLikelyHitBorderlineOddsBucket(row.AmericanPrice),
            BuildLikelyHitBreakEvenMarginBucket(row.BreakEvenMargin),
            BuildLikelyHitPublicHitChanceBucket(row),
            BuildLikelyHitPublicEffectiveSampleBucket(row.EffectiveSampleSize));
    }

    private static string BuildLikelyHitHistoricalPublicTopPickProfileKey(LikelyHitReplayRow row)
    {
        return string.Join("|",
            row.Sport,
            row.ProviderCategory,
            BuildLikelyHitPublicPropShapeKey(row),
            BuildLikelyHitBorderlineOddsBucket(row.AmericanPrice),
            BuildLikelyHitBreakEvenMarginBucket(row.HistoricalBreakEvenMargin),
            BuildLikelyHitHistoricalPublicHitChanceBucket(row),
            BuildLikelyHitPublicEffectiveSampleBucket(row.HistoricalEffectiveSampleSize));
    }

    private static string BuildLikelyHitPublicPropShapeKey(LikelyHitReplayRow row)
    {
        var line = row.Line.HasValue
            ? row.Line.Value.ToString("0.####", CultureInfo.InvariantCulture)
            : "Line unavailable";
        return string.Join("|", row.Sport, row.PropType, row.Side, line);
    }

    private static string BuildLikelyHitPublicPropShapeLabel(LikelyHitReplayRow row)
    {
        var line = row.Line.HasValue
            ? row.Line.Value.ToString("0.####", CultureInfo.InvariantCulture)
            : "Line unavailable";
        return string.Join(" ", new[] { BuildLikelyHitSportCompetitionLabel(row), row.PropType, row.Side, line }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildLikelyHitSportCompetitionLabel(LikelyHitReplayRow row)
    {
        if (string.Equals(row.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(row.Competition) || string.Equals(row.Competition, row.Sport, StringComparison.OrdinalIgnoreCase)
                ? "Soccer"
                : $"Soccer {row.Competition}";
        }

        return string.IsNullOrWhiteSpace(row.Sport) ? "Unknown" : row.Sport;
    }

    private static string BuildLikelyHitPublicHitChanceBucket(LikelyHitReplayRow row)
    {
        return BuildLikelyHitPublicHitChanceBucket(row.HitChance);
    }

    private static string BuildLikelyHitHistoricalPublicHitChanceBucket(LikelyHitReplayRow row)
    {
        return BuildLikelyHitPublicHitChanceBucket(row.HistoricalHitChance);
    }

    private static string BuildLikelyHitPublicHitChanceBucket(decimal? hitChance)
    {
        if (!hitChance.HasValue)
        {
            return "Unavailable";
        }

        return hitChance.Value switch
        {
            < 0.65m => "<65",
            < 0.70m => "65-70",
            < 0.75m => "70-75",
            < 0.80m => "75-80",
            < 0.85m => "80-85",
            < 0.90m => "85-90",
            _ => "90+"
        };
    }

    private static string BuildLikelyHitPublicEffectiveSampleBucket(decimal sampleSize)
    {
        return sampleSize switch
        {
            < 10m => "<10",
            < 20m => "10-19",
            < 30m => "20-29",
            < 50m => "30-49",
            < 100m => "50-99",
            _ => "100+"
        };
    }

    private static string BuildLikelyHitPublicUtilityBucket(decimal? score)
    {
        if (!score.HasValue)
        {
            return "Unavailable";
        }

        return score.Value switch
        {
            < 0.50m => "<0.50",
            < 0.60m => "0.50-0.60",
            < 0.70m => "0.60-0.70",
            < 0.80m => "0.70-0.80",
            _ => "0.80+"
        };
    }

    private static string BuildLikelyHitPublicScoreBucket(decimal? score)
    {
        if (!score.HasValue)
        {
            return "Unavailable";
        }

        return score.Value switch
        {
            < 0.25m => "<0.25",
            < 0.50m => "0.25-0.50",
            < 0.75m => "0.50-0.75",
            _ => "0.75+"
        };
    }

    private static object ToLikelyHitPublicTopPickSample(LikelyHitReplayRow row, bool useHistoricalReplayMetrics)
    {
        var hitChance = useHistoricalReplayMetrics ? row.HistoricalHitChance : row.HitChance;
        var effectiveSampleSize = useHistoricalReplayMetrics ? row.HistoricalEffectiveSampleSize : row.EffectiveSampleSize;
        var topPickScore = useHistoricalReplayMetrics ? row.HistoricalTopPickScore : row.TopPickScore;
        var balancedLineUtility = useHistoricalReplayMetrics ? row.HistoricalBalancedLineUtility : row.BalancedLineUtility;
        var payoutQualityScore = useHistoricalReplayMetrics ? row.HistoricalPayoutQualityScore : row.PayoutQualityScore;
        var sweetSpotScore = useHistoricalReplayMetrics ? row.HistoricalSweetSpotScore : row.SweetSpotScore;
        var breakEvenMargin = hitChance.HasValue && row.ImpliedProbability.HasValue
            ? Math.Round(hitChance.Value - row.ImpliedProbability.Value, 6)
            : (decimal?)null;

        return new
        {
            sport = row.Sport,
            competition = row.Competition,
            eventDate = row.EventDate,
            player = row.Player,
            propType = row.PropType,
            side = row.Side,
            line = row.Line,
            book = row.Bookmaker,
            americanPrice = row.AmericanPrice,
            decimalPrice = row.DecimalPrice,
            impliedProbability = row.ImpliedProbability,
            hitChance,
            breakEvenMargin,
            effectiveSampleSize,
            resultValue = row.ActualResult,
            outcome = row.OutcomeLabel,
            propShape = BuildLikelyHitPublicPropShapeLabel(row),
            topPickScore,
            balancedLineUtility,
            payoutQualityScore,
            sweetSpotScore,
            isReplayTopPick = row.IsHistoricalPublicTopPick,
            currentPublicTopPick = row.IsTopPick,
            row.ReplayStatus
        };
    }

    private static LikelyHitBorderlineSegment[] BuildLikelyHitBorderlineSegmentSummary(
        IEnumerable<LikelyHitReplayRow> sourceRows,
        Func<LikelyHitReplayRow, string> bucketSelector)
    {
        return sourceRows
            .GroupBy(bucketSelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rows = group.ToArray();
                var graded = rows.Where(row => row.IsGraded).ToArray();
                return new LikelyHitBorderlineSegment(
                    group.Key,
                    group.Key,
                    rows.Length,
                    rows.Count(row => row.ReplayStatus == "Replayable"),
                    graded.Length,
                    graded.Count(row => row.Outcome > 0),
                    graded.Count(row => row.Outcome < 0),
                    graded.Count(row => row.Outcome == 0),
                    CalculateLikelyHitReplayHitRate(graded),
                    AverageLikelyHitDecimal(rows.Select(row => row.HitChance)),
                    AverageLikelyHitDecimal(rows.Select(row => row.ImpliedProbability)),
                    AverageLikelyHitDecimal(rows.Select(row => row.BreakEvenMargin)),
                    AverageLikelyHitDecimal(rows.Select(row => (decimal?)row.EffectiveSampleSize)),
                    CalculateLikelyHitReplayRoi(graded),
                    rows.Count(row => row.IsTopPick),
                    rows.Count(row => !row.IsTopPick));
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<LikelyHitReplayRow> SelectLikelyHitBorderlineSportsbookRows(IEnumerable<LikelyHitReplayRow> rows)
    {
        return rows.Where(row =>
            string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase) &&
            row.HitChance >= 0.60m &&
            row.HitChance < 0.65m);
    }

    private static string BuildLikelyHitBorderlinePropShapeKey(LikelyHitReplayRow row)
    {
        var line = row.Line.HasValue
            ? row.Line.Value.ToString("0.####", CultureInfo.InvariantCulture)
            : "Line unavailable";
        return string.Join("|", row.Sport, row.PropType, row.Side, line);
    }

    internal static string BuildLikelyHitBorderlineOddsBucket(int? americanPrice)
    {
        if (!americanPrice.HasValue)
        {
            return "Unavailable";
        }

        return americanPrice.Value switch
        {
            >= -300 and <= -251 => "-300 to -251",
            >= -250 and <= -201 => "-250 to -201",
            >= -200 and <= -151 => "-200 to -151",
            >= -150 and <= -126 => "-150 to -126",
            >= -125 and <= -111 => "-125 to -111",
            >= -110 and <= -101 => "-110 to -101",
            >= 100 and <= 125 => "+100 to +125",
            >= 126 and <= 150 => "+126 to +150",
            >= 151 and <= 200 => "+151 to +200",
            >= 201 and <= 250 => "+201 to +250",
            < -300 => "Below -300",
            > 250 => "Above +250",
            _ => "Other"
        };
    }

    internal static string BuildLikelyHitBreakEvenMarginBucket(decimal? margin)
    {
        if (!margin.HasValue)
        {
            return "Unavailable";
        }

        return margin.Value switch
        {
            <= 0m => "<=0",
            <= 0.025m => "0-2.5%",
            <= 0.05m => "2.5-5%",
            <= 0.075m => "5-7.5%",
            <= 0.10m => "7.5-10%",
            _ => "10%+"
        };
    }

    private static string BuildLikelyHitMarketGapBucket(decimal? marketGap)
    {
        if (!marketGap.HasValue)
        {
            return "Unavailable";
        }

        return marketGap.Value switch
        {
            <= 0m => "<=0",
            <= 0.05m => "0-5%",
            <= 0.10m => "5-10%",
            <= 0.15m => "10-15%",
            <= 0.25m => "15-25%",
            _ => "25%+"
        };
    }

    internal static string BuildLikelyHitBorderlineSampleBucket(decimal sampleSize)
    {
        return sampleSize switch
        {
            < 10m => "<10",
            < 20m => "10-19",
            < 30m => "20-29",
            < 50m => "30-49",
            < 100m => "50-99",
            _ => "100+"
        };
    }

    internal static string ResolveLikelyHitBorderlineProfileRecommendation(
        int graded,
        decimal? hitRate,
        decimal? roi,
        decimal? averageHitChance,
        out string[] reasonCodes)
    {
        var reasons = new List<string>();
        if (graded < 30)
        {
            reasons.Add("GradedSampleBelow30");
            reasonCodes = reasons.ToArray();
            return "NeedsMoreData";
        }

        if (hitRate < 0.50m || roi <= -0.10m)
        {
            if (hitRate < 0.50m)
            {
                reasons.Add("HitRateBelow50Percent");
            }

            if (roi <= -0.10m)
            {
                reasons.Add("RoiAtOrBelowMinus10Percent");
            }

            reasonCodes = reasons.ToArray();
            return "Block";
        }

        if ((hitRate.HasValue && averageHitChance.HasValue && hitRate.Value <= averageHitChance.Value - 0.05m) || roi < 0m)
        {
            if (hitRate.HasValue && averageHitChance.HasValue && hitRate.Value <= averageHitChance.Value - 0.05m)
            {
                reasons.Add("UnderperformsExpectedHitChanceByAtLeast5Points");
            }

            if (roi < 0m)
            {
                reasons.Add("NegativeRoi");
            }

            reasonCodes = reasons.ToArray();
            return "Penalize";
        }

        if (hitRate.HasValue && averageHitChance.HasValue && hitRate.Value >= averageHitChance.Value - 0.03m && (roi >= 0m || hitRate.Value >= averageHitChance.Value + 0.05m))
        {
            reasons.Add("CalibrationAndRoiSupportProfile");
            reasonCodes = reasons.ToArray();
            return "Allow";
        }

        reasons.Add("MixedEvidence");
        reasonCodes = reasons.ToArray();
        return "Penalize";
    }

    internal static string ResolveLikelyHitPublicTopPickProfileRecommendation(
        int graded,
        decimal? hitRate,
        decimal? roi,
        decimal? averageHitChance,
        out string[] reasonCodes)
    {
        var reasons = new List<string>();
        if (graded < 30)
        {
            reasons.Add("GradedSampleBelow30");
            reasonCodes = reasons.ToArray();
            return "NeedsMoreData";
        }

        if (hitRate < 0.50m || roi <= -0.10m)
        {
            if (hitRate < 0.50m)
            {
                reasons.Add("HitRateBelow50Percent");
            }

            if (roi <= -0.10m)
            {
                reasons.Add("RoiAtOrBelowMinus10Percent");
            }

            reasonCodes = reasons.ToArray();
            return "Block";
        }

        if ((hitRate.HasValue && averageHitChance.HasValue && hitRate.Value <= averageHitChance.Value - 0.05m) || roi <= -0.05m)
        {
            if (hitRate.HasValue && averageHitChance.HasValue && hitRate.Value <= averageHitChance.Value - 0.05m)
            {
                reasons.Add("UnderperformsExpectedHitChanceByAtLeast5Points");
            }

            if (roi <= -0.05m)
            {
                reasons.Add("MateriallyNegativeRoi");
            }

            reasonCodes = reasons.ToArray();
            return "Penalize";
        }

        if (hitRate.HasValue && averageHitChance.HasValue && hitRate.Value >= averageHitChance.Value - 0.03m && (roi >= 0m || hitRate.Value >= averageHitChance.Value + 0.05m))
        {
            reasons.Add("CalibrationAndRoiSupportProfile");
            reasonCodes = reasons.ToArray();
            return "Allow";
        }

        reasons.Add("MixedEvidence");
        reasonCodes = reasons.ToArray();
        return "Penalize";
    }

    private static bool PassesLikelyHitBorderlineSimulation(
        LikelyHitReplayRow row,
        IReadOnlyDictionary<string, LikelyHitBorderlineProfileRecommendation> profileRecommendationsByKey,
        LikelyHitBorderlineSimulationOptions options)
    {
        if ((row.BreakEvenMargin ?? decimal.MinValue) < options.MinBreakEvenMargin)
        {
            return false;
        }

        if (row.EffectiveSampleSize < options.MinEffectiveSampleSize)
        {
            return false;
        }

        if (!PassesLikelyHitBorderlineOddsSimulation(row.AmericanPrice, options))
        {
            return false;
        }

        if (!options.RequireProfileAllow)
        {
            return true;
        }

        var key = string.Join("|",
            BuildLikelyHitBorderlinePropShapeKey(row),
            BuildLikelyHitBorderlineOddsBucket(row.AmericanPrice),
            BuildLikelyHitBreakEvenMarginBucket(row.BreakEvenMargin),
            BuildLikelyHitBorderlineSampleBucket(row.EffectiveSampleSize));
        return profileRecommendationsByKey.TryGetValue(key, out var recommendation) &&
            string.Equals(recommendation.Recommendation, "Allow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PassesLikelyHitBorderlineOddsSimulation(int? americanPrice, LikelyHitBorderlineSimulationOptions options)
    {
        if (!americanPrice.HasValue)
        {
            return false;
        }

        if (americanPrice.Value < 0)
        {
            return americanPrice.Value >= options.WorstAllowedFavoriteAmericanOdds;
        }

        return americanPrice.Value <= options.BestAllowedUnderdogAmericanOdds;
    }

    private static object ToLikelyHitBorderlineSample(LikelyHitReplayRow row)
    {
        return new
        {
            row.Sport,
            row.Competition,
            eventDate = row.EventDate,
            row.Player,
            propType = row.PropType,
            row.Side,
            row.Line,
            book = row.Bookmaker,
            row.AmericanPrice,
            row.DecimalPrice,
            impliedProbability = row.ImpliedProbability,
            row.HitChance,
            breakEvenMargin = row.BreakEvenMargin,
            row.EffectiveSampleSize,
            resultValue = row.ActualResult,
            outcome = row.OutcomeLabel,
            propShape = BuildLikelyHitBorderlinePropShapeKey(row),
            row.TopPickScore,
            row.BalancedLineUtility,
            isReplayTopPick = row.IsTopPick,
            currentPublicTopPick = row.IsTopPick,
            row.ReplayStatus
        };
    }

    private static object BuildLikelyHitReplayRecommendations(
        IReadOnlyList<LikelyHitReplayRow> rows,
        IReadOnlyList<LikelyHitReplayBucket> propShapeBuckets,
        IReadOnlyList<LikelyHitReplayBucket> hitChanceBuckets,
        IReadOnlyList<LikelyHitReplayBucket> sideBuckets,
        IReadOnlyList<LikelyHitReplayBucket> oddsBuckets)
    {
        var topPickRows = rows.Where(row => row.IsTopPick).ToArray();
        var totalTopPicks = Math.Max(1, topPickRows.Length);
        return new
        {
            below60TopPickLeakDetected = topPickRows.Any(row =>
                string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
                && row.HitChance < 0.60m),
            overConcentratedPropShapes = propShapeBuckets
                .Where(bucket => bucket.TopPickRows > 0 && (decimal)bucket.TopPickRows / totalTopPicks >= 0.10m)
                .Select(bucket => new
                {
                    propShape = bucket.Bucket,
                    count = bucket.TopPickRows,
                    percentOfTopPicks = Math.Round((decimal)bucket.TopPickRows / totalTopPicks, 6),
                    bucket.HitRate,
                    recommendation = bucket.Graded < 30
                        ? "NeedsMoreData"
                        : bucket.HitRate < 0.50m ? "Penalize" : "Allow"
                })
                .ToArray(),
            poorCalibrationBuckets = hitChanceBuckets
                .Where(bucket => bucket.Graded >= 10 && bucket.CalibrationError.HasValue && Math.Abs(bucket.CalibrationError.Value) >= 0.15m)
                .Select(bucket => new
                {
                    bucket = bucket.Bucket,
                    expectedHitChance = bucket.AverageHitChance,
                    actualHitRate = bucket.HitRate,
                    error = bucket.CalibrationError
                })
                .ToArray(),
            sideBias = new
            {
                overCount = topPickRows.Count(row => string.Equals(row.Side, "Over", StringComparison.OrdinalIgnoreCase)),
                underCount = topPickRows.Count(row => string.Equals(row.Side, "Under", StringComparison.OrdinalIgnoreCase)),
                overHitRate = sideBuckets.FirstOrDefault(row => string.Equals(row.Bucket, "Over", StringComparison.OrdinalIgnoreCase))?.HitRate,
                underHitRate = sideBuckets.FirstOrDefault(row => string.Equals(row.Bucket, "Under", StringComparison.OrdinalIgnoreCase))?.HitRate
            },
            priceBucketWarnings = oddsBuckets
                .Where(bucket => bucket.Graded >= 10 && (bucket.HitRate < 0.50m || bucket.SportsbookRoi < 0m))
                .Select(bucket => new
                {
                    oddsBucket = bucket.Bucket,
                    bucket.HitRate,
                    roi = bucket.SportsbookRoi,
                    recommendation = bucket.Graded < 30 ? "NeedsMoreData" : "Review"
                })
                .ToArray()
        };
    }

    private static object ToLikelyHitReplaySample(LikelyHitReplayRow row)
    {
        return new
        {
            row.Sport,
            row.Competition,
            row.ProviderCategory,
            row.Player,
            propType = row.PropType,
            row.Side,
            row.Line,
            row.EventDate,
            row.Bookmaker,
            row.AmericanPrice,
            row.HitChance,
            row.ImpliedProbability,
            breakEvenMargin = row.BreakEvenMargin,
            row.MinimumHitChanceRequired,
            row.HitChanceGatePassed,
            row.EffectiveSampleSize,
            row.ReliabilityScore,
            row.PayoutQualityScore,
            row.SweetSpotScore,
            row.BalancedLineUtility,
            row.TopPickScore,
            row.SelectedRecommendedLine,
            row.IsTopPick,
            row.ReplayStatus,
            row.HistoryRowsBeforeEvent,
            row.ActualResult,
            outcome = row.OutcomeLabel,
            row.Profit,
            fatalConflictCodes = row.FatalConflictCodes
        };
    }

    private static decimal? CalculateLikelyHitReplayHitRate(IReadOnlyCollection<LikelyHitReplayRow> gradedRows)
    {
        var wins = gradedRows.Count(row => row.Outcome > 0);
        var losses = gradedRows.Count(row => row.Outcome < 0);
        return wins + losses == 0 ? null : Math.Round((decimal)wins / (wins + losses), 6);
    }

    private static decimal? CalculateLikelyHitReplayRoi(IReadOnlyCollection<LikelyHitReplayRow> gradedRows)
    {
        var profits = gradedRows
            .Where(row => row.Profit.HasValue)
            .Select(row => row.Profit!.Value)
            .ToArray();
        return profits.Length == 0 ? null : Math.Round(profits.Sum() / profits.Length, 6);
    }

    private static string BuildLikelyHitHitChanceBucket(decimal? hitChance)
    {
        if (!hitChance.HasValue)
        {
            return "Unavailable";
        }

        return hitChance.Value switch
        {
            < 0.60m => "<60",
            < 0.65m => "60-65",
            < 0.70m => "65-70",
            < 0.75m => "70-75",
            < 0.80m => "75-80",
            _ => "80+"
        };
    }

    private static string BuildLikelyHitOddsBucket(int? americanPrice)
    {
        if (!americanPrice.HasValue)
        {
            return "Unavailable";
        }

        return americanPrice.Value switch
        {
            >= -300 and <= -251 => "-300 to -251",
            >= -250 and <= -201 => "-250 to -201",
            >= -200 and <= -151 => "-200 to -151",
            >= -150 and <= -111 => "-150 to -111",
            >= -110 and <= -101 => "-110 to -101",
            >= 100 and <= 150 => "+100 to +150",
            >= 151 and <= 250 => "+151 to +250",
            < -300 => "Below -300",
            > 250 => "Above +250",
            _ => "Other"
        };
    }

    private static string BuildLikelyHitSampleBucket(decimal sampleSize)
    {
        return sampleSize switch
        {
            < 5m => "<5",
            < 10m => "5-9",
            < 20m => "10-19",
            < 50m => "20-49",
            _ => "50+"
        };
    }

    private static string BuildLikelyHitScoreBucket(decimal? score, string label)
    {
        if (!score.HasValue)
        {
            return $"{label}: Unavailable";
        }

        return score.Value switch
        {
            < 0.50m => $"{label}: 0.00-0.50",
            < 0.65m => $"{label}: 0.50-0.65",
            < 0.80m => $"{label}: 0.65-0.80",
            < 0.90m => $"{label}: 0.80-0.90",
            _ => $"{label}: 0.90-1.00"
        };
    }

    private static string BuildLikelyHitLineBucket(string propType, decimal? line)
    {
        if (!line.HasValue)
        {
            return "Line unavailable";
        }

        if (line.Value % 1m == 0.5m)
        {
            return line.Value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        if (propType.Contains("Pitcher Strikeout", StringComparison.OrdinalIgnoreCase))
        {
            return line.Value switch
            {
                <= 3.5m => "<=3.5",
                <= 5.5m => "4.0-5.5",
                <= 7.5m => "6.0-7.5",
                _ => "8+"
            };
        }

        return line.Value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static bool IsLikelyHitReplaySide(string side)
    {
        return string.Equals(side, "Over", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(side, "Under", StringComparison.OrdinalIgnoreCase);
    }

    private static int ClassifyLikelyHitReplayOutcome(decimal actual, decimal line, string side)
    {
        if (actual == line)
        {
            return 0;
        }

        return string.Equals(side, "Over", StringComparison.OrdinalIgnoreCase)
            ? actual > line ? 1 : -1
            : actual < line ? 1 : -1;
    }

    private static Guid? ReadLikelyHitGuid(JsonObject obj, string propertyName)
    {
        var value = ReadLikelyHitString(obj, propertyName);
        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }

        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return null;
        }

        return node.GetValueKind() == JsonValueKind.String && Guid.TryParse(node.GetValue<string>(), out guid)
            ? guid
            : null;
    }

    private sealed record LikelyHitReplayEvaluatedRow(JsonObject Row, LikelyHitModelResult Result);

    private sealed record LikelyHitReplayRow(
        string Sport,
        string Competition,
        string ProviderCategory,
        string Player,
        string PropType,
        string Side,
        decimal? Line,
        DateOnly? EventDate,
        string? Bookmaker,
        int? AmericanPrice,
        decimal? DecimalPrice,
        decimal? ImpliedProbability,
        decimal? MarketGap,
        decimal? HitChance,
        decimal EffectiveSampleSize,
        decimal ReliabilityScore,
        decimal PayoutQualityScore,
        decimal? SweetSpotScore,
        decimal? BalancedLineUtility,
        decimal? TopPickScore,
        string MinimumHitChanceGate,
        decimal MinimumHitChanceRequired,
        bool HitChanceGatePassed,
        IReadOnlyList<string> FatalConflictCodes,
        bool SelectedRecommendedLine,
        bool IsTopPick,
        string ReplayStatus,
        int HistoryRowsBeforeEvent,
        decimal? ActualResult,
        int? Outcome,
        decimal? Profit,
        bool HistoricalSelectedRecommendedLine,
        bool IsHistoricalPublicTopPick,
        decimal? HistoricalHitChance,
        decimal HistoricalEffectiveSampleSize,
        decimal HistoricalReliabilityScore,
        decimal HistoricalPayoutQualityScore,
        decimal? HistoricalSweetSpotScore,
        decimal? HistoricalBalancedLineUtility,
        decimal? HistoricalTopPickScore,
        string HistoricalMinimumHitChanceGate,
        decimal HistoricalMinimumHitChanceRequired,
        bool HistoricalHitChanceGatePassed,
        IReadOnlyList<string> HistoricalFatalConflictCodes)
    {
        public bool IsGraded => Outcome.HasValue;
        public string OutcomeLabel => Outcome switch
        {
            > 0 => "Won",
            < 0 => "Lost",
            0 => "Pushed",
            _ => "Ungraded"
        };

        public decimal? BreakEvenMargin => HitChance.HasValue && ImpliedProbability.HasValue
            ? Math.Round(HitChance.Value - ImpliedProbability.Value, 6)
            : null;

        public decimal? HistoricalBreakEvenMargin => HistoricalHitChance.HasValue && ImpliedProbability.HasValue
            ? Math.Round(HistoricalHitChance.Value - ImpliedProbability.Value, 6)
            : null;
    }

    private sealed record LikelyHitReplayBucket(
        string Bucket,
        int Total,
        int Graded,
        int Wins,
        int Losses,
        int Pushes,
        decimal? HitRate,
        decimal? AverageHitChance,
        decimal? CalibrationError,
        decimal? SportsbookRoi,
        int TopPickRows);

    private sealed record LikelyHitBorderlineSimulationOptions(
        bool Enabled,
        decimal MinBreakEvenMargin,
        decimal MinEffectiveSampleSize,
        int WorstAllowedFavoriteAmericanOdds,
        int BestAllowedUnderdogAmericanOdds,
        bool RequireProfileAllow);

    private sealed record LikelyHitPublicTopPickSimulationOptions(
        bool Enabled,
        bool RequireProfileAllow,
        bool BlockProfileBlock,
        bool PenalizeProfilePenalize,
        decimal MaxPropShapeShare,
        int MaxRowsPerPropShape,
        decimal? MinBreakEvenMargin,
        decimal? MinBalancedUtility);

    private sealed record LikelyHitBorderlineSegment(
        string Key,
        string Label,
        int Total,
        int Replayable,
        int Graded,
        int Wins,
        int Losses,
        int Pushes,
        decimal? HitRate,
        decimal? AverageHitChance,
        decimal? AverageImpliedProbability,
        decimal? AverageBreakEvenMargin,
        decimal? AverageEffectiveSampleSize,
        decimal? Roi,
        int TopPickRows,
        int NonTopPickRows);

    private sealed record LikelyHitBorderlineProfileRecommendation(
        string ProfileKey,
        string Sport,
        string ProviderCategory,
        string PropShape,
        string OddsBucket,
        string BreakEvenMarginBucket,
        string EffectiveSampleBucket,
        int Total,
        int Graded,
        int Wins,
        int Losses,
        int Pushes,
        decimal? HitRate,
        decimal? Roi,
        decimal? AverageHitChance,
        decimal? AverageImpliedProbability,
        decimal? AverageBreakEvenMargin,
        string Recommendation,
        IReadOnlyList<string> ReasonCodes);

    private sealed record LikelyHitPublicTopPickProfileRecommendation(
        string ProfileKey,
        string Sport,
        string ProviderCategory,
        string PropShape,
        string OddsBucket,
        string BreakEvenMarginBucket,
        string HitChanceBucket,
        string EffectiveSampleBucket,
        int Total,
        int Replayable,
        int Graded,
        int Wins,
        int Losses,
        int Pushes,
        decimal? HitRate,
        decimal? Roi,
        decimal? AverageHitChance,
        decimal? AverageImpliedProbability,
        decimal? AverageBreakEvenMargin,
        string Recommendation,
        IReadOnlyList<string> ReasonCodes);


    private static decimal? AverageLikelyHitDecimal(IEnumerable<decimal?> values)
    {
        var materialized = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : Math.Round(materialized.Average(), 6);
    }

    internal static string? ReadLikelyHitString(JsonObject obj, string propertyName)
    {
        return obj.TryGetPropertyValue(propertyName, out var node) && node is not null && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;
    }

    internal static decimal? ReadLikelyHitDecimal(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return null;
        }

        if (node.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<decimal>(out var value))
        {
            return value;
        }

        return null;
    }

    internal static int? ReadLikelyHitInt(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return null;
        }

        if (node.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var value))
        {
            return value;
        }

        return null;
    }

    internal static bool? ReadLikelyHitBool(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return null;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static DateOnly? ReadLikelyHitDateOnly(JsonObject obj, string propertyName)
    {
        var value = ReadLikelyHitString(obj, propertyName);
        return DateOnly.TryParse(value, out var date) ? date : null;
    }
}
