# Provider Identity Linking

`provider_entity_links` is the durable cross-provider identity layer for external identities. It is designed to support multiple sports, providers, and entity types without making every integration invent its own mapping table.

## Why It Exists

Provider result feeds and market feeds often use different ids for the same real-world player, team, event, or competition. Name-only matching is not safe enough for grading, especially for Soccer / World Cup where duplicate names and missing team context are common.

`provider_entity_links` stores provider identities separately from approval. A row can exist as a candidate from an import without implying that the app should grade against it.

## Relationship To `player_external_refs`

`player_external_refs` remains in place for existing player-specific workflows, especially MLB compatibility and older player mapping tools.

Going forward:

- `provider_entity_links` is the reusable provider identity inventory and review layer.
- `player_external_refs` is still supported for existing player-only compatibility.
- New multi-sport identity diagnostics should prefer `provider_entity_links` and may read `player_external_refs` as legacy evidence.

## Supported Shape

The table supports these entity types conceptually:

- `Player`
- `Team`
- `Event`
- `Competition`

Current implementation writes API-Sports Football Soccer `Player` identities only.

Important fields:

- `sport_id`
- `entity_type`
- `canonical_entity_id`
- `canonical_entity_kind`
- `canonical_entity_name`
- `provider_key`
- `provider_entity_id`
- `provider_entity_name`
- provider team/event/competition context fields
- `confidence`
- `link_status`
- `link_source`
- `metadata_json`

## Link Statuses

- `Candidate`: imported or discovered identity; not approved.
- `NeedsReview`: candidate has useful evidence but cannot be linked safely.
- `Approved`: reviewed link to a canonical entity.
- `Rejected`: reviewed and intentionally not linked.
- `Conflict`: diagnostic or review found conflicting identity evidence.

## Link Sources

- `Import`
- `Diagnostic`
- `Manual`
- `AutoSafe`
- `Migration`

## API-Sports Soccer Use Case

API-Sports World Cup result imports create or update `provider_entity_links` rows:

- `provider_key=api-sports-football`
- `entity_type=Player`
- `provider_entity_id=<API-Sports player id>`
- `provider_entity_name=<API-Sports player name>`
- `provider_team_id/provider_team_name` when available
- `link_status=Candidate`
- `link_source=Import`

The migration also backfills Candidate rows from existing API-Sports Soccer `prop_results` by extracting the provider player id from `external_source_identifier`.

No public Soccer grading is enabled by these rows. No Soccer players are auto-linked on name alone.

## Diagnostics

Global provider link inventory:

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/debug/identity/provider-links?sport=SOCCER&entityType=Player&providerKey=api-sports-football&includeSamples=true&maxSamples=20" |
  ConvertTo-Json -Depth 80
```

Soccer identity diagnostics also report provider link counts:

```powershell
Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-21&includeSamples=true&maxSamples=20" |
  ConvertTo-Json -Depth 80
```

## Current Soccer Limitation

Persisted Soccer platform props currently include start time, provider/bookmaker, and provider line/projection identifiers, but do not provide enough team/opponent/event context to approve safe API-Sports links automatically.

Next recommended path:

- Keep Candidate rows as durable review inventory.
- Add a narrow approval workflow later.
- Persist richer provider event/team context during Soccer market ingestion if available.
- Keep Soccer public grading disabled until approved identity links and stat mappings are proven.
