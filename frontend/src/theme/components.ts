import type { Components, Theme } from '@mui/material/styles';
import { palette } from './palette';

export const components: Components<Omit<Theme, 'components'>> = {
  MuiCssBaseline: {
    styleOverrides: {
      '*': {
        scrollbarWidth: 'thin',
        scrollbarColor: `${palette.textSubtle} ${palette.bg}`
      },
      '*::-webkit-scrollbar': {
        width: 10,
        height: 10
      },
      '*::-webkit-scrollbar-track': {
        backgroundColor: palette.bg
      },
      '*::-webkit-scrollbar-thumb': {
        backgroundColor: palette.textSubtle,
        borderRadius: 999,
        border: `2px solid ${palette.bg}`
      },
      '*::-webkit-scrollbar-thumb:hover': {
        backgroundColor: palette.textMuted
      },
      '*::-webkit-scrollbar-corner': {
        backgroundColor: palette.bg
      },
      '.app-scrollbar': {
        scrollbarWidth: 'thin',
        scrollbarColor: `${palette.textSubtle} ${palette.bg}`
      }
    }
  },
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
        paddingInline: 18,
        minHeight: 38,
        transition:
          'background-color .16s ease, border-color .16s ease, box-shadow .16s ease, color .16s ease, transform .08s ease',
        '& .MuiButton-startIcon, & .MuiButton-endIcon': {
          color: 'inherit'
        },
        '&:focus-visible': {
          outline: 'none',
          boxShadow: `0 0 0 3px rgba(34, 211, 238, 0.22), 0 0 0 1px ${palette.primary}`
        },
        '&:active:not(.Mui-disabled)': {
          transform: 'translateY(1px)'
        }
      },
      contained: {
        backgroundColor: 'rgba(11, 74, 94, 0.92)',
        color: palette.text,
        border: '1px solid rgba(125, 211, 252, 0.5)',
        textShadow: 'none',
        boxShadow: 'inset 0 1px 0 rgba(255,255,255,.12), 0 8px 20px rgba(8, 145, 178, 0.12)',
        '&:hover': {
          backgroundColor: 'rgba(13, 91, 116, 0.96)',
          borderColor: 'rgba(165, 243, 252, 0.64)',
          boxShadow: 'inset 0 1px 0 rgba(255,255,255,.14)'
        },
        '&:active:not(.Mui-disabled)': {
          boxShadow: 'inset 0 2px 8px rgba(3,7,18,.34), 0 5px 16px rgba(8, 145, 178, 0.14)'
        },
        '&.Mui-disabled': {
          color: 'rgba(203,213,225,.44)',
          background: 'rgba(51,65,85,.36)',
          border: '1px solid rgba(148,163,184,.16)',
          boxShadow: 'none',
          textShadow: 'none'
        }
      },
      outlined: {
        borderColor: palette.border,
        color: palette.textMuted,
        backgroundColor: 'rgba(15, 23, 42, 0.5)',
        boxShadow: 'inset 0 1px 0 rgba(255,255,255,.03)',
        '&:hover': {
          borderColor: palette.borderStrong,
          color: palette.text,
          backgroundColor: 'rgba(34, 211, 238, 0.07)'
        },
        '&.Mui-disabled': {
          color: 'rgba(203,213,225,.36)',
          borderColor: 'rgba(148,163,184,.12)',
          backgroundColor: 'rgba(51,65,85,.24)'
        }
      }
    }
  },
  MuiCard: {
    styleOverrides: {
      root: {
        borderRadius: 20,
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
  MuiTooltip: {
    styleOverrides: {
      tooltip: {
        backgroundColor: palette.surfaceRaised,
        color: palette.text,
        border: `1px solid ${palette.border}`,
        borderRadius: 8,
        boxShadow: '0 14px 34px rgba(0,0,0,.34)',
        padding: '8px 10px',
        fontSize: 12,
        lineHeight: 1.45,
        maxWidth: 360
      },
      arrow: {
        color: palette.surfaceRaised,
        '&::before': {
          border: `1px solid ${palette.border}`
        }
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
