namespace SportsBetting.Api.Integrations.Results.Soccer;

public sealed class SoccerResultImportWorkerOptions
{
    public const string SectionName = "SoccerResultImportWorker";

    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 60;
    public int MaxFixturesPerRun { get; set; } = 8;
    public int RecentWindowDays { get; set; } = 3;
    public bool ImportCompletedFixturesOnly { get; set; } = true;
    public string Competition { get; set; } = "World Cup";
    public string Provider { get; set; } = "ApiSportsFootball";
}
