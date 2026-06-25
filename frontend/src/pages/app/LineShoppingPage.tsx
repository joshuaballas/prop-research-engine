import { Box, Grid, Stack, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import { groupLineShoppingMarkets, LineShoppingCard, LineShoppingComparisonList, type LineShoppingGroup } from '../../components/markets/LineShoppingTable';
import { MarketFilters, type MarketFilterOption, type MarketFilterValues } from '../../components/markets/MarketFilters';
import { PageHeader } from '../../components/ui/PageHeader';
import { AppTablePagination } from '../../components/ui/AppTablePagination';
import { MetricCard } from '../../components/ui/MetricCard';
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
  sort: 'Edge',
  canAddOnly: true
};

export function LineShoppingPage() {
  const { isMobile } = useResponsiveView();
  const builder = useEntryBuilder();
  const [filters, setFilters] = useState<MarketFilterValues>(defaultLineShoppingFilters);
  const [candidateOnly, setCandidateOnly] = useState(true);
  const [page, setPage] = useState(1);
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
  const groups = useMemo(() => groupLineShoppingMarkets(visibleMarkets), [visibleMarkets]);
  const pageSize = isMobile ? 8 : 20;
  const pageCount = Math.max(1, Math.ceil(groups.length / pageSize));
  const visibleGroups = useMemo(() => {
    const start = (page - 1) * pageSize;
    return groups.slice(start, start + pageSize);
  }, [groups, page, pageSize]);
  const bestEdge = visibleMarkets.length ? Math.max(...visibleMarkets.map((market) => market.probabilityEdge)) : 0;
  const dfsProviderCount = new Set(visibleMarkets.map((market) => market.provider)).size;
  const clearFilters = () => {
    setFilters(defaultLineShoppingFilters);
    setCandidateOnly(true);
  };

  useEffect(() => {
    setPage(1);
  }, [candidateOnly, filters, pageSize]);

  useEffect(() => {
    setPage((current) => Math.min(current, pageCount));
  }, [pageCount]);

  return (
    <Stack spacing={3} sx={{ px: { xs: 0.75, sm: 0 } }}>
      <PageHeader
        title="Line Shopping"
        description="Compare provider projections against reference lines without mixing incompatible entry types."
      />

      <Grid container spacing={2}>
        <Grid size={{ xs: 6, sm: 6, lg: 4 }}>
          <MetricCard label="Best Edge" value={formatPercent(bestEdge, { signed: true })} helper="Top available line" valueColor="success.main" />
        </Grid>
        <Grid size={{ xs: 6, sm: 6, lg: 4 }}>
          <MetricCard label="Providers" value={String(dfsProviderCount)} helper="Live providers" />
        </Grid>
        <Grid size={{ xs: 6, sm: 6, lg: 4 }}>
          <MetricCard label="Lines" value={String(visibleMarkets.length)} helper="Available now" />
        </Grid>
      </Grid>

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
        Sportsbook lines are for comparison only. Provider lines are the picks you can add to an entry.
      </Typography>
      {lineShoppingQuery.loading ? (
        <ApiStateCard message="Loading latest line-shopping rows..." />
      ) : lineShoppingQuery.error ? (
        <ApiStateCard tone="error" message="Line-shopping rows could not be loaded from the market API." />
      ) : isMobile ? (
        groups.length ? (
          <>
            <Stack sx={{ gap: 1.5 }}>
              {visibleGroups.map((group) => (
                <LineShoppingCard key={group.key} market={marketFromLineShoppingGroup(group)} />
              ))}
            </Stack>
            <AppTablePagination count={groups.length} page={page} pageSize={pageSize} itemLabel="opportunities" onChange={setPage} />
          </>
        ) : <ApiStateCard message="No line-shopping opportunities match these filters." />
      ) : groups.length ? (
        <>
          <LineShoppingComparisonList groups={visibleGroups} />
          <AppTablePagination count={groups.length} page={page} pageSize={pageSize} itemLabel="opportunities" onChange={setPage} />
        </>
      ) : <ApiStateCard message="No line-shopping opportunities match these filters." />}
    </Stack>
  );
}

function marketFromLineShoppingGroup(group: LineShoppingGroup): MarketOpportunity {
  return {
    ...group.bestMarket,
    providerLines: [
      ...group.providerLines,
      ...group.referenceLines
    ]
  };
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
