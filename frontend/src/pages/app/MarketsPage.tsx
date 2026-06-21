import { Box, Grid, Stack, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import { MarketCard } from '../../components/markets/MarketCard';
import { MarketFilters, type MarketFilterOption, type MarketFilterValues } from '../../components/markets/MarketFilters';
import { MarketTable } from '../../components/markets/MarketTable';
import { AppTablePagination } from '../../components/ui/AppTablePagination';
import { MetricCard } from '../../components/ui/MetricCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { useMarketScannerData } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { palette } from '../../theme/palette';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { isPlayableMarket } from '../../utils/marketCautions';
import { marketTagLabel, marketTagValue } from '../../utils/marketTags';

const defaultFilters: MarketFilterValues = {
  search: '',
  sport: 'All',
  provider: 'All',
  providerCategory: 'All',
  trust: 'All',
  opportunity: 'All',
  sort: 'EV',
  canAddOnly: true
};

const sortOptions: MarketFilterOption[] = [
  { label: 'EV', value: 'EV' },
  { label: 'Edge', value: 'Edge' },
  { label: 'Hit chance', value: 'HitChance' },
  { label: 'Trust', value: 'Trust' },
  { label: 'Player A-Z', value: 'PlayerAz' },
  { label: 'Starts soon', value: 'StartsSoon' }
];

export function MarketsPage() {
  const { isMobile } = useResponsiveView();
  const [filters, setFilters] = useState<MarketFilterValues>(defaultFilters);
  const [page, setPage] = useState(1);
  const marketsQuery = useMarketScannerData(75, 'market');
  const allMarkets = marketsQuery.markets;
  const sportOptions = useMemo(() => buildOptions(allMarkets.map((market) => market.sport)), [allMarkets]);
  const sportFilteredRows = useMemo(
    () => allMarkets.filter((market) => filters.sport === 'All' || market.sport === filters.sport),
    [allMarkets, filters.sport]
  );
  const providerOptions = useMemo(() => buildOptions(sportFilteredRows.map((market) => market.provider)), [sportFilteredRows]);
  const providerCategoryOptions = useMemo(
    () => buildOptions(sportFilteredRows.map((market) => categoryFilterValue(market.providerCategory)), categoryFilterLabel),
    [sportFilteredRows]
  );
  const trustOptions = useMemo(() => buildOptions(sportFilteredRows.map((market) => trustFilterValue(market)), trustFilterLabel), [sportFilteredRows]);
  const opportunityOptions = useMemo(
    () => buildOptions(sportFilteredRows.map((market) => marketTagValue(market)), marketTagLabel),
    [sportFilteredRows]
  );
  const filteredMarkets = useMemo(
    () => allMarkets.filter((market) => marketMatchesFilters(market, filters)),
    [allMarkets, filters]
  );
  const sortedMarkets = useMemo(() => sortMarkets(filteredMarkets, filters.sort), [filteredMarkets, filters.sort]);
  const pageSize = isMobile ? 8 : 20;
  const pageCount = Math.max(1, Math.ceil(sortedMarkets.length / pageSize));
  const visibleMarkets = useMemo(() => {
    const start = (page - 1) * pageSize;
    return sortedMarkets.slice(start, start + pageSize);
  }, [page, pageSize, sortedMarkets]);
  const dfsMarkets = filteredMarkets.filter((market) => market.providerCategory === 'DFS');
  const bestEdge = dfsMarkets.length ? Math.max(...dfsMarkets.map((market) => market.probabilityEdge)) : 0;
  const dfsProviderCount = new Set(dfsMarkets.map((market) => market.provider)).size;

  useEffect(() => {
    setFilters((current) => normalizeFilterSelection(current, {
      sportOptions,
      providerOptions,
      providerCategoryOptions,
      trustOptions,
      opportunityOptions
    }));
  }, [opportunityOptions, providerCategoryOptions, providerOptions, sportOptions, trustOptions]);

  useEffect(() => {
    setPage(1);
  }, [filters, pageSize]);

  useEffect(() => {
    setPage((current) => Math.min(current, pageCount));
  }, [pageCount]);

  return (
    <Stack spacing={3} sx={{ px: { xs: 0.75, sm: 0 } }}>
      <PageHeader title="Prop Scanner" description="Browse current provider props with hit chance, edge, trust, and entry status." />

      <Grid container spacing={2}>
        <Grid size={{ xs: 6, sm: 6, lg: 4 }}>
          <MetricCard label="Props" value={String(filteredMarkets.length)} helper="Available now" />
        </Grid>
        <Grid size={{ xs: 6, sm: 6, lg: 4 }}>
          <MetricCard label="Providers" value={String(dfsProviderCount)} helper="Live providers" />
        </Grid>
        <Grid size={{ xs: 6, sm: 6, lg: 4 }}>
          <MetricCard label="Best Edge" value={formatPercent(bestEdge, { signed: true })} helper="Top current prop" valueColor="success.main" />
        </Grid>
      </Grid>

      <MarketFilters
        scannerFields
        compactMobile
        classificationLabel="Trust"
        values={filters}
        onValuesChange={setFilters}
        sportOptions={sportOptions}
        providerOptions={providerOptions}
        providerCategoryOptions={providerCategoryOptions}
        trustOptions={trustOptions}
        opportunityOptions={opportunityOptions}
        sortOptions={sortOptions}
        showCanAddOnly
        onClearFilters={() => setFilters(defaultFilters)}
      />

      <SectionHeader title="Available props" description="Review available props, open Details for player history, and add eligible picks to an entry." />
      {marketsQuery.loading ? (
        <ApiStateCard message="Loading prop scanner rows..." />
      ) : marketsQuery.error ? (
        <ApiStateCard tone="error" message="Prop scanner rows could not be loaded from the market API." />
      ) : isMobile ? (
        sortedMarkets.length ? (
          <>
            <Grid container spacing={2}>{visibleMarkets.map((market) => <Grid key={market.id} size={12}><MarketCard market={market} /></Grid>)}</Grid>
            <AppTablePagination count={sortedMarkets.length} page={page} pageSize={pageSize} itemLabel="props" onChange={setPage} />
          </>
        ) : <ApiStateCard message="No props match these filters." />
      ) : sortedMarkets.length ? (
        <>
          <MarketTable markets={visibleMarkets} />
          <AppTablePagination count={sortedMarkets.length} page={page} pageSize={pageSize} itemLabel="props" onChange={setPage} />
        </>
      ) : <ApiStateCard message="No props match these filters." />}
    </Stack>
  );
}

function buildOptions(values: string[], labelForValue?: (value: string) => string): MarketFilterOption[] {
  const uniqueValues = Array.from(new Set(values.filter(Boolean))).sort((a, b) => (labelForValue?.(a) ?? a).localeCompare(labelForValue?.(b) ?? b));
  return [{ label: 'All', value: 'All' }, ...uniqueValues.map((value) => ({ label: labelForValue?.(value) ?? value, value }))];
}

function normalizeFilterSelection(
  filters: MarketFilterValues,
  options: {
    sportOptions: MarketFilterOption[];
    providerOptions: MarketFilterOption[];
    providerCategoryOptions: MarketFilterOption[];
    trustOptions: MarketFilterOption[];
    opportunityOptions: MarketFilterOption[];
  }
) {
  const next = {
    ...filters,
    sport: optionValueOrAll(filters.sport, options.sportOptions),
    provider: optionValueOrAll(filters.provider, options.providerOptions),
    providerCategory: optionValueOrAll(filters.providerCategory, options.providerCategoryOptions),
    trust: optionValueOrAll(filters.trust, options.trustOptions),
    opportunity: optionValueOrAll(filters.opportunity, options.opportunityOptions),
    canAddOnly: filters.canAddOnly
  };

  return shallowEqualFilters(filters, next) ? filters : next;
}

function optionValueOrAll(value: string, options: MarketFilterOption[]) {
  return options.some((option) => option.value === value) ? value : 'All';
}

function shallowEqualFilters(left: MarketFilterValues, right: MarketFilterValues) {
  return Object.keys(left).every((key) => left[key as keyof MarketFilterValues] === right[key as keyof MarketFilterValues]);
}

function marketMatchesFilters(market: MarketOpportunity, filters: MarketFilterValues) {
  if (filters.sport !== 'All' && market.sport !== filters.sport) return false;
  if (filters.provider !== 'All' && market.provider !== filters.provider) return false;
  if (filters.providerCategory !== 'All' && categoryFilterValue(market.providerCategory) !== filters.providerCategory) return false;
  if (filters.trust !== 'All' && trustFilterValue(market) !== filters.trust) return false;
  if (filters.opportunity !== 'All' && marketTagValue(market) !== filters.opportunity) return false;
  if (filters.canAddOnly && !isPlayableMarket(market)) return false;

  const query = filters.search.trim().toLowerCase();
  if (!query) return true;

  return [market.player, market.team, market.opponent, market.event, market.propType, market.provider, market.side]
    .filter(Boolean)
    .some((value) => String(value).toLowerCase().includes(query));
}

function sortMarkets(markets: MarketOpportunity[], sort: string) {
  const rows = [...markets];
  switch (sort) {
    case 'Edge':
      return rows.sort((a, b) => b.probabilityEdge - a.probabilityEdge);
    case 'HitChance':
      return rows.sort((a, b) => b.modelProbability - a.modelProbability);
    case 'Trust':
      return rows.sort((a, b) => trustRank(b) - trustRank(a) || b.estimatedEv - a.estimatedEv);
    case 'PlayerAz':
      return rows.sort((a, b) => a.player.localeCompare(b.player));
    case 'StartsSoon':
      return rows.sort((a, b) => Date.parse(a.startTime || '') - Date.parse(b.startTime || ''));
    case 'EV':
    default:
      return rows.sort((a, b) => b.estimatedEv - a.estimatedEv);
  }
}

function categoryFilterValue(category: string) {
  return category === 'Sportsbook' ? 'Reference only' : category;
}

function categoryFilterLabel(value: string) {
  return value;
}

function trustFilterValue(market: MarketOpportunity) {
  if (market.trustLevel === 'Strong') return 'High';
  if (market.trustLevel === 'Medium') return 'Medium';
  return 'Low';
}

function trustFilterLabel(value: string) {
  return value;
}

function trustRank(market: MarketOpportunity) {
  const value = trustFilterValue(market);
  if (value === 'High') return 3;
  if (value === 'Medium') return 2;
  return 1;
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


