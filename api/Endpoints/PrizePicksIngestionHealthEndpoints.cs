using SportsBetting.Api.Ingestion;

namespace SportsBetting.Api.Endpoints;

public static class PrizePicksIngestionHealthEndpoints
{
    public static IEndpointRouteBuilder MapPrizePicksIngestionHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health/prizepicks-ingestion", (IPrizePicksIngestionWorkerState state) =>
        {
            return Results.Ok(new
            {
                lastSuccessfulIngestionTime = state.LastSuccessfulIngestionTime,
                lastIngestionDuration = state.LastIngestionDuration,
                lastIngestionStatus = "disabled",
                lastErrorMessage = state.LastErrorMessage,
                failureCount = state.FailureCount,
                isRunning = false
            });
        })
        .WithName("PrizePicksIngestionHealth")
        .WithSummary("Returns PrizePicks background ingestion worker status.");

        return endpoints;
    }
}
