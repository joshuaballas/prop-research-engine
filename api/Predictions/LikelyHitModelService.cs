using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Predictions;

public interface ILikelyHitModelService
{
    LikelyHitModelResult Evaluate(LikelyHitOpportunityInput input);
}

public sealed class LikelyHitModelService(IOptions<TopPicksModelOptions> options) : ILikelyHitModelService
{
    public const string ModelVersion = "LikelyHitV1";
    public const string ProbabilityModelVersion = "WeightedDistributionV1";
    public const string RecommendationContractVersion = "LikelyHitTopPicksV1";

    public LikelyHitModelResult Evaluate(LikelyHitOpportunityInput input)
    {
        var effectiveOptions = options.Value;
        var fatalConflicts = new List<string>();
        var selectedSide = NormalizeSide(input.SelectedSide);

        if (selectedSide is null)
        {
            fatalConflicts.Add("MissingSelectedSide");
        }

        if (!input.Line.HasValue)
        {
            fatalConflicts.Add("MissingLine");
        }

        if (!input.IsLifecycleActive || !input.IsFresh || input.IsStale || input.IsExpired)
        {
            fatalConflicts.Add("InactiveStaleOrExpiredLine");
        }

        if (!input.SupportedPropType)
        {
            fatalConflicts.Add("UnsupportedPropType");
        }

        if (input.RequiresPlayerMapping && !input.HasApprovedPlayerMapping)
        {
            fatalConflicts.Add("PlayerMappingMissing");
        }

        if (input.PriceRequired && !input.HasPrice)
        {
            fatalConflicts.Add("SportsbookPriceMissing");
        }

        var effectiveAmericanOdds = ResolveEffectiveAmericanOdds(input);
        var priceGate = EvaluateSportsbookPriceGate(input, effectiveAmericanOdds, effectiveOptions);
        if (!priceGate.PlayablePriceEligible)
        {
            fatalConflicts.Add(priceGate.FatalConflictCode!);
        }

        if (input.ProjectionProbabilityConflict)
        {
            fatalConflicts.Add("ProjectionProbabilityConflict");
        }

        if (input.RecentWeightedProjectionProbabilityConflict)
        {
            fatalConflicts.Add("RecentWeightedProjectionProbabilityConflict");
        }

        var probability = input.History.Count > 0 && input.Line.HasValue && selectedSide is not null
            ? EvaluateWeightedHistory(input, selectedSide, input.Line.Value)
            : BuildAggregateProbability(input);

        if (!probability.RawWeightedProbability.HasValue)
        {
            fatalConflicts.Add("HitChanceUnavailable");
        }

        if (probability.EffectiveSampleSize < effectiveOptions.MinimumEffectiveSampleSize)
        {
            fatalConflicts.Add(probability.EffectiveSampleSize < 5m
                ? "InsufficientEffectiveSampleBelowFive"
                : "InsufficientEffectiveSampleContextOnly");
        }

        var priorWeight = SelectPriorWeight(probability.EffectiveSampleSize);
        var priorProbability = ClampProbability(input.PriorProbability ?? 0.50m);
        var smoothedProbability = probability.RawWeightedProbability.HasValue
            ? probability.UsesAggregateProbability
                ? probability.RawWeightedProbability.Value
                : DivideOrNull(
                    probability.WeightedHits + priorWeight * priorProbability,
                    probability.WeightedHits + probability.WeightedLosses + priorWeight)
            : null;
        var recentModifier = probability.UsesAggregateProbability || !smoothedProbability.HasValue
            ? 0m
            : CalculateRecentModifier(
                probability.Recent10HitRate,
                probability.Recent5HitRate,
                probability.RawWeightedProbability.GetValueOrDefault(),
                effectiveOptions);
        var preCalibratedProbability = smoothedProbability.HasValue
            ? ClampProbability(smoothedProbability.Value + recentModifier)
            : (decimal?)null;
        var marketCalibrationAdjustment = CalculateMarketCalibrationAdjustment(input, preCalibratedProbability, effectiveOptions);
        var calibratedProbability = preCalibratedProbability.HasValue
            ? ClampProbability(preCalibratedProbability.Value + marketCalibrationAdjustment)
            : (decimal?)null;
        var recentHistoryConflict = HasRecentHistoryConflict(probability.RawWeightedProbability, probability.Recent10HitRate);
        var uncertaintyPenalty = CalculateUncertaintyPenalty(input, probability.EffectiveSampleSize, recentHistoryConflict);
        var hitChance = calibratedProbability.HasValue
            ? ClampProbability(calibratedProbability.Value - uncertaintyPenalty)
            : (decimal?)null;
        var reliabilityScore = CalculateReliabilityScore(input, probability.EffectiveSampleSize, fatalConflicts, recentHistoryConflict);

        if (!hitChance.HasValue)
        {
            fatalConflicts.Add("HitChanceUnavailable");
        }

        var isSportsbook = string.Equals(input.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase);
        var sportsbookMarketGap = CalculateSportsbookMarketGap(input, hitChance, effectiveOptions);
        if (isSportsbook &&
            sportsbookMarketGap.HasValue &&
            sportsbookMarketGap.Value > effectiveOptions.SportsbookBalancedSuspiciousMarketGap)
        {
            fatalConflicts.Add("SportsbookSuspiciousModelMarketGap");
        }

        var sportThreshold = ResolveHitChanceThreshold(input, effectiveOptions);
        var threshold = ResolveMinimumHitChanceThreshold(input, effectiveOptions, sportThreshold);
        var hitChanceGatePassed = hitChance.HasValue && hitChance.Value >= threshold;
        if (isSportsbook && hitChance.HasValue && hitChance.Value < threshold)
        {
            fatalConflicts.Add("BelowBalancedMinimumHitChance");
        }
        var publicTopPickMinimumHitChance = isSportsbook
            ? Math.Max(threshold, effectiveOptions.SportsbookBalancedPublicMinimumHitChance)
            : threshold;
        var publicTopPickHitChanceGatePassed = hitChance.HasValue && hitChance.Value >= publicTopPickMinimumHitChance;
        if (isSportsbook && hitChance.HasValue && hitChance.Value < publicTopPickMinimumHitChance)
        {
            fatalConflicts.Add("BelowSportsbookPublicTopPickHitChance");
        }
        var reliabilityThreshold = ResolveReliabilityThreshold(input, effectiveOptions);
        var valueScore = CalculateValueScore(input);
        var payoutQualityScore = CalculatePayoutQualityScore(input, effectiveAmericanOdds, effectiveOptions);
        var freshnessScore = input.IsLifecycleActive && input.IsFresh && !input.IsStale && !input.IsExpired ? 1m : 0m;
        var volatilityPenalty = IsHighVolatilityProp(input.PropType) ? 0.02m : 0m;
        var hitChanceScore = hitChance.HasValue
            ? isSportsbook
                ? CalculateBalancedHitChanceScore(hitChance.Value, effectiveOptions)
                : NormalizeHitChanceForUtility(hitChance.Value, threshold)
            : (decimal?)null;
        var sweetSpotScore = hitChance.HasValue
            ? isSportsbook
                ? CalculateSweetSpotScore(hitChance.Value, effectiveOptions)
                : NormalizeHitChanceForUtility(hitChance.Value, threshold)
            : (decimal?)null;
        var normalizedHitChance = hitChance.HasValue
            ? NormalizeHitChanceForUtility(hitChance.Value, threshold)
            : (decimal?)null;
        var lineUtilityScore = hitChanceScore.HasValue
            ? isSportsbook
                ? Math.Clamp(
                    Math.Round(
                        (0.45m * hitChanceScore.Value) +
                        (0.30m * payoutQualityScore) +
                        (0.20m * (sweetSpotScore ?? 0m)) +
                        (0.05m * reliabilityScore),
                        6),
                    0m,
                    1m)
                : Math.Clamp(
                    Math.Round((0.60m * normalizedHitChance!.Value) + (0.30m * payoutQualityScore) + (0.10m * reliabilityScore) - volatilityPenalty, 6),
                    0m,
                    1m)
            : (decimal?)null;
        var topPickScore = lineUtilityScore.HasValue
            ? Math.Round(100m * lineUtilityScore.Value, 4)
            : (decimal?)null;
        var canShowAsPick =
            hitChance.HasValue &&
            topPickScore.HasValue &&
            fatalConflicts.Count == 0 &&
            hitChanceGatePassed &&
            publicTopPickHitChanceGatePassed &&
            reliabilityScore >= reliabilityThreshold;
        var balancedLineSelectionReason = BuildBalancedLineSelectionReason(
            isSportsbook,
            canShowAsPick,
            hitChance,
            threshold,
            reliabilityScore,
            reliabilityThreshold,
            fatalConflicts);

        return new LikelyHitModelResult(
            ModelVersion,
            ProbabilityModelVersion,
            RecommendationContractVersion,
            probability.RawWeightedProbability,
            smoothedProbability,
            priorProbability,
            priorWeight,
            recentModifier,
            marketCalibrationAdjustment,
            uncertaintyPenalty,
            hitChance,
            probability.EffectiveSampleSize,
            probability.WeightedHits,
            probability.WeightedLosses,
            probability.WeightedPushes,
            probability.Recent5HitRate,
            probability.Recent10HitRate,
            probability.SeasonHitRate,
            probability.LongHistoryHitRate,
            probability.UsesAggregateProbability,
            reliabilityScore,
            fatalConflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            canShowAsPick,
            canShowAsPick,
            input.CanAddLeg,
            topPickScore,
            threshold,
            isSportsbook ? "SportsbookBalancedMinimumHitChance" : "SportMinimumHitChance",
            threshold,
            hitChanceGatePassed,
            publicTopPickMinimumHitChance,
            publicTopPickHitChanceGatePassed,
            reliabilityThreshold,
            valueScore,
            freshnessScore,
            payoutQualityScore,
            sweetSpotScore,
            hitChanceScore,
            priceGate.Status,
            priceGate.FatalConflictCode,
            priceGate.PlayablePriceEligible,
            canShowAsPick,
            lineUtilityScore,
            isSportsbook ? lineUtilityScore : null,
            effectiveAmericanOdds,
            effectiveOptions.SportsbookBalancedMaxFavoriteAmericanOdds,
            effectiveOptions.SportsbookBalancedMaxUnderdogAmericanOdds,
            effectiveOptions.SportsbookBalancedSuspiciousMarketGap,
            sportsbookMarketGap,
            balancedLineSelectionReason);
    }

    private static WeightedProbabilityResult EvaluateWeightedHistory(
        LikelyHitOpportunityInput input,
        string selectedSide,
        decimal line)
    {
        var ordered = input.History
            .Where(row => row.IsUsable)
            .OrderByDescending(row => row.EventDate)
            .ToArray();
        var weightedHits = 0m;
        var weightedLosses = 0m;
        var weightedPushes = 0m;
        var sumWeights = 0m;
        var sumSquaredWeights = 0m;

        for (var index = 0; index < ordered.Length; index++)
        {
            var row = ordered[index];
            var weight = CalculateObservationWeight(input, row, index);
            sumWeights += weight;
            sumSquaredWeights += weight * weight;

            var outcome = ClassifyOutcome(row.Value, line, selectedSide);
            if (outcome > 0)
            {
                weightedHits += weight;
            }
            else if (outcome < 0)
            {
                weightedLosses += weight;
            }
            else
            {
                weightedPushes += weight;
            }
        }

        var effectiveSampleSize = sumSquaredWeights > 0m
            ? Math.Round(sumWeights * sumWeights / sumSquaredWeights, 4)
            : 0m;
        var rawProbability = DivideOrNull(weightedHits, weightedHits + weightedLosses);

        return new WeightedProbabilityResult(
            rawProbability,
            effectiveSampleSize,
            Math.Round(weightedHits, 4),
            Math.Round(weightedLosses, 4),
            Math.Round(weightedPushes, 4),
            CalculateWindowHitRate(ordered.Take(5), line, selectedSide),
            CalculateWindowHitRate(ordered.Take(10), line, selectedSide),
            CalculateWindowHitRate(ordered.Where(row => row.EventDate.Year == input.EventDate?.Year), line, selectedSide),
            CalculateWindowHitRate(ordered, line, selectedSide),
            UsesAggregateProbability: false);
    }

    private static WeightedProbabilityResult BuildAggregateProbability(LikelyHitOpportunityInput input)
    {
        var sampleSize = Math.Max(0, input.RawSampleSize);
        var probability = input.SelectedSideProbability.HasValue
            ? ClampProbability(input.SelectedSideProbability.Value)
            : (decimal?)null;

        return new WeightedProbabilityResult(
            probability,
            sampleSize,
            probability.HasValue ? Math.Round(sampleSize * probability.Value, 4) : 0m,
            probability.HasValue ? Math.Round(sampleSize * (1m - probability.Value), 4) : 0m,
            0m,
            null,
            null,
            null,
            probability,
            UsesAggregateProbability: true);
    }

    private static decimal CalculateObservationWeight(
        LikelyHitOpportunityInput input,
        LikelyHitObservation row,
        int zeroBasedRecencyIndex)
    {
        var recencyWeight = zeroBasedRecencyIndex < 5
            ? 1.25m
            : zeroBasedRecencyIndex < 10
                ? 1.10m
                : input.EventDate.HasValue && row.EventDate.Year == input.EventDate.Value.Year
                    ? 1.00m
                    : 0.60m;
        var sportWeight = string.Equals(input.Sport, "MLB", StringComparison.OrdinalIgnoreCase)
            ? input.EventDate.HasValue && row.EventDate.Year == input.EventDate.Value.Year ? 1.10m : 0.70m
            : string.Equals(input.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
                ? row.SameCompetition ? 1.15m : 0.75m
                : 1.00m;
        var roleWeight = row.Role?.ToUpperInvariant() switch
        {
            "NORMAL" or "START" or "STARTER" => 1.00m,
            "LIMITED" => 0.50m,
            _ => 0.85m
        };
        var dataQualityWeight = row.DataQualityWeight <= 0m ? 1.00m : row.DataQualityWeight;

        return Math.Max(0m, recencyWeight * sportWeight * roleWeight * dataQualityWeight);
    }

    private static int ClassifyOutcome(decimal value, decimal line, string selectedSide)
    {
        if (value == line)
        {
            return 0;
        }

        if (string.Equals(selectedSide, "Over", StringComparison.OrdinalIgnoreCase))
        {
            return value > line ? 1 : -1;
        }

        return value < line ? 1 : -1;
    }

    private static decimal? CalculateWindowHitRate(IEnumerable<LikelyHitObservation> rows, decimal line, string selectedSide)
    {
        var outcomes = rows
            .Where(row => row.IsUsable)
            .Select(row => ClassifyOutcome(row.Value, line, selectedSide))
            .Where(outcome => outcome != 0)
            .ToArray();

        return outcomes.Length == 0
            ? null
            : Math.Round((decimal)outcomes.Count(outcome => outcome > 0) / outcomes.Length, 4);
    }

    private static int SelectPriorWeight(decimal effectiveSampleSize)
    {
        if (effectiveSampleSize >= 20m)
        {
            return 0;
        }

        if (effectiveSampleSize >= 10m)
        {
            return 2;
        }

        return effectiveSampleSize >= 5m ? 4 : 0;
    }

    private static decimal CalculateRecentModifier(
        decimal? recent10HitRate,
        decimal? recent5HitRate,
        decimal empiricalWinProbability,
        TopPicksModelOptions options)
    {
        var recent10 = recent10HitRate ?? empiricalWinProbability;
        var recent5 = recent5HitRate ?? recent10;
        var modifier = (0.05m * (recent10 - empiricalWinProbability)) + (0.03m * (recent5 - recent10));

        return Math.Clamp(Math.Round(modifier, 6), options.RecentModifierMin, options.RecentModifierMax);
    }

    private static decimal CalculateMarketCalibrationAdjustment(
        LikelyHitOpportunityInput input,
        decimal? preCalibratedProbability,
        TopPicksModelOptions options)
    {
        return 0m;
    }

    private static decimal CalculateUncertaintyPenalty(
        LikelyHitOpportunityInput input,
        decimal effectiveSampleSize,
        bool recentHistoryConflict)
    {
        var penalty = effectiveSampleSize >= 30m
            ? 0.01m
            : effectiveSampleSize >= 20m
                ? 0.02m
                : effectiveSampleSize >= 10m
                    ? 0.035m
                    : effectiveSampleSize >= 5m
                        ? 0.06m
                        : 0.10m;

        if (string.Equals(input.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase))
        {
            penalty += 0.02m;
        }

        if (IsHighVolatilityProp(input.PropType))
        {
            penalty += 0.02m;
        }

        if (recentHistoryConflict)
        {
            penalty += 0.03m;
        }

        if (input.ProjectionProbabilityConflict || input.RecentWeightedProjectionProbabilityConflict)
        {
            penalty += 0.06m;
        }

        return Math.Round(penalty, 6);
    }

    private static decimal CalculateReliabilityScore(
        LikelyHitOpportunityInput input,
        decimal effectiveSampleSize,
        IReadOnlyCollection<string> fatalConflicts,
        bool recentHistoryConflict)
    {
        var sampleQuality = effectiveSampleSize >= 30m
            ? 1.00m
            : effectiveSampleSize >= 20m
                ? 0.90m
                : effectiveSampleSize >= 10m
                    ? 0.75m
                    : effectiveSampleSize >= 5m
                        ? 0.50m
                        : 0.00m;
        var mappingQuality = !input.RequiresPlayerMapping ? 1.00m : input.HasApprovedPlayerMapping ? 1.00m : 0.00m;
        var lifecycleQuality = input.IsLifecycleActive && input.IsFresh && !input.IsStale && !input.IsExpired ? 1.00m : 0.00m;
        var agreementQuality = fatalConflicts.Any(reason => reason.Contains("Conflict", StringComparison.OrdinalIgnoreCase))
            ? 0.00m
            : recentHistoryConflict
                ? 0.75m
                : 1.00m;
        var propTypeStability = IsHighVolatilityProp(input.PropType) ? 0.90m : 1.00m;
        var sportMaturity = string.Equals(input.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase) ? 0.95m : 1.00m;
        var score = sampleQuality * mappingQuality * lifecycleQuality * agreementQuality * propTypeStability * sportMaturity;
        if (score >= 1.00m)
        {
            score = effectiveSampleSize >= 50m && !IsHighVolatilityProp(input.PropType) && !recentHistoryConflict
                ? 0.99m
                : 0.97m;
        }

        return Math.Round(score, 6);
    }

    private static bool HasRecentHistoryConflict(decimal? empiricalWinProbability, decimal? recent10HitRate)
    {
        return empiricalWinProbability.HasValue &&
            recent10HitRate.HasValue &&
            Math.Abs(empiricalWinProbability.Value - recent10HitRate.Value) >= 0.25m;
    }

    private static decimal ResolveHitChanceThreshold(LikelyHitOpportunityInput input, TopPicksModelOptions options)
    {
        return string.Equals(input.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
            ? options.SoccerWorldCupMinimumHitChance
            : options.MlbMinimumHitChance;
    }

    private static decimal ResolveMinimumHitChanceThreshold(
        LikelyHitOpportunityInput input,
        TopPicksModelOptions options,
        decimal sportThreshold)
    {
        return string.Equals(input.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(sportThreshold, options.SportsbookBalancedTargetMinHitChance)
            : sportThreshold;
    }

    private static decimal ResolveReliabilityThreshold(LikelyHitOpportunityInput input, TopPicksModelOptions options)
    {
        return string.Equals(input.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
            ? options.SoccerWorldCupMinimumReliability
            : options.MlbMinimumReliability;
    }

    private static decimal CalculateValueScore(LikelyHitOpportunityInput input)
    {
        if (!string.Equals(input.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase) ||
            !input.DecimalPrice.HasValue ||
            input.DecimalPrice.Value <= 1m)
        {
            return 0m;
        }

        return Math.Clamp(Math.Round((input.DecimalPrice.Value - 1m) / 3m, 6), 0m, 1m);
    }

    private static PriceGateResult EvaluateSportsbookPriceGate(
        LikelyHitOpportunityInput input,
        int? effectiveAmericanOdds,
        TopPicksModelOptions options)
    {
        if (!string.Equals(input.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return new PriceGateResult(true, "NotSportsbook", null);
        }

        if (!effectiveAmericanOdds.HasValue)
        {
            return new PriceGateResult(false, "MissingSportsbookPrice", "SportsbookPriceMissing");
        }

        if (options.SportsbookExtremeFavoriteIneligible &&
            effectiveAmericanOdds.Value < options.SportsbookBalancedMaxFavoriteAmericanOdds)
        {
            return new PriceGateResult(
                false,
                "IneligibleTooExpensiveForBalancedTopPick",
                "SportsbookPriceTooExpensiveForBalancedTopPick");
        }

        if (effectiveAmericanOdds.Value > options.SportsbookBalancedMaxUnderdogAmericanOdds)
        {
            return new PriceGateResult(
                false,
                "IneligibleTooLongshotForBalancedTopPick",
                "SportsbookPriceTooLongshotForBalancedTopPick");
        }

        return new PriceGateResult(true, "EligibleBalancedPrice", null);
    }

    private static decimal CalculatePayoutQualityScore(
        LikelyHitOpportunityInput input,
        int? effectiveAmericanOdds,
        TopPicksModelOptions options)
    {
        if (!string.Equals(input.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return 1.00m;
        }

        if (!effectiveAmericanOdds.HasValue)
        {
            return 0m;
        }

        var odds = effectiveAmericanOdds.Value;
        if (odds < options.SportsbookBalancedMaxFavoriteAmericanOdds ||
            odds > options.SportsbookBalancedMaxUnderdogAmericanOdds)
        {
            return 0m;
        }

        if (odds < -200)
        {
            return Interpolate(odds, options.SportsbookBalancedMaxFavoriteAmericanOdds, -200, 0.25m, 0.45m);
        }

        if (odds < -120)
        {
            return Interpolate(odds, -200, -120, 0.45m, 0.75m);
        }

        if (odds <= 120)
        {
            var distanceFromEven = Math.Abs(odds) / 120m;
            return Math.Round(1.00m - (0.15m * distanceFromEven), 6);
        }

        return Interpolate(odds, 120, options.SportsbookBalancedMaxUnderdogAmericanOdds, 0.85m, 0.50m);
    }

    private static decimal? CalculateSportsbookMarketGap(
        LikelyHitOpportunityInput input,
        decimal? hitChance,
        TopPicksModelOptions options)
    {
        if (!string.Equals(input.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase) ||
            !hitChance.HasValue ||
            !input.ImpliedProbability.HasValue)
        {
            return null;
        }

        var balancedHitChance = Math.Min(hitChance.Value, options.SportsbookBalancedTargetMaxHitChance);
        return Math.Round(balancedHitChance - input.ImpliedProbability.Value, 6);
    }

    private static decimal CalculateBalancedHitChanceScore(decimal hitChance, TopPicksModelOptions options)
    {
        var denominator = Math.Max(0.01m, options.SportsbookBalancedTargetMaxHitChance - options.SportsbookBalancedTargetMinHitChance);
        return Math.Clamp(
            Math.Round((hitChance - options.SportsbookBalancedTargetMinHitChance) / denominator, 6),
            0m,
            1m);
    }

    private static decimal CalculateSweetSpotScore(decimal hitChance, TopPicksModelOptions options)
    {
        var targetMin = options.SportsbookBalancedTargetMinHitChance;
        var targetMax = options.SportsbookBalancedTargetMaxHitChance;
        var mid = (targetMin + targetMax) / 2m;
        var range = Math.Max(0.01m, (targetMax - targetMin) / 2m);

        if (hitChance < targetMin)
        {
            return 0m;
        }

        if (hitChance <= targetMax)
        {
            return Math.Clamp(
                Math.Round(1m - Math.Min(Math.Abs(hitChance - mid) / range, 1m), 6),
                0m,
                1m);
        }

        var aboveTarget = hitChance - targetMax;
        return Math.Clamp(Math.Round(0.40m - (2m * aboveTarget), 6), 0.10m, 0.40m);
    }

    private static string BuildBalancedLineSelectionReason(
        bool isSportsbook,
        bool canShowAsPick,
        decimal? hitChance,
        decimal threshold,
        decimal reliabilityScore,
        decimal reliabilityThreshold,
        IReadOnlyCollection<string> fatalConflicts)
    {
        if (!isSportsbook)
        {
            return canShowAsPick ? "EligibleLikelyHitPick" : "NotSportsbookBalancedLine";
        }

        if (canShowAsPick)
        {
            return "PlayableBalancedLineCandidate";
        }

        if (fatalConflicts.Count > 0)
        {
            return string.Join(",", fatalConflicts.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(row => row, StringComparer.OrdinalIgnoreCase));
        }

        if (!hitChance.HasValue || hitChance.Value < threshold)
        {
            return "BelowHitChanceThreshold";
        }

        if (reliabilityScore < reliabilityThreshold)
        {
            return "BelowReliabilityThreshold";
        }

        return "NotTopPickEligible";
    }

    private static decimal NormalizeHitChanceForUtility(decimal hitChance, decimal threshold)
    {
        var denominator = Math.Max(0.01m, 1m - threshold);
        return Math.Clamp(Math.Round((hitChance - threshold) / denominator, 6), 0m, 1m);
    }

    private static decimal Interpolate(decimal value, decimal minValue, decimal maxValue, decimal minScore, decimal maxScore)
    {
        if (maxValue == minValue)
        {
            return Math.Round(maxScore, 6);
        }

        var ratio = Math.Clamp((value - minValue) / (maxValue - minValue), 0m, 1m);
        return Math.Round(minScore + ((maxScore - minScore) * ratio), 6);
    }

    private static int? ResolveEffectiveAmericanOdds(LikelyHitOpportunityInput input)
    {
        if (input.AmericanPrice.HasValue)
        {
            return input.AmericanPrice.Value;
        }

        if (!input.DecimalPrice.HasValue || input.DecimalPrice.Value <= 1m)
        {
            return null;
        }

        var decimalPrice = input.DecimalPrice.Value;
        return decimalPrice >= 2m
            ? (int)Math.Round((decimalPrice - 1m) * 100m, MidpointRounding.AwayFromZero)
            : (int)Math.Round(-100m / (decimalPrice - 1m), MidpointRounding.AwayFromZero);
    }

    private static bool IsHighVolatilityProp(string? propType)
    {
        return propType is not null &&
            (propType.Contains("Home Run", StringComparison.OrdinalIgnoreCase) ||
             propType.Contains("Total Bases", StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeSide(string? side)
    {
        if (string.Equals(side, "Over", StringComparison.OrdinalIgnoreCase))
        {
            return "Over";
        }

        if (string.Equals(side, "Under", StringComparison.OrdinalIgnoreCase))
        {
            return "Under";
        }

        return null;
    }

    private static decimal? DivideOrNull(decimal numerator, decimal denominator)
    {
        return denominator == 0m ? null : Math.Round(numerator / denominator, 6);
    }

    private static decimal ClampProbability(decimal value)
    {
        return Math.Clamp(Math.Round(value, 6), 0.01m, 0.99m);
    }

    private sealed record WeightedProbabilityResult(
        decimal? RawWeightedProbability,
        decimal EffectiveSampleSize,
        decimal WeightedHits,
        decimal WeightedLosses,
        decimal WeightedPushes,
        decimal? Recent5HitRate,
        decimal? Recent10HitRate,
        decimal? SeasonHitRate,
        decimal? LongHistoryHitRate,
        bool UsesAggregateProbability);

    private sealed record PriceGateResult(
        bool PlayablePriceEligible,
        string Status,
        string? FatalConflictCode);
}

public sealed record LikelyHitOpportunityInput(
    string Sport,
    string? Competition,
    string ProviderCategory,
    string? PlayerName,
    string? PropType,
    string? SelectedSide,
    decimal? Line,
    DateOnly? EventDate,
    bool IsLifecycleActive,
    bool IsFresh,
    bool IsStale,
    bool IsExpired,
    bool HasPrice,
    bool PriceRequired,
    bool SupportedPropType,
    bool RequiresPlayerMapping,
    bool HasApprovedPlayerMapping,
    bool ProjectionProbabilityConflict,
    bool RecentWeightedProjectionProbabilityConflict,
    decimal? SelectedSideProbability,
    decimal? PriorProbability,
    decimal? ImpliedProbability,
    int? AmericanPrice,
    decimal? DecimalPrice,
    int RawSampleSize,
    bool CanAddLeg,
    IReadOnlyList<LikelyHitObservation> History);

public sealed record LikelyHitObservation(
    DateOnly EventDate,
    decimal Value,
    string? Competition = null,
    bool SameCompetition = false,
    string? Role = null,
    decimal DataQualityWeight = 1m,
    bool IsUsable = true);

public sealed record LikelyHitModelResult(
    string ModelVersion,
    string ProbabilityModelVersion,
    string RecommendationContractVersion,
    decimal? RawWeightedProbability,
    decimal? SmoothedProbability,
    decimal PriorProbability,
    int PriorWeight,
    decimal RecentModifier,
    decimal MarketCalibrationAdjustment,
    decimal UncertaintyPenalty,
    decimal? HitChance,
    decimal EffectiveSampleSize,
    decimal WeightedHits,
    decimal WeightedLosses,
    decimal WeightedPushes,
    decimal? Recent5HitRate,
    decimal? Recent10HitRate,
    decimal? SeasonHitRate,
    decimal? LongHistoryHitRate,
    bool UsesAggregateProbability,
    decimal ReliabilityScore,
    IReadOnlyList<string> FatalConflictCodes,
    bool IsTopPick,
    bool CanShowAsPick,
    bool CanAddLeg,
    decimal? TopPickScore,
    decimal HitChanceThreshold,
    string MinimumHitChanceGate,
    decimal MinimumHitChanceRequired,
    bool HitChanceGatePassed,
    decimal PublicTopPickMinimumHitChance,
    bool PublicTopPickHitChanceGatePassed,
    decimal ReliabilityThreshold,
    decimal ValueScore,
    decimal FreshnessScore,
    decimal PayoutQualityScore,
    decimal? SweetSpotScore,
    decimal? HitChanceScore,
    string PriceGateStatus,
    string? PriceGateReason,
    bool PlayablePriceEligible,
    bool TopPickQualityEligible,
    decimal? LineUtilityScore,
    decimal? BalancedLineUtility,
    int? EffectiveAmericanOdds,
    int FavoritePriceCeiling,
    int UnderdogPriceCeiling,
    decimal SuspiciousMarketGap,
    decimal? MarketGap,
    string BalancedLineSelectionReason);
