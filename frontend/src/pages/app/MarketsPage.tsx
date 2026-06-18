import { Box, Grid, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { MarketCard } from '../../components/markets/MarketCard';
import { MarketFilters } from '../../components/markets/MarketFilters';
import { MarketTable } from '../../components/markets/MarketTable';
import { SportToggleRail } from '../../components/navigation/SportToggleRail';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { useMarketScannerData } from '../../hooks/useMarketData';
import { useSelectedSports } from '../../hooks/useSelectedSports';
import { useResponsiveView } from '../../hooks/useResponsiveView';
import { formatPercent } from '../../utils/formatters';

export function MarketsPage() {
  const sports = useSelectedSports();
  const { isMobile } = useResponsiveView();
  const [candidateOnly, setCandidateOnly] = useState(false);
  const [dfsActionableOnly, setDfsActionableOnly] = useState(true);
  const marketsQuery = useMarketScannerData(75, 'market');
  const scannerMarkets = marketsQuery.markets.filter((market) => sports.includesSport(market.sport));
  const dfsMarkets = scannerMarkets.filter((market) => market.providerCategory === 'DFS');
  const markets = scannerMarkets.filter((market) => {
    if (dfsActionableOnly && market.providerCategory !== 'DFS') return false;
    if (candidateOnly && market.strategyClassification !== 'Candidate') return false;
    return true;
  });
  const bestEv = dfsMarkets.length ? Math.max(...dfsMarkets.map((market) => market.estimatedEv)) : 0;
  const dfsProviderCount = new Set(dfsMarkets.map((market) => market.provider)).size;
  const lowSampleRows = scannerMarkets.filter((market) => market.historicalSampleSize < 35).length;
  return (
    <Stack spacing={3} sx={{ px: { xs: 0.75, sm: 0 } }}>
      <PageHeader title="Markets" description="Scan available DFS rows, provider lines, model edge, and entry compatibility." />
      <SportToggleRail selectedSports={sports.selectedSports} onToggle={sports.toggleSport} />

      {isMobile ? (
        <MobileSummaryStrip
          marketCount={scannerMarkets.length}
          dfsProviderCount={dfsProviderCount}
          bestEv={bestEv}
          lowSampleRows={lowSampleRows}
        />
      ) : (
        <Grid container spacing={1.5}>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Active markets" value={String(scannerMarkets.length)} helper="Rows in selected sports" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="DFS providers" value={String(dfsProviderCount)} helper="Entry-capable sources" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Best EV" value={formatPercent(bestEv, { signed: true })} helper="Top DFS row" tone="success" />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
            <SummaryMetric label="Low-sample rows" value={String(lowSampleRows)} helper="Use extra caution" tone="warning" />
          </Grid>
        </Grid>
      )}

      <MarketFilters
        useToggleButtons
        scannerFields
        compactMobile
        classificationLabel="Confidence"
        candidateOnly={candidateOnly}
        dfsActionableOnly={dfsActionableOnly}
        onCandidateOnlyChange={setCandidateOnly}
        onDfsActionableOnlyChange={setDfsActionableOnly}
      />

      <SectionHeader title="Market scanner" description="Inventory view for available rows. Use Details for research rows, low-quality rows, and reference-only lines." />
      {marketsQuery.loading ? (
        <ApiStateCard message="Loading market scanner rows..." />
      ) : marketsQuery.error ? (
        <ApiStateCard tone="error" message="Market scanner rows could not be loaded from the market API." />
      ) : isMobile ? (
        <Grid container spacing={2}>{markets.map((market) => <Grid key={market.id} size={12}><MarketCard market={market} /></Grid>)}</Grid>
      ) : markets.length ? <MarketTable markets={markets} /> : <ApiStateCard message="No market rows match the current filters." />}
    </Stack>
  );
}

function ApiStateCard({ message, tone = 'default' }: { message: string; tone?: 'default' | 'error' }) {
  return (
    <Box
      sx={{
        border: `1px solid ${tone === 'error' ? 'rgba(244,63,94,.32)' : 'rgba(148,163,184,.16)'}`,
        borderRadius: 4,
        bgcolor: 'rgba(15,23,42,.68)',
        p: 2
      }}
    >
      <Typography color={tone === 'error' ? 'error.main' : 'text.secondary'}>{message}</Typography>
    </Box>
  );
}

function MobileSummaryStrip({
  marketCount,
  dfsProviderCount,
  bestEv,
  lowSampleRows
}: {
  marketCount: number;
  dfsProviderCount: number;
  bestEv: number;
  lowSampleRows: number;
}) {
  const items = [
    { value: String(marketCount), label: 'Markets' },
    { value: String(dfsProviderCount), label: 'DFS providers' },
    { value: formatPercent(bestEv, { signed: true }), label: 'Best EV', tone: 'success' },
    { value: String(lowSampleRows), label: 'Low sample', tone: 'warning' }
  ];

  return (
    <Box
      sx={{
        border: '1px solid rgba(148,163,184,.16)',
        borderRadius: 3,
        bgcolor: 'rgba(15,23,42,.62)',
        px: 1.2,
        py: 1
      }}
    >
      <Grid container spacing={0.8}>
        {items.map((item) => (
          <Grid key={item.label} size={6}>
            <Stack spacing={0.15} alignItems="center" textAlign="center">
              <Typography variant="body1" color={item.tone === 'success' ? 'success.main' : item.tone === 'warning' ? 'warning.main' : 'text.primary'} fontWeight={950}>
                {item.value}
              </Typography>
              <Typography variant="caption" color="text.secondary" fontWeight={800}>
                {item.label}
              </Typography>
            </Stack>
          </Grid>
        ))}
      </Grid>
    </Box>
  );
}

function SummaryMetric({
  label,
  value,
  helper,
  tone = 'default'
}: {
  label: string;
  value: string;
  helper: string;
  tone?: 'default' | 'success' | 'warning';
}) {
  return (
    <Box
      sx={{
        height: '100%',
        border: '1px solid rgba(148,163,184,.16)',
        borderRadius: 3,
        bgcolor: 'rgba(15,23,42,.72)',
        p: 1.7
      }}
    >
      <Stack spacing={0.35}>
        <Typography variant="caption" color="text.secondary" fontWeight={850} textTransform="uppercase">
          {label}
        </Typography>
        <Typography variant="h5" color={tone === 'success' ? 'success.main' : tone === 'warning' ? 'warning.main' : 'text.primary'} fontWeight={950}>
          {value}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {helper}
        </Typography>
      </Stack>
    </Box>
  );
}
