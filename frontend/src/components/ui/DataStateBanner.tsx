import { Box, Stack, Typography } from '@mui/material';
import { palette } from '../../theme/palette';
import { MetadataText } from './MetadataText';

interface DataStateBannerProps {
  title: string;
  message: string;
  status: 'Fresh' | 'Stale' | 'Limited' | 'Unavailable' | 'No data';
}

export function DataStateBanner({ title, message, status }: DataStateBannerProps) {
  const tone = status === 'Fresh' ? 'fresh' : status === 'Stale' || status === 'Limited' ? 'stale' : status === 'Unavailable' ? 'danger' : 'default';

  return (
    <Box
      sx={{
        border: `1px solid ${status === 'Unavailable' ? 'rgba(244,63,94,.35)' : palette.borderStrong}`,
        borderRadius: 4,
        p: 2,
        backgroundColor: status === 'Unavailable' ? 'rgba(76, 5, 25, 0.34)' : palette.surfaceGlow
      }}
    >
      <Stack direction={{ xs: 'column', sm: 'row' }} alignItems={{ sm: 'center' }} justifyContent="space-between" gap={1}>
        <Box>
          <Typography fontWeight={900}>{title}</Typography>
          <Typography color="text.secondary" variant="body2">
            {message}
          </Typography>
        </Box>
        {status === 'Fresh' ? null : <MetadataText tone={tone}>{status}</MetadataText>}
      </Stack>
    </Box>
  );
}
