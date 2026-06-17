import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined';
import TrendingUpOutlinedIcon from '@mui/icons-material/TrendingUpOutlined';
import { Box, Container, Grid, Stack, Typography } from '@mui/material';
import { PlanCard } from '../../components/billing/PlanCard';
import { AppCard } from '../../components/ui/AppCard';
import { StatusPill } from '../../components/ui/StatusPill';
import { mockPricingPlans } from '../../data/mockPricingPlans';

export function PricingPage() {
  const trustNotes = [
    {
      icon: <TrendingUpOutlinedIcon />,
      title: 'Built for research',
      body: 'Plans unlock workflow depth, not promises. Performance views include sample-size context and clear caution states.'
    },
    {
      icon: <ShieldOutlinedIcon />,
      title: 'Provider-aware entries',
      body: 'Entry tools are designed around DFS provider compatibility so incompatible legs stay separated before review.'
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
          <StatusPill label="DFS research plans" />
          <Typography variant="h2" maxWidth={860}>
            Choose your DFS research workflow.
          </Typography>
          <Typography color="text.secondary" maxWidth={760} fontSize={{ md: 18 }}>
            Start with provider-safe line shopping, top candidates, and modifier-aware entry research.
          </Typography>
          <Box
            sx={{
              mt: 1,
              display: 'flex',
              alignItems: 'center',
              gap: 1,
              px: 1.4,
              py: 0.8,
              borderRadius: 999,
              border: '1px solid rgba(148,163,184,.16)',
              bgcolor: 'rgba(8,13,26,.34)'
            }}
          >
            <Typography variant="body2" fontWeight={900}>
              Monthly pricing
            </Typography>
            <Box sx={{ width: 4, height: 4, borderRadius: 999, bgcolor: 'rgba(148,163,184,.45)' }} />
            <Typography variant="body2" color="text.secondary" fontWeight={760}>
              Annual plans coming later
            </Typography>
          </Box>
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
              <AppCard sx={{ height: '100%', background: 'rgba(15,23,42,.76)' }}>
                <Stack direction="row" spacing={1.5} alignItems="flex-start">
                  <Box
                    sx={{
                      width: 42,
                      height: 42,
                      borderRadius: 2,
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
            EdgeScope is a research tool. Projections, payouts, and availability should be confirmed in the provider app before submitting entries.
          </Typography>
        </Stack>
      </Stack>
    </Container>
  );
}
