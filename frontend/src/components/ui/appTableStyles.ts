export const appTableRowSx = {
  height: 90,
  '& td': {
    borderColor: 'rgba(148,163,184,.12)',
    py: 1.05,
    verticalAlign: 'middle'
  },
  '&:nth-of-type(even)': {
    backgroundColor: 'rgba(15, 23, 42, 0.18)'
  },
  '&:nth-of-type(even) td[data-sticky-action="true"]': {
    backgroundColor: '#0B1220'
  },
  '&.MuiTableRow-hover:hover, &:hover': {
    backgroundColor: 'rgba(15, 23, 42, 0.34)'
  },
  '&.MuiTableRow-hover:hover td[data-sticky-action="true"], &:hover td[data-sticky-action="true"]': {
    backgroundColor: '#111827'
  }
} as const;

export const stackedRowActionsSx = {
  width: 98,
  alignItems: 'stretch'
} as const;

export const stickyActionCellSx = {
  position: 'sticky',
  right: 0,
  zIndex: 2,
  backgroundColor: '#080D19',
  borderLeft: '1px solid rgba(148,163,184,.14)'
} as const;

export const stickyActionHeaderCellSx = {
  ...stickyActionCellSx,
  zIndex: 4,
  backgroundColor: '#0B1220'
} as const;
