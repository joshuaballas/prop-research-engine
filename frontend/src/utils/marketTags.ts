import type { MarketOpportunity, StrategyClassification } from '../types/markets';
import { palette } from '../theme/palette';

const tagLabels: Record<string, string> = {
  Candidate: 'Featured',
  TopPick: 'Top Pick',
  'Top Pick': 'Top Pick',
  Watchlist: 'Watchlist',
  Research: 'Research',
  Avoid: 'Blocked'
};

export const marketTagTextColors: Record<string, string> = {
  Candidate: '#FACC15',
  Featured: '#FACC15',
  'Top Pick': '#FACC15',
  Watchlist: palette.warning,
  Research: '#CBD5E1',
  Avoid: palette.danger,
  Blocked: palette.danger
};

export function marketTagValue(market: MarketOpportunity) {
  return market.strategyClassification;
}

export function marketTagLabel(value: string) {
  return tagLabels[value as StrategyClassification] ?? tagLabels[value] ?? humanizeTag(value);
}

function humanizeTag(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}
