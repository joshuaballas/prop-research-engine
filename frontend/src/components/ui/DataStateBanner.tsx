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
        borderRadius: 3,
        p: 2,
        background:
          status === 'Unavailable'
            ? 'linear-gradient(135deg, rgba(244,63,94,.12), rgba(15,23,42,.84))'
            : 'linear-gradient(135deg, rgba(34,211,238,.11), rgba(15,23,42,.84))'
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
