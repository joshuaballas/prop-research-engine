import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { Box, Button, IconButton, Stack, TextField, Typography } from '@mui/material';
import type { EntryLeg, EntryRule } from '../../types/entryBuilder';
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
        <EntryBuilderSummary legs={legs} rules={rules} amount={amount} />

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
                background: 'rgba(15,23,42,.42)'
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
        <Button variant="contained" disabled={!legs.length}>
          Review entry
        </Button>
        <Button variant="outlined" size="small" onClick={onClear} disabled={!legs.length}>
          Clear entry
        </Button>
      </Stack>
    </Box>
  );
}
