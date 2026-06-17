import { Box, Container, Stack } from '@mui/material';
import { Outlet } from 'react-router-dom';
import { AppSidebar } from '../components/navigation/AppSidebar';
import { AppTopBar } from '../components/navigation/AppTopBar';
import { MobileNav } from '../components/navigation/MobileNav';
import { palette } from '../theme/palette';

export function AppLayout() {
  return (
    <Stack
      direction="row"
      minHeight="100vh"
      sx={{
        bgcolor: palette.bg,
        background:
          `radial-gradient(circle at 18% 0%, rgba(34,211,238,.12), transparent 30%), radial-gradient(circle at 88% 8%, rgba(129,140,248,.1), transparent 32%), ${palette.bg}`
      }}
    >
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
