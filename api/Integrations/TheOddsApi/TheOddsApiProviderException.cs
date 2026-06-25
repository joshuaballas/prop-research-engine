using System.Net;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsApiProviderException : Exception
{
    public TheOddsApiProviderException(
        string errorCode,
        string message,
        string recommendedAction,
        HttpStatusCode? statusCode = null,
        string? providerBodyPreview = null,
        string? requestUrl = null,
        TheOddsApiCreditUsage? creditUsage = null,
        string? operation = null,
        string providerName = "The Odds API",
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        RecommendedAction = recommendedAction;
        StatusCode = statusCode;
        ProviderBodyPreview = providerBodyPreview;
        RequestUrl = requestUrl;
        RawProviderBody = providerBodyPreview;
        SanitizedRequestUrl = requestUrl;
        Operation = operation;
        ProviderName = providerName;
        CreditUsage = creditUsage ?? new TheOddsApiCreditUsage(null, null, null);
    }

    public string ErrorCode { get; }

    public string RecommendedAction { get; }

    public HttpStatusCode? StatusCode { get; }

    public int? StatusCodeNumber => StatusCode.HasValue ? (int)StatusCode.Value : null;

    public string? ProviderBodyPreview { get; }

    public string? RequestUrl { get; }

    public string? RawProviderBody { get; }

    public string? SanitizedRequestUrl { get; }

    public string? Operation { get; }

    public string ProviderName { get; }

    public TheOddsApiCreditUsage CreditUsage { get; }
}
