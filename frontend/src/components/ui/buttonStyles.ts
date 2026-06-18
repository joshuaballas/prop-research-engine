import { palette } from '../../theme/palette';

export const primaryActionButtonSx = {
  color: palette.text,
  border: '1px solid rgba(125, 211, 252, 0.5)',
  background: 'rgba(11, 74, 94, 0.92)',
  textShadow: 'none',
  boxShadow: 'inset 0 1px 0 rgba(255,255,255,.12), 0 8px 20px rgba(8, 145, 178, 0.12)',
  transition:
    'background-color .16s ease, border-color .16s ease, box-shadow .16s ease, color .16s ease, transform .08s ease',
  '& .MuiButton-startIcon, & .MuiButton-endIcon': {
    color: 'inherit'
  },
  '&:hover': {
    color: palette.text,
    borderColor: 'rgba(165, 243, 252, 0.64)',
    background: 'rgba(13, 91, 116, 0.96)',
    boxShadow: 'inset 0 1px 0 rgba(255,255,255,.14)'
  },
  '&:active:not(.Mui-disabled)': {
    transform: 'translateY(1px)',
    boxShadow: 'inset 0 2px 8px rgba(3,7,18,.34), 0 5px 16px rgba(8, 145, 178, 0.14)'
  },
  '&:focus-visible': {
    outline: 'none',
    boxShadow: `0 0 0 3px rgba(34, 211, 238, 0.22), 0 0 0 1px ${palette.primary}`
  },
  '&.Mui-disabled': {
    color: 'rgba(203,213,225,.44)',
    background: 'rgba(51,65,85,.36)',
    border: '1px solid rgba(148,163,184,.16)',
    boxShadow: 'none',
    textShadow: 'none'
  }
};

const compactButtonBaseSx = {
  height: 32,
  minHeight: 32,
  px: 1.1,
  fontSize: 12,
  fontWeight: 900,
  whiteSpace: 'nowrap',
  borderRadius: 2,
  position: 'relative',
  overflow: 'visible',
  '& .MuiButton-startIcon': {
    mr: 0.55,
    ml: -0.15,
    color: 'inherit'
  }
};

export const secondaryGhostButtonSx = {
  ...compactButtonBaseSx,
  color: palette.textMuted,
  background: 'rgba(15, 23, 42, 0.48)',
  border: '1px solid rgba(148,163,184,.2)',
  boxShadow: 'inset 0 1px 0 rgba(255,255,255,.03)',
  '& .MuiButton-startIcon, & .MuiButton-endIcon': {
    color: 'inherit'
  },
  '&:hover': {
    color: palette.text,
    borderColor: 'rgba(125, 211, 252, 0.28)',
    background: 'rgba(34, 211, 238, 0.07)'
  },
  '&:focus-visible': {
    outline: 'none',
    boxShadow: `0 0 0 3px rgba(34, 211, 238, 0.18), 0 0 0 1px ${palette.borderStrong}`
  }
};

export const compactActionButtonSx = {
  ...compactButtonBaseSx,
  color: palette.text,
  background: 'rgba(11, 74, 94, 0.82)',
  border: '1px solid rgba(125, 211, 252, 0.48)',
  boxShadow: 'inset 0 1px 0 rgba(255,255,255,.08)',
  textShadow: 'none',
  '& .MuiButton-startIcon, & .MuiButton-endIcon': {
    color: 'inherit'
  },
  '&:hover': {
    color: palette.text,
    background: 'rgba(13, 91, 116, 0.9)',
    borderColor: 'rgba(165, 243, 252, 0.62)',
    boxShadow: 'inset 0 1px 0 rgba(255,255,255,.1)'
  },
  '&:active:not(.Mui-disabled)': {
    transform: 'translateY(1px)',
    boxShadow: 'inset 0 2px 8px rgba(3,7,18,.28)'
  },
  '&:active:not(.Mui-disabled)::after': {
    content: '""',
    position: 'absolute',
    inset: -4,
    borderRadius: 'inherit',
    border: '1px solid rgba(125, 211, 252, 0.45)',
    animation: 'edgeScopeAddPulse 360ms ease-out'
  },
  '@keyframes edgeScopeAddPulse': {
    '0%': { opacity: 0.72, transform: 'scale(0.98)' },
    '100%': { opacity: 0, transform: 'scale(1.08)' }
  },
  '@media (prefers-reduced-motion: reduce)': {
    '&:active:not(.Mui-disabled)::after': {
      animation: 'none'
    }
  },
  '&:focus-visible': {
    outline: 'none',
    boxShadow: `0 0 0 3px rgba(34, 211, 238, 0.2), 0 0 0 1px ${palette.primary}`
  },
  '&.Mui-disabled': {
    color: 'rgba(203,213,225,.44)',
    background: 'rgba(51,65,85,.36)',
    border: '1px solid rgba(148,163,184,.16)',
    boxShadow: 'none'
  }
};

export const removeActionButtonSx = {
  color: '#FFE4E6',
  background: 'rgba(127, 29, 29, 0.34)',
  border: '1px solid rgba(244,63,94,.44)',
  boxShadow: 'inset 0 1px 0 rgba(255,255,255,.08)',
  textShadow: 'none',
  '& .MuiButton-startIcon, & .MuiButton-endIcon': {
    color: 'inherit'
  },
  '&:hover': {
    color: '#FFE4E6',
    background: 'rgba(159, 18, 57, 0.4)',
    borderColor: 'rgba(251,113,133,.6)',
    boxShadow: 'inset 0 1px 0 rgba(255,255,255,.1)'
  },
  '&:active:not(.Mui-disabled)': {
    transform: 'translateY(1px)',
    boxShadow: 'inset 0 2px 8px rgba(3,7,18,.28)'
  },
  '&:focus-visible': {
    outline: 'none',
    boxShadow: '0 0 0 3px rgba(244, 63, 94, 0.22), 0 0 0 1px rgba(251, 113, 133, 0.72)'
  }
};

export const destructiveOutlineButtonSx = {
  color: '#FDA4AF',
  background: 'rgba(127, 29, 29, 0.12)',
  border: '1px solid rgba(244,63,94,.38)',
  boxShadow: 'inset 0 1px 0 rgba(255,255,255,.04)',
  '&:hover': {
    color: '#FFE4E6',
    background: 'rgba(127, 29, 29, 0.18)',
    borderColor: 'rgba(251,113,133,.56)'
  },
  '&:focus-visible': {
    outline: 'none',
    boxShadow: '0 0 0 3px rgba(244, 63, 94, 0.2), 0 0 0 1px rgba(251, 113, 133, 0.68)'
  }
};

export const lockedActionButtonSx = {
  color: '#CBD5E1',
  background: 'rgba(51,65,85,.42)',
  border: '1px solid rgba(148,163,184,.24)',
  boxShadow: 'none',
  textShadow: 'none',
  '& .MuiButton-startIcon, & .MuiButton-endIcon': {
    color: 'inherit'
  },
  '&.Mui-disabled': {
    color: '#CBD5E1',
    background: 'rgba(51,65,85,.42)',
    border: '1px solid rgba(148,163,184,.24)',
    opacity: 1,
    boxShadow: 'none'
  }
};
