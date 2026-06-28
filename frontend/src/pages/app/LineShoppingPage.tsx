import { Box, Grid, Stack, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import { groupLineShoppingMarkets, LineShoppingCard, LineShoppingComparisonList, type LineShoppingGroup } from '../../components/markets/LineShoppingTable';
import { MarketFilters, type MarketFilterOption, type MarketFilterValues } from '../../components/markets/MarketFilters';
import { PageHeader } from '../../components/ui/PageHeader';
import { AppTablePagination } from '../../components/ui/AppTablePagination';
import { MarketCardSkeletonGrid, MarketTableSkeleton, MetricCardSkeleton } from '../../components/ui/LoadingSkeletons';
import { MetricCard } from '../../components/ui/MetricCard';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { useLineShoppingData } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { palette } from '../../theme/palette';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { isEntryBuilderAddableMarket, isLineNoLongerAvailable } from '../../utils/marketCautions';

const defaultLineShoppingFilters: MarketFilterValues = {
  search: '',
  sport: 'All',
  provider: 'All',
  providerCategory: 'All',
  trust: 'All',
  opportunity: 'All',
  sort: 'Edge',
  canAddOnly: false
};

export function LineShoppingPage() {
  const { isMobile } = useResponsiveView();
  const builder = useEntryBuilder();
  const [filters, setFilters] = useState<MarketFilterValues>(defaultLineShoppingFilters);
  const [candidateOnly, setCandidateOnly] = useState(false);
  const [page, setPage] = useState(1);
  const lineShoppingQuery = useLineShoppingData(50, false, 'line-shopping');
  const lineShoppingMarkets = lineShoppingQuery.markets.filter(isLineShoppingDisplayMarket);
  const sportOptions = useMemo(() => buildOptions(lineShoppingMarkets.map((market) => market.sport)), [lineShoppingMarkets]);
  const sportFilteredRows = useMemo(
    () => lineShoppingMarkets.filter((market) => filters.sport === 'All' || market.sport === filters.sport),
    [filters.sport, lineShoppingMarkets]
  );
  const providerOptions = useMemo(() => buildOptions(sportFilteredRows.map((market) => market.provider)), [sportFilteredRows]);
  const providerCategoryOptions = useMemo(
    () => buildOptions(sportFilteredRows.map((market) => categoryFilterValue(market.providerCategory)), categoryFilterLabel),
    [sportFilteredRows]
  );
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
  const bestEdge = visibleMarkets.length ? Math.max(...visibleMarkets.map((market) => market.probabilityEdge ?? Number.NEGATIVE_INFINITY)) : null;
  const dfsProviderCount = new Set(visibleMarkets.map((market) => market.provider)).size;
  const clearFilters = () => {
    setFilters(defaultLineShoppingFilters);
    setCandidateOnly(false);
  };

  useEffect(() => {
    setFilters((current) => normalizeFilterSelection(current, {
      sportOptions,
      providerOptions,
      providerCategoryOptions,
      opportunityOptions: [{ label: 'All', value: 'All' }]
    }));
  }, [providerCategoryOptions, providerOptions, sportOptions]);

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

      {lineShoppingQuery.loading ? (
        <MetricCardSkeleton count={3} />
      ) : (
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
      )}

      <MarketFilters
        idPrefix="line-shopping-filters"
        values={filters}
        onValuesChange={setFilters}
        scannerFields
        compactMobile
        sportOptions={sportOptions}
        providerOptions={providerOptions}
        providerCategoryOptions={providerCategoryOptions}
        showClassificationFilter={false}
        showOpportunityFilter={false}
        candidateOnly={candidateOnly}
        candidateOnlyLabel="Featured only"
        onCandidateOnlyChange={setCandidateOnly}
        showCanAddOnly
        canAddOnlyLabel="Entry-compatible"
        onClearFilters={clearFilters}
      />
      <Typography variant="body2" color="text.secondary" sx={{ mt: -1.25 }}>
        {filters.canAddOnly
          ? 'Showing Entry-compatible DFS rows only. Sportsbook rows are hidden because they cannot be added to Entry Builder.'
          : 'Showing all current rows. Use Entry-compatible to show only DFS rows that can be added to an entry.'}
      </Typography>
      {lineShoppingQuery.loading ? (
        isMobile ? <MarketCardSkeletonGrid count={6} /> : <MarketTableSkeleton rows={8} columns={7} minWidth={980} />
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
  return category;
}

function categoryFilterLabel(value: string) {
  return value;
}

function normalizeFilterSelection(
  filters: MarketFilterValues,
  options: {
    sportOptions: MarketFilterOption[];
    providerOptions: MarketFilterOption[];
    providerCategoryOptions: MarketFilterOption[];
    opportunityOptions: MarketFilterOption[];
  }
) {
  const next = {
    ...filters,
    sport: optionValueOrAll(filters.sport, options.sportOptions),
    provider: optionValueOrAll(filters.provider, options.providerOptions),
    providerCategory: optionValueOrAll(filters.providerCategory, options.providerCategoryOptions),
    opportunity: optionValueOrAll(filters.opportunity, options.opportunityOptions)
  };

  return shallowEqualFilters(filters, next) ? filters : next;
}

function optionValueOrAll(value: string, options: MarketFilterOption[]) {
  return options.some((option) => option.value === value) ? value : 'All';
}

function shallowEqualFilters(left: MarketFilterValues, right: MarketFilterValues) {
  return Object.keys(left).every((key) => left[key as keyof MarketFilterValues] === right[key as keyof MarketFilterValues]);
}

function isLineShoppingDisplayMarket(market: MarketOpportunity) {
  return (market.providerCategory === 'DFS' || market.providerCategory === 'Sportsbook') && !isLineNoLongerAvailable(market);
}

function lineShoppingMarketMatchesFilters(
  market: MarketOpportunity,
  filters: MarketFilterValues,
  candidateOnly: boolean,
  builder: ReturnType<typeof useEntryBuilder>
) {
  if (filters.sport !== 'All' && market.sport !== filters.sport) return false;
  if (filters.provider !== 'All' && market.provider !== filters.provider) return false;
  if (filters.providerCategory !== 'All' && categoryFilterValue(market.providerCategory) !== filters.providerCategory) return false;
  if (candidateOnly && market.strategyClassification !== 'Candidate' && !market.sportsbookContextResearchOnly) return false;
  if (filters.canAddOnly && !isEntryCompatibleMarket(market, builder)) return false;

  const query = filters.search.trim().toLowerCase();
  if (!query) return true;

  return [market.player, market.team, market.opponent, market.event, market.propType, market.provider, market.side]
    .filter(Boolean)
    .some((value) => String(value).toLowerCase().includes(query));
}

function isEntryCompatibleMarket(market: MarketOpportunity, builder: ReturnType<typeof useEntryBuilder>) {
  const selected = builder.isSelected(market.id);
  return selected || (isEntryBuilderAddableMarket(market) && builder.isCompatible(market));
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
