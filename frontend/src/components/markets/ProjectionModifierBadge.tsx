import { StatusPill } from '../ui/StatusPill';
import type { ProjectionModifier } from '../../types/markets';
import type { SxProps, Theme } from '@mui/material';

export function ProjectionModifierBadge({ modifier, sx }: { modifier: ProjectionModifier; sx?: SxProps<Theme> }) {
  if (modifier === 'Normal') return null;
  return <StatusPill label={modifier === 'Discount' ? 'Discounted Line' : modifier} sx={sx} />;
}
