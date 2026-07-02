import { SignUp } from '@clerk/clerk-react';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useMemo } from 'react';
import { Link as RouterLink, Navigate, useSearchParams } from 'react-router-dom';
import { getPublicPlans } from '../../api/billing';
import { mapPlanDtoToPricingPlan } from '../../api/planMappers';
import { useApiQuery } from '../../api/useApiQuery';
import { AuthSetupState } from '../../auth/AuthSetupState';
import { AuthFormSkeleton, authFormSlotMinHeight } from '../../auth/AuthFormSkeleton';
import { clerkSignInUrl, useAppAuth } from '../../auth/AppAuthProvider';
import { clerkAuthAppearance } from '../../auth/clerkAppearance';
import { planSelectionCode } from '../../components/billing/PlanCard';
import { AccountFunnelCard } from '../../components/auth/AccountFunnelCard';
import { formatCurrency } from '../../utils/formatters';

const accountFormRailMaxWidth = 440;
const signUpRailAppearance = {
  ...clerkAuthAppearance,
  elements: {
    ...clerkAuthAppearance.elements,
    rootBox: {
      ...clerkAuthAppearance.elements.rootBox,
      width: '100%',
      maxWidth: '100%',
      marginInline: 'auto'
    },
    cardBox: {
      ...clerkAuthAppearance.elements.cardBox,
      width: '100%',
      maxWidth: '100%'
    },
    card: {
      ...clerkAuthAppearance.elements.card,
      width: '100%',
      maxWidth: '100%',
      padding: '0'
    },
    main: {
      ...clerkAuthAppearance.elements.main,
      width: '100%',
      maxWidth: '100%',
      padding: '0'
    },
    form: {
      ...clerkAuthAppearance.elements.form,
      width: '100%',
      maxWidth: '100%'
    },
    formField: {
      ...clerkAuthAppearance.elements.formField,
      width: '100%',
      maxWidth: '100%'
    },
    formFieldRow: {
      ...clerkAuthAppearance.elements.formFieldRow,
      width: '100%',
      maxWidth: '100%'
    },
    formButtonPrimary: {
      ...clerkAuthAppearance.elements.formButtonPrimary,
      width: '100%',
      maxWidth: '100%'
    },
    footer: {
      ...clerkAuthAppearance.elements.footer,
      width: '100%',
      maxWidth: '100%',
      paddingInline: '0'
    }
  }
} as const;

export function CreateAccountPage() {
  const auth = useAppAuth();
  const [searchParams] = useSearchParams();
  const selectedPlan = searchParams.get('plan') ?? '';
  const selectedInterval = searchParams.get('interval') ?? 'Monthly';
  const selectedPlanLabel = formatSelectedPlanLabel(selectedPlan);
  const billingReturnPath = buildBillingReturnPath(selectedPlan, selectedInterval);
  const returnTo = readSafeAppRedirect(searchParams) ?? billingReturnPath;
  const plansQuery = useApiQuery(getPublicPlans);
  const plans = useMemo(() => plansQuery.data?.plans.map(mapPlanDtoToPricingPlan) ?? [], [plansQuery.data?.plans]);
  const selectedPlanDetails = useMemo(() => {
    const normalized = selectedPlan.trim().toLowerCase();
    return plans.find((plan) => [plan.code, plan.slug, plan.checkoutPlanCode, planSelectionCode(plan)]
      .filter(Boolean)
      .some((value) => value?.toLowerCase() === normalized));
  }, [plans, selectedPlan]);
  const selectedPlanPrice = selectedPlanDetails
    ? selectedPlanDetails.priceCents > 0
      ? `${formatCurrency(selectedPlanDetails.priceCents)}/${formatBillingIntervalForPrice(selectedPlanDetails.billingInterval)}`
      : '$0/month'
    : selectedPlanLabel.toLowerCase() === 'free'
      ? '$0/month'
      : selectedPlanLabel.toLowerCase() === 'pro'
        ? '$29/month'
        : null;
  if (!auth.authConfigured) {
    return <AuthSetupState title="Account setup needed" />;
  }

  if (auth.isLoaded && auth.isSignedIn) {
    return <Navigate to={returnTo} replace />;
  }

  return (
    <AccountFunnelCard maxWidth={560} contentMaxWidth={accountFormRailMaxWidth}>
      <Stack spacing={2.1} sx={{ width: '100%' }}>
        {selectedPlan ? (
          <SelectedPlanSummary
            planName={selectedPlanDetails?.name ?? selectedPlanLabel}
            price={selectedPlanPrice}
          />
        ) : (
          <Button component={RouterLink} to="/pricing" variant="outlined" fullWidth>
            View plans
          </Button>
        )}

        <Box sx={{ minHeight: authFormSlotMinHeight.signUp, width: '100%' }}>
          {auth.isLoaded ? (
            <SignUp
              routing="path"
              path="/create-account"
              signInUrl={clerkSignInUrl}
              fallbackRedirectUrl={billingReturnPath}
              appearance={signUpRailAppearance}
            />
          ) : (
            <AuthFormSkeleton variant="signUp" />
          )}
        </Box>
      </Stack>
    </AccountFunnelCard>
  );
}

function SelectedPlanSummary({ planName, price }: { planName: string; price: string | null }) {
  return (
    <Box
      sx={{
        width: '100%',
        py: 1.25,
        borderTop: '1px solid rgba(148,163,184,.18)',
        borderBottom: '1px solid rgba(148,163,184,.18)'
      }}
    >
      <Stack spacing={0.65}>
        <Stack direction="row" justifyContent="space-between" gap={1.5} alignItems="flex-start">
          <Box>
            <Typography variant="caption" color="text.secondary" fontWeight={900}>
              SELECTED PLAN
            </Typography>
            <Typography fontWeight={950}>{planName}</Typography>
          </Box>
          <Box sx={{ textAlign: 'right', flexShrink: 0 }}>
            <Typography fontWeight={950}>{price ?? 'Selected'}</Typography>
          </Box>
        </Stack>
        <Typography
          component={RouterLink}
          to="/pricing"
          variant="body2"
          sx={{
            color: 'primary.main',
            fontWeight: 850,
            textDecoration: 'none',
            width: 'fit-content',
            '&:hover': { textDecoration: 'underline' }
          }}
        >
          Change plan
        </Typography>
      </Stack>
    </Box>
  );
}

function formatSelectedPlanLabel(plan: string) {
  const knownPlans: Record<string, string> = {
    free: 'Free',
    pro: 'Pro'
  };
  const normalized = plan.trim().toLowerCase();

  if (knownPlans[normalized]) {
    return knownPlans[normalized];
  }

  return plan
    .trim()
    .replace(/[-_]+/g, ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function buildBillingReturnPath(plan: string, interval: string) {
  if (!plan) {
    return '/app/billing';
  }

  const query = new URLSearchParams({ plan, interval });
  return `/app/billing?${query.toString()}`;
}

function formatBillingIntervalForPrice(interval: string) {
  const normalized = interval.trim().toLowerCase();

  if (normalized === 'monthly') {
    return 'month';
  }

  if (normalized === 'yearly' || normalized === 'annual' || normalized === 'annually') {
    return 'year';
  }

  return normalized || 'month';
}

function readSafeAppRedirect(searchParams: URLSearchParams) {
  const candidate =
    searchParams.get('redirect_url') ||
    searchParams.get('redirectUrl') ||
    searchParams.get('returnUrl') ||
    searchParams.get('returnTo');

  return candidate?.startsWith('/app/') ? candidate : null;
}
