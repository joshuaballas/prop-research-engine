import { mockMarkets } from './mockMarkets';

export const mockTopPicks = mockMarkets
  .filter((market) => market.strategyClassification === 'Candidate' || market.strategyClassification === 'Watchlist')
  .sort((a, b) => b.estimatedEv - a.estimatedEv)
  .map((market, index) => ({ ...market, rank: index + 1 }));
