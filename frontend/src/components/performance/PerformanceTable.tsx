import { Stack, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import type { PerformanceGroup } from '../../types/performance';
import { formatPercent } from '../../utils/formatters';
import { MetadataText } from '../ui/MetadataText';
import { palette } from '../../theme/palette';
import { appTableRowSx } from '../ui/appTableStyles';
import { AppDataTable } from '../ui/AppDataTable';

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
                      <Typography fontWeight={900}>{row.groupKey.replace(/([a-z])([A-Z])/g, '$1 $2')}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        {row.groupType}
                      </Typography>
                    </Stack>
                  </TableCell>
                  <TableCell align="right">{row.gradedPicks}</TableCell>
                  <TableCell align="right">
                    {row.wins}-{row.losses}-{row.pushes}
                  </TableCell>
                  <TableCell align="right">{row.wins + row.losses > 0 ? formatPercent(row.hitRate) : '-'}</TableCell>
                  <TableCell
                    align="right"
                    sx={{ color: row.roi >= 0 ? palette.success : palette.danger, fontWeight: 900 }}
                  >
                    {row.gradedPicks > 0 ? formatPercent(row.roi, { signed: true }) : '-'}
                  </TableCell>
                  <TableCell align="right">{formatPercent(row.averageEv, { signed: true })}</TableCell>
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
