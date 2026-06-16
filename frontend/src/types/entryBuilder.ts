import type { MarketOpportunity, ProjectionModifier } from './markets';

export interface EntryRule {
  provider: string;
  providerCategory: 'DFS' | 'Sportsbook';
  actionableForUser: boolean;
  compatibilityGroup: string;
  minLegs: number;
  maxLegs: number;
  allowedLegCounts: number[];
  entryTypes: string[];
  supportsFlex: boolean | 'Unknown';
  supportsPower: boolean | 'Unknown';
  supportsModifiers: boolean | 'Unknown';
  sameGameAllowed: boolean | 'Unknown';
  sourceConfidence: 'Known' | 'Assumed' | 'NeedsVerification';
  notes: string;
}

export interface EntryLeg extends MarketOpportunity {
  stakeImpact?: number;
}

export interface PayoutEstimate {
  multiplier: number;
  estimatedPayout: number;
  warning?: string;
  modifiers: ProjectionModifier[];
}
