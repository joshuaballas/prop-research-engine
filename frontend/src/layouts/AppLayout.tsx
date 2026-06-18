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
        background: palette.bg,
        // Keep the app shell background solid so component state changes cannot shift page-level color.
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
