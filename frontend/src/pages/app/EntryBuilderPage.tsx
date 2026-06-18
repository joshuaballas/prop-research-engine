import { Box, Button, Grid, Stack, Typography } from '@mui/material';
import { EntryCandidateCard } from '../../components/entry-builder/EntryCandidateCard';
import { DataStateBanner } from '../../components/ui/DataStateBanner';
import { PageHeader } from '../../components/ui/PageHeader';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { useEntryCandidateData } from '../../hooks/useMarketData';
import type { MarketOpportunity } from '../../types/markets';

export function EntryBuilderPage() {
  const builder = useEntryBuilder();
  const candidatesQuery = useEntryCandidateData(40);
  const lockedProvider = builder.lockedProviderName;
  const lockedGroup = builder.lockedProviderKey;
  const candidates = candidatesQuery.markets;
  const visibleCandidates = lockedGroup && !builder.showAllCandidates ? candidates.filter((market) => market.entryCompatibilityGroup === lockedGroup) : candidates;

  const handleAddLeg = (market: MarketOpportunity) => {
    builder.toggleLeg(market);
  };

  return (
    <Stack
      spacing={3}
      sx={{
        position: 'relative',
        isolation: 'isolate',
        pb: builder.legs.length && !builder.isDrawerOpen ? { xs: 'calc(64px + env(safe-area-inset-bottom))', md: 0 } : 0
      }}
    >
      <PageHeader title="Entry Builder" description="Build a provider-compatible DFS entry from current candidate legs." />
      <DataStateBanner
        title="Provider rules are enforced"
        message="Choose a DFS leg to lock the entry to that provider. You can mix sports when that provider allows it."
        status="Fresh"
      />

      <Stack spacing={1.5}>
        <Stack spacing={1}>
          <Typography variant="h5">Candidate legs</Typography>
          <Typography color="text.secondary">
            Add DFS candidates one at a time. Sportsbook reference lines stay out of the entry.
          </Typography>
        </Stack>

        {!builder.legs.length ? (
          <Box
            sx={{
              border: '1px dashed rgba(148,163,184,.24)',
              borderRadius: 3,
              p: 1.75,
              background: 'rgba(15,23,42,.32)'
            }}
          >
            <Typography fontWeight={950}>Add a DFS leg to start an entry.</Typography>
            <Typography variant="body2" color="text.secondary">
              The first leg locks the provider.
            </Typography>
          </Box>
        ) : (
          <Stack
            direction={{ xs: 'column', sm: 'row' }}
            alignItems={{ sm: 'center' }}
            justifyContent="space-between"
            gap={1}
            sx={{
              border: '1px solid rgba(34,211,238,.2)',
              borderRadius: 3,
              p: 1.5,
              background: '#081321'
            }}
          >
            <Typography variant="body2" color="text.secondary">
              Entry locked to <Box component="span" sx={{ color: 'text.primary', fontWeight: 900 }}>{lockedProvider}</Box>. {builder.showAllCandidates ? 'Showing all candidates.' : 'Showing compatible legs.'}
            </Typography>
            <Button
              variant="text"
              size="small"
              disableRipple
              onClick={builder.toggleShowAllCandidates}
              sx={{
                alignSelf: { xs: 'flex-start', sm: 'center' },
                minWidth: 'auto',
                p: 0,
                justifyContent: 'flex-start',
                textAlign: 'left',
                backgroundColor: 'transparent',
                '&:hover, &:focus, &:active': {
                  backgroundColor: 'transparent'
                },
                WebkitTapHighlightColor: 'transparent'
              }}
            >
              {builder.showAllCandidates ? 'Show compatible legs' : 'Show all candidates'}
            </Button>
          </Stack>
        )}
      </Stack>

      <Grid container spacing={1.5} alignItems="stretch">
        {candidatesQuery.loading ? (
          <Grid size={12}>
            <StateCard message="Loading entry candidates..." />
          </Grid>
        ) : candidatesQuery.error ? (
          <Grid size={12}>
            <StateCard tone="error" message="Entry candidates could not be loaded from the market API." />
          </Grid>
        ) : visibleCandidates.length ? visibleCandidates.map((market) => (
          <Grid key={market.id} size={{ xs: 12, md: 6, lg: 4 }} sx={{ display: 'flex', alignSelf: 'stretch' }}>
            <EntryCandidateCard
              market={market}
              lockedProvider={lockedProvider}
              selected={builder.legs.some((leg) => leg.id === market.id)}
              onAdd={handleAddLeg}
            />
          </Grid>
        )) : (
          <Grid size={12}>
            <StateCard message="No compatible entry candidates are available." />
          </Grid>
        )}
      </Grid>

    </Stack>
  );
}

function StateCard({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <Box
      sx={{
        border: `1px solid ${tone === 'error' ? 'rgba(244,63,94,.32)' : 'rgba(148,163,184,.16)'}`,
        borderRadius: 3,
        bgcolor: 'rgba(15,23,42,.58)',
        p: 2
      }}
    >
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </Box>
  );
}
