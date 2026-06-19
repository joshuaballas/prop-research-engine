using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public interface IProviderLineLifecycleService
{
    Task<ProviderLineLifecycleTrackingResult> TrackRefreshAsync(Guid refreshRunId, CancellationToken cancellationToken);
}

public sealed record ProviderLineLifecycleTrackingResult(
    bool Succeeded,
    int SnapshotsRead,
    int SeenLines,
    int LifecycleRowsInserted,
    int LifecycleRowsUpdated,
    int LifecycleEventsInserted,
    int LifecycleEventsSkippedExisting,
    int LifecycleEventModifiedEntriesDetached,
    int DuplicateLifecycleKeys,
    bool ConcurrencyRetryAttempted,
    bool ConcurrencyRetrySucceeded,
    string? Error);

public sealed class ProviderLineLifecycleService(
    ApplicationDbContext dbContext,
    IOptions<ProviderLineLifecycleOptions> options,
    ILogger<ProviderLineLifecycleService> logger) : IProviderLineLifecycleService
{
    public async Task<ProviderLineLifecycleTrackingResult> TrackRefreshAsync(Guid refreshRunId, CancellationToken cancellationToken)
    {
        var retryAttempted = false;
        ProviderLineLifecycleTrackingResult? firstFailure = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return await TrackRefreshAttemptAsync(
                    refreshRunId,
                    retryAttempted,
                    retrySucceeded: retryAttempted,
                    cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                LogLifecycleConcurrencyException(exception, refreshRunId, attempt);
                firstFailure ??= BuildConcurrencyFailure(exception, retryAttempted: false);
                DetachLifecycleEntries();

                if (attempt == 1)
                {
                    retryAttempted = true;
                    continue;
                }

                return firstFailure with
                {
                    ConcurrencyRetryAttempted = true,
                    ConcurrencyRetrySucceeded = false,
                    Error = exception.Message
                };
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Provider line lifecycle tracking failed for refresh {RefreshRunId}.", refreshRunId);
                DetachLifecycleEntries();
                return new ProviderLineLifecycleTrackingResult(
                    Succeeded: false,
                    SnapshotsRead: 0,
                    SeenLines: 0,
                    LifecycleRowsInserted: 0,
                    LifecycleRowsUpdated: 0,
                    LifecycleEventsInserted: 0,
                    LifecycleEventsSkippedExisting: 0,
                    LifecycleEventModifiedEntriesDetached: 0,
                    DuplicateLifecycleKeys: 0,
                    ConcurrencyRetryAttempted: retryAttempted,
                    ConcurrencyRetrySucceeded: false,
                    Error: exception.Message);
            }
        }

        return firstFailure ?? new ProviderLineLifecycleTrackingResult(false, 0, 0, 0, 0, 0, 0, 0, 0, retryAttempted, false, "Lifecycle tracking did not complete.");
    }

    private async Task<ProviderLineLifecycleTrackingResult> TrackRefreshAttemptAsync(
        Guid refreshRunId,
        bool retryAttempted,
        bool retrySucceeded,
        CancellationToken cancellationToken)
    {
        DetachLifecycleEntries();

        var refreshRun = await dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == refreshRunId, cancellationToken);
        if (refreshRun is null || !refreshRun.Succeeded)
        {
            return new ProviderLineLifecycleTrackingResult(true, 0, 0, 0, 0, 0, 0, 0, 0, retryAttempted, retrySucceeded, null);
        }

        var snapshots = await dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.TheOddsRefreshRunId == refreshRunId)
            .OrderBy(row => row.ProviderCategory)
            .ThenBy(row => row.Provider)
            .ThenBy(row => row.Bookmaker)
            .ThenBy(row => row.Player)
            .ThenBy(row => row.PropType)
            .ToListAsync(cancellationToken);

        if (snapshots.Count == 0)
        {
            return new ProviderLineLifecycleTrackingResult(true, 0, 0, 0, 0, 0, 0, 0, 0, retryAttempted, retrySucceeded, null);
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
        var lifecycleGroupsByKey = lifecycleRows.GroupBy(ToLifecycleKey, LifecycleKey.Comparer).ToArray();
        var duplicateLifecycleKeys = lifecycleGroupsByKey.Sum(group => Math.Max(0, group.Count() - 1));
        var lifecycleByKey = lifecycleGroupsByKey.ToDictionary(
            group => group.Key,
            group => group.OrderByDescending(row => row.LastSeenUtc).ThenByDescending(row => row.UpdatedAtUtc).First(),
            LifecycleKey.Comparer);
        var eventContext = new LifecycleEventContext(await LoadExistingEventKeysAsync(refreshRunId, lifecycleRows, cancellationToken));
        var seenKeys = new HashSet<LifecycleKey>(LifecycleKey.Comparer);

        foreach (var seenLine in seenRows)
        {
            seenKeys.Add(seenLine.Key);
            if (!lifecycleByKey.TryGetValue(seenLine.Key, out var lifecycle))
            {
                lifecycle = CreateLifecycle(refreshRun, seenLine, now);
                dbContext.ProviderLineLifecycles.Add(lifecycle);
                AddEvent(
                    eventContext,
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

            if (lifecycle.LastRefreshRunId == refreshRun.Id && lifecycle.LastSnapshotId == seenLine.SnapshotId)
            {
                continue;
            }

            UpdateSeenLifecycle(eventContext, lifecycle, refreshRun, seenLine, now);
        }

        foreach (var lifecycle in lifecycleRows)
        {
            var key = ToLifecycleKey(lifecycle);
            if (seenKeys.Contains(key))
            {
                continue;
            }

            UpdateMissingLifecycle(eventContext, lifecycle, refreshRun, now);
        }

        var lifecycleEventModifiedEntriesDetached = DetachUnsafeLifecycleEventEntries(refreshRunId);
        var lifecycleRowsInserted = dbContext.ChangeTracker.Entries<ProviderLineLifecycle>().Count(entry => entry.State == EntityState.Added);
        var lifecycleRowsUpdated = dbContext.ChangeTracker.Entries<ProviderLineLifecycle>().Count(entry => entry.State == EntityState.Modified);
        var lifecycleEventsInserted = dbContext.ChangeTracker.Entries<ProviderLineLifecycleEvent>().Count(entry => entry.State == EntityState.Added);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ProviderLineLifecycleTrackingResult(
            Succeeded: true,
            SnapshotsRead: snapshots.Count,
            SeenLines: seenRows.Length,
            LifecycleRowsInserted: lifecycleRowsInserted,
            LifecycleRowsUpdated: lifecycleRowsUpdated,
            LifecycleEventsInserted: lifecycleEventsInserted,
            LifecycleEventsSkippedExisting: eventContext.SkippedExistingCount,
            LifecycleEventModifiedEntriesDetached: lifecycleEventModifiedEntriesDetached,
            DuplicateLifecycleKeys: duplicateLifecycleKeys,
            ConcurrencyRetryAttempted: retryAttempted,
            ConcurrencyRetrySucceeded: retrySucceeded,
            Error: null);
    }

    private static ProviderLineLifecycleTrackingResult BuildConcurrencyFailure(DbUpdateConcurrencyException exception, bool retryAttempted) =>
        new(
            Succeeded: false,
            SnapshotsRead: 0,
            SeenLines: 0,
            LifecycleRowsInserted: 0,
            LifecycleRowsUpdated: 0,
            LifecycleEventsInserted: 0,
            LifecycleEventsSkippedExisting: 0,
            LifecycleEventModifiedEntriesDetached: 0,
            DuplicateLifecycleKeys: 0,
            ConcurrencyRetryAttempted: retryAttempted,
            ConcurrencyRetrySucceeded: false,
            Error: exception.Message);

    private void DetachLifecycleEntries()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries()
            .Where(entry => entry.Entity is ProviderLineLifecycle or ProviderLineLifecycleEvent)
            .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<HashSet<LifecycleEventKey>> LoadExistingEventKeysAsync(
        Guid refreshRunId,
        IReadOnlyCollection<ProviderLineLifecycle> lifecycleRows,
        CancellationToken cancellationToken)
    {
        var lifecycleIds = lifecycleRows.Select(row => row.Id).ToArray();
        if (lifecycleIds.Length == 0)
        {
            return new HashSet<LifecycleEventKey>(LifecycleEventKey.Comparer);
        }

        var existingEvents = await dbContext.ProviderLineLifecycleEvents
            .AsNoTracking()
            .Where(row => row.RefreshRunId == refreshRunId && lifecycleIds.Contains(row.ProviderLineLifecycleId))
            .Select(row => new ExistingLifecycleEvent(
                row.ProviderLineLifecycleId,
                row.RefreshRunId,
                row.EventType,
                row.PreviousStatus,
                row.NewStatus,
                row.PreviousLine,
                row.NewLine))
            .ToListAsync(cancellationToken);

        return existingEvents
            .Select(row => new LifecycleEventKey(
                row.ProviderLineLifecycleId,
                row.RefreshRunId,
                row.EventType,
                NormalizeToken(row.PreviousStatus),
                NormalizeToken(row.NewStatus),
                NormalizeEventLine(row.PreviousLine),
                NormalizeEventLine(row.NewLine)))
            .ToHashSet(LifecycleEventKey.Comparer);
    }

    private int DetachUnsafeLifecycleEventEntries(Guid refreshRunId)
    {
        var unsafeEventEntries = dbContext.ChangeTracker.Entries<ProviderLineLifecycleEvent>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .ToArray();

        foreach (var entry in unsafeEventEntries)
        {
            logger.LogWarning(
                "Provider line lifecycle event tracked as {State} before save for refresh {RefreshRunId}; detaching because lifecycle events are append-only. Entry: {Entry}",
                entry.State,
                refreshRunId,
                JsonSerializer.Serialize(BuildConcurrencyEntryLog(entry)));
            entry.State = EntityState.Detached;
        }

        return unsafeEventEntries.Length;
    }

    private void LogLifecycleConcurrencyException(DbUpdateConcurrencyException exception, Guid refreshRunId, int attempt)
    {
        var trackedCounts = dbContext.ChangeTracker.Entries()
            .GroupBy(entry => entry.Metadata.ClrType.Name)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var failingEntries = exception.Entries.Select(BuildConcurrencyEntryLog).ToArray();

        logger.LogWarning(
            exception,
            "Provider line lifecycle concurrency conflict while tracking refresh {RefreshRunId}. Attempt {Attempt}. FailingEntries: {FailingEntries}. TrackedCounts: {TrackedCounts}",
            refreshRunId,
            attempt,
            JsonSerializer.Serialize(failingEntries),
            JsonSerializer.Serialize(trackedCounts));
    }

    private static object BuildConcurrencyEntryLog(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey();
        var primaryKeyValues = primaryKey?.Properties.ToDictionary(
            property => property.Name,
            property => entry.Property(property.Name).CurrentValue?.ToString() ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

        return entry.Entity switch
        {
            ProviderLineLifecycle lifecycle => new
            {
                entityType = entry.Metadata.ClrType.Name,
                state = entry.State.ToString(),
                primaryKey = primaryKeyValues,
                lifecycle.ProviderKey,
                lifecycle.ProviderDisplayName,
                lifecycle.ProviderCategory,
                lifecycle.EventId,
                lifecycle.NormalizedPlayerName,
                lifecycle.PropType,
                lifecycle.MarketKey,
                lifecycle.Side,
                line = lifecycle.CurrentLine,
                lifecycle.LastRefreshRunId,
                lifecycle.LastSnapshotId,
                lifecycle.CurrentStatus
            },
            ProviderLineLifecycleEvent lifecycleEvent => new
            {
                entityType = entry.Metadata.ClrType.Name,
                state = entry.State.ToString(),
                primaryKey = primaryKeyValues,
                lifecycleEvent.ProviderLineLifecycleId,
                lifecycleEvent.RefreshRunId,
                lifecycleEvent.EventType,
                lifecycleEvent.PreviousStatus,
                lifecycleEvent.NewStatus,
                previousLine = lifecycleEvent.PreviousLine,
                newLine = lifecycleEvent.NewLine
            },
            _ => new
            {
                entityType = entry.Metadata.ClrType.Name,
                state = entry.State.ToString(),
                primaryKey = primaryKeyValues
            }
        };
    }

    private void UpdateSeenLifecycle(
        LifecycleEventContext eventContext,
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
                eventContext,
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
                eventContext,
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
                eventContext,
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

    private void UpdateMissingLifecycle(
        LifecycleEventContext eventContext,
        ProviderLineLifecycle lifecycle,
        TheOddsRefreshRun refreshRun,
        DateTimeOffset now)
    {
        var previousStatus = lifecycle.CurrentStatus;
        var expireAfter = TimeSpan.FromHours(Math.Max(1, options.Value.ExpireAfterHours));
        var staleAfter = TimeSpan.FromMinutes(Math.Max(1, options.Value.ActiveLineMaxAgeMinutes));
        var pulledAfterMisses = Math.Max(1, options.Value.PulledAfterSuccessfulMissingRefreshes);

        if (lifecycle.EventStartTimeUtc.HasValue && now >= lifecycle.EventStartTimeUtc.Value.Add(expireAfter))
        {
            SetStatusWithEvent(eventContext, lifecycle, refreshRun.Id, now, ProviderLineLifecycleStatuses.Expired, ProviderLineLifecycleEventTypes.MarkedExpired);
            lifecycle.ExpiredAtUtc ??= now;
            return;
        }

        if (lifecycle.EventStartTimeUtc.HasValue && now >= lifecycle.EventStartTimeUtc.Value)
        {
            SetStatusWithEvent(eventContext, lifecycle, refreshRun.Id, now, ProviderLineLifecycleStatuses.Locked, ProviderLineLifecycleEventTypes.EventLocked);
            lifecycle.LockedAtUtc ??= now;
            return;
        }

        lifecycle.MissingCount++;
        lifecycle.UpdatedAtUtc = now;
        AddEvent(
            eventContext,
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
            SetStatusWithEvent(eventContext, lifecycle, refreshRun.Id, now, ProviderLineLifecycleStatuses.Pulled, ProviderLineLifecycleEventTypes.ProviderPulled);
            lifecycle.PulledAtUtc ??= now;
            return;
        }

        if (now - lifecycle.LastSeenUtc > staleAfter)
        {
            SetStatusWithEvent(eventContext, lifecycle, refreshRun.Id, now, ProviderLineLifecycleStatuses.Stale, ProviderLineLifecycleEventTypes.MarkedStale);
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
        LifecycleEventContext eventContext,
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
            eventContext,
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

    private void AddEvent(
        LifecycleEventContext eventContext,
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
        var eventKey = new LifecycleEventKey(
            lifecycle.Id,
            refreshRunId,
            eventType,
            NormalizeToken(previousStatus),
            NormalizeToken(newStatus),
            NormalizeEventLine(previousLine),
            NormalizeEventLine(newLine));

        if (!eventContext.EventKeys.Add(eventKey))
        {
            eventContext.SkippedExistingCount++;
            return;
        }

        dbContext.ProviderLineLifecycleEvents.Add(new ProviderLineLifecycleEvent
        {
            Id = Guid.NewGuid(),
            ProviderLineLifecycleId = lifecycle.Id,
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

    private static decimal? NormalizeEventLine(decimal? value) =>
        value.HasValue ? decimal.Round(value.Value, 4) : null;

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

    private sealed class LifecycleEventContext(HashSet<LifecycleEventKey> eventKeys)
    {
        public HashSet<LifecycleEventKey> EventKeys { get; } = eventKeys;
        public int SkippedExistingCount { get; set; }
    }

    private sealed record ExistingLifecycleEvent(
        Guid ProviderLineLifecycleId,
        Guid? RefreshRunId,
        string EventType,
        string? PreviousStatus,
        string? NewStatus,
        decimal? PreviousLine,
        decimal? NewLine);

    private sealed record LifecycleEventKey(
        Guid ProviderLineLifecycleId,
        Guid? RefreshRunId,
        string EventType,
        string PreviousStatus,
        string NewStatus,
        decimal? PreviousLine,
        decimal? NewLine)
    {
        public static IEqualityComparer<LifecycleEventKey> Comparer { get; } = new LifecycleEventKeyComparer();

        private sealed class LifecycleEventKeyComparer : IEqualityComparer<LifecycleEventKey>
        {
            public bool Equals(LifecycleEventKey? x, LifecycleEventKey? y) =>
                x is not null && y is not null &&
                x.ProviderLineLifecycleId == y.ProviderLineLifecycleId &&
                x.RefreshRunId == y.RefreshRunId &&
                string.Equals(x.EventType, y.EventType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.PreviousStatus, y.PreviousStatus, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.NewStatus, y.NewStatus, StringComparison.OrdinalIgnoreCase) &&
                x.PreviousLine == y.PreviousLine &&
                x.NewLine == y.NewLine;

            public int GetHashCode(LifecycleEventKey obj) =>
                HashCode.Combine(
                    obj.ProviderLineLifecycleId,
                    obj.RefreshRunId,
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.EventType),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PreviousStatus),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.NewStatus),
                    obj.PreviousLine,
                    obj.NewLine);
        }
    }

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
