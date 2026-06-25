# Provider Stat Mappings

Provider stat mappings are DB-backed product configuration. Backend code owns the generic mechanics for reading provider stats, applying transforms, and writing `prop_results`; rows in `provider_stat_mappings` define how a provider/product stat maps to a canonical prop/stat.

## Table

`provider_stat_mappings` stores active and inactive mapping rules.

Important fields:

- `sport_id`: required sport scope.
- `provider_key`: provider source key, for example `api-sports-football`.
- `provider_product`: provider product label, for example `API-Sports Football`.
- `competition_scope`: optional scope such as `World Cup`.
- `season`: optional season, for example `2026`.
- `provider_mapping_key`: stable unique mapping key for active rows.
- `provider_prop_name` / `provider_stat_key`: provider-facing labels or stat field.
- `provider_stat_fields_json`: ordered stat fields used by the transform.
- `canonical_prop_type_id`: optional resolved `prop_types` row.
- `canonical_stat_key`: canonical stat key such as `SHOTS_ON_TARGET`.
- `display_name`: customer-readable prop/stat name.
- `value_transform`: `Direct`, `Sum`, `GoalkeeperOnlyDirect`, `GoalkeeperOnlySum`, `Unsupported`, or `ManualOnly`.
- `rule_confidence`: `Known`, `NeedsConfirmation`, or `Unsupported`.
- `is_gradeable`: whether this mapping is considered gradeable by verified rules.
- `public_grading_enabled`: public grading gate. Soccer mappings currently keep this `false`.

## API-Sports Soccer

The migration `20260621193000_AddProviderStatMappings` seeds API-Sports Football / World Cup 2026 rows for current known, needs-confirmation, and unsupported mappings.

Known gradeable rows include Goals, Assists, Shots, Shots On Target, Passes Attempted, Tackles, Fouls, Fouls Drawn, Offsides, Goalie Saves, and Attempted Dribbles.

Needs-confirmation rows include Goal + Assist, Cards, Goals Allowed, and Shots Assisted. They can be imported for diagnostics but are not public-gradeable.

Unsupported rows document Crosses, Clearances, Outfield Fantasy Score, Goalie Fantasy Score, and Combo props.

## Runtime Behavior

API-Sports Soccer fixture import now loads active DB mappings first. If no active DB mappings exist for the scoped provider/product/competition/season, the import can temporarily use the existing hardcoded fallback. Responses include mapping diagnostics:

- `providerMappingsConfigured`
- `dbMappingsUsed`
- `fallbackMappingsUsed`
- `missingMappingCount`
- `mappingsByConfidence`
- `mappingsByGradeable`

If a provider stat field is encountered without an active mapping, it is skipped and reported as `MissingMapping`. The import does not silently create `prop_results` for unmapped stats.

## Diagnostics

Read-only mapping inventory:

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/debug/provider-stat-mappings?sport=SOCCER&providerKey=api-sports-football&competitionScope=World%20Cup&season=2026&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80
```

Read-only Soccer prop/stat mapping gap diagnostic across resolved The Odds/API-Sports fixture pairs:

```powershell
Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/prop-mapping-gap?fromDate=2026-06-21&toDate=2026-06-24&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80
```

The gap diagnostic is for review only. It reports The Odds market keys missing canonical mappings, API-Sports result stat keys missing provider mappings, canonical key mismatches, mappings that exist but are not gradeable, imported stats that were not requested by The Odds, and player-matched/stat-missing rows. It never writes `provider_stat_mappings`, writes grades, imports results, refreshes odds, or enables public Soccer grading.

Read-only mapping approval review:

```powershell
Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/stat-mapping-review?fromDate=2026-06-21&toDate=2026-06-24&includeSamples=true&maxSamples=100" |
  ConvertTo-Json -Depth 80
```

The review endpoint lists each NeedsConfirmation, Unsupported, non-gradeable, or non-public-gradeable Soccer mapping with source fields, affected rows, imported-stat-noise rows, current notes, why it is non-gradeable today, risk, and recommendation. It is designed to support future explicit manual approval decisions, not to approve mappings automatically.

Dry-run one fixture and verify DB mappings:

```powershell
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/import-fixture" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489393","dryRun":true,"forceLive":true}' |
  ConvertTo-Json -Depth 80
```

Soccer public grading remains disabled until identity linking, provider event/team context, mapping confidence, and grading rules are explicitly approved. The Odds Soccer market refresh now persists event-level context for new rows, but The Odds does not currently provide stable provider player ids or player team fields in the player prop payload.

`/api/debug/soccer/results/api-sports/world-cup/grading-preview` can use these canonical stat keys for a read-only preview once API-Sports result rows and The Odds Soccer platform rows join by event, normalized player name, and prop/stat key. That preview compares actual values to sportsbook context side/line only after the result row is joined. It does not write grades and does not change the `public_grading_enabled=false` Soccer gate.

`NeedsConfirmation` mappings remain non-public-gradeable until explicitly reviewed. The Soccer spike status endpoint, `/api/debug/soccer/world-cup/spike-status`, reports these guardrails alongside prop/stat mapping blockers so public Soccer grading cannot be promoted accidentally.
