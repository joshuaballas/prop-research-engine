import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { Box, CircularProgress, Dialog, DialogContent, DialogTitle, IconButton, Stack, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { getPropHistory } from '../../api/markets';
import type { PropHistoryResponseDto } from '../../api/types';
import { isDemoDataMode } from '../../config/dataMode';
import { mockPlayerResearchPlayers } from '../../data/mockPlayerResearch';
import { formatPercent } from '../../utils/formatters';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { TrustValue } from '../metrics/TrustValue';
import { ProviderLabel } from '../providers/ProviderLabel';
import { AppCard } from '../ui/AppCard';
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
          boxShadow: '0 28px 80px rgba(0,0,0,.48)'
        }
      }}
    >
      <DialogTitle id="prop-details-title" sx={{ pr: 7, pb: 1 }}>
        <Stack spacing={0.65}>
          <Typography variant="h4">{market?.player ?? 'Prop details'}</Typography>
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
            right: 14,
            top: 14,
            color: 'text.secondary',
            '&:hover': { color: 'text.primary', bgcolor: 'rgba(148,163,184,.10)' }
          }}
        >
          <CloseRoundedIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent sx={{ pt: 1, pb: 3 }}>
        {market ? (
          <Stack spacing={1.5}>
            <MetricStrip market={market} />
            {isDemoDataMode && demoHistory ? (
              <AppCard contentSx={{ p: { xs: 1.5, md: 2 } }}>
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
              <AppCard contentSx={{ p: { xs: 1.5, md: 2 } }}>
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
                    {market.provider} · {market.side} {market.propType} {formatLineNumber(market.line)}
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
  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' },
        gap: 1,
        p: 1.2,
        borderRadius: 2.5,
        border: '1px solid rgba(148,163,184,.14)',
        bgcolor: 'rgba(15,23,42,.45)'
      }}
    >
      <DetailMetric metric="hitChance" value={formatPercent(market.modelProbability)} />
      <DetailMetric metric="edge" value={formatPercent(market.probabilityEdge, { signed: true })} />
      <DetailMetric metric="ev" value={formatPercent(market.estimatedEv, { signed: true })} tone={market.estimatedEv >= 0 ? 'success.main' : 'error.main'} />
      <DetailMetric metric="trust" value={market.trustLevel === 'Research' ? 'Weak' : market.trustLevel} />
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
    <Stack spacing={0.25} minWidth={0}>
      <MetricLabelWithHelp metric={metric} />
      {metric === 'trust' ? (
        <TrustValue level={value} />
      ) : (
        <Typography fontWeight={900} sx={{ color: tone }} noWrap>
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
