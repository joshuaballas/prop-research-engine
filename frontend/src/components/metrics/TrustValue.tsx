import { Box, Typography, type SxProps, type Theme } from '@mui/material';
import { normalizeTrustLevel, trustColor } from '../../utils/trustColors';

interface TrustValueProps {
  level?: string | null;
  variant?: 'text' | 'badge';
  size?: 'small' | 'medium';
  showSuffix?: boolean;
  sx?: SxProps<Theme>;
}

export function TrustValue({ level, variant = 'text', size = 'medium', showSuffix = false, sx }: TrustValueProps) {
  const label = normalizeTrustLevel(level);
  const display = label === 'Unknown' ? 'Unknown' : label;
  const color = trustColor(label);
  const text = showSuffix ? `${display} trust` : display;

  if (variant === 'badge') {
    return (
      <Box
        component="span"
        sx={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: size === 'small' ? 22 : 26,
          px: size === 'small' ? 0.85 : 1,
          borderRadius: 1.5,
          border: '1px solid rgba(148,163,184,.2)',
          color,
          bgcolor: 'rgba(15,23,42,.46)',
          fontSize: size === 'small' ? 11 : 12,
          fontWeight: 850,
          lineHeight: 1,
          whiteSpace: 'nowrap',
          ...sx
        }}
      >
        {text}
      </Box>
    );
  }

  return (
    <Typography
      component="span"
      variant={size === 'small' ? 'caption' : 'body2'}
      fontWeight={850}
      sx={{ color, whiteSpace: 'nowrap', ...sx }}
    >
      {text}
    </Typography>
  );
}
