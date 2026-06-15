using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Data;

public sealed class PredictionRepository(ApplicationDbContext dbContext) : IPredictionRepository
{
    public async Task<IReadOnlyList<PredictionRun>> GetLatestRunsAsync(int limit, CancellationToken cancellationToken)
    {
        return await dbContext.PredictionRuns
            .AsNoTracking()
            .Include(run => run.Predictions)
            .Include(run => run.SkipCounts)
            .OrderByDescending(run => run.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Prediction>> GetLatestPredictionsAsync(int limit, CancellationToken cancellationToken)
    {
        return await dbContext.Predictions
            .AsNoTracking()
            .Include(prediction => prediction.PredictionRun)
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.Player)
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.League)
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.PropType)
            .OrderByDescending(prediction => prediction.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<PredictionRun?> GetRunByIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        return dbContext.PredictionRuns
            .AsNoTracking()
            .Include(run => run.SkipCounts)
            .Include(run => run.Predictions)
            .ThenInclude(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.Player)
            .Include(run => run.Predictions)
            .ThenInclude(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.League)
            .Include(run => run.Predictions)
            .ThenInclude(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.PropType)
            .SingleOrDefaultAsync(run => run.Id == runId, cancellationToken);
    }

    public async Task AddPredictionRunAsync(PredictionRun predictionRun, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.PredictionRuns.Add(predictionRun);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PredictionRun> CreateSamplePredictionsAsync(CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var props = await dbContext.Props
            .Include(prop => prop.Player)
            .Include(prop => prop.League)
            .Include(prop => prop.PropType)
            .OrderByDescending(prop => prop.UpdatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        var predictionRun = new PredictionRun
        {
            Id = Guid.NewGuid(),
            ModelName = "SampleModel",
            ModelVersion = "1.0",
            CreatedAt = observedAt,
            CompletedAt = observedAt,
            Notes = "Debug sample prediction run with fake predictions."
        };

        foreach (var prop in props)
        {
            predictionRun.Predictions.Add(new Prediction
            {
                Id = Guid.NewGuid(),
                PredictionRunId = predictionRun.Id,
                PropId = prop.Id,
                PredictedProbability = 0.62m,
                PredictedValue = null,
                Recommendation = "OVER",
                Confidence = 0.75m,
                CreatedAt = observedAt,
                Prop = prop,
                PredictionRun = predictionRun
            });
        }

        dbContext.PredictionRuns.Add(predictionRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        return predictionRun;
    }
}
