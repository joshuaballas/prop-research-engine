using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public sealed class TheOddsApiMlbPlayerPropsAdapter(
    HttpClient httpClient,
    IOptions<TheOddsApiOptions> options,
    ILogger<TheOddsApiMlbPlayerPropsAdapter> logger) : ITheOddsApiMlbPlayerPropsAdapter
{
    private const string DataProvider = "The Odds API";
    private const int ProviderBodyPreviewLimit = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TheOddsApiOptions _options = options.Value;

    public async Task<TheOddsApiPlayerPropsSpikeResult> FetchSpikeAsync(
        string? eventId,
        string? markets,
        string? bookmakers,
        string? regions,
        string? oddsFormat,
        CancellationToken cancellationToken,
        string? sportKey = null)
    {
        EnsureApiKeyConfigured();
        var effectiveSportKey = FirstNonEmpty(sportKey, _options.SportKey);

        if (string.IsNullOrWhiteSpace(eventId))
        {
            return await FetchEventsAsync(effectiveSportKey, cancellationToken);
        }

        return await FetchEventOddsAsync(
            effectiveSportKey,
            eventId,
            FirstNonEmpty(markets, _options.Markets),
            bookmakers,
            FirstNonEmpty(regions, _options.Regions),
            FirstNonEmpty(oddsFormat, _options.OddsFormat),
            cancellationToken);
    }

    private async Task<TheOddsApiPlayerPropsSpikeResult> FetchEventsAsync(string sportKey, CancellationToken cancellationToken)
    {
        var url = BuildUrl($"/v4/sports/{Uri.EscapeDataString(sportKey)}/events", []);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await SendAsync(request, cancellationToken);
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfProviderFailure(response, rawJson, url, "FetchEvents");

        var events = DeserializeProviderJson<List<TheOddsApiEventDto>>(rawJson, url, response, "FetchEvents") ?? [];
        var eventRecords = events
            .OrderBy(x => x.CommenceTime)
            .Select(x => new TheOddsApiEventRecord(
                x.Id,
                x.SportKey,
                x.CommenceTime,
                x.HomeTeam,
                x.AwayTeam))
            .ToList();
        var selectedEventId = eventRecords
            .Where(x => x.EventStartTime >= DateTimeOffset.UtcNow.AddHours(-6))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.EventId))
            ?.EventId;

        logger.LogInformation(
            "The Odds API MLB player props spike returned {EventCount} events. SelectedEventId: {SelectedEventId}.",
            eventRecords.Count,
            selectedEventId);

        return new TheOddsApiPlayerPropsSpikeResult(
            eventRecords,
            selectedEventId,
            [],
            [],
            ExtractCreditUsage(response),
            MaskApiKey(url),
            rawJson);
    }

    private async Task<TheOddsApiPlayerPropsSpikeResult> FetchEventOddsAsync(
        string sportKey,
        string eventId,
        string markets,
        string? bookmakers,
        string regions,
        string oddsFormat,
        CancellationToken cancellationToken)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("regions", regions),
            new("markets", markets),
            new("oddsFormat", oddsFormat)
        };

        if (!string.IsNullOrWhiteSpace(bookmakers))
        {
            query.Add(new KeyValuePair<string, string?>("bookmakers", bookmakers));
        }

        var url = BuildUrl(
            $"/v4/sports/{Uri.EscapeDataString(sportKey)}/events/{Uri.EscapeDataString(eventId)}/odds",
            query);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await SendAsync(request, cancellationToken);
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfProviderFailure(response, rawJson, url, "FetchEventOdds");

        var payload = DeserializeProviderJson<TheOddsApiEventOddsDto>(rawJson, url, response, "FetchEventOdds")
            ?? new TheOddsApiEventOddsDto();
        var records = Normalize(payload);
        var diagnostics = BuildEventOddsDiagnostics(payload);

        logger.LogInformation(
            "The Odds API MLB player props spike parsed {RecordCount} outcome records for event {EventId}.",
            records.Count,
            eventId);

        return new TheOddsApiPlayerPropsSpikeResult(
            [
                new TheOddsApiEventRecord(
                    payload.Id,
                    payload.SportKey,
                    payload.CommenceTime,
                    payload.HomeTeam,
                    payload.AwayTeam)
            ],
            payload.Id ?? eventId,
            records,
            [diagnostics],
            ExtractCreditUsage(response),
            MaskApiKey(url),
            rawJson);
    }

    private static TheOddsApiEventOddsDiagnostics BuildEventOddsDiagnostics(TheOddsApiEventOddsDto payload)
    {
        var bookmakerKeys = payload.Bookmakers
            .Select(bookmaker => bookmaker.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var marketKeys = payload.Bookmakers
            .SelectMany(bookmaker => bookmaker.Markets)
            .Select(market => market.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var markets = payload.Bookmakers.SelectMany(bookmaker => bookmaker.Markets).ToArray();
        var outcomes = markets.SelectMany(market => market.Outcomes).ToArray();

        return new TheOddsApiEventOddsDiagnostics(
            payload.Id,
            payload.HomeTeam,
            payload.AwayTeam,
            payload.CommenceTime,
            EventOddsHttpSucceeded: true,
            BookmakerCount: payload.Bookmakers.Count,
            BookmakerKeys: bookmakerKeys,
            MarketCount: markets.Length,
            MarketKeys: marketKeys,
            OutcomeCount: outcomes.Length,
            PlayerOutcomeCount: outcomes.Count(outcome => !string.IsNullOrWhiteSpace(outcome.Description)));
    }

    private static List<TheOddsApiPlayerPropSpikeRecord> Normalize(TheOddsApiEventOddsDto payload)
    {
        var records = new List<TheOddsApiPlayerPropSpikeRecord>();

        foreach (var bookmaker in payload.Bookmakers)
        {
            foreach (var market in bookmaker.Markets)
            {
                foreach (var outcome in market.Outcomes)
                {
                    var side = NormalizeSide(outcome.Name);
                    var playerName = string.IsNullOrWhiteSpace(outcome.Description)
                        ? null
                        : outcome.Description.Trim();
                    var priceDecimal = ConvertAmericanToDecimal(outcome.Price);
                    var propType = NormalizePropTypeCandidate(market.Key);
                    var platformPropId = BuildPlatformPropIdCandidate(
                        bookmaker.Key,
                        payload.Id,
                        market.Key,
                        playerName,
                        side,
                        outcome.Point);

                    records.Add(new TheOddsApiPlayerPropSpikeRecord(
                        DataProvider,
                        bookmaker.Key,
                        bookmaker.Title,
                        payload.Id,
                        payload.CommenceTime,
                        payload.HomeTeam,
                        payload.AwayTeam,
                        market.Key,
                        market.LastUpdate ?? bookmaker.LastUpdate,
                        outcome.Name,
                        playerName,
                        side,
                        outcome.Point,
                        outcome.Price,
                        priceDecimal,
                        propType,
                        platformPropId,
                        new
                        {
                            dataProvider = DataProvider,
                            bookmaker = new
                            {
                                bookmaker.Key,
                                bookmaker.Title,
                                bookmaker.LastUpdate
                            },
                            eventInfo = new
                            {
                                payload.Id,
                                payload.SportKey,
                                payload.CommenceTime,
                                payload.HomeTeam,
                                payload.AwayTeam
                            },
                            market = new
                            {
                                market.Key,
                                market.LastUpdate
                            },
                            outcome = new
                            {
                                outcome.Name,
                                outcome.Description,
                                outcome.Price,
                                outcome.Point
                            }
                        }));
                }
            }
        }

        return records;
    }

    private string BuildUrl(string path, IReadOnlyCollection<KeyValuePair<string, string?>> query)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var builder = new StringBuilder($"{baseUrl}{path}?apiKey={Uri.EscapeDataString(_options.ApiKey)}");

        foreach (var pair in query.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            builder.Append('&');
            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value!));
        }

        return builder.ToString();
    }

    private static string MaskApiKey(string url)
    {
        const string parameter = "apiKey=";
        var keyIndex = url.IndexOf(parameter, StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0)
        {
            return url;
        }

        var valueStart = keyIndex + parameter.Length;
        var valueEnd = url.IndexOf('&', valueStart);

        return valueEnd < 0
            ? string.Concat(url.AsSpan(0, valueStart), "***")
            : string.Concat(url.AsSpan(0, valueStart), "***", url.AsSpan(valueEnd));
    }

    private void EnsureApiKeyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.Equals(_options.ApiKey.Trim(), "THE_ODDS_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            throw new TheOddsApiProviderException(
                "MISSING_API_KEY",
                "The Odds API key is not configured.",
                "Set TheOddsApi:ApiKey in appsettings.Local.json or THEODDSAPI__APIKEY, then restart the API.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TheOddsApiProviderException(
                "PROVIDER_TIMEOUT",
                "The Odds API request timed out.",
                "Retry later. If this persists, check network connectivity and The Odds API provider status.",
                requestUrl: MaskApiKey(request.RequestUri?.ToString() ?? string.Empty),
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TheOddsApiProviderException(
                "PROVIDER_REQUEST_FAILED",
                "The Odds API request failed before a response was received.",
                "Retry later. If this persists, check network connectivity and The Odds API provider status.",
                statusCode: exception.StatusCode,
                requestUrl: MaskApiKey(request.RequestUri?.ToString() ?? string.Empty),
                innerException: exception);
        }
    }

    private void ThrowIfProviderFailure(
        HttpResponseMessage response,
        string rawBody,
        string requestUrl,
        string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var preview = SanitizeProviderText(Preview(rawBody, ProviderBodyPreviewLimit));
        var creditUsage = ExtractCreditUsage(response);
        var (errorCode, message, recommendedAction) = BuildProviderFailure(response.StatusCode, preview);

        logger.LogWarning(
            "The Odds API {Operation} returned non-success status {StatusCode}. BodyPreview: {BodyPreview}",
            operation,
            (int)response.StatusCode,
            preview);

        throw new TheOddsApiProviderException(
            errorCode,
            message,
            recommendedAction,
            response.StatusCode,
            preview,
            MaskApiKey(requestUrl),
            creditUsage,
            operation);
    }

    private T? DeserializeProviderJson<T>(
        string rawJson,
        string requestUrl,
        HttpResponseMessage response,
        string operation)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(rawJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            var preview = SanitizeProviderText(Preview(rawJson, ProviderBodyPreviewLimit));

            logger.LogWarning(
                exception,
                "The Odds API {Operation} returned malformed JSON. StatusCode: {StatusCode}. BodyPreview: {BodyPreview}",
                operation,
                (int)response.StatusCode,
                preview);

            throw new TheOddsApiProviderException(
                "MALFORMED_PROVIDER_RESPONSE",
                "The Odds API returned a response that could not be parsed.",
                "Retry later. If this persists, inspect The Odds API response shape and adapter DTOs.",
                response.StatusCode,
                preview,
                MaskApiKey(requestUrl),
                ExtractCreditUsage(response),
                operation,
                DataProvider,
                exception);
        }
    }

    private static (string ErrorCode, string Message, string RecommendedAction) BuildProviderFailure(
        HttpStatusCode statusCode,
        string? bodyPreview)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => (
                "INVALID_KEY",
                "The Odds API rejected the configured API key.",
                "Check TheOddsApi:ApiKey configuration and restart the API."),
            HttpStatusCode.PaymentRequired => (
                "QUOTA_OR_PAYMENT_REQUIRED",
                "The Odds API rejected the request because credits, quota, or payment may be exhausted.",
                "Check The Odds API account usage, plan limits, and billing status before retrying."),
            (HttpStatusCode)429 => (
                "RATE_LIMITED",
                "The Odds API rate limited the request.",
                "Wait before retrying and reduce manual request frequency."),
            >= HttpStatusCode.InternalServerError => (
                "PROVIDER_ERROR",
                "The Odds API returned a provider error.",
                "Retry later. If this persists, check The Odds API provider status."),
            _ => (
                "PROVIDER_NON_SUCCESS",
                string.IsNullOrWhiteSpace(bodyPreview)
                    ? $"The Odds API returned HTTP {(int)statusCode}."
                    : $"The Odds API returned HTTP {(int)statusCode}.",
                "Review the sanitized provider response and request parameters before retrying.")
        };
    }

    private string SanitizeProviderText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = MaskApiKey(value);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            sanitized = sanitized.Replace(_options.ApiKey, "***", StringComparison.Ordinal);
        }

        return sanitized;
    }

    private static string Preview(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static TheOddsApiCreditUsage ExtractCreditUsage(HttpResponseMessage response)
    {
        return new TheOddsApiCreditUsage(
            GetHeaderInt(response, "x-requests-used"),
            GetHeaderInt(response, "x-requests-remaining"),
            GetHeaderInt(response, "x-requests-last"));
    }

    private static int? GetHeaderInt(HttpResponseMessage response, string headerName)
    {
        return response.Headers.TryGetValues(headerName, out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? NormalizeSide(string? outcomeName)
    {
        return outcomeName?.Trim().ToUpperInvariant() switch
        {
            "OVER" => "Over",
            "UNDER" => "Under",
            _ => outcomeName
        };
    }

    private static decimal? ConvertAmericanToDecimal(int? americanPrice)
    {
        if (!americanPrice.HasValue || americanPrice.Value == 0)
        {
            return null;
        }

        return americanPrice.Value > 0
            ? Math.Round(1m + americanPrice.Value / 100m, 4)
            : Math.Round(1m + 100m / Math.Abs(americanPrice.Value), 4);
    }

    private static string NormalizePropTypeCandidate(string? marketKey)
    {
        return marketKey switch
        {
            "batter_hits" => "Hits",
            "batter_runs_scored" => "Runs",
            "batter_rbis" => "RBIs",
            "batter_home_runs" => "Home Runs",
            "batter_total_bases" => "Total Bases",
            "batter_stolen_bases" => "Stolen Bases",
            "batter_strikeouts" => "Strikeouts",
            "pitcher_strikeouts" => "Pitcher Strikeouts",
            "batter_walks" => "Batter Walks",
            "batter_doubles" => "Doubles",
            "batter_singles" => "Singles",
            "batter_hits_runs_rbis" => "Hits + Runs + RBIs",
            "pitcher_walks" => "Walks Allowed",
            "pitcher_hits_allowed" => "Hits Allowed",
            "pitcher_earned_runs" => "Earned Runs Allowed",
            "pitcher_outs" => "Pitching Outs",
            "player_goals" => "Goals",
            "player_assists" => "Assists",
            "player_shots" => "Shots",
            "player_shots_on_target" => "Shots On Target",
            "player_tackles" => "Tackles",
            _ when !string.IsNullOrWhiteSpace(marketKey) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(marketKey.Replace('_', ' ')),
            _ => "Unknown"
        };
    }

    private static string BuildPlatformPropIdCandidate(
        string? bookmakerKey,
        string? eventId,
        string? marketKey,
        string? playerName,
        string? side,
        decimal? line)
    {
        var parts = new[]
        {
            "theodds",
            NormalizeKeyPart(bookmakerKey),
            NormalizeKeyPart(eventId),
            NormalizeKeyPart(marketKey),
            NormalizeKeyPart(playerName),
            NormalizeKeyPart(side),
            line?.ToString("0.####", CultureInfo.InvariantCulture) ?? "no_line"
        };

        return string.Join(':', parts);
    }

    private static string NormalizeKeyPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder(value.Length);
        var previousUnderscore = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousUnderscore = false;
            }
            else if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        return builder.ToString().Trim('_');
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
