import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import { Accordion, AccordionDetails, AccordionSummary, Box, Stack, Typography } from '@mui/material';
import type { PerformanceGroup } from '../../types/performance';
import { palette } from '../../theme/palette';
import { formatPercent } from '../../utils/formatters';

function getSampleStatus(row: PerformanceGroup) {
  if (row.warning) {
    return row.warning === 'Low sample size' ? 'Limited history' : row.warning;
  }

  return row.gradedPicks < 30 ? 'Limited history' : 'Stable sample';
}

export function PerformanceMobileAccordion({ rows, category }: { rows: PerformanceGroup[]; category: string }) {
  if (!rows.length) {
    return <Typography color="text.secondary">No {category.toLowerCase()} performance rows yet.</Typography>;
  }

  return (
    <Stack spacing={1}>
      {rows.map((row) => {
        const sampleStatus = getSampleStatus(row);
        const isLimited = sampleStatus !== 'Stable sample';
        const stats = [
          ['Hit rate', formatPercent(row.hitRate), palette.text],
          ['Simulated ROI', formatPercent(row.roi, { signed: true }), row.roi < 0 ? palette.danger : palette.success],
          ['Avg EV', formatPercent(row.averageEv, { signed: true }), palette.text],
          ['Sample status', sampleStatus, isLimited ? palette.warning : palette.text]
        ];

        return (
          <Accordion
            key={`${row.groupType}-${row.groupKey}`}
            disableGutters
            sx={{
              border: `1px solid ${palette.border}`,
              borderRadius: 3,
              background: 'linear-gradient(180deg, rgba(17, 28, 49, 0.95), rgba(8, 13, 26, 0.98))',
              overflow: 'hidden',
              '&:before': { display: 'none' }
            }}
          >
            <AccordionSummary expandIcon={<ExpandMoreRoundedIcon />}>
              <Stack spacing={0.3} sx={{ minWidth: 0 }}>
                <Typography fontWeight={950}>{row.groupKey.replace(/([a-z])([A-Z])/g, '$1 $2')}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {row.groupType} - {row.gradedPicks} graded - {row.wins}-{row.losses}-{row.pushes}
                </Typography>
              </Stack>
            </AccordionSummary>
            <AccordionDetails sx={{ pt: 0.5 }}>
              <Box
                sx={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
                  gap: 1
                }}
              >
                {stats.map(([label, value, color]) => (
                  <Box
                    key={label}
                    sx={{
                      border: `1px solid ${palette.border}`,
                      borderRadius: 2,
                      backgroundColor: 'rgba(15, 23, 42, 0.48)',
                      p: 1
                    }}
                  >
                    <Typography component="div" variant="caption" color="text.secondary" fontWeight={850}>
                      {label}
                    </Typography>
                    <Typography component="div" fontWeight={900} sx={{ color, mt: 0.25, lineHeight: 1.25 }}>
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
