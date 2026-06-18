import type { SportCode } from './sports';

export type ProviderCategory = 'DFS' | 'Sportsbook';
export type StrategyClassification = 'Candidate' | 'Watchlist' | 'Research' | 'Avoid' | 'ResearchOnly';
export type ProjectionModifier = 'Normal' | 'Demon' | 'Goblin' | 'Discount' | 'Payout Boost' | 'Bonus Lineup' | 'Unknown Promo';

export interface MarketOpportunity {
  id: string;
  rank?: number;
  playerId?: string;
  canonicalPlayerId?: string;
  player: string;
  team?: string;
  opponent?: string;
  event: string;
  startTime: string;
  sport: Exclude<SportCode, 'ALL'>;
  propType: string;
  side: 'Over' | 'Under';
  line: number;
  provider: string;
  providerCategory: ProviderCategory;
  entryCompatibilityGroup: string;
  entryProvider: string;
  canAddToEntry: boolean;
  bestAmericanPrice: number;
  bestDecimalPrice: number;
  modelProbability: number;
  impliedProbability: number;
  probabilityEdge: number;
  estimatedEv: number;
  strategyClassification: StrategyClassification;
  strategyTier: string;
  trustLevel: 'Strong' | 'Medium' | 'Weak' | 'Research';
  marketShape: string;
  historicalSampleSize: number;
  freshness: 'Fresh' | 'Stale' | 'Unavailable';
  projectionModifier: ProjectionModifier;
  reasons: string[];
  warnings: string[];
  sportsbookContext?: string;
  providerLines?: ProviderLine[];
}

export interface ProviderLine {
  provider: string;
  providerCategory: ProviderCategory;
  line: number;
  overPrice?: number;
  underPrice?: number;
  note?: string;
}
