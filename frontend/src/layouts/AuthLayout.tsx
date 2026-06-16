import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined';
import StackedLineChartOutlinedIcon from '@mui/icons-material/StackedLineChartOutlined';
import VerifiedUserOutlinedIcon from '@mui/icons-material/VerifiedUserOutlined';
import { Box, Container, Grid, Stack, Typography } from '@mui/material';
import { Outlet } from 'react-router-dom';
import { AppCard } from '../components/ui/AppCard';
import { StatusPill } from '../components/ui/StatusPill';
import { palette } from '../theme/palette';

export function AuthLayout() {
  return (
    <Box minHeight="100vh" sx={{ background: `radial-gradient(circle at 20% 10%, rgba(34,211,238,.18), transparent 32%), ${palette.bg}` }}>
      <Container maxWidth="lg" sx={{ py: { xs: 4, md: 9 } }}>
        <Grid container spacing={4} alignItems="center">
          <Grid size={{ xs: 12, md: 6 }}>
            <Stack spacing={2}>
              <StatusPill label="Secure research workspace" sx={{ width: 'fit-content' }} />
              <Typography variant="h2">Sign in to your DFS market command center.</Typography>
              <Typography color="text.secondary">
                Review fresh candidates, compare provider lines, and build entries that respect each platform's compatibility rules.
              </Typography>
              <Grid container spacing={1.5}>
                {[
                  { icon: <ShieldOutlinedIcon color="primary" />, title: 'MFA-ready access', body: 'Protect your research workflow with modern account security.' },
                  { icon: <StackedLineChartOutlinedIcon color="primary" />, title: 'Fresh market snapshots', body: 'See data freshness and provider availability before acting.' },
                  { icon: <VerifiedUserOutlinedIcon color="primary" />, title: 'Provider-safe entries', body: 'Avoid combining legs across incompatible DFS platforms.' }
                ].map((item) => (
                  <Grid key={item.title} size={{ xs: 12, sm: 4, md: 12 }}>
                    <AppCard>
                      <Stack direction="row" spacing={1.5}>
                        {item.icon}
                        <Box>
                          <Typography fontWeight={900}>{item.title}</Typography>
                          <Typography variant="body2" color="text.secondary">
                            {item.body}
                          </Typography>
                        </Box>
                      </Stack>
                    </AppCard>
                  </Grid>
                ))}
              </Grid>
            </Stack>
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <Outlet />
          </Grid>
        </Grid>
      </Container>
    </Box>
  );
}
