using System.Diagnostics;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Diagnostics;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsMlbIngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TheOddsIngestionWorkerOptions> options,
    ITheOddsMlbIngestionWorkerState state,
    ILogger<TheOddsMlbIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            state.MarkDisabled(["worker disabled"]);
            logger.LogInformation("The Odds API MLB ingestion worker is disabled. No The Odds API calls will be made.");
            return;
        }

        var interval = options.Value.GetInterval();
        logger.LogWarning(
            "The Odds API MLB ingestion worker is enabled. DryRun: {DryRun}. Interval: {Interval}. First run will occur after the first interval.",
            options.Value.DryRun,
            interval);

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunPlanningCycleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("The Odds API MLB ingestion worker is stopping.");
        }
    }

    private async Task RunPlanningCycleAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var workerOptions = options.Value;
        state.MarkStarted(startedAt, workerOptions.DryRun);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var planner = scope.ServiceProvider.GetRequiredService<ITheOddsMlbIngestionPlanner>();
            var estimatedCreditsUsedToday = state.GetEstimatedCreditsUsedToday(DateOnly.FromDateTime(startedAt.UtcDateTime));
            var plan = await planner.BuildPlanAsync(workerOptions, estimatedCreditsUsedToday, cancellationToken);
            var skippedReasons = plan.SkippedReasons.ToList();

            if (plan.PlannedEventCount == 0 && plan.CandidateEvents > 0)
            {
                skippedReasons.Add("credit cap reached");
            }

            var status = plan.PlannedEventCount == 0 ? "skipped" : "planned";
            if (workerOptions.DryRun)
            {
                status = "dry_run";
            }

            stopwatch.Stop();
            state.MarkCompleted(
                DateTimeOffset.UtcNow,
                status,
                workerOptions.DryRun,
                plan.PlannedEvents,
                plan.CandidateEvents,
                plan.EstimatedCredits,
                skippedReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                consumeEstimatedCredits: false);

            logger.LogInformation(
                "The Odds API MLB ingestion worker planning cycle completed. Status: {Status}. DryRun: {DryRun}. CandidateEvents: {CandidateEvents}. PlannedEvents: {PlannedEvents}. EstimatedCredits: {EstimatedCredits}. Duration: {Duration}. SkippedReasons: {SkippedReasons}",
                status,
                workerOptions.DryRun,
                plan.CandidateEvents,
                plan.PlannedEventCount,
                plan.EstimatedCredits,
                stopwatch.Elapsed,
                string.Join("; ", skippedReasons));

            if (workerOptions.DryRun)
            {
                logger.LogInformation("The Odds API MLB ingestion worker is in dry-run mode; no The Odds API calls or odds writes were performed.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            state.MarkFailed(DateTimeOffset.UtcNow, workerOptions.DryRun, exception.Message);
            logger.LogError(exception, "The Odds API MLB ingestion worker planning cycle failed.");
            await PersistWorkerExceptionAsync(exception, startedAt, stopwatch.Elapsed);
        }
    }

    private async Task PersistWorkerExceptionAsync(Exception exception, DateTimeOffset startedAt, TimeSpan duration)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var errorLogger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await errorLogger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                "worker",
                nameof(TheOddsMlbIngestionWorker),
                "RunPlanningCycle",
                exception,
                CorrelationId: Guid.NewGuid().ToString("N"),
                Metadata: new
                {
                    startedAt,
                    duration = duration.ToString(),
                    options.Value.DryRun,
                    options.Value.Enabled
                },
                Sport: "MLB",
                Provider: "The Odds API"), CancellationToken.None);
        }
        catch (Exception loggingException)
        {
            logger.LogError(loggingException, "Failed to persist The Odds API MLB ingestion worker exception.");
        }
    }
}
