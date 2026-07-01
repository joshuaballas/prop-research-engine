import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import {
  Autocomplete,
  Box,
  CircularProgress,
  Grid,
  Stack,
  TextField,
  Typography
} from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import { getPropHistory } from '../../api/markets';
import { getPlayerResearchCurrentLines, searchPlayers } from '../../api/players';
import type { PlayerSearchResultDto, PropHistoryResponseDto } from '../../api/types';
import { mapMarketGroupsToOpportunities } from '../../api/marketMappers';
import { ProjectionModifierBadge } from '../../components/markets/ProjectionModifierBadge';
import { PropHistoryCard } from '../../components/prop-history/PropHistoryCard';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { AppCard } from '../../components/ui/AppCard';
import { MarketCardSkeletonGrid, PropHistorySkeleton } from '../../components/ui/LoadingSkeletons';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatusPill } from '../../components/ui/StatusPill';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';
import type { MarketOpportunity } from '../../types/markets';

export function PlayerResearchPage() {
  const [selectedPlayer, setSelectedPlayer] = useState<PlayerSearchResultDto | null>(null);
  const [selectedLineId, setSelectedLineId] = useState<string>('');
  const currentLinesQuery = usePlayerResearchCurrentLines(selectedPlayer);
  const playerLines = currentLinesQuery.lines;

  useEffect(() => {
    setSelectedLineId(playerLines[0]?.id ?? '');
  }, [selectedPlayer, playerLines]);

  const selectedOffering = useMemo(
    () => playerLines.find((line) => line.id === selectedLineId) ?? playerLines[0],
    [selectedLineId, playerLines]
  );

  const handleSelectLine = (line: MarketOpportunity) => {
    setSelectedLineId(line.id);
  };

  return (
    <Stack spacing={{ xs: 2, md: 3 }} sx={{ pb: { xs: 10, md: 0 } }}>
      <PageHeader
        title="Player Research"
        description="Search prop-relevant history, current provider lines, and recent results before building an entry."
      />

      <PlayerSearch value={selectedPlayer} onChange={setSelectedPlayer} />

      {!selectedPlayer ? (
        <EmptyResearchState />
      ) : (
        <Stack spacing={2.5}>
          <PlayerProfile player={selectedPlayer} lines={playerLines} />
          {currentLinesQuery.loading ? (
            <MarketCardSkeletonGrid count={4} />
          ) : currentLinesQuery.error ? (
            <ApiStateCard tone="error" message="Current provider lines could not be loaded right now." />
          ) : selectedOffering ? (
            <PlayerResearchDashboard
              player={selectedPlayer}
              lines={playerLines}
              selectedOffering={selectedOffering}
              selectedLineId={selectedOffering.id}
              onSelectLine={handleSelectLine}
            />
          ) : (
            <ApiStateCard message="No current provider lines found for this player. Try another player or refresh market data after new props are captured." />
          )}
        </Stack>
      )}
    </Stack>
  );
}

function PlayerSearch({
  value,
  onChange
}: {
  value: PlayerSearchResultDto | null;
  onChange: (player: PlayerSearchResultDto | null) => void;
}) {
  const [inputValue, setInputValue] = useState('');
  const [options, setOptions] = useState<PlayerSearchResultDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [searchFailed, setSearchFailed] = useState(false);
  const [autocompleteOpen, setAutocompleteOpen] = useState(false);
  const [completedQuery, setCompletedQuery] = useState('');
  const canSearch = inputValue.trim().length >= 2;
  const trimmedInputValue = inputValue.trim();
  const shouldShowMenu = autocompleteOpen && canSearch && (loading || options.length > 0 || searchFailed || completedQuery === trimmedInputValue);

  useEffect(() => {
    const query = inputValue.trim();
    if (query.length < 2) {
      setOptions([]);
      setLoading(false);
      setSearchFailed(false);
      setAutocompleteOpen(false);
      setCompletedQuery('');
      return;
    }

    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => {
      setLoading(true);
      setSearchFailed(false);
      searchPlayers(query, 5, controller.signal)
        .then((players) => {
          setOptions(players);
          setCompletedQuery(query);
        })
        .catch((error: unknown) => {
          if (error instanceof DOMException && error.name === 'AbortError') return;
          setOptions([]);
          setSearchFailed(true);
          setCompletedQuery(query);
        })
        .finally(() => {
          if (!controller.signal.aborted) {
            setLoading(false);
          }
        });
    }, 250);

    return () => {
      window.clearTimeout(timeoutId);
      controller.abort();
    };
  }, [inputValue]);

  return (
    <Autocomplete
      value={value}
      inputValue={inputValue}
      onInputChange={(_, nextValue, reason) => {
        if (reason !== 'reset') {
          setInputValue(nextValue);
          setAutocompleteOpen(nextValue.trim().length >= 2);
        }
      }}
      onChange={(_, nextValue) => {
        setInputValue(nextValue?.displayName ?? '');
        setAutocompleteOpen(false);
        onChange(nextValue);
      }}
      options={options}
      open={shouldShowMenu}
      onOpen={() => setAutocompleteOpen(inputValue.trim().length >= 2)}
      onClose={() => setAutocompleteOpen(false)}
      forcePopupIcon={false}
      loading={loading}
      loadingText="Searching players..."
      noOptionsText={getNoOptionsText(inputValue, searchFailed)}
      filterOptions={(searchOptions) => searchOptions}
      getOptionLabel={(option) => option.displayName}
      isOptionEqualToValue={(option, nextValue) => option.playerId === nextValue.playerId}
      ListboxProps={{
        sx: {
          maxHeight: 320
        }
      }}
      renderOption={(props, option) => (
        <Box component="li" {...props}>
          <Stack spacing={0.1} minWidth={0}>
            <Typography fontWeight={850}>{option.displayName}</Typography>
            {option.subtitle ? (
              <Typography variant="caption" color="text.secondary" noWrap>
                {option.subtitle}
              </Typography>
            ) : null}
          </Stack>
        </Box>
      )}
      renderInput={(params) => (
        <TextField
          {...params}
          placeholder="Search player by name"
          InputProps={{
            ...params.InputProps,
            startAdornment: (
              <>
                <SearchRoundedIcon sx={{ color: 'text.secondary', mr: 1 }} />
                {params.InputProps.startAdornment}
              </>
            ),
            endAdornment: (
              <>
                {loading ? <CircularProgress color="inherit" size={18} sx={{ mr: 1 }} /> : null}
                {params.InputProps.endAdornment}
              </>
            )
          }}
        />
      )}
      sx={{
        maxWidth: 760,
        '& .MuiOutlinedInput-root': {
          minHeight: 58,
          borderRadius: radius.card,
          bgcolor: palette.cardSurface,
          border: `1px solid ${palette.border}`
        }
      }}
    />
  );
}

function EmptyResearchState() {
  const helperCards = [
    {
      title: 'Research current lines',
      body: 'See available provider props with trust, model edge, and modifier context.'
    },
    {
      title: 'Compare recent prop history',
      body: 'Review persisted results for stat categories that map to current lines.'
    },
    {
      title: 'Check hit rate vs line',
      body: 'Use line-specific history and sample notes before adding a leg.'
    }
  ];

  return (
    <AppCard
      sx={{
        borderColor: 'rgba(125,211,252,.22)',
        backgroundColor: palette.cardSurface
      }}
      contentSx={{ p: { xs: 2, md: 2.6 } }}
    >
      <Stack spacing={2}>
        <Stack spacing={0.75}>
          <Typography variant="h4">Search for a player</Typography>
          <Typography color="text.secondary">
            Start typing a player name to load persisted players from the database, then review current provider lines and prop history.
          </Typography>
        </Stack>
        <Grid container spacing={1.2}>
          {helperCards.map((card) => (
            <Grid key={card.title} size={{ xs: 12, md: 4 }}>
              <Box
                sx={{
                  height: '100%',
                  p: 1.45,
                  borderRadius: radius.card,
                  border: '1px solid rgba(148,163,184,.14)',
                  bgcolor: palette.cardSurface
                }}
              >
                <Typography fontWeight={900}>{card.title}</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.45 }}>
                  {card.body}
                </Typography>
              </Box>
            </Grid>
          ))}
        </Grid>
      </Stack>
    </AppCard>
  );
}

function SectionHeader({ title, description }: { title: string; description?: string }) {
  return (
    <Stack spacing={0.25}>
      <Typography variant="h5">{title}</Typography>
      {description ? (
        <Typography color="text.secondary" variant="body2">
          {description}
        </Typography>
      ) : null}
    </Stack>
  );
}

function PlayerProfile({
  player,
  lines
}: {
  player: PlayerSearchResultDto;
  lines: MarketOpportunity[];
}) {
  const nextGame = useMemo(() => findNextGame(lines), [lines]);

  return (
    <AppCard contentSx={{ p: { xs: 1.45, md: 2.4 } }}>
      <Grid container spacing={{ xs: 1.15, md: 2 }} alignItems="center">
        <Grid size={{ xs: 12, md: 7 }}>
          <Stack spacing={{ xs: 0.55, md: 0.75 }}>
            <Typography variant="h2" sx={{ fontSize: { xs: 28, sm: 34, md: 42 }, lineHeight: { xs: 1.05, md: 1.08 } }}>
              {player.displayName}
            </Typography>
            <Stack direction="row" gap={0.8} flexWrap="wrap" useFlexGap>
              {player.league || player.sport ? <StatusPill label={player.league ?? player.sport ?? ''} /> : null}
              {player.position ? <StatusPill label={player.position} /> : null}
            </Stack>
          </Stack>
        </Grid>
        <Grid size={{ xs: 12, md: 5 }}>
          <Stack
            spacing={0.35}
            sx={{
              p: { xs: 1.1, md: 1.7 },
              borderRadius: radius.card,
              border: `1px solid ${palette.border}`,
              bgcolor: palette.cardSurface
            }}
          >
            <Typography variant="caption" color="text.secondary" fontWeight={800}>
              Next game
            </Typography>
            <Typography fontWeight={850} sx={{ fontSize: { xs: 13.5, md: 16 }, lineHeight: 1.35 }}>
              {nextGame ?? 'No upcoming game found'}
            </Typography>
          </Stack>
        </Grid>
      </Grid>
    </AppCard>
  );
}

function findNextGame(lines: MarketOpportunity[]) {
  const now = Date.now();
  const eventRows = lines
    .map(toPlayerLineEventRow)
    .filter((row): row is PlayerLineEventRow => Boolean(row));
  const upcoming = eventRows
    .filter((row) => row.timestamp >= now)
    .sort((left, right) => left.timestamp - right.timestamp)[0];
  const fallback = eventRows
    .sort((left, right) => right.timestamp - left.timestamp)[0];
  const event = upcoming ?? fallback;

  if (!event) {
    return null;
  }

  return event.matchup
    ? `${event.matchup} - ${formatNextGameTime(event.timestamp)}`
    : formatNextGameTime(event.timestamp);
}

interface PlayerLineEventRow {
  matchup?: string;
  timestamp: number;
}

function toPlayerLineEventRow(line: MarketOpportunity): PlayerLineEventRow | null {
  if (!line.eventStartTimeUtc) {
    return null;
  }

  const timestamp = new Date(line.eventStartTimeUtc).getTime();
  if (!Number.isFinite(timestamp)) {
    return null;
  }

  return {
    matchup: isPlaceholderEventLabel(line.event) ? undefined : line.event,
    timestamp
  };
}

function isPlaceholderEventLabel(value?: string | null) {
  return !value
    || value === 'Current provider line'
    || value === 'Upcoming event';
}

function formatNextGameTime(timestamp: number) {
  const date = new Date(timestamp);
  const now = new Date();
  const tomorrow = new Date(now);
  tomorrow.setDate(now.getDate() + 1);
  const timeLabel = new Intl.DateTimeFormat('en-US', {
    hour: 'numeric',
    minute: '2-digit'
  }).format(date);

  if (date.toDateString() === now.toDateString()) {
    return `Today ${timeLabel}`;
  }

  if (date.toDateString() === tomorrow.toDateString()) {
    return `Tomorrow ${timeLabel}`;
  }

  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  }).format(date);
}

function PlayerResearchDashboard({
  player,
  lines,
  selectedOffering,
  selectedLineId,
  onSelectLine
}: {
  player: PlayerSearchResultDto;
  lines: MarketOpportunity[];
  selectedOffering: MarketOpportunity;
  selectedLineId: string;
  onSelectLine: (line: MarketOpportunity) => void;
}) {
  return (
    <Grid container spacing={1.5} alignItems="stretch">
      <Grid size={{ xs: 12, md: 6 }}>
        <CurrentLinesTile lines={lines} selectedLineId={selectedLineId} onSelectLine={onSelectLine} />
      </Grid>
      <Grid size={{ xs: 12, md: 6 }}>
        <PropHistoryTile player={player} selectedOffering={selectedOffering} allLines={lines} />
      </Grid>
    </Grid>
  );
}

function CurrentLinesTile({
  lines,
  selectedLineId,
  onSelectLine
}: {
  lines: MarketOpportunity[];
  selectedLineId: string;
  onSelectLine: (line: MarketOpportunity) => void;
}) {
  const [providerFilter, setProviderFilter] = useState('All');
  const providerOptions = useMemo(() => ['All', ...Array.from(new Set(lines.map((line) => line.provider)))], [lines]);
  const visibleLines = useMemo(
    () => sortCurrentLines(providerFilter === 'All' ? lines : lines.filter((line) => line.provider === providerFilter)),
    [lines, providerFilter]
  );

  useEffect(() => {
    if (!providerOptions.includes(providerFilter)) {
      setProviderFilter('All');
    }
  }, [providerFilter, providerOptions]);

  useEffect(() => {
    if (visibleLines.length && !visibleLines.some((line) => line.id === selectedLineId)) {
      onSelectLine(visibleLines[0]);
    }
  }, [onSelectLine, selectedLineId, visibleLines]);

  return (
    <DashboardTile>
      <SectionHeader title="Current lines" description="Available provider lines for the selected player." />
      <ProviderFilter options={providerOptions} selected={providerFilter} onChange={setProviderFilter} />
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' },
          gridAutoRows: 'auto',
          gap: 0.85,
          overflowY: 'auto',
          maxHeight: { xs: 378, md: 'none' },
          pr: { xs: 0.85, md: 0.85 },
          pb: { xs: 0.5, md: 0 },
          minHeight: 0,
          flex: 1,
          alignContent: 'start',
          scrollbarWidth: 'thin',
          scrollbarColor: 'rgba(148,163,184,.42) rgba(15,23,42,.34)',
          '&::-webkit-scrollbar': {
            width: 8
          },
          '&::-webkit-scrollbar-track': {
            background: 'rgba(15,23,42,.34)',
            borderRadius: radius.pill
          },
          '&::-webkit-scrollbar-thumb': {
            background: 'rgba(148,163,184,.42)',
            borderRadius: radius.pill
          },
          '&::-webkit-scrollbar-thumb:hover': {
            background: 'rgba(148,163,184,.56)'
          }
        }}
      >
        {visibleLines.map((line) => (
          <CurrentLineCard key={line.id} line={line} active={line.id === selectedLineId} onSelect={() => onSelectLine(line)} />
        ))}
      </Box>
    </DashboardTile>
  );
}

function ProviderFilter({
  options,
  selected,
  onChange
}: {
  options: string[];
  selected: string;
  onChange: (provider: string) => void;
}) {
  return (
    <Box
      sx={{
        display: 'flex',
        gap: 0.65,
        overflowX: 'auto',
        pb: 0.25,
        scrollbarWidth: 'none',
        '&::-webkit-scrollbar': { display: 'none' }
      }}
    >
      {options.map((provider) => {
        const active = provider === selected;
        return (
          <Box
            component="button"
            key={provider}
            onClick={() => onChange(provider)}
            sx={{
              flexShrink: 0,
              minHeight: 28,
              px: 1.15,
              borderRadius: radius.featuredCard,
              fontSize: 11,
              fontWeight: 800,
              color: active ? palette.text : palette.textMuted,
              border: active ? '1px solid rgba(34,211,238,.48)' : '1px solid rgba(148,163,184,.18)',
              bgcolor: active ? 'rgba(11,74,94,.38)' : 'rgba(15,23,42,.28)',
              cursor: 'pointer',
              '&:hover': {
                borderColor: active ? 'rgba(34,211,238,.58)' : 'rgba(148,163,184,.28)',
                bgcolor: active ? 'rgba(11,74,94,.46)' : 'rgba(15,23,42,.42)'
              },
              '&:focus-visible': {
                outline: '2px solid rgba(34,211,238,.42)',
                outlineOffset: 2
              }
            }}
          >
            {provider}
          </Box>
        );
      })}
    </Box>
  );
}

function CurrentLineCard({ line, active, onSelect }: { line: MarketOpportunity; active: boolean; onSelect: () => void }) {
  const eventContext = formatCurrentLineEventContext(line);

  return (
    <Box
      role="button"
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          onSelect();
        }
      }}
      sx={{
        px: 1.35,
        py: 1.35,
        borderRadius: radius.card,
        border: active ? '1px solid rgba(34,211,238,.5)' : '1px solid rgba(148,163,184,.16)',
        bgcolor: active ? 'rgba(11,74,94,.26)' : palette.cardSurface,
        cursor: 'pointer',
        transition: 'border-color .16s ease, background-color .16s ease',
        '&:hover': {
          borderColor: active ? 'rgba(34,211,238,.58)' : 'rgba(148,163,184,.26)',
          bgcolor: active ? 'rgba(11,74,94,.32)' : palette.cardSurface
        },
        '&:focus-visible': {
          outline: '2px solid rgba(34,211,238,.42)',
          outlineOffset: 2
        }
      }}
    >
      <Stack direction="row" alignItems="flex-start" justifyContent="space-between" gap={1.2}>
        <Stack spacing={0.5} minWidth={0} width="100%">
          <ProviderLabel provider={line.provider} category={line.providerCategory} compact />
          <Stack direction="row" alignItems="center" gap={0.65} flexWrap="wrap" useFlexGap sx={{ mt: 0.15 }}>
            <Typography fontWeight={700} variant="body2">
              {line.propType}
            </Typography>
            <Typography color="text.secondary" variant="body2">
              |
            </Typography>
            <Typography color="rgba(226,232,240,.84)" fontWeight={700} variant="body2">
              {formatLineLabel(line)}
            </Typography>
          </Stack>
          {eventContext ? (
            <Stack spacing={0.2} sx={{ minWidth: 0, pt: 0.05 }}>
              {eventContext.matchup ? (
                <Typography
                  color="text.secondary"
                  variant="caption"
                  sx={{
                    lineHeight: 1.35,
                    overflowWrap: 'anywhere'
                  }}
                >
                  {eventContext.matchup}
                </Typography>
              ) : null}
              <Typography color="text.secondary" variant="caption" sx={{ display: 'block', lineHeight: 1.4 }}>
                {eventContext.time}
              </Typography>
            </Stack>
          ) : null}
        </Stack>
        <Stack alignItems="flex-end" spacing={0.45} sx={{ flexShrink: 0 }}>
          {line.projectionModifier !== 'Normal' ? <ProjectionModifierBadge modifier={line.projectionModifier} /> : null}
        </Stack>
      </Stack>
    </Box>
  );
}

function formatCurrentLineEventContext(line: MarketOpportunity) {
  const event = toPlayerLineEventRow(line);
  if (!event) {
    return null;
  }

  return {
    matchup: event.matchup,
    time: formatNextGameTime(event.timestamp)
  };
}

function PropHistoryTile({
  player,
  selectedOffering,
  allLines
}: {
  player: PlayerSearchResultDto;
  selectedOffering: MarketOpportunity;
  allLines: MarketOpportunity[];
}) {
  const historyQuery = usePropHistory(player, selectedOffering);

  return (
    <DashboardTile>
      {historyQuery.loading ? (
        <PropHistorySkeleton />
      ) : historyQuery.error ? (
        <InlineState tone="error" message="Prop history could not be loaded for this line." />
      ) : historyQuery.history ? (
        <PropHistoryCard history={historyQuery.history} selectedOffering={selectedOffering} allLines={allLines} />
      ) : (
        <InlineState message="Prop history is not available for this line yet." />
      )}
    </DashboardTile>
  );
}

function usePropHistory(player: PlayerSearchResultDto, selectedOffering: MarketOpportunity) {
  const [history, setHistory] = useState<PropHistoryResponseDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(false);
    setHistory(null);

    getPropHistory(
      {
        sport: selectedOffering.sport,
        playerId: getEffectivePlayerId(player),
        playerName: player.displayName,
        propType: selectedOffering.propType,
        side: selectedOffering.side,
        line: selectedOffering.line,
        provider: selectedOffering.provider,
        take: 10
      },
      controller.signal
    )
      .then((response) => {
        setHistory(response);
      })
      .catch((requestError: unknown) => {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') return;
        setError(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => {
      controller.abort();
    };
  }, [
    player.displayName,
    player.effectivePlayerId,
    player.playerId,
    selectedOffering.line,
    selectedOffering.propType,
    selectedOffering.provider,
    selectedOffering.side,
    selectedOffering.sport
  ]);

  return { history, loading, error };
}

function usePlayerResearchCurrentLines(player: PlayerSearchResultDto | null) {
  const [lines, setLines] = useState<MarketOpportunity[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);

  useEffect(() => {
    if (!player) {
      setLines([]);
      setLoading(false);
      setError(false);
      return;
    }

    const controller = new AbortController();
    setLoading(true);
    setError(false);
    setLines([]);

    getPlayerResearchCurrentLines(getEffectivePlayerId(player), 50, controller.signal)
      .then((response) => {
        setLines(mapMarketGroupsToOpportunities(response.lines, 'player-research'));
      })
      .catch((requestError: unknown) => {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') return;
        setError(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => {
      controller.abort();
    };
  }, [player]);

  return { lines, loading, error };
}

function getEffectivePlayerId(player: PlayerSearchResultDto) {
  return player.effectivePlayerId ?? player.playerId;
}

function ApiStateCard({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <AppCard contentSx={{ p: { xs: 2, md: 2.4 } }}>
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'} fontWeight={750}>
        {message}
      </Typography>
    </AppCard>
  );
}

function InlineState({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <Stack alignItems="center" justifyContent="center" sx={{ minHeight: 260, flex: 1 }}>
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'} fontWeight={760} textAlign="center">
        {message}
      </Typography>
    </Stack>
  );
}

function DashboardTile({ children }: { children: React.ReactNode }) {
  return (
    <AppCard
      sx={{ height: { xs: 'auto', md: 595 } }}
      contentSx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        gap: 0.95,
        overflow: 'hidden'
      }}
    >
      {children}
    </AppCard>
  );
}

function getNoOptionsText(inputValue: string, searchFailed: boolean) {
  if (searchFailed) return 'Player search is unavailable right now.';
  if (inputValue.trim().length < 2) return '';
  return 'No players found.';
}

function sortCurrentLines(lines: MarketOpportunity[]) {
  return [...lines].sort((first, second) => modifierPriority(first.projectionModifier) - modifierPriority(second.projectionModifier));
}

function modifierPriority(modifier: MarketOpportunity['projectionModifier']) {
  switch (modifier) {
    case 'Demon':
    case 'Goblin':
      return 0;
    case 'Discount':
      return 1;
    case 'Payout Boost':
      return 2;
    case 'Bonus Lineup':
      return 3;
    case 'Unknown Promo':
      return 4;
    case 'Normal':
    default:
      return 5;
  }
}

function formatLineLabel(line: MarketOpportunity) {
  return `${line.side} ${formatLineNumber(line.line)}`;
}

function formatLineNumber(line: number) {
  return Number.isInteger(line) ? String(line) : line.toFixed(1);
}
