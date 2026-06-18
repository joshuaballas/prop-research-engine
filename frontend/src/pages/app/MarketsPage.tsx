import { Box, Button, Grid, Stack, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import { MarketCard } from '../../components/markets/MarketCard';
import { MarketFilters, type MarketFilterOption, type MarketFilterValues } from '../../components/markets/MarketFilters';
import { MarketTable } from '../../components/markets/MarketTable';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { useMarketScannerData } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
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
  const bestEv = dfsMarkets.length ? Math.max(...dfsMarkets.map((market) => market.estimatedEv)) : 0;
  const dfsProviderCount = new Set(dfsMarkets.map((market) => market.provider)).size;
  const lowSampleRows = filteredMarkets.filter((market) => market.historicalSampleSize < 35).length;

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
      <PageHeader title="Markets" description="Browse current provider lines with hit chance, edge, trust, and entry status." />

      {isMobile ? (
        <MobileSummaryStrip
          marketCount={filteredMarkets.length}
          dfsProviderCount={dfsProviderCount}
          bestEv={bestEv}
          lowSampleRows={lowSampleRows}
        />
      ) : (
        <Grid container spacing={1.5}>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Active lines" value={String(filteredMarkets.length)} helper="Lines matching filters" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Providers" value={String(dfsProviderCount)} helper="Entry-supported platforms" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Best EV" value={formatPercent(bestEv, { signed: true })} helper="Highest-EV line" tone="success" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Low-sample lines" value={String(lowSampleRows)} helper="Use extra caution" tone="warning" />
          </Grid>
        </Grid>
      )}

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

      <SectionHeader title="Line scanner" description="Review available lines, open Details for player history, and add eligible picks to an entry." />
      {marketsQuery.loading ? (
        <ApiStateCard message="Loading market scanner rows..." />
      ) : marketsQuery.error ? (
        <ApiStateCard tone="error" message="Market scanner rows could not be loaded from the market API." />
      ) : isMobile ? (
        sortedMarkets.length ? (
          <>
            <Grid container spacing={2}>{visibleMarkets.map((market) => <Grid key={market.id} size={12}><MarketCard market={market} /></Grid>)}</Grid>
            <MarketsPagination count={sortedMarkets.length} page={page} pageSize={pageSize} onChange={setPage} />
          </>
        ) : <ApiStateCard message="No markets match these filters." />
      ) : sortedMarkets.length ? (
        <>
          <MarketTable markets={visibleMarkets} />
          <MarketsPagination count={sortedMarkets.length} page={page} pageSize={pageSize} onChange={setPage} />
        </>
      ) : <ApiStateCard message="No markets match these filters." />}
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
        bgcolor: 'rgba(15,23,42,.68)',
        p: 2
      }}
    >
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </Box>
  );
}

function MobileSummaryStrip({
  marketCount,
  dfsProviderCount,
  bestEv,
  lowSampleRows
}: {
  marketCount: number;
  dfsProviderCount: number;
  bestEv: number;
  lowSampleRows: number;
}) {
  const items = [
    { value: String(marketCount), label: 'Markets' },
    { value: String(dfsProviderCount), label: 'DFS providers' },
    { value: formatPercent(bestEv, { signed: true }), label: 'Best EV', tone: 'success' },
    { value: String(lowSampleRows), label: 'Low sample', tone: 'warning' }
  ];

  return (
    <Box
      sx={{
        border: '1px solid rgba(148,163,184,.16)',
        borderRadius: 3,
        bgcolor: 'rgba(15,23,42,.62)',
        px: 1.2,
        py: 1
      }}
    >
      <Grid container spacing={0.8}>
        {items.map((item) => (
          <Grid key={item.label} size={6}>
            <Stack spacing={0.15} alignItems="center" textAlign="center">
              <Typography variant="body1" color={item.tone === 'success' ? 'success.main' : item.tone === 'warning' ? 'warning.main' : 'text.primary'} fontWeight={950}>
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

function SummaryMetric({
  label,
  value,
  helper,
  tone = 'default'
}: {
  label: string;
  value: string;
  helper: string;
  tone?: 'default' | 'success' | 'warning';
}) {
  return (
    <Box
      sx={{
        height: '100%',
        border: '1px solid rgba(148,163,184,.16)',
        borderRadius: 3,
        bgcolor: 'rgba(15,23,42,.72)',
        p: 1.7
      }}
    >
      <Stack spacing={0.35}>
        <Typography variant="caption" color="text.secondary" fontWeight={850} textTransform="uppercase">
          {label}
        </Typography>
        <Typography variant="h5" color={tone === 'success' ? 'success.main' : tone === 'warning' ? 'warning.main' : 'text.primary'} fontWeight={950}>
          {value}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {helper}
        </Typography>
      </Stack>
    </Box>
  );
}

function MarketsPagination({
  count,
  page,
  pageSize,
  onChange
}: {
  count: number;
  page: number;
  pageSize: number;
  onChange: (page: number) => void;
}) {
  if (count <= pageSize) return null;

  const pageCount = Math.ceil(count / pageSize);
  const start = (page - 1) * pageSize + 1;
  const end = Math.min(page * pageSize, count);
  const pages = getPageWindow(page, pageCount);

  const handlePageChange = (nextPage: number) => {
    if (nextPage < 1 || nextPage > pageCount || nextPage === page) return;
    onChange(nextPage);
  };

  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={{ xs: 1.4, sm: 1.2 }}
      alignItems={{ xs: 'stretch', sm: 'center' }}
      justifyContent="space-between"
      sx={{
        borderTop: '1px solid rgba(148,163,184,.14)',
        pt: { xs: 2, sm: 1.5 },
        pb: { xs: 2.5, sm: 0 }
      }}
    >
      <Typography variant="body2" color="text.secondary" fontWeight={750} textAlign={{ xs: 'center', sm: 'left' }}>
        Showing {start}-{end} of {count} markets
      </Typography>
      <Stack
        direction="row"
        spacing={0.65}
        alignItems="center"
        justifyContent={{ xs: 'center', sm: 'flex-end' }}
        sx={{
          overflowX: 'auto',
          scrollbarWidth: 'none',
          msOverflowStyle: 'none',
          '&::-webkit-scrollbar': {
            display: 'none'
          }
        }}
      >
        <PaginationButton label="<" disabled={page === 1} onClick={() => handlePageChange(page - 1)} ariaLabel="Previous markets page" />
        {pages.map((pageNumber) => (
          <PaginationButton
            key={pageNumber}
            label={String(pageNumber)}
            active={pageNumber === page}
            onClick={() => handlePageChange(pageNumber)}
            ariaLabel={`Markets page ${pageNumber}`}
          />
        ))}
        <PaginationButton label=">" disabled={page === pageCount} onClick={() => handlePageChange(page + 1)} ariaLabel="Next markets page" />
      </Stack>
    </Stack>
  );
}

function getPageWindow(page: number, pageCount: number) {
  const windowSize = Math.min(4, pageCount);
  let start = Math.max(1, page - Math.floor(windowSize / 2));
  const end = Math.min(pageCount, start + windowSize - 1);
  start = Math.max(1, end - windowSize + 1);

  return Array.from({ length: end - start + 1 }, (_, index) => start + index);
}

function PaginationButton({
  label,
  active = false,
  disabled = false,
  onClick,
  ariaLabel
}: {
  label: string;
  active?: boolean;
  disabled?: boolean;
  onClick: () => void;
  ariaLabel: string;
}) {
  return (
    <Button
      type="button"
      variant="outlined"
      disabled={disabled}
      aria-label={ariaLabel}
      aria-current={active ? 'page' : undefined}
      onMouseDown={(event) => event.preventDefault()}
      onClick={onClick}
      sx={{
        minWidth: 34,
        width: 34,
        height: 32,
        px: 0,
        borderRadius: 2,
        color: active ? 'text.primary' : 'text.secondary',
        borderColor: active ? 'rgba(34,211,238,.52)' : 'rgba(148,163,184,.18)',
        bgcolor: active ? 'rgba(8,145,178,.22)' : 'rgba(15,23,42,.48)',
        fontSize: 12,
        fontWeight: 850,
        lineHeight: 1,
        flex: '0 0 auto',
        '&:hover': {
          borderColor: active ? 'rgba(34,211,238,.62)' : 'rgba(148,163,184,.3)',
          bgcolor: active ? 'rgba(8,145,178,.26)' : 'rgba(34,211,238,.08)'
        },
        '&.Mui-disabled': {
          color: 'rgba(148,163,184,.34)',
          borderColor: 'rgba(148,163,184,.12)',
          bgcolor: 'rgba(15,23,42,.28)'
        }
      }}
    >
      {label}
    </Button>
  );
}
