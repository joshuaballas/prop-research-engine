import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { entryBuilderUnavailableReason, getOperationalMarketCautions, isEntryBuilderAddableMarket } from '../../utils/marketCautions';
import { TrustValue } from '../metrics/TrustValue';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { removeActionButtonSx } from '../ui/buttonStyles';
import { mobileCardActionButtonSx, mobileCardDetailsButtonSx } from '../ui/AppRowActions';
import {
  OpportunityCardLayout,
  OpportunityMetricGrid,
  OpportunityMetricItem,
  OpportunityPickText,
  OpportunityProviderRow
} from '../ui/OpportunityCard';
import { marketTagLabel } from '../../utils/marketTags';
import { CautionLabels } from './CautionLabels';
import { getDisplayExpectedValue } from '../../utils/expectedValueDisplay';
import { marketDisplayLabel } from '../../utils/marketDisplay';

export function MarketCard({ market }: { market: MarketOpportunity }) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const builder = useEntryBuilder();
  const addable = isEntryBuilderAddableMarket(market);
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const canAddFromScanner = addable && !incompatible;
  const cautions = getOperationalMarketCautions(market, { locked: incompatible && addable });
  const locked = !selected && !canAddFromScanner;
  const displayEv = getDisplayExpectedValue(market);
  const headlineValue = displayEv ?? market.probabilityEdge;
  const displayLabel = marketDisplayLabel(market);
  const actions = (
    <>
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
      <Button size="small" variant="outlined" onClick={() => setDetailsOpen(true)} sx={secondaryActionButtonSx}>
        Details
      </Button>
    </>
  );

  return (
    <>
      <OpportunityCardLayout
        compact
        actionMode="stacked"
        shellSx={{ minHeight: 292 }}
        title={market.player}
        value={
          <Stack spacing={0.1} alignItems="flex-end">
            <Typography variant="caption" color="text.secondary" fontWeight={850} lineHeight={1}>
              {displayEv === null ? 'Edge' : 'EV'}
            </Typography>
            <Typography color={(headlineValue ?? 0) > 0 ? 'success.main' : 'error.main'} fontWeight={950} whiteSpace="nowrap" lineHeight={1.1}>
              {formatPercent(headlineValue, { signed: true })}
            </Typography>
          </Stack>
        }
        event={
          <Box
            component="span"
            title={`${displayLabel} - ${market.event} - ${market.startTime}`}
            sx={{
              minHeight: 38,
              display: '-webkit-box',
              WebkitLineClamp: 2,
              WebkitBoxOrient: 'vertical',
              overflow: 'hidden'
            }}
          >
            {displayLabel} - {market.event} - {market.startTime}
          </Box>
        }
        market={<OpportunityPickText side={market.side} propType={market.propType} line={market.line} />}
        provider={
          <OpportunityProviderRow provider={market.provider || (market.providerCategory === 'Sportsbook' ? 'Sportsbook' : 'Unknown')} category={market.providerCategory} sx={{ minHeight: 28 }}>
            <StatusRow market={market} />
          </OpportunityProviderRow>
        }
        metrics={
          <Box>
            <OpportunityMetricGrid columns={{ xs: 2, sm: 4 }} sx={{ borderTop: 0, pt: 0 }}>
              <OpportunityMetricItem label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} metric="edge" />
              <OpportunityMetricItem label="Hit chance" value={formatPercent(market.modelProbability)} metric="hitChance" />
              <OpportunityMetricItem label="Trust" value={<TrustValue level={market.trustLevel} sx={{ fontSize: 14, fontWeight: 950, lineHeight: 1.5 }} />} metric="trust" />
            </OpportunityMetricGrid>
          </Box>
        }
        caution={cautions.length ? <CautionLabels cautions={cautions} /> : null}
        actions={actions}
      />
      <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
    </>
  );
}

function StatusRow({ market }: { market: MarketOpportunity }) {
  return (
    <Stack direction="row" gap={0.6} flexWrap="wrap" useFlexGap justifyContent="flex-end">
      {market.providerCategory === 'Sportsbook' ? <StatusPill label={market.recommendationActionableForUser ? 'Sportsbook pick' : 'Sportsbook line'} /> : null}
      <StatusPill label={marketTagLabel(market.strategyClassification)} />
      {market.projectionModifier === 'Unknown Promo' ? null : <ProjectionModifierBadge modifier={market.projectionModifier} />}
    </Stack>
  );
}

const baseActionButtonSx = mobileCardActionButtonSx;
const secondaryActionButtonSx = mobileCardDetailsButtonSx;

function entryButtonSx(selected: boolean) {
  if (selected) {
    return {
      ...baseActionButtonSx,
      ...removeActionButtonSx
    };
  }

  return baseActionButtonSx;
}
