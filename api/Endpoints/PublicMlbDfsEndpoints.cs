using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace SportsBetting.Api.Endpoints;

public static class PublicMlbDfsEndpoints
{
    private static readonly string[] SupportedMarketSports = ["MLB"];
    private const string MlbOnlyMarketReadLimitation = "Only MLB persisted market read models are currently implemented. Additional sports should be added behind these /api/markets routes instead of duplicating route families.";

    public static IEndpointRouteBuilder MapPublicMlbDfsEndpoints(this IEndpointRouteBuilder endpoints)
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
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await BuildMarketProviderStatusResponseAsync(
                freshnessMinutes,
                dbContext,
                cancellationToken));
        })
        .WithName("MarketsProviderStatus")
        .WithSummary("Production-safe provider/API-key/credit status from persisted refresh metadata.");

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
            int? minSampleSize,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
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
                candidateOnly ?? true,
                fromDate,
                toDate,
                minSampleSize,
                dbContext,
                cancellationToken);

            return Results.Ok(response);
        })
        .WithName("MarketsPerformanceSummary")
        .WithSummary("Production-safe read-only market recommendation performance summary.");

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
            limitation = MlbOnlyMarketReadLimitation,
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

    private static async Task<object> BuildMarketProviderStatusResponseAsync(
        int? freshnessMinutes,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var providerStatus = await BuildTheOddsProviderStatusAsync(
            freshnessMinutes,
            dbContext,
            cancellationToken);

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            callsExternalApis = false,
            readOnly = true,
            providers = new object[] { providerStatus }
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
            limitation = MlbOnlyMarketReadLimitation,
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
            limitation = MlbOnlyMarketReadLimitation,
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

    private static MarketSportResolution ResolveMarketSport(string? sport)
    {
        var requestedSport = string.IsNullOrWhiteSpace(sport)
            ? null
            : sport.Trim().ToUpperInvariant();
        var effectiveSport = requestedSport ?? "MLB";

        if (!string.Equals(effectiveSport, "MLB", StringComparison.OrdinalIgnoreCase))
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
                    limitation = MlbOnlyMarketReadLimitation,
                    error = $"Sport '{requestedSport}' is not implemented for production market reads yet."
                }));
        }

        return new MarketSportResolution(requestedSport, "MLB", UnsupportedResponse: null);
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
        int? minSampleSize,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var latestRefresh = await TheOddsApiDebugEndpoints.BuildMlbRefreshFreshnessResponseAsync(
            slateDate: null,
            bookmakerPreset: null,
            freshnessMinutes: null,
            dbContext,
            cancellationToken);
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

        if (candidateOnly)
        {
            snapshotQuery = snapshotQuery.Where(row => row.IsCandidate);
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
        var scoredSnapshots = snapshots
            .Select(row => new PerformancePickRow(row, ExtractHistoricalSampleSize(row.DetailsJson)))
            .Where(row => !minSampleSize.HasValue || row.HistoricalSampleSize >= minSampleSize.Value)
            .ToArray();

        var resultDates = scoredSnapshots.Select(row => row.Snapshot.SlateDate).Distinct().ToArray();
        var actualResultRows = resultDates.Length == 0
            ? []
            : await dbContext.PropResults
                .AsNoTracking()
                .Include(row => row.Player)
                .Include(row => row.PropType)
                .Where(row => resultDates.Contains(row.EventDate))
                .Select(row => new ActualResultLookupRow(
                    row.EventDate,
                    row.Player == null ? string.Empty : row.Player.DisplayName,
                    row.PropType == null ? string.Empty : row.PropType.Name,
                    row.ActualResultValue))
                .ToListAsync(cancellationToken);
        var actualResults = actualResultRows
            .GroupBy(row => new ActualResultLookupKey(
                row.EventDate,
                NormalizePerformanceKey(row.PlayerName),
                NormalizePerformanceKey(row.PropType)))
            .ToDictionary(group => group.Key, group => group.First().ActualResultValue);
        var gradedRows = scoredSnapshots
            .Select(row => GradePerformancePick(row, actualResults))
            .ToArray();
        var totalGraded = gradedRows.Count(row => row.Outcome is "Win" or "Loss" or "Push");

        return new
        {
            generatedAtUtc,
            callsExternalApis = false,
            readOnly = true,
            supportedSports = SupportedMarketSports,
            requestedSport,
            effectiveSport,
            limitation = "Performance summary currently supports MLB snapshots only. Grading uses market_line_shopping_snapshots matched to prop_results by normalized player name, prop type, and slate date until snapshot rows persist canonical ids.",
            filters = new
            {
                providerCategory,
                provider,
                bookmaker,
                propType,
                strategyClassification,
                strategyTier,
                candidateOnly,
                fromDate,
                toDate,
                minSampleSize
            },
            dataAvailability = new
            {
                snapshotRowsLoaded = snapshots.Count,
                picksAfterFilters = scoredSnapshots.Length,
                gradedPicks = totalGraded,
                hasEnoughData = totalGraded >= 30,
                warning = snapshots.Count == 0
                    ? latestRefresh.LineShoppingSnapshotCount == 0 && latestRefresh.LatestSuccessfulRun is not null
                        ? "Latest refresh produced no line-shopping snapshots."
                        : latestRefresh.LineShoppingSnapshotCount > 0 && latestRefresh.HasUsableMarketData == false
                            ? "Latest refresh produced line-shopping snapshots, but none are usable active market data."
                        : "No line-shopping snapshots exist yet. Run a successful live refresh with forceLive=true to create snapshots."
                    : totalGraded == 0
                        ? "No matching graded prop_results were found for the filtered snapshot rows."
                        : totalGraded < 30
                            ? "Small graded sample. Treat ROI and hit-rate as directional only."
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
            groups = new
            {
                overall = BuildPerformanceGroups(gradedRows, "overall", row => "All"),
                byProviderCategory = BuildPerformanceGroups(gradedRows, "providerCategory", row => row.Snapshot.ProviderCategory),
                byProvider = BuildPerformanceGroups(gradedRows, "provider", row => row.Snapshot.EntryProvider),
                byBookmaker = BuildPerformanceGroups(gradedRows, "bookmaker", row => row.Snapshot.BestBookmaker ?? row.Snapshot.Bookmaker ?? "Unknown"),
                byPropType = BuildPerformanceGroups(gradedRows, "propType", row => row.Snapshot.PropType),
                byStrategyClassification = BuildPerformanceGroups(gradedRows, "strategyClassification", row => row.Snapshot.StrategyClassification),
                byStrategyTier = BuildPerformanceGroups(gradedRows, "strategyTier", row => row.Snapshot.StrategyTier),
                byProviderAndPropType = BuildPerformanceGroups(gradedRows, "providerAndPropType", row => $"{row.Snapshot.EntryProvider} | {row.Snapshot.PropType}")
            }
        };
    }

    private static PerformanceGradedPickRow GradePerformancePick(
        PerformancePickRow row,
        IReadOnlyDictionary<ActualResultLookupKey, decimal> actualResults)
    {
        var key = new ActualResultLookupKey(
            row.Snapshot.SlateDate,
            NormalizePerformanceKey(row.Snapshot.Player),
            NormalizePerformanceKey(row.Snapshot.PropType));
        var actualResult = actualResults.TryGetValue(key, out var value)
            ? value
            : (decimal?)null;
        var outcome = GradePerformanceOutcome(row.Snapshot.BestSide, row.Snapshot.Line, actualResult);
        var profit = CalculatePerformanceProfit(outcome, row.Snapshot.BestDecimalPrice);

        return new PerformanceGradedPickRow(
            row.Snapshot,
            row.HistoricalSampleSize,
            actualResult,
            outcome,
            profit);
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
        var roi = graded.Length == 0 ? 0m : Math.Round(profit / graded.Length, 4);
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
            wins,
            losses,
            pushes,
            hitRate = decisions == 0 ? 0m : Math.Round(wins / (decimal)decisions, 4),
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
        int staleOrExpiredGroupCount);

    private sealed record ProviderStatusDiagnostics(
        int? RequestsUsed,
        int? RequestsRemaining,
        int? RequestsLast,
        int ProviderIngestionFailureCount,
        int ProviderIngestionSuccessCount,
        bool ProviderRejectedKeyDetected);

    private sealed record PerformancePickRow(
        Models.MarketLineShoppingSnapshot Snapshot,
        int? HistoricalSampleSize);

    private sealed record PerformanceGradedPickRow(
        Models.MarketLineShoppingSnapshot Snapshot,
        int? HistoricalSampleSize,
        decimal? ActualResult,
        string? Outcome,
        decimal? Profit);

    private sealed record ActualResultLookupRow(
        DateOnly EventDate,
        string PlayerName,
        string PropType,
        decimal ActualResultValue);

    private readonly record struct ActualResultLookupKey(
        DateOnly EventDate,
        string PlayerName,
        string PropType);
}
