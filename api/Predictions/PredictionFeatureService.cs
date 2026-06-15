using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;

namespace SportsBetting.Api.Predictions;

public sealed class PredictionFeatureService(
    ApplicationDbContext dbContext,
    IPlayerIdentityAliasService playerIdentityAliasService,
    ILogger<PredictionFeatureService> logger) : IPredictionFeatureService
{
    private IReadOnlyDictionary<Guid, Guid[]>? equivalentPropTypeIdsById;

    public async Task<PlayerPredictionFeatures> GeneratePlayerFeaturesAsync(
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate,
        CancellationToken cancellationToken)
    {
        var lookup = await LoadHistoricalLookupAsync(playerId, propTypeId, eventDate, cancellationToken);
        var historicalResults = lookup.HistoricalResults;

        var last5 = historicalResults.Take(5).ToArray();
        var last10 = historicalResults.Take(10).ToArray();
        var seasonResults = historicalResults
            .Where(result => result.EventDate.Year == eventDate.Year)
            .ToArray();
        var latestResult = historicalResults.FirstOrDefault();

        return new PlayerPredictionFeatures(
            playerId,
            propTypeId,
            eventDate,
            Average(last5),
            Average(last10),
            Average(seasonResults),
            StandardDeviation(last10),
            NonZeroHitRate(last5),
            NonZeroHitRate(last10),
            latestResult is null ? null : eventDate.DayNumber - latestResult.EventDate.DayNumber,
            historicalResults.Count,
            seasonResults.Length,
            "Share of historical games in the window where actual_result_value > 0. No sportsbook line is used.",
            "Last 10 historical games before event_date.");
    }

    public async Task<PlayerHistoricalLookupDiagnostic> AnalyzeHistoricalLookupAsync(
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate,
        CancellationToken cancellationToken)
    {
        var lookup = await LoadHistoricalLookupAsync(playerId, propTypeId, eventDate, cancellationToken);
        var historicalResultsForEquivalentPropTypes = await dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => lookup.EquivalentPropTypeIds.Contains(result.PropTypeId)
                && result.EventDate < eventDate, cancellationToken);
        var historicalResultsForEquivalentPlayers = await dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => lookup.EquivalentPlayerIds.Contains(result.PlayerId)
                && result.EventDate < eventDate, cancellationToken);
        var mlbMappingsForEquivalentPlayers = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .CountAsync(reference => lookup.EquivalentPlayerIds.Contains(reference.PlayerId)
                && reference.Provider == "mlb", cancellationToken);
        var seasonGamesAvailable = lookup.HistoricalResults
            .Count(result => result.EventDate.Year == eventDate.Year);

        return new PlayerHistoricalLookupDiagnostic(
            playerId,
            propTypeId,
            eventDate,
            lookup.EquivalentPlayerIds,
            lookup.EquivalentPropTypeIds,
            lookup.HistoricalResults.Count,
            seasonGamesAvailable,
            lookup.HistoricalResults.Count(result => result.PlayerId != playerId),
            historicalResultsForEquivalentPropTypes,
            historicalResultsForEquivalentPlayers,
            mlbMappingsForEquivalentPlayers,
            lookup.EquivalentPlayerIds.Length > 1,
            mlbMappingsForEquivalentPlayers > 0,
            historicalResultsForEquivalentPropTypes > 0,
            historicalResultsForEquivalentPlayers > 0);
    }

    private async Task<HistoricalLookupResult> LoadHistoricalLookupAsync(
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate,
        CancellationToken cancellationToken)
    {
        var equivalentPropTypeIds = await GetEquivalentPropTypeIdsAsync(propTypeId, cancellationToken);
        var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
            playerId,
            "MLB",
            cancellationToken);
        var equivalentPlayerIdArray = equivalentPlayerIds.Distinct().ToArray();
        var historicalResults = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => equivalentPlayerIdArray.Contains(result.PlayerId)
                && equivalentPropTypeIds.Contains(result.PropTypeId)
                && result.EventDate < eventDate)
            .OrderByDescending(result => result.EventDate)
            .Select(result => new FeatureResult(result.PlayerId, result.EventDate, result.ActualResultValue))
            .ToListAsync(cancellationToken);
        var historicalResultCountRecovered = historicalResults.Count(result => result.PlayerId != playerId);

        if (equivalentPlayerIdArray.Length > 1 && historicalResultCountRecovered > 0)
        {
            logger.LogDebug(
                "Resolved prediction historical results through player aliases. OriginalPlayerId: {OriginalPlayerId}; AliasCount: {AliasCount}; HistoricalResultCountRecovered: {HistoricalResultCountRecovered}.",
                playerId,
                equivalentPlayerIdArray.Length - 1,
                historicalResultCountRecovered);
        }

        return new HistoricalLookupResult(
            equivalentPlayerIdArray,
            equivalentPropTypeIds,
            historicalResults);
    }

    private async Task<Guid[]> GetEquivalentPropTypeIdsAsync(Guid propTypeId, CancellationToken cancellationToken)
    {
        equivalentPropTypeIdsById ??= await LoadEquivalentPropTypeIdsAsync(cancellationToken);

        return equivalentPropTypeIdsById.TryGetValue(propTypeId, out var equivalentPropTypeIds)
            ? equivalentPropTypeIds
            : [propTypeId];
    }

    private async Task<IReadOnlyDictionary<Guid, Guid[]>> LoadEquivalentPropTypeIdsAsync(CancellationToken cancellationToken)
    {
        var propTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(propType => new
            {
                propType.Id,
                propType.Name
            })
            .ToListAsync(cancellationToken);

        return propTypes
            .GroupBy(propType => CanonicalPropTypeName(propType.Name), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var equivalentIds = group.Select(propType => propType.Id).Distinct().ToArray();
                return group.Select(propType => new
                {
                    propType.Id,
                    EquivalentIds = equivalentIds
                });
            })
            .ToDictionary(item => item.Id, item => item.EquivalentIds);
    }

    private static string CanonicalPropTypeName(string propTypeName)
    {
        return NormalizePropTypeName(propTypeName) switch
        {
            "RBI" => "RBIS",
            _ => NormalizePropTypeName(propTypeName)
        };
    }

    private static string NormalizePropTypeName(string propTypeName)
    {
        var builder = new System.Text.StringBuilder(propTypeName.Length);

        foreach (var character in propTypeName.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static decimal? Average(IReadOnlyCollection<FeatureResult> results)
    {
        return results.Count == 0
            ? null
            : Math.Round(results.Average(result => result.Value), 4);
    }

    private static decimal? NonZeroHitRate(IReadOnlyCollection<FeatureResult> results)
    {
        return results.Count == 0
            ? null
            : Math.Round(results.Count(result => result.Value > 0) / (decimal)results.Count, 4);
    }

    private static decimal? StandardDeviation(IReadOnlyCollection<FeatureResult> results)
    {
        if (results.Count < 2)
        {
            return null;
        }

        var average = results.Average(result => result.Value);
        var variance = results.Sum(result => (result.Value - average) * (result.Value - average)) / results.Count;

        return Math.Round((decimal)Math.Sqrt((double)variance), 4);
    }

    private sealed record FeatureResult(Guid PlayerId, DateOnly EventDate, decimal Value);

    private sealed record HistoricalLookupResult(
        Guid[] EquivalentPlayerIds,
        Guid[] EquivalentPropTypeIds,
        IReadOnlyList<FeatureResult> HistoricalResults);
}
