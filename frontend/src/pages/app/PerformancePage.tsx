import { Box, ButtonBase, Stack, Typography } from '@mui/material';
import { PerformanceMobileAccordion } from '../../components/performance/PerformanceMobileAccordion';
import { PerformanceSummaryCards } from '../../components/performance/PerformanceSummaryCards';
import { PerformanceTable } from '../../components/performance/PerformanceTable';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { usePerformanceData } from '../../hooks/useMarketData';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { palette } from '../../theme/palette';
import { useMemo, useState } from 'react';

const breakdownTabs = [
  { label: 'Overview', types: ['Overall', 'Provider + Sport'] },
  { label: 'Providers', types: ['Provider'] },
  { label: 'Sports', types: ['Sport'] },
  { label: 'Prop Types', types: ['Prop Type'] },
  { label: 'Confidence', types: ['Confidence'] },
  { label: 'Strategy', types: ['Strategy'] }
];

export function PerformancePage() {
  const { isMobile } = useResponsiveView();
  const [selectedTab, setSelectedTab] = useState(breakdownTabs[0].label);
  const performanceQuery = usePerformanceData();
  const performanceRows = performanceQuery.rows;
  const pendingCurrentSlatePicks = performanceQuery.summary?.dataAvailability?.currentSlatePendingPicks ?? 0;
  const activeTab = breakdownTabs.find((tab) => tab.label === selectedTab) ?? breakdownTabs[0];
  const visibleRows = useMemo(
    () => performanceRows.filter((row) => activeTab.types.includes(row.groupType)),
    [activeTab.types, performanceRows]
  );

  return (
    <Stack spacing={3} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader title="Performance" description="Review historical candidate results with sample-size context." />

      <AppCard
        sx={{
          borderColor: 'rgba(245, 158, 11, 0.34)',
          backgroundColor: 'rgba(60, 42, 18, 0.72)'
        }}
        contentSx={{ py: { xs: 2, md: 2.25 } }}
      >
        <Stack spacing={0.7}>
          <Typography variant="h6" fontWeight={950}>
            Performance is sample-size dependent
          </Typography>
          <Typography color="text.secondary">
            Treat early results as research, not proof. Historical groups with limited graded picks need more context before
            they should influence an entry workflow.
          </Typography>
        </Stack>
      </AppCard>

      {performanceQuery.loading ? (
        <ApiStateCard message="Loading performance summary..." />
      ) : performanceQuery.error ? (
        <ApiStateCard tone="error" message="Performance summary could not be loaded from the market API." />
      ) : performanceRows.length ? (
        <PerformanceSummaryCards rows={performanceRows} />
      ) : (
        <ApiStateCard
          message={
            pendingCurrentSlatePicks > 0
              ? `No graded recommendations yet. Today: ${pendingCurrentSlatePicks} candidates pending results.`
              : 'No graded performance summary is available yet.'
          }
        />
      )}

      <Stack spacing={1.5}>
        <Stack spacing={0.4}>
          <Typography variant="h5" fontWeight={950}>
            Performance breakdowns
          </Typography>
          <Typography color="text.secondary">
            Compare historical candidate results by one category at a time.
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

        {performanceQuery.loading || performanceQuery.error || !performanceRows.length ? (
          <ApiStateCard message={performanceQuery.loading ? 'Loading breakdowns...' : 'No performance breakdown rows are available.'} />
        ) : isMobile ? (
          <PerformanceMobileAccordion rows={visibleRows} category={selectedTab} />
        ) : (
          <PerformanceTable rows={visibleRows} category={selectedTab} />
        )}
      </Stack>

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' },
          gap: 2
        }}
      >
        <AppCard>
          <Typography variant="h6" fontWeight={950} gutterBottom>
            How to read this
          </Typography>
          <Typography color="text.secondary">
            A high simulated ROI in a small group can move quickly as more picks are graded. Treat limited-history groups as
            leads for research, not conclusions.
          </Typography>
        </AppCard>
        <AppCard>
          <Typography variant="h6" fontWeight={950} gutterBottom>
            What to watch
          </Typography>
          <Typography color="text.secondary">
            Provider, prop type, and confidence splits become more useful once each group has enough graded picks to smooth
            out one-slate noise.
          </Typography>
        </AppCard>
      </Box>
    </Stack>
  );
}

function ApiStateCard({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <AppCard sx={{ borderColor: tone === 'error' ? 'rgba(244,63,94,.32)' : undefined }}>
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </AppCard>
  );
}
