using SportsBetting.Api.Models;

namespace SportsBetting.Api.Data;

public interface IPlayerExternalRefRepository
{
    Task<IReadOnlyList<PlayerExternalRef>> GetLatestAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlayerExternalRef>> GetByPlayerIdAsync(Guid playerId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, PlayerExternalRef>> GetByProviderAsync(string provider, CancellationToken cancellationToken);
    Task<PlayerExternalRef?> GetByProviderPlayerIdAsync(string provider, string providerPlayerId, CancellationToken cancellationToken);
    Task<PlayerExternalRef> AddAsync(PlayerExternalRef reference, CancellationToken cancellationToken);
}
