import { ClerkProvider, useAuth as useClerkAuth, useUser } from '@clerk/clerk-react';
import { createContext, useContext, useEffect, useMemo } from 'react';
import type { PropsWithChildren } from 'react';
import { useNavigate } from 'react-router-dom';
import { setAccessTokenProvider } from '../api/authToken';
import { clerkBaseAppearance } from './clerkAppearance';
import { clerkLocalization } from './clerkLocalization';

export interface AppAuthUser {
  displayName?: string;
  email?: string;
  imageUrl?: string;
}

export interface AppAuthState {
  authConfigured: boolean;
  isLoaded: boolean;
  isSignedIn: boolean;
  user?: AppAuthUser;
  getToken: () => Promise<string | null>;
  signOut: () => Promise<void>;
}

const missingAuthState: AppAuthState = {
  authConfigured: false,
  isLoaded: true,
  isSignedIn: false,
  getToken: async () => null,
  signOut: async () => {}
};

const AppAuthContext = createContext<AppAuthState>(missingAuthState);

export const clerkPublishableKey = (import.meta.env.VITE_CLERK_PUBLISHABLE_KEY ?? '').trim();
export const clerkSignInUrl = (import.meta.env.VITE_CLERK_SIGN_IN_URL ?? '/sign-in').trim() || '/sign-in';
export const clerkSignUpUrl = (import.meta.env.VITE_CLERK_SIGN_UP_URL ?? '/create-account').trim() || '/create-account';
export const clerkSignInFallbackRedirectUrl = (import.meta.env.VITE_CLERK_SIGN_IN_FALLBACK_REDIRECT_URL ?? '/app/dashboard').trim() || '/app/dashboard';
export const clerkSignUpFallbackRedirectUrl = (import.meta.env.VITE_CLERK_SIGN_UP_FALLBACK_REDIRECT_URL ?? '/app/billing').trim() || '/app/billing';

export function AppAuthProvider({ children }: PropsWithChildren) {
  if (!clerkPublishableKey) {
    return <AppAuthContext.Provider value={missingAuthState}>{children}</AppAuthContext.Provider>;
  }

  return (
    <ClerkProvider
      publishableKey={clerkPublishableKey}
      signInUrl={clerkSignInUrl}
      signUpUrl={clerkSignUpUrl}
      signInFallbackRedirectUrl={clerkSignInFallbackRedirectUrl}
      signUpFallbackRedirectUrl={clerkSignUpFallbackRedirectUrl}
      appearance={clerkBaseAppearance}
      localization={clerkLocalization}
    >
      <ClerkAuthBridge>{children}</ClerkAuthBridge>
    </ClerkProvider>
  );
}

function ClerkAuthBridge({ children }: PropsWithChildren) {
  const navigate = useNavigate();
  const { getToken, isLoaded, isSignedIn, signOut } = useClerkAuth();
  const { user } = useUser();

  useEffect(() => {
    setAccessTokenProvider(async () => {
      if (!isLoaded || !isSignedIn) {
        return null;
      }

      return getToken();
    });

    return () => setAccessTokenProvider(null);
  }, [getToken, isLoaded, isSignedIn]);

  const value = useMemo<AppAuthState>(
    () => ({
      authConfigured: true,
      isLoaded,
      isSignedIn: Boolean(isSignedIn),
      user: {
        displayName: user?.fullName ?? user?.username ?? undefined,
        email: user?.primaryEmailAddress?.emailAddress,
        imageUrl: user?.imageUrl
      },
      getToken: async () => {
        if (!isLoaded || !isSignedIn) {
          return null;
        }

        return getToken();
      },
      signOut: async () => {
        await signOut();
        navigate('/', { replace: true });
      }
    }),
    [getToken, isLoaded, isSignedIn, navigate, signOut, user?.fullName, user?.imageUrl, user?.primaryEmailAddress?.emailAddress, user?.username]
  );

  return <AppAuthContext.Provider value={value}>{children}</AppAuthContext.Provider>;
}

export function useAppAuth() {
  return useContext(AppAuthContext);
}
