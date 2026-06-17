import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Box, Button, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { ProviderLabel } from '../providers/ProviderLabel';

export function MarketTable({ markets }: { markets: MarketOpportunity[] }) {
  const builder = useEntryBuilder();
  return (
    <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 3 }}>
      <Table size="small" stickyHeader>
        <TableHead>
          <TableRow>
            <TableCell>Market</TableCell>
            <TableCell>Sport</TableCell>
            <TableCell>Provider</TableCell>
            <TableCell>Line</TableCell>
            <TableCell align="right">EV</TableCell>
            <TableCell align="right">Edge</TableCell>
            <TableCell>Confidence</TableCell>
            <TableCell>Tags</TableCell>
            <TableCell>Warning</TableCell>
            <TableCell>Action</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {markets.map((market) => {
            const addable = isScannerAddable(market);
            const selected = builder.isSelected(market.id);
            const incompatible = !selected && !builder.isCompatible(market);
            const canAddFromScanner = addable && !incompatible;
            return (
              <TableRow key={market.id} hover>
                <TableCell>
                  <Typography fontWeight={950}>{market.player}</Typography>
                  <Typography color="text.secondary" fontSize={12}>
                    {market.side} {market.propType} {market.line}
                  </Typography>
                  <Typography color="text.secondary" fontSize={12}>
                    {market.event} - {market.startTime}
                  </Typography>
                </TableCell>
                <TableCell>
                  <Typography fontWeight={850}>{market.sport}</Typography>
                </TableCell>
                <TableCell>
                  {market.providerCategory === 'Sportsbook' ? (
                    <Stack spacing={0.2}>
                      <Typography fontWeight={900}>Reference only</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Sportsbook line
                      </Typography>
                    </Stack>
                  ) : (
                    <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
                  )}
                </TableCell>
                <TableCell>
                  <Typography fontWeight={900}>
                    {market.side} {market.line}
                  </Typography>
                </TableCell>
                <TableCell align="right">
                  <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
                    {formatPercent(market.estimatedEv, { signed: true })}
                  </Typography>
                </TableCell>
                <TableCell align="right">
                  <Typography fontWeight={900}>{formatPercent(market.probabilityEdge, { signed: true })}</Typography>
                </TableCell>
                <TableCell><ConfidenceLabel level={market.trustLevel} /></TableCell>
                <TableCell>
                  <Stack direction="row" gap={0.6} flexWrap="wrap" useFlexGap>
                    <StatusPill label={market.strategyClassification} />
                    {market.projectionModifier === 'Unknown Promo' ? null : <ProjectionModifierBadge modifier={market.projectionModifier} />}
                  </Stack>
                </TableCell>
                <TableCell sx={{ minWidth: 170 }}>
                  <Typography variant="body2" color={market.warnings.length || !addable ? 'warning.main' : 'text.secondary'}>
                    {formatWarning(market, addable)}
                  </Typography>
                </TableCell>
                <TableCell>
                  <Button
                    size="small"
                    variant={canAddFromScanner || selected ? 'contained' : 'outlined'}
                    startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible && addable ? <LockRoundedIcon /> : canAddFromScanner ? <AddCircleOutlineRoundedIcon /> : undefined}
                    disabled={incompatible && addable}
                    onClick={() => (canAddFromScanner || selected ? builder.toggleLeg(market) : undefined)}
                    sx={entryButtonSx(selected, incompatible && addable)}
                  >
                    {selected ? 'Remove' : incompatible && addable ? 'Locked' : canAddFromScanner ? 'Add leg' : 'Details'}
                  </Button>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </Box>
  );
}

const baseActionButtonSx = {
  minWidth: 94,
  height: 32,
  whiteSpace: 'nowrap',
  fontWeight: 900
};

function entryButtonSx(selected: boolean, incompatible: boolean) {
  if (selected) {
    return {
      ...baseActionButtonSx,
      color: '#FECACA',
      background: 'rgba(244,63,94,.16)',
      border: '1px solid rgba(244,63,94,.42)',
      '&:hover': {
        background: 'rgba(244,63,94,.24)',
        borderColor: 'rgba(244,63,94,.56)',
        color: '#FFE4E6'
      }
    };
  }

  if (incompatible) {
    return {
      ...baseActionButtonSx,
      color: '#CBD5E1',
      background: 'rgba(51,65,85,.42)',
      border: '1px solid rgba(148,163,184,.24)',
      '&.Mui-disabled': {
        color: '#CBD5E1',
        background: 'rgba(51,65,85,.42)',
        border: '1px solid rgba(148,163,184,.24)',
        opacity: 1
      }
    };
  }

  return baseActionButtonSx;
}

function isScannerAddable(market: MarketOpportunity) {
  return (
    market.providerCategory === 'DFS' &&
    market.canAddToEntry &&
    market.estimatedEv > 0 &&
    (market.strategyClassification === 'Candidate' || market.strategyClassification === 'Watchlist') &&
    market.freshness !== 'Unavailable'
  );
}

function ConfidenceLabel({ level }: { level: MarketOpportunity['trustLevel'] }) {
  const label = level === 'Research' ? 'Weak' : level;
  const color = label === 'Strong' ? 'success.main' : label === 'Medium' ? 'warning.main' : 'error.main';
  return (
    <Typography color={color} fontWeight={950}>
      {label}
    </Typography>
  );
}

function formatWarning(market: MarketOpportunity, addable: boolean) {
  if (market.providerCategory === 'Sportsbook') return 'Reference lines are not entry legs.';
  if (market.estimatedEv <= 0) return 'Non-positive EV. Review details only.';
  if (market.freshness === 'Unavailable') return 'Limited coverage.';
  if (!addable && market.strategyClassification === 'Research') return 'Research row. Review before adding.';
  if (!market.warnings.length) return 'None';
  return market.warnings[0]
    .replace('Sportsbook context rows cannot be added', 'Reference lines are not entry legs')
    .replace('Provider limited for this sport', 'Limited coverage')
    .replace('Low sample size', 'Low sample')
    .replace('Confirm final payout in provider app', 'Payout may vary');
}
