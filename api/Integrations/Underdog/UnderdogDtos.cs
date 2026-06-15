using System.Text.Json;
using System.Text.Json.Serialization;

namespace SportsBetting.Api.Integrations.Underdog;

public sealed class UnderdogResponseDto
{
    [JsonPropertyName("appearances")]
    public List<UnderdogAppearanceDto> Appearances { get; set; } = [];

    [JsonPropertyName("games")]
    public List<UnderdogGameDto> Games { get; set; } = [];

    [JsonPropertyName("opened_lines_count")]
    public int? OpenedLinesCount { get; set; }

    [JsonPropertyName("over_under_lines")]
    public List<UnderdogOverUnderLineDto> OverUnderLines { get; set; } = [];

    [JsonPropertyName("players")]
    public List<UnderdogPlayerDto> Players { get; set; } = [];

    [JsonPropertyName("solo_games")]
    public List<JsonElement> SoloGames { get; set; } = [];
}

public sealed class UnderdogOverUnderLineDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("contract_terms_url")]
    public string? ContractTermsUrl { get; set; }

    [JsonPropertyName("contract_url")]
    public string? ContractUrl { get; set; }

    [JsonPropertyName("entry_stable_id")]
    public string? EntryStableId { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("line_type")]
    public string? LineType { get; set; }

    [JsonPropertyName("live_event")]
    public JsonElement? LiveEvent { get; set; }

    [JsonPropertyName("live_event_stat")]
    public JsonElement? LiveEventStat { get; set; }

    [JsonPropertyName("non_discounted_stat_value")]
    public decimal? NonDiscountedStatValue { get; set; }

    [JsonPropertyName("options")]
    public List<UnderdogOptionDto> Options { get; set; } = [];

    [JsonPropertyName("over_under")]
    public UnderdogOverUnderDto? OverUnder { get; set; }

    [JsonPropertyName("over_under_id")]
    public string? OverUnderId { get; set; }

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; set; }

    [JsonPropertyName("rank")]
    public long? Rank { get; set; }

    [JsonPropertyName("sort_by")]
    public string? SortBy { get; set; }

    [JsonPropertyName("stable_id")]
    public string? StableId { get; set; }

    [JsonPropertyName("stat_value")]
    public decimal? StatValue { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class UnderdogOverUnderDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("display_mode")]
    public string? DisplayMode { get; set; }

    [JsonPropertyName("has_alternates")]
    public bool? HasAlternates { get; set; }

    [JsonPropertyName("appearance_stat")]
    public UnderdogAppearanceStatDto? AppearanceStat { get; set; }

    [JsonPropertyName("boost")]
    public JsonElement? Boost { get; set; }

    [JsonPropertyName("prediction_market")]
    public JsonElement? PredictionMarket { get; set; }
}

public sealed class UnderdogAppearanceStatDto
{
    [JsonPropertyName("appearance_id")]
    public string? AppearanceId { get; set; }

    [JsonPropertyName("display_stat")]
    public string? DisplayStat { get; set; }

    [JsonPropertyName("graded_by")]
    public string? GradedBy { get; set; }

    [JsonPropertyName("pickem_stat_id")]
    public string? PickemStatId { get; set; }

    [JsonPropertyName("rank")]
    public long? Rank { get; set; }

    [JsonPropertyName("stat")]
    public string? Stat { get; set; }
}

public sealed class UnderdogOptionDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("american_price")]
    public string? AmericanPrice { get; set; }

    [JsonPropertyName("appearance_id")]
    public string? AppearanceId { get; set; }

    [JsonPropertyName("choice")]
    public string? Choice { get; set; }

    [JsonPropertyName("choice_display")]
    public string? ChoiceDisplay { get; set; }

    [JsonPropertyName("choice_display_name_shorter")]
    public string? ChoiceDisplayNameShorter { get; set; }

    [JsonPropertyName("choice_display_short")]
    public string? ChoiceDisplayShort { get; set; }

    [JsonPropertyName("choice_id")]
    public string? ChoiceId { get; set; }

    [JsonPropertyName("decimal_price")]
    public decimal? DecimalPrice { get; set; }

    [JsonPropertyName("grouping_id")]
    public string? GroupingId { get; set; }

    [JsonPropertyName("over_under_line_id")]
    public string? OverUnderLineId { get; set; }

    [JsonPropertyName("payout_multiplier")]
    public decimal? PayoutMultiplier { get; set; }

    [JsonPropertyName("raw_probability")]
    public decimal? RawProbability { get; set; }

    [JsonPropertyName("rfq_combinable")]
    public bool? RfqCombinable { get; set; }

    [JsonPropertyName("selection_header")]
    public string? SelectionHeader { get; set; }

    [JsonPropertyName("selection_subheader")]
    public string? SelectionSubheader { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class UnderdogAppearanceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("match_id")]
    public int? MatchId { get; set; }

    [JsonPropertyName("match_type")]
    public string? MatchType { get; set; }

    [JsonPropertyName("multiple_picks_allowed")]
    public bool? MultiplePicksAllowed { get; set; }

    [JsonPropertyName("player_id")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("position_id")]
    public string? PositionId { get; set; }

    [JsonPropertyName("team_id")]
    public string? TeamId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public sealed class UnderdogPlayerDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("sport_id")]
    public string? SportId { get; set; }

    [JsonPropertyName("team_id")]
    public string? TeamId { get; set; }

    [JsonPropertyName("position_id")]
    public string? PositionId { get; set; }

    [JsonPropertyName("position_name")]
    public string? PositionName { get; set; }

    [JsonPropertyName("position_display_name")]
    public string? PositionDisplayName { get; set; }

    [JsonPropertyName("jersey_number")]
    public string? JerseyNumber { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("light_image_url")]
    public string? LightImageUrl { get; set; }

    [JsonPropertyName("dark_image_url")]
    public string? DarkImageUrl { get; set; }
}

public sealed class UnderdogGameDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("short_title")]
    public string? ShortTitle { get; set; }

    [JsonPropertyName("abbreviated_title")]
    public string? AbbreviatedTitle { get; set; }

    [JsonPropertyName("full_team_names_title")]
    public string? FullTeamNamesTitle { get; set; }

    [JsonPropertyName("home_team_id")]
    public string? HomeTeamId { get; set; }

    [JsonPropertyName("away_team_id")]
    public string? AwayTeamId { get; set; }

    [JsonPropertyName("scheduled_at")]
    public DateTimeOffset? ScheduledAt { get; set; }

    [JsonPropertyName("sport_id")]
    public string? SportId { get; set; }

    [JsonPropertyName("season_type")]
    public string? SeasonType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}
