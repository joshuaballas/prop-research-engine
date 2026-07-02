import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import { Box, Button, Card, CardContent, Divider, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { useAppAuth } from '../../auth/AppAuthProvider';
import type { PricingPlan } from '../../types/billing';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';
import { formatCurrency } from '../../utils/formatters';

export function planSelectionCode(plan: PricingPlan) {
  return plan.checkoutPlanCode || plan.slug || plan.code || plan.name.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
}

export function PlanCard({ plan }: { plan: PricingPlan }) {
  const isFeatured = Boolean(plan.isFeatured);
  const auth = useAppAuth();
  const selectedPlanCode = planSelectionCode(plan);
  const targetPath = auth.isLoaded && auth.isSignedIn
    ? `/app/billing?plan=${encodeURIComponent(selectedPlanCode)}&interval=Monthly`
    : `/create-account?plan=${encodeURIComponent(selectedPlanCode)}&interval=Monthly`;

  return (
    <Card
      elevation={0}
      sx={{
        height: '100%',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        borderRadius: radius.featuredCard,
        border: `1px solid ${isFeatured ? 'rgba(34,211,238,.62)' : 'rgba(148,163,184,.18)'}`,
        backgroundColor: palette.cardSurface,
        backgroundImage: 'none',
        boxShadow: 'none'
      }}
    >
      <CardContent
        sx={{
          p: 2.25,
          '&:last-child': { pb: 2.25 },
          display: 'flex',
          flex: 1,
          flexDirection: 'column',
          width: '100%'
        }}
      >
        <Stack spacing={2} sx={{ flex: 1, minHeight: 0 }}>
          <Stack spacing={1.2}>
            <Typography variant="h5">{plan.name}</Typography>
            <Typography color="text.secondary" minHeight={48}>
              {plan.description}
            </Typography>
          </Stack>

          <Typography variant="h3" lineHeight={1}>
            {formatCurrency(plan.priceCents)}
            <Typography component="span" color="text.secondary" variant="body2" fontWeight={800}>
              /{plan.billingInterval}
            </Typography>
          </Typography>

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

          <Box sx={{ flex: 1 }} />

          <Stack spacing={1.5}>
            <Divider />
            <Button
              component={RouterLink}
              to={targetPath}
              variant={isFeatured ? 'contained' : 'outlined'}
              size="large"
              fullWidth
            >
              Select {plan.name}
            </Button>
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
}
