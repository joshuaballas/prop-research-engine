import { Box, type SxProps, type Theme } from '@mui/material';
import type { ProviderCategory } from '../../types/markets';

type LogoModule = { default: string } | string;

const providerLogoModules = import.meta.globEager('../../assets/providers/*.{svg,png,jpg,jpeg,webp}') as Record<string, LogoModule>;

const sportsbookLogoModules = import.meta.globEager('../../assets/sportsbooks/*.{svg,png,jpg,jpeg,webp}') as Record<string, LogoModule>;

const aliases: Record<string, string> = {
  prizepicks: 'prizepicks',
  prize_picks: 'prizepicks',
  underdog: 'underdog',
  underdogfantasy: 'underdog',
  draftkingspick6: 'draftkings-pick6',
  draftkings_pick6: 'draftkings-pick6',
  pick6: 'draftkings-pick6',
  betr: 'betr-picks',
  betrdfs: 'betr-picks',
  betrpicks: 'betr-picks',
  betr_us_dfs: 'betr-picks',
  sportsbookcontext: 'sportsbook-context',
  draftkings: 'draftkings',
  fanduel: 'fanduel',
  betmgm: 'betmgm',
  espnbet: 'espn-bet',
  caesars: 'caesars',
  betrivers: 'betrivers',
  fanatics: 'fanatics'
};

const monograms: Record<string, string> = {
  prizepicks: 'PP',
  underdog: 'UD',
  'draftkings-pick6': 'P6',
  'betr-picks': 'BP',
  draftkings: 'DK',
  fanduel: 'FD',
  betmgm: 'MGM',
  'espn-bet': 'ESPN',
  caesars: 'CZR',
  betrivers: 'BR',
  fanatics: 'FAN',
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

  return (
    <Box
      aria-label={`${provider} logo`}
      sx={{
        width: size,
        height: size,
        borderRadius: 2,
        display: 'grid',
        placeItems: 'center',
        flexShrink: 0,
        overflow: 'hidden',
        border: category === 'Sportsbook' ? '1px solid rgba(148,163,184,.22)' : '1px solid rgba(34,211,238,.28)',
        bgcolor: category === 'Sportsbook' ? 'rgba(148,163,184,.1)' : 'rgba(34,211,238,.12)',
        color: category === 'Sportsbook' ? 'rgba(226,232,240,.78)' : '#031018',
        background: logo
          ? 'rgba(8,13,26,.42)'
          : category === 'Sportsbook'
            ? 'linear-gradient(135deg, rgba(148,163,184,.16), rgba(15,23,42,.3))'
            : 'linear-gradient(135deg, #22D3EE, #A78BFA)',
        fontSize: fallback.length > 3 ? size * 0.24 : size * 0.31,
        fontWeight: 950,
        letterSpacing: 0,
        ...sx
      }}
    >
      {logo ? (
        <Box
          component="img"
          src={logo}
          alt=""
          sx={{ maxWidth: '78%', maxHeight: '78%', objectFit: 'contain', display: 'block' }}
        />
      ) : (
        fallback
      )}
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
