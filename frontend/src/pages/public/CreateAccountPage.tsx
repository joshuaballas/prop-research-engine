import WorkspacePremiumOutlinedIcon from '@mui/icons-material/WorkspacePremiumOutlined';
import { Button, Divider, Stack, TextField, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { AppCard } from '../../components/ui/AppCard';
import { StatusPill } from '../../components/ui/StatusPill';

export function CreateAccountPage() {
  return (
    <AppCard>
      <Stack spacing={2.2}>
        <Stack spacing={0.8}>
          <StatusPill label="Start researching" sx={{ width: 'fit-content' }} />
          <Typography variant="h4">Create your EdgeScope account</Typography>
          <Typography color="text.secondary">
            Set up a secure workspace for DFS line shopping, top picks, and provider-compatible entries.
          </Typography>
        </Stack>
        <Button variant="outlined" startIcon={<WorkspacePremiumOutlinedIcon />}>
          Continue with secure account setup
        </Button>
        <Divider>or</Divider>
        <TextField label="Name" placeholder="Your name" />
        <TextField label="Email" placeholder="you@example.com" />
        <TextField label="Password" type="password" placeholder="Create a password" />
        <Button variant="contained" size="large">
          Create Account
        </Button>
        <Typography variant="body2" color="text.secondary">
          Secure sign-in and MFA support are built into the account experience.
        </Typography>
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
