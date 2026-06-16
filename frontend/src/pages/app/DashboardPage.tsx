import { Grid, Stack } from '@mui/material';
import { TopPickCard } from '../../components/markets/TopPickCard';
import { LineShoppingTable } from '../../components/markets/LineShoppingTable';
import { SportToggleRail } from '../../components/navigation/SportToggleRail';
import { DataStateBanner } from '../../components/ui/DataStateBanner';
import { MetricCard } from '../../components/ui/MetricCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { mockMarkets } from '../../data/mockMarkets';
import { mockTopPicks } from '../../data/mockTopPicks';
import { useSelectedSports } from '../../hooks/useSelectedSports';
import { formatPercent } from '../../utils/formatters';

export function DashboardPage() {
  const sports = useSelectedSports();
  const filtered = mockTopPicks.filter((market) => sports.includesSport(market.sport));
  return (
    <Stack spacing={3}>
      <PageHeader title="Dashboard" description="Multi-sport DFS research overview with provider-safe entry behavior." />
      <SportToggleRail selectedSports={sports.selectedSports} onToggle={sports.toggleSport} />
      <DataStateBanner title="Data is fresh" message="Provider status is healthy in this mock snapshot. Sportsbook rows remain market context only." status="Fresh" />
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}><MetricCard label="Active Candidates" value={`${filtered.length}`} helper="Selected sports" /></Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}><MetricCard label="Top EV" value={formatPercent(filtered[0]?.estimatedEv ?? 0, { signed: true })} helper={filtered[0]?.player} /></Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}><MetricCard label="DFS Live" value="4" helper="PrizePicks, UD, Pick6, Betr" /></Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}><MetricCard label="Last Refresh" value="4m" helper="Customer-safe freshness" /></Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 2.4 }}><MetricCard label="Sample" value="200" helper="Early validation" /></Grid>
      </Grid>
      <SectionHeader title="Top opportunities" description="Ranked candidates and watchlist rows across selected sports." />
      <Grid container spacing={2}>
        {filtered.slice(0, 3).map((market) => <Grid key={market.id} size={{ xs: 12, md: 4 }}><TopPickCard market={market} /></Grid>)}
      </Grid>
      <SectionHeader title="Line shopping preview" description="Best DFS provider, market context, and entry compatibility." />
      <LineShoppingTable markets={mockMarkets.slice(0, 5)} />
    </Stack>
  );
}
