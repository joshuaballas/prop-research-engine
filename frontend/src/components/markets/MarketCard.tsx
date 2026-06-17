import { Stack, Typography } from '@mui/material';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { AppCard } from '../ui/AppCard';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { ProviderLabel } from '../providers/ProviderLabel';

export function MarketCard({ market }: { market: MarketOpportunity }) {
  return (
    <AppCard>
      <Stack spacing={1}>
        <Stack direction="row" justifyContent="space-between">
          <Typography fontWeight={950}>{market.player}</Typography>
          <StatusPill label={market.strategyClassification} />
        </Stack>
        <Typography color="text.secondary" variant="body2">
          {market.sport} · {market.event}
        </Typography>
        <Typography fontWeight={900}>
          {market.side} {market.propType} {market.line}
        </Typography>
        <Stack direction="row" gap={1} flexWrap="wrap">
          <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
          <StatusPill label={market.providerCategory} />
          <ProjectionModifierBadge modifier={market.projectionModifier} />
        </Stack>
        <Stack direction="row" justifyContent="space-between">
          <Typography color="text.secondary">EV</Typography>
          <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
            {formatPercent(market.estimatedEv, { signed: true })}
          </Typography>
        </Stack>
      </Stack>
    </AppCard>
  );
}
