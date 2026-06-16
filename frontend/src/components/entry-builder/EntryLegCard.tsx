import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import { IconButton, Stack, Typography } from '@mui/material';
import type { EntryLeg } from '../../types/entryBuilder';
import { AppCard } from '../ui/AppCard';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from '../markets/ProjectionModifierBadge';

export function EntryLegCard({ leg, onRemove }: { leg: EntryLeg; onRemove?: (id: string) => void }) {
  return (
    <AppCard>
      <Stack direction="row" justifyContent="space-between" gap={1}>
        <Stack spacing={0.6}>
          <Typography fontWeight={950}>{leg.player}</Typography>
          <Typography variant="body2" color="text.secondary">
            {leg.side} {leg.propType} {leg.line} · {leg.sport}
          </Typography>
          <Stack direction="row" gap={0.8} flexWrap="wrap">
            <StatusPill label={leg.entryProvider} />
            <ProjectionModifierBadge modifier={leg.projectionModifier} />
          </Stack>
        </Stack>
        {onRemove ? (
          <IconButton onClick={() => onRemove(leg.id)}>
            <DeleteOutlineRoundedIcon />
          </IconButton>
        ) : null}
      </Stack>
    </AppCard>
  );
}
