using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Endpoints;

public static class ApplicationErrorDebugEndpoints
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;
    private const int ListStackTracePreviewLength = 1000;

    public static IEndpointRouteBuilder MapApplicationErrorDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug/application-errors", ListApplicationErrorsAsync)
            .WithName("ApplicationErrorsDebugList")
            .WithSummary("Returns recent persisted application errors.");

        endpoints.MapGet("/api/debug/application-errors/summary", SummarizeApplicationErrorsAsync)
            .WithName("ApplicationErrorsDebugSummary")
            .WithSummary("Summarizes persisted application errors.");

        endpoints.MapGet("/api/debug/application-errors/{id:guid}", GetApplicationErrorDetailAsync)
            .WithName("ApplicationErrorsDebugDetail")
            .WithSummary("Returns full details for one persisted application error.");

        endpoints.MapGet("/api/debug/errors/recent", ListApplicationErrorsAsync)
            .WithName("RecentApplicationErrorsDebug")
            .WithSummary("Compatibility alias for recent persisted application errors.");

        endpoints.MapGet("/api/debug/errors/summary", SummarizeApplicationErrorsAsync)
            .WithName("ApplicationErrorsSummaryDebug")
            .WithSummary("Compatibility alias for persisted application error summary.");

        endpoints.MapGet("/api/debug/errors/{id:guid}", GetApplicationErrorDetailAsync)
            .WithName("ApplicationErrorDetailDebug")
            .WithSummary("Compatibility alias for one persisted application error.");

        return endpoints;
    }

    private static async Task<IResult> ListApplicationErrorsAsync(
        int? take,
        string? severity,
        string? component,
        string? sport,
        string? provider,
        string? correlationId,
        Guid? runId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? source,
        bool? includeCanceled,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
        var includeCanceledRequests = includeCanceled == true;
        var query = ApplyFilters(
            dbContext.ApplicationErrors.AsNoTracking(),
            severity,
            component,
            sport,
            provider,
            correlationId,
            runId,
            fromUtc,
            toUtc,
            source);

        if (!includeCanceledRequests)
        {
            query = ExcludeCanceledRequests(query);
        }

        var errors = await query
            .OrderByDescending(error => error.OccurredAtUtc)
            .Take(pageSize)
            .Select(error => new
            {
                error.Id,
                error.OccurredAtUtc,
                error.CreatedAtUtc,
                error.Severity,
                error.Component,
                error.Source,
                error.Operation,
                error.RequestPath,
                error.RequestMethod,
                error.StatusCode,
                error.Message,
                error.ExceptionType,
                stackTracePreview = Cap(error.StackTrace, ListStackTracePreviewLength),
                error.CorrelationId,
                error.RunId,
                error.Sport,
                error.Provider
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            readOnly = true,
            callsExternalApis = false,
            generatedAtUtc = DateTimeOffset.UtcNow,
            count = errors.Count,
            take = pageSize,
            filters = new
            {
                severity,
                component,
                sport,
                provider,
                correlationId,
                runId,
                fromUtc,
                toUtc,
                source,
                includeCanceled = includeCanceledRequests
            },
            errors
        });
    }

    private static async Task<IResult> SummarizeApplicationErrorsAsync(
        int? hours,
        string? severity,
        string? component,
        string? sport,
        string? provider,
        string? correlationId,
        Guid? runId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? source,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var effectiveFromUtc = fromUtc ?? DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours ?? 24, 1, 24 * 30));
        var query = ApplyFilters(
            dbContext.ApplicationErrors.AsNoTracking(),
            severity,
            component,
            sport,
            provider,
            correlationId,
            runId,
            effectiveFromUtc,
            toUtc,
            source);

        var totalRecords = await query.CountAsync(cancellationToken);
        var errorRecords = await query.CountAsync(
            error => (error.Severity == "Error" || error.Severity == "Critical")
                && error.Operation != "ClientRequestCanceled"
                && error.ExceptionType != "System.OperationCanceledException"
                && error.ExceptionType != "System.Threading.Tasks.TaskCanceledException",
            cancellationToken);
        var canceledRequests = await query.CountAsync(
            error => error.Operation == "ClientRequestCanceled"
                || error.ExceptionType == "System.OperationCanceledException"
                || error.ExceptionType == "System.Threading.Tasks.TaskCanceledException",
            cancellationToken);
        var mostRecentOccurredAtUtc = await query.MaxAsync(error => (DateTimeOffset?)error.OccurredAtUtc, cancellationToken);

        var bySeverity = await query
            .GroupBy(error => error.Severity)
            .Select(group => new { severity = group.Key, count = group.Count() })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.severity)
            .ToListAsync(cancellationToken);
        var byComponent = await query
            .GroupBy(error => error.Component)
            .Select(group => new { component = group.Key, count = group.Count() })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.component)
            .ToListAsync(cancellationToken);
        var bySport = await query
            .GroupBy(error => error.Sport ?? "Unknown")
            .Select(group => new { sport = group.Key, count = group.Count() })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.sport)
            .ToListAsync(cancellationToken);
        var byProvider = await query
            .GroupBy(error => error.Provider ?? "Unknown")
            .Select(group => new { provider = group.Key, count = group.Count() })
            .OrderByDescending(row => row.count)
            .ThenBy(row => row.provider)
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            readOnly = true,
            callsExternalApis = false,
            generatedAtUtc = DateTimeOffset.UtcNow,
            fromUtc = effectiveFromUtc,
            toUtc,
            totalRecords,
            errorRecords,
            canceledRequests,
            mostRecentOccurredAtUtc,
            bySeverity,
            byComponent,
            bySport,
            byProvider
        });
    }

    private static async Task<IResult> GetApplicationErrorDetailAsync(
        Guid id,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var error = await dbContext.ApplicationErrors
            .AsNoTracking()
            .SingleOrDefaultAsync(error => error.Id == id, cancellationToken);

        if (error is null)
        {
            return Results.NotFound(new
            {
                readOnly = true,
                callsExternalApis = false,
                error = $"Application error '{id}' was not found."
            });
        }

        return Results.Ok(new
        {
            readOnly = true,
            callsExternalApis = false,
            error.Id,
            error.OccurredAtUtc,
            error.CreatedAtUtc,
            error.Severity,
            error.Component,
            error.Source,
            error.Operation,
            error.RequestPath,
            error.RequestMethod,
            error.UserAgent,
            error.RemoteIpHash,
            error.StatusCode,
            error.Message,
            error.ExceptionType,
            error.StackTrace,
            error.InnerException,
            error.CorrelationId,
            error.RunId,
            error.Sport,
            error.Provider,
            metadataJson = DeserializeMetadata(error.MetadataJson)
        });
    }

    private static IQueryable<ApplicationError> ApplyFilters(
        IQueryable<ApplicationError> query,
        string? severity,
        string? component,
        string? sport,
        string? provider,
        string? correlationId,
        Guid? runId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? source)
    {
        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(error => error.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(component))
        {
            query = query.Where(error => error.Component == component);
        }

        if (!string.IsNullOrWhiteSpace(sport))
        {
            query = query.Where(error => error.Sport == sport);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(error => error.Provider == provider);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query = query.Where(error => error.CorrelationId == correlationId);
        }

        if (runId.HasValue)
        {
            query = query.Where(error => error.RunId == runId.Value);
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(error => error.OccurredAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(error => error.OccurredAtUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(error => error.Source == source);
        }

        return query;
    }

    private static IQueryable<ApplicationError> ExcludeCanceledRequests(IQueryable<ApplicationError> query)
    {
        return query.Where(error => error.Operation != "ClientRequestCanceled"
            && error.StatusCode != StatusCodes.Status499ClientClosedRequest
            && error.ExceptionType != "System.OperationCanceledException"
            && error.ExceptionType != "System.Threading.Tasks.TaskCanceledException");
    }

    private static object? DeserializeMetadata(JsonDocument? document)
    {
        return document is null
            ? null
            : JsonSerializer.Deserialize<object>(document.RootElement.GetRawText());
    }

    private static string? Cap(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        const string suffix = "[truncated]";
        return value[..Math.Max(0, maxLength - suffix.Length)] + suffix;
    }
}
