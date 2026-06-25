import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { formatAmericanOdds, formatDecimal, formatPercent } from '../../utils/formatters';
import type { MarketOpportunity } from '../../types/markets';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../ui/buttonStyles';
import { mobileCardActionButtonSx, mobileCardDetailsButtonSx } from '../ui/AppRowActions';
import { OpportunityCardLayout, OpportunityMetricGrid, OpportunityMetricItem, OpportunityPickText, OpportunityProviderRow } from '../ui/OpportunityCard';
import { hasRenderableWarning, renderOpportunityCautionText, renderWarningText } from '../../utils/warnings';
import { getDisplayExpectedValue } from '../../utils/expectedValueDisplay';

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
  const warningText = renderOpportunityCautionText(market.warnings);
  const confidence = resolveConfidence(market);
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
        context={<TopPickStatus market={market} includeRank={!compact} label={featured || compact ? 'Top Pick' : market.strategyClassification} />}
        title={featured ? <Typography variant="h5" lineHeight={1.08}>{market.player}</Typography> : market.player}
        value={<TopPickEv market={market} compact={compact} />}
        event={`${market.event} - ${market.startTime}`}
        market={<OpportunityPickText side={market.side} propType={market.propType} line={market.line} />}
        provider={<TopPickProviderRow market={market} confidence={confidence} showTrust={!compact} />}
        metrics={<TopPickMetrics market={market} detailed={!compact} confidence={confidence} />}
        caution={warningText}
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
          #{market.rank ?? '-'} {market.sport}
        </Typography>
      ) : null}
      {label ? <StatusPill label={label} /> : null}
      <ProjectionModifierBadge modifier={market.projectionModifier} />
    </>
  );
}

function TopPickEv({ market, compact = false }: { market: MarketOpportunity; compact?: boolean }) {
  const displayEv = getDisplayExpectedValue(market);
  const value = displayEv ?? market.probabilityEdge;
  return (
    <Stack spacing={0.1} alignItems="flex-end">
      {displayEv === null ? (
        <MetricLabelWithHelp metric="edge" align="right" />
      ) : (
        <MetricLabelWithHelp metric="ev" align="right" />
      )}
      <Typography
        color={value === null ? 'text.secondary' : value >= 0 ? 'success.main' : 'error.main'}
        fontWeight={950}
        fontSize={compact ? 14 : undefined}
        whiteSpace="nowrap"
        lineHeight={1.1}
      >
        {formatPercent(value, { signed: true })}
      </Typography>
    </Stack>
  );
}

function TopPickProviderRow({ market, confidence, showTrust = true }: { market: MarketOpportunity; confidence: 'Strong' | 'Medium' | 'Weak'; showTrust?: boolean }) {
  return (
    <OpportunityProviderRow provider={market.provider} category={market.providerCategory}>
      {showTrust ? (
        <Stack
          direction="row"
          alignItems="center"
          gap={0.65}
          sx={{
            '& .MuiTypography-root': {
              lineHeight: 1.1
            }
          }}
        >
          <MetricLabelWithHelp metric="trust" iconPosition="before" />
          <Typography component="span" color="text.secondary" aria-hidden sx={{ lineHeight: 1.1 }}>
            ·
          </Typography>
          <TrustValue level={confidence} size="small" />
        </Stack>
      ) : null}
    </OpportunityProviderRow>
  );
}

function TopPickMetrics({ market, detailed, confidence }: { market: MarketOpportunity; detailed: boolean; confidence: 'Strong' | 'Medium' | 'Weak' }) {
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
      <OpportunityMetricItem label="Model hit chance" value={formatPercent(market.modelProbability)} metric="hitChance" />
      {showSportsbookPricing ? <OpportunityMetricItem label="Break-even" value={formatPercent(market.impliedProbability)} /> : null}
      <OpportunityMetricItem label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} metric="edge" />
      {detailed && market.showExpectedValue ? (
        <OpportunityMetricItem label="EV" value={formatPercent(market.displayExpectedValue ?? 0, { signed: true })} metric="ev" />
      ) : null}
      {!detailed ? (
        <>
          <OpportunityMetricItem label="Trust" value={<TrustValue level={confidence} sx={{ fontSize: 14, fontWeight: 950, lineHeight: 1.5 }} />} metric="trust" />
        </>
      ) : null}
      <OpportunityMetricItem label="Sample" value={String(market.historicalSampleSize)} />
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
  return (
    <>
      <Button
        variant="contained"
        size="small"
        startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
        onClick={() => onAdd?.(market)}
        disabled={incompatible}
        sx={entryButtonSx(selected, incompatible)}
      >
        {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
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
  return (
    <>
      <Button
        variant="contained"
        size="small"
        startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
        onClick={() => onAdd?.(market)}
        disabled={incompatible}
        sx={mobileEntryButtonSx(selected, incompatible)}
      >
        {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
      </Button>
      <Button variant="outlined" size="small" onClick={onDetails} sx={mobileCardDetailsButtonSx}>
        Details
      </Button>
    </>
  );
}

function resolveConfidence(market: MarketOpportunity): 'Strong' | 'Medium' | 'Weak' {
  const warningText = renderWarningText(market.warnings).toLowerCase();
  const hasMajorCaution =
    market.estimatedEv <= 0 ||
    market.freshness === 'Unavailable' ||
    warningText.includes('low sample') ||
    warningText.includes('limited') ||
    warningText.includes('non-positive');

  if (hasMajorCaution || market.historicalSampleSize < 35) {
    return 'Weak';
  }

  const hasModerateCaution = hasRenderableWarning(market.warnings) || market.projectionModifier !== 'Normal' || market.historicalSampleSize < 90;

  const edge = market.probabilityEdge ?? 0;
  if (edge > 0.1 && market.historicalSampleSize >= 90 && !hasModerateCaution) {
    return 'Strong';
  }

  return edge > 0 ? 'Medium' : 'Weak';
}

function cleanWarning(warning: string) {
  const replacements: Record<string, string> = {
    'Provider limited for this sport': 'Limited coverage',
    'Confirm final payout in provider app': 'Payout may vary',
    'NBA sample is thin for this market': 'Limited history',
    'Low sample size': 'Low sample'
  };

  return replacements[warning] ?? warning;
}

export function getTopPickConfidence(market: MarketOpportunity): 'Strong' | 'Medium' | 'Weak' {
  return resolveConfidence(market);
}

export function formatTopPickWarning(market: MarketOpportunity) {
  return renderWarningText(market.warnings, '-');
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
