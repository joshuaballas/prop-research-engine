import { Stack, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import type { PerformanceGroup } from '../../types/performance';
import { formatPercent } from '../../utils/formatters';
import { MetadataText } from '../ui/MetadataText';
import { appTableRowSx } from '../ui/appTableStyles';
import { AppDataTable } from '../ui/AppDataTable';
import { AppTableEvValue, AppTableMetricValue, AppTablePrimaryText, AppTableSecondaryText } from '../ui/AppTableTypography';

function getSampleWarning(row: PerformanceGroup) {
  if (row.warning) {
    return row.warning === 'Low sample size' ? 'Limited history' : row.warning;
  }

  return row.gradedPicks < 30 ? 'Limited history' : 'Stable sample';
}

export function PerformanceTable({ rows, category }: { rows: PerformanceGroup[]; category: string }) {
  return (
    <AppDataTable>
        <TableHead>
          <TableRow>
            <TableCell>Group</TableCell>
            <TableCell align="right">Graded</TableCell>
            <TableCell align="right">W-L-P</TableCell>
            <TableCell align="right">Hit Rate</TableCell>
            <TableCell align="right">Simulated ROI</TableCell>
            <TableCell align="right">Avg EV</TableCell>
            <TableCell>Sample Status / Caution</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.length ? (
            rows.map((row) => {
              const warning = getSampleWarning(row);
              const warningTone = warning === 'Stable sample' ? undefined : 'stale';
              return (
                <TableRow
                  key={`${row.groupType}-${row.groupKey}`}
                  hover
                  sx={appTableRowSx}
                >
                  <TableCell>
                    <Stack spacing={0.25}>
                      <AppTablePrimaryText noWrap={false}>{row.groupKey.replace(/([a-z])([A-Z])/g, '$1 $2')}</AppTablePrimaryText>
                      <AppTableSecondaryText noWrap={false}>
                        {row.groupType}
                      </AppTableSecondaryText>
                    </Stack>
                  </TableCell>
                  <TableCell align="right"><AppTableMetricValue>{row.gradedPicks}</AppTableMetricValue></TableCell>
                  <TableCell align="right">
                    <AppTableMetricValue>{row.wins}-{row.losses}-{row.pushes}</AppTableMetricValue>
                  </TableCell>
                  <TableCell align="right">
                    <AppTableMetricValue>{row.wins + row.losses > 0 ? formatPercent(row.hitRate) : '-'}</AppTableMetricValue>
                  </TableCell>
                  <TableCell align="right">
                    {row.gradedPicks > 0 ? (
                      <AppTableEvValue positive={row.roi >= 0}>{formatPercent(row.roi, { signed: true })}</AppTableEvValue>
                    ) : (
                      <AppTableMetricValue>-</AppTableMetricValue>
                    )}
                  </TableCell>
                  <TableCell align="right"><AppTableMetricValue>{formatPercent(row.averageEv, { signed: true })}</AppTableMetricValue></TableCell>
                  <TableCell>
                    {warningTone ? (
                      <MetadataText tone={warningTone}>{warning}</MetadataText>
                    ) : (
                      warning
                    )}
                  </TableCell>
                </TableRow>
              );
            })
          ) : (
            <TableRow>
              <TableCell colSpan={7}>
                <Typography color="text.secondary">No {category.toLowerCase()} performance rows yet.</Typography>
              </TableCell>
            </TableRow>
          )}
        </TableBody>
    </AppDataTable>
  );
}
