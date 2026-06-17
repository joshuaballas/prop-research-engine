import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import CompareArrowsRoundedIcon from '@mui/icons-material/CompareArrowsRounded';
import { Button, Divider, Stack, Typography } from '@mui/material';
import { useToast } from '../../hooks/useToast';
import { formatAmericanOdds, formatDecimal, formatPercent, formatRiskToWin } from '../../utils/formatters';
import type { MarketOpportunity } from '../../types/markets';
import { AppCard } from '../ui/AppCard';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { ProviderLabel } from '../providers/ProviderLabel';

interface TopPickCardProps {
  market: MarketOpportunity;
  onAdd?: (market: MarketOpportunity) => void;
  hideActions?: boolean;
}

export function TopPickCard({ market, onAdd, hideActions = false }: TopPickCardProps) {
  const { showToast } = useToast();
  const handleCompare = () => showToast('Line comparison is available inside the app workspace.', 'info');

  return (
    <AppCard sx={{ height: '100%' }}>
      <Stack spacing={1.5}>
        <Stack direction="row" justifyContent="space-between" gap={1}>
          <Stack spacing={0.2}>
            <Typography variant="caption" color="primary" fontWeight={950}>
              #{market.rank ?? '-'} {market.sport}
            </Typography>
            <Typography variant="h6" lineHeight={1.1}>
              {market.player}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {market.event} - {market.startTime}
            </Typography>
          </Stack>
          <Stack direction="row" spacing={0.6} alignItems="flex-start" flexWrap="wrap" justifyContent="flex-end">
            <StatusPill label={market.side} />
            <StatusPill label={market.strategyClassification} />
            <ProjectionModifierBadge modifier={market.projectionModifier} />
          </Stack>
        </Stack>

        <Typography fontWeight={950}>
          {market.side} {market.propType} {market.line}
        </Typography>

        <Stack direction="row" flexWrap="wrap" gap={1}>
          <StatusPill label={market.providerCategory} />
          <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
          <StatusPill label={market.trustLevel} />
          <StatusPill label={market.marketShape} />
        </Stack>

        <Divider />

        <Stack direction="row" flexWrap="wrap" gap={2}>
          <Metric label="Odds" value={`${formatAmericanOdds(market.bestAmericanPrice)} / ${formatDecimal(market.bestDecimalPrice)}`} />
          <Metric label="Risk" value={formatRiskToWin(market.bestAmericanPrice)} />
          <Metric label="Model" value={formatPercent(market.modelProbability)} />
          <Metric label="Break-even" value={formatPercent(market.impliedProbability)} />
          <Metric label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} />
          <Metric label="EV" value={formatPercent(market.estimatedEv, { signed: true })} />
          <Metric label="Sample" value={`${market.historicalSampleSize}`} />
        </Stack>

        <Typography variant="body2" color="text.secondary">
          {market.reasons.join(' - ')}
        </Typography>
        {market.warnings.length ? (
          <Typography variant="body2" color="warning.main">
            {market.warnings.join(' - ')}
          </Typography>
        ) : null}

        {hideActions ? null : (
          <Stack direction="row" spacing={1}>
            <Button
              startIcon={<AddCircleOutlineRoundedIcon />}
              variant="contained"
              disabled={!market.canAddToEntry}
              onClick={() => onAdd?.(market)}
            >
              Add to Entry
            </Button>
            <Button startIcon={<CompareArrowsRoundedIcon />} variant="outlined" onClick={handleCompare}>
              Compare
            </Button>
          </Stack>
        )}
      </Stack>
    </AppCard>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <Stack minWidth={92} spacing={0.2}>
      <Typography variant="caption" color="text.secondary" fontWeight={800}>
        {label}
      </Typography>
      <Typography variant="body2" fontWeight={900}>
        {value}
      </Typography>
    </Stack>
  );
}
