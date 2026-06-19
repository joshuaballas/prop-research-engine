import { Box, Divider, Stack, Typography, type SxProps, type Theme } from '@mui/material';
import type { ReactNode } from 'react';
import type { MetricHelpKey } from '../metrics/metricHelp';
import { MetricLabelWithHelp } from '../metrics/MetricLabelWithHelp';
import { ProviderLabel } from '../providers/ProviderLabel';
import type { ProviderCategory } from '../../types/markets';
import { AppCard } from './AppCard';
import { AppMobileCardActions } from './AppRowActions';
import { renderOpportunityCautionText } from '../../utils/warnings';

type OpportunityCardShellProps = {
  children: ReactNode;
  sx?: SxProps<Theme>;
  contentSx?: SxProps<Theme>;
};

export function OpportunityCardShell({ children, sx, contentSx }: OpportunityCardShellProps) {
  return (
    <AppCard
      sx={[
        { width: '100%', height: '100%', display: 'flex', flexDirection: 'column' },
        ...(Array.isArray(sx) ? sx : sx ? [sx] : [])
      ]}
      contentSx={[
        { flex: 1, display: 'flex', flexDirection: 'column' },
        ...(Array.isArray(contentSx) ? contentSx : contentSx ? [contentSx] : [])
      ]}
    >
      {children}
    </AppCard>
  );
}

export function OpportunityProviderRow({
  provider,
  category,
  children,
  compact = true,
  sx
}: {
  provider: string;
  category: ProviderCategory;
  children?: ReactNode;
  compact?: boolean;
  sx?: SxProps<Theme>;
}) {
  return (
    <Stack
      direction="row"
      justifyContent="space-between"
      alignItems="center"
      gap={1}
      flexWrap="wrap"
      useFlexGap
      sx={sx}
    >
      <ProviderLabel provider={provider} category={category} compact={compact} />
      {children}
    </Stack>
  );
}

export function OpportunityMetricGrid({
  children,
  columns = { xs: 2, sm: 4 },
  sx
}: {
  children: ReactNode;
  columns?: { xs: number; sm?: number; md?: number };
  sx?: SxProps<Theme>;
}) {
  const template = {
    xs: `repeat(${columns.xs}, minmax(0, 1fr))`,
    ...(columns.sm ? { sm: `repeat(${columns.sm}, minmax(0, 1fr))` } : null),
    ...(columns.md ? { md: `repeat(${columns.md}, minmax(0, 1fr))` } : null)
  };

  return (
    <Box
      sx={[
        {
          display: 'grid',
          gridTemplateColumns: template,
          columnGap: { xs: 1.75, sm: 1.25 },
          rowGap: 1,
          borderTop: '1px solid rgba(148,163,184,.14)',
          pt: 1
        },
        ...(Array.isArray(sx) ? sx : sx ? [sx] : [])
      ]}
    >
      {children}
    </Box>
  );
}

export function OpportunityMetricItem({
  label,
  value,
  metric,
  noWrap = true
}: {
  label: string;
  value: ReactNode;
  metric?: MetricHelpKey;
  noWrap?: boolean;
}) {
  const resolvedMetric = metric ?? (label.trim().toLowerCase() === 'sample' ? 'sample' : undefined);

  return (
    <Stack spacing={0.2} minWidth={0}>
      {resolvedMetric ? (
        <MetricLabelWithHelp metric={resolvedMetric} />
      ) : (
        <Typography variant="caption" color="text.secondary" fontWeight={850} sx={{ lineHeight: 1.1, whiteSpace: 'nowrap' }}>
          {label}
        </Typography>
      )}
      {typeof value === 'string' || typeof value === 'number' ? (
        <Typography fontWeight={950} sx={{ lineHeight: 1.45 }} noWrap={noWrap}>
          {value}
        </Typography>
      ) : (
        value
      )}
    </Stack>
  );
}

export function OpportunityPickText({
  side,
  propType,
  line,
  sport,
  sx
}: {
  side: ReactNode;
  propType: ReactNode;
  line: ReactNode;
  sport?: ReactNode;
  sx?: SxProps<Theme>;
}) {
  return (
    <Stack
      direction="row"
      alignItems="center"
      gap={0.7}
      flexWrap="wrap"
      useFlexGap
      sx={[
        {
          color: 'text.secondary',
          fontWeight: 500,
          lineHeight: 1.35
        },
        ...(Array.isArray(sx) ? sx : sx ? [sx] : [])
      ]}
    >
      <Typography
        component="span"
        color="text.secondary"
        fontWeight={500}
        sx={{ lineHeight: 'inherit', whiteSpace: 'nowrap' }}
      >
        {side} {line}
      </Typography>
      <Typography component="span" color="text.secondary" aria-hidden sx={{ lineHeight: 'inherit' }}>
        ·
      </Typography>
      <Typography component="span" color="text.secondary" fontWeight={500} sx={{ lineHeight: 'inherit' }}>
        {propType}
      </Typography>
      {sport ? (
        <>
          <Typography component="span" color="text.secondary" aria-hidden sx={{ lineHeight: 'inherit' }}>
            ·
          </Typography>
          <Typography component="span" color="text.secondary" fontWeight={500} sx={{ lineHeight: 'inherit' }}>
            {sport}
          </Typography>
        </>
      ) : null}
    </Stack>
  );
}

export function OpportunityCautionText({ children }: { children?: ReactNode }) {
  if (!children) return null;
  const displayText = typeof children === 'string' ? renderOpportunityCautionText(children) : children;

  if (!displayText) return null;

  return (
    <Typography variant="body2" color="warning.main" fontWeight={800} sx={{ lineHeight: 1.45 }}>
      {displayText}
    </Typography>
  );
}

export function OpportunityCardActions({ children }: { children: ReactNode }) {
  return <AppMobileCardActions>{children}</AppMobileCardActions>;
}

export function OpportunityInlineActions({ children, sx }: { children: ReactNode; sx?: SxProps<Theme> }) {
  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={1}
      flexWrap={{ xs: 'nowrap', sm: 'wrap' }}
      useFlexGap
      sx={[
        {
          '& .MuiButton-root': {
            width: { xs: '100%', sm: 'auto' }
          }
        },
        ...(Array.isArray(sx) ? sx : sx ? [sx] : [])
      ]}
    >
      {children}
    </Stack>
  );
}

export function OpportunityCardLayout({
  context,
  title,
  value,
  event,
  market,
  status,
  provider,
  metrics,
  caution,
  helper,
  actions,
  actionMode = 'inline',
  shellSx,
  contentSx,
  compact = false
}: {
  context?: ReactNode;
  title: ReactNode;
  value?: ReactNode;
  event?: ReactNode;
  market?: ReactNode;
  status?: ReactNode;
  provider?: ReactNode;
  metrics?: ReactNode;
  caution?: ReactNode;
  helper?: ReactNode;
  actions?: ReactNode;
  actionMode?: 'inline' | 'stacked';
  shellSx?: SxProps<Theme>;
  contentSx?: SxProps<Theme>;
  compact?: boolean;
}) {
  return (
    <OpportunityCardShell
      sx={shellSx}
      contentSx={[
        { p: compact ? 1.75 : 2.25, '&:last-child': { pb: compact ? 1.75 : 2.25 } },
        ...(Array.isArray(contentSx) ? contentSx : contentSx ? [contentSx] : [])
      ]}
    >
      <Stack sx={{ flex: 1, minHeight: 0 }}>
        <Stack spacing={compact ? 1.05 : 1.35}>
          <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1.25}>
            <Box minWidth={0}>
              {context ? (
                <Stack direction="row" gap={0.65} rowGap={0.55} alignItems="center" flexWrap="wrap" sx={{ mb: 0.45 }}>
                  {context}
                </Stack>
              ) : null}
              {typeof title === 'string' ? (
                <Typography variant={compact ? 'subtitle1' : 'h6'} fontWeight={950} sx={{ lineHeight: 1.08 }} noWrap>
                  {title}
                </Typography>
              ) : (
                title
              )}
              {event ? (
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.35, lineHeight: 1.35 }}>
                  {event}
                </Typography>
              ) : null}
            </Box>
            {value ? <Box sx={{ flexShrink: 0, alignSelf: 'flex-start', textAlign: 'right' }}>{value}</Box> : null}
          </Stack>

          {market ? (
            typeof market === 'string' ? (
              <Typography fontWeight={950}>{market}</Typography>
            ) : (
              market
            )
          ) : null}

          {provider ? provider : null}

          {metrics ? (
            <>
              <Divider />
              {metrics}
            </>
          ) : null}

          {caution ? (
            typeof caution === 'string' ? (
              <OpportunityCautionText>{caution}</OpportunityCautionText>
            ) : (
              caution
            )
          ) : null}

          {helper ? (
            typeof helper === 'string' ? (
              <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.45 }}>
                {helper}
              </Typography>
            ) : (
              helper
            )
          ) : null}
        </Stack>
        {actions ? (
          <Box
            sx={{
              mt: 'auto',
              pt: {
                xs: actionMode === 'stacked' ? (compact ? 2.75 : 2.95) : (compact ? 2.05 : 2.25),
                sm: compact ? 1.05 : 1.35
              }
            }}
          >
            {actionMode === 'stacked' ? (
              <OpportunityCardActions>{actions}</OpportunityCardActions>
            ) : (
              <OpportunityInlineActions>{actions}</OpportunityInlineActions>
            )}
          </Box>
        ) : null}
      </Stack>
    </OpportunityCardShell>
  );
}
