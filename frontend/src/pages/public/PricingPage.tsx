import { Box, Container, Grid, Stack, Switch, Typography } from '@mui/material';
import { PlanCard } from '../../components/billing/PlanCard';
import { AppCard } from '../../components/ui/AppCard';
import { StatusPill } from '../../components/ui/StatusPill';
import { mockPricingPlans } from '../../data/mockPricingPlans';

export function PricingPage() {
  return (
    <Container maxWidth="xl" sx={{ py: { xs: 5, md: 8 } }}>
      <Stack spacing={5}>
        <Stack spacing={1} textAlign="center" alignItems="center">
          <StatusPill label="Simple plans" />
          <Typography variant="h2">Choose the research workflow that fits your volume.</Typography>
          <Typography color="text.secondary" maxWidth={740}>
            Start with top picks, upgrade into line shopping and entry building, or bring a small research group into one shared workspace.
          </Typography>
          <Box display="flex" alignItems="center" gap={1}>
            <Typography>Monthly</Typography>
            <Switch checked />
            <Typography color="text.secondary">Annual billing coming soon</Typography>
          </Box>
        </Stack>

        <Grid container spacing={2.5} alignItems="stretch">
          {mockPricingPlans.map((plan) => (
            <Grid key={plan.code} size={{ xs: 12, md: 4 }}>
              <PlanCard plan={plan} />
            </Grid>
          ))}
        </Grid>

        <Grid container spacing={2.5}>
          <Grid size={{ xs: 12, md: 4 }}>
            <AppCard>
              <Typography variant="h6">Built for research</Typography>
              <Typography color="text.secondary" mt={1}>
                Plans unlock workflow depth, not promises. Performance views include sample-size context and warnings.
              </Typography>
            </AppCard>
          </Grid>
          <Grid size={{ xs: 12, md: 4 }}>
            <AppCard>
              <Typography variant="h6">Provider-aware entries</Typography>
              <Typography color="text.secondary" mt={1}>
                Entry tools are designed around DFS provider compatibility so incompatible legs stay separate.
              </Typography>
            </AppCard>
          </Grid>
          <Grid size={{ xs: 12, md: 4 }}>
            <AppCard>
              <Typography variant="h6">Status-first experience</Typography>
              <Typography color="text.secondary" mt={1}>
                Freshness, provider availability, and stale-data states are visible before you spend time comparing markets.
              </Typography>
            </AppCard>
          </Grid>
        </Grid>
      </Stack>
    </Container>
  );
}
