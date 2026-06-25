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

The Odds Soccer alias links can also be written after a dry-run review:

- `provider_key=the-odds-soccer`
- `entity_type=Player`
- `provider_entity_id=soccer_fifa_world_cup:<providerEventId>:<normalized The Odds player name>`
- `provider_entity_name=<The Odds player display name>`
- `canonical_entity_id=<local API-Sports/result player id>`
- `link_status=Approved`
- `link_source=DebugTheOddsApiSportsAliasLink`

These rows are created only by `/api/debug/soccer/player-identity/the-odds/api-sports/alias-link`, `/api/debug/soccer/player-identity/the-odds/api-sports/alias-link-batch`, or `/api/debug/soccer/player-identity/the-odds/api-sports/alias-link/manual-approve` with `dryRun=false` after a reviewed dry run. They bridge long-form The Odds player display names to API-Sports/local player identities for debug market-join and grading-preview diagnostics. Batch review starts with `/api/debug/soccer/player-identity/the-odds/api-sports/alias-candidates-batch`, which groups candidates by resolved provider event and fixture, deduplicates review rows, and estimates coverage impact without writing. Manual approval is one-at-a-time for reviewed `NeedsReview` rows and requires the submitted API-Sports/local ids to match the current diagnostic candidate. These links do not enable public Soccer grading, merge player rows, or call external APIs.

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
- `safeOnly=true` to return only candidate rows with no blockers
- `includeNoCandidate=true` to include null-confidence rows when `minConfidence` is set
- `sort=confidenceDesc` by default, which orders `ApproveCandidate`, `NeedsReview`, `Conflict`, then `NoCandidate`, with confidence descending inside each group

`MissingTeamContext` is a blocker. A name match with that blocker is not approval-ready for bulk approval, even when confidence is high. Use `safeOnly=true` or the export `safeCandidates` bucket for bulk-review candidates.

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

Bulk approval is also available for explicit reviewed pairs only. It does not infer or auto-approve names:

```powershell
Invoke-RestMethod "$base/api/debug/soccer/player-identity/api-sports/world-cup/review-queue/export?fromDate=2026-06-11&toDate=2026-06-21&minConfidence=0.80&safeOnly=true&maxResults=25" |
  ConvertTo-Json -Depth 80

Invoke-RestMethod -Method Post "$base/api/debug/soccer/player-identity/api-sports/world-cup/bulk-approve-links" `
  -ContentType "application/json" `
  -Body '{
    "dryRun": true,
    "allowPartial": false,
    "approvals": [
      {
        "providerEntityLinkId": "<providerEntityLinkId>",
        "canonicalPlayerId": "<canonicalPlayerId>",
        "reason": "manual exact-name review"
      }
    ]
  }' |
  ConvertTo-Json -Depth 80
```

The export response separates rows into `safeCandidates`, `needsReviewCandidates`, `noCandidate`, and `ambiguousConflict`. Bulk approval should use `safeCandidates` unless a human has completed additional review.

`bulk-approve-links` uses the same validation as single approval. If `allowPartial=false` and any row is blocked, a real write writes nothing. If `allowPartial=true`, valid rows are approved and blocked rows are reported. By default, bulk approval rejects rows with review blockers such as `MissingTeamContext`; set `allowNeedsReview=true` only for an explicit reviewed exception.

Approval validation is narrow by design:

- provider key must be `api-sports-football`
- entity type must be `Player`
- provider link sport must be `SOCCER`
- canonical player must exist and belong to Soccer
- Rejected or Conflict links cannot be approved
- a link already approved to a different canonical player is blocked

When `dryRun=false`, approval sets `link_status=Approved`, `link_source=Manual`, `approved_by=debug/manual`, and stores the review reason in metadata. Rejection sets `link_status=Rejected`, clears canonical player fields, and stores the review reason.

Single-link approval remains available for explicit human review. If the selected pair currently has blockers, the response includes warning values such as `ManualApprovalNeedsReviewCandidate:MissingTeamContext` so the risk is visible before a real write.

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

Invoke-RestMethod "$base/api/debug/soccer/results/api-sports/world-cup/market-join-preview?fromDate=2026-06-21&toDate=2026-06-21&fixtureId=1489393&providerEventId=852a3abe6d46820720febc0fedebff9a&includeSamples=true&maxSamples=50" |
  ConvertTo-Json -Depth 80
```

That response reports `playerLinkCoverage.approvedProviderLinksUsed`, `resultRowsWithApprovedPlayerLink`, and `playerLinkCoverageRate` separately from market join coverage. Public Soccer grading remains disabled.

The market-join preview endpoint is also read-only. It compares imported API-Sports result rows with The Odds Soccer `platform_props` sportsbook context by event date/team context, normalized player name, and canonical prop/stat key. It helps prove overlap for one event before any debug-only grading preview is built, without approving identities, writing aliases, or enabling public Soccer grading.

## Current Soccer Limitation

New The Odds Soccer market refreshes persist event-level context with `contextSchemaVersion=the-odds-provider-context-v2`: provider event id, event name, home team, away team, event start time, bookmaker/provider key, competition, market key, outcome line metadata, and player display name from the outcome description. Old Soccer platform rows remain pre-context or partial until the Soccer market refresh is rerun.

The Odds Soccer player prop payload does not currently provide a stable provider player id or the player's team on the outcome row. Event-level home/away context can support review and event matching, but it does not make name-only player links bulk-safe by itself.

Soccer / World Cup manual refresh can persist sportsbook-context rows from The Odds region `us` when no DFS rows are available in `us_dfs`. These rows are research-only and not Entry Builder playable, but they still provide provider event/team/bookmaker/market context that can help manual identity review.

Next recommended path:

- Keep Candidate rows as durable review inventory.
- Use the debug-only manual approval workflow for reviewed API-Sports Soccer links.
- Continue treating `MissingTeamContext` and `EventTeamOnly` as manual-review blockers for player approvals.
- Store raw provider context in `platform_props.metadata`; promote stable event/team/player mappings into `provider_entity_links` only after review.
- Keep Soccer public grading disabled until approved identity links and stat mappings are proven.

## Related Stat Mapping Configuration

Provider identity links answer which provider player/team/event maps to a local canonical entity. Provider stat mappings answer which provider stat field maps to a canonical prop/stat type. API-Sports Soccer stat mappings now live in `provider_stat_mappings`; see `docs/PROVIDER_STAT_MAPPING.md`.
