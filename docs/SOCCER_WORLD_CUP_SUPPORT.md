# Soccer / World Cup Provider-Line Support

Phase 1 supports persisted provider-line reads for Soccer / World Cup without adding soccer result grading.

## Terminology

- Sport: Soccer
- Competition: World Cup
- Provider labels and raw source keys should be preserved when available.
- World Cup is not modeled as the sport. Future soccer competitions should reuse the Soccer sport foundation.

## Supported Public Reads

The production market endpoints accept `sport=SOCCER` for persisted snapshot reads:

- `/api/markets/freshness?sport=SOCCER`
- `/api/markets/top-picks?sport=SOCCER`
- `/api/markets/line-shopping?sport=SOCCER`

These endpoints are read-only and do not call external APIs. They return Soccer / World Cup rows only when persisted snapshots exist.

Phase 1 public app behavior is intentionally narrow:

- Line Shopping may surface persisted World Cup sportsbook context with `sport=SOCCER&competition=World Cup`.
- If Soccer line-shopping snapshots do not exist because no Soccer prediction-backed groups were generated, public Line Shopping builds read-only research groups directly from persisted The Odds `platform_props` context rows.
- Top Picks remains DFS-scoped in the customer app and must not present Soccer sportsbook context as model recommendations.
- Soccer rows are always returned as research-only sportsbook context: `providerCategory=Sportsbook`, `entryActionableForUser=false`, `sportsbookContextResearchOnly=true`, `resultGradingSupported=false`, and `publicSoccerGradingEnabled=false`.
- Soccer rows include unavailable reasons for grading and entry actionability. Model hit chance and EV should render as unavailable when prediction inputs are not present; do not show fake zeros.
- Entry Builder must not treat Soccer sportsbook context as playable DFS legs.

## Manual Refresh Workflow

A sport-neutral debug/admin endpoint exists for manual refresh planning:

- `POST /api/debug/the-odds/markets/refresh-current`

Example dry run:

```json
{
  "sport": "SOCCER",
  "competition": "World Cup",
  "dryRun": true,
  "forceLive": false
}
```

Safety rules:

- `dryRun=true` never calls external APIs and never writes rows.
- `forceLive=true` is required before any live provider call can be considered.
- Soccer scheduled refresh is not enabled.
- Soccer live ingestion is blocked until the provider sport key and market list are configured explicitly.
- The app does not guess The Odds API Soccer / World Cup sport keys.

Required config placeholders:

- `TheOddsApi:SoccerWorldCupSportKey`
- `TheOddsApi:SoccerWorldCupMarkets`

If the config is missing, the endpoint returns `MissingSoccerWorldCupProviderConfig` and `callsExternalApis=false`.
If the config is present, `dryRun=false` plus `forceLive=true` can refresh Soccer / World Cup market rows and persist provider event/team context. This is still debug/admin-only; Soccer scheduled refresh and public Soccer grading remain disabled.

Soccer / World Cup manual refresh defaults to sportsbook context because The Odds has shown World Cup player prop availability in region `us` and no DFS availability in region `us_dfs` for tested events. The default Soccer write request uses:

- provider category: `Sportsbook`
- region: `TheOddsApi:SoccerWorldCupSportsbookRegion`, default `us`
- `entry.actionableForUser=false`
- research-only entry warning: sportsbook context is not directly playable in the Entry Builder

To explicitly probe or ingest the DFS context path, pass `includeDfs=true` or `providerCategory=DFS`; that uses `TheOddsApi:SoccerWorldCupDfsRegion`, default `us_dfs`. Soccer sportsbook context rows must not be treated as DFS-playable.

Optional Soccer region config:

- `TheOddsApi:SoccerWorldCupSportsbookRegion`, default `us`
- `TheOddsApi:SoccerWorldCupDfsRegion`, default `us_dfs`

If The Odds rejects a Soccer / World Cup request, the manual refresh endpoint returns a structured non-500 response instead of treating the provider 4xx as an unhandled application failure:

```json
{
  "success": false,
  "blocked": true,
  "providerRejectedRequest": true,
  "blockReason": "ProviderRejectedRequest",
  "callsExternalApis": true,
  "wouldWriteRows": false,
  "sport": "SOCCER",
  "competition": "World Cup",
  "resultGradingSupported": false,
  "providerConfig": {
    "configuredSportKey": "soccer_fifa_world_cup",
    "configuredMarkets": "player_shots",
    "requiredConfig": [
      "TheOddsApi:SoccerWorldCupSportKey",
      "TheOddsApi:SoccerWorldCupMarkets"
    ]
  },
  "providerError": {
    "statusCode": 422,
    "operation": "FetchEventOdds",
    "sanitizedRequestUrl": "https://...apiKey=***...",
    "rawProviderBody": "{\"message\":\"...\"}"
  },
  "diagnostics": {
    "message": "The Odds API rejected the Soccer / World Cup market request. Review the sanitized request URL, provider response body, and configured sport/market parameters before retrying.",
    "likelyRejectedInputs": {
      "sportKey": "soccer_fifa_world_cup",
      "markets": "player_shots",
      "bookmakers": null,
      "regions": "us_dfs",
      "eventId": "provider-event-id"
    }
  }
}
```

The `sanitizedRequestUrl` must never expose the API key. The provider body is included for debug diagnostics only, sanitized and capped before it is returned or persisted in application error metadata.

Successful Soccer refreshes that write zero rows include explicit zero-row diagnostics. The response reports aggregate counts such as `eventsFetched`, `eventsWithBookmakers`, `eventsWithMarkets`, `marketsReturned`, `configuredMarketsRequested`, `configuredMarketsReturned`, `configuredMarketsMissing`, `unsupportedMarketsReturned`, `mappedMarketsReturned`, `normalizedPropsCount`, `noProviderMarketsReturned`, `providerMarketsReturnedButNoRowsMapped`, and `likelyReason`.

The `diagnostics.events[]` array reports each fetched event with `providerEventId`, `homeTeam`, `awayTeam`, `commenceTimeUtc`, requested market/region/bookmaker parameters, whether the event odds HTTP request succeeded, returned bookmaker keys, returned market keys, outcome counts, normalized prop count, row create/update/unchanged/skip counts, and skip reasons. This distinguishes common successful-zero-row cases:

- The event odds response had no bookmakers.
- The response had bookmakers but no markets.
- The response returned markets that were not in `TheOddsApi:SoccerWorldCupMarkets`.
- The response returned configured markets with zero outcomes.
- The adapter normalized outcomes but no provider rows were mapped.

`soccerMarketsUnsupported` mirrors returned raw provider market keys that are outside the configured Soccer market list. These diagnostics are descriptive only; they do not guess new market keys or enable public Soccer grading.

## Provider Availability Probe

Use the read-only availability endpoint before changing the Soccer write refresh configuration:

- `GET /api/debug/the-odds/soccer/world-cup/provider-availability`

Query parameters:

- `slateDate` optional, defaults to current UTC date.
- `regions` optional, defaults to `us_dfs`; comma-separated values such as `us_dfs,us,us2,uk,eu,au` are allowed.
- `markets` optional, defaults to `TheOddsApi:SoccerWorldCupMarkets`.
- `bookmakers` optional.
- `maxEvents` optional, defaults to `3` and is capped at `10`.
- `forceLive=true` is required for external calls.

The endpoint is read-only and never writes `platform_props`, snapshots, refresh runs, recommendations, or Soccer grading rows. Live call count is:

```text
1 event-list call + (matched events checked * regions checked)
```

For example, `maxEvents=3&regions=us_dfs` can make up to 4 live calls. `maxEvents=3&regions=us_dfs,us,us2,uk,eu,au` can make up to 19 live calls.

The response includes `eventsFetched`, `eventsMatchedToSlate`, `eventsWithBookmakers`, `eventsWithMarkets`, `regionsChecked`, `marketsChecked`, `hasAnyProviderLines`, `estimatedLiveCalls`, `actualLiveCalls`, and `recommendedAction`. Each `events[]` item reports the event id, teams, start time, requested region/bookmakers/markets, returned bookmaker keys, returned market keys, configured missing/returned markets, unsupported returned markets, outcome counts, player outcome counts, and structured `providerRejectedRequest` details if The Odds returns a 4xx.

Example:

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/debug/the-odds/soccer/world-cup/provider-availability?slateDate=2026-06-21&regions=us_dfs,us&markets=player_shots&maxEvents=3&forceLive=true" |
  ConvertTo-Json -Depth 60
```

## Prop Normalization

Known World Cup categories are normalized so provider lines can be displayed consistently:

- Goals
- Assists
- Goal + Assist
- Shots
- Shots On Target
- Passes Attempted
- Tackles
- Crosses
- Clearances
- Fouls
- Fouls Drawn
- Cards
- Offsides
- Goalie Saves
- Goals Allowed
- Attempted Dribbles
- Shots Assisted

Provider-scored fantasy props and combo props are recognized but are not gradeable in phase 1:

- Outfield Fantasy Score
- Goalie Fantasy Score
- Goalie Saves (Combo)
- Passes Attempted (Combo)
- Shots (Combo)
- Shots On Target (Combo)
- Goals Allowed (Combo)
- Clearances (Combo)
- Goals (Combo)

## Soccer Result Persistence Spike

API-Sports Football has a debug-only single-fixture result import spike for World Cup player stats:

- `POST /api/debug/soccer/results/api-sports/import-fixture`
- `GET /api/debug/soccer/results/api-sports/import-fixture/status?fixtureId=...`
- `POST /api/debug/soccer/results/api-sports/world-cup/backfill-completed`
- `GET /api/debug/soccer/results/api-sports/world-cup/import-status`
- `GET /api/debug/soccer/results/api-sports/world-cup/coverage`
- `GET /api/debug/soccer/results/api-sports/world-cup/market-join-preview`
- `GET /api/debug/soccer/results/api-sports/world-cup/grading-preview`
- `GET /api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary`
- `GET /api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage`
- `GET /api/debug/soccer/the-odds/world-cup/ingestion-coverage`
- `GET /api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates`
- `POST /api/debug/soccer/player-identity/the-odds/api-sports/alias-link`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/coverage`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/candidates`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/link-preview`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/platform-context`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/review-queue`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export`
- `POST /api/debug/soccer/player-identity/api-sports/world-cup/approve-link`
- `POST /api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links`
- `POST /api/debug/soccer/player-identity/api-sports/world-cup/reject-link`
- `GET /api/debug/identity/provider-links`

The import endpoint is force-live gated, supports `dryRun=true`, and writes only supported mappings for the requested fixture when `dryRun=false`. It reuses `prop_results` with `ExternalSource=api-sports-football`; no Soccer grading is enabled globally.

The World Cup backfill endpoint is intentionally narrow: API-Sports Football only, World Cup only, league `1`, season `2026`, completed fixtures only, and no past seasons. It discovers fixtures by date range and imports only fixtures with API-Sports status `FT` / `Match Finished`; scheduled, live, postponed, and canceled fixtures are skipped with reasons. `dryRun=true` remains the default, and `forceLive=true` is required for discovery because API-Sports calls are external even when no rows are written.

`import-status` is persisted-only unless `forceLive=true`. Persisted-only status reports imported fixture/result rows and `coverageByDate` without API-Sports calls. Force-live status adds discovered fixture count, completed fixture count, skipped pending fixture count, missing completed fixture count, and per-date API call estimates. `backfill-completed` returns the same `coverageByDate` shape plus previewed/written result rows, failed fixtures, and per-fixture import diagnostics.

Broader current-year Soccer history is guarded debug-only. `GET /api/debug/soccer/results/api-sports/historical/coverage-status` reports configured API-Sports Football historical competitions and local imported coverage for Goals, Assists, Shots, and Shots On Target. `POST /api/debug/soccer/results/api-sports/historical/backfill-completed` can dry-run one configured competition with `forceLive=true` to discover completed fixtures and preview player-stat rows for those same four stats. `dryRun=false&forceLive=true` writes rows idempotently by `external_source_identifier=api-sports-football:{fixtureId}:{playerId}:{statKey}`. Requests are limited to `maxDays` default `7`; larger ranges are blocked before API calls. The default configured set is EPL, La Liga, Bundesliga, Serie A, Ligue 1, MLS, and Champions League. Public Soccer grading, Soccer predictions, and Soccer Entry Builder remain disabled.

Supported mappings include goals, assists, shots, shots on target, passes attempted, tackles, fouls, fouls drawn, offsides, goalie saves for goalkeepers, and attempted dribbles. Conservative mappings such as Goal + Assist, Cards, Goals Allowed, and Shots Assisted are flagged as `NeedsConfirmation` in import diagnostics.

Soccer prop type display names are not unique because period-specific and combo markets can share customer-facing names. API-Sports result imports resolve prop types by Sport + canonical `statKey`, not display name.

Unsupported mappings are skipped for now: crosses, clearances, fantasy score props, and combo props.

Imported API-Sports result players are not automatically linked to The Odds/platform player identities. The player identity diagnostics are read-only and compare persisted API-Sports World Cup result players against local Soccer players, platform props, recommendation snapshots, and player external refs. They report already linked identities, high-confidence candidates, ambiguous names, conflicts, unmatched players, and blockers. They do not write aliases, merge players, or enable public Soccer grading.

The platform-context diagnostic reports whether existing persisted Soccer platform/snapshot rows contain team, opponent, event, provider event id, provider player id, bookmaker, and competition context that could support identity matching. New The Odds Soccer market refreshes persist event-level context with `contextSchemaVersion=the-odds-provider-context-v2`: provider event id, event name, home team, away team, event start time, bookmaker/provider key, competition, market key, outcome line metadata, and player display name from the outcome description. Old rows remain pre-context or partial until Soccer market refresh is rerun.

The Odds Soccer player prop payload does not currently provide a stable provider player id or the player's team on the outcome row. Treat name-only Soccer candidates and rows with `MissingTeamContext` or `EventTeamOnly` as manual review items. Event-level home/away context helps review and event matching, but it is not by itself safe for bulk player approval.

API-Sports result imports now create durable `provider_entity_links` Candidate rows for imported Soccer player identities. Existing `player_external_refs` remains supported for player-only compatibility; `provider_entity_links` is the reusable provider identity inventory and review layer going forward. See `docs/PROVIDER_IDENTITY_LINKING.md`.

Debug-only manual review endpoints can approve or reject API-Sports Soccer provider player links. They default to `dryRun=true`, validate Soccer/provider/canonical player constraints, and never merge player rows. Bulk approval accepts only explicit reviewed `providerEntityLinkId` + `canonicalPlayerId` pairs and supports all-or-nothing or partial-write behavior. By default, bulk approval blocks rows with review blockers such as `MissingTeamContext`; `allowNeedsReview=true` is required for an explicit reviewed exception. Approved links are used only by debug coverage diagnostics for now; public Soccer grading remains disabled.

The Soccer market refresh also creates normalized `games` / `teams` from The Odds event home/away/start context for newly ingested Soccer rows. Raw provider context remains in `platform_props.metadata`; stable provider event/team/player link promotion should still go through `provider_entity_links` review. Public Soccer grading remains disabled.

`/api/debug/soccer/results/api-sports/world-cup/market-join-preview` is a read-only bridge diagnostic for one date range or event. It compares imported API-Sports `prop_results` to The Odds Soccer `platform_props` sportsbook context by event date/team context, normalized player name, and canonical prop/stat key. It returns join counts, matched/unmatched samples, and blockers such as missing provider event ids, missing API-Sports fixture ids on market rows, missing provider player/team context, prop mapping gaps, player-name mismatch, and event-date mismatch. Use `fixtureId` and `providerEventId` to isolate known events such as Uruguay vs Cape Verde before building any debug-only Soccer grading preview.

`/api/debug/soccer/results/api-sports/world-cup/grading-preview` uses the same read-only join bridge, then compares the joined API-Sports actual value to the persisted The Odds sportsbook side/line. It returns preview-only `OverWin`, `UnderWin`, `Push`, or `NoLine` rows plus grouped summary counts. It does not write grades, create snapshots, auto-link players, or enable public Soccer grading. Sportsbook context rows remain research-only and not Entry Builder playable.

`/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary` evaluates persisted coverage across resolved real The Odds Soccer provider-event/API-Sports fixture pairs. It filters platform rows to context-v2 The Odds Soccer World Cup sportsbook context and buckets unresolved provider events, API-Sports result-only fixtures, and legacy/synthetic platform-only event ids separately from graded coverage. The response reports resolved-pair before/after alias join counts, graded preview rows, top blockers, sample graded rows, sample unjoinable platform rows, best/worst coverage fixtures, `knownWorkingPairDetected`, and a recommended next action. It never imports results, refreshes odds, writes aliases, writes grades, calls providers, or enables public Soccer grading.

`/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap` explains remaining Soccer prop/stat mapping blockers after event and player alias joins are resolved. It compares ungraded The Odds sportsbook context rows against API-Sports fixture result stats, reports missing The Odds market canonical mappings, missing API-Sports stat mappings, mapping-exists-but-not-gradeable rows, canonical mismatches, and imported-stat noise. It is diagnostic only: it does not create provider stat mappings, write grades, import results, refresh odds, or enable public Soccer grading.

`/api/debug/soccer/results/api-sports/world-cup/stat-mapping-review` reviews the exact API-Sports Soccer mappings that are NeedsConfirmation, Unsupported, non-gradeable, or not public-gradeable. It reports current source fields, affected rows, The Odds returned platform-row counts, imported-stat noise, why the mapping is non-gradeable today, and a recommendation such as `KeepNonGradeable`, `ManualApprovalCandidate`, `NeedsMoreProviderEvidence`, or `IgnoreAsImportedNoise`. It is explicitly read-only and always reports `publicGradingEligible=false`.

`/api/debug/soccer/world-cup/spike-status` is the roll-up status endpoint for the Soccer World Cup spike. It is read-only, never calls providers, and reports `productionReadiness.status=ResearchOnly`. It aggregates persisted ingestion, fixture mapping, alias, grading coverage, and prop/stat mapping diagnostics, then restates the hard guardrails: public Soccer grading is disabled, Soccer rows are not Entry Builder playable, sportsbook context is research-only, and alias/stat mapping approval remains manual.

The spike-status alias summary intentionally separates `aliasPlatformPlayerNamesEvaluated`, `aliasCandidateCount`, `aliasLinksApproved`, `aliasSafeCandidatesApproved`, `aliasManualApprovalsApproved`, and remaining buckets such as `aliasNeedsReviewRemaining`, `aliasNoCandidateRemaining`, `aliasAmbiguousRemaining`, and `aliasNewSafeCandidatesRemaining`. Do not treat evaluated player-name counts as unresolved alias work.

`/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage` is the read-only event bridge diagnostic for scaling beyond known single-event pairs. It lists persisted real The Odds Soccer World Cup sportsbook provider events, excludes synthetic ids such as `|balanced`, `|alternate`, and `SCR_game`, and reports whether each provider event resolves to exactly one API-Sports fixture using event date, kickoff tolerance, normalized team names, and known World Cup team aliases such as Cape Verde / Cape Verde Islands and USA / United States. It returns resolved, unresolved, ambiguous, no-candidate, date-mismatch, team-mismatch, and missing-result counts without importing results, refreshing odds, writing mappings, or enabling public Soccer grading.

`/api/debug/soccer/the-odds/world-cup/ingestion-coverage` explains why fixture mapping coverage may only have one real provider event. It audits persisted The Odds Soccer World Cup sportsbook platform rows, buckets legacy/synthetic Soccer rows with exclusion reasons, compares against imported API-Sports fixture/result coverage, and surfaces the latest persisted Soccer The Odds refresh-run diagnostics when available. Use this before rerunning Soccer refreshes so the blocker is clear: missing context schema, event targeting, region/bookmaker filters, no sportsbook rows returned, or skipped ingestion. The endpoint is read-only and does not call providers.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates` is a read-only alias review helper for one The Odds provider event and one API-Sports fixture. It compares The Odds sportsbook-context player display names to API-Sports fixture result player names using accent-insensitive normalized names, token subset matching, and conservative ambiguity blocking. It is intended to surface long-form-name aliases such as The Odds long names versus shorter API-Sports names. It does not write aliases, approve links, or enable Soccer grading.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-link` is the dry-run-first persistence step for reviewed alias candidates. It defaults to `dryRun=true`, writes only unique `SafeAliasCandidate` rows above `minConfidence`, and stores approved debug links in `provider_entity_links` with `provider_key=the-odds-soccer`. Ambiguous, needs-review, no-candidate, below-confidence, and conflicting existing-link rows are reported as skipped. Approved alias links can improve the debug market-join and grading-preview diagnostics, but they do not enable public Soccer grading, write grades, merge players, or call external APIs.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve` is the explicit one-at-a-time path for reviewed `NeedsReview` candidates. It defaults to `dryRun=true`, recomputes the current diagnostic candidate, requires the submitted `apiSportsPlayerId` and `localPlayerId` to match that candidate, rejects ambiguous/no-candidate rows, and writes at most one approved event-scoped alias link when `dryRun=false`. Use this only after manual review of a specific player identity.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch` expands alias review across all resolved The Odds/API-Sports fixture pairs for a date range. It reuses the single-pair alias matcher, groups rows by provider event and fixture, deduplicates display candidates for review, reports already-linked and new safe candidates, and estimates the possible join/grade coverage impact. It is read-only.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch` is the dry-run-first batch persistence step. It defaults to `dryRun=true`, only persists `SafeAliasCandidate` rows above `minConfidence`, skips already-linked and conflicting rows, and never writes `NeedsReview`, ambiguous, no-candidate, or below-confidence rows. Because The Odds alias links are currently event-scoped, the batch write may create one approved link per provider event even when the same long-form name maps to the same API-Sports player across fixtures.

## Public History Still Disabled

Soccer result grading and public player prop history are intentionally unavailable until the Soccer import workflow is promoted beyond debug-only and grading semantics are explicitly enabled.

- `/api/markets/prop-history?sport=SOCCER...` returns `Soccer history is not available yet.`
- Model Performance should not include Soccer as graded performance.
- Soccer fantasy score and combo props must not be graded from fake or inferred data.
- API-Sports Football is the current result-provider spike candidate. See `docs/SOCCER_RESULTS_PROVIDER_SPIKE.md`.

## The Odds Multi-Event Refresh

The sport-neutral debug refresh endpoint can now plan or ingest Soccer / World Cup sportsbook context across a date window instead of only the latest slate date:

- endpoint: `POST /api/debug/the-odds/markets/refresh-current`
- request window: `date` for one day, or `fromDate` / `toDate` for a range
- event cap: `maxEvents`, default 10, hard-limited to 100
- Soccer default context: sportsbook research rows, `providerCategory=Sportsbook`, region from `TheOddsApi:SoccerWorldCupSportsbookRegion` default `us`
- DFS probe path: pass `includeDfs=true` or `providerCategory=DFS`; this uses `TheOddsApi:SoccerWorldCupDfsRegion` default `us_dfs`
- `markets` and `bookmakers` may be comma-separated strings or JSON arrays

Safety behavior:

- `dryRun=true&forceLive=false` does not call The Odds and estimates the maximum request shape.
- `dryRun=true&forceLive=true` calls only event discovery, writes nothing, and returns `eventsDiscovered`, `eventsEligible`, `eventsToFetch`, `estimatedApiCalls`, skip reasons, and per-event request plans.
- `dryRun=false&forceLive=true` writes provider context rows for the selected events.
- Soccer sportsbook rows remain research-only and non-entry-actionable. Public Soccer grading remains disabled.
- Public Soccer market reads may expose persisted market context, but response rows are forced to `entryActionableForUser=false`, `sportsbookContextResearchOnly=true`, and `entryProviderCategory=Sportsbook` as a backstop. Entry Builder must not treat Soccer sportsbook context as playable legs.

## Verification

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/markets/freshness?sport=SOCCER" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "$base/api/markets/top-picks?sport=SOCCER&take=10" |
  ConvertTo-Json -Depth 30

Invoke-RestMethod "$base/api/markets/line-shopping?sport=SOCCER&take=10&includeBookmakerPrices=false" |
  ConvertTo-Json -Depth 30

Invoke-RestMethod "$base/api/markets/prop-history?sport=SOCCER&playerName=Example&propType=Shots&side=Over&line=1.5" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod -Method Post "$base/api/debug/the-odds/markets/refresh-current" `
  -ContentType "application/json" `
  -Body '{"sport":"SOCCER","competition":"World Cup","dryRun":true,"forceLive":false}' |
  ConvertTo-Json -Depth 30

# Force-live dry run: event discovery only, no writes.
Invoke-RestMethod -Method Post "$base/api/debug/the-odds/markets/refresh-current" `
  -ContentType "application/json" `
  -Body '{"sport":"SOCCER","competition":"World Cup","fromDate":"2026-06-11","toDate":"2026-06-24","dryRun":true,"forceLive":true,"includeSportsbook":true,"maxEvents":5,"bookmakers":["fanduel","betmgm","betrivers"],"markets":["player_goals","player_assists","player_shots","player_shots_on_target"]}' |
  ConvertTo-Json -Depth 60

# After reviewing dry-run output, forceLive=true with dryRun=false persists Soccer sportsbook context rows.
Invoke-RestMethod -Method Post "$base/api/debug/the-odds/markets/refresh-current" `
  -ContentType "application/json" `
  -Body '{"sport":"SOCCER","competition":"World Cup","fromDate":"2026-06-11","toDate":"2026-06-24","dryRun":false,"forceLive":true,"includeSportsbook":true,"maxEvents":5,"bookmakers":["fanduel","betmgm","betrivers"],"markets":["player_goals","player_assists","player_shots","player_shots_on_target"]}' |
  ConvertTo-Json -Depth 60

Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/import-fixture" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489393","dryRun":true,"forceLive":false}' |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/world-cup/backfill-completed" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-25","toDate":"2026-06-25","dryRun":true,"forceLive":true}' |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/world-cup/backfill-completed" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-25","toDate":"2026-06-25","dryRun":false,"forceLive":true}' |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/import-status?fromDate=2026-06-25&toDate=2026-06-25" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-11&toDate=2026-06-24&includeFixtureDetails=true&includeSamples=true&maxFixtures=50&maxSamplesPerFixture=3" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-24&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/stat-mapping-review?fromDate=2026-06-21&toDate=2026-06-24&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/world-cup/spike-status?fromDate=2026-06-21&toDate=2026-06-24&includeDetails=true" |
  ConvertTo-Json -Depth 100

Invoke-RestMethod "$base/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-11&toDate=2026-06-24&includeResolved=true&includeUnresolved=true&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/the-odds/world-cup/ingestion-coverage?fromDate=2026-06-11&toDate=2026-06-24&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch?fromDate=2026-06-21&toDate=2026-06-24&includeSamples=true&maxFixtures=50&maxSamples=200&minConfidence=0.94" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-21","toDate":"2026-06-24","dryRun":true,"minConfidence":0.94,"maxLinks":200,"includeExactNormalizedMatches":true,"includeTokenSubsetMatches":true}' |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"<fixtureId>","providerEventId":"<providerEventId>","platformPlayerName":"Carlos Casemiro","apiSportsPlayerId":"<apiSportsPlayerId>","localPlayerId":"<localPlayerGuid>","dryRun":true,"reason":"manual reviewed exact soccer player identity"}' |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-21" |
  ConvertTo-Json -Depth 60

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-21&includeSamples=true&maxSamples=20" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/candidates?fromDate=2026-06-11&toDate=2026-06-21&minConfidence=0.80&maxResults=100" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/platform-context?fromDate=2026-06-11&toDate=2026-06-21" |
  ConvertTo-Json -Depth 60

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/platform-context?fromDate=2026-06-11&toDate=2026-06-21&includeSamples=true&maxSamples=20" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-21&minConfidence=0.80&hasCandidate=true&suggestedAction=NeedsReview&maxResults=25" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-21&minConfidence=0.80&hasCandidate=true&safeOnly=true&maxResults=25" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export?fromDate=2026-06-11&toDate=2026-06-21&minConfidence=0.80&safeOnly=true&maxResults=25" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/api-sports/world-cup/approve-link" `
  -ContentType "application/json" `
  -Body '{"providerEntityLinkId":"<providerEntityLinkId>","canonicalPlayerId":"<canonicalPlayerId>","dryRun":true,"reason":"manual review"}' |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links" `
  -ContentType "application/json" `
  -Body '{
    "dryRun": true,
    "allowPartial": false,
    "allowNeedsReview": false,
    "approvals": [
      {
        "providerEntityLinkId": "<providerEntityLinkId>",
        "canonicalPlayerId": "<canonicalPlayerId>",
        "reason": "manual exact-name review"
      }
    ]
  }' |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/api-sports/world-cup/reject-link" `
  -ContentType "application/json" `
  -Body '{"providerEntityLinkId":"<providerEntityLinkId>","dryRun":true,"reason":"wrong player"}' |
  ConvertTo-Json -Depth 40

Invoke-RestMethod "$base/api/debug/identity/provider-links?sport=SOCCER&entityType=Player&providerKey=api-sports-football&includeSamples=true&maxSamples=20" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/provider-stat-mappings?sport=SOCCER&providerKey=api-sports-football&competitionScope=World%20Cup&season=2026&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80
```

World Cup import status is persisted-only by default and does not call API-Sports. It still reports imported fixture counts, rows by fixture, total rows, and latest imported timestamps. Add `forceLive=true` only when live discovery counts and missing completed fixture diagnostics are needed.

The World Cup coverage endpoint is read-only and checks whether imported API-Sports `prop_results` can connect to local Soccer platform props, recommendation snapshots, or market snapshots. It reports inventory counts, prop type coverage, join coverage, mismatch reasons, optional samples, and player-link coverage from approved `provider_entity_links`. Public Soccer grading remains disabled until those diagnostics prove a safe join path and the feature is explicitly promoted.

The player identity diagnostics are also read-only and never call external APIs. Use them before any future alias approval workflow so API-Sports result players can be reviewed against provider/platform player identities without changing data.

Provider stat mappings for API-Sports Football / World Cup live in `provider_stat_mappings`; see `docs/PROVIDER_STAT_MAPPING.md`. These mappings drive Soccer result import mechanics while keeping public Soccer grading disabled.

