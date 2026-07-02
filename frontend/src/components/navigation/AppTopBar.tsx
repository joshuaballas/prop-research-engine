import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded';
import ManageAccountsOutlinedIcon from '@mui/icons-material/ManageAccountsOutlined';
import PaymentOutlinedIcon from '@mui/icons-material/PaymentOutlined';
import { Avatar, Box, Button, Divider, IconButton, ListItemIcon, Menu, MenuItem, Stack, Tooltip, Typography, useMediaQuery, useTheme } from '@mui/material';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { GlossaryDialog } from '../glossary/GlossaryDialog';
import type { MarketReadinessDto } from '../../api/types';
import { useAppAuth } from '../../auth/AppAuthProvider';
import { isDemoDataMode } from '../../config/dataMode';
import { useMarketFreshnessData } from '../../hooks/useMarketData';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

export function AppTopBar() {
  const [glossaryOpen, setGlossaryOpen] = useState(false);
  const [profileAnchorEl, setProfileAnchorEl] = useState<HTMLElement | null>(null);
  const navigate = useNavigate();
  const theme = useTheme();
  const desktopProfileMenuEnabled = useMediaQuery(theme.breakpoints.up('lg'));
  const auth = useAppAuth();
  const freshnessQuery = useMarketFreshnessData(['app-top-bar-live-data']);
  const freshness = freshnessQuery.data;
  const liveDataState = resolveLiveDataState(freshness, Boolean(freshnessQuery.error));
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
        sx={{ py: 1.1, px: { xs: 2, md: 3 }, borderBottom: '1px solid rgba(148,163,184,.12)', bgcolor: '#070B14', position: 'sticky', top: 0, zIndex: 10 }}
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
          {liveDataState ? <LiveDataIndicator state={liveDataState} /> : null}
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
          <Box aria-hidden="true" sx={{ display: { xs: 'flex', lg: 'none' }, alignItems: 'center', flexShrink: 0 }}>
            <Avatar sx={{ width: 34, height: 34 }}>{userInitial}</Avatar>
          </Box>
          <Tooltip title="Account menu" arrow>
            <IconButton
              aria-label="Open account menu"
              aria-controls={profileMenuOpen ? 'top-bar-profile-menu' : undefined}
              aria-haspopup="menu"
              aria-expanded={profileMenuOpen ? 'true' : undefined}
              onClick={(event) => setProfileAnchorEl(event.currentTarget)}
              size="small"
              sx={{
                display: { xs: 'none', lg: 'inline-flex' },
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
              <Avatar sx={{ width: 34, height: 34 }}>{userInitial}</Avatar>
            </IconButton>
          </Tooltip>
        </Stack>
      </Stack>
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
      <GlossaryDialog open={glossaryOpen} onClose={() => setGlossaryOpen(false)} />
    </>
  );
}

type LiveDataState = 'healthy' | 'syncing' | 'issue';

function LiveDataIndicator({ state }: { state: LiveDataState }) {
  const isHealthy = state === 'healthy';
  const isIssue = state === 'issue';
  const label = isHealthy ? 'Auto refresh' : isIssue ? 'Refresh issue' : 'Syncing market data';
  const tooltip = isHealthy
    ? 'Market data refreshes automatically in the background. No page reload needed.'
    : isIssue
      ? 'Market data could not refresh. The current board may be stale.'
      : 'Market data is updating in the background. No page reload needed.';
  const color = isHealthy ? palette.success : isIssue ? palette.danger : palette.warning;

  return (
    <Tooltip title={tooltip} arrow>
      <Stack
        component="span"
        direction="row"
        alignItems="center"
        spacing={0.75}
        aria-label={`${label}. ${tooltip}`}
        sx={{
          color,
          flexShrink: 0,
          cursor: 'default'
        }}
      >
        <Box
          component="span"
          sx={{
            width: 8,
            height: 8,
            borderRadius: radius.pill,
            bgcolor: color,
            boxShadow: isHealthy ? `0 0 0 0 ${palette.success}66` : `0 0 0 2px ${color}22`,
            animation: isHealthy ? 'live-data-pulse 1.8s ease-out infinite' : 'none',
            '@keyframes live-data-pulse': {
              '0%': { boxShadow: `0 0 0 0 ${palette.success}66` },
              '70%': { boxShadow: `0 0 0 7px ${palette.success}00` },
              '100%': { boxShadow: `0 0 0 0 ${palette.success}00` }
            }
          }}
        />
        <Typography
          component="span"
          variant="caption"
          sx={{
            color: 'inherit',
            fontWeight: 900,
            lineHeight: 1,
            whiteSpace: 'nowrap',
            letterSpacing: 0
          }}
        >
          {label}
        </Typography>
      </Stack>
    </Tooltip>
  );
}

function resolveLiveDataState(
  freshness: ReturnType<typeof useMarketFreshnessData>['data'],
  hasPollingError: boolean
): LiveDataState | null {
  if (!freshness && !hasPollingError) {
    return null;
  }

  if (hasPollingError) {
    return 'issue';
  }

  const readinessState = resolveReadinessState(freshness?.marketReadiness);
  if (readinessState) {
    return readinessState;
  }

  if (freshness?.hasUsableMarketData === false || freshness?.providerRejectedKeyDetected === true || hasProviderIssue(freshness?.providerStatus?.status, freshness?.hasUsableMarketData)) {
    return 'issue';
  }

  return 'healthy';
}

function resolveReadinessState(readiness: MarketReadinessDto | null | undefined): LiveDataState | null {
  const status = readiness?.status?.trim().toLowerCase();
  const severity = readiness?.severity?.trim().toLowerCase();

  if (!status && !severity) {
    return null;
  }

  if (severity === 'error' || status === 'failed' || status === 'providerunavailable') {
    return 'issue';
  }

  if (status === 'fresh' && (severity === undefined || severity === 'ok')) {
    return 'healthy';
  }

  if (['syncing', 'initializing', 'partial', 'stale'].includes(status ?? '') || severity === 'info' || severity === 'warning') {
    return 'syncing';
  }

  return null;
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
