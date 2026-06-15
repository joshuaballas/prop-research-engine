using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface ITheOddsMlbIngestionPlanner
{
    Task<TheOddsMlbIngestionWorkerPlan> BuildPlanAsync(
        TheOddsIngestionWorkerOptions options,
        int estimatedCreditsUsedToday,
        CancellationToken cancellationToken);
}

public sealed class TheOddsMlbIngestionPlanner(ApplicationDbContext dbContext)
    : ITheOddsMlbIngestionPlanner
{
    public async Task<TheOddsMlbIngestionWorkerPlan> BuildPlanAsync(
        TheOddsIngestionWorkerOptions options,
        int estimatedCreditsUsedToday,
        CancellationToken cancellationToken)
    {
        var requestedBookmakers = ParseBookmakerSet(options.Bookmakers);
        var requestedMarkets = ParseMarketSet(options.Markets);
        var gameDateFilter = ResolveGameDateFilter(options.OnlyGameDates);
        using var filter = JsonSerializer.SerializeToDocument(new { dataProvider = "The Odds API" });

        var rows = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => platformProp.Metadata != null
                && EF.Functions.JsonContains(platformProp.Metadata, filter))
            .Select(platformProp => new TheOddsMlbPlannerPlatformPropRow(
                platformProp.Id,
                platformProp.Label,
                platformProp.ObservedAt,
                platformProp.Platform == null ? "unknown" : platformProp.Platform.Slug,
                platformProp.Metadata))
            .ToListAsync(cancellationToken);

        var parsedRows = rows
            .Select(ParseRow)
            .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
            .Where(row => !gameDateFilter.HasValue
                || DateOnly.FromDateTime((row.EventStartTime ?? row.ObservedAt).UtcDateTime) == gameDateFilter.Value)
            .ToArray();

        var candidateEvents = parsedRows
            .GroupBy(row => row.EventId, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPlannedEvent(group, requestedBookmakers, requestedMarkets))
            .Where(row => row.CoverageOpportunity)
            .OrderBy(row => row.EventStartTime ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.EventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var skippedReasons = new List<string>();
        if (options.DryRun)
        {
            skippedReasons.Add("dry run");
        }

        if (parsedRows.Length == 0)
        {
            skippedReasons.Add("no persisted The Odds API MLB events matched the worker date filter");
        }

        if (candidateEvents.Length == 0)
        {
            skippedReasons.Add("no candidate events");
        }

        var remainingDailyCredits = Math.Max(0, options.MaxEstimatedCreditsPerDay - estimatedCreditsUsedToday);
        var creditLimitedEvents = Math.Min(candidateEvents.Length, Math.Max(0, options.MaxEstimatedCreditsPerRun));
        creditLimitedEvents = Math.Min(creditLimitedEvents, remainingDailyCredits);
        var selectedEvents = candidateEvents
            .Take(Math.Max(0, Math.Min(options.MaxEventsPerRun, creditLimitedEvents)))
            .ToArray();

        if (remainingDailyCredits == 0 && candidateEvents.Length > 0)
        {
            skippedReasons.Add("daily estimated credit cap reached");
        }

        if (options.MaxEstimatedCreditsPerRun <= 0 && candidateEvents.Length > 0)
        {
            skippedReasons.Add("per-run estimated credit cap reached");
        }

        if (selectedEvents.Length < candidateEvents.Length)
        {
            skippedReasons.Add("candidate events limited by max events or estimated credit caps");
        }

        if (selectedEvents.Any(row => row.CoverageOpportunity))
        {
            skippedReasons.Add("optional coverage only");
        }

        if (!options.DryRun)
        {
            skippedReasons.Add("live automatic The Odds API polling is not implemented in this guarded skeleton");
        }

        return new TheOddsMlbIngestionWorkerPlan(
            candidateEvents.Length,
            selectedEvents.Length,
            selectedEvents,
            selectedEvents.Length,
            skippedReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static TheOddsMlbIngestionWorkerPlannedEvent BuildPlannedEvent(
        IGrouping<string, TheOddsMlbPlannerParsedRow> group,
        IReadOnlySet<string> requestedBookmakers,
        IReadOnlySet<string> requestedMarkets)
    {
        var rows = group.ToArray();
        var presentBookmakers = rows
            .Select(row => NormalizeBookmakerKey(row.BookmakerKey, row.PlatformSlug))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var presentMarkets = rows
            .Select(row => row.MarketKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingBookmakers = requestedBookmakers
            .Where(bookmaker => !presentBookmakers.Contains(bookmaker))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingMarkets = requestedMarkets
            .Where(market => !presentMarkets.Contains(market))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reasons = new List<string>();

        if (missingBookmakers.Length > 0)
        {
            reasons.Add($"Missing requested bookmakers: {string.Join(", ", missingBookmakers)}.");
        }

        if (missingMarkets.Length > 0)
        {
            reasons.Add($"Missing requested markets: {string.Join(", ", missingMarkets)}.");
        }

        var homeTeam = rows.Select(row => row.HomeTeam).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var awayTeam = rows.Select(row => row.AwayTeam).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var status = reasons.Count > 0 ? "CoverageOpportunity" : "AlreadyCovered";

        if (reasons.Count == 0)
        {
            reasons.Add("event already line-shopping ready for requested persisted coverage");
        }

        return new TheOddsMlbIngestionWorkerPlannedEvent(
            group.Key,
            BuildEventName(homeTeam, awayTeam),
            rows.Select(row => row.EventStartTime).FirstOrDefault(value => value.HasValue),
            rows.Length,
            missingBookmakers.Length > 0 || missingMarkets.Length > 0,
            status,
            reasons);
    }

    private static TheOddsMlbPlannerParsedRow ParseRow(TheOddsMlbPlannerPlatformPropRow row)
    {
        return new TheOddsMlbPlannerParsedRow(
            row.PlatformPropGuid,
            row.ObservedAt,
            row.PlatformSlug,
            ExtractEventString(row.Metadata, "eventId") ?? string.Empty,
            ExtractEventStartTime(row.Metadata),
            ExtractEventString(row.Metadata, "homeTeam"),
            ExtractEventString(row.Metadata, "awayTeam"),
            ExtractMetadataString(row.Metadata, "bookmaker", "key"),
            ExtractMetadataString(row.Metadata, "market", "key"));
    }

    private static DateOnly? ResolveGameDateFilter(string onlyGameDates)
    {
        var value = onlyGameDates.Trim();
        if (string.Equals(value, "All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(value, "Tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        }

        if (DateOnly.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static IReadOnlySet<string> ParseBookmakerSet(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => NormalizeBookmakerKey(item, item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ParseMarketSet(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeBookmakerKey(string? bookmakerKey, string? bookmakerName)
    {
        var value = !string.IsNullOrWhiteSpace(bookmakerKey) ? bookmakerKey : bookmakerName ?? string.Empty;
        var normalized = new StringBuilder(value.Length);

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(character);
            }
        }

        return normalized.ToString() switch
        {
            "draftkings" => "draftkings",
            "fanduel" => "fanduel",
            "betmgm" => "betmgm",
            "bovada" => "bovada",
            var other when !string.IsNullOrWhiteSpace(other) => other,
            _ => "unknown"
        };
    }

    private static string BuildEventName(string? homeTeam, string? awayTeam)
    {
        if (!string.IsNullOrWhiteSpace(awayTeam) && !string.IsNullOrWhiteSpace(homeTeam))
        {
            return $"{awayTeam} @ {homeTeam}";
        }

        return !string.IsNullOrWhiteSpace(homeTeam)
            ? homeTeam
            : !string.IsNullOrWhiteSpace(awayTeam)
                ? awayTeam
                : "Unknown MLB event";
    }

    private static string? ExtractEventString(JsonDocument? metadata, string propertyName)
    {
        return ExtractMetadataString(metadata, "eventInfo", propertyName);
    }

    private static string? ExtractMetadataString(JsonDocument? metadata, string sectionName, string propertyName)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty(sectionName, out var section)
            || section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static DateTimeOffset? ExtractEventStartTime(JsonDocument? metadata)
    {
        if (metadata?.RootElement.ValueKind != JsonValueKind.Object
            || !metadata.RootElement.TryGetProperty("eventInfo", out var eventInfo)
            || eventInfo.ValueKind != JsonValueKind.Object
            || !eventInfo.TryGetProperty("eventStartTime", out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(property.GetString(), out var parsed)
                ? parsed
                : null;
    }

    private sealed record TheOddsMlbPlannerPlatformPropRow(
        Guid PlatformPropGuid,
        string Label,
        DateTimeOffset ObservedAt,
        string PlatformSlug,
        JsonDocument? Metadata);

    private sealed record TheOddsMlbPlannerParsedRow(
        Guid PlatformPropGuid,
        DateTimeOffset ObservedAt,
        string PlatformSlug,
        string EventId,
        DateTimeOffset? EventStartTime,
        string? HomeTeam,
        string? AwayTeam,
        string? BookmakerKey,
        string? MarketKey);
}

public sealed record TheOddsMlbIngestionWorkerPlan(
    int CandidateEvents,
    int PlannedEventCount,
    IReadOnlyList<TheOddsMlbIngestionWorkerPlannedEvent> PlannedEvents,
    int EstimatedCredits,
    IReadOnlyList<string> SkippedReasons);
