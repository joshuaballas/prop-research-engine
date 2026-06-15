namespace SportsBetting.Api.Integrations.Underdog;

public interface IUnderdogIngestionService
{
    Task<UnderdogIngestionResult> IngestAsync(CancellationToken cancellationToken);
}
