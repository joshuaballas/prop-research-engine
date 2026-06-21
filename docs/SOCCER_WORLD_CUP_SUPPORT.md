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
If the config is present, live Soccer ingestion still returns a guarded not-implemented diagnostic until the provider key is validated against snapshot persistence.

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
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/coverage`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/candidates`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/link-preview`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/platform-context`
- `GET /api/debug/soccer/player-identity/api-sports/world-cup/review-queue`
- `POST /api/debug/soccer/player-identity/api-sports/world-cup/approve-link`
- `POST /api/debug/soccer/player-identity/api-sports/world-cup/reject-link`
- `GET /api/debug/identity/provider-links`

The import endpoint is force-live gated, supports `dryRun=true`, and writes only supported mappings for the requested fixture when `dryRun=false`. It reuses `prop_results` with `ExternalSource=api-sports-football`; no Soccer grading is enabled globally.

The World Cup backfill endpoint is intentionally narrow: API-Sports Football only, World Cup only, league `1`, season `2026`, completed fixtures only, and no past seasons. It discovers fixtures by date range and imports only fixtures with API-Sports status `FT` / `Match Finished`; scheduled, live, postponed, and canceled fixtures are skipped with reasons.

Supported mappings include goals, assists, shots, shots on target, passes attempted, tackles, fouls, fouls drawn, offsides, goalie saves for goalkeepers, and attempted dribbles. Conservative mappings such as Goal + Assist, Cards, Goals Allowed, and Shots Assisted are flagged as `NeedsConfirmation` in import diagnostics.

Soccer prop type display names are not unique because period-specific and combo markets can share customer-facing names. API-Sports result imports resolve prop types by Sport + canonical `statKey`, not display name.

Unsupported mappings are skipped for now: crosses, clearances, fantasy score props, and combo props.

Imported API-Sports result players are not automatically linked to The Odds/platform player identities. The player identity diagnostics are read-only and compare persisted API-Sports World Cup result players against local Soccer players, platform props, recommendation snapshots, and player external refs. They report already linked identities, high-confidence candidates, ambiguous names, conflicts, unmatched players, and blockers. They do not write aliases, merge players, or enable public Soccer grading.

The platform-context diagnostic reports whether existing persisted Soccer platform/snapshot rows contain team, opponent, event, provider event id, provider player id, bookmaker, and competition context that could support identity matching. Current local diagnostics show start time, provider/bookmaker, and provider line/projection identifiers exist, but player team/opponent/event name/provider event id context is missing. Treat name-only Soccer candidates as manual review items until richer team/event identity context is persisted.

API-Sports result imports now create durable `provider_entity_links` Candidate rows for imported Soccer player identities. Existing `player_external_refs` remains supported for player-only compatibility; `provider_entity_links` is the reusable provider identity inventory and review layer going forward. See `docs/PROVIDER_IDENTITY_LINKING.md`.

Debug-only manual review endpoints can approve or reject one API-Sports Soccer provider player link at a time. They default to `dryRun=true`, validate Soccer/provider/canonical player constraints, and never merge player rows. Approved links are used only by debug coverage diagnostics for now; public Soccer grading remains disabled.

## Public History Still Disabled

Soccer result grading and public player prop history are intentionally unavailable until the Soccer import workflow is promoted beyond debug-only and grading semantics are explicitly enabled.

- `/api/markets/prop-history?sport=SOCCER...` returns `Soccer history is not available yet.`
- Model Performance should not include Soccer as graded performance.
- Soccer fantasy score and combo props must not be graded from fake or inferred data.
- API-Sports Football is the current result-provider spike candidate. See `docs/SOCCER_RESULTS_PROVIDER_SPIKE.md`.

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

Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/import-fixture" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489393","dryRun":true,"forceLive":false}' |
  ConvertTo-Json -Depth 40

Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/world-cup/backfill-completed" `
  -ContentType "application/json" `
  -Body '{"fromDate":"2026-06-11","toDate":"2026-06-21","dryRun":true,"forceLive":false}' |
  ConvertTo-Json -Depth 60

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/import-status?fromDate=2026-06-11&toDate=2026-06-21" |
  ConvertTo-Json -Depth 60

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

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/api-sports/world-cup/approve-link" `
  -ContentType "application/json" `
  -Body '{"providerEntityLinkId":"<providerEntityLinkId>","canonicalPlayerId":"<canonicalPlayerId>","dryRun":true,"reason":"manual review"}' |
  ConvertTo-Json -Depth 40

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

