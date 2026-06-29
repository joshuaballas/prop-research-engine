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
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/top-picks?take=10&freshnessMinutes=30"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/line-shopping?sport=MLB&take=25&freshnessMinutes=30&includeBookmakerPrices=false"
Invoke-RestMethod -Uri "http://localhost:5299/api/dfs/entry-rules"
```

These endpoints are read-only, use persisted database rows only, and do not call The Odds API or Underdog. The entry-rules endpoint maintains rules only for explicitly supported DFS pick'em entry providers; sportsbook rows are context/reference only and do not require min/max leg rules. Optional payout multiplier tables are configured by the app, not The Odds. PrizePicks standard Player Pick top-payout multipliers are configured for Power 2-6 picks and Flex 3-6 picks from the PrizePicks Help Center Payouts page and are marked `StandardOnly`; partial-hit Flex payouts, special projections, and lineup-specific variations are not modeled as estimated payout rows. Underdog, DraftKings Pick6, and Betr Picks currently have no configured multiplier rows because official exact payout tables were not verified through accessible provider sources. Underdog and DraftKings Pick6 expose `payoutModelType=UnknownOrUnavailable`; Betr Picks exposes `payoutModelType=VariableMultiplier` because official House Rules state prizes are posted before entry and non-standard payout conditions can affect multipliers. Entry Builder must keep payout unavailable until exact official payout terms are modeled. Market read DTOs include projection-safety metadata (`projectionType`, `projectionTypeConfidence`, `isAlternateMarket`, `payoutMultiplierMayDiffer`, and `specialProjectionWarning`) so Entry Builder can suppress standard payout estimates for alternate/special rows. If a provider, entry type, and leg count has no configured multiplier row, Entry Builder must show multiplier/payout unavailable and point the user to the provider app. The generic market routes currently support MLB only and report `supportedSports=["MLB"]`; add future sports behind `/api/markets/*` instead of duplicating endpoint trees.

After a successful live The Odds refresh, the app stores stable Top Picks and line-shopping snapshots. Public Top Picks is a persisted read-model route: model/history loading, Likely-Hit evaluation, selected-side consistency checks, and Top Pick selection happen during refresh/snapshot capture or debug-only audits, not during public GET reads. Public MLB snapshot reads recompute lifecycle/actionability at read time before returning rows. A row persisted as active at capture time is excluded once its event has started, it is explicitly expired/stale, or the selected side is unavailable. Top Picks also require usable sportsbook price context; Line Shopping can still show current unpriced sportsbook rows with EV unavailable. If no active Top Pick snapshots exist for the requested filters, `/api/markets/top-picks` returns an empty board with a clear `fallbackReason`; it does not dynamically recompute recommendations. Public Line Shopping can still use its documented persisted context fallback for Soccer / World Cup rows when snapshots are unavailable.

Freshness separates recency from usability: `isFresh=true` means a successful usable public refresh happened within the threshold, while `hasUsableMarketData=false` means that refresh did not produce or no longer has currently actionable market snapshots/data. The freshness response distinguishes `latestProviderIngestionRun`, `latestFinalizationRun`, and `latestUsablePublicSnapshotRun` so a zero-API scheduled finalization/publication attempt is not mistaken for provider ingestion. It also includes snapshot counts, active/stale group counts, `currentPublicBoardStatus`, `currentActionableSnapshotRowCount`, `preservedSnapshotExpiredAtReadTime`, provider ingestion success/failure counts, provider rejected-key detection, and a `usabilityWarning`. Large diagnostic freshness windows can relax stale-by-age checks, but they never override event-start expiration.

Provider line lifecycle tracking runs only after successful snapshot persistence. It tracks first seen, last seen, movement, missing refreshes, pulled status, stale status, event lock, and expiration for provider lines. A provider/API failure is not the same as a pulled line: failed refreshes and rejected keys should not mark every previously seen line as pulled. `Pulled` means the line went missing from successful provider refreshes; `Stale` means it has not been confirmed recently; `Locked` means event start has passed.

Use lifecycle diagnostics before tightening refresh cadence or upgrading The Odds API plan:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/lifecycle-summary?sport=MLB" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/provider-lines/refresh-cadence-insights?sport=MLB" |
  ConvertTo-Json -Depth 20
```

When a refresh succeeds but public Top Picks or Line Shopping show no active rows, first check the latest snapshot lifecycle audit:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/latest-market-lifecycle-audit?sport=MLB&providerCategory=Sportsbook&freshnessMinutes=180" |
  ConvertTo-Json -Depth 100
```

The audit is read-only and compares persisted snapshot lifecycle state with the current clock. If every latest attempted replacement row has `eventStartUtc <= nowUtc`, the no-active-row state is expected dead-slate behavior. Scheduled MLB finalization records dead-slate/no-actionable diagnostics and, when a prior usable public snapshot exists, preserves that prior usable board instead of replacing it with an all-expired snapshot batch. That preserved board is still filtered on every public read; if its events have also started, Top Picks and Line Shopping return zero current rows and freshness reports `currentPublicBoardStatus=NoCurrentlyActionableRows`. If future-event rows exist but are persisted as stale/expired/non-active, inspect stale reasons, observed timestamps, and provider/category filters before changing model gates.

Cadence insight is observational. If only a small percent of lines change or get pulled within 10-15 minutes, keep 15-30 minute polling outside near-lock windows. If a provider/prop type changes quickly, lower cadence for that slice instead of globally.

Provider status is exposed separately at `/api/markets/provider-status`. It reads persisted refresh metadata only and surfaces The Odds API key/credit/provider availability, including rejected-key detection, low/exhausted credit warnings, latest refresh usability, and a recommended action. Its request counters are The Odds API market-refresh telemetry only; they do not include API-Sports Football Soccer result/stat calls or MLB Stats API result-import calls. Use it in frontend health banners before implying current market data is actionable.

Refresh planning is exposed at `/api/markets/refresh-plan`. It is also read-only and uses persisted provider status, snapshots, refresh metadata, configured `TheOddsScheduledRefresh:Targets`, and `TheOddsRefreshPlanner` budget/cadence config to explain whether a scheduled refresh would run now or skip. The legacy `plans[0]` object remains the MLB-compatible planner view; `targetSummary` and `targets` list every configured refresh target with sport, competition, provider category, bookmaker preset, provider sport key, estimated calls, latest run ids, next eligible refresh, skip reason, `executionWired`, `providerSportKeyVerified`, latest run success/block state, latest actual API calls, and latest row create/update/upsert counts. Latest-run fields are attributed only to runs matching the target sport, competition, provider category, and effective persisted bookmaker preset; for MLB this includes the same preset normalization used by live refresh, so a configured `sportsbookContext` target can match persisted `default` sportsbook runs without matching DFS. World Cup runs do not appear on disabled Soccer league placeholders and MLB DFS/Sportsbook runs do not cross over. It estimates calls and credit-budget impact, but it never calls The Odds API and never enables the scheduled worker. `TheOddsRefreshPlanner:EnforcementEnabled=false` by default, so local daily/monthly budgets, provider remaining-credit estimates, and per-run estimate caps are diagnostics/warnings only. Active scheduled-refresh blockers in the default mode are real provider/safety conditions such as rejected API keys (`ProviderRejectedKey`), provider unavailability, and recent quota/rate-limit/provider-failure backoff. Provider quota headers and actual provider errors are the source of truth: fresh low remaining-credit telemetry is reported as a warning, but it does not block unless `EnforcementEnabled=true` and `BlockOnProviderCredits=true`. Per-run estimate caps block only when `EnforcementEnabled=true` and `BlockOnPerRunEstimate=true`. Local daily/monthly caps are deprecated as blockers (`MaxDailyCredits=0` by default) unless explicitly enabled with their block flags. Provider credit telemetry older than `TheOddsRefreshPlanner:ProviderTelemetryStaleAfterMinutes` is reported as stale and does not hard-stop refreshes based only on old `requestsRemaining`; the next successful provider call refreshes the quota headers.

The scheduled The Odds refresh worker is planner-gated but its timer is intentionally simple. Base `appsettings.json` keeps the worker disabled, uses `IntervalMinutes=15`, and keeps `AllowScheduledLiveRefresh=false` so production-like defaults do not burn credits unexpectedly. The scheduler evaluates configured `Targets` together, then starts every runnable/execution-wired target ingest in parallel with its own DI scope and `ApplicationDbContext`. MLB scheduled targets run in ingest-only mode and defer public snapshot, lifecycle, and ledger finalization until all runnable MLB target ingests finish; then the worker runs one controlled MLB finalization pass. Scheduled target diagnostics include the worker target-launch timestamp as well as persisted run timings so same-cycle launch spread can be audited separately from provider event discovery or persistence duration. Soccer World Cup Sportsbook refresh currently persists provider-context rows and identity diagnostics during its target ingest path. After a successful full cycle completes, the next run is scheduled at `lastSuccessfulRefreshCompletedAtUtc + effectiveIntervalMinutes`; the worker delay is `max(0, nextRefreshAtUtc - now)`. There is no separate theoretical due time, worker wake due time, header data-available estimate, or frontend rollover clock. Development config enables `mlb-dfs` with `bookmakerPreset=dfsAll` and `mlb-sportsbook` with `bookmakerPreset=allSportsbooks`; MLB Sportsbook rows remain sportsbook recommendations/context and are not Entry Builder-addable. `dfsAll` maps to `prizepicks,underdog,pick6,betr_us_dfs`; `allSportsbooks` maps to `draftkings,fanduel,betmgm,espnbet,betrivers,fanatics,williamhill_us,betonlineag,bovada,lowvig,mybookieag`. Soccer World Cup Sportsbook is execution-wired through the same guarded sport-neutral refresh path used by `POST /api/debug/the-odds/markets/refresh-current`; Development config enables that target with `providerSportKey=soccer_fifa_world_cup`, `bookmakerPreset=allSportsbooks`, `providerCategory=Sportsbook`, and `Regions=us`. This worker refreshes market/provider-context rows only; Soccer outcomes/API-Sports player stats are imported by the separate `SoccerResultImportWorker`, which reuses the guarded manual World Cup `backfill-completed` importer and persists run/fixture diagnostics. Base config keeps the Soccer result worker disabled; Development enables it but still requires valid API-Sports config before it calls the provider. `/api/debug/soccer/results/api-sports/world-cup/scheduled-import-status` reports the result worker state and latest persisted import coverage. Base config keeps MLB Sportsbook and Soccer World Cup disabled. EPL, Ligue 1, Bundesliga, Serie A, La Liga, and MLS remain disabled placeholders with missing/unverified provider keys and `ScheduledExecutionNotYetWiredForTarget`/`ProviderSportKeyNotVerified` style skip reasons until their The Odds keys and execution paths are approved. Do not guess missing soccer provider keys in code or config. The guarded cycle still re-reads the planner before provider calls and skips without provider calls when the planner says `shouldRefresh=false`, when an explicitly enabled planner gate sets `budgetAllowed=false`, when provider status is unavailable/rejected-key, or when scheduled live refresh is not explicitly allowed. Missing persisted slate events are not a hard stop when `TheOddsScheduledRefresh:AllowEventDiscoveryWhenNoPersistedEvents=true`; the planner uses `FallbackEstimatedApiCalls` for diagnostics and lets the refresh service discover events from The Odds. When current MLB slate events have already started and provider discovery returns future events, scheduled ingest uses the next future MLB slate; when no future provider events are returned, started events are not ingested as an actionable public board. `TheOddsScheduledRefresh:MaxEstimatedCallsPerRun=0` is disabled; a positive value blocks only when planner enforcement and `BlockOnPerRunEstimate` are enabled. Game/event start is not a global refresh stop by default: `TheOddsRefreshPlanner:StopAfterEventStart=false` means provider polling can continue after games start so lifecycle tracking can capture movement, pulled lines, stale rows, and provider availability.

Live The Odds refresh writes are serialized inside each API process by a process-local concurrency gate at the full-cycle/manual-live level. A scheduled cycle holds the gate while its sibling target ingests run in parallel, so debug/admin live refresh paths cannot overlap an active scheduled cycle, but runnable scheduled targets do not serialize each other through the gate. Dry-run requests and blocked `forceLive=false` requests do not enter this live-write path. Provider line lifecycle tracking runs after core snapshot and ledger persistence and is best-effort: it uses no-tracking source reads, retries once on EF concurrency conflicts, logs the failing lifecycle entity details, and records lifecycle diagnostics on the refresh run. Lifecycle event history is append-only and idempotent: existing equivalent events are skipped rather than updated, and any historical event tracked as `Modified` or `Deleted` before save is detached and logged. If lifecycle tracking still fails, the core market refresh remains successful while diagnostics show the lifecycle failure. If a refresh fails after EF has tracked side-effect entities, the failure cleanup detaches pending side-effect entities and records only the failed `TheOddsRefreshRun` state, so stale tracked snapshot/lifecycle/ledger entities do not cause a second failure while recording the original failure. This guard is not a cross-process database lock; avoid running multiple local API instances with scheduled workers enabled against the same database unless a deployment-level or database-level singleton guard is added.

Refreshability and actionability are separate. Refreshability controls whether the backend calls The Odds API and persists the latest provider state. Actionability controls whether a user can add a line to a DFS entry. A started event can still be refreshed for lifecycle tracking while its DFS Add Leg action is disabled or locked in the frontend. Provider/API failures must not mark previously seen rows pulled; only successful refreshes that no longer include a line should advance missing/pulled lifecycle state.

Planner decisions can be persisted for audit. The scheduled worker writes a planner decision row each time it runs a planning cycle. Manual reads remain read-only unless `/api/markets/refresh-plan?persistDecision=true` is explicitly used. Read history with `/api/markets/refresh-plan/history?take=20`.

Generic scheduled status is available at `/api/debug/the-odds/scheduled-refresh/status`; the old MLB compatibility route remains at `/api/debug/the-odds/mlb/scheduled-refresh/status`. Generic status reports the simple timer fields `lastSuccessfulRefreshCompletedAtUtc`, `nextRefreshAtUtc`, `secondsUntilNextRefresh`, `isRefreshInProgress`, `currentRunStartedAtUtc`, `currentRunElapsedSeconds`, `lastRunStartedAtUtc`, `lastRunCompletedAtUtc`, `lastRunSucceeded`, `lastRunDurationSeconds`, and `lastError`, plus target count, enabled target count, runnable target count, per-target latest run ids, latest run success/block/failure state, estimated/actual calls, latest row create/update/upsert counts, provider sport key, provider-key verification, execution wiring, competition, provider category, skip reason, and `executionBlockedReason` when config prevents scheduled live execution. Compatibility fields such as `workerNextWakeUtc`, `nextRefreshDueUtc`, `nextRefreshStartEstimateUtc`, `nextHeaderSyncTargetUtc`, and `nextDataAvailableEstimateUtc` map to `nextRefreshAtUtc` and should not be treated as separate estimates. Active-run and next-cycle estimate fields are null while a run is active. It also includes `lastRunPhaseTimings` for diagnostics and `resultImportWorkflows` so operators can see MLB result import and Soccer / World Cup API-Sports result import as separate workers from The Odds market refresh. Use `/api/debug/the-odds/refresh-runs/recent?take=20` to audit recent run ids, trigger source, `scheduledCycleId`, `targetKey`, target launch/ingest timings, finalization timings, duration, actual API calls, and overlap indicators. Same-cycle target overlap is intentional parallelism and appears under `parallelSiblingOverlapCount`; `rowsWithOverlap` and `rowsStartedWhileAnotherRunActive` are reserved for forbidden overlap. Status endpoints remain read-only and never call The Odds API.

Use the production-safe performance summary to inspect provider, prop-type, and strategy slices before showing recommendations confidently:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-summary?sport=MLB&candidateOnly=true"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-summary?sport=MLB&providerCategory=DFS&candidateOnly=true"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-summary?sport=MLB&propType=Total%20Bases&candidateOnly=true"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-summary?sport=MLB&candidateOnly=true&gradingMode=latestCompleted&includeDiagnostics=true&diagnosticSampleSize=10"
Invoke-RestMethod -Uri "http://localhost:5299/api/markets/performance-results?sport=MLB&date=2026-06-19"
```

The summary is read-only and now prefers the durable `recommendation_snapshots` ledger plus `prop_results`. During the current guarded Likely-Hit era, refresh-time ledger capture writes the same refresh-computed public Top Pick set that is later exposed through persisted `/api/markets/top-picks` snapshots; legacy internal candidate/watchlist rows are no longer written as public-performance rows. Older polluted same-day rows can remain in the table, but default guarded-era Performance excludes rows that were not public Top Picks at capture time, rows with invalid/missing side, missing provider/bookmaker key, stale/non-active lifecycle, explicit performance exclusion, and duplicate exact public pick keys. Default Performance is intentionally narrow: `candidateOnly=true` evaluates public recommendation rows marked `IsCandidate` or `IsTopPick`; for the guarded era this effectively means public-at-capture `IsTopPick=true` after the stricter validation and de-dupe pass. Sportsbook recommendations are performance-trackable even though they are not Entry Builder-addable; `entryActionableForUser=false` by itself does not make a public sportsbook Top Pick stale or non-playable. `topPickOnly=true` narrows the dataset to `IsTopPick` rows and still applies the same default watchlist/research/stale exclusions. Explicit diagnostic switches such as `includeWatchlist=true`, `includeResearch=true`, `includeStale=true`, or `includeLegacy=true` can still broaden the read for audit/debugging, but those rows are not part of the default guarded-era public performance view. Performance eligibility also requires a gradeability precheck: canonical player id, supported MLB result prop type, event id, event start time, and expected result date must be present. Ledger result lookup is recalculated at read time from imported results and uses expected result date, canonical `players.id` or a unique normalized player-name fallback, and prop type id or normalized prop/stat type. Provider, side, and line are not used to find the result row; side and line are grading inputs only after the stat result is found. `fromDate`/`toDate` and `startDate`/`endDate` aliases can be used for date-sliced audits. `dataAvailability.publicPerformanceLedgerRowsLoaded`, `dataAvailability.rawCurrentEraLedgerRowCount`, `dataAvailability.publicAtCaptureEligibleRawRowCount`, `dataAvailability.duplicateEligibleExcludedRowCount`, `dataAvailability.finalPublicPerformanceRowCount`, and the older compatibility aliases explain guarded-era drift without deleting rows. `dataAvailability.matchedViaExactPlayerIdDateStatRows`, `matchedViaNormalizedPlayerNameDateStatRows`, and `ambiguousFallbackRows` show how selected rows matched. If no ledger rows exist yet for non-Dashboard modes, legacy snapshot diagnostics may still be used as fallback. Customer Dashboard performance should use `gradingMode=latestCompleted`, which uses only the latest imported result date and suppresses current-slate pending counts; use `currentSlate` only when you explicitly need pending/current slate diagnostics. Public Performance groups include only determined outcomes (`Won`, `Lost`, and `Pushed`) by default. `NoResult`, `Unmatched`, and `Pending` rows are excluded from hit rate, ROI, and default customer group breakdowns; use debug audit endpoints or `includeNoResultDiagnostics=true` only for admin diagnostics. When no rows are gradeable, hit rate and simulated ROI are returned as unavailable/null rather than fake 0% or -100% performance.

Performance `dataAvailability` exposes both pre-filter and post-filter counts for the selected completed date. Use `preFilterRowsForCompletedDate`, `rowsExcludedWatchlist`, `rowsExcludedResearch`, `rowsExcludedStale`, `rowsExcludedNonPlayable`, `rowsExcludedNotCandidateOrTopPick`, `rowsExcludedMissingPlayer`, `rowsExcludedMissingProp`, `rowsExcludedUnsupportedProp`, `rowsExcludedMissingEventMapping`, and `rowsExcludedEventDateMappingMismatch` to explain why rows were removed. Structural gradeability exclusion counts are based on rows that already passed the requested performance branch, so `rowsExcludedUnsupportedProp` means an otherwise-selected row was actually removed because the normalized prop is unsupported. `rowsAfterPerformanceEligibility`, `filteredRowsForCompletedDate`, and the group `totalPicks` are the post-filter set used for grading and summary math.

The public Performance page separates the latest completed slate from aggregate history. The "Yesterday's Results" section uses `/api/markets/performance-results` and lists only determined `Won`, `Lost`, and `Pushed` rows. `summary.noResultExcluded` explains provider lines with no player result that were excluded from hit rate and simulated ROI. The "Overall History" section uses `/api/markets/performance-summary` and keeps no-result diagnostics out of customer-facing tables unless explicitly requested for admin/debug investigation. Performance is model-era quarantined by default: when `modelEra` is omitted and `includeLegacy=false`, it uses `TopPicksModel:CurrentModelEra` and excludes rows with missing, unknown, or older eras. Use `includeLegacy=true` only for audit/comparison; diagnostics report `excludedLegacyRows` and `modelEraBreakdown`.

To reset a polluted visible performance baseline without deleting data, use the debug-only soft exclusion endpoint. Always inspect the dry run first. For the Dashboard/public candidate baseline, use the same filters as the public summary:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/recommendations/performance/reset-baseline?sport=MLB&candidateOnly=true&gradingMode=latestCompleted&gradedOnly=true&take=6&reason=PerformanceBaselineReset&dryRun=true" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/recommendations/performance/reset-baseline?sport=MLB&candidateOnly=true&gradingMode=latestCompleted&gradedOnly=true&take=6&reason=PerformanceBaselineReset&dryRun=false" |
  ConvertTo-Json -Depth 20
```

The reset endpoint selects from the same post-filter row set used by `/api/markets/performance-summary` for the requested flags (`candidateOnly`, `topPickOnly`, `includeWatchlist`, `includeResearch`, `includeStale`, `includeExcluded`, `gradingMode`, optional `propType`, and optional `provider`). Its dry-run response includes `selectionSource=PerformanceSummaryFilteredRows`, `filtersApplied`, `gradedRowsAvailableBeforeTake`, `alreadyExcludedRowsSkipped`, and before/after public performance summaries so the affected rows can be compared with the visible public summary before executing. The reset endpoint marks selected `recommendation_snapshots` with `is_excluded_from_performance`, `performance_exclusion_reason`, `performance_excluded_at_utc`, and `performance_excluded_by`. It does not delete recommendation snapshots, predictions, prediction runs, or `prop_results`. Public `/api/markets/performance-summary` excludes those rows by default and reports `excludedFromPerformanceRows`; `includeExcluded=true` can be used for debug/admin review and reports `includedExcludedRows`.

Audit/backfill recommendation history without provider calls:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/recommendations/ledger/audit?fromDate=2026-06-17&toDate=2026-06-18" |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/recommendations/pipeline-audit?sport=MLB&fromDate=2026-06-17&toDate=2026-06-18" |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/recommendations/ledger/result-coverage?date=2026-06-17&take=25" |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/performance/top-pick-ledger-capture-audit" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/performance/result-match-audit?sport=MLB&date=2026-06-19" |
  ConvertTo-Json -Depth 30

Invoke-RestMethod -Uri "http://localhost:5299/api/debug/recommendations/ledger/no-result-audit?sport=MLB&date=2026-06-17&candidateOnly=true" |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/recommendations/ledger/backfill?fromDate=2026-06-17&toDate=2026-06-18&source=both&dryRun=true" |
  ConvertTo-Json -Depth 40
```

`pipeline-audit` rolls up each date/prop type across imported `prop_results`, prediction runs/predictions, provider snapshot rows, recommendation snapshots, gradeable rows, `NoResult`, `Unmatched`, `Pending`, stale/non-playable rows, and reason codes including `MatchedAndGraded`, `ProviderLineForInactivePlayer`, `EventDateMismatch`, and `Unknown`. `performance-summary` and `ledger/no-result-audit` use the same reason-code ordering for performance-eligible rows: same player/prop on a nearby imported date is `EventDateMismatch`, same-date prop imported for other players without this player is `ProviderLineForInactivePlayer`, and same-date player rows without this prop are `NoResultForPlayerPropOnDate`. `debug/performance/result-match-audit` is the most direct row-by-row tool for a bad public Performance slate: it compares current matcher outcome with the legacy exact-id-only outcome and reports same-date, same-player-id, same-player-name, same-player/stat, player mapping status, and closest `prop_results` candidates. Use it before changing grading code so the failing key is proven. `ledger/result-coverage` explains individual captured picks: canonical player id, normalized player/prop keys, selected side/line, model probability/edge/EV/trust, stored historical sample size, recent persisted historical results, exact matched `prop_results` ids/value when present, nearby date player/prop matches for date-mismatch debugging, final grade status, reason code, and user-facing reason. `ledger/no-result-audit` groups performance-eligible non-graded rows by prop/provider/reason and exposes per-row result coverage counts, nearby player/prop matches, UTC/Eastern event-date diagnostics, and same-season player/prop result counts so top-pick NoResult cases are explainable instead of silently falling into an unbucketed NoResult pile.

`debug/performance/top-pick-ledger-capture-audit` compares the debug-only current dynamic Top Picks reconstruction against eligible same-day recommendation ledger rows. It separates current-board coverage from public-at-capture ledger inventory, so a smaller current board does not imply every older public-at-capture pick is polluted. Prefer the clearer fields `rawLedgerRowCountForSlate`, `rawCurrentEraLedgerRowCount`, `publicAtCaptureEligibleRawRowCount`, `rawDuplicateKeyCount`, `rawDuplicateRowCount`, `duplicateEligibleRawRowCount`, `duplicateEligibleExcludedRowCount`, `duplicateEligibleCountedRowCount`, `finalPublicPerformanceRowCount`, and `finalPublicPerformanceRowsByProviderCategory`; older duplicate/ledger count names remain temporary compatibility aliases.

MLB expected result dates should represent the actual MLB game date, not the UTC capture date. For games that start after midnight UTC but are still the previous evening in Eastern time, the expected result date can be the prior UTC date. Result coverage diagnostics expose both the UTC event date and Eastern event date so `EventDateMismatch` rows can be traced to result-date assignment rather than treated as model performance. Future The Odds ledger captures also run a conservative gradeability check before marking rows as Candidate or Top Pick; rows with missing canonical player, unsupported result prop, missing event/result-date mapping, Eastern-date mismatch, or non-playable lifecycle state remain available for research diagnostics but are not promoted into default performance picks.

The ledger idempotency key is one row per exact public opportunity per model era: sport, expected result date, event id, canonical player id, prop type id or normalized prop type, side, line, provider category, bookmaker/provider key, and model era. Internal strategy labels and candidate/watchlist flags are not part of the guarded-era public pick identity, so repeated sightings across refresh runs update latest model metrics, `LastSeenAtUtc`, and `SeenCount` while preserving `FirstSeenAtUtc` instead of creating multiple independent bets. Existing historical duplicate rows are not deleted; default guarded-era performance de-dupes them by the same exact public pick key.

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

The compact Top Picks panel uses the production-safe `/api/markets/top-picks` response and shows active, non-stale, non-expired Likely-Hit recommendations sorted by Top Picks score/hit chance. Top Picks is the only public recommendation-curated market surface; Prop Scanner, Prop Board, and Line Shopping remain broad inventory pages and should not hide valid current rows because they are not Top Picks.

Public market display rows include `providerCategory`, `isDfs`, `isSportsbook`, `showExpectedValue`, `displayExpectedValue`, and `expectedValueUnavailableReason` for compatibility and diagnostics. Customer-facing UI should foreground Hit chance, Edge, provider, side, line, sport/competition, lifecycle, and performance outcomes; it should not render labels named EV, Trust, Warning, Reason, Sample, Strategy, ResearchOnly, Avoid, or Candidate as the primary recommendation language. EV, trust, strategy, warning, and sample fields remain available to internal/debug tooling where needed.

The active Top Picks model is configured under `TopPicksModel`. The default is `ActiveModel=LikelyHitV1`, with `CurrentModelEra=LikelyHitV1_SportsbookBalanced_65Plus_ConsistencyGuarded`, base thresholds `MlbMinimumHitChance=0.58`, `MlbMinimumReliability=0.65`, `SoccerWorldCupMinimumHitChance=0.60`, `SoccerWorldCupMinimumReliability=0.70`, and `MinimumEffectiveSampleSize=10`. Balanced sportsbook defaults are `SportsbookBalancedMaxFavoriteAmericanOdds=-300`, `SportsbookBalancedMaxUnderdogAmericanOdds=250`, `SportsbookBalancedTargetMinHitChance=0.60`, `SportsbookBalancedPublicMinimumHitChance=0.65`, `SportsbookBalancedTargetMaxHitChance=0.75`, and `SportsbookBalancedSuspiciousMarketGap=0.25`; sportsbook Top Picks reject selected-line prices below `-300` or above `+250`. The `SportsbookBalancedTargetMinHitChance` floor is the lower modelability/inventory/replay gate, while public sportsbook Top Picks require the post-uncertainty selected-line hit chance to meet `SportsbookBalancedPublicMinimumHitChance`; 60-65% sportsbook rows remain visible in Line Shopping, Prop Scanner, and replay diagnostics but are excluded from public Top Picks with `BelowSportsbookPublicTopPickHitChance`. MLB public Top Picks now load persisted selected-side history before promotion and fail closed when a high displayed hit chance severely contradicts the recent chart window or side/line averages. New Top Picks snapshots, Line Shopping snapshots, and recommendation ledger rows persist `modelVersion`, `recommendationContractVersion`, `probabilityModelVersion`, and `modelEra`; old rows without a model era stay legacy/unknown and are not assigned the current era retroactively. Line Shopping and Prop Scanner keep the full inventory, including active rows rejected by these Top Picks gates. Likely-Hit v1 computes one selected-side hit chance per exact side/line, then uses sportsbook price only for payout quality, suspicious market-gap gating, Balanced ladder utility, and the selected-side consistency guard. EV is not the primary promotion gate, and sportsbook rows can remain recommendations with display EV hidden/suppressed. Use `/api/debug/model/likely-hit/opportunity-audit` to inspect accepted and rejected current opportunities, `/api/debug/model/likely-hit/top-pick-consistency-audit` to compare current Top Picks against selected-side history/chart diagnostics, and `/api/debug/model/likely-hit/replay` to run read-only replay/calibration buckets against persisted opportunities and local results without writes or provider calls.
Top Picks and line-shopping rows include `entryCompatibilityGroup` and related entry-rule metadata. Entry Builder validates only the locked DFS provider/compatibility group. PrizePicks legs cannot mix with Underdog, DraftKings Pick6, Betr Picks, or sportsbook-context rows. Sportsbook rows remain comparison context only and cannot be added to entries. The provider app is the final source of truth for available entry types, final terms, and payout; app-configured multipliers are estimates only and carry source confidence. PrizePicks multiplier rows represent standard all-correct Player Pick top payouts only. Entry Builder must not apply those standard rows when selected legs include Demon, Goblin, Taco, promo, discount, boost, alt-line, special, alternate, or unknown projection types unless verified modifier logic is added. Underdog, DraftKings Pick6, and Betr Picks payout estimates remain unavailable because exact official payout rows are not configured. The Odds `_alternate` market keys are enough to identify alternate payout context; PrizePicks even/+100 alternate rows are inferred as Demon, while Goblin remains Alternate unless explicit provider metadata is available.

Soccer / World Cup Phase 1 public support is persisted-only sportsbook context. Use `sport=SOCCER&competition=World Cup` for persisted Line Shopping context. If no Soccer line-shopping snapshots exist, public Line Shopping may build groups from persisted The Odds `platform_props` context rows; this fallback is read-only and does not write predictions, grades, or snapshots. When an approved `api-sports-football` provider entity link connects the local platform player to persisted API-Sports `prop_results`, those fallback rows include compact `playerHistorySummary` with stat averages, competition breakdown, and recent game log. Soccer rows remain `entryActionableForUser=false` and must not enter Entry Builder. By default `SoccerPredictions:Enabled=false`, `SoccerGrading:Enabled=false`, and `SoccerTopPicks:Enabled=false`, so model hit chance, public grading, and Top Picks stay unavailable. In Development those flags can be enabled for the safe stat subset only: Goals, Assists, Shots, and Shots On Target. Soccer Top Picks return promoted sportsbook recommendations only when rows pass supported-prop, sample, hit-probability, and lifecycle gates; research-only Soccer rows remain Line Shopping context and are excluded from Top Picks. The empirical Soccer model reads persisted history only; it does not call providers or write from public endpoints. EV is shown only when a sportsbook row has real odds plus model probability, never as DFS-style fake EV.

```powershell
$base = "http://localhost:5299"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$path = "tmp-soccer-line-shopping-history-$stamp.json"
$response = Invoke-RestMethod "$base/api/markets/line-shopping?sport=SOCCER&competition=World%20Cup&includeBookmakerPrices=false&take=25"
$response | ConvertTo-Json -Depth 100 | Set-Content $path
$response | Select-Object snapshotBacked,summary,diagnostics | ConvertTo-Json -Depth 20
```

Recent World Cup result coverage is checked with persisted-only status first:

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/import-status?fromDate=2026-06-25&toDate=2026-06-25" |
  ConvertTo-Json -Depth 80
```

Backfill completed API-Sports Football fixtures with the existing debug-only endpoint. `dryRun=true` is the default, but live discovery still requires `forceLive=true`; review `coverageByDate`, skipped fixtures, failures, and preview row counts before setting `dryRun=false`.

```powershell
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/world-cup/backfill-completed" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-25","toDate":"2026-06-25","dryRun":true,"forceLive":true}' |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/world-cup/backfill-completed" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-25","toDate":"2026-06-25","dryRun":false,"forceLive":true}' |
  ConvertTo-Json -Depth 80
```
  
### DFS Payout Rule Source Status

Current payout-model classifications:

- PrizePicks: `FixedMultiplierTableWithPartialHitRows`. Standard Player Pick all-correct top-payout rows are configured for Power and Flex. Partial-hit Flex rows and special projection modifiers are not modeled.
- Underdog: `UnknownOrUnavailable`. Official source reviewed: `https://app.underdogfantasy.com/rules`. No exact provider + entry type + leg count payout table was verified in this pass.
- DraftKings Pick6: `UnknownOrUnavailable`. Official source reviewed: `https://pick6.draftkings.com/`. The accessible app shell confirms Pick6 flow and at least two picks, but no fixed payout table was verified.
- Betr Picks: `VariableMultiplier`. Official source reviewed: `https://help.betr.app/en/articles/9461972-betr-picks-house-rules`. House Rules verify 2-10 player lineups, Perfect Play/Flex Play formats, posted prize before entry, and non-standard payout conditions such as Edge Combos. No exact fixed multiplier table is configured.

Do not add multiplier rows from affiliate articles, screenshots, memory, or inferred app behavior. Add rows only when the official provider source verifies exact payout terms and any partial-hit, promo, boost, contest, jurisdiction, or projection-modifier conditions.

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
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/results/import/mlb/coverage-audit?fromDate=2026-06-19&toDate=2026-06-23&includeDetails=true"
```

`/api/debug/results/import/mlb/status` includes `latestWorkerCycle` when a worker run has persisted date attempts. Each date outcome reports the selection reason, row/player/prop counts, coverage status, and an `outcomeCategory`. `Complete` is a successful finished date. `Partial`, `PendingGames`, and `NoGames` are coverage outcomes, not hard worker failures. `Failed` is reserved for provider exceptions, rejected/rate-limited API calls, database errors, or other true per-date exceptions.

By default the scheduled worker only selects recent actionable dates. `MlbResultImportWorker:RecentWindowDays` defaults to `14`, `HistoricalBackfillEnabled` defaults to `false`, and old historical `Partial` / `NotStarted` / `Failed` dates are skipped during normal startup so development restarts do not repeatedly spend MLB Stats API calls on stale 2024 coverage gaps. When historical backfill is explicitly enabled, old non-complete dates still honor `HistoricalNonCompleteAttemptCap` default `3`. Use `/api/debug/results/import/mlb-range/status` to see `workerSelectionPolicy`, selected dates, skipped old historical counts/reasons, and the latest worker cycle before enabling historical backfill.

Manual one-day import:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/results/import/mlb" `
  -ContentType "application/json" `
  -Body '{"date":"2026-06-15"}'
```

Repair partial or missing dates with a dry run first:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/results/import/mlb/repair-missing" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-23","toDate":"2026-06-23","dryRun":true,"repairPartial":true,"repairMissing":true}'

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5299/api/debug/results/import/mlb/repair-missing" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-23","toDate":"2026-06-23","dryRun":false,"repairPartial":true,"repairMissing":true}'
```

Performance summary skips dates whose MLB result coverage is `Partial`, `NotStarted`, `PendingGames`, `NoGames`, or `Failed` when complete coverage states exist. Check `diagnostics.resultCoverage` for the clearest read: `latestCoverageStatus` describes the latest available/imported date, while `selectedGradedDateCoverageStatus` and `resultCoverageCompleteForSelectedDate` describe the date actually used for performance. The older `dataAvailability` coverage fields remain for compatibility.

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

Side-selection audit:

```powershell
$base = "http://localhost:5299"
Invoke-RestMethod -Method Post "$base/api/debug/predictions/generate/mlb"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=true&includeSamples=true&maxSamples=200"
```

Use the side-selection audit before changing MLB recommendation selection logic when picks look unusually concentrated on Over or Under. Validate against a real `BaselineAverageModel` run from `/api/debug/predictions/generate/mlb` or `/api/debug/predictions/generate/mlb/the-odds`; do not use `/api/debug/predictions/sample`, which creates a fake `SampleModel` demo run. The audit defaults `excludeSampleRuns=true`, reports `latestPredictionRunModelName`, and ignores fake sample runs for latest-run selection. It is read-only, uses persisted predictions/snapshots/ledger rows only, and reports prediction-stage projection/probability/EV diagnostics even when no market snapshots exist. Review `predictionStageSummary`, `hitProbabilityDiagnostics`, `passGateSummary`, `passReasonsByCount`, `failedGatesByCount`, `actualSelectedOverSamples`, `overEligibleButPassedSamples`, `passReasonMissingSamples`, `rejectedByHitProbabilitySamples`, `positiveEdgeRejectedByHitProbabilitySamples`, `passesProjectionEdgeButFailsHitProbabilitySamples`, `highestOverEvSamples`, `projectionAboveLineButSelectedUnderSamples`, and `rowsWhereOverProbabilityGreaterThanUnderSamples` to determine whether an imbalance starts in prediction generation, probability orientation, neutral-price EV, or downstream line-shopping/ledger projection. Production MLB DFS selection is controlled by `MlbPredictionGeneration`: Over and Under projection-edge thresholds default to `0.75`, user-facing DFS picks require `MinimumPickHitProbability=0.55`, strong candidates use `StrongPickHitProbability=0.60`, and rows below `ResearchOnlyBelowHitProbability=0.50` are not promoted. The audit's diagnostic EV is a comparison signal, not a DFS production selection gate.

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

Top Picks use Likely-Hit v1 when `TopPicksModel:ActiveModel=LikelyHitV1`. Promotion prioritizes selected-side hit chance, reliability, effective sample size, lifecycle validity, supported prop mapping, and Balanced-mode payout quality. Projection/probability conflicts, recent-weighted projection conflicts, missing side/line, missing supported history, stale/expired lifecycle, insufficient reliability, sportsbook prices outside the Balanced window, sportsbook Balanced modelability hit chance below `SportsbookBalancedTargetMinHitChance`, public sportsbook Top Picks hit chance below `SportsbookBalancedPublicMinimumHitChance`, and suspicious model-vs-market gaps keep rows out of Top Picks. For alternate lines, Top Picks dedupes to one row per sport/competition/event/player/prop/side/provider-category ladder and selects the eligible line with the best capped hit-chance, payout-quality, sweet-spot, and reliability utility; the full line ladder remains visible in Line Shopping. Sportsbook EV is display/context only and may be suppressed without removing a valid likely-hit recommendation. DFS EV stays hidden from customer-facing display.

Use `/api/debug/model/likely-hit/replay` for read-only Likely-Hit replay and calibration. The replay endpoint includes public 65%+ Top Pick diagnostics in `publicTopPickPerformanceAnalysis`, `publicTopPickConcentrationAnalysis`, `publicTopPickProfileRecommendations`, `currentPublicTopPickSummary`, optional `publicTopPickSamples`, and optional `publicTopPickSimulation`. `publicTopPickPerformanceAnalysis`, `publicTopPickProfileRecommendations`, and `publicTopPickSimulation` use historical replay rows recalculated as Likely-Hit public Top Picks under the active config; they do not trust stale persisted candidate/recommendation flags and can report replayable/graded/hit-rate/ROI metrics. `currentPublicTopPickSummary` remains the current live board. `publicTopPickConcentrationAnalysis` remains current-board concentration, with historical performance attached to matching prop shapes when available. Segment groups include hit chance, prop shape, odds, break-even margin, effective sample, side, bookmaker, provider category, sport/competition, balanced utility, payout quality, and sweet-spot score. Concentration diagnostics flag shapes above 25% of the current board and top-three shape share above 60%, but do not enforce caps. Public profile recommendations (`Allow`, `Penalize`, `Block`, `NeedsMoreData`) and public simulations (`simulatePublicTopPickProfileGate`, `maxRowsPerPropShape`, `maxPropShapeShare`, `minPublicBreakEvenMargin`, `minPublicBalancedUtility`) are what-if diagnostics only and do not change public Top Picks, Line Shopping, Prop Scanner, or model gates.

The replay endpoint also keeps `borderlineHitChanceAnalysis` for sportsbook rows in the 60-65% final hit-chance bucket, `borderlineProfileRecommendations` for diagnostic `Allow`/`Penalize`/`Block`/`NeedsMoreData` profile labels, `currentBoardBorderlineSummary`, and optional `simulateBorderlineProfileGate=true` what-if output. These fields are analysis infrastructure only: they do not tune thresholds, remove rows from Line Shopping or Prop Scanner, or write data. Public sportsbook Top Picks use the replay-backed `SportsbookBalancedPublicMinimumHitChance=0.65` floor by default, so 60-65% sportsbook rows are preserved for inventory/replay but are not public Top Picks unless that configuration is explicitly changed.

```powershell
$base = "http://localhost:5299"
$replay = Invoke-RestMethod "$base/api/debug/model/likely-hit/replay?sport=MLB&providerCategory=Sportsbook&includeSamples=true&sampleTake=20"
$replay.borderlineHitChanceAnalysis | ConvertTo-Json -Depth 100
$replay.borderlineProfileRecommendations |
  Sort-Object graded -Descending |
  Select-Object -First 50 |
  ConvertTo-Json -Depth 100

$sim = Invoke-RestMethod "$base/api/debug/model/likely-hit/replay?sport=MLB&providerCategory=Sportsbook&simulateBorderlineProfileGate=true&minBorderlineBreakEvenMargin=0.05&minBorderlineEffectiveSampleSize=30&borderlineWorstAllowedFavoriteAmericanOdds=-125&borderlineBestAllowedUnderdogAmericanOdds=150&requireBorderlineProfileAllow=true&includeSamples=true&sampleTake=20"
$sim.simulation | ConvertTo-Json -Depth 100
```

Public market rows distinguish model hit chance from historical chart hit rate. `selectedSideProbability`/`modelProbability` may be null when no selected side has a usable model probability even if the details chart has historical results. In that case the frontend should show an unavailable marker for Hit chance and keep the chart metric labeled as historical hit rate. Do not default missing model probability to 0, and do not substitute historical hit rate as the model probability. Use `/api/debug/predictions/mlb/public-row-probability-audit` with `playerName`, `provider`, `propType`, `side`, and `line` to trace a public row back to snapshot details, prediction joins, and persisted result history.

Audit sportsbook EV calibration before promoting new sportsbook recommendation behavior:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/the-odds/mlb/sportsbook-ev-audit?takeSamples=25" |
  ConvertTo-Json -Depth 80
```

Public Top Picks and Line Shopping reads should stay snapshot-backed when snapshots are available and should not regenerate prediction features or issue per-row `prop_results` history queries. MLB prediction generation batches historical `prop_results` by prop type/date and computes per-player features and selected-side probabilities in memory; repeated `player_id ANY / prop_type_id ANY / event_date <` queries during startup usually indicate a live refresh or prediction-generation path, not DTO read-time probability enrichment.

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

Manual MLB Stats mapping approval for one local player:

```powershell
$base = "http://localhost:5299"
Invoke-RestMethod -Method Post "$base/api/debug/mlb-player-mappings/approve" `
  -ContentType "application/json" `
  -Body '{"playerId":"382564d5-feae-419a-807e-7b4a91bb51f0","mlbPlayerId":"694973","displayName":"Paul Skenes"}'
```

This creates a single `player_external_refs` row for provider `mlb`. It is idempotent for the same player/id pair, refuses an MLB id already mapped to another player, refuses adding a second different MLB id to a player that is already mapped, and does not merge player rows. After approving a missing MLB mapping, rerun the MLB result import/backfill for the relevant completed dates so future `prop_results` attach to the mapped player:

Provider-line MLB mapping repair uses accent-insensitive compact player-name keys, so names such as `Rodriguez` and `Rodríguez`, `Diaz` and `Díaz`, or `Suarez` and `Suárez` audit together without blindly merging rows. Review local-only repair candidates:

```powershell
$base = "http://localhost:5299"
Invoke-RestMethod "$base/api/debug/mlb-player-mappings/provider-line-repair-candidates" |
  ConvertTo-Json -Depth 30
```

Dry-run safe local auto-approval. This does not call MLB or The Odds APIs and writes nothing by default:

```powershell
$base = "http://localhost:5299"
Invoke-RestMethod -Method Post "$base/api/debug/mlb-player-mappings/provider-line-repair/auto-approve" `
  -ContentType "application/json" `
  -Body '{"dryRun":true,"approvedBy":"debug-auto-safe","reason":"Safe local MLB ref repair for provider-line players"}'
```

Commit only after reviewing the dry run:

```powershell
$base = "http://localhost:5299"
Invoke-RestMethod -Method Post "$base/api/debug/mlb-player-mappings/provider-line-repair/auto-approve" `
  -ContentType "application/json" `
  -Body '{"dryRun":false,"approvedBy":"debug-auto-safe","reason":"Safe local MLB ref repair for provider-line players"}'
```

The local auto-approval path only approves unclaimed local/legacy MLB ids already present on the provider-line player. Duplicate rows that already own an MLB id are reported for manual review because `player_external_refs` enforces one `provider=mlb` mapping per MLB id and this workflow does not merge player rows.

```powershell
$base = "http://localhost:5299"
Invoke-RestMethod -Method Post "$base/api/debug/results/import/mlb-range" `
  -ContentType "application/json" `
  -Body '{"startDate":"2026-06-01","endDate":"2026-06-19"}'
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
