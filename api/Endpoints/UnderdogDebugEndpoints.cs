using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.Underdog;
using SportsBetting.Api.Models;

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

        endpoints.MapGet("/api/debug/underdog/source-coverage-audit", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;

            var persistedRows = await dbContext.PlatformProps
                .AsNoTracking()
                .Include(x => x.Platform)
                .Include(x => x.Prop)
                    .ThenInclude(x => x!.Player)
                .Include(x => x.Prop)
                    .ThenInclude(x => x!.PropType)
                .Include(x => x.Prop)
                    .ThenInclude(x => x!.League)
                    .ThenInclude(x => x!.Sport)
                .Where(x =>
                    (x.Platform != null && (x.Platform.Slug.ToLower().Contains("underdog") || x.Platform.Name.ToLower().Contains("underdog"))) ||
                    (x.Metadata != null && (
                        EF.Functions.JsonContains(x.Metadata, """{"source":"underdog"}""") ||
                        EF.Functions.JsonContains(x.Metadata, """{"dataProvider":"The Odds API"}"""))))
                .ToListAsync(cancellationToken);

            var directRows = persistedRows
                .Where(IsDirectUnderdogRow)
                .Select(row => BuildCoverageRow(row, "DirectUnderdog"))
                .ToList();

            var theOddsRows = persistedRows
                .Where(IsTheOddsUnderdogDfsRow)
                .Select(row => BuildCoverageRow(row, "TheOddsUnderdogDfs"))
                .ToList();

            var directByKey = directRows
                .GroupBy(row => row.MatchKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var theOddsByKey = theOddsRows
                .GroupBy(row => row.MatchKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var matchedKeys = directByKey.Keys
                .Intersect(theOddsByKey.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var uniqueDirectKeys = directByKey.Keys
                .Except(theOddsByKey.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var uniqueTheOddsKeys = theOddsByKey.Keys
                .Except(directByKey.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var uniqueDirectRows = uniqueDirectKeys.SelectMany(key => directByKey[key]).ToList();
            var uniqueTheOddsRows = uniqueTheOddsKeys.SelectMany(key => theOddsByKey[key]).ToList();
            var matchedDirectRows = matchedKeys.SelectMany(key => directByKey[key]).ToList();
            var matchedTheOddsRows = matchedKeys.SelectMany(key => theOddsByKey[key]).ToList();

            var failedMatchSamples = BuildFailedMatchSamples(uniqueDirectRows, theOddsRows, "DirectOnly")
                .Concat(BuildFailedMatchSamples(uniqueTheOddsRows, directRows, "TheOddsOnly"))
                .Take(50)
                .ToList();

            return Results.Ok(new
            {
                generatedAtUtc,
                callsExternalApis = false,
                readOnly = true,
                directUnderdog = BuildCoverageSummary(directRows),
                theOddsUnderdog = BuildCoverageSummary(theOddsRows),
                overlap = new
                {
                    matchedKeys = matchedKeys.Count,
                    matchedDirectRows = matchedDirectRows.Count,
                    matchedTheOddsRows = matchedTheOddsRows.Count,
                    uniqueToDirectKeys = uniqueDirectKeys.Count,
                    uniqueToTheOddsKeys = uniqueTheOddsKeys.Count,
                    overlapRateByDirectKeys = directByKey.Count == 0 ? 0 : Math.Round((decimal)matchedKeys.Count / directByKey.Count, 4),
                    overlapRateByTheOddsKeys = theOddsByKey.Count == 0 ? 0 : Math.Round((decimal)matchedKeys.Count / theOddsByKey.Count, 4)
                },
                uniqueToDirect = new
                {
                    count = uniqueDirectRows.Count,
                    keyCount = uniqueDirectKeys.Count,
                    samples = uniqueDirectRows.Take(50)
                },
                uniqueToTheOdds = new
                {
                    count = uniqueTheOddsRows.Count,
                    keyCount = uniqueTheOddsKeys.Count,
                    samples = uniqueTheOddsRows.Take(50)
                },
                normalizedSportBreakdown = BuildBreakdown(directRows, theOddsRows, row => row.Sport),
                propTypeBreakdown = BuildBreakdown(directRows, theOddsRows, row => row.PropType),
                eventDateBreakdown = BuildBreakdown(directRows, theOddsRows, row => row.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown"),
                freshnessComparison = BuildFreshnessComparison(directRows, theOddsRows, generatedAtUtc),
                matchedSamples = matchedKeys
                    .Take(25)
                    .Select(key => new
                    {
                        matchKey = key,
                        direct = directByKey[key].Take(3),
                        theOdds = theOddsByKey[key].Take(3)
                    })
                    .ToList(),
                failedMatchSamples,
                recommendation = RecommendSourceCoverage(directRows, theOddsRows, matchedKeys.Count, uniqueDirectKeys.Count)
            });
        })
        .WithName("DebugUnderdogSourceCoverageAudit")
        .WithSummary("Compares persisted direct Underdog rows against persisted The Odds API Underdog DFS rows without external calls.");

        return endpoints;
    }

    private static bool IsDirectUnderdogRow(PlatformProp row)
    {
        if (row.Platform is not null &&
            (EqualsIgnoreCase(row.Platform.Slug, "underdog") ||
             row.Platform.Slug.Contains("underdog", StringComparison.OrdinalIgnoreCase) ||
             row.Platform.Name.Contains("Underdog", StringComparison.OrdinalIgnoreCase)))
        {
            return !IsTheOddsUnderdogDfsRow(row);
        }

        return EqualsIgnoreCase(ExtractRootString(row.Metadata, "source"), "underdog") &&
            !EqualsIgnoreCase(ExtractRootString(row.Metadata, "dataProvider"), "The Odds API");
    }

    private static bool IsTheOddsUnderdogDfsRow(PlatformProp row)
    {
        var metadata = row.Metadata;

        return EqualsIgnoreCase(ExtractRootString(metadata, "dataProvider"), "The Odds API") &&
            EqualsIgnoreCase(ExtractNestedString(metadata, "bookmaker", "key"), "underdog") &&
            EqualsIgnoreCase(ExtractRootString(metadata, "providerCategory"), "DFS");
    }

    private static UnderdogCoverageAuditRow BuildCoverageRow(PlatformProp row, string source)
    {
        var metadata = row.Metadata;
        var eventStartTime = ExtractNestedDateTimeOffset(metadata, "eventInfo", "EventStartTime")
            ?? ExtractNestedDateTimeOffset(metadata, "eventInfo", "eventStartTime")
            ?? ExtractNestedDateTimeOffset(metadata, "game", "StartsAt")
            ?? row.Prop?.StartsAt;

        var playerName = row.Prop?.Player?.DisplayName
            ?? ExtractNestedString(metadata, "player", "PlayerName")
            ?? ExtractRawOutcomePlayerName(metadata)
            ?? "Unknown";

        var sport = row.Prop?.League?.Sport?.Code
            ?? row.Prop?.League?.Sport?.Name
            ?? ExtractNestedString(metadata, "player", "SportId")
            ?? "Unknown";

        var league = row.Prop?.League?.Code
            ?? row.Prop?.League?.Name
            ?? sport;

        var propType = row.Prop?.PropType?.Name
            ?? ExtractNestedString(metadata, "stat", "StatDisplayName")
            ?? ExtractNestedString(metadata, "market", "key")
            ?? "Unknown";

        var eventName = BuildEventName(metadata)
            ?? ExtractNestedString(metadata, "game", "GameTitle")
            ?? "Unknown";

        var bookmakerKey = ExtractNestedString(metadata, "bookmaker", "key");
        var bookmakerTitle = ExtractNestedString(metadata, "bookmaker", "title");
        var eventId = ExtractNestedString(metadata, "eventInfo", "eventId") ?? ExtractNestedString(metadata, "game", "GameId");
        DateOnly? eventDate = eventStartTime.HasValue
            ? DateOnly.FromDateTime(eventStartTime.Value.UtcDateTime.Date)
            : null;
        var side = row.OverAvailable && row.UnderAvailable
            ? "Both"
            : row.OverAvailable
                ? "Over"
                : row.UnderAvailable
                    ? "Under"
                    : "None";

        var matchKey = BuildMatchKey(playerName, sport, league, propType, row.CurrentLine, side, eventDate, eventName);

        return new UnderdogCoverageAuditRow(
            Source: source,
            Platform: row.Platform?.Name ?? "Unknown",
            PlatformSlug: row.Platform?.Slug ?? "Unknown",
            BookmakerKey: bookmakerKey,
            BookmakerTitle: bookmakerTitle,
            PlatformPropId: row.PlatformPropId,
            Player: playerName,
            Sport: NormalizeDisplay(sport),
            League: NormalizeDisplay(league),
            PropType: NormalizeDisplay(propType),
            Line: row.CurrentLine,
            SideAvailability: side,
            EventId: eventId,
            EventName: eventName,
            EventStartTime: eventStartTime,
            EventDate: eventDate,
            ObservedAt: row.ObservedAt,
            Status: row.Status,
            OverAvailable: row.OverAvailable,
            UnderAvailable: row.UnderAvailable,
            PayoutType: row.PayoutType,
            MatchKey: matchKey);
    }

    private static object BuildCoverageSummary(IReadOnlyCollection<UnderdogCoverageAuditRow> rows)
    {
        var latestObservedAt = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(row => row.ObservedAt);
        var earliestObservedAt = rows.Count == 0 ? (DateTimeOffset?)null : rows.Min(row => row.ObservedAt);

        return new
        {
            platformPropCount = rows.Count,
            distinctMatchKeys = rows.Select(row => row.MatchKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            activeRows = rows.Count(row => IsActiveStatus(row.Status)),
            overAvailableCount = rows.Count(row => row.OverAvailable),
            underAvailableCount = rows.Count(row => row.UnderAvailable),
            latestObservedAt,
            earliestObservedAt,
            sports = rows
                .GroupBy(row => row.Sport, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { sport = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.sport, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            propTypes = rows
                .GroupBy(row => row.PropType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { propType = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.propType, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList(),
            platforms = rows
                .GroupBy(row => row.Platform, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { platform = group.Key, count = group.Count() })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.platform, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static object BuildFreshnessComparison(
        IReadOnlyCollection<UnderdogCoverageAuditRow> directRows,
        IReadOnlyCollection<UnderdogCoverageAuditRow> theOddsRows,
        DateTimeOffset now)
    {
        static object Freshness(IReadOnlyCollection<UnderdogCoverageAuditRow> rows, DateTimeOffset now)
        {
            var latestObservedAt = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(row => row.ObservedAt);
            return new
            {
                latestObservedAt,
                minutesSinceLatestObserved = latestObservedAt.HasValue
                    ? Math.Round((now - latestObservedAt.Value).TotalMinutes, 2)
                    : (double?)null,
                rowsObservedLast30Minutes = rows.Count(row => (now - row.ObservedAt).TotalMinutes <= 30),
                rowsObservedLast2Hours = rows.Count(row => (now - row.ObservedAt).TotalHours <= 2),
                rowsObservedLast24Hours = rows.Count(row => (now - row.ObservedAt).TotalHours <= 24)
            };
        }

        return new
        {
            directUnderdog = Freshness(directRows, now),
            theOddsUnderdog = Freshness(theOddsRows, now)
        };
    }

    private static object BuildBreakdown(
        IReadOnlyCollection<UnderdogCoverageAuditRow> directRows,
        IReadOnlyCollection<UnderdogCoverageAuditRow> theOddsRows,
        Func<UnderdogCoverageAuditRow, string> keySelector)
    {
        var directGroups = directRows
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var theOddsGroups = theOddsRows
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return directGroups.Keys
            .Union(theOddsGroups.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(key => new
            {
                key,
                directUnderdogCount = directGroups.GetValueOrDefault(key),
                theOddsUnderdogCount = theOddsGroups.GetValueOrDefault(key)
            })
            .OrderByDescending(row => row.directUnderdogCount + row.theOddsUnderdogCount)
            .ThenBy(row => row.key, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
    }

    private static IEnumerable<object> BuildFailedMatchSamples(
        IReadOnlyCollection<UnderdogCoverageAuditRow> sourceRows,
        IReadOnlyCollection<UnderdogCoverageAuditRow> comparisonRows,
        string sampleType)
    {
        return sourceRows
            .Take(25)
            .Select(row =>
            {
                var samePlayer = comparisonRows
                    .Where(candidate => EqualsIgnoreCase(NormalizeText(candidate.Player), NormalizeText(row.Player)))
                    .Take(5)
                    .ToList();
                var samePropAndLine = comparisonRows
                    .Where(candidate =>
                        EqualsIgnoreCase(NormalizeText(candidate.PropType), NormalizeText(row.PropType)) &&
                        candidate.Line == row.Line)
                    .Take(5)
                    .ToList();
                var sameEventDate = row.EventDate.HasValue
                    ? comparisonRows
                        .Where(candidate => candidate.EventDate == row.EventDate)
                        .Take(5)
                        .ToList()
                    : [];

                var likelyReason = samePlayer.Count > 0
                    ? "Player matched but prop/line/event did not match."
                    : samePropAndLine.Count > 0
                        ? "Prop type and line matched but player/event did not match."
                        : sameEventDate.Count > 0
                            ? "Event date matched but player/prop/line did not match."
                            : "No conservative player/prop/line/event match found.";

                return new
                {
                    sampleType,
                    source = row,
                    likelyReason,
                    closestSamePlayer = samePlayer,
                    closestSamePropAndLine = samePropAndLine,
                    closestSameEventDate = sameEventDate
                };
            });
    }

    private static string RecommendSourceCoverage(
        IReadOnlyCollection<UnderdogCoverageAuditRow> directRows,
        IReadOnlyCollection<UnderdogCoverageAuditRow> theOddsRows,
        int matchedKeyCount,
        int uniqueDirectKeyCount)
    {
        if (directRows.Count == 0 && theOddsRows.Count == 0)
        {
            return "NeedsMoreData";
        }

        if (directRows.Count > 0 && theOddsRows.Count == 0)
        {
            return "KeepDirectUnderdog";
        }

        if (directRows.Count == 0)
        {
            return "NeedsMoreData";
        }

        var directKeyCount = directRows.Select(row => row.MatchKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var overlapRate = directKeyCount == 0 ? 0 : (decimal)matchedKeyCount / directKeyCount;
        var directLatest = directRows.Max(row => row.ObservedAt);
        var theOddsLatest = theOddsRows.Count == 0 ? DateTimeOffset.MinValue : theOddsRows.Max(row => row.ObservedAt);

        if (uniqueDirectKeyCount > 0 || directRows.Select(row => row.Sport).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            return "KeepDirectUnderdog";
        }

        if (overlapRate >= 0.95m && theOddsLatest >= directLatest)
        {
            return "NeedsMoreData";
        }

        return "KeepDirectUnderdog";
    }

    private static bool IsActiveStatus(string status)
    {
        return string.IsNullOrWhiteSpace(status) ||
            status.Equals("active", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("available", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMatchKey(
        string playerName,
        string sport,
        string league,
        string propType,
        decimal line,
        string side,
        DateOnly? eventDate,
        string eventName)
    {
        var datePart = eventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? NormalizeText(eventName);

        return string.Join('|',
            NormalizeText(playerName),
            NormalizeText(sport),
            NormalizeText(league),
            NormalizeText(propType),
            line.ToString("0.####", CultureInfo.InvariantCulture),
            NormalizeText(side),
            datePart);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (char.IsWhiteSpace(character) || character is '_' or '-' or '/' or '.')
            {
                builder.Append(' ');
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeDisplay(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }

    private static bool EqualsIgnoreCase(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractRootString(JsonDocument? metadata, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object ||
            !metadata.RootElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? ExtractNestedString(JsonDocument? metadata, string parentName, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object ||
            !metadata.RootElement.TryGetProperty(parentName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static DateTimeOffset? ExtractNestedDateTimeOffset(JsonDocument? metadata, string parentName, string propertyName)
    {
        var value = ExtractNestedString(metadata, parentName, propertyName);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ExtractRawOutcomePlayerName(JsonDocument? metadata)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object ||
            !metadata.RootElement.TryGetProperty("rawOutcomes", out var rawOutcomes) ||
            rawOutcomes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var outcome in rawOutcomes.EnumerateArray())
        {
            if (outcome.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (outcome.TryGetProperty("PlayerName", out var playerName) && playerName.ValueKind == JsonValueKind.String)
            {
                return playerName.GetString();
            }

            if (outcome.TryGetProperty("playerName", out var camelPlayerName) && camelPlayerName.ValueKind == JsonValueKind.String)
            {
                return camelPlayerName.GetString();
            }
        }

        return null;
    }

    private static string? BuildEventName(JsonDocument? metadata)
    {
        var awayTeam = ExtractNestedString(metadata, "eventInfo", "AwayTeam")
            ?? ExtractNestedString(metadata, "eventInfo", "awayTeam");
        var homeTeam = ExtractNestedString(metadata, "eventInfo", "HomeTeam")
            ?? ExtractNestedString(metadata, "eventInfo", "homeTeam");

        if (string.IsNullOrWhiteSpace(awayTeam) && string.IsNullOrWhiteSpace(homeTeam))
        {
            return null;
        }

        return $"{awayTeam ?? "Unknown"} @ {homeTeam ?? "Unknown"}";
    }

    private sealed record UnderdogCoverageAuditRow(
        string Source,
        string Platform,
        string PlatformSlug,
        string? BookmakerKey,
        string? BookmakerTitle,
        string PlatformPropId,
        string Player,
        string Sport,
        string League,
        string PropType,
        decimal Line,
        string SideAvailability,
        string? EventId,
        string EventName,
        DateTimeOffset? EventStartTime,
        DateOnly? EventDate,
        DateTimeOffset ObservedAt,
        string Status,
        bool OverAvailable,
        bool UnderAvailable,
        string PayoutType,
        string MatchKey);
}
