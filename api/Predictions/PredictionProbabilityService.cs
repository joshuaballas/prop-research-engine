using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;

namespace SportsBetting.Api.Predictions;

public sealed class PredictionProbabilityService(
    ApplicationDbContext dbContext,
    IPlayerIdentityAliasService playerIdentityAliasService) : IPredictionProbabilityService
{
    private const int PriorSampleWeight = 25;
    private IReadOnlyDictionary<Guid, Guid[]>? equivalentPropTypeIdsById;

    public async Task<PredictionProbabilityRunAnalysis> AnalyzeLatestRunAsync(CancellationToken cancellationToken)
    {
        return await AnalyzeLatestRunAsync(propIds: null, cancellationToken);
    }

    public async Task<PredictionProbabilityRunAnalysis> AnalyzeLatestRunForPropsAsync(
        IReadOnlyCollection<Guid> propIds,
        CancellationToken cancellationToken)
    {
        return await AnalyzeLatestRunAsync(propIds, cancellationToken);
    }

    private async Task<PredictionProbabilityRunAnalysis> AnalyzeLatestRunAsync(
        IReadOnlyCollection<Guid>? propIds,
        CancellationToken cancellationToken)
    {
        var runId = await dbContext.PredictionRuns
            .AsNoTracking()
            .Where(run => run.ModelName == "BaselineAverageModel")
            .OrderByDescending(run => run.CreatedAt)
            .Select(run => (Guid?)run.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!runId.HasValue)
        {
            return new PredictionProbabilityRunAnalysis(
                Guid.Empty,
                "BaselineAverageModel",
                "1.0",
                DateTimeOffset.MinValue,
                0,
                []);
        }

        return (await AnalyzeRunAsync(runId.Value, propIds, cancellationToken))!;
    }

    public async Task<PredictionProbabilityRunAnalysis?> AnalyzeRunAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        return await AnalyzeRunAsync(runId, propIds: null, cancellationToken);
    }

    public async Task<PredictionProbabilityRunAnalysis?> AnalyzeRunForPropsAsync(
        Guid runId,
        IReadOnlyCollection<Guid> propIds,
        CancellationToken cancellationToken)
    {
        return await AnalyzeRunAsync(runId, propIds, cancellationToken);
    }

    private async Task<PredictionProbabilityRunAnalysis?> AnalyzeRunAsync(
        Guid runId,
        IReadOnlyCollection<Guid>? propIds,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.PredictionRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(predictionRun => predictionRun.Id == runId, cancellationToken);

        if (run is null)
        {
            return null;
        }

        var predictions = await LoadPredictionsAsync(runId, propIds, cancellationToken);
        if (predictions.Count == 0)
        {
            return new PredictionProbabilityRunAnalysis(
                run.Id,
                run.ModelName,
                run.ModelVersion,
                run.CreatedAt,
                0,
                []);
        }

        var predictionPropTypeIds = predictions
            .Select(prediction => prediction.PropTypeId)
            .Distinct()
            .ToArray();
        var equivalentPropTypeIds = await LoadEquivalentPropTypeIdsAsync(cancellationToken);
        equivalentPropTypeIdsById = equivalentPropTypeIds;
        var relevantPropTypeIds = predictionPropTypeIds
            .SelectMany(propTypeId => equivalentPropTypeIds.TryGetValue(propTypeId, out var ids) ? ids : [propTypeId])
            .Distinct()
            .ToArray();
        var maxPredictionEventDate = predictions.Max(prediction => prediction.EventDate);
        var historicalResults = await LoadHistoricalResultsAsync(
            relevantPropTypeIds,
            maxPredictionEventDate,
            cancellationToken);
        var historicalResultsByPlayerPropType = historicalResults
            .GroupBy(result => new PlayerPropTypeKey(result.PlayerId, result.PropTypeId))
            .ToDictionary(group => group.Key, group => group.OrderBy(result => result.EventDate).ToArray());
        var historicalResultsByPropType = historicalResults
            .GroupBy(result => result.PropTypeId)
            .ToDictionary(group => group.Key, group => group.OrderBy(result => result.EventDate).ToArray());
        var aliasCache = new Dictionary<Guid, Guid[]>();
        var probabilityResults = new List<PredictionProbabilityResult>(predictions.Count);

        foreach (var prediction in predictions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            probabilityResults.Add(await AnalyzePredictionAsync(
                prediction,
                historicalResultsByPlayerPropType,
                historicalResultsByPropType,
                aliasCache,
                cancellationToken));
        }

        return new PredictionProbabilityRunAnalysis(
            run.Id,
            run.ModelName,
            run.ModelVersion,
            run.CreatedAt,
            probabilityResults.Count,
            probabilityResults);
    }

    private async Task<PredictionProbabilityResult> AnalyzePredictionAsync(
        PredictionProbabilityInput prediction,
        IReadOnlyDictionary<PlayerPropTypeKey, HistoricalResultRow[]> historicalResultsByPlayerPropType,
        IReadOnlyDictionary<Guid, HistoricalResultRow[]> historicalResultsByPropType,
        Dictionary<Guid, Guid[]> aliasCache,
        CancellationToken cancellationToken)
    {
        if (!prediction.SportsbookLine.HasValue)
        {
            return ToUnavailableResult(prediction, "Unavailable: missing sportsbook line.");
        }

        var equivalentPlayerIds = await GetEquivalentPlayerIdsAsync(
            prediction.PlayerId,
            prediction.LeagueCode,
            aliasCache,
            cancellationToken);
        var equivalentPropTypeIds = await GetEquivalentPropTypeIdsAsync(prediction.PropTypeId, cancellationToken);
        var playerResults = equivalentPlayerIds
            .SelectMany(playerId => equivalentPropTypeIds.Select(propTypeId => new PlayerPropTypeKey(playerId, propTypeId)))
            .Where(historicalResultsByPlayerPropType.ContainsKey)
            .SelectMany(key => historicalResultsByPlayerPropType[key])
            .Where(result => result.EventDate < prediction.EventDate)
            .Select(result => result.Value)
            .ToArray();
        var priorResults = equivalentPropTypeIds
            .Where(historicalResultsByPropType.ContainsKey)
            .SelectMany(propTypeId => historicalResultsByPropType[propTypeId])
            .Where(result => result.EventDate < prediction.EventDate)
            .Select(result => result.Value)
            .ToArray();

        if (playerResults.Length == 0 && priorResults.Length == 0)
        {
            return ToUnavailableResult(prediction, "Unavailable: no historical player or prop-type results before event date.");
        }

        var line = prediction.SportsbookLine.Value;
        var playerProbability = CalculateEmpiricalProbability(playerResults, line);
        var priorProbability = CalculateEmpiricalProbability(priorResults, line);
        var sampleWeight = playerResults.Length / (decimal)(playerResults.Length + PriorSampleWeight);
        var probabilityOver = BlendProbability(playerProbability.Over, priorProbability.Over, sampleWeight);
        var probabilityUnder = BlendProbability(playerProbability.Under, priorProbability.Under, sampleWeight);
        var probabilityPush = BlendProbability(playerProbability.Push, priorProbability.Push, sampleWeight);
        var confidenceScore = CalculateConfidenceScore(playerResults.Length, priorResults.Length, prediction.Confidence);
        var rankingScore = CalculateRankingScore(probabilityOver, probabilityUnder, probabilityPush, confidenceScore);
        var methodUsed = playerResults.Length == 0
            ? "PropTypePriorEmpiricalDistribution"
            : "PlayerEmpiricalDistributionWithPropTypePriorShrinkage";

        return new PredictionProbabilityResult(
            prediction.PredictionId,
            prediction.PropId,
            prediction.PlayerId,
            prediction.PlayerName,
            prediction.PropTypeId,
            prediction.PropType,
            prediction.EventDate,
            prediction.PredictedValue,
            prediction.SportsbookLine,
            prediction.Edge,
            prediction.Recommendation,
            prediction.Confidence,
            probabilityOver,
            probabilityUnder,
            probabilityPush,
            confidenceScore,
            rankingScore,
            playerResults.Length,
            methodUsed);
    }

    private async Task<Guid[]> GetEquivalentPlayerIdsAsync(
        Guid playerId,
        string leagueCode,
        Dictionary<Guid, Guid[]> aliasCache,
        CancellationToken cancellationToken)
    {
        if (aliasCache.TryGetValue(playerId, out var cachedPlayerIds))
        {
            return cachedPlayerIds;
        }

        var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
            playerId,
            leagueCode,
            cancellationToken);
        var equivalentPlayerIdArray = equivalentPlayerIds.Distinct().ToArray();
        aliasCache[playerId] = equivalentPlayerIdArray;

        return equivalentPlayerIdArray;
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

    private async Task<List<PredictionProbabilityInput>> LoadPredictionsAsync(
        Guid runId,
        IReadOnlyCollection<Guid>? propIds,
        CancellationToken cancellationToken)
    {
        var propIdFilter = propIds?
            .Where(propId => propId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (propIdFilter is { Length: 0 })
        {
            return [];
        }

        var query = dbContext.Predictions
            .AsNoTracking()
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.Player)
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.League)
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.PropType)
            .Where(prediction => prediction.PredictionRunId == runId
                && prediction.Prop != null
                && prediction.Prop.Player != null
                && prediction.Prop.League != null
                && prediction.Prop.PropType != null);

        if (propIdFilter is not null)
        {
            query = query.Where(prediction => propIdFilter.Contains(prediction.PropId));
        }

        var predictions = await query
            .Select(prediction => new PredictionProbabilityInput(
                prediction.Id,
                prediction.PropId,
                prediction.Prop!.PlayerId,
                prediction.Prop.Player!.DisplayName,
                prediction.Prop.League!.Code,
                prediction.Prop.PropTypeId,
                prediction.Prop.PropType!.Name,
                DateOnly.FromDateTime(prediction.Prop.StartsAt.UtcDateTime),
                prediction.PredictedValue,
                prediction.SportsbookLine,
                prediction.Edge,
                prediction.Recommendation,
                prediction.Confidence))
            .ToListAsync(cancellationToken);

        return predictions
            .OrderByDescending(prediction => prediction.Edge.HasValue ? Math.Abs(prediction.Edge.Value) : 0m)
            .ThenBy(prediction => prediction.PlayerName)
            .ToList();
    }

    private async Task<List<HistoricalResultRow>> LoadHistoricalResultsAsync(
        IReadOnlyCollection<Guid> propTypeIds,
        DateOnly maxPredictionEventDate,
        CancellationToken cancellationToken)
    {
        if (propTypeIds.Count == 0)
        {
            return [];
        }

        return await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.League)
            .Where(result => result.League != null
                && result.League.Code == "MLB"
                && propTypeIds.Contains(result.PropTypeId)
                && result.EventDate < maxPredictionEventDate)
            .Select(result => new HistoricalResultRow(
                result.PlayerId,
                result.PropTypeId,
                result.EventDate,
                result.ActualResultValue))
            .ToListAsync(cancellationToken);
    }

    private static EmpiricalProbability CalculateEmpiricalProbability(IReadOnlyCollection<decimal> values, decimal line)
    {
        if (values.Count == 0)
        {
            return new EmpiricalProbability(null, null, null);
        }

        return new EmpiricalProbability(
            Math.Round(values.Count(value => value > line) / (decimal)values.Count, 4),
            Math.Round(values.Count(value => value < line) / (decimal)values.Count, 4),
            Math.Round(values.Count(value => value == line) / (decimal)values.Count, 4));
    }

    private static decimal? BlendProbability(decimal? playerProbability, decimal? priorProbability, decimal sampleWeight)
    {
        if (playerProbability.HasValue && priorProbability.HasValue)
        {
            return Math.Round((playerProbability.Value * sampleWeight) + (priorProbability.Value * (1m - sampleWeight)), 4);
        }

        return playerProbability ?? priorProbability;
    }

    private static decimal CalculateConfidenceScore(int sampleSize, int priorSampleSize, decimal modelConfidence)
    {
        if (sampleSize == 0 && priorSampleSize == 0)
        {
            return 0m;
        }

        var sampleScore = sampleSize switch
        {
            >= 200 => 1.00m,
            >= 100 => 0.90m,
            >= 50 => 0.75m,
            >= 25 => 0.60m,
            >= 10 => 0.45m,
            > 0 => 0.25m,
            _ => 0.10m
        };
        var priorScore = priorSampleSize >= 1000 ? 1.00m : Math.Min(1.00m, priorSampleSize / 1000m);

        return Math.Round(((sampleScore * 0.70m) + (priorScore * 0.10m) + (modelConfidence * 0.20m)), 4);
    }

    private static decimal CalculateRankingScore(
        decimal? probabilityOver,
        decimal? probabilityUnder,
        decimal? probabilityPush,
        decimal confidenceScore)
    {
        if (!probabilityOver.HasValue || !probabilityUnder.HasValue)
        {
            return 0m;
        }

        var bestSideProbability = Math.Max(probabilityOver.Value, probabilityUnder.Value);
        var noPushProbability = 1m - (probabilityPush ?? 0m);
        var directionalStrength = Math.Max(0m, bestSideProbability - 0.50m);

        return Math.Round(directionalStrength * noPushProbability * confidenceScore * 100m, 4);
    }

    private static PredictionProbabilityResult ToUnavailableResult(
        PredictionProbabilityInput prediction,
        string methodUsed)
    {
        return new PredictionProbabilityResult(
            prediction.PredictionId,
            prediction.PropId,
            prediction.PlayerId,
            prediction.PlayerName,
            prediction.PropTypeId,
            prediction.PropType,
            prediction.EventDate,
            prediction.PredictedValue,
            prediction.SportsbookLine,
            prediction.Edge,
            prediction.Recommendation,
            prediction.Confidence,
            null,
            null,
            null,
            0m,
            0m,
            0,
            methodUsed);
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

    private readonly record struct PlayerPropTypeKey(Guid PlayerId, Guid PropTypeId);

    private sealed record PredictionProbabilityInput(
        Guid PredictionId,
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        string LeagueCode,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate,
        decimal? PredictedValue,
        decimal? SportsbookLine,
        decimal? Edge,
        string Recommendation,
        decimal Confidence);

    private sealed record HistoricalResultRow(
        Guid PlayerId,
        Guid PropTypeId,
        DateOnly EventDate,
        decimal Value);

    private sealed record EmpiricalProbability(
        decimal? Over,
        decimal? Under,
        decimal? Push);
}
