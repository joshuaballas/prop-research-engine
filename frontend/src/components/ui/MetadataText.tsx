import { Typography, type TypographyProps } from '@mui/material';
import { palette } from '../../theme/palette';

type MetadataTone = 'default' | 'fresh' | 'stale' | 'danger' | 'primary';

const toneColors: Record<MetadataTone, string> = {
  default: palette.textMuted,
  fresh: palette.success,
  stale: palette.warning,
  danger: palette.danger,
  primary: palette.primary
};

interface MetadataTextProps extends Omit<TypographyProps, 'color'> {
  tone?: MetadataTone;
}

export function MetadataText({ tone = 'default', sx, children, ...props }: MetadataTextProps) {
  return (
    <Typography
      variant="caption"
      {...props}
      sx={{
        color: toneColors[tone],
        fontWeight: 850,
        letterSpacing: 0,
        ...sx
      }}
    >
      {children}
    </Typography>
  );
}
