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

Dry-run one fixture and verify DB mappings:

```powershell
Invoke-RestMethod -Method Post "$base/api/debug/soccer/results/api-sports/import-fixture" `
  -ContentType "application/json" `
  -Body '{"fixtureId":"1489393","dryRun":true,"forceLive":true}' |
  ConvertTo-Json -Depth 80
```

Soccer public grading remains disabled until identity linking, mapping confidence, and grading rules are explicitly approved.
