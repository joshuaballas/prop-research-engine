namespace SportsBetting.Api.Diagnostics;

public sealed record ApplicationErrorLogRequest(
    string Severity,
    string Source,
    string Component,
    string? Operation,
    Exception Exception,
    string? RequestPath = null,
    string? RequestMethod = null,
    int? StatusCode = null,
    string? CorrelationId = null,
    object? Metadata = null,
    string? UserAgent = null,
    string? RemoteIpAddress = null,
    Guid? RunId = null,
    string? Sport = null,
    string? Provider = null);
