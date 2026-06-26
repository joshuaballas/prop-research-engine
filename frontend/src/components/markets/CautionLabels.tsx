import { Box, Stack, Tooltip, Typography, useMediaQuery, useTheme } from '@mui/material';
import type { MarketCaution } from '../../utils/marketCautions';
import { StatusPill } from '../ui/StatusPill';

export function CautionLabels({ cautions, maxVisible }: { cautions: MarketCaution[]; maxVisible?: 1 | 2 }) {
  const theme = useTheme();
  const isCompact = useMediaQuery(theme.breakpoints.down('sm'));

  if (!cautions.length) {
    return null;
  }

  const visibleCount = maxVisible ?? (isCompact ? 1 : 2);
  const visible = cautions.slice(0, visibleCount);
  const hiddenCount = Math.max(0, cautions.length - visible.length);
  const hidden = cautions.slice(visible.length);

  return (
    <Stack
      component="span"
      direction="row"
      alignItems="center"
      gap={0.55}
      flexWrap="nowrap"
      useFlexGap
      aria-label={`Caution: ${cautions.map((caution) => caution.label).join(', ')}`}
      sx={{
        minWidth: 0,
        maxWidth: '100%',
        overflow: 'hidden',
        '& .MuiChip-root': {
          maxWidth: { xs: 120, sm: 152 },
          flexShrink: 1
        },
        '& .MuiChip-label': {
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap'
        }
      }}
    >
      {visible.map((caution) => (
        <StatusPill
          key={caution.key}
          label={caution.label}
          title={caution.label}
        />
      ))}
      {hiddenCount ? (
        <Tooltip title={<CautionTooltipContent cautions={hidden} />} arrow enterTouchDelay={0} leaveTouchDelay={5000}>
          <Box
            component="span"
            tabIndex={0}
            sx={{
              display: 'inline-flex',
              flexShrink: 0,
              cursor: 'help',
              outline: 'none',
              '&:focus-visible .MuiChip-root': {
                borderColor: 'rgba(34,211,238,.58)'
              }
            }}
          >
            <StatusPill
              label={`+${hiddenCount}`}
              sx={{
                bgcolor: 'rgba(148,163,184,.08)',
                color: 'text.secondary',
                borderColor: 'rgba(148,163,184,.22)',
                cursor: 'help'
              }}
            />
          </Box>
        </Tooltip>
      ) : null}
    </Stack>
  );
}

function CautionTooltipContent({ cautions }: { cautions: MarketCaution[] }) {
  return (
    <Stack spacing={0.65}>
      {cautions.map((caution) => (
        <Typography key={caution.key} component="div" variant="caption">
          <Box component="span" sx={{ color: 'warning.main', fontWeight: 900 }}>
            {caution.label}:
          </Box>{' '}
          {caution.description}
        </Typography>
      ))}
    </Stack>
  );
}
