import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined';
import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import InsightsOutlinedIcon from '@mui/icons-material/InsightsOutlined';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import ShoppingCartOutlinedIcon from '@mui/icons-material/ShoppingCartOutlined';
import { Box, Button, Divider, Grid, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { ProjectionModifierBadge } from '../../components/markets/ProjectionModifierBadge';
import { SportToggleRail } from '../../components/navigation/SportToggleRail';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { AppCard } from '../../components/ui/AppCard';
import { DataStateBanner } from '../../components/ui/DataStateBanner';
import { MetadataText } from '../../components/ui/MetadataText';
import { MetricCard } from '../../components/ui/MetricCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { StatusPill } from '../../components/ui/StatusPill';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../../components/ui/buttonStyles';
import { mockMarkets } from '../../data/mockMarkets';
import { mockPerformance } from '../../data/mockPerformance';
import { mockTopPicks } from '../../data/mockTopPicks';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { useSelectedSports } from '../../hooks/useSelectedSports';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';

export function DashboardPage() {
  const sports = useSelectedSports();
  const builder = useEntryBuilder();
  const filtered = mockTopPicks.filter((market) => sports.includesSport(market.sport));
  const filteredMarkets = mockMarkets.filter((market) => sports.includesSport(market.sport));
  const activeDfs = filteredMarkets.filter((market) => market.providerCategory === 'DFS' && market.freshness === 'Fresh');
  const entryProvider = builder.lockedProviderName ?? 'No provider selected';
  const entryLegs = builder.legs;
  const compatibleSports = Array.from(new Set(entryLegs.map((market) => market.sport)));
  const overall = mockPerformance[0];
  const bestStrategy = mockPerformance.find((item) => item.groupType === 'Strategy');
  const limitedHistoryCount = mockPerformance.filter((item) => item.warning).length;

  return (
    <Stack spacing={{ xs: 2.5, md: 3 }}>
      <PageHeader
        title="Dashboard"
        description="Current DFS opportunities, provider-compatible entry state, reference lines, and recent performance."
        action={
          <Stack spacing={0.3} alignItems={{ xs: 'flex-start', md: 'flex-end' }}>
            <MetadataText tone="fresh">Updated 4m ago</MetadataText>
            <Typography variant="caption" color="text.secondary">
              Next scheduled market check in 1m
            </Typography>
          </Stack>
        }
      />

      <SportToggleRail selectedSports={sports.selectedSports} onToggle={sports.toggleSport} />

      <DataStateBanner
        title="Market data is usable"
        message="DFS rows are current for selected sports. Sportsbook rows are reference lines only and are not added to entries."
        status="Fresh"
      />

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="Top Picks" value={`${filtered.filter((market) => market.strategyClassification === 'Candidate').length}`} helper="Selected sports" />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="Best EV" value={formatPercent(filtered[0]?.estimatedEv ?? 0, { signed: true })} helper={filtered[0]?.player ?? 'No active row'} />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="DFS Providers" value={`${new Set(activeDfs.map((market) => market.provider)).size}`} helper="Entry-supported sources" />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="Entry Builder" value={`${entryLegs.length} legs`} helper={entryProvider} />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}>
          <MetricCard label="Graded picks" value={`${overall.gradedPicks}`} helper="Recent performance" />
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
        {filtered.slice(0, 3).map((market) => (
          <Grid key={market.id} size={{ xs: 12, lg: 4 }}>
            <DashboardOpportunityCard market={market} builder={builder} />
          </Grid>
        ))}
      </Grid>

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, lg: 7 }}>
          <SectionHeader title="Line-shopping preview" description="Best DFS provider beside sportsbook reference lines." />
          <Box mt={2}>
            <Stack spacing={1.2}>
              {filteredMarkets.slice(0, 5).map((market) => (
                <LineShoppingPreviewCard key={market.id} market={market} />
              ))}
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
        title="Performance snapshot"
        description="Early strategy evidence with limited-history warnings kept visible."
        action={
          <Button component={RouterLink} to="/app/performance" variant="outlined">
            View performance
          </Button>
        }
      />
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 4 }}>
          <PerformancePanel icon={<InsightsOutlinedIcon />} title="Candidate performance" value={formatPercent(overall.roi, { signed: true })} detail={`${overall.wins}-${overall.losses}-${overall.pushes} across ${overall.gradedPicks} graded picks`} />
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <PerformancePanel icon={<AutoGraphOutlinedIcon />} title="Best strategy" value={bestStrategy ? formatPercent(bestStrategy.roi, { signed: true }) : '-'} detail={bestStrategy ? `${bestStrategy.groupKey} · ${bestStrategy.gradedPicks} graded picks` : 'No graded strategy yet'} />
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <PerformancePanel icon={<AccountTreeOutlinedIcon />} title="Limited history" value={`${limitedHistoryCount} groups`} detail="Review small samples before treating a slice as reliable." tone="warning" />
        </Grid>
      </Grid>
    </Stack>
  );
}

function DashboardOpportunityCard({ market, builder }: { market: MarketOpportunity; builder: ReturnType<typeof useEntryBuilder> }) {
  const warning = market.warnings[0];
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
          <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
            {formatPercent(market.estimatedEv, { signed: true })}
          </Typography>
        </Stack>

        <ProviderLabel provider={market.provider} category={market.providerCategory} compact />

        <Stack direction="row" gap={0.75} flexWrap="wrap">
          <StatusPill label={market.strategyClassification} />
          <ProjectionModifierBadge modifier={market.projectionModifier} />
        </Stack>

        <Divider />

        <Grid container spacing={1.2}>
          <Grid size={{ xs: 4 }}>
            <CompactMetric label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} />
          </Grid>
          <Grid size={{ xs: 4 }}>
            <CompactMetric label="Model" value={formatPercent(market.modelProbability)} />
          </Grid>
          <Grid size={{ xs: 4 }}>
            <CompactMetric label="Sample" value={`${market.historicalSampleSize}`} />
          </Grid>
        </Grid>

        {warning ? (
          <Typography variant="body2" color="warning.main">
            {customerWarning(warning)}
          </Typography>
        ) : (
          <Typography variant="body2" color="text.secondary">
            {market.reasons[0]}
          </Typography>
        )}

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
          <Button component={RouterLink} to="/app/line-shopping" size="small" variant="outlined" sx={compareButtonSx}>
            Compare
          </Button>
        </Stack>
      </Stack>
    </AppCard>
  );
}

function LineShoppingPreviewCard({ market }: { market: MarketOpportunity }) {
  const warning = market.warnings[0] ? customerWarning(market.warnings[0]) : undefined;
  const referenceState = market.sportsbookContext ?? 'No reference line';

  return (
    <AppCard sx={{ background: 'rgba(15,23,42,.72)' }}>
      <Stack spacing={1.1}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={2}>
          <Box minWidth={0}>
            <Typography fontWeight={900} noWrap>
              {market.player}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {market.side} {market.propType} {market.line}
            </Typography>
          </Box>
          <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950} flexShrink={0}>
            {formatPercent(market.estimatedEv, { signed: true })}
          </Typography>
        </Stack>

        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={{ xs: 0.8, sm: 1.4 }} alignItems={{ sm: 'center' }} flexWrap="wrap" useFlexGap>
          <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
          <Typography variant="body2" color="text.secondary">
            Entry group: <Box component="span" sx={{ color: 'text.primary', fontWeight: 850 }}>{market.entryCompatibilityGroup}</Box>
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Reference lines: <Box component="span" sx={{ color: 'text.primary', fontWeight: 850 }}>{referenceState}</Box>
          </Typography>
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

function EntryBuilderPreview({ provider, legs, sports }: { provider: string; legs: MarketOpportunity[]; sports: string[] }) {
  const hasProvider = legs.length > 0;
  const projectedEv = legs.length ? legs.reduce((sum, leg) => sum + leg.estimatedEv, 0) / legs.length : 0;
  const payoutMayVary = legs.some((leg) => leg.projectionModifier !== 'Normal');

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

function CompactMetric({ label, value }: { label: string; value: string }) {
  return (
    <Stack spacing={0.2} minWidth={0}>
      <Typography variant="caption" color="text.secondary" fontWeight={800}>
        {label}
      </Typography>
      <Typography variant="body2" fontWeight={900} noWrap>
        {value}
      </Typography>
    </Stack>
  );
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

function customerWarning(warning: string) {
  const replacements: Record<string, string> = {
    'Sportsbook context rows cannot be added': 'Reference lines are not entry legs',
    'NBA sample is thin for this market': 'Limited history for this market',
    'Low sample size': 'Limited history',
    'Provider limited for this sport': 'Limited coverage'
  };

  return replacements[warning] ?? warning;
}
