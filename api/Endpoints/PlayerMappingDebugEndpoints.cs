using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Ingestion;
using SportsBetting.Api.Ingestion.Players;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Models;
using SportsBetting.Api.Utilities;

namespace SportsBetting.Api.Endpoints;

public static class PlayerMappingDebugEndpoints
{
    public static IEndpointRouteBuilder MapPlayerMappingDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/player-mappings", async (
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            var mappings = await repository.GetLatestAsync(100, cancellationToken);

            return Results.Ok(new
            {
                count = mappings.Count,
                results = mappings.Select(ToResponse).ToArray()
            });
        })
        .WithName("PlayerMappingsDebug")
        .WithSummary("Returns provider-to-canonical player mappings.");

        endpoints.MapGet("/api/debug/player-mappings/{playerId:guid}", async (
            Guid playerId,
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            var mappings = await repository.GetByPlayerIdAsync(playerId, cancellationToken);

            return Results.Ok(new
            {
                playerId,
                count = mappings.Count,
                results = mappings.Select(ToResponse).ToArray()
            });
        })
        .WithName("PlayerMappingsByPlayerDebug")
        .WithSummary("Returns provider mappings for a specific canonical player.");

        endpoints.MapGet("/api/debug/player-identity", async (
            string name,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { error = "Query parameter 'name' is required." });
            }

            var normalizedName = NormalizePlayerName(name);
            var players = await dbContext.Players
                .AsNoTracking()
                .Include(player => player.League)
                .ThenInclude(league => league!.Sport)
                .Include(player => player.Team)
                .Include(player => player.ExternalProviderRefs)
                .Where(player => player.DisplayName.ToLower() == name.Trim().ToLower())
                .OrderBy(player => player.DisplayName)
                .ThenBy(player => player.League!.Code)
                .ToListAsync(cancellationToken);

            if (players.Count == 0)
            {
                players = await dbContext.Players
                    .AsNoTracking()
                    .Include(player => player.League)
                    .ThenInclude(league => league!.Sport)
                    .Include(player => player.Team)
                    .Include(player => player.ExternalProviderRefs)
                    .Where(player => player.DisplayName.ToLower().Contains(name.Trim().ToLower()))
                    .OrderBy(player => player.DisplayName)
                    .ThenBy(player => player.League!.Code)
                    .ToListAsync(cancellationToken);
            }

            var playerIds = players.Select(player => player.Id).ToArray();
            var platformSources = await dbContext.PlatformProps
                .AsNoTracking()
                .Include(platformProp => platformProp.Platform)
                .Include(platformProp => platformProp.Prop)
                .ThenInclude(prop => prop!.PropType)
                .Where(platformProp => platformProp.Prop != null && playerIds.Contains(platformProp.Prop.PlayerId))
                .GroupBy(platformProp => platformProp.Prop!.PlayerId)
                .Select(group => new
                {
                    PlayerId = group.Key,
                    Platforms = group
                        .Select(platformProp => platformProp.Platform == null ? "Unknown" : platformProp.Platform.Name)
                        .Distinct()
                        .OrderBy(platform => platform)
                        .ToArray(),
                    PlatformPropCount = group.Count(),
                    SampleProps = group
                        .OrderByDescending(platformProp => platformProp.ObservedAt)
                        .Take(10)
                        .Select(platformProp => new
                        {
                            platform = platformProp.Platform == null ? "Unknown" : platformProp.Platform.Name,
                            platformPropId = platformProp.PlatformPropId,
                            label = platformProp.Label,
                            propType = platformProp.Prop!.PropType == null ? string.Empty : platformProp.Prop.PropType.Name,
                            observedAt = platformProp.ObservedAt
                        })
                        .ToArray()
                })
                .ToDictionaryAsync(source => source.PlayerId, cancellationToken);

            var rows = players.Select(player =>
            {
                platformSources.TryGetValue(player.Id, out var platformSource);
                var externalRefSummary = SummarizeExternalRefs(player.ExternalRefs);

                return new
                {
                    playerId = player.Id,
                    displayName = player.DisplayName,
                    normalizedName = NormalizePlayerName(player.DisplayName),
                    firstName = player.FirstName,
                    lastName = player.LastName,
                    position = player.Position,
                    team = player.Team is null
                        ? null
                        : new
                        {
                            teamId = player.Team.Id,
                            player.Team.Name,
                            player.Team.Abbreviation
                        },
                    league = player.League is null
                        ? null
                        : new
                        {
                            leagueId = player.League.Id,
                            player.League.Name,
                            player.League.Code,
                            sport = player.League.Sport == null ? null : player.League.Sport.Code
                        },
                    creationSource = InferCreationSource(player.ExternalRefs, platformSource?.Platforms ?? []),
                    identityKeys = new
                    {
                        sportsbookFallbackKey = player.LeagueId == Guid.Empty ? null : $"{player.LeagueId:N}:{player.DisplayName}",
                        normalizedDisplayName = NormalizePlayerName(player.DisplayName),
                        externalRefs = externalRefSummary
                    },
                    providerRefs = player.ExternalProviderRefs
                        .OrderBy(reference => reference.Provider)
                        .Select(reference => new
                        {
                            reference.Id,
                            reference.Provider,
                            reference.ProviderPlayerId,
                            reference.ProviderDisplayName,
                            reference.CreatedAt,
                            reference.UpdatedAt
                        })
                        .ToArray(),
                    platformSources = platformSource?.Platforms ?? [],
                    platformPropCount = platformSource?.PlatformPropCount ?? 0,
                    sampleProps = platformSource?.SampleProps ?? []
                };
            }).ToArray();

            return Results.Ok(new
            {
                query = name,
                normalizedQuery = normalizedName,
                playerRowCount = rows.Length,
                rootCauseAnalysis = new
                {
                    playerLookupLogic = "Provider ingestion first tries its own provider id from Player.ExternalRefs, then falls back to league_id + display_name.",
                    leagueIdentityIssue = "MLBLIVE is normalized into a separate league row from MLB, so the fallback key differs even when display_name is identical.",
                    crossPlatformIssue = "Provider feeds can use different player id namespaces in Player.ExternalRefs and may not consult player_external_refs or a league-agnostic canonical name key during ingestion.",
                    impact = "The same human can be created once per league variant and once per sportsbook namespace."
                },
                players = rows
            });
        })
        .WithName("PlayerIdentityDebug")
        .WithSummary("Explains canonical player identity rows, provider refs, and sportsbook creation sources for a display name.");

        endpoints.MapGet("/api/debug/player-identity-summary", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var rows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var duplicateGroups = GetDuplicateIdentityGroups(rows);
            var duplicatesByLeague = duplicateGroups
                .SelectMany(group => group.Players)
                .GroupBy(player => player.LeagueCode ?? "UNKNOWN", StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    league = group.Key,
                    duplicateRows = group.Count(),
                    duplicateNames = group.Select(player => player.NormalizedDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                })
                .OrderByDescending(group => group.duplicateRows)
                .ThenBy(group => group.league)
                .ToArray();

            return Results.Ok(new
            {
                totalPlayers = rows.Count,
                uniqueDisplayNames = rows.Select(row => row.NormalizedDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                duplicateRows = duplicateGroups.Sum(group => group.Players.Count - 1),
                duplicateNames = duplicateGroups.Count,
                duplicatesByLeague
            });
        })
        .WithName("PlayerIdentitySummaryDebug")
        .WithSummary("Returns canonical player identity duplicate summary statistics.");

        endpoints.MapGet("/api/debug/player-identity-candidates", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var rows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var candidates = GetDuplicateIdentityGroups(rows)
                .Select(ToDuplicateCandidateResponse)
                .ToArray();

            return Results.Ok(new
            {
                duplicateGroupCount = candidates.Length,
                candidates
            });
        })
        .WithName("PlayerIdentityCandidatesDebug")
        .WithSummary("Returns likely duplicate canonical player groups.");

        endpoints.MapGet("/api/debug/player-identity-merge-preview", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var rows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var duplicateGroups = GetDuplicateIdentityGroups(rows);
            var duplicatePlayerIds = duplicateGroups
                .SelectMany(group => group.Players.Select(player => player.PlayerId))
                .Distinct()
                .ToArray();

            var propRows = await dbContext.Props
                .AsNoTracking()
                .Where(prop => duplicatePlayerIds.Contains(prop.PlayerId))
                .Select(prop => new PlayerMergePropRow(prop.Id, prop.PlayerId))
                .ToListAsync(cancellationToken);

            var propIds = propRows.Select(prop => prop.Id).ToArray();
            var platformPropRows = await dbContext.PlatformProps
                .AsNoTracking()
                .Where(platformProp => propIds.Contains(platformProp.PropId))
                .Select(platformProp => new PlayerMergePlatformPropRow(platformProp.Id, platformProp.PropId))
                .ToListAsync(cancellationToken);

            var platformPropIds = platformPropRows.Select(platformProp => platformProp.Id).ToArray();
            var snapshotRows = await dbContext.PropLineSnapshots
                .AsNoTracking()
                .Where(snapshot => platformPropIds.Contains(snapshot.PlatformPropId))
                .Select(snapshot => new PlayerMergeSnapshotRow(snapshot.Id, snapshot.PlatformPropId))
                .ToListAsync(cancellationToken);

            var mappingRows = await dbContext.PlayerExternalRefs
                .AsNoTracking()
                .Where(reference => duplicatePlayerIds.Contains(reference.PlayerId))
                .Select(reference => new PlayerMergeMappingRow(reference.Id, reference.PlayerId))
                .ToListAsync(cancellationToken);

            var mergeCandidates = duplicateGroups
                .Select(group =>
                {
                    var groupPlayerIds = group.Players.Select(player => player.PlayerId).ToHashSet();
                    var groupPropIds = propRows
                        .Where(prop => groupPlayerIds.Contains(prop.PlayerId))
                        .Select(prop => prop.Id)
                        .ToHashSet();
                    var groupPlatformPropIds = platformPropRows
                        .Where(platformProp => groupPropIds.Contains(platformProp.PropId))
                        .Select(platformProp => platformProp.Id)
                        .ToHashSet();

                    return new
                    {
                        displayName = group.DisplayName,
                        leagueCodes = group.Players.Select(player => player.LeagueCode ?? "UNKNOWN").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(code => code).ToArray(),
                        playerIds = group.Players.Select(player => player.PlayerId).ToArray(),
                        affectedPlayers = group.Players.Count,
                        affectedProps = groupPropIds.Count,
                        affectedSnapshots = snapshotRows.Count(snapshot => groupPlatformPropIds.Contains(snapshot.PlatformPropId)),
                        affectedMappings = mappingRows.Count(mapping => groupPlayerIds.Contains(mapping.PlayerId)),
                        providerRefs = group.Players.SelectMany(player => player.ProviderRefs).ToArray()
                    };
                })
                .OrderByDescending(candidate => candidate.affectedPlayers)
                .ThenBy(candidate => candidate.displayName)
                .ToArray();

            return Results.Ok(new
            {
                duplicateGroupCount = duplicateGroups.Count,
                affectedPlayers = duplicatePlayerIds.Length,
                affectedProps = propRows.Count,
                affectedSnapshots = snapshotRows.Count,
                affectedMappings = mappingRows.Count,
                mergeCandidates
            });
        })
        .WithName("PlayerIdentityMergePreviewDebug")
        .WithSummary("Returns a dry-run preview of player identity merge blast radius.");

        endpoints.MapGet("/api/debug/player-merge-plan", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var rows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var duplicateGroups = GetDuplicateIdentityGroups(rows);
            var duplicatePlayerIds = duplicateGroups
                .SelectMany(group => group.Players.Select(player => player.PlayerId))
                .Distinct()
                .ToArray();

            var propRows = await dbContext.Props
                .AsNoTracking()
                .Where(prop => duplicatePlayerIds.Contains(prop.PlayerId))
                .Select(prop => new PlayerMergePropRow(prop.Id, prop.PlayerId))
                .ToListAsync(cancellationToken);

            var propIds = propRows.Select(prop => prop.Id).ToArray();
            var platformPropRows = await dbContext.PlatformProps
                .AsNoTracking()
                .Where(platformProp => propIds.Contains(platformProp.PropId))
                .Select(platformProp => new PlayerMergePlatformPropRow(platformProp.Id, platformProp.PropId))
                .ToListAsync(cancellationToken);

            var platformPropIds = platformPropRows.Select(platformProp => platformProp.Id).ToArray();
            var snapshotRows = await dbContext.PropLineSnapshots
                .AsNoTracking()
                .Where(snapshot => platformPropIds.Contains(snapshot.PlatformPropId))
                .Select(snapshot => new PlayerMergeSnapshotRow(snapshot.Id, snapshot.PlatformPropId))
                .ToListAsync(cancellationToken);

            var resultRows = await dbContext.PropResults
                .AsNoTracking()
                .Where(result => duplicatePlayerIds.Contains(result.PlayerId))
                .Select(result => new PlayerMergeResultRow(result.Id, result.PlayerId))
                .ToListAsync(cancellationToken);

            var mappingRows = await dbContext.PlayerExternalRefs
                .AsNoTracking()
                .Where(reference => duplicatePlayerIds.Contains(reference.PlayerId))
                .Select(reference => new PlayerMergeMappingRow(reference.Id, reference.PlayerId))
                .ToListAsync(cancellationToken);

            var mergeCandidates = duplicateGroups
                .Select(group => BuildPlayerMergeCandidate(
                    group,
                    propRows,
                    platformPropRows,
                    snapshotRows,
                    mappingRows,
                    resultRows))
                .OrderBy(candidate => candidate.Conflicts.Count)
                .ThenByDescending(candidate => candidate.DuplicatePlayerIds.Count)
                .ThenBy(candidate => candidate.DisplayName)
                .ToArray();

            var safeMergeGroups = mergeCandidates.Count(candidate => candidate.Conflicts.Count == 0);
            var manualReviewGroups = mergeCandidates.Length - safeMergeGroups;

            return Results.Ok(new
            {
                duplicateGroups = duplicateGroups.Count,
                safeMergeGroups,
                manualReviewGroups,
                analysis = new
                {
                    estimatedSafeMergeCount = safeMergeGroups,
                    estimatedManualReviewCount = manualReviewGroups,
                    survivorSelection = "Prefer player with most external references, then most props, then oldest created_at.",
                    conflictDetection = new[]
                    {
                        "Different normalized names",
                        "Multiple MLB mappings",
                        "Conflicting provider IDs",
                        "Conflicting teams",
                        "Conflicting canonical leagues"
                    },
                    migrationSequence = new[]
                    {
                        "Create a database backup and export the generated merge plan.",
                        "For each safe group, choose the planned survivor player_id.",
                        "Repoint props.player_id from duplicate ids to the survivor.",
                        "Repoint prop_results.player_id from duplicate ids to the survivor.",
                        "Repoint player_external_refs.player_id from duplicate ids to the survivor after validating provider/player id uniqueness.",
                        "Merge legacy players.external_refs into the survivor when the provider ids do not conflict.",
                        "Delete duplicate player rows only after dependent rows are repointed and validated.",
                        "Re-run player identity summary, MLB reconciliation health, market comparison, and line history diagnostics."
                    },
                    rollbackStrategy = new[]
                    {
                        "Run the merge in one transaction per group or one bounded batch transaction.",
                        "Persist a merge audit table or external audit file containing duplicate player ids, survivor id, and all original foreign-key targets before updates.",
                        "Rollback the transaction immediately if any conflict appears during execution.",
                        "If a committed batch must be reverted, use the audit data to restore original props, prop_results, and player_external_refs player_id values, then recreate deleted player rows from backup."
                    }
                },
                mergeCandidates
            });
        })
        .WithName("PlayerMergePlanDebug")
        .WithSummary("Returns a read-only canonical player merge plan with survivor selection and conflict diagnostics.");

        endpoints.MapGet("/api/debug/player-merge-conflicts", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var mergeCandidates = await LoadPlayerMergeCandidatesAsync(dbContext, cancellationToken);
            var conflictTypes = new[]
            {
                "MultipleMlbMappings",
                "ConflictingProviderIds",
                "ConflictingTeams",
                "ConflictingLeagues",
                "DifferentNormalizedNames"
            };
            var conflictSummary = conflictTypes
                .Select(conflictType => new
                {
                    conflictType,
                    count = mergeCandidates.Count(candidate => candidate.Conflicts.Contains(conflictType, StringComparer.OrdinalIgnoreCase))
                })
                .OrderByDescending(summary => summary.count)
                .ThenBy(summary => summary.conflictType)
                .ToArray();

            var canonicalMlbPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
            var canonicalMlbPlayerIds = canonicalMlbPlayers.Select(player => player.Id).ToHashSet();
            var mappedMlbPlayerIds = await dbContext.PlayerExternalRefs
                .AsNoTracking()
                .Where(reference => reference.Provider == "mlb")
                .Select(reference => reference.PlayerId)
                .ToListAsync(cancellationToken);
            var mappedMlbPlayerIdSet = mappedMlbPlayerIds.ToHashSet();
            var currentlyMappedMlbPlayers = canonicalMlbPlayerIds.Count(mappedMlbPlayerIdSet.Contains);
            var safeMergeResolvableMlbPlayers = CountMergeResolvableMlbPlayers(
                mergeCandidates.Where(candidate => candidate.Conflicts.Count == 0),
                canonicalMlbPlayerIds,
                mappedMlbPlayerIdSet);
            var allMergeResolvableMlbPlayers = CountMergeResolvableMlbPlayers(
                mergeCandidates,
                canonicalMlbPlayerIds,
                mappedMlbPlayerIdSet);
            var safeMergeGroups = mergeCandidates.Count(candidate => candidate.Conflicts.Count == 0);
            var duplicateGroups = mergeCandidates.Count;
            var safeMergeCoveragePercent = duplicateGroups == 0
                ? 0m
                : Math.Round(safeMergeGroups * 100m / duplicateGroups, 2);
            var projectedCoverageAfterSafeMerge = CalculateProjectedCoverage(
                canonicalMlbPlayerIds.Count,
                currentlyMappedMlbPlayers,
                safeMergeResolvableMlbPlayers);
            var projectedCoverageAfterAllMergeCandidates = CalculateProjectedCoverage(
                canonicalMlbPlayerIds.Count,
                currentlyMappedMlbPlayers,
                allMergeResolvableMlbPlayers);
            var sampleGroups = mergeCandidates
                .Where(candidate => candidate.Conflicts.Count > 0)
                .OrderByDescending(candidate => candidate.Conflicts.Count)
                .ThenByDescending(candidate => candidate.DuplicatePlayerIds.Count)
                .ThenBy(candidate => candidate.DisplayName)
                .Take(50)
                .ToArray();

            return Results.Ok(new
            {
                conflictSummary,
                sampleGroups,
                safeMergeCoveragePercent,
                projectedCoverageAfterSafeMerge,
                projectedCoverageAfterAllMergeCandidates,
                duplicateGroups,
                safeMergeGroups,
                manualReviewGroups = duplicateGroups - safeMergeGroups,
                mlbCoverage = new
                {
                    totalMlbPlayers = canonicalMlbPlayerIds.Count,
                    currentlyMappedMlbPlayers,
                    safeMergeResolvableMlbPlayers,
                    allMergeResolvableMlbPlayers
                }
            });
        })
        .WithName("PlayerMergeConflictsDebug")
        .WithSummary("Returns canonical player merge conflict summaries and projected MLB coverage impact.");

        endpoints.MapPost("/api/debug/player-merge-safe", async (
            ApplicationDbContext dbContext,
            IPlayerMergeExecutionState mergeState,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("PlayerMappingDebugEndpoints");
            var startedAt = DateTimeOffset.UtcNow;
            var candidates = await LoadPlayerMergeCandidatesAsync(dbContext, cancellationToken);
            var safeCandidates = candidates
                .Where(candidate => candidate.Conflicts.Count == 0)
                .ToArray();
            var groupsProcessed = 0;
            var groupsSucceeded = 0;
            var groupsFailed = 0;
            var playersMerged = 0;
            var propsReassigned = 0;
            var snapshotsAffected = 0;
            var mappingsReassigned = 0;
            var resultsReassigned = 0;
            var failures = new List<object>();

            foreach (var candidate in safeCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                groupsProcessed++;

                try
                {
                    var result = await ExecuteSafePlayerMergeAsync(dbContext, candidate, cancellationToken);
                    groupsSucceeded++;
                    playersMerged += result.PlayersMerged;
                    propsReassigned += result.PropsReassigned;
                    snapshotsAffected += result.SnapshotsAffected;
                    mappingsReassigned += result.MappingsReassigned;
                    resultsReassigned += result.ResultsReassigned;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    dbContext.ChangeTracker.Clear();
                    groupsFailed++;
                    failures.Add(new
                    {
                        candidate.DisplayName,
                        candidate.SurvivorPlayerId,
                        candidate.DuplicatePlayerIds,
                        exceptionType = exception.GetType().Name,
                        exception.Message
                    });
                    logger.LogError(
                        exception,
                        "Safe player merge failed for {DisplayName} survivor {SurvivorPlayerId}.",
                        candidate.DisplayName,
                        candidate.SurvivorPlayerId);
                }
            }

            var completedAt = DateTimeOffset.UtcNow;
            var status = new PlayerMergeExecutionStatusSnapshot(
                groupsProcessed,
                groupsSucceeded,
                groupsFailed,
                playersMerged,
                propsReassigned,
                snapshotsAffected,
                mappingsReassigned,
                resultsReassigned,
                startedAt,
                completedAt);
            mergeState.Update(status);

            var identitySummary = await BuildPlayerIdentitySummaryAsync(dbContext, cancellationToken);
            var mlbReconciliationHealth = await BuildMlbReconciliationHealthAsync(dbContext, cancellationToken);

            return Results.Ok(new
            {
                groupsProcessed,
                groupsSucceeded,
                groupsFailed,
                playersMerged,
                propsReassigned,
                snapshotsAffected,
                mappingsReassigned,
                resultsReassigned,
                startedAt,
                completedAt,
                failures,
                playerIdentitySummary = identitySummary,
                mlbReconciliationHealth
            });
        })
        .WithName("ExecuteSafePlayerMergeDebug")
        .WithSummary("Executes only zero-conflict canonical player merge groups and records merge audit rows.");

        endpoints.MapGet("/api/debug/player-merge-safe/status", (
            IPlayerMergeExecutionState mergeState) =>
        {
            var status = mergeState.Snapshot();

            return Results.Ok(new
            {
                groupsProcessed = status.GroupsProcessed,
                groupsSucceeded = status.GroupsSucceeded,
                groupsFailed = status.GroupsFailed,
                playersMerged = status.PlayersMerged,
                propsReassigned = status.PropsReassigned,
                snapshotsAffected = status.SnapshotsAffected,
                mappingsReassigned = status.MappingsReassigned,
                resultsReassigned = status.ResultsReassigned,
                startedAt = status.StartedAt,
                completedAt = status.CompletedAt
            });
        })
        .WithName("SafePlayerMergeStatusDebug")
        .WithSummary("Returns latest safe canonical player merge execution status.");

        endpoints.MapGet("/api/debug/player-resolution-stats", (
            IPlayerResolutionStats stats) =>
        {
            var snapshot = stats.Snapshot();

            return Results.Ok(new
            {
                resolvedByExternalRef = snapshot.ResolvedByExternalRef,
                resolvedByLegacyRef = snapshot.ResolvedByLegacyRef,
                resolvedByFallback = snapshot.ResolvedByFallback,
                newlyCreated = snapshot.NewlyCreated
            });
        })
        .WithName("PlayerResolutionStatsDebug")
        .WithSummary("Returns runtime player resolution counters for sportsbook ingestion.");

        endpoints.MapGet("/api/debug/player-resolution-created", (
            IPlayerResolutionStats stats) =>
        {
            var createdPlayers = stats.CreatedPlayers()
                .Select(player => new
                {
                    playerId = player.PlayerId,
                    displayName = player.DisplayName,
                    leagueCode = player.LeagueCode,
                    provider = player.Provider,
                    providerPlayerId = player.ProviderPlayerId,
                    createdAt = player.CreatedAt,
                    creationReason = player.CreationReason
                })
                .ToArray();

            return Results.Ok(new
            {
                createdPlayers
            });
        })
        .WithName("PlayerResolutionCreatedDebug")
        .WithSummary("Returns the rolling in-memory history of players created by PlayerResolutionService.");

        endpoints.MapGet("/api/debug/player-resolution-created-summary", (
            IPlayerResolutionStats stats) =>
        {
            var snapshot = stats.Snapshot();
            var createdPlayers = stats.CreatedPlayers();
            var byProvider = createdPlayers
                .GroupBy(player => player.Provider, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => new
                {
                    provider = group.Key,
                    count = group.Count()
                })
                .ToArray();
            var byLeague = createdPlayers
                .GroupBy(player => player.LeagueCode, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => new
                {
                    leagueCode = group.Key,
                    count = group.Count()
                })
                .ToArray();

            return Results.Ok(new
            {
                totalCreated = snapshot.NewlyCreated,
                byProvider,
                byLeague
            });
        })
        .WithName("PlayerResolutionCreatedSummaryDebug")
        .WithSummary("Returns provider and league summaries for the rolling created-player history.");

        endpoints.MapGet("/api/debug/mlb-player-candidates", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var candidateSet = await GetMlbCandidateSetAsync(dbContext, mlbResultsProvider, repository, targetSeason, cancellationToken);
            var orderedCandidates = OrderMlbCandidates(candidateSet.Candidates);

            loggerFactory.CreateLogger("PlayerMappingDebugEndpoints").LogInformation(
                "Analyzed {CandidateCount} MLB player mapping candidates for {CanonicalPlayerCount} canonical MLB prop players.",
                orderedCandidates.Length,
                candidateSet.TotalCanonicalPlayers);

            return Results.Ok(new
            {
                provider = "mlb",
                season = targetSeason,
                candidateCount = orderedCandidates.Length,
                candidates = orderedCandidates
            });
        })
        .WithName("MlbPlayerCandidatesDebug")
        .WithSummary("Returns likely MLB Stats API player mappings for canonical MLB prop players.");

        endpoints.MapGet("/api/debug/mlb-player-candidates/summary", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var candidateSet = await GetMlbCandidateSetAsync(dbContext, mlbResultsProvider, repository, targetSeason, cancellationToken);
            var buckets = candidateSet.Candidates
                .GroupBy(candidate => candidate.Confidence)
                .OrderByDescending(group => group.Key)
                .Select(group => new
                {
                    confidence = group.Key,
                    count = group.Count()
                })
                .ToArray();

            loggerFactory.CreateLogger("PlayerMappingDebugEndpoints").LogInformation(
                "Analyzed {CandidateCount} MLB player mapping candidates for summary across {CanonicalPlayerCount} canonical MLB prop players.",
                candidateSet.Candidates.Count,
                candidateSet.TotalCanonicalPlayers);

            return Results.Ok(new
            {
                totalCandidates = candidateSet.Candidates.Count,
                confidenceBuckets = buckets
            });
        })
        .WithName("MlbPlayerCandidateSummaryDebug")
        .WithSummary("Returns MLB player candidate counts by confidence bucket.");

        endpoints.MapGet("/api/debug/mlb-player-candidates/high-confidence", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var candidateSet = await GetMlbCandidateSetAsync(dbContext, mlbResultsProvider, repository, targetSeason, cancellationToken);
            var highConfidenceCandidates = GetHighConfidenceSingleCandidates(candidateSet.Candidates);

            loggerFactory.CreateLogger("PlayerMappingDebugEndpoints").LogInformation(
                "Analyzed {CandidateCount} MLB candidates and found {HighConfidenceCount} high-confidence single matches.",
                candidateSet.Candidates.Count,
                highConfidenceCandidates.Length);

            return Results.Ok(new
            {
                provider = "mlb",
                season = targetSeason,
                candidateCount = highConfidenceCandidates.Length,
                candidates = highConfidenceCandidates
            });
        })
        .WithName("MlbHighConfidencePlayerCandidatesDebug")
        .WithSummary("Returns high-confidence MLB player candidates where exactly one candidate exists for the canonical player.");

        endpoints.MapGet("/api/debug/mlb-player-candidates/skipped", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var skipped = await GetMlbSkippedCandidatesAsync(dbContext, mlbResultsProvider, repository, targetSeason, cancellationToken);

            return Results.Ok(new
            {
                provider = "mlb",
                season = targetSeason,
                skippedCount = skipped.Length,
                skipped
            });
        })
        .WithName("MlbSkippedPlayerCandidatesDebug")
        .WithSummary("Returns MLB player candidate diagnostics for mappings that were not auto-approved.");

        endpoints.MapGet("/api/debug/mlb-player-candidates/skipped-summary", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var skipped = await GetMlbSkippedCandidatesAsync(dbContext, mlbResultsProvider, repository, targetSeason, cancellationToken);
            var byReason = skipped
                .GroupBy(item => item.Reason)
                .OrderBy(group => group.Key)
                .Select(group => new
                {
                    reason = group.Key,
                    count = group.Count()
                })
                .ToArray();

            return Results.Ok(new
            {
                totalSkipped = skipped.Length,
                byReason
            });
        })
        .WithName("MlbSkippedPlayerCandidateSummaryDebug")
        .WithSummary("Returns MLB skipped candidate diagnostics grouped by reason.");

        endpoints.MapGet("/api/debug/mlb-player-mappings/provider-line-repair-candidates", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var candidates = await BuildProviderLineMlbRepairCandidatesAsync(dbContext, cancellationToken);
            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                unmappedProviderLinePlayers = candidates.Count,
                safeAutoApprovalCandidates = candidates.Count(candidate => candidate.IsSafeAutoApproval),
                ambiguousOrManualCandidates = candidates.Count(candidate => !candidate.IsSafeAutoApproval),
                candidates
            });
        })
        .WithName("ProviderLineMlbRepairCandidatesDebug")
        .WithSummary("Local-only repair candidates for MLB provider-line players missing MLB refs.");

        endpoints.MapPost("/api/debug/mlb-player-mappings/provider-line-repair/auto-approve", async (
            ProviderLineMlbRepairAutoApproveRequest request,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var dryRun = request.DryRun != false;
            var candidates = await BuildProviderLineMlbRepairCandidatesAsync(dbContext, cancellationToken);
            var safeCandidates = candidates
                .Where(candidate => candidate.IsSafeAutoApproval)
                .Take(Math.Clamp(request.Take ?? 100, 1, 500))
                .ToArray();
            var rows = new List<object>();
            var observedAt = DateTimeOffset.UtcNow;

            foreach (var candidate in safeCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mlbId = candidate.RecommendedMlbPlayerId;
                if (string.IsNullOrWhiteSpace(mlbId))
                {
                    rows.Add(new { candidate.PlayerId, candidate.DisplayName, action = "Skipped", reason = "No recommended MLB id." });
                    continue;
                }

                var existingForPlayer = await dbContext.PlayerExternalRefs
                    .SingleOrDefaultAsync(reference => reference.PlayerId == candidate.PlayerId
                        && reference.Provider == "mlb", cancellationToken);
                var existingForMlbId = await dbContext.PlayerExternalRefs
                    .AsNoTracking()
                    .Where(reference => reference.Provider == "mlb"
                        && reference.ProviderPlayerId == mlbId)
                    .Select(reference => new { reference.PlayerId, reference.ProviderPlayerId, reference.ProviderDisplayName })
                    .ToArrayAsync(cancellationToken);
                if (existingForPlayer is not null || existingForMlbId.Any(reference => reference.PlayerId != candidate.PlayerId))
                {
                    rows.Add(new
                    {
                        candidate.PlayerId,
                        candidate.DisplayName,
                        mlbPlayerId = mlbId,
                        action = "SkippedConflict",
                        reason = "Mapping changed after candidate generation."
                    });
                    continue;
                }

                if (!dryRun)
                {
                    dbContext.PlayerExternalRefs.Add(new PlayerExternalRef
                    {
                        Id = Guid.NewGuid(),
                        PlayerId = candidate.PlayerId,
                        Provider = "mlb",
                        ProviderPlayerId = mlbId,
                        ProviderDisplayName = candidate.RecommendedMlbDisplayName ?? candidate.DisplayName,
                        CreatedAt = observedAt,
                        UpdatedAt = observedAt
                    });
                }

                rows.Add(new
                {
                    candidate.PlayerId,
                    candidate.DisplayName,
                    mlbPlayerId = mlbId,
                    action = dryRun ? "WouldCreate" : "Created",
                    reason = candidate.SafeAutoApprovalReason
                });
            }

            if (!dryRun)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new
            {
                dryRun,
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                approvedBy = request.ApprovedBy,
                reason = request.Reason,
                totalCandidates = candidates.Count,
                safeAutoApprovalCandidates = safeCandidates.Length,
                rows
            });
        })
        .WithName("ProviderLineMlbRepairAutoApproveDebug")
        .WithSummary("Dry-run by default local-only auto-approval for safe MLB provider-line mapping repairs.");

        endpoints.MapPost("/api/debug/mlb-player-mappings/approve", async (
            MlbPlayerMappingApprovalRequest request,
            ApplicationDbContext dbContext,
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (request.PlayerId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "playerId is required." });
            }

            if (string.IsNullOrWhiteSpace(request.MlbPlayerId))
            {
                return Results.BadRequest(new { error = "mlbPlayerId is required." });
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.BadRequest(new { error = "displayName is required." });
            }

            var canonicalPlayer = await dbContext.Players
                .Include(player => player.League)
                .SingleOrDefaultAsync(player => player.Id == request.PlayerId, cancellationToken);

            if (canonicalPlayer is null)
            {
                return Results.NotFound(new { error = $"Canonical player '{request.PlayerId}' was not found." });
            }

            if (canonicalPlayer.League is null || canonicalPlayer.League.Code != "MLB")
            {
                return Results.BadRequest(new { error = "MLB player mappings can only be approved for canonical MLB players." });
            }

            var existingPlayerMlbRefs = await dbContext.PlayerExternalRefs
                .AsNoTracking()
                .Where(reference => reference.PlayerId == canonicalPlayer.Id
                    && reference.Provider == "mlb")
                .Select(reference => new
                {
                    reference.PlayerId,
                    reference.Provider,
                    reference.ProviderPlayerId,
                    reference.ProviderDisplayName
                })
                .ToArrayAsync(cancellationToken);
            var conflictingPlayerRefs = existingPlayerMlbRefs
                .Where(reference => !string.Equals(reference.ProviderPlayerId, request.MlbPlayerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (conflictingPlayerRefs.Length > 0)
            {
                return Results.Conflict(new
                {
                    error = "Canonical player already has a different MLB player mapping. This endpoint does not replace mappings.",
                    existingMappings = conflictingPlayerRefs
                });
            }

            var existingReference = await repository.GetByProviderPlayerIdAsync("mlb", request.MlbPlayerId, cancellationToken);
            if (existingReference is not null)
            {
                if (existingReference.PlayerId == canonicalPlayer.Id)
                {
                    return Results.Ok(new
                    {
                        created = false,
                        mapping = ToResponse(existingReference)
                    });
                }

                return Results.Conflict(new
                {
                    error = "MLB player is already mapped to a different canonical player.",
                    existingMapping = ToResponse(existingReference)
                });
            }

            var observedAt = DateTimeOffset.UtcNow;
            var reference = new PlayerExternalRef
            {
                Id = Guid.NewGuid(),
                PlayerId = canonicalPlayer.Id,
                Provider = "mlb",
                ProviderPlayerId = request.MlbPlayerId,
                ProviderDisplayName = request.DisplayName,
                CreatedAt = observedAt,
                UpdatedAt = observedAt,
                Player = canonicalPlayer
            };

            await repository.AddAsync(reference, cancellationToken);

            return Results.Ok(new
            {
                created = true,
                mapping = ToResponse(reference)
            });
        })
        .WithName("ApproveMlbPlayerMappingDebug")
        .WithSummary("Manually approves one MLB Stats API provider player mapping.");

        endpoints.MapPost("/api/debug/mlb-player-mappings/auto-approve", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var candidateSet = await GetMlbCandidateSetAsync(dbContext, mlbResultsProvider, repository, targetSeason, cancellationToken);
            var highConfidenceCandidates = GetHighConfidenceSingleCandidates(candidateSet.Candidates);
            var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
            var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
            var skippedCount = 0;
            var mappingsCreated = new List<PlayerExternalRefResponse>();

            foreach (var candidate in highConfidenceCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (mappedPlayerIds.Contains(candidate.PlayerId) || existingRefs.ContainsKey(candidate.CandidateMlbPlayerId))
                {
                    skippedCount++;
                    continue;
                }

                var observedAt = DateTimeOffset.UtcNow;
                var reference = new PlayerExternalRef
                {
                    Id = Guid.NewGuid(),
                    PlayerId = candidate.PlayerId,
                    Provider = "mlb",
                    ProviderPlayerId = candidate.CandidateMlbPlayerId,
                    ProviderDisplayName = candidate.CandidateDisplayName,
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                };

                await repository.AddAsync(reference, cancellationToken);
                existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
                mappedPlayerIds.Add(candidate.PlayerId);
                mappingsCreated.Add(ToResponse(reference));
            }

            var coverage = await GetMlbMappingCoverageAsync(dbContext, repository, cancellationToken);
            loggerFactory.CreateLogger("PlayerMappingDebugEndpoints").LogInformation(
                "Auto-approved {ApprovedCount} MLB mappings, skipped {SkippedCount}, final coverage {CoveragePercent}%.",
                mappingsCreated.Count,
                skippedCount,
                coverage.CoveragePercent);

            return Results.Ok(new
            {
                approvedCount = mappingsCreated.Count,
                skippedCount,
                mappingsCreated
            });
        })
        .WithName("AutoApproveMlbPlayerMappingsDebug")
        .WithSummary("Auto-approves high-confidence single MLB player mapping candidates.");

        endpoints.MapGet("/api/debug/mlb-player-mappings", async (
            ApplicationDbContext dbContext,
            IPlayerExternalRefRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var coverage = await GetMlbMappingCoverageAsync(dbContext, repository, cancellationToken);

            loggerFactory.CreateLogger("PlayerMappingDebugEndpoints").LogInformation(
                "MLB player mapping coverage: {MappedPlayers}/{TotalMlbPlayers} ({CoveragePercent}%).",
                coverage.MappedPlayers,
                coverage.TotalMlbPlayers,
                coverage.CoveragePercent);

            return Results.Ok(new
            {
                totalMlbPlayers = coverage.TotalMlbPlayers,
                mappedPlayers = coverage.MappedPlayers,
                unmappedPlayers = coverage.UnmappedPlayers,
                coveragePercent = coverage.CoveragePercent
            });
        })
        .WithName("MlbPlayerMappingCoverageDebug")
        .WithSummary("Returns MLB player mapping coverage for canonical MLB prop players.");

        endpoints.MapGet("/api/debug/mlb/mapping-coverage", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var activeMlbPlayers = await GetActiveMlbPlayerCoverageRowsAsync(dbContext, cancellationToken);
            var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
            var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
            var providerPlayers = await mlbResultsProvider.GetPlayersAsync(targetSeason, cancellationToken);
            var providerPlayersByNormalizedName = providerPlayers
                .GroupBy(player => NormalizePlayerName(player.DisplayName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(player => player.DisplayName)
                        .ThenBy(player => player.MlbPlayerId)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var identityRows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var duplicateGroups = GetDuplicateIdentityGroups(identityRows);
            var duplicateGroupByPlayerId = duplicateGroups
                .SelectMany(group => group.Players.Select(player => new
                {
                    player.PlayerId,
                    Group = group
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Group);
            var historicalResultCountsByPlayer = await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.League)
                .Where(result => result.League != null && result.League.Code == "MLB")
                .GroupBy(result => result.PlayerId)
                .Select(group => new
                {
                    PlayerId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(group => group.PlayerId, group => group.Count, cancellationToken);
            var playerRows = activeMlbPlayers
                .Select(player => BuildMlbMappingCoveragePlayerResponse(
                    player,
                    mappedPlayerIds,
                    existingRefs.Values,
                    providerPlayersByNormalizedName,
                    duplicateGroupByPlayerId,
                    historicalResultCountsByPlayer))
                .ToArray();
            var mappedPlayers = playerRows.Count(player => player.HasMlbMapping);
            var unmappedPlayers = playerRows.Length - mappedPlayers;
            var totalMlbProps = playerRows.Sum(player => player.ActivePropCount);
            var mappedProps = playerRows.Where(player => player.HasMlbMapping).Sum(player => player.ActivePropCount);
            var unmappedProps = totalMlbProps - mappedProps;
            var unmappedRows = playerRows
                .Where(player => !player.HasMlbMapping)
                .OrderByDescending(player => player.ActivePropCount)
                .ThenBy(player => player.DisplayName)
                .ToArray();

            return Results.Ok(new
            {
                provider = "mlb",
                season = targetSeason,
                totalMlbPlayers = playerRows.Length,
                mappedPlayers,
                unmappedPlayers,
                totalMlbProps,
                mappedProps,
                unmappedProps,
                estimatedRecoverableProps = unmappedRows
                    .Where(player => player.PossibleMlbPlayerIds.Count > 0 || player.DuplicateIdentityCandidates.Count > 0)
                    .Sum(player => player.ActivePropCount),
                estimatedRecoverableHistoricalResultLookups = unmappedRows.Sum(player => player.RecoverableHistoricalResultCount),
                players = playerRows
                    .OrderBy(player => player.HasMlbMapping)
                    .ThenByDescending(player => player.ActivePropCount)
                    .ThenBy(player => player.DisplayName)
                    .ToArray(),
                unmappedPlayerDetails = unmappedRows
            });
        })
        .WithName("MlbDetailedMappingCoverageDebug")
        .WithSummary("Returns read-only MLB mapping recovery diagnostics for active MLB prop players.");

        endpoints.MapGet("/api/debug/mlb-player-mappings/unmapped-analysis", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
            var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
            var providerPlayers = await mlbResultsProvider.GetPlayersAsync(targetSeason, cancellationToken);
            var canonicalPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
            var unmappedPlayers = canonicalPlayers
                .Where(player => !mappedPlayerIds.Contains(player.Id))
                .OrderBy(player => player.DisplayName)
                .ThenBy(player => player.Id)
                .ToArray();
            var identityRows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var duplicateGroups = GetDuplicateIdentityGroups(identityRows);
            var duplicateGroupByPlayerId = duplicateGroups
                .SelectMany(group => group.Players.Select(player => new
                {
                    player.PlayerId,
                    Group = group
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Group);
            var analyses = unmappedPlayers
                .Select(player => AnalyzeUnmappedMlbPlayer(
                    player,
                    providerPlayers,
                    existingRefs,
                    duplicateGroupByPlayerId))
                .OrderBy(analysis => analysis.Reason)
                .ThenByDescending(analysis => analysis.CandidateCount)
                .ThenBy(analysis => analysis.PlayerName)
                .ToArray();
            var byReason = analyses
                .GroupBy(analysis => analysis.Reason)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => new
                {
                    reason = group.Key,
                    count = group.Count()
                })
                .ToArray();
            var summaryByReason = analyses
                .GroupBy(analysis => analysis.Reason)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var canonicalMappedPlayers = canonicalPlayers.Count(player => mappedPlayerIds.Contains(player.Id));
            var examples = analyses
                .GroupBy(analysis => analysis.Reason)
                .OrderBy(group => group.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(analysis => analysis.CandidateCount)
                        .ThenBy(analysis => analysis.PlayerName)
                        .Take(10)
                        .ToArray());

            return Results.Ok(new
            {
                provider = "mlb",
                season = targetSeason,
                summary = new
                {
                    mappedPlayers = canonicalMappedPlayers,
                    unmappedPlayers = analyses.Length,
                    providerPlayersInSeasonFeed = providerPlayers.Count,
                    noCandidateFound = summaryByReason.GetValueOrDefault("NoCandidateFound"),
                    multipleCandidates = summaryByReason.GetValueOrDefault("MultipleCandidates"),
                    confidenceBelowThreshold = summaryByReason.GetValueOrDefault("ConfidenceBelowThreshold"),
                    providerPlayerAlreadyMapped = summaryByReason.GetValueOrDefault("ProviderPlayerAlreadyMapped"),
                    duplicateCanonicalIdentityConflict = summaryByReason.GetValueOrDefault("DuplicateCanonicalIdentityConflict"),
                    inactiveOrMissingFromSeasonFeed = summaryByReason.GetValueOrDefault("InactiveOrMissingFromSeasonFeed"),
                    other = summaryByReason.GetValueOrDefault("Other"),
                    byReason
                },
                examples,
                players = analyses
            });
        })
        .WithName("MlbUnmappedPlayerAnalysisDebug")
        .WithSummary("Classifies why unmapped MLB players did not pass auto-approval.");

        endpoints.MapGet("/api/debug/player-identity-conflict-analysis", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
            var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
            var providerPlayers = await mlbResultsProvider.GetPlayersAsync(targetSeason, cancellationToken);
            var providerPlayersById = providerPlayers.ToDictionary(player => player.MlbPlayerId, StringComparer.OrdinalIgnoreCase);
            var canonicalPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
            var unmappedCanonicalPlayers = canonicalPlayers
                .Where(player => !mappedPlayerIds.Contains(player.Id))
                .ToArray();
            var identityRows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var duplicateGroups = GetDuplicateIdentityGroups(identityRows);
            var duplicateGroupByPlayerId = duplicateGroups
                .SelectMany(group => group.Players.Select(player => new
                {
                    player.PlayerId,
                    Group = group
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Group);

            var duplicatePlayerIds = duplicateGroups
                .SelectMany(group => group.Players.Select(player => player.PlayerId))
                .Distinct()
                .ToArray();
            var propRows = await dbContext.Props
                .AsNoTracking()
                .Where(prop => duplicatePlayerIds.Contains(prop.PlayerId))
                .Select(prop => new PlayerIdentityConflictPropRow(prop.Id, prop.PlayerId))
                .ToListAsync(cancellationToken);
            var propIds = propRows.Select(prop => prop.Id).ToArray();
            var sportsbookCountsByPlayer = await dbContext.PlatformProps
                .AsNoTracking()
                .Where(platformProp => propIds.Contains(platformProp.PropId))
                .Join(
                    dbContext.Props.AsNoTracking(),
                    platformProp => platformProp.PropId,
                    prop => prop.Id,
                    (platformProp, prop) => new
                    {
                        prop.PlayerId,
                        platformProp.PlatformId
                    })
                .GroupBy(row => row.PlayerId)
                .Select(group => new
                {
                    PlayerId = group.Key,
                    Count = group.Select(row => row.PlatformId).Distinct().Count()
                })
                .ToDictionaryAsync(group => group.PlayerId, group => group.Count, cancellationToken);
            var resultCountsByPlayer = await dbContext.PropResults
                .AsNoTracking()
                .Where(result => duplicatePlayerIds.Contains(result.PlayerId))
                .GroupBy(result => result.PlayerId)
                .Select(group => new
                {
                    PlayerId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(group => group.PlayerId, group => group.Count, cancellationToken);
            var mergeCandidatesByDisplayName = await LoadPlayerMergeCandidatesAsync(dbContext, cancellationToken);
            var mergeCandidateByPlayerId = mergeCandidatesByDisplayName
                .SelectMany(candidate => candidate.AllPlayerIds.Select(playerId => new
                {
                    PlayerId = playerId,
                    Candidate = candidate
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Candidate);

            var conflicts = unmappedCanonicalPlayers
                .SelectMany(canonicalPlayer => BuildPlayerIdentityConflictResponses(
                    canonicalPlayer,
                    duplicateGroupByPlayerId,
                    mergeCandidateByPlayerId,
                    propRows,
                    sportsbookCountsByPlayer,
                    resultCountsByPlayer,
                    providerPlayersById))
                .OrderByDescending(conflict => conflict.PropCount)
                .ThenByDescending(conflict => conflict.HistoricalResultCount)
                .ThenBy(conflict => conflict.DisplayName)
                .ThenBy(conflict => conflict.ConflictingDisplayName)
                .ToArray();
            var safeMergeCandidates = conflicts.Count(conflict => conflict.SafeToMerge);

            return Results.Ok(new
            {
                provider = "mlb",
                season = targetSeason,
                summary = new
                {
                    totalConflicts = conflicts.Length,
                    safeMergeCandidates,
                    manualReviewRequired = conflicts.Length - safeMergeCandidates,
                    recoverablePropCount = conflicts
                        .Where(conflict => conflict.SafeToMerge)
                        .Sum(conflict => conflict.PropCount),
                    recoverableHistoricalResultCount = conflicts
                        .Where(conflict => conflict.SafeToMerge)
                        .Sum(conflict => conflict.HistoricalResultCount)
                },
                conflicts
            });
        })
        .WithName("PlayerIdentityConflictAnalysisDebug")
        .WithSummary("Analyzes duplicate canonical identity conflicts that block MLB automatic reconciliation.");

        endpoints.MapGet("/api/debug/player-identity-conflict-analysis/reasons", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
            var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
            var providerPlayers = await mlbResultsProvider.GetPlayersAsync(targetSeason, cancellationToken);
            var providerPlayersById = providerPlayers.ToDictionary(player => player.MlbPlayerId, StringComparer.OrdinalIgnoreCase);
            var canonicalPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
            var unmappedCanonicalPlayers = canonicalPlayers
                .Where(player => !mappedPlayerIds.Contains(player.Id))
                .ToArray();
            var identityRows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var duplicateGroups = GetDuplicateIdentityGroups(identityRows);
            var duplicateGroupByPlayerId = duplicateGroups
                .SelectMany(group => group.Players.Select(player => new
                {
                    player.PlayerId,
                    Group = group
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Group);
            var duplicatePlayerIds = duplicateGroups
                .SelectMany(group => group.Players.Select(player => player.PlayerId))
                .Distinct()
                .ToArray();
            var propRows = await dbContext.Props
                .AsNoTracking()
                .Where(prop => duplicatePlayerIds.Contains(prop.PlayerId))
                .Select(prop => new PlayerIdentityConflictPropRow(prop.Id, prop.PlayerId))
                .ToListAsync(cancellationToken);
            var propIds = propRows.Select(prop => prop.Id).ToArray();
            var sportsbookIdsByPlayer = await dbContext.PlatformProps
                .AsNoTracking()
                .Where(platformProp => propIds.Contains(platformProp.PropId))
                .Join(
                    dbContext.Props.AsNoTracking(),
                    platformProp => platformProp.PropId,
                    prop => prop.Id,
                    (platformProp, prop) => new
                    {
                        prop.PlayerId,
                        platformProp.PlatformId
                    })
                .ToListAsync(cancellationToken);
            var sportsbooksByPlayer = sportsbookIdsByPlayer
                .GroupBy(row => row.PlayerId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.PlatformId).Distinct().ToHashSet());
            var snapshotCountsByPlayer = await dbContext.PropLineSnapshots
                .AsNoTracking()
                .Where(snapshot => propIds.Contains(snapshot.PlatformProp!.PropId))
                .Select(snapshot => new
                {
                    snapshot.PlatformProp!.Prop!.PlayerId
                })
                .GroupBy(row => row.PlayerId)
                .Select(group => new
                {
                    PlayerId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(group => group.PlayerId, group => group.Count, cancellationToken);
            var resultCountsByPlayer = await dbContext.PropResults
                .AsNoTracking()
                .Where(result => duplicatePlayerIds.Contains(result.PlayerId))
                .GroupBy(result => result.PlayerId)
                .Select(group => new
                {
                    PlayerId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(group => group.PlayerId, group => group.Count, cancellationToken);
            var reasons = unmappedCanonicalPlayers
                .SelectMany(canonicalPlayer => BuildPlayerIdentityConflictReasonResponses(
                    canonicalPlayer,
                    duplicateGroupByPlayerId,
                    propRows,
                    sportsbooksByPlayer,
                    snapshotCountsByPlayer,
                    resultCountsByPlayer,
                    providerPlayersById))
                .OrderByDescending(reason => reason.BlockerCount)
                .ThenBy(reason => reason.DisplayName)
                .ThenBy(reason => reason.ConflictingDisplayName)
                .ToArray();

            return Results.Ok(new
            {
                provider = "mlb",
                season = targetSeason,
                summary = new
                {
                    totalConflicts = reasons.Length,
                    blockers = new
                    {
                        ConflictingProviderIds = reasons.Count(reason => reason.ConflictingProviderIds),
                        ConflictingMlbMappings = reasons.Count(reason => reason.ConflictingMlbMappings),
                        ConflictingExternalRefs = reasons.Count(reason => reason.ConflictingExternalRefs),
                        ConflictingTeams = reasons.Count(reason => reason.ConflictingTeams),
                        ConflictingLeagues = reasons.Count(reason => reason.ConflictingLeagues),
                        ConflictingSportsbooks = reasons.Count(reason => reason.ConflictingSportsbooks),
                        ConflictingPropHistory = reasons.Count(reason => reason.ConflictingPropHistory),
                        ConflictingHistoricalResults = reasons.Count(reason => reason.ConflictingHistoricalResults)
                    }
                },
                conflicts = reasons
            });
        })
        .WithName("PlayerIdentityConflictReasonAnalysisDebug")
        .WithSummary("Explains merge blockers for MLB duplicate canonical identity conflicts.");

        endpoints.MapGet("/api/debug/mlb-reconciliation-health", async (
            int? season,
            ApplicationDbContext dbContext,
            IMlbResultsProvider mlbResultsProvider,
            IPlayerExternalRefRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var targetSeason = season ?? DateTimeOffset.UtcNow.Year;
            var canonicalPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
            var candidateSet = await GetMlbCandidateSetAsync(dbContext, mlbResultsProvider, repository, targetSeason, cancellationToken);
            var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
            var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
            var canonicalPlayerIds = canonicalPlayers.Select(player => player.Id).ToHashSet();
            var unmappedPlayerIds = canonicalPlayerIds
                .Where(playerId => !mappedPlayerIds.Contains(playerId))
                .ToHashSet();

            var identityRows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
            var duplicateGroups = GetDuplicateIdentityGroups(identityRows)
                .Where(group => group.Players.Any(player => canonicalPlayerIds.Contains(player.PlayerId)))
                .ToArray();
            var duplicateGroupByPlayerId = duplicateGroups
                .SelectMany(group => group.Players.Select(player => new
                {
                    player.PlayerId,
                    Group = group
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Group);

            var unmappedPlayersInDuplicateGroups = unmappedPlayerIds
                .Count(playerId => duplicateGroupByPlayerId.ContainsKey(playerId));
            var mergeResolvableUnmappedPlayers = unmappedPlayerIds
                .Count(playerId =>
                    duplicateGroupByPlayerId.TryGetValue(playerId, out var group)
                    && group.Players.Any(player => mappedPlayerIds.Contains(player.PlayerId)
                        || player.ProviderRefs.Any(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))));

            var mappedPlayers = canonicalPlayers.Count(player => mappedPlayerIds.Contains(player.Id));
            var estimatedMappedPlayersAfterMerge = mappedPlayers + mergeResolvableUnmappedPlayers;
            var estimatedCoverageAfterMerge = canonicalPlayers.Count == 0
                ? 0m
                : Math.Round(estimatedMappedPlayersAfterMerge * 100m / canonicalPlayers.Count, 2);
            var currentCoverage = canonicalPlayers.Count == 0
                ? 0m
                : Math.Round(mappedPlayers * 100m / canonicalPlayers.Count, 2);

            var mergeCandidateSamples = duplicateGroups
                .OrderByDescending(group => group.Players.Count)
                .ThenBy(group => group.DisplayName)
                .Take(25)
                .Select(group => new
                {
                    group.DisplayName,
                    leagueCodes = group.Players
                        .Select(player => player.LeagueCode ?? "UNKNOWN")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(code => code)
                        .ToArray(),
                    playerIds = group.Players.Select(player => player.PlayerId).ToArray(),
                    mappedMlbPlayerIds = group.Players
                        .Where(player => mappedPlayerIds.Contains(player.PlayerId)
                            || player.ProviderRefs.Any(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase)))
                        .Select(player => player.PlayerId)
                        .ToArray(),
                    providerRefs = group.Players
                        .SelectMany(player => player.ProviderRefs.Select(reference => new
                        {
                            playerId = player.PlayerId,
                            leagueCode = player.LeagueCode,
                            reference.Provider,
                            reference.ProviderPlayerId,
                            reference.ProviderDisplayName
                        }))
                        .ToArray()
                })
                .ToArray();

            loggerFactory.CreateLogger("PlayerMappingDebugEndpoints").LogInformation(
                "MLB reconciliation health: {MappedPlayers}/{TotalMlbPlayers} mapped ({CurrentCoverage}%), {DuplicateGroups} duplicate groups, {UnmappedInDuplicateGroups} unmapped players in duplicate groups, estimated post-merge coverage {EstimatedCoverageAfterMerge}%.",
                mappedPlayers,
                canonicalPlayers.Count,
                currentCoverage,
                duplicateGroups.Length,
                unmappedPlayersInDuplicateGroups,
                estimatedCoverageAfterMerge);

            return Results.Ok(new
            {
                totalMlbPlayers = canonicalPlayers.Count,
                mappedPlayers,
                unmappedPlayers = canonicalPlayers.Count - mappedPlayers,
                duplicatePlayerGroups = duplicateGroups.Length,
                playersWithMultipleCanonicalRows = duplicateGroups
                    .SelectMany(group => group.Players)
                    .Select(player => player.PlayerId)
                    .Distinct()
                    .Count(),
                estimatedCoverageAfterMerge,
                currentCoverage,
                season = targetSeason,
                candidatesGenerated = candidateSet.Candidates.Count,
                unmappedPlayersInDuplicateGroups,
                mergeResolvableUnmappedPlayers,
                estimatedMappedPlayersAfterMerge,
                mergeCandidateSamples
            });
        })
        .WithName("MlbReconciliationHealthDebug")
        .WithSummary("Returns MLB mapping coverage diagnostics with duplicate-player merge impact estimates.");

        return endpoints;
    }

    private static PlayerExternalRefResponse ToResponse(PlayerExternalRef reference)
    {
        return new PlayerExternalRefResponse(
            reference.Id,
            reference.PlayerId,
            reference.Player?.DisplayName ?? string.Empty,
            reference.Player?.League?.Code ?? string.Empty,
            reference.Provider,
            reference.ProviderPlayerId,
            reference.ProviderDisplayName,
            reference.CreatedAt,
            reference.UpdatedAt);
    }

    private sealed record PlayerExternalRefResponse(
        Guid Id,
        Guid PlayerId,
        string PlayerName,
        string League,
        string Provider,
        string ProviderPlayerId,
        string ProviderDisplayName,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record MlbPlayerCandidateResponse(
        Guid PlayerId,
        string PlayerName,
        string CandidateMlbPlayerId,
        string CandidateDisplayName,
        decimal Confidence);

    private sealed record MlbSkippedCandidateResponse(
        Guid PlayerId,
        string PlayerName,
        string Reason,
        int CandidateCount,
        IReadOnlyCollection<MlbSkippedCandidateMatchResponse> Candidates);

    private sealed record MlbSkippedCandidateMatchResponse(
        string CandidateMlbPlayerId,
        string CandidateDisplayName,
        decimal Confidence);

    private sealed record MlbPlayerMappingApprovalRequest(
        Guid PlayerId,
        string MlbPlayerId,
        string DisplayName);

    private sealed record ProviderLineMlbRepairAutoApproveRequest(
        bool? DryRun,
        int? Take,
        string? ApprovedBy,
        string? Reason);

    private sealed record ProviderLineMlbRepairCandidate(
        Guid PlayerId,
        string DisplayName,
        string? League,
        string? Team,
        string? Position,
        int ActivePlatformPropCount,
        int CurrentProviderSnapshotCount,
        IReadOnlyCollection<ProviderLineMlbRepairExternalRef> ExistingExternalRefs,
        string AccentInsensitiveNormalizedName,
        IReadOnlyCollection<string> LocalLegacyMlbIds,
        IReadOnlyCollection<ProviderLineMlbRepairIdentityCandidate> CandidateMlbIdentities,
        bool IsSafeAutoApproval,
        string? RecommendedMlbPlayerId,
        string? RecommendedMlbDisplayName,
        string? SafeAutoApprovalReason,
        string? ConflictReason);

    private sealed record ProviderLineMlbRepairIdentityCandidate(
        Guid CandidatePlayerId,
        string DisplayName,
        string? League,
        string? Team,
        string? Position,
        IReadOnlyCollection<ProviderLineMlbRepairExternalRef> MlbRefs,
        int PropResultCount,
        int PitcherStrikeoutResultCount,
        string Reason);

    private sealed record ProviderLineMlbRepairExternalRef(
        string Provider,
        string ProviderPlayerId,
        string ProviderDisplayName);

    private sealed record MlbCandidateSet(
        int TotalCanonicalPlayers,
        IReadOnlyList<MlbPlayerCandidateResponse> Candidates);

    private sealed record MlbMappingCoverage(
        int TotalMlbPlayers,
        int MappedPlayers,
        int UnmappedPlayers,
        decimal CoveragePercent);

    private sealed record ActiveMlbPlayerCoverageRow(
        Guid PlayerId,
        string DisplayName,
        string? FirstName,
        string? LastName,
        string? TeamName,
        string? TeamAbbreviation,
        int ActivePropCount);

    private sealed record MlbMappingCoveragePlayerResponse(
        Guid PlayerId,
        string DisplayName,
        int ActivePropCount,
        bool HasMlbMapping,
        IReadOnlyCollection<MlbPossiblePlayerIdResponse> ExactNormalizedNameMatches,
        IReadOnlyCollection<MlbDuplicateIdentityCandidateResponse> DuplicateIdentityCandidates,
        IReadOnlyCollection<string> PossibleMlbPlayerIds,
        int RecoverableHistoricalResultCount);

    private sealed record MlbPossiblePlayerIdResponse(
        string MlbPlayerId,
        string DisplayName,
        string Source,
        decimal Confidence);

    private sealed record MlbDuplicateIdentityCandidateResponse(
        Guid PlayerId,
        string DisplayName,
        string LeagueCode,
        int HistoricalResultCount,
        IReadOnlyCollection<MlbPossiblePlayerIdResponse> PossibleMlbPlayerIds);

    private sealed record MlbUnmappedPlayerAnalysisResponse(
        Guid PlayerId,
        string PlayerName,
        string Reason,
        int CandidateCount,
        int UnmappedCandidateCount,
        IReadOnlyCollection<MlbUnmappedPlayerCandidateResponse> Candidates,
        IReadOnlyCollection<MlbUnmappedDuplicateCandidateResponse> DuplicateIdentityCandidates);

    private sealed record MlbUnmappedPlayerCandidateResponse(
        string CandidateMlbPlayerId,
        string CandidateDisplayName,
        decimal Confidence,
        bool ProviderPlayerAlreadyMapped,
        Guid? MappedCanonicalPlayerId,
        string? MappedCanonicalPlayerName);

    private sealed record MlbUnmappedDuplicateCandidateResponse(
        Guid PlayerId,
        string DisplayName,
        string LeagueCode,
        IReadOnlyCollection<MlbMappedProviderRefResponse> MlbMappings);

    private sealed record MlbMappedProviderRefResponse(
        string MlbPlayerId,
        string ProviderDisplayName);

    private sealed record PlayerIdentityConflictResponse(
        Guid CanonicalPlayerId,
        string DisplayName,
        Guid ConflictingPlayerId,
        string ConflictingDisplayName,
        string MlbPlayerId,
        int HistoricalResultCount,
        int PropCount,
        int SportsbookCount,
        decimal Confidence,
        bool SafeToMerge);

    private sealed record PlayerIdentityConflictReasonResponse(
        Guid CanonicalPlayerId,
        string DisplayName,
        Guid ConflictingPlayerId,
        string ConflictingDisplayName,
        string MlbPlayerId,
        decimal Confidence,
        bool ConflictingProviderIds,
        bool ConflictingMlbMappings,
        bool ConflictingExternalRefs,
        bool ConflictingTeams,
        bool ConflictingLeagues,
        bool ConflictingSportsbooks,
        bool ConflictingPropHistory,
        bool ConflictingHistoricalResults)
    {
        public int BlockerCount =>
            Convert.ToInt32(ConflictingProviderIds)
            + Convert.ToInt32(ConflictingMlbMappings)
            + Convert.ToInt32(ConflictingExternalRefs)
            + Convert.ToInt32(ConflictingTeams)
            + Convert.ToInt32(ConflictingLeagues)
            + Convert.ToInt32(ConflictingSportsbooks)
            + Convert.ToInt32(ConflictingPropHistory)
            + Convert.ToInt32(ConflictingHistoricalResults);
    }

    private sealed record PlayerIdentityConflictPropRow(
        Guid Id,
        Guid PlayerId);

    private sealed record ExternalRefSummary(
        string Provider,
        string? PlayerId);

    private sealed record PlayerIdentityRow(
        Guid PlayerId,
        string DisplayName,
        string NormalizedDisplayName,
        Guid LeagueId,
        string? LeagueCode,
        string? LeagueName,
        Guid? TeamId,
        string? TeamName,
        string? TeamAbbreviation,
        DateTimeOffset CreatedAt,
        IReadOnlyCollection<ExternalRefSummary> LegacyExternalRefs,
        IReadOnlyCollection<PlayerProviderRefDiagnostic> ProviderRefs);

    private sealed record PlayerProviderRefDiagnostic(
        Guid Id,
        string Provider,
        string ProviderPlayerId,
        string ProviderDisplayName);

    private sealed record DuplicateIdentityGroup(
        string DisplayName,
        string NormalizedDisplayName,
        IReadOnlyList<PlayerIdentityRow> Players);

    private sealed record DuplicateIdentityCandidateResponse(
        string DisplayName,
        string NormalizedDisplayName,
        IReadOnlyCollection<string> LeagueCodes,
        IReadOnlyCollection<Guid> PlayerIds,
        IReadOnlyCollection<DuplicateIdentityProviderRefResponse> ProviderRefs,
        IReadOnlyCollection<DuplicateIdentityLegacyExternalRefResponse> LegacyExternalRefs);

    private sealed record DuplicateIdentityProviderRefResponse(
        Guid PlayerId,
        string? LeagueCode,
        string Provider,
        string ProviderPlayerId,
        string ProviderDisplayName);

    private sealed record DuplicateIdentityLegacyExternalRefResponse(
        Guid PlayerId,
        string? LeagueCode,
        string Provider,
        string? ProviderPlayerId);

    private sealed record PlayerMergePropRow(Guid Id, Guid PlayerId);

    private sealed record PlayerMergePlatformPropRow(Guid Id, Guid PropId);

    private sealed record PlayerMergeSnapshotRow(Guid Id, Guid PlatformPropId);

    private sealed record PlayerMergeMappingRow(Guid Id, Guid PlayerId);

    private sealed record PlayerMergeResultRow(Guid Id, Guid PlayerId);

    private sealed record PlayerMergeExecutionResult(
        int PlayersMerged,
        int PropsReassigned,
        int SnapshotsAffected,
        int MappingsReassigned,
        int ResultsReassigned);

    private sealed record PlayerMergeCandidateResponse(
        string DisplayName,
        Guid SurvivorPlayerId,
        IReadOnlyCollection<Guid> DuplicatePlayerIds,
        int AffectedProps,
        int AffectedSnapshots,
        int AffectedMappings,
        int AffectedResults,
        IReadOnlyCollection<string> Conflicts,
        IReadOnlyCollection<Guid> AllPlayerIds,
        IReadOnlyCollection<string> LeagueCodes,
        IReadOnlyCollection<string> CanonicalLeagueCodes,
        IReadOnlyCollection<object> PlayerRows,
        IReadOnlyCollection<object> ProviderRefs);

    private static async Task<IReadOnlyList<PlayerIdentityRow>> LoadPlayerIdentityRowsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var players = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)
            .Include(player => player.Team)
            .Include(player => player.ExternalProviderRefs)
            .ToListAsync(cancellationToken);

        return players
            .Select(player => new PlayerIdentityRow(
                player.Id,
                player.DisplayName,
                NormalizePlayerName(player.DisplayName),
                player.LeagueId,
                player.League?.Code,
                player.League?.Name,
                player.TeamId,
                player.Team?.Name,
                player.Team?.Abbreviation,
                player.CreatedAt,
                SummarizeExternalRefs(player.ExternalRefs),
                player.ExternalProviderRefs
                    .OrderBy(reference => reference.Provider)
                    .ThenBy(reference => reference.ProviderPlayerId)
                    .Select(reference => new PlayerProviderRefDiagnostic(
                        reference.Id,
                        reference.Provider,
                        reference.ProviderPlayerId,
                        reference.ProviderDisplayName))
                    .ToArray()))
            .Where(row => !string.IsNullOrWhiteSpace(row.NormalizedDisplayName))
            .ToArray();
    }

    private static IReadOnlyList<DuplicateIdentityGroup> GetDuplicateIdentityGroups(IReadOnlyList<PlayerIdentityRow> rows)
    {
        return rows
            .GroupBy(row => row.NormalizedDisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateIdentityGroup(
                group.First().DisplayName,
                group.Key,
                group
                    .OrderBy(row => row.LeagueCode)
                    .ThenBy(row => row.PlayerId)
                    .ToArray()))
            .OrderByDescending(group => group.Players.Count)
            .ThenBy(group => group.DisplayName)
            .ToArray();
    }

    private static DuplicateIdentityCandidateResponse ToDuplicateCandidateResponse(DuplicateIdentityGroup group)
    {
        return new DuplicateIdentityCandidateResponse(
            group.DisplayName,
            group.NormalizedDisplayName,
            group.Players
                .Select(player => player.LeagueCode ?? "UNKNOWN")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToArray(),
            group.Players
                .Select(player => player.PlayerId)
                .ToArray(),
            group.Players
                .SelectMany(player => player.ProviderRefs.Select(reference => new DuplicateIdentityProviderRefResponse(
                    player.PlayerId,
                    player.LeagueCode,
                    reference.Provider,
                    reference.ProviderPlayerId,
                    reference.ProviderDisplayName)))
                .ToArray(),
            group.Players
                .SelectMany(player => player.LegacyExternalRefs.Select(reference => new DuplicateIdentityLegacyExternalRefResponse(
                    player.PlayerId,
                    player.LeagueCode,
                    reference.Provider,
                    reference.PlayerId)))
                .ToArray());
    }

    private static async Task<IReadOnlyList<PlayerMergeCandidateResponse>> LoadPlayerMergeCandidatesAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
        var duplicateGroups = GetDuplicateIdentityGroups(rows);
        var duplicatePlayerIds = duplicateGroups
            .SelectMany(group => group.Players.Select(player => player.PlayerId))
            .Distinct()
            .ToArray();

        var propRows = await dbContext.Props
            .AsNoTracking()
            .Where(prop => duplicatePlayerIds.Contains(prop.PlayerId))
            .Select(prop => new PlayerMergePropRow(prop.Id, prop.PlayerId))
            .ToListAsync(cancellationToken);

        var propIds = propRows.Select(prop => prop.Id).ToArray();
        var platformPropRows = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => propIds.Contains(platformProp.PropId))
            .Select(platformProp => new PlayerMergePlatformPropRow(platformProp.Id, platformProp.PropId))
            .ToListAsync(cancellationToken);

        var platformPropIds = platformPropRows.Select(platformProp => platformProp.Id).ToArray();
        var snapshotRows = await dbContext.PropLineSnapshots
            .AsNoTracking()
            .Where(snapshot => platformPropIds.Contains(snapshot.PlatformPropId))
            .Select(snapshot => new PlayerMergeSnapshotRow(snapshot.Id, snapshot.PlatformPropId))
            .ToListAsync(cancellationToken);

        var resultRows = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => duplicatePlayerIds.Contains(result.PlayerId))
            .Select(result => new PlayerMergeResultRow(result.Id, result.PlayerId))
            .ToListAsync(cancellationToken);

        var mappingRows = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => duplicatePlayerIds.Contains(reference.PlayerId))
            .Select(reference => new PlayerMergeMappingRow(reference.Id, reference.PlayerId))
            .ToListAsync(cancellationToken);

        return duplicateGroups
            .Select(group => BuildPlayerMergeCandidate(
                group,
                propRows,
                platformPropRows,
                snapshotRows,
                mappingRows,
                resultRows))
            .OrderBy(candidate => candidate.Conflicts.Count)
            .ThenByDescending(candidate => candidate.DuplicatePlayerIds.Count)
            .ThenBy(candidate => candidate.DisplayName)
            .ToArray();
    }

    private static async Task<object> BuildPlayerIdentitySummaryAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
        var duplicateGroups = GetDuplicateIdentityGroups(rows);
        var duplicatesByLeague = duplicateGroups
            .SelectMany(group => group.Players)
            .GroupBy(player => player.LeagueCode ?? "UNKNOWN", StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                league = group.Key,
                duplicateRows = group.Count(),
                duplicateNames = group.Select(player => player.NormalizedDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            })
            .OrderByDescending(group => group.duplicateRows)
            .ThenBy(group => group.league)
            .ToArray();

        return new
        {
            totalPlayers = rows.Count,
            uniqueDisplayNames = rows.Select(row => row.NormalizedDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            duplicateRows = duplicateGroups.Sum(group => group.Players.Count - 1),
            duplicateNames = duplicateGroups.Count,
            duplicatesByLeague
        };
    }

    private static async Task<object> BuildMlbReconciliationHealthAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var canonicalPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
        var canonicalPlayerIds = canonicalPlayers.Select(player => player.Id).ToHashSet();
        var mappedMlbPlayerIds = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == "mlb")
            .Select(reference => reference.PlayerId)
            .ToListAsync(cancellationToken);
        var mappedPlayerIdSet = mappedMlbPlayerIds.ToHashSet();
        var mappedPlayers = canonicalPlayerIds.Count(mappedPlayerIdSet.Contains);
        var rows = await LoadPlayerIdentityRowsAsync(dbContext, cancellationToken);
        var duplicateGroups = GetDuplicateIdentityGroups(rows)
            .Where(group => group.Players.Any(player => canonicalPlayerIds.Contains(player.PlayerId)))
            .ToArray();
        var duplicateGroupByPlayerId = duplicateGroups
            .SelectMany(group => group.Players.Select(player => new
            {
                player.PlayerId,
                Group = group
            }))
            .GroupBy(item => item.PlayerId)
            .ToDictionary(group => group.Key, group => group.First().Group);
        var unmappedPlayerIds = canonicalPlayerIds
            .Where(playerId => !mappedPlayerIdSet.Contains(playerId))
            .ToHashSet();
        var unmappedPlayersInDuplicateGroups = unmappedPlayerIds
            .Count(playerId => duplicateGroupByPlayerId.ContainsKey(playerId));
        var mergeResolvableUnmappedPlayers = unmappedPlayerIds
            .Count(playerId =>
                duplicateGroupByPlayerId.TryGetValue(playerId, out var group)
                && group.Players.Any(player => mappedPlayerIdSet.Contains(player.PlayerId)
                    || player.ProviderRefs.Any(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))));
        var estimatedMappedPlayersAfterMerge = mappedPlayers + mergeResolvableUnmappedPlayers;

        return new
        {
            totalMlbPlayers = canonicalPlayerIds.Count,
            mappedPlayers,
            unmappedPlayers = canonicalPlayerIds.Count - mappedPlayers,
            duplicatePlayerGroups = duplicateGroups.Length,
            playersWithMultipleCanonicalRows = duplicateGroups
                .SelectMany(group => group.Players)
                .Select(player => player.PlayerId)
                .Distinct()
                .Count(),
            currentCoverage = canonicalPlayerIds.Count == 0
                ? 0m
                : Math.Round(mappedPlayers * 100m / canonicalPlayerIds.Count, 2),
            estimatedCoverageAfterMerge = canonicalPlayerIds.Count == 0
                ? 0m
                : Math.Round(estimatedMappedPlayersAfterMerge * 100m / canonicalPlayerIds.Count, 2),
            unmappedPlayersInDuplicateGroups,
            mergeResolvableUnmappedPlayers,
            estimatedMappedPlayersAfterMerge
        };
    }

    private static async Task<PlayerMergeExecutionResult> ExecuteSafePlayerMergeAsync(
        ApplicationDbContext dbContext,
        PlayerMergeCandidateResponse candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.Conflicts.Count > 0)
        {
            throw new InvalidOperationException("Candidate has conflicts and cannot be safely merged.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var survivor = await dbContext.Players
            .SingleOrDefaultAsync(player => player.Id == candidate.SurvivorPlayerId, cancellationToken)
            ?? throw new InvalidOperationException($"Survivor player '{candidate.SurvivorPlayerId}' was not found.");
        var duplicatePlayerIds = candidate.DuplicatePlayerIds.ToArray();
        var duplicatePlayers = await dbContext.Players
            .Where(player => duplicatePlayerIds.Contains(player.Id))
            .ToListAsync(cancellationToken);

        if (duplicatePlayers.Count != duplicatePlayerIds.Length)
        {
            throw new InvalidOperationException("One or more duplicate players were not found.");
        }

        survivor.ExternalRefs = MergeLegacyExternalRefs(
            [survivor.ExternalRefs, .. duplicatePlayers.Select(player => player.ExternalRefs)]);
        survivor.UpdatedAt = DateTimeOffset.UtcNow;

        var propRows = await dbContext.Props
            .AsNoTracking()
            .Where(prop => duplicatePlayerIds.Contains(prop.PlayerId))
            .Select(prop => new PlayerMergePropRow(prop.Id, prop.PlayerId))
            .ToListAsync(cancellationToken);
        var propIds = propRows.Select(prop => prop.Id).ToArray();
        var platformPropRows = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => propIds.Contains(platformProp.PropId))
            .Select(platformProp => new PlayerMergePlatformPropRow(platformProp.Id, platformProp.PropId))
            .ToListAsync(cancellationToken);
        var platformPropIds = platformPropRows.Select(platformProp => platformProp.Id).ToArray();
        var snapshotRows = await dbContext.PropLineSnapshots
            .AsNoTracking()
            .Where(snapshot => platformPropIds.Contains(snapshot.PlatformPropId))
            .Select(snapshot => new PlayerMergeSnapshotRow(snapshot.Id, snapshot.PlatformPropId))
            .ToListAsync(cancellationToken);
        var resultRows = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => duplicatePlayerIds.Contains(result.PlayerId))
            .Select(result => new PlayerMergeResultRow(result.Id, result.PlayerId))
            .ToListAsync(cancellationToken);
        var mappingRows = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => duplicatePlayerIds.Contains(reference.PlayerId))
            .Select(reference => new PlayerMergeMappingRow(reference.Id, reference.PlayerId))
            .ToListAsync(cancellationToken);
        var mergedAt = DateTimeOffset.UtcNow;
        var auditRows = duplicatePlayerIds
            .Select(mergedPlayerId =>
            {
                var mergedPropIds = propRows
                    .Where(prop => prop.PlayerId == mergedPlayerId)
                    .Select(prop => prop.Id)
                    .ToHashSet();
                var mergedPlatformPropIds = platformPropRows
                    .Where(platformProp => mergedPropIds.Contains(platformProp.PropId))
                    .Select(platformProp => platformProp.Id)
                    .ToHashSet();

                return new PlayerMergeAudit
                {
                    MergeId = Guid.NewGuid(),
                    SurvivorPlayerId = candidate.SurvivorPlayerId,
                    MergedPlayerId = mergedPlayerId,
                    MergedAt = mergedAt,
                    AffectedProps = propRows.Count(prop => prop.PlayerId == mergedPlayerId),
                    AffectedSnapshots = snapshotRows.Count(snapshot => mergedPlatformPropIds.Contains(snapshot.PlatformPropId)),
                    AffectedResults = resultRows.Count(result => result.PlayerId == mergedPlayerId),
                    AffectedMappings = mappingRows.Count(mapping => mapping.PlayerId == mergedPlayerId)
                };
            })
            .ToArray();

        dbContext.PlayerMergeAudits.AddRange(auditRows);

        await dbContext.Props
            .Where(prop => duplicatePlayerIds.Contains(prop.PlayerId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(prop => prop.PlayerId, candidate.SurvivorPlayerId), cancellationToken);
        await dbContext.PropResults
            .Where(result => duplicatePlayerIds.Contains(result.PlayerId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(result => result.PlayerId, candidate.SurvivorPlayerId), cancellationToken);
        await dbContext.PlayerExternalRefs
            .Where(reference => duplicatePlayerIds.Contains(reference.PlayerId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(reference => reference.PlayerId, candidate.SurvivorPlayerId), cancellationToken);

        dbContext.Players.RemoveRange(duplicatePlayers);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        return new PlayerMergeExecutionResult(
            duplicatePlayerIds.Length,
            propRows.Count,
            snapshotRows.Count,
            mappingRows.Count,
            resultRows.Count);
    }

    private static JsonDocument? MergeLegacyExternalRefs(IEnumerable<JsonDocument?> documents)
    {
        var providers = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            if (document?.RootElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                providers[property.Name] = property.Value.Clone();
            }
        }

        return providers.Count == 0
            ? null
            : JsonSerializer.SerializeToDocument(providers);
    }

    private static int CountMergeResolvableMlbPlayers(
        IEnumerable<PlayerMergeCandidateResponse> mergeCandidates,
        IReadOnlySet<Guid> canonicalMlbPlayerIds,
        IReadOnlySet<Guid> mappedMlbPlayerIds)
    {
        return mergeCandidates
            .Where(candidate => candidate.AllPlayerIds.Any(mappedMlbPlayerIds.Contains))
            .SelectMany(candidate => candidate.AllPlayerIds)
            .Where(canonicalMlbPlayerIds.Contains)
            .Where(playerId => !mappedMlbPlayerIds.Contains(playerId))
            .Distinct()
            .Count();
    }

    private static decimal CalculateProjectedCoverage(
        int totalMlbPlayers,
        int currentlyMappedMlbPlayers,
        int mergeResolvableMlbPlayers)
    {
        return totalMlbPlayers == 0
            ? 0m
            : Math.Round((currentlyMappedMlbPlayers + mergeResolvableMlbPlayers) * 100m / totalMlbPlayers, 2);
    }

    private static PlayerMergeCandidateResponse BuildPlayerMergeCandidate(
        DuplicateIdentityGroup group,
        IReadOnlyCollection<PlayerMergePropRow> propRows,
        IReadOnlyCollection<PlayerMergePlatformPropRow> platformPropRows,
        IReadOnlyCollection<PlayerMergeSnapshotRow> snapshotRows,
        IReadOnlyCollection<PlayerMergeMappingRow> mappingRows,
        IReadOnlyCollection<PlayerMergeResultRow> resultRows)
    {
        var groupPlayerIds = group.Players.Select(player => player.PlayerId).ToHashSet();
        var propCounts = propRows
            .Where(prop => groupPlayerIds.Contains(prop.PlayerId))
            .GroupBy(prop => prop.PlayerId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Count());
        var survivor = group.Players
            .OrderByDescending(player => player.ProviderRefs.Count + player.LegacyExternalRefs.Count)
            .ThenByDescending(player => propCounts.GetValueOrDefault(player.PlayerId))
            .ThenBy(player => player.CreatedAt)
            .ThenBy(player => player.PlayerId)
            .First();
        var duplicatePlayerIds = group.Players
            .Where(player => player.PlayerId != survivor.PlayerId)
            .Select(player => player.PlayerId)
            .ToArray();
        var groupPropIds = propRows
            .Where(prop => groupPlayerIds.Contains(prop.PlayerId))
            .Select(prop => prop.Id)
            .ToHashSet();
        var groupPlatformPropIds = platformPropRows
            .Where(platformProp => groupPropIds.Contains(platformProp.PropId))
            .Select(platformProp => platformProp.Id)
            .ToHashSet();
        var conflicts = DetectMergeConflicts(group);

        return new PlayerMergeCandidateResponse(
            group.DisplayName,
            survivor.PlayerId,
            duplicatePlayerIds,
            groupPropIds.Count,
            snapshotRows.Count(snapshot => groupPlatformPropIds.Contains(snapshot.PlatformPropId)),
            mappingRows.Count(mapping => groupPlayerIds.Contains(mapping.PlayerId)),
            resultRows.Count(result => groupPlayerIds.Contains(result.PlayerId)),
            conflicts,
            group.Players.Select(player => player.PlayerId).ToArray(),
            group.Players
                .Select(player => player.LeagueCode ?? "UNKNOWN")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToArray(),
            group.Players
                .Select(player => CanonicalLeagueCode(player.LeagueCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToArray(),
            group.Players
                .OrderBy(player => player.PlayerId == survivor.PlayerId ? 0 : 1)
                .ThenBy(player => player.LeagueCode)
                .Select(player => new
                {
                    playerId = player.PlayerId,
                    player.DisplayName,
                    player.NormalizedDisplayName,
                    leagueCode = player.LeagueCode,
                    canonicalLeagueCode = CanonicalLeagueCode(player.LeagueCode),
                    player.TeamId,
                    player.TeamName,
                    player.TeamAbbreviation,
                    player.CreatedAt,
                    externalReferenceCount = player.ProviderRefs.Count + player.LegacyExternalRefs.Count,
                    propCount = propCounts.GetValueOrDefault(player.PlayerId),
                    selectedSurvivor = player.PlayerId == survivor.PlayerId
                })
                .Cast<object>()
                .ToArray(),
            group.Players
                .SelectMany(player => player.ProviderRefs.Select(reference => new
                {
                    source = "player_external_refs",
                    playerId = player.PlayerId,
                    leagueCode = player.LeagueCode,
                    reference.Provider,
                    reference.ProviderPlayerId,
                    reference.ProviderDisplayName
                }).Cast<object>().Concat(player.LegacyExternalRefs.Select(reference => new
                {
                    source = "players.external_refs",
                    playerId = player.PlayerId,
                    leagueCode = player.LeagueCode,
                    reference.Provider,
                    providerPlayerId = reference.PlayerId,
                    providerDisplayName = player.DisplayName
                })))
                .ToArray());
    }

    private static IReadOnlyCollection<string> DetectMergeConflicts(DuplicateIdentityGroup group)
    {
        var conflicts = new List<string>();
        var normalizedNames = group.Players
            .Select(player => player.NormalizedDisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedNames.Length > 1)
        {
            conflicts.Add("DifferentNormalizedNames");
        }

        var canonicalLeagueCodes = group.Players
            .Select(player => CanonicalLeagueCode(player.LeagueCode))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (canonicalLeagueCodes.Length > 1)
        {
            conflicts.Add("ConflictingLeagues");
        }

        var teamIds = group.Players
            .Where(player => player.TeamId.HasValue)
            .Select(player => player.TeamId!.Value)
            .Distinct()
            .ToArray();
        if (teamIds.Length > 1)
        {
            conflicts.Add("ConflictingTeams");
        }

        var providerIds = group.Players
            .SelectMany(player => player.ProviderRefs.Select(reference => new
            {
                reference.Provider,
                ProviderPlayerId = reference.ProviderPlayerId
            }).Concat(player.LegacyExternalRefs
                .Where(reference => !string.IsNullOrWhiteSpace(reference.PlayerId))
                .Select(reference => new
                {
                    reference.Provider,
                    ProviderPlayerId = reference.PlayerId!
                })))
            .GroupBy(reference => reference.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(grouping => new
            {
                Provider = grouping.Key,
                PlayerIds = grouping
                    .Select(reference => reference.ProviderPlayerId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();

        if (providerIds.Any(provider => string.Equals(provider.Provider, "mlb", StringComparison.OrdinalIgnoreCase) && provider.PlayerIds.Length > 1))
        {
            conflicts.Add("MultipleMlbMappings");
        }

        if (providerIds.Any(provider => provider.PlayerIds.Length > 1))
        {
            conflicts.Add("ConflictingProviderIds");
        }

        return conflicts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(conflict => conflict)
            .ToArray();
    }

    private static string CanonicalLeagueCode(string? leagueCode)
    {
        return string.IsNullOrWhiteSpace(leagueCode)
            ? "UNKNOWN"
            : CanonicalLeagueNormalizer.NormalizeCode(leagueCode);
    }

    private static async Task<MlbCandidateSet> GetMlbCandidateSetAsync(
        ApplicationDbContext dbContext,
        IMlbResultsProvider mlbResultsProvider,
        IPlayerExternalRefRepository repository,
        int season,
        CancellationToken cancellationToken)
    {
        var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
        var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
        var canonicalPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
        var providerPlayers = await mlbResultsProvider.GetPlayersAsync(season, cancellationToken);
        var candidates = new List<MlbPlayerCandidateResponse>();

        foreach (var canonicalPlayer in canonicalPlayers)
        {
            if (mappedPlayerIds.Contains(canonicalPlayer.Id))
            {
                continue;
            }

            foreach (var providerPlayer in providerPlayers)
            {
                if (existingRefs.ContainsKey(providerPlayer.MlbPlayerId))
                {
                    continue;
                }

                var confidence = CalculateMlbMatchConfidence(canonicalPlayer, providerPlayer);
                if (confidence <= 0)
                {
                    continue;
                }

                candidates.Add(new MlbPlayerCandidateResponse(
                    canonicalPlayer.Id,
                    canonicalPlayer.DisplayName,
                    providerPlayer.MlbPlayerId,
                    providerPlayer.DisplayName,
                    confidence));
            }
        }

        return new MlbCandidateSet(canonicalPlayers.Count, candidates);
    }

    private static async Task<IReadOnlyList<Player>> GetCanonicalMlbPropPlayersAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var players = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.Player)
            .ThenInclude(player => player!.Team)
            .Include(prop => prop.League)
            .Where(prop => prop.League != null && prop.League.Code == "MLB" && prop.Player != null)
            .Select(prop => prop.Player!)
            .ToListAsync(cancellationToken);

        return players
            .GroupBy(player => player.Id)
            .Select(group => group.First())
            .ToArray();
    }

    private static async Task<IReadOnlyList<ActiveMlbPlayerCoverageRow>> GetActiveMlbPlayerCoverageRowsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.Player)
            .ThenInclude(player => player!.Team)
            .Include(prop => prop.League)
            .Include(prop => prop.PlatformProps)
            .Where(prop => prop.League != null
                && prop.League.Code == "MLB"
                && prop.Player != null
                && prop.PlatformProps.Any())
            .Select(prop => new
            {
                PlayerId = prop.PlayerId,
                PlayerName = prop.Player == null ? "UNKNOWN" : prop.Player.DisplayName,
                FirstName = prop.Player == null ? null : prop.Player.FirstName,
                LastName = prop.Player == null ? null : prop.Player.LastName,
                TeamName = prop.Player == null || prop.Player.Team == null ? null : prop.Player.Team.Name,
                TeamAbbreviation = prop.Player == null || prop.Player.Team == null ? null : prop.Player.Team.Abbreviation,
                PropId = prop.Id
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new
            {
                row.PlayerId,
                row.PlayerName,
                row.FirstName,
                row.LastName,
                row.TeamName,
                row.TeamAbbreviation
            })
            .Select(group => new ActiveMlbPlayerCoverageRow(
                group.Key.PlayerId,
                group.Key.PlayerName,
                group.Key.FirstName,
                group.Key.LastName,
                group.Key.TeamName,
                group.Key.TeamAbbreviation,
                group.Select(row => row.PropId).Distinct().Count()))
            .OrderByDescending(row => row.ActivePropCount)
            .ThenBy(row => row.DisplayName)
            .ToArray();
    }

    private static MlbMappingCoveragePlayerResponse BuildMlbMappingCoveragePlayerResponse(
        ActiveMlbPlayerCoverageRow player,
        IReadOnlySet<Guid> mappedPlayerIds,
        IEnumerable<PlayerExternalRef> existingRefs,
        IReadOnlyDictionary<string, MlbPlayerRecord[]> providerPlayersByNormalizedName,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId,
        IReadOnlyDictionary<Guid, int> historicalResultCountsByPlayer)
    {
        var hasMlbMapping = mappedPlayerIds.Contains(player.PlayerId);
        var exactNormalizedName = NormalizePlayerName(player.DisplayName);
        var exactProviderMatches = providerPlayersByNormalizedName.TryGetValue(exactNormalizedName, out var providerMatches)
            ? providerMatches
                .Select(providerPlayer => new MlbPossiblePlayerIdResponse(
                    providerPlayer.MlbPlayerId,
                    providerPlayer.DisplayName,
                    "ExactNormalizedName",
                    1.0m))
                .ToArray()
            : [];
        var duplicateCandidates = duplicateGroupByPlayerId.TryGetValue(player.PlayerId, out var duplicateGroup)
            ? duplicateGroup.Players
                .Where(duplicatePlayer => duplicatePlayer.PlayerId != player.PlayerId)
                .Select(duplicatePlayer =>
                {
                    var duplicateRefs = existingRefs
                        .Where(reference => reference.PlayerId == duplicatePlayer.PlayerId)
                        .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
                        .Select(reference => new MlbPossiblePlayerIdResponse(
                            reference.ProviderPlayerId,
                            reference.ProviderDisplayName,
                            "DuplicateIdentityMlbMapping",
                            1.0m))
                        .ToArray();

                    return new MlbDuplicateIdentityCandidateResponse(
                        duplicatePlayer.PlayerId,
                        duplicatePlayer.DisplayName,
                        duplicatePlayer.LeagueCode ?? "UNKNOWN",
                        historicalResultCountsByPlayer.GetValueOrDefault(duplicatePlayer.PlayerId),
                        duplicateRefs);
                })
                .Where(candidate => candidate.HistoricalResultCount > 0 || candidate.PossibleMlbPlayerIds.Count > 0)
                .OrderByDescending(candidate => candidate.HistoricalResultCount)
                .ThenBy(candidate => candidate.DisplayName)
                .ToArray()
            : [];
        var recoverableHistoricalResultCount = duplicateCandidates.Sum(candidate => candidate.HistoricalResultCount);

        return new MlbMappingCoveragePlayerResponse(
            player.PlayerId,
            player.DisplayName,
            player.ActivePropCount,
            hasMlbMapping,
            exactProviderMatches,
            duplicateCandidates,
            exactProviderMatches.Select(match => match.MlbPlayerId)
                .Concat(duplicateCandidates.SelectMany(candidate => candidate.PossibleMlbPlayerIds.Select(match => match.MlbPlayerId)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(mlbPlayerId => mlbPlayerId)
                .ToArray(),
            recoverableHistoricalResultCount);
    }

    private static MlbUnmappedPlayerAnalysisResponse AnalyzeUnmappedMlbPlayer(
        Player canonicalPlayer,
        IReadOnlyCollection<MlbPlayerRecord> providerPlayers,
        IReadOnlyDictionary<string, PlayerExternalRef> existingRefs,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId)
    {
        var candidates = providerPlayers
            .Select(providerPlayer =>
            {
                var confidence = CalculateMlbMatchConfidence(canonicalPlayer, providerPlayer);
                existingRefs.TryGetValue(providerPlayer.MlbPlayerId, out var existingReference);

                return new MlbUnmappedPlayerCandidateResponse(
                    providerPlayer.MlbPlayerId,
                    providerPlayer.DisplayName,
                    confidence,
                    existingReference is not null,
                    existingReference?.PlayerId,
                    existingReference?.Player?.DisplayName);
            })
            .Where(candidate => candidate.Confidence > 0)
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.CandidateDisplayName)
            .ToArray();

        var unmappedCandidates = candidates
            .Where(candidate => !candidate.ProviderPlayerAlreadyMapped)
            .ToArray();
        var duplicateIdentityCandidates = BuildMlbUnmappedDuplicateIdentityCandidates(canonicalPlayer.Id, duplicateGroupByPlayerId);
        var reason = DetermineUnmappedMlbReason(
            candidates,
            unmappedCandidates,
            duplicateIdentityCandidates,
            providerPlayers.Count);

        return new MlbUnmappedPlayerAnalysisResponse(
            canonicalPlayer.Id,
            canonicalPlayer.DisplayName,
            reason,
            candidates.Length,
            unmappedCandidates.Length,
            candidates.Take(10).ToArray(),
            duplicateIdentityCandidates);
    }

    private static MlbUnmappedDuplicateCandidateResponse[] BuildMlbUnmappedDuplicateIdentityCandidates(
        Guid playerId,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId)
    {
        if (!duplicateGroupByPlayerId.TryGetValue(playerId, out var duplicateGroup))
        {
            return [];
        }

        return duplicateGroup.Players
            .Where(player => player.PlayerId != playerId)
            .Select(player => new MlbUnmappedDuplicateCandidateResponse(
                player.PlayerId,
                player.DisplayName,
                player.LeagueCode ?? "UNKNOWN",
                player.ProviderRefs
                    .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
                    .Select(reference => new MlbMappedProviderRefResponse(
                        reference.ProviderPlayerId,
                        reference.ProviderDisplayName))
                    .OrderBy(reference => reference.ProviderDisplayName)
                    .ThenBy(reference => reference.MlbPlayerId)
                    .ToArray()))
            .Where(candidate => candidate.MlbMappings.Count > 0)
            .OrderBy(candidate => candidate.DisplayName)
            .ThenBy(candidate => candidate.PlayerId)
            .ToArray();
    }

    private static string DetermineUnmappedMlbReason(
        IReadOnlyCollection<MlbUnmappedPlayerCandidateResponse> candidates,
        IReadOnlyCollection<MlbUnmappedPlayerCandidateResponse> unmappedCandidates,
        IReadOnlyCollection<MlbUnmappedDuplicateCandidateResponse> duplicateIdentityCandidates,
        int providerPlayersInSeasonFeed)
    {
        if (duplicateIdentityCandidates.Count > 0)
        {
            return "DuplicateCanonicalIdentityConflict";
        }

        if (candidates.Count > 0 && unmappedCandidates.Count == 0)
        {
            return "ProviderPlayerAlreadyMapped";
        }

        if (unmappedCandidates.Count > 1)
        {
            return "MultipleCandidates";
        }

        if (unmappedCandidates.Count == 1 && unmappedCandidates.Single().Confidence < 1.0m)
        {
            return "ConfidenceBelowThreshold";
        }

        if (providerPlayersInSeasonFeed == 0)
        {
            return "InactiveOrMissingFromSeasonFeed";
        }

        if (candidates.Count == 0)
        {
            return "NoCandidateFound";
        }

        return "Other";
    }

    private static PlayerIdentityConflictResponse[] BuildPlayerIdentityConflictResponses(
        Player canonicalPlayer,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId,
        IReadOnlyDictionary<Guid, PlayerMergeCandidateResponse> mergeCandidateByPlayerId,
        IReadOnlyCollection<PlayerIdentityConflictPropRow> propRows,
        IReadOnlyDictionary<Guid, int> sportsbookCountsByPlayer,
        IReadOnlyDictionary<Guid, int> resultCountsByPlayer,
        IReadOnlyDictionary<string, MlbPlayerRecord> providerPlayersById)
    {
        if (!duplicateGroupByPlayerId.TryGetValue(canonicalPlayer.Id, out var duplicateGroup))
        {
            return [];
        }

        mergeCandidateByPlayerId.TryGetValue(canonicalPlayer.Id, out var mergeCandidate);
        var safeToMergeGroup = mergeCandidate is not null && mergeCandidate.Conflicts.Count == 0;
        var canonicalPropRows = propRows
            .Where(prop => prop.PlayerId == canonicalPlayer.Id)
            .ToArray();
        var propCount = canonicalPropRows.Length;
        var sportsbookCount = sportsbookCountsByPlayer.GetValueOrDefault(canonicalPlayer.Id);
        var conflictingRows = duplicateGroup.Players
            .Where(player => player.PlayerId != canonicalPlayer.Id)
            .SelectMany(player => player.ProviderRefs
                .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
                .Select(reference => new
                {
                    Player = player,
                    Reference = reference
                }))
            .ToArray();
        var groupMlbMappingCount = duplicateGroup.Players
            .SelectMany(player => player.ProviderRefs)
            .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.ProviderPlayerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return conflictingRows
            .Select(row =>
            {
                var confidence = providerPlayersById.TryGetValue(row.Reference.ProviderPlayerId, out var providerPlayer)
                    ? CalculateMlbMatchConfidence(canonicalPlayer, providerPlayer)
                    : CalculateDisplayNameConfidence(canonicalPlayer.DisplayName, row.Player.DisplayName);
                var safeToMerge = safeToMergeGroup && groupMlbMappingCount == 1;

                return new PlayerIdentityConflictResponse(
                    canonicalPlayer.Id,
                    canonicalPlayer.DisplayName,
                    row.Player.PlayerId,
                    row.Player.DisplayName,
                    row.Reference.ProviderPlayerId,
                    resultCountsByPlayer.GetValueOrDefault(row.Player.PlayerId),
                    propCount,
                    sportsbookCount,
                    confidence,
                    safeToMerge);
            })
            .ToArray();
    }

    private static PlayerIdentityConflictReasonResponse[] BuildPlayerIdentityConflictReasonResponses(
        Player canonicalPlayer,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId,
        IReadOnlyCollection<PlayerIdentityConflictPropRow> propRows,
        IReadOnlyDictionary<Guid, HashSet<Guid>> sportsbooksByPlayer,
        IReadOnlyDictionary<Guid, int> snapshotCountsByPlayer,
        IReadOnlyDictionary<Guid, int> resultCountsByPlayer,
        IReadOnlyDictionary<string, MlbPlayerRecord> providerPlayersById)
    {
        if (!duplicateGroupByPlayerId.TryGetValue(canonicalPlayer.Id, out var duplicateGroup))
        {
            return [];
        }

        var canonicalIdentityRow = duplicateGroup.Players.SingleOrDefault(player => player.PlayerId == canonicalPlayer.Id);
        if (canonicalIdentityRow is null)
        {
            return [];
        }

        var groupConflictingProviderIds = HasConflictingProviderIds(duplicateGroup, includeMlb: true);
        var groupConflictingMlbMappings = HasConflictingMlbMappings(duplicateGroup);
        var canonicalSportsbooks = sportsbooksByPlayer.GetValueOrDefault(canonicalPlayer.Id) ?? [];
        var canonicalHasPropHistory = snapshotCountsByPlayer.GetValueOrDefault(canonicalPlayer.Id) > 0;
        var canonicalHasHistoricalResults = resultCountsByPlayer.GetValueOrDefault(canonicalPlayer.Id) > 0;

        return duplicateGroup.Players
            .Where(player => player.PlayerId != canonicalPlayer.Id)
            .SelectMany(player => player.ProviderRefs
                .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
                .Select(reference =>
                {
                    var conflictingSportsbooks = sportsbooksByPlayer.GetValueOrDefault(player.PlayerId) ?? [];
                    var confidence = providerPlayersById.TryGetValue(reference.ProviderPlayerId, out var providerPlayer)
                        ? CalculateMlbMatchConfidence(canonicalPlayer, providerPlayer)
                        : CalculateDisplayNameConfidence(canonicalPlayer.DisplayName, player.DisplayName);

                    return new PlayerIdentityConflictReasonResponse(
                        canonicalPlayer.Id,
                        canonicalPlayer.DisplayName,
                        player.PlayerId,
                        player.DisplayName,
                        reference.ProviderPlayerId,
                        confidence,
                        groupConflictingProviderIds,
                        groupConflictingMlbMappings,
                        HasConflictingExternalRefs(canonicalIdentityRow, player),
                        HasConflictingTeams(canonicalIdentityRow, player),
                        !string.Equals(
                            CanonicalLeagueCode(canonicalIdentityRow.LeagueCode),
                            CanonicalLeagueCode(player.LeagueCode),
                            StringComparison.OrdinalIgnoreCase),
                        canonicalSportsbooks.Count > 0 && conflictingSportsbooks.Count > 0 && canonicalSportsbooks.Overlaps(conflictingSportsbooks),
                        canonicalHasPropHistory && snapshotCountsByPlayer.GetValueOrDefault(player.PlayerId) > 0,
                        canonicalHasHistoricalResults && resultCountsByPlayer.GetValueOrDefault(player.PlayerId) > 0);
                }))
            .ToArray();
    }

    private static bool HasConflictingProviderIds(DuplicateIdentityGroup duplicateGroup, bool includeMlb)
    {
        return duplicateGroup.Players
            .SelectMany(player => GetExternalProviderIds(player))
            .Where(reference => includeMlb || !string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
            .GroupBy(reference => reference.Provider, StringComparer.OrdinalIgnoreCase)
            .Any(group => group
                .Select(reference => reference.ProviderPlayerId)
                .Where(providerPlayerId => !string.IsNullOrWhiteSpace(providerPlayerId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1);
    }

    private static bool HasConflictingMlbMappings(DuplicateIdentityGroup duplicateGroup)
    {
        return duplicateGroup.Players
            .SelectMany(player => GetExternalProviderIds(player))
            .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.ProviderPlayerId)
            .Where(providerPlayerId => !string.IsNullOrWhiteSpace(providerPlayerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;
    }

    private static bool HasConflictingExternalRefs(PlayerIdentityRow canonicalPlayer, PlayerIdentityRow conflictingPlayer)
    {
        var canonicalRefsByProvider = GetExternalProviderIds(canonicalPlayer)
            .Where(reference => !string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
            .GroupBy(reference => reference.Provider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(reference => reference.ProviderPlayerId)
                    .Where(providerPlayerId => !string.IsNullOrWhiteSpace(providerPlayerId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var conflictingRefsByProvider = GetExternalProviderIds(conflictingPlayer)
            .Where(reference => !string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
            .GroupBy(reference => reference.Provider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(reference => reference.ProviderPlayerId)
                    .Where(providerPlayerId => !string.IsNullOrWhiteSpace(providerPlayerId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        return canonicalRefsByProvider.Any(reference =>
            conflictingRefsByProvider.TryGetValue(reference.Key, out var conflictingIds)
            && reference.Value.Count > 0
            && conflictingIds.Count > 0
            && !reference.Value.SetEquals(conflictingIds));
    }

    private static bool HasConflictingTeams(PlayerIdentityRow canonicalPlayer, PlayerIdentityRow conflictingPlayer)
    {
        if (canonicalPlayer.TeamId.HasValue && conflictingPlayer.TeamId.HasValue)
        {
            return canonicalPlayer.TeamId.Value != conflictingPlayer.TeamId.Value;
        }

        var canonicalTeam = NormalizePlayerName($"{canonicalPlayer.TeamName} {canonicalPlayer.TeamAbbreviation}");
        var conflictingTeam = NormalizePlayerName($"{conflictingPlayer.TeamName} {conflictingPlayer.TeamAbbreviation}");

        return !string.IsNullOrWhiteSpace(canonicalTeam)
            && !string.IsNullOrWhiteSpace(conflictingTeam)
            && !string.Equals(canonicalTeam, conflictingTeam, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string Provider, string ProviderPlayerId)> GetExternalProviderIds(PlayerIdentityRow player)
    {
        foreach (var reference in player.ProviderRefs)
        {
            yield return (reference.Provider, reference.ProviderPlayerId);
        }

        foreach (var reference in player.LegacyExternalRefs)
        {
            if (!string.IsNullOrWhiteSpace(reference.PlayerId))
            {
                yield return (reference.Provider, reference.PlayerId);
            }
        }
    }

    private static decimal CalculateDisplayNameConfidence(string displayName, string conflictingDisplayName)
    {
        var normalizedName = NormalizePlayerName(displayName);
        var normalizedConflictingName = NormalizePlayerName(conflictingDisplayName);

        return !string.IsNullOrWhiteSpace(normalizedName) && normalizedName == normalizedConflictingName
            ? 1.0m
            : 0m;
    }

    private static MlbPlayerCandidateResponse[] GetHighConfidenceSingleCandidates(
        IReadOnlyCollection<MlbPlayerCandidateResponse> candidates)
    {
        return candidates
            .GroupBy(candidate => candidate.PlayerId)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(candidate => candidate.Confidence >= 1.0m)
            .OrderBy(candidate => candidate.PlayerName)
            .ThenBy(candidate => candidate.CandidateDisplayName)
            .ToArray();
    }

    private static MlbPlayerCandidateResponse[] OrderMlbCandidates(
        IReadOnlyCollection<MlbPlayerCandidateResponse> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.PlayerName)
            .ThenBy(candidate => candidate.CandidateDisplayName)
            .ToArray();
    }

    private static async Task<MlbMappingCoverage> GetMlbMappingCoverageAsync(
        ApplicationDbContext dbContext,
        IPlayerExternalRefRepository repository,
        CancellationToken cancellationToken)
    {
        var canonicalPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
        var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
        var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
        var mappedPlayers = canonicalPlayers.Count(player => mappedPlayerIds.Contains(player.Id));
        var totalPlayers = canonicalPlayers.Count;
        var unmappedPlayers = totalPlayers - mappedPlayers;
        var coveragePercent = totalPlayers == 0
            ? 0m
            : Math.Round(mappedPlayers * 100m / totalPlayers, 2);

        return new MlbMappingCoverage(totalPlayers, mappedPlayers, unmappedPlayers, coveragePercent);
    }

    private static async Task<MlbSkippedCandidateResponse[]> GetMlbSkippedCandidatesAsync(
        ApplicationDbContext dbContext,
        IMlbResultsProvider mlbResultsProvider,
        IPlayerExternalRefRepository repository,
        int season,
        CancellationToken cancellationToken)
    {
        var existingRefs = await repository.GetByProviderAsync("mlb", cancellationToken);
        var mappedPlayerIds = existingRefs.Values.Select(reference => reference.PlayerId).ToHashSet();
        var canonicalPlayers = await GetCanonicalMlbPropPlayersAsync(dbContext, cancellationToken);
        var providerPlayers = await mlbResultsProvider.GetPlayersAsync(season, cancellationToken);
        var skipped = new List<MlbSkippedCandidateResponse>();

        foreach (var canonicalPlayer in canonicalPlayers.OrderBy(player => player.DisplayName))
        {
            if (mappedPlayerIds.Contains(canonicalPlayer.Id))
            {
                skipped.Add(new MlbSkippedCandidateResponse(
                    canonicalPlayer.Id,
                    canonicalPlayer.DisplayName,
                    "AlreadyMapped",
                    0,
                    []));
                continue;
            }

            var candidates = providerPlayers
                .Where(providerPlayer => !existingRefs.ContainsKey(providerPlayer.MlbPlayerId))
                .Select(providerPlayer => new MlbSkippedCandidateMatchResponse(
                    providerPlayer.MlbPlayerId,
                    providerPlayer.DisplayName,
                    CalculateMlbMatchConfidence(canonicalPlayer, providerPlayer)))
                .Where(candidate => candidate.Confidence > 0)
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.CandidateDisplayName)
                .ToArray();

            var reason = candidates.Length switch
            {
                0 => "NoCandidateFound",
                > 1 => "MultipleCandidates",
                _ when candidates[0].Confidence < 1.0m => "ConfidenceTooLow",
                _ => null
            };

            if (reason is null)
            {
                continue;
            }

            skipped.Add(new MlbSkippedCandidateResponse(
                canonicalPlayer.Id,
                canonicalPlayer.DisplayName,
                reason,
                candidates.Length,
                candidates));
        }

        return skipped.ToArray();
    }

    private static IReadOnlyList<ExternalRefSummary> SummarizeExternalRefs(JsonDocument? externalRefs)
    {
        if (externalRefs is null || externalRefs.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var summaries = new List<ExternalRefSummary>();
        foreach (var providerProperty in externalRefs.RootElement.EnumerateObject())
        {
            string? playerId = null;
            if (providerProperty.Value.ValueKind == JsonValueKind.Object
                && providerProperty.Value.TryGetProperty("player_id", out var playerIdElement))
            {
                playerId = playerIdElement.ValueKind == JsonValueKind.String
                    ? playerIdElement.GetString()
                    : playerIdElement.GetRawText();
            }

            summaries.Add(new ExternalRefSummary(providerProperty.Name, playerId));
        }

        return summaries;
    }

    private static string InferCreationSource(JsonDocument? externalRefs, IReadOnlyCollection<string> platformSources)
    {
        var summaries = SummarizeExternalRefs(externalRefs);
        if (summaries.Any(summary => string.Equals(summary.Provider, "prizepicks", StringComparison.OrdinalIgnoreCase)))
        {
            return "Historical DFS provider source row";
        }

        if (summaries.Any(summary => string.Equals(summary.Provider, "underdog", StringComparison.OrdinalIgnoreCase)))
        {
            return "Underdog ingestion";
        }

        if (platformSources.Count > 0)
        {
            return $"Unknown legacy source; referenced by {string.Join(", ", platformSources.OrderBy(source => source))}";
        }

        return "Unknown or manual";
    }

    private static async Task<IReadOnlyList<ProviderLineMlbRepairCandidate>> BuildProviderLineMlbRepairCandidatesAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var players = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)
            .Include(player => player.Team)
            .Include(player => player.ExternalProviderRefs)
            .ToArrayAsync(cancellationToken);
        var activePlatformCounts = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Status == "active"
                && platformProp.Prop != null)
            .GroupBy(platformProp => platformProp.Prop!.PlayerId)
            .Select(group => new { PlayerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
        var snapshotRows = await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Sport == "MLB"
                && snapshot.LifecycleStatus == "Active"
                && !snapshot.IsExpired)
            .Select(snapshot => snapshot.DetailsJson)
            .ToArrayAsync(cancellationToken);
        var snapshotCounts = snapshotRows
            .Select(ExtractSnapshotPlayerId)
            .Where(playerId => playerId.HasValue)
            .GroupBy(playerId => playerId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var playerIdsWithLines = activePlatformCounts.Keys
            .Concat(snapshotCounts.Keys)
            .Distinct()
            .ToHashSet();
        var propResultCounts = await dbContext.PropResults
            .AsNoTracking()
            .GroupBy(result => result.PlayerId)
            .Select(group => new { PlayerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
        var pitcherStrikeoutAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PITCHERSTRIKEOUTS",
            "PITCHERSTRIKEOUT",
            "STRIKEOUTS",
            "PITCHERKS",
            "KS"
        };
        var allPropTypes = await dbContext.PropTypes
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var pitcherStrikeoutPropTypeIds = allPropTypes
            .Where(propType => pitcherStrikeoutAliases.Contains(NormalizePlayerName(propType.Name))
                || pitcherStrikeoutAliases.Contains(NormalizePlayerName(propType.StatKey)))
            .Select(propType => propType.Id)
            .ToArray();
        var pitcherStrikeoutCounts = pitcherStrikeoutPropTypeIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(result => pitcherStrikeoutPropTypeIds.Contains(result.PropTypeId))
                .GroupBy(result => result.PlayerId)
                .Select(group => new { PlayerId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
        var mlbRefsByPlayerId = players
            .Select(player => new
            {
                player.Id,
                Refs = player.ExternalProviderRefs
                    .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            })
            .ToDictionary(row => row.Id, row => row.Refs);
        var playersByNormalizedName = players
            .Where(player => string.Equals(player.League?.Code, "MLB", StringComparison.OrdinalIgnoreCase)
                || string.Equals(player.League?.Code, "MLBLIVE", StringComparison.OrdinalIgnoreCase))
            .GroupBy(player => NormalizePlayerName(player.DisplayName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var existingMlbIds = players
            .SelectMany(player => player.ExternalProviderRefs
                .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
                .Select(reference => new { reference.PlayerId, reference.ProviderPlayerId }))
            .GroupBy(reference => reference.ProviderPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(reference => reference.PlayerId).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ProviderLineMlbRepairCandidate>();

        foreach (var player in players
            .Where(player => playerIdsWithLines.Contains(player.Id))
            .Where(player => string.Equals(player.League?.Code, "MLB", StringComparison.OrdinalIgnoreCase)
                || string.Equals(player.League?.Code, "MLBLIVE", StringComparison.OrdinalIgnoreCase))
            .Where(player => !mlbRefsByPlayerId.GetValueOrDefault(player.Id, []).Any())
            .OrderBy(player => player.DisplayName))
        {
            var normalizedName = NormalizePlayerName(player.DisplayName);
            playersByNormalizedName.TryGetValue(normalizedName, out var sameNamePlayers);
            sameNamePlayers ??= [];
            var legacyMlbIds = ExtractLegacyMlbIds(player.ExternalRefs);
            var exactNameCandidates = sameNamePlayers
                .Where(candidate => candidate.Id != player.Id)
                .Select(candidate =>
                {
                    mlbRefsByPlayerId.TryGetValue(candidate.Id, out var candidateMlbRefs);
                    candidateMlbRefs ??= [];
                    return new ProviderLineMlbRepairIdentityCandidate(
                        candidate.Id,
                        candidate.DisplayName,
                        candidate.League?.Code,
                        candidate.Team?.Abbreviation ?? candidate.Team?.Name,
                        candidate.Position,
                        candidateMlbRefs
                            .Select(reference => new ProviderLineMlbRepairExternalRef(reference.Provider, reference.ProviderPlayerId, reference.ProviderDisplayName))
                            .ToArray(),
                        propResultCounts.GetValueOrDefault(candidate.Id),
                        pitcherStrikeoutCounts.GetValueOrDefault(candidate.Id),
                        candidateMlbRefs.Any()
                            ? "Accent-insensitive exact-name duplicate with existing MLB ref."
                            : propResultCounts.GetValueOrDefault(candidate.Id) > 0
                                ? "Accent-insensitive exact-name duplicate owns result rows but has no MLB ref."
                                : "Accent-insensitive exact-name duplicate.");
                })
                .ToArray();
            var unclaimedLegacyMlbIds = legacyMlbIds
                .Where(id => !existingMlbIds.TryGetValue(id, out var ownerIds)
                    || ownerIds.Length == 0
                    || ownerIds.All(ownerId => ownerId == player.Id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var conflictingLegacyMlbIds = legacyMlbIds
                .Except(unclaimedLegacyMlbIds, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var exactNameMlbRefCount = exactNameCandidates
                .SelectMany(candidate => candidate.MlbRefs)
                .Select(reference => reference.ProviderPlayerId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var isSafe = unclaimedLegacyMlbIds.Length == 1
                && conflictingLegacyMlbIds.Length == 0
                && exactNameMlbRefCount == 0;
            var conflictReason = isSafe
                ? null
                : legacyMlbIds.Length == 0 && exactNameCandidates.Length == 0
                    ? "No local MLB id or accent-insensitive exact-name candidate."
                    : conflictingLegacyMlbIds.Length > 0
                        ? "Legacy MLB id is already mapped to another player."
                        : exactNameMlbRefCount > 0
                            ? "Matching MLB id is already mapped to a different player; manual merge/alias review is required."
                            : exactNameCandidates.Length > 1
                                ? "Multiple accent-insensitive exact-name candidates require manual review."
                                : "No unclaimed local MLB id is available for safe auto-approval.";

            candidates.Add(new ProviderLineMlbRepairCandidate(
                player.Id,
                player.DisplayName,
                player.League?.Code,
                player.Team?.Abbreviation ?? player.Team?.Name,
                player.Position,
                activePlatformCounts.GetValueOrDefault(player.Id),
                snapshotCounts.GetValueOrDefault(player.Id),
                player.ExternalProviderRefs
                    .OrderBy(reference => reference.Provider)
                    .Select(reference => new ProviderLineMlbRepairExternalRef(reference.Provider, reference.ProviderPlayerId, reference.ProviderDisplayName))
                    .ToArray(),
                normalizedName,
                legacyMlbIds,
                exactNameCandidates,
                isSafe,
                isSafe ? unclaimedLegacyMlbIds[0] : null,
                isSafe ? player.DisplayName : null,
                isSafe ? "Exactly one unclaimed local/legacy MLB id is available on this provider-line player." : null,
                conflictReason));
        }

        return candidates;
    }

    private static string[] ExtractLegacyMlbIds(JsonDocument? externalRefs)
    {
        if (externalRefs is null || externalRefs.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var providerProperty in externalRefs.RootElement.EnumerateObject())
        {
            if (!string.Equals(providerProperty.Name, "mlb", StringComparison.OrdinalIgnoreCase)
                || providerProperty.Value.ValueKind != JsonValueKind.Object
                || !providerProperty.Value.TryGetProperty("player_id", out var playerIdElement))
            {
                continue;
            }

            var value = playerIdElement.ValueKind == JsonValueKind.String
                ? playerIdElement.GetString()
                : playerIdElement.GetRawText();
            if (!string.IsNullOrWhiteSpace(value))
            {
                ids.Add(value.Trim());
            }
        }

        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Guid? ExtractSnapshotPlayerId(JsonDocument detailsJson)
    {
        var root = detailsJson.RootElement;
        if (TryReadGuidProperty(root, "canonicalPlayerId", out var canonicalPlayerId)
            || TryReadGuidProperty(root, "playerId", out canonicalPlayerId))
        {
            return canonicalPlayerId;
        }

        if (root.TryGetProperty("bestLine", out var bestLine)
            && (TryReadGuidProperty(bestLine, "canonicalPlayerId", out canonicalPlayerId)
                || TryReadGuidProperty(bestLine, "playerId", out canonicalPlayerId)))
        {
            return canonicalPlayerId;
        }

        return null;
    }

    private static bool TryReadGuidProperty(JsonElement element, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.String && Guid.TryParse(property.GetString(), out value);
    }

    private static string NormalizePlayerName(string displayName)
    {
        return PlayerNameNormalizer.ToCompactKey(displayName).ToUpperInvariant();
    }

    private static decimal CalculateMlbMatchConfidence(Player canonicalPlayer, MlbPlayerRecord providerPlayer)
    {
        var canonicalName = NormalizePlayerName(canonicalPlayer.DisplayName);
        var providerName = NormalizePlayerName(providerPlayer.DisplayName);
        var canonicalFirstLast = NormalizePlayerName($"{canonicalPlayer.FirstName} {canonicalPlayer.LastName}");
        var providerFirstLast = NormalizePlayerName($"{providerPlayer.FirstName} {providerPlayer.LastName}");

        var nameMatches = !string.IsNullOrWhiteSpace(canonicalName) && canonicalName == providerName;
        var firstLastMatches = !string.IsNullOrWhiteSpace(canonicalFirstLast)
            && !string.IsNullOrWhiteSpace(providerFirstLast)
            && canonicalFirstLast == providerFirstLast;

        if (!nameMatches && !firstLastMatches)
        {
            return 0;
        }

        var confidence = nameMatches ? 1.0m : 0.95m;
        if (TeamsMatch(canonicalPlayer.Team, providerPlayer))
        {
            confidence += 0.03m;
        }

        return Math.Min(confidence, 1.0m);
    }

    private static bool TeamsMatch(Team? canonicalTeam, MlbPlayerRecord providerPlayer)
    {
        if (canonicalTeam is null)
        {
            return false;
        }

        var canonicalName = NormalizePlayerName(canonicalTeam.Name);
        var canonicalAbbreviation = NormalizePlayerName(canonicalTeam.Abbreviation ?? string.Empty);
        var providerTeamName = NormalizePlayerName(providerPlayer.TeamName ?? string.Empty);
        var providerTeamId = NormalizePlayerName(providerPlayer.TeamId ?? string.Empty);

        return !string.IsNullOrWhiteSpace(providerTeamName)
            && (providerTeamName == canonicalName || providerTeamName == canonicalAbbreviation || providerTeamId == canonicalAbbreviation);
    }
}
