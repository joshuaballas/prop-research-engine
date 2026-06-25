import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { MarketOpportunity } from '../../types/markets';
import { palette } from '../../theme/palette';
import { marketDisplayLabel } from '../../utils/marketDisplay';
import { renderOpportunityCautionText } from '../../utils/warnings';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { ProjectionModifierBadge } from '../markets/ProjectionModifierBadge';
import { StatusPill } from '../ui/StatusPill';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../ui/buttonStyles';
import {
  OpportunityCardLayout,
  OpportunityMetricGrid,
  OpportunityMetricItem,
  OpportunityPickText,
  OpportunityProviderRow
} from '../ui/OpportunityCard';

interface EntryCandidateCardProps {
  market: MarketOpportunity;
  lockedProvider?: string;
  selected?: boolean;
  onAdd: (market: MarketOpportunity) => void;
}

export function EntryCandidateCard({ market, lockedProvider, selected = false, onAdd }: EntryCandidateCardProps) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const incompatible = Boolean(lockedProvider && lockedProvider !== market.entryProvider);
  const warning = renderOpportunityCautionText(market.warnings);
  const actions = (
    <>
      <Button
        variant="contained"
        size="small"
        startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
        onClick={() => onAdd(market)}
        disabled={incompatible}
        sx={{
          ...compactActionButtonSx,
          minWidth: 106,
          ...(selected
            ? {
                ...removeActionButtonSx
              }
            : incompatible
              ? {
                  ...lockedActionButtonSx
                }
              : undefined)
        }}
      >
        {selected ? 'Remove' : 'Add leg'}
      </Button>
      <Button
        variant="outlined"
        size="small"
        onClick={() => setDetailsOpen(true)}
        sx={{ ...secondaryGhostButtonSx, minWidth: 106 }}
      >
        Details
      </Button>
    </>
  );

  return (
    <>
      <OpportunityCardLayout
        context={
          <>
            <Typography variant="caption" color="primary.main" fontWeight={900}>
              {marketDisplayLabel(market)}
            </Typography>
            <StatusPill label={market.strategyClassification} sx={{ height: 22 }} />
            <ProjectionModifierBadge modifier={market.projectionModifier} sx={{ height: 22 }} />
          </>
        }
        title={market.player}
        value={
          <Stack alignItems="flex-end" spacing={0.25} sx={{ flexShrink: 0 }}>
            <MetricLabelWithHelp metric="edge" align="right" />
            <Typography color={(market.probabilityEdge ?? 0) > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
              {formatSignedPercent(market.probabilityEdge)}
            </Typography>
          </Stack>
        }
        event={`${market.event} - ${market.startTime}`}
        market={<OpportunityPickText side={market.side} propType={market.propType} line={market.line} />}
        provider={
          <OpportunityProviderRow provider={market.entryProvider} category={market.providerCategory} />
        }
        metrics={
          <OpportunityMetricGrid columns={{ xs: 2, sm: 4 }} sx={{ borderTop: 0, pt: 0 }}>
            <OpportunityMetricItem label="Edge" value={formatSignedPercent(market.probabilityEdge)} metric="edge" />
            <OpportunityMetricItem label="Hit chance" value={formatPercent(market.modelProbability)} metric="hitChance" />
            <OpportunityMetricItem label="Trust" value={<TrustValue level={market.trustLevel} sx={{ fontSize: 14, fontWeight: 950, lineHeight: 1.5 }} />} metric="trust" />
            <OpportunityMetricItem label="Sample" value={String(market.historicalSampleSize)} />
          </OpportunityMetricGrid>
        }
        caution={warning || (incompatible ? `Locked to ${lockedProvider}` : undefined)}
        actions={actions}
        shellSx={{
          position: 'relative',
          width: '100%',
          height: '100%',
          borderColor: selected ? 'rgba(34,211,238,.42)' : incompatible ? 'rgba(245,158,11,.28)' : palette.border
        }}
        contentSx={{ p: 1.75 }}
      />
      <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
    </>
  );
}

function formatPercent(value: number | null | undefined) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  return `${(value * 100).toFixed(2)}%`;
}

function formatSignedPercent(value: number | null | undefined) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  const formatted = `${Math.abs(value * 100).toFixed(2)}%`;
  return value >= 0 ? `+${formatted}` : `-${formatted}`;
}
