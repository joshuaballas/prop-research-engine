export type WarningInput = string | null | undefined | Array<string | null | undefined>;

const emptyWarningValues = new Set(['', 'none', 'null', 'undefined']);

const warningReplacements: Record<string, string> = {
  LowSample: 'Low sample',
  'Low sample size': 'Low sample',
  ProviderUnavailable: 'Provider unavailable',
  NearLock: 'Near lock',
  StopAfterEventStart: 'Slate locked',
  NoPersistedSlateEvents: 'No active slate',
  NoEvents: 'No active slate',
  NoActiveSlate: 'No active slate',
  'Sportsbook context rows cannot be added': 'Reference lines are not entry legs',
  'NBA sample is thin for this market': 'Limited history for this market',
  'Provider limited for this sport': 'Limited coverage'
};

export function normalizeWarnings(input: WarningInput) {
  const values = Array.isArray(input) ? input : [input];

  return values
    .map((value) => formatWarningLabel(value))
    .filter((value): value is string => Boolean(value));
}

export function hasRenderableWarning(input: WarningInput) {
  return normalizeWarnings(input).length > 0;
}

export function renderWarningText(input: WarningInput, emptyValue = '') {
  const warnings = normalizeWarnings(input);
  return warnings.length ? warnings.join(', ') : emptyValue;
}

export function formatWarningLabel(value: string | null | undefined) {
  const trimmed = String(value ?? '').trim();
  if (emptyWarningValues.has(trimmed.toLowerCase())) return null;

  return warningReplacements[trimmed] ?? humanizeWarning(trimmed);
}

function humanizeWarning(value: string) {
  return value
    .replace(/[_-]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}
