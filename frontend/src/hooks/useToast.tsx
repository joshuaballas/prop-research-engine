import { Alert, Snackbar, useMediaQuery, useTheme } from '@mui/material';
import { createContext, useContext, useMemo, useState, type PropsWithChildren } from 'react';

type ToastSeverity = 'success' | 'error' | 'warning' | 'info';

interface ToastState {
  message: string;
  severity: ToastSeverity;
}

interface ToastContextValue {
  showToast: (message: string, severity?: ToastSeverity) => void;
}

const ToastContext = createContext<ToastContextValue | undefined>(undefined);

export function ToastProvider({ children }: PropsWithChildren) {
  const [toast, setToast] = useState<ToastState | null>(null);
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
  const value = useMemo<ToastContextValue>(
    () => ({
      showToast: (message, severity = 'info') => setToast({ message, severity })
    }),
    []
  );

  return (
    <ToastContext.Provider value={value}>
      {children}
      <Snackbar
        open={Boolean(toast)}
        autoHideDuration={toast?.severity === 'error' || toast?.severity === 'warning' ? 6500 : 2400}
        onClose={() => setToast(null)}
        anchorOrigin={{ vertical: isMobile ? 'top' : 'bottom', horizontal: isMobile ? 'center' : 'left' }}
        sx={{
          ...(isMobile
            ? {
                top: '72px !important',
                left: '16px !important',
                right: '16px !important',
                width: 'auto'
              }
            : undefined)
        }}
      >
        {toast ? (
          <Alert
            severity={toast.severity}
            variant="filled"
            onClose={() => setToast(null)}
            sx={{ borderRadius: 2, fontWeight: 800, width: isMobile ? '100%' : undefined }}
          >
            {toast.message}
          </Alert>
        ) : undefined}
      </Snackbar>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const context = useContext(ToastContext);
  if (!context) throw new Error('useToast must be used within ToastProvider');
  return context;
}
