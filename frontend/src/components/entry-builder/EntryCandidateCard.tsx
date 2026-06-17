import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import CompareArrowsRoundedIcon from '@mui/icons-material/CompareArrowsRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Box, Button, Stack, Typography } from '@mui/material';
import type { MarketOpportunity } from '../../types/markets';
import { palette } from '../../theme/palette';
import { AppCard } from '../ui/AppCard';
import { ProjectionModifierBadge } from '../markets/ProjectionModifierBadge';
import { ProviderLabel } from '../providers/ProviderLabel';
import { StatusPill } from '../ui/StatusPill';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../ui/buttonStyles';

interface EntryCandidateCardProps {
  market: MarketOpportunity;
  lockedProvider?: string;
  selected?: boolean;
  onAdd: (market: MarketOpportunity) => void;
}

export function EntryCandidateCard({ market, lockedProvider, selected = false, onAdd }: EntryCandidateCardProps) {
  const incompatible = Boolean(lockedProvider && lockedProvider !== market.entryProvider);
  const warning = normalizeWarning(market.warnings[0]);

  return (
    <AppCard
      contentSx={{
        p: 1.75,
        pb: '76px',
        '&:last-child': { pb: '76px' },
        flex: 1,
        display: 'flex',
        flexDirection: 'column'
      }}
      sx={{
        position: 'relative',
        width: '100%',
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        borderColor: selected ? 'rgba(34,211,238,.42)' : incompatible ? 'rgba(245,158,11,.28)' : palette.border
      }}
    >
      <Stack spacing={1.35} sx={{ flex: 1, minHeight: 0 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1.25}>
          <Box sx={{ minWidth: 0 }}>
            <Stack direction="row" gap={0.65} rowGap={0.55} alignItems="center" flexWrap="wrap" sx={{ mb: 0.45 }}>
              <Typography variant="caption" color="primary.main" fontWeight={900}>
                {market.sport}
              </Typography>
              {market.strategyClassification === 'Candidate' ? <StatusPill label="Top Pick" sx={{ height: 22 }} /> : <StatusPill label={market.strategyClassification} sx={{ height: 22 }} />}
              <ProjectionModifierBadge modifier={market.projectionModifier} sx={{ height: 22 }} />
            </Stack>
            <Typography variant="h6" fontWeight={950} sx={{ lineHeight: 1.08 }}>
              {market.player}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.35 }}>
              {market.event} - {market.startTime}
            </Typography>
          </Box>
          <Stack alignItems="flex-end" spacing={0.25} sx={{ flexShrink: 0 }}>
            <Typography variant="caption" color="text.secondary" fontWeight={900}>
              EV
            </Typography>
            <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
              {formatSignedPercent(market.estimatedEv)}
            </Typography>
          </Stack>
        </Stack>

        <Typography fontWeight={950}>
          {market.side} {market.propType} {market.line}
        </Typography>

        <Stack direction="row" alignItems="center" justifyContent="space-between" gap={1} flexWrap="wrap">
          <ProviderLabel provider={market.entryProvider} category={market.providerCategory} compact />
          <Typography
            variant="body2"
            fontWeight={900}
            color={market.trustLevel === 'Strong' ? 'success.main' : market.trustLevel === 'Medium' ? 'warning.main' : 'error.main'}
          >
            {market.trustLevel} confidence
          </Typography>
        </Stack>

        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(4, minmax(0, 1fr))' },
            gap: 1,
            borderTop: `1px solid ${palette.border}`,
            pt: 1.15
          }}
        >
          <Metric label="Edge" value={formatSignedPercent(market.probabilityEdge)} />
          <Metric label="Model" value={formatPercent(market.modelProbability)} />
          <Metric label="Sample" value={String(market.historicalSampleSize)} />
          <Metric label="Line" value={String(market.line)} />
        </Box>

        <Stack spacing={0.5} sx={{ minHeight: 42 }}>
          <Typography variant="body2" color="text.secondary">
            {normalizeReason(market.reasons[0])}
          </Typography>
          {warning ? (
            <Typography variant="body2" color="warning.main" fontWeight={800}>
              {warning}
            </Typography>
          ) : null}
          {incompatible ? (
            <Typography variant="body2" color="warning.main" fontWeight={800}>
              Locked to {lockedProvider}
            </Typography>
          ) : null}
        </Stack>

        <Stack
          direction="row"
          spacing={1}
          sx={{
            position: 'absolute',
            left: 14,
            right: 14,
            bottom: 14
          }}
        >
          <Button
            variant="contained"
            size="small"
            startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
            onClick={() => onAdd(market)}
            disabled={incompatible}
            sx={{
              ...compactActionButtonSx,
              minWidth: 106,
              ...(selected
                ? {
                    ...removeActionButtonSx
                  }
                : incompatible
                  ? {
                      ...lockedActionButtonSx
                    }
                  : undefined)
            }}
          >
            {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
          </Button>
          <Button
            variant="outlined"
            size="small"
            startIcon={<CompareArrowsRoundedIcon />}
            sx={{ ...secondaryGhostButtonSx, minWidth: 106 }}
          >
            Compare
          </Button>
        </Stack>
      </Stack>
    </AppCard>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary" fontWeight={900}>
        {label}
      </Typography>
      <Typography variant="body2" fontWeight={950} noWrap>
        {value}
      </Typography>
    </Box>
  );
}

function formatPercent(value: number) {
  return `${(value * 100).toFixed(2)}%`;
}

function formatSignedPercent(value: number) {
  const formatted = `${Math.abs(value * 100).toFixed(2)}%`;
  return value >= 0 ? `+${formatted}` : `-${formatted}`;
}

function normalizeReason(value?: string) {
  if (!value) return 'Positive model edge';
  return value.replace('Line 1.5 profile', 'Line shape matches strategy').replace('Discount projection', 'Discounted line context');
}

function normalizeWarning(value?: string) {
  if (!value) return '';
  return value
    .replace('Confirm final payout in provider app', 'Payout may vary')
    .replace('Sportsbook context rows cannot be added', 'Reference lines are not entry legs')
    .replace('Provider limited for this sport', 'Limited coverage for this sport');
}
