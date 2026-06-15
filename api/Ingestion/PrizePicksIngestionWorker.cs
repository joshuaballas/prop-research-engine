using System.Diagnostics;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Diagnostics;
using SportsBetting.Api.Integrations.PrizePicks;

namespace SportsBetting.Api.Ingestion;

public sealed class PrizePicksIngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PrizePicksIngestionOptions> options,
    IPrizePicksIngestionWorkerState state,
    ILogger<PrizePicksIngestionWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim runGate = new(1, 1);
    private readonly object currentRunLock = new();
    private Task? currentRun;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            state.MarkDisabled();
            logger.LogInformation("PrizePicks background ingestion worker is disabled.");
            return;
        }

        var interval = options.Value.GetInterval();
        logger.LogInformation(
            "PrizePicks background ingestion worker is enabled. Interval: {Interval}. First run will occur after the first interval.",
            interval);

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    if (!runGate.Wait(0))
                    {
                        var skippedAt = DateTimeOffset.UtcNow;
                        state.MarkSkipped(skippedAt);
                        logger.LogWarning(
                            "Skipping scheduled PrizePicks ingestion at {SkippedAt:O} because the previous run is still active.",
                            skippedAt);
                        continue;
                    }

                    var run = RunIngestionAsync(stoppingToken);
                    lock (currentRunLock)
                    {
                        currentRun = run;
                    }

                    _ = ObserveRunCompletionAsync(run);
                }
                catch (Exception exception)
                {
                    var failedAt = DateTimeOffset.UtcNow;
                    state.MarkFailed(failedAt, TimeSpan.Zero, exception.Message);
                    LogWorkerException(
                        exception,
                        "PrizePicks ingestion worker failed while scheduling a run.",
                        failedAt,
                        TimeSpan.Zero);
                    await PersistWorkerExceptionAsync(exception, "ScheduleRun", failedAt, TimeSpan.Zero);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("PrizePicks background ingestion worker is stopping.");
        }

        Task? runToAwait;
        lock (currentRunLock)
        {
            runToAwait = currentRun;
        }

        if (runToAwait is not null)
        {
            try
            {
                await runToAwait.WaitAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                var failedAt = DateTimeOffset.UtcNow;
                state.MarkFailed(failedAt, TimeSpan.Zero, exception.Message);
                LogWorkerException(
                    exception,
                    "PrizePicks ingestion worker observed a run failure while stopping.",
                    failedAt,
                    TimeSpan.Zero);
                await PersistWorkerExceptionAsync(exception, "StopObserveRun", failedAt, TimeSpan.Zero);
            }
        }
    }

    private async Task ObserveRunCompletionAsync(Task run)
    {
        try
        {
            await run;
        }
        catch (Exception exception)
        {
            var failedAt = DateTimeOffset.UtcNow;
            state.MarkFailed(failedAt, TimeSpan.Zero, exception.Message);
            LogWorkerException(
                exception,
                "PrizePicks ingestion worker observed an unhandled run failure.",
                failedAt,
                TimeSpan.Zero);
            await PersistWorkerExceptionAsync(exception, "ObserveRunCompletion", failedAt, TimeSpan.Zero);
        }
        finally
        {
            lock (currentRunLock)
            {
                if (ReferenceEquals(currentRun, run))
                {
                    currentRun = null;
                }
            }
        }
    }

    private async Task RunIngestionAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            state.MarkStarted(startedAt);

            logger.LogInformation("Starting scheduled PrizePicks ingestion at {StartedAt:O}.", startedAt);

            using var scope = scopeFactory.CreateScope();
            var ingestionService = scope.ServiceProvider.GetRequiredService<IPrizePicksIngestionService>();
            var result = await ingestionService.IngestAsync(cancellationToken);

            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;
            state.MarkSucceeded(completedAt, stopwatch.Elapsed);

            logger.LogInformation(
                "Completed scheduled PrizePicks ingestion at {CompletedAt:O}. Started: {StartedAt:O}. Duration: {Duration}. Success: {Success}. Projections: {ProjectionsProcessed}. Snapshots: {SnapshotsCreated}.",
                completedAt,
                startedAt,
                stopwatch.Elapsed,
                true,
                result.ProjectionsProcessed,
                result.SnapshotsCreated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;
            state.MarkCanceled(completedAt, stopwatch.Elapsed);
            logger.LogInformation(
                "Canceled scheduled PrizePicks ingestion at {CompletedAt:O}. Started: {StartedAt:O}. Duration: {Duration}.",
                completedAt,
                startedAt,
                stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;
            state.MarkFailed(completedAt, stopwatch.Elapsed, exception.Message);
            LogWorkerException(
                exception,
                "Scheduled PrizePicks ingestion failed.",
                completedAt,
                startedAt,
                stopwatch.Elapsed,
                false);
            await PersistWorkerExceptionAsync(exception, "RunIngestion", completedAt, stopwatch.Elapsed);
        }
        finally
        {
            runGate.Release();
        }
    }

    private void LogWorkerException(
        Exception exception,
        string message,
        DateTimeOffset failedAt,
        TimeSpan duration)
    {
        logger.LogError(
            exception,
            "{Message} FailedAt: {FailedAt:O}. Duration: {Duration}. ExceptionType: {ExceptionType}. ExceptionMessage: {ExceptionMessage}. StackTrace: {StackTrace}",
            message,
            failedAt,
            duration,
            exception.GetType().FullName,
            exception.Message,
            exception.StackTrace);
    }

    private async Task PersistWorkerExceptionAsync(
        Exception exception,
        string operation,
        DateTimeOffset failedAt,
        TimeSpan duration)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var errorLogger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await errorLogger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                "worker",
                nameof(PrizePicksIngestionWorker),
                operation,
                exception,
                StatusCode: null,
                CorrelationId: Guid.NewGuid().ToString("N"),
                Metadata: new
                {
                    failedAt,
                    duration = duration.ToString(),
                    platform = "PrizePicks"
                }), CancellationToken.None);
        }
        catch (Exception loggingException)
        {
            logger.LogError(loggingException, "Failed to persist PrizePicks worker exception.");
        }
    }

    private void LogWorkerException(
        Exception exception,
        string message,
        DateTimeOffset completedAt,
        DateTimeOffset startedAt,
        TimeSpan duration,
        bool success)
    {
        logger.LogError(
            exception,
            "{Message} CompletedAt: {CompletedAt:O}. StartedAt: {StartedAt:O}. Duration: {Duration}. Success: {Success}. ExceptionType: {ExceptionType}. ExceptionMessage: {ExceptionMessage}. StackTrace: {StackTrace}",
            message,
            completedAt,
            startedAt,
            duration,
            success,
            exception.GetType().FullName,
            exception.Message,
            exception.StackTrace);
    }
}
