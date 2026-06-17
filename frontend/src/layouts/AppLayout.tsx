import { Box, Container, Stack } from '@mui/material';
import { Outlet } from 'react-router-dom';
import { SharedEntryBuilderDrawer } from '../components/entry-builder/SharedEntryBuilderDrawer';
import { AppSidebar } from '../components/navigation/AppSidebar';
import { AppTopBar } from '../components/navigation/AppTopBar';
import { MobileNav } from '../components/navigation/MobileNav';
import { useEntryBuilder } from '../hooks/useEntryBuilder';
import { palette } from '../theme/palette';

export function AppLayout() {
  const builder = useEntryBuilder();
  return (
    <Stack
      direction="row"
      minHeight="100vh"
      sx={{
        position: 'relative',
        isolation: 'isolate',
        bgcolor: palette.bg,
        background: {
          xs: palette.bg,
          md: `radial-gradient(circle at 18% 0%, rgba(34,211,238,.12), transparent 30%), radial-gradient(circle at 88% 8%, rgba(129,140,248,.1), transparent 32%), ${palette.bg}`
        },
        '&::before': {
          content: '""',
          display: { xs: 'block', md: 'none' },
          position: 'fixed',
          inset: 0,
          zIndex: -1,
          pointerEvents: 'none',
          background: `radial-gradient(circle at 18vw 0, rgba(34,211,238,.12), transparent 30vh), radial-gradient(circle at 88vw 8vh, rgba(129,140,248,.1), transparent 32vh), ${palette.bg}`
        }
      }}
    >
      <AppSidebar />
      <Box
        flex={1}
        minWidth={0}
        pb={{ xs: builder.legs.length && !builder.isDrawerOpen ? 18 : 9, lg: 0 }}
        mr={builder.isDrawerOpen ? { xs: 0, md: '360px' } : 0}
        sx={{ transition: 'margin-right 180ms ease, padding-bottom 180ms ease' }}
      >
        <AppTopBar />
        <Container maxWidth="xl" sx={{ py: { xs: 2.5, md: 4 } }}>
          <Outlet />
        </Container>
        <MobileNav />
      </Box>
      <SharedEntryBuilderDrawer />
    </Stack>
  );
}
