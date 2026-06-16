export function formatPercent(value: number, options?: { signed?: boolean; digits?: number }) {
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

export function formatAmericanOdds(value: number) {
  return value > 0 ? `+${value}` : `${value}`;
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
