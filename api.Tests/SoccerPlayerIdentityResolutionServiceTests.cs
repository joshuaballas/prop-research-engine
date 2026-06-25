using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Tests;

public sealed class SoccerPlayerIdentityResolutionServiceTests
{
    [Fact]
    public async Task DryRun_WritesNothing()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await SeedSafeAliasToApprovedProviderAsync(factory);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var result = await service.ReconcileVisibleAsync(BuildRequest(dryRun: true), CancellationToken.None);
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, result.SafeLinkable);
        Assert.Equal(0, result.Written);
        Assert.Equal(1, await CountApprovedApiSportsLinksAsync(dbContext, "747"));
    }

    [Fact]
    public async Task WriteMode_CreatesSafeAliasToAlreadyApprovedProviderLink()
    {
        await using var factory = new SafetyTestApplicationFactory();
        var localPlayerId = await SeedSafeAliasToApprovedProviderAsync(factory);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var result = await service.ReconcileVisibleAsync(BuildRequest(dryRun: false), CancellationToken.None);
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, result.Written);
        var link = await dbContext.ProviderEntityLinks.SingleAsync(link =>
            link.ProviderKey == "api-sports-football"
            && link.ProviderEntityId == "747"
            && link.CanonicalEntityId == localPlayerId);
        Assert.Equal("SafeAliasToAlreadyApprovedProvider", link.LinkSource);
    }

    [Fact]
    public async Task WriteMode_CreatesSafeReviewedAliasPatternLink()
    {
        await using var factory = new SafetyTestApplicationFactory();
        var localPlayerId = await SeedVisibleAliasScenarioAsync(
            factory,
            visiblePlayerName: "Juan Manuel Sanabria",
            providerPlayerId: "162891",
            providerDisplayName: "Juan Sanabria");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var result = await service.ReconcileVisibleAsync(BuildRequest(dryRun: false), CancellationToken.None);
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, result.Written);
        var link = await dbContext.ProviderEntityLinks.SingleAsync(link =>
            link.ProviderKey == "api-sports-football"
            && link.ProviderEntityId == "162891"
            && link.CanonicalEntityId == localPlayerId);
        Assert.Equal("SafeReviewedAliasPattern", link.LinkSource);
    }

    [Fact]
    public async Task ExistingApprovedLocalLink_BlocksOverwrite()
    {
        await using var factory = new SafetyTestApplicationFactory();
        var localPlayerId = await SeedSafeAliasToApprovedProviderAsync(factory);
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sportId = await dbContext.Sports.Where(sport => sport.Code == "SOCCER").Select(sport => sport.Id).SingleAsync();
            dbContext.ProviderEntityLinks.Add(BuildApprovedLink(sportId, localPlayerId, "existing-provider", "Existing Provider", "ExistingTestLink"));
            await dbContext.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var result = await service.ReconcileVisibleAsync(BuildRequest(dryRun: false), CancellationToken.None);
        var verifyContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(0, result.Written);
        Assert.Empty(result.Rows);
        Assert.Equal(1, await verifyContext.ProviderEntityLinks.CountAsync(link =>
            link.ProviderKey == "api-sports-football"
            && link.CanonicalEntityId == localPlayerId
            && link.LinkStatus == "Approved"));
    }

    [Fact]
    public async Task AmbiguousHighFanoutCandidate_IsReviewRequiredAndDoesNotWrite()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await SeedVisibleAliasScenarioAsync(
            factory,
            visiblePlayerName: "Danilo Luiz da Silva",
            providerPlayerId: "618",
            providerDisplayName: "Danilo",
            extraProviderNames: ["Danilo Silva", "Luiz Danilo", "Danilo Luiz", "Daniel Silva"]);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var result = await service.ReconcileVisibleAsync(BuildRequest(dryRun: false, minScore: 0.80m), CancellationToken.None);

        Assert.Equal(0, result.Written);
        var row = Assert.Single(result.Rows);
        Assert.Equal("ReviewRequired", row.Decision);
        Assert.Contains("CandidateCountExceedsMaxCandidateCount", row.BlockReasons);
    }

    [Fact]
    public async Task BelowThresholdCandidate_IsReviewRequiredAndDoesNotWrite()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await SeedVisibleAliasScenarioAsync(
            factory,
            visiblePlayerName: "Rayan Vitor",
            providerPlayerId: "407806",
            providerDisplayName: "Rayan");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var result = await service.ReconcileVisibleAsync(BuildRequest(dryRun: false, minScore: 0.90m), CancellationToken.None);

        Assert.Equal(0, result.Written);
        var row = Assert.Single(result.Rows);
        Assert.Equal("ReviewRequired", row.Decision);
        Assert.Contains("SelectedCandidateBelowMinScore", row.BlockReasons);
    }

    [Fact]
    public async Task RerunAfterWrite_IsIdempotent()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await SeedSafeAliasToApprovedProviderAsync(factory);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var first = await service.ReconcileVisibleAsync(BuildRequest(dryRun: false), CancellationToken.None);
        var second = await service.ReconcileVisibleAsync(BuildRequest(dryRun: false), CancellationToken.None);
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, first.Written);
        Assert.Equal(0, second.Written);
        Assert.Equal(2, await CountApprovedApiSportsLinksAsync(dbContext, "747"));
    }

    [Fact]
    public async Task RobertoStyleCandidate_RemainsReviewRequired()
    {
        await using var factory = new SafetyTestApplicationFactory();
        await SeedVisibleAliasScenarioAsync(
            factory,
            visiblePlayerName: "Roberto Lopes",
            providerPlayerId: "422779",
            providerDisplayName: "Robert");

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISoccerPlayerIdentityResolutionService>();
        var result = await service.ReconcileVisibleAsync(BuildRequest(dryRun: false, minScore: 0.80m), CancellationToken.None);

        Assert.Equal(0, result.Written);
        var row = Assert.Single(result.Rows);
        Assert.Equal("ReviewRequired", row.Decision);
        Assert.Contains("NoAutomaticSafeRuleMatched", row.BlockReasons);
    }

    private static SoccerIdentityResolutionRequest BuildRequest(bool dryRun, decimal minScore = 0.90m)
    {
        return new SoccerIdentityResolutionRequest(
            Competition: "World Cup",
            DryRun: dryRun,
            ForceWrite: !dryRun,
            MinScore: minScore,
            MaxCandidateCount: 3,
            OnlyVisible: true,
            MaxWrites: 100);
    }

    private static async Task<Guid> SeedSafeAliasToApprovedProviderAsync(SafetyTestApplicationFactory factory)
    {
        var localPlayerId = await SeedVisibleAliasScenarioAsync(
            factory,
            visiblePlayerName: "Carlos Casemiro",
            providerPlayerId: "747",
            providerDisplayName: "Casemiro");
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sportId = await dbContext.Sports.Where(sport => sport.Code == "SOCCER").Select(sport => sport.Id).SingleAsync();
        var alreadyApprovedLocalPlayer = await AddPlayerAsync(dbContext, "Existing Casemiro Alias", await dbContext.Leagues.Where(league => league.Code == "WC2026").Select(league => league.Id).SingleAsync());
        dbContext.ProviderEntityLinks.Add(BuildApprovedLink(sportId, alreadyApprovedLocalPlayer, "747", "Casemiro", "ExistingAlias"));
        await dbContext.SaveChangesAsync();
        return localPlayerId;
    }

    private static async Task<Guid> SeedVisibleAliasScenarioAsync(
        SafetyTestApplicationFactory factory,
        string visiblePlayerName,
        string providerPlayerId,
        string providerDisplayName,
        IReadOnlyList<string>? extraProviderNames = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var sport = new Sport { Id = Guid.NewGuid(), Code = "SOCCER", Name = "Soccer" };
        var league = new League { Id = Guid.NewGuid(), SportId = sport.Id, Code = "WC2026", Name = "World Cup" };
        var platform = new Platform { Id = Guid.NewGuid(), Name = "FanDuel", Slug = "fanduel", CreatedAt = now, UpdatedAt = now };
        var propType = new PropType { Id = Guid.NewGuid(), SportId = sport.Id, Name = "Shots", StatKey = "SHOTS", ValueType = "numeric" };
        dbContext.Sports.Add(sport);
        dbContext.Leagues.Add(league);
        dbContext.Platforms.Add(platform);
        dbContext.PropTypes.Add(propType);
        await dbContext.SaveChangesAsync();
        var visiblePlayerId = await AddPlayerAsync(dbContext, visiblePlayerName, league.Id);
        var prop = new Prop
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            PlayerId = visiblePlayerId,
            PropTypeId = propType.Id,
            NormalizedKey = $"{visiblePlayerName}:shots",
            StartsAt = new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Props.Add(prop);
        dbContext.PlatformProps.Add(new PlatformProp
        {
            Id = Guid.NewGuid(),
            PropId = prop.Id,
            PlatformId = platform.Id,
            PlatformPropId = $"{visiblePlayerName}:platform",
            ExternalStableKey = "real-event-1",
            Label = $"{visiblePlayerName} Shots",
            CurrentLine = 0.5m,
            OverAvailable = true,
            UnderAvailable = true,
            Status = "active",
            ObservedAt = now,
            Metadata = BuildWorldCupMetadata("real-event-1")
        });
        await AddHistoricalPlayerAsync(dbContext, league.Id, propType.Id, providerPlayerId, providerDisplayName, 5);
        var index = 0;
        foreach (var name in extraProviderNames ?? [])
        {
            index++;
            await AddHistoricalPlayerAsync(dbContext, league.Id, propType.Id, $"{providerPlayerId}-extra-{index}", name, 5);
        }

        await dbContext.SaveChangesAsync();
        return visiblePlayerId;
    }

    private static async Task<Guid> AddPlayerAsync(ApplicationDbContext dbContext, string displayName, Guid leagueId)
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            LeagueId = leagueId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();
        return player.Id;
    }

    private static async Task AddHistoricalPlayerAsync(
        ApplicationDbContext dbContext,
        Guid leagueId,
        Guid propTypeId,
        string providerPlayerId,
        string displayName,
        int resultRows)
    {
        var playerId = await AddPlayerAsync(dbContext, displayName, leagueId);
        dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Provider = "api-sports-football",
            ProviderPlayerId = providerPlayerId,
            ProviderDisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        for (var index = 0; index < resultRows; index++)
        {
            dbContext.PropResults.Add(new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                LeagueId = leagueId,
                PropTypeId = propTypeId,
                EventDate = new DateOnly(2026, 6, 24).AddDays(-index),
                ActualResultValue = index % 3,
                ExternalSource = "api-sports-football",
                ExternalSourceIdentifier = $"api-sports-football:fixture-{index}:{providerPlayerId}:SHOTS",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static ProviderEntityLink BuildApprovedLink(Guid sportId, Guid localPlayerId, string providerPlayerId, string providerName, string source)
    {
        return new ProviderEntityLink
        {
            Id = Guid.NewGuid(),
            SportId = sportId,
            EntityType = "Player",
            CanonicalEntityId = localPlayerId,
            CanonicalEntityKind = "Player",
            CanonicalEntityName = providerName,
            ProviderKey = "api-sports-football",
            ProviderEntityId = providerPlayerId,
            ProviderEntityName = providerName,
            LinkStatus = "Approved",
            LinkSource = source,
            Confidence = 0.99m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = DateTimeOffset.UtcNow,
            ApprovedBy = "test"
        };
    }

    private static JsonDocument BuildWorldCupMetadata(string eventId)
    {
        return JsonSerializer.SerializeToDocument(new
        {
            dataProvider = "The Odds API",
            contextSchemaVersion = "the-odds-provider-context-v2",
            providerCategory = "Sportsbook",
            competition = "World Cup",
            sport = new
            {
                providerSportKey = "soccer_fifa_world_cup",
                competition = "World Cup"
            },
            eventInfo = new
            {
                providerEventId = eventId,
                competition = "World Cup"
            },
            entry = new
            {
                providerCategory = "Sportsbook",
                actionableForUser = false
            }
        });
    }

    private static Task<int> CountApprovedApiSportsLinksAsync(ApplicationDbContext dbContext, string providerPlayerId)
    {
        return dbContext.ProviderEntityLinks.CountAsync(link =>
            link.ProviderKey == "api-sports-football"
            && link.ProviderEntityId == providerPlayerId
            && link.LinkStatus == "Approved");
    }
}
