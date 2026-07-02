import { Box, Skeleton, Stack } from '@mui/material';
import type { PropsWithChildren } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { AuthSetupState } from './AuthSetupState';
import { useAppAuth } from './AppAuthProvider';

export function ProtectedAppRoute({ children }: PropsWithChildren) {
  const auth = useAppAuth();
  const location = useLocation();
  const returnTo = `${location.pathname}${location.search}`;

  if (!auth.authConfigured) {
    return (
      <Box sx={{ p: { xs: 2, md: 4 } }}>
        <AuthSetupState />
      </Box>
    );
  }

  if (!auth.isLoaded) {
    return (
      <Stack spacing={2.4} sx={{ p: { xs: 2, md: 4 } }}>
        <Skeleton variant="rounded" height={44} />
        <Skeleton variant="rounded" height={180} />
        <Skeleton variant="rounded" height={260} />
      </Stack>
    );
  }

  if (!auth.isSignedIn) {
    return <Navigate to={`/sign-in?returnUrl=${encodeURIComponent(returnTo)}`} replace />;
  }

  return <>{children}</>;
}
