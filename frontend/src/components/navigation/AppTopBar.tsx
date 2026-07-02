import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded';
import ManageAccountsOutlinedIcon from '@mui/icons-material/ManageAccountsOutlined';
import PaymentOutlinedIcon from '@mui/icons-material/PaymentOutlined';
import { Avatar, Box, Button, ClickAwayListener, Divider, IconButton, ListItemIcon, Menu, MenuItem, Stack, Tooltip, Typography, useMediaQuery, useTheme } from '@mui/material';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { GlossaryDialog } from '../glossary/GlossaryDialog';
import type { MarketFreshnessDto, MarketReadinessDto } from '../../api/types';
import { useAppAuth } from '../../auth/AppAuthProvider';
import { isDemoDataMode } from '../../config/dataMode';
import { useMarketFreshnessData } from '../../hooks/useMarketData';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

export function AppTopBar() {
  const [glossaryOpen, setGlossaryOpen] = useState(false);
  const [profileAnchorEl, setProfileAnchorEl] = useState<HTMLElement | null>(null);
  const [statusPopoverOpen, setStatusPopoverOpen] = useState(false);
  const navigate = useNavigate();
  const theme = useTheme();
  const desktopProfileMenuEnabled = useMediaQuery(theme.breakpoints.up('lg'));
  const auth = useAppAuth();
  const freshnessQuery = useMarketFreshnessData(['app-top-bar-live-data']);
  const freshness = freshnessQuery.data;
  const marketDataStatus = resolveMarketDataStatus(freshness, freshnessQuery.loading, Boolean(freshnessQuery.error));
  const userInitial = (auth.user?.displayName || auth.user?.email || 'U').trim()[0]?.toUpperCase() ?? 'U';
  const profileMenuOpen = desktopProfileMenuEnabled && Boolean(profileAnchorEl);
  const displayName = auth.user?.displayName?.trim();
  const email = auth.user?.email?.trim();
  const accountLabel = displayName || email || 'Account';
  const secondaryAccountLabel = displayName && email ? email : undefined;

  useEffect(() => {
    if (!desktopProfileMenuEnabled) {
      setProfileAnchorEl(null);
    }
  }, [desktopProfileMenuEnabled]);

  useEffect(() => {
    if (profileMenuOpen) {
      setStatusPopoverOpen(false);
    }
  }, [profileMenuOpen]);

  const closeProfileMenu = () => setProfileAnchorEl(null);

  const navigateFromProfileMenu = (path: string) => {
    closeProfileMenu();
    navigate(path);
  };

  const handleSignOut = async () => {
    closeProfileMenu();
    await auth.signOut();
  };

  return (
    <>
      <Stack
        direction="row"
        alignItems="center"
        justifyContent="space-between"
        gap={2}
        sx={{ py: 1.1, px: { xs: 2, md: 3 }, borderBottom: '1px solid rgba(148,163,184,.12)', bgcolor: '#070B14', position: 'sticky', top: 0, zIndex: 10, flexShrink: 0 }}
      >
        <Button
          variant="outlined"
          size="small"
          startIcon={<HelpOutlineRoundedIcon />}
          onClick={() => setGlossaryOpen(true)}
          sx={{
            minHeight: 34,
            px: 1.2,
            borderRadius: radius.card,
            borderColor: 'rgba(148,163,184,.18)',
            bgcolor: 'rgba(15,23,42,.52)',
            color: 'text.secondary',
            fontWeight: 800,
            '& .MuiButton-startIcon': { color: 'inherit' },
            '&:hover': {
              borderColor: 'rgba(34,211,238,.38)',
              bgcolor: 'rgba(15,23,42,.78)',
              color: 'text.primary'
            }
          }}
        >
          Glossary
        </Button>
        <Stack direction="row" spacing={{ xs: 1, sm: 2 }} alignItems="center" minWidth={0} sx={{ flex: { xs: 1, sm: '0 1 auto' }, justifyContent: 'flex-end' }}>
          {isDemoDataMode ? (
            <Typography
              variant="caption"
              sx={{
                color: 'warning.main',
                fontWeight: 850
              }}
            >
              Demo data
            </Typography>
          ) : null}
          {desktopProfileMenuEnabled ? (
            <Tooltip
              title={<MarketDataStatusPopover status={marketDataStatus} />}
              arrow
              placement="bottom-end"
              open={statusPopoverOpen && !profileMenuOpen}
              disableFocusListener
              disableHoverListener
              disableTouchListener
              slotProps={{
                tooltip: {
                  sx: {
                    mt: 1,
                    maxWidth: 280,
                    p: 1.25,
                    borderRadius: radius.card,
                    border: `1px solid ${palette.border}`,
                    bgcolor: '#080D1A',
                    backgroundImage: 'none',
                    color: 'text.primary',
                    boxShadow: '0 16px 42px rgba(0,0,0,.42)'
                  }
                },
                arrow: {
                  sx: {
                    color: '#080D1A',
                    '&:before': {
                      border: `1px solid ${palette.border}`
                    }
                  }
                }
              }}
            >
              <IconButton
                aria-label="Open account menu"
                aria-controls={profileMenuOpen ? 'top-bar-profile-menu' : undefined}
                aria-haspopup="menu"
                aria-expanded={profileMenuOpen ? 'true' : undefined}
                onMouseEnter={() => setStatusPopoverOpen(true)}
                onMouseLeave={() => setStatusPopoverOpen(false)}
                onFocus={() => setStatusPopoverOpen(true)}
                onBlur={() => setStatusPopoverOpen(false)}
                onClick={(event) => {
                  setStatusPopoverOpen(false);
                  setProfileAnchorEl(event.currentTarget);
                }}
                size="small"
                sx={{
                  p: 0.25,
                  borderRadius: radius.circle,
                  color: 'text.primary',
                  '&:hover': { bgcolor: 'rgba(148,163,184,.12)' },
                  '&:focus-visible': {
                    outline: `2px solid ${palette.primary}`,
                    outlineOffset: 2
                  }
                }}
              >
                <AvatarWithStatusBadge initial={userInitial} status={marketDataStatus} />
              </IconButton>
            </Tooltip>
          ) : (
            <ClickAwayListener onClickAway={() => setStatusPopoverOpen(false)}>
              <Box sx={{ flexShrink: 0 }}>
                <Tooltip
                  title={<MarketDataStatusPopover status={marketDataStatus} />}
                  arrow
                  placement="bottom-end"
                  open={statusPopoverOpen}
                  disableFocusListener
                  disableHoverListener
                  disableTouchListener
                  slotProps={{
                    tooltip: {
                      sx: {
                        mt: 1,
                        maxWidth: 260,
                        p: 1.25,
                        borderRadius: radius.card,
                        border: `1px solid ${palette.border}`,
                        bgcolor: '#080D1A',
                        backgroundImage: 'none',
                        color: 'text.primary',
                        boxShadow: '0 16px 42px rgba(0,0,0,.42)'
                      }
                    },
                    arrow: {
                      sx: {
                        color: '#080D1A',
                        '&:before': {
                          border: `1px solid ${palette.border}`
                        }
                      }
                    }
                  }}
                >
                  <IconButton
                    aria-label={`Show market data status: ${marketDataStatus.label}`}
                    aria-expanded={statusPopoverOpen ? 'true' : undefined}
                    onClick={() => setStatusPopoverOpen((open) => !open)}
                    onKeyDown={(event) => {
                      if (event.key === 'Escape') {
                        setStatusPopoverOpen(false);
                      }
                    }}
                    size="small"
                    sx={{
                      p: 0.25,
                      borderRadius: radius.circle,
                      color: 'text.primary',
                      '&:hover': { bgcolor: 'rgba(148,163,184,.08)' },
                      '&:focus-visible': {
                        outline: `2px solid ${palette.primary}`,
                        outlineOffset: 2
                      }
                    }}
                  >
                    <AvatarWithStatusBadge initial={userInitial} status={marketDataStatus} />
                  </IconButton>
                </Tooltip>
              </Box>
            </ClickAwayListener>
          )}
        </Stack>
      </Stack>
      {desktopProfileMenuEnabled ? (
        <Menu
          id="top-bar-profile-menu"
          anchorEl={profileAnchorEl}
          open={profileMenuOpen}
          onClose={closeProfileMenu}
          anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
          transformOrigin={{ vertical: 'top', horizontal: 'right' }}
          PaperProps={{
            sx: {
              mt: 1,
              minWidth: 240,
              borderRadius: radius.card,
              border: `1px solid ${palette.border}`,
              bgcolor: '#080D1A',
              backgroundImage: 'none',
              color: 'text.primary',
              boxShadow: '0 22px 60px rgba(0,0,0,.45)'
            }
          }}
          MenuListProps={{ 'aria-label': 'Account menu' }}
        >
          <Box sx={{ px: 1.75, py: 1.4, maxWidth: 280 }}>
            <Typography fontWeight={950} noWrap>
              {accountLabel}
            </Typography>
            {secondaryAccountLabel ? (
              <Typography variant="body2" color="text.secondary" noWrap>
                {secondaryAccountLabel}
              </Typography>
            ) : null}
          </Box>
          <Divider sx={{ borderColor: palette.border }} />
          <MenuItem onClick={() => navigateFromProfileMenu('/app/account')}>
            <ListItemIcon sx={{ color: 'text.secondary', minWidth: 34 }}>
              <ManageAccountsOutlinedIcon fontSize="small" />
            </ListItemIcon>
            Account
          </MenuItem>
          <MenuItem onClick={() => navigateFromProfileMenu('/app/billing')}>
            <ListItemIcon sx={{ color: 'text.secondary', minWidth: 34 }}>
              <PaymentOutlinedIcon fontSize="small" />
            </ListItemIcon>
            Billing
          </MenuItem>
          <Divider sx={{ borderColor: palette.border }} />
          <MenuItem onClick={() => void handleSignOut()} sx={{ color: palette.danger }}>
            <ListItemIcon sx={{ color: 'inherit', minWidth: 34 }}>
              <LogoutRoundedIcon fontSize="small" />
            </ListItemIcon>
            Sign out
          </MenuItem>
        </Menu>
      ) : null}
      <GlossaryDialog open={glossaryOpen} onClose={() => setGlossaryOpen(false)} />
    </>
  );
}

type MarketDataStatusTone = 'fresh' | 'syncing' | 'issue' | 'unknown';

interface MarketDataStatusView {
  tone: MarketDataStatusTone;
  label: string;
  badgeColor: string;
  detailLabel: string;
}

function AvatarWithStatusBadge({ initial, status }: { initial: string; status: MarketDataStatusView }) {
  return (
    <Box sx={{ position: 'relative', width: 34, height: 34, flexShrink: 0 }}>
      <Avatar sx={{ width: 34, height: 34 }}>{initial}</Avatar>
      <Box
        component="span"
        sx={{
          position: 'absolute',
          top: 1,
          right: 1,
          width: 10,
          height: 10,
          borderRadius: radius.circle,
          bgcolor: status.badgeColor,
          border: '2px solid #070B14',
          boxShadow: `0 0 0 1px ${status.badgeColor}55`
        }}
      />
    </Box>
  );
}

function MarketDataStatusPopover({ status }: { status: MarketDataStatusView }) {
  return (
    <Stack spacing={0.55}>
      <Stack direction="row" spacing={0.75} alignItems="center">
        <Box
          component="span"
          sx={{
            width: 8,
            height: 8,
            borderRadius: radius.circle,
            bgcolor: status.badgeColor,
            flexShrink: 0
          }}
        />
        <Typography variant="body2" fontWeight={850} lineHeight={1.2}>
          {status.label}
        </Typography>
      </Stack>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', lineHeight: 1.35 }}>
        {status.detailLabel}
      </Typography>
    </Stack>
  );
}

function resolveMarketDataStatus(freshness: MarketFreshnessDto | undefined, isLoading: boolean, hasPollingError: boolean): MarketDataStatusView {
  if (hasPollingError && !freshness) {
    return {
      tone: 'issue',
      label: 'Refresh issue',
      badgeColor: palette.danger,
      detailLabel: 'Market data is unavailable'
    };
  }

  if (isLoading && !freshness) {
    return {
      tone: 'unknown',
      label: 'Checking',
      badgeColor: palette.textSubtle,
      detailLabel: 'Market status is loading'
    };
  }

  const readiness = freshness?.marketReadiness;
  const label = resolveMarketDataStatusLabel(readiness, freshness, hasPollingError);
  const tone = resolveMarketDataStatusTone(readiness, freshness, hasPollingError);

  return {
    tone,
    label,
    badgeColor: resolveMarketDataBadgeColor(tone),
    detailLabel: resolveMarketDataDetail(label)
  };
}

function resolveMarketDataStatusTone(
  readiness: MarketReadinessDto | null | undefined,
  freshness: MarketFreshnessDto | undefined,
  hasPollingError: boolean
): MarketDataStatusTone {
  if (hasPollingError) {
    return 'issue';
  }

  const status = readiness?.status?.trim().toLowerCase();
  const severity = readiness?.severity?.trim().toLowerCase();

  if (severity === 'error' || status === 'failed' || status === 'providerunavailable') {
    return 'issue';
  }

  if (freshness?.hasUsableMarketData === false || freshness?.providerRejectedKeyDetected === true || hasProviderIssue(freshness?.providerStatus?.status, freshness?.hasUsableMarketData)) {
    return 'issue';
  }

  if (status === 'fresh' && (severity === undefined || severity === 'ok')) {
    return 'fresh';
  }

  if (['syncing', 'initializing', 'partial', 'stale'].includes(status ?? '') || severity === 'info' || severity === 'warning') {
    return 'syncing';
  }

  return status ? 'unknown' : freshness ? 'fresh' : 'unknown';
}

function resolveMarketDataStatusLabel(
  readiness: MarketReadinessDto | null | undefined,
  freshness: MarketFreshnessDto | undefined,
  hasPollingError: boolean
) {
  if (hasPollingError) {
    return 'Refresh issue';
  }

  const status = readiness?.status?.trim().toLowerCase();
  const severity = readiness?.severity?.trim().toLowerCase();

  if (status === 'providerunavailable') {
    return 'Provider unavailable';
  }

  if (severity === 'error' || status === 'failed') {
    return 'Refresh issue';
  }

  if (status === 'fresh' && (severity === undefined || severity === 'ok')) {
    return 'Fresh';
  }

  if (status === 'syncing') {
    return 'Syncing';
  }

  if (status === 'initializing') {
    return 'Initializing';
  }

  if (status === 'partial') {
    return 'Partial';
  }

  if (status === 'stale') {
    return 'Stale';
  }

  if (freshness?.hasUsableMarketData === false || freshness?.providerRejectedKeyDetected === true || hasProviderIssue(freshness?.providerStatus?.status, freshness?.hasUsableMarketData)) {
    return 'Refresh issue';
  }

  if (status) {
    return 'Unknown';
  }

  return freshness ? 'Fresh' : 'Unknown';
}

function resolveMarketDataBadgeColor(tone: MarketDataStatusTone) {
  if (tone === 'fresh') {
    return palette.success;
  }

  if (tone === 'syncing') {
    return palette.warning;
  }

  if (tone === 'issue') {
    return palette.danger;
  }

  return palette.textSubtle;
}

function resolveMarketDataDetail(label: string) {
  if (label === 'Fresh') {
    return 'Market data is up-to-date';
  }

  if (label === 'Checking') {
    return 'Market status is loading';
  }

  if (label === 'Syncing') {
    return 'Market data is updating';
  }

  if (label === 'Partial') {
    return 'Some markets are still updating';
  }

  if (label === 'Stale') {
    return 'Market data may be outdated';
  }

  if (label === 'Refresh issue' || label === 'Provider unavailable') {
    return 'Market data is unavailable';
  }

  return 'Market status is unavailable';
}

function hasProviderIssue(status: string | undefined, hasUsableMarketData: boolean | undefined) {
  const normalizedStatus = status?.trim().toLowerCase();
  if (!normalizedStatus) {
    return false;
  }

  if (normalizedStatus === 'degraded') {
    return hasUsableMarketData !== true;
  }

  return ['blocked', 'error', 'rejectedkey', 'unavailable', 'unhealthy'].includes(normalizedStatus);
}
