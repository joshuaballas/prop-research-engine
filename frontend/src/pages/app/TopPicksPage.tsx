import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { Box, Button, Collapse, Grid, MenuItem, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useMemo, useState } from 'react';
import { ProjectionModifierBadge } from '../../components/markets/ProjectionModifierBadge';
import { formatTopPickWarning, getTopPickConfidence, TopPickCard } from '../../components/markets/TopPickCard';
import { MetricLabelWithHelp } from '../../components/metrics/MetricLabelWithHelp';
import { TrustValue } from '../../components/metrics/TrustValue';
import { PropDetailsModal } from '../../components/prop-history/PropDetailsModal';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatusPill } from '../../components/ui/StatusPill';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../../components/ui/buttonStyles';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { useTopPicksData } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { buildAvailableSportOptions, getAvailableSportCodes } from '../../utils/sportOptions';
import { hasRenderableWarning } from '../../utils/warnings';

export function TopPicksPage() {
  const builder = useEntryBuilder();
  const { isMobile } = useResponsiveView();
  const topPicksQuery = useTopPicksData(30, 'top-pick');
  const topPicks = topPicksQuery.markets;
  const availableSportCodes = useMemo(() => getAvailableSportCodes(topPicks), [topPicks]);
  const sportFilterLabels = useMemo(() => {
    const labels = buildAvailableSportOptions(availableSportCodes).map((sport) => sport.label);
    return labels.length ? labels : ['All'];
  }, [availableSportCodes]);
  const featured = topPicks.slice(0, 3);

  return (
    <Stack spacing={{ xs: 2, md: 3 }}>
      <PageHeader title="Top Picks" description="Curated candidates ranked by model edge, EV, provider compatibility, and freshness." />
      <TopPickControls isMobile={isMobile} sportOptions={sportFilterLabels} />

      {topPicksQuery.loading ? (
        <ApiStateCard message="Loading latest top picks..." />
      ) : topPicksQuery.error ? (
        <ApiStateCard tone="error" message="Top picks could not be loaded from the market API." />
      ) : topPicks.length === 0 ? (
        <ApiStateCard message="No top picks are available from the latest market snapshot." />
      ) : isMobile ? (
        <Stack spacing={1.35}>
          {topPicks.map((market) => (
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
            <RankedPicksTable markets={topPicks} builder={builder} />
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
        bgcolor: 'rgba(15,23,42,.68)',
        p: 2
      }}
    >
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </Box>
  );
}

function TopPickControls({ isMobile, sportOptions }: { isMobile: boolean; sportOptions: string[] }) {
  const [filtersOpen, setFiltersOpen] = useState(false);

  if (isMobile) {
    return (
      <Box
        sx={{
          border: '1px solid rgba(148,163,184,.14)',
          borderRadius: 3,
          bgcolor: 'rgba(15,23,42,.42)',
          p: 1
        }}
      >
        <Stack spacing={1}>
          <Stack direction="row" spacing={1} alignItems="center">
            <TextField label="Search player" size="small" fullWidth />
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
          <TextField label="Sort by" size="small" select defaultValue="EV" fullWidth>
            {['EV', 'Edge', 'Trust', 'Sample'].map((item) => (
              <MenuItem key={item} value={item}>
                {item}
              </MenuItem>
            ))}
          </TextField>
          <Collapse in={filtersOpen} timeout="auto" unmountOnExit>
            <Stack spacing={1} sx={{ pt: 0.25 }}>
              <TextField label="Sport" size="small" select defaultValue="All" fullWidth>
                {sportOptions.map((item) => (
                  <MenuItem key={item} value={item}>
                    {item}
                  </MenuItem>
                ))}
              </TextField>
              <TextField label="Provider" size="small" select defaultValue="All" fullWidth>
                {['All', 'PrizePicks', 'Underdog', 'DraftKings Pick6', 'Betr Picks'].map((item) => (
                  <MenuItem key={item} value={item}>
                    {item}
                  </MenuItem>
                ))}
              </TextField>
              <TextField label="Opportunity" size="small" select defaultValue="All" fullWidth>
                {['All', 'Candidate', 'Watchlist', 'Research'].map((item) => (
                  <MenuItem key={item} value={item}>
                    {item}
                  </MenuItem>
                ))}
              </TextField>
              <TextField label="Trust" size="small" select defaultValue="All" fullWidth>
                {['All', 'Strong', 'Medium', 'Weak'].map((item) => (
                  <MenuItem key={item} value={item}>
                    {item}
                  </MenuItem>
                ))}
              </TextField>
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
        <TextField label="Search player" size="small" fullWidth sx={{ maxWidth: 360, minWidth: 300 }} />
        <TextField label="Sport" size="small" select defaultValue="All" sx={{ width: 122 }}>
          {sportOptions.map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField label="Provider" size="small" select defaultValue="All" sx={{ width: 168 }}>
          {['All', 'PrizePicks', 'Underdog', 'DraftKings Pick6', 'Betr Picks'].map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField label="Opportunity" size="small" select defaultValue="All" sx={{ width: 138 }}>
          {['All', 'Candidate', 'Watchlist', 'Research'].map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField label="Trust" size="small" select defaultValue="All" sx={{ width: 132 }}>
          {['All', 'Strong', 'Medium', 'Weak'].map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField label="Sort by" size="small" select defaultValue="EV" sx={{ width: 126 }}>
          {['EV', 'Edge', 'Trust', 'Sample'].map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <Button
          variant="text"
          size="small"
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
      <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 4, bgcolor: 'rgba(8,13,26,.28)' }}>
        <Table size="small" stickyHeader sx={{ minWidth: 1040 }}>
          <TableHead>
            <TableRow>
              <TableCell>Rank</TableCell>
              <TableCell>Market</TableCell>
              <TableCell>Provider</TableCell>
              <TableCell align="right"><MetricLabelWithHelp metric="ev" align="right" variant="table" /></TableCell>
              <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
              <TableCell align="right"><MetricLabelWithHelp metric="hitChance" align="right" variant="table" /></TableCell>
              <TableCell><MetricLabelWithHelp metric="trust" variant="table" /></TableCell>
              <TableCell><MetricLabelWithHelp metric="warning" variant="table" /></TableCell>
              <TableCell align="center">Action</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {markets.map((market) => {
              const confidence = getTopPickConfidence(market);
              const selected = builder.isSelected(market.id);
              const incompatible = !selected && !builder.isCompatible(market);
              const hasWarning = hasRenderableWarning(market.warnings);
              return (
              <TableRow key={market.id} hover sx={{ '& td': { borderColor: 'rgba(148,163,184,.12)', py: 1.25 } }}>
                <TableCell>
                  <Typography fontWeight={950}>#{market.rank ?? '-'}</Typography>
                </TableCell>
                <TableCell sx={{ minWidth: 230 }}>
                  <Stack spacing={0.3}>
                    <Typography fontWeight={900} noWrap>
                      {market.player}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {market.side} {market.propType} {market.line} - {market.sport}
                    </Typography>
                    <Stack direction="row" gap={0.6} flexWrap="wrap">
                      <StatusPill label={market.strategyClassification} />
                      <ProjectionModifierBadge modifier={market.projectionModifier} />
                    </Stack>
                  </Stack>
                </TableCell>
                <TableCell sx={{ minWidth: 160 }}>
                  <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
                </TableCell>
                <TableCell align="right">
                  <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
                    {formatPercent(market.estimatedEv, { signed: true })}
                  </Typography>
                </TableCell>
                <TableCell align="right">{formatPercent(market.probabilityEdge, { signed: true })}</TableCell>
                <TableCell align="right">{formatPercent(market.modelProbability)}</TableCell>
                <TableCell>
                  <TrustValue level={confidence} variant="badge" size="small" />
                </TableCell>
                <TableCell sx={{ minWidth: 180, maxWidth: 240 }}>
                  <Typography variant="body2" color={hasWarning ? 'warning.main' : 'text.secondary'} sx={{ lineHeight: 1.4 }}>
                    {formatTopPickWarning(market)}
                  </Typography>
                </TableCell>
                <TableCell align="center" sx={{ minWidth: 190 }}>
                  <Stack direction="row" spacing={0.8} justifyContent="center" flexWrap="nowrap">
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
                    <Button startIcon={<InfoOutlinedIcon />} size="small" variant="outlined" onClick={() => setDetailsMarket(market)} sx={compareButtonSx}>
                      Details
                    </Button>
                  </Stack>
                </TableCell>
              </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </Box>
      <PropDetailsModal market={detailsMarket} open={Boolean(detailsMarket)} onClose={() => setDetailsMarket(null)} />
    </>
  );
}

const actionButtonSx = {
  ...compactActionButtonSx,
  width: 98,
  minWidth: 98,
  height: 32,
  fontSize: 12,
  whiteSpace: 'nowrap'
};

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
  ...secondaryGhostButtonSx,
  width: 98,
  minWidth: 98,
  height: 32,
  fontSize: 12,
  fontWeight: 850,
  whiteSpace: 'nowrap'
};
