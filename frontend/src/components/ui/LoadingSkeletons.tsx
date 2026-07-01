import { Box, Grid, Skeleton, Stack, TableBody, TableCell, TableHead, TableRow } from '@mui/material';
import { AppCard } from './AppCard';
import { AppDataTable } from './AppDataTable';
import { appTableRowSx, stickyActionCellSx, stickyActionHeaderCellSx } from './appTableStyles';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';

export function MetricCardSkeleton({ count = 3 }: { count?: number }) {
  return (
    <Grid container spacing={2} aria-busy="true">
      {Array.from({ length: count }).map((_, index) => (
        <Grid key={index} size={{ xs: 6, sm: 6, lg: count >= 5 ? 2.4 : count === 4 ? 3 : 4 }}>
          <AppCard contentSx={{ minHeight: 118, p: { xs: 1.4, md: 1.7 }, '&:last-child': { pb: { xs: 1.4, md: 1.7 } } }}>
            <Stack spacing={1.25} sx={{ height: '100%', justifyContent: 'space-between' }}>
              <Skeleton width="52%" height={16} />
              <Skeleton width="72%" height={32} />
              <Skeleton width="62%" height={16} />
            </Stack>
          </AppCard>
        </Grid>
      ))}
    </Grid>
  );
}

export function MarketCardSkeletonGrid({ count = 6 }: { count?: number }) {
  return (
    <Grid container spacing={2} aria-busy="true">
      {Array.from({ length: count }).map((_, index) => (
        <Grid key={index} size={12}>
          <MarketCardSkeleton />
        </Grid>
      ))}
    </Grid>
  );
}

export function MarketCardSkeleton() {
  return (
    <AppCard contentSx={{ p: 1.7, '&:last-child': { pb: 1.7 } }}>
      <Stack spacing={1.35} aria-hidden="true">
        <Stack direction="row" justifyContent="space-between" gap={1.5}>
          <Stack spacing={0.65} sx={{ flex: 1, minWidth: 0 }}>
            <Skeleton width="62%" height={22} />
            <Skeleton width="46%" height={16} />
          </Stack>
          <Skeleton variant="rounded" width={74} height={28} sx={{ borderRadius: radius.pill }} />
        </Stack>
        <Stack direction="row" spacing={1} alignItems="center">
          <Skeleton variant="rounded" width={28} height={28} sx={{ borderRadius: radius.card }} />
          <Skeleton width="38%" height={18} />
        </Stack>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 1 }}>
          {Array.from({ length: 3 }).map((_, index) => (
            <Stack key={index} spacing={0.35}>
              <Skeleton width="68%" height={14} />
              <Skeleton width="86%" height={22} />
            </Stack>
          ))}
        </Box>
        <Stack direction="row" spacing={1}>
          <Skeleton variant="rounded" width={94} height={34} sx={{ borderRadius: radius.featuredCard }} />
          <Skeleton variant="rounded" width={84} height={34} sx={{ borderRadius: radius.featuredCard }} />
        </Stack>
      </Stack>
    </AppCard>
  );
}

export function MarketTableSkeleton({ rows = 8, columns = 8, minWidth = 980 }: { rows?: number; columns?: number; minWidth?: number }) {
  return (
    <Box aria-busy="true">
      <AppDataTable minWidth={minWidth}>
        <TableHead>
          <TableRow>
            {Array.from({ length: columns }).map((_, index) => (
              <TableCell key={index} align={index >= columns - 3 ? 'right' : 'left'} sx={index === columns - 1 ? stickyActionHeaderCellSx : undefined}>
                <Skeleton width={index === 0 ? 52 : 86} height={18} />
              </TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {Array.from({ length: rows }).map((_, rowIndex) => (
            <TableRow key={rowIndex} sx={appTableRowSx}>
              {Array.from({ length: columns }).map((_, cellIndex) => (
                <TableCell key={cellIndex} align={cellIndex >= columns - 3 ? 'right' : 'left'} sx={cellIndex === columns - 1 ? stickyActionCellSx : undefined}>
                  <Skeleton width={cellSkeletonWidth(cellIndex)} height={cellIndex === 1 ? 24 : 20} />
                  {cellIndex === 1 ? <Skeleton width="48%" height={14} sx={{ mt: 0.35 }} /> : null}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </AppDataTable>
    </Box>
  );
}

export function TopPickSkeletonGrid({ count = 3 }: { count?: number }) {
  return (
    <Grid container spacing={1.5} aria-busy="true">
      {Array.from({ length: count }).map((_, index) => (
        <Grid key={index} size={{ xs: 12, lg: 4 }} sx={{ display: 'flex' }}>
          <TopPickCardSkeleton />
        </Grid>
      ))}
    </Grid>
  );
}

export function TopPickCardSkeleton() {
  return (
    <AppCard sx={{ width: '100%' }} contentSx={{ p: { xs: 1.45, md: 1.7 }, '&:last-child': { pb: { xs: 1.45, md: 1.7 } } }}>
      <Stack spacing={1.35} aria-hidden="true">
        <Stack direction="row" justifyContent="space-between" gap={1.4}>
          <Skeleton variant="rounded" width={88} height={28} sx={{ borderRadius: radius.pill }} />
          <Skeleton width={56} height={18} />
        </Stack>
        <Stack spacing={0.55}>
          <Skeleton width="64%" height={26} />
          <Skeleton width="48%" height={18} />
        </Stack>
        <Stack direction="row" spacing={1} alignItems="center">
          <Skeleton variant="rounded" width={30} height={30} sx={{ borderRadius: radius.card }} />
          <Skeleton width="34%" height={18} />
        </Stack>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 1 }}>
          {Array.from({ length: 6 }).map((_, index) => (
            <Stack key={index} spacing={0.3}>
              <Skeleton width="76%" height={13} />
              <Skeleton width="58%" height={20} />
            </Stack>
          ))}
        </Box>
        <Stack direction="row" spacing={1}>
          <Skeleton variant="rounded" width={92} height={34} sx={{ borderRadius: radius.featuredCard }} />
          <Skeleton variant="rounded" width={82} height={34} sx={{ borderRadius: radius.featuredCard }} />
        </Stack>
      </Stack>
    </AppCard>
  );
}

export function PerformanceSummarySkeleton({ count = 5 }: { count?: number }) {
  return <MetricCardSkeleton count={count} />;
}

export function PerformanceBreakdownSkeleton({ mobile = false }: { mobile?: boolean }) {
  if (mobile) {
    return (
      <Stack spacing={1.25} aria-busy="true">
        {Array.from({ length: 4 }).map((_, index) => (
          <AppCard key={index} contentSx={{ p: 1.35, '&:last-child': { pb: 1.35 } }}>
            <Stack spacing={1}>
              <Stack direction="row" justifyContent="space-between">
                <Skeleton width="48%" height={22} />
                <Skeleton variant="rounded" width={66} height={24} sx={{ borderRadius: radius.pill }} />
              </Stack>
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 1 }}>
                {Array.from({ length: 4 }).map((__, metricIndex) => (
                  <Stack key={metricIndex} spacing={0.35}>
                    <Skeleton width="52%" height={14} />
                    <Skeleton width="72%" height={20} />
                  </Stack>
                ))}
              </Box>
            </Stack>
          </AppCard>
        ))}
      </Stack>
    );
  }

  return <MarketTableSkeleton rows={6} columns={6} minWidth={860} />;
}

export function PropHistorySkeleton() {
  return (
    <AppCard contentSx={{ p: { xs: 1.25, md: 2 }, '&:last-child': { pb: { xs: 1.25, md: 2 } } }}>
      <Stack spacing={1.35} aria-busy="true" aria-hidden="true">
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <Stack spacing={0.5}>
            <Skeleton width={142} height={24} />
            <Skeleton width={220} height={16} />
          </Stack>
          <Skeleton variant="rounded" width={92} height={28} sx={{ borderRadius: radius.pill }} />
        </Stack>
        <Box sx={{ height: 180, borderRadius: radius.featuredCard, border: `1px solid ${palette.border}`, p: 1.2 }}>
          <Skeleton width="100%" height="100%" variant="rounded" sx={{ borderRadius: radius.card }} />
        </Box>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 1 }}>
          {Array.from({ length: 4 }).map((_, index) => (
            <Stack key={index} spacing={0.35}>
              <Skeleton width="70%" height={14} />
              <Skeleton width="54%" height={22} />
            </Stack>
          ))}
        </Box>
      </Stack>
    </AppCard>
  );
}

function cellSkeletonWidth(index: number) {
  if (index === 1) return '76%';
  if (index === 0) return 48;
  if (index >= 6) return 82;
  return `${52 + (index % 3) * 12}%`;
}
