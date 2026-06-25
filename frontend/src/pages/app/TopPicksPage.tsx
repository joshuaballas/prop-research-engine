import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { Box, Button, Collapse, Grid, MenuItem, Stack, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import { getTopPickConfidence, TopPickCard } from '../../components/markets/TopPickCard';
import { MetricLabelWithHelp } from '../../components/metrics/MetricLabelWithHelp';
import { TrustValue } from '../../components/metrics/TrustValue';
import { PropDetailsModal } from '../../components/prop-history/PropDetailsModal';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { PageHeader } from '../../components/ui/PageHeader';
import { appTableRowSx, stickyActionCellSx, stickyActionHeaderCellSx } from '../../components/ui/appTableStyles';
import { lockedActionButtonSx, removeActionButtonSx } from '../../components/ui/buttonStyles';
import { AppDataTable } from '../../components/ui/AppDataTable';
import { AppRowActions, desktopRowActionButtonSx, desktopRowDetailsButtonSx } from '../../components/ui/AppRowActions';
import { AppTablePagination } from '../../components/ui/AppTablePagination';
import { AppTableMetricValue, AppTablePrimaryText, AppTableSecondaryText } from '../../components/ui/AppTableTypography';
import { OpportunityPickText } from '../../components/ui/OpportunityCard';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { useTopPicksData } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { palette } from '../../theme/palette';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { marketTagLabel, marketTagValue } from '../../utils/marketTags';
import { buildAvailableSportOptions, getAvailableSportCodes } from '../../utils/sportOptions';
import { normalizeTrustLevel } from '../../utils/trustColors';

type TopPickFilterValues = {
  search: string;
  sport: string;
  provider: string;
  opportunity: string;
  trust: string;
  sort: string;
};

const defaultTopPickFilters: TopPickFilterValues = {
  search: '',
  sport: 'All',
  provider: 'All',
  opportunity: 'All',
  trust: 'All',
  sort: 'Edge'
};

export function TopPicksPage() {
  const builder = useEntryBuilder();
  const { isMobile } = useResponsiveView();
  const [filters, setFilters] = useState<TopPickFilterValues>(defaultTopPickFilters);
  const [page, setPage] = useState(1);
  const topPicksQuery = useTopPicksData(30, 'top-pick');
  const topPicks = topPicksQuery.markets;
  const availableSportCodes = useMemo(() => getAvailableSportCodes(topPicks), [topPicks]);
  const sportFilterLabels = useMemo(() => {
    const labels = buildAvailableSportOptions(availableSportCodes).map((sport) => sport.label);
    return labels.length ? labels : ['All'];
  }, [availableSportCodes]);
  const providerOptions = useMemo(() => buildStringOptions(topPicks.map((market) => market.provider)), [topPicks]);
  const opportunityOptions = useMemo(
    () => buildStringOptions(topPicks.map((market) => marketTagValue(market)), marketTagLabel),
    [topPicks]
  );
  const showOpportunityFilter = opportunityOptions.length > 2;
  const visibleTopPicks = useMemo(() => sortTopPicks(filterTopPicks(topPicks, filters), filters.sort), [filters, topPicks]);
  const pageSize = isMobile ? 8 : 20;
  const pageCount = Math.max(1, Math.ceil(visibleTopPicks.length / pageSize));
  const pagedTopPicks = useMemo(() => {
    const start = (page - 1) * pageSize;
    return visibleTopPicks.slice(start, start + pageSize);
  }, [page, pageSize, visibleTopPicks]);
  const featured = pagedTopPicks.slice(0, 3);

  useEffect(() => {
    setPage(1);
  }, [filters, pageSize]);

  useEffect(() => {
    setPage((current) => Math.min(current, pageCount));
  }, [pageCount]);

  return (
    <Stack spacing={{ xs: 2, md: 3 }}>
      <PageHeader title="Top Picks" description="Curated picks ranked by model edge, hit chance, trust, provider fit, and freshness." />
      <TopPickControls
        isMobile={isMobile}
        filters={filters}
        onFiltersChange={setFilters}
        sportOptions={sportFilterLabels}
        providerOptions={providerOptions}
        opportunityOptions={opportunityOptions}
        showOpportunityFilter={showOpportunityFilter}
      />

      {topPicksQuery.loading ? (
        <ApiStateCard message="Loading latest top picks..." />
      ) : topPicksQuery.error ? (
        <ApiStateCard tone="error" message="Top picks could not be loaded from the market API." />
      ) : topPicks.length === 0 ? (
        <ApiStateCard message="No top picks are available from the latest market snapshot." />
      ) : visibleTopPicks.length === 0 ? (
        <ApiStateCard message="No top picks match these filters." />
      ) : isMobile ? (
        <>
          <Stack spacing={1.35}>
            {pagedTopPicks.map((market) => (
              <TopPickCard
                key={market.id}
                market={market}
                variant="compact"
                selected={builder.isSelected(market.id)}
                incompatible={!builder.isSelected(market.id) && !builder.isCompatible(market)}
                onAdd={builder.toggleLeg}
              />
            ))}
          </Stack>
          <AppTablePagination count={visibleTopPicks.length} page={page} pageSize={pageSize} itemLabel="picks" onChange={setPage} />
        </>
      ) : (
        <>
          <Stack spacing={1.4}>
            <Typography variant="h5" fontWeight={950}>
              Featured Top Picks
            </Typography>
            <Grid container spacing={1.5}>
              {featured.map((market) => (
                <Grid key={market.id} size={{ xs: 12, lg: 4 }} sx={{ display: 'flex' }}>
                  <TopPickCard
                    market={market}
                    variant="featured"
                    selected={builder.isSelected(market.id)}
                    incompatible={!builder.isSelected(market.id) && !builder.isCompatible(market)}
                    onAdd={builder.toggleLeg}
                  />
                </Grid>
              ))}
            </Grid>
          </Stack>

          <Stack spacing={1.4}>
            <Typography variant="h5" fontWeight={950}>
              All Ranked Picks
            </Typography>
            <RankedPicksTable markets={pagedTopPicks} rankOffset={(page - 1) * pageSize} builder={builder} />
            <AppTablePagination count={visibleTopPicks.length} page={page} pageSize={pageSize} itemLabel="picks" onChange={setPage} />
          </Stack>
        </>
      )}
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

function buildStringOptions(values: string[], labelForValue?: (value: string) => string) {
  const uniqueValues = Array.from(new Set(values.filter(Boolean))).sort((a, b) => (labelForValue?.(a) ?? a).localeCompare(labelForValue?.(b) ?? b));
  return ['All', ...uniqueValues.map((value) => labelForValue?.(value) ?? value)];
}

function filterTopPicks(markets: MarketOpportunity[], filters: TopPickFilterValues) {
  const query = filters.search.trim().toLowerCase();

  return markets.filter((market) => {
    if (query && !market.player.toLowerCase().includes(query)) return false;
    if (filters.sport !== 'All' && market.sport !== filters.sport) return false;
    if (filters.provider !== 'All' && market.provider !== filters.provider) return false;
    if (filters.opportunity !== 'All' && marketTagLabel(marketTagValue(market)) !== filters.opportunity) return false;
    if (filters.trust !== 'All' && normalizeTrustLevel(getTopPickConfidence(market)) !== filters.trust) return false;
    return true;
  });
}

function sortTopPicks(markets: MarketOpportunity[], sort: string) {
  const rows = [...markets];

  switch (sort) {
    case 'Edge':
      return rows.sort((a, b) => b.probabilityEdge - a.probabilityEdge);
    case 'Hit chance':
      return rows.sort((a, b) => b.modelProbability - a.modelProbability);
    case 'Trust':
      return rows.sort((a, b) => trustSortRank(b) - trustSortRank(a) || b.probabilityEdge - a.probabilityEdge);
    case 'Rank':
      return rows.sort((a, b) => (a.rank ?? Number.MAX_SAFE_INTEGER) - (b.rank ?? Number.MAX_SAFE_INTEGER));
    case 'Sample':
      return rows.sort((a, b) => b.historicalSampleSize - a.historicalSampleSize);
    default:
      return rows.sort((a, b) => b.probabilityEdge - a.probabilityEdge);
  }
}

function trustSortRank(market: MarketOpportunity) {
  const trust = normalizeTrustLevel(getTopPickConfidence(market));
  if (trust === 'High') return 3;
  if (trust === 'Medium') return 2;
  return 1;
}

function TopPickControls({
  isMobile,
  filters,
  onFiltersChange,
  sportOptions,
  providerOptions,
  opportunityOptions,
  showOpportunityFilter
}: {
  isMobile: boolean;
  filters: TopPickFilterValues;
  onFiltersChange: (filters: TopPickFilterValues) => void;
  sportOptions: string[];
  providerOptions: string[];
  opportunityOptions: string[];
  showOpportunityFilter: boolean;
}) {
  const [filtersOpen, setFiltersOpen] = useState(false);
  const updateFilter = (key: keyof TopPickFilterValues, value: string) => {
    onFiltersChange({ ...filters, [key]: value });
  };
  const clearFilters = () => onFiltersChange(defaultTopPickFilters);
  const sortOptions = ['Edge', 'Hit chance', 'Trust', 'Rank', 'Sample'];

  if (isMobile) {
    return (
      <Box
        sx={{
          border: '1px solid rgba(148,163,184,.14)',
          borderRadius: 3,
          bgcolor: palette.cardSurface,
          p: 1
        }}
      >
        <Stack spacing={1}>
          <Stack direction="row" spacing={1} alignItems="center">
            <TextField
              label="Search player"
              size="small"
              fullWidth
              value={filters.search}
              onChange={(event) => updateFilter('search', event.target.value)}
            />
            <Button
              startIcon={<TuneRoundedIcon />}
              variant={filtersOpen ? 'contained' : 'outlined'}
              size="small"
              onClick={() => setFiltersOpen((open) => !open)}
              sx={{ height: 40, minWidth: 104, whiteSpace: 'nowrap' }}
            >
              Filters
            </Button>
          </Stack>
          <TextField label="Sort by" size="small" select value={filters.sort} onChange={(event) => updateFilter('sort', event.target.value)} fullWidth>
            {sortOptions.map((item) => (
              <MenuItem key={item} value={item}>
                {item}
              </MenuItem>
            ))}
          </TextField>
          <Collapse in={filtersOpen} timeout="auto" unmountOnExit>
            <Stack spacing={1} sx={{ pt: 0.25 }}>
              <TextField label="Sport" size="small" select value={filters.sport} onChange={(event) => updateFilter('sport', event.target.value)} fullWidth>
                {sportOptions.map((item) => (
                  <MenuItem key={item} value={item}>
                    {item}
                  </MenuItem>
                ))}
              </TextField>
              <TextField label="Provider" size="small" select value={filters.provider} onChange={(event) => updateFilter('provider', event.target.value)} fullWidth>
                {providerOptions.map((item) => (
                  <MenuItem key={item} value={item}>
                    {item}
                  </MenuItem>
                ))}
              </TextField>
              {showOpportunityFilter ? (
                <TextField label="Opportunity" size="small" select value={filters.opportunity} onChange={(event) => updateFilter('opportunity', event.target.value)} fullWidth>
                  {opportunityOptions.map((item) => (
                    <MenuItem key={item} value={item}>
                      {item}
                    </MenuItem>
                  ))}
                </TextField>
              ) : null}
              <TextField label="Trust" size="small" select value={filters.trust} onChange={(event) => updateFilter('trust', event.target.value)} fullWidth>
                {['All', 'High', 'Medium', 'Low'].map((item) => (
                  <MenuItem key={item} value={item}>
                    {item}
                  </MenuItem>
                ))}
              </TextField>
              <Button variant="text" size="small" onClick={clearFilters} sx={{ alignSelf: 'flex-start', color: 'text.secondary', fontWeight: 850 }}>
                Clear filters
              </Button>
            </Stack>
          </Collapse>
        </Stack>
      </Box>
    );
  }

  return (
    <Box
      sx={{
        borderBottom: '1px solid rgba(148,163,184,.14)',
        pb: 1.35
      }}
    >
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
        <TextField
          label="Search player"
          size="small"
          fullWidth
          value={filters.search}
          onChange={(event) => updateFilter('search', event.target.value)}
          sx={{ maxWidth: 360, minWidth: 300 }}
        />
        <TextField label="Sport" size="small" select value={filters.sport} onChange={(event) => updateFilter('sport', event.target.value)} sx={{ width: 122 }}>
          {sportOptions.map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField label="Provider" size="small" select value={filters.provider} onChange={(event) => updateFilter('provider', event.target.value)} sx={{ width: 168 }}>
          {providerOptions.map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        {showOpportunityFilter ? (
          <TextField label="Opportunity" size="small" select value={filters.opportunity} onChange={(event) => updateFilter('opportunity', event.target.value)} sx={{ width: 138 }}>
            {opportunityOptions.map((item) => (
              <MenuItem key={item} value={item}>
                {item}
              </MenuItem>
            ))}
          </TextField>
        ) : null}
        <TextField label="Trust" size="small" select value={filters.trust} onChange={(event) => updateFilter('trust', event.target.value)} sx={{ width: 132 }}>
          {['All', 'High', 'Medium', 'Low'].map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField label="Sort by" size="small" select value={filters.sort} onChange={(event) => updateFilter('sort', event.target.value)} sx={{ width: 126 }}>
          {sortOptions.map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <Button
          variant="text"
          size="small"
          onClick={clearFilters}
          sx={{
            ml: 'auto',
            minWidth: 92,
            height: 38,
            color: 'text.secondary',
            fontWeight: 850,
            '&:hover': { bgcolor: 'rgba(148,163,184,.07)', color: 'text.primary' }
          }}
        >
          Clear filters
        </Button>
      </Stack>
    </Box>
  );
}

function RankedPicksTable({ markets, rankOffset, builder }: { markets: MarketOpportunity[]; rankOffset: number; builder: ReturnType<typeof useEntryBuilder> }) {
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);

  return (
    <>
      <AppDataTable minWidth={1040}>
          <TableHead>
            <TableRow>
              <TableCell>Rank</TableCell>
              <TableCell>Opportunity</TableCell>
              <TableCell>Provider</TableCell>
              <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
              <TableCell align="right"><MetricLabelWithHelp metric="hitChance" align="right" variant="table" /></TableCell>
              <TableCell><MetricLabelWithHelp metric="trust" variant="table" /></TableCell>
              <TableCell align="center" sx={stickyActionHeaderCellSx}>Action</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {markets.map((market, index) => {
              const confidence = getTopPickConfidence(market);
              const selected = builder.isSelected(market.id);
              const incompatible = !selected && !builder.isCompatible(market);
              return (
              <TableRow key={market.id} hover sx={appTableRowSx}>
                <TableCell>
                  <AppTablePrimaryText>#{rankOffset + index + 1}</AppTablePrimaryText>
                </TableCell>
                <TableCell sx={{ minWidth: 230 }}>
                  <Stack spacing={0.3}>
                    <AppTablePrimaryText>{market.player}</AppTablePrimaryText>
                    <OpportunityPickText
                      side={market.side}
                      propType={market.propType}
                      line={market.line}
                      sport={market.sport}
                      sx={{
                        fontSize: 13,
                        flexWrap: 'nowrap',
                        overflow: 'hidden',
                        '& .MuiTypography-root': { fontSize: 'inherit' }
                      }}
                    />
                    {market.event || market.startTime ? (
                      <AppTableSecondaryText>
                        {[market.event, market.startTime].filter(Boolean).join(' - ')}
                      </AppTableSecondaryText>
                    ) : null}
                  </Stack>
                </TableCell>
                <TableCell sx={{ minWidth: 160 }}>
                  <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
                </TableCell>
                <TableCell align="right"><AppTableMetricValue>{formatPercent(market.probabilityEdge, { signed: true })}</AppTableMetricValue></TableCell>
                <TableCell align="right"><AppTableMetricValue>{formatPercent(market.modelProbability)}</AppTableMetricValue></TableCell>
                <TableCell>
                  <TrustValue level={confidence} />
                </TableCell>
                <TableCell data-sticky-action="true" align="center" sx={{ ...stickyActionCellSx, minWidth: 130 }}>
                  <AppRowActions sx={{ mx: 'auto' }}>
                    <Button
                      startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
                      size="small"
                      variant="contained"
                      disabled={!market.canAddToEntry || incompatible}
                      onClick={() => builder.toggleLeg(market)}
                      sx={entryButtonSx(selected, incompatible)}
                    >
                      {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
                    </Button>
                    <Button size="small" variant="outlined" onClick={() => setDetailsMarket(market)} sx={compareButtonSx}>
                      Details
                    </Button>
                  </AppRowActions>
                </TableCell>
              </TableRow>
              );
            })}
          </TableBody>
      </AppDataTable>
      <PropDetailsModal market={detailsMarket} open={Boolean(detailsMarket)} onClose={() => setDetailsMarket(null)} />
    </>
  );
}

const actionButtonSx = desktopRowActionButtonSx;

function entryButtonSx(selected: boolean, incompatible: boolean) {
  if (selected) {
    return {
      ...actionButtonSx,
      ...removeActionButtonSx
    };
  }

  if (incompatible) {
    return {
      ...actionButtonSx,
      ...lockedActionButtonSx
    };
  }

  return actionButtonSx;
}

const compareButtonSx = {
  ...desktopRowDetailsButtonSx
};
