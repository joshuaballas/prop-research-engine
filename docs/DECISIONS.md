# Decisions

This file records durable decisions that should not be rediscovered from chat history.

## PrizePicks Direct Ingestion Is Disabled

PrizePicks direct ingestion is disabled because DataDome blocks automated API access. Browser automation and bypass experiments are not part of the active product direction.

Decision:

- Do not attempt DataDome bypass.
- Do not ship scraping/browser automation.
- Revisit PrizePicks only through an official/allowed integration or a stable legal data source.

## The Odds API Is A Data Provider, Not A Sportsbook

The Odds API aggregates bookmaker data. Persist actual bookmaker/platform rows such as DraftKings, FanDuel, BetMGM, or Bovada.

Decision:

- Do not create one generic sportsbook platform called "The Odds API" for all markets.
- Store The Odds API as provider metadata/source context.

## The Odds Worker Is Disabled By Default

The Odds API calls spend limited monthly credits.

Decision:

- The worker skeleton remains disabled by default.
- It defaults to dry-run.
- Manual dashboard/API calls must be explicit.
- No automatic polling should be enabled until credit caps, freshness policy, and operational monitoring are proven.

## `/research` Is Internal/Admin Only

The research dashboard exposes internal diagnostics and manual write operations.

Decision:

- `/research` is not public product UI.
- `/research` and `/api/debug/*` are protected outside Development.
- If explicitly enabled outside Development, require `X-Debug-Admin-Key`.

## MLB First, Other Sports Later

The app is structured for multiple sports, but production-quality data flow is MLB-first.

Decision:

- Keep MLB workflows strong before expanding.
- Other sports should reuse provider/player/result/prediction abstractions but need their own result coverage and mapping validation.

## Research Before Real Betting Confidence

The current system has promising signals but is still research-grade.

Decision:

- Surface probabilities, line-shopping, lifecycle state, and warnings.
- Do not market recommendations as final best bets until backtests validate them.
- Treat EV as research-only until profitable, stable subsets are proven.

## Non-Standard Markets Are Visible But Not Normal Recommendations

Promotional, Demon, Goblin, Alternate, Special, and Unknown markets can be interesting research rows.

Decision:

- Keep them visible with labels and warnings.
- Exclude them from normal recommendation eligibility by default.
- Do not compare non-standard markets directly to standard sportsbook lines unless payout semantics are captured.

## Player Alias Links Are Preferred Over Risky Bulk Merges

Historical duplicate player rows exist.

Decision:

- Use conservative read-time alias/equivalent player lookup for predictions.
- Use manual or auto-safe alias links for The Odds source players when a single safe target exists.
- Do not merge ambiguous identities such as Max Muncy variants without manual review.

## Generated Outputs Stay Out Of Project Root

Endpoint outputs, logs, and temporary build artifacts should not clutter source directories.

Decision:

- JSON/report outputs go to `tmp-analysis`.
- Run logs go to `logs`.
- Temporary build/test outputs go to `tmp-build`.
- See [GENERATED_OUTPUTS.md](GENERATED_OUTPUTS.md).
