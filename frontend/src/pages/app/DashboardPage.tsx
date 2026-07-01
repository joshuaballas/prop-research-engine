import { Box, Button, Grid, Stack, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { MetricLabelWithHelp } from '../../components/metrics/MetricLabelWithHelp';
import { SportToggleRail } from '../../components/navigation/SportToggleRail';
import { PropDetailsModal } from '../../components/prop-history/PropDetailsModal';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { DataStateBanner } from '../../components/ui/DataStateBanner';
import { MarketTableSkeleton, MetricCardSkeleton } from '../../components/ui/LoadingSkeletons';
import { MetricCard } from '../../components/ui/MetricCard';
import { OpportunityPickText } from '../../components/ui/OpportunityCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { AppDataTable } from '../../components/ui/AppDataTable';
import { AppRowActions, desktopRowDetailsButtonSx, mobileCardDetailsButtonSx } from '../../components/ui/AppRowActions';
import { AppTableMetricValue, AppTablePrimaryText, AppTableSecondaryText } from '../../components/ui/AppTableTypography';
import { appTableRowSx, stickyActionCellSx, stickyActionHeaderCellSx } from '../../components/ui/appTableStyles';
import { useLineShoppingData, useMarketFreshnessData, usePerformanceData, useTopPicksData } from '../../hooks/useMarketData';
import type { MarketReadSport } from '../../hooks/useMarketData';
import { useSelectedSports } from '../../hooks/useSelectedSports';
import { radius } from '../../theme/radius';
import type { MarketOpportunity } from '../../types/markets';
import type { SportCode } from '../../types/sports';
import { formatAmericanOdds, formatPercent } from '../../utils/formatters';
import { marketDisplayLabel } from '../../utils/marketDisplay';
import { buildAvailableSportOptions, getAvailableSportCodes } from '../../utils/sportOptions';

const TOP_OPPORTUNITY_LIMIT = 8;
const LINE_EDGE_LIMIT = 5;

export function DashboardPage() {
  const sports = useSelectedSports();
  const marketReadSport = resolveDashboardMarketReadSport(sports.selectedSports);
  const freshnessQuery = useMarketFreshnessData();
  const topPicksQuery = useTopPicksData(10, 'dashboard-top-pick', marketReadSport);
  const lineShoppingQuery = useLineShoppingData(12, true, 'dashboard-line-shopping', marketReadSport);
  const performanceQuery = usePerformanceData('latestGraded', marketReadSport);
  const topPicks = topPicksQuery.markets;
  const lineShoppingMarkets = lineShoppingQuery.markets;
  const performanceRows = performanceQuery.rows;
  const performanceAvailability = performanceQuery.summary?.dataAvailability;
  const availableSportCodes = useMemo(() => getAvailableSportCodes([...topPicks, ...lineShoppingMarkets]), [lineShoppingMarkets, topPicks]);
  const dashboardSports = useMemo(() => buildAvailableSportOptions(availableSportCodes), [availableSportCodes]);
  const filteredTopPicks = topPicks.filter((market) => sports.includesSport(market.sport));
  const filteredLineMarkets = lineShoppingMarkets.filter((market) => sports.includesSport(market.sport));
  const overall = performanceRows.find((item) => item.groupType === 'Overall') ?? performanceRows[0];
  const latestGradedDate = performanceQuery.summary?.performanceGrainDiagnostics?.latestGradedDate
    ?? performanceAvailability?.latestGradedDate
    ?? performanceAvailability?.selectedGradedDate
    ?? undefined;
  const latestGradedDateLabel = formatDateShort(latestGradedDate);
  const bestEdgeMarket = filteredTopPicks.reduce<MarketOpportunity | undefined>(
    (best, market) => (!best || (market.probabilityEdge ?? Number.NEGATIVE_INFINITY) > (best.probabilityEdge ?? Number.NEGATIVE_INFINITY) ? market : best),
    undefined
  );
  const freshness = freshnessQuery.data;
  const dataStatus = freshness?.hasUsableMarketData ? 'Fresh' : freshness?.isStale ? 'Stale' : 'No data';
  const isDataHealthy = !freshnessQuery.error && freshness?.hasUsableMarketData && !freshness?.isStale;
  const hasLatestGradedSlate = Boolean(latestGradedDateLabel && overall && overall.gradedPicks > 0);
  const anyPrimaryLoading = topPicksQuery.loading || lineShoppingQuery.loading || performanceQuery.loading;

  useEffect(() => {
    const selectedSpecificSports = sports.selectedSports.filter((sport): sport is Exclude<SportCode, 'ALL'> => sport !== 'ALL');
    if (!selectedSpecificSports.length) return;
    if (selectedSpecificSports.some((sport) => !availableSportCodes.has(sport))) {
      sports.toggleSport('ALL');
    }
  }, [availableSportCodes, sports.selectedSports, sports.toggleSport]);

  return (
    <Stack spacing={{ xs: 2, md: 3 }} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader
        title="Dashboard"
        description="Track today's best props, compare available lines, and review recent model results."
      />

      <SportToggleRail
        selectedSports={sports.selectedSports}
        onToggle={sports.toggleSport}
        compact
        sports={dashboardSports}
      />

      {!isDataHealthy ? (
        <DataStateBanner
          title={freshnessQuery.error ? 'Market data is unavailable' : freshness?.hasUsableMarketData ? 'Market data needs attention' : 'No usable market data yet'}
          message={freshnessQuery.error ? 'Market data could not be reached.' : freshness?.usabilityWarning ?? 'Current market availability may be limited.'}
          status={dataStatus}
        />
      ) : null}

      {anyPrimaryLoading ? (
        <MetricCardSkeleton count={3} />
      ) : (
        <DashboardMetricCards
          items={[
            { label: 'Top opportunities', value: `${filteredTopPicks.length}`, helper: 'Current board' },
            { label: 'Best edge', value: formatPercent(bestEdgeMarket?.probabilityEdge, { signed: true }), helper: bestEdgeMarket?.player ?? 'No active prop', valueColor: 'success.main' },
            {
              label: 'Latest graded slate',
              value: hasLatestGradedSlate ? latestGradedDateLabel ?? 'Pending' : 'Pending',
              helper: hasLatestGradedSlate && overall ? formatLatestGradedHelper(overall) : 'Awaiting graded slate'
            }
          ]}
        />
      )}

      <Stack spacing={{ xs: 2.2, md: 3 }}>
        <DashboardMarketSection
          title="Today's top opportunities"
          description="Ranked picks from the current board."
          actionLabel="View Top Picks"
          to="/app/top-picks"
        >
          {topPicksQuery.loading ? (
            <MarketTableSkeleton rows={6} columns={6} minWidth={0} />
          ) : topPicksQuery.error ? (
            <InlineState tone="error" message="Top opportunities could not be loaded." />
          ) : filteredTopPicks.length ? (
            <TopOpportunitiesBoard markets={filteredTopPicks.slice(0, TOP_OPPORTUNITY_LIMIT)} />
          ) : (
            <InlineState message="No top opportunities are available for the selected sports." />
          )}
        </DashboardMarketSection>

        <DashboardMarketSection
          title="Best line-shopping edges"
          description="Compact view of the best available provider lines."
          actionLabel="Open Line Shopping"
          to="/app/line-shopping"
        >
          {lineShoppingQuery.loading ? (
            <MarketTableSkeleton rows={5} columns={5} minWidth={0} />
          ) : lineShoppingQuery.error ? (
            <InlineState tone="error" message="Line-shopping edges could not be loaded." />
          ) : filteredLineMarkets.length ? (
            <LineShoppingEdgesBoard markets={filteredLineMarkets.slice(0, LINE_EDGE_LIMIT)} />
          ) : (
            <InlineState message="No line-shopping edges are available for the selected sports." />
          )}
        </DashboardMarketSection>
      </Stack>
    </Stack>
  );
}

function resolveDashboardMarketReadSport(selectedSports: SportCode[]): MarketReadSport {
  const selectedSpecificSports = selectedSports.filter((sport): sport is Exclude<SportCode, 'ALL'> => sport !== 'ALL');
  return selectedSpecificSports.length === 1 && (selectedSpecificSports[0] === 'MLB' || selectedSpecificSports[0] === 'SOCCER')
    ? selectedSpecificSports[0]
    : 'All';
}

function DashboardMetricCards({ items }: { items: Array<{ label: string; value: string; helper: string; valueColor?: string }> }) {
  return (
    <Grid container spacing={2}>
      {items.map((item) => (
        <Grid key={item.label} size={{ xs: 6, sm: 6, lg: 4 }}>
          <MetricCard label={item.label} value={item.value} helper={item.helper} valueColor={item.valueColor} />
        </Grid>
      ))}
    </Grid>
  );
}

function DashboardMarketSection({
  title,
  description,
  actionLabel,
  to,
  children
}: {
  title: string;
  description: string;
  actionLabel: string;
  to: string;
  children: ReactNode;
}) {
  return (
    <Box>
      <SectionHeader
        title={title}
        description={description}
        action={(
          <Button component={RouterLink} to={to} variant="outlined" size="small" sx={{ display: { xs: 'none', sm: 'inline-flex' } }}>
            {actionLabel}
          </Button>
        )}
      />
      <Button component={RouterLink} to={to} variant="outlined" size="small" fullWidth sx={{ display: { xs: 'inline-flex', sm: 'none' }, mt: 1 }}>
        {actionLabel}
      </Button>
      <Box mt={{ xs: 1, md: 1.6 }}>{children}</Box>
    </Box>
  );
}

function TopOpportunitiesBoard({ markets }: { markets: MarketOpportunity[] }) {
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);

  return (
    <>
      <Box sx={{ display: { xs: 'none', md: 'block' } }}>
        <AppDataTable tableSx={{ tableLayout: 'fixed', width: '100%' }}>
          <colgroup>
            <col style={{ width: '7%' }} />
            <col style={{ width: '28%' }} />
            <col style={{ width: '29%' }} />
            <col style={{ width: '12%' }} />
            <col style={{ width: '13%' }} />
            <col style={{ width: '11%' }} />
          </colgroup>
          <TableHead>
            <TableRow>
              <TableCell>Rank</TableCell>
              <TableCell>Player / prop</TableCell>
              <TableCell>Best pick</TableCell>
              <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
              <TableCell align="right"><MetricLabelWithHelp metric="hitChance" align="right" variant="table" /></TableCell>
              <TableCell align="center" sx={stickyActionHeaderCellSx}>Details</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {markets.map((market, index) => (
              <DashboardTopPickRow
                key={market.id}
                market={market}
                rank={market.displayRank ?? market.rank ?? index + 1}
                onDetails={() => setDetailsMarket(market)}
              />
            ))}
          </TableBody>
        </AppDataTable>
      </Box>

      <DashboardMobileList markets={markets} ranked onDetails={setDetailsMarket} />
      <PropDetailsModal market={detailsMarket} open={Boolean(detailsMarket)} onClose={() => setDetailsMarket(null)} />
    </>
  );
}

function DashboardTopPickRow({
  market,
  rank,
  onDetails
}: {
  market: MarketOpportunity;
  rank: number;
  onDetails: () => void;
}) {
  return (
    <TableRow hover sx={dashboardTableRowSx}>
      <TableCell>
        <AppTablePrimaryText>#{rank}</AppTablePrimaryText>
      </TableCell>
      <TableCell sx={{ overflow: 'hidden' }}>
        <Stack spacing={0.35}>
          <AppTablePrimaryText>{market.player}</AppTablePrimaryText>
          <AppTableSecondaryText>
            {[market.propType, marketDisplayLabel(market)].filter(Boolean).join(' - ')}
          </AppTableSecondaryText>
          <AppTableSecondaryText>{market.event || market.startTime ? [market.event, market.startTime].filter(Boolean).join(' - ') : 'Current board'}</AppTableSecondaryText>
        </Stack>
      </TableCell>
      <TableCell sx={{ overflow: 'hidden' }}>
        <Stack spacing={0.45} alignItems="flex-start">
          <OpportunityPickText
            side={market.side}
            propType={market.propType}
            line={market.line}
            sx={{
              fontSize: 13,
              flexWrap: 'nowrap',
              overflow: 'hidden',
              '& .MuiTypography-root': { fontSize: 'inherit', fontWeight: 700 }
            }}
          />
          <Stack direction="row" spacing={0.65} alignItems="center" minWidth={0}>
            <ProviderLabel provider={providerDisplayName(market)} category={market.providerCategory} compact />
            <AppTableSecondaryText sx={{ flexShrink: 0 }}>{formatPrice(market.bestAmericanPrice)}</AppTableSecondaryText>
          </Stack>
        </Stack>
      </TableCell>
      <TableCell align="right" sx={metricCellSx}><AppTableMetricValue>{formatPercent(market.probabilityEdge, { signed: true })}</AppTableMetricValue></TableCell>
      <TableCell align="right" sx={metricCellSx}><AppTableMetricValue>{formatPercent(market.modelProbability)}</AppTableMetricValue></TableCell>
      <TableCell data-sticky-action="true" align="center" sx={{ ...stickyActionCellSx, px: 0.75 }}>
        <DashboardMarketActions onDetails={onDetails} />
      </TableCell>
    </TableRow>
  );
}

function LineShoppingEdgesBoard({ markets }: { markets: MarketOpportunity[] }) {
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);

  return (
    <>
      <Box sx={{ display: { xs: 'none', md: 'block' } }}>
        <AppDataTable tableSx={{ tableLayout: 'fixed', width: '100%' }}>
          <colgroup>
            <col style={{ width: '30%' }} />
            <col style={{ width: '24%' }} />
            <col style={{ width: '17%' }} />
            <col style={{ width: '12%' }} />
            <col style={{ width: '7%' }} />
            <col style={{ width: '10%' }} />
          </colgroup>
          <TableHead>
            <TableRow>
              <TableCell>Player / prop</TableCell>
              <TableCell>Best line</TableCell>
              <TableCell>Best provider</TableCell>
              <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
              <TableCell align="right">Providers</TableCell>
              <TableCell align="center" sx={stickyActionHeaderCellSx}>Details</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {markets.map((market) => (
              <DashboardLineShoppingRow
                key={market.id}
                market={market}
                onDetails={() => setDetailsMarket(market)}
              />
            ))}
          </TableBody>
        </AppDataTable>
      </Box>

      <DashboardMobileList markets={markets} onDetails={setDetailsMarket} showProviderCount />
      <PropDetailsModal market={detailsMarket} open={Boolean(detailsMarket)} onClose={() => setDetailsMarket(null)} />
    </>
  );
}

function DashboardLineShoppingRow({
  market,
  onDetails
}: {
  market: MarketOpportunity;
  onDetails: () => void;
}) {
  return (
    <TableRow hover sx={dashboardTableRowSx}>
      <TableCell sx={{ overflow: 'hidden' }}>
        <Stack spacing={0.35}>
          <AppTablePrimaryText>{market.player}</AppTablePrimaryText>
          <AppTableSecondaryText>
            {[market.propType, marketDisplayLabel(market)].filter(Boolean).join(' - ')}
          </AppTableSecondaryText>
          <AppTableSecondaryText>{market.event || 'Current board'}</AppTableSecondaryText>
        </Stack>
      </TableCell>
      <TableCell sx={{ overflow: 'hidden' }}>
        <Stack spacing={0.35}>
          <OpportunityPickText
            side={market.side}
            propType={market.propType}
            line={market.line}
            sx={{
              fontSize: 13,
              flexWrap: 'nowrap',
              overflow: 'hidden',
              '& .MuiTypography-root': { fontSize: 'inherit', fontWeight: 700 }
            }}
          />
          <AppTableSecondaryText>{formatPrice(market.bestAmericanPrice)}</AppTableSecondaryText>
        </Stack>
      </TableCell>
      <TableCell sx={{ overflow: 'hidden' }}>
        <ProviderLabel provider={providerDisplayName(market)} category={market.providerCategory} compact />
      </TableCell>
      <TableCell align="right" sx={metricCellSx}><AppTableMetricValue>{formatPercent(market.probabilityEdge, { signed: true })}</AppTableMetricValue></TableCell>
      <TableCell align="right" sx={metricCellSx}><AppTableMetricValue>{getProviderCount(market)}</AppTableMetricValue></TableCell>
      <TableCell data-sticky-action="true" align="center" sx={{ ...stickyActionCellSx, px: 0.5 }}>
        <DashboardMarketActions onDetails={onDetails} />
      </TableCell>
    </TableRow>
  );
}

function DashboardMobileList({
  markets,
  ranked = false,
  showProviderCount = false,
  onDetails
}: {
  markets: MarketOpportunity[];
  ranked?: boolean;
  showProviderCount?: boolean;
  onDetails: (market: MarketOpportunity) => void;
}) {
  return (
    <Box
      sx={{
        display: { xs: 'block', md: 'none' },
        border: '1px solid rgba(148,163,184,.16)',
        borderRadius: radius.table,
        bgcolor: 'rgba(8,13,26,.28)',
        overflow: 'hidden'
      }}
    >
      {markets.map((market, index) => (
          <Box
            key={market.id}
            sx={{
              p: 1.15,
              borderBottom: index === markets.length - 1 ? 0 : '1px solid rgba(148,163,184,.12)'
            }}
          >
            <Stack spacing={1}>
              <Stack direction="row" justifyContent="space-between" gap={1.4} alignItems="flex-start">
                <Box minWidth={0}>
                  <Stack direction="row" spacing={0.7} alignItems="center">
                    {ranked ? (
                      <Typography color="primary.main" fontWeight={950} fontSize={13}>
                        #{market.displayRank ?? market.rank ?? index + 1}
                      </Typography>
                    ) : null}
                    <Typography fontWeight={950} noWrap>
                      {market.player}
                    </Typography>
                  </Stack>
                  <OpportunityPickText
                    side={market.side}
                    propType={market.propType}
                    line={market.line}
                    sport={marketDisplayLabel(market)}
                    sx={{
                      fontSize: 13,
                      flexWrap: 'nowrap',
                      overflow: 'hidden',
                      '& .MuiTypography-root': { fontSize: 'inherit' }
                    }}
                  />
                </Box>
                <Stack spacing={0.25} alignItems="flex-end" flexShrink={0}>
                  <Typography color={(market.probabilityEdge ?? 0) >= 0 ? 'success.main' : 'error.main'} fontWeight={950}>
                    {formatPercent(market.probabilityEdge, { signed: true })}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {formatPercent(market.modelProbability)} hit
                  </Typography>
                </Stack>
              </Stack>

              <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
                <ProviderLabel provider={providerDisplayName(market)} category={market.providerCategory} compact />
                {showProviderCount ? (
                  <Typography variant="caption" color="text.secondary" fontWeight={850}>
                    {getProviderCount(market)} providers
                  </Typography>
                ) : null}
              </Stack>

              <Box sx={{ display: 'grid', gridTemplateColumns: '1fr', gap: 0.65, '& > .MuiButton-root': { width: '100%' } }}>
                <Button size="small" variant="outlined" onClick={() => onDetails(market)} sx={mobileCardDetailsButtonSx}>
                  Details
                </Button>
              </Box>
            </Stack>
          </Box>
      ))}
    </Box>
  );
}

function DashboardMarketActions({ onDetails }: { onDetails: () => void }) {
  return (
    <AppRowActions sx={{ mx: 'auto' }}>
      <Button size="small" variant="outlined" onClick={onDetails} sx={desktopRowDetailsButtonSx}>
        Details
      </Button>
    </AppRowActions>
  );
}

function InlineState({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <Box
      sx={{
        borderTop: '1px solid rgba(148,163,184,.16)',
        borderBottom: '1px solid rgba(148,163,184,.16)',
        py: 2,
        px: 1.5,
        bgcolor: tone === 'error' ? 'rgba(127,29,29,.12)' : 'rgba(8,13,26,.2)'
      }}
    >
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </Box>
  );
}

const dashboardTableRowSx = {
  ...appTableRowSx,
  height: 58,
  '& td': {
    ...appTableRowSx['& td'],
    py: 0.55
  }
} as const;

const metricCellSx = {
  whiteSpace: 'nowrap',
  overflow: 'visible'
} as const;

function providerDisplayName(market: MarketOpportunity) {
  return market.provider || (market.providerCategory === 'Sportsbook' ? 'Sportsbook' : 'Unknown');
}

function getProviderCount(market: MarketOpportunity) {
  const providers = new Set<string>();
  if (market.provider) providers.add(market.provider);
  for (const line of market.providerLines ?? []) {
    if (line.provider) providers.add(line.provider);
  }

  return Math.max(
    providers.size || 1,
    market.providerFanoutCount ?? 1,
    (market.alternateProviderCount ?? 0) + 1
  );
}

function formatPrice(value?: number | null) {
  return value && Number.isFinite(value) ? formatAmericanOdds(value) : 'Price n/a';
}

function formatLatestGradedHelper(row: { wins: number; losses: number; pushes: number; hitRate: number; roi: number }) {
  return `${row.wins}-${row.losses}-${row.pushes} - ${formatPercent(row.hitRate)} - ${formatPercent(row.roi, { signed: true })}`;
}

function formatDateShort(value?: string | null) {
  if (!value) return undefined;
  const parsed = new Date(`${value}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(parsed);
}
