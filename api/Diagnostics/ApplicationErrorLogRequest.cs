namespace SportsBetting.Api.Diagnostics;

public sealed record ApplicationErrorLogRequest(
    string Severity,
    string Source,
    string Component,
    string Operation,
    Exception Exception,
    string? RequestPath = null,
    string? HttpMethod = null,
    int? StatusCode = null,
    string? CorrelationId = null,
    object? Metadata = null);
