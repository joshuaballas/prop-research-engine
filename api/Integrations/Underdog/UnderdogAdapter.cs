using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SportsBetting.Api.Integrations.Underdog;

public sealed class UnderdogAdapter(
    HttpClient httpClient,
    IOptions<UnderdogOptions> options,
    ILogger<UnderdogAdapter> logger) : IUnderdogAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true
    };

    private readonly UnderdogOptions _options = options.Value;

    public async Task<IReadOnlyList<UnderdogProjectionRecord>> GetActiveProjectionsAsync(CancellationToken cancellationToken)
    {
        var result = await FetchActiveProjectionsAsync(cancellationToken);

        return result.Projections;
    }

    public async Task<UnderdogFetchResult> FetchActiveProjectionsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.OverUnderLinesUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<UnderdogResponseDto>(rawJson, JsonOptions)
            ?? new UnderdogResponseDto();

        var normalized = Normalize(payload);

        logger.LogInformation(
            "Parsed {ProjectionCount} Underdog projections from {Endpoint}. Sample players: {SamplePlayers}",
            normalized.Count,
            _options.OverUnderLinesUrl,
            string.Join(", ", normalized.Select(x => x.PlayerName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(5)));

        return new UnderdogFetchResult(_options.OverUnderLinesUrl, rawJson, normalized);
    }

    private static List<UnderdogProjectionRecord> Normalize(UnderdogResponseDto payload)
    {
        var appearances = payload.Appearances
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id!, StringComparer.OrdinalIgnoreCase);

        var players = payload.Players
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id!, StringComparer.OrdinalIgnoreCase);

        var games = payload.Games
            .Where(x => x.Id.HasValue)
            .Select(x => new { Id = x.Id.GetValueOrDefault(), Game = x })
            .ToDictionary(x => x.Id.ToString(CultureInfo.InvariantCulture), x => x.Game, StringComparer.OrdinalIgnoreCase);

        var records = new List<UnderdogProjectionRecord>(payload.OverUnderLines.Count);

        foreach (var line in payload.OverUnderLines)
        {
            if (string.IsNullOrWhiteSpace(line.Id))
            {
                continue;
            }

            var appearanceId = line.OverUnder?.AppearanceStat?.AppearanceId;
            appearances.TryGetValue(appearanceId ?? string.Empty, out var appearance);

            var playerId = appearance?.PlayerId;
            players.TryGetValue(playerId ?? string.Empty, out var player);

            var gameId = appearance?.MatchId?.ToString(CultureInfo.InvariantCulture);
            games.TryGetValue(gameId ?? string.Empty, out var game);

            var higherOption = NormalizeOption(line.Options.FirstOrDefault(x => string.Equals(x.Choice, "higher", StringComparison.OrdinalIgnoreCase)));
            var lowerOption = NormalizeOption(line.Options.FirstOrDefault(x => string.Equals(x.Choice, "lower", StringComparison.OrdinalIgnoreCase)));
            var playerName = FirstNonEmpty(JoinName(player?.FirstName, player?.LastName), higherOption?.SelectionHeader, lowerOption?.SelectionHeader, line.OverUnder?.Title, "Unknown Player");
            var sportId = FirstNonEmpty(game?.SportId, player?.SportId, "UNKNOWN");
            var statType = FirstNonEmpty(line.OverUnder?.AppearanceStat?.Stat, line.OverUnder?.AppearanceStat?.DisplayStat, line.OverUnder?.Title, "Unknown Stat");

            records.Add(new UnderdogProjectionRecord(
                LineId: line.Id,
                StableKey: line.StableId,
                OverUnderId: line.OverUnderId,
                EntryStableId: line.EntryStableId,
                PlayerId: playerId,
                PlayerName: playerName,
                FirstName: player?.FirstName,
                LastName: player?.LastName,
                TeamId: appearance?.TeamId ?? player?.TeamId,
                League: sportId,
                SportId: sportId,
                Position: FirstNonEmpty(player?.PositionDisplayName, player?.PositionName),
                Line: line.StatValue,
                NonDiscountedLine: line.NonDiscountedStatValue,
                StatType: statType,
                StatDisplayName: line.OverUnder?.AppearanceStat?.DisplayStat,
                Title: line.OverUnder?.Title,
                Status: line.Status,
                LineType: line.LineType,
                ProviderId: line.ProviderId,
                OverAvailable: IsActive(higherOption),
                UnderAvailable: IsActive(lowerOption),
                HigherOption: higherOption,
                LowerOption: lowerOption,
                GameId: gameId,
                GameTitle: FirstNonEmpty(game?.Title, game?.ShortTitle, game?.AbbreviatedTitle),
                StartsAt: game?.ScheduledAt,
                UpdatedAt: line.UpdatedAt,
                ExpiresAt: line.ExpiresAt));
        }

        return records;
    }

    private static UnderdogOptionRecord? NormalizeOption(UnderdogOptionDto? option)
    {
        if (option is null)
        {
            return null;
        }

        return new UnderdogOptionRecord(
            OptionId: option.Id,
            Choice: option.Choice,
            Status: option.Status,
            AmericanPrice: option.AmericanPrice,
            DecimalPrice: option.DecimalPrice,
            PayoutMultiplier: option.PayoutMultiplier,
            SelectionHeader: option.SelectionHeader,
            SelectionSubheader: option.SelectionSubheader,
            UpdatedAt: option.UpdatedAt);
    }

    private static bool IsActive(UnderdogOptionRecord? option)
    {
        return option is not null && string.Equals(option.Status, "active", StringComparison.OrdinalIgnoreCase);
    }

    private static string? JoinName(string? firstName, string? lastName)
    {
        var parts = new[] { firstName, lastName }
            .Where(x => !string.IsNullOrWhiteSpace(x));

        var name = string.Join(" ", parts);

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
