using SportsBetting.Api.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SportsBetting.Api.Data;
using SportsBetting.Api.Integrations.TheOddsApi;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Endpoints;

public interface ITheOddsLiveRefreshGate
{
    Task<IAsyncDisposable> EnterAsync(CancellationToken cancellationToken);
}

public sealed class TheOddsLiveRefreshGate : ITheOddsLiveRefreshGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<IAsyncDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

public interface ITheOddsMlbRefreshService
{
    Task<IResult> RefreshCurrentAsync(
        TheOddsMlbRefreshRequest request,
        HttpContext? httpContext,
        CancellationToken cancellationToken);

    Task<TheOddsScheduledMlbFinalizationResult> FinalizeScheduledCycleAsync(
        TheOddsScheduledMlbFinalizationRequest request,
        CancellationToken cancellationToken);
}

public sealed record TheOddsMlbRefreshRequest(
    DateOnly? Date,
    bool? DryRun,
    bool? ForceLive,
    string? BookmakerPreset,
    bool? IncludeDfs,
    bool? IncludeCandidates,
    bool? IncludeLineShopping,
    bool? IncludeFullDiagnostics,
    int? FreshnessMinutes,
    string? TriggerSource = null,
    Guid? ScheduledCycleId = null,
    string? TargetKey = null,
    DateTimeOffset? TargetLaunchStartedAtUtc = null,
    bool DeferPublicFinalization = false,
    bool BypassLiveRefreshGate = false);

public sealed record TheOddsScheduledMlbFinalizationRequest(
    Guid ScheduledCycleId,
    DateOnly? Date,
    IReadOnlyCollection<string> ProviderCategories,
    IReadOnlyCollection<string> BookmakerKeys,
    IReadOnlyCollection<string> Markets,
    IReadOnlyCollection<Guid> TargetRefreshRunIds,
    bool IncludeCandidates,
    bool IncludeLineShopping,
    bool IncludeFullDiagnostics,
    int? FreshnessMinutes);

public sealed record TheOddsScheduledMlbFinalizationResult(
    bool Succeeded,
    Guid? RefreshRunId,
    int PredictionCount,
    int LineShoppingGroupCount,
    int ActiveCandidateCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? Error);

public static partial class TheOddsApiDebugEndpoints
{
    public sealed class TheOddsMlbRefreshService(
        IOptions<TheOddsIngestionWorkerOptions> workerOptions,
        IOptions<TheOddsApiOptions> theOddsApiOptions,
        ITheOddsApiMlbPlayerPropsAdapter adapter,
        IPredictionGenerationService generationService,
        IPredictionProbabilityService probabilityService,
        ILikelyHitModelService likelyHitModel,
        IOptions<TopPicksModelOptions> topPicksModelOptions,
        IProviderLineLifecycleService providerLineLifecycleService,
        IServiceScopeFactory scopeFactory,
        ApplicationDbContext dbContext,
        IApplicationErrorLogger errorLogger,
        ITheOddsLiveRefreshGate liveRefreshGate) : ITheOddsMlbRefreshService
    {
        public async Task<IResult> RefreshCurrentAsync(
            TheOddsMlbRefreshRequest request,
            HttpContext? httpContext,
            CancellationToken cancellationToken)
        {
            var date = request.Date;
            var dryRun = request.DryRun;
            var includeCandidates = request.IncludeCandidates;
            var includeLineShopping = request.IncludeLineShopping;
            var includeFullDiagnostics = request.IncludeFullDiagnostics;
            var includeDfs = request.IncludeDfs;
            var bookmakerPreset = request.BookmakerPreset;
            var forceLive = request.ForceLive;
            var freshnessMinutes = request.FreshnessMinutes;
            var triggerSource = NullIfWhiteSpace(request.TriggerSource)
                ?? (httpContext is null ? "Internal" : "ManualDebugEndpoint");
            var scheduledCycleId = request.ScheduledCycleId;
            var targetKey = NullIfWhiteSpace(request.TargetKey);
            var deferPublicFinalization = request.DeferPublicFinalization;
            var totalStopwatch = Stopwatch.StartNew();
            var stepStopwatch = Stopwatch.StartNew();
            var isDryRun = dryRun == true;
            var shouldIncludeCandidates = includeCandidates ?? true;
            var shouldIncludeLineShopping = includeLineShopping ?? true;
            var shouldIncludeFullDiagnostics = includeFullDiagnostics ?? true;
            var options = workerOptions.Value;
            var apiOptions = theOddsApiOptions.Value;
            var maxConcurrentRequests = Math.Clamp(apiOptions.MaxConcurrentRequests, 1, 10);
            var slateDate = date ?? ResolveCurrentMlbSlateDate();
            var originallyResolvedSlateDate = slateDate;
            var resolveSlateDateMs = stepStopwatch.ElapsedMilliseconds;
            var lifecycleFreshnessMinutes = Math.Clamp(freshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
            var preset = ResolveTheOddsBookmakerPreset(bookmakerPreset);
            var configuredSportsbookBookmakers = NullIfWhiteSpace(options.Bookmakers);
            var configuredExpandedSportsbookBookmakers = NullIfWhiteSpace(options.ExpandedBookmakers)
                ?? ExpandedTheOddsUsSportsbookBookmakers;
            var configuredDfsBookmakers = NullIfWhiteSpace(options.DfsBookmakers) ?? DefaultTheOddsDfsBookmakers;
            var presetSelection = ResolveTheOddsRefreshBookmakerSelection(
                preset,
                configuredSportsbookBookmakers,
                configuredExpandedSportsbookBookmakers,
                configuredDfsBookmakers,
                includeDfs ?? options.IncludeDfsBookmakers);
            var bookmakers = presetSelection.SportsbookBookmakers;
            var includeDfsBookmakers = presetSelection.IncludeDfs;
            var dfsRegion = NullIfWhiteSpace(options.DfsRegion) ?? "us_dfs";
            var dfsBookmakers = presetSelection.DfsBookmakers;
            var markets = NullIfWhiteSpace(options.Markets);
            var shouldFetchSportsbookBookmakers = !string.IsNullOrWhiteSpace(bookmakers);
            var requestedBookmakers = ParseCommaSeparatedSet(bookmakers);
            var requestedDfsBookmakers = includeDfsBookmakers
                ? ParseCommaSeparatedSet(dfsBookmakers)
                : [];
            var providerCategoriesIncluded = BuildTheOddsProviderCategoryScope(
                shouldFetchSportsbookBookmakers,
                includeDfsBookmakers && !string.IsNullOrWhiteSpace(dfsBookmakers));
            var scopedBookmakerKeys = BuildTheOddsBookmakerKeyScope(bookmakers, includeDfsBookmakers ? dfsBookmakers : null);
            var providerRegionsEnabled = BuildTheOddsProviderRegionsEnabled(
                shouldFetchSportsbookBookmakers,
                includeDfsBookmakers && !string.IsNullOrWhiteSpace(dfsBookmakers),
                dfsRegion);
            var requestedMarkets = ParseCommaSeparatedSet(markets);
            var requestedPropTypes = requestedMarkets
                .Select(NormalizeTheOddsMarketKeyToPropType)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var predictionBookmakers = includeDfsBookmakers
                ? JoinCommaSeparatedDistinct(bookmakers, dfsBookmakers)
                : bookmakers;
            stepStopwatch.Restart();
            var existingSlateProps = await CountPersistedTheOddsPropsForSlateAsync(dbContext, slateDate, cancellationToken);
            var estimatedSlateEvents = await CountPersistedTheOddsEventsForSlateAsync(dbContext, slateDate, cancellationToken);
            var countExistingSlatePropsMs = stepStopwatch.ElapsedMilliseconds;
            var estimatedApiCallShape = new
            {
                events = 1,
                sportsbookEventOddsPerSlateEvent = shouldFetchSportsbookBookmakers ? 1 : 0,
                dfsEventOddsPerSlateEvent = includeDfsBookmakers ? 1 : 0,
                note = "Live estimate is 1 event-list request plus one event-odds request per slate event for each enabled provider region. Slate event count is estimated from persisted The Odds rows when blocking before provider calls."
            };
            var estimatedApiCalls = 1 + (estimatedSlateEvents * ((shouldFetchSportsbookBookmakers ? 1 : 0) + (includeDfsBookmakers ? 1 : 0)));

            if (isDryRun)
            {
                totalStopwatch.Stop();
                return Results.Ok(new
                {
                    success = true,
                    dryRun = true,
                    slateDate,
                    predictionRunId = (Guid?)null,
                    oddsEventsFetched = 0,
                    platformPropsCreated = 0,
                    platformPropsUpdated = 0,
                    platformPropsUpserted = 0,
                    predictionCount = 0,
                    lineShoppingSummary = new
                    {
                        totalComparisonGroups = 0,
                        activeGroups = 0,
                        activeCandidates = 0,
                        activeWatchlist = 0,
                        activeAvoid = 0
                    },
                    strategyClassificationSummary = Array.Empty<object>(),
                    activeCandidateCount = 0,
                    activeWatchlistCount = 0,
                    activeAvoidCount = 0,
                    topActiveCandidates = Array.Empty<object>(),
                    topWatchlist = Array.Empty<object>(),
                    diagnostics = new
                    {
                        wouldCallTheOddsApi = false,
                        wouldWriteRows = false,
                        totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                        resolveSlateDateMs,
                        countExistingSlatePropsMs,
                        theOddsApiCallCount = 0,
                        bookmakerPreset = preset,
                        configuredBookmakers = configuredSportsbookBookmakers,
                        configuredExpandedBookmakers = configuredExpandedSportsbookBookmakers,
                        configuredDfsBookmakers,
                        effectiveSportsbookBookmakers = bookmakers,
                        effectiveDfsBookmakers = dfsBookmakers,
                        includeDfs = includeDfsBookmakers,
                        dfsRegion,
                        providerRegionsEnabled,
                        configuredMarkets = markets,
                        bookmakersRequested = requestedBookmakers,
                        marketsRequested = requestedMarkets,
                        propTypesRequested = requestedPropTypes,
                        sportsbookBookmakersRequested = requestedBookmakers,
                        dfsBookmakersRequested = requestedDfsBookmakers,
                        totalBookmakerCount = requestedBookmakers.Count + requestedDfsBookmakers.Count,
                        providerCategoriesIncluded,
                        maxConcurrentRequests,
                        existingPersistedTheOddsPropsForSlate = existingSlateProps,
                        estimatedSlateEvents,
                        estimatedApiCalls,
                        estimatedApiCallShape,
                        recommendedAction = "Run without dryRun only when you want to spend The Odds API credits and refresh persisted market rows."
                    }
                });
            }

            if (forceLive != true)
            {
                totalStopwatch.Stop();
                return Results.Conflict(new
                {
                    success = false,
                    blocked = true,
                    blockReason = "Live The Odds API refresh requires forceLive=true because it spends provider credits and writes refreshed market rows.",
                    wouldCallTheOddsApi = true,
                    requiresForceLive = true,
                    forceLive = forceLive == true,
                    dryRun = false,
                    bookmakerPreset = preset,
                    providerRegionsEnabled,
                    estimatedApiCalls,
                    estimatedApiCallShape,
                    estimatedSlateEvents,
                    sportsbookBookmakersRequested = requestedBookmakers,
                    dfsBookmakersRequested = requestedDfsBookmakers,
                    diagnostics = new
                    {
                        callsTheOddsApi = false,
                        wouldWriteRows = true,
                        totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                        resolveSlateDateMs,
                        countExistingSlatePropsMs,
                        existingPersistedTheOddsPropsForSlate = existingSlateProps,
                        triggerSource,
                        message = "Request was blocked before any The Odds API calls. Re-run with dryRun=true for planning or forceLive=true for an explicit live refresh."
                    }
                });
            }

            var gateLease = request.BypassLiveRefreshGate
                ? null
                : await liveRefreshGate.EnterAsync(cancellationToken);
            try
            {
                var refreshRun = new TheOddsRefreshRun
                {
                    Id = Guid.NewGuid(),
                    Provider = "The Odds API",
                    Sport = "MLB",
                    SlateDate = slateDate,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    Succeeded = false,
                    Failed = false,
                    Blocked = false,
                    DryRun = false,
                    ForceLive = true,
                    BookmakerPreset = preset,
                    ProviderCategoriesIncluded = string.Join(",", providerCategoriesIncluded),
                    SportsbookBookmakersRequested = string.Join(",", requestedBookmakers),
                    DfsBookmakersRequested = string.Join(",", requestedDfsBookmakers),
                    MarketsRequested = string.Join(",", requestedMarkets),
                    EstimatedApiCalls = estimatedApiCalls
                };
                var targetIngestStartedAtUtc = request.TargetLaunchStartedAtUtc ?? refreshRun.StartedAtUtc;
                dbContext.TheOddsRefreshRuns.Add(refreshRun);
                await dbContext.SaveChangesAsync(cancellationToken);

                try
                {
                stepStopwatch.Restart();
                var beforePlatformPropIds = await LoadTheOddsPlatformPropIdsAsync(dbContext, cancellationToken);
                var loadBeforePlatformPropsMs = stepStopwatch.ElapsedMilliseconds;

                stepStopwatch.Restart();
                var eventFetch = await adapter.FetchSpikeAsync(
                    eventId: null,
                    markets: null,
                    bookmakers: null,
                    regions: null,
                    oddsFormat: null,
                    cancellationToken);
                var fetchEventsMs = stepStopwatch.ElapsedMilliseconds;

                stepStopwatch.Restart();
                var eventDiscoveryObservedAtUtc = DateTimeOffset.UtcNow;
                var discoveredEvents = eventFetch.Events
                    .Where(row => !string.IsNullOrWhiteSpace(row.EventId))
                    .OrderBy(row => row.EventStartTime)
                    .ThenBy(row => row.EventId)
                    .ToArray();
                var originallyResolvedSlateEvents = discoveredEvents
                    .Where(row => ResolveMlbSlateDate(row.EventStartTime) == slateDate)
                    .ToArray();
                var originallyResolvedSlateFutureEvents = originallyResolvedSlateEvents
                    .Count(row => row.EventStartTime.HasValue && row.EventStartTime.Value > eventDiscoveryObservedAtUtc);
                DateOnly? nextUpcomingSlateDate = null;
                var selectedUpcomingSlate = false;
                var noUpcomingProviderEvents = false;
                if (!date.HasValue && originallyResolvedSlateFutureEvents == 0)
                {
                    var nextUpcomingEvent = discoveredEvents
                        .Where(row => row.EventStartTime.HasValue && row.EventStartTime.Value > eventDiscoveryObservedAtUtc)
                        .OrderBy(row => row.EventStartTime)
                        .FirstOrDefault();
                    nextUpcomingSlateDate = ResolveMlbSlateDate(nextUpcomingEvent?.EventStartTime);
                    if (nextUpcomingSlateDate.HasValue && nextUpcomingSlateDate.Value != slateDate)
                    {
                        slateDate = nextUpcomingSlateDate.Value;
                        selectedUpcomingSlate = true;
                        refreshRun.SlateDate = slateDate;
                        existingSlateProps = await CountPersistedTheOddsPropsForSlateAsync(dbContext, slateDate, cancellationToken);
                        estimatedSlateEvents = await CountPersistedTheOddsEventsForSlateAsync(dbContext, slateDate, cancellationToken);
                        refreshRun.EstimatedApiCalls = 1 + (estimatedSlateEvents * ((shouldFetchSportsbookBookmakers ? 1 : 0) + (includeDfsBookmakers ? 1 : 0)));
                    }
                    else if (!nextUpcomingSlateDate.HasValue)
                    {
                        noUpcomingProviderEvents = true;
                    }
                }

                var slateEvents = noUpcomingProviderEvents
                    ? []
                    : discoveredEvents
                        .Where(row => ResolveMlbSlateDate(row.EventStartTime) == slateDate)
                        .OrderBy(row => row.EventStartTime)
                        .ThenBy(row => row.EventId)
                        .ToArray();
                var selectedSlateFutureEvents = slateEvents
                    .Count(row => row.EventStartTime.HasValue && row.EventStartTime.Value > eventDiscoveryObservedAtUtc);
                var selectedSlateEventStartedEvents = slateEvents
                    .Count(row => row.EventStartTime.HasValue && row.EventStartTime.Value <= eventDiscoveryObservedAtUtc);
                var filterEventsMs = stepStopwatch.ElapsedMilliseconds;

                stepStopwatch.Restart();
                var ingestionResults = shouldFetchSportsbookBookmakers
                    ? await IngestTheOddsSlateEventsAsync(
                        slateEvents,
                        bookmakers,
                        markets,
                        region: null,
                        providerCategory: "Sportsbook",
                        isActionableForUser: false,
                        isIndicativeOdds: false,
                        maxConcurrentRequests,
                        scopeFactory,
                        cancellationToken)
                    : [];
                var sportsbookIngestEventsWallClockMs = stepStopwatch.ElapsedMilliseconds;

                stepStopwatch.Restart();
                var dfsIngestionResults = includeDfsBookmakers && !string.IsNullOrWhiteSpace(dfsBookmakers)
                    ? await IngestTheOddsSlateEventsAsync(
                        slateEvents,
                        dfsBookmakers,
                        markets,
                        dfsRegion,
                        providerCategory: "DFS",
                        isActionableForUser: true,
                        isIndicativeOdds: true,
                        maxConcurrentRequests,
                        scopeFactory,
                        cancellationToken)
                    : [];
                var dfsIngestEventsWallClockMs = stepStopwatch.ElapsedMilliseconds;
                var allIngestionResults = ingestionResults.Concat(dfsIngestionResults).ToArray();
                var ingestEventsWallClockMs = sportsbookIngestEventsWallClockMs + dfsIngestEventsWallClockMs;

                stepStopwatch.Restart();
                var afterPlatformPropIds = await LoadTheOddsPlatformPropIdsAsync(dbContext, cancellationToken);
                var platformPropsUpserted = allIngestionResults.Sum(row => row.PlatformPropsUpserted);
                var platformPropsCreated = deferPublicFinalization
                    ? allIngestionResults.Sum(row => row.Timing.RowsCreated)
                    : afterPlatformPropIds.Except(beforePlatformPropIds).Count();
                var platformPropsUpdated = Math.Max(0, platformPropsUpserted - platformPropsCreated);
                var loadAfterPlatformPropsMs = stepStopwatch.ElapsedMilliseconds;

                stepStopwatch.Restart();
                var predictionResult = slateEvents.Length == 0 || deferPublicFinalization
                    ? null
                    : await generationService.GenerateTheOddsMlbBaselinePredictionsAsync(
                        new TheOddsMlbPredictionGenerationRequest(null, predictionBookmakers, markets),
                        cancellationToken);
                var generatePredictionsMs = stepStopwatch.ElapsedMilliseconds;

                var generatedAtUtc = DateTimeOffset.UtcNow;
                var needsLineShopping = predictionResult is not null
                    && !deferPublicFinalization
                    && (shouldIncludeLineShopping || shouldIncludeCandidates);
                stepStopwatch.Restart();
                var lineShopping = !needsLineShopping
                    ? new TheOddsCurrentRecommendationLineShoppingResult([], [], [], Array.Empty<object>())
                    : await BuildCurrentRecommendationLineShoppingResultAsync(
                        predictionResult!.RunId,
                        generatedAtUtc,
                        lifecycleFreshnessMinutes,
                        dbContext,
                        probabilityService,
                        providerCategoriesIncluded,
                        scopedBookmakerKeys,
                        cancellationToken);
                var lineShoppingMs = stepStopwatch.ElapsedMilliseconds;

                stepStopwatch.Restart();
                var activeGroups = lineShopping.Groups
                    .Where(IsActiveTheOddsLineShoppingGroup)
                    .ToArray();
                var classifiedGroups = activeGroups
                    .Select(group => new
                    {
                        Group = group,
                        Strategy = ClassifyTheOddsLineShoppingStrategy(group)
                    })
                    .ToArray();
                var strategyClassificationMs = stepStopwatch.ElapsedMilliseconds;

                stepStopwatch.Restart();
                var activeCandidates = classifiedGroups
                    .Where(row => row.Strategy.IsCandidate)
                    .OrderByDescending(row => row.Group.BestEstimatedEv ?? decimal.MinValue)
                    .ThenByDescending(row => row.Group.RankingScore)
                    .Select(row => row.Group)
                    .ToArray();
                var activeWatchlist = classifiedGroups
                    .Where(row => row.Strategy.IsWatchlist)
                    .OrderByDescending(row => row.Group.BestEstimatedEv ?? decimal.MinValue)
                    .ThenByDescending(row => row.Group.RankingScore)
                    .Select(row => row.Group)
                    .ToArray();
                var activeAvoid = classifiedGroups
                    .Where(row => row.Strategy.IsAvoid)
                    .Select(row => row.Group)
                    .ToArray();
                var topCandidatesMs = stepStopwatch.ElapsedMilliseconds;

                stepStopwatch.Restart();
                var topActiveCandidates = shouldIncludeCandidates
                    ? activeCandidates.Take(25).Select(group => ToLineShoppingResponseGroup(group, includeBookmakerPrices: false)).ToArray()
                    : Array.Empty<object>();
                var topWatchlist = shouldIncludeCandidates
                    ? activeWatchlist.Take(25).Select(group => ToLineShoppingResponseGroup(group, includeBookmakerPrices: false)).ToArray()
                    : Array.Empty<object>();
                var responseBuildMs = stepStopwatch.ElapsedMilliseconds;
                totalStopwatch.Stop();
                var lineShoppingSummary = new
                {
                    totalComparisonGroups = lineShopping.Groups.Count,
                    totalMarketsCompared = lineShopping.Rows.Count,
                    totalBookmakers = lineShopping.Rows.Select(row => row.Bookmaker).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    eligibleComparisonGroups = lineShopping.Groups.Count(row => row.IsEligibleAsNormalRecommendation),
                    actionableCandidateGroups = lineShopping.Groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate && group.IsActionableForUser),
                    sportsbookContextCandidateGroups = lineShopping.Groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate && !group.IsActionableForUser),
                    dfsCandidateGroups = lineShopping.Groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate && string.Equals(group.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
                    providerCategoryBreakdown = BuildTheOddsProviderCategoryBreakdown(lineShopping.Groups),
                    bestBookmakerDistribution = BuildTheOddsBestBookmakerDistribution(lineShopping.Groups),
                    providerCategoriesIncluded,
                    predictionSourceProviderCategories = lineShopping.Rows
                        .Select(row => row.ProviderCategory)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    lineShoppingProviderCategories = lineShopping.Groups
                        .Select(group => group.ProviderCategory)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    sportsbookContextCandidateCount = lineShopping.Groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate && !group.IsActionableForUser),
                    dfsCandidateCount = lineShopping.Groups.Count(group => ClassifyTheOddsLineShoppingStrategy(group).IsCandidate && string.Equals(group.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
                    activeGroups = activeGroups.Length,
                    staleOrExpiredGroups = lineShopping.Groups.Count(row => !IsActiveTheOddsLineShoppingGroup(row))
                };
                var targetIngestCompletedAtUtc = DateTimeOffset.UtcNow;
                var diagnostics = new
                {
                    scheduledCycleId,
                    targetKey,
                    triggerSource,
                    targetIngestMode = deferPublicFinalization ? "Parallel" : "FullRefresh",
                    targetLaunchStartedAtUtc = request.TargetLaunchStartedAtUtc,
                    targetIngestStartedAtUtc,
                    targetIngestCompletedAtUtc,
                    targetIngestDurationSeconds = Math.Round((targetIngestCompletedAtUtc - targetIngestStartedAtUtc).TotalSeconds, 3),
                    publicFinalizationDeferred = deferPublicFinalization,
                    callsTheOddsApi = true,
                    totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                    resolveSlateDateMs,
                    countExistingSlatePropsMs,
                    loadBeforePlatformPropsMs,
                    fetchEventsMs,
                    filterEventsMs,
                    fetchMarketsTotalMs = allIngestionResults.Sum(row => row.Timing.FetchMarketsMs),
                    fetchMarketsWallClockMs = ingestEventsWallClockMs,
                    fetchMarketsByEventMs = allIngestionResults.Select(row => new
                    {
                        row.EventId,
                        row.Success,
                        durationMs = row.Timing.TotalMs,
                        fetchMarketsMs = row.Timing.FetchMarketsMs,
                        persistPlatformPropsMs = row.Timing.PersistPlatformPropsMs,
                        normalizedPropsCount = row.Timing.NormalizedPropsCount,
                        existingRowsMatched = row.Timing.ExistingRowsMatched,
                        rowsCreated = row.Timing.RowsCreated,
                        rowsUpdated = row.Timing.RowsUpdated,
                        rowsUnchanged = row.Timing.RowsUnchanged,
                        rowsSkipped = row.Timing.RowsSkipped,
                        saveChangesMs = row.Timing.SaveChangesMs
                    }),
                    fetchMarketsByBookmakerMs = Array.Empty<object>(),
                    fetchMarketsByBookmakerTimingNote = "The Odds event odds endpoint returns all requested bookmakers for an event in one response, so per-bookmaker network timings are not available.",
                    persistPlatformPropsMs = allIngestionResults.Sum(row => row.Timing.PersistPlatformPropsMs),
                    persistPlatformPropsWallClockMs = ingestEventsWallClockMs,
                    normalizedPropsCount = allIngestionResults.Sum(row => row.Timing.NormalizedPropsCount),
                    existingLookupMs = allIngestionResults.Sum(row => row.Timing.ExistingLookupMs),
                    existingRowsMatched = allIngestionResults.Sum(row => row.Timing.ExistingRowsMatched),
                    rowsUnchanged = allIngestionResults.Sum(row => row.Timing.RowsUnchanged),
                    rowsCreated = allIngestionResults.Sum(row => row.Timing.RowsCreated),
                    rowsUpdated = allIngestionResults.Sum(row => row.Timing.RowsUpdated),
                    rowsSkipped = allIngestionResults.Sum(row => row.Timing.RowsSkipped),
                    saveChangesMs = allIngestionResults.Sum(row => row.Timing.SaveChangesMs),
                    dbRoundTripsEstimate = allIngestionResults.Sum(row => row.Timing.DbRoundTripsEstimate),
                    sportsbookBookmakersRequested = requestedBookmakers,
                    dfsBookmakersRequested = requestedDfsBookmakers,
                    totalBookmakerCount = requestedBookmakers.Count + requestedDfsBookmakers.Count,
                    bookmakerPreset = preset,
                    providerCategoriesIncluded,
                    predictionSourceProviderCategories = lineShopping.Rows
                        .Select(row => row.ProviderCategory)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    lineShoppingProviderCategories = lineShopping.Groups
                        .Select(group => group.ProviderCategory)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    sportsbookContextCandidateCount = activeCandidates.Count(group => !group.IsActionableForUser),
                    dfsCandidateCount = activeCandidates.Count(group => string.Equals(group.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
                    sportsbookEventsFetched = shouldFetchSportsbookBookmakers ? slateEvents.Length : 0,
                    dfsEventsFetched = includeDfsBookmakers ? slateEvents.Length : 0,
                    dfsPlatformPropsCreated = dfsIngestionResults.Sum(row => row.Timing.RowsCreated),
                    dfsPlatformPropsUpdated = dfsIngestionResults.Sum(row => row.Timing.RowsUpdated),
                    dfsPlatformPropsUnchanged = dfsIngestionResults.Sum(row => row.Timing.RowsUnchanged),
                    dfsApiCallCount = includeDfsBookmakers ? slateEvents.Length : 0,
                    dfsFetchMs = dfsIngestionResults.Sum(row => row.Timing.FetchMarketsMs),
                    dfsPersistMs = dfsIngestionResults.Sum(row => row.Timing.PersistPlatformPropsMs),
                    sportsbookFetchMs = ingestionResults.Sum(row => row.Timing.FetchMarketsMs),
                    sportsbookPersistMs = ingestionResults.Sum(row => row.Timing.PersistPlatformPropsMs),
                    sportsbookFetchWallClockMs = sportsbookIngestEventsWallClockMs,
                    dfsFetchWallClockMs = dfsIngestEventsWallClockMs,
                    loadAfterPlatformPropsMs,
                    generatePredictionsMs,
                    lineShoppingMs,
                    strategyClassificationMs,
                    topCandidatesMs,
                    responseBuildMs,
                    theOddsApiCallCount = 1 + (shouldFetchSportsbookBookmakers ? slateEvents.Length : 0) + (includeDfsBookmakers ? slateEvents.Length : 0),
                    theOddsApiCallsByType = new
                    {
                        events = 1,
                        sportsbookEventOdds = shouldFetchSportsbookBookmakers ? slateEvents.Length : 0,
                        dfsEventOdds = includeDfsBookmakers ? slateEvents.Length : 0
                    },
                    apiCallsMade = 1 + (shouldFetchSportsbookBookmakers ? slateEvents.Length : 0) + (includeDfsBookmakers ? slateEvents.Length : 0),
                    actualApiCalls = 1 + (shouldFetchSportsbookBookmakers ? slateEvents.Length : 0) + (includeDfsBookmakers ? slateEvents.Length : 0),
                    maxConcurrentRequests,
                    eventDiscoveryCreditUsage = eventFetch.CreditUsage,
                    eventIngestionCreditUsage = shouldIncludeFullDiagnostics ? allIngestionResults.Select(row => new
                    {
                        row.EventId,
                        row.CreditUsage
                    }).Cast<object>().ToArray() : Array.Empty<object>(),
                    configuredBookmakers = configuredSportsbookBookmakers,
                    configuredExpandedBookmakers = configuredExpandedSportsbookBookmakers,
                    configuredDfsBookmakers,
                    effectiveSportsbookBookmakers = bookmakers,
                    effectiveDfsBookmakers = dfsBookmakers,
                    includeDfs = includeDfsBookmakers,
                    dfsRegion,
                    configuredMarkets = markets,
                    eventsFetched = slateEvents.Length,
                    eventsDiscovered = discoveredEvents.Length,
                    originallyResolvedSlateDate,
                    selectedSlateDate = slateDate,
                    selectedUpcomingSlate,
                    nextUpcomingSlateDate,
                    noUpcomingProviderEvents,
                    originallyResolvedSlateEvents = originallyResolvedSlateEvents.Length,
                    originallyResolvedSlateFutureEvents,
                    selectedSlateFutureEvents,
                    selectedSlateEventStartedEvents,
                    marketsRequested = requestedMarkets,
                    bookmakersRequested = requestedBookmakers,
                    propTypesRequested = requestedPropTypes,
                    platformPropsCreated,
                    platformPropsUpdated,
                    predictionCount = predictionResult?.PredictionsGenerated ?? 0,
                    lineShoppingGroupCount = lineShopping.Groups.Count,
                    providerCategoryBreakdown = BuildTheOddsProviderCategoryBreakdown(lineShopping.Groups),
                    bestBookmakerDistribution = BuildTheOddsBestBookmakerDistribution(lineShopping.Groups),
                    existingPersistedTheOddsPropsForSlate = existingSlateProps,
                    events = shouldIncludeFullDiagnostics ? slateEvents.Select(row => new
                    {
                        row.EventId,
                        row.HomeTeam,
                        row.AwayTeam,
                        row.EventStartTime,
                        slateDate = ResolveMlbSlateDate(row.EventStartTime)
                    }).Cast<object>().ToArray() : Array.Empty<object>(),
                    ingestion = shouldIncludeFullDiagnostics ? allIngestionResults.Select(row => new
                    {
                        row.EventId,
                        row.Success,
                        row.NormalizedRecords,
                        row.PlatformPropsUpserted,
                        row.SnapshotsCreated,
                        row.SnapshotsUnchanged,
                        row.Warnings,
                        row.Errors,
                        timing = new
                        {
                            row.Timing.TotalMs,
                            row.Timing.FetchMarketsMs,
                            row.Timing.NormalizeMs,
                            row.Timing.PersistPlatformPropsMs,
                            row.Timing.NormalizedPropsCount,
                            row.Timing.ExistingLookupMs,
                            row.Timing.ExistingRowsMatched,
                            row.Timing.RowsUnchanged,
                            row.Timing.RowsCreated,
                            row.Timing.RowsUpdated,
                            row.Timing.RowsSkipped,
                            row.Timing.SaveChangesMs,
                            row.Timing.DbRoundTripsEstimate
                        }
                    }).Cast<object>().ToArray() : Array.Empty<object>(),
                    prediction = predictionResult is null ? null : new
                    {
                        predictionResult.RunId,
                        predictionResult.PropsEvaluated,
                        predictionResult.PredictionsGenerated,
                        predictionResult.SkippedProps,
                        predictionResult.RecommendationsSuppressedByThreshold,
                        predictionResult.RecommendationsSuppressedByPropTypeFilter,
                        predictionResult.TimingDiagnostics
                    },
                    includeLineShopping = shouldIncludeLineShopping,
                    includeCandidates = shouldIncludeCandidates,
                    includeFullDiagnostics = shouldIncludeFullDiagnostics,
                    freshnessMinutes = lifecycleFreshnessMinutes,
                    warning = noUpcomingProviderEvents
                        ? "The Odds returned no future MLB events in the provider event list; started events were not ingested as an actionable public board."
                        : activeGroups.Length == 0 && lineShopping.Groups.Count > 0
                            ? "All line-shopping rows are stale, expired, or game-started for the requested freshness window."
                            : null
                };

                refreshRun.CompletedAtUtc = targetIngestCompletedAtUtc;
                refreshRun.Succeeded = true;
                refreshRun.Failed = false;
                refreshRun.Blocked = false;
                refreshRun.ActualApiCalls = diagnostics.actualApiCalls;
                refreshRun.PlatformPropsCreated = platformPropsCreated;
                refreshRun.PlatformPropsUpdated = platformPropsUpdated;
                refreshRun.PlatformPropsUpserted = platformPropsUpserted;
                refreshRun.PredictionCount = predictionResult?.PredictionsGenerated ?? 0;
                refreshRun.LineShoppingGroupCount = lineShopping.Groups.Count;
                refreshRun.ActiveCandidateCount = activeCandidates.Length;
                refreshRun.ActiveWatchlistCount = activeWatchlist.Length;
                refreshRun.ActiveAvoidCount = activeAvoid.Length;
                refreshRun.DiagnosticsJson = JsonSerializer.SerializeToDocument(diagnostics);
                object lifecycleTracking;
                if (deferPublicFinalization)
                {
                    lifecycleTracking = new
                    {
                        skipped = true,
                        reason = "Scheduled target public finalization is deferred until all parallel target ingests complete."
                    };
                    refreshRun.DiagnosticsJson = JsonSerializer.SerializeToDocument(diagnostics);
                }
                else
                {
                    await PersistTheOddsMarketSnapshotsAsync(
                        refreshRun,
                        predictionResult?.RunId,
                        generatedAtUtc,
                        lineShopping.Groups,
                        activeCandidates.Take(25).ToArray(),
                        likelyHitModel,
                        topPicksModelOptions.Value,
                        dbContext,
                        cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    var tracking = await providerLineLifecycleService.TrackRefreshAsync(refreshRun.Id, cancellationToken);
                    lifecycleTracking = tracking;
                    refreshRun.DiagnosticsJson = BuildRefreshDiagnosticsDocument(diagnostics, tracking);
                }
                await dbContext.SaveChangesAsync(cancellationToken);

                return Results.Ok(new
                {
                    success = true,
                    dryRun = false,
                    slateDate,
                    predictionRunId = predictionResult?.RunId,
                    oddsEventsFetched = slateEvents.Length,
                    platformPropsCreated,
                    platformPropsUpdated,
                    platformPropsUpserted,
                    predictionCount = predictionResult?.PredictionsGenerated ?? 0,
                    lineShoppingSummary,
                    strategyClassificationSummary = shouldIncludeLineShopping || shouldIncludeCandidates
                        ? lineShopping.StrategyClassificationSummary
                        : Array.Empty<object>(),
                    activeCandidateCount = activeCandidates.Length,
                    actionableCandidateCount = activeCandidates.Count(group => group.IsActionableForUser),
                    sportsbookContextCandidateCount = activeCandidates.Count(group => !group.IsActionableForUser),
                    dfsCandidateCount = activeCandidates.Count(group => string.Equals(group.ProviderCategory, "DFS", StringComparison.OrdinalIgnoreCase)),
                    activeWatchlistCount = activeWatchlist.Length,
                    activeAvoidCount = activeAvoid.Length,
                    lifecycleTracking,
                    topActiveCandidates,
                    topWatchlist,
                    diagnostics
                });
            }
            catch (TheOddsApiProviderException exception)
            {
                DetachRefreshFailureSideEffects(dbContext, refreshRun);
                refreshRun.CompletedAtUtc = DateTimeOffset.UtcNow;
                refreshRun.Succeeded = false;
                refreshRun.Failed = true;
                refreshRun.Blocked = false;
                refreshRun.ErrorMessage = exception.Message;
                refreshRun.ActualApiCalls = 0;
                refreshRun.DiagnosticsJson = JsonSerializer.SerializeToDocument(new
                {
                    provider = "The Odds API",
                    exception.ErrorCode,
                    providerStatusCode = exception.StatusCodeNumber,
                    exception.ProviderBodyPreview,
                    exception.RequestUrl,
                    exception.RecommendedAction,
                    exception.CreditUsage
                });
                await dbContext.SaveChangesAsync(cancellationToken);

                await LogTheOddsProviderFailureAsync(
                    errorLogger,
                    exception,
                    httpContext,
                    "RefreshCurrentMlbRecommendations",
                    new
                    {
                        slateDate,
                        dryRun = isDryRun,
                        includeCandidates = shouldIncludeCandidates,
                        freshnessMinutes = lifecycleFreshnessMinutes,
                        bookmakers,
                        markets
                    },
                    cancellationToken);

                return Results.Json(
                    BuildTheOddsProviderFailureResponse(exception),
                    statusCode: MapTheOddsProviderFailureStatusCode(exception));
            }
            catch (Exception exception)
            {
                DetachRefreshFailureSideEffects(dbContext, refreshRun);
                refreshRun.CompletedAtUtc = DateTimeOffset.UtcNow;
                refreshRun.Succeeded = false;
                refreshRun.Failed = true;
                refreshRun.Blocked = false;
                refreshRun.ErrorMessage = exception.Message;
                refreshRun.DiagnosticsJson = JsonSerializer.SerializeToDocument(new
                {
                    exceptionType = exception.GetType().FullName,
                    exception.Message
                });
                await dbContext.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            }
            finally
            {
                if (gateLease is not null)
                {
                    await gateLease.DisposeAsync();
                }
            }
        }

        public async Task<TheOddsScheduledMlbFinalizationResult> FinalizeScheduledCycleAsync(
            TheOddsScheduledMlbFinalizationRequest request,
            CancellationToken cancellationToken)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var totalStopwatch = Stopwatch.StartNew();
            var slateDate = request.Date ?? ResolveCurrentMlbSlateDate();
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var lifecycleFreshnessMinutes = Math.Clamp(request.FreshnessMinutes ?? DefaultTheOddsLineShoppingFreshnessMinutes, 1, 1440);
            var providerCategoriesIncluded = request.ProviderCategories
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var scopedBookmakerKeys = request.BookmakerKeys
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var requestedMarkets = request.Markets
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var predictionBookmakers = scopedBookmakerKeys.Length == 0 ? null : string.Join(",", scopedBookmakerKeys);
            var markets = requestedMarkets.Length == 0 ? NullIfWhiteSpace(theOddsApiOptions.Value.Markets) : string.Join(",", requestedMarkets);

            var refreshRun = new TheOddsRefreshRun
            {
                Id = Guid.NewGuid(),
                Provider = "The Odds API",
                Sport = "MLB",
                SlateDate = slateDate,
                StartedAtUtc = startedAtUtc,
                Succeeded = false,
                Failed = false,
                Blocked = false,
                DryRun = false,
                ForceLive = false,
                BookmakerPreset = "scheduledCycleFinalization",
                ProviderCategoriesIncluded = string.Join(",", providerCategoriesIncluded),
                SportsbookBookmakersRequested = string.Join(",", scopedBookmakerKeys),
                DfsBookmakersRequested = string.Join(",", scopedBookmakerKeys),
                MarketsRequested = string.Join(",", requestedMarkets),
                EstimatedApiCalls = 0,
                ActualApiCalls = 0
            };

            dbContext.TheOddsRefreshRuns.Add(refreshRun);
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                var predictionStopwatch = Stopwatch.StartNew();
                var predictionResult = await generationService.GenerateTheOddsMlbBaselinePredictionsAsync(
                    new TheOddsMlbPredictionGenerationRequest(null, predictionBookmakers, markets),
                    cancellationToken);
                predictionStopwatch.Stop();

                var lineShoppingStopwatch = Stopwatch.StartNew();
                var lineShopping = !request.IncludeLineShopping && !request.IncludeCandidates
                    ? new TheOddsCurrentRecommendationLineShoppingResult([], [], [], Array.Empty<object>())
                    : await BuildCurrentRecommendationLineShoppingResultAsync(
                        predictionResult.RunId,
                        generatedAtUtc,
                        lifecycleFreshnessMinutes,
                        dbContext,
                        probabilityService,
                        providerCategoriesIncluded,
                        scopedBookmakerKeys,
                        cancellationToken);
                lineShoppingStopwatch.Stop();

                var activeGroups = lineShopping.Groups
                    .Where(IsActiveTheOddsLineShoppingGroup)
                    .ToArray();
                var classifiedGroups = activeGroups
                    .Select(group => new
                    {
                        Group = group,
                        Strategy = ClassifyTheOddsLineShoppingStrategy(group)
                    })
                    .ToArray();
                var activeCandidates = classifiedGroups
                    .Where(row => row.Strategy.IsCandidate)
                    .OrderByDescending(row => row.Group.BestEstimatedEv ?? decimal.MinValue)
                    .ThenByDescending(row => row.Group.RankingScore)
                    .Select(row => row.Group)
                    .ToArray();
                var activeWatchlist = classifiedGroups
                    .Where(row => row.Strategy.IsWatchlist)
                    .Select(row => row.Group)
                    .ToArray();
                var activeAvoid = classifiedGroups
                    .Where(row => row.Strategy.IsAvoid)
                    .Select(row => row.Group)
                    .ToArray();
                var futureActionableGroups = activeGroups
                    .Where(group => group.EventStartTime.HasValue && group.EventStartTime.Value > generatedAtUtc)
                    .ToArray();
                var replacementFutureEventGroupCount = lineShopping.Groups
                    .Count(group => group.EventStartTime.HasValue && group.EventStartTime.Value > generatedAtUtc);
                var replacementEventStartedGroupCount = lineShopping.Groups
                    .Count(group => group.EventStartTime.HasValue && group.EventStartTime.Value <= generatedAtUtc);
                var replacementStaleOrExpiredGroupCount = lineShopping.Groups.Count(group => !IsActiveTheOddsLineShoppingGroup(group));
                var replacementHasActionableFutureRows = futureActionableGroups.Length > 0;
                var priorUsablePublicBoardExists = await HasPriorUsableMlbPublicSnapshotAsync(
                    dbContext,
                    providerCategoriesIncluded,
                    cancellationToken);
                var shouldPublishPublicSnapshots = replacementHasActionableFutureRows || !priorUsablePublicBoardExists;
                var publicBoardPublicationSkipped = !shouldPublishPublicSnapshots;
                var publicBoardPublicationSkipReason = publicBoardPublicationSkipped
                    ? lineShopping.Groups.Count == 0
                        ? "ZeroReplacementPreservedPriorUsableBoard"
                        : "DeadSlateNoActionableFutureLinesPreservedPriorUsableBoard"
                    : null;

                var persistStopwatch = Stopwatch.StartNew();
                if (shouldPublishPublicSnapshots)
                {
                    await PersistTheOddsMarketSnapshotsAsync(
                        refreshRun,
                        predictionResult.RunId,
                        generatedAtUtc,
                        lineShopping.Groups,
                        activeCandidates.Take(25).ToArray(),
                        likelyHitModel,
                        topPicksModelOptions.Value,
                        dbContext,
                        cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                persistStopwatch.Stop();

                var lifecycleStopwatch = Stopwatch.StartNew();
                object lifecycleTracking = shouldPublishPublicSnapshots
                    ? await providerLineLifecycleService.TrackRefreshAsync(refreshRun.Id, cancellationToken)
                    : new
                    {
                        skipped = true,
                        reason = publicBoardPublicationSkipReason
                    };
                lifecycleStopwatch.Stop();

                var completedAtUtc = DateTimeOffset.UtcNow;
                totalStopwatch.Stop();
                var diagnostics = new
                {
                    scheduledCycleId = request.ScheduledCycleId,
                    triggerSource = "ScheduledWorker",
                    targetIngestMode = "ParallelFinalization",
                    finalizationStartedAtUtc = startedAtUtc,
                    finalizationCompletedAtUtc = completedAtUtc,
                    finalizationDurationSeconds = Math.Round((completedAtUtc - startedAtUtc).TotalSeconds, 3),
                    callsTheOddsApi = false,
                    publicFinalizationDeferred = false,
                    finalizedTargetRefreshRunIds = request.TargetRefreshRunIds,
                    providerCategoriesIncluded,
                    scopedBookmakerKeys,
                    requestedMarkets,
                    predictionMs = predictionStopwatch.ElapsedMilliseconds,
                    lineShoppingMs = lineShoppingStopwatch.ElapsedMilliseconds,
                    persistSnapshotsMs = persistStopwatch.ElapsedMilliseconds,
                    lifecycleTrackingMs = lifecycleStopwatch.ElapsedMilliseconds,
                    totalDurationMs = totalStopwatch.ElapsedMilliseconds,
                    publicBoardPublicationStatus = shouldPublishPublicSnapshots ? "Published" : "Skipped",
                    publicBoardPublicationSkipped,
                    publicBoardPublicationSkipReason,
                    priorUsablePublicBoardExists,
                    replacementSnapshotRows = lineShopping.Groups.Count,
                    replacementActiveGroupCount = activeGroups.Length,
                    replacementActionableFutureGroupCount = futureActionableGroups.Length,
                    replacementFutureEventGroupCount,
                    replacementEventStartedGroupCount,
                    replacementStaleOrExpiredGroupCount,
                    replacementHasActionableFutureRows,
                    predictionRunId = predictionResult.RunId,
                    predictionCount = predictionResult.PredictionsGenerated,
                    lineShoppingGroupCount = lineShopping.Groups.Count,
                    activeCandidateCount = activeCandidates.Length,
                    activeWatchlistCount = activeWatchlist.Length,
                    activeAvoidCount = activeAvoid.Length
                };

                refreshRun.CompletedAtUtc = completedAtUtc;
                refreshRun.Succeeded = true;
                refreshRun.Failed = false;
                refreshRun.Blocked = false;
                refreshRun.PredictionCount = predictionResult.PredictionsGenerated;
                refreshRun.LineShoppingGroupCount = lineShopping.Groups.Count;
                refreshRun.ActiveCandidateCount = activeCandidates.Length;
                refreshRun.ActiveWatchlistCount = activeWatchlist.Length;
                refreshRun.ActiveAvoidCount = activeAvoid.Length;
                refreshRun.DiagnosticsJson = shouldPublishPublicSnapshots && lifecycleTracking is ProviderLineLifecycleTrackingResult tracking
                    ? BuildRefreshDiagnosticsDocument(diagnostics, tracking)
                    : JsonSerializer.SerializeToDocument(diagnostics);
                await dbContext.SaveChangesAsync(cancellationToken);

                return new TheOddsScheduledMlbFinalizationResult(
                    true,
                    refreshRun.Id,
                    predictionResult.PredictionsGenerated,
                    lineShopping.Groups.Count,
                    activeCandidates.Length,
                    startedAtUtc,
                    completedAtUtc,
                    Error: null);
            }
            catch (Exception exception)
            {
                var completedAtUtc = DateTimeOffset.UtcNow;
                refreshRun.CompletedAtUtc = completedAtUtc;
                refreshRun.Succeeded = false;
                refreshRun.Failed = true;
                refreshRun.Blocked = false;
                refreshRun.ErrorMessage = exception.Message;
                refreshRun.DiagnosticsJson = JsonSerializer.SerializeToDocument(new
                {
                    scheduledCycleId = request.ScheduledCycleId,
                    triggerSource = "ScheduledWorker",
                    targetIngestMode = "ParallelFinalization",
                    finalizationStartedAtUtc = startedAtUtc,
                    finalizationCompletedAtUtc = completedAtUtc,
                    finalizationDurationSeconds = Math.Round((completedAtUtc - startedAtUtc).TotalSeconds, 3),
                    callsTheOddsApi = false,
                    exceptionType = exception.GetType().FullName,
                    exception.Message
                });
                await dbContext.SaveChangesAsync(CancellationToken.None);

                return new TheOddsScheduledMlbFinalizationResult(
                    false,
                    refreshRun.Id,
                    PredictionCount: 0,
                    LineShoppingGroupCount: 0,
                    ActiveCandidateCount: 0,
                    startedAtUtc,
                    completedAtUtc,
                    exception.Message);
            }
        }

        private static JsonDocument BuildRefreshDiagnosticsDocument(object diagnostics, ProviderLineLifecycleTrackingResult lifecycleTracking)
        {
            var node = JsonSerializer.SerializeToNode(diagnostics) as JsonObject ?? [];
            node["lifecycleTrackingSucceeded"] = lifecycleTracking.Succeeded;
            node["lifecycleTrackingError"] = lifecycleTracking.Error;
            node["lifecycleRowsInserted"] = lifecycleTracking.LifecycleRowsInserted;
            node["lifecycleRowsUpdated"] = lifecycleTracking.LifecycleRowsUpdated;
            node["lifecycleEventsInserted"] = lifecycleTracking.LifecycleEventsInserted;
            node["lifecycleEventsSkippedExisting"] = lifecycleTracking.LifecycleEventsSkippedExisting;
            node["lifecycleEventModifiedEntriesDetached"] = lifecycleTracking.LifecycleEventModifiedEntriesDetached;
            node["lifecycleSnapshotsRead"] = lifecycleTracking.SnapshotsRead;
            node["lifecycleSeenLines"] = lifecycleTracking.SeenLines;
            node["lifecycleDuplicateKeys"] = lifecycleTracking.DuplicateLifecycleKeys;
            node["lifecycleConcurrencyRetryAttempted"] = lifecycleTracking.ConcurrencyRetryAttempted;
            node["lifecycleConcurrencyRetrySucceeded"] = lifecycleTracking.ConcurrencyRetrySucceeded;
            return JsonSerializer.SerializeToDocument(node);
        }

        private static async Task<bool> HasPriorUsableMlbPublicSnapshotAsync(
            ApplicationDbContext dbContext,
            IReadOnlyCollection<string> providerCategories,
            CancellationToken cancellationToken)
        {
            var query = dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(row => row.Sport == "MLB"
                    && row.LifecycleStatus == "Active"
                    && !row.IsStale
                    && !row.IsExpired);

            var scopedProviderCategories = providerCategories
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (scopedProviderCategories.Length > 0)
            {
                query = query.Where(row => scopedProviderCategories.Contains(row.ProviderCategory));
            }

            return await query.AnyAsync(cancellationToken);
        }

        private static void DetachRefreshFailureSideEffects(ApplicationDbContext dbContext, TheOddsRefreshRun refreshRun)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries()
                .Where(entry =>
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                    !ReferenceEquals(entry.Entity, refreshRun))
                .ToArray())
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
