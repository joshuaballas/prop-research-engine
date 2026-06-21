using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Integrations.Underdog;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Tests;

public sealed class RefreshSafetyTests
{
    [Fact]
    public async Task Planner_AllowsBudget_WhenLocalDailyAndMonthlyCapsAreConfiguredButEnforcementIsDisabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxDailyCredits"] = "1",
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "1"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.False(plan.GetProperty("plannerEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("localBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("dailyBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("monthlyBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("localDailyBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("localMonthlyBudgetEnforcementEnabled").GetBoolean());
        Assert.False(plan.GetProperty("perRunEstimateBlockingEnabled").GetBoolean());
        Assert.False(plan.GetProperty("providerCreditBlockingEnabled").GetBoolean());
        Assert.False(plan.GetProperty("dailyBudgetEnabled").GetBoolean());
        Assert.Equal(1, plan.GetProperty("maxDailyCredits").GetInt32());
        Assert.True(plan.GetProperty("monthlyBudgetAllowed").GetBoolean());
        Assert.False(plan.GetProperty("localBudgetExceeded").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByLocalBudget").GetBoolean());
        Assert.Equal(1, plan.GetProperty("configuredMonthlyCreditLimit").GetInt32());
        Assert.Equal(2360, plan.GetProperty("providerReportedRequestsUsed").GetInt32());
        Assert.True(plan.GetProperty("plannerWouldBlockIfEnforced").GetBoolean());
    }

    [Fact]
    public async Task Planner_AllowsEstimatedCalls_WhenProviderTelemetryDoesNotBlockAndNoPerRunCap()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxDailyCredits"] = "1",
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "1"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.False(plan.GetProperty("dailyBudgetEnabled").GetBoolean());
        Assert.True(plan.GetProperty("monthlyBudgetAllowed").GetBoolean());
        Assert.False(plan.GetProperty("localBudgetExceeded").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByLocalBudget").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByProviderCredits").GetBoolean());
    }

    [Fact]
    public async Task Planner_IgnoresStaleProviderCreditTelemetry_ForProviderCreditHardStop()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000",
            ["TheOddsRefreshPlanner:ProviderTelemetryStaleAfterMinutes"] = "60"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 2, observedMinutesAgo: 180);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.True(plan.GetProperty("isProviderTelemetryStale").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByProviderCredits").GetBoolean());
    }

    [Fact]
    public async Task Planner_WarnsButAllowsProviderCreditsExceeded_WhenEnforcementIsDisabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 2, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.False(plan.GetProperty("isBlockedByProviderCredits").GetBoolean());
        Assert.True(plan.GetProperty("plannerWouldBlockIfEnforced").GetBoolean());
        Assert.False(plan.GetProperty("providerTelemetryUsedForBlocking").GetBoolean());
        Assert.False(plan.GetProperty("isBlockedByLocalBudget").GetBoolean());
    }

    [Fact]
    public async Task Planner_BlocksWithProviderCreditsExceeded_WhenEnforcementAndProviderCreditBlockingAreEnabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:EnforcementEnabled"] = "true",
            ["TheOddsRefreshPlanner:BlockOnProviderCredits"] = "true",
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 2, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.False(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal("ProviderCreditsExceeded", plan.GetProperty("budgetBlockReason").GetString());
        Assert.True(plan.GetProperty("isBlockedByProviderCredits").GetBoolean());
        Assert.True(plan.GetProperty("providerCreditBlockingEnabled").GetBoolean());
        Assert.True(plan.GetProperty("providerTelemetryUsedForBlocking").GetBoolean());
    }

    [Fact]
    public async Task Planner_WarnsButAllowsPerRunEstimateExceeded_WhenEnforcementIsDisabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000",
            ["TheOddsScheduledRefresh:MaxEstimatedCallsPerRun"] = "5"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
        Assert.False(plan.GetProperty("isBlockedByPerRunEstimate").GetBoolean());
        Assert.True(plan.GetProperty("plannerWouldBlockIfEnforced").GetBoolean());
    }

    [Fact]
    public async Task Planner_BlocksWithPerRunEstimateExceeded_WhenEnforcementAndPerRunBlockingAreEnabled()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsRefreshPlanner:EnforcementEnabled"] = "true",
            ["TheOddsRefreshPlanner:BlockOnPerRunEstimate"] = "true",
            ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "100000",
            ["TheOddsScheduledRefresh:MaxEstimatedCallsPerRun"] = "5"
        });
        using var client = factory.CreateClient();
        await factory.SeedCreditTelemetryRefreshAsync(requestsUsed: 2360, requestsRemaining: 97640, observedMinutesAgo: 10);

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.False(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal("PerRunEstimateExceeded", plan.GetProperty("budgetBlockReason").GetString());
        Assert.True(plan.GetProperty("isBlockedByPerRunEstimate").GetBoolean());
        Assert.True(plan.GetProperty("perRunEstimateBlockingEnabled").GetBoolean());
    }

    [Fact]
    public async Task DryRunRefreshCurrent_DoesNotCallProvider_AndDoesNotWriteRefreshArtifacts()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsync(
            "/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true&bookmakerPreset=dfsOnly",
            content: null);

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostics = json.GetProperty("diagnostics");
        Assert.False(diagnostics.GetProperty("wouldCallTheOddsApi").GetBoolean());
        Assert.False(diagnostics.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_DryRun_DoesNotCallProvider_AndReportsMissingConfig()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            dryRun = true,
            forceLive = false
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("dryRun").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldCallExternalApis").GetBoolean());
        Assert.Equal("SOCCER", json.GetProperty("sport").GetString());
        Assert.Equal("World Cup", json.GetProperty("competition").GetString());
        Assert.False(json.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "TheOddsApi:SoccerWorldCupSportKey");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_WithoutForceLive_DoesNotCallProvider()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            dryRun = false,
            forceLive = false
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("requiresForceLive").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("wouldCallExternalApis").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_ForceLiveWithMissingConfig_DoesNotCallProvider()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            dryRun = false,
            forceLive = true
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MissingSoccerWorldCupProviderConfig", json.GetProperty("blockReason").GetString());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "TheOddsApi:SoccerWorldCupSportKey");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task LiveShapedRefreshCurrent_WithoutForceLive_Returns409_AndDoesNotCallProviderOrWrite()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsync(
            "/api/debug/the-odds/mlb/recommendations/refresh-current?bookmakerPreset=dfsOnly&includeCandidates=true&includeLineShopping=true&includeFullDiagnostics=true&freshnessMinutes=30",
            content: null);

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("requiresForceLive").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Theory]
    [InlineData("/api/markets/freshness")]
    [InlineData("/api/markets/provider-status")]
    [InlineData("/api/markets/refresh-plan")]
    [InlineData("/api/markets/refresh-plan/history?take=20")]
    [InlineData("/api/markets/top-picks?take=10")]
    [InlineData("/api/markets/line-shopping?take=10&includeBookmakerPrices=false")]
    [InlineData("/api/markets/performance-summary?candidateOnly=true")]
    [InlineData("/api/dfs/entry-rules")]
    public async Task PublicMarketEndpoints_AreReadOnly_AndDoNotCallProviders(string path)
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync(path);

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(0, factory.UnderdogAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task EntryRules_SeparateBuildableDfsProvidersFromContextOnlySportsbooks()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dfs/entry-rules");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var buildableProviders = json.GetProperty("entryBuildableProviders").EnumerateArray().ToArray();
        var contextOnlyProviders = json.GetProperty("contextOnlyProviders").EnumerateArray().ToArray();
        var rules = json.GetProperty("rules").EnumerateArray().ToArray();

        Assert.Contains(buildableProviders, row => row.GetProperty("provider").GetString() == "PrizePicks");
        Assert.Contains(buildableProviders, row => row.GetProperty("provider").GetString() == "Underdog");
        Assert.Contains(contextOnlyProviders, row => row.GetProperty("providerCategory").GetString() == "Sportsbook");
        Assert.DoesNotContain(rules, row => row.GetProperty("providerCategory").GetString() == "Sportsbook");
        Assert.All(rules, row => Assert.True(row.TryGetProperty("payoutMultipliers", out _)));
        var prizePicks = rules.Single(row => row.GetProperty("provider").GetString() == "PrizePicks");
        var prizePicksMultipliers = prizePicks.GetProperty("payoutMultipliers").EnumerateArray().ToArray();
        Assert.Equal(9, prizePicksMultipliers.Length);
        AssertMultiplier(prizePicksMultipliers, "Power", 2, 3m);
        AssertMultiplier(prizePicksMultipliers, "Power", 3, 6m);
        AssertMultiplier(prizePicksMultipliers, "Power", 4, 10m);
        AssertMultiplier(prizePicksMultipliers, "Power", 5, 20m);
        AssertMultiplier(prizePicksMultipliers, "Power", 6, 37.5m);
        AssertMultiplier(prizePicksMultipliers, "Flex", 3, 3m);
        AssertMultiplier(prizePicksMultipliers, "Flex", 4, 6m);
        AssertMultiplier(prizePicksMultipliers, "Flex", 5, 10m);
        AssertMultiplier(prizePicksMultipliers, "Flex", 6, 25m);
        Assert.Equal("FixedMultiplierTableWithPartialHitRows", prizePicks.GetProperty("payoutModelType").GetString());
        Assert.True(prizePicks.GetProperty("hasPartialHitPayouts").GetBoolean());
        Assert.False(prizePicks.GetProperty("partialHitPayoutsModeled").GetBoolean());

        foreach (var provider in new[] { "Underdog", "DraftKings Pick6" })
        {
            var rule = rules.Single(row => row.GetProperty("provider").GetString() == provider);
            Assert.Equal("UnknownOrUnavailable", rule.GetProperty("payoutModelType").GetString());
            Assert.False(rule.GetProperty("hasPartialHitPayouts").GetBoolean());
            Assert.False(rule.GetProperty("partialHitPayoutsModeled").GetBoolean());
            Assert.Empty(rule.GetProperty("payoutMultipliers").EnumerateArray());
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("payoutRulesNotes").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("sourceUrl").GetString()));
        }

        var betrPicks = rules.Single(row => row.GetProperty("provider").GetString() == "Betr Picks");
        Assert.Equal("VariableMultiplier", betrPicks.GetProperty("payoutModelType").GetString());
        Assert.True(betrPicks.GetProperty("hasPartialHitPayouts").GetBoolean());
        Assert.False(betrPicks.GetProperty("partialHitPayoutsModeled").GetBoolean());
        Assert.Empty(betrPicks.GetProperty("payoutMultipliers").EnumerateArray());
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 }, betrPicks.GetProperty("allowedLegCounts").EnumerateArray().Select(row => row.GetInt32()).ToArray());
        Assert.Contains(betrPicks.GetProperty("entryTypes").EnumerateArray(), row => row.GetString() == "Perfect Play");
        Assert.Contains(betrPicks.GetProperty("entryTypes").EnumerateArray(), row => row.GetString() == "Flex Play");
    }

    [Fact]
    public async Task ScheduledWorkerStatus_DefaultsDisabled_AndShowsPlannerSkip()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();

        var response = await client.GetAsync("/api/debug/the-odds/mlb/scheduled-refresh/status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("enabled").GetBoolean());
        Assert.False(json.GetProperty("allowScheduledLiveRefresh").GetBoolean());
        Assert.Equal("disabled", json.GetProperty("status").GetString());
        var currentPlan = json.GetProperty("currentPlan");
        Assert.False(currentPlan.GetProperty("shouldRefresh").GetBoolean());
        Assert.False(currentPlan.GetProperty("providerAvailable").GetBoolean());
        Assert.Equal("Unavailable", currentPlan.GetProperty("providerStatus").GetString());
    }

    [Fact]
    public async Task Planner_BlocksWhenProviderUnavailableOrCreditsBelowHardStop()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.False(plan.GetProperty("shouldRefresh").GetBoolean());
        Assert.Equal("ProviderRejectedKey", plan.GetProperty("skipReason").GetString());
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.False(plan.GetProperty("providerAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("budgetBlockReason").ValueKind);
    }

    [Fact]
    public async Task Planner_BlocksOnRecentProviderQuotaFailureBackoff_WhenEnforcementIsDisabled()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedProviderQuotaFailureAsync();

        var response = await client.GetAsync("/api/markets/refresh-plan");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plan = json.GetProperty("plans")[0];
        Assert.False(plan.GetProperty("shouldRefresh").GetBoolean());
        Assert.True(plan.GetProperty("budgetAllowed").GetBoolean());
        Assert.Equal("RecentProviderFailureBackoff", plan.GetProperty("skipReason").GetString());
    }

    [Fact]
    public async Task ProviderStatus_DetectsRejectedKeyFromPersistedDiagnostics()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();

        var response = await client.GetAsync("/api/markets/provider-status");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var provider = json.GetProperty("providers")[0];
        Assert.Equal("Unavailable", provider.GetProperty("status").GetString());
        Assert.True(provider.GetProperty("providerRejectedKeyDetected").GetBoolean());
        Assert.Equal(498, provider.GetProperty("requestsUsed").GetInt32());
        Assert.Equal(2, provider.GetProperty("requestsRemaining").GetInt32());
    }

    [Fact]
    public async Task SnapshotReads_ReturnSnapshotBackedFalse_WhenNoUsableRowsMatch()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync(includeStaleSnapshot: true);

        var topPicksResponse = await client.GetAsync("/api/markets/top-picks?take=10");
        var lineShoppingResponse = await client.GetAsync("/api/markets/line-shopping?take=10&includeBookmakerPrices=false");
        await EnsureSuccessWithBodyAsync(topPicksResponse);
        await EnsureSuccessWithBodyAsync(lineShoppingResponse);
        var topPicks = await topPicksResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lineShopping = await lineShoppingResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(topPicks.GetProperty("snapshotBacked").GetBoolean());
        Assert.False(lineShopping.GetProperty("snapshotBacked").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task SoccerWorldCupMarketReads_ReturnPersistedSnapshots_WithoutCallingProviders()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedSoccerWorldCupSnapshotsAsync();
        var before = await factory.CountArtifactsAsync();

        var freshnessResponse = await client.GetAsync("/api/markets/freshness?sport=SOCCER");
        var topPicksResponse = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&take=10");
        var lineShoppingResponse = await client.GetAsync("/api/markets/line-shopping?sport=SOCCER&take=10&includeBookmakerPrices=false");

        await EnsureSuccessWithBodyAsync(freshnessResponse);
        await EnsureSuccessWithBodyAsync(topPicksResponse);
        await EnsureSuccessWithBodyAsync(lineShoppingResponse);
        var freshness = await freshnessResponse.Content.ReadFromJsonAsync<JsonElement>();
        var topPicks = await topPicksResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lineShopping = await lineShoppingResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("SOCCER", freshness.GetProperty("effectiveSport").GetString());
        Assert.Equal("World Cup", freshness.GetProperty("competition").GetString());
        Assert.False(freshness.GetProperty("resultGradingSupported").GetBoolean());
        Assert.True(topPicks.GetProperty("snapshotBacked").GetBoolean());
        Assert.Equal("SOCCER", topPicks.GetProperty("topPicks")[0].GetProperty("sport").GetString());
        Assert.Equal("World Cup", topPicks.GetProperty("topPicks")[0].GetProperty("competition").GetString());
        Assert.True(lineShopping.GetProperty("snapshotBacked").GetBoolean());
        Assert.Equal("SOCCER", lineShopping.GetProperty("comparisonGroups")[0].GetProperty("sport").GetString());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SoccerPropHistory_ReturnsUnavailableState()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedSoccerWorldCupSnapshotsAsync();

        var response = await client.GetAsync("/api/markets/prop-history?sport=SOCCER&playerName=Test%20Forward&propType=Shots&side=Over&line=1.5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SOCCER", json.GetProperty("sport").GetString());
        Assert.Equal("Soccer history is not available yet.", json.GetProperty("emptyReason").GetString());
        Assert.Empty(json.GetProperty("games").EnumerateArray());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task ApiSportsConfigDiagnostics_ReportMissingConfig_WithoutCallingExternalApis()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/config");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal("API-Sports", json.GetProperty("provider").GetString());
        Assert.False(json.GetProperty("configured").GetBoolean());
        Assert.Contains(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:ApiKey");
        Assert.Contains(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:Football:WorldCupLeagueId");
    }

    [Theory]
    [InlineData("/api/debug/soccer/api-sports/fixtures?date=2026-06-21&forceLive=false")]
    [InlineData("/api/debug/soccer/api-sports/fixture-player-stats?fixtureId=123&forceLive=false")]
    [InlineData("/api/debug/soccer/api-sports/fixture-prop-coverage?fixtureId=123&forceLive=false")]
    public async Task ApiSportsForceLiveEndpoints_BlockWithoutForceLive(string path)
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("requiresForceLive").GetBoolean());
    }

    [Fact]
    public async Task ApiSportsForceLiveFixtures_ReturnsMissingConfig_WhenKeyIsAbsent()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?date=2026-06-21&forceLive=true");

        await AssertStatusWithBodyAsync(response, HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.Equal("MissingApiSportsConfig", json.GetProperty("blockReason").GetString());
        Assert.Contains(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:ApiKey");
        Assert.Contains(json.GetProperty("missingConfig").EnumerateArray(), row => row.GetString() == "ApiSports:Football:WorldCupLeagueId");
    }

    [Fact]
    public async Task ApiSportsFixtures_FixtureId_BuildsIdOnlyRequest()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?fixtureId=12345&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(1, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal("12345", json.GetProperty("request").GetProperty("used").GetProperty("fixtureId").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("date").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("league").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("season").ValueKind);
        Assert.EndsWith("/fixtures?id=12345", json.GetProperty("sanitizedUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiSportsFixtures_DateOnly_CanOmitConfiguredLeagueAndSeason()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig(includeWorldCupDefaults: false));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?date=2026-06-20&useConfiguredLeague=false&useConfiguredSeason=false&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-06-20", json.GetProperty("request").GetProperty("used").GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("league").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("request").GetProperty("used").GetProperty("season").ValueKind);
        Assert.EndsWith("/fixtures?date=2026-06-20", json.GetProperty("sanitizedUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiSportsFixtures_ExplicitLeagueAndSeason_OverrideConfig()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?date=2026-06-20&league=39&season=2024&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(39, json.GetProperty("request").GetProperty("used").GetProperty("league").GetInt32());
        Assert.Equal(2024, json.GetProperty("request").GetProperty("used").GetProperty("season").GetInt32());
        Assert.EndsWith("/fixtures?date=2026-06-20&league=39&season=2024", json.GetProperty("sanitizedUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiSportsFixtures_ConfiguredDefaults_PreserveCurrentBehavior()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-sports/fixtures?date=2026-06-20&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("request").GetProperty("used").GetProperty("league").GetInt32());
        Assert.Equal(2026, json.GetProperty("request").GetProperty("used").GetProperty("season").GetInt32());
        Assert.EndsWith("/fixtures?date=2026-06-20&league=1&season=2026", json.GetProperty("sanitizedUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiSportsFixtures_OldApiFootballRoute_IsNotPromoted()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/soccer/api-football/fixtures?date=2026-06-20&forceLive=false");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_BlocksWithoutForceLive()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = false
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_DryRun_WritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(0, json.GetProperty("resultRowsWritten").GetInt32());
        Assert.True(json.GetProperty("resultRowsPreviewed").GetInt32() > 0);
        Assert.Equal(0, await CountApiSportsSoccerResultsAsync(factory));
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_WritesIdempotently()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var firstResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(firstResponse);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inserted = first.GetProperty("resultRowsInserted").GetInt32();

        var secondResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(secondResponse);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(inserted > 0);
        Assert.Equal(inserted, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(0, second.GetProperty("resultRowsInserted").GetInt32());
        Assert.Equal(0, second.GetProperty("resultRowsUpdated").GetInt32());
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_AllowsDuplicateDisplayNamesWithDifferentStatKeys()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedSoccerPropTypesWithDuplicateNamesAndDifferentStatKeysAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("resultRowsInserted").GetInt32() > 0);
        Assert.True(await factory.HasSoccerPropTypeAsync("GOALIE_SAVES_COMBO"));
        Assert.True(await factory.HasSoccerPropTypeAsync("GOALIE_SAVES"));
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_ReturnsStructuredPropTypeConflict_ForDuplicateStatKey()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedConflictingSoccerPropTypesWithSameStatKeyAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PropTypeResolutionFailed", json.GetProperty("blockReason").GetString());
        Assert.Equal("PropTypeResolution", json.GetProperty("stage").GetString());
        var conflict = Assert.Single(json.GetProperty("conflicts").EnumerateArray());
        Assert.Equal("GOALIE_SAVES", conflict.GetProperty("statKey").GetString());
        Assert.Equal("ExistingPropTypes", conflict.GetProperty("stage").GetString());
        Assert.Equal(2, conflict.GetProperty("conflictingMetadata").EnumerateArray().Count());
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_FlagsConservativeAndSkipsUnsupportedRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("unsupportedSkipped").GetInt32() > 0);
        Assert.True(json.GetProperty("ruleConfidenceCounts").TryGetProperty("NeedsConfirmation", out var needsConfirmation));
        Assert.True(needsConfirmation.GetInt32() > 0);

        var playerGroups = json.GetProperty("previewRowsByPlayer").EnumerateArray().ToArray();
        var forward = Assert.Single(playerGroups, group => group.GetProperty("playerName").GetString() == "Test Forward");
        Assert.DoesNotContain(forward.GetProperty("rows").EnumerateArray(), row => row.GetProperty("propType").GetString() == "Goalie Saves");
        Assert.DoesNotContain(playerGroups, group => group.GetProperty("playerName").GetString() == "Unused Substitute");
        var keeper = Assert.Single(playerGroups, group => group.GetProperty("playerName").GetString() == "Test Keeper");
        Assert.Contains(keeper.GetProperty("rows").EnumerateArray(), row => row.GetProperty("propType").GetString() == "Goalie Saves");
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixtureStatus_ReturnsLocalCounts()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        var statusResponse = await client.GetAsync("/api/debug/soccer/results/api-sports/import-fixture/status?fixtureId=1489393");

        await EnsureSuccessWithBodyAsync(statusResponse);
        var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(status.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(status.GetProperty("rowCount").GetInt32() > 0);
        Assert.Contains(status.GetProperty("countsByPropType").EnumerateArray(), row => row.GetProperty("propType").GetString() == "Goals");
    }

    [Fact]
    public async Task RefreshPlanHistory_ReadOnly_DoesNotCallProviders()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedRejectedKeyRefreshAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/markets/refresh-plan/history?take=20");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected success status code but received {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
    }

    private static async Task AssertStatusWithBodyAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        if (response.StatusCode != expectedStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected status code {(int)expectedStatusCode} {expectedStatusCode} but received {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
    }

    private static void AssertMultiplier(JsonElement[] multipliers, string entryType, int legCount, decimal expectedMultiplier)
    {
        var row = Assert.Single(multipliers, multiplier =>
            multiplier.GetProperty("entryType").GetString() == entryType
            && multiplier.GetProperty("legCount").GetInt32() == legCount);

        Assert.Equal(expectedMultiplier, row.GetProperty("multiplier").GetDecimal());
        Assert.Equal("StandardOnly", row.GetProperty("appliesToProjectionType").GetString());
        Assert.Equal("Known", row.GetProperty("sourceConfidence").GetString());
        Assert.Equal(JsonValueKind.String, row.GetProperty("lastVerifiedAtUtc").ValueKind);
        Assert.NotEmpty(row.GetProperty("notes").EnumerateArray());
    }

    private static Dictionary<string, string?> ApiSportsLiveConfig(bool includeWorldCupDefaults = true)
    {
        var config = new Dictionary<string, string?>
        {
            ["ApiSports:Enabled"] = "true",
            ["ApiSports:ApiKey"] = "TEST_API_SPORTS_KEY",
            ["ApiSports:Football:BaseUrl"] = "https://v3.football.api-sports.io"
        };

        if (includeWorldCupDefaults)
        {
            config["ApiSports:Football:WorldCupLeagueId"] = "1";
            config["ApiSports:Football:Season"] = "2026";
        }

        return config;
    }

    private static async Task<int> CountApiSportsSoccerResultsAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PropResults.CountAsync(result => result.ExternalSource == "api-sports-football");
    }
}

public sealed class SafetyTestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection databaseConnection = new("Data Source=:memory:");
    private readonly IReadOnlyDictionary<string, string?> configOverrides;

    public CountingTheOddsAdapter TheOddsAdapter { get; } = new();
    public CountingUnderdogAdapter UnderdogAdapter { get; } = new();
    public StubApiSportsFootballClient ApiSportsFootballClient { get; } = new();

    public SafetyTestApplicationFactory(IReadOnlyDictionary<string, string?>? configOverrides = null)
    {
        this.configOverrides = configOverrides ?? new Dictionary<string, string?>();
        databaseConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:SportsBettingDb"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["DebugAdmin:Enabled"] = "true",
                ["DebugAdmin:RequireApiKey"] = "false",
                ["TheOddsApi:ApiKey"] = "TEST_THE_ODDS_API_KEY",
                ["TheOddsRefreshPlanner:MaxDailyCredits"] = "500",
                ["TheOddsRefreshPlanner:MaxMonthlyCredits"] = "500",
                ["TheOddsRefreshPlanner:ProviderRemainingHardStopCredits"] = "5",
                ["TheOddsScheduledRefresh:Enabled"] = "false",
                ["TheOddsScheduledRefresh:AllowScheduledLiveRefresh"] = "false",
                ["UnderdogIngestion:Enabled"] = "false",
                ["MlbResultImportWorker:Enabled"] = "false",
                ["ApiSports:Enabled"] = "false",
                ["ApiSports:ApiKey"] = "API_SPORTS_KEY",
                ["ApiSports:Football:BaseUrl"] = "https://v3.football.api-sports.io",
                ["ApiSports:Football:WorldCupLeagueId"] = "",
                ["ApiSports:Football:Season"] = ""
            };

            foreach (var (key, value) in configOverrides)
            {
                settings[key] = value;
            }

            config.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            RemoveProviderSpecificServices(services, "Npgsql");
            services.RemoveAll<ITheOddsApiMlbPlayerPropsAdapter>();
            services.RemoveAll<IUnderdogAdapter>();
            services.RemoveAll<IApiSportsFootballClient>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(databaseConnection));
            services.AddSingleton<ITheOddsApiMlbPlayerPropsAdapter>(TheOddsAdapter);
            services.AddSingleton<IUnderdogAdapter>(UnderdogAdapter);
            services.AddSingleton<IApiSportsFootballClient>(ApiSportsFootballClient);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            databaseConnection.Dispose();
        }
    }

    private static void RemoveProviderSpecificServices(IServiceCollection services, string providerName)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (ContainsProviderName(descriptor.ServiceType)
                || ContainsProviderName(descriptor.ImplementationType)
                || ContainsProviderName(descriptor.ImplementationInstance?.GetType()))
            {
                services.RemoveAt(index);
            }
        }

        bool ContainsProviderName(Type? type)
        {
            return type?.FullName?.Contains(providerName, StringComparison.OrdinalIgnoreCase) == true
                || type?.Assembly.FullName?.Contains(providerName, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public async Task SeedRejectedKeyRefreshAsync(bool includeStaleSnapshot = false)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS",
            SportsbookBookmakersRequested = string.Empty,
            DfsBookmakersRequested = "prizepicks,underdog,pick6,betr_us_dfs",
            MarketsRequested = "batter_hits,batter_total_bases,pitcher_strikeouts",
            EstimatedApiCalls = 16,
            ActualApiCalls = 16,
            PredictionCount = 87,
            LineShoppingGroupCount = 87,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                events = Enumerable.Range(1, 15).Select(index => new
                {
                    eventId = $"event-{index}",
                    eventStartTime = DateTimeOffset.UtcNow.AddHours(4 + index)
                }),
                creditUsage = new
                {
                    requestsUsed = 498,
                    requestsRemaining = 2,
                    requestsLast = 0
                },
                ingestion = Enumerable.Range(1, 15).Select(index => new
                {
                    eventId = $"event-{index}",
                    success = false,
                    errors = new[] { "The Odds API rejected the configured API key." }
                })
            })
        };
        dbContext.TheOddsRefreshRuns.Add(refreshRun);

        if (includeStaleSnapshot)
        {
            dbContext.MarketLineShoppingSnapshots.Add(new MarketLineShoppingSnapshot
            {
                Id = Guid.NewGuid(),
                TheOddsRefreshRunId = refreshRun.Id,
                PredictionRunId = null,
                Sport = "MLB",
                SlateDate = refreshRun.SlateDate,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                BookmakerPreset = "dfsOnly",
                ProviderCategory = "DFS",
                Provider = "The Odds API",
                Bookmaker = "PrizePicks",
                Player = "Test Player",
                EventId = "event-1",
                EventName = "Away @ Home",
                EventStartTime = DateTimeOffset.UtcNow.AddHours(-1),
                LifecycleStatus = "GameStarted",
                IsStale = true,
                IsExpired = true,
                PropType = "Total Bases",
                Line = 1.5m,
                BestSide = "Under",
                BestBookmaker = "PrizePicks",
                BestAmericanPrice = -120,
                BestDecimalPrice = 1.833333m,
                BestProbability = 0.6m,
                BestImpliedProbability = 0.545455m,
                BestProbabilityEdge = 0.054545m,
                BestEstimatedEv = 0.05m,
                StrategyClassification = "Candidate",
                StrategyTier = "ConservativeCandidate",
                IsCandidate = true,
                IsWatchlist = false,
                IsAvoid = false,
                IsResearchOnly = false,
                EntryProvider = "PrizePicks",
                EntryCompatibilityGroup = "PrizePicks",
                EntryProviderCategory = "DFS",
                EntryActionableForUser = true,
                DetailsJson = JsonDocument.Parse("""{"historicalSampleSize":100}""")
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task EnsureDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task SeedSoccerPropTypesWithDuplicateNamesAndDifferentStatKeysAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        dbContext.PropTypes.AddRange(
            new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goalie Saves",
                StatKey = "GOALIE_SAVES_COMBO",
                ValueType = "numeric"
            },
            new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goalie Saves",
                StatKey = "GOALIE_SAVES",
                ValueType = "numeric"
            });
        await dbContext.SaveChangesAsync();
    }

    public async Task SeedConflictingSoccerPropTypesWithSameStatKeyAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        dbContext.PropTypes.AddRange(
            new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goalie Saves",
                StatKey = "GOALIE_SAVES",
                ValueType = "numeric"
            },
            new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goalkeeper Saves",
                StatKey = "goalie_saves",
                ValueType = "numeric"
            });
        await dbContext.SaveChangesAsync();
    }

    public async Task<bool> HasSoccerPropTypeAsync(string statKey)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PropTypes.AnyAsync(propType =>
            propType.Sport != null
            && propType.Sport.Code == "SOCCER"
            && propType.StatKey == statKey);
    }

    private static async Task<Sport> GetOrCreateSoccerSportAsync(ApplicationDbContext dbContext)
    {
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is not null)
        {
            return sport;
        }

        sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
        dbContext.Sports.Add(sport);
        await dbContext.SaveChangesAsync();
        return sport;
    }

    public async Task SeedCreditTelemetryRefreshAsync(int requestsUsed, int requestsRemaining, int observedMinutesAgo)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-observedMinutesAgo);
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = observedAt.AddMinutes(-1),
            CompletedAtUtc = observedAt,
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS",
            SportsbookBookmakersRequested = string.Empty,
            DfsBookmakersRequested = "prizepicks,underdog,pick6,betr_us_dfs",
            MarketsRequested = "batter_hits,batter_total_bases,pitcher_strikeouts",
            EstimatedApiCalls = 8,
            ActualApiCalls = 8,
            PredictionCount = 12,
            LineShoppingGroupCount = 12,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                events = Enumerable.Range(1, 7).Select(index => new
                {
                    eventId = $"event-{index}",
                    eventStartTime = DateTimeOffset.UtcNow.AddHours(4 + index)
                }),
                creditUsage = new
                {
                    requestsUsed,
                    requestsRemaining,
                    requestsLast = 0
                }
            })
        };

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        await dbContext.SaveChangesAsync();
    }

    public async Task SeedSoccerWorldCupSnapshotsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "SOCCER",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-8),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-7),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS",
            SportsbookBookmakersRequested = string.Empty,
            DfsBookmakersRequested = "prizepicks",
            MarketsRequested = "soccer_world_cup_shots",
            EstimatedApiCalls = 1,
            ActualApiCalls = 1,
            PredictionCount = 1,
            LineShoppingGroupCount = 1,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                sport = "SOCCER",
                competition = "World Cup",
                soccerResultGradingSupported = false
            })
        };

        var details = JsonDocument.Parse("""
        {
          "player": "Test Forward",
          "propType": "Shots",
          "side": "Over",
          "line": 1.5,
          "provider": "PrizePicks",
          "providerCategory": "DFS",
          "entryProvider": "PrizePicks",
          "entryActionableForUser": true,
          "estimatedEv": 0.12,
          "bestEstimatedEv": 0.12,
          "eventName": "Country A @ Country B"
        }
        """);

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        dbContext.MarketTopPickSnapshots.Add(new MarketTopPickSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            Sport = "SOCCER",
            SlateDate = refreshRun.SlateDate,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BookmakerPreset = "dfsOnly",
            ProviderCategory = "DFS",
            Provider = "The Odds API",
            Bookmaker = "PrizePicks",
            Player = "Test Forward",
            EventId = "world-cup-event-1",
            EventName = "Country A @ Country B",
            EventStartTime = DateTimeOffset.UtcNow.AddHours(3),
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Shots",
            Line = 1.5m,
            BestSide = "Over",
            BestBookmaker = "PrizePicks",
            BestProbability = 0.62m,
            BestEstimatedEv = 0.12m,
            BestProbabilityEdge = 0.08m,
            StrategyClassification = "Candidate",
            StrategyTier = "ConservativeCandidate",
            IsCandidate = true,
            EntryProvider = "PrizePicks",
            EntryCompatibilityGroup = "PrizePicks",
            EntryProviderCategory = "DFS",
            EntryActionableForUser = true,
            DetailsJson = details
        });
        dbContext.MarketLineShoppingSnapshots.Add(new MarketLineShoppingSnapshot
        {
            Id = Guid.NewGuid(),
            TheOddsRefreshRunId = refreshRun.Id,
            Sport = "SOCCER",
            SlateDate = refreshRun.SlateDate,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BookmakerPreset = "dfsOnly",
            ProviderCategory = "DFS",
            Provider = "The Odds API",
            Bookmaker = "PrizePicks",
            Player = "Test Forward",
            EventId = "world-cup-event-1",
            EventName = "Country A @ Country B",
            EventStartTime = DateTimeOffset.UtcNow.AddHours(3),
            LifecycleStatus = "Active",
            IsStale = false,
            IsExpired = false,
            PropType = "Shots",
            Line = 1.5m,
            BestSide = "Over",
            BestBookmaker = "PrizePicks",
            BestProbability = 0.62m,
            BestEstimatedEv = 0.12m,
            BestProbabilityEdge = 0.08m,
            StrategyClassification = "Candidate",
            StrategyTier = "ConservativeCandidate",
            IsCandidate = true,
            EntryProvider = "PrizePicks",
            EntryCompatibilityGroup = "PrizePicks",
            EntryProviderCategory = "DFS",
            EntryActionableForUser = true,
            DetailsJson = details
        });

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedProviderQuotaFailureAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "MLB",
            SlateDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-20),
            Succeeded = false,
            Failed = true,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "dfsOnly",
            ProviderCategoriesIncluded = "DFS",
            SportsbookBookmakersRequested = string.Empty,
            DfsBookmakersRequested = "prizepicks,underdog,pick6,betr_us_dfs",
            MarketsRequested = "batter_hits,batter_total_bases,pitcher_strikeouts",
            EstimatedApiCalls = 8,
            ActualApiCalls = 1,
            PredictionCount = 0,
            LineShoppingGroupCount = 0,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                events = Enumerable.Range(1, 7).Select(index => new
                {
                    eventId = $"event-{index}",
                    eventStartTime = DateTimeOffset.UtcNow.AddHours(4 + index)
                }),
                error = "The Odds API quota or rate limit was exceeded.",
                creditUsage = new
                {
                    requestsUsed = 2360,
                    requestsRemaining = 2,
                    requestsLast = 1
                }
            })
        };

        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        await dbContext.SaveChangesAsync();
    }

    public async Task<ArtifactCounts> CountArtifactsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return new ArtifactCounts(
            await dbContext.TheOddsRefreshRuns.CountAsync(),
            await dbContext.MarketTopPickSnapshots.CountAsync(),
            await dbContext.MarketLineShoppingSnapshots.CountAsync(),
            await dbContext.TheOddsRefreshPlannerDecisions.CountAsync());
    }
}

public sealed class CountingTheOddsAdapter : ITheOddsApiMlbPlayerPropsAdapter
{
    public int CallCount { get; private set; }

    public Task<TheOddsApiPlayerPropsSpikeResult> FetchSpikeAsync(
        string? eventId,
        string? markets,
        string? bookmakers,
        string? regions,
        string? oddsFormat,
        CancellationToken cancellationToken)
    {
        CallCount++;
        throw new InvalidOperationException("The Odds API adapter should not be called by safety tests.");
    }
}

public sealed class CountingUnderdogAdapter : IUnderdogAdapter
{
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<UnderdogProjectionRecord>> GetActiveProjectionsAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        throw new InvalidOperationException("Underdog adapter should not be called by safety tests.");
    }

    public Task<UnderdogFetchResult> FetchActiveProjectionsAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        throw new InvalidOperationException("Underdog adapter should not be called by safety tests.");
    }
}

public sealed class StubApiSportsFootballClient : IApiSportsFootballClient
{
    public int FixturesCallCount { get; private set; }
    public int FixturePlayerStatsCallCount { get; private set; }

    public Task<ApiSportsFootballDiagnosticResponse> GetFixturesAsync(
        ApiSportsFootballFixturesRequest request,
        CancellationToken cancellationToken)
    {
        FixturesCallCount++;
        var sanitizedUrl = BuildFixturesUrl(request);
        var payload = JsonDocument.Parse(
            """
            {
              "get": "fixtures",
              "parameters": {},
              "errors": [],
              "results": 1,
              "paging": { "current": 1, "total": 1 },
              "response": [
                {
                  "fixture": { "id": 12345, "date": "2026-06-20T19:00:00+00:00", "status": { "short": "NS", "long": "Not Started" } },
                  "league": { "id": 1, "name": "World Cup", "season": 2026, "round": "Group Stage" },
                  "teams": {
                    "home": { "id": 10, "name": "Home FC" },
                    "away": { "id": 20, "name": "Away FC" }
                  }
                }
              ]
            }
            """);

        return Task.FromResult(new ApiSportsFootballDiagnosticResponse(payload, 1, 1, 1, sanitizedUrl));
    }

    public Task<ApiSportsFootballDiagnosticResponse> GetFixturePlayerStatsAsync(
        string fixtureId,
        CancellationToken cancellationToken)
    {
        FixturePlayerStatsCallCount++;
        var payload = JsonDocument.Parse(
            """
            {
              "get": "fixtures/players",
              "errors": [],
              "results": 2,
              "paging": { "current": 1, "total": 1 },
              "response": [
                {
                  "team": { "id": 10, "name": "Home FC" },
                  "players": [
                    {
                      "player": { "id": 1001, "name": "Test Forward" },
                      "statistics": [
                        {
                          "games": { "position": "F", "minutes": 90, "substitute": false },
                          "goals": { "total": 1, "assists": 1, "saves": 2, "conceded": 0 },
                          "shots": { "total": 4, "on": 2 },
                          "passes": { "total": 35, "key": 3 },
                          "tackles": { "total": 1 },
                          "fouls": { "committed": 2, "drawn": 1 },
                          "offsides": 1,
                          "dribbles": { "attempts": 5 },
                          "cards": { "yellow": 1, "red": 0 }
                        }
                      ]
                    },
                    {
                      "player": { "id": 1002, "name": "Unused Substitute" },
                      "statistics": [
                        {
                          "games": { "position": "M", "minutes": null, "substitute": true },
                          "goals": { "total": null }
                        }
                      ]
                    }
                  ]
                },
                {
                  "team": { "id": 20, "name": "Away FC" },
                  "players": [
                    {
                      "player": { "id": 2001, "name": "Test Keeper" },
                      "statistics": [
                        {
                          "games": { "position": "G", "minutes": 90, "substitute": false },
                          "goals": { "total": 0, "assists": 0, "saves": 6, "conceded": 1 },
                          "shots": { "total": 0, "on": 0 },
                          "passes": { "total": 22, "key": 0 },
                          "tackles": { "total": 0 },
                          "fouls": { "committed": 0, "drawn": 0 },
                          "offsides": 0,
                          "dribbles": { "attempts": 0 },
                          "cards": { "yellow": 0, "red": 0 }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);
        return Task.FromResult(new ApiSportsFootballDiagnosticResponse(
            payload,
            2,
            1,
            1,
            $"https://v3.football.api-sports.io/fixtures/players?fixture={Uri.EscapeDataString(fixtureId)}"));
    }

    private static string BuildFixturesUrl(ApiSportsFootballFixturesRequest request)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.FixtureId))
        {
            query.Add($"id={Uri.EscapeDataString(request.FixtureId)}");
        }
        else
        {
            if (request.Date.HasValue)
            {
                query.Add($"date={request.Date.Value:yyyy-MM-dd}");
            }

            if (request.League.HasValue)
            {
                query.Add($"league={request.League.Value}");
            }

            if (request.Season.HasValue)
            {
                query.Add($"season={request.Season.Value}");
            }
        }

        return $"https://v3.football.api-sports.io/fixtures?{string.Join("&", query)}";
    }
}

public sealed record ArtifactCounts(
    int RefreshRuns,
    int TopPickSnapshots,
    int LineShoppingSnapshots,
    int PlannerDecisions);

