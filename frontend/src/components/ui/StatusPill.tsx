import { Chip, type ChipProps } from '@mui/material';
import { palette } from '../../theme/palette';

const colors: Record<string, { bg: string; color: string; border: string }> = {
  Candidate: { bg: 'rgba(34,197,94,.13)', color: palette.success, border: 'rgba(34,197,94,.35)' },
  Watchlist: { bg: 'rgba(245,158,11,.13)', color: palette.warning, border: 'rgba(245,158,11,.35)' },
  Avoid: { bg: 'rgba(244,63,94,.13)', color: palette.danger, border: 'rgba(244,63,94,.35)' },
  Fresh: { bg: 'rgba(34,211,238,.13)', color: palette.primary, border: 'rgba(34,211,238,.35)' },
  DFS: { bg: 'rgba(129,140,248,.14)', color: '#C4B5FD', border: 'rgba(129,140,248,.38)' }
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
