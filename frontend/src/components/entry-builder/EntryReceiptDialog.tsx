import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogContent,
  DialogTitle,
  IconButton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography
} from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import { palette } from '../../theme/palette';
import type { EntryLeg } from '../../types/entryBuilder';
import type { buildEntryReview } from '../../utils/entryReview';
import { formatPercent } from '../../utils/formatters';
import { TrustValue } from '../metrics/TrustValue';
import { ProviderLabel } from '../providers/ProviderLabel';
import { AppCard } from '../ui/AppCard';
import { OpportunityPickText } from '../ui/OpportunityCard';

interface EntryReceiptDialogProps {
  open: boolean;
  onClose: () => void;
  legs: EntryLeg[];
  review: ReturnType<typeof buildEntryReview>;
  amount: number;
}

export function EntryReceiptDialog({ open, onClose, legs, review, amount }: EntryReceiptDialogProps) {
  const [reviewedAt, setReviewedAt] = useState<Date | null>(null);
  const receiptStats = useMemo(() => buildReceiptStats(legs), [legs]);
  const ready = review.status === 'Ready';
  const statusLabel = getReceiptStatusLabel(review);
  const disclaimerProvider = review.provider && review.provider !== 'No provider selected' ? review.provider : 'the provider';
  const warnings = buildReceiptWarnings(review);
  const trackingHelperText = ready ? 'Entry Tracker coming later.' : 'Fix blockers before tracking.';
  const legNotes = useMemo(() => legs.map((leg) => getLegNote(leg)), [legs]);
  const showNotesColumn = legNotes.some(Boolean);

  useEffect(() => {
    if (open) {
      setReviewedAt(new Date());
    }
  }, [open]);

  return (
    <Dialog
      open={open}
      onClose={onClose}
      fullWidth
      maxWidth="lg"
      aria-labelledby="entry-receipt-title"
      PaperProps={{
        sx: {
          borderRadius: 2.5,
          border: `1px solid ${palette.border}`,
          bgcolor: 'rgba(8,13,25,.98)',
          backgroundImage: 'none',
          m: { xs: '10px', sm: 4 },
          width: { xs: 'calc(100vw - 20px)', sm: 'auto' },
          maxWidth: { xs: 'calc(100vw - 20px)', sm: 1100 },
          maxHeight: { xs: 'calc(100vh - 24px)', sm: 'calc(100vh - 64px)' },
          boxShadow: 'none'
        }
      }}
    >
      <DialogTitle id="entry-receipt-title" sx={{ pr: { xs: 6, sm: 7 }, pb: 1, pt: { xs: 2, sm: 2.5 } }}>
        <Typography component="span" variant="h4" sx={{ display: 'block', fontSize: { xs: 28, sm: 34 }, lineHeight: 1.05 }}>
          Track Entry
        </Typography>
        <IconButton
          aria-label="Close entry receipt"
          onClick={onClose}
          sx={{
            position: 'absolute',
            right: { xs: 10, sm: 14 },
            top: { xs: 10, sm: 14 },
            color: 'text.secondary',
            '&:hover': { color: 'text.primary', bgcolor: 'rgba(148,163,184,.10)' }
          }}
        >
          <CloseRoundedIcon />
        </IconButton>
      </DialogTitle>

      <DialogContent className="app-scrollbar" sx={{ pt: 0.5, pb: { xs: 2.25, sm: 3 }, px: { xs: 1.25, sm: 3 } }}>
        <Stack spacing={1.5}>
          <Alert
            severity="info"
            variant="outlined"
            sx={{
              borderColor: 'rgba(56,189,248,.28)',
              color: 'text.primary',
              bgcolor: 'rgba(15,23,42,.42)'
            }}
          >
            This does not place an entry with {disclaimerProvider}. Build and submit it manually in the provider app.
          </Alert>

          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' },
              gap: 1
            }}
          >
            <ReceiptMetric label="Provider" value={review.provider} />
            <ReceiptMetric label="Reviewed" value={reviewedAt ? formatReceiptTime(reviewedAt) : '-'} />
            <ReceiptMetric label="Entry type" value={review.selectedEntryType || 'Unavailable'} />
            <ReceiptMetric label="Tracking status" value="Preview only" />
            <ReceiptMetric label="Leg count" value={String(review.legCount)} />
            <ReceiptMetric label="Entry amount" value={formatMoney(amount)} />
            <ReceiptMetric label="Est. multiplier" value={review.multiplier === null ? 'Unavailable' : `${review.multiplier.toFixed(2)}x`} />
            <ReceiptMetric label="Est. payout" value={review.estimatedPayout === null ? 'Unavailable' : formatMoney(review.estimatedPayout)} />
            <ReceiptMetric label="Avg EV" value={receiptStats.averageEv === null ? 'Unavailable' : formatPercent(receiptStats.averageEv, { signed: true })} />
            <ReceiptMetric label="Estimated all-hit chance" value={receiptStats.allHitChance === null ? 'Unavailable' : formatPercent(receiptStats.allHitChance)} />
            <ReceiptMetric
              label="Status"
              value={statusLabel}
              tone={ready ? 'success.main' : 'warning.main'}
              sx={{ gridColumn: { xs: '1 / -1', md: 'span 2' } }}
            />
          </Box>

          <Typography variant="body2" color="text.secondary">
            Assumes legs are independent. Correlation, same-game picks, lineup changes, and provider rules can change real outcomes.
          </Typography>

          <AppCard contentSx={{ p: { xs: 1.15, md: 1.35 }, '&:last-child': { pb: { xs: 1.15, md: 1.35 } } }}>
            <Stack spacing={1}>
              <Typography fontWeight={950}>Entry legs</Typography>
              <Box sx={{ display: { xs: 'none', md: 'block' }, overflowX: 'auto' }}>
                <Table size="small" sx={{ minWidth: 920 }}>
                  <TableHead>
                    <TableRow>
                      <TableCell>#</TableCell>
                      <TableCell>Player</TableCell>
                      <TableCell>Pick</TableCell>
                      <TableCell>Prop</TableCell>
                      <TableCell>Game</TableCell>
                      <TableCell align="right">EV</TableCell>
                      <TableCell align="right">Hit Chance</TableCell>
                      <TableCell>Trust</TableCell>
                      <TableCell align="right">Sample</TableCell>
                      {showNotesColumn ? <TableCell>Notes</TableCell> : null}
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {legs.map((leg, index) => (
                      <TableRow key={leg.id}>
                        <TableCell>{index + 1}</TableCell>
                        <TableCell>
                          <Stack spacing={0.25}>
                            <Typography fontWeight={900}>{leg.player}</Typography>
                            <ProviderLabel provider={leg.entryProvider} category={leg.providerCategory} compact />
                          </Stack>
                        </TableCell>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatPick(leg)}</TableCell>
                        <TableCell>{leg.propType}</TableCell>
                        <TableCell>
                          <Typography variant="body2" color="text.secondary">
                            {leg.event} - {leg.startTime}
                          </Typography>
                        </TableCell>
                        <TableCell align="right">{formatPercent(leg.estimatedEv, { signed: true })}</TableCell>
                        <TableCell align="right">{formatPercent(leg.modelProbability)}</TableCell>
                        <TableCell><TrustValue level={leg.trustLevel} /></TableCell>
                        <TableCell align="right">{leg.historicalSampleSize}</TableCell>
                        {showNotesColumn ? (
                          <TableCell>
                            <Typography variant="body2" color="text.secondary">
                              {legNotes[index]}
                            </Typography>
                          </TableCell>
                        ) : null}
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Box>

              <Stack spacing={0.85} sx={{ display: { xs: 'flex', md: 'none' } }}>
                {legs.map((leg, index) => (
                  <Stack
                    key={leg.id}
                    spacing={0.75}
                    sx={{
                      border: `1px solid ${palette.border}`,
                      borderRadius: 2,
                      p: 1,
                      bgcolor: 'rgba(15,23,42,.34)'
                    }}
                  >
                    <Stack direction="row" justifyContent="space-between" gap={1}>
                      <Box>
                        <Typography variant="caption" color="text.secondary" fontWeight={900}>
                          #{index + 1}
                        </Typography>
                        <Typography fontWeight={950}>{leg.player}</Typography>
                      </Box>
                      <TrustValue level={leg.trustLevel} />
                    </Stack>
                    <OpportunityPickText side={leg.side} propType={leg.propType} line={leg.line} />
                    <Typography variant="body2" color="text.secondary">
                      {leg.event} - {leg.startTime}
                    </Typography>
                    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 0.75 }}>
                      <ReceiptMiniMetric label="EV" value={formatPercent(leg.estimatedEv, { signed: true })} />
                      <ReceiptMiniMetric label="Hit Chance" value={formatPercent(leg.modelProbability)} />
                      <ReceiptMiniMetric label="Sample" value={String(leg.historicalSampleSize)} />
                    </Box>
                    {legNotes[index] ? (
                      <Typography variant="body2" color="text.secondary">
                        {legNotes[index]}
                      </Typography>
                    ) : null}
                  </Stack>
                ))}
              </Stack>
            </Stack>
          </AppCard>

          <ReceiptList title="Blockers" emptyText={ready ? 'No blocking issues.' : 'No additional blockers.'} items={review.blockers} tone="warning" />
          <ReceiptList title="Warnings" emptyText="No additional warnings." items={warnings} tone="muted" />

          <Stack
            direction={{ xs: 'column', sm: 'row' }}
            justifyContent="flex-end"
            alignItems={{ xs: 'stretch', sm: 'flex-end' }}
            gap={1}
            sx={{
              borderTop: `1px solid ${palette.border}`,
              pt: 1.25
            }}
          >
            <Stack spacing={0.65} alignItems={{ xs: 'stretch', sm: 'flex-end' }}>
              <Button variant="contained" disabled>
                Track this entry
              </Button>
              <Typography variant="body2" color={ready ? 'text.secondary' : 'warning.main'}>
                {trackingHelperText}
              </Typography>
            </Stack>
          </Stack>
        </Stack>
      </DialogContent>
    </Dialog>
  );
}

function ReceiptMetric({ label, value, tone = 'text.primary', sx }: { label: string; value: string; tone?: string; sx?: object }) {
  return (
    <Stack
      spacing={0.25}
      sx={{
        minWidth: 0,
        border: `1px solid ${palette.border}`,
        borderRadius: 2,
        p: 1,
        bgcolor: palette.cardSurface,
        ...sx
      }}
    >
      <Typography variant="caption" color="text.secondary" fontWeight={900}>
        {label}
      </Typography>
      <Typography fontWeight={950} color={tone} sx={{ overflowWrap: 'anywhere' }}>
        {value}
      </Typography>
    </Stack>
  );
}

function ReceiptMiniMetric({ label, value }: { label: string; value: string }) {
  return (
    <Stack spacing={0.15} minWidth={0}>
      <Typography variant="caption" color="text.secondary" fontWeight={900}>
        {label}
      </Typography>
      <Typography variant="body2" fontWeight={900}>
        {value}
      </Typography>
    </Stack>
  );
}

function ReceiptList({ title, items, emptyText, tone }: { title: string; items: string[]; emptyText: string; tone: 'warning' | 'muted' }) {
  const color = tone === 'warning' ? 'warning.main' : 'text.secondary';
  return (
    <Stack spacing={0.55}>
      <Stack direction="row" alignItems="center" gap={0.75}>
        <Typography variant="caption" color="text.secondary" fontWeight={900}>
          {title}
        </Typography>
        {items.length ? <Chip size="small" label={items.length} sx={{ height: 20, fontWeight: 900 }} /> : null}
      </Stack>
      {items.length ? (
        <Stack component="ul" spacing={0.4} sx={{ m: 0, pl: 2 }}>
          {items.map((item) => (
            <Typography key={item} component="li" variant="body2" color={color}>
              {item}
            </Typography>
          ))}
        </Stack>
      ) : (
        <Typography variant="body2" color="text.secondary">
          {emptyText}
        </Typography>
      )}
    </Stack>
  );
}

function buildReceiptStats(legs: EntryLeg[]) {
  if (!legs.length) {
    return { averageEv: null, allHitChance: null };
  }

  const evValues = legs.map((leg) => leg.estimatedEv).filter((value) => Number.isFinite(value));
  const probabilities = legs.map((leg) => leg.modelProbability).filter((value) => Number.isFinite(value) && value > 0);

  return {
    averageEv: evValues.length === legs.length ? evValues.reduce((sum, value) => sum + value, 0) / legs.length : null,
    allHitChance: probabilities.length === legs.length ? probabilities.reduce((product, value) => product * value, 1) : null
  };
}

function buildReceiptWarnings(review: ReturnType<typeof buildEntryReview>) {
  const provider = review.provider.toLowerCase();
  const isPrizePicks = provider.includes('prizepicks') || provider.includes('prize picks');
  const hasExactPayout = review.multiplier !== null;
  const warnings = review.warnings.filter(
    (warning) => !(isPrizePicks && hasExactPayout && warning === 'Provider rules need final confirmation in the DFS app.')
  );

  if (hasExactPayout) {
    if (isPrizePicks) {
      warnings.push('Confirm final lines and payout in PrizePicks before submitting.');
    } else {
      warnings.push(`Confirm final lines and payout in ${review.provider} before tracking.`);
    }
  }

  return Array.from(new Set(warnings));
}

function getReceiptStatusLabel(review: ReturnType<typeof buildEntryReview>) {
  if (review.status === 'Ready') return 'Ready to build manually';
  if (review.status === 'Needs more legs' && review.blockers.some((item) => item.includes('Provider rules are unavailable'))) return 'Rules unavailable';
  return review.status;
}

function getLegNote(leg: EntryLeg) {
  if (leg.specialProjectionWarning) return leg.specialProjectionWarning;
  if (leg.payoutMultiplierMayDiffer) return 'Special or alternate projection can change payout.';
  if (leg.projectionModifier && leg.projectionModifier !== 'Normal') return leg.projectionModifier;
  if (leg.freshness === 'Stale') return 'Stale line';
  if (leg.freshness === 'Unavailable') return 'Line unavailable';
  if (leg.historicalSampleSize > 0 && leg.historicalSampleSize < 20) return 'Low sample';
  return '';
}

function formatPick(leg: EntryLeg) {
  return `${leg.side} ${formatLine(leg.line)}`;
}

function formatLine(line: number) {
  return Number.isInteger(line) ? String(line) : line.toFixed(1);
}

function formatMoney(value: number) {
  return `$${value.toFixed(2)}`;
}

function formatReceiptTime(value: Date) {
  return value.toLocaleString([], {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  });
}
