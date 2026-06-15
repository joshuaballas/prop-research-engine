using SportsBetting.Api.Models;

namespace SportsBetting.Api.Data;

public interface IPropResultRepository
{
    Task<IReadOnlyList<PropResult>> GetLatestAsync(int limit, CancellationToken cancellationToken);
    Task<PropResult?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
