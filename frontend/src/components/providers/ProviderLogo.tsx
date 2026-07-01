import { Box, type SxProps, type Theme } from '@mui/material';
import type { ProviderCategory } from '../../types/markets';
import { radius } from '../../theme/radius';

type LogoModule = { default: string } | string;

const providerLogoModules = import.meta.globEager('../../assets/providers/*.{svg,png,jpg,jpeg,webp}') as Record<string, LogoModule>;

const sportsbookLogoModules = import.meta.globEager('../../assets/sportsbooks/*.{svg,png,jpg,jpeg,webp}') as Record<string, LogoModule>;

const aliases: Record<string, string> = {
  prizepicks: 'prizepicks',
  prize_picks: 'prizepicks',
  underdog: 'underdogfantasy',
  underdogfantasy: 'underdogfantasy',
  draftkingspick6: 'dkpick6webp',
  draftkings_pick6: 'dkpick6webp',
  pick6: 'dkpick6webp',
  betr: 'betr',
  betrdfs: 'betr',
  betrpicks: 'betr',
  betr_us_dfs: 'betr',
  sportsbookcontext: 'sportsbook-context',
  draftkings: 'draftkings',
  fanduel: 'fanduel',
  betmgm: 'betmgm',
  espnbet: 'espnbet',
  caesars: 'caesars',
  williamhillus: 'caesars',
  williamhill_us: 'caesars',
  betrivers: 'betrivers',
  fanatics: 'fanatics',
  betonline: 'betonline',
  betonlineag: 'betonline',
  bovada: 'bovada',
  lowvig: 'lowvig',
  mybookie: 'mybookie',
  mybookieag: 'mybookie',
  ballybet: 'ballybet',
  betanything: 'betanything',
  betparx: 'betparx',
  betus: 'betus',
  getfliff: 'getfliff',
  hardrock: 'hardrock',
  hardrockbet: 'hardrock',
  rebet: 'rebet',
  thescore: 'thescore',
  thescorebet: 'thescore',
  the_score: 'thescore',
  the_score_bet: 'thescore',
  scorebet: 'thescore'
};

const monograms: Record<string, string> = {
  prizepicks: 'PP',
  underdogfantasy: 'UD',
  dkpick6webp: 'P6',
  betr: 'BP',
  draftkings: 'DK',
  fanduel: 'FD',
  betmgm: 'MGM',
  espnbet: 'ESPN',
  caesars: 'CZR',
  betrivers: 'BR',
  fanatics: 'FAN',
  betonline: 'BOL',
  bovada: 'BOV',
  lowvig: 'LV',
  mybookie: 'MYB',
  thescore: 'TS',
  'sportsbook-context': 'SB'
};

interface ProviderLogoProps {
  provider: string;
  category?: ProviderCategory;
  mark?: string;
  size?: number;
  sx?: SxProps<Theme>;
}

export function providerLogoKey(provider: string) {
  const normalized = provider.toLowerCase().replace(/[^a-z0-9_]+/g, '');
  return aliases[normalized] ?? normalized;
}

export function ProviderLogo({ provider, category = 'DFS', mark, size = 38, sx }: ProviderLogoProps) {
  const key = providerLogoKey(provider);
  const logo = resolveLogo(key, category);
  const fallback = mark ?? monograms[key] ?? initials(provider);

  if (logo) {
    return (
      <Box
        component="img"
        src={logo}
        alt=""
        aria-label={`${provider} logo`}
        sx={{
          width: size,
          height: size,
          borderRadius: radius.card,
          objectFit: 'cover',
          display: 'block',
          flexShrink: 0,
          ...sx
        }}
      />
    );
  }

  return (
    <Box
      aria-label={`${provider} logo`}
      sx={{
        width: size,
        height: size,
        borderRadius: radius.card,
        display: 'grid',
        placeItems: 'center',
        flexShrink: 0,
        overflow: 'hidden',
        border: category === 'Sportsbook' ? '1px solid rgba(148,163,184,.22)' : '1px solid rgba(34,211,238,.28)',
        bgcolor: category === 'Sportsbook' ? 'rgba(148,163,184,.1)' : 'rgba(34,211,238,.16)',
        color: category === 'Sportsbook' ? 'rgba(226,232,240,.78)' : '#F8FAFC',
        fontSize: fallback.length > 3 ? size * 0.24 : size * 0.31,
        fontWeight: 950,
        letterSpacing: 0,
        ...sx
      }}
    >
      {fallback}
    </Box>
  );
}

function resolveLogo(key: string, category: ProviderCategory) {
  const modules = category === 'Sportsbook' ? sportsbookLogoModules : providerLogoModules;
  const match = Object.entries(modules).find(([path]) => path.includes(`/${key}.`));
  if (!match) {
    return undefined;
  }

  const module = match[1];
  return typeof module === 'string' ? module : module.default;
}

function initials(provider: string) {
  const words = provider.split(/\s+/).filter(Boolean);
  const text = words.length > 1 ? words.map((word) => word[0]).join('') : provider.slice(0, 3);
  return text.toUpperCase();
}
