using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Diagnostics;
using SportsBetting.Api.Endpoints;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.Results.Soccer;

public sealed class SoccerResultImportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SoccerResultImportWorkerOptions> options,
    ILogger<SoccerResultImportWorker> logger) : BackgroundService
{
    internal const string Source = "api-sports-football";
    internal const string Competition = "World Cup";
    private static readonly DateOnly WorldCupStartDate = new(2026, 6, 11);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerOptions = options.Value;
        if (!workerOptions.Enabled)
        {
            logger.LogInformation("Soccer result import worker is disabled.");
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

    internal async Task RunImportCycleAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        logger.LogInformation("Soccer World Cup result import worker cycle started at {StartedAt}.", startedAt);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var apiSportsOptions = scope.ServiceProvider.GetRequiredService<IOptions<ApiSportsOptions>>().Value;
            var state = await GetOrCreateStateAsync(dbContext, cancellationToken);

            state.LastStartedAt = startedAt;
            state.LastStatus = "running";
            state.LastErrorMessage = null;
            state.UpdatedAt = startedAt;
            await dbContext.SaveChangesAsync(cancellationToken);

            var configError = ValidateApiSportsConfig(apiSportsOptions);
            if (configError is not null)
            {
                state.LastCompletedAt = DateTimeOffset.UtcNow;
                state.LastStatus = "not_configured";
                state.LastErrorMessage = configError;
                state.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Soccer World Cup result import worker skipped because API-Sports is not configured. Reason: {Reason}", configError);
                return;
            }

            var workerOptions = options.Value;
            if (!string.Equals(workerOptions.Competition, Competition, StringComparison.OrdinalIgnoreCase))
            {
                state.LastCompletedAt = DateTimeOffset.UtcNow;
                state.LastStatus = "not_configured";
                state.LastErrorMessage = $"Unsupported Soccer result import competition '{workerOptions.Competition}'. Only World Cup is wired.";
                state.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Soccer result import worker skipped unsupported competition {Competition}.", workerOptions.Competition);
                return;
            }

            var toDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var fromDate = toDate.AddDays(-Math.Max(0, workerOptions.RecentWindowDays));
            if (fromDate < WorldCupStartDate)
            {
                fromDate = WorldCupStartDate;
            }

            var client = scope.ServiceProvider.GetRequiredService<IApiSportsFootballClient>();
            var result = await ResultsDebugEndpoints.RunApiSportsWorldCupBackfillCompletedAsync(
                client,
                dbContext,
                fromDate,
                toDate,
                dryRun: false,
                maxCompletedFixtures: Math.Max(1, workerOptions.MaxFixturesPerRun),
                cancellationToken);

            await PersistFixtureOutcomesAsync(dbContext, result.ScheduledOutcomes, cancellationToken);
            var latestCompletedFixture = result.CompletedFixtures
                .Where(row => row.EventDate.HasValue)
                .OrderByDescending(row => row.EventDate)
                .ThenByDescending(row => row.KickoffUtc)
                .FirstOrDefault();
            var latestImportedFixture = result.Imports
                .Where(row => row.EventDate.HasValue)
                .OrderByDescending(row => row.EventDate)
                .ThenByDescending(row => row.KickoffUtc)
                .FirstOrDefault();
            var completedAt = DateTimeOffset.UtcNow;

            state.LastCompletedAt = completedAt;
            state.LastStatus = result.FailedFixtureCount > 0
                ? "partial_failure"
                : result.SkippedFixtureCount > 0
                    ? "completed_with_pending_fixtures"
                    : "succeeded";
            state.LastErrorMessage = result.Failures.FirstOrDefault()?.Message;
            state.LastSuccessfulRunAt = result.FailedFixtureCount == 0 ? completedAt : state.LastSuccessfulRunAt;
            state.LastSuccessfulImportDate = latestImportedFixture?.EventDate ?? state.LastSuccessfulImportDate;
            state.LatestCompletedFixtureDetectedId = latestCompletedFixture?.FixtureId;
            state.LatestCompletedFixtureDetectedDate = latestCompletedFixture?.EventDate;
            state.LatestCompletedFixtureImportedId = latestImportedFixture?.FixtureId ?? state.LatestCompletedFixtureImportedId;
            state.LatestCompletedFixtureImportedDate = latestImportedFixture?.EventDate ?? state.LatestCompletedFixtureImportedDate;
            state.LastApiCallsUsed = result.ApiCallsUsed;
            state.LastDiscoveredFixtureCount = result.DiscoveredFixtureCount;
            state.LastCompletedFixtureCount = result.CompletedFixtureCount;
            state.LastImportedFixtureCount = result.ImportedFixtureCount;
            state.LastFailedFixtureCount = result.FailedFixtureCount;
            state.LastSkippedPendingFixtureCount = result.SkippedFixtureCount;
            state.LastResultRowsWritten = result.ResultRowsWritten;
            state.LastResultRowsInserted = result.ResultRowsInserted;
            state.LastResultRowsUpdated = result.ResultRowsUpdated;
            state.UpdatedAt = completedAt;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Soccer World Cup result import worker cycle completed. Range: {FromDate}..{ToDate}; discovered: {Discovered}; completed: {Completed}; importedFixtures: {ImportedFixtures}; skippedPending: {SkippedPending}; failed: {Failed}; rowsWritten: {RowsWritten}; apiCallsUsed: {ApiCallsUsed}.",
                fromDate,
                toDate,
                result.DiscoveredFixtureCount,
                result.CompletedFixtureCount,
                result.ImportedFixtureCount,
                result.SkippedFixtureCount,
                result.FailedFixtureCount,
                result.ResultRowsWritten,
                result.ApiCallsUsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Soccer World Cup result import worker cycle failed.");
            await PersistWorkerExceptionAsync(exception, startedAt);
        }
    }

    internal static string? ValidateApiSportsConfig(ApiSportsOptions options)
    {
        if (!options.Enabled)
        {
            return "ApiSports:Enabled is false.";
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey)
            || string.Equals(options.ApiKey.Trim(), "API_SPORTS_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return "ApiSports:ApiKey is not configured.";
        }

        if (string.IsNullOrWhiteSpace(options.Football.BaseUrl))
        {
            return "ApiSports:Football:BaseUrl is not configured.";
        }

        return null;
    }

    internal static async Task<SoccerResultImportState> GetOrCreateStateAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.SoccerResultImportStates
            .SingleOrDefaultAsync(row => row.Source == Source && row.Competition == Competition, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new SoccerResultImportState
        {
            Id = Guid.NewGuid(),
            Source = Source,
            Competition = Competition
        };
        dbContext.SoccerResultImportStates.Add(state);
        await dbContext.SaveChangesAsync(cancellationToken);
        return state;
    }

    private static async Task PersistFixtureOutcomesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<ResultsDebugEndpoints.ApiSportsWorldCupScheduledFixtureOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        if (outcomes.Count == 0)
        {
            return;
        }

        var fixtureIds = outcomes.Select(row => row.FixtureId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = await dbContext.SoccerResultImportFixtureStates
            .Where(row => row.Source == Source
                && row.Competition == Competition
                && fixtureIds.Contains(row.FixtureId))
            .ToDictionaryAsync(row => row.FixtureId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var outcome in outcomes)
        {
            if (!existing.TryGetValue(outcome.FixtureId, out var state))
            {
                state = new SoccerResultImportFixtureState
                {
                    Id = Guid.NewGuid(),
                    Source = Source,
                    Competition = Competition,
                    FixtureId = outcome.FixtureId,
                    CreatedAt = now
                };
                dbContext.SoccerResultImportFixtureStates.Add(state);
                existing[outcome.FixtureId] = state;
            }

            state.EventDate = outcome.EventDate;
            state.KickoffUtc = outcome.KickoffUtc;
            state.HomeTeamName = outcome.HomeTeamName;
            state.AwayTeamName = outcome.AwayTeamName;
            state.StatusShort = outcome.StatusShort;
            state.StatusLong = outcome.StatusLong;
            state.ImportStatus = outcome.ImportStatus;
            state.SkipReason = outcome.SkipReason;
            state.ResultRowsPreviewed = outcome.ResultRowsPreviewed;
            state.ResultRowsWritten = outcome.ResultRowsWritten;
            state.ResultRowsInserted = outcome.ResultRowsInserted;
            state.ResultRowsUpdated = outcome.ResultRowsUpdated;
            state.LastErrorMessage = outcome.ErrorMessage;
            state.UpdatedAt = now;

            if (outcome.ImportStatus is "Imported" or "Failed")
            {
                state.Attempts++;
                state.LastAttemptedAt = now;
            }

            if (outcome.ImportStatus == "Imported")
            {
                state.LastSuccessfulAt = now;
            }
        }
    }

    private async Task PersistWorkerExceptionAsync(Exception exception, DateTimeOffset startedAt)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var errorLogger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await errorLogger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                nameof(SoccerResultImportWorker),
                nameof(SoccerResultImportWorker),
                "RunImportCycle",
                exception,
                Metadata: new { startedAt }));

            var state = await GetOrCreateStateAsync(dbContext, CancellationToken.None);
            state.LastCompletedAt = DateTimeOffset.UtcNow;
            state.LastStatus = "failed";
            state.LastErrorMessage = exception.Message;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception stateException)
        {
            logger.LogError(stateException, "Failed to persist Soccer World Cup result import worker failure state.");
        }
    }
}
