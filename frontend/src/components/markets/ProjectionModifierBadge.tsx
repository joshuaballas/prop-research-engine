import { StatusPill } from '../ui/StatusPill';
import type { ProjectionModifier } from '../../types/markets';

export function ProjectionModifierBadge({ modifier }: { modifier: ProjectionModifier }) {
  if (modifier === 'Normal') return null;
  return <StatusPill label={modifier} />;
}
