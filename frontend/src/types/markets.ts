import type { SportCode } from './sports';

export type ProviderCategory = 'DFS' | 'Sportsbook';
export type StrategyClassification = 'Candidate' | 'Watchlist' | 'Research' | 'Avoid' | 'ResearchOnly';
export type ProjectionModifier =
  | 'Normal'
  | 'Alternate'
  | 'Demon'
  | 'Goblin'
  | 'Taco'
  | 'Promo'
  | 'Discount'
  | 'Payout Boost'
  | 'Bonus Lineup'
  | 'Special'
  | 'Unknown Promo'
  | 'Unknown';

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
  eventStartTimeUtc?: string;
  sport: Exclude<SportCode, 'ALL'>;
  propType: string;
  side: 'Over' | 'Under';
  line: number;
  provider: string;
  providerCategory: ProviderCategory;
  isDfs?: boolean;
  isSportsbook?: boolean;
  showExpectedValue?: boolean;
  displayExpectedValue?: number | null;
  expectedValueUnavailableReason?: string | null;
  sportsbookContextResearchOnly?: boolean;
  isResearchOnly?: boolean;
  resultGradingSupported?: boolean;
  resultGradingUnavailableReason?: string | null;
  entryUnavailableReason?: string | null;
  entryCompatibilityGroup: string;
  entryProvider: string;
  canAddToEntry: boolean;
  bestAmericanPrice: number;
  bestDecimalPrice: number;
  modelProbability: number | null;
  selectedSideProbability?: number | null;
  modelProbabilityAvailable?: boolean;
  hitChanceUnavailableReason?: string | null;
  modelOverProbability?: number | null;
  modelUnderProbability?: number | null;
  probabilitySource?: string | null;
  impliedProbability: number | null;
  probabilityEdge: number | null;
  estimatedEv: number;
  strategyClassification: StrategyClassification;
  strategyTier: string;
  trustLevel: 'Strong' | 'Medium' | 'Weak' | 'Research';
  marketShape: string;
  historicalSampleSize: number;
  freshness: 'Fresh' | 'Stale' | 'Unavailable';
  projectionModifier: ProjectionModifier;
  projectionType?: string;
  projectionTypeConfidence?: 'Known' | 'Inferred' | 'Unknown' | string;
  isAlternateMarket?: boolean;
  payoutMultiplierMayDiffer?: boolean;
  specialProjectionWarning?: string | null;
  reasons: string[];
  warnings: string[];
  sportsbookContext?: string;
  providerLines?: ProviderLine[];
  playerHistorySummary?: PlayerHistorySummary | null;
}

export interface ProviderLine {
  provider: string;
  providerCategory: ProviderCategory;
  line: number;
  overPrice?: number;
  underPrice?: number;
  note?: string;
}

export interface PlayerHistorySummary {
  available: boolean;
  unavailableReason?: string | null;
  provider?: string;
  providerPlayerId?: string | null;
  sampleSize?: number;
  latestResultDate?: string | null;
  stats?: Partial<Record<PlayerHistoryStatKey, PlayerHistoryStatSummary>>;
  competitionBreakdown?: PlayerHistoryCompetitionBreakdown[];
  recentGameLog?: PlayerHistoryGameLogRow[];
}

export type PlayerHistoryStatKey = 'Goals' | 'Assists' | 'Shots' | 'ShotsOnTarget';

export interface PlayerHistoryStatSummary {
  count?: number;
  average?: number;
  last5Average?: number;
  last10Average?: number;
}

export interface PlayerHistoryCompetitionBreakdown {
  competitionKey?: string | null;
  competitionName?: string | null;
  sampleSize?: number;
  latestResultDate?: string | null;
}

export interface PlayerHistoryGameLogRow {
  eventDate?: string | null;
  competition?: string | null;
  competitionKey?: string | null;
  fixtureId?: string | null;
  stats?: Partial<Record<PlayerHistoryStatKey, number>>;
}
