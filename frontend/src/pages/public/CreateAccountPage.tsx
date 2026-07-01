import WorkspacePremiumOutlinedIcon from '@mui/icons-material/WorkspacePremiumOutlined';
import { Box, Button, Stack, TextField, Typography } from '@mui/material';
import type { FormEvent } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import { planSelectionCode } from '../../components/billing/PlanCard';
import { AppCard } from '../../components/ui/AppCard';
import { mockPricingPlans } from '../../data/mockPricingPlans';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

const authFieldSx = {
  '& .MuiOutlinedInput-root': {
    borderRadius: radius.featuredCard,
    backgroundColor: 'rgba(8,13,26,.48)',
    '& fieldset': { borderColor: 'rgba(148,163,184,.18)' },
    '&:hover fieldset': { borderColor: 'rgba(125,211,252,.34)' },
    '&.Mui-focused fieldset': { borderColor: 'rgba(34,211,238,.72)' }
  },
  '& .MuiInputLabel-root': {
    color: 'text.secondary'
  }
};

export function CreateAccountPage() {
  const [searchParams] = useSearchParams();
  const selectedPlanCode = searchParams.get('plan') ?? '';
  const selectedPlan = mockPricingPlans.find((plan) => planSelectionCode(plan) === selectedPlanCode);
  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
  };

  if (!selectedPlan) {
    return (
      <AppCard
        sx={{
          maxWidth: 520,
          ml: { md: 'auto' },
          backgroundColor: palette.cardSurface,
          borderColor: 'rgba(125,211,252,.22)',
          boxShadow: '0 24px 80px rgba(0,0,0,.35)'
        }}
      >
        <Stack spacing={2.2}>
          <Stack spacing={0.8}>
            <Typography variant="h4">Choose a plan first</Typography>
            <Typography color="text.secondary">
              Select a plan before creating an account so checkout can continue with the right subscription.
            </Typography>
          </Stack>
          <Button component={RouterLink} to="/pricing" variant="contained" size="large">
            View Plans
          </Button>
          <Typography variant="body2" color="text.secondary">
            Already have an account?{' '}
            <Typography component={RouterLink} to="/sign-in" color="primary" sx={{ textDecoration: 'none', fontWeight: 900 }}>
              Sign in
            </Typography>
          </Typography>
        </Stack>
      </AppCard>
    );
  }

  return (
    <AppCard
      sx={{
        maxWidth: 520,
        ml: { md: 'auto' },
        backgroundColor: palette.cardSurface,
        borderColor: 'rgba(125,211,252,.22)',
        boxShadow: '0 24px 80px rgba(0,0,0,.35)'
      }}
    >
      <Stack spacing={2.2}>
        <Stack spacing={0.8}>
          <Typography variant="h4">Create your account</Typography>
          <Typography color="text.secondary">
            Start with prop research, provider line context, and modifier-aware entry review.
          </Typography>
        </Stack>

        <Box
          sx={{
            p: 1.5,
            borderRadius: radius.featuredCard,
            border: '1px solid rgba(34,211,238,.2)',
            bgcolor: 'rgba(34,211,238,.06)'
          }}
        >
          <Stack direction="row" spacing={1.3} alignItems="flex-start">
            <WorkspacePremiumOutlinedIcon color="primary" />
            <Box>
              <Typography fontWeight={950}>Selected plan: {selectedPlan.name}</Typography>
              <Typography variant="body2" color="text.secondary">
                Billing handled securely at checkout later.
              </Typography>
            </Box>
          </Stack>
        </Box>

        <Stack component="form" spacing={1.6} onSubmit={handleSubmit}>
          <TextField id="create-account-email" name="email" label="Email" type="email" placeholder="you@example.com" autoComplete="email" fullWidth sx={authFieldSx} />
          <TextField id="create-account-password" name="new-password" label="Password" type="password" placeholder="Create a password" autoComplete="new-password" fullWidth sx={authFieldSx} />
          <TextField id="create-account-confirm-password" name="confirm-password" label="Confirm password" type="password" placeholder="Confirm your password" autoComplete="new-password" fullWidth sx={authFieldSx} />
          <Button type="submit" variant="contained" size="large">
            Continue to Checkout
          </Button>
        </Stack>

        <Stack spacing={1.2}>
          <Typography variant="body2" color="text.secondary">
            Already have an account?{' '}
            <Typography component={RouterLink} to="/sign-in" color="primary" sx={{ textDecoration: 'none', fontWeight: 900 }}>
              Sign in
            </Typography>
          </Typography>
        </Stack>
      </Stack>
    </AppCard>
  );
}
