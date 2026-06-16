import { Accordion, AccordionDetails, AccordionSummary, Box, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import type { MarketOpportunity } from '../../types/markets';
import { formatPercent } from '../../utils/formatters';
import { StatusPill } from '../ui/StatusPill';

export function LineShoppingTable({ markets }: { markets: MarketOpportunity[] }) {
  return (
    <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 3 }}>
      <Table size="small" stickyHeader>
        <TableHead>
          <TableRow>
            <TableCell>Market</TableCell>
            <TableCell>Best DFS</TableCell>
            <TableCell>Entry Group</TableCell>
            <TableCell>Sportsbook Context</TableCell>
            <TableCell align="right">EV</TableCell>
            <TableCell>Warnings</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {markets.map((market) => (
            <TableRow key={market.id}>
              <TableCell>
                <Typography fontWeight={900}>{market.player}</Typography>
                <Typography variant="caption" color="text.secondary">{market.side} {market.propType} {market.line}</Typography>
              </TableCell>
              <TableCell><StatusPill label={market.provider} /></TableCell>
              <TableCell>{market.entryCompatibilityGroup}</TableCell>
              <TableCell>{market.sportsbookContext ?? 'No context row'}</TableCell>
              <TableCell align="right">{formatPercent(market.estimatedEv, { signed: true })}</TableCell>
              <TableCell>{market.warnings.join(', ') || 'None'}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}

export function LineShoppingCard({ market }: { market: MarketOpportunity }) {
  return (
    <Accordion>
      <AccordionSummary expandIcon={<ExpandMoreRoundedIcon />}>
        <Box>
          <Typography fontWeight={900}>{market.player}</Typography>
          <Typography variant="body2" color="text.secondary">
            {market.side} {market.propType} {market.line} · {market.provider}
          </Typography>
        </Box>
      </AccordionSummary>
      <AccordionDetails>
        {market.providerLines?.map((line) => (
          <Box key={`${market.id}-${line.provider}`} display="flex" justifyContent="space-between" py={0.8}>
            <Typography>{line.provider}</Typography>
            <Typography color="text.secondary">Line {line.line}</Typography>
          </Box>
        )) ?? <Typography color="text.secondary">No provider-line details available.</Typography>}
      </AccordionDetails>
    </Accordion>
  );
}
