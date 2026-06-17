import { Grid, Stack } from '@mui/material';
import { BillingSummaryCard } from '../../components/billing/BillingSummaryCard';
import { PlanCard } from '../../components/billing/PlanCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockPricingPlans } from '../../data/mockPricingPlans';

export function BillingPage() {
  return (
    <Stack spacing={3}>
      <PageHeader title="Billing" description="Manage plan and subscription details." />
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 4 }}><BillingSummaryCard /></Grid>
        {mockPricingPlans.map((plan) => (
          <Grid key={plan.code} size={{ xs: 12, md: 4 }}>
            <PlanCard plan={plan} />
          </Grid>
        ))}
      </Grid>
    </Stack>
  );
}
