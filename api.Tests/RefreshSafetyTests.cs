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
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = string.Empty,
            ["TheOddsApi:SoccerWorldCupMarkets"] = string.Empty
        });
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
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = string.Empty,
            ["TheOddsApi:SoccerWorldCupMarkets"] = string.Empty
        });
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
    public async Task SportNeutralSoccerRefresh_ForceLive_PersistsProviderEventContext()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupContextPayload();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            date = "2026-06-21",
            dryRun = false,
            forceLive = true,
            includeDfs = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(2, factory.TheOddsAdapter.CallCount);
        Assert.Equal(1, json.GetProperty("platformPropsUpserted").GetInt32());
        Assert.Equal(2, json.GetProperty("soccerMarketsDiscovered").GetInt32());
        Assert.False(json.GetProperty("diagnostics").GetProperty("providerPlayerIdAvailable").GetBoolean());
        Assert.False(json.GetProperty("diagnostics").GetProperty("playerTeamAvailable").GetBoolean());
        Assert.Equal(1, json.GetProperty("diagnostics").GetProperty("normalizedPropsCount").GetInt32());
        Assert.False(json.GetProperty("diagnostics").GetProperty("noProviderMarketsReturned").GetBoolean());
        Assert.False(json.GetProperty("diagnostics").GetProperty("providerMarketsReturnedButNoRowsMapped").GetBoolean());
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("mappedMarketsReturned").EnumerateArray(), row => row.GetString() == "player_shots");

        var contextResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/platform-context?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true&maxSamples=10");
        await EnsureSuccessWithBodyAsync(contextResponse);
        var context = await contextResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(context.GetProperty("fieldAvailability").GetProperty("hasEventNameCount").GetInt32() > 0);
        Assert.True(context.GetProperty("fieldAvailability").GetProperty("hasStartTimeCount").GetInt32() > 0);
        Assert.True(context.GetProperty("fieldAvailability").GetProperty("hasProviderEventIdCount").GetInt32() > 0);
        Assert.True(context.GetProperty("fieldAvailability").GetProperty("hasLeagueOrCompetitionCount").GetInt32() > 0);
        Assert.Equal(0, context.GetProperty("fieldAvailability").GetProperty("hasProviderPlayerIdCount").GetInt32());
        Assert.NotEqual("PreContextOrMissingContext", context.GetProperty("contextEnrichment").GetProperty("status").GetString());
        var sample = Assert.Single(context.GetProperty("samples").EnumerateArray());
        Assert.Equal("Away FC @ Home FC", sample.GetProperty("eventName").GetString());
        Assert.Equal("soccer-event-1", sample.GetProperty("providerEventId").GetString());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_Provider422_ReturnsStructuredProviderRejection()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "bad_market"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupProviderRejection();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            date = "2026-06-21",
            dryRun = false,
            forceLive = true,
            includeDfs = true
        });

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertStatusWithBodyAsync(response, HttpStatusCode.BadGateway);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("providerRejectedRequest").GetBoolean());
        Assert.Equal("ProviderRejectedRequest", json.GetProperty("blockReason").GetString());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        var providerError = json.GetProperty("providerError");
        Assert.Equal(422, providerError.GetProperty("statusCode").GetInt32());
        Assert.Equal("FetchEventOdds", providerError.GetProperty("operation").GetString());
        Assert.Contains("bad_market", providerError.GetProperty("sanitizedRequestUrl").GetString());
        Assert.DoesNotContain("TEST_THE_ODDS_API_KEY", providerError.GetProperty("sanitizedRequestUrl").GetString());
        Assert.Contains("Invalid market", providerError.GetProperty("rawProviderBody").GetString());
        var rejectedInputs = json.GetProperty("diagnostics").GetProperty("likelyRejectedInputs");
        Assert.Equal("soccer_fifa_world_cup", rejectedInputs.GetProperty("sportKey").GetString());
        Assert.Equal("bad_market", rejectedInputs.GetProperty("markets").GetString());
        Assert.Equal("soccer-event-1", rejectedInputs.GetProperty("eventId").GetString());
        Assert.False(json.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal(2, factory.TheOddsAdapter.CallCount);
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_DefaultsToSportsbookContext_PersistsResearchOnlyRows()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_goals,player_assists,player_shots,player_shots_on_target,player_tackles",
            ["TheOddsApi:SoccerWorldCupSportsbookRegion"] = "us",
            ["TheOddsApi:SoccerWorldCupDfsRegion"] = "us_dfs"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupSportsbookContextPayload();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            date = "2026-06-21",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("Sportsbook", json.GetProperty("providerCategory").GetString());
        Assert.Equal("us", json.GetProperty("region").GetString());
        Assert.True(json.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.True(json.GetProperty("sportsbookContextNotDfsPlayable").GetBoolean());
        Assert.Equal(1, json.GetProperty("platformPropsUpserted").GetInt32());
        Assert.Equal(1, json.GetProperty("sportsbookContextRowsCreated").GetInt32());
        Assert.Equal(0, json.GetProperty("dfsRowsCreated").GetInt32());
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("configuredMarketsReturned").EnumerateArray(), row => row.GetString() == "player_goals");
        Assert.Contains(json.GetProperty("diagnostics").GetProperty("configuredMarketsMissing").EnumerateArray(), row => row.GetString() == "player_tackles");

        var metadata = await GetSingleSoccerPlatformPropMetadataAsync(factory);
        Assert.Equal("Sportsbook", metadata.GetProperty("providerCategory").GetString());
        Assert.Equal("us", metadata.GetProperty("region").GetString());
        Assert.False(metadata.GetProperty("isActionableForUser").GetBoolean());
        Assert.False(metadata.GetProperty("isIndicativeOdds").GetBoolean());
        var entry = metadata.GetProperty("entry");
        Assert.Equal("Sportsbook", entry.GetProperty("providerCategory").GetString());
        Assert.False(entry.GetProperty("actionableForUser").GetBoolean());
        Assert.Contains("research-only", entry.GetProperty("ruleWarning").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("soccer_fifa_world_cup", metadata.GetProperty("sport").GetProperty("providerSportKey").GetString());
        Assert.Equal("World Cup", metadata.GetProperty("sport").GetProperty("competition").GetString());
        Assert.Equal("betmgm", metadata.GetProperty("bookmaker").GetProperty("key").GetString());
        Assert.Equal("player_goals", metadata.GetProperty("market").GetProperty("key").GetString());
        var eventInfo = metadata.GetProperty("eventInfo");
        Assert.Equal("soccer-event-1", eventInfo.GetProperty("providerEventId").GetString());
        Assert.Equal("Home FC", eventInfo.GetProperty("homeTeam").GetString());
        Assert.Equal("Away FC", eventInfo.GetProperty("awayTeam").GetString());
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_DryRunForceLive_PlansEligibleWindowEvents_AndWritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_goals,player_shots",
            ["TheOddsApi:SoccerWorldCupSportsbookRegion"] = "us"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupMultiEventSportsbookContextPayload();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            fromDate = "2026-06-21",
            toDate = "2026-06-24",
            dryRun = true,
            forceLive = true,
            maxEvents = 2,
            includeSportsbook = true,
            bookmakers = new[] { "fanduel", "betmgm", "betrivers" },
            markets = new[] { "player_goals", "player_shots" }
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("dryRun").GetBoolean());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal("Sportsbook", json.GetProperty("providerCategory").GetString());
        Assert.Equal("us", json.GetProperty("region").GetString());
        Assert.Equal(3, json.GetProperty("eventsDiscovered").GetInt32());
        Assert.Equal(3, json.GetProperty("eventsEligible").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsSkipped").GetInt32());
        Assert.Equal(2, json.GetProperty("eventsToFetch").GetInt32());
        Assert.Equal(3, json.GetProperty("estimatedApiCalls").GetInt32());
        Assert.Equal(1, json.GetProperty("skipReasons").GetProperty("maxEventsLimit").GetInt32());
        Assert.Equal(2, json.GetProperty("perEventExpectedRequests").GetArrayLength());
        Assert.Equal(1, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task SportNeutralSoccerRefresh_RealMode_PersistsMultipleProviderEvents_RespectingMaxEvents()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_goals,player_shots",
            ["TheOddsApi:SoccerWorldCupSportsbookRegion"] = "us"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupMultiEventSportsbookContextPayload();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            fromDate = "2026-06-21",
            toDate = "2026-06-24",
            dryRun = false,
            forceLive = true,
            maxEvents = 2,
            includeSportsbook = true,
            bookmakers = new[] { "fanduel", "betmgm", "betrivers" },
            markets = new[] { "player_goals", "player_shots" }
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("Sportsbook", json.GetProperty("providerCategory").GetString());
        Assert.Equal("us", json.GetProperty("region").GetString());
        Assert.True(json.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.True(json.GetProperty("sportsbookContextNotDfsPlayable").GetBoolean());
        Assert.False(json.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal(3, json.GetProperty("eventsDiscovered").GetInt32());
        Assert.Equal(3, json.GetProperty("eventsEligible").GetInt32());
        Assert.Equal(2, json.GetProperty("eventsFetched").GetInt32());
        Assert.Equal(2, json.GetProperty("eventsWithRows").GetInt32());
        Assert.Equal(0, json.GetProperty("eventsWithoutRows").GetInt32());
        Assert.Equal(2, json.GetProperty("providerEventsPersisted").GetInt32());
        Assert.Equal(2, json.GetProperty("platformPropsUpserted").GetInt32());
        Assert.Equal(2, json.GetProperty("sportsbookContextRowsCreated").GetInt32());
        Assert.Equal(0, json.GetProperty("dfsRowsCreated").GetInt32());
        Assert.Equal(3, factory.TheOddsAdapter.CallCount);

        var rowsByProviderEvent = json.GetProperty("rowsByProviderEvent");
        Assert.Equal(1, rowsByProviderEvent.GetProperty("soccer-event-1").GetInt32());
        Assert.Equal(1, rowsByProviderEvent.GetProperty("soccer-event-2").GetInt32());
        Assert.False(rowsByProviderEvent.TryGetProperty("soccer-event-3", out _));
        Assert.Equal(2, await CountSoccerPlatformPropsAsync(factory));

        var metadataRows = await GetSoccerPlatformPropMetadataAsync(factory);
        Assert.Equal(2, metadataRows.Length);
        Assert.All(metadataRows, metadata =>
        {
            Assert.Equal("the-odds-provider-context-v2", metadata.GetProperty("contextSchemaVersion").GetString());
            Assert.Equal("Sportsbook", metadata.GetProperty("providerCategory").GetString());
            Assert.False(metadata.GetProperty("isActionableForUser").GetBoolean());
            Assert.Equal("soccer_fifa_world_cup", metadata.GetProperty("sport").GetProperty("providerSportKey").GetString());
            Assert.Equal("World Cup", metadata.GetProperty("sport").GetProperty("competition").GetString());
            var providerEventId = metadata.GetProperty("eventInfo").GetProperty("providerEventId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(providerEventId));
            Assert.DoesNotContain("|balanced", providerEventId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("|alternate", providerEventId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SCR_game", providerEventId, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("zeroBookmakers", 0, 0, 0, "NoBookmakersReturned")]
    [InlineData("zeroMarkets", 1, 0, 0, "NoMarketsReturned")]
    [InlineData("unsupportedMarket", 1, 1, 2, "UnsupportedMarketsReturned")]
    [InlineData("configuredMarketZeroOutcomes", 1, 1, 0, "ConfiguredMarketsReturnedWithNoOutcomes")]
    public async Task SportNeutralSoccerRefresh_ZeroRowDiagnostics_ExplainProviderShape(
        string payloadMode,
        int expectedBookmakerCount,
        int expectedMarketCount,
        int expectedOutcomeCount,
        string expectedSkipReason)
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupContextPayload(payloadMode);
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/the-odds/markets/refresh-current", new
        {
            sport = "SOCCER",
            competition = "World Cup",
            date = "2026-06-21",
            dryRun = false,
            forceLive = true,
            includeDfs = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.GetProperty("platformPropsUpserted").GetInt32());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
        var diagnostics = json.GetProperty("diagnostics");
        Assert.Equal(1, diagnostics.GetProperty("eventsFetched").GetInt32());
        Assert.Equal(1, diagnostics.GetProperty("events").GetArrayLength());
        var eventDiagnostics = diagnostics.GetProperty("events")[0];
        Assert.Equal("soccer-event-1", eventDiagnostics.GetProperty("providerEventId").GetString());
        Assert.Equal(expectedBookmakerCount, eventDiagnostics.GetProperty("bookmakerCount").GetInt32());
        Assert.Equal(expectedMarketCount, eventDiagnostics.GetProperty("marketCount").GetInt32());
        Assert.Equal(expectedOutcomeCount, eventDiagnostics.GetProperty("outcomeCount").GetInt32());
        Assert.Equal(0, eventDiagnostics.GetProperty("normalizedPropsCount").GetInt32());
        Assert.Contains(
            eventDiagnostics.GetProperty("skipReasons").EnumerateArray(),
            row => row.GetString() == expectedSkipReason);

        if (payloadMode == "unsupportedMarket")
        {
            Assert.Contains(json.GetProperty("soccerMarketsUnsupported").EnumerateArray(), row => row.GetString() == "player_passes");
            Assert.True(diagnostics.GetProperty("providerMarketsReturnedButNoRowsMapped").GetBoolean());
        }

        if (payloadMode is "zeroBookmakers" or "zeroMarkets")
        {
            Assert.True(diagnostics.GetProperty("noProviderMarketsReturned").GetBoolean());
        }
    }

    [Fact]
    public async Task SoccerWorldCupProviderAvailability_WithoutForceLive_DoesNotCallProviderOrWrite()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/debug/the-odds/soccer/world-cup/provider-availability?slateDate=2026-06-21");

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("blocked").GetBoolean());
        Assert.True(json.GetProperty("requiresForceLive").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task SoccerWorldCupProviderAvailability_ComparesRegions_WithoutWritingRows()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "player_shots"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupAvailabilityByRegion();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/debug/the-odds/soccer/world-cup/provider-availability?slateDate=2026-06-21&regions=us_dfs,us&markets=player_shots&maxEvents=1&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal(3, json.GetProperty("estimatedLiveCalls").GetInt32());
        Assert.Equal(3, json.GetProperty("actualLiveCalls").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsFetched").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsMatchedToSlate").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsWithBookmakers").GetInt32());
        Assert.Equal(1, json.GetProperty("eventsWithMarkets").GetInt32());
        Assert.True(json.GetProperty("hasAnyProviderLines").GetBoolean());
        var events = json.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(2, events.Length);
        var dfsEvent = Assert.Single(events, row => row.GetProperty("requestedRegions").GetString() == "us_dfs");
        Assert.Equal(0, dfsEvent.GetProperty("bookmakerCount").GetInt32());
        var usEvent = Assert.Single(events, row => row.GetProperty("requestedRegions").GetString() == "us");
        Assert.Equal(1, usEvent.GetProperty("bookmakerCount").GetInt32());
        Assert.Contains(usEvent.GetProperty("bookmakerKeysReturned").EnumerateArray(), row => row.GetString() == "prizepicks");
        Assert.Contains(usEvent.GetProperty("configuredMarketsReturned").EnumerateArray(), row => row.GetString() == "player_shots");
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
    }

    [Fact]
    public async Task SoccerWorldCupProviderAvailability_Provider422_IsStructuredAndDoesNotWriteRows()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsApi:SoccerWorldCupSportKey"] = "soccer_fifa_world_cup",
            ["TheOddsApi:SoccerWorldCupMarkets"] = "bad_market"
        });
        factory.TheOddsAdapter.ConfigureSoccerWorldCupProviderRejection();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/debug/the-odds/soccer/world-cup/provider-availability?slateDate=2026-06-21&regions=us_dfs&markets=bad_market&maxEvents=1&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("wouldWriteRows").GetBoolean());
        Assert.Equal(1, json.GetProperty("providerRejectedCount").GetInt32());
        var eventRow = Assert.Single(json.GetProperty("events").EnumerateArray());
        Assert.True(eventRow.GetProperty("providerRejectedRequest").GetBoolean());
        Assert.False(eventRow.GetProperty("eventOddsHttpSucceeded").GetBoolean());
        var providerError = eventRow.GetProperty("providerError");
        Assert.Equal(422, providerError.GetProperty("statusCode").GetInt32());
        Assert.Equal("FetchEventOdds", providerError.GetProperty("operation").GetString());
        Assert.Contains("bad_market", providerError.GetProperty("sanitizedRequestUrl").GetString());
        Assert.DoesNotContain("TEST_THE_ODDS_API_KEY", providerError.GetProperty("sanitizedRequestUrl").GetString());
        Assert.Contains("Invalid market", providerError.GetProperty("rawProviderBody").GetString());
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(0, await CountSoccerPlatformPropsAsync(factory));
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
        Assert.False(topPicks.GetProperty("topPicks")[0].GetProperty("entryActionableForUser").GetBoolean());
        Assert.True(topPicks.GetProperty("topPicks")[0].GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.Equal("Sportsbook", topPicks.GetProperty("topPicks")[0].GetProperty("entryProviderCategory").GetString());
        Assert.Equal(0, topPicks.GetProperty("summary").GetProperty("actionableCandidateCount").GetInt32());
        Assert.True(lineShopping.GetProperty("snapshotBacked").GetBoolean());
        Assert.Equal("SOCCER", lineShopping.GetProperty("comparisonGroups")[0].GetProperty("sport").GetString());
        Assert.False(lineShopping.GetProperty("comparisonGroups")[0].GetProperty("entryActionableForUser").GetBoolean());
        Assert.True(lineShopping.GetProperty("comparisonGroups")[0].GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.Equal("Sportsbook", lineShopping.GetProperty("comparisonGroups")[0].GetProperty("entryProviderCategory").GetString());
        Assert.Equal(0, lineShopping.GetProperty("summary").GetProperty("actionableComparisonGroups").GetInt32());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(before, await factory.CountArtifactsAsync());
    }

    [Fact]
    public async Task SoccerPerformanceSummary_RemainsUnavailable_WithoutCallingProviders()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.SeedSoccerWorldCupSnapshotsAsync();
        var before = await factory.CountArtifactsAsync();

        var response = await client.GetAsync("/api/markets/performance-summary?sport=SOCCER&fromDate=2026-06-21&toDate=2026-06-24");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SOCCER", json.GetProperty("sport").GetString());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("gradedPicks").GetInt32());
        Assert.Equal("Soccer history is not available yet.", json.GetProperty("unavailableReason").GetString());
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
        await factory.EnsureDatabaseAsync();

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
        Assert.Equal(0, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
    }

    [Fact]
    public async Task ProviderStatMappingsDebug_ReturnsSeededApiSportsSoccerMappings()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.GetAsync("/api/debug/provider-stat-mappings?sport=SOCCER&providerKey=api-sports-football&competitionScope=World%20Cup&season=2026&includeSamples=true&maxSamples=100");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(20, json.GetProperty("totalMappings").GetInt32());
        Assert.Equal(20, json.GetProperty("activeMappings").GetInt32());
        var publicGradingGroup = Assert.Single(json.GetProperty("counts").GetProperty("byPublicGradingEnabled").EnumerateArray());
        Assert.False(publicGradingGroup.GetProperty("publicGradingEnabled").GetBoolean());
        Assert.Equal(20, publicGradingGroup.GetProperty("count").GetInt32());
        Assert.Contains(json.GetProperty("samples").EnumerateArray(), row =>
            row.GetProperty("displayName").GetString() == "Shots On Target"
            && row.GetProperty("providerStatFields").EnumerateArray().Single().GetString() == "shots.on");
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_UsesDbProviderStatMappings_WhenConfigured()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var mappingDiagnostics = json.GetProperty("mappingDiagnostics");
        Assert.Equal("Database", mappingDiagnostics.GetProperty("mappingSource").GetString());
        Assert.Equal(20, mappingDiagnostics.GetProperty("providerMappingsConfigured").GetInt32());
        Assert.Equal(20, mappingDiagnostics.GetProperty("dbMappingsUsed").GetInt32());
        Assert.Equal(0, mappingDiagnostics.GetProperty("fallbackMappingsUsed").GetInt32());
        Assert.True(json.GetProperty("resultRowsPreviewed").GetInt32() > 0);
        Assert.Contains(json.GetProperty("previewRowsByPlayer").EnumerateArray()
                .SelectMany(group => group.GetProperty("rows").EnumerateArray()),
            row => row.GetProperty("mappingSource").GetString() == "Database"
                && row.GetProperty("publicGradingEnabled").GetBoolean() == false);
    }

    [Fact]
    public async Task ApiSportsSoccerImportFixture_ReportsMissingMapping_WhenDbMappingIsAbsent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync(excludeCanonicalStatKey: "SHOTS");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("mappingDiagnostics").GetProperty("missingMappingCount").GetInt32() > 0);
        var missingMapping = Assert.Single(json.GetProperty("unsupportedSkippedSummary").EnumerateArray(), row => row.GetProperty("reason").GetString() == "MissingMapping");
        Assert.Contains(missingMapping.GetProperty("props").EnumerateArray(), prop => prop.GetString() == "shots.total");
        Assert.DoesNotContain(json.GetProperty("previewRowsByPlayer").EnumerateArray()
                .SelectMany(group => group.GetProperty("rows").EnumerateArray()),
            row => row.GetProperty("propType").GetString() == "Shots");
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
        var providerLinksAfterFirst = await CountApiSportsSoccerProviderPlayerLinksAsync(factory);

        var secondResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/import-fixture", new
        {
            fixtureId = "1489393",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(secondResponse);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(inserted > 0);
        Assert.Equal(2, providerLinksAfterFirst);
        Assert.Equal(providerLinksAfterFirst, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
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
        await factory.EnsureDatabaseAsync();

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
    public async Task ApiSportsWorldCupBackfill_BlocksWithoutForceLive()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-13",
            dryRun = true,
            forceLive = false
        });

        await AssertStatusWithBodyAsync(response, HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.Equal(0, factory.ApiSportsFootballClient.FixturesCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupBackfill_DryRunDiscoversCompletedAndSkipsOpenFixturesWithoutWriting()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-13",
            dryRun = true,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(3, json.GetProperty("discoveredFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(2, json.GetProperty("skippedFixtureCount").GetInt32());
        Assert.Equal(0, json.GetProperty("resultRowsWritten").GetInt32());
        Assert.Equal(0, await CountApiSportsSoccerResultsAsync(factory));
        var skippedReasons = json.GetProperty("skippedFixtures").EnumerateArray().Select(row => row.GetProperty("reason").GetString()).ToArray();
        Assert.Contains("Scheduled", skippedReasons);
        Assert.Contains("InProgress", skippedReasons);
    }

    [Fact]
    public async Task ApiSportsWorldCupBackfill_WritesCompletedFixturesIdempotently()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var firstResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(firstResponse);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inserted = first.GetProperty("resultRowsInserted").GetInt32();

        var secondResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
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
    public async Task ApiSportsWorldCupBackfill_ReportsPerFixtureFailureAndContinues()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-14",
            dryRun = false,
            forceLive = true
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("failedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("importedFixtureCount").GetInt32());
        Assert.True(json.GetProperty("resultRowsInserted").GetInt32() > 0);
        var failure = Assert.Single(json.GetProperty("failures").EnumerateArray());
        Assert.Equal("99999", failure.GetProperty("fixtureId").GetString());
    }

    [Fact]
    public async Task ApiSportsWorldCupImportStatus_ReturnsPersistedOnlyByDefault()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/import-status?fromDate=2026-06-11&toDate=2026-06-11");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("discoveryAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("discoveredFixtureCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("completedFixtureCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("missingCompletedFixtureCount").ValueKind);
        Assert.Equal(1, json.GetProperty("importedFixtureCount").GetInt32());
        Assert.True(json.GetProperty("totalRows").GetInt32() > 0);
        Assert.NotEmpty(json.GetProperty("rowsByFixture").EnumerateArray());
        Assert.Equal("1489393", json.GetProperty("latestImportedFixtureId").GetString());
        Assert.Equal("2026-06-11", json.GetProperty("latestImportedEventDate").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("latestImportedAtUtc").ValueKind);
    }

    [Fact]
    public async Task ApiSportsWorldCupImportStatus_WithForceLiveReportsDiscoveryCounts()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/import-status?fromDate=2026-06-11&toDate=2026-06-14&forceLive=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("discoveryAvailable").GetBoolean());
        Assert.Equal(4, json.GetProperty("discoveredFixtureCount").GetInt32());
        Assert.Equal(2, json.GetProperty("completedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("importedFixtureCount").GetInt32());
        Assert.Equal(1, json.GetProperty("missingCompletedFixtureCount").GetInt32());
        var missing = Assert.Single(json.GetProperty("missingCompletedFixtures").EnumerateArray());
        Assert.Equal("99999", missing.GetProperty("fixtureId").GetString());
    }

    [Fact]
    public async Task ApiSportsWorldCupCoverage_ReturnsPersistedResultCountsReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var before = await CountApiSportsSoccerResultsAsync(factory);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(before, json.GetProperty("importedResultSummary").GetProperty("totalRows").GetInt32());
        Assert.Equal(1, json.GetProperty("importedResultSummary").GetProperty("distinctFixtures").GetInt32());
        Assert.NotEmpty(json.GetProperty("importedResultSummary").GetProperty("rowsByPropType").EnumerateArray());
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
    }

    [Fact]
    public async Task ApiSportsWorldCupCoverage_ReportsNoMarketDataGracefully_WithSamples()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var join = json.GetProperty("marketJoinCoverage");
        Assert.True(join.GetProperty("noMarketDataAvailable").GetBoolean());
        Assert.Equal(0, join.GetProperty("matchedAny").GetInt32());
        Assert.True(join.GetProperty("unmatchedCount").GetInt32() > 0);
        Assert.True(json.GetProperty("mismatchBreakdown").GetProperty("marketMissing").GetInt32() > 0);
        Assert.NotEmpty(json.GetProperty("samples").GetProperty("marketMissing").EnumerateArray());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
    }

    [Fact]
    public async Task ApiSportsWorldCupMarketJoinPreview_DetectsEventPlayerPropOverlap_ReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync();
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/market-join-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());

        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("importedResultRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("candidatePlatformRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("eventMatchedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("playerNameMatchedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("propTypeMatchedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("playerAndPropMatchedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("fullyJoinableRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("unmatchedResultRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("unmatchedPlatformRows").GetInt32());

        var matched = Assert.Single(json.GetProperty("samples").GetProperty("matched").EnumerateArray());
        Assert.Equal("1489393", matched.GetProperty("apiSportsFixtureId").GetString());
        Assert.Equal("Test Forward", matched.GetProperty("apiSportsPlayerName").GetString());
        Assert.Equal("GOALS", matched.GetProperty("apiSportsStatKey").GetString());
        Assert.Equal("852a3abe6d46820720febc0fedebff9a", matched.GetProperty("providerEventId").GetString());
        Assert.Equal("player_goals", matched.GetProperty("providerMarketKey").GetString());
        Assert.Equal("betmgm", matched.GetProperty("bookmaker").GetString());

        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupMarketJoinPreview_ReportsMissingProviderContextBlockers()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            includeProviderEventId: false,
            includeResultTeam: false,
            includeMarketKey: false);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/market-join-preview?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var blockers = json.GetProperty("blockers");
        Assert.Equal(1, blockers.GetProperty("missingProviderEventId").GetInt32());
        Assert.Equal(1, blockers.GetProperty("missingApiSportsFixtureIdOnMarketRows").GetInt32());
        Assert.True(blockers.GetProperty("missingPlayerTeam").GetInt32() >= 2);
        Assert.Equal(1, blockers.GetProperty("missingProviderPlayerId").GetInt32());
        Assert.True(blockers.GetProperty("propTypeMappingMissing").GetInt32() > 0);
        Assert.NotEmpty(json.GetProperty("samples").GetProperty("unmatchedResults").EnumerateArray());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
    }

    [Fact]
    public async Task ApiSportsWorldCupPropMappingGap_IdentifiesMissingTheOddsMarketMapping_ReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_crosses",
            platformPropTypeName: "Crosses",
            platformStatKey: "CROSSES",
            apiSportsPropTypeName: "Crosses",
            apiSportsStatKey: "CROSSES");
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Contains(
            json.GetProperty("theOddsMarketGaps").EnumerateArray(),
            row => row.GetProperty("providerMarketKey").GetString() == "player_crosses"
                && row.GetProperty("category").GetString() == "TheOddsMarketKeyMissingCanonicalMapping");
        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupPropMappingGap_IdentifiesMissingApiSportsStatMapping()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_goals",
            platformPropTypeName: "Goals",
            platformStatKey: "GOALS",
            apiSportsPropTypeName: "Mystery Stat",
            apiSportsStatKey: "MYSTERY_STAT");

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            json.GetProperty("apiSportsStatGaps").EnumerateArray(),
            row => row.GetProperty("apiSportsStatKey").GetString() == "MYSTERYSTAT"
                && row.GetProperty("category").GetString() == "ApiSportsStatKeyMissingProviderMapping");
    }

    [Fact]
    public async Task ApiSportsWorldCupPropMappingGap_SeparatesImportedStatNoise()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_goals",
            platformPropTypeName: "Goals",
            platformStatKey: "GOALS",
            apiSportsPropTypeName: "Passes Attempted",
            apiSportsStatKey: "PASSES_ATTEMPTED");
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            json.GetProperty("apiSportsStatGaps").EnumerateArray(),
            row => row.GetProperty("apiSportsStatKey").GetString() == "PASSES_ATTEMPTED"
                && row.GetProperty("category").GetString() == "ImportedStatNotRequestedByTheOdds"
                && row.GetProperty("recommendation").GetString() == "IgnoreImportedStatNoise");
    }

    [Fact]
    public async Task ApiSportsWorldCupPropMappingGap_DetectsMappingExistsButNotGradeable()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            platformMarketKey: "player_goals",
            platformPropTypeName: "Goals",
            platformStatKey: "GOALS",
            apiSportsPropTypeName: "Goal + Assist",
            apiSportsStatKey: "GOAL_ASSIST");
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            json.GetProperty("apiSportsStatGaps").EnumerateArray(),
            row => row.GetProperty("apiSportsStatKey").GetString() == "GOAL_ASSIST"
                && row.GetProperty("category").GetString() == "MappingExistsButNotGradeable"
                && row.GetProperty("isGradeable").GetBoolean() == false);
    }

    [Fact]
    public async Task SoccerWorldCupSpikeStatus_IsResearchOnlyReadOnlyAndNoExternalApis()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync();
        await factory.SeedApiSportsSoccerProviderStatMappingsAsync();
        await AddSoccerTheOddsRefreshRunAsync(factory);
        var before = await factory.CountArtifactsAsync();
        var apiSportsCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;
        var theOddsCallsBefore = factory.TheOddsAdapter.CallCount;

        var response = await client.GetAsync("/api/debug/soccer/world-cup/spike-status?fromDate=2026-06-21&toDate=2026-06-24&includeDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal("SOCCER", json.GetProperty("sport").GetString());
        Assert.Equal("World Cup", json.GetProperty("competition").GetString());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.False(json.GetProperty("entryBuilderEnabled").GetBoolean());

        var readiness = json.GetProperty("productionReadiness");
        Assert.Equal("ResearchOnly", readiness.GetProperty("status").GetString());
        Assert.False(readiness.GetProperty("canShowPublicGradedPerformance").GetBoolean());
        Assert.False(readiness.GetProperty("canUseForEntryBuilder").GetBoolean());

        var guardrails = json.GetProperty("guardrails");
        Assert.True(guardrails.GetProperty("publicEndpointsReadOnly").GetBoolean());
        Assert.True(guardrails.GetProperty("publicSoccerGradingDisabled").GetBoolean());
        Assert.True(guardrails.GetProperty("soccerEntryBuilderDisabled").GetBoolean());
        Assert.True(guardrails.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.True(guardrails.GetProperty("autoAliasApprovalDisabled").GetBoolean());
        Assert.True(guardrails.GetProperty("autoStatMappingApprovalDisabled").GetBoolean());
        Assert.True(guardrails.GetProperty("needsConfirmationMappingsNotPublicGradeable").GetBoolean());

        Assert.True(json.GetProperty("summary").GetProperty("totalCandidatePlatformRows").GetInt32() >= 1);
        Assert.NotEmpty(json.GetProperty("blockers").EnumerateArray());
        Assert.Equal(before, await factory.CountArtifactsAsync());
        Assert.Equal(apiSportsCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
        Assert.Equal(theOddsCallsBefore, factory.TheOddsAdapter.CallCount);
    }

    [Theory]
    [InlineData(1, 0.5, true, false, "OverWin", 1, 0, 0, 0)]
    [InlineData(0, 0.5, false, true, "UnderWin", 0, 1, 0, 0)]
    [InlineData(0.5, 0.5, true, false, "Push", 0, 0, 1, 0)]
    [InlineData(1, 0.5, false, false, "NoLine", 0, 0, 0, 1)]
    public async Task ApiSportsWorldCupGradingPreview_GradesJoinableRows(
        decimal actualValue,
        decimal line,
        bool overAvailable,
        bool underAvailable,
        string expectedGrade,
        int expectedOverWins,
        int expectedUnderWins,
        int expectedPushes,
        int expectedNoLineRows)
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            actualValue: actualValue,
            currentLine: line,
            overAvailable: overAvailable,
            underAvailable: underAvailable);
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());

        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("candidatePlatformRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("importedResultRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("fullyJoinableRows").GetInt32());
        Assert.Equal(expectedNoLineRows == 0 ? 1 : 0, summary.GetProperty("gradedPreviewRows").GetInt32());
        Assert.Equal(expectedOverWins, summary.GetProperty("overWins").GetInt32());
        Assert.Equal(expectedUnderWins, summary.GetProperty("underWins").GetInt32());
        Assert.Equal(expectedPushes, summary.GetProperty("pushes").GetInt32());
        Assert.Equal(expectedNoLineRows, summary.GetProperty("noLineRows").GetInt32());

        var sample = Assert.Single(json.GetProperty("samples").GetProperty("graded").EnumerateArray());
        Assert.Equal(expectedGrade, sample.GetProperty("gradeResult").GetString());
        Assert.Equal("Sportsbook", sample.GetProperty("providerCategory").GetString());
        Assert.True(sample.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.False(sample.GetProperty("entryActionableForUser").GetBoolean());
        Assert.Equal(actualValue, sample.GetProperty("actualValue").GetDecimal());
        Assert.Equal(line, sample.GetProperty("line").GetDecimal());

        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingPreview_DoesNotGradeUnjoinableRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(platformPlayerName: "Different Forward");

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-preview?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("fullyJoinableRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("gradedPreviewRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("ungradedRows").GetInt32());
        Assert.Equal(2, json.GetProperty("blockers").GetProperty("unjoinableRows").GetInt32());
        Assert.Empty(json.GetProperty("samples").GetProperty("graded").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("samples").GetProperty("unjoinableResults").EnumerateArray());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_ReturnsSafeZeroSummaryForEmptyData()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalCandidatePlatformRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalImportedResultRows").GetInt32());
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_ReportsResultsWithoutPlatformRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(includePlatformRow: false);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(1, summary.GetProperty("resultOnlyFixtures").GetInt32());
        Assert.Empty(json.GetProperty("fixtureDetails").EnumerateArray());
        var resultOnly = Assert.Single(json.GetProperty("resultOnlyFixtures").EnumerateArray());
        Assert.Equal("1489393", resultOnly.GetProperty("fixtureId").GetString());
        Assert.Equal(1, resultOnly.GetProperty("importedResultRows").GetInt32());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_ReportsPlatformRowsWithoutResults()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(includeResultRow: false);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(1, summary.GetProperty("unresolvedProviderEvents").GetInt32());
        Assert.Empty(json.GetProperty("fixtureDetails").EnumerateArray());
        var unresolved = Assert.Single(json.GetProperty("unresolvedProviderEvents").EnumerateArray());
        Assert.Equal("852a3abe6d46820720febc0fedebff9a", unresolved.GetProperty("providerEventId").GetString());
        Assert.Equal(1, unresolved.GetProperty("candidatePlatformRows").GetInt32());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_BucketsSyntheticProviderEventsSeparately()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            includeResultRow: false,
            useTheOddsContext: false,
            providerEventIdOverride: "320878-50-SCR_game_8upFm4DZHyMeLIkeLhuBs52o-11-false");

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(0, summary.GetProperty("unresolvedProviderEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("platformOnlyEvents").GetInt32());
        Assert.Empty(json.GetProperty("fixtureDetails").EnumerateArray());
        var synthetic = Assert.Single(json.GetProperty("platformOnlyEvents").EnumerateArray());
        Assert.Equal("NotTheOddsSoccerWorldCupProviderEvent", synthetic.GetProperty("reason").GetString());
        Assert.Equal("320878-50-SCR_game_8upFm4DZHyMeLIkeLhuBs52o-11-false", synthetic.GetProperty("providerEventId").GetString());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_UsesApprovedAliasLinks()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Maximiliano Araujo",
            platformPlayerName: "Maximiliano Javier Araujo Vilches",
            fixtureId: "1489398");
        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false
        });
        await EnsureSuccessWithBodyAsync(writeResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true&includeSamples=true&maxSamplesPerFixture=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("knownWorkingPairDetected").GetBoolean());
        var detail = Assert.Single(json.GetProperty("fixtureDetails").EnumerateArray());
        Assert.Equal(0, detail.GetProperty("fullyJoinableRowsBeforeAlias").GetInt32());
        Assert.Equal(1, detail.GetProperty("fullyJoinableRowsAfterAlias").GetInt32());
        Assert.Equal(1, detail.GetProperty("gradedPreviewRows").GetInt32());
        Assert.Equal(1, detail.GetProperty("aliasLinkedPlayers").GetInt32());
        Assert.NotEmpty(detail.GetProperty("sampleAliasLinkedRows").EnumerateArray());
    }

    [Fact]
    public async Task ApiSportsWorldCupGradingCoverageSummary_AggregatesBlockersAndIsReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(platformPlayerName: "Different Forward");
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var linksBefore = await CountTheOddsSoccerAliasLinksAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-21&toDate=2026-06-21&includeFixtureDetails=true&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(1, json.GetProperty("topBlockersAggregated").GetProperty("playerNameMismatch").GetInt32());
        Assert.True(json.GetProperty("topBlockersAggregated").GetProperty("unjoinableRows").GetInt32() > 0);
        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(linksBefore, await CountTheOddsSoccerAliasLinksAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_ResolvesKnownWorkingProviderEvent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(fixtureId: "1489398");
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeResolved=true&includeUnresolved=true&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.True(json.GetProperty("knownWorkingPairDetected").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("providerEventsEvaluated").GetInt32());
        Assert.Equal(1, summary.GetProperty("resolvedEvents").GetInt32());
        Assert.Equal(0, summary.GetProperty("unresolvedEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("resolvedPlatformRows").GetInt32());
        var resolved = Assert.Single(json.GetProperty("resolvedEvents").EnumerateArray());
        Assert.Equal("852a3abe6d46820720febc0fedebff9a", resolved.GetProperty("providerEventId").GetString());
        Assert.Equal("1489398", resolved.GetProperty("resolvedFixtureId").GetString());
        Assert.Equal("Resolved", resolved.GetProperty("resolutionStatus").GetString());
        Assert.True(resolved.GetProperty("confidence").GetDecimal() >= 0.80m);
        Assert.Contains(resolved.GetProperty("matchReasons").EnumerateArray(), row => row.GetString() == "ExactEventDate");
        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_ExcludesSyntheticProviderEvents()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            includeResultRow: false,
            providerEventIdOverride: "852a3abe6d46820720febc0fedebff9a|balanced");

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeResolved=true&includeUnresolved=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("providerEventsEvaluated").GetInt32());
        Assert.Equal(0, summary.GetProperty("resolvedEvents").GetInt32());
        Assert.Equal(0, summary.GetProperty("unresolvedEvents").GetInt32());
        var excluded = Assert.Single(json.GetProperty("excludedEvents").EnumerateArray());
        Assert.Equal("Excluded", excluded.GetProperty("resolutionStatus").GetString());
        Assert.Equal("852a3abe6d46820720febc0fedebff9a|balanced", excluded.GetProperty("providerEventId").GetString());
        Assert.Contains(excluded.GetProperty("blockers").EnumerateArray(), row => row.GetString() == "NotTheOddsSoccerWorldCupProviderEvent");
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_ReportsNoCandidateEvents()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            includeResultRow: false,
            eventHomeTeamName: "Uruguay",
            eventAwayTeamName: "Cape Verde");
        await AddApiSportsSoccerResultFixtureAsync(factory, "1489400", new DateOnly(2026, 6, 25), "Mexico");

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-25&includeResolved=true&includeUnresolved=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("providerEventsEvaluated").GetInt32());
        Assert.Equal(0, summary.GetProperty("resolvedEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("noCandidateEvents").GetInt32());
        var unresolved = Assert.Single(json.GetProperty("unresolvedEvents").EnumerateArray());
        Assert.Equal("NoCandidate", unresolved.GetProperty("resolutionStatus").GetString());
        Assert.Contains(unresolved.GetProperty("blockers").EnumerateArray(), row => row.GetString() == "NoFixtureCandidates");
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_ReportsAmbiguousEvents()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(fixtureId: "1489398");
        await AddApiSportsSoccerResultFixtureAsync(factory, "1489399", new DateOnly(2026, 6, 21), "Uruguay");

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeResolved=true&includeUnresolved=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("resolvedEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("ambiguousEvents").GetInt32());
        var unresolved = Assert.Single(json.GetProperty("unresolvedEvents").EnumerateArray());
        Assert.Equal("Ambiguous", unresolved.GetProperty("resolutionStatus").GetString());
        Assert.Contains(unresolved.GetProperty("blockers").EnumerateArray(), row => row.GetString() == "MultipleHighConfidenceFixtures");
        Assert.True(unresolved.GetProperty("candidateApiSportsFixtures").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task TheOddsApiSportsFixtureMappingCoverage_UsesWorldCupTeamAliases()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            fixtureId: "1489398",
            eventHomeTeamName: "Uruguay",
            eventAwayTeamName: "Cape Verde",
            resultPlayerTeamName: "Cape Verde Islands");

        var response = await client.GetAsync("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeResolved=true&includeUnresolved=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var resolved = Assert.Single(json.GetProperty("resolvedEvents").EnumerateArray());
        Assert.Equal("Resolved", resolved.GetProperty("resolutionStatus").GetString());
        Assert.Equal("1489398", resolved.GetProperty("resolvedFixtureId").GetString());
        Assert.Contains(resolved.GetProperty("matchReasons").EnumerateArray(), row => row.GetString() == "OneTeamMatch");
    }

    [Fact]
    public async Task TheOddsSoccerWorldCupIngestionCoverage_CountsRealLegacyAndApiSportsGaps_ReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(fixtureId: "1489398");
        await AddLegacySoccerPlatformPropAsync(factory, "320878-50-SCR_game_test");
        await AddApiSportsSoccerResultFixtureAsync(factory, "1489400", new DateOnly(2026, 6, 22), "Mexico");
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/the-odds/world-cup/ingestion-coverage?fromDate=2026-06-21&toDate=2026-06-22&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("realTheOddsProviderEvents").GetInt32());
        Assert.Equal(1, summary.GetProperty("realTheOddsPlatformRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("syntheticOrLegacySoccerRows").GetInt32());
        Assert.Equal(2, summary.GetProperty("apiSportsFixturesWithResults").GetInt32());
        Assert.Equal(1, summary.GetProperty("resolvedProviderEvents").GetInt32());
        Assert.Equal(1, json.GetProperty("realProviderEvents").GetArrayLength());
        Assert.Equal(1, json.GetProperty("syntheticOrLegacySoccerRows").GetProperty("samples").GetArrayLength());
        Assert.Equal(1, json.GetProperty("gapAnalysis").GetProperty("apiSportsFixturesWithResultsButNoRealTheOddsEvent").GetInt32());
        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsSoccerWorldCupIngestionCoverage_SurfacesLatestSoccerRefreshDiagnostics()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(fixtureId: "1489398");
        var refreshRunId = await AddSoccerTheOddsRefreshRunAsync(factory);

        var response = await client.GetAsync("/api/debug/soccer/the-odds/world-cup/ingestion-coverage?fromDate=2026-06-21&toDate=2026-06-21&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = json.GetProperty("summary");
        Assert.Equal(refreshRunId, summary.GetProperty("latestSoccerRefreshRunId").GetGuid());
        Assert.True(summary.GetProperty("latestSoccerRefreshSucceeded").GetBoolean());
        Assert.Equal(34, summary.GetProperty("latestSoccerRefreshEventCount").GetInt32());
        Assert.Equal(119, summary.GetProperty("latestSoccerRefreshPlatformRowsUpserted").GetInt32());
        var refresh = json.GetProperty("latestSoccerRefreshRun");
        Assert.Equal("SOCCER", refresh.GetProperty("sport").GetString());
        Assert.Equal("Sportsbook", refresh.GetProperty("providerCategoriesIncluded").GetString());
        Assert.Equal("player_goals,player_shots", refresh.GetProperty("marketsRequested").GetString());
        Assert.Equal(34, refresh.GetProperty("diagnostics").GetProperty("eventsFetched").GetInt32());
    }

    [Theory]
    [InlineData("Test Forward", "Test Forward", "SafeAliasCandidate", true)]
    [InlineData("Mathías Olivera", "Mathias Olivera", "SafeAliasCandidate", true)]
    [InlineData("Maximiliano Araújo", "Maximiliano Javier Araujo Vilches", "SafeAliasCandidate", false)]
    [InlineData("Ryan Mendes", "Ryan Mendes da Graca", "SafeAliasCandidate", false)]
    public async Task TheOddsApiSportsAliasCandidates_ReturnsConservativeNameAliases(
        string apiSportsName,
        string theOddsName,
        string expectedAction,
        bool expectedExactNormalized)
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync([apiSportsName], [theOddsName]);
        var resultsBefore = await CountApiSportsSoccerResultsAsync(factory);
        var platformPropsBefore = await CountSoccerPlatformPropsAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("candidateCount").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("safeCandidateCount").GetInt32());
        Assert.Equal(expectedExactNormalized ? 0 : 1, json.GetProperty("summary").GetProperty("totalUnjoinedPlatformPlayers").GetInt32());

        var sample = Assert.Single(json.GetProperty("samples").GetProperty("aliasCandidates").EnumerateArray());
        Assert.Equal(theOddsName, sample.GetProperty("platformPlayerName").GetString());
        Assert.Equal(expectedAction, sample.GetProperty("suggestedAction").GetString());
        Assert.Equal(expectedExactNormalized, sample.GetProperty("exactNormalizedMatch").GetBoolean());
        var candidate = Assert.Single(sample.GetProperty("candidates").EnumerateArray());
        Assert.Equal(apiSportsName, candidate.GetProperty("apiSportsPlayerName").GetString());
        Assert.True(candidate.GetProperty("confidence").GetDecimal() >= 0.92m);

        Assert.Equal(resultsBefore, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(platformPropsBefore, await CountSoccerPlatformPropsAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsAliasCandidates_BlocksAmbiguousLongNameCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(
            ["Ryan Mendes", "Ryan Graca"],
            ["Ryan Mendes da Graca"]);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("summary").GetProperty("ambiguousCount").GetInt32());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("safeCandidateCount").GetInt32());
        var sample = Assert.Single(json.GetProperty("samples").GetProperty("aliasCandidates").EnumerateArray());
        Assert.Equal("Ambiguous", sample.GetProperty("suggestedAction").GetString());
        Assert.Equal(2, sample.GetProperty("candidates").GetArrayLength());
        Assert.Contains(sample.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "AmbiguousCandidate");
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
    }

    [Fact]
    public async Task TheOddsApiSportsAliasCandidates_ReturnsNoCandidateForWeakOverlap()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Kevin Lenini"], ["Completely Different"]);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("summary").GetProperty("noCandidateCount").GetInt32());
        Assert.Equal(1, json.GetProperty("blockers").GetProperty("insufficientTokenOverlap").GetInt32());
        var sample = Assert.Single(json.GetProperty("samples").GetProperty("noCandidate").EnumerateArray());
        Assert.Equal("NoCandidate", sample.GetProperty("suggestedAction").GetString());
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_DryRunProposesSafeLinksAndWritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Maximiliano Araujo"], ["Maximiliano Javier Araujo Vilches"]);
        var linksBefore = await CountTheOddsSoccerAliasLinksAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = true,
            minConfidence = 0.94m,
            maxLinks = 50
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksProposed").GetInt32());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("safeCandidates").GetInt32());
        Assert.Single(json.GetProperty("proposedLinks").EnumerateArray());
        Assert.Empty(json.GetProperty("writtenLinks").EnumerateArray());
        Assert.Equal(linksBefore, await CountTheOddsSoccerAliasLinksAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_WritesSafelyAndImprovesGradingPreviewJoin()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Maximiliano Araujo",
            platformPlayerName: "Maximiliano Javier Araujo Vilches");

        var beforePreview = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");
        await EnsureSuccessWithBodyAsync(beforePreview);
        var beforeJson = await beforePreview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, beforeJson.GetProperty("summary").GetProperty("fullyJoinableRows").GetInt32());

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489393",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false,
            minConfidence = 0.94m,
            maxLinks = 50
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));

        var afterPreview = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/grading-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");
        await EnsureSuccessWithBodyAsync(afterPreview);
        var afterJson = await afterPreview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, afterJson.GetProperty("summary").GetProperty("fullyJoinableRows").GetInt32());
        Assert.Equal(1, afterJson.GetProperty("summary").GetProperty("gradedPreviewRows").GetInt32());
        var sample = Assert.Single(afterJson.GetProperty("samples").GetProperty("graded").EnumerateArray());
        Assert.Contains("approvedTheOddsAliasLink", sample.GetProperty("joinMethod").GetString());
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_IsIdempotentOnSecondWrite()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Maximiliano Araujo"], ["Maximiliano Javier Araujo Vilches"]);

        var first = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false
        });
        await EnsureSuccessWithBodyAsync(first);

        var second = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false
        });

        await EnsureSuccessWithBodyAsync(second);
        var json = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksAlreadyExisting").GetInt32());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_RejectsAmbiguousAndNoCandidateRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(
            ["Ryan Mendes", "Ryan Graca"],
            ["Ryan Mendes da Graca", "Completely Different"]);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("skippedAmbiguous").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("skippedNoCandidate").GetInt32());
        Assert.Empty(json.GetProperty("writtenLinks").EnumerateArray());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLink_RespectsMinimumConfidence()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Maximiliano Araujo"], ["Maximiliano Javier Araujo Vilches"]);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            dryRun = false,
            minConfidence = 0.95m
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("skippedBelowConfidence").GetInt32());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasManualApprove_DryRunWritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Casemiro"], ["Carlos Casemiro"]);
        var candidate = await GetSingleTheOddsAliasCandidateAsync(client);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Carlos Casemiro",
            apiSportsPlayerId = candidate.ApiSportsPlayerId,
            localPlayerId = candidate.LocalPlayerId,
            dryRun = true,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal("NeedsReview", json.GetProperty("summary").GetProperty("suggestedAction").GetString());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksProposed").GetInt32());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal("Carlos Casemiro", json.GetProperty("proposedLink").GetProperty("platformPlayerName").GetString());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsAliasManualApprove_WritesNeedsReviewLinkAndIsIdempotent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Casemiro"], ["Carlos Casemiro"]);
        var candidate = await GetSingleTheOddsAliasCandidateAsync(client);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Carlos Casemiro",
            apiSportsPlayerId = candidate.ApiSportsPlayerId,
            localPlayerId = candidate.LocalPlayerId,
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal("Carlos Casemiro", json.GetProperty("writtenLink").GetProperty("platformPlayerName").GetString());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));

        var second = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Carlos Casemiro",
            apiSportsPlayerId = candidate.ApiSportsPlayerId,
            localPlayerId = candidate.LocalPlayerId,
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(second);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(secondJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(0, secondJson.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal("AlreadyLinked", secondJson.GetProperty("skipped").GetProperty("reason").GetString());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasManualApprove_RejectsAmbiguousAndNoCandidateRows()
    {
        await using var ambiguousFactory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var ambiguousClient = ambiguousFactory.CreateClient();
        await ambiguousFactory.SeedApiSportsSoccerAliasCandidateRowsAsync(
            ["Ryan Mendes", "Ryan Graca"],
            ["Ryan Mendes da Graca"]);

        var ambiguousResponse = await ambiguousClient.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Ryan Mendes da Graca",
            apiSportsPlayerId = "1000",
            localPlayerId = Guid.NewGuid(),
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(ambiguousResponse);
        var ambiguousJson = await ambiguousResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(ambiguousJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal("Ambiguous", ambiguousJson.GetProperty("skipped").GetProperty("reason").GetString());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(ambiguousFactory));

        await using var noCandidateFactory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var noCandidateClient = noCandidateFactory.CreateClient();
        await noCandidateFactory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Kevin Lenini"], ["Completely Different"]);

        var noCandidateResponse = await noCandidateClient.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Completely Different",
            apiSportsPlayerId = "1000",
            localPlayerId = Guid.NewGuid(),
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(noCandidateResponse);
        var noCandidateJson = await noCandidateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(noCandidateJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal("NoCandidate", noCandidateJson.GetProperty("skipped").GetProperty("reason").GetString());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(noCandidateFactory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasManualApprove_RejectsMismatchedCandidateIds()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerAliasCandidateRowsAsync(["Casemiro"], ["Carlos Casemiro"]);

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", new
        {
            fixtureId = "1489398",
            providerEventId = "852a3abe6d46820720febc0fedebff9a",
            platformPlayerName = "Carlos Casemiro",
            apiSportsPlayerId = "wrong-player-id",
            localPlayerId = Guid.NewGuid(),
            dryRun = false,
            reason = "manual reviewed exact soccer player identity"
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal("CandidateIdMismatch", json.GetProperty("skipped").GetProperty("reason").GetString());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasCandidatesBatch_ReusesSingleFixtureBehaviorAcrossResolvedPairs()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await SeedTwoResolvedSoccerAliasPairsAsync(factory);
        var linksBefore = await CountTheOddsSoccerAliasLinksAsync(factory);
        var providerCallsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch?fromDate=2026-06-21&toDate=2026-06-22&includeSamples=true&maxFixtures=50&maxSamples=20&minConfidence=0.94");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        var summary = json.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("resolvedPairsEvaluated").GetInt32());
        Assert.Equal(2, summary.GetProperty("platformPlayerNamesEvaluated").GetInt32());
        Assert.Equal(2, summary.GetProperty("safeCandidateCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("ambiguousCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("alreadyLinkedCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("newSafeCandidateCount").GetInt32());
        Assert.Equal(2, json.GetProperty("candidateGroups").GetArrayLength());
        Assert.True(json.GetProperty("deduplicatedCandidates").GetArrayLength() >= 2);
        var impact = json.GetProperty("estimatedCoverageImpact");
        Assert.Equal(0, impact.GetProperty("currentFullyJoinableRows").GetInt32());
        Assert.True(impact.GetProperty("projectedAdditionalJoinableRows").GetInt32() >= 2);
        Assert.Equal(linksBefore, await CountTheOddsSoccerAliasLinksAsync(factory));
        Assert.Equal(providerCallsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLinkBatch_DryRunWritesNothing_RealWriteIsIdempotent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await SeedTwoResolvedSoccerAliasPairsAsync(factory);

        var dryRun = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", new
        {
            fromDate = "2026-06-21",
            toDate = "2026-06-22",
            dryRun = true,
            minConfidence = 0.94m,
            maxLinks = 200,
            includeExactNormalizedMatches = true,
            includeTokenSubsetMatches = true
        });

        await EnsureSuccessWithBodyAsync(dryRun);
        var dryRunJson = await dryRun.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunJson.GetProperty("readOnly").GetBoolean());
        Assert.False(dryRunJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(2, dryRunJson.GetProperty("summary").GetProperty("linksProposed").GetInt32());
        Assert.Equal(0, dryRunJson.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(0, await CountTheOddsSoccerAliasLinksAsync(factory));

        var write = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", new
        {
            fromDate = "2026-06-21",
            toDate = "2026-06-22",
            dryRun = false,
            minConfidence = 0.94m,
            maxLinks = 200,
            includeExactNormalizedMatches = true,
            includeTokenSubsetMatches = true
        });

        await EnsureSuccessWithBodyAsync(write);
        var writeJson = await write.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(writeJson.GetProperty("readOnly").GetBoolean());
        Assert.True(writeJson.GetProperty("writesRows").GetBoolean());
        Assert.False(writeJson.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(2, writeJson.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(2, writeJson.GetProperty("writtenLinks").GetArrayLength());
        Assert.Equal(2, await CountTheOddsSoccerAliasLinksAsync(factory));

        var secondWrite = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", new
        {
            fromDate = "2026-06-21",
            toDate = "2026-06-22",
            dryRun = false,
            minConfidence = 0.94m,
            maxLinks = 200
        });

        await EnsureSuccessWithBodyAsync(secondWrite);
        var secondJson = await secondWrite.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(secondJson.GetProperty("writesRows").GetBoolean());
        Assert.Equal(0, secondJson.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(2, secondJson.GetProperty("summary").GetProperty("linksAlreadyExisting").GetInt32());
        Assert.Equal(2, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task TheOddsApiSportsAliasLinkBatch_SkipsExistingConflictingLinks()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Maximiliano Araujo",
            platformPlayerName: "Maximiliano Javier Araujo Vilches",
            fixtureId: "1489398",
            providerEventIdOverride: "event-conflict");
        await AddConflictingTheOddsSoccerAliasLinkAsync(
            factory,
            "event-conflict",
            "MAXIMILIANOJAVIERARAUJOVILCHES");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", new
        {
            fromDate = "2026-06-21",
            toDate = "2026-06-21",
            dryRun = false,
            minConfidence = 0.94m,
            maxLinks = 200
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("linksInserted").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("skippedConflictingExisting").GetInt32());
        Assert.Single(json.GetProperty("skipped").EnumerateArray());
        Assert.Equal(1, await CountTheOddsSoccerAliasLinksAsync(factory));
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityCoverage_IsReadOnlyAndReportsNoCandidatesGracefully()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var before = await CountApiSportsSoccerResultsAsync(factory);
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.GetProperty("importedPlayers").GetProperty("distinctApiSportsPlayers").GetInt32() > 0);
        Assert.Equal(0, json.GetProperty("existingSoccerIdentity").GetProperty("soccerPlatformProps").GetInt32());
        Assert.True(json.GetProperty("matchSummary").GetProperty("unmatchedCount").GetInt32() > 0);
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityCandidates_ReturnsNormalizedNameTeamCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Tést-Forward", "Home FC", addApiSportsRef: true);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/candidates?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        var candidate = Assert.Single(json.GetProperty("candidates").EnumerateArray());
        Assert.Equal("1001", candidate.GetProperty("apiSportsPlayerId").GetString());
        Assert.Equal("Tést-Forward", candidate.GetProperty("candidateCanonicalPlayerName").GetString());
        Assert.True(candidate.GetProperty("confidence").GetDecimal() >= 0.95m);
        Assert.Equal("SafeAliasLink", candidate.GetProperty("suggestedAction").GetString());
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityCoverage_ReportsAmbiguousDuplicateNames()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC");
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", providerName: "Underdog");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("matchSummary").GetProperty("ambiguousMatchCount").GetInt32() > 0);
        Assert.NotEmpty(json.GetProperty("samples").GetProperty("ambiguous").EnumerateArray());
    }

    [Fact]
    public async Task ApiSportsSoccerPlatformContext_ReturnsReadOnlyEmptyContextGracefully()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var before = await CountApiSportsSoccerResultsAsync(factory);
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/platform-context?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(0, json.GetProperty("soccerPlatformProps").GetInt32());
        Assert.Equal(0, json.GetProperty("fieldAvailability").GetProperty("hasTeamNameCount").GetInt32());
        Assert.Equal(before, await CountApiSportsSoccerResultsAsync(factory));
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlatformContext_ReportsPlatformTeamAndProviderFields()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/platform-context?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("soccerPlatformProps").GetInt32());
        Assert.Equal(1, json.GetProperty("soccerPlatformPlayers").GetInt32());
        Assert.Equal(1, json.GetProperty("fieldAvailability").GetProperty("hasTeamNameCount").GetInt32());
        Assert.Equal(1, json.GetProperty("fieldAvailability").GetProperty("hasStartTimeCount").GetInt32());
        Assert.Equal(0, json.GetProperty("fieldAvailability").GetProperty("hasProviderPlayerIdCount").GetInt32());
        var sample = Assert.Single(json.GetProperty("samples").EnumerateArray());
        Assert.Equal("Test Forward", sample.GetProperty("playerName").GetString());
        Assert.Equal("Home FC", sample.GetProperty("teamName").GetString());
    }

    [Fact]
    public async Task ProviderEntityLinksDiagnostic_IsReadOnly()
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
        var before = await CountApiSportsSoccerProviderPlayerLinksAsync(factory);
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/identity/provider-links?sport=SOCCER&entityType=Player&providerKey=api-sports-football&includeSamples=true&maxSamples=5");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.Equal(before, json.GetProperty("totalLinks").GetInt32());
        Assert.Equal(before, json.GetProperty("candidateCount").GetInt32());
        Assert.NotEmpty(json.GetProperty("samples").EnumerateArray());
        Assert.Equal(before, await CountApiSportsSoccerProviderPlayerLinksAsync(factory));
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityReviewQueue_IsReadOnly()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var before = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        var row = Assert.Single(json.GetProperty("rows").EnumerateArray(), row => row.GetProperty("apiSportsPlayerId").GetString() == "1001");
        Assert.Equal(before.Id, row.GetProperty("providerEntityLinkId").GetGuid());
        Assert.Equal("ApproveCandidate", row.GetProperty("suggestedAction").GetString());
        var after = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal(before.LinkStatus, after.LinkStatus);
        Assert.Null(after.CanonicalEntityId);
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
    }

    [Fact]
    public async Task ApiSportsSoccerPlayerIdentityReviewQueue_FiltersCandidatesAndSortsActionableFirst()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);

        var filteredResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&hasCandidate=true&suggestedAction=ApproveCandidate&maxResults=25");

        await EnsureSuccessWithBodyAsync(filteredResponse);
        var filteredJson = await filteredResponse.Content.ReadFromJsonAsync<JsonElement>();
        var filteredRows = filteredJson.GetProperty("rows").EnumerateArray().ToArray();
        Assert.NotEmpty(filteredRows);
        Assert.All(filteredRows, row =>
        {
            Assert.True(row.GetProperty("hasCandidate").GetBoolean());
            Assert.Equal("ApproveCandidate", row.GetProperty("suggestedAction").GetString());
            Assert.True(row.GetProperty("confidence").GetDecimal() >= 0.80m);
        });

        var sortedResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&includeNoCandidate=true&maxResults=25");

        await EnsureSuccessWithBodyAsync(sortedResponse);
        var sortedJson = await sortedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sortedRows = sortedJson.GetProperty("rows").EnumerateArray().ToArray();
        Assert.NotEmpty(sortedRows);
        Assert.Equal("ApproveCandidate", sortedRows[0].GetProperty("suggestedAction").GetString());
        Assert.True(sortedRows[0].GetProperty("hasCandidate").GetBoolean());
        Assert.Contains(sortedRows, row => row.GetProperty("suggestedAction").GetString() == "NoCandidate");
    }

    [Fact]
    public async Task ApiSportsSoccerApproveLink_DryRunWritesNothing_AndWriteApproves()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = true,
            reason = "manual test review"
        });

        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var dryRunJson = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunJson.GetProperty("dryRun").GetBoolean());
        Assert.True(dryRunJson.GetProperty("readOnly").GetBoolean());
        Assert.True(dryRunJson.GetProperty("changed").GetBoolean());
        var afterDryRun = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Candidate", afterDryRun.LinkStatus);
        Assert.Null(afterDryRun.CanonicalEntityId);

        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = false,
            reason = "manual test review"
        });

        await EnsureSuccessWithBodyAsync(writeResponse);
        var approved = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Approved", approved.LinkStatus);
        Assert.Equal("Manual", approved.LinkSource);
        Assert.Equal(canonicalPlayerId, approved.CanonicalEntityId);
        Assert.Equal("debug/manual", approved.ApprovedBy);
    }

    [Fact]
    public async Task ApiSportsSoccerReviewQueueExport_ReturnsCompactApprovalRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&suggestedAction=ApproveCandidate&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        var row = Assert.Single(json.GetProperty("safeCandidates").EnumerateArray());
        Assert.Equal("Test Forward", row.GetProperty("apiSportsPlayerName").GetString());
        Assert.Equal("Test Forward", row.GetProperty("candidateCanonicalPlayerName").GetString());
        Assert.True(row.GetProperty("suggestedApprovalJson").TryGetProperty("providerEntityLinkId", out _));
        Assert.True(row.GetProperty("suggestedApprovalJson").TryGetProperty("canonicalPlayerId", out _));
    }

    [Fact]
    public async Task ApiSportsSoccerReviewQueueSafeOnly_ExcludesMissingTeamContextCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");

        var needsReviewResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&hasCandidate=true&suggestedAction=NeedsReview&maxResults=10");
        await EnsureSuccessWithBodyAsync(needsReviewResponse);
        var needsReviewJson = await needsReviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var needsReviewRow = Assert.Single(needsReviewJson.GetProperty("rows").EnumerateArray());
        Assert.Contains(needsReviewRow.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "MissingTeamContext");

        var safeOnlyResponse = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&hasCandidate=true&safeOnly=true&maxResults=10");
        await EnsureSuccessWithBodyAsync(safeOnlyResponse);
        var safeOnlyJson = await safeOnlyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(safeOnlyJson.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task ApiSportsSoccerReviewQueueExport_SeparatesNeedsReviewFromSafeCandidates()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Keeper");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(json.GetProperty("safeCandidates").EnumerateArray());
        var needsReviewRow = Assert.Single(json.GetProperty("needsReviewCandidates").EnumerateArray());
        Assert.Equal("Test Keeper", needsReviewRow.GetProperty("apiSportsPlayerName").GetString());
        Assert.Contains(needsReviewRow.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "MissingTeamContext");
        Assert.True(json.TryGetProperty("noCandidate", out _));
        Assert.True(json.TryGetProperty("ambiguousConflict", out _));
    }

    [Fact]
    public async Task ApiSportsSoccerLinkPreview_ReturnsOnlyNoBlockerSafeAliasRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");

        var response = await client.GetAsync("/api/debug/soccer/player-identity/api-sports/world-cup/link-preview?fromDate=2026-06-11&toDate=2026-06-11&minConfidence=0.80&maxResults=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("candidateCount").GetInt32());
        Assert.Empty(json.GetProperty("candidates").EnumerateArray());
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_DryRunWritesNothing_AndIsIdempotent()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = true,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });

        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var dryRunJson = await dryRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dryRunJson.GetProperty("dryRun").GetBoolean());
        Assert.True(dryRunJson.GetProperty("readOnly").GetBoolean());
        Assert.Equal(1, dryRunJson.GetProperty("validCount").GetInt32());
        Assert.Equal(0, dryRunJson.GetProperty("changedCount").GetInt32());
        var afterDryRun = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Candidate", afterDryRun.LinkStatus);
        Assert.Null(afterDryRun.CanonicalEntityId);

        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });
        await EnsureSuccessWithBodyAsync(writeResponse);
        var writeJson = await writeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, writeJson.GetProperty("changedCount").GetInt32());
        var approved = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Approved", approved.LinkStatus);
        Assert.Equal(canonicalPlayerId, approved.CanonicalEntityId);

        var idempotentResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });
        await EnsureSuccessWithBodyAsync(idempotentResponse);
        var idempotentJson = await idempotentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, idempotentJson.GetProperty("validCount").GetInt32());
        Assert.Equal(0, idempotentJson.GetProperty("changedCount").GetInt32());
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_AllOrNothingBlocksWrites_WhenPartialNotAllowed()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var badCanonicalPlayerId = await AddMlbCanonicalPlayerAsync(factory);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var keeperLink = await GetApiSportsProviderLinkStateAsync(factory, "2001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new object[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" },
                new { providerEntityLinkId = keeperLink.Id, canonicalPlayerId = badCanonicalPlayerId, reason = "bad sport" }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("validCount").GetInt32());
        Assert.Equal(1, json.GetProperty("blockedCount").GetInt32());
        Assert.Contains(json.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "CanonicalPlayerSportMismatch");
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
        Assert.Null((await GetApiSportsProviderLinkStateAsync(factory, "1001")).CanonicalEntityId);
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "2001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_AllowsPartialWritesValidRows()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var badCanonicalPlayerId = await AddMlbCanonicalPlayerAsync(factory);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var keeperLink = await GetApiSportsProviderLinkStateAsync(factory, "2001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = true,
            approvals = new object[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" },
                new { providerEntityLinkId = keeperLink.Id, canonicalPlayerId = badCanonicalPlayerId, reason = "bad sport" }
            }
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("allowPartial").GetBoolean());
        Assert.Equal(1, json.GetProperty("validCount").GetInt32());
        Assert.Equal(1, json.GetProperty("blockedCount").GetInt32());
        Assert.Equal(1, json.GetProperty("changedCount").GetInt32());
        Assert.Equal("Approved", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "2001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_BlocksNeedsReviewCandidatesByDefault()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("blockedCount").GetInt32());
        Assert.Contains(json.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "CandidateReviewBlocker:MissingTeamContext");
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_AllowsNeedsReviewOnlyWhenExplicit()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            allowNeedsReview = true,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("allowNeedsReview").GetBoolean());
        Assert.Equal(1, json.GetProperty("changedCount").GetInt32());
        var result = Assert.Single(json.GetProperty("results").EnumerateArray());
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == "ApprovingNeedsReviewCandidate:MissingTeamContext");
        Assert.Equal("Approved", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerBulkApprove_BlocksAlreadyApprovedDifferentCanonical()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var otherCanonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Other Forward", "Home FC");
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var approveResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId, reason = "manual exact-name review" }
            }
        });
        await EnsureSuccessWithBodyAsync(approveResponse);

        var differentResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", new
        {
            dryRun = false,
            allowPartial = false,
            approvals = new[]
            {
                new { providerEntityLinkId = link.Id, canonicalPlayerId = otherCanonicalPlayerId, reason = "different player" }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, differentResponse.StatusCode);
        var json = await differentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("blockedCount").GetInt32());
        Assert.Contains(json.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "AlreadyApprovedToDifferentCanonicalPlayer");
        var approved = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal(canonicalPlayerId, approved.CanonicalEntityId);
    }

    [Fact]
    public async Task ApiSportsSoccerApproveLink_WarnsWhenExplicitManualApprovalHasReviewBlockers()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await AddSoccerCanonicalPlayerWithoutPlatformAsync(factory, "Test Forward");
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var response = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = true,
            reason = "manual explicit review"
        });

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(json.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == "ManualApprovalNeedsReviewCandidate:MissingTeamContext");
        Assert.Equal("Candidate", (await GetApiSportsProviderLinkStateAsync(factory, "1001")).LinkStatus);
    }

    [Fact]
    public async Task ApiSportsSoccerApproveLink_ValidatesCanonicalSport_AndBlocksDifferentApproval()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var otherSoccerPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Other Forward", "Home FC");
        var mlbPlayerId = await AddMlbCanonicalPlayerAsync(factory);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var badSportResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId = mlbPlayerId,
            dryRun = false,
            reason = "bad sport"
        });
        Assert.Equal(HttpStatusCode.Conflict, badSportResponse.StatusCode);
        var badSportJson = await badSportResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(badSportJson.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "CanonicalPlayerSportMismatch");

        var approveResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = false,
            reason = "manual test review"
        });
        await EnsureSuccessWithBodyAsync(approveResponse);

        var differentResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId = otherSoccerPlayerId,
            dryRun = false,
            reason = "different player"
        });
        Assert.Equal(HttpStatusCode.Conflict, differentResponse.StatusCode);
        var differentJson = await differentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(differentJson.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "AlreadyApprovedToDifferentCanonicalPlayer");
    }

    [Fact]
    public async Task ApiSportsSoccerRejectLink_DryRunWritesNothing_AndWriteRejects()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");

        var dryRunResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/reject-link", new
        {
            providerEntityLinkId = link.Id,
            dryRun = true,
            reason = "wrong player"
        });

        await EnsureSuccessWithBodyAsync(dryRunResponse);
        var afterDryRun = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Candidate", afterDryRun.LinkStatus);
        Assert.Null(afterDryRun.RejectedAtUtc);

        var writeResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/reject-link", new
        {
            providerEntityLinkId = link.Id,
            dryRun = false,
            reason = "wrong player"
        });

        await EnsureSuccessWithBodyAsync(writeResponse);
        var rejected = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        Assert.Equal("Rejected", rejected.LinkStatus);
        Assert.Equal("Manual", rejected.LinkSource);
        Assert.Equal("wrong player", rejected.RejectedReason);
        Assert.NotNull(rejected.RejectedAtUtc);
    }

    [Fact]
    public async Task ApiSportsSoccerCoverage_ReportsApprovedPlayerLinkCoverage()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        var importResponse = await client.PostAsJsonAsync("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", new
        {
            fromDate = "2026-06-11",
            toDate = "2026-06-11",
            dryRun = false,
            forceLive = true
        });
        await EnsureSuccessWithBodyAsync(importResponse);
        var canonicalPlayerId = await factory.AddSoccerPlatformIdentityCandidateAsync("Test Forward", "Home FC", addApiSportsRef: true);
        var link = await GetApiSportsProviderLinkStateAsync(factory, "1001");
        var approveResponse = await client.PostAsJsonAsync("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", new
        {
            providerEntityLinkId = link.Id,
            canonicalPlayerId,
            dryRun = false,
            reason = "manual test review"
        });
        await EnsureSuccessWithBodyAsync(approveResponse);

        var response = await client.GetAsync("/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-11&includeSamples=true&maxSamples=10");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.Equal(1, json.GetProperty("playerLinkCoverage").GetProperty("approvedProviderLinksUsed").GetInt32());
        Assert.True(json.GetProperty("playerLinkCoverage").GetProperty("resultRowsWithApprovedPlayerLink").GetInt32() > 0);
        Assert.True(json.GetProperty("marketJoinCoverage").GetProperty("matchedToPlatformProps").GetInt32() > 0);
    }

    [Fact]
    public async Task MlbRegressionSmoke_IsReadOnlyAndDoesNotCallProviders()
    {
        await using var factory = new SafetyTestApplicationFactory(ApiSportsLiveConfig());
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await AddMlbPropResultAsync(factory, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        var callsBefore = factory.ApiSportsFootballClient.FixturePlayerStatsCallCount;

        var response = await client.GetAsync("/api/debug/mlb/regression-smoke");

        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.True(json.TryGetProperty("players", out _));
        Assert.True(json.TryGetProperty("mlbPlayerExternalRefs", out _));
        Assert.True(json.TryGetProperty("latestMlbResultImportDate", out _));
        Assert.Equal("SportCode", json.GetProperty("resolvedMlb").GetProperty("lookupMethod").GetString());
        Assert.Equal("MLB", json.GetProperty("resolvedMlb").GetProperty("sportCode").GetString());
        Assert.True(json.GetProperty("mlbPropResults").GetProperty("recentCount").GetInt32() > 0);
        Assert.True(json.GetProperty("mlbPropResults").GetProperty("allTimeCount").GetInt32() > 0);
        Assert.True(json.GetProperty("mlbPropResults").TryGetProperty("latestResultDate", out _));
        Assert.True(json.GetProperty("mlbPropResults").GetProperty("recentWindow").TryGetProperty("fromDate", out _));
        Assert.DoesNotContain(json.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == "No MLB Sport or League identity was found.");
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
        Assert.Equal(callsBefore, factory.ApiSportsFootballClient.FixturePlayerStatsCallCount);
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

    private static async Task<int> CountApiSportsSoccerProviderPlayerLinksAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.ProviderEntityLinks.CountAsync(link =>
            link.ProviderKey == "api-sports-football"
            && link.EntityType == "Player");
    }

    private static async Task<int> CountTheOddsSoccerAliasLinksAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.ProviderEntityLinks.CountAsync(link =>
            link.ProviderKey == "the-odds-soccer"
            && link.EntityType == "Player");
    }

    private static async Task<TheOddsAliasCandidateIds> GetSingleTheOddsAliasCandidateAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true");
        await EnsureSuccessWithBodyAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sample = Assert.Single(json.GetProperty("samples").GetProperty("aliasCandidates").EnumerateArray());
        Assert.Equal("NeedsReview", sample.GetProperty("suggestedAction").GetString());
        var candidate = Assert.Single(sample.GetProperty("candidates").EnumerateArray());
        return new TheOddsAliasCandidateIds(
            candidate.GetProperty("apiSportsPlayerId").GetString()!,
            candidate.GetProperty("localPlayerId").GetGuid());
    }

    private sealed record TheOddsAliasCandidateIds(
        string ApiSportsPlayerId,
        Guid LocalPlayerId);

    private static async Task SeedTwoResolvedSoccerAliasPairsAsync(SafetyTestApplicationFactory factory)
    {
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Maximiliano Araujo",
            platformPlayerName: "Maximiliano Javier Araujo Vilches",
            fixtureId: "1489398",
            providerEventIdOverride: "event-one",
            eventHomeTeamName: "Uruguay",
            eventAwayTeamName: "Cape Verde",
            resultPlayerTeamName: "Uruguay",
            eventDate: new DateOnly(2026, 6, 21));
        await factory.SeedApiSportsSoccerMarketJoinRowsAsync(
            resultPlayerName: "Ryan Mendes",
            platformPlayerName: "Ryan Mendes da Graca",
            fixtureId: "1489399",
            providerEventIdOverride: "event-two",
            eventHomeTeamName: "United States",
            eventAwayTeamName: "Czechia",
            resultPlayerTeamName: "United States",
            eventDate: new DateOnly(2026, 6, 22),
            resetDatabase: false);
    }

    private static async Task AddConflictingTheOddsSoccerAliasLinkAsync(
        SafetyTestApplicationFactory factory,
        string providerEventId,
        string normalizedPlatformPlayerName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleAsync(row => row.Code == "SOCCER");
        var league = await dbContext.Leagues.FirstAsync(row => row.SportId == sport.Id);
        var wrongPlayer = new Player
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            DisplayName = "Wrong Soccer Player",
            Position = "F"
        };
        dbContext.Players.Add(wrongPlayer);
        dbContext.ProviderEntityLinks.Add(new ProviderEntityLink
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            EntityType = "Player",
            CanonicalEntityId = wrongPlayer.Id,
            CanonicalEntityKind = "Player",
            CanonicalEntityName = wrongPlayer.DisplayName,
            ProviderKey = "the-odds-soccer",
            ProviderEntityId = $"soccer_fifa_world_cup:{providerEventId}:{normalizedPlatformPlayerName}",
            ProviderEntityName = normalizedPlatformPlayerName,
            ProviderCompetitionId = "soccer_fifa_world_cup",
            ProviderCompetitionName = "World Cup",
            ProviderEventId = providerEventId,
            Confidence = 1m,
            LinkStatus = "Approved",
            LinkSource = "TestConflict",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = DateTimeOffset.UtcNow,
            ApprovedBy = "test"
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<int> CountSoccerPlatformPropsAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.PlatformProps
            .Include(row => row.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .CountAsync(row => row.Prop != null
                && row.Prop.League != null
                && row.Prop.League.Sport != null
                && row.Prop.League.Sport.Code == "SOCCER");
    }

    private static async Task<JsonElement> GetSingleSoccerPlatformPropMetadataAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var metadata = await dbContext.PlatformProps
            .Include(row => row.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(row => row.Prop != null
                && row.Prop.League != null
                && row.Prop.League.Sport != null
                && row.Prop.League.Sport.Code == "SOCCER")
            .Select(row => row.Metadata)
            .SingleAsync();
        return metadata!.RootElement.Clone();
    }

    private static async Task<JsonElement[]> GetSoccerPlatformPropMetadataAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var metadataRows = await dbContext.PlatformProps
            .Include(row => row.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(row => row.Prop != null
                && row.Prop.League != null
                && row.Prop.League.Sport != null
                && row.Prop.League.Sport.Code == "SOCCER")
            .OrderBy(row => row.PlatformPropId)
            .Select(row => row.Metadata)
            .ToListAsync();
        return metadataRows
            .Select(metadata => metadata!.RootElement.Clone())
            .ToArray();
    }

    private static async Task AddApiSportsSoccerResultFixtureAsync(
        SafetyTestApplicationFactory factory,
        string fixtureId,
        DateOnly eventDate,
        string teamName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "WC2026");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "World Cup",
                Code = "WC2026",
                SeasonType = "2026"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var team = await dbContext.Teams.SingleOrDefaultAsync(row => row.LeagueId == league.Id && row.Name == teamName);
        if (team is null)
        {
            team = new Team
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Name = teamName
            };
            dbContext.Teams.Add(team);
        }

        var propType = await dbContext.PropTypes.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == "GOALS");
        if (propType is null)
        {
            propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Goals",
                StatKey = "GOALS",
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = $"{teamName} Fixture Player {fixtureId}",
            TeamId = team.Id,
            LeagueId = league.Id,
            Position = "F"
        };
        dbContext.Players.Add(player);
        dbContext.PropResults.Add(new PropResult
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            EventDate = eventDate,
            ActualResultValue = 1m,
            ExternalSource = "api-sports-football",
            ExternalSourceIdentifier = $"api-sports-football:{fixtureId}:{Guid.NewGuid():N}:GOALS",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddLegacySoccerPlatformPropAsync(
        SafetyTestApplicationFactory factory,
        string providerEventId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleAsync(row => row.Code == "SOCCER");
        var league = await dbContext.Leagues.FirstAsync(row => row.SportId == sport.Id);
        var homeTeam = await dbContext.Teams.FirstAsync(row => row.LeagueId == league.Id);
        var awayTeam = await dbContext.Teams.FirstAsync(row => row.LeagueId == league.Id && row.Id != homeTeam.Id);
        var game = await dbContext.Games.FirstAsync(row => row.LeagueId == league.Id);
        var propType = await dbContext.PropTypes.FirstAsync(row => row.SportId == sport.Id);
        var platform = await dbContext.Platforms.FirstOrDefaultAsync(row => row.Slug == "legacy-bookmaker");
        if (platform is null)
        {
            platform = new Platform
            {
                Id = Guid.NewGuid(),
                Name = "Legacy Bookmaker",
                Slug = "legacy-bookmaker"
            };
            dbContext.Platforms.Add(platform);
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Legacy Soccer Player",
            TeamId = homeTeam.Id,
            LeagueId = league.Id,
            Position = "F"
        };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            GameId = game.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            NormalizedKey = $"legacy-soccer-{Guid.NewGuid():N}",
            StartsAt = game.ScheduledStartAt,
            Status = "active"
        };
        dbContext.Players.Add(player);
        dbContext.Props.Add(prop);
        dbContext.PlatformProps.Add(new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = $"legacy-{Guid.NewGuid():N}",
            ExternalStableKey = providerEventId,
            Label = "Legacy Soccer Player Goals",
            CurrentLine = 0.5m,
            OverAvailable = true,
            UnderAvailable = false,
            PayoutType = "sportsbook",
            Status = "active",
            ObservedAt = DateTimeOffset.UtcNow,
            Metadata = JsonSerializer.SerializeToDocument(new
            {
                dataProvider = "Internal",
                contextSchemaVersion = "legacy",
                providerCategory = "Sportsbook",
                sport = new
                {
                    competition = "World Cup",
                    providerSportKey = "soccer_fifa_world_cup"
                },
                eventInfo = new
                {
                    providerEventId,
                    eventId = providerEventId,
                    homeTeam = homeTeam.Name,
                    awayTeam = awayTeam.Name
                },
                market = new { key = "player_goals" },
                player = new
                {
                    providerPlayerId = (string?)null,
                    teamName = (string?)null
                }
            })
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> AddSoccerTheOddsRefreshRunAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var refreshRun = new TheOddsRefreshRun
        {
            Id = Guid.NewGuid(),
            Provider = "The Odds API",
            Sport = "SOCCER",
            SlateDate = new DateOnly(2026, 6, 21),
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
            Succeeded = true,
            Failed = false,
            Blocked = false,
            DryRun = false,
            ForceLive = true,
            BookmakerPreset = "sportsbookContext",
            ProviderCategoriesIncluded = "Sportsbook",
            SportsbookBookmakersRequested = "betmgm,fanduel",
            DfsBookmakersRequested = string.Empty,
            MarketsRequested = "player_goals,player_shots",
            EstimatedApiCalls = 35,
            ActualApiCalls = 35,
            PlatformPropsCreated = 119,
            PlatformPropsUpdated = 0,
            PlatformPropsUpserted = 119,
            PredictionCount = 0,
            LineShoppingGroupCount = 34,
            DiagnosticsJson = JsonSerializer.SerializeToDocument(new
            {
                sport = "SOCCER",
                competition = "World Cup",
                configuredSportKey = "soccer_fifa_world_cup",
                requestedRegions = "us",
                requestedMarkets = new[] { "player_goals", "player_shots" },
                eventsFetched = 34,
                eventOddsCalls = 1,
                platformPropsCreated = 119,
                platformPropsUpdated = 0,
                platformPropsUpserted = 119
            })
        };
        dbContext.TheOddsRefreshRuns.Add(refreshRun);
        await dbContext.SaveChangesAsync();
        return refreshRun.Id;
    }

    private static async Task<ApiSportsProviderLinkTestState> GetApiSportsProviderLinkStateAsync(
        SafetyTestApplicationFactory factory,
        string providerEntityId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var link = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .SingleAsync(row => row.ProviderKey == "api-sports-football"
                && row.EntityType == "Player"
                && row.ProviderEntityId == providerEntityId);
        return new ApiSportsProviderLinkTestState(
            link.Id,
            link.LinkStatus,
            link.LinkSource,
            link.CanonicalEntityId,
            link.ApprovedBy,
            link.RejectedAtUtc,
            link.RejectedReason);
    }

    private static async Task<Guid> AddMlbCanonicalPlayerAsync(SafetyTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "MLB");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "MLB" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "MLB");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Major League Baseball",
                Code = "MLB"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Wrong Sport Player",
            LeagueId = league.Id
        };
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();
        return player.Id;
    }

    private static async Task<Guid> AddSoccerCanonicalPlayerWithoutPlatformAsync(
        SafetyTestApplicationFactory factory,
        string playerName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "WC2026");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "World Cup",
                Code = "WC2026",
                SeasonType = "2026"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = playerName,
            LeagueId = league.Id
        };
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();
        return player.Id;
    }

    private static async Task AddMlbPropResultAsync(SafetyTestApplicationFactory factory, DateOnly eventDate)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "MLB");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "MLB", Name = "Major League Baseball" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "MLB");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Major League Baseball",
                Code = "MLB"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "Smoke Test Batter",
            LeagueId = league.Id
        };
        var propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Hits",
            StatKey = "HITS",
            ValueType = "numeric"
        };
        dbContext.Players.Add(player);
        dbContext.PropTypes.Add(propType);
        dbContext.PropResults.Add(new PropResult
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            EventDate = eventDate,
            ActualResultValue = 1,
            ExternalSource = "mlb",
            ExternalSourceIdentifier = $"mlb:{eventDate:yyyyMMdd}:{player.Id:N}:{propType.Id:N}"
        });
        dbContext.MlbResultImportDateStates.Add(new MlbResultImportDateState
        {
            Id = Guid.NewGuid(),
            EventDate = eventDate,
            Status = "succeeded",
            LastSuccessfulAt = DateTimeOffset.UtcNow,
            RecordsFound = 1,
            RecordsMapped = 1,
            RecordsInserted = 1
        });
        await dbContext.SaveChangesAsync();
    }

    private sealed record ApiSportsProviderLinkTestState(
        Guid Id,
        string LinkStatus,
        string LinkSource,
        Guid? CanonicalEntityId,
        string? ApprovedBy,
        DateTimeOffset? RejectedAtUtc,
        string? RejectedReason);
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

    public async Task SeedApiSportsSoccerProviderStatMappingsAsync(string? excludeCanonicalStatKey = null)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        var rows = new[]
        {
            ("Goals", "GOALS", new[] { "goals.total" }, "Direct", "Known", true, "goals.total"),
            ("Assists", "ASSISTS", new[] { "goals.assists" }, "Direct", "Known", true, "goals.assists"),
            ("Shots", "SHOTS", new[] { "shots.total" }, "Direct", "Known", true, "shots.total"),
            ("Shots On Target", "SHOTS_ON_TARGET", new[] { "shots.on" }, "Direct", "Known", true, "shots.on"),
            ("Passes Attempted", "PASSES_ATTEMPTED", new[] { "passes.total" }, "Direct", "Known", true, "passes.total"),
            ("Tackles", "TACKLES", new[] { "tackles.total" }, "Direct", "Known", true, "tackles.total"),
            ("Fouls", "FOULS", new[] { "fouls.committed" }, "Direct", "Known", true, "fouls.committed"),
            ("Fouls Drawn", "FOULS_DRAWN", new[] { "fouls.drawn" }, "Direct", "Known", true, "fouls.drawn"),
            ("Offsides", "OFFSIDES", new[] { "offsides" }, "Direct", "Known", true, "offsides"),
            ("Goalie Saves", "GOALIE_SAVES", new[] { "goals.saves" }, "GoalkeeperOnlyDirect", "Known", true, "goals.saves"),
            ("Attempted Dribbles", "ATTEMPTED_DRIBBLES", new[] { "dribbles.attempts" }, "Direct", "Known", true, "dribbles.attempts"),
            ("Goal + Assist", "GOAL_ASSIST", new[] { "goals.total", "goals.assists" }, "Sum", "NeedsConfirmation", false, null),
            ("Cards", "CARDS", new[] { "cards.yellow", "cards.red" }, "Sum", "NeedsConfirmation", false, null),
            ("Goals Allowed", "GOALS_ALLOWED", new[] { "goals.conceded" }, "GoalkeeperOnlyDirect", "NeedsConfirmation", false, "goals.conceded"),
            ("Shots Assisted", "SHOTS_ASSISTED", new[] { "passes.key" }, "Direct", "NeedsConfirmation", false, "passes.key"),
            ("Crosses", "CROSSES", Array.Empty<string>(), "Unsupported", "Unsupported", false, null),
            ("Clearances", "CLEARANCES", Array.Empty<string>(), "Unsupported", "Unsupported", false, null),
            ("Outfield Fantasy Score", "OUTFIELD_FANTASY_SCORE", Array.Empty<string>(), "Unsupported", "Unsupported", false, null),
            ("Goalie Fantasy Score", "GOALIE_FANTASY_SCORE", Array.Empty<string>(), "Unsupported", "Unsupported", false, null),
            ("Combo props", "COMBO_PROPS", Array.Empty<string>(), "Unsupported", "Unsupported", false, null)
        };

        foreach (var (displayName, statKey, fields, transform, confidence, isGradeable, providerStatKey) in rows)
        {
            if (string.Equals(statKey, excludeCanonicalStatKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dbContext.ProviderStatMappings.Add(new ProviderStatMapping
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                ProviderKey = "api-sports-football",
                ProviderProduct = "API-Sports Football",
                CompetitionScope = "World Cup",
                Season = 2026,
                ProviderMappingKey = $"api-sports-football:SOCCER:World Cup:2026:{statKey}",
                ProviderPropName = displayName,
                ProviderStatKey = providerStatKey,
                ProviderStatFieldsJson = JsonSerializer.SerializeToDocument(fields),
                CanonicalStatKey = statKey,
                DisplayName = displayName,
                ValueTransform = transform,
                RuleConfidence = confidence,
                IsGradeable = isGradeable,
                PublicGradingEnabled = false,
                IsActive = true,
                Notes = "Seeded by test fixture.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedApiSportsSoccerMarketJoinRowsAsync(
        bool includeProviderEventId = true,
        bool includeResultTeam = true,
        bool includeMarketKey = true,
        decimal actualValue = 1m,
        decimal currentLine = 0.5m,
        bool overAvailable = true,
        bool underAvailable = false,
        string resultPlayerName = "Test Forward",
        string platformPlayerName = "Test Forward",
        bool includeResultRow = true,
        bool includePlatformRow = true,
        string fixtureId = "1489393",
        bool useTheOddsContext = true,
        string? providerEventIdOverride = null,
        string eventHomeTeamName = "Uruguay",
        string eventAwayTeamName = "Cape Verde",
        string resultPlayerTeamName = "Uruguay",
        DateOnly? eventDate = null,
        string platformMarketKey = "player_goals",
        string platformPropTypeName = "Goals",
        string platformStatKey = "GOALS",
        string apiSportsPropTypeName = "Goals",
        string apiSportsStatKey = "GOALS",
        bool resetDatabase = true)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (resetDatabase)
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }

        var effectiveEventDate = eventDate ?? new DateOnly(2026, 6, 21);
        var eventStart = new DateTimeOffset(effectiveEventDate.Year, effectiveEventDate.Month, effectiveEventDate.Day, 21, 0, 0, TimeSpan.Zero);
        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        var league = resetDatabase
            ? null
            : await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "WC2026");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "World Cup",
                Code = "WC2026",
                SeasonType = "2026"
            };
            dbContext.Leagues.Add(league);
        }
        var homeTeam = new Team
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            Name = eventHomeTeamName
        };
        var awayTeam = new Team
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            Name = eventAwayTeamName
        };
        var resultTeam = string.Equals(resultPlayerTeamName, eventHomeTeamName, StringComparison.OrdinalIgnoreCase)
            ? homeTeam
            : new Team
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Name = resultPlayerTeamName
            };
        var game = new Game
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            ScheduledStartAt = eventStart,
            Status = "scheduled"
        };
        var propType = resetDatabase
            ? null
            : await dbContext.PropTypes.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == platformStatKey);
        if (propType is null)
        {
            propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = platformPropTypeName,
                StatKey = platformStatKey,
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
        }
        var resultPropType = string.Equals(platformStatKey, apiSportsStatKey, StringComparison.OrdinalIgnoreCase)
            ? propType
            : resetDatabase
                ? null
                : await dbContext.PropTypes.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == apiSportsStatKey);
        if (resultPropType is null)
        {
            resultPropType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = apiSportsPropTypeName,
                StatKey = apiSportsStatKey,
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(resultPropType);
        }
        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = resultPlayerName,
            TeamId = includeResultTeam ? resultTeam.Id : null,
            LeagueId = league.Id,
            Position = "F"
        };
        var platformPlayer = string.Equals(platformPlayerName, resultPlayerName, StringComparison.Ordinal)
            ? player
            : new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = platformPlayerName,
                TeamId = includeResultTeam ? resultTeam.Id : null,
                LeagueId = league.Id,
                Position = "F"
            };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = platformPlayer.Id,
            GameId = game.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            NormalizedKey = $"soccer-market-join-{Guid.NewGuid():N}",
            StartsAt = game.ScheduledStartAt,
            Status = "active"
        };
        var platform = resetDatabase
            ? null
            : await dbContext.Platforms.SingleOrDefaultAsync(row => row.Slug == "betmgm");
        if (platform is null)
        {
            platform = new Platform
            {
                Id = Guid.NewGuid(),
                Name = "BetMGM",
                Slug = "betmgm"
            };
            dbContext.Platforms.Add(platform);
        }
        var providerEventId = includeProviderEventId ? providerEventIdOverride ?? "852a3abe6d46820720febc0fedebff9a" : null;
        var metadata = JsonSerializer.SerializeToDocument(new
        {
            dataProvider = useTheOddsContext ? "The Odds API" : "Internal",
            contextSchemaVersion = useTheOddsContext ? "the-odds-provider-context-v2" : "legacy",
            providerCategory = "Sportsbook",
            sport = new
            {
                competition = "World Cup",
                providerSportKey = useTheOddsContext ? "soccer_fifa_world_cup" : "legacy_soccer"
            },
            eventInfo = new
            {
                providerEventId,
                eventId = providerEventId,
                homeTeam = eventHomeTeamName,
                awayTeam = eventAwayTeamName
            },
            market = includeMarketKey
                ? new { key = platformMarketKey }
                : null,
            player = new
            {
                providerPlayerId = (string?)null,
                teamName = (string?)null
            }
        });

        dbContext.Teams.AddRange(homeTeam, awayTeam);
        dbContext.Games.Add(game);
        dbContext.Players.Add(player);
        if (resultTeam.Id != homeTeam.Id)
        {
            dbContext.Teams.Add(resultTeam);
        }

        if (platformPlayer.Id != player.Id)
        {
            dbContext.Players.Add(platformPlayer);
        }

        dbContext.Props.Add(prop);
        if (includePlatformRow)
        {
            dbContext.PlatformProps.Add(new PlatformProp
            {
                Id = Guid.NewGuid(),
                PropId = prop.Id,
                PlatformId = platform.Id,
                PlatformPropId = $"betmgm-{fixtureId}-{Guid.NewGuid():N}",
                ExternalStableKey = providerEventId,
                Label = $"{platformPlayerName} {platformPropTypeName}",
                CurrentLine = currentLine,
                OverAvailable = overAvailable,
                UnderAvailable = underAvailable,
                PayoutType = "sportsbook",
                Status = "active",
                ObservedAt = DateTimeOffset.UtcNow,
                Metadata = metadata
            });
        }

        if (includeResultRow)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = resultPropType.Id,
                EventDate = effectiveEventDate,
                ActualResultValue = actualValue,
                ExternalSource = "api-sports-football",
                ExternalSourceIdentifier = $"api-sports-football:{fixtureId}:1001:{apiSportsStatKey}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedApiSportsSoccerAliasCandidateRowsAsync(
        IReadOnlyList<string> apiSportsPlayerNames,
        IReadOnlyList<string> platformPlayerNames)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var sport = await GetOrCreateSoccerSportAsync(dbContext);
        var league = new League
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "World Cup",
            Code = "WC2026",
            SeasonType = "2026"
        };
        var homeTeam = new Team
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            Name = "Uruguay"
        };
        var awayTeam = new Team
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            Name = "Cape Verde"
        };
        var game = new Game
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            ScheduledStartAt = new DateTimeOffset(2026, 6, 21, 22, 0, 0, TimeSpan.Zero),
            Status = "scheduled"
        };
        var propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            Name = "Goals",
            StatKey = "GOALS",
            ValueType = "numeric"
        };
        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            Name = "BetMGM",
            Slug = "theodds-betmgm"
        };
        const string providerEventId = "852a3abe6d46820720febc0fedebff9a";

        dbContext.Leagues.Add(league);
        dbContext.Teams.AddRange(homeTeam, awayTeam);
        dbContext.Games.Add(game);
        dbContext.PropTypes.Add(propType);
        dbContext.Platforms.Add(platform);

        for (var index = 0; index < apiSportsPlayerNames.Count; index++)
        {
            var player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = apiSportsPlayerNames[index],
                TeamId = homeTeam.Id,
                LeagueId = league.Id,
                Position = "F"
            };
            dbContext.Players.Add(player);
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                EventDate = new DateOnly(2026, 6, 21),
                ActualResultValue = 0,
                ExternalSource = "api-sports-football",
                ExternalSourceIdentifier = $"api-sports-football:1489398:{1000 + index}:GOALS",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        foreach (var platformPlayerName in platformPlayerNames)
        {
            var player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = platformPlayerName,
                TeamId = homeTeam.Id,
                LeagueId = league.Id,
                Position = "F"
            };
            var prop = new Prop
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                GameId = game.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                NormalizedKey = $"soccer-alias-{Guid.NewGuid():N}",
                StartsAt = game.ScheduledStartAt,
                Status = "active"
            };
            dbContext.Players.Add(player);
            dbContext.Props.Add(prop);
            dbContext.PlatformProps.Add(new PlatformProp
            {
                Id = Guid.NewGuid(),
                PropId = prop.Id,
                PlatformId = platform.Id,
                PlatformPropId = $"betmgm-{Guid.NewGuid():N}",
                ExternalStableKey = providerEventId,
                Label = $"{platformPlayerName} Goals",
                CurrentLine = 0.5m,
                OverAvailable = true,
                UnderAvailable = false,
                PayoutType = "sportsbook",
                Status = "active",
                ObservedAt = DateTimeOffset.UtcNow,
                Metadata = JsonSerializer.SerializeToDocument(new
                {
                    providerCategory = "Sportsbook",
                    eventInfo = new
                    {
                        providerEventId,
                        eventId = providerEventId,
                        homeTeam = "Uruguay",
                        awayTeam = "Cape Verde"
                    },
                    market = new { key = "player_goals" },
                    player = new
                    {
                        providerPlayerId = (string?)null,
                        teamName = (string?)null
                    }
                })
            });
        }

        await dbContext.SaveChangesAsync();
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

    public async Task<Guid> AddSoccerPlatformIdentityCandidateAsync(
        string playerName,
        string teamName,
        string providerName = "PrizePicks",
        bool addApiSportsRef = false)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == "SOCCER");
        if (sport is null)
        {
            sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
            dbContext.Sports.Add(sport);
            await dbContext.SaveChangesAsync();
        }

        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.SportId == sport.Id && row.Code == "WC2026");
        if (league is null)
        {
            league = new League
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "World Cup",
                Code = "WC2026",
                SeasonType = "2026"
            };
            dbContext.Leagues.Add(league);
            await dbContext.SaveChangesAsync();
        }

        var team = await dbContext.Teams.FirstOrDefaultAsync(row => row.LeagueId == league.Id && row.Name == teamName);
        if (team is null)
        {
            team = new Team
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Name = teamName
            };
            dbContext.Teams.Add(team);
            await dbContext.SaveChangesAsync();
        }

        var propType = await dbContext.PropTypes.FirstOrDefaultAsync(row => row.SportId == sport.Id && row.StatKey == "SHOTS");
        if (propType is null)
        {
            propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = "Shots",
                StatKey = "SHOTS",
                ValueType = "numeric"
            };
            dbContext.PropTypes.Add(propType);
            await dbContext.SaveChangesAsync();
        }

        var platformSlug = providerName.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
        var platform = await dbContext.Platforms.FirstOrDefaultAsync(row => row.Slug == platformSlug);
        if (platform is null)
        {
            platform = new Platform
            {
                Id = Guid.NewGuid(),
                Name = providerName,
                Slug = platformSlug
            };
            dbContext.Platforms.Add(platform);
            await dbContext.SaveChangesAsync();
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = playerName,
            TeamId = team.Id,
            LeagueId = league.Id,
            Position = "F"
        };
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            LeagueId = league.Id,
            PropTypeId = propType.Id,
            NormalizedKey = $"soccer-test-{Guid.NewGuid():N}",
            StartsAt = new DateTimeOffset(2026, 6, 11, 20, 0, 0, TimeSpan.Zero),
            Status = "active"
        };
        var platformProp = new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = $"{platformSlug}-{Guid.NewGuid():N}",
            ExternalStableKey = $"{platformSlug}:{playerName}:{teamName}",
            Label = $"{playerName} Shots",
            CurrentLine = 1.5m,
            OverAvailable = true,
            UnderAvailable = true,
            PayoutType = "standard",
            Status = "active",
            ObservedAt = DateTimeOffset.UtcNow
        };
        dbContext.Players.Add(player);
        dbContext.Props.Add(prop);
        dbContext.PlatformProps.Add(platformProp);

        if (addApiSportsRef)
        {
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Provider = "api-sports-football",
                ProviderPlayerId = player.Id.ToString("N"),
                ProviderDisplayName = "Test Forward"
            });
        }

        await dbContext.SaveChangesAsync();
        return player.Id;
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
    private bool useSoccerWorldCupContextPayload;
    private bool rejectSoccerWorldCupEventOdds;
    private string soccerWorldCupPayloadMode = "valid";

    public void ConfigureSoccerWorldCupContextPayload(string payloadMode = "valid")
    {
        useSoccerWorldCupContextPayload = true;
        soccerWorldCupPayloadMode = payloadMode;
    }

    public void ConfigureSoccerWorldCupProviderRejection()
    {
        useSoccerWorldCupContextPayload = true;
        rejectSoccerWorldCupEventOdds = true;
    }

    public void ConfigureSoccerWorldCupAvailabilityByRegion()
    {
        useSoccerWorldCupContextPayload = true;
        soccerWorldCupPayloadMode = "availabilityByRegion";
    }

    public void ConfigureSoccerWorldCupSportsbookContextPayload()
    {
        useSoccerWorldCupContextPayload = true;
        soccerWorldCupPayloadMode = "sportsbookUs";
    }

    public void ConfigureSoccerWorldCupMultiEventSportsbookContextPayload()
    {
        useSoccerWorldCupContextPayload = true;
        soccerWorldCupPayloadMode = "multiSportsbookUs";
    }

    public Task<TheOddsApiPlayerPropsSpikeResult> FetchSpikeAsync(
        string? eventId,
        string? markets,
        string? bookmakers,
        string? regions,
        string? oddsFormat,
        CancellationToken cancellationToken,
        string? sportKey = null)
    {
        CallCount++;
        if (useSoccerWorldCupContextPayload
            && string.Equals(sportKey, "soccer_fifa_world_cup", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                var events = soccerWorldCupPayloadMode == "multiSportsbookUs"
                    ? new[]
                    {
                        new TheOddsApiEventRecord(
                            "soccer-event-1",
                            "soccer_fifa_world_cup",
                            new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
                            "Home FC",
                            "Away FC"),
                        new TheOddsApiEventRecord(
                            "soccer-event-2",
                            "soccer_fifa_world_cup",
                            new DateTimeOffset(2026, 6, 22, 20, 0, 0, TimeSpan.Zero),
                            "Second Home",
                            "Second Away"),
                        new TheOddsApiEventRecord(
                            "soccer-event-3",
                            "soccer_fifa_world_cup",
                            new DateTimeOffset(2026, 6, 23, 21, 0, 0, TimeSpan.Zero),
                            "Third Home",
                            "Third Away")
                    }
                    : [
                        new TheOddsApiEventRecord(
                            "soccer-event-1",
                            "soccer_fifa_world_cup",
                            new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
                            "Home FC",
                            "Away FC")
                    ];
                return Task.FromResult(new TheOddsApiPlayerPropsSpikeResult(
                    events,
                    "soccer-event-1",
                    [],
                    [],
                    new TheOddsApiCreditUsage(1, 99, 1),
                    "https://api.the-odds-api.test/v4/sports/soccer_fifa_world_cup/events",
                    "{}"));
            }

            if (rejectSoccerWorldCupEventOdds)
            {
                throw new TheOddsApiProviderException(
                    "PROVIDER_NON_SUCCESS",
                    "The Odds API returned HTTP 422.",
                    "Review the sanitized provider response and request parameters before retrying.",
                    HttpStatusCode.UnprocessableEntity,
                    """{"message":"Invalid market: bad_market","error_code":"INVALID_MARKET"}""",
                    "https://api.the-odds-api.test/v4/sports/soccer_fifa_world_cup/events/soccer-event-1/odds?apiKey=***&regions=us_dfs&markets=bad_market&oddsFormat=american",
                    new TheOddsApiCreditUsage(2, 98, 1),
                    "FetchEventOdds");
            }

            return Task.FromResult(BuildSoccerWorldCupEventOddsResult(eventId, regions));
        }

        throw new InvalidOperationException("The Odds API adapter should not be called by safety tests.");
    }

    private TheOddsApiPlayerPropsSpikeResult BuildSoccerWorldCupEventOddsResult(string? eventId, string? regions)
    {
        var eventStartTime = eventId switch
        {
            "soccer-event-2" => new DateTimeOffset(2026, 6, 22, 20, 0, 0, TimeSpan.Zero),
            "soccer-event-3" => new DateTimeOffset(2026, 6, 23, 21, 0, 0, TimeSpan.Zero),
            _ => new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero)
        };
        var homeTeam = eventId switch
        {
            "soccer-event-2" => "Second Home",
            "soccer-event-3" => "Third Home",
            _ => "Home FC"
        };
        var awayTeam = eventId switch
        {
            "soccer-event-2" => "Second Away",
            "soccer-event-3" => "Third Away",
            _ => "Away FC"
        };
        var eventRecord = new TheOddsApiEventRecord(
            eventId,
            "soccer_fifa_world_cup",
            eventStartTime,
            homeTeam,
            awayTeam);
        var requestUrl = "https://api.the-odds-api.test/v4/sports/soccer_fifa_world_cup/events/soccer-event-1/odds";

        var effectivePayloadMode = soccerWorldCupPayloadMode switch
        {
            "availabilityByRegion" => string.Equals(regions, "us", StringComparison.OrdinalIgnoreCase) ? "valid" : "zeroBookmakers",
            "sportsbookUs" or "multiSportsbookUs" => string.Equals(regions, "us", StringComparison.OrdinalIgnoreCase) ? "sportsbookUs" : "zeroBookmakers",
            _ => soccerWorldCupPayloadMode
        };

        return effectivePayloadMode switch
        {
            "zeroBookmakers" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [],
                [BuildSoccerWorldCupDiagnostics(eventId, [], [], outcomeCount: 0, playerOutcomeCount: 0)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                """{"bookmakers":[]}"""),
            "zeroMarkets" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [],
                [BuildSoccerWorldCupDiagnostics(eventId, ["underdog"], [], outcomeCount: 0, playerOutcomeCount: 0)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                """{"bookmakers":[{"key":"underdog","markets":[]}]}"""),
            "unsupportedMarket" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [],
                [BuildSoccerWorldCupDiagnostics(eventId, ["underdog"], ["player_passes"], outcomeCount: 2, playerOutcomeCount: 2)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                """{"bookmakers":[{"key":"underdog","markets":[{"key":"player_passes"}]}]}"""),
            "configuredMarketZeroOutcomes" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [],
                [BuildSoccerWorldCupDiagnostics(eventId, ["underdog"], ["player_shots"], outcomeCount: 0, playerOutcomeCount: 0)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                """{"bookmakers":[{"key":"underdog","markets":[{"key":"player_shots","outcomes":[]}]}]}"""),
            "sportsbookUs" => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [
                    new TheOddsApiPlayerPropSpikeRecord(
                        "The Odds API",
                        "betmgm",
                        "BetMGM",
                        eventId,
                        eventStartTime,
                        homeTeam,
                        awayTeam,
                        "player_goals",
                        new DateTimeOffset(2026, 6, 21, 18, 45, 0, TimeSpan.Zero),
                        "Over",
                        "Test Forward",
                        "Over",
                        0.5m,
                        120,
                        2.2m,
                        "Goals",
                        $"theodds:betmgm:{eventId}:player_goals:test-forward:0.5",
                        new { }),
                    new TheOddsApiPlayerPropSpikeRecord(
                        "The Odds API",
                        "betmgm",
                        "BetMGM",
                        eventId,
                        eventStartTime,
                        homeTeam,
                        awayTeam,
                        "player_goals",
                        new DateTimeOffset(2026, 6, 21, 18, 45, 0, TimeSpan.Zero),
                        "Under",
                        "Test Forward",
                        "Under",
                        0.5m,
                        -150,
                        1.6667m,
                        "Goals",
                        $"theodds:betmgm:{eventId}:player_goals:test-forward:0.5",
                        new { })
                ],
                [BuildSoccerWorldCupDiagnostics(eventId, ["betmgm"], ["player_goals"], outcomeCount: 2, playerOutcomeCount: 2)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                "{}"),
            _ => new TheOddsApiPlayerPropsSpikeResult(
                [eventRecord],
                eventId,
                [
                    new TheOddsApiPlayerPropSpikeRecord(
                        "The Odds API",
                        "prizepicks",
                        "PrizePicks",
                        eventId,
                        new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
                        "Home FC",
                        "Away FC",
                        "player_shots",
                        new DateTimeOffset(2026, 6, 21, 18, 45, 0, TimeSpan.Zero),
                        "Over",
                        "Test Forward",
                        "Over",
                        1.5m,
                        -110,
                        1.91m,
                        "Shots",
                        "theodds:prizepicks:soccer-event-1:player_shots:test-forward:1.5",
                        new { }),
                    new TheOddsApiPlayerPropSpikeRecord(
                        "The Odds API",
                        "prizepicks",
                        "PrizePicks",
                        eventId,
                        new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
                        "Home FC",
                        "Away FC",
                        "player_shots",
                        new DateTimeOffset(2026, 6, 21, 18, 45, 0, TimeSpan.Zero),
                        "Under",
                        "Test Forward",
                        "Under",
                        1.5m,
                        -110,
                        1.91m,
                        "Shots",
                        "theodds:prizepicks:soccer-event-1:player_shots:test-forward:1.5",
                        new { })
                ],
                [BuildSoccerWorldCupDiagnostics(eventId, ["prizepicks"], ["player_shots"], outcomeCount: 2, playerOutcomeCount: 2)],
                new TheOddsApiCreditUsage(2, 98, 1),
                requestUrl,
                "{}")
        };
    }

    private static TheOddsApiEventOddsDiagnostics BuildSoccerWorldCupDiagnostics(
        string? eventId,
        IReadOnlyList<string> bookmakerKeys,
        IReadOnlyList<string> marketKeys,
        int outcomeCount,
        int playerOutcomeCount)
    {
        return new TheOddsApiEventOddsDiagnostics(
            eventId,
            "Home FC",
            "Away FC",
            new DateTimeOffset(2026, 6, 21, 19, 0, 0, TimeSpan.Zero),
            EventOddsHttpSucceeded: true,
            bookmakerKeys.Count,
            bookmakerKeys,
            marketKeys.Count,
            marketKeys,
            outcomeCount,
            playerOutcomeCount);
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
        var fixtures = BuildFixtureResponseItems(request);
        var payload = JsonDocument.Parse(
            $$"""
            {
              "get": "fixtures",
              "parameters": {},
              "errors": [],
              "results": {{fixtures.Count}},
              "paging": { "current": 1, "total": 1 },
              "response": [{{string.Join(",", fixtures)}}]
            }
            """);

        return Task.FromResult(new ApiSportsFootballDiagnosticResponse(payload, fixtures.Count, 1, 1, sanitizedUrl));
    }

    public Task<ApiSportsFootballDiagnosticResponse> GetFixturePlayerStatsAsync(
        string fixtureId,
        CancellationToken cancellationToken)
    {
        FixturePlayerStatsCallCount++;
        if (fixtureId == "99999")
        {
            throw new InvalidOperationException("Synthetic API-Sports player-stat failure.");
        }

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

    private static List<string> BuildFixtureResponseItems(ApiSportsFootballFixturesRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FixtureId))
        {
            return request.FixtureId switch
            {
                "1489393" => [BuildFixtureJson("1489393", "2026-06-11T19:00:00+00:00", "FT", "Match Finished")],
                "99999" => [BuildFixtureJson("99999", "2026-06-14T19:00:00+00:00", "FT", "Match Finished")],
                _ => [BuildFixtureJson(request.FixtureId, "2026-06-20T19:00:00+00:00", "FT", "Match Finished")]
            };
        }

        return request.Date?.ToString("yyyy-MM-dd") switch
        {
            "2026-06-11" => [BuildFixtureJson("1489393", "2026-06-11T19:00:00+00:00", "FT", "Match Finished")],
            "2026-06-12" => [BuildFixtureJson("22222", "2026-06-12T19:00:00+00:00", "NS", "Not Started")],
            "2026-06-13" => [BuildFixtureJson("33333", "2026-06-13T19:00:00+00:00", "1H", "First Half")],
            "2026-06-14" => [BuildFixtureJson("99999", "2026-06-14T19:00:00+00:00", "FT", "Match Finished")],
            _ => []
        };
    }

    private static string BuildFixtureJson(string fixtureId, string kickoffUtc, string statusShort, string statusLong)
    {
        return $$"""
        {
          "fixture": { "id": {{fixtureId}}, "date": "{{kickoffUtc}}", "status": { "short": "{{statusShort}}", "long": "{{statusLong}}" } },
          "league": { "id": 1, "name": "World Cup", "season": 2026, "round": "Group Stage" },
          "teams": {
            "home": { "id": 10, "name": "Home FC" },
            "away": { "id": 20, "name": "Away FC" }
          }
        }
        """;
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

