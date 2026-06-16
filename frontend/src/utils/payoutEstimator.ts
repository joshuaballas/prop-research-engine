import type { EntryLeg, EntryRule, PayoutEstimate } from '../types/entryBuilder';

const baseMultipliers: Record<number, number> = {
  2: 3,
  3: 5,
  4: 10,
  5: 20,
  6: 35
};

export function estimatePayout(legs: EntryLeg[], entryAmount: number, rules: EntryRule[]): PayoutEstimate {
  const modifiers = legs.map((leg) => leg.projectionModifier);
  if (legs.length === 0) {
    return { multiplier: 0, estimatedPayout: 0, modifiers };
  }

  const rule = rules.find((item) => item.compatibilityGroup === legs[0].entryCompatibilityGroup);
  const base = baseMultipliers[legs.length] ?? legs.length * 2;
  const modifierAdjustment = modifiers.reduce((total, modifier) => {
    if (modifier === 'Demon') return total + 0.35;
    if (modifier === 'Goblin' || modifier === 'Discount') return total - 0.15;
    if (modifier === 'Payout Boost') return total + 0.2;
    return total;
  }, 0);
  const multiplier = Math.max(1, base + modifierAdjustment);

  return {
    multiplier,
    estimatedPayout: entryAmount * multiplier,
    modifiers,
    warning:
      rule?.sourceConfidence === 'NeedsVerification'
        ? 'Estimated payout may vary. Confirm final payout in the provider app.'
        : undefined
  };
}
