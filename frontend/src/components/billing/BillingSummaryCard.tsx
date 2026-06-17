import { Divider, Stack, Typography } from '@mui/material';
import { mockUser } from '../../data/mockUser';
import { palette } from '../../theme/palette';
import { AppCard } from '../ui/AppCard';

function BillingDetail({ label, value, tone }: { label: string; value: string; tone?: 'success' }) {
  return (
    <Stack
      direction="row"
      justifyContent="space-between"
      spacing={2}
      sx={{
        py: 1,
        borderBottom: `1px solid ${palette.border}`,
        '&:last-child': { borderBottom: 0, pb: 0 }
      }}
    >
      <Typography color="text.secondary">{label}</Typography>
      <Typography fontWeight={900} textAlign="right" sx={{ color: tone === 'success' ? palette.success : palette.text }}>
        {value}
      </Typography>
    </Stack>
  );
}

export function BillingSummaryCard() {
  return (
    <AppCard sx={{ height: '100%' }}>
      <Stack spacing={1.6}>
        <Stack spacing={0.35}>
          <Typography variant="h5" fontWeight={950}>
            Current subscription
          </Typography>
          <Typography color="text.secondary">Plan access, renewal status, and payment details.</Typography>
        </Stack>
        <Divider />
        <Stack spacing={0.4}>
          <BillingDetail label="Current plan" value={mockUser.planName} />
          <BillingDetail label="Status" value={mockUser.subscriptionStatus} tone="success" />
          <BillingDetail label="Renews" value={mockUser.renewalDate} />
          <BillingDetail label="Payment method" value="Visa ending in 4242" />
        </Stack>
      </Stack>
    </AppCard>
  );
}
