using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Data;

public sealed class PlayerExternalRefRepository(ApplicationDbContext dbContext) : IPlayerExternalRefRepository
{
    public async Task<IReadOnlyList<PlayerExternalRef>> GetLatestAsync(int limit, CancellationToken cancellationToken)
    {
        return await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Include(reference => reference.Player)
            .ThenInclude(player => player!.League)
            .OrderBy(reference => reference.Provider)
            .ThenBy(reference => reference.ProviderDisplayName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerExternalRef>> GetByPlayerIdAsync(Guid playerId, CancellationToken cancellationToken)
    {
        return await dbContext.PlayerExternalRefs
            .AsNoTracking()
            .Include(reference => reference.Player)
            .ThenInclude(player => player!.League)
            .Where(reference => reference.PlayerId == playerId)
            .OrderBy(reference => reference.Provider)
            .ThenBy(reference => reference.ProviderDisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, PlayerExternalRef>> GetByProviderAsync(string provider, CancellationToken cancellationToken)
    {
        return await dbContext.PlayerExternalRefs
            .Include(reference => reference.Player)
            .ThenInclude(player => player!.League)
            .Where(reference => reference.Provider == provider)
            .ToDictionaryAsync(reference => reference.ProviderPlayerId, StringComparer.OrdinalIgnoreCase, cancellationToken);
    }

    public Task<PlayerExternalRef?> GetByProviderPlayerIdAsync(string provider, string providerPlayerId, CancellationToken cancellationToken)
    {
        return dbContext.PlayerExternalRefs
            .Include(reference => reference.Player)
            .ThenInclude(player => player!.League)
            .SingleOrDefaultAsync(
                reference => reference.Provider == provider && reference.ProviderPlayerId == providerPlayerId,
                cancellationToken);
    }

    public async Task<PlayerExternalRef> AddAsync(PlayerExternalRef reference, CancellationToken cancellationToken)
    {
        dbContext.PlayerExternalRefs.Add(reference);
        await dbContext.SaveChangesAsync(cancellationToken);

        return reference;
    }
}
