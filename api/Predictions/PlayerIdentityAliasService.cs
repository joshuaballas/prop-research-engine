using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Ingestion;

namespace SportsBetting.Api.Predictions;

public sealed class PlayerIdentityAliasService(ApplicationDbContext dbContext) : IPlayerIdentityAliasService
{
    private const string MlbAliasProvider = "mlb-alias";
    private IReadOnlyDictionary<Guid, Guid[]>? mlbAliasesByPlayerId;

    public async Task<IReadOnlyCollection<Guid>> GetEquivalentPlayerIdsAsync(
        Guid playerId,
        string leagueCode,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(CanonicalLeagueNormalizer.NormalizeCode(leagueCode), "MLB", StringComparison.OrdinalIgnoreCase))
        {
            return [playerId];
        }

        mlbAliasesByPlayerId ??= await LoadMlbAliasesByPlayerIdAsync(cancellationToken);

        return mlbAliasesByPlayerId.TryGetValue(playerId, out var aliases)
            ? aliases
            : [playerId];
    }

    private async Task<IReadOnlyDictionary<Guid, Guid[]>> LoadMlbAliasesByPlayerIdAsync(CancellationToken cancellationToken)
    {
        var players = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)
            .Select(player => new
            {
                player.Id,
                player.DisplayName,
                LeagueCode = player.League == null ? null : player.League.Code
            })
            .ToListAsync(cancellationToken);

        var adjacency = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var player in players)
        {
            EnsureNode(adjacency, player.Id);
        }

        foreach (var group in players
            .Where(player => !string.IsNullOrWhiteSpace(player.DisplayName))
            .GroupBy(player => NormalizePlayerName(player.DisplayName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Where(group => group.Any(player => string.Equals(
                CanonicalLeagueNormalizer.NormalizeCode(player.LeagueCode ?? string.Empty),
                "MLB",
                StringComparison.OrdinalIgnoreCase))))
        {
            var ids = group.Select(player => player.Id).Distinct().ToArray();
            AddCompleteGraph(adjacency, ids);
        }

        var aliasRefs = await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Where(reference => reference.Provider == MlbAliasProvider)
            .Select(reference => new
            {
                reference.PlayerId,
                reference.ProviderPlayerId
            })
            .ToListAsync(cancellationToken);

        foreach (var aliasRef in aliasRefs)
        {
            if (TryParseMlbAliasTargetPlayerId(aliasRef.ProviderPlayerId, out var targetPlayerId))
            {
                EnsureNode(adjacency, aliasRef.PlayerId);
                EnsureNode(adjacency, targetPlayerId);
                adjacency[aliasRef.PlayerId].Add(targetPlayerId);
                adjacency[targetPlayerId].Add(aliasRef.PlayerId);
            }
        }

        return BuildConnectedComponents(adjacency);
    }

    private static void EnsureNode(Dictionary<Guid, HashSet<Guid>> adjacency, Guid playerId)
    {
        if (!adjacency.ContainsKey(playerId))
        {
            adjacency[playerId] = [];
        }
    }

    private static void AddCompleteGraph(Dictionary<Guid, HashSet<Guid>> adjacency, IReadOnlyList<Guid> playerIds)
    {
        foreach (var playerId in playerIds)
        {
            EnsureNode(adjacency, playerId);
        }

        for (var i = 0; i < playerIds.Count; i++)
        {
            for (var j = i + 1; j < playerIds.Count; j++)
            {
                adjacency[playerIds[i]].Add(playerIds[j]);
                adjacency[playerIds[j]].Add(playerIds[i]);
            }
        }
    }

    private static IReadOnlyDictionary<Guid, Guid[]> BuildConnectedComponents(Dictionary<Guid, HashSet<Guid>> adjacency)
    {
        var result = new Dictionary<Guid, Guid[]>();
        var visited = new HashSet<Guid>();

        foreach (var playerId in adjacency.Keys)
        {
            if (!visited.Add(playerId))
            {
                continue;
            }

            var component = new List<Guid>();
            var stack = new Stack<Guid>();
            stack.Push(playerId);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                component.Add(current);

                foreach (var next in adjacency[current])
                {
                    if (visited.Add(next))
                    {
                        stack.Push(next);
                    }
                }
            }

            if (component.Count <= 1)
            {
                continue;
            }

            var aliases = component.Distinct().OrderBy(id => id).ToArray();
            foreach (var id in aliases)
            {
                result[id] = aliases;
            }
        }

        return result;
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

    private static string NormalizePlayerName(string displayName)
    {
        var builder = new System.Text.StringBuilder(displayName.Length);

        foreach (var character in displayName.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
