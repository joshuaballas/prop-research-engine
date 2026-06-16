import { CssBaseline, ThemeProvider } from '@mui/material';
import type { PropsWithChildren } from 'react';
import { BrowserRouter } from 'react-router-dom';
import { ToastProvider } from '../hooks/useToast';
import { theme } from '../theme/theme';

export function AppProviders({ children }: PropsWithChildren) {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <ToastProvider>
        <BrowserRouter>{children}</BrowserRouter>
      </ToastProvider>
    </ThemeProvider>
  );
}
