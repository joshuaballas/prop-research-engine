using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Endpoints;

public static class FeatureDebugEndpoints
{
    public static IEndpointRouteBuilder MapFeatureDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/features/player", async (
            Guid playerId,
            Guid propTypeId,
            DateOnly date,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            if (playerId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "playerId is required." });
            }

            if (propTypeId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "propTypeId is required." });
            }

            var features = await featureService.GeneratePlayerFeaturesAsync(
                playerId,
                propTypeId,
                date,
                cancellationToken);

            return Results.Ok(new
            {
                features.PlayerId,
                features.PropTypeId,
                eventDate = features.EventDate,
                features.Last5GameAverage,
                features.Last10GameAverage,
                features.SeasonAverage,
                features.RollingStandardDeviation,
                features.HitRateLast5Games,
                features.HitRateLast10Games,
                features.DaysSinceLastGame,
                features.HistoricalGamesAvailable,
                features.SeasonGamesAvailable,
                features.HitRateDefinition,
                features.RollingStandardDeviationWindow
            });
        })
        .WithName("PlayerPredictionFeaturesDebug")
        .WithSummary("Returns read-only prediction features generated from prop_results for one player/stat/date.");

        return endpoints;
    }
}
