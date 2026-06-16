import { Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';

interface PageHeaderProps {
  title: string;
  description: string;
  action?: ReactNode;
}

export function PageHeader({ title, description, action }: PageHeaderProps) {
  return (
    <Stack direction={{ xs: 'column', md: 'row' }} alignItems={{ md: 'end' }} justifyContent="space-between" gap={2}>
      <Stack spacing={0.8} maxWidth={760}>
        <Typography variant="h3">{title}</Typography>
        <Typography color="text.secondary">{description}</Typography>
      </Stack>
      {action}
    </Stack>
  );
}
