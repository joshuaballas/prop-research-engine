import { Stack, Typography } from '@mui/material';
import { AppCard } from './AppCard';

interface MetricCardProps {
  label: string;
  value: string;
  helper?: string;
}

export function MetricCard({ label, value, helper }: MetricCardProps) {
  return (
    <AppCard>
      <Stack spacing={0.6}>
        <Typography variant="caption" color="text.secondary" fontWeight={800} textTransform="uppercase">
          {label}
        </Typography>
        <Typography variant="h4" fontWeight={950}>
          {value}
        </Typography>
        {helper ? (
          <Typography variant="body2" color="text.secondary">
            {helper}
          </Typography>
        ) : null}
      </Stack>
    </AppCard>
  );
}
