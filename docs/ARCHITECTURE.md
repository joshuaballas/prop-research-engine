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
  -> The Odds API
  -> Underdog Fantasy
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

Underdog is supported as a DFS provider through The Odds API `us_dfs` feed. Direct Underdog ingestion remains in the codebase for explicit debug/audit checks only and is disabled by default.

The production strategy is provider-feed-first: The Odds API or another paid/allowed provider feed should be the source of truth for customer-facing provider rows.

Keep direct Underdog disabled unless an explicit debug/audit comparison is being run. It must not feed public `/api/markets/*` read models.

### The Odds API

The Odds API is a provider/aggregator, not a sportsbook. The system persists actual bookmaker/platform names from the payload, such as DraftKings or BetMGM, and stores The Odds API provider context in metadata.

The Odds API is the desired production source of truth for odds and DFS pick'em provider rows. It can provide sportsbook market context and DFS rows through region `us_dfs`, including PrizePicks, Underdog, DraftKings Pick6, and Betr Picks when configured.

The current integration is still manual/debug-first:

- fetch events/player props,
- dry-run or commit one selected event,
- generate scoped predictions,
- line-shop persisted rows,
- run backtests later after results import.

The worker skeleton is disabled by default and dry-run-first. It should not poll The Odds API automatically until explicitly enabled and hardened.

Long-term, The Odds API calls should run through a scheduled backend worker/admin process on a controlled cadence based on lifecycle evidence, credit budget, target availability, and freshness needs. Development defaults keep scheduled refresh disabled unless explicitly enabled, with a 15-minute minimum cadence. Frontend users should never trigger provider refreshes. Event/game start is not a global refresh stop by default; it affects user actionability while lifecycle tracking may continue to refresh and observe provider state.

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

## Performance Ledger And Grading

Performance uses `recommendation_snapshots` as the durable captured-pick ledger. The ledger is written during successful The Odds refreshes from surfaced candidate/watchlist/top-pick line-shopping opportunities. `market_top_pick_snapshots` and `market_line_shopping_snapshots` remain current read models; they are not the primary historical grading ledger when recommendation snapshots exist.

Grading is provider-independent and public performance summary regrades captured rows at read time from `recommendation_snapshots` plus imported `prop_results`; persisted historical `GradeStatus=NoResult` values are diagnostic evidence, not authoritative public scoring when newer result rows can be matched. A captured recommendation matches `prop_results` by expected result date, player identity, and prop type id or normalized prop/stat type. The preferred player identity match is canonical `players.id`; if that id diverges but exactly one same-date/same-stat result row matches the captured normalized player name, grading may use that normalized-name fallback. Provider, side, and line are not used to locate the result row. After exactly one result row is found, side and line grade the outcome as Win/Loss/Push and diagnostics report whether it matched by exact player id/date/stat or unique normalized player-name/date/stat fallback. Missing imported dates are `Pending`; same player/prop rows on a nearby imported date are `EventDateMismatch`; same-date player rows with no matching prop/stat are `NoResultForPlayerPropOnDate`; date/prop rows with no matching player by id or normalized name are `ProviderLineForInactivePlayer`; duplicate/ambiguous matches are `Unmatched`. Public Performance groups only show determined outcomes by default: Win/Loss/Push. `NoResult`, `Unmatched`, and `Pending` remain diagnostic/admin concepts, are excluded from customer group breakdowns, hit rate, and simulated ROI, and zero gradeable picks returns unavailable/null metrics rather than fake poor performance. The public performance summary and the no-result audit use the same reason-code precedence so one captured row does not receive different explanations across endpoints.

Default public Performance is intentionally limited to actionable candidate rows; `topPickOnly=true` switches the primary inclusion flag to top picks. Watchlist, research/inventory, stale, locked, expired, and non-playable provider rows are preserved for diagnostics but excluded from default candidate/top-pick performance unless explicitly included by query parameters. If diagnostic include switches are supplied without an explicit `candidateOnly`, the endpoint broadens out of candidate-only mode so watchlist/research inventory can be inspected. A row must also pass a gradeability precheck before it can enter performance math: canonical player id, supported MLB result prop type, event id, event start time, expected result date, and an Eastern MLB event date aligned with the expected result date are required. This prevents stale/provider-inactive or structurally ungradeable rows from being counted as failed recommendations while still allowing audit endpoints and opt-in performance-summary calls to explain the excluded inventory. The Odds ledger capture applies the same conservative gate before promoting future rows as Candidate or Top Pick; suspicious rows can still be kept for research/watchlist diagnostics without contaminating default pick performance.

MLB expected result dates use the actual MLB game date, not the UTC capture date. Late games can begin after midnight UTC while still being the previous evening in Eastern time, so diagnostics expose UTC and Eastern event-date comparisons when investigating `EventDateMismatch`.

Debug audit endpoints:

- `/api/debug/recommendations/pipeline-audit`: date/prop rollup across `prop_results`, predictions, provider snapshots, recommendation snapshots, gradeability, reason codes, and model buckets.
- `/api/debug/recommendations/ledger/result-coverage`: row-level captured-pick diagnostics, including normalized identity/prop keys, result coverage counts, matched result ids/value, final grade status, reason code, and persisted historical-result feature context.
- `/api/debug/performance/result-match-audit`: row-by-row performance/result matching audit for one sport/date. It reports the exact canonical-id matcher outcome, normalized-name fallback outcome, result coverage checks, player mapping status, and closest `prop_results` candidates so player id, date, and stat-key failures can be proven before changing grading.
- `/api/debug/recommendations/ledger/no-result-audit`: focused rollup for performance-eligible non-graded rows, grouped by prop/provider/reason with row-level result coverage counts, nearby player/prop matches, UTC/Eastern date diagnostics, and same-season local `prop_results` counts for inactive-player investigation.

## Worker Architecture

Hosted workers:

- `UnderdogIngestionWorker`: registered for debug/audit visibility but disabled by default.
- `MlbResultImportWorker`: enabled by default.
- `TheOddsMlbIngestionWorker`: registered but disabled by default, dry-run-first skeleton.
- `TheOddsMlbScheduledRefreshWorker`: registered but disabled by default. It is the foundation for production scheduled The Odds refreshes and exposes read-only status at `/api/debug/the-odds/mlb/scheduled-refresh/status`. It will not call The Odds API unless explicitly enabled and live execution is unblocked in local/ops config.

Retired:

- There is no direct PrizePicks worker.

Workers should log caught exceptions to `application_errors` and continue or fail according to their existing behavior.

## Production Data Delivery Direction

The production architecture should be provider-feed-first, not scraper-first. The Odds API or another paid/allowed feed should provide production odds and DFS pick'em rows. Custom direct scrapers do not scale across PrizePicks, Pick6, Betr, Sleeper, ParlayPlay, and other DFS apps, and they can break when platforms change payloads or add anti-bot protections.

The production architecture should use scheduled backend refreshes plus read-only frontend endpoints. Frontend users should not trigger The Odds API provider refreshes. Provider refreshes spend credits and should not happen per user, page load, filter change, or button click in a public product UI.

Target flow:

1. A scheduled backend worker or admin process refreshes The Odds API data on a controlled cadence based on credit budget, enabled targets, and observed line lifecycle behavior. Development defaults use DFS-only MLB and a 15-minute minimum cadence when explicitly enabled.
2. The worker uses configurable interval, bookmaker/provider preset, markets, concurrency, and credit caps.
3. The worker fetches provider odds once per interval, persists normalized platform props and snapshots, generates scoped predictions, builds line-shopping/top-pick outputs, and records freshness metadata.
4. All users read the same latest persisted odds, predictions, line-shopping groups, and top picks.
5. Frontend product routes call only read-only internal endpoints.
6. UI displays `lastRefreshedAt`, line freshness, stale-data warnings, and game-started/expired lifecycle state.
7. DFS Add Leg can be disabled for locked/started events even when provider data is still being refreshed.

The existing `refresh-current` debug/admin endpoint remains useful for manual controlled refreshes and requires `forceLive=true` for live provider calls. It is not the target frontend data path.

The scheduled refresh worker foundation stays disabled in base config, defaults to `BookmakerPreset=dfsOnly`, clamps cadence to at least 15 minutes, and is guarded by `TheOddsScheduledRefresh:RequireForceLive=true`. Development/local config may enable the MLB DFS-only worker with explicit scheduled live execution at the 15-minute cadence. `TheOddsRefreshPlanner:EnforcementEnabled=false` by default, so daily/monthly local credit caps, provider remaining-credit estimates, and per-run estimate caps are diagnostics/warnings only. The Odds quota headers and actual provider errors are the source of truth; the planner reports `plannerWouldBlockIfEnforced` and `plannerWarnings` when estimates look risky. Exact budget block categories (`ProviderCreditsExceeded`, `PerRunEstimateExceeded`, or `LocalBudgetExceeded`) are active only when planner enforcement is enabled and the corresponding explicit block flag is enabled. Provider rejected keys, provider unavailability, and recent quota/rate-limit failure backoff remain separate provider/auth safety gates. Provider credit telemetry includes age; stale telemetry beyond `TheOddsRefreshPlanner:ProviderTelemetryStaleAfterMinutes` is treated as unknown so an old `requestsRemaining` value cannot stop scheduled refreshes. When enabled, the worker sleeps until the planner's next eligible time, then runs a guarded planning cycle so overdue refreshes are not stuck waiting for the first timer tick. Successful live refreshes anchor the next normal run to completion plus the scheduled interval; a failed latest live attempt after the last success switches to `TheOddsRefreshPlanner:RetryAfterFailureMinutes=1` until a success resets the normal cadence. If no persisted slate events are available, `TheOddsScheduledRefresh:AllowEventDiscoveryWhenNoPersistedEvents=true` lets the planner use the configured fallback call estimate and let the shared refresh service discover events from The Odds. `TheOddsScheduledRefresh:MaxEstimatedCallsPerRun=0` means no per-run estimate cap; a positive value blocks only with `EnforcementEnabled=true` and `BlockOnPerRunEstimate=true`. Scheduled status separates worker heartbeat timing (`nextWakeEstimateUtc`) from frontend data-read timing (`nextDataAvailableEstimateUtc`, planner due time plus `PostRefreshReadBufferSeconds`) and exposes the budget explanation fields used by the worker. `TheOddsRefreshPlanner:StopAfterEventStart=false` by default so provider polling can continue after first pitch; rows/events can still be marked locked/non-actionable through lifecycle/actionability fields. If enabled without explicit live unblocking, it logs a warning and marks the cycle blocked. The debug/admin `refresh-current` endpoint and scheduled worker share `ITheOddsMlbRefreshService`, so scheduled execution reuses the same ingestion, prediction, line-shopping, classification, and diagnostics path rather than duplicating logic.

Live The Odds refresh persistence is protected by a process-local concurrency gate. Scheduled refresh cycles and debug/admin live refresh paths serialize their writes so market snapshots, provider line lifecycle rows, recommendation ledger rows, and refresh run status are not persisted concurrently within the same API process. Dry-run and blocked non-live paths are not affected. Provider line lifecycle tracking is best-effort after snapshot and recommendation ledger persistence: it uses no-tracking source reads, retries once on EF concurrency conflicts, logs the failing lifecycle entity details, and reports lifecycle diagnostics on the refresh run without turning an otherwise successful market refresh into a failed refresh. Lifecycle event history is append-only and idempotent: equivalent events for the same lifecycle row, refresh run, event type, previous/new status, and previous/new line are skipped rather than updated, and any tracked historical event found as `Modified` or `Deleted` before save is detached. If a refresh fails after side-effect entities have been tracked, the failure handler detaches pending side-effect entities and saves only the current `TheOddsRefreshRun` failure state, preventing stale tracked snapshot/lifecycle/ledger entities from being retried during failure recording. This is process-local protection only: if multiple API processes point at the same database with scheduled workers enabled, duplicate workers can still race until a database-level lock is added. Local development should avoid running multiple API instances with scheduled workers enabled against the same database.

The Underdog source coverage audit is retained for explicit comparisons against persisted The Odds API `us_dfs` Underdog rows. Direct Underdog should not run continuously or supply customer-facing market data.

## Error Logging

Unhandled API exceptions pass through global middleware and are persisted to `application_errors` with a correlation id. Normal client request cancellations are not treated as application failures. Background workers also log caught exceptions.

The dashboard shows summaries but does not expose stack traces by default.
