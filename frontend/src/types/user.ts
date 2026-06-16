export interface MockUser {
  name: string;
  email: string;
  planName: string;
  subscriptionStatus: 'Active' | 'Trialing' | 'Past due';
  renewalDate: string;
  mfaEnabled: boolean;
  themePreference: 'Dark' | 'System' | 'Light';
}
