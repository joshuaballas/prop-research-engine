import { Grid, Stack, Typography } from '@mui/material';
import type { PerformanceGroup } from '../../types/performance';
import { formatPercent } from '../../utils/formatters';
import { AppCard } from '../ui/AppCard';
import { palette } from '../../theme/palette';

interface SummaryMetric {
  label: string;
  value: string;
  helper: string;
  tone?: 'default' | 'good' | 'neutral';
}

function SummaryCard({ metric }: { metric: SummaryMetric }) {
  const valueColor =
    metric.tone === 'good' ? palette.success : metric.tone === 'neutral' ? palette.info : palette.text;

  return (
    <AppCard
      sx={{
        height: '100%',
        backgroundColor: palette.cardSurface
      }}
      contentSx={{
        height: '100%',
        minHeight: { xs: 96, md: 126 },
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'space-between',
        gap: { xs: 0.85, md: 1.2 },
        p: { xs: 1.25, md: 2.25 },
        '&:last-child': { pb: { xs: 1.25, md: 2.25 } }
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

function formatCategoryLabel(value?: string) {
  if (!value) return '-';
  return value
    .split('|')
    .map((part) => part.trim().replace(/([a-z])([A-Z])/g, '$1 $2'))
    .filter(Boolean)
    .join(' · ');
}

export function PerformanceSummaryCards({ rows }: { rows: PerformanceGroup[] }) {
  const overall = rows.find((row) => row.groupType === 'Overall') ?? rows[0];
  const hasGradedSample = (overall?.gradedPicks ?? 0) > 0;
  const topSegment = rows
    .filter((row) => row.groupType !== 'Overall' && row.gradedPicks >= 10)
    .sort((a, b) => b.roi - a.roi)[0];
  const hasDecisions = overall.wins + overall.losses > 0;
  const metrics: SummaryMetric[] = [
    {
      label: 'Graded Results',
      value: `${overall.gradedPicks}`,
      helper: 'Provider lines graded'
    },
    {
      label: 'Hit Rate',
      value: hasDecisions ? formatPercent(overall.hitRate) : '-',
      helper: 'Pushes excluded'
    },
    {
      label: 'Simulated ROI',
      value: hasGradedSample ? formatPercent(overall.roi, { signed: true }) : '-',
      helper: hasGradedSample ? 'Flat 1-unit tracking' : 'No graded sample',
      tone: hasGradedSample ? 'good' : 'default'
    },
    {
      label: 'Avg Model Edge',
      value: formatPercent(overall.averageEv, { signed: true }),
      helper: 'Average edge when picked',
      tone: 'good'
    },
    {
      label: 'Best Category',
      value: formatCategoryLabel(topSegment?.groupKey),
      helper: topSegment ? 'Best by simulated ROI' : 'Needs more graded results'
    }
  ];

  return (
    <Grid container spacing={2}>
      {metrics.map((metric) => (
        <Grid key={metric.label} size={{ xs: 6, md: 4, xl: 2.4 }}>
          <SummaryCard metric={metric} />
        </Grid>
      ))}
    </Grid>
  );
}
