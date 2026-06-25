import { Box, Stack, Typography, useMediaQuery, useTheme } from '@mui/material';
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
import type { PlayerResearchStat } from '../../data/mockPlayerResearch';
import { palette } from '../../theme/palette';
import type { PropHistoryResponseDto } from '../../api/types';
import type { MarketOpportunity } from '../../types/markets';
import { renderWarningText } from '../../utils/warnings';

interface PropHistoryCardProps {
  stat?: PlayerResearchStat;
  history?: PropHistoryResponseDto;
  selectedOffering: MarketOpportunity;
  allLines?: MarketOpportunity[];
}

interface PropHistoryView {
  label: string;
  selectedLine?: number | null;
  selectedSide?: MarketOpportunity['side'] | string | null;
  summaryMode?: 'default' | 'soccerPlayerHistory';
  last5Average: string;
  last10Average: string;
  seasonAverage: string;
  bestRecentResult: string;
  sampleSize?: string;
  sampleWarning?: string | null;
  log: PropHistoryLogRow[];
}

interface PropHistoryLogRow {
  date: string;
  event: string;
  value: string | number;
  result?: string;
  line?: number | null;
  side?: string | null;
  sortKey?: number;
}

export function PropHistoryCard({ stat, history, selectedOffering, allLines = [] }: PropHistoryCardProps) {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
  const historyView = history ? mapApiHistoryToView(history) : stat ? mapPlayerStatToView(stat) : null;

  if (!historyView) {
    return null;
  }

  const chartGameLimit = isMobile ? 5 : 10;
  const chartHistoryView = { ...historyView, log: getDisplayLog(historyView.log, chartGameLimit) };

  return (
    <Stack spacing={{ xs: 0.75, sm: 0.95 }} sx={{ height: '100%', minHeight: 0 }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'stretch', sm: 'flex-start' }} gap={{ xs: 0.55, sm: 1 }}>
        <Stack spacing={0.25}>
          <Typography variant="h5">Prop history</Typography>
          <Typography color="text.secondary" variant="body2">
            Recent stat values against the selected line.
          </Typography>
        </Stack>
        <Typography color="text.secondary" variant="body2" fontWeight={850} textAlign={{ xs: 'left', sm: 'right' }} sx={{ overflowWrap: 'anywhere' }}>
          {formatOfferingLabel(selectedOffering)}
        </Typography>
      </Stack>
      <PropHistoryBarChart stat={chartHistoryView} selectedOffering={selectedOffering} />
      <StatSummary stat={historyView} selectedOffering={selectedOffering} allLines={allLines} displayedLog={chartHistoryView.log} />
    </Stack>
  );
}

function PropHistoryBarChart({ stat, selectedOffering }: { stat: PropHistoryView; selectedOffering: MarketOpportunity }) {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
  const lineValue = typeof stat.selectedLine === 'number' ? stat.selectedLine : selectedOffering.line;
  const selectedSide = normalizeSide(stat.selectedSide) ?? selectedOffering.side;
  const data: ChartDatum[] = stat.log
    .map((row) => {
      const value = parseStatNumber(row.value) ?? 0;
      const rowLine = typeof row.line === 'number' ? row.line : lineValue;
      const rowSide = normalizeSide(row.side) ?? selectedSide;
      const result = normalizeResult(row.result) ?? deriveResult(value, rowLine, rowSide);
      return {
        date: row.date,
        event: row.event,
        value,
        valueLabel: formatStatValue(value),
        line: rowLine,
        lineLabel: `${rowSide} ${formatLineNumber(rowLine)}`,
        result
      };
    })
    .filter((row) => Number.isFinite(row.value));

  return (
    <Box
      sx={{
        width: '100%',
        height: { xs: 235, md: 280, xl: 300 },
        minHeight: { xs: 235, md: 280 },
        minWidth: 0,
        flex: { md: '1 1 auto' },
        flexShrink: 0,
        position: 'relative',
        zIndex: 0
      }}
    >
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={isMobile ? { top: 10, right: 6, bottom: 2, left: -8 } : { top: 12, right: 12, bottom: 2, left: -10 }}>
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
            width={isMobile ? 30 : 60}
            domain={[0, 'dataMax + 4']}
            allowDecimals
          />
          <Tooltip cursor={{ fill: 'rgba(125,211,252,.06)' }} content={<PropHistoryTooltip />} />
          {lineValue !== undefined ? (
            <ReferenceLine y={lineValue} stroke={palette.warning} strokeDasharray="5 5" strokeWidth={2.2} />
          ) : null}
          <Bar dataKey="value" radius={[8, 8, 3, 3]} maxBarSize={42}>
            {data.map((row) => (
              <Cell
                key={`${row.date}-${row.event}`}
                fill={
                  row.result === 'Cleared'
                    ? 'rgba(34,197,94,.82)'
                    : row.result === 'Missed'
                      ? 'rgba(244,63,94,.78)'
                      : 'rgba(148,163,184,.62)'
                }
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
        boxShadow: 'none'
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
  allLines,
  displayedLog
}: {
  stat: PropHistoryView;
  selectedOffering: MarketOpportunity;
  allLines: MarketOpportunity[];
  displayedLog: PropHistoryLogRow[];
}) {
  const hitRate = calculateHitRate(stat, selectedOffering);
  const displayedResults = displayedLog
    .map((row) => {
      const value = parseStatNumber(row.value);
      return value === undefined ? undefined : normalizeResult(row.result) ?? deriveResult(value, selectedOffering.line, selectedOffering.side);
    })
    .filter((result) => result === 'Cleared' || result === 'Missed');
  const displayedClears = displayedResults.filter((result) => result === 'Cleared').length;
  const alternatives = allLines.filter((line) => line.propType === selectedOffering.propType && line.id !== selectedOffering.id);
  const alternativeProviders = Array.from(new Set(alternatives.map((line) => line.provider))).slice(0, 3);
  const metrics = stat.summaryMode === 'soccerPlayerHistory'
    ? [
        ['Sample', stat.sampleSize ?? hitRate],
        ['Latest', stat.bestRecentResult],
        ['Average', stat.seasonAverage],
        ['Last 5', stat.last5Average],
        ['Last 10', stat.last10Average]
      ]
    : [
        ['Last 5', stat.last5Average],
        ['Last 10', stat.last10Average],
        ['Season', stat.seasonAverage],
        ['Hit rate', hitRate],
        ['Best', stat.bestRecentResult]
      ];
  const warning = renderWarningText(stat.sampleWarning);
  const hasWarning = Boolean(warning);
  const displayedGameCount = displayedResults.length;
  const shownGameLabel = displayedGameCount === 1 ? 'shown game' : 'shown games';
  const baseContext = `${selectedOffering.provider} ${formatLineLabel(selectedOffering)}: last 5 avg ${stat.last5Average} vs selected line ${formatLineNumber(
    selectedOffering.line
  )}. Cleared ${displayedClears} of ${displayedGameCount} ${shownGameLabel}.`;
  const alternativeContext = alternativeProviders.length ? ` Alternatives listed from ${alternativeProviders.join(', ')}.` : '';

  return (
    <Stack spacing={{ xs: 0.6, sm: 0.75 }}>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(5, max-content)' },
          justifyContent: { xs: 'stretch', sm: 'start' },
          alignItems: 'center',
          px: { xs: 0.95, sm: 1.3 },
          py: { xs: 0.8, sm: 0.85 },
          columnGap: { xs: 0.95, sm: 0 },
          rowGap: { xs: 0.65, sm: 0.85 },
          border: '1px solid rgba(148,163,184,.16)',
          borderRadius: 3,
          bgcolor: palette.cardSurface,
          boxShadow: 'none',
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
          bgcolor: hasWarning ? 'rgba(54, 38, 13, 0.18)' : palette.cardSurface
        }}
      >
        <Typography variant="body2" color={hasWarning ? 'warning.main' : 'text.secondary'} fontWeight={hasWarning ? 820 : 650}>
          {hasWarning ? `Limited history: ${warning}. ${baseContext}` : `${baseContext}${alternativeContext}`}
        </Typography>
      </Box>
    </Stack>
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

function parseStatNumber(value?: string | number) {
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined;
  if (!value) return undefined;
  const parsed = Number.parseFloat(value.replace(/[^\d.-]/g, ''));
  return Number.isFinite(parsed) ? parsed : undefined;
}

function deriveResult(value: number, line?: number, side: MarketOpportunity['side'] = 'Over') {
  if (line === undefined) return 'No line';
  if (value === line) return 'Push';
  if (side === 'Under') return value < line ? 'Cleared' : 'Missed';
  return value > line ? 'Cleared' : 'Missed';
}

function calculateHitRate(stat: PropHistoryView, selectedOffering: MarketOpportunity) {
  const graded = stat.log
    .map((row) => {
      const value = parseStatNumber(row.value);
      return value === undefined ? undefined : normalizeResult(row.result) ?? deriveResult(value, selectedOffering.line, selectedOffering.side);
    })
    .filter((result) => result === 'Cleared' || result === 'Missed');

  if (!graded.length) return 'No sample';

  const clears = graded.filter((result) => result === 'Cleared').length;
  return `${Math.round((clears / graded.length) * 100)}%`;
}

function getDisplayLog(log: PropHistoryLogRow[], limit: number) {
  if (log.length <= limit) return log;

  const sortable = log.every((row) => typeof row.sortKey === 'number' && Number.isFinite(row.sortKey));
  const orderedLog = sortable ? [...log].sort((a, b) => (a.sortKey ?? 0) - (b.sortKey ?? 0)) : log;

  return orderedLog.slice(-limit);
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

function normalizeResult(value?: string) {
  if (!value) return undefined;
  const normalized = value.toLowerCase();
  if (normalized === 'cleared' || normalized === 'hit' || normalized === 'won') return 'Cleared';
  if (normalized === 'missed' || normalized === 'loss' || normalized === 'lost') return 'Missed';
  if (normalized === 'push' || normalized === 'pushed' || normalized === 'void') return 'Push';
  return undefined;
}

function mapPlayerStatToView(stat: PlayerResearchStat): PropHistoryView {
  return {
    label: stat.label,
    last5Average: stat.last5Average,
    last10Average: stat.last10Average,
    seasonAverage: stat.seasonAverage,
    bestRecentResult: stat.bestRecentResult,
    sampleSize: undefined,
    sampleWarning: stat.sampleWarning,
    log: stat.log.map((row) => ({
      date: row.date,
      event: row.event,
      value: row.value
    }))
  };
}

function mapApiHistoryToView(history: PropHistoryResponseDto): PropHistoryView {
  const summary = history.summary;
  return {
    label: history.displayPropType ?? history.propType ?? 'Prop',
    selectedLine: history.line,
    selectedSide: history.side,
    summaryMode: history.diagnostics?.lookupMode === 'soccerPlayerHistorySummary' ? 'soccerPlayerHistory' : 'default',
    last5Average: formatSummaryNumber(summary?.last5Average),
    last10Average: formatSummaryNumber(summary?.last10Average),
    seasonAverage: formatSummaryNumber(summary?.seasonAverage),
    bestRecentResult: summary?.bestLabel ?? summary?.bestRecentResult ?? 'No recent result',
    sampleSize: summary?.sampleSize === undefined ? undefined : String(summary.sampleSize),
    sampleWarning: history.warning,
    log: (history.games ?? []).map((game) => ({
      date: game.dateLabel ?? formatDateLabel(game.date),
      event: game.gameLabel ?? game.event ?? 'MLB game',
      value: game.value ?? 0,
      result: game.result,
      line: game.selectedLine ?? game.line ?? history.line,
      side: game.side ?? history.side,
      sortKey: parseDateSortKey(game.date)
    }))
  };
}

function normalizeSide(value?: string | null): MarketOpportunity['side'] | undefined {
  if (!value) return undefined;
  return value.toLowerCase() === 'under' || value.toLowerCase() === 'lower' ? 'Under' : 'Over';
}

function formatStatValue(value: number) {
  return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/\.?0+$/, '');
}

function formatSummaryNumber(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '-';
  return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/\.?0+$/, '');
}

function formatDateLabel(value?: string) {
  if (!value) return 'Game';
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' }).format(date);
}

function parseDateSortKey(value?: string) {
  if (!value) return undefined;
  const parsed = Date.parse(`${value}T00:00:00`);
  return Number.isFinite(parsed) ? parsed : undefined;
}
