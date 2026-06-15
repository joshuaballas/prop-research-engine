using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsBetting.Api.Data;
using SportsBetting.Api.Models;

namespace SportsBetting.Api.Integrations.Results.Mlb;

public sealed class MlbResultsProvider(
    HttpClient httpClient,
    ApplicationDbContext dbContext,
    IPlayerExternalRefRepository playerExternalRefRepository,
    IOptions<MlbResultsOptions> options,
    ILogger<MlbResultsProvider> logger) : IMlbResultsProvider
{
    private const string Provider = "mlb";
    private const string LeagueCode = "MLB";
    private readonly MlbResultsOptions options = options.Value;

    public string SourceName => Provider;

    public async Task<IReadOnlyCollection<ResultRecord>> GetResultsAsync(
        DateOnly eventDate,
        CancellationToken cancellationToken)
    {
        var results = await GetMlbResultsAsync(eventDate, cancellationToken);

        return results
            .Where(result => result.IsMapped)
            .Select(result => result.ResultRecord)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<MlbResultRecord>> GetMlbResultsAsync(
        DateOnly eventDate,
        CancellationToken cancellationToken)
    {
        var schedule = await GetJsonAsync($"/api/v1/schedule?sportId=1&date={FormatDate(eventDate)}", cancellationToken);
        using (schedule)
        {
            var gameIds = ExtractFinalGameIds(schedule.RootElement).ToArray();
            var externalRefs = await playerExternalRefRepository.GetByProviderAsync(Provider, cancellationToken);
            var mlbSport = await GetOrCreateMlbSportAsync(cancellationToken);
            var propTypes = await LoadPropTypesAsync(mlbSport.Id, cancellationToken);
            var records = new List<MlbResultRecord>();
            var processedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var gameId in gameIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var boxScore = await GetJsonAsync($"/api/v1/game/{Uri.EscapeDataString(gameId)}/boxscore", cancellationToken);
                AddBoxScoreRecords(boxScore.RootElement, eventDate, gameId, externalRefs, propTypes, records, processedPlayers);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Processed {GameCount} MLB finalized games for {Date}. Players processed: {PlayerCount}; results produced: {ResultCount}.",
                gameIds.Length,
                eventDate,
                processedPlayers.Count,
                records.Count);

            return records;
        }
    }

    public async Task<IReadOnlyCollection<MlbPlayerRecord>> GetPlayersAsync(
        int season,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"/api/v1/sports/1/players?season={season}", cancellationToken);

        if (!document.RootElement.TryGetProperty("people", out var people) || people.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return people
            .EnumerateArray()
            .Select(ToPlayerRecord)
            .Where(player => player is not null)
            .Select(player => player!)
            .ToArray();
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"MLB Stats API request to '{relativeUrl}' failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        return JsonDocument.Parse(body);
    }

    private static IEnumerable<string> ExtractFinalGameIds(JsonElement scheduleRoot)
    {
        if (!scheduleRoot.TryGetProperty("dates", out var dates) || dates.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var date in dates.EnumerateArray())
        {
            if (!date.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var game in games.EnumerateArray())
            {
                var status = GetObject(game, "status");
                var detailedState = status is null ? null : GetString(status.Value, "detailedState");
                var abstractState = status is null ? null : GetString(status.Value, "abstractGameState");

                if (!string.Equals(detailedState, "Final", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(abstractState, "Final", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var gameId = GetString(game, "gamePk");
                if (!string.IsNullOrWhiteSpace(gameId))
                {
                    yield return gameId;
                }
            }
        }
    }

    private void AddBoxScoreRecords(
        JsonElement boxScoreRoot,
        DateOnly eventDate,
        string gameId,
        IReadOnlyDictionary<string, PlayerExternalRef> externalRefs,
        IDictionary<string, PropType> propTypes,
        ICollection<MlbResultRecord> records,
        ISet<string> processedPlayers)
    {
        var teams = GetObject(boxScoreRoot, "teams");
        if (teams is null)
        {
            return;
        }

        foreach (var side in new[] { "away", "home" })
        {
            var team = GetObject(teams.Value, side);
            var players = team is null ? null : GetObject(team.Value, "players");
            if (players is null)
            {
                continue;
            }

            foreach (var playerProperty in players.Value.EnumerateObject())
            {
                var playerElement = playerProperty.Value;
                var person = GetObject(playerElement, "person");
                var mlbPlayerId = person is null ? null : GetString(person.Value, "id");
                var playerName = person is null ? string.Empty : GetString(person.Value, "fullName") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(mlbPlayerId) || string.IsNullOrWhiteSpace(playerName))
                {
                    continue;
                }

                processedPlayers.Add(mlbPlayerId);
                externalRefs.TryGetValue(mlbPlayerId, out var externalRef);
                var batting = GetObject(GetObject(playerElement, "stats") ?? default, "batting");
                var pitching = GetObject(GetObject(playerElement, "stats") ?? default, "pitching");

                if (batting is not null)
                {
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Hits", "hits", batting.Value, "hits");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Runs", "runs", batting.Value, "runs");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "RBIs", "rbi", batting.Value, "rbi");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Home Runs", "homeRuns", batting.Value, "homeRuns");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Total Bases", "totalBases", batting.Value, "totalBases");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Stolen Bases", "stolenBases", batting.Value, "stolenBases");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Strikeouts", "strikeOuts", batting.Value, "strikeOuts");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Batter Walks", "batterWalks", batting.Value, "baseOnBalls");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Doubles", "doubles", batting.Value, "doubles");
                    AddComputedBattingStats(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, batting.Value);
                }

                if (pitching is not null)
                {
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Pitcher Strikeouts", "pitcherStrikeOuts", pitching.Value, "strikeOuts");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Walks Allowed", "walksAllowed", pitching.Value, "baseOnBalls");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Hits Allowed", "hitsAllowed", pitching.Value, "hits");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Earned Runs Allowed", "earnedRunsAllowed", pitching.Value, "earnedRuns");
                    AddStat(records, propTypes, externalRef, eventDate, gameId, mlbPlayerId, playerName, "Pitching Outs", "pitchingOuts", pitching.Value, "outs");
                }
            }
        }
    }

    private void AddComputedBattingStats(
        ICollection<MlbResultRecord> records,
        IDictionary<string, PropType> propTypes,
        PlayerExternalRef? externalRef,
        DateOnly eventDate,
        string gameId,
        string mlbPlayerId,
        string playerName,
        JsonElement batting)
    {
        if (TryGetDecimal(batting, "hits", out var hits)
            && TryGetDecimal(batting, "runs", out var runs)
            && TryGetDecimal(batting, "rbi", out var rbi))
        {
            AddComputedStat(
                records,
                propTypes,
                externalRef,
                eventDate,
                gameId,
                mlbPlayerId,
                playerName,
                "Hits + Runs + RBIs",
                "hitsRunsRbis",
                hits + runs + rbi);
        }

        if (TryGetDecimal(batting, "hits", out var singlesHits)
            && TryGetDecimal(batting, "doubles", out var doubles)
            && TryGetDecimal(batting, "triples", out var triples)
            && TryGetDecimal(batting, "homeRuns", out var homeRuns))
        {
            AddComputedStat(
                records,
                propTypes,
                externalRef,
                eventDate,
                gameId,
                mlbPlayerId,
                playerName,
                "Singles",
                "singles",
                singlesHits - doubles - triples - homeRuns);
        }
    }

    private void AddStat(
        ICollection<MlbResultRecord> records,
        IDictionary<string, PropType> propTypes,
        PlayerExternalRef? externalRef,
        DateOnly eventDate,
        string gameId,
        string mlbPlayerId,
        string playerName,
        string statName,
        string statKey,
        JsonElement statContainer,
        string jsonPropertyName)
    {
        if (!TryGetDecimal(statContainer, jsonPropertyName, out var value))
        {
            return;
        }

        var propType = GetOrCreatePropType(propTypes, statName, statKey);
        var isMapped = externalRef?.Player is not null && externalRef.Player.League is not null;
        var resultRecord = new ResultRecord(
            isMapped ? externalRef!.PlayerId : Guid.Empty,
            isMapped ? externalRef!.Player!.LeagueId : Guid.Empty,
            propType.Id,
            eventDate,
            value,
            BuildExternalSourceIdentifier(gameId, mlbPlayerId, statKey));

        records.Add(new MlbResultRecord(
            resultRecord,
            playerName,
            mlbPlayerId,
            gameId,
            statName,
            value,
            isMapped));
    }

    private void AddComputedStat(
        ICollection<MlbResultRecord> records,
        IDictionary<string, PropType> propTypes,
        PlayerExternalRef? externalRef,
        DateOnly eventDate,
        string gameId,
        string mlbPlayerId,
        string playerName,
        string statName,
        string statKey,
        decimal value)
    {
        var propType = GetOrCreatePropType(propTypes, statName, statKey);
        var isMapped = externalRef?.Player is not null && externalRef.Player.League is not null;
        var resultRecord = new ResultRecord(
            isMapped ? externalRef!.PlayerId : Guid.Empty,
            isMapped ? externalRef!.Player!.LeagueId : Guid.Empty,
            propType.Id,
            eventDate,
            value,
            BuildExternalSourceIdentifier(gameId, mlbPlayerId, statKey));

        records.Add(new MlbResultRecord(
            resultRecord,
            playerName,
            mlbPlayerId,
            gameId,
            statName,
            value,
            isMapped));
    }

    private async Task<Sport> GetOrCreateMlbSportAsync(CancellationToken cancellationToken)
    {
        var sport = await dbContext.Sports.SingleOrDefaultAsync(x => x.Code == "BASEBALL", cancellationToken);
        if (sport is not null)
        {
            return sport;
        }

        sport = new Sport
        {
            Id = Guid.NewGuid(),
            Name = "Baseball",
            Code = "BASEBALL"
        };

        dbContext.Sports.Add(sport);
        return sport;
    }

    private async Task<Dictionary<string, PropType>> LoadPropTypesAsync(Guid sportId, CancellationToken cancellationToken)
    {
        return await dbContext.PropTypes
            .Where(propType => propType.SportId == sportId)
            .ToDictionaryAsync(propType => propType.StatKey, StringComparer.OrdinalIgnoreCase, cancellationToken);
    }

    private PropType GetOrCreatePropType(IDictionary<string, PropType> propTypes, string statName, string statKey)
    {
        var normalizedStatKey = NormalizeCode(statKey);
        if (propTypes.TryGetValue(normalizedStatKey, out var propType))
        {
            if (propType.Name != statName)
            {
                propType.Name = statName;
            }

            return propType;
        }

        var sportId = dbContext.Sports.Local.FirstOrDefault(x => x.Code == "BASEBALL")?.Id
            ?? dbContext.Sports.Single(x => x.Code == "BASEBALL").Id;

        propType = new PropType
        {
            Id = Guid.NewGuid(),
            SportId = sportId,
            Name = statName,
            StatKey = normalizedStatKey,
            ValueType = "numeric"
        };

        dbContext.PropTypes.Add(propType);
        propTypes[normalizedStatKey] = propType;
        return propType;
    }

    private static JsonElement? GetObject(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0;

        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static MlbPlayerRecord? ToPlayerRecord(JsonElement player)
    {
        var playerId = GetString(player, "id");
        var displayName = GetString(player, "fullName");

        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var currentTeam = GetObject(player, "currentTeam");

        return new MlbPlayerRecord(
            playerId,
            displayName,
            GetString(player, "firstName"),
            GetString(player, "lastName"),
            currentTeam is null ? null : GetString(currentTeam.Value, "id"),
            currentTeam is null ? null : GetString(currentTeam.Value, "name"));
    }

    private static string BuildExternalSourceIdentifier(string gameId, string mlbPlayerId, string statKey)
    {
        return $"mlb:{gameId}:{mlbPlayerId}:{NormalizeCode(statKey)}";
    }

    private static string NormalizeCode(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousUnderscore = false;

        foreach (var character in value.Trim().ToUpperInvariant())
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

    private static string FormatDate(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
