using SportsBetting.Api.Ingestion;

namespace SportsBetting.Api.Endpoints;

public static class UnderdogIngestionHealthEndpoints
{
    public static IEndpointRouteBuilder MapUnderdogIngestionHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health/underdog-ingestion", (IUnderdogIngestionWorkerState state) =>
        {
            return Results.Ok(new
            {
                isRunning = state.IsRunning,
                lastStatus = state.LastStatus,
                lastSuccessTime = state.LastSuccessTime,
                lastDuration = state.LastDuration,
                failureCount = state.FailureCount,
                lastErrorMessage = state.LastErrorMessage
            });
        })
        .WithName("UnderdogIngestionHealth")
        .WithSummary("Returns background Underdog ingestion worker status.");

        return endpoints;
    }
}
