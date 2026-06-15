using System.Text.Json;
using System.Text.Json.Serialization;

namespace SportsBetting.Api.Integrations.PrizePicks;

public sealed class PrizePicksResponseDto
{
    [JsonPropertyName("data")]
    public List<PrizePicksProjectionDto> Data { get; set; } = [];

    [JsonPropertyName("included")]
    public List<PrizePicksIncludedResourceDto> Included { get; set; } = [];
}

public sealed class PrizePicksProjectionDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("attributes")]
    public PrizePicksProjectionAttributesDto Attributes { get; set; } = new();

    [JsonPropertyName("relationships")]
    public PrizePicksProjectionRelationshipsDto Relationships { get; set; } = new();
}

public sealed class PrizePicksProjectionAttributesDto
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("game_id")]
    public string? GameId { get; set; }

    [JsonPropertyName("group_key")]
    public string? GroupKey { get; set; }

    [JsonPropertyName("line_score")]
    public decimal? LineScore { get; set; }

    [JsonPropertyName("flash_sale_line_score")]
    public decimal? FlashSaleLineScore { get; set; }

    [JsonPropertyName("stat_type")]
    public string? StatType { get; set; }

    [JsonPropertyName("stat_display_name")]
    public string? StatDisplayName { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("odds_type")]
    public string? OddsType { get; set; }

    [JsonPropertyName("projection_type")]
    public string? ProjectionType { get; set; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    [JsonPropertyName("start_time")]
    public DateTimeOffset? StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public DateTimeOffset? EndTime { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("board_time")]
    public DateTimeOffset? BoardTime { get; set; }

    [JsonPropertyName("in_game")]
    public bool? InGame { get; set; }

    [JsonPropertyName("is_live")]
    public bool? IsLive { get; set; }

    [JsonPropertyName("is_promo")]
    public bool? IsPromo { get; set; }

    [JsonPropertyName("refundable")]
    public bool? Refundable { get; set; }

    [JsonPropertyName("trending_count")]
    public int? TrendingCount { get; set; }
}

public sealed class PrizePicksProjectionRelationshipsDto
{
    [JsonPropertyName("duration")]
    public PrizePicksRelationshipDto? Duration { get; set; }

    [JsonPropertyName("game")]
    public PrizePicksRelationshipDto? Game { get; set; }

    [JsonPropertyName("league")]
    public PrizePicksRelationshipDto? League { get; set; }

    [JsonPropertyName("new_player")]
    public PrizePicksRelationshipDto? NewPlayer { get; set; }

    [JsonPropertyName("projection_type")]
    public PrizePicksRelationshipDto? ProjectionType { get; set; }

    [JsonPropertyName("score")]
    public PrizePicksRelationshipDto? Score { get; set; }

    [JsonPropertyName("stat_type")]
    public PrizePicksRelationshipDto? StatType { get; set; }
}

public sealed class PrizePicksRelationshipDto
{
    [JsonPropertyName("data")]
    public PrizePicksResourceIdentifierDto? Data { get; set; }
}

public sealed class PrizePicksResourceIdentifierDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class PrizePicksIncludedResourceDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("attributes")]
    public JsonElement Attributes { get; set; }
}

public sealed class PrizePicksPlayerAttributesDto
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("league")]
    public string? League { get; set; }

    [JsonPropertyName("league_id")]
    public int? LeagueId { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }

    [JsonPropertyName("team_name")]
    public string? TeamName { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("ppid")]
    public string? PrizePicksPlayerId { get; set; }

    [JsonPropertyName("combo")]
    public bool? Combo { get; set; }
}
