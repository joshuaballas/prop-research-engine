import { Grid, Stack } from '@mui/material';
import { EntryBuilderTray } from '../../components/entry-builder/EntryBuilderTray';
import { TopPickCard } from '../../components/markets/TopPickCard';
import { DataStateBanner } from '../../components/ui/DataStateBanner';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockEntryRules } from '../../data/mockEntryRules';
import { mockTopPicks } from '../../data/mockTopPicks';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';

export function EntryBuilderPage() {
  const builder = useEntryBuilder();
  return (
    <Stack spacing={3}>
      <PageHeader title="Entry Builder" description="Provider-locked entry construction. DFS providers cannot be mixed." />
      <DataStateBanner title="Provider-scoped rules active" message="First leg locks the provider. Sports can mix only when provider rules allow." status="Fresh" />
      <Grid container spacing={3}>
        <Grid size={{ xs: 12, lg: 7 }}>
          <Grid container spacing={2}>
            {mockTopPicks.slice(0, 5).map((market) => (
              <Grid key={market.id} size={{ xs: 12, md: 6 }}>
                <TopPickCard market={market} onAdd={builder.addLeg} />
              </Grid>
            ))}
          </Grid>
        </Grid>
        <Grid size={{ xs: 12, lg: 5 }}>
          <EntryBuilderTray legs={builder.legs} rules={mockEntryRules} amount={builder.amount} onAmountChange={builder.setAmount} onRemove={builder.removeLeg} onClear={builder.clear} />
        </Grid>
      </Grid>
    </Stack>
  );
}
