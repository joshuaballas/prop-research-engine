import { Box, Stack, Typography } from '@mui/material';
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
  const isLongValue = metric.value.length > 18;

  return (
    <AppCard
      sx={{
        height: '100%',
        backgroundColor: palette.cardSurface
      }}
      contentSx={{
        height: '100%',
        minHeight: { xs: 96, md: 108 },
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'space-between',
        gap: { xs: 0.45, md: 0.6 },
        p: { xs: 1.1, md: 1.6 },
        '&:last-child': { pb: { xs: 1.1, md: 1.6 } }
      }}
    >
      <Typography
        variant="caption"
        color="text.secondary"
        fontWeight={850}
        textTransform="uppercase"
        sx={{ lineHeight: 1.15 }}
      >
        {metric.label}
      </Typography>
      <Stack justifyContent="center" sx={{ flex: 1, minHeight: 0 }}>
        <Typography
          fontWeight={950}
          sx={{
            color: valueColor,
            fontSize: isLongValue ? { xs: 17, md: 20 } : { xs: 20, md: 24 },
            lineHeight: isLongValue ? 1.12 : 1.08,
            display: '-webkit-box',
            overflow: 'hidden',
            WebkitBoxOrient: 'vertical',
            WebkitLineClamp: 2,
            overflowWrap: 'anywhere'
          }}
        >
          {metric.value}
        </Typography>
      </Stack>
      <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.2 }}>
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
      label: 'Overall W-L-P',
      value: `${overall.wins}-${overall.losses}-${overall.pushes}`,
      helper: `${overall.wins} wins · ${overall.losses} losses · ${overall.pushes} pushes`
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
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(5, minmax(0, 1fr))' }, gap: { xs: 1, md: 1.5 } }}>
      {metrics.map((metric) => (
        <Box key={metric.label}>
          <SummaryCard metric={metric} />
        </Box>
      ))}
    </Box>
  );
}
