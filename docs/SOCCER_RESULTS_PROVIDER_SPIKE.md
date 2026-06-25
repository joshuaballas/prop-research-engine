# Soccer Results Provider Spike

API-Sports is the current primary vendor candidate for Soccer / World Cup result imports. Football/Soccer is the API-Sports product/base URL used for World Cup data.

The goal of this spike is to verify whether API-Sports Football can provide player-level World Cup match stats needed to grade PrizePicks-style soccer props. This does not enable public Soccer grading yet.

## Candidate Provider

API-Sports appears viable for an MVP if the Football fixture player-stat payload contains the required fields:

- Free: 100 requests/day
- Pro: $19/month, 7,500 requests/day
- Ultra: $29/month, 75,000 requests/day
- Mega: $39/month, 150,000 requests/day

The intended pattern is the same as MLB results:

- Backend imports completed fixture stats.
- Stats are persisted locally after validation.
- Frontend and grading read only from the local database.
- No user interaction calls API-Sports directly.

## Configuration

No secrets are committed. Local config should use:

- `ApiSports:ApiKey`
- `ApiSports:Enabled`
- `ApiSports:TimeoutSeconds`
- `ApiSports:MaxRetries`
- `ApiSports:Football:BaseUrl`
- `ApiSports:Football:WorldCupLeagueId`
- `ApiSports:Football:Season`

One API-Sports key may cover multiple sports/products depending on subscription. This spike uses the Football product endpoint for World Cup.

If config is missing, debug endpoints return `MissingApiSportsConfig` with the missing fields.

## Debug Endpoints

All API-Sports endpoints are debug-only and force-live gated.

- `GET /api/debug/soccer/api-sports/config`
- `GET /api/debug/soccer/api-sports/fixtures?date=YYYY-MM-DD&forceLive=false`
- `GET /api/debug/soccer/api-sports/fixture-player-stats?fixtureId=...&forceLive=false`
- `GET /api/debug/soccer/api-sports/fixture-prop-coverage?fixtureId=...&forceLive=false`
- `POST /api/debug/soccer/results/api-sports/import-fixture`
- `GET /api/debug/soccer/results/api-sports/import-fixture/status?fixtureId=...`
- `GET /api/debug/soccer/results/api-sports/world-cup/import-status`
- `POST /api/debug/soccer/results/api-sports/world-cup/backfill-completed`
- `GET /api/debug/soccer/results/api-sports/world-cup/coverage`
- `GET /api/debug/soccer/results/api-sports/world-cup/market-join-preview`
- `GET /api/debug/soccer/results/api-sports/world-cup/grading-preview`
- `GET /api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary`
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

Rules:

- `forceLive=false` blocks external calls.
- `forceLive=true` is required for any API-Sports request.
- Config endpoint never calls external APIs.
- Fixture/player-stat endpoints do not persist data.
- Import-fixture is debug-only, imports only one requested fixture, and requires `forceLive=true`.
- World Cup backfill is debug-only, API-Sports Football only, league `1`, season `2026`, and completed fixtures only.
- `dryRun=true` previews rows and never writes.
- `dryRun=false` writes idempotent `prop_results` rows for supported mappings only.
- Soccer public grading remains disabled globally.
- Soccer prop type display names are not unique. API-Sports imports resolve and create `prop_types` by Sport + canonical `statKey`; display name is presentation metadata only.
- Completed fixtures are importable when API-Sports returns `FT` or `Match Finished`; scheduled, live, postponed, and canceled fixtures are skipped with reasons.
- No past-season Soccer backfill is supported by this spike.
- Imported Soccer results do not currently join safely to provider markets because API-Sports player identities are not bridged to The Odds/platform identities.
- Player identity coverage, candidates, link-preview, platform-context, and review-queue endpoints are read-only diagnostics only.
- Manual approve/reject endpoints are debug-only, dry-run by default, write only `provider_entity_links`, and do not merge players or enable public Soccer grading.
- API-Sports imports now create durable `provider_entity_links` Candidate rows for imported Soccer player identities. These are identity inventory rows, not approved grading links.

Fixture diagnostics support flexible API-Sports query shapes so access can be tested safely before upgrading a plan:

- `fixtureId=<id>` queries `/fixtures?id=<id>` and does not require date, league, or season.
- `date=YYYY-MM-DD` includes a date filter.
- `league=<id>` and `season=<year>` override configured World Cup defaults.
- `useConfiguredLeague=false` omits `ApiSports:Football:WorldCupLeagueId`.
- `useConfiguredSeason=false` omits `ApiSports:Football:Season`.
- Defaults preserve current World Cup behavior: configured league and season are used unless explicitly disabled.

Fixture responses include `sanitizedUrl`, `callsExternalApis`, the request parameters actually used, raw API-Sports errors, result count, and a compact fixture list.

## Coverage Matrix

The coverage endpoint maps discovered API-Sports Football stat fields to the required PrizePicks World Cup props:

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

Supported debug import mappings:

- Goals -> `goals.total`
- Assists -> `goals.assists`
- Shots -> `shots.total`
- Shots On Target -> `shots.on`
- Passes Attempted -> `passes.total`
- Tackles -> `tackles.total`
- Fouls -> `fouls.committed`
- Fouls Drawn -> `fouls.drawn`
- Offsides -> `offsides`
- Goalie Saves -> `goals.saves`, only for goalkeepers with minutes
- Attempted Dribbles -> `dribbles.attempts`

Conservative mappings are imported with `ruleConfidence=NeedsConfirmation` in diagnostics:

- Goal + Assist -> `goals.total + goals.assists`
- Cards -> `cards.yellow + cards.red`
- Goals Allowed -> `goals.conceded`, only for goalkeepers with minutes
- Shots Assisted -> `passes.key`

Unsupported mappings are skipped:

- Crosses
- Clearances
- Outfield Fantasy Score
- Goalie Fantasy Score
- Combo props

Players with null or zero minutes are skipped. Null stat values are treated as zero only when the API-Sports field is present for a player who logged minutes.

## Player Identity Bridge Diagnostics

API-Sports World Cup result rows are persisted with `ExternalSource=api-sports-football`, and the API-Sports player id can be extracted from `external_source_identifier` values shaped like `api-sports-football:{fixtureId}:{playerId}:{statKey}`. Those imported result players still need a reviewed identity bridge to the canonical/provider players used by The Odds platform props and recommendation snapshots before Soccer grading can be promoted.

Read-only diagnostics:

- `/api/debug/soccer/player-identity/api-sports/world-cup/coverage` reports imported API-Sports player inventory, existing Soccer player/platform/ref counts, already-linked market identities, normalized-name candidates, team-aware candidates, ambiguous names, conflicts, and unmatched players.
- `/api/debug/soccer/player-identity/api-sports/world-cup/candidates` returns candidate link rows with confidence, reasons, blockers, and suggested action.
- `/api/debug/soccer/player-identity/api-sports/world-cup/link-preview` returns only high-confidence `SafeAliasLink` candidates with no blockers, but still writes nothing.
- `/api/debug/soccer/player-identity/api-sports/world-cup/platform-context` reports which persisted Soccer platform/snapshot fields are available for identity matching, including team, opponent, event name, start time, provider event id, provider player id, bookmaker, and competition fields.
- `/api/debug/soccer/player-identity/api-sports/world-cup/review-queue` joins durable Candidate links to candidate evidence and suggests `ApproveCandidate`, `NeedsReview`, `NoCandidate`, or `Conflict`. It supports `suggestedAction`, `hasCandidate`, `safeOnly`, `includeNoCandidate`, and `sort=confidenceDesc` filters so manual review can focus on actionable rows first.
- `/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export` separates compact rows into `safeCandidates`, `needsReviewCandidates`, `noCandidate`, and `ambiguousConflict` buckets that can be reviewed before explicit approval payloads are built.
- `/api/debug/soccer/player-identity/api-sports/world-cup/approve-link`, `/bulk-approve-links`, and `/reject-link` default to `dryRun=true`. When `dryRun=false`, they update only explicit `provider_entity_links` rows after validating the provider key, entity type, Soccer sport, canonical Soccer player, and existing link status.
- `/bulk-approve-links` never infers approvals by name. It approves only supplied `providerEntityLinkId` + `canonicalPlayerId` pairs. With `allowPartial=false`, any blocked row prevents all writes; with `allowPartial=true`, valid rows are written and invalid rows are reported. By default it rejects rows with blockers such as `MissingTeamContext`; `allowNeedsReview=true` is required for an explicit reviewed exception.

The matcher normalizes names case-insensitively, strips punctuation through the app compact player-name normalizer, handles diacritics, and uses team names when both sides have team context. Name-only candidates are review items, not automatic links. `MissingTeamContext` is not safe for bulk approval. Ambiguous names or team mismatches remain blockers.

New The Odds Soccer market refreshes persist event-level context with `contextSchemaVersion=the-odds-provider-context-v2`: provider event id, event name, home team, away team, event start time, bookmaker/provider key, competition, market key, outcome line metadata, and player display name from the outcome description. Old Soccer market rows remain pre-context or partial until the Soccer market refresh is rerun.

The Odds Soccer player prop payload does not currently provide a stable provider player id or the player's team on the outcome row. Event-level home/away context is useful for review and event matching, but name-only candidates with `MissingTeamContext` or `EventTeamOnly` are still not safe for bulk player approval.

The Soccer market refresh creates normalized `games` / `teams` from The Odds event home/away/start context for newly ingested Soccer rows. Raw provider context lives in `platform_props.metadata`; reliable event/team/player links should still be promoted through `provider_entity_links` review before any grading promotion.

Approved provider links are used by `/api/debug/soccer/results/api-sports/world-cup/coverage` to report player-link coverage and debug market joins. The coverage response keeps player-link coverage separate from market join coverage. Public Soccer grading remains disabled.

`/api/debug/soccer/results/api-sports/world-cup/market-join-preview` is a narrower bridge diagnostic for API-Sports result rows versus The Odds Soccer `platform_props` sportsbook context. It is read-only, does not call external APIs, and does not write rows. It reports imported result rows, candidate platform rows, event/player/prop join counts, matched/unmatched samples, and blockers such as missing provider event ids, missing API-Sports fixture ids on market rows, missing player team/provider player ids, prop mapping gaps, player-name mismatches, and event-date mismatches. This is the diagnostic to use for a known The Odds provider event id, such as Uruguay vs Cape Verde, before any debug-only Soccer grading preview is implemented.

`/api/debug/soccer/results/api-sports/world-cup/grading-preview` is the next read-only step after `market-join-preview` proves overlap. It grades only already-joinable persisted rows by comparing API-Sports actual stat values to The Odds sportsbook side/line context. It returns preview-only `OverWin`, `UnderWin`, `Push`, `NoLine`, unjoinable, and blocker counts. It does not persist grades, update snapshots, auto-link players, or enable public Soccer grading.

`/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary` runs the same read-only preview logic across resolved real The Odds Soccer provider-event/API-Sports fixture pairs. It reports resolved-pair totals, per-fixture before/after alias join counts, graded preview rows, blockers, best/worst coverage fixtures, samples, unresolved provider events, result-only fixtures, and legacy/synthetic platform-only events. It consumes approved The Odds alias links but never writes aliases, imports results, refreshes odds, persists grades, or enables public Soccer grading.

`/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap` is the read-only follow-up when grading coverage is blocked by `propTypeMappingMissing`. It uses only persisted API-Sports `prop_results`, persisted The Odds Soccer sportsbook `platform_props`, resolved fixture/provider-event pairs, approved The Odds alias links, and DB-backed API-Sports stat mappings. It separates missing The Odds market canonical mappings, API-Sports stats without provider mappings, canonical key mismatches, non-gradeable mappings, imported stat noise, and player-matched/stat-missing rows. It recommends review or mapping work but does not write mappings, grades, aliases, imports, or provider refreshes.

`/api/debug/soccer/world-cup/spike-status` summarizes the spike without writing or calling providers. It combines The Odds ingestion coverage, fixture mapping, alias/link coverage, debug grading coverage, and prop/stat mapping gaps into a single `ResearchOnly` status. The response includes production guardrails showing public Soccer grading disabled, Soccer Entry Builder usage disabled, sportsbook context research-only, and automatic alias/stat-mapping approval disabled.

The alias portion of spike status uses explicit fields instead of a generic unresolved count: evaluated platform player names, total alias candidates, approved links, safe-link approvals, manual approvals, remaining needs-review rows, remaining no-candidate rows, remaining ambiguous rows, and remaining new safe candidates.

`/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage` reports whether persisted real The Odds Soccer World Cup sportsbook provider events can be resolved to API-Sports fixtures. It uses only local context-v2 platform rows and imported API-Sports result rows, excludes synthetic provider event ids, applies date/team matching plus World Cup team aliases, and returns resolved, no-candidate, ambiguous, date-mismatch, team-mismatch, and missing-result statuses. Use this before expanding grading previews beyond the known working Uruguay vs Cape Verde event pair.

`/api/debug/soccer/the-odds/world-cup/ingestion-coverage` audits persisted The Odds Soccer World Cup provider-event ingestion. It counts real context-v2 sportsbook provider events, buckets synthetic or legacy Soccer platform rows, summarizes imported API-Sports fixture/result coverage, surfaces the latest persisted Soccer The Odds refresh run when present, and reports gap analysis for API-Sports fixtures with results but no real The Odds event. It is read-only, does not refresh odds, does not import results, and does not enable public Soccer grading.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates` compares The Odds sportsbook-context player names for a provider event to API-Sports fixture result players. It is useful when The Odds uses a long-form player name and API-Sports uses a shorter name. The matcher is accent-insensitive and supports token-subset matches, but ambiguous rows remain blocked for manual review. The endpoint is read-only and does not write aliases or approve links.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-link` is the dry-run-first persistence endpoint for the safe rows surfaced by `alias-candidates`. It defaults to `dryRun=true`, persists only unique `SafeAliasCandidate` rows above `minConfidence`, and writes approved `provider_entity_links` rows with `provider_key=the-odds-soccer` only when `dryRun=false`. Ambiguous, needs-review, no-candidate, below-confidence, and conflicting existing-link rows are skipped and reported. These links can be consumed by the debug market-join and grading-preview endpoints, but public Soccer grading remains disabled and no external APIs are called.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve` handles one reviewed `NeedsReview` alias at a time. It defaults to `dryRun=true`, verifies the supplied The Odds player name, API-Sports player id, and local player id still match the current diagnostic candidate, rejects ambiguous/no-candidate rows, and writes a manual approval link only when `dryRun=false`. This is intentionally not a bulk approval path.

`/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch` and `/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch` apply the same review/write workflow across all resolved The Odds/API-Sports World Cup fixture pairs in a date range. Batch candidate review is read-only and includes grouped candidates, deduplicated review rows, already-linked counts, and estimated coverage impact. Batch linking defaults to `dryRun=true`, writes only safe candidates when `dryRun=false`, skips ambiguous/needs-review/no-candidate/below-confidence/conflicting rows, and remains event-scoped because current The Odds alias provider entity ids include the provider event id.

See `docs/PROVIDER_IDENTITY_LINKING.md` for the durable provider identity model. `player_external_refs` remains supported for existing player-only compatibility, while `provider_entity_links` is the reusable identity inventory and review layer going forward.

Public Soccer grading remains disabled while these diagnostics are reviewed.

Public Soccer market reads are persisted-only and must remain research context. They should not call API-Sports or The Odds, should not write public Soccer performance rows, and should not make Soccer rows Entry Builder actionable. Response payloads force Soccer rows to `sportsbookContextResearchOnly=true` and `entryActionableForUser=false` as a production guardrail.

## Request-Limit Strategy

MVP imports should be by completed fixture/date, not per user.

Suggested workflow:

- Discover completed World Cup fixtures by date.
- Fetch player stats once per completed fixture.
- Persist all gradeable stat rows locally after coverage is verified.
- Recheck recent completed fixtures until final/stable.
- Avoid repeated imports for fixtures already marked final and stable.

Rough estimate:

- 1 fixtures request per date.
- 1 player-stat request per completed fixture.
- A busy World Cup day with 4 matches is roughly 5 requests for initial import, plus limited rechecks.

The $19/month tier is likely enough for an MVP if the required fields are available, because 7,500 requests/day is well above a fixture-level polling strategy. The free tier may be enough for occasional manual validation but is tight for repeated development and rechecks.

## Deprioritized Providers

- SportsDataIO: potentially useful, but likely higher-friction for World Cup player prop stat coverage and pricing validation.
- BallDontLie: not a soccer/World Cup match-stat fit.
- Rolling Insights: unclear coverage and integration fit for player-level World Cup stat imports.
- Sportmonks: viable alternative, but API-Sports is the first spike because of lower apparent entry cost and simple Football fixture/player endpoints.

## Verification

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/debug/soccer/api-sports/config" |
  ConvertTo-Json -Depth 20

Invoke-RestMethod "$base/api/debug/soccer/api-sports/fixtures?date=2026-06-21&forceLive=false" |
  ConvertTo-Json -Depth 20

# Only after key/config are set and the user approves an external call:

# Current free-plan date-only test, no league/season:
Invoke-RestMethod "$base/api/debug/soccer/api-sports/fixtures?date=2026-06-20&useConfiguredLeague=false&useConfiguredSeason=false&forceLive=true" |
  ConvertTo-Json -Depth 30

# League only, no season:
Invoke-RestMethod "$base/api/debug/soccer/api-sports/fixtures?date=2026-06-20&useConfiguredLeague=true&useConfiguredSeason=false&forceLive=true" |
  ConvertTo-Json -Depth 30

# Configured current behavior:
Invoke-RestMethod "$base/api/debug/soccer/api-sports/fixtures?date=2026-06-20&forceLive=true" |
  ConvertTo-Json -Depth 30

# Fixture id lookup once a fixture id is known:
Invoke-RestMethod "$base/api/debug/soccer/api-sports/fixtures?fixtureId=<fixtureId>&forceLive=true" |
  ConvertTo-Json -Depth 30

Invoke-RestMethod "$base/api/debug/soccer/api-sports/fixture-player-stats?fixtureId=<fixtureId>&forceLive=true" |
  ConvertTo-Json -Depth 50

Invoke-RestMethod "$base/api/debug/soccer/api-sports/fixture-prop-coverage?fixtureId=<fixtureId>&forceLive=true" |
  ConvertTo-Json -Depth 50

# Dry-run should block without forceLive:
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/import-fixture" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489393","dryRun":true,"forceLive":false}' |
  ConvertTo-Json -Depth 40

# The Odds Soccer DFS-region refresh path; use only after local Soccer sport key/markets are configured.
# This remains available for diagnostics but may return no rows if us_dfs has no World Cup player props.
Invoke-RestMethod -Method Post "$base/api/debug/the-odds/markets/refresh-current" `
  -ContentType "application/json" `
  -Body '{"sport":"SOCCER","competition":"World Cup","date":"2026-06-21","dryRun":false,"forceLive":true,"includeDfs":true}' |
  ConvertTo-Json -Depth 60

# Sportsbook-context default, equivalent to providerCategory=Sportsbook and region=us unless config overrides it:
Invoke-RestMethod -Method Post "$base/api/debug/the-odds/markets/refresh-current" `
  -ContentType "application/json" `
  -Body '{"sport":"SOCCER","competition":"World Cup","date":"2026-06-21","dryRun":false,"forceLive":true}' |
  ConvertTo-Json -Depth 60

# Multi-event sportsbook-context planning across a World Cup date window.
# dryRun=true with forceLive=true calls event discovery only and writes nothing.
Invoke-RestMethod -Method Post "$base/api/debug/the-odds/markets/refresh-current" `
  -ContentType "application/json" `
  -Body '{"sport":"SOCCER","competition":"World Cup","fromDate":"2026-06-11","toDate":"2026-06-24","dryRun":true,"forceLive":true,"includeSportsbook":true,"maxEvents":5,"bookmakers":["fanduel","betmgm","betrivers"],"markets":["player_goals","player_assists","player_shots","player_shots_on_target"]}' |
  ConvertTo-Json -Depth 60

# After reviewing the dry-run plan, dryRun=false persists the selected provider events.
Invoke-RestMethod -Method Post "$base/api/debug/the-odds/markets/refresh-current" `
  -ContentType "application/json" `
  -Body '{"sport":"SOCCER","competition":"World Cup","fromDate":"2026-06-11","toDate":"2026-06-24","dryRun":false,"forceLive":true,"includeSportsbook":true,"maxEvents":5,"bookmakers":["fanduel","betmgm","betrivers"],"markets":["player_goals","player_assists","player_shots","player_shots_on_target"]}' |
  ConvertTo-Json -Depth 60

# If The Odds rejects the configured Soccer sport key/market/region/event request,
# the endpoint returns success=false, providerRejectedRequest=true, blockReason=ProviderRejectedRequest,
# providerError.statusCode/operation/sanitizedRequestUrl/rawProviderBody, and diagnostics.likelyRejectedInputs.
# The sanitizedRequestUrl redacts apiKey values before returning or persisting diagnostics.
# If the refresh succeeds but writes zero rows, inspect diagnostics.events[] and aggregate fields such as
# noProviderMarketsReturned, providerMarketsReturnedButNoRowsMapped, configuredMarketsMissing,
# unsupportedMarketsReturned, bookmakerCount, marketKeysReturned, outcomeCount, and skipReasons.

# Read-only The Odds Soccer provider availability probe. This never writes rows.
# Live calls = 1 event-list call + matched events checked * regions checked.
Invoke-RestMethod "$base/api/debug/the-odds/soccer/world-cup/provider-availability?slateDate=2026-06-21&regions=us_dfs,us&markets=player_shots&maxEvents=3&forceLive=true" |
  ConvertTo-Json -Depth 60

# Dry-run with live provider call:
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/import-fixture" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489393","dryRun":true,"forceLive":true}' |
  ConvertTo-Json -Depth 60

# Only after reviewing dry-run:
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/import-fixture" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489393","dryRun":false,"forceLive":true}' |
  ConvertTo-Json -Depth 60

# Verify local rows:
Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/import-fixture/status?fixtureId=1489393" |
  ConvertTo-Json -Depth 40

# Read-only identity bridge diagnostics:
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

# World Cup 2026 completed-fixture backfill safety block:
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/world-cup/backfill-completed" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-11","toDate":"2026-06-21","dryRun":true,"forceLive":false}' |
  ConvertTo-Json -Depth 60

# Dry-run live discovery:
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/world-cup/backfill-completed" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-11","toDate":"2026-06-21","dryRun":true,"forceLive":true}' |
  ConvertTo-Json -Depth 80

# Write completed fixtures after reviewing dry-run:
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/world-cup/backfill-completed" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-11","toDate":"2026-06-21","dryRun":false,"forceLive":true}' |
  ConvertTo-Json -Depth 80

# Status; forceLive omitted means persisted-only and no external API calls:
Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/import-status?fromDate=2026-06-11&toDate=2026-06-21" |
  ConvertTo-Json -Depth 60
```

`import-status` in persisted-only mode reports local `importedFixtureCount`, `rowsByFixture`, `totalRows`, `latestImportedEventDate`, and `latestImportedAtUtc` without calling API-Sports. Discovery-dependent counts such as discovered, completed, and missing completed fixtures are only populated when `forceLive=true`.

`coverage` is read-only and never calls API-Sports. It inventories imported World Cup `prop_results` by fixture, player, prop type, and rule confidence, then checks whether imported rows have local join candidates in platform props, recommendation snapshots, or market snapshots. The diagnostic is conservative: `prop_results` do not store provider/bookmaker/market keys, and market snapshots do not store API-Sports fixture ids, so unmatched rows are reported by mismatch category instead of being silently ignored.

When reviewed provider identity links are approved, `coverage` also reports `playerLinkCoverage` with approved links used, result rows with/without approved links, and link coverage rate. This does not enable public Soccer grading.

API-Sports Soccer stat mappings are DB-backed in `provider_stat_mappings`; see `docs/PROVIDER_STAT_MAPPING.md`. Import and coverage diagnostics report whether DB mappings or temporary fallback mappings were used. Soccer `public_grading_enabled` remains false.

```powershell
Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-21" |
  ConvertTo-Json -Depth 60

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-21&includeSamples=true&maxSamples=10" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/market-join-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true&maxSamples=50" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/grading-preview?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true&maxSamples=50" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/grading-coverage-summary?fromDate=2026-06-11&toDate=2026-06-24&includeFixtureDetails=true&includeSamples=true&maxFixtures=50&maxSamplesPerFixture=3" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-24&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/world-cup/spike-status?fromDate=2026-06-21&toDate=2026-06-24&includeDetails=true" |
  ConvertTo-Json -Depth 100

Invoke-RestMethod "$base/api/debug/soccer/the-odds/api-sports/fixture-mapping-coverage?fromDate=2026-06-11&toDate=2026-06-24&includeResolved=true&includeUnresolved=true&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/the-odds/world-cup/ingestion-coverage?fromDate=2026-06-11&toDate=2026-06-24&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates?fixtureId=1489398&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true&maxSamples=50" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch?fromDate=2026-06-21&toDate=2026-06-24&includeSamples=true&maxFixtures=50&maxSamples=200&minConfidence=0.94" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-link" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489398","providerEventId":"852a3abe6d46820720febc0fedebff9a","dryRun":true,"minConfidence":0.94,"maxLinks":50}' |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-21","toDate":"2026-06-24","dryRun":true,"minConfidence":0.94,"maxLinks":200,"includeExactNormalizedMatches":true,"includeTokenSubsetMatches":true}' |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"<fixtureId>","providerEventId":"<providerEventId>","platformPlayerName":"Carlos Casemiro","apiSportsPlayerId":"<apiSportsPlayerId>","localPlayerId":"<localPlayerGuid>","dryRun":true,"reason":"manual reviewed exact soccer player identity"}' |
  ConvertTo-Json -Depth 80

# Only after reviewing proposedLinks from the dry run.
Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/the-odds/api-sports/alias-link" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489398","providerEventId":"852a3abe6d46820720febc0fedebff9a","dryRun":false,"minConfidence":0.94,"maxLinks":50}' |
  ConvertTo-Json -Depth 80
```

