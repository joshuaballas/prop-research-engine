import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { Box, Button, Collapse, Grid, MenuItem, Stack, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import { TopPickCard } from '../../components/markets/TopPickCard';
import { MetricLabelWithHelp } from '../../components/metrics/MetricLabelWithHelp';
import { PropDetailsModal } from '../../components/prop-history/PropDetailsModal';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { PageHeader } from '../../components/ui/PageHeader';
import { appTableRowSx, stickyActionCellSx, stickyActionHeaderCellSx } from '../../components/ui/appTableStyles';
import { lockedActionButtonSx, removeActionButtonSx } from '../../components/ui/buttonStyles';
import { AppDataTable } from '../../components/ui/AppDataTable';
import { AppRowActions, desktopRowActionButtonSx, desktopRowDetailsButtonSx } from '../../components/ui/AppRowActions';
import { AppTablePagination } from '../../components/ui/AppTablePagination';
import { AppTableMetricValue, AppTablePrimaryText, AppTableSecondaryText } from '../../components/ui/AppTableTypography';
import { MarketTableSkeleton, TopPickCardSkeleton, TopPickSkeletonGrid } from '../../components/ui/LoadingSkeletons';
import { OpportunityPickText } from '../../components/ui/OpportunityCard';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { useTopPicksData } from '../../hooks/useMarketData';
import type { MarketReadSport } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { palette } from '../../theme/palette';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { marketDisplayLabel } from '../../utils/marketDisplay';
import { buildAvailableSportOptions, getAvailableSportCodes } from '../../utils/sportOptions';

type TopPickFilterValues = {
  search: string;
  sport: string;
  providerCategory: string;
  provider: string;
  sort: string;
};

type SelectOption = {
  label: string;
  value: string;
};

const defaultTopPickFilters: TopPickFilterValues = {
  search: '',
  sport: 'All',
  providerCategory: 'All',
  provider: 'All',
  sort: 'Hit chance'
};

export function TopPicksPage() {
  const builder = useEntryBuilder();
  const { isMobile } = useResponsiveView();
  const [filters, setFilters] = useState<TopPickFilterValues>(defaultTopPickFilters);
  const [page, setPage] = useState(1);
  const topPicksQuery = useTopPicksData(500, 'top-pick', toMarketReadSport(filters.sport));
  const topPicks = topPicksQuery.markets;
  const availableSportCodes = useMemo(() => getAvailableSportCodes(topPicks), [topPicks]);
  const sportFilterOptions = useMemo<SelectOption[]>(() => {
    const options = buildAvailableSportOptions(availableSportCodes).map((sport) => ({
      label: sport.label,
      value: sport.code === 'ALL' ? 'All' : sport.code
    }));
    return options.length ? options : [{ label: 'All', value: 'All' }];
  }, [availableSportCodes]);
  const providerOptions = useMemo(() => buildStringOptions(topPicks.map((market) => market.provider)), [topPicks]);
  const providerCategoryOptions = useMemo(() => buildStringOptions(topPicks.map((market) => market.providerCategory)), [topPicks]);
  const visibleTopPicks = useMemo(() => sortTopPicks(filterTopPicks(topPicks, filters), filters.sort), [filters, topPicks]);
  const rankedTopPicks = useMemo(() => applyDisplayRanks(visibleTopPicks), [visibleTopPicks]);
  const pageSize = isMobile ? 8 : 20;
  const pageCount = Math.max(1, Math.ceil(rankedTopPicks.length / pageSize));
  const pagedTopPicks = useMemo(() => {
    const start = (page - 1) * pageSize;
    return rankedTopPicks.slice(start, start + pageSize);
  }, [page, pageSize, rankedTopPicks]);
  const featured = pagedTopPicks.slice(0, 3);

  useEffect(() => {
    setPage(1);
  }, [filters, pageSize]);

  useEffect(() => {
    setPage((current) => Math.min(current, pageCount));
  }, [pageCount]);

  return (
    <Stack spacing={{ xs: 2, md: 3 }}>
      <PageHeader title="Top Picks" description="Curated props ranked by likely hit chance, line quality, provider fit, and freshness." />
      <TopPickControls
        isMobile={isMobile}
        filters={filters}
        onFiltersChange={setFilters}
        sportOptions={sportFilterOptions}
        providerOptions={providerOptions}
        providerCategoryOptions={providerCategoryOptions}
      />

      {topPicksQuery.loading ? (
        isMobile ? (
          <Stack spacing={1.35} aria-busy="true">
            {Array.from({ length: 6 }).map((_, index) => (
              <TopPickCardSkeleton key={index} />
            ))}
          </Stack>
        ) : (
          <>
            <Stack spacing={1.4}>
              <Typography variant="h5" fontWeight={950}>
                Featured Top Picks
              </Typography>
              <TopPickSkeletonGrid count={3} />
            </Stack>
            <Stack spacing={1.4}>
              <Typography variant="h5" fontWeight={950}>
                All Ranked Picks
              </Typography>
              <MarketTableSkeleton rows={8} columns={6} minWidth={1040} />
            </Stack>
          </>
        )
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
          <AppTablePagination count={rankedTopPicks.length} page={page} pageSize={pageSize} itemLabel="picks" onChange={setPage} />
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
            <RankedPicksTable markets={pagedTopPicks} builder={builder} />
            <AppTablePagination count={rankedTopPicks.length} page={page} pageSize={pageSize} itemLabel="picks" onChange={setPage} />
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

function toMarketReadSport(value: string): MarketReadSport {
  return value === 'MLB' || value === 'SOCCER' ? value : 'All';
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
    if (filters.providerCategory !== 'All' && market.providerCategory !== filters.providerCategory) return false;
    if (filters.provider !== 'All' && market.provider !== filters.provider) return false;
    return true;
  });
}

function sortTopPicks(markets: MarketOpportunity[], sort: string) {
  const rows = [...markets];

  switch (sort) {
    case 'Edge':
      return rows.sort((a, b) => (b.probabilityEdge ?? Number.NEGATIVE_INFINITY) - (a.probabilityEdge ?? Number.NEGATIVE_INFINITY));
    case 'Hit chance':
      return rows.sort((a, b) => (b.modelProbability ?? Number.NEGATIVE_INFINITY) - (a.modelProbability ?? Number.NEGATIVE_INFINITY));
    case 'Rank':
      return rows.sort((a, b) => (a.rank ?? Number.MAX_SAFE_INTEGER) - (b.rank ?? Number.MAX_SAFE_INTEGER));
    default:
      return rows.sort((a, b) =>
        (b.topPickScore ?? Number.NEGATIVE_INFINITY) - (a.topPickScore ?? Number.NEGATIVE_INFINITY) ||
        (b.modelProbability ?? Number.NEGATIVE_INFINITY) - (a.modelProbability ?? Number.NEGATIVE_INFINITY)
      );
  }
}

function applyDisplayRanks(markets: MarketOpportunity[]) {
  return markets.map((market, index) => ({
    ...market,
    displayRank: index + 1,
    sourceRank: market.rank
  }));
}

function TopPickControls({
  isMobile,
  filters,
  onFiltersChange,
  sportOptions,
  providerOptions,
  providerCategoryOptions
}: {
  isMobile: boolean;
  filters: TopPickFilterValues;
  onFiltersChange: (filters: TopPickFilterValues) => void;
  sportOptions: SelectOption[];
  providerOptions: string[];
  providerCategoryOptions: string[];
}) {
  const [filtersOpen, setFiltersOpen] = useState(false);
  const updateFilter = (key: keyof TopPickFilterValues, value: string) => {
    onFiltersChange({ ...filters, [key]: value });
  };
  const clearFilters = () => onFiltersChange(defaultTopPickFilters);
  const sortOptions = ['Hit chance', 'Edge', 'Rank'];

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
              id="top-picks-mobile-search"
              name="top-picks-mobile-search"
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
          <TextField id="top-picks-mobile-sort" name="top-picks-mobile-sort" label="Sort by" size="small" select value={filters.sort} onChange={(event) => updateFilter('sort', event.target.value)} fullWidth>
            {sortOptions.map((item) => (
              <MenuItem key={item} value={item}>
                {item}
              </MenuItem>
            ))}
          </TextField>
          <Collapse in={filtersOpen} timeout="auto" unmountOnExit>
            <Stack spacing={1} sx={{ pt: 0.25 }}>
              <TextField id="top-picks-mobile-sport" name="top-picks-mobile-sport" label="Sport" size="small" select value={filters.sport} onChange={(event) => updateFilter('sport', event.target.value)} fullWidth>
                {sportOptions.map((item) => (
                  <MenuItem key={item.value} value={item.value}>
                    {item.label}
                  </MenuItem>
                ))}
              </TextField>
              <TextField id="top-picks-mobile-provider" name="top-picks-mobile-provider" label="Provider" size="small" select value={filters.provider} onChange={(event) => updateFilter('provider', event.target.value)} fullWidth>
                {providerOptions.map((item) => (
                  <MenuItem key={item} value={item}>
                    {item}
                  </MenuItem>
                ))}
              </TextField>
              <TextField id="top-picks-mobile-provider-category" name="top-picks-mobile-provider-category" label="Category" size="small" select value={filters.providerCategory} onChange={(event) => updateFilter('providerCategory', event.target.value)} fullWidth>
                {providerCategoryOptions.map((item) => (
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
          id="top-picks-desktop-search"
          name="top-picks-desktop-search"
          label="Search player"
          size="small"
          fullWidth
          value={filters.search}
          onChange={(event) => updateFilter('search', event.target.value)}
          sx={{ maxWidth: 360, minWidth: 300 }}
        />
        <TextField id="top-picks-desktop-sport" name="top-picks-desktop-sport" label="Sport" size="small" select value={filters.sport} onChange={(event) => updateFilter('sport', event.target.value)} sx={{ width: 122 }}>
          {sportOptions.map((item) => (
            <MenuItem key={item.value} value={item.value}>
              {item.label}
            </MenuItem>
          ))}
        </TextField>
        <TextField id="top-picks-desktop-provider" name="top-picks-desktop-provider" label="Provider" size="small" select value={filters.provider} onChange={(event) => updateFilter('provider', event.target.value)} sx={{ width: 168 }}>
          {providerOptions.map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField id="top-picks-desktop-provider-category" name="top-picks-desktop-provider-category" label="Category" size="small" select value={filters.providerCategory} onChange={(event) => updateFilter('providerCategory', event.target.value)} sx={{ width: 132 }}>
          {providerCategoryOptions.map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField id="top-picks-desktop-sort" name="top-picks-desktop-sort" label="Sort by" size="small" select value={filters.sort} onChange={(event) => updateFilter('sort', event.target.value)} sx={{ width: 126 }}>
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

function RankedPicksTable({ markets, builder }: { markets: MarketOpportunity[]; builder: ReturnType<typeof useEntryBuilder> }) {
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
              <TableCell align="center" sx={stickyActionHeaderCellSx}>Action</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {markets.map((market, index) => {
              const selected = builder.isSelected(market.id);
              const incompatible = !selected && !builder.isCompatible(market);
              const entryDisabled = isEntryBuilderDisabled(market) && !selected;
              const locked = incompatible || entryDisabled;
              return (
              <TableRow key={market.id} hover sx={appTableRowSx}>
                <TableCell>
                  <AppTablePrimaryText>#{market.displayRank ?? index + 1}</AppTablePrimaryText>
                </TableCell>
                <TableCell sx={{ minWidth: 230 }}>
                  <Stack spacing={0.3}>
                    <AppTablePrimaryText>{market.player}</AppTablePrimaryText>
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
                <TableCell data-sticky-action="true" align="center" sx={{ ...stickyActionCellSx, minWidth: 130 }}>
                  <AppRowActions sx={{ mx: 'auto' }}>
                    <Button
                      startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : locked ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
                      size="small"
                      variant="contained"
                      disabled={locked || !market.canAddToEntry}
                      onClick={() => (locked || !market.canAddToEntry ? undefined : builder.toggleLeg(market))}
                      title={entryDisabled ? entryDisabledReason(market) : undefined}
                      sx={entryButtonSx(selected, locked)}
                    >
                      {selected ? 'Remove' : 'Add leg'}
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

function isEntryBuilderDisabled(market: MarketOpportunity) {
  return !market.canAddToEntry;
}

function entryDisabledReason(market: MarketOpportunity) {
  return market.entryRuleWarning ?? market.entryUnavailableReason ?? (market.providerCategory === 'Sportsbook' ? 'Entry Builder is available only for DFS entries.' : 'Entry Builder unavailable for this row.');
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
