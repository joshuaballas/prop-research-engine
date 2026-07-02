import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import { Box, Button, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { AppCard } from '../components/ui/AppCard';
import { palette } from '../theme/palette';
import { radius } from '../theme/radius';

export function AuthSetupState({ title = 'Authentication setup needed' }: { title?: string }) {
  return (
    <AppCard
      sx={{
        maxWidth: 540,
        mx: 'auto',
        backgroundColor: palette.cardSurface,
        borderColor: 'rgba(125,211,252,.22)'
      }}
    >
      <Stack spacing={2.2} alignItems="flex-start">
        <Box
          sx={{
            width: 42,
            height: 42,
            borderRadius: radius.featuredCard,
            display: 'grid',
            placeItems: 'center',
            color: 'primary.main',
            bgcolor: 'rgba(34,211,238,.1)',
            border: '1px solid rgba(34,211,238,.22)'
          }}
        >
          <LockOutlinedIcon />
        </Box>
        <Stack spacing={0.8}>
          <Typography variant="h4">{title}</Typography>
          <Typography color="text.secondary">
            Clerk is not configured for this local frontend yet.
          </Typography>
        </Stack>
        <Button component={RouterLink} to="/" variant="outlined">
          Back to home
        </Button>
      </Stack>
    </AppCard>
  );
}
