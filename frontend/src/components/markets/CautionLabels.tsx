import { Box, Stack, Tooltip, Typography } from '@mui/material';
import type { MarketCaution } from '../../utils/marketCautions';
import { StatusPill } from '../ui/StatusPill';

export function CautionLabels({ cautions }: { cautions: MarketCaution[] }) {
  if (!cautions.length) {
    return null;
  }

  const visible = cautions.slice(0, 2);
  const hiddenCount = Math.max(0, cautions.length - visible.length);

  return (
    <Tooltip title={<CautionTooltipContent cautions={cautions} />} arrow enterTouchDelay={0} leaveTouchDelay={5000}>
      <Stack
        component="span"
        tabIndex={0}
        direction="row"
        alignItems="center"
        gap={0.55}
        flexWrap="wrap"
        useFlexGap
        aria-label={`Caution: ${cautions.map((caution) => caution.label).join(', ')}`}
        sx={{
          cursor: 'help',
          outline: 'none',
          '&:focus-visible .MuiChip-root': {
            borderColor: 'rgba(34,211,238,.58)'
          }
        }}
      >
        {visible.map((caution) => (
          <StatusPill
            key={caution.key}
            label={caution.label}
            sx={{ cursor: 'help' }}
          />
        ))}
        {hiddenCount ? (
          <StatusPill
            label={`+${hiddenCount}`}
            sx={{
              bgcolor: 'rgba(148,163,184,.08)',
              color: 'text.secondary',
              borderColor: 'rgba(148,163,184,.22)',
              cursor: 'help'
            }}
          />
        ) : null}
      </Stack>
    </Tooltip>
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
