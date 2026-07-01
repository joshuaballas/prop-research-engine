import AnalyticsOutlinedIcon from '@mui/icons-material/AnalyticsOutlined';
import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import FactCheckOutlinedIcon from '@mui/icons-material/FactCheckOutlined';
import PersonOutlineOutlinedIcon from '@mui/icons-material/PersonOutlineOutlined';
import PersonSearchOutlinedIcon from '@mui/icons-material/PersonSearchOutlined';
import SearchOutlinedIcon from '@mui/icons-material/SearchOutlined';
import ShoppingCartOutlinedIcon from '@mui/icons-material/ShoppingCartOutlined';
import StackedLineChartOutlinedIcon from '@mui/icons-material/StackedLineChartOutlined';
import { Box, List, ListItemButton, ListItemIcon, ListItemText, Stack, Typography } from '@mui/material';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

const navItems = [
  { label: 'Dashboard', to: '/app/dashboard', icon: <DashboardOutlinedIcon /> },
  { label: 'Prop Scanner', to: '/app/markets', icon: <SearchOutlinedIcon /> },
  { label: 'Top Picks', to: '/app/top-picks', icon: <AutoGraphOutlinedIcon /> },
  { label: 'Line Shopping', to: '/app/line-shopping', icon: <StackedLineChartOutlinedIcon /> },
  { label: 'Player Research', to: '/app/player-research', icon: <PersonSearchOutlinedIcon /> },
  { label: 'Entry Tracker', to: '/app/entry-tracker', icon: <ShoppingCartOutlinedIcon /> },
  { label: 'Model Performance', to: '/app/performance', icon: <AnalyticsOutlinedIcon /> },
  { label: 'Account', to: '/app/account', icon: <PersonOutlineOutlinedIcon /> }
];

export function AppSidebar() {
  const location = useLocation();

  return (
    <Box
      sx={{
        width: 256,
        flexShrink: 0,
        display: { xs: 'none', lg: 'block' },
        borderRight: '1px solid rgba(148,163,184,.12)',
        backgroundColor: '#070B14',
        minHeight: '100vh',
        position: 'sticky',
        top: 0,
        overflow: 'hidden'
      }}
    >
      <Stack spacing={2.4} px={2} py={2.1} sx={{ position: 'relative', zIndex: 1 }}>
        <Stack
          direction="row"
          spacing={1.1}
          alignItems="center"
          sx={{
            pb: 1.6,
            borderBottom: '1px solid rgba(148,163,184,.1)'
          }}
        >
          <Box
            sx={{
              width: 36,
              height: 36,
              borderRadius: radius.card,
              display: 'grid',
              placeItems: 'center',
              color: 'primary.main',
              bgcolor: 'rgba(34,211,238,.1)',
              border: '1px solid rgba(34,211,238,.24)',
              boxShadow: 'none'
            }}
          >
            <FactCheckOutlinedIcon fontSize="small" />
          </Box>
          <Box>
            <Typography variant="h6" lineHeight={1} fontWeight={950}>
              SportsBetting
            </Typography>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25 }}>
              Prop market workspace
            </Typography>
          </Box>
        </Stack>
        <List disablePadding>
          {navItems.map((item) => {
            const active = location.pathname === item.to;
            const inactiveColor = 'rgba(180, 197, 221, 0.78)';
            const inactiveHoverColor = 'rgba(226, 232, 240, 0.92)';
            const activeTextColor = 'rgba(232, 251, 255, 0.96)';
            return (
              <ListItemButton
                key={item.to}
                component={RouterLink}
                to={item.to}
                selected={active}
                sx={{
                  mb: 0.35,
                  minHeight: 42,
                  px: 1.25,
                  py: 0.8,
                  borderRadius: radius.control,
                  color: active ? activeTextColor : inactiveColor,
                  border: '1px solid transparent',
                  position: 'relative',
                  overflow: 'hidden',
                  transition: 'background-color .18s ease, border-color .18s ease, color .18s ease, transform .18s ease',
                  '&:before': {
                    content: '""',
                    position: 'absolute',
                    left: 0,
                    top: 10,
                    bottom: 10,
                    width: 2,
                    borderRadius: radius.pill,
                    backgroundColor: active ? 'rgba(34,211,238,.86)' : 'transparent'
                  },
                  '& .MuiSvgIcon-root': {
                    fontSize: 20,
                    color: active ? 'rgba(103,232,249,.94)' : 'rgba(148, 163, 184, 0.78)'
                  },
                  '&:hover': {
                    bgcolor: active ? 'rgba(34,211,238,.07)' : 'rgba(125,211,252,.045)',
                    color: active ? activeTextColor : inactiveHoverColor,
                    transform: 'translateX(1px)'
                  },
                  '&:hover .MuiSvgIcon-root': {
                    color: active ? 'rgba(103,232,249,.98)' : 'rgba(203, 213, 225, 0.9)'
                  },
                  '&.Mui-selected': {
                    bgcolor: 'rgba(34,211,238,.052)',
                    borderColor: 'rgba(125,211,252,.08)',
                    color: activeTextColor,
                    '&:hover': {
                      bgcolor: 'rgba(34,211,238,.07)'
                    }
                  },
                  '&.Mui-focusVisible': {
                    outline: '2px solid rgba(34,211,238,.5)',
                    outlineOffset: 2,
                    boxShadow: 'none'
                  }
                }}
              >
                <ListItemIcon sx={{ color: 'inherit', minWidth: 34 }}>{item.icon}</ListItemIcon>
                <ListItemText primary={item.label} primaryTypographyProps={{ fontWeight: 850, fontSize: 14 }} />
              </ListItemButton>
            );
          })}
        </List>
      </Stack>
    </Box>
  );
}
