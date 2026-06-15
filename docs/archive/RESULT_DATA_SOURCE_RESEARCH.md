# Result Data Source Research

> Historical note: this document predates the current MLB-first architecture. BallDontLie was evaluated here as a possible paid multi-sport results provider, but it is not part of the active runtime. Current MLB results use MLB Stats API.

Date: 2026-06-12

## Goal

Determine the best source for actual player prop outcomes that can populate `prop_results` for UFC, NBA, WNBA, MLB, and NHL.

The platform needs post-event player-level statistics that can be mapped to:

- `Player`
- `League`
- `PropType`
- `EventDate`
- `ActualResultValue`
- `ExternalSourceIdentifier`

The source must support prop grading, not merely game scores or betting odds.

## Executive Recommendation

Use **BallDontLie as the first paid/personal-project results provider**, with league-specific official/public APIs as optional supplemental sources.

Recommended order:

1. **BallDontLie** for initial multi-sport coverage across NBA, WNBA, MLB, NHL, and MMA/UFC.
2. **Official/public MLB Stats API** as a free MLB supplement or fallback.
3. **NHL public web API** as a free NHL supplement or fallback.
4. **SportsDataIO** if BallDontLie coverage proves insufficient and a commercial plan is acceptable.
5. **Sportradar** only if the project becomes commercial or needs enterprise-grade official feeds.

Avoid using **The Odds API** for result grading. It is useful for odds and limited game scores, but it does not provide the player box-score/fight-stat data needed to grade props.

Avoid relying on **StatMuse** as a system data source. It is useful for manual lookup and QA, but it does not expose a stable official API contract for automated ingestion.

## Summary Matrix

| Source | Sports Fit | Cost | Auth | Player Stats | Historical Coverage | Update Frequency | Prop Grading Fit |
|---|---:|---:|---|---|---|---|---|
| BallDontLie | NBA, WNBA, MLB, NHL, MMA/UFC | Free tier, $9.99/mo per sport, $39.99/mo per sport, $299.99/mo all-access | API key header | Yes | Varies by sport; NBA 1946-current, WNBA 2008-current, MLB 2002-current, MMA/UFC covered | Real-time or near real-time depending endpoint | Best personal-project choice |
| SportsDataIO | NBA, WNBA, MLB, NHL, MMA/UFC | Commercial, free trial / sales access | API key query/header | Yes | Commercial historical products available | Real-time feeds available | Strong, but pricing/access heavier |
| Sportradar | NBA, MLB, NHL, MMA/UFC; WNBA availability likely through basketball/global coverage | Enterprise/sales | API key / marketplace credentials | Yes | Enterprise historical coverage | Real-time/official-grade | Best enterprise choice |
| The Odds API | NBA, MLB, NHL, MMA; WNBA not evident in docs sampled | Free 500 credits/mo; paid from $30/mo | API key query | No team-roster player stats | Historical odds on paid plans; scores only recent | Scores about every 30s live | Not sufficient for result grading |
| ESPN public endpoints | NBA, WNBA, MLB, NHL; UFC/MMA varies | Free | None observed | Partial/undocumented | Informal, not guaranteed | Live scoreboard/summary style | Useful fallback/QA, risky primary |
| Official MLB Stats API | MLB | Free | None observed | Yes | Strong MLB-specific coverage | Live/final box scores | Excellent MLB-specific source |
| NHL public API | NHL | Free | None observed | Yes | Strong NHL-specific coverage | Live/final box scores | Excellent NHL-specific source |
| StatMuse | NBA, WNBA, MLB, NHL, etc. | Consumer product; no official API pricing found | Web login for product features | Query answers, not stable feed | Good for manual lookup | Query-dependent | QA only |

## BallDontLie

### Why It Fits

BallDontLie now covers the exact target set needed by this platform:

- NBA
- WNBA
- MLB
- NHL
- MMA, with UFC coverage

Its public site lists 20+ sports and pricing tiers: Free, All-Star at `$9.99/mo` per sport, GOAT at `$39.99/mo` per sport, and All-Access at `$299.99/mo` for all sports. The site also lists rate limits of 5, 60, and 600 requests/minute depending tier.

Sources:

- [BallDontLie main pricing and sports list](https://www.balldontlie.io/)
- [BallDontLie NBA docs](https://docs.balldontlie.io/)
- [BallDontLie WNBA docs](https://wnba.balldontlie.io/)
- [BallDontLie MLB docs](https://mlb.balldontlie.io/)
- [BallDontLie NHL docs](https://nhl.balldontlie.io/)
- [BallDontLie MMA docs](https://mma.balldontlie.io/)

### Authentication

API key required in the `Authorization` header.

Example from docs:

```text
Authorization: PROVIDER_API_KEY
```

### Cost

Current published pricing:

- Free: `$0/mo` per sport, basic access, 5 requests/minute.
- All-Star: `$9.99/mo` per sport, extended access, 60 requests/minute.
- GOAT: `$39.99/mo` per sport, full access, 600 requests/minute.
- All-Access: `$299.99/mo`, all sports, full access, 600 requests/minute, webhooks listed.

Important: paid tiers do not automatically apply across sports unless using All-Access.

### Historical Coverage

Published coverage varies:

- NBA: 1946-current.
- WNBA: 2008-current.
- MLB: 2002-current.
- NHL: described as comprehensive NHL data; exact earliest season was not visible in the sampled page.
- MMA: covers UFC comprehensively, with limited data for other MMA leagues.

### Update Frequency

Docs describe real-time updates for several game/player-stat endpoints:

- WNBA player stats are updated in real time for games in progress.
- NHL box scores are updated in real time for games in progress.
- NBA game data is real-time for in-progress games.

### Player Stats Availability

Strong.

Examples:

- NBA: game player stats, box scores, advanced stats, play-by-play, player props.
- WNBA: player game statistics, advanced stats, play-by-play, props.
- MLB: per-game player stats including batting, pitching, and fielding; detailed fields may be null until detailed box score data is available.
- NHL: box scores include player stats such as goals, assists, shots on goal, faceoff percentage, time on ice, blocks, hits, giveaways, takeaways.
- MMA/UFC: fights, fight results, and detailed fight statistics including significant strikes, takedowns, submissions, control time, knockdowns, and winner flag.

### Prop Grading Fit

High.

BallDontLie can directly support most standard props:

- NBA/WNBA: points, rebounds, assists, threes, PRA, points+rebounds, points+assists, rebounds+assists, double-double, triple-double.
- MLB: hits, runs, RBI, home runs, doubles, stolen bases, pitcher strikeouts, pitching outs/innings, earned runs, walks, hits allowed.
- NHL: goals, assists, points, shots on goal, saves, blocked shots, hits, power-play points.
- UFC/MMA: fight winner, significant strikes, takedowns, submissions attempted, control time, knockdowns.

The biggest implementation work will be canonical player mapping between sportsbook players and BallDontLie player IDs.

### Risks

- Multi-sport cost can rise quickly if separate GOAT plans are needed.
- Some detailed MLB fields may be temporarily null before detailed box score finalization.
- NHL per-game box scores appear usable, but the docs emphasize season stat endpoints more prominently; test NHL box score completeness before committing.
- MMA props may require careful prop-type mapping because sportsbooks use varied labels.

## SportsDataIO

### Why It Fits

SportsDataIO has league APIs for NBA, WNBA, MLB, NHL, and MMA/UFC. Their docs state that endpoints use HTTP GET and require an API key passed as a query parameter or as the `Ocp-Apim-Subscription-Key` header.

Sources:

- [SportsDataIO NBA API docs](https://sportsdata.io/developers/api-documentation/nba)
- [SportsDataIO WNBA API docs](https://sportsdata.io/developers/api-documentation/wnba)
- [SportsDataIO MLB API docs](https://sportsdata.io/developers/api-documentation/mlb)
- [SportsDataIO NHL API docs](https://sportsdata.io/developers/api-documentation/nhl)
- [SportsDataIO MMA API docs](https://sportsdata.io/developers/api-documentation/mma)

### Cost

Pricing was not clearly available from the public docs sampled. The docs reference free trials, limited access, and contacting sales for production access. Treat this as commercial/sales-driven until pricing is confirmed.

### Authentication

API key via query parameter or:

```text
Ocp-Apim-Subscription-Key: {key}
```

### Historical Coverage

SportsDataIO advertises historical data products and historical sports data archives. Exact coverage by league and endpoint should be verified during account evaluation.

### Update Frequency

SportsDataIO provides real-time sports feeds for many leagues. Exact call intervals and endpoint speed vary by endpoint and product tier.

### Player Stats Availability

Likely strong across target sports. SportsDataIO’s league APIs include scores, stats, play-by-play, betting, fantasy, and data dictionaries.

### Prop Grading Fit

High if account access includes player game stats and MMA fight stats.

### Risks

- Pricing/access may be less personal-project friendly than BallDontLie.
- Access varies by product tier and subfeed.
- Need account/trial to inspect exact response shape and historical depth.

## Sportradar

### Why It Fits

Sportradar has official-grade APIs and docs for NBA, MLB, NHL, and MMA. It is the strongest enterprise option for reliability, official coverage, and low-latency data.

Sources:

- [Sportradar NBA overview](https://developer.sportradar.com/basketball/reference/nba-overview)
- [Sportradar MLB overview](https://developer.sportradar.com/baseball/reference/mlb-overview)
- [Sportradar NHL overview](https://developer.sportradar.com/ice-hockey/reference/nhl-overview)
- [Sportradar MMA overview](https://developer.sportradar.com/mma/reference/mma-overview)

### Cost

Enterprise/sales-driven. Not recommended as the first source for a personal project unless budget is already available.

### Authentication

API access through Sportradar developer/marketplace credentials.

### Historical Coverage

Strong, but access depends on contract and products purchased.

### Update Frequency

Real-time/official-grade feeds are available. Exact latency depends on product.

### Player Stats Availability

Strong across covered sports.

### Prop Grading Fit

Very high technically, but cost and contracting make it a later-stage source.

## The Odds API

### Why It Does Not Fit Results

The Odds API is excellent for odds collection and market discovery, but it is not a player-stat outcomes provider.

Sources:

- [The Odds API docs](https://the-odds-api.com/liveapi/guides/v4/)
- [The Odds API pricing](https://the-odds-api.com/)

### Cost

Current published pricing:

- Free: 500 credits/month.
- 20K: `$30/mo`.
- 100K: `$59/mo`.
- 5M: `$119/mo`.

### Authentication

API key query parameter.

### Historical Coverage

Historical odds are available on paid plans. Scores endpoint can return completed games only up to 3 days back using `daysFrom`.

### Update Frequency

Live scores update approximately every 30 seconds.

### Player Stats Availability

No. Scores endpoint returns team/game scores, not individual player box-score stats.

### Prop Grading Fit

Low for outcomes. It can identify available prop markets and odds, but cannot grade player props by itself.

## ESPN Public Endpoints

### Why It Is Useful

ESPN exposes public JSON endpoints for scoreboards and summaries. These are useful for lightweight checks, game schedules, and some leader/stat information.

Examples sampled:

- [ESPN NBA scoreboard](https://site.api.espn.com/apis/site/v2/sports/basketball/nba/scoreboard)
- [ESPN WNBA scoreboard](https://site.api.espn.com/apis/site/v2/sports/basketball/wnba/scoreboard)
- [ESPN MLB scoreboard](https://site.api.espn.com/apis/site/v2/sports/baseball/mlb/scoreboard)
- [ESPN NHL scoreboard](https://site.api.espn.com/apis/site/v2/sports/hockey/nhl/scoreboard)

### Cost

Free public endpoints.

### Authentication

No authentication observed for sampled scoreboard endpoints.

### Historical Coverage

Undocumented. Date-based scoreboards often work, but there is no stable published contract.

### Update Frequency

Live scoreboard style data updates during games.

### Player Stats Availability

Partial. Scoreboards include teams, scores, leaders, and some statistics. Full player box scores may require undocumented summary/boxscore endpoints and can vary by sport.

### Prop Grading Fit

Medium to low as a primary source. Useful as QA or emergency fallback, risky for automated production grading.

### Risks

- Undocumented API.
- Response shape can change without notice.
- Potential terms-of-use concerns.
- Player stat completeness varies.

## Official/Public MLB Stats API

### Why It Fits

MLB’s Stats API is one of the best free sport-specific options. It can return schedules, game IDs, box scores, players, and game/player stats.

Sources:

- [MLB StatsAPI wrapper wiki](https://github.com/toddrob99/MLB-StatsAPI/wiki)
- MLB Stats API endpoint pattern: `https://statsapi.mlb.com/api/v1/...`

### Cost

Free public endpoints.

### Authentication

No authentication observed.

### Historical Coverage

Strong MLB historical coverage, though exact endpoint availability varies.

### Update Frequency

Live/final game data.

### Player Stats Availability

Strong for MLB box scores and player game stats.

### Prop Grading Fit

Excellent for MLB.

### Risks

- Documentation is community-maintained rather than an official polished developer portal.
- MLB data usage terms should be reviewed before any commercial use.
- Only solves MLB.

## NHL Public Web API

### Why It Fits

The NHL public web API exposes gamecenter boxscore data with `playerByGameStats`, including skater and goalie stats.

Sources:

- [NHL boxscore example](https://api-web.nhle.com/v1/gamecenter/2023020204/boxscore)
- [Unofficial NHL API reference](https://github.com/Zmalski/NHL-API-Reference)

### Cost

Free public endpoints.

### Authentication

No authentication observed for sampled endpoint.

### Historical Coverage

Strong for recent seasons; exact long-term historical coverage should be verified.

### Update Frequency

Live/final gamecenter data.

### Player Stats Availability

Strong for standard NHL props:

- goals
- assists
- points
- shots on goal
- saves
- blocks
- hits
- faceoff win percentage
- time on ice

### Prop Grading Fit

Excellent for NHL.

### Risks

- Not presented as a formal commercial API contract.
- Response shape can change.
- Only solves NHL.

## StatMuse

### Why It Is Not a Primary Source

StatMuse is excellent for human-readable questions and manual validation. It visibly supports NBA, WNBA, NHL, MLB, and other domains, but no stable official API was found during this research.

Source:

- [StatMuse](https://www.statmuse.com/)

### Cost

Consumer pricing/API pricing for automated data access was not found in the sampled public pages.

### Authentication

Website login exists, but no official data API authentication contract was found.

### Historical Coverage

Strong for manual querying, unclear for automated extraction.

### Update Frequency

Query-driven.

### Player Stats Availability

Yes for human search answers, but not via stable machine API.

### Prop Grading Fit

Low as an automated source; useful for QA and spot checks.

## League-by-League Recommendation

### NBA

Recommended:

1. BallDontLie GOAT NBA, if budget allows.
2. NBA public/stats endpoints as exploratory fallback.
3. SportsDataIO if a more commercial feed is needed.
4. Sportradar for enterprise.

BallDontLie has historical NBA data from 1946-current and game player stats. It is the cleanest personal-project path.

### WNBA

Recommended:

1. BallDontLie GOAT WNBA.
2. ESPN public endpoints only as QA/fallback.
3. SportsDataIO if more support/commercial reliability is needed.

BallDontLie’s WNBA docs explicitly list data from 2008-current and player game statistics. This is unusually useful for a personal project because WNBA public data is often less convenient than NBA.

### MLB

Recommended:

1. MLB Stats API for free canonical MLB results.
2. BallDontLie if a consistent multi-sport provider is preferred.
3. SportsDataIO/Sportradar for commercial reliability.

MLB is the easiest sport to support without paid data.

### NHL

Recommended:

1. NHL public web API for free canonical NHL results.
2. BallDontLie if a consistent multi-sport provider is preferred.
3. SportsDataIO/Sportradar for commercial reliability.

NHL public boxscores can directly grade common NHL props.

### UFC / MMA

Recommended:

1. BallDontLie MMA/UFC GOAT.
2. SportsDataIO MMA if BallDontLie coverage is insufficient.
3. Sportradar MMA for enterprise.
4. UFCStats scraping only as a last-resort research fallback.

UFC/MMA is where BallDontLie is most valuable for a personal project because official free structured result data is less convenient than MLB/NHL.

## Recommended Implementation Path

### Phase 1: BallDontLie Evaluation Spike

Create a non-persistent debug spike for:

- WNBA player stats by game/date.
- NBA player stats by game/date.
- MLB player stats by game/date.
- NHL box scores by game/date.
- MMA fight stats by event/fight.

Validate:

- Can each endpoint produce one row per player/fighter per game/fight?
- Are player IDs stable?
- Are event/game IDs stable?
- Can sportsbook player names be matched to provider players?
- Are all active `PropType` values gradeable?

### Phase 2: Provider Metadata Mapping

Extend result ingestion planning around:

- provider player ID
- provider game/fight ID
- provider league ID
- source endpoint
- raw payload hash
- stat mapping table or code map from `PropType.StatKey` to provider stat fields

No new identity system should be introduced; use provider IDs as external refs for existing canonical players.

### Phase 3: Free MLB/NHL Supplement

Implement MLB and NHL public APIs only if:

- BallDontLie multi-sport cost is too high, or
- provider completeness is insufficient, or
- free backfill is needed.

### Phase 4: Commercial Upgrade Decision

Move to SportsDataIO or Sportradar only if:

- prop grading becomes business-critical,
- uptime/support matters,
- latency matters,
- contractual data rights matter,
- free/public APIs become unstable.

## Final Recommendation

For this personal project, start with **BallDontLie All-Access trial** or individual GOAT trials for the highest-volume leagues. It has the best combination of:

- target sport coverage,
- published API docs,
- player-level stats,
- MMA/UFC support,
- usable pricing,
- API-key auth,
- prop-oriented endpoints,
- enough historical coverage for backtesting.

Use official/free MLB and NHL APIs as excellent sport-specific supplements. Do not spend time trying to make The Odds API or StatMuse serve as outcome providers; they do not fit the prop-result ingestion problem cleanly.
