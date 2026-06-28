using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Endpoints;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Tests;

public sealed class LikelyHitModelServiceTests
{
    [Fact]
    public void WeightedDistribution_FavorsOver_WhenNineOfTenRecentValuesClearLine()
    {
        var service = CreateService();
        var input = CreateInput(
            selectedSide: "Over",
            line: 7.5m,
            historyValues: [9m, 8m, 11m, 10m, 8m, 12m, 8m, 8m, 15m, 7m]);

        var result = service.Evaluate(input);

        Assert.True(result.RawWeightedProbability > 0.85m);
        Assert.True(result.HitChance > 0.75m);
        Assert.DoesNotContain("HitChanceUnavailable", result.FatalConflictCodes);
    }

    [Fact]
    public void WeightedDistribution_FavorsAgainstUnder_WhenNineOfTenRecentValuesClearLine()
    {
        var service = CreateService();
        var input = CreateInput(
            selectedSide: "Under",
            line: 7.5m,
            historyValues: [9m, 8m, 11m, 10m, 8m, 12m, 8m, 8m, 15m, 7m]);

        var result = service.Evaluate(input);

        Assert.True(result.RawWeightedProbability < 0.15m);
        Assert.False(result.CanShowAsPick);
    }

    [Fact]
    public void EffectiveSampleBelowTen_BlocksTopPick()
    {
        var service = CreateService();
        var input = CreateInput(
            selectedSide: "Over",
            line: 0.5m,
            historyValues: [1m, 1m, 1m, 1m, 1m, 1m]);

        var result = service.Evaluate(input);

        Assert.False(result.CanShowAsPick);
        Assert.Contains("InsufficientEffectiveSampleContextOnly", result.FatalConflictCodes);
    }

    [Fact]
    public void PriorCannotCreateTopPickFromThinAggregateSample()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.56m,
            priorProbability: 0.90m,
            sampleSize: 5);

        var result = service.Evaluate(input);

        Assert.False(result.CanShowAsPick);
        Assert.Contains("InsufficientEffectiveSampleContextOnly", result.FatalConflictCodes);
    }

    [Fact]
    public void RecentModifier_IsBoundedAndCanDowngradeRecentConflict()
    {
        var service = CreateService();
        var input = CreateInput(
            selectedSide: "Over",
            line: 0.5m,
            historyValues: [0m, 0m, 0m, 0m, 0m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 1m]);

        var result = service.Evaluate(input);

        Assert.InRange(result.RecentModifier, -0.06m, 0.03m);
        Assert.True(result.RecentModifier < 0m);
    }

    [Fact]
    public void MarketSanity_BlocksExtremeModelGapButDoesNotBoostWeakProbability()
    {
        var service = CreateService();
        var strongGap = CreateAggregateInput(
            selectedSideProbability: 0.65m,
            priorProbability: 0.50m,
            sampleSize: 30,
            impliedProbability: 0.30m);
        var weakGap = CreateAggregateInput(
            selectedSideProbability: 0.54m,
            priorProbability: 0.50m,
            sampleSize: 30,
            impliedProbability: 0.70m);

        var strongResult = service.Evaluate(strongGap);
        var weakResult = service.Evaluate(weakGap);

        Assert.Equal(0m, strongResult.MarketCalibrationAdjustment);
        Assert.Equal(0m, weakResult.MarketCalibrationAdjustment);
        Assert.Contains("SportsbookSuspiciousModelMarketGap", strongResult.FatalConflictCodes);
        Assert.DoesNotContain("SportsbookSuspiciousModelMarketGap", weakResult.FatalConflictCodes);
    }

    [Fact]
    public void LifecycleConflict_BlocksTopPick()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.80m,
            priorProbability: 0.50m,
            sampleSize: 30) with
        {
            IsLifecycleActive = false,
            IsExpired = true
        };

        var result = service.Evaluate(input);

        Assert.False(result.CanShowAsPick);
        Assert.Contains("InactiveStaleOrExpiredLine", result.FatalConflictCodes);
    }

    [Fact]
    public void EligibleSportsbookAggregate_CanBecomeLikelyHitTopPickWithoutPositiveEvGate()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 30,
            americanPrice: -180,
            impliedProbability: 0.68m,
            decimalPrice: 1.47m);

        var result = service.Evaluate(input);

        Assert.True(result.CanShowAsPick);
        Assert.True(result.HitChance >= 0.58m);
        Assert.DoesNotContain("SportsbookPriceMissing", result.FatalConflictCodes);
    }

    [Theory]
    [InlineData(-1400)]
    [InlineData(-900)]
    [InlineData(-500)]
    [InlineData(-400)]
    [InlineData(-301)]
    public void HeavyFavoriteSportsbookGate_BlocksExpensiveBalancedTopPicks(int americanPrice)
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.92m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: americanPrice,
            impliedProbability: 0.80m,
            decimalPrice: 1.10m);

        var result = service.Evaluate(input);

        Assert.False(result.CanShowAsPick);
        Assert.False(result.PlayablePriceEligible);
        Assert.Equal("IneligibleTooExpensiveForBalancedTopPick", result.PriceGateStatus);
        Assert.Contains("SportsbookPriceTooExpensiveForBalancedTopPick", result.FatalConflictCodes);
    }

    [Fact]
    public void FavoriteCeiling_AllowsMinusThreeHundred_WhenOtherBalancedGatesPass()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -300,
            impliedProbability: 0.75m,
            decimalPrice: 1.3333m);

        var result = service.Evaluate(input);

        Assert.True(result.CanShowAsPick);
        Assert.True(result.PlayablePriceEligible);
        Assert.Equal("EligibleBalancedPrice", result.PriceGateStatus);
        Assert.InRange(result.PayoutQualityScore, 0.24m, 0.30m);
    }

    [Fact]
    public void SportsbookBalancedMinimumHitChance_BlocksMlbBelowSixtyPercent()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.60m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -110,
            impliedProbability: 0.55m,
            decimalPrice: 1.9091m);

        var result = service.Evaluate(input);

        Assert.Equal(0.59m, result.HitChance);
        Assert.False(result.CanShowAsPick);
        Assert.False(result.HitChanceGatePassed);
        Assert.Equal("SportsbookBalancedMinimumHitChance", result.MinimumHitChanceGate);
        Assert.Equal(0.60m, result.MinimumHitChanceRequired);
        Assert.Contains("BelowBalancedMinimumHitChance", result.FatalConflictCodes);
    }

    [Fact]
    public void SportsbookPublicMinimumHitChance_BlocksMlbBelowSixtyFivePercent()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.659m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -110,
            impliedProbability: 0.55m,
            decimalPrice: 1.9091m);

        var result = service.Evaluate(input);

        Assert.Equal(0.649m, result.HitChance);
        Assert.False(result.CanShowAsPick);
        Assert.True(result.HitChanceGatePassed);
        Assert.False(result.PublicTopPickHitChanceGatePassed);
        Assert.Equal(0.60m, result.MinimumHitChanceRequired);
        Assert.Equal(0.65m, result.PublicTopPickMinimumHitChance);
        Assert.Contains("BelowSportsbookPublicTopPickHitChance", result.FatalConflictCodes);
    }

    [Fact]
    public void SportsbookPublicMinimumHitChance_AllowsMlbAtSixtyFivePercent()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.66m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -110,
            impliedProbability: 0.55m,
            decimalPrice: 1.9091m);

        var result = service.Evaluate(input);

        Assert.Equal(0.65m, result.HitChance);
        Assert.True(result.CanShowAsPick);
        Assert.True(result.HitChanceGatePassed);
        Assert.True(result.PublicTopPickHitChanceGatePassed);
        Assert.Equal(0.60m, result.MinimumHitChanceRequired);
        Assert.Equal(0.65m, result.PublicTopPickMinimumHitChance);
    }

    [Theory]
    [InlineData(-125, "-125 to -111")]
    [InlineData(-150, "-150 to -126")]
    [InlineData(150, "+126 to +150")]
    [InlineData(250, "+201 to +250")]
    public void BorderlineReplayOddsBuckets_MatchSportsbookAuditBoundaries(int americanPrice, string expectedBucket)
    {
        Assert.Equal(expectedBucket, TheOddsApiDebugEndpoints.BuildLikelyHitBorderlineOddsBucket(americanPrice));
    }

    [Theory]
    [InlineData(0.05, "2.5-5%")]
    [InlineData(0.00, "<=0")]
    [InlineData(-0.01, "<=0")]
    [InlineData(0.075, "5-7.5%")]
    public void BorderlineReplayBreakEvenBuckets_UseHitChanceMinusImpliedProbability(decimal margin, string expectedBucket)
    {
        Assert.Equal(expectedBucket, TheOddsApiDebugEndpoints.BuildLikelyHitBreakEvenMarginBucket(margin));
    }

    [Theory]
    [InlineData(10, "10-19")]
    [InlineData(20, "20-29")]
    [InlineData(30, "30-49")]
    [InlineData(50, "50-99")]
    [InlineData(100, "100+")]
    public void BorderlineReplayEffectiveSampleBuckets_MatchAuditBoundaries(decimal sampleSize, string expectedBucket)
    {
        Assert.Equal(expectedBucket, TheOddsApiDebugEndpoints.BuildLikelyHitBorderlineSampleBucket(sampleSize));
    }

    [Fact]
    public void BorderlineReplayProfileRecommendations_AreDiagnosticAndRuleBased()
    {
        var smallSample = TheOddsApiDebugEndpoints.ResolveLikelyHitBorderlineProfileRecommendation(
            graded: 29,
            hitRate: 0.90m,
            roi: 0.50m,
            averageHitChance: 0.62m,
            out var smallSampleReasons);
        var allow = TheOddsApiDebugEndpoints.ResolveLikelyHitBorderlineProfileRecommendation(
            graded: 30,
            hitRate: 0.62m,
            roi: 0.02m,
            averageHitChance: 0.63m,
            out var allowReasons);
        var block = TheOddsApiDebugEndpoints.ResolveLikelyHitBorderlineProfileRecommendation(
            graded: 30,
            hitRate: 0.49m,
            roi: -0.11m,
            averageHitChance: 0.62m,
            out var blockReasons);
        var penalize = TheOddsApiDebugEndpoints.ResolveLikelyHitBorderlineProfileRecommendation(
            graded: 30,
            hitRate: 0.56m,
            roi: -0.01m,
            averageHitChance: 0.62m,
            out var penalizeReasons);

        Assert.Equal("NeedsMoreData", smallSample);
        Assert.Contains("GradedSampleBelow30", smallSampleReasons);
        Assert.Equal("Allow", allow);
        Assert.Contains("CalibrationAndRoiSupportProfile", allowReasons);
        Assert.Equal("Block", block);
        Assert.Contains("HitRateBelow50Percent", blockReasons);
        Assert.Equal("Penalize", penalize);
        Assert.Contains("UnderperformsExpectedHitChanceByAtLeast5Points", penalizeReasons);
    }

    [Fact]
    public void SportsbookBalancedMinimumHitChance_BlocksSoccerBelowSixtyPercent()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.62m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -110,
            impliedProbability: 0.55m,
            decimalPrice: 1.9091m,
            sport: "SOCCER",
            competition: "World Cup",
            propType: "Shots");

        var result = service.Evaluate(input);

        Assert.Equal(0.59m, result.HitChance);
        Assert.False(result.CanShowAsPick);
        Assert.False(result.HitChanceGatePassed);
        Assert.Contains("BelowBalancedMinimumHitChance", result.FatalConflictCodes);
    }

    [Fact]
    public void SportsbookPublicMinimumHitChance_BlocksSoccerBelowSixtyFivePercent()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.679m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -110,
            impliedProbability: 0.55m,
            decimalPrice: 1.9091m,
            sport: "SOCCER",
            competition: "World Cup",
            propType: "Shots");

        var result = service.Evaluate(input);

        Assert.Equal(0.649m, result.HitChance);
        Assert.False(result.CanShowAsPick);
        Assert.True(result.HitChanceGatePassed);
        Assert.False(result.PublicTopPickHitChanceGatePassed);
        Assert.Equal(0.65m, result.PublicTopPickMinimumHitChance);
        Assert.Contains("BelowSportsbookPublicTopPickHitChance", result.FatalConflictCodes);
    }

    [Fact]
    public void SportsbookPublicMinimumHitChance_AllowsSoccerAtSixtyFivePercent()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.68m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -110,
            impliedProbability: 0.55m,
            decimalPrice: 1.9091m,
            sport: "SOCCER",
            competition: "World Cup",
            propType: "Shots");

        var result = service.Evaluate(input);

        Assert.Equal(0.65m, result.HitChance);
        Assert.True(result.CanShowAsPick);
        Assert.True(result.HitChanceGatePassed);
        Assert.True(result.PublicTopPickHitChanceGatePassed);
        Assert.Equal(0.65m, result.PublicTopPickMinimumHitChance);
    }

    [Fact]
    public void DfsRows_KeepSportMinimumHitChanceThreshold()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.59m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: null,
            impliedProbability: null,
            decimalPrice: null,
            providerCategory: "DFS",
            priceRequired: false,
            canAddLeg: true);

        var result = service.Evaluate(input);

        Assert.Equal(0.58m, result.HitChance);
        Assert.True(result.CanShowAsPick);
        Assert.Equal("SportMinimumHitChance", result.MinimumHitChanceGate);
        Assert.Equal(0.58m, result.MinimumHitChanceRequired);
        Assert.DoesNotContain("BelowBalancedMinimumHitChance", result.FatalConflictCodes);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(251)]
    public void UnderdogCeiling_BlocksLongshotBalancedTopPicks(int americanPrice)
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: americanPrice,
            impliedProbability: 0.55m,
            decimalPrice: americanPrice > 0 ? 1m + (americanPrice / 100m) : null);

        var result = service.Evaluate(input);

        Assert.False(result.CanShowAsPick);
        Assert.False(result.PlayablePriceEligible);
        Assert.Equal("IneligibleTooLongshotForBalancedTopPick", result.PriceGateStatus);
        Assert.Contains("SportsbookPriceTooLongshotForBalancedTopPick", result.FatalConflictCodes);
    }

    [Theory]
    [InlineData(250)]
    [InlineData(100)]
    public void UnderdogCeiling_AllowsPlayableUnderdogPrices_WhenOtherBalancedGatesPass(int americanPrice)
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: americanPrice,
            impliedProbability: 0.60m,
            decimalPrice: 1m + (americanPrice / 100m));

        var result = service.Evaluate(input);

        Assert.True(result.CanShowAsPick);
        Assert.True(result.PlayablePriceEligible);
        Assert.Equal("EligibleBalancedPrice", result.PriceGateStatus);
    }

    [Fact]
    public void ModerateSportsbookPrice_CanRemainEligible_WhenHitChanceAndReliabilityPass()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -110,
            impliedProbability: 0.5238m,
            decimalPrice: 1.9091m);

        var result = service.Evaluate(input);

        Assert.True(result.CanShowAsPick);
        Assert.True(result.PlayablePriceEligible);
        Assert.Equal("EligibleBalancedPrice", result.PriceGateStatus);
        Assert.True(result.PayoutQualityScore >= 0.80m);
    }

    [Fact]
    public void PayoutQualityScore_FavorsBalancedPricesOverCeilingFavorites()
    {
        var service = CreateService();
        var minus300 = service.Evaluate(CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -300,
            impliedProbability: 0.75m,
            decimalPrice: 1.3333m));
        var minus150 = service.Evaluate(CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -150,
            impliedProbability: 0.60m,
            decimalPrice: 1.6667m));
        var minus110 = service.Evaluate(CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -110,
            impliedProbability: 0.5238m,
            decimalPrice: 1.9091m));
        var plus100 = service.Evaluate(CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: 100,
            impliedProbability: 0.60m,
            decimalPrice: 2.00m));

        Assert.True(minus300.PayoutQualityScore < minus150.PayoutQualityScore);
        Assert.True(minus150.PayoutQualityScore < minus110.PayoutQualityScore);
        Assert.True(plus100.PayoutQualityScore > minus150.PayoutQualityScore);
    }

    [Fact]
    public void DfsRows_AreNotAffectedBySportsbookPriceGate()
    {
        var service = CreateService();
        var input = CreateAggregateInput(
            selectedSideProbability: 0.72m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: -1400,
            impliedProbability: null,
            decimalPrice: null,
            providerCategory: "DFS",
            priceRequired: false,
            canAddLeg: true);

        var result = service.Evaluate(input);

        Assert.True(result.CanShowAsPick);
        Assert.True(result.PlayablePriceEligible);
        Assert.Equal("NotSportsbook", result.PriceGateStatus);
    }

    [Fact]
    public void TopPickRows_DedupeAlternateLinesByOpportunityAndChooseBalancedPlayableLine()
    {
        var service = CreateService();
        var rows = new[]
        {
            BuildRow(line: 9.5m, selectedSideProbability: 0.90m, americanPrice: -900),
            BuildRow(line: 8.5m, selectedSideProbability: 0.82m, americanPrice: -450),
            BuildRow(line: 7.5m, selectedSideProbability: 0.68m, americanPrice: -150),
            BuildRow(line: 6.5m, selectedSideProbability: 0.54m, americanPrice: 130)
        };

        var selected = TheOddsApiDebugEndpoints.BuildLikelyHitTopPickRows(
            rows,
            service,
            new TopPicksModelOptions(),
            take: 10);

        var row = Assert.Single(selected);
        Assert.Equal(7.5m, row["line"]!.GetValue<decimal>());
        Assert.Equal(-150, row["bestAmericanPrice"]!.GetValue<int>());
        Assert.True(row["selectedRecommendedLine"]!.GetValue<bool>());
        Assert.True(row["canShowAsPick"]!.GetValue<bool>());
    }

    [Fact]
    public void TopPickRows_SameExactLineChoosesBestAvailableBookPrice()
    {
        var service = CreateService();
        var rows = new[]
        {
            BuildRow(line: 6.5m, selectedSideProbability: 0.68m, americanPrice: -140, bookmaker: "BetMGM"),
            BuildRow(line: 6.5m, selectedSideProbability: 0.68m, americanPrice: -125, bookmaker: "FanDuel"),
            BuildRow(line: 6.5m, selectedSideProbability: 0.68m, americanPrice: -110, bookmaker: "DraftKings")
        };

        var selected = TheOddsApiDebugEndpoints.BuildLikelyHitTopPickRows(
            rows,
            service,
            new TopPicksModelOptions(),
            take: 10);

        var row = Assert.Single(selected);
        Assert.Equal(6.5m, row["line"]!.GetValue<decimal>());
        Assert.Equal(-110, row["bestAmericanPrice"]!.GetValue<int>());
        Assert.Equal("DraftKings", row["bestBookmaker"]!.GetValue<string>());
    }

    [Fact]
    public void TopPickRows_BalancedLadderDoesNotBlindlyChooseHighestHitChance()
    {
        var service = CreateService();
        var rows = new[]
        {
            BuildRow(line: 4.5m, selectedSideProbability: 0.82m, americanPrice: -300),
            BuildRow(line: 5.5m, selectedSideProbability: 0.72m, americanPrice: -180),
            BuildRow(line: 6.5m, selectedSideProbability: 0.61m, americanPrice: -110),
            BuildRow(line: 7.5m, selectedSideProbability: 0.46m, americanPrice: 150)
        };

        var selected = TheOddsApiDebugEndpoints.BuildLikelyHitTopPickRows(
            rows,
            service,
            new TopPicksModelOptions(),
            take: 10);

        var row = Assert.Single(selected);
        Assert.Equal(5.5m, row["line"]!.GetValue<decimal>());
        Assert.Equal(-180, row["bestAmericanPrice"]!.GetValue<int>());
        Assert.True(row["selectedRecommendedLine"]!.GetValue<bool>());
        Assert.Equal("SelectedBestBalancedLineInAlternateLadder", row["balancedLineSelectionReason"]!.GetValue<string>());
    }

    [Fact]
    public void TopPickRows_BalancedUnderLadderSkipsExtremeFavoritesAndLowHitLongshot()
    {
        var service = CreateService();
        var rows = new[]
        {
            BuildRow(line: 9.5m, selectedSideProbability: 0.92m, americanPrice: -900),
            BuildRow(line: 8.5m, selectedSideProbability: 0.84m, americanPrice: -450),
            BuildRow(line: 7.5m, selectedSideProbability: 0.74m, americanPrice: -280),
            BuildRow(line: 6.5m, selectedSideProbability: 0.62m, americanPrice: -130),
            BuildRow(line: 5.5m, selectedSideProbability: 0.48m, americanPrice: 140)
        };

        var selected = TheOddsApiDebugEndpoints.BuildLikelyHitTopPickRows(
            rows,
            service,
            new TopPicksModelOptions(),
            take: 10);

        var row = Assert.Single(selected);
        Assert.Equal(7.5m, row["line"]!.GetValue<decimal>());
        Assert.Equal(-280, row["bestAmericanPrice"]!.GetValue<int>());
    }

    [Fact]
    public void TopPickRows_EmitNoPick_WhenAllAlternateLinesAreTooExpensive()
    {
        var service = CreateService();
        var rows = new[]
        {
            BuildRow(line: 9.5m, selectedSideProbability: 0.90m, americanPrice: -900),
            BuildRow(line: 8.5m, selectedSideProbability: 0.82m, americanPrice: -500)
        };

        var selected = TheOddsApiDebugEndpoints.BuildLikelyHitTopPickRows(
            rows,
            service,
            new TopPicksModelOptions(),
            take: 10);

        Assert.Empty(selected);
    }

    [Fact]
    public void TopPickRows_EmitNoPick_WhenNoLineClearsBalancedHitChanceAndPriceGates()
    {
        var service = CreateService();
        var rows = new[]
        {
            BuildRow(line: 1.5m, selectedSideProbability: 0.94m, americanPrice: -900, propType: "Hits"),
            BuildRow(line: 0.5m, selectedSideProbability: 0.42m, americanPrice: 140, propType: "Hits")
        };

        var selected = TheOddsApiDebugEndpoints.BuildLikelyHitTopPickRows(
            rows,
            service,
            new TopPicksModelOptions(),
            take: 10);

        Assert.Empty(selected);
    }

    [Fact]
    public void SuspiciousMarketDisagreement_BlocksDefaultBalancedTopPick()
    {
        var service = CreateService();
        var suspicious = CreateAggregateInput(
            selectedSideProbability: 0.66m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: 100,
            impliedProbability: 0.30m,
            decimalPrice: 2.00m);
        var acceptable = CreateAggregateInput(
            selectedSideProbability: 0.66m,
            priorProbability: 0.50m,
            sampleSize: 40,
            americanPrice: 100,
            impliedProbability: 0.50m,
            decimalPrice: 2.00m);

        var suspiciousResult = service.Evaluate(suspicious);
        var acceptableResult = service.Evaluate(acceptable);

        Assert.False(suspiciousResult.CanShowAsPick);
        Assert.Contains("SportsbookSuspiciousModelMarketGap", suspiciousResult.FatalConflictCodes);
        Assert.True(acceptableResult.CanShowAsPick);
        Assert.DoesNotContain("SportsbookSuspiciousModelMarketGap", acceptableResult.FatalConflictCodes);
    }

    [Fact]
    public void SoccerSportsbookHeavyFavorite_IsNotBalancedTopPickButAcceptablePriceCanQualify()
    {
        var service = CreateService();
        var rows = new[]
        {
            BuildRow(
                line: 0.5m,
                selectedSideProbability: 0.95m,
                americanPrice: -1115,
                sport: "SOCCER",
                competition: "World Cup",
                propType: "Shots"),
            BuildRow(
                line: 1.5m,
                selectedSideProbability: 0.76m,
                americanPrice: -150,
                sport: "SOCCER",
                competition: "World Cup",
                propType: "Shots")
        };

        var selected = TheOddsApiDebugEndpoints.BuildLikelyHitTopPickRows(
            rows,
            service,
            new TopPicksModelOptions(),
            take: 10);

        var row = Assert.Single(selected);
        Assert.Equal(1.5m, row["line"]!.GetValue<decimal>());
        Assert.Equal(-150, row["bestAmericanPrice"]!.GetValue<int>());
        Assert.False(row["entryActionableForUser"]!.GetValue<bool>());
    }

    [Fact]
    public void PublicTopPickQuality_IsCuratedAndDoesNotReturnHeavyFavoriteFlood()
    {
        var service = CreateService();
        var heavyFavorites = Enumerable.Range(0, 100)
            .Select(index => BuildRow(
                line: 1.5m,
                selectedSideProbability: 0.92m,
                americanPrice: -900,
                player: $"Heavy Favorite {index}",
                eventId: $"event-{index}"));
        var playable = new[]
        {
            BuildRow(line: 0.5m, selectedSideProbability: 0.72m, americanPrice: -110, player: "Playable One", eventId: "playable-1"),
            BuildRow(line: 1.5m, selectedSideProbability: 0.69m, americanPrice: 120, player: "Playable Two", eventId: "playable-2")
        };

        var selected = TheOddsApiDebugEndpoints.BuildLikelyHitTopPickRows(
            heavyFavorites.Concat(playable),
            service,
            new TopPicksModelOptions(),
            take: 500);

        Assert.Equal(2, selected.Length);
        Assert.All(selected, row => Assert.InRange(row["bestAmericanPrice"]!.GetValue<int>(), -300, 250));
    }

    private static LikelyHitModelService CreateService() =>
        new(Options.Create(new TopPicksModelOptions()));

    private static LikelyHitOpportunityInput CreateInput(
        string selectedSide,
        decimal line,
        decimal[] historyValues)
    {
        var eventDate = new DateOnly(2026, 6, 27);
        var observations = historyValues
            .Select((value, index) => new LikelyHitObservation(
                eventDate.AddDays(-(index + 1)),
                value,
                Competition: "MLB",
                SameCompetition: true,
                Role: "Normal"))
            .ToArray();

        return new LikelyHitOpportunityInput(
            "MLB",
            "MLB",
            "DFS",
            "Test Player",
            "Pitcher Strikeouts",
            selectedSide,
            line,
            eventDate,
            IsLifecycleActive: true,
            IsFresh: true,
            IsStale: false,
            IsExpired: false,
            HasPrice: true,
            PriceRequired: false,
            SupportedPropType: true,
            RequiresPlayerMapping: true,
            HasApprovedPlayerMapping: true,
            ProjectionProbabilityConflict: false,
            RecentWeightedProjectionProbabilityConflict: false,
            SelectedSideProbability: null,
            PriorProbability: 0.50m,
            ImpliedProbability: null,
            AmericanPrice: null,
            DecimalPrice: null,
            RawSampleSize: historyValues.Length,
            CanAddLeg: true,
            observations);
    }

    private static LikelyHitOpportunityInput CreateAggregateInput(
        decimal selectedSideProbability,
        decimal priorProbability,
        int sampleSize,
        int? americanPrice = -110,
        decimal? impliedProbability = 0.55m,
        decimal? decimalPrice = 1.80m,
        string providerCategory = "Sportsbook",
        bool priceRequired = true,
        bool canAddLeg = false,
        string sport = "MLB",
        string competition = "MLB",
        string propType = "Pitcher Strikeouts")
    {
        return new LikelyHitOpportunityInput(
            sport,
            competition,
            providerCategory,
            "Test Player",
            propType,
            "Over",
            5.5m,
            new DateOnly(2026, 6, 27),
            IsLifecycleActive: true,
            IsFresh: true,
            IsStale: false,
            IsExpired: false,
            HasPrice: americanPrice.HasValue || decimalPrice.HasValue || impliedProbability.HasValue,
            PriceRequired: priceRequired,
            SupportedPropType: true,
            RequiresPlayerMapping: true,
            HasApprovedPlayerMapping: true,
            ProjectionProbabilityConflict: false,
            RecentWeightedProjectionProbabilityConflict: false,
            selectedSideProbability,
            priorProbability,
            impliedProbability,
            americanPrice,
            decimalPrice,
            sampleSize,
            canAddLeg,
            Array.Empty<LikelyHitObservation>());
    }

    private static JsonObject BuildRow(
        decimal line,
        decimal selectedSideProbability,
        int americanPrice = -140,
        string sport = "MLB",
        string competition = "MLB",
        string propType = "Pitcher Strikeouts",
        string player = "Test Player",
        string eventId = "event-1",
        string bookmaker = "FanDuel")
    {
        var decimalPrice = americanPrice > 0
            ? 1m + (americanPrice / 100m)
            : 1m + (100m / Math.Abs(americanPrice));
        var impliedProbability = americanPrice > 0
            ? 100m / (americanPrice + 100m)
            : Math.Abs(americanPrice) / (Math.Abs(americanPrice) + 100m);

        return new JsonObject
        {
            ["sport"] = sport,
            ["competition"] = competition,
            ["eventId"] = eventId,
            ["playerId"] = "player-1",
            ["canonicalPlayerId"] = "player-1",
            ["player"] = player,
            ["normalizedPropType"] = propType,
            ["bestSide"] = "Over",
            ["line"] = line,
            ["providerCategory"] = "Sportsbook",
            ["lifecycleStatus"] = "Active",
            ["isFresh"] = true,
            ["isStale"] = false,
            ["isExpired"] = false,
            ["bestBookmaker"] = bookmaker,
            ["bestAmericanPrice"] = americanPrice,
            ["bestDecimalPrice"] = Math.Round(decimalPrice, 6),
            ["bestImpliedProbability"] = Math.Round(impliedProbability, 6),
            ["historicalSampleSize"] = 30,
            ["rawSelectedSideProbability"] = selectedSideProbability,
            ["selectedSideProbability"] = selectedSideProbability,
            ["entryActionableForUser"] = false,
            ["projectionProbabilityConflict"] = false,
            ["recentWeightedProjectionProbabilityConflict"] = false
        };
    }
}
