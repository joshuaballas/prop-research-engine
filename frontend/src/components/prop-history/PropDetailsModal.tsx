import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { Box, Dialog, DialogContent, DialogTitle, IconButton, Stack, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { getPropHistory } from '../../api/markets';
import type { PropHistoryResponseDto } from '../../api/types';
import { isDemoDataMode } from '../../config/dataMode';
import { mockPlayerResearchPlayers } from '../../data/mockPlayerResearch';
import { palette } from '../../theme/palette';
import { formatPercent } from '../../utils/formatters';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { ProviderLabel } from '../providers/ProviderLabel';
import { AppCard } from '../ui/AppCard';
import { PropHistorySkeleton } from '../ui/LoadingSkeletons';
import type { MarketOpportunity, PlayerHistoryStatKey } from '../../types/markets';
import { getHistoryContentMaxWidth, PropHistoryCard } from './PropHistoryCard';

interface PropDetailsModalProps {
  market: MarketOpportunity | null;
  open: boolean;
  onClose: () => void;
}

export function PropDetailsModal({ market, open, onClose }: PropDetailsModalProps) {
  const demoHistory = market ? findHistoryForMarket(market) : null;
  const soccerHistory = market ? buildSoccerPlayerHistory(market) : null;
  const { data: apiHistory, loading, error } = usePropHistory(market, open && !isDemoDataMode && !soccerHistory);
  const hasApiHistory = Boolean(apiHistory?.games?.length);
  const detailsHistory = hasApiHistory ? apiHistory : soccerHistory;
  const demoHistoryCardMaxWidth = market && demoHistory ? getDemoHistoryCardMaxWidth(demoHistory.stat.log, market) : undefined;
  const detailsHistoryCardMaxWidth = market && detailsHistory ? getApiHistoryCardMaxWidth(detailsHistory, market) : undefined;
  const activeHistoryCardMaxWidth = isDemoDataMode ? demoHistoryCardMaxWidth : detailsHistoryCardMaxWidth;

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
                {market.propType} · {market.side} {formatLineNumber(market.line)}
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
            <MetricStrip market={market} maxWidth={activeHistoryCardMaxWidth} />
            {isDemoDataMode && demoHistory ? (
              <AppCard sx={historyCardSx(demoHistoryCardMaxWidth)} contentSx={{ p: { xs: 1.25, md: 2 } }}>
                <PropHistoryCard stat={demoHistory.stat} selectedOffering={market} allLines={demoHistory.allLines} />
              </AppCard>
            ) : null}
            {!isDemoDataMode && loading ? (
              <PropHistorySkeleton />
            ) : null}
            {!isDemoDataMode && !loading && detailsHistory ? (
              <AppCard sx={historyCardSx(detailsHistoryCardMaxWidth)} contentSx={{ p: { xs: 1.25, md: 2 } }}>
                <PropHistoryCard history={detailsHistory} selectedOffering={market} />
              </AppCard>
            ) : null}
            {(!isDemoDataMode && !loading && !hasApiHistory && !soccerHistory) || (isDemoDataMode && !demoHistory) ? (
              <AppCard>
                <Stack spacing={0.85}>
                  <Typography variant="h5">Prop history</Typography>
                  <Typography color="text.secondary">
                    {getPropHistoryUnavailableMessage(market, apiHistory, error)}
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

function getPropHistoryUnavailableMessage(market: MarketOpportunity, apiHistory?: PropHistoryResponseDto, error?: Error) {
  if (market.sport === 'SOCCER' && market.playerHistorySummary?.available === false) {
    return market.playerHistorySummary.unavailableReason?.trim() || 'Player history is not available for this row.';
  }

  if (error) {
    return 'Prop history could not be loaded right now.';
  }

  return apiHistory?.emptyReason ?? 'Prop history is not available for this opportunity yet.';
}

function MetricStrip({ market, maxWidth }: { market: MarketOpportunity; maxWidth?: number }) {
  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: {
          xs: 'repeat(2, minmax(0, 1fr))',
          md: 'repeat(2, minmax(0, 1fr))'
        },
        gap: { xs: 1.05, md: 1.25 },
        px: { xs: 1.35, md: 1.6 },
        py: { xs: 1.25, md: 1.45 },
        borderRadius: 2.5,
        border: '1px solid rgba(148,163,184,.14)',
        bgcolor: palette.cardSurface,
        ...historyCardSx(maxWidth)
      }}
    >
      <DetailMetric metric="hitChance" value={formatPercent(market.modelProbability)} />
      <DetailMetric metric="edge" value={formatPercent(market.probabilityEdge, { signed: true })} />
    </Box>
  );
}

function DetailMetric({
  metric,
  value,
  tone = 'text.primary'
}: {
  metric: 'edge' | 'hitChance';
  value: string;
  tone?: string;
}) {
  return (
    <Stack spacing={0.35} minWidth={0}>
      <MetricLabelWithHelp metric={metric} />
      <Typography fontWeight={900} sx={{ color: tone, lineHeight: 1.25 }} noWrap>
        {value}
      </Typography>
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

function historyCardSx(maxWidth?: number) {
  if (!maxWidth) return undefined;

  return {
    width: '100%',
    maxWidth: { xs: '100%', sm: maxWidth },
    mx: 'auto'
  };
}

function getDemoHistoryCardMaxWidth(log: Array<{ value?: string | number | null }>, market: MarketOpportunity) {
  return getHistoryContentMaxWidth(countPlottedValues(log.map((row) => row.value)), 'card', formatOfferingLabel(market));
}

function getApiHistoryCardMaxWidth(history: PropHistoryResponseDto, market: MarketOpportunity) {
  return getHistoryContentMaxWidth(countPlottedValues((history.games ?? []).map((game) => game.value)), 'card', formatOfferingLabel(market));
}

function countPlottedValues(values: Array<string | number | null | undefined>) {
  return values
    .slice(-10)
    .filter((value) => {
      if (typeof value === 'number') return Number.isFinite(value);
      if (!value) return false;
      return Number.isFinite(Number.parseFloat(value.replace(/[^\d.-]/g, '')));
    }).length;
}

function buildSoccerPlayerHistory(market: MarketOpportunity): PropHistoryResponseDto | null {
  if (market.sport !== 'SOCCER' || !market.playerHistorySummary?.available) return null;

  const statKey = soccerHistoryStatKeyForProp(market.propType);
  if (!statKey) return null;

  const summary = market.playerHistorySummary;
  const stat = summary.stats?.[statKey];
  if (!stat) return null;

  return {
    sport: market.sport,
    playerId: market.playerId ?? market.canonicalPlayerId,
    player: market.player,
    propType: market.propType,
    displayPropType: market.propType,
    side: market.side,
    line: market.line,
    provider: market.provider,
    games: (summary.recentGameLog ?? [])
      .map((row) => {
        const value = row.stats?.[statKey];
        if (typeof value !== 'number') return null;
        return {
          date: row.eventDate ?? undefined,
          event: row.competition ?? row.competitionKey ?? 'Soccer match',
          value,
          line: market.line,
          selectedLine: market.line,
          side: market.side
        };
      })
      .filter((row): row is NonNullable<typeof row> => Boolean(row)),
    summary: {
      sampleSize: stat.count ?? summary.sampleSize,
      last5Average: stat.last5Average ?? null,
      last10Average: stat.last10Average ?? null,
      seasonAverage: stat.average ?? null,
      bestLabel: formatSoccerHistoryDate(summary.latestResultDate) ?? '-'
    },
    diagnostics: {
      lookupMode: 'soccerPlayerHistorySummary'
    }
  };
}

function soccerHistoryStatKeyForProp(propType: string): PlayerHistoryStatKey | null {
  const normalized = propType.replace(/[\s_-]+/g, '').toLowerCase();
  if (normalized === 'goals' || normalized === 'playergoals') return 'Goals';
  if (normalized === 'assists' || normalized === 'playerassists') return 'Assists';
  if (normalized === 'shots' || normalized === 'playershots') return 'Shots';
  if (normalized === 'shotsontarget' || normalized === 'playershotsontarget' || normalized === 'sot' || normalized === 'playersot') return 'ShotsOnTarget';
  return null;
}

function formatSoccerHistoryDate(value?: string | null) {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric'
  }).format(date);
}

function normalizeLabel(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '');
}

function formatOfferingLabel(line: MarketOpportunity) {
  return `${line.provider} Â· ${line.propType} Â· ${line.side} ${formatLineNumber(line.line)}`;
}

function formatLineNumber(line: number) {
  return Number.isInteger(line) ? String(line) : line.toFixed(1);
}
