# PrizePicks Direct Ingestion Deprecated

PrizePicks direct ingestion is deprecated and scheduled for complete removal due to DataDome blocking.

Do not attempt DataDome bypass, browser scraping, or browser-automation workarounds.

PrizePicks can be researched through The Odds API DFS feed instead:

- region: `us_dfs`
- bookmaker key: `prizepicks`
- endpoint path: `POST /api/debug/the-odds/mlb/recommendations/refresh-current?includeDfs=true`

DFS odds from provider feeds are indicative and can vary based on user selections. Keep the old direct PrizePicks scraper/API/browser paths disabled until they are removed.

Do not add new dependencies on direct PrizePicks ingestion. Revisit PrizePicks only through The Odds API `us_dfs` or another official/allowed provider integration.
