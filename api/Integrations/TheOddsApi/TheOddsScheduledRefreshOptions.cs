namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsScheduledRefreshOptions
{
    public const string SectionName = "TheOddsScheduledRefresh";

    public bool Enabled { get; set; } = false;
    public int IntervalMinutes { get; set; } = 15;
    public string BookmakerPreset { get; set; } = "dfsAll";
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

        return CreateDefaultTargets(BookmakerPreset, GetFallbackEstimatedApiCalls(), developmentEnabled: false);
    }

    public static IReadOnlyList<TheOddsScheduledRefreshTargetOptions> CreateDefaultTargets(
        string bookmakerPreset,
        int fallbackEstimatedApiCalls,
        bool developmentEnabled)
    {
        var mlbSportsbookEnabled = developmentEnabled;
        var soccerWorldCupEnabled = developmentEnabled;
        return
        [
            new TheOddsScheduledRefreshTargetOptions
            {
                Enabled = true,
                Key = "mlb-dfs",
                Sport = "MLB",
                Competition = "MLB",
                ProviderCategory = "DFS",
                BookmakerPreset = string.IsNullOrWhiteSpace(bookmakerPreset) ? "dfsAll" : bookmakerPreset,
                ProviderSportKey = "baseball_mlb",
                MaxEvents = null,
                EstimatedApiCalls = Math.Max(1, fallbackEstimatedApiCalls),
                RefreshIntervalMinutes = 15,
                SupportsResults = true,
                SupportsHistory = true
            },
            new TheOddsScheduledRefreshTargetOptions
            {
                Enabled = mlbSportsbookEnabled,
                Key = "mlb-sportsbook",
                Sport = "MLB",
                Competition = "MLB",
                ProviderCategory = "Sportsbook",
                BookmakerPreset = "allSportsbooks",
                ProviderSportKey = "baseball_mlb",
                EstimatedApiCalls = Math.Max(1, fallbackEstimatedApiCalls),
                RefreshIntervalMinutes = 15,
                SupportsResults = true,
                SupportsHistory = true,
                DisabledReason = mlbSportsbookEnabled ? null : "Enable explicitly after sportsbook scheduled cadence is approved.",
                Notes = mlbSportsbookEnabled
                    ? "Development-only scheduled MLB sportsbook context refresh. Sportsbook rows remain non-Entry-Builder."
                    : null
            },
            new TheOddsScheduledRefreshTargetOptions
            {
                Enabled = soccerWorldCupEnabled,
                Key = "soccer-world-cup-sportsbook",
                Sport = "SOCCER",
                Competition = "World Cup",
                ProviderCategory = "Sportsbook",
                BookmakerPreset = "allSportsbooks",
                ProviderSportKey = "soccer_fifa_world_cup",
                Regions = "us",
                MaxEvents = 10,
                EstimatedApiCalls = Math.Max(1, fallbackEstimatedApiCalls),
                RefreshIntervalMinutes = 30,
                SupportsResults = false,
                SupportsHistory = true,
                DisabledReason = soccerWorldCupEnabled ? null : "Enable explicitly after Soccer scheduled execution is approved.",
                Notes = soccerWorldCupEnabled
                    ? "Development-only scheduled Soccer World Cup sportsbook context refresh. Entry Builder and World Cup DFS remain disabled."
                    : null
            },
            new TheOddsScheduledRefreshTargetOptions
            {
                Key = "soccer-epl-sportsbook",
                Sport = "SOCCER",
                Competition = "EPL",
                ProviderCategory = "Sportsbook",
                BookmakerPreset = "soccerSportsbookContext",
                RefreshIntervalMinutes = 30,
                DisabledReason = "The Odds EPL sport key needs verification before scheduled refresh."
            },
            new TheOddsScheduledRefreshTargetOptions
            {
                Key = "soccer-ligue-1-sportsbook",
                Sport = "SOCCER",
                Competition = "Ligue 1",
                ProviderCategory = "Sportsbook",
                BookmakerPreset = "soccerSportsbookContext",
                RefreshIntervalMinutes = 30,
                DisabledReason = "The Odds Ligue 1 sport key needs verification before scheduled refresh."
            },
            new TheOddsScheduledRefreshTargetOptions
            {
                Key = "soccer-bundesliga-sportsbook",
                Sport = "SOCCER",
                Competition = "Bundesliga",
                ProviderCategory = "Sportsbook",
                BookmakerPreset = "soccerSportsbookContext",
                RefreshIntervalMinutes = 30,
                DisabledReason = "The Odds Bundesliga sport key needs verification before scheduled refresh."
            },
            new TheOddsScheduledRefreshTargetOptions
            {
                Key = "soccer-serie-a-sportsbook",
                Sport = "SOCCER",
                Competition = "Serie A",
                ProviderCategory = "Sportsbook",
                BookmakerPreset = "soccerSportsbookContext",
                RefreshIntervalMinutes = 30,
                DisabledReason = "The Odds Serie A sport key needs verification before scheduled refresh."
            },
            new TheOddsScheduledRefreshTargetOptions
            {
                Key = "soccer-la-liga-sportsbook",
                Sport = "SOCCER",
                Competition = "La Liga",
                ProviderCategory = "Sportsbook",
                BookmakerPreset = "soccerSportsbookContext",
                RefreshIntervalMinutes = 30,
                DisabledReason = "The Odds La Liga sport key needs verification before scheduled refresh."
            },
            new TheOddsScheduledRefreshTargetOptions
            {
                Key = "soccer-mls-sportsbook",
                Sport = "SOCCER",
                Competition = "MLS",
                ProviderCategory = "Sportsbook",
                BookmakerPreset = "soccerSportsbookContext",
                RefreshIntervalMinutes = 30,
                DisabledReason = "The Odds MLS sport key needs verification before scheduled refresh."
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
    public int RefreshIntervalMinutes { get; set; } = 30;
    public bool SupportsResults { get; set; } = false;
    public bool SupportsHistory { get; set; } = false;
    public string? DisabledReason { get; set; }
    public string? Notes { get; set; }
    public int Priority { get; set; } = 100;
    public DateTimeOffset? CreatedAtUtc { get; set; }

    public TimeSpan GetRefreshInterval() => TimeSpan.FromMinutes(Math.Clamp(RefreshIntervalMinutes, 1, 1440));

    public string ResolveKey()
    {
        if (!string.IsNullOrWhiteSpace(Key))
        {
            return Key;
        }

        return $"{Sport}-{Competition}-{ProviderCategory}-{BookmakerPreset}".Replace(' ', '-').ToLowerInvariant();
    }
}
