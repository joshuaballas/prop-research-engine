import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import { AppBar, Box, Button, Container, Stack, Toolbar, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';

export function PublicNav() {
  return (
    <AppBar position="sticky" elevation={0} color="transparent" sx={{ backdropFilter: 'blur(18px)', borderBottom: '1px solid rgba(148,163,184,.12)' }}>
      <Container maxWidth="xl">
        <Toolbar disableGutters sx={{ gap: 2, minHeight: 72 }}>
          <Stack component={RouterLink} to="/" direction="row" spacing={1.1} alignItems="center" sx={{ color: 'text.primary', textDecoration: 'none' }}>
            <Box sx={{ width: 34, height: 34, borderRadius: 2, display: 'grid', placeItems: 'center', background: 'linear-gradient(135deg, #22D3EE, #818CF8)', color: '#031019' }}>
              <AutoGraphOutlinedIcon fontSize="small" />
            </Box>
            <Box>
              <Typography variant="h6" lineHeight={1} sx={{ fontWeight: 950 }}>
                EdgeScope
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'none', md: 'block' } }}>
                DFS research platform
              </Typography>
            </Box>
          </Stack>
          <Box flex={1} />
          <Stack direction="row" spacing={1} sx={{ display: { xs: 'none', sm: 'flex' } }}>
            <Button component={RouterLink} to="/#how-it-works" color="inherit">
              How it works
            </Button>
            <Button component={RouterLink} to="/pricing" color="inherit">
              Pricing
            </Button>
            <Button component={RouterLink} to="/sign-in" color="inherit">
              Sign in
            </Button>
            <Button component={RouterLink} to="/create-account" variant="contained">
              Create account
            </Button>
          </Stack>
        </Toolbar>
      </Container>
    </AppBar>
  );
}
