namespace SportsBetting.Api.Integrations.Results.Soccer;

public sealed class ApiSportsOptions
{
    public const string SectionName = "ApiSports";

    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetries { get; set; } = 0;
    public ApiSportsFootballOptions Football { get; set; } = new();
}

public sealed class ApiSportsFootballOptions
{
    public string BaseUrl { get; set; } = "https://v3.football.api-sports.io";
    public int? WorldCupLeagueId { get; set; }
    public int? Season { get; set; }
    public List<ApiSportsFootballHistoricalCompetitionOptions> HistoricalCompetitions { get; set; } =
    [
        new() { Key = "EPL", DisplayName = "EPL", LeagueId = 39, Season = 2025, Enabled = true, Country = "England", Type = "League" },
        new() { Key = "LA_LIGA", DisplayName = "La Liga", LeagueId = 140, Season = 2025, Enabled = true, Country = "Spain", Type = "League" },
        new() { Key = "BUNDESLIGA", DisplayName = "Bundesliga", LeagueId = 78, Season = 2025, Enabled = true, Country = "Germany", Type = "League" },
        new() { Key = "SERIE_A", DisplayName = "Serie A", LeagueId = 135, Season = 2025, Enabled = true, Country = "Italy", Type = "League" },
        new() { Key = "LIGUE_1", DisplayName = "Ligue 1", LeagueId = 61, Season = 2025, Enabled = true, Country = "France", Type = "League" },
        new() { Key = "MLS", DisplayName = "MLS", LeagueId = 253, Season = 2026, Enabled = true, Country = "USA", Type = "League" },
        new() { Key = "CHAMPIONS_LEAGUE", DisplayName = "Champions League", LeagueId = 2, Season = 2025, Enabled = true, Country = "Europe", Type = "Cup" }
    ];
}

public sealed class ApiSportsFootballHistoricalCompetitionOptions
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int LeagueId { get; set; }
    public int Season { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Country { get; set; }
    public string? Type { get; set; }
}
