import { Box } from '@mui/material';
import { Outlet } from 'react-router-dom';
import { PublicNav } from '../components/navigation/PublicNav';

export function PublicLayout() {
  return (
    <Box minHeight="100vh">
      <PublicNav />
      <Outlet />
    </Box>
  );
}
