namespace SportsBetting.Api.Integrations.Underdog;

public interface IUnderdogAdapter
{
    Task<IReadOnlyList<UnderdogProjectionRecord>> GetActiveProjectionsAsync(CancellationToken cancellationToken);
    Task<UnderdogFetchResult> FetchActiveProjectionsAsync(CancellationToken cancellationToken);
}
