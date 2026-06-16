import { Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { AppCard } from './AppCard';

export function EmptyState({ title, message, action }: { title: string; message: string; action?: ReactNode }) {
  return (
    <AppCard>
      <Stack spacing={1} alignItems="flex-start">
        <Typography variant="h6">{title}</Typography>
        <Typography color="text.secondary">{message}</Typography>
        {action}
      </Stack>
    </AppCard>
  );
}
