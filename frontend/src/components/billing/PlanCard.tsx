import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import { Button, Divider, Stack, Typography } from '@mui/material';
import type { PricingPlan } from '../../types/billing';
import { formatCurrency } from '../../utils/formatters';
import { AppCard } from '../ui/AppCard';
import { StatusPill } from '../ui/StatusPill';

export function PlanCard({ plan }: { plan: PricingPlan }) {
  return (
    <AppCard sx={{ height: '100%', borderColor: plan.isFeatured ? 'rgba(34,211,238,.5)' : undefined }}>
      <Stack spacing={1.5} height="100%">
        <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
          <Typography variant="h5">{plan.name}</Typography>
          {plan.badgeText ? <StatusPill label={plan.badgeText} /> : null}
        </Stack>
        <Typography color="text.secondary" minHeight={48}>
          {plan.description}
        </Typography>
        <Typography variant="h3">
          {formatCurrency(plan.priceCents)}
          <Typography component="span" color="text.secondary" variant="body2">
            /{plan.billingInterval}
          </Typography>
        </Typography>
        <Divider />
        <Stack spacing={1} flex={1}>
          {plan.features.map((feature) => (
            <Stack key={feature} direction="row" spacing={1} alignItems="flex-start">
              <CheckCircleRoundedIcon color="primary" sx={{ fontSize: 18, mt: 0.2 }} />
              <Typography variant="body2">{feature}</Typography>
            </Stack>
          ))}
        </Stack>
        <Divider />
        <Stack spacing={0.6}>
          {plan.entitlements.map((entitlement) => (
            <Typography key={entitlement} variant="caption" color="text.secondary">
              {entitlement}
            </Typography>
          ))}
        </Stack>
        <Button variant={plan.isFeatured ? 'contained' : 'outlined'} size="large">
          Select {plan.name}
        </Button>
      </Stack>
    </AppCard>
  );
}
