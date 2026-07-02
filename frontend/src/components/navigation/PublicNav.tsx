import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import { AppBar, Box, Button, Container, Stack, Toolbar, Typography } from '@mui/material';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { radius } from '../../theme/radius';

export function PublicNav() {
  const location = useLocation();
  const isPricingRoute = location.pathname === '/pricing';

  return (
    <AppBar position="sticky" elevation={0} color="transparent" sx={{ bgcolor: '#070B14', borderBottom: '1px solid rgba(148,163,184,.12)' }}>
      <Container maxWidth="xl">
        <Toolbar disableGutters sx={{ gap: 2, minHeight: 72 }}>
          <Stack component={RouterLink} to="/" direction="row" spacing={1.1} alignItems="center" sx={{ color: 'text.primary', textDecoration: 'none' }}>
            <Box sx={{ width: 34, height: 34, borderRadius: radius.featuredCard, display: 'grid', placeItems: 'center', bgcolor: 'rgba(34,211,238,.14)', color: 'primary.main', border: '1px solid rgba(34,211,238,.28)' }}>
              <AutoGraphOutlinedIcon fontSize="small" />
            </Box>
            <Box>
              <Typography variant="h6" lineHeight={1} sx={{ fontWeight: 950 }}>
                SportsBetting
              </Typography>
            </Box>
          </Stack>
          <Box flex={1} />
          <Stack direction="row" spacing={1.5} alignItems="center" sx={{ display: { xs: 'none', sm: 'flex' } }}>
            <Button component={RouterLink} to="/sign-in" color="inherit" variant="text">
              Sign in
            </Button>
            {!isPricingRoute ? (
              <Button component={RouterLink} to="/pricing" variant="contained">
                View Plans
              </Button>
            ) : null}
          </Stack>
        </Toolbar>
      </Container>
    </AppBar>
  );
}
