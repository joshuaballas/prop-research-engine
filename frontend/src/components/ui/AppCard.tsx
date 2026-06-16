import { Card, CardContent, type CardProps } from '@mui/material';
import { palette } from '../../theme/palette';

export function AppCard({ children, sx, ...props }: CardProps) {
  return (
    <Card
      {...props}
      sx={{
        borderRadius: 3,
        border: `1px solid ${palette.border}`,
        background: `linear-gradient(180deg, rgba(17, 28, 49, 0.96), rgba(10, 16, 32, 0.96))`,
        ...sx
      }}
    >
      <CardContent sx={{ p: 2.25, '&:last-child': { pb: 2.25 } }}>{children}</CardContent>
    </Card>
  );
}
