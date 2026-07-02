import { Box, Container } from '@mui/material';
import { Outlet } from 'react-router-dom';
import { palette } from '../theme/palette';

export function AuthLayout() {
  return (
    <Box
      minHeight="100vh"
      sx={{
        backgroundColor: palette.bg,
        position: 'relative',
        overflowX: 'hidden'
      }}
    >
      <Container
        maxWidth="sm"
        sx={{
          minHeight: '100vh',
          py: { xs: 2.5, sm: 5, md: 7 },
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          position: 'relative',
          zIndex: 1
        }}
      >
        <Box sx={{ width: '100%' }}>
          <Outlet />
        </Box>
      </Container>
    </Box>
  );
}
