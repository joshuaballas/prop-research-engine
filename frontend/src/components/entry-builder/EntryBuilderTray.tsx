import { Button, Stack, TextField, Typography } from '@mui/material';
import type { EntryLeg, EntryRule } from '../../types/entryBuilder';
import { EntryBuilderSummary } from './EntryBuilderSummary';
import { EntryLegCard } from './EntryLegCard';

export function EntryBuilderTray({
  legs,
  rules,
  amount,
  onAmountChange,
  onRemove,
  onClear
}: {
  legs: EntryLeg[];
  rules: EntryRule[];
  amount: number;
  onAmountChange: (value: number) => void;
  onRemove: (id: string) => void;
  onClear: () => void;
}) {
  return (
    <Stack spacing={2}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h5">Builder</Typography>
        <Button variant="outlined" onClick={onClear} disabled={!legs.length}>
          Clear
        </Button>
      </Stack>
      <TextField
        label="Entry amount"
        type="number"
        value={amount}
        onChange={(event) => onAmountChange(Number(event.target.value))}
        inputProps={{ min: 1 }}
      />
      <EntryBuilderSummary legs={legs} rules={rules} amount={amount} />
      <Stack spacing={1}>
        {legs.map((leg) => (
          <EntryLegCard key={leg.id} leg={leg} onRemove={onRemove} />
        ))}
        {!legs.length ? (
          <Typography color="text.secondary">Add a DFS candidate to lock this entry to one provider.</Typography>
        ) : null}
      </Stack>
    </Stack>
  );
}
