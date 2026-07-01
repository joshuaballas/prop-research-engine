import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined';
import TrendingUpOutlinedIcon from '@mui/icons-material/TrendingUpOutlined';
import { Box, Container, Grid, Stack, Typography } from '@mui/material';
import { PlanCard } from '../../components/billing/PlanCard';
import { AppCard } from '../../components/ui/AppCard';
import { MetadataText } from '../../components/ui/MetadataText';
import { mockPricingPlans } from '../../data/mockPricingPlans';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

export function PricingPage() {
  const trustNotes = [
    {
      icon: <TrendingUpOutlinedIcon />,
      title: 'Built for research',
      body: 'Plans unlock workflow depth, not promises. Performance views include sample-size context and clear caution states.'
    },
    {
      icon: <ShieldOutlinedIcon />,
      title: 'Entry compatibility',
      body: 'DFS entry tools keep incompatible legs separated, while sportsbook rows remain recommendation and comparison context.'
    },
    {
      icon: <LockOutlinedIcon />,
      title: 'Secure billing path',
      body: 'Checkout and subscription management are designed for secure billing once payment processing is connected.'
    }
  ];

  return (
    <Container maxWidth="xl" sx={{ py: { xs: 5, md: 8 } }}>
      <Stack spacing={{ xs: 4, md: 5 }}>
        <Stack spacing={1.35} textAlign="center" alignItems="center">
          <Typography variant="h2" maxWidth={860}>
            Choose your prop research workflow.
          </Typography>
          <Typography color="text.secondary" maxWidth={760} fontSize={{ md: 18 }}>
            Start with sportsbook and DFS line shopping, top candidates, and modifier-aware entry review.
          </Typography>
          <MetadataText sx={{ mt: 1 }}>Monthly pricing. Annual plans coming later.</MetadataText>
        </Stack>

        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', md: 'repeat(3, minmax(0, 1fr))' },
            gap: 2.5,
            alignItems: 'stretch'
          }}
        >
          {mockPricingPlans.map((plan) => (
            <Box key={plan.code} sx={{ display: 'flex', minWidth: 0 }}>
              <PlanCard plan={plan} />
            </Box>
          ))}
        </Box>

        <Grid container spacing={2.5}>
          {trustNotes.map((note) => (
            <Grid key={note.title} size={{ xs: 12, md: 4 }}>
              <AppCard sx={{ height: '100%', background: palette.cardSurface }}>
                <Stack direction="row" spacing={1.5} alignItems="flex-start">
                  <Box
                    sx={{
                      width: 42,
                      height: 42,
                      borderRadius: radius.featuredCard,
                      display: 'grid',
                      placeItems: 'center',
                      color: 'primary.main',
                      bgcolor: 'rgba(34,211,238,.1)',
                      border: '1px solid rgba(34,211,238,.22)',
                      flexShrink: 0
                    }}
                  >
                    {note.icon}
                  </Box>
                  <Box>
                    <Typography variant="h6">{note.title}</Typography>
                    <Typography color="text.secondary" mt={0.75}>
                      {note.body}
                    </Typography>
                  </Box>
                </Stack>
              </AppCard>
            </Grid>
          ))}
        </Grid>

        <Stack spacing={1} textAlign="center" alignItems="center">
          <Typography color="text.secondary" variant="body2">
            Prices are shown monthly. Secure checkout and billing management will be connected before accepting paid subscriptions.
          </Typography>
          <Typography color="text.secondary" variant="body2" maxWidth={820}>
            This is a research tool. Odds, projections, payouts, and availability should be confirmed in the provider app before submitting.
          </Typography>
        </Stack>
      </Stack>
    </Container>
  );
}
