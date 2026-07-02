export interface PricingPlan {
  code: string;
  slug?: string;
  name: string;
  description: string;
  priceCents: number;
  currency: 'USD';
  billingInterval: 'month' | 'year';
  isFeatured?: boolean;
  features: string[];
  checkoutPlanCode: string;
}
