using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SportsBetting.Api.Data;

namespace SportsBetting.Api.Endpoints;

public static class ApplicationErrorDebugEndpoints
{
    public static IEndpointRouteBuilder MapApplicationErrorDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/errors/recent", async (
            int? take,
            string? source,
            string? component,
            string? severity,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var pageSize = Math.Clamp(take ?? 50, 1, 250);
            var query = dbContext.ApplicationErrors.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(source))
            {
                query = query.Where(error => error.Source == source);
            }

            if (!string.IsNullOrWhiteSpace(component))
            {
                query = query.Where(error => error.Component == component);
            }

            if (!string.IsNullOrWhiteSpace(severity))
            {
                query = query.Where(error => error.Severity == severity);
            }

            var errors = await query
                .OrderByDescending(error => error.OccurredAtUtc)
                .Take(pageSize)
                .Select(error => new
                {
                    error.Id,
                    error.OccurredAtUtc,
                    error.Severity,
                    error.Source,
                    error.Component,
                    error.Operation,
                    error.RequestPath,
                    error.HttpMethod,
                    error.StatusCode,
                    error.Message,
                    error.ExceptionType,
                    error.CorrelationId
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                count = errors.Count,
                errors
            });
        })
        .WithName("RecentApplicationErrorsDebug")
        .WithSummary("Returns recent persisted application errors.");

        endpoints.MapGet("/api/debug/errors/summary", async (
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var since = DateTimeOffset.UtcNow.AddHours(-24);
            var totalErrors = await dbContext.ApplicationErrors.CountAsync(cancellationToken);
            var recordsLast24h = await dbContext.ApplicationErrors.CountAsync(
                error => error.OccurredAtUtc >= since,
                cancellationToken);
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
            var mostRecentErrorTimestamp = await dbContext.ApplicationErrors
                .MaxAsync(error => (DateTimeOffset?)error.OccurredAtUtc, cancellationToken);
            var errorsBySeverity = await dbContext.ApplicationErrors
                .AsNoTracking()
                .GroupBy(error => error.Severity)
                .Select(group => new
                {
                    severity = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.severity)
                .ToListAsync(cancellationToken);
            var errorsBySource = await dbContext.ApplicationErrors
                .AsNoTracking()
                .GroupBy(error => error.Source)
                .Select(group => new
                {
                    source = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.source)
                .ToListAsync(cancellationToken);
            var errorsByComponent = await dbContext.ApplicationErrors
                .AsNoTracking()
                .GroupBy(error => error.Component)
                .Select(group => new
                {
                    component = group.Key,
                    count = group.Count()
                })
                .OrderByDescending(row => row.count)
                .ThenBy(row => row.component)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                totalErrors,
                recordsLast24h,
                errorsLast24h,
                canceledRequestsLast24h,
                errorsBySeverity,
                errorsBySource,
                errorsByComponent,
                mostRecentErrorTimestamp
            });
        })
        .WithName("ApplicationErrorsSummaryDebug")
        .WithSummary("Summarizes persisted application errors.");

        endpoints.MapGet("/api/debug/errors/{id:guid}", async (
            Guid id,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var error = await dbContext.ApplicationErrors
                .AsNoTracking()
                .SingleOrDefaultAsync(error => error.Id == id, cancellationToken);

            if (error is null)
            {
                return Results.NotFound(new { error = $"Application error '{id}' was not found." });
            }

            return Results.Ok(new
            {
                error.Id,
                error.OccurredAtUtc,
                error.Severity,
                error.Source,
                error.Component,
                error.Operation,
                error.RequestPath,
                error.HttpMethod,
                error.StatusCode,
                error.Message,
                error.ExceptionType,
                error.StackTrace,
                error.InnerException,
                error.CorrelationId,
                MetadataJson = error.MetadataJson is null
                    ? null
                    : JsonSerializer.Deserialize<object>(error.MetadataJson.RootElement.GetRawText())
            });
        })
        .WithName("ApplicationErrorDetailDebug")
        .WithSummary("Returns full details for one persisted application error.");

        return endpoints;
    }
}
