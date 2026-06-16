import { Accordion, AccordionDetails, AccordionSummary, Stack, Typography } from '@mui/material';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import type { PerformanceGroup } from '../../data/mockPerformance';
import { formatPercent } from '../../utils/formatters';

export function PerformanceMobileAccordion({ rows }: { rows: PerformanceGroup[] }) {
  return (
    <Stack spacing={1}>
      {rows.map((row) => (
        <Accordion key={`${row.groupType}-${row.groupKey}`}>
          <AccordionSummary expandIcon={<ExpandMoreRoundedIcon />}>
            <Typography fontWeight={900}>{row.groupKey}</Typography>
          </AccordionSummary>
          <AccordionDetails>
            <Typography color="text.secondary">
              {row.groupType} · {row.wins}-{row.losses}-{row.pushes} · ROI {formatPercent(row.roi, { signed: true })}
            </Typography>
          </AccordionDetails>
        </Accordion>
      ))}
    </Stack>
  );
}
