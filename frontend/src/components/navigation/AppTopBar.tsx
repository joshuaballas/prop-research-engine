import { Avatar, Box, Stack, Tooltip, Typography } from '@mui/material';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { MarketFreshnessDto, ScheduledRefreshStatusDto } from '../../api/types';
import { isDemoDataMode } from '../../config/dataMode';
import { mockUser } from '../../data/mockUser';
import { useMarketFreshnessData, useProviderStatusData, useScheduledRefreshStatusData } from '../../hooks/useMarketData';
import { bumpMarketDataRefreshVersion } from '../../hooks/useMarketDataRefresh';

interface SyncWatchState {
  active: boolean;
  startedAt: number;
  exhausted: boolean;
}

export function AppTopBar() {
  const [now, setNow] = useState(() => Date.now());
  const [queryTick, setQueryTick] = useState(0);
  const [syncWatch, setSyncWatch] = useState<SyncWatchState | null>(null);
  const lastBroadcastRefreshMarkerRef = useRef<string | null>(null);
  const freshnessQuery = useMarketFreshnessData([queryTick]);
  const providerStatusQuery = useProviderStatusData([queryTick]);
  const scheduledRefreshQuery = useScheduledRefreshStatusData([queryTick]);
  const freshness = freshnessQuery.data;
  const providerStatus = providerStatusQuery.data;
  const scheduledRefresh = scheduledRefreshQuery.data;
  const successfulRefreshMarker = useMemo(
    () => getSuccessfulRefreshMarker(scheduledRefresh, freshness),
    [freshness, scheduledRefresh]
  );
  const syncDisplay = useMemo(
    () => buildSyncDisplay({
      now,
      scheduledRefresh,
      freshness,
      syncWatch,
      hasStatusError: Boolean(freshnessQuery.error || providerStatusQuery.error || scheduledRefreshQuery.error),
      providerRejectedKeyDetected: providerStatus?.providerRejectedKeyDetected,
    }),
    [
      freshness,
      freshnessQuery.error,
      now,
      providerStatus?.providerRejectedKeyDetected,
      providerStatusQuery.error,
      scheduledRefresh,
      scheduledRefreshQuery.error,
      syncWatch
    ]
  );

  useEffect(() => {
    const countdownTimer = window.setInterval(() => setNow(Date.now()), 1000);
    const queryTimer = window.setInterval(() => setQueryTick((value) => value + 1), 60000);

    return () => {
      window.clearInterval(countdownTimer);
      window.clearInterval(queryTimer);
    };
  }, []);

  useEffect(() => {
    if (!syncDisplay.shouldStartSyncWatch || syncWatch?.active) return;

    setSyncWatch({
      active: true,
      startedAt: now,
      exhausted: false
    });
    setQueryTick((value) => value + 1);
  }, [now, syncDisplay.shouldStartSyncWatch, syncWatch?.active]);

  useEffect(() => {
    if (!syncWatch?.active) return;

    const pollTimer = window.setInterval(() => setQueryTick((value) => value + 1), 5000);
    return () => window.clearInterval(pollTimer);
  }, [syncWatch?.active]);

  useEffect(() => {
    if (!syncWatch?.active) return;

    if (isScheduledRefreshSkipped(scheduledRefresh)) {
      setSyncWatch(null);
      return;
    }

    if (now - syncWatch.startedAt >= 60000) {
      setSyncWatch((current) => current?.active ? { ...current, active: false, exhausted: true } : current);
    }
  }, [now, scheduledRefresh, syncWatch]);

  useEffect(() => {
    if (!successfulRefreshMarker) return;

    const nextReadableAtMs = getLatestSuccessfulRefreshReadableAtMs(scheduledRefresh, freshness);
    if (syncWatch?.active && nextReadableAtMs !== null && now < nextReadableAtMs) {
      return;
    }

    if (lastBroadcastRefreshMarkerRef.current === null) {
      lastBroadcastRefreshMarkerRef.current = successfulRefreshMarker;
      if (!syncWatch?.active) return;

      bumpMarketDataRefreshVersion();
      setSyncWatch(null);
      setQueryTick((value) => value + 1);
      return;
    }

    if (lastBroadcastRefreshMarkerRef.current !== successfulRefreshMarker) {
      lastBroadcastRefreshMarkerRef.current = successfulRefreshMarker;
      bumpMarketDataRefreshVersion();
      setSyncWatch(null);
      setQueryTick((value) => value + 1);
    }
  }, [freshness, now, scheduledRefresh, successfulRefreshMarker, syncWatch?.active]);

  return (
    <Stack
      direction="row"
      alignItems="center"
      justifyContent="space-between"
      gap={2}
      sx={{ py: 1.35, px: { xs: 2, md: 3 }, borderBottom: '1px solid rgba(148,163,184,.12)', bgcolor: 'rgba(7,11,20,.76)', backdropFilter: 'blur(18px)', position: 'sticky', top: 0, zIndex: 10 }}
    >
      <Stack direction="row" alignItems="center" spacing={1.5}>
        <Box>
          <Typography fontWeight={900}>DFS research dashboard</Typography>
          <Typography variant="caption" color="text.secondary">
            Line comparison and entry building
          </Typography>
        </Box>
      </Stack>
      <Stack direction="row" spacing={{ xs: 1.25, sm: 2 }} alignItems="center" minWidth={0}>
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
  );
}

function buildSyncDisplay({
  now,
  scheduledRefresh,
  freshness,
  syncWatch,
  hasStatusError,
  providerRejectedKeyDetected,
}: {
  now: number;
  scheduledRefresh?: ScheduledRefreshStatusDto;
  freshness?: MarketFreshnessDto;
  syncWatch: SyncWatchState | null;
  hasStatusError: boolean;
  providerRejectedKeyDetected?: boolean;
}) {
  const primary = formatUpdatedAge(now, freshness);
  if (hasStatusError || providerRejectedKeyDetected) {
    return {
      primary,
      secondary: 'Provider unavailable',
      tone: 'warning' as const,
      detail: undefined,
      shouldStartSyncWatch: false
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
      detail: currentPlan?.recommendedAction ?? currentPlan?.RecommendedAction ?? undefined,
      shouldStartSyncWatch: false
    };
  }

  const enabled = scheduledRefresh?.enabled ?? scheduledRefresh?.Enabled ?? false;
  const willCallExternalApis = scheduledRefresh?.willCallExternalApis ?? scheduledRefresh?.WillCallExternalApis ?? false;
  if (!enabled || !willCallExternalApis) {
    return {
      primary,
      secondary: 'Auto-sync off',
      tone: 'default' as const,
      detail: undefined,
      shouldStartSyncWatch: false
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
      detail: currentPlan?.recommendedAction ?? currentPlan?.RecommendedAction ?? skipReason,
      shouldStartSyncWatch: false
    };
  }

  if (shouldRefresh === false && skipReason && skipReason !== 'CadenceNotDue') {
    return {
      primary,
      secondary: formatSkipReason(skipReason),
      tone: 'warning' as const,
      detail: currentPlan?.recommendedAction ?? currentPlan?.RecommendedAction ?? skipReason ?? undefined,
      shouldStartSyncWatch: false
    };
  }

  if (syncWatch?.active) {
    return {
      primary,
      secondary: 'Syncing...',
      tone: 'default' as const,
      detail: undefined,
      shouldStartSyncWatch: false
    };
  }

  if (syncWatch?.exhausted) {
    return {
      primary,
      secondary: 'Sync delayed',
      tone: 'default' as const,
      detail: 'Waiting for a completed scheduled refresh.',
      shouldStartSyncWatch: false
    };
  }

  const nextRunMs = getNextSyncMs(scheduledRefresh, freshness);
  if (nextRunMs === null) {
    return {
      primary,
      secondary: 'Checking...',
      tone: 'default' as const,
      detail: undefined,
      shouldStartSyncWatch: true
    };
  }

  const remainingMs = nextRunMs - now;
  if (remainingMs <= 0) {
    return {
      primary,
      secondary: 'Syncing...',
      tone: 'default' as const,
      detail: undefined,
      shouldStartSyncWatch: true
    };
  }

  return {
    primary,
    secondary: `Next sync in ${formatSyncCountdown(remainingMs)}`,
    tone: 'success' as const,
    detail: undefined,
    shouldStartSyncWatch: false
  };
}

function getNextSyncMs(scheduledRefresh?: ScheduledRefreshStatusDto, freshness?: MarketFreshnessDto) {
  const nextDataAvailableEstimateUtc =
    scheduledRefresh?.nextDataAvailableEstimateUtc ?? scheduledRefresh?.NextDataAvailableEstimateUtc;
  if (nextDataAvailableEstimateUtc) {
    const nextDataAvailableMs = Date.parse(nextDataAvailableEstimateUtc);
    if (!Number.isNaN(nextDataAvailableMs)) return nextDataAvailableMs;
  }

  const nextRunEstimateUtc = scheduledRefresh?.nextRunEstimateUtc ?? scheduledRefresh?.NextRunEstimateUtc;
  if (nextRunEstimateUtc) {
    const nextRunMs = Date.parse(nextRunEstimateUtc);
    if (!Number.isNaN(nextRunMs)) return nextRunMs;
  }

  const effectiveIntervalMinutes = readNumber(scheduledRefresh, 'effectiveIntervalMinutes', 'EffectiveIntervalMinutes');
  if (!effectiveIntervalMinutes) return null;

  const completedAt = getLatestSuccessfulRefreshCompletedAt(scheduledRefresh, freshness);
  if (!completedAt) return null;

  const completedAtMs = Date.parse(completedAt);
  if (Number.isNaN(completedAtMs)) return null;

  const postRefreshReadBufferSeconds = getPostRefreshReadBufferSeconds(scheduledRefresh);
  return completedAtMs + effectiveIntervalMinutes * 60000 + postRefreshReadBufferSeconds * 1000;
}

function getLatestSuccessfulRefreshReadableAtMs(scheduledRefresh?: ScheduledRefreshStatusDto, freshness?: MarketFreshnessDto) {
  const completedAt = getLatestSuccessfulRefreshCompletedAt(scheduledRefresh, freshness);
  if (!completedAt) return null;

  const completedAtMs = Date.parse(completedAt);
  if (Number.isNaN(completedAtMs)) return null;

  return completedAtMs + getPostRefreshReadBufferSeconds(scheduledRefresh) * 1000;
}

function getLatestSuccessfulRefreshCompletedAt(scheduledRefresh?: ScheduledRefreshStatusDto, freshness?: MarketFreshnessDto) {
  return (
    scheduledRefresh?.latestSuccessfulRefreshCompletedAtUtc ??
    scheduledRefresh?.LatestSuccessfulRefreshCompletedAtUtc ??
    getRefreshCompletedAt(scheduledRefresh?.latestSuccessfulRefresh ?? scheduledRefresh?.LatestSuccessfulRefresh) ??
    getRefreshCompletedAt(freshness?.latestSuccessfulRefresh ?? freshness?.LatestSuccessfulRefresh) ??
    null
  );
}

function getSuccessfulRefreshMarker(scheduledRefresh?: ScheduledRefreshStatusDto, freshness?: MarketFreshnessDto) {
  const latestRefreshRunId = readString(
    scheduledRefresh,
    'latestSuccessfulRefreshRunId',
    'LatestSuccessfulRefreshRunId',
    'latestRefreshRunId',
    'LatestRefreshRunId'
  );
  const latestSuccessfulRefresh = scheduledRefresh?.latestSuccessfulRefresh ?? scheduledRefresh?.LatestSuccessfulRefresh;
  const latestSuccessfulRefreshId = latestRefreshRunId ?? latestSuccessfulRefresh?.id ?? latestSuccessfulRefresh?.Id ?? null;
  const latestSuccessfulCompletedAt = getLatestSuccessfulRefreshCompletedAt(scheduledRefresh, freshness);

  if (latestSuccessfulRefreshId || latestSuccessfulCompletedAt) {
    return `${latestSuccessfulRefreshId ?? 'latest-success'}|${latestSuccessfulCompletedAt ?? 'no-completed-at'}`;
  }

  const lastRunSucceeded = readBoolean(scheduledRefresh, 'lastRunSucceeded', 'LastRunSucceeded');
  const lastRunCompletedAt = scheduledRefresh?.lastRunCompletedAtUtc ?? scheduledRefresh?.LastRunCompletedAtUtc ?? null;
  if (lastRunSucceeded && lastRunCompletedAt) {
    return `last-run-success|${lastRunCompletedAt}`;
  }

  return null;
}

function isScheduledRefreshSkipped(scheduledRefresh?: ScheduledRefreshStatusDto) {
  if (!scheduledRefresh) return false;
  const status = readString(scheduledRefresh, 'status', 'Status')?.toLowerCase();
  const lastRunSkipped = readBoolean(scheduledRefresh, 'lastRunSkipped', 'LastRunSkipped');
  const skipReason = readString(scheduledRefresh, 'lastSkipReason', 'LastSkipReason', 'lastPlanSkipReason', 'LastPlanSkipReason');
  if (skipReason === 'CadenceNotDue') return false;
  return status === 'skipped' || lastRunSkipped === true;
}

function formatSyncCountdown(diffMs: number) {
  const clampedDiffMs = Math.max(0, diffMs);
  const totalSeconds = Math.ceil(clampedDiffMs / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}

function getRefreshCompletedAt(refresh?: { completedAtUtc?: string | null; CompletedAtUtc?: string | null; startedAtUtc?: string | null; StartedAtUtc?: string | null } | null) {
  return refresh?.completedAtUtc ?? refresh?.CompletedAtUtc ?? refresh?.startedAtUtc ?? refresh?.StartedAtUtc ?? null;
}

function formatUpdatedAge(now: number, freshness?: MarketFreshnessDto) {
  const completedAt =
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

function readNumber(record: object | undefined, ...keys: string[]) {
  if (!record) return undefined;
  const source = record as Record<string, unknown>;
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    if (typeof value === 'string' && value.trim() && Number.isFinite(Number(value))) return Number(value);
  }
  return undefined;
}

function getPostRefreshReadBufferSeconds(scheduledRefresh?: ScheduledRefreshStatusDto) {
  return readNumber(scheduledRefresh, 'postRefreshReadBufferSeconds', 'PostRefreshReadBufferSeconds') ?? 30;
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
