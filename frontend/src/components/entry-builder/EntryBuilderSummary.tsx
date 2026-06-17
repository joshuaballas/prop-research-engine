import { Divider, Stack, Typography } from '@mui/material';
import type { EntryLeg, EntryRule } from '../../types/entryBuilder';
import { estimatePayout } from '../../utils/payoutEstimator';
import { AppCard } from '../ui/AppCard';
import { MetadataText } from '../ui/MetadataText';
import { ProviderLabel } from '../providers/ProviderLabel';

export function EntryBuilderSummary({ legs, rules, amount }: { legs: EntryLeg[]; rules: EntryRule[]; amount: number }) {
  const rule = legs[0] ? rules.find((item) => item.compatibilityGroup === legs[0].entryCompatibilityGroup) : undefined;
  const estimate = estimatePayout(legs, amount, rules);
  const averageEv = legs.length ? legs.reduce((sum, leg) => sum + leg.estimatedEv, 0) / legs.length : 0;

  return (
    <AppCard>
      <Stack spacing={1.5}>
        <Typography variant="h6">Entry Summary</Typography>
        <Stack direction="row" gap={1} flexWrap="wrap">
          {rule ? <ProviderLabel provider={rule.provider} category={rule.providerCategory} compact /> : <MetadataText>No provider locked</MetadataText>}
          <MetadataText>{legs.length} legs</MetadataText>
          <MetadataText tone={averageEv > 0 ? 'fresh' : 'default'}>Avg EV {(averageEv * 100).toFixed(1)}%</MetadataText>
        </Stack>
        <Divider />
        <Metric label="Allowed legs" value={rule ? rule.allowedLegCounts.join(', ') : 'Select a leg'} />
        <Metric label="Entry types" value={rule ? rule.entryTypes.join(', ') : 'Select a provider'} />
        <Metric label="Estimated multiplier" value={legs.length ? `${estimate.multiplier.toFixed(2)}x` : '-'} />
        <Metric label="Estimated payout" value={legs.length ? `$${estimate.estimatedPayout.toFixed(2)}` : '-'} />
        {estimate.warning ? (
          <Typography variant="body2" color="warning.main">
            {estimate.warning}
          </Typography>
        ) : null}
      </Stack>
    </AppCard>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" justifyContent="space-between">
      <Typography color="text.secondary">{label}</Typography>
      <Typography fontWeight={900}>{value}</Typography>
    </Stack>
  );
}
