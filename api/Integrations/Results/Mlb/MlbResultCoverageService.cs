using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed class MlbResultCoverageService(
    ApplicationDbContext dbContext,
    IOptions<MlbResultImportWorkerOptions> options) : IMlbResultCoverageService
{
    private const string Source = "mlb";

    public async Task<MlbResultCoverageAuditResponse> AuditAsync(
        DateOnly fromDate,
        DateOnly toDate,
        bool includeDetails,
        bool persist,
        CancellationToken cancellationToken)
    {
        if (toDate < fromDate)
        {
            throw new ArgumentException("toDate must be greater than or equal to fromDate.", nameof(toDate));
        }

        var details = new List<MlbResultCoverageDateAudit>();
        var current = fromDate;
        while (current <= toDate)
        {
            details.Add(await AuditDateAsync(current, persist, cancellationToken));
            current = current.AddDays(1);
        }

        var recommendedRepairActions = details
            .Where(row => row.NeedsRepair)
            .Select(row => new
            {
                date = row.Date,
                row.CoverageStatus,
                row.CoverageReason,
                action = row.RecommendedRepairAction
            })
            .Cast<object>()
            .ToArray();

        return new MlbResultCoverageAuditResponse(
            DateTimeOffset.UtcNow,
            CallsExternalApis: false,
            ReadOnly: !persist,
            fromDate,
            toDate,
            details.Count,
            details.Count(row => row.CoverageStatus == "Complete"),
            details.Count(row => row.CoverageStatus == "Partial"),
            details.Count(row => row.CoverageStatus == "NotStarted"),
            details.Count(row => row.CoverageStatus == "PendingGames"),
            details.Count(row => row.CoverageStatus == "NoGames"),
            details.Count(row => row.CoverageStatus == "Failed"),
            includeDetails ? details : [],
            recommendedRepairActions);
    }

    public async Task<MlbResultCoverageDateAudit> AuditDateAsync(
        DateOnly eventDate,
        bool persist,
        CancellationToken cancellationToken)
    {
        var dateState = await dbContext.MlbResultImportDateStates
            .SingleOrDefaultAsync(row => row.EventDate == eventDate, cancellationToken);
        var coverage = await dbContext.PropResults
            .AsNoTracking()
            .Where(row => row.ExternalSource == Source && row.EventDate == eventDate)
            .GroupBy(row => row.EventDate)
            .Select(group => new
            {
                ImportedResults = group.Count(),
                DistinctPlayers = group.Select(row => row.PlayerId).Distinct().Count(),
                DistinctPropTypes = group.Select(row => row.PropTypeId).Distinct().Count()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var importedResults = coverage?.ImportedResults ?? dateState?.ImportedResults ?? 0;
        var distinctPlayers = coverage?.DistinctPlayers ?? dateState?.DistinctPlayers ?? 0;
        var distinctPropTypes = coverage?.DistinctPropTypes ?? dateState?.DistinctPropTypes ?? 0;
        var workerOptions = options.Value;
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

        var status = ResolveCoverageStatus(
            eventDate,
            importedResults,
            distinctPlayers,
            distinctPropTypes,
            dateState?.Status,
            dateState?.LastErrorMessage,
            todayUtc,
            workerOptions);
        var reason = BuildCoverageReason(
            status,
            importedResults,
            distinctPlayers,
            distinctPropTypes,
            workerOptions);

        if (persist)
        {
            var observedAt = DateTimeOffset.UtcNow;
            if (dateState is null)
            {
                dateState = new MlbResultImportDateState
                {
                    Id = Guid.NewGuid(),
                    EventDate = eventDate,
                    Status = status == "Complete" ? "succeeded" : status == "Failed" ? "failed" : status == "NoGames" ? "skipped" : "pending",
                    CreatedAt = observedAt
                };
                dbContext.MlbResultImportDateStates.Add(dateState);
            }

            dateState.CompletedGamesExpected = null;
            dateState.CompletedGamesImported = null;
            dateState.ImportedResults = importedResults;
            dateState.DistinctPlayers = distinctPlayers;
            dateState.DistinctPropTypes = distinctPropTypes;
            dateState.CoverageStatus = status;
            dateState.CoverageReason = reason;
            dateState.LatestImportAttemptUtc = dateState.LastAttemptedAt;
            dateState.UpdatedAt = observedAt;

            if (status != "Complete" && dateState.Status == "succeeded")
            {
                dateState.Status = status == "Partial" ? "partial" : status == "NoGames" ? "skipped" : "pending";
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new MlbResultCoverageDateAudit(
            eventDate,
            CompletedGamesExpected: null,
            CompletedGamesImported: null,
            importedResults,
            distinctPlayers,
            distinctPropTypes,
            status,
            reason,
            dateState?.LastAttemptedAt,
            dateState?.LatestSuccessfulRepairUtc,
            dateState?.LastErrorMessage,
            NeedsRepair: status is "Partial" or "NotStarted",
            RecommendedRepairAction: status switch
            {
                "Partial" => "RepairPartialImport",
                "NotStarted" => "ImportMissingDate",
                "Failed" => "ReviewLatestErrorThenRetry",
                "PendingGames" => "WaitForGamesToComplete",
                "NoGames" => "NoRepairNeeded",
                _ => "None"
            });
    }

    private static string ResolveCoverageStatus(
        DateOnly eventDate,
        int importedResults,
        int distinctPlayers,
        int distinctPropTypes,
        string? importStatus,
        string? lastErrorMessage,
        DateOnly todayUtc,
        MlbResultImportWorkerOptions options)
    {
        if (string.Equals(importStatus, "failed", StringComparison.OrdinalIgnoreCase) && importedResults == 0 && !string.IsNullOrWhiteSpace(lastErrorMessage))
        {
            return "Failed";
        }

        if (eventDate >= todayUtc && importedResults == 0)
        {
            return "PendingGames";
        }

        if (string.Equals(importStatus, "skipped", StringComparison.OrdinalIgnoreCase) && importedResults == 0)
        {
            return "NoGames";
        }

        if (importedResults == 0)
        {
            return "NotStarted";
        }

        return importedResults >= options.MinimumCompleteImportedResults
            && distinctPlayers >= options.MinimumCompleteDistinctPlayers
            && distinctPropTypes >= options.MinimumCompleteDistinctPropTypes
                ? "Complete"
                : "Partial";
    }

    private static string BuildCoverageReason(
        string status,
        int importedResults,
        int distinctPlayers,
        int distinctPropTypes,
        MlbResultImportWorkerOptions options)
    {
        return status switch
        {
            "Complete" => $"Fallback coverage thresholds met: importedResults={importedResults}, distinctPlayers={distinctPlayers}, distinctPropTypes={distinctPropTypes}.",
            "Partial" => $"Fallback coverage thresholds not met: importedResults={importedResults}/{options.MinimumCompleteImportedResults}, distinctPlayers={distinctPlayers}/{options.MinimumCompleteDistinctPlayers}, distinctPropTypes={distinctPropTypes}/{options.MinimumCompleteDistinctPropTypes}.",
            "NotStarted" => "No persisted MLB prop_results exist for this completed date.",
            "PendingGames" => "Date is not a completed historical MLB result date yet.",
            "NoGames" => "MLB provider returned no result records for this completed date.",
            "Failed" => "Latest import attempt failed and no persisted MLB prop_results exist for this date.",
            _ => "Coverage status is unknown."
        };
    }
}
