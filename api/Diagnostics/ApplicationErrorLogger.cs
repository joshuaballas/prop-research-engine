using System.Text.Json;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Diagnostics;

public sealed class ApplicationErrorLogger(
    ApplicationDbContext dbContext,
    ILogger<ApplicationErrorLogger> logger) : IApplicationErrorLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApplicationError?> LogAsync(
        ApplicationErrorLogRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exception = request.Exception;
            var error = new ApplicationError
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = DateTimeOffset.UtcNow,
                Severity = NullIfWhiteSpace(request.Severity) ?? "Error",
                Source = NullIfWhiteSpace(request.Source) ?? "unknown",
                Component = NullIfWhiteSpace(request.Component) ?? "unknown",
                Operation = NullIfWhiteSpace(request.Operation) ?? "unknown",
                RequestPath = request.RequestPath,
                HttpMethod = request.HttpMethod,
                StatusCode = request.StatusCode,
                Message = exception.Message,
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                StackTrace = exception.ToString(),
                InnerException = BuildInnerExceptionText(exception.InnerException),
                CorrelationId = NullIfWhiteSpace(request.CorrelationId) ?? Guid.NewGuid().ToString("N"),
                MetadataJson = request.Metadata is null
                    ? null
                    : JsonSerializer.SerializeToDocument(request.Metadata, JsonOptions)
            };

            dbContext.ApplicationErrors.Add(error);
            await dbContext.SaveChangesAsync(cancellationToken);

            return error;
        }
        catch (Exception loggingException)
        {
            logger.LogError(
                loggingException,
                "Failed to persist application error. OriginalExceptionType: {OriginalExceptionType}; OriginalMessage: {OriginalMessage}",
                request.Exception.GetType().FullName,
                request.Exception.Message);

            return null;
        }
    }

    private static string? BuildInnerExceptionText(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        var innerExceptions = new List<string>();
        var current = exception;

        while (current is not null)
        {
            innerExceptions.Add($"{current.GetType().FullName}: {current.Message}{Environment.NewLine}{current.StackTrace}");
            current = current.InnerException;
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", innerExceptions);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
