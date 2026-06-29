import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import { Avatar, Button, Stack, Tooltip, Typography } from '@mui/material';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { MarketFreshnessDto, ScheduledRefreshStatusDto } from '../../api/types';
import { GlossaryDialog } from '../glossary/GlossaryDialog';
import { isDemoDataMode } from '../../config/dataMode';
import { mockUser } from '../../data/mockUser';
import { useMarketFreshnessData, useProviderStatusData, useScheduledRefreshStatusData } from '../../hooks/useMarketData';
import { bumpMarketDataRefreshVersion } from '../../hooks/useMarketDataRefresh';

export function AppTopBar() {
  const [now, setNow] = useState(() => Date.now());
  const [marketStatusQueryTick, setMarketStatusQueryTick] = useState(0);
  const [scheduledStatusQueryTick, setScheduledStatusQueryTick] = useState(0);
  const [glossaryOpen, setGlossaryOpen] = useState(false);
  const lastObservedSuccessfulRefreshRef = useRef<string | null | undefined>(undefined);
  const lastZeroRefetchTargetMsRef = useRef<number | null>(null);
  const freshnessQuery = useMarketFreshnessData([marketStatusQueryTick]);
  const providerStatusQuery = useProviderStatusData([marketStatusQueryTick]);
  const scheduledRefreshQuery = useScheduledRefreshStatusData([scheduledStatusQueryTick]);
  const freshness = freshnessQuery.data;
  const providerStatus = providerStatusQuery.data;
  const scheduledRefresh = scheduledRefreshQuery.data;
  const countdownTargetMs = useMemo(
    () => getCountdownTargetMs(scheduledRefresh),
    [scheduledRefresh]
  );
  const serverClientOffsetMs = useMemo(
    () => getServerClientOffsetMs(scheduledRefresh),
    [scheduledRefresh]
  );
  const estimatedServerNowMs = now + serverClientOffsetMs;
  const isRefreshInProgress = useMemo(
    () => isScheduledRefreshInProgress(scheduledRefresh),
    [scheduledRefresh]
  );
  const shouldPollScheduledStatus =
    isRefreshInProgress ||
    (countdownTargetMs !== null && estimatedServerNowMs >= countdownTargetMs);
  const latestScheduledSuccessfulRefreshCompletedAt = useMemo(
    () => getLatestSuccessfulRefreshCompletedAt(scheduledRefresh, undefined),
    [scheduledRefresh]
  );
  const syncDisplay = useMemo(
    () => buildSyncDisplay({
      now: estimatedServerNowMs,
      scheduledRefresh,
      countdownTargetMs,
      freshness,
      hasStatusError: Boolean(freshnessQuery.error || providerStatusQuery.error || scheduledRefreshQuery.error),
      providerRejectedKeyDetected: providerStatus?.providerRejectedKeyDetected,
    }),
    [
      freshness,
      freshnessQuery.error,
      estimatedServerNowMs,
      providerStatus?.providerRejectedKeyDetected,
      providerStatusQuery.error,
      countdownTargetMs,
      scheduledRefresh,
      scheduledRefreshQuery.error
    ]
  );

  useEffect(() => {
    const countdownTimer = window.setInterval(() => setNow(Date.now()), 1000);

    return () => {
      window.clearInterval(countdownTimer);
    };
  }, []);

  useEffect(() => {
    const statusPollTimer = window.setInterval(() => {
      setScheduledStatusQueryTick((value) => value + 1);
    }, 30000);

    return () => {
      window.clearInterval(statusPollTimer);
    };
  }, []);

  useEffect(() => {
    if (!shouldPollScheduledStatus) return;

    setScheduledStatusQueryTick((value) => value + 1);
    const statusPollTimer = window.setInterval(() => {
      setScheduledStatusQueryTick((value) => value + 1);
    }, 7000);

    return () => {
      window.clearInterval(statusPollTimer);
    };
  }, [shouldPollScheduledStatus]);

  useEffect(() => {
    if (countdownTargetMs === null) return;

    if (estimatedServerNowMs < countdownTargetMs) {
      lastZeroRefetchTargetMsRef.current = null;
      return;
    }

    if (lastZeroRefetchTargetMsRef.current === countdownTargetMs) return;

    lastZeroRefetchTargetMsRef.current = countdownTargetMs;
    setScheduledStatusQueryTick((value) => value + 1);
  }, [countdownTargetMs, estimatedServerNowMs]);

  useEffect(() => {
    const refetchScheduledStatus = () => {
      lastZeroRefetchTargetMsRef.current = null;
      setNow(Date.now());
      setScheduledStatusQueryTick((value) => value + 1);
    };

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        refetchScheduledStatus();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    window.addEventListener('focus', refetchScheduledStatus);
    window.addEventListener('pageshow', refetchScheduledStatus);

    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange);
      window.removeEventListener('focus', refetchScheduledStatus);
      window.removeEventListener('pageshow', refetchScheduledStatus);
    };
  }, []);

  useEffect(() => {
    if (!latestScheduledSuccessfulRefreshCompletedAt) return;

    if (lastObservedSuccessfulRefreshRef.current === undefined) {
      lastObservedSuccessfulRefreshRef.current = latestScheduledSuccessfulRefreshCompletedAt;
      return;
    }

    if (lastObservedSuccessfulRefreshRef.current === latestScheduledSuccessfulRefreshCompletedAt) return;

    lastObservedSuccessfulRefreshRef.current = latestScheduledSuccessfulRefreshCompletedAt;
    setMarketStatusQueryTick((value) => value + 1);
    bumpMarketDataRefreshVersion();
  }, [latestScheduledSuccessfulRefreshCompletedAt]);

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
          <Tooltip title={`${syncDisplay.primary} - ${syncDisplay.secondary}`} arrow>
            <Stack
              spacing={0.2}
              alignItems="flex-end"
              sx={{
                display: { xs: 'flex', sm: 'none' },
                minWidth: 0,
                maxWidth: 'min(45vw, 190px)',
                textAlign: 'right'
              }}
            >
              <Typography
                variant="caption"
                noWrap
                color="text.secondary"
                sx={{
                  maxWidth: '100%',
                  fontSize: 11,
                  fontWeight: 750,
                  lineHeight: 1.05
                }}
              >
                {formatMobileUpdatedAge(syncDisplay.primary)}
              </Typography>
              <Typography
                variant="caption"
                noWrap
                sx={{
                  maxWidth: '100%',
                  color: syncDisplay.tone === 'success' ? 'success.main' : syncDisplay.tone === 'warning' ? 'warning.main' : 'text.secondary',
                  fontVariantNumeric: 'tabular-nums',
                  fontSize: 12,
                  fontWeight: 900,
                  lineHeight: 1.05
                }}
              >
                {formatMobileSyncStatus(syncDisplay.secondary)}
              </Typography>
            </Stack>
          </Tooltip>
          <Stack spacing={0.1} alignItems="flex-end" sx={{ display: { xs: 'none', sm: 'flex' } }}>
            <Stack direction="row" spacing={1} alignItems="center">
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
              <Typography variant="caption" color="text.secondary">
                {syncDisplay.primary}
              </Typography>
            </Stack>
            <Tooltip title={syncDisplay.detail ?? ''} disableHoverListener={!syncDisplay.detail} arrow>
              <Typography
                color={syncDisplay.tone === 'success' ? 'success.main' : syncDisplay.tone === 'warning' ? 'warning.main' : 'text.secondary'}
                sx={{ fontVariantNumeric: 'tabular-nums', fontWeight: 900, lineHeight: 1 }}
              >
                {syncDisplay.secondary}
              </Typography>
            </Tooltip>
          </Stack>
          <Avatar sx={{ width: 34, height: 34 }}>{mockUser.name[0]}</Avatar>
        </Stack>
      </Stack>
      <GlossaryDialog open={glossaryOpen} onClose={() => setGlossaryOpen(false)} />
    </>
  );
}

function buildSyncDisplay({
  now,
  scheduledRefresh,
  countdownTargetMs,
  freshness,
  hasStatusError,
  providerRejectedKeyDetected,
}: {
  now: number;
  scheduledRefresh?: ScheduledRefreshStatusDto;
  countdownTargetMs: number | null;
  freshness?: MarketFreshnessDto;
  hasStatusError: boolean;
  providerRejectedKeyDetected?: boolean;
}) {
  const primary = formatUpdatedAge(now, scheduledRefresh, freshness);
  if (hasStatusError || providerRejectedKeyDetected) {
    return {
      primary,
      secondary: 'Provider unavailable',
      tone: 'warning' as const,
      detail: undefined
    };
  }

  const currentPlan = scheduledRefresh?.currentPlan ?? scheduledRefresh?.CurrentPlan;
  const planProviderAvailable = currentPlan?.providerAvailable ?? currentPlan?.ProviderAvailable;
  const planProviderRejected = currentPlan?.providerRejectedKeyDetected ?? currentPlan?.ProviderRejectedKeyDetected;
  if (planProviderAvailable === false || planProviderRejected) {
    return {
      primary,
      secondary: 'Provider unavailable',
      tone: 'warning' as const,
      detail: currentPlan?.recommendedAction ?? currentPlan?.RecommendedAction ?? undefined
    };
  }

  const enabled = scheduledRefresh?.enabled ?? scheduledRefresh?.Enabled ?? false;
  const allowScheduledLiveRefresh = scheduledRefresh?.allowScheduledLiveRefresh ?? scheduledRefresh?.AllowScheduledLiveRefresh ?? false;
  if (!enabled || !allowScheduledLiveRefresh) {
    return {
      primary,
      secondary: 'Auto-sync off',
      tone: 'default' as const,
      detail: undefined
    };
  }

  const shouldRefresh = currentPlan?.shouldRefresh ?? currentPlan?.ShouldRefresh;
  const skipReason =
    currentPlan?.skipReason ??
    currentPlan?.SkipReason ??
    scheduledRefresh?.lastPlanSkipReason ??
    scheduledRefresh?.LastPlanSkipReason ??
    scheduledRefresh?.lastSkipReason ??
    scheduledRefresh?.LastSkipReason;

  if (skipReason === 'NoPersistedSlateEvents') {
    return {
      primary,
      secondary: 'No enabled refresh targets',
      tone: 'default' as const,
      detail: currentPlan?.recommendedAction ?? currentPlan?.RecommendedAction ?? skipReason
    };
  }

  if (shouldRefresh === false && skipReason && skipReason !== 'CadenceNotDue') {
    return {
      primary,
      secondary: formatSkipReason(skipReason),
      tone: 'warning' as const,
      detail: currentPlan?.recommendedAction ?? currentPlan?.RecommendedAction ?? skipReason ?? undefined
    };
  }

  const nextRunMs = countdownTargetMs;
  if (nextRunMs === null) {
    return {
      primary,
      secondary: 'Checking...',
      tone: 'default' as const,
      detail: undefined
    };
  }

  const isOverdue = readBoolean(scheduledRefresh, 'isOverdue', 'IsOverdue') === true;
  const remainingMs = nextRunMs - now;

  return {
    primary,
    secondary: `Next sync in ${formatSyncCountdown(remainingMs)}`,
    tone: isOverdue ? 'warning' as const : 'success' as const,
    detail: isOverdue ? 'The backend expected data to be available already and has not reported a completed refresh.' : undefined
  };
}

export function isScheduledRefreshInProgress(scheduledRefresh?: ScheduledRefreshStatusDto) {
  return readBoolean(scheduledRefresh, 'isRefreshInProgress', 'IsRefreshInProgress') === true;
}

function getCountdownTargetMs(scheduledRefresh?: ScheduledRefreshStatusDto) {
  const nextHeaderSyncTargetUtc =
    scheduledRefresh?.nextHeaderSyncTargetUtc ?? scheduledRefresh?.NextHeaderSyncTargetUtc;
  if (nextHeaderSyncTargetUtc) {
    const nextHeaderSyncTargetMs = Date.parse(nextHeaderSyncTargetUtc);
    if (!Number.isNaN(nextHeaderSyncTargetMs)) return nextHeaderSyncTargetMs;
  }

  const nextDataAvailableEstimateUtc =
    scheduledRefresh?.nextDataAvailableEstimateUtc ?? scheduledRefresh?.NextDataAvailableEstimateUtc;
  if (nextDataAvailableEstimateUtc) {
    const nextDataAvailableMs = Date.parse(nextDataAvailableEstimateUtc);
    if (!Number.isNaN(nextDataAvailableMs)) return nextDataAvailableMs;
  }

  const nextRefreshAtUtc = scheduledRefresh?.nextRefreshAtUtc ?? scheduledRefresh?.NextRefreshAtUtc;
  if (nextRefreshAtUtc) {
    const nextRefreshAtMs = Date.parse(nextRefreshAtUtc);
    if (!Number.isNaN(nextRefreshAtMs)) return nextRefreshAtMs;
  }

  return null;
}

function getServerClientOffsetMs(scheduledRefresh?: ScheduledRefreshStatusDto) {
  const serverTimestampUtc =
    readString(scheduledRefresh, 'generatedAtUtc', 'GeneratedAtUtc', 'nowUtc', 'NowUtc') ??
    readString(scheduledRefresh?.currentPlan ?? scheduledRefresh?.CurrentPlan, 'generatedAtUtc', 'GeneratedAtUtc');
  if (!serverTimestampUtc) return 0;

  const serverTimestampMs = Date.parse(serverTimestampUtc);
  return Number.isNaN(serverTimestampMs) ? 0 : serverTimestampMs - Date.now();
}

function getLatestSuccessfulRefreshCompletedAt(scheduledRefresh?: ScheduledRefreshStatusDto, freshness?: MarketFreshnessDto) {
  return getMostRecentUtc([
    scheduledRefresh?.lastSuccessfulRefreshCompletedAtUtc,
    scheduledRefresh?.LastSuccessfulRefreshCompletedAtUtc,
    scheduledRefresh?.latestSuccessfulRefreshCompletedAtUtc,
    scheduledRefresh?.LatestSuccessfulRefreshCompletedAtUtc,
    getRefreshCompletedAt(scheduledRefresh?.latestSuccessfulRefresh ?? scheduledRefresh?.LatestSuccessfulRefresh),
    getLatestTargetSuccessfulRefreshCompletedAt(scheduledRefresh),
    getRefreshCompletedAt(freshness?.latestSuccessfulRefresh ?? freshness?.LatestSuccessfulRefresh)
  ]);
}

function formatSyncCountdown(diffMs: number) {
  const clampedDiffMs = Math.max(0, diffMs);
  const totalSeconds = Math.ceil(clampedDiffMs / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}

function formatMobileSyncStatus(status: string) {
  if (isDemoDataMode) return 'Demo data';
  if (status.startsWith('Next sync in ') || status.startsWith('Next refresh in ')) return status;
  if (status === 'Checking...') return 'Syncing...';
  if (status === 'Auto-sync off') return 'Auto off';
  if (status === 'No enabled refresh targets') return 'No targets';
  return status;
}

function formatMobileUpdatedAge(status: string) {
  return status.replace(/^Updated (\d+)m ago$/, 'Updated $1m ago');
}

function getRefreshCompletedAt(refresh?: { completedAtUtc?: string | null; CompletedAtUtc?: string | null; startedAtUtc?: string | null; StartedAtUtc?: string | null } | null) {
  return refresh?.completedAtUtc ?? refresh?.CompletedAtUtc ?? refresh?.startedAtUtc ?? refresh?.StartedAtUtc ?? null;
}

function getLatestTargetSuccessfulRefreshCompletedAt(scheduledRefresh?: ScheduledRefreshStatusDto) {
  const targets = readArray(scheduledRefresh, 'targets', 'Targets');
  const targetTimes = targets.map((target) => {
    const latestSuccessfulCompletedAt = readString(target, 'latestSuccessfulRefreshCompletedAtUtc', 'LatestSuccessfulRefreshCompletedAtUtc');
    if (latestSuccessfulCompletedAt) return latestSuccessfulCompletedAt;

    const latestRunSucceeded = readBoolean(target, 'lastRunSucceeded', 'LatestRunSucceeded', 'latestRunSucceeded');
    if (!latestRunSucceeded) return null;

    return readString(target, 'lastRunCompletedAtUtc', 'LatestRunCompletedAtUtc', 'latestRunCompletedAtUtc');
  });

  return getMostRecentUtc(targetTimes);
}

function getMostRecentUtc(values: Array<string | null | undefined>) {
  let latestValue: string | null = null;
  let latestMs = Number.NEGATIVE_INFINITY;

  for (const value of values) {
    if (!value) continue;

    const parsedMs = Date.parse(value);
    if (Number.isNaN(parsedMs) || parsedMs < latestMs) continue;

    latestMs = parsedMs;
    latestValue = value;
  }

  return latestValue;
}

function formatUpdatedAge(now: number, scheduledRefresh?: ScheduledRefreshStatusDto, freshness?: MarketFreshnessDto) {
  const completedAt =
    getLatestSuccessfulRefreshCompletedAt(scheduledRefresh, undefined) ??
    getRefreshCompletedAt(freshness?.latestSuccessfulRefresh ?? freshness?.LatestSuccessfulRefresh) ??
    freshness?.generatedAtUtc;
  if (!completedAt) return 'No market sync yet';

  const completedAtMs = Date.parse(completedAt);
  if (Number.isNaN(completedAtMs)) return 'No market sync yet';

  const totalMinutes = Math.max(0, Math.floor((now - completedAtMs) / 60000));
  if (totalMinutes < 1) return 'Updated just now';
  if (totalMinutes < 60) return `Updated ${totalMinutes}m ago`;

  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return minutes ? `Updated ${hours}h ${minutes}m ago` : `Updated ${hours}h ago`;
}

function formatSkipReason(reason?: string | null) {
  switch (reason) {
    case 'StopAfterEventStart':
      return 'Auto-sync paused';
    case 'CadenceNotDue':
      return 'Next sync pending';
    case 'BudgetBlocked':
    case 'CreditBudgetExceeded':
      return 'Budget paused';
    case 'ProviderUnavailable':
      return 'Provider unavailable';
    case 'NoActiveSlate':
    case 'NoEvents':
    case 'NoPersistedSlateEvents':
      return 'No active slate';
    default:
      return 'Sync paused';
  }
}

function readString(record: object | undefined, ...keys: string[]) {
  if (!record) return undefined;
  const source = record as Record<string, unknown>;
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string' && value.trim()) return value;
  }
  return undefined;
}

function readArray(record: object | undefined, ...keys: string[]) {
  if (!record) return [];
  const source = record as Record<string, unknown>;
  for (const key of keys) {
    const value = source[key];
    if (Array.isArray(value)) return value.filter((item): item is Record<string, unknown> => Boolean(item) && typeof item === 'object') as Record<string, unknown>[];
  }
  return [];
}

function readBoolean(record: object | undefined, ...keys: string[]) {
  if (!record) return undefined;
  const source = record as Record<string, unknown>;
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'boolean') return value;
  }
  return undefined;
}
