import { SignIn } from '@clerk/clerk-react';
import { Box } from '@mui/material';
import { Navigate, useSearchParams } from 'react-router-dom';
import { AuthSetupState } from '../../auth/AuthSetupState';
import { AuthFormSkeleton, authFormSlotMinHeight } from '../../auth/AuthFormSkeleton';
import { clerkSignInFallbackRedirectUrl, clerkSignUpUrl, useAppAuth } from '../../auth/AppAuthProvider';
import { clerkAuthAppearance } from '../../auth/clerkAppearance';
import { AccountFunnelCard } from '../../components/auth/AccountFunnelCard';

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
    <AccountFunnelCard maxWidth={460}>
      <Box sx={{ minHeight: authFormSlotMinHeight.signIn, width: '100%' }}>
        {auth.isLoaded ? (
          <SignIn
            routing="path"
            path="/sign-in"
            signUpUrl={clerkSignUpUrl}
            fallbackRedirectUrl={returnTo}
            appearance={clerkAuthAppearance}
          />
        ) : (
          <AuthFormSkeleton variant="signIn" />
        )}
      </Box>
    </AccountFunnelCard>
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
