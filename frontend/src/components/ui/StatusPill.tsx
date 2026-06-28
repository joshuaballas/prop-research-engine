import { Chip, type ChipProps } from '@mui/material';
import { palette } from '../../theme/palette';

const colors: Record<string, { bg: string; color: string; border: string }> = {
  Candidate: { bg: 'rgba(250,204,21,.15)', color: '#FACC15', border: 'rgba(250,204,21,.42)' },
  'Top Pick': { bg: 'rgba(250,204,21,.15)', color: '#FACC15', border: 'rgba(250,204,21,.42)' },
  Watchlist: { bg: 'rgba(245,158,11,.13)', color: palette.warning, border: 'rgba(245,158,11,.35)' },
  Research: { bg: 'rgba(148,163,184,.12)', color: '#CBD5E1', border: 'rgba(148,163,184,.28)' },
  'Market context': { bg: 'rgba(148,163,184,.12)', color: palette.textMuted, border: 'rgba(148,163,184,.24)' },
  Avoid: { bg: 'rgba(244,63,94,.13)', color: palette.danger, border: 'rgba(244,63,94,.35)' },
  Fresh: { bg: 'rgba(34,211,238,.13)', color: palette.primary, border: 'rgba(34,211,238,.35)' },
  DFS: { bg: 'rgba(129,140,248,.14)', color: '#C4B5FD', border: 'rgba(129,140,248,.38)' },
  Alternate: { bg: 'rgba(167,139,250,.13)', color: '#C4B5FD', border: 'rgba(167,139,250,.34)' },
  Demon: { bg: 'rgba(248,113,113,.14)', color: '#F87171', border: 'rgba(248,113,113,.44)' },
  Goblin: { bg: 'rgba(52,211,153,.13)', color: '#34D399', border: 'rgba(52,211,153,.38)' },
  Taco: { bg: 'rgba(251,191,36,.13)', color: '#FBBF24', border: 'rgba(251,191,36,.36)' },
  Promo: { bg: 'rgba(34,211,238,.13)', color: '#22D3EE', border: 'rgba(34,211,238,.38)' },
  Special: { bg: 'rgba(167,139,250,.13)', color: '#A78BFA', border: 'rgba(167,139,250,.38)' },
  Unknown: { bg: 'rgba(148,163,184,.12)', color: palette.textMuted, border: 'rgba(148,163,184,.28)' },
  'Discounted Line': { bg: 'rgba(251,191,36,.13)', color: '#FBBF24', border: 'rgba(251,191,36,.36)' },
  'Payout Boost': { bg: 'rgba(34,211,238,.13)', color: '#22D3EE', border: 'rgba(34,211,238,.38)' },
  'Bonus Lineup': { bg: 'rgba(167,139,250,.13)', color: '#A78BFA', border: 'rgba(167,139,250,.38)' },
  Strong: { bg: 'rgba(34,197,94,.13)', color: palette.success, border: 'rgba(34,197,94,.38)' },
  Medium: { bg: 'rgba(250,204,21,.13)', color: '#FACC15', border: 'rgba(250,204,21,.35)' },
  Weak: { bg: 'rgba(248,113,113,.13)', color: '#F87171', border: 'rgba(248,113,113,.38)' },
  'Low sample': { bg: 'rgba(245,158,11,.13)', color: palette.warning, border: 'rgba(245,158,11,.35)' },
  Stale: { bg: 'rgba(245,158,11,.13)', color: palette.warning, border: 'rgba(245,158,11,.35)' },
  Started: { bg: 'rgba(251,146,60,.13)', color: '#FB923C', border: 'rgba(251,146,60,.36)' },
  Expired: { bg: 'rgba(251,146,60,.13)', color: '#FB923C', border: 'rgba(251,146,60,.36)' },
  Locked: { bg: 'rgba(148,163,184,.12)', color: '#CBD5E1', border: 'rgba(148,163,184,.28)' },
  'Provider issue': { bg: 'rgba(245,158,11,.13)', color: palette.warning, border: 'rgba(245,158,11,.35)' },
  'No longer available': { bg: 'rgba(244,63,94,.13)', color: palette.danger, border: 'rgba(244,63,94,.35)' },
  'Pricing caution': { bg: 'rgba(245,158,11,.13)', color: palette.warning, border: 'rgba(245,158,11,.35)' },
  'Model caution': { bg: 'rgba(245,158,11,.13)', color: palette.warning, border: 'rgba(245,158,11,.35)' }
};

export function StatusPill({ label, sx, ...props }: ChipProps) {
  const text = String(label ?? '');
  const color = colors[text] ?? { bg: 'rgba(148,163,184,.12)', color: palette.textMuted, border: palette.border };
  return (
    <Chip
      label={label}
      size="small"
      {...props}
      sx={{
        bgcolor: color.bg,
        color: color.color,
        border: `1px solid ${color.border}`,
        ...sx
      }}
    />
  );
}
