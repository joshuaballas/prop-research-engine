using Microsoft.Extensions.Options;
using SportsBetting.Api.Ingestion;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Integrations.Results.Soccer;
using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Configuration;

public sealed class AppConfigOptionsOverrides(
    IAppConfigService appConfigService) :
    IConfigureOptions<ApiSportsOptions>,
    IConfigureOptions<TheOddsApiOptions>,
    IConfigureOptions<TheOddsIngestionWorkerOptions>,
    IConfigureOptions<TheOddsScheduledRefreshOptions>,
    IConfigureOptions<TheOddsRefreshPlannerOptions>,
    IConfigureOptions<ProviderLineLifecycleOptions>,
    IConfigureOptions<PublicMarketReadinessOptions>,
    IConfigureOptions<MlbResultImportWorkerOptions>,
    IConfigureOptions<SoccerResultImportWorkerOptions>,
    IConfigureOptions<SoccerIdentityResolutionOptions>,
    IConfigureOptions<SoccerPredictionsOptions>,
    IConfigureOptions<SoccerGradingOptions>,
    IConfigureOptions<SoccerTopPicksOptions>,
    IConfigureOptions<UnderdogIngestionOptions>
{
    public void Configure(ApiSportsOptions options)
    {
        options.Enabled = Value("ApiSports.Enabled", options.Enabled);
        options.TimeoutSeconds = Value("ApiSports.TimeoutSeconds", options.TimeoutSeconds);
        options.MaxRetries = Value("ApiSports.MaxRetries", options.MaxRetries);
        options.Football.SeasonMetadataCacheHours = Value("Soccer.Results.ApiSports.SeasonMetadataCacheHours", options.Football.SeasonMetadataCacheHours);
    }

    public void Configure(TheOddsApiOptions options)
    {
        options.SportKey = Value("TheOdds.Api.SportKey", options.SportKey);
        options.SoccerWorldCupSportsbookRegion = Value("TheOdds.Api.SoccerWorldCupSportsbookRegion", options.SoccerWorldCupSportsbookRegion);
        options.SoccerWorldCupDfsRegion = Value("TheOdds.Api.SoccerWorldCupDfsRegion", options.SoccerWorldCupDfsRegion);
        options.Regions = Value("TheOdds.Api.Regions", options.Regions);
        options.OddsFormat = Value("TheOdds.Api.OddsFormat", options.OddsFormat);
        options.MaxConcurrentRequests = Value("TheOdds.Api.MaxConcurrentRequests", options.MaxConcurrentRequests);
    }

    public void Configure(TheOddsIngestionWorkerOptions options)
    {
        options.Enabled = Value("TheOdds.IngestionWorker.Enabled", options.Enabled);
        options.DryRun = Value("TheOdds.IngestionWorker.DryRun", options.DryRun);
        options.IntervalMinutes = Value("TheOdds.IngestionWorker.IntervalMinutes", options.IntervalMinutes);
        options.MaxEventsPerRun = Value("TheOdds.IngestionWorker.MaxEventsPerRun", options.MaxEventsPerRun);
        options.MaxEstimatedCreditsPerRun = Value("TheOdds.IngestionWorker.MaxEstimatedCreditsPerRun", options.MaxEstimatedCreditsPerRun);
        options.MaxEstimatedCreditsPerDay = Value("TheOdds.IngestionWorker.MaxEstimatedCreditsPerDay", options.MaxEstimatedCreditsPerDay);
        options.IncludeDfsBookmakers = Value("TheOdds.IngestionWorker.IncludeDfsBookmakers", options.IncludeDfsBookmakers);
        options.DfsRegion = Value("TheOdds.IngestionWorker.DfsRegion", options.DfsRegion);
        options.OnlyGameDates = Value("TheOdds.IngestionWorker.OnlyGameDates", options.OnlyGameDates);
    }

    public void Configure(TheOddsScheduledRefreshOptions options)
    {
        options.Enabled = Value("TheOdds.ScheduledRefresh.Enabled", options.Enabled);
        options.IntervalMinutes = Value("TheOdds.ScheduledRefresh.IntervalMinutes", options.IntervalMinutes);
        options.BookmakerPreset = Value("TheOdds.ScheduledRefresh.BookmakerPreset", options.BookmakerPreset);
        options.IncludeCandidates = Value("TheOdds.ScheduledRefresh.IncludeCandidates", options.IncludeCandidates);
        options.IncludeLineShopping = Value("TheOdds.ScheduledRefresh.IncludeLineShopping", options.IncludeLineShopping);
        options.IncludeFullDiagnostics = Value("TheOdds.ScheduledRefresh.IncludeFullDiagnostics", options.IncludeFullDiagnostics);
        options.FreshnessMinutes = Value("TheOdds.ScheduledRefresh.FreshnessMinutes", options.FreshnessMinutes);
        options.RequireForceLive = Value("TheOdds.ScheduledRefresh.RequireForceLive", options.RequireForceLive);
        options.AllowScheduledLiveRefresh = Value("TheOdds.ScheduledRefresh.AllowScheduledLiveRefresh", options.AllowScheduledLiveRefresh);
        options.AllowEventDiscoveryWhenNoPersistedEvents = Value("TheOdds.ScheduledRefresh.AllowEventDiscoveryWhenNoPersistedEvents", options.AllowEventDiscoveryWhenNoPersistedEvents);
        options.FallbackEstimatedApiCalls = Value("TheOdds.ScheduledRefresh.FallbackEstimatedApiCalls", options.FallbackEstimatedApiCalls);
        options.MaxEstimatedCallsPerRun = Value("TheOdds.ScheduledRefresh.MaxEstimatedCallsPerRun", options.MaxEstimatedCallsPerRun);
        options.ExpectedRefreshDurationSeconds = Value("TheOdds.ScheduledRefresh.ExpectedRefreshDurationSeconds", options.ExpectedRefreshDurationSeconds);
        options.PostRefreshReadBufferSeconds = Value("TheOdds.ScheduledRefresh.PostRefreshReadBufferSeconds", options.PostRefreshReadBufferSeconds);
    }

    public void Configure(TheOddsRefreshPlannerOptions options)
    {
        options.Enabled = Value("TheOdds.RefreshPlanner.Enabled", options.Enabled);
        options.EnforcementEnabled = Value("TheOdds.RefreshPlanner.EnforcementEnabled", options.EnforcementEnabled);
        options.DefaultBookmakerPreset = Value("TheOdds.RefreshPlanner.DefaultBookmakerPreset", options.DefaultBookmakerPreset);
        options.PlannerIntervalSeconds = Value("TheOdds.RefreshPlanner.PlannerIntervalSeconds", options.PlannerIntervalSeconds);
        options.MaxDailyCredits = Value("TheOdds.RefreshPlanner.MaxDailyCredits", options.MaxDailyCredits);
        options.MaxMonthlyCredits = Value("TheOdds.RefreshPlanner.MaxMonthlyCredits", options.MaxMonthlyCredits);
        options.BlockOnPerRunEstimate = Value("TheOdds.RefreshPlanner.BlockOnPerRunEstimate", options.BlockOnPerRunEstimate);
        options.BlockOnProviderCredits = Value("TheOdds.RefreshPlanner.BlockOnProviderCredits", options.BlockOnProviderCredits);
        options.BlockOnLocalMonthlyBudget = Value("TheOdds.RefreshPlanner.BlockOnLocalMonthlyBudget", options.BlockOnLocalMonthlyBudget);
        options.BlockOnLocalDailyBudget = Value("TheOdds.RefreshPlanner.BlockOnLocalDailyBudget", options.BlockOnLocalDailyBudget);
        options.WarnAtDailyPercent = Value("TheOdds.RefreshPlanner.WarnAtDailyPercent", options.WarnAtDailyPercent);
        options.WarnAtMonthlyPercent = Value("TheOdds.RefreshPlanner.WarnAtMonthlyPercent", options.WarnAtMonthlyPercent);
        options.HardStopAtDailyPercent = Value("TheOdds.RefreshPlanner.HardStopAtDailyPercent", options.HardStopAtDailyPercent);
        options.HardStopAtMonthlyPercent = Value("TheOdds.RefreshPlanner.HardStopAtMonthlyPercent", options.HardStopAtMonthlyPercent);
        options.ProviderRemainingHardStopCredits = Value("TheOdds.RefreshPlanner.ProviderRemainingHardStopCredits", options.ProviderRemainingHardStopCredits);
        options.ProviderTelemetryStaleAfterMinutes = Value("TheOdds.RefreshPlanner.ProviderTelemetryStaleAfterMinutes", options.ProviderTelemetryStaleAfterMinutes);
        options.FarOutMaxMinutes = Value("TheOdds.RefreshPlanner.FarOutMaxMinutes", options.FarOutMaxMinutes);
        options.FarOutRefreshMinutes = Value("TheOdds.RefreshPlanner.FarOutRefreshMinutes", options.FarOutRefreshMinutes);
        options.SameDayMaxMinutes = Value("TheOdds.RefreshPlanner.SameDayMaxMinutes", options.SameDayMaxMinutes);
        options.SameDayRefreshMinutes = Value("TheOdds.RefreshPlanner.SameDayRefreshMinutes", options.SameDayRefreshMinutes);
        options.PreLockMaxMinutes = Value("TheOdds.RefreshPlanner.PreLockMaxMinutes", options.PreLockMaxMinutes);
        options.PreLockRefreshMinutes = Value("TheOdds.RefreshPlanner.PreLockRefreshMinutes", options.PreLockRefreshMinutes);
        options.NearLockMaxMinutes = Value("TheOdds.RefreshPlanner.NearLockMaxMinutes", options.NearLockMaxMinutes);
        options.NearLockRefreshMinutes = Value("TheOdds.RefreshPlanner.NearLockRefreshMinutes", options.NearLockRefreshMinutes);
        options.RetryAfterFailureMinutes = Value("TheOdds.RefreshPlanner.RetryAfterFailureMinutes", options.RetryAfterFailureMinutes);
        options.StopAfterEventStart = Value("TheOdds.RefreshPlanner.StopAfterEventStart", options.StopAfterEventStart);
    }

    public void Configure(ProviderLineLifecycleOptions options)
    {
        options.ActiveLineMaxAgeMinutes = Value("ProviderLineLifecycle.ActiveLineMaxAgeMinutes", options.ActiveLineMaxAgeMinutes);
        options.PulledAfterSuccessfulMissingRefreshes = Value("ProviderLineLifecycle.PulledAfterSuccessfulMissingRefreshes", options.PulledAfterSuccessfulMissingRefreshes);
        options.ExpireAfterHours = Value("ProviderLineLifecycle.ExpireAfterHours", options.ExpireAfterHours);
        options.RecordSeenUnchangedEvents = Value("ProviderLineLifecycle.RecordSeenUnchangedEvents", options.RecordSeenUnchangedEvents);
    }

    public void Configure(PublicMarketReadinessOptions options)
    {
        options.ColdStartGraceMinutes = Value("PublicMarkets.ColdStartGraceMinutes", options.ColdStartGraceMinutes);
    }

    public void Configure(MlbResultImportWorkerOptions options)
    {
        options.Enabled = Value("Mlb.Results.Worker.Enabled", options.Enabled);
        options.IntervalMinutes = Value("Mlb.Results.Worker.IntervalMinutes", options.IntervalMinutes);
        options.MaxBackfillDatesPerRun = Value("Mlb.Results.Worker.MaxBackfillDatesPerRun", options.MaxBackfillDatesPerRun);
        options.RecentWindowDays = Value("Mlb.Results.Worker.RecentWindowDays", options.RecentWindowDays);
        options.HistoricalBackfillEnabled = Value("Mlb.Results.Worker.HistoricalBackfillEnabled", options.HistoricalBackfillEnabled);
        options.HistoricalNonCompleteAttemptCap = Value("Mlb.Results.Worker.HistoricalNonCompleteAttemptCap", options.HistoricalNonCompleteAttemptCap);
        options.RepairLookbackDays = Value("Mlb.Results.Worker.RepairLookbackDays", options.RepairLookbackDays);
        options.MinimumCompleteImportedResults = Value("Mlb.Results.Worker.MinimumCompleteImportedResults", options.MinimumCompleteImportedResults);
        options.MinimumCompleteDistinctPlayers = Value("Mlb.Results.Worker.MinimumCompleteDistinctPlayers", options.MinimumCompleteDistinctPlayers);
        options.MinimumCompleteDistinctPropTypes = Value("Mlb.Results.Worker.MinimumCompleteDistinctPropTypes", options.MinimumCompleteDistinctPropTypes);
    }

    public void Configure(SoccerResultImportWorkerOptions options)
    {
        options.Enabled = Value("Soccer.Results.ApiSports.Worker.Enabled", options.Enabled);
        options.IntervalMinutes = Value("Soccer.Results.ApiSports.Worker.IntervalMinutes", options.IntervalMinutes);
        options.MaxFixturesPerRun = Value("Soccer.Results.ApiSports.Worker.MaxFixturesPerRun", options.MaxFixturesPerRun);
        options.MaxFixturesPerCompetitionPerRun = Value("Soccer.Results.ApiSports.Worker.MaxFixturesPerCompetitionPerRun", options.MaxFixturesPerCompetitionPerRun ?? options.MaxFixturesPerRun);
        options.MaxFixturesTotalPerRun = Value("Soccer.Results.ApiSports.Worker.MaxFixturesTotalPerRun", options.MaxFixturesTotalPerRun ?? options.MaxFixturesPerCompetitionPerRun ?? options.MaxFixturesPerRun);
        options.RecentWindowDays = Value("Soccer.Results.ApiSports.Worker.RecentWindowDays", options.RecentWindowDays);
        options.ImportCompletedFixturesOnly = Value("Soccer.Results.ApiSports.Worker.ImportCompletedFixturesOnly", options.ImportCompletedFixturesOnly);
        options.Competition = Value("Soccer.Results.ApiSports.Worker.Competition", options.Competition);
        options.Provider = Value("Soccer.Results.ApiSports.Worker.Provider", options.Provider);
    }

    public void Configure(SoccerIdentityResolutionOptions options)
    {
        options.Enabled = Value("Soccer.IdentityResolution.Enabled", options.Enabled);
        options.AutoWriteSafeLinks = Value("Soccer.IdentityResolution.AutoWriteSafeLinks", options.AutoWriteSafeLinks);
        options.MaxWritesPerRun = Value("Soccer.IdentityResolution.MaxWritesPerRun", options.MaxWritesPerRun);
    }

    public void Configure(SoccerPredictionsOptions options)
    {
        options.Enabled = Value("Soccer.Predictions.Enabled", options.Enabled);
        options.MinimumSampleSize = Value("Soccer.Predictions.MinimumSampleSize", options.MinimumSampleSize);
        options.LowSampleWarningThreshold = Value("Soccer.Predictions.LowSampleWarningThreshold", options.LowSampleWarningThreshold);
        options.TopPickMinimumSampleSize = Value("Soccer.Predictions.TopPickMinimumSampleSize", options.TopPickMinimumSampleSize);
        options.MinimumTopPickHitProbability = Value("Soccer.Predictions.MinimumTopPickHitProbability", options.MinimumTopPickHitProbability);
    }

    public void Configure(SoccerGradingOptions options)
    {
        options.Enabled = Value("Soccer.Grading.Enabled", options.Enabled);
    }

    public void Configure(SoccerTopPicksOptions options)
    {
        options.Enabled = Value("Soccer.TopPicks.Enabled", options.Enabled);
        options.MinimumTopPickSampleSize = Value("Soccer.TopPicks.MinimumTopPickSampleSize", options.MinimumTopPickSampleSize);
        options.MinimumHitProbability = Value("Soccer.TopPicks.MinimumHitProbability", options.MinimumHitProbability);
        options.AllowNegativeEv = Value("Soccer.TopPicks.AllowNegativeEv", options.AllowNegativeEv);
    }

    public void Configure(UnderdogIngestionOptions options)
    {
        options.Enabled = Value("Underdog.Ingestion.Enabled", options.Enabled);
        options.IntervalMinutes = Value("Underdog.Ingestion.IntervalMinutes", options.IntervalMinutes);
    }

    private T Value<T>(string key, T fallback)
        => appConfigService.GetValue(key, fallback);
}
