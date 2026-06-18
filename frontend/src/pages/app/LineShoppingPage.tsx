import { Box, Grid, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { LineShoppingCard, LineShoppingTable } from '../../components/markets/LineShoppingTable';
import { MarketFilters } from '../../components/markets/MarketFilters';
import { PageHeader } from '../../components/ui/PageHeader';
import { useLineShoppingData } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { palette } from '../../theme/palette';
import { formatPercent } from '../../utils/formatters';

export function LineShoppingPage() {
  const { isMobile } = useResponsiveView();
  const [candidateOnly, setCandidateOnly] = useState(true);
  const [dfsActionableOnly, setDfsActionableOnly] = useState(true);
  const lineShoppingQuery = useLineShoppingData(50, false, 'line-shopping');
  const lineShoppingMarkets = lineShoppingQuery.markets.filter((market) => market.providerCategory === 'DFS');
  const visibleMarkets = lineShoppingMarkets.filter((market) => {
    if (candidateOnly && market.strategyClassification !== 'Candidate') return false;
    if (dfsActionableOnly && !market.canAddToEntry) return false;
    return true;
  });
  const bestEv = lineShoppingMarkets.length ? Math.max(...lineShoppingMarkets.map((market) => market.estimatedEv)) : 0;
  const dfsProviderCount = new Set(lineShoppingMarkets.map((market) => market.provider)).size;
  const activeComparisons = lineShoppingMarkets.filter((market) => market.freshness === 'Fresh').length;
  const referenceCoverage = lineShoppingMarkets.filter(
    (market) => Boolean(market.sportsbookContext) || market.providerLines?.some((line) => line.providerCategory === 'Sportsbook')
  ).length;

  return (
    <Stack spacing={3} sx={{ px: { xs: 0.75, sm: 0 } }}>
      <PageHeader
        title="Line Shopping"
        description="Compare DFS projections against sportsbook reference lines without mixing incompatible providers."
      />

      {isMobile ? (
        <MobileSummaryStrip
          bestEv={bestEv}
          dfsProviderCount={dfsProviderCount}
          marketCount={lineShoppingMarkets.length}
          referenceCoverage={referenceCoverage}
        />
      ) : (
        <Grid container spacing={1.5}>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Best EV" value={formatPercent(bestEv, { signed: true })} helper="Top visible market" tone="success" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="DFS providers" value={String(dfsProviderCount)} helper="Actionable sources" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Active comparisons" value={String(activeComparisons)} helper="Fresh DFS markets" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Reference-line coverage" value={`${referenceCoverage}/${lineShoppingMarkets.length}`} helper="Markets with references" />
          </Grid>
        </Grid>
      )}

      <MarketFilters
        useToggleButtons
        classificationLabel="Opportunity"
        candidateOnly={candidateOnly}
        dfsActionableOnly={dfsActionableOnly}
        onCandidateOnlyChange={setCandidateOnly}
        onDfsActionableOnlyChange={setDfsActionableOnly}
      />
      <Typography variant="body2" color="text.secondary" sx={{ mt: -1.25 }}>
        Reference lines are sportsbook prices used for comparison. They are not added to DFS entries.
      </Typography>
      {lineShoppingQuery.loading ? (
        <ApiStateCard message="Loading latest line-shopping rows..." />
      ) : lineShoppingQuery.error ? (
        <ApiStateCard tone="error" message="Line-shopping rows could not be loaded from the market API." />
      ) : isMobile ? (
        <Stack spacing={1.5}>
          {visibleMarkets.length ? visibleMarkets.map((market) => (
            <LineShoppingCard key={market.id} market={market} />
          )) : <ApiStateCard message="No DFS line-shopping rows match the current filters." />}
        </Stack>
      ) : visibleMarkets.length ? <LineShoppingTable markets={visibleMarkets} /> : <ApiStateCard message="No DFS line-shopping rows match the current filters." />}
    </Stack>
  );
}

function ApiStateCard({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <Box
      sx={{
        border: `1px solid ${tone === 'error' ? 'rgba(244,63,94,.32)' : 'rgba(148,163,184,.16)'}`,
        borderRadius: 4,
        bgcolor: palette.cardSurface,
        p: 2
      }}
    >
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </Box>
  );
}

function MobileSummaryStrip({
  bestEv,
  dfsProviderCount,
  marketCount,
  referenceCoverage
}: {
  bestEv: number;
  dfsProviderCount: number;
  marketCount: number;
  referenceCoverage: number;
}) {
  const items = [
    { value: formatPercent(bestEv, { signed: true }), label: 'Best EV', tone: 'success' },
    { value: String(dfsProviderCount), label: 'DFS providers' },
    { value: String(marketCount), label: 'Markets' },
    { value: `${referenceCoverage}/${marketCount}`, label: 'References' }
  ];

  return (
    <Box
      sx={{
        border: '1px solid rgba(148,163,184,.16)',
        borderRadius: 4,
        bgcolor: palette.cardSurface,
        px: 1.2,
        py: 1
      }}
    >
      <Grid container spacing={0.8}>
        {items.map((item) => (
          <Grid key={item.label} size={6}>
            <Stack spacing={0.15} alignItems="center" textAlign="center">
              <Typography variant="body1" color={item.tone === 'success' ? 'success.main' : 'text.primary'} fontWeight={950}>
                {item.value}
              </Typography>
              <Typography variant="caption" color="text.secondary" fontWeight={800}>
                {item.label}
              </Typography>
            </Stack>
          </Grid>
        ))}
      </Grid>
    </Box>
  );
}

function SummaryMetric({ label, value, helper, tone = 'default' }: { label: string; value: string; helper: string; tone?: 'default' | 'success' }) {
  return (
    <Box
      sx={{
        height: '100%',
        border: '1px solid rgba(148,163,184,.16)',
        borderRadius: 4,
        bgcolor: palette.cardSurface,
        p: 1.7
      }}
    >
      <Stack spacing={0.35}>
        <Typography variant="caption" color="text.secondary" fontWeight={850} textTransform="uppercase">
          {label}
        </Typography>
        <Typography variant="h5" color={tone === 'success' ? 'success.main' : 'text.primary'} fontWeight={950}>
          {value}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {helper}
        </Typography>
      </Stack>
    </Box>
  );
}
