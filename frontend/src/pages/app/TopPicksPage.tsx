import { Grid, Stack } from '@mui/material';
import { TopPickCard } from '../../components/markets/TopPickCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockTopPicks } from '../../data/mockTopPicks';
import { useToast } from '../../hooks/useToast';

export function TopPicksPage() {
  const { showToast } = useToast();
  return (
    <Stack spacing={3}>
      <PageHeader title="Top Picks" description="Curated candidates ranked by model edge, EV, provider compatibility, and freshness." />
      <Grid container spacing={2}>
        {mockTopPicks.map((market) => (
          <Grid key={market.id} size={{ xs: 12, lg: 6, xl: 4 }}>
            <TopPickCard market={market} onAdd={() => showToast(`${market.player} added to mock entry.`, 'success')} />
          </Grid>
        ))}
      </Grid>
    </Stack>
  );
}
