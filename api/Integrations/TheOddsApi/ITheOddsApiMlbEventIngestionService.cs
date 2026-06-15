namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface ITheOddsApiMlbEventIngestionService
{
    Task<TheOddsApiMlbEventIngestionResult> IngestEventAsync(
        TheOddsApiMlbEventIngestionRequest request,
        bool dryRun,
        CancellationToken cancellationToken);
}
