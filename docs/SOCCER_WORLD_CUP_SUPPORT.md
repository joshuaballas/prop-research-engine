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

The import endpoint is force-live gated, supports `dryRun=true`, and writes only supported mappings for the requested fixture when `dryRun=false`. It reuses `prop_results` with `ExternalSource=api-sports-football`; no Soccer grading is enabled globally.

Supported mappings include goals, assists, shots, shots on target, passes attempted, tackles, fouls, fouls drawn, offsides, goalie saves for goalkeepers, and attempted dribbles. Conservative mappings such as Goal + Assist, Cards, Goals Allowed, and Shots Assisted are flagged as `NeedsConfirmation` in import diagnostics.

Soccer prop type display names are not unique because period-specific and combo markets can share customer-facing names. API-Sports result imports resolve prop types by Sport + canonical `statKey`, not display name.

Unsupported mappings are skipped for now: crosses, clearances, fantasy score props, and combo props.

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
```

