# Sports Pick'em Analyzer

## Overview

Build a web application that analyzes PrizePicks, Underdog Fantasy, Sleeper Picks, and similar fantasy pick'em operators.

The goal is to identify the highest probability and highest expected value entries based on:

- Current prop lines
- Historical line movement
- Cross-platform line comparison
- Statistical projections
- Correlation between picks

This is NOT an OddsJam clone.

This application is focused on fantasy pick'em operators rather than traditional sportsbooks and arbitrage betting.

---

# Primary Objectives

1. Collect all active props from supported platforms.
2. Store historical line movement.
3. Compare lines across platforms.
4. Calculate estimated probability of a prop hitting.
5. Calculate expected value.
6. Identify correlations between props.
7. Generate optimized 2-leg through 6-leg entries.
8. Rank entries by expected value and confidence.

---

# Supported Platforms

Initial Targets:

- PrizePicks
- Underdog Fantasy

Future Targets:

- Sleeper Picks
- ParlayPlay
- Chalkboard

---

# Core Features

## Data Collection

The system should:

- Collect all active props
- Refresh on a configurable schedule
- Store snapshots of every observed line
- Detect line movement

Example:

9:00 AM
Patrick Mahomes Passing Yards = 278.5

10:00 AM
Patrick Mahomes Passing Yards = 281.5

11:00 AM
Patrick Mahomes Passing Yards = 284.5

Historical movement should be preserved.

---

## Prop Analysis

For every prop:

Calculate:

- Estimated hit probability
- Confidence score
- Historical hit rate
- Platform comparisons
- Recent line movement

---

## Correlation Analysis

The system should detect relationships between props.

Examples:

Positive Correlation:
- QB Passing Yards Over
- WR Receiving Yards Over

Negative Correlation:
- RB Rushing Yards Over
- QB Passing Yards Over

Correlation should be used when generating entries.

---

## Parlay Optimization

Generate optimized entries using:

- Expected Value
- Hit Probability
- Correlation Analysis

Supported Entry Sizes:

- 2 Leg
- 3 Leg
- 4 Leg
- 5 Leg
- 6 Leg

---

# Dashboard Requirements

Display:

- Active Props
- Best Plays
- Line Movers
- Recommended Entries
- Historical Trends
- Platform Comparisons

---

# Technical Stack

Backend:

- ASP.NET Core 10
- Entity Framework Core
- PostgreSQL

Frontend:

- React
- Material UI

Infrastructure:

- Docker
- Background Workers

---

# Phase 1

Goal:

Collect and store data.

Deliverables:

- Database schema
- Platform integrations
- Historical line storage
- Background jobs

---

# Phase 2

Goal:

Visualization.

Deliverables:

- Dashboard
- Prop explorer
- Search and filtering
- Line movement charts

---

# Phase 3

Goal:

Probability engine.

Deliverables:

- Hit probability calculations
- Confidence scoring
- Projection modeling

---

# Phase 4

Goal:

Correlation engine.

Deliverables:

- Correlation calculations
- Correlation database
- Correlation visualizations

---

# Phase 5

Goal:

Parlay optimizer.

Deliverables:

- Entry generation
- EV calculations
- Ranking system

---

# Success Criteria

The application can:

1. Collect and store all active props.
2. Track historical line movement.
3. Calculate estimated hit probability.
4. Generate optimized entries.
5. Present information through a professional dashboard.