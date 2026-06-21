using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SportsBetting.Api.Endpoints;

public static class PublicMarketsEndpoints
{
    private static readonly string[] SupportedMarketSports = ["MLB", "SOCCER"];
    private static readonly string[] SupportedMarketCompetitions = ["MLB", "World Cup"];
    private const string SoccerCompetitionName = "World Cup";
    private static readonly string[] SupportedMlbPerformancePropTypeKeys =
    [
        "HITS",
        "RUNS",
        "RBIS",
        "HOME_RUNS",
        "TOTAL_BASES",
        "STOLEN_BASES",
        "WALKS",
        "HITS_RUNS_RBIS",
        "PITCHER_STRIKEOUTS",
        "PITCHER_WALKS",
        "PITCHER_HITS_ALLOWED",
        "PITCHER_EARNED_RUNS",
        "HOMERUNS",
        "TOTALBASES",
        "STOLENBASES",
        "BATTERWALKS",
        "HITSRUNSRBIS",
        "PITCHERSTRIKEOUTS",
        "WALKSALLOWED",
        "PITCHERWALKS",
        "PITCHERHITSALLOWED",
        "HITSALLOWED",
        "PITCHEREARNEDRUNS",
        "EARNEDRUNSALLOWED"
    ];
    private const string MarketReadLimitation = "MLB result grading is supported. Soccer / World Cup provider-line reads are supported from persisted snapshots when available; soccer history and performance grading are not available yet.";
    private const string SoccerHistoryUnavailableMessage = "Soccer history is not available yet.";

    public static IEndpointRouteBuilder MapPublicMarketsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/markets/freshness", async (
            string? sport,
            DateOnly? slateDate,
            string? bookmakerPreset,
            int? freshnessMinutes,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var marketSport = ResolveMarketSport(sport);
            if (marketSport.UnsupportedResponse is not null)
            {
                return marketSport.UnsupportedResponse;
            }

            var response = await BuildMarketFreshnessResponseAsync(
                marketSport.RequestedSport,
                marketSport.EffectiveSport!,
                slateDate,
                bookmakerPreset,
                freshnessMinutes,
                dbContext,
                cancellationToken);

            return Results.Ok(response);
        })
        .WithName("MarketsFreshness")
        .WithSummary("Production-safe persisted market freshness metadata.");

        endpoints.MapGet("/api/markets/provider-status", async (
            int? freshnessMinutes,
            ITheOddsRefreshPlannerService planner,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await BuildMarketProviderStatusResponseAsync(
                freshnessMinutes,
                planner,
                dbContext,
                cancellationToken));
        })
        .WithName("MarketsProviderStatus")
        .WithSummary("Production-safe provider/API-key/credit status from persisted refresh metadata.");

        endpoints.MapGet("/api/markets/refresh-plan", async (
            bool? persistDecision,
            ITheOddsRefreshPlannerService planner,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await BuildMarketRefreshPlanResponseAsync(
                persistDecision ?? false,
                planner,
                cancellationToken));
        })
        .WithName("MarketsRefreshPlan")
        .WithSummary("Production-safe read-only The Odds refresh planner and credit budget estimator.");

        endpoints.MapGet("/api/markets/refresh-plan/history", async (
            int? take,
            string? sport,
            bool? skipped,
            string? source,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var query = dbContext.TheOddsRefreshPlannerDecisions
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(sport))
            {
                query = query.Where(row => row.Sport == sport);
            }

            if (skipped.HasValue)
            {
                query = query.Where(row => row.Skipped == skipped.Value);
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                query = query.Where(row => row.Source == source);
            }

            var rows = await query
                .OrderByDescending(row => row.GeneratedAtUtc)
                .Take(Math.Clamp(take ?? 50, 1, 500))
                .Select(row => new
                {
                    row.Id,
                    row.GeneratedAtUtc,
                    row.Source,
                    row.Sport,
                    row.SlateDate,
                    row.BookmakerPreset,
                    row.ShouldRefresh,
                    row.Skipped,
                    row.SkipReason,
                    row.ProviderStatus,
                    row.ProviderAvailable,
                    row.ProviderRejectedKeyDetected,
                    row.RequestsUsed,
                    row.RequestsRemaining,
                    row.EstimatedApiCalls,
                    row.DailyCreditsUsed,
                    row.MonthlyCreditsUsed,
                    row.DailyCreditLimit,
                    row.MonthlyCreditLimit,
                    row.BudgetAllowed,
                    row.CadenceRule,
                    row.MinutesUntilNextEventStart,
                    row.MinutesSinceLastSuccessfulRefresh,
                    row.NextEligibleRefreshUtc,
                    row.RecommendedAction,
                    row.DetailsJson
                })
                .ToArrayAsync(cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                filters = new { take = Math.Clamp(take ?? 50, 1, 500), sport, skipped, source },
                decisions = rows
            });
        })
        .WithName("MarketsRefreshPlanHistory")
        .WithSummary("Production-safe read-only refresh planner decision history.");

        endpoints.MapGet("/api/mlb/freshness", async (
            DateOnly? slateDate,
            string? bookmakerPreset,
            int? freshnessMinutes,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var response = await BuildMarketFreshnessResponseAsync(
                requestedSport: "MLB",
                effectiveSport: "MLB",
                slateDate,
                bookmakerPreset,
                freshnessMinutes,
                dbContext,
                cancellationToken);

            return Results.Ok(response);
        })
        .WithName("MlbFreshness")
        .WithSummary("Compatibility wrapper for /api/markets/freshness?sport=MLB.");

        endpoints.MapGet("/api/markets/top-picks", async (
            string? sport,
            Guid? runId,
            int? take,
            int? freshnessMinutes,
            string? providerCategory,
            string? bookmakerPreset,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            CancellationToken cancellationToken) =>
        {
            var marketSport = ResolveMarketSport(sport);
            if (marketSport.UnsupportedResponse is not null)
            {
                return marketSport.UnsupportedResponse;
            }

            return Results.Ok(await BuildMarketTopPicksResponseAsync(
                marketSport.RequestedSport,
                marketSport.EffectiveSport!,
                runId,
                take,
                freshnessMinutes,
                providerCategory,
                bookmakerPreset,
                dbContext,
                probabilityService,
                cancellationToken));
        })
        .WithName("MarketsTopPicks")
        .WithSummary("Production-safe persisted market top picks.");

        endpoints.MapGet("/api/mlb/top-picks", async (
            Guid? runId,
            int? take,
            int? freshnessMinutes,
            string? providerCategory,
            string? bookmakerPreset,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await BuildMarketTopPicksResponseAsync(
                requestedSport: "MLB",
                effectiveSport: "MLB",
                runId,
                take,
                freshnessMinutes,
                providerCategory,
                bookmakerPreset,
                dbContext,
                probabilityService,
                cancellationToken));
        })
        .WithName("MlbTopPicks")
        .WithSummary("Compatibility wrapper for /api/markets/top-picks?sport=MLB.");

        endpoints.MapGet("/api/markets/line-shopping", async (
            string? sport,
            Guid? runId,
            int? take,
            bool? topOnly,
            int? freshnessMinutes,
            string? providerCategory,
            bool? includeBookmakerPrices,
            string? bookmakerPreset,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            CancellationToken cancellationToken) =>
        {
            var marketSport = ResolveMarketSport(sport);
            if (marketSport.UnsupportedResponse is not null)
            {
                return marketSport.UnsupportedResponse;
            }

            return Results.Ok(await BuildMarketLineShoppingResponseAsync(
                marketSport.RequestedSport,
                marketSport.EffectiveSport!,
                runId,
                take,
                topOnly,
                freshnessMinutes,
                providerCategory,
                includeBookmakerPrices,
                bookmakerPreset,
                dbContext,
                probabilityService,
                cancellationToken));
        })
        .WithName("MarketsLineShopping")
        .WithSummary("Production-safe persisted market line-shopping rows.");

        endpoints.MapGet("/api/markets/performance-summary", async (
            string? sport,
            string? providerCategory,
            string? provider,
            string? bookmaker,
            string? propType,
            string? strategyClassification,
            string? strategyTier,
            bool? candidateOnly,
            DateOnly? fromDate,
            DateOnly? toDate,
            DateOnly? startDate,
            DateOnly? endDate,
            int? minSampleSize,
            string? gradingMode,
            bool? topPickOnly,
            bool? includeWatchlist,
            bool? includeResearch,
            bool? includeStale,
            bool? includeExcluded,
            bool? includeNoResultDiagnostics,
            bool? includeDiagnostics,
            int? diagnosticSampleSize,
            IPlayerIdentityAliasService playerIdentityAliasService,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var effectiveFromDate = fromDate ?? startDate;
            var effectiveToDate = toDate ?? endDate;
            var marketSport = ResolveMarketSport(sport);
            if (marketSport.UnsupportedResponse is not null)
            {
                return marketSport.UnsupportedResponse;
            }

            var response = await BuildMarketPerformanceSummaryResponseAsync(
                marketSport.RequestedSport,
                marketSport.EffectiveSport!,
                providerCategory,
                provider,
                bookmaker,
                propType,
                strategyClassification,
                strategyTier,
                ResolveEffectiveCandidateOnly(candidateOnly, includeWatchlist, includeResearch),
                effectiveFromDate,
                effectiveToDate,
                startDate,
                endDate,
                minSampleSize,
                gradingMode,
                topPickOnly ?? false,
                includeWatchlist ?? false,
                includeResearch ?? false,
                includeStale ?? false,
                includeExcluded ?? false,
                includeNoResultDiagnostics ?? false,
                includeDiagnostics ?? false,
                diagnosticSampleSize,
                playerIdentityAliasService,
                dbContext,
                cancellationToken);

            return Results.Ok(response);
        })
        .WithName("MarketsPerformanceSummary")
        .WithSummary("Production-safe read-only market recommendation performance summary.");

        endpoints.MapGet("/api/markets/performance-results", async (
            string? sport,
            DateOnly? date,
            bool? includeDiagnostics,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var marketSport = ResolveMarketSport(sport);
            if (marketSport.UnsupportedResponse is not null)
            {
                return marketSport.UnsupportedResponse;
            }

            var response = await BuildMarketPerformanceResultsResponseAsync(
                marketSport.RequestedSport,
                marketSport.EffectiveSport!,
                date,
                includeDiagnostics ?? false,
                dbContext,
                cancellationToken);

            return Results.Ok(response);
        })
        .WithName("MarketsPerformanceResults")
        .WithSummary("Production-safe read-only determined recommendation results for one completed slate date.");

        endpoints.MapGet("/api/markets/prop-history", async (
            string? sport,
            Guid? playerId,
            string? playerName,
            string? propType,
            string? side,
            decimal? line,
            int? take,
            int? season,
            string? provider,
            ApplicationDbContext dbContext,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await BuildMarketPropHistoryResponseAsync(
                sport,
                playerId,
                playerName,
                propType,
                side,
                line,
                take,
                season,
                provider,
                dbContext,
                playerIdentityAliasService,
                cancellationToken));
        })
        .WithName("MarketsPropHistory")
        .WithSummary("Production-safe read-only historical prop results for selected market lines.");

        endpoints.MapGet("/api/mlb/line-shopping", async (
            Guid? runId,
            int? take,
            bool? topOnly,
            int? freshnessMinutes,
            string? providerCategory,
            bool? includeBookmakerPrices,
            string? bookmakerPreset,
            ApplicationDbContext dbContext,
            IPredictionProbabilityService probabilityService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await BuildMarketLineShoppingResponseAsync(
                requestedSport: "MLB",
                effectiveSport: "MLB",
                runId,
                take,
                topOnly,
                freshnessMinutes,
                providerCategory,
                includeBookmakerPrices,
                bookmakerPreset,
                dbContext,
                probabilityService,
                cancellationToken));
        })
        .WithName("MlbLineShopping")
        .WithSummary("Compatibility wrapper for /api/markets/line-shopping?sport=MLB.");

        endpoints.MapGet("/api/dfs/entry-rules", () => Results.Ok(DfsEntryRulesCatalog.BuildResponse()))
            .WithName("DfsEntryRules")
            .WithSummary("Production-safe provider-scoped DFS entry compatibility rules.");

        return endpoints;
    }

    private static async Task<object> BuildMarketFreshnessResponseAsync(
        string? requestedSport,
        string effectiveSport,
        DateOnly? slateDate,
        string? bookmakerPreset,
        int? freshnessMinutes,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (IsSoccerSport(effectiveSport))
        {
            return await BuildGenericMarketFreshnessResponseAsync(
                requestedSport,
                effectiveSport,
                slateDate,
                bookmakerPreset,
                freshnessMinutes,
                dbContext,
                cancellationToken);
        }

        var response = await TheOddsApiDebugEndpoints.BuildMlbRefreshFreshnessResponseAsync(
            slateDate,
            bookmakerPreset,
            freshnessMinutes,
            dbContext,
            cancellationToken);
        var providerStatus = await BuildTheOddsProviderStatusAsync(
            freshnessMinutes,
            dbContext,
            cancellationToken);

        return new
        {
            response.GeneratedAtUtc,
            response.CallsExternalApis,
            response.ReadOnly,
            supportedSports = SupportedMarketSports,
            requestedSport,
            effectiveSport,
            limitation = MarketReadLimitation,
            latestSuccessfulRefresh = response.LatestSuccessfulRun,
            response.MinutesSinceLatestSuccessfulRefresh,
            response.IsFresh,
            response.IsStale,
            response.StaleReason,
            response.HasUsableMarketData,
            response.LineShoppingSnapshotCount,
            response.TopPickSnapshotCount,
            response.ActiveGroupCount,
            response.StaleOrExpiredGroupCount,
            response.ProviderIngestionFailureCount,
            response.ProviderIngestionSuccessCount,
            response.ProviderRejectedKeyDetected,
            response.UsabilityWarning,
            providerStatus = new
            {
                providerStatus.provider,
                providerStatus.status,
                providerStatus.latestRefreshRunId,
                providerStatus.latestRefreshHadUsableMarketData,
                providerStatus.requestsUsed,
                providerStatus.requestsRemaining,
                providerStatus.providerRejectedKeyDetected,
                providerStatus.creditExhaustionLikely,
                providerStatus.statusReason,
                providerStatus.recommendedAction
            },
            freshnessMinutesThreshold = response.FreshnessThresholdMinutes
        };
    }

    private static async Task<object> BuildGenericMarketFreshnessResponseAsync(
        string? requestedSport,
        string effectiveSport,
        DateOnly? slateDate,
        string? bookmakerPreset,
        int? freshnessMinutes,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var thresholdMinutes = Math.Clamp(freshnessMinutes ?? 30, 1, 1440);
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var refreshQuery = dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport);

        if (slateDate.HasValue)
        {
            refreshQuery = refreshQuery.Where(row => row.SlateDate == slateDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(bookmakerPreset))
        {
            refreshQuery = refreshQuery.Where(row => row.BookmakerPreset == bookmakerPreset);
        }

        var latestSuccessfulRun = await refreshQuery
            .Where(row => row.Succeeded && !row.DryRun)
            .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var snapshotRunId = latestSuccessfulRun?.Id;
        var lineShoppingQuery = dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport);
        var topPickQuery = dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport);

        if (snapshotRunId.HasValue)
        {
            lineShoppingQuery = lineShoppingQuery.Where(row => row.TheOddsRefreshRunId == snapshotRunId.Value);
            topPickQuery = topPickQuery.Where(row => row.TheOddsRefreshRunId == snapshotRunId.Value);
        }

        var lineShoppingSnapshotCount = await lineShoppingQuery.CountAsync(cancellationToken);
        var topPickSnapshotCount = await topPickQuery.CountAsync(cancellationToken);
        var activeGroupCount = await lineShoppingQuery.CountAsync(
            row => row.LifecycleStatus == "Active" && !row.IsStale && !row.IsExpired,
            cancellationToken);
        var staleOrExpiredGroupCount = await lineShoppingQuery.CountAsync(
            row => row.IsStale || row.IsExpired || row.LifecycleStatus != "Active",
            cancellationToken);
        var latestCompletedOrStarted = latestSuccessfulRun?.CompletedAtUtc ?? latestSuccessfulRun?.StartedAtUtc;
        var minutesSinceLatest = latestCompletedOrStarted.HasValue
            ? Math.Round((generatedAtUtc - latestCompletedOrStarted.Value).TotalMinutes, 1)
            : (double?)null;
        var hasUsableMarketData = topPickSnapshotCount > 0 || activeGroupCount > 0;
        var isFresh = minutesSinceLatest.HasValue && minutesSinceLatest.Value <= thresholdMinutes;

        return new
        {
            generatedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            supportedCompetitions = BuildSupportedCompetitionDiagnostics(),
            requestedSport,
            effectiveSport,
            competition = ResolveCompetitionName(effectiveSport),
            resultGradingSupported = false,
            limitation = MarketReadLimitation,
            latestSuccessfulRefresh = latestSuccessfulRun is null
                ? null
                : new
                {
                    latestSuccessfulRun.Id,
                    latestSuccessfulRun.StartedAtUtc,
                    latestSuccessfulRun.CompletedAtUtc,
                    latestSuccessfulRun.Succeeded,
                    latestSuccessfulRun.Failed,
                    latestSuccessfulRun.Blocked,
                    latestSuccessfulRun.DryRun,
                    latestSuccessfulRun.ForceLive,
                    latestSuccessfulRun.BookmakerPreset
                },
            minutesSinceLatestSuccessfulRefresh = minutesSinceLatest,
            isFresh,
            isStale = !isFresh,
            staleReason = isFresh ? null : latestSuccessfulRun is null ? $"No persisted {ResolveSportDisplayName(effectiveSport)} refresh snapshots exist yet." : $"Latest persisted {ResolveSportDisplayName(effectiveSport)} refresh is older than {thresholdMinutes} minutes.",
            hasUsableMarketData,
            lineShoppingSnapshotCount,
            topPickSnapshotCount,
            activeGroupCount,
            staleOrExpiredGroupCount,
            providerIngestionFailureCount = 0,
            providerIngestionSuccessCount = hasUsableMarketData ? 1 : 0,
            providerRejectedKeyDetected = false,
            usabilityWarning = IsSoccerSport(effectiveSport) ? SoccerHistoryUnavailableMessage : null,
            diagnostics = new
            {
                supportedSports = SupportedMarketSports,
                supportedCompetitions = SupportedMarketCompetitions,
                soccerMarketsDiscovered = IsSoccerSport(effectiveSport) ? lineShoppingSnapshotCount + topPickSnapshotCount : 0,
                soccerMarketsMapped = IsSoccerSport(effectiveSport) ? activeGroupCount + topPickSnapshotCount : 0,
                soccerCurrentLineCounts = IsSoccerSport(effectiveSport) ? activeGroupCount : 0,
                soccerResultGradingSupported = false
            },
            freshnessMinutesThreshold = thresholdMinutes
        };
    }

    private static async Task<object> BuildMarketProviderStatusResponseAsync(
        int? freshnessMinutes,
        ITheOddsRefreshPlannerService planner,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var providerStatus = await BuildTheOddsProviderStatusAsync(
            freshnessMinutes,
            dbContext,
            cancellationToken);
        var plan = await planner.BuildPlanAsync(cancellationToken);

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            callsExternalApis = false,
            readOnly = true,
            providers = new object[]
            {
                providerStatus with
                {
                    budgetAllowed = plan.Budget.BudgetAllowed,
                    budgetBlockReason = plan.Budget.BudgetBlockReason,
                    budgetExplanation = plan.Budget.BudgetExplanation,
                    plannerEnforcementEnabled = plan.Budget.PlannerEnforcementEnabled,
                    localBudgetEnforcementEnabled = plan.Budget.LocalBudgetEnforcementEnabled,
                    dailyBudgetEnforcementEnabled = plan.Budget.LocalDailyBudgetEnforcementEnabled,
                    monthlyBudgetEnforcementEnabled = plan.Budget.LocalMonthlyBudgetEnforcementEnabled,
                    localDailyBudgetEnforcementEnabled = plan.Budget.LocalDailyBudgetEnforcementEnabled,
                    localMonthlyBudgetEnforcementEnabled = plan.Budget.LocalMonthlyBudgetEnforcementEnabled,
                    perRunEstimateBlockingEnabled = plan.Budget.PerRunEstimateBlockingEnabled,
                    providerCreditBlockingEnabled = plan.Budget.ProviderCreditBlockingEnabled,
                    dailyBudgetEnabled = plan.Budget.DailyBudgetEnabled,
                    maxDailyCredits = plan.Budget.ConfiguredDailyCreditBudget,
                    maxEstimatedCallsPerRun = plan.Budget.ConfiguredMaxEstimatedCallsPerRun,
                    legacyConfiguredMaxDailyCredits = plan.Budget.ConfiguredDailyCreditBudget,
                    legacyConfiguredMaxMonthlyCredits = plan.Budget.ConfiguredMonthlyCreditBudget,
                    monthlyBudgetAllowed = !plan.Budget.LocalMonthlyBudgetEnforcementEnabled || !plan.Budget.MonthlyHardStop,
                    localBudgetExceeded = plan.Budget.IsBlockedByLocalBudget,
                    configuredMonthlyCreditLimit = plan.Budget.ConfiguredMonthlyCreditBudget,
                    configuredReservedCreditBuffer = plan.Budget.ConfiguredReservedCreditBuffer,
                    providerReportedRequestsUsed = plan.Budget.ProviderReportedRequestsUsed,
                    providerReportedRequestsRemaining = plan.Budget.ProviderReportedRequestsRemaining,
                    providerCreditTelemetryAgeMinutes = plan.Budget.ProviderCreditTelemetryAgeMinutes,
                    providerTelemetryUsedForBlocking = plan.Budget.ProviderTelemetryUsedForBlocking,
                    localBudgetUsedEstimate = plan.Budget.MonthlyUsedCredits,
                    localBudgetRemainingEstimate = Math.Max(0, plan.Budget.ConfiguredMonthlyCreditBudget - plan.Budget.MonthlyAfterEstimate),
                    isProviderTelemetryStale = plan.Budget.IsProviderTelemetryStale,
                    isBlockedByProviderCredits = plan.Budget.IsBlockedByProviderCredits,
                    isBlockedByLocalBudget = plan.Budget.IsBlockedByLocalBudget,
                    isBlockedByPerRunEstimate = plan.Budget.IsBlockedByPerRunEstimate,
                    plannerWouldBlockIfEnforced = plan.Budget.PlannerWouldBlockIfEnforced,
                    plannerWarnings = plan.Budget.PlannerWarnings
                }
            }
        };
    }

    private static async Task<object> BuildMarketRefreshPlanResponseAsync(
        bool persistDecision,
        ITheOddsRefreshPlannerService planner,
        CancellationToken cancellationToken)
    {
        var plan = await planner.BuildPlanAsync(cancellationToken);
        TheOddsRefreshPlannerDecision? persistedDecision = null;
        if (persistDecision)
        {
            persistedDecision = await planner.PersistDecisionAsync(plan, "ManualPlan", cancellationToken);
        }

        return new
        {
            generatedAtUtc = plan.GeneratedAtUtc,
            callsExternalApis = false,
            readOnly = !persistDecision,
            decisionPersisted = persistedDecision is not null,
            decisionId = persistedDecision?.Id,
            providerStatus = new
            {
                provider = "The Odds API",
                status = plan.ProviderStatus,
                requestsUsed = plan.RequestsUsed,
                requestsRemaining = plan.RequestsRemaining,
                providerRejectedKeyDetected = plan.ProviderRejectedKeyDetected,
                providerAvailable = plan.ProviderAvailable,
                providerReportedRequestsUsed = plan.Budget.ProviderReportedRequestsUsed,
                providerReportedRequestsRemaining = plan.Budget.ProviderReportedRequestsRemaining,
                providerCreditTelemetryAgeMinutes = plan.Budget.ProviderCreditTelemetryAgeMinutes,
                isProviderTelemetryStale = plan.Budget.IsProviderTelemetryStale
            },
            budgetSummary = plan.Budget,
            plans = new[]
            {
                new
                {
                    sport = "MLB",
                    plan.SlateDate,
                    provider = "The Odds API",
                    plan.BookmakerPreset,
                    supportedSports = SupportedMarketSports,
                    plan.EstimatedSlateEvents,
                    plan.EstimatedApiCalls,
                    plan.EstimateSource,
                    cadenceRule = plan.CadenceRule,
                    plan.MinutesUntilNextEventStart,
                    plan.LastSuccessfulRefreshAtUtc,
                    plan.MinutesSinceLastSuccessfulRefresh,
                    plan.LastAttemptAtUtc,
                    plan.LastFailedAttemptAtUtc,
                    plan.MinutesSinceLastAttempt,
                    plan.ShouldRefresh,
                    plan.SkipReason,
                    plan.NextEligibleRefreshUtc,
                    budgetAllowed = plan.Budget.BudgetAllowed,
                    budgetBlockReason = plan.Budget.BudgetBlockReason,
                    budgetExplanation = plan.Budget.BudgetExplanation,
                    plannerEnforcementEnabled = plan.Budget.PlannerEnforcementEnabled,
                    maxEstimatedCallsPerRun = plan.Budget.ConfiguredMaxEstimatedCallsPerRun,
                    localBudgetEnforcementEnabled = plan.Budget.LocalBudgetEnforcementEnabled,
                    dailyBudgetEnforcementEnabled = plan.Budget.LocalDailyBudgetEnforcementEnabled,
                    monthlyBudgetEnforcementEnabled = plan.Budget.LocalMonthlyBudgetEnforcementEnabled,
                    localDailyBudgetEnforcementEnabled = plan.Budget.LocalDailyBudgetEnforcementEnabled,
                    localMonthlyBudgetEnforcementEnabled = plan.Budget.LocalMonthlyBudgetEnforcementEnabled,
                    perRunEstimateBlockingEnabled = plan.Budget.PerRunEstimateBlockingEnabled,
                    providerCreditBlockingEnabled = plan.Budget.ProviderCreditBlockingEnabled,
                    dailyBudgetEnabled = plan.Budget.DailyBudgetEnabled,
                    maxDailyCredits = plan.Budget.ConfiguredDailyCreditBudget,
                    legacyConfiguredMaxDailyCredits = plan.Budget.ConfiguredDailyCreditBudget,
                    legacyConfiguredMaxMonthlyCredits = plan.Budget.ConfiguredMonthlyCreditBudget,
                    monthlyBudgetAllowed = !plan.Budget.LocalMonthlyBudgetEnforcementEnabled || !plan.Budget.MonthlyHardStop,
                    localBudgetExceeded = plan.Budget.IsBlockedByLocalBudget,
                    configuredMonthlyCreditLimit = plan.Budget.ConfiguredMonthlyCreditBudget,
                    configuredReservedCreditBuffer = plan.Budget.ConfiguredReservedCreditBuffer,
                    providerReportedRequestsUsed = plan.Budget.ProviderReportedRequestsUsed,
                    providerReportedRequestsRemaining = plan.Budget.ProviderReportedRequestsRemaining,
                    providerCreditTelemetryAgeMinutes = plan.Budget.ProviderCreditTelemetryAgeMinutes,
                    providerTelemetryUsedForBlocking = plan.Budget.ProviderTelemetryUsedForBlocking,
                    localBudgetUsedEstimate = plan.Budget.MonthlyUsedCredits,
                    localBudgetRemainingEstimate = Math.Max(0, plan.Budget.ConfiguredMonthlyCreditBudget - plan.Budget.MonthlyAfterEstimate),
                    isProviderTelemetryStale = plan.Budget.IsProviderTelemetryStale,
                    isBlockedByProviderCredits = plan.Budget.IsBlockedByProviderCredits,
                    isBlockedByLocalBudget = plan.Budget.IsBlockedByLocalBudget,
                    isBlockedByPerRunEstimate = plan.Budget.IsBlockedByPerRunEstimate,
                    plannerWouldBlockIfEnforced = plan.Budget.PlannerWouldBlockIfEnforced,
                    plannerWarnings = plan.Budget.PlannerWarnings,
                    plan.ProviderAvailable,
                    hasUsableMarketData = plan.HasUsableMarketData,
                    plan.RecommendedAction
                }
            }
        };
    }

    private static async Task<TheOddsProviderStatusReadModel> BuildTheOddsProviderStatusAsync(
        int? freshnessMinutes,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var thresholdMinutes = Math.Clamp(freshnessMinutes ?? 30, 1, 1440);
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var latestRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API" && row.Sport == "MLB")
            .OrderByDescending(row => row.StartedAtUtc)
            .ThenByDescending(row => row.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestRun is null)
        {
            return new TheOddsProviderStatusReadModel(
                provider: "The Odds API",
                status: "Unknown",
                hasRecentRefresh: false,
                latestRefreshRunId: null,
                latestRefreshStartedAtUtc: null,
                latestRefreshCompletedAtUtc: null,
                latestRefreshSucceeded: false,
                latestRefreshFailed: false,
                latestRefreshHadUsableMarketData: false,
                latestBookmakerPreset: null,
                latestProviderCategoriesIncluded: [],
                actualApiCalls: 0,
                estimatedApiCalls: 0,
                requestsUsed: null,
                requestsRemaining: null,
                requestsLast: null,
                providerRejectedKeyDetected: false,
                creditExhaustionLikely: false,
                providerIngestionSuccessCount: 0,
                providerIngestionFailureCount: 0,
                lastErrorMessage: null,
                statusReason: "No The Odds API refresh metadata exists yet.",
                recommendedAction: "Run a dry-run refresh to inspect the planned provider call shape before any live refresh.",
                lineShoppingSnapshotCount: 0,
                topPickSnapshotCount: 0,
                activeGroupCount: 0,
                staleOrExpiredGroupCount: 0);
        }

        var lineShoppingSnapshotCount = await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .CountAsync(row => row.TheOddsRefreshRunId == latestRun.Id, cancellationToken);
        var topPickSnapshotCount = await dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .CountAsync(row => row.TheOddsRefreshRunId == latestRun.Id, cancellationToken);
        var activeGroupCount = await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .CountAsync(row => row.TheOddsRefreshRunId == latestRun.Id
                && row.LifecycleStatus == "Active"
                && !row.IsStale
                && !row.IsExpired, cancellationToken);
        var staleOrExpiredGroupCount = await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .CountAsync(row => row.TheOddsRefreshRunId == latestRun.Id
                && (row.IsStale || row.IsExpired || row.LifecycleStatus != "Active"), cancellationToken);

        var diagnostics = ExtractProviderStatusDiagnostics(latestRun.DiagnosticsJson);
        var latestCompletedOrStarted = latestRun.CompletedAtUtc ?? latestRun.StartedAtUtc;
        var hasRecentRefresh = generatedAtUtc - latestCompletedOrStarted <= TimeSpan.FromMinutes(thresholdMinutes);
        var latestRefreshHadUsableMarketData = topPickSnapshotCount > 0 || activeGroupCount > 0;
        var creditExhaustionLikely = diagnostics.RequestsRemaining.HasValue && diagnostics.RequestsRemaining.Value <= 0;
        var creditLow = diagnostics.RequestsRemaining.HasValue && diagnostics.RequestsRemaining.Value <= 5;

        var (status, statusReason, recommendedAction) = BuildTheOddsProviderStatusDecision(
            latestRun,
            latestRefreshHadUsableMarketData,
            diagnostics.ProviderRejectedKeyDetected,
            creditExhaustionLikely,
            creditLow);

        return new TheOddsProviderStatusReadModel(
            provider: "The Odds API",
            status,
            hasRecentRefresh,
            latestRefreshRunId: latestRun.Id,
            latestRefreshStartedAtUtc: latestRun.StartedAtUtc,
            latestRefreshCompletedAtUtc: latestRun.CompletedAtUtc,
            latestRefreshSucceeded: latestRun.Succeeded,
            latestRefreshFailed: latestRun.Failed,
            latestRefreshHadUsableMarketData,
            latestBookmakerPreset: latestRun.BookmakerPreset,
            latestProviderCategoriesIncluded: SplitCsv(latestRun.ProviderCategoriesIncluded),
            actualApiCalls: latestRun.ActualApiCalls,
            estimatedApiCalls: latestRun.EstimatedApiCalls,
            requestsUsed: diagnostics.RequestsUsed,
            requestsRemaining: diagnostics.RequestsRemaining,
            requestsLast: diagnostics.RequestsLast,
            providerRejectedKeyDetected: diagnostics.ProviderRejectedKeyDetected,
            creditExhaustionLikely,
            providerIngestionSuccessCount: diagnostics.ProviderIngestionSuccessCount,
            providerIngestionFailureCount: diagnostics.ProviderIngestionFailureCount,
            lastErrorMessage: latestRun.ErrorMessage,
            statusReason,
            recommendedAction,
            lineShoppingSnapshotCount,
            topPickSnapshotCount,
            activeGroupCount,
            staleOrExpiredGroupCount);
    }

    private static async Task<object> BuildMarketTopPicksResponseAsync(
        string? requestedSport,
        string effectiveSport,
        Guid? runId,
        int? take,
        int? freshnessMinutes,
        string? providerCategory,
        string? bookmakerPreset,
        ApplicationDbContext dbContext,
        IPredictionProbabilityService probabilityService,
        CancellationToken cancellationToken)
    {
        if (IsSoccerSport(effectiveSport))
        {
            return await BuildGenericMarketTopPicksResponseAsync(
                requestedSport,
                effectiveSport,
                runId,
                take,
                providerCategory,
                dbContext,
                cancellationToken);
        }

        var freshness = await TheOddsApiDebugEndpoints.BuildMlbRefreshFreshnessResponseAsync(
            slateDate: null,
            bookmakerPreset,
            freshnessMinutes,
            dbContext,
            cancellationToken);
        var topPicks = await TheOddsApiDebugEndpoints.BuildMlbTopPicksResponseAsync(
            runId,
            take,
            freshnessMinutes,
            providerCategory,
            dbContext,
            probabilityService,
            cancellationToken);

        return new
        {
            generatedAtUtc = topPicks.GeneratedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            requestedSport,
            effectiveSport,
            limitation = MarketReadLimitation,
            freshness = ToPublicFreshnessSummary(freshness),
            topPicks.SnapshotBacked,
            refreshRunId = topPicks.RefreshRunId,
            topPicks.FallbackReason,
            topPicks.Run,
            topPicks.Summary,
            topPicks.StrategyClassificationSummary,
            topPicks.TopPicks
        };
    }

    private static async Task<object> BuildGenericMarketTopPicksResponseAsync(
        string? requestedSport,
        string effectiveSport,
        Guid? runId,
        int? take,
        string? providerCategory,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var requestedTake = Math.Clamp(take ?? 25, 1, 250);
        var latestRunId = runId ?? await ResolveLatestTopPickSnapshotRunIdAsync(
            dbContext,
            effectiveSport,
            cancellationToken);
        var query = dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport);

        if (latestRunId.HasValue)
        {
            query = query.Where(row => row.TheOddsRefreshRunId == latestRunId.Value);
        }

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.ProviderCategory == providerCategory);
        }

        var rows = await query
            .Where(row => row.LifecycleStatus == "Active" && !row.IsStale && !row.IsExpired)
            .OrderByDescending(row => row.BestEstimatedEv)
            .ThenByDescending(row => row.BestProbabilityEdge)
            .ThenBy(row => row.Player)
            .Take(requestedTake)
            .ToArrayAsync(cancellationToken);

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            supportedCompetitions = BuildSupportedCompetitionDiagnostics(),
            requestedSport,
            effectiveSport,
            competition = ResolveCompetitionName(effectiveSport),
            resultGradingSupported = false,
            limitation = MarketReadLimitation,
            freshness = await BuildGenericMarketFreshnessResponseAsync(requestedSport, effectiveSport, null, null, null, dbContext, cancellationToken),
            snapshotBacked = rows.Length > 0,
            refreshRunId = latestRunId,
            fallbackReason = rows.Length == 0 ? $"No persisted {ResolveSportDisplayName(effectiveSport)} top-pick snapshots are available yet." : null,
            run = new
            {
                runId = latestRunId,
                predictionCount = rows.Length
            },
            summary = new
            {
                activeCandidateCount = rows.Length,
                actionableCandidateCount = rows.Count(row => row.EntryActionableForUser),
                returnedTopPicks = rows.Length,
                totalActiveComparisonGroups = rows.Length
            },
            strategyClassificationSummary = rows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.StrategyClassification) ? "Unclassified" : row.StrategyClassification)
                .Select(group => new { strategyClassification = group.Key, count = group.Count() })
                .ToArray(),
            topPicks = rows
                .Select(row => BuildSnapshotDetails(row.DetailsJson, row.Sport, row.EventName, row.EventStartTime))
                .ToArray()
        };
    }

    private static async Task<object> BuildMarketLineShoppingResponseAsync(
        string? requestedSport,
        string effectiveSport,
        Guid? runId,
        int? take,
        bool? topOnly,
        int? freshnessMinutes,
        string? providerCategory,
        bool? includeBookmakerPrices,
        string? bookmakerPreset,
        ApplicationDbContext dbContext,
        IPredictionProbabilityService probabilityService,
        CancellationToken cancellationToken)
    {
        if (IsSoccerSport(effectiveSport))
        {
            return await BuildGenericMarketLineShoppingResponseAsync(
                requestedSport,
                effectiveSport,
                runId,
                take,
                topOnly,
                providerCategory,
                dbContext,
                cancellationToken);
        }

        var freshness = await TheOddsApiDebugEndpoints.BuildMlbRefreshFreshnessResponseAsync(
            slateDate: null,
            bookmakerPreset,
            freshnessMinutes,
            dbContext,
            cancellationToken);
        var lineShopping = await TheOddsApiDebugEndpoints.BuildMlbLineShoppingResponseAsync(
            runId,
            summaryOnly: false,
            topOnly: topOnly ?? true,
            take ?? 25,
            includeBookmakerPrices ?? false,
            freshnessMinutes,
            providerCategory,
            dbContext,
            probabilityService,
            cancellationToken);

        return new
        {
            generatedAtUtc = lineShopping.GeneratedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            requestedSport,
            effectiveSport,
            limitation = MarketReadLimitation,
            freshness = ToPublicFreshnessSummary(freshness),
            lineShopping.SnapshotBacked,
            refreshRunId = lineShopping.RefreshRunId,
            lineShopping.FallbackReason,
            lineShopping.Run,
            lineShopping.Summary,
            lineShopping.StrategyClassificationSummary,
            lineShopping.ComparisonGroups
        };
    }

    private static async Task<object> BuildGenericMarketLineShoppingResponseAsync(
        string? requestedSport,
        string effectiveSport,
        Guid? runId,
        int? take,
        bool? topOnly,
        string? providerCategory,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var requestedTake = Math.Clamp(take ?? 50, 1, 500);
        var latestRunId = runId ?? await ResolveLatestLineShoppingSnapshotRunIdAsync(
            dbContext,
            effectiveSport,
            cancellationToken);
        var query = dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport);

        if (latestRunId.HasValue)
        {
            query = query.Where(row => row.TheOddsRefreshRunId == latestRunId.Value);
        }

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.ProviderCategory == providerCategory);
        }

        if (topOnly ?? true)
        {
            query = query.Where(row => row.IsCandidate || row.EntryActionableForUser);
        }

        var rows = await query
            .Where(row => row.LifecycleStatus == "Active" && !row.IsStale && !row.IsExpired)
            .OrderByDescending(row => row.BestEstimatedEv)
            .ThenByDescending(row => row.BestProbabilityEdge)
            .ThenBy(row => row.Player)
            .Take(requestedTake)
            .ToArrayAsync(cancellationToken);

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            supportedCompetitions = BuildSupportedCompetitionDiagnostics(),
            requestedSport,
            effectiveSport,
            competition = ResolveCompetitionName(effectiveSport),
            resultGradingSupported = false,
            limitation = MarketReadLimitation,
            freshness = await BuildGenericMarketFreshnessResponseAsync(requestedSport, effectiveSport, null, null, null, dbContext, cancellationToken),
            snapshotBacked = rows.Length > 0,
            refreshRunId = latestRunId,
            fallbackReason = rows.Length == 0 ? $"No persisted {ResolveSportDisplayName(effectiveSport)} line-shopping snapshots are available yet." : null,
            run = new
            {
                runId = latestRunId,
                comparisonGroupCount = rows.Length
            },
            summary = new
            {
                totalComparisonGroups = rows.Length,
                totalActiveComparisonGroups = rows.Length
            },
            strategyClassificationSummary = rows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.StrategyClassification) ? "Unclassified" : row.StrategyClassification)
                .Select(group => new { strategyClassification = group.Key, count = group.Count() })
                .ToArray(),
            comparisonGroups = rows
                .Select(row => BuildSnapshotDetails(row.DetailsJson, row.Sport, row.EventName, row.EventStartTime))
                .ToArray()
        };
    }

    private static MarketSportResolution ResolveMarketSport(string? sport)
    {
        var requestedSport = string.IsNullOrWhiteSpace(sport)
            ? null
            : NormalizeMarketSportCode(sport);
        var effectiveSport = requestedSport ?? "MLB";

        if (!SupportedMarketSports.Contains(effectiveSport, StringComparer.OrdinalIgnoreCase))
        {
            return new MarketSportResolution(
                requestedSport,
                EffectiveSport: null,
                Results.BadRequest(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    supportedSports = SupportedMarketSports,
                    requestedSport,
                    effectiveSport = (string?)null,
                    supportedCompetitions = BuildSupportedCompetitionDiagnostics(),
                    limitation = MarketReadLimitation,
                    error = $"Sport '{requestedSport}' is not implemented for production market reads yet."
                }));
        }

        return new MarketSportResolution(requestedSport, effectiveSport, UnsupportedResponse: null);
    }

    private static string NormalizeMarketSportCode(string? sport)
    {
        var normalized = sport?.Trim().ToUpperInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            null or "" => "MLB",
            "MLB" => "MLB",
            "SOCCER" or "FOOTBALL" or "FUTBOL" or "WORLD_CUP" or "WORLDCUP" or "FIFA_WORLD_CUP" => "SOCCER",
            _ => normalized
        };
    }

    private static bool IsSoccerSport(string? sport)
    {
        return string.Equals(sport, "SOCCER", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSportDisplayName(string? sport)
    {
        return IsSoccerSport(sport) ? "Soccer / World Cup" : "MLB";
    }

    private static string? ResolveCompetitionName(string? sport)
    {
        return IsSoccerSport(sport) ? SoccerCompetitionName : string.Equals(sport, "MLB", StringComparison.OrdinalIgnoreCase) ? "MLB" : null;
    }

    private static object[] BuildSupportedCompetitionDiagnostics()
    {
        return
        [
            new { sport = "MLB", competition = "MLB", resultGradingSupported = true },
            new { sport = "SOCCER", competition = SoccerCompetitionName, resultGradingSupported = false }
        ];
    }

    private static JsonElement BuildSnapshotDetails(JsonDocument detailsJson, string sport, string? eventName, DateTimeOffset? eventStartTime)
    {
        JsonObject details;
        try
        {
            details = JsonNode.Parse(detailsJson.RootElement.GetRawText()) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            details = [];
        }

        details["sport"] = sport;
        details["Sport"] = sport;
        var competition = ResolveCompetitionName(sport);
        if (!string.IsNullOrWhiteSpace(competition))
        {
            details["competition"] = competition;
            details["Competition"] = competition;
        }

        if (!string.IsNullOrWhiteSpace(eventName) && details["eventName"] is null)
        {
            details["eventName"] = eventName;
        }

        if (eventStartTime.HasValue && details["eventStartTimeUtc"] is null)
        {
            details["eventStartTimeUtc"] = eventStartTime.Value;
        }

        using var document = JsonSerializer.SerializeToDocument(details);
        return document.RootElement.Clone();
    }

    private static async Task<Guid?> ResolveLatestTopPickSnapshotRunIdAsync(
        ApplicationDbContext dbContext,
        string effectiveSport,
        CancellationToken cancellationToken)
    {
        return await dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport)
            .OrderByDescending(row => row.GeneratedAtUtc)
            .Select(row => (Guid?)row.TheOddsRefreshRunId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<Guid?> ResolveLatestLineShoppingSnapshotRunIdAsync(
        ApplicationDbContext dbContext,
        string effectiveSport,
        CancellationToken cancellationToken)
    {
        return await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport)
            .OrderByDescending(row => row.GeneratedAtUtc)
            .Select(row => (Guid?)row.TheOddsRefreshRunId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool ResolveEffectiveCandidateOnly(bool? candidateOnly, bool? includeWatchlist, bool? includeResearch)
    {
        return candidateOnly ?? !(includeWatchlist == true || includeResearch == true);
    }

    private static async Task<object> BuildMarketPerformanceSummaryResponseAsync(
        string? requestedSport,
        string effectiveSport,
        string? providerCategory,
        string? provider,
        string? bookmaker,
        string? propType,
        string? strategyClassification,
        string? strategyTier,
        bool candidateOnly,
        DateOnly? fromDate,
        DateOnly? toDate,
        DateOnly? startDate,
        DateOnly? endDate,
        int? minSampleSize,
        string? gradingMode,
        bool topPickOnly,
        bool includeWatchlist,
        bool includeResearch,
        bool includeStale,
        bool includeExcluded,
        bool includeNoResultDiagnostics,
        bool includeDiagnostics,
        int? diagnosticSampleSize,
        IPlayerIdentityAliasService playerIdentityAliasService,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var normalizedGradingMode = NormalizePerformanceGradingMode(gradingMode);
        if (IsSoccerSport(effectiveSport))
        {
            return BuildUnsupportedPerformanceSummaryResponse(
                generatedAtUtc,
                requestedSport,
                effectiveSport,
                normalizedGradingMode);
        }

        var latestRefresh = await TheOddsApiDebugEndpoints.BuildMlbRefreshFreshnessResponseAsync(
            slateDate: null,
            bookmakerPreset: null,
            freshnessMinutes: null,
            dbContext,
            cancellationToken);
        var ledgerResponse = await TryBuildLedgerPerformanceSummaryResponseAsync(
            requestedSport,
            effectiveSport,
            providerCategory,
            provider,
            bookmaker,
            propType,
            strategyClassification,
            strategyTier,
            candidateOnly,
            fromDate,
            toDate,
            startDate,
            endDate,
            minSampleSize,
            normalizedGradingMode,
            latestRefresh,
            topPickOnly,
            includeWatchlist,
            includeResearch,
            includeStale,
            includeExcluded,
            includeNoResultDiagnostics,
            includeDiagnostics,
            diagnosticSampleSize,
            dbContext,
            cancellationToken);
        if (ledgerResponse is not null)
        {
            return ledgerResponse;
        }

        var snapshotQuery = dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport);

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            snapshotQuery = snapshotQuery.Where(row => row.ProviderCategory == providerCategory);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            snapshotQuery = snapshotQuery.Where(row => row.Provider == provider || row.EntryProvider == provider);
        }

        if (!string.IsNullOrWhiteSpace(bookmaker))
        {
            snapshotQuery = snapshotQuery.Where(row => row.Bookmaker == bookmaker || row.BestBookmaker == bookmaker);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            snapshotQuery = snapshotQuery.Where(row => row.PropType == propType);
        }

        if (!string.IsNullOrWhiteSpace(strategyClassification))
        {
            snapshotQuery = snapshotQuery.Where(row => row.StrategyClassification == strategyClassification);
        }

        if (!string.IsNullOrWhiteSpace(strategyTier))
        {
            snapshotQuery = snapshotQuery.Where(row => row.StrategyTier == strategyTier);
        }

        if (topPickOnly)
        {
            snapshotQuery = snapshotQuery.Where(row => row.IsCandidate && row.StrategyClassification == "TopPick");
        }
        else if (candidateOnly)
        {
            snapshotQuery = snapshotQuery.Where(row => row.IsCandidate);
        }
        else if (!includeResearch)
        {
            snapshotQuery = snapshotQuery.Where(row => row.IsCandidate || (includeWatchlist && row.IsWatchlist));
        }

        if (!includeWatchlist)
        {
            snapshotQuery = snapshotQuery.Where(row => !row.IsWatchlist);
        }

        if (!includeStale)
        {
            snapshotQuery = snapshotQuery.Where(row =>
                row.EntryActionableForUser &&
                row.LifecycleStatus == "Active");
        }

        if (fromDate.HasValue)
        {
            snapshotQuery = snapshotQuery.Where(row => row.SlateDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            snapshotQuery = snapshotQuery.Where(row => row.SlateDate <= toDate.Value);
        }

        var snapshots = await snapshotQuery
            .OrderByDescending(row => row.GeneratedAtUtc)
            .Take(5000)
            .ToListAsync(cancellationToken);
        var snapshotPlayerResolutions = await ResolvePerformanceSnapshotPlayerIdsAsync(
            dbContext,
            snapshots,
            cancellationToken);
        var scoredSnapshots = snapshots
            .Select(row =>
            {
                var resolution = snapshotPlayerResolutions.TryGetValue(row.Id, out var value)
                    ? value
                    : new PerformanceSnapshotPlayerResolution(null, "NormalizedNameFallback", "Snapshot player id was not available.");
                return new PerformancePickRow(
                    row,
                    ExtractHistoricalSampleSize(row.DetailsJson),
                    resolution.PlayerId,
                    resolution.PlayerId.HasValue ? [resolution.PlayerId.Value] : [],
                    resolution.LookupMode,
                    resolution.FailureReason);
            })
            .Where(row => !minSampleSize.HasValue || row.HistoricalSampleSize >= minSampleSize.Value)
            .ToArray();
        var equivalentPlayerIdsByResolvedId = await LoadPerformanceEquivalentPlayerIdsAsync(
            scoredSnapshots,
            playerIdentityAliasService,
            cancellationToken);
        scoredSnapshots = scoredSnapshots
            .Select(row => row.ResolvedPlayerId.HasValue
                ? row with { EquivalentPlayerIds = equivalentPlayerIdsByResolvedId.GetValueOrDefault(row.ResolvedPlayerId.Value, [row.ResolvedPlayerId.Value]) }
                : row)
            .ToArray();

        var resultDates = scoredSnapshots.Select(row => row.Snapshot.SlateDate).Distinct().ToArray();
        var diagnosticResultDates = resultDates
            .SelectMany(date => new[] { date.AddDays(-1), date, date.AddDays(1) })
            .Distinct()
            .ToArray();
        var actualResultRows = diagnosticResultDates.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Include(row => row.Player)
                .Include(row => row.PropType)
                .Where(row => diagnosticResultDates.Contains(row.EventDate))
                .Select(row => new ActualResultLookupRow(
                    row.EventDate,
                    row.PlayerId,
                    row.Player == null ? string.Empty : row.Player.DisplayName,
                    row.PropTypeId,
                    row.PropType == null ? string.Empty : row.PropType.Name,
                    row.ActualResultValue))
                .ToListAsync(cancellationToken);
        var actualResultsByPlayerId = actualResultRows
            .GroupBy(row => new ActualResultPlayerLookupKey(
                row.EventDate,
                row.PlayerId,
                NormalizePerformancePropTypeKey(row.PropType)))
            .Where(group => !string.IsNullOrWhiteSpace(group.Key.PropType))
            .ToDictionary(group => group.Key, group => group.First().ActualResultValue);
        var gradedRows = scoredSnapshots
            .Select(row => GradePerformancePick(row, actualResultsByPlayerId, actualResultRows))
            .ToArray();
        var dateDiagnostics = gradedRows
            .GroupBy(row => row.Snapshot.SlateDate)
            .Select(group =>
            {
                var rows = group.ToArray();
                var gradedCount = rows.Count(row => IsGradedOutcome(row.Outcome));
                var resultRowsForDate = actualResultRows
                    .Where(row => row.EventDate == group.Key)
                    .ToArray();
                return new PerformanceDateDiagnostics(
                    group.Key,
                    rows.Length,
                    gradedCount,
                    rows.Length - gradedCount,
                    resultRowsForDate.Length,
                    resultRowsForDate.Select(row => row.PlayerId).Distinct().Count(),
                    resultRowsForDate.Select(row => NormalizePerformancePropTypeKey(row.PropType)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    rows
                        .Where(row => !IsGradedOutcome(row.Outcome))
                        .GroupBy(row => string.IsNullOrWhiteSpace(row.MatchFailureReason) ? "Unknown" : row.MatchFailureReason!)
                        .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.Count()),
                    BuildPerformanceDateMatchStrategy(rows));
            })
            .OrderByDescending(row => row.SlateDate)
            .ToArray();
        var latestSnapshotDate = scoredSnapshots.Select(row => (DateOnly?)row.Snapshot.SlateDate).Max();
        var latestResultsImportDate = await dbContext.PropResults
            .AsNoTracking()
            .Select(row => (DateOnly?)row.EventDate)
            .MaxAsync(cancellationToken);
        var latestCompletedDate = latestResultsImportDate;
        var latestCompletedPropResultsCount = latestCompletedDate.HasValue
            ? await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(row => row.EventDate == latestCompletedDate.Value, cancellationToken)
            : 0;
        var latestGradedDate = dateDiagnostics
            .Where(row => row.GradedPicks > 0)
            .Select(row => (DateOnly?)row.SlateDate)
            .Max();
        var selectedDate = normalizedGradingMode switch
        {
            "currentSlate" => latestSnapshotDate,
            "latestGraded" => latestGradedDate,
            "latestCompleted" => latestCompletedDate,
            _ => null
        };
        var useAllFilteredRows = normalizedGradingMode is "rollingWindow" or "allHistory";
        var selectedRows = selectedDate.HasValue
            ? gradedRows.Where(row => row.Snapshot.SlateDate == selectedDate.Value).ToArray()
            : useAllFilteredRows
                ? gradedRows
                : [];
        var totalGraded = selectedRows.Count(row => IsGradedOutcome(row.Outcome));
        var currentSlateDate = latestSnapshotDate;
        var includeCurrentSlateAvailability = !string.Equals(normalizedGradingMode, "latestCompleted", StringComparison.OrdinalIgnoreCase);
        var currentSlateRows = includeCurrentSlateAvailability && currentSlateDate.HasValue
            ? gradedRows.Where(row => row.Snapshot.SlateDate == currentSlateDate.Value).ToArray()
            : [];
        var currentSlateDiagnostics = includeCurrentSlateAvailability && currentSlateDate.HasValue
            ? dateDiagnostics.FirstOrDefault(row => row.SlateDate == currentSlateDate.Value)
            : null;
        var currentSlateTotalPicks = currentSlateRows.Length;
        var currentSlateGradedPicks = currentSlateRows.Count(row => IsGradedOutcome(row.Outcome));
        var currentSlatePendingPicks = Math.Max(0, currentSlateTotalPicks - currentSlateGradedPicks);
        var hasPendingCurrentSlate = currentSlatePendingPicks > 0;
        var noSnapshotWarning = snapshots.Count == 0
            ? latestRefresh.LineShoppingSnapshotCount == 0 && latestRefresh.LatestSuccessfulRun is not null
                ? "Latest refresh produced no line-shopping snapshots."
                : latestRefresh.LineShoppingSnapshotCount > 0 && latestRefresh.HasUsableMarketData == false
                    ? "Latest refresh produced line-shopping snapshots, but none are usable active market data."
                    : "No line-shopping snapshots exist yet. Run a successful live refresh with forceLive=true to create snapshots."
            : null;
        var latestCompletedDateLabel = latestCompletedDate?.ToString("MMM d", CultureInfo.InvariantCulture);
        var warning = noSnapshotWarning
            ?? (totalGraded == 0
                ? normalizedGradingMode == "latestCompleted" && latestCompletedDate.HasValue && selectedRows.Length > 0
                    ? $"Captured recommendations exist for {latestCompletedDateLabel}, but none safely matched imported results."
                    : normalizedGradingMode == "latestCompleted" && latestCompletedDate.HasValue
                        ? $"Imported results exist for {latestCompletedDateLabel}, but no captured recommendations were found for that completed date."
                        : normalizedGradingMode == "latestGraded" && scoredSnapshots.Length > 0
                            ? "No graded recommendations have been matched yet."
                            : "No matching graded prop_results were found for the filtered snapshot rows."
                : totalGraded < 30
                    ? "Small graded sample. Treat ROI and hit-rate as directional only."
                    : null);
        var latestGradedBlocker = BuildLatestGradedBlocker(
            normalizedGradingMode,
            selectedDate,
            totalGraded,
            scoredSnapshots,
            actualResultRows,
            dateDiagnostics);

        return new
        {
            generatedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            requestedSport,
            effectiveSport,
            gradingMode = normalizedGradingMode,
            limitation = "Performance summary currently supports MLB snapshots only. Grading uses market_line_shopping_snapshots matched to prop_results by slate date, normalized prop type, and canonical player id when the snapshot can be resolved; otherwise it falls back to normalized player name.",
            filters = new
            {
                providerCategory,
                provider,
                bookmaker,
                propType,
                strategyClassification,
                strategyTier,
                candidateOnly,
                topPickOnly,
                includeWatchlist,
                includeResearch,
                includeStale,
                fromDate,
                toDate,
                minSampleSize,
                gradingMode = normalizedGradingMode,
                includeDiagnostics,
                diagnosticSampleSize = Math.Clamp(diagnosticSampleSize ?? 5, 1, 25)
            },
            dataAvailability = new
            {
                snapshotRowsLoaded = snapshots.Count,
                picksAfterFilters = selectedRows.Length,
                totalPicksAfterFilters = scoredSnapshots.Length,
                gradedPicks = totalGraded,
                hasEnoughData = totalGraded >= 30,
                warning,
                latestGradedBlocker = latestGradedBlocker.Blocker,
                latestGradedBlockerDetail = latestGradedBlocker.Detail,
                latestSnapshotDate,
                latestGradedDate,
                latestCompletedDate,
                latestResultsImportDate,
                currentSlateDate = includeCurrentSlateAvailability ? currentSlateDate : null,
                currentSlateTotalPicks = includeCurrentSlateAvailability ? currentSlateTotalPicks : (int?)null,
                currentSlateGradedPicks = includeCurrentSlateAvailability ? currentSlateGradedPicks : (int?)null,
                currentSlatePendingPicks = includeCurrentSlateAvailability ? currentSlatePendingPicks : (int?)null,
                snapshotRowsForCompletedDate = latestCompletedDate.HasValue
                    ? dateDiagnostics.FirstOrDefault(row => row.SlateDate == latestCompletedDate.Value)?.TotalPicks ?? 0
                    : 0,
                propResultsForCompletedDate = latestCompletedDate.HasValue
                    ? dateDiagnostics.FirstOrDefault(row => row.SlateDate == latestCompletedDate.Value)?.PropResultsAvailableForDate ?? latestCompletedPropResultsCount
                    : 0,
                unmatchedPicks = selectedRows.Length - totalGraded,
                hasGradedData = totalGraded > 0,
                hasPendingCurrentSlate = includeCurrentSlateAvailability ? hasPendingCurrentSlate : (bool?)null,
                pendingReason = hasPendingCurrentSlate
                    ? currentSlateDiagnostics?.PropResultsAvailableForDate > 0
                        ? "Current slate has imported prop_results, but selected snapshot rows did not match player/prop results. Check diagnostics.matchFailureCounts."
                        : "Current slate has not been fully graded because results are not imported yet."
                    : null,
                usedDateRange = useAllFilteredRows
                    ? new { fromDate, toDate }
                    : selectedDate.HasValue
                        ? new { fromDate = selectedDate, toDate = selectedDate }
                        : null,
                latestRefresh.HasUsableMarketData,
                latestRefresh.LineShoppingSnapshotCount,
                latestRefresh.TopPickSnapshotCount,
                latestRefresh.ActiveGroupCount,
                latestRefresh.StaleOrExpiredGroupCount,
                latestRefresh.ProviderIngestionFailureCount,
                latestRefresh.ProviderIngestionSuccessCount,
                latestRefresh.ProviderRejectedKeyDetected,
                latestRefresh.UsabilityWarning
            },
            diagnostics = new
            {
                snapshotDatesConsidered = dateDiagnostics.Select(row => row.SlateDate).ToArray(),
                resultDatesAvailable = actualResultRows.Select(row => row.EventDate).Distinct().OrderByDescending(date => date).ToArray(),
                rowsByDate = dateDiagnostics.Select(row => new
                {
                    slateDate = row.SlateDate,
                    snapshotRows = row.TotalPicks,
                    matchedGradedRows = row.GradedPicks,
                    ungradedPendingRows = row.PendingPicks,
                    propResultsAvailableForDate = row.PropResultsAvailableForDate,
                    distinctPropResultPlayers = row.DistinctPropResultPlayers,
                    distinctPropResultPropTypes = row.DistinctPropResultPropTypes,
                    matchFailureCounts = row.MatchFailureCounts,
                    matchStrategyUsed = row.MatchStrategyUsed
                }).ToArray(),
                matchingStrategy = "market_line_shopping_snapshots matched to prop_results by slate date, canonical player id when resolvable, normalized prop type, then normalized player-name fallback.",
                snapshotPlayerIdSource = "MarketLineShoppingSnapshot does not persist canonical player ids; performance summary resolves ids from DetailsJson playerId/canonicalPlayerId when present, otherwise by unique MLB display-name lookup.",
                matchStrategyCounts = gradedRows
                    .GroupBy(row => IsGradedOutcome(row.Outcome)
                        ? row.MatchLookupMode
                        : string.IsNullOrWhiteSpace(row.MatchFailureReason) ? "Unknown" : row.MatchFailureReason!)
                    .ToDictionary(group => group.Key, group => group.Count()),
                unmatchedExamples = includeDiagnostics
                    ? BuildPerformanceUnmatchedExamples(
                        gradedRows,
                        actualResultRows,
                        Math.Clamp(diagnosticSampleSize ?? 5, 1, 25))
                    : []
            },
            groups = new
            {
                overall = BuildPerformanceGroups(selectedRows, "overall", row => "All"),
                byProviderCategory = BuildPerformanceGroups(selectedRows, "providerCategory", row => row.Snapshot.ProviderCategory),
                byProvider = BuildPerformanceGroups(selectedRows, "provider", row => row.Snapshot.EntryProvider),
                byBookmaker = BuildPerformanceGroups(selectedRows, "bookmaker", row => row.Snapshot.BestBookmaker ?? row.Snapshot.Bookmaker ?? "Unknown"),
                byPropType = BuildPerformanceGroups(selectedRows, "propType", row => row.Snapshot.PropType),
                byStrategyClassification = BuildPerformanceGroups(selectedRows, "strategyClassification", row => row.Snapshot.StrategyClassification),
                byStrategyTier = BuildPerformanceGroups(selectedRows, "strategyTier", row => row.Snapshot.StrategyTier),
                byProviderAndPropType = BuildPerformanceGroups(selectedRows, "providerAndPropType", row => $"{row.Snapshot.EntryProvider} | {row.Snapshot.PropType}")
            }
        };
    }

    private static string NormalizePerformanceGradingMode(string? gradingMode)
    {
        return gradingMode?.Trim().ToLowerInvariant() switch
        {
            "currentslate" or "current-slate" or "current_slate" => "currentSlate",
            "latestcompleted" or "latest-completed" or "latest_completed" or "previouscompleted" or "previous-completed" or "previous_completed" => "latestCompleted",
            "rollingwindow" or "rolling-window" or "rolling_window" => "rollingWindow",
            "allhistory" or "all-history" or "all_history" or "history" or "cumulative" => "allHistory",
            _ => "latestGraded"
        };
    }

    private static object BuildUnsupportedPerformanceSummaryResponse(
        DateTimeOffset generatedAtUtc,
        string? requestedSport,
        string effectiveSport,
        string gradingMode)
    {
        return new
        {
            generatedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            supportedCompetitions = BuildSupportedCompetitionDiagnostics(),
            requestedSport,
            effectiveSport,
            sport = effectiveSport,
            competition = ResolveCompetitionName(effectiveSport),
            resultGradingSupported = false,
            unavailableReason = SoccerHistoryUnavailableMessage,
            summary = new
            {
                gradedPicks = 0,
                won = 0,
                lost = 0,
                pushed = 0,
                noResultPicks = 0,
                hitRate = null as decimal?,
                simulatedRoi = null as decimal?,
                avgEvAtRecommendationTime = null as decimal?
            },
            breakdowns = new
            {
                overview = Array.Empty<object>(),
                providers = Array.Empty<object>(),
                propTypes = Array.Empty<object>()
            },
            diagnostics = new
            {
                matchingStrategy = SoccerHistoryUnavailableMessage,
                gradingMode,
                resultGradingSupported = false,
                soccerResultGradingSupported = false
            }
        };
    }

    private static async Task<object> BuildMarketPerformanceResultsResponseAsync(
        string? requestedSport,
        string effectiveSport,
        DateOnly? requestedDate,
        bool includeDiagnostics,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        if (IsSoccerSport(effectiveSport))
        {
            return new
            {
                generatedAtUtc,
                callsExternalApis = false,
                readOnly = true,
                supportedSports = SupportedMarketSports,
                supportedCompetitions = BuildSupportedCompetitionDiagnostics(),
                requestedSport,
                sport = effectiveSport,
                competition = ResolveCompetitionName(effectiveSport),
                resultGradingSupported = false,
                unavailableReason = SoccerHistoryUnavailableMessage,
                date = requestedDate,
                summary = new
                {
                    gradedPicks = 0,
                    won = 0,
                    lost = 0,
                    pushed = 0,
                    noResultExcluded = 0,
                    hitRate = null as decimal?,
                    simulatedRoi = null as decimal?,
                    avgEvAtRecommendationTime = null as decimal?
                },
                rows = Array.Empty<object>(),
                diagnostics = includeDiagnostics
                    ? new
                    {
                        reasonCounts = new Dictionary<string, int>(),
                        examples = Array.Empty<object>(),
                        resultGradingSupported = false,
                        message = SoccerHistoryUnavailableMessage
                    }
                    : null
            };
        }

        var selectedDate = requestedDate ??
            await dbContext.PropResults
                .AsNoTracking()
                .Select(row => (DateOnly?)row.EventDate)
                .MaxAsync(cancellationToken);

        if (!selectedDate.HasValue)
        {
            return new
            {
                generatedAtUtc,
                callsExternalApis = false,
                readOnly = true,
                requestedSport,
                sport = effectiveSport,
                date = requestedDate,
                summary = new
                {
                    gradedPicks = 0,
                    won = 0,
                    lost = 0,
                    pushed = 0,
                    noResultExcluded = 0,
                    hitRate = null as decimal?,
                    simulatedRoi = null as decimal?,
                    avgEvAtRecommendationTime = null as decimal?
                },
                rows = Array.Empty<object>(),
                diagnostics = includeDiagnostics ? new { reasonCounts = new Dictionary<string, int>(), examples = Array.Empty<object>() } : null
            };
        }

        var ledgerQuery = dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(row =>
                row.Sport == effectiveSport &&
                row.ExpectedResultDate == selectedDate.Value &&
                !row.IsExcludedFromPerformance);

        ledgerQuery = ApplyRecommendationPerformanceEligibility(
            ledgerQuery,
            candidateOnly: true,
            topPickOnly: false,
            includeWatchlist: false,
            includeResearch: false,
            includeStale: false);

        var selectedRows = (await ledgerQuery
                .OrderByDescending(row => row.RankingScore)
                .ThenByDescending(row => row.EstimatedEv)
                .Take(5000)
                .ToArrayAsync(cancellationToken))
            .Where(HasPerformanceGradeability)
            .ToArray();
        var gradedRows = await GradeLedgerRowsAsync(selectedRows, dbContext, cancellationToken);
        var determinedRows = gradedRows
            .Where(row => IsGradedOutcome(row.Outcome))
            .OrderByDescending(row => row.Snapshot.EstimatedEv)
            .ThenBy(row => row.Snapshot.PlayerName)
            .ToArray();
        var noResultRows = gradedRows
            .Where(row => IsNoResultFailure(row.MatchFailureReason))
            .ToArray();
        var wins = determinedRows.Count(row => row.Outcome == "Win");
        var losses = determinedRows.Count(row => row.Outcome == "Loss");
        var pushes = determinedRows.Count(row => row.Outcome == "Push");
        var decisions = wins + losses;
        var profit = determinedRows.Sum(row => row.Profit ?? 0m);
        var roi = determinedRows.Length == 0 ? (decimal?)null : Math.Round(profit / determinedRows.Length, 4);

        return new
        {
            generatedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            requestedSport,
            sport = effectiveSport,
            date = selectedDate.Value,
            summary = new
            {
                gradedPicks = determinedRows.Length,
                won = wins,
                lost = losses,
                pushed = pushes,
                noResultExcluded = noResultRows.Length,
                hitRate = decisions == 0 ? (decimal?)null : Math.Round(wins / (decimal)decisions, 4),
                simulatedRoi = roi,
                avgEvAtRecommendationTime = AverageNullable(determinedRows.Select(row => row.Snapshot.EstimatedEv))
            },
            rows = determinedRows
                .Select(row => new
                {
                    recommendationSnapshotId = row.Snapshot.Id,
                    result = ToSnapshotGradeStatus(row.Outcome),
                    playerName = row.Snapshot.PlayerName,
                    propType = row.Snapshot.PropType,
                    side = row.Snapshot.Side,
                    line = row.Snapshot.Line,
                    actualValue = row.ActualResult,
                    provider = row.Snapshot.EntryProvider,
                    bookmaker = row.Snapshot.Bookmaker,
                    eventStartTimeUtc = row.Snapshot.EventStartTimeUtc,
                    estimatedEv = row.Snapshot.EstimatedEv,
                    hitChance = row.Snapshot.HitChance,
                    trust = row.Snapshot.TrustLevel,
                    sampleSize = row.Snapshot.HistoricalSampleSize,
                    matchMethod = row.MatchMethod
                })
                .ToArray(),
            diagnostics = includeDiagnostics
                ? new
                {
                    noResultCounts = noResultRows
                        .GroupBy(row => row.MatchFailureReason ?? "Unknown")
                        .ToDictionary(group => group.Key, group => group.Count()),
                    noResultExamples = noResultRows
                        .Take(10)
                        .Select(row => new
                        {
                            recommendationSnapshotId = row.Snapshot.Id,
                            row.Snapshot.PlayerName,
                            row.Snapshot.PropType,
                            row.Snapshot.Side,
                            row.Snapshot.Line,
                            provider = row.Snapshot.EntryProvider,
                            reason = row.MatchFailureReason
                        })
                        .ToArray(),
                    matchMethodCounts = determinedRows
                        .GroupBy(row => row.MatchMethod ?? "Unknown")
                        .ToDictionary(group => group.Key, group => group.Count())
                }
                : null
        };
    }

    private static async Task<object?> TryBuildLedgerPerformanceSummaryResponseAsync(
        string? requestedSport,
        string effectiveSport,
        string? providerCategory,
        string? provider,
        string? bookmaker,
        string? propType,
        string? strategyClassification,
        string? strategyTier,
        bool candidateOnly,
        DateOnly? fromDate,
        DateOnly? toDate,
        DateOnly? startDate,
        DateOnly? endDate,
        int? minSampleSize,
        string normalizedGradingMode,
        TheOddsApiDebugEndpoints.MlbRefreshFreshnessResponse latestRefresh,
        bool topPickOnly,
        bool includeWatchlist,
        bool includeResearch,
        bool includeStale,
        bool includeExcluded,
        bool includeNoResultDiagnostics,
        bool includeDiagnostics,
        int? diagnosticSampleSize,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var latestCompletedDate = await dbContext.PropResults
            .AsNoTracking()
            .Select(row => (DateOnly?)row.EventDate)
            .MaxAsync(cancellationToken);
        var latestCompletedPropResultsCount = latestCompletedDate.HasValue
            ? await dbContext.PropResults
                .AsNoTracking()
                .CountAsync(row => row.EventDate == latestCompletedDate.Value, cancellationToken)
            : 0;

        var allLedgerQuery = dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport);
        var ledgerQuery = allLedgerQuery;

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            ledgerQuery = ledgerQuery.Where(row => row.ProviderCategory == providerCategory);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            ledgerQuery = ledgerQuery.Where(row => row.Provider == provider || row.EntryProvider == provider);
        }

        if (!string.IsNullOrWhiteSpace(bookmaker))
        {
            ledgerQuery = ledgerQuery.Where(row => row.Bookmaker == bookmaker);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            var normalizedPropType = RecommendationLedgerWriter.NormalizeKey(propType);
            ledgerQuery = ledgerQuery.Where(row => row.PropType == propType || row.NormalizedPropType == normalizedPropType);
        }

        if (!string.IsNullOrWhiteSpace(strategyClassification))
        {
            ledgerQuery = ledgerQuery.Where(row => row.StrategyClassification == strategyClassification);
        }

        if (!string.IsNullOrWhiteSpace(strategyTier))
        {
            ledgerQuery = ledgerQuery.Where(row => row.StrategyTier == strategyTier);
        }

        var ledgerPreExclusionQuery = ledgerQuery;
        if (!includeExcluded)
        {
            ledgerQuery = ledgerQuery.Where(row => !row.IsExcludedFromPerformance);
        }

        var ledgerPreEligibilityQuery = ledgerQuery;
        ledgerQuery = ApplyRecommendationPerformanceEligibility(
            ledgerPreEligibilityQuery,
            candidateOnly,
            topPickOnly,
            includeWatchlist,
            includeResearch,
            includeStale);

        if (fromDate.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(row => row.ExpectedResultDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(row => row.ExpectedResultDate <= toDate.Value);
        }

        if (minSampleSize.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(row => row.HistoricalSampleSize >= minSampleSize.Value);
        }

        var ledgerTotalAfterFilters = await ledgerQuery.CountAsync(cancellationToken);
        if (ledgerTotalAfterFilters == 0 && !string.Equals(normalizedGradingMode, "latestCompleted", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var latestLedgerDate = ledgerTotalAfterFilters == 0
            ? null
            : await ledgerQuery.Select(row => (DateOnly?)row.ExpectedResultDate).MaxAsync(cancellationToken);
        var ledgerRowsForLatestCompleted = latestCompletedDate.HasValue
            ? await allLedgerQuery.CountAsync(row => row.ExpectedResultDate == latestCompletedDate.Value, cancellationToken)
            : 0;
        var candidateLedgerRowsForLatestCompleted = latestCompletedDate.HasValue
            ? await allLedgerQuery.CountAsync(row =>
                row.ExpectedResultDate == latestCompletedDate.Value &&
                (row.IsCandidate || row.IsTopPick || row.IsWatchlist), cancellationToken)
            : 0;
        var filteredLedgerRowsForLatestCompleted = latestCompletedDate.HasValue
            ? await ledgerQuery.CountAsync(row => row.ExpectedResultDate == latestCompletedDate.Value, cancellationToken)
            : 0;
        var selectedDate = normalizedGradingMode switch
        {
            "currentSlate" => latestLedgerDate,
            "latestCompleted" => latestCompletedDate,
            _ => null
        };
        var useAllFilteredRows = normalizedGradingMode is "rollingWindow" or "allHistory";
        var baseSelectedQuery = selectedDate.HasValue
            ? ledgerQuery.Where(row => row.ExpectedResultDate == selectedDate.Value)
            : ledgerQuery;
        var selectedPreEligibilityQuery = selectedDate.HasValue
            ? ledgerPreEligibilityQuery.Where(row => row.ExpectedResultDate == selectedDate.Value)
            : useAllFilteredRows
                ? ledgerPreEligibilityQuery
                : ledgerPreEligibilityQuery.Where(row => false);
        var selectedPreEligibilityRows = await selectedPreEligibilityQuery
            .Select(row => new
            {
                row.IsCandidate,
                row.IsTopPick,
                row.IsWatchlist,
                row.IsActionableForUser,
                row.LifecycleStatus,
                row.Warning,
                row.CanonicalPlayerId,
                row.PropTypeId,
                row.NormalizedPropType,
                row.EventId,
                row.EventStartTimeUtc,
                row.ExpectedResultDate
            })
            .ToArrayAsync(cancellationToken);
        var selectedRowsBeforeGradeability = await baseSelectedQuery
            .OrderByDescending(row => row.RankingScore)
            .ThenByDescending(row => row.EstimatedEv)
            .Take(5000)
            .ToArrayAsync(cancellationToken);
        var selectedRows = selectedRowsBeforeGradeability
            .Where(HasPerformanceGradeability)
            .ToArray();
        var resultDates = selectedRows.Select(row => row.ExpectedResultDate).Distinct().ToArray();
        var playerIds = selectedRows
            .Where(row => row.CanonicalPlayerId.HasValue)
            .Select(row => row.CanonicalPlayerId!.Value)
            .Distinct()
            .ToArray();
        var resultRows = resultDates.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(row => resultDates.Contains(row.EventDate))
                .Select(row => new LedgerActualResultLookupRow(
                    row.Id,
                    row.EventDate,
                    row.PlayerId,
                    row.Player != null ? row.Player.DisplayName : string.Empty,
                    row.PropTypeId,
                    row.PropType != null ? row.PropType.Name : string.Empty,
                    row.ActualResultValue))
                .ToArrayAsync(cancellationToken);
        var diagnosticResultDates = resultDates
            .SelectMany(date => new[] { date.AddDays(-1), date, date.AddDays(1) })
            .Distinct()
            .ToArray();
        var diagnosticResultRows = diagnosticResultDates.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(row => diagnosticResultDates.Contains(row.EventDate))
                .Select(row => new LedgerDiagnosticResultRow(
                    row.Id,
                    row.EventDate,
                    row.PlayerId,
                    row.PlayerId,
                    row.Player != null ? row.Player.DisplayName : string.Empty,
                    row.PropTypeId,
                    row.PropType != null ? row.PropType.Name : string.Empty,
                    row.ActualResultValue))
                .ToArrayAsync(cancellationToken);
        var gradedRows = selectedRows
            .Select(row => GradeLedgerPerformanceRow(row, resultRows, diagnosticResultRows))
            .ToArray();
        var latestGradedDate = ledgerTotalAfterFilters == 0
            ? null
            : gradedRows
                .Where(row => IsGradedOutcome(row.Outcome))
                .Select(row => (DateOnly?)row.Snapshot.ExpectedResultDate)
                .DefaultIfEmpty()
                .Max();
        var summaryRows = normalizedGradingMode == "latestGraded" && latestGradedDate.HasValue
            ? gradedRows.Where(row => row.Snapshot.ExpectedResultDate == latestGradedDate.Value).ToArray()
            : gradedRows;
        var summarySelectedRows = normalizedGradingMode == "latestGraded" && latestGradedDate.HasValue
            ? selectedRows.Where(row => row.ExpectedResultDate == latestGradedDate.Value).ToArray()
            : selectedRows;
        var summarySelectedRowsBeforeGradeability = normalizedGradingMode == "latestGraded" && latestGradedDate.HasValue
            ? selectedRowsBeforeGradeability.Where(row => row.ExpectedResultDate == latestGradedDate.Value).ToArray()
            : selectedRowsBeforeGradeability;
        var totalGraded = summaryRows.Count(row => IsGradedOutcome(row.Outcome));
        var wonRows = summaryRows.Count(row => row.Outcome == "Win");
        var lostRows = summaryRows.Count(row => row.Outcome == "Loss");
        var pushedRows = summaryRows.Count(row => row.Outcome == "Push");
        var noResultRows = summaryRows.Count(row => IsNoResultFailure(row.MatchFailureReason));
        var pendingRows = summaryRows.Count(row => IsPendingFailure(row.MatchFailureReason));
        var unmatchedRows = summaryRows.Count(row =>
            row.Outcome is null &&
            !IsNoResultFailure(row.MatchFailureReason) &&
            !IsPendingFailure(row.MatchFailureReason));
        var warning = totalGraded == 0
            ? summarySelectedRows.Length == 0 && latestCompletedDate.HasValue
                ? $"No captured recommendations exist for the latest completed result date ({latestCompletedDate.Value:MMM d})."
                : summarySelectedRows.Length > 0
                    ? noResultRows > 0 && unmatchedRows == 0
                        ? $"Captured recommendations exist for {selectedDate:MMM d}, but none had gradeable result rows."
                        : $"Captured recommendations exist for {selectedDate:MMM d}, but none safely matched imported results."
                    : "No captured recommendations exist yet."
            : totalGraded < 30
                ? "Small graded sample. Treat ROI and hit-rate as directional only."
                : null;

        var groupRows = includeNoResultDiagnostics
            ? summaryRows
            : summaryRows.Where(row => IsGradedOutcome(row.Outcome)).ToArray();

        return new
        {
            generatedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            requestedSport,
            effectiveSport,
            gradingMode = normalizedGradingMode,
            performanceSource = "recommendationLedger",
            limitation = "Performance summary uses recommendation_snapshots as the captured-pick ledger and matches rows to prop_results by expected result date, canonical player id, and normalized prop/stat type. Provider, side, and line are not used to find the result row; side and line are used only after a result is found.",
            filters = new
            {
                providerCategory,
                provider,
                bookmaker,
                propType,
                strategyClassification,
                strategyTier,
                candidateOnly,
                topPickOnly,
                includeWatchlist,
                includeResearch,
                includeStale,
                includeExcluded,
                includeNoResultDiagnostics,
                startDate,
                endDate,
                fromDate,
                toDate,
                minSampleSize,
                gradingMode = normalizedGradingMode
            },
            summary = new
            {
                gradedPicks = totalGraded,
                won = wonRows,
                lost = lostRows,
                pushed = pushedRows,
                noResultExcluded = noResultRows,
                hitRate = wonRows + lostRows == 0 ? (decimal?)null : Math.Round(wonRows / (decimal)(wonRows + lostRows), 4),
                simulatedRoi = totalGraded == 0 ? (decimal?)null : Math.Round(summaryRows.Sum(row => row.Profit ?? 0m) / totalGraded, 4),
                avgEvAtRecommendationTime = AverageNullable(summaryRows.Where(row => IsGradedOutcome(row.Outcome)).Select(row => row.Snapshot.EstimatedEv))
            },
            dataAvailability = new
            {
                ledgerRowsLoaded = summarySelectedRows.Length,
                gradedLedgerRows = totalGraded,
                pendingLedgerRows = pendingRows,
                noResultLedgerRows = noResultRows,
                unmatchedLedgerRows = unmatchedRows,
                preFilterRowsForCompletedDate = selectedPreEligibilityRows.Length,
                excludedFromPerformanceRows = selectedDate.HasValue
                    ? await ledgerPreExclusionQuery.CountAsync(row => row.ExpectedResultDate == selectedDate.Value && row.IsExcludedFromPerformance, cancellationToken)
                    : await ledgerPreExclusionQuery.CountAsync(row => row.IsExcludedFromPerformance, cancellationToken),
                includedExcludedRows = includeExcluded ? summarySelectedRows.Count(row => row.IsExcludedFromPerformance) : 0,
                rowsAfterPerformanceEligibility = summarySelectedRows.Length,
                rowsExcludedWatchlist = selectedPreEligibilityRows.Count(row => row.IsWatchlist && !includeWatchlist),
                rowsExcludedResearch = selectedPreEligibilityRows.Count(row => !row.IsCandidate && !row.IsTopPick && !row.IsWatchlist && !includeResearch),
                rowsExcludedStale = selectedPreEligibilityRows.Count(row =>
                    !includeStale &&
                    !string.IsNullOrWhiteSpace(row.Warning) &&
                    row.Warning.Contains("stale", StringComparison.OrdinalIgnoreCase)),
                rowsExcludedNonPlayable = selectedPreEligibilityRows.Count(row =>
                    !includeStale &&
                    (!row.IsActionableForUser || row.LifecycleStatus != "Active")),
                rowsExcludedNotCandidateOrTopPick = selectedPreEligibilityRows.Count(row =>
                    topPickOnly
                        ? !row.IsTopPick
                        : candidateOnly
                            ? !row.IsCandidate
                            : !includeResearch && !row.IsCandidate && !row.IsTopPick && !(includeWatchlist && row.IsWatchlist)),
                rowsExcludedMissingPlayer = summarySelectedRowsBeforeGradeability.Count(row => !row.CanonicalPlayerId.HasValue),
                rowsExcludedMissingProp = summarySelectedRowsBeforeGradeability.Count(row => !row.PropTypeId.HasValue || string.IsNullOrWhiteSpace(row.NormalizedPropType)),
                rowsExcludedUnsupportedProp = summarySelectedRowsBeforeGradeability.Count(row =>
                    row.PropTypeId.HasValue &&
                    !string.IsNullOrWhiteSpace(row.NormalizedPropType) &&
                    !IsSupportedMlbPerformancePropType(row.NormalizedPropType)),
                rowsExcludedMissingEventMapping = summarySelectedRowsBeforeGradeability.Count(row =>
                    string.IsNullOrWhiteSpace(row.EventId) ||
                    !row.EventStartTimeUtc.HasValue ||
                    row.ExpectedResultDate == default),
                rowsExcludedEventDateMappingMismatch = summarySelectedRowsBeforeGradeability.Count(row =>
                    row.EventStartTimeUtc.HasValue &&
                    row.ExpectedResultDate != default &&
                    ResolveEasternDate(row.EventStartTimeUtc.Value) != row.ExpectedResultDate),
                candidateRows = summarySelectedRows.Count(row => row.IsCandidate),
                topPickRows = summarySelectedRows.Count(row => row.IsTopPick),
                watchlistRows = summarySelectedRows.Count(row => row.IsWatchlist),
                researchRows = summarySelectedRows.Count(row => !row.IsCandidate && !row.IsTopPick && !row.IsWatchlist),
                preFilterCandidateRows = selectedPreEligibilityRows.Count(row => row.IsCandidate),
                preFilterTopPickRows = selectedPreEligibilityRows.Count(row => row.IsTopPick),
                preFilterWatchlistRows = selectedPreEligibilityRows.Count(row => row.IsWatchlist),
                preFilterResearchRows = selectedPreEligibilityRows.Count(row => !row.IsCandidate && !row.IsTopPick && !row.IsWatchlist),
                staleLockedExpiredRows = summarySelectedRows.Count(row =>
                    !row.IsActionableForUser ||
                    row.LifecycleStatus != "Active" ||
                    (!string.IsNullOrWhiteSpace(row.Warning) && row.Warning.Contains("stale", StringComparison.OrdinalIgnoreCase))),
                staleOrNonPlayableRows = summarySelectedRows.Count(row =>
                    !row.IsActionableForUser ||
                    row.LifecycleStatus != "Active" ||
                    (!string.IsNullOrWhiteSpace(row.Warning) && row.Warning.Contains("stale", StringComparison.OrdinalIgnoreCase))),
                preFilterStaleOrNonPlayableRows = selectedPreEligibilityRows.Count(row =>
                    !row.IsActionableForUser ||
                    row.LifecycleStatus != "Active" ||
                    (!string.IsNullOrWhiteSpace(row.Warning) && row.Warning.Contains("stale", StringComparison.OrdinalIgnoreCase))),
                noResultProviderInactiveRows = summaryRows.Count(row => row.MatchFailureReason == "ProviderLineForInactivePlayer"),
                matchedViaExactPlayerIdDateStatRows = summaryRows.Count(row => row.MatchMethod == "ExactPlayerIdDateStat"),
                matchedViaNormalizedPlayerNameDateStatRows = summaryRows.Count(row => row.MatchMethod == "NormalizedPlayerNameDateStatFallback"),
                ambiguousFallbackRows = summaryRows.Count(row => row.MatchFailureReason == "AmbiguousNormalizedPlayerNameDateStatFallback"),
                noResultEventDateMismatchRows = summaryRows.Count(row => row.MatchFailureReason == "EventDateMismatch"),
                noResultUnknownRowsForCompletedDate = summaryRows.Count(row => row.MatchFailureReason == "Unknown"),
                noResultMissingPlayerRowsForCompletedDate = summaryRows.Count(row => row.MatchFailureReason == "MissingLedgerPlayerId"),
                noResultMissingPropRowsForCompletedDate = summaryRows.Count(row => row.MatchFailureReason == "MissingLedgerPropType"),
                noResultUnsupportedPropRowsForCompletedDate = summaryRows.Count(row => row.MatchFailureReason == "UnsupportedPropType"),
                noResultResultsNotImportedRowsForCompletedDate = summaryRows.Count(row => row.MatchFailureReason == "NoPropResultsForDate"),
                noResultReasonCodeCounts = summaryRows
                    .Where(row => row.Outcome is null)
                    .GroupBy(row => row.MatchFailureReason ?? "Unknown")
                    .ToDictionary(group => group.Key, group => group.Count()),
                snapshotRowsLoaded = 0,
                picksAfterFilters = summarySelectedRows.Length,
                totalPicksAfterFilters = ledgerTotalAfterFilters,
                gradedPicks = totalGraded,
                noResultPicks = noResultRows,
                hasGradeableSample = totalGraded > 0,
                gradeableGroupCount = CountGradeableGroups(groupRows),
                noResultOnlyGroupCount = includeNoResultDiagnostics ? CountNoResultOnlyGroups(summaryRows) : 0,
                hasEnoughData = totalGraded >= 30,
                warning,
                latestCompletedDate,
                latestResultsImportDate = latestCompletedDate,
                latestLedgerDate,
                latestGradedDate,
                currentSlateDate = normalizedGradingMode == "currentSlate" ? latestLedgerDate : null,
                currentSlateTotalPicks = normalizedGradingMode == "currentSlate" ? summarySelectedRows.Length : (int?)null,
                currentSlateGradedPicks = normalizedGradingMode == "currentSlate" ? totalGraded : (int?)null,
                currentSlatePendingPicks = normalizedGradingMode == "currentSlate" ? Math.Max(0, summarySelectedRows.Length - totalGraded) : (int?)null,
                snapshotRowsForCompletedDate = filteredLedgerRowsForLatestCompleted,
                ledgerRowsForCompletedDate = ledgerRowsForLatestCompleted,
                candidateLedgerRowsForCompletedDate = candidateLedgerRowsForLatestCompleted,
                filteredRowsForCompletedDate = filteredLedgerRowsForLatestCompleted,
                gradedRowsForCompletedDate = totalGraded,
                noResultRowsForCompletedDate = noResultRows,
                unmatchedRowsForCompletedDate = unmatchedRows,
                pendingRowsForCompletedDate = pendingRows,
                matchedAndGradedRowsForCompletedDate = totalGraded,
                matchedViaExactPlayerIdDateStatRowsForCompletedDate = summaryRows.Count(row => row.MatchMethod == "ExactPlayerIdDateStat"),
                matchedViaNormalizedPlayerNameDateStatRowsForCompletedDate = summaryRows.Count(row => row.MatchMethod == "NormalizedPlayerNameDateStatFallback"),
                ambiguousFallbackRowsForCompletedDate = summaryRows.Count(row => row.MatchFailureReason == "AmbiguousNormalizedPlayerNameDateStatFallback"),
                providerInactiveRowsForCompletedDate = summaryRows.Count(row => row.MatchFailureReason == "ProviderLineForInactivePlayer"),
                eventDateMismatchRowsForCompletedDate = summaryRows.Count(row => row.MatchFailureReason == "EventDateMismatch"),
                wonRowsForCompletedDate = wonRows,
                lostRowsForCompletedDate = lostRows,
                pushedRowsForCompletedDate = pushedRows,
                importedResultsForCompletedDate = latestCompletedPropResultsCount,
                propResultsForCompletedDate = latestCompletedPropResultsCount,
                unmatchedPicks = unmatchedRows,
                hasLedgerRowsForCompletedDate = ledgerRowsForLatestCompleted > 0,
                hasFilteredRowsForCompletedDate = filteredLedgerRowsForLatestCompleted > 0,
                hasGradedData = totalGraded > 0,
                hasPendingCurrentSlate = normalizedGradingMode == "currentSlate" ? summarySelectedRows.Length > totalGraded : (bool?)null,
                pendingReason = null as string,
                usedDateRange = useAllFilteredRows
                    ? new { fromDate, toDate }
                    : normalizedGradingMode == "latestGraded" && latestGradedDate.HasValue
                        ? new { fromDate = latestGradedDate, toDate = latestGradedDate }
                    : selectedDate.HasValue
                        ? new { fromDate = selectedDate, toDate = selectedDate }
                        : null,
                latestRefresh.HasUsableMarketData,
                latestRefresh.LineShoppingSnapshotCount,
                latestRefresh.TopPickSnapshotCount,
                latestRefresh.ActiveGroupCount,
                latestRefresh.StaleOrExpiredGroupCount,
                latestRefresh.ProviderIngestionFailureCount,
                latestRefresh.ProviderIngestionSuccessCount,
                latestRefresh.ProviderRejectedKeyDetected,
                latestRefresh.UsabilityWarning
            },
            diagnostics = new
            {
                ledgerRowsByDate = selectedRows
                    .GroupBy(row => row.ExpectedResultDate)
                    .OrderByDescending(group => group.Key)
                    .Select(group => new
                    {
                        expectedResultDate = group.Key,
                        ledgerRows = group.Count(),
                        gradedRows = gradedRows.Count(row => row.Snapshot.ExpectedResultDate == group.Key && IsGradedOutcome(row.Outcome)),
                        noResultRows = gradedRows.Count(row => row.Snapshot.ExpectedResultDate == group.Key && IsNoResultFailure(row.MatchFailureReason)),
                        unmatchedRows = gradedRows.Count(row =>
                            row.Snapshot.ExpectedResultDate == group.Key &&
                            row.Outcome is null &&
                            !IsNoResultFailure(row.MatchFailureReason) &&
                            !IsPendingFailure(row.MatchFailureReason)),
                        pendingRows = gradedRows.Count(row => row.Snapshot.ExpectedResultDate == group.Key && IsPendingFailure(row.MatchFailureReason)),
                        importedResultRows = latestCompletedDate == group.Key ? latestCompletedPropResultsCount : resultRows.Count(row => row.EventDate == group.Key)
                    })
                    .ToArray(),
                matchFailureCounts = gradedRows
                    .Where(row => row.Outcome is null)
                    .GroupBy(row => row.MatchFailureReason ?? "Unknown")
                    .ToDictionary(group => group.Key, group => group.Count()),
                matchMethodCounts = gradedRows
                    .Where(row => row.Outcome is not null)
                    .GroupBy(row => row.MatchMethod ?? "Unknown")
                    .ToDictionary(group => group.Key, group => group.Count()),
                noResultLedgerExamples = includeDiagnostics
                    ? BuildLedgerNoResultExamples(
                        gradedRows,
                        diagnosticResultRows,
                        Math.Clamp(diagnosticSampleSize ?? 5, 1, 25))
                    : [],
                unmatchedLedgerExamples = includeDiagnostics
                    ? BuildLedgerUnmatchedExamples(
                        gradedRows,
                        diagnosticResultRows,
                        Math.Clamp(diagnosticSampleSize ?? 5, 1, 25))
                    : [],
                matchingStrategy = "recommendation_snapshots are graded at read time against prop_results by expected result date plus prop/stat. Exact canonical player id matches are preferred; if no exact id row exists, one unique normalized player-name/date/stat fallback row can be used. Provider, side, and line are not used to find the result row; side and line are only used to grade the found result."
            },
            groups = new
            {
                overall = BuildLedgerPerformanceGroups(groupRows, "overall", row => "All"),
                byProviderCategory = BuildLedgerPerformanceGroups(groupRows, "providerCategory", row => row.Snapshot.ProviderCategory),
                byProvider = BuildLedgerPerformanceGroups(groupRows, "provider", row => row.Snapshot.EntryProvider),
                byBookmaker = BuildLedgerPerformanceGroups(groupRows, "bookmaker", row => row.Snapshot.Bookmaker),
                byPropType = BuildLedgerPerformanceGroups(groupRows, "propType", row => row.Snapshot.PropType),
                byStrategyClassification = BuildLedgerPerformanceGroups(groupRows, "strategyClassification", row => row.Snapshot.StrategyClassification),
                byStrategyTier = BuildLedgerPerformanceGroups(groupRows, "strategyTier", row => row.Snapshot.StrategyTier),
                byProviderAndPropType = BuildLedgerPerformanceGroups(groupRows, "providerAndPropType", row => $"{row.Snapshot.EntryProvider} | {row.Snapshot.PropType}")
            }
        };
    }

    internal static async Task<PerformanceResetSelectionResult> BuildPerformanceResetSelectionAsync(
        string? requestedSport,
        string effectiveSport,
        string? provider,
        string? propType,
        bool candidateOnly,
        bool topPickOnly,
        bool includeWatchlist,
        bool includeResearch,
        bool includeStale,
        bool includeExcluded,
        string gradingMode,
        bool gradedOnly,
        int take,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var normalizedGradingMode = NormalizePerformanceGradingMode(gradingMode);
        var latestCompletedDate = await dbContext.PropResults
            .AsNoTracking()
            .Select(row => (DateOnly?)row.EventDate)
            .MaxAsync(cancellationToken);

        var allLedgerQuery = dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == effectiveSport);
        var ledgerQuery = allLedgerQuery;

        if (!string.IsNullOrWhiteSpace(provider))
        {
            ledgerQuery = ledgerQuery.Where(row => row.Provider == provider || row.EntryProvider == provider);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            var normalizedPropType = RecommendationLedgerWriter.NormalizeKey(propType);
            ledgerQuery = ledgerQuery.Where(row => row.PropType == propType || row.NormalizedPropType == normalizedPropType);
        }

        var ledgerPreExclusionQuery = ledgerQuery;
        if (!includeExcluded)
        {
            ledgerQuery = ledgerQuery.Where(row => !row.IsExcludedFromPerformance);
        }

        ledgerQuery = ApplyRecommendationPerformanceEligibility(
            ledgerQuery,
            candidateOnly,
            topPickOnly,
            includeWatchlist,
            includeResearch,
            includeStale);

        var latestLedgerDate = await ledgerQuery
            .Select(row => (DateOnly?)row.ExpectedResultDate)
            .DefaultIfEmpty()
            .MaxAsync(cancellationToken);
        var selectedDate = normalizedGradingMode switch
        {
            "currentSlate" => latestLedgerDate,
            "latestCompleted" => latestCompletedDate,
            _ => null
        };
        var baseSelectedQuery = selectedDate.HasValue
            ? ledgerQuery.Where(row => row.ExpectedResultDate == selectedDate.Value)
            : ledgerQuery;
        var selectedRowsBeforeGradeability = await baseSelectedQuery
            .OrderByDescending(row => row.RankingScore)
            .ThenByDescending(row => row.EstimatedEv)
            .Take(5000)
            .ToArrayAsync(cancellationToken);
        var selectedRows = selectedRowsBeforeGradeability
            .Where(HasPerformanceGradeability)
            .ToArray();
        var gradedRows = await GradeLedgerRowsAsync(selectedRows, dbContext, cancellationToken);
        var selectableRows = gradedRows
            .Where(row => !gradedOnly || IsGradedOutcome(row.Outcome))
            .ToArray();
        var selectedResetRows = selectableRows
            .Take(take)
            .ToArray();
        var excludedRowsSkipped = includeExcluded
            ? 0
            : await CountExcludedResetCandidatesAsync(
                ledgerPreExclusionQuery,
                selectedDate,
                candidateOnly,
                topPickOnly,
                includeWatchlist,
                includeResearch,
                includeStale,
                gradedOnly,
                dbContext,
                cancellationToken);
        var before = BuildResetPerformanceSummary(gradedRows);
        var selectedIds = selectedResetRows.Select(row => row.Snapshot.Id).ToHashSet();
        var after = BuildResetPerformanceSummary(gradedRows.Where(row => !selectedIds.Contains(row.Snapshot.Id)).ToArray());

        return new PerformanceResetSelectionResult(
            LatestCompletedDate: latestCompletedDate,
            FiltersApplied: new
            {
                requestedSport,
                effectiveSport,
                provider,
                propType,
                candidateOnly,
                topPickOnly,
                includeWatchlist,
                includeResearch,
                includeStale,
                includeExcluded,
                gradingMode = normalizedGradingMode,
                gradedOnly,
                take
            },
            GradedRowsAvailableBeforeTake: selectableRows.Length,
            AlreadyExcludedRowsSkipped: excludedRowsSkipped,
            PublicPerformanceBeforeReset: before,
            ExpectedPublicPerformanceAfterReset: after,
            Rows: selectedResetRows.Select(row => new PerformanceResetSelectionRow(
                RecommendationSnapshotId: row.Snapshot.Id,
                PlayerName: row.Snapshot.PlayerName,
                PropType: row.Snapshot.PropType,
                Provider: row.Snapshot.Provider,
                Side: row.Snapshot.Side,
                Line: row.Snapshot.Line,
                FinalGradeStatus: ToSnapshotGradeStatus(row.Outcome),
                CapturedAtUtc: row.Snapshot.CapturedAtUtc,
                ExpectedResultDate: row.Snapshot.ExpectedResultDate,
                AlreadyExcluded: row.Snapshot.IsExcludedFromPerformance,
                PerformanceExclusionReason: row.Snapshot.PerformanceExclusionReason,
                PerformanceExcludedAtUtc: row.Snapshot.PerformanceExcludedAtUtc,
                PerformanceExcludedBy: row.Snapshot.PerformanceExcludedBy)).ToArray());
    }

    private static async Task<LedgerPerformanceRow[]> GradeLedgerRowsAsync(
        RecommendationSnapshot[] selectedRows,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var resultDates = selectedRows.Select(row => row.ExpectedResultDate).Distinct().ToArray();
        var playerIds = selectedRows
            .Where(row => row.CanonicalPlayerId.HasValue)
            .Select(row => row.CanonicalPlayerId!.Value)
            .Distinct()
            .ToArray();
        var resultRows = resultDates.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(row => resultDates.Contains(row.EventDate))
                .Select(row => new LedgerActualResultLookupRow(
                    row.Id,
                    row.EventDate,
                    row.PlayerId,
                    row.Player != null ? row.Player.DisplayName : string.Empty,
                    row.PropTypeId,
                    row.PropType != null ? row.PropType.Name : string.Empty,
                    row.ActualResultValue))
                .ToArrayAsync(cancellationToken);
        var diagnosticResultDates = resultDates
            .SelectMany(date => new[] { date.AddDays(-1), date, date.AddDays(1) })
            .Distinct()
            .ToArray();
        var diagnosticResultRows = diagnosticResultDates.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(row => diagnosticResultDates.Contains(row.EventDate))
                .Select(row => new LedgerDiagnosticResultRow(
                    row.Id,
                    row.EventDate,
                    row.PlayerId,
                    row.PlayerId,
                    row.Player != null ? row.Player.DisplayName : string.Empty,
                    row.PropTypeId,
                    row.PropType != null ? row.PropType.Name : string.Empty,
                    row.ActualResultValue))
                .ToArrayAsync(cancellationToken);

        return selectedRows
            .Select(row => GradeLedgerPerformanceRow(row, resultRows, diagnosticResultRows))
            .ToArray();
    }

    private static async Task<int> CountExcludedResetCandidatesAsync(
        IQueryable<RecommendationSnapshot> ledgerPreExclusionQuery,
        DateOnly? selectedDate,
        bool candidateOnly,
        bool topPickOnly,
        bool includeWatchlist,
        bool includeResearch,
        bool includeStale,
        bool gradedOnly,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var excludedQuery = ApplyRecommendationPerformanceEligibility(
            ledgerPreExclusionQuery.Where(row => row.IsExcludedFromPerformance),
            candidateOnly,
            topPickOnly,
            includeWatchlist,
            includeResearch,
            includeStale);
        if (selectedDate.HasValue)
        {
            excludedQuery = excludedQuery.Where(row => row.ExpectedResultDate == selectedDate.Value);
        }

        var excludedRows = (await excludedQuery
                .OrderByDescending(row => row.RankingScore)
                .ThenByDescending(row => row.EstimatedEv)
                .Take(5000)
                .ToArrayAsync(cancellationToken))
            .Where(HasPerformanceGradeability)
            .ToArray();
        if (!gradedOnly)
        {
            return excludedRows.Length;
        }

        var gradedExcludedRows = await GradeLedgerRowsAsync(excludedRows, dbContext, cancellationToken);
        return gradedExcludedRows.Count(row => IsGradedOutcome(row.Outcome));
    }

    private static object BuildResetPerformanceSummary(IReadOnlyCollection<LedgerPerformanceRow> rows)
    {
        return new
        {
            gradedPicks = rows.Count(row => IsGradedOutcome(row.Outcome)),
            wins = rows.Count(row => row.Outcome == "Win"),
            losses = rows.Count(row => row.Outcome == "Loss"),
            pushes = rows.Count(row => row.Outcome == "Push"),
            noResults = rows.Count(row => IsNoResultFailure(row.MatchFailureReason))
        };
    }

    private static string ToSnapshotGradeStatus(string? outcome)
    {
        return outcome switch
        {
            "Win" => "Won",
            "Loss" => "Lost",
            "Push" => "Pushed",
            _ => string.IsNullOrWhiteSpace(outcome) ? "NoResult" : outcome
        };
    }

    private static IQueryable<RecommendationSnapshot> ApplyRecommendationPerformanceEligibility(
        IQueryable<RecommendationSnapshot> query,
        bool candidateOnly,
        bool topPickOnly,
        bool includeWatchlist,
        bool includeResearch,
        bool includeStale)
    {
        if (topPickOnly)
        {
            query = query.Where(row => row.IsTopPick);
        }
        else if (candidateOnly)
        {
            query = query.Where(row => row.IsCandidate);
        }
        else if (!includeResearch)
        {
            query = query.Where(row =>
                row.IsCandidate ||
                row.IsTopPick ||
                (includeWatchlist && row.IsWatchlist));
        }

        if (!includeWatchlist)
        {
            query = query.Where(row => !row.IsWatchlist);
        }

        if (!includeResearch)
        {
            query = query.Where(row => row.IsCandidate || row.IsTopPick || row.IsWatchlist);
        }

        if (!includeStale)
        {
            query = query.Where(row =>
                row.IsActionableForUser &&
                row.LifecycleStatus == "Active" &&
                (row.Warning == null || !row.Warning.ToLower().Contains("stale")));
        }

        return query;
    }

    private static bool HasPerformanceGradeability(RecommendationSnapshot row)
    {
        var normalizedPropType = string.IsNullOrWhiteSpace(row.NormalizedPropType)
            ? NormalizePerformancePropTypeKey(row.PropType)
            : NormalizePerformancePropTypeKey(row.NormalizedPropType);
        return row.CanonicalPlayerId.HasValue &&
            row.PropTypeId.HasValue &&
            IsSupportedMlbPerformancePropType(normalizedPropType) &&
            !string.IsNullOrWhiteSpace(row.EventId) &&
            row.EventStartTimeUtc.HasValue &&
            row.ExpectedResultDate != default &&
            ResolveEasternDate(row.EventStartTimeUtc.Value) == row.ExpectedResultDate;
    }

    private static bool IsSupportedMlbPerformancePropType(string? propType)
    {
        var normalizedPropType = NormalizePerformancePropTypeKey(propType);
        return !string.IsNullOrWhiteSpace(normalizedPropType) &&
            SupportedMlbPerformancePropTypeKeys.Contains(normalizedPropType);
    }

    private static bool IsGradedOutcome(string? outcome)
    {
        return outcome is "Win" or "Loss" or "Push";
    }

    private static bool IsNoResultFailure(string? reason)
    {
        return reason is "ProviderLineForInactivePlayer"
            or "NoResultForPlayerPropOnDate"
            or "NoResultForPlayerOnDate"
            or "EventDateMismatch"
            or "MissingLedgerPlayerId"
            or "MissingLedgerPropType"
            or "UnsupportedPropType"
            or "PropTypeNormalizationMismatch"
            or "AmbiguousNormalizedPlayerNameDateStatFallback"
            or "Unknown";
    }

    private static bool IsPendingFailure(string? reason)
    {
        return reason is "NoPropResultsForDate";
    }

    private static (string? Blocker, string? Detail) BuildLatestGradedBlocker(
        string gradingMode,
        DateOnly? selectedDate,
        int totalGraded,
        IReadOnlyCollection<PerformancePickRow> scoredSnapshots,
        IReadOnlyCollection<ActualResultLookupRow> actualResultRows,
        IReadOnlyCollection<PerformanceDateDiagnostics> dateDiagnostics)
    {
        if ((!string.Equals(gradingMode, "latestGraded", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(gradingMode, "latestCompleted", StringComparison.OrdinalIgnoreCase))
            || totalGraded > 0)
        {
            return (null, null);
        }

        if (scoredSnapshots.Count == 0)
        {
            return (
                "NoCapturedRecommendations",
                "No captured recommendation snapshots are available for the requested filters.");
        }

        if (actualResultRows.Count == 0)
        {
            return (
                "NoImportedResults",
                "No imported prop_results are available for the captured recommendation dates.");
        }

        var diagnosticsToInspect = selectedDate.HasValue
            ? dateDiagnostics.Where(row => row.SlateDate == selectedDate.Value).ToArray()
            : dateDiagnostics;
        var datesWithResults = diagnosticsToInspect
            .Where(row => row.PropResultsAvailableForDate > 0)
            .ToArray();
        if (datesWithResults.Length == 0)
        {
            return (
                "NoResultForSnapshotDates",
                "Captured recommendations exist, but imported results do not cover their slate dates yet.");
        }

        var combinedFailures = datesWithResults
            .SelectMany(row => row.MatchFailureCounts)
            .GroupBy(row => row.Key)
            .OrderByDescending(group => group.Sum(row => row.Value))
            .Select(group => $"{group.Key}: {group.Sum(row => row.Value)}")
            .ToArray();

        return (
            "NoSafeMatchesForSnapshotPlayers",
            $"Imported results exist through {datesWithResults.Max(row => row.SlateDate):MMM d}, but captured recommendations did not safely match result rows by canonical id, alias-equivalent id, or unique normalized-name fallback. Failure counts: {string.Join(", ", combinedFailures)}.");
    }

    private static PerformanceGradedPickRow GradePerformancePick(
        PerformancePickRow row,
        IReadOnlyDictionary<ActualResultPlayerLookupKey, decimal> actualResultsByPlayerId,
        IReadOnlyCollection<ActualResultLookupRow> actualResultRows)
    {
        var normalizedPropType = NormalizePerformancePropTypeKey(row.Snapshot.PropType);
        decimal? actualResult = null;
        var matchLookupMode = "Unmatched";
        string? matchFailureReason = null;

        if (string.IsNullOrWhiteSpace(normalizedPropType))
        {
            matchFailureReason = "UnsupportedPropType";
        }

        if (matchFailureReason is null && row.ResolvedPlayerId.HasValue)
        {
            foreach (var playerId in row.EquivalentPlayerIds.Prepend(row.ResolvedPlayerId.Value).Distinct())
            {
                var playerIdKey = new ActualResultPlayerLookupKey(
                    row.Snapshot.SlateDate,
                    playerId,
                    normalizedPropType);
                if (!actualResultsByPlayerId.TryGetValue(playerIdKey, out var value))
                {
                    continue;
                }

                actualResult = value;
                matchLookupMode = playerId == row.ResolvedPlayerId.Value
                    ? "CanonicalPlayerId"
                    : "AliasEquivalentPlayerId";
                break;
            }
        }

        if (actualResult is null && matchFailureReason is null)
        {
            var uniqueFallback = TryResolveUniquePerformanceNameFallback(
                row,
                normalizedPropType,
                actualResultRows);
            if (uniqueFallback.HasValue)
            {
                actualResult = uniqueFallback.Value;
                matchLookupMode = "NormalizedNameUniqueFallback";
            }
        }

        if (actualResult is null && matchFailureReason is null)
        {
            matchFailureReason = DeterminePerformanceMatchFailureReason(row, normalizedPropType, actualResultRows);
        }

        var outcome = GradePerformanceOutcome(row.Snapshot.BestSide, row.Snapshot.Line, actualResult);
        var profit = CalculatePerformanceProfit(outcome, row.Snapshot.BestDecimalPrice);

        return new PerformanceGradedPickRow(
            row.Snapshot,
            row.HistoricalSampleSize,
            row.ResolvedPlayerId,
            row.EquivalentPlayerIds,
            row.PlayerResolutionMode,
            row.PlayerResolutionFailureReason,
            actualResult,
            outcome,
            profit,
            matchLookupMode,
            matchFailureReason);
    }

    private static string DeterminePerformanceMatchFailureReason(
        PerformancePickRow row,
        string normalizedPropType,
        IReadOnlyCollection<ActualResultLookupRow> actualResultRows)
    {
        var dateRows = actualResultRows
            .Where(result => result.EventDate == row.Snapshot.SlateDate)
            .ToArray();
        if (dateRows.Length == 0)
        {
            return "NoResultForDate";
        }

        if (string.IsNullOrWhiteSpace(normalizedPropType))
        {
            return "UnsupportedPropType";
        }

        var propRows = dateRows
            .Where(result => string.Equals(
                NormalizePerformancePropTypeKey(result.PropType),
                normalizedPropType,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (propRows.Length == 0)
        {
            return "PropTypeMismatch";
        }

        if (row.ResolvedPlayerId.HasValue)
        {
            var equivalentIds = row.EquivalentPlayerIds.Prepend(row.ResolvedPlayerId.Value).Distinct().ToHashSet();
            return propRows.Any(result => equivalentIds.Contains(result.PlayerId))
                ? "MatchedPlayerAndPropButNoResult"
                : "PlayerIdMismatch";
        }

        var normalizedSnapshotPlayer = NormalizePerformanceKey(row.Snapshot.Player);
        var normalizedNameMatches = propRows
            .Where(result => NormalizePerformanceKey(result.PlayerName) == normalizedSnapshotPlayer)
            .Select(result => result.PlayerId)
            .Distinct()
            .Count();
        if (normalizedNameMatches > 1)
        {
            return "AmbiguousNameFallback";
        }

        if (normalizedNameMatches == 0)
        {
            return row.ResolvedPlayerId.HasValue ? "PlayerIdMismatch" : "PlayerIdMissingOnSnapshot";
        }

        return row.ResolvedPlayerId.HasValue ? "PlayerIdMismatch" : "Unknown";
    }

    private static string BuildPerformanceDateMatchStrategy(IReadOnlyCollection<PerformanceGradedPickRow> rows)
    {
        var gradedRows = rows.Where(row => IsGradedOutcome(row.Outcome)).ToArray();
        if (gradedRows.Length == 0)
        {
            return "No matched rows.";
        }

        var modes = gradedRows
            .Select(row => row.MatchLookupMode)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return modes.Length == 0
            ? "Matched rows."
            : string.Join(" + ", modes);
    }

    private static async Task<IReadOnlyDictionary<Guid, Guid[]>> LoadPerformanceEquivalentPlayerIdsAsync(
        IReadOnlyCollection<PerformancePickRow> rows,
        IPlayerIdentityAliasService playerIdentityAliasService,
        CancellationToken cancellationToken)
    {
        var resolvedPlayerIds = rows
            .Where(row => row.ResolvedPlayerId.HasValue)
            .Select(row => row.ResolvedPlayerId!.Value)
            .Distinct()
            .ToArray();
        var result = new Dictionary<Guid, Guid[]>();

        foreach (var playerId in resolvedPlayerIds)
        {
            var equivalentIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
                playerId,
                "MLB",
                cancellationToken);
            result[playerId] = equivalentIds
                .Append(playerId)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
        }

        return result;
    }

    private static decimal? TryResolveUniquePerformanceNameFallback(
        PerformancePickRow row,
        string normalizedPropType,
        IReadOnlyCollection<ActualResultLookupRow> actualResultRows)
    {
        if (string.IsNullOrWhiteSpace(normalizedPropType))
        {
            return null;
        }

        var normalizedSnapshotPlayer = NormalizePerformanceKey(row.Snapshot.Player);
        var candidates = actualResultRows
            .Where(result => result.EventDate == row.Snapshot.SlateDate
                && string.Equals(NormalizePerformancePropTypeKey(result.PropType), normalizedPropType, StringComparison.OrdinalIgnoreCase)
                && NormalizePerformanceKey(result.PlayerName) == normalizedSnapshotPlayer)
            .GroupBy(result => result.PlayerId)
            .ToArray();

        return candidates.Length == 1
            ? candidates[0].First().ActualResultValue
            : null;
    }

    private static async Task<IReadOnlyDictionary<Guid, PerformanceSnapshotPlayerResolution>> ResolvePerformanceSnapshotPlayerIdsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Models.MarketLineShoppingSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return new Dictionary<Guid, PerformanceSnapshotPlayerResolution>();
        }

        var result = new Dictionary<Guid, PerformanceSnapshotPlayerResolution>();
        foreach (var snapshot in snapshots)
        {
            var detailsPlayerId = ExtractPerformanceSnapshotPlayerId(snapshot.DetailsJson);
            if (detailsPlayerId.HasValue)
            {
                result[snapshot.Id] = new PerformanceSnapshotPlayerResolution(detailsPlayerId.Value, "DetailsJsonCanonicalPlayerId", null);
            }
        }

        var unresolvedNames = snapshots
            .Where(snapshot => !result.ContainsKey(snapshot.Id))
            .Select(snapshot => snapshot.Player)
            .Where(player => !string.IsNullOrWhiteSpace(player))
            .Select(player => new { Player = player, Normalized = NormalizePerformanceKey(player) })
            .Where(row => !string.IsNullOrWhiteSpace(row.Normalized))
            .DistinctBy(row => row.Normalized)
            .ToArray();

        if (unresolvedNames.Length == 0)
        {
            foreach (var snapshot in snapshots.Where(snapshot => !result.ContainsKey(snapshot.Id)))
            {
                result[snapshot.Id] = new PerformanceSnapshotPlayerResolution(null, "NormalizedNameFallback", "Snapshot did not include a player name.");
            }

            return result;
        }

        var players = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.League != null && player.League.Code == "MLB")
            .Select(player => new { player.Id, player.DisplayName })
            .ToArrayAsync(cancellationToken);
        var playerIds = players.Select(player => player.Id).ToArray();
        var resultCounts = playerIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.PropResults
                .AsNoTracking()
                .Where(row => playerIds.Contains(row.PlayerId))
                .GroupBy(row => row.PlayerId)
                .Select(group => new { PlayerId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.PlayerId, row => row.Count, cancellationToken);

        var playersByNormalizedName = players
            .GroupBy(player => NormalizePerformanceKey(player.DisplayName))
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var resolvedByName = new Dictionary<string, PerformanceSnapshotPlayerResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var unresolvedName in unresolvedNames)
        {
            if (!playersByNormalizedName.TryGetValue(unresolvedName.Normalized, out var candidates))
            {
                resolvedByName[unresolvedName.Normalized] = new PerformanceSnapshotPlayerResolution(
                    null,
                    "NormalizedNameFallback",
                    "No canonical MLB player matched the snapshot player name.");
                continue;
            }

            var ranked = candidates
                .Select(candidate => new
                {
                    candidate.Id,
                    ResultCount = resultCounts.TryGetValue(candidate.Id, out var count) ? count : 0
                })
                .OrderByDescending(candidate => candidate.ResultCount)
                .ThenBy(candidate => candidate.Id)
                .ToArray();
            if (ranked.Length == 1 || ranked[0].ResultCount > ranked.Skip(1).Select(candidate => candidate.ResultCount).DefaultIfEmpty(0).Max())
            {
                resolvedByName[unresolvedName.Normalized] = new PerformanceSnapshotPlayerResolution(
                    ranked[0].Id,
                    "ResolvedByNormalizedName",
                    ranked[0].ResultCount == 0 ? "Player resolved by name but has no prop_results rows." : null);
            }
            else
            {
                resolvedByName[unresolvedName.Normalized] = new PerformanceSnapshotPlayerResolution(
                    null,
                    "NormalizedNameFallback",
                    "Multiple canonical MLB players matched the snapshot player name.");
            }
        }

        foreach (var snapshot in snapshots.Where(snapshot => !result.ContainsKey(snapshot.Id)))
        {
            var normalized = NormalizePerformanceKey(snapshot.Player);
            result[snapshot.Id] = resolvedByName.TryGetValue(normalized, out var resolution)
                ? resolution
                : new PerformanceSnapshotPlayerResolution(null, "NormalizedNameFallback", "Snapshot player could not be resolved.");
        }

        return result;
    }

    private static Guid? ExtractPerformanceSnapshotPlayerId(JsonDocument detailsJson)
    {
        var root = detailsJson.RootElement;
        if (TryReadGuidProperty(root, "canonicalPlayerId", out var canonicalPlayerId)
            || TryReadGuidProperty(root, "playerId", out canonicalPlayerId))
        {
            return canonicalPlayerId;
        }

        if (TryGetPropertyIgnoreCase(root, "bestLine", out var bestLine)
            && (TryReadGuidProperty(bestLine, "canonicalPlayerId", out canonicalPlayerId)
                || TryReadGuidProperty(bestLine, "playerId", out canonicalPlayerId)))
        {
            return canonicalPlayerId;
        }

        return null;
    }

    private static bool TryReadGuidProperty(JsonElement element, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value)
            && value != Guid.Empty)
        {
            return true;
        }

        return false;
    }

    private static object[] BuildPerformanceUnmatchedExamples(
        IReadOnlyCollection<PerformanceGradedPickRow> gradedRows,
        IReadOnlyCollection<ActualResultLookupRow> actualResultRows,
        int take)
    {
        return gradedRows
            .Where(row => !IsGradedOutcome(row.Outcome))
            .OrderByDescending(row => row.Snapshot.SlateDate)
            .ThenBy(row => row.Snapshot.Player)
            .Take(take)
            .Select(row =>
            {
                var normalizedPropType = NormalizePerformancePropTypeKey(row.Snapshot.PropType);
                var normalizedPlayer = NormalizePerformanceKey(row.Snapshot.Player);
                var equivalentIds = row.EquivalentPlayerIds
                    .Concat(row.ResolvedPlayerId.HasValue ? [row.ResolvedPlayerId.Value] : [])
                    .Distinct()
                    .ToHashSet();
                var candidates = actualResultRows
                    .Where(result => result.EventDate == row.Snapshot.SlateDate
                        || equivalentIds.Contains(result.PlayerId)
                        || NormalizePerformanceKey(result.PlayerName) == normalizedPlayer)
                    .Select(result => new
                    {
                        result.EventDate,
                        result.PlayerId,
                        canonicalPlayerId = result.PlayerId,
                        playerIdSource = "prop_results.PlayerId -> canonical players.id from MLB player_external_refs",
                        result.PlayerName,
                        result.PropType,
                        normalizedResultPropType = NormalizePerformancePropTypeKey(result.PropType),
                        result.ActualResultValue,
                        namesMatch = NormalizePerformanceKey(result.PlayerName) == normalizedPlayer,
                        propTypesMatch = NormalizePerformancePropTypeKey(result.PropType) == normalizedPropType,
                        datesMatch = result.EventDate == row.Snapshot.SlateDate,
                        playerIdsEquivalent = equivalentIds.Contains(result.PlayerId),
                        score =
                            (NormalizePerformanceKey(result.PlayerName) == normalizedPlayer ? 16 : 0)
                            + (equivalentIds.Contains(result.PlayerId) ? 8 : 0)
                            + (NormalizePerformancePropTypeKey(result.PropType) == normalizedPropType ? 4 : 0)
                            + (result.EventDate == row.Snapshot.SlateDate ? 2 : 0)
                    })
                    .OrderByDescending(candidate => candidate.score)
                    .ThenByDescending(candidate => candidate.EventDate)
                    .ThenBy(candidate => candidate.PlayerName)
                    .Take(3)
                    .ToArray();

                return new
                {
                    snapshotId = row.Snapshot.Id,
                    snapshotCreatedAtUtc = row.Snapshot.GeneratedAtUtc,
                    slateDate = row.Snapshot.SlateDate,
                    expectedResultDate = ResolveSnapshotExpectedResultDate(row.Snapshot),
                    eventStartTimeUtc = row.Snapshot.EventStartTime,
                    player = row.Snapshot.Player,
                    provider = row.Snapshot.EntryProvider,
                    bookmaker = row.Snapshot.BestBookmaker ?? row.Snapshot.Bookmaker,
                    snapshotPlayerId = row.ResolvedPlayerId,
                    canonicalPlayerId = row.ResolvedPlayerId,
                    snapshotPlayerIdSource = BuildSnapshotPlayerIdSource(row.PlayerResolutionMode),
                    resolvedPlayerId = row.ResolvedPlayerId,
                    resolvedPlayerIdSource = BuildSnapshotPlayerIdSource(row.PlayerResolutionMode),
                    equivalentPlayerIds = row.EquivalentPlayerIds,
                    playerResolutionMode = row.PlayerResolutionMode,
                    playerResolutionFailureReason = row.PlayerResolutionFailureReason,
                    propType = row.Snapshot.PropType,
                    normalizedPropType,
                    side = row.Snapshot.BestSide,
                    line = row.Snapshot.Line,
                    matchFailureReason = row.MatchFailureReason,
                    likelyFailureReason = BuildPerformanceLikelyFailureReason(row, candidates.Any(candidate => candidate.namesMatch), candidates.Any(candidate => candidate.propTypesMatch), candidates.Any(candidate => candidate.datesMatch)),
                    nearbyDateDiagnostics = BuildPerformanceNearbyDateDiagnostics(row, normalizedPropType, normalizedPlayer, equivalentIds, actualResultRows),
                    closestResultCandidates = candidates
                };
            })
            .Cast<object>()
            .ToArray();
    }

    private static string NormalizePerformancePropTypeKey(string? propType)
    {
        var resolved = ResolveResultPropTypeKey(propType);
        var value = string.IsNullOrWhiteSpace(resolved) ? propType : resolved;
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit));
    }

    private static DateOnly? ResolveSnapshotExpectedResultDate(Models.MarketLineShoppingSnapshot snapshot)
    {
        return snapshot.EventStartTime.HasValue
            ? DateOnly.FromDateTime(snapshot.EventStartTime.Value.UtcDateTime)
            : snapshot.SlateDate;
    }

    private static string BuildSnapshotPlayerIdSource(string playerResolutionMode)
    {
        return playerResolutionMode switch
        {
            "DetailsJsonCanonicalPlayerId" => "canonical players.id from snapshot DetailsJson",
            "ResolvedByNormalizedName" => "canonical players.id resolved by normalized MLB display name",
            "NormalizedNameFallback" => "unknown",
            _ => "unknown"
        };
    }

    private static object[] BuildPerformanceNearbyDateDiagnostics(
        PerformanceGradedPickRow row,
        string normalizedPropType,
        string normalizedPlayer,
        IReadOnlySet<Guid> equivalentIds,
        IReadOnlyCollection<ActualResultLookupRow> actualResultRows)
    {
        return new[] { row.Snapshot.SlateDate.AddDays(-1), row.Snapshot.SlateDate, row.Snapshot.SlateDate.AddDays(1) }
            .Select(date =>
            {
                var rowsForDate = actualResultRows.Where(result => result.EventDate == date).ToArray();
                return new
                {
                    date,
                    sameDateAndPropTypeCount = rowsForDate.Count(result =>
                        string.Equals(NormalizePerformancePropTypeKey(result.PropType), normalizedPropType, StringComparison.OrdinalIgnoreCase)),
                    sameNormalizedPlayerNameCount = rowsForDate.Count(result =>
                        NormalizePerformanceKey(result.PlayerName) == normalizedPlayer),
                    samePlayerIdOrAliasEquivalentCount = rowsForDate.Count(result => equivalentIds.Contains(result.PlayerId))
                };
            })
            .Cast<object>()
            .ToArray();
    }

    private static string BuildPerformanceLikelyFailureReason(
        PerformanceGradedPickRow row,
        bool hasNameCandidate,
        bool hasPropTypeCandidate,
        bool hasDateCandidate)
    {
        if (string.Equals(row.MatchFailureReason, "PlayerIdMismatch", StringComparison.OrdinalIgnoreCase)
            && hasNameCandidate)
        {
            return "Snapshot player resolved to a different internal player id than the matching MLB result row. Alias/external-ref reconciliation is missing or ambiguous.";
        }

        if (string.Equals(row.MatchFailureReason, "PlayerIdMismatch", StringComparison.OrdinalIgnoreCase))
        {
            return "Imported results exist for the date and prop type, but no safe player identity match was found.";
        }

        if (!hasDateCandidate)
        {
            return "No imported result row exists for this slate date.";
        }

        if (!hasPropTypeCandidate)
        {
            return "Imported result rows exist for the date, but not for this normalized prop type.";
        }

        return row.MatchFailureReason ?? "Unknown";
    }

    private static object[] BuildPerformanceGroups(
        IReadOnlyCollection<PerformanceGradedPickRow> rows,
        string groupType,
        Func<PerformanceGradedPickRow, string?> keySelector)
    {
        return rows
            .GroupBy(row => string.IsNullOrWhiteSpace(keySelector(row)) ? "Unknown" : keySelector(row)!)
            .Select(group => BuildPerformanceGroup(groupType, group.Key, group.ToArray()))
            .OrderByDescending(row => row.gradedPicks)
            .ThenBy(row => row.groupKey)
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildLedgerNoResultExamples(
        IReadOnlyCollection<LedgerPerformanceRow> gradedRows,
        IReadOnlyCollection<LedgerDiagnosticResultRow> diagnosticResultRows,
        int take)
    {
        return BuildLedgerResultDiagnosticExamples(
            gradedRows.Where(row => row.Outcome is null && IsNoResultFailure(row.MatchFailureReason)),
            diagnosticResultRows,
            take);
    }

    private static object[] BuildLedgerUnmatchedExamples(
        IReadOnlyCollection<LedgerPerformanceRow> gradedRows,
        IReadOnlyCollection<LedgerDiagnosticResultRow> diagnosticResultRows,
        int take)
    {
        return BuildLedgerResultDiagnosticExamples(
            gradedRows.Where(row =>
                row.Outcome is null &&
                !IsNoResultFailure(row.MatchFailureReason) &&
                !IsPendingFailure(row.MatchFailureReason)),
            diagnosticResultRows,
            take);
    }

    private static object[] BuildLedgerResultDiagnosticExamples(
        IEnumerable<LedgerPerformanceRow> rows,
        IReadOnlyCollection<LedgerDiagnosticResultRow> diagnosticResultRows,
        int take)
    {
        return rows
            .OrderByDescending(row => row.Snapshot.ExpectedResultDate)
            .ThenBy(row => row.Snapshot.PlayerName)
            .Take(take)
            .Select(row =>
            {
                var snapshot = row.Snapshot;
                var normalizedPlayerName = string.IsNullOrWhiteSpace(snapshot.NormalizedPlayerName)
                    ? NormalizePerformanceKey(snapshot.PlayerName)
                    : snapshot.NormalizedPlayerName;
                var normalizedPropType = string.IsNullOrWhiteSpace(snapshot.NormalizedPropType)
                    ? NormalizePerformancePropTypeKey(snapshot.PropType)
                    : NormalizePerformancePropTypeKey(snapshot.NormalizedPropType);
                var nearbyDates = new[] { snapshot.ExpectedResultDate.AddDays(-1), snapshot.ExpectedResultDate, snapshot.ExpectedResultDate.AddDays(1) };
                var nearbyRows = diagnosticResultRows
                    .Where(result => nearbyDates.Contains(result.EventDate))
                    .ToArray();
                var aliasEquivalentPlayerIds = nearbyRows
                    .Where(result => !string.IsNullOrWhiteSpace(normalizedPlayerName)
                        && string.Equals(result.NormalizedPlayerName, normalizedPlayerName, StringComparison.OrdinalIgnoreCase))
                    .Select(result => result.PlayerId)
                    .Concat(snapshot.CanonicalPlayerId.HasValue ? [snapshot.CanonicalPlayerId.Value] : [])
                    .Distinct()
                    .ToArray();
                var sameDateRows = nearbyRows
                    .Where(result => result.EventDate == snapshot.ExpectedResultDate)
                    .ToArray();
                var sameDateSamePropTypeCount = sameDateRows.Count(result =>
                    LedgerPropTypesMatch(snapshot, result, normalizedPropType));
                var sameDateSamePlayerIdCount = snapshot.CanonicalPlayerId.HasValue
                    ? sameDateRows.Count(result => result.PlayerId == snapshot.CanonicalPlayerId.Value)
                    : 0;
                var sameDateSamePlayerNameCount = sameDateRows.Count(result =>
                    !string.IsNullOrWhiteSpace(normalizedPlayerName)
                    && string.Equals(result.NormalizedPlayerName, normalizedPlayerName, StringComparison.OrdinalIgnoreCase));
                var sameDateSamePlayerAndPropTypeCount = sameDateRows.Count(result =>
                    LedgerPlayersMatch(snapshot, result, aliasEquivalentPlayerIds, normalizedPlayerName)
                    && LedgerPropTypesMatch(snapshot, result, normalizedPropType));
                var samePlayerIdNearbyDatesCount = snapshot.CanonicalPlayerId.HasValue
                    ? nearbyRows.Count(result => result.PlayerId == snapshot.CanonicalPlayerId.Value)
                    : 0;
                var samePlayerNameNearbyDatesCount = nearbyRows.Count(result =>
                    !string.IsNullOrWhiteSpace(normalizedPlayerName)
                    && string.Equals(result.NormalizedPlayerName, normalizedPlayerName, StringComparison.OrdinalIgnoreCase));
                var samePlayerAndPropNearbyDatesCount = nearbyRows.Count(result =>
                    LedgerPlayersMatch(snapshot, result, aliasEquivalentPlayerIds, normalizedPlayerName)
                    && LedgerPropTypesMatch(snapshot, result, normalizedPropType));
                var samePropTypeNearbyDatesCount = nearbyRows.Count(result =>
                    LedgerPropTypesMatch(snapshot, result, normalizedPropType));
                var eventUtcDate = snapshot.EventStartTimeUtc.HasValue
                    ? DateOnly.FromDateTime(snapshot.EventStartTimeUtc.Value.UtcDateTime)
                    : (DateOnly?)null;
                var eventEasternDate = snapshot.EventStartTimeUtc.HasValue
                    ? ResolveEasternDate(snapshot.EventStartTimeUtc.Value)
                    : null;

                return new
                {
                    recommendationSnapshotId = snapshot.Id,
                    snapshot.IdempotencyKey,
                    snapshot.CapturedAtUtc,
                    snapshot.FirstSeenAtUtc,
                    snapshot.LastSeenAtUtc,
                    snapshot.ExpectedResultDate,
                    snapshot.SlateDate,
                    snapshot.EventId,
                    snapshot.EventStartTimeUtc,
                    eventUtcDate,
                    eventEasternDate,
                    eventUtcDateDiffersFromExpectedResultDate = eventUtcDate.HasValue && eventUtcDate.Value != snapshot.ExpectedResultDate,
                    eventEasternDateDiffersFromExpectedResultDate = eventEasternDate.HasValue && eventEasternDate.Value != snapshot.ExpectedResultDate,
                    playerName = snapshot.PlayerName,
                    playerId = snapshot.CanonicalPlayerId,
                    canonicalPlayerId = snapshot.CanonicalPlayerId,
                    normalizedPlayerName,
                    snapshot.Provider,
                    snapshot.EntryProvider,
                    snapshot.Bookmaker,
                    propType = snapshot.PropType,
                    propTypeId = snapshot.PropTypeId,
                    normalizedPropType,
                    snapshot.Side,
                    snapshot.Line,
                    snapshot.StrategyClassification,
                    snapshot.StrategyTier,
                    snapshot.IsCandidate,
                    snapshot.IsTopPick,
                    isBackfilled = !snapshot.RefreshRunId.HasValue && !snapshot.PredictionRunId.HasValue,
                    sourceTable = "recommendation_snapshots",
                    source = snapshot.RefreshRunId.HasValue
                        ? "the_odds_refresh"
                        : snapshot.PredictionRunId.HasValue
                            ? "prediction_run"
                            : "unknown_or_backfill",
                    resultDateSearched = snapshot.ExpectedResultDate,
                    expectedResultDateSource = snapshot.ResultDateSource ?? "recommendation_snapshots.expected_result_date",
                    propResultDateSource = "prop_results.event_date",
                    propResultsForDateCount = sameDateRows.Length,
                    sameDateSamePropTypeCount,
                    sameDateSamePlayerIdCount,
                    sameDateSamePlayerNameCount,
                    sameDateSamePlayerAndPropTypeCount,
                    samePlayerIdNearbyDatesCount,
                    samePlayerNameNearbyDatesCount,
                    samePlayerAndPropNearbyDatesCount,
                    samePropTypeNearbyDatesCount,
                    aliasEquivalentPlayerIds,
                    likelyFailureReason = row.MatchFailureReason ?? BuildLedgerMatchFailureReason(snapshot, diagnosticResultRows),
                    closestResultCandidates = BuildLedgerClosestResultCandidates(snapshot, nearbyRows, aliasEquivalentPlayerIds, normalizedPlayerName, normalizedPropType)
                };
            })
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildLedgerClosestResultCandidates(
        RecommendationSnapshot snapshot,
        IReadOnlyCollection<LedgerDiagnosticResultRow> nearbyRows,
        IReadOnlyCollection<Guid> aliasEquivalentPlayerIds,
        string normalizedPlayerName,
        string normalizedPropType)
    {
        var candidates = nearbyRows
            .Select(result =>
            {
                var datesMatch = result.EventDate == snapshot.ExpectedResultDate;
                var playerIdsMatch = snapshot.CanonicalPlayerId.HasValue && result.PlayerId == snapshot.CanonicalPlayerId.Value;
                var playerIdsAliasEquivalent = aliasEquivalentPlayerIds.Contains(result.PlayerId);
                var namesMatch = !string.IsNullOrWhiteSpace(normalizedPlayerName)
                    && string.Equals(result.NormalizedPlayerName, normalizedPlayerName, StringComparison.OrdinalIgnoreCase);
                var propTypesMatch = LedgerPropTypesMatch(snapshot, result, normalizedPropType);
                var priority =
                    datesMatch && playerIdsMatch ? 1 :
                    datesMatch && namesMatch ? 2 :
                    datesMatch && propTypesMatch ? 3 :
                    !datesMatch && playerIdsMatch && propTypesMatch ? 4 :
                    !datesMatch && namesMatch && propTypesMatch ? 5 :
                    99;

                return new
                {
                    priority,
                    result.PropResultId,
                    resultDate = result.EventDate,
                    result.PlayerName,
                    result.PlayerId,
                    result.CanonicalPlayerId,
                    result.NormalizedPlayerName,
                    result.PropType,
                    result.PropTypeId,
                    result.NormalizedPropType,
                    value = result.ActualResultValue,
                    namesMatch,
                    playerIdsMatch,
                    playerIdsAliasEquivalent,
                    propTypesMatch,
                    datesMatch
                };
            })
            .Where(candidate => candidate.priority < 99)
            .OrderBy(candidate => candidate.priority)
            .ThenBy(candidate => Math.Abs(candidate.resultDate.DayNumber - snapshot.ExpectedResultDate.DayNumber))
            .ThenBy(candidate => candidate.PlayerName)
            .Take(10)
            .Select(candidate => new
            {
                candidate.PropResultId,
                candidate.resultDate,
                candidate.PlayerName,
                candidate.PlayerId,
                candidate.CanonicalPlayerId,
                candidate.NormalizedPlayerName,
                candidate.PropType,
                candidate.PropTypeId,
                candidate.NormalizedPropType,
                candidate.value,
                candidate.namesMatch,
                candidate.playerIdsMatch,
                candidate.playerIdsAliasEquivalent,
                candidate.propTypesMatch,
                candidate.datesMatch
            })
            .Cast<object>()
            .ToArray();

        return candidates;
    }

    private static LedgerPerformanceRow GradeLedgerPerformanceRow(
        RecommendationSnapshot row,
        IReadOnlyCollection<LedgerActualResultLookupRow> actualResults,
        IReadOnlyCollection<LedgerDiagnosticResultRow> diagnosticResultRows)
    {
        decimal? actualResult = null;
        string? matchFailureReason = null;
        string? matchMethod = null;

        if (!row.CanonicalPlayerId.HasValue)
        {
            matchFailureReason = "MissingLedgerPlayerId";
        }
        else if (!row.PropTypeId.HasValue)
        {
            matchFailureReason = "MissingLedgerPropType";
        }
        else
        {
            var normalizedPropType = string.IsNullOrWhiteSpace(row.NormalizedPropType)
                ? NormalizePerformancePropTypeKey(row.PropType)
                : NormalizePerformancePropTypeKey(row.NormalizedPropType);
            if (!IsSupportedMlbPerformancePropType(normalizedPropType))
            {
                matchFailureReason = "UnsupportedPropType";
            }
            else
            {
                var normalizedPlayerName = string.IsNullOrWhiteSpace(row.NormalizedPlayerName)
                    ? NormalizePerformanceKey(row.PlayerName)
                    : NormalizePerformanceKey(row.NormalizedPlayerName);
                var matches = actualResults
                .Where(result => result.EventDate == row.ExpectedResultDate
                    && result.PlayerId == row.CanonicalPlayerId.Value
                    && (result.PropTypeId == row.PropTypeId.Value ||
                        string.Equals(
                            NormalizePerformancePropTypeKey(result.PropType),
                            normalizedPropType,
                            StringComparison.OrdinalIgnoreCase)))
                .ToArray();
                var normalizedNameMatches = matches.Length == 0 && !string.IsNullOrWhiteSpace(normalizedPlayerName)
                    ? actualResults
                        .Where(result => result.EventDate == row.ExpectedResultDate
                            && string.Equals(result.NormalizedPlayerName, normalizedPlayerName, StringComparison.OrdinalIgnoreCase)
                            && (result.PropTypeId == row.PropTypeId.Value ||
                                string.Equals(
                                    NormalizePerformancePropTypeKey(result.PropType),
                                    normalizedPropType,
                                    StringComparison.OrdinalIgnoreCase)))
                        .ToArray()
                    : [];

                if (matches.Length == 1)
                {
                    actualResult = matches[0].ActualResultValue;
                    matchMethod = "ExactPlayerIdDateStat";
                }
                else if (matches.Length > 1)
                {
                    matchFailureReason = "MultipleResultRowsForPlayerPropDate";
                }
                else if (normalizedNameMatches.Length == 1)
                {
                    actualResult = normalizedNameMatches[0].ActualResultValue;
                    matchMethod = "NormalizedPlayerNameDateStatFallback";
                }
                else if (normalizedNameMatches.Length > 1)
                {
                    matchFailureReason = "AmbiguousNormalizedPlayerNameDateStatFallback";
                }
                else
                {
                    matchFailureReason = BuildLedgerMatchFailureReason(row, diagnosticResultRows);
                }
            }
        }

        var outcome = GradePerformanceOutcome(row.Side, row.Line, actualResult);
        decimal? profit = outcome switch
        {
            "Win" => 1m,
            "Loss" => -1m,
            "Push" => 0m,
            _ => null
        };

        return new LedgerPerformanceRow(row, actualResult, outcome, profit, matchFailureReason, matchMethod);
    }

    private static string BuildLedgerMatchFailureReason(
        RecommendationSnapshot row,
        IReadOnlyCollection<LedgerDiagnosticResultRow> diagnosticResultRows)
    {
        if (!row.CanonicalPlayerId.HasValue)
        {
            return "MissingLedgerPlayerId";
        }

        if (!row.PropTypeId.HasValue)
        {
            return "MissingLedgerPropType";
        }

        var normalizedPlayerName = string.IsNullOrWhiteSpace(row.NormalizedPlayerName)
            ? NormalizePerformanceKey(row.PlayerName)
            : row.NormalizedPlayerName;
        var normalizedPropType = string.IsNullOrWhiteSpace(row.NormalizedPropType)
            ? NormalizePerformancePropTypeKey(row.PropType)
            : NormalizePerformancePropTypeKey(row.NormalizedPropType);
        var nearbyDates = new[] { row.ExpectedResultDate.AddDays(-1), row.ExpectedResultDate, row.ExpectedResultDate.AddDays(1) };
        var nearbyRows = diagnosticResultRows
            .Where(result => nearbyDates.Contains(result.EventDate))
            .ToArray();
        var sameDateRows = nearbyRows
            .Where(result => result.EventDate == row.ExpectedResultDate)
            .ToArray();
        var aliasEquivalentPlayerIds = nearbyRows
            .Where(result => !string.IsNullOrWhiteSpace(normalizedPlayerName)
                && string.Equals(result.NormalizedPlayerName, normalizedPlayerName, StringComparison.OrdinalIgnoreCase))
            .Select(result => result.PlayerId)
            .Append(row.CanonicalPlayerId.Value)
            .Distinct()
            .ToArray();
        var hasSameDateSamePlayerId = sameDateRows.Any(result => result.PlayerId == row.CanonicalPlayerId.Value);
        var hasSameDateSamePlayerName = sameDateRows.Any(result =>
            !string.IsNullOrWhiteSpace(normalizedPlayerName)
            && string.Equals(result.NormalizedPlayerName, normalizedPlayerName, StringComparison.OrdinalIgnoreCase));
        var hasSameDateSamePropType = sameDateRows.Any(result => LedgerPropTypesMatch(row, result, normalizedPropType));
        var hasSameDatePlayerAndProp = sameDateRows.Any(result =>
            LedgerPlayersMatch(row, result, aliasEquivalentPlayerIds, normalizedPlayerName)
            && LedgerPropTypesMatch(row, result, normalizedPropType));
        var hasNearbyPlayerAndProp = nearbyRows.Any(result =>
            result.EventDate != row.ExpectedResultDate
            && LedgerPlayersMatch(row, result, aliasEquivalentPlayerIds, normalizedPlayerName)
            && LedgerPropTypesMatch(row, result, normalizedPropType));

        if (hasNearbyPlayerAndProp)
        {
            return "EventDateMismatch";
        }

        if (sameDateRows.Length == 0)
        {
            return "NoPropResultsForDate";
        }

        if ((hasSameDateSamePlayerId || hasSameDateSamePlayerName) && !hasSameDatePlayerAndProp)
        {
            return "NoResultForPlayerPropOnDate";
        }

        if (hasSameDateSamePropType && !hasSameDateSamePlayerId && hasSameDateSamePlayerName)
        {
            return "PlayerIdMismatch";
        }

        if (hasSameDateSamePropType)
        {
            return "ProviderLineForInactivePlayer";
        }

        if (!hasSameDateSamePlayerId && !hasSameDateSamePlayerName)
        {
            return "NoResultForPlayerOnDate";
        }

        return "Unknown";
    }

    private static bool LedgerPlayersMatch(
        RecommendationSnapshot snapshot,
        LedgerDiagnosticResultRow result,
        IReadOnlyCollection<Guid> aliasEquivalentPlayerIds,
        string normalizedPlayerName)
    {
        return (snapshot.CanonicalPlayerId.HasValue && result.PlayerId == snapshot.CanonicalPlayerId.Value)
            || aliasEquivalentPlayerIds.Contains(result.PlayerId)
            || (!string.IsNullOrWhiteSpace(normalizedPlayerName)
                && string.Equals(result.NormalizedPlayerName, normalizedPlayerName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LedgerPropTypesMatch(
        RecommendationSnapshot snapshot,
        LedgerDiagnosticResultRow result,
        string normalizedPropType)
    {
        return (snapshot.PropTypeId.HasValue && result.PropTypeId == snapshot.PropTypeId.Value)
            || string.Equals(result.NormalizedPropType, normalizedPropType, StringComparison.OrdinalIgnoreCase);
    }

    private static DateOnly? ResolveEasternDate(DateTimeOffset value)
    {
        try
        {
            var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, eastern).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(value.UtcDateTime);
        }
        catch (InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(value.UtcDateTime);
        }
    }

    private static object[] BuildLedgerPerformanceGroups(
        IReadOnlyCollection<LedgerPerformanceRow> rows,
        string groupType,
        Func<LedgerPerformanceRow, string?> keySelector)
    {
        return rows
            .GroupBy(row => string.IsNullOrWhiteSpace(keySelector(row)) ? "Unknown" : keySelector(row)!)
            .Select(group => BuildLedgerPerformanceGroup(groupType, group.Key, group.ToArray()))
            .OrderByDescending(row => row.gradedPicks)
            .ThenBy(row => row.groupKey)
            .Cast<object>()
            .ToArray();
    }

    private static int CountGradeableGroups(IReadOnlyCollection<LedgerPerformanceRow> rows)
    {
        return CountLedgerPerformanceGroups(rows, group => group.Any(row => IsGradedOutcome(row.Outcome)));
    }

    private static int CountNoResultOnlyGroups(IReadOnlyCollection<LedgerPerformanceRow> rows)
    {
        return CountLedgerPerformanceGroups(rows, group =>
        {
            var groupRows = group.ToArray();
            return groupRows.All(row => !IsGradedOutcome(row.Outcome))
                && groupRows.Any(row => IsNoResultFailure(row.MatchFailureReason))
                && groupRows.All(row => IsNoResultFailure(row.MatchFailureReason));
        });
    }

    private static int CountLedgerPerformanceGroups(
        IReadOnlyCollection<LedgerPerformanceRow> rows,
        Func<IGrouping<string, LedgerPerformanceRow>, bool> predicate)
    {
        return new[]
            {
                rows.GroupBy(row => "All"),
                rows.GroupBy(row => string.IsNullOrWhiteSpace(row.Snapshot.ProviderCategory) ? "Unknown" : row.Snapshot.ProviderCategory),
                rows.GroupBy(row => string.IsNullOrWhiteSpace(row.Snapshot.EntryProvider) ? "Unknown" : row.Snapshot.EntryProvider),
                rows.GroupBy(row => string.IsNullOrWhiteSpace(row.Snapshot.Bookmaker) ? "Unknown" : row.Snapshot.Bookmaker),
                rows.GroupBy(row => string.IsNullOrWhiteSpace(row.Snapshot.PropType) ? "Unknown" : row.Snapshot.PropType),
                rows.GroupBy(row => string.IsNullOrWhiteSpace(row.Snapshot.StrategyClassification) ? "Unknown" : row.Snapshot.StrategyClassification),
                rows.GroupBy(row => string.IsNullOrWhiteSpace(row.Snapshot.StrategyTier) ? "Unknown" : row.Snapshot.StrategyTier),
                rows.GroupBy(row => $"{row.Snapshot.EntryProvider} | {row.Snapshot.PropType}")
            }
            .SelectMany(group => group)
            .Count(predicate);
    }

    private static dynamic BuildLedgerPerformanceGroup(
        string groupType,
        string groupKey,
        IReadOnlyCollection<LedgerPerformanceRow> rows)
    {
        var graded = rows.Where(row => IsGradedOutcome(row.Outcome)).ToArray();
        var wins = graded.Count(row => row.Outcome == "Win");
        var losses = graded.Count(row => row.Outcome == "Loss");
        var pushes = graded.Count(row => row.Outcome == "Push");
        var noResults = rows.Count(row => IsNoResultFailure(row.MatchFailureReason));
        var pending = rows.Count(row => IsPendingFailure(row.MatchFailureReason));
        var unmatched = rows.Count(row =>
            row.Outcome is null &&
            !IsNoResultFailure(row.MatchFailureReason) &&
            !IsPendingFailure(row.MatchFailureReason));
        var decisions = wins + losses;
        var profit = graded.Sum(row => row.Profit ?? 0m);
        var roi = graded.Length == 0 ? (decimal?)null : Math.Round(profit / graded.Length, 4);
        var first = rows.FirstOrDefault()?.Snapshot;

        return new
        {
            groupType,
            groupKey,
            sport = first?.Sport ?? "MLB",
            providerCategory = FirstDistinctOrNull(rows.Select(row => row.Snapshot.ProviderCategory)),
            provider = FirstDistinctOrNull(rows.Select(row => row.Snapshot.EntryProvider)),
            bookmaker = FirstDistinctOrNull(rows.Select(row => row.Snapshot.Bookmaker)),
            propType = FirstDistinctOrNull(rows.Select(row => row.Snapshot.PropType)),
            strategyClassification = FirstDistinctOrNull(rows.Select(row => row.Snapshot.StrategyClassification)),
            strategyTier = FirstDistinctOrNull(rows.Select(row => row.Snapshot.StrategyTier)),
            totalPicks = rows.Count,
            gradedPicks = graded.Length,
            hasGradeableSample = graded.Length > 0,
            isNoResultOnly = graded.Length == 0 && noResults > 0 && unmatched == 0 && pending == 0,
            wins,
            losses,
            pushes,
            noResults,
            unmatched,
            pending,
            hitRate = decisions == 0 ? (decimal?)null : Math.Round(wins / (decimal)decisions, 4),
            averageEstimatedEv = AverageNullable(rows.Select(row => row.Snapshot.EstimatedEv)),
            averageModelProbability = AverageNullable(rows.Select(row => row.Snapshot.HitChance)),
            averageImpliedProbability = AverageNullable(rows.Select(row => row.Snapshot.ImpliedProbability)),
            averageProbabilityEdge = AverageNullable(rows.Select(row => row.Snapshot.ProbabilityEdge)),
            simulatedProfit = Math.Round(profit, 4),
            simulatedRoi = roi,
            averagePrice = null as decimal?,
            latestPickDate = rows.Select(row => (DateOnly?)row.Snapshot.ExpectedResultDate).Max(),
            notes = BuildPerformanceNotes(graded.Length),
            sampleSizeWarning = graded.Length < 30 ? "SmallSample" : null
        };
    }

    private static dynamic BuildPerformanceGroup(
        string groupType,
        string groupKey,
        IReadOnlyCollection<PerformanceGradedPickRow> rows)
    {
        var graded = rows.Where(row => row.Outcome is "Win" or "Loss" or "Push").ToArray();
        var wins = graded.Count(row => row.Outcome == "Win");
        var losses = graded.Count(row => row.Outcome == "Loss");
        var pushes = graded.Count(row => row.Outcome == "Push");
        var decisions = wins + losses;
        var profit = graded.Sum(row => row.Profit ?? 0m);
        var roi = graded.Length == 0 ? (decimal?)null : Math.Round(profit / graded.Length, 4);
        var first = rows.FirstOrDefault()?.Snapshot;

        return new
        {
            groupType,
            groupKey,
            sport = first?.Sport ?? "MLB",
            providerCategory = FirstDistinctOrNull(rows.Select(row => row.Snapshot.ProviderCategory)),
            provider = FirstDistinctOrNull(rows.Select(row => row.Snapshot.EntryProvider)),
            bookmaker = FirstDistinctOrNull(rows.Select(row => row.Snapshot.BestBookmaker ?? row.Snapshot.Bookmaker)),
            propType = FirstDistinctOrNull(rows.Select(row => row.Snapshot.PropType)),
            strategyClassification = FirstDistinctOrNull(rows.Select(row => row.Snapshot.StrategyClassification)),
            strategyTier = FirstDistinctOrNull(rows.Select(row => row.Snapshot.StrategyTier)),
            totalPicks = rows.Count,
            gradedPicks = graded.Length,
            hasGradeableSample = graded.Length > 0,
            isNoResultOnly = false,
            wins,
            losses,
            pushes,
            hitRate = decisions == 0 ? (decimal?)null : Math.Round(wins / (decimal)decisions, 4),
            averageEstimatedEv = AverageNullable(rows.Select(row => row.Snapshot.BestEstimatedEv)),
            averageModelProbability = AverageNullable(rows.Select(row => row.Snapshot.BestProbability)),
            averageImpliedProbability = AverageNullable(rows.Select(row => row.Snapshot.BestImpliedProbability)),
            averageProbabilityEdge = AverageNullable(rows.Select(row => row.Snapshot.BestProbabilityEdge)),
            simulatedProfit = Math.Round(profit, 4),
            simulatedRoi = roi,
            averagePrice = AverageNullable(rows.Select(row => row.Snapshot.BestDecimalPrice)),
            latestPickDate = rows.Select(row => (DateOnly?)row.Snapshot.SlateDate).Max(),
            notes = BuildPerformanceNotes(graded.Length),
            sampleSizeWarning = graded.Length < 30 ? "SmallSample" : null
        };
    }

    private static string? GradePerformanceOutcome(string bestSide, decimal line, decimal? actualResult)
    {
        if (!actualResult.HasValue || string.Equals(bestSide, "None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return bestSide switch
        {
            "Over" when actualResult.Value > line => "Win",
            "Over" when actualResult.Value < line => "Loss",
            "Under" when actualResult.Value < line => "Win",
            "Under" when actualResult.Value > line => "Loss",
            _ when actualResult.Value == line => "Push",
            _ => null
        };
    }

    private static decimal? CalculatePerformanceProfit(string? outcome, decimal? decimalPrice)
    {
        if (outcome is null)
        {
            return null;
        }

        return outcome switch
        {
            "Win" when decimalPrice.HasValue => Math.Round(decimalPrice.Value - 1m, 4),
            "Loss" => -1m,
            "Push" => 0m,
            _ => null
        };
    }

    private static int? ExtractHistoricalSampleSize(System.Text.Json.JsonDocument detailsJson)
    {
        return detailsJson.RootElement.TryGetProperty("historicalSampleSize", out var value) &&
            value.ValueKind == System.Text.Json.JsonValueKind.Number &&
            value.TryGetInt32(out var sample)
            ? sample
            : null;
    }

    private static decimal? AverageNullable(IEnumerable<decimal?> values)
    {
        var numbers = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return numbers.Length == 0 ? null : Math.Round(numbers.Average(), 6);
    }

    private static string? FirstDistinctOrNull(IEnumerable<string?> values)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static string[] BuildPerformanceNotes(int gradedCount)
    {
        return gradedCount switch
        {
            0 => ["No graded picks in this group."],
            < 30 => ["Small graded sample; use as research only."],
            _ => []
        };
    }

    private static async Task<PropHistoryResponse> BuildMarketPropHistoryResponseAsync(
        string? sport,
        Guid? playerId,
        string? playerName,
        string? propType,
        string? side,
        decimal? line,
        int? take,
        int? season,
        string? provider,
        ApplicationDbContext dbContext,
        IPlayerIdentityAliasService playerIdentityAliasService,
        CancellationToken cancellationToken)
    {
        var requestedSport = string.IsNullOrWhiteSpace(sport) ? "MLB" : NormalizeMarketSportCode(sport);
        var displayPropType = ResolveDisplayPropType(propType);
        var normalizedPropType = ResolveResultPropTypeKey(propType);
        var normalizedSide = NormalizeResultSide(side);
        var requestedTake = Math.Clamp(take ?? 10, 1, 50);

        if (!string.Equals(requestedSport, "MLB", StringComparison.OrdinalIgnoreCase))
        {
            var reason = IsSoccerSport(requestedSport)
                ? SoccerHistoryUnavailableMessage
                : $"Prop history currently supports MLB only. Requested sport: {requestedSport}.";
            return EmptyPropHistory(requestedSport, playerId, playerName, normalizedPropType ?? propType, displayPropType, normalizedSide, line, provider, season, reason);
        }

        if (string.IsNullOrWhiteSpace(normalizedPropType))
        {
            return EmptyPropHistory("MLB", playerId, playerName, propType, displayPropType, normalizedSide, line, provider, season, "Unsupported or missing prop type.");
        }

        if (line is null)
        {
            return EmptyPropHistory("MLB", playerId, playerName, normalizedPropType, displayPropType, normalizedSide, null, provider, season, "Line is required to evaluate historical results.");
        }

        var playerResolution = await ResolvePropHistoryPlayerAsync(playerId, playerName, dbContext, cancellationToken);
        if (playerResolution.ResolvedPlayerId is null)
        {
            return EmptyPropHistory(
                "MLB",
                playerId,
                playerName,
                normalizedPropType,
                displayPropType,
                normalizedSide,
                line,
                provider,
                season,
                string.IsNullOrWhiteSpace(playerName)
                    ? "Player id or player name is required."
                    : $"No mapped MLB player was found for '{playerName}'.",
                playerResolution);
        }

        var equivalentPlayerIds = (await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
                playerResolution.ResolvedPlayerId.Value,
                "MLB",
                cancellationToken))
            .Append(playerResolution.ResolvedPlayerId.Value)
            .Distinct()
            .ToArray();
        var propTypeResolution = await ResolvePropHistoryPropTypesAsync(normalizedPropType, dbContext, cancellationToken);
        if (propTypeResolution.PropTypeIds.Length == 0)
        {
            return EmptyPropHistory(
                "MLB",
                playerResolution.ResolvedPlayerId,
                playerResolution.ResolvedPlayerName ?? playerName,
                normalizedPropType,
                displayPropType,
                normalizedSide,
                line,
                provider,
                season,
                $"No persisted prop type mapping exists for '{displayPropType}'.",
                playerResolution,
                propTypeResolution,
                equivalentPlayerIds.Length);
        }

        var resolvedName = playerResolution.ResolvedPlayerName ?? playerName;
        var exactNamePlayerIds = await ResolveExactNamePropHistoryPlayerIdsAsync(resolvedName, dbContext, cancellationToken);
        var directObservationCountAfterPropType = await dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => equivalentPlayerIds.Contains(result.PlayerId)
                && propTypeResolution.PropTypeIds.Contains(result.PropTypeId), cancellationToken);
        if (directObservationCountAfterPropType == 0 && exactNamePlayerIds.Length > 0)
        {
            var exactNameOwners = await dbContext.PropResults
                .AsNoTracking()
                .Where(result => exactNamePlayerIds.Contains(result.PlayerId)
                    && propTypeResolution.PropTypeIds.Contains(result.PropTypeId))
                .GroupBy(result => result.PlayerId)
                .Select(group => new { PlayerId = group.Key, Count = group.Count() })
                .ToArrayAsync(cancellationToken);
            if (exactNameOwners.Length == 1)
            {
                equivalentPlayerIds = equivalentPlayerIds.Append(exactNameOwners[0].PlayerId).Distinct().ToArray();
                playerResolution = playerResolution with
                {
                    LookupMode = "playerId+ExactNameHistoryOwnerFallback",
                    ResolvedPlayerId = exactNameOwners[0].PlayerId,
                    PlayerNameFallbackUsed = true,
                    Warning = "Selected player id had no matching history; used the only exact-name player identity with persisted history."
                };
            }
            else if (exactNameOwners.Length > 1)
            {
                playerResolution = playerResolution with
                {
                    Warning = "Multiple exact-name player identities have persisted history for this prop type; history was not combined without an explicit alias."
                };
            }
        }

        var candidateObservationCount = await dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => equivalentPlayerIds.Contains(result.PlayerId), cancellationToken);
        var observationCountAfterPropType = await dbContext.PropResults
            .AsNoTracking()
            .CountAsync(result => equivalentPlayerIds.Contains(result.PlayerId)
                && propTypeResolution.PropTypeIds.Contains(result.PropTypeId), cancellationToken);
        var query = dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.Player)
            .Include(result => result.PropType)
            .Where(result => equivalentPlayerIds.Contains(result.PlayerId)
                && result.ExternalSource == "mlb"
                && propTypeResolution.PropTypeIds.Contains(result.PropTypeId));

        if (season.HasValue)
        {
            query = query.Where(result => result.EventDate.Year == season.Value);
        }

        var observationCountAfterSeason = await query.CountAsync(cancellationToken);
        var resultRows = await query
            .OrderByDescending(result => result.EventDate)
            .Take(requestedTake)
            .Select(result => new
            {
                result.Id,
                result.PlayerId,
                Player = result.Player == null ? playerResolution.ResolvedPlayerName ?? playerName ?? string.Empty : result.Player.DisplayName,
                result.EventDate,
                result.ActualResultValue,
                result.ExternalSourceIdentifier
            })
            .ToArrayAsync(cancellationToken);

        if (resultRows.Length == 0)
        {
            return EmptyPropHistory(
                "MLB",
                playerResolution.ResolvedPlayerId,
                playerResolution.ResolvedPlayerName ?? playerName,
                normalizedPropType,
                displayPropType,
                normalizedSide,
                line,
                provider,
                season,
                "No persisted MLB result history exists for this player and prop type.",
                playerResolution,
                propTypeResolution,
                equivalentPlayerIds.Length,
                candidateObservationCount,
                observationCountAfterPropType,
                observationCountAfterSeason);
        }

        var chronologicalRows = resultRows.OrderBy(result => result.EventDate).ThenBy(result => result.Id).ToArray();
        var games = chronologicalRows
            .Select(result =>
            {
                var outcome = EvaluateSelectedSide(result.ActualResultValue, line.Value, normalizedSide);
                return new PropHistoryGame(
                    result.EventDate,
                    result.EventDate.ToString("MMM d", CultureInfo.InvariantCulture),
                    "MLB game",
                    "MLB game",
                    result.ActualResultValue,
                    line.Value,
                    normalizedSide,
                    outcome.ClearedSelectedSide,
                    outcome.Result,
                    result.ExternalSourceIdentifier);
            })
            .ToArray();

        var newestValues = resultRows.Select(result => result.ActualResultValue).ToArray();
        var gradedGames = games.Where(game => game.ClearedSelectedSide.HasValue).ToArray();
        var clears = gradedGames.Count(game => game.ClearedSelectedSide == true);
        var misses = gradedGames.Count(game => game.ClearedSelectedSide == false);
        var pushes = games.Length - gradedGames.Length;
        var bestGame = chronologicalRows.OrderByDescending(result => result.ActualResultValue).First();
        var warning = gradedGames.Length < 10 ? "LowSample" : null;

        return new PropHistoryResponse(
            Sport: "MLB",
            PlayerId: playerResolution.ResolvedPlayerId,
            Player: playerResolution.ResolvedPlayerName ?? chronologicalRows[0].Player,
            PropType: normalizedPropType,
            DisplayPropType: displayPropType,
            Side: normalizedSide,
            Line: line.Value,
            Provider: provider,
            Season: season,
            Games: games,
            Summary: new PropHistorySummary(
                SampleSize: games.Length,
                GradedResults: gradedGames.Length,
                Cleared: clears,
                Missed: misses,
                Pushed: pushes,
                HitRate: gradedGames.Length == 0 ? null : Math.Round((decimal)clears / gradedGames.Length, 4),
                Last5Average: AverageLatest(newestValues, 5),
                Last10Average: AverageLatest(newestValues, 10),
                SeasonAverage: Math.Round(newestValues.Average(), 2),
                BestValue: bestGame.ActualResultValue,
                BestLabel: $"{FormatDecimal(bestGame.ActualResultValue)} {displayPropType.ToLowerInvariant()} on {bestGame.EventDate.ToString("MMM d", CultureInfo.InvariantCulture)}",
                BestRecentResult: $"{FormatDecimal(bestGame.ActualResultValue)} {displayPropType.ToLowerInvariant()} on {bestGame.EventDate.ToString("MMM d", CultureInfo.InvariantCulture)}"),
            Warning: warning,
            EmptyReason: null,
            Diagnostics: BuildPropHistoryDiagnostics(
                playerResolution,
                propTypeResolution,
                equivalentPlayerIds.Length,
                candidateObservationCount,
                observationCountAfterPropType,
                observationCountAfterSeason));
    }

    private static async Task<PropHistoryPlayerResolution> ResolvePropHistoryPlayerAsync(
        Guid? playerId,
        string? playerName,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (playerId.HasValue && playerId.Value != Guid.Empty)
        {
            var player = await dbContext.Players
                .AsNoTracking()
                .Where(row => row.Id == playerId.Value)
                .Select(row => new { row.Id, row.DisplayName })
                .SingleOrDefaultAsync(cancellationToken);

            return new PropHistoryPlayerResolution("playerId", player is not null, player?.Id, player?.DisplayName, false, null);
        }

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return new PropHistoryPlayerResolution("none", false, null, null, false, null);
        }

        var normalizedPlayerName = NormalizeLookupText(playerName);
        var candidates = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.League != null && player.League.Code == "MLB")
            .Select(player => new { player.Id, player.DisplayName })
            .ToArrayAsync(cancellationToken);
        var matches = candidates
            .Where(player => NormalizeLookupText(player.DisplayName) == normalizedPlayerName)
            .DefaultIfEmpty()
            .ToArray();
        var resolved = matches.FirstOrDefault()
            ?? candidates.FirstOrDefault(player => NormalizeLookupText(player.DisplayName).Contains(normalizedPlayerName, StringComparison.OrdinalIgnoreCase));

        return new PropHistoryPlayerResolution(
            "playerNameFallback",
            resolved is not null,
            resolved?.Id,
            resolved?.DisplayName,
            true,
            resolved is null
                ? "Player name fallback was used but no player was resolved. Frontend should pass playerId."
                : "Player name fallback was used; results may be ambiguous. Frontend should pass playerId.");
    }

    private static async Task<Guid[]> ResolveExactNamePropHistoryPlayerIdsAsync(
        string? playerName,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return [];
        }

        var normalizedPlayerName = NormalizeLookupText(playerName);
        var candidates = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.League != null && player.League.Code == "MLB")
            .Select(player => new { player.Id, player.DisplayName })
            .ToArrayAsync(cancellationToken);

        return candidates
            .Where(player => NormalizeLookupText(player.DisplayName) == normalizedPlayerName)
            .Select(player => player.Id)
            .Distinct()
            .ToArray();
    }

    private static string ResolveDisplayPropType(string? propType)
    {
        return ResolveResultPropTypeKey(propType) switch
        {
            "PITCHER_STRIKEOUTS" => "Pitcher Strikeouts",
            "HITS" => "Hits",
            "TOTAL_BASES" => "Total Bases",
            "RUNS" => "Runs",
            "RBIS" => "RBIs",
            "HOME_RUNS" => "Home Runs",
            "STOLEN_BASES" => "Stolen Bases",
            "HITS_RUNS_RBIS" => "Hits + Runs + RBIs",
            "BATTER_STRIKEOUTS" => "Batter Strikeouts",
            "WALKS" => "Batter Walks",
            "PITCHER_WALKS" => "Pitcher Walks",
            "PITCHER_HITS_ALLOWED" => "Pitcher Hits Allowed",
            "PITCHER_EARNED_RUNS" => "Pitcher Earned Runs",
            "PITCHER_OUTS" => "Pitcher Outs",
            "GOALS" => "Goals",
            "ASSISTS" => "Assists",
            "GOAL_ASSIST" => "Goal + Assist",
            "SHOTS" => "Shots",
            "SHOTS_ON_TARGET" => "Shots On Target",
            "PASSES_ATTEMPTED" => "Passes Attempted",
            "TACKLES" => "Tackles",
            "CROSSES" => "Crosses",
            "CLEARANCES" => "Clearances",
            "FOULS" => "Fouls",
            "FOULS_DRAWN" => "Fouls Drawn",
            "CARDS" => "Cards",
            "OFFSIDES" => "Offsides",
            "GOALIE_SAVES" => "Goalie Saves",
            "GOALS_ALLOWED" => "Goals Allowed",
            "ATTEMPTED_DRIBBLES" => "Attempted Dribbles",
            "SHOTS_ASSISTED" => "Shots Assisted",
            "OUTFIELD_FANTASY_SCORE" => "Outfield Fantasy Score",
            "GOALIE_FANTASY_SCORE" => "Goalie Fantasy Score",
            _ => string.IsNullOrWhiteSpace(propType) ? "Prop" : HumanizePropType(propType)
        };
    }

    private static string? ResolveResultPropTypeKey(string? propType)
    {
        var normalized = NormalizePropTypeKey(propType);
        return normalized switch
        {
            "PITCHER_STRIKEOUTS" or "PITCHER_STRIKE_OUTS" or "STRIKEOUTS_PITCHER" or "PITCHER_KS" or "KS" => "PITCHER_STRIKEOUTS",
            "HITS" or "BATTER_HITS" => "HITS",
            "TOTAL_BASES" or "BATTER_TOTAL_BASES" => "TOTAL_BASES",
            "RUNS" or "BATTER_RUNS" or "RUNS_SCORED" or "BATTER_RUNS_SCORED" => "RUNS",
            "RBI" or "RBIS" or "BATTER_RBI" or "BATTER_RBIS" => "RBIS",
            "HOME_RUN" or "HOME_RUNS" or "HOMERUNS" or "BATTER_HOME_RUN" or "BATTER_HOME_RUNS" => "HOME_RUNS",
            "STOLEN_BASE" or "STOLEN_BASES" or "BATTER_STOLEN_BASE" or "BATTER_STOLEN_BASES" => "STOLEN_BASES",
            "HITS_RUNS_RBI" or "HITS_RUNS_RBIS" or "BATTER_HITS_RUNS_RBI" or "BATTER_HITS_RUNS_RBIS" => "HITS_RUNS_RBIS",
            "BATTER_STRIKEOUTS" or "BATTER_STRIKE_OUTS" => "BATTER_STRIKEOUTS",
            "WALK" or "WALKS" or "BATTER_WALK" or "BATTER_WALKS" => "WALKS",
            "DOUBLE" or "DOUBLES" or "BATTER_DOUBLE" or "BATTER_DOUBLES" => "DOUBLES",
            "SINGLE" or "SINGLES" or "BATTER_SINGLE" or "BATTER_SINGLES" => "SINGLES",
            "PITCHER_WALK" or "PITCHER_WALKS" => "PITCHER_WALKS",
            "PITCHER_HITS_ALLOWED" or "PITCHER_HIT_ALLOWED" => "PITCHER_HITS_ALLOWED",
            "PITCHER_EARNED_RUN" or "PITCHER_EARNED_RUNS" => "PITCHER_EARNED_RUNS",
            "PITCHER_OUT" or "PITCHER_OUTS" => "PITCHER_OUTS",
            "GOAL" or "GOALS" => "GOALS",
            "ASSIST" or "ASSISTS" => "ASSISTS",
            "GOAL_ASSIST" or "GOAL_ASSISTS" or "GOAL_AND_ASSIST" or "GOAL_PLUS_ASSIST" or "GOALS_ASSISTS" or "GOALS_ASSISTS_COMBO" => "GOAL_ASSIST",
            "SHOT" or "SHOTS" => "SHOTS",
            "SHOT_COMBO" or "SHOTS_COMBO" => "SHOTS_COMBO",
            "SHOT_ON_TARGET" or "SHOTS_ON_TARGET" => "SHOTS_ON_TARGET",
            "SHOT_ON_TARGET_COMBO" or "SHOTS_ON_TARGET_COMBO" => "SHOTS_ON_TARGET_COMBO",
            "PASS_ATTEMPTED" or "PASSES_ATTEMPTED" => "PASSES_ATTEMPTED",
            "PASS_ATTEMPTED_COMBO" or "PASSES_ATTEMPTED_COMBO" => "PASSES_ATTEMPTED_COMBO",
            "TACKLE" or "TACKLES" => "TACKLES",
            "CROSS" or "CROSSES" => "CROSSES",
            "CLEARANCE" or "CLEARANCES" => "CLEARANCES",
            "CLEARANCE_COMBO" or "CLEARANCES_COMBO" => "CLEARANCES_COMBO",
            "FOUL" or "FOULS" => "FOULS",
            "FOUL_DRAWN" or "FOULS_DRAWN" => "FOULS_DRAWN",
            "CARD" or "CARDS" => "CARDS",
            "OFFSIDE" or "OFFSIDES" => "OFFSIDES",
            "GOALIE_SAVE" or "GOALIE_SAVES" or "SAVES" => "GOALIE_SAVES",
            "GOALIE_SAVE_COMBO" or "GOALIE_SAVES_COMBO" => "GOALIE_SAVES_COMBO",
            "GOAL_ALLOWED" or "GOALS_ALLOWED" => "GOALS_ALLOWED",
            "GOAL_ALLOWED_COMBO" or "GOALS_ALLOWED_COMBO" => "GOALS_ALLOWED_COMBO",
            "GOAL_COMBO" or "GOALS_COMBO" => "GOALS_COMBO",
            "ATTEMPTED_DRIBBLE" or "ATTEMPTED_DRIBBLES" => "ATTEMPTED_DRIBBLES",
            "SHOT_ASSISTED" or "SHOTS_ASSISTED" => "SHOTS_ASSISTED",
            "OUTFIELD_FANTASY_SCORE" or "FANTASY_SCORE_OUTFIELD" => "OUTFIELD_FANTASY_SCORE",
            "GOALIE_FANTASY_SCORE" or "FANTASY_SCORE_GOALIE" => "GOALIE_FANTASY_SCORE",
            _ => null
        };
    }

    private static string NormalizeResultSide(string? side)
    {
        return side?.Trim().ToLowerInvariant() switch
        {
            "under" or "lower" => "Under",
            _ => "Over"
        };
    }

    private static (bool? ClearedSelectedSide, string Result) EvaluateSelectedSide(decimal value, decimal line, string side)
    {
        if (value == line)
        {
            return (null, "Push");
        }

        var cleared = string.Equals(side, "Under", StringComparison.OrdinalIgnoreCase)
            ? value < line
            : value > line;

        return (cleared, cleared ? "Cleared" : "Missed");
    }

    private static decimal AverageLatest(decimal[] newestValues, int count)
    {
        if (newestValues.Length == 0)
        {
            return 0;
        }

        return Math.Round(newestValues.Take(count).Average(), 2);
    }

    private static PropHistoryResponse EmptyPropHistory(
        string sport,
        Guid? playerId,
        string? player,
        string? propType,
        string displayPropType,
        string side,
        decimal? line,
        string? provider,
        int? season,
        string emptyReason,
        PropHistoryPlayerResolution? playerResolution = null,
        PropHistoryPropTypeResolution? propTypeResolution = null,
        int equivalentPlayerCount = 0,
        int candidateObservationCount = 0,
        int observationCountAfterPropType = 0,
        int observationCountAfterSeason = 0)
    {
        return new PropHistoryResponse(
            Sport: sport,
            PlayerId: playerId,
            Player: player,
            PropType: propType,
            DisplayPropType: displayPropType,
            Side: side,
            Line: line,
            Provider: provider,
            Season: season,
            Games: [],
            Summary: new PropHistorySummary(0, 0, 0, 0, 0, null, null, null, null, null, null, null),
            Warning: null,
            EmptyReason: emptyReason,
            Diagnostics: BuildPropHistoryDiagnostics(
                playerResolution,
                propTypeResolution,
                equivalentPlayerCount,
                candidateObservationCount,
                observationCountAfterPropType,
                observationCountAfterSeason));
    }

    private static async Task<PropHistoryPropTypeResolution> ResolvePropHistoryPropTypesAsync(
        string normalizedPropType,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var requestedCanonicalName = CanonicalPropTypeName(normalizedPropType);
        var requestedPerformanceKey = NormalizePerformanceKey(requestedCanonicalName);
        var requestedAliases = BuildPropHistoryPerformanceAliases(requestedPerformanceKey);
        var propTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(propType => new { propType.Id, propType.Name, propType.StatKey })
            .ToArrayAsync(cancellationToken);
        var matches = propTypes
            .Where(propType =>
                string.Equals(CanonicalPropTypeName(propType.Name), requestedCanonicalName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizePropTypeKey(propType.StatKey), normalizedPropType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(CanonicalPropTypeName(propType.StatKey), requestedCanonicalName, StringComparison.OrdinalIgnoreCase)
                || requestedAliases.Contains(NormalizePerformanceKey(propType.Name))
                || requestedAliases.Contains(NormalizePerformanceKey(propType.StatKey)))
            .ToArray();

        return new PropHistoryPropTypeResolution(
            PropTypeResolved: matches.Select(propType => propType.StatKey ?? propType.Name).FirstOrDefault(),
            PropTypeIds: matches.Select(propType => propType.Id).Distinct().ToArray(),
            PropTypeNames: matches.Select(propType => propType.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static HashSet<string> BuildPropHistoryPerformanceAliases(string requestedPerformanceKey)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(requestedPerformanceKey))
        {
            aliases.Add(requestedPerformanceKey);
        }

        if (string.Equals(requestedPerformanceKey, "pitcherstrikeouts", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("pitcherks");
            aliases.Add("ks");
        }
        else if (string.Equals(requestedPerformanceKey, "batterstrikeouts", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("strikeouts");
            aliases.Add("strikeout");
            aliases.Add("batterstrikeout");
            aliases.Add("hitterstrikeouts");
            aliases.Add("hitterstrikeout");
        }
        else if (string.Equals(requestedPerformanceKey, "walks", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("walk");
            aliases.Add("batterwalks");
            aliases.Add("batterwalk");
            aliases.Add("hitterwalks");
            aliases.Add("hitterwalk");
            aliases.Add("baseonballs");
            aliases.Add("batterbaseonballs");
        }

        return aliases;
    }

    private static PropHistoryDiagnostics BuildPropHistoryDiagnostics(
        PropHistoryPlayerResolution? playerResolution,
        PropHistoryPropTypeResolution? propTypeResolution,
        int equivalentPlayerCount,
        int candidateObservationCount,
        int observationCountAfterPropType,
        int observationCountAfterSeason)
    {
        return new PropHistoryDiagnostics(
            LookupMode: playerResolution?.LookupMode ?? "none",
            PlayerResolved: playerResolution?.PlayerResolved ?? false,
            ResolvedPlayerId: playerResolution?.ResolvedPlayerId,
            PlayerNameFallbackUsed: playerResolution?.PlayerNameFallbackUsed ?? false,
            Warning: playerResolution?.Warning,
            PropTypeResolved: propTypeResolution?.PropTypeResolved,
            HistorySource: "PropResults via PredictionFeatureService equivalent player/prop lookup",
            EquivalentPlayerCount: equivalentPlayerCount,
            CandidateObservationCount: candidateObservationCount,
            ObservationCountAfterPropType: observationCountAfterPropType,
            ObservationCountAfterSeason: observationCountAfterSeason);
    }

    private static string CanonicalPropTypeName(string? propTypeName)
    {
        var normalized = NormalizeCompactLookupText(propTypeName);
        return string.Equals(normalized, "RBI", StringComparison.OrdinalIgnoreCase) ? "RBIS" : normalized;
    }

    private static string NormalizeCompactLookupText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit));
    }

    private static string NormalizePropTypeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousUnderscore = false;

        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousUnderscore = false;
            }
            else if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        return builder.ToString().Trim('_');
    }

    private static string NormalizeLookupText(string value)
    {
        return string.Concat(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));
    }

    private static string HumanizePropType(string value)
    {
        return string.Join(' ', NormalizePropTypeKey(value)
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant())));
    }

    private static string FormatDecimal(decimal value)
    {
        return value % 1 == 0
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string NormalizePerformanceKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static (string Status, string StatusReason, string RecommendedAction) BuildTheOddsProviderStatusDecision(
        TheOddsRefreshRun latestRun,
        bool latestRefreshHadUsableMarketData,
        bool providerRejectedKeyDetected,
        bool creditExhaustionLikely,
        bool creditLow)
    {
        if (providerRejectedKeyDetected)
        {
            return (
                "Unavailable",
                "The latest refresh diagnostics indicate The Odds API rejected the configured API key.",
                "Check TheOddsApi:ApiKey and account credit status before running another live refresh.");
        }

        if (creditExhaustionLikely)
        {
            return (
                "Unavailable",
                "The latest persisted credit metadata indicates The Odds API credits are exhausted.",
                "Wait for credit reset or increase the credit plan before running another live refresh.");
        }

        if (creditLow)
        {
            return (
                "Degraded",
                "The latest persisted credit metadata indicates The Odds API credits are very low.",
                "Use dry-run planning and avoid live refreshes unless the remaining credit budget is intentional.");
        }

        if (latestRun.Succeeded && latestRefreshHadUsableMarketData)
        {
            return (
                "Healthy",
                "The latest refresh succeeded and produced usable market data.",
                "Continue serving persisted read endpoints; use dry-run before any manual live refresh.");
        }

        if (latestRun.Succeeded)
        {
            return (
                "Degraded",
                "The latest refresh succeeded but produced no usable active market snapshots for the current read model.",
                "Inspect freshness, line-shopping snapshots, and provider diagnostics before trusting public market output.");
        }

        if (latestRun.Failed)
        {
            return (
                "Unavailable",
                string.IsNullOrWhiteSpace(latestRun.ErrorMessage)
                    ? "The latest The Odds refresh failed."
                    : $"The latest The Odds refresh failed: {latestRun.ErrorMessage}",
                "Review the latest refresh diagnostics, API key, and credit balance before running another live refresh.");
        }

        return (
            "Unknown",
            "The latest The Odds refresh metadata is incomplete.",
            "Run a dry-run refresh to inspect the planned provider call shape before any live refresh.");
    }

    private static ProviderStatusDiagnostics ExtractProviderStatusDiagnostics(JsonDocument? diagnosticsJson)
    {
        if (diagnosticsJson is null)
        {
            return new ProviderStatusDiagnostics(null, null, null, 0, 0, ProviderRejectedKeyDetected: false);
        }

        var root = diagnosticsJson.RootElement;
        var providerSuccessCount = 0;
        var providerFailureCount = 0;
        var providerRejectedKeyDetected = ContainsPersistedText(root, "rejected the configured API key") ||
            ContainsPersistedText(root, "INVALID_KEY");

        if (TryGetPropertyIgnoreCase(root, "ingestion", out var ingestion) &&
            ingestion.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ingestion.EnumerateArray())
            {
                var success = TryGetBoolProperty(item, "success");
                if (success == true)
                {
                    providerSuccessCount++;
                }
                else
                {
                    providerFailureCount++;
                }
            }
        }

        return new ProviderStatusDiagnostics(
            RequestsUsed: FindFirstIntProperty(root, "requestsUsed", "RequestsUsed", "requests_used"),
            RequestsRemaining: FindFirstIntProperty(root, "requestsRemaining", "RequestsRemaining", "requests_remaining"),
            RequestsLast: FindFirstIntProperty(root, "requestsLast", "RequestsLast", "requests_last"),
            providerFailureCount,
            providerSuccessCount,
            providerRejectedKeyDetected);
    }

    private static int? FindFirstIntProperty(JsonElement element, params string[] names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetInt32(out var intValue))
                        {
                            return intValue;
                        }

                        if (property.Value.ValueKind == JsonValueKind.String &&
                            int.TryParse(property.Value.GetString(), out var stringValue))
                        {
                            return stringValue;
                        }
                    }

                    var nested = FindFirstIntProperty(property.Value, names);
                    if (nested.HasValue)
                    {
                        return nested;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindFirstIntProperty(item, names);
                    if (nested.HasValue)
                    {
                        return nested;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static bool ContainsPersistedText(JsonElement element, string text)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Object => element.EnumerateObject().Any(property => ContainsPersistedText(property.Value, text)),
            JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsPersistedText(item, text)),
            _ => false
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool? TryGetBoolProperty(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string[] SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static object ToPublicFreshnessSummary(TheOddsApiDebugEndpoints.MlbRefreshFreshnessResponse response)
    {
        return new
        {
            response.GeneratedAtUtc,
            response.CallsExternalApis,
            response.ReadOnly,
            latestSuccessfulRefresh = response.LatestSuccessfulRun,
            response.MinutesSinceLatestSuccessfulRefresh,
            response.IsFresh,
            response.IsStale,
            response.StaleReason,
            freshnessMinutesThreshold = response.FreshnessThresholdMinutes
        };
    }

    internal sealed record PerformanceResetSelectionResult(
        DateOnly? LatestCompletedDate,
        object FiltersApplied,
        int GradedRowsAvailableBeforeTake,
        int AlreadyExcludedRowsSkipped,
        object PublicPerformanceBeforeReset,
        object ExpectedPublicPerformanceAfterReset,
        PerformanceResetSelectionRow[] Rows);

    internal sealed record PerformanceResetSelectionRow(
        Guid RecommendationSnapshotId,
        string PlayerName,
        string PropType,
        string Provider,
        string Side,
        decimal Line,
        string FinalGradeStatus,
        DateTimeOffset CapturedAtUtc,
        DateOnly ExpectedResultDate,
        bool AlreadyExcluded,
        string? PerformanceExclusionReason,
        DateTimeOffset? PerformanceExcludedAtUtc,
        string? PerformanceExcludedBy);

    private sealed record MarketSportResolution(
        string? RequestedSport,
        string? EffectiveSport,
        IResult? UnsupportedResponse);

    private sealed record TheOddsProviderStatusReadModel(
        string provider,
        string status,
        bool hasRecentRefresh,
        Guid? latestRefreshRunId,
        DateTimeOffset? latestRefreshStartedAtUtc,
        DateTimeOffset? latestRefreshCompletedAtUtc,
        bool latestRefreshSucceeded,
        bool latestRefreshFailed,
        bool latestRefreshHadUsableMarketData,
        string? latestBookmakerPreset,
        string[] latestProviderCategoriesIncluded,
        int actualApiCalls,
        int estimatedApiCalls,
        int? requestsUsed,
        int? requestsRemaining,
        int? requestsLast,
        bool providerRejectedKeyDetected,
        bool creditExhaustionLikely,
        int providerIngestionSuccessCount,
        int providerIngestionFailureCount,
        string? lastErrorMessage,
        string statusReason,
        string recommendedAction,
        int lineShoppingSnapshotCount,
        int topPickSnapshotCount,
        int activeGroupCount,
        int staleOrExpiredGroupCount)
    {
        public bool? budgetAllowed { get; init; }
        public string? budgetBlockReason { get; init; }
        public string? budgetExplanation { get; init; }
        public bool? plannerEnforcementEnabled { get; init; }
        public int? maxEstimatedCallsPerRun { get; init; }
        public bool? localBudgetEnforcementEnabled { get; init; }
        public bool? dailyBudgetEnforcementEnabled { get; init; }
        public bool? monthlyBudgetEnforcementEnabled { get; init; }
        public bool? localDailyBudgetEnforcementEnabled { get; init; }
        public bool? localMonthlyBudgetEnforcementEnabled { get; init; }
        public bool? perRunEstimateBlockingEnabled { get; init; }
        public bool? providerCreditBlockingEnabled { get; init; }
        public bool? dailyBudgetEnabled { get; init; }
        public int? maxDailyCredits { get; init; }
        public int? legacyConfiguredMaxDailyCredits { get; init; }
        public int? legacyConfiguredMaxMonthlyCredits { get; init; }
        public bool? monthlyBudgetAllowed { get; init; }
        public bool? localBudgetExceeded { get; init; }
        public int? configuredMonthlyCreditLimit { get; init; }
        public int? configuredReservedCreditBuffer { get; init; }
        public int? providerReportedRequestsUsed { get; init; }
        public int? providerReportedRequestsRemaining { get; init; }
        public decimal? providerCreditTelemetryAgeMinutes { get; init; }
        public bool? providerTelemetryUsedForBlocking { get; init; }
        public int? localBudgetUsedEstimate { get; init; }
        public int? localBudgetRemainingEstimate { get; init; }
        public bool? isProviderTelemetryStale { get; init; }
        public bool? isBlockedByProviderCredits { get; init; }
        public bool? isBlockedByLocalBudget { get; init; }
        public bool? isBlockedByPerRunEstimate { get; init; }
        public bool? plannerWouldBlockIfEnforced { get; init; }
        public string[]? plannerWarnings { get; init; }
    }

    private sealed record ProviderStatusDiagnostics(
        int? RequestsUsed,
        int? RequestsRemaining,
        int? RequestsLast,
        int ProviderIngestionFailureCount,
        int ProviderIngestionSuccessCount,
        bool ProviderRejectedKeyDetected);

    private sealed record PerformancePickRow(
        Models.MarketLineShoppingSnapshot Snapshot,
        int? HistoricalSampleSize,
        Guid? ResolvedPlayerId,
        IReadOnlyCollection<Guid> EquivalentPlayerIds,
        string PlayerResolutionMode,
        string? PlayerResolutionFailureReason);

    private sealed record PerformanceGradedPickRow(
        Models.MarketLineShoppingSnapshot Snapshot,
        int? HistoricalSampleSize,
        Guid? ResolvedPlayerId,
        IReadOnlyCollection<Guid> EquivalentPlayerIds,
        string PlayerResolutionMode,
        string? PlayerResolutionFailureReason,
        decimal? ActualResult,
        string? Outcome,
        decimal? Profit,
        string MatchLookupMode,
        string? MatchFailureReason);

    private sealed record LedgerActualResultLookupRow(
        Guid PropResultId,
        DateOnly EventDate,
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropType,
        decimal ActualResultValue)
    {
        public string NormalizedPlayerName { get; } = NormalizePerformanceKey(PlayerName);
        public string NormalizedPropType { get; } = NormalizePerformancePropTypeKey(PropType);
    }

    private sealed record LedgerDiagnosticResultRow(
        Guid PropResultId,
        DateOnly EventDate,
        Guid PlayerId,
        Guid CanonicalPlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropType,
        decimal ActualResultValue)
    {
        public string NormalizedPlayerName { get; } = NormalizePerformanceKey(PlayerName);
        public string NormalizedPropType { get; } = NormalizePerformancePropTypeKey(PropType);
    }

    private sealed record LedgerPerformanceRow(
        RecommendationSnapshot Snapshot,
        decimal? ActualResult,
        string? Outcome,
        decimal? Profit,
        string? MatchFailureReason,
        string? MatchMethod);

    private sealed record PerformanceDateDiagnostics(
        DateOnly SlateDate,
        int TotalPicks,
        int GradedPicks,
        int PendingPicks,
        int PropResultsAvailableForDate,
        int DistinctPropResultPlayers,
        int DistinctPropResultPropTypes,
        IReadOnlyDictionary<string, int> MatchFailureCounts,
        string MatchStrategyUsed);

    private sealed record PerformanceSnapshotPlayerResolution(
        Guid? PlayerId,
        string LookupMode,
        string? FailureReason);

    private sealed record ActualResultLookupRow(
        DateOnly EventDate,
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropType,
        decimal ActualResultValue);

    private readonly record struct ActualResultPlayerLookupKey(
        DateOnly EventDate,
        Guid PlayerId,
        string PropType);

    private sealed record PropHistoryResponse(
        string Sport,
        Guid? PlayerId,
        string? Player,
        string? PropType,
        string DisplayPropType,
        string Side,
        decimal? Line,
        string? Provider,
        int? Season,
        IReadOnlyList<PropHistoryGame> Games,
        PropHistorySummary Summary,
        string? Warning,
        string? EmptyReason,
        PropHistoryDiagnostics Diagnostics);

    private sealed record PropHistoryGame(
        DateOnly Date,
        string DateLabel,
        string Event,
        string GameLabel,
        decimal Value,
        decimal SelectedLine,
        string Side,
        bool? ClearedSelectedSide,
        string Result,
        string ExternalSourceIdentifier);

    private sealed record PropHistorySummary(
        int SampleSize,
        int GradedResults,
        int Cleared,
        int Missed,
        int Pushed,
        decimal? HitRate,
        decimal? Last5Average,
        decimal? Last10Average,
        decimal? SeasonAverage,
        decimal? BestValue,
        string? BestLabel,
        string? BestRecentResult);

    private sealed record PropHistoryPlayerResolution(
        string LookupMode,
        bool PlayerResolved,
        Guid? ResolvedPlayerId,
        string? ResolvedPlayerName,
        bool PlayerNameFallbackUsed,
        string? Warning);

    private sealed record PropHistoryPropTypeResolution(
        string? PropTypeResolved,
        Guid[] PropTypeIds,
        string[] PropTypeNames);

    private sealed record PropHistoryDiagnostics(
        string LookupMode,
        bool PlayerResolved,
        Guid? ResolvedPlayerId,
        bool PlayerNameFallbackUsed,
        string? Warning,
        string? PropTypeResolved,
        string HistorySource,
        int EquivalentPlayerCount,
        int CandidateObservationCount,
        int ObservationCountAfterPropType,
        int ObservationCountAfterSeason);
}
