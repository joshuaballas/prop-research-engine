using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Predictions;

public sealed class PredictionGenerationService(
    ApplicationDbContext dbContext,
    IPredictionRepository predictionRepository,
    IPlayerIdentityAliasService playerIdentityAliasService,
    IOptions<PredictionGenerationOptions> options,
    ILogger<PredictionGenerationService> logger) : IPredictionGenerationService
{
    private const string ModelName = "BaselineAverageModel";
    private const string ModelVersion = "1.0";
    private const int TheOddsFreshnessWindowHours = 24;
    private const int PriorSampleWeight = 25;
    private IReadOnlyDictionary<Guid, Guid[]>? equivalentPropTypeIdsById;

    public async Task<PredictionGenerationResult> GenerateMlbBaselinePredictionsAsync(CancellationToken cancellationToken)
    {
        return await GenerateMlbBaselinePredictionsAsync(
            PredictionGenerationScope.FullMlb,
            request: null,
            cancellationToken);
    }

    public async Task<PredictionGenerationResult> GenerateTheOddsMlbBaselinePredictionsAsync(
        TheOddsMlbPredictionGenerationRequest request,
        CancellationToken cancellationToken)
    {
        return await GenerateMlbBaselinePredictionsAsync(
            PredictionGenerationScope.TheOdds,
            request,
            cancellationToken);
    }

    private async Task<PredictionGenerationResult> GenerateMlbBaselinePredictionsAsync(
        PredictionGenerationScope scope,
        TheOddsMlbPredictionGenerationRequest? request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observedAt = DateTimeOffset.UtcNow;
        var loadCandidatePropsStopwatch = Stopwatch.StartNew();
        var props = scope == PredictionGenerationScope.TheOdds
            ? await LoadTheOddsCandidatePropsAsync(request ?? new TheOddsMlbPredictionGenerationRequest(null, null, null), observedAt, cancellationToken)
            : await LoadFullMlbCandidatePropsAsync(cancellationToken);
        loadCandidatePropsStopwatch.Stop();

        var predictionRun = new PredictionRun
        {
            Id = Guid.NewGuid(),
            ModelName = ModelName,
            ModelVersion = ModelVersion,
            CreatedAt = observedAt,
            Notes = BuildRunNotes(scope, request)
        };
        var skipCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["NoHistoricalResults"] = 0,
            ["NoSportsbookLine"] = 0,
            ["NoPropType"] = 0,
            ["InsufficientHistory"] = 0
        };
        var suppressionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["RecommendationSuppressedByThreshold"] = 0,
            ["RecommendationSuppressedByPropTypeFilter"] = 0,
            ["RecommendationSuppressedByHitProbability"] = 0,
            ["RecommendationSuppressedByMissingProbability"] = 0
        };
        var generationOptions = options.Value;
        var enabledPropTypes = generationOptions.EnabledPropTypes
            .Where(propType => !string.IsNullOrWhiteSpace(propType))
            .Select(NormalizePropTypeName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var historyContextStopwatch = Stopwatch.StartNew();
        var historyContext = await LoadBatchPredictionHistoryContextAsync(
            props
                .Where(prop => !string.IsNullOrWhiteSpace(prop.PropTypeName) && prop.SportsbookLine.HasValue)
                .ToArray(),
            cancellationToken);
        historyContextStopwatch.Stop();

        var featureGenerationMs = 0L;
        var probabilityOrRecommendationMs = 0L;
        var rowsRequiringHistory = 0;
        var rowsWithHistory = 0;

        foreach (var prop in props)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(prop.PropTypeName))
            {
                IncrementSkip(skipCounts, "NoPropType");
                continue;
            }

            if (prop.SportsbookLine is null)
            {
                IncrementSkip(skipCounts, "NoSportsbookLine");
                continue;
            }

            var eventDate = DateOnly.FromDateTime(prop.StartsAt.UtcDateTime);
            var featureStopwatch = Stopwatch.StartNew();
            var features = GeneratePlayerFeaturesFromBatch(
                prop.PlayerId,
                prop.PropTypeId,
                eventDate,
                historyContext);
            featureStopwatch.Stop();
            featureGenerationMs += featureStopwatch.ElapsedMilliseconds;
            rowsRequiringHistory++;
            if (features.HistoricalGamesAvailable > 0)
            {
                rowsWithHistory++;
            }

            if (features.HistoricalGamesAvailable == 0)
            {
                IncrementSkip(skipCounts, "NoHistoricalResults");
                continue;
            }

            if (features.SeasonAverage is null
                || features.Last10GameAverage is null
                || features.Last5GameAverage is null)
            {
                IncrementSkip(skipCounts, "InsufficientHistory");
                continue;
            }

            var predictedValue = (features.SeasonAverage.Value * 0.50m)
                + (features.Last10GameAverage.Value * 0.30m)
                + (features.Last5GameAverage.Value * 0.20m);
            predictedValue = Math.Round(predictedValue, 4);

            var recommendationStopwatch = Stopwatch.StartNew();
            var edge = Math.Round(predictedValue - prop.SportsbookLine.Value, 4);
            var confidence = CalculateConfidence(features);
            var sideProbability = CalculateSideProbabilitiesFromBatch(
                prop.PlayerId,
                prop.PropTypeId,
                prop.SportsbookLine.Value,
                eventDate,
                historyContext);
            var recommendationDecision = GetRecommendation(
                edge,
                prop.PropTypeName,
                sideProbability,
                generationOptions,
                enabledPropTypes,
                suppressionCounts);
            recommendationStopwatch.Stop();
            probabilityOrRecommendationMs += recommendationStopwatch.ElapsedMilliseconds;

            predictionRun.Predictions.Add(new Prediction
            {
                Id = Guid.NewGuid(),
                PredictionRunId = predictionRun.Id,
                PropId = prop.Id,
                PredictedProbability = recommendationDecision.SelectedSideProbability,
                PredictedValue = predictedValue,
                SportsbookLine = prop.SportsbookLine,
                Edge = edge,
                Recommendation = recommendationDecision.Recommendation,
                Confidence = confidence,
                CreatedAt = observedAt
            });
        }

        var skippedReasonAggregationStopwatch = Stopwatch.StartNew();
        foreach (var skipCount in skipCounts
            .Concat(suppressionCounts)
            .Where(skipCount => skipCount.Value > 0))
        {
            predictionRun.SkipCounts.Add(new PredictionRunSkipCount
            {
                Id = Guid.NewGuid(),
                PredictionRunId = predictionRun.Id,
                Reason = skipCount.Key,
                Count = skipCount.Value,
                CreatedAt = observedAt
            });
        }
        skippedReasonAggregationStopwatch.Stop();

        predictionRun.CompletedAt = DateTimeOffset.UtcNow;

        var persistenceStopwatch = Stopwatch.StartNew();
        await predictionRepository.AddPredictionRunAsync(predictionRun, cancellationToken);
        persistenceStopwatch.Stop();

        stopwatch.Stop();

        logger.LogInformation(
            "Generated MLB baseline predictions. Scope: {Scope}; props evaluated: {PropsEvaluated}; predictions generated: {PredictionsGenerated}; skipped props: {SkippedProps}; threshold suppressions: {ThresholdSuppressions}; prop-type suppressions: {PropTypeSuppressions}; history rows inspected: {HistoryRowsInspected}; rows requiring history: {RowsRequiringHistory}; rows with history: {RowsWithHistory}; history batch load: {HistoryBatchLoadMs}ms; duration: {DurationMs}ms.",
            scope,
            props.Count,
            predictionRun.Predictions.Count,
            skipCounts.Values.Sum(),
            suppressionCounts["RecommendationSuppressedByThreshold"],
            suppressionCounts["RecommendationSuppressedByPropTypeFilter"],
            historyContext.HistoricalRows.Count,
            rowsRequiringHistory,
            rowsWithHistory,
            historyContextStopwatch.ElapsedMilliseconds,
            stopwatch.ElapsedMilliseconds);

        return new PredictionGenerationResult(
            predictionRun.Id,
            ModelName,
            ModelVersion,
            props.Count,
            predictionRun.Predictions.Count,
            skipCounts.Values.Sum(),
            suppressionCounts["RecommendationSuppressedByThreshold"],
            suppressionCounts["RecommendationSuppressedByPropTypeFilter"],
            stopwatch.Elapsed,
            new PredictionGenerationTimingDiagnostics(
                stopwatch.ElapsedMilliseconds,
                loadCandidatePropsStopwatch.ElapsedMilliseconds,
                featureGenerationMs,
                probabilityOrRecommendationMs,
                persistenceStopwatch.ElapsedMilliseconds,
                skippedReasonAggregationStopwatch.ElapsedMilliseconds,
                historyContextStopwatch.ElapsedMilliseconds),
            scope == PredictionGenerationScope.TheOdds ? "TheOddsApiMlb" : "FullMlb");
    }

    private async Task<List<PredictionCandidateProp>> LoadFullMlbCandidatePropsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Props
            .AsNoTracking()
            .Where(prop => prop.League != null && prop.League.Code == "MLB")
            .OrderBy(prop => prop.StartsAt)
            .ThenBy(prop => prop.Id)
            .Select(prop => new PredictionCandidateProp(
                prop.Id,
                prop.PlayerId,
                prop.PropTypeId,
                prop.PropType == null ? string.Empty : prop.PropType.Name,
                prop.StartsAt,
                prop.PlatformProps
                    .OrderByDescending(platformProp => platformProp.ObservedAt)
                    .ThenByDescending(platformProp => platformProp.Id)
                    .Select(platformProp => (decimal?)platformProp.CurrentLine)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<PredictionCandidateProp>> LoadTheOddsCandidatePropsAsync(
        TheOddsMlbPredictionGenerationRequest request,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        using var providerFilter = JsonSerializer.SerializeToDocument(new { dataProvider = "The Odds API" });
        var freshnessCutoff = observedAt.AddHours(-TheOddsFreshnessWindowHours);
        var rows = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Metadata != null
                && EF.Functions.JsonContains(platformProp.Metadata, providerFilter)
                && platformProp.Status == "active"
                && platformProp.ObservedAt >= freshnessCutoff
                && platformProp.Prop != null
                && platformProp.Prop.League != null
                && platformProp.Prop.League.Code == "MLB")
            .OrderByDescending(platformProp => platformProp.ObservedAt)
            .ThenByDescending(platformProp => platformProp.Id)
            .Select(platformProp => new TheOddsCandidatePlatformProp(
                platformProp.Id,
                platformProp.PropId,
                platformProp.CurrentLine,
                platformProp.ObservedAt,
                platformProp.Metadata,
                platformProp.Prop!.PlayerId,
                platformProp.Prop.PropTypeId,
                platformProp.Prop.PropType == null ? string.Empty : platformProp.Prop.PropType.Name,
                platformProp.Prop.StartsAt))
            .ToListAsync(cancellationToken);

        var eventIdFilter = NullIfWhiteSpace(request.EventId);
        var bookmakerFilter = SplitCsv(request.Bookmakers);
        var marketFilter = SplitCsv(request.Markets);

        return rows
            .Where(row => MatchesTheOddsFilters(row.Metadata, eventIdFilter, bookmakerFilter, marketFilter))
            .GroupBy(row => row.PropId)
            .Select(group => group
                .OrderByDescending(row => row.ObservedAt)
                .ThenByDescending(row => row.PlatformPropId)
                .First())
            .OrderBy(row => row.StartsAt)
            .ThenBy(row => row.PropId)
            .Select(row => new PredictionCandidateProp(
                row.PropId,
                row.PlayerId,
                row.PropTypeId,
                row.PropTypeName,
                row.StartsAt,
                row.CurrentLine))
            .ToList();
    }

    private static bool MatchesTheOddsFilters(
        JsonDocument? metadata,
        string? eventId,
        IReadOnlySet<string> bookmakers,
        IReadOnlySet<string> markets)
    {
        if (!string.IsNullOrWhiteSpace(eventId)
            && !string.Equals(ExtractMetadataString(metadata, "eventInfo", "eventId"), eventId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (bookmakers.Count > 0)
        {
            var bookmakerKey = ExtractMetadataString(metadata, "bookmaker", "key");
            var bookmakerTitle = ExtractMetadataString(metadata, "bookmaker", "title");
            if ((bookmakerKey is null || !bookmakers.Contains(bookmakerKey))
                && (bookmakerTitle is null || !bookmakers.Contains(bookmakerTitle)))
            {
                return false;
            }
        }

        if (markets.Count > 0)
        {
            var marketKey = ExtractMetadataString(metadata, "market", "key");
            if (marketKey is null || !markets.Contains(marketKey))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ExtractMetadataString(JsonDocument? metadata, string sectionName, string propertyName)
    {
        return metadata?.RootElement.ValueKind == JsonValueKind.Object
            && metadata.RootElement.TryGetProperty(sectionName, out var section)
            && section.ValueKind == JsonValueKind.Object
            && section.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlySet<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildRunNotes(
        PredictionGenerationScope scope,
        TheOddsMlbPredictionGenerationRequest? request)
    {
        if (scope != PredictionGenerationScope.TheOdds)
        {
            return "Baseline weighted-average predictions generated from prop_results features.";
        }

        return string.Join(
            " ",
            "Scoped The Odds API MLB baseline weighted-average predictions generated from fresh persisted platform_props.",
            $"eventId={NullIfWhiteSpace(request?.EventId) ?? "any"};",
            $"bookmakers={NullIfWhiteSpace(request?.Bookmakers) ?? "any"};",
            $"markets={NullIfWhiteSpace(request?.Markets) ?? "any"}.");
    }

    private static void IncrementSkip(Dictionary<string, int> skipCounts, string reason)
    {
        skipCounts[reason] = skipCounts.GetValueOrDefault(reason) + 1;
    }

    private async Task<BatchPredictionHistoryContext> LoadBatchPredictionHistoryContextAsync(
        IReadOnlyCollection<PredictionCandidateProp> props,
        CancellationToken cancellationToken)
    {
        if (props.Count == 0)
        {
            return new BatchPredictionHistoryContext(
                new Dictionary<Guid, Guid[]>(),
                new Dictionary<Guid, Guid[]>(),
                new Dictionary<PlayerPropTypeKey, HistoricalResultRow[]>(),
                new Dictionary<Guid, HistoricalResultRow[]>(),
                []);
        }

        var equivalentPropTypeIds = await LoadEquivalentPropTypeIdsAsync(cancellationToken);
        equivalentPropTypeIdsById = equivalentPropTypeIds;
        var playerIds = props
            .Select(prop => prop.PlayerId)
            .Distinct()
            .ToArray();
        var playerAliasMap = new Dictionary<Guid, Guid[]>();
        foreach (var playerId in playerIds)
        {
            var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
                playerId,
                "MLB",
                cancellationToken);
            playerAliasMap[playerId] = equivalentPlayerIds.Distinct().ToArray();
        }

        var relevantPropTypeIds = props
            .Select(prop => prop.PropTypeId)
            .SelectMany(propTypeId => equivalentPropTypeIds.TryGetValue(propTypeId, out var ids) ? ids : [propTypeId])
            .Distinct()
            .ToArray();
        var maxEventDate = props.Max(prop => DateOnly.FromDateTime(prop.StartsAt.UtcDateTime));
        var historicalRows = relevantPropTypeIds.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(result => relevantPropTypeIds.Contains(result.PropTypeId)
                    && result.EventDate < maxEventDate)
                .Select(result => new HistoricalResultRow(
                    result.PlayerId,
                    result.PropTypeId,
                    result.EventDate,
                    result.ActualResultValue))
                .ToListAsync(cancellationToken);

        var byPlayerPropType = historicalRows
            .GroupBy(result => new PlayerPropTypeKey(result.PlayerId, result.PropTypeId))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(result => result.EventDate).ToArray());
        var byPropType = historicalRows
            .GroupBy(result => result.PropTypeId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(result => result.EventDate).ToArray());

        return new BatchPredictionHistoryContext(
            playerAliasMap,
            equivalentPropTypeIds,
            byPlayerPropType,
            byPropType,
            historicalRows);
    }

    private static PlayerPredictionFeatures GeneratePlayerFeaturesFromBatch(
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate,
        BatchPredictionHistoryContext historyContext)
    {
        var historicalResults = GetPlayerHistoricalRowsFromBatch(playerId, propTypeId, eventDate, historyContext);
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
            Average(last5.Select(result => result.Value)),
            Average(last10.Select(result => result.Value)),
            Average(seasonResults.Select(result => result.Value)),
            StandardDeviation(last10.Select(result => result.Value)),
            NonZeroHitRate(last5.Select(result => result.Value)),
            NonZeroHitRate(last10.Select(result => result.Value)),
            latestResult is null ? null : eventDate.DayNumber - latestResult.EventDate.DayNumber,
            historicalResults.Length,
            seasonResults.Length,
            "Share of historical games in the window where actual_result_value > 0. No sportsbook line is used.",
            "Last 10 historical games before event_date.");
    }

    private static SideProbabilityEstimate CalculateSideProbabilitiesFromBatch(
        Guid playerId,
        Guid propTypeId,
        decimal line,
        DateOnly eventDate,
        BatchPredictionHistoryContext historyContext)
    {
        var playerResults = GetPlayerHistoricalRowsFromBatch(playerId, propTypeId, eventDate, historyContext)
            .Select(result => result.Value)
            .ToArray();
        var priorResults = GetEquivalentPropTypeIdsFromBatch(propTypeId, historyContext)
            .Where(historyContext.HistoricalResultsByPropType.ContainsKey)
            .SelectMany(propTypeId => historyContext.HistoricalResultsByPropType[propTypeId])
            .Where(result => result.EventDate < eventDate)
            .Select(result => result.Value)
            .ToArray();

        if (playerResults.Length == 0 && priorResults.Length == 0)
        {
            return new SideProbabilityEstimate(null, null, null, 0, 0);
        }

        var playerProbability = CalculateEmpiricalProbability(playerResults, line);
        var priorProbability = CalculateEmpiricalProbability(priorResults, line);
        var sampleWeight = playerResults.Length / (decimal)(playerResults.Length + PriorSampleWeight);

        return new SideProbabilityEstimate(
            BlendProbability(playerProbability.Over, priorProbability.Over, sampleWeight),
            BlendProbability(playerProbability.Under, priorProbability.Under, sampleWeight),
            BlendProbability(playerProbability.Push, priorProbability.Push, sampleWeight),
            playerResults.Length,
            priorResults.Length);
    }

    private static HistoricalResultRow[] GetPlayerHistoricalRowsFromBatch(
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate,
        BatchPredictionHistoryContext historyContext)
    {
        var equivalentPlayerIds = historyContext.EquivalentPlayerIdsByPlayerId.TryGetValue(playerId, out var playerIds)
            ? playerIds
            : [playerId];
        var equivalentPropTypeIds = GetEquivalentPropTypeIdsFromBatch(propTypeId, historyContext);

        return equivalentPlayerIds
            .SelectMany(equivalentPlayerId => equivalentPropTypeIds.Select(equivalentPropTypeId => new PlayerPropTypeKey(equivalentPlayerId, equivalentPropTypeId)))
            .Where(historyContext.HistoricalResultsByPlayerPropType.ContainsKey)
            .SelectMany(key => historyContext.HistoricalResultsByPlayerPropType[key])
            .Where(result => result.EventDate < eventDate)
            .OrderByDescending(result => result.EventDate)
            .ToArray();
    }

    private static Guid[] GetEquivalentPropTypeIdsFromBatch(
        Guid propTypeId,
        BatchPredictionHistoryContext historyContext)
    {
        return historyContext.EquivalentPropTypeIdsByPropTypeId.TryGetValue(propTypeId, out var equivalentPropTypeIds)
            ? equivalentPropTypeIds
            : [propTypeId];
    }

    private async Task<SideProbabilityEstimate> CalculateSideProbabilitiesAsync(
        Guid playerId,
        Guid propTypeId,
        decimal line,
        DateOnly eventDate,
        CancellationToken cancellationToken)
    {
        var equivalentPropTypeIds = await GetEquivalentPropTypeIdsAsync(propTypeId, cancellationToken);
        var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
            playerId,
            "MLB",
            cancellationToken);
        var equivalentPlayerIdArray = equivalentPlayerIds.Distinct().ToArray();

        var playerResults = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => equivalentPlayerIdArray.Contains(result.PlayerId)
                && equivalentPropTypeIds.Contains(result.PropTypeId)
                && result.EventDate < eventDate)
            .Select(result => result.ActualResultValue)
            .ToListAsync(cancellationToken);
        var priorResults = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => equivalentPropTypeIds.Contains(result.PropTypeId)
                && result.EventDate < eventDate)
            .Select(result => result.ActualResultValue)
            .ToListAsync(cancellationToken);

        if (playerResults.Count == 0 && priorResults.Count == 0)
        {
            return new SideProbabilityEstimate(null, null, null, 0, 0);
        }

        var playerProbability = CalculateEmpiricalProbability(playerResults, line);
        var priorProbability = CalculateEmpiricalProbability(priorResults, line);
        var sampleWeight = playerResults.Count / (decimal)(playerResults.Count + PriorSampleWeight);

        return new SideProbabilityEstimate(
            BlendProbability(playerProbability.Over, priorProbability.Over, sampleWeight),
            BlendProbability(playerProbability.Under, priorProbability.Under, sampleWeight),
            BlendProbability(playerProbability.Push, priorProbability.Push, sampleWeight),
            playerResults.Count,
            priorResults.Count);
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

    private static PredictionRecommendationDecision GetRecommendation(
        decimal edge,
        string propType,
        SideProbabilityEstimate sideProbability,
        PredictionGenerationOptions generationOptions,
        IReadOnlySet<string> enabledPropTypes,
        IDictionary<string, int> suppressionCounts)
    {
        var propTypeEnabled = enabledPropTypes.Count == 0 || enabledPropTypes.Contains(NormalizePropTypeName(propType));
        var candidates = new[]
        {
            BuildRecommendationCandidate(
                "OVER",
                edge,
                edge,
                sideProbability.ProbabilityOver,
                edge >= generationOptions.OverProjectionEdgeThreshold,
                generationOptions.MinimumPickHitProbability),
            BuildRecommendationCandidate(
                "UNDER",
                edge,
                -edge,
                sideProbability.ProbabilityUnder,
                edge <= -generationOptions.UnderProjectionEdgeThreshold,
                generationOptions.MinimumPickHitProbability)
        }
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();

        var eligibleCandidates = candidates
            .Where(candidate => propTypeEnabled && candidate.ProjectionGatePassed && candidate.HitProbabilityGatePassed)
            .OrderByDescending(candidate => candidate.SelectedSideProbability)
            .ThenByDescending(candidate => candidate.ProjectionEdge)
            .ToArray();
        if (eligibleCandidates.Length > 0)
        {
            var selected = eligibleCandidates[0];
            return new PredictionRecommendationDecision(
                selected.Recommendation,
                selected.SelectedSideProbability,
                selected.ReasonCode);
        }

        var projectionCandidates = candidates
            .Where(candidate => candidate.ProjectionGatePassed)
            .ToArray();
        if (projectionCandidates.Length == 0)
        {
            IncrementSuppression(suppressionCounts, "RecommendationSuppressedByThreshold");
            return new PredictionRecommendationDecision("PASS", null, "RecommendationSuppressedByThreshold");
        }

        if (!propTypeEnabled)
        {
            IncrementSuppression(suppressionCounts, "RecommendationSuppressedByPropTypeFilter");
            return new PredictionRecommendationDecision("PASS", null, "RecommendationSuppressedByPropTypeFilter");
        }

        if (projectionCandidates.Any(candidate => !candidate.SelectedSideProbability.HasValue))
        {
            IncrementSuppression(suppressionCounts, "RecommendationSuppressedByMissingProbability");
            return new PredictionRecommendationDecision("PASS", null, "RecommendationSuppressedByMissingProbability");
        }

        IncrementSuppression(suppressionCounts, "RecommendationSuppressedByHitProbability");
        return new PredictionRecommendationDecision("PASS", null, "RecommendationSuppressedByHitProbability");
    }

    private static PredictionRecommendationCandidate? BuildRecommendationCandidate(
        string recommendation,
        decimal edge,
        decimal projectionEdge,
        decimal? selectedSideProbability,
        bool projectionGatePassed,
        decimal minimumPickHitProbability)
    {
        if ((recommendation == "OVER" && edge <= 0m) || (recommendation == "UNDER" && edge >= 0m))
        {
            return null;
        }

        return new PredictionRecommendationCandidate(
            recommendation,
            Math.Round(projectionEdge, 4),
            selectedSideProbability,
            projectionGatePassed,
            selectedSideProbability.HasValue && selectedSideProbability.Value >= minimumPickHitProbability,
            recommendation == "OVER" ? "OverProjectionAndHitProbability" : "UnderProjectionAndHitProbability");
    }

    private static void IncrementSuppression(IDictionary<string, int> suppressionCounts, string reason)
    {
        suppressionCounts[reason] = suppressionCounts.TryGetValue(reason, out var count)
            ? count + 1
            : 1;
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

    private static string CanonicalPropTypeName(string propTypeName)
    {
        return NormalizePropTypeName(propTypeName) switch
        {
            "RBI" => "RBIS",
            _ => NormalizePropTypeName(propTypeName)
        };
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

    private static decimal CalculateConfidence(PlayerPredictionFeatures features)
    {
        var confidence = 0.50m;

        if (features.SeasonGamesAvailable >= 25)
        {
            confidence += 0.10m;
        }

        if (features.SeasonGamesAvailable >= 50)
        {
            confidence += 0.10m;
        }

        if (features.HistoricalGamesAvailable >= 100)
        {
            confidence += 0.10m;
        }

        if (features.HistoricalGamesAvailable >= 200)
        {
            confidence += 0.10m;
        }

        return Math.Min(confidence, 0.90m);
    }

    private static decimal? Average(IEnumerable<decimal> values)
    {
        var valueArray = values.ToArray();
        return valueArray.Length == 0 ? null : Math.Round(valueArray.Average(), 4);
    }

    private static decimal? StandardDeviation(IEnumerable<decimal> values)
    {
        var valueArray = values.ToArray();
        if (valueArray.Length == 0)
        {
            return null;
        }

        var average = valueArray.Average();
        var variance = valueArray.Sum(value => Math.Pow((double)(value - average), 2)) / valueArray.Length;
        return Math.Round((decimal)Math.Sqrt(variance), 4);
    }

    private static decimal? NonZeroHitRate(IEnumerable<decimal> values)
    {
        var valueArray = values.ToArray();
        return valueArray.Length == 0
            ? null
            : Math.Round(valueArray.Count(value => value > 0m) / (decimal)valueArray.Length, 4);
    }

    private enum PredictionGenerationScope
    {
        FullMlb,
        TheOdds
    }

    private sealed record PredictionCandidateProp(
        Guid Id,
        Guid PlayerId,
        Guid PropTypeId,
        string PropTypeName,
        DateTimeOffset StartsAt,
        decimal? SportsbookLine);

    private sealed record SideProbabilityEstimate(
        decimal? ProbabilityOver,
        decimal? ProbabilityUnder,
        decimal? ProbabilityPush,
        int PlayerSampleSize,
        int PriorSampleSize);

    private sealed record BatchPredictionHistoryContext(
        IReadOnlyDictionary<Guid, Guid[]> EquivalentPlayerIdsByPlayerId,
        IReadOnlyDictionary<Guid, Guid[]> EquivalentPropTypeIdsByPropTypeId,
        IReadOnlyDictionary<PlayerPropTypeKey, HistoricalResultRow[]> HistoricalResultsByPlayerPropType,
        IReadOnlyDictionary<Guid, HistoricalResultRow[]> HistoricalResultsByPropType,
        IReadOnlyCollection<HistoricalResultRow> HistoricalRows);

    private readonly record struct PlayerPropTypeKey(Guid PlayerId, Guid PropTypeId);

    private sealed record HistoricalResultRow(
        Guid PlayerId,
        Guid PropTypeId,
        DateOnly EventDate,
        decimal Value);

    private sealed record EmpiricalProbability(
        decimal? Over,
        decimal? Under,
        decimal? Push);

    private sealed record PredictionRecommendationDecision(
        string Recommendation,
        decimal? SelectedSideProbability,
        string ReasonCode);

    private sealed record PredictionRecommendationCandidate(
        string Recommendation,
        decimal ProjectionEdge,
        decimal? SelectedSideProbability,
        bool ProjectionGatePassed,
        bool HitProbabilityGatePassed,
        string ReasonCode);

    private sealed record TheOddsCandidatePlatformProp(
        Guid PlatformPropId,
        Guid PropId,
        decimal CurrentLine,
        DateTimeOffset ObservedAt,
        JsonDocument? Metadata,
        Guid PlayerId,
        Guid PropTypeId,
        string PropTypeName,
        DateTimeOffset StartsAt);
}
