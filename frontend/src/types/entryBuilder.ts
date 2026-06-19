import type { MarketOpportunity, ProjectionModifier } from './markets';

export interface EntryRule {
  provider: string;
  providerCategory: 'DFS' | 'Sportsbook';
  actionableForUser: boolean;
  compatibilityGroup: string;
  minLegs: number | null;
  maxLegs: number | null;
  allowedLegCounts: number[];
  entryTypes: string[];
  supportsFlex: boolean | 'Unknown';
  supportsPower: boolean | 'Unknown';
  supportsModifiers: boolean | 'Unknown';
  sameGameAllowed: boolean | 'Unknown';
  sourceConfidence: 'Known' | 'Assumed' | 'NeedsVerification';
  notes: string;
  multiplierTable?: EntryMultiplierRule[];
}

export interface EntryLeg extends MarketOpportunity {
  stakeImpact?: number;
}

export interface PayoutEstimate {
  multiplier: number | null;
  estimatedPayout: number | null;
  warning?: string;
  modifiers: ProjectionModifier[];
}

export interface EntryMultiplierRule {
  entryType: string;
  legCount: number;
  multiplier: number;
  sourceConfidence: 'Known' | 'Assumed' | 'NeedsVerification';
  lastVerifiedAtUtc?: string | null;
  notes: string[];
}
