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
}

public sealed class SafetyTestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection databaseConnection = new("Data Source=:memory:");
    private readonly IReadOnlyDictionary<string, string?> configOverrides;

    public CountingTheOddsAdapter TheOddsAdapter { get; } = new();
    public CountingUnderdogAdapter UnderdogAdapter { get; } = new();

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
                ["MlbResultImportWorker:Enabled"] = "false"
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
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(databaseConnection));
            services.AddSingleton<ITheOddsApiMlbPlayerPropsAdapter>(TheOddsAdapter);
            services.AddSingleton<IUnderdogAdapter>(UnderdogAdapter);
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

public sealed record ArtifactCounts(
    int RefreshRuns,
    int TopPickSnapshots,
    int LineShoppingSnapshots,
    int PlannerDecisions);
