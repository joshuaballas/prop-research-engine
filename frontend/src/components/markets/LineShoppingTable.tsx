import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import { Accordion, AccordionDetails, AccordionSummary, Box, Button, Collapse, Divider, IconButton, Stack, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useState } from 'react';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity, PlayerHistoryStatSummary, ProviderLine } from '../../types/markets';
import { formatDecimal, formatPercent } from '../../utils/formatters';
import { entryBuilderUnavailableReason, isEntryBuilderAddableMarket } from '../../utils/marketCautions';
import { marketDisplayLabel } from '../../utils/marketDisplay';
import { hasRenderableWarning, renderOpportunityCautionText, renderWarningText } from '../../utils/warnings';
import { palette } from '../../theme/palette';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { PropDetailsModal } from '../prop-history/PropDetailsModal';
import { ProviderLabel } from '../providers/ProviderLabel';
import { appTableRowSx, stickyActionCellSx, stickyActionHeaderCellSx } from '../ui/appTableStyles';
import { lockedActionButtonSx, removeActionButtonSx } from '../ui/buttonStyles';
import { AppDataTable } from '../ui/AppDataTable';
import {
  AppMobileCardActions,
  AppRowActions,
  desktopRowActionButtonSx,
  desktopRowDetailsButtonSx,
  mobileCardActionButtonSx,
  mobileCardDetailsButtonSx
} from '../ui/AppRowActions';
import { AppTableMetricValue, AppTablePrimaryText, AppTableSecondaryText } from '../ui/AppTableTypography';
import { OpportunityMetricGrid, OpportunityMetricItem, OpportunityPickText } from '../ui/OpportunityCard';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';

export interface LineShoppingGroup {
  key: string;
  bestMarket: MarketOpportunity;
  markets: MarketOpportunity[];
  providerLines: ProviderLine[];
  referenceLines: ProviderLine[];
}

const lineShoppingMobileCardShellSx = {
  border: '1px solid rgba(148,163,184,.16)',
  borderRadius: '16px',
  bgcolor: palette.cardSurface,
  p: 1.6
};

export function LineShoppingTable({ markets, showActions = true }: { markets: MarketOpportunity[]; showActions?: boolean }) {
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);
  const visibleMarkets = markets.filter((market) => market.providerCategory === 'DFS' || market.providerCategory === 'Sportsbook');
  const showReferenceLines = visibleMarkets.some(hasReferenceLine);
  const minWidth = (showActions ? 800 : 680) + 95 - (showReferenceLines ? 0 : 170);

  return (
    <>
      {!showReferenceLines ? (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.4 }}>
          Reference lines unavailable. Showing best provider lines only.
        </Typography>
      ) : null}
      <AppDataTable minWidth={minWidth}>
        <TableHead>
          <TableRow>
            <TableCell>Opportunity</TableCell>
            <TableCell>Provider</TableCell>
            {showReferenceLines ? <TableCell>Reference Lines</TableCell> : null}
            <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="hitChance" align="right" variant="table" /></TableCell>
            <TableCell><MetricLabelWithHelp metric="trust" variant="table" /></TableCell>
            {showActions ? <TableCell align="center" sx={stickyActionHeaderCellSx}>Action</TableCell> : null}
          </TableRow>
        </TableHead>
        <TableBody>
          {visibleMarkets.map((market) => (
            <LineShoppingRow key={market.id} market={market} showActions={showActions} showReferenceLines={showReferenceLines} onDetails={() => setDetailsMarket(market)} />
          ))}
        </TableBody>
      </AppDataTable>
      <PropDetailsModal market={detailsMarket} open={Boolean(detailsMarket)} onClose={() => setDetailsMarket(null)} />
    </>
  );
}

function LineShoppingRow({
  market,
  showActions,
  showReferenceLines,
  onDetails
}: {
  market: MarketOpportunity;
  showActions: boolean;
  showReferenceLines: boolean;
  onDetails: () => void;
}) {
  const confidence = resolveConfidence(market);

  return (
    <TableRow
      hover
      sx={appTableRowSx}
    >
              <TableCell sx={{ minWidth: 220 }}>
                <Stack spacing={0.45}>
                  <AppTablePrimaryText>{market.player}</AppTablePrimaryText>
                  <OpportunityPickText
                    side={market.side}
                    propType={market.propType}
                    line={market.line}
                    sport={marketDisplayLabel(market)}
                    sx={{
                      fontSize: 13,
                      flexWrap: 'nowrap',
                      overflow: 'hidden',
                      '& .MuiTypography-root': { fontSize: 'inherit' }
                    }}
                  />
                  <AppTableSecondaryText>
                    {market.event}
                  </AppTableSecondaryText>
                </Stack>
              </TableCell>
              <TableCell sx={{ minWidth: 150 }}>
                <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
              </TableCell>
              {showReferenceLines ? (
                <TableCell sx={{ minWidth: 170 }}>
                  <AppTableSecondaryText noWrap={false} sx={{ lineHeight: 1.4 }}>
                    {market.sportsbookContext ?? 'No reference line'}
                  </AppTableSecondaryText>
                </TableCell>
              ) : null}
              <TableCell align="right">
                <AppTableMetricValue>{formatPercent(market.probabilityEdge, { signed: true })}</AppTableMetricValue>
              </TableCell>
              <TableCell align="right">
                <AppTableMetricValue>{formatPercent(market.modelProbability)}</AppTableMetricValue>
              </TableCell>
              <TableCell sx={{ minWidth: 145 }}>
                <Stack direction="row" gap={0.6} flexWrap="wrap">
                  <TrustValue level={confidence} />
                </Stack>
              </TableCell>
              {showActions ? <LineShoppingActionCell market={market} onDetails={onDetails} /> : null}
    </TableRow>
  );
}

function LineShoppingActionCell({ market, onDetails }: { market: MarketOpportunity; onDetails: () => void }) {
  const builder = useEntryBuilder();
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const locked = !selected && (!isEntryBuilderAddableMarket(market) || incompatible);

  return (
    <TableCell data-sticky-action="true" align="center" sx={{ ...stickyActionCellSx, minWidth: 130 }}>
      <AppRowActions sx={{ mx: 'auto' }}>
        <Button
          startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : locked ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
          size="small"
          variant="contained"
          disabled={locked}
          onClick={() => (locked ? undefined : builder.toggleLeg(market))}
          title={locked ? incompatible ? 'This entry is locked to another provider.' : entryBuilderUnavailableReason(market) : undefined}
          sx={entryButtonSx(selected, locked)}
        >
          {selected ? 'Remove' : 'Add leg'}
        </Button>
        <Button size="small" variant="outlined" onClick={onDetails} sx={detailsButtonSx}>
          Details
        </Button>
      </AppRowActions>
    </TableCell>
  );
}

export function LineShoppingCard({ market }: { market: MarketOpportunity }) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const confidence = resolveConfidence(market);
  const providerLines = orderProviderLines((market.providerLines?.filter((line) => line.providerCategory === market.providerCategory) ?? []).map((line) => ({
    ...line,
    note: `${market.side} ${line.line}`
  })), market.provider);
  const referenceLines = market.providerCategory === 'DFS'
    ? market.providerLines?.filter((line) => line.providerCategory === 'Sportsbook') ?? []
    : [];
  const canExpand = providerLines.length > 1 || referenceLines.length > 0 || hasAvailableSoccerHistory(market);
  const builder = useEntryBuilder();
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const locked = !selected && (!isEntryBuilderAddableMarket(market) || incompatible);
  const warning = renderOpportunityCautionText(market.warnings);
  const actions = (
    <>
      <Button
        startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : locked ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
        size="small"
        variant="contained"
        disabled={locked}
        onClick={() => (locked ? undefined : builder.toggleLeg(market))}
        title={locked ? incompatible ? 'This entry is locked to another provider.' : entryBuilderUnavailableReason(market) : undefined}
        sx={mobileEntryButtonSxFor(selected, locked)}
      >
        {selected ? 'Remove' : 'Add leg'}
      </Button>
      <Button size="small" variant="outlined" onClick={() => setDetailsOpen(true)} sx={mobileDetailsButtonSx}>
        Details
      </Button>
    </>
  );
  const summaryContent = (
    <Stack spacing={0.75} width="100%" minWidth={0}>
      <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1.5}>
        <Box minWidth={0}>
          <Typography fontWeight={950} noWrap>
            {market.player}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap component="div">
            {market.event}
          </Typography>
        </Box>
        <Stack spacing={0.1} alignItems="flex-end" flexShrink={0}>
          <Typography variant="caption" color="text.secondary" fontWeight={850} lineHeight={1}>
            Edge
          </Typography>
          <Typography color={(market.probabilityEdge ?? 0) > 0 ? 'success.main' : 'error.main'} fontWeight={950} lineHeight={1.1}>
            {formatPercent(market.probabilityEdge, { signed: true })}
          </Typography>
        </Stack>
      </Stack>
      <OpportunityPickText side={market.side} propType={market.propType} line={market.line} />
      <LineShoppingPlayerHistoryContext market={market} compact />
      <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1.2} flexWrap="wrap">
        <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
        <Stack direction="row" gap={0.6} flexWrap="wrap" useFlexGap justifyContent="flex-end">
          <ProjectionModifierBadge modifier={market.projectionModifier} />
        </Stack>
      </Stack>
      {warning ? (
        <Typography variant="body2" color="warning.main">
          {warning}
        </Typography>
      ) : null}
    </Stack>
  );

  if (!canExpand) {
    return (
      <>
        <Box
          sx={lineShoppingMobileCardShellSx}
        >
          <Stack spacing={1.25}>
            {summaryContent}
            <AppMobileCardActions sx={{ mt: 1.6, pt: 0.2 }}>
              {actions}
            </AppMobileCardActions>
          </Stack>
        </Box>
        <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
      </>
    );
  }

  return (
    <>
      <Accordion
        disableGutters
        sx={{
          ...lineShoppingMobileCardShellSx,
          boxShadow: 'none',
          backgroundImage: 'none',
          overflow: 'hidden',
          '&:before': { display: 'none' },
          '&.Mui-expanded': {
            m: 0,
            bgcolor: palette.cardSurface
          }
        }}
      >
        <AccordionSummary
          expandIcon={<ExpandMoreRoundedIcon />}
          sx={{
            p: 0,
            minHeight: 'auto',
            '&.Mui-expanded': { minHeight: 'auto' },
            '& .MuiAccordionSummary-content': { my: 0 },
            '& .MuiAccordionSummary-content.Mui-expanded': { my: 0 }
          }}
        >
          {summaryContent}
        </AccordionSummary>
        <AccordionDetails sx={{ p: 0, pt: 1.25 }}>
          <Stack spacing={1.1}>
            <Divider />
            <OpportunityMetricGrid columns={{ xs: 2 }} sx={{ borderTop: 0, pt: 0 }}>
              <OpportunityMetricItem label="Edge" value={formatPercent(market.probabilityEdge, { signed: true })} metric="edge" />
              <OpportunityMetricItem label="Hit chance" value={formatPercent(market.modelProbability)} metric="hitChance" />
              <OpportunityMetricItem label="Trust" value={<TrustValue level={confidence} sx={{ fontSize: 14, fontWeight: 950, lineHeight: 1.5 }} />} metric="trust" />
            </OpportunityMetricGrid>
            <ProviderLineGroup title="Provider lines" lines={providerLines} marketId={market.id} bestProvider={market.provider} />
            {referenceLines.length ? <ProviderLineGroup title="Reference lines" lines={referenceLines} marketId={market.id} /> : null}
            <AppMobileCardActions sx={{ mt: 2.45, pt: 0.2 }}>
              {actions}
            </AppMobileCardActions>
          </Stack>
        </AccordionDetails>
      </Accordion>
      <PropDetailsModal market={market} open={detailsOpen} onClose={() => setDetailsOpen(false)} />
    </>
  );
}

function LineShoppingCardActions({ market, onDetails }: { market: MarketOpportunity; onDetails: () => void }) {
  const builder = useEntryBuilder();
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const locked = !selected && (!isEntryBuilderAddableMarket(market) || incompatible);

  return (
    <AppRowActions sx={{ mx: 'auto' }}>
      <Button
        startIcon={selected ? <RemoveCircleOutlineRoundedIcon /> : locked ? <LockRoundedIcon /> : <AddCircleOutlineRoundedIcon />}
        size="small"
        variant="contained"
        disabled={locked}
        onClick={() => (locked ? undefined : builder.toggleLeg(market))}
        title={locked ? incompatible ? 'This entry is locked to another provider.' : entryBuilderUnavailableReason(market) : undefined}
        sx={entryButtonSx(selected, locked)}
      >
        {selected ? 'Remove' : 'Add leg'}
      </Button>
      <Button size="small" variant="outlined" onClick={onDetails} sx={detailsButtonSx}>
        Details
      </Button>
    </AppRowActions>
  );
}

export function LineShoppingComparisonList({ groups }: { groups: LineShoppingGroup[] }) {
  const [expandedKeys, setExpandedKeys] = useState<Record<string, boolean>>({});
  const [detailsMarket, setDetailsMarket] = useState<MarketOpportunity | null>(null);
  return (
    <>
      <AppDataTable minWidth={960}>
        <TableHead>
          <TableRow>
            <TableCell>Opportunity</TableCell>
            <TableCell>Best Provider</TableCell>
            <TableCell>Best Pick</TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="edge" align="right" variant="table" /></TableCell>
            <TableCell align="right"><MetricLabelWithHelp metric="hitChance" align="right" variant="table" /></TableCell>
            <TableCell><MetricLabelWithHelp metric="trust" variant="table" /></TableCell>
            <TableCell align="center" aria-label="Expand details" />
            <TableCell align="center" sx={stickyActionHeaderCellSx}>Action</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {groups.map((group) => (
            <LineShoppingComparisonRow
              key={group.key}
              group={group}
              expanded={Boolean(expandedKeys[group.key])}
              onToggle={() => setExpandedKeys((current) => ({ ...current, [group.key]: !current[group.key] }))}
              onDetails={() => setDetailsMarket(group.bestMarket)}
            />
          ))}
        </TableBody>
      </AppDataTable>
      <PropDetailsModal market={detailsMarket} open={Boolean(detailsMarket)} onClose={() => setDetailsMarket(null)} />
    </>
  );
}

function LineShoppingComparisonRow({
  group,
  expanded,
  onToggle,
  onDetails
}: {
  group: LineShoppingGroup;
  expanded: boolean;
  onToggle: () => void;
  onDetails: () => void;
}) {
  const market = group.bestMarket;
  const confidence = resolveConfidence(market);
  const canExpand = group.providerLines.length > 1 || group.referenceLines.length > 0 || hasAvailableSoccerHistory(market);
  const expandedVisible = canExpand && expanded;

  return (
    <>
      <TableRow hover sx={appTableRowSx}>
        <TableCell sx={{ minWidth: 260 }}>
          <Stack spacing={0.45}>
            <AppTablePrimaryText>{market.player}</AppTablePrimaryText>
            <OpportunityPickText
              side={market.side}
              propType={market.propType}
              line={market.line}
              sport={marketDisplayLabel(market)}
              sx={{
                fontSize: 13,
                flexWrap: 'nowrap',
                overflow: 'hidden',
                '& .MuiTypography-root': { fontSize: 'inherit' }
              }}
            />
            <AppTableSecondaryText>{market.event}</AppTableSecondaryText>
          </Stack>
        </TableCell>
        <TableCell sx={{ minWidth: 150 }}>
          <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
        </TableCell>
        <TableCell sx={{ minWidth: 108 }}>
          <AppTableMetricValue sx={{ whiteSpace: 'nowrap' }}>
            {market.side} {market.line}
          </AppTableMetricValue>
        </TableCell>
        <TableCell align="right">
          <AppTableMetricValue>{formatPercent(market.probabilityEdge, { signed: true })}</AppTableMetricValue>
        </TableCell>
        <TableCell align="right">
          <AppTableMetricValue>{formatPercent(market.modelProbability)}</AppTableMetricValue>
        </TableCell>
        <TableCell sx={{ minWidth: 125 }}>
          <TrustValue level={confidence} />
        </TableCell>
        <TableCell align="center" sx={{ width: 48 }}>
          {canExpand ? (
            <IconButton
              size="small"
              aria-label={expandedVisible ? 'Collapse provider comparison' : 'Expand provider comparison'}
              aria-expanded={expandedVisible}
              onClick={onToggle}
              sx={{
                color: 'text.secondary',
                transform: expandedVisible ? 'rotate(180deg)' : 'rotate(0deg)',
                transition: 'transform .16s ease, color .16s ease',
                '&:hover': { color: 'primary.main', bgcolor: 'rgba(34,211,238,.08)' }
              }}
            >
              <ExpandMoreRoundedIcon fontSize="small" />
            </IconButton>
          ) : null}
        </TableCell>
        <TableCell data-sticky-action="true" align="center" sx={{ ...stickyActionCellSx, minWidth: 130 }}>
          <LineShoppingCardActions market={market} onDetails={onDetails} />
        </TableCell>
      </TableRow>
      {canExpand ? (
        <TableRow>
          <TableCell colSpan={8} sx={{ p: 0, borderBottom: '1px solid rgba(148,163,184,.14)' }}>
            <Collapse in={expandedVisible} timeout="auto" unmountOnExit>
              <Box
                sx={{
                  bgcolor: 'rgba(8,13,25,.58)',
                  borderTop: '1px solid rgba(148,163,184,.1)',
                  px: 2.6,
                  py: 1.25,
                  pl: 4
                }}
              >
                <Stack spacing={1.2}>
                  <ComparisonLineGroups providerLines={group.providerLines} referenceLines={group.referenceLines} marketId={group.key} bestProvider={market.provider} />
                  <LineShoppingPlayerHistoryContext market={market} />
                </Stack>
              </Box>
            </Collapse>
          </TableCell>
        </TableRow>
      ) : null}
    </>
  );
}

function ComparisonLineGroups({
  providerLines,
  referenceLines,
  marketId,
  bestProvider
}: {
  providerLines: NonNullable<MarketOpportunity['providerLines']>;
  referenceLines: NonNullable<MarketOpportunity['providerLines']>;
  marketId: string;
  bestProvider: string;
}) {
  const showReferenceLines = referenceLines.length > 0;
  const rowCount = Math.max(providerLines.length, showReferenceLines ? referenceLines.length : 0);

  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: showReferenceLines ? { xs: 'minmax(0, 1fr)', md: '320px 320px' } : { xs: 'minmax(0, 1fr)', md: '320px' },
        columnGap: { xs: 0, md: 8 },
        rowGap: 0.45,
        alignItems: 'start',
        width: 'fit-content',
        maxWidth: '100%'
      }}
    >
      <Typography variant="caption" color="text.secondary" fontWeight={850}>
        Provider lines
      </Typography>
      {showReferenceLines ? (
        <Typography variant="caption" color="text.secondary" fontWeight={850} sx={{ display: { xs: 'none', md: 'block' } }}>
          Reference lines
        </Typography>
      ) : null}
      {Array.from({ length: rowCount }).map((_, index) => (
        <Box key={`${marketId}-comparison-${index}`} sx={{ display: 'contents' }}>
          <LineComparisonCell line={providerLines[index]} fallbackText={index === 0 ? 'No provider-line details available.' : undefined} isBest={providerLines[index]?.provider === bestProvider} />
          {showReferenceLines ? <LineComparisonCell line={referenceLines[index]} sx={{ display: { xs: 'none', md: 'grid' } }} /> : null}
        </Box>
      ))}
      {showReferenceLines ? (
        <Box sx={{ display: { xs: 'block', md: 'none' }, mt: 1.2 }}>
          <ProviderLineGroup title="Reference lines" lines={referenceLines} marketId={marketId} />
        </Box>
      ) : null}
    </Box>
  );
}

function LineComparisonCell({
  line,
  fallbackText,
  isBest = false,
  sx
}: {
  line?: ProviderLine;
  fallbackText?: string;
  isBest?: boolean;
  sx?: Record<string, unknown>;
}) {
  if (!line) {
    return (
      <Box
        sx={{
          minHeight: 28,
          display: 'grid',
          alignItems: 'center',
          ...(sx ?? {})
        }}
      >
        {fallbackText ? (
          <Typography color="text.secondary" variant="body2">
            {fallbackText}
          </Typography>
        ) : null}
      </Box>
    );
  }

  return (
    <Box
      display="grid"
      gridTemplateColumns="minmax(0, 1fr) auto"
      alignItems="center"
      columnGap={1.4}
      minHeight={28}
      sx={sx}
    >
      <Stack direction="row" alignItems="center" gap={0.65} minWidth={0}>
        <ProviderLabel provider={line.provider} category={line.providerCategory} compact />
        {isBest ? <BestLineLabel /> : null}
      </Stack>
      <Typography color="text.secondary" variant="body2" fontWeight={850} lineHeight={1.2} sx={{ whiteSpace: 'nowrap' }}>
        {line.note ?? `Line ${line.line}`}
      </Typography>
    </Box>
  );
}

function ProviderLineGroup({
  title,
  lines,
  marketId,
  bestProvider,
  emptyText = 'No provider-line details available.'
}: {
  title: string;
  lines: NonNullable<MarketOpportunity['providerLines']>;
  marketId: string;
  bestProvider?: string;
  emptyText?: string;
}) {
  return (
    <Stack spacing={0.6}>
      <Typography variant="caption" color="text.secondary" fontWeight={850}>
        {title}
      </Typography>
      {lines.length ? (
        lines.map((line) => (
          <Box
            key={`${marketId}-${title}-${line.provider}`}
            display="grid"
            gridTemplateColumns="minmax(0, 1fr) auto"
            alignItems="center"
            columnGap={1.4}
            minHeight={28}
          >
            <Stack direction="row" alignItems="center" gap={0.65} minWidth={0}>
              <ProviderLabel provider={line.provider} category={line.providerCategory} compact />
              {line.provider === bestProvider ? <BestLineLabel /> : null}
            </Stack>
            <Typography color="text.secondary" variant="body2" fontWeight={850} lineHeight={1.2} sx={{ whiteSpace: 'nowrap' }}>
              {line.note ?? `Line ${line.line}`}
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

function BestLineLabel() {
  return (
    <Typography variant="caption" color="text.secondary" fontWeight={850} sx={{ opacity: 0.9 }}>
      Best
    </Typography>
  );
}

function LineShoppingPlayerHistoryContext({ market, compact = false }: { market: MarketOpportunity; compact?: boolean }) {
  const stat = resolveHistoryStat(market);
  const summary = market.playerHistorySummary;
  if (!summary?.available || !stat) return null;

  const sampleSize = stat.count ?? summary.sampleSize;
  const latestDate = formatHistoryDate(summary.latestResultDate);

  return (
    <Box
      sx={{
        border: '1px solid rgba(148,163,184,.14)',
        borderRadius: 2,
        bgcolor: 'rgba(15,23,42,.34)',
        px: compact ? 1 : 1.15,
        py: compact ? 0.75 : 0.9
      }}
    >
      <Stack spacing={0.7}>
        <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1.2}>
          <Typography variant="caption" color="text.secondary" fontWeight={850}>
            Player history
          </Typography>
          {latestDate ? (
            <Typography variant="caption" color="text.secondary" fontWeight={750} sx={{ whiteSpace: 'nowrap' }}>
              {latestDate}
            </Typography>
          ) : null}
        </Stack>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: compact ? 'repeat(2, minmax(0, 1fr))' : { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(4, minmax(0, auto))' },
            gap: compact ? 0.8 : 1.1,
            alignItems: 'start'
          }}
        >
          <HistoryStatValue label="Avg" value={formatHistoryAverage(stat.average)} />
          <HistoryStatValue label="Last 5" value={formatHistoryAverage(stat.last5Average)} />
          <HistoryStatValue label="Last 10" value={formatHistoryAverage(stat.last10Average)} />
          {!compact && latestDate ? <HistoryStatValue label="Latest" value={latestDate} /> : null}
        </Box>
        {sampleSize !== undefined ? (
          <Typography variant="caption" color="text.secondary" fontWeight={650}>
            Based on {sampleSize} historical games.
          </Typography>
        ) : null}
      </Stack>
    </Box>
  );
}

function HistoryStatValue({ label, value }: { label: string; value: string }) {
  return (
    <Stack spacing={0.1} minWidth={0}>
      <Typography variant="caption" color="text.secondary" fontWeight={800} lineHeight={1.15}>
        {label}
      </Typography>
      <Typography variant="body2" fontWeight={900} lineHeight={1.2} sx={{ whiteSpace: 'nowrap' }}>
        {value}
      </Typography>
    </Stack>
  );
}

function hasAvailableSoccerHistory(market: MarketOpportunity) {
  return market.sport === 'SOCCER' && market.playerHistorySummary?.available === true && Boolean(resolveHistoryStat(market));
}

function resolveHistoryStat(market: MarketOpportunity): PlayerHistoryStatSummary | null {
  if (market.sport !== 'SOCCER' || !market.playerHistorySummary?.available) return null;
  const statKey = historyStatKeyForProp(market.propType);
  return statKey ? market.playerHistorySummary.stats?.[statKey] ?? null : null;
}

function historyStatKeyForProp(propType: string) {
  const normalized = propType.replace(/[\s_-]+/g, '').toLowerCase();
  if (normalized === 'goals' || normalized === 'playergoals') return 'Goals';
  if (normalized === 'assists' || normalized === 'playerassists') return 'Assists';
  if (normalized === 'shots' || normalized === 'playershots') return 'Shots';
  if (normalized === 'shotsontarget' || normalized === 'playershotsontarget' || normalized === 'sot' || normalized === 'playersot') return 'ShotsOnTarget';
  return null;
}

function formatHistoryAverage(value?: number) {
  return typeof value === 'number' ? formatDecimal(value, 2) : '-';
}

function formatHistoryDate(value?: string | null) {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric'
  }).format(date);
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

const addLegButtonSx = desktopRowActionButtonSx;

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

const detailsButtonSx = desktopRowDetailsButtonSx;

const mobileEntryButtonSx = mobileCardActionButtonSx;
const mobileDetailsButtonSx = mobileCardDetailsButtonSx;

function mobileEntryButtonSxFor(selected: boolean, incompatible: boolean) {
  if (selected) {
    return {
      ...mobileEntryButtonSx,
      ...removeActionButtonSx
    };
  }

  if (incompatible) {
    return {
      ...mobileEntryButtonSx,
      ...lockedActionButtonSx
    };
  }

  return mobileEntryButtonSx;
}

export function groupLineShoppingMarkets(markets: MarketOpportunity[]): LineShoppingGroup[] {
  const groups = new Map<string, MarketOpportunity[]>();

  markets.forEach((market) => {
    const key = [
      market.playerId ?? market.canonicalPlayerId ?? market.player,
      market.sport,
      market.providerCategory,
      market.event,
      market.propType,
      market.side
    ].join('|');
    groups.set(key, [...(groups.get(key) ?? []), market]);
  });

  return Array.from(groups.entries()).map(([key, rows]) => {
    const sortedRows = [...rows].sort((a, b) => Number(isEntryBuilderAddableMarket(b)) - Number(isEntryBuilderAddableMarket(a)) || b.estimatedEv - a.estimatedEv);
    const bestMarket = sortedRows[0];

    return {
      key,
      bestMarket,
      markets: rows,
      providerLines: orderProviderLines(uniqueProviderLines(rows.map((market) => ({
        provider: market.provider,
        providerCategory: market.providerCategory,
        line: market.line,
        note: `${market.side} ${market.line}`
      }))), bestMarket.provider),
      referenceLines: bestMarket.providerCategory === 'DFS'
        ? uniqueProviderLines(rows.flatMap((market) => market.providerLines?.filter((line) => line.providerCategory === 'Sportsbook') ?? []))
        : []
    };
  });
}

function orderProviderLines(lines: ProviderLine[], bestProvider: string) {
  return [...lines].sort((a, b) => {
    const bestDelta = Number(b.provider === bestProvider) - Number(a.provider === bestProvider);
    return bestDelta || a.provider.localeCompare(b.provider);
  });
}

function uniqueProviderLines(lines: ProviderLine[]) {
  const unique = new Map<string, ProviderLine>();

  lines.forEach((line) => {
    const key = `${line.provider}|${line.providerCategory}|${line.line}`;
    if (!unique.has(key)) {
      unique.set(key, line);
    }
  });

  return Array.from(unique.values()).sort((a, b) => a.provider.localeCompare(b.provider));
}

function cleanWarning(warning: string) {
  const replacements: Record<string, string> = {
    'Sportsbook context rows cannot be added': 'Reference lines are not entry legs',
    'Provider limited for this sport': 'Limited coverage for this sport',
    'Confirm final payout in provider app': 'Payout may vary'
  };

  return replacements[warning] ?? warning;
}

function hasReferenceLine(market: MarketOpportunity) {
  const context = String(market.sportsbookContext ?? '').trim().toLowerCase();
  return Boolean(context && !context.includes('no sportsbook reference') && context !== 'no reference line' && context !== 'none');
}

function resolveConfidence(market: MarketOpportunity): 'Strong' | 'Medium' | 'Weak' {
  if (market.isRecommendation && market.recommendationActionableForUser && isDisplayableTrustLevel(market.trustLevel)) {
    return market.trustLevel;
  }

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

  const edge = market.probabilityEdge ?? 0;
  if (edge > 0.1 && market.historicalSampleSize >= 90 && !hasModerateCaution) {
    return 'Strong';
  }

  return edge > 0 ? 'Medium' : 'Weak';
}

function isDisplayableTrustLevel(value: MarketOpportunity['trustLevel']): value is 'Strong' | 'Medium' | 'Weak' {
  return value === 'Strong' || value === 'Medium' || value === 'Weak';
}
