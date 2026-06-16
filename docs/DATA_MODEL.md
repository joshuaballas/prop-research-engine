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

## Results

### `prop_results`

Actual historical outcomes. MLB results are imported from MLB Stats API.

Important uniqueness:

- external source,
- external source identifier.

MLB prediction features read from this table by player/equivalent player, prop type/equivalent prop type, and event date.

### `mlb_result_import_states`

Overall MLB result importer state: last successful date/run and source-level progress.

### `mlb_result_import_date_states`

Date-level MLB import/backfill state. Used by the worker to detect missing dates and continue after restarts.

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

## Operations

### `application_errors`

Persistent error log for API and worker exceptions:

- severity,
- source,
- component,
- operation,
- request path/method/status,
- message,
- exception type,
- stack trace,
- correlation id,
- metadata JSON.

Client request cancellations should not be treated as real application failures.

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
