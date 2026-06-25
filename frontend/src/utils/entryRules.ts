import type { EntryLeg, EntryRule } from '../types/entryBuilder';
import { entryBuilderUnavailableReason, isEntryBuilderAddableMarket } from './marketCautions';

export function canAddLeg(existingLegs: EntryLeg[], candidate: EntryLeg, rules: EntryRule[]) {
  if (existingLegs.some((leg) => leg.id === candidate.id)) {
    return { allowed: false, reason: 'This leg is already in your entry.' };
  }

  if (existingLegs.length > 0) {
    const lockedGroup = existingLegs[0].entryCompatibilityGroup;
    if (candidate.entryCompatibilityGroup !== lockedGroup) {
      return { allowed: false, reason: `This entry is locked to ${existingLegs[0].entryProvider}. Add ${existingLegs[0].entryProvider} legs only.` };
    }
  }

  if (!isEntryBuilderAddableMarket(candidate)) {
    return { allowed: false, reason: entryBuilderUnavailableReason(candidate) };
  }

  const compatibilityGroup = existingLegs[0]?.entryCompatibilityGroup ?? candidate.entryCompatibilityGroup;
  const rule = rules.find((item) => item.compatibilityGroup === compatibilityGroup);

  if (!rule) {
    return { allowed: false, reason: 'Entry rules are unavailable for this provider.' };
  }

  if (!rule.actionableForUser) {
    return { allowed: false, reason: 'Entry rules are unavailable for this provider.' };
  }

  if (rule.maxLegs !== null && existingLegs.length >= rule.maxLegs) {
    return { allowed: false, reason: `${rule.provider} entries allow up to ${rule.maxLegs} legs.` };
  }

  return { allowed: true, reason: existingLegs.length ? 'Leg is compatible.' : 'Provider locked for this entry.' };
}
