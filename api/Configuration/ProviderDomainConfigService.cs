using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Configuration;

public interface IProviderDomainConfigService
{
    Task<ProviderDomainConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiSportsSoccerCompetitionDescriptor>> GetApiSportsSoccerCompetitionsAsync(
        ApiSportsOptions fallbackOptions,
        bool enabledOnly,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TheOddsScheduledRefreshTargetOptions>> GetTheOddsScheduledRefreshTargetsAsync(
        TheOddsScheduledRefreshOptions fallbackOptions,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetTheOddsBookmakerKeysAsync(
        TheOddsScheduledRefreshTargetOptions target,
        TheOddsIngestionWorkerOptions fallbackOptions,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetTheOddsMarketKeysAsync(
        TheOddsScheduledRefreshTargetOptions target,
        TheOddsApiOptions apiFallbackOptions,
        TheOddsIngestionWorkerOptions workerFallbackOptions,
        CancellationToken cancellationToken);
    void ClearCache();
}

public sealed class ProviderDomainConfigService(
    ApplicationDbContext dbContext,
    IMemoryCache cache,
    ILogger<ProviderDomainConfigService> logger) : IProviderDomainConfigService
{
    public const string TheOddsProviderKey = "THE_ODDS_API";
    public const string ApiSportsFootballProviderKey = "API_SPORTS_FOOTBALL";
    public const string WorldCupCompetitionKey = "WORLD_CUP_2026";

    private const string CacheKey = "provider-domain-config-snapshot";
    private const string DefaultExpandedSportsbookBookmakers =
        "draftkings,fanduel,betmgm,espnbet,betrivers,fanatics,williamhill_us,betonlineag,bovada,lowvig,mybookieag";
    private const string DefaultDfsBookmakers = "prizepicks,underdog,pick6,betr_us_dfs";
    private const string DefaultSoccerWorldCupMarkets = "player_goals,player_assists,player_shots,player_shots_on_target";

    public async Task<ProviderDomainConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out ProviderDomainConfigSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var competitions = await dbContext.ProviderCompetitions
                .AsNoTracking()
                .OrderBy(row => row.ProviderKey)
                .ThenBy(row => row.Sport)
                .ThenBy(row => row.Priority)
                .ThenBy(row => row.CompetitionKey)
                .ToArrayAsync(cancellationToken);
            var markets = await dbContext.ProviderMarkets
                .AsNoTracking()
                .OrderBy(row => row.ProviderKey)
                .ThenBy(row => row.Sport)
                .ThenBy(row => row.Priority)
                .ThenBy(row => row.MarketKey)
                .ToArrayAsync(cancellationToken);
            var bookmakers = await dbContext.ProviderBookmakers
                .AsNoTracking()
                .OrderBy(row => row.ProviderKey)
                .ThenBy(row => row.ProviderCategory)
                .ThenBy(row => row.Priority)
                .ThenBy(row => row.BookmakerKey)
                .ToArrayAsync(cancellationToken);
            var refreshTargets = await dbContext.ProviderRefreshTargets
                .AsNoTracking()
                .OrderBy(row => row.ProviderKey)
                .ThenBy(row => row.Priority)
                .ThenBy(row => row.TargetKey)
                .ToArrayAsync(cancellationToken);

            var snapshot = new ProviderDomainConfigSnapshot(
                generatedAtUtc,
                loadSucceeded: true,
                loadError: null,
                competitions,
                markets,
                bookmakers,
                refreshTargets,
                appsettingsFallbackCount: 0);
            cache.Set(CacheKey, snapshot, TimeSpan.FromSeconds(60));
            return snapshot;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Provider domain config DB read failed; runtime will use appsettings fallback where available.");
            return new ProviderDomainConfigSnapshot(
                DateTimeOffset.UtcNow,
                loadSucceeded: false,
                loadError: exception.Message,
                providerCompetitions: [],
                providerMarkets: [],
                providerBookmakers: [],
                providerRefreshTargets: [],
                appsettingsFallbackCount: 4);
        }
    }

    public async Task<IReadOnlyList<ApiSportsSoccerCompetitionDescriptor>> GetApiSportsSoccerCompetitionsAsync(
        ApiSportsOptions fallbackOptions,
        bool enabledOnly,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var rows = snapshot.ProviderCompetitions
            .Where(row => string.Equals(row.ProviderKey, ApiSportsFootballProviderKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
                && row.SupportsResultsImport
                && (!enabledOnly || row.IsEnabled))
            .OrderBy(row => row.Priority)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rows.Length > 0)
        {
            return rows
                .Select(ToApiSportsCompetitionDescriptor)
                .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        snapshot.RegisterAppsettingsFallback();
        return BuildFallbackApiSportsSoccerCompetitions(fallbackOptions, enabledOnly);
    }

    public async Task<IReadOnlyList<TheOddsScheduledRefreshTargetOptions>> GetTheOddsScheduledRefreshTargetsAsync(
        TheOddsScheduledRefreshOptions fallbackOptions,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var rows = snapshot.ProviderRefreshTargets
            .Where(row => string.Equals(row.ProviderKey, TheOddsProviderKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.Priority)
            .ThenBy(row => row.TargetKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rows.Length > 0)
        {
            return rows.Select(ToTheOddsScheduledRefreshTargetOptions).ToArray();
        }

        snapshot.RegisterAppsettingsFallback();
        return fallbackOptions.GetConfiguredTargets();
    }

    public async Task<IReadOnlyList<string>> GetTheOddsBookmakerKeysAsync(
        TheOddsScheduledRefreshTargetOptions target,
        TheOddsIngestionWorkerOptions fallbackOptions,
        CancellationToken cancellationToken)
    {
        var explicitBookmakers = SplitCsv(target.Bookmakers);
        if (explicitBookmakers.Length > 0)
        {
            return explicitBookmakers;
        }

        var snapshot = await GetSnapshotAsync(cancellationToken);
        var providerCategory = NormalizeProviderCategory(target.ProviderCategory);
        var region = ResolveTargetRegion(target, fallbackOptions);
        var bookmakerRows = snapshot.ProviderBookmakers
            .Where(row => string.Equals(row.ProviderKey, TheOddsProviderKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.ProviderCategory, providerCategory, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.Region, region, StringComparison.OrdinalIgnoreCase)
                && row.IsEnabled)
            .ToArray();

        if (bookmakerRows.Length > 0)
        {
            var preset = ResolveMlbPersistedBookmakerPreset(target.BookmakerPreset);
            var requireExpanded = string.Equals(providerCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
                && preset is "expanded" or "allSportsbooks" or "all";
            var selected = bookmakerRows
                .Where(row => requireExpanded ? row.IsExpandedCoverage : row.IsDefault)
                .OrderBy(row => row.Priority)
                .ThenBy(row => row.BookmakerKey, StringComparer.OrdinalIgnoreCase)
                .Select(row => row.BookmakerKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selected.Length > 0)
            {
                return selected;
            }
        }

        snapshot.RegisterAppsettingsFallback();
        return ResolveFallbackBookmakerKeys(target, fallbackOptions);
    }

    public async Task<IReadOnlyList<string>> GetTheOddsMarketKeysAsync(
        TheOddsScheduledRefreshTargetOptions target,
        TheOddsApiOptions apiFallbackOptions,
        TheOddsIngestionWorkerOptions workerFallbackOptions,
        CancellationToken cancellationToken)
    {
        var explicitMarkets = SplitCsv(target.Markets);
        if (explicitMarkets.Length > 0)
        {
            return explicitMarkets;
        }

        var snapshot = await GetSnapshotAsync(cancellationToken);
        var competitionKey = NormalizeCompetitionKey(target.Sport, target.Competition);
        var providerCategory = NormalizeProviderCategory(target.ProviderCategory);
        var rows = snapshot.ProviderMarkets
            .Where(row => string.Equals(row.ProviderKey, TheOddsProviderKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.Sport, target.Sport, StringComparison.OrdinalIgnoreCase)
                && row.IsEnabled
                && (string.IsNullOrWhiteSpace(row.CompetitionKey)
                    || string.Equals(row.CompetitionKey, competitionKey, StringComparison.OrdinalIgnoreCase))
                && (string.Equals(row.ProviderCategory, "Any", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.ProviderCategory, providerCategory, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => row.Priority)
            .ThenBy(row => row.MarketKey, StringComparer.OrdinalIgnoreCase)
            .Select(row => row.MarketKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rows.Length > 0)
        {
            return rows;
        }

        snapshot.RegisterAppsettingsFallback();
        return ResolveFallbackMarketKeys(target, apiFallbackOptions, workerFallbackOptions);
    }

    public void ClearCache() => cache.Remove(CacheKey);

    public static string NormalizeCompetitionKey(string sport, string? competition)
    {
        if (string.Equals(sport, "MLB", StringComparison.OrdinalIgnoreCase))
        {
            return "MLB";
        }

        var value = string.IsNullOrWhiteSpace(competition) ? sport : competition.Trim();
        if (string.Equals(value, "World Cup", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "WORLD_CUP", StringComparison.OrdinalIgnoreCase))
        {
            return WorldCupCompetitionKey;
        }

        return value.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
    }

    internal static ApiSportsSoccerCompetitionDescriptor ToApiSportsCompetitionDescriptor(ProviderCompetition row)
    {
        var isWorldCup = string.Equals(row.CompetitionKey, WorldCupCompetitionKey, StringComparison.OrdinalIgnoreCase);
        var season = row.IsFixedSeason ? row.FixedSeason.GetValueOrDefault(0) : 0;
        return new ApiSportsSoccerCompetitionDescriptor(
            row.CompetitionKey,
            row.DisplayName,
            row.DisplayName,
            row.ProviderLeagueId ?? 0,
            season,
            row.IsEnabled,
            row.Country,
            row.CompetitionType,
            isWorldCup ? new DateOnly(2026, 6, 11) : null,
            isWorldCup ? new DateOnly(2026, 7, 19) : null,
            isWorldCup ? new DateOnly(2026, 6, 11) : null,
            isWorldCup ? new DateOnly(2026, 7, 19) : null,
            isWorldCup ? "WORLD_CUP" : BuildHistoricalLeagueCode(row.CompetitionKey, season),
            isWorldCup ? "WorldCupResultImport" : "HistoricalResultImport",
            isWorldCup);
    }

    internal static IReadOnlyList<ApiSportsSoccerCompetitionDescriptor> BuildFallbackApiSportsSoccerCompetitions(
        ApiSportsOptions options,
        bool enabledOnly)
    {
        var worldCup = new ApiSportsSoccerCompetitionDescriptor(
            "world-cup-2026",
            "World Cup",
            "World Cup",
            1,
            2026,
            true,
            "International",
            "Cup",
            new DateOnly(2026, 6, 11),
            new DateOnly(2026, 7, 19),
            new DateOnly(2026, 6, 11),
            new DateOnly(2026, 7, 19),
            "WORLD_CUP",
            "WorldCupResultImport",
            true);
        var historical = (options.Football.HistoricalCompetitions ?? [])
            .Where(row => !string.IsNullOrWhiteSpace(row.Key)
                && !string.IsNullOrWhiteSpace(row.DisplayName)
                && row.LeagueId > 0
                && (!enabledOnly || row.Enabled))
            .Select(row => new ApiSportsSoccerCompetitionDescriptor(
                row.Key.Trim(),
                row.DisplayName.Trim(),
                row.DisplayName.Trim(),
                row.LeagueId,
                row.Season ?? 0,
                row.Enabled,
                string.IsNullOrWhiteSpace(row.Country) ? null : row.Country.Trim(),
                string.IsNullOrWhiteSpace(row.Type) ? null : row.Type.Trim(),
                row.ActiveFromDate ?? row.BackfillFromDate,
                row.ActiveToDate ?? row.BackfillToDate,
                row.BackfillFromDate ?? row.ActiveFromDate,
                row.BackfillToDate ?? row.ActiveToDate,
                BuildHistoricalLeagueCode(row.Key.Trim(), row.Season ?? 0),
                "HistoricalResultImport",
                false))
            .ToArray();

        return new[] { worldCup }
            .Concat(historical)
            .Where(row => !enabledOnly || row.Enabled)
            .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    internal static string BuildHistoricalLeagueCode(string key, int season)
    {
        var normalizedKey = key.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
        return season > 0 ? $"API_SPORTS_{normalizedKey}_{season}" : $"API_SPORTS_{normalizedKey}";
    }

    internal static IReadOnlyList<ApiSportsSoccerCompetitionDescriptor> DefaultApiSportsHistoricalCompetitions()
        =>
        [
            HistoricalCompetition("EPL", "EPL", 39, "England", "League"),
            HistoricalCompetition("LA_LIGA", "La Liga", 140, "Spain", "League"),
            HistoricalCompetition("BUNDESLIGA", "Bundesliga", 78, "Germany", "League"),
            HistoricalCompetition("SERIE_A", "Serie A", 135, "Italy", "League"),
            HistoricalCompetition("LIGUE_1", "Ligue 1", 61, "France", "League"),
            HistoricalCompetition("MLS", "MLS", 253, "USA", "League"),
            HistoricalCompetition("CHAMPIONS_LEAGUE", "Champions League", 2, "Europe", "Cup")
        ];

    private static ApiSportsSoccerCompetitionDescriptor HistoricalCompetition(
        string key,
        string displayName,
        int leagueId,
        string country,
        string type)
        => new(
            key,
            displayName,
            displayName,
            leagueId,
            0,
            true,
            country,
            type,
            null,
            null,
            null,
            null,
            BuildHistoricalLeagueCode(key, 0),
            "HistoricalResultImport",
            false);

    private static TheOddsScheduledRefreshTargetOptions ToTheOddsScheduledRefreshTargetOptions(ProviderRefreshTarget row)
        => new()
        {
            Enabled = row.IsEnabled,
            Key = row.TargetKey,
            Sport = row.Sport,
            Competition = string.IsNullOrWhiteSpace(row.CompetitionDisplayName)
                ? row.CompetitionKey
                : row.CompetitionDisplayName,
            ProviderCategory = row.ProviderCategory,
            BookmakerPreset = row.BookmakerPreset,
            ProviderSportKey = row.ProviderSportKey,
            Regions = row.Regions,
            MaxEvents = row.MaxEvents,
            EstimatedApiCalls = row.EstimatedApiCalls,
            SupportsResults = row.SupportsResults,
            SupportsHistory = row.SupportsHistory,
            DisabledReason = row.DisabledReason,
            Notes = row.Notes
        };

    private static string NormalizeProviderCategory(string? value)
        => string.Equals(value, "DFS", StringComparison.OrdinalIgnoreCase) ? "DFS" : "Sportsbook";

    private static string ResolveTargetRegion(TheOddsScheduledRefreshTargetOptions target, TheOddsIngestionWorkerOptions fallbackOptions)
    {
        if (!string.IsNullOrWhiteSpace(target.Regions))
        {
            return SplitCsv(target.Regions).FirstOrDefault() ?? "us";
        }

        return string.Equals(target.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)
            ? NullIfWhiteSpace(fallbackOptions.DfsRegion) ?? "us_dfs"
            : "us";
    }

    private static IReadOnlyList<string> ResolveFallbackBookmakerKeys(
        TheOddsScheduledRefreshTargetOptions target,
        TheOddsIngestionWorkerOptions fallbackOptions)
    {
        if (string.Equals(target.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase))
        {
            return SplitCsv(NullIfWhiteSpace(fallbackOptions.DfsBookmakers) ?? DefaultDfsBookmakers);
        }

        if (!string.Equals(target.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var preset = ResolveMlbPersistedBookmakerPreset(target.BookmakerPreset);
        var expanded = SplitCsv(NullIfWhiteSpace(fallbackOptions.ExpandedBookmakers) ?? DefaultExpandedSportsbookBookmakers);
        var conservative = SplitCsv(NullIfWhiteSpace(fallbackOptions.Bookmakers));
        return preset is "expanded" or "allSportsbooks" or "all"
            ? expanded
            : conservative.Length > 0 ? conservative : expanded;
    }

    private static IReadOnlyList<string> ResolveFallbackMarketKeys(
        TheOddsScheduledRefreshTargetOptions target,
        TheOddsApiOptions apiFallbackOptions,
        TheOddsIngestionWorkerOptions workerFallbackOptions)
    {
        if (string.Equals(target.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Competition, "World Cup", StringComparison.OrdinalIgnoreCase))
        {
            return SplitCsv(NullIfWhiteSpace(apiFallbackOptions.SoccerWorldCupMarkets) ?? DefaultSoccerWorldCupMarkets);
        }

        return SplitCsv(NullIfWhiteSpace(workerFallbackOptions.Markets) ?? NullIfWhiteSpace(apiFallbackOptions.Markets));
    }

    private static string ResolveMlbPersistedBookmakerPreset(string? bookmakerPreset)
        => (bookmakerPreset ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "expanded" => "expanded",
            "dfsonly" or "dfs-only" or "dfs_only" => "dfsOnly",
            "dfsall" or "dfs-all" or "dfs_all" => "dfsAll",
            "sportsbookonly" or "sportsbook-only" or "sportsbook_only" => "sportsbookOnly",
            "sportsbookexpanded" or "sportsbook-expanded" or "sportsbook_expanded" => "allSportsbooks",
            "allsportsbooks" or "all-sportsbooks" or "all_sportsbooks" => "allSportsbooks",
            "all" => "all",
            _ => "default"
        };

    private static string[] SplitCsv(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ProviderDomainConfigSnapshot(
    DateTimeOffset generatedAtUtc,
    bool loadSucceeded,
    string? loadError,
    IReadOnlyList<ProviderCompetition> providerCompetitions,
    IReadOnlyList<ProviderMarket> providerMarkets,
    IReadOnlyList<ProviderBookmaker> providerBookmakers,
    IReadOnlyList<ProviderRefreshTarget> providerRefreshTargets,
    int appsettingsFallbackCount)
{
    private int fallbackCount = appsettingsFallbackCount;

    public DateTimeOffset GeneratedAtUtc { get; } = generatedAtUtc;
    public bool LoadSucceeded { get; } = loadSucceeded;
    public string? LoadError { get; } = loadError;
    public IReadOnlyList<ProviderCompetition> ProviderCompetitions { get; } = providerCompetitions;
    public IReadOnlyList<ProviderMarket> ProviderMarkets { get; } = providerMarkets;
    public IReadOnlyList<ProviderBookmaker> ProviderBookmakers { get; } = providerBookmakers;
    public IReadOnlyList<ProviderRefreshTarget> ProviderRefreshTargets { get; } = providerRefreshTargets;
    public int AppsettingsFallbackCount => fallbackCount;

    internal void RegisterAppsettingsFallback() => Interlocked.Increment(ref fallbackCount);
}

public sealed record ProviderDomainConfigSeedResult(
    int CompetitionsInserted,
    int MarketsInserted,
    int BookmakersInserted,
    int RefreshTargetsInserted,
    int MetadataUpdated,
    DateTimeOffset SeededAtUtc);

public static class ProviderDomainConfigSeeder
{
    private const string TheOddsProviderKey = ProviderDomainConfigService.TheOddsProviderKey;
    private const string ApiSportsFootballProviderKey = ProviderDomainConfigService.ApiSportsFootballProviderKey;

    public static async Task<ProviderDomainConfigSeedResult> SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var scheduledOptions = scope.ServiceProvider.GetRequiredService<IOptions<TheOddsScheduledRefreshOptions>>().Value;
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var now = DateTimeOffset.UtcNow;
        var metadataUpdated = 0;

        var competitionSeedRows = BuildCompetitionSeeds().ToArray();
        var competitionsInserted = 0;
        foreach (var seed in competitionSeedRows)
        {
            var existing = await dbContext.ProviderCompetitions.SingleOrDefaultAsync(
                row => row.ProviderKey == seed.ProviderKey
                    && row.Sport == seed.Sport
                    && row.CompetitionKey == seed.CompetitionKey);
            if (existing is null)
            {
                dbContext.ProviderCompetitions.Add(seed);
                competitionsInserted++;
                continue;
            }

            metadataUpdated += UpdateCompetitionMetadata(existing, seed, now);
        }

        var marketSeedRows = BuildMarketSeeds().ToArray();
        var marketsInserted = 0;
        foreach (var seed in marketSeedRows)
        {
            var existing = await dbContext.ProviderMarkets.SingleOrDefaultAsync(
                row => row.ProviderKey == seed.ProviderKey
                    && row.Sport == seed.Sport
                    && row.CompetitionKey == seed.CompetitionKey
                    && row.ProviderCategory == seed.ProviderCategory
                    && row.MarketKey == seed.MarketKey);
            if (existing is null)
            {
                dbContext.ProviderMarkets.Add(seed);
                marketsInserted++;
                continue;
            }

            metadataUpdated += UpdateMarketMetadata(existing, seed, now);
        }

        var bookmakerSeedRows = BuildBookmakerSeeds().ToArray();
        var bookmakersInserted = 0;
        foreach (var seed in bookmakerSeedRows)
        {
            var existing = await dbContext.ProviderBookmakers.SingleOrDefaultAsync(
                row => row.ProviderKey == seed.ProviderKey
                    && row.BookmakerKey == seed.BookmakerKey
                    && row.ProviderCategory == seed.ProviderCategory
                    && row.Region == seed.Region);
            if (existing is null)
            {
                dbContext.ProviderBookmakers.Add(seed);
                bookmakersInserted++;
                continue;
            }

            metadataUpdated += UpdateBookmakerMetadata(existing, seed, now);
        }

        var targetSeedRows = BuildRefreshTargetSeeds(scheduledOptions, environment).ToArray();
        var refreshTargetsInserted = 0;
        foreach (var seed in targetSeedRows)
        {
            var existing = await dbContext.ProviderRefreshTargets.SingleOrDefaultAsync(
                row => row.ProviderKey == seed.ProviderKey && row.TargetKey == seed.TargetKey);
            if (existing is null)
            {
                dbContext.ProviderRefreshTargets.Add(seed);
                refreshTargetsInserted++;
                continue;
            }

            metadataUpdated += UpdateRefreshTargetMetadata(existing, seed, now);
        }

        await dbContext.SaveChangesAsync();

        var domainConfig = scope.ServiceProvider.GetService<IProviderDomainConfigService>();
        domainConfig?.ClearCache();

        return new ProviderDomainConfigSeedResult(
            competitionsInserted,
            marketsInserted,
            bookmakersInserted,
            refreshTargetsInserted,
            metadataUpdated,
            now);
    }

    private static IEnumerable<ProviderCompetition> BuildCompetitionSeeds()
    {
        var now = DateTimeOffset.UtcNow;
        yield return new ProviderCompetition
        {
            Id = Guid.NewGuid(),
            ProviderKey = ApiSportsFootballProviderKey,
            Sport = "SOCCER",
            CompetitionKey = ProviderDomainConfigService.WorldCupCompetitionKey,
            DisplayName = "World Cup",
            Country = "International",
            CompetitionType = "Cup",
            ProviderLeagueId = 1,
            IsEnabled = true,
            SupportsResultsImport = true,
            SupportsOddsImport = false,
            SupportsSeasonMetadata = false,
            IsFixedSeason = true,
            FixedSeason = 2026,
            Priority = 10,
            Notes = "Fixed API-Sports Football World Cup result/stat import competition.",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var priority = 100;
        foreach (var competition in ProviderDomainConfigService.DefaultApiSportsHistoricalCompetitions().OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            yield return new ProviderCompetition
            {
                Id = Guid.NewGuid(),
                ProviderKey = ApiSportsFootballProviderKey,
                Sport = "SOCCER",
                CompetitionKey = NormalizeKey(competition.Key),
                DisplayName = competition.DisplayName,
                Country = competition.Country,
                CompetitionType = competition.Type,
                ProviderLeagueId = competition.LeagueId,
                IsEnabled = competition.Enabled,
                SupportsResultsImport = true,
                SupportsOddsImport = false,
                SupportsSeasonMetadata = true,
                IsFixedSeason = false,
                FixedSeason = null,
                Priority = priority++,
                Notes = "Recurring API-Sports Football competition. Season windows are resolved from provider metadata.",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }
    }

    private static IEnumerable<ProviderMarket> BuildMarketSeeds()
    {
        var now = DateTimeOffset.UtcNow;
        var apiDefaults = new TheOddsApiOptions();
        var workerDefaults = new TheOddsIngestionWorkerOptions();
        var mlbMarkets = SplitCsv(apiDefaults.Markets)
            .Concat(SplitCsv(workerDefaults.Markets))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var priority = 100;
        foreach (var market in mlbMarkets)
        {
            yield return new ProviderMarket
            {
                Id = Guid.NewGuid(),
                ProviderKey = TheOddsProviderKey,
                Sport = "MLB",
                CompetitionKey = "MLB",
                ProviderCategory = "Any",
                MarketKey = market,
                DisplayName = HumanizeMarketKey(market),
                InternalPropType = MapTheOddsMarketToPropType(market),
                IsEnabled = true,
                IsDefault = true,
                SupportsOddsImport = true,
                SupportsLineShopping = true,
                SupportsTopPicks = true,
                Priority = priority++,
                Notes = "Seeded from The Odds MLB market configuration.",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }

        priority = 100;
        foreach (var market in SplitCsv("player_goals,player_assists,player_shots,player_shots_on_target"))
        {
            yield return new ProviderMarket
            {
                Id = Guid.NewGuid(),
                ProviderKey = TheOddsProviderKey,
                Sport = "SOCCER",
                CompetitionKey = ProviderDomainConfigService.WorldCupCompetitionKey,
                ProviderCategory = "Sportsbook",
                MarketKey = market,
                DisplayName = HumanizeMarketKey(market),
                InternalPropType = MapTheOddsMarketToPropType(market),
                IsEnabled = true,
                IsDefault = true,
                SupportsOddsImport = true,
                SupportsLineShopping = true,
                SupportsTopPicks = true,
                Priority = priority++,
                Notes = "Seeded Soccer World Cup sportsbook market key.",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }
    }

    private static IEnumerable<ProviderBookmaker> BuildBookmakerSeeds()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new TheOddsIngestionWorkerOptions();
        var defaultSportsbookKeys = SplitCsv(options.Bookmakers);
        var expandedSportsbookKeys = SplitCsv(string.IsNullOrWhiteSpace(options.ExpandedBookmakers)
            ? "draftkings,fanduel,betmgm,espnbet,betrivers,fanatics,williamhill_us,betonlineag,bovada,lowvig,mybookieag"
            : options.ExpandedBookmakers);
        var priority = 100;
        foreach (var bookmaker in defaultSportsbookKeys.Concat(expandedSportsbookKeys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new ProviderBookmaker
            {
                Id = Guid.NewGuid(),
                ProviderKey = TheOddsProviderKey,
                BookmakerKey = bookmaker,
                DisplayName = ToBookmakerDisplayName(bookmaker),
                ProviderCategory = "Sportsbook",
                Region = "us",
                IsEnabled = true,
                IsDefault = defaultSportsbookKeys.Contains(bookmaker, StringComparer.OrdinalIgnoreCase),
                IsExpandedCoverage = expandedSportsbookKeys.Contains(bookmaker, StringComparer.OrdinalIgnoreCase),
                SupportsLineShopping = true,
                SupportsEntryBuilder = false,
                Priority = priority++,
                Notes = "Seeded The Odds sportsbook bookmaker key.",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }

        priority = 100;
        var dfsBookmakerKeys = SplitCsv(string.IsNullOrWhiteSpace(options.DfsBookmakers)
            ? "prizepicks,underdog,pick6,betr_us_dfs"
            : options.DfsBookmakers);
        foreach (var bookmaker in dfsBookmakerKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new ProviderBookmaker
            {
                Id = Guid.NewGuid(),
                ProviderKey = TheOddsProviderKey,
                BookmakerKey = bookmaker,
                DisplayName = ToBookmakerDisplayName(bookmaker),
                ProviderCategory = "DFS",
                Region = string.IsNullOrWhiteSpace(options.DfsRegion) ? "us_dfs" : options.DfsRegion,
                IsEnabled = true,
                IsDefault = true,
                IsExpandedCoverage = false,
                SupportsLineShopping = true,
                SupportsEntryBuilder = true,
                Priority = priority++,
                Notes = "Seeded The Odds DFS bookmaker key.",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }
    }

    private static IEnumerable<ProviderRefreshTarget> BuildRefreshTargetSeeds(
        TheOddsScheduledRefreshOptions options,
        IHostEnvironment environment)
    {
        var now = DateTimeOffset.UtcNow;
        var priority = 100;
        var targets = options.Targets.Count > 0
            ? options.GetConfiguredTargets()
            : TheOddsScheduledRefreshOptions.CreateDefaultTargets(
                options.BookmakerPreset,
                options.GetFallbackEstimatedApiCalls(),
                developmentEnabled: environment.IsDevelopment());
        foreach (var target in targets)
        {
            var competitionKey = ProviderDomainConfigService.NormalizeCompetitionKey(target.Sport, target.Competition);
            yield return new ProviderRefreshTarget
            {
                Id = Guid.NewGuid(),
                ProviderKey = TheOddsProviderKey,
                TargetKey = target.ResolveKey(),
                Sport = target.Sport,
                CompetitionKey = competitionKey,
                CompetitionDisplayName = target.Competition,
                ProviderCategory = target.ProviderCategory,
                BookmakerPreset = target.BookmakerPreset,
                ProviderSportKey = target.ProviderSportKey,
                Regions = target.Regions,
                MaxEvents = target.MaxEvents,
                EstimatedApiCalls = target.EstimatedApiCalls,
                SupportsResults = target.SupportsResults,
                SupportsHistory = target.SupportsHistory,
                IsEnabled = target.Enabled,
                DisabledReason = target.DisabledReason,
                Notes = target.Notes,
                Priority = priority++,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }
    }

    private static int UpdateCompetitionMetadata(ProviderCompetition existing, ProviderCompetition seed, DateTimeOffset now)
    {
        existing.DisplayName = seed.DisplayName;
        existing.Country = seed.Country;
        existing.CompetitionType = seed.CompetitionType;
        existing.ProviderLeagueId = seed.ProviderLeagueId;
        existing.ProviderSportKey = seed.ProviderSportKey;
        existing.SupportsResultsImport = seed.SupportsResultsImport;
        existing.SupportsOddsImport = seed.SupportsOddsImport;
        existing.SupportsSeasonMetadata = seed.SupportsSeasonMetadata;
        existing.IsFixedSeason = seed.IsFixedSeason;
        existing.FixedSeason = seed.FixedSeason;
        existing.Notes = seed.Notes;
        existing.UpdatedAtUtc = now;
        return 1;
    }

    private static int UpdateMarketMetadata(ProviderMarket existing, ProviderMarket seed, DateTimeOffset now)
    {
        existing.DisplayName = seed.DisplayName;
        existing.InternalPropType = seed.InternalPropType;
        existing.IsDefault = seed.IsDefault;
        existing.SupportsOddsImport = seed.SupportsOddsImport;
        existing.SupportsLineShopping = seed.SupportsLineShopping;
        existing.SupportsTopPicks = seed.SupportsTopPicks;
        existing.Notes = seed.Notes;
        existing.UpdatedAtUtc = now;
        return 1;
    }

    private static int UpdateBookmakerMetadata(ProviderBookmaker existing, ProviderBookmaker seed, DateTimeOffset now)
    {
        existing.DisplayName = seed.DisplayName;
        existing.IsDefault = seed.IsDefault;
        existing.IsExpandedCoverage = seed.IsExpandedCoverage;
        existing.SupportsLineShopping = seed.SupportsLineShopping;
        existing.SupportsEntryBuilder = seed.SupportsEntryBuilder;
        existing.Notes = seed.Notes;
        existing.UpdatedAtUtc = now;
        return 1;
    }

    private static int UpdateRefreshTargetMetadata(ProviderRefreshTarget existing, ProviderRefreshTarget seed, DateTimeOffset now)
    {
        existing.Sport = seed.Sport;
        existing.CompetitionKey = seed.CompetitionKey;
        existing.CompetitionDisplayName = seed.CompetitionDisplayName;
        existing.ProviderCategory = seed.ProviderCategory;
        existing.BookmakerPreset = seed.BookmakerPreset;
        existing.ProviderSportKey = seed.ProviderSportKey;
        existing.Regions = seed.Regions;
        existing.MaxEvents = seed.MaxEvents;
        existing.EstimatedApiCalls = seed.EstimatedApiCalls;
        existing.SupportsResults = seed.SupportsResults;
        existing.SupportsHistory = seed.SupportsHistory;
        existing.DisabledReason = seed.DisabledReason;
        existing.Notes = seed.Notes;
        existing.UpdatedAtUtc = now;
        return 1;
    }

    private static string NormalizeKey(string value)
        => value.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');

    private static string[] SplitCsv(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string HumanizeMarketKey(string value)
        => string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string? MapTheOddsMarketToPropType(string value)
        => value.ToLowerInvariant() switch
        {
            "batter_hits" => "Hits",
            "batter_runs_scored" => "Runs",
            "batter_rbis" => "RBIs",
            "batter_home_runs" => "Home Runs",
            "batter_total_bases" => "Total Bases",
            "batter_stolen_bases" => "Stolen Bases",
            "batter_strikeouts" => "Batter Strikeouts",
            "pitcher_strikeouts" => "Pitcher Strikeouts",
            "batter_walks" => "Walks",
            "batter_doubles" => "Doubles",
            "batter_singles" => "Singles",
            "batter_hits_runs_rbis" => "Hits + Runs + RBIs",
            "pitcher_walks" => "Pitcher Walks",
            "pitcher_hits_allowed" => "Pitcher Hits Allowed",
            "pitcher_earned_runs" => "Pitcher Earned Runs",
            "pitcher_outs" => "Pitcher Outs",
            "player_goals" => "Goals",
            "player_assists" => "Assists",
            "player_shots" => "Shots",
            "player_shots_on_target" => "Shots On Target",
            _ => null
        };

    private static string ToBookmakerDisplayName(string key)
        => key.ToLowerInvariant() switch
        {
            "draftkings" => "DraftKings",
            "fanduel" => "FanDuel",
            "betmgm" => "BetMGM",
            "espnbet" => "ESPN BET",
            "betrivers" => "BetRivers",
            "fanatics" => "Fanatics",
            "williamhill_us" => "Caesars",
            "betonlineag" => "BetOnline",
            "bovada" => "Bovada",
            "lowvig" => "LowVig",
            "mybookieag" => "MyBookie",
            "prizepicks" => "PrizePicks",
            "underdog" => "Underdog",
            "pick6" => "DraftKings Pick6",
            "betr_us_dfs" => "Betr Picks",
            _ => key
        };
}
