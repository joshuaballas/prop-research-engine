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

## Manual Review Workflow

API-Sports Soccer player links can be reviewed through debug-only endpoints. These endpoints never call external APIs and default to dry-run for writes.

Review queue:

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/review-queue?fromDate=2026-06-11&toDate=2026-06-21&minConfidence=0.80&hasCandidate=true&suggestedAction=NeedsReview&maxResults=25" |
  ConvertTo-Json -Depth 80
```

Useful filters:

- `suggestedAction=ApproveCandidate|NeedsReview|Conflict|NoCandidate`
- `hasCandidate=true|false`
- `includeNoCandidate=true` to include null-confidence rows when `minConfidence` is set
- `sort=confidenceDesc` by default, which orders `ApproveCandidate`, `NeedsReview`, `Conflict`, then `NoCandidate`, with confidence descending inside each group

Approve one reviewed link:

```powershell
Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/api-sports/world-cup/approve-link" `
  -ContentType "application/json" `
  -Body '{"providerEntityLinkId":"<providerEntityLinkId>","canonicalPlayerId":"<canonicalPlayerId>","dryRun":true,"reason":"manual review"}' |
  ConvertTo-Json -Depth 40
```

Reject one reviewed link:

```powershell
Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/api-sports/world-cup/reject-link" `
  -ContentType "application/json" `
  -Body '{"providerEntityLinkId":"<providerEntityLinkId>","dryRun":true,"reason":"wrong player"}' |
  ConvertTo-Json -Depth 40
```

Approval validation is narrow by design:

- provider key must be `api-sports-football`
- entity type must be `Player`
- provider link sport must be `SOCCER`
- canonical player must exist and belong to Soccer
- Rejected or Conflict links cannot be approved
- a link already approved to a different canonical player is blocked

When `dryRun=false`, approval sets `link_status=Approved`, `link_source=Manual`, `approved_by=debug/manual`, and stores the review reason in metadata. Rejection sets `link_status=Rejected`, clears canonical player fields, and stores the review reason.

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

Imported-result coverage uses approved provider links in debug diagnostics only:

```powershell
Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/coverage?fromDate=2026-06-11&toDate=2026-06-21&includeSamples=true&maxSamples=10" |
  ConvertTo-Json -Depth 80
```

That response reports `playerLinkCoverage.approvedProviderLinksUsed`, `resultRowsWithApprovedPlayerLink`, and `playerLinkCoverageRate` separately from market join coverage. Public Soccer grading remains disabled.

## Current Soccer Limitation

Persisted Soccer platform props currently include start time, provider/bookmaker, and provider line/projection identifiers, but do not provide enough team/opponent/event context to approve safe API-Sports links automatically.

Next recommended path:

- Keep Candidate rows as durable review inventory.
- Use the debug-only manual approval workflow for reviewed API-Sports Soccer links.
- Persist richer provider event/team context during Soccer market ingestion if available.
- Keep Soccer public grading disabled until approved identity links and stat mappings are proven.

## Related Stat Mapping Configuration

Provider identity links answer which provider player/team/event maps to a local canonical entity. Provider stat mappings answer which provider stat field maps to a canonical prop/stat type. API-Sports Soccer stat mappings now live in `provider_stat_mappings`; see `docs/PROVIDER_STAT_MAPPING.md`.
