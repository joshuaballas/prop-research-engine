import { Button, Divider, Stack, Typography } from '@mui/material';
import { mockUser } from '../../data/mockUser';
import { useToast } from '../../hooks/useToast';
import { AppCard } from '../ui/AppCard';
import { MetadataText } from '../ui/MetadataText';

export function BillingSummaryCard() {
  const { showToast } = useToast();
  return (
    <AppCard>
      <Stack spacing={1.5}>
        <Stack direction="row" justifyContent="space-between">
          <Typography variant="h5">Billing</Typography>
          <MetadataText tone="fresh">{mockUser.subscriptionStatus}</MetadataText>
        </Stack>
        <Divider />
        <Typography>
          Current plan: <b>{mockUser.planName}</b>
        </Typography>
        <Typography color="text.secondary">Renews {mockUser.renewalDate}</Typography>
        <Typography color="text.secondary">Payment method: Visa ending in 4242</Typography>
        <Button variant="contained" onClick={() => showToast('Billing portal action mocked.', 'success')}>
          Manage billing
        </Button>
      </Stack>
    </AppCard>
  );
}
