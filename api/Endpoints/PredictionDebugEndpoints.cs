using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Ingestion;
using SportsBetting.Api.Models;
using SportsBetting.Api.Predictions;

namespace SportsBetting.Api.Endpoints;

public static class PredictionDebugEndpoints
{
    public static IEndpointRouteBuilder MapPredictionDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/prediction-runs", async (
            IPredictionRepository predictionRepository,
            CancellationToken cancellationToken) =>
        {
            var runs = await predictionRepository.GetLatestRunsAsync(100, cancellationToken);

            return Results.Ok(new
            {
                count = runs.Count,
                runs = runs.Select(ToRunResponse).ToArray()
            });
        })
        .WithName("PredictionRunsDebug")
        .WithSummary("Returns recent prediction runs.");

        endpoints.MapGet("/api/debug/predictions", async (
            IPredictionRepository predictionRepository,
            CancellationToken cancellationToken) =>
        {
            var predictions = await predictionRepository.GetLatestPredictionsAsync(100, cancellationToken);

            return Results.Ok(new
            {
                count = predictions.Count,
                predictions = predictions.Select(ToPredictionResponse).ToArray()
            });
        })
        .WithName("PredictionsDebug")
        .WithSummary("Returns recent predictions.");

        endpoints.MapGet("/api/debug/predictions/probabilities", async (
            Guid? runId,
            IPredictionProbabilityService probabilityService,
            CancellationToken cancellationToken) =>
        {
            var analysis = runId.HasValue
                ? await probabilityService.AnalyzeRunAsync(runId.Value, cancellationToken)
                : await probabilityService.AnalyzeLatestRunAsync(cancellationToken);

            if (analysis is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            return Results.Ok(new
            {
                runId = analysis.RunId,
                analysis.ModelName,
                analysis.ModelVersion,
                analysis.CreatedAt,
                predictionCount = analysis.PredictionCount,
                results = analysis.Results
                    .OrderByDescending(result => result.RankingScore)
                    .ThenByDescending(result => Math.Abs(result.Edge ?? 0m))
                    .Select(result => new
                    {
                        result.PredictionId,
                        result.PropId,
                        result.PlayerId,
                        result.PlayerName,
                        result.PropTypeId,
                        result.PropType,
                        result.EventDate,
                        result.PredictedValue,
                        result.SportsbookLine,
                        result.Edge,
                        result.Recommendation,
                        result.ProbabilityOver,
                        result.ProbabilityUnder,
                        result.ProbabilityPush,
                        result.ConfidenceScore,
                        result.RankingScore,
                        result.SampleSize,
                        result.MethodUsed
                    })
                    .ToArray()
            });
        })
        .WithName("PredictionProbabilitiesDebug")
        .WithSummary("Returns read-only prototype probability estimates for persisted predictions.");

        endpoints.MapGet("/api/debug/predictions/mlb/public-row-probability-audit", async (
            string playerName,
            string? provider,
            string? bookmaker,
            string propType,
            decimal line,
            string side,
            DateOnly? eventDate,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var latestRunId = await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(row => row.Sport == "MLB")
                .OrderByDescending(row => row.GeneratedAtUtc)
                .Select(row => (Guid?)row.TheOddsRefreshRunId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!latestRunId.HasValue)
            {
                return Results.Ok(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    publicRowMatched = false,
                    rootCauseCategory = "NoPublicLineShoppingSnapshots"
                });
            }

            var snapshots = await dbContext.MarketLineShoppingSnapshots
                .AsNoTracking()
                .Where(row => row.TheOddsRefreshRunId == latestRunId.Value)
                .ToArrayAsync(cancellationToken);
            var normalizedPlayer = NormalizePlayerName(playerName);
            var normalizedPropType = NormalizePredictionPropTypeName(propType);
            var normalizedSide = NormalizeSideName(side);
            var normalizedProvider = NormalizeProviderName(provider ?? bookmaker);
            var matchedRows = snapshots
                .Select(snapshot => new { Snapshot = snapshot, Details = snapshot.DetailsJson.RootElement })
                .Where(row => NormalizePlayerName(ReadJsonString(row.Details, "player", "Player") ?? row.Snapshot.Player) == normalizedPlayer)
                .Where(row => NormalizePredictionPropTypeName(ReadJsonString(row.Details, "normalizedPropType", "NormalizedPropType") ?? row.Snapshot.PropType) == normalizedPropType)
                .Where(row => row.Snapshot.Line == line)
                .Where(row =>
                {
                    var rowSide = NormalizeSideName(ReadJsonString(row.Details, "bestSide", "BestSide") ?? row.Snapshot.BestSide);
                    return rowSide == normalizedSide || string.IsNullOrWhiteSpace(rowSide);
                })
                .Where(row => !eventDate.HasValue || row.Snapshot.SlateDate == eventDate.Value || ReadJsonDateOnly(row.Details, "expectedResultDate", "ExpectedResultDate") == eventDate.Value)
                .Where(row => string.IsNullOrWhiteSpace(normalizedProvider)
                    || NormalizeProviderName(ReadJsonString(row.Details, "entryProvider", "EntryProvider", "bestBookmaker", "BestBookmaker") ?? row.Snapshot.Provider ?? row.Snapshot.Bookmaker) == normalizedProvider
                    || NormalizeProviderName(row.Snapshot.Provider) == normalizedProvider
                    || NormalizeProviderName(row.Snapshot.Bookmaker) == normalizedProvider)
                .OrderByDescending(row => row.Snapshot.GeneratedAtUtc)
                .ToArray();
            var publicRow = matchedRows.FirstOrDefault();

            if (publicRow is null)
            {
                return Results.Ok(new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    callsExternalApis = false,
                    readOnly = true,
                    latestSnapshotRunId = latestRunId,
                    publicRowMatched = false,
                    rootCauseCategory = "NoPublicRowMatchedFilters"
                });
            }

            var details = publicRow.Details;
            var platformPropId = ReadFirstBookmakerPlatformPropId(details) ?? ReadJsonString(details, "platformPropId", "PlatformPropId");
            var platformProp = string.IsNullOrWhiteSpace(platformPropId)
                ? null
                : await dbContext.PlatformProps
                    .AsNoTracking()
                    .Include(row => row.Prop)
                        .ThenInclude(prop => prop!.Player)
                    .Include(row => row.Prop)
                        .ThenInclude(prop => prop!.PropType)
                    .SingleOrDefaultAsync(row => row.PlatformPropId == platformPropId, cancellationToken);
            var prediction = platformProp is null || !publicRow.Snapshot.PredictionRunId.HasValue
                ? null
                : await dbContext.Predictions
                    .AsNoTracking()
                    .Where(row => row.PredictionRunId == publicRow.Snapshot.PredictionRunId.Value && row.PropId == platformProp.PropId)
                    .OrderByDescending(row => row.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            var selectedSideProbability = ReadJsonDecimal(details, "selectedSideProbability", "SelectedSideProbability", "bestProbability", "BestProbability");
            var modelOverProbability = ReadJsonDecimal(details, "modelProbabilityOver", "ModelProbabilityOver", "modelOverProbability", "ModelOverProbability");
            var modelUnderProbability = ReadJsonDecimal(details, "modelProbabilityUnder", "ModelProbabilityUnder", "modelUnderProbability", "ModelUnderProbability");
            var probabilityEdge = ReadJsonDecimal(details, "bestProbabilityEdge", "BestProbabilityEdge");
            var historicalSampleSize = ReadJsonInt(details, "historicalSampleSize", "HistoricalSampleSize") ?? 0;
            var historyCount = platformProp?.Prop is null
                ? 0
                : await dbContext.PropResults
                    .AsNoTracking()
                    .CountAsync(row => row.ExternalSource == "mlb"
                        && row.PlayerId == platformProp.Prop.PlayerId
                        && row.PropTypeId == platformProp.Prop.PropTypeId, cancellationToken);
            var historyAvailable = historyCount > 0 || historicalSampleSize > 0;
            var rootCause = selectedSideProbability.HasValue
                ? "ModelProbabilityAvailable"
                : historyAvailable
                    ? "HistoricalDataAvailableButModelProbabilityMissing"
                    : prediction is null
                        ? "NoPredictionJoinedToPublicRow"
                        : "ModelProbabilityMissing";

            return Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                callsExternalApis = false,
                readOnly = true,
                latestSnapshotRunId = latestRunId,
                publicRowMatched = true,
                matchedPublicRows = matchedRows.Length,
                rootCauseCategory = rootCause,
                publicRow = new
                {
                    snapshotId = publicRow.Snapshot.Id,
                    snapshotRunId = publicRow.Snapshot.TheOddsRefreshRunId,
                    predictionRunId = publicRow.Snapshot.PredictionRunId,
                    provider = publicRow.Snapshot.Provider,
                    bookmaker = publicRow.Snapshot.Bookmaker,
                    providerCategory = publicRow.Snapshot.ProviderCategory,
                    platformPropId,
                    playerId = platformProp?.Prop?.PlayerId,
                    playerName = publicRow.Snapshot.Player,
                    propType = publicRow.Snapshot.PropType,
                    side = publicRow.Snapshot.BestSide,
                    publicRow.Snapshot.Line,
                    eventId = publicRow.Snapshot.EventId,
                    providerEventId = publicRow.Snapshot.EventId,
                    eventDate = ReadJsonDateOnly(details, "expectedResultDate", "ExpectedResultDate") ?? publicRow.Snapshot.SlateDate,
                    hitChance = selectedSideProbability,
                    selectedSideProbability,
                    predictedProbability = prediction?.PredictedProbability,
                    edge = probabilityEdge,
                    trust = ReadJsonString(details, "trustLevel", "TrustLevel") ?? publicRow.Snapshot.StrategyTier,
                    modelProbabilityAvailable = selectedSideProbability.HasValue,
                    hitChanceUnavailableReason = ReadJsonString(details, "hitChanceUnavailableReason", "HitChanceUnavailableReason")
                        ?? (selectedSideProbability.HasValue ? null : historyAvailable ? "HistoricalDataAvailableButModelProbabilityMissing" : "Model hit chance is unavailable for this row. Historical hit rate is shown in the chart."),
                    modelOverProbability,
                    modelUnderProbability,
                    probabilitySource = ReadJsonString(details, "probabilitySource", "ProbabilitySource", "probabilityMethod", "ProbabilityMethod")
                },
                latestPredictionMatch = prediction is null
                    ? null
                    : new
                    {
                        prediction.Id,
                        prediction.PredictionRunId,
                        prediction.PropId,
                        prediction.PredictedProbability,
                        prediction.PredictedValue,
                        prediction.SportsbookLine,
                        prediction.Edge,
                        prediction.Recommendation,
                        prediction.Confidence
                    },
                history = new
                {
                    persistedPropResults = historyCount,
                    historicalSampleSize,
                    historyAvailable
                },
                join = new
                {
                    platformPropResolved = platformProp is not null,
                    predictionJoinedBy = platformProp is null ? null : "snapshot.PredictionRunId + platformProp.PropId",
                    playerNormalized = normalizedPlayer,
                    propTypeNormalized = normalizedPropType,
                    providerNormalized = normalizedProvider
                }
            });
        })
        .WithName("MlbPublicRowProbabilityAuditDebug")
        .WithSummary("Read-only audit for public MLB market rows with missing model hit probability.");

        endpoints.MapGet("/api/debug/predictions/mlb/side-selection-audit", async (
            DateOnly? date,
            DateOnly? fromDate,
            DateOnly? toDate,
            bool? latestRunOnly,
            bool? includeSamples,
            int? maxSamples,
            string? provider,
            string? propType,
            bool? excludeSampleRuns,
            IPredictionProbabilityService probabilityService,
            IOptions<PredictionGenerationOptions> predictionGenerationOptions,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var effectiveFromDate = date ?? fromDate;
            var effectiveToDate = date ?? toDate;
            var useLatestRunOnly = latestRunOnly ?? true;
            var shouldExcludeSampleRuns = excludeSampleRuns ?? true;
            var shouldIncludeSamples = includeSamples ?? true;
            var requestedMaxSamples = Math.Clamp(maxSamples.GetValueOrDefault(200) <= 0 ? 200 : maxSamples.GetValueOrDefault(200), 1, 500);
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var latestRunId = useLatestRunOnly
                ? await FindLatestMlbPredictionRunIdAsync(dbContext, shouldExcludeSampleRuns, cancellationToken)
                : null;
            var latestPredictionRun = latestRunId.HasValue
                ? await dbContext.PredictionRuns
                    .AsNoTracking()
                    .Where(run => run.Id == latestRunId.Value)
                    .Select(run => new { run.Id, run.ModelName, run.ModelVersion, run.CreatedAt, run.CompletedAt, run.Notes })
                    .SingleOrDefaultAsync(cancellationToken)
                : null;
            var probabilityAnalysis = latestRunId.HasValue
                ? await probabilityService.AnalyzeRunAsync(latestRunId.Value, cancellationToken)
                : null;
            var predictionAuditRows = BuildPredictionStageAuditRows(
                probabilityAnalysis,
                effectiveFromDate,
                effectiveToDate,
                propType,
                predictionGenerationOptions.Value);
            var predictionStageSummary = BuildPredictionStageSummary(
                predictionAuditRows,
                latestPredictionRun?.Id,
                latestPredictionRun?.CreatedAt);
            var passGateSummary = BuildPredictionPassGateSummary(predictionAuditRows);
            var hitProbabilityDiagnostics = BuildPredictionHitProbabilityDiagnostics(predictionAuditRows, predictionGenerationOptions.Value);

            var predictionStage = await BuildPredictionSideDistributionAsync(
                dbContext,
                latestRunId,
                effectiveFromDate,
                effectiveToDate,
                propType,
                cancellationToken);
            var recommendationStage = await BuildRecommendationSideDistributionAsync(
                dbContext,
                latestRunId,
                effectiveFromDate,
                effectiveToDate,
                provider,
                propType,
                cancellationToken);
            var topPickStage = await BuildTopPickSideDistributionAsync(
                dbContext,
                latestRunId,
                effectiveFromDate,
                effectiveToDate,
                provider,
                propType,
                cancellationToken);
            var lineShoppingStage = await BuildLineShoppingSideDistributionAsync(
                dbContext,
                latestRunId,
                effectiveFromDate,
                effectiveToDate,
                provider,
                propType,
                cancellationToken);

            var lineShoppingRows = await BuildLineShoppingAuditQuery(
                    dbContext,
                    latestRunId,
                    effectiveFromDate,
                    effectiveToDate,
                    provider,
                    propType)
                .OrderByDescending(row => row.BestEstimatedEv)
                .ThenBy(row => row.Player)
                .ToListAsync(cancellationToken);

            var auditRows = lineShoppingRows
                .Select(ToSideSelectionAuditRow)
                .ToList();
            var rowSummaries = auditRows.Select(row => row.Summary).ToList();
            var suspiciousUnderCount = rowSummaries.Count(row => row.SelectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase) && row.ReasonCodes.Count > 0);
            var suspiciousOverCount = rowSummaries.Count(row => row.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase) && row.ReasonCodes.Count > 0);
            var possibleSideInversionCount = rowSummaries.Count(row => row.ReasonCodes.Contains("PossibleSideInversion"));
            var possibleEvProbabilityMismatchCount = rowSummaries.Count(row => row.ReasonCodes.Contains("PositiveEvButSelectedProbabilityBelowBreakEven")
                || row.ReasonCodes.Contains("ProbabilityEdgeDoesNotMatchSelectedProbability"));
            var missingSideDefaultedCount = rowSummaries.Count(row => row.ReasonCodes.Contains("MissingSideSelection"));

            var sideLabelChangedCount = await CountSideLabelChangesAsync(
                dbContext,
                latestRunId,
                effectiveFromDate,
                effectiveToDate,
                provider,
                propType,
                cancellationToken);

            var samples = shouldIncludeSamples
                ? auditRows
                    .OrderByDescending(row => row.Summary.ReasonCodes.Count)
                    .ThenByDescending(row => row.Summary.EstimatedEv)
                    .Take(requestedMaxSamples)
                    .Select(row => row.Detail)
                    .ToArray()
                : [];

            var totalRowsEvaluated = rowSummaries.Count;
            var overSelections = rowSummaries.Count(row => row.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase));
            var underSelections = rowSummaries.Count(row => row.SelectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase));
            var missingSideSelections = rowSummaries.Count(row => string.IsNullOrWhiteSpace(row.SelectedSide)
                || row.SelectedSide.Equals("Missing", StringComparison.OrdinalIgnoreCase)
                || row.SelectedSide.Equals("None", StringComparison.OrdinalIgnoreCase));

            return Results.Ok(new
            {
                generatedAtUtc,
                callsExternalApis = false,
                readOnly = true,
                writesRows = false,
                sport = "MLB",
                latestPredictionRunId = latestPredictionRun?.Id,
                latestPredictionRunModelName = latestPredictionRun?.ModelName,
                latestPredictionRunModelVersion = latestPredictionRun?.ModelVersion,
                latestPredictionRunCreatedAtUtc = latestPredictionRun?.CreatedAt,
                latestPredictionRunCompletedAtUtc = latestPredictionRun?.CompletedAt,
                latestPredictionRunNotes = latestPredictionRun?.Notes,
                filters = new
                {
                    date,
                    fromDate = effectiveFromDate,
                    toDate = effectiveToDate,
                    latestRunOnly = useLatestRunOnly,
                    excludeSampleRuns = shouldExcludeSampleRuns,
                    latestPredictionRunId = latestRunId,
                    provider,
                    propType,
                    includeSamples = shouldIncludeSamples,
                    maxSamples = requestedMaxSamples
                },
                totalRowsEvaluated,
                predictionStageSummary,
                passGateSummary,
                hitProbabilityDiagnostics,
                minimumPickHitProbability = predictionGenerationOptions.Value.MinimumPickHitProbability,
                strongPickHitProbability = predictionGenerationOptions.Value.StrongPickHitProbability,
                researchOnlyBelowHitProbability = predictionGenerationOptions.Value.ResearchOnlyBelowHitProbability,
                selectedRowsBelow50Count = hitProbabilityDiagnostics.SelectedRowsBelow50Count,
                selectedRowsBelowMinimumHitProbabilityCount = hitProbabilityDiagnostics.SelectedRowsBelowMinimumHitProbabilityCount,
                overRowsRejectedByHitProbability = hitProbabilityDiagnostics.OverRowsRejectedByHitProbability,
                underRowsRejectedByHitProbability = hitProbabilityDiagnostics.UnderRowsRejectedByHitProbability,
                overRowsRejectedByProjectionThreshold = hitProbabilityDiagnostics.OverRowsRejectedByProjectionThreshold,
                underRowsRejectedByProjectionThreshold = hitProbabilityDiagnostics.UnderRowsRejectedByProjectionThreshold,
                candidateSideDistributionAfterFix = hitProbabilityDiagnostics.CandidateSideDistributionAfterFix,
                passReasonsByCount = passGateSummary.PassReasonsByCount,
                failedGatesByCount = passGateSummary.FailedGatesByCount,
                predictionRowsEvaluated = predictionStageSummary.PredictionRowsEvaluated,
                predictionRowsWithLine = predictionStageSummary.PredictionRowsWithLine,
                predictionRowsMissingLine = predictionStageSummary.PredictionRowsMissingLine,
                predictionOverSelections = predictionStageSummary.PredictionOverSelections,
                predictionUnderSelections = predictionStageSummary.PredictionUnderSelections,
                predictionPassSelections = predictionStageSummary.PredictionPassSelections,
                predictionOverSelectionRate = predictionStageSummary.PredictionOverSelectionRate,
                predictionUnderSelectionRate = predictionStageSummary.PredictionUnderSelectionRate,
                predictionPassRate = predictionStageSummary.PredictionPassRate,
                predictionRowsWhereProjectionAboveLine = predictionStageSummary.PredictionRowsWhereProjectionAboveLine,
                predictionRowsWhereProjectionBelowLine = predictionStageSummary.PredictionRowsWhereProjectionBelowLine,
                predictionRowsWhereProjectionEqualsLine = predictionStageSummary.PredictionRowsWhereProjectionEqualsLine,
                projectionAboveLineButSelectedUnderCount = predictionStageSummary.ProjectionAboveLineButSelectedUnderCount,
                projectionBelowLineButSelectedOverCount = predictionStageSummary.ProjectionBelowLineButSelectedOverCount,
                overProbabilityGreaterThanUnderCount = predictionStageSummary.OverProbabilityGreaterThanUnderCount,
                underProbabilityGreaterThanOverCount = predictionStageSummary.UnderProbabilityGreaterThanOverCount,
                overEvGreaterThanUnderEvCount = predictionStageSummary.OverEvGreaterThanUnderEvCount,
                underEvGreaterThanOverEvCount = predictionStageSummary.UnderEvGreaterThanOverEvCount,
                overEvPositiveCount = predictionStageSummary.OverEvPositiveCount,
                underEvPositiveCount = predictionStageSummary.UnderEvPositiveCount,
                overEvPositiveButPassedCount = predictionStageSummary.OverEvPositiveButPassedCount,
                underEvPositiveButPassedCount = predictionStageSummary.UnderEvPositiveButPassedCount,
                overEvPositiveButSelectedUnderCount = predictionStageSummary.OverEvPositiveButSelectedUnderCount,
                selectedUnderWithModelUnderBelowBreakEvenCount = predictionStageSummary.SelectedUnderWithModelUnderBelowBreakEvenCount,
                selectedUnderWithModelOverAboveModelUnderCount = predictionStageSummary.SelectedUnderWithModelOverAboveModelUnderCount,
                selectedUnderWithProjectionAboveLineCount = predictionStageSummary.SelectedUnderWithProjectionAboveLineCount,
                possibleProbabilityOrientationIssueCount = predictionStageSummary.PossibleProbabilityOrientationIssueCount,
                overEligibleButPassedCount = predictionStageSummary.OverEligibleButPassedCount,
                passReasonMissingCount = predictionStageSummary.PassReasonMissingCount,
                overSelections,
                underSelections,
                missingSideSelections,
                overSelectionRate = CalculateRate(overSelections, totalRowsEvaluated),
                underSelectionRate = CalculateRate(underSelections, totalRowsEvaluated),
                rowsWithModelProbability = rowSummaries.Count(row => row.ModelProbabilityOver.HasValue || row.ModelProbabilityUnder.HasValue),
                rowsWithModelOverProbability = rowSummaries.Count(row => row.ModelProbabilityOver.HasValue),
                rowsWithModelUnderProbability = rowSummaries.Count(row => row.ModelProbabilityUnder.HasValue),
                suspiciousUnderCount,
                suspiciousOverCount,
                possibleSideInversionCount,
                possibleEvProbabilityMismatchCount,
                missingSideDefaultedCount,
                sideLabelChangedCount,
                sideDistributionByStage = new
                {
                    predictions = predictionStage,
                    recommendationSnapshots = recommendationStage,
                    topPicks = topPickStage,
                    lineShoppingGroups = lineShoppingStage,
                    publicResponseRows = lineShoppingStage,
                    performanceLedgerRows = recommendationStage
                },
                samples,
                selectedUnderSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SelectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)), shouldIncludeSamples, requestedMaxSamples),
                passSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SelectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)), shouldIncludeSamples, requestedMaxSamples),
                selectedOverSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)), shouldIncludeSamples, requestedMaxSamples),
                actualSelectedOverSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)), shouldIncludeSamples, requestedMaxSamples),
                overEligibleButPassedSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SelectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase) && row.OverCandidateAfterGates.IncludedAfterProductionGates), shouldIncludeSamples, requestedMaxSamples),
                passReasonMissingSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("PassReasonMissing")), shouldIncludeSamples, requestedMaxSamples),
                overPositivePassSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("OverEvPositiveButPassed")), shouldIncludeSamples, requestedMaxSamples),
                overBeatsUnderButPassSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("OverEvGreaterThanUnderEvButPassed")), shouldIncludeSamples, requestedMaxSamples),
                projectionAboveLinePassSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("ProjectionAboveLineButPassed")), shouldIncludeSamples, requestedMaxSamples),
                rejectedByMinProbabilitySamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.FailedGates.Contains("MinProbability")), shouldIncludeSamples, requestedMaxSamples),
                rejectedByHitProbabilitySamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.FailedGates.Contains("MinProbability")), shouldIncludeSamples, requestedMaxSamples),
                positiveEdgeRejectedByHitProbabilitySamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("PositiveEdgeRejectedByHitProbability")), shouldIncludeSamples, requestedMaxSamples),
                passesHitProbabilityButFailsProjectionEdgeSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("PassesHitProbabilityButFailsProjectionEdge")), shouldIncludeSamples, requestedMaxSamples),
                passesProjectionEdgeButFailsHitProbabilitySamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("PassesProjectionEdgeButFailsHitProbability")), shouldIncludeSamples, requestedMaxSamples),
                rejectedByMinEdgeSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.FailedGates.Contains("GenerationProjectionEdgeThreshold")), shouldIncludeSamples, requestedMaxSamples),
                rejectedByMinEvSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.FailedGates.Contains("MinExpectedValue")), shouldIncludeSamples, requestedMaxSamples),
                rejectedByTrustSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.FailedGates.Contains("TrustLevel")), shouldIncludeSamples, requestedMaxSamples),
                rejectedBySampleSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.FailedGates.Contains("SampleSize")), shouldIncludeSamples, requestedMaxSamples),
                rejectedByUnknownGateSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.FailedGates.Contains("UnknownGate") || row.SuspiciousReasons.Contains("PassReasonMissing")), shouldIncludeSamples, requestedMaxSamples),
                projectionAboveLineButSelectedUnderSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("ProjectionAboveLineButSelectedUnder")), shouldIncludeSamples, requestedMaxSamples),
                overEvPositiveButPassedSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("OverEvPositiveButPassed")), shouldIncludeSamples, requestedMaxSamples),
                overEvPositiveButSelectedUnderSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.SuspiciousReasons.Contains("OverEvPositiveButSelectedUnder")), shouldIncludeSamples, requestedMaxSamples),
                highestOverEvSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.OverEstimatedEv.HasValue).OrderByDescending(row => row.OverEstimatedEv), shouldIncludeSamples, requestedMaxSamples),
                highestUnderEvSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.UnderEstimatedEv.HasValue).OrderByDescending(row => row.UnderEstimatedEv), shouldIncludeSamples, requestedMaxSamples),
                rowsWhereOverProbabilityGreaterThanUnderSamples = BuildPredictionSamples(predictionAuditRows.Where(row => row.ModelOverProbability > row.ModelUnderProbability), shouldIncludeSamples, requestedMaxSamples),
                recommendedNextAction = BuildSideSelectionAuditRecommendation(
                    totalRowsEvaluated,
                    predictionStageSummary,
                    possibleSideInversionCount,
                    possibleEvProbabilityMismatchCount,
                    missingSideDefaultedCount,
                    sideLabelChangedCount,
                    underSelections,
                    overSelections)
            });
        })
        .WithName("MlbSideSelectionAuditDebug")
        .WithSummary("Read-only audit of MLB recommendation side selection from persisted prediction, snapshot, and ledger rows.");

        endpoints.MapPost("/api/debug/predictions/sample", async (
            IPredictionRepository predictionRepository,
            CancellationToken cancellationToken) =>
        {
            var run = await predictionRepository.CreateSamplePredictionsAsync(cancellationToken);

            return Results.Ok(new
            {
                run = ToRunResponse(run),
                predictions = run.Predictions.Select(ToPredictionResponse).ToArray()
            });
        })
        .WithName("SamplePredictionsDebug")
        .WithSummary("Creates a sample prediction run with fake predictions for up to five existing props.");

        endpoints.MapPost("/api/debug/predictions/generate/mlb", async (
            IPredictionGenerationService generationService,
            CancellationToken cancellationToken) =>
        {
            var result = await generationService.GenerateMlbBaselinePredictionsAsync(cancellationToken);

            return Results.Ok(ToPredictionGenerationResponse(result));
        })
        .WithName("GenerateMlbBaselinePredictionsDebug")
        .WithSummary("Generates and persists baseline weighted-average MLB predictions.");

        endpoints.MapPost("/api/debug/predictions/generate/mlb/the-odds", async (
            string? eventId,
            string? bookmakers,
            string? markets,
            IPredictionGenerationService generationService,
            CancellationToken cancellationToken) =>
        {
            var result = await generationService.GenerateTheOddsMlbBaselinePredictionsAsync(
                new TheOddsMlbPredictionGenerationRequest(eventId, bookmakers, markets),
                cancellationToken);

            return Results.Ok(ToPredictionGenerationResponse(result));
        })
        .WithName("GenerateTheOddsMlbBaselinePredictionsDebug")
        .WithSummary("Generates and persists baseline weighted-average MLB predictions for fresh The Odds API sourced markets.");

        endpoints.MapGet("/api/debug/predictions/run/{runId:guid}", async (
            Guid runId,
            IPredictionRepository predictionRepository,
            CancellationToken cancellationToken) =>
        {
            var run = await predictionRepository.GetRunByIdAsync(runId, cancellationToken);
            if (run is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var predictions = run.Predictions
                .OrderBy(prediction => prediction.Prop?.Player?.DisplayName)
                .ThenBy(prediction => prediction.Prop?.PropType?.Name)
                .ThenBy(prediction => prediction.PropId)
                .ToArray();
            var averageConfidence = predictions.Length == 0
                ? 0m
                : Math.Round(predictions.Average(prediction => prediction.Confidence), 4);

            return Results.Ok(new
            {
                runId = run.Id,
                predictionCount = predictions.Length,
                averageConfidence,
                predictions = predictions.Select(ToPredictionResponse).ToArray()
            });
        })
        .WithName("PredictionRunDetailDebug")
        .WithSummary("Returns prediction details for one prediction run.");

        endpoints.MapGet("/api/debug/predictions/run/{runId:guid}/summary", async (
            Guid runId,
            IPredictionRepository predictionRepository,
            CancellationToken cancellationToken) =>
        {
            var run = await predictionRepository.GetRunByIdAsync(runId, cancellationToken);
            if (run is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var predictions = run.Predictions.ToArray();
            var predictionsWithEdge = predictions.Where(prediction => prediction.Edge.HasValue).ToArray();
            var byPropType = predictions
                .GroupBy(prediction => prediction.Prop?.PropType?.Name ?? "Unknown")
                .OrderBy(group => group.Key)
                .Select(group => new
                {
                    propType = group.Key,
                    count = group.Count(),
                    averagePrediction = AverageOrZero(group.Select(prediction => prediction.PredictedValue)),
                    averageLine = AverageOrZero(group.Select(prediction => prediction.SportsbookLine)),
                    averageEdge = AverageOrZero(group.Select(prediction => prediction.Edge))
                })
                .ToArray();

            return Results.Ok(new
            {
                runId = run.Id,
                modelName = run.ModelName,
                modelVersion = run.ModelVersion,
                predictionsGenerated = predictions.Length,
                overCount = predictions.Count(prediction => prediction.Recommendation == "OVER"),
                underCount = predictions.Count(prediction => prediction.Recommendation == "UNDER"),
                passCount = predictions.Count(prediction => prediction.Recommendation == "PASS"),
                averageEdge = predictionsWithEdge.Length == 0 ? 0m : Math.Round(predictionsWithEdge.Average(prediction => prediction.Edge!.Value), 4),
                maxEdge = predictionsWithEdge.Length == 0 ? 0m : predictionsWithEdge.Max(prediction => prediction.Edge!.Value),
                minEdge = predictionsWithEdge.Length == 0 ? 0m : predictionsWithEdge.Min(prediction => prediction.Edge!.Value),
                byPropType
            });
        })
        .WithName("PredictionRunSummaryDebug")
        .WithSummary("Returns recommendation and edge summary statistics for one prediction run.");

        endpoints.MapGet("/api/debug/predictions/run/{runId:guid}/skips", async (
            Guid runId,
            IPredictionRepository predictionRepository,
            CancellationToken cancellationToken) =>
        {
            var run = await predictionRepository.GetRunByIdAsync(runId, cancellationToken);
            if (run is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var skipCounts = run.SkipCounts.ToDictionary(skip => skip.Reason, skip => skip.Count, StringComparer.OrdinalIgnoreCase);
            var hardSkippedTotal = GetSkipCount(skipCounts, "NoHistoricalResults")
                + GetSkipCount(skipCounts, "NoSportsbookLine")
                + GetSkipCount(skipCounts, "NoPropType")
                + GetSkipCount(skipCounts, "InsufficientHistory");

            return Results.Ok(new
            {
                propsEvaluated = run.Predictions.Count + hardSkippedTotal,
                predictionsGenerated = run.Predictions.Count,
                skipped = new
                {
                    NoHistoricalResults = GetSkipCount(skipCounts, "NoHistoricalResults"),
                    NoSportsbookLine = GetSkipCount(skipCounts, "NoSportsbookLine"),
                    NoPropType = GetSkipCount(skipCounts, "NoPropType"),
                    InsufficientHistory = GetSkipCount(skipCounts, "InsufficientHistory")
                },
                suppressed = new
                {
                    RecommendationSuppressedByThreshold = GetSkipCount(skipCounts, "RecommendationSuppressedByThreshold"),
                    RecommendationSuppressedByPropTypeFilter = GetSkipCount(skipCounts, "RecommendationSuppressedByPropTypeFilter")
                }
            });
        })
        .WithName("PredictionRunSkipsDebug")
        .WithSummary("Returns skip reason counts for one prediction run.");

        endpoints.MapGet("/api/debug/predictions/backtest", async (
            Guid? runId,
            ApplicationDbContext dbContext,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var selectedRun = runId.HasValue
                ? await dbContext.PredictionRuns
                    .AsNoTracking()
                    .SingleOrDefaultAsync(run => run.Id == runId.Value, cancellationToken)
                : await dbContext.PredictionRuns
                    .AsNoTracking()
                    .Where(run => run.ModelName == "BaselineAverageModel")
                    .OrderByDescending(run => run.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

            if (selectedRun is null)
            {
                return runId.HasValue
                    ? Results.NotFound(new { error = $"Prediction run '{runId}' was not found." })
                    : Results.NotFound(new { error = "No prediction run was found." });
            }

            var predictions = await LoadBacktestPredictionsAsync(dbContext, selectedRun.Id, cancellationToken);
            var actualResults = await LoadBacktestActualResultsAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var gradedPredictions = new List<BacktestPredictionResult>();
            var ungradedPredictions = 0;
            var aliasCache = new Dictionary<Guid, Guid[]>();

            foreach (var prediction in predictions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var equivalentPlayerIds = await GetBacktestEquivalentPlayerIdsAsync(
                    prediction.PlayerId,
                    prediction.LeagueCode,
                    playerIdentityAliasService,
                    aliasCache,
                    cancellationToken);
                var equivalentPropTypeIds = GetEquivalentPropTypeIds(equivalentPropTypeIdsById, prediction.PropTypeId);
                var actualResult = FindBacktestActualResult(
                    actualResults,
                    prediction,
                    equivalentPlayerIds,
                    equivalentPropTypeIds);

                if (actualResult is null)
                {
                    ungradedPredictions++;
                    continue;
                }

                gradedPredictions.Add(new BacktestPredictionResult(
                    prediction.PredictionId,
                    prediction.PropType,
                    prediction.Recommendation,
                    prediction.Confidence,
                    prediction.Edge,
                    prediction.SportsbookLine,
                    prediction.PredictedValue,
                    actualResult.Value,
                    GradePrediction(prediction.Recommendation, prediction.SportsbookLine, actualResult.Value)));
            }

            return Results.Ok(new
            {
                runId = selectedRun.Id,
                selectedRun.ModelName,
                selectedRun.ModelVersion,
                selectedRun.CreatedAt,
                predictionsEvaluated = predictions.Count,
                gradedPredictions = gradedPredictions.Count,
                ungradedPredictions,
                overall = BuildBacktestSummary(gradedPredictions),
                byPropType = BuildBacktestGroups(gradedPredictions, prediction => prediction.PropType)
                    .OrderByDescending(group => group.GradedPredictions)
                    .ThenBy(group => group.Key)
                    .ToArray(),
                byRecommendation = BuildBacktestGroups(gradedPredictions, prediction => prediction.Recommendation)
                    .OrderBy(group => group.Key)
                    .ToArray(),
                byEdgeBucket = BuildBacktestGroups(gradedPredictions, prediction => GetEdgeBucket(prediction.Edge))
                    .OrderBy(group => EdgeBucketSortOrder(group.Key))
                    .ToArray(),
                byConfidenceBucket = BuildBacktestGroups(gradedPredictions, prediction => GetConfidenceBucket(prediction.Confidence))
                    .OrderBy(group => ConfidenceBucketSortOrder(group.Key))
                    .ToArray()
            });
        })
        .WithName("MlbPredictionBacktestDebug")
        .WithSummary("Backtests persisted MLB recommendations against imported prop_results outcomes.");

        endpoints.MapGet("/api/debug/predictions/mlb-quality-analysis", async (
            Guid? runId,
            ApplicationDbContext dbContext,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var selectedRun = runId.HasValue
                ? await dbContext.PredictionRuns
                    .AsNoTracking()
                    .SingleOrDefaultAsync(run => run.Id == runId.Value, cancellationToken)
                : await dbContext.PredictionRuns
                    .AsNoTracking()
                    .Where(run => run.ModelName == "BaselineAverageModel")
                    .OrderByDescending(run => run.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

            if (selectedRun is null)
            {
                return runId.HasValue
                    ? Results.NotFound(new { error = $"Prediction run '{runId}' was not found." })
                    : Results.NotFound(new { error = "No prediction run was found." });
            }

            var predictions = await LoadBacktestPredictionsAsync(dbContext, selectedRun.Id, cancellationToken);
            var actualResults = await LoadBacktestActualResultsAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var aliasCache = new Dictionary<Guid, Guid[]>();
            var resultKeysByPlayerProp = actualResults.Keys
                .GroupBy(key => new PlayerPropTypeKey(key.PlayerId, key.PropTypeId))
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(key => key.EventDate).ToHashSet());
            var playersWithResults = actualResults.Keys
                .Select(key => key.PlayerId)
                .ToHashSet();
            var propTypesWithResults = actualResults.Keys
                .Select(key => key.PropTypeId)
                .ToHashSet();
            var gradedPredictions = new List<BacktestPredictionResult>();
            var ungradedRows = new List<UngradedPredictionAnalysis>();
            var totalCandidateMatches = 0;
            var duplicateCandidatePredictions = 0;
            var duplicateDistinctOutcomePredictions = 0;

            foreach (var prediction in predictions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var equivalentPlayerIds = await GetBacktestEquivalentPlayerIdsAsync(
                    prediction.PlayerId,
                    prediction.LeagueCode,
                    playerIdentityAliasService,
                    aliasCache,
                    cancellationToken);
                var equivalentPropTypeIds = GetEquivalentPropTypeIds(equivalentPropTypeIdsById, prediction.PropTypeId);
                var candidateMatches = FindBacktestActualResultCandidates(
                    actualResults,
                    prediction,
                    equivalentPlayerIds,
                    equivalentPropTypeIds);

                if (candidateMatches.Length > 1)
                {
                    duplicateCandidatePredictions++;
                }

                if (candidateMatches.Select(match => match.ActualResult).Distinct().Count() > 1)
                {
                    duplicateDistinctOutcomePredictions++;
                }

                totalCandidateMatches += candidateMatches.Length;
                var actualResult = candidateMatches.FirstOrDefault();

                if (actualResult is null)
                {
                    ungradedRows.Add(BuildUngradedPredictionAnalysis(
                        prediction,
                        equivalentPlayerIds,
                        equivalentPropTypeIds,
                        resultKeysByPlayerProp,
                        playersWithResults,
                        propTypesWithResults));
                    continue;
                }

                gradedPredictions.Add(new BacktestPredictionResult(
                    prediction.PredictionId,
                    prediction.PropType,
                    prediction.Recommendation,
                    prediction.Confidence,
                    prediction.Edge,
                    prediction.SportsbookLine,
                    prediction.PredictedValue,
                    actualResult.ActualResult,
                    GradePrediction(prediction.Recommendation, prediction.SportsbookLine, actualResult.ActualResult)));
            }

            var underRows = gradedPredictions
                .Where(prediction => prediction.Recommendation == "UNDER")
                .ToArray();
            var overRows = gradedPredictions
                .Where(prediction => prediction.Recommendation == "OVER")
                .ToArray();

            return Results.Ok(new
            {
                runId = selectedRun.Id,
                selectedRun.ModelName,
                selectedRun.ModelVersion,
                selectedRun.CreatedAt,
                predictionsEvaluated = predictions.Count,
                gradedPredictions = gradedPredictions.Count,
                ungradedPredictions = ungradedRows.Count,
                duplicateMatching = new
                {
                    totalCandidateMatches,
                    duplicateCandidatePredictions,
                    duplicateDistinctOutcomePredictions
                },
                ungradedReasons = ungradedRows
                    .GroupBy(row => row.Reason, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        reason = group.Key,
                        count = group.Count(),
                        distinctPlayers = group.Select(row => row.PlayerId).Distinct().Count(),
                        distinctPropTypes = group.Select(row => row.PropTypeId).Distinct().Count(),
                        estimatedRecoverable = EstimateUngradedRecoverability(group.Key, group.Count()),
                        topPropTypes = group
                            .GroupBy(row => row.PropType, StringComparer.OrdinalIgnoreCase)
                            .Select(propTypeGroup => new
                            {
                                propType = propTypeGroup.Key,
                                count = propTypeGroup.Count()
                            })
                            .OrderByDescending(row => row.count)
                            .ThenBy(row => row.propType)
                            .Take(10)
                            .ToArray()
                    })
                    .OrderByDescending(row => row.count)
                    .ThenBy(row => row.reason)
                    .ToArray(),
                recommendationSummary = BuildQualityRecommendationSummary(gradedPredictions),
                underDominance = new
                {
                    underCount = underRows.Length,
                    overCount = overRows.Length,
                    underWinCount = underRows.Count(row => row.Outcome == "Win"),
                    overWinCount = overRows.Count(row => row.Outcome == "Win"),
                    underHitRate = BuildBacktestSummary(underRows).HitRate,
                    overHitRate = BuildBacktestSummary(overRows).HitRate,
                    averageUnderLine = AverageDecimal(underRows.Select(row => (decimal?)row.SportsbookLine)),
                    averageUnderActual = AverageDecimal(underRows.Select(row => (decimal?)row.ActualResult)),
                    averageUnderPrediction = AverageDecimal(underRows.Select(row => row.PredictedValue)),
                    averageOverLine = AverageDecimal(overRows.Select(row => (decimal?)row.SportsbookLine)),
                    averageOverActual = AverageDecimal(overRows.Select(row => (decimal?)row.ActualResult)),
                    averageOverPrediction = AverageDecimal(overRows.Select(row => row.PredictedValue))
                },
                propTypeBias = gradedPredictions
                    .Where(row => row.Recommendation is "OVER" or "UNDER")
                    .GroupBy(row => new { row.PropType, row.Recommendation })
                    .Select(group =>
                    {
                        var summary = BuildBacktestSummary(group.ToArray());

                        return new
                        {
                            group.Key.PropType,
                            group.Key.Recommendation,
                            count = group.Count(),
                            summary.Wins,
                            summary.Losses,
                            summary.Pushes,
                            summary.HitRate,
                            averageSportsbookLine = AverageDecimal(group.Select(row => (decimal?)row.SportsbookLine)),
                            averageActualResult = AverageDecimal(group.Select(row => (decimal?)row.ActualResult)),
                            averageModelPrediction = AverageDecimal(group.Select(row => row.PredictedValue)),
                            averageEdge = AverageDecimal(group.Select(row => row.Edge))
                        };
                    })
                    .OrderByDescending(row => row.Wins)
                    .ThenByDescending(row => row.count)
                    .ThenBy(row => row.PropType)
                    .Take(50)
                    .ToArray(),
                improvementPriorities = new[]
                {
                    new
                    {
                        priority = 1,
                        recommendation = "Add sportsbook-line calibration to the baseline model.",
                        evidence = "UNDER predictions have a much higher hit rate than OVER predictions when the model is below the sportsbook line. This points to calibration against the line, not just more raw stat averaging.",
                        expectedImpact = "High",
                        complexity = "Low-Medium"
                    },
                    new
                    {
                        priority = 2,
                        recommendation = "Add game-context splits: probable pitcher handedness, starter quality, park, and batting order when available.",
                        evidence = "Current model uses only player historical averages, so it cannot separate favorable and unfavorable game contexts that move MLB props.",
                        expectedImpact = "High",
                        complexity = "Medium-High"
                    },
                    new
                    {
                        priority = 3,
                        recommendation = "Add home/away and opponent strength features.",
                        evidence = "These are stable MLB context features that can explain systematic deviation between season averages and actual game outcomes.",
                        expectedImpact = "Medium-High",
                        complexity = "Medium"
                    },
                    new
                    {
                        priority = 4,
                        recommendation = "Replace fixed 50/30/20 averaging with validated recency weights per prop type.",
                        evidence = "The current formula assumes the same recency profile for all stat types and players.",
                        expectedImpact = "Medium",
                        complexity = "Low"
                    },
                    new
                    {
                        priority = 5,
                        recommendation = "Use rolling variance and recent playing-time stability as confidence modifiers.",
                        evidence = "Variance already exists as a feature but is not used in prediction generation or recommendation filtering.",
                        expectedImpact = "Medium",
                        complexity = "Low"
                    }
                }
            });
        })
        .WithName("MlbPredictionQualityAnalysisDebug")
        .WithSummary("Returns a comprehensive read-only quality analysis for the latest MLB prediction backtest.");

        endpoints.MapGet("/api/debug/predictions/edge-analysis", async (
            Guid? runId,
            ApplicationDbContext dbContext,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var selectedRun = runId.HasValue
                ? await dbContext.PredictionRuns
                    .AsNoTracking()
                    .SingleOrDefaultAsync(run => run.Id == runId.Value, cancellationToken)
                : await dbContext.PredictionRuns
                    .AsNoTracking()
                    .Where(run => run.ModelName == "BaselineAverageModel")
                    .OrderByDescending(run => run.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

            if (selectedRun is null)
            {
                return runId.HasValue
                    ? Results.NotFound(new { error = $"Prediction run '{runId}' was not found." })
                    : Results.NotFound(new { error = "No prediction run was found." });
            }

            var predictions = await LoadBacktestPredictionsAsync(dbContext, selectedRun.Id, cancellationToken);
            var actualResults = await LoadBacktestActualResultsAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var aliasCache = new Dictionary<Guid, Guid[]>();
            var gradedPredictions = new List<BacktestPredictionResult>();
            var ungradedPredictions = 0;

            foreach (var prediction in predictions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var equivalentPlayerIds = await GetBacktestEquivalentPlayerIdsAsync(
                    prediction.PlayerId,
                    prediction.LeagueCode,
                    playerIdentityAliasService,
                    aliasCache,
                    cancellationToken);
                var equivalentPropTypeIds = GetEquivalentPropTypeIds(equivalentPropTypeIdsById, prediction.PropTypeId);
                var actualResult = FindBacktestActualResult(
                    actualResults,
                    prediction,
                    equivalentPlayerIds,
                    equivalentPropTypeIds);

                if (actualResult is null)
                {
                    ungradedPredictions++;
                    continue;
                }

                gradedPredictions.Add(new BacktestPredictionResult(
                    prediction.PredictionId,
                    prediction.PropType,
                    prediction.Recommendation,
                    prediction.Confidence,
                    prediction.Edge,
                    prediction.SportsbookLine,
                    prediction.PredictedValue,
                    actualResult.Value,
                    GradePrediction(prediction.Recommendation, prediction.SportsbookLine, actualResult.Value)));
            }

            var placedPredictions = gradedPredictions
                .Where(prediction => prediction.Recommendation is "OVER" or "UNDER")
                .ToArray();
            var edgeBuckets = gradedPredictions
                .Where(prediction => prediction.Edge.HasValue)
                .GroupBy(prediction => GetDetailedEdgeBucket(prediction.Edge!.Value), StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildEdgeBucketAnalysis(group.Key, group.ToArray()))
                .OrderBy(bucket => DetailedEdgeBucketSortOrder(bucket.EdgeBucket))
                .ToArray();
            var confidenceBuckets = gradedPredictions
                .GroupBy(prediction => GetConfidenceDecileBucket(prediction.Confidence), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var summary = BuildBacktestSummary(group.ToArray());

                    return new
                    {
                        confidenceBucket = group.Key,
                        count = group.Count(),
                        summary.Wins,
                        summary.Losses,
                        summary.Pushes,
                        summary.HitRate
                    };
                })
                .OrderBy(bucket => ConfidenceDecileBucketSortOrder(bucket.confidenceBucket))
                .ToArray();
            var propTypeStats = gradedPredictions
                .Where(prediction => prediction.Recommendation is "OVER" or "UNDER")
                .GroupBy(prediction => prediction.PropType, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var summary = BuildBacktestSummary(group.ToArray());

                    return new
                    {
                        propType = group.Key,
                        count = group.Count(),
                        summary.Wins,
                        summary.Losses,
                        summary.Pushes,
                        summary.HitRate,
                        overCount = group.Count(prediction => prediction.Recommendation == "OVER"),
                        underCount = group.Count(prediction => prediction.Recommendation == "UNDER"),
                        averageEdge = AverageDecimal(group.Select(prediction => prediction.Edge)),
                        averageSportsbookLine = AverageDecimal(group.Select(prediction => (decimal?)prediction.SportsbookLine)),
                        averageActualResult = AverageDecimal(group.Select(prediction => (decimal?)prediction.ActualResult)),
                        averageModelPrediction = AverageDecimal(group.Select(prediction => prediction.PredictedValue))
                    };
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.propType)
                .ToArray();
            var thresholdAnalysis = new[] { 0.25m, 0.50m, 0.75m, 1.00m, 1.25m }
                .Select(threshold => BuildThresholdSimulation(threshold, gradedPredictions))
                .ToArray();

            return Results.Ok(new
            {
                runId = selectedRun.Id,
                selectedRun.ModelName,
                selectedRun.ModelVersion,
                selectedRun.CreatedAt,
                predictionsEvaluated = predictions.Count,
                gradedPredictions = gradedPredictions.Count,
                ungradedPredictions,
                placedPredictions = placedPredictions.Length,
                overallPlaced = BuildBacktestSummary(placedPredictions),
                edgeBuckets,
                confidenceBuckets,
                propTypes = new
                {
                    hitRateByPropType = propTypeStats,
                    topWinningPropTypes = propTypeStats
                        .Where(row => row.count >= 10)
                        .OrderByDescending(row => row.HitRate)
                        .ThenByDescending(row => row.count)
                        .Take(10)
                        .ToArray(),
                    worstPropTypes = propTypeStats
                        .Where(row => row.count >= 10)
                        .OrderBy(row => row.HitRate)
                        .ThenByDescending(row => row.count)
                        .Take(10)
                        .ToArray()
                },
                thresholdAnalysis,
                interpretationHints = new
                {
                    edgeQuestion = "Compare edgeBuckets hitRate as edge magnitude grows. Split OVER/UNDER stats show whether positive and negative edge behave differently.",
                    confidenceQuestion = "Confidence is meaningful only if confidenceBuckets hitRate generally increases with bucket.",
                    thresholdQuestion = "Use thresholdAnalysis to compare fewer recommendations versus higher hit rate.",
                    passHandling = "PASS rows are included in graded prediction counts where applicable but are excluded from placed prediction summaries."
                }
            });
        })
        .WithName("MlbPredictionEdgeAnalysisDebug")
        .WithSummary("Analyzes whether persisted sportsbook edge is predictive for graded MLB predictions.");

        endpoints.MapGet("/api/debug/predictions/no-historical-results", async (
            Guid? runId,
            int? limit,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var selectedRun = runId.HasValue
                ? await dbContext.PredictionRuns
                    .AsNoTracking()
                    .Include(run => run.SkipCounts)
                    .SingleOrDefaultAsync(run => run.Id == runId.Value, cancellationToken)
                : await dbContext.PredictionRuns
                    .AsNoTracking()
                    .Include(run => run.SkipCounts)
                    .Where(run => run.ModelName == "BaselineAverageModel")
                    .OrderByDescending(run => run.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

            if (runId.HasValue && selectedRun is null)
            {
                return Results.NotFound(new { error = $"Prediction run '{runId}' was not found." });
            }

            var reportLimit = Math.Clamp(limit ?? 100, 1, 500);
            var skippedProps = await ReconstructNoHistoricalResultSkipsAsync(dbContext, cancellationToken);
            var duplicateGroups = await LoadDuplicateIdentityGroupsAsync(dbContext, cancellationToken);
            var duplicateGroupByPlayerId = duplicateGroups
                .SelectMany(group => group.Players.Select(player => new
                {
                    player.PlayerId,
                    Group = group
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Group);
            var historicalResults = await LoadHistoricalResultLookupAsync(dbContext, cancellationToken);
            var recoverablePropIds = skippedProps
                .Where(prop => HasDifferentCanonicalHistoricalResult(prop, duplicateGroupByPlayerId, historicalResults))
                .Select(prop => prop.PropId)
                .Distinct()
                .ToHashSet();
            var playerReports = skippedProps
                .GroupBy(prop => prop.PlayerId)
                .Select(group => BuildNoHistoricalResultPlayerReport(
                    group.Key,
                    group.ToArray(),
                    duplicateGroupByPlayerId,
                    historicalResults,
                    recoverablePropIds))
                .OrderByDescending(report => report.PropCountSkipped)
                .ThenByDescending(report => report.RecoverablePropCountAfterDuplicateMerge)
                .ThenBy(report => report.PlayerName)
                .ToArray();
            var persistedNoHistoricalResults = selectedRun?.SkipCounts
                .SingleOrDefault(skip => string.Equals(skip.Reason, "NoHistoricalResults", StringComparison.OrdinalIgnoreCase))
                ?.Count;

            return Results.Ok(new
            {
                runId = selectedRun?.Id,
                modelName = selectedRun?.ModelName,
                modelVersion = selectedRun?.ModelVersion,
                runCreatedAt = selectedRun?.CreatedAt,
                diagnosticMethod = "Read-only reconstruction of the current MLB prediction generation gates: MLB prop, prop type present, latest platform line present, and zero prior prop_results for player_id + prop_type_id before the prop event date.",
                persistedNoHistoricalResultsForRun = persistedNoHistoricalResults,
                reconstructedNoHistoricalResults = skippedProps.Count,
                playersWithNoHistoricalResults = playerReports.Length,
                playersInDuplicateIdentityGroups = playerReports.Count(report => report.BelongsToDuplicateIdentityGroup),
                playersWithHistoricalResultsUnderDifferentCanonicalPlayer = playerReports.Count(report => report.HistoricalPropResultsExistUnderDifferentCanonicalPlayerId),
                playersWithAnotherDuplicateHavingMlbMapping = playerReports.Count(report => report.AnotherPlayerInDuplicateGroupHasMlbMapping),
                estimatedAdditionalPredictionsAfterFullDuplicateReconciliation = recoverablePropIds.Count,
                topPlayers = playerReports.Take(reportLimit).ToArray()
            });
        })
        .WithName("NoHistoricalResultsPredictionDiagnosticsDebug")
        .WithSummary("Ranks MLB prediction skips caused by missing historical results and estimates duplicate-merge recoverability.");

        endpoints.MapGet("/api/debug/predictions/player-history/{playerId:guid}", async (
            Guid playerId,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var player = await dbContext.Players
                .AsNoTracking()
                .Include(player => player.League)
                .Include(player => player.ExternalProviderRefs)
                .SingleOrDefaultAsync(player => player.Id == playerId, cancellationToken);

            if (player is null)
            {
                return Results.NotFound(new { error = $"Player '{playerId}' was not found." });
            }

            var providerRefs = BuildCombinedProviderRefs(player.Id, player.DisplayName, player.ExternalProviderRefs, player.ExternalRefs);
            var duplicateGroups = await LoadDuplicateIdentityGroupsAsync(dbContext, cancellationToken);
            var duplicateGroup = duplicateGroups.SingleOrDefault(group => group.Players.Any(groupPlayer => groupPlayer.PlayerId == playerId));
            var duplicatePlayers = duplicateGroup?.Players ?? [ToPlayerIdentityRow(player)];
            var duplicatePlayerIds = duplicatePlayers.Select(groupPlayer => groupPlayer.PlayerId).Distinct().ToArray();
            var requestedHistory = await LoadPlayerHistoricalResultSummaryAsync(dbContext, playerId, cancellationToken);
            var duplicatePlayerHistory = await LoadDuplicatePlayerHistoryAsync(dbContext, duplicatePlayerIds, cancellationToken);
            var duplicatePlayerResultCount = duplicatePlayerHistory
                .Where(history => history.PlayerId != playerId)
                .Sum(history => history.HistoricalResultCount);
            var predictionGateDiagnostics = await LoadPlayerPredictionGateDiagnosticsAsync(dbContext, playerId, cancellationToken);

            return Results.Ok(new
            {
                playerId = player.Id,
                playerName = player.DisplayName,
                league = player.League?.Code ?? "UNKNOWN",
                canonicalLeague = player.League is null ? "UNKNOWN" : CanonicalLeagueNormalizer.NormalizeCode(player.League.Code),
                providerRefs,
                duplicateIdentityGroup = duplicatePlayers
                    .OrderBy(groupPlayer => groupPlayer.PlayerId == playerId ? 0 : 1)
                    .ThenBy(groupPlayer => groupPlayer.DisplayName)
                    .Select(groupPlayer => new
                    {
                        groupPlayer.PlayerId,
                        playerName = groupPlayer.DisplayName,
                        leagueCode = groupPlayer.LeagueCode,
                        canonicalLeague = groupPlayer.CanonicalLeagueCode,
                        isRequestedPlayer = groupPlayer.PlayerId == playerId,
                        providerRefs = groupPlayer.ProviderRefs,
                        legacyProviderRefs = groupPlayer.LegacyProviderRefs
                    })
                    .ToArray(),
                historicalResults = requestedHistory,
                duplicatePlayerHistory,
                predictionEligibility = new
                {
                    hasAnyHistoricalResults = requestedHistory.TotalCount > 0,
                    hasDuplicatePlayersWithResults = duplicatePlayerResultCount > 0,
                    duplicatePlayerResultCount,
                    predictionGateDiagnostics.HasCurrentMlbProps,
                    predictionGateDiagnostics.CurrentMlbProps,
                    predictionGateDiagnostics.CurrentMlbPropsWithPriorHistory,
                    predictionGateDiagnostics.CurrentMlbPropsWithoutPriorHistory,
                    predictionGateDiagnostics.PropsWithoutPriorHistoryByPropType
                }
            });
        })
        .WithName("PlayerHistoryPredictionDiagnosticsDebug")
        .WithSummary("Returns read-only player history diagnostics for MLB prediction eligibility.");

        endpoints.MapGet("/api/debug/predictions/skip-analysis", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var skippedProps = await ReconstructNoHistoricalResultSkipsAsync(dbContext, cancellationToken);
            var eligiblePredictionCount = await CountCurrentlyEligibleMlbPredictionsAsync(dbContext, cancellationToken);
            var historicalResultCountsByPropType = await LoadMlbHistoricalResultCountsByPropTypeAsync(dbContext, cancellationToken);
            var totalSkippedProps = skippedProps.Count;
            var skippedByPropType = skippedProps
                .GroupBy(prop => new
                {
                    prop.PropTypeId,
                    prop.PropType
                })
                .Select(group => new SkipAnalysisPropTypeCount(
                    group.Key.PropType,
                    group.Count(),
                    totalSkippedProps == 0 ? 0m : Math.Round(group.Count() * 100m / totalSkippedProps, 2)))
                .OrderByDescending(group => group.SkippedCount)
                .ThenBy(group => group.PropType)
                .Take(25)
                .ToArray();
            var skippedPropTypeCounts = skippedProps
                .GroupBy(prop => new
                {
                    prop.PropTypeId,
                    prop.PropType
                })
                .Select(group => new
                {
                    group.Key.PropTypeId,
                    group.Key.PropType,
                    SkippedCount = group.Count()
                })
                .ToArray();
            var supportedPropTypes = historicalResultCountsByPropType
                .Select(group => new SupportedPropTypeSummary(
                    group.PropType,
                    group.HistoricalResultCount))
                .OrderByDescending(group => group.HistoricalResultCount)
                .ThenBy(group => group.PropType)
                .ToArray();
            var unsupportedPropTypes = skippedPropTypeCounts
                .Where(group => !historicalResultCountsByPropType.Any(result => result.PropTypeId == group.PropTypeId))
                .Select(group => new UnsupportedPropTypeSummary(
                    group.PropType,
                    group.SkippedCount))
                .OrderByDescending(group => group.SkippedCount)
                .ThenBy(group => group.PropType)
                .ToArray();
            var unsupportedSkippedCount = unsupportedPropTypes.Sum(group => group.SkippedCount);
            var projectedDenominator = eligiblePredictionCount + totalSkippedProps - unsupportedSkippedCount;
            var projectedCoveragePercent = projectedDenominator == 0
                ? 0m
                : Math.Round(eligiblePredictionCount * 100m / projectedDenominator, 2);

            return Results.Ok(new
            {
                totalSkippedProps,
                skippedByPropType,
                supportedPropTypes,
                unsupportedPropTypes,
                projectedPredictionsIfUnsupportedTypesWereIgnored = eligiblePredictionCount,
                projectedCoveragePercent
            });
        })
        .WithName("MlbPredictionSkipAnalysisDebug")
        .WithSummary("Analyzes MLB NoHistoricalResults prediction skips by prop type.");

        endpoints.MapGet("/api/debug/predictions/no-history-analysis", async (
            int? limit,
            ApplicationDbContext dbContext,
            IPredictionFeatureService featureService,
            CancellationToken cancellationToken) =>
        {
            var reportLimit = Math.Clamp(limit ?? 100, 1, 500);
            var currentProps = await LoadCurrentMlbPropsWithSportsbookLinesAsync(dbContext, cancellationToken);
            var skippedProps = new List<NoHistoryAnalysisSkippedProp>();
            var insufficientHistoricalGames = 0;

            foreach (var prop in currentProps
                .OrderBy(prop => prop.EventDate)
                .ThenBy(prop => prop.PlayerName)
                .ThenBy(prop => prop.PropType))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var lookup = await featureService.AnalyzeHistoricalLookupAsync(
                    prop.PlayerId,
                    prop.PropTypeId,
                    prop.EventDate,
                    cancellationToken);

                if (lookup.HistoricalGamesAvailable == 0)
                {
                    skippedProps.Add(new NoHistoryAnalysisSkippedProp(
                        prop.PropId,
                        prop.PlayerId,
                        prop.PlayerName,
                        prop.PropTypeId,
                        prop.PropType,
                        prop.EventDate,
                        ClassifyNoHistorySkip(lookup),
                        lookup.EquivalentPlayerIds.Count,
                        lookup.EquivalentPropTypeIds.Count,
                        lookup.HistoricalResultsForEquivalentPropTypes,
                        lookup.HistoricalResultsForEquivalentPlayers,
                        lookup.MlbMappingsForEquivalentPlayers));
                    continue;
                }

                if (lookup.SeasonGamesAvailable == 0)
                {
                    insufficientHistoricalGames++;
                }
            }

            var summary = new[]
            {
                "NoMlbMapping",
                "NoAliasResolution",
                "UnsupportedPropType",
                "NoHistoricalResultsExist",
                "InsufficientHistoricalGames",
                "Other"
            }
                .Select(reason => new
                {
                    reason,
                    count = string.Equals(reason, "InsufficientHistoricalGames", StringComparison.OrdinalIgnoreCase)
                        ? insufficientHistoricalGames
                        : skippedProps.Count(prop => string.Equals(prop.Reason, reason, StringComparison.OrdinalIgnoreCase))
                })
                .ToArray();
            var examples = skippedProps
                .GroupBy(prop => prop.Reason, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(prop => prop.HistoricalResultsForEquivalentPropTypes)
                        .ThenBy(prop => prop.PlayerName)
                        .ThenBy(prop => prop.PropType)
                        .Take(reportLimit)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new
            {
                propsEvaluated = currentProps.Count,
                noHistoricalResults = skippedProps.Count,
                insufficientHistoricalGames,
                summary,
                examples
            });
        })
        .WithName("MlbPredictionNoHistoryAnalysisDebug")
        .WithSummary("Classifies current MLB prediction history skips using PredictionFeatureService lookup rules.");

        endpoints.MapGet("/api/debug/predictions/prop-type-mapping-analysis", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var currentPropTypes = await LoadCurrentMlbSportsbookPropTypesAsync(dbContext, cancellationToken);
            var historicalPropTypes = await LoadMlbHistoricalResultCountsByPropTypeAsync(dbContext, cancellationToken);
            var duplicateHistoricalPropTypes = historicalPropTypes
                .GroupBy(propType => NormalizePropTypeName(propType.PropType), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => new DuplicateHistoricalPropTypeSummary(
                    group.Key,
                    group.Count(),
                    group
                        .OrderByDescending(propType => propType.HistoricalResultCount)
                        .ThenBy(propType => propType.PropType)
                        .Select(propType => new DuplicateHistoricalPropTypeRow(
                            propType.PropTypeId,
                            propType.PropType,
                            propType.HistoricalResultCount))
                        .ToArray()))
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.NormalizedName)
                .ToArray();
            var historicalByNormalizedName = historicalPropTypes
                .GroupBy(propType => NormalizePropTypeName(propType.PropType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(propType => propType.HistoricalResultCount).First(), StringComparer.OrdinalIgnoreCase);
            var analysisRows = currentPropTypes
                .Select(propType => BuildPropTypeMappingAnalysisRow(propType, historicalByNormalizedName))
                .OrderByDescending(row => row.ActivePropCount)
                .ThenBy(row => row.SportsbookPropType)
                .ToArray();
            var suggestedRows = analysisRows
                .Where(row => !row.ExactHistoricalMatch && row.MatchedHistoricalPropType is not null)
                .ToArray();
            var skippedProps = await ReconstructNoHistoricalResultSkipsAsync(dbContext, cancellationToken);
            var historicalResultLookup = await LoadHistoricalResultLookupAsync(dbContext, cancellationToken);
            var historicalIdByName = historicalPropTypes
                .GroupBy(propType => propType.PropType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(propType => propType.HistoricalResultCount)
                        .ThenBy(propType => propType.PropTypeId)
                        .First()
                        .PropTypeId,
                    StringComparer.OrdinalIgnoreCase);
            var suggestedHistoricalPropTypeIdBySportsbookPropType = suggestedRows
                .Where(row => row.MatchedHistoricalPropType is not null && historicalIdByName.ContainsKey(row.MatchedHistoricalPropType))
                .GroupBy(row => row.SportsbookPropType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => historicalIdByName[group
                        .OrderByDescending(row => row.ActivePropCount)
                        .ThenByDescending(row => row.HistoricalResultCount)
                        .First()
                        .MatchedHistoricalPropType!],
                    StringComparer.OrdinalIgnoreCase);
            var projectedAdditionalPredictionsRecoverable = skippedProps
                .Where(prop => suggestedHistoricalPropTypeIdBySportsbookPropType.TryGetValue(prop.PropType, out var suggestedPropTypeId)
                    && HasHistoricalResult(historicalResultLookup, prop.PlayerId, suggestedPropTypeId, prop.EventDate))
                .Select(prop => prop.PropId)
                .Distinct()
                .Count();

            return Results.Ok(new
            {
                exactMatches = analysisRows.Count(row => row.ExactHistoricalMatch),
                suggestedMappings = suggestedRows,
                unmappedPropTypes = analysisRows
                    .Where(row => row.MatchedHistoricalPropType is null)
                    .ToArray(),
                duplicateHistoricalPropTypes,
                projectedAdditionalPredictionsRecoverable,
                propTypes = analysisRows
            });
        })
        .WithName("MlbPropTypeMappingAnalysisDebug")
        .WithSummary("Analyzes likely mappings between sportsbook MLB prop types and historical result prop types.");

        endpoints.MapGet("/api/debug/predictions/coverage-gaps", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var currentProps = await LoadCurrentMlbPropsWithSportsbookLinesAsync(dbContext, cancellationToken);
            var historicalResults = await LoadHistoricalResultLookupAsync(dbContext, cancellationToken);
            var historicalPropTypes = await LoadMlbHistoricalResultCountsByPropTypeAsync(dbContext, cancellationToken);
            var historicalPropTypeIdsByNormalizedName = historicalPropTypes
                .GroupBy(propType => NormalizePropTypeName(propType.PropType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(propType => propType.PropTypeId).Distinct().ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var duplicateGroups = await LoadDuplicateIdentityGroupsAsync(dbContext, cancellationToken);
            var duplicateGroupByPlayerId = duplicateGroups
                .SelectMany(group => group.Players.Select(player => new
                {
                    player.PlayerId,
                    Group = group
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Group);
            var classifications = currentProps
                .Select(prop => ClassifyCoverageGap(
                    prop,
                    historicalResults,
                    historicalPropTypeIdsByNormalizedName,
                    duplicateGroupByPlayerId))
                .ToArray();
            var missingClassifications = classifications
                .Where(classification => !classification.HistoricalMatchFound)
                .ToArray();

            return Results.Ok(new
            {
                totalProps = classifications.Length,
                historicalMatchFound = classifications.Count(classification => classification.HistoricalMatchFound),
                missingPlayerHistory = missingClassifications.Count(classification => classification.RootCause == "MissingPlayerHistory"),
                missingPropTypeHistory = missingClassifications.Count(classification => classification.RootCause == "MissingPropTypeHistory"),
                recoverableByPropTypeAlias = classifications.Count(classification => classification.RecoverableByPropTypeAlias),
                recoverableByDuplicatePlayerMerge = classifications.Count(classification => classification.RecoverableByDuplicatePlayerMerge),
                recoverableByBoth = classifications.Count(classification => classification.RecoverableByBoth),
                topMissingPropTypes = missingClassifications
                    .GroupBy(classification => classification.PropType, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new CoverageGapPropTypeCount(group.Key, group.Count()))
                    .OrderByDescending(group => group.Count)
                    .ThenBy(group => group.PropType)
                    .Take(25)
                    .ToArray(),
                topRecoverableAliases = classifications
                    .Where(classification => classification.RecoverableByPropTypeAlias || classification.RecoverableByBoth)
                    .GroupBy(classification => new
                    {
                        classification.PropType,
                        classification.SuggestedAlias
                    })
                    .Select(group => new CoverageGapAliasCount(
                        group.Key.PropType,
                        group.Key.SuggestedAlias ?? "UNKNOWN",
                        group.Count()))
                    .OrderByDescending(group => group.Count)
                    .ThenBy(group => group.PropType)
                    .Take(25)
                    .ToArray(),
                topRecoverablePlayers = classifications
                    .Where(classification => classification.RecoverableByDuplicatePlayerMerge || classification.RecoverableByBoth)
                    .GroupBy(classification => new
                    {
                        classification.PlayerId,
                        classification.PlayerName
                    })
                    .Select(group => new CoverageGapPlayerCount(
                        group.Key.PlayerId,
                        group.Key.PlayerName,
                        group.Count()))
                    .OrderByDescending(group => group.Count)
                    .ThenBy(group => group.PlayerName)
                    .Take(25)
                    .ToArray()
            });
        })
        .WithName("MlbPredictionCoverageGapsDebug")
        .WithSummary("Classifies MLB prediction historical-result coverage gaps by player identity and prop-type alias recovery.");

        endpoints.MapGet("/api/debug/predictions/trace", async (
            Guid playerId,
            Guid propTypeId,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var traceProp = await LoadTracePropAsync(dbContext, playerId, propTypeId, cancellationToken);
            if (traceProp is null)
            {
                return Results.NotFound(new
                {
                    error = "No current MLB prop with a sportsbook line was found for the requested playerId and propTypeId."
                });
            }

            var historicalResults = await LoadHistoricalResultLookupAsync(dbContext, cancellationToken);
            var duplicateGroups = await LoadDuplicateIdentityGroupsAsync(dbContext, cancellationToken);
            var duplicateGroup = duplicateGroups.SingleOrDefault(group => group.Players.Any(player => player.PlayerId == playerId));
            var duplicatePlayers = duplicateGroup?.Players
                .Where(player => player.PlayerId != playerId)
                .OrderBy(player => player.DisplayName)
                .ThenBy(player => player.PlayerId)
                .ToArray() ?? [];
            var historicalPropTypes = await LoadMlbHistoricalResultCountsByPropTypeAsync(dbContext, cancellationToken);
            var historicalPropTypeIdsByNormalizedName = historicalPropTypes
                .GroupBy(propType => NormalizePropTypeName(propType.PropType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(propType => propType.HistoricalResultCount).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var aliasMappings = GetAliasHistoricalPropTypes(traceProp.PropTypeName, historicalPropTypeIdsByNormalizedName);
            var aliasPropTypeIds = aliasMappings.Select(mapping => mapping.PropTypeId).Where(id => id != propTypeId).Distinct().ToArray();
            var duplicatePlayerIds = duplicatePlayers.Select(player => player.PlayerId).Distinct().ToArray();
            var samePlayerSamePropTypeMatches = await LoadHistoricalMatchesAsync(dbContext, [playerId], [propTypeId], traceProp.EventDate, cancellationToken);
            var samePlayerAliasPropTypeMatches = await LoadHistoricalMatchesAsync(dbContext, [playerId], aliasPropTypeIds, traceProp.EventDate, cancellationToken);
            var duplicatePlayerSamePropTypeMatches = await LoadHistoricalMatchesAsync(dbContext, duplicatePlayerIds, [propTypeId], traceProp.EventDate, cancellationToken);
            var duplicatePlayerAliasPropTypeMatches = await LoadHistoricalMatchesAsync(dbContext, duplicatePlayerIds, aliasPropTypeIds, traceProp.EventDate, cancellationToken);
            var allMatches = samePlayerSamePropTypeMatches
                .Concat(samePlayerAliasPropTypeMatches)
                .Concat(duplicatePlayerSamePropTypeMatches)
                .Concat(duplicatePlayerAliasPropTypeMatches)
                .ToArray();
            var matchingHistoricalPropTypes = allMatches
                .GroupBy(match => new
                {
                    match.PropTypeId,
                    match.PropTypeName
                })
                .Select(group => new
                {
                    group.Key.PropTypeId,
                    propTypeName = group.Key.PropTypeName,
                    resultCount = group.Sum(match => match.ResultCount),
                    firstResultDate = group.Min(match => match.FirstResultDate),
                    lastResultDate = group.Max(match => match.LastResultDate)
                })
                .OrderByDescending(group => group.resultCount)
                .ThenBy(group => group.propTypeName)
                .ToArray();
            var matchingHistoricalPlayers = allMatches
                .GroupBy(match => new
                {
                    match.PlayerId,
                    match.PlayerName,
                    match.LeagueCode
                })
                .Select(group => new
                {
                    group.Key.PlayerId,
                    playerName = group.Key.PlayerName,
                    leagueCode = group.Key.LeagueCode,
                    resultCount = group.Sum(match => match.ResultCount),
                    firstResultDate = group.Min(match => match.FirstResultDate),
                    lastResultDate = group.Max(match => match.LastResultDate)
                })
                .OrderByDescending(group => group.resultCount)
                .ThenBy(group => group.playerName)
                .ToArray();
            var predictionWouldGenerate = HasHistoricalResult(historicalResults, playerId, propTypeId, traceProp.EventDate);

            return Results.Ok(new
            {
                playerId,
                playerName = traceProp.PlayerName,
                propTypeId,
                propTypeName = traceProp.PropTypeName,
                propId = traceProp.PropId,
                eventDate = traceProp.EventDate,
                historicalLookup = new
                {
                    samePlayerSamePropTypeCount = samePlayerSamePropTypeMatches.Sum(match => match.ResultCount),
                    samePlayerAliasPropTypeCount = samePlayerAliasPropTypeMatches.Sum(match => match.ResultCount),
                    duplicatePlayerSamePropTypeCount = duplicatePlayerSamePropTypeMatches.Sum(match => match.ResultCount),
                    duplicatePlayerAliasPropTypeCount = duplicatePlayerAliasPropTypeMatches.Sum(match => match.ResultCount)
                },
                duplicatePlayers = duplicatePlayers
                    .Select(player => new
                    {
                        player.PlayerId,
                        playerName = player.DisplayName,
                        leagueCode = player.LeagueCode,
                        canonicalLeague = player.CanonicalLeagueCode,
                        providerRefs = player.ProviderRefs,
                        legacyProviderRefs = player.LegacyProviderRefs
                    })
                    .ToArray(),
                aliasMappings,
                matchingHistoricalPropTypes,
                matchingHistoricalPlayers,
                predictionWouldGenerate
            });
        })
        .WithName("MlbPredictionLookupTraceDebug")
        .WithSummary("Traces historical-result lookup inputs for one MLB player and prop type.");

        endpoints.MapGet("/api/debug/predictions/alias-recovery-analysis", async (
            int? limit,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var reportLimit = Math.Clamp(limit ?? 50, 1, 500);
            var currentProps = await LoadCurrentMlbPropsWithSportsbookLinesAsync(dbContext, cancellationToken);
            var historicalResults = await LoadHistoricalResultLookupAsync(dbContext, cancellationToken);
            var equivalentPropTypeIdsById = await LoadEquivalentPropTypeIdsByIdAsync(dbContext, cancellationToken);
            var duplicateGroups = await LoadDuplicateIdentityGroupsAsync(dbContext, cancellationToken);
            var duplicateGroupByPlayerId = duplicateGroups
                .SelectMany(group => group.Players.Select(player => new
                {
                    player.PlayerId,
                    Group = group
                }))
                .GroupBy(item => item.PlayerId)
                .ToDictionary(group => group.Key, group => group.First().Group);
            var missingProps = currentProps
                .Where(prop => !HasHistoricalResult(
                    historicalResults,
                    prop.PlayerId,
                    GetEquivalentPropTypeIds(equivalentPropTypeIdsById, prop.PropTypeId),
                    prop.EventDate))
                .ToArray();
            var recoverableProps = missingProps
                .Select(prop => BuildAliasRecoveryProp(
                    prop,
                    historicalResults,
                    equivalentPropTypeIdsById,
                    duplicateGroupByPlayerId))
                .Where(prop => prop.RecoverableHistoricalResultCount > 0)
                .ToArray();
            var examples = recoverableProps
                .GroupBy(prop => prop.PlayerId)
                .Select(group =>
                {
                    var first = group.First();
                    var duplicatePlayers = group
                        .SelectMany(prop => prop.MatchedDuplicatePlayers)
                        .GroupBy(player => player.PlayerId)
                        .Select(playerGroup =>
                        {
                            var firstPlayer = playerGroup.First();
                            return new AliasRecoveryDuplicatePlayerExample(
                                firstPlayer.PlayerId,
                                firstPlayer.PlayerName,
                                firstPlayer.League,
                                playerGroup.Sum(player => player.RecoverableHistoricalResultCount));
                        })
                        .OrderByDescending(player => player.RecoverableHistoricalResultCount)
                        .ThenBy(player => player.PlayerName)
                        .ToArray();

                    return new AliasRecoveryPlayerExample(
                        first.PlayerId,
                        first.PlayerName,
                        group.Count(),
                        group.Sum(prop => prop.RecoverableHistoricalResultCount),
                        group
                            .OrderByDescending(prop => prop.RecoverableHistoricalResultCount)
                            .ThenBy(prop => prop.PropType)
                            .Take(10)
                            .ToArray(),
                        duplicatePlayers);
                })
                .OrderByDescending(example => example.RecoverablePredictionCount)
                .ThenByDescending(example => example.RecoverableHistoricalResultCount)
                .ThenBy(example => example.PlayerName)
                .Take(reportLimit)
                .ToArray();

            return Results.Ok(new
            {
                currentlyMissingHistory = missingProps.Length,
                recoverableViaAlias = recoverableProps.Length,
                recoverablePredictions = recoverableProps.Length,
                recoverableHistoricalResults = recoverableProps.Sum(prop => prop.RecoverableHistoricalResultCount),
                playersCurrentlyMissingHistory = missingProps.Select(prop => prop.PlayerId).Distinct().Count(),
                playersRecoverableViaAlias = recoverableProps.Select(prop => prop.PlayerId).Distinct().Count(),
                examples
            });
        })
        .WithName("MlbPredictionAliasRecoveryAnalysisDebug")
        .WithSummary("Estimates MLB prediction recovery when duplicate canonical identities are used as historical-result aliases.");

        endpoints.MapGet("/api/debug/predictions/alias-resolution-sample", async (
            Guid? playerId,
            ApplicationDbContext dbContext,
            IPlayerIdentityAliasService playerIdentityAliasService,
            CancellationToken cancellationToken) =>
        {
            var requestedPlayer = playerId.HasValue
                ? await dbContext.Players
                    .AsNoTracking()
                    .Include(player => player.League)
                    .SingleOrDefaultAsync(player => player.Id == playerId.Value, cancellationToken)
                : null;

            if (playerId.HasValue && requestedPlayer is null)
            {
                return Results.NotFound(new { error = $"Player '{playerId}' was not found." });
            }

            var samplePlayer = requestedPlayer ?? await FindMlbAliasSamplePlayerAsync(
                dbContext,
                playerIdentityAliasService,
                cancellationToken);

            if (samplePlayer is null)
            {
                return Results.NotFound(new { error = "No MLB player with duplicate identity aliases was found." });
            }

            var leagueCode = samplePlayer.League?.Code ?? "MLB";
            var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
                samplePlayer.Id,
                leagueCode,
                cancellationToken);
            var equivalentPlayerIdArray = equivalentPlayerIds.Distinct().ToArray();
            var equivalentPlayerRows = await dbContext.Players
                .AsNoTracking()
                .Include(player => player.League)
                .Include(player => player.ExternalProviderRefs)
                .Where(player => equivalentPlayerIdArray.Contains(player.Id))
                .OrderBy(player => player.DisplayName)
                .ThenBy(player => player.Id)
                .ToListAsync(cancellationToken);
            var equivalentPlayers = equivalentPlayerRows
                .Select(player => new
                {
                    playerId = player.Id,
                    playerName = player.DisplayName,
                    league = player.League == null ? "UNKNOWN" : player.League.Code,
                    providerRefs = player.ExternalProviderRefs
                        .OrderBy(reference => reference.Provider)
                        .ThenBy(reference => reference.ProviderPlayerId)
                        .Select(reference => new
                        {
                            reference.Provider,
                            reference.ProviderPlayerId,
                            reference.ProviderDisplayName
                        })
                        .ToArray()
                })
                .ToArray();

            return Results.Ok(new
            {
                playerId = samplePlayer.Id,
                playerName = samplePlayer.DisplayName,
                league = leagueCode,
                equivalentPlayerIds = equivalentPlayerIdArray,
                equivalentPlayers
            });
        })
        .WithName("MlbPredictionAliasResolutionSampleDebug")
        .WithSummary("Returns player identity aliases used by MLB prediction historical-result lookup.");

        endpoints.MapGet("/api/debug/predictions/prop-type-id-conflicts", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var propTypes = await dbContext.PropTypes
                .AsNoTracking()
                .Select(propType => new
                {
                    propType.Id,
                    propType.Name
                })
                .ToListAsync(cancellationToken);
            var conflictGroups = propTypes
                .GroupBy(propType => CanonicalPropTypeName(propType.Name), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(propType => propType.Id).Distinct().Count() > 1)
                .ToArray();
            var conflictPropTypeIds = conflictGroups
                .SelectMany(group => group.Select(propType => propType.Id))
                .Distinct()
                .ToArray();
            var currentProps = await LoadCurrentMlbPropsWithSportsbookLinesAsync(dbContext, cancellationToken);
            var historicalResults = await LoadHistoricalResultLookupAsync(dbContext, cancellationToken);
            var historicalCountsByPropTypeId = await dbContext.PropResults
                .AsNoTracking()
                .Include(result => result.League)
                .Where(result => result.League != null
                    && result.League.Code == "MLB"
                    && conflictPropTypeIds.Contains(result.PropTypeId))
                .GroupBy(result => result.PropTypeId)
                .Select(group => new
                {
                    PropTypeId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(group => group.PropTypeId, group => group.Count, cancellationToken);
            var activeCountsByPropTypeId = currentProps
                .Where(prop => conflictPropTypeIds.Contains(prop.PropTypeId))
                .GroupBy(prop => prop.PropTypeId)
                .ToDictionary(group => group.Key, group => group.Count());
            var conflicts = conflictGroups
                .Select(group =>
                {
                    var groupPropTypes = group
                        .OrderBy(propType => propType.Name)
                        .ThenBy(propType => propType.Id)
                        .ToArray();
                    var groupPropTypeIds = groupPropTypes.Select(propType => propType.Id).ToArray();
                    var activeGroupProps = currentProps
                        .Where(prop => groupPropTypeIds.Contains(prop.PropTypeId))
                        .ToArray();
                    var activePredictionSkipCount = activeGroupProps.Count(prop =>
                        !HasHistoricalResult(historicalResults, prop.PlayerId, prop.PropTypeId, prop.EventDate)
                        && groupPropTypeIds
                            .Where(propTypeId => propTypeId != prop.PropTypeId)
                            .Any(propTypeId => HasHistoricalResult(historicalResults, prop.PlayerId, propTypeId, prop.EventDate)));

                    return new PropTypeIdConflictResponse(
                        groupPropTypes
                            .GroupBy(propType => propType.Name, StringComparer.OrdinalIgnoreCase)
                            .OrderByDescending(nameGroup => nameGroup.Count())
                            .ThenBy(nameGroup => nameGroup.Key)
                            .First()
                            .Key,
                        groupPropTypeIds,
                        groupPropTypes
                            .Select(propType => new PropTypeIdConflictDetail(
                                propType.Id,
                                propType.Name,
                                activeCountsByPropTypeId.GetValueOrDefault(propType.Id),
                                historicalCountsByPropTypeId.GetValueOrDefault(propType.Id)))
                            .ToArray(),
                        activeGroupProps.Length,
                        groupPropTypeIds.Sum(propTypeId => historicalCountsByPropTypeId.GetValueOrDefault(propTypeId)),
                        activePredictionSkipCount,
                        activePredictionSkipCount);
                })
                .OrderByDescending(conflict => conflict.EstimatedRecoverablePredictions)
                .ThenByDescending(conflict => conflict.ActivePredictionSkipCount)
                .ThenBy(conflict => conflict.PropTypeName)
                .ToArray();

            return Results.Ok(new
            {
                conflictCount = conflicts.Length,
                estimatedRecoverablePredictions = conflicts.Sum(conflict => conflict.EstimatedRecoverablePredictions),
                conflicts
            });
        })
        .WithName("MlbPredictionPropTypeIdConflictsDebug")
        .WithSummary("Finds MLB logical prop types split across multiple prop_type IDs and estimates prediction recovery.");

        return endpoints;
    }

    private static decimal AverageOrZero(IEnumerable<decimal?> values)
    {
        var populatedValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return populatedValues.Length == 0 ? 0m : Math.Round(populatedValues.Average(), 4);
    }

    private static int GetSkipCount(IReadOnlyDictionary<string, int> skipCounts, string reason)
    {
        return skipCounts.TryGetValue(reason, out var count) ? count : 0;
    }

    private static async Task<IReadOnlyList<NoHistoricalResultSkippedProp>> ReconstructNoHistoricalResultSkipsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var props = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.Player)
            .Include(prop => prop.League)
            .Include(prop => prop.PropType)
            .Include(prop => prop.PlatformProps)
            .ThenInclude(platformProp => platformProp.Platform)
            .Where(prop => prop.League != null && prop.League.Code == "MLB")
            .OrderBy(prop => prop.StartsAt)
            .ThenBy(prop => prop.Id)
            .ToListAsync(cancellationToken);
        var historicalResults = await LoadHistoricalResultLookupAsync(dbContext, cancellationToken);
        var skippedProps = new List<NoHistoricalResultSkippedProp>();

        foreach (var prop in props)
        {
            if (prop.Player is null || prop.League is null || prop.PropType is null)
            {
                continue;
            }

            var currentPlatformProp = prop.PlatformProps
                .OrderByDescending(platformProp => platformProp.ObservedAt)
                .ThenByDescending(platformProp => platformProp.Id)
                .FirstOrDefault();
            if (currentPlatformProp is null)
            {
                continue;
            }

            var eventDate = DateOnly.FromDateTime(prop.StartsAt.UtcDateTime);
            if (HasHistoricalResult(historicalResults, prop.PlayerId, prop.PropTypeId, eventDate))
            {
                continue;
            }

            skippedProps.Add(new NoHistoricalResultSkippedProp(
                prop.Id,
                prop.PlayerId,
                prop.Player.DisplayName,
                prop.League.Code,
                CanonicalLeagueNormalizer.NormalizeCode(prop.League.Code),
                prop.PropTypeId,
                prop.PropType.Name,
                eventDate,
                currentPlatformProp.Platform?.Name ?? currentPlatformProp.Platform?.Slug ?? "Unknown",
                currentPlatformProp.CurrentLine));
        }

        return skippedProps;
    }

    private static async Task<IReadOnlyList<BacktestPredictionInput>> LoadBacktestPredictionsAsync(
        ApplicationDbContext dbContext,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Predictions
            .AsNoTracking()
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.Player)
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.League)
            .Include(prediction => prediction.Prop)
            .ThenInclude(prop => prop!.PropType)
            .Where(prediction => prediction.PredictionRunId == runId
                && prediction.Prop != null
                && prediction.Prop.League != null
                && prediction.Prop.League.Code == "MLB"
                && prediction.Prop.Player != null
                && prediction.Prop.PropType != null
                && prediction.SportsbookLine.HasValue)
            .OrderBy(prediction => prediction.Prop!.StartsAt)
            .ThenBy(prediction => prediction.Prop!.Player!.DisplayName)
            .ThenBy(prediction => prediction.Prop!.PropType!.Name)
            .Select(prediction => new
            {
                PredictionId = prediction.Id,
                prediction.PropId,
                PlayerId = prediction.Prop!.PlayerId,
                PlayerName = prediction.Prop.Player == null ? "UNKNOWN" : prediction.Prop.Player.DisplayName,
                LeagueCode = prediction.Prop.League == null ? "UNKNOWN" : prediction.Prop.League.Code,
                PropTypeId = prediction.Prop.PropTypeId,
                PropType = prediction.Prop.PropType == null ? "UNKNOWN" : prediction.Prop.PropType.Name,
                EventDate = DateOnly.FromDateTime(prediction.Prop.StartsAt.UtcDateTime),
                prediction.Recommendation,
                prediction.Confidence,
                prediction.Edge,
                prediction.PredictedValue,
                SportsbookLine = prediction.SportsbookLine!.Value
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new BacktestPredictionInput(
                row.PredictionId,
                row.PropId,
                row.PlayerId,
                row.PlayerName,
                row.LeagueCode,
                row.PropTypeId,
                row.PropType,
                row.EventDate,
                row.Recommendation,
                row.Confidence,
                row.Edge,
                row.PredictedValue,
                row.SportsbookLine))
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<BacktestActualResultKey, decimal>> LoadBacktestActualResultsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.League)
            .Where(result => result.League != null && result.League.Code == "MLB")
            .Select(result => new
            {
                result.PlayerId,
                result.PropTypeId,
                result.EventDate,
                result.ActualResultValue
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new BacktestActualResultKey(row.PlayerId, row.PropTypeId, row.EventDate))
            .ToDictionary(
                group => group.Key,
                group => group.First().ActualResultValue);
    }

    private static async Task<Guid[]> GetBacktestEquivalentPlayerIdsAsync(
        Guid playerId,
        string leagueCode,
        IPlayerIdentityAliasService playerIdentityAliasService,
        Dictionary<Guid, Guid[]> aliasCache,
        CancellationToken cancellationToken)
    {
        if (aliasCache.TryGetValue(playerId, out var cachedPlayerIds))
        {
            return cachedPlayerIds;
        }

        var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
            playerId,
            leagueCode,
            cancellationToken);
        var equivalentPlayerIdArray = equivalentPlayerIds.Distinct().ToArray();
        aliasCache[playerId] = equivalentPlayerIdArray;

        return equivalentPlayerIdArray;
    }

    private static decimal? FindBacktestActualResult(
        IReadOnlyDictionary<BacktestActualResultKey, decimal> actualResults,
        BacktestPredictionInput prediction,
        IReadOnlyCollection<Guid> equivalentPlayerIds,
        IReadOnlyCollection<Guid> equivalentPropTypeIds)
    {
        var candidatePlayerIds = equivalentPlayerIds
            .OrderBy(playerId => playerId == prediction.PlayerId ? 0 : 1)
            .ThenBy(playerId => playerId)
            .ToArray();
        var candidatePropTypeIds = equivalentPropTypeIds
            .OrderBy(propTypeId => propTypeId == prediction.PropTypeId ? 0 : 1)
            .ThenBy(propTypeId => propTypeId)
            .ToArray();

        foreach (var playerId in candidatePlayerIds)
        {
            foreach (var propTypeId in candidatePropTypeIds)
            {
                if (actualResults.TryGetValue(
                    new BacktestActualResultKey(playerId, propTypeId, prediction.EventDate),
                    out var actualResult))
                {
                    return actualResult;
                }
            }
        }

        return null;
    }

    private static BacktestActualResultCandidate[] FindBacktestActualResultCandidates(
        IReadOnlyDictionary<BacktestActualResultKey, decimal> actualResults,
        BacktestPredictionInput prediction,
        IReadOnlyCollection<Guid> equivalentPlayerIds,
        IReadOnlyCollection<Guid> equivalentPropTypeIds)
    {
        var candidatePlayerIds = equivalentPlayerIds
            .OrderBy(playerId => playerId == prediction.PlayerId ? 0 : 1)
            .ThenBy(playerId => playerId)
            .ToArray();
        var candidatePropTypeIds = equivalentPropTypeIds
            .OrderBy(propTypeId => propTypeId == prediction.PropTypeId ? 0 : 1)
            .ThenBy(propTypeId => propTypeId)
            .ToArray();
        var candidates = new List<BacktestActualResultCandidate>();

        foreach (var playerId in candidatePlayerIds)
        {
            foreach (var propTypeId in candidatePropTypeIds)
            {
                if (actualResults.TryGetValue(
                    new BacktestActualResultKey(playerId, propTypeId, prediction.EventDate),
                    out var actualResult))
                {
                    candidates.Add(new BacktestActualResultCandidate(playerId, propTypeId, actualResult));
                }
            }
        }

        return candidates.ToArray();
    }

    private static UngradedPredictionAnalysis BuildUngradedPredictionAnalysis(
        BacktestPredictionInput prediction,
        IReadOnlyCollection<Guid> equivalentPlayerIds,
        IReadOnlyCollection<Guid> equivalentPropTypeIds,
        IReadOnlyDictionary<PlayerPropTypeKey, HashSet<DateOnly>> resultKeysByPlayerProp,
        IReadOnlySet<Guid> playersWithResults,
        IReadOnlySet<Guid> propTypesWithResults)
    {
        var hasAnyPlayerHistory = equivalentPlayerIds.Any(playersWithResults.Contains);
        var hasAnyPropTypeHistory = equivalentPropTypeIds.Any(propTypesWithResults.Contains);
        var equivalentDates = equivalentPlayerIds
            .SelectMany(playerId => equivalentPropTypeIds.Select(propTypeId => new PlayerPropTypeKey(playerId, propTypeId)))
            .Where(resultKeysByPlayerProp.ContainsKey)
            .SelectMany(key => resultKeysByPlayerProp[key])
            .Distinct()
            .ToArray();
        var hasPriorEquivalentHistory = equivalentDates.Any(date => date < prediction.EventDate);
        var hasLaterEquivalentHistory = equivalentDates.Any(date => date > prediction.EventDate);
        var reason = (hasAnyPlayerHistory, hasAnyPropTypeHistory, hasPriorEquivalentHistory, hasLaterEquivalentHistory) switch
        {
            (false, _, _, _) => "NoPlayerHistoricalResults",
            (_, false, _, _) => "NoPropTypeHistoricalResults",
            (_, _, true, true) => "MissingSpecificEventDateResult",
            (_, _, true, false) => "LikelyDateMismatchOrNoSameDayResult",
            (_, _, false, true) => "PredictionBeforeFirstImportedResult",
            _ => "OtherNoSameDayOutcome"
        };

        return new UngradedPredictionAnalysis(
            prediction.PredictionId,
            prediction.PlayerId,
            prediction.PlayerName,
            prediction.PropTypeId,
            prediction.PropType,
            prediction.EventDate,
            reason);
    }

    private static object[] BuildQualityRecommendationSummary(IReadOnlyCollection<BacktestPredictionResult> predictions)
    {
        return predictions
            .GroupBy(prediction => prediction.Recommendation, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var summary = BuildBacktestSummary(group.ToArray());

                return new
                {
                    recommendation = group.Key,
                    count = group.Count(),
                    summary.Wins,
                    summary.Losses,
                    summary.Pushes,
                    summary.HitRate,
                    averageSportsbookLine = AverageDecimal(group.Select(row => (decimal?)row.SportsbookLine)),
                    averageActualResult = AverageDecimal(group.Select(row => (decimal?)row.ActualResult)),
                    averageModelPrediction = AverageDecimal(group.Select(row => row.PredictedValue)),
                    averageEdge = AverageDecimal(group.Select(row => row.Edge))
                };
            })
            .OrderBy(row => row.recommendation)
            .Cast<object>()
            .ToArray();
    }

    private static EdgeBucketAnalysis BuildEdgeBucketAnalysis(
        string edgeBucket,
        IReadOnlyCollection<BacktestPredictionResult> predictions)
    {
        var summary = BuildBacktestSummary(predictions);
        var overPredictions = predictions
            .Where(prediction => prediction.Recommendation == "OVER")
            .ToArray();
        var underPredictions = predictions
            .Where(prediction => prediction.Recommendation == "UNDER")
            .ToArray();

        return new EdgeBucketAnalysis(
            edgeBucket,
            predictions.Count,
            summary.Wins,
            summary.Losses,
            summary.Pushes,
            summary.HitRate,
            BuildBacktestSummary(overPredictions),
            BuildBacktestSummary(underPredictions));
    }

    private static ThresholdSimulationResult BuildThresholdSimulation(
        decimal threshold,
        IReadOnlyCollection<BacktestPredictionResult> gradedPredictions)
    {
        var simulatedPredictions = gradedPredictions
            .Where(prediction => prediction.Edge.HasValue && Math.Abs(prediction.Edge.Value) >= threshold)
            .Select(prediction =>
            {
                var simulatedRecommendation = prediction.Edge!.Value >= threshold ? "OVER" : "UNDER";

                return prediction with
                {
                    Recommendation = simulatedRecommendation,
                    Outcome = GradePrediction(
                        simulatedRecommendation,
                        prediction.SportsbookLine,
                        prediction.ActualResult)
                };
            })
            .ToArray();
        var summary = BuildBacktestSummary(simulatedPredictions);

        return new ThresholdSimulationResult(
            threshold,
            simulatedPredictions.Length,
            summary.Wins,
            summary.Losses,
            summary.Pushes,
            summary.HitRate,
            simulatedPredictions.Count(prediction => prediction.Recommendation == "OVER"),
            simulatedPredictions.Count(prediction => prediction.Recommendation == "UNDER"));
    }

    private static decimal? AverageDecimal(IEnumerable<decimal?> values)
    {
        var valueArray = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return valueArray.Length == 0
            ? null
            : Math.Round(valueArray.Average(), 4);
    }

    private static string EstimateUngradedRecoverability(string reason, int count)
    {
        return reason switch
        {
            "NoPlayerHistoricalResults" => "Low without more player mappings or imported historical results.",
            "NoPropTypeHistoricalResults" => "Medium only if the prop type can be imported from MLB Stats API fields.",
            "LikelyDateMismatchOrNoSameDayResult" => "Medium-High if event-date alignment or same-day result coverage is fixed.",
            "MissingSpecificEventDateResult" => $"Potentially recoverable after same-day result coverage audit; affected predictions: {count}.",
            "PredictionBeforeFirstImportedResult" => "Low for historical backtest unless earlier result backfill is available.",
            _ => "Unknown; inspect examples before estimating recovery."
        };
    }

    private static string GetDetailedEdgeBucket(decimal edge)
    {
        return edge switch
        {
            <= -2.0m => "<= -2.0",
            <= -1.5m => "-2.0 to -1.5",
            <= -1.0m => "-1.5 to -1.0",
            <= -0.75m => "-1.0 to -0.75",
            <= -0.50m => "-0.75 to -0.50",
            <= -0.25m => "-0.50 to -0.25",
            <= 0m => "-0.25 to 0",
            <= 0.25m => "0 to 0.25",
            <= 0.50m => "0.25 to 0.50",
            <= 0.75m => "0.50 to 0.75",
            <= 1.0m => "0.75 to 1.0",
            <= 1.5m => "1.0 to 1.5",
            <= 2.0m => "1.5 to 2.0",
            _ => ">= 2.0"
        };
    }

    private static int DetailedEdgeBucketSortOrder(string bucket)
    {
        return bucket switch
        {
            "<= -2.0" => 0,
            "-2.0 to -1.5" => 1,
            "-1.5 to -1.0" => 2,
            "-1.0 to -0.75" => 3,
            "-0.75 to -0.50" => 4,
            "-0.50 to -0.25" => 5,
            "-0.25 to 0" => 6,
            "0 to 0.25" => 7,
            "0.25 to 0.50" => 8,
            "0.50 to 0.75" => 9,
            "0.75 to 1.0" => 10,
            "1.0 to 1.5" => 11,
            "1.5 to 2.0" => 12,
            ">= 2.0" => 13,
            _ => 14
        };
    }

    private static string GetConfidenceDecileBucket(decimal confidence)
    {
        var percent = Math.Clamp(confidence * 100m, 0m, 100m);
        var lowerBound = percent >= 100m
            ? 90
            : (int)(Math.Floor(percent / 10m) * 10m);
        var upperBound = lowerBound + 10;

        return $"{lowerBound}-{upperBound}";
    }

    private static int ConfidenceDecileBucketSortOrder(string bucket)
    {
        var separatorIndex = bucket.IndexOf('-', StringComparison.Ordinal);
        return separatorIndex <= 0 || !int.TryParse(bucket[..separatorIndex], out var lowerBound)
            ? 100
            : lowerBound;
    }

    private static string GradePrediction(string recommendation, decimal sportsbookLine, decimal actualResult)
    {
        return recommendation.ToUpperInvariant() switch
        {
            "OVER" when actualResult > sportsbookLine => "Win",
            "OVER" when actualResult < sportsbookLine => "Loss",
            "UNDER" when actualResult < sportsbookLine => "Win",
            "UNDER" when actualResult > sportsbookLine => "Loss",
            _ => "Push"
        };
    }

    private static BacktestSummary BuildBacktestSummary(IReadOnlyCollection<BacktestPredictionResult> predictions)
    {
        var wins = predictions.Count(prediction => prediction.Outcome == "Win");
        var losses = predictions.Count(prediction => prediction.Outcome == "Loss");
        var pushes = predictions.Count(prediction => prediction.Outcome == "Push");
        var decisions = wins + losses;

        return new BacktestSummary(
            predictions.Count,
            wins,
            losses,
            pushes,
            decisions == 0 ? 0m : Math.Round(wins * 100m / decisions, 2));
    }

    private static BacktestGroupSummary[] BuildBacktestGroups(
        IReadOnlyCollection<BacktestPredictionResult> predictions,
        Func<BacktestPredictionResult, string> groupKey)
    {
        return predictions
            .GroupBy(groupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var summary = BuildBacktestSummary(group.ToArray());

                return new BacktestGroupSummary(
                    group.Key,
                    summary.GradedPredictions,
                    summary.Wins,
                    summary.Losses,
                    summary.Pushes,
                    summary.HitRate);
            })
            .ToArray();
    }

    private static string GetEdgeBucket(decimal? edge)
    {
        if (!edge.HasValue)
        {
            return "NoEdge";
        }

        return edge.Value switch
        {
            < -2m => "< -2.00",
            < -1m => "-2.00 to -1.01",
            < -0.5m => "-1.00 to -0.51",
            < -0.25m => "-0.50 to -0.26",
            <= 0.25m => "-0.25 to 0.25",
            <= 0.5m => "0.26 to 0.50",
            <= 1m => "0.51 to 1.00",
            <= 2m => "1.01 to 2.00",
            _ => "> 2.00"
        };
    }

    private static int EdgeBucketSortOrder(string bucket)
    {
        return bucket switch
        {
            "< -2.00" => 0,
            "-2.00 to -1.01" => 1,
            "-1.00 to -0.51" => 2,
            "-0.50 to -0.26" => 3,
            "-0.25 to 0.25" => 4,
            "0.26 to 0.50" => 5,
            "0.51 to 1.00" => 6,
            "1.01 to 2.00" => 7,
            "> 2.00" => 8,
            _ => 9
        };
    }

    private static string GetConfidenceBucket(decimal confidence)
    {
        return confidence switch
        {
            < 0.6m => "< 0.60",
            < 0.7m => "0.60 to 0.69",
            < 0.8m => "0.70 to 0.79",
            < 0.9m => "0.80 to 0.89",
            _ => ">= 0.90"
        };
    }

    private static int ConfidenceBucketSortOrder(string bucket)
    {
        return bucket switch
        {
            "< 0.60" => 0,
            "0.60 to 0.69" => 1,
            "0.70 to 0.79" => 2,
            "0.80 to 0.89" => 3,
            ">= 0.90" => 4,
            _ => 5
        };
    }

    private static async Task<int> CountCurrentlyEligibleMlbPredictionsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var props = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.League)
            .Include(prop => prop.PropType)
            .Include(prop => prop.PlatformProps)
            .Where(prop => prop.League != null && prop.League.Code == "MLB")
            .Select(prop => new
            {
                prop.PlayerId,
                prop.PropTypeId,
                HasPropType = prop.PropType != null,
                HasSportsbookLine = prop.PlatformProps.Any(),
                prop.StartsAt
            })
            .ToListAsync(cancellationToken);
        var historicalResults = await LoadHistoricalResultLookupAsync(dbContext, cancellationToken);

        return props
            .Where(prop => prop.HasPropType && prop.HasSportsbookLine)
            .Count(prop => HasHistoricalResult(
                historicalResults,
                prop.PlayerId,
                prop.PropTypeId,
                DateOnly.FromDateTime(prop.StartsAt.UtcDateTime)));
    }

    private static async Task<IReadOnlyList<MlbHistoricalResultPropTypeCount>> LoadMlbHistoricalResultCountsByPropTypeAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.League)
            .Where(result => result.League != null && result.League.Code == "MLB")
            .GroupBy(result => result.PropTypeId)
            .Select(group => new
            {
                PropTypeId = group.Key,
                HistoricalResultCount = group.Count()
            })
            .ToListAsync(cancellationToken);
        var propTypeIds = rows.Select(row => row.PropTypeId).Distinct().ToArray();
        var propTypeNames = await dbContext.PropTypes
            .AsNoTracking()
            .Where(propType => propTypeIds.Contains(propType.Id))
            .Select(propType => new
            {
                propType.Id,
                propType.Name
            })
            .ToDictionaryAsync(propType => propType.Id, propType => propType.Name, cancellationToken);

        return rows
            .Select(row => new MlbHistoricalResultPropTypeCount(
                row.PropTypeId,
                propTypeNames.GetValueOrDefault(row.PropTypeId, "UNKNOWN"),
                row.HistoricalResultCount))
            .ToArray();
    }

    private static async Task<IReadOnlyList<CurrentMlbSportsbookPropTypeCount>> LoadCurrentMlbSportsbookPropTypesAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.League)
            .Include(prop => prop.PropType)
            .Include(prop => prop.PlatformProps)
            .Where(prop => prop.League != null
                && prop.League.Code == "MLB"
                && prop.PropType != null
                && prop.PlatformProps.Any())
            .Select(prop => new
            {
                prop.PropTypeId,
                PropType = prop.PropType == null ? "UNKNOWN" : prop.PropType.Name
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new
            {
                row.PropTypeId,
                row.PropType
            })
            .Select(group => new CurrentMlbSportsbookPropTypeCount(
                group.Key.PropTypeId,
                group.Key.PropType,
                group.Count()))
            .OrderByDescending(group => group.ActivePropCount)
            .ThenBy(group => group.SportsbookPropType)
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<Guid, Guid[]>> LoadEquivalentPropTypeIdsByIdAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var propTypes = await dbContext.PropTypes
            .AsNoTracking()
            .Select(propType => new
            {
                propType.Id,
                propType.Name
            })
            .ToListAsync(cancellationToken);

        return propTypes
            .GroupBy(propType => CanonicalPropTypeName(propType.Name), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var equivalentIds = group.Select(propType => propType.Id).Distinct().ToArray();
                return group.Select(propType => new
                {
                    propType.Id,
                    EquivalentIds = equivalentIds
                });
            })
            .ToDictionary(item => item.Id, item => item.EquivalentIds);
    }

    private static Guid[] GetEquivalentPropTypeIds(
        IReadOnlyDictionary<Guid, Guid[]> equivalentPropTypeIdsById,
        Guid propTypeId)
    {
        return equivalentPropTypeIdsById.TryGetValue(propTypeId, out var equivalentPropTypeIds)
            ? equivalentPropTypeIds
            : [propTypeId];
    }

    private static async Task<IReadOnlyList<CurrentMlbPropForCoverage>> LoadCurrentMlbPropsWithSportsbookLinesAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var props = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.Player)
            .Include(prop => prop.League)
            .Include(prop => prop.PropType)
            .Include(prop => prop.PlatformProps)
            .Where(prop => prop.League != null
                && prop.League.Code == "MLB"
                && prop.Player != null
                && prop.PropType != null
                && prop.PlatformProps.Any())
            .Select(prop => new
            {
                prop.Id,
                prop.PlayerId,
                PlayerName = prop.Player == null ? "UNKNOWN" : prop.Player.DisplayName,
                prop.PropTypeId,
                PropType = prop.PropType == null ? "UNKNOWN" : prop.PropType.Name,
                prop.StartsAt
            })
            .ToListAsync(cancellationToken);

        return props
            .Select(prop => new CurrentMlbPropForCoverage(
                prop.Id,
                prop.PlayerId,
                prop.PlayerName,
                prop.PropTypeId,
                prop.PropType,
                DateOnly.FromDateTime(prop.StartsAt.UtcDateTime)))
            .ToArray();
    }

    private static async Task<Player?> FindMlbAliasSamplePlayerAsync(
        ApplicationDbContext dbContext,
        IPlayerIdentityAliasService playerIdentityAliasService,
        CancellationToken cancellationToken)
    {
        var players = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.Player)
            .ThenInclude(player => player!.League)
            .Include(prop => prop.League)
            .Where(prop => prop.League != null
                && prop.League.Code == "MLB"
                && prop.Player != null)
            .Select(prop => prop.Player!)
            .ToListAsync(cancellationToken);

        foreach (var player in players
            .GroupBy(player => player.Id)
            .Select(group => group.First())
            .OrderBy(player => player.DisplayName)
            .ThenBy(player => player.Id))
        {
            var equivalentPlayerIds = await playerIdentityAliasService.GetEquivalentPlayerIdsAsync(
                player.Id,
                player.League?.Code ?? "MLB",
                cancellationToken);

            if (equivalentPlayerIds.Distinct().Count() > 1)
            {
                return player;
            }
        }

        return null;
    }

    private static async Task<PredictionTraceProp?> LoadTracePropAsync(
        ApplicationDbContext dbContext,
        Guid playerId,
        Guid propTypeId,
        CancellationToken cancellationToken)
    {
        var prop = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.Player)
            .Include(prop => prop.League)
            .Include(prop => prop.PropType)
            .Include(prop => prop.PlatformProps)
            .Where(prop => prop.PlayerId == playerId
                && prop.PropTypeId == propTypeId
                && prop.League != null
                && prop.League.Code == "MLB"
                && prop.Player != null
                && prop.PropType != null
                && prop.PlatformProps.Any())
            .OrderByDescending(prop => prop.StartsAt)
            .ThenByDescending(prop => prop.Id)
            .Select(prop => new
            {
                prop.Id,
                PlayerName = prop.Player == null ? "UNKNOWN" : prop.Player.DisplayName,
                PropTypeName = prop.PropType == null ? "UNKNOWN" : prop.PropType.Name,
                prop.StartsAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        return prop is null
            ? null
            : new PredictionTraceProp(
                prop.Id,
                prop.PlayerName,
                prop.PropTypeName,
                DateOnly.FromDateTime(prop.StartsAt.UtcDateTime));
    }

    private static CoverageGapClassification ClassifyCoverageGap(
        CurrentMlbPropForCoverage prop,
        IReadOnlyDictionary<HistoricalResultKey, DateOnly[]> historicalResults,
        IReadOnlyDictionary<string, Guid[]> historicalPropTypeIdsByNormalizedName,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId)
    {
        var exactMatch = HasHistoricalResult(historicalResults, prop.PlayerId, prop.PropTypeId, prop.EventDate);
        if (exactMatch)
        {
            return new CoverageGapClassification(
                prop.PropId,
                prop.PlayerId,
                prop.PlayerName,
                prop.PropType,
                true,
                false,
                false,
                false,
                null,
                "HistoricalMatchFound");
        }

        var aliasPropTypeIds = GetAliasHistoricalPropTypeIds(
            prop.PropType,
            historicalPropTypeIdsByNormalizedName)
            .Where(propTypeId => propTypeId != prop.PropTypeId)
            .Distinct()
            .ToArray();
        var recoverableByAlias = aliasPropTypeIds
            .Any(propTypeId => HasHistoricalResult(historicalResults, prop.PlayerId, propTypeId, prop.EventDate));
        var duplicatePlayerIds = duplicateGroupByPlayerId.TryGetValue(prop.PlayerId, out var duplicateGroup)
            ? duplicateGroup.Players
                .Select(player => player.PlayerId)
                .Where(playerId => playerId != prop.PlayerId)
                .Distinct()
                .ToArray()
            : [];
        var recoverableByDuplicate = duplicatePlayerIds
            .Any(playerId => HasHistoricalResult(historicalResults, playerId, prop.PropTypeId, prop.EventDate));
        var recoverableByBoth = duplicatePlayerIds
            .Any(playerId => aliasPropTypeIds.Any(propTypeId => HasHistoricalResult(historicalResults, playerId, propTypeId, prop.EventDate)));
        var hasAnyPlayerHistory = historicalResults.Keys.Any(key => key.PlayerId == prop.PlayerId)
            || duplicatePlayerIds.Any(playerId => historicalResults.Keys.Any(key => key.PlayerId == playerId));
        var rootCause = hasAnyPlayerHistory ? "MissingPropTypeHistory" : "MissingPlayerHistory";

        return new CoverageGapClassification(
            prop.PropId,
            prop.PlayerId,
            prop.PlayerName,
            prop.PropType,
            false,
            recoverableByAlias,
            recoverableByDuplicate,
            recoverableByBoth,
            GetSuggestedAliasName(prop.PropType),
            rootCause);
    }

    private static string ClassifyNoHistorySkip(PlayerHistoricalLookupDiagnostic lookup)
    {
        if (!lookup.HasSupportedPropTypeHistory)
        {
            return "UnsupportedPropType";
        }

        if (!lookup.HasMlbMappingForEquivalentPlayers)
        {
            return "NoMlbMapping";
        }

        if (!lookup.HasAliasResolution)
        {
            return "NoAliasResolution";
        }

        if (!lookup.HasHistoricalResultsForEquivalentPlayers)
        {
            return "NoHistoricalResultsExist";
        }

        return "Other";
    }

    private static AliasRecoveryPropExample BuildAliasRecoveryProp(
        CurrentMlbPropForCoverage prop,
        IReadOnlyDictionary<HistoricalResultKey, DateOnly[]> historicalResults,
        IReadOnlyDictionary<Guid, Guid[]> equivalentPropTypeIdsById,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId)
    {
        var equivalentPropTypeIds = GetEquivalentPropTypeIds(equivalentPropTypeIdsById, prop.PropTypeId);
        var duplicatePlayers = duplicateGroupByPlayerId.TryGetValue(prop.PlayerId, out var duplicateGroup)
            ? duplicateGroup.Players
                .Where(player => player.PlayerId != prop.PlayerId)
                .Select(player =>
                {
                    var recoverableHistoricalResultCount = CountHistoricalResults(
                        historicalResults,
                        player.PlayerId,
                        equivalentPropTypeIds,
                        prop.EventDate);

                    return new AliasRecoveryDuplicatePlayerExample(
                        player.PlayerId,
                        player.DisplayName,
                        player.LeagueCode,
                        recoverableHistoricalResultCount);
                })
                .Where(player => player.RecoverableHistoricalResultCount > 0)
                .OrderByDescending(player => player.RecoverableHistoricalResultCount)
                .ThenBy(player => player.PlayerName)
                .ToArray()
            : [];

        return new AliasRecoveryPropExample(
            prop.PropId,
            prop.PlayerId,
            prop.PlayerName,
            prop.PropTypeId,
            prop.PropType,
            prop.EventDate,
            duplicatePlayers.Sum(player => player.RecoverableHistoricalResultCount),
            duplicatePlayers);
    }

    private static Guid[] GetAliasHistoricalPropTypeIds(
        string sportsbookPropType,
        IReadOnlyDictionary<string, Guid[]> historicalPropTypeIdsByNormalizedName)
    {
        var suggestedAlias = GetSuggestedAliasName(sportsbookPropType);
        if (suggestedAlias is null)
        {
            return [];
        }

        return historicalPropTypeIdsByNormalizedName.TryGetValue(NormalizePropTypeName(suggestedAlias), out var propTypeIds)
            ? propTypeIds
            : [];
    }

    private static IReadOnlyList<PredictionTraceAliasMapping> GetAliasHistoricalPropTypes(
        string sportsbookPropType,
        IReadOnlyDictionary<string, MlbHistoricalResultPropTypeCount[]> historicalPropTypesByNormalizedName)
    {
        var suggestedAlias = GetSuggestedAliasName(sportsbookPropType);
        if (suggestedAlias is null)
        {
            return [];
        }

        return historicalPropTypesByNormalizedName.TryGetValue(NormalizePropTypeName(suggestedAlias), out var historicalPropTypes)
            ? historicalPropTypes
                .Select(propType => new PredictionTraceAliasMapping(
                    sportsbookPropType,
                    suggestedAlias,
                    propType.PropTypeId,
                    propType.PropType,
                    propType.HistoricalResultCount,
                    0.95m))
                .ToArray()
            : [];
    }

    private static async Task<IReadOnlyList<PredictionTraceHistoricalMatch>> LoadHistoricalMatchesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> playerIds,
        IReadOnlyCollection<Guid> propTypeIds,
        DateOnly eventDate,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0 || propTypeIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => playerIds.Contains(result.PlayerId)
                && propTypeIds.Contains(result.PropTypeId)
                && result.EventDate < eventDate)
            .GroupBy(result => new
            {
                result.PlayerId,
                result.PropTypeId,
                result.LeagueId
            })
            .Select(group => new
            {
                group.Key.PlayerId,
                group.Key.PropTypeId,
                group.Key.LeagueId,
                ResultCount = group.Count(),
                FirstResultDate = group.Min(result => result.EventDate),
                LastResultDate = group.Max(result => result.EventDate)
            })
            .ToListAsync(cancellationToken);
        var matchPlayerIds = rows.Select(row => row.PlayerId).Distinct().ToArray();
        var matchPropTypeIds = rows.Select(row => row.PropTypeId).Distinct().ToArray();
        var matchLeagueIds = rows.Select(row => row.LeagueId).Distinct().ToArray();
        var playerNames = await dbContext.Players
            .AsNoTracking()
            .Where(player => matchPlayerIds.Contains(player.Id))
            .Select(player => new
            {
                player.Id,
                player.DisplayName
            })
            .ToDictionaryAsync(player => player.Id, player => player.DisplayName, cancellationToken);
        var propTypeNames = await dbContext.PropTypes
            .AsNoTracking()
            .Where(propType => matchPropTypeIds.Contains(propType.Id))
            .Select(propType => new
            {
                propType.Id,
                propType.Name
            })
            .ToDictionaryAsync(propType => propType.Id, propType => propType.Name, cancellationToken);
        var leagueCodes = await dbContext.Leagues
            .AsNoTracking()
            .Where(league => matchLeagueIds.Contains(league.Id))
            .Select(league => new
            {
                league.Id,
                league.Code
            })
            .ToDictionaryAsync(league => league.Id, league => league.Code, cancellationToken);

        return rows
            .Select(row => new PredictionTraceHistoricalMatch(
                row.PlayerId,
                playerNames.GetValueOrDefault(row.PlayerId, "UNKNOWN"),
                row.PropTypeId,
                propTypeNames.GetValueOrDefault(row.PropTypeId, "UNKNOWN"),
                row.LeagueId,
                leagueCodes.GetValueOrDefault(row.LeagueId, "UNKNOWN"),
                row.ResultCount,
                row.FirstResultDate,
                row.LastResultDate))
            .ToArray();
    }

    private static PropTypeMappingAnalysisRow BuildPropTypeMappingAnalysisRow(
        CurrentMlbSportsbookPropTypeCount sportsbookPropType,
        IReadOnlyDictionary<string, MlbHistoricalResultPropTypeCount> historicalByNormalizedName)
    {
        var normalizedSportsbookName = NormalizePropTypeName(sportsbookPropType.SportsbookPropType);
        if (historicalByNormalizedName.TryGetValue(normalizedSportsbookName, out var exactMatch))
        {
            return new PropTypeMappingAnalysisRow(
                sportsbookPropType.SportsbookPropType,
                sportsbookPropType.ActivePropCount,
                true,
                exactMatch.PropType,
                exactMatch.HistoricalResultCount,
                exactMatch.PropType,
                1.00m);
        }

        var suggestedCanonicalPropType = SuggestCanonicalMlbPropType(sportsbookPropType.SportsbookPropType);
        if (suggestedCanonicalPropType is not null
            && historicalByNormalizedName.TryGetValue(NormalizePropTypeName(suggestedCanonicalPropType), out var suggestedMatch))
        {
            return new PropTypeMappingAnalysisRow(
                sportsbookPropType.SportsbookPropType,
                sportsbookPropType.ActivePropCount,
                false,
                suggestedMatch.PropType,
                suggestedMatch.HistoricalResultCount,
                suggestedMatch.PropType,
                0.95m);
        }

        return new PropTypeMappingAnalysisRow(
            sportsbookPropType.SportsbookPropType,
            sportsbookPropType.ActivePropCount,
            false,
            null,
            0,
            suggestedCanonicalPropType,
            0m);
    }

    private static string? SuggestCanonicalMlbPropType(string sportsbookPropType)
    {
        var normalized = NormalizePropTypeName(sportsbookPropType);

        return normalized switch
        {
            "HITTERKS" or "BATTERSTRIKEOUTS" or "PITCHERSTRIKEOUTS" or "KS" or "K" => "Strikeouts",
            "RBI" => "RBIs",
            "PO" => "Pitching Outs",
            _ => null
        };
    }

    private static string? GetSuggestedAliasName(string sportsbookPropType)
    {
        return SuggestCanonicalMlbPropType(sportsbookPropType);
    }

    private static string CanonicalPropTypeName(string propTypeName)
    {
        return NormalizePropTypeName(propTypeName) switch
        {
            "RBI" => "RBIS",
            _ => NormalizePropTypeName(propTypeName)
        };
    }

    private static string NormalizePropTypeName(string propType)
    {
        var builder = new System.Text.StringBuilder(propType.Length);

        foreach (var character in propType.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static async Task<Dictionary<HistoricalResultKey, DateOnly[]>> LoadHistoricalResultLookupAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.PropResults
            .AsNoTracking()
            .Include(result => result.League)
            .Where(result => result.League != null && result.League.Code == "MLB")
            .Select(result => new
            {
                result.PlayerId,
                result.PropTypeId,
                result.EventDate
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new HistoricalResultKey(row.PlayerId, row.PropTypeId))
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.EventDate).Distinct().OrderBy(date => date).ToArray());
    }

    private static bool HasHistoricalResult(
        IReadOnlyDictionary<HistoricalResultKey, DateOnly[]> historicalResults,
        Guid playerId,
        Guid propTypeId,
        DateOnly eventDate)
    {
        return historicalResults.TryGetValue(new HistoricalResultKey(playerId, propTypeId), out var resultDates)
            && resultDates.Any(resultDate => resultDate < eventDate);
    }

    private static bool HasHistoricalResult(
        IReadOnlyDictionary<HistoricalResultKey, DateOnly[]> historicalResults,
        Guid playerId,
        IReadOnlyCollection<Guid> propTypeIds,
        DateOnly eventDate)
    {
        return propTypeIds.Any(propTypeId => HasHistoricalResult(historicalResults, playerId, propTypeId, eventDate));
    }

    private static int CountHistoricalResults(
        IReadOnlyDictionary<HistoricalResultKey, DateOnly[]> historicalResults,
        Guid playerId,
        IReadOnlyCollection<Guid> propTypeIds,
        DateOnly eventDate)
    {
        return propTypeIds.Sum(propTypeId =>
            historicalResults.TryGetValue(new HistoricalResultKey(playerId, propTypeId), out var resultDates)
                ? resultDates.Count(resultDate => resultDate < eventDate)
                : 0);
    }

    private static async Task<IReadOnlyList<DuplicateIdentityGroup>> LoadDuplicateIdentityGroupsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var players = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)
            .Include(player => player.ExternalProviderRefs)
            .ToListAsync(cancellationToken);

        return players
            .Select(ToPlayerIdentityRow)
            .Where(player => !string.IsNullOrWhiteSpace(player.NormalizedDisplayName))
            .GroupBy(player => player.NormalizedDisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateIdentityGroup(
                group.Key,
                group.OrderBy(player => player.CanonicalLeagueCode).ThenBy(player => player.PlayerId).ToArray()))
            .ToArray();
    }

    private static NoHistoricalResultPlayerReport BuildNoHistoricalResultPlayerReport(
        Guid playerId,
        IReadOnlyCollection<NoHistoricalResultSkippedProp> skippedProps,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId,
        IReadOnlyDictionary<HistoricalResultKey, DateOnly[]> historicalResults,
        IReadOnlySet<Guid> recoverablePropIds)
    {
        var firstProp = skippedProps.First();
        var duplicateGroup = duplicateGroupByPlayerId.GetValueOrDefault(playerId);
        var duplicatePlayers = duplicateGroup?.Players ?? [];
        var otherDuplicatePlayers = duplicatePlayers
            .Where(player => player.PlayerId != playerId)
            .ToArray();
        var otherDuplicatePlayersWithMlbMappings = otherDuplicatePlayers
            .Where(player => player.ProviderRefs.Any(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase)))
            .Select(player => new DuplicatePlayerMappingSummary(
                player.PlayerId,
                player.DisplayName,
                player.LeagueCode,
                player.ProviderRefs
                    .Where(reference => string.Equals(reference.Provider, "mlb", StringComparison.OrdinalIgnoreCase))
                    .Select(reference => reference.ProviderPlayerId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(providerPlayerId => providerPlayerId)
                    .ToArray()))
            .ToArray();
        var differentCanonicalHistoricalMatches = skippedProps
            .SelectMany(prop => otherDuplicatePlayers
                .Where(player => HasHistoricalResult(historicalResults, player.PlayerId, prop.PropTypeId, prop.EventDate))
                .Select(player => new DifferentCanonicalHistoricalMatch(
                    prop.PropId,
                    prop.PropTypeId,
                    prop.PropType,
                    prop.EventDate,
                    player.PlayerId,
                    player.DisplayName,
                    player.LeagueCode)))
            .Distinct()
            .OrderBy(match => match.PropType)
            .ThenBy(match => match.EventDate)
            .Take(20)
            .ToArray();
        var sportsbooks = skippedProps
            .Select(prop => prop.Sportsbook)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sportsbook => sportsbook)
            .ToArray();
        var propCountsBySportsbook = skippedProps
            .GroupBy(prop => prop.Sportsbook, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SportsbookSkippedPropCount(group.Key, group.Count()))
            .OrderByDescending(group => group.PropCountSkipped)
            .ThenBy(group => group.Sportsbook)
            .ToArray();
        var skippedPropTypes = skippedProps
            .GroupBy(prop => prop.PropType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PropTypeSkippedPropCount(group.Key, group.Count()))
            .OrderByDescending(group => group.PropCountSkipped)
            .ThenBy(group => group.PropType)
            .ToArray();

        return new NoHistoricalResultPlayerReport(
            playerId,
            firstProp.PlayerName,
            firstProp.League,
            firstProp.CanonicalLeague,
            sportsbooks.FirstOrDefault() ?? "Unknown",
            sportsbooks,
            skippedProps.Count,
            skippedProps.Count(prop => recoverablePropIds.Contains(prop.PropId)),
            differentCanonicalHistoricalMatches.Length > 0,
            duplicateGroup is not null,
            otherDuplicatePlayersWithMlbMappings.Length > 0,
            duplicatePlayers
                .Select(player => new DuplicateIdentityPlayerSummary(
                    player.PlayerId,
                    player.DisplayName,
                    player.LeagueCode,
                    player.CanonicalLeagueCode,
                    player.ProviderRefs
                        .Select(reference => new PlayerProviderRefSummary(
                            reference.Provider,
                            reference.ProviderPlayerId,
                            reference.ProviderDisplayName))
                        .ToArray()))
                .ToArray(),
            otherDuplicatePlayersWithMlbMappings,
            differentCanonicalHistoricalMatches,
            propCountsBySportsbook,
            skippedPropTypes);
    }

    private static bool HasDifferentCanonicalHistoricalResult(
        NoHistoricalResultSkippedProp skippedProp,
        IReadOnlyDictionary<Guid, DuplicateIdentityGroup> duplicateGroupByPlayerId,
        IReadOnlyDictionary<HistoricalResultKey, DateOnly[]> historicalResults)
    {
        return duplicateGroupByPlayerId.TryGetValue(skippedProp.PlayerId, out var duplicateGroup)
            && duplicateGroup.Players
                .Where(player => player.PlayerId != skippedProp.PlayerId)
                .Any(player => HasHistoricalResult(historicalResults, player.PlayerId, skippedProp.PropTypeId, skippedProp.EventDate));
    }

    private static async Task<PlayerHistoricalResultSummary> LoadPlayerHistoricalResultSummaryAsync(
        ApplicationDbContext dbContext,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var summary = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.PlayerId == playerId)
            .GroupBy(result => result.PlayerId)
            .Select(group => new
            {
                TotalCount = group.Count(),
                FirstResultDate = group.Min(result => result.EventDate),
                LastResultDate = group.Max(result => result.EventDate)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var propTypeCounts = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.PlayerId == playerId)
            .GroupBy(result => result.PropTypeId)
            .Select(group => new
            {
                PropTypeId = group.Key,
                Count = group.Count(),
                FirstResultDate = group.Min(result => result.EventDate),
                LastResultDate = group.Max(result => result.EventDate)
            })
            .ToListAsync(cancellationToken);
        var propTypeIds = propTypeCounts.Select(group => group.PropTypeId).Distinct().ToArray();
        var propTypeNames = await dbContext.PropTypes
            .AsNoTracking()
            .Where(propType => propTypeIds.Contains(propType.Id))
            .Select(propType => new
            {
                propType.Id,
                propType.Name
            })
            .ToDictionaryAsync(propType => propType.Id, propType => propType.Name, cancellationToken);
        var countsByPropType = propTypeCounts
            .Select(group => new HistoricalResultPropTypeCount(
                group.PropTypeId,
                propTypeNames.GetValueOrDefault(group.PropTypeId, "UNKNOWN"),
                group.Count,
                group.FirstResultDate,
                group.LastResultDate))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.PropType)
            .ToArray();

        var leagueCounts = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.PlayerId == playerId)
            .GroupBy(result => result.LeagueId)
            .Select(group => new
            {
                LeagueId = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);
        var leagueIds = leagueCounts.Select(group => group.LeagueId).Distinct().ToArray();
        var leagueCodes = await dbContext.Leagues
            .AsNoTracking()
            .Where(league => leagueIds.Contains(league.Id))
            .Select(league => new
            {
                league.Id,
                league.Code
            })
            .ToDictionaryAsync(league => league.Id, league => league.Code, cancellationToken);
        var countsByLeague = leagueCounts
            .Select(group => new HistoricalResultLeagueCount(
                group.LeagueId,
                leagueCodes.GetValueOrDefault(group.LeagueId, "UNKNOWN"),
                group.Count))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.League)
            .ToArray();

        var externalSourceCounts = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.PlayerId == playerId)
            .GroupBy(result => result.ExternalSource ?? "UNKNOWN")
            .Select(group => new
            {
                ExternalSource = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);
        var countsByExternalSource = externalSourceCounts
            .Select(group => new HistoricalResultExternalSourceCount(
                group.ExternalSource,
                group.Count))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.ExternalSource)
            .ToArray();

        return new PlayerHistoricalResultSummary(
            summary?.TotalCount ?? 0,
            summary?.FirstResultDate,
            summary?.LastResultDate,
            countsByPropType,
            countsByLeague,
            countsByExternalSource);
    }

    private static async Task<IReadOnlyList<DuplicatePlayerHistorySummary>> LoadDuplicatePlayerHistoryAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> duplicatePlayerIds,
        CancellationToken cancellationToken)
    {
        var players = await dbContext.Players
            .AsNoTracking()
            .Include(player => player.League)
            .Where(player => duplicatePlayerIds.Contains(player.Id))
            .Select(player => new
            {
                player.Id,
                player.DisplayName,
                LeagueCode = player.League == null ? "UNKNOWN" : player.League.Code
            })
            .ToListAsync(cancellationToken);
        var resultSummaries = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => duplicatePlayerIds.Contains(result.PlayerId))
            .GroupBy(result => result.PlayerId)
            .Select(group => new
            {
                PlayerId = group.Key,
                HistoricalResultCount = group.Count(),
                FirstResultDate = group.Min(result => result.EventDate),
                LastResultDate = group.Max(result => result.EventDate)
            })
            .ToDictionaryAsync(group => group.PlayerId, cancellationToken);

        return players
            .OrderBy(player => player.DisplayName)
            .ThenBy(player => player.Id)
            .Select(player =>
            {
                resultSummaries.TryGetValue(player.Id, out var summary);

                return new DuplicatePlayerHistorySummary(
                    player.Id,
                    player.DisplayName,
                    player.LeagueCode,
                    summary?.HistoricalResultCount ?? 0,
                    summary?.FirstResultDate,
                    summary?.LastResultDate);
            })
            .ToArray();
    }

    private static async Task<PlayerPredictionGateDiagnostics> LoadPlayerPredictionGateDiagnosticsAsync(
        ApplicationDbContext dbContext,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var propRows = await dbContext.Props
            .AsNoTracking()
            .Include(prop => prop.League)
            .Include(prop => prop.PropType)
            .Include(prop => prop.PlatformProps)
            .Where(prop => prop.PlayerId == playerId
                && prop.League != null
                && prop.League.Code == "MLB"
                && prop.PropType != null)
            .Select(prop => new
            {
                prop.Id,
                prop.PropTypeId,
                PropType = prop.PropType == null ? "UNKNOWN" : prop.PropType.Name,
                prop.StartsAt,
                HasSportsbookLine = prop.PlatformProps.Any()
            })
            .ToListAsync(cancellationToken);
        var currentMlbProps = propRows
            .Where(prop => prop.HasSportsbookLine)
            .Select(prop => new
            {
                prop.Id,
                prop.PropTypeId,
                prop.PropType,
                EventDate = DateOnly.FromDateTime(prop.StartsAt.UtcDateTime)
            })
            .ToArray();
        var historicalResults = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.PlayerId == playerId)
            .Select(result => new
            {
                result.PropTypeId,
                result.EventDate
            })
            .ToListAsync(cancellationToken);
        var propsWithHistory = currentMlbProps
            .Count(prop => historicalResults.Any(result => result.PropTypeId == prop.PropTypeId && result.EventDate < prop.EventDate));
        var propsWithoutHistoryByPropType = currentMlbProps
            .Where(prop => !historicalResults.Any(result => result.PropTypeId == prop.PropTypeId && result.EventDate < prop.EventDate))
            .GroupBy(prop => prop.PropType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PropTypeSkippedPropCount(group.Key, group.Count()))
            .OrderByDescending(group => group.PropCountSkipped)
            .ThenBy(group => group.PropType)
            .ToArray();

        return new PlayerPredictionGateDiagnostics(
            currentMlbProps.Length > 0,
            currentMlbProps.Length,
            propsWithHistory,
            currentMlbProps.Length - propsWithHistory,
            propsWithoutHistoryByPropType);
    }

    private static PlayerIdentityRow ToPlayerIdentityRow(Player player)
    {
        var leagueCode = player.League?.Code ?? "UNKNOWN";

        return new PlayerIdentityRow(
            player.Id,
            player.DisplayName,
            leagueCode,
            CanonicalLeagueNormalizer.NormalizeCode(leagueCode),
            player.ExternalProviderRefs
                .OrderBy(reference => reference.Provider)
                .ThenBy(reference => reference.ProviderPlayerId)
                .Select(reference => new PlayerProviderRefSummary(
                    reference.Provider,
                    reference.ProviderPlayerId,
                    reference.ProviderDisplayName))
                .ToArray(),
            SummarizeLegacyExternalRefs(player.ExternalRefs),
            NormalizePlayerName(player.DisplayName));
    }

    private static IReadOnlyList<PlayerHistoryProviderRefSummary> BuildCombinedProviderRefs(
        Guid playerId,
        string displayName,
        IEnumerable<PlayerExternalRef> providerRefs,
        JsonDocument? legacyExternalRefs)
    {
        return providerRefs
            .OrderBy(reference => reference.Provider)
            .ThenBy(reference => reference.ProviderPlayerId)
            .Select(reference => new PlayerHistoryProviderRefSummary(
                "player_external_refs",
                reference.Provider,
                reference.ProviderPlayerId,
                reference.ProviderDisplayName))
            .Concat(SummarizeLegacyExternalRefs(legacyExternalRefs)
                .Select(reference => new PlayerHistoryProviderRefSummary(
                    "players.external_refs",
                    reference.Provider,
                    reference.ProviderPlayerId ?? string.Empty,
                    displayName)))
            .OrderBy(reference => reference.Provider)
            .ThenBy(reference => reference.Source)
            .ThenBy(reference => reference.ProviderPlayerId)
            .ToArray();
    }

    private static IReadOnlyList<LegacyProviderRefSummary> SummarizeLegacyExternalRefs(JsonDocument? externalRefs)
    {
        if (externalRefs is null || externalRefs.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var summaries = new List<LegacyProviderRefSummary>();
        foreach (var providerProperty in externalRefs.RootElement.EnumerateObject())
        {
            string? playerId = null;
            if (providerProperty.Value.ValueKind == JsonValueKind.Object
                && providerProperty.Value.TryGetProperty("player_id", out var playerIdElement))
            {
                playerId = playerIdElement.ValueKind == JsonValueKind.String
                    ? playerIdElement.GetString()
                    : playerIdElement.GetRawText();
            }

            summaries.Add(new LegacyProviderRefSummary(providerProperty.Name, playerId));
        }

        return summaries;
    }

    private static string NormalizePlayerName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(displayName.Length);

        foreach (var character in displayName.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static async Task<Guid?> FindLatestMlbPredictionRunIdAsync(
        ApplicationDbContext dbContext,
        bool excludeSampleRuns,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PredictionRuns
            .AsNoTracking()
            .Where(run => run.Predictions.Any(prediction => prediction.Prop != null
                && prediction.Prop.League != null
                && prediction.Prop.League.Code == "MLB"));

        if (excludeSampleRuns)
        {
            query = query.Where(run => run.ModelName != "SampleModel"
                && (run.Notes == null || !run.Notes.ToLower().Contains("fake predictions")));
        }

        return await query
            .OrderByDescending(run => run.CompletedAt)
            .ThenByDescending(run => run.CreatedAt)
            .Select(run => (Guid?)run.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<Prediction> BuildPredictionAuditQuery(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? propType)
    {
        var query = dbContext.Predictions
            .AsNoTracking()
            .Where(row => row.Prop != null
                && row.Prop.League != null
                && row.Prop.League.Code == "MLB");

        if (predictionRunId.HasValue)
        {
            query = query.Where(row => row.PredictionRunId == predictionRunId.Value);
        }

        if (fromDate.HasValue)
        {
            var fromDateTime = new DateTimeOffset(fromDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(row => row.Prop!.StartsAt >= fromDateTime);
        }

        if (toDate.HasValue)
        {
            var nextDateTime = new DateTimeOffset(toDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(row => row.Prop!.StartsAt < nextDateTime);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            query = query.Where(row => row.Prop!.PropType != null && row.Prop.PropType.Name == propType);
        }

        return query;
    }

    private static IQueryable<RecommendationSnapshot> BuildRecommendationAuditQuery(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? provider,
        string? propType)
    {
        var query = dbContext.RecommendationSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == "MLB");

        if (predictionRunId.HasValue)
        {
            query = query.Where(row => row.PredictionRunId == predictionRunId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(row => row.ExpectedResultDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(row => row.ExpectedResultDate <= toDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(row => row.Provider == provider || row.Bookmaker == provider || row.EntryProvider == provider);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            query = query.Where(row => row.PropType == propType);
        }

        return query;
    }

    private static IQueryable<MarketTopPickSnapshot> BuildTopPickAuditQuery(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? provider,
        string? propType)
    {
        var query = dbContext.MarketTopPickSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == "MLB");

        if (predictionRunId.HasValue)
        {
            query = query.Where(row => row.PredictionRunId == predictionRunId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(row => row.SlateDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(row => row.SlateDate <= toDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(row => row.Provider == provider || row.Bookmaker == provider || row.EntryProvider == provider);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            query = query.Where(row => row.PropType == propType);
        }

        return query;
    }

    private static IQueryable<MarketLineShoppingSnapshot> BuildLineShoppingAuditQuery(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? provider,
        string? propType)
    {
        var query = dbContext.MarketLineShoppingSnapshots
            .AsNoTracking()
            .Where(row => row.Sport == "MLB");

        if (predictionRunId.HasValue)
        {
            query = query.Where(row => row.PredictionRunId == predictionRunId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(row => row.SlateDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(row => row.SlateDate <= toDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(row => row.Provider == provider || row.Bookmaker == provider || row.EntryProvider == provider);
        }

        if (!string.IsNullOrWhiteSpace(propType))
        {
            query = query.Where(row => row.PropType == propType);
        }

        return query;
    }

    private static async Task<object> BuildPredictionSideDistributionAsync(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? propType,
        CancellationToken cancellationToken)
    {
        var rows = await BuildPredictionAuditQuery(dbContext, predictionRunId, fromDate, toDate, propType)
            .GroupBy(row => row.Recommendation)
            .Select(group => new { Side = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return BuildSideDistribution(rows.Select(row => ((string?)row.Side, row.Count)));
    }

    private static async Task<object> BuildRecommendationSideDistributionAsync(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? provider,
        string? propType,
        CancellationToken cancellationToken)
    {
        var rows = await BuildRecommendationAuditQuery(dbContext, predictionRunId, fromDate, toDate, provider, propType)
            .GroupBy(row => row.Side)
            .Select(group => new { Side = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return BuildSideDistribution(rows.Select(row => ((string?)row.Side, row.Count)));
    }

    private static async Task<object> BuildTopPickSideDistributionAsync(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? provider,
        string? propType,
        CancellationToken cancellationToken)
    {
        var rows = await BuildTopPickAuditQuery(dbContext, predictionRunId, fromDate, toDate, provider, propType)
            .GroupBy(row => row.BestSide)
            .Select(group => new { Side = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return BuildSideDistribution(rows.Select(row => ((string?)row.Side, row.Count)));
    }

    private static async Task<object> BuildLineShoppingSideDistributionAsync(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? provider,
        string? propType,
        CancellationToken cancellationToken)
    {
        var rows = await BuildLineShoppingAuditQuery(dbContext, predictionRunId, fromDate, toDate, provider, propType)
            .GroupBy(row => row.BestSide)
            .Select(group => new { Side = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return BuildSideDistribution(rows.Select(row => ((string?)row.Side, row.Count)));
    }

    private static object BuildSideDistribution(IEnumerable<(string? Side, int Count)> rows)
    {
        var counts = rows
            .GroupBy(row => NormalizeSide(row.Side))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Count), StringComparer.OrdinalIgnoreCase);
        var total = counts.Values.Sum();
        counts.TryGetValue("Over", out var over);
        counts.TryGetValue("Under", out var under);
        counts.TryGetValue("None", out var none);
        counts.TryGetValue("Missing", out var missing);
        counts.TryGetValue("Pass", out var pass);

        return new
        {
            total,
            over,
            under,
            none,
            pass,
            missing,
            overRate = CalculateRate(over, total),
            underRate = CalculateRate(under, total),
            bySide = counts
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new { side = pair.Key, count = pair.Value })
                .ToArray()
        };
    }

    private static IReadOnlyList<PredictionStageAuditRow> BuildPredictionStageAuditRows(
        PredictionProbabilityRunAnalysis? probabilityAnalysis,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? propType,
        PredictionGenerationOptions generationOptions)
    {
        if (probabilityAnalysis is null)
        {
            return [];
        }

        return probabilityAnalysis.Results
            .Where(row => !fromDate.HasValue || row.EventDate >= fromDate.Value)
            .Where(row => !toDate.HasValue || row.EventDate <= toDate.Value)
            .Where(row => string.IsNullOrWhiteSpace(propType) || row.PropType == propType)
            .Select(result => ToPredictionStageAuditRow(result, generationOptions))
            .ToArray();
    }

    private static PredictionStageAuditRow ToPredictionStageAuditRow(
        PredictionProbabilityResult result,
        PredictionGenerationOptions generationOptions)
    {
        const decimal diagnosticDecimalPrice = 1.909091m;
        var overImpliedProbability = CalculateImpliedProbability(diagnosticDecimalPrice);
        var underImpliedProbability = CalculateImpliedProbability(diagnosticDecimalPrice);
        var overBreakEvenProbability = CalculateBreakEvenProbability(overImpliedProbability, result.ProbabilityPush);
        var underBreakEvenProbability = CalculateBreakEvenProbability(underImpliedProbability, result.ProbabilityPush);
        var overEstimatedEv = CalculateEstimatedEv(result.ProbabilityOver, result.ProbabilityUnder, diagnosticDecimalPrice);
        var underEstimatedEv = CalculateEstimatedEv(result.ProbabilityUnder, result.ProbabilityOver, diagnosticDecimalPrice);
        var selectedSide = NormalizeSide(result.Recommendation);
        var selectedSideProbability = selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
            ? result.ProbabilityOver
            : selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)
                ? result.ProbabilityUnder
                : null;
        var selectedSideEv = selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
            ? overEstimatedEv
            : selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)
                ? underEstimatedEv
                : null;
        var thresholdsApplied = BuildPredictionThresholdsApplied(generationOptions);
        var overCandidate = BuildPredictionSideCandidate("Over", result, overBreakEvenProbability, overEstimatedEv, generationOptions);
        var underCandidate = BuildPredictionSideCandidate("Under", result, underBreakEvenProbability, underEstimatedEv, generationOptions);
        var passReasonCodes = BuildPredictionPassReasonCodes(result, selectedSide, overCandidate, underCandidate, generationOptions);
        var failedGates = passReasonCodes
            .Select(MapPassReasonToFailedGate)
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => reason!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suspiciousReasons = BuildPredictionStageSuspiciousReasons(
            result,
            selectedSide,
            overBreakEvenProbability,
            underBreakEvenProbability,
            overEstimatedEv,
            underEstimatedEv,
            overCandidate,
            underCandidate,
            passReasonCodes);

        return new PredictionStageAuditRow(
            result.PredictionId,
            result.PlayerName,
            result.PropType,
            Provider: null,
            Bookmaker: null,
            result.EventDate,
            result.SportsbookLine,
            result.PredictedValue,
            result.ProbabilityOver,
            result.ProbabilityUnder,
            result.ProbabilityPush,
            overBreakEvenProbability,
            underBreakEvenProbability,
            overEstimatedEv,
            underEstimatedEv,
            overEstimatedEv,
            overEstimatedEv,
            0m,
            underEstimatedEv,
            underEstimatedEv,
            0m,
            selectedSide,
            selectedSideProbability,
            selectedSideEv,
            BuildPredictionSelectionReason(result, selectedSide, overEstimatedEv, underEstimatedEv, passReasonCodes),
            passReasonCodes,
            failedGates,
            thresholdsApplied,
            overCandidate,
            underCandidate,
            overCandidate with { IncludedAfterProductionGates = overCandidate.IncludedAfterProductionGates },
            underCandidate with { IncludedAfterProductionGates = underCandidate.IncludedAfterProductionGates },
            suspiciousReasons,
            result.ConfidenceScore,
            result.RankingScore,
            result.SampleSize,
            result.MethodUsed,
            result.Edge);
    }

    private static IReadOnlyList<string> BuildPredictionStageSuspiciousReasons(
        PredictionProbabilityResult result,
        string selectedSide,
        decimal? overBreakEvenProbability,
        decimal? underBreakEvenProbability,
        decimal? overEstimatedEv,
        decimal? underEstimatedEv,
        PredictionSideCandidate overCandidate,
        PredictionSideCandidate underCandidate,
        IReadOnlyList<string> passReasonCodes)
    {
        var reasons = new List<string>();

        if (result.PredictedValue.HasValue && result.SportsbookLine.HasValue)
        {
            if (result.PredictedValue.Value > result.SportsbookLine.Value && selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("ProjectionAboveLineButSelectedUnder");
            }

            if (result.PredictedValue.Value < result.SportsbookLine.Value && selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("ProjectionBelowLineButSelectedOver");
            }

            if (result.PredictedValue.Value > result.SportsbookLine.Value
                && result.ProbabilityUnder.HasValue
                && result.ProbabilityOver.HasValue
                && result.ProbabilityUnder.Value > result.ProbabilityOver.Value)
            {
                reasons.Add("ProjectionAboveLineButUnderProbabilityHigher");
            }

            if (result.PredictedValue.Value < result.SportsbookLine.Value
                && result.ProbabilityOver.HasValue
                && result.ProbabilityUnder.HasValue
                && result.ProbabilityOver.Value > result.ProbabilityUnder.Value)
            {
                reasons.Add("ProjectionBelowLineButOverProbabilityHigher");
            }
        }

        if (overEstimatedEv.HasValue && overEstimatedEv.Value > 0m && selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("OverEvPositiveButPassed");
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
            && overEstimatedEv.HasValue
            && underEstimatedEv.HasValue
            && overEstimatedEv.Value > underEstimatedEv.Value)
        {
            reasons.Add("OverEvGreaterThanUnderEvButPassed");
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
            && result.PredictedValue.HasValue
            && result.SportsbookLine.HasValue
            && result.PredictedValue.Value > result.SportsbookLine.Value)
        {
            reasons.Add("ProjectionAboveLineButPassed");
        }

        if (underEstimatedEv.HasValue && underEstimatedEv.Value > 0m && selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("UnderEvPositiveButPassed");
        }

        if (overEstimatedEv.HasValue && overEstimatedEv.Value > 0m && selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("OverEvPositiveButSelectedUnder");
        }

        if (selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)
            && result.ProbabilityUnder.HasValue
            && underBreakEvenProbability.HasValue
            && result.ProbabilityUnder.Value < underBreakEvenProbability.Value)
        {
            reasons.Add("SelectedUnderWithModelUnderBelowBreakEven");
        }

        if (selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)
            && result.ProbabilityOver.HasValue
            && result.ProbabilityUnder.HasValue
            && result.ProbabilityOver.Value > result.ProbabilityUnder.Value)
        {
            reasons.Add("SelectedUnderWithModelOverAboveModelUnder");
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
            && result.ProbabilityOver.HasValue
            && result.ProbabilityUnder.HasValue
            && result.ProbabilityOver.Value > result.ProbabilityUnder.Value)
        {
            reasons.Add("PassWithModelOverAboveModelUnder");
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
            && overEstimatedEv.HasValue
            && overEstimatedEv.Value > 0m
            && passReasonCodes.Count == 0)
        {
            reasons.Add("PositiveOverEvButFailedUnknownGate");
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
            && passReasonCodes.Count == 0)
        {
            reasons.Add("PassReasonMissing");
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
            && result.ProbabilityOver.HasValue
            && overBreakEvenProbability.HasValue
            && result.ProbabilityOver.Value >= overBreakEvenProbability.Value)
        {
            reasons.Add("OverProbabilityAboveThresholdButPassed");
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
            && overCandidate.ProbabilityEdge.HasValue
            && overCandidate.ProbabilityEdge.Value > 0m)
        {
            reasons.Add("OverEdgeAboveThresholdButPassed");
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
            && result.PredictedValue.HasValue
            && result.SportsbookLine.HasValue
            && result.PredictedValue.Value > result.SportsbookLine.Value
            && !overCandidate.IncludedBeforeProductionGates)
        {
            reasons.Add("OverProjectionAboveLineButNoOverCandidate");
        }

        if (overEstimatedEv.HasValue
            && underEstimatedEv.HasValue
            && (overEstimatedEv.Value - underEstimatedEv.Value) != 0m
            && Math.Abs(overCandidate.DiagnosticProductionEvDelta ?? 0m) > 0.0001m)
        {
            reasons.Add("DiagnosticProductionEvMismatch");
        }

        if (selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)
            && underCandidate.Gates.Any(gate => gate.Name.Contains("Under", StringComparison.OrdinalIgnoreCase) && !gate.Passed)
            && overCandidate.Gates.All(gate => !gate.Name.Contains("Under", StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("AsymmetricUnderGateApplied");
        }

        if ((overCandidate.EstimatedEv > 0m && overCandidate.GateFailureReasons.Contains("BelowMinimumUserFacingHitProbability"))
            || (underCandidate.EstimatedEv > 0m && underCandidate.GateFailureReasons.Contains("BelowMinimumUserFacingHitProbability")))
        {
            reasons.Add("PositiveEdgeRejectedByHitProbability");
        }

        var overPassesHitProbability = overCandidate.Gates.Any(gate => gate.Name == "MinProbability" && gate.Passed);
        var underPassesHitProbability = underCandidate.Gates.Any(gate => gate.Name == "MinProbability" && gate.Passed);
        if ((overPassesHitProbability && overCandidate.GateFailureReasons.Contains("OverProjectionEdgeBelowConfiguredThreshold"))
            || (underPassesHitProbability && underCandidate.GateFailureReasons.Contains("UnderProjectionEdgeBelowConfiguredThreshold")))
        {
            reasons.Add("PassesHitProbabilityButFailsProjectionEdge");
        }

        if ((overCandidate.GateFailureReasons.Contains("BelowMinimumUserFacingHitProbability")
                && !overCandidate.GateFailureReasons.Contains("OverProjectionEdgeBelowConfiguredThreshold"))
            || (underCandidate.GateFailureReasons.Contains("BelowMinimumUserFacingHitProbability")
                && !underCandidate.GateFailureReasons.Contains("UnderProjectionEdgeBelowConfiguredThreshold")))
        {
            reasons.Add("PassesProjectionEdgeButFailsHitProbability");
        }

        return reasons
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PredictionThresholdsApplied BuildPredictionThresholdsApplied(PredictionGenerationOptions generationOptions)
    {
        var enabledPropTypes = generationOptions.EnabledPropTypes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();

        return new PredictionThresholdsApplied(
            GenerationSelectionSource: "PredictionGenerationService.GetRecommendation",
            ProductionSelectionRule: "Prediction generation selects from projection edge thresholds plus selected-side hit probability. Public DFS recommendations keep the same MinimumPickHitProbability gate; 0.50-0.55 stays research/watchlist.",
            OverProjectionEdgeThreshold: generationOptions.OverProjectionEdgeThreshold,
            UnderProjectionEdgeThreshold: generationOptions.UnderProjectionEdgeThreshold,
            EnabledPropTypes: enabledPropTypes,
            MinimumPickHitProbability: generationOptions.MinimumPickHitProbability,
            StrongPickHitProbability: generationOptions.StrongPickHitProbability,
            ResearchOnlyBelowHitProbability: generationOptions.ResearchOnlyBelowHitProbability,
            MinExpectedValueThreshold: null,
            MinTrustLevel: null,
            MinSampleSize: null);
    }

    private static PredictionSideCandidate BuildPredictionSideCandidate(
        string side,
        PredictionProbabilityResult result,
        decimal? breakEvenProbability,
        decimal? estimatedEv,
        PredictionGenerationOptions generationOptions)
    {
        var edge = result.Edge;
        var isOver = side.Equals("Over", StringComparison.OrdinalIgnoreCase);
        var modelProbability = isOver ? result.ProbabilityOver : result.ProbabilityUnder;
        var projectionDelta = result.PredictedValue.HasValue && result.SportsbookLine.HasValue
            ? Math.Round(result.PredictedValue.Value - result.SportsbookLine.Value, 4)
            : (decimal?)null;
        var productionEdgeGatePassed = edge.HasValue && (isOver
            ? edge.Value >= generationOptions.OverProjectionEdgeThreshold
            : edge.Value <= -generationOptions.UnderProjectionEdgeThreshold);
        var hitProbabilityGatePassed = modelProbability.HasValue && modelProbability.Value >= generationOptions.MinimumPickHitProbability;
        var researchProbabilityGatePassed = modelProbability.HasValue && modelProbability.Value >= generationOptions.ResearchOnlyBelowHitProbability;
        var directionSupportsSide = projectionDelta.HasValue && (isOver
            ? projectionDelta.Value > 0m
            : projectionDelta.Value < 0m);
        var propTypeEnabled = IsPropTypeEnabledForPredictionGeneration(result.PropType, generationOptions);
        var includedBeforeGates = directionSupportsSide || (estimatedEv.HasValue && estimatedEv.Value > 0m);
        var gateFailures = new List<string>();
        if (!edge.HasValue)
        {
            gateFailures.Add("MissingProjectionEdge");
        }
        else if (!productionEdgeGatePassed)
        {
            gateFailures.Add(isOver ? "OverProjectionEdgeBelowConfiguredThreshold" : "UnderProjectionEdgeBelowConfiguredThreshold");
        }

        if (!hitProbabilityGatePassed)
        {
            gateFailures.Add("BelowMinimumUserFacingHitProbability");
        }

        if (!propTypeEnabled)
        {
            gateFailures.Add("PropTypeDisabledForPredictionGeneration");
        }

        var gates = new[]
        {
            new PredictionCandidateGate(
                isOver ? "OverProjectionEdgeThreshold" : "UnderProjectionEdgeThreshold",
                productionEdgeGatePassed,
                edge,
                isOver ? generationOptions.OverProjectionEdgeThreshold : -generationOptions.UnderProjectionEdgeThreshold,
                isOver ? ">=" : "<=",
                "Production generation gate."),
            new PredictionCandidateGate(
                "PropTypeEnabled",
                propTypeEnabled,
                null,
                null,
                "contains",
                "Production generation prop-type allowlist gate."),
            new PredictionCandidateGate(
                "MinProbability",
                hitProbabilityGatePassed,
                modelProbability,
                generationOptions.MinimumPickHitProbability,
                ">=",
                "User-facing DFS candidate gate."),
            new PredictionCandidateGate(
                "MinExpectedValue",
                true,
                estimatedEv,
                null,
                "not_applied",
                "Production generation does not use diagnostic EV."),
            new PredictionCandidateGate(
                "SampleSize",
                true,
                result.SampleSize,
                null,
                "not_applied",
                "Production generation does not gate on probability sample size.")
        };

        return new PredictionSideCandidate(
            Side: side,
            ModelProbability: modelProbability,
            BreakEvenProbability: breakEvenProbability,
            ProbabilityEdge: modelProbability.HasValue && breakEvenProbability.HasValue ? Math.Round(modelProbability.Value - breakEvenProbability.Value, 6) : null,
            EstimatedEv: estimatedEv,
            DiagnosticEv: estimatedEv,
            ProductionEv: estimatedEv,
            DiagnosticProductionEvDelta: 0m,
            ModelProjection: result.PredictedValue,
            Line: result.SportsbookLine,
            ProjectionDelta: projectionDelta,
            SampleSize: result.SampleSize,
            TrustLevel: BuildPredictionTrustLevel(result.ConfidenceScore),
            ConfidenceScore: result.ConfidenceScore,
            IncludedBeforeProductionGates: includedBeforeGates && researchProbabilityGatePassed,
            IncludedAfterProductionGates: includedBeforeGates && productionEdgeGatePassed && propTypeEnabled && hitProbabilityGatePassed,
            Gates: gates,
            GateFailureReasons: gateFailures);
    }

    private static IReadOnlyList<string> BuildPredictionPassReasonCodes(
        PredictionProbabilityResult result,
        string selectedSide,
        PredictionSideCandidate overCandidate,
        PredictionSideCandidate underCandidate,
        PredictionGenerationOptions generationOptions)
    {
        if (!selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var reasons = new List<string>();
        if (!result.SportsbookLine.HasValue)
        {
            reasons.Add("MissingSportsbookLine");
        }

        var propTypeEnabled = IsPropTypeEnabledForPredictionGeneration(result.PropType, generationOptions);
        var overClearedProjectionGate = result.Edge.HasValue && result.Edge.Value >= generationOptions.OverProjectionEdgeThreshold;
        var underClearedProjectionGate = result.Edge.HasValue && result.Edge.Value <= -generationOptions.UnderProjectionEdgeThreshold;
        if (!overClearedProjectionGate && !underClearedProjectionGate)
        {
            reasons.Add("RecommendationSuppressedByThreshold");
        }

        if ((overClearedProjectionGate || underClearedProjectionGate) && !propTypeEnabled)
        {
            reasons.Add("RecommendationSuppressedByPropTypeFilter");
        }

        if (overCandidate.EstimatedEv.HasValue
            && overCandidate.EstimatedEv.Value > 0m
            && !overClearedProjectionGate)
        {
            reasons.Add("DiagnosticOverEvPositiveButProductionOverEdgeBelowThreshold");
        }

        if ((overClearedProjectionGate && !overCandidate.IncludedAfterProductionGates)
            || (underClearedProjectionGate && !underCandidate.IncludedAfterProductionGates))
        {
            reasons.Add("BelowMinimumUserFacingHitProbability");
        }

        if (overCandidate.EstimatedEv.HasValue
            && underCandidate.EstimatedEv.HasValue
            && overCandidate.EstimatedEv.Value > underCandidate.EstimatedEv.Value
            && !overClearedProjectionGate)
        {
            reasons.Add("DiagnosticOverEvBeatsUnderButProductionOverEdgeBelowThreshold");
        }

        return reasons
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? MapPassReasonToFailedGate(string passReason)
    {
        return passReason switch
        {
            "MissingSportsbookLine" => "SportsbookLine",
            "RecommendationSuppressedByThreshold" => "GenerationProjectionEdgeThreshold",
            "RecommendationSuppressedByPropTypeFilter" => "PropTypeAllowlist",
            "RecommendationSuppressedByHitProbability" => "MinProbability",
            "RecommendationSuppressedByMissingProbability" => "ModelProbability",
            "BelowMinimumUserFacingHitProbability" => "MinProbability",
            "DiagnosticOverEvPositiveButProductionOverEdgeBelowThreshold" => "GenerationProjectionEdgeThreshold",
            "DiagnosticOverEvBeatsUnderButProductionOverEdgeBelowThreshold" => "GenerationProjectionEdgeThreshold",
            _ => "UnknownGate"
        };
    }

    private static bool IsPropTypeEnabledForPredictionGeneration(string propType, PredictionGenerationOptions generationOptions)
    {
        if (generationOptions.EnabledPropTypes.Length == 0)
        {
            return true;
        }

        var normalized = NormalizePredictionPropTypeName(propType);
        return generationOptions.EnabledPropTypes
            .Select(NormalizePredictionPropTypeName)
            .Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePredictionPropTypeName(string propType)
    {
        return new string(propType.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private static string NormalizeSideName(string? side)
    {
        var normalized = side?.Trim().ToLowerInvariant();
        return normalized is "under" or "lower" ? "Under" : normalized is "over" or "higher" ? "Over" : string.Empty;
    }

    private static string NormalizeProviderName(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return string.Empty;
        }

        var normalized = new string(provider.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return normalized switch
        {
            "betrdfs" or "betrpicks" or "betrusdfs" => "betrpicks",
            "underdogfantasy" or "underdog" => "underdog",
            "prizepicks" or "prizepicksdfs" => "prizepicks",
            "draftkingspick6" or "pick6" => "draftkingspick6",
            _ => normalized
        };
    }

    private static string? ReadJsonString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static decimal? ReadJsonDecimal(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
            {
                return decimalValue;
            }

            if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out decimalValue))
            {
                return decimalValue;
            }
        }

        return null;
    }

    private static int? ReadJsonInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out intValue))
            {
                return intValue;
            }
        }

        return null;
    }

    private static DateOnly? ReadJsonDateOnly(JsonElement element, params string[] propertyNames)
    {
        var value = ReadJsonString(element, propertyNames);
        return DateOnly.TryParse(value, out var date) ? date : null;
    }

    private static string? ReadFirstBookmakerPlatformPropId(JsonElement element)
    {
        if (!element.TryGetProperty("bookmakerPrices", out var prices) && !element.TryGetProperty("BookmakerPrices", out prices))
        {
            return null;
        }

        if (prices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var price in prices.EnumerateArray())
        {
            var platformPropId = ReadJsonString(price, "platformPropId", "PlatformPropId");
            if (!string.IsNullOrWhiteSpace(platformPropId))
            {
                return platformPropId;
            }
        }

        return null;
    }

    private static string BuildPredictionTrustLevel(decimal confidenceScore)
    {
        if (confidenceScore >= 0.75m)
        {
            return "High";
        }

        if (confidenceScore >= 0.55m)
        {
            return "Medium";
        }

        return "Low";
    }

    private static string BuildPredictionSelectionReason(
        PredictionProbabilityResult result,
        string selectedSide,
        decimal? overEstimatedEv,
        decimal? underEstimatedEv,
        IReadOnlyList<string> passReasonCodes)
    {
        if (!result.SportsbookLine.HasValue)
        {
            return "Missing sportsbook line.";
        }

        if (selectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase))
        {
            var reasons = passReasonCodes.Count == 0 ? "PassReasonMissing" : string.Join(", ", passReasonCodes);
            return $"Production generation returned Pass because {reasons}. Projection edge={result.Edge?.ToString("0.####") ?? "n/a"}. Diagnostic over EV={overEstimatedEv?.ToString("0.####") ?? "n/a"}, under EV={underEstimatedEv?.ToString("0.####") ?? "n/a"}. Diagnostic EV is read-only and is not a production selection gate.";
        }

        return $"Prediction generation selected {selectedSide} from projection edge {result.Edge?.ToString("0.####") ?? "n/a"}; diagnostic EV is estimated from neutral -110 pricing.";
    }

    private static PredictionStageSummary BuildPredictionStageSummary(
        IReadOnlyCollection<PredictionStageAuditRow> rows,
        Guid? latestPredictionRunId,
        DateTimeOffset? latestPredictionRunCreatedAtUtc)
    {
        var total = rows.Count;
        var withLine = rows.Count(row => row.Line.HasValue);
        var missingLine = rows.Count(row => !row.Line.HasValue);
        var overSelections = rows.Count(row => row.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase));
        var underSelections = rows.Count(row => row.SelectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase));
        var passSelections = rows.Count(row => row.SelectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase));
        var aboveLine = rows.Count(row => row.Projection.HasValue && row.Line.HasValue && row.Projection.Value > row.Line.Value);
        var belowLine = rows.Count(row => row.Projection.HasValue && row.Line.HasValue && row.Projection.Value < row.Line.Value);
        var equalsLine = rows.Count(row => row.Projection.HasValue && row.Line.HasValue && row.Projection.Value == row.Line.Value);

        return new PredictionStageSummary(
            latestPredictionRunId,
            latestPredictionRunCreatedAtUtc,
            total,
            withLine,
            missingLine,
            overSelections,
            underSelections,
            passSelections,
            CalculateRate(overSelections, total),
            CalculateRate(underSelections, total),
            CalculateRate(passSelections, total),
            aboveLine,
            belowLine,
            equalsLine,
            rows.Count(row => row.SuspiciousReasons.Contains("ProjectionAboveLineButSelectedUnder")),
            rows.Count(row => row.SuspiciousReasons.Contains("ProjectionBelowLineButSelectedOver")),
            rows.Count(row => row.ModelOverProbability > row.ModelUnderProbability),
            rows.Count(row => row.ModelUnderProbability > row.ModelOverProbability),
            rows.Count(row => row.OverEstimatedEv > row.UnderEstimatedEv),
            rows.Count(row => row.UnderEstimatedEv > row.OverEstimatedEv),
            rows.Count(row => row.OverEstimatedEv > 0m),
            rows.Count(row => row.UnderEstimatedEv > 0m),
            rows.Count(row => row.SuspiciousReasons.Contains("OverEvPositiveButPassed")),
            rows.Count(row => row.SuspiciousReasons.Contains("UnderEvPositiveButPassed")),
            rows.Count(row => row.SuspiciousReasons.Contains("OverEvPositiveButSelectedUnder")),
            rows.Count(row => row.SuspiciousReasons.Contains("SelectedUnderWithModelUnderBelowBreakEven")),
            rows.Count(row => row.SuspiciousReasons.Contains("SelectedUnderWithModelOverAboveModelUnder")),
            rows.Count(row => row.SuspiciousReasons.Contains("ProjectionAboveLineButSelectedUnder")),
            rows.Count(row => row.SuspiciousReasons.Contains("ProjectionAboveLineButUnderProbabilityHigher")
                || row.SuspiciousReasons.Contains("ProjectionBelowLineButOverProbabilityHigher")
                || row.SuspiciousReasons.Contains("SelectedUnderWithModelOverAboveModelUnder")),
            rows.Count(row => row.SelectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase)
                && row.OverCandidateAfterGates.IncludedAfterProductionGates),
            rows.Count(row => row.SuspiciousReasons.Contains("PassReasonMissing")));
    }

    private static PredictionPassGateSummary BuildPredictionPassGateSummary(IReadOnlyCollection<PredictionStageAuditRow> rows)
    {
        var passRows = rows
            .Where(row => row.SelectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var overPositivePassRows = passRows
            .Where(row => row.OverEstimatedEv > 0m)
            .ToArray();

        return new PredictionPassGateSummary(
            PassRows: passRows.Length,
            PassRowsWithPositiveOverEv: overPositivePassRows.Length,
            PassRowsWhereOverEvBeatsUnderEv: passRows.Count(row => row.OverEstimatedEv > row.UnderEstimatedEv),
            PassRowsWhereProjectionAboveLine: passRows.Count(row => row.Projection.HasValue && row.Line.HasValue && row.Projection.Value > row.Line.Value),
            PassRowsRejectedByGenerationProjectionEdgeThreshold: passRows.Count(row => row.FailedGates.Contains("GenerationProjectionEdgeThreshold")),
            PassRowsRejectedByPropTypeAllowlist: passRows.Count(row => row.FailedGates.Contains("PropTypeAllowlist")),
            PassRowsRejectedByUnknownGate: passRows.Count(row => row.FailedGates.Contains("UnknownGate") || row.SuspiciousReasons.Contains("PassReasonMissing")),
            OverPositivePassRowsRejectedByGenerationProjectionEdgeThreshold: overPositivePassRows.Count(row => row.FailedGates.Contains("GenerationProjectionEdgeThreshold")),
            OverPositivePassRowsRejectedByPropTypeAllowlist: overPositivePassRows.Count(row => row.FailedGates.Contains("PropTypeAllowlist")),
            OverPositivePassRowsRejectedByUnknownGate: overPositivePassRows.Count(row => row.FailedGates.Contains("UnknownGate") || row.SuspiciousReasons.Contains("PassReasonMissing")),
            DiagnosticOverEvPositiveButProductionOverEdgeBelowThresholdCount: passRows.Count(row => row.PassReasonCodes.Contains("DiagnosticOverEvPositiveButProductionOverEdgeBelowThreshold")),
            DiagnosticOverEvBeatsUnderButProductionOverEdgeBelowThresholdCount: passRows.Count(row => row.PassReasonCodes.Contains("DiagnosticOverEvBeatsUnderButProductionOverEdgeBelowThreshold")),
            PassReasonsByCount: CountStrings(passRows.SelectMany(row => row.PassReasonCodes)),
            FailedGatesByCount: CountStrings(passRows.SelectMany(row => row.FailedGates)));
    }

    private static PredictionHitProbabilityDiagnostics BuildPredictionHitProbabilityDiagnostics(
        IReadOnlyCollection<PredictionStageAuditRow> rows,
        PredictionGenerationOptions options)
    {
        var selectedRows = rows.Where(row => row.SelectedSide is "Over" or "Under").ToArray();
        var overRejectedByHitProbability = rows.Count(row => row.OverCandidateBeforeGates.IncludedBeforeProductionGates
            && row.OverCandidateBeforeGates.GateFailureReasons.Contains("BelowMinimumUserFacingHitProbability"));
        var underRejectedByHitProbability = rows.Count(row => row.UnderCandidateBeforeGates.IncludedBeforeProductionGates
            && row.UnderCandidateBeforeGates.GateFailureReasons.Contains("BelowMinimumUserFacingHitProbability"));
        var overRejectedByProjection = rows.Count(row => row.OverCandidateBeforeGates.GateFailureReasons.Contains("OverProjectionEdgeBelowConfiguredThreshold"));
        var underRejectedByProjection = rows.Count(row => row.UnderCandidateBeforeGates.GateFailureReasons.Contains("UnderProjectionEdgeBelowConfiguredThreshold"));
        var projectedSelections = rows
            .Select(row => SelectProjectedDfsCandidateSide(row.OverCandidateAfterGates, row.UnderCandidateAfterGates))
            .GroupBy(side => side, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new PredictionHitProbabilityDiagnostics(
            MinimumPickHitProbability: options.MinimumPickHitProbability,
            StrongPickHitProbability: options.StrongPickHitProbability,
            ResearchOnlyBelowHitProbability: options.ResearchOnlyBelowHitProbability,
            SelectedRowsBelow50Count: selectedRows.Count(row => row.SelectedSideProbability < 0.50m),
            SelectedRowsBelowMinimumHitProbabilityCount: selectedRows.Count(row => row.SelectedSideProbability < options.MinimumPickHitProbability),
            OverRowsRejectedByHitProbability: overRejectedByHitProbability,
            UnderRowsRejectedByHitProbability: underRejectedByHitProbability,
            OverRowsRejectedByProjectionThreshold: overRejectedByProjection,
            UnderRowsRejectedByProjectionThreshold: underRejectedByProjection,
            CandidateSideDistributionAfterFix: projectedSelections);
    }

    private static string SelectProjectedDfsCandidateSide(
        PredictionSideCandidate overCandidate,
        PredictionSideCandidate underCandidate)
    {
        var candidates = new[] { overCandidate, underCandidate }
            .Where(candidate => candidate.IncludedAfterProductionGates)
            .OrderByDescending(candidate => candidate.ModelProbability ?? decimal.MinValue)
            .ThenByDescending(candidate => candidate.ProjectionDelta.HasValue ? Math.Abs(candidate.ProjectionDelta.Value) : decimal.MinValue)
            .ThenByDescending(candidate => candidate.ConfidenceScore)
            .ToArray();

        return candidates.FirstOrDefault()?.Side ?? "Pass";
    }

    private static IReadOnlyDictionary<string, int> CountStrings(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private static object[] BuildPredictionSamples(
        IEnumerable<PredictionStageAuditRow> rows,
        bool includeSamples,
        int maxSamples)
    {
        if (!includeSamples)
        {
            return [];
        }

        return rows
            .Take(maxSamples)
            .Select(row => new
            {
                predictionId = row.PredictionId,
                playerName = row.PlayerName,
                propType = row.PropType,
                provider = row.Provider,
                bookmaker = row.Bookmaker,
                eventDate = row.EventDate,
                line = row.Line,
                projection = row.Projection,
                predictedMean = row.Projection,
                modelProjection = row.Projection,
                modelOverProbability = row.ModelOverProbability,
                modelUnderProbability = row.ModelUnderProbability,
                modelPushProbability = row.ModelPushProbability,
                overBreakEvenProbability = row.OverBreakEvenProbability,
                underBreakEvenProbability = row.UnderBreakEvenProbability,
                overEstimatedEv = row.OverEstimatedEv,
                underEstimatedEv = row.UnderEstimatedEv,
                diagnosticOverEv = row.DiagnosticOverEv,
                productionOverEv = row.ProductionOverEv,
                diagnosticUnderEv = row.DiagnosticUnderEv,
                productionUnderEv = row.ProductionUnderEv,
                diagnosticProductionEvDeltaOver = row.DiagnosticProductionEvDeltaOver,
                diagnosticProductionEvDeltaUnder = row.DiagnosticProductionEvDeltaUnder,
                selectedSide = row.SelectedSide,
                selectedSideProbability = row.SelectedSideProbability,
                selectedSideEv = row.SelectedSideEv,
                passReason = row.SelectedSide.Equals("Pass", StringComparison.OrdinalIgnoreCase) ? row.SelectionReason : null,
                passReasonCodes = row.PassReasonCodes,
                failedGates = row.FailedGates,
                thresholdsApplied = row.ThresholdsApplied,
                overCandidateBeforeGates = row.OverCandidateBeforeGates,
                underCandidateBeforeGates = row.UnderCandidateBeforeGates,
                overCandidateAfterGates = row.OverCandidateAfterGates,
                underCandidateAfterGates = row.UnderCandidateAfterGates,
                selectionReason = row.SelectionReason,
                suspiciousReasons = row.SuspiciousReasons,
                row.ConfidenceScore,
                row.RankingScore,
                row.SampleSize,
                row.ProbabilityMethod,
                row.Edge
            })
            .ToArray();
    }

    private static SideSelectionAuditProjection ToSideSelectionAuditRow(MarketLineShoppingSnapshot row)
    {
        var details = row.DetailsJson.RootElement;
        var modelProbabilityOver = GetDecimal(details, "modelProbabilityOver");
        var modelProbabilityUnder = GetDecimal(details, "modelProbabilityUnder")
            ?? (modelProbabilityOver.HasValue ? Math.Round(1m - modelProbabilityOver.Value, 6) : null);
        var modelProbabilityPush = GetDecimal(details, "modelProbabilityPush");
        var selectedSide = NormalizeSide(row.BestSide);
        var selectedSideProbability = row.BestProbability
            ?? (selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)
                ? modelProbabilityUnder
                : selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
                    ? modelProbabilityOver
                    : null);
        var impliedProbability = row.BestImpliedProbability ?? GetDecimal(details, "bestImpliedProbability");
        var probabilityEdge = row.BestProbabilityEdge ?? GetDecimal(details, "bestProbabilityEdge");
        var estimatedEv = row.BestEstimatedEv ?? GetDecimal(details, "bestEstimatedEv");
        var breakEvenProbability = CalculateBreakEvenProbability(impliedProbability, modelProbabilityPush);
        var reasonCodes = BuildSideSelectionReasonCodes(
            row,
            selectedSide,
            modelProbabilityOver,
            modelProbabilityUnder,
            modelProbabilityPush,
            selectedSideProbability,
            breakEvenProbability,
            probabilityEdge,
            estimatedEv);
        var selectedSideSource = string.IsNullOrWhiteSpace(row.BestSide) || selectedSide is "Missing" or "None"
            ? "MissingOrNone"
            : "MarketLineShoppingSnapshot.BestSide";

        var summary = new SideSelectionAuditSummary(
            selectedSide,
            modelProbabilityOver,
            modelProbabilityUnder,
            estimatedEv,
            reasonCodes);
        var detail = new
        {
            snapshotId = row.Id,
            row.PredictionRunId,
            row.TheOddsRefreshRunId,
            player = row.Player,
            propType = row.PropType,
            provider = row.Provider,
            bookmaker = row.Bookmaker,
            eventDate = row.SlateDate,
            eventStartTimeUtc = row.EventStartTime,
            line = row.Line,
            selectedSide,
            selectedSideSource,
            modelOverProbability = modelProbabilityOver,
            modelUnderProbability = modelProbabilityUnder,
            modelPushProbability = modelProbabilityPush,
            selectedSideProbability,
            impliedProbability,
            breakEvenProbability,
            probabilityEdge,
            estimatedEv,
            modelProjection = (decimal?)null,
            providerLine = row.Line,
            recommendationScore = GetDecimal(details, "rankingScore"),
            strategyClassification = row.StrategyClassification,
            row.StrategyTier,
            row.LifecycleStatus,
            row.IsCandidate,
            row.IsWatchlist,
            row.IsAvoid,
            row.IsResearchOnly,
            reasonCodes,
            diagnostics = new
            {
                row.BestSide,
                row.BestProbability,
                row.BestImpliedProbability,
                row.BestProbabilityEdge,
                row.BestEstimatedEv,
                bestOverEstimatedEv = GetDecimal(details, "bestOverEstimatedEv"),
                bestUnderEstimatedEv = GetDecimal(details, "bestUnderEstimatedEv"),
                bestOverAmericanPrice = GetInt(details, "bestOverAmericanPrice"),
                bestUnderAmericanPrice = GetInt(details, "bestUnderAmericanPrice"),
                probabilitySource = modelProbabilityOver.HasValue && modelProbabilityUnder.HasValue
                    ? "MarketLineShoppingSnapshot.DetailsJson"
                    : "Unavailable"
            }
        };

        return new SideSelectionAuditProjection(summary, detail);
    }

    private static IReadOnlyList<string> BuildSideSelectionReasonCodes(
        MarketLineShoppingSnapshot row,
        string selectedSide,
        decimal? modelProbabilityOver,
        decimal? modelProbabilityUnder,
        decimal? modelProbabilityPush,
        decimal? selectedSideProbability,
        decimal? breakEvenProbability,
        decimal? probabilityEdge,
        decimal? estimatedEv)
    {
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(row.BestSide) || selectedSide is "Missing" or "None")
        {
            reasons.Add("MissingSideSelection");
        }

        if (selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase))
        {
            if (modelProbabilityUnder.HasValue && modelProbabilityUnder.Value < 0.50m)
            {
                reasons.Add("UnderSelectedWithModelUnderBelowFifty");
            }

            if (modelProbabilityOver.HasValue && modelProbabilityOver.Value > 0.50m)
            {
                reasons.Add("UnderSelectedWithModelOverAboveFifty");
            }

            if (modelProbabilityOver.HasValue && modelProbabilityUnder.HasValue && modelProbabilityOver.Value > modelProbabilityUnder.Value)
            {
                reasons.Add("PossibleSideInversion");
            }
        }

        if (selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase))
        {
            if (modelProbabilityOver.HasValue && modelProbabilityOver.Value < 0.50m)
            {
                reasons.Add("OverSelectedWithModelOverBelowFifty");
            }

            if (modelProbabilityOver.HasValue && modelProbabilityUnder.HasValue && modelProbabilityUnder.Value > modelProbabilityOver.Value)
            {
                reasons.Add("PossibleSideInversion");
            }
        }

        if (estimatedEv.HasValue && estimatedEv.Value > 0m
            && selectedSideProbability.HasValue
            && breakEvenProbability.HasValue
            && selectedSideProbability.Value <= breakEvenProbability.Value)
        {
            reasons.Add("PositiveEvButSelectedProbabilityBelowBreakEven");
        }

        if (selectedSideProbability.HasValue && breakEvenProbability.HasValue && probabilityEdge.HasValue)
        {
            var expectedEdge = Math.Round(selectedSideProbability.Value - breakEvenProbability.Value, 6);
            if (Math.Abs(expectedEdge - probabilityEdge.Value) > 0.0005m)
            {
                reasons.Add(modelProbabilityPush.GetValueOrDefault() > 0m
                    ? "ProbabilityEdgeUsesRawImpliedProbabilityWithPushProbability"
                    : "ProbabilityEdgeDoesNotMatchSelectedProbability");
            }
        }

        if (selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)
            && modelProbabilityUnder.HasValue
            && modelProbabilityUnder.Value < 0.50m
            && estimatedEv.HasValue
            && estimatedEv.Value > 0m)
        {
            reasons.Add("UnderPositiveEvBelowMinProbability");
        }

        return reasons
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<int> CountSideLabelChangesAsync(
        ApplicationDbContext dbContext,
        Guid? predictionRunId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? provider,
        string? propType,
        CancellationToken cancellationToken)
    {
        var recommendations = await BuildRecommendationAuditQuery(dbContext, predictionRunId, fromDate, toDate, provider, propType)
            .Select(row => new
            {
                row.PredictionRunId,
                row.EventId,
                row.PlayerName,
                row.PropType,
                row.Line,
                row.Bookmaker,
                row.Provider,
                row.Side
            })
            .ToListAsync(cancellationToken);
        var lineShoppingRows = await BuildLineShoppingAuditQuery(dbContext, predictionRunId, fromDate, toDate, provider, propType)
            .Select(row => new
            {
                row.PredictionRunId,
                row.EventId,
                row.Player,
                row.PropType,
                row.Line,
                row.Bookmaker,
                row.Provider,
                row.BestSide
            })
            .ToListAsync(cancellationToken);
        var lineSideByKey = lineShoppingRows
            .GroupBy(row => BuildSideContinuityKey(row.PredictionRunId, row.EventId, row.Player, row.PropType, row.Line, row.Bookmaker, row.Provider))
            .ToDictionary(group => group.Key, group => NormalizeSide(group.First().BestSide), StringComparer.OrdinalIgnoreCase);

        return recommendations.Count(row =>
        {
            var key = BuildSideContinuityKey(row.PredictionRunId, row.EventId, row.PlayerName, row.PropType, row.Line, row.Bookmaker, row.Provider);
            return lineSideByKey.TryGetValue(key, out var lineSide)
                && !NormalizeSide(row.Side).Equals(lineSide, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string BuildSideContinuityKey(
        Guid? predictionRunId,
        string? eventId,
        string? player,
        string? propType,
        decimal line,
        string? bookmaker,
        string? provider)
    {
        return string.Join("|", new[]
        {
            predictionRunId?.ToString("D") ?? string.Empty,
            eventId ?? string.Empty,
            NormalizePlayerName(player),
            NormalizePlayerName(propType),
            line.ToString("0.####"),
            NormalizePlayerName(bookmaker),
            NormalizePlayerName(provider)
        });
    }

    private static string NormalizeSide(string? side)
    {
        if (string.IsNullOrWhiteSpace(side))
        {
            return "Missing";
        }

        if (side.Equals("OVER", StringComparison.OrdinalIgnoreCase))
        {
            return "Over";
        }

        if (side.Equals("UNDER", StringComparison.OrdinalIgnoreCase))
        {
            return "Under";
        }

        if (side.Equals("PASS", StringComparison.OrdinalIgnoreCase))
        {
            return "Pass";
        }

        if (side.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            return "None";
        }

        return side;
    }

    private static decimal? CalculateRate(int numerator, int denominator)
    {
        return denominator <= 0
            ? null
            : Math.Round((decimal)numerator / denominator, 4);
    }

    private static decimal? CalculateImpliedProbability(decimal? decimalPrice)
    {
        return decimalPrice.HasValue && decimalPrice.Value > 0m
            ? Math.Round(1m / decimalPrice.Value, 6)
            : null;
    }

    private static decimal? CalculateEstimatedEv(decimal? winProbability, decimal? loseProbability, decimal? decimalPrice)
    {
        return winProbability.HasValue && loseProbability.HasValue && decimalPrice.HasValue
            ? Math.Round((winProbability.Value * (decimalPrice.Value - 1m)) - loseProbability.Value, 6)
            : null;
    }

    private static decimal? CalculateBreakEvenProbability(decimal? impliedProbability, decimal? pushProbability)
    {
        if (!impliedProbability.HasValue)
        {
            return null;
        }

        if (!pushProbability.HasValue || pushProbability.Value <= 0m)
        {
            return impliedProbability;
        }

        return Math.Round(impliedProbability.Value * (1m - pushProbability.Value), 6);
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static string BuildSideSelectionAuditRecommendation(
        int totalRows,
        PredictionStageSummary predictionStageSummary,
        int possibleSideInversionCount,
        int possibleEvProbabilityMismatchCount,
        int missingSideDefaultedCount,
        int sideLabelChangedCount,
        int underSelections,
        int overSelections)
    {
        if (totalRows == 0)
        {
            if (predictionStageSummary.PredictionRowsEvaluated == 0)
            {
                return "No persisted line-shopping rows or prediction rows matched the requested filters. Run a prediction/market refresh first or widen the filters.";
            }

            if (predictionStageSummary.ProjectionAboveLineButSelectedUnderCount > 0
                || predictionStageSummary.SelectedUnderWithProjectionAboveLineCount > 0)
            {
                return "Prediction-stage diagnostics found Under selections where the projected value is above the line. Investigate prediction recommendation orientation before changing downstream snapshots.";
            }

            if (predictionStageSummary.PredictionRowsWhereProjectionBelowLine > predictionStageSummary.PredictionRowsWhereProjectionAboveLine * 3
                && predictionStageSummary.PredictionRowsWhereProjectionBelowLine >= 20)
            {
                return "No persisted line-shopping rows matched the requested filters. Prediction-stage diagnostics show projections are mostly below the line, which explains Under dominance before downstream selection.";
            }

            if (predictionStageSummary.OverEvPositiveCount > 0 && predictionStageSummary.PredictionOverSelections == 0)
            {
                return "No persisted line-shopping rows matched the requested filters. Prediction-stage diagnostics found positive diagnostic Over EV but no Over selections; review generation thresholds and projection-vs-line selection.";
            }

            if (predictionStageSummary.OverEvGreaterThanUnderEvCount == 0)
            {
                return "No persisted line-shopping rows matched the requested filters. Prediction-stage diagnostics show Under EV beats Over EV on every evaluated row.";
            }

            return "No persisted line-shopping rows matched the requested filters. Use predictionStageSummary and prediction sample groups to diagnose the upstream prediction-side imbalance.";
        }

        if (missingSideDefaultedCount > 0)
        {
            return "Investigate missing/None BestSide rows before changing side selection; missing side should not silently become Under.";
        }

        if (possibleEvProbabilityMismatchCount > 0)
        {
            return "Investigate EV/probability-edge consistency first; selected-side probability may not match break-even math on some rows.";
        }

        if (sideLabelChangedCount > 0)
        {
            return "Investigate side label continuity between line-shopping snapshots and recommendation ledger rows before changing model logic.";
        }

        if (possibleSideInversionCount > 0)
        {
            return "Review sampled rows with PossibleSideInversion; high Under share may be valid if Under EV beats Over EV, but these rows need manual inspection.";
        }

        if (underSelections > overSelections * 3 && underSelections >= 20)
        {
            return "Under selections are heavily concentrated, but no direct probability/EV mismatch was detected. Review model distribution, market pricing, and prop mix before changing selection code.";
        }

        return "No direct persisted evidence of side inversion was detected. Keep monitoring side distribution and inspect samples before changing recommendation behavior.";
    }

    private static PredictionRunResponse ToRunResponse(PredictionRun run)
    {
        return new PredictionRunResponse(
            run.Id,
            run.ModelName,
            run.ModelVersion,
            run.CreatedAt,
            run.CompletedAt,
            run.Notes,
            run.Predictions.Count);
    }

    private static object ToPredictionGenerationResponse(PredictionGenerationResult result)
    {
        return new
        {
            runId = result.RunId,
            modelName = result.ModelName,
            modelVersion = result.ModelVersion,
            scope = result.Scope,
            propsEvaluated = result.PropsEvaluated,
            predictionsGenerated = result.PredictionsGenerated,
            skippedProps = result.SkippedProps,
            recommendationsSuppressedByThreshold = result.RecommendationsSuppressedByThreshold,
            recommendationsSuppressedByPropTypeFilter = result.RecommendationsSuppressedByPropTypeFilter,
            duration = result.Duration,
            diagnostics = result.TimingDiagnostics
        };
    }

    private static PredictionResponse ToPredictionResponse(Prediction prediction)
    {
        return new PredictionResponse(
            prediction.Id,
            prediction.PredictionRunId,
            prediction.PredictionRun?.ModelName,
            prediction.PredictionRun?.ModelVersion,
            prediction.PropId,
            prediction.Prop?.Player?.DisplayName,
            prediction.Prop?.League?.Code,
            prediction.Prop?.PropType?.Name,
            prediction.PredictedProbability,
            prediction.PredictedValue,
            prediction.SportsbookLine,
            prediction.Edge,
            prediction.Recommendation,
            prediction.Confidence,
            prediction.CreatedAt);
    }

    private sealed record PredictionRunResponse(
        Guid Id,
        string ModelName,
        string ModelVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        string? Notes,
        int PredictionCount);

    private sealed record PredictionResponse(
        Guid Id,
        Guid PredictionRunId,
        string? ModelName,
        string? ModelVersion,
        Guid PropId,
        string? PlayerName,
        string? LeagueCode,
        string? PropType,
        decimal? PredictedProbability,
        decimal? PredictedValue,
        decimal? SportsbookLine,
        decimal? Edge,
        string Recommendation,
        decimal Confidence,
        DateTimeOffset CreatedAt);

    private sealed record SideSelectionAuditProjection(
        SideSelectionAuditSummary Summary,
        object Detail);

    private sealed record SideSelectionAuditSummary(
        string SelectedSide,
        decimal? ModelProbabilityOver,
        decimal? ModelProbabilityUnder,
        decimal? EstimatedEv,
        IReadOnlyList<string> ReasonCodes);

    private sealed record PredictionStageSummary(
        Guid? LatestPredictionRunId,
        DateTimeOffset? LatestPredictionRunCreatedAtUtc,
        int PredictionRowsEvaluated,
        int PredictionRowsWithLine,
        int PredictionRowsMissingLine,
        int PredictionOverSelections,
        int PredictionUnderSelections,
        int PredictionPassSelections,
        decimal? PredictionOverSelectionRate,
        decimal? PredictionUnderSelectionRate,
        decimal? PredictionPassRate,
        int PredictionRowsWhereProjectionAboveLine,
        int PredictionRowsWhereProjectionBelowLine,
        int PredictionRowsWhereProjectionEqualsLine,
        int ProjectionAboveLineButSelectedUnderCount,
        int ProjectionBelowLineButSelectedOverCount,
        int OverProbabilityGreaterThanUnderCount,
        int UnderProbabilityGreaterThanOverCount,
        int OverEvGreaterThanUnderEvCount,
        int UnderEvGreaterThanOverEvCount,
        int OverEvPositiveCount,
        int UnderEvPositiveCount,
        int OverEvPositiveButPassedCount,
        int UnderEvPositiveButPassedCount,
        int OverEvPositiveButSelectedUnderCount,
        int SelectedUnderWithModelUnderBelowBreakEvenCount,
        int SelectedUnderWithModelOverAboveModelUnderCount,
        int SelectedUnderWithProjectionAboveLineCount,
        int PossibleProbabilityOrientationIssueCount,
        int OverEligibleButPassedCount,
        int PassReasonMissingCount);

    private sealed record PredictionPassGateSummary(
        int PassRows,
        int PassRowsWithPositiveOverEv,
        int PassRowsWhereOverEvBeatsUnderEv,
        int PassRowsWhereProjectionAboveLine,
        int PassRowsRejectedByGenerationProjectionEdgeThreshold,
        int PassRowsRejectedByPropTypeAllowlist,
        int PassRowsRejectedByUnknownGate,
        int OverPositivePassRowsRejectedByGenerationProjectionEdgeThreshold,
        int OverPositivePassRowsRejectedByPropTypeAllowlist,
        int OverPositivePassRowsRejectedByUnknownGate,
        int DiagnosticOverEvPositiveButProductionOverEdgeBelowThresholdCount,
        int DiagnosticOverEvBeatsUnderButProductionOverEdgeBelowThresholdCount,
        IReadOnlyDictionary<string, int> PassReasonsByCount,
        IReadOnlyDictionary<string, int> FailedGatesByCount);

    private sealed record PredictionHitProbabilityDiagnostics(
        decimal MinimumPickHitProbability,
        decimal StrongPickHitProbability,
        decimal ResearchOnlyBelowHitProbability,
        int SelectedRowsBelow50Count,
        int SelectedRowsBelowMinimumHitProbabilityCount,
        int OverRowsRejectedByHitProbability,
        int UnderRowsRejectedByHitProbability,
        int OverRowsRejectedByProjectionThreshold,
        int UnderRowsRejectedByProjectionThreshold,
        IReadOnlyDictionary<string, int> CandidateSideDistributionAfterFix);

    private sealed record PredictionStageAuditRow(
        Guid PredictionId,
        string PlayerName,
        string PropType,
        string? Provider,
        string? Bookmaker,
        DateOnly EventDate,
        decimal? Line,
        decimal? Projection,
        decimal? ModelOverProbability,
        decimal? ModelUnderProbability,
        decimal? ModelPushProbability,
        decimal? OverBreakEvenProbability,
        decimal? UnderBreakEvenProbability,
        decimal? OverEstimatedEv,
        decimal? UnderEstimatedEv,
        decimal? DiagnosticOverEv,
        decimal? ProductionOverEv,
        decimal? DiagnosticProductionEvDeltaOver,
        decimal? DiagnosticUnderEv,
        decimal? ProductionUnderEv,
        decimal? DiagnosticProductionEvDeltaUnder,
        string SelectedSide,
        decimal? SelectedSideProbability,
        decimal? SelectedSideEv,
        string SelectionReason,
        IReadOnlyList<string> PassReasonCodes,
        IReadOnlyList<string> FailedGates,
        PredictionThresholdsApplied ThresholdsApplied,
        PredictionSideCandidate OverCandidateBeforeGates,
        PredictionSideCandidate UnderCandidateBeforeGates,
        PredictionSideCandidate OverCandidateAfterGates,
        PredictionSideCandidate UnderCandidateAfterGates,
        IReadOnlyList<string> SuspiciousReasons,
        decimal ConfidenceScore,
        decimal RankingScore,
        int SampleSize,
        string ProbabilityMethod,
        decimal? Edge);

    private sealed record PredictionThresholdsApplied(
        string GenerationSelectionSource,
        string ProductionSelectionRule,
        decimal OverProjectionEdgeThreshold,
        decimal UnderProjectionEdgeThreshold,
        IReadOnlyList<string> EnabledPropTypes,
        decimal MinimumPickHitProbability,
        decimal StrongPickHitProbability,
        decimal ResearchOnlyBelowHitProbability,
        decimal? MinExpectedValueThreshold,
        string? MinTrustLevel,
        int? MinSampleSize);

    private sealed record PredictionSideCandidate(
        string Side,
        decimal? ModelProbability,
        decimal? BreakEvenProbability,
        decimal? ProbabilityEdge,
        decimal? EstimatedEv,
        decimal? DiagnosticEv,
        decimal? ProductionEv,
        decimal? DiagnosticProductionEvDelta,
        decimal? ModelProjection,
        decimal? Line,
        decimal? ProjectionDelta,
        int SampleSize,
        string TrustLevel,
        decimal ConfidenceScore,
        bool IncludedBeforeProductionGates,
        bool IncludedAfterProductionGates,
        IReadOnlyList<PredictionCandidateGate> Gates,
        IReadOnlyList<string> GateFailureReasons);

    private sealed record PredictionCandidateGate(
        string Name,
        bool Passed,
        object? Actual,
        object? Threshold,
        string Comparator,
        string Notes);

    private sealed record BacktestPredictionInput(
        Guid PredictionId,
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        string LeagueCode,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate,
        string Recommendation,
        decimal Confidence,
        decimal? Edge,
        decimal? PredictedValue,
        decimal SportsbookLine);

    private readonly record struct BacktestActualResultKey(
        Guid PlayerId,
        Guid PropTypeId,
        DateOnly EventDate);

    private readonly record struct PlayerPropTypeKey(
        Guid PlayerId,
        Guid PropTypeId);

    private sealed record BacktestActualResultCandidate(
        Guid PlayerId,
        Guid PropTypeId,
        decimal ActualResult);

    private sealed record BacktestPredictionResult(
        Guid PredictionId,
        string PropType,
        string Recommendation,
        decimal Confidence,
        decimal? Edge,
        decimal SportsbookLine,
        decimal? PredictedValue,
        decimal ActualResult,
        string Outcome);

    private sealed record BacktestSummary(
        int GradedPredictions,
        int Wins,
        int Losses,
        int Pushes,
        decimal HitRate);

    private sealed record BacktestGroupSummary(
        string Key,
        int GradedPredictions,
        int Wins,
        int Losses,
        int Pushes,
        decimal HitRate);

    private sealed record EdgeBucketAnalysis(
        string EdgeBucket,
        int PredictionCount,
        int Wins,
        int Losses,
        int Pushes,
        decimal HitRate,
        BacktestSummary Over,
        BacktestSummary Under);

    private sealed record ThresholdSimulationResult(
        decimal EdgeThreshold,
        int PredictionsPlaced,
        int Wins,
        int Losses,
        int Pushes,
        decimal HitRate,
        int OverCount,
        int UnderCount);

    private sealed record UngradedPredictionAnalysis(
        Guid PredictionId,
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate,
        string Reason);

    private readonly record struct HistoricalResultKey(Guid PlayerId, Guid PropTypeId);

    private sealed record NoHistoricalResultSkippedProp(
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        string League,
        string CanonicalLeague,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate,
        string Sportsbook,
        decimal SportsbookLine);

    private sealed record PlayerProviderRefSummary(
        string Provider,
        string ProviderPlayerId,
        string ProviderDisplayName);

    private sealed record LegacyProviderRefSummary(
        string Provider,
        string? ProviderPlayerId);

    private sealed record PlayerHistoryProviderRefSummary(
        string Source,
        string Provider,
        string ProviderPlayerId,
        string ProviderDisplayName);

    private sealed record PlayerIdentityRow(
        Guid PlayerId,
        string DisplayName,
        string LeagueCode,
        string CanonicalLeagueCode,
        IReadOnlyCollection<PlayerProviderRefSummary> ProviderRefs,
        IReadOnlyCollection<LegacyProviderRefSummary> LegacyProviderRefs,
        string NormalizedDisplayName);

    private sealed record DuplicateIdentityGroup(
        string NormalizedDisplayName,
        IReadOnlyList<PlayerIdentityRow> Players);

    private sealed record NoHistoricalResultPlayerReport(
        Guid PlayerId,
        string PlayerName,
        string League,
        string CanonicalLeague,
        string PrimarySportsbook,
        IReadOnlyCollection<string> Sportsbooks,
        int PropCountSkipped,
        int RecoverablePropCountAfterDuplicateMerge,
        bool HistoricalPropResultsExistUnderDifferentCanonicalPlayerId,
        bool BelongsToDuplicateIdentityGroup,
        bool AnotherPlayerInDuplicateGroupHasMlbMapping,
        IReadOnlyCollection<DuplicateIdentityPlayerSummary> DuplicateIdentityGroupPlayers,
        IReadOnlyCollection<DuplicatePlayerMappingSummary> OtherDuplicatePlayersWithMlbMappings,
        IReadOnlyCollection<DifferentCanonicalHistoricalMatch> DifferentCanonicalHistoricalMatches,
        IReadOnlyCollection<SportsbookSkippedPropCount> PropCountsBySportsbook,
        IReadOnlyCollection<PropTypeSkippedPropCount> SkippedPropTypes);

    private sealed record DuplicateIdentityPlayerSummary(
        Guid PlayerId,
        string PlayerName,
        string League,
        string CanonicalLeague,
        IReadOnlyCollection<PlayerProviderRefSummary> ProviderRefs);

    private sealed record DuplicatePlayerMappingSummary(
        Guid PlayerId,
        string PlayerName,
        string League,
        IReadOnlyCollection<string> MlbProviderPlayerIds);

    private sealed record DifferentCanonicalHistoricalMatch(
        Guid PropId,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate,
        Guid HistoricalPlayerId,
        string HistoricalPlayerName,
        string HistoricalPlayerLeague);

    private sealed record SportsbookSkippedPropCount(
        string Sportsbook,
        int PropCountSkipped);

    private sealed record PropTypeSkippedPropCount(
        string PropType,
        int PropCountSkipped);

    private sealed record NoHistoryAnalysisSkippedProp(
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate,
        string Reason,
        int EquivalentPlayerCount,
        int EquivalentPropTypeCount,
        int HistoricalResultsForEquivalentPropTypes,
        int HistoricalResultsForEquivalentPlayers,
        int MlbMappingsForEquivalentPlayers);

    private sealed record AliasRecoveryPlayerExample(
        Guid PlayerId,
        string PlayerName,
        int RecoverablePredictionCount,
        int RecoverableHistoricalResultCount,
        IReadOnlyCollection<AliasRecoveryPropExample> SampleProps,
        IReadOnlyCollection<AliasRecoveryDuplicatePlayerExample> DuplicatePlayersWithHistory);

    private sealed record AliasRecoveryPropExample(
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate,
        int RecoverableHistoricalResultCount,
        IReadOnlyCollection<AliasRecoveryDuplicatePlayerExample> MatchedDuplicatePlayers);

    private sealed record AliasRecoveryDuplicatePlayerExample(
        Guid PlayerId,
        string PlayerName,
        string League,
        int RecoverableHistoricalResultCount);

    private sealed record SkipAnalysisPropTypeCount(
        string PropType,
        int SkippedCount,
        decimal PercentOfSkippedProps);

    private sealed record SupportedPropTypeSummary(
        string PropType,
        int HistoricalResultCount);

    private sealed record UnsupportedPropTypeSummary(
        string PropType,
        int SkippedCount);

    private sealed record MlbHistoricalResultPropTypeCount(
        Guid PropTypeId,
        string PropType,
        int HistoricalResultCount);

    private sealed record CurrentMlbSportsbookPropTypeCount(
        Guid PropTypeId,
        string SportsbookPropType,
        int ActivePropCount);

    private sealed record PropTypeMappingAnalysisRow(
        string SportsbookPropType,
        int ActivePropCount,
        bool ExactHistoricalMatch,
        string? MatchedHistoricalPropType,
        int HistoricalResultCount,
        string? SuggestedCanonicalPropType,
        decimal MappingConfidence);

    private sealed record DuplicateHistoricalPropTypeSummary(
        string NormalizedName,
        int Count,
        IReadOnlyCollection<DuplicateHistoricalPropTypeRow> PropTypes);

    private sealed record DuplicateHistoricalPropTypeRow(
        Guid PropTypeId,
        string PropType,
        int HistoricalResultCount);

    private sealed record CurrentMlbPropForCoverage(
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropType,
        DateOnly EventDate);

    private sealed record PredictionTraceProp(
        Guid PropId,
        string PlayerName,
        string PropTypeName,
        DateOnly EventDate);

    private sealed record CoverageGapClassification(
        Guid PropId,
        Guid PlayerId,
        string PlayerName,
        string PropType,
        bool HistoricalMatchFound,
        bool RecoverableByPropTypeAlias,
        bool RecoverableByDuplicatePlayerMerge,
        bool RecoverableByBoth,
        string? SuggestedAlias,
        string RootCause);

    private sealed record CoverageGapPropTypeCount(
        string PropType,
        int Count);

    private sealed record CoverageGapAliasCount(
        string PropType,
        string SuggestedAlias,
        int Count);

    private sealed record CoverageGapPlayerCount(
        Guid PlayerId,
        string PlayerName,
        int Count);

    private sealed record PredictionTraceAliasMapping(
        string SportsbookPropType,
        string SuggestedCanonicalPropType,
        Guid PropTypeId,
        string HistoricalPropType,
        int HistoricalResultCount,
        decimal MappingConfidence);

    private sealed record PredictionTraceHistoricalMatch(
        Guid PlayerId,
        string PlayerName,
        Guid PropTypeId,
        string PropTypeName,
        Guid LeagueId,
        string LeagueCode,
        int ResultCount,
        DateOnly FirstResultDate,
        DateOnly LastResultDate);

    private sealed record PropTypeIdConflictResponse(
        string PropTypeName,
        IReadOnlyCollection<Guid> PropTypeIds,
        IReadOnlyCollection<PropTypeIdConflictDetail> PropTypeIdDetails,
        int SportsbookPropCount,
        int HistoricalResultCount,
        int ActivePredictionSkipCount,
        int EstimatedRecoverablePredictions);

    private sealed record PropTypeIdConflictDetail(
        Guid PropTypeId,
        string PropTypeName,
        int SportsbookPropCount,
        int HistoricalResultCount);

    private sealed record PlayerHistoricalResultSummary(
        int TotalCount,
        DateOnly? FirstResultDate,
        DateOnly? LastResultDate,
        IReadOnlyCollection<HistoricalResultPropTypeCount> CountsByPropType,
        IReadOnlyCollection<HistoricalResultLeagueCount> CountsByLeague,
        IReadOnlyCollection<HistoricalResultExternalSourceCount> CountsByExternalSource);

    private sealed record HistoricalResultPropTypeCount(
        Guid PropTypeId,
        string PropType,
        int Count,
        DateOnly FirstResultDate,
        DateOnly LastResultDate);

    private sealed record HistoricalResultLeagueCount(
        Guid LeagueId,
        string League,
        int Count);

    private sealed record HistoricalResultExternalSourceCount(
        string ExternalSource,
        int Count);

    private sealed record DuplicatePlayerHistorySummary(
        Guid PlayerId,
        string PlayerName,
        string LeagueCode,
        int HistoricalResultCount,
        DateOnly? FirstResultDate,
        DateOnly? LastResultDate);

    private sealed record PlayerPredictionGateDiagnostics(
        bool HasCurrentMlbProps,
        int CurrentMlbProps,
        int CurrentMlbPropsWithPriorHistory,
        int CurrentMlbPropsWithoutPriorHistory,
        IReadOnlyCollection<PropTypeSkippedPropCount> PropsWithoutPriorHistoryByPropType);
}
