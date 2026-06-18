import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Accordion, AccordionDetails, AccordionSummary, Box, Button, Divider, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useState } from 'react';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { hasRenderableWarning, renderWarningText } from '../../utils/warnings';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { ProviderLabel } from '../providers/ProviderLabel';
import { StatusPill } from '../ui/StatusPill';
import { compactActionButtonSx, lockedActionButtonSx, removeActionButtonSx, secondaryGhostButtonSx } from '../ui/buttonStyles';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';

export function LineShoppingTable({ markets, showActions = true }: { markets: MarketOpportunity[]; showActions?: boolean }) {
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);
  const visibleMarkets = markets.filter((market) => market.providerCategory === 'DFS');

  return (
    <>
      <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 4, bgcolor: 'rgba(8,13,26,.28)' }}>
        <Table size="small" stickyHeader sx={{ minWidth: showActions ? 980 : 820 }}>
        <TableHead>
          <TableRow>
            <TableCell>Market</TableCell>
            <TableCell>Best DFS / Source</TableCell>
            <TableCell>DFS Line</TableCell>
            <TableCell>Reference Lines</TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="ev" align="right" variant="table" /></TableCell>
            <TableCell><MetricLabelWithHelp metric="trust" variant="table" /></TableCell>
            <TableCell><MetricLabelWithHelp metric="warning" variant="table" /></TableCell>
            {showActions ? <TableCell align="center">Action</TableCell> : null}
          </TableRow>
        </TableHead>
        <TableBody>
          {visibleMarkets.map((market) => (
            <LineShoppingRow key={market.id} market={market} showActions={showActions} onDetails={() => setDetailsMarket(market)} />
          ))}
        </TableBody>
      </Table>
      </Box>
      <PropDetailsModal market={detailsMarket} open={Boolean(detailsMarket)} onClose={() => setDetailsMarket(null)} />
    </>
  );
}

function LineShoppingRow({ market, showActions, onDetails }: { market: MarketOpportunity; showActions: boolean; onDetails: () => void }) {
  const confidence = resolveConfidence(market);
  const warning = renderWarningText(market.warnings);

  return (
    <TableRow
      hover
      sx={{
        '& td': { borderColor: 'rgba(148,163,184,.12)', py: 1.35 }
      }}
    >
              <TableCell sx={{ minWidth: 220 }}>
                <Stack spacing={0.45}>
                  <Typography fontWeight={900} noWrap>
                    {market.player}
                  </Typography>
                  <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.35 }}>
                    {market.side} {market.propType} {market.line} - {market.event}
                  </Typography>
                  <Stack direction="row" gap={0.6} flexWrap="wrap">
                    <StatusPill label={market.strategyClassification} />
                    <ProjectionModifierBadge modifier={market.projectionModifier} />
                  </Stack>
                </Stack>
              </TableCell>
              <TableCell sx={{ minWidth: 150 }}>
                <Stack spacing={0.35}>
                  <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
                  <Typography variant="caption" color="text.secondary">
                    {market.entryCompatibilityGroup}
                  </Typography>
                </Stack>
              </TableCell>
              <TableCell sx={{ minWidth: 112 }}>
                <Typography fontWeight={900}>{market.side} {market.line}</Typography>
              </TableCell>
              <TableCell sx={{ minWidth: 170 }}>
                <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.4 }}>
                  {market.sportsbookContext ?? 'No reference line'}
                </Typography>
              </TableCell>
              <TableCell align="right">
                <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950}>
                  {formatPercent(market.estimatedEv, { signed: true })}
                </Typography>
              </TableCell>
              <TableCell sx={{ minWidth: 145 }}>
                <Stack direction="row" gap={0.6} flexWrap="wrap">
                  <TrustValue level={confidence} variant="badge" size="small" />
                </Stack>
              </TableCell>
              <TableCell sx={{ minWidth: 185, maxWidth: 230 }}>
                <Typography variant="body2" color={warning ? 'warning.main' : 'text.secondary'} sx={{ lineHeight: 1.4 }}>
                  {warning || '-'}
                </Typography>
              </TableCell>
              {showActions ? <LineShoppingActionCell market={market} onDetails={onDetails} /> : null}
    </TableRow>
  );
}

function LineShoppingActionCell({ market, onDetails }: { market: MarketOpportunity; onDetails: () => void }) {
  const builder = useEntryBuilder();
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const canAdd = market.canAddToEntry && market.estimatedEv > 0 && !incompatible;

  return (
    <TableCell align="center" sx={{ minWidth: 175 }}>
      <Stack direction="row" spacing={0.8} justifyContent="center" flexWrap="nowrap">
        <Button
          startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
          size="small"
          variant="contained"
          disabled={!selected && !canAdd}
          onClick={() => (selected || canAdd ? builder.toggleLeg(market) : undefined)}
          sx={entryButtonSx(selected, incompatible)}
        >
          {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
        </Button>
        <Button startIcon={<InfoOutlinedIcon />} size="small" variant="outlined" onClick={onDetails} sx={detailsButtonSx}>
          Details
        </Button>
      </Stack>
    </TableCell>
  );
}

export function LineShoppingCard({ market }: { market: MarketOpportunity }) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const dfsLines = market.providerLines?.filter((line) => line.providerCategory === 'DFS') ?? [];
  const referenceLines = market.providerLines?.filter((line) => line.providerCategory === 'Sportsbook') ?? [];
  const confidence = resolveConfidence(market);
  const builder = useEntryBuilder();
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const canAdd = market.canAddToEntry && market.estimatedEv > 0 && !incompatible;
  const warning = renderWarningText(market.warnings);

  return (
    <Accordion
      disableGutters
      sx={{
        border: '1px solid rgba(148,163,184,.16)',
        borderRadius: '16px !important',
        bgcolor: 'rgba(15,23,42,.82)',
        overflow: 'hidden',
        '&:before': { display: 'none' }
      }}
    >
      <AccordionSummary expandIcon={<ExpandMoreRoundedIcon />} sx={{ px: 1.6 }}>
        <Stack spacing={0.75} width="100%">
          <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1.5}>
            <Box minWidth={0}>
              <Typography fontWeight={900} noWrap>
                {market.player}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {market.side} {market.propType} {market.line}
              </Typography>
              <Stack direction="row" gap={0.7} flexWrap="wrap" mt={0.65}>
                <StatusPill label={market.strategyClassification} />
                <ProjectionModifierBadge modifier={market.projectionModifier} />
              </Stack>
            </Box>
            <Typography color={market.estimatedEv > 0 ? 'success.main' : 'error.main'} fontWeight={950} flexShrink={0}>
              {formatPercent(market.estimatedEv, { signed: true })}
            </Typography>
          </Stack>
          <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1.2} flexWrap="wrap">
            <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
          </Stack>
          {warning ? (
            <Typography variant="body2" color="warning.main">
              {warning}
            </Typography>
          ) : null}
        </Stack>
      </AccordionSummary>
      <AccordionDetails>
        <Stack spacing={1.1}>
          <Divider />
          <DetailLine label="Best DFS line" value={`${market.provider} - ${market.side} ${market.line}`} />
          <DetailLine label="Trust" value={confidence} metric="trust" />
          <DetailLine label="Reference lines" value={market.sportsbookContext ?? 'No reference line'} />
          <ProviderLineGroup title="DFS lines" lines={dfsLines} marketId={market.id} />
          <ProviderLineGroup title="Reference lines" lines={referenceLines} marketId={market.id} emptyText="No reference-line details available." />
          <Stack direction="row" spacing={1} flexWrap="nowrap" pt={0.2}>
            <Button
              startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : incompatible ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
              size="small"
              variant="contained"
              disabled={!selected && !canAdd}
              onClick={() => (selected || canAdd ? builder.toggleLeg(market) : undefined)}
              sx={entryButtonSx(selected, incompatible)}
            >
              {selected ? 'Remove' : incompatible ? 'Locked' : 'Add leg'}
            </Button>
            <Button startIcon={<InfoOutlinedIcon />} size="small" variant="outlined" onClick={() => setDetailsOpen(true)} sx={detailsButtonSx}>
              Details
            </Button>
          </Stack>
        </Stack>
      </AccordionDetails>
      <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
    </Accordion>
  );
}

function ProviderLineGroup({
  title,
  lines,
  marketId,
  emptyText = 'No DFS provider-line details available.'
}: {
  title: string;
  lines: NonNullable<MarketOpportunity['providerLines']>;
  marketId: string;
  emptyText?: string;
}) {
  return (
    <Stack spacing={0.6}>
      <Typography variant="caption" color="text.secondary" fontWeight={850}>
        {title}
      </Typography>
      {lines.length ? (
        lines.map((line) => (
          <Box key={`${marketId}-${title}-${line.provider}`} display="flex" justifyContent="space-between" alignItems="center" gap={1} py={0.35}>
            <ProviderLabel provider={line.provider} category={line.providerCategory} compact />
            <Typography color="text.secondary" variant="body2">
              Line {line.line}
            </Typography>
          </Box>
        ))
      ) : (
        <Typography color="text.secondary" variant="body2">
          {emptyText}
        </Typography>
      )}
    </Stack>
  );
}

function DetailLine({ label, value, tone = 'text.primary', metric }: { label: string; value: string; tone?: string; metric?: 'ev' | 'edge' | 'hitChance' | 'trust' | 'warning' }) {
  return (
    <Stack spacing={0.15}>
      {metric ? <MetricLabelWithHelp metric={metric} /> : (
        <Typography variant="caption" color="text.secondary" fontWeight={850}>
          {label}
        </Typography>
      )}
      {metric === 'trust' ? <TrustValue level={value} /> : <Typography fontWeight={900} color={tone}>{value}</Typography>}
    </Stack>
  );
}

const addLegButtonSx = {
  ...compactActionButtonSx,
  width: 98,
  minWidth: 98,
  height: 32,
  minHeight: 32,
  px: 1,
  fontSize: 12,
  whiteSpace: 'nowrap'
};

function entryButtonSx(selected: boolean, incompatible: boolean) {
  if (selected) {
    return {
      ...addLegButtonSx,
      ...removeActionButtonSx
    };
  }

  if (incompatible) {
    return {
      ...addLegButtonSx,
      ...lockedActionButtonSx
    };
  }

  return addLegButtonSx;
}

const detailsButtonSx = {
  ...secondaryGhostButtonSx,
  width: 98,
  minWidth: 98,
  height: 32,
  minHeight: 32,
  px: 1,
  fontSize: 12,
  fontWeight: 850,
  whiteSpace: 'nowrap'
};

function cleanWarning(warning: string) {
  const replacements: Record<string, string> = {
    'Sportsbook context rows cannot be added': 'Reference lines are not entry legs',
    'Provider limited for this sport': 'Limited coverage for this sport',
    'Confirm final payout in provider app': 'Payout may vary'
  };

  return replacements[warning] ?? warning;
}

function resolveConfidence(market: MarketOpportunity): 'Strong' | 'Medium' | 'Weak' {
  const warningText = renderWarningText(market.warnings).toLowerCase();
  const hasMajorCaution =
    market.estimatedEv <= 0 ||
    market.freshness === 'Unavailable' ||
    warningText.includes('low sample') ||
    warningText.includes('limited') ||
    warningText.includes('non-positive');

  if (hasMajorCaution || market.historicalSampleSize < 35) {
    return 'Weak';
  }

  const hasModerateCaution = hasRenderableWarning(market.warnings) || market.projectionModifier !== 'Normal' || market.historicalSampleSize < 90;

  if (market.estimatedEv > 0.1 && market.historicalSampleSize >= 90 && !hasModerateCaution) {
    return 'Strong';
  }

  return market.estimatedEv > 0 ? 'Medium' : 'Weak';
}
