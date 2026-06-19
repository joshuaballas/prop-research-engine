import { Typography, type SxProps, type Theme } from '@mui/material';
import type { ReactNode } from 'react';

type AppTableTextProps = {
  children: ReactNode;
  noWrap?: boolean;
  sx?: SxProps<Theme>;
};

export function AppTablePrimaryText({ children, noWrap = true, sx }: AppTableTextProps) {
  return (
    <Typography fontWeight={950} noWrap={noWrap} sx={sx}>
      {children}
    </Typography>
  );
}

export function AppTableSecondaryText({ children, noWrap = true, sx }: AppTableTextProps) {
  return (
    <Typography color="text.secondary" fontSize={12} noWrap={noWrap} sx={sx}>
      {children}
    </Typography>
  );
}

export function AppTableMetricValue({ children, noWrap = true, sx }: AppTableTextProps) {
  return (
    <Typography fontWeight={900} noWrap={noWrap} sx={sx}>
      {children}
    </Typography>
  );
}

export function AppTableEvValue({ children, positive, noWrap = true, sx }: AppTableTextProps & { positive?: boolean }) {
  return (
    <Typography color={positive ? 'success.main' : 'error.main'} fontWeight={950} noWrap={noWrap} sx={sx}>
      {children}
    </Typography>
  );
}
