namespace SportsBetting.Api.Integrations.Results.Soccer;

public sealed record ApiSportsSoccerCompetitionDescriptor(
    string Key,
    string Competition,
    string DisplayName,
    int LeagueId,
    int Season,
    bool Enabled,
    string? Country,
    string? Type,
    DateOnly? ActiveFromDate,
    DateOnly? ActiveToDate,
    DateOnly? BackfillFromDate,
    DateOnly? BackfillToDate,
    string LeagueCode,
    string ImportSource,
    bool IsWorldCup);
