import { Box, Grid, Stack, Typography } from '@mui/material';
import { useMemo, useState } from 'react';
import { LineShoppingCard, LineShoppingTable } from '../../components/markets/LineShoppingTable';
import { MarketFilters, type MarketFilterOption, type MarketFilterValues } from '../../components/markets/MarketFilters';
import { PageHeader } from '../../components/ui/PageHeader';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { useLineShoppingData } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { palette } from '../../theme/palette';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { marketTagLabel, marketTagValue } from '../../utils/marketTags';

const defaultLineShoppingFilters: MarketFilterValues = {
  search: '',
  sport: 'All',
  provider: 'All',
  providerCategory: 'All',
  trust: 'All',
  opportunity: 'All',
  sort: 'EV',
  canAddOnly: true
};

export function LineShoppingPage() {
  const { isMobile } = useResponsiveView();
  const builder = useEntryBuilder();
  const [filters, setFilters] = useState<MarketFilterValues>(defaultLineShoppingFilters);
  const [candidateOnly, setCandidateOnly] = useState(true);
  const lineShoppingQuery = useLineShoppingData(50, false, 'line-shopping');
  const lineShoppingMarkets = lineShoppingQuery.markets.filter((market) => market.providerCategory === 'DFS');
  const providerCategoryOptions = useMemo(
    () => buildOptions(lineShoppingMarkets.map((market) => categoryFilterValue(market.providerCategory)), categoryFilterLabel),
    [lineShoppingMarkets]
  );
  const opportunityOptions = useMemo(
    () => buildOptions(lineShoppingMarkets.map((market) => marketTagValue(market)), marketTagLabel),
    [lineShoppingMarkets]
  );
  const showOpportunityFilter = opportunityOptions.length > 2;
  const visibleMarkets = useMemo(
    () => lineShoppingMarkets.filter((market) => lineShoppingMarketMatchesFilters(market, filters, candidateOnly, builder)),
    [builder, candidateOnly, filters, lineShoppingMarkets]
  );
  const bestEv = visibleMarkets.length ? Math.max(...visibleMarkets.map((market) => market.estimatedEv)) : 0;
  const dfsProviderCount = new Set(visibleMarkets.map((market) => market.provider)).size;
  const activeComparisons = visibleMarkets.filter((market) => market.freshness === 'Fresh').length;
  const referenceCoverage = visibleMarkets.filter(
    (market) => Boolean(market.sportsbookContext) || market.providerLines?.some((line) => line.providerCategory === 'Sportsbook')
  ).length;
  const clearFilters = () => {
    setFilters(defaultLineShoppingFilters);
    setCandidateOnly(true);
  };

  return (
    <Stack spacing={3} sx={{ px: { xs: 0.75, sm: 0 } }}>
      <PageHeader
        title="Line Shopping"
        description="Compare provider projections against reference lines without mixing incompatible entry types."
      />

      {isMobile ? (
        <MobileSummaryStrip
          bestEv={bestEv}
          dfsProviderCount={dfsProviderCount}
          marketCount={visibleMarkets.length}
          referenceCoverage={referenceCoverage}
        />
      ) : (
        <Grid container spacing={1.5}>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Best EV" value={formatPercent(bestEv, { signed: true })} helper="Top visible market" tone="success" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Providers" value={String(dfsProviderCount)} helper="Entry-supported platforms" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Active comparisons" value={String(activeComparisons)} helper="Fresh provider lines" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Reference-line coverage" value={`${referenceCoverage}/${visibleMarkets.length}`} helper="Markets with references" />
          </Grid>
        </Grid>
      )}

      <MarketFilters
        classificationLabel="Opportunity"
        values={filters}
        onValuesChange={setFilters}
        providerCategoryOptions={providerCategoryOptions}
        trustOptions={opportunityOptions}
        showClassificationFilter={showOpportunityFilter}
        candidateOnly={candidateOnly}
        onCandidateOnlyChange={setCandidateOnly}
        showCanAddOnly
        canAddOnlyLabel="Entry-compatible"
        onClearFilters={clearFilters}
      />
      <Typography variant="body2" color="text.secondary" sx={{ mt: -1.25 }}>
        Reference lines are sportsbook prices used for comparison. They are not added to entries.
      </Typography>
      {lineShoppingQuery.loading ? (
        <ApiStateCard message="Loading latest line-shopping rows..." />
      ) : lineShoppingQuery.error ? (
        <ApiStateCard tone="error" message="Line-shopping rows could not be loaded from the market API." />
      ) : isMobile ? (
        <Stack spacing={1.5}>
          {visibleMarkets.length ? visibleMarkets.map((market) => (
            <LineShoppingCard key={market.id} market={market} />
          )) : <ApiStateCard message="No line-shopping opportunities match these filters." />}
        </Stack>
      ) : visibleMarkets.length ? <LineShoppingTable markets={visibleMarkets} /> : <ApiStateCard message="No line-shopping opportunities match these filters." />}
    </Stack>
  );
}

function buildOptions(values: string[], labelForValue?: (value: string) => string): MarketFilterOption[] {
  const uniqueValues = Array.from(new Set(values.filter(Boolean))).sort((a, b) => (labelForValue?.(a) ?? a).localeCompare(labelForValue?.(b) ?? b));
  return [{ label: 'All', value: 'All' }, ...uniqueValues.map((value) => ({ label: labelForValue?.(value) ?? value, value }))];
}

function categoryFilterValue(category: string) {
  return category === 'Sportsbook' ? 'Reference only' : category;
}

function categoryFilterLabel(value: string) {
  return value;
}

function lineShoppingMarketMatchesFilters(
  market: MarketOpportunity,
  filters: MarketFilterValues,
  candidateOnly: boolean,
  builder: ReturnType<typeof useEntryBuilder>
) {
  if (filters.providerCategory !== 'All' && categoryFilterValue(market.providerCategory) !== filters.providerCategory) return false;
  if (filters.trust !== 'All' && marketTagValue(market) !== filters.trust) return false;
  if (candidateOnly && market.strategyClassification !== 'Candidate') return false;
  if (filters.canAddOnly && !isEntryCompatibleMarket(market, builder)) return false;

  const query = filters.search.trim().toLowerCase();
  if (!query) return true;

  return [market.player, market.team, market.opponent, market.event, market.propType, market.provider, market.side]
    .filter(Boolean)
    .some((value) => String(value).toLowerCase().includes(query));
}

function isEntryCompatibleMarket(market: MarketOpportunity, builder: ReturnType<typeof useEntryBuilder>) {
  const selected = builder.isSelected(market.id);
  return selected || (market.canAddToEntry && market.estimatedEv > 0 && builder.isCompatible(market));
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
    { value: String(dfsProviderCount), label: 'Providers' },
    { value: String(marketCount), label: 'Lines' },
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
