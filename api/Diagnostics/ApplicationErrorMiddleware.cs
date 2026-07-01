using SportsBetting.Api.Integrations.TheOddsApi;

namespace SportsBetting.Api.Diagnostics;

public sealed class ApplicationErrorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IApplicationErrorLogger errorLogger)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            var correlationId = GetOrCreateCorrelationId(context);

            if (IsClientRequestCancellation(exception, context))
            {
                await errorLogger.LogAsync(new ApplicationErrorLogRequest(
                    "Information",
                    context.Request.Path.Value ?? "unknown",
                    "Middleware",
                    "ClientRequestCanceled",
                    exception,
                    context.Request.Path.Value,
                    context.Request.Method,
                    StatusCodes.Status499ClientClosedRequest,
                    correlationId,
                    new
                    {
                        queryString = context.Request.QueryString.Value,
                        traceIdentifier = context.TraceIdentifier,
                        requestAborted = true
                    },
                    UserAgent: context.Request.Headers.UserAgent.ToString(),
                    RemoteIpAddress: context.Connection.RemoteIpAddress?.ToString()), CancellationToken.None);

                return;
            }

            await errorLogger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                context.Request.Path.Value ?? "unknown",
                "Middleware",
                $"{context.Request.Method} {context.Request.Path}",
                exception,
                context.Request.Path.Value,
                context.Request.Method,
                StatusCodes.Status500InternalServerError,
                correlationId,
                    new
                    {
                        queryString = context.Request.QueryString.Value,
                        traceIdentifier = context.TraceIdentifier,
                        providerException = BuildProviderExceptionMetadata(exception, correlationId)
                    },
                    UserAgent: context.Request.Headers.UserAgent.ToString(),
                    RemoteIpAddress: context.Connection.RemoteIpAddress?.ToString()), CancellationToken.None);

            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    error = "An unexpected application error occurred.",
                    correlationId
                });
            }
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationHeader)
            && !string.IsNullOrWhiteSpace(correlationHeader.FirstOrDefault()))
        {
            return correlationHeader.First()!;
        }

        return context.TraceIdentifier;
    }

    private static bool IsClientRequestCancellation(Exception exception, HttpContext context)
    {
        return context.RequestAborted.IsCancellationRequested
            && exception is OperationCanceledException;
    }

    private static object? BuildProviderExceptionMetadata(Exception exception, string correlationId)
    {
        return exception is not TheOddsApiProviderException providerException
            ? null
            : new
            {
                providerName = providerException.ProviderName,
                statusCode = providerException.StatusCodeNumber,
                operation = providerException.Operation,
                sanitizedRequestUrl = providerException.SanitizedRequestUrl,
                rawProviderBody = providerException.RawProviderBody,
                providerException.ErrorCode,
                providerException.RecommendedAction,
                providerException.CreditUsage,
                correlationId
            };
    }
}
