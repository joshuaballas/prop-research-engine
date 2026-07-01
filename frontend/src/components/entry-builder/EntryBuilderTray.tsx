import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { useEffect, useState } from 'react';
import { Box, Button, IconButton, Stack, TextField, Typography } from '@mui/material';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';
import type { EntryLeg, EntryRule } from '../../types/entryBuilder';
import { buildEntryReview, resolveEntryType } from '../../utils/entryReview';
import { EntryReceiptDialog } from './EntryReceiptDialog';
import { EntryBuilderSummary } from './EntryBuilderSummary';
import { EntryLegCard } from './EntryLegCard';

export function EntryBuilderTray({
  legs,
  rules,
  amount,
  selectedEntryType,
  onAmountChange,
  onEntryTypeChange,
  onRemove,
  onClear,
  onClose
}: {
  legs: EntryLeg[];
  rules: EntryRule[];
  amount: number;
  selectedEntryType: string;
  onAmountChange: (value: number) => void;
  onEntryTypeChange: (entryType: string) => void;
  onRemove: (id: string) => void;
  onClear: () => void;
  onClose?: () => void;
}) {
  const [receiptOpen, setReceiptOpen] = useState(false);
  const review = buildEntryReview(legs, rules, amount, selectedEntryType);

  useEffect(() => {
    const nextEntryType = resolveEntryType(review.entryTypes, selectedEntryType);
    if (nextEntryType !== selectedEntryType) {
      onEntryTypeChange(nextEntryType);
    }
  }, [onEntryTypeChange, review.entryTypes, selectedEntryType]);

  useEffect(() => {
    if (!legs.length) {
      setReceiptOpen(false);
    }
  }, [legs.length]);

  const handleClear = () => {
    setReceiptOpen(false);
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
          onEntryTypeChange={onEntryTypeChange}
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
                borderRadius: radius.card,
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
      </Stack>

      <Stack spacing={1} sx={{ p: 2, pt: 1.5, borderTop: '1px solid rgba(148,163,184,.16)' }}>
        <Button variant="contained" disabled={!legs.length} onClick={() => setReceiptOpen(true)}>
          Track entry
        </Button>
        <Button variant="outlined" size="small" onClick={handleClear} disabled={!legs.length}>
          Clear entry
        </Button>
      </Stack>
      <EntryReceiptDialog
        open={receiptOpen}
        onClose={() => setReceiptOpen(false)}
        legs={legs}
        review={review}
        amount={amount}
      />
    </Box>
  );
}
