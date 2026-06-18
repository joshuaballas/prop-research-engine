import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { hasRenderableWarning, renderWarningText } from '../../utils/warnings';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { ProviderLabel } from '../providers/ProviderLabel';
import { AppCard } from '../ui/AppCard';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../ui/buttonStyles';

export function MarketCard({ market }: { market: MarketOpportunity }) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const builder = useEntryBuilder();
  const addable = isScannerAddable(market);
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const canAddFromScanner = addable && !incompatible;
  return (
    <AppCard contentSx={{ p: 1.75 }}>
      <Stack spacing={1.15}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
          <Stack spacing={0.2} minWidth={0}>
            <Typography fontWeight={950}>{market.player}</Typography>
            <Typography color="text.secondary" variant="body2">
              {market.side} {market.propType} {market.line}
            </Typography>
          </Stack>
          <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950} whiteSpace="nowrap">
            {formatPercent(market.estimatedEv, { signed: true })}
          </Typography>
        </Stack>

        <Typography color="text.secondary" variant="body2">
          {market.sport} - {market.event} - {market.startTime}
        </Typography>

        <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
          {market.providerCategory === 'Sportsbook' ? (
            <Stack spacing={0}>
              <Typography fontWeight={900}>Reference only</Typography>
              <Typography variant="caption" color="text.secondary">
                Sportsbook line
              </Typography>
            </Stack>
          ) : (
            <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
          )}
          <TrustValue level={market.trustLevel} size="small" />
        </Stack>

        <Stack direction="row" gap={0.6} flexWrap="wrap" useFlexGap>
          <StatusPill label={market.strategyClassification} />
          {market.projectionModifier === 'Unknown Promo' ? null : <ProjectionModifierBadge modifier={market.projectionModifier} />}
        </Stack>

        <Stack direction="row" justifyContent="space-between" sx={{ borderTop: '1px solid rgba(148,163,184,.14)', pt: 1 }}>
          <Metric label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} metric="edge" />
          <Metric label="Hit chance" value={formatPercent(market.modelProbability)} metric="hitChance" />
          <Metric label="Sample" value={String(market.historicalSampleSize)} />
          <Metric label="Line" value={String(market.line)} />
        </Stack>

        <Typography variant="body2" color={hasRenderableWarning(market.warnings) || !addable ? 'warning.main' : 'text.secondary'}>
          {formatWarning(market, addable)}
        </Typography>

        <Stack direction="row" spacing={1}>
          <Button
            size="small"
            variant={canAddFromScanner || selected ? 'contained' : 'outlined'}
            startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible && addable ? <LockRoundedIcon /> : canAddFromScanner ? <AddCircleOutlineRoundedIcon /> : undefined}
            disabled={incompatible && addable}
            onClick={() => (canAddFromScanner || selected ? builder.toggleLeg(market) : setDetailsOpen(true))}
            sx={entryButtonSx(selected, incompatible && addable, canAddFromScanner)}
          >
            {selected ? 'Remove' : incompatible && addable ? 'Locked' : canAddFromScanner ? 'Add leg' : 'Details'}
          </Button>
          <Button size="small" variant="outlined" onClick={() => setDetailsOpen(true)} sx={{ ...secondaryActionButtonSx, display: canAddFromScanner || selected ? 'inline-flex' : 'none' }}>
            Details
          </Button>
        </Stack>
      </Stack>
      <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
    </AppCard>
  );
}

const baseActionButtonSx = {
  ...compactActionButtonSx,
  width: 100,
  minWidth: 100,
  height: 32,
  fontSize: 12,
  whiteSpace: 'nowrap'
};

const secondaryActionButtonSx = {
  ...secondaryGhostButtonSx,
  width: 100,
  minWidth: 100,
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

  return canAdd ? baseActionButtonSx : secondaryActionButtonSx;
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

function Metric({ label, value, metric }: { label: string; value: string; metric?: 'ev' | 'edge' | 'hitChance' | 'trust' | 'warning' }) {
  return (
    <Stack spacing={0.15}>
      {metric ? <MetricLabelWithHelp metric={metric} /> : (
        <Typography variant="caption" color="text.secondary" fontWeight={850}>
          {label}
        </Typography>
      )}
      <Typography fontWeight={950}>{value}</Typography>
    </Stack>
  );
}

function formatWarning(market: MarketOpportunity, addable: boolean) {
  if (market.providerCategory === 'Sportsbook') return 'Reference lines are not entry legs.';
  if (market.estimatedEv <= 0) return 'Non-positive EV. Review details only.';
  if (market.freshness === 'Unavailable') return 'Limited coverage.';
  if (!addable && market.strategyClassification === 'Research') return 'Research row. Review before adding.';
  return renderWarningText(market.warnings, market.reasons[0] ?? 'Active DFS row');
}
