import type { EntryLeg, EntryRule, PayoutEstimate } from '../types/entryBuilder';

export function estimatePayout(legs: EntryLeg[], entryAmount: number, rules: EntryRule[], entryType?: string): PayoutEstimate {
  const modifiers = legs.map((leg) => leg.projectionModifier);
  if (legs.length === 0) {
    return { multiplier: null, estimatedPayout: null, modifiers };
  }

  const rule = rules.find((item) => item.compatibilityGroup === legs[0].entryCompatibilityGroup);
  const multiplier = rule?.multiplierTable?.find(
    (item) => item.legCount === legs.length && (!entryType || item.entryType === entryType)
  )?.multiplier ?? null;

  return {
    multiplier,
    estimatedPayout: multiplier === null ? null : entryAmount * multiplier,
    modifiers,
    warning:
      multiplier === null || rule?.sourceConfidence === 'NeedsVerification'
        ? 'Estimated payout may vary. Confirm final payout in the provider app.'
        : undefined
  };
}
