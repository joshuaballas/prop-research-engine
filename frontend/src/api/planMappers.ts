import type { PlanDto } from './billing';
import type { PricingPlan } from '../types/billing';

export function mapPlanDtoToPricingPlan(plan: PlanDto): PricingPlan {
  const includedFeatures = plan.features
    .filter((feature) => feature.included)
    .sort((left, right) => left.sortOrder - right.sortOrder);

  return {
    code: plan.slug,
    slug: plan.slug,
    name: plan.name,
    description: plan.description || plan.summary,
    priceCents: plan.monthlyPriceCents ?? 0,
    currency: plan.currency,
    billingInterval: 'month',
    badgeText: plan.slug === 'pro' ? 'Most useful' : undefined,
    isFeatured: plan.slug === 'pro',
    features: includedFeatures.map((feature) => feature.label),
    entitlements: includedFeatures.slice(0, 3).map((feature) => feature.value || feature.label),
    checkoutPlanCode: plan.slug
  };
}
