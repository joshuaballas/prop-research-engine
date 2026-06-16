import { Box, Container, Stack } from '@mui/material';
import { Outlet } from 'react-router-dom';
import { AppSidebar } from '../components/navigation/AppSidebar';
import { AppTopBar } from '../components/navigation/AppTopBar';
import { MobileNav } from '../components/navigation/MobileNav';

export function AppLayout() {
  return (
    <Stack direction="row" minHeight="100vh">
      <AppSidebar />
      <Box flex={1} minWidth={0} pb={{ xs: 9, lg: 0 }}>
        <AppTopBar />
        <Container maxWidth="xl" sx={{ py: { xs: 2.5, md: 4 } }}>
          <Outlet />
        </Container>
        <MobileNav />
      </Box>
    </Stack>
  );
}
