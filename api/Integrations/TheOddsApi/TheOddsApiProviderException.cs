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
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        RecommendedAction = recommendedAction;
        StatusCode = statusCode;
        ProviderBodyPreview = providerBodyPreview;
        RequestUrl = requestUrl;
        CreditUsage = creditUsage ?? new TheOddsApiCreditUsage(null, null, null);
    }

    public string ErrorCode { get; }

    public string RecommendedAction { get; }

    public HttpStatusCode? StatusCode { get; }

    public int? StatusCodeNumber => StatusCode.HasValue ? (int)StatusCode.Value : null;

    public string? ProviderBodyPreview { get; }

    public string? RequestUrl { get; }

    public TheOddsApiCreditUsage CreditUsage { get; }
}
