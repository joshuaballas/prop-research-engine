# Free Results Provider Research

> Historical note: BallDontLie is discussed here as a possible fallback provider. It is not part of the active runtime and no local setup requires a BallDontLie API key.

Date: 2026-06-12

## Goal

Identify free, public, official, or community-maintained result data sources that can replace or supplement BallDontLie for `prop_results`.

Priority sports:

1. UFC / MMA
2. NBA
3. WNBA
4. NFL
5. MLB
6. NHL

The key requirement is player-level outcome data that can grade sportsbook player props.

## Executive Summary

If the goal is to avoid recurring subscriptions for as long as possible, the recommended strategy is a **sport-specific free provider stack**:

| Sport | Recommended Free Source | Fit |
|---|---|---|
| UFC / MMA | UFCStats scraper | Best free source, but highest scraping risk |
| NBA | stats.nba.com via `nba_api` | Strong player box-score coverage, unofficial access pattern |
| WNBA | stats.nba.com / WNBA stats via `nba_api` or `py_ball` | Viable, but more fragile than NBA |
| NFL | nflverse data (`nfl_data_py`, `nflfastR`, `nflreadr`) | Excellent historical/player stat coverage, not official live feed |
| MLB | MLB Stats API | Best free source overall |
| NHL | NHL public web API | Strong free source for box scores and player stats |

BallDontLie remains attractive because it normalizes access across sports, but it is not necessary as the first results provider if the project can tolerate sport-specific adapters and some scraping/API fragility.

## Source Comparison Table

| Source | Sports | Cost | Auth | Rate Limits | Historical Lookup | Player Stats | Active Maintenance | Prop Grading Fit |
|---|---|---:|---|---|---|---|---|---|
| UFCStats | UFC/MMA | Free | None observed | Not published | Yes, completed events and fight pages | Yes, fight-level stats | Website active; scrapers community-maintained | High for UFC, scraping risk |
| stats.nba.com | NBA/WNBA | Free | None, but headers required | Not published; throttling likely | Yes | Yes, box scores, traditional/advanced stats | Official site API, unofficial use | High |
| `nba_api` | NBA, partial WNBA support through stats endpoints | Free OSS | None directly; wraps stats.nba.com | Same as stats.nba.com | Yes | Yes | Active GitHub project | High for NBA |
| `py_ball` | NBA/WNBA | Free OSS | None directly; wraps NBA/WNBA endpoints | Same as stats.nba.com | Yes | Yes | Community project; verify freshness | Medium-high |
| nflverse / `nfl_data_py` | NFL | Free OSS/data | None for public datasets | GitHub/data host dependent | Yes, strong | Yes, weekly/player stats | Very active ecosystem | High for settled historical grading |
| MLB Stats API | MLB | Free | None observed | Not formally published | Yes | Yes, box score/player stats | Official MLB public API | Very high |
| NHL public web API | NHL | Free | None observed | Not formally published | Yes | Yes, skater/goalie box stats | Public league API, unofficial docs | Very high |
| ESPN public APIs | NBA, WNBA, NFL, MLB, NHL, UFC | Free | None observed | Not published | Some date support | Partial, inconsistent | Public but undocumented | QA/fallback only |
| StatMuse | Multi-sport | Free consumer site | Web product, no official API | N/A | Query-based | Human-readable answers | Product active | Manual QA only |

## Coverage By Sport

## 1. UFC / MMA

### Recommended Free Source: UFCStats

Source:

- [UFCStats completed events](http://ufcstats.com/statistics/events/completed?page=all)
- [UFCStats statistics landing page](http://ufcstats.com/statistics/fighters)

### What It Provides

UFCStats is the official-looking public stats site commonly used for UFC fight data. It exposes:

- Completed event history.
- Event pages.
- Fight detail pages.
- Per-fighter fight totals.
- Significant strike breakdowns.
- Takedown attempts and successes.
- Submission attempts.
- Reversals.
- Control time.
- Knockdowns.
- Fight result, method, round, and time.

### UFC-Specific Requirements

| Requirement | Available From UFCStats? | Notes |
|---|---:|---|
| Significant strikes | Yes | Fight detail pages include significant strike totals and breakdowns. |
| Takedowns | Yes | Includes successful/attempted takedowns. |
| Control time | Yes | Exposed as `CTRL` on fight stat tables. |
| Submission attempts | Yes | Exposed as `SUB` / submission attempts. |
| Historical fight lookup | Yes | Completed events list links to historical events and fights. |
| Fighter identity | Partial | Fighter pages have stable URLs/IDs, but no formal API contract. |

### Rate Limits

No formal rate limits are published. Treat as a public website, not an API:

- Use slow request cadence.
- Cache raw event/fight pages.
- Avoid parallel scraping.
- Add backoff and retries.
- Expect occasional HTML changes.

### Authentication

No authentication observed.

### Active Maintenance

The UFCStats website remains active, but there is no formal API. Community scrapers exist, but maintenance varies. For this project, a small custom scraper may be safer than depending on an abandoned package.

### Prop Grading Fit

High for UFC props such as:

- Fight winner.
- Significant strikes.
- Takedowns.
- Submission attempts.
- Knockdowns.
- Control time.
- Fight duration / round / method.

### Risks

- HTML scraping is brittle.
- No SLA or formal terms for automated access.
- Fighter names can include aliases, accents, and spelling variance.
- Live/in-progress grading is not realistic; use post-event finalized data.

### Recommendation

Use UFCStats as the first free UFC result source. It is likely the only no-subscription option that supports the detailed stat categories needed for UFC prop grading.

## 2. NBA

### Recommended Free Source: stats.nba.com via `nba_api`

Sources:

- [nba_api GitHub](https://github.com/swar/nba_api)
- [nba_api documentation](https://nba-api-sbang.readthedocs.io/en/latest/)
- [stats.nba.com](https://www.nba.com/stats)

### API Availability

The NBA stats site has JSON endpoints used by the public web app. `nba_api` wraps many of these endpoints and is the best community-supported way to interact with them.

Useful endpoint families:

- Scoreboard / games by date.
- Box score traditional.
- Box score advanced.
- Player game logs.
- Player profiles.
- League game finder.

### Authentication

No API key, but requests typically require browser-like headers:

- `User-Agent`
- `Referer: https://www.nba.com/`
- `Origin: https://www.nba.com`
- `Accept-Language`

### Rate Limits

No formal public limit. Throttling and blocking can happen. Use:

- Low concurrency.
- Request caching.
- Retry with backoff.
- Daily backfill jobs instead of aggressive live polling.

### Historical Lookup

Strong. Historical seasons and game IDs can be queried through stats endpoints.

### Player-Level Stats

Strong. Supports common props:

- Points.
- Rebounds.
- Assists.
- 3PM.
- Steals.
- Blocks.
- Turnovers.
- Minutes.
- PRA and other combo props can be derived.

### Active Maintenance

`nba_api` is actively used and maintained by the community. The underlying endpoints are unofficial and can change.

### Prop Grading Fit

High.

### Recommendation

Use stats.nba.com through `nba_api` as the first free NBA result provider.

## 3. WNBA

### Recommended Free Source: stats.nba.com / WNBA stats endpoints

Sources:

- [WNBA stats site](https://stats.wnba.com/)
- [py_ball GitHub](https://github.com/basketballrelativity/py_ball)
- [nba_api GitHub](https://github.com/swar/nba_api)

### API Availability

WNBA stats uses a similar stats infrastructure to NBA. Some community tooling, especially `py_ball`, explicitly targets NBA and WNBA stats endpoints.

### Authentication

No API key, but browser-like headers are usually required.

### Rate Limits

No formal public limit. Treat similarly to stats.nba.com:

- Low concurrency.
- Cache raw responses.
- Retry with backoff.
- Avoid excessive historical backfills.

### Historical Lookup

Available, though less convenient and less documented than NBA.

### Player-Level Stats

Strong enough for common WNBA props:

- Points.
- Rebounds.
- Assists.
- 3PM.
- Steals.
- Blocks.
- Turnovers.
- Minutes.
- Combo props derivable.

### Active Maintenance

The WNBA stats site is active. Community tooling is less robust than NBA tooling, so verify endpoints during implementation.

### Prop Grading Fit

High if the endpoints remain accessible.

### Recommendation

Use WNBA stats endpoints directly or through `py_ball` / compatible `nba_api` endpoint wrappers. Keep BallDontLie as a fallback only if WNBA endpoint friction becomes too high.

## 4. NFL

### Recommended Free Source: nflverse

Sources:

- [nflverse](https://nflverse.nflverse.com/)
- [nflfastR GitHub](https://github.com/nflverse/nflfastR)
- [nfl_data_py GitHub](https://github.com/nflverse/nfl_data_py)
- [nflreadr GitHub](https://github.com/nflverse/nflreadr)

### API Availability

nflverse is not a traditional API. It is a community-maintained data ecosystem with downloadable datasets and language-specific clients.

Useful datasets:

- Weekly player stats.
- Play-by-play.
- Rosters.
- Schedules.
- Player IDs.
- Participation/snap data in related datasets.

### Authentication

No auth for public datasets.

### Rate Limits

Depends on the hosting location. Generally better handled as batch data downloads, not high-frequency API calls.

### Historical Lookup

Excellent. nflverse is one of the best free historical football data ecosystems.

### Player-Level Stats

Strong for settled stats:

- Passing yards.
- Passing TDs.
- Interceptions.
- Rushing yards.
- Receiving yards.
- Receptions.
- Targets.
- Tackles and defensive stats depending dataset.
- Kicking stats.

### Active Maintenance

Very active community ecosystem.

### Prop Grading Fit

High for post-game settled props. We should not expect it to behave like a live official stat feed.

### Recommendation

Use nflverse for NFL result grading and historical backtesting. It is likely better than a generic paid API for early personal-project NFL work.

## 5. MLB

### Recommended Free Source: MLB Stats API

Sources:

- [MLB StatsAPI community wiki](https://github.com/toddrob99/MLB-StatsAPI/wiki)
- MLB endpoint pattern: `https://statsapi.mlb.com/api/v1/...`

### API Availability

MLB has a strong public Stats API. It supports schedules, games, box scores, players, teams, and game feed data.

Useful endpoints:

- Schedule by date.
- Game feed by gamePk.
- Box score by gamePk.
- Player lookup.

### Authentication

No authentication observed.

### Rate Limits

No formal public rate limit found. Use polite request rates and caching.

### Historical Lookup

Strong. Historical schedules and game feeds are available.

### Player-Level Stats

Excellent for common props:

- Hits.
- Runs.
- RBIs.
- Home runs.
- Total bases.
- Stolen bases.
- Pitcher strikeouts.
- Outs recorded / innings pitched.
- Walks.
- Earned runs.
- Hits allowed.

### Active Maintenance

The API backs MLB properties and is widely used. Community docs are active enough for practical implementation.

### Prop Grading Fit

Very high.

### Recommendation

Use MLB Stats API as the first MLB results provider. It is the strongest free provider in this research.

## 6. NHL

### Recommended Free Source: NHL Public Web API

Sources:

- [NHL boxscore example](https://api-web.nhle.com/v1/gamecenter/2023020204/boxscore)
- [NHL API reference](https://github.com/Zmalski/NHL-API-Reference)

### API Availability

The NHL public web API exposes schedules, gamecenter box scores, play-by-play, standings, teams, and player data.

Useful endpoints:

- Schedule by date.
- Gamecenter boxscore.
- Gamecenter play-by-play.
- Player landing pages.

### Authentication

No authentication observed for public endpoints.

### Rate Limits

No formal public limit. Use caching, low concurrency, and daily finalization.

### Historical Lookup

Strong for recent seasons and gamecenter IDs. Longer historical coverage should be verified during implementation.

### Player-Level Stats

Strong for common NHL props:

- Goals.
- Assists.
- Points.
- Shots on goal.
- Saves.
- Blocks.
- Hits.
- Time on ice.
- Faceoffs in some contexts.

### Active Maintenance

The public API is used by NHL web properties. Documentation is unofficial/community-maintained.

### Prop Grading Fit

Very high.

### Recommendation

Use NHL public web API as the first NHL results provider.

## Cost Comparison

| Strategy | Monthly Cost | Notes |
|---|---:|---|
| Sport-specific free stack | $0 | Highest engineering complexity; no formal SLAs. |
| BallDontLie per-sport paid plans | Variable recurring | Simpler integration; cost grows with sports. |
| BallDontLie all-access | Higher recurring | Single normalized provider, but more than needed early. |
| SportsDataIO / Sportradar | Commercial / enterprise | Best reliability; not personal-project friendly. |

Free stack cost is `$0`, but the real cost is maintenance:

- Scraper breakage.
- Endpoint shape changes.
- Rate-limit handling.
- Sport-specific adapters.
- Player identity reconciliation.

## Technical Complexity Comparison

| Sport | Free Source Complexity | Reason |
|---|---|---|
| UFC / MMA | High | HTML scraping; detailed stat parsing; no formal API. |
| NBA | Medium | Unofficial JSON endpoints; headers/throttling. |
| WNBA | Medium-high | Similar to NBA but less documented/tool-supported. |
| NFL | Low-medium | Excellent community data, but batch-oriented rather than live. |
| MLB | Low | Public API is rich and stable enough. |
| NHL | Low-medium | Public API is clean, docs are unofficial. |

## Risks

### Legal / Terms Risk

Free/public does not always mean unrestricted automated use. Before commercializing, review terms for:

- UFCStats.
- NBA/WNBA stats sites.
- MLB Stats API.
- NHL public API.
- nflverse data licenses.

### Reliability Risk

Free sources usually lack:

- SLA.
- Versioning guarantees.
- Support.
- Formal deprecation notices.

### Mapping Risk

Every provider has its own player IDs. The existing `player_external_refs` table is the right foundation, but free sources will require sport-specific mapping workflows:

- UFC fighter names and aliases.
- NBA/WNBA stats IDs.
- NFL GSIS / nflverse IDs.
- MLBAM player IDs.
- NHL player IDs.

### Data Finalization Risk

Player stats can change after games/fights:

- Stat corrections.
- Official scoring updates.
- Delayed box score finalization.

Each provider should support re-import/update by `external_source` + `external_source_identifier`.

## Recommended Provider Per Sport

| Sport | First Choice | Fallback |
|---|---|---|
| UFC / MMA | UFCStats scraper | BallDontLie MMA only if scraper proves too brittle |
| NBA | stats.nba.com via `nba_api` | BallDontLie NBA |
| WNBA | WNBA stats endpoints / `py_ball` | BallDontLie WNBA |
| NFL | nflverse weekly/player stats | ESPN public API for QA only; paid provider if live grading needed |
| MLB | MLB Stats API | BallDontLie MLB |
| NHL | NHL public web API | BallDontLie NHL |

## Recommended No-Subscription Strategy

If we wanted to avoid recurring subscriptions for as long as possible, I would recommend this plan:

1. **Keep BallDontLie as a documented fallback option only**, but do not keep active code or subscribe yet.
2. **Implement MLB first using MLB Stats API** because it is the lowest-risk free provider.
3. **Implement NHL next using the NHL public web API** because it has clean player box-score data.
4. **Implement NFL through nflverse** for historical and settled post-game grading.
5. **Implement NBA through stats.nba.com / `nba_api`**, with conservative request headers, caching, and retries.
6. **Implement WNBA after NBA**, reusing the same stats endpoint approach but treating it as a separate adapter because endpoint behavior may differ.
7. **Implement UFCStats as a scraper-backed provider**, with raw HTML persistence/caching and a parser test suite.

For this project, UFC should not be the first implementation despite being the top priority sport, because it has the highest scraping complexity. A better path is:

- Prove the results ingestion architecture with MLB or NHL.
- Reuse the same `prop_results` and `player_external_refs` flow.
- Then tackle UFCStats with more confidence and stronger parser tests.

## Final Recommendation

Do **not** make BallDontLie the default paid dependency yet.

Use a free, sport-specific provider stack:

- UFC: UFCStats scraper.
- NBA: stats.nba.com through `nba_api`.
- WNBA: WNBA stats endpoints through `py_ball` or direct stats calls.
- NFL: nflverse.
- MLB: MLB Stats API.
- NHL: NHL public web API.

This strategy avoids recurring subscriptions and gives the project the broadest runway. The tradeoff is higher engineering and maintenance effort, especially for UFC and NBA/WNBA endpoint access. BallDontLie should remain a fallback provider for the sports where free sources become too brittle or too time-consuming to maintain.
