using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Data;

public sealed class PropResultRepository(ApplicationDbContext dbContext) : IPropResultRepository
{
    public async Task<IReadOnlyList<PropResult>> GetLatestAsync(int limit, CancellationToken cancellationToken)
    {
        return await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.Player)
            .Include(result => result.League)
            .Include(result => result.PropType)
            .OrderByDescending(result => result.EventDate)
            .ThenBy(result => result.Player!.DisplayName)
            .ThenBy(result => result.PropType!.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<PropResult?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.Player)
            .Include(result => result.League)
            .Include(result => result.PropType)
            .SingleOrDefaultAsync(result => result.Id == id, cancellationToken);
    }
}
