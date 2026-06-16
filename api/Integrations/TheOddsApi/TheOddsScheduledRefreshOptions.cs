namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsScheduledRefreshOptions
{
    public const string SectionName = "TheOddsScheduledRefresh";

    public bool Enabled { get; set; } = false;
    public int IntervalMinutes { get; set; } = 5;
    public string BookmakerPreset { get; set; } = "dfsOnly";
    public bool IncludeCandidates { get; set; } = true;
    public bool IncludeLineShopping { get; set; } = true;
    public bool IncludeFullDiagnostics { get; set; } = false;
    public int FreshnessMinutes { get; set; } = 30;
    public bool RequireForceLive { get; set; } = true;
    public bool AllowScheduledLiveRefresh { get; set; } = false;

    public TimeSpan GetInterval() => TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes));
}
