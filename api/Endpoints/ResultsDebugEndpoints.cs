using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;
using SportsBetting.Api.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SportsBetting.Api.Endpoints;

public static class ResultsDebugEndpoints
{
    private const string ApiSportsFootballResultSource = "api-sports-football";
    private const string ApiSportsFootballPlayerRefProvider = "api-sports-football";
    private const string TheOddsSoccerAliasProviderKey = "the-odds-soccer";
    private const int ApiSportsWorldCupLeagueId = 1;
    private const int ApiSportsWorldCupSeason = 2026;
    private static readonly DateOnly ApiSportsWorldCup2026StartDate = new(2026, 6, 11);
    private static readonly string[] ApiSportsHistoricalStatKeysOfInterest = ["GOALS", "ASSISTS", "SHOTS", "SHOTS_ON_TARGET"];
    private sealed record MlbLatestWorkerCycleDateOutcome(
        DateOnly EventDate,
        string SelectedReason,
        string Status,
        int? CompletedGamesExpected,
        int? CompletedGamesImported,
        int RecordsFound,
        int RecordsMapped,
        int ImportedResults,
        int DistinctPlayers,
        int DistinctPropTypes,
        string CoverageStatus,
        string? CoverageReason,
        string OutcomeCategory,
        DateTimeOffset? LastAttemptedAt,
        DateTimeOffset? LastSuccessfulAt,
        string? LastErrorMessage);

    public static IEndpointRouteBuilder MapResultsDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/results", async (
            IPropResultRepository resultRepository,
            CancellationToken cancellationToken) =>
        {
            var results = await resultRepository.GetLatestAsync(100, cancellationToken);

            return Results.Ok(new
            {
                resultCount = results.Count,
                results = results.Select(ToResponse).ToArray()
            });
        })
        .WithName("ResultsDebug")
        .WithSummary("Returns stored prop results.");

        endpoints.MapGet("/api/debug/results/{id:guid}", async (
            Guid id,
            IPropResultRepository resultRepository,
            CancellationToken cancellationToken) =>
        {
            var result = await resultRepository.GetByIdAsync(id, cancellationToken);

            return result is null
                ? Results.NotFound(new { error = $"Result '{id}' was not found." })
                : Results.Ok(ToResponse(result));
        })
        .WithName("ResultByIdDebug")
        .WithSummary("Returns one stored prop result by id.");

        endpoints.MapGet("/api/debug/identity/provider-links", async (
            string? sport,
            string? entityType,
            string? providerKey,
            string? linkStatus,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var sampleLimit = Math.Clamp(maxSamples ?? 50, 1, 250);
            var query = dbContext.ProviderEntityLinks
                .AsNoTracking()
                .Include(link => link.Sport)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(sport))
            {
                var normalizedSport = sport.Trim();
                query = query.Where(link => link.Sport != null && link.Sport.Code == normalizedSport);
            }

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                var normalizedEntityType = entityType.Trim();
                query = query.Where(link => link.EntityType == normalizedEntityType);
            }

            if (!string.IsNullOrWhiteSpace(providerKey))
            {
                var normalizedProviderKey = providerKey.Trim();
                query = query.Where(link => link.ProviderKey == normalizedProviderKey);
            }

            if (!string.IsNullOrWhiteSpace(linkStatus))
            {
                var normalizedLinkStatus = linkStatus.Trim();
                query = query.Where(link => link.LinkStatus == normalizedLinkStatus);
            }

            var rows = await query
                .Select(link => new
                {
                    link.Id,
                    Sport = link.Sport == null ? null : link.Sport.Code,
                    link.EntityType,
                    link.CanonicalEntityId,
                    link.CanonicalEntityKind,
                    link.CanonicalEntityName,
                    link.ProviderKey,
                    link.ProviderEntityId,
                    link.ProviderEntityName,
                    link.ProviderTeamId,
                    link.ProviderTeamName,
                    link.ProviderCompetitionId,
                    link.ProviderCompetitionName,
                    link.ProviderEventId,
                    link.EventStartUtc,
                    link.Confidence,
                    link.LinkStatus,
                    link.LinkSource,
                    link.CreatedAtUtc,
                    link.UpdatedAtUtc,
                    link.ApprovedAtUtc,
                    link.ApprovedBy,
                    link.RejectedAtUtc,
                    link.RejectedReason
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                filters = new { sport, entityType, providerKey, linkStatus },
                totalLinks = rows.Count,
                linkedCount = rows.Count(row => row.CanonicalEntityId.HasValue && string.Equals(row.LinkStatus, "Approved", StringComparison.OrdinalIgnoreCase)),
                candidateCount = rows.Count(row => string.Equals(row.LinkStatus, "Candidate", StringComparison.OrdinalIgnoreCase)),
                needsReviewCount = rows.Count(row => string.Equals(row.LinkStatus, "NeedsReview", StringComparison.OrdinalIgnoreCase)),
                conflictCount = rows.Count(row => string.Equals(row.LinkStatus, "Conflict", StringComparison.OrdinalIgnoreCase)),
                rejectedCount = rows.Count(row => string.Equals(row.LinkStatus, "Rejected", StringComparison.OrdinalIgnoreCase)),
                counts = new
                {
                    bySport = rows
                        .GroupBy(row => row.Sport ?? "Unscoped")
                        .OrderBy(group => group.Key)
                        .Select(group => new { sport = group.Key, count = group.Count() })
                        .ToArray(),
                    byEntityType = rows
                        .GroupBy(row => row.EntityType)
                        .OrderBy(group => group.Key)
                        .Select(group => new { entityType = group.Key, count = group.Count() })
                        .ToArray(),
                    byProviderKey = rows
                        .GroupBy(row => row.ProviderKey)
                        .OrderBy(group => group.Key)
                        .Select(group => new { providerKey = group.Key, count = group.Count() })
                        .ToArray(),
                    byLinkStatus = rows
                        .GroupBy(row => row.LinkStatus)
                        .OrderBy(group => group.Key)
                        .Select(group => new { linkStatus = group.Key, count = group.Count() })
                        .ToArray(),
                    bySportEntityProviderStatus = rows
                        .GroupBy(row => new { Sport = row.Sport ?? "Unscoped", row.EntityType, row.ProviderKey, row.LinkStatus })
                        .OrderBy(group => group.Key.Sport)
                        .ThenBy(group => group.Key.EntityType)
                        .ThenBy(group => group.Key.ProviderKey)
                        .ThenBy(group => group.Key.LinkStatus)
                        .Select(group => new
                        {
                            sport = group.Key.Sport,
                            group.Key.EntityType,
                            group.Key.ProviderKey,
                            group.Key.LinkStatus,
                            count = group.Count()
                        })
                        .ToArray()
                },
                samples = includeSamples == true
                    ? rows
                        .OrderBy(row => row.Sport)
                        .ThenBy(row => row.EntityType)
                        .ThenBy(row => row.ProviderKey)
                        .ThenBy(row => row.ProviderEntityName)
                        .Take(sampleLimit)
                        .ToArray()
                    : null
            });
        })
        .WithName("ProviderEntityLinksDebug")
        .WithSummary("Read-only provider identity link diagnostics.");

        endpoints.MapGet("/api/debug/provider-stat-mappings", async (
            string? sport,
            string? providerKey,
            string? competitionScope,
            int? season,
            bool? includeInactive,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var sampleLimit = Math.Clamp(maxSamples ?? 100, 1, 250);
            var query = dbContext.ProviderStatMappings
                .AsNoTracking()
                .Include(mapping => mapping.Sport)
                .Include(mapping => mapping.CanonicalPropType)
                .AsQueryable();

            if (includeInactive != true)
            {
                query = query.Where(mapping => mapping.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(sport))
            {
                var normalizedSport = sport.Trim();
                query = query.Where(mapping => mapping.Sport != null && mapping.Sport.Code == normalizedSport);
            }

            if (!string.IsNullOrWhiteSpace(providerKey))
            {
                var normalizedProviderKey = providerKey.Trim();
                query = query.Where(mapping => mapping.ProviderKey == normalizedProviderKey);
            }

            if (!string.IsNullOrWhiteSpace(competitionScope))
            {
                var normalizedCompetitionScope = competitionScope.Trim();
                query = query.Where(mapping => mapping.CompetitionScope == normalizedCompetitionScope);
            }

            if (season.HasValue)
            {
                query = query.Where(mapping => mapping.Season == season.Value);
            }

            var mappingRows = await query.ToListAsync(cancellationToken);
            var rows = mappingRows
                .Select(mapping => new
                {
                    mapping.Id,
                    Sport = mapping.Sport?.Code,
                    mapping.ProviderKey,
                    mapping.ProviderProduct,
                    mapping.CompetitionScope,
                    mapping.Season,
                    mapping.ProviderMappingKey,
                    mapping.ProviderPropName,
                    mapping.ProviderStatKey,
                    ProviderStatFields = ExtractJsonStringArray(mapping.ProviderStatFieldsJson),
                    mapping.CanonicalPropTypeId,
                    CanonicalPropTypeName = mapping.CanonicalPropType?.Name,
                    mapping.CanonicalStatKey,
                    mapping.DisplayName,
                    mapping.ValueTransform,
                    mapping.RuleConfidence,
                    mapping.IsGradeable,
                    mapping.PublicGradingEnabled,
                    mapping.IsActive,
                    mapping.Notes,
                    mapping.CreatedAtUtc,
                    mapping.UpdatedAtUtc
                })
                .ToList();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                filters = new { sport, providerKey, competitionScope, season, includeInactive = includeInactive == true },
                totalMappings = rows.Count,
                activeMappings = rows.Count(row => row.IsActive),
                missingCanonicalPropTypeCount = rows.Count(row => !row.CanonicalPropTypeId.HasValue),
                counts = new
                {
                    bySport = rows
                        .GroupBy(row => row.Sport ?? "Unscoped")
                        .OrderBy(group => group.Key)
                        .Select(group => new { sport = group.Key, count = group.Count() })
                        .ToArray(),
                    byProvider = rows
                        .GroupBy(row => row.ProviderKey)
                        .OrderBy(group => group.Key)
                        .Select(group => new { providerKey = group.Key, count = group.Count() })
                        .ToArray(),
                    byRuleConfidence = rows
                        .GroupBy(row => row.RuleConfidence)
                        .OrderBy(group => group.Key)
                        .Select(group => new { ruleConfidence = group.Key, count = group.Count() })
                        .ToArray(),
                    byGradeable = rows
                        .GroupBy(row => row.IsGradeable)
                        .OrderByDescending(group => group.Key)
                        .Select(group => new { isGradeable = group.Key, count = group.Count() })
                        .ToArray(),
                    byPublicGradingEnabled = rows
                        .GroupBy(row => row.PublicGradingEnabled)
                        .OrderByDescending(group => group.Key)
                        .Select(group => new { publicGradingEnabled = group.Key, count = group.Count() })
                        .ToArray()
                },
                samples = includeSamples == true
                    ? rows
                        .OrderBy(row => row.Sport)
                        .ThenBy(row => row.ProviderKey)
                        .ThenBy(row => row.CompetitionScope)
                        .ThenBy(row => row.Season)
                        .ThenBy(row => row.DisplayName)
                        .Take(sampleLimit)
                        .ToArray()
                    : null
            });
        })
        .WithName("ProviderStatMappingsDebug")
        .WithSummary("Read-only provider stat mapping diagnostics.");

        endpoints.MapGet("/api/debug/mlb/regression-smoke", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var now = DateTimeOffset.UtcNow;
            var windowToDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var windowFromDate = windowToDate.AddDays(-14);
            var sportRows = await dbContext.Sports
                .AsNoTracking()
                .Select(sport => new { sport.Id, sport.Code, sport.Name })
                .ToListAsync(cancellationToken);
            var leagueRows = await dbContext.Leagues
                .AsNoTracking()
                .Include(league => league.Sport)
                .Select(league => new
                {
                    league.Id,
                    league.Code,
                    league.Name,
                    league.SeasonType,
                    SportId = (Guid?)league.SportId,
                    SportCode = league.Sport == null ? null : league.Sport.Code,
                    SportName = league.Sport == null ? null : league.Sport.Name
                })
                .ToListAsync(cancellationToken);
            var resolvedMlbSport = sportRows.FirstOrDefault(sport => IsMlbLabel(sport.Code) || IsMlbLabel(sport.Name))
                ?? sportRows.FirstOrDefault(sport => leagueRows.Any(league => league.SportId == sport.Id && (IsMlbLabel(league.Code) || IsMlbLabel(league.Name))));
            var resolvedMlbLeagues = leagueRows
                .Where(league => IsMlbLabel(league.Code)
                    || IsMlbLabel(league.Name)
                    || IsMlbLabel(league.SportCode)
                    || IsMlbLabel(league.SportName)
                    || (resolvedMlbSport is not null && league.SportId == resolvedMlbSport.Id))
                .ToArray();
            var resolvedMlbLeagueIds = resolvedMlbLeagues.Select(league => league.Id).Distinct().ToArray();
            var mlbLookupMethod = resolvedMlbSport is not null && IsMlbLabel(resolvedMlbSport.Code)
                ? "SportCode"
                : resolvedMlbSport is not null && IsMlbLabel(resolvedMlbSport.Name)
                    ? "SportName"
                    : resolvedMlbLeagues.Length > 0
                        ? "LeagueCodeOrName"
                        : "NotResolved";
            var latestMlbResultImportDate = await dbContext.MlbResultImportDateStates
                .AsNoTracking()
                .Where(state => state.LastSuccessfulAt.HasValue
                    || state.RecordsFound > 0
                    || state.RecordsMapped > 0
                    || state.RecordsInserted > 0
                    || state.RecordsUpdated > 0)
                .OrderByDescending(state => state.EventDate)
                .Select(state => (DateOnly?)state.EventDate)
                .FirstOrDefaultAsync(cancellationToken);
            var mlbPropResultsQuery = dbContext.PropResults
                .AsNoTracking()
                .Where(result => resolvedMlbLeagueIds.Contains(result.LeagueId));
            var recentMlbPropResults = await dbContext.PropResults
                .AsNoTracking()
                .Where(result => resolvedMlbLeagueIds.Contains(result.LeagueId)
                    && result.EventDate >= windowFromDate
                    && result.EventDate <= windowToDate)
                .CountAsync(cancellationToken);
            var allTimeMlbPropResults = await mlbPropResultsQuery.CountAsync(cancellationToken);
            var latestMlbPropResultDate = await mlbPropResultsQuery
                .OrderByDescending(result => result.EventDate)
                .Select(result => (DateOnly?)result.EventDate)
                .FirstOrDefaultAsync(cancellationToken);
            var response = new
            {
                generatedAtUtc = now,
                callsExternalApis = false,
                readOnly = true,
                resolvedMlb = new
                {
                    sportId = resolvedMlbSport?.Id,
                    sportCode = resolvedMlbSport?.Code,
                    sportName = resolvedMlbSport?.Name,
                    lookupMethod = mlbLookupMethod,
                    leagues = resolvedMlbLeagues.Select(league => new
                    {
                        leagueId = league.Id,
                        leagueCode = league.Code,
                        leagueName = league.Name,
                        league.SeasonType,
                        league.SportId,
                        league.SportCode,
                        league.SportName
                    }).ToArray()
                },
                players = await dbContext.Players.AsNoTracking().CountAsync(cancellationToken),
                mlbPlayerExternalRefs = await dbContext.PlayerExternalRefs.AsNoTracking().CountAsync(reference => reference.Provider == "mlb", cancellationToken),
                mlbPropResults = new
                {
                    recentWindow = new { fromDate = windowFromDate, toDate = windowToDate },
                    recentCount = recentMlbPropResults,
                    allTimeCount = allTimeMlbPropResults,
                    latestResultDate = latestMlbPropResultDate
                },
                recentMlbPropResults,
                allTimeMlbPropResults,
                latestMlbPropResultDate,
                recentMlbMarketSnapshots = await dbContext.MarketTopPickSnapshots.AsNoTracking().CountAsync(snapshot => snapshot.Sport == "MLB", cancellationToken)
                    + await dbContext.MarketLineShoppingSnapshots.AsNoTracking().CountAsync(snapshot => snapshot.Sport == "MLB", cancellationToken),
                latestMlbResultImportDate,
                predictionRuns = await dbContext.PredictionRuns.AsNoTracking().CountAsync(cancellationToken),
                recommendationSnapshots = await dbContext.RecommendationSnapshots.AsNoTracking().CountAsync(snapshot => snapshot.Sport == "MLB", cancellationToken),
                mlbProviderEntityLinks = resolvedMlbSport is not null
                    ? await dbContext.ProviderEntityLinks.AsNoTracking().CountAsync(link => link.SportId == resolvedMlbSport.Id, cancellationToken)
                    : 0,
                warnings = new[]
                    {
                        latestMlbResultImportDate.HasValue ? null : "No imported MLB result date was found.",
                        recentMlbPropResults > 0 ? null : allTimeMlbPropResults > 0
                            ? "No MLB prop_results were found in the smoke-check window, but older MLB prop_results exist."
                            : "No MLB prop_results were found.",
                        resolvedMlbSport is not null || resolvedMlbLeagues.Length > 0 ? null : "No MLB Sport or League identity was found."
                    }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray()
            };

            return Results.Ok(response);
        })
        .WithName("MlbRegressionSmokeDebug")
        .WithSummary("Read-only MLB regression smoke diagnostics.");

        endpoints.MapGet("/api/debug/mlb-results", async (
            DateOnly date,
            string? playerName,
            IMlbResultsProvider mlbResultsProvider,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var results = await mlbResultsProvider.GetMlbResultsAsync(date, cancellationToken);
                var filteredResults = string.IsNullOrWhiteSpace(playerName)
                    ? results
                    : results
                        .Where(result => result.PlayerName.Contains(playerName, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                return Results.Ok(new
                {
                    provider = "mlb",
                    date,
                    resultCount = filteredResults.Count,
                    results = filteredResults
                        .OrderBy(result => result.PlayerName)
                        .ThenBy(result => result.GameId)
                        .ThenBy(result => result.StatName)
                        .Select(result => new
                        {
                            player = result.PlayerName,
                            mlbPlayerId = result.MlbPlayerId,
                            gameId = result.GameId,
                            statName = result.StatName,
                            statValue = result.StatValue,
                            isMapped = result.IsMapped,
                            externalSourceIdentifier = result.ResultRecord.ExternalSourceIdentifier
                        })
                        .ToArray()
                });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithName("MlbResultsDebug")
        .WithSummary("Fetches finalized MLB player box-score result records by game date.");

        endpoints.MapPost("/api/debug/results/import/mlb", async (
            MlbResultImportRequest request,
            IMlbResultImportService importService,
            IMlbResultCoverageService coverageService,
            CancellationToken cancellationToken) =>
        {
            if (request.DryRun)
            {
                var audit = await coverageService.AuditDateAsync(request.Date, persist: false, cancellationToken);
                return Results.Ok(new
                {
                    request.Date,
                    dryRun = true,
                    writesRows = false,
                    wouldImport = request.Force || audit.NeedsRepair,
                    beforeCoverage = audit
                });
            }

            try
            {
                var result = await importService.ImportAsync(request.Date, cancellationToken);
                var coverage = await coverageService.AuditDateAsync(request.Date, persist: true, cancellationToken);

                return Results.Ok(new
                {
                    date = result.Date,
                    recordsFound = result.RecordsFound,
                    recordsMapped = result.RecordsMapped,
                    recordsInserted = result.RecordsInserted,
                    recordsUpdated = result.RecordsUpdated,
                    importDuration = result.ImportDuration,
                    coverage
                });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithName("MlbResultsImportDebug")
        .WithSummary("Imports mapped MLB result records into prop_results.");

        endpoints.MapGet("/api/debug/results/import/mlb/status", async (
            DateOnly? date,
            ApplicationDbContext dbContext,
            IMlbResultImportService importService,
            IMlbResultCoverageService coverageService,
            CancellationToken cancellationToken) =>
        {
            var state = await dbContext.MlbResultImportStates
                .AsNoTracking()
                .SingleOrDefaultAsync(importState => importState.Source == "mlb", cancellationToken);
            var coverage = await dbContext.PropResults
                .AsNoTracking()
                .Where(result => result.ExternalSource == "mlb")
                .GroupBy(result => result.ExternalSource)
                .Select(group => new
                {
                    Start = group.Min(result => result.EventDate),
                    End = group.Max(result => result.EventDate)
                })
                .SingleOrDefaultAsync(cancellationToken);
            var latestCompletedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var pendingBackfillDates = await dbContext.MlbResultImportDateStates
                .AsNoTracking()
                .Where(dateState => dateState.EventDate <= latestCompletedDate
                    && dateState.CoverageStatus != "Complete"
                    && dateState.CoverageStatus != "NoGames")
                .OrderBy(dateState => dateState.EventDate)
                .Select(dateState => dateState.EventDate)
                .Take(100)
                .ToArrayAsync(cancellationToken);
            var latestCycleDates = Array.Empty<MlbLatestWorkerCycleDateOutcome>();
            if (state?.LastStartedAt is not null)
            {
                var latestCycleDateStates = await dbContext.MlbResultImportDateStates
                    .AsNoTracking()
                    .Where(dateState => dateState.LastAttemptedAt >= state.LastStartedAt)
                    .Where(dateState => state.LastCompletedAt == null || dateState.LastAttemptedAt <= state.LastCompletedAt)
                    .OrderBy(dateState => dateState.EventDate)
                    .ToArrayAsync(cancellationToken);
                latestCycleDates = latestCycleDateStates
                    .Select(dateState => new MlbLatestWorkerCycleDateOutcome(
                        dateState.EventDate,
                        dateState.CoverageStatus == "Complete"
                            ? "CompletedDuringLatestCycle"
                            : dateState.CoverageStatus == "NoGames"
                                ? "NoGamesSkippedAfterSuccessfulCheck"
                                : dateState.CoverageStatus == "Partial"
                                    ? "CoveragePartial"
                                    : dateState.CoverageStatus == "NotStarted"
                                        ? "CoverageNotStarted"
                                        : dateState.CoverageStatus == "PendingGames"
                                            ? "PendingGamesRecheck"
                                            : dateState.CoverageStatus == "Failed"
                                                ? "PreviousFailureRetry"
                                                : "CoverageNotComplete",
                        dateState.Status,
                        dateState.CompletedGamesExpected,
                        dateState.CompletedGamesImported,
                        dateState.RecordsFound,
                        dateState.RecordsMapped,
                        dateState.ImportedResults,
                        dateState.DistinctPlayers,
                        dateState.DistinctPropTypes,
                        dateState.CoverageStatus,
                        dateState.CoverageReason,
                        MlbResultImportWorker.ClassifyDateOutcome(
                            dateState.CoverageStatus,
                            dateState.Status,
                            dateState.RecordsFound,
                            exceptionOccurred: false).ToString(),
                        dateState.LastAttemptedAt,
                        dateState.LastSuccessfulAt,
                        dateState.LastErrorMessage))
                    .ToArray();
            }
            var missingDates = coverage is null
                ? []
                : await BuildMissingMlbImportDatesAsync(dbContext, coverage.Start, latestCompletedDate, cancellationToken);
            var dateStatus = date.HasValue
                ? await importService.GetStatusAsync(date.Value, cancellationToken)
                : null;
            var dateCoverage = date.HasValue
                ? await coverageService.AuditDateAsync(date.Value, persist: true, cancellationToken)
                : null;

            return Results.Ok(new
            {
                lastSuccessfulImport = state?.LastSuccessfulImportDate,
                lastSuccessfulRun = state?.LastSuccessfulRunAt,
                lastStartedAt = state?.LastStartedAt,
                lastCompletedAt = state?.LastCompletedAt,
                lastStatus = state?.LastStatus ?? "never_run",
                lastErrorMessage = state?.LastErrorMessage,
                missingDates,
                pendingBackfillDates,
                historicalCoverageStart = coverage?.Start,
                historicalCoverageEnd = coverage?.End,
                latestScheduledImportRun = state is null
                    ? null
                    : new
                    {
                        state.LastStartedAt,
                        state.LastCompletedAt,
                        state.LastStatus,
                        state.LastSuccessfulRunAt,
                        state.LastErrorMessage
                    },
                latestWorkerCycle = state is null
                    ? null
                    : new
                    {
                        state.LastStartedAt,
                        state.LastCompletedAt,
                        state.LastStatus,
                        processedDates = latestCycleDates.Length,
                        complete = latestCycleDates.Count(row => row.OutcomeCategory == "Complete"),
                        partial = latestCycleDates.Count(row => row.OutcomeCategory == "Partial"),
                        pendingGames = latestCycleDates.Count(row => row.OutcomeCategory == "PendingGames"),
                        noGames = latestCycleDates.Count(row => row.OutcomeCategory == "NoGames"),
                        skipped = latestCycleDates.Count(row => row.OutcomeCategory == "Skipped"),
                        failed = latestCycleDates.Count(row => row.OutcomeCategory == "Failed"),
                        dateOutcomes = latestCycleDates
                    },
                latestSuccessfulImportAttemptForRequestedDate = dateCoverage?.LatestImportAttemptUtc,
                latestRepairRunForRequestedDate = dateCoverage?.LatestSuccessfulRepairUtc,
                dateStatus = dateStatus is null
                    ? null
                    : new
                    {
                        date = dateStatus.Date,
                        importedResults = dateStatus.ImportedResults,
                        distinctPlayers = dateStatus.DistinctPlayers,
                        distinctPropTypes = dateStatus.DistinctPropTypes,
                        coverageStatus = dateCoverage?.CoverageStatus,
                        coverageReason = dateCoverage?.CoverageReason,
                        completedGamesExpected = dateCoverage?.CompletedGamesExpected,
                        completedGamesImported = dateCoverage?.CompletedGamesImported,
                        latestImportAttemptUtc = dateCoverage?.LatestImportAttemptUtc,
                        latestSuccessfulRepairUtc = dateCoverage?.LatestSuccessfulRepairUtc,
                        latestErrorMessage = dateCoverage?.LatestErrorMessage
                    }
            });
        })
        .WithName("MlbResultsImportStatusDebug")
        .WithSummary("Returns MLB result import worker status and optional date-specific import status.");

        endpoints.MapGet("/api/debug/results/import/mlb-range/status", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var state = await dbContext.MlbResultImportStates
                .AsNoTracking()
                .SingleOrDefaultAsync(importState => importState.Source == "mlb", cancellationToken);
            var latestCompletedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var pendingBackfillDates = await dbContext.MlbResultImportDateStates
                .AsNoTracking()
                .Where(dateState => dateState.EventDate <= latestCompletedDate
                    && dateState.CoverageStatus != "Complete"
                    && dateState.CoverageStatus != "NoGames")
                .OrderBy(dateState => dateState.EventDate)
                .Select(dateState => new
                {
                    dateState.EventDate,
                    dateState.CoverageStatus,
                    dateState.CoverageReason,
                    dateState.Attempts,
                    dateState.LastAttemptedAt
                })
                .Take(100)
                .ToArrayAsync(cancellationToken);
            var latestCycleDates = Array.Empty<MlbLatestWorkerCycleDateOutcome>();
            if (state?.LastStartedAt is not null)
            {
                var latestCycleDateStates = await dbContext.MlbResultImportDateStates
                    .AsNoTracking()
                    .Where(dateState => dateState.LastAttemptedAt >= state.LastStartedAt)
                    .Where(dateState => state.LastCompletedAt == null || dateState.LastAttemptedAt <= state.LastCompletedAt)
                    .OrderBy(dateState => dateState.EventDate)
                    .ToArrayAsync(cancellationToken);
                latestCycleDates = latestCycleDateStates
                    .Select(dateState => new MlbLatestWorkerCycleDateOutcome(
                        dateState.EventDate,
                        dateState.CoverageStatus == "Complete"
                            ? "CompletedDuringLatestCycle"
                            : dateState.CoverageStatus == "NoGames"
                                ? "NoGamesSkippedAfterSuccessfulCheck"
                                : dateState.CoverageStatus == "Partial"
                                    ? "CoveragePartial"
                                    : dateState.CoverageStatus == "NotStarted"
                                        ? "CoverageNotStarted"
                                        : dateState.CoverageStatus == "PendingGames"
                                            ? "PendingGamesRecheck"
                                            : dateState.CoverageStatus == "Failed"
                                                ? "PreviousFailureRetry"
                                                : "CoverageNotComplete",
                        dateState.Status,
                        dateState.CompletedGamesExpected,
                        dateState.CompletedGamesImported,
                        dateState.RecordsFound,
                        dateState.RecordsMapped,
                        dateState.ImportedResults,
                        dateState.DistinctPlayers,
                        dateState.DistinctPropTypes,
                        dateState.CoverageStatus,
                        dateState.CoverageReason,
                        MlbResultImportWorker.ClassifyDateOutcome(
                            dateState.CoverageStatus,
                            dateState.Status,
                            dateState.RecordsFound,
                            exceptionOccurred: false).ToString(),
                        dateState.LastAttemptedAt,
                        dateState.LastSuccessfulAt,
                        dateState.LastErrorMessage))
                    .ToArray();
            }

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                latestCompletedDate,
                pendingBackfillDateCount = pendingBackfillDates.Length,
                pendingBackfillDates,
                latestWorkerCycle = state is null
                    ? null
                    : new
                    {
                        state.LastStartedAt,
                        state.LastCompletedAt,
                        state.LastStatus,
                        state.LastSuccessfulRunAt,
                        state.LastSuccessfulImportDate,
                        state.LastErrorMessage,
                        processedDates = latestCycleDates.Length,
                        complete = latestCycleDates.Count(row => row.OutcomeCategory == "Complete"),
                        partial = latestCycleDates.Count(row => row.OutcomeCategory == "Partial"),
                        pendingGames = latestCycleDates.Count(row => row.OutcomeCategory == "PendingGames"),
                        noGames = latestCycleDates.Count(row => row.OutcomeCategory == "NoGames"),
                        skipped = latestCycleDates.Count(row => row.OutcomeCategory == "Skipped"),
                        failed = latestCycleDates.Count(row => row.OutcomeCategory == "Failed"),
                        dateOutcomes = latestCycleDates
                    }
            });
        })
        .WithName("MlbResultsImportRangeStatusDebug")
        .WithSummary("Read-only MLB range import worker diagnostics with latest per-date outcomes.");

        endpoints.MapPost("/api/debug/results/import/mlb-range", async (
            MlbResultImportRangeRequest request,
            IMlbResultImportService importService,
            CancellationToken cancellationToken) =>
        {
            if (request.EndDate < request.StartDate)
            {
                return Results.BadRequest(new { error = "endDate must be greater than or equal to startDate." });
            }

            var startedAt = DateTimeOffset.UtcNow;
            var currentDate = request.StartDate;
            var datesProcessed = 0;
            var datesSucceeded = 0;
            var datesFailed = 0;
            var recordsFound = 0;
            var recordsMapped = 0;
            var recordsInserted = 0;
            var recordsUpdated = 0;
            var failures = new List<object>();

            while (currentDate <= request.EndDate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                datesProcessed++;

                try
                {
                    var result = await importService.ImportAsync(currentDate, cancellationToken);
                    datesSucceeded++;
                    recordsFound += result.RecordsFound;
                    recordsMapped += result.RecordsMapped;
                    recordsInserted += result.RecordsInserted;
                    recordsUpdated += result.RecordsUpdated;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    datesFailed++;
                    failures.Add(new
                    {
                        date = currentDate,
                        exceptionType = exception.GetType().Name,
                        exception.Message
                    });
                }

                currentDate = currentDate.AddDays(1);
            }

            var duration = DateTimeOffset.UtcNow - startedAt;

            return Results.Ok(new
            {
                startDate = request.StartDate,
                endDate = request.EndDate,
                datesProcessed,
                datesSucceeded,
                datesFailed,
                recordsFound,
                recordsMapped,
                recordsInserted,
                recordsUpdated,
                duration,
                failures
            });
        })
        .WithName("MlbResultsImportRangeDebug")
        .WithSummary("Imports mapped MLB result records across an inclusive date range.");

        endpoints.MapGet("/api/debug/results/import/mlb/coverage-audit", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeDetails,
            IMlbResultCoverageService coverageService,
            CancellationToken cancellationToken) =>
        {
            var effectiveToDate = toDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var effectiveFromDate = fromDate ?? effectiveToDate.AddDays(-7);
            if (effectiveToDate < effectiveFromDate)
            {
                return Results.BadRequest(new { error = "toDate must be greater than or equal to fromDate." });
            }

            var audit = await coverageService.AuditAsync(
                effectiveFromDate,
                effectiveToDate,
                includeDetails ?? true,
                persist: false,
                cancellationToken);
            return Results.Ok(audit);
        })
        .WithName("MlbResultsImportCoverageAuditDebug")
        .WithSummary("Read-only MLB result import coverage audit for complete, partial, missing, and pending dates.");

        endpoints.MapPost("/api/debug/results/import/mlb/repair-missing", async (
            MlbResultImportRepairRequest request,
            IMlbResultImportService importService,
            IMlbResultCoverageService coverageService,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (request.ToDate < request.FromDate)
            {
                return Results.BadRequest(new { error = "toDate must be greater than or equal to fromDate." });
            }

            var dryRun = request.DryRun ?? true;
            var repairPartial = request.RepairPartial ?? true;
            var repairMissing = request.RepairMissing ?? true;
            var before = await coverageService.AuditAsync(
                request.FromDate,
                request.ToDate,
                includeDetails: true,
                persist: !dryRun,
                cancellationToken);
            var candidates = before.Details
                .Where(row => request.Force == true
                    || (repairPartial && row.CoverageStatus == "Partial")
                    || (repairMissing && row.CoverageStatus == "NotStarted"))
                .OrderBy(row => row.Date)
                .ToArray();

            var repaired = new List<object>();
            var failures = new List<object>();

            if (!dryRun)
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var importResult = await importService.ImportAsync(candidate.Date, cancellationToken);
                        var afterDate = await coverageService.AuditDateAsync(candidate.Date, persist: true, cancellationToken);
                        var dateState = await dbContext.MlbResultImportDateStates
                            .SingleOrDefaultAsync(row => row.EventDate == candidate.Date, cancellationToken);
                        if (dateState is not null)
                        {
                            dateState.LatestSuccessfulRepairUtc = DateTimeOffset.UtcNow;
                            dateState.UpdatedAt = DateTimeOffset.UtcNow;
                            await dbContext.SaveChangesAsync(cancellationToken);
                            afterDate = await coverageService.AuditDateAsync(candidate.Date, persist: true, cancellationToken);
                        }
                        repaired.Add(new
                        {
                            date = candidate.Date,
                            before = candidate,
                            importResult,
                            after = afterDate
                        });
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failures.Add(new
                        {
                            date = candidate.Date,
                            exceptionType = exception.GetType().Name,
                            exception.Message
                        });
                    }
                }
            }

            var after = dryRun
                ? null
                : await coverageService.AuditAsync(
                    request.FromDate,
                    request.ToDate,
                    includeDetails: true,
                    persist: true,
                    cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = !dryRun && candidates.Length > 0,
                dryRun,
                writesRows = !dryRun,
                request.FromDate,
                request.ToDate,
                request.Force,
                repairPartial,
                repairMissing,
                datesAudited = before.DatesAudited,
                datesSelectedForRepair = candidates.Length,
                selectedDates = candidates.Select(row => row.Date).ToArray(),
                before,
                repaired,
                failures,
                after
            });
        })
        .WithName("MlbResultsImportRepairMissingDebug")
        .WithSummary("Audits and repairs missing or partial MLB result imports. Dry run is the default.");

        endpoints.MapGet("/api/debug/results/import/mlb/coverage", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var results = await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.PropType)
                .Where(result => result.ExternalSource == "mlb")
                .Select(result => new
                {
                    result.PlayerId,
                    result.PropTypeId,
                    propType = result.PropType == null ? "Unknown" : result.PropType.Name,
                    result.EventDate
                })
                .ToListAsync(cancellationToken);

            var dateRangeCovered = results.Count == 0
                ? null
                : new
                {
                    startDate = results.Min(result => result.EventDate),
                    endDate = results.Max(result => result.EventDate)
                };
            var resultsByPropType = results
                .GroupBy(result => new { result.PropTypeId, result.propType })
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key.propType)
                .Select(group => new
                {
                    propTypeId = group.Key.PropTypeId,
                    propType = group.Key.propType,
                    resultCount = group.Count(),
                    distinctPlayers = group.Select(result => result.PlayerId).Distinct().Count()
                })
                .ToArray();

            return Results.Ok(new
            {
                totalPropResults = results.Count,
                distinctPlayers = results.Select(result => result.PlayerId).Distinct().Count(),
                distinctPropTypes = results.Select(result => result.PropTypeId).Distinct().Count(),
                dateRangeCovered,
                resultsByPropType
            });
        })
        .WithName("MlbResultsImportCoverageDebug")
        .WithSummary("Returns MLB prop result coverage across imported historical results.");

        endpoints.MapGet("/api/debug/soccer/api-sports/config", (
            IOptions<ApiSportsOptions> options) =>
        {
            var diagnostics = BuildApiSportsConfigDiagnostics(options.Value);
            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                provider = "API-Sports",
                product = "API-Sports Football",
                diagnostics.configured,
                diagnostics.missingConfig,
                config = diagnostics.safeConfig
            });
        })
        .WithName("ApiSportsConfigDebug")
        .WithSummary("Read-only API-Sports Football result provider configuration diagnostics.");

        endpoints.MapGet("/api/debug/soccer/api-sports/fixtures", async (
            DateOnly? date,
            string? fixtureId,
            int? league,
            int? season,
            bool? useConfiguredLeague,
            bool? useConfiguredSeason,
            bool? forceLive,
            IOptions<ApiSportsOptions> options,
            IApiSportsFootballClient client,
            CancellationToken cancellationToken) =>
        {
            var fixtureRequest = BuildApiSportsFixtureRequest(
                options.Value,
                date,
                fixtureId,
                league,
                season,
                useConfiguredLeague,
                useConfiguredSeason);
            var requestDiagnostics = new
            {
                date,
                fixtureId,
                league,
                season,
                useConfiguredLeague = useConfiguredLeague != false,
                useConfiguredSeason = useConfiguredSeason != false,
                forceLive = forceLive == true,
                used = fixtureRequest
            };

            if (string.IsNullOrWhiteSpace(fixtureRequest.FixtureId) && !fixtureRequest.Date.HasValue)
            {
                return Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    provider = "API-Sports",
                    product = "API-Sports Football",
                    operation = "fixtures",
                    request = requestDiagnostics,
                    blocked = true,
                    blockReason = "MissingFixtureQuery",
                    message = "Provide either fixtureId or date for API-Sports Football fixture diagnostics."
                });
            }

            var block = BuildApiSportsBlockedResponse(
                "fixtures",
                forceLive,
                options.Value,
                requestDiagnostics,
                requireConfiguredLeague: string.IsNullOrWhiteSpace(fixtureRequest.FixtureId) && league is null && useConfiguredLeague != false,
                requireConfiguredSeason: string.IsNullOrWhiteSpace(fixtureRequest.FixtureId) && season is null && useConfiguredSeason != false);
            if (block is not null)
            {
                return block;
            }

            var response = await client.GetFixturesAsync(fixtureRequest, cancellationToken);
            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = true,
                readOnly = true,
                provider = "API-Sports",
                product = "API-Sports Football",
                request = requestDiagnostics,
                response.Results,
                resultCount = response.Results,
                response.RequestsCurrent,
                response.RequestsLimit,
                response.SanitizedUrl,
                errors = ExtractApiSportsErrors(response.Payload.RootElement),
                fixtures = MapApiSportsFixtures(response.Payload.RootElement),
                raw = response.Payload.RootElement.Clone()
            });
        })
        .WithName("ApiSportsFixturesDebug")
        .WithSummary("Force-live gated API-Sports Football fixture discovery diagnostics.");

        endpoints.MapGet("/api/debug/soccer/api-sports/fixture-player-stats", async (
            string fixtureId,
            bool? forceLive,
            IOptions<ApiSportsOptions> options,
            IApiSportsFootballClient client,
            CancellationToken cancellationToken) =>
        {
            var block = BuildApiSportsBlockedResponse("fixture-player-stats", forceLive, options.Value, new { fixtureId });
            if (block is not null)
            {
                return block;
            }

            var response = await client.GetFixturePlayerStatsAsync(fixtureId, cancellationToken);
            var playerStats = ExtractApiSportsPlayerStats(response.Payload.RootElement);
            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = true,
                readOnly = true,
                provider = "API-Sports",
                product = "API-Sports Football",
                request = new { fixtureId, forceLive = true },
                response.Results,
                response.RequestsCurrent,
                response.RequestsLimit,
                response.SanitizedUrl,
                teamCount = playerStats.Select(row => row.TeamName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                playerCount = playerStats.Select(row => row.PlayerId).Distinct().Count(),
                statKeysDiscovered = playerStats.SelectMany(row => row.StatKeys).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                players = playerStats.Take(200).Select(row => new
                {
                    row.TeamName,
                    row.PlayerId,
                    row.PlayerName,
                    row.StatKeys,
                    row.RawStats
                }).ToArray(),
                raw = response.Payload.RootElement.Clone()
            });
        })
        .WithName("ApiSportsFixturePlayerStatsDebug")
        .WithSummary("Force-live gated API-Sports Football fixture player-stat diagnostics.");

        endpoints.MapGet("/api/debug/soccer/api-sports/fixture-prop-coverage", async (
            string fixtureId,
            bool? forceLive,
            IOptions<ApiSportsOptions> options,
            IApiSportsFootballClient client,
            CancellationToken cancellationToken) =>
        {
            var block = BuildApiSportsBlockedResponse("fixture-prop-coverage", forceLive, options.Value, new { fixtureId });
            if (block is not null)
            {
                return block;
            }

            var response = await client.GetFixturePlayerStatsAsync(fixtureId, cancellationToken);
            var playerStats = ExtractApiSportsPlayerStats(response.Payload.RootElement);
            var coverage = BuildApiSportsPropCoverage(playerStats);
            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = true,
                readOnly = true,
                provider = "API-Sports",
                product = "API-Sports Football",
                request = new { fixtureId, forceLive = true },
                response.Results,
                response.RequestsCurrent,
                response.RequestsLimit,
                response.SanitizedUrl,
                fieldsDiscovered = playerStats.SelectMany(row => row.StatKeys).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                playerRowsInspected = playerStats.Count,
                coverage
            });
        })
        .WithName("ApiSportsFixturePropCoverageDebug")
        .WithSummary("Force-live gated API-Sports Football coverage matrix for PrizePicks-style Soccer props.");

        endpoints.MapPost("/api/debug/soccer/results/api-sports/import-fixture", async (
            ApiSportsSoccerImportFixtureRequest request,
            IOptions<ApiSportsOptions> options,
            IApiSportsFootballClient client,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.FixtureId))
            {
                return Results.BadRequest(new { error = "fixtureId is required." });
            }

            var fixtureId = request.FixtureId.Trim();
            var dryRun = request.DryRun != false;
            var requestDiagnostics = new { fixtureId, dryRun, forceLive = request.ForceLive == true };
            var block = BuildApiSportsBlockedResponse(
                "soccer-results-import-fixture",
                request.ForceLive,
                options.Value,
                requestDiagnostics,
                requireConfiguredLeague: false,
                requireConfiguredSeason: false);
            if (block is not null)
            {
                return block;
            }

            var fixtureResponse = await client.GetFixturesAsync(
                new ApiSportsFootballFixturesRequest(null, fixtureId, null, null),
                cancellationToken);
            var fixtureSummary = ExtractApiSportsFixtureSummary(fixtureResponse.Payload.RootElement, fixtureId);
            if (fixtureSummary.EventDate is null)
            {
                return Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = true,
                    externalApiCalled = true,
                    readOnly = dryRun,
                    provider = "API-Sports",
                    product = "API-Sports Football",
                    request = requestDiagnostics,
                    blocked = true,
                    blockReason = "MissingFixtureDate",
                    fixtureSummary,
                    message = "API-Sports fixture metadata did not include a usable fixture date, so prop_results cannot be written."
                });
            }

            var statsResponse = await client.GetFixturePlayerStatsAsync(fixtureId, cancellationToken);
            var playerStats = ExtractApiSportsPlayerStats(statsResponse.Payload.RootElement);
            var mappings = await LoadApiSportsSoccerResultMappingsAsync(dbContext, cancellationToken);
            var preview = BuildApiSportsSoccerResultPreview(fixtureId, fixtureSummary.EventDate.Value, playerStats, mappings);
            ApiSportsSoccerImportWriteSummary writeSummary;
            try
            {
                writeSummary = dryRun
                    ? new ApiSportsSoccerImportWriteSummary(0, 0)
                    : await UpsertApiSportsSoccerResultsAsync(dbContext, fixtureSummary, preview.Rows, cancellationToken);
            }
            catch (ApiSportsPropTypeResolutionException exception)
            {
                return Results.Conflict(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = true,
                    externalApiCalled = true,
                    readOnly = dryRun,
                    provider = "API-Sports",
                    product = "API-Sports Football",
                    request = requestDiagnostics,
                    blocked = true,
                    blockReason = "PropTypeResolutionFailed",
                    stage = "PropTypeResolution",
                    conflicts = exception.Conflicts,
                    message = "API-Sports Soccer import could not resolve prop types because duplicate names had conflicting metadata."
                });
            }
            var ruleConfidenceCounts = preview.Rows
                .GroupBy(row => row.RuleConfidence)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = true,
                externalApiCalled = true,
                readOnly = dryRun,
                provider = "API-Sports",
                product = "API-Sports Football",
                request = requestDiagnostics,
                fixtureSummary,
                fixtureDiagnostics = new
                {
                    fixtureResponse.SanitizedUrl,
                    fixtureResponse.Results,
                    errors = ExtractApiSportsErrors(fixtureResponse.Payload.RootElement)
                },
                playerStatsDiagnostics = new
                {
                    statsResponse.SanitizedUrl,
                    statsResponse.Results,
                    errors = ExtractApiSportsErrors(statsResponse.Payload.RootElement)
                },
                teamsRead = playerStats.Select(row => row.TeamName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                playersRead = playerStats.Count,
                playersWithMinutes = playerStats.Count(row => row.Minutes.GetValueOrDefault() > 0),
                resultRowsPreviewed = preview.Rows.Count,
                resultRowsWritten = writeSummary.Inserted + writeSummary.Updated,
                resultRowsInserted = writeSummary.Inserted,
                resultRowsUpdated = writeSummary.Updated,
                unsupportedSkipped = preview.UnsupportedSkipped.Count,
                unsupportedSkippedSummary = preview.UnsupportedSkipped
                    .GroupBy(row => row.Reason)
                    .OrderBy(group => group.Key)
                    .Select(group => new { reason = group.Key, count = group.Count(), props = group.Select(row => row.PropType).Distinct().Order().ToArray() })
                    .ToArray(),
                mappingDiagnostics = BuildApiSportsSoccerMappingDiagnostics(preview),
                ruleConfidenceCounts,
                rawFieldCoverage = playerStats.SelectMany(row => row.StatKeys).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                previewRowsByPlayer = preview.Rows
                    .GroupBy(row => new { row.PlayerId, row.PlayerName, row.TeamId, row.TeamName, row.Position, row.Minutes })
                    .OrderBy(group => group.Key.TeamName)
                    .ThenBy(group => group.Key.PlayerName)
                    .Select(group => new
                    {
                        group.Key.PlayerId,
                        group.Key.PlayerName,
                        group.Key.TeamId,
                        group.Key.TeamName,
                        group.Key.Position,
                        group.Key.Minutes,
                        rows = group.OrderBy(row => row.PropType).Select(row => new
                        {
                            row.PropType,
                            row.StatSourceFields,
                            row.RawStatValue,
                            row.Value,
                            row.RuleConfidence,
                            row.IsGradeable,
                            row.PublicGradingEnabled,
                            row.MappingSource,
                            row.Notes,
                            row.ExternalSourceIdentifier
                        }).ToArray()
                    })
                    .ToArray()
            });
        })
        .WithName("ApiSportsSoccerImportFixtureDebug")
        .WithSummary("Force-live gated debug import of one API-Sports Football fixture into Soccer prop_results.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/import-fixture/status", async (
            string fixtureId,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var sourcePrefix = $"{ApiSportsFootballResultSource}:{fixtureId.Trim()}:";
            var rows = await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.Player)
                .Include(result => result.PropType)
                .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                    && result.ExternalSourceIdentifier.StartsWith(sourcePrefix))
                .Select(result => new
                {
                    result.Id,
                    result.EventDate,
                    result.PlayerId,
                    playerName = result.Player == null ? null : result.Player.DisplayName,
                    result.PropTypeId,
                    propType = result.PropType == null ? null : result.PropType.Name,
                    result.ActualResultValue,
                    result.ExternalSourceIdentifier,
                    result.CreatedAt,
                    result.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                provider = "API-Sports",
                product = "API-Sports Football",
                request = new { fixtureId },
                fixtureId,
                rowCount = rows.Count,
                countsByPropType = rows
                    .GroupBy(row => row.propType ?? "Unknown")
                    .OrderBy(group => group.Key)
                    .Select(group => new { propType = group.Key, count = group.Count() })
                    .ToArray(),
                rows = rows.OrderBy(row => row.playerName).ThenBy(row => row.propType).ToArray()
            });
        })
        .WithName("ApiSportsSoccerImportFixtureStatusDebug")
        .WithSummary("Read-only local status for API-Sports Football fixture result imports.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/historical/coverage-status", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? enabledOnly,
            IOptions<ApiSportsOptions> options,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsHistoricalCoverageDateRange(fromDate, toDate);
            var competitions = GetConfiguredApiSportsHistoricalCompetitions(options.Value, enabledOnly != false);
            var statKeysOfInterest = ApiSportsHistoricalStatKeysOfInterest;
            var sport = await dbContext.Sports
                .AsNoTracking()
                .SingleOrDefaultAsync(row => row.Code == "SOCCER", cancellationToken);
            var leagueCodes = competitions
                .Select(row => BuildApiSportsHistoricalLeagueCode(row))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var leagues = sport is null || leagueCodes.Length == 0
                ? []
                : await dbContext.Leagues
                    .AsNoTracking()
                    .Where(row => row.SportId == sport.Id && leagueCodes.Contains(row.Code))
                    .Select(row => new { row.Id, row.Code, row.Name })
                    .ToListAsync(cancellationToken);
            var leagueIdByCode = leagues.ToDictionary(row => row.Code, row => row.Id, StringComparer.OrdinalIgnoreCase);
            var leagueIds = leagueIdByCode.Values.ToArray();

            var rows = sport is null || leagueIds.Length == 0
                ? []
                : await dbContext.PropResults
                    .AsNoTracking()
                    .Include(result => result.PropType)
                    .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                        && leagueIds.Contains(result.LeagueId)
                        && result.EventDate >= range.FromDate
                        && result.EventDate <= range.ToDate
                        && result.PropType != null
                        && statKeysOfInterest.Contains(result.PropType.StatKey))
                    .Select(result => new
                    {
                        result.LeagueId,
                        result.EventDate,
                        PropTypeName = result.PropType!.Name,
                        StatKey = result.PropType.StatKey,
                        result.PlayerId,
                        FixtureId = ExtractApiSportsFixtureId(result.ExternalSourceIdentifier)
                    })
                    .ToListAsync(cancellationToken);

            var coverageByCompetition = competitions
                .Select(competition =>
                {
                    var leagueCode = BuildApiSportsHistoricalLeagueCode(competition);
                    var leagueId = leagueIdByCode.TryGetValue(leagueCode, out var id) ? id : (Guid?)null;
                    var competitionRows = leagueId.HasValue
                        ? rows.Where(row => row.LeagueId == leagueId.Value).ToArray()
                        : [];
                    var importedStatKeys = competitionRows
                        .Select(row => row.StatKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return new
                    {
                        competition.Key,
                        competition.DisplayName,
                        competition.LeagueId,
                        competition.Season,
                        competition.Enabled,
                        competition.Country,
                        competition.Type,
                        localLeagueCode = leagueCode,
                        localLeagueId = leagueId,
                        localLeagueExists = leagueId.HasValue,
                        importedResultRows = competitionRows.Length,
                        importedFixtureCount = competitionRows
                            .Select(row => row.FixtureId)
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count(),
                        distinctPlayers = competitionRows.Select(row => row.PlayerId).Distinct().Count(),
                        firstImportedEventDate = competitionRows.Length == 0 ? (DateOnly?)null : competitionRows.Min(row => row.EventDate),
                        latestImportedEventDate = competitionRows.Length == 0 ? (DateOnly?)null : competitionRows.Max(row => row.EventDate),
                        importedStatKeys,
                        missingStatKeys = statKeysOfInterest.Except(importedStatKeys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                        rowsByStat = statKeysOfInterest
                            .Select(statKey =>
                            {
                                var statRows = competitionRows.Where(row => string.Equals(row.StatKey, statKey, StringComparison.OrdinalIgnoreCase)).ToArray();
                                return new
                                {
                                    statKey,
                                    displayName = statRows.FirstOrDefault()?.PropTypeName ?? ToApiSportsHistoricalStatDisplayName(statKey),
                                    resultRows = statRows.Length,
                                    distinctPlayers = statRows.Select(row => row.PlayerId).Distinct().Count(),
                                    fixtureCount = statRows
                                        .Select(row => row.FixtureId)
                                        .Where(value => !string.IsNullOrWhiteSpace(value))
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .Count(),
                                    firstEventDate = statRows.Length == 0 ? (DateOnly?)null : statRows.Min(row => row.EventDate),
                                    latestEventDate = statRows.Length == 0 ? (DateOnly?)null : statRows.Max(row => row.EventDate)
                                };
                            })
                            .ToArray()
                    };
                })
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                provider = "API-Sports",
                product = "API-Sports Football",
                scope = new
                {
                    sport = "SOCCER",
                    purpose = "HistoricalPlayerStatsCoverageStatus",
                    range.FromDate,
                    range.ToDate
                },
                guardrails = new
                {
                    publicSoccerGradingEnabled = false,
                    soccerPredictionsEnabled = false,
                    soccerEntryBuilderEnabled = false,
                    importsRows = false
                },
                configuredCompetitionCount = competitions.Count,
                enabledCompetitionCount = competitions.Count(row => row.Enabled),
                statsOfInterest = statKeysOfInterest.Select(statKey => new { statKey, displayName = ToApiSportsHistoricalStatDisplayName(statKey) }).ToArray(),
                totalImportedResultRows = rows.Count,
                totalImportedFixtureCount = rows
                    .Select(row => row.FixtureId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                totalDistinctPlayers = rows.Select(row => row.PlayerId).Distinct().Count(),
                competitions = coverageByCompetition,
                recommendedNextAction = "Use this persisted/config-only status to confirm league config and local coverage before adding a dry-run historical backfill endpoint. Public Soccer grading remains disabled."
            });
        })
        .WithName("ApiSportsSoccerHistoricalCoverageStatusDebug")
        .WithSummary("Read-only config and persisted-result coverage status for API-Sports Soccer historical player stats.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/world-cup/import-status", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? forceLive,
            IOptions<ApiSportsOptions> options,
            IApiSportsFootballClient client,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var persisted = await LoadApiSportsWorldCupPersistedImportStatusAsync(dbContext, range.FromDate, range.ToDate, cancellationToken);
            IReadOnlyList<ApiSportsFixtureDiscoveryRow> discovered = [];
            var warnings = new List<string>();

            if (forceLive == true)
            {
                var block = BuildApiSportsBlockedResponse(
                    "soccer-world-cup-import-status",
                    forceLive,
                    options.Value,
                    new { range.FromDate, range.ToDate, forceLive = true },
                    requireConfiguredLeague: false,
                    requireConfiguredSeason: false);
                if (block is not null)
                {
                    return block;
                }

                discovered = await DiscoverApiSportsWorldCupFixturesAsync(client, range.FromDate, range.ToDate, cancellationToken);
            }
            else
            {
                warnings.Add("forceLive=false; status is based on persisted local prop_results only and does not discover API-Sports fixtures.");
            }

            var completed = discovered.Where(row => IsApiSportsFixtureCompleted(row.StatusShort, row.StatusLong)).ToArray();
            var importedFixtureIds = persisted.RowCountsByFixture.Select(row => row.FixtureId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingCompleted = completed
                .Where(row => !importedFixtureIds.Contains(row.FixtureId))
                .Select(row => new
                {
                    row.FixtureId,
                    row.EventDate,
                    row.KickoffUtc,
                    row.HomeTeamName,
                    row.AwayTeamName,
                    row.StatusShort,
                    row.StatusLong
                })
                .ToArray();
            var coverageByDate = BuildApiSportsWorldCupStatusCoverageByDate(
                range.FromDate,
                range.ToDate,
                persisted.RowCountsByFixture,
                discovered,
                forceLive == true,
                forceLive == true ? CountDateRangeDays(range.FromDate, range.ToDate) : null);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = forceLive == true,
                readOnly = true,
                provider = "API-Sports",
                product = "API-Sports Football",
                scope = new
                {
                    sport = "SOCCER",
                    competition = "World Cup",
                    leagueId = ApiSportsWorldCupLeagueId,
                    season = ApiSportsWorldCupSeason,
                    range.FromDate,
                    range.ToDate
                },
                configured = new
                {
                    leagueId = options.Value.Football.WorldCupLeagueId,
                    season = options.Value.Football.Season
                },
                discoveryAvailable = forceLive == true,
                apiCallsUsed = forceLive == true ? CountDateRangeDays(range.FromDate, range.ToDate) : 0,
                discoveredFixtureCount = forceLive == true ? discovered.Count : (int?)null,
                completedFixtureCount = forceLive == true ? completed.Length : (int?)null,
                importedFixtureCount = persisted.RowCountsByFixture.Count,
                missingCompletedFixtureCount = forceLive == true ? missingCompleted.Length : (int?)null,
                completedFixturesDiscovered = completed,
                coverageByDate,
                rowsByFixture = persisted.RowCountsByFixture,
                importedFixtures = persisted.RowCountsByFixture,
                missingCompletedFixtures = missingCompleted,
                persisted.TotalRows,
                persisted.LatestImportedFixtureId,
                latestImportedEventDate = persisted.LatestImportedDate,
                persisted.LatestImportedAtUtc,
                warnings
            });
        })
        .WithName("ApiSportsSoccerWorldCupImportStatusDebug")
        .WithSummary("Read-only World Cup 2026 API-Sports Soccer import coverage diagnostics.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/world-cup/coverage", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? fixtureId,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var normalizedFixtureId = string.IsNullOrWhiteSpace(fixtureId) ? null : fixtureId.Trim();
            var sampleLimit = Math.Clamp(maxSamples ?? 25, 1, 100);
            var resultRows = await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.Player)
                .Include(result => result.PropType)
                .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                    && result.EventDate >= range.FromDate
                    && result.EventDate <= range.ToDate
                    && result.ExternalSourceIdentifier.StartsWith($"{ApiSportsFootballResultSource}:"))
                .Select(result => new ApiSportsCoverageResultRow(
                    result.Id,
                    result.EventDate,
                    ExtractApiSportsFixtureId(result.ExternalSourceIdentifier),
                    ExtractApiSportsPlayerId(result.ExternalSourceIdentifier),
                    result.PlayerId,
                    result.Player == null ? string.Empty : result.Player.DisplayName,
                    NormalizeResultPlayerName(result.Player == null ? null : result.Player.DisplayName),
                    result.PropTypeId,
                    result.PropType == null ? string.Empty : result.PropType.Name,
                    result.PropType == null ? string.Empty : result.PropType.StatKey,
                    NormalizeResultPropTypeName(result.PropType == null ? null : result.PropType.Name),
                    result.ActualResultValue,
                    result.ExternalSourceIdentifier,
                    null,
                    null))
                .ToListAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(normalizedFixtureId))
            {
                resultRows = resultRows
                    .Where(row => string.Equals(row.FixtureId, normalizedFixtureId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var soccerSportId = await GetSportIdAsync(dbContext, "SOCCER", cancellationToken);
            var resultApiSportsPlayerIds = resultRows
                .Select(row => row.ApiSportsPlayerId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var approvedPlayerLinks = soccerSportId.HasValue && resultApiSportsPlayerIds.Length > 0
                ? await dbContext.ProviderEntityLinks
                    .AsNoTracking()
                    .Where(link => link.SportId == soccerSportId.Value
                        && link.EntityType == "Player"
                        && link.ProviderKey == ApiSportsFootballPlayerRefProvider
                        && link.LinkStatus == "Approved"
                        && link.CanonicalEntityId.HasValue
                        && resultApiSportsPlayerIds.Contains(link.ProviderEntityId))
                    .ToListAsync(cancellationToken)
                : [];
            var approvedPlayerLinksByProviderId = approvedPlayerLinks
                .GroupBy(link => link.ProviderEntityId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(link => link.UpdatedAtUtc).First(), StringComparer.OrdinalIgnoreCase);
            resultRows = resultRows
                .Select(row => row.ApiSportsPlayerId is not null && approvedPlayerLinksByProviderId.TryGetValue(row.ApiSportsPlayerId, out var link)
                    ? row with
                    {
                        ApprovedCanonicalPlayerId = link.CanonicalEntityId,
                        ApprovedCanonicalPlayerName = link.CanonicalEntityName
                    }
                    : row)
                .ToList();

            var soccerPlatformProps = await dbContext.PlatformProps
                .AsNoTracking()
                .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Player)
                .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.PropType)
                .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
                .Where(platformProp => platformProp.Prop != null
                    && platformProp.Prop.League != null
                    && platformProp.Prop.League.Sport != null
                    && platformProp.Prop.League.Sport.Code == "SOCCER")
                .Select(platformProp => new ApiSportsCoverageMarketRow(
                    "PlatformProp",
                    platformProp.Prop!.PlayerId,
                    platformProp.Prop.Player == null ? string.Empty : platformProp.Prop.Player.DisplayName,
                    NormalizeResultPlayerName(platformProp.Prop.Player == null ? null : platformProp.Prop.Player.DisplayName),
                    platformProp.Prop.PropTypeId,
                    platformProp.Prop.PropType == null ? string.Empty : platformProp.Prop.PropType.Name,
                    platformProp.Prop.PropType == null ? string.Empty : platformProp.Prop.PropType.StatKey,
                    NormalizeResultPropTypeName(platformProp.Prop.PropType == null ? null : platformProp.Prop.PropType.Name),
                    DateOnly.FromDateTime(platformProp.Prop.StartsAt.UtcDateTime),
                    platformProp.PlatformPropId,
                    platformProp.ExternalStableKey,
                    platformProp.Status))
                .ToListAsync(cancellationToken);
            var soccerTopSnapshots = await dbContext.MarketTopPickSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.Sport == "SOCCER"
                    && snapshot.SlateDate >= range.FromDate
                    && snapshot.SlateDate <= range.ToDate)
                .Select(snapshot => new ApiSportsCoverageSnapshotRow(
                    "TopPickSnapshot",
                    snapshot.Player,
                    NormalizeResultPlayerName(snapshot.Player),
                    snapshot.PropType,
                    string.Empty,
                    NormalizeResultPropTypeName(snapshot.PropType),
                    snapshot.EventStartTime.HasValue ? DateOnly.FromDateTime(snapshot.EventStartTime.Value.UtcDateTime) : snapshot.SlateDate,
                    snapshot.EventId,
                    snapshot.Bookmaker,
                    snapshot.ProviderCategory))
                .ToListAsync(cancellationToken);
            var soccerLineSnapshots = await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.Sport == "SOCCER"
                    && snapshot.SlateDate >= range.FromDate
                    && snapshot.SlateDate <= range.ToDate)
                .Select(snapshot => new ApiSportsCoverageSnapshotRow(
                    "LineShoppingSnapshot",
                    snapshot.Player,
                    NormalizeResultPlayerName(snapshot.Player),
                    snapshot.PropType,
                    string.Empty,
                    NormalizeResultPropTypeName(snapshot.PropType),
                    snapshot.EventStartTime.HasValue ? DateOnly.FromDateTime(snapshot.EventStartTime.Value.UtcDateTime) : snapshot.SlateDate,
                    snapshot.EventId,
                    snapshot.Bookmaker,
                    snapshot.ProviderCategory))
                .ToListAsync(cancellationToken);
            var soccerRecommendationSnapshots = await dbContext.RecommendationSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.Sport == "SOCCER"
                    && snapshot.ExpectedResultDate >= range.FromDate
                    && snapshot.ExpectedResultDate <= range.ToDate)
                .Select(snapshot => new ApiSportsCoverageSnapshotRow(
                    "RecommendationSnapshot",
                    snapshot.PlayerName,
                    snapshot.NormalizedPlayerName,
                    snapshot.PropType,
                    string.Empty,
                    snapshot.NormalizedPropType,
                    snapshot.ExpectedResultDate,
                    snapshot.EventId,
                    snapshot.Bookmaker,
                    snapshot.ProviderCategory))
                .ToListAsync(cancellationToken);

            var snapshotRows = soccerTopSnapshots.Concat(soccerLineSnapshots).Concat(soccerRecommendationSnapshots).ToArray();
            var marketRows = soccerPlatformProps.ToArray();
            var noMarketDataAvailable = marketRows.Length == 0 && snapshotRows.Length == 0;
            var resultDiagnostics = resultRows.Select(row => BuildApiSportsCoverageMatch(row, marketRows, snapshotRows, noMarketDataAvailable)).ToArray();
            var matchedPlatformCount = resultDiagnostics.Count(row => row.MatchedToPlatformProps);
            var matchedSnapshotCount = resultDiagnostics.Count(row => row.MatchedToSnapshots);
            var matchedAnyCount = resultDiagnostics.Count(row => row.MatchedToPlatformProps || row.MatchedToSnapshots);
            var unmatched = resultDiagnostics.Where(row => !row.MatchedToPlatformProps && !row.MatchedToSnapshots).ToArray();
            var resultRowsWithApprovedPlayerLink = resultRows.Count(row => row.ApprovedCanonicalPlayerId.HasValue);
            var mappingSet = await LoadApiSportsSoccerResultMappingsAsync(dbContext, cancellationToken);
            var mappingByStatKey = mappingSet.Mappings
                .GroupBy(mapping => mapping.StatKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var rowsByPropType = resultRows
                .GroupBy(row => new { row.PropTypeId, row.PropTypeName, row.StatKey })
                .OrderBy(group => group.Key.PropTypeName)
                .Select(group => new
                {
                    propTypeId = group.Key.PropTypeId,
                    displayName = group.Key.PropTypeName,
                    statKey = group.Key.StatKey,
                    rowCount = group.Count(),
                    ruleConfidence = mappingByStatKey.TryGetValue(group.Key.StatKey, out var mapping) ? mapping.RuleConfidence : "Unknown",
                    isGradeable = mappingByStatKey.TryGetValue(group.Key.StatKey, out var gradeableMapping) && gradeableMapping.IsGradeable
                })
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                scope = new
                {
                    sport = "SOCCER",
                    provider = "API-Sports",
                    product = "API-Sports Football",
                    competition = "World Cup",
                    season = ApiSportsWorldCupSeason,
                    range.FromDate,
                    range.ToDate,
                    fixtureId = normalizedFixtureId
                },
                importedResultSummary = new
                {
                    totalRows = resultRows.Count,
                    distinctFixtures = resultRows.Select(row => row.FixtureId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    distinctPlayers = resultRows.Select(row => row.PlayerId).Distinct().Count(),
                    rowsByFixture = resultRows
                        .GroupBy(row => new { row.FixtureId, row.EventDate })
                        .OrderBy(group => group.Key.EventDate)
                        .ThenBy(group => group.Key.FixtureId)
                        .Select(group => new { group.Key.FixtureId, group.Key.EventDate, rowCount = group.Count() })
                        .ToArray(),
                    rowsByPropType,
                    rowsByRuleConfidence = rowsByPropType
                        .GroupBy(row => row.ruleConfidence)
                        .OrderBy(group => group.Key)
                        .Select(group => new { ruleConfidence = group.Key, rowCount = group.Sum(row => row.rowCount) })
                        .ToArray()
                },
                mappingDiagnostics = new
                {
                    providerMappingsConfigured = mappingSet.DbMappingsConfigured,
                    dbMappingsUsed = mappingSet.DbMappingsUsed,
                    fallbackMappingsUsed = mappingSet.FallbackMappingsUsed,
                    missingMappingCount = 0,
                    mappingsByConfidence = mappingSet.Mappings
                        .GroupBy(mapping => mapping.RuleConfidence)
                        .OrderBy(group => group.Key)
                        .Select(group => new { ruleConfidence = group.Key, mappingCount = group.Count() })
                        .ToArray(),
                    mappingsByGradeable = mappingSet.Mappings
                        .GroupBy(mapping => mapping.IsGradeable)
                        .OrderByDescending(group => group.Key)
                        .Select(group => new { isGradeable = group.Key, mappingCount = group.Count() })
                        .ToArray()
                },
                propTypeCoverage = rowsByPropType.Select(row => new
                {
                    row.propTypeId,
                    row.statKey,
                    row.displayName,
                    row.rowCount,
                    row.ruleConfidence,
                    gradeableNow = row.isGradeable,
                    publicSoccerGradingEnabled = false
                }).ToArray(),
                marketJoinCoverage = new
                {
                    totalImportedResults = resultRows.Count,
                    matchedToPlatformProps = matchedPlatformCount,
                    matchedToSnapshots = matchedSnapshotCount,
                    matchedAny = matchedAnyCount,
                    unmatchedCount = unmatched.Length,
                    matchRate = resultRows.Count == 0 ? 0 : decimal.Round((decimal)matchedAnyCount / resultRows.Count, 4),
                    noMarketDataAvailable,
                    soccerPlatformProps = marketRows.Length,
                    soccerSnapshots = snapshotRows.Length,
                    soccerTopPickSnapshots = soccerTopSnapshots.Count,
                    soccerLineShoppingSnapshots = soccerLineSnapshots.Count,
                    soccerRecommendationSnapshots = soccerRecommendationSnapshots.Count,
                    identifiersMissing = new[]
                    {
                        "prop_results do not store provider/bookmaker/market keys.",
                        "market snapshots do not store API-Sports fixture ids.",
                        "snapshot matching uses normalized player name, prop type display name, and event/result date."
                    }
                },
                playerLinkCoverage = new
                {
                    approvedProviderLinksUsed = resultRows
                        .Where(row => row.ApprovedCanonicalPlayerId.HasValue)
                        .Select(row => row.ApiSportsPlayerId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    resultRowsWithApprovedPlayerLink,
                    resultRowsWithoutApprovedPlayerLink = resultRows.Count - resultRowsWithApprovedPlayerLink,
                    playerLinkCoverageRate = resultRows.Count == 0 ? 0 : decimal.Round((decimal)resultRowsWithApprovedPlayerLink / resultRows.Count, 4),
                    note = "Approved provider identity links are used only by this debug coverage diagnostic. Public Soccer grading remains disabled."
                },
                mismatchBreakdown = new
                {
                    playerMissing = unmatched.Count(row => row.Reason == "PlayerMissing"),
                    propTypeMissing = unmatched.Count(row => row.Reason == "PropTypeMissing"),
                    dateMismatch = unmatched.Count(row => row.Reason == "DateMismatch"),
                    eventMismatch = unmatched.Count(row => row.Reason == "EventIdentifierUnavailable"),
                    marketMissing = unmatched.Count(row => row.Reason == "MarketMissing"),
                    unknown = unmatched.Count(row => row.Reason == "Unknown")
                },
                samples = includeSamples == true ? new
                {
                    matched = resultDiagnostics
                        .Where(row => row.MatchedToPlatformProps || row.MatchedToSnapshots)
                        .Take(sampleLimit)
                        .ToArray(),
                    unmatchedPlayers = unmatched.Where(row => row.Reason == "PlayerMissing").Take(sampleLimit).ToArray(),
                    unmatchedPropTypes = unmatched.Where(row => row.Reason == "PropTypeMissing").Take(sampleLimit).ToArray(),
                    marketMissing = unmatched.Where(row => row.Reason == "MarketMissing").Take(sampleLimit).ToArray(),
                    dateMismatches = unmatched.Where(row => row.Reason == "DateMismatch").Take(sampleLimit).ToArray()
                } : null,
                publicSoccerGradingEnabled = false,
                recommendation = matchedAnyCount > 0
                    ? "Imported Soccer results have at least one local market join candidate, but public Soccer grading remains disabled until matching is reviewed."
                    : "Imported Soccer results are persisted, but no safe market join path was proven in this diagnostic range."
            });
        })
        .WithName("ApiSportsSoccerWorldCupCoverageDebug")
        .WithSummary("Read-only local coverage diagnostics for imported API-Sports World Cup Soccer prop_results.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/world-cup/market-join-preview", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? fixtureId,
            string? providerEventId,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var normalizedFixtureId = string.IsNullOrWhiteSpace(fixtureId) ? null : fixtureId.Trim();
            var normalizedProviderEventId = string.IsNullOrWhiteSpace(providerEventId) ? null : providerEventId.Trim();
            var sampleLimit = Math.Clamp(maxSamples ?? 50, 1, 250);
            var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, normalizedFixtureId, cancellationToken);
            var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, normalizedProviderEventId, cancellationToken);
            var preview = BuildApiSportsMarketJoinPreview(resultRows, platformRows, sampleLimit);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    fixtureId = normalizedFixtureId,
                    providerEventId = normalizedProviderEventId
                },
                summary = preview.Summary,
                blockers = preview.Blockers,
                samples = includeSamples != false ? preview.Samples : null,
                recommendation = preview.Recommendation
            });
        })
        .WithName("ApiSportsSoccerWorldCupMarketJoinPreviewDebug")
        .WithSummary("Read-only bridge diagnostic comparing API-Sports World Cup result rows to The Odds Soccer platform props by event, player, and prop.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/world-cup/grading-preview", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? fixtureId,
            string? providerEventId,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var normalizedFixtureId = string.IsNullOrWhiteSpace(fixtureId) ? null : fixtureId.Trim();
            var normalizedProviderEventId = string.IsNullOrWhiteSpace(providerEventId) ? null : providerEventId.Trim();
            var sampleLimit = Math.Clamp(maxSamples ?? 50, 1, 250);
            var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, normalizedFixtureId, cancellationToken);
            var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, normalizedProviderEventId, cancellationToken);
            var preview = BuildApiSportsSoccerGradingPreview(resultRows, platformRows, sampleLimit);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    fixtureId = normalizedFixtureId,
                    providerEventId = normalizedProviderEventId
                },
                summary = preview.Summary,
                blockers = preview.Blockers,
                samples = includeSamples != false ? preview.Samples : null,
                recommendation = preview.Recommendation
            });
        })
        .WithName("ApiSportsSoccerWorldCupGradingPreviewDebug")
        .WithSummary("Read-only debug-only Soccer grading preview for joinable API-Sports result rows and The Odds platform props.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeFixtureDetails,
            bool? includeSamples,
            int? maxFixtures,
            int? maxSamplesPerFixture,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var fixtureLimit = Math.Clamp(maxFixtures ?? 50, 1, 250);
            var sampleLimit = Math.Clamp(maxSamplesPerFixture ?? 5, 0, 25);
            var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, null, cancellationToken);
            var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, null, cancellationToken);
            var coverage = BuildApiSportsSoccerGradingCoverageSummary(
                resultRows,
                platformRows,
                includeFixtureDetails != false,
                includeSamples != false,
                fixtureLimit,
                sampleLimit);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    maxFixtures = fixtureLimit,
                    maxSamplesPerFixture = sampleLimit
                },
                summary = coverage.Summary,
                topBlockersAggregated = coverage.TopBlockersAggregated,
                bestCoverageFixtures = coverage.BestCoverageFixtures,
                worstCoverageFixtures = coverage.WorstCoverageFixtures,
                fixtureDetails = includeFixtureDetails != false ? coverage.FixtureDetails : null,
                unresolvedProviderEvents = coverage.UnresolvedProviderEvents,
                resultOnlyFixtures = coverage.ResultOnlyFixtures,
                platformOnlyEvents = coverage.PlatformOnlyEvents,
                knownWorkingPairDetected = coverage.KnownWorkingPairDetected,
                recommendedNextAction = coverage.RecommendedNextAction
            });
        })
        .WithName("ApiSportsSoccerWorldCupGradingCoverageSummaryDebug")
        .WithSummary("Read-only Soccer World Cup grading coverage summary across persisted completed fixture/provider event pairs.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var sampleLimit = Math.Clamp(maxSamples ?? 100, 1, 500);
            var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, null, cancellationToken);
            var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, null, cancellationToken);
            var apiSportsMappings = await LoadApiSportsSoccerProviderStatMappingRowsAsync(dbContext, cancellationToken);
            var diagnostic = BuildApiSportsSoccerPropMappingGapDiagnostic(
                resultRows,
                platformRows,
                apiSportsMappings,
                includeSamples != false,
                sampleLimit);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    maxSamples = sampleLimit
                },
                summary = diagnostic.Summary,
                theOddsMarketGaps = diagnostic.TheOddsMarketGaps,
                apiSportsStatGaps = diagnostic.ApiSportsStatGaps,
                gapsByCategory = diagnostic.GapsByCategory,
                lowRiskMappingCandidates = diagnostic.LowRiskMappingCandidates,
                needsReviewMappingCandidates = diagnostic.NeedsReviewMappingCandidates,
                ignoredNoise = diagnostic.IgnoredNoise,
                recommendedNextAction = diagnostic.RecommendedNextAction
            });
        })
        .WithName("ApiSportsSoccerWorldCupPropMappingGapDebug")
        .WithSummary("Read-only Soccer World Cup prop/stat mapping gap diagnostic across resolved The Odds/API-Sports fixture pairs.");

        endpoints.MapGet("/api/debug/soccer/results/api-sports/world-cup/stat-mapping-review", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var sampleLimit = Math.Clamp(maxSamples ?? 100, 1, 500);
            var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, null, cancellationToken);
            var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, null, cancellationToken);
            var apiSportsMappings = await LoadApiSportsSoccerProviderStatMappingRowsAsync(dbContext, cancellationToken);
            var propGap = BuildApiSportsSoccerPropMappingGapDiagnostic(
                resultRows,
                platformRows,
                apiSportsMappings,
                includeSamples != false,
                sampleLimit);
            var review = BuildApiSportsSoccerStatMappingReview(
                resultRows,
                platformRows,
                apiSportsMappings,
                propGap,
                includeSamples != false,
                sampleLimit);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    maxSamples = sampleLimit
                },
                summary = review.Summary,
                mappings = review.Mappings,
                importedStatNoise = review.ImportedStatNoise,
                recommendedNextAction = review.RecommendedNextAction
            });
        })
        .WithName("ApiSportsSoccerWorldCupStatMappingReviewDebug")
        .WithSummary("Read-only Soccer World Cup needs-review/non-gradeable provider stat mapping review.");

        endpoints.MapGet("/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeResolved,
            bool? includeUnresolved,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var sampleLimit = Math.Clamp(maxSamples ?? 100, 1, 500);
            var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, null, cancellationToken);
            var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, null, cancellationToken);
            var coverage = BuildApiSportsSoccerFixtureMappingCoverage(
                resultRows,
                platformRows,
                includeResolved != false,
                includeUnresolved != false,
                includeSamples != false,
                sampleLimit);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    maxSamples = sampleLimit
                },
                summary = coverage.Summary,
                resolvedEvents = includeResolved != false ? coverage.ResolvedEvents : null,
                unresolvedEvents = includeUnresolved != false ? coverage.UnresolvedEvents : null,
                excludedEvents = coverage.ExcludedEvents,
                knownWorkingPairDetected = coverage.KnownWorkingPairDetected,
                recommendedNextAction = coverage.RecommendedNextAction
            });
        })
        .WithName("TheOddsApiSportsSoccerFixtureMappingCoverageDebug")
        .WithSummary("Read-only diagnostic for persisted The Odds Soccer provider event to API-Sports fixture mapping coverage.");

        endpoints.MapGet("/api/debug/soccer/the-odds/world-cup/ingestion-coverage", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var sampleLimit = Math.Clamp(maxSamples ?? 100, 1, 500);
            var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, null, cancellationToken);
            var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, null, cancellationToken);
            var mappingCoverage = BuildApiSportsSoccerFixtureMappingCoverage(
                resultRows,
                platformRows,
                includeResolved: true,
                includeUnresolved: true,
                includeSamples != false,
                sampleLimit);
            var coverage = await BuildTheOddsSoccerWorldCupIngestionCoverageAsync(
                dbContext,
                range,
                resultRows,
                platformRows,
                mappingCoverage,
                includeSamples != false,
                sampleLimit,
                cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    maxSamples = sampleLimit
                },
                summary = coverage.Summary,
                realProviderEvents = coverage.RealProviderEvents,
                syntheticOrLegacySoccerRows = coverage.SyntheticOrLegacySoccerRows,
                apiSportsFixtureResultCoverage = coverage.ApiSportsFixtureResultCoverage,
                latestSoccerRefreshRun = coverage.LatestSoccerRefreshRun,
                gapAnalysis = coverage.GapAnalysis,
                recommendedNextAction = coverage.RecommendedNextAction
            });
        })
        .WithName("TheOddsSoccerWorldCupIngestionCoverageDebug")
        .WithSummary("Read-only diagnostic for persisted The Odds Soccer World Cup provider event ingestion coverage.");

        endpoints.MapGet("/api/debug/soccer/world-cup/spike-status", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeDetails,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, null, cancellationToken);
            var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, null, cancellationToken);
            var mappingCoverage = BuildApiSportsSoccerFixtureMappingCoverage(
                resultRows,
                platformRows,
                includeResolved: true,
                includeUnresolved: true,
                includeSamples: includeDetails != false,
                maxSamples: 25);
            var ingestionCoverage = await BuildTheOddsSoccerWorldCupIngestionCoverageAsync(
                dbContext,
                range,
                resultRows,
                platformRows,
                mappingCoverage,
                includeDetails != false,
                sampleLimit: 25,
                cancellationToken);
            var gradingCoverage = BuildApiSportsSoccerGradingCoverageSummary(
                resultRows,
                platformRows,
                includeFixtureDetails: includeDetails != false,
                includeSamples: includeDetails != false,
                maxFixtures: 50,
                maxSamplesPerFixture: includeDetails != false ? 3 : 0);
            var apiSportsMappings = await LoadApiSportsSoccerProviderStatMappingRowsAsync(dbContext, cancellationToken);
            var propGap = BuildApiSportsSoccerPropMappingGapDiagnostic(
                resultRows,
                platformRows,
                apiSportsMappings,
                includeSamples: includeDetails != false,
                sampleLimit: 25);
            var aliasBatch = await BuildTheOddsApiSportsAliasBatchAsync(
                dbContext,
                range,
                maxFixtures: 50,
                sampleLimit: 5,
                minimumConfidence: 0.94m,
                includeExactNormalizedMatches: true,
                includeTokenSubsetMatches: true,
                maxLinks: 100,
                includePlan: true,
                cancellationToken);
            var aliasApprovalCounts = await CountApprovedTheOddsSoccerAliasLinksAsync(dbContext, cancellationToken);
            var nonGradeableMappingRows = propGap.ApiSportsStatGaps
                .Where(row => row.Category == "MappingExistsButNotGradeable")
                .Sum(row => row.RowCount);
            var importedStatNoiseRows = propGap.IgnoredNoise.Sum(row => row.RowCount);
            var blockers = BuildSoccerWorldCupSpikeStatusBlockers(
                gradingCoverage.Summary,
                propGap.Summary,
                aliasBatch.NeedsReviewRemaining + aliasBatch.AmbiguousRemaining,
                nonGradeableMappingRows);
            var readinessReasons = new[]
            {
                "Soccer World Cup is still a debug/research spike.",
                "Public Soccer grading is disabled.",
                "Soccer sportsbook context rows are research-only and not Entry Builder playable.",
                "Alias and provider stat mapping approvals remain manual.",
                "Needs-confirmation provider stat mappings are not public-gradeable."
            };

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                sport = "SOCCER",
                competition = "World Cup",
                publicSoccerGradingEnabled = false,
                entryBuilderEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate
                },
                productionReadiness = new
                {
                    status = "ResearchOnly",
                    canShowPublicMarketContext = true,
                    publicMarketContextCaution = "Persisted Soccer sportsbook context may be shown only as research-only market context.",
                    canShowPublicGradedPerformance = false,
                    canUseForEntryBuilder = false,
                    reasons = readinessReasons
                },
                summary = new
                {
                    ingestionCoverage.Summary.RealTheOddsProviderEvents,
                    ingestionCoverage.Summary.RealTheOddsPlatformRows,
                    ingestionCoverage.Summary.ApiSportsFixturesWithResults,
                    ingestionCoverage.Summary.ResolvedProviderEvents,
                    ingestionCoverage.Summary.UnresolvedProviderEvents,
                    aliasPlatformPlayerNamesEvaluated = aliasBatch.CandidatesEvaluated,
                    aliasCandidateCount = aliasBatch.AliasCandidateCount,
                    aliasLinksApproved = aliasApprovalCounts.TotalApproved,
                    aliasSafeCandidatesApproved = aliasApprovalCounts.SafeCandidateApproved,
                    aliasManualApprovalsApproved = aliasApprovalCounts.ManualApproved,
                    aliasNeedsReviewRemaining = aliasBatch.NeedsReviewRemaining,
                    aliasNoCandidateRemaining = aliasBatch.NoCandidateRemaining,
                    aliasAmbiguousRemaining = aliasBatch.AmbiguousRemaining,
                    aliasNewSafeCandidatesRemaining = aliasBatch.NewSafeCandidatesRemaining,
                    gradingCoverage.Summary.TotalCandidatePlatformRows,
                    gradingCoverage.Summary.TotalImportedResultRows,
                    gradingCoverage.Summary.TotalFullyJoinableRows,
                    gradingCoverage.Summary.TotalGradedPreviewRows,
                    gradingCoverage.Summary.TotalNoLineRows,
                    gradingCoverage.Summary.OverallGradeCoverageRate,
                    propMappingMissingRows = propGap.Summary.PropTypeMappingMissingRows,
                    importedStatNoiseRows,
                    nonGradeableMappingRows
                },
                guardrails = new
                {
                    publicEndpointsReadOnly = true,
                    publicSoccerGradingDisabled = true,
                    soccerEntryBuilderDisabled = true,
                    sportsbookContextResearchOnly = true,
                    autoAliasApprovalDisabled = true,
                    autoStatMappingApprovalDisabled = true,
                    needsConfirmationMappingsNotPublicGradeable = true
                },
                blockers,
                recommendedNextAction = BuildSoccerWorldCupSpikeStatusRecommendation(blockers, propGap.RecommendedNextAction, gradingCoverage.RecommendedNextAction),
                details = includeDetails != false
                    ? new
                    {
                        ingestion = ingestionCoverage.Summary,
                        fixtureMapping = mappingCoverage.Summary,
                        alias = new
                        {
                            aliasBatch.ResolvedPairsEvaluated,
                            aliasBatch.CandidatesEvaluated,
                            aliasBatch.SafeCandidates,
                            aliasCandidateCount = aliasBatch.AliasCandidateCount,
                            aliasLinksApproved = aliasApprovalCounts.TotalApproved,
                            aliasSafeCandidatesApproved = aliasApprovalCounts.SafeCandidateApproved,
                            aliasManualApprovalsApproved = aliasApprovalCounts.ManualApproved,
                            aliasNeedsReviewRemaining = aliasBatch.NeedsReviewRemaining,
                            aliasNoCandidateRemaining = aliasBatch.NoCandidateRemaining,
                            aliasAmbiguousRemaining = aliasBatch.AmbiguousRemaining,
                            aliasNewSafeCandidatesRemaining = aliasBatch.NewSafeCandidatesRemaining,
                            candidateGroups = aliasBatch.CandidateGroups
                        },
                        grading = gradingCoverage.Summary,
                        propMapping = propGap.Summary,
                        propMappingGapsByCategory = propGap.GapsByCategory,
                        propMappingGapsByApiSportsStatKey = propGap.GapsByApiSportsStatKey,
                        propMappingGapsByProviderMarketKey = propGap.GapsByProviderMarketKey
                    }
                    : null
            });
        })
        .WithName("SoccerWorldCupSpikeStatusDebug")
        .WithSummary("Read-only Soccer World Cup spike status and production guardrail summary.");

        endpoints.MapGet("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates", async (
            string? fixtureId,
            string? providerEventId,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var normalizedFixtureId = string.IsNullOrWhiteSpace(fixtureId) ? null : fixtureId.Trim();
            var normalizedProviderEventId = string.IsNullOrWhiteSpace(providerEventId) ? null : providerEventId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedFixtureId) || string.IsNullOrWhiteSpace(normalizedProviderEventId))
            {
                return Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    writesRows = false,
                    publicSoccerGradingEnabled = false,
                    error = "fixtureId and providerEventId are required."
                });
            }

            var sampleLimit = Math.Clamp(maxSamples ?? 50, 1, 250);
            var resultPlayers = await LoadApiSportsAliasFixturePlayersAsync(dbContext, normalizedFixtureId, cancellationToken);
            var platformPlayers = await LoadTheOddsAliasPlatformPlayersAsync(dbContext, normalizedProviderEventId, cancellationToken);
            var preview = BuildTheOddsApiSportsAliasCandidatePreview(resultPlayers, platformPlayers, sampleLimit);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    fixtureId = normalizedFixtureId,
                    providerEventId = normalizedProviderEventId
                },
                summary = preview.Summary,
                blockers = preview.Blockers,
                samples = includeSamples != false ? preview.Samples : null,
                recommendation = preview.Recommendation
            });
        })
        .WithName("TheOddsApiSportsSoccerAliasCandidatesDebug")
        .WithSummary("Read-only Soccer player alias candidate diagnostic for The Odds platform context rows against API-Sports fixture result players.");

        endpoints.MapPost("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link", async (
            TheOddsApiSportsAliasLinkRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var normalizedFixtureId = string.IsNullOrWhiteSpace(request.FixtureId) ? null : request.FixtureId.Trim();
            var normalizedProviderEventId = string.IsNullOrWhiteSpace(request.ProviderEventId) ? null : request.ProviderEventId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedFixtureId) || string.IsNullOrWhiteSpace(normalizedProviderEventId))
            {
                return Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    writesRows = false,
                    publicSoccerGradingEnabled = false,
                    error = "fixtureId and providerEventId are required."
                });
            }

            var dryRun = request.DryRun != false;
            var minConfidence = Math.Clamp(request.MinConfidence ?? 0.94m, 0m, 1m);
            var maxLinks = Math.Clamp(request.MaxLinks ?? 50, 1, 250);
            var includeExactNormalizedMatches = request.IncludeExactNormalizedMatches != false;
            var includeTokenSubsetMatches = request.IncludeTokenSubsetMatches != false;
            var resultPlayers = await LoadApiSportsAliasFixturePlayersAsync(dbContext, normalizedFixtureId, cancellationToken);
            var platformPlayers = await LoadTheOddsAliasPlatformPlayersAsync(dbContext, normalizedProviderEventId, cancellationToken);
            var candidateRows = platformPlayers
                .Select(platformPlayer => BuildTheOddsApiSportsAliasCandidateRow(platformPlayer, resultPlayers))
                .ToArray();
            var plan = await BuildTheOddsApiSportsAliasLinkPlanAsync(
                dbContext,
                candidateRows,
                normalizedFixtureId,
                normalizedProviderEventId,
                minConfidence,
                includeExactNormalizedMatches,
                includeTokenSubsetMatches,
                maxLinks,
                cancellationToken);
            var insertedLinks = Array.Empty<TheOddsApiSportsAliasLinkAuditRow>();

            if (!dryRun && plan.ProposedLinks.Count > 0)
            {
                insertedLinks = await WriteTheOddsApiSportsAliasLinksAsync(
                    dbContext,
                    normalizedFixtureId,
                    normalizedProviderEventId,
                    plan.ProposedLinks,
                    cancellationToken);
            }

            var linksInserted = insertedLinks.Length;
            var summary = new
            {
                candidatesEvaluated = candidateRows.Length,
                safeCandidates = candidateRows.Count(row => row.SuggestedAction == "SafeAliasCandidate"),
                linksProposed = plan.ProposedLinks.Count,
                linksInserted,
                linksAlreadyExisting = plan.LinksAlreadyExisting,
                linksSkipped = plan.Skipped.Count,
                skippedAmbiguous = plan.Skipped.Count(row => row.Reason == "Ambiguous"),
                skippedNeedsReview = plan.Skipped.Count(row => row.Reason == "NeedsReview"),
                skippedNoCandidate = plan.Skipped.Count(row => row.Reason == "NoCandidate"),
                skippedBelowConfidence = plan.Skipped.Count(row => row.Reason == "BelowMinimumConfidence"),
                skippedExistingConflict = plan.Skipped.Count(row => row.Reason == "ExistingLinkConflict"),
                skippedExcludedByRequest = plan.Skipped.Count(row => row.Reason == "ExactNormalizedMatchExcluded" || row.Reason == "TokenSubsetMatchExcluded"),
                dryRun
            };

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = dryRun,
                writesRows = !dryRun && linksInserted > 0,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    fixtureId = normalizedFixtureId,
                    providerEventId = normalizedProviderEventId,
                    minConfidence,
                    includeExactNormalizedMatches,
                    includeTokenSubsetMatches,
                    maxLinks
                },
                summary,
                proposedLinks = plan.ProposedLinks,
                writtenLinks = insertedLinks,
                skipped = plan.Skipped,
                recommendation = BuildTheOddsApiSportsAliasLinkRecommendation(dryRun, plan.ProposedLinks.Count, linksInserted, plan.Skipped)
            });
        })
        .WithName("TheOddsApiSportsSoccerAliasLinkDebug")
        .WithSummary("Debug-only dry-run-first persistence for approved The Odds Soccer player aliases to API-Sports/local player identities.");

        endpoints.MapPost("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve", async (
            TheOddsApiSportsAliasManualApprovalRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var normalizedFixtureId = string.IsNullOrWhiteSpace(request.FixtureId) ? null : request.FixtureId.Trim();
            var normalizedProviderEventId = string.IsNullOrWhiteSpace(request.ProviderEventId) ? null : request.ProviderEventId.Trim();
            var normalizedPlatformPlayerName = NormalizeResultPlayerName(request.PlatformPlayerName);
            var normalizedApiSportsPlayerId = string.IsNullOrWhiteSpace(request.ApiSportsPlayerId) ? null : request.ApiSportsPlayerId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedFixtureId)
                || string.IsNullOrWhiteSpace(normalizedProviderEventId)
                || string.IsNullOrWhiteSpace(normalizedPlatformPlayerName)
                || string.IsNullOrWhiteSpace(normalizedApiSportsPlayerId)
                || request.LocalPlayerId is null)
            {
                return Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    writesRows = false,
                    publicSoccerGradingEnabled = false,
                    error = "fixtureId, providerEventId, platformPlayerName, apiSportsPlayerId, and localPlayerId are required."
                });
            }

            var dryRun = request.DryRun != false;
            var resultPlayers = await LoadApiSportsAliasFixturePlayersAsync(dbContext, normalizedFixtureId, cancellationToken);
            var platformPlayers = await LoadTheOddsAliasPlatformPlayersAsync(dbContext, normalizedProviderEventId, cancellationToken);
            var platformPlayer = platformPlayers
                .SingleOrDefault(row => string.Equals(row.NormalizedPlayerName, normalizedPlatformPlayerName, StringComparison.OrdinalIgnoreCase));
            TheOddsApiSportsAliasCandidateRow? candidateRow = platformPlayer is null
                ? null
                : BuildTheOddsApiSportsAliasCandidateRow(platformPlayer, resultPlayers);
            var skipped = new List<TheOddsApiSportsAliasLinkSkippedRow>();
            TheOddsApiSportsAliasLinkAuditRow[] proposed = [];
            TheOddsApiSportsAliasLinkAuditRow[] written = [];

            if (candidateRow is null)
            {
                skipped.Add(new TheOddsApiSportsAliasLinkSkippedRow(
                    request.PlatformPlayerName ?? string.Empty,
                    normalizedPlatformPlayerName,
                    normalizedProviderEventId,
                    0,
                    "PlatformPlayerNotFound",
                    ["NoPlatformPlayerCandidate"],
                    null,
                    null,
                    null));
            }
            else if (candidateRow.SuggestedAction != "NeedsReview")
            {
                skipped.Add(BuildTheOddsApiSportsAliasSkippedRow(candidateRow, candidateRow.SuggestedAction));
            }
            else if (candidateRow.Candidates.Count != 1)
            {
                skipped.Add(BuildTheOddsApiSportsAliasSkippedRow(candidateRow, "Ambiguous"));
            }
            else
            {
                var candidate = candidateRow.Candidates[0];
                if (!string.Equals(candidate.ApiSportsPlayerId, normalizedApiSportsPlayerId, StringComparison.OrdinalIgnoreCase)
                    || candidate.LocalPlayerId != request.LocalPlayerId.Value)
                {
                    skipped.Add(new TheOddsApiSportsAliasLinkSkippedRow(
                        candidateRow.PlatformPlayerName,
                        candidateRow.NormalizedPlatformPlayerName,
                        candidateRow.ProviderEventId,
                        candidateRow.Confidence,
                        "CandidateIdMismatch",
                        candidateRow.Blockers,
                        candidate.ApiSportsPlayerName,
                        candidate.LocalPlayerId,
                        null));
                }
                else
                {
                    var auditRow = BuildTheOddsApiSportsAliasLinkAuditRow(candidateRow, candidate, normalizedProviderEventId);
                    var existingLink = await dbContext.ProviderEntityLinks
                        .AsNoTracking()
                        .Where(link => link.ProviderKey == TheOddsSoccerAliasProviderKey
                            && link.EntityType == "Player"
                            && link.ProviderEntityId == auditRow.ProviderEntityId)
                        .Select(link => new
                        {
                            link.CanonicalEntityId,
                            link.CanonicalEntityName,
                            link.LinkStatus
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (existingLink is not null
                        && string.Equals(existingLink.LinkStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                        && existingLink.CanonicalEntityId == auditRow.LocalPlayerId)
                    {
                        skipped.Add(new TheOddsApiSportsAliasLinkSkippedRow(
                            auditRow.PlatformPlayerName,
                            auditRow.NormalizedPlatformPlayerName,
                            auditRow.ProviderEventId,
                            auditRow.Confidence,
                            "AlreadyLinked",
                            auditRow.Blockers,
                            auditRow.CandidateApiSportsPlayerName,
                            auditRow.LocalPlayerId,
                            existingLink.CanonicalEntityName));
                    }
                    else if (existingLink is not null)
                    {
                        skipped.Add(new TheOddsApiSportsAliasLinkSkippedRow(
                            auditRow.PlatformPlayerName,
                            auditRow.NormalizedPlatformPlayerName,
                            auditRow.ProviderEventId,
                            auditRow.Confidence,
                            "ExistingLinkConflict",
                            auditRow.Blockers,
                            auditRow.CandidateApiSportsPlayerName,
                            auditRow.LocalPlayerId,
                            existingLink.CanonicalEntityName));
                    }
                    else
                    {
                        proposed = [auditRow];
                        if (!dryRun)
                        {
                            written = await WriteTheOddsApiSportsAliasLinksAsync(
                                dbContext,
                                normalizedFixtureId,
                                normalizedProviderEventId,
                                proposed,
                                cancellationToken,
                                approvalReason: FirstNonBlank(request.Reason),
                                linkSource: "DebugTheOddsApiSportsManualAliasApproval",
                                dryRunFirstEndpoint: "/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve",
                                approvedBy: "debug/manual-approve");
                        }
                    }
                }
            }

            var summary = new
            {
                candidateFound = candidateRow is not null,
                suggestedAction = candidateRow?.SuggestedAction,
                confidence = candidateRow?.Confidence,
                candidateCount = candidateRow?.Candidates.Count ?? 0,
                linksProposed = proposed.Length,
                linksInserted = written.Length,
                linksSkipped = skipped.Count,
                dryRun
            };

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = dryRun,
                writesRows = !dryRun && written.Length > 0,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    fixtureId = normalizedFixtureId,
                    providerEventId = normalizedProviderEventId,
                    platformPlayerName = request.PlatformPlayerName,
                    apiSportsPlayerId = normalizedApiSportsPlayerId,
                    localPlayerId = request.LocalPlayerId,
                    reason = FirstNonBlank(request.Reason)
                },
                candidate = candidateRow,
                summary,
                proposedLink = proposed.FirstOrDefault(),
                writtenLink = written.FirstOrDefault(),
                skipped = skipped.FirstOrDefault(),
                recommendation = BuildTheOddsApiSportsManualAliasApprovalRecommendation(dryRun, proposed.Length, written.Length, skipped)
            });
        })
        .WithName("TheOddsApiSportsSoccerAliasManualApprovalDebug")
        .WithSummary("Debug-only manual one-at-a-time approval for reviewed The Odds Soccer NeedsReview aliases.");

        endpoints.MapGet("/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeSamples,
            int? maxFixtures,
            int? maxSamples,
            decimal? minConfidence,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var fixtureLimit = Math.Clamp(maxFixtures ?? 50, 1, 250);
            var sampleLimit = Math.Clamp(maxSamples ?? 200, 1, 1000);
            var minimumConfidence = Math.Clamp(minConfidence ?? 0.94m, 0m, 1m);
            var batch = await BuildTheOddsApiSportsAliasBatchAsync(
                dbContext,
                range,
                fixtureLimit,
                sampleLimit,
                minimumConfidence,
                includeExactNormalizedMatches: true,
                includeTokenSubsetMatches: true,
                maxLinks: sampleLimit,
                includePlan: true,
                cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    maxFixtures = fixtureLimit,
                    maxSamples = sampleLimit,
                    minConfidence = minimumConfidence
                },
                summary = batch.CandidateSummary,
                estimatedCoverageImpact = batch.EstimatedCoverageImpact,
                candidateGroups = includeSamples != false ? batch.CandidateGroups.Take(sampleLimit).ToArray() : null,
                deduplicatedCandidates = includeSamples != false ? batch.DeduplicatedCandidates.Take(sampleLimit).ToArray() : null,
                blockers = batch.Blockers,
                recommendation = batch.CandidateRecommendation
            });
        })
        .WithName("TheOddsApiSportsSoccerAliasCandidatesBatchDebug")
        .WithSummary("Read-only batch Soccer alias candidate diagnostic across resolved The Odds/API-Sports World Cup fixture pairs.");

        endpoints.MapPost("/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch", async (
            TheOddsApiSportsAliasLinkBatchRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(request.FromDate, request.ToDate);
            var dryRun = request.DryRun != false;
            var minimumConfidence = Math.Clamp(request.MinConfidence ?? 0.94m, 0m, 1m);
            var maxLinks = Math.Clamp(request.MaxLinks ?? 200, 1, 1000);
            var includeExactNormalizedMatches = request.IncludeExactNormalizedMatches != false;
            var includeTokenSubsetMatches = request.IncludeTokenSubsetMatches != false;
            var batch = await BuildTheOddsApiSportsAliasBatchAsync(
                dbContext,
                range,
                maxFixtures: 250,
                sampleLimit: maxLinks,
                minimumConfidence,
                includeExactNormalizedMatches,
                includeTokenSubsetMatches,
                maxLinks,
                includePlan: true,
                cancellationToken);
            var writtenLinks = new List<TheOddsApiSportsAliasLinkAuditRow>();

            if (!dryRun)
            {
                foreach (var group in batch.LinkPlans)
                {
                    if (writtenLinks.Count >= maxLinks)
                    {
                        break;
                    }

                    var remaining = maxLinks - writtenLinks.Count;
                    var proposed = group.Plan.ProposedLinks.Take(remaining).ToArray();
                    if (proposed.Length == 0)
                    {
                        continue;
                    }

                    var inserted = await WriteTheOddsApiSportsAliasLinksAsync(
                        dbContext,
                        group.FixtureId,
                        group.ProviderEventId,
                        proposed,
                        cancellationToken);
                    writtenLinks.AddRange(inserted);
                }
            }

            var linksInserted = writtenLinks.Count;
            var proposedLinks = batch.LinkPlans
                .SelectMany(group => group.Plan.ProposedLinks)
                .Take(maxLinks)
                .ToArray();
            var skipped = batch.LinkPlans
                .SelectMany(group => group.Plan.Skipped)
                .ToArray();
            var summary = new
            {
                resolvedPairsEvaluated = batch.ResolvedPairsEvaluated,
                candidatesEvaluated = batch.CandidatesEvaluated,
                safeCandidates = batch.SafeCandidates,
                linksProposed = proposedLinks.Length,
                linksInserted,
                linksAlreadyExisting = batch.LinkPlans.Sum(group => group.Plan.LinksAlreadyExisting),
                skippedNoCandidate = skipped.Count(row => row.Reason == "NoCandidate"),
                skippedAmbiguous = skipped.Count(row => row.Reason == "Ambiguous"),
                skippedNeedsReview = skipped.Count(row => row.Reason == "NeedsReview"),
                skippedBelowConfidence = skipped.Count(row => row.Reason == "BelowMinimumConfidence"),
                skippedConflictingExisting = skipped.Count(row => row.Reason == "ExistingLinkConflict"),
                skippedExcludedByRequest = skipped.Count(row => row.Reason == "ExactNormalizedMatchExcluded" || row.Reason == "TokenSubsetMatchExcluded"),
                dryRun
            };

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = dryRun,
                writesRows = !dryRun && linksInserted > 0,
                publicSoccerGradingEnabled = false,
                scope = new
                {
                    range.FromDate,
                    range.ToDate,
                    minConfidence = minimumConfidence,
                    maxLinks,
                    includeExactNormalizedMatches,
                    includeTokenSubsetMatches
                },
                summary,
                proposedLinks,
                writtenLinks,
                skipped,
                recommendation = BuildTheOddsApiSportsAliasLinkRecommendation(dryRun, proposedLinks.Length, linksInserted, skipped)
            });
        })
        .WithName("TheOddsApiSportsSoccerAliasLinkBatchDebug")
        .WithSummary("Debug-only dry-run-first batch persistence for safe The Odds Soccer aliases across resolved API-Sports fixture pairs.");

        endpoints.MapGet("/api/debug/soccer/player-identity/api-sports/world-cup/coverage", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? fixtureId,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var normalizedFixtureId = string.IsNullOrWhiteSpace(fixtureId) ? null : fixtureId.Trim();
            var sampleLimit = Math.Clamp(maxSamples ?? 50, 1, 250);
            var identity = await BuildApiSportsSoccerIdentityDiagnosticsAsync(
                dbContext,
                range,
                normalizedFixtureId,
                cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                scope = BuildApiSportsSoccerIdentityScope(range, normalizedFixtureId),
                importedPlayers = new
                {
                    distinctApiSportsPlayers = identity.ImportedPlayers.Count,
                    resultRows = identity.ResultRowCount,
                    rowsByTeam = identity.ImportedPlayers
                        .GroupBy(player => string.IsNullOrWhiteSpace(player.ApiSportsTeamName) ? "Unknown" : player.ApiSportsTeamName, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(group => group.Key)
                        .Select(group => new { teamName = group.Key, playerCount = group.Count(), resultRows = group.Sum(player => player.ResultRowCount) })
                        .ToArray()
                },
                existingSoccerIdentity = new
                {
                    canonicalSoccerPlayers = identity.CanonicalSoccerPlayerCount,
                    soccerPlatformPlayers = identity.SoccerPlatformPlayerCount,
                    soccerPlayerExternalRefs = identity.SoccerPlayerExternalRefCount,
                    providerEntityLinks = identity.ProviderEntityLinkCount,
                    apiSportsProviderPlayerLinks = identity.ApiSportsProviderPlayerLinkCount,
                    approvedProviderPlayerLinks = identity.ApprovedProviderPlayerLinkCount,
                    candidateProviderPlayerLinks = identity.CandidateProviderPlayerLinkCount,
                    needsReviewProviderPlayerLinks = identity.NeedsReviewProviderPlayerLinkCount,
                    rejectedProviderPlayerLinks = identity.RejectedProviderPlayerLinkCount,
                    conflictProviderPlayerLinks = identity.ConflictProviderPlayerLinkCount,
                    providerLinksWithTeamContext = identity.ProviderLinksWithTeamContextCount,
                    soccerPlatformProps = identity.SoccerPlatformPropCount,
                    soccerSnapshotPlayers = identity.SoccerSnapshotPlayerCount,
                    identifiersAvailable = new[]
                    {
                        "API-Sports player ids are extracted from prop_results.external_source_identifier.",
                        "Platform prop candidates use local canonical player ids and team names when present.",
                        "Market snapshots do not carry canonical player ids, so they contribute name-only evidence."
                    }
                },
                matchSummary = new
                {
                    alreadyLinkedCount = identity.AlreadyLinked.Count,
                    exactNameMatchCount = identity.ExactNameCandidates.Count,
                    normalizedNameMatchCount = identity.NormalizedNameCandidates.Count,
                    teamAwareNameMatchCount = identity.TeamAwareCandidates.Count,
                    ambiguousMatchCount = identity.Ambiguous.Count,
                    unmatchedCount = identity.Unmatched.Count,
                    conflictCount = identity.Conflicts.Count
                },
                samples = includeSamples == true ? new
                {
                    alreadyLinked = identity.AlreadyLinked.Take(sampleLimit).ToArray(),
                    exactNameCandidates = identity.ExactNameCandidates.Take(sampleLimit).ToArray(),
                    normalizedNameCandidates = identity.NormalizedNameCandidates.Take(sampleLimit).ToArray(),
                    teamAwareCandidates = identity.TeamAwareCandidates.Take(sampleLimit).ToArray(),
                    ambiguous = identity.Ambiguous.Take(sampleLimit).ToArray(),
                    unmatched = identity.Unmatched.Take(sampleLimit).ToArray(),
                    conflicts = identity.Conflicts.Take(sampleLimit).ToArray()
                } : null,
                publicSoccerGradingEnabled = false,
                recommendation = "Use these diagnostics to review API-Sports-to-platform player identity candidates. This endpoint does not write aliases or enable Soccer grading."
            });
        })
        .WithName("ApiSportsSoccerWorldCupPlayerIdentityCoverageDebug")
        .WithSummary("Read-only API-Sports World Cup Soccer player identity reconciliation coverage.");

        endpoints.MapGet("/api/debug/soccer/player-identity/api-sports/world-cup/candidates", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? fixtureId,
            decimal? minConfidence,
            bool? includeAmbiguous,
            int? maxResults,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var normalizedFixtureId = string.IsNullOrWhiteSpace(fixtureId) ? null : fixtureId.Trim();
            var minimumConfidence = Math.Clamp(minConfidence ?? 0.80m, 0m, 1m);
            var take = Math.Clamp(maxResults ?? 100, 1, 500);
            var identity = await BuildApiSportsSoccerIdentityDiagnosticsAsync(
                dbContext,
                range,
                normalizedFixtureId,
                cancellationToken);
            var candidates = identity.Candidates
                .Where(candidate => candidate.Confidence >= minimumConfidence)
                .Where(candidate => includeAmbiguous == true || !candidate.Blockers.Contains("AmbiguousName", StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.ApiSportsPlayerName)
                .ThenBy(candidate => candidate.CandidateCanonicalPlayerName)
                .Take(take)
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                scope = BuildApiSportsSoccerIdentityScope(range, normalizedFixtureId),
                filters = new
                {
                    minConfidence = minimumConfidence,
                    includeAmbiguous = includeAmbiguous != false,
                    maxResults = take,
                    providerEntityLinks = identity.ProviderEntityLinkCount,
                    apiSportsProviderPlayerLinks = identity.ApiSportsProviderPlayerLinkCount,
                    approvedProviderPlayerLinks = identity.ApprovedProviderPlayerLinkCount,
                    candidateProviderPlayerLinks = identity.CandidateProviderPlayerLinkCount,
                    needsReviewProviderPlayerLinks = identity.NeedsReviewProviderPlayerLinkCount,
                    rejectedProviderPlayerLinks = identity.RejectedProviderPlayerLinkCount,
                    conflictProviderPlayerLinks = identity.ConflictProviderPlayerLinkCount
                },
                candidateCount = candidates.Length,
                candidates,
                publicSoccerGradingEnabled = false
            });
        })
        .WithName("ApiSportsSoccerWorldCupPlayerIdentityCandidatesDebug")
        .WithSummary("Read-only API-Sports World Cup Soccer player identity link candidates.");

        endpoints.MapGet("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            decimal? minConfidence,
            bool? includeAmbiguous,
            string? suggestedAction,
            bool? hasCandidate,
            bool? safeOnly,
            string? sort,
            bool? includeNoCandidate,
            string? linkStatus,
            int? maxResults,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var minimumConfidence = Math.Clamp(minConfidence ?? 0.80m, 0m, 1m);
            var includeAmbiguousRows = includeAmbiguous != false;
            var includeNoCandidateRows = includeNoCandidate == true || (!minConfidence.HasValue && hasCandidate != true);
            var normalizedSuggestedAction = string.IsNullOrWhiteSpace(suggestedAction) ? null : suggestedAction.Trim();
            var requireSafeOnly = safeOnly == true;
            var normalizedSort = string.IsNullOrWhiteSpace(sort) ? "confidenceDesc" : sort.Trim();
            var normalizedLinkStatus = string.IsNullOrWhiteSpace(linkStatus) ? "Candidate" : linkStatus.Trim();
            var take = Math.Clamp(maxResults ?? 100, 1, 500);
            var soccerSportId = await GetSportIdAsync(dbContext, "SOCCER", cancellationToken);
            var links = soccerSportId.HasValue
                ? await dbContext.ProviderEntityLinks
                    .AsNoTracking()
                    .Where(link => link.SportId == soccerSportId.Value
                        && link.EntityType == "Player"
                        && link.ProviderKey == ApiSportsFootballPlayerRefProvider
                        && link.LinkStatus == normalizedLinkStatus)
                    .OrderBy(link => link.ProviderEntityName)
                    .ToListAsync(cancellationToken)
                : [];
            var rows = await BuildApiSportsReviewQueueRowsAsync(
                dbContext,
                range,
                minimumConfidence,
                includeAmbiguousRows,
                includeNoCandidateRows,
                normalizedLinkStatus,
                cancellationToken);

            var filteredRows = rows
                .Where(row => !hasCandidate.HasValue || row.HasCandidate == hasCandidate.Value)
                .Where(row => !requireSafeOnly || row.HasCandidate && row.Blockers.Count == 0)
                .Where(row => normalizedSuggestedAction is null || row.SuggestedAction.Equals(normalizedSuggestedAction, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var sortedRows = SortApiSportsReviewQueueRows(filteredRows, normalizedSort)
                .Take(take)
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                scope = BuildApiSportsSoccerIdentityScope(range, fixtureId: null),
                filters = new
                {
                    minConfidence = minimumConfidence,
                    includeAmbiguous = includeAmbiguousRows,
                    includeNoCandidate = includeNoCandidateRows,
                    hasCandidate,
                    safeOnly = requireSafeOnly,
                    suggestedAction = normalizedSuggestedAction,
                    sort = normalizedSort,
                    linkStatus = normalizedLinkStatus,
                    maxResults = take
                },
                providerLinksInStatus = links.Count,
                reviewRowCount = filteredRows.Length,
                rows = sortedRows,
                publicSoccerGradingEnabled = false
            });
        })
        .WithName("ApiSportsSoccerWorldCupPlayerIdentityReviewQueueDebug")
        .WithSummary("Read-only manual review queue for API-Sports Soccer player identity links.");

        endpoints.MapPost("/api/debug/soccer/player-identity/api-sports/world-cup/approve-link", async (
            ApiSportsProviderLinkApprovalRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var result = await BuildApiSportsProviderLinkApprovalResultAsync(dbContext, request, cancellationToken);
            result = await AddApiSportsSingleApprovalReviewWarningsAsync(dbContext, result, request, cancellationToken);
            if (result.Blockers.Count > 0)
            {
                return Results.Conflict(result);
            }

            if (result.DryRun || !result.Changed)
            {
                return Results.Ok(result);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(result with { After = BuildProviderEntityLinkState(result.Link!) });
        })
        .WithName("ApiSportsSoccerWorldCupApproveProviderLinkDebug")
        .WithSummary("Debug-only dry-run-default manual approval for one API-Sports Soccer provider player link.");

        endpoints.MapPost("/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links", async (
            ApiSportsBulkApproveLinksRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var result = await BuildApiSportsBulkApproveLinksResultAsync(dbContext, request, cancellationToken);
            if (!result.DryRun && !result.AllowPartial && result.BlockedCount > 0)
            {
                return Results.Conflict(result);
            }

            return Results.Ok(result);
        })
        .WithName("ApiSportsSoccerWorldCupBulkApproveProviderLinksDebug")
        .WithSummary("Debug-only dry-run-default bulk approval for explicit API-Sports Soccer provider player link pairs.");

        endpoints.MapPost("/api/debug/soccer/player-identity/api-sports/world-cup/reject-link", async (
            ApiSportsProviderLinkRejectionRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var result = await BuildApiSportsProviderLinkRejectionResultAsync(dbContext, request, cancellationToken);
            if (result.Blockers.Count > 0)
            {
                return Results.Conflict(result);
            }

            if (result.DryRun || !result.Changed)
            {
                return Results.Ok(result);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(result with { After = BuildProviderEntityLinkState(result.Link!) });
        })
        .WithName("ApiSportsSoccerWorldCupRejectProviderLinkDebug")
        .WithSummary("Debug-only dry-run-default manual rejection for one API-Sports Soccer provider player link.");

        endpoints.MapGet("/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            decimal? minConfidence,
            string? suggestedAction,
            bool? hasCandidate,
            bool? safeOnly,
            int? maxResults,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var minimumConfidence = Math.Clamp(minConfidence ?? 0.80m, 0m, 1m);
            var normalizedSuggestedAction = string.IsNullOrWhiteSpace(suggestedAction) ? null : suggestedAction.Trim();
            var filterHasCandidate = hasCandidate;
            var requireSafeOnly = safeOnly == true;
            var take = Math.Clamp(maxResults ?? 100, 1, 500);
            var rows = await BuildApiSportsReviewQueueRowsAsync(
                dbContext,
                range,
                minimumConfidence,
                includeAmbiguousRows: true,
                includeNoCandidateRows: false,
                normalizedLinkStatus: "Candidate",
                cancellationToken);
            var filteredRows = rows
                .Where(row => !filterHasCandidate.HasValue || row.HasCandidate == filterHasCandidate.Value)
                .Where(row => !requireSafeOnly || row.HasCandidate && row.Blockers.Count == 0)
                .Where(row => normalizedSuggestedAction is null || row.SuggestedAction.Equals(normalizedSuggestedAction, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var safeCandidates = ToApiSportsReviewQueueExportRows(filteredRows
                    .Where(row => row.HasCandidate && row.Blockers.Count == 0)
                    .ToArray(), take);
            var needsReviewCandidates = ToApiSportsReviewQueueExportRows(filteredRows
                    .Where(row => row.HasCandidate
                        && row.Blockers.Count > 0
                        && !row.Blockers.Contains("AmbiguousName", StringComparer.OrdinalIgnoreCase)
                        && !row.SuggestedAction.Equals("Conflict", StringComparison.OrdinalIgnoreCase))
                    .ToArray(), take);
            var noCandidateRows = ToApiSportsReviewQueueExportRows(filteredRows
                    .Where(row => !row.HasCandidate || row.SuggestedAction.Equals("NoCandidate", StringComparison.OrdinalIgnoreCase))
                    .ToArray(), take);
            var ambiguousOrConflictRows = ToApiSportsReviewQueueExportRows(filteredRows
                    .Where(row => row.Blockers.Contains("AmbiguousName", StringComparer.OrdinalIgnoreCase)
                        || row.SuggestedAction.Equals("Conflict", StringComparison.OrdinalIgnoreCase))
                    .ToArray(), take);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                scope = BuildApiSportsSoccerIdentityScope(range, fixtureId: null),
                filters = new
                {
                    minConfidence = minimumConfidence,
                    suggestedAction = normalizedSuggestedAction,
                    hasCandidate = filterHasCandidate,
                    safeOnly = requireSafeOnly,
                    maxResults = take
                },
                rowCount = filteredRows.Length,
                safeCandidates,
                needsReviewCandidates,
                noCandidate = noCandidateRows,
                ambiguousConflict = ambiguousOrConflictRows,
                publicSoccerGradingEnabled = false
            });
        })
        .WithName("ApiSportsSoccerWorldCupPlayerIdentityReviewQueueExportDebug")
        .WithSummary("Read-only compact export of API-Sports Soccer player identity review candidates.");

        endpoints.MapGet("/api/debug/soccer/player-identity/api-sports/world-cup/link-preview", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? fixtureId,
            decimal? minConfidence,
            int? maxResults,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var normalizedFixtureId = string.IsNullOrWhiteSpace(fixtureId) ? null : fixtureId.Trim();
            var minimumConfidence = Math.Clamp(minConfidence ?? 0.95m, 0m, 1m);
            var take = Math.Clamp(maxResults ?? 100, 1, 500);
            var identity = await BuildApiSportsSoccerIdentityDiagnosticsAsync(
                dbContext,
                range,
                normalizedFixtureId,
                cancellationToken);
            var candidates = identity.Candidates
                .Where(candidate => candidate.Confidence >= minimumConfidence)
                .Where(candidate => string.Equals(candidate.SuggestedAction, "SafeAliasLink", StringComparison.OrdinalIgnoreCase))
                .Where(candidate => candidate.Blockers.Count == 0)
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.ApiSportsPlayerName)
                .Take(take)
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                writesAliases = false,
                scope = BuildApiSportsSoccerIdentityScope(range, normalizedFixtureId),
                filters = new
                {
                    minConfidence = minimumConfidence,
                    maxResults = take,
                    apiSportsProviderPlayerLinks = identity.ApiSportsProviderPlayerLinkCount,
                    approvedProviderPlayerLinks = identity.ApprovedProviderPlayerLinkCount
                },
                candidateCount = candidates.Length,
                candidates,
                warning = "Preview only. No player_external_refs are created by this endpoint.",
                publicSoccerGradingEnabled = false
            });
        })
        .WithName("ApiSportsSoccerWorldCupPlayerIdentityLinkPreviewDebug")
        .WithSummary("Read-only preview of high-confidence API-Sports Soccer player links.");

        endpoints.MapGet("/api/debug/soccer/player-identity/api-sports/world-cup/platform-context", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? includeSamples,
            int? maxSamples,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(fromDate, toDate);
            var sampleLimit = Math.Clamp(maxSamples ?? 25, 1, 100);
            var contextRows = await LoadApiSportsSoccerPlatformContextRowsAsync(dbContext, range, cancellationToken);
            var platformRows = contextRows.Where(row => row.SourceName == "platform_props").ToArray();
            var snapshotRows = contextRows.Where(row => row.SourceName != "platform_props").ToArray();
            var fieldAvailability = BuildApiSportsSoccerPlatformContextAvailability(contextRows);
            var contextEnrichedRows = contextRows.Count(row =>
                !string.IsNullOrWhiteSpace(row.ProviderEventId)
                && !string.IsNullOrWhiteSpace(row.EventName)
                && !string.IsNullOrWhiteSpace(row.HomeTeamName)
                && !string.IsNullOrWhiteSpace(row.AwayTeamName)
                && row.StartTime.HasValue);
            var contextBySource = contextRows
                .GroupBy(row => new { row.SourceName, row.TableName })
                .OrderBy(group => group.Key.SourceName)
                .Select(group =>
                {
                    var availability = BuildApiSportsSoccerPlatformContextAvailability(group);
                    return new
                    {
                        group.Key.SourceName,
                        group.Key.TableName,
                        rowCount = group.Count(),
                        availableFields = BuildAvailableContextFields(availability),
                        missingFields = BuildMissingContextFields(availability)
                    };
                })
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                scope = BuildApiSportsSoccerIdentityScope(range, fixtureId: null),
                soccerPlatformProps = platformRows.Length,
                soccerPlatformPlayers = platformRows.Select(row => row.CanonicalPlayerId).Where(id => id.HasValue).Distinct().Count(),
                soccerSnapshots = snapshotRows.Length,
                fieldAvailability,
                contextEnrichment = new
                {
                    enrichedRows = contextEnrichedRows,
                    preContextOrPartialRows = Math.Max(0, contextRows.Count - contextEnrichedRows),
                    status = contextRows.Count == 0
                        ? "NoRows"
                        : contextEnrichedRows == contextRows.Count
                            ? "ContextEnriched"
                            : contextEnrichedRows == 0
                                ? "PreContextOrMissingContext"
                                : "Mixed"
                },
                contextBySource,
                samples = includeSamples == true
                    ? contextRows
                        .Where(row => row.HasAnyContext)
                        .Take(sampleLimit)
                        .Select(row => new
                        {
                            row.SourceName,
                            row.TableName,
                            row.PlayerName,
                            row.PropType,
                            teamName = row.TeamName,
                            opponentName = row.OpponentName,
                            eventName = row.EventName,
                            startTime = row.StartTime,
                            providerEventId = row.ProviderEventId,
                            providerPlayerId = row.ProviderPlayerId,
                            provider = row.Provider,
                            bookmaker = row.Bookmaker
                        })
                        .ToArray()
                    : null,
                conclusion = BuildApiSportsSoccerPlatformContextConclusion(fieldAvailability)
            });
        })
        .WithName("ApiSportsSoccerWorldCupPlatformContextDebug")
        .WithSummary("Read-only Soccer platform/snapshot context availability for API-Sports identity matching.");

        endpoints.MapPost("/api/debug/soccer/results/api-sports/world-cup/backfill-completed", async (
            ApiSportsWorldCupBackfillCompletedRequest request,
            IOptions<ApiSportsOptions> options,
            IApiSportsFootballClient client,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var range = NormalizeApiSportsWorldCupDateRange(request.FromDate, request.ToDate);
            var dryRun = request.DryRun != false;
            var requestDiagnostics = new
            {
                range.FromDate,
                range.ToDate,
                dryRun,
                forceLive = request.ForceLive == true,
                leagueId = ApiSportsWorldCupLeagueId,
                season = ApiSportsWorldCupSeason
            };
            var block = BuildApiSportsBlockedResponse(
                "soccer-world-cup-backfill-completed",
                request.ForceLive,
                options.Value,
                requestDiagnostics,
                requireConfiguredLeague: false,
                requireConfiguredSeason: false);
            if (block is not null)
            {
                return block;
            }

            var discovered = await DiscoverApiSportsWorldCupFixturesAsync(client, range.FromDate, range.ToDate, cancellationToken);
            var completed = discovered.Where(row => IsApiSportsFixtureCompleted(row.StatusShort, row.StatusLong)).ToArray();
            var skipped = discovered
                .Where(row => !IsApiSportsFixtureCompleted(row.StatusShort, row.StatusLong))
                .Select(row => new ApiSportsWorldCupSkippedFixture(row.FixtureId, row.EventDate, row.StatusShort, row.StatusLong, GetApiSportsFixtureSkipReason(row)))
                .ToList();
            var results = new List<ApiSportsWorldCupFixtureImportOutcome>();
            var failures = new List<ApiSportsWorldCupFixtureImportFailure>();
            var totalPreviewed = 0;
            var totalWritten = 0;
            var totalInserted = 0;
            var totalUpdated = 0;

            foreach (var fixture in completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var import = await ImportApiSportsSoccerFixtureAsync(
                        client,
                        dbContext,
                        fixture.FixtureId,
                        dryRun,
                        cancellationToken);
                    totalPreviewed += import.ResultRowsPreviewed;
                    totalWritten += import.ResultRowsWritten;
                    totalInserted += import.ResultRowsInserted;
                    totalUpdated += import.ResultRowsUpdated;
                    results.Add(new ApiSportsWorldCupFixtureImportOutcome(
                        fixture.FixtureId,
                        fixture.EventDate,
                        fixture.KickoffUtc,
                        fixture.HomeTeamName,
                        fixture.AwayTeamName,
                        fixture.StatusShort,
                        fixture.StatusLong,
                        import.ResultRowsPreviewed,
                        import.ResultRowsWritten,
                        import.ResultRowsInserted,
                        import.ResultRowsUpdated,
                        import.Preview.UnsupportedSkipped,
                        import.MappingDiagnostics,
                        import.RuleConfidenceCounts));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(new ApiSportsWorldCupFixtureImportFailure(
                        fixture.FixtureId,
                        fixture.EventDate,
                        fixture.StatusShort,
                        fixture.StatusLong,
                        exception.GetType().Name,
                        exception.Message));
                }
            }
            var coverageByDate = BuildApiSportsWorldCupBackfillCoverageByDate(
                range.FromDate,
                range.ToDate,
                discovered,
                skipped,
                results,
                failures);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = true,
                externalApiCalled = true,
                readOnly = dryRun,
                provider = "API-Sports",
                product = "API-Sports Football",
                scope = new
                {
                    sport = "SOCCER",
                    competition = "World Cup",
                    leagueId = ApiSportsWorldCupLeagueId,
                    season = ApiSportsWorldCupSeason,
                    range.FromDate,
                    range.ToDate
                },
                request = requestDiagnostics,
                discoveredFixtureCount = discovered.Count,
                completedFixtureCount = completed.Length,
                skippedFixtureCount = skipped.Count,
                importedFixtureCount = results.Count,
                failedFixtureCount = failures.Count,
                resultRowsPreviewed = totalPreviewed,
                resultRowsWritten = totalWritten,
                resultRowsInserted = totalInserted,
                resultRowsUpdated = totalUpdated,
                apiCallsUsed = CountDateRangeDays(range.FromDate, range.ToDate) + completed.Length,
                coverageByDate,
                completedFixtures = completed,
                skippedFixtures = skipped,
                imports = results,
                failures
            });
        })
        .WithName("ApiSportsSoccerWorldCupBackfillCompletedDebug")
        .WithSummary("Force-live gated debug backfill of completed World Cup 2026 API-Sports fixtures.");

        endpoints.MapGet("/api/debug/mlb/pitcher-strikeouts/import-coverage", async (
            string? playerName,
            string? mlbStatsId,
            DateOnly? startDate,
            DateOnly? endDate,
            ApplicationDbContext dbContext,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            var dateRange = NormalizePitcherStrikeoutDateRange(startDate, endDate);
            var affectedProps = await LoadTheOddsPitcherStrikeoutPropsAsync(
                dbContext,
                playerName,
                mlbStatsId,
                dateRange.StartDate,
                dateRange.EndDate,
                cancellationToken);
            var playerIds = affectedProps.Select(prop => prop.PlayerId).Distinct().ToArray();
            var playerRefs = await LoadMlbRefsByPlayerIdAsync(dbContext, playerIds, cancellationToken);
            var propResults = await LoadMlbPitchingResultRowsAsync(dbContext, playerIds, dateRange.StartDate, dateRange.EndDate, cancellationToken);
            var importDateStates = await dbContext.MlbResultImportDateStates
                .AsNoTracking()
                .Where(state => state.EventDate >= dateRange.StartDate
                    && state.EventDate <= dateRange.EndDate)
                .Select(state => new { state.EventDate, state.Status })
                .ToListAsync(cancellationToken);
            var completedImportDates = importDateStates
                .Where(state => state.Status == "succeeded")
                .Select(state => state.EventDate)
                .ToHashSet();
            var pitcherRows = new List<MlbPitcherStrikeoutCoverageRow>();

            foreach (var group in affectedProps.GroupBy(prop => prop.PlayerId))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var props = group.ToArray();
                var first = props.OrderBy(prop => prop.EventDate).First();
                var latestEventDate = props.Max(prop => prop.EventDate);
                var playerResultRows = propResults.Where(result => result.PlayerId == group.Key).ToArray();
                var pitcherStrikeoutRows = playerResultRows
                    .Where(result => NormalizeResultPropTypeName(result.PropTypeName) == "PITCHERSTRIKEOUTS")
                    .ToArray();
                var genericStrikeoutRows = playerResultRows
                    .Where(result => NormalizeResultPropTypeName(result.PropTypeName) is "STRIKEOUTS" or "KS")
                    .ToArray();
                var pitchingStatRows = playerResultRows
                    .Where(result => IsPitchingResultPropType(result.PropTypeName))
                    .ToArray();
                var missingResultDates = pitchingStatRows
                    .Select(result => result.EventDate)
                    .Distinct()
                    .Where(date => !pitcherStrikeoutRows.Any(result => result.EventDate == date))
                    .OrderBy(date => date)
                    .Take(100)
                    .ToArray();
                var lookup = await featureService.AnalyzeHistoricalLookupAsync(
                    group.Key,
                    first.PropTypeId,
                    latestEventDate,
                    cancellationToken);

                pitcherRows.Add(new MlbPitcherStrikeoutCoverageRow(
                    first.PlayerName,
                    group.Key,
                    playerRefs.GetValueOrDefault(group.Key)?.FirstOrDefault(),
                    props.Min(prop => prop.EventDate),
                    latestEventDate,
                    completedImportDates.Count(date => date < latestEventDate),
                    pitchingStatRows.Select(result => result.EventDate).Distinct().Count(),
                    pitcherStrikeoutRows.Length,
                    genericStrikeoutRows.Length,
                    pitcherStrikeoutRows.Length + genericStrikeoutRows.Length,
                    pitcherStrikeoutRows
                        .OrderByDescending(result => result.EventDate)
                        .Take(10)
                        .Select(result => new MlbPitcherStrikeoutResultSample(
                            result.EventDate,
                            result.PropTypeName,
                            result.ActualResultValue))
                        .ToArray(),
                    missingResultDates,
                    ClassifyPitcherStrikeoutCoverageRootCause(
                        lookup,
                        pitcherStrikeoutRows.Length,
                        genericStrikeoutRows.Length,
                        pitchingStatRows.Length,
                        missingResultDates.Length,
                        completedImportDates.Count(date => date < latestEventDate))));
            }

            var pitcherStrikeoutPropTypeIds = await LoadPropTypeIdsByNameAsync(dbContext, "Pitcher Strikeouts", cancellationToken);
            var genericStrikeoutPropTypeIds = await LoadPropTypeIdsByNamesAsync(dbContext, ["Strikeouts", "Ks"], cancellationToken);
            var pitcherStrikeoutDates = await LoadResultDatesForPropTypesAsync(dbContext, pitcherStrikeoutPropTypeIds, dateRange.StartDate, dateRange.EndDate, cancellationToken);
            var genericStrikeoutDates = await LoadResultDatesForPropTypesAsync(dbContext, genericStrikeoutPropTypeIds, dateRange.StartDate, dateRange.EndDate, cancellationToken);
            var datesWithCompleteImportButNoPitcherStrikeouts = completedImportDates
                .Where(date => !pitcherStrikeoutDates.Contains(date))
                .OrderBy(date => date)
                .Take(100)
                .ToArray();

            return Results.Ok(new
            {
                filters = new
                {
                    playerName,
                    mlbStatsId,
                    dateRange.StartDate,
                    dateRange.EndDate
                },
                summary = new
                {
                    affectedPitchers = pitcherRows.Count,
                    totalPitcherStrikeoutResultsInDb = await CountResultsForPropTypesAsync(dbContext, pitcherStrikeoutPropTypeIds, dateRange.StartDate, dateRange.EndDate, cancellationToken),
                    totalGenericStrikeoutResultsInDb = await CountResultsForPropTypesAsync(dbContext, genericStrikeoutPropTypeIds, dateRange.StartDate, dateRange.EndDate, cancellationToken),
                    datesWithPitcherStrikeoutResults = pitcherStrikeoutDates.Count,
                    datesWithGenericStrikeoutResults = genericStrikeoutDates.Count,
                    datesWithMlbImportCompleteButNoPitcherStrikeouts = datesWithCompleteImportButNoPitcherStrikeouts.Length,
                    likelyBackfillNeeded = pitcherRows.Any(row => row.LikelyRootCause is "PitcherStrikeoutsImportGap" or "ImportedBeforePitcherStrikeoutsSupport"),
                    recommendedBackfillRange = pitcherRows.Count == 0
                        ? null
                        : new
                        {
                            startDate = pitcherRows.Min(row => row.FirstRelevantDate).AddYears(-1),
                            endDate = pitcherRows.Max(row => row.LatestRelevantDate).AddDays(-1)
                        }
                },
                datesWithCompleteImportButNoPitcherStrikeouts,
                pitchers = pitcherRows
            });
        })
        .WithName("MlbPitcherStrikeoutsImportCoverageDebug")
        .WithSummary("Audits MLB Pitcher Strikeouts result import coverage for The Odds API gaps.");

        endpoints.MapPost("/api/debug/mlb/pitcher-strikeouts/backfill", async (
            MlbPitcherStrikeoutBackfillRequest request,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            CancellationToken cancellationToken) =>
        {
            var dryRun = request.DryRun ?? true;
            if (request.EndDate < request.StartDate)
            {
                return Results.BadRequest(new { error = "endDate must be greater than or equal to startDate." });
            }

            var playerNameFilter = (request.PlayerNames ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeResultPlayerName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var datesScanned = 0;
            var gamesScanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pitcherResultsDetected = 0;
            var existingPitcherStrikeoutResults = 0;
            var missingPitcherStrikeoutResults = 0;
            var createdPitcherStrikeoutResults = 0;
            var affectedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<object>();
            var currentDate = request.StartDate;

            while (currentDate <= request.EndDate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                datesScanned++;

                try
                {
                    var records = await mlbResultsProvider.GetMlbResultsAsync(currentDate, cancellationToken);
                    var pitcherStrikeoutRecords = records
                        .Where(record => record.IsMapped
                            && string.Equals(record.StatName, "Pitcher Strikeouts", StringComparison.OrdinalIgnoreCase))
                        .Where(record => playerNameFilter.Count == 0 || playerNameFilter.Contains(NormalizeResultPlayerName(record.PlayerName)))
                        .ToArray();
                    pitcherResultsDetected += pitcherStrikeoutRecords.Length;
                    foreach (var gameId in pitcherStrikeoutRecords.Select(record => record.GameId))
                    {
                        gamesScanned.Add(gameId);
                    }

                    var collapsedRecords = pitcherStrikeoutRecords
                        .GroupBy(record => new MlbPitcherStrikeoutResultKey(
                            record.ResultRecord.PlayerId,
                            record.ResultRecord.LeagueId,
                            record.ResultRecord.PropTypeId,
                            record.ResultRecord.EventDate))
                        .Select(group => new
                        {
                            group.Key,
                            Value = group.Sum(record => record.StatValue),
                            SourceIdentifier = $"mlb:{group.Key.EventDate:yyyyMMdd}:{group.Key.PlayerId:N}:{group.Key.PropTypeId:N}",
                            PlayerName = group.Select(record => record.PlayerName).FirstOrDefault() ?? "Unknown"
                        })
                        .ToArray();
                    var playerIds = collapsedRecords.Select(record => record.Key.PlayerId).Distinct().ToArray();
                    var propTypeIds = collapsedRecords.Select(record => record.Key.PropTypeId).Distinct().ToArray();
                    var existing = await dbContext.PropResults
                        .Where(result => result.EventDate == currentDate
                            && playerIds.Contains(result.PlayerId)
                            && propTypeIds.Contains(result.PropTypeId))
                        .ToListAsync(cancellationToken);
                    var existingByKey = existing
                        .GroupBy(result => new MlbPitcherStrikeoutResultKey(result.PlayerId, result.LeagueId, result.PropTypeId, result.EventDate))
                        .ToDictionary(group => group.Key, group => group.First());
                    var observedAt = DateTimeOffset.UtcNow;

                    foreach (var record in collapsedRecords)
                    {
                        if (existingByKey.ContainsKey(record.Key))
                        {
                            existingPitcherStrikeoutResults++;
                            continue;
                        }

                        missingPitcherStrikeoutResults++;
                        affectedPlayers.Add(record.PlayerName);

                        if (dryRun)
                        {
                            continue;
                        }

                        dbContext.PropResults.Add(new PropResult
                        {
                            Id = Guid.NewGuid(),
                            PlayerId = record.Key.PlayerId,
                            LeagueId = record.Key.LeagueId,
                            PropTypeId = record.Key.PropTypeId,
                            EventDate = record.Key.EventDate,
                            ActualResultValue = record.Value,
                            ExternalSource = "mlb",
                            ExternalSourceIdentifier = record.SourceIdentifier,
                            CreatedAt = observedAt,
                            UpdatedAt = observedAt
                        });
                        createdPitcherStrikeoutResults++;
                    }

                    if (!dryRun)
                    {
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errors.Add(new
                    {
                        date = currentDate,
                        exceptionType = exception.GetType().Name,
                        exception.Message
                    });
                }

                currentDate = currentDate.AddDays(1);
            }

            return Results.Ok(new
            {
                request.StartDate,
                request.EndDate,
                dryRun,
                datesScanned,
                gamesScanned = gamesScanned.Count,
                pitcherResultsDetected,
                existingPitcherStrikeoutResults,
                missingPitcherStrikeoutResults,
                createdPitcherStrikeoutResults,
                affectedPlayers = affectedPlayers.Order().ToArray(),
                errors
            });
        })
        .WithName("MlbPitcherStrikeoutsBackfillDebug")
        .WithSummary("Backfills missing MLB Pitcher Strikeouts prop_results using MLB Stats boxscore provider records.");

        return endpoints;
    }

    private static ApiSportsConfigDiagnostics BuildApiSportsConfigDiagnostics(ApiSportsOptions options)
    {
        var missingConfig = new List<string>();
        if (!options.Enabled)
        {
            missingConfig.Add("ApiSports:Enabled");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey)
            || string.Equals(options.ApiKey.Trim(), "API_SPORTS_KEY", StringComparison.OrdinalIgnoreCase))
        {
            missingConfig.Add("ApiSports:ApiKey");
        }

        if (string.IsNullOrWhiteSpace(options.Football.BaseUrl))
        {
            missingConfig.Add("ApiSports:Football:BaseUrl");
        }

        if (!options.Football.WorldCupLeagueId.HasValue)
        {
            missingConfig.Add("ApiSports:Football:WorldCupLeagueId");
        }

        if (!options.Football.Season.HasValue)
        {
            missingConfig.Add("ApiSports:Football:Season");
        }

        return new ApiSportsConfigDiagnostics(
            missingConfig.Count == 0,
            missingConfig.ToArray(),
            new
            {
                options.Enabled,
                hasApiKey = !string.IsNullOrWhiteSpace(options.ApiKey)
                    && !string.Equals(options.ApiKey.Trim(), "API_SPORTS_KEY", StringComparison.OrdinalIgnoreCase),
                timeoutSeconds = options.TimeoutSeconds,
                maxRetries = options.MaxRetries,
                football = new
                {
                    options.Football.BaseUrl,
                    options.Football.WorldCupLeagueId,
                    options.Football.Season
                }
            });
    }

    private static IResult? BuildApiSportsBlockedResponse(
        string operation,
        bool? forceLive,
        ApiSportsOptions options,
        object request,
        bool requireConfiguredLeague = true,
        bool requireConfiguredSeason = true)
    {
        var config = BuildApiSportsConfigDiagnostics(options);
        var missingConfig = config.missingConfig
            .Where(key => key switch
            {
                "ApiSports:Football:WorldCupLeagueId" => requireConfiguredLeague,
                "ApiSports:Football:Season" => requireConfiguredSeason,
                _ => true
            })
            .ToArray();
        var configured = missingConfig.Length == 0;
        if (forceLive != true)
        {
            return Results.Conflict(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                provider = "API-Sports",
                product = "API-Sports Football",
                operation,
                request,
                blocked = true,
                requiresForceLive = true,
                forceLive = forceLive == true,
                missingConfig,
                config = config.safeConfig,
                message = "API-Sports Football diagnostics are blocked unless forceLive=true because they call an external provider."
            });
        }

        if (!configured)
        {
            return Results.BadRequest(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                provider = "API-Sports",
                product = "API-Sports Football",
                operation,
                request,
                blocked = true,
                blockReason = "MissingApiSportsConfig",
                missingConfig,
                config = config.safeConfig,
                message = "API-Sports config is incomplete. Configure the missing fields before force-live diagnostics."
            });
        }

        return null;
    }

    private static ApiSportsFootballFixturesRequest BuildApiSportsFixtureRequest(
        ApiSportsOptions options,
        DateOnly? date,
        string? fixtureId,
        int? league,
        int? season,
        bool? useConfiguredLeague,
        bool? useConfiguredSeason)
    {
        if (!string.IsNullOrWhiteSpace(fixtureId))
        {
            return new ApiSportsFootballFixturesRequest(null, fixtureId.Trim(), null, null);
        }

        var effectiveLeague = league;
        if (!effectiveLeague.HasValue && useConfiguredLeague != false)
        {
            effectiveLeague = options.Football.WorldCupLeagueId;
        }

        var effectiveSeason = season;
        if (!effectiveSeason.HasValue && useConfiguredSeason != false)
        {
            effectiveSeason = options.Football.Season;
        }

        return new ApiSportsFootballFixturesRequest(date, null, effectiveLeague, effectiveSeason);
    }

    private static object ExtractApiSportsErrors(JsonElement root)
    {
        return root.TryGetProperty("errors", out var errors)
            ? errors.Clone()
            : Array.Empty<string>();
    }

    private static object[] MapApiSportsFixtures(JsonElement root)
    {
        return EnumerateResponseItems(root)
            .Select(item =>
            {
                var fixture = GetPropertyOrDefault(item, "fixture");
                var teams = GetPropertyOrDefault(item, "teams");
                var league = GetPropertyOrDefault(item, "league");
                return new
                {
                    fixtureId = GetNestedInt(fixture, "id"),
                    kickoffUtc = GetNestedString(fixture, "date"),
                    status = new
                    {
                        shortName = GetNestedString(GetPropertyOrDefault(fixture, "status"), "short"),
                        longName = GetNestedString(GetPropertyOrDefault(fixture, "status"), "long")
                    },
                    homeTeam = GetNestedString(GetPropertyOrDefault(teams, "home"), "name"),
                    awayTeam = GetNestedString(GetPropertyOrDefault(teams, "away"), "name"),
                    league = new
                    {
                        id = GetNestedInt(league, "id"),
                        name = GetNestedString(league, "name"),
                        season = GetNestedInt(league, "season")
                    }
                };
            })
            .ToArray();
    }

    private static List<ApiSportsPlayerStatsRow> ExtractApiSportsPlayerStats(JsonElement root)
    {
        var rows = new List<ApiSportsPlayerStatsRow>();
        foreach (var teamNode in EnumerateResponseItems(root))
        {
            var team = GetPropertyOrDefault(teamNode, "team");
            var teamId = GetNestedInt(team, "id");
            var teamName = GetNestedString(team, "name");
            if (!teamNode.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var playerNode in players.EnumerateArray())
            {
                var player = GetPropertyOrDefault(playerNode, "player");
                var statsArray = playerNode.TryGetProperty("statistics", out var statistics) && statistics.ValueKind == JsonValueKind.Array
                    ? statistics.EnumerateArray().ToArray()
                    : [];
                var flattened = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var statNode in statsArray)
                {
                    FlattenStats(statNode, null, flattened);
                }

                var rawStats = flattened.ToDictionary(pair => pair.Key, pair => ToScalarValue(pair.Value), StringComparer.OrdinalIgnoreCase);
                rows.Add(new ApiSportsPlayerStatsRow(
                    teamId,
                    teamName,
                    GetNestedInt(player, "id"),
                    GetNestedString(player, "name"),
                    flattened.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                    rawStats,
                    GetString(rawStats, "games.position"),
                    GetDecimal(rawStats, "games.minutes")));
            }
        }

        return rows;
    }

    private static object[] BuildApiSportsPropCoverage(IReadOnlyList<ApiSportsPlayerStatsRow> playerStats)
    {
        var fields = playerStats
            .SelectMany(row => row.StatKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RequiredSoccerPropMappings
            .Select(mapping =>
            {
                var availableFields = mapping.CandidateFields.Where(fields.Contains).ToArray();
                return new
                {
                    prizePicksProp = mapping.Prop,
                    apiSportsSourceField = availableFields.FirstOrDefault(),
                    candidateFields = mapping.CandidateFields,
                    playerLevelAvailable = availableFields.Length > 0,
                    teamOrGoalieLevelAvailable = mapping.TeamOrGoalieLevel && availableFields.Length > 0,
                    gradeableNow = mapping.StandardStat && availableFields.Length > 0,
                    notes = availableFields.Length > 0
                        ? mapping.Notes
                        : $"No API-Sports Football player stat field was discovered for {mapping.Prop} in this fixture payload."
                };
            })
            .ToArray();
    }

    private static ApiSportsFixtureSummary ExtractApiSportsFixtureSummary(JsonElement root, string fallbackFixtureId)
    {
        var item = EnumerateResponseItems(root).FirstOrDefault();
        if (item.ValueKind == JsonValueKind.Undefined)
        {
            return new ApiSportsFixtureSummary(fallbackFixtureId, null, null, null, null, null, null, null, null, null);
        }

        var fixture = GetPropertyOrDefault(item, "fixture");
        var teams = GetPropertyOrDefault(item, "teams");
        var league = GetPropertyOrDefault(item, "league");
        var kickoffUtc = ParseDateTimeOffset(GetNestedString(fixture, "date"));
        return new ApiSportsFixtureSummary(
            (GetNestedInt(fixture, "id")?.ToString()) ?? fallbackFixtureId,
            kickoffUtc,
            kickoffUtc.HasValue ? DateOnly.FromDateTime(kickoffUtc.Value.UtcDateTime) : null,
            GetNestedInt(league, "id"),
            GetNestedString(league, "name") ?? "World Cup",
            GetNestedInt(league, "season"),
            GetNestedInt(GetPropertyOrDefault(teams, "home"), "id"),
            GetNestedString(GetPropertyOrDefault(teams, "home"), "name"),
            GetNestedInt(GetPropertyOrDefault(teams, "away"), "id"),
            GetNestedString(GetPropertyOrDefault(teams, "away"), "name"));
    }

    private static async Task<ApiSportsSoccerFixtureImportResult> ImportApiSportsSoccerFixtureAsync(
        IApiSportsFootballClient client,
        ApplicationDbContext dbContext,
        string fixtureId,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var fixtureResponse = await client.GetFixturesAsync(
            new ApiSportsFootballFixturesRequest(null, fixtureId, null, null),
            cancellationToken);
        var fixtureSummary = ExtractApiSportsFixtureSummary(fixtureResponse.Payload.RootElement, fixtureId);
        if (fixtureSummary.EventDate is null)
        {
            throw new InvalidOperationException("API-Sports fixture metadata did not include a usable fixture date, so prop_results cannot be written.");
        }

        var statsResponse = await client.GetFixturePlayerStatsAsync(fixtureId, cancellationToken);
        var playerStats = ExtractApiSportsPlayerStats(statsResponse.Payload.RootElement);
        var mappings = await LoadApiSportsSoccerResultMappingsAsync(dbContext, cancellationToken);
        var preview = BuildApiSportsSoccerResultPreview(fixtureId, fixtureSummary.EventDate.Value, playerStats, mappings);
        var writeSummary = dryRun
            ? new ApiSportsSoccerImportWriteSummary(0, 0)
            : await UpsertApiSportsSoccerResultsAsync(dbContext, fixtureSummary, preview.Rows, cancellationToken);
        var ruleConfidenceCounts = preview.Rows
            .GroupBy(row => row.RuleConfidence)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new ApiSportsSoccerFixtureImportResult(
            fixtureSummary,
            fixtureResponse,
            statsResponse,
            playerStats,
            preview,
            writeSummary,
            BuildApiSportsSoccerMappingDiagnostics(preview),
            ruleConfidenceCounts);
    }

    private static async Task<IReadOnlyList<ApiSportsFixtureDiscoveryRow>> DiscoverApiSportsWorldCupFixturesAsync(
        IApiSportsFootballClient client,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var fixtures = new List<ApiSportsFixtureDiscoveryRow>();
        var current = fromDate;
        while (current <= toDate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await client.GetFixturesAsync(
                new ApiSportsFootballFixturesRequest(current, null, ApiSportsWorldCupLeagueId, ApiSportsWorldCupSeason),
                cancellationToken);
            fixtures.AddRange(ExtractApiSportsFixtureDiscoveryRows(response.Payload.RootElement));
            current = current.AddDays(1);
        }

        return fixtures
            .GroupBy(row => row.FixtureId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => row.EventDate)
            .ThenBy(row => row.KickoffUtc)
            .ThenBy(row => row.FixtureId)
            .ToArray();
    }

    private static IReadOnlyList<ApiSportsFixtureDiscoveryRow> ExtractApiSportsFixtureDiscoveryRows(JsonElement root)
    {
        return EnumerateResponseItems(root)
            .Select(item =>
            {
                var fixture = GetPropertyOrDefault(item, "fixture");
                var teams = GetPropertyOrDefault(item, "teams");
                var league = GetPropertyOrDefault(item, "league");
                var kickoffUtc = ParseDateTimeOffset(GetNestedString(fixture, "date"));
                var fixtureId = GetNestedInt(fixture, "id")?.ToString() ?? string.Empty;
                var status = GetPropertyOrDefault(fixture, "status");
                return new ApiSportsFixtureDiscoveryRow(
                    fixtureId,
                    kickoffUtc.HasValue ? DateOnly.FromDateTime(kickoffUtc.Value.UtcDateTime) : null,
                    kickoffUtc,
                    GetNestedInt(league, "id"),
                    GetNestedInt(league, "season"),
                    GetNestedString(GetPropertyOrDefault(teams, "home"), "name"),
                    GetNestedString(GetPropertyOrDefault(teams, "away"), "name"),
                    GetNestedString(status, "short"),
                    GetNestedString(status, "long"));
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.FixtureId))
            .ToArray();
    }

    private static bool IsApiSportsFixtureCompleted(string? statusShort, string? statusLong)
    {
        return string.Equals(statusShort, "FT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusLong, "Match Finished", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetApiSportsFixtureSkipReason(ApiSportsFixtureDiscoveryRow fixture)
    {
        if (string.IsNullOrWhiteSpace(fixture.StatusShort) && string.IsNullOrWhiteSpace(fixture.StatusLong))
        {
            return "UnknownStatus";
        }

        if (string.Equals(fixture.StatusShort, "NS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fixture.StatusLong, "Not Started", StringComparison.OrdinalIgnoreCase))
        {
            return "Scheduled";
        }

        if (string.Equals(fixture.StatusShort, "1H", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fixture.StatusShort, "2H", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fixture.StatusShort, "HT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fixture.StatusLong, "Halftime", StringComparison.OrdinalIgnoreCase))
        {
            return "InProgress";
        }

        if (string.Equals(fixture.StatusShort, "PST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fixture.StatusShort, "CANC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fixture.StatusLong, "Match Postponed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fixture.StatusLong, "Match Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return "PostponedOrCanceled";
        }

        return "NotCompleted";
    }

    private static ApiSportsWorldCupDateRange NormalizeApiSportsWorldCupDateRange(DateOnly? fromDate, DateOnly? toDate)
    {
        var end = toDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = fromDate ?? ApiSportsWorldCup2026StartDate;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (start < ApiSportsWorldCup2026StartDate)
        {
            start = ApiSportsWorldCup2026StartDate;
        }

        return new ApiSportsWorldCupDateRange(start, end);
    }

    private static ApiSportsWorldCupDateRange NormalizeApiSportsHistoricalCoverageDateRange(DateOnly? fromDate, DateOnly? toDate)
    {
        var end = toDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = fromDate ?? new DateOnly(end.Year, 1, 1);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return new ApiSportsWorldCupDateRange(start, end);
    }

    private static IReadOnlyList<ApiSportsFootballHistoricalCompetitionOptions> GetConfiguredApiSportsHistoricalCompetitions(
        ApiSportsOptions options,
        bool enabledOnly)
    {
        return (options.Football.HistoricalCompetitions ?? [])
            .Where(row => !string.IsNullOrWhiteSpace(row.Key)
                && !string.IsNullOrWhiteSpace(row.DisplayName)
                && row.LeagueId > 0
                && row.Season > 0)
            .GroupBy(row => new { Key = row.Key.Trim().ToUpperInvariant(), row.LeagueId, row.Season })
            .Select(group => group.Last())
            .Where(row => !enabledOnly || row.Enabled)
            .Select(row => new ApiSportsFootballHistoricalCompetitionOptions
            {
                Key = row.Key.Trim(),
                DisplayName = row.DisplayName.Trim(),
                LeagueId = row.LeagueId,
                Season = row.Season,
                Enabled = row.Enabled,
                Country = string.IsNullOrWhiteSpace(row.Country) ? null : row.Country.Trim(),
                Type = string.IsNullOrWhiteSpace(row.Type) ? null : row.Type.Trim()
            })
            .OrderBy(row => row.DisplayName)
            .ThenBy(row => row.Season)
            .ToArray();
    }

    private static string BuildApiSportsHistoricalLeagueCode(ApiSportsFootballHistoricalCompetitionOptions competition)
    {
        var key = Regex.Replace(competition.Key.Trim().ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');
        return $"API_SPORTS_{key}_{competition.Season}";
    }

    private static string ToApiSportsHistoricalStatDisplayName(string statKey)
    {
        return statKey.ToUpperInvariant() switch
        {
            "GOALS" => "Goals",
            "ASSISTS" => "Assists",
            "SHOTS" => "Shots",
            "SHOTS_ON_TARGET" => "Shots On Target",
            _ => statKey
        };
    }

    private static async Task<ApiSportsWorldCupPersistedImportStatus> LoadApiSportsWorldCupPersistedImportStatusAsync(
        ApplicationDbContext dbContext,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                && result.EventDate >= fromDate
                && result.EventDate <= toDate
                && result.ExternalSourceIdentifier.StartsWith($"{ApiSportsFootballResultSource}:"))
            .Select(result => new
            {
                result.ExternalSourceIdentifier,
                result.EventDate,
                result.CreatedAt,
                result.UpdatedAt
            })
            .ToListAsync(cancellationToken);
        var grouped = rows
            .Select(row => new { FixtureId = ExtractApiSportsFixtureId(row.ExternalSourceIdentifier), row.EventDate, row.CreatedAt, row.UpdatedAt })
            .Where(row => !string.IsNullOrWhiteSpace(row.FixtureId))
            .GroupBy(row => row.FixtureId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ApiSportsWorldCupFixtureRowCount(
                group.Key,
                group.Min(row => row.EventDate),
                group.Count(),
                group.Max(row => row.UpdatedAt)))
            .OrderBy(row => row.EventDate)
            .ThenBy(row => row.FixtureId)
            .ToArray();
        var latest = grouped.OrderByDescending(row => row.EventDate).FirstOrDefault();
        DateTimeOffset? latestImportedAtUtc = rows.Count == 0 ? null : rows.Max(row => row.UpdatedAt);
        return new ApiSportsWorldCupPersistedImportStatus(
            grouped,
            grouped.Sum(row => row.RowCount),
            latest?.FixtureId,
            latest?.EventDate,
            latestImportedAtUtc);
    }

    private static IReadOnlyList<ApiSportsWorldCupDateCoverageRow> BuildApiSportsWorldCupStatusCoverageByDate(
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyList<ApiSportsWorldCupFixtureRowCount> persistedRows,
        IReadOnlyList<ApiSportsFixtureDiscoveryRow> discovered,
        bool discoveryAvailable,
        int? discoveryApiCallsUsed)
    {
        var completedFixtureIds = discovered
            .Where(row => IsApiSportsFixtureCompleted(row.StatusShort, row.StatusLong))
            .Select(row => row.FixtureId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importedFixtureIds = persistedRows
            .Select(row => row.FixtureId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return EnumerateDateRange(fromDate, toDate)
            .Select(date =>
            {
                var discoveredForDate = discovered.Where(row => row.EventDate == date).ToArray();
                var importedForDate = persistedRows.Where(row => row.EventDate == date).ToArray();
                var completedForDate = discoveredForDate
                    .Where(row => completedFixtureIds.Contains(row.FixtureId))
                    .ToArray();
                var skippedForDate = discoveredForDate.Length == 0
                    ? []
                    : discoveredForDate.Where(row => !completedFixtureIds.Contains(row.FixtureId)).ToArray();
                var missingCompletedForDate = completedForDate
                    .Where(row => !importedFixtureIds.Contains(row.FixtureId))
                    .ToArray();

                return new ApiSportsWorldCupDateCoverageRow(
                    date,
                    discoveryAvailable ? discoveredForDate.Length : null,
                    discoveryAvailable ? completedForDate.Length : null,
                    importedForDate.Length,
                    importedForDate.Sum(row => row.RowCount),
                    null,
                    discoveryAvailable ? skippedForDate.Length : null,
                    discoveryAvailable ? missingCompletedForDate.Length : null,
                    0,
                    discoveryAvailable ? 1 : null);
            })
            .ToArray();
    }

    private static IReadOnlyList<ApiSportsWorldCupDateCoverageRow> BuildApiSportsWorldCupBackfillCoverageByDate(
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyList<ApiSportsFixtureDiscoveryRow> discovered,
        IReadOnlyList<ApiSportsWorldCupSkippedFixture> skipped,
        IReadOnlyList<ApiSportsWorldCupFixtureImportOutcome> imports,
        IReadOnlyList<ApiSportsWorldCupFixtureImportFailure> failures)
    {
        var completedFixtureIds = discovered
            .Where(row => IsApiSportsFixtureCompleted(row.StatusShort, row.StatusLong))
            .Select(row => row.FixtureId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skippedFixtureIds = skipped
            .Select(row => row.FixtureId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var successfulImportFixtureIds = imports
            .Select(row => row.FixtureId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedFixtureIds = failures
            .Select(row => row.FixtureId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return EnumerateDateRange(fromDate, toDate)
            .Select(date =>
            {
                var discoveredForDate = discovered.Where(row => row.EventDate == date).ToArray();
                var completedForDate = discoveredForDate
                    .Where(row => completedFixtureIds.Contains(row.FixtureId))
                    .ToArray();
                var skippedForDate = discoveredForDate
                    .Where(row => skippedFixtureIds.Contains(row.FixtureId))
                    .ToArray();
                var importsForDate = imports.Where(row => row.EventDate == date).ToArray();
                var failuresForDate = failures.Where(row => row.EventDate == date).ToArray();
                var missingCompletedForDate = completedForDate
                    .Where(row => !successfulImportFixtureIds.Contains(row.FixtureId)
                        && !failedFixtureIds.Contains(row.FixtureId))
                    .ToArray();

                return new ApiSportsWorldCupDateCoverageRow(
                    date,
                    discoveredForDate.Length,
                    completedForDate.Length,
                    importsForDate.Length,
                    importsForDate.Sum(row => row.ResultRowsWritten),
                    importsForDate.Sum(row => row.ResultRowsPreviewed),
                    skippedForDate.Length,
                    missingCompletedForDate.Length,
                    failuresForDate.Length,
                    1 + completedForDate.Length);
            })
            .ToArray();
    }

    private static IEnumerable<DateOnly> EnumerateDateRange(DateOnly fromDate, DateOnly toDate)
    {
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static int CountDateRangeDays(DateOnly fromDate, DateOnly toDate)
    {
        return toDate.DayNumber - fromDate.DayNumber + 1;
    }

    private static string? ExtractApiSportsFixtureId(string externalSourceIdentifier)
    {
        var parts = externalSourceIdentifier.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && string.Equals(parts[0], ApiSportsFootballResultSource, StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }

    private static string? ExtractApiSportsPlayerId(string externalSourceIdentifier)
    {
        var parts = externalSourceIdentifier.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 3 && string.Equals(parts[0], ApiSportsFootballResultSource, StringComparison.OrdinalIgnoreCase)
            ? parts[2]
            : null;
    }

    private static object BuildApiSportsSoccerIdentityScope(ApiSportsWorldCupDateRange range, string? fixtureId)
    {
        return new
        {
            sport = "SOCCER",
            provider = "API-Sports",
            product = "API-Sports Football",
            competition = "World Cup",
            season = ApiSportsWorldCupSeason,
            range.FromDate,
            range.ToDate,
            fixtureId
        };
    }

    private static async Task<ApiSportsSoccerIdentityDiagnostics> BuildApiSportsSoccerIdentityDiagnosticsAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        string? fixtureId,
        CancellationToken cancellationToken)
    {
        var resultRows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.Player)!.ThenInclude(player => player!.Team)
            .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                && result.EventDate >= range.FromDate
                && result.EventDate <= range.ToDate
                && result.ExternalSourceIdentifier.StartsWith($"{ApiSportsFootballResultSource}:"))
            .Select(result => new
            {
                result.PlayerId,
                PlayerName = result.Player == null ? string.Empty : result.Player.DisplayName,
                TeamName = result.Player == null || result.Player.Team == null ? null : result.Player.Team.Name,
                result.ExternalSourceIdentifier
            })
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(fixtureId))
        {
            resultRows = resultRows
                .Where(row => string.Equals(ExtractApiSportsFixtureId(row.ExternalSourceIdentifier), fixtureId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var importedPlayers = resultRows
            .Select(row => new
            {
                ApiSportsPlayerId = ExtractApiSportsPlayerId(row.ExternalSourceIdentifier),
                row.PlayerId,
                row.PlayerName,
                NormalizedPlayerName = NormalizeResultPlayerName(row.PlayerName),
                row.TeamName,
                NormalizedTeamName = NormalizeResultPlayerName(row.TeamName),
                row.ExternalSourceIdentifier
            })
            .GroupBy(
                row => string.IsNullOrWhiteSpace(row.ApiSportsPlayerId)
                    ? $"local:{row.PlayerId}"
                    : row.ApiSportsPlayerId!,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group
                    .OrderBy(row => string.IsNullOrWhiteSpace(row.PlayerName))
                    .ThenBy(row => row.PlayerName)
                    .First();
                return new ApiSportsIdentityImportedPlayer(
                    group.Key,
                    first.PlayerId,
                    first.PlayerName,
                    first.NormalizedPlayerName,
                    first.TeamName,
                    first.NormalizedTeamName,
                    group.Count());
            })
            .OrderBy(player => player.ApiSportsPlayerName)
            .ToArray();

        var soccerPlayers = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.Team)
            .Include(player => player.League)!.ThenInclude(league => league!.Sport)
            .Where(player => player.League != null
                && player.League.Sport != null
                && player.League.Sport.Code == "SOCCER")
            .Select(player => new
            {
                player.Id,
                player.DisplayName,
                TeamName = player.Team == null ? null : player.Team.Name
            })
            .ToListAsync(cancellationToken);
        var soccerPlayerIds = soccerPlayers.Select(player => player.Id).ToArray();

        var contextRows = await LoadApiSportsSoccerPlatformContextRowsAsync(dbContext, range, cancellationToken);
        var contextRowsByPlayerId = contextRows
            .Where(row => row.CanonicalPlayerId.HasValue)
            .GroupBy(row => row.CanonicalPlayerId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var platformEvidenceRows = await dbContext.PlatformProps
            .AsNoTracking()
            .Include(platformProp => platformProp.Platform)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Player)!.ThenInclude(player => player!.Team)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(platformProp => platformProp.Prop != null
                && platformProp.Prop.Player != null
                && platformProp.Prop.League != null
                && platformProp.Prop.League.Sport != null
                && platformProp.Prop.League.Sport.Code == "SOCCER")
            .Select(platformProp => new
            {
                PlayerId = platformProp.Prop!.PlayerId,
                PlayerName = platformProp.Prop.Player == null ? string.Empty : platformProp.Prop.Player.DisplayName,
                TeamName = platformProp.Prop.Player == null || platformProp.Prop.Player.Team == null ? null : platformProp.Prop.Player.Team.Name,
                Provider = platformProp.Platform == null ? null : platformProp.Platform.Name,
                ProviderPlayerId = platformProp.ExternalStableKey ?? platformProp.PlatformPropId
            })
            .ToListAsync(cancellationToken);
        var platformPlayerIds = platformEvidenceRows.Select(row => row.PlayerId).Distinct().ToHashSet();
        var platformPropsCount = platformEvidenceRows.Count;

        var soccerRefs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => soccerPlayerIds.Contains(reference.PlayerId))
            .Select(reference => new
            {
                reference.PlayerId,
                reference.Provider,
                reference.ProviderPlayerId,
                reference.ProviderDisplayName
            })
            .ToListAsync(cancellationToken);
        var refsByApiSportsPlayerId = soccerRefs
            .Where(reference => string.Equals(reference.Provider, ApiSportsFootballPlayerRefProvider, StringComparison.OrdinalIgnoreCase))
            .GroupBy(reference => reference.ProviderPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var refsByPlayerId = soccerRefs
            .GroupBy(reference => reference.PlayerId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var soccerSportId = await dbContext.Sports
            .AsNoTracking()
            .Where(row => row.Code == "SOCCER")
            .Select(row => (Guid?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var providerLinks = soccerSportId.HasValue
            ? await dbContext.ProviderEntityLinks
                .AsNoTracking()
                .Where(link => link.SportId == soccerSportId.Value
                    && link.EntityType == "Player"
                    && link.ProviderKey == ApiSportsFootballPlayerRefProvider)
                .ToListAsync(cancellationToken)
            : [];
        var providerLinksByApiSportsPlayerId = providerLinks
            .GroupBy(link => link.ProviderEntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var snapshotRows = await LoadApiSportsSoccerSnapshotIdentityRowsAsync(dbContext, range, cancellationToken);
        var snapshotNames = snapshotRows
            .Select(row => row.NormalizedPlayerName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var candidatePlayers = soccerPlayers
            .GroupJoin(
                platformEvidenceRows,
                player => player.Id,
                evidence => evidence.PlayerId,
                (player, evidence) =>
                {
                    var evidenceRows = evidence.ToArray();
                    var contextForPlayer = contextRowsByPlayerId.GetValueOrDefault(player.Id) ?? [];
                    var refs = refsByPlayerId.TryGetValue(player.Id, out var playerRefs) ? playerRefs : [];
                    var providerEvidence = evidenceRows
                        .Select(row => row.Provider)
                        .Where(provider => !string.IsNullOrWhiteSpace(provider))
                        .Select(provider => provider!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order()
                        .ToArray();
                    return new ApiSportsIdentityLocalPlayer(
                        player.Id,
                        player.DisplayName,
                        NormalizeResultPlayerName(player.DisplayName),
                        player.TeamName,
                        NormalizeResultPlayerName(player.TeamName),
                        contextForPlayer
                            .SelectMany(row => new[] { row.TeamName, row.HomeTeamName, row.AwayTeamName })
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(NormalizeResultPlayerName)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        evidenceRows.Length,
                        providerEvidence,
                        evidenceRows.Select(row => row.ProviderPlayerId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
                        refs.Select(reference => reference.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
                })
            .ToArray();
        var candidatesByName = candidatePlayers
            .Where(player => !string.IsNullOrWhiteSpace(player.NormalizedPlayerName))
            .GroupBy(player => player.NormalizedPlayerName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var candidateRows = new List<ApiSportsIdentityCandidateRow>();
        var alreadyLinked = new List<object>();
        var exactName = new List<object>();
        var normalizedName = new List<object>();
        var teamAware = new List<object>();
        var ambiguous = new List<object>();
        var unmatched = new List<object>();
        var conflicts = new List<object>();

        foreach (var imported in importedPlayers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var apiRefs = refsByApiSportsPlayerId.TryGetValue(imported.ApiSportsPlayerId, out var refs)
                ? refs
                : [];
            var apiProviderLinks = providerLinksByApiSportsPlayerId.TryGetValue(imported.ApiSportsPlayerId, out var links)
                ? links
                : [];
            var approvedProviderLinks = apiProviderLinks
                .Where(link => string.Equals(link.LinkStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                    && link.CanonicalEntityId.HasValue)
                .ToArray();
            var linkedPlatformRefs = apiRefs.Where(reference => platformPlayerIds.Contains(reference.PlayerId)).ToArray();
            var nameCandidates = candidatesByName.TryGetValue(imported.NormalizedPlayerName, out var candidates)
                ? candidates.Where(candidate => candidate.PlayerId != imported.LocalResultPlayerId || candidate.PlatformPropCount > 0).ToArray()
                : [];
            var platformCandidates = nameCandidates.Where(candidate => candidate.PlatformPropCount > 0).ToArray();
            var effectiveCandidates = platformCandidates.Length > 0 ? platformCandidates : nameCandidates;
            var teamMatches = effectiveCandidates
                .Where(candidate => HasAnyTeamContextMatch(imported.NormalizedTeamName, candidate))
                .ToArray();
            var exactDisplayMatches = effectiveCandidates
                .Where(candidate => string.Equals(candidate.PlayerName, imported.ApiSportsPlayerName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var hasSnapshotNameEvidence = snapshotNames.Contains(imported.NormalizedPlayerName, StringComparer.OrdinalIgnoreCase);

            if (approvedProviderLinks.Length > 0)
            {
                alreadyLinked.Add(new
                {
                    imported.ApiSportsPlayerId,
                    apiSportsPlayerName = imported.ApiSportsPlayerName,
                    imported.ApiSportsTeamName,
                    imported.ResultRowCount,
                    linkedPlayerIds = approvedProviderLinks.Select(link => link.CanonicalEntityId).Distinct().ToArray(),
                    source = "provider_entity_links",
                    reason = "Approved provider entity link exists for this API-Sports player id."
                });
            }
            else if (linkedPlatformRefs.Length > 0)
            {
                alreadyLinked.Add(new
                {
                    imported.ApiSportsPlayerId,
                    apiSportsPlayerName = imported.ApiSportsPlayerName,
                    imported.ApiSportsTeamName,
                    imported.ResultRowCount,
                    linkedPlayerIds = linkedPlatformRefs.Select(reference => reference.PlayerId).Distinct().ToArray(),
                    source = "player_external_refs",
                    reason = "API-Sports external ref resolves to a Soccer player that has platform prop identity evidence."
                });
            }

            if (apiRefs.Length > 0 && linkedPlatformRefs.Length == 0 && apiRefs.Any(reference => reference.PlayerId != imported.LocalResultPlayerId))
            {
                conflicts.Add(new
                {
                    imported.ApiSportsPlayerId,
                    apiSportsPlayerName = imported.ApiSportsPlayerName,
                    imported.ApiSportsTeamName,
                    mappedPlayerIds = apiRefs.Select(reference => reference.PlayerId).Distinct().ToArray(),
                    reason = "API-Sports id is mapped to another Soccer player, but that player has no platform prop identity evidence in this diagnostic."
                });
            }

            if (effectiveCandidates.Length == 0)
            {
                unmatched.Add(new
                {
                    imported.ApiSportsPlayerId,
                    apiSportsPlayerName = imported.ApiSportsPlayerName,
                    imported.ApiSportsTeamName,
                    imported.ResultRowCount,
                    hasSnapshotNameEvidence,
                    reason = hasSnapshotNameEvidence
                        ? "Name appears in market snapshots, but no canonical Soccer player/platform identity candidate was found."
                        : "No normalized-name Soccer player/platform candidate was found."
                });
                continue;
            }

            if (exactDisplayMatches.Length > 0)
            {
                exactName.Add(BuildIdentitySample(imported, exactDisplayMatches, "ExactDisplayName"));
            }

            normalizedName.Add(BuildIdentitySample(imported, effectiveCandidates, "NormalizedName"));

            if (teamMatches.Length > 0)
            {
                teamAware.Add(BuildIdentitySample(imported, teamMatches, "NormalizedNameAndTeam"));
            }

            var ambiguousCandidates = teamMatches.Length > 1 || (teamMatches.Length == 0 && effectiveCandidates.Length > 1);
            if (ambiguousCandidates)
            {
                ambiguous.Add(BuildIdentitySample(imported, effectiveCandidates, "AmbiguousName"));
            }

            var teamConflict = !string.IsNullOrWhiteSpace(imported.NormalizedTeamName)
                && teamMatches.Length == 0
                && effectiveCandidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate.NormalizedTeamName));
            if (teamConflict)
            {
                conflicts.Add(BuildIdentitySample(imported, effectiveCandidates, "TeamMismatch"));
            }

            foreach (var candidate in effectiveCandidates)
            {
                var candidateTeamMatch = HasAnyTeamContextMatch(imported.NormalizedTeamName, candidate);
                var directPlayerTeamMatch = NamesMatch(imported.NormalizedTeamName, candidate.NormalizedTeamName);
                var candidateExactName = string.Equals(candidate.PlayerName, imported.ApiSportsPlayerName, StringComparison.OrdinalIgnoreCase);
                var blockers = new List<string>();
                var reasons = new List<string> { "Normalized player names match." };
                if (candidateExactName)
                {
                    reasons.Add("Display names match ignoring case.");
                }

                if (directPlayerTeamMatch)
                {
                    reasons.Add("Team names match after normalization.");
                }
                else if (candidateTeamMatch)
                {
                    blockers.Add("EventTeamOnly");
                    reasons.Add("Imported team appears in provider event context, but the candidate player's own team is missing.");
                }
                else if (string.IsNullOrWhiteSpace(imported.NormalizedTeamName) || (string.IsNullOrWhiteSpace(candidate.NormalizedTeamName) && candidate.NormalizedEventTeamNames.Count == 0))
                {
                    blockers.Add("MissingTeamContext");
                    reasons.Add("Team context is missing on one side.");
                }
                else
                {
                    blockers.Add("TeamMismatch");
                    reasons.Add("Team names do not match.");
                }

                if (ambiguousCandidates)
                {
                    blockers.Add("AmbiguousName");
                }

                if (candidate.PlatformPropCount == 0)
                {
                    blockers.Add("NoPlatformPropEvidence");
                }

                var confidence = CalculateApiSportsIdentityConfidence(candidateExactName, directPlayerTeamMatch, blockers);
                var suggestedAction = DetermineApiSportsIdentitySuggestedAction(confidence, blockers);
                candidateRows.Add(new ApiSportsIdentityCandidateRow(
                    imported.ApiSportsPlayerId,
                    imported.ApiSportsPlayerName,
                    imported.ApiSportsTeamName,
                    imported.ResultRowCount,
                    candidate.PlayerId,
                    candidate.PlayerName,
                    candidate.ProviderPlayerId,
                    candidate.Providers.FirstOrDefault(),
                    candidate.TeamName,
                    confidence,
                    reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    suggestedAction));
            }
        }

        return new ApiSportsSoccerIdentityDiagnostics(
            importedPlayers,
            resultRows.Count,
            soccerPlayers.Count,
            platformPlayerIds.Count,
            soccerRefs.Count,
            providerLinks.Count,
            providerLinks.Count(link => string.Equals(link.ProviderKey, ApiSportsFootballPlayerRefProvider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.EntityType, "Player", StringComparison.OrdinalIgnoreCase)),
            providerLinks.Count(link => string.Equals(link.LinkStatus, "Approved", StringComparison.OrdinalIgnoreCase) && link.CanonicalEntityId.HasValue),
            providerLinks.Count(link => string.Equals(link.LinkStatus, "Candidate", StringComparison.OrdinalIgnoreCase)),
            providerLinks.Count(link => string.Equals(link.LinkStatus, "NeedsReview", StringComparison.OrdinalIgnoreCase)),
            providerLinks.Count(link => string.Equals(link.LinkStatus, "Rejected", StringComparison.OrdinalIgnoreCase)),
            providerLinks.Count(link => string.Equals(link.LinkStatus, "Conflict", StringComparison.OrdinalIgnoreCase)),
            providerLinks.Count(link => !string.IsNullOrWhiteSpace(link.ProviderTeamId) || !string.IsNullOrWhiteSpace(link.ProviderTeamName)),
            platformPropsCount,
            snapshotNames.Length,
            alreadyLinked,
            exactName,
            normalizedName,
            teamAware,
            ambiguous,
            unmatched,
            conflicts,
            candidateRows);
    }

    private static async Task<IReadOnlyList<ApiSportsCoverageSnapshotRow>> LoadApiSportsSoccerSnapshotIdentityRowsAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        CancellationToken cancellationToken)
    {
        var top = await dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Sport == "SOCCER"
                && snapshot.SlateDate >= range.FromDate
                && snapshot.SlateDate <= range.ToDate)
            .Select(snapshot => new ApiSportsCoverageSnapshotRow(
                "TopPickSnapshot",
                snapshot.Player,
                NormalizeResultPlayerName(snapshot.Player),
                snapshot.PropType,
                string.Empty,
                NormalizeResultPropTypeName(snapshot.PropType),
                snapshot.EventStartTime.HasValue ? DateOnly.FromDateTime(snapshot.EventStartTime.Value.UtcDateTime) : snapshot.SlateDate,
                snapshot.EventId,
                snapshot.Bookmaker,
                snapshot.ProviderCategory))
            .ToListAsync(cancellationToken);
        var lineShopping = await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Sport == "SOCCER"
                && snapshot.SlateDate >= range.FromDate
                && snapshot.SlateDate <= range.ToDate)
            .Select(snapshot => new ApiSportsCoverageSnapshotRow(
                "LineShoppingSnapshot",
                snapshot.Player,
                NormalizeResultPlayerName(snapshot.Player),
                snapshot.PropType,
                string.Empty,
                NormalizeResultPropTypeName(snapshot.PropType),
                snapshot.EventStartTime.HasValue ? DateOnly.FromDateTime(snapshot.EventStartTime.Value.UtcDateTime) : snapshot.SlateDate,
                snapshot.EventId,
                snapshot.Bookmaker,
                snapshot.ProviderCategory))
            .ToListAsync(cancellationToken);
        var recommendations = await dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Sport == "SOCCER"
                && snapshot.ExpectedResultDate >= range.FromDate
                && snapshot.ExpectedResultDate <= range.ToDate)
            .Select(snapshot => new ApiSportsCoverageSnapshotRow(
                "RecommendationSnapshot",
                snapshot.PlayerName,
                snapshot.NormalizedPlayerName,
                snapshot.PropType,
                string.Empty,
                snapshot.NormalizedPropType,
                snapshot.ExpectedResultDate,
                snapshot.EventId,
                snapshot.Bookmaker,
                snapshot.ProviderCategory))
            .ToListAsync(cancellationToken);

        return top.Concat(lineShopping).Concat(recommendations).ToArray();
    }

    private static async Task<IReadOnlyList<ApiSportsPlatformContextRow>> LoadApiSportsSoccerPlatformContextRowsAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        CancellationToken cancellationToken)
    {
        var fromUtc = new DateTimeOffset(range.FromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(range.ToDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var platformPropRawRows = await dbContext.PlatformProps
            .AsNoTracking()
            .Include(platformProp => platformProp.Platform)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Player)!.ThenInclude(player => player!.Team)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.PropType)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Game)!.ThenInclude(game => game!.HomeTeam)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Game)!.ThenInclude(game => game!.AwayTeam)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(platformProp => platformProp.Prop != null
                && platformProp.Prop.League != null
                && platformProp.Prop.League.Sport != null
                && platformProp.Prop.League.Sport.Code == "SOCCER"
                && platformProp.Prop.StartsAt >= fromUtc
                && platformProp.Prop.StartsAt <= toUtc)
            .Select(platformProp => new
            {
                platformProp.Id,
                platformProp.PlatformPropId,
                platformProp.ExternalStableKey,
                platformProp.Metadata,
                Provider = platformProp.Platform == null ? null : platformProp.Platform.Name,
                Bookmaker = platformProp.Platform == null ? null : platformProp.Platform.Slug,
                CanonicalPlayerId = (Guid?)platformProp.Prop!.PlayerId,
                PlayerName = platformProp.Prop.Player == null ? string.Empty : platformProp.Prop.Player.DisplayName,
                TeamName = platformProp.Prop.Player == null || platformProp.Prop.Player.Team == null ? null : platformProp.Prop.Player.Team.Name,
                PropType = platformProp.Prop.PropType == null ? string.Empty : platformProp.Prop.PropType.Name,
                StartTime = platformProp.Prop.StartsAt,
                GameHomeTeam = platformProp.Prop.Game == null || platformProp.Prop.Game.HomeTeam == null ? null : platformProp.Prop.Game.HomeTeam.Name,
                GameAwayTeam = platformProp.Prop.Game == null || platformProp.Prop.Game.AwayTeam == null ? null : platformProp.Prop.Game.AwayTeam.Name
            })
            .ToListAsync(cancellationToken);
        var rows = platformPropRawRows
            .Select(row =>
            {
                var metadataHome = ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "homeTeam");
                var metadataAway = ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "awayTeam");
                var homeTeam = FirstNonBlank(row.GameHomeTeam, metadataHome);
                var awayTeam = FirstNonBlank(row.GameAwayTeam, metadataAway);
                var eventName = BuildApiSportsContextEventName(awayTeam, homeTeam);
                var opponent = ResolveOpponent(row.TeamName, homeTeam, awayTeam);
                return new ApiSportsPlatformContextRow(
                    "platform_props",
                    "platform_props",
                    row.CanonicalPlayerId,
                    row.PlayerName,
                    row.PropType,
                    row.TeamName,
                    opponent,
                    eventName,
                    row.StartTime,
                    FirstNonBlank(
                        ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "providerEventId"),
                        ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "eventId")),
                    FirstNonBlank(
                        ExtractApiSportsContextMetadataString(row.Metadata, "player", "id"),
                        ExtractApiSportsContextMetadataString(row.Metadata, "participant", "id")),
                    row.Provider,
                    row.Bookmaker,
                    homeTeam,
                    awayTeam,
                    FirstNonBlank(
                        ExtractApiSportsContextMetadataRootString(row.Metadata, "competition"),
                        ExtractApiSportsContextMetadataRootString(row.Metadata, "league"),
                        ExtractApiSportsContextMetadataString(row.Metadata, "sport", "competition"),
                        ExtractApiSportsContextMetadataString(row.Metadata, "sport", "leagueName"),
                        ExtractApiSportsContextMetadataString(row.Metadata, "sport", "providerSportKey"),
                        ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "competition"),
                        ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "league")));
            })
            .ToList();

        var topRows = await dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Sport == "SOCCER"
                && snapshot.SlateDate >= range.FromDate
                && snapshot.SlateDate <= range.ToDate)
            .Select(snapshot => new ApiSportsPlatformContextRow(
                "market_top_pick_snapshots",
                "market_top_pick_snapshots",
                null,
                snapshot.Player,
                snapshot.PropType,
                null,
                null,
                snapshot.EventName,
                snapshot.EventStartTime,
                snapshot.EventId,
                null,
                snapshot.Provider,
                snapshot.Bookmaker,
                null,
                null,
                snapshot.BookmakerPreset))
            .ToListAsync(cancellationToken);
        var lineRows = await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Sport == "SOCCER"
                && snapshot.SlateDate >= range.FromDate
                && snapshot.SlateDate <= range.ToDate)
            .Select(snapshot => new ApiSportsPlatformContextRow(
                "market_line_shopping_snapshots",
                "market_line_shopping_snapshots",
                null,
                snapshot.Player,
                snapshot.PropType,
                null,
                null,
                snapshot.EventName,
                snapshot.EventStartTime,
                snapshot.EventId,
                null,
                snapshot.Provider,
                snapshot.Bookmaker,
                null,
                null,
                snapshot.BookmakerPreset))
            .ToListAsync(cancellationToken);
        var recommendationRows = await dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Sport == "SOCCER"
                && snapshot.ExpectedResultDate >= range.FromDate
                && snapshot.ExpectedResultDate <= range.ToDate)
            .Select(snapshot => new ApiSportsPlatformContextRow(
                "recommendation_snapshots",
                "recommendation_snapshots",
                snapshot.CanonicalPlayerId,
                snapshot.PlayerName,
                snapshot.PropType,
                null,
                null,
                null,
                snapshot.EventStartTimeUtc,
                snapshot.EventId,
                null,
                snapshot.Provider,
                snapshot.Bookmaker,
                null,
                null,
                null))
            .ToListAsync(cancellationToken);

        return rows.Concat(topRows).Concat(lineRows).Concat(recommendationRows).ToArray();
    }

    private static ApiSportsPlatformContextAvailability BuildApiSportsSoccerPlatformContextAvailability(IEnumerable<ApiSportsPlatformContextRow> rows)
    {
        var array = rows.ToArray();
        return new ApiSportsPlatformContextAvailability(
            array.Count(row => !string.IsNullOrWhiteSpace(row.TeamName)),
            array.Count(row => !string.IsNullOrWhiteSpace(row.OpponentName)),
            array.Count(row => !string.IsNullOrWhiteSpace(row.EventName)),
            array.Count(row => row.StartTime.HasValue),
            array.Count(row => !string.IsNullOrWhiteSpace(row.ProviderEventId)),
            array.Count(row => !string.IsNullOrWhiteSpace(row.ProviderPlayerId)),
            array.Count(row => !string.IsNullOrWhiteSpace(row.Bookmaker)),
            array.Count(row => !string.IsNullOrWhiteSpace(row.LeagueOrCompetition)));
    }

    private static IReadOnlyList<string> BuildAvailableContextFields(ApiSportsPlatformContextAvailability availability)
    {
        var fields = new List<string>();
        if (availability.HasTeamNameCount > 0) fields.Add("teamName");
        if (availability.HasOpponentNameCount > 0) fields.Add("opponentName");
        if (availability.HasEventNameCount > 0) fields.Add("eventName");
        if (availability.HasStartTimeCount > 0) fields.Add("startTime");
        if (availability.HasProviderEventIdCount > 0) fields.Add("providerEventId");
        if (availability.HasProviderPlayerIdCount > 0) fields.Add("providerPlayerId");
        if (availability.HasBookmakerKeyCount > 0) fields.Add("bookmakerKey");
        if (availability.HasLeagueOrCompetitionCount > 0) fields.Add("leagueOrCompetition");
        return fields;
    }

    private static IReadOnlyList<string> BuildMissingContextFields(ApiSportsPlatformContextAvailability availability)
    {
        var fields = new List<string>();
        if (availability.HasTeamNameCount == 0) fields.Add("teamName");
        if (availability.HasOpponentNameCount == 0) fields.Add("opponentName");
        if (availability.HasEventNameCount == 0) fields.Add("eventName");
        if (availability.HasStartTimeCount == 0) fields.Add("startTime");
        if (availability.HasProviderEventIdCount == 0) fields.Add("providerEventId");
        if (availability.HasProviderPlayerIdCount == 0) fields.Add("providerPlayerId");
        if (availability.HasBookmakerKeyCount == 0) fields.Add("bookmakerKey");
        if (availability.HasLeagueOrCompetitionCount == 0) fields.Add("leagueOrCompetition");
        return fields;
    }

    private static string BuildApiSportsSoccerPlatformContextConclusion(ApiSportsPlatformContextAvailability availability)
    {
        if (availability.HasTeamNameCount > 0)
        {
            return "Canonical Soccer player team context exists for some platform rows and can support team-aware candidate review.";
        }

        if (availability.HasEventNameCount > 0 || availability.HasProviderEventIdCount > 0 || availability.HasStartTimeCount > 0)
        {
            return "Event context exists, but player team context is missing; use manual review before creating aliases.";
        }

        return "No persisted platform team/event context was found for this range; manual alias review remains required.";
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? ResolveOpponent(string? teamName, string? homeTeam, string? awayTeam)
    {
        var normalizedTeam = NormalizeResultPlayerName(teamName);
        if (string.IsNullOrWhiteSpace(normalizedTeam))
        {
            return null;
        }

        if (NamesMatch(normalizedTeam, NormalizeResultPlayerName(homeTeam)))
        {
            return awayTeam;
        }

        if (NamesMatch(normalizedTeam, NormalizeResultPlayerName(awayTeam)))
        {
            return homeTeam;
        }

        return null;
    }

    private static string? BuildApiSportsContextEventName(string? awayTeam, string? homeTeam)
    {
        return !string.IsNullOrWhiteSpace(awayTeam) && !string.IsNullOrWhiteSpace(homeTeam)
            ? $"{awayTeam} @ {homeTeam}"
            : null;
    }

    private static string? ExtractApiSportsContextMetadataRootString(JsonDocument? metadata, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static string? ExtractApiSportsContextMetadataString(JsonDocument? metadata, string sectionName, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty(sectionName, out var section)
            || section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static bool? ExtractApiSportsContextMetadataBool(JsonDocument? metadata, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? ExtractApiSportsContextMetadataBool(JsonDocument? metadata, string sectionName, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty(sectionName, out var section)
            || section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool NamesMatch(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAnyTeamContextMatch(string? normalizedImportedTeamName, ApiSportsIdentityLocalPlayer candidate)
    {
        return NamesMatch(normalizedImportedTeamName, candidate.NormalizedTeamName)
            || candidate.NormalizedEventTeamNames.Contains(normalizedImportedTeamName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static decimal CalculateApiSportsIdentityConfidence(bool exactName, bool teamMatch, IReadOnlyCollection<string> blockers)
    {
        if (blockers.Contains("TeamMismatch", StringComparer.OrdinalIgnoreCase))
        {
            return 0.65m;
        }

        if (blockers.Contains("AmbiguousName", StringComparer.OrdinalIgnoreCase))
        {
            return teamMatch ? 0.88m : 0.78m;
        }

        if (teamMatch)
        {
            return exactName ? 0.97m : 0.95m;
        }

        if (blockers.Contains("MissingTeamContext", StringComparer.OrdinalIgnoreCase))
        {
            return exactName ? 0.86m : 0.84m;
        }

        return exactName ? 0.82m : 0.80m;
    }

    private static string DetermineApiSportsIdentitySuggestedAction(decimal confidence, IReadOnlyCollection<string> blockers)
    {
        if (blockers.Contains("TeamMismatch", StringComparer.OrdinalIgnoreCase))
        {
            return "Conflict";
        }

        if (blockers.Contains("NoPlatformPropEvidence", StringComparer.OrdinalIgnoreCase))
        {
            return "NeedsReview";
        }

        if (confidence >= 0.95m && blockers.Count == 0)
        {
            return "SafeAliasLink";
        }

        return "NeedsReview";
    }

    private static async Task<Guid?> GetSportIdAsync(ApplicationDbContext dbContext, string sportCode, CancellationToken cancellationToken)
    {
        return await dbContext.Sports
            .AsNoTracking()
            .Where(sport => sport.Code == sportCode)
            .Select(sport => (Guid?)sport.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static bool IsMlbLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return normalized.Equals("MLB", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Major League Baseball", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<ApiSportsReviewQueueRow>> BuildApiSportsReviewQueueRowsAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        decimal minimumConfidence,
        bool includeAmbiguousRows,
        bool includeNoCandidateRows,
        string normalizedLinkStatus,
        CancellationToken cancellationToken)
    {
        var identity = await BuildApiSportsSoccerIdentityDiagnosticsAsync(dbContext, range, fixtureId: null, cancellationToken);
        var soccerSportId = await GetSportIdAsync(dbContext, "SOCCER", cancellationToken);
        var links = soccerSportId.HasValue
            ? await dbContext.ProviderEntityLinks
                .AsNoTracking()
                .Where(link => link.SportId == soccerSportId.Value
                    && link.EntityType == "Player"
                    && link.ProviderKey == ApiSportsFootballPlayerRefProvider
                    && link.LinkStatus == normalizedLinkStatus)
                .OrderBy(link => link.ProviderEntityName)
                .ToListAsync(cancellationToken)
            : [];
        var importedByApiSportsId = identity.ImportedPlayers.ToDictionary(player => player.ApiSportsPlayerId, StringComparer.OrdinalIgnoreCase);
        var candidatesByApiSportsId = identity.Candidates
            .Where(candidate => candidate.Confidence >= minimumConfidence)
            .Where(candidate => includeAmbiguousRows || !candidate.Blockers.Contains("AmbiguousName", StringComparer.OrdinalIgnoreCase))
            .GroupBy(candidate => candidate.ApiSportsPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(candidate => candidate.Confidence).ThenBy(candidate => candidate.CandidateCanonicalPlayerName).ToArray(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<ApiSportsReviewQueueRow>();
        foreach (var link in links)
        {
            importedByApiSportsId.TryGetValue(link.ProviderEntityId, out var imported);
            if (!candidatesByApiSportsId.TryGetValue(link.ProviderEntityId, out var candidates) || candidates.Length == 0)
            {
                if (includeNoCandidateRows)
                {
                    rows.Add(BuildApiSportsReviewQueueRow(link, imported, candidate: null));
                }

                continue;
            }

            foreach (var candidate in candidates)
            {
                rows.Add(BuildApiSportsReviewQueueRow(link, imported, candidate));
            }
        }

        return rows;
    }

    private static ApiSportsReviewQueueRow BuildApiSportsReviewQueueRow(
        ProviderEntityLink link,
        ApiSportsIdentityImportedPlayer? imported,
        ApiSportsIdentityCandidateRow? candidate)
    {
        var blockers = candidate?.Blockers ?? [];
        var suggestedAction = link.LinkStatus.Equals("Conflict", StringComparison.OrdinalIgnoreCase)
            ? "Conflict"
            : candidate is null
                ? "NoCandidate"
                : candidate.SuggestedAction.Equals("Conflict", StringComparison.OrdinalIgnoreCase)
                    ? "Conflict"
                    : candidate.SuggestedAction.Equals("SafeAliasLink", StringComparison.OrdinalIgnoreCase)
                        ? "ApproveCandidate"
                        : "NeedsReview";

        return new ApiSportsReviewQueueRow(
            link.Id,
            link.ProviderEntityId,
            imported?.ApiSportsPlayerName ?? link.ProviderEntityName,
            imported?.ApiSportsTeamName ?? link.ProviderTeamName,
            imported?.ResultRowCount ?? 0,
            link.LinkStatus,
            candidate is not null,
            candidate?.CandidateCanonicalPlayerId,
            candidate?.CandidateCanonicalPlayerName,
            candidate?.CandidateProviderPlayerId,
            candidate?.CandidateProvider,
            candidate?.CandidateTeamName,
            candidate?.Confidence,
            candidate?.Reasons ?? [],
            blockers,
            suggestedAction);
    }

    private static IOrderedEnumerable<ApiSportsReviewQueueRow> SortApiSportsReviewQueueRows(
        IReadOnlyList<ApiSportsReviewQueueRow> rows,
        string sort)
    {
        return sort.Equals("playerName", StringComparison.OrdinalIgnoreCase)
            ? rows
                .OrderBy(row => row.ApiSportsPlayerName)
                .ThenBy(row => GetApiSportsReviewQueueActionRank(row.SuggestedAction))
                .ThenByDescending(row => row.Confidence ?? -1m)
            : rows
                .OrderBy(row => GetApiSportsReviewQueueActionRank(row.SuggestedAction))
                .ThenByDescending(row => row.Confidence ?? -1m)
                .ThenBy(row => row.ApiSportsPlayerName);
    }

    private static int GetApiSportsReviewQueueActionRank(string suggestedAction)
    {
        return suggestedAction switch
        {
            "ApproveCandidate" => 0,
            "NeedsReview" => 1,
            "Conflict" => 2,
            "NoCandidate" => 3,
            _ => 4
        };
    }

    private static object[] ToApiSportsReviewQueueExportRows(IReadOnlyList<ApiSportsReviewQueueRow> rows, int take)
    {
        return SortApiSportsReviewQueueRows(rows, "confidenceDesc")
            .Take(take)
            .Select(row => new
            {
                row.ProviderEntityLinkId,
                row.ApiSportsPlayerName,
                row.ApiSportsTeamName,
                row.CandidateCanonicalPlayerId,
                row.CandidateCanonicalPlayerName,
                row.CandidateProvider,
                row.Confidence,
                row.Blockers,
                row.SuggestedAction,
                suggestedApprovalJson = row.HasCandidate && row.CandidateCanonicalPlayerId.HasValue
                    ? new
                    {
                        providerEntityLinkId = row.ProviderEntityLinkId,
                        canonicalPlayerId = row.CandidateCanonicalPlayerId,
                        reason = "manual exact-name review"
                    }
                    : null
            })
            .ToArray();
    }

    private static async Task<Dictionary<ApiSportsBulkApprovalKey, IReadOnlyList<string>>> LoadApiSportsBulkApprovalReviewBlockersAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<ApiSportsBulkApprovalRequest> approvals,
        CancellationToken cancellationToken)
    {
        if (approvals.Count == 0)
        {
            return [];
        }

        var range = NormalizeApiSportsWorldCupDateRange(fromDate: null, toDate: null);
        var rows = await BuildApiSportsReviewQueueRowsAsync(
            dbContext,
            range,
            minimumConfidence: 0m,
            includeAmbiguousRows: true,
            includeNoCandidateRows: false,
            normalizedLinkStatus: "Candidate",
            cancellationToken);
        return rows
            .Where(row => row.HasCandidate
                && row.CandidateCanonicalPlayerId.HasValue
                && row.Blockers.Count > 0)
            .GroupBy(row => new ApiSportsBulkApprovalKey(row.ProviderEntityLinkId, row.CandidateCanonicalPlayerId!.Value))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.SelectMany(row => row.Blockers).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static async Task<ApiSportsProviderLinkMutationResult> AddApiSportsSingleApprovalReviewWarningsAsync(
        ApplicationDbContext dbContext,
        ApiSportsProviderLinkMutationResult result,
        ApiSportsProviderLinkApprovalRequest request,
        CancellationToken cancellationToken)
    {
        if (result.Blockers.Count > 0)
        {
            return result;
        }

        var blockers = await LoadApiSportsBulkApprovalReviewBlockersAsync(
            dbContext,
            [new ApiSportsBulkApprovalRequest(request.ProviderEntityLinkId, request.CanonicalPlayerId, request.Reason)],
            cancellationToken);
        if (!blockers.TryGetValue(new ApiSportsBulkApprovalKey(request.ProviderEntityLinkId, request.CanonicalPlayerId), out var reviewBlockers)
            || reviewBlockers.Count == 0)
        {
            return result;
        }

        return result with
        {
            Warnings = result.Warnings
                .Concat(reviewBlockers.Select(blocker => $"ManualApprovalNeedsReviewCandidate:{blocker}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static async Task<ApiSportsProviderLinkMutationResult> BuildApiSportsProviderLinkApprovalResultAsync(
        ApplicationDbContext dbContext,
        ApiSportsProviderLinkApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var dryRun = request.DryRun != false;
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var blockers = new List<string>();
        var link = await dbContext.ProviderEntityLinks
            .Include(row => row.Sport)
            .SingleOrDefaultAsync(row => row.Id == request.ProviderEntityLinkId, cancellationToken);
        var before = link is null ? null : BuildProviderEntityLinkState(link);

        if (link is null)
        {
            blockers.Add("ProviderEntityLinkNotFound");
            return new ApiSportsProviderLinkMutationResult(dryRun, dryRun, false, before, null, warnings, blockers, null);
        }

        ValidateApiSportsSoccerProviderLink(link, blockers);

        var canonicalPlayer = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)!.ThenInclude(league => league!.Sport)
            .SingleOrDefaultAsync(player => player.Id == request.CanonicalPlayerId, cancellationToken);
        if (canonicalPlayer is null)
        {
            blockers.Add("CanonicalPlayerNotFound");
        }
        else if (!string.Equals(canonicalPlayer.League?.Sport?.Code, "SOCCER", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("CanonicalPlayerSportMismatch");
        }

        if (link.LinkStatus.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("ProviderEntityLinkRejected");
        }
        else if (link.LinkStatus.Equals("Conflict", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("ProviderEntityLinkConflict");
        }

        if (link.LinkStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase)
            && link.CanonicalEntityId.HasValue
            && link.CanonicalEntityId.Value != request.CanonicalPlayerId)
        {
            blockers.Add("AlreadyApprovedToDifferentCanonicalPlayer");
        }

        if (blockers.Count > 0 || canonicalPlayer is null)
        {
            return new ApiSportsProviderLinkMutationResult(dryRun, dryRun, false, before, before, warnings, blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), link);
        }

        if (link.LinkStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase)
            && link.CanonicalEntityId == request.CanonicalPlayerId)
        {
            warnings.Add("AlreadyApproved");
            return new ApiSportsProviderLinkMutationResult(dryRun, dryRun, false, before, before, warnings, blockers, link);
        }

        var after = BuildApprovedProviderEntityLinkState(link, canonicalPlayer, now, request.Reason);
        if (dryRun)
        {
            return new ApiSportsProviderLinkMutationResult(true, true, true, before, after, warnings, blockers, link);
        }

        ApplyApprovedProviderEntityLink(link, canonicalPlayer, now, request.Reason);
        return new ApiSportsProviderLinkMutationResult(false, false, true, before, BuildProviderEntityLinkState(link), warnings, blockers, link);
    }

    private static async Task<ApiSportsBulkApproveLinksResult> BuildApiSportsBulkApproveLinksResultAsync(
        ApplicationDbContext dbContext,
        ApiSportsBulkApproveLinksRequest request,
        CancellationToken cancellationToken)
    {
        var dryRun = request.DryRun != false;
        var allowPartial = request.AllowPartial == true;
        var allowNeedsReview = request.AllowNeedsReview == true;
        var approvals = request.Approvals ?? [];
        var warnings = new List<string>();
        var blockers = new List<string>();
        var reviewBlockersByApproval = await LoadApiSportsBulkApprovalReviewBlockersAsync(dbContext, approvals, cancellationToken);
        var validationResults = new List<ApiSportsProviderLinkMutationResult>();
        foreach (var approval in approvals)
        {
            var result = await BuildApiSportsProviderLinkApprovalResultAsync(
                dbContext,
                new ApiSportsProviderLinkApprovalRequest(approval.ProviderEntityLinkId, approval.CanonicalPlayerId, true, approval.Reason),
                cancellationToken);
            if (!allowNeedsReview
                && reviewBlockersByApproval.TryGetValue(new ApiSportsBulkApprovalKey(approval.ProviderEntityLinkId, approval.CanonicalPlayerId), out var reviewBlockers)
                && reviewBlockers.Count > 0)
            {
                result = result with
                {
                    Blockers = result.Blockers
                        .Concat(reviewBlockers.Select(blocker => $"CandidateReviewBlocker:{blocker}"))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }
            else if (allowNeedsReview
                && reviewBlockersByApproval.TryGetValue(new ApiSportsBulkApprovalKey(approval.ProviderEntityLinkId, approval.CanonicalPlayerId), out var allowedReviewBlockers)
                && allowedReviewBlockers.Count > 0)
            {
                result = result with
                {
                    Warnings = result.Warnings
                        .Concat(allowedReviewBlockers.Select(blocker => $"ApprovingNeedsReviewCandidate:{blocker}"))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }

            validationResults.Add(result);
        }

        var blockedCount = validationResults.Count(result => result.Blockers.Count > 0);
        var validResults = validationResults.Where(result => result.Blockers.Count == 0).ToArray();
        if (blockedCount > 0)
        {
            blockers.AddRange(validationResults.SelectMany(result => result.Blockers).Distinct(StringComparer.OrdinalIgnoreCase));
        }

        if (dryRun || (blockedCount > 0 && !allowPartial))
        {
            if (blockedCount > 0 && !allowPartial && !dryRun)
            {
                warnings.Add("No rows were written because allowPartial=false and at least one approval was blocked.");
            }

            return new ApiSportsBulkApproveLinksResult(
                dryRun,
                dryRun,
                allowPartial,
                allowNeedsReview,
                approvals.Count,
                validResults.Length,
                blockedCount,
                0,
                validationResults,
                blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                warnings);
        }

        var writeResults = new List<ApiSportsProviderLinkMutationResult>();
        for (var index = 0; index < approvals.Count; index++)
        {
            var approval = approvals[index];
            var validation = validationResults[index];
            if (validation.Blockers.Count > 0)
            {
                writeResults.Add(validation);
                continue;
            }

            writeResults.Add(await BuildApiSportsProviderLinkApprovalResultAsync(
                dbContext,
                new ApiSportsProviderLinkApprovalRequest(approval.ProviderEntityLinkId, approval.CanonicalPlayerId, false, approval.Reason),
                cancellationToken));
            if (allowNeedsReview
                && reviewBlockersByApproval.TryGetValue(new ApiSportsBulkApprovalKey(approval.ProviderEntityLinkId, approval.CanonicalPlayerId), out var allowedReviewBlockers)
                && allowedReviewBlockers.Count > 0)
            {
                var lastIndex = writeResults.Count - 1;
                writeResults[lastIndex] = writeResults[lastIndex] with
                {
                    Warnings = writeResults[lastIndex].Warnings
                        .Concat(allowedReviewBlockers.Select(blocker => $"ApprovingNeedsReviewCandidate:{blocker}"))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var normalizedWriteResults = writeResults
            .Select(result => result.Link is not null && result.Blockers.Count == 0
                ? result with { After = BuildProviderEntityLinkState(result.Link) }
                : result)
            .ToArray();
        return new ApiSportsBulkApproveLinksResult(
            false,
            false,
            allowPartial,
            allowNeedsReview,
            approvals.Count,
            validResults.Length,
            blockedCount,
            normalizedWriteResults.Count(result => result.Changed && result.Blockers.Count == 0),
            normalizedWriteResults,
            blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings);
    }

    private static async Task<ApiSportsProviderLinkMutationResult> BuildApiSportsProviderLinkRejectionResultAsync(
        ApplicationDbContext dbContext,
        ApiSportsProviderLinkRejectionRequest request,
        CancellationToken cancellationToken)
    {
        var dryRun = request.DryRun != false;
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var blockers = new List<string>();
        var link = await dbContext.ProviderEntityLinks
            .Include(row => row.Sport)
            .SingleOrDefaultAsync(row => row.Id == request.ProviderEntityLinkId, cancellationToken);
        var before = link is null ? null : BuildProviderEntityLinkState(link);

        if (link is null)
        {
            blockers.Add("ProviderEntityLinkNotFound");
            return new ApiSportsProviderLinkMutationResult(dryRun, dryRun, false, before, null, warnings, blockers, null);
        }

        ValidateApiSportsSoccerProviderLink(link, blockers);
        if (link.LinkStatus.Equals("Conflict", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("ProviderEntityLinkConflict");
        }

        if (blockers.Count > 0)
        {
            return new ApiSportsProviderLinkMutationResult(dryRun, dryRun, false, before, before, warnings, blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), link);
        }

        if (link.LinkStatus.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("AlreadyRejected");
            return new ApiSportsProviderLinkMutationResult(dryRun, dryRun, false, before, before, warnings, blockers, link);
        }

        var after = BuildRejectedProviderEntityLinkState(link, now, request.Reason);
        if (dryRun)
        {
            return new ApiSportsProviderLinkMutationResult(true, true, true, before, after, warnings, blockers, link);
        }

        ApplyRejectedProviderEntityLink(link, now, request.Reason);
        return new ApiSportsProviderLinkMutationResult(false, false, true, before, BuildProviderEntityLinkState(link), warnings, blockers, link);
    }

    private static void ValidateApiSportsSoccerProviderLink(ProviderEntityLink link, ICollection<string> blockers)
    {
        if (!string.Equals(link.ProviderKey, ApiSportsFootballPlayerRefProvider, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("ProviderKeyMismatch");
        }

        if (!string.Equals(link.EntityType, "Player", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("EntityTypeMismatch");
        }

        if (!string.Equals(link.Sport?.Code, "SOCCER", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("ProviderEntityLinkSportMismatch");
        }
    }

    private static void ApplyApprovedProviderEntityLink(ProviderEntityLink link, Player canonicalPlayer, DateTimeOffset now, string? reason)
    {
        link.CanonicalEntityId = canonicalPlayer.Id;
        link.CanonicalEntityKind = "Player";
        link.CanonicalEntityName = canonicalPlayer.DisplayName;
        link.LinkStatus = "Approved";
        link.LinkSource = "Manual";
        link.ApprovedAtUtc = now;
        link.ApprovedBy = "debug/manual";
        link.RejectedAtUtc = null;
        link.RejectedReason = null;
        link.UpdatedAtUtc = now;
        link.MetadataJson = BuildManualReviewMetadata("Approved", reason, now);
    }

    private static void ApplyRejectedProviderEntityLink(ProviderEntityLink link, DateTimeOffset now, string? reason)
    {
        link.CanonicalEntityId = null;
        link.CanonicalEntityKind = null;
        link.CanonicalEntityName = null;
        link.LinkStatus = "Rejected";
        link.LinkSource = "Manual";
        link.ApprovedAtUtc = null;
        link.ApprovedBy = null;
        link.RejectedAtUtc = now;
        link.RejectedReason = string.IsNullOrWhiteSpace(reason) ? "manual review" : reason.Trim();
        link.UpdatedAtUtc = now;
        link.MetadataJson = BuildManualReviewMetadata("Rejected", reason, now);
    }

    private static JsonDocument BuildManualReviewMetadata(string action, string? reason, DateTimeOffset reviewedAtUtc)
    {
        return JsonSerializer.SerializeToDocument(new
        {
            action,
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            reviewedAtUtc,
            reviewedBy = "debug/manual"
        });
    }

    private static ProviderEntityLinkState BuildApprovedProviderEntityLinkState(ProviderEntityLink link, Player canonicalPlayer, DateTimeOffset now, string? reason)
    {
        return BuildProviderEntityLinkState(
            link,
            canonicalEntityId: canonicalPlayer.Id,
            canonicalEntityKind: "Player",
            canonicalEntityName: canonicalPlayer.DisplayName,
            linkStatus: "Approved",
            linkSource: "Manual",
            approvedAtUtc: now,
            approvedBy: "debug/manual",
            rejectedAtUtc: null,
            rejectedReason: null);
    }

    private static ProviderEntityLinkState BuildRejectedProviderEntityLinkState(ProviderEntityLink link, DateTimeOffset now, string? reason)
    {
        return new ProviderEntityLinkState(
            link.Id,
            link.SportId,
            link.Sport?.Code,
            link.EntityType,
            null,
            null,
            null,
            link.ProviderKey,
            link.ProviderEntityId,
            link.ProviderEntityName,
            link.ProviderTeamId,
            link.ProviderTeamName,
            "Rejected",
            "Manual",
            null,
            null,
            now,
            string.IsNullOrWhiteSpace(reason) ? "manual review" : reason.Trim());
    }

    private static ProviderEntityLinkState BuildProviderEntityLinkState(
        ProviderEntityLink link,
        Guid? canonicalEntityId = null,
        string? canonicalEntityKind = null,
        string? canonicalEntityName = null,
        string? linkStatus = null,
        string? linkSource = null,
        DateTimeOffset? approvedAtUtc = null,
        string? approvedBy = null,
        DateTimeOffset? rejectedAtUtc = null,
        string? rejectedReason = null)
    {
        return new ProviderEntityLinkState(
            link.Id,
            link.SportId,
            link.Sport?.Code,
            link.EntityType,
            canonicalEntityId ?? link.CanonicalEntityId,
            canonicalEntityKind ?? link.CanonicalEntityKind,
            canonicalEntityName ?? link.CanonicalEntityName,
            link.ProviderKey,
            link.ProviderEntityId,
            link.ProviderEntityName,
            link.ProviderTeamId,
            link.ProviderTeamName,
            linkStatus ?? link.LinkStatus,
            linkSource ?? link.LinkSource,
            approvedAtUtc ?? link.ApprovedAtUtc,
            approvedBy ?? link.ApprovedBy,
            rejectedAtUtc ?? link.RejectedAtUtc,
            rejectedReason ?? link.RejectedReason);
    }

    private static object BuildIdentitySample(
        ApiSportsIdentityImportedPlayer imported,
        IReadOnlyList<ApiSportsIdentityLocalPlayer> candidates,
        string reason)
    {
        return new
        {
            imported.ApiSportsPlayerId,
            apiSportsPlayerName = imported.ApiSportsPlayerName,
            imported.ApiSportsTeamName,
            imported.ResultRowCount,
            reason,
            candidates = candidates
                .OrderByDescending(candidate => NamesMatch(imported.NormalizedTeamName, candidate.NormalizedTeamName))
                .ThenByDescending(candidate => candidate.PlatformPropCount)
                .ThenBy(candidate => candidate.PlayerName)
                .Take(5)
                .Select(candidate => new
                {
                    candidate.PlayerId,
                    playerName = candidate.PlayerName,
                    teamName = candidate.TeamName,
                    candidate.PlatformPropCount,
                    providers = candidate.Providers
                })
                .ToArray()
        };
    }

    private static ApiSportsCoverageMatchDiagnostic BuildApiSportsCoverageMatch(
        ApiSportsCoverageResultRow result,
        IReadOnlyList<ApiSportsCoverageMarketRow> platformRows,
        IReadOnlyList<ApiSportsCoverageSnapshotRow> snapshotRows,
        bool noMarketDataAvailable)
    {
        if (noMarketDataAvailable)
        {
            return ApiSportsCoverageMatchDiagnostic.FromResult(result, false, false, "MarketMissing", "No Soccer platform props, recommendations, or market snapshots exist in this date range.");
        }

        var effectivePlayerId = result.ApprovedCanonicalPlayerId ?? result.PlayerId;
        var platformPlayerMatches = platformRows.Where(row => row.PlayerId == effectivePlayerId).ToArray();
        var platformPropMatches = platformPlayerMatches.Where(row => row.PropTypeId == result.PropTypeId || string.Equals(row.StatKey, result.StatKey, StringComparison.OrdinalIgnoreCase)).ToArray();
        var platformDateMatches = platformPropMatches.Where(row => row.EventDate == result.EventDate).ToArray();
        var snapshotPlayerMatches = snapshotRows.Where(row => row.NormalizedPlayerName == result.NormalizedPlayerName).ToArray();
        var snapshotPropMatches = snapshotPlayerMatches
            .Where(row => row.NormalizedPropType == result.NormalizedPropTypeName
                || (!string.IsNullOrWhiteSpace(row.StatKey) && string.Equals(row.StatKey, result.StatKey, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var snapshotDateMatches = snapshotPropMatches.Where(row => row.EventDate == result.EventDate).ToArray();
        var matchedPlatform = platformDateMatches.Length > 0;
        var matchedSnapshot = snapshotDateMatches.Length > 0;

        if (matchedPlatform || matchedSnapshot)
        {
            return ApiSportsCoverageMatchDiagnostic.FromResult(
                result,
                matchedPlatform,
                matchedSnapshot,
                "Matched",
                matchedPlatform
                    ? result.ApprovedCanonicalPlayerId.HasValue
                        ? "Matched by approved provider entity link, prop type/statKey, and event date."
                        : "Matched by canonical player id, prop type/statKey, and event date."
                    : "Matched by normalized player name, prop type, and event date.");
        }

        if (platformPlayerMatches.Length == 0 && snapshotPlayerMatches.Length == 0)
        {
            return ApiSportsCoverageMatchDiagnostic.FromResult(result, false, false, "PlayerMissing", "No matching Soccer player was found in platform props, recommendations, or market snapshots.");
        }

        if (platformPropMatches.Length == 0 && snapshotPropMatches.Length == 0)
        {
            return ApiSportsCoverageMatchDiagnostic.FromResult(result, false, false, "PropTypeMissing", "Player matched, but no local market row matched the imported result prop type/statKey.");
        }

        if (platformDateMatches.Length == 0 && snapshotDateMatches.Length == 0)
        {
            return ApiSportsCoverageMatchDiagnostic.FromResult(result, false, false, "DateMismatch", "Player and prop matched, but no local market row matched the imported event/result date.");
        }

        return ApiSportsCoverageMatchDiagnostic.FromResult(result, false, false, "Unknown", "No safe local join path was identified.");
    }

    private static async Task<IReadOnlyList<ApiSportsMarketJoinResultRow>> LoadApiSportsMarketJoinResultRowsAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        string? fixtureId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.Player)!.ThenInclude(player => player!.Team)
            .Include(result => result.PropType)
            .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                && result.EventDate >= range.FromDate
                && result.EventDate <= range.ToDate
                && result.ExternalSourceIdentifier.StartsWith($"{ApiSportsFootballResultSource}:"))
            .Select(result => new
            {
                result.Id,
                result.EventDate,
                result.ExternalSourceIdentifier,
                result.ActualResultValue,
                result.PlayerId,
                PlayerName = result.Player == null ? string.Empty : result.Player.DisplayName,
                TeamName = result.Player == null || result.Player.Team == null ? null : result.Player.Team.Name,
                PropTypeName = result.PropType == null ? string.Empty : result.PropType.Name,
                StatKey = result.PropType == null ? string.Empty : result.PropType.StatKey
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ApiSportsMarketJoinResultRow(
                row.Id,
                row.EventDate,
                ExtractApiSportsFixtureId(row.ExternalSourceIdentifier),
                ExtractApiSportsPlayerId(row.ExternalSourceIdentifier),
                row.PlayerId,
                row.PlayerName,
                NormalizeResultPlayerName(row.PlayerName),
                row.TeamName,
                NormalizeResultPlayerName(row.TeamName),
                row.PropTypeName,
                NormalizeApiSportsResultStatKey(row.StatKey, row.PropTypeName),
                row.ActualResultValue,
                row.ExternalSourceIdentifier))
            .Where(row => string.IsNullOrWhiteSpace(fixtureId)
                || string.Equals(row.ApiSportsFixtureId, fixtureId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static async Task<IReadOnlyList<ApiSportsMarketJoinPlatformRow>> LoadApiSportsMarketJoinPlatformRowsAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        string? providerEventId,
        CancellationToken cancellationToken)
    {
        var fromUtc = new DateTimeOffset(range.FromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(range.ToDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var rows = await dbContext.PlatformProps
            .AsNoTracking()
            .Include(platformProp => platformProp.Platform)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Player)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.PropType)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Game)!.ThenInclude(game => game!.HomeTeam)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Game)!.ThenInclude(game => game!.AwayTeam)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(platformProp => platformProp.Prop != null
                && platformProp.Prop.League != null
                && platformProp.Prop.League.Sport != null
                && platformProp.Prop.League.Sport.Code == "SOCCER"
                && platformProp.Prop.StartsAt >= fromUtc
                && platformProp.Prop.StartsAt <= toUtc)
            .Select(platformProp => new
            {
                platformProp.Id,
                platformProp.PlatformPropId,
                platformProp.ExternalStableKey,
                platformProp.CurrentLine,
                platformProp.OverAvailable,
                platformProp.UnderAvailable,
                platformProp.Metadata,
                Provider = platformProp.Platform == null ? null : platformProp.Platform.Name,
                Bookmaker = platformProp.Platform == null ? null : platformProp.Platform.Slug,
                PlayerName = platformProp.Prop!.Player == null ? string.Empty : platformProp.Prop.Player.DisplayName,
                PropTypeName = platformProp.Prop.PropType == null ? string.Empty : platformProp.Prop.PropType.Name,
                StatKey = platformProp.Prop.PropType == null ? string.Empty : platformProp.Prop.PropType.StatKey,
                EventStartTime = platformProp.Prop.StartsAt,
                GameHomeTeam = platformProp.Prop.Game == null || platformProp.Prop.Game.HomeTeam == null ? null : platformProp.Prop.Game.HomeTeam.Name,
                GameAwayTeam = platformProp.Prop.Game == null || platformProp.Prop.Game.AwayTeam == null ? null : platformProp.Prop.Game.AwayTeam.Name
            })
            .ToListAsync(cancellationToken);

        var platformRows = rows
            .Select(row =>
            {
                var metadataProviderEventId = FirstNonBlank(
                    ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "providerEventId"),
                    ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "eventId"),
                    row.ExternalStableKey);
                var marketKey = ExtractApiSportsContextMetadataString(row.Metadata, "market", "key");
                var homeTeam = FirstNonBlank(row.GameHomeTeam, ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "homeTeam"));
                var awayTeam = FirstNonBlank(row.GameAwayTeam, ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "awayTeam"));
                return new ApiSportsMarketJoinPlatformRow(
                    row.Id,
                    row.PlatformPropId,
                    metadataProviderEventId,
                    ExtractApiSportsContextMetadataRootString(row.Metadata, "dataProvider"),
                    ExtractApiSportsContextMetadataRootString(row.Metadata, "contextSchemaVersion"),
                    row.Provider,
                    row.Bookmaker,
                    row.PlayerName,
                    NormalizeResultPlayerName(row.PlayerName),
                    row.PropTypeName,
                    NormalizeApiSportsResultStatKey(row.StatKey, row.PropTypeName),
                    marketKey,
                    NormalizeTheOddsSoccerMarketKeyToStatKey(marketKey),
                    row.CurrentLine,
                    ResolveApiSportsMarketJoinSide(row.OverAvailable, row.UnderAvailable),
                    DateOnly.FromDateTime(row.EventStartTime.UtcDateTime),
                    row.EventStartTime,
                    BuildApiSportsContextEventName(awayTeam, homeTeam),
                    homeTeam,
                    awayTeam,
                    ExtractApiSportsContextMetadataString(row.Metadata, "player", "providerPlayerId"),
                    ExtractApiSportsContextMetadataString(row.Metadata, "player", "teamName"),
                    FirstNonBlank(
                        ExtractApiSportsContextMetadataRootString(row.Metadata, "providerCategory"),
                        ExtractApiSportsContextMetadataString(row.Metadata, "entry", "providerCategory")),
                    ExtractApiSportsContextMetadataString(row.Metadata, "sport", "providerSportKey"),
                    ExtractApiSportsContextMetadataString(row.Metadata, "sport", "competition"),
                    ExtractApiSportsContextMetadataBool(row.Metadata, "isActionableForUser")
                        ?? ExtractApiSportsContextMetadataBool(row.Metadata, "entry", "actionableForUser"),
                    ExtractApiSportsContextMetadataBool(row.Metadata, "sportsbookContextResearchOnly")
                        ?? string.Equals(
                            FirstNonBlank(
                                ExtractApiSportsContextMetadataRootString(row.Metadata, "providerCategory"),
                                ExtractApiSportsContextMetadataString(row.Metadata, "entry", "providerCategory")),
                            "Sportsbook",
                            StringComparison.OrdinalIgnoreCase),
                    AliasCanonicalPlayerId: null);
            })
            .Where(row => string.IsNullOrWhiteSpace(providerEventId)
                || string.Equals(row.ProviderEventId, providerEventId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var aliasProviderEntityIds = platformRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderEventId))
            .Select(row => BuildTheOddsSoccerAliasProviderEntityId(row.ProviderEventId!, row.NormalizedPlayerName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (aliasProviderEntityIds.Length == 0)
        {
            return platformRows;
        }

        var aliasLinks = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == TheOddsSoccerAliasProviderKey
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved"
                && link.CanonicalEntityId.HasValue
                && aliasProviderEntityIds.Contains(link.ProviderEntityId))
            .Select(link => new { link.ProviderEntityId, link.CanonicalEntityId })
            .ToListAsync(cancellationToken);
        var aliasesByProviderEntityId = aliasLinks
            .GroupBy(link => link.ProviderEntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().CanonicalEntityId, StringComparer.OrdinalIgnoreCase);

        return platformRows
            .Select(row =>
            {
                if (string.IsNullOrWhiteSpace(row.ProviderEventId))
                {
                    return row;
                }

                var providerEntityId = BuildTheOddsSoccerAliasProviderEntityId(row.ProviderEventId, row.NormalizedPlayerName);
                return aliasesByProviderEntityId.TryGetValue(providerEntityId, out var canonicalPlayerId)
                    ? row with { AliasCanonicalPlayerId = canonicalPlayerId }
                    : row;
            })
            .ToArray();
    }

    private static async Task<IReadOnlyList<ApiSportsProviderStatMappingDiagnosticRow>> LoadApiSportsSoccerProviderStatMappingRowsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.ProviderStatMappings
            .AsNoTracking()
            .Include(mapping => mapping.Sport)
            .Where(mapping => mapping.IsActive
                && mapping.ProviderKey == ApiSportsFootballResultSource
                && mapping.Sport != null
                && mapping.Sport.Code == "SOCCER"
                && mapping.CompetitionScope == "World Cup"
                && mapping.Season == ApiSportsWorldCupSeason)
            .Select(mapping => new ApiSportsProviderStatMappingDiagnosticRow(
                mapping.CanonicalStatKey,
                mapping.DisplayName,
                ExtractJsonStringArray(mapping.ProviderStatFieldsJson),
                mapping.RuleConfidence,
                mapping.IsGradeable,
                mapping.PublicGradingEnabled,
                mapping.ProviderStatKey,
                mapping.ValueTransform,
                mapping.Notes))
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<TheOddsSoccerAliasApprovalCounts> CountApprovedTheOddsSoccerAliasLinksAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var links = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == TheOddsSoccerAliasProviderKey
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved")
            .Select(link => new { link.LinkSource })
            .ToListAsync(cancellationToken);
        return new TheOddsSoccerAliasApprovalCounts(
            links.Count,
            links.Count(link => string.Equals(link.LinkSource, "DebugTheOddsApiSportsAliasLink", StringComparison.OrdinalIgnoreCase)),
            links.Count(link => string.Equals(link.LinkSource, "DebugTheOddsApiSportsManualAliasApproval", StringComparison.OrdinalIgnoreCase)
                || string.Equals(link.LinkSource, "Manual", StringComparison.OrdinalIgnoreCase)),
            links.Count(link => !string.Equals(link.LinkSource, "DebugTheOddsApiSportsAliasLink", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(link.LinkSource, "DebugTheOddsApiSportsManualAliasApproval", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(link.LinkSource, "Manual", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<IReadOnlySet<string>> LoadApprovedTheOddsSoccerAliasProviderEntityIdsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<string> providerEntityIds,
        CancellationToken cancellationToken)
    {
        if (providerEntityIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var approved = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == TheOddsSoccerAliasProviderKey
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved"
                && providerEntityIds.Contains(link.ProviderEntityId))
            .Select(link => link.ProviderEntityId)
            .ToArrayAsync(cancellationToken);
        return approved.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ApiSportsMarketJoinPreview BuildApiSportsMarketJoinPreview(
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        int sampleLimit)
    {
        var eventMatches = resultRows.ToDictionary(
            row => row.ResultId,
            row => platformRows.Where(platform => IsApiSportsMarketJoinEventMatch(row, platform)).ToArray());
        var playerMatches = resultRows.ToDictionary(
            row => row.ResultId,
            row => eventMatches[row.ResultId].Where(platform => IsApiSportsMarketJoinPlayerMatch(row, platform)).ToArray());
        var propMatches = resultRows.ToDictionary(
            row => row.ResultId,
            row => eventMatches[row.ResultId].Where(platform => ApiSportsSoccerStatKeysMatch(row.StatKey, platform.CanonicalStatKey)).ToArray());
        var fullMatches = resultRows
            .SelectMany(result => eventMatches[result.ResultId]
                .Where(platform => IsApiSportsMarketJoinPlayerMatch(result, platform)
                    && ApiSportsSoccerStatKeysMatch(result.StatKey, platform.CanonicalStatKey))
                .Select(platform => BuildApiSportsMarketJoinMatchedSample(result, platform)))
            .ToArray();
        var fullyJoinableResultIds = fullMatches.Select(row => row.ResultId).Distinct().ToHashSet();
        var matchedPlatformIds = fullMatches.Select(row => row.PlatformPropId).Distinct().ToHashSet();
        var unmatchedResults = resultRows
            .Where(row => !fullyJoinableResultIds.Contains(row.ResultId))
            .Select(row => BuildApiSportsMarketJoinUnmatchedResultSample(row, eventMatches[row.ResultId], playerMatches[row.ResultId], propMatches[row.ResultId]))
            .ToArray();
        var unmatchedPlatforms = platformRows
            .Where(row => !matchedPlatformIds.Contains(row.PlatformPropId))
            .Select(BuildApiSportsMarketJoinUnmatchedPlatformSample)
            .ToArray();
        var eventMatchedRows = eventMatches.Count(pair => pair.Value.Length > 0);
        var playerNameMatchedRows = playerMatches.Count(pair => pair.Value.Length > 0);
        var propTypeMatchedRows = propMatches.Count(pair => pair.Value.Length > 0);
        var playerAndPropMatchedRows = resultRows.Count(row => playerMatches[row.ResultId]
            .Any(platform => ApiSportsSoccerStatKeysMatch(row.StatKey, platform.CanonicalStatKey)));
        var fullyJoinableRows = fullyJoinableResultIds.Count;
        var blockerSummary = BuildApiSportsMarketJoinBlockers(resultRows, platformRows, unmatchedResults);
        var summary = new
        {
            importedResultRows = resultRows.Count,
            candidatePlatformRows = platformRows.Count,
            eventMatchedRows,
            playerNameMatchedRows,
            propTypeMatchedRows,
            playerAndPropMatchedRows,
            fullyJoinableRows,
            unmatchedResultRows = unmatchedResults.Length,
            unmatchedPlatformRows = unmatchedPlatforms.Length,
            joinRate = resultRows.Count == 0 ? 0 : decimal.Round((decimal)fullyJoinableRows / resultRows.Count, 4)
        };

        return new ApiSportsMarketJoinPreview(
            summary,
            blockerSummary,
            new
            {
                matched = fullMatches.Take(sampleLimit).ToArray(),
                unmatchedResults = unmatchedResults.Take(sampleLimit).ToArray(),
                unmatchedPlatformRows = unmatchedPlatforms.Take(sampleLimit).ToArray()
            },
            BuildApiSportsMarketJoinRecommendation(resultRows.Count, eventMatchedRows, fullyJoinableRows, blockerSummary));
    }

    private static bool IsApiSportsMarketJoinEventMatch(ApiSportsMarketJoinResultRow result, ApiSportsMarketJoinPlatformRow platform)
    {
        if (result.EventDate != platform.EventDate)
        {
            return false;
        }

        if (NamesMatch(result.NormalizedTeamName, NormalizeResultPlayerName(platform.HomeTeamName))
            || NamesMatch(result.NormalizedTeamName, NormalizeResultPlayerName(platform.AwayTeamName)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(platform.ProviderEventId)
            && string.IsNullOrWhiteSpace(result.TeamName);
    }

    private static bool IsApiSportsMarketJoinPlayerMatch(ApiSportsMarketJoinResultRow result, ApiSportsMarketJoinPlatformRow platform)
    {
        return string.Equals(platform.NormalizedPlayerName, result.NormalizedPlayerName, StringComparison.OrdinalIgnoreCase)
            || (platform.AliasCanonicalPlayerId.HasValue && platform.AliasCanonicalPlayerId.Value == result.PlayerId);
    }

    private static bool ApiSportsSoccerStatKeysMatch(string? resultStatKey, string? platformStatKey)
    {
        return !string.IsNullOrWhiteSpace(resultStatKey)
            && !string.IsNullOrWhiteSpace(platformStatKey)
            && string.Equals(resultStatKey, platformStatKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeApiSportsResultStatKey(string? statKey, string? propTypeName)
    {
        var compact = NormalizeResultPropTypeName(FirstNonBlank(statKey, propTypeName));
        return compact switch
        {
            "GOAL" or "GOALS" => "GOALS",
            "ASSIST" or "ASSISTS" => "ASSISTS",
            "SHOT" or "SHOTS" => "SHOTS",
            "SHOTSONTARGET" or "SHOTONTARGET" or "SHOTS_ON_TARGET" or "PLAYERSHOTSONTARGET" => "SHOTS_ON_TARGET",
            "TACKLE" or "TACKLES" => "TACKLES",
            "GOALIESAVES" or "GOALKEEPERSAVES" or "SAVES" => "GOALIE_SAVES",
            "PASSESATTEMPTED" or "PASSES" or "PASSATTEMPTS" => "PASSES_ATTEMPTED",
            "GOALASSIST" or "GOALSASSISTS" or "GOALPLUSASSIST" => "GOAL_ASSIST",
            "ATTEMPTEDDRIBBLES" or "DRIBBLESATTEMPTED" => "ATTEMPTED_DRIBBLES",
            "FOULSDRAWN" or "DRAWNFOULS" => "FOULS_DRAWN",
            "SHOTSASSISTED" or "ASSISTEDSHOTS" => "SHOTS_ASSISTED",
            _ => compact
        };
    }

    private static string? NormalizeTheOddsSoccerMarketKeyToStatKey(string? marketKey)
    {
        return marketKey?.Trim().ToLowerInvariant() switch
        {
            "player_goals" => "GOALS",
            "player_assists" => "ASSISTS",
            "player_shots" => "SHOTS",
            "player_shots_on_target" => "SHOTS_ON_TARGET",
            "player_tackles" => "TACKLES",
            "player_goalkeeper_saves" => "GOALIE_SAVES",
            _ => null
        };
    }

    private static string BuildTheOddsSoccerAliasProviderEntityId(string providerEventId, string normalizedPlatformPlayerName)
    {
        return $"soccer_fifa_world_cup:{providerEventId.Trim()}:{normalizedPlatformPlayerName.Trim().ToUpperInvariant()}";
    }

    private static string ResolveApiSportsMarketJoinSide(bool overAvailable, bool underAvailable)
    {
        return (overAvailable, underAvailable) switch
        {
            (true, false) => "Over",
            (false, true) => "Under",
            (true, true) => "Both",
            _ => "Unavailable"
        };
    }

    private static ApiSportsMarketJoinMatchedSample BuildApiSportsMarketJoinMatchedSample(
        ApiSportsMarketJoinResultRow result,
        ApiSportsMarketJoinPlatformRow platform)
    {
        var joinBasis = new List<string> { "EventDate", "NormalizedPlayerName", "CanonicalStatKey" };
        if (platform.AliasCanonicalPlayerId.HasValue && platform.AliasCanonicalPlayerId.Value == result.PlayerId)
        {
            joinBasis.Add("ApprovedTheOddsAliasLink");
        }

        if (!string.IsNullOrWhiteSpace(platform.ProviderEventId))
        {
            joinBasis.Add("ProviderEventIdPresent");
        }

        if (!string.IsNullOrWhiteSpace(result.TeamName)
            && (NamesMatch(result.NormalizedTeamName, NormalizeResultPlayerName(platform.HomeTeamName))
                || NamesMatch(result.NormalizedTeamName, NormalizeResultPlayerName(platform.AwayTeamName))))
        {
            joinBasis.Add("TeamContext");
        }

        return new ApiSportsMarketJoinMatchedSample(
            result.ResultId,
            platform.PlatformPropId,
            result.ApiSportsFixtureId,
            result.PlayerName,
            result.TeamName,
            result.PropTypeName,
            result.StatKey,
            result.ActualResultValue,
            platform.ProviderEventId,
            platform.PlayerName,
            platform.Bookmaker,
            platform.Provider,
            platform.MarketKey,
            platform.PropTypeName,
            platform.Line,
            platform.Side,
            platform.EventName,
            platform.EventDate,
            platform.EventStartTimeUtc,
            platform.HomeTeamName,
            platform.AwayTeamName,
            joinBasis);
    }

    private static ApiSportsMarketJoinUnmatchedResultSample BuildApiSportsMarketJoinUnmatchedResultSample(
        ApiSportsMarketJoinResultRow result,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> eventCandidates,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> playerCandidates,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> propCandidates)
    {
        var reason = eventCandidates.Count == 0
            ? "EventDateMismatch"
            : playerCandidates.Count == 0
                ? "PlayerNameMismatch"
                : propCandidates.Count == 0
                    ? "PropTypeMappingMissing"
                    : "PlayerAndPropMismatch";

        return new ApiSportsMarketJoinUnmatchedResultSample(
            reason,
            result.ApiSportsFixtureId,
            result.PlayerName,
            result.TeamName,
            result.PropTypeName,
            result.StatKey,
            result.ActualResultValue,
            result.EventDate,
            eventCandidates.Count,
            playerCandidates.Count,
            propCandidates.Count);
    }

    private static ApiSportsMarketJoinUnmatchedPlatformSample BuildApiSportsMarketJoinUnmatchedPlatformSample(ApiSportsMarketJoinPlatformRow platform)
    {
        return new ApiSportsMarketJoinUnmatchedPlatformSample(
            platform.ProviderEventId,
            platform.PlayerName,
            platform.Bookmaker,
            platform.Provider,
            platform.MarketKey,
            platform.CanonicalStatKey,
            platform.PropTypeName,
            platform.Line,
            platform.Side,
            platform.EventName,
            platform.EventDate,
            platform.EventStartTimeUtc,
            platform.HomeTeamName,
            platform.AwayTeamName);
    }

    private static ApiSportsMarketJoinBlockerSummary BuildApiSportsMarketJoinBlockers(
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        IReadOnlyList<ApiSportsMarketJoinUnmatchedResultSample> unmatchedResults)
    {
        var missingProviderEventId = platformRows.Count(row => string.IsNullOrWhiteSpace(row.ProviderEventId));
        var missingProviderPlayerId = platformRows.Count(row => string.IsNullOrWhiteSpace(row.ProviderPlayerId));
        var missingPlayerTeam = resultRows.Count(row => string.IsNullOrWhiteSpace(row.TeamName))
            + platformRows.Count(row => string.IsNullOrWhiteSpace(row.PlayerTeamName));
        var propTypeMappingMissing = platformRows.Count(row => string.IsNullOrWhiteSpace(row.CanonicalStatKey))
            + unmatchedResults.Count(row => row.Reason == "PropTypeMappingMissing");
        var playerNameMismatch = unmatchedResults.Count(row => row.Reason == "PlayerNameMismatch");
        var eventDateMismatch = unmatchedResults.Count(row => row.Reason == "EventDateMismatch");
        var notes = new List<string>();

        if (platformRows.Count > 0)
        {
            notes.Add("The Odds platform rows do not currently store API-Sports fixture ids; event joins rely on provider event context plus date/team context.");
        }

        if (missingProviderPlayerId > 0 || platformRows.Any(row => string.IsNullOrWhiteSpace(row.PlayerTeamName)))
        {
            notes.Add("The Odds Soccer player outcomes do not include provider player ids or player team names, so player joins are normalized-name diagnostics only.");
        }

        return new ApiSportsMarketJoinBlockerSummary(
            missingProviderEventId,
            platformRows.Count,
            missingPlayerTeam,
            missingProviderPlayerId,
            propTypeMappingMissing,
            playerNameMismatch,
            eventDateMismatch,
            notes);
    }

    private static string BuildApiSportsMarketJoinRecommendation(
        int resultRows,
        int eventMatchedRows,
        int fullyJoinableRows,
        ApiSportsMarketJoinBlockerSummary blockers)
    {
        if (resultRows == 0)
        {
            return "No imported API-Sports result rows were found for this scope. Import completed fixture results before evaluating the market join.";
        }

        if (fullyJoinableRows > 0)
        {
            return "Persisted API-Sports results and The Odds sportsbook context overlap by event, player name, and prop stat key. A debug-only Soccer grading preview can be built next while public Soccer grading remains disabled.";
        }

        if (eventMatchedRows > 0 && blockers.PlayerNameMismatch > 0)
        {
            return "Event context overlaps, but player names do not join cleanly. Continue manual identity review or add safer provider player/team context before grading.";
        }

        if (blockers.PropTypeMappingMissing > 0)
        {
            return "Event/player context exists, but one or more market/stat mappings are missing. Update provider stat mapping support before attempting a grading preview.";
        }

        return "No safe event/player/prop join was proven. Review event date/team context and provider event ids before building any Soccer grading preview.";
    }

    private static ApiSportsSoccerGradingPreview BuildApiSportsSoccerGradingPreview(
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        int sampleLimit)
    {
        var computation = BuildApiSportsSoccerGradingComputation(resultRows, platformRows, sampleLimit);

        return new ApiSportsSoccerGradingPreview(
            computation.Summary,
            computation.Blockers,
            new
            {
                graded = computation.GradedSamples,
                unjoinableResults = computation.UnjoinableResultSamples,
                unjoinablePlatformRows = computation.UnjoinablePlatformSamples
            },
            BuildApiSportsSoccerGradingPreviewRecommendation(computation.Summary.GradedPreviewRows, computation.Blockers));
    }

    private static ApiSportsSoccerGradingCoverageSummary BuildApiSportsSoccerGradingCoverageSummary(
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        bool includeFixtureDetails,
        bool includeSamples,
        int maxFixtures,
        int maxSamplesPerFixture)
    {
        var resolvedDetails = new List<ApiSportsSoccerGradingCoverageFixtureDetail>();
        var resultGroups = resultRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ApiSportsFixtureId))
            .GroupBy(row => row.ApiSportsFixtureId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var platformGroups = platformRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderEventId))
            .GroupBy(row => row.ProviderEventId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var mappingCoverage = BuildApiSportsSoccerFixtureMappingCoverage(resultRows, platformRows, true, true, false, int.MaxValue);

        foreach (var mapping in mappingCoverage.ResolvedEvents)
        {
            if (!string.IsNullOrWhiteSpace(mapping.ResolvedFixtureId)
                && !string.IsNullOrWhiteSpace(mapping.ProviderEventId)
                && resultGroups.TryGetValue(mapping.ResolvedFixtureId, out var fixtureResults)
                && platformGroups.TryGetValue(mapping.ProviderEventId, out var eventPlatforms))
            {
                resolvedDetails.Add(BuildApiSportsSoccerGradingCoverageFixtureDetail(
                    mapping.ResolvedFixtureId,
                    mapping.ProviderEventId,
                    fixtureResults,
                    eventPlatforms,
                    includeSamples,
                    maxSamplesPerFixture));
            }
        }

        var resultOnlyFixtures = resultGroups
            .Where(pair => !mappingCoverage.ResolvedEvents.Any(mapping => string.Equals(mapping.ResolvedFixtureId, pair.Key, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(pair => pair.Value.Min(row => row.EventDate))
            .ThenBy(pair => pair.Key)
            .Select(pair => BuildApiSportsSoccerGradingCoverageResultOnlyFixture(pair.Key, pair.Value))
            .ToArray();
        var platformOnlyEvents = mappingCoverage.UnresolvedEvents
            .Concat(mappingCoverage.ExcludedEvents)
            .Select(row => new ApiSportsSoccerGradingCoverageUnresolvedEvent(
                row.ResolvedFixtureId,
                row.ProviderEventId,
                row.EventName,
                row.EventDate,
                row.HomeTeam,
                row.AwayTeam,
                row.PlatformRowCount,
                row.UniquePlatformPlayers,
                row.ResolutionStatus == "Excluded"
                    ? row.Blockers.FirstOrDefault() ?? row.ResolutionStatus
                    : row.ResolutionStatus))
            .OrderBy(row => row.EventDate)
            .ThenBy(row => row.ProviderEventId)
            .ToArray();

        var limitedDetails = resolvedDetails
            .OrderBy(detail => detail.EventDate)
            .ThenBy(detail => detail.EventName)
            .ThenBy(detail => detail.FixtureId)
            .ThenBy(detail => detail.ProviderEventId)
            .Take(maxFixtures)
            .ToArray();
        var aggregatedBlockers = new ApiSportsSoccerGradingPreviewBlockers(
            limitedDetails.Sum(detail => detail.TopBlockers.MissingApiSportsFixtureIdOnMarketRows),
            limitedDetails.Sum(detail => detail.TopBlockers.MissingProviderPlayerId),
            limitedDetails.Sum(detail => detail.TopBlockers.MissingPlayerTeam),
            limitedDetails.Sum(detail => detail.TopBlockers.PlayerNameMismatch),
            limitedDetails.Sum(detail => detail.TopBlockers.PropTypeMappingMissing),
            limitedDetails.Sum(detail => detail.TopBlockers.EventDateMismatch),
            limitedDetails.Sum(detail => detail.TopBlockers.NoLineRows),
            limitedDetails.Sum(detail => detail.TopBlockers.UnjoinableRows));
        var totalCandidatePlatformRows = limitedDetails.Sum(detail => detail.CandidatePlatformRows);
        var totalGradedPreviewRows = limitedDetails.Sum(detail => detail.GradedPreviewRows);
        var summary = new ApiSportsSoccerGradingCoverageTopLevelSummary(
            limitedDetails.Length,
            mappingCoverage.UnresolvedEvents.Count,
            resultOnlyFixtures.Length,
            platformOnlyEvents.Length,
            limitedDetails.Count(detail => detail.GradedPreviewRows > 0),
            totalCandidatePlatformRows,
            limitedDetails.Sum(detail => detail.ImportedResultRows),
            limitedDetails.Sum(detail => detail.FullyJoinableRowsAfterAlias),
            totalGradedPreviewRows,
            limitedDetails.Sum(detail => detail.NoLineRows),
            limitedDetails.Sum(detail => detail.UngradedRows),
            totalCandidatePlatformRows == 0 ? 0 : decimal.Round((decimal)totalGradedPreviewRows / totalCandidatePlatformRows, 4));

        var fixtureSummaries = limitedDetails
            .Where(detail => detail.CandidatePlatformRows > 0 || detail.ImportedResultRows > 0)
            .Select(detail => new ApiSportsSoccerGradingCoverageFixtureRank(
                detail.FixtureId,
                detail.ProviderEventId,
                detail.EventName,
                detail.EventDate,
                detail.CandidatePlatformRows,
                detail.ImportedResultRows,
                detail.GradedPreviewRows,
                detail.GradeCoverageRate))
            .ToArray();
        var best = fixtureSummaries
            .Where(detail => detail.CandidatePlatformRows > 0)
            .OrderByDescending(detail => detail.GradeCoverageRate)
            .ThenByDescending(detail => detail.GradedPreviewRows)
            .ThenBy(detail => detail.EventName)
            .Take(5)
            .ToArray();
        var worst = fixtureSummaries
            .Where(detail => detail.CandidatePlatformRows > 0)
            .OrderBy(detail => detail.GradeCoverageRate)
            .ThenByDescending(detail => detail.CandidatePlatformRows)
            .ThenBy(detail => detail.EventName)
            .Take(5)
            .ToArray();

        return new ApiSportsSoccerGradingCoverageSummary(
            summary,
            aggregatedBlockers,
            best,
            worst,
            includeFixtureDetails ? limitedDetails : [],
            mappingCoverage.UnresolvedEvents
                .Select(row => new ApiSportsSoccerGradingCoverageUnresolvedEvent(
                    row.ResolvedFixtureId,
                    row.ProviderEventId,
                    row.EventName,
                    row.EventDate,
                    row.HomeTeam,
                    row.AwayTeam,
                    row.PlatformRowCount,
                    row.UniquePlatformPlayers,
                    row.ResolutionStatus))
                .Take(maxFixtures)
                .ToArray(),
            resultOnlyFixtures.Take(maxFixtures).ToArray(),
            platformOnlyEvents.Take(maxFixtures).ToArray(),
            mappingCoverage.KnownWorkingPairDetected,
            BuildApiSportsSoccerGradingCoverageRecommendation(summary, aggregatedBlockers));
    }

    private static ApiSportsSoccerPropMappingGapDiagnostic BuildApiSportsSoccerPropMappingGapDiagnostic(
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        IReadOnlyList<ApiSportsProviderStatMappingDiagnosticRow> apiSportsMappings,
        bool includeSamples,
        int sampleLimit)
    {
        var mappingCoverage = BuildApiSportsSoccerFixtureMappingCoverage(resultRows, platformRows, true, false, false, int.MaxValue);
        var resultGroups = resultRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ApiSportsFixtureId))
            .GroupBy(row => row.ApiSportsFixtureId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var platformGroups = platformRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderEventId))
            .GroupBy(row => row.ProviderEventId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var mappingsByStatKey = apiSportsMappings
            .GroupBy(mapping => mapping.CanonicalStatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var platformGapRows = new List<ApiSportsSoccerPlatformMappingGapRow>();
        var resultGapRows = new List<ApiSportsSoccerResultMappingGapRow>();
        var resolvedPairsEvaluated = 0;

        foreach (var mapping in mappingCoverage.ResolvedEvents)
        {
            if (string.IsNullOrWhiteSpace(mapping.ResolvedFixtureId)
                || string.IsNullOrWhiteSpace(mapping.ProviderEventId)
                || !resultGroups.TryGetValue(mapping.ResolvedFixtureId, out var fixtureResults)
                || !platformGroups.TryGetValue(mapping.ProviderEventId, out var eventPlatforms))
            {
                continue;
            }

            resolvedPairsEvaluated++;
            foreach (var platform in eventPlatforms)
            {
                var playerMatchedResults = fixtureResults
                    .Where(result => IsApiSportsMarketJoinPlayerMatch(result, platform))
                    .ToArray();
                var isFullyJoinable = playerMatchedResults
                    .Any(result => ApiSportsSoccerStatKeysMatch(result.StatKey, platform.CanonicalStatKey));
                if (isFullyJoinable)
                {
                    continue;
                }

                var category = string.IsNullOrWhiteSpace(platform.CanonicalStatKey)
                    ? "TheOddsMarketKeyMissingCanonicalMapping"
                    : playerMatchedResults.Length > 0
                        ? "PlayerMatchedButStatMissing"
                        : fixtureResults.Any(result => ApiSportsSoccerStatKeysMatch(result.StatKey, platform.CanonicalStatKey))
                            ? "Other"
                            : "CanonicalKeyMismatch";
                var suggested = BuildApiSportsSoccerPlatformSuggestedCanonicalStatKey(platform);
                var recommendation = BuildApiSportsSoccerPlatformMappingRecommendation(category, suggested);
                platformGapRows.Add(new ApiSportsSoccerPlatformMappingGapRow(
                    mapping.ResolvedFixtureId,
                    mapping.ProviderEventId,
                    platform.PlatformPropId,
                    platform.PlayerName,
                    platform.PropTypeName,
                    platform.MarketKey,
                    platform.CanonicalStatKey,
                    platform.EventName,
                    platform.EventDate,
                    platform.Bookmaker,
                    platform.Provider,
                    category,
                    recommendation,
                    BuildApiSportsSoccerPlatformMappingRisk(category, suggested),
                    suggested));
            }

            foreach (var result in fixtureResults)
            {
                var playerMatchedPlatforms = eventPlatforms
                    .Where(platform => IsApiSportsMarketJoinPlayerMatch(result, platform))
                    .ToArray();
                if (playerMatchedPlatforms.Length == 0
                    || playerMatchedPlatforms.Any(platform => ApiSportsSoccerStatKeysMatch(result.StatKey, platform.CanonicalStatKey)))
                {
                    continue;
                }

                var mappingExists = mappingsByStatKey.TryGetValue(result.StatKey, out var statMapping);
                var category = !mappingExists
                    ? "ApiSportsStatKeyMissingProviderMapping"
                    : !statMapping!.IsGradeable
                        ? "MappingExistsButNotGradeable"
                        : playerMatchedPlatforms.Any(platform => string.IsNullOrWhiteSpace(platform.CanonicalStatKey))
                            ? "CanonicalKeyMismatch"
                            : "ImportedStatNotRequestedByTheOdds";
                var recommendation = BuildApiSportsSoccerResultMappingRecommendation(category);
                resultGapRows.Add(new ApiSportsSoccerResultMappingGapRow(
                    result.ApiSportsFixtureId,
                    result.ResultId,
                    result.PlayerName,
                    result.TeamName,
                    result.PropTypeName,
                    result.StatKey,
                    result.EventDate,
                    statMapping?.RuleConfidence,
                    statMapping?.IsGradeable,
                    category,
                    recommendation,
                    BuildApiSportsSoccerResultMappingRisk(category),
                    result.ActualResultValue));
            }
        }

        var theOddsMarketGaps = platformGapRows
            .GroupBy(row => new
            {
                ProviderMarketKey = row.ProviderMarketKey ?? string.Empty,
                row.PlatformPropType,
                CanonicalMarketStatKey = row.CanonicalMarketStatKey ?? string.Empty,
                row.Category
            })
            .Select(group =>
            {
                var first = group.First();
                return new ApiSportsSoccerTheOddsMarketGap(
                    string.IsNullOrWhiteSpace(first.ProviderMarketKey) ? null : first.ProviderMarketKey,
                    first.PlatformPropType,
                    string.IsNullOrWhiteSpace(first.CanonicalMarketStatKey) ? null : first.CanonicalMarketStatKey,
                    group.Count(),
                    group.Select(row => row.PlayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    group.Select(row => row.ProviderEventId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    first.Category,
                    !string.IsNullOrWhiteSpace(first.CanonicalMarketStatKey),
                    first.CanFixWithProviderStatMapping,
                    first.SuggestedCanonicalStatKey,
                    first.RiskLevel,
                    first.Recommendation,
                    includeSamples ? group.Take(sampleLimit).ToArray() : []);
            })
            .OrderByDescending(row => row.RowCount)
            .ThenBy(row => row.ProviderMarketKey)
            .ThenBy(row => row.PlatformPropType)
            .ToArray();
        var apiSportsStatGaps = resultGapRows
            .GroupBy(row => new
            {
                row.ApiSportsPropType,
                row.ApiSportsStatKey,
                row.Category
            })
            .Select(group =>
            {
                var first = group.First();
                return new ApiSportsSoccerApiSportsStatGap(
                    first.ApiSportsPropType,
                    first.ApiSportsStatKey,
                    first.RuleConfidence,
                    first.IsGradeable,
                    group.Count(),
                    group.Select(row => row.PlayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    group.Select(row => row.FixtureId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    first.Category,
                    first.CanFixWithProviderStatMapping,
                    first.SuggestedCanonicalStatKey,
                    first.RiskLevel,
                    first.Recommendation,
                    includeSamples ? group.Take(sampleLimit).ToArray() : []);
            })
            .OrderByDescending(row => row.RowCount)
            .ThenBy(row => row.ApiSportsStatKey)
            .ToArray();
        var gapsByCategory = platformGapRows
            .Select(row => row.Category)
            .Concat(resultGapRows.Select(row => row.Category))
            .GroupBy(category => category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { category = group.Key, rows = group.Count() })
            .OrderByDescending(row => row.rows)
            .ThenBy(row => row.category)
            .ToArray();
        var gapsByProviderMarketKey = platformGapRows
            .GroupBy(row => row.ProviderMarketKey ?? "(missing)", StringComparer.OrdinalIgnoreCase)
            .Select(group => new { providerMarketKey = group.Key, rows = group.Count() })
            .OrderByDescending(row => row.rows)
            .ThenBy(row => row.providerMarketKey)
            .ToArray();
        var gapsByApiSportsStatKey = resultGapRows
            .GroupBy(row => row.ApiSportsStatKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { apiSportsStatKey = group.Key, rows = group.Count() })
            .OrderByDescending(row => row.rows)
            .ThenBy(row => row.apiSportsStatKey)
            .ToArray();
        var lowRiskMappingCandidates = theOddsMarketGaps
            .Where(row => row.RiskLevel == "Low" && row.CanFixWithProviderStatMapping)
            .Cast<object>()
            .Concat(apiSportsStatGaps.Where(row => row.RiskLevel == "Low" && row.CanFixWithProviderStatMapping))
            .Take(25)
            .ToArray();
        var needsReviewMappingCandidates = theOddsMarketGaps
            .Where(row => row.Recommendation is "AddNeedsConfirmationMapping" or "NeedsManualReview")
            .Cast<object>()
            .Concat(apiSportsStatGaps.Where(row => row.Recommendation is "AddNeedsConfirmationMapping" or "NeedsManualReview"))
            .Take(25)
            .ToArray();
        var ignoredNoise = apiSportsStatGaps
            .Where(row => row.Recommendation == "IgnoreImportedStatNoise")
            .ToArray();
        var propTypeMappingMissingRows = platformGapRows.Count(row => row.Category is "TheOddsMarketKeyMissingCanonicalMapping" or "CanonicalKeyMismatch")
            + resultGapRows.Count(row => row.Category is "ApiSportsStatKeyMissingProviderMapping" or "MappingExistsButNotGradeable");
        var totalUngradedRows = platformGapRows.Count + resultGapRows.Count;
        var summary = new ApiSportsSoccerPropMappingGapSummary(
            resolvedPairsEvaluated,
            totalUngradedRows,
            propTypeMappingMissingRows,
            gapsByCategory,
            gapsByProviderMarketKey,
            gapsByApiSportsStatKey,
            lowRiskMappingCandidates.Length,
            needsReviewMappingCandidates.Length,
            ignoredNoise.Sum(row => row.RowCount),
            BuildApiSportsSoccerPropMappingGapRecommendation(propTypeMappingMissingRows, lowRiskMappingCandidates.Length, needsReviewMappingCandidates.Length, ignoredNoise.Sum(row => row.RowCount)));

        return new ApiSportsSoccerPropMappingGapDiagnostic(
            summary,
            theOddsMarketGaps,
            apiSportsStatGaps,
            gapsByCategory,
            gapsByProviderMarketKey,
            gapsByApiSportsStatKey,
            lowRiskMappingCandidates,
            needsReviewMappingCandidates,
            ignoredNoise,
            summary.RecommendedNextAction);
    }

    private static string? BuildApiSportsSoccerPlatformSuggestedCanonicalStatKey(ApiSportsMarketJoinPlatformRow platform)
    {
        return FirstNonBlank(platform.CanonicalStatKey, NormalizeApiSportsResultStatKey(platform.StatKey, platform.PropTypeName));
    }

    private static string BuildApiSportsSoccerPlatformMappingRecommendation(string category, string? suggestedCanonicalStatKey)
    {
        return category switch
        {
            "TheOddsMarketKeyMissingCanonicalMapping" when !string.IsNullOrWhiteSpace(suggestedCanonicalStatKey) => "AddKnownMapping",
            "TheOddsMarketKeyMissingCanonicalMapping" => "NeedsManualReview",
            "PlayerMatchedButStatMissing" => "IgnoreImportedStatNoise",
            "CanonicalKeyMismatch" => "NeedsManualReview",
            _ => "NeedsManualReview"
        };
    }

    private static string BuildApiSportsSoccerPlatformMappingRisk(string category, string? suggestedCanonicalStatKey)
    {
        return category switch
        {
            "TheOddsMarketKeyMissingCanonicalMapping" when !string.IsNullOrWhiteSpace(suggestedCanonicalStatKey) => "Low",
            "PlayerMatchedButStatMissing" => "Medium",
            "CanonicalKeyMismatch" => "High",
            _ => "High"
        };
    }

    private static string BuildApiSportsSoccerResultMappingRecommendation(string category)
    {
        return category switch
        {
            "ApiSportsStatKeyMissingProviderMapping" => "AddNeedsConfirmationMapping",
            "MappingExistsButNotGradeable" => "NeedsManualReview",
            "ImportedStatNotRequestedByTheOdds" => "IgnoreImportedStatNoise",
            "CanonicalKeyMismatch" => "NeedsManualReview",
            _ => "NeedsManualReview"
        };
    }

    private static string BuildApiSportsSoccerResultMappingRisk(string category)
    {
        return category switch
        {
            "ImportedStatNotRequestedByTheOdds" => "Low",
            "ApiSportsStatKeyMissingProviderMapping" => "Medium",
            "MappingExistsButNotGradeable" => "High",
            _ => "High"
        };
    }

    private static string BuildApiSportsSoccerPropMappingGapRecommendation(
        int propTypeMappingMissingRows,
        int lowRiskMappingCandidates,
        int needsReviewMappingCandidates,
        int ignoredNoiseRows)
    {
        if (propTypeMappingMissingRows == 0 && ignoredNoiseRows > 0)
        {
            return "Remaining unmapped rows look like imported stat noise outside requested The Odds markets.";
        }

        if (lowRiskMappingCandidates > 0)
        {
            return "Review low-risk provider stat mapping candidates before adding any Soccer grading workflow.";
        }

        if (needsReviewMappingCandidates > 0)
        {
            return "Review provider stat mapping candidates manually; do not enable public Soccer grading yet.";
        }

        return propTypeMappingMissingRows == 0
            ? "No prop/stat mapping gaps were detected for resolved Soccer fixture pairs."
            : "Mapping gaps remain but require manual review before any Soccer grading expansion.";
    }

    private static ApiSportsSoccerStatMappingReview BuildApiSportsSoccerStatMappingReview(
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        IReadOnlyList<ApiSportsProviderStatMappingDiagnosticRow> apiSportsMappings,
        ApiSportsSoccerPropMappingGapDiagnostic propGap,
        bool includeSamples,
        int sampleLimit)
    {
        var resultRowsByStatKey = resultRows
            .GroupBy(row => row.StatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var platformRowsByCanonicalKey = platformRows
            .Where(row => !string.IsNullOrWhiteSpace(row.CanonicalStatKey))
            .GroupBy(row => row.CanonicalStatKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var gapRowsByStatKey = propGap.ApiSportsStatGaps
            .GroupBy(row => row.ApiSportsStatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var importedNoiseByStatKey = propGap.IgnoredNoise
            .GroupBy(row => row.ApiSportsStatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var reviewMappings = apiSportsMappings
            .Where(mapping => !mapping.IsGradeable
                || !string.Equals(mapping.RuleConfidence, "Known", StringComparison.OrdinalIgnoreCase))
            .Select(mapping =>
            {
                resultRowsByStatKey.TryGetValue(mapping.CanonicalStatKey, out var matchingResultRows);
                platformRowsByCanonicalKey.TryGetValue(mapping.CanonicalStatKey, out var matchingPlatformRows);
                gapRowsByStatKey.TryGetValue(mapping.CanonicalStatKey, out var statGaps);
                importedNoiseByStatKey.TryGetValue(mapping.CanonicalStatKey, out var importedNoiseRows);
                matchingResultRows ??= [];
                matchingPlatformRows ??= [];
                statGaps ??= [];
                importedNoiseRows ??= [];
                var nonGradeableGapRows = statGaps
                    .Where(row => row.Category == "MappingExistsButNotGradeable")
                    .ToArray();
                var affectedRows = nonGradeableGapRows.Sum(row => row.RowCount);
                var requestedByTheOddsRows = matchingPlatformRows.Length;
                var importedNoiseRowsCount = importedNoiseRows.Sum(row => row.RowCount);
                var recommendation = BuildApiSportsSoccerStatMappingReviewRecommendation(mapping, affectedRows, requestedByTheOddsRows, importedNoiseRowsCount);
                var risk = BuildApiSportsSoccerStatMappingReviewRisk(mapping, affectedRows, requestedByTheOddsRows);
                return new ApiSportsSoccerStatMappingReviewRow(
                    mapping.DisplayName,
                    mapping.CanonicalStatKey,
                    mapping.ProviderStatFields,
                    mapping.RuleConfidence,
                    mapping.IsGradeable,
                    mapping.PublicGradingEnabled,
                    affectedRows,
                    matchingResultRows.Select(row => row.PlayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    matchingResultRows.Select(row => row.ApiSportsFixtureId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    requestedByTheOddsRows,
                    importedNoiseRowsCount,
                    includeSamples
                        ? nonGradeableGapRows.SelectMany(row => row.SampleRows).Take(sampleLimit).ToArray()
                        : [],
                    mapping.Notes,
                    BuildApiSportsSoccerStatMappingNonGradeableReason(mapping, affectedRows, requestedByTheOddsRows, importedNoiseRowsCount),
                    risk,
                    recommendation,
                    PublicGradingEligible: false,
                    DebugPreviewEligibleIfApproved: recommendation == "ManualApprovalCandidate");
            })
            .OrderByDescending(row => row.RowCountAffected)
            .ThenBy(row => row.PropType)
            .ToArray();
        var importedNoise = propGap.IgnoredNoise
            .Select(row => new ApiSportsSoccerImportedStatNoiseReviewRow(
                row.ApiSportsPropType,
                row.ApiSportsStatKey,
                row.RowCount,
                row.DistinctPlayers,
                row.DistinctFixtures,
                "Imported API-Sports stat exists for matched players, but The Odds did not request or return this market for the platform rows in scope.",
                "IgnoreAsImportedNoise"))
            .OrderByDescending(row => row.RowCount)
            .ThenBy(row => row.ApiSportsStatKey)
            .ToArray();
        var summary = new ApiSportsSoccerStatMappingReviewSummary(
            reviewMappings.Length,
            reviewMappings.Count(row => row.RuleConfidence == "NeedsConfirmation"),
            reviewMappings.Count(row => row.RuleConfidence == "Unsupported"),
            reviewMappings.Count(row => !row.IsGradeable),
            reviewMappings.Sum(row => row.RowCountAffected),
            reviewMappings.Count(row => row.Recommendation == "ManualApprovalCandidate"),
            reviewMappings.Count(row => row.Recommendation == "KeepNonGradeable"),
            reviewMappings.Count(row => row.Recommendation == "NeedsMoreProviderEvidence"),
            importedNoise.Sum(row => row.RowCount),
            false);

        return new ApiSportsSoccerStatMappingReview(
            summary,
            reviewMappings,
            importedNoise,
            BuildApiSportsSoccerStatMappingReviewNextAction(summary));
    }

    private static string BuildApiSportsSoccerStatMappingReviewRecommendation(
        ApiSportsProviderStatMappingDiagnosticRow mapping,
        int affectedRows,
        int requestedByTheOddsRows,
        int importedNoiseRows)
    {
        if (string.Equals(mapping.RuleConfidence, "Unsupported", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapping.ValueTransform, "Unsupported", StringComparison.OrdinalIgnoreCase))
        {
            return "KeepNonGradeable";
        }

        if (requestedByTheOddsRows == 0 && importedNoiseRows > 0)
        {
            return "IgnoreAsImportedNoise";
        }

        if (affectedRows > 0 && requestedByTheOddsRows > 0)
        {
            return "ManualApprovalCandidate";
        }

        return "NeedsMoreProviderEvidence";
    }

    private static string BuildApiSportsSoccerStatMappingReviewRisk(
        ApiSportsProviderStatMappingDiagnosticRow mapping,
        int affectedRows,
        int requestedByTheOddsRows)
    {
        if (string.Equals(mapping.RuleConfidence, "Unsupported", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapping.ValueTransform, "Unsupported", StringComparison.OrdinalIgnoreCase))
        {
            return "High";
        }

        if (affectedRows > 0 && requestedByTheOddsRows > 0)
        {
            return "Medium";
        }

        return "Low";
    }

    private static string BuildApiSportsSoccerStatMappingNonGradeableReason(
        ApiSportsProviderStatMappingDiagnosticRow mapping,
        int affectedRows,
        int requestedByTheOddsRows,
        int importedNoiseRows)
    {
        if (string.Equals(mapping.RuleConfidence, "Unsupported", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapping.ValueTransform, "Unsupported", StringComparison.OrdinalIgnoreCase))
        {
            return "Mapping is marked unsupported and should remain non-gradeable without new provider evidence.";
        }

        if (affectedRows > 0 && requestedByTheOddsRows > 0)
        {
            return "Mapping exists but is marked NeedsConfirmation/non-gradeable while The Odds returned matching platform markets in scope.";
        }

        if (requestedByTheOddsRows == 0 && importedNoiseRows > 0)
        {
            return "API-Sports imported this stat for matched players, but The Odds did not return the corresponding market in scope.";
        }

        return "Mapping has not been proven against returned The Odds markets in the selected range.";
    }

    private static string BuildApiSportsSoccerStatMappingReviewNextAction(ApiSportsSoccerStatMappingReviewSummary summary)
    {
        if (summary.ManualApprovalCandidates > 0)
        {
            return "Review manual approval candidates one at a time; public Soccer grading remains disabled.";
        }

        if (summary.NeedsMoreProviderEvidence > 0)
        {
            return "Collect more provider evidence before approving any NeedsConfirmation Soccer stat mappings.";
        }

        return "Keep current non-gradeable Soccer mappings unchanged.";
    }

    private static IReadOnlyList<object> BuildSoccerWorldCupSpikeStatusBlockers(
        ApiSportsSoccerGradingCoverageTopLevelSummary gradingSummary,
        ApiSportsSoccerPropMappingGapSummary propGapSummary,
        int unresolvedAliasCandidates,
        int nonGradeableMappingRows)
    {
        var blockers = new List<object>
        {
            new
            {
                area = "ProductionGate",
                severity = "High",
                description = "Public Soccer grading is disabled and must remain off until explicitly promoted.",
                requiredBeforePublicGrading = true
            },
            new
            {
                area = "EntryBuilder",
                severity = "High",
                description = "Soccer sportsbook context is research-only and cannot be used as Entry Builder legs.",
                requiredBeforePublicGrading = false
            }
        };

        if (propGapSummary.PropTypeMappingMissingRows > 0 || nonGradeableMappingRows > 0)
        {
            blockers.Add(new
            {
                area = "ProviderStatMapping",
                severity = "Medium",
                description = $"{propGapSummary.PropTypeMappingMissingRows} rows still require prop/stat mapping review; {nonGradeableMappingRows} rows use mappings that exist but are not gradeable.",
                requiredBeforePublicGrading = true
            });
        }

        if (unresolvedAliasCandidates > 0)
        {
            blockers.Add(new
            {
                area = "ProviderIdentity",
                severity = "Medium",
                description = $"{unresolvedAliasCandidates} Soccer alias candidates remain unresolved or require manual review.",
                requiredBeforePublicGrading = true
            });
        }

        if (gradingSummary.OverallGradeCoverageRate < 0.95m)
        {
            blockers.Add(new
            {
                area = "Coverage",
                severity = "Medium",
                description = $"Debug grading preview coverage is {gradingSummary.OverallGradeCoverageRate:P2}; public grading needs broader reviewed coverage.",
                requiredBeforePublicGrading = true
            });
        }

        return blockers;
    }

    private static string BuildSoccerWorldCupSpikeStatusRecommendation(
        IReadOnlyList<object> blockers,
        string propMappingRecommendation,
        string gradingRecommendation)
    {
        if (blockers.Count > 0 && !string.IsNullOrWhiteSpace(propMappingRecommendation))
        {
            return propMappingRecommendation;
        }

        return !string.IsNullOrWhiteSpace(gradingRecommendation)
            ? gradingRecommendation
            : "Keep Soccer World Cup research-only until public grading, entry actionability, aliases, and provider stat mappings are explicitly approved.";
    }

    private static bool IsRealTheOddsSoccerWorldCupProviderEvent(IReadOnlyList<ApiSportsMarketJoinPlatformRow> rows)
    {
        return rows.Any(row =>
            string.Equals(row.DataProvider, "The Odds API", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.ContextSchemaVersion, "the-odds-provider-context-v2", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.ProviderSportKey, "soccer_fifa_world_cup", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.Competition, "World Cup", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(row.ProviderEventId)
            && !IsSyntheticTheOddsSoccerProviderEventId(row.ProviderEventId));
    }

    private static bool IsSyntheticTheOddsSoccerProviderEventId(string? providerEventId)
    {
        return string.IsNullOrWhiteSpace(providerEventId)
            || providerEventId.Contains('|', StringComparison.Ordinal)
            || providerEventId.Contains("SCR_game", StringComparison.OrdinalIgnoreCase);
    }

    private static ApiSportsSoccerFixtureMappingCoverage BuildApiSportsSoccerFixtureMappingCoverage(
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        bool includeResolved,
        bool includeUnresolved,
        bool includeSamples,
        int maxSamples)
    {
        var resultGroups = resultRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ApiSportsFixtureId))
            .GroupBy(row => row.ApiSportsFixtureId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildApiSportsFixtureMappingResultFixture(group.Key, group.ToArray()))
            .ToArray();
        var platformGroups = platformRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderEventId))
            .GroupBy(row => row.ProviderEventId!, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resolved = new List<ApiSportsSoccerFixtureMappingEvent>();
        var unresolved = new List<ApiSportsSoccerFixtureMappingEvent>();
        var excluded = new List<ApiSportsSoccerFixtureMappingEvent>();

        foreach (var platformGroup in platformGroups.OrderBy(group => group.Min(row => row.EventStartTimeUtc)).ThenBy(group => group.Key))
        {
            var platformEventRows = platformGroup.ToArray();
            var eventSummary = BuildApiSportsFixtureMappingProviderEvent(platformGroup.Key, platformEventRows);
            if (!IsRealTheOddsSoccerWorldCupProviderEvent(platformEventRows))
            {
                excluded.Add(BuildApiSportsFixtureMappingEvent(eventSummary, [], null, "Excluded", 0, [], ["NotTheOddsSoccerWorldCupProviderEvent"], includeSamples, maxSamples));
                continue;
            }

            var candidates = resultGroups
                .Select(resultGroup => ScoreApiSportsFixtureCandidate(eventSummary, resultGroup))
                .Where(candidate => candidate.Confidence > 0)
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.FixtureId)
                .ToArray();
            var highConfidence = candidates.Where(candidate => candidate.Confidence >= 0.80m).ToArray();
            ApiSportsSoccerFixtureMappingEvent row;
            if (resultGroups.Length == 0)
            {
                row = BuildApiSportsFixtureMappingEvent(eventSummary, candidates, null, "MissingApiSportsResults", 0, [], ["NoApiSportsResults"], includeSamples, maxSamples);
            }
            else if (highConfidence.Length == 1)
            {
                row = BuildApiSportsFixtureMappingEvent(eventSummary, candidates, highConfidence[0].FixtureId, "Resolved", highConfidence[0].Confidence, highConfidence[0].MatchReasons, highConfidence[0].Blockers, includeSamples, maxSamples);
            }
            else if (highConfidence.Length > 1)
            {
                row = BuildApiSportsFixtureMappingEvent(eventSummary, candidates, null, "Ambiguous", highConfidence[0].Confidence, highConfidence.SelectMany(candidate => candidate.MatchReasons).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), ["MultipleHighConfidenceFixtures"], includeSamples, maxSamples);
            }
            else if (candidates.Length == 0)
            {
                row = BuildApiSportsFixtureMappingEvent(eventSummary, candidates, null, "NoCandidate", 0, [], ["NoFixtureCandidates"], includeSamples, maxSamples);
            }
            else
            {
                var hasDateCandidate = candidates.Any(candidate => candidate.MatchReasons.Contains("DateWithinTolerance", StringComparer.OrdinalIgnoreCase));
                var hasTeamCandidate = candidates.Any(candidate => candidate.MatchReasons.Any(reason => reason is "BothTeamsMatch" or "OneTeamMatch"));
                var status = !hasDateCandidate
                    ? "DateMismatch"
                    : !hasTeamCandidate
                        ? "TeamMismatch"
                        : "NoCandidate";
                var blockers = candidates.Length == 0
                    ? new[] { "NoFixtureCandidates" }
                    : candidates.SelectMany(candidate => candidate.Blockers).DefaultIfEmpty(status).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                row = BuildApiSportsFixtureMappingEvent(eventSummary, candidates, null, status, candidates.FirstOrDefault()?.Confidence ?? 0, candidates.FirstOrDefault()?.MatchReasons ?? [], blockers, includeSamples, maxSamples);
            }

            if (row.ResolutionStatus == "Resolved")
            {
                resolved.Add(row);
            }
            else
            {
                unresolved.Add(row);
            }
        }

        var resolvedPlatformRows = resolved.Sum(row => row.PlatformRowCount);
        var unresolvedPlatformRows = unresolved.Sum(row => row.PlatformRowCount);
        var summary = new ApiSportsSoccerFixtureMappingCoverageSummary(
            resolved.Count + unresolved.Count,
            resolved.Count,
            unresolved.Count,
            unresolved.Count(row => row.ResolutionStatus == "Ambiguous"),
            unresolved.Count(row => row.ResolutionStatus is "NoCandidate" or "DateMismatch" or "TeamMismatch" or "MissingApiSportsResults"),
            resolved.Count + unresolved.Count,
            resultGroups.Length,
            platformGroups.Sum(group => group.Count()),
            resolvedPlatformRows,
            unresolvedPlatformRows);

        return new ApiSportsSoccerFixtureMappingCoverage(
            summary,
            includeResolved ? resolved.Take(maxSamples).ToArray() : [],
            includeUnresolved ? unresolved.Take(maxSamples).ToArray() : [],
            excluded.Take(maxSamples).ToArray(),
            resolved.Any(row => string.Equals(row.ResolvedFixtureId, "1489398", StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.ProviderEventId, "852a3abe6d46820720febc0fedebff9a", StringComparison.OrdinalIgnoreCase)),
            BuildApiSportsSoccerFixtureMappingRecommendation(summary));
    }

    private static ApiSportsSoccerFixtureMappingProviderEvent BuildApiSportsFixtureMappingProviderEvent(
        string providerEventId,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> rows)
    {
        var first = rows.OrderBy(row => row.EventStartTimeUtc).First();
        return new ApiSportsSoccerFixtureMappingProviderEvent(
            providerEventId,
            first.EventName,
            first.EventStartTimeUtc,
            first.EventDate,
            first.HomeTeamName,
            first.AwayTeamName,
            NormalizeSoccerFixtureTeamName(first.HomeTeamName),
            NormalizeSoccerFixtureTeamName(first.AwayTeamName),
            rows.Count,
            rows.Select(row => row.NormalizedPlayerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static ApiSportsSoccerFixtureMappingResultFixture BuildApiSportsFixtureMappingResultFixture(
        string fixtureId,
        IReadOnlyList<ApiSportsMarketJoinResultRow> rows)
    {
        var first = rows.OrderBy(row => row.EventDate).First();
        var teams = rows
            .Select(row => row.TeamName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToArray();
        var normalizedTeams = teams
            .Select(NormalizeSoccerFixtureTeamName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ApiSportsSoccerFixtureMappingResultFixture(
            fixtureId,
            first.EventDate,
            rows.Count,
            rows.Select(row => FirstNonBlank(row.ApiSportsPlayerId, row.NormalizedPlayerName)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            teams,
            normalizedTeams);
    }

    private static ApiSportsSoccerFixtureCandidate ScoreApiSportsFixtureCandidate(
        ApiSportsSoccerFixtureMappingProviderEvent providerEvent,
        ApiSportsSoccerFixtureMappingResultFixture fixture)
    {
        var reasons = new List<string>();
        var blockers = new List<string>();
        var dateDelta = Math.Abs(fixture.EventDate.DayNumber - providerEvent.EventDate.DayNumber);
        decimal confidence = 0;
        if (dateDelta == 0)
        {
            confidence += 0.45m;
            reasons.Add("ExactEventDate");
            reasons.Add("DateWithinTolerance");
        }
        else if (dateDelta <= 1)
        {
            confidence += 0.30m;
            reasons.Add("DateWithinTolerance");
        }
        else
        {
            blockers.Add("DateMismatch");
        }

        var providerTeams = new[] { providerEvent.NormalizedHomeTeam, providerEvent.NormalizedAwayTeam }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matchedTeams = providerTeams.Count(team => fixture.NormalizedTeamNames.Contains(team, StringComparer.OrdinalIgnoreCase));
        if (providerTeams.Length >= 2 && matchedTeams >= 2)
        {
            confidence += 0.55m;
            reasons.Add("BothTeamsMatch");
        }
        else if (matchedTeams == 1)
        {
            confidence += 0.40m;
            reasons.Add("OneTeamMatch");
            if (dateDelta == 0 && fixture.NormalizedTeamNames.Count <= 2)
            {
                confidence += 0.05m;
                reasons.Add("UniqueDateTeamCandidate");
            }
        }
        else
        {
            blockers.Add("TeamMismatch");
        }

        return new ApiSportsSoccerFixtureCandidate(
            fixture.FixtureId,
            fixture.EventDate,
            fixture.TeamNames,
            fixture.ResultRowCount,
            fixture.UniqueApiSportsPlayers,
            Math.Min(1m, confidence),
            reasons,
            blockers);
    }

    private static ApiSportsSoccerFixtureMappingEvent BuildApiSportsFixtureMappingEvent(
        ApiSportsSoccerFixtureMappingProviderEvent providerEvent,
        IReadOnlyList<ApiSportsSoccerFixtureCandidate> candidates,
        string? resolvedFixtureId,
        string status,
        decimal confidence,
        IReadOnlyList<string> matchReasons,
        IReadOnlyList<string> blockers,
        bool includeSamples,
        int maxSamples)
    {
        return new ApiSportsSoccerFixtureMappingEvent(
            providerEvent.ProviderEventId,
            providerEvent.EventName,
            providerEvent.EventStartUtc,
            providerEvent.EventDate,
            providerEvent.HomeTeam,
            providerEvent.AwayTeam,
            providerEvent.PlatformRowCount,
            providerEvent.UniquePlatformPlayers,
            includeSamples ? candidates.Take(maxSamples).ToArray() : [],
            resolvedFixtureId,
            status,
            confidence,
            matchReasons,
            blockers);
    }

    private static string NormalizeSoccerFixtureTeamName(string? value)
    {
        var normalized = NormalizeResultPlayerName(value);
        return normalized switch
        {
            "CAPEVERDEISLANDS" => "CAPEVERDE",
            "UNITEDSTATES" => "USA",
            "CONGODR" => "DRCONGO",
            "COTEDIVOIRE" => "IVORYCOAST",
            "CZECHREPUBLIC" => "CZECHIA",
            _ => normalized
        };
    }

    private static string BuildApiSportsSoccerFixtureMappingRecommendation(ApiSportsSoccerFixtureMappingCoverageSummary summary)
    {
        if (summary.ProviderEventsEvaluated == 0)
        {
            return "No real The Odds Soccer World Cup sportsbook provider events were found in persisted platform rows for this range.";
        }

        if (summary.ResolvedEvents == 0 && summary.UnresolvedEvents > 0)
        {
            return "No The Odds Soccer provider events resolved to API-Sports fixtures. Review unresolvedEvents blockers and recover event/team mapping before expanding grading coverage.";
        }

        if (summary.UnresolvedEvents > summary.ResolvedEvents)
        {
            return "Some provider events resolve, but unresolved event mapping is still the main blocker. Review DateMismatch and TeamMismatch rows before broader Soccer grading diagnostics.";
        }

        return "The Odds provider events are resolving to API-Sports fixtures. Use grading-coverage-summary to inspect player/stat coverage next.";
    }

    private static async Task<TheOddsSoccerWorldCupIngestionCoverage> BuildTheOddsSoccerWorldCupIngestionCoverageAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        ApiSportsSoccerFixtureMappingCoverage mappingCoverage,
        bool includeSamples,
        int sampleLimit,
        CancellationToken cancellationToken)
    {
        var groups = platformRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderEventId))
            .GroupBy(row => row.ProviderEventId!, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var realGroups = groups.Where(group => IsRealTheOddsSoccerWorldCupProviderEvent(group.ToArray())).ToArray();
        var syntheticGroups = groups.Where(group => !IsRealTheOddsSoccerWorldCupProviderEvent(group.ToArray())).ToArray();
        var realEvents = realGroups
            .Select(group => BuildTheOddsSoccerWorldCupRealProviderEvent(group.Key, group.ToArray()))
            .OrderBy(row => row.EventStartUtc)
            .ThenBy(row => row.ProviderEventId)
            .Take(includeSamples ? sampleLimit : 0)
            .ToArray();
        var syntheticSamples = syntheticGroups
            .Select(group => BuildTheOddsSoccerWorldCupSyntheticProviderEvent(group.Key, group.ToArray()))
            .OrderBy(row => row.EventDate)
            .ThenBy(row => row.ProviderEventId)
            .Take(includeSamples ? sampleLimit : 0)
            .ToArray();
        var resultFixtures = resultRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ApiSportsFixtureId))
            .GroupBy(row => row.ApiSportsFixtureId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildTheOddsSoccerWorldCupApiSportsFixtureSample(group.Key, group.ToArray()))
            .OrderBy(row => row.EventDate)
            .ThenBy(row => row.FixtureId)
            .ToArray();
        var resolvedFixtureIds = mappingCoverage.ResolvedEvents
            .Select(row => row.ResolvedFixtureId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var apiSportsFixturesWithResultsButNoRealTheOddsEvent = resultFixtures
            .Where(row => !resolvedFixtureIds.Contains(row.FixtureId))
            .Take(includeSamples ? sampleLimit : 0)
            .ToArray();
        var realTheOddsEventsWithoutApiSportsResults = mappingCoverage.UnresolvedEvents
            .Where(row => row.ResolutionStatus is "MissingApiSportsResults" or "NoCandidate" or "DateMismatch" or "TeamMismatch")
            .Take(includeSamples ? sampleLimit : 0)
            .ToArray();
        var latestRefreshRun = await LoadLatestTheOddsSoccerWorldCupRefreshRunAsync(dbContext, range, cancellationToken);
        var realPlatformRows = realGroups.Sum(group => group.Count());
        var syntheticPlatformRows = syntheticGroups.Sum(group => group.Count());
        var latestRefreshEventCount = ExtractJsonInt(latestRefreshRun?.Diagnostics, "eventsFetched")
            ?? ExtractJsonInt(latestRefreshRun?.Diagnostics, "oddsEventsFetched")
            ?? latestRefreshRun?.LineShoppingGroupCount;
        var likelyReason = BuildTheOddsSoccerWorldCupIngestionLikelyReason(
            realGroups.Length,
            syntheticPlatformRows,
            latestRefreshRun,
            resultFixtures.Length,
            mappingCoverage.Summary.ResolvedEvents);
        var summary = new TheOddsSoccerWorldCupIngestionCoverageSummary(
            realGroups.Length,
            realPlatformRows,
            syntheticPlatformRows,
            resultFixtures.Length,
            mappingCoverage.Summary.ResolvedEvents,
            mappingCoverage.Summary.UnresolvedEvents,
            latestRefreshRun?.Id,
            latestRefreshRun?.Succeeded,
            latestRefreshEventCount,
            latestRefreshRun?.PlatformPropsUpserted);
        var gapAnalysis = new TheOddsSoccerWorldCupIngestionGapAnalysis(
            resultFixtures.Count(row => !resolvedFixtureIds.Contains(row.FixtureId)),
            mappingCoverage.UnresolvedEvents.Count(row => !string.IsNullOrWhiteSpace(row.ProviderEventId)),
            mappingCoverage.Summary.ResolvedEvents,
            mappingCoverage.Summary.UnresolvedEvents,
            likelyReason,
            apiSportsFixturesWithResultsButNoRealTheOddsEvent,
            realTheOddsEventsWithoutApiSportsResults);

        return new TheOddsSoccerWorldCupIngestionCoverage(
            summary,
            realEvents,
            new TheOddsSoccerWorldCupSyntheticCoverage(syntheticPlatformRows, syntheticSamples),
            new TheOddsSoccerWorldCupApiSportsResultCoverage(
                resultFixtures.Length,
                resultFixtures.Length,
                range.FromDate,
                range.ToDate,
                resultFixtures.Take(includeSamples ? sampleLimit : 0).ToArray()),
            latestRefreshRun,
            gapAnalysis,
            BuildTheOddsSoccerWorldCupIngestionRecommendation(summary, gapAnalysis));
    }

    private static TheOddsSoccerWorldCupRealProviderEvent BuildTheOddsSoccerWorldCupRealProviderEvent(
        string providerEventId,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> rows)
    {
        var first = rows.OrderBy(row => row.EventStartTimeUtc).First();
        return new TheOddsSoccerWorldCupRealProviderEvent(
            providerEventId,
            first.EventName,
            first.EventStartTimeUtc,
            first.EventDate,
            first.HomeTeamName,
            first.AwayTeamName,
            rows.Count,
            rows.Select(row => row.NormalizedPlayerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            rows.Select(row => FirstNonBlank(row.Bookmaker, row.Provider)).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            rows.Select(row => FirstNonBlank(row.MarketKey, row.PropTypeName)).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            first.ProviderCategory,
            first.ContextSchemaVersion,
            first.ProviderSportKey,
            first.Competition);
    }

    private static TheOddsSoccerWorldCupSyntheticProviderEvent BuildTheOddsSoccerWorldCupSyntheticProviderEvent(
        string providerEventId,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> rows)
    {
        var first = rows.OrderBy(row => row.EventStartTimeUtc).First();
        return new TheOddsSoccerWorldCupSyntheticProviderEvent(
            providerEventId,
            first.EventDate,
            rows.Count,
            BuildTheOddsSoccerWorldCupExclusionReasons(rows));
    }

    private static IReadOnlyList<string> BuildTheOddsSoccerWorldCupExclusionReasons(IReadOnlyList<ApiSportsMarketJoinPlatformRow> rows)
    {
        var first = rows.OrderBy(row => row.EventStartTimeUtc).FirstOrDefault();
        if (first is null)
        {
            return ["EmptyProviderEventGroup"];
        }

        var reasons = new List<string>();
        if (!string.Equals(first.DataProvider, "The Odds API", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("DataProviderNotTheOddsApi");
        }

        if (!string.Equals(first.ContextSchemaVersion, "the-odds-provider-context-v2", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("MissingContextSchemaV2");
        }

        if (!string.Equals(first.ProviderSportKey, "soccer_fifa_world_cup", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("ProviderSportKeyMismatch");
        }

        if (!string.Equals(first.Competition, "World Cup", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("CompetitionMismatch");
        }

        if (!string.Equals(first.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("ProviderCategoryNotSportsbook");
        }

        if (IsSyntheticTheOddsSoccerProviderEventId(first.ProviderEventId))
        {
            reasons.Add("SyntheticProviderEventId");
        }

        return reasons.Count == 0 ? ["UnknownLegacyOrSyntheticRow"] : reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static TheOddsSoccerWorldCupApiSportsFixtureSample BuildTheOddsSoccerWorldCupApiSportsFixtureSample(
        string fixtureId,
        IReadOnlyList<ApiSportsMarketJoinResultRow> rows)
    {
        var first = rows.OrderBy(row => row.EventDate).First();
        return new TheOddsSoccerWorldCupApiSportsFixtureSample(
            fixtureId,
            first.EventDate,
            rows.Count,
            rows.Select(row => FirstNonBlank(row.ApiSportsPlayerId, row.NormalizedPlayerName)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            rows.Select(row => row.TeamName).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
    }

    private static async Task<TheOddsSoccerWorldCupRefreshRun?> LoadLatestTheOddsSoccerWorldCupRefreshRunAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(refresh => refresh.Provider == "The Odds API"
                && refresh.Sport == "SOCCER"
                && refresh.SlateDate >= range.FromDate
                && refresh.SlateDate <= range.ToDate)
            .OrderByDescending(refresh => refresh.CompletedAtUtc ?? refresh.StartedAtUtc)
            .Select(refresh => new
            {
                refresh.Id,
                refresh.Sport,
                refresh.SlateDate,
                refresh.StartedAtUtc,
                refresh.CompletedAtUtc,
                refresh.Succeeded,
                refresh.Failed,
                refresh.Blocked,
                refresh.DryRun,
                refresh.ForceLive,
                refresh.BookmakerPreset,
                refresh.ProviderCategoriesIncluded,
                refresh.SportsbookBookmakersRequested,
                refresh.DfsBookmakersRequested,
                refresh.MarketsRequested,
                refresh.EstimatedApiCalls,
                refresh.ActualApiCalls,
                refresh.PlatformPropsCreated,
                refresh.PlatformPropsUpdated,
                refresh.PlatformPropsUpserted,
                refresh.PredictionCount,
                refresh.LineShoppingGroupCount,
                refresh.ErrorMessage,
                refresh.DiagnosticsJson
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var diagnostics = row.DiagnosticsJson?.RootElement.Clone();
        return new TheOddsSoccerWorldCupRefreshRun(
            row.Id,
            row.Sport,
            row.SlateDate,
            row.StartedAtUtc,
            row.CompletedAtUtc,
            row.Succeeded,
            row.Failed,
            row.Blocked,
            row.DryRun,
            row.ForceLive,
            row.BookmakerPreset,
            row.ProviderCategoriesIncluded,
            row.SportsbookBookmakersRequested,
            row.DfsBookmakersRequested,
            row.MarketsRequested,
            row.EstimatedApiCalls,
            row.ActualApiCalls,
            row.PlatformPropsCreated,
            row.PlatformPropsUpdated,
            row.PlatformPropsUpserted,
            row.PredictionCount,
            row.LineShoppingGroupCount,
            row.ErrorMessage,
            diagnostics);
    }

    private static int? ExtractJsonInt(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } json)
        {
            return null;
        }

        if (!TryGetJsonPropertyIgnoreCase(json, propertyName, out var property)
            && (!TryGetJsonPropertyIgnoreCase(json, "diagnostics", out var diagnostics)
                || diagnostics.ValueKind != JsonValueKind.Object
                || !TryGetJsonPropertyIgnoreCase(diagnostics, propertyName, out property)))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }

    private static bool TryGetJsonPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string BuildTheOddsSoccerWorldCupIngestionLikelyReason(
        int realProviderEvents,
        int syntheticPlatformRows,
        TheOddsSoccerWorldCupRefreshRun? latestRefreshRun,
        int apiSportsFixturesWithResults,
        int resolvedProviderEvents)
    {
        if (realProviderEvents == 0 && syntheticPlatformRows > 0)
        {
            return "Persisted Soccer platform rows exist, but they are legacy/synthetic or missing The Odds context-v2 World Cup sportsbook metadata.";
        }

        var eventsMatchedToSlate = ExtractJsonInt(latestRefreshRun?.Diagnostics, "eventsMatchedToSlate");
        if (realProviderEvents <= 1 && latestRefreshRun is not null && eventsMatchedToSlate <= 1)
        {
            return "Latest Soccer refresh discovered provider events but matched or targeted only one slate event for persisted sportsbook context.";
        }

        if (realProviderEvents <= 1 && latestRefreshRun is not null && latestRefreshRun.PlatformPropsUpserted <= realProviderEvents)
        {
            return "Only one real The Odds Soccer provider event appears to have been ingested in the latest persisted Soccer refresh run.";
        }

        if (realProviderEvents <= 1 && latestRefreshRun is not null && latestRefreshRun.Succeeded && latestRefreshRun.PlatformPropsUpserted == 0)
        {
            return "Latest Soccer refresh succeeded but upserted zero provider rows, likely because no sportsbook rows were returned or events were skipped.";
        }

        if (realProviderEvents <= 1 && apiSportsFixturesWithResults > resolvedProviderEvents)
        {
            return "API-Sports has results for more fixtures than the number of real The Odds provider events persisted locally.";
        }

        return "The Odds Soccer provider event ingestion appears proportional to persisted API-Sports fixture coverage.";
    }

    private static string BuildTheOddsSoccerWorldCupIngestionRecommendation(
        TheOddsSoccerWorldCupIngestionCoverageSummary summary,
        TheOddsSoccerWorldCupIngestionGapAnalysis gapAnalysis)
    {
        if (summary.RealTheOddsProviderEvents == 0)
        {
            return "No real The Odds Soccer World Cup sportsbook provider events are persisted. Inspect Soccer refresh diagnostics and rerun the guarded manual refresh only after confirming provider availability.";
        }

        if (summary.ApiSportsFixturesWithResults > summary.RealTheOddsProviderEvents)
        {
            return "The Odds provider-event ingestion is the current scale blocker. Review latest refresh diagnostics, event targeting, region/bookmaker filters, and provider returned markets before expanding grading coverage.";
        }

        if (gapAnalysis.RealTheOddsEventsUnresolved > 0)
        {
            return "Some real The Odds provider events are persisted but unresolved. Use fixture-mapping-coverage blockers before grading coverage.";
        }

        return "Provider event ingestion and fixture mapping are aligned. Continue with grading coverage and alias expansion diagnostics.";
    }

    private static ApiSportsSoccerGradingCoverageUnresolvedEvent BuildApiSportsSoccerGradingCoverageUnresolvedEvent(
        string? fixtureId,
        string? providerEventId,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> rows,
        string reason)
    {
        var first = rows.OrderBy(row => row.EventStartTimeUtc).FirstOrDefault();
        return new ApiSportsSoccerGradingCoverageUnresolvedEvent(
            fixtureId,
            providerEventId,
            first?.EventName,
            first?.EventDate,
            first?.HomeTeamName,
            first?.AwayTeamName,
            rows.Count,
            rows.Select(row => row.NormalizedPlayerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            reason);
    }

    private static ApiSportsSoccerGradingCoverageResultOnlyFixture BuildApiSportsSoccerGradingCoverageResultOnlyFixture(
        string fixtureId,
        IReadOnlyList<ApiSportsMarketJoinResultRow> rows)
    {
        var first = rows.OrderBy(row => row.EventDate).FirstOrDefault();
        return new ApiSportsSoccerGradingCoverageResultOnlyFixture(
            fixtureId,
            first?.EventDate,
            rows.Count,
            rows.Select(row => FirstNonBlank(row.ApiSportsPlayerId, row.NormalizedPlayerName)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "NoResolvedTheOddsProviderEvent");
    }

    private static ApiSportsSoccerGradingCoverageFixtureDetail BuildApiSportsSoccerGradingCoverageFixtureDetail(
        string? fixtureId,
        string? providerEventId,
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        bool includeSamples,
        int sampleLimit)
    {
        var beforeAliasPlatformRows = platformRows
            .Select(row => row with { AliasCanonicalPlayerId = null })
            .ToArray();
        var beforeAlias = BuildApiSportsSoccerGradingComputation(resultRows, beforeAliasPlatformRows, sampleLimit);
        var afterAlias = BuildApiSportsSoccerGradingComputation(resultRows, platformRows, sampleLimit);
        var firstPlatform = platformRows.OrderBy(row => row.EventStartTimeUtc).FirstOrDefault();
        var firstResult = resultRows.OrderBy(row => row.EventDate).FirstOrDefault();
        var sampleAliasLinkedRows = afterAlias.GradedSamples
            .Where(row => row.JoinMethod.Contains("approvedTheOddsAliasLink", StringComparison.OrdinalIgnoreCase))
            .Take(sampleLimit)
            .ToArray();

        return new ApiSportsSoccerGradingCoverageFixtureDetail(
            fixtureId,
            providerEventId,
            firstPlatform?.EventName,
            firstPlatform?.EventDate ?? firstResult?.EventDate,
            firstPlatform?.HomeTeamName,
            firstPlatform?.AwayTeamName,
            afterAlias.Summary.CandidatePlatformRows,
            afterAlias.Summary.ImportedResultRows,
            platformRows.Select(row => row.NormalizedPlayerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            resultRows.Select(row => FirstNonBlank(row.ApiSportsPlayerId, row.NormalizedPlayerName)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            platformRows.Where(row => row.AliasCanonicalPlayerId.HasValue).Select(row => row.AliasCanonicalPlayerId!.Value).Distinct().Count(),
            beforeAlias.Summary.FullyJoinableRows,
            afterAlias.Summary.FullyJoinableRows,
            afterAlias.Summary.GradedPreviewRows,
            afterAlias.Summary.OverWins,
            afterAlias.Summary.UnderWins,
            afterAlias.Summary.Pushes,
            afterAlias.Summary.NoLineRows,
            afterAlias.Summary.UngradedRows,
            afterAlias.Summary.GradeCoverageRate,
            afterAlias.Blockers,
            includeSamples ? afterAlias.GradedSamples.Take(sampleLimit).ToArray() : [],
            includeSamples ? afterAlias.UnjoinablePlatformSamples.Take(sampleLimit).ToArray() : [],
            includeSamples ? sampleAliasLinkedRows : []);
    }

    private static ApiSportsSoccerGradingComputation BuildApiSportsSoccerGradingComputation(
        IReadOnlyList<ApiSportsMarketJoinResultRow> resultRows,
        IReadOnlyList<ApiSportsMarketJoinPlatformRow> platformRows,
        int sampleLimit)
    {
        var eventMatches = resultRows.ToDictionary(
            row => row.ResultId,
            row => platformRows.Where(platform => IsApiSportsMarketJoinEventMatch(row, platform)).ToArray());
        var playerMatches = resultRows.ToDictionary(
            row => row.ResultId,
            row => eventMatches[row.ResultId].Where(platform => IsApiSportsMarketJoinPlayerMatch(row, platform)).ToArray());
        var propMatches = resultRows.ToDictionary(
            row => row.ResultId,
            row => eventMatches[row.ResultId].Where(platform => ApiSportsSoccerStatKeysMatch(row.StatKey, platform.CanonicalStatKey)).ToArray());
        var fullMatches = resultRows
            .SelectMany(result => eventMatches[result.ResultId]
                .Where(platform => IsApiSportsMarketJoinPlayerMatch(result, platform)
                    && ApiSportsSoccerStatKeysMatch(result.StatKey, platform.CanonicalStatKey))
                .Select(platform => BuildApiSportsSoccerGradingSample(result, platform)))
            .ToArray();
        var fullyJoinableResultIds = fullMatches.Select(row => row.ResultId).Distinct().ToHashSet();
        var matchedPlatformIds = fullMatches.Select(row => row.PlatformPropId).Distinct().ToHashSet();
        var unmatchedResults = resultRows
            .Where(row => !fullyJoinableResultIds.Contains(row.ResultId))
            .Select(row => BuildApiSportsMarketJoinUnmatchedResultSample(row, eventMatches[row.ResultId], playerMatches[row.ResultId], propMatches[row.ResultId]))
            .ToArray();
        var noLineRows = fullMatches.Count(row => row.GradeResult == "NoLine");
        var gradedRows = fullMatches.Where(row => row.GradeResult is "OverWin" or "UnderWin" or "Push").ToArray();
        var blockers = new ApiSportsSoccerGradingPreviewBlockers(
            platformRows.Count,
            platformRows.Count(row => string.IsNullOrWhiteSpace(row.ProviderPlayerId)),
            resultRows.Count(row => string.IsNullOrWhiteSpace(row.TeamName))
                + platformRows.Count(row => string.IsNullOrWhiteSpace(row.PlayerTeamName)),
            unmatchedResults.Count(row => row.Reason == "PlayerNameMismatch"),
            platformRows.Count(row => string.IsNullOrWhiteSpace(row.CanonicalStatKey))
                + unmatchedResults.Count(row => row.Reason == "PropTypeMappingMissing"),
            unmatchedResults.Count(row => row.Reason == "EventDateMismatch"),
            noLineRows,
            unmatchedResults.Length + platformRows.Count(row => !matchedPlatformIds.Contains(row.PlatformPropId)));
        var summary = new ApiSportsSoccerGradingPreviewSummary(
            platformRows.Count,
            resultRows.Count,
            fullyJoinableResultIds.Count,
            gradedRows.Length,
            fullMatches.Count(row => row.GradeResult == "OverWin"),
            fullMatches.Count(row => row.GradeResult == "UnderWin"),
            fullMatches.Count(row => row.GradeResult == "Push"),
            noLineRows,
            fullMatches.Count(row => row.GradeResult is "NoLine" or "Unjoinable") + unmatchedResults.Length,
            platformRows.Count == 0 ? 0 : decimal.Round((decimal)gradedRows.Length / platformRows.Count, 4),
            fullMatches
                .GroupBy(row => string.IsNullOrWhiteSpace(row.Bookmaker) ? "Unknown" : row.Bookmaker, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => new
                {
                    bookmaker = group.Key,
                    rows = group.Count(),
                    overWins = group.Count(row => row.GradeResult == "OverWin"),
                    underWins = group.Count(row => row.GradeResult == "UnderWin"),
                    pushes = group.Count(row => row.GradeResult == "Push"),
                    noLineRows = group.Count(row => row.GradeResult == "NoLine")
                })
                .ToArray(),
            fullMatches
                .GroupBy(row => string.IsNullOrWhiteSpace(row.ProviderMarketKey) ? "Unknown" : row.ProviderMarketKey, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => new
                {
                    market = group.Key,
                    rows = group.Count(),
                    overWins = group.Count(row => row.GradeResult == "OverWin"),
                    underWins = group.Count(row => row.GradeResult == "UnderWin"),
                    pushes = group.Count(row => row.GradeResult == "Push"),
                    noLineRows = group.Count(row => row.GradeResult == "NoLine")
                })
                .ToArray(),
            fullMatches
                .GroupBy(row => string.IsNullOrWhiteSpace(row.PropType) ? "Unknown" : row.PropType, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => new
                {
                    propType = group.Key,
                    rows = group.Count(),
                    overWins = group.Count(row => row.GradeResult == "OverWin"),
                    underWins = group.Count(row => row.GradeResult == "UnderWin"),
                    pushes = group.Count(row => row.GradeResult == "Push"),
                    noLineRows = group.Count(row => row.GradeResult == "NoLine")
                })
                .ToArray());

        return new ApiSportsSoccerGradingComputation(
            summary,
            blockers,
            fullMatches.Take(sampleLimit).ToArray(),
            unmatchedResults.Take(sampleLimit).ToArray(),
            platformRows
                .Where(row => !matchedPlatformIds.Contains(row.PlatformPropId))
                .Select(BuildApiSportsMarketJoinUnmatchedPlatformSample)
                .Take(sampleLimit)
                .ToArray());
    }

    private static ApiSportsSoccerGradingPreviewSample BuildApiSportsSoccerGradingSample(
        ApiSportsMarketJoinResultRow result,
        ApiSportsMarketJoinPlatformRow platform)
    {
        var gradeResult = ResolveApiSportsSoccerPreviewGrade(platform.Side, platform.Line, result.ActualResultValue);
        var notes = new List<string>
        {
            "Debug-only preview. No grade was written.",
            "Sportsbook context rows are research-only and not Entry Builder playable."
        };
        if (gradeResult == "NoLine")
        {
            notes.Add("No usable Over/Under side was available for this platform row.");
        }

        return new ApiSportsSoccerGradingPreviewSample(
            result.ResultId,
            platform.PlatformPropId,
            platform.ProviderEventId,
            result.ApiSportsFixtureId,
            platform.EventName,
            platform.EventDate,
            platform.Bookmaker,
            platform.Provider,
            string.IsNullOrWhiteSpace(platform.ProviderCategory) ? "Sportsbook" : platform.ProviderCategory,
            platform.SportsbookContextResearchOnly ?? true,
            platform.EntryActionableForUser ?? false,
            platform.PlayerName,
            result.PlayerName,
            result.NormalizedPlayerName,
            result.PropTypeName,
            platform.MarketKey,
            platform.Line,
            platform.Side == "Unavailable" ? null : platform.Side,
            result.ActualResultValue,
            gradeResult,
            BuildApiSportsSoccerPreviewJoinMethod(result, platform),
            gradeResult == "NoLine" ? "Low" : "DiagnosticOnly",
            notes);
    }

    private static string ResolveApiSportsSoccerPreviewGrade(string side, decimal line, decimal actualValue)
    {
        if (side == "Unavailable")
        {
            return "NoLine";
        }

        if (actualValue == line)
        {
            return "Push";
        }

        return actualValue > line ? "OverWin" : "UnderWin";
    }

    private static string BuildApiSportsSoccerPreviewJoinMethod(ApiSportsMarketJoinResultRow result, ApiSportsMarketJoinPlatformRow platform)
    {
        var parts = new List<string> { "eventDate", "normalizedPlayerName", "canonicalStatKey" };
        if (platform.AliasCanonicalPlayerId.HasValue && platform.AliasCanonicalPlayerId.Value == result.PlayerId)
        {
            parts.Add("approvedTheOddsAliasLink");
        }

        if (!string.IsNullOrWhiteSpace(platform.ProviderEventId))
        {
            parts.Add("providerEventContext");
        }

        if (!string.IsNullOrWhiteSpace(result.TeamName)
            && (NamesMatch(result.NormalizedTeamName, NormalizeResultPlayerName(platform.HomeTeamName))
                || NamesMatch(result.NormalizedTeamName, NormalizeResultPlayerName(platform.AwayTeamName))))
        {
            parts.Add("teamContext");
        }

        return string.Join("+", parts);
    }

    private static string BuildApiSportsSoccerGradingCoverageRecommendation(
        ApiSportsSoccerGradingCoverageTopLevelSummary summary,
        ApiSportsSoccerGradingPreviewBlockers blockers)
    {
        if (summary.ResolvedPairsEvaluated == 0
            && summary.UnresolvedProviderEvents == 0
            && summary.ResultOnlyFixtures == 0)
        {
            return "No persisted API-Sports results or The Odds Soccer platform rows were found for this date range. Import results and ingest sportsbook context before reviewing Soccer grading coverage.";
        }

        if (summary.UnresolvedProviderEvents > 0 && summary.ResolvedPairsEvaluated == 0)
        {
            return "Persisted The Odds Soccer provider events exist, but none resolved to API-Sports fixtures. Recover provider event-to-fixture mapping before expanding Soccer grading diagnostics.";
        }

        if (summary.ResultOnlyFixtures > 0 && summary.ResolvedPairsEvaluated == 0)
        {
            return "Imported API-Sports results exist, but no resolved The Odds Soccer provider events were found for those fixtures. Ingest or recover Soccer sportsbook context before reviewing grading coverage.";
        }

        if (blockers.PlayerNameMismatch > blockers.PropTypeMappingMissing
            && blockers.PlayerNameMismatch > blockers.EventDateMismatch)
        {
            return "Many persisted rows fail at player-name matching. Expand reviewed The Odds to API-Sports alias links before considering broader Soccer grading diagnostics.";
        }

        if (blockers.EventDateMismatch > 0
            && blockers.EventDateMismatch >= blockers.PropTypeMappingMissing
            || blockers.MissingApiSportsFixtureIdOnMarketRows > 0
            && blockers.MissingApiSportsFixtureIdOnMarketRows >= blockers.PropTypeMappingMissing)
        {
            return "Coverage is limited by fixture/date mapping. Recover provider event-to-fixture mapping before expanding Soccer grading diagnostics.";
        }

        if (blockers.PropTypeMappingMissing > 0
            && blockers.PropTypeMappingMissing >= blockers.PlayerNameMismatch)
        {
            return "Many persisted rows fail at prop/stat mapping. Expand Soccer provider stat mappings before considering broader Soccer grading diagnostics.";
        }

        if (summary.FixturesWithGradedPreviewRows >= 2 && summary.OverallGradeCoverageRate >= 0.50m)
        {
            return "Debug-only Soccer grading coverage is promising across multiple fixtures. Consider a debug-only Soccer performance summary next. Public Soccer grading should remain disabled until explicitly promoted.";
        }

        if (summary.TotalGradedPreviewRows > 0)
        {
            return "Some Soccer rows are gradeable in debug preview, but coverage is still uneven. Review worstCoverageFixtures and topBlockersAggregated before promoting any broader workflow.";
        }

        return "No gradeable Soccer preview rows were found. Review fixture matching, alias coverage, and stat mappings before building any Soccer performance summary.";
    }

    private static string BuildApiSportsSoccerGradingPreviewRecommendation(
        int gradedPreviewRows,
        ApiSportsSoccerGradingPreviewBlockers blockers)
    {
        if (gradedPreviewRows > 0)
        {
            return "Debug-only Soccer grading preview is available. Public Soccer grading remains disabled.";
        }

        var largestBlocker = new[]
            {
                ("unjoinable rows", blockers.UnjoinableRows),
                ("missing provider player ids", blockers.MissingProviderPlayerId),
                ("missing player team context", blockers.MissingPlayerTeam),
                ("player-name mismatches", blockers.PlayerNameMismatch),
                ("prop mapping gaps", blockers.PropTypeMappingMissing),
                ("event-date mismatches", blockers.EventDateMismatch),
                ("no-line rows", blockers.NoLineRows)
            }
            .OrderByDescending(row => row.Item2)
            .First();

        return largestBlocker.Item2 > 0
            ? $"No graded preview rows are available. Largest blocker: {largestBlocker.Item1} ({largestBlocker.Item2})."
            : "No graded preview rows are available for this scope.";
    }

    private static async Task<IReadOnlyList<TheOddsAliasPlatformPlayer>> LoadTheOddsAliasPlatformPlayersAsync(
        ApplicationDbContext dbContext,
        string providerEventId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PlatformProps
            .AsNoTracking()
            .Include(platformProp => platformProp.Platform)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Player)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Game)!.ThenInclude(game => game!.HomeTeam)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Game)!.ThenInclude(game => game!.AwayTeam)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(platformProp => platformProp.Prop != null
                && platformProp.Prop.League != null
                && platformProp.Prop.League.Sport != null
                && platformProp.Prop.League.Sport.Code == "SOCCER")
            .Select(platformProp => new
            {
                platformProp.Id,
                platformProp.PlatformPropId,
                platformProp.ExternalStableKey,
                platformProp.Metadata,
                Provider = platformProp.Platform == null ? null : platformProp.Platform.Name,
                Bookmaker = platformProp.Platform == null ? null : platformProp.Platform.Slug,
                PlayerName = platformProp.Prop!.Player == null ? string.Empty : platformProp.Prop.Player.DisplayName,
                EventStartTime = platformProp.Prop.StartsAt,
                HomeTeamName = platformProp.Prop.Game == null || platformProp.Prop.Game.HomeTeam == null ? null : platformProp.Prop.Game.HomeTeam.Name,
                AwayTeamName = platformProp.Prop.Game == null || platformProp.Prop.Game.AwayTeam == null ? null : platformProp.Prop.Game.AwayTeam.Name
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                var rowProviderEventId = FirstNonBlank(
                    ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "providerEventId"),
                    ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "eventId"),
                    row.ExternalStableKey);
                var homeTeam = FirstNonBlank(row.HomeTeamName, ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "homeTeam"));
                var awayTeam = FirstNonBlank(row.AwayTeamName, ExtractApiSportsContextMetadataString(row.Metadata, "eventInfo", "awayTeam"));
                return new
                {
                    row.Id,
                    row.PlatformPropId,
                    ProviderEventId = rowProviderEventId,
                    row.Provider,
                    row.Bookmaker,
                    row.PlayerName,
                    NormalizedPlayerName = NormalizeResultPlayerName(row.PlayerName),
                    Tokens = PlayerNameNormalizer.Tokenize(row.PlayerName),
                    row.EventStartTime,
                    EventName = BuildApiSportsContextEventName(awayTeam, homeTeam),
                    HomeTeamName = homeTeam,
                    AwayTeamName = awayTeam,
                    ProviderPlayerId = ExtractApiSportsContextMetadataString(row.Metadata, "player", "providerPlayerId"),
                    PlayerTeamName = ExtractApiSportsContextMetadataString(row.Metadata, "player", "teamName")
                };
            })
            .Where(row => string.Equals(row.ProviderEventId, providerEventId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.NormalizedPlayerName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group
                    .OrderBy(row => string.IsNullOrWhiteSpace(row.PlayerName))
                    .ThenBy(row => row.PlayerName)
                    .First();
                return new TheOddsAliasPlatformPlayer(
                    first.PlayerName,
                    first.NormalizedPlayerName,
                    first.Tokens,
                    first.ProviderEventId,
                    first.EventName,
                    first.EventStartTime,
                    first.HomeTeamName,
                    first.AwayTeamName,
                    first.ProviderPlayerId,
                    first.PlayerTeamName,
                    group.Select(row => row.Provider).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!,
                    group.Select(row => row.Bookmaker).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!,
                    group.Count());
            })
            .OrderBy(row => row.PlatformPlayerName)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ApiSportsAliasFixturePlayer>> LoadApiSportsAliasFixturePlayersAsync(
        ApplicationDbContext dbContext,
        string fixtureId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.Player)!.ThenInclude(player => player!.Team)
            .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                && result.ExternalSourceIdentifier.StartsWith($"{ApiSportsFootballResultSource}:"))
            .Select(result => new
            {
                result.PlayerId,
                result.ExternalSourceIdentifier,
                PlayerName = result.Player == null ? string.Empty : result.Player.DisplayName,
                TeamName = result.Player == null || result.Player.Team == null ? null : result.Player.Team.Name
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => string.Equals(ExtractApiSportsFixtureId(row.ExternalSourceIdentifier), fixtureId, StringComparison.OrdinalIgnoreCase))
            .Select(row => new
            {
                ApiSportsPlayerId = ExtractApiSportsPlayerId(row.ExternalSourceIdentifier),
                row.PlayerId,
                row.PlayerName,
                NormalizedPlayerName = NormalizeResultPlayerName(row.PlayerName),
                Tokens = PlayerNameNormalizer.Tokenize(row.PlayerName),
                row.TeamName,
                NormalizedTeamName = NormalizeResultPlayerName(row.TeamName)
            })
            .GroupBy(
                row => string.IsNullOrWhiteSpace(row.ApiSportsPlayerId)
                    ? $"local:{row.PlayerId}"
                    : row.ApiSportsPlayerId!,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group
                    .OrderBy(row => string.IsNullOrWhiteSpace(row.PlayerName))
                    .ThenBy(row => row.PlayerName)
                    .First();
                return new ApiSportsAliasFixturePlayer(
                    group.Key,
                    first.PlayerId,
                    first.PlayerName,
                    first.NormalizedPlayerName,
                    first.Tokens,
                    first.TeamName,
                    first.NormalizedTeamName,
                    group.Count());
            })
            .OrderBy(row => row.ApiSportsPlayerName)
            .ToArray();
    }

    private static TheOddsApiSportsAliasCandidatePreview BuildTheOddsApiSportsAliasCandidatePreview(
        IReadOnlyList<ApiSportsAliasFixturePlayer> resultPlayers,
        IReadOnlyList<TheOddsAliasPlatformPlayer> platformPlayers,
        int sampleLimit)
    {
        var candidateRows = platformPlayers
            .Select(platformPlayer => BuildTheOddsApiSportsAliasCandidateRow(platformPlayer, resultPlayers))
            .ToArray();
        var summary = new
        {
            totalUnjoinedPlatformPlayers = candidateRows.Count(row => !row.ExactNormalizedMatch),
            platformPlayerCount = platformPlayers.Count,
            apiSportsFixturePlayerCount = resultPlayers.Count,
            candidateCount = candidateRows.Count(row => row.SuggestedAction is "SafeAliasCandidate" or "NeedsReview" or "Ambiguous"),
            safeCandidateCount = candidateRows.Count(row => row.SuggestedAction == "SafeAliasCandidate"),
            needsReviewCount = candidateRows.Count(row => row.SuggestedAction == "NeedsReview"),
            ambiguousCount = candidateRows.Count(row => row.SuggestedAction == "Ambiguous"),
            noCandidateCount = candidateRows.Count(row => row.SuggestedAction == "NoCandidate")
        };
        var blockers = new
        {
            missingProviderPlayerId = platformPlayers.Count(row => string.IsNullOrWhiteSpace(row.ProviderPlayerId)),
            missingPlayerTeam = platformPlayers.Count(row => string.IsNullOrWhiteSpace(row.PlayerTeamName)),
            exactAlreadyJoined = candidateRows.Count(row => row.ExactNormalizedMatch),
            ambiguous = candidateRows.Count(row => row.SuggestedAction == "Ambiguous"),
            noCandidate = candidateRows.Count(row => row.SuggestedAction == "NoCandidate"),
            insufficientTokenOverlap = candidateRows.Count(row => row.Blockers.Contains("InsufficientTokenOverlap", StringComparer.OrdinalIgnoreCase))
        };

        return new TheOddsApiSportsAliasCandidatePreview(
            summary,
            blockers,
            new
            {
                aliasCandidates = candidateRows
                    .Where(row => row.SuggestedAction != "NoCandidate")
                    .Take(sampleLimit)
                    .ToArray(),
                noCandidate = candidateRows
                    .Where(row => row.SuggestedAction == "NoCandidate")
                    .Take(sampleLimit)
                    .ToArray()
            },
            BuildTheOddsApiSportsAliasRecommendation(candidateRows));
    }

    private static TheOddsApiSportsAliasCandidateRow BuildTheOddsApiSportsAliasCandidateRow(
        TheOddsAliasPlatformPlayer platformPlayer,
        IReadOnlyList<ApiSportsAliasFixturePlayer> resultPlayers)
    {
        var candidates = resultPlayers
            .Select(resultPlayer => ScoreTheOddsApiSportsAliasCandidate(platformPlayer, resultPlayer))
            .Where(candidate => candidate.Confidence > 0)
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.ApiSportsPlayerName)
            .ToArray();
        var bestConfidence = candidates.Length == 0 ? 0 : candidates[0].Confidence;
        var topCandidates = candidates
            .Where(candidate => candidate.Confidence == bestConfidence)
            .ToArray();
        var exactNormalizedMatch = candidates.Any(candidate => candidate.MatchReasons.Contains("ExactNormalizedName", StringComparer.OrdinalIgnoreCase));
        var blockers = new List<string>();
        string suggestedAction;

        if (topCandidates.Length == 0)
        {
            suggestedAction = "NoCandidate";
            blockers.Add("InsufficientTokenOverlap");
        }
        else if (topCandidates.Length > 1 && bestConfidence < 1m)
        {
            suggestedAction = "Ambiguous";
            blockers.Add("AmbiguousCandidate");
        }
        else if (bestConfidence >= 0.92m)
        {
            suggestedAction = "SafeAliasCandidate";
        }
        else if (bestConfidence >= 0.70m)
        {
            suggestedAction = "NeedsReview";
            if (string.IsNullOrWhiteSpace(platformPlayer.PlayerTeamName))
            {
                blockers.Add("MissingTeamContext");
            }
        }
        else
        {
            suggestedAction = "NoCandidate";
            blockers.Add("InsufficientTokenOverlap");
        }

        return new TheOddsApiSportsAliasCandidateRow(
            platformPlayer.PlatformPlayerName,
            platformPlayer.NormalizedPlayerName,
            platformPlayer.ProviderEventId,
            platformPlayer.EventName,
            platformPlayer.EventStartUtc,
            platformPlayer.HomeTeamName,
            platformPlayer.AwayTeamName,
            platformPlayer.ProviderPlayerId,
            platformPlayer.PlayerTeamName,
            platformPlayer.Providers,
            platformPlayer.Bookmakers,
            platformPlayer.PlatformRowCount,
            exactNormalizedMatch,
            suggestedAction,
            bestConfidence,
            blockers,
            topCandidates);
    }

    private static TheOddsApiSportsAliasCandidateScore ScoreTheOddsApiSportsAliasCandidate(
        TheOddsAliasPlatformPlayer platformPlayer,
        ApiSportsAliasFixturePlayer resultPlayer)
    {
        var reasons = new List<string>();
        var platformTokens = platformPlayer.Tokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resultTokens = resultPlayer.Tokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sharedTokens = resultTokens.Intersect(platformTokens, StringComparer.OrdinalIgnoreCase).ToArray();
        decimal confidence = 0;

        if (string.Equals(platformPlayer.NormalizedPlayerName, resultPlayer.NormalizedPlayerName, StringComparison.OrdinalIgnoreCase))
        {
            confidence = 1.0m;
            reasons.Add("ExactNormalizedName");
        }
        else if (resultTokens.Count > 0 && resultTokens.All(token => platformTokens.Contains(token)) && sharedTokens.Length >= 2)
        {
            confidence = 0.94m;
            reasons.Add("ApiSportsTokensSubsetOfTheOddsName");
        }
        else if (platformTokens.Count > 0 && platformTokens.All(token => resultTokens.Contains(token)) && sharedTokens.Length >= 2)
        {
            confidence = 0.90m;
            reasons.Add("TheOddsTokensSubsetOfApiSportsName");
        }
        else if (!string.IsNullOrWhiteSpace(platformPlayer.NormalizedPlayerName)
            && !string.IsNullOrWhiteSpace(resultPlayer.NormalizedPlayerName)
            && (platformPlayer.NormalizedPlayerName.Contains(resultPlayer.NormalizedPlayerName, StringComparison.OrdinalIgnoreCase)
                || resultPlayer.NormalizedPlayerName.Contains(platformPlayer.NormalizedPlayerName, StringComparison.OrdinalIgnoreCase)))
        {
            confidence = 0.84m;
            reasons.Add("NormalizedContainment");
        }
        else if (sharedTokens.Length >= 2)
        {
            confidence = 0.78m;
            reasons.Add("SharedTwoOrMoreNameTokens");
        }
        else if (sharedTokens.Length == 1 && platformTokens.Count <= 2 && resultTokens.Count <= 2)
        {
            confidence = 0.58m;
            reasons.Add("SingleSharedToken");
        }

        var teamMatch = !string.IsNullOrWhiteSpace(resultPlayer.TeamName)
            && (NamesMatch(resultPlayer.NormalizedTeamName, NormalizeResultPlayerName(platformPlayer.PlayerTeamName))
                || NamesMatch(resultPlayer.NormalizedTeamName, NormalizeResultPlayerName(platformPlayer.HomeTeamName))
                || NamesMatch(resultPlayer.NormalizedTeamName, NormalizeResultPlayerName(platformPlayer.AwayTeamName)));
        if (teamMatch)
        {
            reasons.Add("EventTeamContext");
        }

        return new TheOddsApiSportsAliasCandidateScore(
            resultPlayer.ApiSportsPlayerId,
            resultPlayer.LocalPlayerId,
            resultPlayer.ApiSportsPlayerName,
            resultPlayer.NormalizedPlayerName,
            resultPlayer.TeamName,
            confidence,
            sharedTokens,
            reasons);
    }

    private static string BuildTheOddsApiSportsAliasRecommendation(IReadOnlyList<TheOddsApiSportsAliasCandidateRow> rows)
    {
        if (rows.Any(row => row.SuggestedAction == "SafeAliasCandidate"))
        {
            return "Safe alias candidates are available for manual review. This endpoint does not write aliases, approve links, or enable public Soccer grading.";
        }

        if (rows.Any(row => row.SuggestedAction == "Ambiguous"))
        {
            return "Ambiguous aliases need manual review before any identity link approval.";
        }

        if (rows.Any(row => row.SuggestedAction == "NeedsReview"))
        {
            return "Potential aliases need manual review because team/provider player context is incomplete.";
        }

        return "No likely aliases were found for this fixture/provider event.";
    }

    private static async Task<TheOddsApiSportsAliasBatch> BuildTheOddsApiSportsAliasBatchAsync(
        ApplicationDbContext dbContext,
        ApiSportsWorldCupDateRange range,
        int maxFixtures,
        int sampleLimit,
        decimal minimumConfidence,
        bool includeExactNormalizedMatches,
        bool includeTokenSubsetMatches,
        int maxLinks,
        bool includePlan,
        CancellationToken cancellationToken)
    {
        var resultRows = await LoadApiSportsMarketJoinResultRowsAsync(dbContext, range, null, cancellationToken);
        var platformRows = await LoadApiSportsMarketJoinPlatformRowsAsync(dbContext, range, null, cancellationToken);
        var mappingCoverage = BuildApiSportsSoccerFixtureMappingCoverage(
            resultRows,
            platformRows,
            includeResolved: true,
            includeUnresolved: false,
            includeSamples: false,
            maxFixtures);
        var coverage = BuildApiSportsSoccerGradingCoverageSummary(
            resultRows,
            platformRows,
            includeFixtureDetails: false,
            includeSamples: false,
            maxFixtures,
            maxSamplesPerFixture: 0);
        var resolvedPairs = mappingCoverage.ResolvedEvents
            .Where(row => string.Equals(row.ResolutionStatus, "Resolved", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row.ProviderEventId)
                && !string.IsNullOrWhiteSpace(row.ResolvedFixtureId))
            .OrderBy(row => row.EventDate)
            .ThenBy(row => row.EventName)
            .ThenBy(row => row.ProviderEventId)
            .Take(maxFixtures)
            .ToArray();
        var candidateGroups = new List<TheOddsApiSportsAliasBatchCandidateGroup>();
        var linkPlans = new List<TheOddsApiSportsAliasBatchLinkPlan>();
        var flattenedRows = new List<(ApiSportsSoccerFixtureMappingEvent Pair, TheOddsApiSportsAliasCandidateRow Row)>();

        foreach (var pair in resolvedPairs)
        {
            var fixtureId = pair.ResolvedFixtureId!;
            var providerEventId = pair.ProviderEventId;
            var resultPlayers = await LoadApiSportsAliasFixturePlayersAsync(dbContext, fixtureId, cancellationToken);
            var platformPlayers = await LoadTheOddsAliasPlatformPlayersAsync(dbContext, providerEventId, cancellationToken);
            var candidateRows = platformPlayers
                .Select(platformPlayer => BuildTheOddsApiSportsAliasCandidateRow(platformPlayer, resultPlayers))
                .ToArray();
            flattenedRows.AddRange(candidateRows.Select(row => (pair, row)));

            TheOddsApiSportsAliasLinkPlan? plan = null;
            if (includePlan)
            {
                plan = await BuildTheOddsApiSportsAliasLinkPlanAsync(
                    dbContext,
                    candidateRows,
                    fixtureId,
                    providerEventId,
                    minimumConfidence,
                    includeExactNormalizedMatches,
                    includeTokenSubsetMatches,
                    maxLinks,
                    cancellationToken);
                linkPlans.Add(new TheOddsApiSportsAliasBatchLinkPlan(fixtureId, providerEventId, plan));
            }

            candidateGroups.Add(new TheOddsApiSportsAliasBatchCandidateGroup(
                providerEventId,
                fixtureId,
                pair.EventName,
                pair.EventDate,
                pair.HomeTeam,
                pair.AwayTeam,
                platformPlayers.Count,
                resultPlayers.Count,
                candidateRows.Length,
                candidateRows.Count(row => row.SuggestedAction == "SafeAliasCandidate"),
                candidateRows.Count(row => row.SuggestedAction == "NeedsReview"),
                candidateRows.Count(row => row.SuggestedAction == "Ambiguous"),
                candidateRows.Count(row => row.SuggestedAction == "NoCandidate"),
                plan?.LinksAlreadyExisting ?? 0,
                plan?.ProposedLinks.Count ?? candidateRows.Count(row => row.SuggestedAction == "SafeAliasCandidate" && row.Confidence >= minimumConfidence),
                candidateRows
                    .Where(row => row.SuggestedAction != "NoCandidate")
                    .OrderByDescending(row => row.Confidence)
                    .ThenBy(row => row.PlatformPlayerName)
                    .Take(sampleLimit)
                    .ToArray()));
        }

        var candidateRowsAll = flattenedRows.Select(row => row.Row).ToArray();
        var proposedLinks = linkPlans
            .SelectMany(group => group.Plan.ProposedLinks)
            .Take(maxLinks)
            .ToArray();
        var candidateProviderEntityIds = candidateRowsAll
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderEventId))
            .Select(row => BuildTheOddsSoccerAliasProviderEntityId(row.ProviderEventId!, row.NormalizedPlatformPlayerName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var approvedProviderEntityIds = await LoadApprovedTheOddsSoccerAliasProviderEntityIdsAsync(
            dbContext,
            candidateProviderEntityIds,
            cancellationToken);
        bool IsApproved(TheOddsApiSportsAliasCandidateRow row)
        {
            return !string.IsNullOrWhiteSpace(row.ProviderEventId)
                && approvedProviderEntityIds.Contains(BuildTheOddsSoccerAliasProviderEntityId(row.ProviderEventId!, row.NormalizedPlatformPlayerName));
        }

        var deduplicatedCandidates = BuildTheOddsApiSportsAliasBatchDeduplicatedCandidates(flattenedRows, sampleLimit);
        var currentSummary = coverage.Summary;
        var projectedAdditionalJoinableRows = proposedLinks.Sum(link =>
            candidateRowsAll
                .Where(row => string.Equals(row.ProviderEventId, link.ProviderEventId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.NormalizedPlatformPlayerName, link.NormalizedPlatformPlayerName, StringComparison.OrdinalIgnoreCase))
                .Select(row => row.PlatformRowCount)
                .DefaultIfEmpty(1)
                .First());
        var estimatedCoverageImpact = new
        {
            currentFullyJoinableRows = currentSummary.TotalFullyJoinableRows,
            currentGradedPreviewRows = currentSummary.TotalGradedPreviewRows,
            projectedAdditionalJoinableRows,
            projectedAdditionalGradedRows = projectedAdditionalJoinableRows
        };
        var candidateSummary = new
        {
            resolvedPairsEvaluated = resolvedPairs.Length,
            platformPlayerNamesEvaluated = candidateRowsAll.Length,
            candidateCount = candidateRowsAll.Count(row => row.SuggestedAction is "SafeAliasCandidate" or "NeedsReview" or "Ambiguous"),
            safeCandidateCount = candidateRowsAll.Count(row => row.SuggestedAction == "SafeAliasCandidate"),
            needsReviewCount = candidateRowsAll.Count(row => row.SuggestedAction == "NeedsReview"),
            ambiguousCount = candidateRowsAll.Count(row => row.SuggestedAction == "Ambiguous"),
            noCandidateCount = candidateRowsAll.Count(row => row.SuggestedAction == "NoCandidate"),
            alreadyLinkedCount = linkPlans.Sum(group => group.Plan.LinksAlreadyExisting),
            newSafeCandidateCount = proposedLinks.Length
        };
        var blockers = new
        {
            missingProviderPlayerId = candidateRowsAll.Count(row => string.IsNullOrWhiteSpace(row.ProviderPlayerId)),
            missingPlayerTeam = candidateRowsAll.Count(row => string.IsNullOrWhiteSpace(row.PlayerTeamName)),
            ambiguous = candidateRowsAll.Count(row => row.SuggestedAction == "Ambiguous"),
            noCandidate = candidateRowsAll.Count(row => row.SuggestedAction == "NoCandidate"),
            insufficientTokenOverlap = candidateRowsAll.Count(row => row.Blockers.Contains("InsufficientTokenOverlap", StringComparer.OrdinalIgnoreCase)),
            belowMinimumConfidence = candidateRowsAll.Count(row => row.SuggestedAction == "SafeAliasCandidate" && row.Confidence < minimumConfidence),
            existingConflicts = linkPlans.SelectMany(group => group.Plan.Skipped).Count(row => row.Reason == "ExistingLinkConflict")
        };

        return new TheOddsApiSportsAliasBatch(
            resolvedPairs.Length,
            candidateRowsAll.Length,
            candidateRowsAll.Count(row => row.SuggestedAction == "SafeAliasCandidate"),
            candidateSummary,
            estimatedCoverageImpact,
            candidateGroups
                .OrderBy(group => group.EventDate)
                .ThenBy(group => group.EventName)
                .ThenBy(group => group.ProviderEventId)
                .Take(sampleLimit)
                .ToArray(),
            deduplicatedCandidates,
            blockers,
            linkPlans,
            BuildTheOddsApiSportsAliasBatchRecommendation(candidateRowsAll, proposedLinks.Length),
            candidateRowsAll.Count(row => row.SuggestedAction is "SafeAliasCandidate" or "NeedsReview" or "Ambiguous"),
            proposedLinks.Length,
            candidateRowsAll.Count(row => row.SuggestedAction == "NeedsReview" && !IsApproved(row)),
            candidateRowsAll.Count(row => row.SuggestedAction == "NoCandidate" && !IsApproved(row)),
            candidateRowsAll.Count(row => row.SuggestedAction == "Ambiguous" && !IsApproved(row)));
    }

    private static IReadOnlyList<object> BuildTheOddsApiSportsAliasBatchDeduplicatedCandidates(
        IReadOnlyList<(ApiSportsSoccerFixtureMappingEvent Pair, TheOddsApiSportsAliasCandidateRow Row)> rows,
        int sampleLimit)
    {
        return rows
            .Where(row => row.Row.Candidates.Count == 1 && row.Row.SuggestedAction != "NoCandidate")
            .GroupBy(
                row => new
                {
                    row.Row.NormalizedPlatformPlayerName,
                    row.Row.Candidates[0].LocalPlayerId
                })
            .OrderByDescending(group => group.Max(row => row.Row.Confidence))
            .ThenBy(group => group.First().Row.PlatformPlayerName)
            .Take(sampleLimit)
            .Select(group =>
            {
                var first = group.OrderByDescending(row => row.Row.Confidence).First();
                var candidate = first.Row.Candidates[0];
                return new
                {
                    platformPlayerName = first.Row.PlatformPlayerName,
                    normalizedPlatformPlayerName = first.Row.NormalizedPlatformPlayerName,
                    candidateApiSportsPlayerName = candidate.ApiSportsPlayerName,
                    candidateLocalPlayerId = candidate.LocalPlayerId,
                    candidateTeamName = candidate.TeamName,
                    confidence = group.Max(row => row.Row.Confidence),
                    suggestedActions = group.Select(row => row.Row.SuggestedAction).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    occurrences = group.Count(),
                    fixtureIds = group.Select(row => row.Pair.ResolvedFixtureId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    providerEventIds = group.Select(row => row.Pair.ProviderEventId).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    matchReasons = group.SelectMany(row => row.Row.Candidates[0].MatchReasons).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    blockers = group.SelectMany(row => row.Row.Blockers).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()
                };
            })
            .ToArray();
    }

    private static string BuildTheOddsApiSportsAliasBatchRecommendation(
        IReadOnlyList<TheOddsApiSportsAliasCandidateRow> rows,
        int proposedLinks)
    {
        if (proposedLinks > 0)
        {
            return "Batch safe alias candidates are available. Run alias-link-batch with dryRun=true first, review proposedLinks, then rerun with dryRun=false only for safe candidates.";
        }

        if (rows.Any(row => row.SuggestedAction == "Ambiguous"))
        {
            return "Ambiguous aliases remain blocked. Do not bulk approve these without stronger team/player context.";
        }

        if (rows.Any(row => row.SuggestedAction == "NeedsReview"))
        {
            return "NeedsReview aliases remain blocked from batch persistence. Continue manual review or improve provider team/player context.";
        }

        return "No new safe batch alias candidates were found for the resolved fixture pairs.";
    }

    private static string BuildTheOddsApiSportsManualAliasApprovalRecommendation(
        bool dryRun,
        int proposedCount,
        int insertedCount,
        IReadOnlyList<TheOddsApiSportsAliasLinkSkippedRow> skipped)
    {
        if (dryRun && proposedCount == 1)
        {
            return "Dry run validated one NeedsReview alias candidate. Review proposedLink and rerun with dryRun=false only if the identity was manually confirmed.";
        }

        if (!dryRun && insertedCount == 1)
        {
            return "Manual alias approval was written. Rerun Soccer grading coverage to confirm player-name mismatch decreased. Public Soccer grading remains disabled.";
        }

        var reason = skipped.FirstOrDefault()?.Reason;
        return reason switch
        {
            "AlreadyLinked" => "This alias is already approved for the requested player.",
            "CandidateIdMismatch" => "The supplied API-Sports/local player ids do not match the current diagnostic candidate. Re-run alias-candidates before approving.",
            "Ambiguous" => "Ambiguous aliases cannot be manually approved through this endpoint.",
            "NoCandidate" => "No-candidate rows cannot be approved.",
            "SafeAliasCandidate" => "This row is already safe; use the normal dry-run-first alias-link endpoint.",
            "ExistingLinkConflict" => "An existing provider link points at a different canonical player. Resolve the conflict before approving.",
            _ => "No manual alias approval was written."
        };
    }

    private static async Task<TheOddsApiSportsAliasLinkPlan> BuildTheOddsApiSportsAliasLinkPlanAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<TheOddsApiSportsAliasCandidateRow> candidateRows,
        string fixtureId,
        string providerEventId,
        decimal minConfidence,
        bool includeExactNormalizedMatches,
        bool includeTokenSubsetMatches,
        int maxLinks,
        CancellationToken cancellationToken)
    {
        var skipped = new List<TheOddsApiSportsAliasLinkSkippedRow>();
        var eligible = new List<TheOddsApiSportsAliasLinkAuditRow>();

        foreach (var row in candidateRows)
        {
            if (row.SuggestedAction != "SafeAliasCandidate")
            {
                skipped.Add(BuildTheOddsApiSportsAliasSkippedRow(row, row.SuggestedAction));
                continue;
            }

            if (row.Confidence < minConfidence)
            {
                skipped.Add(BuildTheOddsApiSportsAliasSkippedRow(row, "BelowMinimumConfidence"));
                continue;
            }

            if (row.ExactNormalizedMatch && !includeExactNormalizedMatches)
            {
                skipped.Add(BuildTheOddsApiSportsAliasSkippedRow(row, "ExactNormalizedMatchExcluded"));
                continue;
            }

            var candidate = row.Candidates.Count == 1 ? row.Candidates[0] : null;
            if (candidate is null)
            {
                skipped.Add(BuildTheOddsApiSportsAliasSkippedRow(row, "Ambiguous"));
                continue;
            }

            if (!row.ExactNormalizedMatch
                && !includeTokenSubsetMatches
                && candidate.MatchReasons.Any(reason => reason is "ApiSportsTokensSubsetOfTheOddsName" or "TheOddsTokensSubsetOfApiSportsName" or "NormalizedContainment"))
            {
                skipped.Add(BuildTheOddsApiSportsAliasSkippedRow(row, "TokenSubsetMatchExcluded"));
                continue;
            }

            eligible.Add(BuildTheOddsApiSportsAliasLinkAuditRow(row, candidate, providerEventId));
        }

        var providerEntityIds = eligible
            .Select(row => row.ProviderEntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existingLinks = providerEntityIds.Length == 0
            ? []
            : await dbContext.ProviderEntityLinks
                .AsNoTracking()
                .Where(link => link.ProviderKey == TheOddsSoccerAliasProviderKey
                    && link.EntityType == "Player"
                    && providerEntityIds.Contains(link.ProviderEntityId))
                .Select(link => new
                {
                    link.ProviderEntityId,
                    link.CanonicalEntityId,
                    link.CanonicalEntityName,
                    link.LinkStatus
                })
                .ToListAsync(cancellationToken);
        var existingByProviderEntityId = existingLinks
            .GroupBy(link => link.ProviderEntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var proposed = new List<TheOddsApiSportsAliasLinkAuditRow>();
        var alreadyExisting = 0;

        foreach (var row in eligible)
        {
            if (existingByProviderEntityId.TryGetValue(row.ProviderEntityId, out var links))
            {
                var approved = links.FirstOrDefault(link => string.Equals(link.LinkStatus, "Approved", StringComparison.OrdinalIgnoreCase));
                if (approved is not null && approved.CanonicalEntityId == row.LocalPlayerId)
                {
                    alreadyExisting++;
                    continue;
                }

                skipped.Add(new TheOddsApiSportsAliasLinkSkippedRow(
                    row.PlatformPlayerName,
                    row.NormalizedPlatformPlayerName,
                    row.ProviderEventId,
                    row.Confidence,
                    "ExistingLinkConflict",
                    row.Blockers,
                    row.CandidateApiSportsPlayerName,
                    row.LocalPlayerId,
                    approved?.CanonicalEntityName));
                continue;
            }

            proposed.Add(row);
            if (proposed.Count >= maxLinks)
            {
                break;
            }
        }

        return new TheOddsApiSportsAliasLinkPlan(proposed, skipped, alreadyExisting);
    }

    private static TheOddsApiSportsAliasLinkSkippedRow BuildTheOddsApiSportsAliasSkippedRow(
        TheOddsApiSportsAliasCandidateRow row,
        string reason)
    {
        var candidate = row.Candidates.FirstOrDefault();
        return new TheOddsApiSportsAliasLinkSkippedRow(
            row.PlatformPlayerName,
            row.NormalizedPlatformPlayerName,
            row.ProviderEventId,
            row.Confidence,
            reason,
            row.Blockers,
            candidate?.ApiSportsPlayerName,
            candidate?.LocalPlayerId,
            null);
    }

    private static TheOddsApiSportsAliasLinkAuditRow BuildTheOddsApiSportsAliasLinkAuditRow(
        TheOddsApiSportsAliasCandidateRow row,
        TheOddsApiSportsAliasCandidateScore candidate,
        string providerEventId)
    {
        var providerEntityId = BuildTheOddsSoccerAliasProviderEntityId(providerEventId, row.NormalizedPlatformPlayerName);
        return new TheOddsApiSportsAliasLinkAuditRow(
            providerEntityId,
            row.PlatformPlayerName,
            row.NormalizedPlatformPlayerName,
            providerEventId,
            row.EventName,
            row.EventStartUtc,
            row.HomeTeamName,
            row.AwayTeamName,
            row.PlayerTeamName,
            candidate.ApiSportsPlayerId,
            candidate.LocalPlayerId,
            candidate.ApiSportsPlayerName,
            candidate.NormalizedPlayerName,
            candidate.TeamName,
            row.Confidence,
            row.ExactNormalizedMatch,
            candidate.MatchReasons,
            row.Blockers);
    }

    private static async Task<TheOddsApiSportsAliasLinkAuditRow[]> WriteTheOddsApiSportsAliasLinksAsync(
        ApplicationDbContext dbContext,
        string fixtureId,
        string providerEventId,
        IReadOnlyList<TheOddsApiSportsAliasLinkAuditRow> proposedLinks,
        CancellationToken cancellationToken,
        string? approvalReason = null,
        string linkSource = "DebugTheOddsApiSportsAliasLink",
        string dryRunFirstEndpoint = "/api/debug/soccer/player-identity/the-odds/api-sports/alias-link",
        string approvedBy = "debug/alias-link")
    {
        if (proposedLinks.Count == 0)
        {
            return [];
        }

        var sport = await dbContext.Sports.SingleAsync(sport => sport.Code == "SOCCER", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var providerEntityIds = proposedLinks.Select(link => link.ProviderEntityId).ToArray();
        var existingProviderEntityIds = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == TheOddsSoccerAliasProviderKey
                && link.EntityType == "Player"
                && providerEntityIds.Contains(link.ProviderEntityId))
            .Select(link => link.ProviderEntityId)
            .ToListAsync(cancellationToken);
        var existing = existingProviderEntityIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inserted = new List<TheOddsApiSportsAliasLinkAuditRow>();

        foreach (var link in proposedLinks.Where(link => !existing.Contains(link.ProviderEntityId)))
        {
            dbContext.ProviderEntityLinks.Add(new ProviderEntityLink
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                EntityType = "Player",
                CanonicalEntityId = link.LocalPlayerId,
                CanonicalEntityKind = "Player",
                CanonicalEntityName = link.CandidateApiSportsPlayerName,
                ProviderKey = TheOddsSoccerAliasProviderKey,
                ProviderEntityId = link.ProviderEntityId,
                ProviderEntityName = link.PlatformPlayerName,
                ProviderTeamName = link.PlayerTeamName,
                ProviderCompetitionId = "soccer_fifa_world_cup",
                ProviderCompetitionName = "World Cup",
                ProviderEventId = providerEventId,
                EventStartUtc = link.EventStartUtc,
                Confidence = link.Confidence,
                LinkStatus = "Approved",
                LinkSource = linkSource,
                MetadataJson = JsonSerializer.SerializeToDocument(new
                {
                    sourceProvider = "The Odds API",
                    sourceProviderKey = TheOddsSoccerAliasProviderKey,
                    sourceSportKey = "soccer_fifa_world_cup",
                    sourceProviderEventId = providerEventId,
                    sourcePlatformPlayerName = link.PlatformPlayerName,
                    sourceNormalizedPlatformPlayerName = link.NormalizedPlatformPlayerName,
                    targetProvider = "API-Sports",
                    targetProduct = "API-Sports Football",
                    targetFixtureId = fixtureId,
                    targetApiSportsPlayerId = link.ApiSportsPlayerId,
                    targetLocalPlayerId = link.LocalPlayerId,
                    targetPlayerName = link.CandidateApiSportsPlayerName,
                    confidence = link.Confidence,
                    exactNormalizedMatch = link.ExactNormalizedMatch,
                    matchReasons = link.MatchReasons,
                    blockers = link.Blockers,
                    approvalReason,
                    manualApproval = !string.IsNullOrWhiteSpace(approvalReason)
                        || string.Equals(linkSource, "DebugTheOddsApiSportsManualAliasApproval", StringComparison.OrdinalIgnoreCase),
                    publicSoccerGradingEnabled = false,
                    dryRunFirstEndpoint,
                    approvedAtUtc = now
                }),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ApprovedAtUtc = now,
                ApprovedBy = approvedBy
            });
            inserted.Add(link);
        }

        if (inserted.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return inserted.ToArray();
    }

    private static string BuildTheOddsApiSportsAliasLinkRecommendation(
        bool dryRun,
        int proposedCount,
        int insertedCount,
        IReadOnlyList<TheOddsApiSportsAliasLinkSkippedRow> skipped)
    {
        if (dryRun && proposedCount > 0)
        {
            return "Dry run found safe alias links. Review proposedLinks, then rerun with dryRun=false to persist approved debug links.";
        }

        if (!dryRun && insertedCount > 0)
        {
            return "Approved alias links were written. Rerun the Soccer grading preview to verify join coverage improved. Public Soccer grading remains disabled.";
        }

        if (skipped.Any(row => row.Reason == "BelowMinimumConfidence"))
        {
            return "No links were written because candidates were below the requested minimum confidence.";
        }

        if (skipped.Any(row => row.Reason is "Ambiguous" or "NeedsReview"))
        {
            return "Only SafeAliasCandidate rows can be persisted. Review skipped rows manually before explicit single-link approval.";
        }

        return "No new alias links were available to persist.";
    }


    private static async Task<ApiSportsSoccerResultMappingSet> LoadApiSportsSoccerResultMappingsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var mappings = await dbContext.ProviderStatMappings
            .AsNoTracking()
            .Include(mapping => mapping.Sport)
            .Where(mapping => mapping.IsActive
                && mapping.ProviderKey == ApiSportsFootballResultSource
                && mapping.Sport != null
                && mapping.Sport.Code == "SOCCER"
                && mapping.CompetitionScope == "World Cup"
                && mapping.Season == ApiSportsWorldCupSeason)
            .OrderBy(mapping => mapping.DisplayName)
            .ToListAsync(cancellationToken);

        if (mappings.Count == 0)
        {
            return new ApiSportsSoccerResultMappingSet(
                ApiSportsSoccerResultMappings,
                "Fallback",
                0,
                0,
                ApiSportsSoccerResultMappings.Length);
        }

        var importedMappings = mappings.Select(ToApiSportsSoccerResultMapping).ToArray();
        return new ApiSportsSoccerResultMappingSet(
            importedMappings,
            "Database",
            mappings.Count,
            importedMappings.Length,
            0);
    }

    private static ApiSportsSoccerResultMapping ToApiSportsSoccerResultMapping(ProviderStatMapping mapping)
    {
        var sourceFields = ExtractJsonStringArray(mapping.ProviderStatFieldsJson);
        var transform = mapping.ValueTransform.Trim();
        var importSupported = !string.Equals(transform, "Unsupported", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transform, "ManualOnly", StringComparison.OrdinalIgnoreCase)
            && sourceFields.Length > 0;
        var goalieOnly = transform.StartsWith("GoalkeeperOnly", StringComparison.OrdinalIgnoreCase);

        return new ApiSportsSoccerResultMapping(
            mapping.DisplayName,
            mapping.CanonicalStatKey,
            sourceFields,
            importSupported,
            goalieOnly,
            mapping.RuleConfidence,
            mapping.IsGradeable,
            mapping.PublicGradingEnabled,
            "Database",
            mapping.Notes ?? string.Empty);
    }

    private static object BuildApiSportsSoccerMappingDiagnostics(ApiSportsSoccerResultPreview preview)
    {
        var missingMappingCount = preview.UnsupportedSkipped.Count(row => string.Equals(row.Reason, "MissingMapping", StringComparison.OrdinalIgnoreCase));
        return new
        {
            mappingSource = preview.MappingSource,
            providerMappingsConfigured = preview.DbMappingsConfigured,
            dbMappingsUsed = preview.DbMappingsUsed,
            fallbackMappingsUsed = preview.FallbackMappingsUsed,
            missingMappingCount,
            mappingsByConfidence = preview.Rows
                .GroupBy(row => row.RuleConfidence)
                .OrderBy(group => group.Key)
                .Select(group => new { ruleConfidence = group.Key, rowCount = group.Count() })
                .ToArray(),
            mappingsByGradeable = preview.Rows
                .GroupBy(row => row.IsGradeable)
                .OrderByDescending(group => group.Key)
                .Select(group => new { isGradeable = group.Key, rowCount = group.Count() })
                .ToArray()
        };
    }

    private static string[] ExtractJsonStringArray(JsonDocument? document)
    {
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return document.RootElement
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static ApiSportsSoccerResultPreview BuildApiSportsSoccerResultPreview(
        string fixtureId,
        DateOnly eventDate,
        IReadOnlyList<ApiSportsPlayerStatsRow> playerStats,
        ApiSportsSoccerResultMappingSet mappingSet)
    {
        var rows = new List<ApiSportsSoccerResultPreviewRow>();
        var skipped = new List<ApiSportsSoccerUnsupportedSkippedRow>();
        var mappedStatFields = mappingSet.Mappings
            .SelectMany(mapping => mapping.SourceFields)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var player in playerStats)
        {
            if (player.Minutes.GetValueOrDefault() <= 0)
            {
                skipped.Add(new ApiSportsSoccerUnsupportedSkippedRow(
                    player.PlayerId,
                    player.PlayerName,
                    "Player",
                    "PlayerMissingMinutes",
                    "Player has null or zero minutes and was not converted to result rows."));
                continue;
            }

            foreach (var statKey in player.StatKeys.Where(key => !mappedStatFields.Contains(key)).Order(StringComparer.OrdinalIgnoreCase))
            {
                skipped.Add(new ApiSportsSoccerUnsupportedSkippedRow(
                    player.PlayerId,
                    player.PlayerName,
                    statKey,
                    "MissingMapping",
                    "No active provider_stat_mappings row maps this API-Sports Football stat field."));
            }

            foreach (var mapping in mappingSet.Mappings)
            {
                if (!mapping.ImportSupported)
                {
                    skipped.Add(new ApiSportsSoccerUnsupportedSkippedRow(
                        player.PlayerId,
                        player.PlayerName,
                        mapping.PropType,
                        "UnsupportedPropType",
                        mapping.Notes));
                    continue;
                }

                if (mapping.GoalieOnly && !IsGoalkeeper(player.Position))
                {
                    skipped.Add(new ApiSportsSoccerUnsupportedSkippedRow(
                        player.PlayerId,
                        player.PlayerName,
                        mapping.PropType,
                        "GoalieOnlyPositionMismatch",
                        $"{mapping.PropType} is only imported for goalkeepers with minutes."));
                    continue;
                }

                if (!TryComputeApiSportsSoccerValue(player.RawStats, mapping.SourceFields, out var value, out var rawValue))
                {
                    skipped.Add(new ApiSportsSoccerUnsupportedSkippedRow(
                        player.PlayerId,
                        player.PlayerName,
                        mapping.PropType,
                        "MissingSourceField",
                        $"Required source field(s) missing: {string.Join(", ", mapping.SourceFields)}."));
                    continue;
                }

                rows.Add(new ApiSportsSoccerResultPreviewRow(
                    fixtureId,
                    eventDate,
                    player.TeamId,
                    player.TeamName,
                    player.PlayerId,
                    player.PlayerName,
                    player.Position,
                    player.Minutes,
                    mapping.PropType,
                    mapping.StatKey,
                    mapping.SourceFields,
                    rawValue,
                    value,
                    mapping.RuleConfidence,
                    mapping.IsGradeable,
                    mapping.PublicGradingEnabled,
                    mapping.MappingSource,
                    mapping.Notes,
                    $"{ApiSportsFootballResultSource}:{fixtureId}:{player.PlayerId}:{mapping.StatKey}"));
            }
        }

        return new ApiSportsSoccerResultPreview(rows, skipped, mappingSet.MappingSource, mappingSet.DbMappingsConfigured, mappingSet.DbMappingsUsed, mappingSet.FallbackMappingsUsed);
    }

    private static async Task<ApiSportsSoccerImportWriteSummary> UpsertApiSportsSoccerResultsAsync(
        ApplicationDbContext dbContext,
        ApiSportsFixtureSummary fixtureSummary,
        IReadOnlyList<ApiSportsSoccerResultPreviewRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return new ApiSportsSoccerImportWriteSummary(0, 0);
        }

        var observedAt = DateTimeOffset.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var sport = await GetOrCreateSportAsync(dbContext, "Soccer", "SOCCER", cancellationToken);
        var league = await GetOrCreateLeagueAsync(dbContext, sport, fixtureSummary.LeagueName ?? "World Cup", "WORLD_CUP", cancellationToken);
        var teamByProviderId = await UpsertApiSportsTeamsAsync(dbContext, league, fixtureSummary, rows, observedAt, cancellationToken);
        var playerByProviderId = await UpsertApiSportsPlayersAsync(dbContext, league, teamByProviderId, rows, observedAt, cancellationToken);
        await UpsertApiSportsProviderPlayerLinksAsync(dbContext, sport, fixtureSummary, rows, observedAt, cancellationToken);
        var propTypeByStatKey = await GetOrCreatePropTypesAsync(dbContext, sport, rows.Select(row => (row.PropType, row.StatKey)).Distinct().ToArray(), cancellationToken);
        await AttachApiSportsMappingsToCanonicalPropTypesAsync(dbContext, sport, propTypeByStatKey, observedAt, cancellationToken);
        var identifiers = rows.Select(row => row.ExternalSourceIdentifier).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existingResults = await dbContext.PropResults
            .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                && identifiers.Contains(result.ExternalSourceIdentifier))
            .ToDictionaryAsync(result => result.ExternalSourceIdentifier, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var inserted = 0;
        var updated = 0;
        foreach (var row in rows)
        {
            if (!row.PlayerId.HasValue || !playerByProviderId.TryGetValue(row.PlayerId.Value, out var player))
            {
                continue;
            }

            var propType = propTypeByStatKey[row.StatKey];
            if (existingResults.TryGetValue(row.ExternalSourceIdentifier, out var existing))
            {
                var changed = false;
                if (existing.ActualResultValue != row.Value)
                {
                    existing.ActualResultValue = row.Value;
                    changed = true;
                }

                if (existing.PlayerId != player.Id)
                {
                    existing.PlayerId = player.Id;
                    changed = true;
                }

                if (existing.LeagueId != league.Id)
                {
                    existing.LeagueId = league.Id;
                    changed = true;
                }

                if (existing.PropTypeId != propType.Id)
                {
                    existing.PropTypeId = propType.Id;
                    changed = true;
                }

                if (existing.EventDate != row.EventDate)
                {
                    existing.EventDate = row.EventDate;
                    changed = true;
                }

                if (changed)
                {
                    existing.UpdatedAt = observedAt;
                    updated++;
                }

                continue;
            }

            var result = new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                LeagueId = league.Id,
                PropTypeId = propType.Id,
                EventDate = row.EventDate,
                ActualResultValue = row.Value,
                ExternalSource = ApiSportsFootballResultSource,
                ExternalSourceIdentifier = row.ExternalSourceIdentifier,
                CreatedAt = observedAt,
                UpdatedAt = observedAt
            };
            dbContext.PropResults.Add(result);
            existingResults[row.ExternalSourceIdentifier] = result;
            inserted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ApiSportsSoccerImportWriteSummary(inserted, updated);
    }

    private static async Task AttachApiSportsMappingsToCanonicalPropTypesAsync(
        ApplicationDbContext dbContext,
        Sport sport,
        IReadOnlyDictionary<string, PropType> propTypeByStatKey,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        if (propTypeByStatKey.Count == 0)
        {
            return;
        }

        var statKeys = propTypeByStatKey.Keys.ToArray();
        var mappings = await dbContext.ProviderStatMappings
            .Where(mapping => mapping.IsActive
                && mapping.SportId == sport.Id
                && mapping.ProviderKey == ApiSportsFootballResultSource
                && statKeys.Contains(mapping.CanonicalStatKey))
            .ToListAsync(cancellationToken);

        foreach (var mapping in mappings)
        {
            if (!propTypeByStatKey.TryGetValue(mapping.CanonicalStatKey, out var propType))
            {
                continue;
            }

            if (mapping.CanonicalPropTypeId != propType.Id)
            {
                mapping.CanonicalPropTypeId = propType.Id;
                mapping.UpdatedAtUtc = observedAt;
            }
        }
    }

    private static async Task<Sport> GetOrCreateSportAsync(
        ApplicationDbContext dbContext,
        string name,
        string code,
        CancellationToken cancellationToken)
    {
        var sport = await dbContext.Sports.SingleOrDefaultAsync(row => row.Code == code, cancellationToken);
        if (sport is not null)
        {
            return sport;
        }

        sport = new Sport { Id = Guid.NewGuid(), Name = name, Code = code };
        dbContext.Sports.Add(sport);
        await dbContext.SaveChangesAsync(cancellationToken);
        return sport;
    }

    private static async Task<League> GetOrCreateLeagueAsync(
        ApplicationDbContext dbContext,
        Sport sport,
        string name,
        string code,
        CancellationToken cancellationToken)
    {
        var league = await dbContext.Leagues.SingleOrDefaultAsync(row => row.Code == code, cancellationToken);
        if (league is not null)
        {
            return league;
        }

        league = new League { Id = Guid.NewGuid(), SportId = sport.Id, Name = name, Code = code };
        dbContext.Leagues.Add(league);
        await dbContext.SaveChangesAsync(cancellationToken);
        return league;
    }

    private static async Task<Dictionary<int, Team>> UpsertApiSportsTeamsAsync(
        ApplicationDbContext dbContext,
        League league,
        ApiSportsFixtureSummary fixtureSummary,
        IReadOnlyList<ApiSportsSoccerResultPreviewRow> rows,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var candidates = new List<(int? TeamId, string? TeamName)>
        {
            (fixtureSummary.HomeTeamId, fixtureSummary.HomeTeamName),
            (fixtureSummary.AwayTeamId, fixtureSummary.AwayTeamName)
        };
        candidates.AddRange(rows.Select(row => (row.TeamId, row.TeamName)));
        var distinct = candidates
            .Where(row => row.TeamId.HasValue && !string.IsNullOrWhiteSpace(row.TeamName))
            .GroupBy(row => row.TeamId!.Value)
            .Select(group => group.First())
            .ToArray();
        var ids = distinct.Select(row => row.TeamId!.Value.ToString()).ToArray();
        var existingTeams = await dbContext.Teams
            .Where(team => team.LeagueId == league.Id)
            .ToListAsync(cancellationToken);
        var byProviderId = existingTeams
            .Select(team => new { Team = team, ProviderId = ExtractExternalRefValue(team.ExternalRefs, "apiSportsFootballTeamId") })
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderId))
            .ToDictionary(row => int.Parse(row.ProviderId!), row => row.Team);

        foreach (var candidate in distinct)
        {
            if (byProviderId.TryGetValue(candidate.TeamId!.Value, out var existing))
            {
                if (!string.Equals(existing.Name, candidate.TeamName, StringComparison.Ordinal))
                {
                    existing.Name = candidate.TeamName!;
                }

                continue;
            }

            var team = existingTeams.FirstOrDefault(row => string.Equals(row.Name, candidate.TeamName, StringComparison.OrdinalIgnoreCase));
            if (team is null)
            {
                team = new Team { Id = Guid.NewGuid(), LeagueId = league.Id, Name = candidate.TeamName! };
                dbContext.Teams.Add(team);
                existingTeams.Add(team);
            }

            team.ExternalRefs = JsonSerializer.SerializeToDocument(new
            {
                provider = "API-Sports",
                product = "API-Sports Football",
                apiSportsFootballTeamId = candidate.TeamId.Value.ToString(),
                updatedAtUtc = observedAt
            });
            byProviderId[candidate.TeamId.Value] = team;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return byProviderId;
    }

    private static async Task<Dictionary<int, Player>> UpsertApiSportsPlayersAsync(
        ApplicationDbContext dbContext,
        League league,
        IReadOnlyDictionary<int, Team> teamByProviderId,
        IReadOnlyList<ApiSportsSoccerResultPreviewRow> rows,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var candidates = rows
            .Where(row => row.PlayerId.HasValue && !string.IsNullOrWhiteSpace(row.PlayerName))
            .GroupBy(row => row.PlayerId!.Value)
            .Select(group => group.First())
            .ToArray();
        var providerIds = candidates.Select(row => row.PlayerId!.Value.ToString()).ToArray();
        var refs = await dbContext.PlayerExternalRefs
            .Include(reference => reference.Player)
            .Where(reference => reference.Provider == ApiSportsFootballPlayerRefProvider
                && providerIds.Contains(reference.ProviderPlayerId))
            .ToListAsync(cancellationToken);
        var byProviderId = refs
            .Where(reference => int.TryParse(reference.ProviderPlayerId, out _) && reference.Player is not null)
            .ToDictionary(reference => int.Parse(reference.ProviderPlayerId), reference => reference.Player!);

        foreach (var candidate in candidates)
        {
            var providerId = candidate.PlayerId!.Value;
            var teamId = candidate.TeamId.HasValue && teamByProviderId.TryGetValue(candidate.TeamId.Value, out var team)
                ? team.Id
                : (Guid?)null;

            if (byProviderId.TryGetValue(providerId, out var existingPlayer))
            {
                existingPlayer.DisplayName = candidate.PlayerName!;
                existingPlayer.TeamId = teamId;
                existingPlayer.Position = candidate.Position;
                existingPlayer.UpdatedAt = observedAt;
                continue;
            }

            var player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = candidate.PlayerName!,
                LeagueId = league.Id,
                TeamId = teamId,
                Position = candidate.Position,
                CreatedAt = observedAt,
                UpdatedAt = observedAt,
                ExternalRefs = JsonSerializer.SerializeToDocument(new
                {
                    provider = "API-Sports",
                    product = "API-Sports Football",
                    apiSportsFootballPlayerId = providerId.ToString()
                })
            };
            dbContext.Players.Add(player);
            dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Provider = ApiSportsFootballPlayerRefProvider,
                ProviderPlayerId = providerId.ToString(),
                ProviderDisplayName = candidate.PlayerName!,
                CreatedAt = observedAt,
                UpdatedAt = observedAt
            });
            byProviderId[providerId] = player;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return byProviderId;
    }

    private static async Task UpsertApiSportsProviderPlayerLinksAsync(
        ApplicationDbContext dbContext,
        Sport sport,
        ApiSportsFixtureSummary fixtureSummary,
        IReadOnlyList<ApiSportsSoccerResultPreviewRow> rows,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var candidates = rows
            .Where(row => row.PlayerId.HasValue && !string.IsNullOrWhiteSpace(row.PlayerName))
            .GroupBy(row => row.PlayerId!.Value)
            .Select(group =>
            {
                var first = group.First();
                return new
                {
                    ProviderPlayerId = first.PlayerId!.Value.ToString(),
                    ProviderPlayerName = first.PlayerName!,
                    ProviderTeamId = first.TeamId?.ToString(),
                    ProviderTeamName = first.TeamName,
                    FixtureIds = group.Select(row => row.FixtureId).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    ResultRowCount = group.Count()
                };
            })
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var providerPlayerIds = candidates.Select(candidate => candidate.ProviderPlayerId).ToArray();
        var existingLinks = await dbContext.ProviderEntityLinks
            .Where(link => link.SportId == sport.Id
                && link.ProviderKey == ApiSportsFootballPlayerRefProvider
                && link.EntityType == "Player"
                && providerPlayerIds.Contains(link.ProviderEntityId))
            .ToListAsync(cancellationToken);
        var linksByProviderId = existingLinks
            .GroupBy(link => link.ProviderEntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            linksByProviderId.TryGetValue(candidate.ProviderPlayerId, out var links);
            var existing = links?.FirstOrDefault(link => string.Equals(link.LinkStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                ?? links?.FirstOrDefault(link => !string.Equals(link.LinkStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                ?? links?.FirstOrDefault();
            var metadata = JsonSerializer.SerializeToDocument(new
            {
                provider = "API-Sports",
                product = "API-Sports Football",
                importSource = "WorldCupResultImport",
                fixtureIds = candidate.FixtureIds,
                resultRowCount = candidate.ResultRowCount,
                fixtureSummary.FixtureId,
                fixtureSummary.EventDate,
                fixtureSummary.HomeTeamId,
                fixtureSummary.HomeTeamName,
                fixtureSummary.AwayTeamId,
                fixtureSummary.AwayTeamName,
                updatedAtUtc = observedAt
            });

            if (existing is not null)
            {
                existing.ProviderEntityName = candidate.ProviderPlayerName;
                existing.ProviderTeamId = candidate.ProviderTeamId;
                existing.ProviderTeamName = candidate.ProviderTeamName;
                existing.ProviderCompetitionId = ApiSportsWorldCupLeagueId.ToString();
                existing.ProviderCompetitionName = fixtureSummary.LeagueName ?? "World Cup";
                existing.ProviderEventId ??= fixtureSummary.FixtureId;
                existing.EventStartUtc ??= fixtureSummary.KickoffUtc;
                existing.MetadataJson = metadata;
                existing.UpdatedAtUtc = observedAt;
                continue;
            }

            dbContext.ProviderEntityLinks.Add(new ProviderEntityLink
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                EntityType = "Player",
                CanonicalEntityId = null,
                CanonicalEntityKind = null,
                CanonicalEntityName = null,
                ProviderKey = ApiSportsFootballPlayerRefProvider,
                ProviderEntityId = candidate.ProviderPlayerId,
                ProviderEntityName = candidate.ProviderPlayerName,
                ProviderTeamId = candidate.ProviderTeamId,
                ProviderTeamName = candidate.ProviderTeamName,
                ProviderCompetitionId = ApiSportsWorldCupLeagueId.ToString(),
                ProviderCompetitionName = fixtureSummary.LeagueName ?? "World Cup",
                ProviderEventId = fixtureSummary.FixtureId,
                EventStartUtc = fixtureSummary.KickoffUtc,
                Confidence = null,
                LinkStatus = "Candidate",
                LinkSource = "Import",
                MetadataJson = metadata,
                CreatedAtUtc = observedAt,
                UpdatedAtUtc = observedAt
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, PropType>> GetOrCreatePropTypesAsync(
        ApplicationDbContext dbContext,
        Sport sport,
        IReadOnlyList<(string PropType, string StatKey)> propTypes,
        CancellationToken cancellationToken)
    {
        var requested = ResolveRequestedPropTypeDescriptors(propTypes);
        var statKeys = requested.Select(row => row.StatKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingForSport = await dbContext.PropTypes
            .Where(propType => propType.SportId == sport.Id)
            .ToListAsync(cancellationToken);
        var existingConflicts = BuildExistingPropTypeConflicts(existingForSport, statKeys);
        if (existingConflicts.Count > 0)
        {
            throw new ApiSportsPropTypeResolutionException(existingConflicts);
        }

        var byStatKey = existingForSport
            .Where(row => statKeys.Contains(row.StatKey))
            .GroupBy(row => row.StatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in requested)
        {
            if (byStatKey.ContainsKey(candidate.StatKey))
            {
                continue;
            }

            var propType = new PropType
            {
                Id = Guid.NewGuid(),
                SportId = sport.Id,
                Name = candidate.Name,
                StatKey = candidate.StatKey,
                ValueType = candidate.ValueType
            };
            dbContext.PropTypes.Add(propType);
            byStatKey[candidate.StatKey] = propType;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return byStatKey;
    }

    private static IReadOnlyList<ApiSportsPropTypeDescriptor> ResolveRequestedPropTypeDescriptors(
        IReadOnlyList<(string PropType, string StatKey)> propTypes)
    {
        var descriptors = propTypes
            .Where(row => !string.IsNullOrWhiteSpace(row.PropType))
            .Select(row => new ApiSportsPropTypeDescriptor(row.PropType.Trim(), row.StatKey.Trim(), "numeric"))
            .ToArray();
        var conflicts = descriptors
            .GroupBy(row => row.StatKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var values = group
                    .Select(row => new { row.Name, row.ValueType })
                    .Distinct()
                    .ToArray();
                return values.Length <= 1
                    ? null
                    : new ApiSportsPropTypeResolutionConflict(
                        group.Key,
                        "RequestedDescriptors",
                        values.Select(value => new ApiSportsPropTypeMetadata(null, value.Name, value.ValueType)).ToArray());
            })
            .Where(conflict => conflict is not null)
            .Cast<ApiSportsPropTypeResolutionConflict>()
            .ToArray();

        if (conflicts.Length > 0)
        {
            throw new ApiSportsPropTypeResolutionException(conflicts);
        }

        return descriptors
            .GroupBy(row => row.StatKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<ApiSportsPropTypeResolutionConflict> BuildExistingPropTypeConflicts(
        IReadOnlyList<PropType> existingForSport,
        ISet<string> requestedStatKeys)
    {
        return existingForSport
            .Where(row => requestedStatKeys.Contains(row.StatKey))
            .GroupBy(row => row.StatKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var metadata = group
                    .Select(row => new ApiSportsPropTypeMetadata(row.Id, row.Name, row.ValueType))
                    .Distinct()
                    .ToArray();
                return metadata.Length <= 1
                    ? null
                    : new ApiSportsPropTypeResolutionConflict(group.Key, "ExistingPropTypes", metadata);
            })
            .Where(conflict => conflict is not null)
            .Cast<ApiSportsPropTypeResolutionConflict>()
            .ToArray();
    }

    private static bool TryComputeApiSportsSoccerValue(
        IReadOnlyDictionary<string, object?> stats,
        IReadOnlyList<string> sourceFields,
        out decimal value,
        out object rawValue)
    {
        var raw = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        value = 0;
        foreach (var field in sourceFields)
        {
            if (!stats.ContainsKey(field))
            {
                rawValue = raw;
                return false;
            }

            var parsed = GetDecimal(stats, field) ?? 0m;
            raw[field] = parsed;
            value += parsed;
        }

        rawValue = sourceFields.Count == 1 ? raw[sourceFields[0]] : raw;
        return true;
    }

    private static bool IsGoalkeeper(string? position)
    {
        return string.Equals(position, "G", StringComparison.OrdinalIgnoreCase)
            || string.Equals(position, "GK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(position, "Goalkeeper", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static IEnumerable<JsonElement> EnumerateResponseItems(JsonElement root)
    {
        return root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Array
            ? response.EnumerateArray()
            : [];
    }

    private static void FlattenStats(JsonElement node, string? prefix, Dictionary<string, JsonElement> destination)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                destination[prefix] = node.Clone();
            }

            return;
        }

        foreach (var property in node.EnumerateObject())
        {
            var nextPrefix = string.IsNullOrWhiteSpace(prefix)
                ? property.Name
                : $"{prefix}.{property.Name}";
            FlattenStats(property.Value, nextPrefix, destination);
        }
    }

    private static JsonElement GetPropertyOrDefault(JsonElement node, string propertyName)
    {
        return node.ValueKind == JsonValueKind.Object && node.TryGetProperty(propertyName, out var value)
            ? value
            : default;
    }

    private static int? GetNestedInt(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var parsed) => parsed,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? GetNestedString(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static object? ToScalarValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var parsed) => parsed,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static decimal? GetDecimal(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            decimal decimalValue => decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => (decimal)doubleValue,
            string stringValue when decimal.TryParse(stringValue, out var parsed) => parsed,
            _ => null
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static string? ExtractExternalRefValue(JsonDocument? document, string key)
    {
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value)
            ? value.ToString()
            : null;
    }

    private static async Task<DateOnly[]> BuildMissingMlbImportDatesAsync(
        ApplicationDbContext dbContext,
        DateOnly coverageStart,
        DateOnly latestCompletedDate,
        CancellationToken cancellationToken)
    {
        if (coverageStart > latestCompletedDate)
        {
            return [];
        }

        var succeededDates = await dbContext.MlbResultImportDateStates
            .AsNoTracking()
            .Where(dateState => dateState.EventDate >= coverageStart
                && dateState.EventDate <= latestCompletedDate
                && dateState.Status == "succeeded")
            .Select(dateState => dateState.EventDate)
            .ToListAsync(cancellationToken);
        var succeededDateSet = succeededDates.ToHashSet();
        var missingDates = new List<DateOnly>();
        var currentDate = coverageStart;

        while (currentDate <= latestCompletedDate)
        {
            if (!succeededDateSet.Contains(currentDate))
            {
                missingDates.Add(currentDate);
            }

            currentDate = currentDate.AddDays(1);
        }

        return missingDates.Take(100).ToArray();
    }

    private static MlbPitcherStrikeoutDateRange NormalizePitcherStrikeoutDateRange(DateOnly? startDate, DateOnly? endDate)
    {
        var normalizedEndDate = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var normalizedStartDate = startDate ?? normalizedEndDate.AddYears(-2);

        if (normalizedStartDate > normalizedEndDate)
        {
            (normalizedStartDate, normalizedEndDate) = (normalizedEndDate, normalizedStartDate);
        }

        return new MlbPitcherStrikeoutDateRange(normalizedStartDate, normalizedEndDate);
    }

    private static async Task<IReadOnlyList<TheOddsPitcherStrikeoutPropRow>> LoadTheOddsPitcherStrikeoutPropsAsync(
        ApplicationDbContext dbContext,
        string? playerName,
        string? mlbStatsId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        Guid[]? mlbMappedPlayerIds = null;
        if (!string.IsNullOrWhiteSpace(mlbStatsId))
        {
            mlbMappedPlayerIds = await dbContext.PlayerExternalRefs
                .AsNoTracking()
                .Where(reference => reference.Provider == "mlb"
                    && reference.ProviderPlayerId == mlbStatsId.Trim())
                .Select(reference => reference.PlayerId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        }

        using var theOddsFilter = JsonSerializer.SerializeToDocument(new { dataProvider = "The Odds API" });
        var rows = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Metadata != null
                && EF.Functions.JsonContains(platformProp.Metadata, theOddsFilter))
            .Where(platformProp => platformProp.Prop != null
                && platformProp.Prop.League != null
                && platformProp.Prop.League.Code == "MLB"
                && platformProp.Prop.PropType != null
                && platformProp.Prop.PropType.Name == "Pitcher Strikeouts"
                && platformProp.Prop.Player != null)
            .Select(platformProp => new
            {
                platformProp.Prop!.PlayerId,
                PlayerName = platformProp.Prop.Player!.DisplayName,
                platformProp.Prop.PropTypeId,
                platformProp.Prop.StartsAt,
                platformProp.ObservedAt
            })
            .ToListAsync(cancellationToken);

        var normalizedPlayerName = string.IsNullOrWhiteSpace(playerName)
            ? null
            : NormalizeResultPlayerName(playerName);
        var filteredRows = rows
            .Select(row => new TheOddsPitcherStrikeoutPropRow(
                row.PlayerId,
                row.PlayerName,
                row.PropTypeId,
                DateOnly.FromDateTime(row.StartsAt.UtcDateTime),
                row.ObservedAt))
            .Where(row => row.EventDate >= startDate && row.EventDate <= endDate)
            .Where(row => normalizedPlayerName is null
                || NormalizeResultPlayerName(row.PlayerName).Contains(normalizedPlayerName, StringComparison.OrdinalIgnoreCase))
            .Where(row => mlbMappedPlayerIds is null || mlbMappedPlayerIds.Contains(row.PlayerId))
            .ToArray();

        return filteredRows;
    }

    private static async Task<IReadOnlyDictionary<Guid, string[]>> LoadMlbRefsByPlayerIdAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return new Dictionary<Guid, string[]>();
        }

        var refs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => playerIds.Contains(reference.PlayerId)
                && reference.Provider == "mlb")
            .Select(reference => new
            {
                reference.PlayerId,
                reference.ProviderPlayerId
            })
            .ToListAsync(cancellationToken);

        return refs
            .GroupBy(reference => reference.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(reference => reference.ProviderPlayerId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private static async Task<IReadOnlyList<MlbPitchingResultRow>> LoadMlbPitchingResultRowsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> playerIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return [];
        }

        return await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.ExternalSource == "mlb"
                && playerIds.Contains(result.PlayerId)
                && result.EventDate >= startDate
                && result.EventDate <= endDate)
            .Where(result => result.PropType != null)
            .Select(result => new MlbPitchingResultRow(
                result.PlayerId,
                result.PropTypeId,
                result.PropType!.Name,
                result.EventDate,
                result.ActualResultValue))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Guid[]> LoadPropTypeIdsByNameAsync(
        ApplicationDbContext dbContext,
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeResultPropTypeName(name);
        var propTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(propType => new { propType.Id, propType.Name })
            .ToListAsync(cancellationToken);

        return propTypes
            .Where(propType => NormalizeResultPropTypeName(propType.Name) == normalizedName)
            .Select(propType => propType.Id)
            .Distinct()
            .ToArray();
    }

    private static async Task<Guid[]> LoadPropTypeIdsByNamesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken)
    {
        var normalizedNames = names
            .Select(NormalizeResultPropTypeName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var propTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(propType => new { propType.Id, propType.Name })
            .ToListAsync(cancellationToken);

        return propTypes
            .Where(propType => normalizedNames.Contains(NormalizeResultPropTypeName(propType.Name)))
            .Select(propType => propType.Id)
            .Distinct()
            .ToArray();
    }

    private static async Task<HashSet<DateOnly>> LoadResultDatesForPropTypesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> propTypeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        if (propTypeIds.Count == 0)
        {
            return [];
        }

        var dates = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.ExternalSource == "mlb"
                && propTypeIds.Contains(result.PropTypeId)
                && result.EventDate >= startDate
                && result.EventDate <= endDate)
            .Select(result => result.EventDate)
            .Distinct()
            .ToListAsync(cancellationToken);

        return dates.ToHashSet();
    }

    private static Task<int> CountResultsForPropTypesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> propTypeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        if (propTypeIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        return dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => result.ExternalSource == "mlb"
                && propTypeIds.Contains(result.PropTypeId)
                && result.EventDate >= startDate
                && result.EventDate <= endDate,
                cancellationToken);
    }

    private static string ClassifyPitcherStrikeoutCoverageRootCause(
        PlayerHistoricalLookupDiagnostic lookup,
        int pitcherStrikeoutResultCount,
        int genericStrikeoutResultCount,
        int pitchingStatResultCount,
        int missingResultDateCount,
        int completedImportDatesBeforeEvent)
    {
        if (pitcherStrikeoutResultCount > 0 && lookup.HistoricalGamesAvailable > 0)
        {
            return "CoverageExists";
        }

        if (genericStrikeoutResultCount > 0)
        {
            return "GenericStrikeoutsCreatedInstead";
        }

        if (pitchingStatResultCount > 0 && missingResultDateCount > 0)
        {
            return "ImportedBeforePitcherStrikeoutsSupport";
        }

        if (completedImportDatesBeforeEvent > 0)
        {
            return "PitcherStrikeoutsImportGap";
        }

        return lookup.HistoricalResultsForEquivalentPlayers > 0
            ? "PropTypeEquivalenceGap"
            : "TrulyNoHistoricalResults";
    }

    private static string NormalizeResultPropTypeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string NormalizeResultPlayerName(string? value)
    {
        return PlayerNameNormalizer.ToCompactKey(value).ToUpperInvariant();
    }

    private static bool IsPitchingResultPropType(string propTypeName)
    {
        return NormalizeResultPropTypeName(propTypeName) is
            "PITCHERSTRIKEOUTS" or
            "PITCHINGOUTS" or
            "WALKSALLOWED" or
            "HITSALLOWED" or
            "EARNEDRUNSALLOWED";
    }

    private static PropResultResponse ToResponse(PropResult result)
    {
        return new PropResultResponse(
            result.Id,
            result.PlayerId,
            result.Player?.DisplayName ?? string.Empty,
            result.LeagueId,
            result.League?.Code ?? string.Empty,
            result.PropTypeId,
            result.PropType?.Name ?? string.Empty,
            result.EventDate,
            result.ActualResultValue,
            result.ExternalSource,
            result.ExternalSourceIdentifier,
            result.CreatedAt,
            result.UpdatedAt);
    }

    private sealed record PropResultResponse(
        Guid Id,
        Guid PlayerId,
        string PlayerName,
        Guid LeagueId,
        string League,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate,
        decimal ActualResultValue,
        string ExternalSource,
        string ExternalSourceIdentifier,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record MlbResultImportRequest(DateOnly Date, bool DryRun = false, bool Force = false);

    private sealed record MlbResultImportRangeRequest(DateOnly StartDate, DateOnly EndDate);

    private sealed record MlbResultImportRepairRequest(
        DateOnly FromDate,
        DateOnly ToDate,
        bool? DryRun,
        bool? Force,
        bool? RepairPartial,
        bool? RepairMissing);

    private sealed record ApiSportsSoccerImportFixtureRequest(
        string FixtureId,
        bool? DryRun,
        bool? ForceLive);

    private sealed record ApiSportsWorldCupBackfillCompletedRequest(
        DateOnly? FromDate,
        DateOnly? ToDate,
        bool? DryRun,
        bool? ForceLive);

    private sealed record ApiSportsConfigDiagnostics(
        bool configured,
        string[] missingConfig,
        object safeConfig);

    private sealed record ApiSportsPlayerStatsRow(
        int? TeamId,
        string? TeamName,
        int? PlayerId,
        string? PlayerName,
        string[] StatKeys,
        IReadOnlyDictionary<string, object?> RawStats,
        string? Position,
        decimal? Minutes);

    private sealed record ApiSportsFixtureSummary(
        string FixtureId,
        DateTimeOffset? KickoffUtc,
        DateOnly? EventDate,
        int? LeagueId,
        string? LeagueName,
        int? Season,
        int? HomeTeamId,
        string? HomeTeamName,
        int? AwayTeamId,
        string? AwayTeamName);

    private sealed record ApiSportsFixtureDiscoveryRow(
        string FixtureId,
        DateOnly? EventDate,
        DateTimeOffset? KickoffUtc,
        int? LeagueId,
        int? Season,
        string? HomeTeamName,
        string? AwayTeamName,
        string? StatusShort,
        string? StatusLong);

    private sealed record ApiSportsWorldCupDateRange(DateOnly FromDate, DateOnly ToDate);

    private sealed record ApiSportsWorldCupSkippedFixture(
        string FixtureId,
        DateOnly? EventDate,
        string? StatusShort,
        string? StatusLong,
        string Reason);

    private sealed record ApiSportsWorldCupFixtureImportOutcome(
        string FixtureId,
        DateOnly? EventDate,
        DateTimeOffset? KickoffUtc,
        string? HomeTeamName,
        string? AwayTeamName,
        string? StatusShort,
        string? StatusLong,
        int ResultRowsPreviewed,
        int ResultRowsWritten,
        int ResultRowsInserted,
        int ResultRowsUpdated,
        IReadOnlyList<ApiSportsSoccerUnsupportedSkippedRow> UnsupportedSkipped,
        object MappingDiagnostics,
        object RuleConfidenceCounts);

    private sealed record ApiSportsWorldCupFixtureImportFailure(
        string FixtureId,
        DateOnly? EventDate,
        string? StatusShort,
        string? StatusLong,
        string ExceptionType,
        string Message);

    private sealed record ApiSportsWorldCupDateCoverageRow(
        DateOnly Date,
        int? FixtureCount,
        int? CompletedFixtureCount,
        int ImportedFixtureCount,
        int ImportedResultRows,
        int? PreviewedResultRows,
        int? SkippedPendingFixtureCount,
        int? MissingCompletedFixtureCount,
        int FailedFixtureCount,
        int? ApiCallsUsed);

    private sealed record ApiSportsWorldCupFixtureRowCount(
        string FixtureId,
        DateOnly EventDate,
        int RowCount,
        DateTimeOffset LatestImportedAtUtc);

    private sealed record ApiSportsWorldCupPersistedImportStatus(
        IReadOnlyList<ApiSportsWorldCupFixtureRowCount> RowCountsByFixture,
        int TotalRows,
        string? LatestImportedFixtureId,
        DateOnly? LatestImportedDate,
        DateTimeOffset? LatestImportedAtUtc);

    private sealed record ApiSportsCoverageResultRow(
        Guid ResultId,
        DateOnly EventDate,
        string? FixtureId,
        string? ApiSportsPlayerId,
        Guid PlayerId,
        string PlayerName,
        string NormalizedPlayerName,
        Guid PropTypeId,
        string PropTypeName,
        string StatKey,
        string NormalizedPropTypeName,
        decimal ActualResultValue,
        string ExternalSourceIdentifier,
        Guid? ApprovedCanonicalPlayerId,
        string? ApprovedCanonicalPlayerName);

    private sealed record ApiSportsCoverageMarketRow(
        string Source,
        Guid PlayerId,
        string PlayerName,
        string NormalizedPlayerName,
        Guid PropTypeId,
        string PropTypeName,
        string StatKey,
        string NormalizedPropTypeName,
        DateOnly EventDate,
        string? ExternalId,
        string? ExternalStableKey,
        string? Status);

    private sealed record ApiSportsCoverageSnapshotRow(
        string Source,
        string PlayerName,
        string NormalizedPlayerName,
        string PropTypeName,
        string StatKey,
        string NormalizedPropType,
        DateOnly EventDate,
        string? EventId,
        string? Bookmaker,
        string? ProviderCategory);

    private sealed record ApiSportsMarketJoinResultRow(
        Guid ResultId,
        DateOnly EventDate,
        string? ApiSportsFixtureId,
        string? ApiSportsPlayerId,
        Guid PlayerId,
        string PlayerName,
        string NormalizedPlayerName,
        string? TeamName,
        string NormalizedTeamName,
        string PropTypeName,
        string StatKey,
        decimal ActualResultValue,
        string ExternalSourceIdentifier);

    private sealed record ApiSportsMarketJoinPlatformRow(
        Guid PlatformPropId,
        string PlatformPropExternalId,
        string? ProviderEventId,
        string? DataProvider,
        string? ContextSchemaVersion,
        string? Provider,
        string? Bookmaker,
        string PlayerName,
        string NormalizedPlayerName,
        string PropTypeName,
        string StatKey,
        string? MarketKey,
        string? CanonicalStatKey,
        decimal Line,
        string Side,
        DateOnly EventDate,
        DateTimeOffset EventStartTimeUtc,
        string? EventName,
        string? HomeTeamName,
        string? AwayTeamName,
        string? ProviderPlayerId,
        string? PlayerTeamName,
        string? ProviderCategory,
        string? ProviderSportKey,
        string? Competition,
        bool? EntryActionableForUser,
        bool? SportsbookContextResearchOnly,
        Guid? AliasCanonicalPlayerId);

    private sealed record ApiSportsMarketJoinMatchedSample(
        Guid ResultId,
        Guid PlatformPropId,
        string? ApiSportsFixtureId,
        string ApiSportsPlayerName,
        string? ApiSportsTeamName,
        string ApiSportsPropType,
        string ApiSportsStatKey,
        decimal ApiSportsValue,
        string? ProviderEventId,
        string MarketPlayerName,
        string? Bookmaker,
        string? Provider,
        string? ProviderMarketKey,
        string PlatformPropType,
        decimal Line,
        string Side,
        string? EventName,
        DateOnly EventDate,
        DateTimeOffset EventStartTimeUtc,
        string? HomeTeam,
        string? AwayTeam,
        IReadOnlyList<string> JoinBasis);

    private sealed record ApiSportsMarketJoinUnmatchedResultSample(
        string Reason,
        string? ApiSportsFixtureId,
        string ApiSportsPlayerName,
        string? ApiSportsTeamName,
        string ApiSportsPropType,
        string ApiSportsStatKey,
        decimal ApiSportsValue,
        DateOnly EventDate,
        int EventCandidateCount,
        int PlayerNameCandidateCount,
        int PropCandidateCount);

    private sealed record ApiSportsMarketJoinUnmatchedPlatformSample(
        string? ProviderEventId,
        string MarketPlayerName,
        string? Bookmaker,
        string? Provider,
        string? ProviderMarketKey,
        string? CanonicalMarketStatKey,
        string PlatformPropType,
        decimal Line,
        string Side,
        string? EventName,
        DateOnly EventDate,
        DateTimeOffset EventStartTimeUtc,
        string? HomeTeam,
        string? AwayTeam);

    private sealed record ApiSportsMarketJoinBlockerSummary(
        int MissingProviderEventId,
        int MissingApiSportsFixtureIdOnMarketRows,
        int MissingPlayerTeam,
        int MissingProviderPlayerId,
        int PropTypeMappingMissing,
        int PlayerNameMismatch,
        int EventDateMismatch,
        IReadOnlyList<string> Notes);

    private sealed record ApiSportsMarketJoinPreview(
        object Summary,
        ApiSportsMarketJoinBlockerSummary Blockers,
        object Samples,
        string Recommendation);

    private sealed record ApiSportsSoccerGradingPreview(
        object Summary,
        ApiSportsSoccerGradingPreviewBlockers Blockers,
        object Samples,
        string Recommendation);

    private sealed record ApiSportsSoccerGradingComputation(
        ApiSportsSoccerGradingPreviewSummary Summary,
        ApiSportsSoccerGradingPreviewBlockers Blockers,
        IReadOnlyList<ApiSportsSoccerGradingPreviewSample> GradedSamples,
        IReadOnlyList<ApiSportsMarketJoinUnmatchedResultSample> UnjoinableResultSamples,
        IReadOnlyList<ApiSportsMarketJoinUnmatchedPlatformSample> UnjoinablePlatformSamples);

    private sealed record ApiSportsSoccerGradingPreviewSummary(
        int CandidatePlatformRows,
        int ImportedResultRows,
        int FullyJoinableRows,
        int GradedPreviewRows,
        int OverWins,
        int UnderWins,
        int Pushes,
        int NoLineRows,
        int UngradedRows,
        decimal GradeCoverageRate,
        object RowsByBookmaker,
        object RowsByMarket,
        object RowsByPropType);

    private sealed record ApiSportsSoccerGradingPreviewBlockers(
        int MissingApiSportsFixtureIdOnMarketRows,
        int MissingProviderPlayerId,
        int MissingPlayerTeam,
        int PlayerNameMismatch,
        int PropTypeMappingMissing,
        int EventDateMismatch,
        int NoLineRows,
        int UnjoinableRows);

    private sealed record ApiSportsSoccerGradingPreviewSample(
        Guid ResultId,
        Guid PlatformPropId,
        string? ProviderEventId,
        string? FixtureId,
        string? EventName,
        DateOnly EventDate,
        string? Bookmaker,
        string? Provider,
        string ProviderCategory,
        bool SportsbookContextResearchOnly,
        bool EntryActionableForUser,
        string MarketPlayerName,
        string ResultPlayerName,
        string NormalizedPlayerName,
        string PropType,
        string? ProviderMarketKey,
        decimal Line,
        string? Side,
        decimal ActualValue,
        string GradeResult,
        string JoinMethod,
        string Confidence,
        IReadOnlyList<string> Notes);

    private sealed record ApiSportsSoccerGradingCoverageSummary(
        ApiSportsSoccerGradingCoverageTopLevelSummary Summary,
        ApiSportsSoccerGradingPreviewBlockers TopBlockersAggregated,
        IReadOnlyList<ApiSportsSoccerGradingCoverageFixtureRank> BestCoverageFixtures,
        IReadOnlyList<ApiSportsSoccerGradingCoverageFixtureRank> WorstCoverageFixtures,
        IReadOnlyList<ApiSportsSoccerGradingCoverageFixtureDetail> FixtureDetails,
        IReadOnlyList<ApiSportsSoccerGradingCoverageUnresolvedEvent> UnresolvedProviderEvents,
        IReadOnlyList<ApiSportsSoccerGradingCoverageResultOnlyFixture> ResultOnlyFixtures,
        IReadOnlyList<ApiSportsSoccerGradingCoverageUnresolvedEvent> PlatformOnlyEvents,
        bool KnownWorkingPairDetected,
        string RecommendedNextAction);

    private sealed record ApiSportsSoccerGradingCoverageTopLevelSummary(
        int ResolvedPairsEvaluated,
        int UnresolvedProviderEvents,
        int ResultOnlyFixtures,
        int PlatformOnlyEvents,
        int FixturesWithGradedPreviewRows,
        int TotalCandidatePlatformRows,
        int TotalImportedResultRows,
        int TotalFullyJoinableRows,
        int TotalGradedPreviewRows,
        int TotalNoLineRows,
        int TotalUngradedRows,
        decimal OverallGradeCoverageRate);

    private sealed record ApiSportsSoccerGradingCoverageFixtureRank(
        string? FixtureId,
        string? ProviderEventId,
        string? EventName,
        DateOnly? EventDate,
        int CandidatePlatformRows,
        int ImportedResultRows,
        int GradedPreviewRows,
        decimal GradeCoverageRate);

    private sealed record ApiSportsSoccerGradingCoverageFixtureDetail(
        string? FixtureId,
        string? ProviderEventId,
        string? EventName,
        DateOnly? EventDate,
        string? HomeTeam,
        string? AwayTeam,
        int CandidatePlatformRows,
        int ImportedResultRows,
        int UniquePlatformPlayers,
        int UniqueApiSportsPlayers,
        int AliasLinkedPlayers,
        int FullyJoinableRowsBeforeAlias,
        int FullyJoinableRowsAfterAlias,
        int GradedPreviewRows,
        int OverWins,
        int UnderWins,
        int Pushes,
        int NoLineRows,
        int UngradedRows,
        decimal GradeCoverageRate,
        ApiSportsSoccerGradingPreviewBlockers TopBlockers,
        IReadOnlyList<ApiSportsSoccerGradingPreviewSample> SampleGradedRows,
        IReadOnlyList<ApiSportsMarketJoinUnmatchedPlatformSample> SampleUnjoinablePlatformRows,
        IReadOnlyList<ApiSportsSoccerGradingPreviewSample> SampleAliasLinkedRows);

    private sealed record ApiSportsSoccerGradingCoverageUnresolvedEvent(
        string? FixtureId,
        string? ProviderEventId,
        string? EventName,
        DateOnly? EventDate,
        string? HomeTeam,
        string? AwayTeam,
        int CandidatePlatformRows,
        int UniquePlatformPlayers,
        string Reason);

    private sealed record ApiSportsSoccerGradingCoverageResultOnlyFixture(
        string FixtureId,
        DateOnly? EventDate,
        int ImportedResultRows,
        int UniqueApiSportsPlayers,
        string Reason);

    private sealed record ApiSportsSoccerPropMappingGapDiagnostic(
        ApiSportsSoccerPropMappingGapSummary Summary,
        IReadOnlyList<ApiSportsSoccerTheOddsMarketGap> TheOddsMarketGaps,
        IReadOnlyList<ApiSportsSoccerApiSportsStatGap> ApiSportsStatGaps,
        object GapsByCategory,
        object GapsByProviderMarketKey,
        object GapsByApiSportsStatKey,
        IReadOnlyList<object> LowRiskMappingCandidates,
        IReadOnlyList<object> NeedsReviewMappingCandidates,
        IReadOnlyList<ApiSportsSoccerApiSportsStatGap> IgnoredNoise,
        string RecommendedNextAction);

    private sealed record ApiSportsSoccerPropMappingGapSummary(
        int ResolvedPairsEvaluated,
        int TotalUngradedRows,
        int PropTypeMappingMissingRows,
        object GapsByCategory,
        object GapsByProviderMarketKey,
        object GapsByApiSportsStatKey,
        int LowRiskMappingCandidates,
        int NeedsReviewMappingCandidates,
        int IgnoredNoiseRows,
        string RecommendedNextAction);

    private sealed record ApiSportsSoccerTheOddsMarketGap(
        string? ProviderMarketKey,
        string PlatformPropType,
        string? CanonicalMarketStatKey,
        int RowCount,
        int DistinctPlayers,
        int DistinctEvents,
        string Category,
        bool ProviderStatMappingExists,
        bool CanFixWithProviderStatMapping,
        string? SuggestedCanonicalStatKey,
        string RiskLevel,
        string Recommendation,
        IReadOnlyList<ApiSportsSoccerPlatformMappingGapRow> SampleRows);

    private sealed record ApiSportsSoccerApiSportsStatGap(
        string ApiSportsPropType,
        string ApiSportsStatKey,
        string? RuleConfidence,
        bool? IsGradeable,
        int RowCount,
        int DistinctPlayers,
        int DistinctFixtures,
        string Category,
        bool CanFixWithProviderStatMapping,
        string? SuggestedCanonicalStatKey,
        string RiskLevel,
        string Recommendation,
        IReadOnlyList<ApiSportsSoccerResultMappingGapRow> SampleRows);

    private sealed record ApiSportsSoccerPlatformMappingGapRow(
        string? FixtureId,
        string? ProviderEventId,
        Guid PlatformPropId,
        string PlayerName,
        string PlatformPropType,
        string? ProviderMarketKey,
        string? CanonicalMarketStatKey,
        string? EventName,
        DateOnly EventDate,
        string? Bookmaker,
        string? Provider,
        string Category,
        string Recommendation,
        string RiskLevel,
        string? SuggestedCanonicalStatKey)
    {
        public bool CanFixWithProviderStatMapping => Recommendation is "AddKnownMapping" or "AddNeedsConfirmationMapping";
    }

    private sealed record ApiSportsSoccerResultMappingGapRow(
        string? FixtureId,
        Guid ResultId,
        string PlayerName,
        string? TeamName,
        string ApiSportsPropType,
        string ApiSportsStatKey,
        DateOnly EventDate,
        string? RuleConfidence,
        bool? IsGradeable,
        string Category,
        string Recommendation,
        string RiskLevel,
        decimal ActualValue)
    {
        public bool CanFixWithProviderStatMapping => Recommendation is "AddKnownMapping" or "AddNeedsConfirmationMapping";

        public string? SuggestedCanonicalStatKey => CanFixWithProviderStatMapping ? ApiSportsStatKey : null;
    }

    private sealed record ApiSportsProviderStatMappingDiagnosticRow(
        string CanonicalStatKey,
        string DisplayName,
        IReadOnlyList<string> ProviderStatFields,
        string RuleConfidence,
        bool IsGradeable,
        bool PublicGradingEnabled,
        string? ProviderStatKey,
        string ValueTransform,
        string? Notes);

    private sealed record ApiSportsSoccerStatMappingReview(
        ApiSportsSoccerStatMappingReviewSummary Summary,
        IReadOnlyList<ApiSportsSoccerStatMappingReviewRow> Mappings,
        IReadOnlyList<ApiSportsSoccerImportedStatNoiseReviewRow> ImportedStatNoise,
        string RecommendedNextAction);

    private sealed record ApiSportsSoccerStatMappingReviewSummary(
        int MappingsReviewed,
        int NeedsConfirmationMappings,
        int UnsupportedMappings,
        int NonGradeableMappings,
        int RowCountAffected,
        int ManualApprovalCandidates,
        int KeepNonGradeableRecommendations,
        int NeedsMoreProviderEvidence,
        int ImportedStatNoiseRows,
        bool PublicGradingEligible);

    private sealed record ApiSportsSoccerStatMappingReviewRow(
        string PropType,
        string CanonicalStatKey,
        IReadOnlyList<string> ApiSportsSourceFields,
        string RuleConfidence,
        bool IsGradeable,
        bool PublicGradingEnabled,
        int RowCountAffected,
        int DistinctPlayers,
        int DistinctFixtures,
        int TheOddsReturnedPlatformRows,
        int ImportedStatNoiseRows,
        IReadOnlyList<ApiSportsSoccerResultMappingGapRow> SampleRows,
        string? CurrentNotes,
        string WhyNonGradeableToday,
        string RiskAssessment,
        string Recommendation,
        bool PublicGradingEligible,
        bool DebugPreviewEligibleIfApproved);

    private sealed record ApiSportsSoccerImportedStatNoiseReviewRow(
        string ApiSportsPropType,
        string ApiSportsStatKey,
        int RowCount,
        int DistinctPlayers,
        int DistinctFixtures,
        string WhyIgnored,
        string Recommendation);

    private sealed record ApiSportsSoccerFixtureMappingCoverage(
        ApiSportsSoccerFixtureMappingCoverageSummary Summary,
        IReadOnlyList<ApiSportsSoccerFixtureMappingEvent> ResolvedEvents,
        IReadOnlyList<ApiSportsSoccerFixtureMappingEvent> UnresolvedEvents,
        IReadOnlyList<ApiSportsSoccerFixtureMappingEvent> ExcludedEvents,
        bool KnownWorkingPairDetected,
        string RecommendedNextAction);

    private sealed record ApiSportsSoccerFixtureMappingCoverageSummary(
        int ProviderEventsEvaluated,
        int ResolvedEvents,
        int UnresolvedEvents,
        int AmbiguousEvents,
        int NoCandidateEvents,
        int EventsWithPlatformRows,
        int EventsWithApiSportsResults,
        int TotalPlatformRows,
        int ResolvedPlatformRows,
        int UnresolvedPlatformRows);

    private sealed record ApiSportsSoccerFixtureMappingEvent(
        string ProviderEventId,
        string? EventName,
        DateTimeOffset EventStartUtc,
        DateOnly EventDate,
        string? HomeTeam,
        string? AwayTeam,
        int PlatformRowCount,
        int UniquePlatformPlayers,
        IReadOnlyList<ApiSportsSoccerFixtureCandidate> CandidateApiSportsFixtures,
        string? ResolvedFixtureId,
        string ResolutionStatus,
        decimal Confidence,
        IReadOnlyList<string> MatchReasons,
        IReadOnlyList<string> Blockers);

    private sealed record ApiSportsSoccerFixtureCandidate(
        string FixtureId,
        DateOnly EventDate,
        IReadOnlyList<string> TeamNames,
        int ResultRowCount,
        int UniqueApiSportsPlayers,
        decimal Confidence,
        IReadOnlyList<string> MatchReasons,
        IReadOnlyList<string> Blockers);

    private sealed record ApiSportsSoccerFixtureMappingProviderEvent(
        string ProviderEventId,
        string? EventName,
        DateTimeOffset EventStartUtc,
        DateOnly EventDate,
        string? HomeTeam,
        string? AwayTeam,
        string NormalizedHomeTeam,
        string NormalizedAwayTeam,
        int PlatformRowCount,
        int UniquePlatformPlayers);

    private sealed record ApiSportsSoccerFixtureMappingResultFixture(
        string FixtureId,
        DateOnly EventDate,
        int ResultRowCount,
        int UniqueApiSportsPlayers,
        IReadOnlyList<string> TeamNames,
        IReadOnlyList<string> NormalizedTeamNames);

    private sealed record TheOddsSoccerWorldCupIngestionCoverage(
        TheOddsSoccerWorldCupIngestionCoverageSummary Summary,
        IReadOnlyList<TheOddsSoccerWorldCupRealProviderEvent> RealProviderEvents,
        TheOddsSoccerWorldCupSyntheticCoverage SyntheticOrLegacySoccerRows,
        TheOddsSoccerWorldCupApiSportsResultCoverage ApiSportsFixtureResultCoverage,
        TheOddsSoccerWorldCupRefreshRun? LatestSoccerRefreshRun,
        TheOddsSoccerWorldCupIngestionGapAnalysis GapAnalysis,
        string RecommendedNextAction);

    private sealed record TheOddsSoccerWorldCupIngestionCoverageSummary(
        int RealTheOddsProviderEvents,
        int RealTheOddsPlatformRows,
        int SyntheticOrLegacySoccerRows,
        int ApiSportsFixturesWithResults,
        int ResolvedProviderEvents,
        int UnresolvedProviderEvents,
        Guid? LatestSoccerRefreshRunId,
        bool? LatestSoccerRefreshSucceeded,
        int? LatestSoccerRefreshEventCount,
        int? LatestSoccerRefreshPlatformRowsUpserted);

    private sealed record TheOddsSoccerWorldCupRealProviderEvent(
        string ProviderEventId,
        string? EventName,
        DateTimeOffset EventStartUtc,
        DateOnly EventDate,
        string? HomeTeam,
        string? AwayTeam,
        int PlatformRowCount,
        int UniquePlayerCount,
        IReadOnlyList<string> Bookmakers,
        IReadOnlyList<string> Markets,
        string? ProviderCategory,
        string? ContextSchemaVersion,
        string? ProviderSportKey,
        string? Competition);

    private sealed record TheOddsSoccerWorldCupSyntheticCoverage(
        int Count,
        IReadOnlyList<TheOddsSoccerWorldCupSyntheticProviderEvent> Samples);

    private sealed record TheOddsSoccerWorldCupSyntheticProviderEvent(
        string ProviderEventId,
        DateOnly EventDate,
        int PlatformRowCount,
        IReadOnlyList<string> ExclusionReasons);

    private sealed record TheOddsSoccerWorldCupApiSportsResultCoverage(
        int FixtureCountWithResults,
        int CompletedFixturesWithResults,
        DateOnly FromDate,
        DateOnly ToDate,
        IReadOnlyList<TheOddsSoccerWorldCupApiSportsFixtureSample> SampleFixtures);

    private sealed record TheOddsSoccerWorldCupApiSportsFixtureSample(
        string FixtureId,
        DateOnly EventDate,
        int ImportedResultRows,
        int UniqueApiSportsPlayers,
        IReadOnlyList<string> Teams);

    private sealed record TheOddsSoccerWorldCupRefreshRun(
        Guid Id,
        string Sport,
        DateOnly SlateDate,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        bool Succeeded,
        bool Failed,
        bool Blocked,
        bool DryRun,
        bool ForceLive,
        string BookmakerPreset,
        string ProviderCategoriesIncluded,
        string SportsbookBookmakersRequested,
        string DfsBookmakersRequested,
        string MarketsRequested,
        int EstimatedApiCalls,
        int ActualApiCalls,
        int PlatformPropsCreated,
        int PlatformPropsUpdated,
        int PlatformPropsUpserted,
        int PredictionCount,
        int LineShoppingGroupCount,
        string? ErrorMessage,
        JsonElement? Diagnostics);

    private sealed record TheOddsSoccerWorldCupIngestionGapAnalysis(
        int ApiSportsFixturesWithResultsButNoRealTheOddsEvent,
        int RealTheOddsEventsWithoutApiSportsResults,
        int RealTheOddsEventsResolvedToFixture,
        int RealTheOddsEventsUnresolved,
        string LikelyReasonOnlyOneRealEventExists,
        IReadOnlyList<TheOddsSoccerWorldCupApiSportsFixtureSample> ApiSportsFixtureSamplesWithoutRealTheOddsEvent,
        IReadOnlyList<ApiSportsSoccerFixtureMappingEvent> RealTheOddsEventSamplesWithoutApiSportsResults);

    private sealed record TheOddsAliasPlatformPlayer(
        string PlatformPlayerName,
        string NormalizedPlayerName,
        IReadOnlyList<string> Tokens,
        string? ProviderEventId,
        string? EventName,
        DateTimeOffset EventStartUtc,
        string? HomeTeamName,
        string? AwayTeamName,
        string? ProviderPlayerId,
        string? PlayerTeamName,
        IReadOnlyList<string> Providers,
        IReadOnlyList<string> Bookmakers,
        int PlatformRowCount);

    private sealed record ApiSportsAliasFixturePlayer(
        string ApiSportsPlayerId,
        Guid LocalPlayerId,
        string ApiSportsPlayerName,
        string NormalizedPlayerName,
        IReadOnlyList<string> Tokens,
        string? TeamName,
        string NormalizedTeamName,
        int ResultRowCount);

    private sealed record TheOddsApiSportsAliasCandidateScore(
        string ApiSportsPlayerId,
        Guid LocalPlayerId,
        string ApiSportsPlayerName,
        string NormalizedPlayerName,
        string? TeamName,
        decimal Confidence,
        IReadOnlyList<string> SharedTokens,
        IReadOnlyList<string> MatchReasons);

    private sealed record TheOddsApiSportsAliasCandidateRow(
        string PlatformPlayerName,
        string NormalizedPlatformPlayerName,
        string? ProviderEventId,
        string? EventName,
        DateTimeOffset EventStartUtc,
        string? HomeTeamName,
        string? AwayTeamName,
        string? ProviderPlayerId,
        string? PlayerTeamName,
        IReadOnlyList<string> Providers,
        IReadOnlyList<string> Bookmakers,
        int PlatformRowCount,
        bool ExactNormalizedMatch,
        string SuggestedAction,
        decimal Confidence,
        IReadOnlyList<string> Blockers,
        IReadOnlyList<TheOddsApiSportsAliasCandidateScore> Candidates);

    private sealed record TheOddsApiSportsAliasCandidatePreview(
        object Summary,
        object Blockers,
        object Samples,
        string Recommendation);

    private sealed record TheOddsApiSportsAliasLinkRequest(
        string? FixtureId,
        string? ProviderEventId,
        bool? DryRun,
        decimal? MinConfidence,
        bool? IncludeExactNormalizedMatches,
        bool? IncludeTokenSubsetMatches,
        int? MaxLinks);

    private sealed record TheOddsApiSportsAliasLinkBatchRequest(
        DateOnly? FromDate,
        DateOnly? ToDate,
        bool? DryRun,
        decimal? MinConfidence,
        int? MaxLinks,
        bool? IncludeExactNormalizedMatches,
        bool? IncludeTokenSubsetMatches);

    private sealed record TheOddsApiSportsAliasManualApprovalRequest(
        string? ProviderEventId,
        string? FixtureId,
        string? PlatformPlayerName,
        string? ApiSportsPlayerId,
        Guid? LocalPlayerId,
        bool? DryRun,
        string? Reason);

    private sealed record TheOddsApiSportsAliasBatch(
        int ResolvedPairsEvaluated,
        int CandidatesEvaluated,
        int SafeCandidates,
        object CandidateSummary,
        object EstimatedCoverageImpact,
        IReadOnlyList<TheOddsApiSportsAliasBatchCandidateGroup> CandidateGroups,
        IReadOnlyList<object> DeduplicatedCandidates,
        object Blockers,
        IReadOnlyList<TheOddsApiSportsAliasBatchLinkPlan> LinkPlans,
        string CandidateRecommendation,
        int AliasCandidateCount,
        int NewSafeCandidatesRemaining,
        int NeedsReviewRemaining,
        int NoCandidateRemaining,
        int AmbiguousRemaining);

    private sealed record TheOddsSoccerAliasApprovalCounts(
        int TotalApproved,
        int SafeCandidateApproved,
        int ManualApproved,
        int OtherApproved);

    private sealed record TheOddsApiSportsAliasBatchCandidateGroup(
        string ProviderEventId,
        string FixtureId,
        string? EventName,
        DateOnly EventDate,
        string? HomeTeam,
        string? AwayTeam,
        int PlatformPlayerCount,
        int ApiSportsFixturePlayerCount,
        int CandidateCount,
        int SafeCandidateCount,
        int NeedsReviewCount,
        int AmbiguousCount,
        int NoCandidateCount,
        int AlreadyLinkedCount,
        int NewSafeCandidateCount,
        IReadOnlyList<TheOddsApiSportsAliasCandidateRow> Candidates);

    private sealed record TheOddsApiSportsAliasBatchLinkPlan(
        string FixtureId,
        string ProviderEventId,
        TheOddsApiSportsAliasLinkPlan Plan);

    private sealed record TheOddsApiSportsAliasLinkPlan(
        IReadOnlyList<TheOddsApiSportsAliasLinkAuditRow> ProposedLinks,
        IReadOnlyList<TheOddsApiSportsAliasLinkSkippedRow> Skipped,
        int LinksAlreadyExisting);

    private sealed record TheOddsApiSportsAliasLinkAuditRow(
        string ProviderEntityId,
        string PlatformPlayerName,
        string NormalizedPlatformPlayerName,
        string ProviderEventId,
        string? EventName,
        DateTimeOffset EventStartUtc,
        string? HomeTeamName,
        string? AwayTeamName,
        string? PlayerTeamName,
        string ApiSportsPlayerId,
        Guid LocalPlayerId,
        string CandidateApiSportsPlayerName,
        string CandidateNormalizedPlayerName,
        string? CandidateTeamName,
        decimal Confidence,
        bool ExactNormalizedMatch,
        IReadOnlyList<string> MatchReasons,
        IReadOnlyList<string> Blockers);

    private sealed record TheOddsApiSportsAliasLinkSkippedRow(
        string PlatformPlayerName,
        string NormalizedPlatformPlayerName,
        string? ProviderEventId,
        decimal Confidence,
        string Reason,
        IReadOnlyList<string> Blockers,
        string? CandidateApiSportsPlayerName,
        Guid? CandidateLocalPlayerId,
        string? ExistingCanonicalEntityName);

    private sealed record ApiSportsCoverageMatchDiagnostic(
        Guid ResultId,
        string? FixtureId,
        DateOnly EventDate,
        string PlayerName,
        string PropTypeName,
        string StatKey,
        bool MatchedToPlatformProps,
        bool MatchedToSnapshots,
        string Reason,
        string Detail)
    {
        public static ApiSportsCoverageMatchDiagnostic FromResult(
            ApiSportsCoverageResultRow result,
            bool matchedToPlatformProps,
            bool matchedToSnapshots,
            string reason,
            string detail)
        {
            return new ApiSportsCoverageMatchDiagnostic(
                result.ResultId,
                result.FixtureId,
                result.EventDate,
                result.PlayerName,
                result.PropTypeName,
                result.StatKey,
                matchedToPlatformProps,
                matchedToSnapshots,
                reason,
                detail);
        }
    }

    private sealed record ApiSportsIdentityImportedPlayer(
        string ApiSportsPlayerId,
        Guid LocalResultPlayerId,
        string ApiSportsPlayerName,
        string NormalizedPlayerName,
        string? ApiSportsTeamName,
        string NormalizedTeamName,
        int ResultRowCount);

    private sealed record ApiSportsIdentityLocalPlayer(
        Guid PlayerId,
        string PlayerName,
        string NormalizedPlayerName,
        string? TeamName,
        string NormalizedTeamName,
        IReadOnlyList<string> NormalizedEventTeamNames,
        int PlatformPropCount,
        IReadOnlyList<string> Providers,
        string? ProviderPlayerId,
        IReadOnlyList<string> ExternalRefProviders);

    private sealed record ApiSportsIdentityCandidateRow(
        string ApiSportsPlayerId,
        string ApiSportsPlayerName,
        string? ApiSportsTeamName,
        int ResultRowCount,
        Guid CandidateCanonicalPlayerId,
        string CandidateCanonicalPlayerName,
        string? CandidateProviderPlayerId,
        string? CandidateProvider,
        string? CandidateTeamName,
        decimal Confidence,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<string> Blockers,
        string SuggestedAction);

    private sealed record ApiSportsReviewQueueRow(
        Guid ProviderEntityLinkId,
        string ApiSportsPlayerId,
        string? ApiSportsPlayerName,
        string? ApiSportsTeamName,
        int ResultRowCount,
        string CurrentLinkStatus,
        bool HasCandidate,
        Guid? CandidateCanonicalPlayerId,
        string? CandidateCanonicalPlayerName,
        string? CandidateProviderPlayerId,
        string? CandidateProvider,
        string? CandidateTeamName,
        decimal? Confidence,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<string> Blockers,
        string SuggestedAction);

    private sealed record ApiSportsProviderLinkApprovalRequest(
        Guid ProviderEntityLinkId,
        Guid CanonicalPlayerId,
        bool? DryRun,
        string? Reason);

    private sealed record ApiSportsBulkApprovalRequest(
        Guid ProviderEntityLinkId,
        Guid CanonicalPlayerId,
        string? Reason);

    private sealed record ApiSportsBulkApproveLinksRequest(
        IReadOnlyList<ApiSportsBulkApprovalRequest>? Approvals,
        bool? DryRun,
        bool? AllowPartial,
        bool? AllowNeedsReview);

    private sealed record ApiSportsBulkApprovalKey(
        Guid ProviderEntityLinkId,
        Guid CanonicalPlayerId);

    private sealed record ApiSportsProviderLinkRejectionRequest(
        Guid ProviderEntityLinkId,
        bool? DryRun,
        string? Reason);

    private sealed record ProviderEntityLinkState(
        Guid Id,
        Guid? SportId,
        string? Sport,
        string EntityType,
        Guid? CanonicalEntityId,
        string? CanonicalEntityKind,
        string? CanonicalEntityName,
        string ProviderKey,
        string ProviderEntityId,
        string? ProviderEntityName,
        string? ProviderTeamId,
        string? ProviderTeamName,
        string LinkStatus,
        string LinkSource,
        DateTimeOffset? ApprovedAtUtc,
        string? ApprovedBy,
        DateTimeOffset? RejectedAtUtc,
        string? RejectedReason);

    private sealed record ApiSportsProviderLinkMutationResult(
        bool DryRun,
        bool ReadOnly,
        bool Changed,
        ProviderEntityLinkState? Before,
        ProviderEntityLinkState? After,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Blockers,
        [property: JsonIgnore] ProviderEntityLink? Link)
    {
        public bool CallsExternalApis => false;
    }

    private sealed record ApiSportsBulkApproveLinksResult(
        bool DryRun,
        bool ReadOnly,
        bool AllowPartial,
        bool AllowNeedsReview,
        int RequestedCount,
        int ValidCount,
        int BlockedCount,
        int ChangedCount,
        IReadOnlyList<ApiSportsProviderLinkMutationResult> Results,
        IReadOnlyList<string> Blockers,
        IReadOnlyList<string> Warnings)
    {
        public bool CallsExternalApis => false;
    }

    private sealed record ApiSportsSoccerIdentityDiagnostics(
        IReadOnlyList<ApiSportsIdentityImportedPlayer> ImportedPlayers,
        int ResultRowCount,
        int CanonicalSoccerPlayerCount,
        int SoccerPlatformPlayerCount,
        int SoccerPlayerExternalRefCount,
        int ProviderEntityLinkCount,
        int ApiSportsProviderPlayerLinkCount,
        int ApprovedProviderPlayerLinkCount,
        int CandidateProviderPlayerLinkCount,
        int NeedsReviewProviderPlayerLinkCount,
        int RejectedProviderPlayerLinkCount,
        int ConflictProviderPlayerLinkCount,
        int ProviderLinksWithTeamContextCount,
        int SoccerPlatformPropCount,
        int SoccerSnapshotPlayerCount,
        IReadOnlyList<object> AlreadyLinked,
        IReadOnlyList<object> ExactNameCandidates,
        IReadOnlyList<object> NormalizedNameCandidates,
        IReadOnlyList<object> TeamAwareCandidates,
        IReadOnlyList<object> Ambiguous,
        IReadOnlyList<object> Unmatched,
        IReadOnlyList<object> Conflicts,
        IReadOnlyList<ApiSportsIdentityCandidateRow> Candidates);

    private sealed record ApiSportsPlatformContextRow(
        string SourceName,
        string TableName,
        Guid? CanonicalPlayerId,
        string PlayerName,
        string PropType,
        string? TeamName,
        string? OpponentName,
        string? EventName,
        DateTimeOffset? StartTime,
        string? ProviderEventId,
        string? ProviderPlayerId,
        string? Provider,
        string? Bookmaker,
        string? HomeTeamName,
        string? AwayTeamName,
        string? LeagueOrCompetition)
    {
        public bool HasAnyContext =>
            !string.IsNullOrWhiteSpace(TeamName)
            || !string.IsNullOrWhiteSpace(OpponentName)
            || !string.IsNullOrWhiteSpace(EventName)
            || StartTime.HasValue
            || !string.IsNullOrWhiteSpace(ProviderEventId)
            || !string.IsNullOrWhiteSpace(ProviderPlayerId)
            || !string.IsNullOrWhiteSpace(Bookmaker)
            || !string.IsNullOrWhiteSpace(LeagueOrCompetition);
    }

    private sealed record ApiSportsPlatformContextAvailability(
        int HasTeamNameCount,
        int HasOpponentNameCount,
        int HasEventNameCount,
        int HasStartTimeCount,
        int HasProviderEventIdCount,
        int HasProviderPlayerIdCount,
        int HasBookmakerKeyCount,
        int HasLeagueOrCompetitionCount);

    private sealed record ApiSportsSoccerFixtureImportResult(
        ApiSportsFixtureSummary FixtureSummary,
        ApiSportsFootballDiagnosticResponse FixtureResponse,
        ApiSportsFootballDiagnosticResponse StatsResponse,
        IReadOnlyList<ApiSportsPlayerStatsRow> PlayerStats,
        ApiSportsSoccerResultPreview Preview,
        ApiSportsSoccerImportWriteSummary WriteSummary,
        object MappingDiagnostics,
        IReadOnlyDictionary<string, int> RuleConfidenceCounts)
    {
        public int ResultRowsPreviewed => Preview.Rows.Count;
        public int ResultRowsWritten => WriteSummary.Inserted + WriteSummary.Updated;
        public int ResultRowsInserted => WriteSummary.Inserted;
        public int ResultRowsUpdated => WriteSummary.Updated;
        public int UnsupportedSkipped => Preview.UnsupportedSkipped.Count;
    }

    private sealed record ApiSportsSoccerResultPreview(
        IReadOnlyList<ApiSportsSoccerResultPreviewRow> Rows,
        IReadOnlyList<ApiSportsSoccerUnsupportedSkippedRow> UnsupportedSkipped,
        string MappingSource,
        int DbMappingsConfigured,
        int DbMappingsUsed,
        int FallbackMappingsUsed);

    private sealed record ApiSportsSoccerResultPreviewRow(
        string FixtureId,
        DateOnly EventDate,
        int? TeamId,
        string? TeamName,
        int? PlayerId,
        string? PlayerName,
        string? Position,
        decimal? Minutes,
        string PropType,
        string StatKey,
        string[] StatSourceFields,
        object RawStatValue,
        decimal Value,
        string RuleConfidence,
        bool IsGradeable,
        bool PublicGradingEnabled,
        string MappingSource,
        string Notes,
        string ExternalSourceIdentifier);

    private sealed record ApiSportsSoccerUnsupportedSkippedRow(
        int? PlayerId,
        string? PlayerName,
        string PropType,
        string Reason,
        string Notes);

    private sealed record ApiSportsSoccerImportWriteSummary(int Inserted, int Updated);

    private sealed record ApiSportsSoccerResultMapping(
        string PropType,
        string StatKey,
        string[] SourceFields,
        bool ImportSupported,
        bool GoalieOnly,
        string RuleConfidence,
        bool IsGradeable,
        bool PublicGradingEnabled,
        string MappingSource,
        string Notes);

    private sealed record ApiSportsSoccerResultMappingSet(
        IReadOnlyList<ApiSportsSoccerResultMapping> Mappings,
        string MappingSource,
        int DbMappingsConfigured,
        int DbMappingsUsed,
        int FallbackMappingsUsed);

    private sealed record ApiSportsPropTypeDescriptor(
        string Name,
        string StatKey,
        string ValueType);

    private sealed record ApiSportsPropTypeMetadata(
        Guid? PropTypeId,
        string Name,
        string ValueType);

    private sealed record ApiSportsPropTypeResolutionConflict(
        string StatKey,
        string Stage,
        IReadOnlyList<ApiSportsPropTypeMetadata> ConflictingMetadata);

    private sealed class ApiSportsPropTypeResolutionException(
        IReadOnlyList<ApiSportsPropTypeResolutionConflict> conflicts) : Exception("API-Sports prop type resolution failed.")
    {
        public IReadOnlyList<ApiSportsPropTypeResolutionConflict> Conflicts { get; } = conflicts;
    }

    private sealed record ApiSportsPropCoverageMapping(
        string Prop,
        string[] CandidateFields,
        bool StandardStat,
        bool TeamOrGoalieLevel,
        string Notes);

    private static readonly ApiSportsPropCoverageMapping[] RequiredSoccerPropMappings =
    [
        new("Goals", ["goals.total"], true, false, "Maps to player goals if API-Sports Football provides goals.total."),
        new("Assists", ["goals.assists"], true, false, "Maps to player assists if API-Sports Football provides goals.assists."),
        new("Goal + Assist", ["goals.total", "goals.assists"], false, false, "Requires summing goals.total + goals.assists; mark gradeable only after both fields are present and tested."),
        new("Shots", ["shots.total"], true, false, "Maps to player total shots if API-Sports Football provides shots.total."),
        new("Shots On Target", ["shots.on"], true, false, "Maps to player shots on target if API-Sports Football provides shots.on."),
        new("Passes Attempted", ["passes.total"], true, false, "Maps to player attempted passes if API-Sports Football provides passes.total."),
        new("Tackles", ["tackles.total"], true, false, "Maps to player tackles if API-Sports Football provides tackles.total."),
        new("Crosses", ["passes.crosses"], false, false, "Unsupported until API-Sports Football provides a confirmed player crosses field."),
        new("Clearances", ["tackles.blocks"], false, false, "API-Sports Football may not expose clearances directly; tackles.blocks is not equivalent without validation."),
        new("Fouls", ["fouls.committed"], true, false, "Maps to player fouls committed if API-Sports Football provides fouls.committed."),
        new("Fouls Drawn", ["fouls.drawn"], true, false, "Maps to player fouls drawn if API-Sports Football provides fouls.drawn."),
        new("Cards", ["cards.yellow", "cards.red"], false, false, "Requires combining yellow and red cards after scoring rule confirmation."),
        new("Offsides", ["offsides"], true, false, "Maps to player offsides if API-Sports Football provides offsides."),
        new("Goalie Saves", ["goals.saves"], true, true, "Goalkeeper-specific; debug import requires goalkeeper position and minutes."),
        new("Goals Allowed", ["goals.conceded"], false, true, "Conservative goalkeeper-only mapping; imported with NeedsConfirmation diagnostics."),
        new("Attempted Dribbles", ["dribbles.attempts"], true, false, "Maps to attempted dribbles if API-Sports Football provides dribbles.attempts."),
        new("Shots Assisted", ["passes.key"], false, false, "Conservative mapping; passes.key may correspond to shots assisted/key passes and is imported with NeedsConfirmation diagnostics."),
        new("Outfield Fantasy Score", [], false, false, "Provider-scored prop; unsupported until scoring rules and all component fields are verified."),
        new("Goalie Fantasy Score", [], false, true, "Provider-scored prop; unsupported until scoring rules and all component fields are verified."),
        new("Combo props", [], false, false, "Combo props are unsupported until participant and scoring semantics are explicit.")
    ];

    private static readonly ApiSportsSoccerResultMapping[] ApiSportsSoccerResultMappings =
    [
        new("Goals", "GOALS", ["goals.total"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football goals.total."),
        new("Assists", "ASSISTS", ["goals.assists"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football goals.assists."),
        new("Shots", "SHOTS", ["shots.total"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football shots.total."),
        new("Shots On Target", "SHOTS_ON_TARGET", ["shots.on"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football shots.on."),
        new("Passes Attempted", "PASSES_ATTEMPTED", ["passes.total"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football passes.total."),
        new("Tackles", "TACKLES", ["tackles.total"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football tackles.total."),
        new("Fouls", "FOULS", ["fouls.committed"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football fouls.committed."),
        new("Fouls Drawn", "FOULS_DRAWN", ["fouls.drawn"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football fouls.drawn."),
        new("Offsides", "OFFSIDES", ["offsides"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football offsides."),
        new("Goalie Saves", "GOALIE_SAVES", ["goals.saves"], true, true, "Known", true, false, "Fallback", "Goalkeeper-only mapping from API-Sports Football goals.saves."),
        new("Attempted Dribbles", "ATTEMPTED_DRIBBLES", ["dribbles.attempts"], true, false, "Known", true, false, "Fallback", "Maps to API-Sports Football dribbles.attempts."),
        new("Goal + Assist", "GOAL_ASSIST", ["goals.total", "goals.assists"], true, false, "NeedsConfirmation", false, false, "Fallback", "Conservative mapping: sum goals.total + goals.assists."),
        new("Cards", "CARDS", ["cards.yellow", "cards.red"], true, false, "NeedsConfirmation", false, false, "Fallback", "Conservative mapping: sum cards.yellow + cards.red."),
        new("Goals Allowed", "GOALS_ALLOWED", ["goals.conceded"], true, true, "NeedsConfirmation", false, false, "Fallback", "Conservative goalkeeper-only mapping from goals.conceded."),
        new("Shots Assisted", "SHOTS_ASSISTED", ["passes.key"], true, false, "NeedsConfirmation", false, false, "Fallback", "Conservative mapping: passes.key may correspond to shots assisted/key passes."),
        new("Crosses", "CROSSES", [], false, false, "Unsupported", false, false, "Fallback", "Unsupported until API-Sports Football provides a confirmed player crosses field."),
        new("Clearances", "CLEARANCES", [], false, false, "Unsupported", false, false, "Fallback", "Unsupported until API-Sports Football provides a confirmed player clearances field."),
        new("Outfield Fantasy Score", "OUTFIELD_FANTASY_SCORE", [], false, false, "Unsupported", false, false, "Fallback", "Unsupported provider-scored prop."),
        new("Goalie Fantasy Score", "GOALIE_FANTASY_SCORE", [], false, true, "Unsupported", false, false, "Fallback", "Unsupported provider-scored prop."),
        new("Combo props", "COMBO_PROPS", [], false, false, "Unsupported", false, false, "Fallback", "Unsupported combo prop family.")
    ];

    private sealed record MlbPitcherStrikeoutDateRange(DateOnly StartDate, DateOnly EndDate);

    private sealed record TheOddsPitcherStrikeoutPropRow(
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        DateOnly EventDate,
        DateTimeOffset ObservedAt);

    private sealed record MlbPitchingResultRow(
        Guid PlayerId,
        Guid PropTypeId,
        string PropTypeName,
        DateOnly EventDate,
        decimal ActualResultValue);

    private sealed record MlbPitcherStrikeoutCoverageRow(
        string PlayerName,
        Guid PlayerId,
        string? MlbStatsId,
        DateOnly FirstRelevantDate,
        DateOnly LatestRelevantDate,
        int TotalImportedGameDatesBeforeEvent,
        int GamesPitchedDetected,
        int PitcherStrikeoutsResultCount,
        int GenericStrikeoutsResultCount,
        int AnyStrikeoutResultCount,
        IReadOnlyList<MlbPitcherStrikeoutResultSample> LatestPitcherStrikeoutResultsSample,
        IReadOnlyList<DateOnly> MissingResultDates,
        string LikelyRootCause);

    private sealed record MlbPitcherStrikeoutResultSample(
        DateOnly EventDate,
        string PropType,
        decimal Value);

    private sealed record MlbPitcherStrikeoutBackfillRequest(
        DateOnly StartDate,
        DateOnly EndDate,
        IReadOnlyList<string>? PlayerNames,
        bool? DryRun);

    private sealed record MlbPitcherStrikeoutResultKey(
        Guid PlayerId,
        Guid LeagueId,
        Guid PropTypeId,
        DateOnly EventDate);
}


