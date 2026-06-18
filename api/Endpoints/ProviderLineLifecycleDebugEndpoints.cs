using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Endpoints;

public static class ProviderLineLifecycleDebugEndpoints
{
    public static IEndpointRouteBuilder MapProviderLineLifecycleDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/provider-lines/lifecycles", async (
            string? sport,
            string? providerKey,
            string? providerCategory,
            string? status,
            string? propType,
            string? player,
            DateOnly? slateDate,
            string? eventId,
            int? take,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var requestedTake = Math.Clamp(take ?? 100, 1, 500);
            var query = ApplyLifecycleFilters(
                dbContext.ProviderLineLifecycles.AsNoTracking(),
                sport,
                providerKey,
                providerCategory,
                status,
                propType,
                player,
                slateDate,
                eventId);

            var rows = await query
                .OrderByDescending(row => row.UpdatedAtUtc)
                .ThenBy(row => row.ProviderKey)
                .ThenBy(row => row.NormalizedPlayerName)
                .Take(requestedTake)
                .Select(row => new
                {
                    row.Id,
                    row.Sport,
                    row.ProviderKey,
                    row.ProviderDisplayName,
                    row.ProviderCategory,
                    row.EventId,
                    row.EventStartTimeUtc,
                    row.SlateDate,
                    row.NormalizedPlayerName,
                    row.MarketKey,
                    row.PropType,
                    row.Side,
                    row.CurrentLine,
                    row.CurrentPrice,
                    row.CurrentStatus,
                    row.FirstSeenUtc,
                    row.LastSeenUtc,
                    row.LastChangedUtc,
                    row.LastRefreshRunId,
                    row.LastSnapshotId,
                    row.SeenCount,
                    row.ChangedCount,
                    row.MissingCount,
                    row.PulledAtUtc,
                    row.LockedAtUtc,
                    row.ExpiredAtUtc,
                    lineAvailableForMinutes = MinutesBetween(row.FirstSeenUtc, row.PulledAtUtc ?? row.LockedAtUtc ?? row.ExpiredAtUtc ?? row.LastSeenUtc),
                    lineAgeMinutes = MinutesBetween(row.LastSeenUtc, DateTimeOffset.UtcNow),
                    lineMoved = row.ChangedCount > 0
                })
                .ToArrayAsync(cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                take = requestedTake,
                count = rows.Length,
                rows
            });
        })
        .WithName("ProviderLineLifecyclesDebug")
        .WithSummary("Read-only provider line lifecycle rows.");

        endpoints.MapGet("/api/debug/provider-lines/lifecycle-summary", async (
            string? sport,
            string? providerKey,
            string? providerCategory,
            string? propType,
            DateOnly? fromDate,
            DateOnly? toDate,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var query = ApplyLifecycleFilters(
                dbContext.ProviderLineLifecycles.AsNoTracking(),
                sport,
                providerKey,
                providerCategory,
                status: null,
                propType,
                player: null,
                slateDate: null,
                eventId: null);

            if (fromDate.HasValue)
            {
                query = query.Where(row => row.SlateDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(row => row.SlateDate <= toDate.Value);
            }

            var rows = await query.ToArrayAsync(cancellationToken);
            var lifetimeMinutes = rows
                .Select(row => MinutesBetween(row.FirstSeenUtc, row.PulledAtUtc ?? row.LockedAtUtc ?? row.ExpiredAtUtc ?? row.LastSeenUtc))
                .Where(value => value >= 0)
                .Order()
                .ToArray();
            var timeToFirstChangeMinutes = rows
                .Where(row => row.LastChangedUtc.HasValue)
                .Select(row => MinutesBetween(row.FirstSeenUtc, row.LastChangedUtc!.Value))
                .Where(value => value >= 0)
                .Order()
                .ToArray();

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                totalLinesTracked = rows.Length,
                activeCount = CountStatus(rows, ProviderLineLifecycleStatuses.Active),
                staleCount = CountStatus(rows, ProviderLineLifecycleStatuses.Stale),
                pulledCount = CountStatus(rows, ProviderLineLifecycleStatuses.Pulled),
                lockedCount = CountStatus(rows, ProviderLineLifecycleStatuses.Locked),
                expiredCount = CountStatus(rows, ProviderLineLifecycleStatuses.Expired),
                providerUnavailableCount = CountStatus(rows, ProviderLineLifecycleStatuses.ProviderUnavailable),
                averageLifetimeMinutes = AverageOrNull(lifetimeMinutes),
                medianLifetimeMinutes = MedianOrNull(lifetimeMinutes),
                averageTimeToFirstChangeMinutes = AverageOrNull(timeToFirstChangeMinutes),
                changedLineCount = rows.Count(row => row.ChangedCount > 0),
                pulledLineCount = rows.Count(row => string.Equals(row.CurrentStatus, ProviderLineLifecycleStatuses.Pulled, StringComparison.OrdinalIgnoreCase)),
                byProvider = rows
                    .GroupBy(row => new { row.ProviderKey, row.ProviderDisplayName, row.ProviderCategory })
                    .OrderByDescending(group => group.Count())
                    .Select(group => new
                    {
                        group.Key.ProviderKey,
                        group.Key.ProviderDisplayName,
                        group.Key.ProviderCategory,
                        count = group.Count(),
                        active = group.Count(row => string.Equals(row.CurrentStatus, ProviderLineLifecycleStatuses.Active, StringComparison.OrdinalIgnoreCase)),
                        pulled = group.Count(row => string.Equals(row.CurrentStatus, ProviderLineLifecycleStatuses.Pulled, StringComparison.OrdinalIgnoreCase)),
                        changed = group.Count(row => row.ChangedCount > 0)
                    })
                    .ToArray(),
                byPropType = rows
                    .GroupBy(row => row.PropType)
                    .OrderByDescending(group => group.Count())
                    .Select(group => new { propType = group.Key, count = group.Count(), changed = group.Count(row => row.ChangedCount > 0) })
                    .ToArray(),
                bySport = rows
                    .GroupBy(row => row.Sport)
                    .OrderByDescending(group => group.Count())
                    .Select(group => new { sport = group.Key, count = group.Count() })
                    .ToArray()
            });
        })
        .WithName("ProviderLineLifecycleSummaryDebug")
        .WithSummary("Read-only provider line lifecycle aggregate summary.");

        endpoints.MapGet("/api/debug/provider-lines/lifecycle-events", async (
            Guid? lifecycleId,
            string? sport,
            string? providerKey,
            string? eventType,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            int? take,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var requestedTake = Math.Clamp(take ?? 100, 1, 500);
            var query = dbContext.ProviderLineLifecycleEvents
                .AsNoTracking()
                .Include(row => row.ProviderLineLifecycle)
                .AsQueryable();

            if (lifecycleId.HasValue)
            {
                query = query.Where(row => row.ProviderLineLifecycleId == lifecycleId.Value);
            }

            if (!string.IsNullOrWhiteSpace(sport))
            {
                query = query.Where(row => row.ProviderLineLifecycle != null && row.ProviderLineLifecycle.Sport == sport);
            }

            if (!string.IsNullOrWhiteSpace(providerKey))
            {
                query = query.Where(row => row.ProviderLineLifecycle != null && row.ProviderLineLifecycle.ProviderKey == NormalizeProviderKey(providerKey));
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(row => row.EventType == eventType);
            }

            if (fromUtc.HasValue)
            {
                query = query.Where(row => row.OccurredAtUtc >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                query = query.Where(row => row.OccurredAtUtc <= toUtc.Value);
            }

            var rows = await query
                .OrderByDescending(row => row.OccurredAtUtc)
                .Take(requestedTake)
                .Select(row => new
                {
                    row.Id,
                    row.ProviderLineLifecycleId,
                    row.RefreshRunId,
                    row.OccurredAtUtc,
                    row.EventType,
                    row.PreviousLine,
                    row.NewLine,
                    row.PreviousPrice,
                    row.NewPrice,
                    row.PreviousStatus,
                    row.NewStatus,
                    row.MetadataJson,
                    lifecycle = row.ProviderLineLifecycle == null ? null : new
                    {
                        row.ProviderLineLifecycle.Sport,
                        row.ProviderLineLifecycle.ProviderKey,
                        row.ProviderLineLifecycle.ProviderDisplayName,
                        row.ProviderLineLifecycle.ProviderCategory,
                        row.ProviderLineLifecycle.NormalizedPlayerName,
                        row.ProviderLineLifecycle.PropType,
                        row.ProviderLineLifecycle.Side,
                        row.ProviderLineLifecycle.CurrentStatus
                    }
                })
                .ToArrayAsync(cancellationToken);

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                take = requestedTake,
                count = rows.Length,
                rows
            });
        })
        .WithName("ProviderLineLifecycleEventsDebug")
        .WithSummary("Read-only provider line lifecycle event history.");

        endpoints.MapGet("/api/debug/provider-lines/refresh-cadence-insights", async (
            string? sport,
            string? providerKey,
            string? propType,
            DateOnly? fromDate,
            DateOnly? toDate,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var query = ApplyLifecycleFilters(
                dbContext.ProviderLineLifecycles.AsNoTracking(),
                sport,
                providerKey,
                providerCategory: null,
                status: null,
                propType,
                player: null,
                slateDate: null,
                eventId: null);

            if (fromDate.HasValue)
            {
                query = query.Where(row => row.SlateDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(row => row.SlateDate <= toDate.Value);
            }

            var rows = await query.ToArrayAsync(cancellationToken);
            var lifetimeMinutes = rows
                .Select(row => MinutesBetween(row.FirstSeenUtc, row.PulledAtUtc ?? row.LockedAtUtc ?? row.ExpiredAtUtc ?? row.LastSeenUtc))
                .Where(value => value >= 0)
                .Order()
                .ToArray();
            var changeMinutes = rows
                .Where(row => row.LastChangedUtc.HasValue)
                .Select(row => MinutesBetween(row.FirstSeenUtc, row.LastChangedUtc!.Value))
                .Where(value => value >= 0)
                .ToArray();
            var pulledMinutes = rows
                .Where(row => row.PulledAtUtc.HasValue)
                .Select(row => MinutesBetween(row.FirstSeenUtc, row.PulledAtUtc!.Value))
                .Where(value => value >= 0)
                .ToArray();
            var changedWithin10 = PercentWithin(changeMinutes, rows.Length, 10);
            var pulledWithin10 = PercentWithin(pulledMinutes, rows.Length, 10);
            var suggestedRefreshMinutes = SuggestRefreshMinutes(changedWithin10, pulledWithin10);
            var providerLabel = string.IsNullOrWhiteSpace(providerKey) ? "provider" : providerKey;
            var propLabel = string.IsNullOrWhiteSpace(propType) ? "lines" : propType;

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                totalLinesTracked = rows.Length,
                averageLineLifetimeMinutes = AverageOrNull(lifetimeMinutes),
                medianLineLifetimeMinutes = MedianOrNull(lifetimeMinutes),
                percentPulledWithin5Minutes = PercentWithin(pulledMinutes, rows.Length, 5),
                percentPulledWithin10Minutes = pulledWithin10,
                percentPulledWithin15Minutes = PercentWithin(pulledMinutes, rows.Length, 15),
                percentPulledWithin30Minutes = PercentWithin(pulledMinutes, rows.Length, 30),
                percentPulledWithin60Minutes = PercentWithin(pulledMinutes, rows.Length, 60),
                percentChangedWithin5Minutes = PercentWithin(changeMinutes, rows.Length, 5),
                percentChangedWithin10Minutes = changedWithin10,
                percentChangedWithin15Minutes = PercentWithin(changeMinutes, rows.Length, 15),
                percentChangedWithin30Minutes = PercentWithin(changeMinutes, rows.Length, 30),
                percentChangedWithin60Minutes = PercentWithin(changeMinutes, rows.Length, 60),
                suggestedRefreshMinutes,
                reason = rows.Length == 0
                    ? "No lifecycle rows matched the requested filters yet."
                    : $"Observed {providerLabel} {propLabel}: {Math.Round(changedWithin10, 2)}% changed and {Math.Round(pulledWithin10, 2)}% pulled within 10 minutes. Use this with credit budget before lowering cadence."
            });
        })
        .WithName("ProviderLineLifecycleRefreshCadenceInsightsDebug")
        .WithSummary("Read-only lifecycle-derived refresh cadence guidance.");

        return endpoints;
    }

    private static IQueryable<ProviderLineLifecycle> ApplyLifecycleFilters(
        IQueryable<ProviderLineLifecycle> query,
        string? sport,
        string? providerKey,
        string? providerCategory,
        string? status,
        string? propType,
        string? player,
        DateOnly? slateDate,
        string? eventId)
    {
        if (!string.IsNullOrWhiteSpace(sport))
        {
            query = query.Where(row => row.Sport == sport);
        }

        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            var normalizedProviderKey = NormalizeProviderKey(providerKey);
            query = query.Where(row => row.ProviderKey == normalizedProviderKey);
        }

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            query = query.Where(row => row.ProviderCategory == providerCategory);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(row => row.CurrentStatus == status);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            query = query.Where(row => row.PropType == propType || row.MarketKey == propType.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(player))
        {
            var normalizedPlayer = player.Trim().ToLower();
            query = query.Where(row => row.NormalizedPlayerName.Contains(normalizedPlayer));
        }

        if (slateDate.HasValue)
        {
            query = query.Where(row => row.SlateDate == slateDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(eventId))
        {
            query = query.Where(row => row.EventId == eventId);
        }

        return query;
    }

    private static int CountStatus(IEnumerable<ProviderLineLifecycle> rows, string status) =>
        rows.Count(row => string.Equals(row.CurrentStatus, status, StringComparison.OrdinalIgnoreCase));

    private static double MinutesBetween(DateTimeOffset start, DateTimeOffset end) =>
        Math.Round((end - start).TotalMinutes, 2);

    private static double? AverageOrNull(IReadOnlyCollection<double> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 2);

    private static double? MedianOrNull(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? Math.Round(values[middle], 2)
            : Math.Round((values[middle - 1] + values[middle]) / 2, 2);
    }

    private static double PercentWithin(IReadOnlyCollection<double> values, int denominator, int minutes) =>
        denominator == 0 ? 0 : Math.Round(values.Count(value => value <= minutes) * 100.0 / denominator, 2);

    private static int SuggestRefreshMinutes(double changedWithin10, double pulledWithin10)
    {
        var fastMovement = Math.Max(changedWithin10, pulledWithin10);
        if (fastMovement >= 25)
        {
            return 5;
        }

        if (fastMovement >= 10)
        {
            return 10;
        }

        return 15;
    }

    private static string NormalizeProviderKey(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLower().Replace(" ", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal);
}
