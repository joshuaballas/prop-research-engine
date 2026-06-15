namespace SportsBetting.Api.Endpoints;

public static class PrizePicksDebugEndpoints
{
    public static IEndpointRouteBuilder MapPrizePicksDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/prizepicks", () =>
        {
            return Results.Json(PrizePicksDisabledResponse(), statusCode: StatusCodes.Status410Gone);
        })
        .WithName("DebugPrizePicks")
        .WithSummary("Returns disabled status for retired PrizePicks direct ingestion experiments.");

        endpoints.MapPost("/api/debug/prizepicks/ingest", () =>
        {
            return Results.Json(PrizePicksDisabledResponse(), statusCode: StatusCodes.Status410Gone);
        })
        .WithName("DebugIngestPrizePicks")
        .WithSummary("Returns disabled status for retired PrizePicks direct ingestion experiments.");

        return endpoints;
    }

    private static object PrizePicksDisabledResponse()
    {
        return new
        {
            success = false,
            source = "PrizePicks",
            status = "disabled",
            message = "PrizePicks direct ingestion experiments are retired.",
            recommendedAction = "Do not attempt DataDome bypass or browser scraping. Revisit only with an official or otherwise allowed stable data source."
        };
    }
}
