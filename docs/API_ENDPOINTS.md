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
| GET | `/api/debug/dfs/entry-rules` | Read-only provider-scoped entry compatibility rules for explicitly supported DFS pick'em providers such as PrizePicks, Underdog, DraftKings Pick6, and Betr Picks. The response separates `entryBuildableProviders` from `contextOnlyProviders`; sportsbooks/bookmakers are context-only and do not require entry-rule maintenance. Rules can include optional `payoutMultipliers` rows keyed by entry type and leg count, each with source confidence and optional verification metadata. Rules also expose payout model metadata such as `payoutModelType`, `hasPartialHitPayouts`, `partialHitPayoutsModeled`, `payoutRulesNotes`, `sourceUrl`, and `lastVerifiedAtUtc`. Missing multiplier rows mean estimated payout is unavailable. Rules are conservative and marked `NeedsVerification` where production details still need confirmation. | No | No | No |
| GET | `/api/dfs/entry-rules` | Production-safe provider-scoped DFS entry compatibility rules. Same persisted/read-only catalog as the debug route, without `/api/debug`; it does not create entry submission/bet placement behavior. Entry rules and payout multiplier tables are maintained by the app, not The Odds, and the provider app remains final source of truth. | No | No | No |

PrizePicks currently includes configured standard Player Pick top-payout multiplier rows for Power 2-6 picks and Flex 3-6 picks, sourced from the PrizePicks Help Center Payouts page. These rows are marked `appliesToProjectionType=StandardOnly`. Flex partial-hit payouts, protected plays, promos, Demon/Goblin/Taco/special projections, ties, DNP/Reboot handling, and any lineup-specific variations are not modeled as estimated payout rows; users must confirm final terms in PrizePicks before submitting.

Underdog, DraftKings Pick6, and Betr Picks currently return no configured payout multiplier rows. The current provider investigation did not verify official, exact multiplier tables for those providers through accessible official sources, so Entry Builder keeps estimated multiplier/payout unavailable. Reviewed official source URLs are recorded in each provider rule: Underdog rules at `https://app.underdogfantasy.com/rules`, DraftKings Pick6 at `https://pick6.draftkings.com/`, and Betr Picks House Rules at `https://help.betr.app/en/articles/9461972-betr-picks-house-rules`. DraftKings Pick6's accessible app page confirms Pick6 flow and at least two picks, but does not expose a fixed payout table. Betr Picks House Rules verify 2-10 player lineups, Perfect Play/Flex Play formats, posted prize before entry, and non-standard payout conditions such as Edge Combos; this is represented as `payoutModelType=VariableMultiplier` with no configured exact rows. Do not add payout rows for these providers until official provider rules verify exact provider + entry type + leg count payout terms, including any partial-hit, promo, boosted, contest, or jurisdiction-specific handling.

The persisted market read models expose projection-safety metadata for Entry Builder payout estimation: `marketKey`, `projectionType`, `projectionTypeConfidence`, `isAlternateMarket`, `payoutMultiplierMayDiffer`, and `specialProjectionWarning`. The Odds `_alternate` market keys are treated as alternate/special payout context. PrizePicks alternate rows with even/+100 pricing are inferred as `Demon`; other alternate rows remain `Alternate` unless explicit provider metadata identifies a more specific type. Standard PrizePicks payout multipliers must not be applied when `payoutMultiplierMayDiffer=true`.

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
| GET | `/api/markets/provider-status` | Production-safe provider/API-key/credit status from persisted refresh metadata plus the read-only planner budget explanation. Returns The Odds API health status, latest refresh metadata, persisted credit usage fields when available, rejected-key detection, ingestion success/failure counts, usable-data flags, planner enforcement flags, budget allowed/block reason/explanation, provider credit telemetry age/staleness, provider/per-run/local block flags, `plannerWouldBlockIfEnforced`, `plannerWarnings`, status reason, and recommended action. This endpoint does not call providers. | No | No | No |
| GET | `/api/markets/refresh-plan` | Production-safe refresh planner and credit budget estimator. By default it is read-only and uses persisted refresh metadata, provider status, snapshots, and configured planning budgets/cadence rules to estimate whether a scheduled The Odds refresh would run now or skip. Event start is not a global polling stop unless `TheOddsRefreshPlanner:StopAfterEventStart=true`; started events can still be refreshed for lifecycle tracking while remaining non-actionable for entries. If no persisted events are available and scheduled event discovery fallback is enabled, the plan uses `estimateSource=EventDiscoveryFallback` plus the configured fallback call estimate instead of skipping solely for missing slate events. Returns estimated slate events/API calls, estimate source, planner enforcement flags (`plannerEnforcementEnabled`, local daily/monthly enforcement, provider credit blocking, per-run estimate blocking), legacy configured caps, provider-reported usage/remaining credits, provider credit telemetry age/staleness, whether provider telemetry was used for blocking, `plannerWouldBlockIfEnforced`, `plannerWarnings`, provider availability, active `budgetBlockReason` when an explicitly enabled gate blocks, budget explanation, cadence rule, skip reason, next eligible refresh, and recommended action. `EnforcementEnabled=false` by default, so local daily/monthly budgets, provider remaining-credit estimates, and per-run estimate caps are diagnostics only. Rejected API keys, provider unavailability, and recent quota/rate-limit provider-failure backoff remain active safety skips. Optional `persistDecision=true` writes one manual planner decision audit row. | Optional, only with `persistDecision=true` | No | No |
| GET | `/api/markets/refresh-plan/history` | Production-safe read-only planner decision history. Supports `take`, `sport`, `skipped`, and `source`. Returns persisted manual/scheduled planner decisions, including skip reason, budget state, provider state, estimated calls, and recommended action. | No | No | No |
| GET | `/api/markets/top-picks` | Preferred production-safe persisted Top Picks route. Supports optional `sport`, `take`, `freshnessMinutes`, optional `runId`, optional `providerCategory`, and optional `bookmakerPreset` for freshness filtering. Returns `snapshotBacked=true` only when snapshot rows exist for the requested filters; otherwise falls back to dynamic persisted-row calculation with `snapshotBacked=false` and `fallbackReason`. Market rows include additive `playerId`/`canonicalPlayerId` when a unique canonical MLB player can be resolved. Rows also include display-safe EV semantics: `providerCategory`, `isDfs`, `isSportsbook`, `showExpectedValue`, `displayExpectedValue`, and `expectedValueUnavailableReason`. DFS rows keep raw `bestEstimatedEv` for compatibility/debugging but set `showExpectedValue=false` and `displayExpectedValue=null`; sportsbook rows show EV only when odds or break-even pricing is present. Dynamic DFS fallback ranking uses selected-side hit chance first, then projection edge and model confidence; EV is not the DFS promotion signal. | No | No | No |
| GET | `/api/markets/line-shopping` | Preferred production-safe persisted line-shopping route. Supports optional `sport`, `runId`, `take`, `topOnly`, `freshnessMinutes`, `providerCategory`, and `bookmakerPreset`. Defaults to `topOnly=true`, `take=25`, and `includeBookmakerPrices=false`. Returns `snapshotBacked=true` only when snapshot rows exist for the requested filters/view; otherwise falls back dynamically with a clear reason. Market rows include additive `playerId`/`canonicalPlayerId` when a unique canonical MLB player can be resolved. Rows also include display-safe EV semantics: `providerCategory`, `isDfs`, `isSportsbook`, `showExpectedValue`, `displayExpectedValue`, and `expectedValueUnavailableReason`. DFS rows keep raw `bestEstimatedEv` for compatibility/debugging but set `showExpectedValue=false` and `displayExpectedValue=null`; sportsbook rows show EV only when odds or break-even pricing is present. Dynamic DFS side selection requires configured hit-probability and projection-edge gates and ranks by hit chance before projection edge; sportsbook rows keep price/EV-aware selection. | No | No | No |
| GET | `/api/players/search` | Production-safe read-only persisted player autocomplete search for Player Research. Supports `q` and `limit`; default limit is 5 and max is 10. Empty or one-character queries return an empty array. Results come only from persisted `players` rows. Ranking is token-aware: exact normalized full-name matches come first, then rows where all query tokens match the display name, then starts-with/contains display-name matches. Duplicate-looking rows with the same normalized display name are collapsed where practical so MLB/MLBLIVE duplicates do not fill the autocomplete; when duplicates exist, rows with current provider lines are preferred. Results include `playerId`, optional `effectivePlayerId`, `displayName`, `league`, `sport`, `team`, `position`, and `subtitle`. This endpoint does not call The Odds API, direct providers, or MLB APIs. | No | No | No |
| GET | `/api/player-research/current-lines` | Production-safe read-only current provider lines for a selected Player Research player. Supports `playerId` and optional `take`; max `take` is 100. Resolves safe equivalent identities such as maintained MLB alias links, then returns current persisted provider-line rows for the effective/canonical player identity. For Player Research only, it may bridge to an exact normalized same-name player row when exactly one persisted current-line-owning identity exists for that name. Ambiguous same-name line owners are not combined without an explicit equivalent identity signal. Duplicate persisted rows from repeated snapshots/platform props are collapsed by logical player/event/provider/prop/side/line before returning customer-facing lines; the audit object includes raw and duplicate current-line counts. Response includes `playerId`, `effectivePlayerId`, `lines`, and an audit object with `resolutionMode`. Does not call external APIs. | No | No | No |
| GET | `/api/markets/performance-summary` | Production-safe read-only strategy/provider performance summary from the durable `recommendation_snapshots` ledger and MLB `prop_results`. Supports `sport`, `providerCategory`, `provider`, `bookmaker`, `propType`, `strategyClassification`, `strategyTier`, `candidateOnly`, `fromDate`, `toDate`, `startDate`/`endDate` aliases, `minSampleSize`, `gradingMode=latestCompleted|latestGraded|currentSlate|rollingWindow`, `includeExcluded`, `includeNoResultDiagnostics`, `includeDiagnostics`, and `diagnosticSampleSize`. `latestCompleted` and `latestGraded` use MLB result import coverage when `mlb_result_import_date_states` has coverage rows; dates marked `Partial`, `NotStarted`, `PendingGames`, or `Failed` are skipped so tiny partial imports cannot headline hit rate/ROI. Existing `dataAvailability` coverage fields remain for compatibility. The clearer coverage summary lives at `diagnostics.resultCoverage` with `latestLedgerDate`, `latestResultsImportDate`, `latestCoverageStatus`, `selectedGradedDate`, `selectedGradedDateCoverageStatus`, `resultCoverageCompleteForSelectedDate`, `latestDateWasSkipped`, `skippedDates`, `skippedDateReasons`, and `warning`. `dataAvailability.resultCoverageComplete` remains the latest available result date completeness flag; use `diagnostics.resultCoverage.resultCoverageCompleteForSelectedDate` to know whether the date used for performance is complete. Performance-excluded recommendation snapshots are hidden by default; `includeExcluded=true` is for debug/admin review. Public groups include only determined outcomes (`Won`, `Lost`, `Pushed`) by default. No-result, unmatched, and pending rows remain in `dataAvailability` counts and debug diagnostics, but no-result-only groups are returned only when `includeNoResultDiagnostics=true` for debug/admin review. `dataAvailability` exposes explicit completed-date counts such as `importedResultsForCompletedDate`, `ledgerRowsForCompletedDate`, `candidateLedgerRowsForCompletedDate`, `filteredRowsForCompletedDate`, `gradedRowsForCompletedDate`, `noResultRowsForCompletedDate`, `unmatchedRowsForCompletedDate`, `pendingRowsForCompletedDate`, `wonRowsForCompletedDate`, `lostRowsForCompletedDate`, `pushedRowsForCompletedDate`, `matchedViaExactPlayerIdDateStatRows`, `matchedViaNormalizedPlayerNameDateStatRows`, `ambiguousFallbackRows`, `excludedFromPerformanceRows`, `includedExcludedRows`, and structural exclusion counts including `rowsExcludedUnsupportedProp` and `rowsExcludedEventDateMappingMismatch`. Hit rate uses wins / (wins + losses), excluding pushes. ROI uses graded outcomes, with pushes as 0-profit results; pending, no-result, and unmatched rows are excluded from hit-rate and ROI denominators. The ledger is written during The Odds refresh from surfaced candidate/watchlist/top-pick line-shopping groups and grades at read time by expected result date, canonical `players.id` or one unique normalized player-name fallback, and prop type id or normalized stat key, then evaluates the captured side/line against the actual result value. | No | No | No |
| GET | `/api/markets/performance-results` | Production-safe read-only determined recommendation results for one completed date. Supports `sport=MLB`, `date=YYYY-MM-DD`, and `includeDiagnostics=true`. Uses the same read-time matcher as `performance-summary`: compact performance stat keys, exact player/date/stat first, one unique normalized player-name/date/stat fallback only when unambiguous, and side/line only after the result row is found. Public `rows` include only determined `Won`, `Lost`, and `Pushed` outcomes. Row DTOs preserve raw `estimatedEv` for compatibility but include `providerCategory`, `isDfs`, `isSportsbook`, `showExpectedValue`, `displayExpectedValue`, and `expectedValueUnavailableReason`; DFS display clients should hide EV and use edge/trust/history context instead. `summary.noResultExcluded` counts no-result rows such as `ProviderLineForInactivePlayer` that were excluded from hit rate and simulated ROI. Diagnostics optionally include no-result reason counts/examples and match-method counts. | No | No | No |
| GET | `/api/debug/recommendations/ledger/audit` | Debug-only read audit for recommendation history. Supports `fromDate` and `toDate`. Reports counts by date for The Odds refresh runs, prediction runs, market top-pick snapshots, market line-shopping snapshots, recommendation ledger rows, and `prop_results`. Does not call providers. | No | No | No |
| GET | `/api/debug/player-research/lookup-audit` | Debug-only read audit for Player Research identity resolution. Supports `playerId` and optional `take`. Returns selected player identity, external refs, equivalent candidate IDs, current provider-line counts, prop-result history counts, effective player ID, and any reason current lines were unavailable. Does not call external APIs. | No | No | No |
| GET | `/api/debug/player-research/name-audit` | Debug-only read audit for Player Research name coverage. Supports `q`. Searches persisted local players, player external refs, current line-shopping snapshots, platform props, recommendation snapshots, and prop results for the normalized player name. Returns matching player rows, external refs, current provider/platform prop rows, per-player line/history/recommendation counts, and searched table counts. Does not call external APIs. | No | No | No |
| GET | `/api/debug/player-research/history-audit` | Debug-only read audit for Player Research prop-history resolution. Supports `playerId`, `propType`, `side`, and `line`. Reports selected/equivalent/exact-name player ids, external refs, normalized expected stat key and aliases, prop-result counts by identity/stat-key stage, nearby available stat keys, matching prop types, and latest matching `prop_results` rows. Does not call external APIs. | No | No | No |
| GET | `/api/debug/mlb/player-result-coverage` | Debug-only read audit for MLB player result coverage. Supports `q`. Reports matching player rows, `player_external_refs`, known MLB ids, prop-result counts by selected/exact-name/MLB-ref identities, pitcher-strikeout coverage, latest matching result rows, fuzzy near-name rows for diagnostics only, and import date coverage. This endpoint does not merge players and does not call external APIs; it is intended to distinguish missing MLB external refs from missing result imports or stat-key coverage. | No | No | No |
| GET | `/api/debug/mlb/regression-smoke` | Debug-only read smoke check for MLB identity/result plumbing. Reports resolved MLB sport/league lookup method, player and external-ref counts, recent/all-time MLB `prop_results`, latest MLB prop result date, latest MLB import date, snapshot counts, provider identity link counts, and warnings. It does not call MLB Stats API and does not write. | No | No | No |
| GET | `/api/debug/recommendations/ledger/result-coverage` | Debug-only read diagnostic for captured recommendation rows. Supports `date`, `sport=MLB`, optional `playerName`, optional `propType`, optional `take`, and optional `recommendationSnapshotId`. Compares captured ledger rows against persisted `prop_results` on the expected date and nearby dates, reports same-player/same-prop coverage counts, closest result candidates, and likely reasons such as `ProviderLineForInactivePlayer`, `PropTypeNotImportedForPlayer`, or `EventDateMismatch`. Raw MLB boxscore payloads are not persisted, so raw boxscore presence is reported as unknown. Does not call providers. | No | No | No |
| GET | `/api/debug/performance/result-match-audit` | Debug-only read row-by-row audit for performance/result matching misses. Supports `sport=MLB` and `date`. Reports captured recommendation identity, expected stat key, current matcher outcome, legacy exact-player-id outcome, no-result reason, player mapping status, same-date/player/name/stat coverage counts, and up to five closest `prop_results` candidates with why each did or did not match. This endpoint is intended to prove whether a public Performance miss is caused by player id mapping, normalized player name fallback, date normalization, stat-key normalization, or duplicate/ambiguous result rows. Does not call providers. | No | No | No |
| POST | `/api/debug/recommendations/performance/reset-baseline` | Debug-only soft exclusion for polluted Performance baselines. Uses the same post-filter row set as `/api/markets/performance-summary` for the requested flags, then selects from that visible performance set. Supports `sport=MLB`, `candidateOnly=true`, `topPickOnly=false`, `includeWatchlist=false`, `includeResearch=false`, `includeStale=false`, `includeExcluded=false`, `gradingMode=latestCompleted`, optional `propType`, optional `provider`, `gradedOnly=true`, `take=6`, `reason=PerformanceBaselineReset`, and `dryRun=true` by default. Dry run returns `selectionSource`, `filtersApplied`, visible public performance before/after reset, already-excluded skipped counts, recommendation snapshot ids, and row details that would be excluded. With `dryRun=false`, marks those `recommendation_snapshots` as excluded from public Performance with audit fields; it does not delete predictions, recommendation snapshots, or `prop_results`. | Yes, only with `dryRun=false` | No | No |
| POST | `/api/debug/recommendations/ledger/backfill` | Debug-only ledger backfill from existing persisted market snapshots. Supports `fromDate`, `toDate`, `source=marketSnapshots|both`, `dryRun=true` by default, and `execute=true` to write idempotently. Reports eligible rows, existing rows, rows to create, and skipped reasons such as missing canonical player id, missing prop type id, or missing expected result date. Does not call providers. | Optional, only with `execute=true` and `dryRun!=true` | No | No |
| GET | `/api/markets/prop-history` | Production-safe read-only prop history for Details modals and Player Research. Supports `sport=MLB`, production `playerId`, manual/debug `playerName` fallback, `propType` (`pitcher_strikeouts`, `batter_strikeouts`, `batter_walks`, `batter_hits`, `batter_total_bases`, or display names), `side`, `line`, `take`, `season`, and `provider`. Uses persisted MLB `prop_results` through equivalent player lookup plus compact performance-style stat-key matching, so labels such as `Pitcher Strikeouts`, `PITCHER_STRIKEOUTS`, hitter `Batter Strikeouts`/`STRIKEOUTS`, and `Batter Walks`/`WALKS` resolve together without mixing hitter strikeouts into pitcher-strikeout history. If the selected player id has no matching history, it may use one unambiguous exact-name history-owning identity; ambiguous same-name history owners are not combined without an explicit equivalent identity. Provider/bookmaker is not required to match history rows, and side/line are used only to evaluate cleared/missed outcomes. No provider calls and no live MLB calls. Returns chart-ready games, summary stats, push-aware results, `emptyReason`, and diagnostics including lookup mode, resolved player id, history source, and observation counts. | No | No | No |
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

Invoke-RestMethod "http://localhost:5299/api/players/search?q=paul&limit=5" |
  ConvertTo-Json -Depth 10

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
| GET | `/api/debug/the-odds/soccer/world-cup/provider-availability` | Debug-only read-only Soccer / World Cup provider availability probe for The Odds regions, bookmakers, and market keys. Requires `forceLive=true` before provider calls. Supports `slateDate`, comma-separated `regions` such as `us_dfs,us,us2,uk,eu,au`, `markets`, `bookmakers`, and `maxEvents` defaulting to 3. It never writes market rows or enables Soccer grading. Live calls are one event-list call plus one event-odds call per matched event and region checked. Returns aggregate and per-event bookmaker, market, outcome, configured-missing, unsupported-market, and structured provider-rejection diagnostics. | No | Yes only with `forceLive=true` | Yes only with live provider calls |
| POST | `/api/debug/the-odds/markets/refresh-current` | Debug/admin sport-neutral manual refresh entry point. For `sport=SOCCER`, dry-run and non-`forceLive` requests do not call providers or write rows. Soccer requests support `date` or `fromDate`/`toDate`, `maxEvents`, `includeSportsbook=true`, `includeDfs=true`, `providerCategory`, `region`/`regions`, and `markets`/`bookmakers` as either comma-separated strings or JSON arrays. `dryRun=true&forceLive=true` performs event discovery only, writes nothing, and returns `eventsDiscovered`, `eventsEligible`, `eventsToFetch`, `estimatedApiCalls`, skip reasons, and per-event request plans. With explicit Soccer / World Cup sport-key and market config, `dryRun=false&forceLive=true` can refresh multiple persisted Soccer provider events across the requested window while public Soccer grading remains disabled. Soccer defaults to sportsbook research context using `TheOddsApi:SoccerWorldCupSportsbookRegion` default `us`; persisted rows are `ProviderCategory=Sportsbook`, `entry.actionableForUser=false`, and not Entry Builder playable. Pass `includeDfs=true` or `providerCategory=DFS` to use the DFS region path, default `us_dfs`. The Odds provider 4xx rejections are returned as structured blocked responses with `providerRejectedRequest=true`, `blockReason=ProviderRejectedRequest`, `providerError.statusCode`, `providerError.operation`, `providerError.sanitizedRequestUrl`, `providerError.rawProviderBody`, and `diagnostics.likelyRejectedInputs`; API keys are redacted from URLs before diagnostics are returned or persisted. Successful Soccer refreshes that write zero rows include aggregate and per-event diagnostics for bookmaker counts, market keys, outcome counts, normalized prop counts, row create/update/skip counts, configured missing markets, unsupported returned markets, `noProviderMarketsReturned`, `providerMarketsReturnedButNoRowsMapped`, and skip reasons. | Yes unless dry-run or blocked | Yes only with `forceLive=true` | Yes only with live provider calls |
| POST | `/api/debug/the-odds/mlb/ingest-event` | Controlled one-event ingest. Supports `dryRun=true`. | Yes unless dry-run | Yes, The Odds API | Yes |
| POST | `/api/debug/the-odds/mlb/recommendations/refresh-current` | Explicit current-slate workflow: discovers today's MLB events using MLB/Eastern date logic, ingests configured The Odds markets, generates scoped predictions, and returns line-shopping strategy summaries. Supports `dryRun=true`, `forceLive=true`, `date`, `bookmakerPreset=default|expanded|dfsOnly|sportsbookOnly|all`, `includeCandidates`, `includeLineShopping`, `includeFullDiagnostics`, `includeDfs=true`, and `freshnessMinutes`. Live mode requires `forceLive=true`; otherwise the request is blocked before any provider call. Live mode uses `TheOddsApi:MaxConcurrentRequests` for bounded parallel event odds ingestion and returns detailed timing diagnostics for event fetch, market fetch, persistence, prediction generation, line-shopping, classification, and response build. With DFS enabled, it also requests configured DFS books through region `us_dfs`; `bookmakerPreset=dfsOnly` scopes the generated prediction and line-shopping output to DFS rows only. Dry-run remains no-call/no-write and reports the effective sportsbook/DFS books and estimated API-call shape. | Yes unless dry-run | Yes unless dry-run | Yes unless dry-run |
| GET | `/api/debug/the-odds/mlb/refresh-freshness` | Reads persisted The Odds MLB refresh run metadata. Supports `slateDate`, `bookmakerPreset`, and `freshnessMinutes`. Returns latest run, latest successful/failed run, freshness flags, stale reason, and latest refresh counts. | No | No | No |
| GET | `/api/debug/the-odds/mlb/ingestion-impact` | Summarizes persisted The Odds rows and prediction impact. | No | No | No |
| GET | `/api/debug/the-odds/mlb/line-shopping` | Compares persisted The Odds sourced markets across bookmakers. Supports `runId`, `freshnessMinutes`, `summaryOnly=true`, `topOnly=true`, `take`, and `includeBookmakerPrices=false`. Each group includes provider fields (`providerCategory`, `region`, `isActionableForUser`, `isIndicativeOdds`), entry compatibility fields (`entryCompatibilityGroup`, `entryProvider`, `entryProviderCategory`, `entryActionableForUser`, `supportedEntryTypes`, `allowedLegCounts`, `entryRuleWarning`), plus strategy-aware classification fields such as `strategyClassification`, `strategyTier`, `isCandidate`, `isAvoid`, `isWatchlist`, `marketShape`, and `riskTags`, plus `strategyClassificationSummary`. DFS candidate tiers require selected-side hit chance and projection edge to clear `MlbPredictionGeneration` gates; rows below the user-facing hit-probability minimum remain watchlist/research only. Sportsbook-context rows can still use positive `bestEstimatedEv` when true odds/break-even pricing is available. `summaryOnly` returns summary/diagnostics only; `topOnly` returns a small active top-picks payload for the dashboard. | No | No | No |
| POST | `/api/debug/predictions/generate/mlb` | Generates and persists the real baseline MLB prediction run from persisted MLB props, platform lines, and imported result history using `PredictionGenerationService`. This is the direct local path for validating production recommendation side selection without calling providers. | Yes | No | No |
| POST | `/api/debug/predictions/generate/mlb/the-odds` | Generates and persists real The Odds-scoped MLB predictions from fresh persisted The Odds platform props. Supports optional `eventId`, `bookmakers`, and `markets` query parameters. It uses `PredictionGenerationService`; it does not fetch The Odds live. | Yes | No | No |
| POST | `/api/debug/predictions/sample` | Creates a fake `SampleModel` run with synthetic sample predictions for debug/demo only. Side-selection audit excludes these sample runs by default so they do not pollute latest-run diagnostics. | Yes | No | No |
| GET | `/api/debug/predictions/mlb/side-selection-audit` | Read-only MLB prediction/market side-selection audit. Supports `latestRunOnly`, `excludeSampleRuns` default `true`, `includeSamples`, `maxSamples`, optional date filters, provider, and prop type. The latest-run selection ignores `SampleModel` fake runs by default and reports `latestPredictionRunModelName`, prediction-stage summary, `actualSelectedOverSamples`, `overEligibleButPassedSamples`, and `passReasonMissingSamples`. | No | No | No |
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

The scheduled worker and debug/admin live `refresh-current` path share the same live refresh service. Live writes are serialized by a process-local gate so snapshot persistence, provider line lifecycle updates, recommendation ledger upserts, and refresh run status writes do not overlap within one API process. Dry-run and blocked non-live requests are unchanged. On live refresh failure, pending side-effect entities are detached before saving the failed `TheOddsRefreshRun` state. Public `/api/markets/*` endpoints remain persisted/read-only and never call The Odds API.
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

## Soccer Results And Identity Diagnostics

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/soccer/results/api-sports/world-cup/market-join-preview` | Read-only bridge diagnostic comparing imported API-Sports World Cup `prop_results` to The Odds Soccer `platform_props` sportsbook context. Supports `fromDate`, `toDate`, optional `fixtureId`, optional `providerEventId`, `includeSamples`, and `maxSamples`. Reports event/player/prop join counts, blockers such as missing provider event id, missing API-Sports fixture ids on market rows, missing provider player/team context, prop mapping gaps, and matched/unmatched samples. Does not call providers, write rows, auto-link identities, or enable public Soccer grading. | No | No | No |
| GET | `/api/debug/soccer/results/api-sports/world-cup/grading-preview` | Read-only debug-only Soccer grading preview for rows already joinable between API-Sports World Cup `prop_results` and The Odds Soccer `platform_props`. Supports `fromDate`, `toDate`, optional `fixtureId`, optional `providerEventId`, `includeSamples`, and `maxSamples`. Compares actual API-Sports result values to persisted sportsbook context side/line and returns preview-only `OverWin`, `UnderWin`, `Push`, or `NoLine` results, grouped by bookmaker, market, and prop type. Does not write grades, create snapshots, call providers, auto-link identities, or enable public Soccer grading. | No | No | No |
| GET | `/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary` | Read-only Soccer World Cup grading coverage summary across resolved real The Odds Soccer provider-event/API-Sports fixture pairs. Supports `fromDate`, `toDate`, `includeFixtureDetails`, `includeSamples`, `maxFixtures`, and `maxSamplesPerFixture`. Uses persisted API-Sports `prop_results`, persisted The Odds Soccer `platform_props`, and approved The Odds alias links to report resolved-pair coverage, before/after alias join counts, graded preview rows, blockers, samples, best/worst coverage fixtures, unresolved provider events, result-only fixtures, and legacy/synthetic platform-only events. Does not import results, refresh odds, write aliases, write grades, call providers, or enable public Soccer grading. | No | No | No |
| GET | `/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap` | Read-only Soccer World Cup prop/stat mapping gap diagnostic across resolved real The Odds provider-event/API-Sports fixture pairs. Supports `fromDate`, `toDate`, `includeSamples`, and `maxSamples`. Uses persisted The Odds Soccer `platform_props`, persisted API-Sports `prop_results`, approved The Odds alias links, and DB-backed API-Sports provider stat mappings to report unresolved The Odds market keys, unmatched API-Sports stat keys, missing/unsupported/non-gradeable mapping categories, low-risk mapping candidates, imported-stat noise, and recommended next action. Does not write provider stat mappings, import results, refresh odds, call providers, write grades, or enable public Soccer grading. | No | No | No |
| GET | `/api/debug/soccer/results/api-sports/world-cup/stat-mapping-review` | Read-only Soccer World Cup provider stat mapping review for needs-confirmation/non-gradeable API-Sports mappings. Supports `fromDate`, `toDate`, `includeSamples`, and `maxSamples`. Lists mappings such as Cards, Goal + Assist, Shots Assisted, and Goals Allowed with API-Sports source fields, current confidence/gradeability, affected rows, whether The Odds returned matching platform markets, imported-stat noise counts, sample rows, risk assessment, recommendation, and explicit `publicGradingEligible=false`. Does not write mappings, approve mappings, call providers, write grades, or enable public Soccer grading. | No | No | No |
| GET | `/api/debug/soccer/world-cup/spike-status` | Read-only Soccer World Cup spike status and production guardrail summary. Supports `fromDate`, `toDate`, and `includeDetails`. Aggregates persisted The Odds ingestion coverage, fixture mapping coverage, alias coverage, grading coverage, and prop/stat mapping gap diagnostics into a `ResearchOnly` readiness response. Alias summary fields distinguish evaluated platform player names, total alias candidates, approved links, safe-link approvals, manual approvals, and remaining needs-review/no-candidate/ambiguous/new-safe buckets. Reports that public Soccer grading is disabled, Soccer Entry Builder usage is disabled, sportsbook context is research-only, and alias/stat mapping approval remains manual. Does not call providers, write rows, approve aliases, approve mappings, write grades, or enable public Soccer grading. | No | No | No |
| GET | `/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage` | Read-only coverage diagnostic for persisted real The Odds Soccer World Cup sportsbook provider events and whether each can be resolved to an API-Sports fixture. Supports `fromDate`, `toDate`, `includeResolved`, `includeUnresolved`, `includeSamples`, and `maxSamples`. Filters to The Odds context-v2 World Cup sportsbook rows, excludes synthetic provider event ids, applies date/team matching with World Cup team aliases, and reports resolved, no-candidate, ambiguous, date-mismatch, team-mismatch, and missing-result statuses. Does not import results, refresh odds, write mappings, call providers, or enable public Soccer grading. | No | No | No |
| GET | `/api/debug/soccer/the-odds/world-cup/ingestion-coverage` | Read-only diagnostic for The Odds Soccer World Cup provider-event ingestion coverage. Supports `fromDate`, `toDate`, `includeSamples`, and `maxSamples`. Reports real context-v2 sportsbook provider events, synthetic/legacy Soccer platform rows and exclusion reasons, API-Sports fixtures with imported results, latest persisted Soccer The Odds refresh-run diagnostics, and gap analysis explaining API-Sports fixtures with results but no real The Odds provider event. Does not refresh odds, import results, write mappings, call providers, or enable public Soccer grading. | No | No | No |
| GET | `/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates` | Read-only Soccer alias diagnostic comparing The Odds sportsbook-context player names for one `providerEventId` against API-Sports fixture result players for one `fixtureId`. Uses accent-insensitive normalized names, token subset matching, containment, and event/team context when available to surface `SafeAliasCandidate`, `NeedsReview`, `Ambiguous`, and `NoCandidate` rows. Does not write aliases, auto-approve identity links, call providers, or enable public Soccer grading. | No | No | No |
| POST | `/api/debug/soccer/player-identity/the-odds/api-sports/alias-link` | Debug-only dry-run-first persistence for The Odds Soccer player aliases to API-Sports/local player identities. Body requires `fixtureId` and `providerEventId`; `dryRun` defaults true, `minConfidence` defaults `0.94`, and only unique `SafeAliasCandidate` rows are eligible. Ambiguous, needs-review, no-candidate, below-confidence, and conflicting existing-link rows are reported as skipped. Writes approved `provider_entity_links` rows with `provider_key=the-odds-soccer` only when `dryRun=false`. Does not call providers, enable public Soccer grading, or merge players. | Yes only with `dryRun=false` | No | No |
| POST | `/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve` | Debug-only one-at-a-time manual approval for reviewed The Odds Soccer `NeedsReview` alias candidates. Body requires `fixtureId`, `providerEventId`, `platformPlayerName`, `apiSportsPlayerId`, `localPlayerId`, optional `reason`, and `dryRun` default true. The endpoint recomputes the diagnostic candidate, rejects ambiguous/no-candidate/safe rows, rejects mismatched candidate ids, is idempotent, and writes one approved `provider_entity_links` row only when `dryRun=false`. Does not bulk approve, call providers, merge players, write grades, or enable public Soccer grading. | Yes only with `dryRun=false` | No | No |
| GET | `/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch` | Read-only batch alias diagnostic across all resolved real The Odds Soccer provider-event/API-Sports fixture pairs in a date range. Supports `fromDate`, `toDate`, `includeSamples`, `maxFixtures`, `maxSamples`, and `minConfidence`. Reuses the single-pair alias matcher, groups candidates by `providerEventId` and `fixtureId`, deduplicates display candidates for review, reports already-linked and new safe candidates, and estimates coverage impact. Does not write aliases, call providers, merge players, or enable public Soccer grading. | No | No | No |
| POST | `/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch` | Debug-only dry-run-first batch persistence for safe The Odds Soccer aliases across resolved provider-event/API-Sports fixture pairs. Body supports `fromDate`, `toDate`, `dryRun` default true, `minConfidence` default `0.94`, `maxLinks`, `includeExactNormalizedMatches`, and `includeTokenSubsetMatches`. Only `SafeAliasCandidate` rows are eligible; already-linked, conflicting, ambiguous, needs-review, no-candidate, and below-confidence rows are skipped and reported. Writes event-scoped `provider_entity_links` rows only when `dryRun=false`. Does not call providers, merge players, write grades, or enable public Soccer grading. | Yes only with `dryRun=false` | No | No |
| GET | `/api/debug/soccer/player-identity/api-sports/world-cup/review-queue` | Read-only API-Sports World Cup player identity review queue with confidence/action filters. Supports `safeOnly=true` to return only candidate rows with no blockers; `MissingTeamContext` remains review-only. | No | No | No |
| GET | `/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export` | Compact read-only export of explicit provider/canonical player pairs for manual review, separated into `safeCandidates`, `needsReviewCandidates`, `noCandidate`, and `ambiguousConflict`. | No | No | No |
| POST | `/api/debug/soccer/player-identity/api-sports/world-cup/approve-link` | Debug-only dry-run-default approval for one explicit API-Sports Soccer provider player link. Single approval remains possible for reviewed rows with blockers, but the response warns about review blockers. | Yes unless dry-run | No | No |
| POST | `/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links` | Debug-only dry-run-default bulk approval for explicit reviewed provider/canonical player pairs. Supports all-or-nothing and partial writes. Blocks rows with review blockers by default; `allowNeedsReview=true` is required for an explicit exception. | Yes unless dry-run | No | No |
| POST | `/api/debug/soccer/player-identity/api-sports/world-cup/reject-link` | Debug-only dry-run-default rejection for one API-Sports Soccer provider player link. | Yes unless dry-run | No | No |

## MLB Results

| Method | Path | Purpose | Writes | External API | Credits |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/debug/results` | Lists persisted prop results. | No | No | No |
| GET | `/api/debug/results/{id}` | Gets one persisted prop result. | No | No | No |
| GET | `/api/debug/mlb-results` | Read-only MLB Stats API result provider output. | No | Yes, MLB Stats API | No |
| POST | `/api/debug/results/import/mlb` | Imports MLB results for one date into `prop_results`, then updates per-date coverage fields. Supports `dryRun` and `force` in the body; dry run audits without writing. | Yes unless `dryRun=true` | Yes, MLB Stats API unless dry run | No |
| GET | `/api/debug/results/import/mlb/status` | Import status for a date and worker state/status shape. Date status includes coverage fields: `completedGamesExpected`, `completedGamesImported`, `importedResults`, `distinctPlayers`, `distinctPropTypes`, `coverageStatus`, `coverageReason`, `latestImportAttemptUtc`, `latestSuccessfulRepairUtc`, and `latestErrorMessage`. `latestWorkerCycle` exposes the last persisted worker cycle with per-date `selectedReason`, row/player/prop counts, final `coverageStatus`, `outcomeCategory`, and error text when present. `Partial`, `PendingGames`, and `NoGames` are non-fatal worker outcomes unless an exception occurred. | No | No | No |
| GET | `/api/debug/results/import/mlb-range/status` | Read-only worker/range diagnostics for operations checks. Returns pending backfill dates and the latest persisted worker cycle with per-date outcome categories, counts, coverage reasons, and errors. | No | No | No |
| POST | `/api/debug/results/import/mlb-range` | Date-range MLB result backfill. | Yes | Yes, MLB Stats API | No |
| GET | `/api/debug/results/import/mlb/coverage-audit` | Read-only per-date MLB result coverage audit. Supports `fromDate`, `toDate`, and `includeDetails=true`. Returns counts for complete, partial, missing/not-started, pending, no-games, and failed dates plus recommended repair actions. Uses persisted rows and fallback coverage thresholds; does not call providers. | No | No | No |
| POST | `/api/debug/results/import/mlb/repair-missing` | Dry-run-first repair for missing or partial MLB result imports. Body supports `fromDate`, `toDate`, `dryRun=true`, `force=false`, `repairPartial=true`, and `repairMissing=true`. Real mode calls the existing MLB import path only for selected repair dates, stamps repair metadata, and re-audits coverage. | Yes unless `dryRun=true` | Yes, MLB Stats API in real mode | No |
| GET | `/api/debug/results/import/mlb/coverage` | Historical MLB result coverage. | No | No | No |
| GET | `/api/debug/mlb/pitcher-strikeouts/import-coverage` | Pitcher strikeout import coverage diagnostic. | No | No | No |
| POST | `/api/debug/mlb/pitcher-strikeouts/backfill` | Safe repair/backfill for Pitcher Strikeouts. Dry-run by default. | Yes unless dry-run | MLB Stats API through import logic | No |

Examples:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/results/import/mlb" -ContentType "application/json" -Body '{"date":"2026-06-12"}'
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/results/import/mlb/coverage-audit?fromDate=2026-06-19&toDate=2026-06-23&includeDetails=true"
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/results/import/mlb/repair-missing" -ContentType "application/json" -Body '{"fromDate":"2026-06-23","toDate":"2026-06-23","dryRun":true,"repairPartial":true,"repairMissing":true}'
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
| GET | `/api/debug/predictions/mlb/side-selection-audit` | Read-only MLB side-selection audit from persisted predictions, market snapshots, and recommendation ledger rows. Supports `date`, `fromDate`, `toDate`, `latestRunOnly=true`, `includeSamples=true`, `maxSamples`, `provider`, and `propType`. Reports prediction-stage counts even when no market snapshots exist, including projection-above/below-line counts, prediction Over/Under/Pass distribution, reconstructed model Over/Under/Push probabilities, diagnostic neutral-price Over/Under EV, positive-EV pass/side mismatch flags, pass gate summaries, pass reason counts, failed gate counts, production threshold values, and side-candidate before/after gate details. Sample groups include `selectedUnderSamples`, `passSamples`, `overPositivePassSamples`, `overBeatsUnderButPassSamples`, `rejectedByMinEdgeSamples`, `highestOverEvSamples`, `projectionAboveLineButSelectedUnderSamples`, and `rowsWhereOverProbabilityGreaterThanUnderSamples`. It also reports downstream side distribution by stage, selected-side probability, implied probability, probability edge, EV, missing side selections, and side continuity between line-shopping snapshots and recommendation snapshots. Production prediction generation uses projection edge thresholds; diagnostic neutral-price EV is read-only and is not a production selection gate. Use this before changing recommendation side-selection behavior when Over/Under distribution looks suspicious. | No | No | No |

Examples:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5299/api/debug/predictions/generate/mlb/the-odds?bookmakers=draftkings,fanduel,betmgm&markets=batter_hits,batter_total_bases,pitcher_strikeouts"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/predictions/edge-analysis"
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/predictions/mlb/side-selection-audit?latestRunOnly=true&includeSamples=true&maxSamples=200"
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
| POST | `/api/debug/mlb-player-mappings/approve` | Manual MLB player mapping approval. Accepts `playerId`, `mlbPlayerId`, and `displayName`. Idempotent when the same mapping already exists, refuses an MLB id already mapped to another player, and refuses adding a second different MLB id to the same player. It creates only a `player_external_refs` row for provider `mlb`; it does not merge players or call external APIs. | Yes | No | No |
| GET | `/api/debug/mlb-player-mappings/provider-line-repair-candidates` | Local-only audit for MLB/MLBLIVE provider-line players with current provider lines or active platform props but no `provider=mlb` ref. Uses accent-insensitive compact name normalization to report duplicate MLB identity candidates, existing refs, result counts, safe auto-approval status, and conflict/manual-review reasons. Does not call external APIs and does not merge rows. | No | No | No |
| POST | `/api/debug/mlb-player-mappings/provider-line-repair/auto-approve` | Dry-run by default local-only auto-approval for safe provider-line MLB mapping repairs. Approves only unambiguous candidates with exactly one unclaimed local/legacy MLB id on the provider-line player and no conflicting duplicate MLB refs. Accepts `dryRun`, `take`, `approvedBy`, and `reason`. Does not call external APIs and does not merge rows. | Yes when `dryRun=false` | No | No |
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
