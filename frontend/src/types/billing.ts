export interface PricingPlan {
  code: string;
  name: string;
  description: string;
  priceCents: number;
  currency: 'USD';
  billingInterval: 'month' | 'year';
  badgeText?: string;
  isFeatured?: boolean;
  features: string[];
  entitlements: string[];
  checkoutPlanCode: string;
}
