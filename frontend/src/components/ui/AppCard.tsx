import { Card, CardContent, type CardProps, type SxProps, type Theme } from '@mui/material';
import { palette } from '../../theme/palette';

interface AppCardProps extends CardProps {
  contentSx?: SxProps<Theme>;
}

export function AppCard({ children, sx, contentSx, ...props }: AppCardProps) {
  return (
    <Card
      elevation={0}
      {...props}
      sx={[
        {
          borderRadius: 4,
          border: `1px solid ${palette.border}`,
          backgroundColor: palette.cardSurface
        },
        ...(Array.isArray(sx) ? sx : sx ? [sx] : [])
      ]}
    >
      <CardContent
        sx={[
          { p: 2.25, '&:last-child': { pb: 2.25 } },
          ...(Array.isArray(contentSx) ? contentSx : contentSx ? [contentSx] : [])
        ]}
      >
        {children}
      </CardContent>
    </Card>
  );
}
