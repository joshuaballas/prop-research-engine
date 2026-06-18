import { Card, CardContent, type CardProps, type SxProps, type Theme } from '@mui/material';
import { palette } from '../../theme/palette';

interface AppCardProps extends CardProps {
  contentSx?: SxProps<Theme>;
}

export function AppCard({ children, sx, contentSx, ...props }: AppCardProps) {
  return (
    <Card
      {...props}
      sx={{
        borderRadius: 4,
        border: `1px solid ${palette.border}`,
        backgroundColor: palette.surface,
        ...sx
      }}
    >
      <CardContent sx={{ p: 2.25, '&:last-child': { pb: 2.25 }, ...contentSx }}>{children}</CardContent>
    </Card>
  );
}
