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
    description: getPlanDisplayDescription(plan),
    priceCents: plan.monthlyPriceCents ?? 0,
    currency: plan.currency,
    billingInterval: 'month',
    isFeatured: plan.slug === 'pro',
    features: includedFeatures.map((feature) => feature.label),
    checkoutPlanCode: plan.slug
  };
}

function getPlanDisplayDescription(plan: PlanDto) {
  const normalized = plan.slug.trim().toLowerCase();

  if (normalized === 'free') {
    return 'Basic access for trying the prop research workflow.';
  }

  if (normalized === 'pro') {
    return 'Top Picks, line shopping, model performance, and entry review workflows.';
  }

  return plan.description || plan.summary;
}
