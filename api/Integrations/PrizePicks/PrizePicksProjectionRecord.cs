namespace SportsBetting.Api.Integrations.PrizePicks;

public sealed record PrizePicksProjectionRecord(
    string ProjectionId,
    string? StableKey,
    string? PlayerId,
    string PlayerName,
    string? Team,
    string? TeamName,
    string? League,
    string? LeagueId,
    string? Position,
    decimal? Line,
    decimal? PromoLine,
    string StatType,
    string? StatDisplayName,
    string? Description,
    string? Status,
    string? OddsType,
    bool IsLive,
    bool IsPromo,
    DateTimeOffset? StartTime,
    DateTimeOffset? UpdatedAt);

public sealed record PrizePicksDebugResponse(
    int ProjectionCount,
    IReadOnlyList<string> PlayerNames,
    IReadOnlyList<decimal> LineValues,
    IReadOnlyList<string> StatTypes,
    IReadOnlyList<PrizePicksProjectionRecord> SampleRecords);
