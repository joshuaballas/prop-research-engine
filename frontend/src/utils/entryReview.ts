import type { EntryLeg, EntryRule } from '../types/entryBuilder';
import { estimatePayout } from './payoutEstimator';

export type EntryReviewStatus = 'Ready' | 'Needs more legs' | 'Too many legs' | 'Incompatible legs';

export interface EntryReview {
  rule?: EntryRule;
  provider: string;
  legCount: number;
  allowedFinalLegCounts: number[];
  entryTypes: string[];
  selectedEntryType: string;
  status: EntryReviewStatus;
  blockers: string[];
  warnings: string[];
  multiplier: number | null;
  estimatedPayout: number | null;
  payoutUnavailableReason?: string;
}

export function buildEntryReview(legs: EntryLeg[], rules: EntryRule[], amount: number, selectedEntryType?: string): EntryReview {
  const rule = legs[0] ? rules.find((item) => item.compatibilityGroup === legs[0].entryCompatibilityGroup) : undefined;
  const provider = rule?.provider ?? legs[0]?.entryProvider ?? 'No provider selected';
  const legCount = legs.length;
  const allAllowedLegCounts = rule?.allowedLegCounts ?? [];
  const allowedFinalLegCounts = allAllowedLegCounts.filter((count) => count >= legCount);
  const entryTypes = rule?.entryTypes ?? [];
  const selectedType = resolveEntryType(entryTypes, selectedEntryType);
  const blockers: string[] = [];
  const warnings: string[] = [];
  const lockedLeg = legs[0];
  const lockedProviderCategory = lockedLeg?.providerCategory;

  if (!legs.length) {
    blockers.push('Add compatible legs to start an entry.');
  }

  if (lockedProviderCategory === 'Sportsbook') {
    blockers.push('Sportsbook lines are comparison context only and cannot be added to an entry.');
  }

  if (rule && !rule.actionableForUser) {
    blockers.push(`${rule.provider} is not available for entries.`);
  }

  if (!rule && legs.length && lockedProviderCategory !== 'Sportsbook') {
    blockers.push('Provider rules are unavailable for this entry.');
  }

  if (rule?.minLegs !== null && rule?.minLegs !== undefined && legCount > 0 && legCount < rule.minLegs) {
    const needed = rule.minLegs - legCount;
    blockers.push(`Add ${needed} more compatible ${needed === 1 ? 'leg' : 'legs'}.`);
    warnings.push(`${provider} entries require at least ${rule.minLegs} legs.`);
  }

  if (rule?.maxLegs !== null && rule?.maxLegs !== undefined && legCount > rule.maxLegs) {
    blockers.push(`${provider} supports up to ${rule.maxLegs} legs.`);
  }

  if (rule && allAllowedLegCounts.length > 0 && legCount > 0 && !allAllowedLegCounts.includes(legCount)) {
    const nextAllowed = allAllowedLegCounts.find((count) => count > legCount);
    if (nextAllowed) {
      const needed = nextAllowed - legCount;
      blockers.push(`Add ${needed} more compatible ${needed === 1 ? 'leg' : 'legs'} for a valid ${nextAllowed}-leg entry.`);
    } else {
      blockers.push(`${legCount} legs is not an allowed final entry size for ${provider}.`);
    }
  }

  const incompatibleLegs = rule ? legs.filter((leg) => leg.entryCompatibilityGroup !== rule.compatibilityGroup) : [];
  if (incompatibleLegs.length) {
    blockers.push(`${incompatibleLegs.length} selected ${incompatibleLegs.length === 1 ? 'leg is' : 'legs are'} incompatible with ${provider}.`);
  }

  if (rule?.sourceConfidence === 'NeedsVerification') {
    warnings.push('Provider rules need final confirmation in the DFS app.');
  }

  const payout = estimatePayout(legs, amount, rules, selectedType);
  const multiplierRule = rule?.multiplierTable?.find((item) => item.entryType === selectedType && item.legCount === legCount);
  const payoutUnavailableReason =
    legs.length && payout.multiplier === null && selectedType
      ? 'Multiplier data is not configured for this entry type and leg count.'
      : legs.length && payout.multiplier === null
        ? 'Multiplier data is not configured for this provider and leg count.'
        : undefined;
  if (legs.length && payout.multiplier === null) {
    warnings.push(payoutUnavailableReason ?? 'Estimated multiplier unavailable for this provider, entry type, and leg count.');
  }

  if (multiplierRule?.sourceConfidence === 'NeedsVerification') {
    warnings.push('Configured multiplier data still needs final verification in the DFS app.');
  }

  const status = resolveStatus(blockers, legCount, rule);

  return {
    rule,
    provider,
    legCount,
    allowedFinalLegCounts,
    entryTypes,
    selectedEntryType: selectedType,
    status,
    blockers,
    warnings: Array.from(new Set(warnings)),
    multiplier: payout.multiplier,
    estimatedPayout: payout.estimatedPayout,
    payoutUnavailableReason
  };
}

export function resolveEntryType(entryTypes: string[], selectedEntryType?: string) {
  if (selectedEntryType && entryTypes.includes(selectedEntryType)) return selectedEntryType;
  return entryTypes[0] ?? '';
}

function resolveStatus(blockers: string[], legCount: number, rule?: EntryRule): EntryReviewStatus {
  if (blockers.some((blocker) => blocker.includes('incompatible'))) return 'Incompatible legs';
  if (rule?.maxLegs !== null && rule?.maxLegs !== undefined && legCount > rule.maxLegs) return 'Too many legs';
  if (blockers.length) return 'Needs more legs';
  return 'Ready';
}
