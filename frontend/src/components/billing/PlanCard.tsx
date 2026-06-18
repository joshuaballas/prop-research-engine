import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import { Button, Card, CardContent, Chip, Divider, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import type { PricingPlan } from '../../types/billing';
import { palette } from '../../theme/palette';
import { formatCurrency } from '../../utils/formatters';
import { StatusPill } from '../ui/StatusPill';

export function planSelectionCode(plan: PricingPlan) {
  return plan.name.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
}

export function PlanCard({ plan }: { plan: PricingPlan }) {
  const isFeatured = Boolean(plan.isFeatured);

  return (
    <Card
      elevation={0}
      sx={{
        height: '100%',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        position: 'relative',
        overflow: 'hidden',
        borderRadius: 4,
        border: `1px solid ${isFeatured ? 'rgba(34,211,238,.62)' : 'rgba(148,163,184,.18)'}`,
        backgroundColor: palette.cardSurface,
        backgroundImage: 'none',
        boxShadow: 'none'
      }}
    >
      <CardContent
        sx={{
          position: 'relative',
          zIndex: 1,
          p: 2.25,
          pb: '150px !important',
          display: 'flex',
          flex: 1,
          flexDirection: 'column',
          width: '100%'
        }}
      >
        <Stack spacing={2} sx={{ flex: 1, minHeight: 0 }}>
          <Stack spacing={1.2}>
            <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
              <Typography variant="h5">{plan.name}</Typography>
              {plan.badgeText ? <StatusPill label={plan.badgeText} /> : null}
            </Stack>
            <Typography color="text.secondary" minHeight={48}>
              {plan.description}
            </Typography>
          </Stack>

          <Stack spacing={0.5}>
            <Typography variant="h3" lineHeight={1}>
              {formatCurrency(plan.priceCents)}
              <Typography component="span" color="text.secondary" variant="body2" fontWeight={800}>
                /{plan.billingInterval}
              </Typography>
            </Typography>
            <Typography variant="caption" color="text.secondary">
              Cancel anytime. Billing will be handled securely at checkout.
            </Typography>
          </Stack>

          <Divider />
          <Stack spacing={1.05}>
            {plan.features.map((feature) => (
              <Stack key={feature} direction="row" spacing={1.1} alignItems="flex-start">
                <CheckCircleRoundedIcon color="primary" sx={{ fontSize: 17, mt: 0.18, flexShrink: 0 }} />
                <Typography variant="body2" fontWeight={760} lineHeight={1.45}>
                  {feature}
                </Typography>
              </Stack>
            ))}
          </Stack>

        </Stack>
      </CardContent>

      <Stack
        spacing={1.5}
        sx={{
          position: 'absolute',
          left: 18,
          right: 18,
          bottom: 18,
          zIndex: 2
        }}
      >
        <Divider />
        <Stack direction="row" flexWrap="wrap" gap={0.8} useFlexGap>
          {plan.entitlements.map((entitlement) => (
            <Chip
              key={entitlement}
              label={entitlement}
              size="small"
              sx={{
                height: 26,
                borderRadius: 1.5,
                color: 'text.secondary',
                bgcolor: 'rgba(8,13,26,.46)',
                border: '1px solid rgba(148,163,184,.14)',
                '& .MuiChip-label': { px: 1, fontWeight: 800 }
              }}
            />
          ))}
        </Stack>

        <Button
          component={RouterLink}
          to={`/create-account?plan=${encodeURIComponent(planSelectionCode(plan))}`}
          variant={isFeatured ? 'contained' : 'outlined'}
          size="large"
          fullWidth
        >
          Select {plan.name}
        </Button>
      </Stack>
    </Card>
  );
}
