import { Avatar, Box, Stack, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import type { MarketFreshnessDto, ScheduledRefreshStatusDto } from '../../api/types';
import { isDemoDataMode } from '../../config/dataMode';
import { mockUser } from '../../data/mockUser';
import { useMarketFreshnessData, useProviderStatusData, useScheduledRefreshStatusData } from '../../hooks/useMarketData';

export function AppTopBar() {
  const [now, setNow] = useState(() => Date.now());
  const [queryTick, setQueryTick] = useState(0);
  const freshnessQuery = useMarketFreshnessData([queryTick]);
  const providerStatusQuery = useProviderStatusData([queryTick]);
  const scheduledRefreshQuery = useScheduledRefreshStatusData([queryTick]);
  const freshness = freshnessQuery.data;
  const providerStatus = providerStatusQuery.data;
  const scheduledRefresh = scheduledRefreshQuery.data;
  const countdownText = useMemo(
    () => buildCadenceCountdownText({
      now,
      scheduledRefresh,
      freshness,
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
      scheduledRefreshQuery.error
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
              Next Sync In
            </Typography>
          </Stack>
          <Typography color="success.main" sx={{ fontVariantNumeric: 'tabular-nums', fontWeight: 900, lineHeight: 1 }}>
            {countdownText}
          </Typography>
        </Stack>
        <Avatar sx={{ width: 34, height: 34 }}>{mockUser.name[0]}</Avatar>
      </Stack>
    </Stack>
  );
}

function buildCadenceCountdownText({
  now,
  scheduledRefresh,
  freshness,
  hasStatusError,
  providerRejectedKeyDetected,
}: {
  now: number;
  scheduledRefresh?: ScheduledRefreshStatusDto;
  freshness?: MarketFreshnessDto;
  hasStatusError: boolean;
  providerRejectedKeyDetected?: boolean;
}) {
  if (hasStatusError || providerRejectedKeyDetected) return formatSyncCountdown(0);

  const currentPlan = scheduledRefresh?.currentPlan ?? scheduledRefresh?.CurrentPlan;
  const planProviderAvailable = currentPlan?.providerAvailable ?? currentPlan?.ProviderAvailable;
  const planProviderRejected = currentPlan?.providerRejectedKeyDetected ?? currentPlan?.ProviderRejectedKeyDetected;
  if (planProviderAvailable === false || planProviderRejected) return formatSyncCountdown(0);

  const enabled = scheduledRefresh?.enabled ?? scheduledRefresh?.Enabled ?? false;
  const willCallExternalApis = scheduledRefresh?.willCallExternalApis ?? scheduledRefresh?.WillCallExternalApis ?? false;
  if (!enabled || !willCallExternalApis) return formatSyncCountdown(0);

  const intervalMinutes = scheduledRefresh?.effectiveIntervalMinutes ?? scheduledRefresh?.EffectiveIntervalMinutes;
  const latestSuccessfulRefreshCompletedAtUtc =
    scheduledRefresh?.latestSuccessfulRefreshCompletedAtUtc ??
    scheduledRefresh?.LatestSuccessfulRefreshCompletedAtUtc ??
    getRefreshCompletedAt(scheduledRefresh?.latestSuccessfulRefresh ?? scheduledRefresh?.LatestSuccessfulRefresh) ??
    getRefreshCompletedAt(freshness?.latestSuccessfulRefresh ?? freshness?.LatestSuccessfulRefresh);

  if (!intervalMinutes || !latestSuccessfulRefreshCompletedAtUtc) return formatSyncCountdown(0);

  const completedAtMs = Date.parse(latestSuccessfulRefreshCompletedAtUtc);
  if (Number.isNaN(completedAtMs)) return formatSyncCountdown(0);

  return formatCadenceCountdown(now - completedAtMs, intervalMinutes);
}

function formatSyncCountdown(diffMs: number) {
  const clampedDiffMs = Math.max(0, diffMs);
  const totalSeconds = Math.ceil(clampedDiffMs / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}

function formatCadenceCountdown(elapsedMs: number, intervalMinutes: number) {
  const intervalMs = Math.max(1, intervalMinutes) * 60 * 1000;
  const elapsedWithinInterval = ((Math.max(0, elapsedMs) % intervalMs) + intervalMs) % intervalMs;
  const remainingMs = elapsedWithinInterval === 0 ? intervalMs : intervalMs - elapsedWithinInterval;

  return formatSyncCountdown(remainingMs);
}

function getRefreshCompletedAt(refresh?: { completedAtUtc?: string | null; CompletedAtUtc?: string | null; startedAtUtc?: string | null; StartedAtUtc?: string | null } | null) {
  return refresh?.completedAtUtc ?? refresh?.CompletedAtUtc ?? refresh?.startedAtUtc ?? refresh?.StartedAtUtc ?? null;
}
