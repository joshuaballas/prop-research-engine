using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Endpoints;

public static partial class TheOddsApiDebugEndpoints
{
    internal static async Task<object> BuildLikelyHitSelectedSideDiagnosticsAsync(
        string? sport,
        string? competition,
        string? propType,
        string? playerName,
        string? providerCategory,
        int? take,
        bool? includePerformance,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var requestedTake = Math.Clamp(take ?? 250, 1, 500);
        var normalizedSport = NormalizeSelectedSideSportFilter(sport);
        var normalizedProviderCategory = NormalizeSelectedSideProviderCategoryFilter(providerCategory);

        var topPickRows = await BuildTopPickSelectedSideDiagnosticRowsAsync(
            normalizedSport,
            competition,
            propType,
            playerName,
            normalizedProviderCategory,
            requestedTake,
            dbContext,
            cancellationToken);

        var performanceRows = includePerformance == false
            ? Array.Empty<SelectedSideDiagnosticRow>()
            : await BuildPerformanceSelectedSideDiagnosticRowsAsync(
                normalizedSport,
                propType,
                playerName,
                normalizedProviderCategory,
                requestedTake,
                topPickRows,
                dbContext,
                cancellationToken);

        return new
        {
            generatedAtUtc,
            readOnly = true,
            callsExternalApis = false,
            writesRows = false,
            source = "persistedMarketTopPickSnapshotsAndRecommendationSnapshots",
            filters = new
            {
                sport = normalizedSport,
                competition,
                propType,
                playerName,
                providerCategory = normalizedProviderCategory,
                take = requestedTake,
                includePerformance = includePerformance != false
            },
            topPicks = new
            {
                summary = BuildSelectedSideDiagnosticsSummary(topPickRows),
                rows = topPickRows
            },
            performance = new
            {
                rowSource = "gradedTopPickRecommendationSnapshots",
                note = "Performance ledger rows store selected-side probability directly. Side-specific Over/Under probabilities are populated only when a matching persisted Top Pick snapshot is available.",
                summary = BuildSelectedSideDiagnosticsSummary(performanceRows),
                rows = performanceRows
            }
        };
    }

    private static async Task<SelectedSideDiagnosticRow[]> BuildTopPickSelectedSideDiagnosticRowsAsync(
        string? normalizedSport,
        string? competition,
        string? propType,
        string? playerName,
        string? normalizedProviderCategory,
        int requestedTake,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.MarketTopPickSnapshots.AsNoTracking();

        if (!IsSelectedSideAllFilter(normalizedSport))
        {
            query = query.Where(row => row.Sport == normalizedSport);
        }

        if (!IsSelectedSideAllFilter(normalizedProviderCategory))
        {
            query = query.Where(row => row.ProviderCategory == normalizedProviderCategory);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            query = query.Where(row => row.PropType == propType);
        }

        if (!string.IsNullOrWhiteSpace(playerName))
        {
            query = query.Where(row => row.Player.Contains(playerName));
        }

        var latestRunCandidates = await query
            .Select(row => new
            {
                row.Sport,
                row.ProviderCategory,
                row.TheOddsRefreshRunId,
                row.GeneratedAtUtc
            })
            .ToArrayAsync(cancellationToken);

        var latestRunIds = latestRunCandidates
            .GroupBy(row => $"{row.Sport}|{row.ProviderCategory}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.GeneratedAtUtc)
                .ThenByDescending(row => row.TheOddsRefreshRunId)
                .First()
                .TheOddsRefreshRunId)
            .ToHashSet();

        if (latestRunIds.Count == 0)
        {
            return [];
        }

        var snapshots = await query
            .Where(row => latestRunIds.Contains(row.TheOddsRefreshRunId))
            .Where(row => row.LifecycleStatus == "Active" && !row.IsStale && !row.IsExpired)
            .OrderByDescending(row => row.BestEstimatedEv)
            .ThenByDescending(row => row.BestProbabilityEdge)
            .ThenBy(row => row.Player)
            .Take(requestedTake)
            .ToArrayAsync(cancellationToken);

        return snapshots
            .Select(row => BuildTopPickSelectedSideSourceRow(row, competition))
            .Where(row => string.IsNullOrWhiteSpace(competition) ||
                string.Equals(ReadLikelyHitString(row, "competition"), competition, StringComparison.OrdinalIgnoreCase))
            .Select(row => BuildSelectedSideDiagnosticRow(row, source: "topPickSnapshot", sideSpecificDiagnosticsSource: "topPickSnapshotDetails"))
            .ToArray();
    }

    private static async Task<SelectedSideDiagnosticRow[]> BuildPerformanceSelectedSideDiagnosticRowsAsync(
        string? normalizedSport,
        string? propType,
        string? playerName,
        string? normalizedProviderCategory,
        int requestedTake,
        IReadOnlyCollection<SelectedSideDiagnosticRow> topPickRows,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(row => row.IsTopPick && !row.IsExcludedFromPerformance)
            .Where(row => row.GradeStatus == "Win" || row.GradeStatus == "Loss" || row.GradeStatus == "Push");

        if (!IsSelectedSideAllFilter(normalizedSport))
        {
            query = query.Where(row => row.Sport == normalizedSport);
        }

        if (!IsSelectedSideAllFilter(normalizedProviderCategory))
        {
            query = query.Where(row => row.ProviderCategory == normalizedProviderCategory);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            var normalizedPropType = RecommendationLedgerWriter.NormalizeKey(propType);
            query = query.Where(row => row.PropType == propType || row.NormalizedPropType == normalizedPropType);
        }

        if (!string.IsNullOrWhiteSpace(playerName))
        {
            query = query.Where(row => row.PlayerName.Contains(playerName));
        }

        var matchingTopPickRows = topPickRows
            .GroupBy(BuildSelectedSideDiagnosticIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var rows = await query
            .OrderByDescending(row => row.ExpectedResultDate)
            .ThenBy(row => row.PlayerName)
            .ThenBy(row => row.PropType)
            .Take(requestedTake)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                matchingTopPickRows.TryGetValue(BuildPerformanceSelectedSideIdentityKey(row), out var matchingTopPick);
                return BuildPerformanceSelectedSideDiagnosticRow(row, matchingTopPick);
            })
            .ToArray();
    }

    private static JsonObject BuildTopPickSelectedSideSourceRow(MarketTopPickSnapshot row, string? competitionFilter)
    {
        JsonObject node;
        try
        {
            node = JsonNode.Parse(row.DetailsJson.RootElement.GetRawText()) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            node = [];
        }

        var competition = string.IsNullOrWhiteSpace(competitionFilter)
            ? row.Sport
            : competitionFilter;
        var propType = ReadLikelyHitString(node, "propType")
            ?? ReadLikelyHitString(node, "normalizedPropType")
            ?? row.PropType;
        var player = ReadLikelyHitString(node, "player")
            ?? ReadLikelyHitString(node, "playerName")
            ?? row.Player;

        node["topPickSnapshotId"] = row.Id.ToString("D");
        node["refreshRunId"] = row.TheOddsRefreshRunId.ToString("D");
        node["sport"] = ReadLikelyHitString(node, "sport") ?? row.Sport;
        node["competition"] = ReadLikelyHitString(node, "competition") ?? competition;
        node["providerCategory"] = ReadLikelyHitString(node, "providerCategory") ?? row.ProviderCategory;
        node["provider"] = ReadLikelyHitString(node, "provider") ?? row.Provider;
        node["bookmaker"] = ReadLikelyHitString(node, "bookmaker") ?? row.Bookmaker;
        node["bestBookmaker"] = ReadLikelyHitString(node, "bestBookmaker") ?? row.BestBookmaker ?? row.Bookmaker;
        node["player"] = player;
        node["playerName"] = ReadLikelyHitString(node, "playerName") ?? player;
        node["eventId"] = ReadLikelyHitString(node, "eventId") ?? row.EventId;
        node["eventName"] = ReadLikelyHitString(node, "eventName")
            ?? ReadLikelyHitString(node, "event")
            ?? row.EventName;
        node["eventStartTimeUtc"] = ReadLikelyHitString(node, "eventStartTimeUtc") ?? row.EventStartTime?.ToString("O", CultureInfo.InvariantCulture);
        node["slateDate"] = ReadLikelyHitString(node, "slateDate") ?? row.SlateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        node["expectedResultDate"] = ReadLikelyHitString(node, "expectedResultDate")
            ?? ReadLikelyHitString(node, "eventDate")
            ?? row.SlateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        node["propType"] = propType;
        node["normalizedPropType"] = ReadLikelyHitString(node, "normalizedPropType") ?? propType;
        node["line"] = ReadLikelyHitDecimal(node, "line") ?? row.Line;
        node["bestSide"] = ReadLikelyHitString(node, "bestSide") ?? row.BestSide;
        node["bestAmericanPrice"] = ReadLikelyHitInt(node, "bestAmericanPrice") ?? row.BestAmericanPrice;
        node["bestDecimalPrice"] = ReadLikelyHitDecimal(node, "bestDecimalPrice") ?? row.BestDecimalPrice;
        node["bestProbability"] = ReadLikelyHitDecimal(node, "bestProbability") ?? row.BestProbability;
        node["selectedSideProbability"] = ReadLikelyHitDecimal(node, "selectedSideProbability") ?? row.BestProbability;
        node["bestImpliedProbability"] = ReadLikelyHitDecimal(node, "bestImpliedProbability") ?? row.BestImpliedProbability;
        node["bestProbabilityEdge"] = ReadLikelyHitDecimal(node, "bestProbabilityEdge") ?? row.BestProbabilityEdge;
        node["bestEstimatedEv"] = ReadLikelyHitDecimal(node, "bestEstimatedEv")
            ?? ReadLikelyHitDecimal(node, "displayExpectedValue")
            ?? row.BestEstimatedEv;
        node["topPickScore"] = ReadLikelyHitDecimal(node, "topPickScore")
            ?? ReadLikelyHitDecimal(node, "rankingScore");
        node["probabilityModelVersion"] = ReadLikelyHitString(node, "probabilityModelVersion") ?? row.ProbabilityModelVersion;
        return node;
    }

    private static SelectedSideDiagnosticRow BuildPerformanceSelectedSideDiagnosticRow(
        RecommendationSnapshot row,
        SelectedSideDiagnosticRow? matchingTopPick)
    {
        var selectedSideProbability = row.HitChance;
        var bestProbabilityEdge = row.ProbabilityEdge;
        var bestEstimatedEv = row.EstimatedEv;
        var selectedSide = row.Side;
        var modelOverProbability = matchingTopPick?.ModelOverProbability;
        var modelUnderProbability = matchingTopPick?.ModelUnderProbability;
        var priceBookmaker = matchingTopPick?.BestPriceBookmaker ?? row.Bookmaker ?? row.Provider;
        var bestAmericanPrice = matchingTopPick?.BestAmericanPrice;
        var bestDecimalPrice = matchingTopPick?.BestDecimalPrice;
        var chosenSideReason = BuildSelectedSideChosenReason(selectedSide, modelOverProbability, modelUnderProbability);

        return new SelectedSideDiagnosticRow(
            Source: "performanceLedger",
            SideSpecificDiagnosticsSource: matchingTopPick is null ? "unavailableInRecommendationSnapshot" : "matchedTopPickSnapshot",
            Sport: row.Sport,
            Competition: matchingTopPick?.Competition ?? row.Sport,
            PropType: string.IsNullOrWhiteSpace(row.PropType) ? row.NormalizedPropType : row.PropType,
            PlayerName: row.PlayerName,
            Line: row.Line,
            BestSide: selectedSide,
            ModelOverProbability: modelOverProbability,
            ModelUnderProbability: modelUnderProbability,
            SelectedSideProbability: selectedSideProbability,
            RawSelectedSideProbability: matchingTopPick?.RawSelectedSideProbability,
            CalibratedSelectedSideProbability: matchingTopPick?.CalibratedSelectedSideProbability,
            BestProbability: selectedSideProbability,
            BestProbabilityEdge: bestProbabilityEdge,
            BestEstimatedEv: bestEstimatedEv,
            ImpliedProbability: row.ImpliedProbability,
            Price: new SelectedSidePriceDiagnostic(priceBookmaker, bestAmericanPrice, bestDecimalPrice),
            LineBucket: BuildLikelyHitLineBucket(string.IsNullOrWhiteSpace(row.PropType) ? row.NormalizedPropType : row.PropType, row.Line),
            BestAmericanPrice: bestAmericanPrice,
            BestDecimalPrice: bestDecimalPrice,
            BestPriceBookmaker: priceBookmaker,
            ProjectedValue: matchingTopPick?.ProjectedValue,
            ProjectionSource: matchingTopPick?.ProjectionSource,
            ProbabilitySource: matchingTopPick?.ProbabilitySource,
            ProbabilityModelVersion: row.ProbabilityModelVersion ?? matchingTopPick?.ProbabilityModelVersion,
            EvCalibrationMethod: matchingTopPick?.EvCalibrationMethod,
            SelectedSideHistoryConsistencyGuardApplied: matchingTopPick?.SelectedSideHistoryConsistencyGuardApplied,
            SelectedSideHistoryConsistencyGuardPassed: matchingTopPick?.SelectedSideHistoryConsistencyGuardPassed,
            FatalSelectedSideHistoryConsistencyCodes: matchingTopPick?.FatalSelectedSideHistoryConsistencyCodes ?? [],
            PriceGateStatus: matchingTopPick?.PriceGateStatus,
            PriceGateReason: matchingTopPick?.PriceGateReason,
            EvSuppressedReason: matchingTopPick?.EvSuppressedReason,
            ChosenSideReason: chosenSideReason,
            OverRejectedReason: BuildSideRejectedReason("Over", selectedSide, modelOverProbability, modelUnderProbability),
            UnderRejectedReason: BuildSideRejectedReason("Under", selectedSide, modelOverProbability, modelUnderProbability),
            SelectedSideProbabilityMismatch: IsSelectedSideProbabilityMismatch(selectedSide, modelOverProbability, modelUnderProbability),
            EventId: row.EventId,
            EventName: matchingTopPick?.EventName,
            HomeTeam: matchingTopPick?.HomeTeam,
            AwayTeam: matchingTopPick?.AwayTeam,
            TeamName: matchingTopPick?.TeamName,
            ProviderCategory: row.ProviderCategory,
            Provider: row.Provider,
            Bookmaker: row.Bookmaker,
            ModelEra: row.ModelEra,
            GradeStatus: row.GradeStatus,
            ExpectedResultDate: row.ExpectedResultDate,
            SlateDate: row.SlateDate,
            TopPickScore: matchingTopPick?.TopPickScore);
    }

    private static SelectedSideDiagnosticRow BuildSelectedSideDiagnosticRow(
        JsonObject row,
        string source,
        string sideSpecificDiagnosticsSource)
    {
        var bestSide = ReadLikelyHitString(row, "bestSide")
            ?? ReadLikelyHitString(row, "selectedSide")
            ?? ReadLikelyHitString(row, "side")
            ?? string.Empty;
        var modelOverProbability = ReadLikelyHitDecimal(row, "modelOverProbability")
            ?? ReadLikelyHitDecimal(row, "modelProbabilityOver");
        var modelUnderProbability = ReadLikelyHitDecimal(row, "modelUnderProbability")
            ?? ReadLikelyHitDecimal(row, "modelProbabilityUnder");
        var selectedSideProbability = ReadLikelyHitDecimal(row, "selectedSideProbability")
            ?? ReadLikelyHitDecimal(row, "hitChance")
            ?? ReadLikelyHitDecimal(row, "bestProbability");
        var bestProbability = ReadLikelyHitDecimal(row, "bestProbability") ?? selectedSideProbability;
        var propType = ReadLikelyHitString(row, "propType")
            ?? ReadLikelyHitString(row, "normalizedPropType")
            ?? "Unknown";
        var bestAmericanPrice = ReadLikelyHitInt(row, "bestAmericanPrice");
        var bestDecimalPrice = ReadLikelyHitDecimal(row, "bestDecimalPrice");
        var bestPriceBookmaker = ReadLikelyHitString(row, "bestBookmaker")
            ?? ReadLikelyHitString(row, "bookmaker")
            ?? ReadLikelyHitString(row, "provider");
        var chosenSideReason = BuildSelectedSideChosenReason(bestSide, modelOverProbability, modelUnderProbability);

        return new SelectedSideDiagnosticRow(
            Source: source,
            SideSpecificDiagnosticsSource: sideSpecificDiagnosticsSource,
            Sport: ReadLikelyHitString(row, "sport") ?? "Unknown",
            Competition: ReadLikelyHitString(row, "competition")
                ?? ReadLikelyHitString(row, "sport")
                ?? "Unknown",
            PropType: propType,
            PlayerName: ReadLikelyHitString(row, "playerName")
                ?? ReadLikelyHitString(row, "player")
                ?? "Unknown",
            Line: ReadLikelyHitDecimal(row, "line"),
            BestSide: bestSide,
            ModelOverProbability: modelOverProbability,
            ModelUnderProbability: modelUnderProbability,
            SelectedSideProbability: selectedSideProbability,
            RawSelectedSideProbability: ReadLikelyHitDecimal(row, "rawSelectedSideProbability"),
            CalibratedSelectedSideProbability: ReadLikelyHitDecimal(row, "calibratedSelectedSideProbability"),
            BestProbability: bestProbability,
            BestProbabilityEdge: ReadLikelyHitDecimal(row, "bestProbabilityEdge"),
            BestEstimatedEv: ReadLikelyHitDecimal(row, "bestEstimatedEv")
                ?? ReadLikelyHitDecimal(row, "displayExpectedValue"),
            ImpliedProbability: ReadLikelyHitDecimal(row, "bestImpliedProbability")
                ?? ReadLikelyHitDecimal(row, "impliedProbability"),
            Price: new SelectedSidePriceDiagnostic(
                bestPriceBookmaker,
                bestAmericanPrice,
                bestDecimalPrice),
            LineBucket: BuildLikelyHitLineBucket(propType, ReadLikelyHitDecimal(row, "line")),
            BestAmericanPrice: bestAmericanPrice,
            BestDecimalPrice: bestDecimalPrice,
            BestPriceBookmaker: bestPriceBookmaker,
            ProjectedValue: ReadLikelyHitDecimal(row, "recommendationProjection")
                ?? ReadLikelyHitDecimal(row, "displayProjection")
                ?? ReadLikelyHitDecimal(row, "projectionMean")
                ?? ReadLikelyHitDecimal(row, "modelProjection")
                ?? ReadLikelyHitDecimal(row, "projectedValue")
                ?? ReadLikelyHitDecimal(row, "recentWeightedProjection"),
            ProjectionSource: ReadLikelyHitString(row, "recommendationProjectionSource")
                ?? ReadLikelyHitString(row, "displayProjectionSource")
                ?? ReadLikelyHitString(row, "projectionSource"),
            ProbabilitySource: ReadLikelyHitString(row, "probabilitySource")
                ?? ReadLikelyHitString(row, "probabilityMethod"),
            ProbabilityModelVersion: ReadLikelyHitString(row, "probabilityModelVersion")
                ?? ReadLikelyHitString(row, "modelVersion"),
            EvCalibrationMethod: ReadLikelyHitString(row, "evCalibrationMethod"),
            SelectedSideHistoryConsistencyGuardApplied: ReadLikelyHitBool(row, "selectedSideHistoryConsistencyGuardApplied"),
            SelectedSideHistoryConsistencyGuardPassed: ReadLikelyHitBool(row, "selectedSideHistoryConsistencyGuardPassed"),
            FatalSelectedSideHistoryConsistencyCodes: ReadLikelyHitStringArray(row, "fatalSelectedSideHistoryConsistencyCodes"),
            PriceGateStatus: ReadLikelyHitString(row, "priceGateStatus"),
            PriceGateReason: ReadLikelyHitString(row, "priceGateReason"),
            EvSuppressedReason: ReadLikelyHitString(row, "evSuppressedReason"),
            ChosenSideReason: chosenSideReason,
            OverRejectedReason: BuildSideRejectedReason("Over", bestSide, modelOverProbability, modelUnderProbability),
            UnderRejectedReason: BuildSideRejectedReason("Under", bestSide, modelOverProbability, modelUnderProbability),
            SelectedSideProbabilityMismatch: IsSelectedSideProbabilityMismatch(bestSide, modelOverProbability, modelUnderProbability),
            EventId: ReadLikelyHitString(row, "eventId"),
            EventName: ReadLikelyHitString(row, "eventName")
                ?? ReadLikelyHitString(row, "event"),
            HomeTeam: ReadLikelyHitString(row, "homeTeam")
                ?? ReadLikelyHitString(row, "homeTeamName"),
            AwayTeam: ReadLikelyHitString(row, "awayTeam")
                ?? ReadLikelyHitString(row, "awayTeamName"),
            TeamName: ReadLikelyHitString(row, "teamName")
                ?? ReadLikelyHitString(row, "team")
                ?? ReadLikelyHitString(row, "playerTeam")
                ?? ReadLikelyHitString(row, "playerTeamName")
                ?? ReadLikelyHitString(row, "teamAbbreviation"),
            ProviderCategory: ReadLikelyHitString(row, "providerCategory"),
            Provider: ReadLikelyHitString(row, "provider"),
            Bookmaker: ReadLikelyHitString(row, "bookmaker")
                ?? ReadLikelyHitString(row, "bestBookmaker"),
            ModelEra: ReadLikelyHitString(row, "modelEra"),
            GradeStatus: null,
            ExpectedResultDate: ReadLikelyHitDateOnly(row, "expectedResultDate")
                ?? ReadLikelyHitDateOnly(row, "eventDate"),
            SlateDate: ReadLikelyHitDateOnly(row, "slateDate"),
            TopPickScore: ReadLikelyHitDecimal(row, "topPickScore")
                ?? ReadLikelyHitDecimal(row, "rankingScore"));
    }

    private static object BuildSelectedSideDiagnosticsSummary(IReadOnlyCollection<SelectedSideDiagnosticRow> rows)
    {
        return new
        {
            totalRows = rows.Count,
            overRows = rows.Count(row => IsSelectedSide(row.BestSide, "Over")),
            underRows = rows.Count(row => IsSelectedSide(row.BestSide, "Under")),
            unknownSideRows = rows.Count(row => !IsSelectedSide(row.BestSide, "Over") && !IsSelectedSide(row.BestSide, "Under")),
            selectedSideProbabilityMismatchCount = rows.Count(row => row.SelectedSideProbabilityMismatch),
            sideDistributionBySport = BuildSelectedSideDistribution(rows, row => row.Sport),
            sideDistributionByPropType = BuildSelectedSideDistribution(rows, row => row.PropType),
            sideDistributionByLineBucket = BuildSelectedSideDistribution(rows, row => BuildLikelyHitLineBucket(row.PropType ?? string.Empty, row.Line)),
            sideDistributionByProbabilitySource = BuildSelectedSideDistribution(rows, row => row.ProbabilitySource ?? "Unknown"),
            sideDistributionByProjectionSource = BuildSelectedSideDistribution(rows, row => row.ProjectionSource ?? "Unknown"),
            averageProbabilitiesByPropType = rows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.PropType) ? "Unknown" : row.PropType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    propType = group.Key,
                    count = group.Count(),
                    averageModelOverProbability = AverageLikelyHitDecimal(group.Select(row => row.ModelOverProbability)),
                    averageModelUnderProbability = AverageLikelyHitDecimal(group.Select(row => row.ModelUnderProbability)),
                    averageSelectedSideProbability = AverageLikelyHitDecimal(group.Select(row => row.SelectedSideProbability))
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.propType)
                .ToArray(),
            averageLineAndProjectedValueBySidePropType = rows
                .GroupBy(row => new
                {
                    Side = string.IsNullOrWhiteSpace(row.BestSide) ? "Unknown" : row.BestSide,
                    PropType = string.IsNullOrWhiteSpace(row.PropType) ? "Unknown" : row.PropType
                })
                .Select(group => new
                {
                    side = group.Key.Side,
                    propType = group.Key.PropType,
                    count = group.Count(),
                    averageLine = AverageLikelyHitDecimal(group.Select(row => row.Line)),
                    averageProjectedValue = AverageLikelyHitDecimal(group.Select(row => row.ProjectedValue)),
                    averageModelOverProbability = AverageLikelyHitDecimal(group.Select(row => row.ModelOverProbability)),
                    averageModelUnderProbability = AverageLikelyHitDecimal(group.Select(row => row.ModelUnderProbability))
                })
                .OrderBy(row => row.propType)
                .ThenBy(row => row.side)
                .ToArray(),
            rejectedOverCandidateReasons = BuildSelectedSideReasonCounts(rows.Select(row => row.OverRejectedReason)),
            rejectedUnderCandidateReasons = BuildSelectedSideReasonCounts(rows.Select(row => row.UnderRejectedReason))
        };
    }

    private static object[] BuildSelectedSideDistribution(
        IReadOnlyCollection<SelectedSideDiagnosticRow> rows,
        Func<SelectedSideDiagnosticRow, string?> keySelector)
    {
        return rows
            .GroupBy(row => string.IsNullOrWhiteSpace(keySelector(row)) ? "Unknown" : keySelector(row)!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                key = group.Key,
                count = group.Count(),
                over = group.Count(row => IsSelectedSide(row.BestSide, "Over")),
                under = group.Count(row => IsSelectedSide(row.BestSide, "Under")),
                unknown = group.Count(row => !IsSelectedSide(row.BestSide, "Over") && !IsSelectedSide(row.BestSide, "Under")),
                averageModelOverProbability = AverageLikelyHitDecimal(group.Select(row => row.ModelOverProbability)),
                averageModelUnderProbability = AverageLikelyHitDecimal(group.Select(row => row.ModelUnderProbability)),
                averageSelectedSideProbability = AverageLikelyHitDecimal(group.Select(row => row.SelectedSideProbability)),
                averageLine = AverageLikelyHitDecimal(group.Select(row => row.Line)),
                averageProjectedValue = AverageLikelyHitDecimal(group.Select(row => row.ProjectedValue))
            })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.key)
            .ToArray();
    }

    private static object[] BuildSelectedSideReasonCounts(IEnumerable<string?> reasons)
    {
        return reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { reason = group.Key, count = group.Count() })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.reason)
            .ToArray();
    }

    private static string BuildSelectedSideChosenReason(string? selectedSide, decimal? modelOverProbability, decimal? modelUnderProbability)
    {
        if (!IsSelectedSide(selectedSide, "Over") && !IsSelectedSide(selectedSide, "Under"))
        {
            return "SelectedSideUnavailable";
        }

        if (!modelOverProbability.HasValue || !modelUnderProbability.HasValue)
        {
            return "SideSpecificModelProbabilitiesUnavailable";
        }

        if (IsSelectedSide(selectedSide, "Over"))
        {
            return modelOverProbability.Value >= modelUnderProbability.Value
                ? "SelectedHigherModelOverProbability"
                : "SelectedSideProbabilityMismatch";
        }

        return modelUnderProbability.Value >= modelOverProbability.Value
            ? "SelectedHigherModelUnderProbability"
            : "SelectedSideProbabilityMismatch";
    }

    private static string? BuildSideRejectedReason(
        string candidateSide,
        string? selectedSide,
        decimal? modelOverProbability,
        decimal? modelUnderProbability)
    {
        if (IsSelectedSide(selectedSide, candidateSide))
        {
            return null;
        }

        if (!IsSelectedSide(candidateSide, "Over") && !IsSelectedSide(candidateSide, "Under"))
        {
            return "UnsupportedCandidateSide";
        }

        if (!IsSelectedSide(selectedSide, "Over") && !IsSelectedSide(selectedSide, "Under"))
        {
            return "NoSelectedSide";
        }

        if (!modelOverProbability.HasValue || !modelUnderProbability.HasValue)
        {
            return "SideSpecificModelProbabilityUnavailable";
        }

        var candidateProbability = IsSelectedSide(candidateSide, "Over")
            ? modelOverProbability.Value
            : modelUnderProbability.Value;
        var selectedProbability = IsSelectedSide(selectedSide, "Over")
            ? modelOverProbability.Value
            : modelUnderProbability.Value;

        if (candidateProbability < selectedProbability)
        {
            return IsSelectedSide(selectedSide, "Under")
                ? "LowerModelProbabilityThanSelectedUnder"
                : "LowerModelProbabilityThanSelectedOver";
        }

        if (candidateProbability == selectedProbability)
        {
            return "TieBrokenByPersistedSelectedSide";
        }

        return "RejectedDespiteHigherModelProbability";
    }

    private static bool IsSelectedSideProbabilityMismatch(string? selectedSide, decimal? modelOverProbability, decimal? modelUnderProbability)
    {
        if (!modelOverProbability.HasValue || !modelUnderProbability.HasValue)
        {
            return false;
        }

        return (IsSelectedSide(selectedSide, "Under") && modelOverProbability.Value > modelUnderProbability.Value)
            || (IsSelectedSide(selectedSide, "Over") && modelUnderProbability.Value > modelOverProbability.Value);
    }

    private static string BuildSelectedSideDiagnosticIdentityKey(SelectedSideDiagnosticRow row)
    {
        return string.Join('|',
            row.Sport ?? string.Empty,
            row.EventId ?? string.Empty,
            row.PlayerName ?? string.Empty,
            row.PropType ?? string.Empty,
            row.BestSide ?? string.Empty,
            row.Line?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
            row.ProviderCategory ?? string.Empty,
            row.Bookmaker ?? row.Provider ?? string.Empty);
    }

    private static string BuildPerformanceSelectedSideIdentityKey(RecommendationSnapshot row)
    {
        return string.Join('|',
            row.Sport,
            row.EventId ?? string.Empty,
            row.PlayerName,
            string.IsNullOrWhiteSpace(row.PropType) ? row.NormalizedPropType : row.PropType,
            row.Side,
            row.Line.ToString("0.####", CultureInfo.InvariantCulture),
            row.ProviderCategory,
            row.Bookmaker ?? row.Provider);
    }

    private static string NormalizeSelectedSideSportFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "MLB";
        }

        return string.Equals(value, "ALL", StringComparison.OrdinalIgnoreCase)
            ? "All"
            : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeSelectedSideProviderCategoryFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return "All";
        }

        if (string.Equals(value, "DFS", StringComparison.OrdinalIgnoreCase))
        {
            return "DFS";
        }

        if (string.Equals(value, "Sportsbook", StringComparison.OrdinalIgnoreCase))
        {
            return "Sportsbook";
        }

        return value.Trim();
    }

    private static bool IsSelectedSideAllFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSelectedSide(string? actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadLikelyHitStringArray(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(item => item?.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private sealed record SelectedSideDiagnosticRow(
        string Source,
        string SideSpecificDiagnosticsSource,
        string Sport,
        string Competition,
        string PropType,
        string PlayerName,
        decimal? Line,
        string BestSide,
        decimal? ModelOverProbability,
        decimal? ModelUnderProbability,
        decimal? SelectedSideProbability,
        decimal? RawSelectedSideProbability,
        decimal? CalibratedSelectedSideProbability,
        decimal? BestProbability,
        decimal? BestProbabilityEdge,
        decimal? BestEstimatedEv,
        decimal? ImpliedProbability,
        SelectedSidePriceDiagnostic Price,
        string LineBucket,
        int? BestAmericanPrice,
        decimal? BestDecimalPrice,
        string? BestPriceBookmaker,
        decimal? ProjectedValue,
        string? ProjectionSource,
        string? ProbabilitySource,
        string? ProbabilityModelVersion,
        string? EvCalibrationMethod,
        bool? SelectedSideHistoryConsistencyGuardApplied,
        bool? SelectedSideHistoryConsistencyGuardPassed,
        IReadOnlyList<string> FatalSelectedSideHistoryConsistencyCodes,
        string? PriceGateStatus,
        string? PriceGateReason,
        string? EvSuppressedReason,
        string ChosenSideReason,
        string? OverRejectedReason,
        string? UnderRejectedReason,
        bool SelectedSideProbabilityMismatch,
        string? EventId,
        string? EventName,
        string? HomeTeam,
        string? AwayTeam,
        string? TeamName,
        string? ProviderCategory,
        string? Provider,
        string? Bookmaker,
        string? ModelEra,
        string? GradeStatus,
        DateOnly? ExpectedResultDate,
        DateOnly? SlateDate,
        decimal? TopPickScore);

    private sealed record SelectedSidePriceDiagnostic(
        string? Bookmaker,
        int? American,
        decimal? Decimal);
}
