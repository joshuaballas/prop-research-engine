namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsScheduledRefreshOptions
{
    public const string SectionName = "TheOddsScheduledRefresh";

    public bool Enabled { get; set; } = false;
    public int IntervalMinutes { get; set; } = 15;
    public string BookmakerPreset { get; set; } = "dfsOnly";
    public bool IncludeCandidates { get; set; } = true;
    public bool IncludeLineShopping { get; set; } = true;
    public bool IncludeFullDiagnostics { get; set; } = false;
    public int FreshnessMinutes { get; set; } = 30;
    public bool RequireForceLive { get; set; } = true;
    public bool AllowScheduledLiveRefresh { get; set; } = false;
    public bool AllowEventDiscoveryWhenNoPersistedEvents { get; set; } = true;
    public int FallbackEstimatedApiCalls { get; set; } = 8;
    public int MaxEstimatedCallsPerRun { get; set; } = 0;
    public int PostRefreshReadBufferSeconds { get; set; } = 30;

    public TimeSpan GetInterval() => TimeSpan.FromMinutes(Math.Max(15, IntervalMinutes));
    public int GetFallbackEstimatedApiCalls() => Math.Max(1, FallbackEstimatedApiCalls);
    public int? GetMaxEstimatedCallsPerRun() => MaxEstimatedCallsPerRun > 0 ? MaxEstimatedCallsPerRun : null;
    public TimeSpan GetPostRefreshReadBuffer() => TimeSpan.FromSeconds(Math.Max(0, PostRefreshReadBufferSeconds));
}
