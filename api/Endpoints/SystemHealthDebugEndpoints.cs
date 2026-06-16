using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Ingestion;
using SportsBetting.Api.Integrations.Results.Mlb;
using SportsBetting.Api.Integrations.TheOddsApi;

namespace SportsBetting.Api.Endpoints;

public static class SystemHealthDebugEndpoints
{
    private const int FreshnessWindowHours = 24;

    public static IEndpointRouteBuilder MapSystemHealthDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/system/health", async (
            ApplicationDbContext dbContext,
            IOptions<UnderdogIngestionOptions> underdogOptions,
            IOptions<MlbResultImportWorkerOptions> mlbImportOptions,
            IUnderdogIngestionWorkerState underdogState,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var applicationErrorSummary = await BuildApplicationErrorSummaryAsync(dbContext, generatedAtUtc, cancellationToken);
            var platformSummaries = await BuildPlatformSummariesAsync(
                dbContext,
                underdogOptions.Value,
                underdogState,
                generatedAtUtc,
                cancellationToken);
            var mlbImportHealth = await BuildMlbImportHealthAsync(
                dbContext,
                mlbImportOptions.Value,
                generatedAtUtc,
                cancellationToken);
            var predictionHealth = await BuildPredictionResearchHealthAsync(dbContext, cancellationToken);
            var overallStatus = DetermineOverallStatus(
                applicationErrorSummary.ErrorsLast24h,
                platformSummaries,
                mlbImportHealth.Status,
                predictionHealth.NormalRecommendationEligibleMarkets);

            return Results.Ok(new
            {
                overall = new
                {
                    status = overallStatus,
                    generatedAtUtc,
                    totalApplicationErrors = applicationErrorSummary.TotalErrors,
                    applicationErrorsLast24h = applicationErrorSummary.ErrorsLast24h,
                    canceledRequestsLast24h = applicationErrorSummary.CanceledRequestsLast24h,
                    mostRecentApplicationErrorAtUtc = applicationErrorSummary.MostRecentErrorAtUtc
                },
                sportsbookIngestionHealth = platformSummaries,
                mlbResultImportHealth = mlbImportHealth,
                predictionResearchHealth = predictionHealth
            });
        })
        .WithName("SystemHealthDebug")
        .WithSummary("Returns consolidated read-only operational health for errors, sportsbook ingestion, MLB import, and research outputs.");

        return endpoints;
    }

    private static async Task<ApplicationErrorSummary> BuildApplicationErrorSummaryAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var since = generatedAtUtc.AddHours(-24);
        var totalErrors = await dbContext.ApplicationErrors.CountAsync(cancellationToken);
        var errorsLast24h = await dbContext.ApplicationErrors.CountAsync(
            error => error.OccurredAtUtc >= since
                && (error.Severity == "Error" || error.Severity == "Critical")
                && error.Operation != "ClientRequestCanceled"
                && error.ExceptionType != "System.OperationCanceledException"
                && error.ExceptionType != "System.Threading.Tasks.TaskCanceledException",
            cancellationToken);
        var canceledRequestsLast24h = await dbContext.ApplicationErrors.CountAsync(
            error => error.OccurredAtUtc >= since
                && (error.Operation == "ClientRequestCanceled"
                    || error.ExceptionType == "System.OperationCanceledException"
                    || error.ExceptionType == "System.Threading.Tasks.TaskCanceledException"),
            cancellationToken);
        var mostRecentErrorAtUtc = await dbContext.ApplicationErrors
            .MaxAsync(error => (DateTimeOffset?)error.OccurredAtUtc, cancellationToken);

        return new ApplicationErrorSummary(totalErrors, errorsLast24h, canceledRequestsLast24h, mostRecentErrorAtUtc);
    }

    private static async Task<SportsbookHealthRow[]> BuildPlatformSummariesAsync(
        ApplicationDbContext dbContext,
        UnderdogIngestionOptions underdogOptions,
        IUnderdogIngestionWorkerState underdogState,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var platformRows = (await dbContext.PlatformProps
            .AsNoTracking()
            .Include(platformProp => platformProp.Platform)
            .Where(platformProp => platformProp.Prop != null
                && platformProp.Prop.League != null
                && platformProp.Prop.League.Code == "MLB")
            .Select(platformProp => new
            {
                Platform = platformProp.Platform == null ? "Unknown" : platformProp.Platform.Name,
                platformProp.ObservedAt,
                platformProp.Status,
                platformProp.OverAvailable,
                platformProp.UnderAvailable
            })
            .ToListAsync(cancellationToken))
            .Select(row => new PlatformObservationRow(
                row.Platform,
                row.ObservedAt,
                row.Status,
                row.OverAvailable,
                row.UnderAvailable))
            .ToArray();
        var rankedMarkets = await dbContext.Predictions
            .AsNoTracking()
            .Where(prediction => prediction.PredictionRun != null
                && prediction.PredictionRun.ModelName == "BaselineAverageModel"
                && prediction.Prop != null
                && prediction.Prop.League != null
                && prediction.Prop.League.Code == "MLB")
            .OrderByDescending(prediction => prediction.PredictionRun!.CreatedAt)
            .Select(prediction => new
            {
                prediction.PropId,
                prediction.PredictionRunId
            })
            .ToListAsync(cancellationToken);
        var latestRunId = rankedMarkets.FirstOrDefault()?.PredictionRunId;
        var latestPropIds = rankedMarkets
            .Where(row => latestRunId.HasValue && row.PredictionRunId == latestRunId.Value)
            .Select(row => row.PropId)
            .Distinct()
            .ToHashSet();
        var rankedPlatformRows = (await dbContext.PlatformProps
            .AsNoTracking()
            .Include(platformProp => platformProp.Platform)
            .Where(platformProp => latestPropIds.Contains(platformProp.PropId))
            .Select(platformProp => new
            {
                Platform = platformProp.Platform == null ? "Unknown" : platformProp.Platform.Name,
                platformProp.ObservedAt,
                platformProp.Status,
                platformProp.OverAvailable,
                platformProp.UnderAvailable
            })
            .ToListAsync(cancellationToken))
            .Select(row => new PlatformObservationRow(
                row.Platform,
                row.ObservedAt,
                row.Status,
                row.OverAvailable,
                row.UnderAvailable))
            .ToArray();
        var latestWorkerErrors = (await dbContext.ApplicationErrors
            .AsNoTracking()
            .Where(error => error.Source == "worker")
            .Where(error => error.Severity == "Error" || error.Severity == "Critical")
            .OrderByDescending(error => error.OccurredAtUtc)
            .Select(error => new
            {
                error.Component,
                error.OccurredAtUtc,
                error.Message
            })
            .ToListAsync(cancellationToken))
            .Select(row => new WorkerErrorRow(
                row.Component,
                row.OccurredAtUtc,
                row.Message))
            .ToArray();

        return
        [
            BuildSportsbookHealth(
                "Underdog Fantasy",
                underdogOptions.Enabled,
                underdogState.LastStatus,
                underdogState.LastErrorMessage,
                rankedPlatformRows,
                platformRows,
                latestWorkerErrors.FirstOrDefault(error => error.Component == nameof(UnderdogIngestionWorker)),
                generatedAtUtc),
            BuildSportsbookHealth(
                "The Odds API DFS",
                true,
                "manual",
                null,
                rankedPlatformRows,
                platformRows,
                latestWorkerErrors.FirstOrDefault(error => error.Component == nameof(TheOddsMlbIngestionWorker)),
                generatedAtUtc)
        ];
    }

    private static SportsbookHealthRow BuildSportsbookHealth(
        string platform,
        bool isEnabled,
        string workerStatus,
        string? workerErrorMessage,
        IReadOnlyCollection<PlatformObservationRow> rankedPlatformRows,
        IReadOnlyCollection<PlatformObservationRow> allPlatformRows,
        WorkerErrorRow? lastWorkerError,
        DateTimeOffset generatedAtUtc)
    {
        var rankedRows = rankedPlatformRows
            .Where(row => string.Equals(row.Platform, platform, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var allRows = allPlatformRows
            .Where(row => string.Equals(row.Platform, platform, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var latestObservedAt = allRows.Length == 0
            ? null
            : (DateTimeOffset?)allRows.Max(row => row.ObservedAt);
        var minutesSinceLatestObserved = latestObservedAt.HasValue
            ? Math.Round((generatedAtUtc - latestObservedAt.Value.ToUniversalTime()).TotalMinutes, 2)
            : (double?)null;
        var isFresh = latestObservedAt.HasValue
            && generatedAtUtc - latestObservedAt.Value.ToUniversalTime() <= TimeSpan.FromHours(FreshnessWindowHours);
        var hiddenStaleMarkets = rankedRows.Count(row => generatedAtUtc - row.ObservedAt.ToUniversalTime() > TimeSpan.FromHours(FreshnessWindowHours));
        var visibleMarkets = rankedRows.Length - hiddenStaleMarkets;
        var status = DetermineSportsbookStatus(isEnabled, isFresh, workerStatus, workerErrorMessage, lastWorkerError);

        return new SportsbookHealthRow(
            platform,
            isEnabled,
            isFresh,
            status,
            latestObservedAt,
            minutesSinceLatestObserved,
            rankedRows.Length,
            visibleMarkets,
            hiddenStaleMarkets,
            allRows.Count(row => IsActiveStatus(row.Status)),
            allRows.Count(row => row.OverAvailable),
            allRows.Count(row => row.UnderAvailable),
            lastWorkerError?.OccurredAtUtc,
            workerErrorMessage ?? lastWorkerError?.Message,
            BuildSportsbookRecommendedAction(platform, isEnabled, isFresh, status));
    }

    private static async Task<MlbImportHealthRow> BuildMlbImportHealthAsync(
        ApplicationDbContext dbContext,
        MlbResultImportWorkerOptions options,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.MlbResultImportStates
            .AsNoTracking()
            .SingleOrDefaultAsync(importState => importState.Source == "mlb", cancellationToken);
        var pendingDateCount = await dbContext.MlbResultImportDateStates.CountAsync(
            dateState => dateState.Status == "pending" || dateState.Status == "running",
            cancellationToken);
        var failedDateCount = await dbContext.MlbResultImportDateStates.CountAsync(
            dateState => dateState.Status == "failed",
            cancellationToken);
        var lastFailureAtUtc = await dbContext.ApplicationErrors
            .AsNoTracking()
            .Where(error => error.Component == nameof(MlbResultImportWorker))
            .Where(error => error.Severity == "Error" || error.Severity == "Critical")
            .MaxAsync(error => (DateTimeOffset?)error.OccurredAtUtc, cancellationToken);
        var status = !options.Enabled
            ? "Disabled"
            : failedDateCount > 0 || state?.LastStatus == "failed"
                ? "Error"
                : pendingDateCount > 0 || state?.LastStatus == "partial_failure"
                    ? "Warning"
                    : "Healthy";

        return new MlbImportHealthRow(
            status,
            state?.LastSuccessfulImportDate,
            pendingDateCount,
            failedDateCount,
            state?.LastCompletedAt,
            state?.LastSuccessfulRunAt,
            lastFailureAtUtc,
            status switch
            {
                "Disabled" => "Enable MlbResultImportWorker when historical result freshness is required.",
                "Error" => "Review failed MLB import dates and application errors for MlbResultImportWorker.",
                "Warning" => "Allow the worker to continue backfilling pending dates or run the import range endpoint manually.",
                _ => "No action required."
            });
    }

    private static async Task<PredictionResearchHealthRow> BuildPredictionResearchHealthAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var latestRun = await dbContext.PredictionRuns
            .AsNoTracking()
            .Where(run => run.ModelName == "BaselineAverageModel")
            .OrderByDescending(run => run.CreatedAt)
            .Select(run => new
            {
                run.Id,
                run.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestRun is null)
        {
            return new PredictionResearchHealthRow(
                null,
                0,
                0,
                0,
                "Unavailable",
                "Generate an MLB prediction run.");
        }

        var predictionRows = await dbContext.Predictions
            .AsNoTracking()
            .Where(prediction => prediction.PredictionRunId == latestRun.Id)
            .Select(prediction => new
            {
                prediction.PropId,
                prediction.Recommendation
            })
            .ToListAsync(cancellationToken);
        var propIds = predictionRows.Select(row => row.PropId).Distinct().ToHashSet();
        var platformRows = await dbContext.PlatformProps
            .AsNoTracking()
            .Where(platformProp => propIds.Contains(platformProp.PropId))
            .Select(platformProp => new
            {
                platformProp.PropId,
                platformProp.ObservedAt,
                platformProp.PayoutType
            })
            .ToListAsync(cancellationToken);
        var latestByPropId = platformRows
            .GroupBy(row => row.PropId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.ObservedAt).First());
        var visibleMarkets = predictionRows.Count(row =>
            latestByPropId.TryGetValue(row.PropId, out var platformProp)
            && DateTimeOffset.UtcNow - platformProp.ObservedAt.ToUniversalTime() <= TimeSpan.FromHours(FreshnessWindowHours));
        var normalEligibleMarkets = predictionRows.Count(row =>
            latestByPropId.TryGetValue(row.PropId, out var platformProp)
            && DateTimeOffset.UtcNow - platformProp.ObservedAt.ToUniversalTime() <= TimeSpan.FromHours(FreshnessWindowHours)
            && string.Equals(platformProp.PayoutType, "standard", StringComparison.OrdinalIgnoreCase));

        return new PredictionResearchHealthRow(
            latestRun.Id,
            predictionRows.Count,
            visibleMarkets,
            normalEligibleMarkets,
            "ResearchOnly",
            normalEligibleMarkets == 0
                ? "Continue using the dashboard for research only. No normal recommendation-eligible markets are currently available."
                : "Review eligible standard markets before surfacing recommendations.");
    }

    private static string DetermineOverallStatus(
        int applicationErrorsLast24h,
        IReadOnlyCollection<SportsbookHealthRow> platformSummaries,
        string mlbStatus,
        int eligibleMarkets)
    {
        if (applicationErrorsLast24h > 0
            || mlbStatus == "Error"
            || platformSummaries.Any(platform => platform.Status == "Blocked"))
        {
            return "Error";
        }

        if (eligibleMarkets == 0 || mlbStatus == "Warning")
        {
            return "Warning";
        }

        return "Healthy";
    }

    private static string DetermineSportsbookStatus(
        bool isEnabled,
        bool isFresh,
        string workerStatus,
        string? workerErrorMessage,
        WorkerErrorRow? lastWorkerError)
    {
        if (!isEnabled)
        {
            return "Disabled";
        }

        if (!string.IsNullOrWhiteSpace(workerErrorMessage)
            || workerStatus == "failed"
            || lastWorkerError is not null)
        {
            return "Blocked";
        }

        return isFresh ? "Healthy" : "Stale";
    }

    private static string BuildSportsbookRecommendedAction(string platform, bool isEnabled, bool isFresh, string status)
    {
        return status switch
        {
            "Disabled" => $"Enable {platform} ingestion if this source should refresh automatically.",
            "Blocked" => $"Review recent application errors and worker status for {platform}.",
            "Stale" => $"Check {platform} ingestion worker and source availability; latest markets are outside the freshness window.",
            _ => "No action required."
        };
    }

    private static bool IsActiveStatus(string status)
    {
        return status.Contains("active", StringComparison.OrdinalIgnoreCase)
            || status.Contains("available", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ApplicationErrorSummary(
        int TotalErrors,
        int ErrorsLast24h,
        int CanceledRequestsLast24h,
        DateTimeOffset? MostRecentErrorAtUtc);

    private sealed record PlatformObservationRow(
        string Platform,
        DateTimeOffset ObservedAt,
        string Status,
        bool OverAvailable,
        bool UnderAvailable);

    private sealed record WorkerErrorRow(
        string Component,
        DateTimeOffset OccurredAtUtc,
        string Message);

    private sealed record SportsbookHealthRow(
        string Platform,
        bool IsEnabled,
        bool IsFresh,
        string Status,
        DateTimeOffset? LatestObservedAt,
        double? MinutesSinceLatestObserved,
        int TotalRankedMarkets,
        int VisibleMarkets,
        int HiddenStaleMarkets,
        int ActiveRows,
        int OverAvailableCount,
        int UnderAvailableCount,
        DateTimeOffset? LastErrorAtUtc,
        string? LastErrorMessage,
        string RecommendedAction);

    private sealed record MlbImportHealthRow(
        string Status,
        DateOnly? LatestImportedDate,
        int PendingDateCount,
        int FailedDateCount,
        DateTimeOffset? LastRunAtUtc,
        DateTimeOffset? LastSuccessAtUtc,
        DateTimeOffset? LastFailureAtUtc,
        string RecommendedAction);

    private sealed record PredictionResearchHealthRow(
        Guid? LatestPredictionRunId,
        int TotalRankedMarkets,
        int VisibleMarkets,
        int NormalRecommendationEligibleMarkets,
        string EvResearchStatus,
        string RecommendedAction);
}
