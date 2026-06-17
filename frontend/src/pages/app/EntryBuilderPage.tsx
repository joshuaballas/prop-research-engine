import { useEffect, useState } from 'react';
import { Box, Button, Drawer, Grid, Stack, Typography, useMediaQuery, useTheme } from '@mui/material';
import { EntryCandidateCard } from '../../components/entry-builder/EntryCandidateCard';
import { EntryBuilderTray } from '../../components/entry-builder/EntryBuilderTray';
import { DataStateBanner } from '../../components/ui/DataStateBanner';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockEntryRules } from '../../data/mockEntryRules';
import { mockTopPicks } from '../../data/mockTopPicks';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';

export function EntryBuilderPage() {
  const builder = useEntryBuilder();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [showAllCandidates, setShowAllCandidates] = useState(false);
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('md'));
  const lockedProvider = builder.legs[0]?.entryProvider;
  const lockedGroup = builder.legs[0]?.entryCompatibilityGroup;
  const candidates = mockTopPicks.filter((market) => market.providerCategory === 'DFS' && market.canAddToEntry);
  const visibleCandidates = lockedGroup && !showAllCandidates ? candidates.filter((market) => market.entryCompatibilityGroup === lockedGroup) : candidates;
  const averageEv = builder.legs.length ? builder.legs.reduce((sum, leg) => sum + leg.estimatedEv, 0) / builder.legs.length : 0;

  useEffect(() => {
    if (!builder.legs.length) {
      setDrawerOpen(false);
      setShowAllCandidates(false);
    }
  }, [builder.legs.length]);

  const handleAddLeg = (market: MarketOpportunity) => {
    const wasSelected = builder.legs.some((leg) => leg.id === market.id);
    const changed = builder.toggleLeg(market);
    if (changed && !wasSelected) {
      setShowAllCandidates(false);
      setDrawerOpen(true);
    }
  };

  const handleClear = () => {
    builder.clear();
    setShowAllCandidates(false);
  };

  return (
    <Stack
      spacing={3}
      sx={{
        position: 'relative',
        isolation: 'isolate',
        pb: builder.legs.length && !drawerOpen ? { xs: 'calc(136px + env(safe-area-inset-bottom))', md: 0 } : 0,
        mr: drawerOpen && !isMobile ? '360px' : 0,
        transition: 'margin-right 180ms ease'
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
              Entry locked to <Box component="span" sx={{ color: 'text.primary', fontWeight: 900 }}>{lockedProvider}</Box>. {showAllCandidates ? 'Showing all candidates.' : 'Showing compatible legs.'}
            </Typography>
            <Button
              variant="text"
              size="small"
              disableRipple
              onClick={() => setShowAllCandidates((current) => !current)}
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
              {showAllCandidates ? 'Show compatible legs' : 'Show all candidates'}
            </Button>
          </Stack>
        )}
      </Stack>

      <Grid container spacing={1.5} alignItems="stretch">
        {visibleCandidates.map((market) => (
          <Grid key={market.id} size={{ xs: 12, md: 6, lg: 4 }} sx={{ display: 'flex', alignSelf: 'stretch' }}>
            <EntryCandidateCard
              market={market}
              lockedProvider={lockedProvider}
              selected={builder.legs.some((leg) => leg.id === market.id)}
              onAdd={handleAddLeg}
            />
          </Grid>
        ))}
      </Grid>

      <Drawer
        anchor={isMobile ? 'bottom' : 'right'}
        variant={isMobile ? 'temporary' : 'persistent'}
        open={drawerOpen}
        onClose={isMobile ? () => setDrawerOpen(false) : undefined}
        ModalProps={{ keepMounted: true }}
        PaperProps={{
          sx: {
            width: { xs: '100%', md: 360 },
            top: { md: '67px' },
            height: { xs: '82vh', md: 'calc(100% - 67px)' },
            maxHeight: { xs: 680, md: 'none' },
            borderTopLeftRadius: { xs: 4, md: 0 },
            borderTopRightRadius: { xs: 4, md: 0 },
            borderLeft: { md: '1px solid rgba(125,211,252,.22)' },
            borderTop: { xs: '1px solid rgba(125,211,252,.22)', md: 'none' },
            background: 'linear-gradient(180deg, rgba(17,28,49,.98), rgba(7,11,20,.98))'
          }
        }}
      >
        <EntryBuilderTray
          legs={builder.legs}
          rules={mockEntryRules}
          amount={builder.amount}
          onAmountChange={builder.setAmount}
          onRemove={builder.removeLeg}
          onClear={handleClear}
          onClose={() => setDrawerOpen(false)}
        />
      </Drawer>

      {builder.legs.length && !drawerOpen ? (
        <Button
          variant="contained"
          onClick={() => setDrawerOpen(true)}
          sx={{
            position: 'fixed',
            zIndex: (item) => item.zIndex.drawer - 1,
            right: { xs: 16, md: 24 },
            left: { xs: 16, md: 'auto' },
            bottom: { xs: 'calc(72px + env(safe-area-inset-bottom))', md: 24 },
            justifyContent: 'space-between',
            px: 2,
            py: 1.1,
            boxShadow: '0 18px 45px rgba(34,211,238,.22)'
          }}
        >
          <Box component="span">
            {isMobile ? `${lockedProvider} - ${builder.legs.length} legs - Avg EV ${formatSignedPercent(averageEv)}` : `Entry: ${lockedProvider} - ${builder.legs.length} legs`}
          </Box>
        </Button>
      ) : null}
    </Stack>
  );
}

function formatSignedPercent(value: number) {
  const formatted = `${Math.abs(value * 100).toFixed(1)}%`;
  return value >= 0 ? `+${formatted}` : `-${formatted}`;
}
