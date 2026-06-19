import { apiGet } from './client';
import type { EntryRule } from '../types/entryBuilder';

interface EntryRulesResponseDto {
  rules?: EntryRuleDto[];
  Rules?: EntryRuleDto[];
}

interface EntryRuleDto {
  provider?: string;
  Provider?: string;
  providerCategory?: string;
  ProviderCategory?: string;
  actionableForUser?: boolean;
  ActionableForUser?: boolean;
  compatibilityGroup?: string;
  CompatibilityGroup?: string;
  minLegs?: number | null;
  MinLegs?: number | null;
  maxLegs?: number | null;
  MaxLegs?: number | null;
  allowedLegCounts?: number[];
  AllowedLegCounts?: number[];
  entryTypes?: string[];
  EntryTypes?: string[];
  supportsFlex?: boolean | null;
  SupportsFlex?: boolean | null;
  supportsPower?: boolean | null;
  SupportsPower?: boolean | null;
  supportsDemonsGoblinsOrAltLines?: string | null;
  SupportsDemonsGoblinsOrAltLines?: string | null;
  sameGameAllowed?: string | boolean | null;
  SameGameAllowed?: string | boolean | null;
  notes?: string[];
  Notes?: string[];
  sourceConfidence?: string;
  SourceConfidence?: string;
  payoutMultipliers?: EntryMultiplierRuleDto[];
  PayoutMultipliers?: EntryMultiplierRuleDto[];
  multiplierTable?: EntryMultiplierRuleDto[];
  MultiplierTable?: EntryMultiplierRuleDto[];
}

interface EntryMultiplierRuleDto {
  entryType?: string;
  EntryType?: string;
  legCount?: number;
  LegCount?: number;
  multiplier?: number;
  Multiplier?: number;
  appliesToProjectionType?: string;
  AppliesToProjectionType?: string;
  sourceConfidence?: string;
  SourceConfidence?: string;
  lastVerifiedAtUtc?: string | null;
  LastVerifiedAtUtc?: string | null;
  notes?: string[];
  Notes?: string[];
}

export function getEntryRules(signal?: AbortSignal) {
  return apiGet<EntryRulesResponseDto>('/api/dfs/entry-rules', signal).then(normalizeEntryRulesResponse);
}

function normalizeEntryRulesResponse(response: EntryRulesResponseDto): EntryRule[] {
  return (response.rules ?? response.Rules ?? []).map((rule) => ({
    provider: rule.provider ?? rule.Provider ?? 'Unknown provider',
    providerCategory: normalizeProviderCategory(rule.providerCategory ?? rule.ProviderCategory),
    actionableForUser: rule.actionableForUser ?? rule.ActionableForUser ?? false,
    compatibilityGroup: rule.compatibilityGroup ?? rule.CompatibilityGroup ?? '',
    minLegs: rule.minLegs ?? rule.MinLegs ?? null,
    maxLegs: rule.maxLegs ?? rule.MaxLegs ?? null,
    allowedLegCounts: rule.allowedLegCounts ?? rule.AllowedLegCounts ?? [],
    entryTypes: rule.entryTypes ?? rule.EntryTypes ?? [],
    supportsFlex: normalizeBooleanUnknown(rule.supportsFlex ?? rule.SupportsFlex),
    supportsPower: normalizeBooleanUnknown(rule.supportsPower ?? rule.SupportsPower),
    supportsModifiers: normalizeBooleanUnknown(rule.supportsDemonsGoblinsOrAltLines ?? rule.SupportsDemonsGoblinsOrAltLines),
    sameGameAllowed: normalizeBooleanUnknown(rule.sameGameAllowed ?? rule.SameGameAllowed),
    sourceConfidence: normalizeSourceConfidence(rule.sourceConfidence ?? rule.SourceConfidence),
    notes: (rule.notes ?? rule.Notes ?? []).join(' '),
    multiplierTable: normalizeMultiplierRules(rule.payoutMultipliers ?? rule.PayoutMultipliers ?? rule.multiplierTable ?? rule.MultiplierTable ?? [])
  }));
}

function normalizeProviderCategory(value: string | undefined): EntryRule['providerCategory'] {
  return value === 'Sportsbook' ? 'Sportsbook' : 'DFS';
}

function normalizeBooleanUnknown(value: boolean | string | null | undefined): boolean | 'Unknown' {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'string') {
    if (value.toLowerCase() === 'true') return true;
    if (value.toLowerCase() === 'false') return false;
  }
  return 'Unknown';
}

function normalizeSourceConfidence(value: string | undefined): EntryRule['sourceConfidence'] {
  if (value === 'Known' || value === 'Assumed' || value === 'NeedsVerification') return value;
  return 'NeedsVerification';
}

function normalizeMultiplierRules(rules: EntryMultiplierRuleDto[]): EntryRule['multiplierTable'] {
  return rules
    .map((rule) => ({
      entryType: rule.entryType ?? rule.EntryType ?? '',
      legCount: rule.legCount ?? rule.LegCount ?? 0,
      multiplier: rule.multiplier ?? rule.Multiplier ?? 0,
      appliesToProjectionType: rule.appliesToProjectionType ?? rule.AppliesToProjectionType ?? 'StandardOnly',
      sourceConfidence: normalizeSourceConfidence(rule.sourceConfidence ?? rule.SourceConfidence),
      lastVerifiedAtUtc: rule.lastVerifiedAtUtc ?? rule.LastVerifiedAtUtc ?? null,
      notes: rule.notes ?? rule.Notes ?? []
    }))
    .filter((rule) => rule.entryType && rule.legCount > 0 && rule.multiplier > 0);
}
