import { useMediaQuery, useTheme } from '@mui/material';

export function useResponsiveView() {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('md'));
  const isCompact = useMediaQuery(theme.breakpoints.down('lg'));

  return { isMobile, isCompact };
}
