# Underdog Adapter Implementation Plan

Created: June 11, 2026

Purpose: define the implementation approach for an Underdog Fantasy adapter that fits the existing PrizePicks ingestion patterns without writing application code in this step.

## Goals

Build an Underdog ingestion path that can:

- Fetch active Underdog Fantasy over/under lines.
- Store the full raw payload.
- Normalize lines into internal platform prop records.
- Create or update sports, leagues, players, props, platform props, and snapshots.
- Preserve Underdog-specific pricing and payout fields for later EV modeling.
- Avoid probability, correlation, optimizer, and frontend work.

## Existing Pattern to Reuse

The current PrizePicks implementation has three useful layers:

1. Adapter fetches raw JSON and deserializes typed DTOs.
2. Adapter normalizes platform-specific response data into a compact projection record.
3. Ingestion service persists raw payloads, upserts normalized entities, and creates snapshots.

Underdog should follow the same shape:

```text
UnderdogAdapter
  FetchActiveLinesAsync
  Deserialize UnderdogResponseDto
  Normalize to UnderdogProjectionRecord[]

UnderdogIngestionService
  Create ingestion run
  Store raw payload
  Upsert platform/reference entities
  Upsert players/props/platform props
  Create snapshots
```

The first implementation can either create a dedicated `UnderdogIngestionService` or extract shared persistence helpers later. For the first adapter, prefer a dedicated service to keep Underdog-specific joins and pricing decisions explicit.

## Endpoint and Options

Add an options object equivalent to `PrizePicksOptions`:

```text
Underdog:
  OverUnderLinesUrl: https://api.underdogfantasy.com/beta/v5/over_under_lines
```

HTTP client configuration:

- Typed client: `IUnderdogAdapter, UnderdogAdapter`
- Timeout: 60 seconds initially, because the payload is large.
- Headers:
  - `Accept: application/json`
  - `User-Agent: SportsBettingAnalyzer/0.1`

Polling should be conservative: 3 to 5 minutes in normal mode, with backoff for access errors.

## DTO Structure

DTOs should model only fields needed for Phase 1 plus pricing metadata. Unknown fields are preserved through raw payload storage.

### `UnderdogResponseDto`

Fields:

- `List<UnderdogAppearanceDto> Appearances`
- `List<UnderdogGameDto> Games`
- `int? OpenedLinesCount`
- `List<UnderdogOverUnderLineDto> OverUnderLines`
- `List<UnderdogPlayerDto> Players`
- `List<UnderdogSoloGameDto> SoloGames`

Use JSON property names matching the payload:

- `appearances`
- `games`
- `opened_lines_count`
- `over_under_lines`
- `players`
- `solo_games`

### `UnderdogOverUnderLineDto`

Fields:

- `string? Id`
- `string? ContractTermsUrl`
- `string? ContractUrl`
- `string? EntryStableId`
- `DateTimeOffset? ExpiresAt`
- `string? LineType`
- `JsonElement? LiveEvent`
- `JsonElement? LiveEventStat`
- `decimal? NonDiscountedStatValue`
- `List<UnderdogOptionDto> Options`
- `UnderdogOverUnderDto? OverUnder`
- `string? OverUnderId`
- `string? ProviderId`
- `int? Rank`
- `string? SortBy`
- `string? StableId`
- `decimal? StatValue`
- `string? Status`
- `DateTimeOffset? UpdatedAt`

Notes:

- `stat_value`, `non_discounted_stat_value`, prices, and multipliers were observed as JSON strings. The DTO should use nullable decimals if `System.Text.Json` numeric-string handling is configured, or strings in DTOs with explicit parse helpers during normalization.
- `live_event` and `live_event_stat` can stay as `JsonElement` until live parsing is needed.

### `UnderdogOverUnderDto`

Fields:

- `string? Id`
- `string? Title`
- `string? Category`
- `string? DisplayMode`
- `bool? HasAlternates`
- `UnderdogAppearanceStatDto? AppearanceStat`
- `JsonElement? Boost`
- `JsonElement? PredictionMarket`

### `UnderdogAppearanceStatDto`

Fields:

- `string? AppearanceId`
- `string? DisplayStat`
- `string? GradedBy`
- `string? PickemStatId`
- `int? Rank`
- `string? Stat`

### `UnderdogOptionDto`

Fields:

- `string? Id`
- `string? AmericanPrice`
- `string? AppearanceId`
- `string? Choice`
- `string? ChoiceDisplay`
- `string? ChoiceDisplayNameShorter`
- `string? ChoiceDisplayShort`
- `string? ChoiceId`
- `decimal? DecimalPrice`
- `string? GroupingId`
- `string? OverUnderLineId`
- `decimal? PayoutMultiplier`
- `decimal? RawProbability`
- `bool? RfqCombinable`
- `string? SelectionHeader`
- `string? SelectionSubheader`
- `string? Status`
- `string? Type`
- `DateTimeOffset? UpdatedAt`

Use parse helpers if decimal values arrive as strings.

### `UnderdogAppearanceDto`

Fields:

- `string? Id`
- `int? MatchId`
- `string? MatchType`
- `bool? MultiplePicksAllowed`
- `string? PlayerId`
- `string? PositionId`
- `string? TeamId`
- `string? Type`
- `JsonElement? Badges`
- `string? LineupStatusId`
- `string? SortBy`

### `UnderdogPlayerDto`

Fields:

- `string? Id`
- `string? FirstName`
- `string? LastName`
- `string? SportId`
- `string? TeamId`
- `string? PositionId`
- `string? PositionName`
- `string? PositionDisplayName`
- `string? JerseyNumber`
- `string? Country`
- `string? ImageUrl`
- `string? LightImageUrl`
- `string? DarkImageUrl`

### `UnderdogGameDto`

Fields:

- `int? Id`
- `string? Title`
- `string? ShortTitle`
- `string? AbbreviatedTitle`
- `string? FullTeamNamesTitle`
- `string? HomeTeamId`
- `string? AwayTeamId`
- `DateTimeOffset? ScheduledAt`
- `string? SportId`
- `string? SeasonType`
- `string? Status`
- `int? Period`
- `decimal? HomeTeamScore`
- `decimal? AwayTeamScore`
- `DateTimeOffset? UpdatedAt`
- `int? Year`
- `string? Type`

### `UnderdogSoloGameDto`

Start with a permissive DTO:

- `JsonElement Raw`

or model fields only after inspecting solo-game joins in actual payloads. Phase 1 can ingest lines even when ordinary `games` does not join, as long as `StartsAt` falls back safely.

## Normalized Record Shape

Create an internal record similar to `PrizePicksProjectionRecord`, but including pricing and game fields:

```text
UnderdogProjectionRecord
  LineId
  StableKey
  OverUnderId
  EntryStableId
  PlayerId
  PlayerName
  FirstName
  LastName
  TeamId
  League
  SportId
  Position
  Line
  NonDiscountedLine
  StatType
  StatDisplayName
  Title
  Status
  LineType
  ProviderId
  OverAvailable
  UnderAvailable
  HigherOption
  LowerOption
  GameId
  GameTitle
  StartsAt
  UpdatedAt
  ExpiresAt
```

`HigherOption` and `LowerOption` can be compact records containing:

- `OptionId`
- `Choice`
- `Status`
- `AmericanPrice`
- `DecimalPrice`
- `PayoutMultiplier`
- `SelectionHeader`
- `SelectionSubheader`
- `UpdatedAt`

## Normalization Process

1. Deserialize the response.
2. Build lookup dictionaries:
   - appearances by `id`
   - players by `id`
   - games by `id`
   - solo games by `id`, if needed later
3. For each `over_under_lines[]`:
   - Validate `id`.
   - Validate `stat_value` can parse to decimal.
   - Read `appearance_id` from `over_under.appearance_stat.appearance_id`.
   - Join to appearance.
   - Join appearance to player.
   - Join appearance to game when `match_type == "Game"` and `match_id` is present.
   - Extract higher and lower active options by `choice`.
   - Normalize sport/league from game `sport_id`, then player `sport_id`, then fallback `UNKNOWN`.
   - Emit a normalized record even if one side is missing, marking availability accordingly.
4. Log:
   - raw line count
   - normalized line count
   - missing player count
   - missing appearance count
   - missing game count
   - lines missing higher/lower options
   - sample players

## Entity Mapping Strategy

Use existing schema without migrations for the first implementation.

### Platform

`platforms`:

- `Name`: `Underdog Fantasy`
- `Slug`: `underdog`
- `IsActive`: `true`

### Sport

Map Underdog `sport_id` to internal `sports.code` and `sports.name`.

Recommended initial mapping:

| Underdog sport ID | Sport code | Sport name |
| --- | --- | --- |
| NFL | FOOTBALL | Football |
| CFL | FOOTBALL | Football |
| NBA | BASKETBALL | Basketball |
| WNBA | BASKETBALL | Basketball |
| MLB | BASEBALL | Baseball |
| KBO | BASEBALL | Baseball |
| NPB | BASEBALL | Baseball |
| NHL | HOCKEY | Hockey |
| FIFA | SOCCER | Soccer |
| TENNIS | TENNIS | Tennis |
| MMA | MMA | MMA |
| CS | ESPORTS | Esports |
| ESPORTS | ESPORTS | Esports |
| VAL | ESPORTS | Esports |
| LOL | ESPORTS | Esports |
| F1SZN | RACING | Racing |
| MOTORCYCLE | RACING | Racing |
| CWS | BASEBALL | Baseball |
| OUTLAWS | SOFTBALL | Softball |
| RAF | UNKNOWN | Unknown |

Unknown values should create or use `UNKNOWN`, but retain the original code in league and metadata.

### League

For Phase 1, treat the Underdog `sport_id` as the league code when no richer league object exists.

`leagues`:

- `SportId`: inferred sport ID
- `Code`: normalized Underdog `sport_id`
- `Name`: original Underdog `sport_id`
- `SeasonType`: game `season_type` when available

Examples:

- `FIFA` under sport `SOCCER`
- `MLB` under sport `BASEBALL`
- `WNBA` under sport `BASKETBALL`

League configuration:

- Ensure every observed league has a `league_configurations` row.
- Preserve current behavior: ingest all leagues, but default only configured major leagues for downstream workflows.
- For Underdog soccer, do not force `FIFA` into enabled `SOCCER`; store `FIFA` as its own league configuration unless a later mapping table defines otherwise.

### Teams

Phase 1 can skip team upserts and store Underdog `team_id`, home/away team IDs, and titles in metadata. The current PrizePicks path also does not upsert teams.

If team upserts are added later:

- Use `team_id` as external ref.
- Team name may not be directly available in the verified payload.
- Game title can be used only as a display fallback, not as stable team identity.

### Players

`players`:

- `DisplayName`: combine `first_name` and `last_name`; fallback to option `selection_header`; fallback to line title.
- `FirstName`: `players[].first_name`
- `LastName`: `players[].last_name`
- `LeagueId`: inferred league
- `Position`: prefer `position_display_name`; fallback `position_name`
- `ExternalRefs`: include Underdog player ID and related identifiers.

Recommended `external_refs` shape:

```json
{
  "underdog": {
    "player_id": "69032a0e-6b72-43bd-88ee-03af64222f14",
    "appearance_id": "988c25a5-9ecf-4ef0-afc1-add41ddd20b1",
    "team_id": "601290ff-bf6f-46c8-ad7c-04f7c0233c85",
    "sport_id": "FIFA",
    "position_id": "c7caca50-5cf8-44d5-806c-927b73f4f73a"
  }
}
```

Player matching priority:

1. Existing `external_refs.underdog.player_id`.
2. League ID + normalized display name.
3. Create new player.

Do not match Underdog players to PrizePicks players in Phase 1 unless a cross-platform identity strategy is added. The existing player table can hold platform-specific players safely.

### Games

The current schema requires home and away team IDs for `games`, but Underdog verified data does not provide reliable team names in the same payload. Avoid game upserts initially unless team handling is implemented first.

For `props.game_id`, use null in Phase 1.

Preserve game context in metadata:

- `match_id`
- `match_type`
- `game_title`
- `scheduled_at`
- `home_team_id`
- `away_team_id`
- `game_status`
- `season_type`

Use `scheduled_at` as `props.starts_at`.

## Stable Key Strategy

There are two different keys to consider:

1. Platform prop identity: for `platform_props.platform_prop_id`.
2. Normalized internal prop identity: for `props.normalized_key`.

### Platform Prop ID

Use:

```text
over_under_lines[].id
```

This is the unique line listing ID and maps directly to `platform_props.platform_prop_id`.

### External Stable Key

Use:

```text
over_under_lines[].stable_id
```

Store in `platform_props.external_stable_key`.

If `stable_id` is missing, fallback to:

```text
{over_under_id}|{line_type}
```

### Normalized Prop Key

The normalized prop should represent player + league + stat + game/time concept, not a specific Underdog line ID.

Recommended key:

```text
underdog:{league_code}:{stat_key}:{player_id_or_name}:{match_id_or_start}:{over_under_id}
```

Use stable values in this order:

- `league_code`: normalized Underdog sport/league code.
- `stat_key`: normalized `appearance_stat.stat`, fallback display stat.
- `player_id_or_name`: Underdog player ID when present; fallback normalized player name.
- `match_id_or_start`: `match_id` when present; fallback UTC `scheduled_at`; fallback `NO_GAME`.
- `over_under_id`: stable market ID.

Do not include `line_type` in the normalized prop key unless alternates should be treated as separate normalized props. Recommended Phase 1 behavior:

- Balanced and alternate lines should be separate `platform_props`.
- For `props`, include `over_under_id` and line value if alternates produce multiple active lines for the same stat/player/game.

Safer Phase 1 normalized key:

```text
underdog:{league_code}:{stat_key}:{player_id_or_name}:{match_id_or_start}:{over_under_id}:{line_type}:{line}
```

This prevents alternate lines from collapsing into one internal prop. Later, a more advanced market model can group alternates under a parent concept.

## Prop Type Mapping

`prop_types`:

- `Name`: `appearance_stat.display_stat`, fallback line title.
- `StatKey`: normalize `appearance_stat.stat`, fallback display stat.
- `ValueType`: `numeric`
- `SportId`: inferred sport.

Examples:

- `display_stat`: `Shots on Target`
- `stat`: `period_1_2_shots_on_target`
- `stat_key`: `PERIOD_1_2_SHOTS_ON_TARGET`

## Platform Prop Mapping

`platform_props`:

| Field | Underdog source |
| --- | --- |
| `prop_id` | matched/created `props.id` |
| `platform_id` | `underdog` platform |
| `platform_prop_id` | `over_under_lines[].id` |
| `external_stable_key` | `over_under_lines[].stable_id` |
| `label` | `over_under.title` |
| `current_line` | parsed `stat_value` |
| `over_available` | active higher option exists |
| `under_available` | active lower option exists |
| `payout_type` | `line_type`, fallback `standard` |
| `status` | `over_under_lines[].status` |
| `observed_at` | ingestion timestamp |
| `raw_payload_id` | current raw payload |
| `metadata` | Underdog-specific pricing/context |

For availability:

- `over_available = true` if any option has `choice == "higher"` and `status == "active"`.
- `under_available = true` if any option has `choice == "lower"` and `status == "active"`.
- Missing options should not fail ingestion.

## Snapshot Strategy

Create a `prop_line_snapshots` row when any snapshot-significant field changes:

- `line`
- `status`
- `over_available`
- `under_available`
- higher option price/multiplier
- lower option price/multiplier
- `line_type`
- `non_discounted_stat_value`

The current snapshot table only stores line, status, and availability. To detect pricing-only movement without schema changes, include pricing fields in `source_hash`.

Recommended source hash input:

```text
{line_id}
|{stat_value}
|{status}
|{over_available}
|{under_available}
|{line_type}
|{non_discounted_stat_value}
|higher:{higher_option_id}:{higher_status}:{higher_american}:{higher_decimal}:{higher_multiplier}
|lower:{lower_option_id}:{lower_status}:{lower_american}:{lower_decimal}:{lower_multiplier}
```

Snapshot row:

- `PlatformPropId`: platform prop ID.
- `Line`: parsed `stat_value`.
- `OverAvailable`: derived higher availability.
- `UnderAvailable`: derived lower availability.
- `Status`: line status.
- `ObservedAt`: ingestion timestamp.
- `SourceHash`: hash above.
- `RawPayloadId`: raw payload ID.

This preserves historical line movement and can detect pricing changes even before a dedicated pricing history table exists.

## Metadata Strategy for Pricing Fields

Store Underdog-specific fields in `platform_props.metadata` as JSON.

Recommended metadata shape:

```json
{
  "source": "underdog",
  "line": {
    "line_type": "balanced",
    "provider_id": "manual",
    "stable_id": "253bd872-87c9-46cf-9323-c087f4bac74c|balanced",
    "entry_stable_id": null,
    "over_under_id": "253bd872-87c9-46cf-9323-c087f4bac74c",
    "non_discounted_stat_value": null,
    "expires_at": null,
    "updated_at": "2026-06-11T23:11:33Z",
    "contract_url": null,
    "contract_terms_url": null,
    "rank": 91,
    "sort_by": null
  },
  "stat": {
    "display_stat": "Shots on Target",
    "stat": "period_1_2_shots_on_target",
    "pickem_stat_id": "..."
  },
  "options": {
    "higher": {
      "id": "109b34e9-3471-4c5e-a026-e3ddab95fac8",
      "choice_id": "over__",
      "status": "active",
      "american_price": "+219",
      "decimal_price": 3.19,
      "payout_multiplier": 1.3,
      "selection_header": "Patrik Schick",
      "selection_subheader": "Higher 1.5 Shots on Target",
      "rfq_combinable": true,
      "updated_at": "2026-06-10T23:00:11Z"
    },
    "lower": {
      "id": "d55c7ea5-c8f9-4a62-a096-7021d653a219",
      "choice_id": "under__",
      "status": "active",
      "american_price": "-278",
      "decimal_price": 1.36,
      "payout_multiplier": 0.67,
      "selection_header": "Patrik Schick",
      "selection_subheader": "Lower 1.5 Shots on Target",
      "rfq_combinable": true,
      "updated_at": "2026-06-10T23:00:11Z"
    }
  },
  "player": {
    "player_id": "69032a0e-6b72-43bd-88ee-03af64222f14",
    "team_id": "601290ff-bf6f-46c8-ad7c-04f7c0233c85",
    "sport_id": "FIFA",
    "position_id": "c7caca50-5cf8-44d5-806c-927b73f4f73a",
    "country": "CZE",
    "jersey_number": "10",
    "image_url": "https://..."
  },
  "game": {
    "match_id": 157081,
    "match_type": "Game",
    "title": "KOR vs CZE",
    "scheduled_at": "2026-06-12T02:00:00Z",
    "home_team_id": "ec643c76-bc3f-4407-aaec-36f7685b46ef",
    "away_team_id": "601290ff-bf6f-46c8-ad7c-04f7c0233c85",
    "status": "scheduled",
    "season_type": "regular"
  }
}
```

Metadata should be overwritten on each platform prop update so it represents current source state. Historical pricing changes are captured indirectly through snapshots using the source hash and raw payload reference.

## Raw Payload Strategy

Use the existing `raw_ingestion_payloads` table:

- Store the full response JSON.
- Store endpoint URL.
- Store payload SHA-256.
- Store captured timestamp.
- Link every current platform prop update and snapshot to the raw payload ID.

Because the payload is large, monitor database growth. Compression or retention policies can be added later if raw Underdog payloads grow too quickly.

## Ingestion Run Strategy

Use existing `ingestion_runs`:

- `PlatformId`: Underdog platform ID.
- `Status`: `running`, `succeeded`, or `failed`.
- `StartedAt` / `CompletedAt`.
- `PropsSeen`: normalized line count.
- `PropsChanged`: props created + updated, or platform props changed if that proves more useful.
- `PayloadSizeBytes`.
- `PayloadHash`.
- `SchemaVersion`: `underdog-over-under-lines-v1`.
- `WarningCount`: count of normalization warnings.
- `ErrorMessage`: exception message on failure.

Unlike the current PrizePicks service, Underdog should intentionally record failed ingestion runs after fetch or parse failures when possible. If the fetch itself fails before a raw payload exists, still create a failed run if platform resolution is available.

## Error and Data Quality Handling

Skip a line, with warning, when:

- line ID is missing.
- line value cannot parse.
- `over_under` or `appearance_stat` is missing.

Ingest with degraded metadata when:

- appearance join is missing.
- player join is missing.
- game join is missing.
- one side option is missing.
- price field is missing or unparsable.

Track warning counts by category in logs. Later this can become structured ingestion-run diagnostics.

## Suggested Files for Implementation

Proposed API project files:

```text
api/Integrations/Underdog/IUnderdogAdapter.cs
api/Integrations/Underdog/IUnderdogIngestionService.cs
api/Integrations/Underdog/UnderdogAdapter.cs
api/Integrations/Underdog/UnderdogDtos.cs
api/Integrations/Underdog/UnderdogFetchResult.cs
api/Integrations/Underdog/UnderdogIngestionResult.cs
api/Integrations/Underdog/UnderdogIngestionService.cs
api/Integrations/Underdog/UnderdogOptions.cs
api/Integrations/Underdog/UnderdogProjectionRecord.cs
api/Endpoints/UnderdogDebugEndpoints.cs
```

Optional later:

```text
api/Ingestion/UnderdogIngestionWorker.cs
```

## Debug Endpoint for Spike

Before persistence, add a debug endpoint equivalent to PrizePicks:

```text
GET /api/debug/underdog
```

Return:

- projection count
- sports/league breakdown
- player names
- line values
- stat types
- higher/lower availability counts
- sample pricing records
- sample normalized records

Then add persistence:

```text
POST /api/debug/underdog/ingest
```

Return:

- projections processed
- players created
- players updated
- props created
- props updated
- platform props created/updated if tracked
- snapshots created
- warnings
- ingestion duration

## Implementation Phases

### Phase 1: Fetch and Parse

- Create options, typed client, adapter DTOs, fetch result, normalized record.
- Fetch live endpoint.
- Parse into DTOs.
- Normalize joins into records.
- Add debug endpoint.
- Log counts and warnings.

### Phase 2: Persistence

- Create Underdog ingestion service.
- Store raw payload and ingestion run.
- Upsert platform, sports, leagues, league configurations, prop types, players, props, platform props.
- Create snapshots using Underdog source hash.
- Store metadata with pricing and context.

### Phase 3: Scheduling

- Add configurable Underdog background worker after manual ingest is stable.
- Use independent enable/interval config per platform or extend ingestion options to platform-specific schedules.
- Add health fields for each platform, not just one global ingestion state.

### Phase 4: Hardening

- Add schema drift checks.
- Add retry/backoff.
- Add payload size monitoring.
- Add raw payload retention policy.
- Add integration tests using captured fixture payloads.

## Open Questions

- Should alternates be separate normalized props or grouped under one parent prop? Recommended Phase 1 answer: separate by line value and line type.
- Should Underdog `FIFA` map to enabled `SOCCER` display workflows automatically? Recommended Phase 1 answer: store `FIFA` as observed, then manage display/analysis through league configuration.
- Should game and team entities be created now? Recommended Phase 1 answer: defer until team names are reliably available.
- Should pricing history get a dedicated table? Recommended Phase 1 answer: no; include pricing in metadata and snapshot source hash first.

## Feasibility Verdict

An Underdog adapter is feasible with the current schema and current endpoint.

No database migration is required for a first implementation if:

- team/game upserts are deferred,
- pricing data is stored in `platform_props.metadata`,
- pricing changes are included in `prop_line_snapshots.source_hash`.

The adapter should be built conservatively because the endpoint is undocumented, large, and likely protected by anti-automation controls.
