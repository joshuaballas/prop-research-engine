using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SportsBetting.Api.Utilities;

namespace SportsBetting.Api.Endpoints;

public static partial class PlayersEndpoints
{
    public static IEndpointRouteBuilder MapPlayersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/players/search", async (
            string? q,
            int? limit,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await SearchPlayersAsync(dbContext, q, limit, cancellationToken));
        })
        .WithName("PlayersSearch")
        .WithSummary("Read-only persisted player search for Player Research autocomplete.");

        endpoints.MapGet("/api/player-research/current-lines", async (
            string? playerId,
            int? take,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(playerId, out var parsedPlayerId) || parsedPlayerId == Guid.Empty)
            {
                return Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    error = "A valid playerId is required."
                });
            }

            var lookup = await BuildPlayerResearchLookupAsync(dbContext, parsedPlayerId, take, cancellationToken);
            return Results.Ok(new
            {
                lookup.GeneratedAtUtc,
                lookup.CallsExternalApis,
                lookup.ReadOnly,
                playerId = lookup.SelectedPlayer?.Id,
                effectivePlayerId = lookup.EffectivePlayerId,
                lines = lookup.CurrentLines,
                audit = lookup.ToAudit()
            });
        })
        .WithName("PlayerResearchCurrentLines")
        .WithSummary("Read-only current provider lines for a selected Player Research player using safe equivalent identity lookup.");

        endpoints.MapGet("/api/debug/player-research/lookup-audit", async (
            Guid? playerId,
            int? take,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!playerId.HasValue || playerId.Value == Guid.Empty)
            {
                return Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    error = "playerId is required."
                });
            }

            var lookup = await BuildPlayerResearchLookupAsync(dbContext, playerId.Value, take, cancellationToken);
            return Results.Ok(lookup.ToAudit());
        })
        .WithName("DebugPlayerResearchLookupAudit")
        .WithSummary("Debug-only read audit for Player Research selected-player identity, current lines, and prop-history coverage.");

        endpoints.MapGet("/api/debug/player-research/name-audit", async (
            string? q,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var normalizedQuery = NormalizeSearchQuery(q);
            if (normalizedQuery.Length < 2)
            {
                return Results.Ok(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    query = q,
                    normalizedQuery,
                    reason = "Enter at least two characters.",
                    tableCountsSearched = await LoadPlayerResearchTableCountsAsync(dbContext, cancellationToken)
                });
            }

            var report = await BuildPlayerResearchNameAuditAsync(dbContext, normalizedQuery, cancellationToken);
            return Results.Ok(report);
        })
        .WithName("DebugPlayerResearchNameAudit")
        .WithSummary("Debug-only read audit for Player Research player-name coverage across persisted players, refs, lines, snapshots, and results.");

        endpoints.MapGet("/api/debug/player-research/history-audit", async (
            Guid? playerId,
            string? propType,
            decimal? line,
            string? side,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!playerId.HasValue || playerId.Value == Guid.Empty)
            {
                return Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    error = "playerId is required."
                });
            }

            var audit = await BuildPlayerResearchHistoryAuditAsync(dbContext, playerId.Value, propType, line, side, cancellationToken);
            return Results.Ok(audit);
        })
        .WithName("DebugPlayerResearchHistoryAudit")
        .WithSummary("Debug-only read audit for Player Research prop-history identity and stat-key resolution.");

        endpoints.MapGet("/api/debug/mlb/player-result-coverage", async (
            string? q,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var audit = await BuildMlbPlayerResultCoverageAuditAsync(dbContext, q, cancellationToken);
            return Results.Ok(audit);
        })
        .WithName("DebugMlbPlayerResultCoverage")
        .WithSummary("Debug-only read audit for MLB player refs and persisted prop_results coverage.");

        return endpoints;
    }

    internal static async Task<IReadOnlyList<PlayerSearchResult>> SearchPlayersAsync(
        ApplicationDbContext dbContext,
        string? query,
        int? limit,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeSearchQuery(query);
        if (normalizedQuery.Length < 2)
        {
            return [];
        }

        var take = Math.Clamp(limit ?? 5, 1, 10);

        var queryTokens = Tokenize(normalizedQuery);
        if (queryTokens.Length == 0)
        {
            return [];
        }

        var primaryToken = queryTokens[0];
        var secondaryToken = queryTokens.Length > 1 ? queryTokens[^1] : primaryToken;
        var hasMultipleTokens = queryTokens.Length > 1;

        var candidates = await dbContext.Players
            .AsNoTracking()
            .Where(player =>
                player.DisplayName.ToLower().Contains(normalizedQuery)
                || player.DisplayName.ToLower().Contains(primaryToken)
                || (hasMultipleTokens && player.DisplayName.ToLower().Contains(secondaryToken)))
            .Select(player => new
            {
                player.Id,
                player.DisplayName,
                League = player.League == null ? null : player.League.Code,
                Sport = player.League == null || player.League.Sport == null ? null : player.League.Sport.Code,
                Team = player.Team == null ? null : player.Team.Name,
                TeamAbbreviation = player.Team == null ? null : player.Team.Abbreviation,
                player.Position,
                HasRecentActivity = player.Props.Any() || player.PropResults.Any(),
                PropResultCount = player.PropResults.Count,
                SortName = player.DisplayName.ToLower()
            })
            .OrderBy(player => player.SortName.Contains(normalizedQuery) ? 0 : player.SortName.Contains(secondaryToken) ? 1 : 2)
            .ThenBy(player => player.DisplayName)
            .Take(250)
            .ToListAsync(cancellationToken);
        var currentLineCounts = await LoadCurrentLineCountsByPlayerIdAsync(dbContext, candidates.Select(player => player.Id).ToArray(), cancellationToken);
        var exactNameCurrentLineCounts = await LoadExactNameCurrentLineCountsAsync(dbContext, candidates.Select(player => player.DisplayName).Distinct().ToArray(), cancellationToken);

        var rankedCandidates = candidates
            .Select(player => new
            {
                Player = player,
                SearchRank = RankCandidate(NormalizeSearchQuery(player.DisplayName), queryTokens, normalizedQuery),
                DedupeKey = NormalizeCompactHistoryKey(player.DisplayName),
                CurrentLineCount = currentLineCounts.GetValueOrDefault(player.Id),
                ExactNameCurrentLineCount = exactNameCurrentLineCounts.GetValueOrDefault(NormalizeCompactHistoryKey(player.DisplayName))
            })
            .Where(candidate => candidate.SearchRank < 100)
            .ToArray();

        var hasAllTokenMatch = queryTokens.Length > 1 && rankedCandidates.Any(candidate => candidate.SearchRank <= 2);
        var visibleCandidates = hasAllTokenMatch
            ? rankedCandidates.Where(candidate => candidate.SearchRank <= 2)
            : rankedCandidates;

        return visibleCandidates
            .OrderBy(candidate => candidate.SearchRank)
            .ThenByDescending(candidate => candidate.CurrentLineCount)
            .ThenByDescending(candidate => candidate.ExactNameCurrentLineCount)
            .ThenByDescending(candidate => candidate.Player.HasRecentActivity)
            .ThenByDescending(candidate => candidate.Player.PropResultCount)
            .ThenBy(candidate => LeaguePriority(candidate.Player.League))
            .ThenBy(candidate => candidate.Player.DisplayName)
            .GroupBy(candidate => candidate.DedupeKey)
            .Select(group => group.First())
            .Take(take)
            .Select(player =>
            {
                var sportOrLeague = FirstNonBlank(player.Player.League, player.Player.Sport);
                var team = FirstNonBlank(player.Player.TeamAbbreviation, player.Player.Team);
                var subtitleParts = new[] { team, sportOrLeague, player.Player.Position }
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .ToArray();

                return new PlayerSearchResult(
                    PlayerId: player.Player.Id,
                    EffectivePlayerId: player.CurrentLineCount > 0 ? player.Player.Id : null,
                    DisplayName: player.Player.DisplayName,
                    League: player.Player.League,
                    Sport: player.Player.Sport,
                    Team: team,
                    Position: player.Player.Position,
                    Subtitle: subtitleParts.Length == 0 ? null : string.Join(" - ", subtitleParts));
            })
            .ToArray();
    }

    private static async Task<PlayerResearchLookup> BuildPlayerResearchLookupAsync(
        ApplicationDbContext dbContext,
        Guid selectedPlayerId,
        int? take,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var requestedTake = Math.Clamp(take ?? 50, 1, 100);
        var selectedPlayer = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)
            .SingleOrDefaultAsync(player => player.Id == selectedPlayerId, cancellationToken);

        if (selectedPlayer is null)
        {
            return PlayerResearchLookup.Empty(generatedAtUtc, "Selected player was not found.");
        }

        var equivalentIds = await ResolvePlayerResearchEquivalentPlayerIdsAsync(dbContext, selectedPlayer, cancellationToken);
        var initialCandidateIds = equivalentIds.Append(selectedPlayer.Id).Distinct().ToArray();
        var candidateIds = initialCandidateIds;
        var candidateSet = candidateIds.ToHashSet();
        var selectedNormalizedName = NormalizeCompactHistoryKey(selectedPlayer.DisplayName);
        var exactNamePlayers = await LoadExactNamePlayersAsync(dbContext, selectedPlayer.DisplayName, cancellationToken);
        var externalRefs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => exactNamePlayers.Select(player => player.Id).Contains(reference.PlayerId))
            .OrderBy(reference => reference.Provider)
            .ThenBy(reference => reference.ProviderDisplayName)
            .Select(reference => new PlayerResearchExternalRef(reference.PlayerId, reference.Provider, reference.ProviderPlayerId, reference.ProviderDisplayName))
            .ToArrayAsync(cancellationToken);
        var snapshotRows = await LoadCurrentPlayerResearchSnapshotsAsync(dbContext, cancellationToken);
        var snapshotInfos = snapshotRows
            .Select(row => new
            {
                Snapshot = row,
                SnapshotPlayerId = ExtractSnapshotPlayerId(row.DetailsJson),
                NameMatches = NormalizeCompactHistoryKey(row.Player) == selectedNormalizedName
            })
            .ToArray();
        var exactNamePlayerIds = exactNamePlayers.Select(player => player.Id).ToArray();
        var exactNamePlatformPropCounts = await LoadActivePlatformPropCountsByPlayerIdAsync(dbContext, exactNamePlayerIds, cancellationToken);
        var exactNameLineOwnerIds = snapshotInfos
            .Where(row => row.NameMatches && row.SnapshotPlayerId.HasValue)
            .GroupBy(row => row.SnapshotPlayerId!.Value)
            .Select(group => group.Key)
            .Concat(exactNamePlatformPropCounts.Where(pair => pair.Value > 0).Select(pair => pair.Key))
            .Distinct()
            .ToArray();
        var exactNameLineOwnerIdsInPlayers = exactNameLineOwnerIds
            .Where(id => exactNamePlayers.Any(player => player.Id == id))
            .Distinct()
            .ToArray();

        string? resolutionMode = "AliasOrSelectedPlayerId";
        string? ambiguityReason = null;
        if (exactNameLineOwnerIdsInPlayers.Length == 1 && !candidateSet.Contains(exactNameLineOwnerIdsInPlayers[0]))
        {
            candidateIds = candidateIds.Append(exactNameLineOwnerIdsInPlayers[0]).Distinct().ToArray();
            candidateSet = candidateIds.ToHashSet();
            resolutionMode = "ExactNormalizedNameSingleLineOwner";
        }
        else if (exactNameLineOwnerIdsInPlayers.Length > 1 && !exactNameLineOwnerIdsInPlayers.Any(candidateSet.Contains))
        {
            ambiguityReason = "Multiple exact-name player identities own current provider lines; Player Research did not combine them without an explicit alias.";
            resolutionMode = "AmbiguousExactNormalizedNameLineOwners";
        }

        var propResultCounts = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => candidateIds.Contains(result.PlayerId) || exactNamePlayers.Select(player => player.Id).Contains(result.PlayerId))
            .GroupBy(result => result.PlayerId)
            .Select(group => new { PlayerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
        var matchingSnapshots = snapshotInfos
            .Where(row => row.SnapshotPlayerId.HasValue
                ? candidateSet.Contains(row.SnapshotPlayerId.Value)
                : ambiguityReason is null && row.NameMatches)
            .ToArray();
        var lineCounts = matchingSnapshots
            .Where(row => row.SnapshotPlayerId.HasValue)
            .GroupBy(row => row.SnapshotPlayerId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var candidatePlatformPropCounts = await LoadActivePlatformPropCountsByPlayerIdAsync(dbContext, candidateIds, cancellationToken);
        var combinedLineCounts = candidateIds.ToDictionary(
            id => id,
            id => lineCounts.GetValueOrDefault(id) + candidatePlatformPropCounts.GetValueOrDefault(id));
        var effectivePlayerId = candidateIds
            .OrderByDescending(id => combinedLineCounts.GetValueOrDefault(id))
            .ThenByDescending(id => propResultCounts.GetValueOrDefault(id))
            .ThenBy(id => id == selectedPlayer.Id ? 0 : 1)
            .FirstOrDefault();

        if (effectivePlayerId == Guid.Empty)
        {
            effectivePlayerId = selectedPlayer.Id;
        }

        var rawSnapshotCurrentLines = matchingSnapshots
            .OrderByDescending(row => row.SnapshotPlayerId == effectivePlayerId)
            .ThenByDescending(row => row.Snapshot.BestEstimatedEv ?? decimal.MinValue)
            .ThenBy(row => row.Snapshot.Player)
            .ThenBy(row => row.Snapshot.PropType)
            .Select(row => ToPlayerResearchMarketLine(row.Snapshot, row.SnapshotPlayerId ?? effectivePlayerId))
            .ToArray();
        var rawCurrentLines = rawSnapshotCurrentLines.Length > 0
            ? rawSnapshotCurrentLines
            : await LoadPlayerResearchPlatformPropLinesAsync(dbContext, candidateIds, effectivePlayerId, requestedTake, cancellationToken);
        var identityGroupKey = BuildPlayerResearchIdentityGroupKey(candidateIds);
        var currentLines = DedupePlayerResearchMarketLines(rawCurrentLines, identityGroupKey, selectedPlayer.Id, effectivePlayerId)
            .Take(requestedTake)
            .ToArray();
        var candidateSummaries = await dbContext.Players
            .AsNoTracking()
            .Where(player => candidateIds.Contains(player.Id))
            .Select(player => new
            {
                player.Id,
                player.DisplayName,
                League = player.League == null ? null : player.League.Code
            })
            .ToArrayAsync(cancellationToken);
        var candidateRows = candidateSummaries
            .Select(player => new PlayerResearchCandidatePlayer(
                player.Id,
                player.DisplayName,
                player.League,
                combinedLineCounts.GetValueOrDefault(player.Id),
                propResultCounts.GetValueOrDefault(player.Id)))
            .OrderByDescending(player => player.CurrentProviderLineCount)
            .ThenByDescending(player => player.PropResultHistoryCount)
            .ThenBy(player => player.DisplayName)
            .ToArray();

        return new PlayerResearchLookup(
            generatedAtUtc,
            CallsExternalApis: false,
            ReadOnly: true,
            new PlayerResearchSelectedPlayer(selectedPlayer.Id, selectedPlayer.DisplayName, selectedPlayer.League?.Code),
            candidateRows,
            ExternalRefs: externalRefs,
            effectivePlayerId,
            currentLines,
            resolutionMode,
            ExactNameLineOwnerPlayerIds: exactNameLineOwnerIdsInPlayers,
            RawCurrentLineCount: rawCurrentLines.Length,
            DuplicateCurrentLineCount: Math.Max(0, rawCurrentLines.Length - currentLines.Length),
            Reason: ambiguityReason ?? (currentLines.Length == 0 ? "No current provider lines matched the selected player's safe equivalent identities or an unambiguous exact-name line owner." : null));
    }

    private static async Task<IReadOnlyDictionary<Guid, int>> LoadCurrentLineCountsByPlayerIdAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var playerIdSet = playerIds.ToHashSet();
        var snapshots = await LoadCurrentPlayerResearchSnapshotsAsync(dbContext, cancellationToken);
        var snapshotCounts = snapshots
            .Select(row => ExtractSnapshotPlayerId(row.DetailsJson))
            .Where(playerId => playerId.HasValue && playerIdSet.Contains(playerId.Value))
            .GroupBy(playerId => playerId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var platformPropCounts = await LoadActivePlatformPropCountsByPlayerIdAsync(dbContext, playerIds, cancellationToken);

        return playerIds
            .Distinct()
            .ToDictionary(
                playerId => playerId,
                playerId => snapshotCounts.GetValueOrDefault(playerId) + platformPropCounts.GetValueOrDefault(playerId));
    }

    private static async Task<IReadOnlyDictionary<string, int>> LoadExactNameCurrentLineCountsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<string> displayNames,
        CancellationToken cancellationToken)
    {
        if (displayNames.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var normalizedNames = displayNames
            .Select(NormalizeCompactHistoryKey)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (normalizedNames.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var snapshots = await LoadCurrentPlayerResearchSnapshotsAsync(dbContext, cancellationToken);
        var snapshotCounts = snapshots
            .Select(row => NormalizeCompactHistoryKey(row.Player))
            .Where(name => normalizedNames.Contains(name))
            .GroupBy(name => name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var platformPropRows = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Prop != null
                && platformProp.Prop.Player != null
                && platformProp.Status == "active")
            .Select(platformProp => platformProp.Prop!.Player!.DisplayName)
            .ToArrayAsync(cancellationToken);
        var platformPropCounts = platformPropRows
            .Select(NormalizeCompactHistoryKey)
            .Where(name => normalizedNames.Contains(name))
            .GroupBy(name => name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return normalizedNames.ToDictionary(
            name => name,
            name => snapshotCounts.GetValueOrDefault(name) + platformPropCounts.GetValueOrDefault(name),
            StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<Guid, int>> LoadActivePlatformPropCountsByPlayerIdAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Prop != null
                && playerIds.Contains(platformProp.Prop.PlayerId)
                && platformProp.Status == "active")
            .GroupBy(platformProp => platformProp.Prop!.PlayerId)
            .Select(group => new { PlayerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
    }

    private static async Task<IReadOnlyList<MarketLineShoppingSnapshot>> LoadCurrentPlayerResearchSnapshotsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == "MLB"
                && !row.IsExpired
                && row.LifecycleStatus == "Active")
            .OrderByDescending(row => row.GeneratedAtUtc)
            .Take(10000)
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<PlayerResearchExactNamePlayer>> LoadExactNamePlayersAsync(
        ApplicationDbContext dbContext,
        string displayName,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeCompactHistoryKey(displayName);
        var lowerName = displayName.Trim().ToLowerInvariant();
        var firstToken = displayName.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .ToLowerInvariant() ?? lowerName;
        var candidates = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.DisplayName.ToLower().Contains(lowerName)
                || player.DisplayName.ToLower().Contains(firstToken))
            .Select(player => new
            {
                player.Id,
                player.DisplayName,
                League = player.League == null ? null : player.League.Code,
                Sport = player.League == null || player.League.Sport == null ? null : player.League.Sport.Code,
                Team = player.Team == null ? null : player.Team.Name,
                TeamAbbreviation = player.Team == null ? null : player.Team.Abbreviation,
                player.Position
            })
            .ToArrayAsync(cancellationToken);

        return candidates
            .Where(player => NormalizeCompactHistoryKey(player.DisplayName) == normalizedName)
            .Select(player => new PlayerResearchExactNamePlayer(
                player.Id,
                player.DisplayName,
                player.League,
                player.Sport,
                FirstNonBlank(player.TeamAbbreviation, player.Team),
                player.Position))
            .ToArray();
    }

    private static async Task<PlayerResearchMarketLine[]> LoadPlayerResearchPlatformPropLinesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> candidatePlayerIds,
        Guid effectivePlayerId,
        int take,
        CancellationToken cancellationToken)
    {
        if (candidatePlayerIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Prop != null
                && candidatePlayerIds.Contains(platformProp.Prop.PlayerId)
                && platformProp.Status == "active")
            .OrderByDescending(platformProp => platformProp.Prop!.PlayerId == effectivePlayerId)
            .ThenByDescending(platformProp => platformProp.ObservedAt)
            .ThenBy(platformProp => platformProp.Platform == null ? platformProp.Label : platformProp.Platform.Name)
            .ThenBy(platformProp => platformProp.Prop!.PropType == null ? platformProp.Label : platformProp.Prop.PropType.Name)
            .Take(Math.Clamp(take * 5, take, 500))
            .Select(platformProp => new
            {
                platformProp.Id,
                platformProp.CurrentLine,
                platformProp.OverAvailable,
                platformProp.UnderAvailable,
                platformProp.Status,
                platformProp.ObservedAt,
                Provider = platformProp.Platform == null ? "Unknown provider" : platformProp.Platform.Name,
                PlayerId = platformProp.Prop!.PlayerId,
                Player = platformProp.Prop.Player == null ? platformProp.Label : platformProp.Prop.Player.DisplayName,
                Sport = platformProp.Prop.League == null || platformProp.Prop.League.Sport == null ? "MLB" : platformProp.Prop.League.Sport.Code,
                PropType = platformProp.Prop.PropType == null ? platformProp.Label : platformProp.Prop.PropType.Name,
                EventStartTime = platformProp.Prop.StartsAt,
                AwayTeamName = platformProp.Prop.Game == null || platformProp.Prop.Game.AwayTeam == null ? null : platformProp.Prop.Game.AwayTeam.Name,
                AwayTeamAbbreviation = platformProp.Prop.Game == null || platformProp.Prop.Game.AwayTeam == null ? null : platformProp.Prop.Game.AwayTeam.Abbreviation,
                HomeTeamName = platformProp.Prop.Game == null || platformProp.Prop.Game.HomeTeam == null ? null : platformProp.Prop.Game.HomeTeam.Name,
                HomeTeamAbbreviation = platformProp.Prop.Game == null || platformProp.Prop.Game.HomeTeam == null ? null : platformProp.Prop.Game.HomeTeam.Abbreviation
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row => ToPlayerResearchMarketLine(
                row.Id,
                row.PlayerId,
                row.Player,
                row.Sport,
                row.Provider,
                row.PropType,
                row.CurrentLine,
                SelectPlatformPropSide(row.OverAvailable, row.UnderAvailable),
                row.EventStartTime,
                row.Status,
                row.ObservedAt,
                FirstNonBlank(row.AwayTeamName, row.AwayTeamAbbreviation),
                FirstNonBlank(row.HomeTeamName, row.HomeTeamAbbreviation)))
            .ToArray();
    }

    private static async Task<object> BuildPlayerResearchNameAuditAsync(
        ApplicationDbContext dbContext,
        string normalizedQuery,
        CancellationToken cancellationToken)
    {
        var tableCounts = await LoadPlayerResearchTableCountsAsync(dbContext, cancellationToken);
        var playerRows = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.DisplayName.ToLower().Contains(normalizedQuery))
            .Select(player => new
            {
                player.Id,
                player.DisplayName,
                League = player.League == null ? null : player.League.Code,
                Sport = player.League == null || player.League.Sport == null ? null : player.League.Sport.Code,
                Team = player.Team == null ? null : player.Team.Name,
                TeamAbbreviation = player.Team == null ? null : player.Team.Abbreviation,
                player.Position
            })
            .Take(100)
            .ToArrayAsync(cancellationToken);
        var compactQuery = NormalizeCompactHistoryKey(normalizedQuery);
        var normalizedPlayerIds = playerRows
            .Where(player => NormalizeCompactHistoryKey(player.DisplayName) == compactQuery)
            .Select(player => player.Id)
            .ToHashSet();
        var externalRefs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => normalizedPlayerIds.Contains(reference.PlayerId)
                || reference.ProviderDisplayName.ToLower().Contains(normalizedQuery)
                || reference.ProviderPlayerId.ToLower().Contains(normalizedQuery))
            .OrderBy(reference => reference.Provider)
            .ThenBy(reference => reference.ProviderDisplayName)
            .Select(reference => new PlayerResearchExternalRef(reference.PlayerId, reference.Provider, reference.ProviderPlayerId, reference.ProviderDisplayName))
            .ToArrayAsync(cancellationToken);
        var currentSnapshots = await LoadCurrentPlayerResearchSnapshotsAsync(dbContext, cancellationToken);
        var matchingSnapshots = currentSnapshots
            .Select(row => new
            {
                Snapshot = row,
                SnapshotPlayerId = ExtractSnapshotPlayerId(row.DetailsJson),
                NormalizedName = NormalizeCompactHistoryKey(row.Player)
            })
            .Where(row => row.NormalizedName == compactQuery)
            .ToArray();
        var currentLineCounts = matchingSnapshots
            .Where(row => row.SnapshotPlayerId.HasValue)
            .GroupBy(row => row.SnapshotPlayerId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var activePlatformPropCounts = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Prop != null
                && normalizedPlayerIds.Contains(platformProp.Prop.PlayerId)
                && platformProp.Status == "active")
            .GroupBy(platformProp => platformProp.Prop!.PlayerId)
            .Select(group => new { PlayerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
        var propResultCounts = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => normalizedPlayerIds.Contains(result.PlayerId))
            .GroupBy(result => result.PlayerId)
            .Select(group => new { PlayerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
        var recommendationSnapshotCounts = await dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.CanonicalPlayerId.HasValue && normalizedPlayerIds.Contains(snapshot.CanonicalPlayerId.Value))
            .GroupBy(snapshot => snapshot.CanonicalPlayerId!.Value)
            .Select(group => new { PlayerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);
        var platformProps = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Prop != null
                && normalizedPlayerIds.Contains(platformProp.Prop.PlayerId))
            .OrderByDescending(platformProp => platformProp.ObservedAt)
            .Take(100)
            .Select(platformProp => new
            {
                platformPropId = platformProp.Id,
                externalPlatformPropId = platformProp.PlatformPropId,
                playerId = platformProp.Prop!.PlayerId,
                playerName = platformProp.Prop.Player == null ? null : platformProp.Prop.Player.DisplayName,
                provider = platformProp.Platform == null ? null : platformProp.Platform.Name,
                propType = platformProp.Prop.PropType == null ? null : platformProp.Prop.PropType.Name,
                side = platformProp.OverAvailable && platformProp.UnderAvailable ? "Over/Under" : platformProp.OverAvailable ? "Over" : platformProp.UnderAvailable ? "Under" : null,
                line = platformProp.CurrentLine,
                eventStartTimeUtc = platformProp.Prop.StartsAt,
                status = platformProp.Status,
                actionable = platformProp.Status == "active"
            })
            .ToArrayAsync(cancellationToken);

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            callsExternalApis = false,
            readOnly = true,
            query = normalizedQuery,
            tableCountsSearched = tableCounts,
            playerRows = playerRows
                .Select(player => new
                {
                    playerId = player.Id,
                    player.DisplayName,
                    player.League,
                    player.Sport,
                    team = FirstNonBlank(player.TeamAbbreviation, player.Team),
                    player.Position,
                    externalRefs = externalRefs.Where(reference => reference.PlayerId == player.Id).ToArray(),
                    currentProviderLineCount = currentLineCounts.GetValueOrDefault(player.Id),
                    activeCurrentPlatformPropCount = activePlatformPropCounts.GetValueOrDefault(player.Id),
                    propHistoryResultCount = propResultCounts.GetValueOrDefault(player.Id),
                    recommendationSnapshotCount = recommendationSnapshotCounts.GetValueOrDefault(player.Id)
                })
                .ToArray(),
            currentProviderSnapshotRows = matchingSnapshots
                .Select(row => new
                {
                    currentLineId = row.Snapshot.Id,
                    playerId = row.SnapshotPlayerId,
                    playerName = row.Snapshot.Player,
                    provider = row.Snapshot.Provider,
                    bookmaker = row.Snapshot.Bookmaker,
                    propType = row.Snapshot.PropType,
                    side = row.Snapshot.BestSide,
                    line = row.Snapshot.Line,
                    eventStartTimeUtc = row.Snapshot.EventStartTime,
                    lifecycleStatus = row.Snapshot.LifecycleStatus,
                    row.Snapshot.IsStale,
                    row.Snapshot.IsExpired,
                    actionable = row.Snapshot.EntryActionableForUser
                })
                .ToArray(),
            platformPropRows = platformProps,
            noRowsFoundAnywhere = playerRows.Length == 0 && matchingSnapshots.Length == 0 && platformProps.Length == 0
        };
    }

    private static async Task<object> BuildPlayerResearchHistoryAuditAsync(
        ApplicationDbContext dbContext,
        Guid selectedPlayerId,
        string? propType,
        decimal? line,
        string? side,
        CancellationToken cancellationToken)
    {
        var selectedPlayer = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)
            .SingleOrDefaultAsync(player => player.Id == selectedPlayerId, cancellationToken);
        if (selectedPlayer is null)
        {
            return new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                selectedPlayerId,
                reason = "Selected player was not found."
            };
        }

        var selectedNormalizedName = NormalizeCompactHistoryKey(selectedPlayer.DisplayName);
        var equivalentIds = await ResolvePlayerResearchEquivalentPlayerIdsAsync(dbContext, selectedPlayer, cancellationToken);
        var exactNamePlayers = await LoadExactNamePlayersAsync(dbContext, selectedPlayer.DisplayName, cancellationToken);
        var exactNamePlayerIds = exactNamePlayers.Select(player => player.Id).ToArray();
        var expectedStatKey = ResolvePlayerResearchHistoryStatKey(propType);
        var expectedAliases = BuildPlayerResearchHistoryStatAliases(expectedStatKey);
        var allPropTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(row => new { row.Id, row.Name, row.StatKey })
            .ToArrayAsync(cancellationToken);
        var matchingPropTypeIds = allPropTypes
            .Where(row => expectedAliases.Contains(NormalizeCompactHistoryKey(row.Name))
                || expectedAliases.Contains(NormalizeCompactHistoryKey(row.StatKey)))
            .Select(row => row.Id)
            .Distinct()
            .ToArray();
        var selectedAndEquivalentIds = equivalentIds.Append(selectedPlayer.Id).Distinct().ToArray();
        var sameSelectedPlayerIdCount = await dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => result.PlayerId == selectedPlayer.Id, cancellationToken);
        var sameEquivalentIdsCount = await dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => selectedAndEquivalentIds.Contains(result.PlayerId), cancellationToken);
        var sameNormalizedNameCount = await dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => exactNamePlayerIds.Contains(result.PlayerId), cancellationToken);
        var sameStatKeyCount = matchingPropTypeIds.Length == 0
            ? 0
            : await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(result => matchingPropTypeIds.Contains(result.PropTypeId), cancellationToken);
        var directMatchingCount = matchingPropTypeIds.Length == 0
            ? 0
            : await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(result => selectedAndEquivalentIds.Contains(result.PlayerId)
                    && matchingPropTypeIds.Contains(result.PropTypeId), cancellationToken);
        var exactNameOwners = matchingPropTypeIds.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(result => exactNamePlayerIds.Contains(result.PlayerId)
                    && matchingPropTypeIds.Contains(result.PropTypeId))
                .GroupBy(result => result.PlayerId)
                .Select(group => new { PlayerId = group.Key, Count = group.Count() })
                .ToArrayAsync(cancellationToken);
        var effectiveHistoryPlayerIds = directMatchingCount > 0
            ? selectedAndEquivalentIds
            : exactNameOwners.Length == 1
                ? selectedAndEquivalentIds.Append(exactNameOwners[0].PlayerId).Distinct().ToArray()
                : selectedAndEquivalentIds;
        var latestRows = matchingPropTypeIds.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.Player)
                .Include(result => result.PropType)
                .Where(result => effectiveHistoryPlayerIds.Contains(result.PlayerId)
                    && matchingPropTypeIds.Contains(result.PropTypeId))
                .OrderByDescending(result => result.EventDate)
                .ThenBy(result => result.Id)
                .Take(10)
                .Select(result => new
                {
                    propResultId = result.Id,
                    resultDate = result.EventDate,
                    playerId = result.PlayerId,
                    playerName = result.Player == null ? null : result.Player.DisplayName,
                    statKey = result.PropType == null ? null : result.PropType.StatKey,
                    propType = result.PropType == null ? null : result.PropType.Name,
                    value = result.ActualResultValue,
                    gameId = result.ExternalSourceIdentifier,
                    gameDate = result.EventDate
                })
                .ToArrayAsync(cancellationToken);
        var availableStatKeys = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.PropType)
            .Where(result => exactNamePlayerIds.Contains(result.PlayerId))
            .GroupBy(result => new
            {
                PropType = result.PropType == null ? "Unknown" : result.PropType.Name,
                StatKey = result.PropType == null ? "Unknown" : result.PropType.StatKey
            })
            .Select(group => new
            {
                propType = group.Key.PropType,
                statKey = group.Key.StatKey,
                normalizedStatKey = NormalizeCompactHistoryKey(group.Key.StatKey),
                count = group.Count()
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.propType)
            .Take(20)
            .ToArrayAsync(cancellationToken);
        var refs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => selectedAndEquivalentIds.Contains(reference.PlayerId) || exactNamePlayerIds.Contains(reference.PlayerId))
            .OrderBy(reference => reference.Provider)
            .ThenBy(reference => reference.ProviderDisplayName)
            .Select(reference => new PlayerResearchExternalRef(reference.PlayerId, reference.Provider, reference.ProviderPlayerId, reference.ProviderDisplayName))
            .ToArrayAsync(cancellationToken);

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            callsExternalApis = false,
            readOnly = true,
            selectedPlayerId = selectedPlayer.Id,
            selectedPlayerName = selectedPlayer.DisplayName,
            selectedNormalizedName,
            selectedPropType = propType,
            selectedSide = side,
            selectedLine = line,
            normalizedExpectedStatKey = expectedStatKey,
            expectedStatKeyAliases = expectedAliases.ToArray(),
            externalRefs = refs,
            equivalentPlayerIds = selectedAndEquivalentIds,
            exactNamePlayerIds,
            effectiveHistoryPlayerIds,
            propResultsCounts = new
            {
                sameSelectedPlayerId = sameSelectedPlayerIdCount,
                sameEffectiveEquivalentIds = sameEquivalentIdsCount,
                sameNormalizedPlayerName = sameNormalizedNameCount,
                sameStatKey = sameStatKeyCount,
                sameEffectiveEquivalentIdsAndStatKey = directMatchingCount,
                exactNameHistoryOwners = exactNameOwners
            },
            availableStatKeysForExactName = availableStatKeys,
            matchingPropTypes = allPropTypes
                .Where(row => matchingPropTypeIds.Contains(row.Id))
                .Select(row => new { row.Id, row.Name, row.StatKey, normalizedStatKey = NormalizeCompactHistoryKey(row.StatKey) })
                .ToArray(),
            latestMatchingRows = latestRows,
            reason = latestRows.Length > 0
                ? null
                : matchingPropTypeIds.Length == 0
                    ? "No prop type matched the selected prop type/stat key."
                    : exactNameOwners.Length > 1 && directMatchingCount == 0
                        ? "Multiple exact-name player identities have this stat history; not combining without an explicit alias."
                        : "No persisted prop_results matched the selected/effective player identity and stat key."
        };
    }

    private static async Task<object> BuildMlbPlayerResultCoverageAuditAsync(
        ApplicationDbContext dbContext,
        string? query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeSearchQuery(query);
        var compactQuery = NormalizeCompactHistoryKey(query);
        if (normalizedQuery.Length < 2 || compactQuery.Length < 2)
        {
            return new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                query,
                normalizedQuery,
                reason = "Enter at least two characters."
            };
        }

        var tokens = Tokenize(normalizedQuery);
        var primaryToken = tokens.Length > 0 ? tokens[0] : normalizedQuery;
        var secondaryToken = tokens.Length > 1 ? tokens[^1] : primaryToken;
        var candidatePlayers = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)
            .Include(player => player.Team)
            .Where(player => player.DisplayName.ToLower().Contains(normalizedQuery)
                || player.DisplayName.ToLower().Contains(primaryToken)
                || player.DisplayName.ToLower().Contains(secondaryToken))
            .Select(player => new
            {
                player.Id,
                player.DisplayName,
                League = player.League == null ? null : player.League.Code,
                Team = player.Team == null ? null : player.Team.Name,
                TeamAbbreviation = player.Team == null ? null : player.Team.Abbreviation,
                player.Position
            })
            .Take(250)
            .ToArrayAsync(cancellationToken);
        var matchingPlayers = candidatePlayers
            .Where(player =>
            {
                var normalizedName = NormalizeSearchQuery(player.DisplayName);
                var compactName = NormalizeCompactHistoryKey(player.DisplayName);
                return normalizedName.Contains(normalizedQuery, StringComparison.Ordinal)
                    || compactName.Contains(compactQuery, StringComparison.Ordinal)
                || tokens.All(token => normalizedName.Contains(token, StringComparison.Ordinal))
                || tokens.All(token => compactName.Contains(NormalizeCompactHistoryKey(token), StringComparison.Ordinal));
            })
            .ToArray();
        var exactNormalizedPlayerIds = matchingPlayers
            .Where(player => NormalizeSearchQuery(player.DisplayName) == normalizedQuery
                || NormalizeCompactHistoryKey(player.DisplayName) == compactQuery)
            .Select(player => player.Id)
            .Distinct()
            .ToArray();
        var matchingPlayerIds = matchingPlayers.Select(player => player.Id).Distinct().ToArray();

        var queryLower = normalizedQuery;
        var externalRefs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Include(reference => reference.Player)
            .ThenInclude(player => player!.League)
            .Where(reference => matchingPlayerIds.Contains(reference.PlayerId)
                || reference.ProviderDisplayName.ToLower().Contains(queryLower)
                || reference.ProviderPlayerId.ToLower().Contains(queryLower))
            .OrderBy(reference => reference.Provider)
            .ThenBy(reference => reference.ProviderDisplayName)
            .Select(reference => new
            {
                reference.PlayerId,
                linkedPlayerName = reference.Player == null ? null : reference.Player.DisplayName,
                linkedPlayerLeague = reference.Player == null || reference.Player.League == null ? null : reference.Player.League.Code,
                reference.Provider,
                reference.ProviderPlayerId,
                reference.ProviderDisplayName
            })
            .ToArrayAsync(cancellationToken);
        var knownMlbRefs = externalRefs
            .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var knownMlbIds = knownMlbRefs
            .Select(reference => reference.ProviderPlayerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id)
            .ToArray();
        var mlbRefPlayerIds = knownMlbRefs.Select(reference => reference.PlayerId).Distinct().ToArray();
        var identityPlayerIds = matchingPlayerIds
            .Concat(mlbRefPlayerIds)
            .Distinct()
            .ToArray();

        var allPropTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(propType => new
            {
                propType.Id,
                propType.Name,
                propType.StatKey,
                NormalizedStatKey = NormalizeCompactHistoryKey(propType.StatKey),
                NormalizedName = NormalizeCompactHistoryKey(propType.Name)
            })
            .ToArrayAsync(cancellationToken);
        var pitcherStrikeoutAliases = BuildPlayerResearchHistoryStatAliases("pitcherstrikeouts");
        var pitcherStrikeoutPropTypeIds = allPropTypes
            .Where(propType => pitcherStrikeoutAliases.Contains(propType.NormalizedStatKey)
                || pitcherStrikeoutAliases.Contains(propType.NormalizedName))
            .Select(propType => propType.Id)
            .Distinct()
            .ToArray();

        var exactSelectedPlayerResultCount = matchingPlayerIds.Length == 0
            ? 0
            : await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(result => matchingPlayerIds.Contains(result.PlayerId), cancellationToken);
        var exactNormalizedNameResultCount = exactNormalizedPlayerIds.Length == 0
            ? 0
            : await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(result => exactNormalizedPlayerIds.Contains(result.PlayerId), cancellationToken);
        var mlbRefResultCount = mlbRefPlayerIds.Length == 0
            ? 0
            : await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(result => mlbRefPlayerIds.Contains(result.PlayerId), cancellationToken);
        var externalIdentifierMlbIdResultCount = 0;
        if (knownMlbIds.Length > 0)
        {
            var externalSourceIdentifiers = await dbContext.PropResults
                .AsNoTracking()
                .Where(result => result.ExternalSource == "mlb")
                .Select(result => result.ExternalSourceIdentifier)
                .ToArrayAsync(cancellationToken);
            externalIdentifierMlbIdResultCount = externalSourceIdentifiers.Count(identifier =>
                knownMlbIds.Any(mlbId => identifier.Contains(mlbId, StringComparison.OrdinalIgnoreCase)));
        }
        var pitcherStrikeoutGlobalCount = pitcherStrikeoutPropTypeIds.Length == 0
            ? 0
            : await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(result => pitcherStrikeoutPropTypeIds.Contains(result.PropTypeId), cancellationToken);
        var pitcherStrikeoutIdentityCount = identityPlayerIds.Length == 0 || pitcherStrikeoutPropTypeIds.Length == 0
            ? 0
            : await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(result => identityPlayerIds.Contains(result.PlayerId)
                    && pitcherStrikeoutPropTypeIds.Contains(result.PropTypeId), cancellationToken);

        var resultCountsByStatKey = identityPlayerIds.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.PropType)
                .Where(result => identityPlayerIds.Contains(result.PlayerId))
                .GroupBy(result => new
                {
                    PropType = result.PropType == null ? "Unknown" : result.PropType.Name,
                    StatKey = result.PropType == null ? "Unknown" : result.PropType.StatKey
                })
                .Select(group => new
                {
                    propType = group.Key.PropType,
                    statKey = group.Key.StatKey,
                    normalizedStatKey = NormalizeCompactHistoryKey(group.Key.StatKey),
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.propType)
                .Take(50)
                .ToArrayAsync(cancellationToken);
        var latestRows = identityPlayerIds.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.Player)
                .ThenInclude(player => player!.League)
                .Include(result => result.PropType)
                .Where(result => identityPlayerIds.Contains(result.PlayerId))
                .OrderByDescending(result => result.EventDate)
                .ThenBy(result => result.Player == null ? string.Empty : result.Player.DisplayName)
                .ThenBy(result => result.PropType == null ? string.Empty : result.PropType.Name)
                .Take(20)
                .Select(result => new
                {
                    propResultId = result.Id,
                    resultDate = result.EventDate,
                    gameDate = result.EventDate,
                    playerId = result.PlayerId,
                    playerName = result.Player == null ? null : result.Player.DisplayName,
                    league = result.Player == null || result.Player.League == null ? null : result.Player.League.Code,
                    statKey = result.PropType == null ? null : result.PropType.StatKey,
                    propType = result.PropType == null ? null : result.PropType.Name,
                    normalizedStatKey = result.PropType == null ? null : NormalizeCompactHistoryKey(result.PropType.StatKey),
                    value = result.ActualResultValue,
                    gameId = result.ExternalSourceIdentifier,
                    source = result.ExternalSource
                })
                .ToArrayAsync(cancellationToken);
        var latestPitcherStrikeoutRows = identityPlayerIds.Length == 0 || pitcherStrikeoutPropTypeIds.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.Player)
                .ThenInclude(player => player!.League)
                .Include(result => result.PropType)
                .Where(result => identityPlayerIds.Contains(result.PlayerId)
                    && pitcherStrikeoutPropTypeIds.Contains(result.PropTypeId))
                .OrderByDescending(result => result.EventDate)
                .ThenBy(result => result.Player == null ? string.Empty : result.Player.DisplayName)
                .Take(20)
                .Select(result => new
                {
                    propResultId = result.Id,
                    resultDate = result.EventDate,
                    gameDate = result.EventDate,
                    playerId = result.PlayerId,
                    playerName = result.Player == null ? null : result.Player.DisplayName,
                    league = result.Player == null || result.Player.League == null ? null : result.Player.League.Code,
                    statKey = result.PropType == null ? null : result.PropType.StatKey,
                    propType = result.PropType == null ? null : result.PropType.Name,
                    normalizedStatKey = result.PropType == null ? null : NormalizeCompactHistoryKey(result.PropType.StatKey),
                    value = result.ActualResultValue,
                    gameId = result.ExternalSourceIdentifier,
                    source = result.ExternalSource
                })
                .ToArrayAsync(cancellationToken);
        var nearNamePlayers = candidatePlayers
            .Where(player => !matchingPlayerIds.Contains(player.Id))
            .Where(player => tokens.Any(token => NormalizeSearchQuery(player.DisplayName).Contains(token, StringComparison.Ordinal))
                || tokens.Any(token => NormalizeCompactHistoryKey(player.DisplayName).Contains(NormalizeCompactHistoryKey(token), StringComparison.Ordinal)))
            .Select(player => new
            {
                playerId = player.Id,
                player.DisplayName,
                player.League,
                team = FirstNonBlank(player.TeamAbbreviation, player.Team),
                player.Position,
                normalizedName = NormalizeSearchQuery(player.DisplayName)
            })
            .Take(20)
            .ToArray();
        var resultCoverage = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.ExternalSource == "mlb")
            .GroupBy(result => result.ExternalSource)
            .Select(group => new
            {
                startDate = group.Min(result => result.EventDate),
                endDate = group.Max(result => result.EventDate),
                rowCount = group.Count(),
                distinctPlayers = group.Select(result => result.PlayerId).Distinct().Count()
            })
            .SingleOrDefaultAsync(cancellationToken);
        var importDateCoverage = await dbContext.MlbResultImportDateStates
            .AsNoTracking()
            .GroupBy(state => state.Status)
            .Select(group => new
            {
                status = group.Key,
                count = group.Count(),
                startDate = group.Min(state => state.EventDate),
                endDate = group.Max(state => state.EventDate)
            })
            .OrderBy(row => row.status)
            .ToArrayAsync(cancellationToken);
        var lastImportState = await dbContext.MlbResultImportStates
            .AsNoTracking()
            .SingleOrDefaultAsync(state => state.Source == "mlb", cancellationToken);
        var rootCause = matchingPlayers.Length == 0
            ? "NoLocalPlayerRowsMatchedQuery"
            : knownMlbRefs.Length == 0
                ? "MissingMlbExternalRefForMatchedPlayerRows"
                : pitcherStrikeoutIdentityCount > 0
                    ? "PitcherStrikeoutResultsAvailableForMatchedIdentity"
                    : pitcherStrikeoutGlobalCount > 0
                        ? "NoPitcherStrikeoutResultsForMatchedIdentity"
                        : "PitcherStrikeoutImportCoverageMissing";

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            callsExternalApis = false,
            readOnly = true,
            query,
            normalizedQuery,
            compactQuery,
            rootCause,
            matchingPlayers = matchingPlayers
                .OrderBy(player => NormalizeSearchQuery(player.DisplayName) == normalizedQuery ? 0 : 1)
                .ThenBy(player => LeaguePriority(player.League))
                .ThenBy(player => player.DisplayName)
                .Select(player => new
                {
                    playerId = player.Id,
                    player.DisplayName,
                    player.League,
                    team = FirstNonBlank(player.TeamAbbreviation, player.Team),
                    player.Position,
                    normalizedName = NormalizeSearchQuery(player.DisplayName),
                    externalRefs = externalRefs.Where(reference => reference.PlayerId == player.Id).ToArray()
                })
                .ToArray(),
            matchingExternalRefs = externalRefs,
            knownMlbIds,
            propResultsCounts = new
            {
                exactSelectedPlayerIds = exactSelectedPlayerResultCount,
                matchingNormalizedPlayerNames = exactNormalizedNameResultCount,
                matchingMlbExternalRefPlayerIds = mlbRefResultCount,
                matchingMlbIdInExternalSourceIdentifier = externalIdentifierMlbIdResultCount,
                pitcherStrikeoutsGlobal = pitcherStrikeoutGlobalCount,
                pitcherStrikeoutsForMatchedIdentities = pitcherStrikeoutIdentityCount
            },
            resultCountsByStatKey,
            latestMatchingResultRows = latestRows,
            latestPitcherStrikeoutRows,
            fuzzyNearNamePlayers = nearNamePlayers,
            importCoverage = new
            {
                propResults = resultCoverage,
                dateStates = importDateCoverage,
                lastSuccessfulImport = lastImportState?.LastSuccessfulImportDate,
                lastSuccessfulRun = lastImportState?.LastSuccessfulRunAt,
                lastStatus = lastImportState?.LastStatus ?? "never_run",
                lastErrorMessage = lastImportState?.LastErrorMessage
            },
            diagnostics = new
            {
                propResultsHaveDirectMlbPlayerIdColumn = false,
                propResultsCanOnlyJoinMlbIdsThroughPlayerExternalRefs = true,
                persistedExternalSourceIdentifierUsesInternalPlayerIdAfterCollapse = true,
                exactBackfillCommand = "$base = \"http://localhost:5299\"; Invoke-RestMethod -Method Post \"$base/api/debug/results/import/mlb-range\" -ContentType \"application/json\" -Body '{\"startDate\":\"YYYY-MM-DD\",\"endDate\":\"YYYY-MM-DD\"}'"
            }
        };
    }

    private static async Task<object> LoadPlayerResearchTableCountsAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        return new
        {
            players = await dbContext.Players.CountAsync(cancellationToken),
            playerExternalRefs = await dbContext.PlayerExternalRefs.CountAsync(cancellationToken),
            platformProps = await dbContext.PlatformProps.CountAsync(cancellationToken),
            currentLineShoppingSnapshots = await dbContext.MarketLineShoppingSnapshots.CountAsync(cancellationToken),
            recommendationSnapshots = await dbContext.RecommendationSnapshots.CountAsync(cancellationToken),
            propResults = await dbContext.PropResults.CountAsync(cancellationToken)
        };
    }

    private static string ResolvePlayerResearchHistoryStatKey(string? propType)
    {
        var normalized = NormalizeCompactHistoryKey(propType);
        return normalized switch
        {
            "pitcherstrikeouts" or "pitcherstrikeout" or "strikeoutspitcher" or "pitcherks" or "ks" => "pitcherstrikeouts",
            "totalbases" or "battertotalbases" => "totalbases",
            "hits" or "batterhits" => "hits",
            "runs" or "batterruns" or "runsscored" => "runs",
            "rbi" or "rbis" or "batterrbi" or "batterrbis" => "rbis",
            "homerun" or "homeruns" or "batterhomerun" or "batterhomeruns" => "homeruns",
            "stolenbase" or "stolenbases" or "batterstolenbase" or "batterstolenbases" => "stolenbases",
            "hitsrunsrbi" or "hitsrunsrbis" or "batterhitsrunsrbi" or "batterhitsrunsrbis" => "hitsrunsrbis",
            "batterstrikeouts" or "batterstrikeout" or "hitterstrikeouts" or "hitterstrikeout" => "batterstrikeouts",
            "walk" or "walks" or "batterwalk" or "batterwalks" or "hitterwalk" or "hitterwalks" => "batterwalks",
            _ => normalized
        };
    }

    private static HashSet<string> BuildPlayerResearchHistoryStatAliases(string expectedStatKey)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(expectedStatKey))
        {
            aliases.Add(expectedStatKey);
        }

        if (string.Equals(expectedStatKey, "pitcherstrikeouts", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("pitcherks");
            aliases.Add("ks");
        }
        else if (string.Equals(expectedStatKey, "batterstrikeouts", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("strikeouts");
            aliases.Add("strikeout");
            aliases.Add("batterstrikeout");
            aliases.Add("hitterstrikeouts");
            aliases.Add("hitterstrikeout");
        }
        else if (string.Equals(expectedStatKey, "batterwalks", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("walks");
            aliases.Add("walk");
            aliases.Add("batterwalk");
            aliases.Add("hitterwalks");
            aliases.Add("hitterwalk");
            aliases.Add("baseonballs");
            aliases.Add("batterbaseonballs");
        }

        return aliases;
    }

    private static string NormalizeCompactHistoryKey(string? value)
    {
        return PlayerNameNormalizer.ToCompactKey(value);
    }

    private static async Task<Guid[]> ResolvePlayerResearchEquivalentPlayerIdsAsync(
        ApplicationDbContext dbContext,
        Player selectedPlayer,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid> { selectedPlayer.Id };
        var aliasRefs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == "mlb-alias")
            .Select(reference => new { reference.PlayerId, reference.ProviderPlayerId })
            .ToArrayAsync(cancellationToken);

        foreach (var aliasRef in aliasRefs)
        {
            if (!TryParseMlbAliasTargetPlayerId(aliasRef.ProviderPlayerId, out var targetPlayerId))
            {
                continue;
            }

            if (aliasRef.PlayerId == selectedPlayer.Id)
            {
                ids.Add(targetPlayerId);
            }
            else if (targetPlayerId == selectedPlayer.Id)
            {
                ids.Add(aliasRef.PlayerId);
            }
        }

        return ids.ToArray();
    }

    private static PlayerResearchMarketLine[] DedupePlayerResearchMarketLines(
        IReadOnlyCollection<PlayerResearchMarketLine> rows,
        string identityGroupKey,
        Guid selectedPlayerId,
        Guid effectivePlayerId)
    {
        return rows
            .GroupBy(row => BuildPlayerResearchMarketLineKey(row, identityGroupKey), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.EntryActionableForUser)
                .ThenBy(row => row.IsExpired)
                .ThenBy(row => row.IsStale)
                .ThenByDescending(row => row.PlayerId == selectedPlayerId)
                .ThenByDescending(row => row.PlayerId == effectivePlayerId)
                .ThenByDescending(row => row.SortObservedAtUtc)
                .ThenByDescending(row => row.BestEstimatedEv ?? decimal.MinValue)
                .ThenBy(row => row.Provider)
                .ThenBy(row => row.BestBookmaker)
                .First())
            .OrderByDescending(row => row.PlayerId == row.CanonicalPlayerId)
            .ThenByDescending(row => row.BestEstimatedEv ?? decimal.MinValue)
            .ThenBy(row => row.Player)
            .ThenBy(row => row.NormalizedPropType)
            .ThenBy(row => row.Provider)
            .ToArray();
    }

    private static string BuildPlayerResearchIdentityGroupKey(IEnumerable<Guid> playerIds)
    {
        return string.Join(
            ",",
            playerIds
                .Distinct()
                .OrderBy(id => id)
                .Select(id => id.ToString("N")));
    }

    private static string BuildPlayerResearchMarketLineKey(PlayerResearchMarketLine row, string identityGroupKey)
    {
        var eventStartKey = row.EventStartTimeUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var eventKey = FirstNonBlank(row.LogicalEventKey, row.EventId, eventStartKey);
        var providerKey = NormalizeCompactHistoryKey(FirstNonBlank(row.BestBookmaker, row.Provider));
        var propKey = NormalizeCompactHistoryKey(row.NormalizedPropType);
        var sideKey = NormalizeCompactHistoryKey(row.BestSide);
        var lineKey = row.Line.ToString("0.####", CultureInfo.InvariantCulture);
        return string.Join("|", identityGroupKey, eventKey, providerKey, propKey, sideKey, lineKey);
    }

    private static PlayerResearchMarketLine ToPlayerResearchMarketLine(MarketLineShoppingSnapshot row, Guid canonicalPlayerId)
    {
        return new PlayerResearchMarketLine(
            row.Sport,
            row.EventId ?? row.Id.ToString("N"),
            row.EventStartTime,
            SanitizePlayerResearchEventText(row.EventName),
            HomeTeam: null,
            PlayerId: canonicalPlayerId,
            CanonicalPlayerId: canonicalPlayerId,
            row.Player,
            row.PropType,
            row.Line,
            row.BestSide,
            row.ProviderCategory,
            row.Provider ?? "Unknown provider",
            row.BestBookmaker ?? row.Bookmaker ?? row.Provider ?? "Unknown provider",
            row.BestAmericanPrice,
            row.BestDecimalPrice,
            row.BestProbability,
            row.BestImpliedProbability,
            row.BestProbabilityEdge,
            row.BestEstimatedEv,
            row.StrategyClassification,
            row.StrategyTier,
            row.IsCandidate,
            row.IsWatchlist,
            row.IsAvoid,
            row.IsResearchOnly,
            row.EntryProvider,
            row.EntryCompatibilityGroup,
            row.EntryProviderCategory,
            row.EntryActionableForUser,
            row.LifecycleStatus,
            row.IsStale,
            row.IsExpired,
            SortObservedAtUtc: row.GeneratedAtUtc,
            LogicalEventKey: row.EventId);
    }

    private static PlayerResearchMarketLine ToPlayerResearchMarketLine(
        Guid lineId,
        Guid playerId,
        string player,
        string sport,
        string provider,
        string propType,
        decimal line,
        string side,
        DateTimeOffset eventStartTime,
        string status,
        DateTimeOffset observedAt,
        string? awayTeam = null,
        string? homeTeam = null)
    {
        var isActive = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
        return new PlayerResearchMarketLine(
            sport,
            lineId.ToString("N"),
            eventStartTime,
            AwayTeam: SanitizePlayerResearchEventText(awayTeam),
            HomeTeam: SanitizePlayerResearchEventText(homeTeam),
            playerId,
            CanonicalPlayerId: playerId,
            player,
            NormalizedPropType: propType,
            line,
            BestSide: side,
            ProviderCategory: "DFS",
            provider,
            BestBookmaker: provider,
            BestAmericanPrice: null,
            BestDecimalPrice: null,
            BestProbability: null,
            BestImpliedProbability: null,
            BestProbabilityEdge: null,
            BestEstimatedEv: null,
            StrategyClassification: "Research",
            StrategyTier: "Research",
            IsCandidate: false,
            IsWatchlist: false,
            IsAvoid: false,
            IsResearchOnly: true,
            EntryProvider: provider,
            EntryCompatibilityGroup: provider.Replace(" ", string.Empty, StringComparison.Ordinal),
            EntryProviderCategory: "DFS",
            EntryActionableForUser: isActive,
            LifecycleStatus: isActive ? "Active" : status,
            IsStale: false,
            IsExpired: !isActive,
            SortObservedAtUtc: observedAt,
            LogicalEventKey: eventStartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    private static string? SanitizePlayerResearchEventText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return IsPlayerResearchPlaceholderEventText(trimmed)
            ? null
            : trimmed;
    }

    private static bool IsPlayerResearchPlaceholderEventText(string value)
    {
        return string.Equals(value, "Current provider line", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Upcoming event", StringComparison.OrdinalIgnoreCase);
    }

    private static string SelectPlatformPropSide(bool overAvailable, bool underAvailable)
    {
        if (!overAvailable && underAvailable)
        {
            return "Under";
        }

        return "Over";
    }

    private static string NormalizeSearchQuery(string? query)
    {
        return PlayerNameNormalizer.ToSearchText(query);
    }

    private static string[] Tokenize(string normalizedQuery)
    {
        return PlayerNameNormalizer.Tokenize(normalizedQuery);
    }

    private static int RankCandidate(string normalizedDisplayName, string[] queryTokens, string normalizedQuery)
    {
        if (normalizedDisplayName == normalizedQuery)
        {
            return 0;
        }

        var displayTokens = Tokenize(normalizedDisplayName);
        var allTokensMatch = queryTokens.All(token => displayTokens.Any(displayToken => displayToken == token || displayToken.StartsWith(token, StringComparison.Ordinal)));
        if (allTokensMatch)
        {
            return 1;
        }

        allTokensMatch = queryTokens.All(token => normalizedDisplayName.Contains(token, StringComparison.Ordinal));
        if (allTokensMatch)
        {
            return 2;
        }

        if (normalizedDisplayName.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            return 3;
        }

        if (normalizedDisplayName.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return 4;
        }

        return queryTokens.Any(token => normalizedDisplayName.Contains(token, StringComparison.Ordinal)) ? 5 : 100;
    }

    private static int LeaguePriority(string? league)
    {
        return league?.ToUpperInvariant() switch
        {
            "MLB" => 0,
            "MLBLIVE" => 1,
            _ => 2
        };
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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

        if (property.ValueKind == JsonValueKind.String && Guid.TryParse(property.GetString(), out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseMlbAliasTargetPlayerId(string providerPlayerId, out Guid targetPlayerId)
    {
        targetPlayerId = Guid.Empty;
        var markerIndex = providerPlayerId.IndexOf(":target:", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var targetValue = providerPlayerId[(markerIndex + ":target:".Length)..];
        return Guid.TryParseExact(targetValue, "N", out targetPlayerId)
            || Guid.TryParse(targetValue, out targetPlayerId);
    }

}

public sealed record PlayerSearchResult(
    Guid PlayerId,
    Guid? EffectivePlayerId,
    string DisplayName,
    string? League,
    string? Sport,
    string? Team,
    string? Position,
    string? Subtitle);

internal sealed record PlayerResearchLookup(
    DateTimeOffset GeneratedAtUtc,
    bool CallsExternalApis,
    bool ReadOnly,
    PlayerResearchSelectedPlayer? SelectedPlayer,
    IReadOnlyList<PlayerResearchCandidatePlayer> CandidatePlayerIdsConsidered,
    IReadOnlyList<PlayerResearchExternalRef> ExternalRefs,
    Guid? EffectivePlayerId,
    IReadOnlyList<PlayerResearchMarketLine> CurrentLines,
    string? ResolutionMode,
    IReadOnlyList<Guid> ExactNameLineOwnerPlayerIds,
    int RawCurrentLineCount,
    int DuplicateCurrentLineCount,
    string? Reason)
{
    public static PlayerResearchLookup Empty(DateTimeOffset generatedAtUtc, string reason)
    {
        return new PlayerResearchLookup(
            generatedAtUtc,
            CallsExternalApis: false,
            ReadOnly: true,
            SelectedPlayer: null,
            CandidatePlayerIdsConsidered: [],
            ExternalRefs: [],
            EffectivePlayerId: null,
            CurrentLines: [],
            ResolutionMode: null,
            ExactNameLineOwnerPlayerIds: [],
            RawCurrentLineCount: 0,
            DuplicateCurrentLineCount: 0,
            Reason: reason);
    }

    public object ToAudit()
    {
        return new
        {
            generatedAtUtc = GeneratedAtUtc,
            callsExternalApis = CallsExternalApis,
            readOnly = ReadOnly,
            selectedPlayerId = SelectedPlayer?.Id,
            selectedPlayerName = SelectedPlayer?.DisplayName,
            selectedPlayerLeague = SelectedPlayer?.League,
            externalRefs = ExternalRefs,
            equivalentCandidatePlayerIds = CandidatePlayerIdsConsidered,
            currentProviderLineCountsByCandidatePlayerId = CandidatePlayerIdsConsidered.ToDictionary(row => row.Id, row => row.CurrentProviderLineCount),
            propResultHistoryCountsByCandidatePlayerId = CandidatePlayerIdsConsidered.ToDictionary(row => row.Id, row => row.PropResultHistoryCount),
            effectivePlayerId = EffectivePlayerId,
            resolutionMode = ResolutionMode,
            exactNameLineOwnerPlayerIds = ExactNameLineOwnerPlayerIds,
            currentProviderLineCount = CurrentLines.Count,
            rawCurrentProviderLineCount = RawCurrentLineCount,
            duplicateCurrentProviderLineCount = DuplicateCurrentLineCount,
            reason = Reason
        };
    }
}

internal sealed record PlayerResearchMarketLine(
    string Sport,
    string EventId,
    DateTimeOffset? EventStartTimeUtc,
    string? AwayTeam,
    string? HomeTeam,
    Guid PlayerId,
    Guid CanonicalPlayerId,
    string Player,
    string NormalizedPropType,
    decimal Line,
    string BestSide,
    string ProviderCategory,
    string Provider,
    string BestBookmaker,
    int? BestAmericanPrice,
    decimal? BestDecimalPrice,
    decimal? BestProbability,
    decimal? BestImpliedProbability,
    decimal? BestProbabilityEdge,
    decimal? BestEstimatedEv,
    string? StrategyClassification,
    string? StrategyTier,
    bool IsCandidate,
    bool IsWatchlist,
    bool IsAvoid,
    bool IsResearchOnly,
    string? EntryProvider,
    string? EntryCompatibilityGroup,
    string? EntryProviderCategory,
    bool EntryActionableForUser,
    string? LifecycleStatus,
    bool IsStale,
    bool IsExpired,
    [property: JsonIgnore] DateTimeOffset SortObservedAtUtc,
    [property: JsonIgnore] string? LogicalEventKey);

internal sealed record PlayerResearchSelectedPlayer(Guid Id, string DisplayName, string? League);

internal sealed record PlayerResearchCandidatePlayer(
    Guid Id,
    string DisplayName,
    string? League,
    int CurrentProviderLineCount,
    int PropResultHistoryCount);

internal sealed record PlayerResearchExternalRef(
    Guid PlayerId,
    string Provider,
    string ProviderPlayerId,
    string ProviderDisplayName);

internal sealed record PlayerResearchExactNamePlayer(
    Guid Id,
    string DisplayName,
    string? League,
    string? Sport,
    string? Team,
    string? Position);
