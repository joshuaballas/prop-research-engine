import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Box, Button, Stack, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
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
import { removeActionButtonSx } from '../ui/buttonStyles';
import { appTableRowSx } from '../ui/appTableStyles';
import { AppDataTable } from '../ui/AppDataTable';
import { AppRowActions, desktopRowActionButtonSx, desktopRowDetailsButtonSx } from '../ui/AppRowActions';
import { TagsLabelWithHelp } from './TagsLabelWithHelp';
import { marketTagLabel } from '../../utils/marketTags';
import { CautionLabels } from './CautionLabels';

export function MarketTable({ markets }: { markets: MarketOpportunity[] }) {
  const builder = useEntryBuilder();
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);
  return (
    <>
      <AppDataTable minWidth={1330} sx={{ borderRadius: 3 }} tableSx={{ tableLayout: 'fixed' }}>
        <colgroup>
          <col style={{ width: '25%' }} />
          <col style={{ width: 64 }} />
          <col style={{ width: 150 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 115 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 200 }} />
          <col style={{ width: 210 }} />
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
              <TableRow
                key={market.id}
                hover
                sx={appTableRowSx}
              >
                <TableCell sx={{ overflow: 'hidden', py: 1.05 }}>
                  <Typography fontWeight={950} noWrap>{market.player}</Typography>
                  <Typography color="text.secondary" fontSize={12} noWrap>
                    {market.side} {market.propType} {market.line}
                  </Typography>
                  <Typography color="text.secondary" fontSize={12} noWrap>
                    {market.event} - {market.startTime}
                  </Typography>
                </TableCell>
                <TableCell sx={{ py: 1.05 }}>
                  <Typography fontWeight={850}>{market.sport}</Typography>
                </TableCell>
                <TableCell sx={{ overflow: 'hidden', py: 1.05 }}>
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
                <TableCell sx={{ py: 1.05 }}>
                  <Typography fontWeight={900}>
                    {market.side} {market.line}
                  </Typography>
                </TableCell>
                <TableCell align="right" sx={{ py: 1.05 }}>
                  <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
                    {formatPercent(market.estimatedEv, { signed: true })}
                  </Typography>
                </TableCell>
                <TableCell align="right" sx={{ py: 1.05 }}>
                  <Typography fontWeight={900}>{formatPercent(market.probabilityEdge, { signed: true })}</Typography>
                </TableCell>
                <TableCell align="right" sx={{ py: 1.05 }}>
                  <Typography fontWeight={900}>{formatPercent(market.modelProbability)}</Typography>
                </TableCell>
                <TableCell sx={{ py: 1.05 }}><TrustValue level={market.trustLevel} /></TableCell>
                <TableCell sx={{ overflow: 'hidden', py: 1.05 }}>
                  <Stack direction="row" gap={0.6} flexWrap="wrap" useFlexGap>
                    <StatusPill label={marketTagLabel(market.strategyClassification)} />
                    {market.projectionModifier === 'Unknown Promo' ? null : <ProjectionModifierBadge modifier={market.projectionModifier} />}
                  </Stack>
                </TableCell>
                <TableCell sx={{ py: 1.05, verticalAlign: 'middle' }}>
                  <Box sx={{ minHeight: 64, display: 'flex', alignItems: 'center' }}>
                    <CautionLabels cautions={cautions} />
                  </Box>
                </TableCell>
                <TableCell sx={{ width: 130, pl: 1.5, pr: 2, py: 1.05 }}>
                  <AppRowActions>
                    {canAddFromScanner || selected ? (
                      <Button
                        size="small"
                        variant="contained"
                        startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
                        onClick={() => builder.toggleLeg(market)}
                        sx={entryButtonSx(selected)}
                      >
                        {selected ? 'Remove' : 'Add leg'}
                      </Button>
                    ) : null}
                    <Button
                      size="small"
                      variant="outlined"
                      onClick={() => setDetailsMarket(market)}
                      sx={detailsActionButtonSx}
                    >
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

const baseActionButtonSx = desktopRowActionButtonSx;
const detailsActionButtonSx = desktopRowDetailsButtonSx;

function entryButtonSx(selected: boolean) {
  if (selected) {
    return {
      ...baseActionButtonSx,
      ...removeActionButtonSx
    };
  }

  return baseActionButtonSx;
}
