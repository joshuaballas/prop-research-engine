using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface IProviderLineLifecycleService
{
    Task TrackRefreshAsync(Guid refreshRunId, CancellationToken cancellationToken);
}

public sealed class ProviderLineLifecycleService(
    ApplicationDbContext dbContext,
    IOptions<ProviderLineLifecycleOptions> options) : IProviderLineLifecycleService
{
    public async Task TrackRefreshAsync(Guid refreshRunId, CancellationToken cancellationToken)
    {
        var refreshRun = await dbContext.TheOddsRefreshRuns
            .FirstOrDefaultAsync(row => row.Id == refreshRunId, cancellationToken);
        if (refreshRun is null || !refreshRun.Succeeded)
        {
            return;
        }

        var snapshots = await dbContext.MarketLineShoppingSnapshots
            .Where(row => row.TheOddsRefreshRunId == refreshRunId)
            .OrderBy(row => row.ProviderCategory)
            .ThenBy(row => row.Provider)
            .ThenBy(row => row.Bookmaker)
            .ThenBy(row => row.Player)
            .ThenBy(row => row.PropType)
            .ToListAsync(cancellationToken);

        if (snapshots.Count == 0)
        {
            return;
        }

        var now = refreshRun.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        var seenRows = snapshots
            .Select(ToSeenLine)
            .GroupBy(row => row.Key, LifecycleKey.Comparer)
            .Select(group => group.First())
            .ToArray();

        var seenProviderKeys = seenRows
            .Select(row => row.ProviderKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lifecycleRows = await dbContext.ProviderLineLifecycles
            .Where(row => row.Sport == refreshRun.Sport && row.SlateDate == refreshRun.SlateDate)
            .Where(row => seenProviderKeys.Contains(row.ProviderKey))
            .ToListAsync(cancellationToken);
        var lifecycleByKey = lifecycleRows.ToDictionary(ToLifecycleKey, LifecycleKey.Comparer);
        var seenKeys = new HashSet<LifecycleKey>(LifecycleKey.Comparer);

        foreach (var seenLine in seenRows)
        {
            seenKeys.Add(seenLine.Key);
            if (!lifecycleByKey.TryGetValue(seenLine.Key, out var lifecycle))
            {
                lifecycle = CreateLifecycle(refreshRun, seenLine, now);
                dbContext.ProviderLineLifecycles.Add(lifecycle);
                AddEvent(
                    lifecycle,
                    refreshRun.Id,
                    ProviderLineLifecycleEventTypes.FirstSeen,
                    now,
                    previousLine: null,
                    newLine: seenLine.Line,
                    previousPrice: null,
                    newPrice: seenLine.Price,
                    previousStatus: null,
                    newStatus: lifecycle.CurrentStatus,
                    new { seenLine.SnapshotId });
                continue;
            }

            UpdateSeenLifecycle(lifecycle, refreshRun, seenLine, now);
        }

        foreach (var lifecycle in lifecycleRows)
        {
            var key = ToLifecycleKey(lifecycle);
            if (seenKeys.Contains(key))
            {
                continue;
            }

            UpdateMissingLifecycle(lifecycle, refreshRun, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void UpdateSeenLifecycle(
        ProviderLineLifecycle lifecycle,
        TheOddsRefreshRun refreshRun,
        SeenProviderLine seenLine,
        DateTimeOffset now)
    {
        var previousStatus = lifecycle.CurrentStatus;
        var previousLine = lifecycle.CurrentLine;
        var previousPrice = lifecycle.CurrentPrice;
        var lineChanged = !NullableDecimalEquals(previousLine, seenLine.Line);
        var priceChanged = !NullableDecimalEquals(previousPrice, seenLine.Price);
        var wasInactive = !string.Equals(previousStatus, ProviderLineLifecycleStatuses.Active, StringComparison.OrdinalIgnoreCase);

        lifecycle.ProviderDisplayName = seenLine.ProviderDisplayName;
        lifecycle.ProviderCategory = seenLine.ProviderCategory;
        lifecycle.EventStartTimeUtc = seenLine.EventStartTimeUtc;
        lifecycle.PropType = seenLine.PropType;
        lifecycle.CurrentLine = seenLine.Line;
        lifecycle.CurrentPrice = seenLine.Price;
        lifecycle.CurrentStatus = ProviderLineLifecycleStatuses.Active;
        lifecycle.LastSeenUtc = now;
        lifecycle.LastRefreshRunId = refreshRun.Id;
        lifecycle.LastSnapshotId = seenLine.SnapshotId;
        lifecycle.SeenCount++;
        lifecycle.MissingCount = 0;
        lifecycle.UpdatedAtUtc = now;
        lifecycle.PulledAtUtc = null;

        if (lineChanged || priceChanged)
        {
            lifecycle.LastChangedUtc = now;
            lifecycle.ChangedCount++;
            AddEvent(
                lifecycle,
                refreshRun.Id,
                lineChanged && priceChanged
                    ? ProviderLineLifecycleEventTypes.LineAndPriceChanged
                    : lineChanged
                        ? ProviderLineLifecycleEventTypes.LineChanged
                        : ProviderLineLifecycleEventTypes.PriceChanged,
                now,
                previousLine,
                seenLine.Line,
                previousPrice,
                seenLine.Price,
                previousStatus,
                lifecycle.CurrentStatus,
                new { seenLine.SnapshotId });
            return;
        }

        if (wasInactive)
        {
            AddEvent(
                lifecycle,
                refreshRun.Id,
                ProviderLineLifecycleEventTypes.Reactivated,
                now,
                previousLine,
                seenLine.Line,
                previousPrice,
                seenLine.Price,
                previousStatus,
                lifecycle.CurrentStatus,
                new { seenLine.SnapshotId });
            return;
        }

        if (options.Value.RecordSeenUnchangedEvents)
        {
            AddEvent(
                lifecycle,
                refreshRun.Id,
                ProviderLineLifecycleEventTypes.SeenUnchanged,
                now,
                previousLine,
                seenLine.Line,
                previousPrice,
                seenLine.Price,
                previousStatus,
                lifecycle.CurrentStatus,
                new { seenLine.SnapshotId });
        }
    }

    private void UpdateMissingLifecycle(ProviderLineLifecycle lifecycle, TheOddsRefreshRun refreshRun, DateTimeOffset now)
    {
        var previousStatus = lifecycle.CurrentStatus;
        var expireAfter = TimeSpan.FromHours(Math.Max(1, options.Value.ExpireAfterHours));
        var staleAfter = TimeSpan.FromMinutes(Math.Max(1, options.Value.ActiveLineMaxAgeMinutes));
        var pulledAfterMisses = Math.Max(1, options.Value.PulledAfterSuccessfulMissingRefreshes);

        if (lifecycle.EventStartTimeUtc.HasValue && now >= lifecycle.EventStartTimeUtc.Value.Add(expireAfter))
        {
            SetStatusWithEvent(lifecycle, refreshRun.Id, now, ProviderLineLifecycleStatuses.Expired, ProviderLineLifecycleEventTypes.MarkedExpired);
            lifecycle.ExpiredAtUtc ??= now;
            return;
        }

        if (lifecycle.EventStartTimeUtc.HasValue && now >= lifecycle.EventStartTimeUtc.Value)
        {
            SetStatusWithEvent(lifecycle, refreshRun.Id, now, ProviderLineLifecycleStatuses.Locked, ProviderLineLifecycleEventTypes.EventLocked);
            lifecycle.LockedAtUtc ??= now;
            return;
        }

        lifecycle.MissingCount++;
        lifecycle.UpdatedAtUtc = now;
        AddEvent(
            lifecycle,
            refreshRun.Id,
            ProviderLineLifecycleEventTypes.MissingFromRefresh,
            now,
            lifecycle.CurrentLine,
            lifecycle.CurrentLine,
            lifecycle.CurrentPrice,
            lifecycle.CurrentPrice,
            previousStatus,
            lifecycle.CurrentStatus,
            new { lifecycle.MissingCount });

        if (lifecycle.MissingCount >= pulledAfterMisses)
        {
            SetStatusWithEvent(lifecycle, refreshRun.Id, now, ProviderLineLifecycleStatuses.Pulled, ProviderLineLifecycleEventTypes.ProviderPulled);
            lifecycle.PulledAtUtc ??= now;
            return;
        }

        if (now - lifecycle.LastSeenUtc > staleAfter)
        {
            SetStatusWithEvent(lifecycle, refreshRun.Id, now, ProviderLineLifecycleStatuses.Stale, ProviderLineLifecycleEventTypes.MarkedStale);
        }
    }

    private static ProviderLineLifecycle CreateLifecycle(
        TheOddsRefreshRun refreshRun,
        SeenProviderLine seenLine,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            Sport = refreshRun.Sport,
            ProviderKey = seenLine.ProviderKey,
            ProviderDisplayName = seenLine.ProviderDisplayName,
            ProviderCategory = seenLine.ProviderCategory,
            EventId = seenLine.EventId,
            EventStartTimeUtc = seenLine.EventStartTimeUtc,
            SlateDate = refreshRun.SlateDate,
            NormalizedPlayerName = seenLine.NormalizedPlayerName,
            MarketKey = seenLine.MarketKey,
            PropType = seenLine.PropType,
            Side = seenLine.Side,
            CurrentLine = seenLine.Line,
            CurrentPrice = seenLine.Price,
            CurrentStatus = ProviderLineLifecycleStatuses.Active,
            FirstSeenUtc = now,
            LastSeenUtc = now,
            LastRefreshRunId = refreshRun.Id,
            LastSnapshotId = seenLine.SnapshotId,
            SeenCount = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private void SetStatusWithEvent(
        ProviderLineLifecycle lifecycle,
        Guid refreshRunId,
        DateTimeOffset now,
        string newStatus,
        string eventType)
    {
        var previousStatus = lifecycle.CurrentStatus;
        if (string.Equals(previousStatus, newStatus, StringComparison.OrdinalIgnoreCase))
        {
            lifecycle.UpdatedAtUtc = now;
            return;
        }

        lifecycle.CurrentStatus = newStatus;
        lifecycle.UpdatedAtUtc = now;
        AddEvent(
            lifecycle,
            refreshRunId,
            eventType,
            now,
            lifecycle.CurrentLine,
            lifecycle.CurrentLine,
            lifecycle.CurrentPrice,
            lifecycle.CurrentPrice,
            previousStatus,
            newStatus,
            null);
    }

    private static void AddEvent(
        ProviderLineLifecycle lifecycle,
        Guid? refreshRunId,
        string eventType,
        DateTimeOffset occurredAtUtc,
        decimal? previousLine,
        decimal? newLine,
        decimal? previousPrice,
        decimal? newPrice,
        string? previousStatus,
        string? newStatus,
        object? metadata)
    {
        lifecycle.Events.Add(new ProviderLineLifecycleEvent
        {
            Id = Guid.NewGuid(),
            RefreshRunId = refreshRunId,
            OccurredAtUtc = occurredAtUtc,
            EventType = eventType,
            PreviousLine = previousLine,
            NewLine = newLine,
            PreviousPrice = previousPrice,
            NewPrice = newPrice,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            MetadataJson = metadata is null ? null : JsonSerializer.SerializeToDocument(metadata)
        });
    }

    private static SeenProviderLine ToSeenLine(MarketLineShoppingSnapshot snapshot)
    {
        var providerCategory = NormalizeToken(snapshot.EntryProviderCategory);
        if (string.IsNullOrWhiteSpace(providerCategory))
        {
            providerCategory = NormalizeToken(snapshot.ProviderCategory);
        }

        var providerDisplayName = FirstNonEmpty(snapshot.EntryProvider, snapshot.Provider, snapshot.Bookmaker, snapshot.BestBookmaker, "Unknown provider");
        var providerKey = NormalizeProviderKey(providerDisplayName);
        var propType = NormalizeToken(snapshot.PropType);
        var side = NormalizeSide(snapshot.BestSide);
        var eventId = NormalizeToken(snapshot.EventId);
        var playerName = NormalizePlayerName(snapshot.Player);
        var marketKey = NormalizeMarketKey(propType);

        return new SeenProviderLine(
            new LifecycleKey(
                NormalizeToken(snapshot.Sport),
                providerKey,
                providerCategory,
                eventId,
                playerName,
                marketKey,
                side),
            snapshot.Id,
            providerKey,
            providerDisplayName,
            providerCategory,
            snapshot.EventId,
            snapshot.EventStartTime,
            playerName,
            marketKey,
            propType,
            side,
            snapshot.Line,
            snapshot.BestDecimalPrice);
    }

    private static LifecycleKey ToLifecycleKey(ProviderLineLifecycle lifecycle) =>
        new(
            NormalizeToken(lifecycle.Sport),
            NormalizeProviderKey(lifecycle.ProviderKey),
            NormalizeToken(lifecycle.ProviderCategory),
            NormalizeToken(lifecycle.EventId),
            NormalizePlayerName(lifecycle.NormalizedPlayerName),
            NormalizeMarketKey(lifecycle.MarketKey),
            NormalizeSide(lifecycle.Side));

    private static bool NullableDecimalEquals(decimal? left, decimal? right)
    {
        if (!left.HasValue && !right.HasValue)
        {
            return true;
        }

        if (!left.HasValue || !right.HasValue)
        {
            return false;
        }

        return decimal.Round(left.Value, 6) == decimal.Round(right.Value, 6);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string NormalizeProviderKey(string? value) =>
        NormalizeToken(value)
            .Replace(" ", "_", StringComparison.Ordinal)
            .Replace("-", "_", StringComparison.Ordinal);

    private static string NormalizeMarketKey(string? value) => NormalizeToken(value);

    private static string NormalizePlayerName(string? value) => NormalizeToken(value);

    private static string NormalizeSide(string? value) => NormalizeToken(value);

    private static string NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

    private sealed record SeenProviderLine(
        LifecycleKey Key,
        Guid SnapshotId,
        string ProviderKey,
        string ProviderDisplayName,
        string ProviderCategory,
        string? EventId,
        DateTimeOffset? EventStartTimeUtc,
        string NormalizedPlayerName,
        string MarketKey,
        string PropType,
        string Side,
        decimal? Line,
        decimal? Price);

    private sealed record LifecycleKey(
        string Sport,
        string ProviderKey,
        string ProviderCategory,
        string? EventId,
        string NormalizedPlayerName,
        string MarketKey,
        string Side)
    {
        public static IEqualityComparer<LifecycleKey> Comparer { get; } = new LifecycleKeyComparer();

        private sealed class LifecycleKeyComparer : IEqualityComparer<LifecycleKey>
        {
            public bool Equals(LifecycleKey? x, LifecycleKey? y) =>
                x is not null && y is not null &&
                string.Equals(x.Sport, y.Sport, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ProviderKey, y.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ProviderCategory, y.ProviderCategory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.EventId ?? string.Empty, y.EventId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.NormalizedPlayerName, y.NormalizedPlayerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.MarketKey, y.MarketKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Side, y.Side, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode(LifecycleKey obj) =>
                HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sport),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ProviderKey),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ProviderCategory),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.EventId ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.NormalizedPlayerName),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.MarketKey),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Side));
        }
    }
}
