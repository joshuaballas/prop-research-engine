import { Box, Stack, Tooltip, Typography } from '@mui/material';
import { marketTagTextColors } from '../../utils/marketTags';

const tagDefinitions = [
  ['Top Pick', 'Higher-priority opportunity based on hit chance, edge, and line context.'],
  ['Watchlist', 'Worth monitoring, but not strong enough to treat as a top pick yet.'],
  ['Research', 'Useful for review, but not recommended for adding directly to an entry.'],
  ['Blocked', 'Low-quality, stale, risky, or historically weak. Review only.'],
  ['Featured', 'A row that currently passes featured-row criteria.']
];

const tagsHelp = (
  <Stack spacing={0.45}>
    {tagDefinitions.map(([label, definition]) => (
      <Typography key={label} component="div" variant="caption">
        <Box component="span" sx={{ color: marketTagTextColors[label], fontWeight: 900 }}>
          {label}:
        </Box>{' '}
        {definition}
      </Typography>
    ))}
  </Stack>
);

export function TagsLabelWithHelp() {
  return (
    <Stack direction="row" alignItems="center" spacing={0.45} sx={{ minWidth: 0 }}>
      <Typography
        component="span"
        variant="caption"
        color="text.secondary"
        fontWeight={850}
        sx={{ lineHeight: 1.1, whiteSpace: 'nowrap' }}
      >
        Tags
      </Typography>
      <Tooltip title={tagsHelp} arrow enterTouchDelay={0} leaveTouchDelay={5000}>
        <Box
          component="span"
          tabIndex={0}
          aria-label="Tags: quick classification for how the row should be treated."
          sx={{
            width: 15,
            height: 15,
            flex: '0 0 auto',
            borderRadius: '50%',
            border: '1px solid rgba(148, 163, 184, 0.34)',
            color: 'text.secondary',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 10,
            fontWeight: 850,
            lineHeight: 1,
            cursor: 'help',
            userSelect: 'none',
            outline: 'none',
            '&:hover, &:focus-visible': {
              color: 'text.primary',
              borderColor: 'rgba(34, 211, 238, 0.58)',
              bgcolor: 'rgba(34, 211, 238, 0.08)'
            }
          }}
        >
          ?
        </Box>
      </Tooltip>
    </Stack>
  );
}
