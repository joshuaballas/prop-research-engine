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
        top: 0,
        overflow: 'hidden',
        '&:before': {
          content: '""',
          position: 'absolute',
          inset: 0,
          background:
            'radial-gradient(circle at 20% 0%, rgba(34,211,238,.1), transparent 34%), linear-gradient(rgba(125,211,252,.035) 1px, transparent 1px), linear-gradient(90deg, rgba(125,211,252,.025) 1px, transparent 1px)',
          backgroundSize: 'auto, 34px 34px, 34px 34px',
          pointerEvents: 'none'
        }
      }}
    >
      <Stack spacing={3} p={2.5} sx={{ position: 'relative', zIndex: 1 }}>
        <Stack direction="row" spacing={1.2} alignItems="center">
          <Box
            sx={{
              width: 40,
              height: 40,
              borderRadius: 2,
              display: 'grid',
              placeItems: 'center',
              color: 'primary.main',
              bgcolor: 'rgba(34,211,238,.12)',
              border: '1px solid rgba(34,211,238,.28)'
            }}
          >
            <FactCheckOutlinedIcon />
          </Box>
          <Box>
            <Typography variant="h6" lineHeight={1} fontWeight={950}>
              EdgeScope
            </Typography>
            <Typography variant="caption" color="text.secondary">
              DFS research workspace
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
                  border: '1px solid transparent',
                  transition: 'background-color .18s ease, border-color .18s ease, color .18s ease',
                  '&:hover': {
                    bgcolor: 'rgba(148,163,184,.08)',
                    color: 'text.primary'
                  },
                  '&.Mui-selected': {
                    bgcolor: 'rgba(34,211,238,.1)',
                    borderColor: palette.borderStrong,
                    color: 'primary.main',
                    '&:hover': {
                      bgcolor: 'rgba(34,211,238,.13)'
                    }
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
