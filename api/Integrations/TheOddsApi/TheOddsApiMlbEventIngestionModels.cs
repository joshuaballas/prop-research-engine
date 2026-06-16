namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed record TheOddsApiMlbEventIngestionRequest(
    string EventId,
    string? Bookmakers,
    string? Markets,
    string? Region = null,
    string ProviderCategory = "Sportsbook",
    bool IsActionableForUser = false,
    bool IsIndicativeOdds = false);

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
    TheOddsApiCreditUsage CreditUsage,
    TheOddsApiMlbEventIngestionTiming Timing);

public sealed record TheOddsApiMlbEventIngestionTiming(
    long TotalMs,
    long FetchMarketsMs,
    long NormalizeMs,
    long PersistPlatformPropsMs,
    int NormalizedPropsCount,
    long ExistingLookupMs,
    int ExistingRowsMatched,
    int RowsUnchanged,
    int RowsCreated,
    int RowsUpdated,
    int RowsSkipped,
    long SaveChangesMs,
    int DbRoundTripsEstimate);
