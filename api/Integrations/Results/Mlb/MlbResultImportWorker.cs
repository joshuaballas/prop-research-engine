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

            await SeedSucceededDatesFromExistingResultsAsync(dbContext, options.Value, cancellationToken);

            var workerOptions = options.Value;
            var latestCompletedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var recentWindowStartDate = GetRecentWindowStartDate(latestCompletedDate, workerOptions);
            var coverageStart = workerOptions.HistoricalBackfillEnabled
                ? await GetHistoricalCoverageStartAsync(dbContext, cancellationToken) ?? recentWindowStartDate
                : recentWindowStartDate;
            await DetectMissingDatesAsync(dbContext, coverageStart, latestCompletedDate, cancellationToken);

            var maxBackfillDates = Math.Max(1, workerOptions.MaxBackfillDatesPerRun);
            var candidateDateStates = await dbContext.MlbResultImportDateStates
                .Where(dateState => dateState.EventDate <= latestCompletedDate
                    && dateState.CoverageStatus != "Complete"
                    && dateState.CoverageStatus != "NoGames")
                .ToListAsync(cancellationToken);
            var selectionRows = BuildSelectionRows(candidateDateStates, latestCompletedDate, workerOptions);
            var selectedDates = selectionRows
                .Where(row => row.IsEligible)
                .OrderBy(row => row.IsHistorical)
                .ThenByDescending(row => row.EventDate)
                .ThenBy(row => row.Attempts)
                .Take(maxBackfillDates)
                .ToArray();
            var selectedDateKeys = selectedDates.Select(row => row.EventDate).ToHashSet();
            var selectedReasonByDate = selectedDates.ToDictionary(row => row.EventDate, row => row.SelectedReason);
            var datesToProcess = candidateDateStates
                .Where(dateState => selectedDateKeys.Contains(dateState.EventDate))
                .OrderBy(dateState => dateState.EventDate < recentWindowStartDate)
                .ThenByDescending(dateState => dateState.EventDate)
                .ThenBy(dateState => dateState.Attempts)
                .ToArray();

            var outcomes = new List<MlbResultImportWorkerDateOutcome>();

            foreach (var dateState in datesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                outcomes.Add(await ProcessDateAsync(
                    importService,
                    dbContext,
                    state,
                    dateState,
                    selectedReasonByDate.GetValueOrDefault(dateState.EventDate) ?? BuildSelectedReason(dateState, latestCompletedDate, workerOptions),
                    cancellationToken));
            }

            var datesComplete = outcomes.Count(outcome => outcome.OutcomeCategory == MlbResultImportWorkerOutcomeCategory.Complete);
            var datesPartial = outcomes.Count(outcome => outcome.OutcomeCategory == MlbResultImportWorkerOutcomeCategory.Partial);
            var datesPending = outcomes.Count(outcome => outcome.OutcomeCategory == MlbResultImportWorkerOutcomeCategory.PendingGames);
            var datesNoGames = outcomes.Count(outcome => outcome.OutcomeCategory == MlbResultImportWorkerOutcomeCategory.NoGames);
            var datesSkipped = outcomes.Count(outcome => outcome.OutcomeCategory == MlbResultImportWorkerOutcomeCategory.Skipped);
            var datesFailed = outcomes.Count(outcome => outcome.OutcomeCategory == MlbResultImportWorkerOutcomeCategory.Failed);
            var datesNonFatal = datesPartial + datesPending + datesNoGames + datesSkipped;
            var completedAt = DateTimeOffset.UtcNow;
            state.LastCompletedAt = completedAt;
            state.LastStatus = datesFailed > 0
                ? "partial_failure"
                : datesNonFatal > 0
                    ? "completed_with_coverage_gaps"
                    : "succeeded";
            state.UpdatedAt = completedAt;

            if (datesFailed == 0)
            {
                state.LastSuccessfulRunAt = completedAt;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "MLB result import worker cycle completed. Dates processed: {DatesProcessed}; complete: {DatesComplete}; partial: {DatesPartial}; pending: {DatesPending}; noGames: {DatesNoGames}; skipped: {DatesSkipped}; failed: {DatesFailed}; historicalBackfillEnabled: {HistoricalBackfillEnabled}; recentWindowDays: {RecentWindowDays}; skippedOldHistoricalDisabled: {SkippedOldHistoricalDisabled}; skippedOldAttemptCap: {SkippedOldAttemptCap}; selectedDates: {SelectedDates}.",
                datesToProcess.Length,
                datesComplete,
                datesPartial,
                datesPending,
                datesNoGames,
                datesSkipped,
                datesFailed,
                workerOptions.HistoricalBackfillEnabled,
                Math.Max(1, workerOptions.RecentWindowDays),
                selectionRows.Count(row => string.Equals(row.SkippedReason, "HistoricalBackfillDisabled", StringComparison.OrdinalIgnoreCase)),
                selectionRows.Count(row => string.Equals(row.SkippedReason, "HistoricalAttemptCapReached", StringComparison.OrdinalIgnoreCase)),
                string.Join(",", datesToProcess.Select(row => row.EventDate.ToString("yyyy-MM-dd"))));
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

    private async Task<MlbResultImportWorkerDateOutcome> ProcessDateAsync(
        IMlbResultImportService importService,
        ApplicationDbContext dbContext,
        MlbResultImportState state,
        MlbResultImportDateState dateState,
        string selectedReason,
        CancellationToken cancellationToken)
    {
        var beforeCoverageStatus = dateState.CoverageStatus;
        var beforeStatus = dateState.Status;

        try
        {
            var result = await importService.ImportAsync(dateState.EventDate, cancellationToken);
            var completedAt = DateTimeOffset.UtcNow;

            dateState.RecordsFound = result.RecordsFound;
            dateState.RecordsMapped = result.RecordsMapped;
            dateState.RecordsInserted = result.RecordsInserted;
            dateState.RecordsUpdated = result.RecordsUpdated;
            dateState.UpdatedAt = completedAt;

            if (dateState.CoverageStatus == "Complete" && (!state.LastSuccessfulImportDate.HasValue || result.Date > state.LastSuccessfulImportDate.Value))
            {
                state.LastSuccessfulImportDate = result.Date;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            var outcome = ClassifyDateOutcome(dateState.CoverageStatus, dateState.Status, result.RecordsFound, exceptionOccurred: false);
            LogDateOutcome(dateState, selectedReason, beforeCoverageStatus, beforeStatus, outcome, null);
            return new MlbResultImportWorkerDateOutcome(
                dateState.EventDate,
                selectedReason,
                beforeCoverageStatus,
                beforeStatus,
                dateState.CompletedGamesExpected,
                dateState.CompletedGamesImported,
                result.RecordsFound,
                result.RecordsMapped,
                dateState.ImportedResults,
                dateState.DistinctPlayers,
                dateState.DistinctPropTypes,
                dateState.CoverageStatus,
                outcome,
                null,
                null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MLB result import failed for {EventDate}.", dateState.EventDate);
            await PersistWorkerExceptionAsync(exception, "ProcessDate", new
            {
                dateState.EventDate,
                dateState.Attempts,
                attemptedAt = DateTimeOffset.UtcNow
            });
            dateState.Status = "failed";
            dateState.CoverageStatus = "Failed";
            dateState.CoverageReason = "Latest import attempt failed.";
            dateState.LastErrorMessage = exception.Message;
            dateState.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            LogDateOutcome(dateState, selectedReason, beforeCoverageStatus, beforeStatus, MlbResultImportWorkerOutcomeCategory.Failed, exception);
            return new MlbResultImportWorkerDateOutcome(
                dateState.EventDate,
                selectedReason,
                beforeCoverageStatus,
                beforeStatus,
                dateState.CompletedGamesExpected,
                dateState.CompletedGamesImported,
                dateState.RecordsFound,
                dateState.RecordsMapped,
                dateState.ImportedResults,
                dateState.DistinctPlayers,
                dateState.DistinctPropTypes,
                dateState.CoverageStatus,
                MlbResultImportWorkerOutcomeCategory.Failed,
                exception.GetType().Name,
                exception.Message);
        }
    }

    internal static MlbResultImportWorkerOutcomeCategory ClassifyDateOutcome(
        string? coverageStatus,
        string? importStatus,
        int recordsFound,
        bool exceptionOccurred)
    {
        if (exceptionOccurred || string.Equals(coverageStatus, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return MlbResultImportWorkerOutcomeCategory.Failed;
        }

        if (string.Equals(coverageStatus, "Complete", StringComparison.OrdinalIgnoreCase))
        {
            return MlbResultImportWorkerOutcomeCategory.Complete;
        }

        if (string.Equals(coverageStatus, "Partial", StringComparison.OrdinalIgnoreCase))
        {
            return MlbResultImportWorkerOutcomeCategory.Partial;
        }

        if (string.Equals(coverageStatus, "PendingGames", StringComparison.OrdinalIgnoreCase))
        {
            return MlbResultImportWorkerOutcomeCategory.PendingGames;
        }

        if (string.Equals(coverageStatus, "NoGames", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(coverageStatus, "NotStarted", StringComparison.OrdinalIgnoreCase)
                && string.Equals(importStatus, "skipped", StringComparison.OrdinalIgnoreCase))
            || (string.Equals(coverageStatus, "NotStarted", StringComparison.OrdinalIgnoreCase) && recordsFound == 0))
        {
            return MlbResultImportWorkerOutcomeCategory.NoGames;
        }

        return MlbResultImportWorkerOutcomeCategory.Skipped;
    }

    private void LogDateOutcome(
        MlbResultImportDateState dateState,
        string selectedReason,
        string? beforeCoverageStatus,
        string? beforeStatus,
        MlbResultImportWorkerOutcomeCategory outcome,
        Exception? exception)
    {
        var logLevel = outcome == MlbResultImportWorkerOutcomeCategory.Failed ? LogLevel.Error : LogLevel.Information;
        logger.Log(
            logLevel,
            exception,
            "MLB result import worker date outcome. Date: {EventDate}; selectedReason: {SelectedReason}; beforeCoverageStatus: {BeforeCoverageStatus}; beforeStatus: {BeforeStatus}; gamesScheduled: {GamesScheduled}; gamesFinal: {GamesFinal}; recordsFound: {RecordsFound}; recordsMapped: {RecordsMapped}; importedRows: {ImportedRows}; distinctPlayers: {DistinctPlayers}; distinctPropTypes: {DistinctPropTypes}; afterCoverageStatus: {AfterCoverageStatus}; outcomeCategory: {OutcomeCategory}; error: {ErrorMessage}.",
            dateState.EventDate,
            selectedReason,
            beforeCoverageStatus,
            beforeStatus,
            dateState.CompletedGamesExpected,
            dateState.CompletedGamesImported,
            dateState.RecordsFound,
            dateState.RecordsMapped,
            dateState.ImportedResults,
            dateState.DistinctPlayers,
            dateState.DistinctPropTypes,
            dateState.CoverageStatus,
            outcome,
            dateState.LastErrorMessage);
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
        MlbResultImportWorkerOptions workerOptions,
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
                RecordsFound = group.Count(),
                DistinctPlayers = group.Select(result => result.PlayerId).Distinct().Count(),
                DistinctPropTypes = group.Select(result => result.PropTypeId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow;

        foreach (var resultDate in resultDates)
        {
            if (existingDateStates.TryGetValue(resultDate.EventDate, out var existingDateState))
            {
                existingDateState.ImportedResults = resultDate.RecordsFound;
                existingDateState.CoverageStatus = ResolveFallbackCoverageStatus(resultDate.RecordsFound, resultDate.DistinctPlayers, resultDate.DistinctPropTypes, workerOptions);
                existingDateState.CoverageReason = BuildFallbackCoverageReason(existingDateState.CoverageStatus, resultDate.RecordsFound, resultDate.DistinctPlayers, resultDate.DistinctPropTypes, workerOptions);
                existingDateState.DistinctPlayers = resultDate.DistinctPlayers;
                existingDateState.DistinctPropTypes = resultDate.DistinctPropTypes;
                existingDateState.Status = existingDateState.CoverageStatus == "Complete" ? "succeeded" : "partial";
                existingDateState.LastSuccessfulAt ??= observedAt;
                existingDateState.RecordsFound = Math.Max(existingDateState.RecordsFound, resultDate.RecordsFound);
                existingDateState.RecordsMapped = Math.Max(existingDateState.RecordsMapped, resultDate.RecordsFound);
                existingDateState.LastErrorMessage = null;
                existingDateState.UpdatedAt = observedAt;

                continue;
            }

            var dateState = new MlbResultImportDateState
            {
                Id = Guid.NewGuid(),
                EventDate = resultDate.EventDate,
                Status = ResolveFallbackCoverageStatus(resultDate.RecordsFound, resultDate.DistinctPlayers, resultDate.DistinctPropTypes, workerOptions) == "Complete" ? "succeeded" : "partial",
                Attempts = 0,
                LastSuccessfulAt = observedAt,
                RecordsFound = resultDate.RecordsFound,
                RecordsMapped = resultDate.RecordsFound,
                ImportedResults = resultDate.RecordsFound,
                DistinctPlayers = resultDate.DistinctPlayers,
                DistinctPropTypes = resultDate.DistinctPropTypes,
                CoverageStatus = ResolveFallbackCoverageStatus(resultDate.RecordsFound, resultDate.DistinctPlayers, resultDate.DistinctPropTypes, workerOptions),
                CoverageReason = BuildFallbackCoverageReason(ResolveFallbackCoverageStatus(resultDate.RecordsFound, resultDate.DistinctPlayers, resultDate.DistinctPropTypes, workerOptions), resultDate.RecordsFound, resultDate.DistinctPlayers, resultDate.DistinctPropTypes, workerOptions),
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
                    CoverageStatus = "NotStarted",
                    CoverageReason = "No persisted MLB prop_results exist for this completed date.",
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static DateOnly GetRecentWindowStartDate(DateOnly latestCompletedDate, MlbResultImportWorkerOptions workerOptions)
    {
        var recentWindowDays = Math.Max(1, workerOptions.RecentWindowDays);
        return latestCompletedDate.AddDays(-(recentWindowDays - 1));
    }

    internal static IReadOnlyList<MlbResultImportWorkerSelectionRow> BuildSelectionRows(
        IEnumerable<MlbResultImportDateState> dateStates,
        DateOnly latestCompletedDate,
        MlbResultImportWorkerOptions workerOptions)
    {
        var recentWindowStartDate = GetRecentWindowStartDate(latestCompletedDate, workerOptions);
        var attemptCap = Math.Max(0, workerOptions.HistoricalNonCompleteAttemptCap);

        return dateStates
            .Select(dateState =>
            {
                var isHistorical = dateState.EventDate < recentWindowStartDate;
                var selectedReason = BuildSelectedReason(dateState, latestCompletedDate, workerOptions);
                string? skippedReason = null;
                if (dateState.EventDate > latestCompletedDate)
                {
                    skippedReason = "FutureOrCurrentDateExcluded";
                }
                else if (isHistorical && !workerOptions.HistoricalBackfillEnabled)
                {
                    skippedReason = "HistoricalBackfillDisabled";
                }
                else if (isHistorical && attemptCap > 0 && dateState.Attempts >= attemptCap)
                {
                    skippedReason = "HistoricalAttemptCapReached";
                }

                return new MlbResultImportWorkerSelectionRow(
                    dateState.EventDate,
                    dateState.CoverageStatus,
                    dateState.Status,
                    dateState.Attempts,
                    isHistorical,
                    skippedReason is null,
                    selectedReason,
                    skippedReason);
            })
            .ToArray();
    }

    internal static string BuildSelectedReason(
        MlbResultImportDateState dateState,
        DateOnly latestCompletedDate,
        MlbResultImportWorkerOptions workerOptions)
    {
        if (dateState.EventDate > latestCompletedDate)
        {
            return "FutureOrCurrentDateExcluded";
        }

        var recentWindowStartDate = GetRecentWindowStartDate(latestCompletedDate, workerOptions);
        var prefix = dateState.EventDate < recentWindowStartDate ? "HistoricalBackfill" : "RecentWindow";

        var reason = dateState.CoverageStatus switch
        {
            "Partial" => "CoveragePartial",
            "NotStarted" => "CoverageNotStarted",
            "PendingGames" => "PendingGamesRecheck",
            "Failed" => "PreviousFailureRetry",
            _ => "CoverageNotComplete"
        };

        return $"{prefix}:{reason}";
    }

    private static string ResolveFallbackCoverageStatus(
        int importedResults,
        int distinctPlayers,
        int distinctPropTypes,
        MlbResultImportWorkerOptions options)
    {
        if (importedResults == 0)
        {
            return "NotStarted";
        }

        return importedResults >= options.MinimumCompleteImportedResults
            && distinctPlayers >= options.MinimumCompleteDistinctPlayers
            && distinctPropTypes >= options.MinimumCompleteDistinctPropTypes
                ? "Complete"
                : "Partial";
    }

    private static string BuildFallbackCoverageReason(
        string status,
        int importedResults,
        int distinctPlayers,
        int distinctPropTypes,
        MlbResultImportWorkerOptions options)
    {
        return status == "Complete"
            ? $"Fallback coverage thresholds met: importedResults={importedResults}, distinctPlayers={distinctPlayers}, distinctPropTypes={distinctPropTypes}."
            : $"Fallback coverage thresholds not met: importedResults={importedResults}/{options.MinimumCompleteImportedResults}, distinctPlayers={distinctPlayers}/{options.MinimumCompleteDistinctPlayers}, distinctPropTypes={distinctPropTypes}/{options.MinimumCompleteDistinctPropTypes}.";
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
                Metadata: metadata,
                Sport: "MLB",
                Provider: Source), CancellationToken.None);
        }
        catch (Exception loggingException)
        {
            logger.LogError(loggingException, "Failed to persist MLB result import worker exception.");
        }
    }
}

internal enum MlbResultImportWorkerOutcomeCategory
{
    Complete,
    Partial,
    PendingGames,
    NoGames,
    Skipped,
    Failed
}

internal sealed record MlbResultImportWorkerDateOutcome(
    DateOnly Date,
    string SelectedReason,
    string? BeforeCoverageStatus,
    string? BeforeStatus,
    int? GamesScheduled,
    int? GamesFinal,
    int RecordsFound,
    int RecordsMapped,
    int ImportedRows,
    int DistinctPlayers,
    int DistinctPropTypes,
    string AfterCoverageStatus,
    MlbResultImportWorkerOutcomeCategory OutcomeCategory,
    string? ExceptionType,
    string? ErrorMessage);

internal sealed record MlbResultImportWorkerSelectionRow(
    DateOnly EventDate,
    string CoverageStatus,
    string Status,
    int Attempts,
    bool IsHistorical,
    bool IsEligible,
    string SelectedReason,
    string? SkippedReason);
