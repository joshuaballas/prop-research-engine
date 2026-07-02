import type { PricingPlan } from '../types/billing';

export const mockPricingPlans: PricingPlan[] = [
  {
    code: 'starter',
    name: 'Starter',
    description: 'Track top prop opportunities with basic market context.',
    priceCents: 1900,
    currency: 'USD',
    billingInterval: 'month',
    features: ['Top picks board', 'Provider filters', 'Basic performance summaries', 'Freshness status'],
    checkoutPlanCode: 'starter_monthly'
  },
  {
    code: 'research-pro',
    name: 'Research Pro',
    description: 'Best for individual prop researchers who want full line shopping, candidate review, and provider-scoped entry building.',
    priceCents: 4900,
    currency: 'USD',
    billingInterval: 'month',
    isFeatured: true,
    features: [
      'Multi-sport market scanner',
      'Provider-compatible entry builder',
      'Projection modifier visibility',
      'Line-shopping comparison views',
      'Performance breakdowns'
    ],
    checkoutPlanCode: 'research_pro_monthly'
  },
  {
    code: 'studio',
    name: 'Team',
    description: 'For serious research groups that want shared workflows.',
    priceCents: 9900,
    currency: 'USD',
    billingInterval: 'month',
    features: ['Team workspaces', 'Exportable performance views', 'Provider coverage reports', 'Advanced support'],
    checkoutPlanCode: 'studio_monthly'
  }
];
