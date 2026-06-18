import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import { Box, Button, Link, Stack, TextField, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { AppCard } from '../../components/ui/AppCard';
import { palette } from '../../theme/palette';

const authFieldSx = {
  '& .MuiOutlinedInput-root': {
    borderRadius: 2,
    backgroundColor: 'rgba(8,13,26,.48)',
    '& fieldset': { borderColor: 'rgba(148,163,184,.18)' },
    '&:hover fieldset': { borderColor: 'rgba(125,211,252,.34)' },
    '&.Mui-focused fieldset': { borderColor: 'rgba(34,211,238,.72)' }
  },
  '& .MuiInputLabel-root': {
    color: 'text.secondary'
  }
};

export function SignInPage() {
  return (
    <AppCard
      sx={{
        maxWidth: 500,
        ml: { md: 'auto' },
        backgroundColor: palette.cardSurface,
        borderColor: 'rgba(125,211,252,.22)',
        boxShadow: '0 24px 80px rgba(0,0,0,.35)'
      }}
    >
      <Stack spacing={2.4}>
        <Stack spacing={0.8}>
          <Typography variant="h4">Welcome back</Typography>
          <Typography color="text.secondary">
            Sign in to continue your DFS research workflow.
          </Typography>
        </Stack>

        <Stack spacing={1.6}>
          <TextField label="Email" placeholder="you@example.com" fullWidth sx={authFieldSx} />
          <TextField label="Password" type="password" placeholder="Enter your password" fullWidth sx={authFieldSx} />
        </Stack>

        <Stack spacing={1.2}>
          <Button variant="contained" size="large" startIcon={<LockOutlinedIcon />}>
            Continue
          </Button>
          <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap">
            <Link component={RouterLink} to="#" color="primary" underline="none" fontWeight={900} variant="body2">
              Forgot password
            </Link>
            <Typography variant="body2" color="text.secondary">
              New to EdgeScope?{' '}
              <Typography component={RouterLink} to="/create-account" color="primary" sx={{ textDecoration: 'none', fontWeight: 900 }}>
                Create account
              </Typography>
            </Typography>
          </Box>
        </Stack>
      </Stack>
    </AppCard>
  );
}
