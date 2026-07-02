import { SignIn } from '@clerk/clerk-react';
import { Box, Stack, Typography } from '@mui/material';
import { Navigate, useSearchParams } from 'react-router-dom';
import { AuthSetupState } from '../../auth/AuthSetupState';
import { AuthFormSkeleton, authFormSlotMinHeight } from '../../auth/AuthFormSkeleton';
import { clerkSignInFallbackRedirectUrl, clerkSignUpUrl, useAppAuth } from '../../auth/AppAuthProvider';
import { clerkAppearance } from '../../auth/clerkAppearance';
import { AppCard } from '../../components/ui/AppCard';
import { palette } from '../../theme/palette';

export function SignInPage() {
  const auth = useAppAuth();
  const [searchParams] = useSearchParams();
  const returnTo = readSafeAppRedirect(searchParams) ?? clerkSignInFallbackRedirectUrl;

  if (!auth.authConfigured) {
    return <AuthSetupState title="Sign-in setup needed" />;
  }

  if (auth.isLoaded && auth.isSignedIn) {
    return <Navigate to={returnTo} replace />;
  }

  return (
    <AppCard
      sx={{
        maxWidth: 520,
        ml: { md: 'auto' },
        backgroundColor: palette.cardSurface,
        borderColor: 'rgba(125,211,252,.22)',
        boxShadow: '0 24px 80px rgba(0,0,0,.35)'
      }}
    >
      <Stack spacing={2.4}>
        <Stack spacing={0.8}>
          <Typography variant="h4">Welcome back</Typography>
          <Typography color="text.secondary">Sign in to continue your prop research workflow.</Typography>
        </Stack>
        <Box sx={{ minHeight: authFormSlotMinHeight.signIn, width: '100%' }}>
          {auth.isLoaded ? (
            <SignIn
              routing="path"
              path="/sign-in"
              signUpUrl={clerkSignUpUrl}
              fallbackRedirectUrl={returnTo}
              appearance={clerkAppearance}
            />
          ) : (
            <AuthFormSkeleton variant="signIn" />
          )}
        </Box>
      </Stack>
    </AppCard>
  );
}

function readSafeAppRedirect(searchParams: URLSearchParams) {
  const candidate =
    searchParams.get('redirect_url') ||
    searchParams.get('redirectUrl') ||
    searchParams.get('returnUrl') ||
    searchParams.get('returnTo');

  return candidate?.startsWith('/app/') ? candidate : null;
}
