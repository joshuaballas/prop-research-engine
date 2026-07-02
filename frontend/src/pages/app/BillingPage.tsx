import CreditCardOutlinedIcon from '@mui/icons-material/CreditCardOutlined';
import SwapHorizOutlinedIcon from '@mui/icons-material/SwapHorizOutlined';
import { Box, Button, Divider, Skeleton, Stack, Typography } from '@mui/material';
import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { createCheckoutIntent, getBilling } from '../../api/billing';
import { mapPlanDtoToPricingPlan } from '../../api/planMappers';
import { useApiQuery } from '../../api/useApiQuery';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { useToast } from '../../hooks/useToast';
import { palette } from '../../theme/palette';
import type { PricingPlan } from '../../types/billing';
import { formatCurrency } from '../../utils/formatters';

function BillingDetail({ label, value, tone }: { label: string; value: string; tone?: 'success' | 'warning' }) {
  const valueColor = tone === 'success' ? palette.success : tone === 'warning' ? palette.warning : palette.text;

  return (
    <Stack direction="row" justifyContent="space-between" spacing={2} sx={{ py: 1, borderBottom: `1px solid ${palette.border}`, '&:last-child': { borderBottom: 0, pb: 0 } }}>
      <Typography color="text.secondary">{label}</Typography>
      <Typography fontWeight={900} textAlign="right" sx={{ color: valueColor }}>
        {value}
      </Typography>
    </Stack>
  );
}

function formatBillingStatus(status?: string | null) {
  if (!status) return 'None';

  const knownStatuses: Record<string, string> = {
    Active: 'Active',
    PendingCheckout: 'Pending checkout',
    RequiresExternalProcessor: 'Requires external processor',
    Completed: 'Completed',
    NotConnected: 'Not connected'
  };

  return knownStatuses[status] ?? status.replace(/([a-z])([A-Z])/g, '$1 $2').trim();
}

function BillingPlanOption({
  plan,
  isCurrent,
  isPending,
  onSelect,
  busy
}: {
  plan: PricingPlan;
  isCurrent: boolean;
  isPending: boolean;
  onSelect: (plan: PricingPlan) => void;
  busy: boolean;
}) {
  const actionLabel = isCurrent ? 'Current plan' : isPending ? 'Pending checkout' : 'Select plan';
  const featureSummary = plan.features.length === 1 ? '1 feature included' : `${plan.features.length} features included`;

  return (
    <AppCard
      sx={{
        height: '100%',
        borderColor: isCurrent ? palette.borderStrong : palette.border,
        backgroundColor: palette.cardSurface
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
          {featureSummary}
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
        disabled={isCurrent || isPending || busy}
        startIcon={<SwapHorizOutlinedIcon />}
        onClick={() => onSelect(plan)}
        sx={{ mt: 'auto' }}
      >
        {busy ? 'Creating intent...' : actionLabel}
      </Button>
    </AppCard>
  );
}

export function BillingPage() {
  const { showToast } = useToast();
  const [searchParams] = useSearchParams();
  const [busyPlan, setBusyPlan] = useState<string | null>(null);
  const billingQuery = useApiQuery(getBilling, [busyPlan]);
  const billing = billingQuery.data;
  const plans = useMemo(() => billing?.availablePlans.map(mapPlanDtoToPricingPlan) ?? [], [billing?.availablePlans]);
  const currentPlanSlug = billing?.currentPlanState?.plan?.slug;
  const currentPlanStatus = billing?.currentPlanState?.status;
  const pendingPlanSlug = currentPlanStatus === 'PendingCheckout' ? billing?.currentPlanState?.plan?.slug : undefined;
  const selectedPlan = searchParams.get('plan');

  const handleSelectPlan = async (plan: PricingPlan) => {
    if (plan.checkoutPlanCode === pendingPlanSlug || (plan.checkoutPlanCode === currentPlanSlug && currentPlanStatus === 'Active')) {
      return;
    }

    setBusyPlan(plan.code);
    try {
      const result = await createCheckoutIntent(plan.checkoutPlanCode, 'Monthly', `${window.location.origin}/app/billing`);
      showToast(result.message, result.paymentProcessorConnected ? 'success' : 'info');
    } catch (error) {
      showToast(error instanceof Error ? error.message : 'Could not create checkout intent.', 'error');
    } finally {
      setBusyPlan(null);
    }
  };

  return (
    <Stack spacing={3} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader title="Billing" description="Review plan access and billing readiness." />

      {billingQuery.loading ? (
        <Stack spacing={2}>
          <Skeleton variant="rounded" height={170} />
          <Skeleton variant="rounded" height={320} />
        </Stack>
      ) : (
        <>
          {selectedPlan ? (
            <AppCard sx={{ borderColor: palette.borderStrong, backgroundColor: palette.cardSurface }}>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} alignItems={{ sm: 'center' }} justifyContent="space-between">
                <Stack spacing={0.35}>
                  <Typography variant="h5" fontWeight={950}>
                    Plan setup
                  </Typography>
                  <Typography color="text.secondary">
                    Selected plan: {selectedPlan}. Create a checkout intent below when ready.
                  </Typography>
                </Stack>
                <CreditCardOutlinedIcon color="primary" />
              </Stack>
            </AppCard>
          ) : null}

          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1fr) minmax(320px, .85fr)' },
              gap: 2
            }}
          >
            <AppCard sx={{ height: '100%' }}>
              <Stack spacing={1.6}>
                <Stack spacing={0.35}>
                  <Typography variant="h5" fontWeight={950}>
                    Plan access
                  </Typography>
                  <Typography color="text.secondary">Current workspace access and billing readiness.</Typography>
                </Stack>
                <Divider />
                <Stack spacing={0.4}>
                  <BillingDetail label="Current plan" value={billing?.currentPlanState?.plan?.name ?? 'None'} />
                  <BillingDetail label="Workspace access" value={formatBillingStatus(currentPlanStatus)} tone={currentPlanStatus === 'Active' ? 'success' : 'warning'} />
                  <BillingDetail label="Billing" value={billing?.paymentProcessorConnected ? 'Connected' : 'Not connected'} tone={billing?.paymentProcessorConnected ? 'success' : 'warning'} />
                  <BillingDetail label="Payment method" value="Not connected" tone="warning" />
                </Stack>
              </Stack>
            </AppCard>

            <AppCard sx={{ height: '100%' }}>
              <Stack spacing={1.8} sx={{ height: '100%' }}>
                <Stack spacing={0.35}>
                  <Typography variant="h5" fontWeight={950}>
                    Payment setup
                  </Typography>
                  <Typography color="text.secondary">
                    Checkout is pending while Stripe is not connected. No payment has been completed.
                  </Typography>
                </Stack>
                <Divider />
                <Stack spacing={0.5}>
                  {billing?.checkoutIntents.slice(0, 3).map((intent) => (
                    <BillingDetail key={intent.id} label={intent.planName ?? intent.planSlug ?? 'Plan'} value={formatBillingStatus(intent.status)} tone={intent.status === 'Completed' ? 'success' : 'warning'} />
                  ))}
                  {billing?.checkoutIntents.length === 0 ? (
                    <Typography color="text.secondary">No checkout intents yet.</Typography>
                  ) : null}
                </Stack>
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
                gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' },
                gap: 2,
                alignItems: 'stretch'
              }}
            >
              {plans.map((plan) => (
                <BillingPlanOption
                  key={plan.code}
                  plan={plan}
                  isCurrent={plan.checkoutPlanCode === currentPlanSlug && currentPlanStatus === 'Active'}
                  isPending={plan.checkoutPlanCode === pendingPlanSlug}
                  busy={busyPlan === plan.code}
                  onSelect={handleSelectPlan}
                />
              ))}
            </Box>
          </Stack>
        </>
      )}
    </Stack>
  );
}
