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
                    "api",
                    "GlobalExceptionMiddleware",
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
                    }), CancellationToken.None);

                return;
            }

            await errorLogger.LogAsync(new ApplicationErrorLogRequest(
                "Error",
                "api",
                "GlobalExceptionMiddleware",
                "UnhandledApiException",
                exception,
                context.Request.Path.Value,
                context.Request.Method,
                StatusCodes.Status500InternalServerError,
                correlationId,
                new
                {
                    queryString = context.Request.QueryString.Value,
                    traceIdentifier = context.TraceIdentifier
                }), CancellationToken.None);

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
}
