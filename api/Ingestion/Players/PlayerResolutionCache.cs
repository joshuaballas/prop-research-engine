using SportsBetting.Api.Models;

namespace SportsBetting.Api.Ingestion.Players;

public sealed class PlayerResolutionCache
{
    public Dictionary<string, Player> ByExternalRef { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Player> ByLegacyRef { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Player> ByCanonicalLeagueAndName { get; } = new(StringComparer.OrdinalIgnoreCase);
}
