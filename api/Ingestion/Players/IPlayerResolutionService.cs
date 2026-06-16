using System.Text.Json;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Ingestion.Players;

public interface IPlayerResolutionService
{
    Task<PlayerResolutionCache> LoadCacheAsync(CancellationToken cancellationToken);

    Task<PlayerResolutionCache> LoadCacheForLeagueNamesAsync(
        string leagueCode,
        IReadOnlyCollection<string> displayNames,
        CancellationToken cancellationToken);

    PlayerResolutionResult ResolveOrCreate(
        PlayerResolutionCache cache,
        PlayerResolutionRequest request);
}

public sealed record PlayerResolutionRequest(
    string Provider,
    string? ProviderPlayerId,
    string DisplayName,
    string? FirstName,
    string? LastName,
    string? Position,
    League League,
    JsonDocument? LegacyExternalRefs,
    DateTimeOffset ObservedAt);

public sealed record PlayerResolutionResult(
    Player Player,
    PlayerResolutionMethod Method,
    bool Created,
    bool Updated);

public enum PlayerResolutionMethod
{
    ExternalRef,
    LegacyRef,
    Fallback,
    Created
}
