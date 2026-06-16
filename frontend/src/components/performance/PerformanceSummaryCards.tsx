import { Grid } from '@mui/material';
import { mockPerformance } from '../../data/mockPerformance';
import { formatPercent } from '../../utils/formatters';
import { MetricCard } from '../ui/MetricCard';

export function PerformanceSummaryCards() {
  const overall = mockPerformance[0];
  return (
    <Grid container spacing={2}>
      <Grid size={{ xs: 12, sm: 6, lg: 2 }}>
        <MetricCard label="Candidate ROI" value={formatPercent(overall.roi, { signed: true })} helper="Mock graded sample" />
      </Grid>
      <Grid size={{ xs: 12, sm: 6, lg: 2 }}>
        <MetricCard label="Hit Rate" value={formatPercent(overall.hitRate)} helper={`${overall.wins}-${overall.losses}-${overall.pushes}`} />
      </Grid>
      <Grid size={{ xs: 12, sm: 6, lg: 2 }}>
        <MetricCard label="Graded Picks" value={`${overall.gradedPicks}`} helper="All mock sources" />
      </Grid>
      <Grid size={{ xs: 12, sm: 6, lg: 2 }}>
        <MetricCard label="Average EV" value={formatPercent(overall.averageEv, { signed: true })} helper="At recommendation time" />
      </Grid>
      <Grid size={{ xs: 12, sm: 6, lg: 2 }}>
        <MetricCard label="Best Strategy" value="Conservative" helper="+12.1% mock ROI" />
      </Grid>
      <Grid size={{ xs: 12, sm: 6, lg: 2 }}>
        <MetricCard label="Low Sample" value="2" helper="Use caution" />
      </Grid>
    </Grid>
  );
}
