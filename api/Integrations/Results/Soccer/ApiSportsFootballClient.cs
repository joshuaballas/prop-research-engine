using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Integrations.Results.Soccer;

public sealed class ApiSportsFootballClient(
    HttpClient httpClient,
    IOptions<ApiSportsOptions> options,
    ILogger<ApiSportsFootballClient> logger) : IApiSportsFootballClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ApiSportsOptions options = options.Value;

    public Task<ApiSportsFootballDiagnosticResponse> GetFixturesAsync(ApiSportsFootballFixturesRequest request, CancellationToken cancellationToken)
    {
        var query = new List<KeyValuePair<string, string?>>();

        if (!string.IsNullOrWhiteSpace(request.FixtureId))
        {
            query.Add(new("id", request.FixtureId.Trim()));
        }
        else
        {
            if (request.Date.HasValue)
            {
                query.Add(new("date", request.Date.Value.ToString("yyyy-MM-dd")));
            }

            if (request.League.HasValue)
            {
                query.Add(new("league", request.League.Value.ToString()));
            }

            if (request.Season.HasValue)
            {
                query.Add(new("season", request.Season.Value.ToString()));
            }
        }

        return GetAsync("/fixtures", query, cancellationToken);
    }

    public Task<ApiSportsFootballDiagnosticResponse> GetFixturePlayerStatsAsync(string fixtureId, CancellationToken cancellationToken)
    {
        return GetAsync("/fixtures/players", [new("fixture", fixtureId)], cancellationToken);
    }

    public Task<ApiSportsFootballDiagnosticResponse> GetLeaguesAsync(ApiSportsFootballLeaguesRequest request, CancellationToken cancellationToken)
    {
        var query = new List<KeyValuePair<string, string?>>();
        if (request.League.HasValue)
        {
            query.Add(new("id", request.League.Value.ToString()));
        }

        return GetAsync("/leagues", query, cancellationToken);
    }

    private async Task<ApiSportsFootballDiagnosticResponse> GetAsync(
        string path,
        IReadOnlyCollection<KeyValuePair<string, string?>> query,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var url = BuildUrl(path, query);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("x-apisports-key", options.ApiKey);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("API-Sports Football returned HTTP {StatusCode}. Body preview: {BodyPreview}", (int)response.StatusCode, Preview(rawJson, 1000));
            throw new HttpRequestException($"API-Sports Football returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }

        JsonDocument payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonDocument>(rawJson, JsonOptions) ?? JsonDocument.Parse("{}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("API-Sports Football returned malformed JSON.", exception);
        }

        var root = payload.RootElement;
        return new ApiSportsFootballDiagnosticResponse(
            payload,
            TryGetInt(root, "results"),
            TryGetNestedInt(root, "paging", "current"),
            TryGetNestedInt(root, "paging", "total"),
            MaskApiKey(url));
    }

    private string BuildUrl(string path, IReadOnlyCollection<KeyValuePair<string, string?>> query)
    {
        var baseUrl = options.Football.BaseUrl.TrimEnd('/');
        var builder = new UriBuilder($"{baseUrl}{path}");
        var parameters = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        builder.Query = string.Join("&", parameters);
        return builder.Uri.ToString();
    }

    private void EnsureConfigured()
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("API-Sports is disabled. Set ApiSports:Enabled=true before live diagnostics.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey)
            || string.Equals(options.ApiKey.Trim(), "API_SPORTS_KEY", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("API-Sports API key is not configured.");
        }
    }

    private string MaskApiKey(string url)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return url;
        }

        return url.Replace(options.ApiKey, "***", StringComparison.Ordinal);
    }

    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static int? TryGetNestedInt(JsonElement root, string propertyName, string nestedPropertyName)
    {
        return root.TryGetProperty(propertyName, out var node) ? TryGetInt(node, nestedPropertyName) : null;
    }

    private static string Preview(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
