namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsApiOptions
{
    public const string SectionName = "TheOddsApi";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.the-odds-api.com";
    public string SportKey { get; set; } = "baseball_mlb";
    public string SoccerWorldCupSportKey { get; set; } = string.Empty;
    public string SoccerWorldCupMarkets { get; set; } = string.Empty;
    public string Regions { get; set; } = "us";
    public string OddsFormat { get; set; } = "american";
    public int MaxConcurrentRequests { get; set; } = 3;
    public string Markets { get; set; } = string.Join(
        ',',
        "batter_hits",
        "batter_runs_scored",
        "batter_rbis",
        "batter_home_runs",
        "batter_total_bases",
        "batter_stolen_bases",
        "batter_strikeouts",
        "pitcher_strikeouts",
        "batter_walks",
        "batter_doubles",
        "batter_singles",
        "batter_hits_runs_rbis",
        "pitcher_walks",
        "pitcher_hits_allowed",
        "pitcher_earned_runs",
        "pitcher_outs");
}
