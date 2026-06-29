using System.Text.Json;

namespace SportsBetting.Api.Integrations.Results.Soccer;

public interface IApiSportsFootballClient
{
    Task<ApiSportsFootballDiagnosticResponse> GetFixturesAsync(ApiSportsFootballFixturesRequest request, CancellationToken cancellationToken);
    Task<ApiSportsFootballDiagnosticResponse> GetFixturePlayerStatsAsync(string fixtureId, CancellationToken cancellationToken);
    Task<ApiSportsFootballDiagnosticResponse> GetLeaguesAsync(ApiSportsFootballLeaguesRequest request, CancellationToken cancellationToken);
}

public sealed record ApiSportsFootballFixturesRequest(
    DateOnly? Date,
    string? FixtureId,
    int? League,
    int? Season);

public sealed record ApiSportsFootballLeaguesRequest(
    int? League);

public sealed record ApiSportsFootballDiagnosticResponse(
    JsonDocument Payload,
    int? Results,
    int? RequestsCurrent,
    int? RequestsLimit,
    string? SanitizedUrl);
