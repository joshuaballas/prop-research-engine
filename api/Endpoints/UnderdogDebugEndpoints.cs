using SportsBetting.Api.Integrations.Underdog;

namespace SportsBetting.Api.Endpoints;

public static class UnderdogDebugEndpoints
{
    public static IEndpointRouteBuilder MapUnderdogDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/underdog", async (
            IUnderdogAdapter adapter,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var projections = await adapter.GetActiveProjectionsAsync(cancellationToken);
            var response = new
            {
                projectionCount = projections.Count,
                sportBreakdown = projections
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.SportId) ? "UNKNOWN" : x.SportId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new { sport = group.Key, count = group.Count() })
                    .OrderByDescending(x => x.count)
                    .ThenBy(x => x.sport, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                playerCount = projections
                    .Select(x => !string.IsNullOrWhiteSpace(x.PlayerId) ? x.PlayerId : x.PlayerName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                availabilityCounts = new
                {
                    higher = projections.Count(x => x.OverAvailable),
                    lower = projections.Count(x => x.UnderAvailable),
                    both = projections.Count(x => x.OverAvailable && x.UnderAvailable),
                    neither = projections.Count(x => !x.OverAvailable && !x.UnderAvailable)
                },
                sampleProjections = projections.Take(25).ToList()
            };

            loggerFactory
                .CreateLogger("UnderdogDebug")
                .LogInformation(
                    "Underdog debug endpoint returned {ProjectionCount} parsed projections and {SampleCount} sample records.",
                    response.projectionCount,
                    response.sampleProjections.Count);

            return Results.Ok(response);
        })
        .WithName("DebugUnderdog")
        .WithSummary("Fetches and parses live Underdog projection data without writing to the database.");

        endpoints.MapPost("/api/debug/underdog/ingest", async (
            IUnderdogIngestionService ingestionService,
            CancellationToken cancellationToken) =>
        {
            var result = await ingestionService.IngestAsync(cancellationToken);

            return Results.Ok(new
            {
                projectionsProcessed = result.ProjectionsProcessed,
                playersCreated = result.PlayersCreated,
                playersUpdated = result.PlayersUpdated,
                propsCreated = result.PropsCreated,
                propsUpdated = result.PropsUpdated,
                snapshotsCreated = result.SnapshotsCreated,
                ingestionDuration = result.IngestionDuration
            });
        })
        .WithName("DebugIngestUnderdog")
        .WithSummary("Fetches and persists live Underdog projection data into PostgreSQL.");

        return endpoints;
    }
}
