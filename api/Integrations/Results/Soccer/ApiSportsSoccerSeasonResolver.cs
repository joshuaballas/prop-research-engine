using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.Results.Soccer;

public interface IApiSportsSoccerSeasonResolver
{
    Task<ApiSportsSoccerSeasonResolutionResult> ResolveDateRangeAsync(
        ApiSportsSoccerCompetitionDescriptor competition,
        DateOnly fromDate,
        DateOnly toDate,
        bool allowLiveMetadataRefresh,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public sealed class ApiSportsSoccerSeasonResolver(
    ApplicationDbContext dbContext,
    IApiSportsFootballClient client,
    IOptions<ApiSportsOptions> options,
    ILogger<ApiSportsSoccerSeasonResolver> logger) : IApiSportsSoccerSeasonResolver
{
    public async Task<ApiSportsSoccerSeasonResolutionResult> ResolveDateRangeAsync(
        ApiSportsSoccerCompetitionDescriptor competition,
        DateOnly fromDate,
        DateOnly toDate,
        bool allowLiveMetadataRefresh,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        if (competition.IsWorldCup)
        {
            var segment = TryBuildSegmentFromDescriptor(competition, fromDate, toDate);
            return new ApiSportsSoccerSeasonResolutionResult(
                competition,
                segment is null ? [] : [segment],
                false,
                false,
                0,
                "fixed-world-cup-2026",
                null,
                segment is null ? "OutsideProviderSeasonWindow" : null,
                "World Cup 2026 is intentionally fixed and does not use automatic season rollover metadata.");
        }

        var cached = await LoadCachedSeasonsAsync(competition.LeagueId, cancellationToken);
        var cacheHours = Math.Max(1, options.Value.Football.SeasonMetadataCacheHours);
        var cacheStale = cached.Count == 0
            || cached.Max(row => row.LastRefreshedAtUtc) <= nowUtc.AddHours(-cacheHours);
        var metadataRefreshAttempted = false;
        var metadataRefreshed = false;
        var metadataApiCallsUsed = 0;
        string? metadataError = null;

        if (allowLiveMetadataRefresh && cacheStale)
        {
            metadataRefreshAttempted = true;
            metadataApiCallsUsed = 1;

            try
            {
                cached = await RefreshLeagueSeasonMetadataAsync(competition.LeagueId, nowUtc, cancellationToken);
                metadataRefreshed = cached.Count > 0;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                metadataError = $"{exception.GetType().Name}: {exception.Message}";
                logger.LogWarning(
                    exception,
                    "Failed to refresh API-Sports season metadata for league {LeagueId}; cached metadata will be used if available.",
                    competition.LeagueId);
            }
        }

        if (cached.Count == 0)
        {
            return new ApiSportsSoccerSeasonResolutionResult(
                competition,
                [],
                metadataRefreshAttempted,
                metadataRefreshed,
                metadataApiCallsUsed,
                metadataRefreshAttempted ? "live-metadata-unavailable" : "cache-missing",
                metadataError,
                "SeasonMetadataUnavailable",
                "No cached API-Sports provider-season metadata is available for this league.");
        }

        var windows = cached
            .Where(row => row.StartDate.HasValue && row.EndDate.HasValue)
            .Where(row => DateRangeOverlaps(fromDate, toDate, row.StartDate!.Value, row.EndDate!.Value))
            .OrderBy(row => row.StartDate)
            .ThenBy(row => row.Season)
            .Select(row => BuildResolvedWindow(competition, row, fromDate, toDate))
            .ToArray();

        return new ApiSportsSoccerSeasonResolutionResult(
            competition,
            windows,
            metadataRefreshAttempted,
            metadataRefreshed,
            metadataApiCallsUsed,
            metadataRefreshed ? "live-refreshed-cache" : cacheStale ? "stale-cache" : "fresh-cache",
            metadataError,
            windows.Length == 0 ? "OutsideProviderSeasonWindow" : null,
            windows.Length == 0
                ? "Requested range does not overlap any cached API-Sports provider-season window."
                : null);
    }

    private async Task<List<ApiSportsSoccerSeasonMetadata>> LoadCachedSeasonsAsync(int leagueId, CancellationToken cancellationToken)
    {
        return await dbContext.ApiSportsSoccerSeasonMetadata
            .Where(row => row.LeagueId == leagueId)
            .OrderBy(row => row.Season)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<ApiSportsSoccerSeasonMetadata>> RefreshLeagueSeasonMetadataAsync(
        int leagueId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var response = await client.GetLeaguesAsync(new ApiSportsFootballLeaguesRequest(leagueId), cancellationToken);
        var parsed = ExtractLeagueSeasonMetadata(response.Payload.RootElement, leagueId, nowUtc);
        if (parsed.Count == 0)
        {
            return [];
        }

        var existing = await dbContext.ApiSportsSoccerSeasonMetadata
            .Where(row => row.LeagueId == leagueId)
            .ToDictionaryAsync(row => row.Season, cancellationToken);

        foreach (var parsedSeason in parsed)
        {
            if (!existing.TryGetValue(parsedSeason.Season, out var row))
            {
                row = new ApiSportsSoccerSeasonMetadata
                {
                    Id = Guid.NewGuid(),
                    LeagueId = parsedSeason.LeagueId,
                    Season = parsedSeason.Season,
                    CreatedAt = nowUtc
                };
                dbContext.ApiSportsSoccerSeasonMetadata.Add(row);
                existing[row.Season] = row;
            }

            row.LeagueName = parsedSeason.LeagueName;
            row.Country = parsedSeason.Country;
            row.Type = parsedSeason.Type;
            row.StartDate = parsedSeason.StartDate;
            row.EndDate = parsedSeason.EndDate;
            row.IsCurrent = parsedSeason.IsCurrent;
            row.LastRefreshedAtUtc = nowUtc;
            row.RawSeasonJson = parsedSeason.RawSeasonJson;
            row.UpdatedAt = nowUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadCachedSeasonsAsync(leagueId, cancellationToken);
    }

    private static IReadOnlyList<ApiSportsSoccerParsedSeasonMetadata> ExtractLeagueSeasonMetadata(
        JsonElement root,
        int requestedLeagueId,
        DateTimeOffset nowUtc)
    {
        var seasons = new List<ApiSportsSoccerParsedSeasonMetadata>();
        if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Array)
        {
            return seasons;
        }

        foreach (var item in response.EnumerateArray())
        {
            var league = GetPropertyOrDefault(item, "league");
            var leagueId = TryGetInt(league, "id") ?? requestedLeagueId;
            if (leagueId != requestedLeagueId)
            {
                continue;
            }

            var leagueName = TryGetString(league, "name");
            var country = TryGetString(GetPropertyOrDefault(item, "country"), "name");
            if (!item.TryGetProperty("seasons", out var seasonArray) || seasonArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var season in seasonArray.EnumerateArray())
            {
                var year = TryGetInt(season, "year");
                if (!year.HasValue)
                {
                    continue;
                }

                seasons.Add(new ApiSportsSoccerParsedSeasonMetadata(
                    leagueId,
                    leagueName,
                    country,
                    null,
                    year.Value,
                    ParseDateOnly(TryGetString(season, "start")),
                    ParseDateOnly(TryGetString(season, "end")),
                    TryGetBool(season, "current") == true,
                    nowUtc,
                    season.GetRawText()));
            }
        }

        return seasons
            .GroupBy(row => row.Season)
            .Select(group => group.Last())
            .OrderBy(row => row.Season)
            .ToArray();
    }

    private static ApiSportsSoccerResolvedSeasonWindow? TryBuildSegmentFromDescriptor(
        ApiSportsSoccerCompetitionDescriptor competition,
        DateOnly fromDate,
        DateOnly toDate)
    {
        if (!competition.ActiveFromDate.HasValue || !competition.ActiveToDate.HasValue)
        {
            return null;
        }

        if (!DateRangeOverlaps(fromDate, toDate, competition.ActiveFromDate.Value, competition.ActiveToDate.Value))
        {
            return null;
        }

        return new ApiSportsSoccerResolvedSeasonWindow(
            competition,
            MaxDate(fromDate, competition.ActiveFromDate.Value),
            MinDate(toDate, competition.ActiveToDate.Value),
            competition.ActiveFromDate,
            competition.ActiveToDate,
            "fixed-world-cup-2026");
    }

    private static ApiSportsSoccerResolvedSeasonWindow BuildResolvedWindow(
        ApiSportsSoccerCompetitionDescriptor competition,
        ApiSportsSoccerSeasonMetadata metadata,
        DateOnly fromDate,
        DateOnly toDate)
    {
        var start = metadata.StartDate!.Value;
        var end = metadata.EndDate!.Value;
        var resolvedCompetition = competition with
        {
            Season = metadata.Season,
            ActiveFromDate = start,
            ActiveToDate = end,
            BackfillFromDate = start,
            BackfillToDate = end,
            LeagueCode = BuildHistoricalLeagueCode(competition.Key, metadata.Season)
        };

        return new ApiSportsSoccerResolvedSeasonWindow(
            resolvedCompetition,
            MaxDate(fromDate, start),
            MinDate(toDate, end),
            start,
            end,
            metadata.IsCurrent ? "api-sports-current-season" : "api-sports-season-metadata");
    }

    private static bool DateRangeOverlaps(DateOnly fromDate, DateOnly toDate, DateOnly start, DateOnly end)
        => fromDate <= end && toDate >= start;

    private static DateOnly MaxDate(DateOnly left, DateOnly right) => left > right ? left : right;

    private static DateOnly MinDate(DateOnly left, DateOnly right) => left < right ? left : right;

    private static string BuildHistoricalLeagueCode(string key, int season)
        => $"API_SPORTS_{NormalizeLeagueCodeToken(key)}_{season}";

    private static string NormalizeLeagueCodeToken(string value)
    {
        var token = new string(value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')
            .ToArray());
        while (token.Contains("__", StringComparison.Ordinal))
        {
            token = token.Replace("__", "_", StringComparison.Ordinal);
        }

        return token.Trim('_');
    }

    private static JsonElement GetPropertyOrDefault(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value
            : default;

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? TryGetInt(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static bool? TryGetBool(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    private static DateOnly? ParseDateOnly(string? value)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private sealed record ApiSportsSoccerParsedSeasonMetadata(
        int LeagueId,
        string? LeagueName,
        string? Country,
        string? Type,
        int Season,
        DateOnly? StartDate,
        DateOnly? EndDate,
        bool IsCurrent,
        DateTimeOffset LastRefreshedAtUtc,
        string RawSeasonJson);
}

public sealed record ApiSportsSoccerSeasonResolutionResult(
    ApiSportsSoccerCompetitionDescriptor RequestedCompetition,
    IReadOnlyList<ApiSportsSoccerResolvedSeasonWindow> SeasonWindows,
    bool MetadataRefreshAttempted,
    bool MetadataRefreshed,
    int MetadataApiCallsUsed,
    string MetadataSource,
    string? MetadataError,
    string? SkippedReason,
    string? SkippedMessage);

public sealed record ApiSportsSoccerResolvedSeasonWindow(
    ApiSportsSoccerCompetitionDescriptor Competition,
    DateOnly FromDate,
    DateOnly ToDate,
    DateOnly? ProviderSeasonStartDate,
    DateOnly? ProviderSeasonEndDate,
    string ResolutionSource);
