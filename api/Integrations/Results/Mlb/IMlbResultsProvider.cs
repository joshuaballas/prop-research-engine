using SportsBetting.Api.Integrations.Results;

namespace SportsBetting.Api.Integrations.Results.Mlb;

public interface IMlbResultsProvider : IResultsProvider
{
    Task<IReadOnlyCollection<MlbResultRecord>> GetMlbResultsAsync(
        DateOnly eventDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<MlbPlayerRecord>> GetPlayersAsync(
        int season,
        CancellationToken cancellationToken);
}
