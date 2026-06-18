import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import { Box, IconButton, Stack, Typography } from '@mui/material';
import type { EntryLeg } from '../../types/entryBuilder';
import { palette } from '../../theme/palette';
import { renderWarningText } from '../../utils/warnings';
import { ProjectionModifierBadge } from '../markets/ProjectionModifierBadge';
import { ProviderLabel } from '../providers/ProviderLabel';

export function EntryLegCard({ leg, onRemove }: { leg: EntryLeg; onRemove?: (id: string) => void }) {
  const warning = renderWarningText(leg.warnings);

  return (
    <Box
      sx={{
        border: `1px solid ${palette.border}`,
        borderRadius: 3,
        p: 1.35,
        background: palette.cardSurface
      }}
    >
      <Stack direction="row" justifyContent="space-between" gap={1.25} alignItems="flex-start">
        <Stack spacing={0.75} sx={{ minWidth: 0, flex: 1 }}>
          <Stack direction="row" justifyContent="space-between" gap={1} alignItems="flex-start">
            <Box sx={{ minWidth: 0 }}>
              <Typography fontWeight={950}>{leg.player}</Typography>
              <Typography variant="body2" color="text.secondary">
                {leg.side} {leg.propType} {leg.line} - {leg.sport}
              </Typography>
            </Box>
            <Typography color={leg.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950} sx={{ flexShrink: 0 }}>
              {formatSignedPercent(leg.estimatedEv)}
            </Typography>
          </Stack>

          <Stack direction="row" gap={0.8} flexWrap="wrap" alignItems="center">
            <ProviderLabel provider={leg.entryProvider} category={leg.providerCategory} compact />
            <ProjectionModifierBadge modifier={leg.projectionModifier} />
          </Stack>

          {warning ? (
            <Typography variant="body2" color="warning.main" fontWeight={800}>
              {warning}
            </Typography>
          ) : null}
        </Stack>

        {onRemove ? (
          <IconButton onClick={() => onRemove(leg.id)} size="small" sx={{ flexShrink: 0 }}>
            <DeleteOutlineRoundedIcon />
          </IconButton>
        ) : null}
      </Stack>
    </Box>
  );
}

function formatSignedPercent(value: number) {
  const formatted = `${Math.abs(value * 100).toFixed(2)}%`;
  return value >= 0 ? `+${formatted}` : `-${formatted}`;
}
