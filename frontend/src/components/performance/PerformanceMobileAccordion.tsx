import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import { Accordion, AccordionDetails, AccordionSummary, Box, Stack, Typography } from '@mui/material';
import type { PerformanceGroup } from '../../types/performance';
import { palette } from '../../theme/palette';
import { formatPercent } from '../../utils/formatters';

export function PerformanceMobileAccordion({ rows, category }: { rows: PerformanceGroup[]; category: string }) {
  if (!rows.length) {
    return <Typography color="text.secondary">No {category.toLowerCase()} performance rows yet.</Typography>;
  }

  return (
    <Stack spacing={0} sx={{ gap: 1 }}>
      {rows.map((row) => {
        const identityFields = getIdentityFields(row, category);
        const hasDecisions = row.wins + row.losses > 0;
        const stats = [
          ['W-L-P', `${row.wins}-${row.losses}-${row.pushes}`, palette.text],
          ['Hit rate', hasDecisions ? formatPercent(row.hitRate) : '-', palette.text],
          ['Simulated ROI', row.gradedPicks > 0 ? formatPercent(row.roi, { signed: true }) : '-', row.roi < 0 ? palette.danger : palette.success],
          ['Avg Model Edge', formatPercent(row.averageEv, { signed: true }), palette.text]
        ];

        return (
          <Accordion
            key={`${row.groupType}-${row.groupKey}`}
            disableGutters
            sx={{
              border: `1px solid ${palette.border}`,
              borderRadius: 3,
              backgroundColor: palette.cardSurface,
              overflow: 'hidden',
              margin: '0 !important',
              '&:before': { display: 'none' },
              '&.Mui-expanded': {
                margin: '0 !important',
                borderRadius: 3
              },
              '&:first-of-type': {
                borderTopLeftRadius: 12,
                borderTopRightRadius: 12
              }
            }}
          >
            <AccordionSummary
              expandIcon={<ExpandMoreRoundedIcon />}
              sx={{
                borderRadius: 3,
                '&.Mui-expanded': {
                  minHeight: 48
                },
                '& .MuiAccordionSummary-content.Mui-expanded': {
                  my: 1.5
                }
              }}
            >
              <Stack spacing={0.25} sx={{ minWidth: 0 }}>
                <Typography fontWeight={950}>{identityFields[0]?.value ?? formatGroupLabel(row.groupKey)}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {identityFields.length > 1 ? `${identityFields[1].value} · ` : ''}{row.gradedPicks} graded
                </Typography>
              </Stack>
            </AccordionSummary>
            <AccordionDetails sx={{ pt: 0.25, pb: 1.5 }}>
              <Box
                sx={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
                  gap: 0.85
                }}
              >
                {stats.map(([label, value, color]) => (
                  <Box
                    key={label}
                    sx={{
                      border: `1px solid ${palette.border}`,
                      borderRadius: 2,
                      backgroundColor: 'rgba(15, 23, 42, 0.48)',
                      p: 0.85
                    }}
                  >
                    <Typography component="div" variant="caption" color="text.secondary" fontWeight={850} sx={{ lineHeight: 1.15 }}>
                      {label}
                    </Typography>
                    <Typography component="div" fontWeight={900} sx={{ color, mt: 0.2, lineHeight: 1.2 }}>
                      {value}
                    </Typography>
                  </Box>
                ))}
              </Box>
            </AccordionDetails>
          </Accordion>
        );
      })}
    </Stack>
  );
}

function getIdentityFields(row: PerformanceGroup, category: string) {
  if (category === 'Overview') {
    const { provider, propType } = splitProviderPropType(row.groupKey);
    return [
      { label: 'Provider', value: provider },
      { label: 'Prop Type', value: propType }
    ];
  }

  if (category === 'Providers') {
    return [{ label: 'Provider', value: formatGroupLabel(row.groupKey) }];
  }

  if (category === 'Prop Types') {
    return [{ label: 'Prop Type', value: formatGroupLabel(row.groupKey) }];
  }

  return [{ label: 'Group', value: formatGroupLabel(row.groupKey) }];
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
