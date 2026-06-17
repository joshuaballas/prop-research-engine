import StackedLineChartOutlinedIcon from '@mui/icons-material/StackedLineChartOutlined';
import VerifiedUserOutlinedIcon from '@mui/icons-material/VerifiedUserOutlined';
import { Box, Container, Grid, Stack, Typography } from '@mui/material';
import { Outlet } from 'react-router-dom';
import { AppCard } from '../components/ui/AppCard';
import { MetadataText } from '../components/ui/MetadataText';
import { palette } from '../theme/palette';

export function AuthLayout() {
  return (
    <Box
      minHeight="100vh"
      sx={{
        background:
          `radial-gradient(circle at 20% 10%, rgba(34,211,238,.16), transparent 32%), radial-gradient(circle at 86% 18%, rgba(129,140,248,.12), transparent 34%), ${palette.bg}`,
        position: 'relative',
        overflow: 'hidden',
        '&:before': {
          content: '""',
          position: 'absolute',
          inset: 0,
          background:
            'linear-gradient(rgba(125,211,252,.035) 1px, transparent 1px), linear-gradient(90deg, rgba(125,211,252,.03) 1px, transparent 1px)',
          backgroundSize: '36px 36px',
          opacity: 0.5,
          pointerEvents: 'none'
        }
      }}
    >
      <Container maxWidth="lg" sx={{ py: { xs: 4, md: 9 } }}>
        <Grid container spacing={{ xs: 3, md: 5 }} alignItems="center" sx={{ position: 'relative', zIndex: 1 }}>
          <Grid size={{ xs: 12, md: 6 }}>
            <Stack spacing={{ xs: 2, md: 2.5 }}>
              <Typography variant="h2">Your DFS research stays organized.</Typography>
              <Typography color="text.secondary" fontSize={{ md: 18 }}>
                Compare top candidates, reference lines, modifiers, and freshness signals before you build entries.
              </Typography>

              <AppCard
                sx={{
                  display: { xs: 'none', md: 'block' },
                  background: 'rgba(8,13,26,.72)',
                  backdropFilter: 'blur(16px)'
                }}
              >
                <Stack spacing={1.35}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Typography variant="caption" color="text.secondary" fontWeight={900}>
                      RESEARCH SNAPSHOT
                    </Typography>
                    <MetadataText tone="fresh">Updated 3m ago</MetadataText>
                  </Stack>
                  {[
                    ['PrizePicks', 'Total Bases', '+12.4% edge'],
                    ['Underdog', 'Pitcher Ks', '+8.8% edge'],
                    ['Pick6', 'Hits', 'Limited history']
                  ].map(([provider, market, state]) => (
                    <Grid key={`${provider}-${market}`} container alignItems="center" spacing={1}>
                      <Grid size={{ xs: 4 }}>
                        <Typography fontWeight={950}>{provider}</Typography>
                      </Grid>
                      <Grid size={{ xs: 4 }}>
                        <Typography color="text.secondary">{market}</Typography>
                      </Grid>
                      <Grid size={{ xs: 4 }}>
                        <Typography textAlign="right" color={state.includes('+') ? 'success.main' : 'text.secondary'} fontWeight={900}>
                          {state}
                        </Typography>
                      </Grid>
                    </Grid>
                  ))}
                </Stack>
              </AppCard>

              <Grid container spacing={1.5}>
                {[
                  { icon: <StackedLineChartOutlinedIcon color="primary" />, title: 'Freshness labels', body: 'See stale-data and provider availability states before comparing.' },
                  { icon: <VerifiedUserOutlinedIcon color="primary" />, title: 'Provider-safe entries', body: 'Avoid combining legs across incompatible DFS platforms.' }
                ].map((item) => (
                  <Grid key={item.title} size={{ xs: 12, sm: 4, md: 12 }}>
                    <AppCard sx={{ background: 'rgba(15,23,42,.72)', backdropFilter: 'blur(12px)' }}>
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
