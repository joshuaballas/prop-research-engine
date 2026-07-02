export function formatPercent(value: number | null | undefined, options?: { signed?: boolean; digits?: number }) {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return '—';
  }

  const digits = options?.digits ?? 2;
  const prefix = options?.signed && value > 0 ? '+' : '';
  return `${prefix}${(value * 100).toFixed(digits)}%`;
}

export function formatCurrency(cents: number) {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0
  }).format(cents / 100);
}

export function formatAmericanOdds(value: number | null | undefined, fallback = 'Odds unavailable') {
  if (value === null || value === undefined || !Number.isFinite(value) || Math.abs(value) < 0.0001) {
    return fallback;
  }

  const roundedValue = Math.round(value);
  return roundedValue > 0 ? `+${roundedValue}` : `${roundedValue}`;
}

export function formatRiskToWin(americanOdds: number) {
  if (americanOdds < 0) {
    return `Risk $${Math.abs(americanOdds)} to win $100`;
  }

  return `Risk $100 to win $${americanOdds}`;
}

export function formatDecimal(value: number, digits = 3) {
  return value.toFixed(digits).replace(/0+$/, '').replace(/\.$/, '');
}
