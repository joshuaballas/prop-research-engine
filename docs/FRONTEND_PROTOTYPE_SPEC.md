We are ready to start the customer-facing frontend prototype for my SportsBetting SaaS app.

Use the existing SportsBetting repo.

Important project context:

* Existing backend/admin/research dashboard is separate and should remain the internal debug/admin tool.
* Do NOT create any admin/debug area in the new customer frontend.
* Do NOT wire real backend APIs yet.
* Do NOT wire Clerk auth yet.
* Do NOT wire Stripe yet.
* Do NOT create live refresh controls.
* Do NOT call `/api/debug/*`.
* This first pass is a static polished frontend prototype using mock data only.
* The goal is visual/design validation. If the design looks bad, I may delete it and retry.

Frontend stack:

* React
* TypeScript
* MUI
* MUI theme overrides
* Dark theme default
* Optional light theme foundation is okay, but dark theme should be the primary/default experience.

Hard design requirements:

* Use MUI components for nearly all UI/layout. Avoid raw HTML/div soup.
* Do not let the app look like default MUI.
* Disable MUI ripples globally.
* Create a custom MUI theme and style guide tokens.
* Use custom wrapper components for common UI patterns.
* The app should look like a premium dark sports analytics SaaS dashboard inspired by modern tools like OddsJam and polished dashboard demos.
* Do not make it look like a casino, sportsbook clone, generic admin template, or default Material UI app.
* Use strong visual hierarchy, dark surfaces, clean cards, compact data, polished chips/badges, and subtle gradients/borders/shadows.

Architecture requirements:

* Do not dump everything into `App.tsx`.
* `App.tsx` should only compose providers, routing, and the root app.
* Create modular pages, layouts, components, hooks, mock data, and types.
* Keep code organized and maintainable from the first pass.
* Create reusable UI components instead of repeating page-specific styling everywhere.
* Desktop table views and mobile card views should be separate reusable components where appropriate.

Suggested structure:

src/
app/
App.tsx
routes.tsx
providers.tsx

theme/
theme.ts
palette.ts
typography.ts
components.ts
shadows.ts

layouts/
PublicLayout.tsx
AuthLayout.tsx
AppLayout.tsx

pages/
public/
HomePage.tsx
PricingPage.tsx
SignInPage.tsx
CreateAccountPage.tsx

```
app/
  DashboardPage.tsx
  MarketsPage.tsx
  TopPicksPage.tsx
  LineShoppingPage.tsx
  EntryBuilderPage.tsx
  PerformancePage.tsx
  AccountPage.tsx
  BillingPage.tsx
```

components/
ui/
AppCard.tsx
AppButton.tsx
StatusPill.tsx
DataStateBanner.tsx
MetricCard.tsx
EmptyState.tsx
SectionHeader.tsx
PageHeader.tsx

```
navigation/
  PublicNav.tsx
  AppTopBar.tsx
  AppSidebar.tsx
  MobileNav.tsx
  SportToggleRail.tsx

markets/
  TopPickCard.tsx
  MarketCard.tsx
  MarketTable.tsx
  MarketFilters.tsx
  LineShoppingTable.tsx
  LineShoppingCard.tsx
  ProviderComparisonDrawer.tsx

entry-builder/
  EntryBuilderTray.tsx
  EntryBuilderSummary.tsx
  EntryLegCard.tsx
  ProjectionModifierBadge.tsx

performance/
  PerformanceSummaryCards.tsx
  PerformanceTable.tsx
  PerformanceMobileAccordion.tsx

billing/
  PlanCard.tsx
  BillingSummaryCard.tsx
```

data/
mockSports.ts
mockMarkets.ts
mockTopPicks.ts
mockPerformance.ts
mockEntryRules.ts
mockPricingPlans.ts
mockUser.ts

types/
sports.ts
markets.ts
entryBuilder.ts
billing.ts
user.ts

hooks/
useSelectedSports.ts
useEntryBuilder.ts
useToast.ts
useResponsiveView.ts

utils/
formatters.ts
entryRules.ts
payoutEstimator.ts

Required public pages:

1. Homepage
2. Pricing
3. Sign In
4. Create Account

Required authenticated app pages:

1. Dashboard
2. Markets
3. Top Picks
4. Line Shopping
5. Entry Builder
6. Performance
7. Account
8. Billing

Routing:

* `/` = public homepage
* `/pricing` = public pricing page
* `/sign-in` = sign in page
* `/create-account` = create account page
* `/app/dashboard` = signed-in dashboard mock
* `/app/markets` = market board
* `/app/top-picks` = top picks
* `/app/line-shopping` = line shopping
* `/app/entry-builder` = entry builder
* `/app/performance` = performance
* `/app/account` = account
* `/app/billing` = billing
* Redirect `/app` to `/app/dashboard`.

Auth/payment:

* Do not implement real Clerk or Stripe yet.
* Sign-in/create-account pages should be visual placeholders.
* Pricing plan cards should be DB-driven in shape, but use mock data for now.
* Do not hardcode pricing tiers directly in JSX.
* Put mock plan data in `mockPricingPlans.ts`.
* Pricing page should render from plan data.
* Do not put plan definitions in appsettings.
* Do not hardcode Stripe price IDs in the frontend.
* Frontend should conceptually select plans by `planCode`.

Pricing plan data shape should support:

* code
* name
* description
* priceCents
* currency
* billingInterval
* badgeText
* isFeatured
* features
* entitlements
* checkoutPlanCode

Future backend expectation:

* Pricing plans will eventually come from a database endpoint such as `/api/public/pricing/plans`.
* Frontend will send `planCode` to backend later.
* Backend will map `planCode` to Stripe product/price IDs from the database.

Theme/style guide:
Create a premium dark theme.

Suggested palette:

* background.default: deep near-black navy
* background.paper: dark slate
* raised surfaces: slightly lighter dark slate
* borders: subtle blue-gray
* primary accent: cyan / electric blue
* secondary/premium accent: indigo
* success/positive edge: green
* warning/stale/low sample: amber
* danger/unavailable/avoid: red
* primary text: soft white
* secondary text: slate/blue-gray

Disable ripples globally:

* `MuiButtonBase.defaultProps.disableRipple = true`

Typography:

* Use a modern SaaS-style font stack.
* Prefer Inter/Plus Jakarta Sans style if available.
* Avoid default-looking Roboto-heavy Material UI feel if possible.

Component styling:

* Buttons should not look like stock MUI.
* Cards should use dark raised surfaces, subtle borders, soft shadows, and strong hierarchy.
* Chips/badges should be compact, custom-colored, and readable.
* Tables should use sticky dark headers, compact readable rows, hover states, and right-aligned numeric values.
* Alerts/status banners should be custom-styled and app-native, not default MUI alerts.
* Inputs/filters should use dark filled/outlined styles, not default blue underline Material inputs.

Responsive design:

* Desktop can use tables/data grids.
* Mobile must NOT use tiny wide desktop tables.
* Mobile pages should use cards, accordions, filter drawers, and bottom sheets.
* Build responsive alternate views for Market Board, Line Shopping, and Performance.
* Use MUI breakpoints and `useMediaQuery`.

Sports behavior:

* App should be multi-sport by default.
* Do NOT use a single sport dropdown like “MLB only.”
* Use a polished sport toggle rail with active-state pill buttons.
* Users should be able to select multiple sports at once.
* Include sports like MLB, NBA, NFL, NHL, MMA, Golf, Tennis, Soccer in mock data where useful.
* Use generic sport icons/emojis/custom icons for now.
* Do not use official league logos in the MVP prototype.

Sport rail behavior:

* Include an “All” pill.
* Each sport pill can show sport label, icon, and candidate count/status.
* Some sports can show “Soon” or “No data.”
* Active pills should have a strong selected state.
* Mobile sport rail should be horizontally scrollable.

Customer-facing data states:
The UI must represent:

* Fresh data
* Stale data
* Provider healthy
* Provider limited
* Provider unavailable
* No usable active market data
* No active picks
* Low sample performance warning
* Snapshot/fallback-style warning conceptually, without exposing debug internals

Do not use debug language like:

* forceLive
* dryRun
* debug endpoints
* planner decision audit
* raw provider diagnostics
* API internals

Use customer-safe language like:

* Data is fresh
* Data is stale
* Provider is limited
* No active opportunities
* Market data unavailable
* Low sample size

Logged-in app shell:

* Desktop: left sidebar + topbar + main content area.
* Mobile: topbar + bottom nav or drawer.
* Include app navigation:

  * Dashboard
  * Markets
  * Top Picks
  * Line Shopping
  * Entry Builder
  * Performance
* Account menu:

  * Account
  * Billing
  * Sign out mock action

Dashboard page:

* Multi-sport dashboard, not MLB-only.
* Include sport toggle rail.
* Include data status banner.
* Include metric cards:

  * Active Candidates
  * Top EV
  * DFS Providers Live
  * Last Refresh
  * Performance Sample
* Include Top Opportunities across selected sports.
* Include Line Shopping Preview.
* Include Entry Builder Preview.
* Include Performance Snapshot.
* Include mock data showing multiple sports and multiple providers.

Markets page:

* Broad scanner page across selected sports.
* Desktop: filter bar + market table.
* Mobile: market cards.
* Filters:

  * selected sports
  * provider category
  * provider
  * prop type
  * strategy
  * candidate only
  * DFS actionable only
  * freshness
  * search player
* Rows/cards should show:

  * player
  * sport
  * prop
  * side
  * line
  * provider
  * provider type
  * entry group
  * EV
  * model probability
  * implied probability
  * edge
  * strategy
  * warnings
  * action

Top Picks page:

* Curated ranked list of best opportunities.
* Use premium pick cards.
* Show:

  * rank
  * player
  * sport
  * prop
  * side/line
  * provider
  * EV
  * model probability
  * implied probability
  * strategy
  * reasons
  * warnings
  * projection modifier if any
  * Add to Entry
  * Compare Lines

Line Shopping page:

* One of the most important pages.
* Desktop: line shopping table with expandable/detail drawer behavior if practical.
* Mobile: comparison cards.
* Show:

  * player
  * sport
  * prop
  * side
  * best DFS provider
  * best DFS line
  * best price
  * sportsbook context
  * EV
  * entry group
  * strategy
  * warnings
  * all provider lines in detail view

Entry Builder:

* Provider-locked, sport-flexible.
* Do not allow mixing DFS providers.
* Allow multiple sports inside one entry if the provider rules allow.
* Selected first leg locks the provider.
* Show:

  * provider
  * sports included
  * legs
  * allowed leg counts
  * supported entry types
  * average EV
  * projection modifiers
  * estimated payout multiplier
  * estimated payout based on entry amount
  * compatibility status
* Use mock provider rules from `mockEntryRules.ts`.

Entry Builder rules:

* PrizePicks legs only combine with PrizePicks legs.
* Underdog legs only combine with Underdog legs.
* DraftKings Pick6 legs only combine with Pick6 legs.
* Betr Picks legs only combine with Betr legs.
* Sportsbook context rows cannot be added.
* DFS providers cannot be mixed.
* Sports can be mixed unless provider rules say otherwise.

Projection modifier system:
The UI must display whether a projection is:

* Normal
* Demon
* Goblin
* Discount/Taco
* Payout Boost
* Bonus Lineup
* Unknown Promo

Modifier badges must appear on:

* Top Pick cards
* Market rows/cards
* Line Shopping comparisons
* Entry Builder legs
* Entry Builder payout summary

Entry Builder payout:

* Estimated payout should consider provider, entry type, leg count, and projection modifiers when rules are known.
* If exact modifier payout rules are unknown, show a warning like:
  “Estimated payout may vary. Confirm final payout in the provider app.”
* Do not imply guaranteed payouts.
* Use “estimated payout,” not “guaranteed payout.”

Performance page:

* Show historical strategy/provider/prop performance with strong sample-size caution.
* Include a top warning:
  “Performance is sample-size dependent. Early results should be treated as research, not proof.”
* Summary cards:

  * Candidate ROI
  * Hit Rate
  * Graded Picks
  * Average EV
  * Best Strategy
  * Low Sample Groups
* Desktop: grouped performance tables.
* Mobile: tabs + accordion cards.
* Groups:

  * by sport
  * by provider
  * by prop type
  * by strategy
  * by provider + sport

Account page:

* Mock account/profile page.
* Show:

  * profile info
  * email
  * MFA status
  * theme preference
  * save button
* Use toasts for save success/error.

Billing page:

* Mock billing page.
* Show:

  * current plan
  * subscription status
  * renewal date
  * payment method
  * manage billing button
* Use toasts for mock billing actions.

Toasts:

* Implement a custom toast provider using MUI Snackbar/Alert.
* Use `useToast()`.
* Support success, error, warning, and info.
* Desktop position: bottom-right.
* Mobile position: bottom-center.
* Use toasts for:

  * profile saved
  * billing action mock success
  * leg added
  * incompatible leg blocked
  * entry cleared
  * settings saved
* Do not use toasts for major persistent data states like provider unavailable or subscription required. Use banners/cards for those.

Mock data:
Create realistic mock data with:

* multiple sports
* multiple DFS providers
* sportsbook context rows
* candidate/watchlist/research/avoid classifications
* normal picks
* Demon picks
* Goblin picks
* Discount/Taco picks
* payout boost examples
* stale/no-data states
* low sample performance groups
* provider unavailable scenario somewhere in the UI

Important copy/tone:

* Avoid “lock,” “guaranteed,” “free money,” or “can’t lose.”
* Use:

  * candidate
  * edge
  * model lean
  * watchlist
  * research only
  * avoid
  * estimated payout
  * confirm in provider app

Public homepage:

* Premium SaaS landing page.
* Hero with value proposition and dashboard preview.
* Explain:

  * line shopping
  * top picks
  * multi-sport DFS research
  * provider-compatible entry building
  * projection modifiers like Demons/Goblins/Tacos
  * performance tracking
  * data freshness/status
* CTA buttons:

  * Create Account
  * View Pricing

Pricing page:

* Render from mock DB-shaped plan data.
* Include plan cards.
* Featured plan should visually stand out.
* Include monthly/yearly toggle visually if reasonable, but mock only.
* Include plan features and CTA.
* No hardcoded pricing cards directly in JSX.

Sign-in/Create-account pages:

* Premium auth shell.
* Split layout on desktop:

  * left side: product value / dashboard preview
  * right side: auth card
* Static mock only for now.
* Mention MFA support visually.

Build quality:

* Run build/lint if available.
* Avoid TypeScript errors.
* Keep components small and modular.
* Use meaningful file names.
* Keep styling centralized in theme/wrapper components as much as practical.
* Avoid massive inline `sx` blocks repeated everywhere.
* If page-specific `sx` is needed, keep it readable and not duplicated.

Deliverable:
A static frontend prototype that can be run locally and reviewed in the browser.
