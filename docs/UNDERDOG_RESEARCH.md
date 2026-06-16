# Underdog Fantasy Research Spike

Research date: June 11, 2026

Purpose: determine whether Underdog Fantasy ingestion is feasible before implementing an adapter.

## Executive Summary

Underdog ingestion is technically feasible for Phase 1 using a currently reachable, unauthenticated JSON endpoint:

```text
GET https://api.underdogfantasy.com/beta/v5/over_under_lines
```

The endpoint returns active pick'em lines, player metadata, game metadata, line values, over/under option availability, and payout/pricing fields. This is enough to support the current ingestion flow: store raw payloads, normalize platform props, create/update players and leagues, and create line snapshots.

The main risk is operational rather than parsing. This endpoint appears to be an undocumented application endpoint, not a public developer API. It may change schema, require additional headers, become authenticated, or start rejecting automated traffic without notice.

Recommended decision: proceed with a local Underdog adapter spike, but treat the source as unstable and add schema-drift checks before running it continuously.

## Verified Public Endpoint

### Active Over/Under Lines

```text
GET https://api.underdogfantasy.com/beta/v5/over_under_lines
```

Observed on June 11, 2026:

| Item | Observed value |
| --- | --- |
| HTTP status | `200` |
| Content-Type | `application/json; charset=utf-8` |
| Cache-Control | `max-age=60, public, stale-while-revalidate=60, stale-if-error=600, s-maxage=60, no-store` |
| Rate-limit headers | None observed |
| Payload size | About 11.5 MB |
| Top-level collections | `appearances`, `games`, `opened_lines_count`, `over_under_lines`, `players`, `solo_games` |

Current observed payload counts:

| Collection | Count |
| --- | ---: |
| `over_under_lines` | 5,200 |
| `players` | 1,185 |
| `appearances` | 1,190 |
| `games` | 78 |
| `solo_games` | 50 |
| `opened_lines_count` | 0 |

No separate player, game, or league endpoint is required for Phase 1 if this payload remains complete.

## Authentication Requirements

No API key, bearer token, session cookie, or user account credential was required for the verified read-only endpoint.

Likely authenticated areas, not needed for this application:

- User account state
- Wallet data
- Entries
- Contests
- Personalized eligibility
- Placing picks

Recommendation: do not use account credentials or private authenticated APIs for Phase 1. Keep ingestion read-only and limited to public board data.

## Request Headers

The endpoint responded successfully to a basic unauthenticated `GET`.

Recommended adapter headers:

```text
Accept: application/json
User-Agent: SportsBettingAnalyzer/0.1
```

Observed requirements:

- No `Authorization` header required.
- No custom Underdog-specific header observed.
- No CSRF header observed.
- No cookie required for the verified endpoint.

Operational note: because this is likely an application endpoint behind CDN/WAF controls, a stable descriptive `User-Agent`, conservative polling, and no parallel duplicate requests are important.

## Response Schema

The response is a denormalized payload. The primary line records live in `over_under_lines`, with joins into `appearances`, `players`, and `games`.

### Top-Level Shape

```json
{
  "appearances": [],
  "games": [],
  "opened_lines_count": 0,
  "over_under_lines": [],
  "players": [],
  "solo_games": []
}
```

### `over_under_lines`

Observed line fields:

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

Important nested `over_under` fields:

- `id`
- `title`
- `category`
- `display_mode`
- `has_alternates`
- `appearance_stat`
- `boost`
- `prediction_market`

Important nested `appearance_stat` fields:

- `appearance_id`
- `display_stat`
- `graded_by`
- `pickem_stat_id`
- `rank`
- `stat`

### `options`

Each line contains one or more selectable options. For ordinary over/under props this is usually a higher option and a lower option.

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

Observed option choices:

| Choice | Count |
| --- | ---: |
| `higher` | 5,137 |
| `lower` | 2,827 |

The mismatch between higher and lower counts means the adapter must not assume every line has both sides available.

### `appearances`

Appearance records connect a line to a player and game.

Observed fields include:

- `id`
- `match_id`
- `match_type`
- `multiple_picks_allowed`
- `player_id`
- `position_id`
- `team_id`
- `type`

### `players`

Observed player fields include:

- `id`
- `first_name`
- `last_name`
- `sport_id`
- `team_id`
- `position_id`
- `position_name`
- `position_display_name`
- `jersey_number`
- `country`
- `image_url`
- `light_image_url`
- `dark_image_url`

### `games`

Observed game fields include:

- `id`
- `title`
- `short_title`
- `abbreviated_title`
- `home_team_id`
- `away_team_id`
- `scheduled_at`
- `sport_id`
- `season_type`
- `status`
- `period`
- `home_team_score`
- `away_team_score`
- `updated_at`

## Player Projection Availability

Underdog exposes active player projections through `over_under_lines`.

For a typical player projection:

1. `over_under_lines[].over_under.appearance_stat.appearance_id` identifies an appearance.
2. `appearances[].player_id` identifies the player.
3. `players[].id` provides name, sport, team, and position metadata.
4. `appearances[].match_id` joins to `games[].id` when the line is tied to a scheduled game.

Useful projection fields:

| Internal concept | Underdog source |
| --- | --- |
| Platform prop ID | `over_under_lines[].id` |
| Stable external key | `over_under_lines[].stable_id` |
| Group/market ID | `over_under_lines[].over_under_id` |
| Player ID | `appearances[].player_id` |
| Player name | `players[].first_name` + `players[].last_name` |
| Sport/league code | `players[].sport_id` or `games[].sport_id` |
| Prop display name | `over_under_lines[].over_under.appearance_stat.display_stat` |
| Prop stat key | `over_under_lines[].over_under.appearance_stat.stat` |
| Display title | `over_under_lines[].over_under.title` |
| Current line | `over_under_lines[].stat_value` |
| Status | `over_under_lines[].status` |
| Game start | `games[].scheduled_at` |
| Last updated | `over_under_lines[].updated_at` |

## League and Sport Coverage

Observed `players[].sport_id` coverage:

| Sport ID | Player count |
| --- | ---: |
| FIFA | 369 |
| CS | 215 |
| NFL | 128 |
| MLB | 119 |
| TENNIS | 69 |
| KBO | 42 |
| NHL | 38 |
| WNBA | 36 |
| ESPORTS | 24 |
| F1SZN | 22 |
| VAL | 20 |
| CFL | 20 |
| NBA | 17 |
| MMA | 16 |
| OUTLAWS | 13 |
| LOL | 10 |
| MOTORCYCLE | 9 |
| NPB | 8 |
| RAF | 6 |
| CWS | 4 |

Observed `games[].sport_id` coverage:

| Sport ID | Game count |
| --- | ---: |
| CS | 23 |
| FIFA | 23 |
| MLB | 6 |
| KBO | 5 |
| NPB | 5 |
| WNBA | 4 |
| ESPORTS | 3 |
| CWS | 2 |
| VAL | 2 |
| CFL | 2 |
| NBA | 1 |
| NHL | 1 |
| LOL | 1 |

Coverage changes with the active board. The adapter should store all observed sport IDs and defer analysis/optimizer eligibility to league configuration.

## Line Values and Payout Information

Observed line statuses:

| Status | Count |
| --- | ---: |
| `active` | 5,200 |

Observed line types:

| Line type | Count |
| --- | ---: |
| `balanced` | 4,001 |
| `alternate` | 1,199 |

Line value fields:

- `stat_value`: current selectable line value.
- `non_discounted_stat_value`: present for some discounted or promotional contexts; often null.

Pricing and payout fields are available at option level:

| Field | Observed availability |
| --- | ---: |
| `american_price` | 7,964 / 7,964 options |
| `decimal_price` | 7,964 / 7,964 options |
| `payout_multiplier` | 7,964 / 7,964 options |
| `raw_probability` | 0 / 7,964 options |

Sample observed option pricing:

| Choice | American | Decimal | Payout multiplier |
| --- | ---: | ---: | ---: |
| higher | +219 | 3.19 | 1.3 |
| lower | -278 | 1.36 | 0.67 |
| higher | +104 | 2.04 | 1.03 |
| lower | -127 | 1.79 | 0.88 |
| higher | -112 | 1.9 | 1.0 |
| lower | -112 | 1.9 | 1.0 |

Underdog exposes option-level prices and multipliers. Phase 1 should store these in `platform_props.metadata` until a dedicated payout/pricing model is added.

## Example Trimmed Record

```json
{
  "line": {
    "id": "5ac131bc-c604-436a-aeda-7d046e109b79",
    "stable_id": "253bd872-87c9-46cf-9323-c087f4bac74c|balanced",
    "over_under_id": "253bd872-87c9-46cf-9323-c087f4bac74c",
    "stat_value": "1.5",
    "status": "active",
    "line_type": "balanced",
    "provider_id": "manual",
    "updated_at": "2026-06-11T23:11:33Z",
    "title": "Patrik Schick Shots on Target O/U",
    "display_stat": "Shots on Target",
    "stat": "period_1_2_shots_on_target",
    "options": [
      {
        "choice": "higher",
        "american_price": "+219",
        "decimal_price": "3.19",
        "payout_multiplier": "1.3",
        "selection_subheader": "Higher 1.5 Shots on Target",
        "status": "active"
      },
      {
        "choice": "lower",
        "american_price": "-278",
        "decimal_price": "1.36",
        "payout_multiplier": "0.67",
        "selection_subheader": "Lower 1.5 Shots on Target",
        "status": "active"
      }
    ]
  },
  "player": {
    "id": "69032a0e-6b72-43bd-88ee-03af64222f14",
    "first_name": "Patrik",
    "last_name": "Schick",
    "sport_id": "FIFA",
    "position_name": "FW",
    "position_display_name": "Forward",
    "team_id": "601290ff-bf6f-46c8-ad7c-04f7c0233c85"
  },
  "game": {
    "id": 157081,
    "title": "KOR vs CZE",
    "scheduled_at": "2026-06-12T02:00:00Z",
    "sport_id": "FIFA",
    "status": "scheduled"
  }
}
```

## Feasibility Assessment

### Feasible for Phase 1

Underdog provides enough data to:

- Fetch active projections.
- Store raw payloads.
- Normalize players.
- Normalize sport/league codes.
- Create platform props.
- Track current line values.
- Track higher/lower availability.
- Preserve line snapshots.
- Store payout and price metadata for later EV work.

### Main Risks

- The endpoint is undocumented and can change without notice.
- The response is large, so polling too frequently will create network, memory, and database pressure.
- The payload does not guarantee both higher and lower choices for every line.
- League/sport IDs are platform-specific and need mapping.
- Some records may join through `solo_games` or non-game appearances instead of ordinary `games`.
- Terms and acceptable-use review is still required before production use.

## Recommended Implementation Approach

1. Create an `UnderdogAdapter` isolated from other provider logic.
2. Fetch only `GET /beta/v5/over_under_lines` for Phase 1.
3. Store the full raw payload and payload hash.
4. Normalize each `over_under_lines[]` record into a platform prop.
5. Join line to appearance, player, and game when IDs are present.
6. Store option-level price/payout data in metadata.
7. Treat missing lower/higher options as unavailable, not as parser errors.
8. Add schema drift validation for required fields:
   - `over_under_lines[].id`
   - `over_under_lines[].stat_value`
   - `over_under_lines[].status`
   - `over_under_lines[].over_under.appearance_stat`
   - `over_under_lines[].options`
9. Poll conservatively, no faster than every 3 to 5 minutes in normal mode.
10. Add backoff for `403`, `429`, `5xx`, and connection failures.

## Verdict

Underdog ingestion is feasible for local development and Phase 1 proof-of-concept work.

Production use should remain conditional on:

- Legal/terms review.
- Conservative polling.
- Adapter-level schema drift monitoring.
- Operational controls to disable Underdog ingestion independently.
