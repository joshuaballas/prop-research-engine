import MenuRoundedIcon from '@mui/icons-material/MenuRounded';
import { Avatar, Box, IconButton, Stack, Typography } from '@mui/material';
import { mockUser } from '../../data/mockUser';
import { StatusPill } from '../ui/StatusPill';

export function AppTopBar() {
  return (
    <Stack
      direction="row"
      alignItems="center"
      justifyContent="space-between"
      sx={{ py: 1.5, px: { xs: 2, md: 3 }, borderBottom: '1px solid rgba(148,163,184,.12)', bgcolor: 'rgba(7,11,20,.72)', backdropFilter: 'blur(18px)' }}
    >
      <Stack direction="row" alignItems="center" spacing={1.5}>
        <IconButton sx={{ display: { lg: 'none' } }}>
          <MenuRoundedIcon />
        </IconButton>
        <Box>
          <Typography fontWeight={900}>Market command center</Typography>
          <Typography variant="caption" color="text.secondary">
            Mock prototype data only
          </Typography>
        </Box>
      </Stack>
      <Stack direction="row" spacing={1.5} alignItems="center">
        <StatusPill label="Fresh" />
        <Avatar sx={{ width: 34, height: 34 }}>{mockUser.name[0]}</Avatar>
      </Stack>
    </Stack>
  );
}
