using System.Text.Json.Serialization;

namespace SportsBetting.Api.Integrations.TheOddsApi;

public class TheOddsApiEventDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("sport_key")]
    public string? SportKey { get; set; }

    [JsonPropertyName("sport_title")]
    public string? SportTitle { get; set; }

    [JsonPropertyName("commence_time")]
    public DateTimeOffset? CommenceTime { get; set; }

    [JsonPropertyName("home_team")]
    public string? HomeTeam { get; set; }

    [JsonPropertyName("away_team")]
    public string? AwayTeam { get; set; }
}

public sealed class TheOddsApiEventOddsDto : TheOddsApiEventDto
{
    [JsonPropertyName("bookmakers")]
    public List<TheOddsApiBookmakerDto> Bookmakers { get; set; } = [];
}

public sealed class TheOddsApiBookmakerDto
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("last_update")]
    public DateTimeOffset? LastUpdate { get; set; }

    [JsonPropertyName("markets")]
    public List<TheOddsApiMarketDto> Markets { get; set; } = [];
}

public sealed class TheOddsApiMarketDto
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("last_update")]
    public DateTimeOffset? LastUpdate { get; set; }

    [JsonPropertyName("outcomes")]
    public List<TheOddsApiOutcomeDto> Outcomes { get; set; } = [];
}

public sealed class TheOddsApiOutcomeDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price")]
    public int? Price { get; set; }

    [JsonPropertyName("point")]
    public decimal? Point { get; set; }
}
