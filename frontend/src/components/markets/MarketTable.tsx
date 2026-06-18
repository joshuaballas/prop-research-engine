import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Box, Button, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useState } from 'react';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { getMarketCautions, isPlayableMarket } from '../../utils/marketCautions';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { ProviderLabel } from '../providers/ProviderLabel';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../ui/buttonStyles';
import { TagsLabelWithHelp } from './TagsLabelWithHelp';
import { marketTagLabel } from '../../utils/marketTags';
import { CautionLabels } from './CautionLabels';

export function MarketTable({ markets }: { markets: MarketOpportunity[] }) {
  const builder = useEntryBuilder();
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);
  return (
    <>
      <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 3 }}>
        <Table size="small" stickyHeader sx={{ tableLayout: 'fixed', minWidth: 1330 }}>
        <colgroup>
          <col style={{ width: '24%' }} />
          <col style={{ width: 70 }} />
          <col style={{ width: 150 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 115 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 130 }} />
          <col style={{ width: 220 }} />
          <col style={{ width: 130 }} />
        </colgroup>
        <TableHead>
          <TableRow>
            <TableCell>Market</TableCell>
            <TableCell>Sport</TableCell>
            <TableCell>Provider</TableCell>
            <TableCell>Line</TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="ev" align="right" variant="table" /></TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="hitChance" align="right" variant="table" /></TableCell>
            <TableCell><MetricLabelWithHelp metric="trust" variant="table" /></TableCell>
            <TableCell><TagsLabelWithHelp /></TableCell>
            <TableCell><MetricLabelWithHelp metric="caution" variant="table" /></TableCell>
            <TableCell sx={{ pl: 1.5, pr: 2 }}>Action</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {markets.map((market) => {
            const addable = isPlayableMarket(market);
            const selected = builder.isSelected(market.id);
            const incompatible = !selected && !builder.isCompatible(market);
            const canAddFromScanner = addable && !incompatible;
            const cautions = getMarketCautions(market, { locked: incompatible && addable });
            return (
              <TableRow key={market.id} hover sx={{ height: 78 }}>
                <TableCell sx={{ overflow: 'hidden', py: 0.85 }}>
                  <Typography fontWeight={950} noWrap>{market.player}</Typography>
                  <Typography color="text.secondary" fontSize={12} noWrap>
                    {market.side} {market.propType} {market.line}
                  </Typography>
                  <Typography color="text.secondary" fontSize={12} noWrap>
                    {market.event} - {market.startTime}
                  </Typography>
                </TableCell>
                <TableCell sx={{ py: 0.85 }}>
                  <Typography fontWeight={850}>{market.sport}</Typography>
                </TableCell>
                <TableCell sx={{ overflow: 'hidden', py: 0.85 }}>
                  {market.providerCategory === 'Sportsbook' ? (
                    <Stack spacing={0.2}>
                      <Typography fontWeight={900} noWrap>Reference only</Typography>
                      <Typography variant="caption" color="text.secondary">
                        Sportsbook line
                      </Typography>
                    </Stack>
                  ) : (
                    <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
                  )}
                </TableCell>
                <TableCell sx={{ py: 0.85 }}>
                  <Typography fontWeight={900}>
                    {market.side} {market.line}
                  </Typography>
                </TableCell>
                <TableCell align="right" sx={{ py: 0.85 }}>
                  <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
                    {formatPercent(market.estimatedEv, { signed: true })}
                  </Typography>
                </TableCell>
                <TableCell align="right" sx={{ py: 0.85 }}>
                  <Typography fontWeight={900}>{formatPercent(market.probabilityEdge, { signed: true })}</Typography>
                </TableCell>
                <TableCell align="right" sx={{ py: 0.85 }}>
                  <Typography fontWeight={900}>{formatPercent(market.modelProbability)}</Typography>
                </TableCell>
                <TableCell sx={{ py: 0.85 }}><TrustValue level={market.trustLevel} /></TableCell>
                <TableCell sx={{ overflow: 'hidden', py: 0.85 }}>
                  <Stack direction="row" gap={0.6} flexWrap="wrap" useFlexGap>
                    <StatusPill label={marketTagLabel(market.strategyClassification)} />
                    {market.projectionModifier === 'Unknown Promo' ? null : <ProjectionModifierBadge modifier={market.projectionModifier} />}
                  </Stack>
                </TableCell>
                <TableCell sx={{ py: 0.85, verticalAlign: 'middle' }}>
                  <Box sx={{ minHeight: 58, display: 'flex', alignItems: 'center' }}>
                    <CautionLabels cautions={cautions} />
                  </Box>
                </TableCell>
                <TableCell sx={{ width: 130, pl: 1.5, pr: 2, py: 0.85 }}>
                  <Button
                    size="small"
                    variant={canAddFromScanner || selected ? 'contained' : 'outlined'}
                    startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible && addable ? <LockRoundedIcon /> : canAddFromScanner ? <AddCircleOutlineRoundedIcon /> : undefined}
                    disabled={incompatible && addable}
                    onClick={() => (canAddFromScanner || selected ? builder.toggleLeg(market) : setDetailsMarket(market))}
                    sx={entryButtonSx(selected, incompatible && addable, canAddFromScanner)}
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
      <PropDetailsModal market={detailsMarket} open={Boolean(detailsMarket)} onClose={() => setDetailsMarket(null)} />
    </>
  );
}

const baseActionButtonSx = {
  ...compactActionButtonSx,
  width: 98,
  minWidth: 98,
  height: 32,
  fontSize: 12,
  whiteSpace: 'nowrap'
};

const detailsActionButtonSx = {
  ...secondaryGhostButtonSx,
  width: 98,
  minWidth: 98,
  height: 32,
  fontSize: 12,
  whiteSpace: 'nowrap'
};

function entryButtonSx(selected: boolean, incompatible: boolean, canAdd: boolean) {
  if (selected) {
    return {
      ...baseActionButtonSx,
      ...removeActionButtonSx
    };
  }

  if (incompatible) {
    return {
      ...baseActionButtonSx,
      ...lockedActionButtonSx
    };
  }

  return canAdd ? baseActionButtonSx : detailsActionButtonSx;
}
