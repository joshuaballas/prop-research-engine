using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;
using SportsBetting.Api.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace SportsBetting.Api.Endpoints;

public static class ResultsDebugEndpoints
{
    private const string ApiSportsFootballResultSource = "api-sports-football";
    private const string ApiSportsFootballPlayerRefProvider = "api-sports-football";

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
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await importService.ImportAsync(request.Date, cancellationToken);

                return Results.Ok(new
                {
                    date = result.Date,
                    recordsFound = result.RecordsFound,
                    recordsMapped = result.RecordsMapped,
                    recordsInserted = result.RecordsInserted,
                    recordsUpdated = result.RecordsUpdated,
                    importDuration = result.ImportDuration
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
                .Where(dateState => dateState.EventDate <= latestCompletedDate && dateState.Status != "succeeded")
                .OrderBy(dateState => dateState.EventDate)
                .Select(dateState => dateState.EventDate)
                .Take(100)
                .ToArrayAsync(cancellationToken);
            var missingDates = coverage is null
                ? []
                : await BuildMissingMlbImportDatesAsync(dbContext, coverage.Start, latestCompletedDate, cancellationToken);
            var dateStatus = date.HasValue
                ? await importService.GetStatusAsync(date.Value, cancellationToken)
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
                dateStatus = dateStatus is null
                    ? null
                    : new
                    {
                        date = dateStatus.Date,
                        importedResults = dateStatus.ImportedResults,
                        distinctPlayers = dateStatus.DistinctPlayers,
                        distinctPropTypes = dateStatus.DistinctPropTypes
                    }
            });
        })
        .WithName("MlbResultsImportStatusDebug")
        .WithSummary("Returns MLB result import worker status and optional date-specific import status.");

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
            var preview = BuildApiSportsSoccerResultPreview(fixtureId, fixtureSummary.EventDate.Value, playerStats);
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

    private static ApiSportsSoccerResultPreview BuildApiSportsSoccerResultPreview(
        string fixtureId,
        DateOnly eventDate,
        IReadOnlyList<ApiSportsPlayerStatsRow> playerStats)
    {
        var rows = new List<ApiSportsSoccerResultPreviewRow>();
        var skipped = new List<ApiSportsSoccerUnsupportedSkippedRow>();

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

            foreach (var mapping in ApiSportsSoccerResultMappings)
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
                    mapping.Notes,
                    $"{ApiSportsFootballResultSource}:{fixtureId}:{player.PlayerId}:{mapping.StatKey}"));
            }
        }

        return new ApiSportsSoccerResultPreview(rows, skipped);
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
        var propTypeByStatKey = await GetOrCreatePropTypesAsync(dbContext, sport, rows.Select(row => (row.PropType, row.StatKey)).Distinct().ToArray(), cancellationToken);
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

    private sealed record MlbResultImportRequest(DateOnly Date);

    private sealed record MlbResultImportRangeRequest(DateOnly StartDate, DateOnly EndDate);

    private sealed record ApiSportsSoccerImportFixtureRequest(
        string FixtureId,
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

    private sealed record ApiSportsSoccerResultPreview(
        IReadOnlyList<ApiSportsSoccerResultPreviewRow> Rows,
        IReadOnlyList<ApiSportsSoccerUnsupportedSkippedRow> UnsupportedSkipped);

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
        string Notes);

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
        new("Goals", "GOALS", ["goals.total"], true, false, "Known", "Maps to API-Sports Football goals.total."),
        new("Assists", "ASSISTS", ["goals.assists"], true, false, "Known", "Maps to API-Sports Football goals.assists."),
        new("Shots", "SHOTS", ["shots.total"], true, false, "Known", "Maps to API-Sports Football shots.total."),
        new("Shots On Target", "SHOTS_ON_TARGET", ["shots.on"], true, false, "Known", "Maps to API-Sports Football shots.on."),
        new("Passes Attempted", "PASSES_ATTEMPTED", ["passes.total"], true, false, "Known", "Maps to API-Sports Football passes.total."),
        new("Tackles", "TACKLES", ["tackles.total"], true, false, "Known", "Maps to API-Sports Football tackles.total."),
        new("Fouls", "FOULS", ["fouls.committed"], true, false, "Known", "Maps to API-Sports Football fouls.committed."),
        new("Fouls Drawn", "FOULS_DRAWN", ["fouls.drawn"], true, false, "Known", "Maps to API-Sports Football fouls.drawn."),
        new("Offsides", "OFFSIDES", ["offsides"], true, false, "Known", "Maps to API-Sports Football offsides."),
        new("Goalie Saves", "GOALIE_SAVES", ["goals.saves"], true, true, "Known", "Goalkeeper-only mapping from API-Sports Football goals.saves."),
        new("Attempted Dribbles", "ATTEMPTED_DRIBBLES", ["dribbles.attempts"], true, false, "Known", "Maps to API-Sports Football dribbles.attempts."),
        new("Goal + Assist", "GOAL_ASSIST", ["goals.total", "goals.assists"], true, false, "NeedsConfirmation", "Conservative mapping: sum goals.total + goals.assists."),
        new("Cards", "CARDS", ["cards.yellow", "cards.red"], true, false, "NeedsConfirmation", "Conservative mapping: sum cards.yellow + cards.red."),
        new("Goals Allowed", "GOALS_ALLOWED", ["goals.conceded"], true, true, "NeedsConfirmation", "Conservative goalkeeper-only mapping from goals.conceded."),
        new("Shots Assisted", "SHOTS_ASSISTED", ["passes.key"], true, false, "NeedsConfirmation", "Conservative mapping: passes.key may correspond to shots assisted/key passes."),
        new("Crosses", "CROSSES", [], false, false, "Unsupported", "Unsupported until API-Sports Football provides a confirmed player crosses field."),
        new("Clearances", "CLEARANCES", [], false, false, "Unsupported", "Unsupported until API-Sports Football provides a confirmed player clearances field."),
        new("Outfield Fantasy Score", "OUTFIELD_FANTASY_SCORE", [], false, false, "Unsupported", "Unsupported provider-scored prop."),
        new("Goalie Fantasy Score", "GOALIE_FANTASY_SCORE", [], false, true, "Unsupported", "Unsupported provider-scored prop."),
        new("Combo props", "COMBO_PROPS", [], false, false, "Unsupported", "Unsupported combo prop family.")
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


