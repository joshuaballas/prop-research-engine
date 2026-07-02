import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import { Box, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { AppCard } from '../ui/AppCard';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

interface AccountFunnelCardProps {
  maxWidth?: number;
  contentMaxWidth?: number;
  children: ReactNode;
}

export function AccountFunnelCard({ maxWidth = 540, contentMaxWidth, children }: AccountFunnelCardProps) {
  return (
    <AppCard
      sx={{
        width: '100%',
        maxWidth,
        marginInline: 'auto',
        backgroundColor: palette.cardSurface,
        borderColor: 'rgba(125,211,252,.22)',
        boxShadow: '0 24px 80px rgba(0,0,0,.35)'
      }}
    >
      <Stack
        spacing={2.1}
        sx={{
          width: '100%',
          maxWidth: contentMaxWidth ?? '100%',
          marginInline: 'auto'
        }}
      >
        <Stack spacing={0.75} alignItems="center" textAlign="center">
          <Box
            sx={{
              width: 34,
              height: 34,
              borderRadius: radius.featuredCard,
              display: 'grid',
              placeItems: 'center',
              bgcolor: 'rgba(34,211,238,.14)',
              color: 'primary.main',
              border: '1px solid rgba(34,211,238,.28)',
              flexShrink: 0
            }}
          >
            <AutoGraphOutlinedIcon fontSize="small" />
          </Box>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="h6" lineHeight={1} sx={{ fontWeight: 950 }}>
              SportsBetting
            </Typography>
          </Box>
        </Stack>
        {children}
      </Stack>
    </AppCard>
  );
}
