namespace SportsBetting.Api.Predictions;

public interface IPredictionGenerationService
{
    Task<PredictionGenerationResult> GenerateMlbBaselinePredictionsAsync(CancellationToken cancellationToken);

    Task<PredictionGenerationResult> GenerateTheOddsMlbBaselinePredictionsAsync(
        TheOddsMlbPredictionGenerationRequest request,
        CancellationToken cancellationToken);
}

public sealed record TheOddsMlbPredictionGenerationRequest(
    string? EventId,
    string? Bookmakers,
    string? Markets);

public sealed record PredictionGenerationResult(
    Guid RunId,
    string ModelName,
    string ModelVersion,
    int PropsEvaluated,
    int PredictionsGenerated,
    int SkippedProps,
    int RecommendationsSuppressedByThreshold,
    int RecommendationsSuppressedByPropTypeFilter,
    TimeSpan Duration,
    PredictionGenerationTimingDiagnostics TimingDiagnostics,
    string Scope);

public sealed record PredictionGenerationTimingDiagnostics(
    long TotalDurationMs,
    long LoadCandidatePropsMs,
    long FeatureGenerationMs,
    long ProbabilityOrRecommendationMs,
    long PersistenceMs,
    long SkippedReasonAggregationMs,
    long ResponseBuildMs);
