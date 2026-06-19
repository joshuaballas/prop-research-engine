import { Box, Table, type TableProps, type SxProps, type Theme } from '@mui/material';
import type { ReactNode } from 'react';

type AppDataTableProps = {
  children: ReactNode;
  minWidth?: number;
  tableSx?: SxProps<Theme>;
  sx?: SxProps<Theme>;
} & Pick<TableProps, 'size' | 'stickyHeader'>;

export function AppDataTable({ children, minWidth, tableSx, sx, size = 'small', stickyHeader = true }: AppDataTableProps) {
  return (
    <Box
      sx={[
        {
          overflowX: 'auto',
          border: '1px solid rgba(148,163,184,.16)',
          borderRadius: 4,
          bgcolor: 'rgba(8,13,26,.28)'
        },
        ...(Array.isArray(sx) ? sx : sx ? [sx] : [])
      ]}
    >
      <Table
        size={size}
        stickyHeader={stickyHeader}
        sx={[
          minWidth ? { minWidth } : {},
          ...(Array.isArray(tableSx) ? tableSx : tableSx ? [tableSx] : [])
        ]}
      >
        {children}
      </Table>
    </Box>
  );
}
