import { Grid, Stack } from '@mui/material';
import { MarketCard } from '../../components/markets/MarketCard';
import { MarketFilters } from '../../components/markets/MarketFilters';
import { MarketTable } from '../../components/markets/MarketTable';
import { SportToggleRail } from '../../components/navigation/SportToggleRail';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { mockMarkets } from '../../data/mockMarkets';
import { useSelectedSports } from '../../hooks/useSelectedSports';
import { useResponsiveView } from '../../hooks/useResponsiveView';

export function MarketsPage() {
  const sports = useSelectedSports();
  const { isMobile } = useResponsiveView();
  const markets = mockMarkets.filter((market) => sports.includesSport(market.sport));
  return (
    <Stack spacing={3}>
      <PageHeader title="Markets" description="Broad scanner across DFS providers and sportsbook context rows." />
      <SportToggleRail selectedSports={sports.selectedSports} onToggle={sports.toggleSport} />
      <MarketFilters />
      <SectionHeader title="Market board" description="Desktop uses a compact table; mobile uses cards." />
      {isMobile ? (
        <Grid container spacing={2}>{markets.map((market) => <Grid key={market.id} size={12}><MarketCard market={market} /></Grid>)}</Grid>
      ) : <MarketTable markets={markets} />}
    </Stack>
  );
}
