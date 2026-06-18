using SportsBetting.Api.Ingestion;
using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Endpoints;

public static class UnderdogIngestionHealthEndpoints
{
    public static IEndpointRouteBuilder MapUnderdogIngestionHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health/underdog-ingestion", (
            IUnderdogIngestionWorkerState state,
            IOptions<UnderdogIngestionOptions> options) => Results.Ok(BuildResponse(state, options.Value)))
        .WithName("UnderdogIngestionHealth")
        .WithSummary("Returns disabled direct Underdog ingestion worker status for debug/audit visibility.");

        endpoints.MapGet("/api/debug/underdog/ingestion-health", (
            IUnderdogIngestionWorkerState state,
            IOptions<UnderdogIngestionOptions> options) => Results.Ok(BuildResponse(state, options.Value)))
        .WithName("UnderdogDebugIngestionHealth")
        .WithSummary("Returns direct Underdog ingestion worker status for debug/audit visibility.");

        return endpoints;
    }

    private static object BuildResponse(
        IUnderdogIngestionWorkerState state,
        UnderdogIngestionOptions options)
    {
        return new
        {
            activeProviderSource = "The Odds API",
            directUnderdogActiveIngestion = options.Enabled ? "enabled" : "disabled",
            directUnderdogRole = "debug audit only",
            scheduledBackgroundIngestionEnabled = options.Enabled,
            intervalMinutes = options.IntervalMinutes,
            effectiveIntervalMinutes = (int)Math.Max(1, options.GetInterval().TotalMinutes),
            callsExternalApis = false,
            readOnly = true,
            isRunning = state.IsRunning,
            lastStatus = state.LastStatus,
            lastSuccessTime = state.LastSuccessTime,
            lastDuration = state.LastDuration,
            failureCount = state.FailureCount,
            lastErrorMessage = state.LastErrorMessage
        };
    }
}
