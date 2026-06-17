import { Avatar, Box, Stack, Typography } from '@mui/material';
import { mockUser } from '../../data/mockUser';
import { MetadataText } from '../ui/MetadataText';

export function AppTopBar() {
  return (
    <Stack
      direction="row"
      alignItems="center"
      justifyContent="space-between"
      gap={2}
      sx={{ py: 1.35, px: { xs: 2, md: 3 }, borderBottom: '1px solid rgba(148,163,184,.12)', bgcolor: 'rgba(7,11,20,.76)', backdropFilter: 'blur(18px)', position: 'sticky', top: 0, zIndex: 10 }}
    >
      <Stack direction="row" alignItems="center" spacing={1.5}>
        <Box>
          <Typography fontWeight={900}>DFS research dashboard</Typography>
          <Typography variant="caption" color="text.secondary">
            Line comparison and entry building
          </Typography>
        </Box>
      </Stack>
      <Stack direction="row" spacing={{ xs: 1.25, sm: 2 }} alignItems="center" minWidth={0}>
        <Stack spacing={0.1} alignItems="flex-end" sx={{ display: { xs: 'none', sm: 'flex' } }}>
          <MetadataText tone="fresh">Updated 4m ago</MetadataText>
          <Typography variant="caption" color="text.secondary">
            DFS rows usable
          </Typography>
        </Stack>
        <Avatar sx={{ width: 34, height: 34 }}>{mockUser.name[0]}</Avatar>
      </Stack>
    </Stack>
  );
}
