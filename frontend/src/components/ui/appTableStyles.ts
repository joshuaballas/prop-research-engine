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
  '&.MuiTableRow-hover:hover, &:hover': {
    backgroundColor: 'rgba(15, 23, 42, 0.34)'
  }
} as const;

export const stackedRowActionsSx = {
  width: 98,
  alignItems: 'stretch'
} as const;
