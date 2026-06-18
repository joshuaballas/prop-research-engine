# API Endpoints

Base URL for local examples:

```text
http://localhost:5299
```

Most endpoints below are debug/admin endpoints. They are available by default in Development. Outside Development, `/api/debug/*` requires `DebugAdmin` to be enabled and may require `X-Debug-Admin-Key`.

Legend:

- Writes: creates or updates database rows.
- External API: calls a provider outside the app.
- Credits: spends The Odds API credits.

## Core Health And Dashboard

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/` | Redirects to `/health`. | No | No | No |
| GET | `/health` | ASP.NET health check. | No | No | No |
| GET | `/health/ready` | Database readiness check. | No | No | No |
| GET | `/research` | Internal research dashboard. | No | No on page load | No |

Example:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/health/ready"
```

## System Health And Errors

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/system/health` | Consolidated application, sportsbook, MLB import, and prediction health. | No | No | No |
| GET | `/api/debug/errors/recent` | Recent persisted application errors. Supports `take`, `source`, `component`, `severity`. | No | No | No |
| GET | `/api/debug/errors/summary` | Error counts and recent error timestamp. | No | No | No |
| GET | `/api/debug/errors/{id}` | Full persisted error detail by id. | No | No | No |

Example:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/system/health"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/errors/recent?take=5"
```

## Underdog

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/underdog` | Debug-only direct Underdog fetch for inspection. Not used by customer-facing market reads. | No | Yes, Underdog | No |
| POST | `/api/debug/underdog/ingest` | Debug-only manual direct Underdog ingestion. Disabled as an active provider source by default. | Yes | Yes, Underdog | No |
| GET | `/api/debug/underdog/source-coverage-audit` | Read-only persisted-row audit comparing direct Underdog coverage with The Odds API `us_dfs` Underdog rows. Returns overlap, unique rows, freshness, sport/prop/event breakdowns, match/failure samples, and a keep/remove/more-data recommendation. | No | No | No |
| GET | `/api/debug/underdog/ingestion-health` | Debug/audit direct Underdog worker health. Reports The Odds API as the active provider source, direct Underdog status, background worker enabled state, interval, and read-only/no-external-call status. | No | No | No |
| GET | `/api/health/underdog-ingestion` | Compatibility health route for the same direct Underdog worker status payload. | No | No | No |

Direct Underdog ingestion is disabled by default and should remain debug/audit only. Customer-facing market rows should come from persisted The Odds API `us_dfs` snapshots.

Example:

```powershell
Invoke-RestMethod "http://localhost:5299/api/debug/underdog/source-coverage-audit" |
  ConvertTo-Json -Depth 20
```

## DFS Provider Planning

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/dfs/provider-expansion-plan` | Lightweight DFS/pick'em provider status and expansion plan. Shows Underdog, PrizePicks, DraftKings Pick6, and Betr Picks through The Odds API `us_dfs`, with direct Underdog disabled as an active source. | No | No | No |
| GET | `/api/debug/dfs/entry-rules` | Read-only provider-scoped entry compatibility rules for PrizePicks, Underdog, DraftKings Pick6, Betr Picks, and sportsbook context providers. Rules are conservative and marked `NeedsVerification` where production details still need confirmation. | No | No | No |
| GET | `/api/dfs/entry-rules` | Production-safe provider-scoped DFS entry compatibility rules. Same persisted/read-only catalog as the debug route, without `/api/debug`. | No | No | No |

Example:

```powershell
Invoke-RestMethod "http://localhost:5299/api/debug/dfs/provider-expansion-plan" |
  ConvertTo-Json -Depth 10

Invoke-RestMethod "http://localhost:5299/api/debug/dfs/entry-rules" |
  ConvertTo-Json -Depth 10

Invoke-RestMethod "http://localhost:5299/api/dfs/entry-rules" |
  ConvertTo-Json -Depth 10
```

## PrizePicks Through The Odds API DFS

PrizePicks and Underdog are supported as The Odds API DFS bookmakers through region `us_dfs` with bookmaker keys `prizepicks` and `underdog`. DraftKings Pick6 and Betr Picks use `pick6` and `betr_us_dfs`. Direct provider scrapers should not feed customer-facing market rows.

## The Odds API MLB

The Odds API is a data provider/aggregator. Persist actual bookmakers as platforms, such as DraftKings, FanDuel, BetMGM, or Bovada. Store provider context in metadata as The Odds API.

## Production-Safe Market Read Endpoints

These routes are intended for the customer frontend/public surface. They never call The Odds API, never call direct Underdog, and never write rows. They read the latest persisted odds, prediction, line-shopping, top-pick, and refresh metadata populated by controlled backend refreshes.

Prefer `/api/markets/*` for frontend work. `/api/mlb/*` remains as compatibility wrappers around the same implementation with `sport=MLB`. Do not add separate duplicated endpoint families such as `/api/nba/*` or `/api/ufc/*` unless there is a specific reason; add new sports behind `/api/markets/*` with sport filters instead.

Current limitation: only MLB persisted market read models are implemented. When `sport` is omitted, `/api/markets/*` currently returns MLB data and includes `supportedSports=["MLB"]` plus a limitation note. When `sport=MLB`, behavior matches `/api/mlb/*`.

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/markets/freshness` | Preferred production-safe freshness metadata route. Supports optional `sport`, `freshnessMinutes`, `slateDate`, and `bookmakerPreset`. Returns supported/effective sport metadata, latest successful refresh, stale flags, `hasUsableMarketData`, snapshot counts, active/stale group counts, provider ingestion success/failure counts, provider rejected-key detection, usability warning, and threshold. `isFresh` means a successful refresh is recent; `hasUsableMarketData` says whether it produced usable market snapshots/data. | No | No | No |
| GET | `/api/markets/provider-status` | Production-safe provider/API-key/credit status from persisted refresh metadata. Returns The Odds API health status, latest refresh metadata, persisted credit usage fields when available, rejected-key detection, ingestion success/failure counts, usable-data flags, status reason, and recommended action. This endpoint does not call providers. | No | No | No |
| GET | `/api/markets/refresh-plan` | Production-safe refresh planner and credit budget estimator. By default it is read-only and uses persisted refresh metadata, provider status, snapshots, and configured planning budgets/cadence rules to estimate whether a scheduled The Odds refresh would run now or skip. Event start is not a global polling stop unless `TheOddsRefreshPlanner:StopAfterEventStart=true`; started events can still be refreshed for lifecycle tracking while remaining non-actionable for entries. If no persisted events are available and scheduled event discovery fallback is enabled, the plan uses `estimateSource=EventDiscoveryFallback` plus the configured fallback call estimate instead of skipping solely for missing slate events. Returns estimated slate events/API calls, estimate source, configured daily/monthly budget limits, daily/monthly budget usage, provider-reported usage/remaining credits, provider availability, cadence rule, skip reason, next eligible refresh, and recommended action. Optional `persistDecision=true` writes one manual planner decision audit row. | Optional, only with `persistDecision=true` | No | No |
| GET | `/api/markets/refresh-plan/history` | Production-safe read-only planner decision history. Supports `take`, `sport`, `skipped`, and `source`. Returns persisted manual/scheduled planner decisions, including skip reason, budget state, provider state, estimated calls, and recommended action. | No | No | No |
| GET | `/api/markets/top-picks` | Preferred production-safe persisted Top Picks route. Supports optional `sport`, `take`, `freshnessMinutes`, optional `runId`, optional `providerCategory`, and optional `bookmakerPreset` for freshness filtering. Returns `snapshotBacked=true` only when snapshot rows exist for the requested filters; otherwise falls back to dynamic persisted-row calculation with `snapshotBacked=false` and `fallbackReason`. Market rows include additive `playerId`/`canonicalPlayerId` when a unique canonical MLB player can be resolved. | No | No | No |
| GET | `/api/markets/line-shopping` | Preferred production-safe persisted line-shopping route. Supports optional `sport`, `runId`, `take`, `topOnly`, `freshnessMinutes`, `providerCategory`, and `bookmakerPreset`. Defaults to `topOnly=true`, `take=25`, and `includeBookmakerPrices=false`. Returns `snapshotBacked=true` only when snapshot rows exist for the requested filters/view; otherwise falls back dynamically with a clear reason. Market rows include additive `playerId`/`canonicalPlayerId` when a unique canonical MLB player can be resolved. | No | No | No |
| GET | `/api/markets/performance-summary` | Production-safe read-only strategy/provider performance summary from the durable `recommendation_snapshots` ledger and MLB `prop_results`. Supports `sport`, `providerCategory`, `provider`, `bookmaker`, `propType`, `strategyClassification`, `strategyTier`, `candidateOnly`, `fromDate`, `toDate`, `minSampleSize`, `gradingMode=latestCompleted|latestGraded|currentSlate|rollingWindow`, `includeDiagnostics`, and `diagnosticSampleSize`. `latestCompleted` uses only the latest imported result date and does not report current-slate pending rows; this is the Dashboard mode. `dataAvailability` exposes explicit completed-date counts such as `importedResultsForCompletedDate`, `ledgerRowsForCompletedDate`, `candidateLedgerRowsForCompletedDate`, `filteredRowsForCompletedDate`, `gradedRowsForCompletedDate`, `noResultRowsForCompletedDate`, `unmatchedRowsForCompletedDate`, `pendingRowsForCompletedDate`, `wonRowsForCompletedDate`, `lostRowsForCompletedDate`, and `pushedRowsForCompletedDate`. `includeDiagnostics=true` adds `diagnostics.noResultLedgerExamples` and `diagnostics.unmatchedLedgerExamples`, including captured ledger identity fields, same-date/nearby result counts, likely failure reason, and closest `prop_results` candidates. `NoResult` means provider lines existed, but the completed MLB import has no gradeable player/prop result row; this can happen when a line is stale, voided, inactive, or otherwise has no boxscore result. `Unmatched` is reserved for ambiguous identity, prop-type, or date matching. Hit rate uses wins / (wins + losses), excluding pushes. ROI uses graded outcomes, with pushes as 0-profit results; pending, no-result, and unmatched rows are excluded from hit-rate and ROI denominators. `currentSlate` is for pending/current slate diagnostics. If no ledger rows exist yet for non-Dashboard modes, legacy market snapshot diagnostics may still be used as fallback. The ledger is written during The Odds refresh from surfaced candidate/watchlist/top-pick line-shopping groups and grades by expected result date, canonical `players.id`, and prop type id, then evaluates the captured side/line against the actual result value. | No | No | No |
| GET | `/api/debug/recommendations/ledger/audit` | Debug-only read audit for recommendation history. Supports `fromDate` and `toDate`. Reports counts by date for The Odds refresh runs, prediction runs, market top-pick snapshots, market line-shopping snapshots, recommendation ledger rows, and `prop_results`. Does not call providers. | No | No | No |
| GET | `/api/debug/recommendations/ledger/result-coverage` | Debug-only read diagnostic for captured recommendation rows. Supports `date`, `sport=MLB`, optional `playerName`, optional `propType`, optional `take`, and optional `recommendationSnapshotId`. Compares captured ledger rows against persisted `prop_results` on the expected date and nearby dates, reports same-player/same-prop coverage counts, closest result candidates, and likely reasons such as `ProviderLineForInactivePlayer`, `PropTypeNotImportedForPlayer`, or `EventDateMismatch`. Raw MLB boxscore payloads are not persisted, so raw boxscore presence is reported as unknown. Does not call providers. | No | No | No |
| POST | `/api/debug/recommendations/ledger/backfill` | Debug-only ledger backfill from existing persisted market snapshots. Supports `fromDate`, `toDate`, `source=marketSnapshots|both`, `dryRun=true` by default, and `execute=true` to write idempotently. Reports eligible rows, existing rows, rows to create, and skipped reasons such as missing canonical player id, missing prop type id, or missing expected result date. Does not call providers. | Optional, only with `execute=true` and `dryRun!=true` | No | No |
| GET | `/api/markets/prop-history` | Production-safe read-only prop history for Details modals. Supports `sport=MLB`, production `playerId`, manual/debug `playerName` fallback, `propType` (`pitcher_strikeouts`, `batter_hits`, `batter_total_bases`, or display names), `side`, `line`, `take`, `season`, and `provider`. Uses persisted MLB `prop_results` through the same equivalent player/prop-type lookup pattern as prediction feature generation; no provider calls and no live MLB calls. Returns chart-ready games, summary stats, push-aware results, `emptyReason`, and diagnostics including lookup mode, resolved player id, history source, and observation counts. | No | No | No |
| GET | `/api/mlb/freshness` | Compatibility wrapper for `/api/markets/freshness?sport=MLB`. | No | No | No |
| GET | `/api/mlb/top-picks` | Compatibility wrapper for `/api/markets/top-picks?sport=MLB`. | No | No | No |
| GET | `/api/mlb/line-shopping` | Compatibility wrapper for `/api/markets/line-shopping?sport=MLB`. | No | No | No |

Examples:

```powershell
Invoke-RestMethod "http://localhost:5299/api/markets/freshness?freshnessMinutes=30" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/freshness?sport=MLB&freshnessMinutes=30" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/provider-status" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/refresh-plan" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/refresh-plan/history?take=20" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/top-picks?sport=MLB&take=10&freshnessMinutes=30" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/line-shopping?sport=MLB&take=10&freshnessMinutes=30&includeBookmakerPrices=false" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/performance-summary?sport=MLB&candidateOnly=true&gradingMode=latestCompleted" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/performance-summary?sport=MLB&candidateOnly=true&gradingMode=latestCompleted&includeDiagnostics=true&diagnosticSampleSize=10" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/markets/performance-summary?sport=MLB&candidateOnly=true&gradingMode=currentSlate" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "http://localhost:5299/api/debug/recommendations/ledger/audit?fromDate=2026-06-17&toDate=2026-06-18" |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Method Post "http://localhost:5299/api/debug/recommendations/ledger/backfill?fromDate=2026-06-17&toDate=2026-06-18&source=both&dryRun=true" |
  ConvertTo-Json -Depth 40

$top = Invoke-RestMethod "http://localhost:5299/api/markets/top-picks?sport=MLB&take=10&freshnessMinutes=30"
$row = $top.topPicks | Select-Object -First 1
$playerId = if ($row.playerId) { $row.playerId } else { $row.canonicalPlayerId }
Invoke-RestMethod "http://localhost:5299/api/markets/prop-history?sport=MLB&playerId=$playerId&propType=pitcher_strikeouts&side=Over&line=4.5&take=10" |
  ConvertTo-Json -Depth 20

# Manual/debug fallback only. Diagnostics will report lookupMode=playerNameFallback.
Invoke-RestMethod "http://localhost:5299/api/markets/prop-history?sport=MLB&playerName=Landen%20Roupp&propType=pitcher_strikeouts&side=Over&line=4.5&take=10" |
  ConvertTo-Json -Depth 20
```

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/the-odds/mlb/player-props-spike` | Read-only event/player-props spike. Without `eventId`, fetches MLB events. With `eventId`, fetches event odds. | No | Yes, The Odds API | Yes |
| POST | `/api/debug/the-odds/mlb/ingest-event` | Controlled one-event ingest. Supports `dryRun=true`. | Yes unless dry-run | Yes, The Odds API | Yes |
| POST | `/api/debug/the-odds/mlb/recommendations/refresh-current` | Explicit current-slate workflow: discovers today's MLB events using MLB/Eastern date logic, ingests configured The Odds markets, generates scoped predictions, and returns line-shopping strategy summaries. Supports `dryRun=true`, `forceLive=true`, `date`, `bookmakerPreset=default|expanded|dfsOnly|sportsbookOnly|all`, `includeCandidates`, `includeLineShopping`, `includeFullDiagnostics`, `includeDfs=true`, and `freshnessMinutes`. Live mode requires `forceLive=true`; otherwise the request is blocked before any provider call. Live mode uses `TheOddsApi:MaxConcurrentRequests` for bounded parallel event odds ingestion and returns detailed timing diagnostics for event fetch, market fetch, persistence, prediction generation, line-shopping, classification, and response build. With DFS enabled, it also requests configured DFS books through region `us_dfs`; `bookmakerPreset=dfsOnly` scopes the generated prediction and line-shopping output to DFS rows only. Dry-run remains no-call/no-write and reports the effective sportsbook/DFS books and estimated API-call shape. | Yes unless dry-run | Yes unless dry-run | Yes unless dry-run |
| GET | `/api/debug/the-odds/mlb/refresh-freshness` | Reads persisted The Odds MLB refresh run metadata. Supports `slateDate`, `bookmakerPreset`, and `freshnessMinutes`. Returns latest run, latest successful/failed run, freshness flags, stale reason, and latest refresh counts. | No | No | No |
| GET | `/api/debug/the-odds/mlb/ingestion-impact` | Summarizes persisted The Odds rows and prediction impact. | No | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping` | Compares persisted The Odds sourced markets across bookmakers. Supports `runId`, `freshnessMinutes`, `summaryOnly=true`, `topOnly=true`, `take`, and `includeBookmakerPrices=false`. Each group includes provider fields (`providerCategory`, `region`, `isActionableForUser`, `isIndicativeOdds`), entry compatibility fields (`entryCompatibilityGroup`, `entryProvider`, `entryProviderCategory`, `entryActionableForUser`, `supportedEntryTypes`, `allowedLegCounts`, `entryRuleWarning`), plus strategy-aware classification fields such as `strategyClassification`, `strategyTier`, `isCandidate`, `isAvoid`, `isWatchlist`, `marketShape`, and `riskTags`, plus `strategyClassificationSummary`. Candidate tiers require positive `bestEstimatedEv`; conservative-shape rows with non-positive EV are downgraded and tagged `NonPositiveEv`. `summaryOnly` returns summary/diagnostics only; `topOnly` returns a small active top-picks payload for the dashboard. | No | No | No |
| GET | `/api/debug/the-odds/mlb/top-picks` | Lightweight active Top Picks read for `/research`. Supports `runId`, `take`, and `freshnessMinutes`. Returns run metadata, timing diagnostics, strategy classification summary, and top active candidate rows only. Uses persisted rows only and omits nested bookmaker prices. Top pick rows include provider-scoped entry compatibility metadata so future builders do not mix incompatible providers. | No | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping-backtest` | Backtests best-side line-shopping groups after results exist. Supports `runId`, `sampleTake`, `includeSamples`, `strategyOnly`, and ad hoc strategy filters such as `bookmaker`, `propType`, `trustLevel`, `line`, `minSampleSize`, `minBestProbability`, `maxBestProbability`, `minEstimatedEv`, `maxEstimatedEv`, `minBestPrice`, and `maxBestPrice`. Includes built-in strategy simulations, calibration breakdowns for model probability, implied probability, probability edge, sample size, price, line value, trust/prop combinations, and a model probability calibration table. Uses persisted rows only and resolves MLB result dates from provider metadata first, then an MLB Eastern-date fallback for late UTC starts. | No | No | No |
| POST | `/api/debug/the-odds/mlb/line-shopping-backtest/strategy-snapshot` | Computes current line-shopping strategy simulations for a supplied `runId` and persists one snapshot row per strategy. | Yes, strategy snapshots only | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping-backtest/strategy-snapshots` | Reads persisted strategy snapshots. Supports `runId`, `strategyName`, `fromDate`, and `toDate`. | No | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping-backtest/strategy-performance-summary` | Aggregates persisted strategy snapshots by strategy name across runs/slates. | No | No | No |
| GET | `/api/debug/the-odds/mlb/event-workflow-status` | Shows event workflow state from persisted rows. | No | No | No |
| GET | `/api/debug/the-odds/mlb/ingestion-plan` | Planner/dashboard helper using persisted DB state only. Supports `date`, `bookmakers`, `markets`, `maxEvents`. | No | No | No |
| GET | `/api/debug/the-odds/mlb/worker-status` | Disabled worker skeleton status and last dry-run fields. | No | No | No |
| POST | `/api/debug/the-odds/mlb/worker-dry-run` | Runs worker planning manually without enabling worker, API calls, or writes. | No | No | No |
| GET | `/api/debug/the-odds/mlb/scheduled-refresh/status` | Read-only status for the scheduled The Odds MLB refresh worker. Reports config, planner interval, latest successful refresh timestamp, last planner decision, skip/block reason, last run state, worker heartbeat estimate, `nextRefreshDueUtc`, `nextDataAvailableEstimateUtc`, and safety flags. Base config keeps scheduled live execution off; Development/local config can explicitly enable the MLB DFS-only worker at the 15-minute minimum cadence. | No | No | No |
| GET | `/api/debug/the-odds/mlb/bookmaker-coverage` | Compact persisted-row coverage report by bookmaker and prop type for the latest relevant run/slate. Shows configured sportsbook books, configured DFS books, observed sportsbook/DFS books, provider-category breakdown, best-book distribution, average books per comparison group, missing configured event coverage, and candidate/watchlist/avoid contribution by best book. | No | No | No |
| GET | `/api/debug/the-odds/mlb/bookmaker-coverage-audit` | Audits persisted bookmaker coverage. Supports `eventId`, `runId`. | No | No | No |
| GET | `/api/debug/the-odds/mlb/bookmaker-prediction-dropout` | Explains why a bookmaker's persisted props did not produce predictions. Supports `bookmaker`, `eventId`, `runId`, `ignoreRunContext`. | No | No | No |
| GET | `/api/debug/the-odds/mlb/prediction-readiness-audit` | Full readiness pipeline for persisted The Odds MLB props. Supports `eventId`, `bookmaker`, `runId`. | No | No | No |
| GET | `/api/debug/the-odds/mlb/player-mapping-recovery-preview` | Previews recovery using existing mapped DB identities. | No | No | No |
| GET | `/api/debug/the-odds/mlb/player-mapping-candidates` | Looks up MLB Stats mapping candidates for unmapped The Odds players. | No | Yes, MLB Stats API | No |
| GET | `/api/debug/the-odds/mlb/player-mapping-candidates/bulk` | Bulk MLB Stats mapping candidates for unmapped The Odds players. | No | Yes, MLB Stats API | No |
| POST | `/api/debug/the-odds/mlb/player-mapping-approve` | Manually approves a source player to MLB Stats id mapping. | Yes | No | No |
| GET | `/api/debug/the-odds/mlb/player-alias-link-preview` | Previews safe alias links to existing MLB-mapped canonical players. | No | No | No |
| POST | `/api/debug/the-odds/mlb/player-alias-link` | Creates an alias bridge from source player to existing MLB-mapped canonical player. Dry-run by default. | Yes unless dry-run | No | No |
| POST | `/api/debug/the-odds/mlb/player-alias-link/auto-safe` | Auto-links only safe alias candidates. Dry-run by default. | Yes unless dry-run | No | No |
| GET | `/api/debug/the-odds/mlb/historical-feature-gap-audit` | Explains missing historical features for The Odds props. | No | No | No |
| GET | `/api/debug/provider-lines/lifecycles` | Lists tracked provider line lifecycle rows. Supports `sport`, `providerKey`, `providerCategory`, `status`, `propType`, `player`, `slateDate`, `eventId`, and `take`. | No | No | No |
| GET | `/api/debug/provider-lines/lifecycle-summary` | Aggregates lifecycle status, lifetime, movement, pull, provider, prop-type, and sport counts. Supports `sport`, `providerKey`, `providerCategory`, `propType`, `fromDate`, and `toDate`. | No | No | No |
| GET | `/api/debug/provider-lines/lifecycle-events` | Reads lifecycle event history. Supports `lifecycleId`, `sport`, `providerKey`, `eventType`, `fromUtc`, `toUtc`, and `take`. | No | No | No |
| GET | `/api/debug/provider-lines/refresh-cadence-insights` | Uses lifecycle movement/pull timing to suggest a refresh cadence for the requested sport/provider/prop filters. Supports `sport`, `providerKey`, `propType`, `fromDate`, and `toDate`. | No | No | No |

Examples:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/ingestion-plan"

Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/player-props-spike"

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/ingest-event?dryRun=true" `
  -ContentType "application/json" `
  -Body '{"eventId":"EVENT_ID","bookmakers":"draftkings,betmgm","markets":"batter_hits,batter_total_bases,pitcher_strikeouts"}'

$refresh = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true"

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/line-shopping"

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/bookmaker-coverage"

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/lifecycle-summary?sport=MLB" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/lifecycles?sport=MLB&take=25" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/lifecycle-events?sport=MLB&take=25" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/refresh-cadence-insights?sport=MLB" |
  ConvertTo-Json -Depth 20
```

## MLB Results

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/results` | Lists persisted prop results. | No | No | No |
| GET | `/api/debug/results/{id}` | Gets one persisted prop result. | No | No | No |
| GET | `/api/debug/mlb-results` | Read-only MLB Stats API result provider output. | No | Yes, MLB Stats API | No |
| POST | `/api/debug/results/import/mlb` | Imports MLB results for one date into `prop_results`. | Yes | Yes, MLB Stats API | No |
| GET | `/api/debug/results/import/mlb/status` | Import status for a date and worker state/status shape. | No | No | No |
| POST | `/api/debug/results/import/mlb-range` | Date-range MLB result backfill. | Yes | Yes, MLB Stats API | No |
| GET | `/api/debug/results/import/mlb/coverage` | Historical MLB result coverage. | No | No | No |
| GET | `/api/debug/mlb/pitcher-strikeouts/import-coverage` | Pitcher strikeout import coverage diagnostic. | No | No | No |
| POST | `/api/debug/mlb/pitcher-strikeouts/backfill` | Safe repair/backfill for Pitcher Strikeouts. Dry-run by default. | Yes unless dry-run | MLB Stats API through import logic | No |

Examples:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/results/import/mlb" -ContentType "application/json" -Body '{"date":"2026-06-12"}'
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/results/import/mlb/coverage"
```

## Predictions, Features, Probabilities, Backtests

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/features/player` | Calculates features for one player/prop/date. | No | No | No |
| GET | `/api/debug/prediction-runs` | Lists prediction runs. | No | No | No |
| GET | `/api/debug/predictions` | Lists predictions. | No | No | No |
| GET | `/api/debug/predictions/probabilities` | Read-only probability prototype for existing predictions. | No | No | No |
| POST | `/api/debug/predictions/sample` | Creates a sample prediction run and fake predictions. | Yes | No | No |
| POST | `/api/debug/predictions/generate/mlb` | Full MLB prediction generation. | Yes | No | No |
| POST | `/api/debug/predictions/generate/mlb/the-odds` | Scoped The Odds MLB prediction generation. Supports filters such as `eventId`, `bookmakers`, `markets`. | Yes | No | No |
| GET | `/api/debug/predictions/run/{runId}` | Prediction run details. | No | No | No |
| GET | `/api/debug/predictions/run/{runId}/summary` | Run summary by recommendation, prop type, edge. | No | No | No |
| GET | `/api/debug/predictions/run/{runId}/skips` | Skip counts for a run. | No | No | No |
| GET | `/api/debug/predictions/backtest` | MLB prediction recommendation backtest. | No | No | No |
| GET | `/api/debug/predictions/mlb-quality-analysis` | Consolidated MLB prediction quality analysis. | No | No | No |
| GET | `/api/debug/predictions/edge-analysis` | Edge bucket, confidence, prop-type, threshold analysis. | No | No | No |
| GET | `/api/debug/predictions/no-historical-results` | Groups no-history skipped props by player. | No | No | No |
| GET | `/api/debug/predictions/player-history/{playerId}` | Historical feature/history diagnostic for one player. | No | No | No |
| GET | `/api/debug/predictions/skip-analysis` | NoHistoricalResults prop-type skip analysis. | No | No | No |
| GET | `/api/debug/predictions/no-history-analysis` | Remaining no-history reason classification using feature lookup rules. | No | No | No |
| GET | `/api/debug/predictions/prop-type-mapping-analysis` | Sportsbook prop type to historical result type analysis. | No | No | No |
| GET | `/api/debug/predictions/coverage-gaps` | Coverage gaps by player/prop-type alias/duplicate identity. | No | No | No |
| GET | `/api/debug/predictions/trace` | One prop's historical lookup trace. | No | No | No |
| GET | `/api/debug/predictions/alias-recovery-analysis` | Alias-based historical lookup recovery analysis. | No | No | No |
| GET | `/api/debug/predictions/alias-resolution-sample` | Equivalent player ids for a sample player. | No | No | No |
| GET | `/api/debug/predictions/prop-type-id-conflicts` | Duplicate prop type id impact and canonical equivalence. | No | No | No |

Examples:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/predictions/generate/mlb/the-odds?bookmakers=draftkings,fanduel,betmgm&markets=batter_hits,batter_total_bases,pitcher_strikeouts"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/predictions/edge-analysis"
```

## Market Analysis

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/market-comparison` | Current market comparison diagnostics. | No | No | No |
| GET | `/api/debug/market-comparison/diagnostics` | Market matching diagnostics. | No | No | No |
| GET | `/api/debug/line-history` | Stored prop line snapshot history. | No | No | No |
| GET | `/api/debug/markets/classification-analysis` | Market class and line-quality audit. | No | No | No |
| GET | `/api/debug/predictions/market-rankings` | Ranked market view with visibility, eligibility, lifecycle, warnings. | No | No | No |
| GET | `/api/debug/markets/freshness-summary` | Platform freshness and visible-market composition. | No | No | No |
| GET | `/api/debug/markets/underdog-payout-analysis` | Underdog metadata and payout semantics audit. | No | No | No |
| GET | `/api/debug/markets/underdog-ev-analysis` | Read-only Underdog EV validation. | No | No | No |
| GET | `/api/debug/markets/underdog-ev-backtest` | Read-only Underdog EV backtest. | No | No | No |
| GET | `/api/debug/markets/underdog-ev-loss-analysis` | Positive-EV Underdog loss analysis. | No | No | No |
| GET | `/api/debug/markets/underdog-prop-ev-reliability` | Prop-type EV reliability audit. | No | No | No |

## Player Identity And Mapping

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/player-mappings` | Lists external refs. | No | No | No |
| GET | `/api/debug/player-mappings/{playerId}` | External refs for one canonical player. | No | No | No |
| GET | `/api/debug/player-identity` | Identity diagnostic by name. | No | No | No |
| GET | `/api/debug/player-identity-summary` | Duplicate player summary. | No | No | No |
| GET | `/api/debug/player-identity-candidates` | Duplicate identity groups. | No | No | No |
| GET | `/api/debug/player-identity-merge-preview` | Dry-run merge impact preview. | No | No | No |
| GET | `/api/debug/player-merge-plan` | Safe/manual merge plan. | No | No | No |
| GET | `/api/debug/player-merge-conflicts` | Merge conflict summary. | No | No | No |
| POST | `/api/debug/player-merge-safe` | Executes only safe merge groups. | Yes | No | No |
| GET | `/api/debug/player-merge-safe/status` | Safe merge execution status. | No | No | No |
| GET | `/api/debug/player-resolution-stats` | In-memory player resolution counters. | No | No | No |
| GET | `/api/debug/player-resolution-created` | Rolling list of players created through resolution service. | No | No | No |
| GET | `/api/debug/player-resolution-created-summary` | Created-player summary by provider/league. | No | No | No |
| GET | `/api/debug/mlb-player-candidates` | MLB Stats mapping candidates for canonical MLB players. | No | MLB Stats API | No |
| GET | `/api/debug/mlb-player-candidates/summary` | MLB candidate confidence summary. | No | MLB Stats API | No |
| GET | `/api/debug/mlb-player-candidates/high-confidence` | High-confidence MLB candidates. | No | MLB Stats API | No |
| GET | `/api/debug/mlb-player-candidates/skipped` | Skipped MLB candidate diagnostics. | No | MLB Stats API | No |
| GET | `/api/debug/mlb-player-candidates/skipped-summary` | Skipped candidate summary. | No | MLB Stats API | No |
| POST | `/api/debug/mlb-player-mappings/approve` | Manual MLB player mapping approval. | Yes | No | No |
| POST | `/api/debug/mlb-player-mappings/auto-approve` | Auto-approve safe MLB candidates. | Yes | No | No |
| GET | `/api/debug/mlb-player-mappings` | MLB mapping coverage. | No | No | No |
| GET | `/api/debug/mlb/mapping-coverage` | MLB mapping recovery coverage. | No | No | No |
| GET | `/api/debug/mlb-player-mappings/unmapped-analysis` | Reasons unmapped players were not auto-approved. | No | No | No |
| GET | `/api/debug/player-identity-conflict-analysis` | Duplicate identity conflict analysis. | No | No | No |
| GET | `/api/debug/player-identity-conflict-analysis/reasons` | Detailed merge blocker reasons. | No | No | No |
| GET | `/api/debug/mlb-reconciliation-health` | Current MLB reconciliation health. | No | No | No |

## Notes On Output Files

When saving endpoint output, write to `tmp-analysis`, not the project root:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/line-shopping" |
  ConvertTo-Json -Depth 20 |
  Set-Content ..\tmp-analysis\line-shopping.json
```
