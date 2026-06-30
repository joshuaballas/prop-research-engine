import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import { Avatar, Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { GlossaryDialog } from '../glossary/GlossaryDialog';
import { isDemoDataMode } from '../../config/dataMode';
import { mockUser } from '../../data/mockUser';

export function AppTopBar() {
  const [glossaryOpen, setGlossaryOpen] = useState(false);

  return (
    <>
      <Stack
        direction="row"
        alignItems="center"
        justifyContent="space-between"
        gap={2}
        sx={{ py: 1.1, px: { xs: 2, md: 3 }, borderBottom: '1px solid rgba(148,163,184,.12)', bgcolor: '#070B14', position: 'sticky', top: 0, zIndex: 10 }}
      >
        <Button
          variant="outlined"
          size="small"
          startIcon={<HelpOutlineRoundedIcon />}
          onClick={() => setGlossaryOpen(true)}
          sx={{
            minHeight: 34,
            px: 1.2,
            borderRadius: 1.5,
            borderColor: 'rgba(148,163,184,.18)',
            bgcolor: 'rgba(15,23,42,.52)',
            color: 'text.secondary',
            fontWeight: 800,
            '& .MuiButton-startIcon': { color: 'inherit' },
            '&:hover': {
              borderColor: 'rgba(34,211,238,.38)',
              bgcolor: 'rgba(15,23,42,.78)',
              color: 'text.primary'
            }
          }}
        >
          Glossary
        </Button>
        <Stack direction="row" spacing={{ xs: 1, sm: 2 }} alignItems="center" minWidth={0} sx={{ flex: { xs: 1, sm: '0 1 auto' }, justifyContent: 'flex-end' }}>
          {isDemoDataMode ? (
            <Typography
              variant="caption"
              sx={{
                color: 'warning.main',
                fontWeight: 850
              }}
            >
              Demo data
            </Typography>
          ) : null}
          <Avatar sx={{ width: 34, height: 34 }}>{mockUser.name[0]}</Avatar>
        </Stack>
      </Stack>
      <GlossaryDialog open={glossaryOpen} onClose={() => setGlossaryOpen(false)} />
    </>
  );
}
