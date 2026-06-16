import { Stack } from '@mui/material';
import { PerformanceMobileAccordion } from '../../components/performance/PerformanceMobileAccordion';
import { PerformanceSummaryCards } from '../../components/performance/PerformanceSummaryCards';
import { PerformanceTable } from '../../components/performance/PerformanceTable';
import { DataStateBanner } from '../../components/ui/DataStateBanner';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockPerformance } from '../../data/mockPerformance';
import { useResponsiveView } from '../../hooks/useResponsiveView';

export function PerformancePage() {
  const { isMobile } = useResponsiveView();
  return (
    <Stack spacing={3}>
      <PageHeader title="Performance" description="Historical strategy/provider/prop performance with sample-size caution." />
      <DataStateBanner
        title="Performance is sample-size dependent"
        message="Early results should be treated as research, not proof."
        status="Limited"
      />
      <PerformanceSummaryCards />
      {isMobile ? <PerformanceMobileAccordion rows={mockPerformance} /> : <PerformanceTable rows={mockPerformance} />}
    </Stack>
  );
}
