import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { useEffect, useState } from 'react';
import { Box, Button, Chip, Divider, IconButton, Stack, TextField, Typography } from '@mui/material';
import { palette } from '../../theme/palette';
import type { EntryLeg, EntryRule } from '../../types/entryBuilder';
import { buildEntryReview, resolveEntryType } from '../../utils/entryReview';
import { EntryBuilderSummary } from './EntryBuilderSummary';
import { EntryLegCard } from './EntryLegCard';

export function EntryBuilderTray({
  legs,
  rules,
  amount,
  onAmountChange,
  onRemove,
  onClear,
  onClose
}: {
  legs: EntryLeg[];
  rules: EntryRule[];
  amount: number;
  onAmountChange: (value: number) => void;
  onRemove: (id: string) => void;
  onClear: () => void;
  onClose?: () => void;
}) {
  const [selectedEntryType, setSelectedEntryType] = useState('');
  const [reviewOpen, setReviewOpen] = useState(false);
  const review = buildEntryReview(legs, rules, amount, selectedEntryType);

  useEffect(() => {
    const nextEntryType = resolveEntryType(review.entryTypes, selectedEntryType);
    if (nextEntryType !== selectedEntryType) {
      setSelectedEntryType(nextEntryType);
    }
  }, [review.entryTypes, selectedEntryType]);

  useEffect(() => {
    if (!legs.length) {
      setReviewOpen(false);
    }
  }, [legs.length]);

  const handleClear = () => {
    setReviewOpen(false);
    onClear();
  };

  return (
    <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      <Stack spacing={0.25} sx={{ p: 2, pb: 1.5, borderBottom: '1px solid rgba(148,163,184,.16)' }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
          <Box>
            <Typography variant="h5">Current entry</Typography>
            <Typography variant="body2" color="text.secondary">
              {legs.length ? 'Provider locked. Add compatible legs only.' : 'No legs selected'}
            </Typography>
          </Box>
          {onClose ? (
            <IconButton onClick={onClose} size="small" aria-label="Close entry slip">
              <CloseRoundedIcon />
            </IconButton>
          ) : null}
        </Stack>
      </Stack>

      <Stack className="app-scrollbar" spacing={2} sx={{ p: 2, overflowY: 'auto', flex: 1 }}>
        <EntryBuilderSummary
          legs={legs}
          rules={rules}
          amount={amount}
          selectedEntryType={review.selectedEntryType}
          onEntryTypeChange={setSelectedEntryType}
        />

        <TextField
          label="Entry amount"
          type="number"
          value={amount}
          onChange={(event) => onAmountChange(Number(event.target.value))}
          inputProps={{ min: 1 }}
          size="small"
          sx={{ maxWidth: 180 }}
        />

        <Stack spacing={1}>
          {legs.map((leg) => (
            <EntryLegCard key={leg.id} leg={leg} onRemove={onRemove} />
          ))}
          {!legs.length ? (
            <Stack
              spacing={0.75}
              sx={{
                border: '1px dashed rgba(148,163,184,.28)',
                borderRadius: 3,
                p: 1.75,
                background: palette.cardSurface
              }}
            >
              <Typography fontWeight={950}>No legs selected</Typography>
              <Typography variant="body2" color="text.secondary">
                Add a DFS candidate to start an entry.
              </Typography>
              <Typography variant="body2" color="text.secondary">
                The first leg locks the provider.
              </Typography>
            </Stack>
          ) : null}
        </Stack>

        {reviewOpen ? <EntryReviewPanel review={review} amount={amount} /> : null}
      </Stack>

      <Stack spacing={1} sx={{ p: 2, pt: 1.5, borderTop: '1px solid rgba(148,163,184,.16)' }}>
        <Button variant="contained" disabled={!legs.length} onClick={() => setReviewOpen(true)}>
          Review entry
        </Button>
        <Button variant="outlined" size="small" onClick={handleClear} disabled={!legs.length}>
          Clear entry
        </Button>
      </Stack>
    </Box>
  );
}

function EntryReviewPanel({ review, amount }: { review: ReturnType<typeof buildEntryReview>; amount: number }) {
  const ready = review.status === 'Ready';
  return (
    <Stack
      spacing={1.35}
      sx={{
        border: `1px solid ${ready ? 'rgba(34,197,94,.28)' : 'rgba(245,158,11,.32)'}`,
        borderRadius: 3,
        p: 1.5,
        backgroundColor: 'rgba(15, 23, 42, 0.42)'
      }}
    >
      <Stack direction="row" alignItems="center" justifyContent="space-between" gap={1}>
        <Box>
          <Typography fontWeight={950}>Entry review</Typography>
          <Typography variant="body2" color="text.secondary">
            {ready ? 'Ready to build' : review.status}
          </Typography>
        </Box>
        <Chip
          size="small"
          label={ready ? 'Ready' : review.status}
          color={ready ? 'success' : 'warning'}
          sx={{ fontWeight: 900 }}
        />
      </Stack>

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
          gap: 1
        }}
      >
        <ReviewMetric label="Provider" value={review.provider} />
        <ReviewMetric label="Legs" value={String(review.legCount)} />
        <ReviewMetric label="Entry type" value={review.selectedEntryType || 'Unavailable'} />
        <ReviewMetric label="Amount" value={`$${amount.toFixed(2)}`} />
        <ReviewMetric label="Multiplier" value={review.multiplier === null ? 'Unavailable' : `${review.multiplier.toFixed(2)}x`} />
        <ReviewMetric label="Est. payout" value={review.estimatedPayout === null ? 'Unavailable' : `$${review.estimatedPayout.toFixed(2)}`} />
      </Box>

      <Divider />

      <ReviewList
        title="Blocking issues"
        emptyText={ready ? 'No blocking issues.' : 'No additional blockers.'}
        items={review.blockers}
        tone="warning"
      />
      <ReviewList
        title="Warnings"
        emptyText="No additional warnings."
        items={review.warnings}
        tone="muted"
      />

      <Typography variant="body2" color="text.secondary">
        {review.multiplier === null
          ? review.payoutUnavailableReason ?? 'Payout unavailable. Confirm final payout in the DFS app.'
          : 'Final payout may vary. Confirm all lines and terms in the DFS app.'}
      </Typography>
    </Stack>
  );
}

function ReviewMetric({ label, value }: { label: string; value: string }) {
  return (
    <Stack spacing={0.2} sx={{ minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary" fontWeight={900}>
        {label}
      </Typography>
      <Typography fontWeight={950} sx={{ overflowWrap: 'anywhere' }}>
        {value}
      </Typography>
    </Stack>
  );
}

function ReviewList({ title, items, emptyText, tone }: { title: string; items: string[]; emptyText: string; tone: 'warning' | 'muted' }) {
  const color = tone === 'warning' ? 'warning.main' : 'text.secondary';
  return (
    <Stack spacing={0.6}>
      <Typography variant="caption" color="text.secondary" fontWeight={900}>
        {title}
      </Typography>
      {items.length ? (
        <Stack component="ul" spacing={0.45} sx={{ m: 0, pl: 2 }}>
          {items.map((item) => (
            <Typography key={item} component="li" variant="body2" color={color}>
              {item}
            </Typography>
          ))}
        </Stack>
      ) : (
        <Typography variant="body2" color="text.secondary">
          {emptyText}
        </Typography>
      )}
    </Stack>
  );
}
