import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { Box, CircularProgress, Dialog, DialogContent, DialogTitle, IconButton, Stack, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { getPropHistory } from '../../api/markets';
import type { PropHistoryResponseDto } from '../../api/types';
import { isDemoDataMode } from '../../config/dataMode';
import { mockPlayerResearchPlayers } from '../../data/mockPlayerResearch';
import { palette } from '../../theme/palette';
import { formatPercent } from '../../utils/formatters';
import { getMarketCautions } from '../../utils/marketCautions';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { ProviderLabel } from '../providers/ProviderLabel';
import { AppCard } from '../ui/AppCard';
import { CautionLabels } from '../markets/CautionLabels';
import type { MarketOpportunity } from '../../types/markets';
import { PropHistoryCard } from './PropHistoryCard';

interface PropDetailsModalProps {
  market: MarketOpportunity | null;
  open: boolean;
  onClose: () => void;
}

export function PropDetailsModal({ market, open, onClose }: PropDetailsModalProps) {
  const demoHistory = market ? findHistoryForMarket(market) : null;
  const { data: apiHistory, loading, error } = usePropHistory(market, open && !isDemoDataMode);
  const hasApiHistory = Boolean(apiHistory?.games?.length);

  return (
    <Dialog
      open={open}
      onClose={onClose}
      fullWidth
      maxWidth="lg"
      aria-labelledby="prop-details-title"
      PaperProps={{
        sx: {
          borderRadius: 2.5,
          border: '1px solid rgba(148,163,184,.18)',
          bgcolor: 'rgba(8,13,25,.98)',
          backgroundImage: 'none',
          m: { xs: '10px', sm: 4 },
          width: { xs: 'calc(100vw - 20px)', sm: 'auto' },
          maxWidth: { xs: 'calc(100vw - 20px)', sm: 1200 },
          maxHeight: { xs: 'calc(100vh - 24px)', sm: 'calc(100vh - 64px)' },
          boxShadow: '0 28px 80px rgba(0,0,0,.48)'
        }
      }}
    >
      <DialogTitle id="prop-details-title" sx={{ pr: { xs: 6, sm: 7 }, pb: { xs: 0.75, sm: 1 }, pt: { xs: 2, sm: 2.5 } }}>
        <Stack spacing={{ xs: 0.45, sm: 0.65 }}>
          <Typography variant="h4" sx={{ fontSize: { xs: 26, sm: 34 }, lineHeight: 1.05 }}>
            {market?.player ?? 'Prop details'}
          </Typography>
          {market ? (
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
              <Typography color="text.secondary" variant="body2">
                {market.side} {market.propType} {formatLineNumber(market.line)}
              </Typography>
            </Stack>
          ) : null}
        </Stack>
        <IconButton
          aria-label="Close prop details"
          onClick={onClose}
          sx={{
            position: 'absolute',
            right: { xs: 10, sm: 14 },
            top: { xs: 10, sm: 14 },
            color: 'text.secondary',
            '&:hover': { color: 'text.primary', bgcolor: 'rgba(148,163,184,.10)' }
          }}
        >
          <CloseRoundedIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent className="app-scrollbar" sx={{ pt: { xs: 0.75, sm: 1 }, pb: { xs: 2.25, sm: 3 }, px: { xs: 1.25, sm: 3 } }}>
        {market ? (
          <Stack spacing={1.5}>
            <MetricStrip market={market} />
            {isDemoDataMode && demoHistory ? (
              <AppCard contentSx={{ p: { xs: 1.25, md: 2 } }}>
                <PropHistoryCard stat={demoHistory.stat} selectedOffering={market} allLines={demoHistory.allLines} />
              </AppCard>
            ) : null}
            {!isDemoDataMode && loading ? (
              <AppCard>
                <Stack direction="row" spacing={1.25} alignItems="center">
                  <CircularProgress size={18} />
                  <Typography color="text.secondary">Loading persisted prop history...</Typography>
                </Stack>
              </AppCard>
            ) : null}
            {!isDemoDataMode && !loading && hasApiHistory && apiHistory ? (
              <AppCard contentSx={{ p: { xs: 1.25, md: 2 } }}>
                <PropHistoryCard history={apiHistory} selectedOffering={market} />
              </AppCard>
            ) : null}
            {(!isDemoDataMode && !loading && !hasApiHistory) || (isDemoDataMode && !demoHistory) ? (
              <AppCard>
                <Stack spacing={0.85}>
                  <Typography variant="h5">Prop history</Typography>
                  <Typography color="text.secondary">
                    {error
                      ? 'Prop history could not be loaded right now.'
                      : apiHistory?.emptyReason ?? 'Prop history is not available for this opportunity yet.'}
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {market.provider} - {market.side} {market.propType} {formatLineNumber(market.line)}
                  </Typography>
                </Stack>
              </AppCard>
            ) : null}
          </Stack>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

function usePropHistory(market: MarketOpportunity | null, enabled: boolean) {
  const [state, setState] = useState<{ data?: PropHistoryResponseDto; loading: boolean; error?: Error }>({ loading: false });

  useEffect(() => {
    if (!market || !enabled) {
      setState({ loading: false });
      return;
    }

    const controller = new AbortController();
    setState({ loading: true });
    const playerId = market.playerId ?? market.canonicalPlayerId;

    getPropHistory(
      {
        sport: market.sport,
        playerId,
        playerName: playerId ? undefined : market.player,
        propType: market.propType,
        side: market.side,
        line: market.line,
        provider: market.provider,
        take: 10
      },
      controller.signal
    )
      .then((data) => {
        if (!controller.signal.aborted) {
          setState({ data, loading: false });
        }
      })
      .catch((error: Error) => {
        if (!controller.signal.aborted) {
          setState({ error, loading: false });
        }
      });

    return () => controller.abort();
  }, [enabled, market?.canonicalPlayerId, market?.id, market?.line, market?.player, market?.playerId, market?.propType, market?.provider, market?.side, market?.sport]);

  return state;
}

function MetricStrip({ market }: { market: MarketOpportunity }) {
  const cautions = getMarketCautions(market);
  const hasCautions = cautions.length > 0;
  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: {
          xs: 'repeat(2, minmax(0, 1fr))',
          md: hasCautions ? 'repeat(5, minmax(0, 1fr))' : 'repeat(4, minmax(0, 1fr))'
        },
        gap: { xs: 1.05, md: 1.25 },
        px: { xs: 1.35, md: 1.6 },
        py: { xs: 1.25, md: 1.45 },
        borderRadius: 2.5,
        border: '1px solid rgba(148,163,184,.14)',
        bgcolor: palette.cardSurface
      }}
    >
      <DetailMetric metric="hitChance" value={formatPercent(market.modelProbability)} />
      <DetailMetric metric="edge" value={formatPercent(market.probabilityEdge, { signed: true })} />
      <DetailMetric metric="ev" value={formatPercent(market.estimatedEv, { signed: true })} tone={market.estimatedEv >= 0 ? 'success.main' : 'error.main'} />
      <DetailMetric metric="trust" value={market.trustLevel === 'Research' ? 'Weak' : market.trustLevel} />
      {hasCautions ? (
        <Stack spacing={0.45} minWidth={0} sx={{ gridColumn: { xs: '1 / -1', md: 'auto' } }}>
          <MetricLabelWithHelp metric="caution" />
          <Box sx={{ display: 'flex', minHeight: 26, alignItems: 'center' }}>
            <CautionLabels cautions={cautions} />
          </Box>
        </Stack>
      ) : null}
    </Box>
  );
}

function DetailMetric({
  metric,
  value,
  tone = 'text.primary'
}: {
  metric: 'ev' | 'edge' | 'hitChance' | 'trust';
  value: string;
  tone?: string;
}) {
  return (
    <Stack spacing={0.35} minWidth={0}>
      <MetricLabelWithHelp metric={metric} />
      {metric === 'trust' ? (
        <TrustValue level={value} />
      ) : (
        <Typography fontWeight={900} sx={{ color: tone, lineHeight: 1.25 }} noWrap>
          {value}
        </Typography>
      )}
    </Stack>
  );
}

function findHistoryForMarket(market: MarketOpportunity) {
  if (!isDemoDataMode) return null;

  const player = mockPlayerResearchPlayers.find((profile) => profile.name.toLowerCase() === market.player.toLowerCase());
  if (!player) return null;

  const stat = player.stats.find((item) => normalizeLabel(item.label) === normalizeLabel(market.propType));
  if (!stat) return null;

  return { stat, allLines: player.currentLines };
}

function normalizeLabel(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '');
}

function formatLineNumber(line: number) {
  return Number.isInteger(line) ? String(line) : line.toFixed(1);
}
