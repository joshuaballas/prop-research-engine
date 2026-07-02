import CreditCardOutlinedIcon from '@mui/icons-material/CreditCardOutlined';
import SecurityOutlinedIcon from '@mui/icons-material/SecurityOutlined';
import { Box, Button, Grid, Skeleton, Stack, TextField, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { getAccount } from '../../api/billing';
import { useApiQuery } from '../../api/useApiQuery';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

const inputSx = {
  '& .MuiOutlinedInput-root': {
    backgroundColor: 'rgba(15, 23, 42, 0.56)'
  }
};

function SectionTitle({ icon, title, description }: { icon: React.ReactNode; title: string; description?: string }) {
  return (
    <Stack direction="row" spacing={1.2} alignItems="flex-start">
      <Box
        sx={{
          width: 34,
          height: 34,
          borderRadius: radius.featuredCard,
          display: 'grid',
          placeItems: 'center',
          color: palette.primary,
          backgroundColor: 'rgba(34, 211, 238, 0.08)',
          border: `1px solid ${palette.borderStrong}`,
          flex: '0 0 auto'
        }}
      >
        {icon}
      </Box>
      <Stack spacing={0.25}>
        <Typography variant="h6" fontWeight={950}>
          {title}
        </Typography>
        {description ? (
          <Typography variant="body2" color="text.secondary">
            {description}
          </Typography>
        ) : null}
      </Stack>
    </Stack>
  );
}

function DetailRow({ label, value, tone }: { label: string; value: string; tone?: 'success' | 'warning' | 'danger' }) {
  const valueColor =
    tone === 'success' ? palette.success : tone === 'warning' ? palette.warning : tone === 'danger' ? palette.danger : palette.text;

  return (
    <Box
      sx={{
        display: 'flex',
        justifyContent: 'space-between',
        gap: 2,
        py: 1.1,
        borderBottom: `1px solid ${palette.border}`,
        '&:last-child': { borderBottom: 0, pb: 0 }
      }}
    >
      <Typography color="text.secondary">{label}</Typography>
      <Typography fontWeight={900} textAlign="right" sx={{ color: valueColor }}>
        {value}
      </Typography>
    </Box>
  );
}

export function AccountPage() {
  const accountQuery = useApiQuery(getAccount);
  const user = accountQuery.data?.user;

  return (
    <Stack spacing={3} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader title="Account" description="Manage profile details, preferences, and plan access." />

      {accountQuery.loading ? (
        <Stack spacing={2}>
          <Skeleton variant="rounded" height={240} />
          <Skeleton variant="rounded" height={220} />
        </Stack>
      ) : (
        <Grid container spacing={2} alignItems="stretch">
          <Grid size={{ xs: 12, lg: 7 }}>
            <Stack spacing={2}>
              <AppCard>
                <Stack spacing={2.25}>
                  <SectionTitle
                    icon={<SecurityOutlinedIcon fontSize="small" />}
                    title="Profile"
                    description="Account details are synced from your signed-in identity."
                  />
                  <Box
                    sx={{
                      display: 'grid',
                      gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' },
                      gap: 2
                    }}
                  >
                    <TextField label="Name" value={user?.displayName ?? ''} InputProps={{ readOnly: true }} sx={inputSx} />
                    <TextField label="Email" value={user?.email ?? ''} InputProps={{ readOnly: true }} sx={inputSx} />
                  </Box>
                </Stack>
              </AppCard>
            </Stack>
          </Grid>

          <Grid size={{ xs: 12, lg: 5 }}>
            <Stack spacing={2} sx={{ height: '100%' }}>
              <AppCard>
                <Stack spacing={2}>
                  <SectionTitle
                    icon={<SecurityOutlinedIcon fontSize="small" />}
                    title="Access"
                    description="Local app role and account status."
                  />
                  <Stack spacing={0.5}>
                    <DetailRow label="Role" value={user?.role ?? 'Member'} />
                    <DetailRow label="Status" value={user?.status ?? 'Active'} tone={user?.status === 'Active' ? 'success' : 'warning'} />
                    <DetailRow label="Auth provider" value={user?.externalAuthProvider ?? 'Clerk'} />
                  </Stack>
                </Stack>
              </AppCard>

              <AppCard sx={{ flex: 1 }}>
                <Stack spacing={2} sx={{ height: '100%' }}>
                  <SectionTitle
                    icon={<CreditCardOutlinedIcon fontSize="small" />}
                    title="Plan access"
                    description="Review current workspace access and billing setup."
                  />
                  <Button
                    component={RouterLink}
                    to="/app/billing"
                    variant="outlined"
                    startIcon={<CreditCardOutlinedIcon />}
                    sx={{ mt: 'auto', alignSelf: { xs: 'stretch', sm: 'flex-start' } }}
                  >
                    Open billing setup
                  </Button>
                </Stack>
              </AppCard>
            </Stack>
          </Grid>
        </Grid>
      )}
    </Stack>
  );
}
