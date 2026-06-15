using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Endpoints;

public static class MarketClassificationDebugEndpoints
{
    private const int FreshnessWindowHours = 24;
    private static readonly string[] PayoutFieldTerms =
    [
        "payout",
        "multiplier",
        "odds",
        "price",
        "boost",
        "demon",
        "goblin",
        "balanced",
        "return",
        "tier"
    ];

    public static IEndpointRouteBuilder MapMarketClassificationDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/markets/classification-analysis", async (
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

            var rankedPredictions = probabilityAnalysis.Results
                .OrderByDescending(result => result.RankingScore)
                .ThenByDescending(result => Math.Abs(result.Edge ?? 0m))
                .Take(500)
                .ToArray();
            var propIds = rankedPredictions.Select(result => result.PropId).Distinct().ToArray();
            var latestPlatformPropRows = await dbContext.PlatformProps
                .AsNoTracking()
                .Include(platformProp => platformProp.Platform)
                .Where(platformProp => propIds.Contains(platformProp.PropId))
                .ToListAsync(cancellationToken);

            var latestPlatformProps = latestPlatformPropRows
                .Select(platformProp => new PlatformPropRow(
                    platformProp.PropId,
                    platformProp.Platform?.Name ?? "Unknown",
                    platformProp.Platform?.Slug ?? "unknown",
                    platformProp.PlatformPropId,
                    platformProp.Label,
                    platformProp.CurrentLine,
                    platformProp.PayoutType,
                    platformProp.Status,
                    platformProp.OverAvailable,
                    platformProp.UnderAvailable,
                    platformProp.ObservedAt,
                    platformProp.RawPayloadId,
                    platformProp.Metadata is null ? null : platformProp.Metadata.RootElement.Clone()))
                .ToList();
            var platformPropByPropId = latestPlatformProps
                .GroupBy(platformProp => platformProp.PropId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(platformProp => platformProp.ObservedAt)
                        .ThenBy(platformProp => platformProp.Platform)
                        .First());
            var propTypeIds = rankedPredictions.Select(result => result.PropTypeId).Distinct().ToArray();
            var historicalRows = await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.League)
                .Where(result => result.League != null
                    && result.League.Code == "MLB"
                    && propTypeIds.Contains(result.PropTypeId))
                .Select(result => new
                {
                    result.PropTypeId,
                    result.ActualResultValue
                })
                .ToListAsync(cancellationToken);
            var distributionByPropType = historicalRows
                .GroupBy(result => result.PropTypeId)
                .ToDictionary(
                    group => group.Key,
                    group => BuildDistribution(group.Select(result => result.ActualResultValue)));
            var rows = rankedPredictions
                .Select((prediction, index) =>
                {
                    platformPropByPropId.TryGetValue(prediction.PropId, out var platformProp);
                    distributionByPropType.TryGetValue(prediction.PropTypeId, out var distribution);
                    var line = prediction.SportsbookLine ?? platformProp?.CurrentLine;
                    var lineFlags = AnalyzeLineQuality(prediction.PropType, line, prediction.PredictedValue, distribution);
                    var classification = ClassifyMarket(platformProp, lineFlags);

                    return new MarketClassificationRow(
                        index + 1,
                        platformProp?.Platform ?? "Unknown",
                        prediction.PlayerName,
                        prediction.PropType,
                        line,
                        platformProp?.Label ?? string.Empty,
                        platformProp?.PlatformPropId ?? string.Empty,
                        platformProp?.PayoutType ?? "unknown",
                        platformProp?.Status ?? "unknown",
                        platformProp?.OverAvailable,
                        platformProp?.UnderAvailable,
                        platformProp?.ObservedAt,
                        platformProp?.Metadata,
                        classification,
                        lineFlags,
                        distribution,
                        prediction.PredictedValue,
                        prediction.ProbabilityOver,
                        prediction.ProbabilityUnder,
                        prediction.ProbabilityPush,
                        prediction.ConfidenceScore,
                        prediction.RankingScore,
                        prediction.SampleSize,
                        prediction.MethodUsed);
                })
                .ToArray();

            return Results.Ok(new
            {
                runId = probabilityAnalysis.RunId,
                probabilityAnalysis.ModelName,
                probabilityAnalysis.ModelVersion,
                probabilityAnalysis.CreatedAt,
                analyzedPredictions = rows.Length,
                top100Summary = BuildSummary(rows.Take(100)),
                top500Summary = BuildSummary(rows),
                recommendations = new
                {
                    canCurrentRankingResultsBeTrusted = "Partially. Ranking has useful probability signal, but rows flagged as Alternate, Promotional, Demon, Goblin, Special, Unknown, or line-abnormal should be labeled clearly and should not be treated as normal betting recommendations yet.",
                    eligibleForFutureRecommendations = new[] { "Standard" },
                    visibleWithWarnings = new[] { "Alternate", "Promotional", "Demon", "Goblin", "Special", "Unknown" },
                    hiddenOnlyWhen = new[] { "StaleMarket", "ClosedOrUnavailableMarket", "MissingSportsbookLine", "CorruptedOrInconsistentPropData" },
                    additionalIngestionFields = new[]
                    {
                        "Explicit market type or promotion type from source payload.",
                        "Payout multiplier / odds tier for each side.",
                        "Whether the line is standard, alternate, discounted, boosted, demon, goblin, or flex-specific.",
                        "Source-side status and availability per option.",
                        "Original stat display name and source category without normalization.",
                        "Game/player context attached to the market, including start time and option IDs."
                    }
                },
                results = rows.Select(row => new
                {
                    row.Rank,
                    sportsbook = row.Sportsbook,
                    player = row.Player,
                    propType = row.PropType,
                    sportsbookLine = row.SportsbookLine,
                    rawSportsbookLabel = row.RawSportsbookLabel,
                    platformPropId = row.PlatformPropId,
                    payoutType = row.PayoutType,
                    status = row.Status,
                    overAvailable = row.OverAvailable,
                    underAvailable = row.UnderAvailable,
                    observedAt = row.ObservedAt,
                    metadata = row.Metadata.HasValue ? JsonSerializer.Deserialize<object>(row.Metadata.Value.GetRawText()) : null,
                    marketClassification = row.MarketClassification,
                    lineQualityFlags = row.LineQualityFlags,
                    historicalDistribution = row.HistoricalDistribution,
                    predictedValue = row.PredictedValue,
                    probabilityOver = row.ProbabilityOver,
                    probabilityUnder = row.ProbabilityUnder,
                    probabilityPush = row.ProbabilityPush,
                    confidenceScore = row.ConfidenceScore,
                    rankingScore = row.RankingScore,
                    sampleSize = row.SampleSize,
                    methodUsed = row.MethodUsed
                })
            });
        })
        .WithName("MarketClassificationAnalysisDebug")
        .WithSummary("Classifies persisted sportsbook markets and line plausibility for top-ranked MLB predictions.");

        endpoints.MapGet("/api/debug/predictions/market-rankings", async (
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

            var rows = await BuildMarketRankingRowsAsync(probabilityAnalysis, dbContext, cancellationToken);
            var visibleRows = rows.Where(row => row.IsVisibleInResearchDashboard).ToArray();
            var eligibleRows = rows.Where(row => row.IsEligibleAsNormalRecommendation).ToArray();
            var hiddenRows = rows.Where(row => !row.IsVisibleInResearchDashboard).ToArray();
            var ineligibleButVisibleRows = rows
                .Where(row => row.IsVisibleInResearchDashboard && !row.IsEligibleAsNormalRecommendation)
                .ToArray();

            return Results.Ok(new
            {
                runId = probabilityAnalysis.RunId,
                probabilityAnalysis.ModelName,
                probabilityAnalysis.ModelVersion,
                probabilityAnalysis.CreatedAt,
                totalRankedMarkets = rows.Length,
                visibleMarkets = visibleRows.Length,
                normalRecommendationEligibleMarkets = eligibleRows.Length,
                ineligibleButVisibleMarkets = ineligibleButVisibleRows.Length,
                hiddenMarkets = hiddenRows.Length,
                countsByMarketClassification = rows
                    .GroupBy(row => row.MarketClassification)
                    .Select(group => new
                    {
                        marketClassification = group.Key,
                        count = group.Count()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.marketClassification)
                    .ToArray(),
                countsByIneligibilityReason = rows
                    .SelectMany(row => row.IneligibilityReasons)
                    .GroupBy(reason => reason)
                    .Select(group => new
                    {
                        reason = group.Key,
                        count = group.Count()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.reason)
                    .ToArray(),
                hiddenReasonCounts = hiddenRows
                    .SelectMany(row => row.HiddenReasons)
                    .GroupBy(reason => reason)
                    .Select(group => new
                    {
                        reason = group.Key,
                        count = group.Count()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.reason)
                    .ToArray(),
                hiddenMarketAssessment = hiddenRows
                    .GroupBy(row => row.HiddenAssessment)
                    .Select(group => new
                    {
                        assessment = group.Key,
                        count = group.Count()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.assessment)
                    .ToArray(),
                hiddenMarketSamples = hiddenRows.Take(50).Select(ToHiddenMarketSampleResponse).ToArray(),
                topVisibleMarkets = visibleRows.Take(100).Select(ToMarketRankingResponse).ToArray(),
                topNormalRecommendationEligibleMarkets = eligibleRows.Take(100).Select(ToMarketRankingResponse).ToArray(),
                topIneligibleButVisibleMarkets = ineligibleButVisibleRows.Take(100).Select(ToMarketRankingResponse).ToArray(),
                results = rows.Select(ToMarketRankingResponse).ToArray()
            });
        })
        .WithName("MarketRankingsDebug")
        .WithSummary("Separates market visibility from normal recommendation eligibility for ranked predictions.");

        endpoints.MapGet("/api/debug/markets/freshness-summary", async (
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

            var rows = await BuildMarketRankingRowsAsync(probabilityAnalysis, dbContext, cancellationToken);
            var visibleRows = rows.Where(row => row.IsVisibleInResearchDashboard).ToArray();
            var hiddenRows = rows.Where(row => !row.IsVisibleInResearchDashboard).ToArray();
            var eligibleRows = rows.Where(row => row.IsEligibleAsNormalRecommendation).ToArray();
            var ineligibleButVisibleRows = visibleRows
                .Where(row => !row.IsEligibleAsNormalRecommendation)
                .ToArray();
            var staleRows = rows.Where(row => row.HiddenReasons.Contains("StaleMarket")).ToArray();
            var standardVisibleRows = visibleRows
                .Where(row => string.Equals(row.MarketClassification, "Standard", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return Results.Ok(new
            {
                runId = probabilityAnalysis.RunId,
                probabilityAnalysis.ModelName,
                probabilityAnalysis.ModelVersion,
                probabilityAnalysis.CreatedAt,
                summary = new
                {
                    totalRankedMarkets = rows.Length,
                    visibleMarkets = visibleRows.Length,
                    hiddenMarkets = hiddenRows.Length,
                    normalRecommendationEligibleMarkets = eligibleRows.Length,
                    ineligibleButVisibleMarkets = ineligibleButVisibleRows.Length,
                    staleMarkets = staleRows.Length,
                    freshnessWindowHours = FreshnessWindowHours
                },
                platformFreshnessSummary = rows
                    .GroupBy(row => row.Sportsbook)
                    .OrderBy(group => group.Key)
                    .Select(BuildPlatformFreshnessSummary)
                    .ToArray(),
                visibleMarketComposition = new
                {
                    byPlatform = CountBy(visibleRows, row => row.Sportsbook, "platform"),
                    byMarketClassification = CountBy(visibleRows, row => row.MarketClassification, "marketClassification"),
                    byPayoutType = CountBy(visibleRows, row => row.PayoutType, "payoutType"),
                    byNormalizedPropType = CountBy(visibleRows, row => row.NormalizedPropType, "normalizedPropType"),
                    byRecommendation = CountBy(visibleRows, row => row.Recommendation, "recommendation"),
                    byLineQualityFlag = visibleRows
                        .SelectMany(row => row.LineQualityFlags)
                        .GroupBy(flag => flag)
                        .Select(group => new
                        {
                            lineQualityFlag = group.Key,
                            count = group.Count()
                        })
                        .OrderByDescending(row => row.count)
                        .ThenBy(row => row.lineQualityFlag)
                        .ToArray()
                },
                normalRecommendationBlockers = ineligibleButVisibleRows
                    .SelectMany(row => row.IneligibilityReasons)
                    .GroupBy(reason => reason)
                    .Select(group => new
                    {
                        reason = group.Key,
                        count = group.Count()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.reason)
                    .ToArray(),
                topVisibleMarkets = visibleRows
                    .Take(100)
                    .Select(ToVisibleMarketFreshnessResponse)
                    .ToArray(),
                findings = new
                {
                    areAllVisibleMarketsFromOnePlatform = visibleRows.Select(row => row.Sportsbook).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1,
                    visiblePlatforms = visibleRows.Select(row => row.Sportsbook).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    areAllVisibleMarketsNonStandard = visibleRows.Length > 0 && standardVisibleRows.Length == 0,
                    freshStandardMarketCount = standardVisibleRows.Length,
                    freshStandardBlockedCount = standardVisibleRows.Count(row => !row.IsEligibleAsNormalRecommendation),
                    freshStandardBlockers = standardVisibleRows
                        .Where(row => !row.IsEligibleAsNormalRecommendation)
                        .SelectMany(row => row.IneligibilityReasons)
                        .GroupBy(reason => reason)
                        .Select(group => new
                        {
                            reason = group.Key,
                            count = group.Count()
                        })
                        .OrderByDescending(row => row.count)
                        .ThenBy(row => row.reason)
                        .ToArray(),
                    primaryReasonForNoNormalRecommendations = DeterminePrimaryNoRecommendationReason(visibleRows, ineligibleButVisibleRows)
                }
            });
        })
        .WithName("MarketFreshnessSummaryDebug")
        .WithSummary("Summarizes fresh market supply, stale market counts, and visible market composition.");

        endpoints.MapGet("/api/debug/markets/underdog-payout-analysis", async (
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

            var rows = await BuildMarketRankingRowsAsync(probabilityAnalysis, dbContext, cancellationToken);
            var underdogRows = rows
                .Where(row => row.Sportsbook.Contains("Underdog", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var visibleUnderdogRows = underdogRows
                .Where(row => row.IsVisibleInResearchDashboard)
                .ToArray();
            var metadataRows = underdogRows
                .Where(row => row.Metadata.HasValue)
                .ToArray();
            var payoutFieldRows = underdogRows
                .Where(row => DetectPayoutFields(row.Metadata).Length > 0)
                .ToArray();
            var completeEvPricingRows = underdogRows
                .Where(row => HasCompleteEvPricingFields(DetectPayoutFields(row.Metadata)))
                .ToArray();
            var metadataKeyFrequencies = BuildMetadataKeyFrequencies(underdogRows);
            var rawPayloadIds = underdogRows
                .Select(row => row.RawPayloadId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
            var rawPayloadKeySummary = await BuildRawPayloadKeySummaryAsync(dbContext, rawPayloadIds, cancellationToken);

            return Results.Ok(new
            {
                runId = probabilityAnalysis.RunId,
                probabilityAnalysis.ModelName,
                probabilityAnalysis.ModelVersion,
                probabilityAnalysis.CreatedAt,
                summary = new
                {
                    totalUnderdogRankedMarkets = underdogRows.Length,
                    visibleUnderdogMarkets = visibleUnderdogRows.Length,
                    payoutTypesFound = underdogRows.Select(row => row.PayoutType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    marketClassificationsFound = underdogRows.Select(row => row.MarketClassification).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    uniqueMetadataKeysFound = metadataKeyFrequencies.Length,
                    percentWithMetadata = Percent(underdogRows.Length, metadataRows.Length),
                    percentWithPayoutType = Percent(underdogRows.Length, underdogRows.Count(row => !string.IsNullOrWhiteSpace(row.PayoutType) && !string.Equals(row.PayoutType, "unknown", StringComparison.OrdinalIgnoreCase))),
                    percentWithAnyMultiplierLikeField = Percent(underdogRows.Length, payoutFieldRows.Length),
                    marketsWithCompleteEvPricingFields = completeEvPricingRows.Length,
                    percentWithCompleteEvPricingFields = Percent(underdogRows.Length, completeEvPricingRows.Length),
                    linkedRawPayloadCount = rawPayloadIds.Length,
                    rawPayloadTopLevelKeys = rawPayloadKeySummary
                },
                payoutMetadataInspection = new
                {
                    inspectedFields = new[]
                    {
                        "platform_props.payout_type",
                        "platform_props.metadata",
                        "platform_props.label",
                        "platform_props.raw_payload_id",
                        "platform_props.metadata.options.higher",
                        "platform_props.metadata.options.lower"
                    },
                    detectedFieldTerms = PayoutFieldTerms,
                    conclusion = DetermineUnderdogPayoutConclusion(underdogRows)
                },
                metadataKeyFrequency = metadataKeyFrequencies,
                payoutTypeBreakdown = underdogRows
                    .GroupBy(row => row.PayoutType, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        payoutType = group.Key,
                        count = group.Count(),
                        marketClassifications = group.Select(row => row.MarketClassification).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                        sampleLabels = group.Select(row => row.RawSportsbookLabel).Where(label => !string.IsNullOrWhiteSpace(label)).Distinct().Take(5).ToArray(),
                        sampleMetadata = group.Select(row => DeserializeMetadata(row.Metadata)).Where(value => value is not null).Take(3).ToArray(),
                        averageLine = Math.Round(group.Where(row => row.SportsbookLine.HasValue).Select(row => row.SportsbookLine!.Value).DefaultIfEmpty().Average(), 4),
                        propTypesRepresented = group.Select(row => row.NormalizedPropType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.payoutType)
                    .ToArray(),
                classificationBreakdown = underdogRows
                    .GroupBy(row => row.MarketClassification, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        marketClassification = group.Key,
                        count = group.Count(),
                        payoutTypes = group.Select(row => row.PayoutType).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                        sampleLabels = group.Select(row => row.RawSportsbookLabel).Where(label => !string.IsNullOrWhiteSpace(label)).Distinct().Take(5).ToArray(),
                        sampleMetadata = group.Select(row => DeserializeMetadata(row.Metadata)).Where(value => value is not null).Take(3).ToArray(),
                        lineQualityFlags = group.SelectMany(row => row.LineQualityFlags).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.marketClassification)
                    .ToArray(),
                topVisibleUnderdogMarkets = visibleUnderdogRows
                    .Take(100)
                    .Select(row =>
                    {
                        var payoutFields = DetectPayoutFields(row.Metadata);
                        var hasEvFields = HasCompleteEvPricingFields(payoutFields);

                        return new
                        {
                            player = row.Player,
                            platformPropId = row.PlatformPropId,
                            rawLabel = row.RawSportsbookLabel,
                            normalizedPropType = row.NormalizedPropType,
                            line = row.SportsbookLine,
                            payoutType = row.PayoutType,
                            marketClassification = row.MarketClassification,
                            metadata = DeserializeMetadata(row.Metadata),
                            probabilityOver = row.ProbabilityOver,
                            probabilityUnder = row.ProbabilityUnder,
                            probabilityPush = row.ProbabilityPush,
                            rankingScore = row.RankingScore,
                            recommendation = row.Recommendation,
                            displayWarnings = row.DisplayWarnings,
                            detectedPayoutOrMultiplierFields = payoutFields,
                            canEstimateEVFromCurrentData = hasEvFields,
                            missingFieldsForEV = hasEvFields
                                ? Array.Empty<string>()
                                : new[] { "higher payout multiplier or decimal price", "lower payout multiplier or decimal price", "confirmed payout semantics for payoutType" }
                        };
                    })
                    .ToArray(),
                findings = new
                {
                    doesCurrentPersistedUnderdogDataContainEnoughPayoutInformationToEstimateEv = completeEvPricingRows.Length == underdogRows.Length && underdogRows.Length > 0,
                    doesCurrentPersistedUnderdogDataContainEnoughPayoutInformationForSomeMarkets = completeEvPricingRows.Length > 0,
                    balancedMeaning = "ImpossibleToDetermineFromCurrentPersistedData",
                    areDemonGoblinPromoIndicatorsExplicit = "Partially. Demon and Goblin are inferred from persisted labels/metadata text when those terms exist; Promotional is currently inferred from payoutType/metadata/label signals such as balanced or boost-like terms rather than a confirmed payout multiplier.",
                    additionalFieldsRequired = new[]
                    {
                        "Confirmed Underdog payout multiplier or decimal price for Higher.",
                        "Confirmed Underdog payout multiplier or decimal price for Lower.",
                        "Explicit market/pick'em type from source: standard, balanced, demon, goblin, special, boosted, alternate.",
                        "Option-level payout tier and risk label.",
                        "Any source odds, return, multiplier, or payout table fields before normalization.",
                        "Whether balanced means standard payout, protected payout, discounted payout, or simply balanced higher/lower pricing."
                    }
                }
            });
        })
        .WithName("UnderdogPayoutAnalysisDebug")
        .WithSummary("Audits persisted Underdog market metadata and payout-like fields for EV feasibility.");

        endpoints.MapGet("/api/debug/markets/underdog-ev-analysis", async (
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

            var rows = await BuildMarketRankingRowsAsync(probabilityAnalysis, dbContext, cancellationToken);
            var underdogRows = rows
                .Where(row => row.Sportsbook.Contains("Underdog", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var visibleUnderdogRows = underdogRows
                .Where(row => row.IsVisibleInResearchDashboard)
                .ToArray();
            var pricedSides = underdogRows
                .SelectMany(BuildUnderdogEvSideRows)
                .ToArray();
            var higherSides = pricedSides.Where(side => side.Side == "Higher").ToArray();
            var lowerSides = pricedSides.Where(side => side.Side == "Lower").ToArray();
            var sidesWithAmericanAndDecimal = pricedSides
                .Where(side => side.DecimalPrice.HasValue && side.DecimalFromAmerican.HasValue)
                .ToArray();
            var americanDecimalMatches = sidesWithAmericanAndDecimal
                .Where(side => IsClose(side.DecimalPrice!.Value, side.DecimalFromAmerican!.Value))
                .ToArray();
            var americanDecimalMismatches = sidesWithAmericanAndDecimal
                .Where(side => !IsClose(side.DecimalPrice!.Value, side.DecimalFromAmerican!.Value))
                .ToArray();
            var sidesWithMultiplierAndDecimal = pricedSides
                .Where(side => side.DecimalPrice.HasValue && side.PayoutMultiplier.HasValue)
                .ToArray();
            var multiplierEqualsDecimal = sidesWithMultiplierAndDecimal
                .Where(side => IsClose(side.PayoutMultiplier!.Value, side.DecimalPrice!.Value))
                .ToArray();
            var multiplierEqualsProfit = sidesWithMultiplierAndDecimal
                .Where(side => IsClose(side.PayoutMultiplier!.Value, side.DecimalPrice!.Value - 1m))
                .ToArray();
            var multiplierUnrelated = sidesWithMultiplierAndDecimal
                .Where(side => !IsClose(side.PayoutMultiplier!.Value, side.DecimalPrice!.Value)
                    && !IsClose(side.PayoutMultiplier!.Value, side.DecimalPrice!.Value - 1m))
                .ToArray();
            var marketsWithPriceMismatch = pricedSides
                .Where(side => side.PriceConsistencyFlags.Contains("AmericanDecimalMismatch")
                    || side.PriceConsistencyFlags.Contains("PayoutMultiplierUnrelated"))
                .Select(side => side.PlatformPropId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var marketsWithBothSidesPriceable = underdogRows.Count(row =>
            {
                var higher = ExtractUnderdogOptionPricing(row.Metadata, "higher");
                var lower = ExtractUnderdogOptionPricing(row.Metadata, "lower");

                return higher.DecimalPrice.HasValue && lower.DecimalPrice.HasValue;
            });
            var evCandidates = pricedSides
                .Where(side => side.DecimalPrice.HasValue
                    && side.WinProbability.HasValue
                    && side.ImpliedProbability.HasValue
                    && side.EstimatedEv.HasValue)
                .OrderByDescending(side => side.EstimatedEv)
                .ThenByDescending(side => side.ProbabilityEdge)
                .Take(100)
                .Select(side => new
                {
                    player = side.Player,
                    platformPropId = side.PlatformPropId,
                    rawLabel = side.RawSportsbookLabel,
                    propType = side.NormalizedPropType,
                    line = side.Line,
                    side = side.Side,
                    decimalPrice = side.DecimalPrice,
                    americanPrice = side.AmericanPrice,
                    payoutMultiplier = side.PayoutMultiplier,
                    winProbability = side.WinProbability,
                    impliedProbability = side.ImpliedProbability,
                    probabilityEdge = side.ProbabilityEdge,
                    estimatedEv = side.EstimatedEv,
                    rankingScore = side.RankingScore,
                    priceConsistencyFlags = side.PriceConsistencyFlags
                })
                .ToArray();

            return Results.Ok(new
            {
                runId = probabilityAnalysis.RunId,
                probabilityAnalysis.ModelName,
                probabilityAnalysis.ModelVersion,
                probabilityAnalysis.CreatedAt,
                summary = new
                {
                    totalUnderdogRankedMarkets = underdogRows.Length,
                    visibleUnderdogMarkets = visibleUnderdogRows.Length,
                    marketsWithHigherDecimalPrice = higherSides.Count(side => side.DecimalPrice.HasValue),
                    marketsWithLowerDecimalPrice = lowerSides.Count(side => side.DecimalPrice.HasValue),
                    marketsWithBothSidesPriceable,
                    marketsWithPriceMismatch,
                    positiveEvHigherCount = higherSides.Count(side => side.EstimatedEv > 0),
                    positiveEvLowerCount = lowerSides.Count(side => side.EstimatedEv > 0)
                },
                priceConsistency = new
                {
                    americanDecimalComparedSides = sidesWithAmericanAndDecimal.Length,
                    americanDecimalMatchCount = americanDecimalMatches.Length,
                    americanDecimalMismatchCount = americanDecimalMismatches.Length,
                    americanDecimalMismatchSamples = americanDecimalMismatches
                        .Take(25)
                        .Select(ToPriceConsistencySample)
                        .ToArray(),
                    payoutMultiplierComparedSides = sidesWithMultiplierAndDecimal.Length,
                    payoutMultiplierEqualsDecimalCount = multiplierEqualsDecimal.Length,
                    payoutMultiplierEqualsDecimalMinusOneCount = multiplierEqualsProfit.Length,
                    payoutMultiplierAppearsUnrelatedCount = multiplierUnrelated.Length,
                    payoutMultiplierMismatchSamples = multiplierUnrelated
                        .Take(25)
                        .Select(ToPriceConsistencySample)
                        .ToArray()
                },
                topEvCandidates = evCandidates,
                findings = new
                {
                    canDecimalPriceAndAmericanPriceBeTrustedAsEquivalent = sidesWithAmericanAndDecimal.Length > 0 && americanDecimalMismatches.Length == 0,
                    canPayoutMultiplierBeTrustedForEv = false,
                    shouldFutureEvUse = "decimalPrice",
                    rationale = "Use decimalPrice first because it directly supports EV math and is equivalent to americanPrice when consistency checks pass. Use americanPrice as a validation/fallback field. Do not use payoutMultiplier for EV until its semantics are verified because it often does not equal decimal odds or decimal odds minus one."
                }
            });
        })
        .WithName("UnderdogEvAnalysisDebug")
        .WithSummary("Validates persisted Underdog option prices and estimates read-only EV from decimal prices.");

        endpoints.MapGet("/api/debug/markets/underdog-ev-backtest", async (
            Guid? runId,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var probabilityAnalysis = runId.HasValue
                ? await probabilityService.AnalyzeRunAsync(runId.Value, cancellationToken)
                : await probabilityService.AnalyzeLatestRunAsync(cancellationToken);

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var rows = await BuildMarketRankingRowsAsync(probabilityAnalysis, dbContext, cancellationToken);
            var underdogRows = rows
                .Where(row => row.Sportsbook.Contains("Underdog", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var actualResults = await LoadMlbActualResultsAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var playerAliasCache = new Dictionary<Guid, Guid[]>();
            var sideRows = new List<UnderdogEvBacktestSideRow>();

            foreach (var row in underdogRows)
            {
                var equivalentPlayerIds = await GetEquivalentPlayerIdsAsync(
                    row.PlayerId,
                    playerIdentityAliasService,
                    playerAliasCache,
                    cancellationToken);
                var equivalentPropTypeIds = equivalentPropTypeIdsById.TryGetValue(row.PropTypeId, out var propTypeIds)
                    ? propTypeIds
                    : [row.PropTypeId];
                var actualResult = FindActualResult(
                    actualResults,
                    row.PlayerId,
                    row.PropTypeId,
                    row.EventDate,
                    equivalentPlayerIds,
                    equivalentPropTypeIds);

                sideRows.AddRange(BuildUnderdogEvSideRows(row)
                    .Select(side =>
                    {
                        var outcome = GradeUnderdogSide(side.Side, row.SportsbookLine, actualResult);
                        var profit = CalculateProfitPerDollarStake(outcome, side.DecimalPrice);

                        return new UnderdogEvBacktestSideRow(
                            side.Player,
                            side.PlatformPropId,
                            side.RawSportsbookLabel,
                            side.NormalizedPropType,
                            side.Line,
                            side.Side,
                            side.DecimalPrice,
                            side.AmericanPrice,
                            side.WinProbability,
                            side.ImpliedProbability,
                            side.ProbabilityEdge,
                            side.EstimatedEv,
                            side.RankingScore,
                            actualResult,
                            outcome,
                            profit,
                            side.PriceConsistencyFlags,
                            row.MarketClassification,
                            row.PayoutType,
                            row.LineQualityFlags,
                            row.ObservedAt);
                    }));
            }

            var gradedSides = sideRows.Where(side => side.Outcome is "Win" or "Loss" or "Push").ToArray();
            var positiveEvSides = sideRows.Where(side => side.EstimatedEv > 0).ToArray();
            var positiveEvGradedSides = positiveEvSides.Where(side => side.Outcome is "Win" or "Loss" or "Push").ToArray();

            return Results.Ok(new
            {
                runId = probabilityAnalysis.RunId,
                probabilityAnalysis.ModelName,
                probabilityAnalysis.ModelVersion,
                probabilityAnalysis.CreatedAt,
                summary = new
                {
                    totalUnderdogMarkets = underdogRows.Length,
                    totalSidesEvaluated = sideRows.Count,
                    gradedSides = gradedSides.Length,
                    ungradedSides = sideRows.Count - gradedSides.Length,
                    higherSides = sideRows.Count(side => side.Side == "Higher"),
                    lowerSides = sideRows.Count(side => side.Side == "Lower"),
                    positiveEvSides = positiveEvSides.Length,
                    positiveEvGradedSides = positiveEvGradedSides.Length,
                    positiveEvWins = positiveEvGradedSides.Count(side => side.Outcome == "Win"),
                    positiveEvLosses = positiveEvGradedSides.Count(side => side.Outcome == "Loss"),
                    positiveEvPushes = positiveEvGradedSides.Count(side => side.Outcome == "Push"),
                    positiveEvHitRate = HitRate(positiveEvGradedSides),
                    simulatedProfitPerOneDollarStake = Profit(positiveEvGradedSides),
                    simulatedRoi = Roi(positiveEvGradedSides)
                },
                evBucketBacktest = BuildEvBucketBacktest(sideRows),
                sideBreakdown = sideRows
                    .GroupBy(side => side.Side)
                    .OrderBy(group => group.Key)
                    .Select(group => BuildSideBreakdown(group.Key, group.ToArray()))
                    .ToArray(),
                topPositiveEvGradedCandidates = positiveEvGradedSides
                    .Where(side => side.EstimatedEv > 0)
                    .OrderByDescending(side => side.EstimatedEv)
                    .ThenByDescending(side => side.ProbabilityEdge)
                    .Take(100)
                    .Select(side => new
                    {
                        player = side.Player,
                        platformPropId = side.PlatformPropId,
                        rawLabel = side.RawSportsbookLabel,
                        propType = side.NormalizedPropType,
                        line = side.Line,
                        side = side.Side,
                        decimalPrice = side.DecimalPrice,
                        americanPrice = side.AmericanPrice,
                        winProbability = side.WinProbability,
                        impliedProbability = side.ImpliedProbability,
                        probabilityEdge = side.ProbabilityEdge,
                        estimatedEv = side.EstimatedEv,
                        actualResult = side.ActualResult,
                        outcome = side.Outcome,
                        profitPerOneDollarStake = side.ProfitPerOneDollarStake
                    })
                    .ToArray(),
                findings = BuildUnderdogEvBacktestFindings(sideRows)
            });
        })
        .WithName("UnderdogEvBacktestDebug")
        .WithSummary("Backtests read-only Underdog decimal-price EV estimates against completed MLB prop_results.");

        endpoints.MapGet("/api/debug/markets/underdog-ev-loss-analysis", async (
            Guid? runId,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var probabilityAnalysis = runId.HasValue
                ? await probabilityService.AnalyzeRunAsync(runId.Value, cancellationToken)
                : await probabilityService.AnalyzeLatestRunAsync(cancellationToken);

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var rows = await BuildMarketRankingRowsAsync(probabilityAnalysis, dbContext, cancellationToken);
            var underdogRows = rows
                .Where(row => row.Sportsbook.Contains("Underdog", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var actualResults = await LoadMlbActualResultsAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var playerAliasCache = new Dictionary<Guid, Guid[]>();
            var sideRows = new List<UnderdogEvBacktestSideRow>();

            foreach (var row in underdogRows)
            {
                var equivalentPlayerIds = await GetEquivalentPlayerIdsAsync(
                    row.PlayerId,
                    playerIdentityAliasService,
                    playerAliasCache,
                    cancellationToken);
                var equivalentPropTypeIds = equivalentPropTypeIdsById.TryGetValue(row.PropTypeId, out var propTypeIds)
                    ? propTypeIds
                    : [row.PropTypeId];
                var actualResult = FindActualResult(
                    actualResults,
                    row.PlayerId,
                    row.PropTypeId,
                    row.EventDate,
                    equivalentPlayerIds,
                    equivalentPropTypeIds);

                sideRows.AddRange(BuildUnderdogEvSideRows(row)
                    .Select(side =>
                    {
                        var outcome = GradeUnderdogSide(side.Side, row.SportsbookLine, actualResult);
                        var profit = CalculateProfitPerDollarStake(outcome, side.DecimalPrice);

                        return new UnderdogEvBacktestSideRow(
                            side.Player,
                            side.PlatformPropId,
                            side.RawSportsbookLabel,
                            side.NormalizedPropType,
                            side.Line,
                            side.Side,
                            side.DecimalPrice,
                            side.AmericanPrice,
                            side.WinProbability,
                            side.ImpliedProbability,
                            side.ProbabilityEdge,
                            side.EstimatedEv,
                            side.RankingScore,
                            actualResult,
                            outcome,
                            profit,
                            side.PriceConsistencyFlags,
                            row.MarketClassification,
                            row.PayoutType,
                            row.LineQualityFlags,
                            row.ObservedAt);
                    }));
            }

            var positiveEvGradedSides = sideRows
                .Where(side => side.EstimatedEv > 0 && side.Outcome is "Win" or "Loss" or "Push")
                .ToArray();
            var groupedSummaries = BuildPositiveEvLossAnalysisGroups(positiveEvGradedSides);

            return Results.Ok(new
            {
                runId = probabilityAnalysis.RunId,
                probabilityAnalysis.ModelName,
                probabilityAnalysis.ModelVersion,
                probabilityAnalysis.CreatedAt,
                summary = new
                {
                    totalUnderdogMarkets = underdogRows.Length,
                    positiveEvGradedSides = positiveEvGradedSides.Length,
                    wins = positiveEvGradedSides.Count(side => side.Outcome == "Win"),
                    losses = positiveEvGradedSides.Count(side => side.Outcome == "Loss"),
                    pushes = positiveEvGradedSides.Count(side => side.Outcome == "Push"),
                    hitRate = HitRate(positiveEvGradedSides),
                    simulatedProfit = Profit(positiveEvGradedSides),
                    simulatedRoi = Roi(positiveEvGradedSides)
                },
                bySide = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => side.Side, "side"),
                byPropType = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => side.NormalizedPropType, "propType"),
                byMarketClassification = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => side.MarketClassification, "marketClassification"),
                byPayoutType = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => side.PayoutType, "payoutType"),
                byLineQualityFlag = positiveEvGradedSides
                    .SelectMany(side => side.LineQualityFlags.Length == 0
                        ? [new LineFlagSide("NoLineQualityFlags", side)]
                        : side.LineQualityFlags.Select(flag => new LineFlagSide(flag, side)))
                    .GroupBy(item => item.Flag, StringComparer.OrdinalIgnoreCase)
                    .Select(group => BuildPositiveEvGroupSummary(group.Key, group.Select(item => item.Side).ToArray(), "lineQualityFlag"))
                    .OrderByDescending(row => row.gradedCount)
                    .ThenBy(row => row.group)
                    .ToArray(),
                byProbabilityBucket = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => ProbabilityBucket(side.WinProbability), "probabilityBucket"),
                byEstimatedEvBucket = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => PositiveEvBucket(side.EstimatedEv), "estimatedEvBucket"),
                byDecimalPriceBucket = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => DecimalPriceBucket(side.DecimalPrice), "decimalPriceBucket"),
                bySportsbookLineBucket = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => SportsbookLineBucket(side.Line), "sportsbookLineBucket"),
                byRankingScoreBucket = BuildGroupedPositiveEvPerformance(positiveEvGradedSides, side => RankingScoreBucket(side.RankingScore), "rankingScoreBucket"),
                bestPerformingPositiveEvSubsets = groupedSummaries
                    .Where(group => group.gradedCount >= 25)
                    .OrderByDescending(group => group.simulatedRoi)
                    .ThenByDescending(group => group.simulatedProfit)
                    .Take(25)
                    .ToArray(),
                worstPerformingPositiveEvSubsets = groupedSummaries
                    .Where(group => group.gradedCount >= 25)
                    .OrderBy(group => group.simulatedRoi)
                    .ThenBy(group => group.simulatedProfit)
                    .Take(25)
                    .ToArray(),
                topPositiveEvLosses = positiveEvGradedSides
                    .Where(side => side.Outcome == "Loss")
                    .OrderByDescending(side => side.EstimatedEv)
                    .ThenByDescending(side => side.RankingScore)
                    .Take(100)
                    .Select(ToPositiveEvResultSample)
                    .ToArray(),
                topPositiveEvWins = positiveEvGradedSides
                    .Where(side => side.Outcome == "Win")
                    .OrderByDescending(side => side.EstimatedEv)
                    .ThenByDescending(side => side.RankingScore)
                    .Take(100)
                    .Select(ToPositiveEvResultSample)
                    .ToArray(),
                diagnosis = BuildUnderdogEvLossDiagnosis(positiveEvGradedSides, groupedSummaries)
            });
        })
        .WithName("UnderdogEvLossAnalysisDebug")
        .WithSummary("Diagnoses read-only positive-EV Underdog losses and profitable subsets.");

        endpoints.MapGet("/api/debug/markets/underdog-prop-ev-reliability", async (
            Guid? runId,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var probabilityAnalysis = runId.HasValue
                ? await probabilityService.AnalyzeRunAsync(runId.Value, cancellationToken)
                : await probabilityService.AnalyzeLatestRunAsync(cancellationToken);

            if (probabilityAnalysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var sideRows = await BuildUnderdogEvBacktestRowsAsync(
                probabilityAnalysis,
                dbContext,
                playerIdentityAliasService,
                cancellationToken);
            var propTypeReliability = sideRows
                .GroupBy(side => side.NormalizedPropType, StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildPropTypeEvReliability(group.Key, group.ToArray()))
                .OrderByDescending(row => row.positiveEvSimulatedRoi)
                .ThenByDescending(row => row.positiveEvGradedSides)
                .ToArray();
            var bestPropType = propTypeReliability
                .Where(row => row.positiveEvGradedSides > 0)
                .OrderByDescending(row => row.positiveEvSimulatedRoi)
                .ThenByDescending(row => row.positiveEvGradedSides)
                .FirstOrDefault();
            var worstPropType = propTypeReliability
                .Where(row => row.positiveEvGradedSides > 0)
                .OrderBy(row => row.positiveEvSimulatedRoi)
                .ThenByDescending(row => row.positiveEvGradedSides)
                .FirstOrDefault();
            var recommendedEligiblePropTypes = propTypeReliability
                .Where(row => row.reliabilityClassification == "ProfitableCandidate")
                .Select(row => row.propType)
                .ToArray();
            var recommendedExcludedPropTypes = propTypeReliability
                .Where(row => row.reliabilityClassification == "ExcludeForNow")
                .Select(row => row.propType)
                .ToArray();
            var samplePropTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (bestPropType is not null)
            {
                samplePropTypes.Add(bestPropType.propType);
            }

            if (worstPropType is not null)
            {
                samplePropTypes.Add(worstPropType.propType);
            }

            samplePropTypes.Add("Stolen Bases");
            foreach (var propType in recommendedEligiblePropTypes)
            {
                samplePropTypes.Add(propType);
            }

            return Results.Ok(new
            {
                runId = probabilityAnalysis.RunId,
                probabilityAnalysis.ModelName,
                probabilityAnalysis.ModelVersion,
                probabilityAnalysis.CreatedAt,
                summary = new
                {
                    totalPropTypes = propTypeReliability.Length,
                    profitableCandidateCount = propTypeReliability.Count(row => row.reliabilityClassification == "ProfitableCandidate"),
                    closeButNotProfitableCount = propTypeReliability.Count(row => row.reliabilityClassification == "CloseButNotProfitable"),
                    unprofitableCount = propTypeReliability.Count(row => row.reliabilityClassification == "Unprofitable"),
                    excludeForNowCount = propTypeReliability.Count(row => row.reliabilityClassification == "ExcludeForNow"),
                    insufficientSampleCount = propTypeReliability.Count(row => row.reliabilityClassification == "InsufficientSample"),
                    bestPropTypesByPositiveEvRoi = propTypeReliability
                        .Where(row => row.positiveEvGradedSides > 0)
                        .OrderByDescending(row => row.positiveEvSimulatedRoi)
                        .Take(10)
                        .Select(ToPropTypeReliabilitySummary)
                        .ToArray(),
                    worstPropTypesByPositiveEvRoi = propTypeReliability
                        .Where(row => row.positiveEvGradedSides > 0)
                        .OrderBy(row => row.positiveEvSimulatedRoi)
                        .Take(10)
                        .Select(ToPropTypeReliabilitySummary)
                        .ToArray(),
                    recommendedEligiblePropTypesForNextExperiment = recommendedEligiblePropTypes,
                    recommendedExcludedPropTypesForNextExperiment = recommendedExcludedPropTypes
                },
                propTypes = propTypeReliability,
                positiveEvSamples = samplePropTypes
                    .Select(propType => BuildPropTypePositiveEvSamples(propType, sideRows))
                    .Where(sample => sample.totalPositiveEvGradedSides > 0)
                    .OrderBy(sample => sample.propType)
                    .ToArray(),
                diagnosis = BuildPropTypeEvReliabilityDiagnosis(propTypeReliability)
            });
        })
        .WithName("UnderdogPropEvReliabilityDebug")
        .WithSummary("Audits Underdog positive-EV reliability by MLB prop type without persisting EV.");

        return endpoints;
    }

    private static async Task<MarketRankingRow[]> BuildMarketRankingRowsAsync(
        PredictionProbabilityRunAnalysis probabilityAnalysis,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rankedPredictions = probabilityAnalysis.Results
            .OrderByDescending(result => result.RankingScore)
            .ThenByDescending(result => Math.Abs(result.Edge ?? 0m))
            .ToArray();
        var propIds = rankedPredictions.Select(result => result.PropId).Distinct().ToArray();
        var latestPlatformPropRows = await dbContext.PlatformProps
            .AsNoTracking()
            .Include(platformProp => platformProp.Platform)
            .Where(platformProp => propIds.Contains(platformProp.PropId))
            .ToListAsync(cancellationToken);

        var latestPlatformProps = latestPlatformPropRows
            .Select(platformProp => new PlatformPropRow(
                platformProp.PropId,
                platformProp.Platform?.Name ?? "Unknown",
                platformProp.Platform?.Slug ?? "unknown",
                platformProp.PlatformPropId,
                platformProp.Label,
                platformProp.CurrentLine,
                platformProp.PayoutType,
                platformProp.Status,
                platformProp.OverAvailable,
                platformProp.UnderAvailable,
                platformProp.ObservedAt,
                platformProp.RawPayloadId,
                platformProp.Metadata is null ? null : platformProp.Metadata.RootElement.Clone()))
            .ToList();
        var platformPropByPropId = latestPlatformProps
            .GroupBy(platformProp => platformProp.PropId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(platformProp => platformProp.ObservedAt)
                    .ThenBy(platformProp => platformProp.Platform)
                    .First());
        var propTypeIds = rankedPredictions.Select(result => result.PropTypeId).Distinct().ToArray();
        var historicalRows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.League)
            .Where(result => result.League != null
                && result.League.Code == "MLB"
                && propTypeIds.Contains(result.PropTypeId))
            .Select(result => new
            {
                result.PropTypeId,
                result.ActualResultValue
            })
            .ToListAsync(cancellationToken);
        var distributionByPropType = historicalRows
            .GroupBy(result => result.PropTypeId)
            .ToDictionary(
                group => group.Key,
                group => BuildDistribution(group.Select(result => result.ActualResultValue)));

        return rankedPredictions
            .Select((prediction, index) =>
            {
                platformPropByPropId.TryGetValue(prediction.PropId, out var platformProp);
                distributionByPropType.TryGetValue(prediction.PropTypeId, out var distribution);
                var line = prediction.SportsbookLine ?? platformProp?.CurrentLine;
                var lineFlags = AnalyzeLineQuality(prediction.PropType, line, prediction.PredictedValue, distribution);
                var classification = ClassifyMarket(platformProp, lineFlags);
                var visibility = DetermineVisibility(platformProp, line, prediction, classification);
                var eligibility = DetermineNormalRecommendationEligibility(
                    platformProp,
                    line,
                    prediction,
                    classification,
                    lineFlags);

                return new MarketRankingRow(
                    index + 1,
                    platformProp?.Platform ?? "Unknown",
                    prediction.PlayerName,
                    prediction.PropType,
                    line,
                    platformProp?.Label ?? string.Empty,
                    platformProp?.PlatformPropId ?? string.Empty,
                    platformProp?.PayoutType ?? "unknown",
                    platformProp?.Status ?? "unknown",
                    platformProp?.OverAvailable,
                    platformProp?.UnderAvailable,
                    platformProp?.ObservedAt,
                    platformProp?.RawPayloadId,
                    platformProp?.Metadata,
                    prediction.PlayerId,
                    prediction.PropTypeId,
                    prediction.EventDate,
                    prediction.PredictedValue,
                    prediction.ProbabilityOver,
                    prediction.ProbabilityUnder,
                    prediction.ProbabilityPush,
                    prediction.RankingScore,
                    prediction.Recommendation,
                    classification,
                    lineFlags,
                    visibility.IsVisible,
                    eligibility.IsEligible,
                    BuildDisplayWarnings(classification, lineFlags),
                    visibility.Reasons,
                    eligibility.Reasons,
                    DetermineHiddenAssessment(visibility.Reasons));
            })
            .ToArray();
    }

    private static object ToMarketRankingResponse(MarketRankingRow row)
    {
        return new
        {
            row.Rank,
            player = row.Player,
            sportsbook = row.Sportsbook,
            platform = row.Sportsbook,
            platformPropId = row.PlatformPropId,
            rawSportsbookLabel = row.RawSportsbookLabel,
            normalizedPropType = row.NormalizedPropType,
            sportsbookLine = row.SportsbookLine,
            predictedValue = row.PredictedValue,
            probabilityOver = row.ProbabilityOver,
            probabilityUnder = row.ProbabilityUnder,
            probabilityPush = row.ProbabilityPush,
            rankingScore = row.RankingScore,
            recommendation = row.Recommendation,
            marketClassification = row.MarketClassification,
            payoutType = row.PayoutType,
            status = row.Status,
            overAvailable = row.OverAvailable,
            underAvailable = row.UnderAvailable,
            observedAt = row.ObservedAt,
            lineQualityFlags = row.LineQualityFlags,
            isVisibleInResearchDashboard = row.IsVisibleInResearchDashboard,
            isEligibleAsNormalRecommendation = row.IsEligibleAsNormalRecommendation,
            displayWarnings = row.DisplayWarnings,
            hiddenReasons = row.HiddenReasons,
            ineligibilityReasons = row.IneligibilityReasons
        };
    }

    private static object ToHiddenMarketSampleResponse(MarketRankingRow row)
    {
        return new
        {
            row.Rank,
            player = row.Player,
            sportsbook = row.Sportsbook,
            platform = row.Sportsbook,
            platformPropId = row.PlatformPropId,
            rawLabel = row.RawSportsbookLabel,
            normalizedPropType = row.NormalizedPropType,
            line = row.SportsbookLine,
            status = row.Status,
            overAvailable = row.OverAvailable,
            underAvailable = row.UnderAvailable,
            observedAt = row.ObservedAt,
            updatedAt = row.ObservedAt,
            probabilityOver = row.ProbabilityOver,
            probabilityUnder = row.ProbabilityUnder,
            rankingScore = row.RankingScore,
            marketClassification = row.MarketClassification,
            hiddenReasons = row.HiddenReasons,
            hiddenAssessment = row.HiddenAssessment,
            ineligibilityReasons = row.IneligibilityReasons
        };
    }

    private static async Task<IReadOnlyDictionary<ActualResultKey, decimal>> LoadMlbActualResultsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.League)
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
            .GroupBy(row => new ActualResultKey(row.PlayerId, row.PropTypeId, row.EventDate))
            .ToDictionary(
                group => group.Key,
                group => group.First().ActualResultValue);
    }

    private static async Task<IReadOnlyDictionary<Guid, Guid[]>> LoadEquivalentPropTypeIdsByIdAsync(
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
            .GroupBy(propType => CanonicalPropTypeName(propType.Name), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var equivalentIds = group.Select(propType => propType.Id).Distinct().ToArray();
                return group.Select(propType => new
                {
                    propType.Id,
                    EquivalentIds = equivalentIds
                });
            })
            .ToDictionary(item => item.Id, item => item.EquivalentIds);
    }

    private static async Task<Guid[]> GetEquivalentPlayerIdsAsync(
        Guid playerId,
        IPlayerIdentityAliasService playerIdentityAliasService,
        IDictionary<Guid, Guid[]> playerAliasCache,
        CancellationToken cancellationToken)
    {
        if (playerAliasCache.TryGetValue(playerId, out var cachedPlayerIds))
        {
            return cachedPlayerIds;
        }

        var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
            playerId,
            "MLB",
            cancellationToken);
        var equivalentPlayerIdArray = equivalentPlayerIds.Distinct().ToArray();
        playerAliasCache[playerId] = equivalentPlayerIdArray;

        return equivalentPlayerIdArray;
    }

    private static decimal? FindActualResult(
        IReadOnlyDictionary<ActualResultKey, decimal> actualResults,
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate,
        IReadOnlyCollection<Guid> equivalentPlayerIds,
        IReadOnlyCollection<Guid> equivalentPropTypeIds)
    {
        var candidatePlayerIds = equivalentPlayerIds
            .OrderBy(candidatePlayerId => candidatePlayerId == playerId ? 0 : 1)
            .ThenBy(candidatePlayerId => candidatePlayerId)
            .ToArray();
        var candidatePropTypeIds = equivalentPropTypeIds
            .OrderBy(candidatePropTypeId => candidatePropTypeId == propTypeId ? 0 : 1)
            .ThenBy(candidatePropTypeId => candidatePropTypeId)
            .ToArray();

        foreach (var candidatePlayerId in candidatePlayerIds)
        {
            foreach (var candidatePropTypeId in candidatePropTypeIds)
            {
                if (actualResults.TryGetValue(new ActualResultKey(candidatePlayerId, candidatePropTypeId, eventDate), out var actualResult))
                {
                    return actualResult;
                }
            }
        }

        return null;
    }

    private static string? GradeUnderdogSide(string side, decimal? line, decimal? actualResult)
    {
        if (!line.HasValue || !actualResult.HasValue)
        {
            return null;
        }

        return side switch
        {
            "Higher" when actualResult.Value > line.Value => "Win",
            "Higher" when actualResult.Value < line.Value => "Loss",
            "Lower" when actualResult.Value < line.Value => "Win",
            "Lower" when actualResult.Value > line.Value => "Loss",
            _ => "Push"
        };
    }

    private static decimal? CalculateProfitPerDollarStake(string? outcome, decimal? decimalPrice)
    {
        return outcome switch
        {
            "Win" when decimalPrice.HasValue => Math.Round(decimalPrice.Value - 1m, 6),
            "Loss" => -1m,
            "Push" => 0m,
            _ => null
        };
    }

    private static object[] BuildEvBucketBacktest(IReadOnlyCollection<UnderdogEvBacktestSideRow> sides)
    {
        var buckets = new[]
        {
            new EvBucket("estimatedEv < -0.25", null, -0.25m, false, true),
            new EvBucket("-0.25 to -0.10", -0.25m, -0.10m, true, false),
            new EvBucket("-0.10 to 0", -0.10m, 0m, true, false),
            new EvBucket("0 to 0.10", 0m, 0.10m, true, false),
            new EvBucket("0.10 to 0.25", 0.10m, 0.25m, true, false),
            new EvBucket("0.25+", 0.25m, null, true, false)
        };

        return buckets
            .Select(bucket =>
            {
                var rows = sides.Where(side => IsInBucket(side.EstimatedEv, bucket)).ToArray();
                return BuildEvBacktestSummary(bucket.Label, rows);
            })
            .ToArray();
    }

    private static object BuildSideBreakdown(string side, IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        var positiveEvRows = rows.Where(row => row.EstimatedEv > 0).ToArray();
        var gradedRows = rows.Where(row => row.Outcome is "Win" or "Loss" or "Push").ToArray();
        var positiveEvGradedRows = positiveEvRows.Where(row => row.Outcome is "Win" or "Loss" or "Push").ToArray();

        return new
        {
            side,
            sideCount = rows.Count,
            gradedCount = gradedRows.Length,
            wins = gradedRows.Count(row => row.Outcome == "Win"),
            losses = gradedRows.Count(row => row.Outcome == "Loss"),
            pushes = gradedRows.Count(row => row.Outcome == "Push"),
            hitRate = HitRate(gradedRows),
            simulatedProfit = Profit(gradedRows),
            simulatedRoi = Roi(gradedRows),
            positiveEvCount = positiveEvRows.Length,
            positiveEvGradedCount = positiveEvGradedRows.Length,
            positiveEvHitRate = HitRate(positiveEvGradedRows),
            positiveEvSimulatedProfit = Profit(positiveEvGradedRows),
            positiveEvSimulatedRoi = Roi(positiveEvGradedRows)
        };
    }

    private static object BuildEvBacktestSummary(string bucket, IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        var gradedRows = rows.Where(row => row.Outcome is "Win" or "Loss" or "Push").ToArray();

        return new
        {
            bucket,
            sideCount = rows.Count,
            gradedCount = gradedRows.Length,
            wins = gradedRows.Count(row => row.Outcome == "Win"),
            losses = gradedRows.Count(row => row.Outcome == "Loss"),
            pushes = gradedRows.Count(row => row.Outcome == "Push"),
            hitRate = HitRate(gradedRows),
            simulatedProfit = Profit(gradedRows),
            simulatedRoi = Roi(gradedRows),
            averageEstimatedEv = Average(rows.Select(row => row.EstimatedEv)),
            averageDecimalPrice = Average(rows.Select(row => row.DecimalPrice)),
            averageWinProbability = Average(rows.Select(row => row.WinProbability))
        };
    }

    private static async Task<UnderdogEvBacktestSideRow[]> BuildUnderdogEvBacktestRowsAsync(
        PredictionProbabilityRunAnalysis probabilityAnalysis,
        ApplicationDbContext dbContext,
        IPlayerIdentityAliasService playerIdentityAliasService,
        CancellationToken cancellationToken)
    {
        var rows = await BuildMarketRankingRowsAsync(probabilityAnalysis, dbContext, cancellationToken);
        var underdogRows = rows
            .Where(row => row.Sportsbook.Contains("Underdog", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var actualResults = await LoadMlbActualResultsAsync(dbContext, cancellationToken);
        var equivalentPropTypeIdsById = await LoadEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
        var playerAliasCache = new Dictionary<Guid, Guid[]>();
        var sideRows = new List<UnderdogEvBacktestSideRow>();

        foreach (var row in underdogRows)
        {
            var equivalentPlayerIds = await GetEquivalentPlayerIdsAsync(
                row.PlayerId,
                playerIdentityAliasService,
                playerAliasCache,
                cancellationToken);
            var equivalentPropTypeIds = equivalentPropTypeIdsById.TryGetValue(row.PropTypeId, out var propTypeIds)
                ? propTypeIds
                : [row.PropTypeId];
            var actualResult = FindActualResult(
                actualResults,
                row.PlayerId,
                row.PropTypeId,
                row.EventDate,
                equivalentPlayerIds,
                equivalentPropTypeIds);

            sideRows.AddRange(BuildUnderdogEvSideRows(row)
                .Select(side =>
                {
                    var outcome = GradeUnderdogSide(side.Side, row.SportsbookLine, actualResult);
                    var profit = CalculateProfitPerDollarStake(outcome, side.DecimalPrice);

                    return new UnderdogEvBacktestSideRow(
                        side.Player,
                        side.PlatformPropId,
                        side.RawSportsbookLabel,
                        side.NormalizedPropType,
                        side.Line,
                        side.Side,
                        side.DecimalPrice,
                        side.AmericanPrice,
                        side.WinProbability,
                        side.ImpliedProbability,
                        side.ProbabilityEdge,
                        side.EstimatedEv,
                        side.RankingScore,
                        actualResult,
                        outcome,
                        profit,
                        side.PriceConsistencyFlags,
                        row.MarketClassification,
                        row.PayoutType,
                        row.LineQualityFlags,
                        row.ObservedAt);
                }));
        }

        return sideRows.ToArray();
    }

    private static PropTypeEvReliabilityRow BuildPropTypeEvReliability(
        string propType,
        IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        var gradedRows = rows.Where(row => row.Outcome is "Win" or "Loss" or "Push").ToArray();
        var positiveEvRows = rows.Where(row => row.EstimatedEv > 0).ToArray();
        var positiveEvGradedRows = positiveEvRows.Where(row => row.Outcome is "Win" or "Loss" or "Push").ToArray();
        var positiveEvRoi = Roi(positiveEvGradedRows);

        return new PropTypeEvReliabilityRow(
            propType,
            rows.Count,
            gradedRows.Length,
            gradedRows.Count(row => row.Outcome == "Win"),
            gradedRows.Count(row => row.Outcome == "Loss"),
            gradedRows.Count(row => row.Outcome == "Push"),
            HitRate(gradedRows),
            Profit(gradedRows),
            Roi(gradedRows),
            Average(rows.Select(row => row.EstimatedEv)),
            Average(rows.Select(row => row.DecimalPrice)),
            Average(rows.Select(row => row.WinProbability)),
            Average(rows.Select(row => row.ImpliedProbability)),
            positiveEvRows.Length,
            positiveEvGradedRows.Length,
            positiveEvGradedRows.Count(row => row.Outcome == "Win"),
            positiveEvGradedRows.Count(row => row.Outcome == "Loss"),
            positiveEvGradedRows.Count(row => row.Outcome == "Push"),
            HitRate(positiveEvGradedRows),
            Profit(positiveEvGradedRows),
            positiveEvRoi,
            Average(positiveEvRows.Select(row => row.EstimatedEv)),
            Average(positiveEvRows.Select(row => row.DecimalPrice)),
            Average(positiveEvRows.Select(row => row.WinProbability)),
            Average(positiveEvRows.Select(row => row.ImpliedProbability)),
            rows.GroupBy(row => row.Side)
                .OrderBy(group => group.Key)
                .Select(group => BuildPropTypeSideReliability(group.Key, group.ToArray()))
                .ToArray(),
            ClassifyPropTypeReliability(positiveEvGradedRows.Length, positiveEvRoi));
    }

    private static PropTypeSideReliabilityRow BuildPropTypeSideReliability(
        string side,
        IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        var gradedRows = rows.Where(row => row.Outcome is "Win" or "Loss" or "Push").ToArray();
        var positiveEvGradedRows = rows
            .Where(row => row.EstimatedEv > 0 && row.Outcome is "Win" or "Loss" or "Push")
            .ToArray();

        return new PropTypeSideReliabilityRow(
            side,
            gradedRows.Length,
            gradedRows.Count(row => row.Outcome == "Win"),
            gradedRows.Count(row => row.Outcome == "Loss"),
            gradedRows.Count(row => row.Outcome == "Push"),
            HitRate(gradedRows),
            Profit(gradedRows),
            Roi(gradedRows),
            positiveEvGradedRows.Length,
            positiveEvGradedRows.Count(row => row.Outcome == "Win"),
            positiveEvGradedRows.Count(row => row.Outcome == "Loss"),
            HitRate(positiveEvGradedRows),
            Profit(positiveEvGradedRows),
            Roi(positiveEvGradedRows));
    }

    private static string ClassifyPropTypeReliability(int positiveEvGradedSides, decimal positiveEvRoi)
    {
        if (positiveEvGradedSides < 50)
        {
            return "InsufficientSample";
        }

        if (positiveEvRoi > 0.05m)
        {
            return "ProfitableCandidate";
        }

        if (positiveEvRoi >= -0.05m)
        {
            return "CloseButNotProfitable";
        }

        return positiveEvRoi >= -0.20m
            ? "Unprofitable"
            : "ExcludeForNow";
    }

    private static object ToPropTypeReliabilitySummary(PropTypeEvReliabilityRow row)
    {
        return new
        {
            row.propType,
            row.positiveEvGradedSides,
            row.positiveEvHitRate,
            row.positiveEvSimulatedProfit,
            row.positiveEvSimulatedRoi,
            row.reliabilityClassification
        };
    }

    private static PropTypePositiveEvSamples BuildPropTypePositiveEvSamples(
        string propType,
        IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        var propTypeRows = rows
            .Where(row => string.Equals(row.NormalizedPropType, propType, StringComparison.OrdinalIgnoreCase)
                && row.EstimatedEv > 0
                && row.Outcome is "Win" or "Loss" or "Push")
            .ToArray();

        return new PropTypePositiveEvSamples(
            propType,
            propTypeRows.Length,
            propTypeRows
                .Where(row => row.Outcome == "Win")
                .OrderByDescending(row => row.EstimatedEv)
                .ThenByDescending(row => row.RankingScore)
                .Take(50)
                .Select(ToPositiveEvResultSample)
                .ToArray(),
            propTypeRows
                .Where(row => row.Outcome == "Loss")
                .OrderByDescending(row => row.EstimatedEv)
                .ThenByDescending(row => row.RankingScore)
                .Take(50)
                .Select(ToPositiveEvResultSample)
                .ToArray());
    }

    private static object BuildPropTypeEvReliabilityDiagnosis(IReadOnlyCollection<PropTypeEvReliabilityRow> propTypes)
    {
        var higherRows = propTypes
            .SelectMany(row => row.sideBreakdown.Where(side => side.side == "Higher"))
            .ToArray();
        var lowerRows = propTypes
            .SelectMany(row => row.sideBreakdown.Where(side => side.side == "Lower"))
            .ToArray();
        var higherProfit = higherRows.Sum(row => row.positiveEvSimulatedProfit);
        var lowerProfit = lowerRows.Sum(row => row.positiveEvSimulatedProfit);
        var stolenBases = propTypes.FirstOrDefault(row => row.propType == "Stolen Bases");
        var excludedPropTypes = propTypes
            .Where(row => row.reliabilityClassification == "ExcludeForNow")
            .Select(row => row.propType)
            .ToArray();
        var eligiblePropTypes = propTypes
            .Where(row => row.reliabilityClassification == "ProfitableCandidate")
            .Select(row => row.propType)
            .ToArray();

        return new
        {
            isEvFailureMostlyPropTypeDriven = excludedPropTypes.Length > 0 || propTypes.Any(row => row.positiveEvSimulatedRoi > 0.05m),
            shouldStolenBasesBeExcludedForNow = stolenBases?.reliabilityClassification == "ExcludeForNow",
            shouldHigherBeExcludedForNow = higherProfit < lowerProfit && higherProfit < 0,
            recommendedNextEvFilterRules = BuildRecommendedPropTypeEvRules(eligiblePropTypes, excludedPropTypes, stolenBases)
        };
    }

    private static string[] BuildRecommendedPropTypeEvRules(
        IReadOnlyCollection<string> eligiblePropTypes,
        IReadOnlyCollection<string> excludedPropTypes,
        PropTypeEvReliabilityRow? stolenBases)
    {
        var rules = new List<string>
        {
            "Keep Underdog EV research-only until prop-type filters are validated on a fresh out-of-sample run.",
            "Require positiveEvGradedSides >= 50 before treating a prop type as reliable."
        };

        if (eligiblePropTypes.Count > 0)
        {
            rules.Add($"Next experiment eligible prop types: {string.Join(", ", eligiblePropTypes)}.");
        }
        else
        {
            rules.Add("No prop type currently qualifies as a ProfitableCandidate under the configured reliability rules.");
        }

        if (excludedPropTypes.Count > 0)
        {
            rules.Add($"Exclude for now: {string.Join(", ", excludedPropTypes)}.");
        }

        if (stolenBases is not null)
        {
            rules.Add($"Stolen Bases classification: {stolenBases.reliabilityClassification}; positive-EV ROI {stolenBases.positiveEvSimulatedRoi:P2}.");
        }

        rules.Add("Analyze Higher and Lower separately before allowing either side into EV ranking.");

        return rules.ToArray();
    }


    private static PositiveEvSubsetSummary[] BuildPositiveEvLossAnalysisGroups(IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        return
        [
            .. BuildGroupedPositiveEvPerformance(rows, side => side.Side, "side"),
            .. BuildGroupedPositiveEvPerformance(rows, side => side.NormalizedPropType, "propType"),
            .. BuildGroupedPositiveEvPerformance(rows, side => side.MarketClassification, "marketClassification"),
            .. BuildGroupedPositiveEvPerformance(rows, side => side.PayoutType, "payoutType"),
            .. rows
                .SelectMany(side => side.LineQualityFlags.Length == 0
                    ? [new LineFlagSide("NoLineQualityFlags", side)]
                    : side.LineQualityFlags.Select(flag => new LineFlagSide(flag, side)))
                .GroupBy(item => item.Flag, StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildPositiveEvGroupSummary(group.Key, group.Select(item => item.Side).ToArray(), "lineQualityFlag")),
            .. BuildGroupedPositiveEvPerformance(rows, side => ProbabilityBucket(side.WinProbability), "probabilityBucket"),
            .. BuildGroupedPositiveEvPerformance(rows, side => PositiveEvBucket(side.EstimatedEv), "estimatedEvBucket"),
            .. BuildGroupedPositiveEvPerformance(rows, side => DecimalPriceBucket(side.DecimalPrice), "decimalPriceBucket"),
            .. BuildGroupedPositiveEvPerformance(rows, side => SportsbookLineBucket(side.Line), "sportsbookLineBucket"),
            .. BuildGroupedPositiveEvPerformance(rows, side => RankingScoreBucket(side.RankingScore), "rankingScoreBucket")
        ];
    }

    private static PositiveEvSubsetSummary[] BuildGroupedPositiveEvPerformance(
        IReadOnlyCollection<UnderdogEvBacktestSideRow> rows,
        Func<UnderdogEvBacktestSideRow, string> groupKey,
        string dimension)
    {
        return rows
            .GroupBy(groupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPositiveEvGroupSummary(group.Key, group.ToArray(), dimension))
            .OrderByDescending(group => group.gradedCount)
            .ThenBy(group => group.group)
            .ToArray();
    }

    private static PositiveEvSubsetSummary BuildPositiveEvGroupSummary(
        string group,
        IReadOnlyCollection<UnderdogEvBacktestSideRow> rows,
        string dimension)
    {
        var gradedRows = rows.Where(row => row.Outcome is "Win" or "Loss" or "Push").ToArray();

        return new PositiveEvSubsetSummary(
            dimension,
            group,
            rows.Count,
            gradedRows.Length,
            gradedRows.Count(row => row.Outcome == "Win"),
            gradedRows.Count(row => row.Outcome == "Loss"),
            gradedRows.Count(row => row.Outcome == "Push"),
            HitRate(gradedRows),
            Profit(gradedRows),
            Roi(gradedRows),
            Average(rows.Select(row => row.EstimatedEv)),
            Average(rows.Select(row => row.DecimalPrice)),
            Average(rows.Select(row => row.WinProbability)),
            Average(rows.Select(row => row.ImpliedProbability)));
    }

    private static object ToPositiveEvResultSample(UnderdogEvBacktestSideRow side)
    {
        return new
        {
            player = side.Player,
            rawLabel = side.RawSportsbookLabel,
            propType = side.NormalizedPropType,
            side = side.Side,
            line = side.Line,
            actualResult = side.ActualResult,
            decimalPrice = side.DecimalPrice,
            winProbability = side.WinProbability,
            impliedProbability = side.ImpliedProbability,
            estimatedEv = side.EstimatedEv,
            rankingScore = side.RankingScore,
            marketClassification = side.MarketClassification,
            payoutType = side.PayoutType,
            lineQualityFlags = side.LineQualityFlags,
            observedAt = side.ObservedAt
        };
    }

    private static object BuildUnderdogEvLossDiagnosis(
        IReadOnlyCollection<UnderdogEvBacktestSideRow> positiveEvGradedSides,
        IReadOnlyCollection<PositiveEvSubsetSummary> groupedSummaries)
    {
        var profitableGroups = groupedSummaries
            .Where(group => group.gradedCount >= 25 && group.simulatedProfit > 0 && group.simulatedRoi > 0)
            .OrderByDescending(group => group.simulatedRoi)
            .ThenByDescending(group => group.simulatedProfit)
            .ToArray();
        var weakestGroup = groupedSummaries
            .Where(group => group.gradedCount >= 25)
            .OrderBy(group => group.simulatedRoi)
            .ThenBy(group => group.simulatedProfit)
            .FirstOrDefault();
        var higherRows = positiveEvGradedSides.Where(side => side.Side == "Higher").ToArray();
        var lowerRows = positiveEvGradedSides.Where(side => side.Side == "Lower").ToArray();
        var extremeOddsRows = positiveEvGradedSides.Where(side => side.DecimalPrice >= 5m).ToArray();
        var losingPropTypeGroups = positiveEvGradedSides
            .GroupBy(side => side.NormalizedPropType, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPositiveEvGroupSummary(group.Key, group.ToArray(), "propType"))
            .Where(group => group.gradedCount >= 25 && group.simulatedProfit < 0)
            .OrderBy(group => group.simulatedProfit)
            .ToArray();

        return new
        {
            strongestProfitableSubsetFound = profitableGroups.Length > 0,
            strongestSubsetDescription = profitableGroups.Length == 0
                ? "No profitable positive-EV subset with gradedCount >= 25 was found."
                : $"{profitableGroups[0].dimension}: {profitableGroups[0].group} ROI {profitableGroups[0].simulatedRoi:P2}, profit {profitableGroups[0].simulatedProfit}.",
            weakestSubsetDescription = weakestGroup is null
                ? "No subset with gradedCount >= 25 was available."
                : $"{weakestGroup.dimension}: {weakestGroup.group} ROI {weakestGroup.simulatedRoi:P2}, profit {weakestGroup.simulatedProfit}.",
            isFailureMostlyHigherSide = Profit(higherRows) < Profit(lowerRows) && Roi(higherRows) < Roi(lowerRows),
            isFailureMostlyExtremeOdds = extremeOddsRows.Length > 0
                && Profit(extremeOddsRows) < 0
                && Math.Abs(Profit(extremeOddsRows)) > Math.Abs(Profit(positiveEvGradedSides)) * 0.5m,
            isFailureMostlySpecificPropTypes = losingPropTypeGroups.Length > 0
                && Math.Abs(losingPropTypeGroups.Take(3).Sum(group => group.simulatedProfit)) > Math.Abs(Profit(positiveEvGradedSides)) * 0.5m,
            recommendedNextRuleChanges = BuildRecommendedEvRuleChanges(profitableGroups, weakestGroup, higherRows, lowerRows, extremeOddsRows, losingPropTypeGroups)
        };
    }

    private static string[] BuildRecommendedEvRuleChanges(
        IReadOnlyCollection<PositiveEvSubsetSummary> profitableGroups,
        PositiveEvSubsetSummary? weakestGroup,
        IReadOnlyCollection<UnderdogEvBacktestSideRow> higherRows,
        IReadOnlyCollection<UnderdogEvBacktestSideRow> lowerRows,
        IReadOnlyCollection<UnderdogEvBacktestSideRow> extremeOddsRows,
        IReadOnlyCollection<PositiveEvSubsetSummary> losingPropTypeGroups)
    {
        var recommendations = new List<string>
        {
            "Keep Underdog EV research-only until positive-EV subsets show stable positive ROI.",
            "Do not persist or surface EV as a recommendation score yet."
        };

        if (Roi(higherRows) < Roi(lowerRows))
        {
            recommendations.Add("Treat Higher-side positive EV as lower priority or require stricter calibration before surfacing.");
        }

        if (extremeOddsRows.Count > 0 && Roi(extremeOddsRows) < 0)
        {
            recommendations.Add("Add an experimental cap or separate review bucket for extreme decimal prices >= 5.0.");
        }

        foreach (var propType in losingPropTypeGroups.Take(3))
        {
            recommendations.Add($"Review or disable positive-EV research surfacing for {propType.group} until calibration improves.");
        }

        if (profitableGroups.Count > 0)
        {
            var best = profitableGroups.First();
            recommendations.Add($"Investigate the profitable subset first: {best.dimension}={best.group}.");
        }

        if (weakestGroup is not null)
        {
            recommendations.Add($"Investigate the weakest subset first: {weakestGroup.dimension}={weakestGroup.group}.");
        }

        return recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ProbabilityBucket(decimal? probability)
    {
        if (!probability.HasValue)
        {
            return "Missing";
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

    private static string PositiveEvBucket(decimal? estimatedEv)
    {
        if (!estimatedEv.HasValue)
        {
            return "Missing";
        }

        return estimatedEv.Value switch
        {
            < 0.05m => "0 to 0.05",
            < 0.10m => "0.05 to 0.10",
            < 0.25m => "0.10 to 0.25",
            < 0.50m => "0.25 to 0.50",
            _ => "0.50+"
        };
    }

    private static string DecimalPriceBucket(decimal? decimalPrice)
    {
        if (!decimalPrice.HasValue)
        {
            return "Missing";
        }

        return decimalPrice.Value switch
        {
            < 1.5m => "<1.5",
            < 2.0m => "1.5-2.0",
            < 3.0m => "2.0-3.0",
            < 5.0m => "3.0-5.0",
            < 10.0m => "5.0-10.0",
            _ => "10.0+"
        };
    }

    private static string SportsbookLineBucket(decimal? line)
    {
        if (!line.HasValue)
        {
            return "Missing";
        }

        return line.Value switch
        {
            0.5m => "0.5",
            1.0m => "1.0",
            1.5m => "1.5",
            2.5m => "2.5",
            >= 3.5m => "3.5+",
            _ => "Other"
        };
    }

    private static string RankingScoreBucket(decimal rankingScore)
    {
        return rankingScore switch
        {
            < 10m => "<10",
            < 20m => "10-20",
            < 30m => "20-30",
            < 40m => "30-40",
            _ => "40+"
        };
    }

    private static object BuildUnderdogEvBacktestFindings(IReadOnlyCollection<UnderdogEvBacktestSideRow> sides)
    {
        var positiveEvGraded = sides.Where(side => side.EstimatedEv > 0 && side.Outcome is "Win" or "Loss" or "Push").ToArray();
        var higherPositive = positiveEvGraded.Where(side => side.Side == "Higher").ToArray();
        var lowerPositive = positiveEvGraded.Where(side => side.Side == "Lower").ToArray();
        var lowPositiveEvGraded = positiveEvGraded
            .Where(side => side.EstimatedEv >= 0m && side.EstimatedEv < 0.10m)
            .ToArray();
        var highPositiveEvGraded = positiveEvGraded
            .Where(side => side.EstimatedEv >= 0.25m)
            .ToArray();
        var higherEvImprovesRoi = highPositiveEvGraded.Length > 0
            && lowPositiveEvGraded.Length > 0
            && Roi(highPositiveEvGraded) > Roi(lowPositiveEvGraded);
        var positiveEvProfit = Profit(positiveEvGraded);
        var signalDistribution = lowerPositive.Length > higherPositive.Length * 1.25m
            ? "MostlyLower"
            : higherPositive.Length > lowerPositive.Length * 1.25m
                ? "MostlyHigher"
                : "Both";

        return new
        {
            doesHigherEstimatedEvCorrespondToBetterRealizedRoi = higherEvImprovesRoi,
            arePositiveEvUnderdogMarketsProfitableInThisBacktest = positiveEvProfit > 0,
            signalDistribution,
            shouldEvBeTrustedEnoughToPersistLater = "ResearchOnlyForNow",
            rationale = positiveEvProfit > 0
                ? "Positive-EV sides were profitable in this sample, but EV remains research-only until grading coverage, promotional market semantics, and calibration are reviewed."
                : "Positive-EV sides were not profitable in this sample. EV should remain research-only; probability calibration and promotional market semantics need more work before persistence or product recommendations."
        };
    }

    private static bool IsInBucket(decimal? value, EvBucket bucket)
    {
        if (!value.HasValue)
        {
            return false;
        }

        var lowerMatches = !bucket.LowerBound.HasValue
            || (bucket.IncludeLower ? value.Value >= bucket.LowerBound.Value : value.Value > bucket.LowerBound.Value);
        var upperMatches = !bucket.UpperBound.HasValue
            || (bucket.IncludeUpper ? value.Value <= bucket.UpperBound.Value : value.Value < bucket.UpperBound.Value);

        return lowerMatches && upperMatches;
    }

    private static decimal HitRate(IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        var decisions = rows.Count(row => row.Outcome is "Win" or "Loss");
        return decisions == 0
            ? 0
            : Math.Round(rows.Count(row => row.Outcome == "Win") / (decimal)decisions, 4);
    }

    private static decimal Profit(IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        return Math.Round(rows.Sum(row => row.ProfitPerOneDollarStake ?? 0m), 4);
    }

    private static decimal Roi(IReadOnlyCollection<UnderdogEvBacktestSideRow> rows)
    {
        var gradedCount = rows.Count(row => row.Outcome is "Win" or "Loss" or "Push");
        return gradedCount == 0
            ? 0
            : Math.Round(Profit(rows) / gradedCount, 4);
    }

    private static decimal Average(IEnumerable<decimal?> values)
    {
        var valueArray = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return valueArray.Length == 0 ? 0 : Math.Round(valueArray.Average(), 4);
    }

    private static string CanonicalPropTypeName(string propTypeName)
    {
        return NormalizePropTypeName(propTypeName) switch
        {
            "RBI" => "RBIS",
            _ => NormalizePropTypeName(propTypeName)
        };
    }

    private static string NormalizePropTypeName(string propTypeName)
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

    private static IEnumerable<UnderdogEvSideRow> BuildUnderdogEvSideRows(MarketRankingRow row)
    {
        var higher = ExtractUnderdogOptionPricing(row.Metadata, "higher");
        var lower = ExtractUnderdogOptionPricing(row.Metadata, "lower");

        yield return BuildUnderdogEvSideRow(row, "Higher", higher, row.ProbabilityOver, row.ProbabilityUnder);
        yield return BuildUnderdogEvSideRow(row, "Lower", lower, row.ProbabilityUnder, row.ProbabilityOver);
    }

    private static UnderdogEvSideRow BuildUnderdogEvSideRow(
        MarketRankingRow row,
        string side,
        UnderdogOptionPricing pricing,
        decimal? winProbability,
        decimal? loseProbability)
    {
        var decimalFromAmerican = ConvertAmericanToDecimal(pricing.AmericanPrice);
        var impliedProbability = pricing.DecimalPrice.HasValue && pricing.DecimalPrice.Value > 0
            ? Math.Round(1m / pricing.DecimalPrice.Value, 6)
            : (decimal?)null;
        var probabilityEdge = winProbability.HasValue && impliedProbability.HasValue
            ? Math.Round(winProbability.Value - impliedProbability.Value, 6)
            : (decimal?)null;
        var estimatedEv = pricing.DecimalPrice.HasValue && winProbability.HasValue && loseProbability.HasValue
            ? Math.Round((winProbability.Value * (pricing.DecimalPrice.Value - 1m)) - loseProbability.Value, 6)
            : (decimal?)null;
        var flags = BuildPriceConsistencyFlags(pricing.DecimalPrice, pricing.AmericanPrice, decimalFromAmerican, pricing.PayoutMultiplier);

        return new UnderdogEvSideRow(
            row.Player,
            row.PlatformPropId,
            row.RawSportsbookLabel,
            row.NormalizedPropType,
            row.SportsbookLine,
            side,
            pricing.DecimalPrice,
            pricing.AmericanPrice,
            decimalFromAmerican,
            pricing.PayoutMultiplier,
            winProbability,
            loseProbability,
            impliedProbability,
            probabilityEdge,
            estimatedEv,
            row.RankingScore,
            flags);
    }

    private static UnderdogOptionPricing ExtractUnderdogOptionPricing(JsonElement? metadata, string side)
    {
        if (!metadata.HasValue
            || metadata.Value.ValueKind != JsonValueKind.Object
            || !metadata.Value.TryGetProperty("options", out var options)
            || options.ValueKind != JsonValueKind.Object
            || !options.TryGetProperty(side, out var option)
            || option.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new UnderdogOptionPricing(null, null, null);
        }

        return new UnderdogOptionPricing(
            TryReadDecimal(option, "decimalPrice"),
            TryReadString(option, "americanPrice"),
            TryReadDecimal(option, "payoutMultiplier"));
    }

    private static decimal? ConvertAmericanToDecimal(string? americanPrice)
    {
        if (string.IsNullOrWhiteSpace(americanPrice)
            || !int.TryParse(americanPrice.Replace("+", string.Empty, StringComparison.Ordinal), out var american)
            || american == 0)
        {
            return null;
        }

        return american > 0
            ? Math.Round(1m + (american / 100m), 6)
            : Math.Round(1m + (100m / Math.Abs(american)), 6);
    }

    private static string[] BuildPriceConsistencyFlags(
        decimal? decimalPrice,
        string? americanPrice,
        decimal? decimalFromAmerican,
        decimal? payoutMultiplier)
    {
        var flags = new List<string>();

        if (!decimalPrice.HasValue)
        {
            flags.Add("MissingDecimalPrice");
        }

        if (string.IsNullOrWhiteSpace(americanPrice))
        {
            flags.Add("MissingAmericanPrice");
        }

        if (!payoutMultiplier.HasValue)
        {
            flags.Add("MissingPayoutMultiplier");
        }

        if (decimalPrice.HasValue && decimalFromAmerican.HasValue)
        {
            flags.Add(IsClose(decimalPrice.Value, decimalFromAmerican.Value)
                ? "AmericanDecimalMatch"
                : "AmericanDecimalMismatch");
        }

        if (decimalPrice.HasValue && payoutMultiplier.HasValue)
        {
            if (IsClose(payoutMultiplier.Value, decimalPrice.Value))
            {
                flags.Add("PayoutMultiplierEqualsDecimalPrice");
            }
            else if (IsClose(payoutMultiplier.Value, decimalPrice.Value - 1m))
            {
                flags.Add("PayoutMultiplierEqualsDecimalPriceMinusOne");
            }
            else
            {
                flags.Add("PayoutMultiplierUnrelated");
            }
        }

        return flags.ToArray();
    }

    private static object ToPriceConsistencySample(UnderdogEvSideRow side)
    {
        return new
        {
            player = side.Player,
            platformPropId = side.PlatformPropId,
            rawLabel = side.RawSportsbookLabel,
            propType = side.NormalizedPropType,
            side = side.Side,
            decimalPrice = side.DecimalPrice,
            americanPrice = side.AmericanPrice,
            decimalFromAmerican = side.DecimalFromAmerican,
            payoutMultiplier = side.PayoutMultiplier,
            priceConsistencyFlags = side.PriceConsistencyFlags
        };
    }

    private static decimal? TryReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        return property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out var stringValue)
            ? stringValue
            : null;
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static bool IsClose(decimal left, decimal right)
    {
        return Math.Abs(left - right) <= 0.01m;
    }

    private static async Task<object[]> BuildRawPayloadKeySummaryAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> rawPayloadIds,
        CancellationToken cancellationToken)
    {
        if (rawPayloadIds.Count == 0)
        {
            return [];
        }

        var rawPayloads = await dbContext.RawIngestionPayloads
            .AsNoTracking()
            .Where(payload => rawPayloadIds.Contains(payload.Id))
            .Select(payload => new
            {
                payload.Id,
                payload.Endpoint,
                payload.Payload
            })
            .ToListAsync(cancellationToken);

        return rawPayloads
            .Select(payload => new RawPayloadKeyShape(
                payload.Endpoint,
                payload.Payload.RootElement.ValueKind == JsonValueKind.Object
                    ? payload.Payload.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray()
                    : [],
                payload.Id))
            .GroupBy(payload => $"{payload.Endpoint}|{string.Join(",", payload.TopLevelKeys)}")
            .Select(group => new
            {
                endpoint = group.First().Endpoint,
                count = group.Count(),
                topLevelKeys = group.First().TopLevelKeys,
                sampleRawPayloadIds = group.Select(payload => payload.RawPayloadId).Take(5).ToArray()
            })
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildMetadataKeyFrequencies(IEnumerable<MarketRankingRow> rows)
    {
        var keyValues = new Dictionary<string, MetadataKeyAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!row.Metadata.HasValue)
            {
                continue;
            }

            CollectMetadataKeys(row.Metadata.Value, string.Empty, keyValues);
        }

        return keyValues
            .Select(pair => new
            {
                key = pair.Key,
                count = pair.Value.Count,
                exampleValues = pair.Value.ExampleValues.ToArray()
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .Cast<object>()
            .ToArray();
    }

    private static void CollectMetadataKeys(
        JsonElement element,
        string path,
        IDictionary<string, MetadataKeyAccumulator> keyValues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";

            if (!keyValues.TryGetValue(key, out var accumulator))
            {
                accumulator = new MetadataKeyAccumulator();
                keyValues[key] = accumulator;
            }

            accumulator.Count++;
            if (accumulator.ExampleValues.Count < 5 && property.Value.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                accumulator.ExampleValues.Add(property.Value.ToString());
            }

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                CollectMetadataKeys(property.Value, key, keyValues);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var child in property.Value.EnumerateArray())
                {
                    CollectMetadataKeys(child, $"{key}[{index}]", keyValues);
                    index++;
                }
            }
        }
    }

    private static object[] DetectPayoutFields(JsonElement? metadata)
    {
        if (!metadata.HasValue)
        {
            return [];
        }

        var fields = new List<object>();
        CollectPayoutFields(metadata.Value, string.Empty, fields);
        return fields.ToArray();
    }

    private static void CollectPayoutFields(JsonElement element, string path, ICollection<object> fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
            var value = property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? null
                : property.Value.ToString();
            var searchable = $"{key} {value}";

            if (PayoutFieldTerms.Any(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                fields.Add(new
                {
                    key,
                    value
                });
            }

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                CollectPayoutFields(property.Value, key, fields);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var child in property.Value.EnumerateArray())
                {
                    CollectPayoutFields(child, $"{key}[{index}]", fields);
                    index++;
                }
            }
        }
    }

    private static bool HasCompleteEvPricingFields(IReadOnlyCollection<object> payoutFields)
    {
        var serialized = JsonSerializer.Serialize(payoutFields);

        return (serialized.Contains("higher", StringComparison.OrdinalIgnoreCase)
                || serialized.Contains("over", StringComparison.OrdinalIgnoreCase))
            && (serialized.Contains("lower", StringComparison.OrdinalIgnoreCase)
                || serialized.Contains("under", StringComparison.OrdinalIgnoreCase))
            && (serialized.Contains("payoutMultiplier", StringComparison.OrdinalIgnoreCase)
                || serialized.Contains("decimalPrice", StringComparison.OrdinalIgnoreCase)
                || serialized.Contains("americanPrice", StringComparison.OrdinalIgnoreCase));
    }

    private static object? DeserializeMetadata(JsonElement? metadata)
    {
        return metadata.HasValue
            ? JsonSerializer.Deserialize<object>(metadata.Value.GetRawText())
            : null;
    }

    private static decimal Percent(int total, int count)
    {
        return total == 0 ? 0 : Math.Round((decimal)count / total * 100m, 2);
    }

    private static string DetermineUnderdogPayoutConclusion(IReadOnlyCollection<MarketRankingRow> rows)
    {
        if (rows.Count == 0)
        {
            return "No Underdog ranked markets were available in the selected prediction run.";
        }

        var completeEvRows = rows.Count(row => HasCompleteEvPricingFields(DetectPayoutFields(row.Metadata)));

        return completeEvRows > 0
            ? "Some persisted Underdog rows contain option-level price or multiplier-like fields. Validate semantics before treating them as EV inputs."
            : "Persisted Underdog rows contain labels, payoutType, and option availability, but no complete verified option-level payout multiplier or price pair for EV estimation.";
    }

    private sealed class MetadataKeyAccumulator
    {
        public int Count { get; set; }
        public List<string> ExampleValues { get; } = [];
    }

    private sealed record RawPayloadKeyShape(
        string Endpoint,
        string[] TopLevelKeys,
        Guid RawPayloadId);

    private static object ToVisibleMarketFreshnessResponse(MarketRankingRow row)
    {
        return new
        {
            row.Rank,
            player = row.Player,
            platform = row.Sportsbook,
            platformPropId = row.PlatformPropId,
            rawLabel = row.RawSportsbookLabel,
            normalizedPropType = row.NormalizedPropType,
            line = row.SportsbookLine,
            status = row.Status,
            overAvailable = row.OverAvailable,
            underAvailable = row.UnderAvailable,
            observedAt = row.ObservedAt,
            marketClassification = row.MarketClassification,
            payoutType = row.PayoutType,
            probabilityOver = row.ProbabilityOver,
            probabilityUnder = row.ProbabilityUnder,
            probabilityPush = row.ProbabilityPush,
            rankingScore = row.RankingScore,
            recommendation = row.Recommendation,
            isEligibleAsNormalRecommendation = row.IsEligibleAsNormalRecommendation,
            displayWarnings = row.DisplayWarnings,
            ineligibilityReasons = row.IneligibilityReasons,
            lineQualityFlags = row.LineQualityFlags
        };
    }

    private static object BuildPlatformFreshnessSummary(IGrouping<string, MarketRankingRow> group)
    {
        var rowArray = group.ToArray();
        var visibleRows = rowArray.Where(row => row.IsVisibleInResearchDashboard).ToArray();
        var hiddenStaleRows = rowArray.Where(row => row.HiddenReasons.Contains("StaleMarket")).ToArray();
        var latestObservedAt = rowArray
            .Where(row => row.ObservedAt.HasValue)
            .Select(row => row.ObservedAt!.Value)
            .DefaultIfEmpty()
            .Max();
        DateTimeOffset? nullableLatestObservedAt = latestObservedAt == default ? null : latestObservedAt;
        var oldestVisibleObservedAt = visibleRows
            .Where(row => row.ObservedAt.HasValue)
            .Select(row => row.ObservedAt!.Value)
            .DefaultIfEmpty()
            .Min();
        var newestHiddenObservedAt = hiddenStaleRows
            .Where(row => row.ObservedAt.HasValue)
            .Select(row => row.ObservedAt!.Value)
            .DefaultIfEmpty()
            .Max();

        return new
        {
            platform = group.Key,
            totalRankedMarkets = rowArray.Length,
            visibleMarkets = visibleRows.Length,
            hiddenStaleMarkets = hiddenStaleRows.Length,
            latestObservedAt = nullableLatestObservedAt,
            oldestVisibleObservedAt = oldestVisibleObservedAt == default ? (DateTimeOffset?)null : oldestVisibleObservedAt,
            newestHiddenObservedAt = newestHiddenObservedAt == default ? (DateTimeOffset?)null : newestHiddenObservedAt,
            minutesSinceLatestObserved = nullableLatestObservedAt.HasValue
                ? Math.Round((DateTimeOffset.UtcNow - nullableLatestObservedAt.Value.ToUniversalTime()).TotalMinutes, 2)
                : (double?)null,
            isCurrentlyFresh = nullableLatestObservedAt.HasValue && !IsStale(nullableLatestObservedAt.Value),
            activeRows = rowArray.Count(row => IsActiveStatus(row.Status)),
            inactiveRows = rowArray.Count(row => !IsActiveStatus(row.Status)),
            overAvailableCount = rowArray.Count(row => row.OverAvailable == true),
            underAvailableCount = rowArray.Count(row => row.UnderAvailable == true)
        };
    }

    private static object[] CountBy(
        IEnumerable<MarketRankingRow> rows,
        Func<MarketRankingRow, string> keySelector,
        string keyName)
    {
        return rows
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Dictionary<string, object>
            {
                [keyName] = group.Key,
                ["count"] = group.Count()
            })
            .OrderByDescending(row => row["count"])
            .ThenBy(row => row[keyName])
            .Cast<object>()
            .ToArray();
    }

    private static string DeterminePrimaryNoRecommendationReason(
        IReadOnlyCollection<MarketRankingRow> visibleRows,
        IReadOnlyCollection<MarketRankingRow> ineligibleButVisibleRows)
    {
        if (visibleRows.Count == 0)
        {
            return "NoFreshVisibleMarkets";
        }

        if (visibleRows.All(row => !string.Equals(row.MarketClassification, "Standard", StringComparison.OrdinalIgnoreCase)))
        {
            return "NoFreshStandardMarkets";
        }

        var topBlocker = ineligibleButVisibleRows
            .SelectMany(row => row.IneligibilityReasons)
            .GroupBy(reason => reason)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault();

        return topBlocker is null ? "NoBlockerFound" : topBlocker.Key;
    }

    private static object BuildSummary(IEnumerable<MarketClassificationRow> rows)
    {
        var rowArray = rows.ToArray();

        return new
        {
            count = rowArray.Length,
            byClassification = rowArray
                .GroupBy(row => row.MarketClassification)
                .Select(group => new
                {
                    marketClassification = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.marketClassification)
                .ToArray(),
            lineQualityFlags = rowArray
                .SelectMany(row => row.LineQualityFlags)
                .GroupBy(flag => flag)
                .Select(group => new
                {
                    flag = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.flag)
                .ToArray()
        };
    }

    private static (bool IsVisible, string[] Reasons) DetermineVisibility(
        PlatformPropRow? platformProp,
        decimal? line,
        PredictionProbabilityResult prediction,
        string classification)
    {
        var reasons = new List<string>();

        if (platformProp is null)
        {
            reasons.Add("MissingPlatformProp");
        }
        else
        {
            if (string.Equals(platformProp.Platform, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("UnknownPlatform");
            }

            if (string.IsNullOrWhiteSpace(platformProp.Label))
            {
                reasons.Add("MissingRawLabel");
            }

            if (string.IsNullOrWhiteSpace(classification))
            {
                reasons.Add("MissingClassificationData");
            }
        }

        if (!line.HasValue)
        {
            reasons.Add("MissingSportsbookLine");
        }

        if (platformProp is not null && IsClosedOrUnavailable(platformProp))
        {
            reasons.Add("ClosedOrUnavailableMarket");
        }

        if (platformProp is not null && IsStale(platformProp.ObservedAt))
        {
            reasons.Add("StaleMarket");
        }

        if (prediction.PropId == Guid.Empty || prediction.PropTypeId == Guid.Empty || string.IsNullOrWhiteSpace(prediction.PlayerName))
        {
            reasons.Add("CorruptedOrInconsistentPropData");
        }

        return (reasons.Count == 0, reasons.ToArray());
    }

    private static string DetermineHiddenAssessment(IReadOnlyCollection<string> hiddenReasons)
    {
        if (hiddenReasons.Count == 0)
        {
            return "Visible";
        }

        if (hiddenReasons.Any(reason => reason is "StaleMarket"
            or "ClosedOrUnavailableMarket"
            or "MissingSportsbookLine"
            or "CorruptedOrInconsistentPropData"))
        {
            return "CorrectlyHiddenBecauseUnavailableStaleOrBadData";
        }

        if (hiddenReasons.Any(reason => reason is "MissingPlatformProp"
            or "UnknownPlatform"
            or "MissingRawLabel"
            or "MissingClassificationData"))
        {
            return "ImpossibleToDetermineWithCurrentMetadata";
        }

        return "PotentiallyUsefulButHiddenDueToStrictRules";
    }

    private static (bool IsEligible, string[] Reasons) DetermineNormalRecommendationEligibility(
        PlatformPropRow? platformProp,
        decimal? line,
        PredictionProbabilityResult prediction,
        string classification,
        IReadOnlyCollection<string> lineFlags)
    {
        var reasons = new List<string>();

        if (!string.Equals(classification, "Standard", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"{classification}Market");
        }

        if (platformProp is null)
        {
            reasons.Add("MissingPlatformProp");
        }
        else
        {
            if (IsStale(platformProp.ObservedAt))
            {
                reasons.Add("StaleMarket");
            }

            if (IsClosedOrUnavailable(platformProp))
            {
                reasons.Add("ClosedOrUnavailableMarket");
            }

            if (string.IsNullOrWhiteSpace(platformProp.PayoutType)
                || string.Equals(platformProp.PayoutType, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("MissingPayoutContext");
            }
        }

        if (!line.HasValue)
        {
            reasons.Add("MissingSportsbookLine");
        }

        if (!prediction.ProbabilityOver.HasValue
            || !prediction.ProbabilityUnder.HasValue
            || !prediction.ProbabilityPush.HasValue)
        {
            reasons.Add("MissingProbabilityData");
        }

        if (prediction.SampleSize < 25)
        {
            reasons.Add("InsufficientHistoricalSample");
        }

        if (lineFlags.Any(IsSevereLineQualityFlag))
        {
            reasons.Add("SevereAbnormalLine");
        }

        return (reasons.Count == 0, reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string[] BuildDisplayWarnings(string classification, IReadOnlyCollection<string> lineFlags)
    {
        var warnings = new List<string>();

        switch (classification)
        {
            case "Promotional":
                warnings.Add("This appears to be a promotional or boosted market. Payout/EV may differ from standard markets.");
                break;
            case "Demon":
                warnings.Add("This appears to be a Demon market with altered payout/risk. Do not compare directly to standard lines until payout multiplier is captured.");
                break;
            case "Goblin":
                warnings.Add("This appears to be a Goblin market with altered payout/risk. Do not compare directly to standard lines until payout multiplier is captured.");
                break;
            case "Alternate":
                warnings.Add("This appears to be an alternate line. Probability may be valid, but it should be grouped separately from standard markets.");
                break;
            case "Unknown":
                warnings.Add("Market type could not be determined from current metadata.");
                break;
        }

        if (lineFlags.Any(IsSevereLineQualityFlag))
        {
            warnings.Add("Line is far outside normal historical distribution for this prop type.");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsClosedOrUnavailable(PlatformPropRow platformProp)
    {
        return !platformProp.OverAvailable
            && !platformProp.UnderAvailable
            || platformProp.Status.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || platformProp.Status.Contains("suspended", StringComparison.OrdinalIgnoreCase)
            || platformProp.Status.Contains("removed", StringComparison.OrdinalIgnoreCase)
            || platformProp.Status.Contains("inactive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStale(DateTimeOffset observedAt)
    {
        return DateTimeOffset.UtcNow - observedAt.ToUniversalTime() > TimeSpan.FromHours(FreshnessWindowHours);
    }

    private static bool IsActiveStatus(string status)
    {
        return status.Contains("active", StringComparison.OrdinalIgnoreCase)
            || status.Contains("available", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSevereLineQualityFlag(string flag)
    {
        return flag is "LineAboveHistoricalP95"
            or "LineBelowHistoricalP05"
            or "LineFarAboveHistoricalAverage"
            or "LineFarBelowHistoricalAverage"
            or "RareEventLineNotHalf"
            or "HighSinglesLine"
            or "HighHitsLine"
            or "HighRunsLine"
            or "HighTotalBasesLine";
    }

    private static HistoricalDistribution? BuildDistribution(IEnumerable<decimal> values)
    {
        var sortedValues = values
            .Order()
            .ToArray();

        if (sortedValues.Length == 0)
        {
            return null;
        }

        var average = sortedValues.Average();

        return new HistoricalDistribution(
            sortedValues.Length,
            Math.Round(average, 4),
            Percentile(sortedValues, 0.05m),
            Percentile(sortedValues, 0.25m),
            Percentile(sortedValues, 0.50m),
            Percentile(sortedValues, 0.75m),
            Percentile(sortedValues, 0.90m),
            Percentile(sortedValues, 0.95m));
    }

    private static decimal Percentile(decimal[] sortedValues, decimal percentile)
    {
        if (sortedValues.Length == 1)
        {
            return sortedValues[0];
        }

        var position = (sortedValues.Length - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var weight = position - lowerIndex;
        return Math.Round(sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * weight), 4);
    }

    private static string[] AnalyzeLineQuality(
        string propType,
        decimal? line,
        decimal? predictedValue,
        HistoricalDistribution? distribution)
    {
        var flags = new List<string>();

        if (!line.HasValue)
        {
            return ["MissingSportsbookLine"];
        }

        if (distribution is null)
        {
            flags.Add("MissingHistoricalDistribution");
        }
        else
        {
            if (line.Value > distribution.P95)
            {
                flags.Add("LineAboveHistoricalP95");
            }
            else if (line.Value > distribution.P90)
            {
                flags.Add("LineAboveHistoricalP90");
            }

            if (line.Value < distribution.P05)
            {
                flags.Add("LineBelowHistoricalP05");
            }

            if (line.Value > distribution.HistoricalAverage + 1m)
            {
                flags.Add("LineFarAboveHistoricalAverage");
            }
            else if (line.Value < distribution.HistoricalAverage - 1m)
            {
                flags.Add("LineFarBelowHistoricalAverage");
            }
        }

        if (predictedValue.HasValue && Math.Abs(line.Value - predictedValue.Value) >= 1m)
        {
            flags.Add("LineFarFromModelPrediction");
        }

        if (propType is "Home Runs" or "Doubles" or "Stolen Bases" && line.Value != 0.5m)
        {
            flags.Add("RareEventLineNotHalf");
        }

        if (propType == "Singles" && line.Value >= 1.5m)
        {
            flags.Add("HighSinglesLine");
        }

        if (propType == "Hits" && line.Value >= 2.5m)
        {
            flags.Add("HighHitsLine");
        }

        if (propType == "Runs" && line.Value >= 1.5m)
        {
            flags.Add("HighRunsLine");
        }

        if (propType == "Total Bases" && line.Value >= 5.5m)
        {
            flags.Add("HighTotalBasesLine");
        }

        return flags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ClassifyMarket(PlatformPropRow? platformProp, IReadOnlyCollection<string> lineFlags)
    {
        if (platformProp is null)
        {
            return "Unknown";
        }

        var combinedText = Normalize($"{platformProp.PayoutType} {platformProp.Label} {platformProp.Metadata?.GetRawText()}");

        if (combinedText.Contains("DEMON", StringComparison.Ordinal))
        {
            return "Demon";
        }

        if (combinedText.Contains("GOBLIN", StringComparison.Ordinal))
        {
            return "Goblin";
        }

        if (combinedText.Contains("PROMO", StringComparison.Ordinal)
            || combinedText.Contains("BOOST", StringComparison.Ordinal)
            || combinedText.Contains("DISCOUNT", StringComparison.Ordinal)
            || combinedText.Contains("TACO", StringComparison.Ordinal))
        {
            return "Promotional";
        }

        if (combinedText.Contains("SPECIAL", StringComparison.Ordinal))
        {
            return "Special";
        }

        if (combinedText.Contains("ALT", StringComparison.Ordinal)
            || combinedText.Contains("ALTERNATE", StringComparison.Ordinal)
            || lineFlags.Contains("LineAboveHistoricalP95")
            || lineFlags.Contains("RareEventLineNotHalf")
            || lineFlags.Contains("HighSinglesLine")
            || lineFlags.Contains("HighHitsLine")
            || lineFlags.Contains("HighRunsLine")
            || lineFlags.Contains("HighTotalBasesLine"))
        {
            return "Alternate";
        }

        return string.Equals(platformProp.PayoutType, "standard", StringComparison.OrdinalIgnoreCase)
            ? "Standard"
            : "Unknown";
    }

    private static string Normalize(string value)
    {
        return new string(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private sealed record PlatformPropRow(
        Guid PropId,
        string Platform,
        string PlatformSlug,
        string PlatformPropId,
        string Label,
        decimal CurrentLine,
        string PayoutType,
        string Status,
        bool OverAvailable,
        bool UnderAvailable,
        DateTimeOffset ObservedAt,
        Guid? RawPayloadId,
        JsonElement? Metadata);

    private sealed record HistoricalDistribution(
        int SampleSize,
        decimal HistoricalAverage,
        decimal P05,
        decimal P25,
        decimal P50,
        decimal P75,
        decimal P90,
        decimal P95);

    private sealed record MarketClassificationRow(
        int Rank,
        string Sportsbook,
        string Player,
        string PropType,
        decimal? SportsbookLine,
        string RawSportsbookLabel,
        string PlatformPropId,
        string PayoutType,
        string Status,
        bool? OverAvailable,
        bool? UnderAvailable,
        DateTimeOffset? ObservedAt,
        JsonElement? Metadata,
        string MarketClassification,
        string[] LineQualityFlags,
        HistoricalDistribution? HistoricalDistribution,
        decimal? PredictedValue,
        decimal? ProbabilityOver,
        decimal? ProbabilityUnder,
        decimal? ProbabilityPush,
        decimal ConfidenceScore,
        decimal RankingScore,
        int SampleSize,
        string MethodUsed);

    private sealed record MarketRankingRow(
        int Rank,
        string Sportsbook,
        string Player,
        string NormalizedPropType,
        decimal? SportsbookLine,
        string RawSportsbookLabel,
        string PlatformPropId,
        string PayoutType,
        string Status,
        bool? OverAvailable,
        bool? UnderAvailable,
        DateTimeOffset? ObservedAt,
        Guid? RawPayloadId,
        JsonElement? Metadata,
        Guid PlayerId,
        Guid PropTypeId,
        DateOnly EventDate,
        decimal? PredictedValue,
        decimal? ProbabilityOver,
        decimal? ProbabilityUnder,
        decimal? ProbabilityPush,
        decimal RankingScore,
        string Recommendation,
        string MarketClassification,
        string[] LineQualityFlags,
        bool IsVisibleInResearchDashboard,
        bool IsEligibleAsNormalRecommendation,
        string[] DisplayWarnings,
        string[] HiddenReasons,
        string[] IneligibilityReasons,
        string HiddenAssessment);

    private sealed record UnderdogOptionPricing(
        decimal? DecimalPrice,
        string? AmericanPrice,
        decimal? PayoutMultiplier);

    private sealed record UnderdogEvSideRow(
        string Player,
        string PlatformPropId,
        string RawSportsbookLabel,
        string NormalizedPropType,
        decimal? Line,
        string Side,
        decimal? DecimalPrice,
        string? AmericanPrice,
        decimal? DecimalFromAmerican,
        decimal? PayoutMultiplier,
        decimal? WinProbability,
        decimal? LoseProbability,
        decimal? ImpliedProbability,
        decimal? ProbabilityEdge,
        decimal? EstimatedEv,
        decimal RankingScore,
        string[] PriceConsistencyFlags);

    private sealed record UnderdogEvBacktestSideRow(
        string Player,
        string PlatformPropId,
        string RawSportsbookLabel,
        string NormalizedPropType,
        decimal? Line,
        string Side,
        decimal? DecimalPrice,
        string? AmericanPrice,
        decimal? WinProbability,
        decimal? ImpliedProbability,
        decimal? ProbabilityEdge,
        decimal? EstimatedEv,
        decimal RankingScore,
        decimal? ActualResult,
        string? Outcome,
        decimal? ProfitPerOneDollarStake,
        string[] PriceConsistencyFlags,
        string MarketClassification,
        string PayoutType,
        string[] LineQualityFlags,
        DateTimeOffset? ObservedAt);

    private sealed record LineFlagSide(
        string Flag,
        UnderdogEvBacktestSideRow Side);

    private sealed record PositiveEvSubsetSummary(
        string dimension,
        string group,
        int sideCount,
        int gradedCount,
        int wins,
        int losses,
        int pushes,
        decimal hitRate,
        decimal simulatedProfit,
        decimal simulatedRoi,
        decimal averageEstimatedEv,
        decimal averageDecimalPrice,
        decimal averageWinProbability,
        decimal averageImpliedProbability);

    private sealed record PropTypeEvReliabilityRow(
        string propType,
        int totalSides,
        int gradedSides,
        int wins,
        int losses,
        int pushes,
        decimal hitRate,
        decimal simulatedProfit,
        decimal simulatedRoi,
        decimal averageEstimatedEv,
        decimal averageDecimalPrice,
        decimal averageWinProbability,
        decimal averageImpliedProbability,
        int positiveEvSides,
        int positiveEvGradedSides,
        int positiveEvWins,
        int positiveEvLosses,
        int positiveEvPushes,
        decimal positiveEvHitRate,
        decimal positiveEvSimulatedProfit,
        decimal positiveEvSimulatedRoi,
        decimal averagePositiveEstimatedEv,
        decimal averagePositiveDecimalPrice,
        decimal averagePositiveWinProbability,
        decimal averagePositiveImpliedProbability,
        PropTypeSideReliabilityRow[] sideBreakdown,
        string reliabilityClassification);

    private sealed record PropTypeSideReliabilityRow(
        string side,
        int gradedSides,
        int wins,
        int losses,
        int pushes,
        decimal hitRate,
        decimal simulatedProfit,
        decimal simulatedRoi,
        int positiveEvGradedSides,
        int positiveEvWins,
        int positiveEvLosses,
        decimal positiveEvHitRate,
        decimal positiveEvSimulatedProfit,
        decimal positiveEvSimulatedRoi);

    private sealed record PropTypePositiveEvSamples(
        string propType,
        int totalPositiveEvGradedSides,
        object[] topPositiveEvWins,
        object[] topPositiveEvLosses);

    private readonly record struct ActualResultKey(
        Guid PlayerId,
        Guid PropTypeId,
        DateOnly EventDate);

    private sealed record EvBucket(
        string Label,
        decimal? LowerBound,
        decimal? UpperBound,
        bool IncludeLower,
        bool IncludeUpper);
}
