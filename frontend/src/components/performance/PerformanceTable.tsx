import { TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import type { PerformanceGroup } from '../../types/performance';
import { formatPercent } from '../../utils/formatters';
import { appTableRowSx } from '../ui/appTableStyles';
import { AppDataTable } from '../ui/AppDataTable';
import { AppTableEvValue, AppTableMetricValue, AppTablePrimaryText } from '../ui/AppTableTypography';

export function PerformanceTable({ rows, category }: { rows: PerformanceGroup[]; category: string }) {
  const isOverview = category === 'Overview';
  const primaryColumnLabel = category === 'Providers' ? 'Provider' : category === 'Prop Types' ? 'Prop Type' : 'Group';

  return (
    <AppDataTable minWidth={760} tableSx={{ tableLayout: 'fixed' }}>
        <colgroup>
          {isOverview ? (
            <>
              <col style={{ width: '22%' }} />
              <col style={{ width: '24%' }} />
            </>
          ) : (
            <col style={{ width: '46%' }} />
          )}
          <col style={{ width: '9%' }} />
          <col style={{ width: '11%' }} />
          <col style={{ width: '11%' }} />
          <col style={{ width: '13%' }} />
          <col style={{ width: '10%' }} />
        </colgroup>
        <TableHead>
          <TableRow>
            {isOverview ? (
              <>
                <TableCell>Provider</TableCell>
                <TableCell>Prop Type</TableCell>
              </>
            ) : (
              <TableCell>{primaryColumnLabel}</TableCell>
            )}
            <TableCell align="center">Graded</TableCell>
            <TableCell align="right">W-L-P</TableCell>
            <TableCell align="right">Hit Rate</TableCell>
            <TableCell align="right">Simulated ROI</TableCell>
            <TableCell align="right">Avg Model Edge</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.length ? (
            rows.map((row) => (
                <TableRow
                  key={`${row.groupType}-${row.groupKey}`}
                  hover
                  sx={appTableRowSx}
                >
                  {isOverview ? (
                    <>
                      <TableCell>
                        <AppTablePrimaryText noWrap={false}>{splitProviderPropType(row.groupKey).provider}</AppTablePrimaryText>
                      </TableCell>
                      <TableCell>
                        <AppTablePrimaryText noWrap={false}>{splitProviderPropType(row.groupKey).propType}</AppTablePrimaryText>
                      </TableCell>
                    </>
                  ) : (
                    <TableCell>
                      <AppTablePrimaryText noWrap={false}>{formatGroupLabel(row.groupKey)}</AppTablePrimaryText>
                    </TableCell>
                  )}
                  <TableCell align="center"><AppTableMetricValue>{row.gradedPicks}</AppTableMetricValue></TableCell>
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
                </TableRow>
              ))
          ) : (
            <TableRow>
              <TableCell colSpan={isOverview ? 7 : 6}>
                <Typography color="text.secondary">No {category.toLowerCase()} performance rows yet.</Typography>
              </TableCell>
            </TableRow>
          )}
        </TableBody>
    </AppDataTable>
  );
}

function splitProviderPropType(groupKey: string) {
  const [provider, propType] = groupKey.split('|').map((part) => part.trim());
  return {
    provider: formatGroupLabel(provider || groupKey),
    propType: formatGroupLabel(propType || '-')
  };
}

function formatGroupLabel(value: string) {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
