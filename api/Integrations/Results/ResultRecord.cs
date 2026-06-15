namespace SportsBetting.Api.Integrations.Results;

public sealed record ResultRecord(
    Guid PlayerId,
    Guid LeagueId,
    Guid PropTypeId,
    DateOnly EventDate,
    decimal ActualResultValue,
    string ExternalSourceIdentifier);
