import { Box, Button, Stack, Typography } from '@mui/material';
import type { EntryLeg, EntryRule } from '../../types/entryBuilder';
import { palette } from '../../theme/palette';
import { buildEntryReview } from '../../utils/entryReview';
import { MetadataText } from '../ui/MetadataText';
import { ProviderLabel } from '../providers/ProviderLabel';

export function EntryBuilderSummary({
  legs,
  rules,
  amount,
  selectedEntryType,
  onEntryTypeChange
}: {
  legs: EntryLeg[];
  rules: EntryRule[];
  amount: number;
  selectedEntryType?: string;
  onEntryTypeChange?: (entryType: string) => void;
}) {
  const review = buildEntryReview(legs, rules, amount, selectedEntryType);
  const rule = review.rule;
  const averageEdge = legs.length ? legs.reduce((sum, leg) => sum + leg.probabilityEdge, 0) / legs.length : 0;
  const sports = Array.from(new Set(legs.map((leg) => leg.sport)));
  const possibleLegCounts = review.allowedFinalLegCounts.length ? review.allowedFinalLegCounts.join(', ') : rule?.allowedLegCounts.join(', ') || 'Unavailable';

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
        <Metric label="Avg Edge" value={legs.length ? formatSignedPercent(averageEdge) : '-'} tone={averageEdge > 0 ? 'fresh' : 'default'} />
        <Metric label="Est. multiplier" value={legs.length ? formatMultiplier(review.multiplier) : '-'} />
        <Metric label="Est. payout" value={legs.length ? formatPayout(review.estimatedPayout) : '-'} />
      </Box>

      <Stack spacing={0.8}>
        <MetricRow label="Allowed leg counts" value={rule ? possibleLegCounts : 'Select a provider leg'} />
        <MetricRow label="Entry type" value={review.selectedEntryType || 'Unavailable'} />
        <MetricRow label="Compatibility" value={rule ? `${rule.provider} legs only` : 'First leg locks provider'} />
      </Stack>

      {review.entryTypes.length > 1 ? (
        <Stack spacing={0.75}>
          <Typography variant="caption" color="text.secondary" fontWeight={900}>
            Entry type
          </Typography>
          <Stack direction="row" gap={0.75} flexWrap="wrap" useFlexGap>
            {review.entryTypes.map((entryType) => (
              <Button
                key={entryType}
                size="small"
                variant={entryType === review.selectedEntryType ? 'contained' : 'outlined'}
                onClick={() => onEntryTypeChange?.(entryType)}
                sx={{ minWidth: 76 }}
              >
                {entryType}
              </Button>
            ))}
          </Stack>
        </Stack>
      ) : null}

      {review.payoutUnavailableReason ? (
        <Typography variant="body2" color="text.secondary">
          {review.payoutUnavailableReason}
        </Typography>
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

function formatMultiplier(value: number | null) {
  return value === null ? 'Unavailable' : `${value.toFixed(2)}x`;
}

function formatPayout(value: number | null) {
  return value === null ? 'Unavailable' : `$${value.toFixed(2)}`;
}
