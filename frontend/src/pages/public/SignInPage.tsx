import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import { Button, Divider, Stack, TextField, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { AppCard } from '../../components/ui/AppCard';
import { StatusPill } from '../../components/ui/StatusPill';

export function SignInPage() {
  return (
    <AppCard>
      <Stack spacing={2.2}>
        <Stack spacing={0.8}>
          <StatusPill label="Welcome back" sx={{ width: 'fit-content' }} />
          <Typography variant="h4">Sign in to EdgeScope</Typography>
          <Typography color="text.secondary">
            Access your market board, saved entries, and performance views.
          </Typography>
        </Stack>
        <Button variant="outlined" startIcon={<LockOutlinedIcon />}>
          Continue with secure sign-in
        </Button>
        <Divider>or</Divider>
        <TextField label="Email" placeholder="you@example.com" />
        <TextField label="Password" type="password" placeholder="Enter your password" />
        <Button variant="contained" size="large">
          Continue
        </Button>
        <Typography variant="body2" color="text.secondary">
          MFA may be required based on your account security settings.
        </Typography>
        <Typography variant="body2" color="text.secondary">
          New to EdgeScope?{' '}
          <Typography component={RouterLink} to="/create-account" color="primary" sx={{ textDecoration: 'none', fontWeight: 900 }}>
            Create an account
          </Typography>
        </Typography>
      </Stack>
    </AppCard>
  );
}
