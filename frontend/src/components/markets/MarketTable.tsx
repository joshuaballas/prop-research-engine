import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Button, Stack, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useState } from 'react';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { isPlayableMarket } from '../../utils/marketCautions';
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
import { AppTableEvValue, AppTableMetricValue, AppTablePrimaryText, AppTableSecondaryText } from '../ui/AppTableTypography';
import { OpportunityPickText } from '../ui/OpportunityCard';
import { TagsLabelWithHelp } from './TagsLabelWithHelp';
import { marketTagLabel } from '../../utils/marketTags';

export function MarketTable({ markets }: { markets: MarketOpportunity[] }) {
  const builder = useEntryBuilder();
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);
  const showSportColumn = new Set(markets.map((market) => market.sport)).size > 1;
  const meaningfulTags = new Set(
    markets.flatMap((market) => [
      marketTagLabel(market.strategyClassification),
      market.projectionModifier === 'Unknown Promo' || market.projectionModifier === 'Normal' ? null : market.projectionModifier
    ].filter(Boolean))
  );
  const showTagsColumn = meaningfulTags.size > 1;
  const minWidth = 1130 - (showSportColumn ? 0 : 64) - (showTagsColumn ? 0 : 200);

  return (
    <>
      <AppDataTable minWidth={minWidth} sx={{ borderRadius: 3 }} tableSx={{ tableLayout: 'fixed' }}>
        <colgroup>
          <col style={{ width: '28%' }} />
          {showSportColumn ? <col style={{ width: 64 }} /> : null}
          <col style={{ width: 150 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 95 }} />
          <col style={{ width: 115 }} />
          <col style={{ width: 95 }} />
          {showTagsColumn ? <col style={{ width: 200 }} /> : null}
          <col style={{ width: 130 }} />
        </colgroup>
        <TableHead>
          <TableRow>
            <TableCell>Opportunity</TableCell>
            {showSportColumn ? <TableCell>Sport</TableCell> : null}
            <TableCell>Provider</TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="ev" align="right" variant="table" /></TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="hitChance" align="right" variant="table" /></TableCell>
            <TableCell><MetricLabelWithHelp metric="trust" variant="table" /></TableCell>
            {showTagsColumn ? <TableCell><TagsLabelWithHelp /></TableCell> : null}
            <TableCell sx={{ pl: 1.5, pr: 2 }}>Action</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {markets.map((market) => {
            const addable = isPlayableMarket(market);
            const selected = builder.isSelected(market.id);
            const incompatible = !selected && !builder.isCompatible(market);
            const canAddFromScanner = addable && !incompatible;
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
                    <AppTableMetricValue>{market.sport}</AppTableMetricValue>
                  </TableCell>
                ) : null}
                <TableCell sx={{ overflow: 'hidden', py: 1.05 }}>
                  {market.providerCategory === 'Sportsbook' ? (
                    <Stack spacing={0.2}>
                      <AppTableMetricValue>Reference only</AppTableMetricValue>
                      <AppTableSecondaryText>
                        Sportsbook line
                      </AppTableSecondaryText>
                    </Stack>
                  ) : (
                    <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
                  )}
                </TableCell>
                <TableCell align="right" sx={{ py: 1.05 }}>
                  <AppTableEvValue positive={market.estimatedEv > 0}>
                    {formatPercent(market.estimatedEv, { signed: true })}
                  </AppTableEvValue>
                </TableCell>
                <TableCell align="right" sx={{ py: 1.05 }}>
                  <AppTableMetricValue>{formatPercent(market.probabilityEdge, { signed: true })}</AppTableMetricValue>
                </TableCell>
                <TableCell align="right" sx={{ py: 1.05 }}>
                  <AppTableMetricValue>{formatPercent(market.modelProbability)}</AppTableMetricValue>
                </TableCell>
                <TableCell sx={{ py: 1.05 }}><TrustValue level={market.trustLevel} /></TableCell>
                {showTagsColumn ? (
                  <TableCell sx={{ overflow: 'hidden', py: 1.05 }}>
                    <Stack direction="row" gap={0.6} flexWrap="wrap" useFlexGap>
                      <StatusPill label={marketTagLabel(market.strategyClassification)} />
                      {market.projectionModifier === 'Unknown Promo' ? null : <ProjectionModifierBadge modifier={market.projectionModifier} />}
                    </Stack>
                  </TableCell>
                ) : null}
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
