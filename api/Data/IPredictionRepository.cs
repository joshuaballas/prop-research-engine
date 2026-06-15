using SportsBetting.Api.Models;

namespace SportsBetting.Api.Data;

public interface IPredictionRepository
{
    Task<IReadOnlyList<PredictionRun>> GetLatestRunsAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Prediction>> GetLatestPredictionsAsync(int limit, CancellationToken cancellationToken);

    Task<PredictionRun?> GetRunByIdAsync(Guid runId, CancellationToken cancellationToken);

    Task AddPredictionRunAsync(PredictionRun predictionRun, CancellationToken cancellationToken);

    Task<PredictionRun> CreateSamplePredictionsAsync(CancellationToken cancellationToken);
}
