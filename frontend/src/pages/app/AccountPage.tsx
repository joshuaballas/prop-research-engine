import { Button, Grid, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockUser } from '../../data/mockUser';
import { useToast } from '../../hooks/useToast';

export function AccountPage() {
  const { showToast } = useToast();
  return (
    <Stack spacing={3}>
      <PageHeader title="Account" description="Profile and preferences." />
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 7 }}>
          <AppCard>
            <Stack spacing={2}>
              <Typography variant="h5">Profile</Typography>
              <TextField label="Name" defaultValue={mockUser.name} />
              <TextField label="Email" defaultValue={mockUser.email} />
              <TextField label="Theme preference" select defaultValue={mockUser.themePreference}>
                {['Dark', 'System', 'Light'].map((item) => <MenuItem key={item} value={item}>{item}</MenuItem>)}
              </TextField>
              <Button variant="contained" onClick={() => showToast('Profile saved.', 'success')}>Save</Button>
            </Stack>
          </AppCard>
        </Grid>
      </Grid>
    </Stack>
  );
}
