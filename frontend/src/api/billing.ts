import { apiGet, apiPost } from './client';

export interface PlanFeatureDto {
  id: string;
  planId: string;
  featureKey: string;
  label: string;
  description?: string | null;
  value?: string | null;
  included: boolean;
  sortOrder: number;
}

export interface PlanDto {
  id: string;
  slug: string;
  name: string;
  summary: string;
  description?: string | null;
  status: string;
  isPublic: boolean;
  sortOrder: number;
  monthlyPriceCents?: number | null;
  annualPriceCents?: number | null;
  currency: 'USD';
  trialDays?: number | null;
  features: PlanFeatureDto[];
}

export interface UserPlanStateDto {
  id: string;
  appUserId: string;
  planId?: string | null;
  plan?: PlanDto | null;
  status: string;
  billingInterval: string;
  currentPeriodStartUtc?: string | null;
  currentPeriodEndUtc?: string | null;
  externalBillingProvider?: string | null;
  externalCustomerId?: string | null;
  externalSubscriptionId?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CheckoutIntentDto {
  id: string;
  appUserId: string;
  planId: string;
  planSlug?: string | null;
  planName?: string | null;
  billingInterval: string;
  amountCents: number;
  currency: 'USD';
  status: string;
  externalBillingProvider?: string | null;
  externalCheckoutSessionId?: string | null;
  returnUrl?: string | null;
  createdAtUtc: string;
  expiresAtUtc: string;
  completedAtUtc?: string | null;
}

export interface PublicPlansResponseDto {
  generatedAtUtc: string;
  readOnly: boolean;
  callsExternalApis: boolean;
  paymentProcessorConnected: boolean;
  plans: PlanDto[];
}

export interface BillingResponseDto {
  generatedAtUtc: string;
  readOnly: boolean;
  callsExternalApis: boolean;
  paymentProcessorConnected: boolean;
  currentPlanState?: UserPlanStateDto | null;
  availablePlans: PlanDto[];
  checkoutIntents: CheckoutIntentDto[];
}

export interface AccountResponseDto {
  generatedAtUtc: string;
  readOnly: boolean;
  callsExternalApis: boolean;
  user: AppUserDto;
}

export interface MeResponseDto {
  generatedAtUtc: string;
  readOnly: boolean;
  callsExternalApis: boolean;
  user: AppUserDto;
  billing: BillingResponseDto;
}

export interface AppUserDto {
  id: string;
  externalAuthProvider: string;
  externalSubjectId: string;
  email?: string | null;
  displayName?: string | null;
  role: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastSeenAtUtc?: string | null;
  lastSessionId?: string | null;
}

export interface CheckoutIntentResponseDto {
  generatedAtUtc: string;
  callsExternalApis: boolean;
  writesRows: boolean;
  checkoutIntentId: string;
  status: string;
  paymentProcessorConnected: boolean;
  externalProviderRequired?: string | null;
  checkoutUrl?: string | null;
  message: string;
  checkoutIntent: CheckoutIntentDto;
}

export function getPublicPlans(signal?: AbortSignal) {
  return apiGet<PublicPlansResponseDto>('/api/plans', signal);
}

export function getAccount(signal?: AbortSignal) {
  return apiGet<AccountResponseDto>('/api/account', signal);
}

export function getMe(signal?: AbortSignal) {
  return apiGet<MeResponseDto>('/api/me', signal);
}

export function getBilling(signal?: AbortSignal) {
  return apiGet<BillingResponseDto>('/api/billing', signal);
}

export function createCheckoutIntent(planSlug: string, billingInterval: string, returnUrl: string) {
  return apiPost<CheckoutIntentResponseDto>('/api/checkout/intents', {
    planSlug,
    billingInterval,
    returnUrl
  });
}
