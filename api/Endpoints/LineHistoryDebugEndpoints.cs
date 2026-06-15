using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;

namespace SportsBetting.Api.Endpoints;

public static class LineHistoryDebugEndpoints
{
    public static IEndpointRouteBuilder MapLineHistoryDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/line-history", async (
            string? playerName,
            string? propType,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(playerName))
            {
                return Results.BadRequest(new { error = "Query parameter 'playerName' is required." });
            }

            if (string.IsNullOrWhiteSpace(propType))
            {
                return Results.BadRequest(new { error = "Query parameter 'propType' is required." });
            }

            var normalizedPlayerName = playerName.Trim();
            var normalizedPropType = propType.Trim();

            var snapshots = await dbContext.PropLineSnapshots
                .AsNoTracking()
                .Where(snapshot =>
                    snapshot.PlatformProp != null
                    && snapshot.PlatformProp.Platform != null
                    && snapshot.PlatformProp.Prop != null
                    && snapshot.PlatformProp.Prop.Player != null
                    && snapshot.PlatformProp.Prop.PropType != null
                    && snapshot.PlatformProp.Prop.Player.DisplayName.ToLower() == normalizedPlayerName.ToLower()
                    && (snapshot.PlatformProp.Prop.PropType.Name.ToLower() == normalizedPropType.ToLower()
                        || snapshot.PlatformProp.Prop.PropType.StatKey.ToLower() == normalizedPropType.ToLower()))
                .Select(snapshot => new LineHistoryRow(
                    snapshot.PlatformProp!.Id,
                    snapshot.PlatformProp.PlatformPropId,
                    snapshot.PlatformProp.Prop!.Id,
                    snapshot.PlatformProp.Prop.NormalizedKey,
                    snapshot.PlatformProp!.Prop!.Player!.DisplayName,
                    snapshot.PlatformProp.Prop.PropType!.Name,
                    snapshot.PlatformProp.Platform!.Name,
                    snapshot.PlatformProp.CurrentLine,
                    snapshot.PlatformProp.Status,
                    snapshot.ObservedAt,
                    snapshot.Line))
                .ToListAsync(cancellationToken);

            var results = snapshots
                .GroupBy(snapshot => new
                {
                    snapshot.PlatformPropRecordId,
                    snapshot.PlatformPropId,
                    snapshot.PropId,
                    snapshot.NormalizedKey,
                    snapshot.PlayerName,
                    snapshot.PropType,
                    snapshot.Platform,
                    snapshot.CurrentLine,
                    snapshot.Status
                })
                .Select(group => new LineHistoryResult(
                    group.Key.PlayerName,
                    group.Key.PropType,
                    group.Key.Platform,
                    group.Key.PlatformPropRecordId,
                    group.Key.PlatformPropId,
                    group.Key.PropId,
                    group.Key.NormalizedKey,
                    group.Key.CurrentLine,
                    group.Key.Status,
                    group
                        .OrderBy(snapshot => snapshot.ObservedAt)
                        .Select(snapshot => new LineHistorySnapshot(snapshot.ObservedAt, snapshot.Line))
                        .ToArray()))
                .OrderBy(result => result.Platform)
                .ThenBy(result => result.PlayerName)
                .ThenBy(result => result.PropType)
                .ThenBy(result => result.PlatformPropId)
                .ToArray();

            return Results.Ok(new
            {
                playerName = normalizedPlayerName,
                propType = normalizedPropType,
                diagnostics = new
                {
                    snapshotSelection = "prop_line_snapshots joined through platform_props -> props -> players and prop_types; filtered by exact player display name and prop type name or stat key, case-insensitive.",
                    rootCause = "The original endpoint grouped snapshots by player name, prop type, and platform, which merged multiple distinct platform_props for alternate lines or related markets into one history.",
                    groupingStrategy = "Each returned series is now grouped by platformPropRecordId, platformPropId, propId, and normalizedKey, with player name, prop type, and platform retained as labels.",
                    snapshotsSelected = snapshots.Count,
                    marketSeriesReturned = results.Length
                },
                resultCount = results.Length,
                results
            });
        })
        .WithName("LineHistoryDebug")
        .WithSummary("Returns persisted line snapshot history for a player and prop type.");

        return endpoints;
    }

    private sealed record LineHistoryRow(
        Guid PlatformPropRecordId,
        string PlatformPropId,
        Guid PropId,
        string NormalizedKey,
        string PlayerName,
        string PropType,
        string Platform,
        decimal CurrentLine,
        string Status,
        DateTimeOffset ObservedAt,
        decimal Line);

    private sealed record LineHistoryResult(
        string PlayerName,
        string PropType,
        string Platform,
        Guid PlatformPropRecordId,
        string PlatformPropId,
        Guid PropId,
        string NormalizedKey,
        decimal CurrentLine,
        string Status,
        LineHistorySnapshot[] LineHistory);

    private sealed record LineHistorySnapshot(DateTimeOffset ObservedAt, decimal Line);
}
