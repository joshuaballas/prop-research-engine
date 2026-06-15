namespace SportsBetting.Api.Integrations.PrizePicks;

public sealed record PrizePicksIngestionResult(
    int ProjectionsProcessed,
    int PlayersCreated,
    int PlayersUpdated,
    int PropsCreated,
    int PropsUpdated,
    int SnapshotsCreated,
    TimeSpan IngestionDuration);
