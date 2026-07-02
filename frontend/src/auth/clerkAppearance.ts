import { palette } from '../theme/palette';
import { radius, resolveThemeRadiusPx } from '../theme/radius';

const cardRadius = `${resolveThemeRadiusPx(radius.featuredCard)}px`;
const controlRadius = `${resolveThemeRadiusPx(radius.control)}px`;
const subtleBorder = '1px solid rgba(148,163,184,.18)';
const primaryAction = palette.primaryDark;
const primaryActionFocus = 'rgba(8,145,178,.32)';
const appPrimaryButtonBackground = 'rgba(11, 74, 94, 0.92)';
const appPrimaryButtonHoverBackground = 'rgba(13, 91, 116, 0.96)';
const appPrimaryButtonBorder = '1px solid rgba(125, 211, 252, 0.5)';
const appPrimaryButtonHoverBorder = '1px solid rgba(165, 243, 252, 0.64)';
const appPrimaryButtonDisabledBackground = 'rgba(51,65,85,.36)';
const appPrimaryButtonDisabledBorder = '1px solid rgba(148,163,184,.16)';
const appPrimaryButtonDisabledColor = 'rgba(203,213,225,.44)';
const duplicateStartHeaderSelector = [
  '[data-localization-key="signIn.start.title"]',
  '[data-localization-key="signIn.start.titleCombined"]',
  '[data-localization-key="signIn.start.subtitle"]',
  '[data-localization-key="signIn.start.subtitleCombined"]',
  '[data-localization-key="signUp.start.title"]',
  '[data-localization-key="signUp.start.titleCombined"]',
  '[data-localization-key="signUp.start.subtitle"]',
  '[data-localization-key="signUp.start.subtitleCombined"]'
].join(', ');

export const clerkAppearance = {
  theme: 'simple',
  variables: {
    colorBackground: palette.bg,
    colorInputBackground: 'rgba(8,13,26,.76)',
    colorInputText: palette.text,
    colorPrimary: primaryAction,
    colorText: palette.text,
    colorTextSecondary: palette.textMuted,
    colorNeutral: palette.textMuted,
    colorDanger: palette.danger,
    colorSuccess: palette.success,
    colorWarning: palette.warning,
    colorRing: primaryAction,
    colorBorder: 'rgba(148,163,184,.18)',
    colorShadow: '#000000',
    borderRadius: cardRadius,
    fontFamily: 'Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
    fontFamilyButtons: 'Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
    fontSize: '0.875rem',
    spacing: '0.9rem'
  },
  layout: {
    logoPlacement: 'none'
  },
  captcha: {
    theme: 'dark'
  },
  elements: {
    rootBox: {
      width: '100%',
      overflow: 'visible'
    },
    cardBox: {
      width: '100%',
      boxShadow: 'none',
      background: 'transparent',
      border: '0',
      overflow: 'visible'
    },
    card: {
      width: '100%',
      backgroundColor: 'transparent',
      backgroundImage: 'none',
      border: '0',
      borderRadius: cardRadius,
      boxShadow: 'none',
      color: palette.text,
      overflow: 'visible'
    },
    header: {
      gap: '0.35rem',
      [`&:has(${duplicateStartHeaderSelector})`]: {
        display: 'none'
      }
    },
    headerTitle: {
      color: palette.text,
      fontWeight: 950,
      letterSpacing: 0,
      [`&:is(${duplicateStartHeaderSelector})`]: {
        display: 'none'
      }
    },
    headerSubtitle: {
      color: palette.textMuted,
      fontWeight: 650,
      [`&:is(${duplicateStartHeaderSelector})`]: {
        display: 'none'
      }
    },
    formHeaderTitle: {
      color: palette.text,
      fontWeight: 900
    },
    formHeaderSubtitle: {
      color: palette.textMuted
    },
    main: {
      width: '100%',
      color: palette.text
    },
    form: {
      width: '100%'
    },
    formField: {
      width: '100%'
    },
    formFieldRow: {
      width: '100%',
      gap: '0.75rem'
    },
    formFieldLabel: {
      color: palette.textMuted,
      fontWeight: 850
    },
    formFieldInput: {
      width: '100%',
      boxSizing: 'border-box',
      minHeight: '42px',
      backgroundColor: 'rgba(8,13,26,.76)',
      border: subtleBorder,
      borderRadius: controlRadius,
      color: palette.text,
      fontWeight: 750,
      boxShadow: 'none',
      '&:focus': {
        border: `1px solid ${primaryAction}`,
        boxShadow: `0 0 0 3px ${primaryActionFocus}`
      }
    },
    otpCodeFieldInput: {
      backgroundColor: 'rgba(8,13,26,.76)',
      border: subtleBorder,
      borderRadius: controlRadius,
      color: palette.text,
      fontWeight: 900,
      boxShadow: 'none',
      '&:focus': {
        border: `1px solid ${primaryAction}`,
        boxShadow: `0 0 0 3px ${primaryActionFocus}`
      }
    },
    formFieldInputShowPasswordButton: {
      color: palette.textMuted
    },
    formFieldAction: {
      color: primaryAction,
      fontWeight: 850
    },
    formButtonPrimary: {
      width: '100%',
      minHeight: '38px',
      paddingInline: '18px',
      backgroundColor: appPrimaryButtonBackground,
      border: appPrimaryButtonBorder,
      borderRadius: controlRadius,
      color: palette.text,
      fontWeight: 800,
      boxShadow: 'none',
      textShadow: 'none',
      textTransform: 'none',
      transition: 'background-color .16s ease, border-color .16s ease, color .16s ease, transform .08s ease',
      '&:hover': {
        backgroundColor: appPrimaryButtonHoverBackground,
        border: appPrimaryButtonHoverBorder,
        color: palette.text,
        boxShadow: 'none'
      },
      '&:focus-visible': {
        outline: `2px solid ${palette.primary}`,
        outlineOffset: '2px',
        boxShadow: 'none'
      },
      '&:active:not(:disabled)': {
        transform: 'translateY(1px)',
        boxShadow: 'none'
      },
      '&:disabled': {
        color: appPrimaryButtonDisabledColor,
        background: appPrimaryButtonDisabledBackground,
        border: appPrimaryButtonDisabledBorder,
        boxShadow: 'none',
        textShadow: 'none'
      }
    },
    formButtonPrimary__loading: {
      color: appPrimaryButtonDisabledColor,
      backgroundColor: appPrimaryButtonDisabledBackground,
      border: appPrimaryButtonDisabledBorder,
      boxShadow: 'none',
      textShadow: 'none'
    },
    buttonArrowIcon: {
      color: 'inherit'
    },
    formButtonReset: {
      color: primaryAction,
      fontWeight: 850
    },
    dividerLine: {
      backgroundColor: 'rgba(148,163,184,.18)'
    },
    dividerText: {
      color: palette.textMuted,
      fontWeight: 800
    },
    footer: {
      width: '100%',
      background: 'transparent',
      borderTop: subtleBorder
    },
    footerActionText: {
      color: palette.textMuted,
      fontWeight: 760
    },
    footerActionLink: {
      color: primaryAction,
      fontWeight: 900
    },
    footerPagesLink: {
      color: palette.textSubtle
    },
    socialButtonsBlockButton: {
      backgroundColor: 'rgba(15,23,42,.72)',
      border: subtleBorder,
      borderRadius: controlRadius,
      color: palette.text,
      boxShadow: 'none',
      '&:hover': {
        backgroundColor: 'rgba(15,23,42,.92)',
        borderColor: 'rgba(125,211,252,.28)'
      }
    },
    socialButtonsBlockButtonText: {
      color: palette.text,
      fontWeight: 850
    },
    identityPreview: {
      backgroundColor: 'transparent',
      border: '0',
      borderRadius: controlRadius,
      color: palette.text,
      boxShadow: 'none',
      minHeight: 'auto',
      padding: '0.25rem 0'
    },
    identityPreviewText: {
      color: palette.text,
      fontWeight: 850
    },
    identityPreviewEditButton: {
      color: primaryAction,
      borderRadius: controlRadius,
      '&:hover': {
        backgroundColor: 'rgba(34,211,238,.08)',
        color: palette.primary
      },
      '& svg': {
        color: 'inherit'
      }
    },
    alert: {
      backgroundColor: 'rgba(244,63,94,.10)',
      border: '1px solid rgba(244,63,94,.28)',
      borderRadius: controlRadius,
      color: palette.text
    },
    alertText: {
      color: palette.text
    },
    formFieldErrorText: {
      color: '#FDA4AF',
      fontWeight: 760
    },
    formResendCodeLink: {
      color: primaryAction,
      fontWeight: 900
    },
    alternativeMethodsBlockButton: {
      backgroundColor: 'rgba(15,23,42,.72)',
      border: subtleBorder,
      borderRadius: controlRadius,
      color: palette.text,
      boxShadow: 'none',
      '&:hover': {
        backgroundColor: 'rgba(15,23,42,.92)',
        borderColor: 'rgba(125,211,252,.28)'
      }
    },
    alternativeMethodsBlockButtonText: {
      color: palette.text,
      fontWeight: 850
    },
    backLink: {
      color: primaryAction,
      fontWeight: 850
    }
  }
} as const;
