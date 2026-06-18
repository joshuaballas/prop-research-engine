import { Box, Stack, Tooltip, Typography } from '@mui/material';
import type { SxProps, Theme } from '@mui/material/styles';
import { metricHelp, type MetricHelpKey } from './metricHelp';

interface MetricLabelWithHelpProps {
  metric: MetricHelpKey;
  align?: 'left' | 'right' | 'center';
  variant?: 'caption' | 'table';
  sx?: SxProps<Theme>;
}

export function MetricLabelWithHelp({ metric, align = 'left', variant = 'caption', sx }: MetricLabelWithHelpProps) {
  const help = metricHelp[metric];
  const justifyContent = align === 'right' ? 'flex-end' : align === 'center' ? 'center' : 'flex-start';

  return (
    <Stack direction="row" alignItems="center" spacing={0.45} justifyContent={justifyContent} sx={{ minWidth: 0, ...sx }}>
      <Typography
        component="span"
        variant="caption"
        color="text.secondary"
        fontWeight={variant === 'table' ? 850 : 800}
        sx={{ lineHeight: 1.1, whiteSpace: 'nowrap' }}
      >
        {help.label}
      </Typography>
      <Tooltip title={help.description} arrow enterTouchDelay={0} leaveTouchDelay={5000}>
        <Box
          component="span"
          tabIndex={0}
          aria-label={`${help.label}: ${help.description}`}
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
