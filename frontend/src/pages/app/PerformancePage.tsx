import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import { Accordion, AccordionDetails, AccordionSummary, Box, ButtonBase, Collapse, IconButton, Stack, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { PerformanceMobileAccordion } from '../../components/performance/PerformanceMobileAccordion';
import { PerformanceSummaryCards } from '../../components/performance/PerformanceSummaryCards';
import { PerformanceTable } from '../../components/performance/PerformanceTable';
import { AppCard } from '../../components/ui/AppCard';
import { AppDataTable } from '../../components/ui/AppDataTable';
import { AppTablePagination } from '../../components/ui/AppTablePagination';
import { appTableRowSx } from '../../components/ui/appTableStyles';
import { AppTableEvValue, AppTableMetricValue, AppTablePrimaryText, AppTableSecondaryText } from '../../components/ui/AppTableTypography';
import { PageHeader } from '../../components/ui/PageHeader';
import { usePerformanceData, usePerformanceResults } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { palette } from '../../theme/palette';
import type { PerformanceResultRowDto } from '../../api/types';
import { formatPercent } from '../../utils/formatters';
import { Fragment, useEffect, useMemo, useState } from 'react';

const breakdownTabs = [
  { label: 'Overview', types: ['Provider + Prop Type'] },
  { label: 'Providers', types: ['Provider'] },
  { label: 'Prop Types', types: ['Prop Type'] }
];

const yesterdayDesktopPageSize = 10;
const yesterdayMobilePageSize = 10;
const overallHistoryPageSize = 10;

type YesterdayResultGroup = {
  id: string;
  result: string;
  playerName: string;
  propType: string;
  side: string;
  actualValue: number | null;
  bestEv: number | null;
  distinctLineCount: number;
  rows: PerformanceResultRowDto[];
};

export function PerformancePage() {
  const { isMobile } = useResponsiveView();
  const [selectedTab, setSelectedTab] = useState(breakdownTabs[0].label);
  const [historyPage, setHistoryPage] = useState(1);
  const performanceQuery = usePerformanceData('allHistory');
  const resultsQuery = usePerformanceResults();
  const performanceRows = performanceQuery.rows;
  const hasGradedSample = (performanceQuery.summary?.dataAvailability?.gradedPicks ?? performanceRows.find((row) => row.groupType === 'Overall')?.gradedPicks ?? 0) > 0;
  const activeTab = breakdownTabs.find((tab) => tab.label === selectedTab) ?? breakdownTabs[0];
  const visibleRows = useMemo(
    () => performanceRows.filter((row) => activeTab.types.includes(row.groupType)),
    [activeTab.types, performanceRows]
  );
  const historyPageCount = Math.max(1, Math.ceil(visibleRows.length / overallHistoryPageSize));
  const currentHistoryPage = Math.min(historyPage, historyPageCount);
  const paginatedHistoryRows = visibleRows.slice((currentHistoryPage - 1) * overallHistoryPageSize, currentHistoryPage * overallHistoryPageSize);

  useEffect(() => {
    setHistoryPage(1);
  }, [activeTab.label, visibleRows.length]);

  return (
    <Stack spacing={3} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader title="Model Performance" description="Review how model-tracked picks performed after games were graded." />

      <Stack spacing={1.5}>
        <Stack spacing={0.4}>
          <Typography variant="h5" fontWeight={950}>
            Yesterday's Results
          </Typography>
          <Typography color="text.secondary">
            Picks from the latest graded slate.
          </Typography>
        </Stack>
        <YesterdayResultsSection isMobile={isMobile} loading={resultsQuery.loading} error={Boolean(resultsQuery.error)} data={resultsQuery.data} />
      </Stack>

      <Stack spacing={1.5}>
        <Stack spacing={0.4}>
          <Typography variant="h5" fontWeight={950}>
            Overall History
          </Typography>
          <Typography color="text.secondary">
            Longer-term results grouped by provider and prop type.
          </Typography>
        </Stack>

      {performanceQuery.loading ? (
        <ApiStateCard message="Loading performance summary..." />
      ) : performanceQuery.error ? (
        <ApiStateCard tone="error" message="Performance summary could not be loaded from the market API." />
      ) : performanceRows.length && hasGradedSample ? (
        <PerformanceSummaryCards rows={performanceRows} />
      ) : (
        <CleanBaselineCard />
      )}
      </Stack>

      <Stack spacing={1.5}>
        <Stack spacing={0.4}>
          <Typography variant="h5" fontWeight={950}>
            History breakdowns
          </Typography>
          <Typography color="text.secondary">
            Compare graded picks by provider and prop type.
          </Typography>
        </Stack>

        <Box
          sx={{
            display: 'flex',
            gap: 1,
            overflowX: 'auto',
            WebkitOverflowScrolling: 'touch',
            mx: { xs: -0.25, md: 0 },
            px: { xs: 0.25, md: 0 },
            pb: { xs: 1.15, md: 1 },
            borderBottom: `1px solid ${palette.border}`,
            scrollbarWidth: 'none',
            msOverflowStyle: 'none',
            '&::-webkit-scrollbar': { display: 'none' }
          }}
        >
          {breakdownTabs.map((tab) => {
            const selected = tab.label === selectedTab;
            return (
              <ButtonBase
                key={tab.label}
                onClick={() => setSelectedTab(tab.label)}
                sx={{
                  px: 1.6,
                  py: { xs: 0.8, md: 0.95 },
                  flex: '0 0 auto',
                  borderRadius: 2,
                  border: `1px solid ${selected ? palette.borderStrong : palette.border}`,
                  color: selected ? palette.text : palette.textMuted,
                  background: selected ? 'rgba(34, 211, 238, 0.09)' : 'transparent',
                  fontWeight: 900,
                  fontSize: { xs: 12.5, md: 13 },
                  userSelect: 'none',
                  transition: 'border-color .16s ease, background .16s ease, color .16s ease',
                  '&:hover': {
                    borderColor: palette.borderStrong,
                    background: selected ? 'rgba(34, 211, 238, 0.11)' : 'rgba(148, 163, 184, 0.06)'
                  }
                }}
              >
                {tab.label}
              </ButtonBase>
            );
          })}
        </Box>

        {performanceQuery.loading || performanceQuery.error || !performanceRows.length || !hasGradedSample ? (
          <ApiStateCard message={performanceQuery.loading ? 'Loading breakdowns...' : 'No graded history yet.'} />
        ) : !visibleRows.length ? (
          <ApiStateCard message="No graded results are available for this view yet." />
        ) : isMobile ? (
          <PerformanceMobileAccordion rows={paginatedHistoryRows} category={selectedTab} />
        ) : (
          <PerformanceTable rows={paginatedHistoryRows} category={selectedTab} />
        )}
        {!performanceQuery.loading && !performanceQuery.error && performanceRows.length && hasGradedSample && visibleRows.length ? (
          <AppTablePagination count={visibleRows.length} page={currentHistoryPage} pageSize={overallHistoryPageSize} itemLabel="groups" onChange={setHistoryPage} />
        ) : null}
      </Stack>

    </Stack>
  );
}

function YesterdayResultsSection({
  isMobile,
  loading,
  error,
  data
}: {
  isMobile: boolean;
  loading: boolean;
  error: boolean;
  data: ReturnType<typeof usePerformanceResults>['data'];
}) {
  const [page, setPage] = useState(1);
  const rows = data?.rows ?? [];
  const groups = useMemo(() => groupYesterdayResults(rows), [rows]);
  const pageSize = isMobile ? yesterdayMobilePageSize : yesterdayDesktopPageSize;
  const pageCount = Math.max(1, Math.ceil(groups.length / pageSize));
  const currentPage = Math.min(page, pageCount);
  const visibleGroups = groups.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const summary = data?.summary;
  const noResultExcluded = summary?.noResultExcluded ?? 0;

  useEffect(() => {
    setPage(1);
  }, [data?.date, rows.length, isMobile]);

  if (loading) {
    return <ApiStateCard message="Loading yesterday's results..." />;
  }

  if (error) {
    return <ApiStateCard tone="error" message="Yesterday's results could not be loaded from the market API." />;
  }

  if (!rows.length) {
    return (
      <AppCard>
        <Stack spacing={0.65}>
          <Typography variant="h6" fontWeight={950}>
            No graded results for yesterday yet.
          </Typography>
          <Typography color="text.secondary">
            Results will appear after completed games are imported and matched.
          </Typography>
        </Stack>
      </AppCard>
    );
  }

  return (
    <Stack spacing={1.5}>
      <YesterdaySummaryCards summary={summary} isMobile={isMobile} />
      {noResultExcluded > 0 ? (
        <Typography variant="body2" color="text.secondary">
          {noResultExcluded} provider {noResultExcluded === 1 ? 'line had' : 'lines had'} no player result and {noResultExcluded === 1 ? 'was' : 'were'} excluded from hit rate and simulated ROI.
        </Typography>
      ) : null}
      {isMobile ? <YesterdayResultsMobileCards groups={visibleGroups} /> : <YesterdayResultsTable groups={visibleGroups} />}
      <AppTablePagination count={groups.length} page={currentPage} pageSize={pageSize} itemLabel="result groups" onChange={setPage} />
    </Stack>
  );
}

function YesterdaySummaryCards({ summary, isMobile }: { summary?: NonNullable<ReturnType<typeof usePerformanceResults>['data']>['summary']; isMobile: boolean }) {
  const cards = [
    { label: isMobile ? 'W-L-P' : 'Yesterday W-L-P', value: `${summary?.won ?? 0}-${summary?.lost ?? 0}-${summary?.pushed ?? 0}`, helper: isMobile ? formatShortRecord(summary?.won ?? 0, summary?.lost ?? 0, summary?.pushed ?? 0) : formatRecord(summary?.won ?? 0, summary?.lost ?? 0, summary?.pushed ?? 0) },
    { label: isMobile ? 'Hit Rate' : 'Yesterday Hit Rate', value: summary?.hitRate == null ? '-' : formatPercent(summary.hitRate), helper: 'Pushes excluded' },
    { label: isMobile ? 'Simulated ROI' : 'Yesterday Simulated ROI', value: summary?.simulatedRoi == null ? '-' : formatPercent(summary.simulatedRoi, { signed: true }), helper: 'Flat 1-unit tracking', tone: (summary?.simulatedRoi ?? 0) >= 0 ? 'good' : 'bad' },
    { label: 'Graded Results', value: `${summary?.gradedPicks ?? 0}`, helper: 'Provider lines graded' },
    { label: 'Excluded', value: `${summary?.noResultExcluded ?? 0}`, helper: 'No player result found' }
  ].filter((card) => !isMobile || card.label !== 'Excluded');

  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(5, minmax(0, 1fr))' }, gap: { xs: 1, md: 1.5 } }}>
      {cards.map((card) => (
        <AppCard key={card.label} contentSx={{ p: { xs: 1.1, md: 1.6 }, '&:last-child': { pb: { xs: 1.1, md: 1.6 } } }}>
          <Stack spacing={{ xs: 0.3, md: 0.45 }}>
            <Typography variant="caption" color="text.secondary" fontWeight={850} textTransform="uppercase" sx={{ lineHeight: 1.15 }}>
              {card.label}
            </Typography>
            <Typography fontWeight={950} sx={{ fontSize: { xs: 20, md: 24 }, color: card.tone === 'good' ? palette.success : card.tone === 'bad' ? palette.danger : palette.text }}>
              {card.value}
            </Typography>
            <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.2 }}>
              {card.helper}
            </Typography>
          </Stack>
        </AppCard>
      ))}
    </Box>
  );
}

function formatRecord(wins: number, losses: number, pushes: number) {
  return `${wins} wins · ${losses} losses · ${pushes} pushes`;
}

function formatShortRecord(wins: number, losses: number, pushes: number) {
  return `${wins}W · ${losses}L · ${pushes}P`;
}

function YesterdayResultsTable({ groups }: { groups: YesterdayResultGroup[] }) {
  const [expandedGroupId, setExpandedGroupId] = useState<string | null>(null);

  return (
    <AppDataTable minWidth={980}>
      <TableHead>
        <TableRow>
          <TableCell>Result</TableCell>
          <TableCell>Player / Prop</TableCell>
          <TableCell>Pick</TableCell>
          <TableCell align="center">Final Stat</TableCell>
          <TableCell align="center">Lines</TableCell>
          <TableCell>Providers</TableCell>
          <TableCell align="right">Best Edge</TableCell>
          <TableCell align="center" aria-label="Expand result details" />
        </TableRow>
      </TableHead>
      <TableBody>
        {groups.map((group) => {
          const expanded = expandedGroupId === group.id;

          return (
            <Fragment key={group.id}>
              <TableRow key={group.id} hover sx={appTableRowSx}>
                <TableCell><ResultBadge result={group.result} /></TableCell>
                <TableCell>
                  <Stack spacing={0.25}>
                    <AppTablePrimaryText noWrap={false}>{group.playerName}</AppTablePrimaryText>
                    <AppTableSecondaryText noWrap={false}>{group.propType}</AppTableSecondaryText>
                  </Stack>
                </TableCell>
                <TableCell><AppTableMetricValue>{group.side}</AppTableMetricValue></TableCell>
                <TableCell align="center"><AppTableMetricValue>{formatNumber(group.actualValue)}</AppTableMetricValue></TableCell>
                <TableCell align="center"><AppTableMetricValue>{formatTargetLineCount(group.distinctLineCount)}</AppTableMetricValue></TableCell>
                <TableCell><AppTableMetricValue>{formatProviderCount(group.rows.length)}</AppTableMetricValue></TableCell>
                <TableCell align="right">
                  {group.bestEv == null ? (
                    <AppTableMetricValue>-</AppTableMetricValue>
                  ) : (
                    <AppTableEvValue positive={group.bestEv >= 0}>{formatPercent(group.bestEv, { signed: true })}</AppTableEvValue>
                  )}
                </TableCell>
                <TableCell align="center">
                  <IconButton
                    aria-label={expanded ? 'Collapse provider result details' : 'Expand provider result details'}
                    size="small"
                    onClick={() => setExpandedGroupId(expanded ? null : group.id)}
                    sx={{
                      color: 'text.secondary',
                      border: `1px solid ${palette.border}`,
                      width: 30,
                      height: 30,
                      '&:hover': {
                        color: 'text.primary',
                        borderColor: palette.borderStrong,
                        backgroundColor: 'rgba(34,211,238,.08)'
                      }
                    }}
                  >
                    <ExpandMoreRoundedIcon sx={{ fontSize: 19, transform: expanded ? 'rotate(180deg)' : 'none', transition: 'transform .16s ease' }} />
                  </IconButton>
                </TableCell>
              </TableRow>
              <TableRow key={`${group.id}-details`}>
                <TableCell colSpan={8} sx={{ p: 0, borderColor: 'rgba(148,163,184,.12)', backgroundColor: 'rgba(8, 13, 25, 0.72)' }}>
                  <Collapse in={expanded} timeout="auto" unmountOnExit>
                    <ProviderResultDetails rows={group.rows} />
                  </Collapse>
                </TableCell>
              </TableRow>
            </Fragment>
          );
        })}
      </TableBody>
    </AppDataTable>
  );
}

function ProviderResultDetails({ rows }: { rows: PerformanceResultRowDto[] }) {
  return (
    <Box sx={{ px: { xs: 1.25, md: 2 }, py: 1.15 }}>
      <Stack spacing={0.85}>
        <Typography variant="caption" color="text.secondary" fontWeight={900}>
          Target lines by provider
        </Typography>
        <Box
          sx={{
            maxWidth: 720,
            border: `1px solid ${palette.border}`,
            borderRadius: 2,
            overflow: 'hidden',
            backgroundColor: 'rgba(15, 23, 42, 0.34)'
          }}
        >
          <Box
            component="table"
            sx={{
              width: '100%',
              borderCollapse: 'collapse',
              tableLayout: 'fixed'
            }}
          >
            <Box component="thead">
              <Box component="tr">
                {['Target', 'Result', 'Provider', 'EV'].map((label) => (
                  <Box
                    key={label}
                    component="th"
                    sx={{
                      px: 1,
                      py: 0.65,
                      borderBottom: `1px solid ${palette.border}`,
                      borderLeft: label === 'Target' ? 'none' : `1px solid ${palette.border}`,
                      color: palette.textMuted,
                      fontSize: 12,
                      fontWeight: 900,
                      textAlign: 'left'
                    }}
                  >
                    {label}
                  </Box>
                ))}
              </Box>
            </Box>
            <Box component="tbody">
              {rows.map((row) => (
                <ProviderResultDetailRow key={row.recommendationSnapshotId} row={row} />
              ))}
            </Box>
          </Box>
        </Box>
      </Stack>
    </Box>
  );
}

function ProviderResultDetailRow({ row }: { row: PerformanceResultRowDto }) {
  return (
    <Box component="tr">
      <Box component="td" sx={{ px: 1, py: 0.72, borderTop: `1px solid ${palette.border}` }}>
        <AppTableMetricValue>{row.side} {formatNumber(row.line)}</AppTableMetricValue>
      </Box>
      <Box component="td" sx={{ px: 1, py: 0.72, borderTop: `1px solid ${palette.border}`, borderLeft: `1px solid ${palette.border}` }}>
        <CompactResultLabel result={row.result} />
      </Box>
      <Box component="td" sx={{ px: 1, py: 0.72, borderTop: `1px solid ${palette.border}`, borderLeft: `1px solid ${palette.border}` }}>
        <AppTableMetricValue noWrap={false}>{row.provider}</AppTableMetricValue>
      </Box>
      <Box component="td" sx={{ px: 1, py: 0.72, borderTop: `1px solid ${palette.border}`, borderLeft: `1px solid ${palette.border}`, textAlign: 'left' }}>
        <AppTableEvValue positive={(row.estimatedEv ?? 0) >= 0}>{row.estimatedEv == null ? '-' : formatPercent(row.estimatedEv, { signed: true })}</AppTableEvValue>
      </Box>
    </Box>
  );
}

function YesterdayResultsMobileCards({ groups }: { groups: YesterdayResultGroup[] }) {
  return (
    <Stack
      spacing={0}
      sx={{
        gap: 1.25
      }}
    >
      {groups.map((group) => (
        <Accordion
          key={group.id}
          disableGutters
          sx={{
            border: `1px solid ${palette.border}`,
            borderRadius: 3,
            backgroundColor: palette.cardSurface,
            overflow: 'hidden',
            margin: '0 !important',
            '&:before': { display: 'none' },
            '&.Mui-expanded': {
              margin: '0 !important',
              borderRadius: 3
            },
            '&:first-of-type': {
              borderTopLeftRadius: 12,
              borderTopRightRadius: 12
            }
          }}
        >
          <AccordionSummary
            sx={{
              position: 'relative',
              minHeight: 'auto',
              p: 1.35,
              borderRadius: 3,
              '&.Mui-expanded': {
                minHeight: 'auto'
              },
              '& .MuiAccordionSummary-content': {
                m: 0,
                minWidth: 0,
                '&.Mui-expanded': {
                  m: 0
                }
              },
              '&.Mui-expanded .mobile-result-chevron': {
                transform: 'rotate(180deg)'
              }
            }}
          >
            <Box sx={{ position: 'absolute', top: 10, right: 10, zIndex: 1 }}>
              <ResultBadge result={group.result} />
            </Box>
            <Stack spacing={0.75} sx={{ minWidth: 0, width: '100%', pr: 0 }}>
              <Stack direction="row" alignItems="flex-start" gap={1}>
                <Stack spacing={0.25} sx={{ minWidth: 0, pr: 8.5 }}>
                  <Typography fontWeight={950}>{group.playerName}</Typography>
                  <Typography color="text.secondary" fontSize={13}>{group.side} · {group.propType}</Typography>
                </Stack>
              </Stack>
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr)) auto', columnGap: 1.4, rowGap: 0.72, alignItems: 'center' }}>
                <MobileInlineMetric label="Final Stat" value={formatNumber(group.actualValue)} />
                <MobileInlineMetric label="Lines" value={formatTargetLineCount(group.distinctLineCount)} />
                <MobileInlineMetric label="Providers" value={formatProviderCount(group.rows.length)} />
                <MobileInlineMetric label="Best Edge" value={group.bestEv == null ? '-' : formatPercent(group.bestEv, { signed: true })} tone={(group.bestEv ?? 0) >= 0 ? 'good' : 'bad'} />
                <Box
                  className="mobile-result-chevron"
                  sx={{
                    gridColumn: '3',
                    gridRow: '1 / span 2',
                    justifySelf: 'end',
                    alignSelf: 'center',
                    color: 'text.secondary',
                    display: 'inline-flex',
                    transition: 'transform .16s ease'
                  }}
                >
                  <ExpandMoreRoundedIcon sx={{ fontSize: 22 }} />
                </Box>
              </Box>
            </Stack>
          </AccordionSummary>
          <AccordionDetails sx={{ pt: 0, px: 2, pb: 1.6 }}>
            <Stack spacing={0.8}>
              <Typography variant="caption" color="text.secondary" fontWeight={900}>
                Provider lines
              </Typography>
              {group.rows.map((row) => (
                <Box
                  key={row.recommendationSnapshotId}
                  sx={{
                    borderTop: `1px solid ${palette.border}`,
                    pt: 0.75
                  }}
                >
                  <Box
                    sx={{
                      display: 'grid',
                      gridTemplateColumns: 'minmax(0, 1fr) auto',
                      columnGap: 1,
                      rowGap: 0.25,
                      alignItems: 'center'
                    }}
                  >
                    <Typography fontSize={13} fontWeight={900} color="text.primary" noWrap>
                      {row.side} {formatNumber(row.line)}
                    </Typography>
                    <CompactResultText result={row.result} compact />
                    <Typography fontSize={13} fontWeight={850} color="text.secondary" noWrap>
                      {row.provider}
                    </Typography>
                    <Typography
                      fontSize={13}
                      fontWeight={900}
                      textAlign="right"
                      sx={{ color: (row.estimatedEv ?? 0) >= 0 ? palette.success : palette.danger }}
                      noWrap
                    >
                      {row.estimatedEv == null ? '-' : formatPercent(row.estimatedEv, { signed: true })}
                    </Typography>
                  </Box>
                </Box>
              ))}
            </Stack>
          </AccordionDetails>
        </Accordion>
      ))}
    </Stack>
  );
}

function MobileInlineMetric({ label, value, tone }: { label: string; value: string; tone?: 'good' | 'bad' }) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography component="div" variant="caption" color="text.secondary" fontWeight={850} sx={{ lineHeight: 1.15 }}>{label}</Typography>
      <Typography component="div" fontWeight={900} sx={{ color: tone === 'good' ? palette.success : tone === 'bad' ? palette.danger : palette.text }} noWrap>
        {value}
      </Typography>
    </Box>
  );
}

function ResultBadge({ result }: { result: string }) {
  const color = result === 'Won' ? palette.success : result === 'Lost' ? palette.danger : palette.textMuted;
  return (
    <Box sx={{ display: 'inline-flex', alignItems: 'center', border: `1px solid ${color}`, color, borderRadius: 999, px: 1, py: 0.35, fontSize: 12, fontWeight: 950 }}>
      {result}
    </Box>
  );
}

function CompactResultLabel({ result }: { result: string }) {
  const color = getResultColor(result);
  return (
    <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.55, color, fontSize: 12, fontWeight: 900 }}>
      <Box component="span" sx={{ width: 6, height: 6, borderRadius: 999, backgroundColor: color, flex: '0 0 auto' }} />
      {result}
    </Box>
  );
}

function CompactResultText({ result, compact = false }: { result: string; compact?: boolean }) {
  return (
    <Box component="span" sx={{ color: getResultColor(result), fontWeight: compact ? 700 : 900, fontSize: compact ? 13 : 'inherit', lineHeight: 1.2 }}>
      {result}
    </Box>
  );
}

function getResultColor(result: string) {
  if (result === 'Won') return palette.success;
  if (result === 'Lost') return palette.danger;
  return palette.textMuted;
}

function groupYesterdayResults(rows: PerformanceResultRowDto[]) {
  const groups = new Map<string, YesterdayResultGroup>();

  rows.forEach((row) => {
    const key = [
      normalizeGroupPart(row.playerName),
      normalizeGroupPart(row.propType),
      normalizeGroupPart(row.side),
      row.actualValue == null ? 'null' : formatNumber(row.actualValue),
    ].join('|');

    const existing = groups.get(key);
    if (existing) {
      existing.rows.push(row);
      existing.result = getGroupResult(existing.rows);
      existing.bestEv = getBestEv(existing.rows);
      existing.distinctLineCount = getDistinctLineCount(existing.rows);
      return;
    }

    groups.set(key, {
      id: key,
      result: row.result,
      playerName: row.playerName,
      propType: row.propType,
      side: row.side,
      actualValue: row.actualValue,
      bestEv: row.estimatedEv ?? null,
      distinctLineCount: 1,
      rows: [row]
    });
  });

  return Array.from(groups.values())
    .map((group) => ({
      ...group,
      rows: [...group.rows].sort((a, b) => (b.estimatedEv ?? Number.NEGATIVE_INFINITY) - (a.estimatedEv ?? Number.NEGATIVE_INFINITY)),
      bestEv: getBestEv(group.rows),
      distinctLineCount: getDistinctLineCount(group.rows),
      result: getGroupResult(group.rows)
    }))
    .sort(sortYesterdayGroups);
}

function sortYesterdayGroups(a: YesterdayResultGroup, b: YesterdayResultGroup) {
  const aTime = getLatestEventTime(a.rows);
  const bTime = getLatestEventTime(b.rows);
  if (aTime !== bTime) return bTime - aTime;

  const evDelta = (b.bestEv ?? Number.NEGATIVE_INFINITY) - (a.bestEv ?? Number.NEGATIVE_INFINITY);
  if (evDelta !== 0) return evDelta;

  return a.playerName.localeCompare(b.playerName);
}

function getLatestEventTime(rows: PerformanceResultRowDto[]) {
  return Math.max(
    ...rows.map((row) => {
      if (!row.eventStartTimeUtc) return 0;
      const parsed = Date.parse(row.eventStartTimeUtc);
      return Number.isNaN(parsed) ? 0 : parsed;
    })
  );
}

function getBestEv(rows: PerformanceResultRowDto[]) {
  const evs = rows.map((row) => row.estimatedEv).filter((value): value is number => typeof value === 'number');
  return evs.length ? Math.max(...evs) : null;
}

function normalizeGroupPart(value?: string | null) {
  return (value ?? '').trim().toLowerCase();
}

function getDistinctLineCount(rows: PerformanceResultRowDto[]) {
  return new Set(rows.map((row) => formatNumber(row.line))).size;
}

function getGroupResult(rows: PerformanceResultRowDto[]) {
  const uniqueResults = new Set(rows.map((row) => row.result));
  return uniqueResults.size === 1 ? rows[0]?.result ?? '-' : 'Mixed';
}

function formatProviderCount(count: number) {
  return `${count} ${count === 1 ? 'provider' : 'providers'}`;
}

function formatTargetLineCount(count: number) {
  return `${count} target ${count === 1 ? 'line' : 'lines'}`;
}

function formatNumber(value?: number | null) {
  if (value === null || value === undefined) return '-';
  return Number(value).toFixed(2).replace(/0+$/, '').replace(/\.$/, '');
}

function CleanBaselineCard() {
  return (
    <AppCard>
      <Stack spacing={0.65}>
        <Typography variant="h6" fontWeight={950}>
          No graded picks yet.
        </Typography>
        <Typography color="text.secondary">
          New recommendations are being tracked. Results will appear after completed slates are imported and matched.
        </Typography>
      </Stack>
    </AppCard>
  );
}

function ApiStateCard({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <AppCard sx={{ borderColor: tone === 'error' ? 'rgba(244,63,94,.32)' : undefined }}>
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </AppCard>
  );
}
