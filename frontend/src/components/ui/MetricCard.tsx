import { Stack, Typography } from '@mui/material';
import { AppCard } from './AppCard';

interface MetricCardProps {
  label: string;
  value: string;
  helper?: string;
  valueColor?: string;
}

export function MetricCard({ label, value, helper, valueColor }: MetricCardProps) {
  return (
    <AppCard
      sx={{
        minHeight: 112,
        height: '100%',
        display: 'flex',
        flexDirection: 'column'
      }}
      contentSx={{
        minHeight: 112,
        boxSizing: 'border-box',
        flex: 1,
        display: 'flex',
        flexDirection: 'column'
      }}
    >
      <Stack
        sx={{
          width: '100%',
          minWidth: 0,
          minHeight: 0,
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'space-between'
        }}
      >
        <Typography variant="caption" color="text.secondary" fontWeight={800} textTransform="uppercase">
          {label}
        </Typography>
        <Typography
          fontWeight={950}
          color={valueColor}
          sx={{
            fontSize: { xs: 'clamp(1.45rem, 8vw, 2rem)', sm: 'clamp(1.65rem, 4vw, 2.125rem)' },
            lineHeight: 1.08,
            letterSpacing: 0,
            whiteSpace: 'nowrap',
            overflow: 'visible',
            maxWidth: '100%',
            pr: 0.5
          }}
        >
          {value}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.35 }}>
          {helper ?? ''}
        </Typography>
      </Stack>
    </AppCard>
  );
}
