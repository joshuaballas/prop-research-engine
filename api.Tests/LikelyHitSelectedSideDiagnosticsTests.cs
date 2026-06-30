using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBetting.Api.Endpoints;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Tests;

public sealed class LikelyHitSelectedSideDiagnosticsTests
{
    [Fact]
    public async Task SelectedSideDiagnostics_ExposeTopPickAndPerformanceSelectedSideReasons()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedSelectedSideDiagnosticScenarioAsync(factory);

        var response = await client.GetFromJsonAsync<JsonElement>(
            "/api/debug/model/likely-hit/selected-side-diagnostics?sport=MLB&providerCategory=Sportsbook&includePerformance=true&take=10");

        Assert.True(response.GetProperty("readOnly").GetBoolean());
        Assert.False(response.GetProperty("callsExternalApis").GetBoolean());

        var topPicks = response.GetProperty("topPicks").GetProperty("rows").EnumerateArray().ToArray();
        var topPick = Assert.Single(topPicks);
        Assert.Equal("Pitcher Strikeouts", topPick.GetProperty("propType").GetString());
        Assert.Equal("Under", topPick.GetProperty("bestSide").GetString());
        Assert.Equal(0.33m, topPick.GetProperty("modelOverProbability").GetDecimal());
        Assert.Equal(0.67m, topPick.GetProperty("modelUnderProbability").GetDecimal());
        Assert.Equal("SelectedHigherModelUnderProbability", topPick.GetProperty("chosenSideReason").GetString());
        Assert.Equal("LowerModelProbabilityThanSelectedUnder", topPick.GetProperty("overRejectedReason").GetString());
        Assert.False(topPick.GetProperty("selectedSideProbabilityMismatch").GetBoolean());

        var summary = response.GetProperty("topPicks").GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("selectedSideProbabilityMismatchCount").GetInt32());
        Assert.Contains(
            summary.GetProperty("rejectedOverCandidateReasons").EnumerateArray(),
            reason => reason.GetProperty("reason").GetString() == "LowerModelProbabilityThanSelectedUnder"
                && reason.GetProperty("count").GetInt32() == 1);

        var performanceRows = response.GetProperty("performance").GetProperty("rows").EnumerateArray().ToArray();
        var performance = Assert.Single(performanceRows);
        Assert.Equal("performanceLedger", performance.GetProperty("source").GetString());
        Assert.Equal("Under", performance.GetProperty("bestSide").GetString());
        Assert.Equal(0.67m, performance.GetProperty("selectedSideProbability").GetDecimal());
        Assert.Equal("matchedTopPickSnapshot", performance.GetProperty("sideSpecificDiagnosticsSource").GetString());
    }

    [Fact]
    public async Task PublicTopPicks_PropTypeFallsBackFromNormalizedPropType()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedSelectedSideDiagnosticScenarioAsync(factory);

        var response = await client.GetFromJsonAsync<JsonElement>(
            "/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=10&freshnessMinutes=30");

        var topPicks = response.GetProperty("topPicks").EnumerateArray().ToArray();
        var topPick = Assert.Single(topPicks);
        Assert.Equal("Pitcher Strikeouts", topPick.GetProperty("propType").GetString());
        Assert.Equal("Pitcher Strikeouts", topPick.GetProperty("normalizedPropType").GetString());
        Assert.Equal("PitchingStrikeouts", topPick.GetProperty("propFamily").GetString());
    }

    [Fact]
    public async Task PublicTopPicks_DedupesProviderFanoutAndPreservesProviderOptions()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedProviderFanoutTopPickScenarioAsync(factory);

        var response = await client.GetFromJsonAsync<JsonElement>(
            "/api/markets/top-picks?sport=MLB&providerCategory=Sportsbook&take=500&freshnessMinutes=30");

        var topPicks = response.GetProperty("topPicks").EnumerateArray().ToArray();
        var pick = Assert.Single(topPicks);
        Assert.Equal("BetMGM", pick.GetProperty("bestBookmaker").GetString());
        Assert.Equal("BetMGM", pick.GetProperty("bestPriceBookmaker").GetString());
        Assert.Equal(3, pick.GetProperty("providerFanoutCount").GetInt32());
        Assert.Equal(2, pick.GetProperty("alternateProviderCount").GetInt32());
        Assert.Equal("BattingContactPower", pick.GetProperty("propFamily").GetString());
        var providerOptions = pick.GetProperty("providerOptions").EnumerateArray().ToArray();
        Assert.Equal(3, providerOptions.Length);
        Assert.Contains(providerOptions, option => option.GetProperty("bestPriceBookmaker").GetString() == "FanDuel");
        Assert.Contains(providerOptions, option => option.GetProperty("bestPriceBookmaker").GetString() == "BetMGM");
        Assert.Contains(providerOptions, option => option.GetProperty("bestPriceBookmaker").GetString() == "DraftKings");

        var summary = response.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("preDedupeCandidateCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("postOpportunityDedupeCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("returnedTopPicks").GetInt32());
        Assert.Equal(2, summary.GetProperty("suppressedProviderFanoutCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("providerFanoutOpportunityCount").GetInt32());
        Assert.Equal(3, summary.GetProperty("maxProviderFanoutCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("rowsWithProviderOptionsCount").GetInt32());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);

        var lineShopping = await client.GetFromJsonAsync<JsonElement>(
            "/api/markets/line-shopping?sport=MLB&providerCategory=Sportsbook&take=500&freshnessMinutes=30&topOnly=false");
        var comparisonGroups = lineShopping.GetProperty("comparisonGroups").EnumerateArray().ToArray();
        Assert.Equal(3, comparisonGroups.Length);

        var allResponse = await client.GetFromJsonAsync<JsonElement>(
            "/api/markets/top-picks?sport=All&providerCategory=Sportsbook&take=500&freshnessMinutes=30");
        var allTopPicks = allResponse.GetProperty("topPicks").EnumerateArray().ToArray();
        var allPick = Assert.Single(allTopPicks);
        Assert.Equal(3, allPick.GetProperty("providerFanoutCount").GetInt32());
        Assert.Equal(2, allPick.GetProperty("alternateProviderCount").GetInt32());
        Assert.Equal("BattingContactPower", allPick.GetProperty("propFamily").GetString());
        Assert.Equal(3, allPick.GetProperty("providerOptions").EnumerateArray().Count());

        var allSummary = allResponse.GetProperty("summary");
        Assert.Equal(1, allSummary.GetProperty("providerFanoutOpportunityCount").GetInt32());
        Assert.Equal(3, allSummary.GetProperty("maxProviderFanoutCount").GetInt32());
        Assert.Equal(1, allSummary.GetProperty("rowsWithProviderOptionsCount").GetInt32());

        var audit = await client.GetFromJsonAsync<JsonElement>(
            "/api/debug/model/likely-hit/value-audit?sport=MLB&providerCategory=Sportsbook&take=500");
        var auditFanout = audit
            .GetProperty("currentTopPicks")
            .GetProperty("curationComparison")
            .GetProperty("finalPublicBoardFanoutDiagnostics");
        Assert.Equal("FinalPublicBoardReturnedRows", auditFanout.GetProperty("scope").GetString());
        Assert.True(auditFanout.GetProperty("usesSharedPublicTopPicksResponse").GetBoolean());
        Assert.Equal(allSummary.GetProperty("providerFanoutOpportunityCount").GetInt32(), auditFanout.GetProperty("providerFanoutOpportunityCount").GetInt32());
        Assert.Equal(allSummary.GetProperty("maxProviderFanoutCount").GetInt32(), auditFanout.GetProperty("maxProviderFanoutCount").GetInt32());
        Assert.Equal(allSummary.GetProperty("rowsWithProviderOptionsCount").GetInt32(), auditFanout.GetProperty("rowsWithProviderOptionsCount").GetInt32());
        Assert.Equal(BuildPublicTopPickFingerprint(allTopPicks), auditFanout.GetProperty("publicBoardFingerprint").GetString());

        var preCapAuditFanout = audit
            .GetProperty("currentTopPicks")
            .GetProperty("curationComparison")
            .GetProperty("preCapProviderFanoutDiagnostics");
        Assert.Equal("BroadPostOpportunityDedupePreExposureCaps", preCapAuditFanout.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task ValueAudit_FinalPublicBoardFanoutExcludesRowsFilteredOutByPublicAllTopPicks()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedProviderFanoutTopPickScenarioAsync(factory, includeNonPublicHighFanout: true);

        var allResponse = await client.GetFromJsonAsync<JsonElement>(
            "/api/markets/top-picks?sport=All&providerCategory=Sportsbook&take=500&freshnessMinutes=30");
        var allSummary = allResponse.GetProperty("summary");
        var publicTopPicks = allResponse.GetProperty("topPicks").EnumerateArray().ToArray();
        Assert.Single(publicTopPicks);
        Assert.DoesNotContain(publicTopPicks, row => row.GetProperty("player").GetString() == "Hidden Fanout Hitter");

        var audit = await client.GetFromJsonAsync<JsonElement>(
            "/api/debug/model/likely-hit/value-audit?sport=All&providerCategory=Sportsbook&take=500");
        var curation = audit
            .GetProperty("currentTopPicks")
            .GetProperty("curationComparison");
        var finalPublicFanout = curation.GetProperty("finalPublicBoardFanoutDiagnostics");
        Assert.Equal("FinalPublicBoardReturnedRows", finalPublicFanout.GetProperty("scope").GetString());
        Assert.True(finalPublicFanout.GetProperty("usesSharedPublicTopPicksResponse").GetBoolean());
        Assert.Equal(allSummary.GetProperty("providerFanoutOpportunityCount").GetInt32(), finalPublicFanout.GetProperty("providerFanoutOpportunityCount").GetInt32());
        Assert.Equal(allSummary.GetProperty("maxProviderFanoutCount").GetInt32(), finalPublicFanout.GetProperty("maxProviderFanoutCount").GetInt32());
        Assert.Equal(allSummary.GetProperty("rowsWithProviderOptionsCount").GetInt32(), finalPublicFanout.GetProperty("rowsWithProviderOptionsCount").GetInt32());
        Assert.Equal(BuildPublicTopPickFingerprint(publicTopPicks), finalPublicFanout.GetProperty("publicBoardFingerprint").GetString());

        var preCapFanout = curation.GetProperty("preCapProviderFanoutDiagnostics");
        Assert.Equal("BroadPostOpportunityDedupePreExposureCaps", preCapFanout.GetProperty("scope").GetString());
        Assert.True(preCapFanout.GetProperty("providerFanoutOpportunityCount").GetInt32() > finalPublicFanout.GetProperty("providerFanoutOpportunityCount").GetInt32());
    }

    [Fact]
    public void PublicTopPickCuration_AppliesPlayerFamilyAndGameExposureCaps()
    {
        var rows = new List<JsonObject>
        {
            BuildTopPickJson("MLB", "event-family-a", "Repeated Player", "Total Bases", "Over", 1.5m, "FanDuel", 0.24m, 0.90m),
            BuildTopPickJson("MLB", "event-family-b", "Repeated Player", "Hits", "Over", 0.5m, "BetMGM", 0.23m, 0.89m),
            BuildTopPickJson("MLB", "event-player-a", "Player Cap", "Pitcher Strikeouts", "Under", 6.5m, "FanDuel", 0.22m, 0.88m),
            BuildTopPickJson("MLB", "event-player-b", "Player Cap", "Runs", "Under", 0.5m, "FanDuel", 0.21m, 0.87m),
            BuildTopPickJson("MLB", "event-player-c", "Player Cap", "Walks", "Under", 0.5m, "FanDuel", 0.20m, 0.86m),
            BuildTopPickJson("MLB", "event-player-d", "Player Cap", "Pitcher Outs", "Under", 17.5m, "FanDuel", 0.19m, 0.85m)
        };

        for (var i = 0; i < 12; i++)
        {
            rows.Add(BuildTopPickJson("MLB", "event-game-cap", $"Game Player {i:00}", "Pitcher Strikeouts", "Under", 5.5m, "FanDuel", 0.18m - (i * 0.001m), 0.84m - (i * 0.001m)));
        }

        var result = TheOddsApiDebugEndpoints.CuratePublicTopPickRows(
            rows,
            requestedTake: 500,
            providerCategory: "Sportsbook",
            new TopPicksModelOptions());

        Assert.Equal(rows.Count, result.Diagnostics.PreDedupeCandidateCount);
        Assert.Equal(rows.Count, result.Diagnostics.PostOpportunityDedupeCount);
        Assert.Equal(14, result.Diagnostics.ReturnedTopPickCount);
        Assert.Equal(1, result.Diagnostics.SuppressedByPlayerPropFamilyExposureCount);
        Assert.Equal(1, result.Diagnostics.SuppressedByPlayerExposureCount);
        Assert.Equal(2, result.Diagnostics.SuppressedByGameExposureCount);
        Assert.DoesNotContain(result.Rows, row =>
            string.Equals(row["player"]?.GetValue<string>(), "Repeated Player", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row["normalizedPropType"]?.GetValue<string>(), "Hits", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublicTopPickCuration_UsesResolvedNonBlankPropFamiliesForPlayerFamilyCaps()
    {
        var rows = new List<JsonObject>
        {
            BuildTopPickJson("MLB", "event-family-1", "Family Test Player", "Total Bases", "Over", 1.5m, "FanDuel", 0.30m, 0.95m),
            BuildTopPickJson("MLB", "event-family-2", "Family Test Player", "Hits", "Over", 0.5m, "BetMGM", 0.29m, 0.94m),
            BuildTopPickJson("MLB", "event-family-3", "Family Test Player", "Runs", "Over", 0.5m, "DraftKings", 0.28m, 0.93m),
            BuildTopPickJson("MLB", "event-family-4", "Family Test Player", "Walks", "Over", 0.5m, "FanDuel", 0.27m, 0.92m)
        };

        var result = TheOddsApiDebugEndpoints.CuratePublicTopPickRows(
            rows,
            requestedTake: 500,
            providerCategory: "Sportsbook",
            new TopPicksModelOptions
            {
                PublicMaxTopPicksPerPlayer = 3,
                PublicMaxTopPicksPerPlayerPropFamily = 1
            });

        Assert.Equal(4, result.Diagnostics.PreDedupeCandidateCount);
        Assert.Equal(4, result.Diagnostics.PostOpportunityDedupeCount);
        Assert.Equal(3, result.Diagnostics.ReturnedTopPickCount);
        Assert.Equal(1, result.Diagnostics.SuppressedByPlayerPropFamilyExposureCount);
        Assert.Equal(0, result.Diagnostics.SuppressedByPlayerExposureCount);
        Assert.All(result.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row["propFamily"]?.GetValue<string>())));

        var selectedFamilies = result.Rows
            .Select(row => row["propFamily"]?.GetValue<string>())
            .ToArray();
        Assert.Contains("BattingContactPower", selectedFamilies);
        Assert.Contains("BattingRunProduction", selectedFamilies);
        Assert.Contains("BattingDiscipline", selectedFamilies);
        Assert.DoesNotContain(result.Rows, row =>
            string.Equals(row["normalizedPropType"]?.GetValue<string>(), "Hits", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValueAudit_ExposesPriceQualityAndSeparatesFlatFromPriceAdjustedRoi()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedValueAuditScenarioAsync(factory);

        var response = await client.GetFromJsonAsync<JsonElement>(
            "/api/debug/model/likely-hit/value-audit?sport=MLB&providerCategory=Sportsbook&take=10");

        Assert.True(response.GetProperty("readOnly").GetBoolean());
        Assert.False(response.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);

        var currentRows = response.GetProperty("currentTopPicks").GetProperty("rows").EnumerateArray().ToArray();
        var current = Assert.Single(currentRows);
        Assert.Equal("Total Bases", current.GetProperty("propType").GetString());
        Assert.Equal("1.5", current.GetProperty("lineBucket").GetString());
        Assert.Equal(-333, current.GetProperty("bestAmericanPrice").GetInt32());
        Assert.Equal(1.3m, current.GetProperty("bestDecimalPrice").GetDecimal());
        Assert.Equal("FanDuel", current.GetProperty("bestPriceBookmaker").GetString());
        Assert.Equal(-0.09m, current.GetProperty("estimatedEv").GetDecimal());
        Assert.Equal("HighProbabilityNonPositiveEv", current.GetProperty("valueRiskFlag").GetString());
        Assert.Equal("SelectedHigherModelUnderProbability", current.GetProperty("chosenSideReason").GetString());

        var currentMetrics = response.GetProperty("currentTopPicks").GetProperty("summary").GetProperty("metrics");
        Assert.Equal(currentRows.Length, currentMetrics.GetProperty("candidateCount").GetInt32());
        Assert.Equal(1, currentMetrics.GetProperty("highHitProbabilityNonPositiveEvCount").GetInt32());

        var highProbabilityNonPositive = response
            .GetProperty("currentTopPicks")
            .GetProperty("diagnosticLists")
            .GetProperty("highProbabilityButNonPositiveEvPicks")
            .EnumerateArray()
            .ToArray();
        Assert.Single(highProbabilityNonPositive);

        var historicalRows = response.GetProperty("historicalGraded").GetProperty("rows").EnumerateArray().ToArray();
        var historical = Assert.Single(historicalRows);
        Assert.Equal("Won", historical.GetProperty("result").GetString());
        Assert.Equal(0.3m, historical.GetProperty("realizedProfitAtLoggedPrice").GetDecimal());
        Assert.Equal(0.3m, historical.GetProperty("realizedRoiAtLoggedPrice").GetDecimal());
        Assert.Equal(1m, historical.GetProperty("flatUnitProfit").GetDecimal());
        Assert.Equal(1m, historical.GetProperty("flatUnitRoi").GetDecimal());
        Assert.Equal("LoggedDecimalPrice", historical.GetProperty("priceStatus").GetString());

        var historicalMetrics = response.GetProperty("historicalGraded").GetProperty("summary").GetProperty("metrics");
        Assert.Equal(historicalRows.Length, historicalMetrics.GetProperty("candidateCount").GetInt32());
        Assert.Equal(1, historicalMetrics.GetProperty("gradedCount").GetInt32());
        Assert.Equal(0.3m, historicalMetrics.GetProperty("priceAdjustedRoi").GetDecimal());
        Assert.Equal(1m, historicalMetrics.GetProperty("flatRoi").GetDecimal());
    }

    [Fact]
    public async Task ValueAudit_ReportsVolumeSelectivityCutsAndWarningFlags()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await SeedHighVolumeValueAuditScenarioAsync(factory);

        var response = await client.GetFromJsonAsync<JsonElement>(
            "/api/debug/model/likely-hit/value-audit?sport=MLB&providerCategory=Sportsbook&take=150");

        Assert.True(response.GetProperty("readOnly").GetBoolean());
        Assert.False(response.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);

        var current = response.GetProperty("currentTopPicks");
        var rows = current.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(105, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal("likelyHit", row.GetProperty("internalDiagnosticTier").GetString());
            Assert.True(row.GetProperty("rank").GetInt32() >= 1);
            Assert.False(row.GetProperty("stricterHypotheticalThresholds").GetProperty("premiumTopPickProfile").GetBoolean());
        });

        var volume = current.GetProperty("volumeDiagnostics");
        Assert.Equal(105, volume.GetProperty("totalCurrentRows").GetInt32());
        Assert.Equal(105, volume.GetProperty("totalCurrentTopPicksBySport")[0].GetProperty("count").GetInt32());
        Assert.Equal(105, volume.GetProperty("picksPerPlayer")[0].GetProperty("count").GetInt32());
        Assert.Equal(105, volume.GetProperty("picksPerGameEvent")[0].GetProperty("count").GetInt32());
        Assert.Equal(105, volume.GetProperty("duplicateExposureByPlayerPropType")[0].GetProperty("count").GetInt32());
        Assert.Equal(105, volume.GetProperty("duplicateExposureByPlayerPropFamily")[0].GetProperty("count").GetInt32());

        var selectivity = current.GetProperty("selectivityDiagnostics");
        Assert.Equal(105, selectivity.GetProperty("boardSize").GetInt32());
        var cuts = selectivity.GetProperty("totalRowsByCut").EnumerateArray().ToArray();
        Assert.Contains(cuts, cut => cut.GetProperty("cut").GetString() == "Top 10" && cut.GetProperty("candidateCount").GetInt32() == 10);
        Assert.Contains(cuts, cut => cut.GetProperty("cut").GetString() == "Top 25" && cut.GetProperty("candidateCount").GetInt32() == 25);
        Assert.Contains(cuts, cut => cut.GetProperty("cut").GetString() == "Top 5%" && cut.GetProperty("candidateCount").GetInt32() == 6);
        Assert.Contains(cuts, cut => cut.GetProperty("cut").GetString() == "Top 200" && cut.GetProperty("candidateCount").GetInt32() == 105);

        var tierCounts = selectivity
            .GetProperty("tierDiagnostics")
            .GetProperty("totalRowsByTier")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(tierCounts, tier => tier.GetProperty("key").GetString() == "likelyHit" && tier.GetProperty("count").GetInt32() == 105);

        var lowValue = current.GetProperty("lowValueVolumeDiagnostics");
        Assert.Equal(105, lowValue.GetProperty("highHitProbabilityNonPositiveEvCount").GetInt32());
        Assert.Equal(105, lowValue.GetProperty("heavilyJuicedPickCount").GetInt32());
        Assert.Equal(84, lowValue.GetProperty("outsideTopTwentyPercentCount").GetInt32());

        var warnings = current.GetProperty("warningFlags");
        Assert.True(warnings.GetProperty("tooManyTopPicksForSlate").GetBoolean());
        Assert.True(warnings.GetProperty("topPicksMostlyLowEv").GetBoolean());
        Assert.True(warnings.GetProperty("topPicksMostlyHeavyJuice").GetBoolean());
        Assert.True(warnings.GetProperty("topPicksHighlyCorrelatedByPlayer").GetBoolean());
        Assert.True(warnings.GetProperty("topPicksHighlyCorrelatedByGame").GetBoolean());
        Assert.True(warnings.GetProperty("topPicksSideConcentrated").GetBoolean());
        Assert.True(warnings.GetProperty("topPicksPropTypeConcentrated").GetBoolean());
        Assert.True(warnings.GetProperty("topPicksMostlyLikelyHitsNotValue").GetBoolean());
        Assert.True(warnings.GetProperty("topPicksNameMayBeTooBroad").GetBoolean());

        var reconciliation = current.GetProperty("reconciliation");
        Assert.Equal(105, reconciliation.GetProperty("totalCurrentRows").GetInt32());
        Assert.Equal(105, reconciliation.GetProperty("sportCountSum").GetInt32());
        Assert.Equal(105, reconciliation.GetProperty("tierCountSum").GetInt32());
        Assert.True(reconciliation.GetProperty("countsReconcile").GetBoolean());

        var curation = current.GetProperty("curationComparison");
        var compactCurationSummary = curation.GetProperty("summary");
        Assert.Equal(105, compactCurationSummary.GetProperty("broadCandidateCount").GetInt32());
        Assert.Equal(105, compactCurationSummary.GetProperty("preDedupeCandidateCount").GetInt32());
        Assert.Equal(1, compactCurationSummary.GetProperty("postOpportunityDedupeCount").GetInt32());
        Assert.Equal(1, compactCurationSummary.GetProperty("curatedTopPickCount").GetInt32());
        Assert.Equal(104, compactCurationSummary.GetProperty("suppressedTotal").GetInt32());
        Assert.Equal(104, compactCurationSummary.GetProperty("suppressedProviderFanoutCount").GetInt32());
        Assert.True(compactCurationSummary.GetProperty("countsReconcile").GetBoolean());
        Assert.True(compactCurationSummary.GetProperty("hasProviderFanoutAlternatives").GetBoolean());
        Assert.Equal(1, compactCurationSummary.GetProperty("curatedRowsWithProviderOptionsCount").GetInt32());

        var fanoutDiagnostics = curation.GetProperty("providerFanoutDiagnostics");
        Assert.Equal("BroadPostOpportunityDedupePreExposureCaps", fanoutDiagnostics.GetProperty("scope").GetString());
        Assert.Equal(1, fanoutDiagnostics.GetProperty("providerFanoutOpportunityCount").GetInt32());
        Assert.Equal(105, fanoutDiagnostics.GetProperty("maxProviderFanoutCount").GetInt32());
        Assert.Equal(1, fanoutDiagnostics.GetProperty("rowsWithProviderOptionsCount").GetInt32());
        Assert.Single(fanoutDiagnostics.GetProperty("sampleProviderFanoutOpportunities").EnumerateArray());

        var finalPublicFanout = curation.GetProperty("finalPublicBoardFanoutDiagnostics");
        Assert.Equal("FinalPublicBoardReturnedRows", finalPublicFanout.GetProperty("scope").GetString());
        Assert.True(finalPublicFanout.GetProperty("usesSharedPublicTopPicksResponse").GetBoolean());
        Assert.Equal(
            finalPublicFanout.GetProperty("rowComputedProviderFanoutOpportunityCount").GetInt32(),
            finalPublicFanout.GetProperty("providerFanoutOpportunityCount").GetInt32());
        Assert.Equal(
            finalPublicFanout.GetProperty("rowComputedMaxProviderFanoutCount").GetInt32(),
            finalPublicFanout.GetProperty("maxProviderFanoutCount").GetInt32());
        Assert.Equal(
            finalPublicFanout.GetProperty("rowComputedRowsWithProviderOptionsCount").GetInt32(),
            finalPublicFanout.GetProperty("rowsWithProviderOptionsCount").GetInt32());

        var curated = curation.GetProperty("curatedTopPicks");
        Assert.True(curation.GetProperty("diagnosticOnly").GetBoolean());
        Assert.Equal(105, curated.GetProperty("preDedupeCandidateCount").GetInt32());
        Assert.Equal(1, curated.GetProperty("postOpportunityDedupeCount").GetInt32());
        Assert.Equal(1, curated.GetProperty("curatedTopPickCount").GetInt32());
        Assert.Equal(104, curated.GetProperty("suppressedProviderFanoutCount").GetInt32());
        Assert.Equal(1, curated.GetProperty("providerFanoutDiagnostics").GetProperty("providerFanoutOpportunityCount").GetInt32());
        Assert.Equal(
            finalPublicFanout.GetProperty("providerFanoutOpportunityCount").GetInt32(),
            curated.GetProperty("finalPublicBoardFanoutDiagnostics").GetProperty("providerFanoutOpportunityCount").GetInt32());
        Assert.True(curation.GetProperty("reconciliation").GetProperty("countsReconcile").GetBoolean());
    }

    private static JsonObject BuildTopPickJson(
        string sport,
        string eventId,
        string player,
        string propType,
        string side,
        decimal line,
        string bookmaker,
        decimal displayExpectedValue,
        decimal topPickScore)
    {
        return new JsonObject
        {
            ["sport"] = sport,
            ["competition"] = sport,
            ["eventId"] = eventId,
            ["eventStartTimeUtc"] = DateTimeOffset.UtcNow.AddHours(2),
            ["player"] = player,
            ["playerName"] = player,
            ["normalizedPropType"] = propType,
            ["propType"] = propType,
            ["line"] = line,
            ["bestSide"] = side,
            ["provider"] = "The Odds API",
            ["providerCategory"] = "Sportsbook",
            ["bookmaker"] = bookmaker,
            ["bestBookmaker"] = bookmaker,
            ["bestAmericanPrice"] = -110,
            ["bestDecimalPrice"] = 1.91m,
            ["bestImpliedProbability"] = 0.52381m,
            ["bestProbability"] = 0.70m,
            ["selectedSideProbability"] = 0.70m,
            ["bestProbabilityEdge"] = 0.17619m,
            ["bestEstimatedEv"] = displayExpectedValue,
            ["displayExpectedValue"] = displayExpectedValue,
            ["topPickScore"] = topPickScore,
            ["playablePriceEligible"] = true,
            ["isCandidate"] = true,
            ["isRecommendation"] = true,
            ["recommendationActionableForUser"] = true,
            ["recommendationType"] = "Sportsbook",
            ["entryActionableForUser"] = false,
            ["lifecycleStatus"] = "Active",
            ["isFresh"] = true,
            ["isStale"] = false,
            ["isExpired"] = false
        };
    }

    private static string BuildPublicTopPickFingerprint(IEnumerable<JsonElement> rows)
    {
        return string.Join(";",
            rows.Select(row =>
            {
                if (row.TryGetProperty("opportunityKey", out var opportunityKey) &&
                    opportunityKey.ValueKind == JsonValueKind.String)
                {
                    return opportunityKey.GetString() ?? string.Empty;
                }

                return string.Join('|', new[]
                {
                    ReadString(row, "sport"),
                    ReadString(row, "competition"),
                    ReadString(row, "eventId", "eventName"),
                    ReadString(row, "playerName", "player"),
                    ReadString(row, "normalizedPropType", "propType"),
                    ReadDecimal(row, "line")?.ToString("0.####", CultureInfo.InvariantCulture) ?? "0",
                    ReadString(row, "bestSide", "selectedSide", "side")
                });
            }));
    }

    private static string ReadString(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static decimal? ReadDecimal(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out var number)
                ? number
                : null;
    }

    private static async Task SeedProviderFanoutTopPickScenarioAsync(
        SafetyTestApplicationFactory factory,
        bool includeNonPublicHighFanout = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRunId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var eventStart = now.AddHours(2);
        var eventId = "provider-fanout-event";

        dbContext.TheOddsRefreshRuns.Add(new TheOddsRefreshRun
        {
            Id = refreshRunId,
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 30),
            StartedAtUtc = now.AddMinutes(-3),
            CompletedAtUtc = now.AddMinutes(-2),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbook",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "fanduel,betmgm,draftkings",
            DfsBookmakersRequested = string.Empty,
            MarketsRequested = "batter_total_bases",
            EstimatedApiCalls = 0,
            ActualApiCalls = 0,
            PredictionCount = 3,
            LineShoppingGroupCount = 3,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new { })
        });

        AddFanoutSnapshot("FanDuel", -115, 1.87m, 0.08m, 0.80m);
        AddFanoutSnapshot("BetMGM", -105, 1.95m, 0.16m, 0.78m);
        AddFanoutSnapshot("DraftKings", -110, 1.91m, 0.12m, 0.79m);
        if (includeNonPublicHighFanout)
        {
            AddFanoutSnapshot(
                "FanDuel",
                -102,
                1.98m,
                0.50m,
                0.99m,
                playerOverride: "Hidden Fanout Hitter",
                eventIdOverride: "hidden-provider-fanout-event",
                lineOverride: 0.5m,
                isRecommendation: false,
                recommendationActionableForUser: false);
            AddFanoutSnapshot(
                "BetMGM",
                -104,
                1.96m,
                0.48m,
                0.98m,
                playerOverride: "Hidden Fanout Hitter",
                eventIdOverride: "hidden-provider-fanout-event",
                lineOverride: 0.5m,
                isRecommendation: false,
                recommendationActionableForUser: false);
        }

        await dbContext.SaveChangesAsync();

        void AddFanoutSnapshot(
            string bookmaker,
            int americanPrice,
            decimal decimalPrice,
            decimal displayExpectedValue,
            decimal topPickScore,
            string? playerOverride = null,
            string? eventIdOverride = null,
            decimal? lineOverride = null,
            bool isRecommendation = true,
            bool recommendationActionableForUser = true)
        {
            var snapshotPlayer = playerOverride ?? "Fanout Hitter";
            var snapshotEventId = eventIdOverride ?? eventId;
            var snapshotLine = lineOverride ?? 1.5m;
            var detailsJson = JsonSerializer.SerializeToDocument(new
            {
                sport = "MLB",
                competition = "MLB",
                eventId = snapshotEventId,
                eventStartTimeUtc = eventStart,
                player = snapshotPlayer,
                playerName = snapshotPlayer,
                normalizedPropType = "Total Bases",
                propType = "Total Bases",
                line = snapshotLine,
                bestSide = "Over",
                provider = "The Odds API",
                providerCategory = "Sportsbook",
                bookmaker,
                bestBookmaker = bookmaker,
                bestAmericanPrice = americanPrice,
                bestDecimalPrice = decimalPrice,
                bestImpliedProbability = Math.Round(1m / decimalPrice, 6),
                bestProbability = 0.66m,
                selectedSideProbability = 0.66m,
                rawSelectedSideProbability = 0.66m,
                calibratedSelectedSideProbability = 0.66m,
                modelOverProbability = 0.66m,
                modelUnderProbability = 0.34m,
                bestProbabilityEdge = Math.Round(0.66m - (1m / decimalPrice), 6),
                bestEstimatedEv = displayExpectedValue,
                displayExpectedValue = displayExpectedValue,
                topPickScore,
                probabilitySource = "LikelyHitV1",
                probabilityModelVersion = "LikelyHitV1",
                evCalibrationMethod = "SportsbookBalanced",
                historicalSampleSize = 40,
                playerSampleSize = 40,
                sampleWarning = "None",
                confidenceScore = 0.82m,
                selectedSideHistoryConsistencyGuardApplied = true,
                selectedSideHistoryConsistencyGuardPassed = true,
                fatalSelectedSideHistoryConsistencyCodes = Array.Empty<string>(),
                priceGateStatus = "Passed",
                priceGateReason = (string?)null,
                evSuppressedReason = (string?)null,
                playablePriceEligible = true,
                isCandidate = true,
                isRecommendation,
                recommendationActionableForUser,
                recommendationType = "Sportsbook",
                entryActionableForUser = false,
                lifecycleStatus = "Active",
                isFresh = true,
                isStale = false,
                isExpired = false,
                modelEra = TopPicksModelOptions.DefaultLikelyHitV1ModelEra
            });

            dbContext.MarketTopPickSnapshots.Add(new MarketTopPickSnapshot
            {
                Id = Guid.NewGuid(),
                TheOddsRefreshRunId = refreshRunId,
                Sport = "MLB",
                SlateDate = new DateOnly(2026, 6, 30),
                GeneratedAtUtc = now.AddMinutes(-2),
                BookmakerPreset = "sportsbook",
                ProviderCategory = "Sportsbook",
                Provider = "The Odds API",
                Bookmaker = bookmaker,
                Player = snapshotPlayer,
                EventId = snapshotEventId,
                EventName = "Away @ Home",
                EventStartTime = eventStart,
                LifecycleStatus = "Active",
                IsStale = false,
                IsExpired = false,
                PropType = "Total Bases",
                Line = snapshotLine,
                BestSide = "Over",
                BestBookmaker = bookmaker,
                BestAmericanPrice = americanPrice,
                BestDecimalPrice = decimalPrice,
                BestProbability = 0.66m,
                BestImpliedProbability = Math.Round(1m / decimalPrice, 6),
                BestProbabilityEdge = Math.Round(0.66m - (1m / decimalPrice), 6),
                BestEstimatedEv = displayExpectedValue,
                StrategyClassification = "Candidate",
                StrategyTier = "MlbSportsbookRecommendation",
                ModelVersion = "LikelyHitV1",
                RecommendationContractVersion = "LikelyHitV1",
                ProbabilityModelVersion = "LikelyHitV1",
                ModelEra = TopPicksModelOptions.DefaultLikelyHitV1ModelEra,
                IsCandidate = true,
                IsWatchlist = false,
                IsAvoid = false,
                IsResearchOnly = false,
                EntryProvider = bookmaker,
                EntryCompatibilityGroup = "Sportsbook",
                EntryProviderCategory = "Sportsbook",
                EntryActionableForUser = false,
                DetailsJson = detailsJson
            });

            dbContext.MarketLineShoppingSnapshots.Add(new MarketLineShoppingSnapshot
            {
                Id = Guid.NewGuid(),
                TheOddsRefreshRunId = refreshRunId,
                Sport = "MLB",
                SlateDate = new DateOnly(2026, 6, 30),
                GeneratedAtUtc = now.AddMinutes(-2),
                BookmakerPreset = "sportsbook",
                ProviderCategory = "Sportsbook",
                Provider = "The Odds API",
                Bookmaker = bookmaker,
                Player = snapshotPlayer,
                EventId = snapshotEventId,
                EventName = "Away @ Home",
                EventStartTime = eventStart,
                LifecycleStatus = "Active",
                IsStale = false,
                IsExpired = false,
                PropType = "Total Bases",
                Line = snapshotLine,
                BestSide = "Over",
                BestBookmaker = bookmaker,
                BestAmericanPrice = americanPrice,
                BestDecimalPrice = decimalPrice,
                BestProbability = 0.66m,
                BestImpliedProbability = Math.Round(1m / decimalPrice, 6),
                BestProbabilityEdge = Math.Round(0.66m - (1m / decimalPrice), 6),
                BestEstimatedEv = displayExpectedValue,
                StrategyClassification = "Candidate",
                StrategyTier = "MlbSportsbookRecommendation",
                ModelVersion = "LikelyHitV1",
                RecommendationContractVersion = "LikelyHitV1",
                ProbabilityModelVersion = "LikelyHitV1",
                ModelEra = TopPicksModelOptions.DefaultLikelyHitV1ModelEra,
                IsCandidate = true,
                IsWatchlist = false,
                IsAvoid = false,
                IsResearchOnly = false,
                EntryProvider = bookmaker,
                EntryCompatibilityGroup = "Sportsbook",
                EntryProviderCategory = "Sportsbook",
                EntryActionableForUser = false,
                DetailsJson = JsonSerializer.SerializeToDocument(detailsJson.RootElement)
            });
        }
    }

    private static async Task SeedSelectedSideDiagnosticScenarioAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRunId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var eventStart = now.AddHours(2);
        var eventId = "selected-side-diagnostics-event";
        var detailsJson = JsonSerializer.SerializeToDocument(new
        {
            sport = "MLB",
            competition = "MLB",
            eventId,
            eventStartTimeUtc = eventStart,
            player = "Diagnostic Pitcher",
            playerName = "Diagnostic Pitcher",
            normalizedPropType = "Pitcher Strikeouts",
            line = 6.5m,
            bestSide = "Under",
            provider = "The Odds API",
            providerCategory = "Sportsbook",
            bookmaker = "FanDuel",
            bestBookmaker = "FanDuel",
            bestAmericanPrice = -110,
            bestDecimalPrice = 1.91m,
            bestImpliedProbability = 0.52381m,
            bestProbability = 0.67m,
            selectedSideProbability = 0.67m,
            rawSelectedSideProbability = 0.67m,
            calibratedSelectedSideProbability = 0.67m,
            modelOverProbability = 0.33m,
            modelUnderProbability = 0.67m,
            bestProbabilityEdge = 0.14619m,
            bestEstimatedEv = 0.2797m,
            displayExpectedValue = 0.2797m,
            probabilitySource = "LikelyHitV1",
            probabilityModelVersion = "LikelyHitV1",
            recommendationProjection = 5.8m,
            recommendationProjectionSource = "EmpiricalMeanFromProbabilityPlayerHistory",
            displayProjectionSource = "EmpiricalMeanFromProbabilityPlayerHistory",
            evCalibrationMethod = "SportsbookBalanced",
            historicalSampleSize = 44,
            playerSampleSize = 44,
            sampleWarning = "None",
            confidenceScore = 0.86m,
            selectedSideHistoryConsistencyGuardApplied = true,
            selectedSideHistoryConsistencyGuardPassed = true,
            fatalSelectedSideHistoryConsistencyCodes = Array.Empty<string>(),
            priceGateStatus = "Passed",
            priceGateReason = (string?)null,
            evSuppressedReason = (string?)null,
            isCandidate = true,
            isRecommendation = true,
            recommendationActionableForUser = true,
            recommendationType = "Sportsbook",
            entryActionableForUser = false,
            lifecycleStatus = "Active",
            isFresh = true,
            isStale = false,
            isExpired = false,
            modelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded"
        });

        dbContext.TheOddsRefreshRuns.Add(new TheOddsRefreshRun
        {
            Id = refreshRunId,
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 30),
            StartedAtUtc = now.AddMinutes(-3),
            CompletedAtUtc = now.AddMinutes(-2),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbook",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "fanduel",
            DfsBookmakersRequested = string.Empty,
            MarketsRequested = "pitcher_strikeouts",
            EstimatedApiCalls = 0,
            ActualApiCalls = 0,
            PredictionCount = 1,
            LineShoppingGroupCount = 1,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new { })
        });

        dbContext.MarketTopPickSnapshots.Add(new MarketTopPickSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRunId,
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 30),
            GeneratedAtUtc = now.AddMinutes(-2),
            BookmakerPreset = "sportsbook",
            ProviderCategory = "Sportsbook",
            Provider = "The Odds API",
            Bookmaker = "FanDuel",
            Player = "Diagnostic Pitcher",
            EventId = eventId,
            EventName = "Away @ Home",
            EventStartTime = eventStart,
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Pitcher Strikeouts",
            Line = 6.5m,
            BestSide = "Under",
            BestBookmaker = "FanDuel",
            BestAmericanPrice = -110,
            BestDecimalPrice = 1.91m,
            BestProbability = 0.67m,
            BestImpliedProbability = 0.52381m,
            BestProbabilityEdge = 0.14619m,
            BestEstimatedEv = 0.2797m,
            StrategyClassification = "Candidate",
            StrategyTier = "MlbSportsbookRecommendation",
            ModelVersion = "LikelyHitV1",
            RecommendationContractVersion = "LikelyHitV1",
            ProbabilityModelVersion = "LikelyHitV1",
            ModelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded",
            IsCandidate = true,
            IsWatchlist = false,
            IsAvoid = false,
            IsResearchOnly = false,
            EntryProvider = "FanDuel",
            EntryCompatibilityGroup = "Sportsbook",
            EntryProviderCategory = "Sportsbook",
            EntryActionableForUser = false,
            DetailsJson = detailsJson
        });

        dbContext.RecommendationSnapshots.Add(new RecommendationSnapshot
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = "selected-side-diagnostics-ledger",
            FirstSeenAtUtc = now.AddMinutes(-2),
            LastSeenAtUtc = now.AddMinutes(-2),
            SeenCount = 1,
            CapturedAtUtc = now.AddMinutes(-2),
            RefreshRunId = refreshRunId,
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 30),
            ExpectedResultDate = new DateOnly(2026, 6, 30),
            ResultDateSource = "ExpectedResultDate",
            EventId = eventId,
            EventStartTimeUtc = eventStart,
            PlayerName = "Diagnostic Pitcher",
            NormalizedPlayerName = "diagnosticpitcher",
            PropType = "Pitcher Strikeouts",
            NormalizedPropType = "pitcherstrikeouts",
            Side = "Under",
            Line = 6.5m,
            Provider = "The Odds API",
            Bookmaker = "FanDuel",
            ProviderCategory = "Sportsbook",
            EntryProvider = "FanDuel",
            EntryCompatibilityGroup = "Sportsbook",
            HitChance = 0.67m,
            ImpliedProbability = 0.52381m,
            ProbabilityEdge = 0.14619m,
            EstimatedEv = 0.2797m,
            TrustLevel = "Strong",
            HistoricalSampleSize = 44,
            StrategyClassification = "Candidate",
            StrategyTier = "MlbSportsbookRecommendation",
            ModelVersion = "LikelyHitV1",
            RecommendationContractVersion = "LikelyHitV1",
            ProbabilityModelVersion = "LikelyHitV1",
            ModelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded",
            RankingScore = 1m,
            IsCandidate = true,
            IsTopPick = true,
            IsWatchlist = false,
            IsActionableForUser = false,
            LifecycleStatus = "Active",
            ResultValue = 4m,
            GradeStatus = "Win",
            GradedAtUtc = now.AddMinutes(-1)
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedValueAuditScenarioAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRunId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var eventStart = now.AddHours(2);
        var eventId = "likely-hit-value-audit-event";
        var detailsJson = JsonSerializer.SerializeToDocument(new
        {
            sport = "MLB",
            competition = "MLB",
            eventId,
            eventStartTimeUtc = eventStart,
            player = "Value Audit Hitter",
            playerName = "Value Audit Hitter",
            normalizedPropType = "Total Bases",
            propType = "Total Bases",
            line = 1.5m,
            bestSide = "Under",
            provider = "The Odds API",
            providerCategory = "Sportsbook",
            bookmaker = "FanDuel",
            bestBookmaker = "FanDuel",
            bestAmericanPrice = -333,
            bestDecimalPrice = 1.3m,
            bestImpliedProbability = 0.769231m,
            bestProbability = 0.70m,
            selectedSideProbability = 0.70m,
            rawSelectedSideProbability = 0.70m,
            calibratedSelectedSideProbability = 0.70m,
            modelOverProbability = 0.30m,
            modelUnderProbability = 0.70m,
            bestProbabilityEdge = -0.069231m,
            bestEstimatedEv = -0.09m,
            displayExpectedValue = -0.09m,
            probabilitySource = "LikelyHitV1",
            probabilityModelVersion = "LikelyHitV1",
            recommendationProjection = 2.2m,
            recommendationProjectionSource = "EmpiricalMeanFromProbabilityPlayerHistory",
            displayProjectionSource = "EmpiricalMeanFromProbabilityPlayerHistory",
            evCalibrationMethod = "SportsbookBalanced",
            historicalSampleSize = 55,
            playerSampleSize = 55,
            sampleWarning = "None",
            confidenceScore = 0.88m,
            selectedSideHistoryConsistencyGuardApplied = true,
            selectedSideHistoryConsistencyGuardPassed = true,
            fatalSelectedSideHistoryConsistencyCodes = Array.Empty<string>(),
            priceGateStatus = "Passed",
            priceGateReason = (string?)null,
            evSuppressedReason = "NonPositiveDisplayExpectedValue",
            isCandidate = true,
            isRecommendation = true,
            recommendationActionableForUser = true,
            recommendationType = "Sportsbook",
            entryActionableForUser = false,
            lifecycleStatus = "Active",
            isFresh = true,
            isStale = false,
            isExpired = false,
            modelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded"
        });

        dbContext.TheOddsRefreshRuns.Add(new TheOddsRefreshRun
        {
            Id = refreshRunId,
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 30),
            StartedAtUtc = now.AddMinutes(-3),
            CompletedAtUtc = now.AddMinutes(-2),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbook",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "fanduel",
            DfsBookmakersRequested = string.Empty,
            MarketsRequested = "batter_total_bases",
            EstimatedApiCalls = 0,
            ActualApiCalls = 0,
            PredictionCount = 1,
            LineShoppingGroupCount = 1,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new { })
        });

        dbContext.MarketTopPickSnapshots.Add(new MarketTopPickSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRunId,
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 30),
            GeneratedAtUtc = now.AddMinutes(-2),
            BookmakerPreset = "sportsbook",
            ProviderCategory = "Sportsbook",
            Provider = "The Odds API",
            Bookmaker = "FanDuel",
            Player = "Value Audit Hitter",
            EventId = eventId,
            EventName = "Away @ Home",
            EventStartTime = eventStart,
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Total Bases",
            Line = 1.5m,
            BestSide = "Under",
            BestBookmaker = "FanDuel",
            BestAmericanPrice = -333,
            BestDecimalPrice = 1.3m,
            BestProbability = 0.70m,
            BestImpliedProbability = 0.769231m,
            BestProbabilityEdge = -0.069231m,
            BestEstimatedEv = -0.09m,
            StrategyClassification = "Candidate",
            StrategyTier = "MlbSportsbookRecommendation",
            ModelVersion = "LikelyHitV1",
            RecommendationContractVersion = "LikelyHitV1",
            ProbabilityModelVersion = "LikelyHitV1",
            ModelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded",
            IsCandidate = true,
            IsWatchlist = false,
            IsAvoid = false,
            IsResearchOnly = false,
            EntryProvider = "FanDuel",
            EntryCompatibilityGroup = "Sportsbook",
            EntryProviderCategory = "Sportsbook",
            EntryActionableForUser = false,
            DetailsJson = detailsJson
        });

        dbContext.RecommendationSnapshots.Add(new RecommendationSnapshot
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = "likely-hit-value-audit-ledger",
            FirstSeenAtUtc = now.AddMinutes(-2),
            LastSeenAtUtc = now.AddMinutes(-2),
            SeenCount = 1,
            CapturedAtUtc = now.AddMinutes(-2),
            RefreshRunId = refreshRunId,
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 30),
            ExpectedResultDate = new DateOnly(2026, 6, 30),
            ResultDateSource = "ExpectedResultDate",
            EventId = eventId,
            EventStartTimeUtc = eventStart,
            PlayerName = "Value Audit Hitter",
            NormalizedPlayerName = "valueaudithitter",
            PropType = "Total Bases",
            NormalizedPropType = "totalbases",
            Side = "Under",
            Line = 1.5m,
            Provider = "The Odds API",
            Bookmaker = "FanDuel",
            ProviderCategory = "Sportsbook",
            EntryProvider = "FanDuel",
            EntryCompatibilityGroup = "Sportsbook",
            HitChance = 0.70m,
            ImpliedProbability = 0.769231m,
            ProbabilityEdge = -0.069231m,
            EstimatedEv = -0.09m,
            TrustLevel = "Strong",
            HistoricalSampleSize = 55,
            StrategyClassification = "Candidate",
            StrategyTier = "MlbSportsbookRecommendation",
            ModelVersion = "LikelyHitV1",
            RecommendationContractVersion = "LikelyHitV1",
            ProbabilityModelVersion = "LikelyHitV1",
            ModelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded",
            RankingScore = 1m,
            IsCandidate = true,
            IsTopPick = true,
            IsWatchlist = false,
            IsActionableForUser = false,
            LifecycleStatus = "Active",
            ResultValue = 1m,
            GradeStatus = "Win",
            GradedAtUtc = now.AddMinutes(-1)
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedHighVolumeValueAuditScenarioAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRunId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var eventStart = now.AddHours(2);
        var eventId = "likely-hit-volume-audit-event";

        dbContext.TheOddsRefreshRuns.Add(new TheOddsRefreshRun
        {
            Id = refreshRunId,
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = new DateOnly(2026, 6, 30),
            StartedAtUtc = now.AddMinutes(-3),
            CompletedAtUtc = now.AddMinutes(-2),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbook",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "fanduel",
            DfsBookmakersRequested = string.Empty,
            MarketsRequested = "batter_total_bases",
            EstimatedApiCalls = 0,
            ActualApiCalls = 0,
            PredictionCount = 105,
            LineShoppingGroupCount = 105,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new { })
        });

        for (var index = 0; index < 105; index++)
        {
            var score = 1m - (index * 0.001m);
            var detailsJson = JsonSerializer.SerializeToDocument(new
            {
                sport = "MLB",
                competition = "MLB",
                eventId,
                eventName = "Away @ Home",
                homeTeam = "Home",
                awayTeam = "Away",
                teamName = "Home",
                eventStartTimeUtc = eventStart,
                slateDate = "2026-06-30",
                expectedResultDate = "2026-06-30",
                player = "Volume Audit Hitter",
                playerName = "Volume Audit Hitter",
                normalizedPropType = "Total Bases",
                propType = "Total Bases",
                line = 1.5m,
                bestSide = "Under",
                provider = "The Odds API",
                providerCategory = "Sportsbook",
                bookmaker = "FanDuel",
                bestBookmaker = "FanDuel",
                bestAmericanPrice = -333,
                bestDecimalPrice = 1.3m,
                bestImpliedProbability = 0.769231m,
                bestProbability = 0.72m,
                selectedSideProbability = 0.72m,
                rawSelectedSideProbability = 0.72m,
                calibratedSelectedSideProbability = 0.72m,
                modelOverProbability = 0.28m,
                modelUnderProbability = 0.72m,
                bestProbabilityEdge = -0.049231m,
                bestEstimatedEv = -0.064m,
                displayExpectedValue = -0.064m,
                topPickScore = score,
                probabilitySource = "LikelyHitV1",
                probabilityModelVersion = "LikelyHitV1",
                recommendationProjection = 1.2m,
                recommendationProjectionSource = "EmpiricalMeanFromProbabilityPlayerHistory",
                displayProjectionSource = "EmpiricalMeanFromProbabilityPlayerHistory",
                evCalibrationMethod = "SportsbookBalanced",
                selectedSideHistoryConsistencyGuardApplied = true,
                selectedSideHistoryConsistencyGuardPassed = true,
                fatalSelectedSideHistoryConsistencyCodes = Array.Empty<string>(),
                priceGateStatus = "Passed",
                isCandidate = true,
                isRecommendation = true,
                recommendationActionableForUser = true,
                recommendationType = "Sportsbook",
                entryActionableForUser = false,
                lifecycleStatus = "Active",
                isFresh = true,
                isStale = false,
                isExpired = false,
                modelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded"
            });

            dbContext.MarketTopPickSnapshots.Add(new MarketTopPickSnapshot
            {
                Id = Guid.NewGuid(),
                TheOddsRefreshRunId = refreshRunId,
                Sport = "MLB",
                SlateDate = new DateOnly(2026, 6, 30),
                GeneratedAtUtc = now.AddMinutes(-2),
                BookmakerPreset = "sportsbook",
                ProviderCategory = "Sportsbook",
                Provider = "The Odds API",
                Bookmaker = "FanDuel",
                Player = "Volume Audit Hitter",
                EventId = eventId,
                EventName = "Away @ Home",
                EventStartTime = eventStart,
                LifecycleStatus = "Active",
                IsStale = false,
                IsExpired = false,
                PropType = "Total Bases",
                Line = 1.5m,
                BestSide = "Under",
                BestBookmaker = "FanDuel",
                BestAmericanPrice = -333,
                BestDecimalPrice = 1.3m,
                BestProbability = 0.72m,
                BestImpliedProbability = 0.769231m,
                BestProbabilityEdge = -0.049231m,
                BestEstimatedEv = -0.064m,
                StrategyClassification = "Candidate",
                StrategyTier = "MlbSportsbookRecommendation",
                ModelVersion = "LikelyHitV1",
                RecommendationContractVersion = "LikelyHitV1",
                ProbabilityModelVersion = "LikelyHitV1",
                ModelEra = "LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded",
                IsCandidate = true,
                IsWatchlist = false,
                IsAvoid = false,
                IsResearchOnly = false,
                EntryProvider = "FanDuel",
                EntryCompatibilityGroup = "Sportsbook",
                EntryProviderCategory = "Sportsbook",
                EntryActionableForUser = false,
                DetailsJson = detailsJson
            });
        }

        await dbContext.SaveChangesAsync();
    }
}
