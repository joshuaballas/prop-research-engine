namespace SportsBetting.Api.Configuration;

public static class AppConfigCatalog
{
    public const string EnvironmentGlobal = "Global";
    public const string LegacyEnvironmentAll = "All";

    public static IReadOnlyList<AppConfigDefinition> Definitions { get; } =
    [
        Secret("ConnectionStrings.SportsBettingDb", "ConnectionStrings:SportsBettingDb", "ConnectionStrings", "Database connection strings are bootstrap secrets and must be provided by user-secrets/environment/secret provider.", ["Program.cs"]),
        Secret("TheOdds.ApiKey", "TheOddsApi:ApiKey", "TheOdds", "The Odds API key is a provider credential.", ["TheOddsApiOptions"]),
        Secret("ApiSports.ApiKey", "ApiSports:ApiKey", "ApiSports", "API-Sports key is a provider credential.", ["ApiSportsOptions"]),
        Secret("DebugAdmin.ApiKey", "DebugAdmin:ApiKey", "DebugAdmin", "Debug admin API key is an auth secret.", ["DebugAdminOptions", "DebugAdminProtectionMiddleware"]),

        Bootstrap("Logging.LogLevel", "Logging", AppConfigValueTypes.Json, "Hosting", "Bootstrap logging must be available before database config is loaded.", ["Program.cs"]),
        Bootstrap("AllowedHosts", "AllowedHosts", AppConfigValueTypes.String, "Hosting", "Host filtering is bootstrap host configuration.", ["ASP.NET Core host"]),
        Bootstrap("Underdog.OverUnderLinesUrl", "Underdog:OverUnderLinesUrl", AppConfigValueTypes.String, "ProviderBootstrap", "Provider base endpoint is bootstrap/provider wiring, not a runtime cadence knob.", ["UnderdogOptions"]),
        Bootstrap("MlbResults.BaseUrl", "MlbResults:BaseUrl", AppConfigValueTypes.String, "ProviderBootstrap", "MLB Stats base URL is provider bootstrap wiring.", ["MlbResultsOptions"]),
        Bootstrap("TheOdds.BaseUrl", "TheOddsApi:BaseUrl", AppConfigValueTypes.String, "ProviderBootstrap", "The Odds base URL is provider bootstrap wiring.", ["TheOddsApiOptions"]),
        Bootstrap("ApiSports.Football.BaseUrl", "ApiSports:Football:BaseUrl", AppConfigValueTypes.String, "ProviderBootstrap", "API-Sports base URL is provider bootstrap wiring.", ["ApiSportsOptions"]),
        Domain("ApiSports.Football.HistoricalCompetitions", "ApiSports:Football:HistoricalCompetitions", AppConfigValueTypes.Json, "Soccer Provider Data", "Supported API-Sports Football competitions are structured domain data and should move to a dedicated competition table/editor, not AppConfigs.", ["ApiSportsOptions", "ApiSportsSoccerSeasonResolver"]),

        Move("ApiSports.Enabled", "ApiSports:Enabled", AppConfigValueTypes.Boolean, "ApiSports", "Enables guarded API-Sports debug/import workflows.", "false", ["ApiSportsOptions"]),
        Move("ApiSports.TimeoutSeconds", "ApiSports:TimeoutSeconds", AppConfigValueTypes.Integer, "ApiSports", "HTTP timeout for API-Sports calls.", "60", ["ApiSportsFootballClient"]),
        Move("ApiSports.MaxRetries", "ApiSports:MaxRetries", AppConfigValueTypes.Integer, "ApiSports", "Retry count for API-Sports calls.", "0", ["ApiSportsFootballClient"]),
        Move("Soccer.Results.ApiSports.SeasonMetadataCacheHours", "ApiSports:Football:SeasonMetadataCacheHours", AppConfigValueTypes.Integer, "SoccerResults", "Low-frequency cache TTL for API-Sports league season metadata.", "24", ["ApiSportsSoccerSeasonResolver"]),

        Move("TheOdds.Api.SportKey", "TheOddsApi:SportKey", AppConfigValueTypes.String, "TheOdds", "Default The Odds sport key.", "baseball_mlb", ["TheOddsApiOptions"]),
        Move("TheOdds.Api.Regions", "TheOddsApi:Regions", AppConfigValueTypes.String, "TheOdds", "Default The Odds region list.", "us", ["TheOddsApiOptions"]),
        Move("TheOdds.Api.OddsFormat", "TheOddsApi:OddsFormat", AppConfigValueTypes.String, "TheOdds", "The Odds odds format.", "american", ["TheOddsApiOptions"]),
        Move("TheOdds.Api.MaxConcurrentRequests", "TheOddsApi:MaxConcurrentRequests", AppConfigValueTypes.Integer, "TheOdds", "Concurrency cap for The Odds provider calls.", "3", ["TheOddsApiOptions"]),
        Domain("TheOdds.Api.Markets", "TheOddsApi:Markets", AppConfigValueTypes.String, "The Odds Provider Data", "Supported The Odds MLB market keys are provider domain data and should move to a dedicated market table/editor.", ["TheOddsApiOptions"]),
        Domain("TheOdds.Api.SoccerWorldCupSportKey", "TheOddsApi:SoccerWorldCupSportKey", AppConfigValueTypes.String, "The Odds Provider Data", "The Odds Soccer World Cup sport key is provider domain identity and should move with supported provider targets.", ["TheOddsApiOptions"]),
        Domain("TheOdds.Api.SoccerWorldCupMarkets", "TheOddsApi:SoccerWorldCupMarkets", AppConfigValueTypes.String, "The Odds Provider Data", "Supported The Odds Soccer World Cup market keys are provider domain data and should move to a dedicated market table/editor.", ["TheOddsApiOptions"]),
        Move("TheOdds.Api.SoccerWorldCupSportsbookRegion", "TheOddsApi:SoccerWorldCupSportsbookRegion", AppConfigValueTypes.String, "TheOdds", "The Odds sportsbook region for Soccer World Cup.", "us", ["TheOddsApiOptions"]),
        Move("TheOdds.Api.SoccerWorldCupDfsRegion", "TheOddsApi:SoccerWorldCupDfsRegion", AppConfigValueTypes.String, "TheOdds", "The Odds DFS region for Soccer World Cup.", "us_dfs", ["TheOddsApiOptions"]),

        Move("TheOdds.IngestionWorker.Enabled", "TheOddsIngestionWorker:Enabled", AppConfigValueTypes.Boolean, "The Odds Legacy Worker", "Enables the older manual The Odds ingestion worker. Keep hidden unless actively debugging that legacy path.", "false", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true, impactLevel: AppConfigImpactLevels.High),
        Move("TheOdds.IngestionWorker.DryRun", "TheOddsIngestionWorker:DryRun", AppConfigValueTypes.Boolean, "The Odds Legacy Worker", "Controls dry-run mode for the older manual The Odds ingestion worker.", "true", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true),
        Move("TheOdds.IngestionWorker.IntervalMinutes", "TheOddsIngestionWorker:IntervalMinutes", AppConfigValueTypes.Integer, "The Odds Legacy Worker", "Cadence, in minutes, for the older manual The Odds ingestion worker when it is enabled.", "60", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true),
        Move("TheOdds.IngestionWorker.MaxEventsPerRun", "TheOddsIngestionWorker:MaxEventsPerRun", AppConfigValueTypes.Integer, "The Odds Legacy Worker", "Maximum events processed by the older manual The Odds ingestion worker per run.", "1", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true),
        Move("TheOdds.IngestionWorker.MaxEstimatedCreditsPerRun", "TheOddsIngestionWorker:MaxEstimatedCreditsPerRun", AppConfigValueTypes.Integer, "The Odds Legacy Worker", "Per-run estimated credit cap for the older manual The Odds ingestion worker.", "5", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true, impactLevel: AppConfigImpactLevels.High),
        Move("TheOdds.IngestionWorker.MaxEstimatedCreditsPerDay", "TheOddsIngestionWorker:MaxEstimatedCreditsPerDay", AppConfigValueTypes.Integer, "The Odds Legacy Worker", "Daily estimated credit cap for the older manual The Odds ingestion worker.", "20", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true, impactLevel: AppConfigImpactLevels.High),
        Domain("TheOdds.IngestionWorker.Bookmakers", "TheOddsIngestionWorker:Bookmakers", AppConfigValueTypes.String, "The Odds Provider Data", "Sportsbook bookmaker keys are provider domain data and should move to a dedicated bookmaker table/editor.", ["TheOddsIngestionWorkerOptions"]),
        Domain("TheOdds.IngestionWorker.ExpandedBookmakers", "TheOddsIngestionWorker:ExpandedBookmakers", AppConfigValueTypes.String, "The Odds Provider Data", "Expanded sportsbook bookmaker keys are provider domain data and should move to a dedicated bookmaker table/editor.", ["TheOddsIngestionWorkerOptions"]),
        Move("TheOdds.IngestionWorker.IncludeDfsBookmakers", "TheOddsIngestionWorker:IncludeDfsBookmakers", AppConfigValueTypes.Boolean, "The Odds Legacy Worker", "Allows the older manual The Odds ingestion worker to include DFS bookmaker keys when enabled.", "false", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true),
        Move("TheOdds.IngestionWorker.DfsRegion", "TheOddsIngestionWorker:DfsRegion", AppConfigValueTypes.String, "The Odds Legacy Worker", "DFS region used by the older manual The Odds ingestion worker.", "us_dfs", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true),
        Domain("TheOdds.IngestionWorker.DfsBookmakers", "TheOddsIngestionWorker:DfsBookmakers", AppConfigValueTypes.String, "The Odds Provider Data", "DFS bookmaker keys are provider domain data and should move to a dedicated bookmaker table/editor.", ["TheOddsIngestionWorkerOptions"]),
        Domain("TheOdds.IngestionWorker.Markets", "TheOddsIngestionWorker:Markets", AppConfigValueTypes.String, "The Odds Provider Data", "Manual ingestion market keys are provider domain data and should move to a dedicated market table/editor.", ["TheOddsIngestionWorkerOptions"]),
        Move("TheOdds.IngestionWorker.OnlyGameDates", "TheOddsIngestionWorker:OnlyGameDates", AppConfigValueTypes.String, "The Odds Legacy Worker", "Date selection mode for the older manual The Odds ingestion worker.", "Today", ["TheOddsIngestionWorkerOptions"], isAdvanced: true, isHidden: true),

        Move("TheOdds.ScheduledRefresh.Enabled", "TheOddsScheduledRefresh:Enabled", AppConfigValueTypes.Boolean, "TheOddsScheduledRefresh", "Enables the scheduled one-clock The Odds refresh worker.", "false", ["TheOddsScheduledRefreshOptions", "TheOddsMlbScheduledRefreshWorker"], developmentDefaultValue: "true"),
        Move("TheOdds.ScheduledRefresh.IntervalMinutes", "TheOddsScheduledRefresh:IntervalMinutes", AppConfigValueTypes.Integer, "TheOddsScheduledRefresh", "Scheduled refresh cadence.", "15", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.BookmakerPreset", "TheOddsScheduledRefresh:BookmakerPreset", AppConfigValueTypes.String, "TheOddsScheduledRefresh", "Default bookmaker preset for fallback scheduled target.", "dfsAll", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.IncludeCandidates", "TheOddsScheduledRefresh:IncludeCandidates", AppConfigValueTypes.Boolean, "TheOddsScheduledRefresh", "Whether scheduled finalization includes candidates.", "true", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.IncludeLineShopping", "TheOddsScheduledRefresh:IncludeLineShopping", AppConfigValueTypes.Boolean, "TheOddsScheduledRefresh", "Whether scheduled finalization includes line shopping.", "true", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.IncludeFullDiagnostics", "TheOddsScheduledRefresh:IncludeFullDiagnostics", AppConfigValueTypes.Boolean, "TheOddsScheduledRefresh", "Whether scheduled refresh stores full diagnostics.", "false", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.FreshnessMinutes", "TheOddsScheduledRefresh:FreshnessMinutes", AppConfigValueTypes.Integer, "TheOddsScheduledRefresh", "Freshness window for scheduled refresh output.", "30", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.RequireForceLive", "TheOddsScheduledRefresh:RequireForceLive", AppConfigValueTypes.Boolean, "TheOddsScheduledRefresh", "Requires explicit force-live intent before scheduled diagnostics can call The Odds.", "true", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.AllowScheduledLiveRefresh", "TheOddsScheduledRefresh:AllowScheduledLiveRefresh", AppConfigValueTypes.Boolean, "TheOddsScheduledRefresh", "Allows scheduled worker to call The Odds.", "false", ["TheOddsScheduledRefreshOptions"], developmentDefaultValue: "true"),
        Move("TheOdds.ScheduledRefresh.AllowEventDiscoveryWhenNoPersistedEvents", "TheOddsScheduledRefresh:AllowEventDiscoveryWhenNoPersistedEvents", AppConfigValueTypes.Boolean, "TheOddsScheduledRefresh", "Allows event discovery when no persisted event cache exists.", "true", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.FallbackEstimatedApiCalls", "TheOddsScheduledRefresh:FallbackEstimatedApiCalls", AppConfigValueTypes.Integer, "TheOddsScheduledRefresh", "Fallback call estimate for scheduled targets.", "8", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.MaxEstimatedCallsPerRun", "TheOddsScheduledRefresh:MaxEstimatedCallsPerRun", AppConfigValueTypes.Integer, "TheOddsScheduledRefresh", "Optional scheduled refresh per-run estimate cap.", "0", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.ExpectedRefreshDurationSeconds", "TheOddsScheduledRefresh:ExpectedRefreshDurationSeconds", AppConfigValueTypes.Integer, "TheOddsScheduledRefresh", "Diagnostic expected refresh duration.", "60", ["TheOddsScheduledRefreshOptions"]),
        Move("TheOdds.ScheduledRefresh.PostRefreshReadBufferSeconds", "TheOddsScheduledRefresh:PostRefreshReadBufferSeconds", AppConfigValueTypes.Integer, "TheOddsScheduledRefresh", "Frontend read buffer after scheduled refresh interval.", "30", ["TheOddsScheduledRefreshOptions"]),
        Domain("TheOdds.ScheduledRefresh.Targets", "TheOddsScheduledRefresh:Targets", AppConfigValueTypes.Json, "The Odds Provider Data", "Scheduled refresh target topology is structured provider/domain data and should move to a dedicated target table/editor.", ["TheOddsScheduledRefreshOptions"]),

        Move("TheOdds.RefreshPlanner.Enabled", "TheOddsRefreshPlanner:Enabled", AppConfigValueTypes.Boolean, "TheOddsRefreshPlanner", "Enables refresh planner decisions.", "true", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.EnforcementEnabled", "TheOddsRefreshPlanner:EnforcementEnabled", AppConfigValueTypes.Boolean, "TheOddsRefreshPlanner", "Enables hard planner enforcement.", "false", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.DefaultBookmakerPreset", "TheOddsRefreshPlanner:DefaultBookmakerPreset", AppConfigValueTypes.String, "TheOddsRefreshPlanner", "Default bookmaker preset for planner estimates.", "dfsOnly", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.PlannerIntervalSeconds", "TheOddsRefreshPlanner:PlannerIntervalSeconds", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Planner evaluation interval.", "60", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.MaxDailyCredits", "TheOddsRefreshPlanner:MaxDailyCredits", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Local daily credit budget.", "0", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.MaxMonthlyCredits", "TheOddsRefreshPlanner:MaxMonthlyCredits", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Local monthly credit budget.", "500", ["TheOddsRefreshPlannerOptions"], developmentDefaultValue: "100000"),
        Move("TheOdds.RefreshPlanner.BlockOnPerRunEstimate", "TheOddsRefreshPlanner:BlockOnPerRunEstimate", AppConfigValueTypes.Boolean, "TheOddsRefreshPlanner", "Blocks when per-run estimate exceeds cap.", "false", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.BlockOnProviderCredits", "TheOddsRefreshPlanner:BlockOnProviderCredits", AppConfigValueTypes.Boolean, "TheOddsRefreshPlanner", "Blocks when provider reported credits are low.", "false", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.BlockOnLocalMonthlyBudget", "TheOddsRefreshPlanner:BlockOnLocalMonthlyBudget", AppConfigValueTypes.Boolean, "TheOddsRefreshPlanner", "Blocks when local monthly budget would be exceeded.", "false", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.BlockOnLocalDailyBudget", "TheOddsRefreshPlanner:BlockOnLocalDailyBudget", AppConfigValueTypes.Boolean, "TheOddsRefreshPlanner", "Blocks when local daily budget would be exceeded.", "false", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.WarnAtDailyPercent", "TheOddsRefreshPlanner:WarnAtDailyPercent", AppConfigValueTypes.Decimal, "TheOddsRefreshPlanner", "Daily budget warning threshold.", "80", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.WarnAtMonthlyPercent", "TheOddsRefreshPlanner:WarnAtMonthlyPercent", AppConfigValueTypes.Decimal, "TheOddsRefreshPlanner", "Monthly budget warning threshold.", "80", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.HardStopAtDailyPercent", "TheOddsRefreshPlanner:HardStopAtDailyPercent", AppConfigValueTypes.Decimal, "TheOddsRefreshPlanner", "Daily budget hard stop threshold.", "100", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.HardStopAtMonthlyPercent", "TheOddsRefreshPlanner:HardStopAtMonthlyPercent", AppConfigValueTypes.Decimal, "TheOddsRefreshPlanner", "Monthly budget hard stop threshold.", "100", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.ProviderRemainingHardStopCredits", "TheOddsRefreshPlanner:ProviderRemainingHardStopCredits", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Provider remaining-credit hard stop.", "5", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.ProviderTelemetryStaleAfterMinutes", "TheOddsRefreshPlanner:ProviderTelemetryStaleAfterMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Provider credit telemetry stale window.", "360", ["TheOddsRefreshPlannerOptions"]),
        Redundant("TheOdds.RefreshPlanner.FarOutMinMinutes", "TheOddsRefreshPlanner:FarOutMinMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Retired lower-bound planner key. No active options class consumes it.", []),
        Move("TheOdds.RefreshPlanner.FarOutMaxMinutes", "TheOddsRefreshPlanner:FarOutMaxMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Far-out cadence upper event-start bound.", "1440", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.FarOutRefreshMinutes", "TheOddsRefreshPlanner:FarOutRefreshMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Far-out refresh cadence.", "60", ["TheOddsRefreshPlannerOptions"]),
        Redundant("TheOdds.RefreshPlanner.SameDayMinMinutes", "TheOddsRefreshPlanner:SameDayMinMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Retired lower-bound planner key. No active options class consumes it.", []),
        Move("TheOdds.RefreshPlanner.SameDayMaxMinutes", "TheOddsRefreshPlanner:SameDayMaxMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Same-day cadence upper event-start bound.", "720", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.SameDayRefreshMinutes", "TheOddsRefreshPlanner:SameDayRefreshMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Same-day refresh cadence.", "30", ["TheOddsRefreshPlannerOptions"]),
        Redundant("TheOdds.RefreshPlanner.PreLockMinMinutes", "TheOddsRefreshPlanner:PreLockMinMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Retired lower-bound planner key. No active options class consumes it.", []),
        Move("TheOdds.RefreshPlanner.PreLockMaxMinutes", "TheOddsRefreshPlanner:PreLockMaxMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Pre-lock cadence upper event-start bound.", "120", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.PreLockRefreshMinutes", "TheOddsRefreshPlanner:PreLockRefreshMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Pre-lock refresh cadence.", "15", ["TheOddsRefreshPlannerOptions"]),
        Redundant("TheOdds.RefreshPlanner.NearLockMinMinutes", "TheOddsRefreshPlanner:NearLockMinMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Retired lower-bound planner key. No active options class consumes it.", []),
        Move("TheOdds.RefreshPlanner.NearLockMaxMinutes", "TheOddsRefreshPlanner:NearLockMaxMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Near-lock cadence upper event-start bound.", "30", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.NearLockRefreshMinutes", "TheOddsRefreshPlanner:NearLockRefreshMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Near-lock refresh cadence.", "5", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.RetryAfterFailureMinutes", "TheOddsRefreshPlanner:RetryAfterFailureMinutes", AppConfigValueTypes.Integer, "TheOddsRefreshPlanner", "Retry cadence after failed refresh.", "1", ["TheOddsRefreshPlannerOptions"]),
        Move("TheOdds.RefreshPlanner.StopAfterEventStart", "TheOddsRefreshPlanner:StopAfterEventStart", AppConfigValueTypes.Boolean, "TheOddsRefreshPlanner", "Stops refresh planning after event start so started events are not refreshed.", "false", ["TheOddsRefreshPlannerOptions"]),

        Move("ProviderLineLifecycle.ActiveLineMaxAgeMinutes", "ProviderLineLifecycle:ActiveLineMaxAgeMinutes", AppConfigValueTypes.Integer, "ProviderLineLifecycle", "Maximum age before a provider line becomes stale.", "15", ["ProviderLineLifecycleOptions"]),
        Move("ProviderLineLifecycle.PulledAfterSuccessfulMissingRefreshes", "ProviderLineLifecycle:PulledAfterSuccessfulMissingRefreshes", AppConfigValueTypes.Integer, "ProviderLineLifecycle", "Missing-refresh count before marking a line pulled.", "2", ["ProviderLineLifecycleOptions"]),
        Move("ProviderLineLifecycle.ExpireAfterHours", "ProviderLineLifecycle:ExpireAfterHours", AppConfigValueTypes.Integer, "ProviderLineLifecycle", "Age after which lifecycle rows expire.", "12", ["ProviderLineLifecycleOptions"]),
        Move("ProviderLineLifecycle.RecordSeenUnchangedEvents", "ProviderLineLifecycle:RecordSeenUnchangedEvents", AppConfigValueTypes.Boolean, "ProviderLineLifecycle", "Whether unchanged sightings create lifecycle events.", "false", ["ProviderLineLifecycleOptions"]),

        Move("Mlb.Results.Worker.Enabled", "MlbResultImportWorker:Enabled", AppConfigValueTypes.Boolean, "MlbResults", "Enables MLB result import worker.", "true", ["MlbResultImportWorkerOptions"], developmentDefaultValue: "true"),
        Move("Mlb.Results.Worker.IntervalMinutes", "MlbResultImportWorker:IntervalMinutes", AppConfigValueTypes.Integer, "MlbResults", "MLB result import worker cadence.", "60", ["MlbResultImportWorkerOptions"]),
        Move("Mlb.Results.Worker.MaxBackfillDatesPerRun", "MlbResultImportWorker:MaxBackfillDatesPerRun", AppConfigValueTypes.Integer, "MlbResults", "MLB result import date cap per run.", "14", ["MlbResultImportWorkerOptions"]),
        Move("Mlb.Results.Worker.RecentWindowDays", "MlbResultImportWorker:RecentWindowDays", AppConfigValueTypes.Integer, "MlbResults", "Recent MLB result import window.", "14", ["MlbResultImportWorkerOptions"]),
        Move("Mlb.Results.Worker.HistoricalBackfillEnabled", "MlbResultImportWorker:HistoricalBackfillEnabled", AppConfigValueTypes.Boolean, "MlbResults", "Allows normal worker historical backfill beyond recent window.", "false", ["MlbResultImportWorkerOptions"]),
        Move("Mlb.Results.Worker.HistoricalNonCompleteAttemptCap", "MlbResultImportWorker:HistoricalNonCompleteAttemptCap", AppConfigValueTypes.Integer, "MlbResults", "Attempt cap for old non-complete MLB dates.", "3", ["MlbResultImportWorkerOptions"]),
        Move("Mlb.Results.Worker.RepairLookbackDays", "MlbResultImportWorker:RepairLookbackDays", AppConfigValueTypes.Integer, "MlbResults", "MLB result repair lookback window.", "7", ["MlbResultImportWorkerOptions"]),
        Move("Mlb.Results.Worker.MinimumCompleteImportedResults", "MlbResultImportWorker:MinimumCompleteImportedResults", AppConfigValueTypes.Integer, "MlbResults", "MLB complete-coverage imported-result threshold.", "1000", ["MlbResultImportWorkerOptions"]),
        Move("Mlb.Results.Worker.MinimumCompleteDistinctPlayers", "MlbResultImportWorker:MinimumCompleteDistinctPlayers", AppConfigValueTypes.Integer, "MlbResults", "MLB complete-coverage distinct-player threshold.", "100", ["MlbResultImportWorkerOptions"]),
        Move("Mlb.Results.Worker.MinimumCompleteDistinctPropTypes", "MlbResultImportWorker:MinimumCompleteDistinctPropTypes", AppConfigValueTypes.Integer, "MlbResults", "MLB complete-coverage prop-type threshold.", "12", ["MlbResultImportWorkerOptions"]),

        Move("Soccer.Results.ApiSports.Worker.Enabled", "SoccerResultImportWorker:Enabled", AppConfigValueTypes.Boolean, "SoccerResults", "Enables Soccer API-Sports result/stat import worker.", "false", ["SoccerResultImportWorkerOptions"], developmentDefaultValue: "true"),
        Move("Soccer.Results.ApiSports.Worker.IntervalMinutes", "SoccerResultImportWorker:IntervalMinutes", AppConfigValueTypes.Integer, "SoccerResults", "Soccer API-Sports result worker cadence.", "60", ["SoccerResultImportWorkerOptions"]),
        Move("Soccer.Results.ApiSports.Worker.MaxFixturesPerRun", "SoccerResultImportWorker:MaxFixturesPerRun", AppConfigValueTypes.Integer, "Soccer Results", "Compatibility fallback fixture cap used only when the newer per-competition and total Soccer caps are unset.", "8", ["SoccerResultImportWorkerOptions"], isAdvanced: true, isHidden: true),
        Move("Soccer.Results.ApiSports.Worker.MaxFixturesPerCompetitionPerRun", "SoccerResultImportWorker:MaxFixturesPerCompetitionPerRun", AppConfigValueTypes.Integer, "SoccerResults", "Per-competition fixture cap per run.", "8", ["SoccerResultImportWorkerOptions"]),
        Move("Soccer.Results.ApiSports.Worker.MaxFixturesTotalPerRun", "SoccerResultImportWorker:MaxFixturesTotalPerRun", AppConfigValueTypes.Integer, "SoccerResults", "Global fixture cap per run.", "40", ["SoccerResultImportWorkerOptions"]),
        Move("Soccer.Results.ApiSports.Worker.RecentWindowDays", "SoccerResultImportWorker:RecentWindowDays", AppConfigValueTypes.Integer, "SoccerResults", "Recent Soccer result import lookback.", "3", ["SoccerResultImportWorkerOptions"]),
        Move("Soccer.Results.ApiSports.Worker.ImportCompletedFixturesOnly", "SoccerResultImportWorker:ImportCompletedFixturesOnly", AppConfigValueTypes.Boolean, "SoccerResults", "Controls completed-only Soccer imports.", "true", ["SoccerResultImportWorkerOptions"]),
        Move("Soccer.Results.ApiSports.Worker.Competition", "SoccerResultImportWorker:Competition", AppConfigValueTypes.String, "SoccerResults", "Soccer result worker competition mode.", "AllSupported", ["SoccerResultImportWorkerOptions"]),
        Move("Soccer.Results.ApiSports.Worker.Provider", "SoccerResultImportWorker:Provider", AppConfigValueTypes.String, "SoccerResults", "Soccer result worker provider identifier.", "ApiSportsFootball", ["SoccerResultImportWorkerOptions"]),

        Move("Soccer.IdentityResolution.Enabled", "SoccerIdentityResolution:Enabled", AppConfigValueTypes.Boolean, "SoccerIdentityResolution", "Enables automatic Soccer identity resolution.", "true", ["SoccerIdentityResolutionOptions"]),
        Move("Soccer.IdentityResolution.AutoWriteSafeLinks", "SoccerIdentityResolution:AutoWriteSafeLinks", AppConfigValueTypes.Boolean, "SoccerIdentityResolution", "Allows automatic safe Soccer alias link writes.", "true", ["SoccerIdentityResolutionOptions"]),
        Move("Soccer.IdentityResolution.MaxWritesPerRun", "SoccerIdentityResolution:MaxWritesPerRun", AppConfigValueTypes.Integer, "SoccerIdentityResolution", "Write cap for automatic Soccer identity resolution.", "100", ["SoccerIdentityResolutionOptions"]),

        Move("Soccer.Predictions.Enabled", "SoccerPredictions:Enabled", AppConfigValueTypes.Boolean, "SoccerPredictions", "Enables Soccer prediction support.", "false", ["SoccerPredictionsOptions"], developmentDefaultValue: "true"),
        Move("Soccer.Predictions.MinimumSampleSize", "SoccerPredictions:MinimumSampleSize", AppConfigValueTypes.Integer, "SoccerPredictions", "Minimum sample for Soccer probability generation.", "5", ["SoccerPredictionsOptions"]),
        Move("Soccer.Predictions.LowSampleWarningThreshold", "SoccerPredictions:LowSampleWarningThreshold", AppConfigValueTypes.Integer, "SoccerPredictions", "Soccer low-sample warning threshold.", "10", ["SoccerPredictionsOptions"]),
        Move("Soccer.Predictions.TopPickMinimumSampleSize", "SoccerPredictions:TopPickMinimumSampleSize", AppConfigValueTypes.Integer, "SoccerPredictions", "Soccer Top Pick sample threshold.", "20", ["SoccerPredictionsOptions"]),
        Move("Soccer.Predictions.MinimumTopPickHitProbability", "SoccerPredictions:MinimumTopPickHitProbability", AppConfigValueTypes.Decimal, "SoccerPredictions", "Soccer Top Pick hit-probability threshold.", "0.55", ["SoccerPredictionsOptions"]),
        Move("Soccer.Grading.Enabled", "SoccerGrading:Enabled", AppConfigValueTypes.Boolean, "SoccerGrading", "Enables Soccer grading support.", "false", ["SoccerGradingOptions"], developmentDefaultValue: "true"),
        Move("Soccer.TopPicks.Enabled", "SoccerTopPicks:Enabled", AppConfigValueTypes.Boolean, "SoccerTopPicks", "Enables Soccer Top Picks support.", "false", ["SoccerTopPicksOptions"], developmentDefaultValue: "true"),
        Move("Soccer.TopPicks.MinimumTopPickSampleSize", "SoccerTopPicks:MinimumTopPickSampleSize", AppConfigValueTypes.Integer, "SoccerTopPicks", "Soccer Top Picks sample threshold.", "20", ["SoccerTopPicksOptions"]),
        Move("Soccer.TopPicks.MinimumHitProbability", "SoccerTopPicks:MinimumHitProbability", AppConfigValueTypes.Decimal, "SoccerTopPicks", "Soccer Top Picks hit-probability threshold.", "0.55", ["SoccerTopPicksOptions"]),
        Move("Soccer.TopPicks.AllowNegativeEv", "SoccerTopPicks:AllowNegativeEv", AppConfigValueTypes.Boolean, "SoccerTopPicks", "Allows Soccer Top Picks without positive EV.", "true", ["SoccerTopPicksOptions"]),

        Move("Underdog.Ingestion.Enabled", "UnderdogIngestion:Enabled", AppConfigValueTypes.Boolean, "Direct Underdog Dev Ingestion", "Enables temporary direct Underdog ingestion. This is supplemental/dev-only and should remain hidden from normal operator settings.", "false", ["UnderdogIngestionOptions"], isAdvanced: true, isHidden: true, impactLevel: AppConfigImpactLevels.High),
        Move("Underdog.Ingestion.IntervalMinutes", "UnderdogIngestion:IntervalMinutes", AppConfigValueTypes.Integer, "Direct Underdog Dev Ingestion", "Cadence, in minutes, for temporary direct Underdog ingestion when enabled.", "15", ["UnderdogIngestionOptions"], isAdvanced: true, isHidden: true),

        Needs("DebugAdmin.Enabled", "DebugAdmin:Enabled", AppConfigValueTypes.Boolean, "DebugAdmin", "Security-adjacent debug access setting; keep bootstrap until an admin config workflow exists.", ["DebugAdminOptions"]),
        Needs("DebugAdmin.RequireApiKey", "DebugAdmin:RequireApiKey", AppConfigValueTypes.Boolean, "DebugAdmin", "Security-adjacent debug access setting; keep bootstrap until an admin config workflow exists.", ["DebugAdminOptions"]),
        Model("MlbPredictionGeneration", "MlbPredictionGeneration", "MLB prediction generation thresholds and enabled prop types moved to versioned model config tables.", ["PredictionGenerationOptions"]),
        Model("TopPicksModel", "TopPicksModel", "Top Picks model thresholds and active model era moved to versioned model config tables.", ["TopPicksModelOptions"])
    ];

    public static AppConfigDefinition? FindByBootstrapPath(string key)
    {
        return Definitions.FirstOrDefault(definition =>
            string.Equals(definition.BootstrapPath, key, StringComparison.OrdinalIgnoreCase));
    }

    public static AppConfigDefinition? FindByAppConfigKey(string key)
    {
        return Definitions.FirstOrDefault(definition =>
            string.Equals(definition.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static AppConfigDefinition Move(
        string key,
        string bootstrapPath,
        string valueType,
        string category,
        string description,
        string? defaultValue,
        IReadOnlyList<string> consumers,
        string? developmentDefaultValue = null,
        bool seedToAppConfigs = true,
        bool isAdvanced = false,
        bool isHidden = false,
        string impactLevel = AppConfigImpactLevels.Low)
        => Create(
            key,
            bootstrapPath,
            valueType,
            category,
            description,
            AppConfigClassifications.MoveToAppConfigs,
            "Non-secret operational/runtime setting that can safely use DB-backed override with appsettings fallback.",
            defaultValue,
            DevelopmentDefaultValue: developmentDefaultValue,
            SeedToAppConfigs: seedToAppConfigs,
            IsSensitive: false,
            IsRuntimeEditable: true,
            RequiresRestart: false,
            RuntimeRefreshSafe: true,
            MaskInDiagnostics: false,
            Consumers: consumers,
            IsAdvanced: isAdvanced,
            IsHidden: isHidden,
            ImpactLevel: impactLevel);

    private static AppConfigDefinition Bootstrap(
        string key,
        string bootstrapPath,
        string valueType,
        string category,
        string reason,
        IReadOnlyList<string> consumers)
        => Create(
            key,
            bootstrapPath,
            valueType,
            category,
            reason,
            AppConfigClassifications.KeepInBootstrapConfig,
            reason,
            null,
            SeedToAppConfigs: false,
            IsSensitive: false,
            IsRuntimeEditable: false,
            RequiresRestart: true,
            RuntimeRefreshSafe: false,
            MaskInDiagnostics: false,
            Consumers: consumers,
            InputType: valueType == AppConfigValueTypes.Json ? AppConfigInputTypes.AdvancedText : null,
            ImpactLevel: AppConfigImpactLevels.Medium);

    private static AppConfigDefinition Secret(
        string key,
        string bootstrapPath,
        string category,
        string reason,
        IReadOnlyList<string> consumers)
        => Create(
            key,
            bootstrapPath,
            AppConfigValueTypes.String,
            category,
            reason,
            AppConfigClassifications.KeepInSecretProvider,
            reason,
            null,
            SeedToAppConfigs: false,
            IsSensitive: true,
            IsRuntimeEditable: false,
            RequiresRestart: true,
            RuntimeRefreshSafe: false,
            MaskInDiagnostics: true,
            Consumers: consumers,
            IsHidden: true,
            ImpactLevel: AppConfigImpactLevels.Dangerous);

    private static AppConfigDefinition Redundant(
        string key,
        string bootstrapPath,
        string valueType,
        string category,
        string reason,
        IReadOnlyList<string> consumers)
        => Create(
            key,
            bootstrapPath,
            valueType,
            category,
            reason,
            AppConfigClassifications.RemoveRedundant,
            reason,
            null,
            SeedToAppConfigs: false,
            IsSensitive: false,
            IsRuntimeEditable: false,
            RequiresRestart: false,
            RuntimeRefreshSafe: false,
            MaskInDiagnostics: false,
            Consumers: consumers,
            IsAdvanced: true,
            IsHidden: true,
            ImpactLevel: AppConfigImpactLevels.Low);

    private static AppConfigDefinition Domain(
        string key,
        string bootstrapPath,
        string valueType,
        string category,
        string reason,
        IReadOnlyList<string> consumers)
        => Create(
            key,
            bootstrapPath,
            valueType,
            category,
            reason,
            AppConfigClassifications.MovedToDomainTable,
            $"{reason} Runtime now reads this structured data from normalized provider domain tables with appsettings fallback during migration.",
            null,
            SeedToAppConfigs: false,
            IsSensitive: false,
            IsRuntimeEditable: false,
            RequiresRestart: true,
            RuntimeRefreshSafe: false,
            MaskInDiagnostics: false,
            Consumers: consumers,
            InputType: AppConfigInputTypes.AdvancedText,
            IsAdvanced: true,
            ImpactLevel: AppConfigImpactLevels.Medium);

    private static AppConfigDefinition Needs(
        string key,
        string bootstrapPath,
        string valueType,
        string category,
        string reason,
        IReadOnlyList<string> consumers)
        => Create(
            key,
            bootstrapPath,
            valueType,
            category,
            reason,
            AppConfigClassifications.NeedsDecision,
            reason,
            null,
            SeedToAppConfigs: false,
            IsSensitive: false,
            IsRuntimeEditable: false,
            RequiresRestart: true,
            RuntimeRefreshSafe: false,
            MaskInDiagnostics: false,
            Consumers: consumers,
            IsAdvanced: true,
            ImpactLevel: AppConfigImpactLevels.Medium);

    private static AppConfigDefinition Model(
        string key,
        string bootstrapPath,
        string reason,
        IReadOnlyList<string> consumers)
        => Create(
            key,
            bootstrapPath,
            AppConfigValueTypes.Json,
            "Model",
            reason,
            AppConfigClassifications.MovedToModelConfig,
            $"{reason} Runtime reads these values from model_config_sets/model_config_parameters.",
            null,
            SeedToAppConfigs: false,
            IsSensitive: false,
            IsRuntimeEditable: false,
            RequiresRestart: true,
            RuntimeRefreshSafe: false,
            MaskInDiagnostics: false,
            Consumers: consumers,
            InputType: AppConfigInputTypes.AdvancedText,
            IsAdvanced: true,
            ImpactLevel: AppConfigImpactLevels.Medium);

    private static AppConfigDefinition Create(
        string key,
        string? bootstrapPath,
        string valueType,
        string category,
        string description,
        string classification,
        string reason,
        string? defaultValue,
        string? DevelopmentDefaultValue = null,
        bool SeedToAppConfigs = false,
        bool IsSensitive = false,
        bool IsRuntimeEditable = true,
        bool RequiresRestart = false,
        bool RuntimeRefreshSafe = true,
        bool MaskInDiagnostics = false,
        string? ValidationJson = null,
        IReadOnlyList<string>? Consumers = null,
        string? DisplayName = null,
        string? ShortDisplayName = null,
        string? Section = null,
        string? HelpText = null,
        string? InputType = null,
        string? Unit = null,
        decimal? MinValue = null,
        decimal? MaxValue = null,
        int SortOrder = 1000,
        bool IsAdvanced = false,
        bool IsHidden = false,
        string ImpactLevel = AppConfigImpactLevels.Low,
        bool IsChildAuditItem = false)
    {
        var friendlyCategory = HumanizeCategory(category);
        var inputType = InputType ?? InferInputType(key, valueType);
        var unit = Unit ?? InferUnit(key, valueType);
        var minValue = MinValue ?? InferMinValue(valueType, inputType);
        var maxValue = MaxValue ?? InferMaxValue(key, valueType);

        var displayName = string.IsNullOrWhiteSpace(DisplayName)
            ? BuildDisplayName(key, friendlyCategory, valueType)
            : NormalizeSpaces(DisplayName);
        var shortDisplayName = string.IsNullOrWhiteSpace(ShortDisplayName)
            ? HumanizeKey(key)
            : NormalizeSpaces(ShortDisplayName);

        return new AppConfigDefinition(
            key,
            bootstrapPath,
            valueType,
            friendlyCategory,
            description,
            classification,
            reason,
            defaultValue,
            DevelopmentDefaultValue,
            SeedToAppConfigs,
            IsSensitive,
            IsRuntimeEditable,
            RequiresRestart,
            RuntimeRefreshSafe,
            MaskInDiagnostics,
            ValidationJson,
            Consumers,
            displayName,
            string.IsNullOrWhiteSpace(Section) ? friendlyCategory : NormalizeSpaces(Section),
            string.IsNullOrWhiteSpace(HelpText) ? description : NormalizeSpaces(HelpText),
            inputType,
            unit,
            minValue,
            maxValue,
            SortOrder,
            IsAdvanced,
            IsHidden,
            ImpactLevel,
            IsChildAuditItem,
            shortDisplayName);
    }

    private static string HumanizeCategory(string category)
        => category switch
        {
            "ApiSports" => "API-Sports",
            "TheOdds" => "The Odds",
            "TheOddsScheduledRefresh" => "The Odds Scheduled Refresh",
            "TheOddsRefreshPlanner" => "The Odds Refresh Planner",
            "ProviderLineLifecycle" => "Provider Line Lifecycle",
            "MlbResults" => "MLB Results",
            "SoccerResults" => "Soccer Results",
            "SoccerIdentityResolution" => "Soccer Identity Resolution",
            "SoccerPredictions" => "Soccer Predictions",
            "SoccerGrading" => "Soccer Grading",
            "SoccerTopPicks" => "Soccer Top Picks",
            "ProviderBootstrap" => "Provider Bootstrap",
            "SoccerProviderBootstrap" => "Soccer Provider Bootstrap",
            "DebugAdmin" => "Debug Admin",
            _ => HumanizeKey(category)
        };

    private static string HumanizeKey(string key)
    {
        if (key.Contains(' '))
        {
            return NormalizeSpaces(key);
        }

        var last = key.Split('.', ':').LastOrDefault() ?? key;
        var chars = new List<char>(last.Length + 8);
        for (var index = 0; index < last.Length; index++)
        {
            var current = last[index];
            if (index > 0
                && char.IsUpper(current)
                && (char.IsLower(last[index - 1]) || index + 1 < last.Length && char.IsLower(last[index + 1])))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        return NormalizeAcronymTokens(NormalizeSpaces(new string(chars.ToArray()).Trim()));
    }

    private static string BuildDisplayName(string key, string section, string valueType)
    {
        var leaf = HumanizeKey(key);
        var normalizedSection = NormalizeSpaces(section);
        if (string.Equals(leaf, "Enabled", StringComparison.OrdinalIgnoreCase))
        {
            return $"Enable {normalizedSection}";
        }

        if (string.Equals(leaf, "Interval Minutes", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedSection} cadence";
        }

        if (string.Equals(leaf, "Timeout Seconds", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedSection} request timeout";
        }

        if (string.Equals(leaf, "Max Retries", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedSection} retry count";
        }

        if (string.Equals(leaf, "Api Key", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedSection} API key";
        }

        if (string.Equals(leaf, "Base Url", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedSection} base URL";
        }

        if (string.Equals(leaf, "Max Daily Credits", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedSection} daily credit budget";
        }

        if (string.Equals(leaf, "Max Monthly Credits", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedSection} monthly credit budget";
        }

        if (valueType == AppConfigValueTypes.Boolean)
        {
            if (leaf.StartsWith("Allow ", StringComparison.OrdinalIgnoreCase))
            {
                return $"{leaf} for {normalizedSection}";
            }

            if (leaf.StartsWith("Include ", StringComparison.OrdinalIgnoreCase))
            {
                return $"{leaf} in {normalizedSection}";
            }

            if (leaf.StartsWith("Require ", StringComparison.OrdinalIgnoreCase))
            {
                return $"{leaf} for {normalizedSection}";
            }

            if (leaf.StartsWith("Block ", StringComparison.OrdinalIgnoreCase)
                || leaf.StartsWith("Stop ", StringComparison.OrdinalIgnoreCase)
                || leaf.StartsWith("Record ", StringComparison.OrdinalIgnoreCase)
                || leaf.StartsWith("Import ", StringComparison.OrdinalIgnoreCase)
                || leaf.StartsWith("Auto ", StringComparison.OrdinalIgnoreCase))
            {
                return $"{leaf} in {normalizedSection}";
            }

            if (leaf.EndsWith(" Enabled", StringComparison.OrdinalIgnoreCase))
            {
                return $"Enable {normalizedSection} {leaf[..^8].Trim()}";
            }
        }

        return $"{normalizedSection} {char.ToLowerInvariant(leaf[0])}{leaf[1..]}";
    }

    private static string NormalizeSpaces(string value)
        => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizeAcronymTokens(string value)
        => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant() switch
            {
                "api" => "API",
                "url" => "URL",
                "ev" => "EV",
                "mlb" => "MLB",
                "dfs" => "DFS",
                _ => token
            }));

    private static string InferInputType(string key, string valueType)
    {
        if (valueType == AppConfigValueTypes.Boolean)
        {
            return AppConfigInputTypes.Boolean;
        }

        if (valueType == AppConfigValueTypes.Integer || valueType == AppConfigValueTypes.Decimal)
        {
            return key.Contains("Seconds", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Minutes", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Hours", StringComparison.OrdinalIgnoreCase)
                    ? AppConfigInputTypes.Duration
                    : AppConfigInputTypes.Number;
        }

        return valueType is AppConfigValueTypes.StringList or AppConfigValueTypes.Json
            ? AppConfigInputTypes.AdvancedText
            : AppConfigInputTypes.Text;
    }

    private static string? InferUnit(string key, string valueType)
    {
        if (valueType is not (AppConfigValueTypes.Integer or AppConfigValueTypes.Decimal))
        {
            return null;
        }

        if (key.Contains("Seconds", StringComparison.OrdinalIgnoreCase))
        {
            return "seconds";
        }

        if (key.Contains("Minutes", StringComparison.OrdinalIgnoreCase))
        {
            return "minutes";
        }

        if (key.Contains("Hours", StringComparison.OrdinalIgnoreCase))
        {
            return "hours";
        }

        if (key.Contains("Credits", StringComparison.OrdinalIgnoreCase))
        {
            return "credits";
        }

        if (key.Contains("Percent", StringComparison.OrdinalIgnoreCase))
        {
            return "percent";
        }

        if (key.Contains("Probability", StringComparison.OrdinalIgnoreCase))
        {
            return "probability";
        }

        return "count";
    }

    private static decimal? InferMinValue(string valueType, string inputType)
        => valueType is AppConfigValueTypes.Integer or AppConfigValueTypes.Decimal
            ? inputType == AppConfigInputTypes.Duration ? 1 : 0
            : null;

    private static decimal? InferMaxValue(string key, string valueType)
    {
        if (valueType is not (AppConfigValueTypes.Integer or AppConfigValueTypes.Decimal))
        {
            return null;
        }

        if (key.Contains("Probability", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (key.Contains("Percent", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        return null;
    }
}
