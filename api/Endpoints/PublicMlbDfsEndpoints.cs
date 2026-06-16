using SportsBetting.Api.Data;
using SportsBetting.Api.Predictions;

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
            freshnessMinutesThreshold = response.FreshnessThresholdMinutes
        };
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
}
