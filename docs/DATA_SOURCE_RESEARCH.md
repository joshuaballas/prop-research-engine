# Data Source Research

Research date: June 11, 2026

Purpose: Determine whether Phase 1 ingestion for PrizePicks and Underdog Fantasy is feasible before implementation begins.

## Executive Summary

Phase 1 data ingestion appears technically feasible for both PrizePicks and Underdog Fantasy using currently reachable, unauthenticated JSON endpoints. Both platforms expose enough public data to collect active props, current lines, player metadata, sport/league context, prop status, and timestamps suitable for historical line snapshots.

The main risk is not technical parsing. The main risk is operational and compliance-related: these are undocumented public application endpoints, not official developer APIs. They can change without notice, may be protected later, and may be subject to platform terms that restrict automated collection.

Recommended Phase 1 decision:

- Proceed with a proof-of-concept ingestion spike.
- Treat both integrations as unstable adapters.
- Store raw payloads exactly as received.
- Poll conservatively using observed cache headers.
- Add health checks for schema drift and sudden prop-count changes.
- Review platform terms and acceptable use before production use.

## Source Summary

| Platform | Verified endpoint | Authentication observed | Response style | Feasibility |
| --- | --- | --- | --- | --- |
| PrizePicks | `https://api.prizepicks.com/projections?...` | None for read-only projection fetch | JSON:API-style `data` plus `included` | Feasible, high schema-drift risk |
| Underdog Fantasy | `https://api.underdogfantasy.com/beta/v5/over_under_lines` | None for read-only line fetch | Denormalized top-level collections | Feasible, large payload and schema-drift risk |

Verified with read-only `GET` requests from the development environment on June 11, 2026.

## 1. PrizePicks Data Collection

### Collection Method

PrizePicks active projections can currently be collected from:

```text
GET https://api.prizepicks.com/projections?per_page=3&single_stat=true
```

A league-filtered variant was also verified:

```text
GET https://api.prizepicks.com/projections?league_id=7&per_page=10&single_stat=true
```

Observed response:

- HTTP status: `200`
- Content type: `application/json`
- Cache-Control: `public, max-age=20, stale-while-revalidate=10, stale-if-error=600`
- Rate-limit headers: none observed

The response uses a JSON:API-style structure:

- `data`: projection records
- `included`: related records such as players, leagues, games, durations, and stat types
- `links`: pagination or API metadata when present
- `meta`: response metadata when present

### Feasibility Notes

The endpoint returns active props with the line value, prop/stat name, event timing, status, promo/discount flags, and relationships to player, league, game, duration, score, and stat type.

This is sufficient for Phase 1:

- Store all active platform props.
- Track line movement using `line_score`.
- Track active/locked/live state using `status`, `start_time`, `end_time`, `in_game`, and `is_live`.
- Normalize players using related `new_player` records.
- Normalize leagues and stat types using relationship IDs and included objects.

### Query Behavior

Observed behavior suggests query parameters work, but should not be treated as a stable contract. A request with `per_page=3` returned a much larger result set during testing, while a league-filtered request produced a smaller response. The adapter should be written defensively:

- Do not assume `per_page` is honored.
- Read `links` and `meta` if pagination appears.
- Prefer platform-provided IDs over pagination assumptions.
- Use league filters only after validating they still return complete data.

## 2. Underdog Fantasy Data Collection

### Collection Method

Underdog Fantasy active over/under lines can currently be collected from:

```text
GET https://api.underdogfantasy.com/beta/v5/over_under_lines
```

Observed response:

- HTTP status: `200`
- Content type: `application/json; charset=utf-8`
- Cache-Control: `max-age=60, public, stale-while-revalidate=60, stale-if-error=600, s-maxage=60, no-store`
- Rate-limit headers: none observed

The response is a large denormalized payload with top-level collections:

- `over_under_lines`
- `appearances`
- `players`
- `games`
- `solo_games`
- `opened_lines_count`

### Feasibility Notes

The endpoint returns active prop lines with:

- line ID
- line value
- status
- line type
- provider ID
- update timestamp
- over/under metadata
- option-level higher/lower choices
- option-level prices and payout multipliers
- player records
- appearance records
- game/match references

This is sufficient for Phase 1 and provides more pricing detail than PrizePicks. Unlike PrizePicks, Underdog line options may expose `american_price`, `decimal_price`, and `payout_multiplier`, which are useful later for EV modeling.

### Payload Size

The verified response was approximately 12 MB and contained thousands of lines. The ingestion worker should avoid excessive polling and should stream or parse efficiently. The raw payload can be stored, but retention policies may matter sooner for Underdog than for PrizePicks.

## 3. Known Public Endpoints

### PrizePicks

Verified:

```text
https://api.prizepicks.com/projections?per_page=3&single_stat=true
https://api.prizepicks.com/projections?league_id=7&per_page=10&single_stat=true
```

Known useful query parameters from observed behavior:

- `league_id`: filters to a PrizePicks league ID.
- `single_stat`: requests single-stat projections.
- `per_page`: present in public examples and accepted by the endpoint, but not reliable as a strict limit based on testing.

Potentially discoverable through response relationships:

- league IDs
- player IDs
- game IDs
- stat type IDs
- projection type IDs

### Underdog Fantasy

Verified:

```text
https://api.underdogfantasy.com/beta/v5/over_under_lines
```

Known from response structure:

- The single endpoint returns both lines and supporting reference entities.
- The path includes `beta/v5`, so version changes should be expected.
- No separate player/game endpoint is needed for Phase 1 if this response remains complete.

## 4. Authentication Requirements

### PrizePicks

No authentication token, cookie, or API key was required for the verified projections endpoint.

Authentication will likely be required for:

- user account data
- entries
- wallet/account state
- placing picks
- personalized promotions

Those authenticated capabilities are not needed for this application. The application should only collect public board data.

### Underdog Fantasy

No authentication token, cookie, or API key was required for the verified over/under lines endpoint.

Authentication will likely be required for:

- user account data
- contests
- entries
- wallet/account state
- placing picks
- personalized availability

Those authenticated capabilities are not needed for Phase 1.

### Recommendation

Do not use user credentials, session cookies, or private account APIs for Phase 1. Keep ingestion limited to public, read-only board data.

## 5. Rate Limiting Concerns

No explicit rate-limit headers were observed from either platform during the test requests.

Observed cache headers imply preferred polling windows:

| Platform | Observed cache window | Recommended minimum poll interval |
| --- | --- | --- |
| PrizePicks | `max-age=20` | 30 to 60 seconds for proof of concept; 3 to 5 minutes for normal Phase 1 |
| Underdog Fantasy | `max-age=60` | 60 to 120 seconds for proof of concept; 3 to 5 minutes for normal Phase 1 |

Recommended safeguards:

- Use a descriptive user agent.
- Respect cache headers.
- Add exponential backoff on `429`, `403`, `5xx`, and connection failures.
- Add jitter so workers do not poll at exact fixed intervals.
- Avoid concurrent duplicate requests per platform.
- Do not poll more frequently than the platform cache window.
- Cache raw responses by payload hash to avoid duplicate writes.
- Disable ingestion automatically after repeated access errors.

## 6. Data Fields Available From Each Source

### PrizePicks Fields

Observed top-level keys:

- `data`
- `included`
- `links`
- `meta`

Observed projection fields:

- `adjusted_odds`
- `board_time`
- `custom_image`
- `description`
- `discount_name`
- `discount_percentage`
- `end_time`
- `event_type`
- `flash_sale_line_score`
- `game_id`
- `group_key`
- `in_game`
- `is_live`
- `is_live_scored`
- `is_promo`
- `line_score`
- `odds_type`
- `projection_type`
- `rank`
- `refundable`
- `start_time`
- `stat_display_name`
- `stat_type`
- `status`
- `trending_count`
- `tv_channel`
- `updated_at`

Observed relationships:

- `duration`
- `game`
- `league`
- `new_player`
- `projection_type`
- `score`
- `stat_type`

Observed related player fields:

- `display_name`
- `name`
- `league`
- `league_id`
- `team`
- `team_name`
- `position`
- `jersey_number`
- `image_url`
- `combo`
- `ppid`

Important Phase 1 mappings:

| Internal field | PrizePicks source |
| --- | --- |
| platform prop ID | `data[].id` |
| player external ID | `relationships.new_player.data.id` |
| player name | `included[type=new_player].attributes.display_name` |
| team | `included[type=new_player].attributes.team` |
| league | `relationships.league.data.id` and included league data |
| stat type | `attributes.stat_type` or `relationships.stat_type.data.id` |
| display stat | `attributes.stat_display_name` |
| current line | `attributes.line_score` |
| promo line | `attributes.flash_sale_line_score` |
| status | `attributes.status` |
| live flag | `attributes.is_live` |
| start time | `attributes.start_time` |
| updated time | `attributes.updated_at` |

### Underdog Fantasy Fields

Observed top-level keys:

- `appearances`
- `games`
- `opened_lines_count`
- `over_under_lines`
- `players`
- `solo_games`

Observed over/under line fields:

- `id`
- `contract_terms_url`
- `contract_url`
- `entry_stable_id`
- `expires_at`
- `line_type`
- `live_event`
- `live_event_stat`
- `non_discounted_stat_value`
- `options`
- `over_under`
- `over_under_id`
- `provider_id`
- `rank`
- `sort_by`
- `stable_id`
- `stat_value`
- `status`
- `updated_at`

Observed option fields:

- `id`
- `american_price`
- `appearance_id`
- `choice`
- `choice_display`
- `choice_display_name_shorter`
- `choice_display_short`
- `choice_id`
- `decimal_price`
- `grouping_id`
- `over_under_line_id`
- `payout_multiplier`
- `raw_probability`
- `rfq_combinable`
- `selection_header`
- `selection_subheader`
- `status`
- `type`
- `updated_at`

Observed nested over/under fields:

- `id`
- `title`
- `category`
- `display_mode`
- `has_alternates`
- `appearance_stat`
- `boost`
- `prediction_market`

Observed appearance stat fields:

- `appearance_id`
- `display_stat`
- `graded_by`
- `pickem_stat_id`
- `rank`
- `stat`

Observed player fields:

- `id`
- `first_name`
- `last_name`
- `sport_id`
- `team_id`
- `position_id`
- `position_name`
- `position_display_name`
- `jersey_number`
- `image_url`
- `country`

Important Phase 1 mappings:

| Internal field | Underdog source |
| --- | --- |
| platform prop ID | `over_under_lines[].id` |
| stable prop ID | `over_under_lines[].stable_id` |
| normalized over/under ID | `over_under_lines[].over_under_id` |
| current line | `over_under_lines[].stat_value` |
| status | `over_under_lines[].status` |
| stat type | `over_under_lines[].over_under.appearance_stat.stat` |
| display stat | `over_under_lines[].over_under.appearance_stat.display_stat` |
| title | `over_under_lines[].over_under.title` |
| options | `over_under_lines[].options[]` |
| over side availability | active option with `choice = higher` |
| under side availability | active option with `choice = lower` |
| price / payout | `options[].american_price`, `decimal_price`, `payout_multiplier` |
| player external ID | join appearance `player_id` to `players[].id` |
| game external ID | `appearances[].match_id` |
| updated time | `over_under_lines[].updated_at` |

## 7. Example Payload Structures

Examples are trimmed and anonymized only by omission. They are not full API responses.

### PrizePicks Projection Example

```json
{
  "projection": {
    "id": "12582741",
    "type": "projection",
    "attributes": {
      "line_score": 56,
      "stat_type": "Points (Combo)",
      "stat_display_name": "Points",
      "description": "NYK/SAS",
      "start_time": "2026-06-13T20:40:00.000-04:00",
      "status": "pre_game",
      "odds_type": "standard",
      "updated_at": "2026-06-11T00:57:19.427-04:00"
    },
    "relationships": {
      "new_player": "320746",
      "league": "7",
      "game": "165607",
      "stat_type": "425"
    }
  },
  "included_new_player": {
    "id": "320746",
    "attributes": {
      "display_name": "Victor Wembanyama + Jalen Brunson",
      "league": "NBA",
      "league_id": 7,
      "position": "F-C",
      "team": "SAS/NYK",
      "team_name": "Spurs/Knicks",
      "combo": true,
      "ppid": "CMBO_group_wmzPQjjnqB97cgUBKPqpcPFY"
    }
  }
}
```

### Underdog Over/Under Line Example

```json
{
  "over_under_line": {
    "id": "29460093-51bc-4fe2-9d3b-2012dc85ffb1",
    "stat_value": "2.5",
    "status": "active",
    "line_type": "balanced",
    "provider_id": "manual",
    "updated_at": "2026-06-11T20:54:48Z",
    "title": "Patrik Schick Shots Attempted O/U",
    "display_stat": "Shots Attempted",
    "stat": "period_1_2_shots_attempted",
    "options": [
      {
        "id": "f7ed7d0a-5ed7-4d84-8978-5c2af74cd747",
        "choice": "higher",
        "american_price": "+104",
        "decimal_price": "2.04",
        "payout_multiplier": "1.03",
        "selection_header": "Patrik Schick",
        "selection_subheader": "Higher 2.5 Shots Attempted",
        "status": "active"
      },
      {
        "id": "4a47c2e2-c385-4a7f-af75-e566a62a4d04",
        "choice": "lower",
        "american_price": "-127",
        "decimal_price": "1.79",
        "payout_multiplier": "0.88",
        "selection_header": "Patrik Schick",
        "selection_subheader": "Lower 2.5 Shots Attempted",
        "status": "active"
      }
    ]
  },
  "appearance": {
    "id": "988c25a5-9ecf-4ef0-afc1-add41ddd20b1",
    "player_id": "69032a0e-6b72-43bd-88ee-03af64222f14",
    "team_id": "601290ff-bf6f-46c8-ad7c-04f7c0233c85",
    "match_id": 157081,
    "match_type": "Game"
  },
  "player": {
    "id": "69032a0e-6b72-43bd-88ee-03af64222f14",
    "first_name": "Patrik",
    "last_name": "Schick",
    "sport_id": "FIFA",
    "position_name": "FW",
    "position_display_name": "Forward",
    "team_id": "601290ff-bf6f-46c8-ad7c-04f7c0233c85"
  }
}
```

## 8. Reliability Risks

### Undocumented API Risk

Neither verified endpoint should be treated as an official public developer API. Either platform may change the endpoint, schema, required headers, authentication behavior, or access policy without notice.

Impact:

- Ingestion failures.
- Missing props.
- Incorrect field mapping.
- Silent data quality regressions.

Mitigation:

- Adapter isolation.
- Raw payload retention.
- Schema drift checks.
- Required-field validation.
- Alerting on prop count changes.
- Manual disable switch per platform.

### Legal and Terms Risk

Automated collection may be restricted by platform terms or acceptable-use policies. This research verifies technical reachability, not legal permission.

Impact:

- Production use may be inappropriate without review.
- Access could be blocked.
- Account or IP-level enforcement could occur if authenticated or aggressive access is used.

Mitigation:

- Review current terms before production deployment.
- Avoid credentials and private account APIs.
- Poll conservatively.
- Use the data only for the intended internal analytics workflow unless rights are confirmed.

### Rate Limit and Blocking Risk

No rate-limit headers were observed, but that does not mean no limits exist. Protections may be enforced through CDN behavior, WAF rules, IP reputation, or traffic patterns.

Impact:

- `403`, `429`, or connection failures.
- Temporary or permanent endpoint blocking.

Mitigation:

- Respect cache headers.
- Back off aggressively on errors.
- Add random jitter.
- Avoid distributed concurrent polling.

### Schema Drift Risk

Fields such as PrizePicks `odds_type`, `flash_sale_line_score`, and Underdog `line_type`, `provider_id`, and nested `appearance_stat` may change as products evolve.

Impact:

- Bad normalization.
- Incorrect promo handling.
- Broken player/game joins.

Mitigation:

- Version adapter parsers.
- Store unknown fields in raw JSON.
- Add integration tests using captured payload fixtures.
- Fail closed when required fields disappear.

### Payload Size Risk

Underdog currently returns a large all-in-one payload. PrizePicks can also return large all-league responses.

Impact:

- Higher memory use.
- Larger raw payload storage.
- Slower parsing.
- Higher network transfer.

Mitigation:

- Prefer filtered requests when verified complete.
- Parse once per poll.
- Hash payloads before storing duplicates.
- Store compressed raw payloads if volume grows.
- Keep snapshot rows change-based rather than heartbeat-only.

### State and Availability Risk

Current board data may differ by jurisdiction, user state, app version, or eligibility. Public endpoint data may not perfectly match what every user sees.

Impact:

- UI may show props unavailable to some users.
- Optimizer may recommend entries that cannot be built in a given jurisdiction or account.

Mitigation:

- Label recommendations as platform-board observations.
- Add jurisdiction/account-specific support only if explicitly required and legally approved.

## 9. Recommended Implementation Approach

### Phase 1 Ingestion Spike

Before building full ingestion, run a short spike:

1. Fetch each verified endpoint on a conservative interval.
2. Store raw payloads and hashes.
3. Parse into a temporary normalized model.
4. Measure payload size, parse time, and prop counts.
5. Track line changes for at least one active sports slate.
6. Document schema changes or access failures.

Success criteria:

- Repeated successful reads over several hours.
- Stable required field mapping.
- Detectable line movement snapshots.
- No access blocks at conservative polling intervals.

### Adapter Design

Create one adapter per platform:

- `PrizePicksAdapter`
- `UnderdogFantasyAdapter`

Each adapter should emit the same internal ingestion item shape:

- platform
- platform prop ID
- stable external key
- player external ID
- player display name
- team
- sport/league
- game external ID
- prop type
- display label
- current line
- over availability
- under availability
- status
- promo/discount flags
- start time
- updated time
- raw payload reference

### Snapshot Policy

Insert a new line snapshot when any of these change:

- current line
- over availability
- under availability
- status
- promo/discount line
- payout type or odds type

Do not insert duplicate snapshots when only the poll timestamp changes.

### Polling Policy

Recommended initial settings:

- PrizePicks: every 3 minutes in normal mode.
- Underdog Fantasy: every 3 minutes in normal mode.
- Spike mode: no faster than observed cache headers plus jitter.
- Error backoff: double delay after each failure up to 30 minutes.

### Data Completeness Checks

Every run should record:

- HTTP status.
- response byte size.
- payload hash.
- total raw props seen.
- active props parsed.
- skipped props.
- parse errors.
- required-field missing counts.
- previous run comparison.

Alert thresholds:

- 50 percent drop in parsed props.
- zero active props during expected active periods.
- missing required fields.
- repeated HTTP failures.
- payload shape changes.

### Recommended Storage Additions

The architecture schema already supports the needed ingestion flow. Consider adding these fields or tables during implementation:

- `raw_ingestion_payloads.compressed_payload` or compression-at-rest support.
- `platform_props.external_stable_key` for Underdog `stable_id` and PrizePicks `group_key`.
- `platform_props.metadata jsonb` for platform-specific flags such as PrizePicks promos and Underdog payout multipliers.
- `ingestion_runs.payload_size_bytes`.
- `ingestion_runs.payload_hash`.
- `ingestion_runs.schema_version`.
- `ingestion_runs.warning_count`.

### Feasibility Verdict

Phase 1 is technically feasible with the current public endpoints.

Recommended go/no-go:

- Go for local proof of concept and internal development.
- Conditional go for production only after legal/terms review.
- Do not design the system as if these endpoints are guaranteed contracts.
- Build adapters and monitoring as first-class features, not afterthoughts.

The safest implementation path is a conservative ingestion service that treats external source data as volatile, preserves raw responses, validates aggressively, and can be disabled per platform without affecting the rest of the application.
