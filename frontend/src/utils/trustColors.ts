export type TrustColorLevel = 'Low' | 'Medium' | 'High' | 'Weak' | 'Strong' | 'Research';

export type NormalizedTrustLevel = 'Low' | 'Medium' | 'High' | 'Unknown';

export function normalizeTrustLevel(level?: string | null): NormalizedTrustLevel {
  const normalized = level?.trim().toLowerCase();
  if (!normalized) return 'Unknown';
  if (normalized === 'high' || normalized === 'strong') return 'High';
  if (normalized === 'medium') return 'Medium';
  if (normalized === 'low' || normalized === 'weak' || normalized === 'research' || normalized === 'researchonly') return 'Low';
  return 'Unknown';
}

export function trustColor(level?: TrustColorLevel | NormalizedTrustLevel | string | null) {
  const normalized = normalizeTrustLevel(level);
  if (normalized === 'High') return 'success.main';
  if (normalized === 'Medium') return 'warning.main';
  if (normalized === 'Unknown') return 'text.secondary';
  return 'error.main';
}
