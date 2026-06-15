using System.Text;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;

namespace SportsBetting.Api.Endpoints;

public static class MarketComparisonDebugEndpoints
{
    private static readonly string[] ComparableStatuses = ["active", "pre_game"];

    public static IEndpointRouteBuilder MapMarketComparisonDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/market-comparison", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var marketRows = await LoadComparableMarketRowsAsync(dbContext, cancellationToken);

            var prizePicksMarkets = SelectLatestByMarketKey(marketRows, "prizepicks");
            var underdogMarkets = SelectLatestByMarketKey(marketRows, "underdog");
            var matchedKeys = prizePicksMarkets.Keys
                .Intersect(underdogMarkets.Keys)
                .ToArray();

            var results = matchedKeys
                .Select(key =>
                {
                    var prizePicks = prizePicksMarkets[key];
                    var underdog = underdogMarkets[key];
                    var lineDifference = prizePicks.Line - underdog.Line;
                    var absoluteLineDifference = Math.Abs(lineDifference);

                    return new MarketComparisonResult(
                        prizePicks.PlayerName,
                        prizePicks.League,
                        prizePicks.PropType,
                        prizePicks.Line,
                        underdog.Line,
                        lineDifference,
                        absoluteLineDifference,
                        prizePicks.ObservedAt,
                        underdog.ObservedAt);
                })
                .OrderByDescending(result => result.AbsoluteLineDifference)
                .ThenBy(result => result.PlayerName)
                .ThenBy(result => result.PropType)
                .Take(100)
                .ToArray();

            return Results.Ok(new
            {
                matchedMarkets = matchedKeys.Length,
                prizePicksMarkets = prizePicksMarkets.Count,
                underdogMarkets = underdogMarkets.Count,
                unmatchedPrizePicksMarkets = prizePicksMarkets.Count - matchedKeys.Length,
                unmatchedUnderdogMarkets = underdogMarkets.Count - matchedKeys.Length,
                results
            });
        })
        .WithName("MarketComparisonDebug")
        .WithSummary("Compares persisted PrizePicks and Underdog markets by canonical league, prop type, and player name.");

        endpoints.MapGet("/api/debug/market-comparison/diagnostics", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var marketRows = await LoadComparableMarketRowsAsync(dbContext, cancellationToken);

            var prizePicksMarkets = SelectLatestByMarketKey(marketRows, "prizepicks");
            var underdogMarkets = SelectLatestByMarketKey(marketRows, "underdog");
            var matchedKeys = prizePicksMarkets.Keys
                .Intersect(underdogMarkets.Keys)
                .ToArray();

            var matchedMarkets = matchedKeys
                .Select(key => new MatchedMarketDiagnostics(
                    prizePicksMarkets[key].PlayerName,
                    prizePicksMarkets[key].League,
                    prizePicksMarkets[key].PropType,
                    [
                        CreateSourceDiagnostics(prizePicksMarkets[key]),
                        CreateSourceDiagnostics(underdogMarkets[key])
                    ]))
                .ToArray();

            var matchCountsByLeague = matchedMarkets
                .GroupBy(market => market.League)
                .Select(group => new MatchCount(group.Key, group.Count()))
                .OrderByDescending(count => count.Count)
                .ThenBy(count => count.Name)
                .ToArray();

            var matchCountsByPropType = matchedMarkets
                .GroupBy(market => market.PropType)
                .Select(group => new MatchCount(group.Key, group.Count()))
                .OrderByDescending(count => count.Count)
                .ThenBy(count => count.Name)
                .ToArray();

            var sampleMatchedMarkets = matchedMarkets
                .OrderBy(market => market.League)
                .ThenBy(market => market.PlayerName)
                .ThenBy(market => market.PropType)
                .Take(100)
                .ToArray();

            return Results.Ok(new
            {
                matchedMarkets = matchedKeys.Length,
                prizePicksMarkets = prizePicksMarkets.Count,
                underdogMarkets = underdogMarkets.Count,
                unmatchedPrizePicksMarkets = prizePicksMarkets.Count - matchedKeys.Length,
                unmatchedUnderdogMarkets = underdogMarkets.Count - matchedKeys.Length,
                matchCountsByLeague,
                matchCountsByPropType,
                sampleMatchedMarkets
            });
        })
        .WithName("MarketComparisonDiagnosticsDebug")
        .WithSummary("Returns diagnostics for persisted PrizePicks and Underdog market matches.");

        return endpoints;
    }

    private static Task<List<MarketRow>> LoadComparableMarketRowsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp =>
                ComparableStatuses.Contains(platformProp.Status)
                && platformProp.Platform != null
                && platformProp.Prop != null
                && platformProp.Prop.Player != null
                && platformProp.Prop.League != null
                && platformProp.Prop.PropType != null
                && (platformProp.Platform.Slug == "prizepicks" || platformProp.Platform.Slug == "underdog"))
            .Select(platformProp => new MarketRow(
                platformProp.Platform!.Slug,
                platformProp.Prop!.Id,
                platformProp.Prop.NormalizedKey,
                platformProp.Id,
                platformProp.PlatformPropId,
                platformProp.Prop.PlayerId,
                platformProp.Prop.Player!.DisplayName,
                platformProp.Prop.LeagueId,
                platformProp.Prop.League!.Code,
                platformProp.Prop.PropTypeId,
                platformProp.Prop.PropType!.Name,
                platformProp.Prop.PropType.StatKey,
                platformProp.CurrentLine,
                platformProp.Status,
                platformProp.ObservedAt))
            .ToListAsync(cancellationToken);
    }

    private static Dictionary<MarketKey, MarketRow> SelectLatestByMarketKey(
        IEnumerable<MarketRow> marketRows,
        string platformSlug)
    {
        return marketRows
            .Where(row => string.Equals(row.PlatformSlug, platformSlug, StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => CreateMarketKey(row))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.ObservedAt)
                    .ThenBy(row => row.Line)
                    .First());
    }

    private static MarketKey CreateMarketKey(MarketRow marketRow)
    {
        return new MarketKey(
            NormalizePlayerName(marketRow.PlayerName),
            marketRow.LeagueId,
            marketRow.PropTypeId);
    }

    private static MarketSourceDiagnostics CreateSourceDiagnostics(MarketRow marketRow)
    {
        return new MarketSourceDiagnostics(
            marketRow.PlayerName,
            marketRow.League,
            marketRow.PropType,
            marketRow.NormalizedKey,
            marketRow.PropId,
            marketRow.PlatformPropRecordId,
            marketRow.PlatformPropId,
            marketRow.PlatformSlug,
            marketRow.Line,
            marketRow.Status,
            marketRow.ObservedAt);
    }

    private static string NormalizePlayerName(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = true;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private sealed record MarketKey(string PlayerNameKey, Guid LeagueId, Guid PropTypeId);

    private sealed record MarketRow(
        string PlatformSlug,
        Guid PropId,
        string NormalizedKey,
        Guid PlatformPropRecordId,
        string PlatformPropId,
        Guid PlayerId,
        string PlayerName,
        Guid LeagueId,
        string League,
        Guid PropTypeId,
        string PropType,
        string PropTypeKey,
        decimal Line,
        string Status,
        DateTimeOffset ObservedAt);

    private sealed record MarketComparisonResult(
        string PlayerName,
        string League,
        string PropType,
        decimal PrizePicksLine,
        decimal UnderdogLine,
        decimal LineDifference,
        decimal AbsoluteLineDifference,
        DateTimeOffset PrizePicksObservedAt,
        DateTimeOffset UnderdogObservedAt);

    private sealed record MatchCount(string Name, int Count);

    private sealed record MatchedMarketDiagnostics(
        string PlayerName,
        string League,
        string PropType,
        MarketSourceDiagnostics[] Sources);

    private sealed record MarketSourceDiagnostics(
        string PlayerName,
        string League,
        string PropType,
        string NormalizedKey,
        Guid PropId,
        Guid PlatformPropRecordId,
        string PlatformPropId,
        string Platform,
        decimal CurrentLine,
        string Status,
        DateTimeOffset ObservedAt);
}
