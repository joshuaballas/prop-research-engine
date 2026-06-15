using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed class MlbResultImportService(
    ApplicationDbContext dbContext,
    IMlbResultsProvider mlbResultsProvider,
    ILogger<MlbResultImportService> logger) : IMlbResultImportService
{
    private const string ExternalSource = "mlb";

    public async Task<MlbResultImportResult> ImportAsync(DateOnly eventDate, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var providerRecords = await mlbResultsProvider.GetMlbResultsAsync(eventDate, cancellationToken);
        var mappedRecords = providerRecords
            .Where(record => record.IsMapped)
            .Select(record => record.ResultRecord)
            .ToArray();

        var collapsedRecords = mappedRecords
            .GroupBy(record => new ResultKey(record.PlayerId, record.LeagueId, record.PropTypeId, record.EventDate))
            .Select(group => new CollapsedResultRecord(
                group.Key,
                group.Sum(record => record.ActualResultValue),
                BuildExternalSourceIdentifier(group.Key)))
            .ToArray();

        var playerIds = collapsedRecords.Select(record => record.Key.PlayerId).Distinct().ToArray();
        var propTypeIds = collapsedRecords.Select(record => record.Key.PropTypeId).Distinct().ToArray();

        var existingResults = await dbContext.PropResults
            .Where(result => result.EventDate == eventDate
                && playerIds.Contains(result.PlayerId)
                && propTypeIds.Contains(result.PropTypeId))
            .ToListAsync(cancellationToken);

        var existingByKey = existingResults
            .GroupBy(result => new ResultKey(result.PlayerId, result.LeagueId, result.PropTypeId, result.EventDate))
            .ToDictionary(group => group.Key, group => group.First());

        var observedAt = DateTimeOffset.UtcNow;
        var recordsInserted = 0;
        var recordsUpdated = 0;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var record in collapsedRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (existingByKey.TryGetValue(record.Key, out var existingResult))
            {
                var updated = false;

                if (existingResult.ActualResultValue != record.ActualResultValue)
                {
                    existingResult.ActualResultValue = record.ActualResultValue;
                    updated = true;
                }

                if (existingResult.ExternalSource != ExternalSource)
                {
                    existingResult.ExternalSource = ExternalSource;
                    updated = true;
                }

                if (existingResult.ExternalSourceIdentifier != record.ExternalSourceIdentifier)
                {
                    existingResult.ExternalSourceIdentifier = record.ExternalSourceIdentifier;
                    updated = true;
                }

                if (updated)
                {
                    existingResult.UpdatedAt = observedAt;
                    recordsUpdated++;
                }

                continue;
            }

            var result = new PropResult
            {
                Id = Guid.NewGuid(),
                PlayerId = record.Key.PlayerId,
                LeagueId = record.Key.LeagueId,
                PropTypeId = record.Key.PropTypeId,
                EventDate = record.Key.EventDate,
                ActualResultValue = record.ActualResultValue,
                ExternalSource = ExternalSource,
                ExternalSourceIdentifier = record.ExternalSourceIdentifier,
                CreatedAt = observedAt,
                UpdatedAt = observedAt
            };

            dbContext.PropResults.Add(result);
            existingByKey[record.Key] = result;
            recordsInserted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        stopwatch.Stop();

        logger.LogInformation(
            "Imported MLB results for {Date} in {DurationMs}ms. Records found: {RecordsFound}; mapped: {RecordsMapped}; inserted: {Inserted}; updated: {Updated}.",
            eventDate,
            stopwatch.ElapsedMilliseconds,
            providerRecords.Count,
            mappedRecords.Length,
            recordsInserted,
            recordsUpdated);

        return new MlbResultImportResult(
            eventDate,
            providerRecords.Count,
            mappedRecords.Length,
            recordsInserted,
            recordsUpdated,
            stopwatch.Elapsed);
    }

    public async Task<MlbResultImportStatus> GetStatusAsync(DateOnly eventDate, CancellationToken cancellationToken)
    {
        var results = await dbContext.PropResults
            .AsNoTracking()
            .Where(result => result.ExternalSource == ExternalSource && result.EventDate == eventDate)
            .Select(result => new
            {
                result.PlayerId,
                result.PropTypeId
            })
            .ToListAsync(cancellationToken);

        return new MlbResultImportStatus(
            eventDate,
            results.Count,
            results.Select(result => result.PlayerId).Distinct().Count(),
            results.Select(result => result.PropTypeId).Distinct().Count());
    }

    private static string BuildExternalSourceIdentifier(ResultKey key)
    {
        return $"mlb:{key.EventDate:yyyyMMdd}:{key.PlayerId:N}:{key.PropTypeId:N}";
    }

    private sealed record ResultKey(
        Guid PlayerId,
        Guid LeagueId,
        Guid PropTypeId,
        DateOnly EventDate);

    private sealed record CollapsedResultRecord(
        ResultKey Key,
        decimal ActualResultValue,
        string ExternalSourceIdentifier);
}
