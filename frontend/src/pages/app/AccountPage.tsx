import CreditCardOutlinedIcon from '@mui/icons-material/CreditCardOutlined';
import PaletteOutlinedIcon from '@mui/icons-material/PaletteOutlined';
import SaveOutlinedIcon from '@mui/icons-material/SaveOutlined';
import SecurityOutlinedIcon from '@mui/icons-material/SecurityOutlined';
import { Box, Button, Grid, Stack, TextField, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockUser } from '../../data/mockUser';
import { useToast } from '../../hooks/useToast';
import { palette } from '../../theme/palette';

const inputSx = {
  '& .MuiOutlinedInput-root': {
    backgroundColor: 'rgba(15, 23, 42, 0.56)'
  }
};

const themeOptions = ['Dark', 'Light', 'System'];

function SectionTitle({ icon, title, description }: { icon: React.ReactNode; title: string; description?: string }) {
  return (
    <Stack direction="row" spacing={1.2} alignItems="flex-start">
      <Box
        sx={{
          width: 34,
          height: 34,
          borderRadius: 2.25,
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
      <Typography
        fontWeight={900}
        textAlign="right"
        sx={{ color: valueColor }}
      >
        {value}
      </Typography>
    </Box>
  );
}

export function AccountPage() {
  const { showToast } = useToast();
  const selectedTheme = mockUser.themePreference;

  return (
    <Stack spacing={3} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader title="Account" description="Manage profile details, preferences, security, and plan access." />

      <Grid container spacing={2} alignItems="stretch">
        <Grid size={{ xs: 12, lg: 7 }}>
          <Stack spacing={2}>
            <AppCard>
              <Stack spacing={2.25}>
                <SectionTitle
                  icon={<SaveOutlinedIcon fontSize="small" />}
                  title="Profile"
                  description="Keep the account details used across your research workspace current."
                />
                <Box
                  sx={{
                    display: 'grid',
                    gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' },
                    gap: 2
                  }}
                >
                  <TextField label="Name" defaultValue={mockUser.name} sx={inputSx} />
                  <TextField label="Email" defaultValue={mockUser.email} sx={inputSx} />
                </Box>
                <Box>
                  <Button
                    variant="contained"
                    startIcon={<SaveOutlinedIcon />}
                    onClick={() => showToast('Account changes saved.', 'success')}
                  >
                    Save changes
                  </Button>
                </Box>
              </Stack>
            </AppCard>

            <AppCard sx={{ flex: 1 }}>
              <Stack spacing={2}>
                <SectionTitle
                  icon={<PaletteOutlinedIcon fontSize="small" />}
                  title="Preferences"
                  description="Choose how the app should present your research workspace."
                />
                <Stack spacing={1}>
                  <Typography variant="caption" color="text.secondary" fontWeight={850} textTransform="uppercase">
                    Theme preference
                  </Typography>
                  <Box
                    sx={{
                      display: 'grid',
                      gridTemplateColumns: { xs: '1fr', sm: 'repeat(3, minmax(0, 1fr))' },
                      gap: 1
                    }}
                  >
                    {themeOptions.map((option) => {
                      const selected = option === selectedTheme;
                      return (
                        <Box
                          key={option}
                          sx={{
                            px: 1.5,
                            py: 1.1,
                            borderRadius: 2.25,
                            border: `1px solid ${selected ? palette.borderStrong : palette.border}`,
                            backgroundColor: selected ? 'rgba(34, 211, 238, 0.09)' : 'rgba(15, 23, 42, 0.46)'
                          }}
                        >
                          <Typography fontWeight={900}>{option}</Typography>
                          <Typography variant="body2" color="text.secondary">
                            {selected ? 'Current setting' : 'Available option'}
                          </Typography>
                        </Box>
                      );
                    })}
                  </Box>
                </Stack>
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
                  title="Security"
                  description="Review account protection settings before connecting live identity controls."
                />
                <Stack spacing={0.5}>
                  <DetailRow label="Email sign-in" value="Enabled" tone="success" />
                  <DetailRow label="MFA status" value="Not connected" tone="danger" />
                  <DetailRow label="Recovery controls" value="Not connected" tone="danger" />
                </Stack>
                <Typography variant="body2" color="text.secondary">
                  Account security can support authenticator-app MFA once connected. Password changes and recovery will be
                  handled by the connected account security flow.
                </Typography>
              </Stack>
            </AppCard>

            <AppCard sx={{ flex: 1 }}>
              <Stack spacing={2} sx={{ height: '100%' }}>
                <SectionTitle
                  icon={<CreditCardOutlinedIcon fontSize="small" />}
                  title="Account status"
                  description="Review current access and jump to billing details."
                />
                <Stack spacing={0.5}>
                  <DetailRow label="Current plan" value={mockUser.planName} />
                  <DetailRow label="Subscription" value={mockUser.subscriptionStatus} tone="success" />
                  <DetailRow label="Renews" value={mockUser.renewalDate} />
                </Stack>
                <Button
                  component={RouterLink}
                  to="/app/billing"
                  variant="outlined"
                  startIcon={<CreditCardOutlinedIcon />}
                  sx={{ mt: 'auto', alignSelf: { xs: 'stretch', sm: 'flex-start' } }}
                >
                  Manage billing
                </Button>
              </Stack>
            </AppCard>
          </Stack>
        </Grid>
      </Grid>
    </Stack>
  );
}
