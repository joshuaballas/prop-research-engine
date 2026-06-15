namespace SportsBetting.Api.Integrations.Underdog;

public sealed record UnderdogIngestionResult(
    int ProjectionsProcessed,
    int PlayersCreated,
    int PlayersUpdated,
    int PropsCreated,
    int PropsUpdated,
    int SnapshotsCreated,
    TimeSpan IngestionDuration);
