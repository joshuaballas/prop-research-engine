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

