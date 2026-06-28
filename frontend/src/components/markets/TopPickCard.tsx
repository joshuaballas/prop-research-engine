import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { formatAmericanOdds, formatDecimal, formatPercent } from '../../utils/formatters';
import { marketDisplayLabel } from '../../utils/marketDisplay';
import type { MarketOpportunity } from '../../types/markets';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../ui/buttonStyles';
import { mobileCardActionButtonSx, mobileCardDetailsButtonSx } from '../ui/AppRowActions';
import { OpportunityCardLayout, OpportunityMetricGrid, OpportunityMetricItem, OpportunityPickText, OpportunityProviderRow } from '../ui/OpportunityCard';

interface TopPickCardProps {
  market: MarketOpportunity;
  onAdd?: (market: MarketOpportunity) => void;
  hideActions?: boolean;
  variant?: 'default' | 'featured' | 'compact';
  selected?: boolean;
  incompatible?: boolean;
}

export function TopPickCard({ market, onAdd, hideActions = false, variant = 'default', selected = false, incompatible = false }: TopPickCardProps) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const compact = variant === 'compact';
  const featured = variant === 'featured';
  const actions = hideActions ? undefined : (
    compact ? (
      <TopPickMobileActions market={market} selected={selected} incompatible={incompatible} onAdd={onAdd} onDetails={() => setDetailsOpen(true)} />
    ) : (
      <TopPickActions market={market} selected={selected} incompatible={incompatible} onAdd={onAdd} onDetails={() => setDetailsOpen(true)} />
    )
  );

  return (
    <>
      <OpportunityCardLayout
        compact={compact}
        actionMode={compact ? 'stacked' : 'inline'}
        shellSx={{ height: '100%' }}
        context={<TopPickStatus market={market} includeRank={!compact} label="Top Pick" />}
        title={featured ? <Typography variant="h5" lineHeight={1.08}>{market.player}</Typography> : market.player}
        value={<TopPickHitChance market={market} compact={compact} />}
        event={`${market.event} - ${market.startTime}`}
        market={<OpportunityPickText side={market.side} propType={market.propType} line={market.line} />}
        provider={<TopPickProviderRow market={market} />}
        metrics={<TopPickMetrics market={market} detailed={!compact} />}
        actions={actions}
      />
      <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
    </>
  );
}

function TopPickStatus({
  market,
  includeRank,
  label
}: {
  market: MarketOpportunity;
  includeRank?: boolean;
  label?: string;
}) {
  return (
    <>
      {includeRank ? (
        <Typography variant="caption" color="primary.main" fontWeight={950}>
          #{market.rank ?? '-'} {marketDisplayLabel(market)}
        </Typography>
      ) : null}
      {label ? <StatusPill label={label} /> : null}
      <ProjectionModifierBadge modifier={market.projectionModifier} />
    </>
  );
}

function TopPickHitChance({ market, compact = false }: { market: MarketOpportunity; compact?: boolean }) {
  return (
    <Stack spacing={0.1} alignItems="flex-end">
      <MetricLabelWithHelp metric="hitChance" align="right" />
      <Typography
        color={market.modelProbability === null ? 'text.secondary' : 'success.main'}
        fontWeight={950}
        fontSize={compact ? 14 : undefined}
        whiteSpace="nowrap"
        lineHeight={1.1}
      >
        {formatPercent(market.modelProbability)}
      </Typography>
    </Stack>
  );
}

function TopPickProviderRow({ market }: { market: MarketOpportunity }) {
  return <OpportunityProviderRow provider={market.provider} category={market.providerCategory} />;
}

function TopPickMetrics({ market, detailed }: { market: MarketOpportunity; detailed: boolean }) {
  const showSportsbookPricing = detailed && market.isSportsbook;
  return (
    <OpportunityMetricGrid columns={{ xs: 2, sm: detailed ? 4 : 3 }} sx={{ borderTop: 0, pt: 0 }}>
      {showSportsbookPricing ? (
        <>
          <OpportunityMetricItem
            label="Odds"
            value={`${formatAmericanOdds(market.bestAmericanPrice)} / ${formatDecimal(market.bestDecimalPrice)}`}
            noWrap={false}
          />
          <OpportunityMetricItem label="Modifier" value={market.projectionModifier === 'Normal' ? '-' : market.projectionModifier} noWrap={false} />
        </>
      ) : null}
      <OpportunityMetricItem label="Hit chance" value={formatPercent(market.modelProbability)} metric="hitChance" />
      {showSportsbookPricing ? <OpportunityMetricItem label="Break-even" value={formatPercent(market.impliedProbability)} /> : null}
      <OpportunityMetricItem label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} metric="edge" />
    </OpportunityMetricGrid>
  );
}

function TopPickActions({
  market,
  selected,
  incompatible,
  onAdd,
  onDetails
}: {
  market: MarketOpportunity;
  selected: boolean;
  incompatible: boolean;
  onAdd?: (market: MarketOpportunity) => void;
  onDetails: () => void;
}) {
  const entryDisabled = isEntryBuilderDisabled(market) && !selected;
  const locked = incompatible || entryDisabled;
  return (
    <>
      <Button
        variant="contained"
        size="small"
        startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : locked ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
        onClick={() => (locked ? undefined : onAdd?.(market))}
        disabled={locked}
        title={entryDisabled ? entryDisabledReason(market) : undefined}
        sx={entryButtonSx(selected, locked)}
      >
        {selected ? 'Remove' : 'Add leg'}
      </Button>
      <Button variant="outlined" size="small" onClick={onDetails} sx={compareButtonSx}>
        Details
      </Button>
    </>
  );
}

function TopPickMobileActions({
  market,
  selected,
  incompatible,
  onAdd,
  onDetails
}: {
  market: MarketOpportunity;
  selected: boolean;
  incompatible: boolean;
  onAdd?: (market: MarketOpportunity) => void;
  onDetails: () => void;
}) {
  const entryDisabled = isEntryBuilderDisabled(market) && !selected;
  const locked = incompatible || entryDisabled;
  return (
    <>
      <Button
        variant="contained"
        size="small"
        startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : locked ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
        onClick={() => (locked ? undefined : onAdd?.(market))}
        disabled={locked}
        title={entryDisabled ? entryDisabledReason(market) : undefined}
        sx={mobileEntryButtonSx(selected, locked)}
      >
        {selected ? 'Remove' : 'Add leg'}
      </Button>
      <Button variant="outlined" size="small" onClick={onDetails} sx={mobileCardDetailsButtonSx}>
        Details
      </Button>
    </>
  );
}

function isEntryBuilderDisabled(market: MarketOpportunity) {
  return !market.canAddToEntry;
}

function entryDisabledReason(market: MarketOpportunity) {
  return market.entryRuleWarning ?? market.entryUnavailableReason ?? (market.providerCategory === 'Sportsbook' ? 'Entry Builder is available only for DFS entries.' : 'Entry Builder unavailable for this row.');
}

const actionButtonSx = {
  ...compactActionButtonSx,
  width: 112,
  minWidth: 112,
  height: 34,
  fontSize: 12.5,
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
  width: 112,
  minWidth: 112,
  height: 34,
  fontSize: 12.5,
  fontWeight: 850,
  whiteSpace: 'nowrap'
};

function mobileEntryButtonSx(selected: boolean, incompatible: boolean) {
  if (selected) {
    return {
      ...mobileCardActionButtonSx,
      ...removeActionButtonSx
    };
  }

  if (incompatible) {
    return {
      ...mobileCardActionButtonSx,
      ...lockedActionButtonSx
    };
  }

  return mobileCardActionButtonSx;
}
