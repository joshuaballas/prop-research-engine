namespace SportsBetting.Api.Integrations.Underdog;

public sealed record UnderdogFetchResult(
    string Endpoint,
    string RawJson,
    IReadOnlyList<UnderdogProjectionRecord> Projections);
