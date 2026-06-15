namespace SportsBetting.Api.Predictions;

public interface IPredictionProbabilityService
{
    Task<PredictionProbabilityRunAnalysis> AnalyzeLatestRunAsync(CancellationToken cancellationToken);

    Task<PredictionProbabilityRunAnalysis> AnalyzeLatestRunForPropsAsync(
        IReadOnlyCollection<Guid> propIds,
        CancellationToken cancellationToken);

    Task<PredictionProbabilityRunAnalysis?> AnalyzeRunAsync(
        Guid runId,
        CancellationToken cancellationToken);

    Task<PredictionProbabilityRunAnalysis?> AnalyzeRunForPropsAsync(
        Guid runId,
        IReadOnlyCollection<Guid> propIds,
        CancellationToken cancellationToken);
}

public sealed record PredictionProbabilityRunAnalysis(
    Guid RunId,
    string ModelName,
    string ModelVersion,
    DateTimeOffset CreatedAt,
    int PredictionCount,
    IReadOnlyCollection<PredictionProbabilityResult> Results);

public sealed record PredictionProbabilityResult(
    Guid PredictionId,
    Guid PropId,
    Guid PlayerId,
    string PlayerName,
    Guid PropTypeId,
    string PropType,
    DateOnly EventDate,
    decimal? PredictedValue,
    decimal? SportsbookLine,
    decimal? Edge,
    string Recommendation,
    decimal Confidence,
    decimal? ProbabilityOver,
    decimal? ProbabilityUnder,
    decimal? ProbabilityPush,
    decimal ConfidenceScore,
    decimal RankingScore,
    int SampleSize,
    string MethodUsed);
