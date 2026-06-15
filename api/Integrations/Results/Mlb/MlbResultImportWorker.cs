using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Diagnostics;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed class MlbResultImportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MlbResultImportWorkerOptions> options,
    ILogger<MlbResultImportWorker> logger) : BackgroundService
{
    private const string Source = "mlb";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerOptions = options.Value;
        if (!workerOptions.Enabled)
        {
            logger.LogInformation("MLB result import worker is disabled.");
            return;
        }

        await RunImportCycleAsync(stoppingToken);

        var interval = TimeSpan.FromMinutes(Math.Max(1, workerOptions.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunImportCycleAsync(stoppingToken);
        }
    }

    private async Task RunImportCycleAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        logger.LogInformation("MLB result import worker cycle started at {StartedAt}.", startedAt);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var importService = scope.ServiceProvider.GetRequiredService<IMlbResultImportService>();
            var state = await GetOrCreateStateAsync(dbContext, cancellationToken);

            state.LastStartedAt = startedAt;
            state.LastStatus = "running";
            state.LastErrorMessage = null;
            state.UpdatedAt = startedAt;
            await dbContext.SaveChangesAsync(cancellationToken);

            await SeedSucceededDatesFromExistingResultsAsync(dbContext, cancellationToken);

            var latestCompletedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var coverageStart = await GetHistoricalCoverageStartAsync(dbContext, cancellationToken) ?? latestCompletedDate;
            await DetectMissingDatesAsync(dbContext, coverageStart, latestCompletedDate, cancellationToken);

            var maxBackfillDates = Math.Max(1, options.Value.MaxBackfillDatesPerRun);
            var datesToProcess = await dbContext.MlbResultImportDateStates
                .Where(dateState => dateState.EventDate <= latestCompletedDate && dateState.Status != "succeeded")
                .OrderBy(dateState => dateState.EventDate)
                .ThenBy(dateState => dateState.Attempts)
                .Take(maxBackfillDates)
                .ToListAsync(cancellationToken);

            var datesSucceeded = 0;
            var datesFailed = 0;

            foreach (var dateState in datesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessDateAsync(importService, dbContext, state, dateState, cancellationToken);

                if (dateState.Status == "succeeded")
                {
                    datesSucceeded++;
                }
                else
                {
                    datesFailed++;
                }
            }

            var completedAt = DateTimeOffset.UtcNow;
            state.LastCompletedAt = completedAt;
            state.LastStatus = datesFailed == 0 ? "succeeded" : "partial_failure";
            state.UpdatedAt = completedAt;

            if (datesFailed == 0)
            {
                state.LastSuccessfulRunAt = completedAt;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "MLB result import worker cycle completed. Dates processed: {DatesProcessed}; succeeded: {DatesSucceeded}; failed: {DatesFailed}.",
                datesToProcess.Count,
                datesSucceeded,
                datesFailed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MLB result import worker cycle failed.");
            await PersistWorkerExceptionAsync(exception, "RunImportCycle", new
            {
                startedAt
            });

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var state = await GetOrCreateStateAsync(dbContext, CancellationToken.None);
                state.LastCompletedAt = DateTimeOffset.UtcNow;
                state.LastStatus = "failed";
                state.LastErrorMessage = exception.Message;
                state.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception stateException)
            {
                logger.LogError(stateException, "Failed to persist MLB result import worker failure state.");
                await PersistWorkerExceptionAsync(stateException, "PersistFailureState", new
                {
                    startedAt,
                    originalExceptionType = exception.GetType().FullName,
                    originalExceptionMessage = exception.Message
                });
            }
        }
    }

    private async Task ProcessDateAsync(
        IMlbResultImportService importService,
        ApplicationDbContext dbContext,
        MlbResultImportState state,
        MlbResultImportDateState dateState,
        CancellationToken cancellationToken)
    {
        var attemptedAt = DateTimeOffset.UtcNow;
        dateState.Status = "running";
        dateState.Attempts++;
        dateState.LastAttemptedAt = attemptedAt;
        dateState.LastErrorMessage = null;
        dateState.UpdatedAt = attemptedAt;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await importService.ImportAsync(dateState.EventDate, cancellationToken);
            var completedAt = DateTimeOffset.UtcNow;

            dateState.Status = "succeeded";
            dateState.LastSuccessfulAt = completedAt;
            dateState.RecordsFound = result.RecordsFound;
            dateState.RecordsMapped = result.RecordsMapped;
            dateState.RecordsInserted = result.RecordsInserted;
            dateState.RecordsUpdated = result.RecordsUpdated;
            dateState.UpdatedAt = completedAt;

            if (!state.LastSuccessfulImportDate.HasValue || result.Date > state.LastSuccessfulImportDate.Value)
            {
                state.LastSuccessfulImportDate = result.Date;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MLB result import failed for {EventDate}.", dateState.EventDate);
            await PersistWorkerExceptionAsync(exception, "ProcessDate", new
            {
                dateState.EventDate,
                dateState.Attempts,
                attemptedAt
            });
            dateState.Status = "failed";
            dateState.LastErrorMessage = exception.Message;
            dateState.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<MlbResultImportState> GetOrCreateStateAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.MlbResultImportStates
            .SingleOrDefaultAsync(importState => importState.Source == Source, cancellationToken);

        if (state is not null)
        {
            return state;
        }

        state = new MlbResultImportState
        {
            Id = Guid.NewGuid(),
            Source = Source,
            LastStatus = "never_run",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.MlbResultImportStates.Add(state);
        await dbContext.SaveChangesAsync(cancellationToken);

        return state;
    }

    private static async Task SeedSucceededDatesFromExistingResultsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var existingDateStates = await dbContext.MlbResultImportDateStates
            .ToDictionaryAsync(dateState => dateState.EventDate, cancellationToken);
        var resultDates = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.ExternalSource == Source)
            .GroupBy(result => result.EventDate)
            .Select(group => new
            {
                EventDate = group.Key,
                RecordsFound = group.Count()
            })
            .ToListAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow;

        foreach (var resultDate in resultDates)
        {
            if (existingDateStates.TryGetValue(resultDate.EventDate, out var existingDateState))
            {
                if (existingDateState.Status != "succeeded")
                {
                    existingDateState.Status = "succeeded";
                    existingDateState.LastSuccessfulAt = observedAt;
                    existingDateState.RecordsFound = resultDate.RecordsFound;
                    existingDateState.RecordsMapped = resultDate.RecordsFound;
                    existingDateState.LastErrorMessage = null;
                    existingDateState.UpdatedAt = observedAt;
                }

                continue;
            }

            var dateState = new MlbResultImportDateState
            {
                Id = Guid.NewGuid(),
                EventDate = resultDate.EventDate,
                Status = "succeeded",
                Attempts = 0,
                LastSuccessfulAt = observedAt,
                RecordsFound = resultDate.RecordsFound,
                RecordsMapped = resultDate.RecordsFound,
                CreatedAt = observedAt,
                UpdatedAt = observedAt
            };
            dbContext.MlbResultImportDateStates.Add(dateState);
            existingDateStates[resultDate.EventDate] = dateState;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task DetectMissingDatesAsync(
        ApplicationDbContext dbContext,
        DateOnly coverageStart,
        DateOnly latestCompletedDate,
        CancellationToken cancellationToken)
    {
        if (coverageStart > latestCompletedDate)
        {
            return;
        }

        var knownDates = await dbContext.MlbResultImportDateStates
            .Where(dateState => dateState.EventDate >= coverageStart && dateState.EventDate <= latestCompletedDate)
            .Select(dateState => dateState.EventDate)
            .ToListAsync(cancellationToken);
        var knownDateSet = knownDates.ToHashSet();
        var observedAt = DateTimeOffset.UtcNow;
        var currentDate = coverageStart;

        while (currentDate <= latestCompletedDate)
        {
            if (!knownDateSet.Contains(currentDate))
            {
                dbContext.MlbResultImportDateStates.Add(new MlbResultImportDateState
                {
                    Id = Guid.NewGuid(),
                    EventDate = currentDate,
                    Status = "pending",
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<DateOnly?> GetHistoricalCoverageStartAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.ExternalSource == Source)
            .MinAsync(result => (DateOnly?)result.EventDate, cancellationToken);
    }

    private async Task PersistWorkerExceptionAsync(
        Exception exception,
        string operation,
        object metadata)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var errorLogger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await errorLogger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                "worker",
                nameof(MlbResultImportWorker),
                operation,
                exception,
                CorrelationId: Guid.NewGuid().ToString("N"),
                Metadata: metadata), CancellationToken.None);
        }
        catch (Exception loggingException)
        {
            logger.LogError(loggingException, "Failed to persist MLB result import worker exception.");
        }
    }
}
