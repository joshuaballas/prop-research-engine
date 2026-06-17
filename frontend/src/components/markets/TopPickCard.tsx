import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import CompareArrowsRoundedIcon from '@mui/icons-material/CompareArrowsRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Box, Button, Divider, Stack, Typography } from '@mui/material';
import { useToast } from '../../hooks/useToast';
import { formatAmericanOdds, formatDecimal, formatPercent, formatRiskToWin } from '../../utils/formatters';
import type { MarketOpportunity } from '../../types/markets';
import { AppCard } from '../ui/AppCard';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { ProviderLabel } from '../providers/ProviderLabel';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../ui/buttonStyles';

interface TopPickCardProps {
  market: MarketOpportunity;
  onAdd?: (market: MarketOpportunity) => void;
  hideActions?: boolean;
  variant?: 'default' | 'featured' | 'compact';
  selected?: boolean;
  incompatible?: boolean;
}

export function TopPickCard({ market, onAdd, hideActions = false, variant = 'default', selected = false, incompatible = false }: TopPickCardProps) {
  const { showToast } = useToast();
  const handleCompare = () => showToast('Line comparison is available inside the app workspace.', 'info');
  const confidence = resolveConfidence(market);

  if (variant === 'featured') {
    return (
      <AppCard
        sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}
        contentSx={{ flex: 1, display: 'grid', gridTemplateRows: '1fr auto', rowGap: 1.25 }}
      >
        <Box>
          <Stack spacing={1.55}>
            <Stack direction="row" justifyContent="space-between" gap={1.2}>
              <Stack spacing={0.35} minWidth={0}>
                <Typography variant="caption" color="primary.main" fontWeight={950}>
                  #{market.rank ?? '-'} {market.sport}
                </Typography>
                <Typography variant="h5" lineHeight={1.08}>
                  {market.player}
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.35 }}>
                  {market.event} - {market.startTime}
                </Typography>
              </Stack>
              <Stack direction="row" spacing={0.6} alignItems="flex-start" flexWrap="wrap" justifyContent="flex-end" maxWidth="48%">
                <StatusPill label="Top Pick" />
                <ProjectionModifierBadge modifier={market.projectionModifier} />
              </Stack>
            </Stack>

            <Typography fontWeight={950} sx={{ fontSize: 17, lineHeight: 1.25 }}>
              {market.side} {market.propType} {market.line}
            </Typography>

            <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1.2} flexWrap="wrap">
              <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
              <Stack direction="row" spacing={0.7} alignItems="center">
                <Typography variant="caption" color="text.secondary" fontWeight={850}>
                  Confidence
                </Typography>
                <StatusPill label={confidence} />
              </Stack>
            </Stack>

            <Divider />

            <Stack direction="row" flexWrap="wrap" gap={1.75} rowGap={1.25}>
              <Metric label="Odds" value={`${formatAmericanOdds(market.bestAmericanPrice)} / ${formatDecimal(market.bestDecimalPrice)}`} />
              <Metric label="Risk" value={formatRiskToWin(market.bestAmericanPrice)} />
              <Metric label="Model" value={formatPercent(market.modelProbability)} />
              <Metric label="Break-even" value={formatPercent(market.impliedProbability)} />
              <Metric label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} />
              <Metric label="EV" value={formatPercent(market.estimatedEv, { signed: true })} />
              <Metric label="Sample" value={`${market.historicalSampleSize}`} />
            </Stack>

            <Stack spacing={0.65}>
              {cleanReasons(market.reasons).length ? (
                <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.45 }}>
                  {cleanReasons(market.reasons).slice(0, 2).join(' - ')}
                </Typography>
              ) : null}
              {market.warnings.length ? (
                <Typography variant="body2" color="warning.main" sx={{ lineHeight: 1.45 }}>
                  {market.warnings.map(cleanWarning).join(' - ')}
                </Typography>
              ) : null}
            </Stack>
          </Stack>

        </Box>

        {hideActions ? null : (
          <Stack direction="row" spacing={1} alignSelf="end">
            <Button
              startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
              variant="contained"
              disabled={!market.canAddToEntry || incompatible}
              onClick={() => onAdd?.(market)}
              sx={entryButtonSx(selected, incompatible)}
            >
              {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
            </Button>
            <Button startIcon={<CompareArrowsRoundedIcon />} variant="outlined" onClick={handleCompare} sx={compareButtonSx}>
              Compare
            </Button>
          </Stack>
        )}
      </AppCard>
    );
  }

  if (variant === 'compact') {
    return (
      <AppCard sx={{ height: '100%' }} contentSx={{ p: 1.45, '&:last-child': { pb: 1.45 } }}>
        <Stack spacing={1.05}>
          <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
            <Stack spacing={0.35} minWidth={0}>
              <Stack direction="row" spacing={0.65} alignItems="center" flexWrap="wrap">
                <Typography variant="caption" color="primary.main" fontWeight={950}>
                  #{market.rank ?? '-'} {market.sport}
                </Typography>
                <StatusPill label={market.strategyClassification} />
                <ProjectionModifierBadge modifier={market.projectionModifier} />
              </Stack>
              <Typography variant="subtitle1" lineHeight={1.1} noWrap>
                {market.player}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.3 }}>
                {market.side} {market.propType} {market.line}
              </Typography>
            </Stack>
            <Stack alignItems="flex-end" spacing={0.1} flexShrink={0}>
              <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
                {formatPercent(market.estimatedEv, { signed: true })}
              </Typography>
              <Typography variant="caption" color="text.secondary" fontWeight={800}>
                EV
              </Typography>
            </Stack>
          </Stack>

          <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1} flexWrap="wrap">
            <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
            <Typography variant="caption" color={confidenceTone[confidence]} fontWeight={900}>
              {confidence} confidence
            </Typography>
          </Stack>

          <Divider />

          <Stack direction="row" flexWrap="wrap" gap={1.4} rowGap={0.75}>
            <Metric label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} compact />
            <Metric label="Model" value={formatPercent(market.modelProbability)} compact />
            <Metric label="Sample" value={`${market.historicalSampleSize}`} compact />
          </Stack>

          {market.warnings.length ? (
            <Typography variant="body2" color="warning.main" sx={{ lineHeight: 1.45 }}>
              {market.warnings.map(cleanWarning).join(' - ')}
            </Typography>
          ) : null}

          {hideActions ? null : (
            <Stack direction="row" spacing={1}>
              <Button
                startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
                variant="contained"
                disabled={!market.canAddToEntry || incompatible}
                onClick={() => onAdd?.(market)}
                sx={entryButtonSx(selected, incompatible)}
              >
                {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
              </Button>
              <Button startIcon={<CompareArrowsRoundedIcon />} variant="outlined" onClick={handleCompare} sx={compareButtonSx}>
                Compare
              </Button>
            </Stack>
          )}
        </Stack>
      </AppCard>
    );
  }

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
              startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
              variant="contained"
              disabled={!market.canAddToEntry || incompatible}
              onClick={() => onAdd?.(market)}
              sx={entryButtonSx(selected, incompatible)}
            >
              {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
            </Button>
            <Button startIcon={<CompareArrowsRoundedIcon />} variant="outlined" onClick={handleCompare} sx={compareButtonSx}>
              Compare
            </Button>
          </Stack>
        )}
      </Stack>
    </AppCard>
  );
}

function Metric({ label, value, compact = false }: { label: string; value: string; compact?: boolean }) {
  return (
    <Stack minWidth={compact ? 68 : 92} spacing={0.2}>
      <Typography variant="caption" color="text.secondary" fontWeight={800}>
        {label}
      </Typography>
      <Typography variant="body2" fontWeight={900}>
        {value}
      </Typography>
    </Stack>
  );
}

function resolveConfidence(market: MarketOpportunity): 'Strong' | 'Medium' | 'Weak' {
  const warningText = market.warnings.join(' ').toLowerCase();
  const hasMajorCaution =
    market.estimatedEv <= 0 ||
    market.freshness === 'Unavailable' ||
    warningText.includes('low sample') ||
    warningText.includes('limited') ||
    warningText.includes('non-positive');

  if (hasMajorCaution || market.historicalSampleSize < 35) {
    return 'Weak';
  }

  const hasModerateCaution = market.warnings.length > 0 || market.projectionModifier !== 'Normal' || market.historicalSampleSize < 90;

  if (market.estimatedEv > 0.1 && market.historicalSampleSize >= 90 && !hasModerateCaution) {
    return 'Strong';
  }

  return market.estimatedEv > 0 ? 'Medium' : 'Weak';
}

function cleanWarning(warning: string) {
  const replacements: Record<string, string> = {
    'Provider limited for this sport': 'Limited coverage',
    'Confirm final payout in provider app': 'Payout may vary',
    'NBA sample is thin for this market': 'Limited history',
    'Low sample size': 'Low sample'
  };

  return replacements[warning] ?? warning;
}

function cleanReasons(reasons: string[]) {
  return reasons
    .filter((reason) => reason !== 'Line 1.5 profile' && reason !== 'Promo-adjusted')
    .map((reason) => {
      const replacements: Record<string, string> = {
        'Discount projection': 'Discounted line context'
      };

      return replacements[reason] ?? reason;
    });
}

export function getTopPickConfidence(market: MarketOpportunity): 'Strong' | 'Medium' | 'Weak' {
  return resolveConfidence(market);
}

export function formatTopPickWarning(market: MarketOpportunity) {
  return market.warnings.map(cleanWarning).join(' - ') || 'None';
}

const confidenceTone = {
  Strong: 'success.main',
  Medium: 'warning.main',
  Weak: 'error.main'
} as const;

const actionButtonSx = {
  ...compactActionButtonSx,
  width: 112,
  minWidth: 112,
  height: 34,
  fontSize: 12.5,
  whiteSpace: 'nowrap'
};

function entryButtonSx(selected: boolean, incompatible: boolean) {
  if (selected) {
    return {
      ...actionButtonSx,
      ...removeActionButtonSx
    };
  }

  if (incompatible) {
    return {
      ...actionButtonSx,
      ...lockedActionButtonSx
    };
  }

  return actionButtonSx;
}

const compareButtonSx = {
  ...secondaryGhostButtonSx,
  width: 112,
  minWidth: 112,
  height: 34,
  fontSize: 12.5,
  fontWeight: 850,
  whiteSpace: 'nowrap'
};
