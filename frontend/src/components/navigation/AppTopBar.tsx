import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import { Avatar, Box, Button, Stack, Tooltip, Typography } from '@mui/material';
import { useState } from 'react';
import { GlossaryDialog } from '../glossary/GlossaryDialog';
import type { MarketReadinessDto } from '../../api/types';
import { isDemoDataMode } from '../../config/dataMode';
import { mockUser } from '../../data/mockUser';
import { useMarketFreshnessData } from '../../hooks/useMarketData';
import { palette } from '../../theme/palette';

export function AppTopBar() {
  const [glossaryOpen, setGlossaryOpen] = useState(false);
  const freshnessQuery = useMarketFreshnessData(['app-top-bar-live-data']);
  const freshness = freshnessQuery.data;
  const liveDataState = resolveLiveDataState(freshness, Boolean(freshnessQuery.error));

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
            borderRadius: 1.5,
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
          <Avatar sx={{ width: 34, height: 34 }}>{mockUser.name[0]}</Avatar>
        </Stack>
      </Stack>
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
            borderRadius: '999px',
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
