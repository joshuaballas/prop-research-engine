import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined';
import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import InsightsOutlinedIcon from '@mui/icons-material/InsightsOutlined';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import ShoppingCartOutlinedIcon from '@mui/icons-material/ShoppingCartOutlined';
import { Box, Button, Divider, Grid, Stack, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { ProjectionModifierBadge } from '../../components/markets/ProjectionModifierBadge';
import { TrustValue } from '../../components/metrics/TrustValue';
import { SportToggleRail } from '../../components/navigation/SportToggleRail';
import { PropDetailsModal } from '../../components/prop-history/PropDetailsModal';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { AppCard } from '../../components/ui/AppCard';
import { DataStateBanner } from '../../components/ui/DataStateBanner';
import { MetadataText } from '../../components/ui/MetadataText';
import { MetricCard } from '../../components/ui/MetricCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { StatusPill } from '../../components/ui/StatusPill';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../../components/ui/buttonStyles';
import { mockSports } from '../../data/mockSports';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { useLineShoppingData, useMarketFreshnessData, usePerformanceData, useTopPicksData } from '../../hooks/useMarketData';
import { useSelectedSports } from '../../hooks/useSelectedSports';
import type { MarketOpportunity } from '../../types/markets';
import type { SportCode } from '../../types/sports';
import { formatPercent } from '../../utils/formatters';
import { hasRenderableWarning, renderWarningText } from '../../utils/warnings';

export function DashboardPage() {
  const sports = useSelectedSports();
  const builder = useEntryBuilder();
  const freshnessQuery = useMarketFreshnessData();
  const topPicksQuery = useTopPicksData(10, 'dashboard-top-pick');
  const lineShoppingQuery = useLineShoppingData(12, true, 'dashboard-line-shopping');
  const performanceQuery = usePerformanceData('latestCompleted');
  const topPicks = topPicksQuery.markets;
  const lineShoppingMarkets = lineShoppingQuery.markets;
  const performanceRows = performanceQuery.rows;
  const performanceAvailability = performanceQuery.summary?.dataAvailability;
  const availableSportCodes = useMemo(() => getAvailableSportCodes([...topPicks, ...lineShoppingMarkets]), [lineShoppingMarkets, topPicks]);
  const dashboardSports = useMemo(() => getDashboardSportOptions(availableSportCodes), [availableSportCodes]);
  const filtered = topPicks.filter((market) => sports.includesSport(market.sport));
  const filteredMarkets = lineShoppingMarkets.filter((market) => sports.includesSport(market.sport));
  const activeDfs = filteredMarkets.filter((market) => market.providerCategory === 'DFS' && market.freshness === 'Fresh');
  const entryProvider = builder.lockedProviderName ?? 'No provider selected';
  const entryLegs = builder.legs;
  const compatibleSports = Array.from(new Set(entryLegs.map((market) => market.sport)));
  const overall = performanceRows.find((item) => item.groupType === 'Overall') ?? performanceRows[0];
  const bestStrategy = performanceRows.find((item) => item.groupType === 'Strategy');
  const limitedHistoryCount = performanceRows.filter((item) => hasRenderableWarning(item.warning) || item.gradedPicks < 30).length;
  const latestCompletedDateLabel = formatDateShort(performanceAvailability?.latestCompletedDate ?? performanceAvailability?.latestGradedDate);
  const latestImportedResultsDateLabel = formatDateShort(performanceAvailability?.latestResultsImportDate);
  const returnedTopPicks = topPicks.length;
  const bestEvMarket = filtered.reduce<MarketOpportunity | undefined>(
    (best, market) => (!best || market.estimatedEv > best.estimatedEv ? market : best),
    undefined
  );
  const freshness = freshnessQuery.data;
  const dataStatus = freshness?.hasUsableMarketData ? 'Fresh' : freshness?.isStale ? 'Stale' : 'No data';
  const isDataHealthy = !freshnessQuery.error && freshness?.hasUsableMarketData && !freshness?.isStale;
  const hasGradedPerformance = performanceAvailability?.hasGradedData ?? Boolean(overall && overall.gradedPicks > 0);
  const performanceIsSparse = !hasGradedPerformance || !overall || (overall.gradedPicks ?? 0) <= 0;

  useEffect(() => {
    const selectedSpecificSports = sports.selectedSports.filter((sport): sport is Exclude<SportCode, 'ALL'> => sport !== 'ALL');
    if (!selectedSpecificSports.length) return;
    if (selectedSpecificSports.some((sport) => !availableSportCodes.has(sport))) {
      sports.toggleSport('ALL');
    }
  }, [availableSportCodes, sports.selectedSports, sports.toggleSport]);

  return (
    <Stack spacing={{ xs: 2.5, md: 3 }}>
      <PageHeader
        title="Dashboard"
        description="Current DFS opportunities, provider-compatible entry state, reference lines, and recent performance."
      />

      <SportToggleRail
        selectedSports={sports.selectedSports}
        onToggle={sports.toggleSport}
        compact
        hideStatuses
        sports={dashboardSports}
      />

      {!isDataHealthy ? (
        <DataStateBanner
          title={freshnessQuery.error ? 'Market data is unavailable' : freshness?.hasUsableMarketData ? 'Market data needs attention' : 'No usable market data yet'}
          message={freshnessQuery.error ? 'The read-only market API could not be reached.' : freshness?.usabilityWarning ?? 'Refresh or provider status may be limiting current market reads.'}
          status={dataStatus}
        />
      ) : null}

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="Top Picks" value={`${returnedTopPicks}`} helper="Current opportunities" />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="Best EV" value={formatPercent(bestEvMarket?.estimatedEv ?? 0, { signed: true })} helper={bestEvMarket?.player ?? 'No active row'} />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="DFS Providers" value={`${new Set(activeDfs.map((market) => market.provider)).size}`} helper="Live providers" />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="Entry Builder" value={`${entryLegs.length} legs`} helper={entryProvider} />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard
            label="Graded picks"
            value={hasGradedPerformance && overall ? `${overall.gradedPicks}` : '-'}
            helper={latestCompletedDateLabel ? `Latest completed slate: ${latestCompletedDateLabel}` : latestImportedResultsDateLabel ? `Latest imported results: ${latestImportedResultsDateLabel}` : 'Awaiting imported results'}
          />
        </Grid>
      </Grid>

      <SectionHeader
        title="Top opportunities"
        description="A quick view of the best current DFS rows without the full research table."
        action={
          <Button component={RouterLink} to="/app/top-picks" variant="outlined">
            View all picks
          </Button>
        }
      />
      <Grid container spacing={2}>
        {topPicksQuery.loading ? (
          <Grid size={12}>
            <ApiStateCard message="Loading top opportunities..." />
          </Grid>
        ) : topPicksQuery.error ? (
          <Grid size={12}>
            <ApiStateCard tone="error" message="Top opportunities could not be loaded." />
          </Grid>
        ) : filtered.length ? filtered.slice(0, 3).map((market) => (
          <Grid key={market.id} size={{ xs: 12, lg: 4 }}>
            <DashboardOpportunityCard market={market} builder={builder} />
          </Grid>
        )) : (
          <Grid size={12}>
            <ApiStateCard message="No top opportunities are available for the selected sports." />
          </Grid>
        )}
      </Grid>

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, lg: 7 }}>
          <SectionHeader title="Provider line preview" description="Best available provider-backed lines from current top opportunities." />
          <Box mt={2}>
            <Stack spacing={1.2}>
              {lineShoppingQuery.loading ? (
                <ApiStateCard message="Loading line-shopping preview..." />
              ) : lineShoppingQuery.error ? (
                <ApiStateCard tone="error" message="Line-shopping preview could not be loaded." />
              ) : filteredMarkets.length ? (
                <LineShoppingPreview markets={filteredMarkets.slice(0, 3)} />
              ) : (
                <ApiStateCard message="No line-shopping rows are available for the selected sports." />
              )}
            </Stack>
          </Box>
        </Grid>

        <Grid size={{ xs: 12, lg: 5 }}>
          <SectionHeader title="Entry builder preview" description="Provider-locked entries with sport-flexible review." />
          <Box mt={2}>
            <EntryBuilderPreview provider={entryProvider} legs={entryLegs} sports={compatibleSports} />
          </Box>
        </Grid>
      </Grid>

      <SectionHeader
        title="Completed slate performance"
        description="Reviews the latest imported result date and grades captured picks when matches are available."
        action={!performanceIsSparse ? (
          <Button component={RouterLink} to="/app/performance" variant="outlined">
            View performance
          </Button>
        ) : undefined}
      />
      {performanceIsSparse ? (
        <SparsePerformancePanel availability={performanceAvailability} />
      ) : (
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 4 }}>
          <PerformancePanel
            icon={<InsightsOutlinedIcon />}
            title={latestCompletedDateLabel ? `Latest completed slate: ${latestCompletedDateLabel}` : 'Recent performance'}
            value={formatPercent(overall?.hitRate ?? 0)}
            detail={overall ? `${overall.wins}-${overall.losses}-${overall.pushes} across ${overall.gradedPicks} graded picks` : 'No graded picks yet'}
          />
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <PerformancePanel icon={<AutoGraphOutlinedIcon />} title="Best strategy" value={bestStrategy ? formatPercent(bestStrategy.roi, { signed: true }) : '-'} detail={bestStrategy ? `${bestStrategy.groupKey} Â· ${bestStrategy.gradedPicks} graded picks` : 'No graded strategy yet'} />
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <PerformancePanel
            icon={<AccountTreeOutlinedIcon />}
            title="Limited history"
            value={`${limitedHistoryCount} groups`}
            detail="Review small samples before treating a slice as reliable."
            tone={limitedHistoryCount > 0 ? 'warning' : 'default'}
          />
        </Grid>
      </Grid>
      )}
    </Stack>
  );
}

function DashboardOpportunityCard({ market, builder }: { market: MarketOpportunity; builder: ReturnType<typeof useEntryBuilder> }) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const warning = renderWarningText(market.warnings);
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const canAdd = market.canAddToEntry && market.estimatedEv > 0 && !incompatible;

  return (
    <AppCard sx={{ height: '100%' }}>
      <Stack spacing={1.35}>
        <Stack direction="row" justifyContent="space-between" gap={1.5} alignItems="flex-start">
          <Box minWidth={0}>
            <Typography fontWeight={950} noWrap>
              {market.player}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {market.side} {market.propType} {market.line}
            </Typography>
          </Box>
          <EvValue value={market.estimatedEv} />
        </Stack>

        <ProviderLabel provider={market.provider} category={market.providerCategory} compact />

        <Stack direction="row" gap={0.75} flexWrap="wrap">
          <StatusPill label={market.strategyClassification} />
          <ProjectionModifierBadge modifier={market.projectionModifier} />
        </Stack>

        <Divider />

        <Grid container spacing={1.2}>
          <Grid size={{ xs: 4 }}>
            <CompactMetric label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} metric="edge" />
          </Grid>
          <Grid size={{ xs: 4 }}>
            <CompactMetric label="Hit chance" value={formatPercent(market.modelProbability)} metric="hitChance" />
          </Grid>
          <Grid size={{ xs: 4 }}>
            <CompactMetric label="Trust" value={market.trustLevel} metric="trust" />
          </Grid>
        </Grid>

        {warning ? (
          <Typography variant="body2" color="warning.main">
            {warning}
          </Typography>
        ) : getDashboardReason(market) ? (
          <Typography variant="body2" color="text.secondary">
            {getDashboardReason(market)}
          </Typography>
        ) : null}

        {market.historicalSampleSize > 0 ? (
          <Typography variant="caption" color="text.secondary">
            Based on {market.historicalSampleSize} results
          </Typography>
        ) : null}

        <Stack direction="row" spacing={1}>
          <Button
            size="small"
            variant="contained"
            startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
            disabled={!selected && !canAdd}
            onClick={() => (selected || canAdd ? builder.toggleLeg(market) : undefined)}
            sx={entryButtonSx(selected, incompatible)}
          >
            {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
          </Button>
          <Button size="small" variant="outlined" onClick={() => setDetailsOpen(true)} sx={compareButtonSx}>
            Details
          </Button>
        </Stack>
      </Stack>
      <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
    </AppCard>
  );
}

function ApiStateCard({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <AppCard sx={{ borderColor: tone === 'error' ? 'rgba(244,63,94,.32)' : undefined }}>
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </AppCard>
  );
}

function EvValue({ value }: { value: number }) {
  return (
    <Stack direction="row" spacing={0.55} alignItems="baseline" flexShrink={0}>
      <Typography variant="caption" color="text.secondary" fontWeight={850}>
        EV
      </Typography>
      <Typography color={value >= 0 ? 'success.main' : 'error.main'} fontWeight={950}>
        {formatPercent(value, { signed: true })}
      </Typography>
    </Stack>
  );
}

function LineShoppingPreview({ markets }: { markets: MarketOpportunity[] }) {
  const allReferenceLinesMissing = markets.every((market) => !hasReferenceLine(market));

  return (
    <Stack spacing={1.2}>
      {markets.map((market) => (
        <LineShoppingPreviewCard key={market.id} market={market} showReference={!allReferenceLinesMissing} />
      ))}
    </Stack>
  );
}

function getAvailableSportCodes(markets: MarketOpportunity[]) {
  return new Set(markets.map((market) => market.sport));
}

function getDashboardSportOptions(availableSportCodes: Set<Exclude<SportCode, 'ALL'>>) {
  if (!availableSportCodes.size) {
    return mockSports.filter((sport) => sport.code === 'ALL');
  }

  return mockSports.filter((sport) => {
    if (sport.code === 'ALL') return true;
    return availableSportCodes.has(sport.code);
  });
}

function LineShoppingPreviewCard({ market, showReference }: { market: MarketOpportunity; showReference: boolean }) {
  const warning = renderWarningText(market.warnings);

  return (
    <AppCard sx={{ background: 'rgba(15,23,42,.72)' }} contentSx={{ p: 1.7, '&:last-child': { pb: 1.7 } }}>
      <Stack spacing={0.85}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={2}>
          <Box minWidth={0}>
            <Typography fontWeight={900} noWrap>
              {market.player}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {market.side} {market.propType} {market.line}
            </Typography>
          </Box>
          <EvValue value={market.estimatedEv} />
        </Stack>

        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={{ xs: 0.8, sm: 1.4 }} alignItems={{ sm: 'center' }} flexWrap="wrap" useFlexGap>
          <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
          <Typography variant="body2" color="text.secondary">
            Best DFS line
          </Typography>
          {showReference && hasReferenceLine(market) ? (
            <Typography variant="body2" color="text.secondary">
              Reference: <Box component="span" sx={{ color: 'text.primary', fontWeight: 850 }}>{market.sportsbookContext}</Box>
            </Typography>
          ) : null}
        </Stack>

        {warning ? (
          <Typography variant="body2" color="warning.main">
            {warning}
          </Typography>
        ) : null}
      </Stack>
    </AppCard>
  );
}

function hasReferenceLine(market: MarketOpportunity) {
  const context = String(market.sportsbookContext ?? '').trim().toLowerCase();
  return Boolean(context && !context.includes('no sportsbook reference') && context !== 'no reference line' && context !== 'none');
}

function EntryBuilderPreview({ provider, legs, sports }: { provider: string; legs: MarketOpportunity[]; sports: string[] }) {
  const hasProvider = legs.length > 0;
  const projectedEv = legs.length ? legs.reduce((sum, leg) => sum + leg.estimatedEv, 0) / legs.length : 0;
  const payoutMayVary = legs.some((leg) => leg.projectionModifier !== 'Normal');

  if (!hasProvider) {
    return (
      <AppCard sx={{ height: '100%' }}>
        <Stack spacing={1.4}>
          <Stack direction="row" spacing={1} alignItems="center">
            <ShoppingCartOutlinedIcon color="primary" />
            <Typography variant="h5">Current entry</Typography>
          </Stack>
          <Box>
            <Typography fontWeight={900}>No legs added yet.</Typography>
            <Typography color="text.secondary" variant="body2">
              Add a top pick to preview provider compatibility.
            </Typography>
          </Box>
          <Button component={RouterLink} to="/app/entry-builder" variant="outlined" disabled sx={{ alignSelf: 'flex-start' }}>
            Review entry
          </Button>
        </Stack>
      </AppCard>
    );
  }

  return (
    <AppCard sx={{ height: '100%' }}>
      <Stack spacing={1.6}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={2}>
          <Stack spacing={0.5}>
            <Stack direction="row" spacing={1} alignItems="center">
              <ShoppingCartOutlinedIcon color="primary" />
              <Typography variant="h5">Current entry</Typography>
            </Stack>
            {hasProvider ? (
              <ProviderLabel provider={provider} category="DFS" />
            ) : (
              <Typography color="text.secondary">No provider selected</Typography>
            )}
          </Stack>
          <MetadataText tone={hasProvider ? 'fresh' : 'default'}>{hasProvider ? 'Compatible' : 'Empty'}</MetadataText>
        </Stack>

        <Divider />

        <Grid container spacing={1.2}>
          <Grid size={{ xs: 6 }}>
            <MiniMetric label="Legs" value={`${legs.length}`} />
          </Grid>
          <Grid size={{ xs: 6 }}>
            <MiniMetric label="Avg EV" value={formatPercent(projectedEv, { signed: true })} />
          </Grid>
          <Grid size={{ xs: 6 }}>
            <MiniMetric label="Sports" value={sports.length ? sports.join(', ') : '-'} />
          </Grid>
          <Grid size={{ xs: 6 }}>
            <MiniMetric label="Payout" value={payoutMayVary ? 'May vary' : 'Standard'} tone={payoutMayVary ? 'warning' : 'default'} />
          </Grid>
        </Grid>

        <Stack spacing={0.9}>
          {legs.length ? legs.map((leg) => (
            <Box key={leg.id} sx={{ py: 0.95, borderBottom: '1px solid rgba(148,163,184,.12)' }}>
              <Stack direction="row" justifyContent="space-between" gap={1}>
                <Box minWidth={0}>
                  <Typography fontWeight={900} noWrap>{leg.player}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {leg.side} {leg.propType} {leg.line}
                  </Typography>
                </Box>
                <Typography color={leg.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
                  {formatPercent(leg.estimatedEv, { signed: true })}
                </Typography>
              </Stack>
            </Box>
          )) : (
            <Typography color="text.secondary">Add a top pick to preview provider compatibility.</Typography>
          )}
        </Stack>

        <Button component={RouterLink} to="/app/entry-builder" variant="contained">
          Review entry
        </Button>
      </Stack>
    </AppCard>
  );
}

function CompactMetric({ label, value, metric }: { label: string; value: string; metric?: 'ev' | 'edge' | 'hitChance' | 'trust' | 'warning' }) {
  return (
    <Stack spacing={0.2} minWidth={0}>
      <Typography variant="caption" color="text.secondary" fontWeight={800}>
        {label}
      </Typography>
      {metric === 'trust' ? (
        <TrustValue
          level={value}
          sx={{
            fontSize: '0.875rem',
            fontWeight: 900,
            lineHeight: 1.43
          }}
        />
      ) : (
        <Typography variant="body2" fontWeight={900} noWrap>
          {value}
        </Typography>
      )}
    </Stack>
  );
}

function getDashboardReason(market: MarketOpportunity) {
  return market.reasons.find((reason) => !/matches\s+conservative\s+candidate\s+strategy/i.test(reason));
}

function MiniMetric({ label, value, tone = 'default' }: { label: string; value: string; tone?: 'default' | 'warning' }) {
  return (
    <Box sx={{ py: 0.9, borderBottom: '1px solid rgba(148,163,184,.12)' }}>
      <Typography variant="caption" color="text.secondary" fontWeight={850}>
        {label}
      </Typography>
      <Typography fontWeight={950} color={tone === 'warning' ? 'warning.main' : 'text.primary'}>
        {value}
      </Typography>
    </Box>
  );
}

const baseEntryButtonSx = {
  ...compactActionButtonSx,
  minWidth: 104,
  height: 34,
  fontSize: 12.5,
  whiteSpace: 'nowrap'
};

const compareButtonSx = {
  ...secondaryGhostButtonSx,
  minWidth: 104,
  height: 34,
  fontSize: 12.5,
  whiteSpace: 'nowrap'
};

function entryButtonSx(selected: boolean, incompatible: boolean) {
  if (selected) {
    return {
      ...baseEntryButtonSx,
      ...removeActionButtonSx
    };
  }

  if (incompatible) {
    return {
      ...baseEntryButtonSx,
      ...lockedActionButtonSx
    };
  }

  return baseEntryButtonSx;
}

function PerformancePanel({ icon, title, value, detail, tone = 'default' }: { icon: ReactNode; title: string; value: string; detail: string; tone?: 'default' | 'warning' }) {
  return (
    <AppCard
      sx={{
        height: '100%',
        borderColor: tone === 'warning' ? 'rgba(245,158,11,.34)' : undefined,
        background: tone === 'warning'
          ? 'linear-gradient(180deg, rgba(17,28,49,.96), rgba(10,16,32,.96))'
          : undefined
      }}
    >
      <Stack spacing={1.2}>
        <Stack direction="row" spacing={1} alignItems="center">
          <Box sx={{ color: tone === 'warning' ? 'warning.main' : 'primary.main', display: 'flex' }}>{icon}</Box>
          <Typography color="text.secondary" fontWeight={850}>
            {title}
          </Typography>
        </Stack>
        <Typography variant="h4">{value}</Typography>
        <Typography color="text.secondary">{detail}</Typography>
      </Stack>
    </AppCard>
  );
}

function SparsePerformancePanel({
  availability
}: {
  availability?: {
    snapshotRowsLoaded?: number;
    latestCompletedDate?: string | null;
    latestResultsImportDate?: string | null;
    snapshotRowsForCompletedDate?: number;
    filteredRowsForCompletedDate?: number;
    gradedRowsForCompletedDate?: number;
    unmatchedRowsForCompletedDate?: number;
    importedResultsForCompletedDate?: number;
    propResultsForCompletedDate?: number;
    unmatchedPicks?: number;
    gradedPicks?: number;
    hasFilteredRowsForCompletedDate?: boolean;
    hasGradedData?: boolean;
    latestGradedBlocker?: string | null;
    latestGradedBlockerDetail?: string | null;
  };
}) {
  const latestCompletedDateLabel = formatDateShort(availability?.latestCompletedDate ?? availability?.latestResultsImportDate);
  const capturedRows = availability?.filteredRowsForCompletedDate
    ?? availability?.snapshotRowsForCompletedDate
    ?? availability?.snapshotRowsLoaded
    ?? 0;
  const importedResults = availability?.importedResultsForCompletedDate ?? availability?.propResultsForCompletedDate ?? 0;
  const gradedRows = availability?.gradedRowsForCompletedDate ?? availability?.gradedPicks ?? 0;
  const unmatchedRows = availability?.unmatchedRowsForCompletedDate ?? availability?.unmatchedPicks ?? 0;
  const hasCapturedRows = capturedRows > 0 || availability?.hasFilteredRowsForCompletedDate === true;
  const hasImportedResults = importedResults > 0;
  const title = gradedRows > 0
    ? latestCompletedDateLabel
      ? `Latest completed slate: ${latestCompletedDateLabel}`
      : 'Latest completed slate'
    : hasCapturedRows && unmatchedRows > 0
      ? latestCompletedDateLabel
        ? `No graded picks for ${latestCompletedDateLabel} yet.`
        : 'No graded picks yet.'
      : hasImportedResults
        ? latestCompletedDateLabel
          ? `No captured picks for ${latestCompletedDateLabel}.`
          : 'No captured picks yet.'
        : latestCompletedDateLabel
          ? `No captured picks for ${latestCompletedDateLabel}.`
          : 'No completed results imported yet.';
  const message = gradedRows > 0
    ? 'Recent performance is based on the latest completed result date.'
    : hasCapturedRows && unmatchedRows > 0
      ? latestCompletedDateLabel
        ? 'Captured picks were found for this completed slate, but none safely matched imported result rows.'
        : 'Captured picks were found for this completed slate, but none safely matched imported result rows.'
      : hasImportedResults
        ? 'Results are imported for this completed slate, but no captured picks were available to grade.'
        : 'Performance appears after completed games are imported and matched to captured picks.';
  const countParts = hasImportedResults
    ? [
        `${capturedRows} captured picks`,
        `${formatInteger(importedResults)} imported results`,
        hasCapturedRows && unmatchedRows > 0 ? `${unmatchedRows} unmatched` : null
      ].filter(Boolean)
    : [];

  return (
    <AppCard>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} spacing={2}>
        <Stack direction="row" spacing={1.2} alignItems="flex-start">
          <Box sx={{ color: 'warning.main', display: 'flex', pt: 0.25 }}>
            <AccountTreeOutlinedIcon />
          </Box>
          <Box>
            <Typography fontWeight={950}>{title}</Typography>
            <Typography color="text.secondary" variant="body2">
              {message}
            </Typography>
            {latestCompletedDateLabel ? (
              <Typography color="text.secondary" variant="body2" fontWeight={700} mt={0.6}>
                Latest completed results: {latestCompletedDateLabel}
              </Typography>
            ) : null}
            {countParts.length ? (
              <Typography color="text.secondary" variant="body2" mt={0.4}>
                {countParts.join(' · ')}
              </Typography>
            ) : null}
          </Box>
        </Stack>
        <Button component={RouterLink} to="/app/performance" variant="outlined" sx={{ alignSelf: { xs: 'flex-start', sm: 'center' } }}>
          View performance
        </Button>
      </Stack>
    </AppCard>
  );
}

function formatDateShort(value?: string | null) {
  if (!value) return undefined;
  const parsed = new Date(`${value}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(parsed);
}

function formatInteger(value: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value);
}


