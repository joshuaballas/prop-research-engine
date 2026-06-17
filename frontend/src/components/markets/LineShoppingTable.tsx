import AddCircleOutlineRoundedIcon from '@mui/icons-material/AddCircleOutlineRounded';
import CompareArrowsRoundedIcon from '@mui/icons-material/CompareArrowsRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import RemoveCircleOutlineRoundedIcon from '@mui/icons-material/RemoveCircleOutlineRounded';
import { Accordion, AccordionDetails, AccordionSummary, Box, Button, Divider, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { ProviderLabel } from '../providers/ProviderLabel';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';

export function LineShoppingTable({ markets }: { markets: MarketOpportunity[] }) {
  const visibleMarkets = markets.filter((market) => market.providerCategory === 'DFS');

  return (
    <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 4, bgcolor: 'rgba(8,13,26,.28)' }}>
      <Table size="small" stickyHeader sx={{ minWidth: 980 }}>
        <TableHead>
          <TableRow>
            <TableCell>Market</TableCell>
            <TableCell>Best DFS / Source</TableCell>
            <TableCell>DFS Line</TableCell>
            <TableCell>Reference Lines</TableCell>
            <TableCell align="right">EV</TableCell>
            <TableCell>Confidence</TableCell>
            <TableCell>Warning</TableCell>
            <TableCell align="center">Action</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {visibleMarkets.map((market) => (
            <LineShoppingRow key={market.id} market={market} />
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}

function LineShoppingRow({ market }: { market: MarketOpportunity }) {
  const confidence = resolveConfidence(market);
  const builder = useEntryBuilder();
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const canAdd = market.canAddToEntry && market.estimatedEv > 0 && !incompatible;

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
                  <StatusPill label={confidence} />
                </Stack>
              </TableCell>
              <TableCell sx={{ minWidth: 185, maxWidth: 230 }}>
                <Typography variant="body2" color={market.warnings.length ? 'warning.main' : 'text.secondary'} sx={{ lineHeight: 1.4 }}>
                  {market.warnings.map(cleanWarning).join(', ') || 'None'}
                </Typography>
              </TableCell>
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
                  <Button startIcon={<CompareArrowsRoundedIcon />} size="small" variant="outlined" sx={detailsButtonSx}>
                    Details
                  </Button>
                </Stack>
              </TableCell>
    </TableRow>
  );
}

export function LineShoppingCard({ market }: { market: MarketOpportunity }) {
  const dfsLines = market.providerLines?.filter((line) => line.providerCategory === 'DFS') ?? [];
  const referenceLines = market.providerLines?.filter((line) => line.providerCategory === 'Sportsbook') ?? [];
  const confidence = resolveConfidence(market);
  const builder = useEntryBuilder();
  const selected = builder.isSelected(market.id);
  const incompatible = !selected && !builder.isCompatible(market);
  const canAdd = market.canAddToEntry && market.estimatedEv > 0 && !incompatible;

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
          {market.warnings.length ? (
            <Typography variant="body2" color="warning.main">
              {cleanWarning(market.warnings[0])}
            </Typography>
          ) : null}
        </Stack>
      </AccordionSummary>
      <AccordionDetails>
        <Stack spacing={1.1}>
          <Divider />
          <DetailLine label="Best DFS line" value={`${market.provider} - ${market.side} ${market.line}`} />
          <DetailLine label="Confidence" value={confidence} tone={confidenceTone[confidence]} />
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
            <Button startIcon={<CompareArrowsRoundedIcon />} size="small" variant="outlined" sx={detailsButtonSx}>
              Details
            </Button>
          </Stack>
        </Stack>
      </AccordionDetails>
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

function DetailLine({ label, value, tone = 'text.primary' }: { label: string; value: string; tone?: string }) {
  return (
    <Stack spacing={0.15}>
      <Typography variant="caption" color="text.secondary" fontWeight={850}>
        {label}
      </Typography>
      <Typography fontWeight={900} color={tone}>{value}</Typography>
    </Stack>
  );
}

const confidenceTone = {
  Strong: 'success.main',
  Medium: 'warning.main',
  Weak: 'error.main'
} as const;

const addLegButtonSx = {
  width: 98,
  minWidth: 98,
  height: 32,
  minHeight: 32,
  px: 1,
  fontSize: 12,
  fontWeight: 900,
  color: '#06111f',
  background: 'linear-gradient(135deg, #22d3ee 0%, #818cf8 100%)',
  boxShadow: 'none',
  whiteSpace: 'nowrap',
  '& .MuiButton-startIcon': {
    mr: 0.55,
    ml: -0.15
  },
  '&:hover': {
    background: 'linear-gradient(135deg, #38e1f4 0%, #8b96ff 100%)',
    boxShadow: '0 8px 24px rgba(34,211,238,.16)'
  },
  '&.Mui-disabled': {
    color: 'rgba(203,213,225,.42)',
    background: 'rgba(148,163,184,.08)',
    border: '1px solid rgba(148,163,184,.16)',
    boxShadow: 'none'
  }
};

function entryButtonSx(selected: boolean, incompatible: boolean) {
  if (selected) {
    return {
      ...addLegButtonSx,
      color: '#FECACA',
      background: 'rgba(244,63,94,.16)',
      border: '1px solid rgba(244,63,94,.42)',
      '&:hover': {
        background: 'rgba(244,63,94,.24)',
        borderColor: 'rgba(244,63,94,.56)',
        color: '#FFE4E6'
      }
    };
  }

  if (incompatible) {
    return {
      ...addLegButtonSx,
      color: '#CBD5E1',
      background: 'rgba(51,65,85,.42)',
      border: '1px solid rgba(148,163,184,.24)',
      boxShadow: 'none',
      '&.Mui-disabled': {
        color: '#CBD5E1',
        background: 'rgba(51,65,85,.42)',
        border: '1px solid rgba(148,163,184,.24)',
        opacity: 1
      }
    };
  }

  return addLegButtonSx;
}

const detailsButtonSx = {
  width: 86,
  minWidth: 86,
  height: 32,
  minHeight: 32,
  px: 1,
  fontSize: 12,
  fontWeight: 850,
  color: 'text.secondary',
  borderColor: 'rgba(148,163,184,.22)',
  whiteSpace: 'nowrap',
  '& .MuiButton-startIcon': {
    mr: 0.55,
    ml: -0.15
  },
  '&:hover': {
    borderColor: 'rgba(148,163,184,.36)',
    bgcolor: 'rgba(148,163,184,.08)'
  }
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
