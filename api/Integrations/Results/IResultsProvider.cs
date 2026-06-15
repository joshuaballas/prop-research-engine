namespace SportsBetting.Api.Integrations.Results;

public interface IResultsProvider
{
    string SourceName { get; }

    Task<IReadOnlyCollection<ResultRecord>> GetResultsAsync(
        DateOnly eventDate,
        CancellationToken cancellationToken);
}
