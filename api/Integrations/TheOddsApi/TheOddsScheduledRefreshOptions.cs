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
    public int ExpectedRefreshDurationSeconds { get; set; } = 60;
    public List<TheOddsScheduledRefreshTargetOptions> Targets { get; set; } = [];

    public TimeSpan GetInterval() => TimeSpan.FromMinutes(Math.Max(15, IntervalMinutes));
    public int GetFallbackEstimatedApiCalls() => Math.Max(1, FallbackEstimatedApiCalls);
    public int? GetMaxEstimatedCallsPerRun() => MaxEstimatedCallsPerRun > 0 ? MaxEstimatedCallsPerRun : null;
    public TimeSpan GetPostRefreshReadBuffer() => TimeSpan.FromSeconds(Math.Max(0, PostRefreshReadBufferSeconds));
    public TimeSpan GetExpectedRefreshDuration() => TimeSpan.FromSeconds(Math.Max(0, ExpectedRefreshDurationSeconds));

    public IReadOnlyList<TheOddsScheduledRefreshTargetOptions> GetConfiguredTargets()
    {
        if (Targets.Count > 0)
        {
            return Targets;
        }

        return
        [
            new TheOddsScheduledRefreshTargetOptions
            {
                Enabled = true,
                Key = "mlb-dfs",
                Sport = "MLB",
                Competition = "MLB",
                ProviderCategory = "DFS",
                BookmakerPreset = BookmakerPreset,
                ProviderSportKey = "baseball_mlb",
                MaxEvents = null,
                EstimatedApiCalls = GetFallbackEstimatedApiCalls(),
                SupportsResults = true,
                SupportsHistory = true
            }
        ];
    }
}

public sealed class TheOddsScheduledRefreshTargetOptions
{
    public bool Enabled { get; set; } = false;
    public string Key { get; set; } = string.Empty;
    public string Sport { get; set; } = "MLB";
    public string Competition { get; set; } = "MLB";
    public string ProviderCategory { get; set; } = "DFS";
    public string BookmakerPreset { get; set; } = "dfsOnly";
    public string? ProviderSportKey { get; set; }
    public int? MaxEvents { get; set; }
    public string? Regions { get; set; }
    public string? Bookmakers { get; set; }
    public string? Markets { get; set; }
    public int? EstimatedApiCalls { get; set; }
    public bool SupportsResults { get; set; } = false;
    public bool SupportsHistory { get; set; } = false;
    public string? DisabledReason { get; set; }
    public string? Notes { get; set; }

    public string ResolveKey()
    {
        if (!string.IsNullOrWhiteSpace(Key))
        {
            return Key;
        }

        return $"{Sport}-{Competition}-{ProviderCategory}-{BookmakerPreset}".Replace(' ', '-').ToLowerInvariant();
    }
}
