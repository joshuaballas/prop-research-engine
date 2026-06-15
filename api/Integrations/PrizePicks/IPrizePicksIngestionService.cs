namespace SportsBetting.Api.Integrations.PrizePicks;

public interface IPrizePicksIngestionService
{
    Task<PrizePicksIngestionResult> IngestAsync(CancellationToken cancellationToken);
}
