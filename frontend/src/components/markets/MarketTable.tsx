import { Box, Button, Table, TableBody, TableCell, TableHead, TableRow } from '@mui/material';
import type { MarketOpportunity } from '../../types/markets';
import { formatAmericanOdds, formatPercent } from '../../utils/formatters';
import { StatusPill } from '../ui/StatusPill';
import { ProjectionModifierBadge } from './ProjectionModifierBadge';
import { ProviderLabel } from '../providers/ProviderLabel';

export function MarketTable({ markets }: { markets: MarketOpportunity[] }) {
  return (
    <Box sx={{ overflowX: 'auto', border: '1px solid rgba(148,163,184,.16)', borderRadius: 3 }}>
      <Table size="small" stickyHeader>
        <TableHead>
          <TableRow>
            <TableCell>Player</TableCell>
            <TableCell>Sport</TableCell>
            <TableCell>Prop</TableCell>
            <TableCell>Provider</TableCell>
            <TableCell align="right">EV</TableCell>
            <TableCell align="right">Model</TableCell>
            <TableCell align="right">Edge</TableCell>
            <TableCell>Strategy</TableCell>
            <TableCell>Action</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {markets.map((market) => (
            <TableRow key={market.id} hover>
              <TableCell>
                <Box fontWeight={900}>{market.player}</Box>
                <Box color="text.secondary" fontSize={12}>{market.event}</Box>
              </TableCell>
              <TableCell>{market.sport}</TableCell>
              <TableCell>
                {market.side} {market.propType} {market.line}
                <Box mt={0.5}><ProjectionModifierBadge modifier={market.projectionModifier} /></Box>
              </TableCell>
              <TableCell>
                <ProviderLabel provider={market.provider} category={market.providerCategory} compact />
                <Box mt={0.5} color="text.secondary" fontSize={12}>{formatAmericanOdds(market.bestAmericanPrice)}</Box>
              </TableCell>
              <TableCell align="right">{formatPercent(market.estimatedEv, { signed: true })}</TableCell>
              <TableCell align="right">{formatPercent(market.modelProbability)}</TableCell>
              <TableCell align="right">{formatPercent(market.probabilityEdge, { signed: true })}</TableCell>
              <TableCell><StatusPill label={market.strategyClassification} /></TableCell>
              <TableCell><Button size="small" variant="outlined" disabled={!market.canAddToEntry}>Add</Button></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}
