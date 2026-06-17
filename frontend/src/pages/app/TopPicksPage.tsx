import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import CompareArrowsRoundedIcon from '@mui/icons-material/CompareArrowsRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { Box, Button, Collapse, Grid, MenuItem, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { ProjectionModifierBadge } from '../../components/markets/ProjectionModifierBadge';
import { formatTopPickWarning, getTopPickConfidence, TopPickCard } from '../../components/markets/TopPickCard';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatusPill } from '../../components/ui/StatusPill';
import { mockTopPicks } from '../../data/mockTopPicks';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { useToast } from '../../hooks/useToast';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';

export function TopPicksPage() {
  const { showToast } = useToast();
  const { isMobile } = useResponsiveView();
  const featured = mockTopPicks.slice(0, 3);

  return (
    <Stack spacing={{ xs: 2, md: 3 }}>
      <PageHeader title="Top Picks" description="Curated candidates ranked by model edge, EV, provider compatibility, and freshness." />
      <TopPickControls isMobile={isMobile} />

      {isMobile ? (
        <Stack spacing={1.35}>
          {mockTopPicks.map((market) => (
            <TopPickCard key={market.id} market={market} variant="compact" onAdd={() => showToast(`${market.player} added to mock entry.`, 'success')} />
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
                  <TopPickCard market={market} variant="featured" onAdd={() => showToast(`${market.player} added to mock entry.`, 'success')} />
                </Grid>
              ))}
            </Grid>
          </Stack>

          <Stack spacing={1.4}>
            <Typography variant="h5" fontWeight={950}>
              All Ranked Picks
            </Typography>
            <RankedPicksTable markets={mockTopPicks} onAdd={(market) => showToast(`${market.player} added to mock entry.`, 'success')} />
          </Stack>
        </>
      )}
    </Stack>
  );
}

function TopPickControls({ isMobile }: { isMobile: boolean }) {
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
            {['EV', 'Edge', 'Confidence', 'Sample'].map((item) => (
              <MenuItem key={item} value={item}>
                {item}
              </MenuItem>
            ))}
          </TextField>
          <Collapse in={filtersOpen} timeout="auto" unmountOnExit>
            <Stack spacing={1} sx={{ pt: 0.25 }}>
              <TextField label="Sport" size="small" select defaultValue="All" fullWidth>
                {['All', 'MLB', 'NBA', 'TENNIS', 'GOLF'].map((item) => (
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
              <TextField label="Confidence" size="small" select defaultValue="All" fullWidth>
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
          {['All', 'MLB', 'NBA', 'TENNIS', 'GOLF'].map((item) => (
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
        <TextField label="Confidence" size="small" select defaultValue="All" sx={{ width: 132 }}>
          {['All', 'Strong', 'Medium', 'Weak'].map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
        <TextField label="Sort by" size="small" select defaultValue="EV" sx={{ width: 126 }}>
          {['EV', 'Edge', 'Confidence', 'Sample'].map((item) => (
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

function RankedPicksTable({ markets, onAdd }: { markets: MarketOpportunity[]; onAdd: (market: MarketOpportunity) => void }) {
  return (
    <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 4, bgcolor: 'rgba(8,13,26,.28)' }}>
      <Table size="small" stickyHeader sx={{ minWidth: 1040 }}>
        <TableHead>
          <TableRow>
            <TableCell>Rank</TableCell>
            <TableCell>Market</TableCell>
            <TableCell>Provider</TableCell>
            <TableCell align="right">EV</TableCell>
            <TableCell align="right">Edge</TableCell>
            <TableCell>Confidence</TableCell>
            <TableCell>Tags</TableCell>
            <TableCell>Warning</TableCell>
            <TableCell align="center">Action</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {markets.map((market) => {
            const confidence = getTopPickConfidence(market);
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
                <TableCell>
                  <StatusPill label={confidence} />
                </TableCell>
                <TableCell sx={{ minWidth: 165 }}>
                  <Stack direction="row" gap={0.6} flexWrap="wrap">
                    <StatusPill label={market.strategyClassification} />
                    <ProjectionModifierBadge modifier={market.projectionModifier} />
                  </Stack>
                </TableCell>
                <TableCell sx={{ minWidth: 180, maxWidth: 240 }}>
                  <Typography variant="body2" color={market.warnings.length ? 'warning.main' : 'text.secondary'} sx={{ lineHeight: 1.4 }}>
                    {formatTopPickWarning(market)}
                  </Typography>
                </TableCell>
                <TableCell align="center" sx={{ minWidth: 190 }}>
                  <Stack direction="row" spacing={0.8} justifyContent="center" flexWrap="nowrap">
                    <Button
                      startIcon={<AddCircleOutlineRoundedIcon />}
                      size="small"
                      variant="contained"
                      disabled={!market.canAddToEntry}
                      onClick={() => onAdd(market)}
                      sx={actionButtonSx}
                    >
                      Add leg
                    </Button>
                    <Button startIcon={<CompareArrowsRoundedIcon />} size="small" variant="outlined" sx={compareButtonSx}>
                      Compare
                    </Button>
                  </Stack>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </Box>
  );
}

const actionButtonSx = {
  width: 98,
  minWidth: 98,
  height: 32,
  fontSize: 12,
  fontWeight: 900,
  color: '#06111f',
  background: 'linear-gradient(135deg, #22d3ee 0%, #818cf8 100%)',
  boxShadow: 'none',
  whiteSpace: 'nowrap',
  '& .MuiButton-startIcon': { mr: 0.55 },
  '&:hover': {
    background: 'linear-gradient(135deg, #38e1f4 0%, #8b96ff 100%)'
  },
  '&.Mui-disabled': {
    color: 'rgba(203,213,225,.42)',
    background: 'rgba(148,163,184,.08)',
    border: '1px solid rgba(148,163,184,.16)'
  }
};

const compareButtonSx = {
  width: 96,
  minWidth: 96,
  height: 32,
  fontSize: 12,
  fontWeight: 850,
  color: 'text.secondary',
  borderColor: 'rgba(148,163,184,.22)',
  whiteSpace: 'nowrap',
  '& .MuiButton-startIcon': { mr: 0.55 },
  '&:hover': {
    borderColor: 'rgba(148,163,184,.36)',
    bgcolor: 'rgba(148,163,184,.08)'
  }
};
