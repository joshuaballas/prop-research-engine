import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import SearchOutlinedIcon from '@mui/icons-material/SearchOutlined';
import ShoppingCartOutlinedIcon from '@mui/icons-material/ShoppingCartOutlined';
import StackedLineChartOutlinedIcon from '@mui/icons-material/StackedLineChartOutlined';
import { BottomNavigation, BottomNavigationAction, Paper } from '@mui/material';
import { useLocation, useNavigate } from 'react-router-dom';

const mobileItems = [
  { label: 'Home', to: '/app/dashboard', icon: <DashboardOutlinedIcon /> },
  { label: 'Props', to: '/app/markets', icon: <SearchOutlinedIcon /> },
  { label: 'Picks', to: '/app/top-picks', icon: <AutoGraphOutlinedIcon /> },
  { label: 'Lines', to: '/app/line-shopping', icon: <StackedLineChartOutlinedIcon /> },
  { label: 'Entry', to: '/app/entry-builder', icon: <ShoppingCartOutlinedIcon /> }
];

export function MobileNav() {
  const location = useLocation();
  const navigate = useNavigate();
  const value = mobileItems.find((item) => location.pathname === item.to)?.to ?? '/app/dashboard';

  return (
    <Paper sx={{ display: { xs: 'block', lg: 'none' }, position: 'fixed', bottom: 0, left: 0, right: 0, zIndex: 20 }} elevation={8}>
      <BottomNavigation value={value} onChange={(_, next) => navigate(next)} showLabels>
        {mobileItems.map((item) => (
          <BottomNavigationAction key={item.to} label={item.label} value={item.to} icon={item.icon} />
        ))}
      </BottomNavigation>
    </Paper>
  );
}
