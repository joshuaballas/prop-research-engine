using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using SportsBetting.Api.Utilities;

namespace SportsBetting.Api.Ingestion.Players;

public sealed class PlayerResolutionService(
    ApplicationDbContext dbContext,
    IPlayerResolutionStats stats,
    ILogger<PlayerResolutionService> logger) : IPlayerResolutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PlayerResolutionCache> LoadCacheAsync(CancellationToken cancellationToken)
    {
        var cache = new PlayerResolutionCache();
        var players = await dbContext.Players
            .Include(player => player.League)
            .Include(player => player.ExternalProviderRefs)
            .ToListAsync(cancellationToken);

        foreach (var player in players)
        {
            foreach (var reference in player.ExternalProviderRefs)
            {
                cache.ByExternalRef[ExternalRefKey(reference.Provider, reference.ProviderPlayerId)] = player;
            }

            foreach (var legacyReference in ReadLegacyReferences(player.ExternalRefs))
            {
                cache.ByLegacyRef[ExternalRefKey(legacyReference.Provider, legacyReference.ProviderPlayerId)] = player;
            }

            var fallbackKey = FallbackKey(player.League?.Code, player.DisplayName);
            if (!string.IsNullOrWhiteSpace(fallbackKey))
            {
                AddFallbackCandidate(cache, fallbackKey, player);
            }
        }

        return cache;
    }

    public async Task<PlayerResolutionCache> LoadCacheForLeagueNamesAsync(
        string leagueCode,
        IReadOnlyCollection<string> displayNames,
        CancellationToken cancellationToken)
    {
        var normalizedLeagueCode = CanonicalLeagueNormalizer.NormalizeCode(leagueCode);
        var normalizedNames = displayNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizePlayerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalizedNames.Count == 0)
        {
            return new PlayerResolutionCache();
        }

        var cache = new PlayerResolutionCache();
        var players = await dbContext.Players
            .Include(player => player.League)
            .Include(player => player.ExternalProviderRefs)
            .Where(player => player.League != null
                && player.League.Code == normalizedLeagueCode)
            .ToListAsync(cancellationToken);

        foreach (var player in players.Where(player => normalizedNames.Contains(NormalizePlayerName(player.DisplayName))))
        {
            foreach (var reference in player.ExternalProviderRefs)
            {
                cache.ByExternalRef[ExternalRefKey(reference.Provider, reference.ProviderPlayerId)] = player;
            }

            foreach (var legacyReference in ReadLegacyReferences(player.ExternalRefs))
            {
                cache.ByLegacyRef[ExternalRefKey(legacyReference.Provider, legacyReference.ProviderPlayerId)] = player;
            }

            var fallbackKey = FallbackKey(player.League?.Code, player.DisplayName);
            if (!string.IsNullOrWhiteSpace(fallbackKey))
            {
                AddFallbackCandidate(cache, fallbackKey, player);
            }
        }

        return cache;
    }

    public PlayerResolutionResult ResolveOrCreate(
        PlayerResolutionCache cache,
        PlayerResolutionRequest request)
    {
        Player? player = null;
        var method = PlayerResolutionMethod.Created;

        if (!string.IsNullOrWhiteSpace(request.ProviderPlayerId)
            && cache.ByExternalRef.TryGetValue(ExternalRefKey(request.Provider, request.ProviderPlayerId), out player))
        {
            method = PlayerResolutionMethod.ExternalRef;
        }

        if (player is null
            && !string.IsNullOrWhiteSpace(request.ProviderPlayerId)
            && cache.ByLegacyRef.TryGetValue(ExternalRefKey(request.Provider, request.ProviderPlayerId), out player))
        {
            method = PlayerResolutionMethod.LegacyRef;
        }

        var fallbackKey = FallbackKey(request.League.Code, request.DisplayName);
        if (player is null
            && !string.IsNullOrWhiteSpace(fallbackKey)
            && cache.ByCanonicalLeagueAndName.TryGetValue(fallbackKey, out player))
        {
            method = PlayerResolutionMethod.Fallback;
        }

        if (player is null)
        {
            player = new Player
            {
                Id = Guid.NewGuid(),
                DisplayName = request.DisplayName,
                FirstName = request.FirstName,
                LastName = request.LastName,
                LeagueId = request.League.Id,
                Position = request.Position,
                ExternalRefs = request.LegacyExternalRefs,
                CreatedAt = request.ObservedAt,
                UpdatedAt = request.ObservedAt
            };

            dbContext.Players.Add(player);
            AddPlayerToCache(cache, player, request);
            EnsureExternalProviderRef(cache, player, request);
            stats.Record(PlayerResolutionMethod.Created);
            stats.RecordCreated(new PlayerCreatedResolutionDiagnostic(
                player.Id,
                player.DisplayName,
                request.League.Code,
                request.Provider,
                request.ProviderPlayerId,
                request.ObservedAt,
                "No player_external_refs, legacy Player.ExternalRefs, or normalized display name + canonical league match found."));
            logger.LogInformation(
                "Player created for provider {Provider} external id {ProviderPlayerId}: {DisplayName} ({LeagueCode}).",
                request.Provider,
                request.ProviderPlayerId,
                request.DisplayName,
                request.League.Code);

            return new PlayerResolutionResult(player, PlayerResolutionMethod.Created, Created: true, Updated: false);
        }

        var updated = ApplyPlayerUpdates(player, request);
        updated |= EnsureLegacyExternalRefs(player, request);
        updated |= EnsureExternalProviderRef(cache, player, request);
        AddPlayerToCache(cache, player, request);
        stats.Record(method);

        logger.LogDebug(
            "Player resolved by {ResolutionMethod} for provider {Provider} external id {ProviderPlayerId}: {DisplayName} -> {PlayerId}.",
            method,
            request.Provider,
            request.ProviderPlayerId,
            request.DisplayName,
            player.Id);

        return new PlayerResolutionResult(player, method, Created: false, updated);
    }

    private bool ApplyPlayerUpdates(Player player, PlayerResolutionRequest request)
    {
        var updated = false;

        if (player.DisplayName != request.DisplayName)
        {
            player.DisplayName = request.DisplayName;
            updated = true;
        }

        updated |= SetIfChanged(player.FirstName, request.FirstName, value => player.FirstName = value);
        updated |= SetIfChanged(player.LastName, request.LastName, value => player.LastName = value);
        updated |= SetIfChanged(player.Position, request.Position, value => player.Position = value);

        if (player.LeagueId != request.League.Id)
        {
            player.LeagueId = request.League.Id;
            updated = true;
        }

        if (updated)
        {
            player.UpdatedAt = request.ObservedAt;
        }

        return updated;
    }

    private bool EnsureExternalProviderRef(
        PlayerResolutionCache cache,
        Player player,
        PlayerResolutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderPlayerId))
        {
            return false;
        }

        var key = ExternalRefKey(request.Provider, request.ProviderPlayerId);
        if (cache.ByExternalRef.ContainsKey(key)
            || player.ExternalProviderRefs.Any(reference =>
                string.Equals(reference.Provider, request.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reference.ProviderPlayerId, request.ProviderPlayerId, StringComparison.OrdinalIgnoreCase)))
        {
            cache.ByExternalRef[key] = player;
            return false;
        }

        var reference = new PlayerExternalRef
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Player = player,
            Provider = request.Provider,
            ProviderPlayerId = request.ProviderPlayerId,
            ProviderDisplayName = request.DisplayName,
            CreatedAt = request.ObservedAt,
            UpdatedAt = request.ObservedAt
        };

        player.ExternalProviderRefs.Add(reference);
        dbContext.PlayerExternalRefs.Add(reference);
        cache.ByExternalRef[key] = player;
        return true;
    }

    private bool EnsureLegacyExternalRefs(Player player, PlayerResolutionRequest request)
    {
        if (request.LegacyExternalRefs is null || string.IsNullOrWhiteSpace(request.ProviderPlayerId))
        {
            return false;
        }

        var currentProviderPlayerId = ReadLegacyReferences(player.ExternalRefs)
            .FirstOrDefault(reference => string.Equals(reference.Provider, request.Provider, StringComparison.OrdinalIgnoreCase))
            ?.ProviderPlayerId;

        if (string.Equals(currentProviderPlayerId, request.ProviderPlayerId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        player.ExternalRefs = MergeLegacyExternalRefs(player.ExternalRefs, request.LegacyExternalRefs);
        player.UpdatedAt = request.ObservedAt;
        return true;
    }

    private static JsonDocument MergeLegacyExternalRefs(JsonDocument? current, JsonDocument incoming)
    {
        var providers = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (current?.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in current.RootElement.EnumerateObject())
            {
                providers[property.Name] = property.Value.Clone();
            }
        }

        if (incoming.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in incoming.RootElement.EnumerateObject())
            {
                providers[property.Name] = property.Value.Clone();
            }
        }

        return JsonSerializer.SerializeToDocument(providers, JsonOptions);
    }

    private static void AddPlayerToCache(
        PlayerResolutionCache cache,
        Player player,
        PlayerResolutionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ProviderPlayerId))
        {
            cache.ByLegacyRef[ExternalRefKey(request.Provider, request.ProviderPlayerId)] = player;
        }

        var fallbackKey = FallbackKey(request.League.Code, player.DisplayName);
        if (!string.IsNullOrWhiteSpace(fallbackKey))
        {
            AddFallbackCandidate(cache, fallbackKey, player);
        }
    }

    private static void AddFallbackCandidate(PlayerResolutionCache cache, string fallbackKey, Player candidate)
    {
        if (!cache.ByCanonicalLeagueAndName.TryGetValue(fallbackKey, out var current))
        {
            cache.ByCanonicalLeagueAndName[fallbackKey] = candidate;
            return;
        }

        if (IsBetterFallbackCandidate(candidate, current))
        {
            cache.ByCanonicalLeagueAndName[fallbackKey] = candidate;
        }
    }

    private static bool IsBetterFallbackCandidate(Player candidate, Player current)
    {
        var candidateRefCount = candidate.ExternalProviderRefs.Count + ReadLegacyReferences(candidate.ExternalRefs).Count;
        var currentRefCount = current.ExternalProviderRefs.Count + ReadLegacyReferences(current.ExternalRefs).Count;

        if (candidateRefCount != currentRefCount)
        {
            return candidateRefCount > currentRefCount;
        }

        if (candidate.CreatedAt != current.CreatedAt)
        {
            return candidate.CreatedAt < current.CreatedAt;
        }

        return candidate.Id.CompareTo(current.Id) < 0;
    }

    private static IReadOnlyList<LegacyPlayerReference> ReadLegacyReferences(JsonDocument? externalRefs)
    {
        if (externalRefs is null || externalRefs.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var references = new List<LegacyPlayerReference>();
        foreach (var providerProperty in externalRefs.RootElement.EnumerateObject())
        {
            if (providerProperty.Value.ValueKind != JsonValueKind.Object
                || !providerProperty.Value.TryGetProperty("player_id", out var playerIdElement))
            {
                continue;
            }

            var playerId = playerIdElement.ValueKind == JsonValueKind.String
                ? playerIdElement.GetString()
                : playerIdElement.GetRawText();

            if (!string.IsNullOrWhiteSpace(playerId))
            {
                references.Add(new LegacyPlayerReference(providerProperty.Name, playerId));
            }
        }

        return references;
    }

    private static string ExternalRefKey(string provider, string providerPlayerId)
    {
        return $"{provider.Trim().ToLowerInvariant()}:{providerPlayerId.Trim()}";
    }

    private static string? FallbackKey(string? leagueCode, string displayName)
    {
        if (string.IsNullOrWhiteSpace(leagueCode) || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        return $"{CanonicalLeagueNormalizer.NormalizeCode(leagueCode)}:{NormalizePlayerName(displayName)}";
    }

    private static string NormalizePlayerName(string displayName)
    {
        return PlayerNameNormalizer.ToCompactKey(displayName).ToUpperInvariant();
    }

    private static bool SetIfChanged(string? currentValue, string? newValue, Action<string?> setter)
    {
        if (currentValue == newValue)
        {
            return false;
        }

        setter(newValue);
        return true;
    }

    private sealed record LegacyPlayerReference(string Provider, string ProviderPlayerId);
}
