# Workflows

This document describes operational workflows for local development and MLB research.

## Start The Local API

```powershell
cd D:\Documents\Programs\Web\SportsBetting\api
Copy-Item .\appsettings.Local.example.json .\appsettings.Local.json
notepad .\appsettings.Local.json
dotnet run --project .\SportsBetting.Api.csproj --urls "http://localhost:5299"
```

Open:

```text
http://localhost:5299/research
```

Check health:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/health/ready"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/system/health"
```

## The Odds API Manual Event Ingest

Use the dashboard section "The Odds API Manual Ingestion" or call endpoints manually.

Important: these calls spend The Odds API credits when they hit The Odds API.

1. Fetch MLB events:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/player-props-spike"
```

2. Dry-run a selected event:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/ingest-event?dryRun=true" `
  -ContentType "application/json" `
  -Body '{"eventId":"EVENT_ID","bookmakers":"draftkings,betmgm","markets":"batter_hits,batter_total_bases,pitcher_strikeouts"}'
```

3. Commit only after reviewing dry-run output:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/ingest-event" `
  -ContentType "application/json" `
  -Body '{"eventId":"EVENT_ID","bookmakers":"draftkings,betmgm","markets":"batter_hits,batter_total_bases,pitcher_strikeouts"}'
```

## Refresh Current MLB Recommendations

Use this when you want the dashboard to produce active strategy-aware candidates for the current MLB slate. The dry-run form does not call The Odds API and does not write rows. Live refresh spends The Odds API credits, persists/refreshes The Odds market rows, generates a scoped prediction run, and returns line-shopping strategy summaries for that run. Live refresh requires `forceLive=true`; calls without it are blocked before any provider request.

This is a debug/admin workflow. A production frontend should not call this endpoint. Production users should read latest persisted odds, predictions, line-shopping groups, top picks, and freshness metadata from read-only endpoints populated by a scheduled backend refresh process.

The base configuration keeps the scheduler off by default, while local Development may enable the DFS-only scheduled worker at the 15-minute minimum cadence. Public/frontend users must not trigger provider refreshes.

Production-style frontend reads should use the non-debug persisted-data endpoints. Prefer `/api/markets/*`; `/api/mlb/*` remains as an MLB compatibility wrapper.

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/freshness?freshnessMinutes=30"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/provider-status"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/refresh-plan"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/top-picks?sport=MLB&take=10&freshnessMinutes=30"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/line-shopping?sport=MLB&take=25&freshnessMinutes=30&includeBookmakerPrices=false"
Invoke-RestMethod -Uri "http://localhost:5299/api/dfs/entry-rules"
```

These endpoints are read-only, use persisted database rows only, and do not call The Odds API or Underdog. The generic market routes currently support MLB only and report `supportedSports=["MLB"]`; add future sports behind `/api/markets/*` instead of duplicating endpoint trees.

After a successful live The Odds refresh, the app stores stable Top Picks and line-shopping snapshots. Public market endpoints prefer those snapshots and return `snapshotBacked=true` only when snapshot rows exist for the requested filters/view. Before usable snapshots exist, the endpoints fall back to the existing dynamic persisted-row read path and return `snapshotBacked=false` with a clear `fallbackReason`.

Freshness separates recency from usability: `isFresh=true` means a successful refresh happened within the threshold, while `hasUsableMarketData=false` means that refresh did not produce usable market snapshots/data. The freshness response includes snapshot counts, active/stale group counts, provider ingestion success/failure counts, provider rejected-key detection, and a `usabilityWarning`.

Provider line lifecycle tracking runs only after successful snapshot persistence. It tracks first seen, last seen, movement, missing refreshes, pulled status, stale status, event lock, and expiration for provider lines. A provider/API failure is not the same as a pulled line: failed refreshes and rejected keys should not mark every previously seen line as pulled. `Pulled` means the line went missing from successful provider refreshes; `Stale` means it has not been confirmed recently; `Locked` means event start has passed.

Use lifecycle diagnostics before tightening refresh cadence or upgrading The Odds API plan:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/lifecycle-summary?sport=MLB" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/refresh-cadence-insights?sport=MLB" |
  ConvertTo-Json -Depth 20
```

Cadence insight is observational. If only a small percent of lines change or get pulled within 10-15 minutes, keep 15-30 minute polling outside near-lock windows. If a provider/prop type changes quickly, lower cadence for that slice instead of globally.

Provider status is exposed separately at `/api/markets/provider-status`. It reads persisted refresh metadata only and surfaces The Odds API key/credit/provider availability, including rejected-key detection, low/exhausted credit warnings, latest refresh usability, and a recommended action. Use it in frontend health banners before implying current market data is actionable.

Refresh planning is exposed at `/api/markets/refresh-plan`. It is also read-only and uses persisted provider status, snapshots, refresh metadata, and `TheOddsRefreshPlanner` budget/cadence config to explain whether a scheduled refresh would run now or skip. It estimates calls and credit-budget impact, but it never calls The Odds API and never enables the scheduled worker.

The scheduled The Odds refresh worker is planner-gated. Base `appsettings.json` keeps the worker disabled, uses `IntervalMinutes=15`, `BookmakerPreset=dfsOnly`, and keeps `AllowScheduledLiveRefresh=false` so production-like defaults do not burn credits unexpectedly. Local Development config enables the existing MLB DFS-only worker with `AllowScheduledLiveRefresh=true` at the same 15-minute minimum cadence. When enabled, the worker runs one guarded startup planning cycle immediately, then wakes every effective interval. Each cycle first reads the same planner decision, and the worker skips without provider calls when the planner says `shouldRefresh=false`, when `budgetAllowed=false`, when provider status is unavailable/rejected-key, or when scheduled live refresh is not explicitly allowed. Missing persisted slate events are not a hard stop when `TheOddsScheduledRefresh:AllowEventDiscoveryWhenNoPersistedEvents=true`; the planner uses `FallbackEstimatedApiCalls` for budget checks and lets the refresh service discover events from The Odds. `TheOddsScheduledRefresh:PostRefreshReadBufferSeconds=30` is added to the latest successful refresh completion time plus the scheduled interval when reporting the next frontend data-availability estimate. Game/event start is not a global refresh stop by default: `TheOddsRefreshPlanner:StopAfterEventStart=false` means provider polling can continue after games start so lifecycle tracking can capture movement, pulled lines, stale rows, and provider availability.

Refreshability and actionability are separate. Refreshability controls whether the backend calls The Odds API and persists the latest provider state. Actionability controls whether a user can add a line to a DFS entry. A started event can still be refreshed for lifecycle tracking while its DFS Add Leg action is disabled or locked in the frontend. Provider/API failures must not mark previously seen rows pulled; only successful refreshes that no longer include a line should advance missing/pulled lifecycle state.

Planner decisions can be persisted for audit. The scheduled worker writes a planner decision row each time it runs a planning cycle. Manual reads remain read-only unless `/api/markets/refresh-plan?persistDecision=true` is explicitly used. Read history with `/api/markets/refresh-plan/history?take=20`.

Scheduled status is available at `/api/debug/the-odds/mlb/scheduled-refresh/status`. It reports whether the worker is registered/running, the last wake, next wake estimate, `nextRefreshDueUtc`, `nextDataAvailableEstimateUtc`, the latest refresh run ids, actual API calls from the latest run, `currentPlan.estimateSource` (`PersistedEvents` or `EventDiscoveryFallback`), and `executionBlockedReason` when config prevents scheduled live execution. `nextWakeEstimateUtc` is the worker heartbeat/check time; `nextDataAvailableEstimateUtc` is the frontend countdown target. The status endpoint itself remains read-only and never calls The Odds API.

Use the production-safe performance summary to inspect provider, prop-type, and strategy slices before showing recommendations confidently:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-summary?sport=MLB&candidateOnly=true"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-summary?sport=MLB&providerCategory=DFS&candidateOnly=true"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-summary?sport=MLB&propType=Total%20Bases&candidateOnly=true"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-summary?sport=MLB&candidateOnly=true&gradingMode=latestCompleted&includeDiagnostics=true&diagnosticSampleSize=10"
```

The summary is read-only and now prefers the durable `recommendation_snapshots` ledger plus `prop_results`. The ledger is written during The Odds refresh from surfaced candidate/watchlist/top-pick line-shopping groups, while `market_*_snapshots` remain current read models for Top Picks and Line Shopping. Ledger grading uses expected result date, canonical `players.id`, prop type id, side, and line. If no ledger rows exist yet for non-Dashboard modes, legacy snapshot diagnostics may still be used as fallback. Customer Dashboard performance should use `gradingMode=latestCompleted`, which uses only the latest imported result date and suppresses current-slate pending counts; use `currentSlate` only when you explicitly need pending/current slate diagnostics.

Audit/backfill recommendation history without provider calls:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/recommendations/ledger/audit?fromDate=2026-06-17&toDate=2026-06-18" |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/recommendations/ledger/backfill?fromDate=2026-06-17&toDate=2026-06-18&source=both&dryRun=true" |
  ConvertTo-Json -Depth 40
```

The ledger idempotency key is one row per unique opportunity per result date/provider/line/side: sport, expected result date, event id, canonical player id, normalized prop type, side, line, provider/bookmaker, strategy classification, and candidate flag. Repeated sightings update latest model metrics, `LastSeenAtUtc`, and `SeenCount` while preserving `FirstSeenAtUtc`.

The current refresh endpoint supports profiling options:

```powershell
$r = Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?forceLive=true&includeCandidates=true&includeLineShopping=true&includeFullDiagnostics=true&freshnessMinutes=30"
$r.diagnostics | ConvertTo-Json -Depth 8
```

`TheOddsApi:MaxConcurrentRequests` controls bounded parallel event-odds ingestion for live refreshes. Keep this conservative; the default is `3`.

Bookmaker coverage is preset-driven:

- `bookmakerPreset=default`: configured conservative sportsbook books, plus DFS only when `includeDfs=true` or local config enables it.
- `bookmakerPreset=expanded`: expanded US sportsbook list, plus DFS only when `includeDfs=true` or local config enables it.
- `bookmakerPreset=dfsOnly`: only DFS books through region `us_dfs`; the resulting prediction run and line-shopping output are scoped to DFS rows only.
- `bookmakerPreset=sportsbookOnly`: only configured conservative sportsbook books.
- `bookmakerPreset=all`: expanded US sportsbooks plus all supported DFS books.

Expanded/All can spend more The Odds API credits and increase refresh time. Use dry-run first.

Dry run:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true"
```

Preset dry-runs:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true&bookmakerPreset=dfsOnly" |
  Select-Object -ExpandProperty diagnostics |
  ConvertTo-Json -Depth 8

Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true&bookmakerPreset=all" |
  Select-Object -ExpandProperty diagnostics |
  ConvertTo-Json -Depth 8
```

Live refresh:

```powershell
$refresh = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?forceLive=true&includeCandidates=true&freshnessMinutes=30"

$refresh.predictionRunId
$refresh.strategyClassificationSummary
$refresh.topActiveCandidates | Select-Object -First 10
$refresh | ConvertTo-Json -Depth 20 | Set-Content ".\tmp-analysis\current-mlb-recommendations-refresh.json"
```

Inspect lifecycle rows after a successful live refresh:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/lifecycles?sport=MLB&take=25" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/lifecycle-events?sport=MLB&take=25" |
  ConvertTo-Json -Depth 20
```

Check persisted refresh freshness without provider calls:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/refresh-freshness?freshnessMinutes=30" |
  ConvertTo-Json -Depth 20
```

Only live refresh attempts that pass the `forceLive=true` gate write `the_odds_refresh_runs` rows. Dry-runs and blocked live-shaped calls remain no-write.

DFS/pick'em refresh through The Odds API:

```powershell
$dryRun = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true&includeDfs=true"

$dryRun.diagnostics | ConvertTo-Json -Depth 8
```

```powershell
$refresh = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?forceLive=true&bookmakerPreset=dfsOnly&includeCandidates=true&includeLineShopping=true&includeFullDiagnostics=true&freshnessMinutes=30"

$refresh.diagnostics | Select-Object `
  dfsBookmakersRequested,
  dfsApiCallCount,
  dfsPlatformPropsCreated,
  dfsPlatformPropsUpdated,
  dfsPlatformPropsUnchanged,
  dfsFetchMs,
  dfsPersistMs

$refresh.topActiveCandidates |
  Where-Object { $_.providerCategory -eq "DFS" -and $_.isActionableForUser } |
  Select-Object -First 10

$refresh | ConvertTo-Json -Depth 20 | Set-Content ".\tmp-analysis\current-mlb-dfs-refresh.json"
```

PrizePicks DFS support comes from The Odds API region `us_dfs` with bookmaker key `prizepicks`. DFS odds are indicative and can vary based on user selections.

## Compare Direct Underdog Versus The Odds Underdog DFS

Direct Underdog ingestion is disabled by default and should be used only for explicit debug/audit comparisons. Customer-facing Underdog rows should come from persisted The Odds API `us_dfs` snapshots.
Use `GET /api/debug/underdog/ingestion-health` for the direct Underdog worker/source status. `GET /api/health/underdog-ingestion` remains as a compatibility health route with the same read-only payload.

Use the read-only coverage audit before making any removal decision:

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/debug/underdog/source-coverage-audit" |
  ConvertTo-Json -Depth 20
```

The audit uses persisted DB rows only. It compares normalized player, sport/league, prop type, line, side availability, and event/date context, then returns overlap, unique rows, freshness, breakdowns, match samples, failed-match samples, and a recommendation.

Important: the audit uses persisted rows only and does not call providers. It should not be treated as an active ingestion path or a reason to re-enable continuous direct Underdog polling.

## Expand The Odds Bookmaker Coverage Safely

The bookmaker list is config-driven through `TheOddsIngestionWorker:Bookmakers`. Current committed defaults are intentionally narrow:

```json
"Bookmakers": "draftkings,fanduel,betmgm"
```

Expanded US sportsbook preset is also config-driven but not used unless you request `bookmakerPreset=expanded` or `bookmakerPreset=all`:

```json
"ExpandedBookmakers": "draftkings,fanduel,betmgm,espnbet,betrivers,fanatics,williamhill_us,betonlineag,bovada,lowvig,mybookieag"
```

DFS books are configured separately and are opt-in by default:

```json
"IncludeDfsBookmakers": false,
"DfsRegion": "us_dfs",
"DfsBookmakers": "prizepicks,underdog,pick6,betr_us_dfs"
```

To test broader coverage, override this value only in ignored local config (`api/appsettings.Local.json`) or environment variables. Add one or two keys at a time, then dry-run the current refresh before live refresh. Candidate opt-in keys depend on The Odds API plan/provider availability; common US keys to verify manually include `bovada`, `espnbet`, `williamhill_us`, and `betrivers`.

Do not build direct scrapers for additional DFS providers. The production path is The Odds API or another paid/allowed provider feed, not custom scraping for each app.

Coverage diagnostic uses persisted rows only and does not spend credits:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/bookmaker-coverage" |
  ConvertTo-Json -Depth 20 |
  Set-Content ".\tmp-analysis\the-odds-bookmaker-coverage.json"
```

Dry-run current refresh after config changes:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true"
```

## Generate Predictions

For The Odds API workflow, prefer scoped generation:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/predictions/generate/mlb/the-odds?eventId=EVENT_ID&bookmakers=draftkings,betmgm&markets=batter_hits,batter_total_bases,pitcher_strikeouts"
```

For full MLB generation:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/predictions/generate/mlb"
```

Full generation is slower and includes more stale/legacy rows.

## Run Line-Shopping

Line-shopping uses persisted DB rows only and does not call The Odds API:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/line-shopping"
```

With a specific run:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/line-shopping?runId=RUN_ID"
```

The dashboard includes presets:

- Best Research
- Conservative Candidates
- All Eligible
- Strong Only
- Multi-book EV
- Avoid / Diagnostics
- Research Only / Diagnostics
- Reset

The compact Top Picks panel uses the current line-shopping response and shows the top 10 active, non-stale, non-expired strategy candidates sorted by estimated EV.
Top Picks and line-shopping rows include `entryCompatibilityGroup` and related entry-rule metadata. DFS entries are provider-scoped: PrizePicks legs cannot mix with Underdog, DraftKings Pick6, Betr Picks, or sportsbook-context legs. Sportsbook rows remain market context for this DFS-only user.

For dashboard startup performance, `/research` initially uses a lightweight Top Picks read:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/top-picks?take=10&freshnessMinutes=30"
```

The full line-shopping table, market rankings, freshness summary, The Odds ingestion planner, EV backtest, prop reliability, strategy snapshots, and other large diagnostics should be loaded from their section buttons only when needed.

## Check Event Workflow And Planner

Use persisted DB state only:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/event-workflow-status"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/ingestion-plan"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/ingestion-plan?date=2026-06-15"
```

Save larger outputs to `tmp-analysis`:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/ingestion-plan" |
  ConvertTo-Json -Depth 20 |
  Set-Content ..\tmp-analysis\the-odds-ingestion-plan.json
```

## Wait For MLB Results

The MLB result import worker should run automatically every configured interval. Check status:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/results/import/mlb/status"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/results/import/mlb/coverage"
```

Manual one-day import:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/results/import/mlb" `
  -ContentType "application/json" `
  -Body '{"date":"2026-06-15"}'
```

Manual range import:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/results/import/mlb-range" `
  -ContentType "application/json" `
  -Body '{"startDate":"2026-06-01","endDate":"2026-06-15"}'
```

## Run Backtests

Prediction backtest:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/predictions/backtest"
```

The Odds line-shopping backtest:

```powershell
$runId = "PREDICTION_RUN_ID"
$r = Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/line-shopping-backtest?runId=$runId&sampleTake=25&includeSamples=true"
$r | ConvertTo-Json -Depth 12 | Set-Content ".\tmp-analysis\the-odds-line-shopping-backtest-$runId.json"
```

The Odds line-shopping backtest resolves MLB result dates from persisted provider event-date metadata when available, otherwise it uses an MLB Eastern-date fallback so late UTC starts can still match the correct MLB slate date.
Use the calibration fields such as `byModelProbabilityBucket`, `byProbabilityEdgeBucket`, `byTrustLevelAndPropType`, and `modelProbabilityCalibration` to review whether model probability, edge, and trust labels are ranking outcomes correctly before changing recommendation rules.
Use `strategySimulations` to compare read-only candidate filters. Add `strategyOnly=true` for a smaller payload, or pass filters such as `bookmaker=DraftKings`, `propType=Total%20Bases`, `line=1.5`, and `minBestPrice=1.75` to produce the `queryFilteredStrategy` row.
The `/research` MLB Line Shopping table shows strategy classification/tier badges. Use the `Conservative Candidates` preset for the current preferred research view and `Avoid / Diagnostics` to inspect structurally suspicious market shapes without changing model output.

Candidate tiers require positive `bestEstimatedEv`. If a row matches the conservative market shape but the best EV is zero or negative, it remains research/watchlist only and is tagged `NonPositiveEv`.

Persist strategy simulation snapshots after a slate is graded:

```powershell
$runId = "PREDICTION_RUN_ID"
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/the-odds/mlb/line-shopping-backtest/strategy-snapshot?runId=$runId"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/line-shopping-backtest/strategy-snapshots?runId=$runId"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/line-shopping-backtest/strategy-performance-summary"
```

Underdog EV research backtests:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/markets/underdog-ev-backtest"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/markets/underdog-prop-ev-reliability"
```

## Safe Alias Mapping

Readiness audit:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/prediction-readiness-audit"
```

Bulk candidates:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/player-mapping-candidates/bulk"
```

Preview safe alias links:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/player-alias-link-preview"
```

Dry-run auto-safe links:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/player-alias-link/auto-safe" `
  -ContentType "application/json" `
  -Body '{"dryRun":true,"approvedBy":"debug-auto-safe","reason":"Auto-safe normalized alias link for The Odds API MLB player"}'
```

Commit only after reviewing the dry-run:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/player-alias-link/auto-safe" `
  -ContentType "application/json" `
  -Body '{"dryRun":false,"approvedBy":"debug-auto-safe","reason":"Auto-safe normalized alias link for The Odds API MLB player"}'
```

## Worker Dry-Run

The Odds worker skeleton is disabled by default. To see what it would plan without API calls or writes:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/the-odds/mlb/worker-dry-run"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/worker-status"
```

The scheduled refresh worker foundation is also disabled by default. Its status endpoint is read-only and does not call The Odds API:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/scheduled-refresh/status" |
  ConvertTo-Json -Depth 20
```

The scheduled worker and debug/admin `refresh-current` endpoint share the same refresh service. Do not enable scheduled live refreshes until credit/ops policy is approved. If enabled while `RequireForceLive=true`, the worker logs a warning and marks the cycle blocked instead of calling The Odds API.

## Dashboard Usage

Use `/research` as the main cockpit:

- Start with System Health.
- Review Event Workflow and Ingestion Plan.
- Use Manual Ingestion carefully.
- Generate scoped predictions after ingest.
- Use Line Shopping presets for active research.
- Use Market Rankings and EV diagnostics as research, not as public recommendations.

The dashboard is internal only and should not be exposed publicly.
