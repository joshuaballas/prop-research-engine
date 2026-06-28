import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Button, Stack, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useState } from 'react';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { entryBuilderUnavailableReason, isEntryBuilderAddableMarket } from '../../utils/marketCautions';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { ProviderLabel } from '../providers/ProviderLabel';
import { removeActionButtonSx } from '../ui/buttonStyles';
import { appTableRowSx, stickyActionCellSx, stickyActionHeaderCellSx } from '../ui/appTableStyles';
import { AppDataTable } from '../ui/AppDataTable';
import { AppRowActions, desktopRowActionButtonSx, desktopRowDetailsButtonSx } from '../ui/AppRowActions';
import { AppTableMetricValue, AppTablePrimaryText, AppTableSecondaryText } from '../ui/AppTableTypography';
import { OpportunityPickText } from '../ui/OpportunityCard';
import { marketDisplayLabel } from '../../utils/marketDisplay';

export function MarketTable({ markets }: { markets: MarketOpportunity[] }) {
  const builder = useEntryBuilder();
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);
  const showSportColumn = new Set(markets.map((market) => market.sport)).size > 1;
  const minWidth = 845 - (showSportColumn ? 0 : 100);

  return (
    <>
      <AppDataTable minWidth={minWidth} sx={{ borderRadius: 3 }} tableSx={{ tableLayout: 'fixed' }}>
        <colgroup>
          <col style={{ width: '28%' }} />
          {showSportColumn ? <col style={{ width: 100 }} /> : null}
          <col style={{ width: 150 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 100 }} />
          <col style={{ width: 130 }} />
        </colgroup>
        <TableHead>
          <TableRow>
            <TableCell>Opportunity</TableCell>
            {showSportColumn ? <TableCell>Sport</TableCell> : null}
            <TableCell>Provider</TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="hitChance" align="right" variant="table" /></TableCell>
            <TableCell align="center" sx={{ ...stickyActionHeaderCellSx, pl: 1.5, pr: 2 }}>Action</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {markets.map((market) => {
            const addable = isEntryBuilderAddableMarket(market);
            const selected = builder.isSelected(market.id);
            const incompatible = !selected && !builder.isCompatible(market);
            const canAddFromScanner = addable && !incompatible;
            const locked = !selected && !canAddFromScanner;
            return (
              <TableRow
                key={market.id}
                hover
                sx={appTableRowSx}
              >
                <TableCell sx={{ overflow: 'hidden', py: 1.05 }}>
                  <AppTablePrimaryText>{market.player}</AppTablePrimaryText>
                  <OpportunityPickText
                    side={market.side}
                    propType={market.propType}
                    line={market.line}
                    sx={{
                      fontSize: 13,
                      flexWrap: 'nowrap',
                      overflow: 'hidden',
                      '& .MuiTypography-root': { fontSize: 'inherit', fontWeight: 500 }
                    }}
                  />
                  <AppTableSecondaryText>
                    {market.event} - {market.startTime}
                  </AppTableSecondaryText>
                </TableCell>
                {showSportColumn ? (
                  <TableCell sx={{ py: 1.05 }}>
                    <AppTableMetricValue>{marketDisplayLabel(market)}</AppTableMetricValue>
                  </TableCell>
                ) : null}
                <TableCell sx={{ overflow: 'hidden', py: 1.05 }}>
                  <Stack spacing={0.45} alignItems="flex-start">
                    <ProviderLabel provider={market.provider || (market.providerCategory === 'Sportsbook' ? 'Sportsbook' : 'Unknown')} category={market.providerCategory} compact />
                  </Stack>
                </TableCell>
                <TableCell align="right" sx={{ py: 1.05 }}>
                  <AppTableMetricValue>{formatPercent(market.probabilityEdge, { signed: true })}</AppTableMetricValue>
                </TableCell>
                <TableCell align="right" sx={{ py: 1.05 }}>
                  <AppTableMetricValue>{formatPercent(market.modelProbability)}</AppTableMetricValue>
                </TableCell>
                <TableCell data-sticky-action="true" align="center" sx={{ ...stickyActionCellSx, width: 130, pl: 1.5, pr: 2, py: 1.05 }}>
                  <AppRowActions sx={{ mx: 'auto' }}>
                    <Button
                      size="small"
                      variant="contained"
                      startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : locked ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
                      disabled={locked}
                      title={locked ? incompatible ? 'This entry is locked to another provider.' : entryBuilderUnavailableReason(market) : undefined}
                      onClick={() => (locked ? undefined : builder.toggleLeg(market))}
                      sx={entryButtonSx(selected)}
                    >
                      {selected ? 'Remove' : 'Add leg'}
                    </Button>
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
