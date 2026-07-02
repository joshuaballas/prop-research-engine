import AlternateEmailOutlinedIcon from '@mui/icons-material/AlternateEmailOutlined';
import ManageAccountsOutlinedIcon from '@mui/icons-material/ManageAccountsOutlined';
import PasswordOutlinedIcon from '@mui/icons-material/PasswordOutlined';
import { useClerk, useUser } from '@clerk/clerk-react';
import { Avatar, Box, Button, Skeleton, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { clerkUserProfileAppearance } from '../../auth/clerkAppearance';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

function SettingsRow({
  icon,
  title,
  value,
  actionLabel,
  onAction
}: {
  icon: ReactNode;
  title: string;
  value: string;
  actionLabel?: string;
  onAction?: () => void;
}) {
  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={1.5}
      alignItems={{ xs: 'stretch', sm: 'center' }}
      justifyContent="space-between"
      sx={{
        py: 1.5,
        borderTop: `1px solid ${palette.border}`,
        '&:first-of-type': { borderTop: 0, pt: 0 },
        '&:last-of-type': { pb: 0 }
      }}
    >
      <Stack direction="row" spacing={1.25} alignItems="center" minWidth={0}>
        <Box
          sx={{
            width: 36,
            height: 36,
            borderRadius: radius.card,
            display: 'grid',
            placeItems: 'center',
            flex: '0 0 auto',
            color: palette.primary,
            backgroundColor: 'rgba(34, 211, 238, 0.08)',
            border: `1px solid ${palette.borderStrong}`
          }}
        >
          {icon}
        </Box>
        <Stack spacing={0.25} minWidth={0}>
          <Typography fontWeight={900}>
            {title}
          </Typography>
          <Typography color="text.secondary" noWrap>
            {value}
          </Typography>
        </Stack>
      </Stack>

      {actionLabel && onAction ? (
        <Button variant="outlined" size="small" onClick={onAction} sx={{ alignSelf: { xs: 'stretch', sm: 'center' } }}>
          {actionLabel}
        </Button>
      ) : null}
    </Stack>
  );
}

export function AccountPage() {
  const { openUserProfile } = useClerk();
  const { isLoaded, user } = useUser();
  const primaryEmail = user?.primaryEmailAddress?.emailAddress ?? '';
  const firstLastName = [user?.firstName, user?.lastName].filter(Boolean).join(' ').trim();
  const displayName = firstLastName || user?.fullName || user?.username || 'Account';
  const avatarInitial = displayName.trim()[0]?.toUpperCase() ?? 'A';

  const openAccountSettings = (startPath?: '/security') => {
    openUserProfile({
      appearance: clerkUserProfileAppearance,
      ...(startPath ? { __experimental_startPath: startPath } : {})
    });
  };

  return (
    <Stack spacing={3} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader title="Account" description="Manage your profile and sign-in settings." />

      {!isLoaded ? (
        <Stack spacing={2}>
          <Skeleton variant="rounded" height={180} />
          <Skeleton variant="rounded" height={280} />
        </Stack>
      ) : (
        <Stack spacing={2}>
          <AppCard contentSx={{ p: { xs: 2, md: 2.75 }, '&:last-child': { pb: { xs: 2, md: 2.75 } } }}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ xs: 'flex-start', sm: 'center' }} justifyContent="space-between">
              <Stack direction="row" spacing={1.75} alignItems="center" minWidth={0}>
                <Avatar
                  src={user?.imageUrl}
                  alt={displayName}
                  sx={{
                    width: 58,
                    height: 58,
                    flex: '0 0 auto',
                    color: palette.text,
                    fontWeight: 950,
                  }}
                >
                  {avatarInitial}
                </Avatar>
                <Stack spacing={0.35} minWidth={0}>
                  <Typography variant="h5" fontWeight={950} noWrap>
                    {displayName}
                  </Typography>
                </Stack>
              </Stack>
              <Button
                variant="contained"
                startIcon={<ManageAccountsOutlinedIcon />}
                onClick={() => openAccountSettings()}
                sx={{ alignSelf: { xs: 'stretch', sm: 'center' } }}
              >
                Manage profile
              </Button>
            </Stack>
          </AppCard>

          <AppCard contentSx={{ p: { xs: 2, md: 2.75 }, '&:last-child': { pb: { xs: 2, md: 2.75 } } }}>
            <Stack spacing={2}>
              <Stack spacing={0.45}>
                <Typography variant="h6" fontWeight={950}>
                  Sign-in & security
                </Typography>
                <Typography color="text.secondary">
                  Review the sign-in details managed by your signed-in identity.
                </Typography>
              </Stack>

              <Stack>
                <SettingsRow
                  icon={<AlternateEmailOutlinedIcon fontSize="small" />}
                  title="Email address"
                  value={primaryEmail || 'No primary email available'}
                  actionLabel="Manage"
                  onAction={() => openAccountSettings()}
                />
                <SettingsRow
                  icon={<PasswordOutlinedIcon fontSize="small" />}
                  title="Password"
                  value="Managed by your signed-in identity"
                  actionLabel="Manage"
                  onAction={() => openAccountSettings('/security')}
                />
              </Stack>
            </Stack>
          </AppCard>
        </Stack>
      )}
    </Stack>
  );
}
