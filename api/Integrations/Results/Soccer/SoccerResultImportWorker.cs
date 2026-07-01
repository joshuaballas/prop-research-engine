using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Configuration;
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
        logger.LogInformation("Soccer API-Sports result import worker cycle started at {StartedAt}.", startedAt);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var apiSportsOptions = scope.ServiceProvider.GetRequiredService<IOptions<ApiSportsOptions>>().Value;
            var providerDomainConfigService = scope.ServiceProvider.GetRequiredService<IProviderDomainConfigService>();
            var configError = ValidateApiSportsConfig(apiSportsOptions);
            if (configError is not null)
            {
                await PersistNotConfiguredForSelectedCompetitionsAsync(
                    dbContext,
                    providerDomainConfigService,
                    apiSportsOptions,
                    startedAt,
                    configError,
                    cancellationToken);
                logger.LogWarning("Soccer API-Sports result import worker skipped because API-Sports is not configured. Reason: {Reason}", configError);
                return;
            }

            var workerOptions = options.Value;
            var competitions = await ResolveWorkerCompetitionsAsync(
                providerDomainConfigService,
                apiSportsOptions,
                workerOptions,
                cancellationToken);
            if (competitions.Count == 0)
            {
                var state = await GetOrCreateStateAsync(dbContext, Competition, cancellationToken);
                state.LastStartedAt = startedAt;
                state.LastCompletedAt = DateTimeOffset.UtcNow;
                state.LastStatus = "not_configured";
                state.LastErrorMessage = "No enabled Soccer API-Sports result import competitions matched the worker configuration.";
                state.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Soccer API-Sports result import worker skipped because no competitions matched {Competition} / {CompetitionKeys}.", workerOptions.Competition, workerOptions.CompetitionKeys);
                return;
            }

            var toDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var fromDate = toDate.AddDays(-Math.Max(0, workerOptions.RecentWindowDays));

            var client = scope.ServiceProvider.GetRequiredService<IApiSportsFootballClient>();
            var seasonResolver = scope.ServiceProvider.GetRequiredService<IApiSportsSoccerSeasonResolver>();
            var maxPerCompetition = Math.Max(1, workerOptions.MaxFixturesPerCompetitionPerRun ?? workerOptions.MaxFixturesPerRun);
            var remainingTotal = workerOptions.MaxFixturesTotalPerRun.GetValueOrDefault() > 0
                ? workerOptions.MaxFixturesTotalPerRun!.Value
                : int.MaxValue;
            var aggregate = new SoccerWorkerCycleAggregate();

            foreach (var competition in competitions)
            {
                if (remainingTotal <= 0)
                {
                    await PersistCompetitionSkippedByGlobalCapAsync(dbContext, competition, startedAt, cancellationToken);
                    continue;
                }

                var pendingBacklogRange = await LoadPendingCompletedFixtureBacklogRangeAsync(
                    dbContext,
                    competition,
                    cancellationToken);
                var requestedRange = pendingBacklogRange ?? new ApiSportsCompetitionDateRange(toDate, toDate);
                var seasonResolution = await seasonResolver.ResolveDateRangeAsync(
                    competition,
                    requestedRange.FromDate,
                    requestedRange.ToDate,
                    allowLiveMetadataRefresh: true,
                    nowUtc: startedAt,
                    cancellationToken);
                if (seasonResolution.SeasonWindows.Count == 0)
                {
                    await PersistCompetitionSkippedBySeasonResolutionAsync(
                        dbContext,
                        competition,
                        startedAt,
                        requestedRange.FromDate,
                        requestedRange.ToDate,
                        seasonResolution.SkippedReason ?? "SeasonMetadataUnavailable",
                        seasonResolution.SkippedMessage,
                        cancellationToken);
                    logger.LogInformation(
                        "Soccer API-Sports result import skipped for {Competition}. Range: {FromDate}..{ToDate}; reason: {Reason}.",
                        competition.Competition,
                        requestedRange.FromDate,
                        requestedRange.ToDate,
                        seasonResolution.SkippedReason);
                    continue;
                }

                foreach (var window in seasonResolution.SeasonWindows)
                {
                    if (remainingTotal <= 0)
                    {
                        await PersistCompetitionSkippedByGlobalCapAsync(dbContext, window.Competition, startedAt, cancellationToken);
                        break;
                    }

                    var maxForCompetition = Math.Min(maxPerCompetition, remainingTotal);
                    var importFromDate = pendingBacklogRange?.FromDate ?? fromDate;
                    var importToDate = pendingBacklogRange?.ToDate ?? toDate;
                    if (window.ProviderSeasonStartDate.HasValue && importFromDate < window.ProviderSeasonStartDate.Value)
                    {
                        importFromDate = window.ProviderSeasonStartDate.Value;
                    }

                    if (window.ProviderSeasonEndDate.HasValue && importToDate > window.ProviderSeasonEndDate.Value)
                    {
                        importToDate = window.ProviderSeasonEndDate.Value;
                    }

                    var state = await GetOrCreateStateAsync(dbContext, window.Competition.Competition, cancellationToken);
                    state.LastStartedAt = startedAt;
                    state.LastStatus = "running";
                    state.LastErrorMessage = null;
                    state.UpdatedAt = startedAt;
                    await dbContext.SaveChangesAsync(cancellationToken);

                    var result = await ResultsDebugEndpoints.RunApiSportsSoccerBackfillCompletedAsync(
                        client,
                        dbContext,
                        window.Competition,
                        importFromDate,
                        importToDate,
                        dryRun: false,
                        maxCompletedFixtures: maxForCompetition,
                        cancellationToken);

                    await PersistFixtureOutcomesAsync(dbContext, window.Competition.Competition, result.ScheduledOutcomes, cancellationToken);
                    await PersistCompetitionResultAsync(dbContext, state, result, cancellationToken);
                    remainingTotal -= result.SelectedCompletedFixtureCount;
                    aggregate.Add(result);

                    logger.LogInformation(
                        "Soccer API-Sports result import completed for {Competition}. Season: {Season}; Range: {FromDate}..{ToDate}; discovered: {Discovered}; completed: {Completed}; selectedCompleted: {SelectedCompleted}; alreadyImported: {AlreadyImported}; importedFixtures: {ImportedFixtures}; deferred: {Deferred}; skippedPending: {SkippedPending}; failed: {Failed}; rowsWritten: {RowsWritten}; apiCallsUsed: {ApiCallsUsed}.",
                        window.Competition.Competition,
                        window.Competition.Season,
                        importFromDate,
                        importToDate,
                        result.DiscoveredFixtureCount,
                        result.CompletedFixtureCount,
                        result.SelectedCompletedFixtureCount,
                        result.AlreadyImportedFixtureCount,
                        result.ImportedFixtureCount,
                        result.DeferredFixtureCount,
                        result.SkippedFixtureCount,
                        result.FailedFixtureCount,
                        result.ResultRowsWritten,
                        result.ApiCallsUsed);
                }
            }

            logger.LogInformation(
                "Soccer API-Sports result import worker cycle completed. Competitions: {CompetitionCount}; range: {FromDate}..{ToDate}; discovered: {Discovered}; completed: {Completed}; selectedCompleted: {SelectedCompleted}; alreadyImported: {AlreadyImported}; importedFixtures: {ImportedFixtures}; deferred: {Deferred}; skippedPending: {SkippedPending}; failed: {Failed}; rowsWritten: {RowsWritten}; apiCallsUsed: {ApiCallsUsed}.",
                competitions.Count,
                fromDate,
                toDate,
                aggregate.DiscoveredFixtureCount,
                aggregate.CompletedFixtureCount,
                aggregate.SelectedCompletedFixtureCount,
                aggregate.AlreadyImportedFixtureCount,
                aggregate.ImportedFixtureCount,
                aggregate.DeferredFixtureCount,
                aggregate.SkippedFixtureCount,
                aggregate.FailedFixtureCount,
                aggregate.ResultRowsWritten,
                aggregate.ApiCallsUsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Soccer API-Sports result import worker cycle failed.");
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
        => await GetOrCreateStateAsync(dbContext, Competition, cancellationToken);

    internal static async Task<SoccerResultImportState> GetOrCreateStateAsync(
        ApplicationDbContext dbContext,
        string competition,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.SoccerResultImportStates
            .SingleOrDefaultAsync(row => row.Source == Source && row.Competition == competition, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new SoccerResultImportState
        {
            Id = Guid.NewGuid(),
            Source = Source,
            Competition = competition
        };
        dbContext.SoccerResultImportStates.Add(state);
        await dbContext.SaveChangesAsync(cancellationToken);
        return state;
    }

    internal static async Task PersistFixtureOutcomesAsync(
        ApplicationDbContext dbContext,
        string competition,
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
                && row.Competition == competition
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
                    Competition = competition,
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

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<ApiSportsSoccerCompetitionDescriptor>> ResolveWorkerCompetitionsAsync(
        IProviderDomainConfigService providerDomainConfigService,
        ApiSportsOptions apiSportsOptions,
        SoccerResultImportWorkerOptions workerOptions,
        CancellationToken cancellationToken)
    {
        var supported = await providerDomainConfigService.GetApiSportsSoccerCompetitionsAsync(
            apiSportsOptions,
            enabledOnly: true,
            cancellationToken);
        var requestedKeys = workerOptions.CompetitionKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .ToArray();
        if (requestedKeys.Length > 0)
        {
            return supported
                .Where(competition => requestedKeys.Any(key => MatchesCompetition(competition, key)))
                .ToArray();
        }

        var requestedCompetition = workerOptions.Competition?.Trim();
        if (string.IsNullOrWhiteSpace(requestedCompetition)
            || string.Equals(requestedCompetition, "All", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requestedCompetition, "AllSupported", StringComparison.OrdinalIgnoreCase))
        {
            return supported;
        }

        return supported
            .Where(competition => MatchesCompetition(competition, requestedCompetition))
            .ToArray();
    }

    private static bool MatchesCompetition(ApiSportsSoccerCompetitionDescriptor competition, string requested)
    {
        return string.Equals(competition.Key, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(competition.Competition, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(competition.DisplayName, requested, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ApiSportsCompetitionDateRange?> LoadPendingCompletedFixtureBacklogRangeAsync(
        ApplicationDbContext dbContext,
        ApiSportsSoccerCompetitionDescriptor competition,
        CancellationToken cancellationToken)
    {
        var pendingDates = await dbContext.SoccerResultImportFixtureStates
            .AsNoTracking()
            .Where(row => row.Source == Source
                && row.Competition == competition.Competition
                && row.EventDate.HasValue
                && row.StatusShort != null
                && (row.StatusShort == "FT" || row.StatusShort == "AET" || row.StatusShort == "PEN")
                && row.ImportStatus != "Imported")
            .Select(row => row.EventDate!.Value)
            .ToArrayAsync(cancellationToken);

        return pendingDates.Length == 0
            ? null
            : new ApiSportsCompetitionDateRange(pendingDates.Min(), pendingDates.Max());
    }

    private static async Task PersistNotConfiguredForSelectedCompetitionsAsync(
        ApplicationDbContext dbContext,
        IProviderDomainConfigService providerDomainConfigService,
        ApiSportsOptions apiSportsOptions,
        DateTimeOffset startedAt,
        string configError,
        CancellationToken cancellationToken)
    {
        var competitions = await providerDomainConfigService.GetApiSportsSoccerCompetitionsAsync(
            apiSportsOptions,
            enabledOnly: true,
            cancellationToken);
        if (competitions.Count == 0)
        {
            competitions = [ResultsDebugEndpoints.GetApiSportsWorldCupCompetitionDescriptor()];
        }

        foreach (var competition in competitions)
        {
            var state = await GetOrCreateStateAsync(dbContext, competition.Competition, cancellationToken);
            state.LastStartedAt = startedAt;
            state.LastCompletedAt = DateTimeOffset.UtcNow;
            state.LastStatus = "not_configured";
            state.LastErrorMessage = configError;
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task PersistCompetitionSkippedByGlobalCapAsync(
        ApplicationDbContext dbContext,
        ApiSportsSoccerCompetitionDescriptor competition,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var state = await GetOrCreateStateAsync(dbContext, competition.Competition, cancellationToken);
        state.LastStartedAt = startedAt;
        state.LastCompletedAt = DateTimeOffset.UtcNow;
        state.LastStatus = "completed_with_pending_fixtures";
        state.LastErrorMessage = "Skipped this cycle because SoccerResultImportWorker:MaxFixturesTotalPerRun was exhausted by earlier competitions.";
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task PersistCompetitionSkippedBySeasonResolutionAsync(
        ApplicationDbContext dbContext,
        ApiSportsSoccerCompetitionDescriptor competition,
        DateTimeOffset startedAt,
        DateOnly requestedFromDate,
        DateOnly requestedToDate,
        string skippedReason,
        string? skippedMessage,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var state = await GetOrCreateStateAsync(dbContext, competition.Competition, cancellationToken);
        var isExpectedSkip = string.Equals(skippedReason, "OutsideProviderSeasonWindow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(skippedReason, "SeasonMetadataUnavailable", StringComparison.OrdinalIgnoreCase);
        state.LastStartedAt = startedAt;
        state.LastCompletedAt = completedAt;
        state.LastStatus = string.Equals(skippedReason, "OutsideProviderSeasonWindow", StringComparison.OrdinalIgnoreCase)
            ? "SkippedOutsideSeasonWindow"
            : string.Equals(skippedReason, "SeasonMetadataUnavailable", StringComparison.OrdinalIgnoreCase)
                ? "SkippedSeasonMetadataUnavailable"
                : "skipped";
        state.LastErrorMessage = isExpectedSkip
            ? null
            : $"{skippedReason}: requested {requestedFromDate:yyyy-MM-dd}..{requestedToDate:yyyy-MM-dd}. {skippedMessage}".Trim();
        state.LastApiCallsUsed = 0;
        state.LastDiscoveredFixtureCount = 0;
        state.LastCompletedFixtureCount = 0;
        state.LastImportedFixtureCount = 0;
        state.LastFailedFixtureCount = 0;
        state.LastSkippedPendingFixtureCount = 0;
        state.LastResultRowsWritten = 0;
        state.LastResultRowsInserted = 0;
        state.LastResultRowsUpdated = 0;
        state.UpdatedAt = completedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task PersistCompetitionResultAsync(
        ApplicationDbContext dbContext,
        SoccerResultImportState state,
        ResultsDebugEndpoints.ApiSportsWorldCupBackfillCompletedResult result,
        CancellationToken cancellationToken)
    {
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
            : result.SkippedFixtureCount > 0 || result.DeferredFixtureCount > 0
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
        state.LastSkippedPendingFixtureCount = result.SkippedFixtureCount + result.DeferredFixtureCount;
        state.LastResultRowsWritten = result.ResultRowsWritten;
        state.LastResultRowsInserted = result.ResultRowsInserted;
        state.LastResultRowsUpdated = result.ResultRowsUpdated;
        state.UpdatedAt = completedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
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
                Metadata: new { startedAt },
                Sport: "SOCCER",
                Provider: Source));

            var state = await GetOrCreateStateAsync(dbContext, CancellationToken.None);
            state.LastCompletedAt = DateTimeOffset.UtcNow;
            state.LastStatus = "failed";
            state.LastErrorMessage = exception.Message;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception stateException)
        {
            logger.LogError(stateException, "Failed to persist Soccer API-Sports result import worker failure state.");
        }
    }

    private sealed class SoccerWorkerCycleAggregate
    {
        public int DiscoveredFixtureCount { get; private set; }
        public int CompletedFixtureCount { get; private set; }
        public int SelectedCompletedFixtureCount { get; private set; }
        public int AlreadyImportedFixtureCount { get; private set; }
        public int ImportedFixtureCount { get; private set; }
        public int DeferredFixtureCount { get; private set; }
        public int SkippedFixtureCount { get; private set; }
        public int FailedFixtureCount { get; private set; }
        public int ResultRowsWritten { get; private set; }
        public int ApiCallsUsed { get; private set; }

        public void Add(ResultsDebugEndpoints.ApiSportsWorldCupBackfillCompletedResult result)
        {
            DiscoveredFixtureCount += result.DiscoveredFixtureCount;
            CompletedFixtureCount += result.CompletedFixtureCount;
            SelectedCompletedFixtureCount += result.SelectedCompletedFixtureCount;
            AlreadyImportedFixtureCount += result.AlreadyImportedFixtureCount;
            ImportedFixtureCount += result.ImportedFixtureCount;
            DeferredFixtureCount += result.DeferredFixtureCount;
            SkippedFixtureCount += result.SkippedFixtureCount;
            FailedFixtureCount += result.FailedFixtureCount;
            ResultRowsWritten += result.ResultRowsWritten;
            ApiCallsUsed += result.ApiCallsUsed;
        }
    }

    private readonly record struct ApiSportsCompetitionDateRange(DateOnly FromDate, DateOnly ToDate);
}
