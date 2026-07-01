import { Button, Stack, Typography } from '@mui/material';
import { radius } from '../../theme/radius';

type AppTablePaginationProps = {
  count: number;
  page: number;
  pageSize: number;
  itemLabel: string;
  onChange: (page: number) => void;
};

export function AppTablePagination({ count, page, pageSize, itemLabel, onChange }: AppTablePaginationProps) {
  if (count <= pageSize) return null;

  const pageCount = Math.ceil(count / pageSize);
  const start = (page - 1) * pageSize + 1;
  const end = Math.min(page * pageSize, count);
  const pages = getPageWindow(page, pageCount);

  const handlePageChange = (nextPage: number) => {
    if (nextPage < 1 || nextPage > pageCount || nextPage === page) return;
    onChange(nextPage);
  };

  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={{ xs: 1.4, sm: 1.2 }}
      alignItems={{ xs: 'stretch', sm: 'center' }}
      justifyContent="space-between"
      sx={{
        borderTop: '1px solid rgba(148,163,184,.14)',
        pt: { xs: 2, sm: 1.5 },
        pb: { xs: 2.5, sm: 0 }
      }}
    >
      <Typography variant="body2" color="text.secondary" fontWeight={750} textAlign={{ xs: 'center', sm: 'left' }}>
        Showing {start}-{end} of {count} {itemLabel}
      </Typography>
      <Stack
        direction="row"
        spacing={0.65}
        alignItems="center"
        justifyContent={{ xs: 'center', sm: 'flex-end' }}
        sx={{
          overflowX: 'auto',
          scrollbarWidth: 'none',
          msOverflowStyle: 'none',
          '&::-webkit-scrollbar': {
            display: 'none'
          }
        }}
      >
        <PaginationButton label="<" disabled={page === 1} onClick={() => handlePageChange(page - 1)} ariaLabel={`Previous ${itemLabel} page`} />
        {pages.map((pageNumber) => (
          <PaginationButton
            key={pageNumber}
            label={String(pageNumber)}
            active={pageNumber === page}
            onClick={() => handlePageChange(pageNumber)}
            ariaLabel={`${itemLabel} page ${pageNumber}`}
          />
        ))}
        <PaginationButton label=">" disabled={page === pageCount} onClick={() => handlePageChange(page + 1)} ariaLabel={`Next ${itemLabel} page`} />
      </Stack>
    </Stack>
  );
}

function getPageWindow(page: number, pageCount: number) {
  const windowSize = Math.min(4, pageCount);
  let start = Math.max(1, page - Math.floor(windowSize / 2));
  const end = Math.min(pageCount, start + windowSize - 1);
  start = Math.max(1, end - windowSize + 1);

  return Array.from({ length: end - start + 1 }, (_, index) => start + index);
}

function PaginationButton({
  label,
  active = false,
  disabled = false,
  onClick,
  ariaLabel
}: {
  label: string;
  active?: boolean;
  disabled?: boolean;
  onClick: () => void;
  ariaLabel: string;
}) {
  return (
    <Button
      type="button"
      variant="outlined"
      disabled={disabled}
      aria-label={ariaLabel}
      aria-current={active ? 'page' : undefined}
      onMouseDown={(event) => event.preventDefault()}
      onClick={onClick}
      sx={{
        minWidth: 34,
        width: 34,
        height: 32,
        px: 0,
        borderRadius: radius.control,
        color: active ? 'text.primary' : 'text.secondary',
        borderColor: active ? 'rgba(34,211,238,.52)' : 'rgba(148,163,184,.18)',
        bgcolor: active ? 'rgba(8,145,178,.22)' : 'rgba(15,23,42,.48)',
        fontSize: 12,
        fontWeight: 850,
        lineHeight: 1,
        flex: '0 0 auto',
        '&:hover': {
          borderColor: active ? 'rgba(34,211,238,.62)' : 'rgba(148,163,184,.3)',
          bgcolor: active ? 'rgba(8,145,178,.26)' : 'rgba(34,211,238,.08)'
        },
        '&.Mui-disabled': {
          color: 'rgba(148,163,184,.34)',
          borderColor: 'rgba(148,163,184,.12)',
          bgcolor: 'rgba(15,23,42,.28)'
        }
      }}
    >
      {label}
    </Button>
  );
}
