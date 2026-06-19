import { Stack, type StackProps } from '@mui/material';
import type { ReactNode } from 'react';
import { compactActionButtonSx, secondaryGhostButtonSx } from './buttonStyles';

export const desktopRowActionButtonSx = {
  ...compactActionButtonSx,
  width: 98,
  minWidth: 98,
  height: 32,
  minHeight: 32,
  px: 1,
  fontSize: 12,
  whiteSpace: 'nowrap'
} as const;

export const desktopRowDetailsButtonSx = {
  ...secondaryGhostButtonSx,
  width: 98,
  minWidth: 98,
  height: 32,
  minHeight: 32,
  px: 1,
  fontSize: 12,
  fontWeight: 850,
  whiteSpace: 'nowrap'
} as const;

export const mobileCardActionButtonSx = {
  ...compactActionButtonSx,
  width: '100%',
  minWidth: 0,
  height: 32,
  minHeight: 32,
  fontSize: 12,
  whiteSpace: 'nowrap'
} as const;

export const mobileCardDetailsButtonSx = {
  ...secondaryGhostButtonSx,
  width: '100%',
  minWidth: 0,
  height: 32,
  minHeight: 32,
  fontSize: 12,
  fontWeight: 850,
  whiteSpace: 'nowrap'
} as const;

export function AppRowActions({ children, sx, ...props }: { children: ReactNode } & StackProps) {
  return (
    <Stack spacing={0.65} sx={[{ width: 98, alignItems: 'stretch' }, ...(Array.isArray(sx) ? sx : sx ? [sx] : [])]} {...props}>
      {children}
    </Stack>
  );
}

export function AppMobileCardActions({ children, sx, ...props }: { children: ReactNode } & StackProps) {
  return (
    <Stack
      spacing={0.8}
      sx={[
        { mt: 'auto', '& > .MuiButton-root': { width: '100%', minWidth: 0 } },
        ...(Array.isArray(sx) ? sx : sx ? [sx] : [])
      ]}
      {...props}
    >
      {children}
    </Stack>
  );
}
