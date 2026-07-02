import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import MenuRoundedIcon from '@mui/icons-material/MenuRounded';
import SearchOutlinedIcon from '@mui/icons-material/SearchOutlined';
import StackedLineChartOutlinedIcon from '@mui/icons-material/StackedLineChartOutlined';
import { BottomNavigation, BottomNavigationAction, Paper } from '@mui/material';
import { useLocation, useNavigate } from 'react-router-dom';

const mobileItems = [
  { label: 'Home', to: '/app/dashboard', icon: <DashboardOutlinedIcon /> },
  { label: 'Props', to: '/app/markets', icon: <SearchOutlinedIcon /> },
  { label: 'Picks', to: '/app/top-picks', icon: <AutoGraphOutlinedIcon /> },
  { label: 'Lines', to: '/app/line-shopping', icon: <StackedLineChartOutlinedIcon /> },
  { label: 'Menu', to: '/app/menu', icon: <MenuRoundedIcon /> }
];

const menuActivePaths = new Set([
  '/app/menu',
  '/app/player-research',
  '/app/entry-tracker',
  '/app/performance',
  '/app/account',
  '/app/billing'
]);

export function MobileNav() {
  const location = useLocation();
  const navigate = useNavigate();
  const value = resolveMobileNavValue(location.pathname);

  return (
    <Paper sx={{ display: { xs: 'block', lg: 'none' }, position: 'fixed', bottom: 0, left: 0, right: 0, zIndex: 20 }} elevation={8}>
      <BottomNavigation
        value={value}
        onChange={(_, next) => {
          navigate(next);
        }}
        showLabels
      >
        {mobileItems.map((item) => (
          <BottomNavigationAction key={item.to} label={item.label} value={item.to} icon={item.icon} />
        ))}
      </BottomNavigation>
    </Paper>
  );
}

function resolveMobileNavValue(pathname: string) {
  const directItem = mobileItems.find((item) => pathname === item.to);
  if (directItem) {
    return directItem.to;
  }

  return menuActivePaths.has(pathname) ? '/app/menu' : false;
}
