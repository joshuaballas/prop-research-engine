using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Integrations.PrizePicks;

public sealed class PrizePicksAdapter(
    HttpClient httpClient,
    IOptions<PrizePicksOptions> options,
    ILogger<PrizePicksAdapter> logger) : IPrizePicksAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PrizePicksOptions _options = options.Value;

    public async Task<IReadOnlyList<PrizePicksProjectionRecord>> GetActiveProjectionsAsync(CancellationToken cancellationToken)
    {
        var result = await FetchActiveProjectionsAsync(cancellationToken);

        return result.Projections;
    }

    public async Task<PrizePicksFetchResult> FetchActiveProjectionsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.ProjectionsUrl);
        request.Headers.Accept.ParseAdd("application/json, text/plain, */*");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseHeaders = FormatHeaders(response);
        var responseBodyPreview = rawJson.Length > 5000 ? rawJson[..5000] : rawJson;

        logger.LogInformation(
            "PrizePicks response received. StatusCode: {StatusCode}. Headers: {Headers}. BodyPreview: {BodyPreview}",
            (int)response.StatusCode,
            responseHeaders,
            responseBodyPreview);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"PrizePicks request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}). Response body: {rawJson}",
                null,
                response.StatusCode);
        }

        var payload = JsonSerializer.Deserialize<PrizePicksResponseDto>(rawJson, JsonOptions)
            ?? new PrizePicksResponseDto();

        var normalized = Normalize(payload);

        logger.LogInformation(
            "Parsed {ProjectionCount} PrizePicks projections from {Endpoint}. Sample players: {SamplePlayers}",
            normalized.Count,
            _options.ProjectionsUrl,
            string.Join(", ", normalized.Select(x => x.PlayerName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(5)));

        return new PrizePicksFetchResult(_options.ProjectionsUrl, rawJson, normalized);
    }

    private static List<PrizePicksProjectionRecord> Normalize(PrizePicksResponseDto payload)
    {
        var players = payload.Included
            .Where(x => string.Equals(x.Type, "new_player", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => new
            {
                x.Id,
                Attributes = DeserializePlayerAttributes(x.Attributes)
            })
            .Where(x => x.Attributes is not null)
            .ToDictionary(x => x.Id!, x => x.Attributes!);

        var records = new List<PrizePicksProjectionRecord>(payload.Data.Count);

        foreach (var projection in payload.Data)
        {
            var attributes = projection.Attributes;
            var playerId = projection.Relationships.NewPlayer?.Data?.Id;
            players.TryGetValue(playerId ?? string.Empty, out var player);

            var projectionId = projection.Id;
            if (string.IsNullOrWhiteSpace(projectionId))
            {
                continue;
            }

            var playerName = FirstNonEmpty(player?.DisplayName, player?.Name, attributes.Description, "Unknown Player");
            var statType = FirstNonEmpty(attributes.StatType, attributes.StatDisplayName, "Unknown Stat");
            records.Add(new PrizePicksProjectionRecord(
                ProjectionId: projectionId,
                StableKey: attributes.GroupKey,
                PlayerId: playerId,
                PlayerName: playerName,
                Team: player?.Team,
                TeamName: player?.TeamName,
                League: player?.League,
                LeagueId: projection.Relationships.League?.Data?.Id ?? player?.LeagueId?.ToString(),
                Position: player?.Position,
                Line: attributes.LineScore,
                PromoLine: attributes.FlashSaleLineScore,
                StatType: statType,
                StatDisplayName: attributes.StatDisplayName,
                Description: attributes.Description,
                Status: attributes.Status,
                OddsType: attributes.OddsType,
                IsLive: attributes.IsLive == true,
                IsPromo: attributes.IsPromo == true || attributes.FlashSaleLineScore.HasValue,
                StartTime: attributes.StartTime,
                UpdatedAt: attributes.UpdatedAt));
        }

        return records;
    }

    private static PrizePicksPlayerAttributesDto? DeserializePlayerAttributes(JsonElement attributes)
    {
        if (attributes.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        try
        {
            return attributes.Deserialize<PrizePicksPlayerAttributesDto>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string FormatHeaders(HttpResponseMessage response)
    {
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}");

        return string.Join("; ", headers);
    }
}
