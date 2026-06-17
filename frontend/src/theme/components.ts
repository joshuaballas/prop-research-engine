import type { Components, Theme } from '@mui/material/styles';
import { palette } from './palette';

export const components: Components<Omit<Theme, 'components'>> = {
  MuiButtonBase: {
    defaultProps: {
      disableRipple: true
    }
  },
  MuiButton: {
    styleOverrides: {
      root: {
        borderRadius: 10,
        textTransform: 'none',
        fontWeight: 800,
        boxShadow: 'none',
        paddingInline: 18
      },
      contained: {
        background: `linear-gradient(135deg, ${palette.primary}, ${palette.secondary})`,
        color: '#031019',
        '&:hover': {
          boxShadow: `0 0 0 1px ${palette.borderStrong}, 0 18px 45px rgba(34, 211, 238, 0.18)`
        }
      },
      outlined: {
        borderColor: palette.border,
        color: palette.text,
        backgroundColor: 'rgba(15, 23, 42, 0.45)',
        '&:hover': {
          borderColor: palette.borderStrong,
          backgroundColor: 'rgba(34, 211, 238, 0.08)'
        }
      }
    }
  },
  MuiCard: {
    styleOverrides: {
      root: {
        borderRadius: 20,
        backgroundImage: 'none',
        border: `1px solid ${palette.border}`,
        backgroundColor: palette.surface,
        boxShadow: '0 16px 40px rgba(0,0,0,.24)'
      }
    }
  },
  MuiChip: {
    styleOverrides: {
      root: {
        borderRadius: 8,
        fontWeight: 800,
        letterSpacing: 0,
        height: 26
      }
    }
  },
  MuiTableCell: {
    styleOverrides: {
      root: {
        borderBottom: `1px solid ${palette.border}`,
        color: palette.text
      },
      head: {
        color: palette.textMuted,
        fontSize: 12,
        fontWeight: 800,
        textTransform: 'uppercase',
        backgroundColor: '#0B1220'
      }
    }
  },
  MuiOutlinedInput: {
    styleOverrides: {
      root: {
        borderRadius: 10,
        backgroundColor: 'rgba(15, 23, 42, 0.72)',
        '& fieldset': { borderColor: palette.border },
        '&:hover fieldset': { borderColor: palette.borderStrong },
        '&.Mui-focused fieldset': { borderColor: palette.primary }
      }
    }
  }
};
