import { Grid, Stack } from '@mui/material';
import { LineShoppingCard, LineShoppingTable } from '../../components/markets/LineShoppingTable';
import { MarketFilters } from '../../components/markets/MarketFilters';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockMarkets } from '../../data/mockMarkets';
import { useResponsiveView } from '../../hooks/useResponsiveView';

export function LineShoppingPage() {
  const { isMobile } = useResponsiveView();
  return (
    <Stack spacing={3}>
      <PageHeader title="Line Shopping" description="Compare DFS projections against sportsbook context without mixing incompatible providers." />
      <MarketFilters />
      {isMobile ? (
        <Grid container spacing={1.5}>{mockMarkets.map((market) => <Grid key={market.id} size={12}><LineShoppingCard market={market} /></Grid>)}</Grid>
      ) : <LineShoppingTable markets={mockMarkets} />}
    </Stack>
  );
}
