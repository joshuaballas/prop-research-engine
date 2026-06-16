import type { EntryLeg, EntryRule } from '../types/entryBuilder';

export function canAddLeg(existingLegs: EntryLeg[], candidate: EntryLeg, rules: EntryRule[]) {
  const rule = rules.find((item) => item.compatibilityGroup === candidate.entryCompatibilityGroup);

  if (!candidate.canAddToEntry || candidate.providerCategory !== 'DFS') {
    return { allowed: false, reason: 'Sportsbook context rows cannot be added to DFS entries.' };
  }

  if (!rule?.actionableForUser) {
    return { allowed: false, reason: 'This provider is not available for entries.' };
  }

  if (existingLegs.length === 0) {
    return { allowed: true, reason: 'Provider locked for this entry.' };
  }

  const lockedGroup = existingLegs[0].entryCompatibilityGroup;
  if (candidate.entryCompatibilityGroup !== lockedGroup) {
    return { allowed: false, reason: `Entries are provider-scoped. Add only ${existingLegs[0].entryProvider} legs.` };
  }

  if (existingLegs.some((leg) => leg.id === candidate.id)) {
    return { allowed: false, reason: 'This leg is already in the entry.' };
  }

  if (existingLegs.length >= rule.maxLegs) {
    return { allowed: false, reason: `${rule.provider} supports up to ${rule.maxLegs} legs in this mock rule set.` };
  }

  return { allowed: true, reason: 'Leg is compatible.' };
}
