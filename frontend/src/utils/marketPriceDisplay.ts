import type { MarketOpportunity, ProviderLine } from '../types/markets';
import { formatAmericanOdds } from './formatters';

type MarketPriceFields = Pick<MarketOpportunity, 'providerCategory' | 'bestAmericanPrice' | 'effectiveAmericanOdds'>;

export function getSelectedMarketAmericanOdds(market: MarketPriceFields) {
  if (market.providerCategory !== 'Sportsbook') {
    return null;
  }

  return firstUsableAmericanOdds(market.effectiveAmericanOdds, market.bestAmericanPrice);
}

export function formatSelectedMarketAmericanOdds(market: MarketPriceFields, fallback: string | null = 'Odds unavailable') {
  const odds = getSelectedMarketAmericanOdds(market);
  if (odds === null) {
    if (market.providerCategory !== 'Sportsbook') {
      return null;
    }

    return fallback;
  }

  return formatAmericanOdds(odds);
}

export function formatProviderLinePickPrice(line: ProviderLine, side: MarketOpportunity['side']) {
  const price = line.providerCategory === 'Sportsbook'
    ? firstUsableAmericanOdds(side === 'Over' ? line.overPrice : line.underPrice)
    : null;
  const pick = `${side} ${line.line}`;
  return price === null ? pick : `${pick} · ${formatAmericanOdds(price)}`;
}

function firstUsableAmericanOdds(...values: Array<number | null | undefined>) {
  for (const value of values) {
    if (value !== null && value !== undefined && Number.isFinite(value) && Math.abs(value) >= 0.0001) {
      return value;
    }
  }

  return null;
}
