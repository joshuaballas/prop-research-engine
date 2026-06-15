namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed record TheOddsApiMlbEventIngestionRequest(
    string EventId,
    string? Bookmakers,
    string? Markets);

public sealed record TheOddsApiMlbEventIngestionResult(
    bool Success,
    bool DryRun,
    string EventId,
    DateTimeOffset? EventStartTime,
    string? HomeTeam,
    string? AwayTeam,
    int BookmakersParsed,
    int MarketsParsed,
    int NormalizedRecords,
    int PropsUpserted,
    int PlatformPropsUpserted,
    int SnapshotsCreated,
    int SnapshotsUnchanged,
    int PlayersResolved,
    int PlayersCreated,
    int PropTypesResolved,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    TheOddsApiCreditUsage CreditUsage);
