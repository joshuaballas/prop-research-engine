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
        height: { xs: '100dvh', lg: 'auto' },
        position: 'relative',
        isolation: 'isolate',
        bgcolor: palette.bg,
        background: palette.bg,
        overflow: { xs: 'hidden', lg: 'visible' },
        // Keep the app shell background solid so component state changes cannot shift page-level color.
      }}
    >
      <AppSidebar />
      <Box
        flex={1}
        minWidth={0}
        mr={builder.isDrawerOpen ? { xs: 0, md: '360px' } : 0}
        sx={{
          height: { xs: '100dvh', lg: 'auto' },
          minHeight: 0,
          display: 'flex',
          flexDirection: 'column',
          overflow: { xs: 'hidden', lg: 'visible' },
          transition: 'margin-right 180ms ease'
        }}
      >
        <AppTopBar />
        <Box
          component="main"
          sx={{
            flex: 1,
            minHeight: 0,
            overflowY: { xs: 'auto', lg: 'visible' },
            overflowX: { xs: 'hidden', lg: 'visible' },
            pb: { xs: builder.legs.length && !builder.isDrawerOpen ? 18 : 9, lg: 0 }
          }}
        >
          <Container maxWidth="xl" sx={{ py: { xs: 2.5, md: 4 } }}>
            <Outlet />
          </Container>
        </Box>
        <MobileNav />
      </Box>
      <SharedEntryBuilderDrawer />
    </Stack>
  );
}
