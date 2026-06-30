using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Tests;

public sealed class PerformanceGradingTests
{
    [Theory]
    [InlineData("Pitcher Strikeouts", "Over", 6.5, 7, 1, 0, 0)]
    [InlineData("Pitcher Strikeouts", "Under", 6.5, 6, 1, 0, 0)]
    [InlineData("Total Bases", "Over", 1.5, 2, 1, 0, 0)]
    [InlineData("Total Bases", "Under", 1.5, 2, 0, 1, 0)]
    public async Task PerformanceSummary_GradesImportedResults_ByPlayerDateAndPropType(
        string propType,
        string side,
        decimal line,
        decimal resultValue,
        int expectedWins,
        int expectedLosses,
        int expectedPushes)
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(factory, date, propType, side, line, resultValue);

        var response = await client.GetAsync("/api/markets/performance-summary?candidateOnly=true&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var overall = json.GetProperty("groups").GetProperty("overall")[0];
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(expectedWins, overall.GetProperty("wins").GetInt32());
        Assert.Equal(expectedLosses, overall.GetProperty("losses").GetInt32());
        Assert.Equal(expectedPushes, overall.GetProperty("pushes").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_DoesNotRequireProviderLineOrSideToFindResultRow()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Under",
            12.5m,
            4,
            provider: "PrizePicks",
            bookmaker: "PrizePicks");

        var response = await client.GetAsync("/api/markets/performance-summary?candidateOnly=true&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var overall = json.GetProperty("groups").GetProperty("overall")[0];
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, overall.GetProperty("wins").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_MissingImportedDate_IsPending_NotLoss()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, resultValue: null);

        var response = await client.GetAsync("/api/markets/performance-summary?candidateOnly=true&gradingMode=rollingWindow");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("pendingRowsForCompletedDate").GetInt32());
        Assert.Empty(json.GetProperty("groups").GetProperty("overall").EnumerateArray());
    }

    [Fact]
    public async Task PerformanceSummary_PlayerDateExistsButPropMissing_IsNoResult_NotLoss()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            resultValue: null,
            alternateResultPropType: "Pitcher Strikeouts",
            alternateResultValue: 6);

        var response = await client.GetAsync("/api/markets/performance-summary?candidateOnly=true&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("noResultRowsForCompletedDate").GetInt32());
        Assert.Empty(json.GetProperty("groups").GetProperty("overall").EnumerateArray());

        var diagnosticResponse = await client.GetAsync("/api/markets/performance-summary?candidateOnly=true&gradingMode=latestCompleted&includeNoResultDiagnostics=true");
        await EnsureSuccessWithBodyAsync(diagnosticResponse);
        var diagnosticJson = await diagnosticResponse.Content.ReadFromJsonAsync<JsonElement>();
        var diagnosticOverall = diagnosticJson.GetProperty("groups").GetProperty("overall")[0];
        Assert.Equal(1, diagnosticOverall.GetProperty("noResults").GetInt32());
    }

    [Fact]
    public async Task PerformanceResults_ReturnsDeterminedRows_AndExcludesNoResultRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(factory, date, "Pitcher Strikeouts", "Under", 6.5m, 4);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Under",
            1.5m,
            resultValue: null,
            sameDateOtherPlayerResultValue: 2,
            resetDatabase: false);

        var response = await client.GetAsync($"/api/markets/performance-results?sport=MLB&date={date:yyyy-MM-dd}&includeDiagnostics=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, summary.GetProperty("won").GetInt32());
        Assert.Equal(1, summary.GetProperty("noResultExcluded").GetInt32());
        Assert.Single(json.GetProperty("rows").EnumerateArray());
        Assert.Equal("Won", json.GetProperty("rows")[0].GetProperty("result").GetString());
        Assert.Single(json.GetProperty("diagnostics").GetProperty("noResultCounts").EnumerateObject());
    }

    [Fact]
    public async Task PerformanceResults_CountsProviderInactiveRowsAsExcludedDiagnostics()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Under",
            1.5m,
            resultValue: null,
            sameDateOtherPlayerResultValue: 2);

        var response = await client.GetAsync($"/api/markets/performance-results?sport=MLB&date={date:yyyy-MM-dd}&includeDiagnostics=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("noResultExcluded").GetInt32());
        Assert.True(json.GetProperty("diagnostics").GetProperty("noResultCounts").TryGetProperty("ProviderLineForInactivePlayer", out _));
        Assert.Empty(json.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task PerformanceResults_GradesEquivalentPropKeys_WhenPropTypeIdsDiffer()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Under",
            1.5m,
            0,
            resultUsesDifferentPropTypeIdWithEquivalentName: true);

        var response = await client.GetAsync($"/api/markets/performance-results?sport=MLB&date={date:yyyy-MM-dd}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal("Won", json.GetProperty("rows")[0].GetProperty("result").GetString());
    }

    [Fact]
    public async Task PerformanceResults_AmbiguousNormalizedNameFallback_DoesNotGrade()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Under",
            6.5m,
            4,
            resultUsesDifferentPlayerIdWithSameName: true,
            additionalSameNameResultValue: 5);

        var response = await client.GetAsync($"/api/markets/performance-results?sport=MLB&date={date:yyyy-MM-dd}&includeDiagnostics=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("noResultExcluded").GetInt32());
        Assert.Empty(json.GetProperty("rows").EnumerateArray());
        Assert.True(json.GetProperty("diagnostics").GetProperty("noResultCounts").TryGetProperty("AmbiguousNormalizedPlayerNameDateStatFallback", out _));
    }

    [Fact]
    public async Task PerformanceResults_DefaultDateSkipsPartialLatestCoverage()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var completeDate = new DateOnly(2026, 6, 22);
        var partialDate = new DateOnly(2026, 6, 23);
        await SeedPerformanceScenarioAsync(
            factory,
            completeDate,
            "Pitcher Strikeouts",
            "Over",
            6.5m,
            7m,
            provider: "PrizePicks");
        await SeedPerformanceScenarioAsync(
            factory,
            partialDate,
            "Pitcher Strikeouts",
            "Over",
            6.5m,
            7m,
            provider: "Underdog",
            resetDatabase: false);
        await SeedMlbCoverageStateAsync(factory, completeDate, "Complete", importedResults: 2930, distinctPlayers: 274, distinctPropTypes: 16);
        await SeedMlbCoverageStateAsync(factory, partialDate, "Partial", importedResults: 220, distinctPlayers: 20, distinctPropTypes: 11);

        var response = await client.GetAsync("/api/markets/performance-results?sport=MLB&includeDiagnostics=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(completeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), json.GetProperty("date").GetString());
        Assert.Equal("LatestCompleteMlbCoverageDate", json.GetProperty("dateSelection").GetProperty("source").GetString());
        Assert.Contains(json.GetProperty("dateSelection").GetProperty("skippedIncompleteResultDates").EnumerateArray(), row => row.GetString() == partialDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("won").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_MultipleMatchingResultRows_IsUnmatched_NotLoss()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Over",
            6.5m,
            7,
            duplicateMatchingResultValue: 8);

        var response = await client.GetAsync("/api/markets/performance-summary?candidateOnly=true&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("unmatchedRowsForCompletedDate").GetInt32());
        Assert.Empty(json.GetProperty("groups").GetProperty("overall").EnumerateArray());
    }

    [Fact]
    public async Task ResultCoverage_MatchedRow_UsesMatchedAndGradedReasonCode()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        var seeded = await SeedPerformanceScenarioAsync(factory, date, "Pitcher Strikeouts", "Under", 5.5m, 4);

        var response = await client.GetAsync($"/api/debug/recommendations/ledger/result-coverage?recommendationSnapshotId={seeded.RecommendationSnapshotId}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var example = json.GetProperty("resultCoverageExamples")[0];
        Assert.Equal("Won", example.GetProperty("finalGradeStatus").GetString());
        Assert.Equal("MatchedAndGraded", example.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task PerformanceSummary_StaleWatchlistRow_IsExcludedFromDefaultCandidatePerformance()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2,
            isCandidate: false,
            isTopPick: false,
            isWatchlist: true,
            isActionableForUser: false,
            lifecycleStatus: "Stale",
            warning: "Line is stale");

        var response = await client.GetAsync("/api/markets/performance-summary?candidateOnly=true&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("picksAfterFilters").GetInt32());
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_StaleCandidateRow_IsIncludedOnlyWithIncludeStale()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2,
            isCandidate: true,
            isTopPick: false,
            isWatchlist: false,
            isActionableForUser: false,
            lifecycleStatus: "Stale",
            warning: "Line is stale");

        var defaultResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");
        var includeStaleResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&includeStale=true");

        await EnsureSuccessWithBodyAsync(defaultResponse);
        await EnsureSuccessWithBodyAsync(includeStaleResponse);
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        var includeStaleJson = await includeStaleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, defaultJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, defaultJson.GetProperty("dataAvailability").GetProperty("rowsExcludedStale").GetInt32());
        Assert.Equal(1, includeStaleJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, includeStaleJson.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_WatchlistOnlyRow_IsIncludedOnlyWithIncludeWatchlist()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2,
            isCandidate: false,
            isTopPick: false,
            isWatchlist: true);

        var defaultResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");
        var includeWatchlistResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&includeWatchlist=true");

        await EnsureSuccessWithBodyAsync(defaultResponse);
        await EnsureSuccessWithBodyAsync(includeWatchlistResponse);
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        var includeWatchlistJson = await includeWatchlistResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, defaultJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, defaultJson.GetProperty("dataAvailability").GetProperty("rowsExcludedWatchlist").GetInt32());
        Assert.Equal(1, includeWatchlistJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_ResearchOnlyRow_IsIncludedOnlyWithIncludeResearch()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2,
            isCandidate: false,
            isTopPick: false,
            isWatchlist: false);

        var defaultResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");
        var includeResearchResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&includeResearch=true");

        await EnsureSuccessWithBodyAsync(defaultResponse);
        await EnsureSuccessWithBodyAsync(includeResearchResponse);
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        var includeResearchJson = await includeResearchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, defaultJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, defaultJson.GetProperty("dataAvailability").GetProperty("rowsExcludedResearch").GetInt32());
        Assert.Equal(1, includeResearchJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_TopPickOnly_ExcludesStaleTopPickByDefault()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Under",
            5.5m,
            4,
            isCandidate: true,
            isTopPick: true,
            isWatchlist: false,
            isActionableForUser: false,
            lifecycleStatus: "Locked");

        var defaultResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&topPickOnly=true");
        var includeStaleResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&topPickOnly=true&includeStale=true");

        await EnsureSuccessWithBodyAsync(defaultResponse);
        await EnsureSuccessWithBodyAsync(includeStaleResponse);
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        var includeStaleJson = await includeStaleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, defaultJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, defaultJson.GetProperty("dataAvailability").GetProperty("rowsExcludedNonPlayable").GetInt32());
        Assert.Equal(1, includeStaleJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_IncludeSwitchesBroadenResultSet()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, 2);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, 2, isCandidate: false, isTopPick: false, isWatchlist: true, resetDatabase: false);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, 2, isCandidate: false, isTopPick: false, isWatchlist: false, resetDatabase: false);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, 2, isCandidate: true, isTopPick: false, isWatchlist: false, isActionableForUser: false, lifecycleStatus: "Expired", resetDatabase: false);

        var defaultResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");
        var broadResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&includeWatchlist=true&includeResearch=true&includeStale=true");

        await EnsureSuccessWithBodyAsync(defaultResponse);
        await EnsureSuccessWithBodyAsync(broadResponse);
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        var broadJson = await broadResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, defaultJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(4, broadJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(
            broadJson.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32(),
            broadJson.GetProperty("groups").GetProperty("overall")[0].GetProperty("totalCandidatePicks").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_CandidateTopPickRequiresCanonicalPlayer()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, 2, includeCanonicalPlayerId: false);

        var response = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("rowsExcludedMissingPlayer").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_CandidateTopPickRequiresSupportedPropType()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(factory, date, "Unsupported Stat", "Over", 1.5m, 2);

        var response = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("rowsExcludedUnsupportedProp").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_DoesNotCountSupportedCompactPropKeysAsUnsupported()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, resultValue: null, sameDateOtherPlayerResultValue: 2);

        var response = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dataAvailability = json.GetProperty("dataAvailability");
        Assert.Equal(1, dataAvailability.GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(0, dataAvailability.GetProperty("rowsExcludedUnsupportedProp").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_CandidateTopPickRequiresEventDateMapping()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, 2, includeEventMapping: false);

        var response = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("rowsExcludedMissingEventMapping").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_NoResultReasonCountsExplainTopPickRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            resultValue: null,
            sameDateOtherPlayerResultValue: 2,
            isCandidate: true,
            isTopPick: true);

        var response = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&topPickOnly=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dataAvailability = json.GetProperty("dataAvailability");
        var reasonCounts = dataAvailability.GetProperty("noResultReasonCodeCounts");
        Assert.Equal(1, dataAvailability.GetProperty("noResultRowsForCompletedDate").GetInt32());
        Assert.Equal(1, reasonCounts.GetProperty("ProviderLineForInactivePlayer").GetInt32());
        Assert.Equal(
            dataAvailability.GetProperty("noResultRowsForCompletedDate").GetInt32(),
            reasonCounts.EnumerateObject().Sum(row => row.Value.GetInt32()));
    }

    [Fact]
    public async Task NoResultAudit_GroupsEligibleTopPickNoResultRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            resultValue: null,
            sameDateOtherPlayerResultValue: 2,
            isCandidate: true,
            isTopPick: true);

        var response = await client.GetAsync($"/api/debug/recommendations/ledger/no-result-audit?sport=MLB&date={date:yyyy-MM-dd}&topPickOnly=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("noResultRows").GetInt32());
        Assert.Equal(1, json.GetProperty("reasonCodeCounts").GetProperty("ProviderLineForInactivePlayer").GetInt32());
        Assert.Equal("ProviderLineForInactivePlayer", json.GetProperty("rows")[0].GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task PerformanceSummary_AndNoResultAudit_UseSameReasonForNearbyDateMismatch()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            resultValue: null,
            nearbyResultDate: date.AddDays(-1),
            nearbyResultValue: 2,
            sameDateOtherPlayerResultValue: 2,
            isCandidate: true,
            isTopPick: true);

        var summaryResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&topPickOnly=true");
        var auditResponse = await client.GetAsync($"/api/debug/recommendations/ledger/no-result-audit?sport=MLB&date={date:yyyy-MM-dd}&topPickOnly=true");

        await EnsureSuccessWithBodyAsync(summaryResponse);
        await EnsureSuccessWithBodyAsync(auditResponse);
        var summaryJson = await summaryResponse.Content.ReadFromJsonAsync<JsonElement>();
        var auditJson = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, summaryJson.GetProperty("dataAvailability").GetProperty("noResultReasonCodeCounts").GetProperty("EventDateMismatch").GetInt32());
        Assert.Equal("EventDateMismatch", auditJson.GetProperty("rows")[0].GetProperty("reasonCode").GetString());
        Assert.Equal(1, auditJson.GetProperty("rows")[0].GetProperty("nearbyDatePlayerPropResultCount").GetInt32());
        Assert.Equal(1, auditJson.GetProperty("rows")[0].GetProperty("nearbyDatePlayerPropMatches").GetArrayLength());
    }

    [Fact]
    public async Task ResultCoverage_AfterMidnightUtcEvent_CanUsePreviousEasternMlbDate()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var easternGameDate = new DateOnly(2026, 6, 17);
        var eventStartUtc = new DateTimeOffset(2026, 6, 18, 1, 30, 0, TimeSpan.Zero);
        var seeded = await SeedPerformanceScenarioAsync(
            factory,
            easternGameDate,
            "Total Bases",
            "Under",
            1.5m,
            1,
            eventStartTimeUtc: eventStartUtc);

        var response = await client.GetAsync($"/api/debug/recommendations/ledger/result-coverage?recommendationSnapshotId={seeded.RecommendationSnapshotId}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var capturedPick = json.GetProperty("resultCoverageExamples")[0].GetProperty("capturedPick");
        Assert.True(capturedPick.GetProperty("eventUtcDateDiffersFromExpectedResultDate").GetBoolean());
        Assert.False(capturedPick.GetProperty("eventEasternDateDiffersFromExpectedResultDate").GetBoolean());
    }

    [Fact]
    public async Task PerformanceSummary_AfterMidnightUtcEvent_GradesAgainstEasternResultDate()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var easternGameDate = new DateOnly(2026, 6, 17);
        var eventStartUtc = new DateTimeOffset(2026, 6, 18, 1, 30, 0, TimeSpan.Zero);
        await SeedPerformanceScenarioAsync(
            factory,
            easternGameDate,
            "Total Bases",
            "Under",
            1.5m,
            1,
            eventStartTimeUtc: eventStartUtc);

        var response = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dataAvailability = json.GetProperty("dataAvailability");
        Assert.Equal(1, dataAvailability.GetProperty("gradedRowsForCompletedDate").GetInt32());
        Assert.Equal(0, dataAvailability.GetProperty("rowsExcludedEventDateMappingMismatch").GetInt32());
    }

    [Fact]
    public async Task ResultCoverage_EventDateMismatch_OnlyWhenNearbyPlayerPropResultExists()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        var seeded = await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Over",
            5.5m,
            resultValue: null,
            nearbyResultDate: date.AddDays(1),
            nearbyResultValue: 7);

        var response = await client.GetAsync($"/api/debug/recommendations/ledger/result-coverage?recommendationSnapshotId={seeded.RecommendationSnapshotId}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var example = json.GetProperty("resultCoverageExamples")[0];
        Assert.Equal("EventDateMismatch", example.GetProperty("reasonCode").GetString());
        Assert.Equal(1, example.GetProperty("importedResultChecks").GetProperty("nearbyDatePlayerPropResultCount").GetInt32());
    }

    [Fact]
    public async Task ResultCoverage_ProviderLineForInactivePlayer_RequiresSameDatePropForOtherPlayer()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        var seeded = await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            resultValue: null,
            sameDateOtherPlayerResultValue: 2);

        var response = await client.GetAsync($"/api/debug/recommendations/ledger/result-coverage?recommendationSnapshotId={seeded.RecommendationSnapshotId}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var importedChecks = json.GetProperty("resultCoverageExamples")[0].GetProperty("importedResultChecks");
        Assert.Equal("ProviderLineForInactivePlayer", json.GetProperty("resultCoverageExamples")[0].GetProperty("reasonCode").GetString());
        Assert.Equal(1, importedChecks.GetProperty("samePropTypeAnyPlayer").GetInt32());
        Assert.Equal(0, importedChecks.GetProperty("sameDatePlayerIdResultCount").GetInt32());
    }

    [Fact]
    public async Task PerformanceResetBaseline_DryRunDoesNotWrite()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, 2);

        var response = await client.PostAsync("/api/debug/recommendations/performance/reset-baseline?dryRun=true&take=1", null);

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, json.GetProperty("affectedCount").GetInt32());
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await dbContext.RecommendationSnapshots.CountAsync(row => row.IsExcludedFromPerformance));
    }

    [Fact]
    public async Task PerformanceResetBaseline_ExcludesLatestSixGradedSnapshots()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        var seededIds = new List<Guid>();
        for (var index = 0; index < 8; index++)
        {
            var seeded = await SeedPerformanceScenarioAsync(
                factory,
                date,
                "Total Bases",
                "Over",
                1.5m,
                2,
                resetDatabase: index == 0,
                capturedAtUtc: new DateTimeOffset(2026, 6, 17, 12, index, 0, TimeSpan.Zero),
                rankingScore: index);
            seededIds.Add(seeded.RecommendationSnapshotId);
        }

        var response = await client.PostAsync("/api/debug/recommendations/performance/reset-baseline?dryRun=false&take=6&reason=TestReset", null);

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("dryRun").GetBoolean());
        Assert.Equal(6, json.GetProperty("affectedCount").GetInt32());
        var expectedExcluded = seededIds.Skip(2).ToHashSet();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var excludedIds = await dbContext.RecommendationSnapshots
            .Where(row => row.IsExcludedFromPerformance)
            .Select(row => row.Id)
            .ToArrayAsync();
        Assert.Equal(expectedExcluded.Order(), excludedIds.Order());
        Assert.All(await dbContext.RecommendationSnapshots.Where(row => row.IsExcludedFromPerformance).ToArrayAsync(), row =>
        {
            Assert.Equal("TestReset", row.PerformanceExclusionReason);
            Assert.Equal("debug/reset-baseline", row.PerformanceExcludedBy);
            Assert.NotNull(row.PerformanceExcludedAtUtc);
        });
    }

    [Fact]
    public async Task PerformanceSummary_ExcludesBaselineResetRowsByDefault_AndCanIncludeThemForDebug()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        for (var index = 0; index < 8; index++)
        {
            await SeedPerformanceScenarioAsync(
                factory,
                date,
                "Total Bases",
                "Over",
                1.5m,
                2,
                resetDatabase: index == 0,
                capturedAtUtc: new DateTimeOffset(2026, 6, 17, 12, index, 0, TimeSpan.Zero));
        }

        var resetResponse = await client.PostAsync("/api/debug/recommendations/performance/reset-baseline?dryRun=false&take=6", null);
        await EnsureSuccessWithBodyAsync(resetResponse);

        var defaultResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted");
        var includeExcludedResponse = await client.GetAsync("/api/markets/performance-summary?gradingMode=latestCompleted&includeExcluded=true");

        await EnsureSuccessWithBodyAsync(defaultResponse);
        await EnsureSuccessWithBodyAsync(includeExcludedResponse);
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        var includeExcludedJson = await includeExcludedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, defaultJson.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(6, defaultJson.GetProperty("dataAvailability").GetProperty("excludedFromPerformanceRows").GetInt32());
        Assert.Equal(8, includeExcludedJson.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(6, includeExcludedJson.GetProperty("dataAvailability").GetProperty("includedExcludedRows").GetInt32());
    }

    [Fact]
    public async Task PerformanceResetBaseline_CandidateOnly_SelectsSameVisibleGradedRowsAsPublicCandidatePerformance()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        var previouslyExcludedCandidateIds = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            var seeded = await SeedPerformanceScenarioAsync(
                factory,
                date,
                "Total Bases",
                "Over",
                1.5m,
                1,
                resetDatabase: index == 0,
                rankingScore: 100 + index);
            previouslyExcludedCandidateIds.Add(seeded.RecommendationSnapshotId);
        }

        var candidateIds = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            var seeded = await SeedPerformanceScenarioAsync(
                factory,
                date,
                "Total Bases",
                "Over",
                1.5m,
                1,
                resetDatabase: false,
                rankingScore: 10 + index);
            candidateIds.Add(seeded.RecommendationSnapshotId);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pollutedRows = await dbContext.RecommendationSnapshots
                .Where(row => previouslyExcludedCandidateIds.Contains(row.Id))
                .ToArrayAsync();
            foreach (var row in pollutedRows)
            {
                row.IsExcludedFromPerformance = true;
                row.PerformanceExclusionReason = "EarlierWrongReset";
                row.PerformanceExcludedAtUtc = DateTimeOffset.UtcNow;
                row.PerformanceExcludedBy = "test";
            }

            await dbContext.SaveChangesAsync();
        }

        var beforeResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&candidateOnly=true&gradingMode=latestCompleted");
        await EnsureSuccessWithBodyAsync(beforeResponse);
        var beforeJson = await beforeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, beforeJson.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());

        var dryRunResponse = await client.PostAsync("/api/debug/recommendations/performance/reset-baseline?sport=MLB&candidateOnly=true&gradingMode=latestCompleted&gradedOnly=true&take=6&reason=PerformanceBaselineReset&dryRun=true", null);
        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var dryRunJson = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        var dryRunIds = dryRunJson.GetProperty("recommendationSnapshotIds")
            .EnumerateArray()
            .Select(row => row.GetGuid())
            .ToArray();
        Assert.Equal("PerformanceSummaryFilteredRows", dryRunJson.GetProperty("selectionSource").GetString());
        Assert.Equal(6, dryRunJson.GetProperty("gradedRowsAvailableBeforeTake").GetInt32());
        Assert.Equal(6, dryRunJson.GetProperty("alreadyExcludedRowsSkipped").GetInt32());
        Assert.Equal(6, dryRunJson.GetProperty("publicPerformanceBeforeReset").GetProperty("losses").GetInt32());
        Assert.Equal(candidateIds.Order(), dryRunIds.Order());
        Assert.DoesNotContain(dryRunIds, previouslyExcludedCandidateIds.Contains);

        var executeResponse = await client.PostAsync("/api/debug/recommendations/performance/reset-baseline?sport=MLB&candidateOnly=true&gradingMode=latestCompleted&gradedOnly=true&take=6&reason=PerformanceBaselineReset&dryRun=false", null);
        await EnsureSuccessWithBodyAsync(executeResponse);
        var afterResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&candidateOnly=true&gradingMode=latestCompleted");
        await EnsureSuccessWithBodyAsync(afterResponse);
        var afterJson = await afterResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, afterJson.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(12, afterJson.GetProperty("dataAvailability").GetProperty("excludedFromPerformanceRows").GetInt32());
    }

    [Fact]
    public async Task ResultCoverage_LabelsExcludedPerformanceRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        var seeded = await SeedPerformanceScenarioAsync(factory, date, "Total Bases", "Over", 1.5m, 2);
        var resetResponse = await client.PostAsync("/api/debug/recommendations/performance/reset-baseline?dryRun=false&take=1&reason=DiagnosticReset", null);
        await EnsureSuccessWithBodyAsync(resetResponse);

        var response = await client.GetAsync($"/api/debug/recommendations/ledger/result-coverage?recommendationSnapshotId={seeded.RecommendationSnapshotId}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var capturedPick = json.GetProperty("resultCoverageExamples")[0].GetProperty("capturedPick");
        Assert.True(capturedPick.GetProperty("isExcludedFromPerformance").GetBoolean());
        Assert.Equal("DiagnosticReset", capturedPick.GetProperty("performanceExclusionReason").GetString());
    }

    [Fact]
    public async Task PerformanceSummary_GradesUniqueSameNameResult_WhenInternalPlayerIdsDiffer()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Under",
            6.5m,
            4,
            resultUsesDifferentPlayerIdWithSameName: true);

        var response = await client.GetAsync("/api/markets/performance-summary?candidateOnly=true&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("gradedPicks").GetInt32());
        var overall = json.GetProperty("groups").GetProperty("overall")[0];
        Assert.Equal(1, overall.GetProperty("wins").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("matchedViaNormalizedPlayerNameDateStatRows").GetInt32());
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("matchedViaExactPlayerIdDateStatRows").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_StartDateEndDateAliases_FilterAndExposeExactMatchCounts()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var olderDate = new DateOnly(2026, 6, 18);
        var targetDate = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(factory, olderDate, "Total Bases", "Over", 1.5m, 2);
        await SeedPerformanceScenarioAsync(
            factory,
            targetDate,
            "Pitcher Strikeouts",
            "Under",
            6.5m,
            4,
            resetDatabase: false);

        var response = await client.GetAsync($"/api/markets/performance-summary?sport=MLB&startDate={targetDate:yyyy-MM-dd}&endDate={targetDate:yyyy-MM-dd}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dataAvailability = json.GetProperty("dataAvailability");
        Assert.Equal(1, dataAvailability.GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, dataAvailability.GetProperty("wonRowsForCompletedDate").GetInt32());
        Assert.Equal(1, dataAvailability.GetProperty("matchedViaExactPlayerIdDateStatRows").GetInt32());
        Assert.Equal(0, dataAvailability.GetProperty("noResultPicks").GetInt32());
        Assert.Equal(targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), json.GetProperty("filters").GetProperty("startDate").GetString());
        Assert.Equal(targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), json.GetProperty("filters").GetProperty("endDate").GetString());
    }

    [Fact]
    public async Task PerformanceSummary_GradesEquivalentPropKeys_WhenPropTypeIdsDiffer()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Under",
            1.5m,
            0,
            resultUsesDifferentPropTypeIdWithEquivalentName: true);

        var response = await client.GetAsync($"/api/markets/performance-summary?sport=MLB&startDate={date:yyyy-MM-dd}&endDate={date:yyyy-MM-dd}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dataAvailability = json.GetProperty("dataAvailability");
        Assert.Equal(1, dataAvailability.GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, dataAvailability.GetProperty("wonRowsForCompletedDate").GetInt32());
        Assert.Equal(1, dataAvailability.GetProperty("matchedViaExactPlayerIdDateStatRows").GetInt32());
        Assert.Equal(0, dataAvailability.GetProperty("noResultPicks").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_AllHistory_RemainsCumulativeWhenNewerSmallSlateExists()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var olderDate = new DateOnly(2026, 6, 19);
        var newerDate = new DateOnly(2026, 6, 20);
        await SeedPerformanceScenarioAsync(
            factory,
            olderDate,
            "Pitcher Strikeouts",
            "Under",
            6.5m,
            4m,
            provider: "PrizePicks",
            estimatedEv: 0.1m);
        await SeedPerformanceScenarioAsync(
            factory,
            olderDate,
            "Pitcher Strikeouts",
            "Under",
            6.5m,
            8m,
            provider: "Underdog",
            resetDatabase: false,
            estimatedEv: 0.2m);
        await SeedPerformanceScenarioAsync(
            factory,
            newerDate,
            "Pitcher Strikeouts",
            "Over",
            6.5m,
            7m,
            provider: "Betr Picks",
            resetDatabase: false,
            estimatedEv: 0.3m);

        var latestResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestGraded");
        var allHistoryResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=allHistory");
        var olderDateResponse = await client.GetAsync($"/api/markets/performance-summary?sport=MLB&startDate={olderDate:yyyy-MM-dd}&endDate={olderDate:yyyy-MM-dd}");

        await EnsureSuccessWithBodyAsync(latestResponse);
        await EnsureSuccessWithBodyAsync(allHistoryResponse);
        await EnsureSuccessWithBodyAsync(olderDateResponse);
        var latestJson = await latestResponse.Content.ReadFromJsonAsync<JsonElement>();
        var allHistoryJson = await allHistoryResponse.Content.ReadFromJsonAsync<JsonElement>();
        var olderDateJson = await olderDateResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("latestGraded", latestJson.GetProperty("gradingMode").GetString());
        Assert.Equal(1, latestJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, latestJson.GetProperty("summary").GetProperty("won").GetInt32());
        Assert.Equal(newerDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), latestJson.GetProperty("dataAvailability").GetProperty("latestGradedDate").GetString());

        Assert.Equal("allHistory", allHistoryJson.GetProperty("gradingMode").GetString());
        Assert.Equal(3, allHistoryJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(2, allHistoryJson.GetProperty("summary").GetProperty("won").GetInt32());
        Assert.Equal(1, allHistoryJson.GetProperty("summary").GetProperty("lost").GetInt32());

        Assert.Equal(2, olderDateJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, olderDateJson.GetProperty("summary").GetProperty("won").GetInt32());
        Assert.Equal(1, olderDateJson.GetProperty("summary").GetProperty("lost").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_LatestGradedSkipsPartialLatestResultCoverage()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var completeDate = new DateOnly(2026, 6, 22);
        var partialDate = new DateOnly(2026, 6, 23);
        await SeedPerformanceScenarioAsync(
            factory,
            completeDate,
            "Pitcher Strikeouts",
            "Over",
            6.5m,
            7m,
            provider: "PrizePicks");
        await SeedPerformanceScenarioAsync(
            factory,
            partialDate,
            "Pitcher Strikeouts",
            "Over",
            6.5m,
            7m,
            provider: "Underdog",
            resetDatabase: false);
        await SeedMlbCoverageStateAsync(factory, completeDate, "Complete", importedResults: 2930, distinctPlayers: 274, distinctPropTypes: 16);
        await SeedMlbCoverageStateAsync(factory, partialDate, "Partial", importedResults: 220, distinctPlayers: 20, distinctPropTypes: 11);

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestGraded");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dataAvailability = json.GetProperty("dataAvailability");
        var resultCoverage = json.GetProperty("diagnostics").GetProperty("resultCoverage");
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(completeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dataAvailability.GetProperty("latestGradedDate").GetString());
        Assert.Equal(partialDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dataAvailability.GetProperty("latestResultsImportDate").GetString());
        Assert.Equal("Partial", dataAvailability.GetProperty("latestCoverageStatus").GetString());
        Assert.Contains(dataAvailability.GetProperty("skippedDates").EnumerateArray(), row => row.GetString() == partialDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.False(dataAvailability.GetProperty("resultCoverageComplete").GetBoolean());
        Assert.Equal("Partial", resultCoverage.GetProperty("latestCoverageStatus").GetString());
        Assert.Equal(completeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), resultCoverage.GetProperty("selectedGradedDate").GetString());
        Assert.Equal("Complete", resultCoverage.GetProperty("selectedGradedDateCoverageStatus").GetString());
        Assert.True(resultCoverage.GetProperty("resultCoverageCompleteForSelectedDate").GetBoolean());
        Assert.True(resultCoverage.GetProperty("latestDateWasSkipped").GetBoolean());
        Assert.Contains(resultCoverage.GetProperty("skippedDates").EnumerateArray(), row => row.GetString() == partialDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task PerformanceSummary_DefaultsToCurrentModelEra_AndQuarantinesLegacyRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Under",
            1.5m,
            2m,
            provider: "LegacyBook",
            bookmaker: "LegacyBook",
            resetDatabase: false,
            modelEra: null);

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=allHistory");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TopPicksModelOptions.DefaultLikelyHitV1ModelEra, json.GetProperty("modelEra").GetString());
        Assert.False(json.GetProperty("includeLegacy").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("won").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("excludedLegacyRows").GetInt32());
        Assert.Contains(
            json.GetProperty("diagnostics").GetProperty("modelEraBreakdown").EnumerateArray(),
            row => row.GetProperty("modelEra").GetString() == "LegacyOrUnknown");
    }

    [Fact]
    public async Task PerformanceSummary_IncludeLegacy_BroadensEraQuarantineWithDiagnostics()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Under",
            1.5m,
            2m,
            provider: "LegacyBook",
            bookmaker: "LegacyBook",
            resetDatabase: false,
            modelEra: null);

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=allHistory&includeLegacy=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("includeLegacy").GetBoolean());
        Assert.Equal(2, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("won").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("lost").GetInt32());
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("excludedLegacyRows").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_ModelEraFilter_UsesRequestedEra()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        const string alternateEra = "LikelyHitV1_TestAlternateEra";
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Under",
            5.5m,
            4m,
            provider: "AltBook",
            bookmaker: "AltBook",
            resetDatabase: false,
            modelEra: alternateEra);

        var response = await client.GetAsync($"/api/markets/performance-summary?sport=MLB&gradingMode=allHistory&modelEra={alternateEra}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(alternateEra, json.GetProperty("modelEra").GetString());
        Assert.Equal(alternateEra, json.GetProperty("requestedModelEra").GetString());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("won").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_DefaultIncludesSportsbookTopPicksThatAreNotEntryActionable()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Under",
            1.5m,
            0m,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false);

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("won").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("rowsExcludedNonPlayable").GetInt32());
        var providerCategoryGroup = Assert.Single(json.GetProperty("groups").GetProperty("byProviderCategory").EnumerateArray());
        Assert.Equal("Sportsbook", providerCategoryGroup.GetProperty("providerCategory").GetString());
        Assert.Equal(1, providerCategoryGroup.GetProperty("totalCandidatePicks").GetInt32());
        Assert.Equal(1, providerCategoryGroup.GetProperty("gradedPicks").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_GuardedEraExcludesLegacyCandidateOnlyRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m,
            isCandidate: true,
            isTopPick: false);

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("excludedNonPublicCurrentEraRows").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("currentEraPollutedLedgerRows").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_GuardedEraExcludesInvalidSideRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "None",
            1.5m,
            2m);

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("excludedInvalidSideRows").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_GuardedEraExcludesSportsbookRowsMissingBookmakerOrProvider()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Under",
            1.5m,
            0m,
            provider: string.Empty,
            bookmaker: null,
            providerCategory: "Sportsbook",
            isActionableForUser: false);

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("excludedMissingBookmakerRows").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_GuardedEraDedupesExactPublicPickKeys()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var original = await dbContext.RecommendationSnapshots.AsNoTracking().SingleAsync();
            dbContext.RecommendationSnapshots.Add(new RecommendationSnapshot
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                FirstSeenAtUtc = original.FirstSeenAtUtc.AddMinutes(10),
                LastSeenAtUtc = original.LastSeenAtUtc.AddMinutes(10),
                SeenCount = 1,
                CapturedAtUtc = original.CapturedAtUtc.AddMinutes(10),
                RefreshRunId = null,
                PredictionRunId = original.PredictionRunId,
                Sport = original.Sport,
                SlateDate = original.SlateDate,
                ExpectedResultDate = original.ExpectedResultDate,
                ResultDateSource = original.ResultDateSource,
                EventId = original.EventId,
                EventStartTimeUtc = original.EventStartTimeUtc,
                CanonicalPlayerId = original.CanonicalPlayerId,
                PlayerName = original.PlayerName,
                NormalizedPlayerName = original.NormalizedPlayerName,
                PropTypeId = original.PropTypeId,
                PropType = original.PropType,
                NormalizedPropType = original.NormalizedPropType,
                Side = original.Side,
                Line = original.Line,
                Provider = original.Provider,
                Bookmaker = original.Bookmaker,
                ProviderCategory = original.ProviderCategory,
                EntryProvider = original.EntryProvider,
                EntryCompatibilityGroup = original.EntryCompatibilityGroup,
                HitChance = original.HitChance,
                ImpliedProbability = original.ImpliedProbability,
                ProbabilityEdge = original.ProbabilityEdge,
                EstimatedEv = original.EstimatedEv,
                TrustLevel = original.TrustLevel,
                SampleWarning = original.SampleWarning,
                Warning = original.Warning,
                HistoricalSampleSize = original.HistoricalSampleSize,
                StrategyClassification = original.StrategyClassification,
                StrategyTier = original.StrategyTier,
                ModelVersion = original.ModelVersion,
                RecommendationContractVersion = original.RecommendationContractVersion,
                ProbabilityModelVersion = original.ProbabilityModelVersion,
                ModelEra = original.ModelEra,
                RankingScore = original.RankingScore + 1m,
                IsCandidate = original.IsCandidate,
                IsTopPick = original.IsTopPick,
                IsWatchlist = original.IsWatchlist,
                IsActionableForUser = original.IsActionableForUser,
                LifecycleStatus = original.LifecycleStatus,
                GradeStatus = "Pending"
            });
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("rowsAfterPerformanceEligibility").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("excludedDuplicateLedgerRows").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_GuardedEraCountsSportsbookBookmakerFanoutAsOneOpportunity()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false,
            estimatedEv: 0.06m);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var original = await dbContext.RecommendationSnapshots.SingleAsync();
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(
                dbContext,
                original,
                provider: "DraftKings",
                bookmaker: "DraftKings",
                estimatedEv: 0.12m,
                rankingScoreDelta: 2m));
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(
                dbContext,
                original,
                provider: "BetMGM",
                bookmaker: "BetMGM",
                estimatedEv: 0.09m,
                rankingScoreDelta: 1m));
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(
                dbContext,
                original,
                provider: "BetRivers",
                bookmaker: "BetRivers",
                estimatedEv: 0.08m,
                rankingScoreDelta: 0.5m));
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Opportunity", json.GetProperty("primaryGrain").GetString());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("won").GetInt32());
        Assert.Equal(1m, json.GetProperty("summary").GetProperty("simulatedRoi").GetDecimal());
        Assert.Equal(0.12m, json.GetProperty("summary").GetProperty("avgEvAtRecommendationTime").GetDecimal());
        var providerLineDiagnostics = json.GetProperty("providerLineDiagnostics");
        Assert.Equal(4, providerLineDiagnostics.GetProperty("providerLineCount").GetInt32());
        Assert.Equal(1, providerLineDiagnostics.GetProperty("dedupedProviderLineCount").GetInt32());
        Assert.Equal(3, providerLineDiagnostics.GetProperty("duplicateProviderLineCount").GetInt32());
        Assert.True(providerLineDiagnostics.GetProperty("sportsbookOpportunityKeyExcludesBookmaker").GetBoolean());
    }

    [Fact]
    public async Task PerformanceResults_DedupesSportsbookBookmakerFanoutToOneOpportunityRow()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Under",
            6.5m,
            4m,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false,
            estimatedEv: 0.05m);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var original = await dbContext.RecommendationSnapshots.SingleAsync();
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(dbContext, original, provider: "DraftKings", bookmaker: "DraftKings", estimatedEv: 0.11m, rankingScoreDelta: 2m));
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(dbContext, original, provider: "BetMGM", bookmaker: "BetMGM", estimatedEv: 0.09m, rankingScoreDelta: 1m));
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(dbContext, original, provider: "BetRivers", bookmaker: "BetRivers", estimatedEv: 0.07m, rankingScoreDelta: 0.5m));
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/markets/performance-results?sport=MLB&date={date:yyyy-MM-dd}&includeDiagnostics=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Opportunity", json.GetProperty("primaryGrain").GetString());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(4, json.GetProperty("summary").GetProperty("providerLineCount").GetInt32());
        Assert.Single(json.GetProperty("rows").EnumerateArray());
        var row = json.GetProperty("rows")[0];
        Assert.Equal(4, row.GetProperty("providerLineCount").GetInt32());
        Assert.Equal(4, row.GetProperty("providerLineBookmakers").GetArrayLength());
        Assert.Equal(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.GetProperty("eventDate").GetString());
        Assert.Equal(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.GetProperty("expectedResultDate").GetString());
        Assert.Equal(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.GetProperty("resultDate").GetString());
        Assert.Equal(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.GetProperty("gradedDate").GetString());
        Assert.Equal(JsonValueKind.String, row.GetProperty("ledgerCapturedAtUtc").ValueKind);
    }

    [Fact]
    public async Task PerformanceSummary_All_AggregatesMlbAndSoccerWithSportBreakdown()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 29);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Shots",
            "Over",
            1.5m,
            3m,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false,
            resetDatabase: false,
            sportCode: "SOCCER");

        var response = await client.GetAsync("/api/markets/performance-summary?sport=All&gradingMode=allHistory&includeNoResultDiagnostics=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ALL", json.GetProperty("requestedSport").GetString());
        Assert.Equal("ALL", json.GetProperty("effectiveSport").GetString());
        Assert.Equal(2, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        var bySport = json.GetProperty("groups").GetProperty("bySport").EnumerateArray().ToArray();
        Assert.Contains(bySport, row =>
            row.GetProperty("groupKey").GetString() == "MLB" &&
            row.GetProperty("gradedPicks").GetInt32() == 1);
        Assert.Contains(bySport, row =>
            row.GetProperty("groupKey").GetString() == "SOCCER" &&
            row.GetProperty("gradedPicks").GetInt32() == 1);
    }

    [Fact]
    public async Task PerformanceResults_All_ReturnsMixedSportRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 29);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Shots",
            "Over",
            1.5m,
            3m,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false,
            resetDatabase: false,
            sportCode: "SOCCER");

        var response = await client.GetAsync($"/api/markets/performance-results?sport=All&date={date:yyyy-MM-dd}&includeDiagnostics=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ALL", json.GetProperty("sport").GetString());
        Assert.Equal(2, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        var rows = json.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Contains(rows, row => row.GetProperty("sport").GetString() == "MLB");
        Assert.Contains(rows, row => row.GetProperty("sport").GetString() == "SOCCER");
    }

    [Fact]
    public async Task PerformanceSummary_All_EqualsMlbWhenSoccerHasNoGradedRows_AndKeepsOpportunityGrain()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 29);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Under",
            6.5m,
            4m,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false,
            estimatedEv: 0.12m);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Hits",
            "Over",
            0.5m,
            null,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false,
            estimatedEv: 0.06m,
            resetDatabase: false);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var original = await dbContext.RecommendationSnapshots.SingleAsync(row => row.Sport == "MLB" && row.PropType == "Pitcher Strikeouts");
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(dbContext, original, provider: "DraftKings", bookmaker: "DraftKings", estimatedEv: 0.11m, rankingScoreDelta: 2m));
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(dbContext, original, provider: "BetMGM", bookmaker: "BetMGM", estimatedEv: 0.10m, rankingScoreDelta: 1m));
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(dbContext, original, provider: "BetRivers", bookmaker: "BetRivers", estimatedEv: 0.09m, rankingScoreDelta: 0.5m));
            var excluded = CloneRecommendationSnapshot(dbContext, original, provider: "Caesars", bookmaker: "Caesars", estimatedEv: 0.20m, rankingScoreDelta: 3m);
            excluded.IsExcludedFromPerformance = true;
            excluded.PerformanceExclusionReason = "TestExcluded";
            dbContext.RecommendationSnapshots.Add(excluded);
            await dbContext.SaveChangesAsync();
        }

        var mlbResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=allHistory&includeNoResultDiagnostics=true");
        var allResponse = await client.GetAsync("/api/markets/performance-summary?sport=All&gradingMode=allHistory&includeNoResultDiagnostics=true");

        await EnsureSuccessWithBodyAsync(mlbResponse);
        await EnsureSuccessWithBodyAsync(allResponse);
        var mlb = await mlbResponse.Content.ReadFromJsonAsync<JsonElement>();
        var all = await allResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(mlb.GetProperty("summary").GetProperty("gradedPicks").GetInt32(), all.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(mlb.GetProperty("summary").GetProperty("won").GetInt32(), all.GetProperty("summary").GetProperty("won").GetInt32());
        Assert.Equal(mlb.GetProperty("summary").GetProperty("lost").GetInt32(), all.GetProperty("summary").GetProperty("lost").GetInt32());
        Assert.Equal(mlb.GetProperty("summary").GetProperty("pushed").GetInt32(), all.GetProperty("summary").GetProperty("pushed").GetInt32());
        Assert.Equal(mlb.GetProperty("overall").GetProperty("gradedOpportunities").GetInt32(), all.GetProperty("overall").GetProperty("gradedOpportunities").GetInt32());
        Assert.Equal(mlb.GetProperty("yesterday").GetProperty("gradedOpportunities").GetInt32(), all.GetProperty("yesterday").GetProperty("gradedOpportunities").GetInt32());
        Assert.Equal(1, all.GetProperty("summary").GetProperty("gradedPicks").GetInt32());

        var providerLineDiagnostics = all.GetProperty("providerLineDiagnostics");
        Assert.Equal(5, providerLineDiagnostics.GetProperty("providerLineCount").GetInt32());
        Assert.Equal(2, providerLineDiagnostics.GetProperty("dedupedProviderLineCount").GetInt32());
        Assert.Equal(1, all.GetProperty("dataAvailability").GetProperty("excludedFromPerformanceRows").GetInt32());

        var bySport = all.GetProperty("groups").GetProperty("bySport").EnumerateArray().ToArray();
        var sportGradedTotal = bySport.Sum(row => row.GetProperty("gradedPicks").GetInt32());
        Assert.Equal(all.GetProperty("summary").GetProperty("gradedPicks").GetInt32(), sportGradedTotal);
        var mlbSportGroup = Assert.Single(bySport);
        Assert.Equal("MLB", mlbSportGroup.GetProperty("groupKey").GetString());
        Assert.False(mlbSportGroup.TryGetProperty("totalPicks", out _));
        Assert.Equal(2, mlbSportGroup.GetProperty("totalCandidatePicks").GetInt32());
        Assert.Equal(1, mlbSportGroup.GetProperty("gradedPicks").GetInt32());
        Assert.Equal(
            mlbSportGroup.GetProperty("gradedPicks").GetInt32() +
            mlbSportGroup.GetProperty("noResults").GetInt32() +
            mlbSportGroup.GetProperty("unmatched").GetInt32() +
            mlbSportGroup.GetProperty("pending").GetInt32(),
            mlbSportGroup.GetProperty("totalCandidatePicks").GetInt32());
        Assert.Equal(1, mlbSportGroup.GetProperty("noResults").GetInt32());
    }

    [Fact]
    public async Task PerformanceSummary_SoccerWithoutRows_ReturnsDiagnosticsInsteadOfUnsupportedResponse()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        var response = await client.GetAsync("/api/markets/performance-summary?sport=SOCCER&includeNoResultDiagnostics=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SOCCER", json.GetProperty("effectiveSport").GetString());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.True(json.TryGetProperty("dataAvailability", out _));
        Assert.True(json.TryGetProperty("performanceGrainDiagnostics", out _));
        Assert.False(json.TryGetProperty("unavailableReason", out _));
    }

    [Fact]
    public async Task PerformanceSummary_DfsDifferentEntryProvidersStaySeparateWhileSameProviderDuplicatesDedupe()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m,
            provider: "PrizePicks",
            bookmaker: "PrizePicks",
            providerCategory: "DFS",
            isActionableForUser: true,
            estimatedEv: 0.06m);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var original = await dbContext.RecommendationSnapshots.SingleAsync();
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(
                dbContext,
                original,
                provider: "PrizePicks",
                bookmaker: "PrizePicks",
                estimatedEv: 0.08m,
                rankingScoreDelta: 1m));
            dbContext.RecommendationSnapshots.Add(CloneRecommendationSnapshot(
                dbContext,
                original,
                provider: "Underdog",
                bookmaker: "Underdog",
                estimatedEv: 0.07m,
                rankingScoreDelta: 0.5m));
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestCompleted");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(2, json.GetProperty("summary").GetProperty("won").GetInt32());
        var providerLineDiagnostics = json.GetProperty("providerLineDiagnostics");
        Assert.Equal(3, providerLineDiagnostics.GetProperty("providerLineCount").GetInt32());
        Assert.Equal(2, providerLineDiagnostics.GetProperty("dedupedProviderLineCount").GetInt32());
        Assert.Equal(1, providerLineDiagnostics.GetProperty("duplicateProviderLineCount").GetInt32());
        Assert.True(providerLineDiagnostics.GetProperty("dfsOpportunityKeyIncludesEntryProvider").GetBoolean());
    }

    [Fact]
    public async Task PerformanceSummary_AllHistoryIncludesLatestGradedSlate_AndResultsUseSameGuardedEligibility()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var priorDate = new DateOnly(2026, 6, 17);
        var latestDate = new DateOnly(2026, 6, 18);

        for (var index = 0; index < 2; index++)
        {
            await SeedPerformanceScenarioAsync(
                factory,
                priorDate,
                "Total Bases",
                "Over",
                1.5m + index,
                10m,
                provider: "FanDuel",
                bookmaker: "FanDuel",
                providerCategory: "Sportsbook",
                isActionableForUser: false,
                resetDatabase: index == 0);
        }

        for (var index = 0; index < 3; index++)
        {
            await SeedPerformanceScenarioAsync(
                factory,
                latestDate,
                "Total Bases",
                "Over",
                1.5m + index,
                10m,
                provider: "FanDuel",
                bookmaker: "FanDuel",
                providerCategory: "Sportsbook",
                isActionableForUser: false,
                resetDatabase: false);
        }

        await SeedPerformanceScenarioAsync(
            factory,
            latestDate,
            "Total Bases",
            "Over",
            9.5m,
            10m,
            provider: "FanDuel",
            bookmaker: "FanDuel",
            providerCategory: "Sportsbook",
            isActionableForUser: false,
            isTopPick: false,
            resetDatabase: false);

        var overallResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=allHistory");
        var latestResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestGraded");
        var defaultResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB");
        var resultsResponse = await client.GetAsync($"/api/markets/performance-results?sport=MLB&date={latestDate:yyyy-MM-dd}&includeDiagnostics=true");

        await EnsureSuccessWithBodyAsync(overallResponse);
        await EnsureSuccessWithBodyAsync(latestResponse);
        await EnsureSuccessWithBodyAsync(defaultResponse);
        await EnsureSuccessWithBodyAsync(resultsResponse);
        var overallJson = await overallResponse.Content.ReadFromJsonAsync<JsonElement>();
        var latestJson = await latestResponse.Content.ReadFromJsonAsync<JsonElement>();
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        var resultsJson = await resultsResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Opportunity", overallJson.GetProperty("primaryGrain").GetString());
        Assert.Equal("Opportunity", latestJson.GetProperty("primaryGrain").GetString());
        Assert.Equal("Opportunity", resultsJson.GetProperty("primaryGrain").GetString());
        Assert.Equal(5, overallJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(3, latestJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(3, resultsJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(3, resultsJson.GetProperty("rows").GetArrayLength());
        Assert.True(overallJson.GetProperty("overall").GetProperty("includesLatestGradedSlate").GetBoolean());
        Assert.Equal(3, overallJson.GetProperty("yesterday").GetProperty("gradedOpportunities").GetInt32());
        Assert.Equal(5, overallJson.GetProperty("overall").GetProperty("gradedOpportunities").GetInt32());
        Assert.Equal(5, overallJson.GetProperty("performanceGrainDiagnostics").GetProperty("overallGradedOpportunities").GetInt32());
        Assert.Equal(3, overallJson.GetProperty("performanceGrainDiagnostics").GetProperty("yesterdayGradedOpportunities").GetInt32());
        Assert.Equal(2, overallJson.GetProperty("performanceGrainDiagnostics").GetProperty("uniqueGradedDateCount").GetInt32());
        Assert.Equal(2, overallJson.GetProperty("performanceGrainDiagnostics").GetProperty("gradedOpportunitiesByDate").GetArrayLength());
        Assert.Equal(5, overallJson.GetProperty("groups").GetProperty("overall")[0].GetProperty("gradedPicks").GetInt32());
        Assert.Equal(5, overallJson.GetProperty("groups").GetProperty("byPropType")[0].GetProperty("gradedPicks").GetInt32());
        Assert.Equal("latestGraded", defaultJson.GetProperty("gradingMode").GetString());
        Assert.Equal(3, defaultJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(3, defaultJson.GetProperty("yesterday").GetProperty("gradedOpportunities").GetInt32());
        Assert.Equal(5, defaultJson.GetProperty("overall").GetProperty("gradedOpportunities").GetInt32());
        Assert.Equal(2, defaultJson.GetProperty("performanceGrainDiagnostics").GetProperty("uniqueGradedDateCount").GetInt32());
        Assert.Equal(5, defaultJson.GetProperty("dataAvailability").GetProperty("overallGradedOpportunities").GetInt32());

        var filteredOverallResponse = await client.GetAsync($"/api/markets/performance-summary?sport=MLB&gradingMode=allHistory&toDate={priorDate:yyyy-MM-dd}");

        await EnsureSuccessWithBodyAsync(filteredOverallResponse);
        var filteredOverallJson = await filteredOverallResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, filteredOverallJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.False(filteredOverallJson.GetProperty("overall").GetProperty("includesLatestGradedSlate").GetBoolean());
        Assert.Equal(latestDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), filteredOverallJson.GetProperty("performanceGrainDiagnostics").GetProperty("latestGradedDate").GetString());
        Assert.Equal(3, filteredOverallJson.GetProperty("performanceGrainDiagnostics").GetProperty("yesterdayGradedOpportunities").GetInt32());
        Assert.Equal(2, filteredOverallJson.GetProperty("performanceGrainDiagnostics").GetProperty("overallGradedOpportunities").GetInt32());
        Assert.Contains("after the requested toDate", filteredOverallJson.GetProperty("overall").GetProperty("excludedLatestGradedSlateReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PerformanceSummary_AllHistoryEqualsLatestGradedWhenOnlyLatestSlateExists()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var latestDate = new DateOnly(2026, 6, 18);

        for (var index = 0; index < 3; index++)
        {
            await SeedPerformanceScenarioAsync(
                factory,
                latestDate,
                "Total Bases",
                "Over",
                1.5m + index,
                10m,
                provider: "FanDuel",
                bookmaker: "FanDuel",
                providerCategory: "Sportsbook",
                isActionableForUser: false,
                resetDatabase: index == 0);
        }

        var overallResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=allHistory");
        var latestResponse = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=latestGraded");

        await EnsureSuccessWithBodyAsync(overallResponse);
        await EnsureSuccessWithBodyAsync(latestResponse);
        var overallJson = await overallResponse.Content.ReadFromJsonAsync<JsonElement>();
        var latestJson = await latestResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Opportunity", overallJson.GetProperty("primaryGrain").GetString());
        Assert.Equal("Opportunity", latestJson.GetProperty("primaryGrain").GetString());
        Assert.Equal(3, overallJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(3, latestJson.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(3, overallJson.GetProperty("overall").GetProperty("gradedOpportunities").GetInt32());
        Assert.Equal(3, overallJson.GetProperty("yesterday").GetProperty("gradedOpportunities").GetInt32());
        Assert.True(overallJson.GetProperty("overall").GetProperty("includesLatestGradedSlate").GetBoolean());
        Assert.Equal(1, overallJson.GetProperty("performanceGrainDiagnostics").GetProperty("uniqueGradedDateCount").GetInt32());
        Assert.Equal(1, overallJson.GetProperty("performanceGrainDiagnostics").GetProperty("gradedOpportunitiesByDate").GetArrayLength());
        Assert.Equal(latestDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), overallJson.GetProperty("performanceGrainDiagnostics").GetProperty("overallDateRange").GetProperty("fromDate").GetString());
        Assert.Equal(latestDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), overallJson.GetProperty("performanceGrainDiagnostics").GetProperty("overallDateRange").GetProperty("toDate").GetString());
    }

    [Fact]
    public void RecommendationLedgerWriter_IdempotencyKeyUsesExactPublicPickIdentity()
    {
        var date = new DateOnly(2026, 6, 17);
        var playerId = Guid.NewGuid();
        var propTypeId = Guid.NewGuid();
        var first = CreateLedgerCandidate(date, playerId, propTypeId, refreshRunId: Guid.NewGuid(), strategyClassification: "Candidate", isCandidate: true);
        var second = CreateLedgerCandidate(date, playerId, propTypeId, refreshRunId: Guid.NewGuid(), strategyClassification: "LegacyCandidate", isCandidate: false);
        var differentLine = CreateLedgerCandidate(date, playerId, propTypeId, refreshRunId: Guid.NewGuid(), strategyClassification: "Candidate", isCandidate: true, line: 2.5m);

        Assert.Equal(RecommendationLedgerWriter.BuildIdempotencyKey(first), RecommendationLedgerWriter.BuildIdempotencyKey(second));
        Assert.NotEqual(RecommendationLedgerWriter.BuildIdempotencyKey(first), RecommendationLedgerWriter.BuildIdempotencyKey(differentLine));
    }

    [Fact]
    public async Task PerformanceSummary_WarnsWhenOnlyLegacyRowsExist()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 17);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Total Bases",
            "Over",
            1.5m,
            2m,
            modelEra: null);

        var response = await client.GetAsync("/api/markets/performance-summary?sport=MLB&gradingMode=allHistory");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal(1, json.GetProperty("dataAvailability").GetProperty("excludedLegacyRows").GetInt32());
        Assert.Equal(
            "No current-era graded picks are available for the selected date/range. Legacy picks are quarantined by default.",
            json.GetProperty("dataAvailability").GetProperty("warning").GetString());
    }

    [Fact]
    public async Task PerformanceResultMatchAudit_ReportsNormalizedNameFallbackEvidence()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        var date = new DateOnly(2026, 6, 19);
        await SeedPerformanceScenarioAsync(
            factory,
            date,
            "Pitcher Strikeouts",
            "Under",
            6.5m,
            4,
            resultUsesDifferentPlayerIdWithSameName: true);

        var response = await client.GetAsync($"/api/debug/performance/result-match-audit?sport=MLB&date={date:yyyy-MM-dd}");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("capturedRecommendations").GetInt32());
        Assert.Equal(1, summary.GetProperty("matched").GetInt32());
        var row = json.GetProperty("rows")[0];
        Assert.Equal("NormalizedNameDateStatOnly", row.GetProperty("playerMappingStatus").GetString());
        Assert.Equal("NoResult", row.GetProperty("legacyExactIdMatcherOutcome").GetString());
        Assert.Equal("Won", row.GetProperty("currentMatcherOutcome").GetString());
        Assert.Equal(1, row.GetProperty("importedResultChecks").GetProperty("normalizedNameOnlyDateStatRows").GetInt32());
        Assert.True(row.GetProperty("closestCandidateResultRows")[0].GetProperty("whyItDidOrDidNotMatch").GetProperty("wouldMatchCurrentFallback").GetBoolean());
    }

    private static RecommendationLedgerCandidate CreateLedgerCandidate(
        DateOnly date,
        Guid playerId,
        Guid propTypeId,
        Guid refreshRunId,
        string strategyClassification,
        bool isCandidate,
        decimal line = 1.5m)
    {
        return new RecommendationLedgerCandidate(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            RefreshRunId: refreshRunId,
            PredictionRunId: null,
            Sport: "MLB",
            SlateDate: date,
            ExpectedResultDate: date,
            ResultDateSource: "Test",
            EventId: "test-game",
            EventStartTimeUtc: new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero),
            CanonicalPlayerId: playerId,
            PlayerName: "Test Player",
            PropTypeId: propTypeId,
            PropType: "Total Bases",
            Side: "Over",
            Line: line,
            Provider: "FanDuel",
            Bookmaker: "FanDuel",
            ProviderCategory: "Sportsbook",
            EntryProvider: "FanDuel",
            EntryCompatibilityGroup: "FanDuel",
            HitChance: 0.7m,
            ImpliedProbability: 0.55m,
            ProbabilityEdge: 0.15m,
            EstimatedEv: 0.08m,
            TrustLevel: "Strong",
            SampleWarning: null,
            Warning: null,
            HistoricalSampleSize: 20,
            StrategyClassification: strategyClassification,
            StrategyTier: "LikelyHit",
            ModelVersion: LikelyHitModelService.ModelVersion,
            RecommendationContractVersion: LikelyHitModelService.RecommendationContractVersion,
            ProbabilityModelVersion: LikelyHitModelService.ProbabilityModelVersion,
            ModelEra: TopPicksModelOptions.DefaultLikelyHitV1ModelEra,
            RankingScore: 1m,
            IsCandidate: isCandidate,
            IsTopPick: true,
            IsWatchlist: false,
            IsActionableForUser: false,
            LifecycleStatus: "Active");
    }

    private static async Task SeedMlbCoverageStateAsync(
        SafetyTestApplicationFactory factory,
        DateOnly date,
        string coverageStatus,
        int importedResults,
        int distinctPlayers,
        int distinctPropTypes)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await dbContext.MlbResultImportDateStates.SingleOrDefaultAsync(row => row.EventDate == date);
        if (existing is null)
        {
            existing = new MlbResultImportDateState
            {
                Id = Guid.NewGuid(),
                EventDate = date,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.MlbResultImportDateStates.Add(existing);
        }

        existing.Status = coverageStatus == "Complete" ? "succeeded" : "partial";
        existing.ImportedResults = importedResults;
        existing.DistinctPlayers = distinctPlayers;
        existing.DistinctPropTypes = distinctPropTypes;
        existing.CoverageStatus = coverageStatus;
        existing.CoverageReason = "Seeded performance coverage state.";
        existing.LastSuccessfulAt = DateTimeOffset.UtcNow;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    private static async Task<SeededPerformanceScenario> SeedPerformanceScenarioAsync(
        SafetyTestApplicationFactory factory,
        DateOnly date,
        string propType,
        string side,
        decimal line,
        decimal? resultValue,
        string provider = "Underdog",
        string? bookmaker = "Underdog",
        string providerCategory = "DFS",
        string? alternateResultPropType = null,
        decimal? alternateResultValue = null,
        decimal? duplicateMatchingResultValue = null,
        bool isCandidate = true,
        bool isTopPick = true,
        bool isWatchlist = false,
        bool isActionableForUser = true,
        string lifecycleStatus = "Active",
        string? warning = null,
        DateTimeOffset? eventStartTimeUtc = null,
        DateOnly? nearbyResultDate = null,
        decimal? nearbyResultValue = null,
        decimal? sameDateOtherPlayerResultValue = null,
        bool resetDatabase = true,
        bool includeCanonicalPlayerId = true,
        bool includeEventMapping = true,
        DateTimeOffset? capturedAtUtc = null,
        decimal rankingScore = 1m,
        decimal estimatedEv = 0.08m,
        bool resultUsesDifferentPlayerIdWithSameName = false,
        bool resultUsesDifferentPropTypeIdWithEquivalentName = false,
        decimal? additionalSameNameResultValue = null,
        string? modelEra = TopPicksModelOptions.DefaultLikelyHitV1ModelEra,
        string sportCode = "MLB",
        string? leagueCode = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (resetDatabase)
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }

        var normalizedSportCode = string.IsNullOrWhiteSpace(sportCode)
            ? "MLB"
            : sportCode.Trim().ToUpperInvariant();
        var effectiveLeagueCode = string.IsNullOrWhiteSpace(leagueCode)
            ? normalizedSportCode == "SOCCER" ? "World Cup" : "MLB"
            : leagueCode.Trim();
        var sportName = normalizedSportCode == "SOCCER" ? "Soccer" : "Major League Baseball";

        var sport = resetDatabase
            ? null
            : await dbContext.Sports.FirstOrDefaultAsync(row => row.Code == normalizedSportCode);
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Name = sportName, Code = normalizedSportCode };
            dbContext.Sports.Add(sport);
        }

        var league = resetDatabase
            ? null
            : await dbContext.Leagues.FirstOrDefaultAsync(row => row.Code == effectiveLeagueCode && row.SportId == sport.Id);
        if (league is null)
        {
            league = new League { Id = Guid.NewGuid(), SportId = sport.Id, Name = effectiveLeagueCode, Code = effectiveLeagueCode };
            dbContext.Leagues.Add(league);
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            DisplayName = "Test Player",
            FirstName = "Test",
            LastName = "Player"
        };
        var primaryPropType = resetDatabase
            ? null
            : await dbContext.PropTypes.FirstOrDefaultAsync(row => row.SportId == sport.Id && row.Name == propType);
        if (primaryPropType is null)
        {
            primaryPropType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = propType,
                StatKey = propType
            };
            dbContext.PropTypes.Add(primaryPropType);
        }
        dbContext.Players.Add(player);

        var recommendationSnapshotId = Guid.NewGuid();
        dbContext.RecommendationSnapshots.Add(new RecommendationSnapshot
        {
            Id = recommendationSnapshotId,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            FirstSeenAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow.AddHours(-2),
            LastSeenAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow.AddHours(-2),
            CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow.AddHours(-2),
            SeenCount = 1,
            Sport = normalizedSportCode,
            SlateDate = date,
            ExpectedResultDate = date,
            ResultDateSource = "Test",
            EventId = includeEventMapping ? "test-game" : null,
            EventStartTimeUtc = includeEventMapping ? eventStartTimeUtc ?? new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero) : null,
            CanonicalPlayerId = includeCanonicalPlayerId ? player.Id : null,
            PlayerName = player.DisplayName,
            NormalizedPlayerName = "testplayer",
            PropTypeId = primaryPropType.Id,
            PropType = primaryPropType.Name,
            NormalizedPropType = NormalizeKey(primaryPropType.Name),
            Side = side,
            Line = line,
            Provider = provider,
            Bookmaker = bookmaker,
            ProviderCategory = providerCategory,
            EntryProvider = provider,
            EntryCompatibilityGroup = provider,
            HitChance = 0.6m,
            ProbabilityEdge = 0.05m,
            EstimatedEv = estimatedEv,
            TrustLevel = "Medium",
            Warning = warning,
            HistoricalSampleSize = 20,
            StrategyClassification = "Candidate",
            StrategyTier = "ConservativeCandidate",
            ModelVersion = LikelyHitModelService.ModelVersion,
            RecommendationContractVersion = LikelyHitModelService.RecommendationContractVersion,
            ProbabilityModelVersion = LikelyHitModelService.ProbabilityModelVersion,
            ModelEra = modelEra,
            RankingScore = rankingScore,
            IsCandidate = isCandidate,
            IsTopPick = isTopPick,
            IsWatchlist = isWatchlist,
            IsActionableForUser = isActionableForUser,
            LifecycleStatus = lifecycleStatus,
            GradeStatus = "Pending"
        });

        if (resultValue.HasValue)
        {
            var resultPlayerId = player.Id;
            var resultPropTypeId = primaryPropType.Id;
            if (resultUsesDifferentPlayerIdWithSameName)
            {
                var resultPlayer = new Player
                {
                    Id = Guid.NewGuid(),
                    LeagueId = league.Id,
                    DisplayName = player.DisplayName,
                    FirstName = player.FirstName,
                    LastName = player.LastName
                };
                dbContext.Players.Add(resultPlayer);
                resultPlayerId = resultPlayer.Id;
            }

            if (resultUsesDifferentPropTypeIdWithEquivalentName)
            {
                var resultPropType = new PropType
                {
                    Id = Guid.NewGuid(),
                    SportId = sport.Id,
                    Name = propType.Replace(" ", "_", StringComparison.Ordinal).ToUpperInvariant(),
                    StatKey = propType.Replace(" ", "_", StringComparison.Ordinal).ToUpperInvariant()
                };
                dbContext.PropTypes.Add(resultPropType);
                resultPropTypeId = resultPropType.Id;
            }

            AddPropResult(dbContext, resultPlayerId, league.Id, resultPropTypeId, date, resultValue.Value, "primary");
        }

        if (additionalSameNameResultValue.HasValue)
        {
            var additionalPlayer = new Player
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                DisplayName = player.DisplayName,
                FirstName = player.FirstName,
                LastName = player.LastName
            };
            dbContext.Players.Add(additionalPlayer);
            AddPropResult(dbContext, additionalPlayer.Id, league.Id, primaryPropType.Id, date, additionalSameNameResultValue.Value, "additional-same-name");
        }

        if (duplicateMatchingResultValue.HasValue)
        {
            AddPropResult(dbContext, player.Id, league.Id, primaryPropType.Id, date, duplicateMatchingResultValue.Value, "duplicate");
        }

        if (nearbyResultDate.HasValue && nearbyResultValue.HasValue)
        {
            AddPropResult(dbContext, player.Id, league.Id, primaryPropType.Id, nearbyResultDate.Value, nearbyResultValue.Value, "nearby");
        }

        if (sameDateOtherPlayerResultValue.HasValue)
        {
            var otherPlayer = new Player
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                DisplayName = "Other Player",
                FirstName = "Other",
                LastName = "Player"
            };
            dbContext.Players.Add(otherPlayer);
            AddPropResult(dbContext, otherPlayer.Id, league.Id, primaryPropType.Id, date, sameDateOtherPlayerResultValue.Value, "other-player");
        }

        if (!string.IsNullOrWhiteSpace(alternateResultPropType) && alternateResultValue.HasValue)
        {
            var alternatePropType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = alternateResultPropType,
                StatKey = alternateResultPropType
            };
            dbContext.PropTypes.Add(alternatePropType);
            AddPropResult(dbContext, player.Id, league.Id, alternatePropType.Id, date, alternateResultValue.Value, "alternate");
        }

        await dbContext.SaveChangesAsync();
        return new SeededPerformanceScenario(recommendationSnapshotId, player.Id, primaryPropType.Id);
    }

    private static RecommendationSnapshot CloneRecommendationSnapshot(
        ApplicationDbContext dbContext,
        RecommendationSnapshot original,
        string? provider = null,
        string? bookmaker = null,
        decimal? estimatedEv = null,
        decimal rankingScoreDelta = 0m)
    {
        var clone = new RecommendationSnapshot();
        dbContext.Entry(clone).CurrentValues.SetValues(original);
        clone.Id = Guid.NewGuid();
        clone.IdempotencyKey = Guid.NewGuid().ToString("N");
        clone.FirstSeenAtUtc = original.FirstSeenAtUtc.AddMinutes(1);
        clone.LastSeenAtUtc = original.LastSeenAtUtc.AddMinutes(1);
        clone.CapturedAtUtc = original.CapturedAtUtc.AddMinutes(1);
        clone.Provider = provider ?? original.Provider;
        clone.Bookmaker = bookmaker ?? original.Bookmaker;
        clone.EntryProvider = provider ?? original.EntryProvider;
        clone.EntryCompatibilityGroup = provider ?? original.EntryCompatibilityGroup;
        clone.EstimatedEv = estimatedEv ?? original.EstimatedEv;
        clone.RankingScore = original.RankingScore + rankingScoreDelta;
        clone.GradeStatus = "Pending";
        return clone;
    }

    private static void AddPropResult(
        ApplicationDbContext dbContext,
        Guid playerId,
        Guid leagueId,
        Guid propTypeId,
        DateOnly eventDate,
        decimal value,
        string sourceSuffix)
    {
        dbContext.PropResults.Add(new PropResult
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            LeagueId = leagueId,
            PropTypeId = propTypeId,
            EventDate = eventDate,
            ActualResultValue = value,
            ExternalSource = "test",
            ExternalSourceIdentifier = $"{sourceSuffix}:{Guid.NewGuid():N}"
        });
    }

    private static string NormalizeKey(string value)
    {
        return string.Concat(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));
    }

    private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected success status code but received {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
    }

    private sealed record SeededPerformanceScenario(
        Guid RecommendationSnapshotId,
        Guid PlayerId,
        Guid PropTypeId);
}
