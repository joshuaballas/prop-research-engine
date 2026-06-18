using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;
using System.Text.Json;

namespace SportsBetting.Api.Endpoints;

public static class RecommendationLedgerDebugEndpoints
{
    public static IEndpointRouteBuilder MapRecommendationLedgerDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/recommendations/ledger/audit", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var from = fromDate ?? DateOnly.MinValue;
            var to = toDate ?? DateOnly.MaxValue;
            var refreshRuns = await dbContext.TheOddsRefreshRuns
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .GroupBy(row => row.SlateDate)
                .Select(group => new
                {
                    date = group.Key,
                    totalRuns = group.Count(),
                    successfulRuns = group.Count(row => row.Succeeded),
                    predictionCount = group.Sum(row => row.PredictionCount),
                    lineShoppingGroupCount = group.Sum(row => row.LineShoppingGroupCount),
                    topPickSnapshotCount = group.Sum(row => row.TopPickSnapshots.Count),
                    activeCandidateCount = group.Sum(row => row.ActiveCandidateCount),
                    platformPropsCreated = group.Sum(row => row.PlatformPropsCreated),
                    platformPropsUpdated = group.Sum(row => row.PlatformPropsUpdated),
                    platformPropsUpserted = group.Sum(row => row.PlatformPropsUpserted),
                    earliestStartedAtUtc = group.Min(row => row.StartedAtUtc),
                    latestCompletedAtUtc = group.Max(row => row.CompletedAtUtc)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var predictionRuns = await dbContext.PredictionRuns
                .AsNoTracking()
                .Where(row => DateOnly.FromDateTime(row.CreatedAt.UtcDateTime) >= from && DateOnly.FromDateTime(row.CreatedAt.UtcDateTime) <= to)
                .GroupBy(row => DateOnly.FromDateTime(row.CreatedAt.UtcDateTime))
                .Select(group => new
                {
                    date = group.Key,
                    predictionRunCount = group.Count(),
                    predictionCount = group.Sum(row => row.Predictions.Count)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var topPickSnapshots = await dbContext.MarketTopPickSnapshots
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .GroupBy(row => row.SlateDate)
                .Select(group => new
                {
                    date = group.Key,
                    rows = group.Count(),
                    distinctRefreshRuns = group.Select(row => row.TheOddsRefreshRunId).Distinct().Count(),
                    candidateRows = group.Count(row => row.IsCandidate),
                    actionableRows = group.Count(row => row.EntryActionableForUser),
                    earliestGeneratedAtUtc = group.Min(row => row.GeneratedAtUtc),
                    latestGeneratedAtUtc = group.Max(row => row.GeneratedAtUtc)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var lineShoppingSnapshots = await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                .GroupBy(row => row.SlateDate)
                .Select(group => new
                {
                    date = group.Key,
                    rows = group.Count(),
                    distinctRefreshRuns = group.Select(row => row.TheOddsRefreshRunId).Distinct().Count(),
                    candidateRows = group.Count(row => row.IsCandidate),
                    watchlistRows = group.Count(row => row.IsWatchlist),
                    activeRows = group.Count(row => row.LifecycleStatus == "Active"),
                    actionableRows = group.Count(row => row.EntryActionableForUser),
                    earliestGeneratedAtUtc = group.Min(row => row.GeneratedAtUtc),
                    latestGeneratedAtUtc = group.Max(row => row.GeneratedAtUtc)
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var ledger = await dbContext.RecommendationSnapshots
                .AsNoTracking()
                .Where(row => row.ExpectedResultDate >= from && row.ExpectedResultDate <= to)
                .GroupBy(row => row.ExpectedResultDate)
                .Select(group => new
                {
                    date = group.Key,
                    rows = group.Count(),
                    candidateRows = group.Count(row => row.IsCandidate),
                    topPickRows = group.Count(row => row.IsTopPick),
                    actionableRows = group.Count(row => row.IsActionableForUser),
                    gradedRows = group.Count(row => row.GradeStatus == "Won" || row.GradeStatus == "Lost" || row.GradeStatus == "Pushed"),
                    unmatchedRows = group.Count(row => row.GradeStatus == "Unmatched")
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);
            var propResults = await dbContext.PropResults
                .AsNoTracking()
                .Where(row => row.EventDate >= from && row.EventDate <= to)
                .GroupBy(row => row.EventDate)
                .Select(group => new
                {
                    date = group.Key,
                    rows = group.Count(),
                    distinctPlayers = group.Select(row => row.PlayerId).Distinct().Count(),
                    distinctPropTypes = group.Select(row => row.PropTypeId).Distinct().Count()
                })
                .OrderBy(row => row.date)
                .ToArrayAsync(cancellationToken);

            return Results.Ok(new
            {
                fromDate = fromDate,
                toDate = toDate,
                callsExternalApis = false,
                refreshRuns,
                predictionRuns,
                topPickSnapshots,
                lineShoppingSnapshots,
                recommendationLedger = ledger,
                propResults,
                notes = new[]
                {
                    "market_*_snapshots are read snapshots keyed by refresh run and are replaced per run before insert.",
                    "recommendation_snapshots is the durable gradeable ledger used by Performance Summary when populated."
                }
            });
        });

        endpoints.MapPost("/api/debug/recommendations/ledger/backfill", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? source,
            bool? dryRun,
            bool? execute,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var from = fromDate ?? DateOnly.MinValue;
            var to = toDate ?? DateOnly.MaxValue;
            var shouldExecute = execute == true && dryRun != true;
            var candidates = new List<RecommendationLedgerCandidate>();
            var requestedSource = string.IsNullOrWhiteSpace(source) ? "marketSnapshots" : source.Trim();
            var resultPropTypes = await dbContext.PropResults
                .AsNoTracking()
                .Where(row => row.EventDate >= from && row.EventDate <= to && row.PropType != null)
                .Select(row => new { row.PropTypeId, row.PropType!.Name, row.PropType.StatKey })
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var resultPropTypeIdsByName = resultPropTypes
                .SelectMany(row => new[]
                {
                    new { Key = RecommendationLedgerWriter.NormalizeKey(row.Name), row.PropTypeId },
                    new { Key = RecommendationLedgerWriter.NormalizeKey(row.StatKey), row.PropTypeId }
                })
                .Where(row => row.Key != string.Empty)
                .GroupBy(row => row.Key)
                .Where(group => group.Select(row => row.PropTypeId).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => (Guid?)group.Select(row => row.PropTypeId).First());
            var propTypes = await dbContext.PropTypes
                .AsNoTracking()
                .Select(row => new { row.Id, row.Name, row.StatKey })
                .ToArrayAsync(cancellationToken);
            var propTypeIdsByName = propTypes
                .SelectMany(row => new[]
                {
                    new { Key = RecommendationLedgerWriter.NormalizeKey(row.Name), row.Id },
                    new { Key = RecommendationLedgerWriter.NormalizeKey(row.StatKey), row.Id }
                })
                .Where(row => row.Key != string.Empty)
                .GroupBy(row => row.Key)
                .Where(group => group.Key != string.Empty && group.Select(row => row.Id).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => (Guid?)group.Select(row => row.Id).First());
            var resultPlayers = await dbContext.PropResults
                .AsNoTracking()
                .Where(row => row.EventDate >= from && row.EventDate <= to && row.Player != null)
                .Select(row => new { row.PlayerId, row.Player!.DisplayName })
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var resultPlayerIdsByName = resultPlayers
                .GroupBy(row => RecommendationLedgerWriter.NormalizeKey(row.DisplayName))
                .Where(group => group.Key != string.Empty && group.Select(row => row.PlayerId).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => (Guid?)group.Select(row => row.PlayerId).First());
            var players = await dbContext.Players
                .AsNoTracking()
                .Where(row => row.League != null && row.League.Code == "MLB")
                .Select(row => new { row.Id, row.DisplayName })
                .ToArrayAsync(cancellationToken);
            var playerIdsByName = players
                .GroupBy(row => RecommendationLedgerWriter.NormalizeKey(row.DisplayName))
                .Where(group => group.Key != string.Empty && group.Select(row => row.Id).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => (Guid?)group.Select(row => row.Id).First());

            if (requestedSource.Equals("marketSnapshots", StringComparison.OrdinalIgnoreCase) ||
                requestedSource.Equals("both", StringComparison.OrdinalIgnoreCase))
            {
                var snapshots = await dbContext.MarketLineShoppingSnapshots
                    .AsNoTracking()
                    .Where(row => row.SlateDate >= from && row.SlateDate <= to && (row.IsCandidate || row.IsWatchlist))
                    .ToArrayAsync(cancellationToken);
                foreach (var snapshot in snapshots)
                {
                    candidates.Add(MapSnapshotToLedgerCandidate(snapshot, isTopPick: false, playerIdsByName, resultPlayerIdsByName, propTypeIdsByName, resultPropTypeIdsByName));
                }

                var topPicks = await dbContext.MarketTopPickSnapshots
                    .AsNoTracking()
                    .Where(row => row.SlateDate >= from && row.SlateDate <= to)
                    .ToArrayAsync(cancellationToken);
                foreach (var snapshot in topPicks)
                {
                    candidates.Add(MapSnapshotToLedgerCandidate(snapshot, isTopPick: true, playerIdsByName, resultPlayerIdsByName, propTypeIdsByName, resultPropTypeIdsByName));
                }
            }

            var result = await RecommendationLedgerWriter.UpsertAsync(
                dbContext,
                candidates,
                shouldExecute,
                cancellationToken);

            return Results.Ok(new
            {
                dryRun = !shouldExecute,
                execute = shouldExecute,
                source = requestedSource,
                fromDate,
                toDate,
                result.CandidateRowsFound,
                result.RowsEligibleForLedger,
                rowsAlreadyExist = result.RowsAlreadyExist,
                rowsToCreate = shouldExecute ? 0 : result.RowsCreated,
                rowsCreated = shouldExecute ? result.RowsCreated : 0,
                result.RowsUpdated,
                result.SkippedMissingPlayerId,
                result.SkippedMissingExpectedResultDate,
                result.SkippedMissingPropType,
                result.SkippedAmbiguousPlayer,
                dateBreakdown = result.DateBreakdown.Values
            });
        });

        return endpoints;
    }

    private static RecommendationLedgerCandidate MapSnapshotToLedgerCandidate(
        MarketLineShoppingSnapshot snapshot,
        bool isTopPick,
        IReadOnlyDictionary<string, Guid?> playerIdsByName,
        IReadOnlyDictionary<string, Guid?> resultPlayerIdsByName,
        IReadOnlyDictionary<string, Guid?> propTypeIdsByName,
        IReadOnlyDictionary<string, Guid?> resultPropTypeIdsByName)
    {
        var playerId = ExtractGuid(snapshot.DetailsJson, "playerId")
            ?? ExtractGuid(snapshot.DetailsJson, "canonicalPlayerId")
            ?? ResolveNullableGuid(resultPlayerIdsByName, snapshot.Player)
            ?? ResolveNullableGuid(playerIdsByName, snapshot.Player);
        var propTypeId = ExtractGuid(snapshot.DetailsJson, "propTypeId")
            ?? ResolveNullableGuid(resultPropTypeIdsByName, snapshot.PropType)
            ?? ResolveNullableGuid(propTypeIdsByName, snapshot.PropType);
        var expectedResultDate = ExtractDate(snapshot.DetailsJson, "expectedResultDate") ?? snapshot.SlateDate;
        var resultDateSource = ExtractString(snapshot.DetailsJson, "resultDateSource") ?? "SnapshotSlateDateFallback";
        var hitChance = string.Equals(snapshot.BestSide, "Under", StringComparison.OrdinalIgnoreCase)
            ? ExtractDecimal(snapshot.DetailsJson, "modelProbabilityUnder") ?? snapshot.BestProbability
            : ExtractDecimal(snapshot.DetailsJson, "modelProbabilityOver") ?? snapshot.BestProbability;

        return new RecommendationLedgerCandidate(
            CapturedAtUtc: snapshot.GeneratedAtUtc,
            RefreshRunId: snapshot.TheOddsRefreshRunId,
            PredictionRunId: snapshot.PredictionRunId,
            Sport: snapshot.Sport,
            SlateDate: snapshot.SlateDate,
            ExpectedResultDate: expectedResultDate,
            ResultDateSource: resultDateSource,
            EventId: snapshot.EventId,
            EventStartTimeUtc: snapshot.EventStartTime,
            CanonicalPlayerId: playerId,
            PlayerName: snapshot.Player,
            PropTypeId: propTypeId,
            PropType: snapshot.PropType,
            Side: snapshot.BestSide,
            Line: snapshot.Line,
            Provider: snapshot.EntryProvider,
            Bookmaker: snapshot.BestBookmaker ?? snapshot.Bookmaker,
            ProviderCategory: snapshot.ProviderCategory,
            EntryProvider: snapshot.EntryProvider,
            EntryCompatibilityGroup: snapshot.EntryCompatibilityGroup,
            HitChance: hitChance,
            ImpliedProbability: snapshot.BestImpliedProbability,
            ProbabilityEdge: snapshot.BestProbabilityEdge,
            EstimatedEv: snapshot.BestEstimatedEv,
            TrustLevel: ExtractString(snapshot.DetailsJson, "trustLevel") ?? string.Empty,
            SampleWarning: ExtractString(snapshot.DetailsJson, "sampleWarning"),
            Warning: ExtractFirstStringArrayValue(snapshot.DetailsJson, "warnings"),
            HistoricalSampleSize: ExtractInt(snapshot.DetailsJson, "historicalSampleSize"),
            StrategyClassification: snapshot.StrategyClassification,
            StrategyTier: snapshot.StrategyTier,
            RankingScore: ExtractDecimal(snapshot.DetailsJson, "rankingScore") ?? 0m,
            IsCandidate: snapshot.IsCandidate,
            IsTopPick: isTopPick,
            IsWatchlist: snapshot.IsWatchlist,
            IsActionableForUser: snapshot.EntryActionableForUser,
            LifecycleStatus: snapshot.LifecycleStatus);
    }

    private static RecommendationLedgerCandidate MapSnapshotToLedgerCandidate(
        MarketTopPickSnapshot snapshot,
        bool isTopPick,
        IReadOnlyDictionary<string, Guid?> playerIdsByName,
        IReadOnlyDictionary<string, Guid?> resultPlayerIdsByName,
        IReadOnlyDictionary<string, Guid?> propTypeIdsByName,
        IReadOnlyDictionary<string, Guid?> resultPropTypeIdsByName)
    {
        var playerId = ExtractGuid(snapshot.DetailsJson, "playerId")
            ?? ExtractGuid(snapshot.DetailsJson, "canonicalPlayerId")
            ?? ResolveNullableGuid(resultPlayerIdsByName, snapshot.Player)
            ?? ResolveNullableGuid(playerIdsByName, snapshot.Player);
        var propTypeId = ExtractGuid(snapshot.DetailsJson, "propTypeId")
            ?? ResolveNullableGuid(resultPropTypeIdsByName, snapshot.PropType)
            ?? ResolveNullableGuid(propTypeIdsByName, snapshot.PropType);
        var expectedResultDate = ExtractDate(snapshot.DetailsJson, "expectedResultDate") ?? snapshot.SlateDate;
        var resultDateSource = ExtractString(snapshot.DetailsJson, "resultDateSource") ?? "SnapshotSlateDateFallback";
        var hitChance = string.Equals(snapshot.BestSide, "Under", StringComparison.OrdinalIgnoreCase)
            ? ExtractDecimal(snapshot.DetailsJson, "modelProbabilityUnder") ?? snapshot.BestProbability
            : ExtractDecimal(snapshot.DetailsJson, "modelProbabilityOver") ?? snapshot.BestProbability;

        return new RecommendationLedgerCandidate(
            CapturedAtUtc: snapshot.GeneratedAtUtc,
            RefreshRunId: snapshot.TheOddsRefreshRunId,
            PredictionRunId: snapshot.PredictionRunId,
            Sport: snapshot.Sport,
            SlateDate: snapshot.SlateDate,
            ExpectedResultDate: expectedResultDate,
            ResultDateSource: resultDateSource,
            EventId: snapshot.EventId,
            EventStartTimeUtc: snapshot.EventStartTime,
            CanonicalPlayerId: playerId,
            PlayerName: snapshot.Player,
            PropTypeId: propTypeId,
            PropType: snapshot.PropType,
            Side: snapshot.BestSide,
            Line: snapshot.Line,
            Provider: snapshot.EntryProvider,
            Bookmaker: snapshot.BestBookmaker ?? snapshot.Bookmaker,
            ProviderCategory: snapshot.ProviderCategory,
            EntryProvider: snapshot.EntryProvider,
            EntryCompatibilityGroup: snapshot.EntryCompatibilityGroup,
            HitChance: hitChance,
            ImpliedProbability: snapshot.BestImpliedProbability,
            ProbabilityEdge: snapshot.BestProbabilityEdge,
            EstimatedEv: snapshot.BestEstimatedEv,
            TrustLevel: ExtractString(snapshot.DetailsJson, "trustLevel") ?? string.Empty,
            SampleWarning: ExtractString(snapshot.DetailsJson, "sampleWarning"),
            Warning: ExtractFirstStringArrayValue(snapshot.DetailsJson, "warnings"),
            HistoricalSampleSize: ExtractInt(snapshot.DetailsJson, "historicalSampleSize"),
            StrategyClassification: snapshot.StrategyClassification,
            StrategyTier: snapshot.StrategyTier,
            RankingScore: ExtractDecimal(snapshot.DetailsJson, "rankingScore") ?? 0m,
            IsCandidate: snapshot.IsCandidate,
            IsTopPick: isTopPick,
            IsWatchlist: snapshot.IsWatchlist,
            IsActionableForUser: snapshot.EntryActionableForUser,
            LifecycleStatus: snapshot.LifecycleStatus);
    }

    private static string? ExtractString(JsonDocument document, string propertyName)
    {
        return TryGetProperty(document.RootElement, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static Guid? ResolveNullableGuid(IReadOnlyDictionary<string, Guid?> lookup, string value)
    {
        return lookup.TryGetValue(RecommendationLedgerWriter.NormalizeKey(value), out var id) ? id : null;
    }

    private static Guid? ExtractGuid(JsonDocument document, string propertyName)
    {
        var text = ExtractString(document, propertyName);
        return Guid.TryParse(text, out var value) ? value : null;
    }

    private static DateOnly? ExtractDate(JsonDocument document, string propertyName)
    {
        var text = ExtractString(document, propertyName);
        return DateOnly.TryParse(text, out var value) ? value : null;
    }

    private static decimal? ExtractDecimal(JsonDocument document, string propertyName)
    {
        if (!TryGetProperty(document.RootElement, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;
    }

    private static int? ExtractInt(JsonDocument document, string propertyName)
    {
        if (!TryGetProperty(document.RootElement, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static string? ExtractFirstStringArrayValue(JsonDocument document, string propertyName)
    {
        if (!TryGetProperty(document.RootElement, propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray()
            .FirstOrDefault(item => item.ValueKind == JsonValueKind.String)
            .GetString();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }

                if (TryGetProperty(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryGetProperty(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
