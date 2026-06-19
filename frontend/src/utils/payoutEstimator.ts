import type { EntryLeg, EntryRule, PayoutEstimate } from '../types/entryBuilder';

export const SPECIAL_PROJECTION_PAYOUT_WARNING = 'Special or alternate projections can change payout. Confirm final payout in PrizePicks.';
export const UNKNOWN_PROJECTION_PAYOUT_WARNING = 'Projection type is unknown. Confirm final payout in the provider app.';

export function estimatePayout(legs: EntryLeg[], entryAmount: number, rules: EntryRule[], entryType?: string): PayoutEstimate {
  const modifiers = legs.map((leg) => leg.projectionModifier);
  if (legs.length === 0) {
    return { multiplier: null, estimatedPayout: null, modifiers };
  }

  const rule = rules.find((item) => item.compatibilityGroup === legs[0].entryCompatibilityGroup);
  const projectionEligibility = getProjectionPayoutEligibility(legs);
  if (projectionEligibility === 'Unknown') {
    return {
      multiplier: null,
      estimatedPayout: null,
      modifiers,
      unavailableReason: UNKNOWN_PROJECTION_PAYOUT_WARNING,
      warning: UNKNOWN_PROJECTION_PAYOUT_WARNING
    };
  }

  const multiplierRule = rule?.multiplierTable?.find(
    (item) =>
      item.legCount === legs.length &&
      (!entryType || item.entryType === entryType) &&
      isMultiplierApplicableToProjectionType(item.appliesToProjectionType, projectionEligibility)
  );
  const multiplier = multiplierRule?.multiplier ?? null;
  if (projectionEligibility === 'Special' && isPrizePicksRule(rule) && !multiplierRule) {
    return {
      multiplier: null,
      estimatedPayout: null,
      modifiers,
      unavailableReason: SPECIAL_PROJECTION_PAYOUT_WARNING,
      warning: SPECIAL_PROJECTION_PAYOUT_WARNING
    };
  }
  const unavailableReason =
    multiplier === null
      ? entryType
        ? 'Multiplier data is not configured for this entry type and leg count.'
        : 'Multiplier data is not configured for this provider and leg count.'
      : undefined;

  return {
    multiplier,
    estimatedPayout: multiplier === null ? null : entryAmount * multiplier,
    modifiers,
    unavailableReason,
    warning:
      multiplier === null || rule?.sourceConfidence === 'NeedsVerification'
        ? unavailableReason ?? 'Estimated payout may vary. Confirm final payout in the provider app.'
        : undefined
  };
}

export function hasSpecialProjectionLeg(legs: EntryLeg[]) {
  return getProjectionPayoutEligibility(legs) === 'Special';
}

export function hasUnknownProjectionLeg(legs: EntryLeg[]) {
  return getProjectionPayoutEligibility(legs) === 'Unknown';
}

type ProjectionPayoutEligibility = 'Standard' | 'Special' | 'Unknown';

function getProjectionPayoutEligibility(legs: EntryLeg[]): ProjectionPayoutEligibility {
  let hasSpecial = false;
  for (const leg of legs) {
    const eligibility = classifyProjectionModifier(leg.projectionModifier);
    if (eligibility === 'Unknown') return 'Unknown';
    if (eligibility === 'Special') hasSpecial = true;
  }

  return hasSpecial ? 'Special' : 'Standard';
}

function classifyProjectionModifier(modifier: string | null | undefined): ProjectionPayoutEligibility {
  const normalized = modifier?.trim().toLowerCase();
  if (!normalized) return 'Unknown';
  if (normalized === 'normal' || normalized === 'standard') return 'Standard';
  if (normalized.includes('unknown')) return 'Unknown';

  const specialIndicators = ['demon', 'goblin', 'taco', 'promo', 'discount', 'boost', 'alt', 'alternate', 'special', 'protected'];
  return specialIndicators.some((indicator) => normalized.includes(indicator)) ? 'Special' : 'Unknown';
}

function isPrizePicksRule(rule: EntryRule | undefined) {
  if (!rule) return false;
  const provider = `${rule.provider} ${rule.compatibilityGroup}`.toLowerCase();
  return provider.includes('prizepicks') || provider.includes('prize picks');
}

function isMultiplierApplicableToProjectionType(appliesToProjectionType: string | undefined, projectionEligibility: ProjectionPayoutEligibility) {
  const appliesTo = appliesToProjectionType ?? 'StandardOnly';
  if (projectionEligibility === 'Standard') return appliesTo === 'StandardOnly' || appliesTo === 'IncludesSpecial' || appliesTo === 'Mixed';
  if (projectionEligibility === 'Special') return appliesTo === 'IncludesSpecial' || appliesTo === 'Mixed';
  return false;
}
