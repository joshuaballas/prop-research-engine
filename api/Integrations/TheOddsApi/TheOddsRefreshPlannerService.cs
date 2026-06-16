using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface ITheOddsRefreshPlannerService
{
    Task<TheOddsRefreshPlanResult> BuildPlanAsync(CancellationToken cancellationToken);
    Task<TheOddsRefreshPlannerDecision> PersistDecisionAsync(
        TheOddsRefreshPlanResult plan,
        string source,
        CancellationToken cancellationToken);
}

public sealed class TheOddsRefreshPlannerService(
    ApplicationDbContext dbContext,
    IOptions<TheOddsRefreshPlannerOptions> options) : ITheOddsRefreshPlannerService
{
    public async Task<TheOddsRefreshPlanResult> BuildPlanAsync(CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var plannerOptions = options.Value;
        var latestRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API" && row.Sport == "MLB")
            .OrderByDescending(row => row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestSuccessfulRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API"
                && row.Sport == "MLB"
                && row.Succeeded
                && !row.DryRun
                && !row.Blocked
                && row.BookmakerPreset == plannerOptions.DefaultBookmakerPreset)
            .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var slateDate = ResolveMlbEasternDate(generatedAtUtc);
        var eventStartTimes = ExtractEventStartTimes(latestRun?.DiagnosticsJson);
        var estimatedSlateEvents = ExtractEstimatedSlateEvents(latestRun?.DiagnosticsJson);
        if (estimatedSlateEvents == 0)
        {
            estimatedSlateEvents = eventStartTimes.Length;
        }

        var estimatedApiCalls = estimatedSlateEvents == 0 ? 0 : 1 + estimatedSlateEvents;
        var providerDiagnostics = ExtractProviderDiagnostics(latestRun?.DiagnosticsJson);
        var activeSnapshotCount = latestRun is null ? 0 : await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .CountAsync(row => row.TheOddsRefreshRunId == latestRun.Id
                && row.LifecycleStatus == "Active"
                && !row.IsStale
                && !row.IsExpired, cancellationToken);
        var topPickSnapshotCount = latestRun is null ? 0 : await dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .CountAsync(row => row.TheOddsRefreshRunId == latestRun.Id, cancellationToken);
        var latestHadUsableMarketData = activeSnapshotCount > 0 || topPickSnapshotCount > 0;
        var providerStatus = ResolveProviderStatus(latestRun, latestHadUsableMarketData, providerDiagnostics);
        var providerAvailable = providerStatus is "Healthy" or "Degraded" &&
            !providerDiagnostics.ProviderRejectedKeyDetected &&
            providerDiagnostics.RequestsRemaining is null or > 0;

        var nextEventStart = eventStartTimes.FirstOrDefault(value => value > generatedAtUtc);
        var minutesUntilNextEventStart = nextEventStart == default
            ? (decimal?)null
            : Math.Round((decimal)(nextEventStart - generatedAtUtc).TotalMinutes, 2);
        var cadence = ResolveCadence(plannerOptions, minutesUntilNextEventStart);
        var lastSuccessfulAt = latestSuccessfulRun?.CompletedAtUtc ?? latestSuccessfulRun?.StartedAtUtc;
        var nextEligibleRefreshUtc = lastSuccessfulAt?.AddMinutes(cadence.RefreshEveryMinutes) ?? generatedAtUtc;
        var cadenceAllowsRefresh = !lastSuccessfulAt.HasValue || generatedAtUtc >= nextEligibleRefreshUtc;
        var budget = await BuildBudgetAsync(plannerOptions, providerDiagnostics, estimatedApiCalls, generatedAtUtc, cancellationToken);
        var stopAfterStartTriggered = plannerOptions.StopAfterEventStart &&
            eventStartTimes.Length > 0 &&
            eventStartTimes.All(value => value <= generatedAtUtc);
        var skipReason = BuildSkipReason(
            plannerOptions,
            providerAvailable,
            budget.BudgetAllowed,
            cadenceAllowsRefresh,
            stopAfterStartTriggered,
            estimatedSlateEvents);

        return new TheOddsRefreshPlanResult(
            generatedAtUtc,
            ProviderStatus: providerStatus,
            ProviderRejectedKeyDetected: providerDiagnostics.ProviderRejectedKeyDetected,
            ProviderAvailable: providerAvailable,
            RequestsUsed: providerDiagnostics.RequestsUsed,
            RequestsRemaining: providerDiagnostics.RequestsRemaining,
            SlateDate: slateDate,
            BookmakerPreset: plannerOptions.DefaultBookmakerPreset,
            EstimatedSlateEvents: estimatedSlateEvents,
            EstimatedApiCalls: estimatedApiCalls,
            CadenceRule: cadence,
            MinutesUntilNextEventStart: minutesUntilNextEventStart,
            LastSuccessfulRefreshAtUtc: lastSuccessfulAt,
            MinutesSinceLastSuccessfulRefresh: lastSuccessfulAt.HasValue
                ? Math.Round((decimal)(generatedAtUtc - lastSuccessfulAt.Value).TotalMinutes, 2)
                : null,
            NextEligibleRefreshUtc: nextEligibleRefreshUtc,
            Budget: budget,
            HasUsableMarketData: latestHadUsableMarketData,
            ShouldRefresh: skipReason is null,
            SkipReason: skipReason,
            RecommendedAction: BuildRecommendedAction(skipReason, providerDiagnostics));
    }

    private async Task<TheOddsRefreshBudgetSummary> BuildBudgetAsync(
        TheOddsRefreshPlannerOptions plannerOptions,
        ProviderDiagnostics diagnostics,
        int estimatedApiCalls,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var today = generatedAtUtc.UtcDateTime.Date;
        var monthStart = new DateTime(generatedAtUtc.Year, generatedAtUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var dailyUsedCredits = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API"
                && !row.DryRun
                && !row.Blocked
                && row.StartedAtUtc >= today)
            .SumAsync(row => row.ActualApiCalls, cancellationToken);
        var persistedMonthlyUsedCredits = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API"
                && !row.DryRun
                && !row.Blocked
                && row.StartedAtUtc >= monthStart)
            .SumAsync(row => row.ActualApiCalls, cancellationToken);
        var monthlyUsedCredits = Math.Max(persistedMonthlyUsedCredits, diagnostics.RequestsUsed ?? 0);
        var dailyAfterEstimate = dailyUsedCredits + estimatedApiCalls;
        var monthlyAfterEstimate = monthlyUsedCredits + estimatedApiCalls;
        var dailyPercentAfterEstimate = Percent(dailyAfterEstimate, plannerOptions.MaxDailyCredits);
        var monthlyPercentAfterEstimate = Percent(monthlyAfterEstimate, plannerOptions.MaxMonthlyCredits);
        var dailyHardStop = dailyPercentAfterEstimate >= plannerOptions.HardStopAtDailyPercent;
        var monthlyHardStop = monthlyPercentAfterEstimate >= plannerOptions.HardStopAtMonthlyPercent;
        var providerRemainingHardStop = diagnostics.RequestsRemaining.HasValue &&
            (diagnostics.RequestsRemaining.Value <= plannerOptions.ProviderRemainingHardStopCredits ||
                diagnostics.RequestsRemaining.Value < estimatedApiCalls);

        return new TheOddsRefreshBudgetSummary(
            dailyUsedCredits,
            monthlyUsedCredits,
            persistedMonthlyUsedCredits,
            diagnostics.RequestsUsed,
            diagnostics.RequestsRemaining,
            estimatedApiCalls,
            dailyAfterEstimate,
            monthlyAfterEstimate,
            dailyPercentAfterEstimate,
            monthlyPercentAfterEstimate,
            dailyPercentAfterEstimate >= plannerOptions.WarnAtDailyPercent,
            monthlyPercentAfterEstimate >= plannerOptions.WarnAtMonthlyPercent,
            dailyHardStop,
            monthlyHardStop,
            providerRemainingHardStop,
            !dailyHardStop && !monthlyHardStop && !providerRemainingHardStop);
    }

    public async Task<TheOddsRefreshPlannerDecision> PersistDecisionAsync(
        TheOddsRefreshPlanResult plan,
        string source,
        CancellationToken cancellationToken)
    {
        var plannerOptions = options.Value;
        var decision = new TheOddsRefreshPlannerDecision
        {
            Id = Guid.NewGuid(),
            GeneratedAtUtc = plan.GeneratedAtUtc,
            Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source,
            Sport = "MLB",
            SlateDate = plan.SlateDate,
            BookmakerPreset = plan.BookmakerPreset,
            ShouldRefresh = plan.ShouldRefresh,
            Skipped = !plan.ShouldRefresh,
            SkipReason = plan.SkipReason,
            ProviderStatus = plan.ProviderStatus,
            ProviderAvailable = plan.ProviderAvailable,
            ProviderRejectedKeyDetected = plan.ProviderRejectedKeyDetected,
            RequestsUsed = plan.RequestsUsed,
            RequestsRemaining = plan.RequestsRemaining,
            EstimatedApiCalls = plan.EstimatedApiCalls,
            DailyCreditsUsed = plan.Budget.DailyUsedCredits,
            MonthlyCreditsUsed = plan.Budget.MonthlyUsedCredits,
            DailyCreditLimit = plannerOptions.MaxDailyCredits,
            MonthlyCreditLimit = plannerOptions.MaxMonthlyCredits,
            BudgetAllowed = plan.Budget.BudgetAllowed,
            CadenceRule = plan.CadenceRule.Name,
            MinutesUntilNextEventStart = plan.MinutesUntilNextEventStart,
            MinutesSinceLastSuccessfulRefresh = plan.MinutesSinceLastSuccessfulRefresh,
            NextEligibleRefreshUtc = plan.NextEligibleRefreshUtc,
            RecommendedAction = plan.RecommendedAction,
            DetailsJson = JsonSerializer.SerializeToDocument(new
            {
                plan.ProviderStatus,
                plan.ProviderAvailable,
                plan.ProviderRejectedKeyDetected,
                plan.RequestsUsed,
                plan.RequestsRemaining,
                plan.EstimatedSlateEvents,
                plan.EstimatedApiCalls,
                plan.CadenceRule,
                plan.Budget,
                plan.HasUsableMarketData,
                plan.ShouldRefresh,
                plan.SkipReason,
                plan.RecommendedAction,
                plannerOptions = new
                {
                    plannerOptions.Enabled,
                    plannerOptions.DefaultBookmakerPreset,
                    plannerOptions.PlannerIntervalSeconds,
                    plannerOptions.MaxDailyCredits,
                    plannerOptions.MaxMonthlyCredits,
                    plannerOptions.WarnAtDailyPercent,
                    plannerOptions.WarnAtMonthlyPercent,
                    plannerOptions.HardStopAtDailyPercent,
                    plannerOptions.HardStopAtMonthlyPercent,
                    plannerOptions.ProviderRemainingHardStopCredits,
                    plannerOptions.StopAfterEventStart
                }
            })
        };

        dbContext.TheOddsRefreshPlannerDecisions.Add(decision);
        await dbContext.SaveChangesAsync(cancellationToken);
        return decision;
    }

    private static string ResolveProviderStatus(
        SportsBetting.Api.Models.TheOddsRefreshRun? latestRun,
        bool hasUsableMarketData,
        ProviderDiagnostics diagnostics)
    {
        if (latestRun is null)
        {
            return "Unknown";
        }

        if (diagnostics.ProviderRejectedKeyDetected)
        {
            return "Unavailable";
        }

        if (diagnostics.RequestsRemaining is <= 0)
        {
            return "Unavailable";
        }

        if (latestRun.Succeeded && hasUsableMarketData)
        {
            return diagnostics.RequestsRemaining is <= 5 ? "Degraded" : "Healthy";
        }

        return latestRun.Succeeded ? "Degraded" : "Unavailable";
    }

    private static string? BuildSkipReason(
        TheOddsRefreshPlannerOptions plannerOptions,
        bool providerAvailable,
        bool budgetAllowed,
        bool cadenceAllowsRefresh,
        bool stopAfterStartTriggered,
        int estimatedSlateEvents)
    {
        if (!plannerOptions.Enabled) return "PlannerDisabled";
        if (!providerAvailable) return "ProviderUnavailable";
        if (!budgetAllowed) return "CreditBudgetExceeded";
        if (stopAfterStartTriggered) return "StopAfterEventStart";
        if (estimatedSlateEvents <= 0) return "NoPersistedSlateEvents";
        if (!cadenceAllowsRefresh) return "CadenceNotDue";
        return null;
    }

    private static string BuildRecommendedAction(string? skipReason, ProviderDiagnostics diagnostics)
    {
        return skipReason switch
        {
            null => "Refresh is eligible by persisted planner state. Scheduled live execution still requires explicit scheduled live config.",
            "ProviderUnavailable" when diagnostics.ProviderRejectedKeyDetected => "Check TheOddsApi:ApiKey and account credit status before live refreshes.",
            "ProviderUnavailable" => "Provider is unavailable or degraded beyond planner limits; skip scheduled refresh.",
            "CreditBudgetExceeded" => "Skip refresh because estimated calls would exceed the configured credit budget or provider remaining credits.",
            "CadenceNotDue" => "Skip refresh until the configured cadence window is due.",
            "StopAfterEventStart" => "Skip refresh because configured planning stops after the slate has started.",
            "NoPersistedSlateEvents" => "No persisted slate events are available for estimation.",
            "PlannerDisabled" => "Planner is disabled in config.",
            _ => "Skip refresh based on current planner state."
        };
    }

    private static PlannerCadenceRule ResolveCadence(TheOddsRefreshPlannerOptions options, decimal? minutesUntilNextEventStart)
    {
        if (!minutesUntilNextEventStart.HasValue)
        {
            return new PlannerCadenceRule("NoKnownUpcomingEvent", null, null, options.FarOutRefreshMinutes, "No persisted upcoming event start was found.");
        }

        var minutes = minutesUntilNextEventStart.Value;
        if (minutes <= options.NearLockMaxMinutes && minutes >= 0)
        {
            return new PlannerCadenceRule("NearLock", 0, options.NearLockMaxMinutes, options.NearLockRefreshMinutes, "0-30 minutes before first pitch.");
        }

        if (minutes <= options.PreLockMaxMinutes)
        {
            return new PlannerCadenceRule("PreLock", options.NearLockMaxMinutes, options.PreLockMaxMinutes, options.PreLockRefreshMinutes, "30-120 minutes before first pitch.");
        }

        if (minutes <= options.SameDayMaxMinutes)
        {
            return new PlannerCadenceRule("SameDay", options.PreLockMaxMinutes, options.SameDayMaxMinutes, options.SameDayRefreshMinutes, "120-720 minutes before first pitch.");
        }

        return new PlannerCadenceRule("FarOut", options.SameDayMaxMinutes, options.FarOutMaxMinutes, options.FarOutRefreshMinutes, "720-1440+ minutes before first pitch.");
    }

    private static ProviderDiagnostics ExtractProviderDiagnostics(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null)
        {
            return new ProviderDiagnostics(null, null, null, ProviderRejectedKeyDetected: false);
        }

        var root = diagnosticsJson.RootElement;
        return new ProviderDiagnostics(
            FindFirstIntProperty(root, "requestsUsed", "RequestsUsed", "requests_used"),
            FindFirstIntProperty(root, "requestsRemaining", "RequestsRemaining", "requests_remaining"),
            FindFirstIntProperty(root, "requestsLast", "RequestsLast", "requests_last"),
            ContainsText(root, "rejected the configured API key") || ContainsText(root, "INVALID_KEY"));
    }

    private static int ExtractEstimatedSlateEvents(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null) return 0;
        var root = diagnosticsJson.RootElement;
        if (TryGetPropertyIgnoreCase(root, "events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            return events.GetArrayLength();
        }

        return FindFirstIntProperty(root, "eventsFetched", "oddsEventsFetched", "estimatedSlateEvents") ?? 0;
    }

    private static DateTimeOffset[] ExtractEventStartTimes(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null ||
            !TryGetPropertyIgnoreCase(diagnosticsJson.RootElement, "events", out var events) ||
            events.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return events.EnumerateArray()
            .Select(item => TryGetDateTimeOffsetProperty(item, "eventStartTime") ?? TryGetDateTimeOffsetProperty(item, "EventStartTime"))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .ToArray();
    }

    private static DateOnly ResolveMlbEasternDate(DateTimeOffset utcNow)
    {
        try
        {
            var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, eastern).DateTime);
        }
        catch
        {
            return DateOnly.FromDateTime(utcNow.ToOffset(TimeSpan.FromHours(-4)).DateTime);
        }
    }

    private static DateTimeOffset? TryGetDateTimeOffsetProperty(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static int? FindFirstIntProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) &&
                    property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var value))
                {
                    return value;
                }

                var nested = FindFirstIntProperty(property.Value, names);
                if (nested.HasValue) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstIntProperty(item, names);
                if (nested.HasValue) return nested;
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool ContainsText(JsonElement element, string text)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Object => element.EnumerateObject().Any(property => ContainsText(property.Value, text)),
            JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsText(item, text)),
            _ => false
        };
    }

    private static decimal Percent(int value, int denominator)
    {
        return denominator <= 0 ? 100m : Math.Round(value / (decimal)denominator * 100m, 2);
    }

    private sealed record ProviderDiagnostics(
        int? RequestsUsed,
        int? RequestsRemaining,
        int? RequestsLast,
        bool ProviderRejectedKeyDetected);
}

public sealed class TheOddsRefreshPlannerOptions
{
    public const string SectionName = "TheOddsRefreshPlanner";

    public bool Enabled { get; set; } = true;
    public string DefaultBookmakerPreset { get; set; } = "dfsOnly";
    public int PlannerIntervalSeconds { get; set; } = 60;
    public int MaxDailyCredits { get; set; } = 500;
    public int MaxMonthlyCredits { get; set; } = 500;
    public decimal WarnAtDailyPercent { get; set; } = 80m;
    public decimal WarnAtMonthlyPercent { get; set; } = 80m;
    public decimal HardStopAtDailyPercent { get; set; } = 100m;
    public decimal HardStopAtMonthlyPercent { get; set; } = 100m;
    public int ProviderRemainingHardStopCredits { get; set; } = 5;
    public int FarOutRefreshMinutes { get; set; } = 60;
    public int SameDayMaxMinutes { get; set; } = 720;
    public int SameDayRefreshMinutes { get; set; } = 30;
    public int PreLockMaxMinutes { get; set; } = 120;
    public int PreLockRefreshMinutes { get; set; } = 15;
    public int NearLockMaxMinutes { get; set; } = 30;
    public int NearLockRefreshMinutes { get; set; } = 5;
    public int FarOutMaxMinutes { get; set; } = 1440;
    public bool StopAfterEventStart { get; set; } = true;

    public TimeSpan GetPlannerInterval() => TimeSpan.FromSeconds(Math.Max(10, PlannerIntervalSeconds));
}

public sealed record TheOddsRefreshPlanResult(
    DateTimeOffset GeneratedAtUtc,
    string ProviderStatus,
    bool ProviderRejectedKeyDetected,
    bool ProviderAvailable,
    int? RequestsUsed,
    int? RequestsRemaining,
    DateOnly SlateDate,
    string BookmakerPreset,
    int EstimatedSlateEvents,
    int EstimatedApiCalls,
    PlannerCadenceRule CadenceRule,
    decimal? MinutesUntilNextEventStart,
    DateTimeOffset? LastSuccessfulRefreshAtUtc,
    decimal? MinutesSinceLastSuccessfulRefresh,
    DateTimeOffset NextEligibleRefreshUtc,
    TheOddsRefreshBudgetSummary Budget,
    bool HasUsableMarketData,
    bool ShouldRefresh,
    string? SkipReason,
    string RecommendedAction);

public sealed record PlannerCadenceRule(
    string Name,
    int? MinMinutesUntilStart,
    int? MaxMinutesUntilStart,
    int RefreshEveryMinutes,
    string Description);

public sealed record TheOddsRefreshBudgetSummary(
    int DailyUsedCredits,
    int MonthlyUsedCredits,
    int PersistedMonthlyUsedCredits,
    int? ProviderReportedRequestsUsed,
    int? ProviderReportedRequestsRemaining,
    int EstimatedApiCalls,
    int DailyAfterEstimate,
    int MonthlyAfterEstimate,
    decimal DailyPercentAfterEstimate,
    decimal MonthlyPercentAfterEstimate,
    bool DailyWarning,
    bool MonthlyWarning,
    bool DailyHardStop,
    bool MonthlyHardStop,
    bool ProviderRemainingHardStop,
    bool BudgetAllowed);
