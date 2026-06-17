import { Box, Divider, Stack, Typography } from '@mui/material';
import type { EntryLeg, EntryRule } from '../../types/entryBuilder';
import { palette } from '../../theme/palette';
import { estimatePayout } from '../../utils/payoutEstimator';
import { MetadataText } from '../ui/MetadataText';
import { ProviderLabel } from '../providers/ProviderLabel';

export function EntryBuilderSummary({ legs, rules, amount }: { legs: EntryLeg[]; rules: EntryRule[]; amount: number }) {
  const rule = legs[0] ? rules.find((item) => item.compatibilityGroup === legs[0].entryCompatibilityGroup) : undefined;
  const estimate = estimatePayout(legs, amount, rules);
  const averageEv = legs.length ? legs.reduce((sum, leg) => sum + leg.estimatedEv, 0) / legs.length : 0;
  const sports = Array.from(new Set(legs.map((leg) => leg.sport)));

  return (
    <Stack spacing={1.5}>
      <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center">
        {rule ? <ProviderLabel provider={rule.provider} category={rule.providerCategory} compact /> : <MetadataText>No provider locked</MetadataText>}
        <MetadataText>{legs.length} legs</MetadataText>
        <MetadataText>{sports.length ? sports.join(', ') : 'No sports selected'}</MetadataText>
      </Stack>

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(3, minmax(0, 1fr))' },
          gap: 1,
          borderTop: `1px solid ${palette.border}`,
          borderBottom: `1px solid ${palette.border}`,
          py: 1.35
        }}
      >
        <Metric label="Avg EV" value={legs.length ? formatSignedPercent(averageEv) : '-'} tone={averageEv > 0 ? 'fresh' : 'default'} />
        <Metric label="Est. multiplier" value={legs.length ? `${estimate.multiplier.toFixed(2)}x` : '-'} />
        <Metric label="Est. payout" value={legs.length ? `$${estimate.estimatedPayout.toFixed(2)}` : '-'} />
      </Box>

      <Stack spacing={0.8}>
        <MetricRow label="Allowed leg counts" value={rule ? rule.allowedLegCounts.join(', ') : 'Select a DFS leg'} />
        <MetricRow label="Entry types" value={rule ? rule.entryTypes.join(', ') : 'Select a provider'} />
        <MetricRow label="Compatibility" value={rule ? `${rule.provider} legs only` : 'First leg locks provider'} />
      </Stack>

      {estimate.warning ? (
        <>
          <Divider />
          <Typography variant="body2" color="warning.main" fontWeight={800}>
            Payout may vary. Final terms should be checked in the DFS app.
          </Typography>
        </>
      ) : null}
    </Stack>
  );
}

function Metric({ label, value, tone = 'default' }: { label: string; value: string; tone?: 'fresh' | 'default' }) {
  const color = tone === 'fresh' ? 'success.main' : 'text.primary';
  return (
    <Stack spacing={0.25}>
      <Typography variant="caption" color="text.secondary" fontWeight={900}>
        {label}
      </Typography>
      <Typography fontWeight={950} color={color}>
        {value}
      </Typography>
    </Stack>
  );
}

function MetricRow({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" justifyContent="space-between" gap={1}>
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2" fontWeight={900} textAlign="right">
        {value}
      </Typography>
    </Stack>
  );
}

function formatSignedPercent(value: number) {
  const formatted = `${Math.abs(value * 100).toFixed(1)}%`;
  return value >= 0 ? `+${formatted}` : `-${formatted}`;
}
