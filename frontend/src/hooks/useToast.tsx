import { Alert, Snackbar, useMediaQuery, useTheme } from '@mui/material';
import { createContext, useContext, useMemo, useState, type PropsWithChildren } from 'react';
import { radius } from '../theme/radius';

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
        anchorOrigin={{ vertical: isMobile ? 'top' : 'bottom', horizontal: 'center' }}
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
            sx={{
              ...toastSeveritySx[toast.severity],
              borderRadius: radius.featuredCard,
              border: `1px solid ${toastSeveritySx[toast.severity].borderColor}`,
              boxShadow: 'none',
              alignItems: 'center',
              backgroundImage: 'none',
              display: 'flex',
              fontWeight: 800,
              minHeight: 0,
              outline: 'none',
              py: 0.55,
              px: 1.2,
              width: isMobile ? '100%' : undefined,
              '&.MuiAlert-root, &.MuiAlert-filled, &.MuiAlert-filledSuccess, &.MuiAlert-filledInfo, &.MuiAlert-filledWarning, &.MuiAlert-filledError': {
                border: `1px solid ${toastSeveritySx[toast.severity].borderColor}`,
                boxShadow: 'none',
                outline: 'none'
              },
              '&:focus, &:focus-visible': {
                outline: 'none'
              },
              '& .MuiAlert-icon': {
                alignItems: 'center',
                color: 'rgba(255,255,255,.92)',
                display: 'flex',
                mr: 1,
                py: 0
              },
              '& .MuiAlert-message': {
                alignItems: 'center',
                display: 'flex',
                flex: 1,
                lineHeight: 1.35,
                py: 0
              },
              '& .MuiAlert-action': {
                alignItems: 'center',
                alignSelf: 'center',
                display: 'flex',
                ml: 1,
                pl: 0,
                py: 0
              },
              '& .MuiAlert-action .MuiIconButton-root': {
                alignItems: 'center',
                color: 'rgba(255,255,255,.72)',
                display: 'inline-flex',
                height: 28,
                justifyContent: 'center',
                p: 0.4,
                width: 28,
                boxShadow: 'none'
              },
              '& .MuiAlert-action .MuiIconButton-root:hover': {
                color: '#fff',
                backgroundColor: 'rgba(255,255,255,.12)'
              }
            }}
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

const toastSeveritySx: Record<ToastSeverity, { bgcolor: string; borderColor: string; color: string }> = {
  success: {
    bgcolor: '#14532D',
    borderColor: 'rgba(34, 197, 94, 0.42)',
    color: '#F0FDF4'
  },
  info: {
    bgcolor: '#0E3A5A',
    borderColor: 'rgba(56, 189, 248, 0.38)',
    color: '#F0F9FF'
  },
  warning: {
    bgcolor: '#92400E',
    borderColor: 'rgba(251, 191, 36, 0.42)',
    color: '#FFFBEB'
  },
  error: {
    bgcolor: '#7F1D1D',
    borderColor: 'rgba(248, 113, 113, 0.44)',
    color: '#FEF2F2'
  }
};
