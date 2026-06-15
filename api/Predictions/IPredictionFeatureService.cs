namespace SportsBetting.Api.Predictions;

public interface IPredictionFeatureService
{
    Task<PlayerPredictionFeatures> GeneratePlayerFeaturesAsync(
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate,
        CancellationToken cancellationToken);

    Task<PlayerHistoricalLookupDiagnostic> AnalyzeHistoricalLookupAsync(
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate,
        CancellationToken cancellationToken);
}

public sealed record PlayerPredictionFeatures(
    Guid PlayerId,
    Guid PropTypeId,
    DateOnly EventDate,
    decimal? Last5GameAverage,
    decimal? Last10GameAverage,
    decimal? SeasonAverage,
    decimal? RollingStandardDeviation,
    decimal? HitRateLast5Games,
    decimal? HitRateLast10Games,
    int? DaysSinceLastGame,
    int HistoricalGamesAvailable,
    int SeasonGamesAvailable,
    string HitRateDefinition,
    string RollingStandardDeviationWindow);

public sealed record PlayerHistoricalLookupDiagnostic(
    Guid PlayerId,
    Guid PropTypeId,
    DateOnly EventDate,
    IReadOnlyCollection<Guid> EquivalentPlayerIds,
    IReadOnlyCollection<Guid> EquivalentPropTypeIds,
    int HistoricalGamesAvailable,
    int SeasonGamesAvailable,
    int HistoricalResultCountRecoveredByAlias,
    int HistoricalResultsForEquivalentPropTypes,
    int HistoricalResultsForEquivalentPlayers,
    int MlbMappingsForEquivalentPlayers,
    bool HasAliasResolution,
    bool HasMlbMappingForEquivalentPlayers,
    bool HasSupportedPropTypeHistory,
    bool HasHistoricalResultsForEquivalentPlayers);
