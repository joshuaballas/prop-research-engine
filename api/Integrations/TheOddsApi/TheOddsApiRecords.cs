namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed record TheOddsApiCreditUsage(
    int? RequestsUsed,
    int? RequestsRemaining,
    int? RequestsLast);

public sealed record TheOddsApiEventRecord(
    string? EventId,
    string? SportKey,
    DateTimeOffset? EventStartTime,
    string? HomeTeam,
    string? AwayTeam);

public sealed record TheOddsApiPlayerPropSpikeRecord(
    string DataProvider,
    string? BookmakerKey,
    string? BookmakerTitle,
    string? EventId,
    DateTimeOffset? EventStartTime,
    string? HomeTeam,
    string? AwayTeam,
    string? MarketKey,
    DateTimeOffset? MarketLastUpdate,
    string? OutcomeName,
    string? PlayerName,
    string? Side,
    decimal? Line,
    int? PriceAmerican,
    decimal? PriceDecimal,
    string NormalizedPropTypeCandidate,
    string PlatformPropIdCandidate,
    object MetadataSample);

public sealed record TheOddsApiEventOddsDiagnostics(
    string? ProviderEventId,
    string? HomeTeam,
    string? AwayTeam,
    DateTimeOffset? CommenceTimeUtc,
    bool EventOddsHttpSucceeded,
    int BookmakerCount,
    IReadOnlyList<string> BookmakerKeys,
    int MarketCount,
    IReadOnlyList<string> MarketKeys,
    int OutcomeCount,
    int PlayerOutcomeCount);

public sealed record TheOddsApiPlayerPropsSpikeResult(
    IReadOnlyList<TheOddsApiEventRecord> Events,
    string? SelectedEventId,
    IReadOnlyList<TheOddsApiPlayerPropSpikeRecord> Records,
    IReadOnlyList<TheOddsApiEventOddsDiagnostics> EventOddsDiagnostics,
    TheOddsApiCreditUsage CreditUsage,
    string RequestUrl,
    string RawJson);
