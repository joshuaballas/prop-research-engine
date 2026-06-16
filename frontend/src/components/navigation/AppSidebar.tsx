import AnalyticsOutlinedIcon from '@mui/icons-material/AnalyticsOutlined';
import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import FactCheckOutlinedIcon from '@mui/icons-material/FactCheckOutlined';
import PaidOutlinedIcon from '@mui/icons-material/PaidOutlined';
import PersonOutlineOutlinedIcon from '@mui/icons-material/PersonOutlineOutlined';
import SearchOutlinedIcon from '@mui/icons-material/SearchOutlined';
import ShoppingCartOutlinedIcon from '@mui/icons-material/ShoppingCartOutlined';
import StackedLineChartOutlinedIcon from '@mui/icons-material/StackedLineChartOutlined';
import { Box, List, ListItemButton, ListItemIcon, ListItemText, Stack, Typography } from '@mui/material';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { palette } from '../../theme/palette';

const navItems = [
  { label: 'Dashboard', to: '/app/dashboard', icon: <DashboardOutlinedIcon /> },
  { label: 'Markets', to: '/app/markets', icon: <SearchOutlinedIcon /> },
  { label: 'Top Picks', to: '/app/top-picks', icon: <AutoGraphOutlinedIcon /> },
  { label: 'Line Shopping', to: '/app/line-shopping', icon: <StackedLineChartOutlinedIcon /> },
  { label: 'Entry Builder', to: '/app/entry-builder', icon: <ShoppingCartOutlinedIcon /> },
  { label: 'Performance', to: '/app/performance', icon: <AnalyticsOutlinedIcon /> },
  { label: 'Account', to: '/app/account', icon: <PersonOutlineOutlinedIcon /> },
  { label: 'Billing', to: '/app/billing', icon: <PaidOutlinedIcon /> }
];

export function AppSidebar() {
  const location = useLocation();

  return (
    <Box
      sx={{
        width: 280,
        flexShrink: 0,
        display: { xs: 'none', lg: 'block' },
        borderRight: `1px solid ${palette.border}`,
        bgcolor: 'rgba(7,11,20,.82)',
        minHeight: '100vh',
        position: 'sticky',
        top: 0
      }}
    >
      <Stack spacing={3} p={2.5}>
        <Stack direction="row" spacing={1.2} alignItems="center">
          <FactCheckOutlinedIcon color="primary" />
          <Box>
            <Typography variant="h6" lineHeight={1} fontWeight={950}>
              EdgeScope
            </Typography>
            <Typography variant="caption" color="text.secondary">
              DFS research suite
            </Typography>
          </Box>
        </Stack>
        <List disablePadding>
          {navItems.map((item) => {
            const active = location.pathname === item.to;
            return (
              <ListItemButton
                key={item.to}
                component={RouterLink}
                to={item.to}
                selected={active}
                sx={{
                  mb: 0.6,
                  borderRadius: 2,
                  color: active ? 'primary.main' : 'text.secondary',
                  '&.Mui-selected': {
                    bgcolor: 'rgba(34,211,238,.1)',
                    border: `1px solid ${palette.borderStrong}`
                  }
                }}
              >
                <ListItemIcon sx={{ color: 'inherit', minWidth: 38 }}>{item.icon}</ListItemIcon>
                <ListItemText primary={item.label} primaryTypographyProps={{ fontWeight: 850 }} />
              </ListItemButton>
            );
          })}
        </List>
      </Stack>
    </Box>
  );
}
