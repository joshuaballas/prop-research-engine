import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Box, Button, Grid, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { useState } from 'react';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { getMarketCautions, isPlayableMarket } from '../../utils/marketCautions';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { ProviderLabel } from '../providers/ProviderLabel';
import { AppCard } from '../ui/AppCard';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { removeActionButtonSx } from '../ui/buttonStyles';
import { AppMobileCardActions, mobileCardActionButtonSx, mobileCardDetailsButtonSx } from '../ui/AppRowActions';
import { marketTagLabel } from '../../utils/marketTags';
import { CautionLabels } from './CautionLabels';

export function MarketCard({ market }: { market: MarketOpportunity }) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const builder = useEntryBuilder();
  const addable = isPlayableMarket(market);
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const canAddFromScanner = addable && !incompatible;
  const cautions = getMarketCautions(market, { locked: incompatible && addable });
  return (
    <AppCard sx={{ minHeight: 292, display: 'flex' }} contentSx={{ p: 1.75, flex: 1, display: 'flex' }}>
      <Stack spacing={1.2} sx={{ flex: 1, minHeight: 0 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
          <Stack spacing={0.2} minWidth={0}>
            <Typography fontWeight={950} noWrap>{market.player}</Typography>
            <Typography color="text.secondary" variant="body2" sx={{ lineHeight: 1.35 }}>
              {market.side} {market.propType} {market.line}
            </Typography>
          </Stack>
          <Stack spacing={0.1} alignItems="flex-end" flexShrink={0}>
            <Typography variant="caption" color="text.secondary" fontWeight={850} lineHeight={1}>
              EV
            </Typography>
            <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950} whiteSpace="nowrap" lineHeight={1.1}>
              {formatPercent(market.estimatedEv, { signed: true })}
            </Typography>
          </Stack>
        </Stack>

        <Typography
          color="text.secondary"
          variant="body2"
          title={`${market.sport} - ${market.event} - ${market.startTime}`}
          sx={{
            lineHeight: 1.35,
            minHeight: 38,
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden'
          }}
        >
          {market.sport} - {market.event} - {market.startTime}
        </Typography>

        <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1} flexWrap="wrap" useFlexGap sx={{ minHeight: 28 }}>
          {market.providerCategory === 'Sportsbook' ? (
            <Stack spacing={0}>
              <Typography fontWeight={900}>Reference only</Typography>
              <Typography variant="caption" color="text.secondary">
                Sportsbook line
              </Typography>
            </Stack>
          ) : (
            <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
          )}
          <Stack direction="row" gap={0.6} flexWrap="wrap" useFlexGap justifyContent="flex-end">
            <StatusPill label={marketTagLabel(market.strategyClassification)} />
            {market.projectionModifier === 'Unknown Promo' ? null : <ProjectionModifierBadge modifier={market.projectionModifier} />}
          </Stack>
        </Stack>

        <Box sx={{ borderTop: '1px solid rgba(148,163,184,.14)', pt: 1 }}>
          <Grid container spacing={1}>
            <Grid size={3}>
              <Metric label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} metric="edge" />
            </Grid>
            <Grid size={3}>
              <Metric label="Hit chance" value={formatPercent(market.modelProbability)} metric="hitChance" />
            </Grid>
            <Grid size={3}>
              <Metric label="Trust" value={<TrustValue level={market.trustLevel} sx={{ fontSize: 14, fontWeight: 950, lineHeight: 1.5 }} />} metric="trust" />
            </Grid>
            <Grid size={3}>
              <Metric label="Line" value={String(market.line)} />
            </Grid>
          </Grid>
          {market.historicalSampleSize > 0 ? (
            <Typography variant="caption" color="text.secondary" fontWeight={750} sx={{ display: 'block', mt: 0.75 }}>
              Based on {market.historicalSampleSize} results
            </Typography>
          ) : null}
        </Box>

        {cautions.length ? (
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            <CautionLabels cautions={cautions} />
          </Box>
        ) : null}

        <AppMobileCardActions>
          {canAddFromScanner || selected ? (
            <Button
              size="small"
              variant="contained"
              startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
              onClick={() => builder.toggleLeg(market)}
              sx={entryButtonSx(selected)}
            >
              {selected ? 'Remove' : 'Add leg'}
            </Button>
          ) : null}
          <Button size="small" variant="outlined" onClick={() => setDetailsOpen(true)} sx={secondaryActionButtonSx}>
            Details
          </Button>
        </AppMobileCardActions>
      </Stack>
      <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
    </AppCard>
  );
}

const baseActionButtonSx = mobileCardActionButtonSx;
const secondaryActionButtonSx = mobileCardDetailsButtonSx;

function entryButtonSx(selected: boolean) {
  if (selected) {
    return {
      ...baseActionButtonSx,
      ...removeActionButtonSx
    };
  }

  return baseActionButtonSx;
}

function Metric({ label, value, metric }: { label: string; value: ReactNode; metric?: 'ev' | 'edge' | 'hitChance' | 'trust' | 'warning' }) {
  return (
    <Stack spacing={0.2} minWidth={0}>
      {metric ? <MetricLabelWithHelp metric={metric} /> : (
        <Typography variant="caption" color="text.secondary" fontWeight={850} sx={{ lineHeight: 1.1, whiteSpace: 'nowrap' }}>
          {label}
        </Typography>
      )}
      {typeof value === 'string' ? <Typography fontWeight={950} sx={{ lineHeight: 1.45 }}>{value}</Typography> : value}
    </Stack>
  );
}
