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
| GET | `/api/debug/underdog` | Fetch and normalize current Underdog lines for inspection. | No | Yes, Underdog | No |
| POST | `/api/debug/underdog/ingest` | Manual Underdog ingestion. | Yes | Yes, Underdog | No |
| GET | `/api/health/underdog-ingestion` | Underdog worker status. | No | No | No |

Underdog ingestion worker is enabled by default.

## PrizePicks Retired Endpoints

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/prizepicks` | Retired tombstone endpoint; returns Gone. | No | No | No |
| POST | `/api/debug/prizepicks/ingest` | Retired tombstone endpoint; returns Gone. | No | No | No |
| GET | `/api/health/prizepicks-ingestion` | Historical worker state endpoint. Worker is disabled/not registered. | No | No | No |

PrizePicks direct ingestion is disabled due to DataDome. Do not add bypass or scraping code.

## The Odds API MLB

The Odds API is a data provider/aggregator. Persist actual bookmakers as platforms, such as DraftKings, FanDuel, BetMGM, or Bovada. Store provider context in metadata as The Odds API.

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/the-odds/mlb/player-props-spike` | Read-only event/player-props spike. Without `eventId`, fetches MLB events. With `eventId`, fetches event odds. | No | Yes, The Odds API | Yes |
| POST | `/api/debug/the-odds/mlb/ingest-event` | Controlled one-event ingest. Supports `dryRun=true`. | Yes unless dry-run | Yes, The Odds API | Yes |
| POST | `/api/debug/the-odds/mlb/recommendations/refresh-current` | Explicit current-slate workflow: discovers today's MLB events using MLB/Eastern date logic, ingests configured The Odds markets, generates scoped predictions, and returns line-shopping strategy summaries. Supports `dryRun=true`, `date`, `includeCandidates`, `includeLineShopping`, `includeFullDiagnostics`, and `freshnessMinutes`. Live mode uses `TheOddsApi:MaxConcurrentRequests` for bounded parallel event odds ingestion and returns detailed timing diagnostics for event fetch, market fetch, persistence, prediction generation, line-shopping, classification, and response build. | Yes unless dry-run | Yes unless dry-run | Yes unless dry-run |
| GET | `/api/debug/the-odds/mlb/ingestion-impact` | Summarizes persisted The Odds rows and prediction impact. | No | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping` | Compares persisted The Odds sourced markets across bookmakers. Supports `runId`, `freshnessMinutes`, `summaryOnly=true`, `topOnly=true`, `take`, and `includeBookmakerPrices=false`. Each group includes strategy-aware classification fields such as `strategyClassification`, `strategyTier`, `isCandidate`, `isAvoid`, `isWatchlist`, `marketShape`, and `riskTags`, plus `strategyClassificationSummary`. `summaryOnly` returns summary/diagnostics only; `topOnly` returns a small active top-picks payload for the dashboard. | No | No | No |
| GET | `/api/debug/the-odds/mlb/top-picks` | Lightweight active Top Picks read for `/research`. Supports `runId`, `take`, and `freshnessMinutes`. Returns run metadata, timing diagnostics, strategy classification summary, and top active candidate rows only. Uses persisted rows only and omits nested bookmaker prices. | No | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping-backtest` | Backtests best-side line-shopping groups after results exist. Supports `runId`, `sampleTake`, `includeSamples`, `strategyOnly`, and ad hoc strategy filters such as `bookmaker`, `propType`, `trustLevel`, `line`, `minSampleSize`, `minBestProbability`, `maxBestProbability`, `minEstimatedEv`, `maxEstimatedEv`, `minBestPrice`, and `maxBestPrice`. Includes built-in strategy simulations, calibration breakdowns for model probability, implied probability, probability edge, sample size, price, line value, trust/prop combinations, and a model probability calibration table. Uses persisted rows only and resolves MLB result dates from provider metadata first, then an MLB Eastern-date fallback for late UTC starts. | No | No | No |
| POST | `/api/debug/the-odds/mlb/line-shopping-backtest/strategy-snapshot` | Computes current line-shopping strategy simulations for a supplied `runId` and persists one snapshot row per strategy. | Yes, strategy snapshots only | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping-backtest/strategy-snapshots` | Reads persisted strategy snapshots. Supports `runId`, `strategyName`, `fromDate`, and `toDate`. | No | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping-backtest/strategy-performance-summary` | Aggregates persisted strategy snapshots by strategy name across runs/slates. | No | No | No |
| GET | `/api/debug/the-odds/mlb/event-workflow-status` | Shows event workflow state from persisted rows. | No | No | No |
| GET | `/api/debug/the-odds/mlb/ingestion-plan` | Planner/dashboard helper using persisted DB state only. Supports `date`, `bookmakers`, `markets`, `maxEvents`. | No | No | No |
| GET | `/api/debug/the-odds/mlb/worker-status` | Disabled worker skeleton status and last dry-run fields. | No | No | No |
| POST | `/api/debug/the-odds/mlb/worker-dry-run` | Runs worker planning manually without enabling worker, API calls, or writes. | No | No | No |
| GET | `/api/debug/the-odds/mlb/bookmaker-coverage` | Compact persisted-row coverage report by bookmaker and prop type for the latest relevant run/slate. Shows configured bookmakers, observed bookmakers, missing configured event coverage, and candidate/watchlist/avoid contribution by best book. | No | No | No |
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
