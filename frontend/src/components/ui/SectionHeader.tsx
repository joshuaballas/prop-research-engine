import { Box, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';

interface SectionHeaderProps {
  title: string;
  eyebrow?: string;
  description?: string;
  action?: ReactNode;
}

export function SectionHeader({ title, eyebrow, description, action }: SectionHeaderProps) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} alignItems={{ sm: 'end' }}>
      <Box>
        {eyebrow ? (
          <Typography variant="caption" color="primary" fontWeight={900} textTransform="uppercase">
            {eyebrow}
          </Typography>
        ) : null}
        <Typography variant="h5">{title}</Typography>
        {description ? (
          <Typography color="text.secondary" variant="body2">
            {description}
          </Typography>
        ) : null}
      </Box>
      {action}
    </Stack>
  );
}
