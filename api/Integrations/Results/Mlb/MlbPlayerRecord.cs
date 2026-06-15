namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed record MlbPlayerRecord(
    string MlbPlayerId,
    string DisplayName,
    string? FirstName,
    string? LastName,
    string? TeamId,
    string? TeamName);
