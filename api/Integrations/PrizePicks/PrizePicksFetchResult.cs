namespace SportsBetting.Api.Integrations.PrizePicks;

public sealed record PrizePicksFetchResult(
    string Endpoint,
    string RawJson,
    IReadOnlyList<PrizePicksProjectionRecord> Projections);
