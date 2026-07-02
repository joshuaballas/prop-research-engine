import WorkspacePremiumOutlinedIcon from '@mui/icons-material/WorkspacePremiumOutlined';
import { SignUp } from '@clerk/clerk-react';
import { Box, Button, Stack, Typography } from '@mui/material';
import { Link as RouterLink, Navigate, useSearchParams } from 'react-router-dom';
import { AuthSetupState } from '../../auth/AuthSetupState';
import { clerkSignInUrl, useAppAuth } from '../../auth/AppAuthProvider';
import { AppCard } from '../../components/ui/AppCard';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

export function CreateAccountPage() {
  const auth = useAppAuth();
  const [searchParams] = useSearchParams();
  const selectedPlan = searchParams.get('plan') ?? '';
  const selectedInterval = searchParams.get('interval') ?? 'Monthly';
  const billingReturnPath = buildBillingReturnPath(selectedPlan, selectedInterval);
  const returnTo = readSafeAppRedirect(searchParams) ?? billingReturnPath;

  if (!auth.authConfigured) {
    return <AuthSetupState title="Account setup needed" />;
  }

  if (auth.isLoaded && auth.isSignedIn) {
    return <Navigate to={returnTo} replace />;
  }

  return (
    <AppCard
      sx={{
        maxWidth: 540,
        ml: { md: 'auto' },
        backgroundColor: palette.cardSurface,
        borderColor: 'rgba(125,211,252,.22)',
        boxShadow: '0 24px 80px rgba(0,0,0,.35)'
      }}
    >
      <Stack spacing={2.2}>
        <Stack spacing={0.8}>
          <Typography variant="h4">Create your account</Typography>
          <Typography color="text.secondary">Start with prop research, provider line context, and entry review.</Typography>
        </Stack>

        {selectedPlan ? (
          <Box
            sx={{
              p: 1.5,
              borderRadius: radius.featuredCard,
              border: '1px solid rgba(34,211,238,.2)',
              bgcolor: 'rgba(34,211,238,.06)'
            }}
          >
            <Stack direction="row" spacing={1.3} alignItems="flex-start">
              <WorkspacePremiumOutlinedIcon color="primary" />
              <Box>
                <Typography fontWeight={950}>Selected plan: {selectedPlan}</Typography>
                <Typography variant="body2" color="text.secondary">
                  You can review plan setup after sign-up.
                </Typography>
              </Box>
            </Stack>
          </Box>
        ) : (
          <Button component={RouterLink} to="/pricing" variant="outlined">
            View plans
          </Button>
        )}

        <SignUp
          routing="path"
          path="/create-account"
          signInUrl={clerkSignInUrl}
          fallbackRedirectUrl={billingReturnPath}
        />
      </Stack>
    </AppCard>
  );
}

function buildBillingReturnPath(plan: string, interval: string) {
  if (!plan) {
    return '/app/billing';
  }

  const query = new URLSearchParams({ plan, interval });
  return `/app/billing?${query.toString()}`;
}

function readSafeAppRedirect(searchParams: URLSearchParams) {
  const candidate =
    searchParams.get('redirect_url') ||
    searchParams.get('redirectUrl') ||
    searchParams.get('returnUrl') ||
    searchParams.get('returnTo');

  return candidate?.startsWith('/app/') ? candidate : null;
}
