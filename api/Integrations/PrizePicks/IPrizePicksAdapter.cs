namespace SportsBetting.Api.Integrations.PrizePicks;

public interface IPrizePicksAdapter
{
    Task<IReadOnlyList<PrizePicksProjectionRecord>> GetActiveProjectionsAsync(CancellationToken cancellationToken);
    Task<PrizePicksFetchResult> FetchActiveProjectionsAsync(CancellationToken cancellationToken);
}
