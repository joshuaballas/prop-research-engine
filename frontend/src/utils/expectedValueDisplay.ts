import type { MarketOpportunity } from '../types/markets';

export const dfsExpectedValueTooltip =
  'EV is only shown for sportsbook-priced markets. DFS props use hit chance, edge, and trust instead.';

export function shouldShowExpectedValue(market: MarketOpportunity) {
  return market.showExpectedValue === true && typeof market.displayExpectedValue === 'number';
}

export function getDisplayExpectedValue(market: MarketOpportunity) {
  return shouldShowExpectedValue(market) ? market.displayExpectedValue : null;
}

export function getPublicRankingValue(market: MarketOpportunity) {
  return getDisplayExpectedValue(market) ?? market.probabilityEdge;
}
