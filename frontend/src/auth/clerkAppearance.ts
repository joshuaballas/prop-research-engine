import { palette } from '../theme/palette';
import { radius, resolveThemeRadiusPx } from '../theme/radius';

const cardRadius = `${resolveThemeRadiusPx(radius.featuredCard)}px`;
const controlRadius = `${resolveThemeRadiusPx(radius.control)}px`;
const userProfileSectionPadding = 'clamp(14px, 2vw, 20px)';
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

export const clerkBaseAppearance = {
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
  captcha: {
    theme: 'dark'
  }
} as const;

export const clerkAuthAppearance = {
  ...clerkBaseAppearance,
  layout: {
    logoPlacement: 'none'
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

export const clerkUserProfileAppearance = {
  ...clerkBaseAppearance,
  elements: {
    modalBackdrop: {
      backgroundColor: 'rgba(2, 6, 23, 0.72)'
    },
    modalContent: {
      width: 'min(920px, calc(100vw - 32px))',
      minWidth: 'min(820px, calc(100vw - 32px))',
      maxWidth: 'min(920px, calc(100vw - 32px))',
      maxHeight: 'min(760px, calc(100vh - 32px))',
      backgroundColor: palette.cardSurface,
      backgroundImage: 'none',
      border: `1px solid ${palette.border}`,
      borderRadius: cardRadius,
      backgroundClip: 'padding-box',
      boxShadow: '0 28px 80px rgba(0,0,0,.58)',
      color: palette.text,
      overflow: 'hidden',
      isolation: 'isolate'
    },
    modalCloseButton: {
      color: palette.textMuted,
      borderRadius: controlRadius,
      boxShadow: 'none',
      '&:hover': {
        backgroundColor: 'rgba(148, 163, 184, 0.12)',
        color: palette.text
      },
      '&:focus:not(:focus-visible)': {
        outline: 'none',
        boxShadow: 'none'
      },
      '&:focus-visible': {
        outline: `2px solid ${palette.primary}`,
        outlineOffset: '2px',
        boxShadow: 'none'
      }
    },
    rootBox: {
      width: '100%',
      borderRadius: cardRadius,
      overflow: 'hidden'
    },
    cardBox: {
      width: '100%',
      maxWidth: 'none',
      boxShadow: 'none',
      background: 'transparent',
      border: '0',
      borderRadius: cardRadius,
      overflow: 'hidden'
    },
    card: {
      width: '100%',
      maxWidth: 'none',
      minHeight: 'min(620px, calc(100vh - 32px))',
      backgroundColor: palette.cardSurface,
      backgroundImage: 'none',
      border: '0',
      borderRadius: cardRadius,
      boxShadow: 'none',
      color: palette.text,
      overflow: 'hidden'
    },
    main: {
      width: '100%',
      maxWidth: 'none',
      minWidth: 0,
      flex: '1 1 auto',
      borderTopLeftRadius: 0,
      borderBottomLeftRadius: 0,
      borderTopRightRadius: cardRadius,
      borderBottomRightRadius: cardRadius,
      overflow: 'hidden'
    },
    navbar: {
      width: '220px',
      minWidth: '220px',
      backgroundColor: 'rgba(8, 13, 26, 0.88)',
      borderRight: subtleBorder,
      borderTopLeftRadius: cardRadius,
      borderBottomLeftRadius: cardRadius
    },
    navbarButtons: {
      width: '100%'
    },
    navbarButton: {
      color: palette.textMuted,
      fontWeight: 850,
      borderRadius: controlRadius,
      '&:hover': {
        backgroundColor: 'rgba(34, 211, 238, 0.08)',
        color: palette.text
      }
    },
    navbarButton__active: {
      backgroundColor: 'rgba(34, 211, 238, 0.11)',
      color: palette.text
    },
    navbarButton__billing: {
      display: 'none'
    },
    navbarButtonIcon: {
      color: 'inherit'
    },
    navbarButtonText: {
      color: 'inherit',
      fontWeight: 850
    },
    scrollBox: {
      borderRadius: 0
    },
    pageScrollBox: {
      width: '100%',
      maxWidth: 'none',
      backgroundColor: 'transparent',
      borderTopLeftRadius: 0,
      borderBottomLeftRadius: 0,
      borderTopRightRadius: cardRadius,
      borderBottomRightRadius: cardRadius
    },
    page: {
      width: '100%',
      maxWidth: 'none',
      backgroundColor: 'transparent',
      color: palette.text,
      boxSizing: 'border-box',
      borderTopLeftRadius: 0,
      borderBottomLeftRadius: 0,
      borderTopRightRadius: cardRadius,
      borderBottomRightRadius: cardRadius
    },
    profilePage: {
      width: '100%',
      maxWidth: 'none'
    },
    profilePage__billing: {
      display: 'none'
    },
    profileSection: {
      width: '100%',
      maxWidth: 'none',
      backgroundColor: 'rgba(8, 13, 26, 0.44)',
      border: subtleBorder,
      borderRadius: cardRadius,
      overflow: 'hidden',
      boxSizing: 'border-box',
      '& .cl-profileSectionHeader': {
        paddingInline: userProfileSectionPadding,
        boxSizing: 'border-box'
      },
      '& .cl-profileSectionContent': {
        paddingInline: userProfileSectionPadding,
        boxSizing: 'border-box'
      },
      '& .cl-profileSectionItem': {
        paddingInline: userProfileSectionPadding,
        boxSizing: 'border-box'
      },
      '& + &': {
        marginTop: '10px'
      }
    },
    profileSection__emailAddresses: {
      marginTop: '10px',
      '& .cl-profileSectionItem__emailAddresses': {
        paddingInlineStart: 0
      },
      '& .cl-profileSectionItem__emailAddresses > div:first-of-type': {
        gap: '10px'
      }
    },
    profileSection__profile: {
      '@media (max-width: 767.95px)': {
        '& .cl-profileSectionItem__profile': {
          flexDirection: 'column',
          alignItems: 'stretch',
          justifyContent: 'flex-start',
          gap: '12px'
        },
        '& .cl-userPreview': {
          width: '100%',
          minWidth: 0
        },
        '& .cl-userPreviewTextContainer': {
          minWidth: 0,
          flex: '1 1 auto'
        },
        '& .cl-userPreviewMainIdentifierText': {
          whiteSpace: 'normal',
          overflow: 'visible',
          textOverflow: 'clip'
        }
      }
    },
    profileSection__activeDevices: {
      marginTop: '10px'
    },
    profileSectionHeader: {
      borderColor: 'rgba(148,163,184,.14)',
      paddingInline: userProfileSectionPadding,
      boxSizing: 'border-box'
    },
    profileSectionHeader__profile: {
      '@media (min-width: 768px)': {
        marginTop: '15px'
      }
    },
    profileSectionTitleText: {
      color: palette.text,
      fontWeight: 950
    },
    profileSectionSubtitleText: {
      color: palette.textMuted
    },
    profileSectionContent: {
      paddingInline: userProfileSectionPadding,
      boxSizing: 'border-box'
    },
    profileSectionItemList: {
      boxSizing: 'border-box'
    },
    profileSectionItem: {
      borderColor: 'rgba(148,163,184,.14)',
      paddingInline: userProfileSectionPadding,
      paddingBlock: '14px',
      boxSizing: 'border-box'
    },
    profileSectionItemButton: {
      paddingInline: userProfileSectionPadding,
      boxSizing: 'border-box'
    },
    formField: {
      width: '100%'
    },
    formField__signOutOfOtherSessions: {
      width: '100%',
      maxWidth: 'none'
    },
    formFieldRow: {
      gap: '0.75rem'
    },
    formFieldRow__signOutOfOtherSessions: {
      width: '100%',
      maxWidth: 'none',
      display: 'block',
      '& > div': {
        width: '100%',
        maxWidth: 'none',
        display: 'grid',
        gridTemplateColumns: '18px minmax(0, 1fr)',
        alignItems: 'flex-start',
        gap: '10px'
      },
      '& > div > input[type="checkbox"]': {
        gridColumn: '1',
        gridRow: '1',
        alignSelf: 'start'
      },
      '& > div > label': {
        gridColumn: '2',
        minWidth: 0,
        width: '100%',
        maxWidth: 'none'
      }
    },
    formFieldLabel: {
      color: 'rgba(203, 213, 225, 0.84)',
      fontWeight: 850
    },
    formFieldInput: {
      boxSizing: 'border-box',
      color: palette.text,
      fontWeight: 760,
      boxShadow: 'none',
      '&:not([type="checkbox"])': {
        width: '100%',
        minHeight: '40px',
        backgroundColor: 'rgba(8, 13, 26, 0.84)',
        border: '1px solid rgba(148, 163, 184, 0.28)',
        borderRadius: controlRadius
      },
      '&[type="checkbox"]': {
        width: '16px',
        height: '16px',
        minWidth: '16px',
        minHeight: '16px',
        flex: '0 0 16px',
        margin: '2px 0 0',
        padding: 0,
        backgroundColor: 'transparent',
        border: '1px solid rgba(148, 163, 184, 0.46)',
        borderRadius: '3px',
        accentColor: primaryAction,
        boxShadow: 'none'
      },
      '&::placeholder': {
        color: 'rgba(148, 163, 184, 0.72)'
      },
      '&:not([type="checkbox"]):hover': {
        borderColor: 'rgba(125, 211, 252, 0.36)'
      },
      '&:not([type="checkbox"]):focus': {
        border: `1px solid ${primaryAction}`,
        boxShadow: `0 0 0 3px ${primaryActionFocus}`
      },
      '&[type="checkbox"]:focus-visible': {
        outline: `2px solid ${palette.primary}`,
        outlineOffset: '2px'
      },
      '&:not([type="checkbox"]):disabled': {
        color: 'rgba(203, 213, 225, 0.48)',
        backgroundColor: 'rgba(15, 23, 42, 0.5)',
        borderColor: 'rgba(148, 163, 184, 0.16)'
      }
    },
    formFieldInputShowPasswordButton: {
      color: palette.textMuted,
      borderRadius: controlRadius,
      '&:hover': {
        backgroundColor: 'rgba(148, 163, 184, 0.12)',
        color: palette.text
      }
    },
    formFieldInputShowPasswordIcon: {
      color: 'inherit',
      opacity: 1
    },
    formFieldCheckboxInput: {
      width: '16px',
      height: '16px',
      minWidth: '16px',
      minHeight: '16px',
      flex: '0 0 16px',
      margin: '2px 0 0',
      accentColor: primaryAction,
      borderColor: 'rgba(148, 163, 184, 0.38)',
      color: palette.text,
      boxShadow: 'none'
    },
    formFieldCheckboxLabel: {
      display: 'block',
      minWidth: 0,
      maxWidth: 'none',
      lineHeight: 1.45,
      color: 'rgba(203, 213, 225, 0.86)',
      fontWeight: 760
    },
    formFieldCheckboxLabel__signOutOfOtherSessions: {
      width: '100%',
      maxWidth: 'none',
      whiteSpace: 'normal'
    },
    formFieldRadioLabel__signOutOfOtherSessions: {
      width: '100%',
      maxWidth: 'none',
      minWidth: 0,
      display: 'block',
      whiteSpace: 'normal'
    },
    formFieldRadioLabelTitle__signOutOfOtherSessions: {
      width: '100%',
      maxWidth: 'none',
      color: palette.text,
      fontWeight: 850,
      lineHeight: 1.35,
      whiteSpace: 'normal'
    },
    formFieldRadioLabelDescription__signOutOfOtherSessions: {
      width: '100%',
      maxWidth: 'none',
      color: 'rgba(203, 213, 225, 0.74)',
      lineHeight: 1.45,
      whiteSpace: 'normal'
    },
    formFieldAction: {
      color: primaryAction,
      fontWeight: 850,
      '&:hover': {
        color: palette.primary
      }
    },
    formFieldErrorText: {
      color: '#FDA4AF',
      fontWeight: 760
    },
    formButtonPrimary: {
      width: 'auto',
      maxWidth: '100%',
      minWidth: '0',
      minHeight: '38px',
      paddingInline: '16px',
      alignSelf: 'flex-start',
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
      boxShadow: 'none'
    },
    formButtonReset: {
      width: 'auto',
      minHeight: '36px',
      paddingInline: '12px',
      color: primaryAction,
      borderRadius: controlRadius,
      fontWeight: 850,
      '&:hover': {
        backgroundColor: 'rgba(34, 211, 238, 0.08)',
        color: palette.primary
      },
      '&:focus-visible': {
        outline: `2px solid ${palette.primary}`,
        outlineOffset: '2px'
      },
      '&:disabled': {
        color: appPrimaryButtonDisabledColor
      }
    },
    avatarImageActions: {
      gap: '8px'
    },
    avatarImageActionsUpload: {
      minHeight: '36px',
      paddingInline: '14px',
      backgroundColor: 'rgba(15, 23, 42, 0.72)',
      border: '1px solid rgba(125, 211, 252, 0.34)',
      borderRadius: controlRadius,
      color: palette.text,
      fontWeight: 850,
      boxShadow: 'none',
      '&:hover': {
        backgroundColor: 'rgba(15, 23, 42, 0.94)',
        borderColor: 'rgba(165, 243, 252, 0.52)',
        color: palette.text
      },
      '&:focus-visible': {
        outline: `2px solid ${palette.primary}`,
        outlineOffset: '2px'
      }
    },
    avatarImageActionsRemove: {
      minHeight: '36px',
      paddingInline: '12px',
      color: palette.textMuted,
      borderRadius: controlRadius,
      fontWeight: 850,
      '&:hover': {
        backgroundColor: 'rgba(244, 63, 94, 0.1)',
        color: '#FDA4AF'
      }
    },
    activeDevice: {
      color: 'rgba(203, 213, 225, 0.82)',
      '& svg': {
        color: 'rgba(203, 213, 225, 0.78)',
        opacity: 1
      }
    },
    activeDevice__current: {
      color: 'rgba(203, 213, 225, 0.9)'
    },
    activeDeviceListItem: {
      color: 'rgba(203, 213, 225, 0.82)'
    },
    activeDeviceListItem__current: {
      color: 'rgba(203, 213, 225, 0.9)'
    },
    activeDeviceIcon: {
      color: 'rgba(203, 213, 225, 0.78)',
      opacity: 1
    },
    badge: {
      color: 'rgba(203, 213, 225, 0.82)',
      backgroundColor: 'transparent',
      border: 0,
      boxShadow: 'none',
      padding: 0,
      fontWeight: 850
    },
    badge__primary: {
      color: 'rgba(203, 213, 225, 0.86)',
      backgroundColor: 'transparent',
      border: 0
    },
    menuButton: {
      color: palette.textMuted,
      borderRadius: controlRadius,
      '&:hover': {
        backgroundColor: 'rgba(148, 163, 184, 0.12)',
        color: palette.text
      },
      '&:focus-visible': {
        outline: `2px solid ${palette.primary}`,
        outlineOffset: '2px'
      }
    },
    menuButtonEllipsis: {
      color: palette.textMuted,
      opacity: 1,
      '& svg': {
        color: 'inherit',
        opacity: 1
      }
    },
    menuButtonEllipsisBordered: {
      color: palette.textMuted,
      borderColor: 'rgba(148, 163, 184, 0.22)',
      '&:hover': {
        color: palette.text,
        borderColor: 'rgba(125, 211, 252, 0.34)'
      }
    },
    profileSectionPrimaryButton: {
      width: 'auto',
      maxWidth: '100%',
      alignSelf: 'flex-start',
      minHeight: '36px',
      paddingInline: '16px',
      backgroundColor: appPrimaryButtonBackground,
      border: appPrimaryButtonBorder,
      borderRadius: controlRadius,
      color: palette.text,
      fontWeight: 800,
      boxShadow: 'none',
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
      }
    },
    profileSectionPrimaryButton__profile: {
      '@media (min-width: 768px)': {
        alignSelf: 'center'
      }
    },
    profileSectionPrimaryButton__emailAddresses: {
      width: 'auto',
      maxWidth: '100%',
      alignSelf: 'flex-start'
    },
    profileSectionButtonGroup: {
      justifyContent: 'flex-start',
      gap: '10px',
      flexWrap: 'wrap'
    },
    footer: {
      background: 'transparent',
      borderTop: subtleBorder
    },
    footerPagesLink: {
      color: palette.textSubtle
    }
  }
} as const;
