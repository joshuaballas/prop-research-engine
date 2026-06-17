import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import {
  Autocomplete,
  Box,
  Button,
  Grid,
  Stack,
  TextField,
  Typography
} from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis
} from 'recharts';
import { ProjectionModifierBadge } from '../../components/markets/ProjectionModifierBadge';
import { ProviderLabel } from '../../components/providers/ProviderLabel';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatusPill } from '../../components/ui/StatusPill';
import { secondaryGhostButtonSx } from '../../components/ui/buttonStyles';
import { mockPlayerResearchPlayers, type PlayerResearchProfile, type PlayerResearchStat } from '../../data/mockPlayerResearch';
import { palette } from '../../theme/palette';
import type { MarketOpportunity } from '../../types/markets';

export function PlayerResearchPage() {
  const [selectedPlayer, setSelectedPlayer] = useState<PlayerResearchProfile | null>(null);
  const [selectedLineId, setSelectedLineId] = useState<string>('');

  useEffect(() => {
    setSelectedLineId(selectedPlayer?.currentLines[0]?.id ?? '');
  }, [selectedPlayer]);

  const selectedOffering = useMemo(
    () => selectedPlayer?.currentLines.find((line) => line.id === selectedLineId) ?? selectedPlayer?.currentLines[0],
    [selectedLineId, selectedPlayer]
  );
  const selectedStat = useMemo(
    () => selectedPlayer?.stats.find((stat) => stat.label === selectedOffering?.propType) ?? selectedPlayer?.stats[0],
    [selectedOffering, selectedPlayer]
  );

  const handleSelectLine = (line: MarketOpportunity) => {
    setSelectedLineId(line.id);
  };

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Player Research"
        description="Search prop-relevant history, current DFS lines, and recent results before building an entry."
      />

      <PlayerSearch value={selectedPlayer} onChange={setSelectedPlayer} />

      {!selectedPlayer ? (
        <EmptyResearchState onSelect={setSelectedPlayer} />
      ) : (
        <Stack spacing={2.5}>
          <PlayerProfile player={selectedPlayer} />
          {selectedStat && selectedOffering ? (
            <PlayerResearchDashboard
              player={selectedPlayer}
              selectedStat={selectedStat}
              selectedOffering={selectedOffering}
              selectedLineId={selectedOffering.id}
              onSelectLine={handleSelectLine}
            />
          ) : null}
        </Stack>
      )}
    </Stack>
  );
}

function PlayerSearch({
  value,
  onChange
}: {
  value: PlayerResearchProfile | null;
  onChange: (player: PlayerResearchProfile | null) => void;
}) {
  return (
    <Autocomplete
      value={value}
      onChange={(_, nextValue) => onChange(nextValue)}
      options={mockPlayerResearchPlayers}
      getOptionLabel={(option) => `${option.name} - ${option.teamOrLeague} - ${option.sport}`}
      isOptionEqualToValue={(option, nextValue) => option.id === nextValue.id}
      renderInput={(params) => (
        <TextField
          {...params}
          placeholder="Search player by name, team, or sport"
          InputProps={{
            ...params.InputProps,
            startAdornment: (
              <>
                <SearchRoundedIcon sx={{ color: 'text.secondary', mr: 1 }} />
                {params.InputProps.startAdornment}
              </>
            )
          }}
        />
      )}
      sx={{
        maxWidth: 760,
        '& .MuiOutlinedInput-root': {
          minHeight: 58,
          borderRadius: 3,
          bgcolor: 'rgba(15,23,42,.68)',
          border: `1px solid ${palette.border}`
        }
      }}
    />
  );
}

function EmptyResearchState({ onSelect }: { onSelect: (player: PlayerResearchProfile) => void }) {
  const helperCards = [
    {
      title: 'Research current lines',
      body: 'See available DFS props with provider, confidence, EV, and modifier context.'
    },
    {
      title: 'Compare recent prop history',
      body: 'Review recent results for the stat categories that map to common DFS lines.'
    },
    {
      title: 'Check hit rate vs line',
      body: 'Use line-specific hit rate and sample notes before adding a leg.'
    }
  ];

  return (
    <AppCard
      sx={{
        borderColor: 'rgba(125,211,252,.22)',
        background: 'linear-gradient(135deg, rgba(14, 35, 55, 0.88), rgba(10, 16, 32, 0.96))'
      }}
      contentSx={{ p: { xs: 2, md: 2.6 } }}
    >
      <Grid container spacing={2.2} alignItems="center">
        <Grid size={{ xs: 12, md: 5 }}>
          <Stack spacing={1}>
            <Typography variant="h4">Start with a player</Typography>
            <Typography color="text.secondary">
              Pull up current prop lines, recent prop history, and sample-size context for supported DFS sports.
            </Typography>
          </Stack>
        </Grid>
        <Grid size={{ xs: 12, md: 7 }}>
          <Stack direction="row" gap={1} flexWrap="wrap" useFlexGap justifyContent={{ xs: 'flex-start', md: 'flex-end' }}>
            {mockPlayerResearchPlayers.map((player) => (
              <Button key={player.id} variant="outlined" onClick={() => onSelect(player)} sx={secondaryGhostButtonSx}>
                {player.name}
              </Button>
            ))}
          </Stack>
        </Grid>
      </Grid>
      <Grid container spacing={1.2} sx={{ mt: 2 }}>
        {helperCards.map((card) => (
          <Grid key={card.title} size={{ xs: 12, md: 4 }}>
            <Box
              sx={{
                height: '100%',
                p: 1.45,
                borderRadius: 3,
                border: '1px solid rgba(148,163,184,.14)',
                bgcolor: 'rgba(2,6,23,.2)'
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

function PlayerProfile({ player }: { player: PlayerResearchProfile }) {
  return (
    <AppCard contentSx={{ p: { xs: 2, md: 2.4 } }}>
      <Grid container spacing={2} alignItems="center">
        <Grid size={{ xs: 12, md: 7 }}>
          <Stack spacing={0.75}>
            <Typography variant="h2">{player.name}</Typography>
            <Stack direction="row" gap={0.8} flexWrap="wrap" useFlexGap>
              <StatusPill label={player.sport} />
              {player.position ? <StatusPill label={player.position} /> : null}
            </Stack>
            <Typography color="text.secondary">
              {player.teamOrLeague} - {player.opponentContext}
            </Typography>
          </Stack>
        </Grid>
        <Grid size={{ xs: 12, md: 5 }}>
          <Stack
            spacing={0.35}
            sx={{
              p: 1.7,
              borderRadius: 3,
              border: `1px solid ${palette.border}`,
              bgcolor: 'rgba(2,6,23,.28)'
            }}
          >
            <Typography variant="caption" color="text.secondary" fontWeight={800}>
              Next event
            </Typography>
            <Typography fontWeight={850}>{player.nextEvent}</Typography>
          </Stack>
        </Grid>
      </Grid>
    </AppCard>
  );
}

function PlayerResearchDashboard({
  player,
  selectedStat,
  selectedOffering,
  selectedLineId,
  onSelectLine
}: {
  player: PlayerResearchProfile;
  selectedStat: PlayerResearchStat;
  selectedOffering: MarketOpportunity;
  selectedLineId: string;
  onSelectLine: (line: MarketOpportunity) => void;
}) {
  return (
    <Grid container spacing={1.5} alignItems="stretch">
      <Grid size={{ xs: 12, md: 6 }}>
        <CurrentLinesTile lines={player.currentLines} selectedLineId={selectedLineId} onSelectLine={onSelectLine} />
      </Grid>
      <Grid size={{ xs: 12, md: 6 }}>
        <PropHistoryTile stat={selectedStat} selectedOffering={selectedOffering} allLines={player.currentLines} />
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
      <SectionHeader title="Current lines" description="Available DFS lines for the selected player." />
      <ProviderFilter options={providerOptions} selected={providerFilter} onChange={setProviderFilter} />
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' },
          gridAutoRows: { md: 'minmax(69px, auto)' },
          gap: 0.85,
          overflowY: 'auto',
          maxHeight: { xs: 408, md: 'none' },
          pr: { xs: 0.25, md: 0.35 },
          minHeight: 0,
          flex: 1,
          alignContent: 'start',
          scrollbarWidth: 'none',
          '&::-webkit-scrollbar': { display: 'none' }
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
          <Button
            key={provider}
            size="small"
            variant="outlined"
            onClick={() => onChange(provider)}
            sx={{
              flexShrink: 0,
              minHeight: 28,
              px: 1.15,
              borderRadius: 2,
              fontSize: 11,
              color: active ? palette.text : palette.textMuted,
              borderColor: active ? 'rgba(34,211,238,.48)' : 'rgba(148,163,184,.18)',
              bgcolor: active ? 'rgba(11,74,94,.38)' : 'rgba(15,23,42,.28)',
              '&:hover': {
                borderColor: active ? 'rgba(34,211,238,.58)' : 'rgba(148,163,184,.28)',
                bgcolor: active ? 'rgba(11,74,94,.46)' : 'rgba(15,23,42,.42)'
              }
            }}
          >
            {provider}
          </Button>
        );
      })}
    </Box>
  );
}

function CurrentLineCard({ line, active, onSelect }: { line: MarketOpportunity; active: boolean; onSelect: () => void }) {
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
        py: 1.25,
        minHeight: { md: 69 },
        borderRadius: 3,
        border: active ? '1px solid rgba(34,211,238,.5)' : '1px solid rgba(148,163,184,.16)',
        bgcolor: active ? 'rgba(11,74,94,.26)' : 'rgba(2,6,23,.22)',
        cursor: 'pointer',
        transition: 'border-color .16s ease, background-color .16s ease',
        '&:hover': {
          borderColor: active ? 'rgba(34,211,238,.58)' : 'rgba(148,163,184,.26)',
          bgcolor: active ? 'rgba(11,74,94,.32)' : 'rgba(15,23,42,.36)'
        },
        '&:focus-visible': {
          outline: '2px solid rgba(34,211,238,.42)',
          outlineOffset: 2
        }
      }}
    >
      <Stack direction="row" alignItems="center" justifyContent="space-between" gap={1.2}>
        <Stack spacing={0.5} minWidth={0}>
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
        </Stack>
        <Stack alignItems="flex-end" spacing={0.45} sx={{ flexShrink: 0 }}>
          {line.projectionModifier !== 'Normal' ? <ProjectionModifierBadge modifier={line.projectionModifier} /> : null}
        </Stack>
      </Stack>
    </Box>
  );
}

function PropHistoryTile({
  stat,
  selectedOffering,
  allLines
}: {
  stat: PlayerResearchStat;
  selectedOffering: MarketOpportunity;
  allLines: MarketOpportunity[];
}) {
  return (
    <DashboardTile>
      <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
        <SectionHeader title="Prop history" description="Recent stat values against the selected line." />
        <Typography color="warning.main" variant="body2" fontWeight={900} textAlign="right">
          {formatOfferingLabel(selectedOffering)}
        </Typography>
      </Stack>
      <PropHistoryBarChart stat={stat} selectedOffering={selectedOffering} />
      <StatSummary stat={stat} selectedOffering={selectedOffering} allLines={allLines} />
    </DashboardTile>
  );
}

function PropHistoryBarChart({ stat, selectedOffering }: { stat: PlayerResearchStat; selectedOffering: MarketOpportunity }) {
  const lineValue = selectedOffering.line;
  const data: ChartDatum[] = stat.log
    .map((row) => {
      const value = parseStatNumber(row.value) ?? 0;
      const result = deriveResult(value, lineValue, selectedOffering.side);
      return {
        date: row.date,
        event: row.event,
        value,
        valueLabel: row.value,
        line: lineValue,
        lineLabel: formatLineLabel(selectedOffering),
        result
      };
    })
    .reverse();

  return (
    <Box
      sx={{
        width: '100%',
        height: { xs: 250, md: 'auto' },
        minHeight: { md: 250, xl: 280 },
        minWidth: 0,
        flex: { md: '1 1 auto' },
        flexShrink: 0,
        position: 'relative',
        zIndex: 0
      }}
    >
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 12, right: 12, bottom: 2, left: -10 }}>
          <CartesianGrid vertical={false} stroke="rgba(148,163,184,.12)" />
          <XAxis
            dataKey="date"
            tick={{ fill: palette.textMuted, fontSize: 12 }}
            tickLine={false}
            axisLine={{ stroke: 'rgba(148,163,184,.18)' }}
          />
          <YAxis
            tick={{ fill: palette.textMuted, fontSize: 12 }}
            tickLine={false}
            axisLine={false}
            domain={[0, 'dataMax + 4']}
            allowDecimals
          />
          <Tooltip cursor={{ fill: 'rgba(125,211,252,.06)' }} content={<PropHistoryTooltip />} />
          {lineValue !== undefined ? (
            <ReferenceLine
              y={lineValue}
              stroke={palette.warning}
              strokeDasharray="5 5"
              strokeWidth={2.2}
            />
          ) : null}
          <Bar dataKey="value" radius={[8, 8, 3, 3]} maxBarSize={42}>
            {data.map((row) => (
              <Cell
                key={`${row.date}-${row.event}`}
                fill={row.result === 'Cleared' ? 'rgba(34,197,94,.82)' : row.result === 'Missed' ? 'rgba(244,63,94,.78)' : 'rgba(148,163,184,.62)'}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </Box>
  );
}

function PropHistoryTooltip({ active, payload }: { active?: boolean; payload?: Array<{ payload: ChartDatum }> }) {
  if (!active || !payload?.length) return null;
  const row = payload[0].payload;
  return (
    <Box
      sx={{
        p: 1.15,
        borderRadius: 2,
        border: '1px solid rgba(148,163,184,.22)',
        bgcolor: 'rgba(2,6,23,.94)',
        boxShadow: '0 16px 32px rgba(0,0,0,.28)'
      }}
    >
      <Typography fontWeight={900}>{row.event}</Typography>
      <Typography variant="caption" color="text.secondary">
        {row.date}
      </Typography>
      <Stack spacing={0.25} sx={{ mt: 0.8 }}>
        <TooltipRow label="Stat value" value={row.valueLabel} />
        <TooltipRow label="Line" value={row.lineLabel} />
        <TooltipRow label="Result" value={row.result} color={resultColor(row.result)} />
      </Stack>
    </Box>
  );
}

function TooltipRow({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <Stack direction="row" spacing={2} justifyContent="space-between">
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="caption" fontWeight={850} sx={{ color: color ?? palette.text }}>
        {value}
      </Typography>
    </Stack>
  );
}

function StatSummary({
  stat,
  selectedOffering,
  allLines
}: {
  stat: PlayerResearchStat;
  selectedOffering: MarketOpportunity;
  allLines: MarketOpportunity[];
}) {
  const hitRate = calculateHitRate(stat, selectedOffering);
  const gradedValues = stat.log
    .map((row) => parseStatNumber(row.value))
    .filter((value): value is number => value !== undefined);
  const clears = gradedValues.filter((value) => deriveResult(value, selectedOffering.line, selectedOffering.side) === 'Cleared').length;
  const alternatives = allLines.filter((line) => line.propType === selectedOffering.propType && line.id !== selectedOffering.id);
  const alternativeProviders = Array.from(new Set(alternatives.map((line) => line.provider))).slice(0, 3);
  const metrics = [
    ['Last 5', stat.last5Average],
    ['Last 10', stat.last10Average],
    ['Season', stat.seasonAverage],
    ['Hit rate', hitRate],
    ['Best', stat.bestRecentResult]
  ];
  const hasWarning = Boolean(stat.sampleWarning);
  const baseContext = `${selectedOffering.provider} ${formatLineLabel(selectedOffering)}: last 5 avg ${stat.last5Average} vs selected line ${formatLineNumber(
    selectedOffering.line
  )}. Cleared ${clears} of ${gradedValues.length} tracked results.`;
  const alternativeContext = alternativeProviders.length ? ` Alternatives listed from ${alternativeProviders.join(', ')}.` : '';

  return (
    <Stack spacing={0.75}>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(5, max-content)' },
          justifyContent: { xs: 'stretch', sm: 'start' },
          alignItems: 'center',
          px: { xs: 1.15, sm: 1.3 },
          py: { xs: 1, sm: 0.85 },
          columnGap: { xs: 1.15, sm: 0 },
          rowGap: 0.85,
          border: '1px solid rgba(148,163,184,.16)',
          borderRadius: 3,
          bgcolor: 'rgba(15, 23, 42, 0.44)',
          boxShadow: 'inset 0 1px 0 rgba(255,255,255,.025)',
          '& > div': {
            minWidth: 0
          },
          '& > div:not(:first-of-type)': {
            borderLeft: { xs: 'none', sm: '1px solid rgba(148,163,184,.14)' },
            pl: { xs: 0, sm: 1.5 },
            ml: { xs: 0, sm: 1.5 }
          }
        }}
      >
        {metrics.map(([label, value]) => (
          <Box key={label}>
            <Typography variant="caption" color="text.secondary" fontWeight={800}>
              {label}
            </Typography>
            <Typography fontWeight={850} variant="body2" sx={{ mt: 0.15 }} noWrap={label !== 'Best'}>
              {value}
            </Typography>
          </Box>
        ))}
      </Box>
      <Box
        sx={{
          p: 1.05,
          borderRadius: 3,
          border: hasWarning ? '1px solid rgba(245,158,11,.28)' : '1px solid rgba(148,163,184,.12)',
          bgcolor: hasWarning ? 'rgba(54, 38, 13, 0.18)' : 'rgba(15, 23, 42, 0.24)'
        }}
      >
        <Typography variant="body2" color={hasWarning ? 'warning.main' : 'text.secondary'} fontWeight={hasWarning ? 820 : 650}>
          {hasWarning
            ? `Limited history: ${stat.sampleWarning} ${baseContext}`
            : `${baseContext}${alternativeContext}`}
        </Typography>
      </Box>
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

interface ChartDatum {
  date: string;
  event: string;
  value: number;
  valueLabel: string;
  line?: number;
  lineLabel: string;
  result: string;
}

function parseStatNumber(value?: string) {
  if (!value) return undefined;
  const parsed = Number.parseFloat(value.replace(/[^\d.-]/g, ''));
  return Number.isFinite(parsed) ? parsed : undefined;
}

function deriveResult(value: number, line?: number, _side: MarketOpportunity['side'] = 'Over') {
  if (line === undefined) return 'No line';
  return value >= line ? 'Cleared' : 'Missed';
}

function calculateHitRate(stat: PlayerResearchStat, selectedOffering: MarketOpportunity) {
  const graded = stat.log
    .map((row) => parseStatNumber(row.value))
    .filter((value): value is number => value !== undefined);

  if (!graded.length) return 'No sample';

  const clears = graded.filter((value) => deriveResult(value, selectedOffering.line, selectedOffering.side) === 'Cleared').length;
  return `${Math.round((clears / graded.length) * 100)}%`;
}

function sortCurrentLines(lines: MarketOpportunity[]) {
  return [...lines].sort((first, second) => {
    const priorityDelta = modifierPriority(first.projectionModifier) - modifierPriority(second.projectionModifier);
    return priorityDelta;
  });
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

function formatOfferingLabel(line: MarketOpportunity) {
  return `${line.provider} · ${line.propType} ${formatLineLabel(line)}`;
}

function formatLineNumber(line: number) {
  return Number.isInteger(line) ? String(line) : line.toFixed(1);
}

function resultColor(result?: string) {
  if (result === 'Cleared') return palette.success;
  if (result === 'Missed') return palette.danger;
  return palette.textMuted;
}
