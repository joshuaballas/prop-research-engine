import AnalyticsOutlinedIcon from '@mui/icons-material/AnalyticsOutlined';
import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import ChevronRightRoundedIcon from '@mui/icons-material/ChevronRightRounded';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded';
import ManageAccountsOutlinedIcon from '@mui/icons-material/ManageAccountsOutlined';
import PaymentOutlinedIcon from '@mui/icons-material/PaymentOutlined';
import PersonSearchOutlinedIcon from '@mui/icons-material/PersonSearchOutlined';
import SearchOutlinedIcon from '@mui/icons-material/SearchOutlined';
import ShoppingCartOutlinedIcon from '@mui/icons-material/ShoppingCartOutlined';
import StackedLineChartOutlinedIcon from '@mui/icons-material/StackedLineChartOutlined';
import { Box, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { useAppAuth } from '../../auth/AppAuthProvider';
import { AppCard } from '../../components/ui/AppCard';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

const mainItems = [
  { label: 'Dashboard', description: 'Today overview', to: '/app/dashboard', icon: <DashboardOutlinedIcon /> },
  { label: 'Prop Scanner', description: 'Browse current props', to: '/app/markets', icon: <SearchOutlinedIcon /> },
  { label: 'Top Picks', description: 'Curated opportunities', to: '/app/top-picks', icon: <AutoGraphOutlinedIcon /> },
  { label: 'Line Shopping', description: 'Compare available lines', to: '/app/line-shopping', icon: <StackedLineChartOutlinedIcon /> }
];

const toolItems = [
  { label: 'Player Research', description: 'Player context', to: '/app/player-research', icon: <PersonSearchOutlinedIcon /> },
  { label: 'Entry Tracker', description: 'Entry tracking workspace', to: '/app/entry-tracker', icon: <ShoppingCartOutlinedIcon /> },
  { label: 'Model Performance', description: 'Latest graded results', to: '/app/performance', icon: <AnalyticsOutlinedIcon /> }
];

const accountItems = [
  { label: 'Account', description: 'Profile and access', to: '/app/account', icon: <ManageAccountsOutlinedIcon /> },
  { label: 'Billing', description: 'Plan and billing setup', to: '/app/billing', icon: <PaymentOutlinedIcon /> }
];

export function AppMenuPage() {
  const auth = useAppAuth();

  const handleSignOut = async () => {
    await auth.signOut();
  };

  return (
    <Stack spacing={1.5} sx={{ pt: { xs: 0.5, lg: 0 }, pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <Typography
        component="h1"
        sx={{
          position: 'absolute',
          width: 1,
          height: 1,
          p: 0,
          m: -1,
          overflow: 'hidden',
          clip: 'rect(0 0 0 0)',
          whiteSpace: 'nowrap',
          border: 0
        }}
      >
        Menu
      </Typography>

      <Stack spacing={1.5}>
        <MenuSection title="Main" items={mainItems} />
        <MenuSection title="Tools" items={toolItems} />
        <MenuSection title="Account" items={accountItems} />

        <Box sx={{ display: 'flex', justifyContent: 'center', pt: 0.75 }}>
          <Box
            component="button"
            type="button"
            onClick={() => void handleSignOut()}
            sx={{
              minHeight: 44,
              px: 1.35,
              py: 0.65,
              border: 0,
              borderRadius: radius.control,
              bgcolor: 'transparent',
              color: palette.danger,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              gap: 0.8,
              textAlign: 'center',
              cursor: 'pointer',
              font: 'inherit',
              '&:hover': {
                bgcolor: 'rgba(244,63,94,.08)'
              },
              '&:active': {
                bgcolor: 'rgba(244,63,94,.12)'
              },
              '&:focus-visible': {
                outline: `2px solid ${palette.danger}`,
                outlineOffset: 2
              }
            }}
          >
            <LogoutRoundedIcon sx={{ fontSize: 19 }} />
            <Typography fontWeight={920} color="inherit">
              Sign out
            </Typography>
          </Box>
        </Box>
      </Stack>
    </Stack>
  );
}

function MenuSection({ title, items }: { title: string; items: Array<{ label: string; description: string; to: string; icon: ReactNode }> }) {
  return (
    <AppCard>
      <Stack spacing={0.9}>
        <Typography variant="body2" color="text.secondary" fontWeight={950} sx={{ lineHeight: 1.2 }}>
          {title}
        </Typography>
        <Stack spacing={0}>
          {items.map((item, index) => (
            <MenuLinkRow key={item.to} item={item} divider={index > 0} />
          ))}
        </Stack>
      </Stack>
    </AppCard>
  );
}

function MenuLinkRow({ item, divider }: { item: { label: string; description: string; to: string; icon: ReactNode }; divider: boolean }) {
  return (
    <Box
      component={RouterLink}
      to={item.to}
      sx={{
        minHeight: 58,
        py: 1.05,
        display: 'flex',
        alignItems: 'center',
        gap: 1.25,
        color: 'text.primary',
        textDecoration: 'none',
        borderTop: divider ? `1px solid ${palette.border}` : 0,
        '&:hover': {
          color: 'primary.main'
        },
        '&:focus-visible': {
          outline: `2px solid ${palette.primary}`,
          outlineOffset: 2,
          borderRadius: radius.control
        }
      }}
    >
      <MenuIcon>{item.icon}</MenuIcon>
      <Box sx={{ minWidth: 0, flex: 1 }}>
        <Typography fontWeight={920}>{item.label}</Typography>
        <Typography variant="body2" color="text.secondary">
          {item.description}
        </Typography>
      </Box>
      <ChevronRightRoundedIcon sx={{ color: 'text.secondary', fontSize: 22, flexShrink: 0 }} />
    </Box>
  );
}

function MenuIcon({ children, tone = 'default' }: { children: ReactNode; tone?: 'default' | 'danger' }) {
  const color = tone === 'danger' ? palette.danger : palette.primary;

  return (
    <Box
      sx={{
        width: 34,
        height: 34,
        borderRadius: radius.featuredCard,
        display: 'grid',
        placeItems: 'center',
        color,
        bgcolor: tone === 'danger' ? 'rgba(244,63,94,.08)' : 'rgba(34,211,238,.08)',
        border: `1px solid ${tone === 'danger' ? 'rgba(244,63,94,.22)' : palette.borderStrong}`,
        flexShrink: 0
      }}
    >
      {children}
    </Box>
  );
}
