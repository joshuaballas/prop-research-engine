import { Grid, Stack, Typography } from '@mui/material';
import type { PerformanceGroup } from '../../types/performance';
import { formatPercent } from '../../utils/formatters';
import { AppCard } from '../ui/AppCard';
import { palette } from '../../theme/palette';

interface SummaryMetric {
  label: string;
  value: string;
  helper: string;
  tone?: 'default' | 'good' | 'caution';
}

function SummaryCard({ metric }: { metric: SummaryMetric }) {
  const valueColor =
    metric.tone === 'good' ? palette.success : metric.tone === 'caution' ? palette.warning : palette.text;

  return (
    <AppCard
      sx={{
        height: '100%',
        backgroundColor: palette.cardSurface
      }}
      contentSx={{
        height: '100%',
        minHeight: { xs: 106, md: 126 },
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'space-between',
        gap: { xs: 0.85, md: 1.2 },
        p: { xs: 1.45, md: 2.25 },
        '&:last-child': { pb: { xs: 1.45, md: 2.25 } }
      }}
    >
      <Stack spacing={{ xs: 0.35, md: 0.55 }}>
        <Typography
          variant="caption"
          color="text.secondary"
          fontWeight={850}
          textTransform="uppercase"
          sx={{ fontSize: { xs: 10.5, md: 12 }, lineHeight: 1.2 }}
        >
          {metric.label}
        </Typography>
        <Typography
          variant="h4"
          fontWeight={950}
          sx={{
            color: valueColor,
            fontSize: { xs: 22, md: 28 },
            lineHeight: 1.05,
            overflowWrap: 'normal',
            wordBreak: 'normal'
          }}
        >
          {metric.value}
        </Typography>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.3, fontSize: { xs: 12, md: 14 } }}>
        {metric.helper}
      </Typography>
    </AppCard>
  );
}

export function PerformanceSummaryCards({ rows }: { rows: PerformanceGroup[] }) {
  const overall = rows.find((row) => row.groupType === 'Overall') ?? rows[0];
  const bestStrategy = rows
    .filter((row) => row.groupType === 'Strategy')
    .sort((a, b) => b.roi - a.roi)[0];
  const limitedHistoryCount = rows.filter((row) => row.warning || row.gradedPicks < 30).length;
  const hasDecisions = overall.wins + overall.losses > 0;
  const metrics: SummaryMetric[] = [
    {
      label: 'Simulated ROI',
      value: formatPercent(overall.roi, { signed: true }),
      helper: 'Historical candidate results',
      tone: 'good'
    },
    {
      label: 'Hit Rate',
      value: hasDecisions ? formatPercent(overall.hitRate) : '-',
      helper: `${overall.wins}-${overall.losses}-${overall.pushes} graded`
    },
    {
      label: 'Graded Picks',
      value: `${overall.gradedPicks}`,
      helper: 'Across graded history'
    },
    {
      label: 'Average EV',
      value: formatPercent(overall.averageEv, { signed: true }),
      helper: 'At recommendation time',
      tone: 'good'
    },
    {
      label: 'Best Strategy',
      value: bestStrategy?.groupKey === 'ConservativeCandidate'
        ? 'Conservative'
        : bestStrategy?.groupKey.replace(/([a-z])([A-Z])/g, '$1 $2') ?? 'N/A',
      helper: bestStrategy ? `${formatPercent(bestStrategy.roi, { signed: true })} simulated ROI` : 'No graded strategy yet'
    },
    {
      label: 'Limited-history Groups',
      value: `${limitedHistoryCount}`,
      helper: 'Need more graded picks',
      tone: 'caution'
    }
  ];

  return (
    <Grid container spacing={2}>
      {metrics.map((metric) => (
        <Grid key={metric.label} size={{ xs: 6, md: 4, xl: 2 }}>
          <SummaryCard metric={metric} />
        </Grid>
      ))}
    </Grid>
  );
}
