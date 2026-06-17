import { createTheme } from '@mui/material/styles';
import { components } from './components';
import { palette } from './palette';
import { fontFamily } from './typography';

export const theme = createTheme({
  palette: {
    mode: 'dark',
    background: {
      default: palette.bg,
      paper: palette.surface
    },
    primary: {
      main: palette.primary,
      dark: palette.primaryDark,
      contrastText: palette.text
    },
    secondary: {
      main: palette.secondary
    },
    success: { main: palette.success },
    warning: { main: palette.warning },
    error: { main: palette.danger },
    text: {
      primary: palette.text,
      secondary: palette.textMuted
    },
    divider: palette.border
  },
  typography: {
    fontFamily,
    h1: { fontWeight: 900, letterSpacing: 0, lineHeight: 1.04 },
    h2: { fontWeight: 900, letterSpacing: 0, lineHeight: 1.08 },
    h3: { fontWeight: 900, letterSpacing: 0 },
    h4: { fontWeight: 900, letterSpacing: 0 },
    h5: { fontWeight: 850, letterSpacing: 0 },
    h6: { fontWeight: 850, letterSpacing: 0 },
    button: { letterSpacing: 0 }
  },
  shape: {
    borderRadius: 5
  },
  components
});
