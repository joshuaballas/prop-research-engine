using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using SportsBetting.Api.Utilities;

namespace SportsBetting.Api.Integrations.Results.Soccer;

public sealed class SoccerIdentityResolutionOptions
{
    public const string SectionName = "SoccerIdentityResolution";

    public bool Enabled { get; set; } = true;
    public bool AutoWriteSafeLinks { get; set; } = true;
    public int MaxWritesPerRun { get; set; } = 100;
}

public interface ISoccerPlayerIdentityResolutionService
{
    Task<SoccerIdentityResolutionResult> ReconcileVisibleAsync(
        SoccerIdentityResolutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record SoccerIdentityResolutionRequest(
    string? Competition,
    bool DryRun,
    bool ForceWrite,
    decimal MinScore,
    int MaxCandidateCount,
    bool OnlyVisible,
    int MaxWrites);

public sealed record SoccerIdentityResolutionResult(
    DateTimeOffset GeneratedAtUtc,
    bool CallsExternalApis,
    bool ReadOnly,
    bool WritesRows,
    string Competition,
    bool DryRun,
    int Evaluated,
    int AlreadyLinked,
    int SafeLinkable,
    int Written,
    int ReviewRequired,
    int Blocked,
    IReadOnlyList<SoccerIdentityResolutionRow> Rows);

public sealed record SoccerIdentityResolutionRow(
    string PlayerName,
    Guid LocalPlayerId,
    Guid CanonicalPlayerId,
    int VisibleRowCount,
    int CandidateCount,
    string? SelectedProviderPlayerId,
    string? SelectedProviderDisplayName,
    decimal? Score,
    string? ConfidenceBucket,
    int SupportedResultRows,
    string Category,
    string Decision,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> BlockReasons,
    Guid? WrittenLinkId);

internal sealed class SoccerPlayerIdentityResolutionService(
    ApplicationDbContext dbContext,
    ILogger<SoccerPlayerIdentityResolutionService> logger) : ISoccerPlayerIdentityResolutionService
{
    private const string ApiSportsFootballResultSource = "api-sports-football";
    private const string ApiSportsFootballPlayerRefProvider = "api-sports-football";
    private const string TheOddsSoccerAliasProviderKey = "the-odds-soccer";
    private static readonly string[] SupportedStatKeys = ["GOALS", "ASSISTS", "SHOTS", "SHOTS_ON_TARGET"];
    private static readonly HashSet<string> AliasParticles = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "da", "do", "dos", "das", "del", "el", "la", "le", "van", "von"
    };

    public async Task<SoccerIdentityResolutionResult> ReconcileVisibleAsync(
        SoccerIdentityResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var competition = string.IsNullOrWhiteSpace(request.Competition) ? "World Cup" : request.Competition.Trim();
        var minScore = Math.Clamp(request.MinScore, 0m, 1m);
        var maxCandidateCount = Math.Clamp(request.MaxCandidateCount, 1, 25);
        var maxWrites = Math.Max(0, request.MaxWrites);
        var visibleRows = await LoadVisibleRowsAsync(competition, cancellationToken);
        var missingGroups = await BuildMissingGroupsAsync(visibleRows, cancellationToken);
        var historicalPlayers = await LoadHistoricalPlayersAsync(cancellationToken);
        var approvedLinksByProvider = await LoadApprovedLinksByProviderAsync(cancellationToken);
        var existingLocalLinks = await LoadExistingLocalLinksAsync(missingGroups, cancellationToken);
        var soccerSport = await dbContext.Sports.AsNoTracking().SingleOrDefaultAsync(sport => sport.Code == "SOCCER", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var rows = new List<SoccerIdentityResolutionRow>();
        var writesRemaining = maxWrites;

        foreach (var group in missingGroups)
        {
            var candidates = historicalPlayers
                .Select(player =>
                {
                    approvedLinksByProvider.TryGetValue(player.ProviderPlayerId, out var providerLinks);
                    providerLinks ??= [];
                    return BuildCandidate(group, player, providerLinks);
                })
                .Where(candidate => candidate.Score >= 0.70m)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.TotalSupportedResultRows)
                .ThenBy(candidate => candidate.ProviderDisplayName)
                .Take(10)
                .ToArray();
            var selected = candidates.FirstOrDefault();
            var localLinks = existingLocalLinks
                .Where(link => link.LocalPlayerId == group.LocalPlayerId || link.LocalPlayerId == group.CanonicalPlayerId)
                .ToArray();
            var decision = Classify(group, selected, candidates, localLinks, minScore, maxCandidateCount);
            Guid? writtenLinkId = null;
            var rowDecision = decision.Category is "SafeAliasToAlreadyApprovedProvider" or "SafeReviewedAliasPattern"
                ? "Linkable"
                : decision.Category == "AlreadyLinked"
                    ? "AlreadyLinked"
                    : "ReviewRequired";

            if (!request.DryRun
                && request.ForceWrite
                && soccerSport is not null
                && rowDecision == "Linkable"
                && writesRemaining > 0)
            {
                var link = new ProviderEntityLink
                {
                    Id = Guid.NewGuid(),
                    SportId = soccerSport.Id,
                    EntityType = "Player",
                    CanonicalEntityId = group.LocalPlayerId,
                    CanonicalEntityKind = "Player",
                    CanonicalEntityName = group.PlayerName,
                    ProviderKey = ApiSportsFootballPlayerRefProvider,
                    ProviderEntityId = selected!.ProviderPlayerId,
                    ProviderEntityName = selected.ProviderDisplayName,
                    ProviderCompetitionId = "soccer_fifa_world_cup",
                    ProviderCompetitionName = competition,
                    Confidence = selected.Score,
                    LinkStatus = "Approved",
                    LinkSource = decision.Category,
                    MetadataJson = JsonSerializer.SerializeToDocument(new
                    {
                        source = "SoccerPlayerIdentityResolutionService",
                        decision.Category,
                        group.PlayerName,
                        group.LocalPlayerId,
                        group.CanonicalPlayerId,
                        selected.ProviderPlayerId,
                        selected.ProviderDisplayName,
                        selected.Score,
                        selected.ConfidenceBucket,
                        selected.MatchReasons,
                        selected.ConflictWarnings,
                        selected.TotalSupportedResultRows,
                        candidateCount = candidates.Length,
                        minScore,
                        maxCandidateCount,
                        publicSoccerGradingEnabled = false,
                        appliedAtUtc = now
                    }),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    ApprovedAtUtc = now,
                    ApprovedBy = "automatic/soccer-identity-resolution"
                };
                dbContext.ProviderEntityLinks.Add(link);
                writtenLinkId = link.Id;
                rowDecision = "Written";
                writesRemaining--;
            }
            else if (!request.DryRun && request.ForceWrite && rowDecision == "Linkable" && writesRemaining <= 0)
            {
                rowDecision = "Blocked";
                decision = decision with { BlockReasons = [.. decision.BlockReasons, "MaxWritesReached"] };
            }

            rows.Add(new SoccerIdentityResolutionRow(
                group.PlayerName,
                group.LocalPlayerId,
                group.CanonicalPlayerId,
                group.VisibleRowCount,
                candidates.Length,
                selected?.ProviderPlayerId,
                selected?.ProviderDisplayName,
                selected?.Score,
                selected?.ConfidenceBucket,
                selected?.TotalSupportedResultRows ?? 0,
                decision.Category,
                rowDecision,
                decision.Reasons,
                decision.BlockReasons,
                writtenLinkId));
        }

        if (!request.DryRun && request.ForceWrite)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var result = new SoccerIdentityResolutionResult(
            DateTimeOffset.UtcNow,
            CallsExternalApis: false,
            ReadOnly: request.DryRun,
            WritesRows: !request.DryRun && request.ForceWrite,
            competition,
            request.DryRun,
            Evaluated: rows.Count,
            AlreadyLinked: rows.Count(row => row.Decision == "AlreadyLinked"),
            SafeLinkable: rows.Count(row => row.Decision is "Linkable" or "Written"),
            Written: rows.Count(row => row.Decision == "Written"),
            ReviewRequired: rows.Count(row => row.Decision == "ReviewRequired"),
            Blocked: rows.Count(row => row.Decision == "Blocked"),
            rows);

        logger.LogInformation(
            "Soccer identity reconciliation completed. Evaluated: {Evaluated}; alreadyLinked: {AlreadyLinked}; safeLinkable: {SafeLinkable}; written: {Written}; reviewRequired: {ReviewRequired}; blocked: {Blocked}; dryRun: {DryRun}",
            result.Evaluated,
            result.AlreadyLinked,
            result.SafeLinkable,
            result.Written,
            result.ReviewRequired,
            result.Blocked,
            request.DryRun);

        return result;
    }

    private async Task<IReadOnlyList<VisibleRow>> LoadVisibleRowsAsync(string competition, CancellationToken cancellationToken)
    {
        var rows = await dbContext.PlatformProps
            .AsNoTracking()
            .Include(platformProp => platformProp.Platform)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.Player)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.PropType)
            .Include(platformProp => platformProp.Prop)!.ThenInclude(prop => prop!.League)!.ThenInclude(league => league!.Sport)
            .Where(platformProp => platformProp.Prop != null
                && platformProp.Prop.Player != null
                && platformProp.Prop.PropType != null
                && platformProp.Prop.League != null
                && platformProp.Prop.League.Sport != null
                && platformProp.Prop.League.Sport.Code == "SOCCER")
            .Select(platformProp => new
            {
                platformProp.ExternalStableKey,
                platformProp.Metadata,
                PlayerId = platformProp.Prop!.PlayerId,
                PlayerName = platformProp.Prop.Player!.DisplayName
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row => new VisibleRow(
                row.PlayerId,
                row.PlayerName,
                NormalizeResultPlayerName(row.PlayerName),
                FirstNonBlank(
                    ExtractMetadataString(row.Metadata, "eventInfo", "providerEventId"),
                    ExtractMetadataString(row.Metadata, "eventInfo", "eventId"),
                    row.ExternalStableKey),
                ExtractMetadataRootString(row.Metadata, "dataProvider"),
                ExtractMetadataRootString(row.Metadata, "contextSchemaVersion"),
                ExtractMetadataString(row.Metadata, "sport", "providerSportKey"),
                FirstNonBlank(
                    ExtractMetadataRootString(row.Metadata, "competition"),
                    ExtractMetadataString(row.Metadata, "sport", "competition"),
                    ExtractMetadataString(row.Metadata, "eventInfo", "competition")),
                FirstNonBlank(
                    ExtractMetadataRootString(row.Metadata, "providerCategory"),
                    ExtractMetadataString(row.Metadata, "entry", "providerCategory"))))
            .Where(row => string.Equals(row.DataProvider, "The Odds API", StringComparison.OrdinalIgnoreCase))
            .Where(row => string.Equals(row.ContextSchemaVersion, "the-odds-provider-context-v2", StringComparison.OrdinalIgnoreCase))
            .Where(row => string.Equals(row.ProviderSportKey, "soccer_fifa_world_cup", StringComparison.OrdinalIgnoreCase))
            .Where(row => string.Equals(row.Competition, competition, StringComparison.OrdinalIgnoreCase))
            .Where(row => string.Equals(row.ProviderCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
            .Where(row => !IsSyntheticEventId(row.EventId))
            .ToArray();
    }

    private async Task<IReadOnlyList<VisibleGroup>> BuildMissingGroupsAsync(
        IReadOnlyList<VisibleRow> visibleRows,
        CancellationToken cancellationToken)
    {
        if (visibleRows.Count == 0)
        {
            return [];
        }

        var aliasIds = visibleRows
            .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
            .Select(row => BuildTheOddsAliasProviderEntityId(row.EventId!, row.NormalizedPlayerName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var aliasLinks = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == TheOddsSoccerAliasProviderKey
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved"
                && link.CanonicalEntityId.HasValue
                && aliasIds.Contains(link.ProviderEntityId))
            .Select(link => new { link.ProviderEntityId, CanonicalPlayerId = link.CanonicalEntityId!.Value })
            .ToArrayAsync(cancellationToken);
        var aliasByProviderEntityId = aliasLinks
            .GroupBy(link => link.ProviderEntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().CanonicalPlayerId, StringComparer.OrdinalIgnoreCase);
        var groupedRows = visibleRows
            .GroupBy(row => new
            {
                row.LocalPlayerId,
                CanonicalPlayerId = ResolveCanonicalPlayerId(row, aliasByProviderEntityId),
                row.PlayerName,
                row.NormalizedPlayerName
            })
            .Select(group => new VisibleGroup(
                group.Key.PlayerName,
                group.Key.NormalizedPlayerName,
                group.Key.LocalPlayerId,
                group.Key.CanonicalPlayerId,
                group.Count()))
            .ToArray();
        var playerIds = groupedRows.SelectMany(group => new[] { group.LocalPlayerId, group.CanonicalPlayerId }).Distinct().ToArray();
        var existingLinks = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == ApiSportsFootballPlayerRefProvider
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved"
                && link.CanonicalEntityId.HasValue
                && playerIds.Contains(link.CanonicalEntityId.Value))
            .Select(link => link.CanonicalEntityId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var existingSet = existingLinks.ToHashSet();

        return groupedRows
            .Where(group => !existingSet.Contains(group.LocalPlayerId) && !existingSet.Contains(group.CanonicalPlayerId))
            .ToArray();
    }

    private async Task<IReadOnlyList<HistoricalPlayer>> LoadHistoricalPlayersAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.Player)
            .Include(result => result.PropType)
            .Where(result => result.ExternalSource == ApiSportsFootballResultSource
                && result.ExternalSourceIdentifier.StartsWith($"{ApiSportsFootballResultSource}:")
                && result.Player != null
                && result.PropType != null
                && SupportedStatKeys.Contains(result.PropType.StatKey))
            .Select(result => new
            {
                result.PlayerId,
                PlayerName = result.Player!.DisplayName,
                result.EventDate,
                result.PropType!.StatKey,
                result.ExternalSourceIdentifier
            })
            .ToArrayAsync(cancellationToken);
        var refs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == ApiSportsFootballPlayerRefProvider)
            .Select(reference => new { reference.ProviderPlayerId, reference.ProviderDisplayName })
            .ToArrayAsync(cancellationToken);
        var refsByProvider = refs
            .GroupBy(reference => reference.ProviderPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().ProviderDisplayName, StringComparer.OrdinalIgnoreCase);

        return rows
            .Select(row => new { Row = row, ProviderPlayerId = TryExtractApiSportsPlayerId(row.ExternalSourceIdentifier) })
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderPlayerId))
            .GroupBy(row => row.ProviderPlayerId!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First().Row;
                var displayName = refsByProvider.GetValueOrDefault(group.Key) ?? first.PlayerName;
                return new HistoricalPlayer(
                    group.Key,
                    displayName,
                    group.Count(),
                    group.Max(row => row.Row.EventDate));
            })
            .ToArray();
    }

    private async Task<Dictionary<string, IReadOnlyList<ApprovedLink>>> LoadApprovedLinksByProviderAsync(CancellationToken cancellationToken)
    {
        var links = await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == ApiSportsFootballPlayerRefProvider
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved"
                && link.CanonicalEntityId.HasValue)
            .Select(link => new ApprovedLink(link.CanonicalEntityId!.Value, link.ProviderEntityId))
            .ToArrayAsync(cancellationToken);

        return links
            .GroupBy(link => link.ProviderPlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ApprovedLink>)group.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<ApprovedLink>> LoadExistingLocalLinksAsync(
        IReadOnlyList<VisibleGroup> groups,
        CancellationToken cancellationToken)
    {
        var playerIds = groups.SelectMany(group => new[] { group.LocalPlayerId, group.CanonicalPlayerId }).Distinct().ToArray();
        return await dbContext.ProviderEntityLinks
            .AsNoTracking()
            .Where(link => link.ProviderKey == ApiSportsFootballPlayerRefProvider
                && link.EntityType == "Player"
                && link.LinkStatus == "Approved"
                && link.CanonicalEntityId.HasValue
                && playerIds.Contains(link.CanonicalEntityId.Value))
            .Select(link => new ApprovedLink(link.CanonicalEntityId!.Value, link.ProviderEntityId))
            .ToArrayAsync(cancellationToken);
    }

    private Candidate BuildCandidate(
        VisibleGroup visible,
        HistoricalPlayer historical,
        IReadOnlyCollection<ApprovedLink> approvedProviderLinks)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();
        var visibleTokens = TokenizeAliasName(visible.PlayerName, removeParticles: true);
        var providerTokens = TokenizeAliasName(historical.ProviderDisplayName, removeParticles: true);
        var visibleCompact = NormalizeAliasCompact(visible.PlayerName, removeParticles: false);
        var providerCompact = NormalizeAliasCompact(historical.ProviderDisplayName, removeParticles: false);
        var visibleNoParticles = NormalizeAliasCompact(visible.PlayerName, removeParticles: true);
        var providerNoParticles = NormalizeAliasCompact(historical.ProviderDisplayName, removeParticles: true);
        var score = 0m;

        if (visibleCompact == providerCompact)
        {
            score = 1m;
            reasons.Add("NormalizedExactMatch");
        }
        else if (visibleNoParticles == providerNoParticles)
        {
            score = 0.98m;
            reasons.Add("ParticleInsensitiveExactMatch");
        }

        if (score < 0.94m && visibleTokens.Count > 1 && providerTokens.Count > 1 && TokensEquivalentIgnoringOrder(visibleTokens, providerTokens))
        {
            score = Math.Max(score, 0.94m);
            reasons.Add("ReversedNameTokenMatch");
        }

        if (score < 0.92m && TokenSubsetMatch(providerTokens, visibleTokens))
        {
            if (providerTokens.Count == 1 && visibleTokens.Count > 1 && TokensEquivalent(providerTokens[0], visibleTokens[^1]))
            {
                score = Math.Max(score, 0.92m);
                reasons.Add("MiddleNameCompression");
            }
            else
            {
                score = Math.Max(score, providerTokens.Count == 1 ? 0.88m : 0.92m);
                reasons.Add("TokenSubsetMatch");
            }
        }

        if (score < 0.90m && FirstAndLastTokenMatch(visibleTokens, providerTokens))
        {
            score = Math.Max(score, 0.90m);
            reasons.Add("FirstAndLastTokenMatch");
        }

        if (score < 0.86m && LastTokenOnlyMatch(visibleTokens, providerTokens))
        {
            score = Math.Max(score, 0.82m);
            reasons.Add("LastTokenOnlyMatch");
        }

        var jaccard = CalculateTokenJaccard(visibleTokens, providerTokens);
        if (jaccard > 0)
        {
            score = Math.Max(score, decimal.Round(jaccard * 0.86m, 2));
            reasons.Add("TokenOverlapJaccard");
        }

        if (historical.TotalSupportedResultRows > 0)
        {
            reasons.Add("SupportedPropResultsExist");
        }

        var confidenceBucket = score >= 0.90m ? "High" : score >= 0.80m ? "Medium" : "Low";
        return new Candidate(
            historical.ProviderPlayerId,
            historical.ProviderDisplayName,
            score,
            confidenceBucket,
            reasons.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            historical.TotalSupportedResultRows,
            historical.LatestResultDate,
            approvedProviderLinks.Count,
            warnings);
    }

    private ResolutionDecision Classify(
        VisibleGroup group,
        Candidate? selected,
        IReadOnlyList<Candidate> candidates,
        IReadOnlyCollection<ApprovedLink> existingLocalLinks,
        decimal minScore,
        int maxCandidateCount)
    {
        if (existingLocalLinks.Count > 0)
        {
            return new ResolutionDecision("AlreadyLinked", ["Existing approved API-Sports link found for local/canonical player."], ["LocalPlayerAlreadyHasApprovedApiSportsProviderLink"]);
        }

        if (selected is null)
        {
            return new ResolutionDecision("ReviewRequired", ["No candidate found."], ["NoCandidate"]);
        }

        var blockReasons = new List<string>();
        if (selected.TotalSupportedResultRows <= 0)
        {
            blockReasons.Add("SelectedCandidateHasNoSupportedPropResults");
        }

        if (selected.Score < minScore)
        {
            blockReasons.Add("SelectedCandidateBelowMinScore");
        }

        if (candidates.Count > maxCandidateCount)
        {
            blockReasons.Add("CandidateCountExceedsMaxCandidateCount");
        }

        if (HasCloseCandidates(candidates, selected, minScore))
        {
            blockReasons.Add("MultipleCloseScoringCandidates");
        }

        if (blockReasons.Count > 0)
        {
            return new ResolutionDecision("ReviewRequired", selected.MatchReasons, blockReasons);
        }

        if (selected.ExistingApprovedAliasCount > 0 && selected.Score >= 0.90m)
        {
            return new ResolutionDecision("SafeAliasToAlreadyApprovedProvider", selected.MatchReasons, []);
        }

        if (selected.Score >= 0.92m && IsStrongReviewedAlias(selected))
        {
            return new ResolutionDecision("SafeReviewedAliasPattern", selected.MatchReasons, []);
        }

        return new ResolutionDecision("ReviewRequired", selected.MatchReasons, ["NoAutomaticSafeRuleMatched"]);
    }

    private static bool IsStrongReviewedAlias(Candidate candidate)
    {
        return candidate.MatchReasons.Any(reason => reason is
            "NormalizedExactMatch" or
            "ParticleInsensitiveExactMatch" or
            "ReversedNameTokenMatch" or
            "MiddleNameCompression" or
            "FirstAndLastTokenMatch" or
            "TokenSubsetMatch");
    }

    private static bool HasCloseCandidates(IReadOnlyList<Candidate> candidates, Candidate selected, decimal minScore)
    {
        return candidates.Any(candidate => candidate.ProviderPlayerId != selected.ProviderPlayerId
            && candidate.Score >= minScore
            && selected.Score - candidate.Score <= 0.03m);
    }

    private static string? TryExtractApiSportsPlayerId(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var parts = identifier.Split(':', StringSplitOptions.TrimEntries);
        return parts.Length >= 4 && string.Equals(parts[0], ApiSportsFootballResultSource, StringComparison.OrdinalIgnoreCase)
            ? parts[2]
            : null;
    }

    private static Guid ResolveCanonicalPlayerId(VisibleRow row, IReadOnlyDictionary<string, Guid> aliasByProviderEntityId)
    {
        if (!string.IsNullOrWhiteSpace(row.EventId)
            && aliasByProviderEntityId.TryGetValue(BuildTheOddsAliasProviderEntityId(row.EventId, row.NormalizedPlayerName), out var canonicalId))
        {
            return canonicalId;
        }

        return row.LocalPlayerId;
    }

    private static string BuildTheOddsAliasProviderEntityId(string providerEventId, string normalizedPlayerName)
    {
        return $"the-odds-soccer:{providerEventId}:{normalizedPlayerName}";
    }

    private static string NormalizeResultPlayerName(string? value)
    {
        return PlayerNameNormalizer.ToCompactKey(value).ToUpperInvariant();
    }

    private static string NormalizeAliasCompact(string? value, bool removeParticles)
    {
        return string.Concat(TokenizeAliasName(value, removeParticles));
    }

    private static IReadOnlyList<string> TokenizeAliasName(string? value, bool removeParticles)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var withoutDiacritics = RemoveDiacritics(value);
        var tokens = Regex.Split(withoutDiacritics.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
        return removeParticles ? tokens.Where(token => !AliasParticles.Contains(token)).ToArray() : tokens;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private static bool TokensEquivalentIgnoringOrder(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count && TokenSubsetMatch(left, right) && TokenSubsetMatch(right, left);
    }

    private static bool TokenSubsetMatch(IReadOnlyList<string> subset, IReadOnlyList<string> superset)
    {
        return subset.Count > 0 && subset.All(token => superset.Any(candidate => TokensEquivalent(token, candidate)));
    }

    private static bool FirstAndLastTokenMatch(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count >= 2
            && right.Count >= 2
            && TokensEquivalent(left[0], right[0])
            && TokensEquivalent(left[^1], right[^1]);
    }

    private static bool LastTokenOnlyMatch(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count > 0 && right.Count > 0 && TokensEquivalent(left[^1], right[^1]);
    }

    private static decimal CalculateTokenJaccard(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0m;
        }

        var matchedRight = new HashSet<int>();
        var intersection = 0;
        foreach (var leftToken in left.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var index = 0; index < right.Count; index++)
            {
                if (matchedRight.Contains(index) || !TokensEquivalent(leftToken, right[index]))
                {
                    continue;
                }

                matchedRight.Add(index);
                intersection++;
                break;
            }
        }

        var union = left.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            + right.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            - intersection;
        return union <= 0 ? 0m : (decimal)intersection / union;
    }

    private static bool TokensEquivalent(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            || (left.Length >= 5 && right.Length >= 5 && CalculateLevenshteinDistance(left, right) <= 1);
    }

    private static int CalculateLevenshteinDistance(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        var distances = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++) distances[i, 0] = i;
        for (var j = 0; j <= right.Length; j++) distances[0, j] = j;
        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[left.Length, right.Length];
    }

    private static bool IsSyntheticEventId(string? eventId)
    {
        return string.IsNullOrWhiteSpace(eventId)
            || eventId.Contains("|balanced", StringComparison.OrdinalIgnoreCase)
            || eventId.Contains("|alternate", StringComparison.OrdinalIgnoreCase)
            || eventId.Contains("SCR_game", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractMetadataRootString(JsonDocument? metadata, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? ExtractMetadataString(JsonDocument? metadata, string objectName, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty(objectName, out var obj)
            || obj.ValueKind != JsonValueKind.Object
            || !obj.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private sealed record VisibleRow(
        Guid LocalPlayerId,
        string PlayerName,
        string NormalizedPlayerName,
        string? EventId,
        string? DataProvider,
        string? ContextSchemaVersion,
        string? ProviderSportKey,
        string? Competition,
        string? ProviderCategory);

    private sealed record VisibleGroup(
        string PlayerName,
        string NormalizedPlayerName,
        Guid LocalPlayerId,
        Guid CanonicalPlayerId,
        int VisibleRowCount);

    private sealed record HistoricalPlayer(
        string ProviderPlayerId,
        string ProviderDisplayName,
        int TotalSupportedResultRows,
        DateOnly LatestResultDate);

    private sealed record ApprovedLink(Guid LocalPlayerId, string ProviderPlayerId);

    private sealed record Candidate(
        string ProviderPlayerId,
        string ProviderDisplayName,
        decimal Score,
        string ConfidenceBucket,
        IReadOnlyList<string> MatchReasons,
        int TotalSupportedResultRows,
        DateOnly LatestResultDate,
        int ExistingApprovedAliasCount,
        IReadOnlyList<string> ConflictWarnings);

    private sealed record ResolutionDecision(
        string Category,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<string> BlockReasons);
}
