using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Endpoints;

public static partial class TheOddsApiDebugEndpoints
{
    private const int LatestMarketLifecycleAuditDefaultExamples = 25;
    private const int LatestMarketLifecycleAuditMaxExamples = 100;

    private static async Task<object> BuildLatestMarketLifecycleAuditAsync(
        string? sport,
        string? providerCategory,
        int? freshnessMinutes,
        int? take,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var effectiveSport = string.IsNullOrWhiteSpace(sport) ? null : sport.Trim().ToUpperInvariant();
        var effectiveProviderCategory = NormalizeLifecycleAuditProviderCategory(providerCategory);
        var effectiveFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
        var sampleTake = Math.Clamp(take ?? LatestMarketLifecycleAuditDefaultExamples, 1, LatestMarketLifecycleAuditMaxExamples);

        var refreshQuery = dbContext.TheOddsRefreshRuns
            .AsNoTracking()
            .Where(row => row.Provider == "The Odds API");
        if (!string.IsNullOrWhiteSpace(effectiveSport))
        {
            refreshQuery = refreshQuery.Where(row => row.Sport == effectiveSport);
        }

        var latestRefresh = await refreshQuery
            .OrderByDescending(row => row.StartedAtUtc)
            .ThenByDescending(row => row.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestSuccessfulRefresh = await refreshQuery
            .Where(row => row.Succeeded && !row.DryRun)
            .OrderByDescending(row => row.CompletedAtUtc ?? row.StartedAtUtc)
            .ThenByDescending(row => row.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var latestLineRunScopes = await FindLatestLifecycleSnapshotRunScopesAsync<MarketLineShoppingSnapshot>(
            dbContext,
            effectiveSport,
            effectiveProviderCategory,
            cancellationToken);
        var latestTopPickRunScopes = await FindLatestLifecycleSnapshotRunScopesAsync<MarketTopPickSnapshot>(
            dbContext,
            effectiveSport,
            effectiveProviderCategory,
            cancellationToken);

        var lineRunIds = latestLineRunScopes.Select(row => row.RefreshRunId).Distinct().ToArray();
        var topPickRunIds = latestTopPickRunScopes.Select(row => row.RefreshRunId).Distinct().ToArray();

        var lineRows = lineRunIds.Length == 0
            ? []
            : await BuildLifecycleSnapshotQuery(dbContext.MarketLineShoppingSnapshots.AsNoTracking(), lineRunIds, effectiveSport, effectiveProviderCategory)
                .OrderBy(row => row.IsExpired || row.IsStale || row.LifecycleStatus != "Active" ? 0 : 1)
                .ThenBy(row => row.EventStartTime ?? DateTimeOffset.MaxValue)
                .ThenBy(row => row.Player)
                .ThenBy(row => row.PropType)
                .ToArrayAsync(cancellationToken);

        var topPickRows = topPickRunIds.Length == 0
            ? []
            : await BuildLifecycleSnapshotQuery(dbContext.MarketTopPickSnapshots.AsNoTracking(), topPickRunIds, effectiveSport, effectiveProviderCategory)
                .OrderBy(row => row.EventStartTime ?? DateTimeOffset.MaxValue)
                .ThenBy(row => row.Player)
                .ThenBy(row => row.PropType)
                .ToArrayAsync(cancellationToken);

        var lineAudits = lineRows
            .Select(row => BuildLifecycleSnapshotAuditRow(row, nowUtc, effectiveFreshnessMinutes))
            .ToArray();
        var topPickAudits = topPickRows
            .Select(row => BuildLifecycleSnapshotAuditRow(row, nowUtc, effectiveFreshnessMinutes))
            .ToArray();
        var excludedRows = lineAudits
            .Where(row => !row.PersistedActive || !row.RecomputedActiveNow)
            .ToArray();

        var latestSnapshotCapturedAtUtc = lineAudits
            .Select(row => (DateTimeOffset?)row.CapturedAtUtc)
            .Concat(topPickAudits.Select(row => (DateTimeOffset?)row.CapturedAtUtc))
            .Where(value => value.HasValue)
            .DefaultIfEmpty()
            .Max();

        return new
        {
            generatedAtUtc = nowUtc,
            callsExternalApis = false,
            readOnly = true,
            filters = new
            {
                sport = effectiveSport,
                providerCategory = effectiveProviderCategory,
                freshnessMinutes = effectiveFreshnessMinutes,
                take = sampleTake
            },
            latestRefreshStartedAtUtc = latestRefresh?.StartedAtUtc,
            latestRefreshCompletedAtUtc = latestRefresh?.CompletedAtUtc,
            latestRefreshRun = ToLifecycleAuditRefreshRun(latestRefresh),
            latestSuccessfulRefreshRun = ToLifecycleAuditRefreshRun(latestSuccessfulRefresh),
            latestSnapshotCapturedAtUtc,
            latestLineShoppingSnapshotRuns = latestLineRunScopes,
            latestTopPickSnapshotRuns = latestTopPickRunScopes,
            totalLatestLineShoppingSnapshots = lineAudits.Length,
            totalLatestTopPickSnapshots = topPickAudits.Length,
            activeGroups = lineAudits.Count(row => row.PersistedActive),
            recomputedActiveGroups = lineAudits.Count(row => row.RecomputedActiveNow),
            staleOrExpiredGroups = lineAudits.Count(row => !row.PersistedActive),
            bySport = BuildLifecycleBreakdown(lineAudits, row => row.Sport),
            byCompetition = BuildLifecycleBreakdown(lineAudits, row => string.IsNullOrWhiteSpace(row.Competition) ? "Unknown" : row.Competition),
            byProviderCategory = BuildLifecycleBreakdown(lineAudits, row => row.ProviderCategory),
            byBookmaker = BuildLifecycleBreakdown(lineAudits, row => string.IsNullOrWhiteSpace(row.Bookmaker) ? "Unknown" : row.Bookmaker),
            byProvider = BuildLifecycleBreakdown(lineAudits, row => string.IsNullOrWhiteSpace(row.Provider) ? "Unknown" : row.Provider),
            byLifecycleStatus = BuildCountBreakdown(lineAudits.Select(row => string.IsNullOrWhiteSpace(row.LifecycleStatus) ? "Unknown" : row.LifecycleStatus)),
            byStaleReason = BuildReasonBreakdown(lineAudits.Where(row => row.IsStale), row => row.PrimaryStaleReason),
            byExpiredReason = BuildReasonBreakdown(lineAudits.Where(row => row.IsExpired), row => row.PrimaryExpiredReason),
            minEventStartUtc = lineAudits.Select(row => row.EventStartUtc).Where(value => value.HasValue).DefaultIfEmpty().Min(),
            maxEventStartUtc = lineAudits.Select(row => row.EventStartUtc).Where(value => value.HasValue).DefaultIfEmpty().Max(),
            minCapturedAtUtc = lineAudits.Select(row => (DateTimeOffset?)row.CapturedAtUtc).DefaultIfEmpty().Min(),
            maxCapturedAtUtc = lineAudits.Select(row => (DateTimeOffset?)row.CapturedAtUtc).DefaultIfEmpty().Max(),
            nowUtc,
            freshnessMinutesApplied = effectiveFreshnessMinutes,
            countEventStarted = lineAudits.Count(row => row.EventStartUtc.HasValue && row.EventStartUtc.Value <= nowUtc),
            countEventFuture = lineAudits.Count(row => row.EventStartUtc.HasValue && row.EventStartUtc.Value > nowUtc),
            countEventStartMissing = lineAudits.Count(row => !row.EventStartUtc.HasValue),
            countCapturedTooOld = lineAudits.Count(row => (nowUtc - row.CapturedAtUtc).TotalMinutes > effectiveFreshnessMinutes),
            countObservedTooOld = lineAudits.Count(row => row.LastObservedAtUtc.HasValue && (nowUtc - row.LastObservedAtUtc.Value).TotalMinutes > effectiveFreshnessMinutes),
            countObservedMissing = lineAudits.Count(row => !row.LastObservedAtUtc.HasValue),
            countLineUnavailable = lineAudits.Count(row => string.Equals(row.Side, "None", StringComparison.OrdinalIgnoreCase)
                || (!row.BestAmericanPrice.HasValue && !row.BestDecimalPrice.HasValue)),
            countProviderInactive = lineAudits.Count(row => !string.Equals(row.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase)),
            countMissingBookmaker = lineAudits.Count(row => string.IsNullOrWhiteSpace(row.Bookmaker) && string.IsNullOrWhiteSpace(row.Provider)),
            countMissingPlayer = lineAudits.Count(row => string.IsNullOrWhiteSpace(row.Player)),
            countMissingProp = lineAudits.Count(row => string.IsNullOrWhiteSpace(row.PropType)),
            countMissingOddsOrPayout = lineAudits.Count(row => !row.BestAmericanPrice.HasValue && !row.BestDecimalPrice.HasValue),
            topPickCandidateSnapshotCount = topPickAudits.Count(row => row.IsCandidate),
            topPickPersistedActiveSnapshotCount = topPickAudits.Count(row => row.PersistedActive),
            likelyDiagnosis = BuildLatestMarketLifecycleDiagnosis(lineAudits, topPickAudits, nowUtc),
            firstExcludedExamples = excludedRows
                .Take(sampleTake)
                .Select(row => new
                {
                    row.Sport,
                    row.Competition,
                    row.ProviderCategory,
                    row.Provider,
                    row.Bookmaker,
                    row.EventId,
                    row.EventStartUtc,
                    row.CapturedAtUtc,
                    row.LastObservedAtUtc,
                    row.ExpiresAtUtc,
                    row.Player,
                    row.PropType,
                    row.Side,
                    row.Line,
                    price = new
                    {
                        row.BestAmericanPrice,
                        row.BestDecimalPrice
                    },
                    row.ModelEra,
                    row.LifecycleStatus,
                    row.IsStale,
                    row.IsExpired,
                    persistedActive = row.PersistedActive,
                    recomputedActiveNow = row.RecomputedActiveNow,
                    exclusionReasons = row.ExclusionReasons
                })
                .ToArray()
        };
    }

    private static IQueryable<TSnapshot> BuildLifecycleSnapshotQuery<TSnapshot>(
        IQueryable<TSnapshot> query,
        IReadOnlyCollection<Guid> refreshRunIds,
        string? sport,
        string? providerCategory)
        where TSnapshot : class
    {
        if (typeof(TSnapshot) == typeof(MarketLineShoppingSnapshot))
        {
            var typed = query.Cast<MarketLineShoppingSnapshot>()
                .Where(row => refreshRunIds.Contains(row.TheOddsRefreshRunId));
            if (!string.IsNullOrWhiteSpace(sport))
            {
                typed = typed.Where(row => row.Sport == sport);
            }

            if (!string.IsNullOrWhiteSpace(providerCategory))
            {
                typed = typed.Where(row => row.ProviderCategory == providerCategory);
            }

            return typed.Cast<TSnapshot>();
        }

        var topPickQuery = query.Cast<MarketTopPickSnapshot>()
            .Where(row => refreshRunIds.Contains(row.TheOddsRefreshRunId));
        if (!string.IsNullOrWhiteSpace(sport))
        {
            topPickQuery = topPickQuery.Where(row => row.Sport == sport);
        }

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            topPickQuery = topPickQuery.Where(row => row.ProviderCategory == providerCategory);
        }

        return topPickQuery.Cast<TSnapshot>();
    }

    private static async Task<LatestLifecycleSnapshotRunScope[]> FindLatestLifecycleSnapshotRunScopesAsync<TSnapshot>(
        ApplicationDbContext dbContext,
        string? sport,
        string? providerCategory,
        CancellationToken cancellationToken)
        where TSnapshot : class
    {
        if (typeof(TSnapshot) == typeof(MarketLineShoppingSnapshot))
        {
            var query = dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Join(
                    dbContext.TheOddsRefreshRuns.AsNoTracking().Where(row => row.Provider == "The Odds API" && row.Succeeded && !row.DryRun),
                    snapshot => snapshot.TheOddsRefreshRunId,
                    refreshRun => refreshRun.Id,
                    (snapshot, refreshRun) => new { snapshot, refreshRun });

            if (!string.IsNullOrWhiteSpace(sport))
            {
                query = query.Where(row => row.snapshot.Sport == sport);
            }

            if (!string.IsNullOrWhiteSpace(providerCategory))
            {
                query = query.Where(row => row.snapshot.ProviderCategory == providerCategory);
            }

            var runScopes = await query
                .GroupBy(row => new
                {
                    row.snapshot.Sport,
                    row.snapshot.ProviderCategory,
                    row.refreshRun.Id,
                    CompletedAtUtc = row.refreshRun.CompletedAtUtc ?? row.refreshRun.StartedAtUtc,
                    row.refreshRun.StartedAtUtc
                })
                .Select(group => new LatestLifecycleSnapshotRunScope(
                    group.Key.Sport,
                    group.Key.ProviderCategory,
                    group.Key.Id,
                    group.Key.StartedAtUtc,
                    group.Key.CompletedAtUtc,
                    group.Max(row => row.snapshot.GeneratedAtUtc),
                    group.Count()))
                .ToArrayAsync(cancellationToken);

            return SelectLatestLifecycleSnapshotRunScopes(runScopes, providerCategory);
        }

        var topPickQuery = dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Join(
                dbContext.TheOddsRefreshRuns.AsNoTracking().Where(row => row.Provider == "The Odds API" && row.Succeeded && !row.DryRun),
                snapshot => snapshot.TheOddsRefreshRunId,
                refreshRun => refreshRun.Id,
                (snapshot, refreshRun) => new { snapshot, refreshRun });

        if (!string.IsNullOrWhiteSpace(sport))
        {
            topPickQuery = topPickQuery.Where(row => row.snapshot.Sport == sport);
        }

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            topPickQuery = topPickQuery.Where(row => row.snapshot.ProviderCategory == providerCategory);
        }

        var topPickRunScopes = await topPickQuery
            .GroupBy(row => new
            {
                row.snapshot.Sport,
                row.snapshot.ProviderCategory,
                row.refreshRun.Id,
                CompletedAtUtc = row.refreshRun.CompletedAtUtc ?? row.refreshRun.StartedAtUtc,
                row.refreshRun.StartedAtUtc
            })
            .Select(group => new LatestLifecycleSnapshotRunScope(
                group.Key.Sport,
                group.Key.ProviderCategory,
                group.Key.Id,
                group.Key.StartedAtUtc,
                group.Key.CompletedAtUtc,
                group.Max(row => row.snapshot.GeneratedAtUtc),
                group.Count()))
            .ToArrayAsync(cancellationToken);

        return SelectLatestLifecycleSnapshotRunScopes(topPickRunScopes, providerCategory);
    }

    private static LatestLifecycleSnapshotRunScope[] SelectLatestLifecycleSnapshotRunScopes(
        IReadOnlyCollection<LatestLifecycleSnapshotRunScope> runScopes,
        string? providerCategory)
    {
        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            return runScopes
                .OrderByDescending(row => row.CompletedAtUtc)
                .ThenByDescending(row => row.LatestSnapshotCapturedAtUtc)
                .Take(1)
                .ToArray();
        }

        return runScopes
            .GroupBy(row => new { row.Sport, row.ProviderCategory })
            .Select(group => group
                .OrderByDescending(row => row.CompletedAtUtc)
                .ThenByDescending(row => row.LatestSnapshotCapturedAtUtc)
                .First())
            .OrderBy(row => row.Sport)
            .ThenBy(row => row.ProviderCategory)
            .ToArray();
    }

    private static LatestLifecycleSnapshotAuditRow BuildLifecycleSnapshotAuditRow(
        MarketLineShoppingSnapshot row,
        DateTimeOffset nowUtc,
        int freshnessMinutes)
    {
        return BuildLifecycleSnapshotAuditRow(
            row.Id,
            row.Sport,
            row.ProviderCategory,
            row.Provider,
            row.Bookmaker,
            row.Player,
            row.EventId,
            row.EventName,
            row.EventStartTime,
            row.GeneratedAtUtc,
            row.LifecycleStatus,
            row.IsStale,
            row.IsExpired,
            row.PropType,
            row.Line,
            row.BestSide,
            row.BestAmericanPrice,
            row.BestDecimalPrice,
            row.IsCandidate,
            row.ModelEra,
            row.DetailsJson,
            nowUtc,
            freshnessMinutes);
    }

    private static LatestLifecycleSnapshotAuditRow BuildLifecycleSnapshotAuditRow(
        MarketTopPickSnapshot row,
        DateTimeOffset nowUtc,
        int freshnessMinutes)
    {
        return BuildLifecycleSnapshotAuditRow(
            row.Id,
            row.Sport,
            row.ProviderCategory,
            row.Provider,
            row.Bookmaker,
            row.Player,
            row.EventId,
            row.EventName,
            row.EventStartTime,
            row.GeneratedAtUtc,
            row.LifecycleStatus,
            row.IsStale,
            row.IsExpired,
            row.PropType,
            row.Line,
            row.BestSide,
            row.BestAmericanPrice,
            row.BestDecimalPrice,
            row.IsCandidate,
            row.ModelEra,
            row.DetailsJson,
            nowUtc,
            freshnessMinutes);
    }

    private static LatestLifecycleSnapshotAuditRow BuildLifecycleSnapshotAuditRow(
        Guid snapshotId,
        string sport,
        string providerCategory,
        string? provider,
        string? bookmaker,
        string player,
        string? eventId,
        string? eventName,
        DateTimeOffset? eventStartUtc,
        DateTimeOffset capturedAtUtc,
        string lifecycleStatus,
        bool isStale,
        bool isExpired,
        string propType,
        decimal line,
        string side,
        int? bestAmericanPrice,
        decimal? bestDecimalPrice,
        bool isCandidate,
        string? modelEra,
        JsonDocument detailsJson,
        DateTimeOffset nowUtc,
        int freshnessMinutes)
    {
        var root = detailsJson.RootElement;
        var competition = TryGetLifecycleString(root, "competition");
        var lastObservedAt = TryGetLifecycleDateTimeOffset(root, "lastObservedAt");
        var expiresAt = TryGetLifecycleDateTimeOffset(root, "expiresAtUtc")
            ?? TryGetLifecycleDateTimeOffset(root, "expiresAt");
        var lifecycleReasons = TryGetLifecycleStringArray(root, "lifecycleReasons");
        var effectiveObservedAt = lastObservedAt ?? capturedAtUtc;
        var recomputedStale = (nowUtc - effectiveObservedAt).TotalMinutes > freshnessMinutes;
        var recomputedExpired = eventStartUtc.HasValue && eventStartUtc.Value <= nowUtc;
        var recomputedActive = !recomputedStale && !recomputedExpired && eventStartUtc.HasValue;
        var persistedActive = string.Equals(lifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase)
            && !isStale
            && !isExpired;
        var exclusionReasons = BuildLifecycleExclusionReasons(
            lifecycleStatus,
            isStale,
            isExpired,
            eventStartUtc,
            lastObservedAt,
            capturedAtUtc,
            nowUtc,
            freshnessMinutes,
            bestAmericanPrice,
            bestDecimalPrice,
            side,
            player,
            propType,
            bookmaker,
            provider,
            lifecycleReasons);

        return new LatestLifecycleSnapshotAuditRow(
            snapshotId,
            sport,
            competition,
            providerCategory,
            provider,
            bookmaker,
            player,
            eventId,
            eventName,
            eventStartUtc,
            capturedAtUtc,
            lastObservedAt,
            expiresAt,
            lifecycleStatus,
            isStale,
            isExpired,
            persistedActive,
            recomputedActive,
            propType,
            line,
            side,
            bestAmericanPrice,
            bestDecimalPrice,
            isCandidate,
            modelEra,
            ResolvePrimaryStaleReason(isStale, lastObservedAt, capturedAtUtc, nowUtc, freshnessMinutes, lifecycleReasons),
            ResolvePrimaryExpiredReason(isExpired, eventStartUtc, nowUtc, lifecycleReasons),
            exclusionReasons);
    }

    private static string[] BuildLifecycleExclusionReasons(
        string lifecycleStatus,
        bool isStale,
        bool isExpired,
        DateTimeOffset? eventStartUtc,
        DateTimeOffset? lastObservedAtUtc,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset nowUtc,
        int freshnessMinutes,
        int? bestAmericanPrice,
        decimal? bestDecimalPrice,
        string? side,
        string? player,
        string? propType,
        string? bookmaker,
        string? provider,
        IReadOnlyCollection<string> persistedReasons)
    {
        var reasons = new List<string>();
        if (!string.Equals(lifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"LifecycleStatus:{(string.IsNullOrWhiteSpace(lifecycleStatus) ? "Unknown" : lifecycleStatus)}");
        }

        if (isStale)
        {
            reasons.Add("PersistedStale");
        }

        if (isExpired)
        {
            reasons.Add("PersistedExpired");
        }

        if (!eventStartUtc.HasValue)
        {
            reasons.Add("EventStartMissing");
        }
        else if (eventStartUtc.Value <= nowUtc)
        {
            reasons.Add("EventStarted");
        }

        if (lastObservedAtUtc.HasValue && (nowUtc - lastObservedAtUtc.Value).TotalMinutes > freshnessMinutes)
        {
            reasons.Add("LastObservedTooOld");
        }

        if ((nowUtc - capturedAtUtc).TotalMinutes > freshnessMinutes)
        {
            reasons.Add("SnapshotCapturedTooOld");
        }

        if (string.IsNullOrWhiteSpace(bookmaker) && string.IsNullOrWhiteSpace(provider))
        {
            reasons.Add("MissingBookmakerOrProvider");
        }

        if (string.IsNullOrWhiteSpace(player))
        {
            reasons.Add("MissingPlayer");
        }

        if (string.IsNullOrWhiteSpace(propType))
        {
            reasons.Add("MissingProp");
        }

        if (!bestAmericanPrice.HasValue && !bestDecimalPrice.HasValue)
        {
            reasons.Add("MissingOddsOrPayout");
        }

        if (string.Equals(side, "None", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("NoSelectedSide");
        }

        reasons.AddRange(persistedReasons.Select(reason => $"PersistedReason:{reason}"));
        return reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolvePrimaryStaleReason(
        bool isStale,
        DateTimeOffset? lastObservedAtUtc,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset nowUtc,
        int freshnessMinutes,
        IReadOnlyCollection<string> persistedReasons)
    {
        if (!isStale)
        {
            return "NotStale";
        }

        if (lastObservedAtUtc.HasValue && (nowUtc - lastObservedAtUtc.Value).TotalMinutes > freshnessMinutes)
        {
            return "LastObservedTooOld";
        }

        if ((nowUtc - capturedAtUtc).TotalMinutes > freshnessMinutes)
        {
            return "SnapshotCapturedTooOld";
        }

        return persistedReasons.FirstOrDefault(reason => reason.Contains("observed", StringComparison.OrdinalIgnoreCase)) ?? "PersistedStale";
    }

    private static string ResolvePrimaryExpiredReason(
        bool isExpired,
        DateTimeOffset? eventStartUtc,
        DateTimeOffset nowUtc,
        IReadOnlyCollection<string> persistedReasons)
    {
        if (!isExpired)
        {
            return "NotExpired";
        }

        if (eventStartUtc.HasValue && eventStartUtc.Value <= nowUtc)
        {
            return "EventStarted";
        }

        return persistedReasons.FirstOrDefault(reason => reason.Contains("started", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("expired", StringComparison.OrdinalIgnoreCase)) ?? "PersistedExpired";
    }

    private static string BuildLatestMarketLifecycleDiagnosis(
        IReadOnlyCollection<LatestLifecycleSnapshotAuditRow> lineRows,
        IReadOnlyCollection<LatestLifecycleSnapshotAuditRow> topPickRows,
        DateTimeOffset nowUtc)
    {
        if (lineRows.Count == 0)
        {
            return "No latest line-shopping snapshots matched the requested filters.";
        }

        if (lineRows.All(row => row.EventStartUtc.HasValue && row.EventStartUtc.Value <= nowUtc))
        {
            return "Expected dead-slate behavior: every latest market snapshot has an eventStartUtc at or before nowUtc.";
        }

        if (lineRows.Any(row => row.EventStartUtc.HasValue && row.EventStartUtc.Value > nowUtc) &&
            lineRows.All(row => !row.PersistedActive))
        {
            return "Bug likely: future-event rows exist but all latest snapshots are persisted as stale, expired, or non-active.";
        }

        if (lineRows.All(row => !row.EventStartUtc.HasValue))
        {
            return "Bug likely: every latest snapshot is missing eventStartUtc metadata.";
        }

        if (lineRows.Any(row => row.PersistedActive))
        {
            return topPickRows.Count == 0
                ? "Latest line-shopping inventory has active rows, but no persisted top-pick snapshots matched the requested filters."
                : "Latest market inventory has active rows.";
        }

        return "Latest snapshots are not active for mixed reasons; inspect firstExcludedExamples and breakdowns.";
    }

    private static object? ToLifecycleAuditRefreshRun(TheOddsRefreshRun? run)
    {
        return run is null
            ? null
            : new
            {
                run.Id,
                run.Sport,
                run.SlateDate,
                run.StartedAtUtc,
                run.CompletedAtUtc,
                run.Succeeded,
                run.Failed,
                run.Blocked,
                run.DryRun,
                run.ForceLive,
                run.BookmakerPreset,
                run.ProviderCategoriesIncluded,
                run.SportsbookBookmakersRequested,
                run.DfsBookmakersRequested,
                run.MarketsRequested,
                run.ActualApiCalls,
                run.PlatformPropsCreated,
                run.PlatformPropsUpdated,
                run.PlatformPropsUpserted,
                run.PredictionCount,
                run.LineShoppingGroupCount,
                run.ActiveCandidateCount,
                run.ActiveWatchlistCount,
                run.ActiveAvoidCount,
                run.ErrorMessage
            };
    }

    private static string? NormalizeLifecycleAuditProviderCategory(string? providerCategory)
    {
        if (string.IsNullOrWhiteSpace(providerCategory)
            || string.Equals(providerCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(providerCategory, "DFS", StringComparison.OrdinalIgnoreCase))
        {
            return "DFS";
        }

        if (string.Equals(providerCategory, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return "Sportsbook";
        }

        return providerCategory.Trim();
    }

    private static object[] BuildLifecycleBreakdown(
        IReadOnlyCollection<LatestLifecycleSnapshotAuditRow> rows,
        Func<LatestLifecycleSnapshotAuditRow, string?> keySelector)
    {
        return rows
            .GroupBy(row => string.IsNullOrWhiteSpace(keySelector(row)) ? "Unknown" : keySelector(row)!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                key = group.Key,
                count = group.Count(),
                active = group.Count(row => row.PersistedActive),
                recomputedActiveNow = group.Count(row => row.RecomputedActiveNow),
                stale = group.Count(row => row.IsStale),
                expired = group.Count(row => row.IsExpired),
                eventStarted = group.Count(row => row.EventStartUtc.HasValue && row.EventStartUtc.Value <= DateTimeOffset.UtcNow),
                eventFuture = group.Count(row => row.EventStartUtc.HasValue && row.EventStartUtc.Value > DateTimeOffset.UtcNow)
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildCountBreakdown(IEnumerable<string> keys)
    {
        return keys
            .GroupBy(key => string.IsNullOrWhiteSpace(key) ? "Unknown" : key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { key = group.Key, count = group.Count() })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildReasonBreakdown(
        IEnumerable<LatestLifecycleSnapshotAuditRow> rows,
        Func<LatestLifecycleSnapshotAuditRow, string> reasonSelector)
    {
        return BuildCountBreakdown(rows.Select(reasonSelector));
    }

    private static string? TryGetLifecycleString(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static DateTimeOffset? TryGetLifecycleDateTimeOffset(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string[] TryGetLifecycleStringArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private sealed record LatestLifecycleSnapshotRunScope(
        string Sport,
        string ProviderCategory,
        Guid RefreshRunId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        DateTimeOffset LatestSnapshotCapturedAtUtc,
        int SnapshotCount);

    private sealed record LatestLifecycleSnapshotAuditRow(
        Guid SnapshotId,
        string Sport,
        string? Competition,
        string ProviderCategory,
        string? Provider,
        string? Bookmaker,
        string Player,
        string? EventId,
        string? EventName,
        DateTimeOffset? EventStartUtc,
        DateTimeOffset CapturedAtUtc,
        DateTimeOffset? LastObservedAtUtc,
        DateTimeOffset? ExpiresAtUtc,
        string LifecycleStatus,
        bool IsStale,
        bool IsExpired,
        bool PersistedActive,
        bool RecomputedActiveNow,
        string PropType,
        decimal Line,
        string Side,
        int? BestAmericanPrice,
        decimal? BestDecimalPrice,
        bool IsCandidate,
        string? ModelEra,
        string PrimaryStaleReason,
        string PrimaryExpiredReason,
        IReadOnlyList<string> ExclusionReasons);
}
