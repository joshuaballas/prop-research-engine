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
}
