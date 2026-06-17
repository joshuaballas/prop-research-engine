import type { PricingPlan } from '../types/billing';

export const mockPricingPlans: PricingPlan[] = [
  {
    code: 'starter',
    name: 'Starter',
    description: 'Track top DFS opportunities with basic market context.',
    priceCents: 1900,
    currency: 'USD',
    billingInterval: 'month',
    features: ['Top picks board', 'DFS provider filters', 'Basic performance summaries', 'Freshness status'],
    entitlements: ['1 seat', '10 saved entries', 'Daily snapshot history'],
    checkoutPlanCode: 'starter_monthly'
  },
  {
    code: 'research-pro',
    name: 'Research Pro',
    description: 'Best for individual DFS researchers who want full line shopping, candidate review, and provider-scoped entry building.',
    priceCents: 4900,
    currency: 'USD',
    billingInterval: 'month',
    badgeText: 'Most popular',
    isFeatured: true,
    features: [
      'Multi-sport market scanner',
      'Provider-compatible entry builder',
      'Projection modifier visibility',
      'Line-shopping comparison views',
      'Strategy performance breakdowns'
    ],
    entitlements: ['3 seats', 'Unlimited saved entries', 'Advanced filters', 'Priority freshness alerts'],
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
    entitlements: ['10 seats', 'Shared watchlists', 'Custom provider presets'],
    checkoutPlanCode: 'studio_monthly'
  }
];
