namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsIngestionWorkerOptions
{
    public const string SectionName = "TheOddsIngestionWorker";

    public bool Enabled { get; set; } = false;
    public bool DryRun { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public int MaxEventsPerRun { get; set; } = 1;
    public int MaxEstimatedCreditsPerRun { get; set; } = 5;
    public int MaxEstimatedCreditsPerDay { get; set; } = 20;
    public string Bookmakers { get; set; } = "draftkings,fanduel,betmgm";
    public string ExpandedBookmakers { get; set; } = "draftkings,fanduel,betmgm,espnbet,betrivers,fanatics,williamhill_us,betonlineag,bovada,lowvig,mybookieag";
    public bool IncludeDfsBookmakers { get; set; } = false;
    public string DfsRegion { get; set; } = "us_dfs";
    public string DfsBookmakers { get; set; } = "prizepicks,underdog,pick6,betr_us_dfs";
    public string Markets { get; set; } = "batter_hits,batter_total_bases,pitcher_strikeouts";
    public string OnlyGameDates { get; set; } = "Today";

    public TimeSpan GetInterval() => TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes));
}
