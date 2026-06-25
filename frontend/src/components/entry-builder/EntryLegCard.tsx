import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import { Button, Typography } from '@mui/material';
import type { EntryLeg } from '../../types/entryBuilder';
import { formatPercent } from '../../utils/formatters';
import { marketDisplayLabel } from '../../utils/marketDisplay';
import { renderOpportunityCautionText } from '../../utils/warnings';
import { ProjectionModifierBadge } from '../markets/ProjectionModifierBadge';
import { TrustValue } from '../metrics/TrustValue';
import { mobileCardActionButtonSx } from '../ui/AppRowActions';
import { removeActionButtonSx } from '../ui/buttonStyles';
import { OpportunityCardLayout, OpportunityMetricGrid, OpportunityMetricItem, OpportunityPickText, OpportunityProviderRow } from '../ui/OpportunityCard';

export function EntryLegCard({ leg, onRemove }: { leg: EntryLeg; onRemove?: (id: string) => void }) {
  const warning = renderOpportunityCautionText(leg.warnings);

  return (
    <OpportunityCardLayout
      compact
      shellSx={{ borderRadius: 3 }}
      contentSx={{ p: 1.35, '&:last-child': { pb: 1.35 } }}
      title={leg.player}
      value={
        <Typography color={(leg.probabilityEdge ?? 0) > 0 ? 'success.main' : 'error.main'} fontWeight={950} sx={{ flexShrink: 0 }}>
          {formatSignedPercent(leg.probabilityEdge)}
        </Typography>
      }
      event={marketDisplayLabel(leg)}
      market={<OpportunityPickText side={leg.side} propType={leg.propType} line={leg.line} />}
      provider={
        <OpportunityProviderRow provider={leg.entryProvider} category={leg.providerCategory} sx={{ justifyContent: 'flex-start', gap: 0.8 }}>
          <ProjectionModifierBadge modifier={leg.projectionModifier} />
        </OpportunityProviderRow>
      }
      metrics={
        <OpportunityMetricGrid columns={{ xs: 2, sm: 4 }} sx={{ borderTop: 0, pt: 0 }}>
          <OpportunityMetricItem label="Edge" value={formatSignedPercent(leg.probabilityEdge)} metric="edge" />
          <OpportunityMetricItem label="Hit chance" value={formatPercent(leg.modelProbability)} metric="hitChance" />
          <OpportunityMetricItem label="Trust" value={<TrustValue level={leg.trustLevel} sx={{ fontSize: 14, fontWeight: 950, lineHeight: 1.5 }} />} metric="trust" />
          <OpportunityMetricItem label="Sample" value={String(leg.historicalSampleSize)} />
        </OpportunityMetricGrid>
      }
      caution={warning}
      actions={
        onRemove ? (
          <Button startIcon={<DeleteOutlineRoundedIcon />} onClick={() => onRemove(leg.id)} size="small" variant="contained" sx={{ ...mobileCardActionButtonSx, ...removeActionButtonSx }}>
            Remove
          </Button>
        ) : null
      }
    />
  );
}

function formatSignedPercent(value: number | null | undefined) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  const formatted = `${Math.abs(value * 100).toFixed(2)}%`;
  return value >= 0 ? `+${formatted}` : `-${formatted}`;
}
