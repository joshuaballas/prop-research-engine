# Architecture

SportsBetting is an ASP.NET Core API with a PostgreSQL persistence layer and a static internal research dashboard. The app is currently MLB-first, but the platform/provider model is designed to support additional sports and books later.

## Runtime Shape

```text
Browser
  -> /research static dashboard
  -> /api/debug/* internal/admin endpoints

ASP.NET Core API
  -> EF Core / PostgreSQL
  -> background workers
  -> provider HTTP clients

External providers
  -> Underdog Fantasy
  -> The Odds API
  -> MLB Stats API
```

## Backend

The backend lives under `api/`.

Important folders:

- `Endpoints`: minimal API endpoint registration.
- `Data`: EF Core `ApplicationDbContext`.
- `Models`: entity model.
- `Integrations`: provider-specific clients, adapters, ingestion services, result import services, workers.
- `Ingestion`: shared ingestion/player resolution worker infrastructure.
- `Predictions`: feature generation, baseline prediction generation, probability prototype, alias lookup.
- `Diagnostics`: debug/admin protection and persistent error logging support.
- `wwwroot/research`: static internal research dashboard.

## Database

PostgreSQL is accessed through EF Core. Migrations are under `api/Migrations`.

Important table families:

- identity: `players`, `player_external_refs`, `player_merge_audits`
- market storage: `platforms`, `props`, `platform_props`, `prop_line_snapshots`, `raw_ingestion_payloads`
- result storage: `prop_results`, `mlb_result_import_states`, `mlb_result_import_date_states`
- prediction storage: `prediction_runs`, `predictions`, `prediction_run_skip_counts`
- operations: `application_errors`, `ingestion_runs`

See [DATA_MODEL.md](DATA_MODEL.md) for details.

## Dashboard

The internal dashboard is served from:

```text
/research
```

The dashboard is static HTML/CSS/JavaScript. It calls existing API endpoints and does not run prediction, ingestion, or provider logic directly.

Dashboard sections include:

- System health and recent errors.
- The Odds MLB event workflow and ingestion planner.
- The Odds API manual ingestion controls.
- MLB line shopping.
- Market rankings.
- Freshness summary.
- Underdog EV research.
- Prop EV reliability.

The dashboard must not call The Odds API on page load. Buttons that call The Odds API should be explicit and labeled because they spend credits.

## Debug/Admin Protection

`DebugAdminProtectionMiddleware` protects:

- `/research`
- `/api/debug/*`

In Development, behavior stays open for local work. Outside Development:

- if `DebugAdmin.Enabled=false`, protected routes return 404;
- if `DebugAdmin.Enabled=true` and `RequireApiKey=true`, requests must include `X-Debug-Admin-Key`;
- the key must not be logged or returned.

Normal public/future API routes are not meant to be protected by this middleware unless they use `/api/debug/*`.

## Provider Integrations

### Underdog

Underdog is a direct source and currently has an enabled ingestion worker. It fetches over/under lines, resolves players and prop types, persists platform props, and stores line snapshots.

### The Odds API

The Odds API is a provider/aggregator, not a sportsbook. The system persists actual bookmaker/platform names from the payload, such as DraftKings or BetMGM, and stores The Odds API provider context in metadata.

The current integration is manual/debug-first:

- fetch events/player props,
- dry-run or commit one selected event,
- generate scoped predictions,
- line-shop persisted rows,
- run backtests later after results import.

The worker skeleton is disabled by default and dry-run-first. It should not poll The Odds API automatically until explicitly enabled and hardened.

### MLB Stats API

MLB Stats API supplies finalized box score/player stats. The app maps those stats into `prop_results` for supported MLB prop types. The MLB result import worker keeps completed game results current and backfills missing dates.

### PrizePicks

PrizePicks support is provided only through The Odds API DFS region `us_dfs` with bookmaker key `prizepicks`. There is no direct PrizePicks ingestion, scraping, worker, or health endpoint.
## Result Import Pipeline

MLB result import flow:

1. Determine date(s) needing import.
2. Fetch completed MLB games and box scores from MLB Stats API.
3. Convert supported stats into result records.
4. Resolve players through `player_external_refs` and alias support.
5. Upsert into `prop_results`.
6. Track import state and date-level progress.

Supported imported MLB stat concepts include hits, runs, RBIs, home runs, total bases, stolen bases, batter strikeouts, pitcher strikeouts, batter walks, doubles, singles, hits+runs+RBIs, walks allowed, hits allowed, earned runs allowed, and pitching outs.

## Prediction Pipeline

Prediction generation:

1. Select candidate MLB props.
2. Use `PredictionFeatureService` to load historical `prop_results`.
3. Resolve equivalent player ids through `IPlayerIdentityAliasService`.
4. Use canonical prop type equivalence where needed.
5. Calculate baseline predicted value.
6. Compare predicted value to sportsbook line.
7. Store prediction, confidence, sportsbook line, edge, recommendation, and skip counts.

The current model is a baseline average model, not machine learning.

## Probability Pipeline

`PredictionProbabilityService` is read-only/prototype-oriented. It derives probability over/under/push, confidence score, ranking score, sample size, and method used for existing predictions. Probability output powers research endpoints and dashboard ranking context.

## Line-Shopping Pipeline

Line-shopping for The Odds API uses persisted DB rows only:

1. Load a prediction run.
2. Filter The Odds API-sourced platform props.
3. Group rows by event, player, prop type, and line.
4. Compare bookmaker prices.
5. Calculate implied probabilities and estimated EV from decimal prices.
6. Choose best side/book/price per group.
7. Add lifecycle state: Active, Stale, GameStarted, AwaitingResults, Graded.

The dashboard should hide expired/game-started rows from the default actionable view while keeping diagnostics available.

## Backtest Pipeline

Backtests join predictions or line-shopping groups to completed `prop_results`. They classify outcomes as Win, Loss, or Push and simulate profit using stored odds where available.

Backtests are read-only. They do not persist grading or ROI yet.

## Worker Architecture

Hosted workers:

- `UnderdogIngestionWorker`: enabled by default.
- `MlbResultImportWorker`: enabled by default.
- `TheOddsMlbIngestionWorker`: registered but disabled by default, dry-run-first skeleton.

Retired:

- There is no direct PrizePicks worker.

Workers should log caught exceptions to `application_errors` and continue or fail according to their existing behavior.

## Error Logging

Unhandled API exceptions pass through global middleware and are persisted to `application_errors` with a correlation id. Normal client request cancellations are not treated as application failures. Background workers also log caught exceptions.

The dashboard shows summaries but does not expose stack traces by default.
