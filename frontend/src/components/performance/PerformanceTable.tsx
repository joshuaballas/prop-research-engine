import { Box, Table, TableBody, TableCell, TableHead, TableRow } from '@mui/material';
import type { PerformanceGroup } from '../../data/mockPerformance';
import { formatPercent } from '../../utils/formatters';
import { StatusPill } from '../ui/StatusPill';

export function PerformanceTable({ rows }: { rows: PerformanceGroup[] }) {
  return (
    <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 3 }}>
      <Table size="small" stickyHeader>
        <TableHead>
          <TableRow>
            <TableCell>Group</TableCell>
            <TableCell>Key</TableCell>
            <TableCell align="right">Graded</TableCell>
            <TableCell align="right">W-L-P</TableCell>
            <TableCell align="right">Hit Rate</TableCell>
            <TableCell align="right">ROI</TableCell>
            <TableCell>Warning</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={`${row.groupType}-${row.groupKey}`}>
              <TableCell>{row.groupType}</TableCell>
              <TableCell>{row.groupKey}</TableCell>
              <TableCell align="right">{row.gradedPicks}</TableCell>
              <TableCell align="right">{row.wins}-{row.losses}-{row.pushes}</TableCell>
              <TableCell align="right">{formatPercent(row.hitRate)}</TableCell>
              <TableCell align="right">{formatPercent(row.roi, { signed: true })}</TableCell>
              <TableCell>{row.warning ? <StatusPill label={row.warning} /> : 'None'}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}
