using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace SportsBetting.Api.Endpoints;

public static class ResultsDebugEndpoints
{
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
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
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
