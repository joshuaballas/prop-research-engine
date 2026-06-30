using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Configuration;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public async Task AppConfigs_TableEntityExists()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.NotNull(dbContext.Model.FindEntityType(typeof(AppConfig)));
        Assert.Empty(await dbContext.AppConfigs.ToArrayAsync());
    }

    [Fact]
    public async Task AppConfigService_ReadsTypedValues()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();
        await AddAppConfigAsync(factory, "Soccer.Results.ApiSports.Worker.Enabled", "true", AppConfigValueTypes.Boolean);
        await AddAppConfigAsync(factory, "Soccer.Results.ApiSports.Worker.MaxFixturesTotalPerRun", "17", AppConfigValueTypes.Integer);
        await AddAppConfigAsync(factory, "Soccer.TopPicks.MinimumHitProbability", "0.61", AppConfigValueTypes.Decimal);

        using var scope = factory.Services.CreateScope();
        var appConfigService = scope.ServiceProvider.GetRequiredService<IAppConfigService>();
        var snapshot = await appConfigService.ReloadAsync();

        Assert.True(snapshot.GetValue("Soccer.Results.ApiSports.Worker.Enabled", false));
        Assert.Equal(17, snapshot.GetValue("Soccer.Results.ApiSports.Worker.MaxFixturesTotalPerRun", 0));
        Assert.Equal(0.61m, snapshot.GetValue("Soccer.TopPicks.MinimumHitProbability", 0m));
        Assert.True(AppConfigValueParser.TryParse("EPL,MLS", typeof(string[]), out var parsedStringList, out _));
        Assert.Equal(["EPL", "MLS"], Assert.IsType<string[]>(parsedStringList));
        Assert.True(AppConfigValueParser.TryParse("00:15:00", typeof(TimeSpan), out var parsedTimeSpan, out _));
        Assert.Equal(TimeSpan.FromMinutes(15), Assert.IsType<TimeSpan>(parsedTimeSpan));
    }

    [Fact]
    public async Task MissingAppConfig_FallsBackToAppsettings()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["SoccerResultImportWorker:RecentWindowDays"] = "6"
        });
        await factory.EnsureDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SoccerResultImportWorkerOptions>>().Value;

        Assert.Equal(6, options.RecentWindowDays);
    }

    [Fact]
    public async Task InvalidAppConfig_DiagnosesErrorAndFallsBack()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["SoccerResultImportWorker:RecentWindowDays"] = "6"
        });
        await factory.EnsureDatabaseAsync();
        await AddAppConfigAsync(factory, "Soccer.Results.ApiSports.Worker.RecentWindowDays", "not-an-int", AppConfigValueTypes.Integer);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAppConfigService>();
        var snapshot = await service.ReloadAsync();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SoccerResultImportWorkerOptions>>().Value;

        Assert.Contains(snapshot.ValidationIssues, issue => issue.Key == "Soccer.Results.ApiSports.Worker.RecentWindowDays");
        Assert.Equal(6, options.RecentWindowDays);
    }

    [Fact]
    public async Task AppConfigSeeder_IsIdempotentAndDoesNotOverwriteCustomizedValues()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsScheduledRefresh:IntervalMinutes"] = "15"
        });
        await factory.EnsureDatabaseAsync();

        var first = await AppConfigSeeder.SeedAsync(factory.Services);
        Assert.True(first.Inserted > 0);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await dbContext.AppConfigs.SingleAsync(config => config.Key == "TheOdds.ScheduledRefresh.IntervalMinutes");
            Assert.Equal(AppConfigCatalog.EnvironmentGlobal, row.Environment);
            row.Value = "99";
            row.UpdatedBy = "test";
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        var second = await AppConfigSeeder.SeedAsync(factory.Services);
        Assert.Equal(0, second.Inserted);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await dbContext.AppConfigs.SingleAsync(config => config.Key == "TheOdds.ScheduledRefresh.IntervalMinutes");
            var domainKeys = AppConfigCatalog.Definitions
                .Where(definition => definition.Classification is AppConfigClassifications.MoveToDomainTable or AppConfigClassifications.MovedToDomainTable)
                .Select(definition => definition.Key)
                .ToArray();
            Assert.Equal("99", row.Value);
            Assert.Empty(await dbContext.AppConfigs.Where(config => config.Key.Contains("ApiKey") || config.Key.Contains("ConnectionStrings")).ToArrayAsync());
            Assert.Empty(await dbContext.AppConfigs.Where(config => config.Key == "Soccer.Results.ApiSports.Worker.CompetitionKeys").ToArrayAsync());
            Assert.Empty(await dbContext.AppConfigs.Where(config => config.Key == "TheOdds.Api.Markets").ToArrayAsync());
            Assert.Empty(await dbContext.AppConfigs.Where(config => domainKeys.Contains(config.Key)).ToArrayAsync());
        }
    }

    [Fact]
    public async Task AppConfigSeeder_SeedsDevelopmentScheduledRefreshEnabledOverride()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        await AppConfigSeeder.SeedAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var enabledGlobal = await dbContext.AppConfigs.SingleAsync(row =>
            row.Key == "TheOdds.ScheduledRefresh.Enabled"
            && row.Environment == AppConfigCatalog.EnvironmentGlobal);
        var enabledDevelopment = await dbContext.AppConfigs.SingleAsync(row =>
            row.Key == "TheOdds.ScheduledRefresh.Enabled"
            && row.Environment == Environments.Development);
        var liveRefreshDevelopment = await dbContext.AppConfigs.SingleAsync(row =>
            row.Key == "TheOdds.ScheduledRefresh.AllowScheduledLiveRefresh"
            && row.Environment == Environments.Development);

        Assert.Equal("false", enabledGlobal.Value);
        Assert.Equal("true", enabledDevelopment.Value);
        Assert.Equal("true", liveRefreshDevelopment.Value);
    }

    [Fact]
    public async Task AppConfigSeeder_SeedsDevelopmentResultWorkerOverridesAcrossSupportedSports()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        await AppConfigSeeder.SeedAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var mlbGlobal = await dbContext.AppConfigs.SingleAsync(row =>
            row.Key == "Mlb.Results.Worker.Enabled"
            && row.Environment == AppConfigCatalog.EnvironmentGlobal);
        var mlbDevelopment = await dbContext.AppConfigs.SingleAsync(row =>
            row.Key == "Mlb.Results.Worker.Enabled"
            && row.Environment == Environments.Development);
        var soccerGlobal = await dbContext.AppConfigs.SingleAsync(row =>
            row.Key == "Soccer.Results.ApiSports.Worker.Enabled"
            && row.Environment == AppConfigCatalog.EnvironmentGlobal);
        var soccerDevelopment = await dbContext.AppConfigs.SingleAsync(row =>
            row.Key == "Soccer.Results.ApiSports.Worker.Enabled"
            && row.Environment == Environments.Development);

        Assert.Equal("true", mlbGlobal.Value);
        Assert.Equal("true", mlbDevelopment.Value);
        Assert.Equal("false", soccerGlobal.Value);
        Assert.Equal("true", soccerDevelopment.Value);
    }

    [Fact]
    public async Task AppConfigSeeder_PreservesOperatorEditedDevelopmentResultWorkerOverride()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var definition = AppConfigCatalog.FindByAppConfigKey("Mlb.Results.Worker.Enabled")!;
            dbContext.AppConfigs.Add(new AppConfig
            {
                Id = Guid.NewGuid(),
                Key = definition.Key,
                Value = "false",
                ValueType = definition.ValueType,
                Category = definition.Category,
                Description = definition.Description,
                DefaultValue = definition.DefaultValue,
                IsSensitive = definition.IsSensitive,
                IsRuntimeEditable = definition.IsRuntimeEditable,
                RequiresRestart = definition.RequiresRestart,
                ValidationJson = definition.ValidationJson,
                Environment = Environments.Development,
                IsEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "Operator"
            });
            await dbContext.SaveChangesAsync();
        }

        await AppConfigSeeder.SeedAsync(factory.Services);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var mlbDevelopment = await dbContext.AppConfigs.SingleAsync(row =>
                row.Key == "Mlb.Results.Worker.Enabled"
                && row.Environment == Environments.Development);

            Assert.Equal("false", mlbDevelopment.Value);
        }
    }

    [Fact]
    public async Task ProviderDomainConfigSeeder_SeedsCompetitionsMarketsBookmakersAndRefreshTargets()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        var result = await ProviderDomainConfigSeeder.SeedAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(result.CompetitionsInserted >= 8);
        Assert.True(result.MarketsInserted >= 16);
        Assert.True(result.BookmakersInserted >= 10);
        Assert.True(result.RefreshTargetsInserted >= 9);
        Assert.Contains(await dbContext.ProviderCompetitions.ToArrayAsync(), row =>
            row.ProviderKey == ProviderDomainConfigService.ApiSportsFootballProviderKey
            && row.CompetitionKey == "EPL"
            && row.ProviderLeagueId == 39
            && row.SupportsSeasonMetadata);
        Assert.Contains(await dbContext.ProviderCompetitions.ToArrayAsync(), row =>
            row.CompetitionKey == ProviderDomainConfigService.WorldCupCompetitionKey
            && row.ProviderLeagueId == 1
            && row.IsFixedSeason
            && row.FixedSeason == 2026);
        Assert.Contains(await dbContext.ProviderMarkets.ToArrayAsync(), row =>
            row.ProviderKey == ProviderDomainConfigService.TheOddsProviderKey
            && row.Sport == "MLB"
            && row.MarketKey == "batter_hits");
        Assert.Contains(await dbContext.ProviderMarkets.ToArrayAsync(), row =>
            row.ProviderKey == ProviderDomainConfigService.TheOddsProviderKey
            && row.Sport == "SOCCER"
            && row.CompetitionKey == ProviderDomainConfigService.WorldCupCompetitionKey
            && row.MarketKey == "player_shots");
        Assert.Contains(await dbContext.ProviderBookmakers.ToArrayAsync(), row =>
            row.BookmakerKey == "draftkings"
            && row.ProviderCategory == "Sportsbook"
            && row.IsDefault);
        Assert.Contains(await dbContext.ProviderBookmakers.ToArrayAsync(), row =>
            row.BookmakerKey == "prizepicks"
            && row.ProviderCategory == "DFS"
            && row.SupportsEntryBuilder);
        Assert.Contains(await dbContext.ProviderRefreshTargets.ToArrayAsync(), row =>
            row.TargetKey == "mlb-dfs"
            && row.Sport == "MLB"
            && row.ProviderCategory == "DFS"
            && row.RefreshIntervalMinutes == 15);
        Assert.Contains(await dbContext.ProviderRefreshTargets.ToArrayAsync(), row =>
            row.TargetKey == "mlb-sportsbook"
            && row.Sport == "MLB"
            && row.ProviderCategory == "Sportsbook"
            && row.RefreshIntervalMinutes == 15);
        Assert.Contains(await dbContext.ProviderRefreshTargets.ToArrayAsync(), row =>
            row.TargetKey == "soccer-world-cup-sportsbook"
            && row.CompetitionKey == ProviderDomainConfigService.WorldCupCompetitionKey
            && row.RefreshIntervalMinutes == 30);
    }

    [Fact]
    public async Task ProviderDomainConfigSeeder_IsIdempotentAndPreservesOperatorFlags()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        await ProviderDomainConfigSeeder.SeedAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var target = await dbContext.ProviderRefreshTargets.SingleAsync(row => row.TargetKey == "mlb-sportsbook");
            target.IsEnabled = true;
            target.Priority = 42;
            await dbContext.SaveChangesAsync();
        }

        var second = await ProviderDomainConfigSeeder.SeedAsync(factory.Services);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var target = await dbContext.ProviderRefreshTargets.SingleAsync(row => row.TargetKey == "mlb-sportsbook");
            Assert.Equal(0, second.RefreshTargetsInserted);
            Assert.True(target.IsEnabled);
            Assert.Equal(42, target.Priority);
        }
    }

    [Fact]
    public async Task ProviderDomainConfigDebugStatus_IsReadOnlyAndCompact()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await ProviderDomainConfigSeeder.SeedAsync(factory.Services);

        var response = await client.GetAsync("/api/debug/provider-domain-config/status");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(json.GetProperty("providerCompetitionCount").GetInt32() >= 8);
        Assert.True(json.GetProperty("providerMarketCount").GetInt32() >= 16);
        Assert.True(json.GetProperty("providerBookmakerCount").GetInt32() >= 10);
        Assert.True(json.GetProperty("providerRefreshTargetCount").GetInt32() >= 9);
        Assert.Equal(0, json.GetProperty("appsettingsFallbackCount").GetInt32());
        Assert.Equal(0, json.GetProperty("appsettingsFallbackInvocationCount").GetInt32());
        Assert.Empty(json.GetProperty("appsettingsFallbacks").EnumerateArray());
    }

    [Fact]
    public async Task ProviderDomainConfigDebugStatus_ListsAppsettingsFallbackDetails()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProviderDomainConfigService>();
            var scheduledOptions = scope.ServiceProvider.GetRequiredService<IOptions<TheOddsScheduledRefreshOptions>>().Value;
            var targets = await service.GetTheOddsScheduledRefreshTargetsAsync(
                scheduledOptions,
                CancellationToken.None);
            Assert.NotEmpty(targets);
        }

        var response = await client.GetAsync("/api/debug/provider-domain-config/status");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fallbacks = json.GetProperty("appsettingsFallbacks").EnumerateArray().ToArray();
        Assert.Equal(1, json.GetProperty("appsettingsFallbackCount").GetInt32());
        Assert.Equal(1, json.GetProperty("appsettingsFallbackInvocationCount").GetInt32());
        var fallback = Assert.Single(fallbacks);
        Assert.Equal("THE_ODDS_API:refresh-targets", fallback.GetProperty("key").GetString());
        Assert.Equal("ProviderRefreshTargets", fallback.GetProperty("configArea").GetString());
        Assert.Equal("TheOddsScheduledRefreshTargets", fallback.GetProperty("consumer").GetString());
        Assert.Contains("appsettings fallback", fallback.GetProperty("reason").GetString());
        Assert.Equal(1, fallback.GetProperty("occurrenceCount").GetInt32());
    }

    [Fact]
    public async Task ModelConfigSeeder_SeedsActiveModelSetsAndParameters()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();

        var result = await ModelConfigSeeder.SeedAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sets = await dbContext.ModelConfigSets.Include(row => row.Parameters).ToArrayAsync();

        Assert.Equal(2, result.SetsInserted);
        Assert.True(result.ParametersInserted >= 30);
        Assert.Contains(sets, row =>
            row.ModelKey == ModelConfigService.TopPicksModelKey
            && row.ModelEra == TopPicksModelOptions.DefaultLikelyHitV1ModelEra
            && row.IsActive);
        Assert.Contains(sets, row =>
            row.ModelKey == ModelConfigService.MlbPredictionGenerationModelKey
            && row.Sport == "MLB"
            && row.Parameters.Any(parameter => parameter.ParameterKey == "EnabledPropType.Pitcher Strikeouts"));
    }

    [Fact]
    public async Task ModelConfigOptionsOverride_UsesModelConfigRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();
        await ModelConfigSeeder.SeedAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var topPicks = await dbContext.ModelConfigParameters.SingleAsync(row =>
            row.ModelConfigSet!.ModelKey == ModelConfigService.TopPicksModelKey
            && row.ParameterKey == nameof(TopPicksModelOptions.MlbMinimumHitChance));
        topPicks.Value = "0.67";
        var minimumPick = await dbContext.ModelConfigParameters.SingleAsync(row =>
            row.ModelConfigSet!.ModelKey == ModelConfigService.MlbPredictionGenerationModelKey
            && row.ParameterKey == nameof(PredictionGenerationOptions.MinimumPickHitProbability));
        minimumPick.Value = "0.59";
        var hits = await dbContext.ModelConfigParameters.SingleAsync(row =>
            row.ModelConfigSet!.ModelKey == ModelConfigService.MlbPredictionGenerationModelKey
            && row.ParameterKey == "EnabledPropType.Hits");
        hits.Value = "false";
        await dbContext.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IModelConfigService>().ClearCache();

        var topPicksOptions = scope.ServiceProvider.GetRequiredService<IOptionsFactory<TopPicksModelOptions>>().Create(Options.DefaultName);
        var predictionOptions = scope.ServiceProvider.GetRequiredService<IOptionsFactory<PredictionGenerationOptions>>().Create(Options.DefaultName);

        Assert.Equal(0.67m, topPicksOptions.MlbMinimumHitChance);
        Assert.Equal(0.59m, predictionOptions.MinimumPickHitProbability);
        Assert.DoesNotContain("Hits", predictionOptions.EnabledPropTypes);
        Assert.Contains("Pitcher Strikeouts", predictionOptions.EnabledPropTypes);
    }

    [Fact]
    public async Task ModelConfigDebugStatus_IsReadOnlyAndCompact()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await ModelConfigSeeder.SeedAsync(factory.Services);

        var response = await client.GetAsync("/api/debug/model-config/status");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        Assert.False(json.GetProperty("writesRows").GetBoolean());
        Assert.True(json.GetProperty("modelConfigSetCount").GetInt32() >= 2);
        Assert.True(json.GetProperty("parameterCount").GetInt32() >= 30);
        Assert.Equal(TopPicksModelOptions.DefaultLikelyHitV1ModelEra, json.GetProperty("activeTopPicksModelEra").GetString());
        Assert.True(json.GetProperty("activeMlbPredictionConfigPresent").GetBoolean());
    }

    [Fact]
    public async Task ProviderDomainConfigService_ResolvesRuntimeListsFromDomainRows()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await factory.EnsureDatabaseAsync();
        await ProviderDomainConfigSeeder.SeedAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IProviderDomainConfigService>();
        var apiSportsOptions = scope.ServiceProvider.GetRequiredService<IOptions<ApiSportsOptions>>().Value;
        var theOddsApiOptions = scope.ServiceProvider.GetRequiredService<IOptions<TheOddsApiOptions>>().Value;
        var workerOptions = scope.ServiceProvider.GetRequiredService<IOptions<TheOddsIngestionWorkerOptions>>().Value;
        var scheduledOptions = scope.ServiceProvider.GetRequiredService<IOptions<TheOddsScheduledRefreshOptions>>().Value;

        var competitions = await service.GetApiSportsSoccerCompetitionsAsync(apiSportsOptions, enabledOnly: true, CancellationToken.None);
        var targets = await service.GetTheOddsScheduledRefreshTargetsAsync(scheduledOptions, CancellationToken.None);
        var mlbSportsbook = targets.Single(row => row.ResolveKey() == "mlb-sportsbook");
        var mlbDfs = targets.Single(row => row.ResolveKey() == "mlb-dfs");
        var worldCup = targets.Single(row => row.ResolveKey() == "soccer-world-cup-sportsbook");
        var sportsbookBookmakers = await service.GetTheOddsBookmakerKeysAsync(mlbSportsbook, workerOptions, CancellationToken.None);
        var dfsBookmakers = await service.GetTheOddsBookmakerKeysAsync(mlbDfs, workerOptions, CancellationToken.None);
        var mlbMarkets = await service.GetTheOddsMarketKeysAsync(mlbSportsbook, theOddsApiOptions, workerOptions, CancellationToken.None);

        Assert.Contains(competitions, row => row.Key == ProviderDomainConfigService.WorldCupCompetitionKey && row.IsWorldCup);
        Assert.Contains(competitions, row => row.Key == "EPL" && !row.IsWorldCup);
        Assert.Contains(targets, row => row.ResolveKey() == "soccer-world-cup-sportsbook");
        Assert.Equal(15, mlbDfs.RefreshIntervalMinutes);
        Assert.Equal(15, mlbSportsbook.RefreshIntervalMinutes);
        Assert.Equal(30, worldCup.RefreshIntervalMinutes);
        Assert.Contains("mybookieag", sportsbookBookmakers);
        Assert.Contains("prizepicks", dfsBookmakers);
        Assert.Contains("batter_hits", mlbMarkets);
    }

    [Fact]
    public async Task AppConfigOptionOverride_UsesAppConfigsValue()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsScheduledRefresh:Enabled"] = "false",
            ["TheOddsScheduledRefresh:IntervalMinutes"] = "15"
        });
        await factory.EnsureDatabaseAsync();
        await AddAppConfigAsync(factory, "TheOdds.ScheduledRefresh.Enabled", "true", AppConfigValueTypes.Boolean);
        await AddAppConfigAsync(factory, "TheOdds.ScheduledRefresh.IntervalMinutes", "21", AppConfigValueTypes.Integer);

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<TheOddsScheduledRefreshOptions>>().Value;

        Assert.True(options.Enabled);
        Assert.Equal(21, options.IntervalMinutes);
    }

    [Fact]
    public async Task ScheduledRefreshStatus_UsesEffectiveAppConfigEnabledOverride()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await AddAppConfigAsync(factory, "TheOdds.ScheduledRefresh.Enabled", "true", AppConfigValueTypes.Boolean, "Testing");
        await AddAppConfigAsync(factory, "TheOdds.ScheduledRefresh.AllowScheduledLiveRefresh", "true", AppConfigValueTypes.Boolean, "Testing");

        var response = await client.GetAsync("/api/debug/the-odds/mlb/scheduled-refresh/status");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("enabled").GetBoolean());
        Assert.True(json.TryGetProperty("nextRefreshDueUtc", out _));
    }

    [Fact]
    public async Task AppConfigOptionOverride_PrefersEnvironmentOverrideThenGlobalThenFallback()
    {
        await using var factory = new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["TheOddsScheduledRefresh:IntervalMinutes"] = "15"
        });
        await factory.EnsureDatabaseAsync();
        await AddAppConfigAsync(factory, "TheOdds.ScheduledRefresh.IntervalMinutes", "31", AppConfigValueTypes.Integer, AppConfigCatalog.EnvironmentGlobal);
        await AddAppConfigAsync(factory, "TheOdds.ScheduledRefresh.IntervalMinutes", "47", AppConfigValueTypes.Integer, "Testing");

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<TheOddsScheduledRefreshOptions>>().Value;
        var snapshot = await scope.ServiceProvider.GetRequiredService<IAppConfigService>().ReloadAsync();

        Assert.Equal(47, options.IntervalMinutes);
        Assert.Equal(1, snapshot.GlobalRowCount);
        Assert.Equal(1, snapshot.EnvironmentOverrideRowCount);
        Assert.Contains(snapshot.Values, row =>
            row.Key == "TheOdds.ScheduledRefresh.IntervalMinutes"
            && row.Source == "AppConfigs environment override"
            && row.SourceEnvironment == "Testing");
    }

    [Fact]
    public async Task AppConfigAudit_ClassifiesSecretsAndMasksValues()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();

        var response = await client.GetAsync("/api/debug/app-configs/audit");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("readOnly").GetBoolean());
        Assert.False(json.GetProperty("callsExternalApis").GetBoolean());
        var settings = json.GetProperty("settings").EnumerateArray().ToArray();
        var theOddsKey = settings.Single(row => row.GetProperty("key").GetString() == "TheOddsApi:ApiKey");
        Assert.Equal(AppConfigClassifications.KeepInSecretProvider, theOddsKey.GetProperty("classification").GetString());
        Assert.True(theOddsKey.GetProperty("secretOrEncryptionRequired").GetBoolean());
        Assert.NotEqual("THE_ODDS_API_KEY", theOddsKey.GetProperty("appsettingsValue").GetString());
    }

    [Fact]
    public async Task AppConfigAudit_ClassifiesStructuredCollectionsAsDomainTables()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/debug/app-configs/audit");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        var settings = json.GetProperty("settings").EnumerateArray().ToArray();
        var historicalCompetitions = settings.Single(row => row.GetProperty("key").GetString() == "ApiSports:Football:HistoricalCompetitions");
        var scheduledTargets = settings.Single(row => row.GetProperty("key").GetString() == "TheOddsScheduledRefresh:Targets");

        Assert.Equal(AppConfigClassifications.MovedToDomainTable, historicalCompetitions.GetProperty("classification").GetString());
        Assert.Equal(AppConfigClassifications.MovedToDomainTable, scheduledTargets.GetProperty("classification").GetString());
        Assert.DoesNotContain(settings, row => row.GetProperty("key").GetString()?.StartsWith("ApiSports:Football:HistoricalCompetitions:0:", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void AppConfigCatalog_MetadataMarksQuestionableSettings()
    {
        var appConfigDefinitions = AppConfigCatalog.Definitions
            .Where(definition => definition.Classification == AppConfigClassifications.MoveToAppConfigs)
            .ToArray();

        Assert.All(appConfigDefinitions, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(definition.HelpText));
            if (definition.ValueType is AppConfigValueTypes.Integer or AppConfigValueTypes.Decimal)
            {
                Assert.False(string.IsNullOrWhiteSpace(definition.Unit));
                Assert.NotNull(definition.MinValue);
            }
        });

        Assert.Contains(AppConfigCatalog.Definitions, definition =>
            definition.Key == "Soccer.Results.ApiSports.Worker.MaxFixturesPerRun"
            && definition.IsAdvanced
            && definition.IsHidden);
        Assert.Contains(AppConfigCatalog.Definitions, definition =>
            definition.Key.StartsWith("TheOdds.IngestionWorker.", StringComparison.Ordinal)
            && definition.IsAdvanced
            && definition.IsHidden);
        Assert.Contains(AppConfigCatalog.Definitions, definition =>
            definition.Key.StartsWith("Underdog.Ingestion.", StringComparison.Ordinal)
            && definition.IsAdvanced
            && definition.IsHidden);
    }

    [Fact]
    public async Task AppConfigsDefaultEndpoint_ReturnsOnlyVisibleScalarEffectiveValues()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await AddAppConfigAsync(factory, "Soccer.Results.ApiSports.Worker.Enabled", "true", AppConfigValueTypes.Boolean);

        var response = await client.GetAsync("/api/debug/app-configs");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("readOnly").GetBoolean());
        var values = json.GetProperty("values").EnumerateArray().ToArray();
        Assert.All(values, row =>
        {
            Assert.Equal(AppConfigClassifications.MoveToAppConfigs, row.GetProperty("classification").GetString());
            Assert.False(row.GetProperty("isSensitive").GetBoolean());
            Assert.False(row.GetProperty("isAdvanced").GetBoolean());
            Assert.False(row.GetProperty("isHidden").GetBoolean());
            Assert.True(row.TryGetProperty("configuredValue", out _));
            Assert.True(row.TryGetProperty("effectiveValue", out _));
            Assert.True(row.TryGetProperty("defaultValue", out _));
            Assert.True(row.TryGetProperty("displayValue", out _));
            Assert.True(row.TryGetProperty("isValueMasked", out _));
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("displayName").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("shortDisplayName").GetString()));
        });
        Assert.Contains(values, row =>
            row.GetProperty("key").GetString() == "Soccer.Results.ApiSports.Worker.Enabled"
            && row.GetProperty("configuredValue").GetString() == "true"
            && row.GetProperty("effectiveValue").GetString() == "true"
            && row.GetProperty("displayValue").GetString() == "true");
        Assert.Contains(values, row =>
            row.GetProperty("key").GetString() == "Soccer.Results.ApiSports.Worker.Enabled"
            && row.TryGetProperty("displayName", out var displayName)
            && !string.IsNullOrWhiteSpace(displayName.GetString())
            && row.TryGetProperty("helpText", out var helpText)
            && !string.IsNullOrWhiteSpace(helpText.GetString()));
        Assert.DoesNotContain(values, row =>
            row.GetProperty("key").GetString() is "TheOdds.ApiKey" or "ApiSports.ApiKey");
        Assert.DoesNotContain(values, row => row.GetProperty("classification").GetString() == AppConfigClassifications.KeepInSecretProvider);
        Assert.DoesNotContain(values, row => row.GetProperty("classification").GetString() == AppConfigClassifications.KeepInBootstrapConfig);
        Assert.DoesNotContain(values, row => row.GetProperty("classification").GetString() == AppConfigClassifications.MoveToDomainTable);
        Assert.DoesNotContain(values, row => row.GetProperty("classification").GetString() == AppConfigClassifications.MovedToDomainTable);
        Assert.DoesNotContain(values, row => row.GetProperty("classification").GetString() == AppConfigClassifications.NeedsDecision);
        Assert.DoesNotContain(values, row => row.GetProperty("classification").GetString() == AppConfigClassifications.RemoveRedundant);
        Assert.DoesNotContain(values, row => row.GetProperty("key").GetString() == "Soccer.Results.ApiSports.Worker.CompetitionKeys");
        Assert.DoesNotContain(values, row => row.GetProperty("key").GetString() == "ApiSports.Football.WorldCupLeagueId");
        Assert.DoesNotContain(values, row => row.GetProperty("key").GetString() == "ApiSports.Football.Season");
        Assert.DoesNotContain(values, row => row.GetProperty("key").GetString() == "TheOdds.RefreshPlanner.FarOutMinMinutes");
    }

    [Fact]
    public async Task AppConfigStatus_NormalizesValidationIssueCount()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseAsync();
        await AppConfigSeeder.SeedAsync(factory.Services);

        var response = await client.GetAsync("/api/debug/app-configs/status");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, json.GetProperty("validationIssueCount").GetInt32());
        Assert.True(json.TryGetProperty("globalRowCount", out _));
        Assert.True(json.TryGetProperty("environmentOverrideRowCount", out _));
        Assert.True(json.TryGetProperty("moveToDomainTableCount", out _));
        Assert.True(json.TryGetProperty("advancedCount", out _));
        Assert.True(json.TryGetProperty("hiddenCount", out _));
    }

    [Fact]
    public async Task AppConfigsAudit_ExcludesLegacySoccerConfigAndMatchesStatusCounts()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var statusResponse = await client.GetAsync("/api/debug/app-configs/status");
        statusResponse.EnsureSuccessStatusCode();
        var statusJson = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        var auditResponse = await client.GetAsync("/api/debug/app-configs/audit");
        auditResponse.EnsureSuccessStatusCode();
        var auditJson = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();

        var auditSettings = auditJson.GetProperty("settings").EnumerateArray().ToArray();
        Assert.DoesNotContain(auditSettings, row => row.GetProperty("key").GetString() == "ApiSports:Football:WorldCupLeagueId");
        Assert.DoesNotContain(auditSettings, row => row.GetProperty("key").GetString() == "ApiSports:Football:Season");
        Assert.DoesNotContain(auditSettings, row => row.GetProperty("appConfigKey").GetString() == "Soccer.Results.ApiSports.Worker.CompetitionKeys");
        Assert.DoesNotContain(auditSettings, row => row.GetProperty("isChildAuditItem").GetBoolean());

        var statusCounts = statusJson.GetProperty("countsByClassification")
            .EnumerateArray()
            .ToDictionary(row => row.GetProperty("classification").GetString()!, row => row.GetProperty("count").GetInt32(), StringComparer.OrdinalIgnoreCase);
        var auditCounts = auditJson.GetProperty("countsByClassification")
            .EnumerateObject()
            .ToDictionary(row => row.Name, row => row.Value.GetInt32(), StringComparer.OrdinalIgnoreCase);

        foreach (var (classification, count) in statusCounts)
        {
            Assert.True(auditCounts.TryGetValue(classification, out var auditCount), $"Missing audit count for {classification}.");
            Assert.Equal(count, auditCount);
        }

        var childResponse = await client.GetAsync("/api/debug/app-configs/audit?includeChildren=true");
        childResponse.EnsureSuccessStatusCode();
        var childJson = await childResponse.Content.ReadFromJsonAsync<JsonElement>();
        var childSettings = childJson.GetProperty("settings").EnumerateArray().ToArray();
        Assert.DoesNotContain(childSettings, row =>
            row.GetProperty("classification").GetString() == AppConfigClassifications.NeedsDecision
            && (row.GetProperty("key").GetString()?.StartsWith("MlbPredictionGeneration:", StringComparison.OrdinalIgnoreCase) == true
                || row.GetProperty("key").GetString()?.StartsWith("TopPicksModel:", StringComparison.OrdinalIgnoreCase) == true
                || row.GetProperty("key").GetString()?.Contains(":Targets:", StringComparison.OrdinalIgnoreCase) == true
                || row.GetProperty("key").GetString()?.Contains(":HistoricalCompetitions:", StringComparison.OrdinalIgnoreCase) == true));
    }

    [Fact]
    public async Task CommittedAppsettings_ArePrunedToBootstrapSecretsAndProviderWiring()
    {
        var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "api"));
        var appsettingsPath = Path.Combine(contentRoot, "appsettings.json");
        await using var stream = File.OpenRead(appsettingsPath);
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var forbiddenSections = new[]
        {
            "TheOddsScheduledRefresh",
            "TheOddsRefreshPlanner",
            "TheOddsIngestionWorker",
            "MlbResultImportWorker",
            "SoccerResultImportWorker",
            "SoccerIdentityResolution",
            "SoccerPredictions",
            "SoccerGrading",
            "SoccerTopPicks",
            "MlbPredictionGeneration",
            "TopPicksModel",
            "UnderdogIngestion"
        };

        foreach (var section in forbiddenSections)
        {
            Assert.False(root.TryGetProperty(section, out _), $"{section} should not be in committed appsettings.json.");
        }

        Assert.True(root.TryGetProperty("ConnectionStrings", out _));
        Assert.True(root.TryGetProperty("TheOddsApi", out var theOdds));
        Assert.True(theOdds.TryGetProperty("BaseUrl", out _));
        Assert.False(theOdds.TryGetProperty("Markets", out _));
        Assert.True(root.TryGetProperty("ApiSports", out var apiSports));
        Assert.True(apiSports.GetProperty("Football").TryGetProperty("BaseUrl", out _));
        Assert.False(apiSports.GetProperty("Football").TryGetProperty("HistoricalCompetitions", out _));
    }

    [Fact]
    public async Task AppConfigsIncludeAuditCandidates_MasksSecretsAndKeepsRedundantOutOfDefault()
    {
        await using var factory = new SafetyTestApplicationFactory();
        using var client = factory.CreateClient();

        var defaultResponse = await client.GetAsync("/api/debug/app-configs");
        defaultResponse.EnsureSuccessStatusCode();
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        var defaultValues = defaultJson.GetProperty("values").EnumerateArray().ToArray();
        Assert.DoesNotContain(defaultValues, row => row.GetProperty("key").GetString() == "TheOdds.RefreshPlanner.FarOutMinMinutes");

        var auditResponse = await client.GetAsync("/api/debug/app-configs?includeAuditCandidates=true&includeHidden=true&includeAdvanced=true&includeSecrets=true");
        auditResponse.EnsureSuccessStatusCode();
        var auditJson = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();
        var values = auditJson.GetProperty("values").EnumerateArray().ToArray();
        var secret = values.Single(row => row.GetProperty("key").GetString() == "TheOdds.ApiKey");
        Assert.True(secret.GetProperty("isValueMasked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, secret.GetProperty("effectiveValue").ValueKind);
        Assert.NotEqual("THE_ODDS_API_KEY", secret.GetProperty("displayValue").GetString());
        Assert.Contains(values, row =>
            row.GetProperty("key").GetString() == "TheOdds.RefreshPlanner.FarOutMinMinutes"
            && row.GetProperty("classification").GetString() == AppConfigClassifications.RemoveRedundant);
    }

    [Fact]
    public void AppConfigCatalog_HasPolishedMetadata()
    {
        Assert.All(AppConfigCatalog.Definitions, definition =>
        {
            Assert.DoesNotContain("  ", definition.Category);
            Assert.DoesNotContain("  ", definition.Section);
            Assert.DoesNotContain("  ", definition.DisplayName);
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(definition.ShortDisplayName));
            if (definition.Classification == AppConfigClassifications.MoveToAppConfigs
                && definition.ValueType == AppConfigValueTypes.Boolean
                && !definition.IsHidden)
            {
                Assert.NotEqual("Enabled", definition.DisplayName);
                Assert.True(
                    ContainsAny(definition.HelpText, ["enable", "allow", "require", "block", "stop", "whether", "control", "record", "import"]),
                    $"Boolean help text should explain the toggle: {definition.Key}");
            }
        });
    }

    private static bool ContainsAny(string value, IEnumerable<string> needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static async Task AddAppConfigAsync(
        SafetyTestApplicationFactory factory,
        string key,
        string value,
        string valueType,
        string environment = "Testing")
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var definition = AppConfigCatalog.FindByAppConfigKey(key);
        dbContext.AppConfigs.Add(new AppConfig
        {
            Id = Guid.NewGuid(),
            Key = key,
            Value = value,
            ValueType = valueType,
            Category = definition?.Category ?? "Test",
            Description = definition?.Description,
            DefaultValue = definition?.DefaultValue,
            IsSensitive = definition?.IsSensitive ?? false,
            IsRuntimeEditable = definition?.IsRuntimeEditable ?? true,
            RequiresRestart = definition?.RequiresRestart ?? false,
            ValidationJson = definition?.ValidationJson,
            Environment = environment,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedBy = "test"
        });
        await dbContext.SaveChangesAsync();
    }
}
