using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Tests;

public sealed class SoccerWorldCupPredictionServiceTests
{
    [Fact]
    public async Task DryRunStylePrediction_ReadsPersistedRowsWithoutWriting()
    {
        await using var factory = CreateFactory(predictionsEnabled: true);
        var seed = await SeedSoccerScenarioAsync(factory, historicalValues: [2m, 1m, 3m, 0m, 2m]);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var beforeLinks = await dbContext.ProviderEntityLinks.CountAsync();
        var beforeResults = await dbContext.PropResults.CountAsync();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerWorldCupPredictionService>();

        var batch = await service.BuildPredictionsAsync([seed.Input], requireEnabled: true, CancellationToken.None);

        Assert.Equal(1, batch.RowsWithModelProbability);
        Assert.Equal(beforeLinks, await dbContext.ProviderEntityLinks.CountAsync());
        Assert.Equal(beforeResults, await dbContext.PropResults.CountAsync());
    }

    [Fact]
    public async Task PredictionGeneration_ComputesEmpiricalProbability_WhenEnoughSampleExists()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, minimumSampleSize: 5);
        var seed = await SeedSoccerScenarioAsync(factory, historicalValues: [2m, 1m, 3m, 0m, 2m], line: 0.5m);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerWorldCupPredictionService>();
        var batch = await service.BuildPredictionsAsync([seed.Input], requireEnabled: true, CancellationToken.None);
        var prediction = Assert.Single(batch.PredictionsByKey.Values);

        Assert.True(prediction.ModelProbabilityAvailable);
        Assert.Equal(0.8m, prediction.ModelProbabilityOver);
        Assert.Equal(0.2m, prediction.ModelProbabilityUnder);
        Assert.Equal("Over", prediction.SelectedSide);
        Assert.Equal(5, prediction.HistoricalSampleSize);
    }

    [Fact]
    public async Task LowSample_ReturnsWarningAndDoesNotBlock_WhenMinimumSampleClears()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, minimumSampleSize: 2, lowSampleThreshold: 10);
        var seed = await SeedSoccerScenarioAsync(factory, historicalValues: [1m, 0m], line: 0.5m);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerWorldCupPredictionService>();
        var batch = await service.BuildPredictionsAsync([seed.Input], requireEnabled: true, CancellationToken.None);
        var prediction = Assert.Single(batch.PredictionsByKey.Values);

        Assert.True(prediction.ModelProbabilityAvailable);
        Assert.Equal("Low sample: 2 historical matches.", prediction.SampleWarning);
    }

    [Fact]
    public async Task GradingCoverage_SupportsSafePropTypes_WhenSameDateResultExists()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: true);
        var seed = await SeedSoccerScenarioAsync(
            factory,
            historicalValues: [2m, 1m, 3m, 0m, 2m],
            sameDateResult: 2m);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerWorldCupPredictionService>();
        var coverage = await service.BuildGradingCoverageAsync([seed.Input], requireEnabled: true, CancellationToken.None);

        Assert.True(coverage.Enabled);
        Assert.Equal(1, coverage.RowsGradeable);
        Assert.Equal(1, coverage.RowsWithSameDateResult);
    }

    [Fact]
    public async Task UnsupportedPropType_IsSkippedForPredictionAndGrading()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: true);
        var seed = await SeedSoccerScenarioAsync(
            factory,
            platformPropTypeName: "Cards",
            platformStatKey: "CARDS",
            marketKey: "player_cards",
            historicalStatKey: "GOALS",
            historicalPropTypeName: "Goals",
            historicalValues: [1m, 0m, 1m, 0m, 1m]);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerWorldCupPredictionService>();
        var batch = await service.BuildPredictionsAsync([seed.Input], requireEnabled: true, CancellationToken.None);
        var coverage = await service.BuildGradingCoverageAsync([seed.Input], requireEnabled: true, CancellationToken.None);
        var prediction = Assert.Single(batch.PredictionsByKey.Values);

        Assert.False(prediction.IsSupportedPropType);
        Assert.False(prediction.ModelProbabilityAvailable);
        Assert.Equal("UnsupportedSoccerPropType", prediction.UnavailableReason);
        Assert.Equal(1, coverage.UnsupportedPropRows);
        Assert.Equal(0, coverage.RowsGradeable);
    }

    [Fact]
    public async Task PublicSoccerLineShopping_ExposesProbabilityOnlyWhenEnabled_AndTopPicksCanRemainDisabled()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: false, topPicksEnabled: false);
        await SeedSoccerScenarioAsync(factory, historicalValues: [2m, 1m, 3m, 0m, 2m], line: 0.5m);
        using var client = factory.CreateClient();

        var lineShoppingResponse = await client.GetAsync("/api/markets/line-shopping?sport=SOCCER&competition=World%20Cup&includeBookmakerPrices=false");
        var topPicksResponse = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&competition=World%20Cup&take=10");

        lineShoppingResponse.EnsureSuccessStatusCode();
        topPicksResponse.EnsureSuccessStatusCode();
        var lineShopping = await lineShoppingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var topPicks = await topPicksResponse.Content.ReadFromJsonAsync<JsonElement>();
        var group = Assert.Single(lineShopping.GetProperty("comparisonGroups").EnumerateArray());

        Assert.True(group.GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.Equal(0.8m, group.GetProperty("rawSelectedSideProbability").GetDecimal());
        Assert.Equal(0.5788m, group.GetProperty("selectedSideProbability").GetDecimal());
        Assert.Equal("MarketImpliedShrinkage", group.GetProperty("probabilityCalibrationMethod").GetString());
        Assert.False(group.GetProperty("entryActionableForUser").GetBoolean());
        Assert.False(group.GetProperty("resultGradingSupported").GetBoolean());
        Assert.Empty(topPicks.GetProperty("topPicks").EnumerateArray());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task PublicSoccerTopPicks_ReturnsEligibleRows_WhenExplicitlyEnabled()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: true, topPicksEnabled: true);
        await SeedSoccerScenarioAsync(
            factory,
            historicalValues: BuildSoccerHistory(overWins: 60, underWins: 15),
            line: 0.5m,
            priceOver: -220,
            priceOverDecimal: 1.454545m);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&competition=World%20Cup&take=10");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var pick = Assert.Single(json.GetProperty("topPicks").EnumerateArray());
        Assert.True(json.TryGetProperty("topPicks", out _));
        Assert.False(json.TryGetProperty("picks", out _));
        Assert.Null(json.GetProperty("fallbackReason").GetString());
        Assert.Contains("Soccer predictions and grading are enabled", json.GetProperty("limitation").GetString());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("activeCandidateCount").GetInt32());
        Assert.True(pick.GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.True(pick.GetProperty("displayExpectedValue").GetDecimal() > 0m);
        Assert.Equal(JsonValueKind.Null, pick.GetProperty("evSuppressedReason").ValueKind);
        Assert.True(pick.GetProperty("isRecommendation").GetBoolean());
        Assert.True(pick.GetProperty("recommendationActionableForUser").GetBoolean());
        Assert.Equal("Sportsbook", pick.GetProperty("recommendationType").GetString());
        Assert.False(pick.GetProperty("entryActionableForUser").GetBoolean());
        Assert.True(pick.GetProperty("resultGradingSupported").GetBoolean());
        Assert.False(pick.GetProperty("sportsbookContextResearchOnly").GetBoolean());
        Assert.False(pick.GetProperty("isResearchOnly").GetBoolean());
        Assert.NotEqual("ResearchOnly", pick.GetProperty("strategyClassification").GetString());
        Assert.NotEqual("ResearchOnly", pick.GetProperty("strategyTier").GetString());
        Assert.NotEqual("ResearchOnly", pick.GetProperty("trustLevel").GetString());
        Assert.DoesNotContain(
            pick.GetProperty("riskTags").EnumerateArray(),
            tag => string.Equals(tag.GetString(), "ResearchOnlySample", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Soccer sportsbook recommendation. Entry Builder is disabled for Soccer.", pick.GetProperty("entryUnavailableReason").GetString());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task PublicTopPicksWithoutSport_ReturnsSoccerRecommendationsFromAllSportsRead()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: true, topPicksEnabled: true);
        await SeedSoccerScenarioAsync(
            factory,
            historicalValues: BuildSoccerHistory(overWins: 60, underWins: 15),
            line: 0.5m,
            priceOver: -220,
            priceOverDecimal: 1.454545m);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/markets/top-picks?take=1");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var pick = Assert.Single(json.GetProperty("topPicks").EnumerateArray());
        Assert.Equal("ALL", json.GetProperty("effectiveSport").GetString());
        Assert.True(json.GetProperty("summary").GetProperty("allSportsMerged").GetBoolean());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("returnedTopPicks").GetInt32());
        Assert.Equal("SOCCER", pick.GetProperty("sport").GetString());
        Assert.Equal("World Cup", pick.GetProperty("competition").GetString());
        Assert.True(pick.GetProperty("isRecommendation").GetBoolean());
        Assert.True(pick.GetProperty("recommendationActionableForUser").GetBoolean());
        Assert.False(pick.GetProperty("entryActionableForUser").GetBoolean());
        Assert.Equal(0, factory.TheOddsAdapter.CallCount);
    }

    [Fact]
    public async Task PublicSoccerTopPicks_DoesNotReturnResearchOnlyLowSampleRows()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: true, topPicksEnabled: true);
        await SeedSoccerScenarioAsync(factory, historicalValues: [2m, 1m, 3m, 0m, 2m], line: 0.5m);
        using var client = factory.CreateClient();

        var topPicksResponse = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&competition=World%20Cup&take=10");
        var lineShoppingResponse = await client.GetAsync("/api/markets/line-shopping?sport=SOCCER&competition=World%20Cup&includeBookmakerPrices=false");

        topPicksResponse.EnsureSuccessStatusCode();
        lineShoppingResponse.EnsureSuccessStatusCode();
        var topPicks = await topPicksResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lineShopping = await lineShoppingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lineGroup = Assert.Single(lineShopping.GetProperty("comparisonGroups").EnumerateArray());

        Assert.Empty(topPicks.GetProperty("topPicks").EnumerateArray());
        Assert.Equal(1, topPicks.GetProperty("summary").GetProperty("sampleBlockedCount").GetInt32());
        Assert.True(lineGroup.GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.True(lineGroup.GetProperty("isResearchOnly").GetBoolean());
        Assert.Equal("ResearchOnly", lineGroup.GetProperty("strategyTier").GetString());
    }

    [Fact]
    public async Task PublicSoccerTopPicks_ExcludesExpiredRows()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: true, topPicksEnabled: true);
        await SeedSoccerScenarioAsync(
            factory,
            historicalValues: BuildSoccerHistory(overWins: 18, underWins: 5),
            line: 0.5m,
            eventStart: DateTimeOffset.UtcNow.AddDays(-1));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&competition=World%20Cup&take=10");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(json.GetProperty("topPicks").EnumerateArray());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("activeCandidateCount").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("modelEligibleBeforeLifecycleCount").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("staleOrExpiredModelEligibleCount").GetInt32());
        Assert.Contains("stale, expired, or already started", json.GetProperty("fallbackReason").GetString());
    }

    [Fact]
    public async Task PublicSoccerTopPicks_ExcludesStaleRows()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: true, topPicksEnabled: true);
        await SeedSoccerScenarioAsync(
            factory,
            historicalValues: BuildSoccerHistory(overWins: 18, underWins: 5),
            line: 0.5m,
            eventStart: DateTimeOffset.UtcNow.AddDays(1),
            observedAt: DateTimeOffset.UtcNow.AddHours(-2));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/markets/top-picks?sport=SOCCER&competition=World%20Cup&take=10&freshnessMinutes=30");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(json.GetProperty("topPicks").EnumerateArray());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("activeCandidateCount").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("modelEligibleBeforeLifecycleCount").GetInt32());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("staleOrExpiredModelEligibleCount").GetInt32());
    }

    [Fact]
    public async Task SoccerDiagnostics_ReadConsistentEnabledFlags_AndEnabledWording()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, gradingEnabled: true, topPicksEnabled: true);
        await SeedSoccerScenarioAsync(factory, historicalValues: [2m, 1m, 3m, 0m, 2m], line: 0.5m);
        using var client = factory.CreateClient();

        var readinessResponse = await client.GetAsync("/api/debug/soccer/predictions/readiness?competition=World%20Cup");
        var eligibilityResponse = await client.GetAsync("/api/debug/soccer/public-row-eligibility?competition=World%20Cup");
        var freshnessResponse = await client.GetAsync("/api/markets/freshness?sport=SOCCER&competition=World%20Cup");
        var lineShoppingResponse = await client.GetAsync("/api/markets/line-shopping?sport=SOCCER&competition=World%20Cup&includeBookmakerPrices=false");

        readinessResponse.EnsureSuccessStatusCode();
        eligibilityResponse.EnsureSuccessStatusCode();
        freshnessResponse.EnsureSuccessStatusCode();
        lineShoppingResponse.EnsureSuccessStatusCode();
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eligibility = await eligibilityResponse.Content.ReadFromJsonAsync<JsonElement>();
        var freshness = await freshnessResponse.Content.ReadFromJsonAsync<JsonElement>();
        var lineShopping = await lineShoppingResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(readiness.GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.True(readiness.GetProperty("soccerTopPicksEnabled").GetBoolean());
        Assert.True(eligibility.GetProperty("flags").GetProperty("publicSoccerGradingEnabled").GetBoolean());
        Assert.True(eligibility.GetProperty("flags").GetProperty("soccerTopPicksEnabled").GetBoolean());
        Assert.Equal("Soccer predictions and grading are enabled for supported World Cup prop types only. Entry Builder remains disabled for Soccer.", freshness.GetProperty("usabilityWarning").GetString());
        Assert.Contains("Soccer predictions and grading are enabled", lineShopping.GetProperty("limitation").GetString());
        Assert.True(lineShopping.GetProperty("publicSoccerGradingEnabled").GetBoolean());
    }

    [Fact]
    public async Task PublicSoccerLineShopping_LeavesProbabilityUnavailable_WhenConfigDisabled()
    {
        await using var factory = CreateFactory(predictionsEnabled: false);
        await SeedSoccerScenarioAsync(factory, historicalValues: [2m, 1m, 3m, 0m, 2m], line: 0.5m);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/markets/line-shopping?sport=SOCCER&competition=World%20Cup&includeBookmakerPrices=false");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var group = Assert.Single(json.GetProperty("comparisonGroups").EnumerateArray());
        Assert.False(group.GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, group.GetProperty("selectedSideProbability").ValueKind);
    }

    [Fact]
    public async Task PublicSoccerLineShopping_UsesSpecificUnavailableReason_WhenSampleIsLow()
    {
        await using var factory = CreateFactory(predictionsEnabled: true, minimumSampleSize: 5);
        await SeedSoccerScenarioAsync(factory, historicalValues: [1m, 0m], line: 0.5m);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/markets/line-shopping?sport=SOCCER&competition=World%20Cup&includeBookmakerPrices=false");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var group = Assert.Single(json.GetProperty("comparisonGroups").EnumerateArray());
        var reason = group.GetProperty("hitChanceUnavailableReason").GetString();
        Assert.False(group.GetProperty("modelProbabilityAvailable").GetBoolean());
        Assert.Contains("only 2 historical matches", reason);
        Assert.DoesNotContain("not available yet", reason);
    }

    private static SafetyTestApplicationFactory CreateFactory(
        bool predictionsEnabled,
        bool gradingEnabled = false,
        bool topPicksEnabled = false,
        int minimumSampleSize = 5,
        int lowSampleThreshold = 10,
        int topPickMinimumSampleSize = 20,
        decimal topPickMinimumHitProbability = 0.55m,
        bool allowNegativeEv = true)
    {
        return new SafetyTestApplicationFactory(new Dictionary<string, string?>
        {
            ["SoccerPredictions:Enabled"] = predictionsEnabled.ToString(),
            ["SoccerPredictions:MinimumSampleSize"] = minimumSampleSize.ToString(),
            ["SoccerPredictions:LowSampleWarningThreshold"] = lowSampleThreshold.ToString(),
            ["SoccerPredictions:TopPickMinimumSampleSize"] = minimumSampleSize.ToString(),
            ["SoccerPredictions:MinimumTopPickHitProbability"] = "0.55",
            ["SoccerGrading:Enabled"] = gradingEnabled.ToString(),
            ["SoccerTopPicks:Enabled"] = topPicksEnabled.ToString(),
            ["SoccerTopPicks:MinimumTopPickSampleSize"] = topPickMinimumSampleSize.ToString(),
            ["SoccerTopPicks:MinimumHitProbability"] = topPickMinimumHitProbability.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SoccerTopPicks:AllowNegativeEv"] = allowNegativeEv.ToString()
        });
    }

    private static IReadOnlyList<decimal> BuildSoccerHistory(int overWins, int underWins)
    {
        return Enumerable
            .Repeat(1m, overWins)
            .Concat(Enumerable.Repeat(0m, underWins))
            .ToArray();
    }

    private static async Task<SeededSoccerPredictionScenario> SeedSoccerScenarioAsync(
        SafetyTestApplicationFactory factory,
        IReadOnlyList<decimal> historicalValues,
        decimal line = 0.5m,
        decimal? sameDateResult = null,
        string platformPropTypeName = "Shots",
        string platformStatKey = "SHOTS",
        string marketKey = "player_shots",
        string historicalStatKey = "SHOTS",
        string historicalPropTypeName = "Shots",
        DateTimeOffset? eventStart = null,
        DateTimeOffset? observedAt = null,
        int priceOver = -110,
        decimal priceOverDecimal = 1.91m,
        int priceUnder = -110,
        decimal priceUnderDecimal = 1.91m)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
        var worldCupLeague = new League { Id = Guid.NewGuid(), SportId = sport.Id, Name = "World Cup", Code = "WC2026", SeasonType = "2026" };
        var historyLeague = new League { Id = Guid.NewGuid(), SportId = sport.Id, Name = "EPL", Code = "API_SPORTS_EPL_2025", SeasonType = "2025" };
        var platform = new Platform { Id = Guid.NewGuid(), Name = "FanDuel", Slug = "theodds-fanduel", CreatedAt = now, UpdatedAt = now };
        var platformPropType = new PropType { Id = Guid.NewGuid(), SportId = sport.Id, Name = platformPropTypeName, StatKey = platformStatKey, ValueType = "numeric" };
        var historicalPropType = string.Equals(platformStatKey, historicalStatKey, StringComparison.OrdinalIgnoreCase)
            ? platformPropType
            : new PropType { Id = Guid.NewGuid(), SportId = sport.Id, Name = historicalPropTypeName, StatKey = historicalStatKey, ValueType = "numeric" };
        var platformPlayer = new Player { Id = Guid.NewGuid(), LeagueId = worldCupLeague.Id, DisplayName = "Test Forward", CreatedAt = now, UpdatedAt = now };
        var historicalPlayer = new Player { Id = Guid.NewGuid(), LeagueId = historyLeague.Id, DisplayName = "Test Forward", CreatedAt = now, UpdatedAt = now };
        var providerPlayerId = "70001";
        var eventStartUtc = eventStart ?? now.AddDays(1);
        var observedAtUtc = observedAt ?? now;
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            PlayerId = platformPlayer.Id,
            LeagueId = worldCupLeague.Id,
            PropTypeId = platformPropType.Id,
            NormalizedKey = "test-forward:shots",
            StartsAt = eventStartUtc,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Sports.Add(sport);
        dbContext.Leagues.AddRange(worldCupLeague, historyLeague);
        dbContext.Platforms.Add(platform);
        dbContext.PropTypes.Add(platformPropType);
        if (!ReferenceEquals(platformPropType, historicalPropType))
        {
            dbContext.PropTypes.Add(historicalPropType);
        }

        dbContext.Players.AddRange(platformPlayer, historicalPlayer);
        dbContext.Props.Add(prop);
        dbContext.PlatformProps.Add(new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = "soccer:test-forward:shots",
            ExternalStableKey = "real-event-1",
            Label = "Test Forward Shots",
            CurrentLine = line,
            OverAvailable = true,
            UnderAvailable = true,
            Status = "active",
            ObservedAt = observedAtUtc,
            Metadata = BuildWorldCupMetadata("real-event-1", marketKey, eventStartUtc, priceOver, priceOverDecimal, priceUnder, priceUnderDecimal)
        });
        dbContext.ProviderEntityLinks.Add(new ProviderEntityLink
        {
            Id = Guid.NewGuid(),
            SportId = sport.Id,
            EntityType = "Player",
            CanonicalEntityId = platformPlayer.Id,
            CanonicalEntityKind = "Player",
            CanonicalEntityName = platformPlayer.DisplayName,
            ProviderKey = "api-sports-football",
            ProviderEntityId = providerPlayerId,
            ProviderEntityName = historicalPlayer.DisplayName,
            LinkStatus = "Approved",
            LinkSource = "Test",
            Confidence = 1m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ApprovedAtUtc = now,
            ApprovedBy = "test"
        });
        dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
        {
            Id = Guid.NewGuid(),
            PlayerId = historicalPlayer.Id,
            Provider = "api-sports-football",
            ProviderPlayerId = providerPlayerId,
            ProviderDisplayName = historicalPlayer.DisplayName,
            CreatedAt = now,
            UpdatedAt = now
        });

        for (var index = 0; index < historicalValues.Count; index++)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = historicalPlayer.Id,
                LeagueId = historyLeague.Id,
                PropTypeId = historicalPropType.Id,
                EventDate = new DateOnly(2026, 1, 1).AddDays(index),
                ActualResultValue = historicalValues[index],
                ExternalSource = "api-sports-football",
                ExternalSourceIdentifier = $"api-sports-football:history-{index}:{providerPlayerId}:{historicalStatKey}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (sameDateResult.HasValue)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = historicalPlayer.Id,
                LeagueId = historyLeague.Id,
                PropTypeId = historicalPropType.Id,
                EventDate = DateOnly.FromDateTime(eventStartUtc.UtcDateTime.Date),
                ActualResultValue = sameDateResult.Value,
                ExternalSource = "api-sports-football",
                ExternalSourceIdentifier = $"api-sports-football:same-date:{providerPlayerId}:{historicalStatKey}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await dbContext.SaveChangesAsync();

        return new SeededSoccerPredictionScenario(new SoccerWorldCupPredictionInput(
            platformPlayer.Id,
            platformPropType.Id,
            platformPlayer.DisplayName,
            platformPropType.Name,
            marketKey,
            line,
            OverAvailable: true,
            UnderAvailable: true,
            DateOnly.FromDateTime(eventStartUtc.UtcDateTime.Date),
            PriceOver: priceOver,
            PriceOverDecimal: priceOverDecimal,
            PriceUnder: priceUnder,
            PriceUnderDecimal: priceUnderDecimal));
    }

    private static JsonDocument BuildWorldCupMetadata(
        string eventId,
        string marketKey,
        DateTimeOffset eventStart,
        int priceOver = -110,
        decimal priceOverDecimal = 1.91m,
        int priceUnder = -110,
        decimal priceUnderDecimal = 1.91m)
    {
        return JsonSerializer.SerializeToDocument(new
        {
            dataProvider = "The Odds API",
            contextSchemaVersion = "the-odds-provider-context-v2",
            providerCategory = "Sportsbook",
            competition = "World Cup",
            sportsbookContextResearchOnly = true,
            sport = new
            {
                code = "SOCCER",
                providerSportKey = "soccer_fifa_world_cup",
                competition = "World Cup"
            },
            eventInfo = new
            {
                eventId,
                eventStartTime = eventStart.ToUniversalTime().ToString("O"),
                competition = "World Cup",
                homeTeam = "Uruguay",
                awayTeam = "Cape Verde"
            },
            bookmaker = new
            {
                key = "fanduel",
                title = "FanDuel"
            },
            market = new
            {
                key = marketKey
            },
            entry = new
            {
                providerCategory = "Sportsbook",
                actionableForUser = false
            },
            options = new
            {
                higher = new { americanPrice = priceOver, decimalPrice = priceOverDecimal },
                lower = new { americanPrice = priceUnder, decimalPrice = priceUnderDecimal }
            }
        });
    }

    private sealed record SeededSoccerPredictionScenario(SoccerWorldCupPredictionInput Input);
}
