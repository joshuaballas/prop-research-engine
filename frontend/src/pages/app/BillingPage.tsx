import CreditCardOutlinedIcon from '@mui/icons-material/CreditCardOutlined';
import SwapHorizOutlinedIcon from '@mui/icons-material/SwapHorizOutlined';
import { Box, Button, Divider, Stack, Typography } from '@mui/material';
import { BillingSummaryCard } from '../../components/billing/BillingSummaryCard';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { destructiveOutlineButtonSx } from '../../components/ui/buttonStyles';
import { mockPricingPlans } from '../../data/mockPricingPlans';
import { mockUser } from '../../data/mockUser';
import { useToast } from '../../hooks/useToast';
import { palette } from '../../theme/palette';
import type { PricingPlan } from '../../types/billing';
import { formatCurrency } from '../../utils/formatters';

function BillingPlanOption({ plan, isCurrent }: { plan: PricingPlan; isCurrent: boolean }) {
  const { showToast } = useToast();
  const actionLabel = isCurrent ? 'Current plan' : `Switch to ${plan.name}`;

  return (
    <AppCard
      sx={{
        height: '100%',
        borderColor: isCurrent ? palette.borderStrong : palette.border,
        background: isCurrent
          ? 'linear-gradient(180deg, rgba(17, 34, 58, 0.98), rgba(8, 16, 32, 0.98))'
          : 'linear-gradient(180deg, rgba(17, 28, 49, 0.94), rgba(10, 16, 32, 0.96))'
      }}
      contentSx={{ height: '100%', display: 'flex', flexDirection: 'column', gap: 2 }}
    >
      <Stack spacing={1}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={1}>
          <Typography variant="h5" fontWeight={950}>
            {plan.name}
          </Typography>
          {isCurrent ? (
            <Typography fontWeight={900} sx={{ color: palette.success }}>
              Active
            </Typography>
          ) : null}
        </Stack>
        <Typography color="text.secondary" sx={{ minHeight: { md: 48 } }}>
          {plan.description}
        </Typography>
      </Stack>

      <Stack spacing={0.4}>
        <Typography variant="h4" fontWeight={950}>
          {formatCurrency(plan.priceCents)}
          <Typography component="span" variant="body2" color="text.secondary" fontWeight={800}>
            /{plan.billingInterval}
          </Typography>
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {plan.entitlements.slice(0, 2).join(' · ')}
        </Typography>
      </Stack>

      <Divider />

      <Stack spacing={0.8}>
        {plan.features.slice(0, 3).map((feature) => (
          <Typography key={feature} variant="body2" color="text.secondary">
            {feature}
          </Typography>
        ))}
      </Stack>

      <Button
        variant={isCurrent ? 'outlined' : 'contained'}
        disabled={isCurrent}
        startIcon={<SwapHorizOutlinedIcon />}
        onClick={() => showToast(`${actionLabel} action will be available when billing is connected.`, 'info')}
        sx={{ mt: 'auto' }}
      >
        {actionLabel}
      </Button>
    </AppCard>
  );
}

export function BillingPage() {
  const { showToast } = useToast();

  return (
    <Stack spacing={3} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader title="Billing" description="Manage your plan, subscription status, and payment details." />

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1.05fr) minmax(320px, 0.95fr)' },
          gap: 2
        }}
      >
        <BillingSummaryCard />

        <AppCard sx={{ height: '100%' }}>
          <Stack spacing={1.8} sx={{ height: '100%' }}>
            <Stack spacing={0.35}>
              <Typography variant="h5" fontWeight={950}>
                Billing actions
              </Typography>
              <Typography color="text.secondary">
                Subscription changes and payment updates will be handled securely once billing is connected.
              </Typography>
            </Stack>
            <Divider />
            <Stack direction={{ xs: 'column', sm: 'row', lg: 'column', xl: 'row' }} spacing={1}>
              <Button
                variant="contained"
                startIcon={<SwapHorizOutlinedIcon />}
                onClick={() => showToast('Plan change action will be available when billing is connected.', 'info')}
              >
                Change plan
              </Button>
              <Button
                variant="outlined"
                startIcon={<CreditCardOutlinedIcon />}
                onClick={() => showToast('Payment update action will be available when billing is connected.', 'info')}
              >
                Update payment method
              </Button>
            </Stack>
            <Button
              variant="outlined"
              onClick={() => showToast('Cancellation flow will be available when billing is connected.', 'warning')}
              sx={{
                ...destructiveOutlineButtonSx,
                alignSelf: { xs: 'stretch', sm: 'flex-start' },
                mt: 'auto'
              }}
            >
              Cancel subscription
            </Button>
          </Stack>
        </AppCard>
      </Box>

      <Stack spacing={1.25}>
        <Typography variant="h5" fontWeight={950}>
          Plan options
        </Typography>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', md: 'repeat(3, minmax(0, 1fr))' },
            gap: 2,
            alignItems: 'stretch'
          }}
        >
          {mockPricingPlans.map((plan) => (
            <BillingPlanOption key={plan.code} plan={plan} isCurrent={plan.name === mockUser.planName} />
          ))}
        </Box>
      </Stack>
    </Stack>
  );
}
