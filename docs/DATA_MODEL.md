# Data Model

This document explains the important tables and concepts. It is intentionally conceptual; use EF migrations and `ApplicationDbContext` for exact column definitions.

## Core Reference Data

### `sports`

Top-level sport records, such as baseball.

### `leagues`

League records scoped to a sport, such as MLB. Players, teams, props, games, and results reference leagues.

### `teams`

Team records scoped to leagues. Players can optionally point to a team.

### `prop_types`

Canonical stat/market types for a sport. Examples:

- Hits
- Total Bases
- Pitcher Strikeouts
- Home Runs

The app has had duplicate prop type ids for the same logical name in the past, so prediction lookup uses canonical/equivalent prop type logic in some paths.

## Player Identity

### `players`

Canonical-ish player rows used by sportsbook props and historical results. The app historically created duplicate player rows from different sources. Newer ingestion uses shared resolution and alias logic to avoid creating more duplicates.

Important idea: a `players` row can be a sportsbook-created source player row or an MLB Stats-backed canonical row. The app treats equivalent rows as aliases at read time instead of blindly merging.

### `player_external_refs`

Stable provider identity refs:

- `player_id`
- `provider`
- `provider_player_id`
- `provider_display_name`

Examples:

- MLB Stats id for MLB result/player identity.
- The Odds/source aliases when bridging source player rows to existing MLB-mapped canonical players.
- Historical provider refs from earlier experiments may exist in old databases, but only MLB/The Odds/Underdog identity paths are part of the active app.

Unique key: provider plus provider player id.

### Alias Bridge Concept

When The Odds API creates a source player row like `Yandy Diaz` and an existing canonical player has MLB Stats identity `Yandy Díaz`, the app can create a safe alias/external-ref bridge. This lets feature lookup include historical results from the existing MLB-mapped player without merging or deleting rows.

Rules:

- Do not create conflicting MLB refs.
- Do not merge ambiguous players automatically.
- Max Muncy-style ambiguous names require manual review.

## Market Storage

### `platforms`

Sportsbook/platform rows. For The Odds API, this must be the actual bookmaker, not a generic "The Odds API" platform.

Examples:

- Underdog Fantasy
- DraftKings
- BetMGM
- FanDuel

### `props`

Normalized internal prop entity:

- player,
- league,
- game when available,
- prop type,
- event date,
- normalized key.

Predictions point to `props`.

### `platform_props`

Source/book-specific market rows tied to internal `props`.

Important fields include:

- platform,
- source platform prop id,
- current line/status/availability,
- observed timestamp,
- payout type,
- metadata JSON,
- raw payload link.

The Odds API metadata stores provider context such as event id, bookmaker key/title, market key, market last update, teams, commence time, and side prices.

### `prop_line_snapshots`

Historical snapshots of platform prop line/price/status/availability over time. This supports line lifecycle, freshness, and future backtesting against what was observed at a point in time.

### `raw_ingestion_payloads`

Captured source payloads for ingestion runs. Useful for audit/debug, not a substitute for normalized platform props and snapshots.

### `ingestion_runs`

Tracks source ingestion runs and links raw payloads to platform/provider runs.

### `the_odds_refresh_runs`

Durable metadata for live The Odds MLB refresh attempts after they pass the `forceLive=true` gate. Dry-runs and blocked live-shaped calls do not write rows.

Important fields include:

- provider and sport,
- slate date,
- started/completed timestamps,
- succeeded/failed/blocked flags,
- bookmaker preset and requested sportsbook/DFS books,
- requested markets,
- estimated and actual API-call counts,
- platform prop, prediction, line-shopping, and active candidate/watchlist/avoid counts,
- error message,
- diagnostics JSON.

This table powers refresh/freshness status for production-style read-only frontend endpoints.

### `market_top_pick_snapshots`

Stable Top Picks output captured after a successful live The Odds MLB refresh. Each row links to:

- `the_odds_refresh_run_id`,
- optional `prediction_run_id`,
- sport and slate date,
- generated timestamp,
- bookmaker preset,
- provider/category/bookmaker,
- player, event, prop type, line, side, odds/probability/EV fields,
- strategy classification/tier and candidate/watchlist/avoid flags,
- provider-scoped entry metadata,
- details JSON for the full row payload.

Dry-runs and blocked live-shaped refresh calls do not write these rows. Failed refreshes should not expose partial snapshots.

### `market_line_shopping_snapshots`

Stable line-shopping comparison output captured after a successful live The Odds MLB refresh. This lets production read endpoints show the same computed market set to every user for a refresh run instead of recomputing from changing underlying rows on each request.

The public `/api/markets/top-picks` and `/api/markets/line-shopping` routes prefer the latest successful snapshots when available. If no snapshot exists yet, they fall back to the dynamic persisted-row read path and return `snapshotBacked=false`.

### `provider_line_lifecycles`

Current lifecycle state for provider/book lines observed in successful The Odds refresh snapshots. A lifecycle identity intentionally does not include line value or price, so a move from `1.5` to `2.0` updates the same lifecycle row and writes a movement event.

The lookup identity is based on stable provider-line context:

- sport,
- provider key and category,
- event id when available,
- normalized player name until canonical/provider player ids are persisted in snapshots,
- market/prop type,
- side/direction.

Important status values:

- `Active`: seen in the latest successful relevant refresh.
- `Stale`: not confirmed recently, but not proven unavailable.
- `Pulled`: missing from successful provider refreshes enough times to treat as removed.
- `Locked`: event start has passed.
- `Expired`: event/slate is old enough to stop treating it as current.
- `ProviderUnavailable`: reserved for cases where provider availability prevents verification; provider failure must not be treated as line pull.

### `provider_line_lifecycle_events`

Append-only lifecycle event history for line availability and movement. Events include `FirstSeen`, `LineChanged`, `PriceChanged`, `LineAndPriceChanged`, `MissingFromRefresh`, `ProviderPulled`, `EventLocked`, `MarkedStale`, `MarkedExpired`, `ProviderUnavailable`, and `Reactivated`.

Lifecycle events are inserted as append-only history and should not be updated by refresh tracking. The refresh tracker treats the logical event identity as lifecycle id, refresh run id, event type, previous/new status, and previous/new line. If an equivalent event already exists for the refresh, it is skipped as idempotent rather than attached or updated. Defensive save cleanup detaches any tracked historical lifecycle event that appears as `Modified` or `Deleted` before `SaveChanges`.

The event table is intentionally debug/admin oriented. Public market endpoints should expose only compact lifecycle fields when needed, not the event firehose.

## Results

### `prop_results`

Actual historical outcomes. MLB results are imported from MLB Stats API.

Important uniqueness:

- external source,
- external source identifier.

MLB prediction features read from this table by player/equivalent player, prop type/equivalent prop type, and event date.

Performance grading treats `prop_results` as provider-independent stat outcomes. A captured recommendation is matched to a result by sport/result date, canonical player id, and prop type id or normalized prop/stat type. Provider, side, and line are not part of result lookup because those describe the offered market, not the final player stat. Once a single stat result is found, side and line determine Win/Loss/Push and diagnostics use `MatchedAndGraded`. If the result date is not imported, the row is `Pending`. If the player/date exists but the prop/stat is missing, the row is `NoResult`. If the same player/prop exists only on a nearby imported date, diagnostics use `EventDateMismatch`. If the date/prop exists but the player has no result by id or normalized name, diagnostics use `ProviderLineForInactivePlayer`. If more than one result row matches the same player/date/prop, the row is `Unmatched` until the duplicate result rows are resolved.

`Pitcher Strikeouts` and `Total Bases` are critical MLB grading props and should normalize consistently across The Odds provider lines, predictions, recommendation snapshots, prop-history reads, and imported results.

### `mlb_result_import_states`

Overall MLB result importer state: last successful date/run and source-level progress.

### `mlb_result_import_date_states`

Date-level MLB import/backfill state. Used by the worker to detect missing dates and continue after restarts. It also stores result coverage status so partial imports are not treated as complete slates:

- `completed_games_expected` / `completed_games_imported`: reserved for game-level coverage when available.
- `imported_results`, `distinct_players`, `distinct_prop_types`: persisted fallback coverage counts.
- `coverage_status`: `NotStarted`, `PendingGames`, `Partial`, `Complete`, or `Failed`.
- `coverage_reason`: human-readable explanation of the coverage decision.
- `latest_import_attempt_utc`, `latest_successful_repair_utc`, `last_error_message`: operational metadata for manual/worker repair.

Performance summary skips dates whose coverage status is not `Complete` when coverage state rows exist.

## Predictions

### `prediction_runs`

One execution of a model, such as:

- `BaselineAverageModel` version `1.0`
- scoped The Odds runs with notes identifying filters.

### `predictions`

One prediction for one prop in a run.

Important fields:

- predicted value,
- predicted probability where used,
- sportsbook line,
- edge,
- recommendation,
- confidence,
- created time.

Recommendations are currently research outputs, not final product advice.

### `prediction_run_skip_counts`

Aggregated skip reasons for a prediction run, such as missing historical results, unsupported prop type, no sportsbook line, or insufficient history.

### `recommendation_snapshots`

Durable captured-pick ledger used by Performance. Rows are created from surfaced The Odds candidate/watchlist/top-pick opportunities and retain the state needed to grade the pick later:

- expected result date,
- canonical player id when safely resolved,
- display and normalized player name,
- prop type id, display prop type, and normalized prop type,
- side and line,
- provider/bookmaker context,
- hit chance, implied probability, edge, EV, trust, warning, and historical sample size,
- candidate/top-pick/watchlist flags,
- actionability and lifecycle status,
- grade status/result fields for diagnostics,
- performance exclusion audit fields: `is_excluded_from_performance`, `performance_exclusion_reason`, `performance_excluded_at_utc`, and `performance_excluded_by`.

The idempotency key includes provider, side, and line because those identify the captured opportunity. Grading does not use provider, side, or line to find the result row. It first finds the final stat result from `prop_results`; then side and line grade that stat as Win/Loss/Push. Public Performance has two layers of filtering: common query filters such as sport/date/provider/prop, then performance eligibility. Default performance eligibility includes actionable `IsCandidate` rows, while `topPickOnly=true` uses `IsTopPick`. Watchlist-only, research-only, stale, locked, expired, non-playable, and performance-excluded rows remain in the ledger for diagnostics but are excluded from public Performance unless the corresponding debug include switch is set. Performance eligibility also requires gradeability metadata: canonical player id, supported MLB result prop type, event id, event start time, and expected result date. Public Performance groups only expose determined outcomes by default: Win/Loss/Push. `NoResult`, `Unmatched`, and `Pending` are not losses and must be excluded from customer group breakdowns, hit rate, and ROI.

Performance baseline resets must not delete predictions, snapshots, or results. They mark polluted captured-pick rows with the performance exclusion fields so public Performance can start from a clean baseline while debug endpoints still show the excluded evidence and reason. The exclusion fields are audit metadata only; they do not alter `prop_results` or erase historical provider captures.

Performance diagnostics expose inclusion fields and exclusion buckets derived from `IsCandidate`, `IsTopPick`, `IsWatchlist`, `IsActionableForUser`, `LifecycleStatus`, stale warning text, canonical player identity, prop type support, event/date mapping, and performance exclusion state. Result-coverage rows report whether the row is research, stale, locked, expired, playable, excluded from performance, included in default/candidate/top-pick performance, and the exact exclusion reasons.

No-result reason buckets include `NoPropResultsForDate`, `MissingCanonicalPlayerId`, `MissingLedgerPropType`, `UnsupportedPropType`, `MissingEventDateMapping`, `NoResultForPlayerPropOnDate`, `NoResultForPlayerOnDate`, `ProviderLineForInactivePlayer`, `EventDateMismatch`, `MultipleResultRowsForPlayerPropDate`, and `Unknown`. Unknown should be visible and should trend toward zero as importer coverage and capture quality improve.

Expected result dates should be the actual MLB game date. Because late MLB games can start after midnight UTC while still belonging to the previous Eastern evening, diagnostics preserve `eventStartTimeUtc`, the UTC event date, and the Eastern event date to make date-boundary mismatches visible.

## Operations

### `application_errors`

Persistent error log for API and worker exceptions:

- severity,
- source,
- component,
- operation,
- request path/method/status,
- user agent and remote IP hash when available,
- message,
- exception type,
- stack trace,
- correlation id,
- optional run id,
- sport/provider context,
- sanitized metadata JSON,
- created timestamp.

The logger caps large strings and metadata before persistence. Metadata keys that look like API keys, tokens, secrets, passwords, authorization headers, connection strings, or credentials are redacted. Client request cancellations should not be treated as real application failures.

### `player_merge_audits`

Audit records for executed safe canonical player merges. Historical merge tooling exists, but current The Odds alias recovery generally prefers safe alias links rather than broad merges.

## Research/Legacy Tables

The model also contains research-oriented tables such as:

- `prop_analysis`
- `line_movements`
- `correlation_pairs`
- `correlation_rules`
- `optimized_entries`
- `optimized_entry_legs`

These are not the center of the current The Odds MLB line-shopping workflow.
