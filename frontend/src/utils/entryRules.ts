import type { EntryLeg, EntryRule } from '../types/entryBuilder';

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

  if (!candidate.canAddToEntry || candidate.providerCategory !== 'DFS') {
    return { allowed: false, reason: getActionabilityMessage(candidate) };
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

function getActionabilityMessage(candidate: EntryLeg) {
  if (candidate.providerCategory === 'Sportsbook') {
    return 'Sportsbook lines are comparison context only and cannot be added to an entry.';
  }

  if (candidate.freshness === 'Stale') {
    return 'This line is stale and cannot be added to an entry.';
  }

  if (candidate.freshness === 'Unavailable') {
    return 'This line is unavailable and cannot be added to an entry.';
  }

  return 'This line cannot be added to an entry.';
}
