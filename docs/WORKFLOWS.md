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

Use this when you want the dashboard to produce active strategy-aware candidates for the current MLB slate. The dry-run form does not call The Odds API and does not write rows. The live form spends The Odds API credits, persists/refreshes The Odds market rows, generates a scoped prediction run, and returns line-shopping strategy summaries for that run.

The current refresh endpoint supports profiling options:

```powershell
$r = Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?includeCandidates=true&includeLineShopping=true&includeFullDiagnostics=true&freshnessMinutes=30"
$r.diagnostics | ConvertTo-Json -Depth 8
```

`TheOddsApi:MaxConcurrentRequests` controls bounded parallel event-odds ingestion for live refreshes. Keep this conservative; the default is `3`.

Dry run:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?dryRun=true"
```

Live refresh:

```powershell
$refresh = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/the-odds/mlb/recommendations/refresh-current?includeCandidates=true&freshnessMinutes=30"

$refresh.predictionRunId
$refresh.strategyClassificationSummary
$refresh.topActiveCandidates | Select-Object -First 10
$refresh | ConvertTo-Json -Depth 20 | Set-Content ".\tmp-analysis\current-mlb-recommendations-refresh.json"
```

## Expand The Odds Bookmaker Coverage Safely

The bookmaker list is config-driven through `TheOddsIngestionWorker:Bookmakers`. Current committed defaults are intentionally narrow:

```json
"Bookmakers": "draftkings,fanduel,betmgm"
```

To test broader coverage, override this value only in ignored local config (`api/appsettings.Local.json`) or environment variables. Add one or two keys at a time, then dry-run the current refresh before live refresh. Candidate opt-in keys depend on The Odds API plan/provider availability; common US keys to verify manually include `bovada`, `espnbet`, `williamhill_us`, and `betrivers`.

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

## Dashboard Usage

Use `/research` as the main cockpit:

- Start with System Health.
- Review Event Workflow and Ingestion Plan.
- Use Manual Ingestion carefully.
- Generate scoped predictions after ingest.
- Use Line Shopping presets for active research.
- Use Market Rankings and EV diagnostics as research, not as public recommendations.

The dashboard is internal only and should not be exposed publicly.
